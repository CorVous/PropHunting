using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

// One player's slot, owned by that player (PHGame assigns it). It carries the player's synced round state and
// their disguise, and holds their station for hiding:
//   station parking (PropHunt Station Lab): a hider sits in their own Immobilize station for the whole round. On
//   the hider's client the station is Mobile and follows them, so VRChat's own walking, jumping and animation
//   work. Every other client parks its copy of the station far below the map, so the hider's avatar (and voice)
//   are drawn there, and shows the synced prop where the hider really is.
//   The prop (proxy) is an instance of the def's prefab; its colliders are what darts hit (not on the owner's
//   client, where they would block the hider's own movement).
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PHSlot : UdonSharpBehaviour
{
    public PHGame game;
    public int index;
    public VRCStation station;
    public Transform proxyRoot;
    public const int HIDER_SELF = 23;   // a user layer: the reference camera leaves it out, the third-person camera draws it
    public GameObject beacon;          // the reveal's marker: green survived, red caught
    public Renderer beaconRenderer;
    public Vector3 parkAt = new Vector3(0f, -400f, 0f);
    public AudioSource voice;          // taunts play here, at the prop

    // ---- synced, written by the owner
    [UdonSynced] public bool inZone;
    [UdonSynced] public int readyRound = -1;
    [UdonSynced] public int def = -1;                 // disguise, -1 = none
    [UdonSynced] public int lastDef = -1;             // the disguise worn last (for the reveal, after a catch)
    [UdonSynced] public Vector3 propPos;
    [UdonSynced] public float propYaw;
    [UdonSynced] public bool hiding;                  // seated and hidden this round
    // a ghost: a caught hider, an eliminated hunter or a mid-round joiner. Seated (Mobile on their own client, so
    // they walk as their own avatar) and seen only by the other ghosts: every other client parks the station
    [UdonSynced] public bool ghost;
    [UdonSynced] public int scrapVote, undoVote;      // PHGame.voteId when this player votes to scrap the last map / undo a scrap
    [UdonSynced] public Vector3 walkPos;
    [UdonSynced] public float walkYaw;
    [UdonSynced] public int health, maxHealth;
    [UdonSynced] public bool caught;
    [UdonSynced] public int survivedMs;
    [UdonSynced] public int hunterHealth = 100;
    [UdonSynced] public bool hunterOut;
    [UdonSynced] public int wrongProps, hits, catches;
    [UdonSynced] public int round = -1;               // the round these figures belong to

    [HideInInspector] public bool seated;             // owner: in the station now
    GameObject proxy;
    int proxyDef = -2;
    float nextSend, nextSit;
    Vector3 shownPos;
    float shownYaw;
    bool dirty;

    public bool IsMine { get { return Networking.IsOwner(gameObject) && game.SlotOf(Networking.LocalPlayer.playerId) == index; } }

    // ------------------------------------------------------------------ owner
    public void Claim()
    {
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
    }

    public void Send() { dirty = true; }

    public void ReportReady(int r)
    {
        if (!IsMine) return;
        readyRound = r;
        Send();
    }

    // a new round: fresh figures
    public void ResetFor(int r, int role)
    {
        round = r;
        def = -1; lastDef = -1; hiding = false; ghost = false; caught = false; survivedMs = 0;
        health = 0; maxHealth = 0;
        hunterHealth = 100; hunterOut = false; wrongProps = 0; hits = 0; catches = 0;
        Send();
    }

    public void StartHiding()
    {
        hiding = true;
        // Immobilize on the hider's client too: PHHiderBody moves the station, and the seated player with it
        station.PlayerMobility = VRCStation.Mobility.Immobilize;
        nextSit = 0f;
        Send();
    }

    // become a ghost: out of the prop's station mode and back on VRChat's own feet (Mobile on this client)
    public void StartGhost()
    {
        hiding = false;
        ghost = true;
        if (seated) station.ExitStation(Networking.LocalPlayer);
        seated = false;
        station.PlayerMobility = VRCStation.Mobility.Mobile;
        nextSit = Time.time + 0.3f;
        VRCPlayerApi me = Networking.LocalPlayer;
        walkPos = me.GetPosition(); walkYaw = me.GetRotation().eulerAngles.y;
        Send();
    }

    public void StopHiding()
    {
        hiding = false;
        ghost = false;
        station.PlayerMobility = VRCStation.Mobility.Immobilize;
        if (seated) station.ExitStation(Networking.LocalPlayer);
        seated = false;
        Send();
    }

    // leave and re-enter, so players who joined since get the enter event (Station Lab: late joiners)
    public void Reseat()
    {
        if (!seated) return;
        station.ExitStation(Networking.LocalPlayer);
        seated = false;
        nextSit = Time.time + 0.3f;
    }

    // a dart from a hunter (sent to the owner). Returns nothing: the owner's synced figures say what happened.
    [NetworkCallable]
    public void TakeHit(int damage, int shooterPlayerId)
    {
        if (!IsMine || caught || def < 0) return;
        health -= damage;
        if (health <= 0)
        {
            health = 0;
            caught = true;
            survivedMs = game.Now - game.huntStartMs;
        }
        Send();
        if (game.local != null) game.local.OnHit(damage, caught, shooterPlayerId);
    }

    // a taunt (6.3), heard by everyone from the prop
    [NetworkCallable]
    public void PlayTaunt(int pick)
    {
        if (voice == null || game.local == null || game.local.sound == null) return;
        PHAudio a = game.local.sound;
        AudioClip c = a.Clip(a.Pick(a.taunts, pick));
        if (c == null) return;
        voice.transform.position = proxy != null ? proxy.transform.position : propPos;
        voice.PlayOneShot(c, 1f);
    }

    bool emptyDone;

    // profiling (PHProfiler): while it runs, the time this script's updates take adds up in profMs
    [System.NonSerialized] public System.Diagnostics.Stopwatch profWatch;
    [System.NonSerialized] public double profMs;

    void Update()
    {
        if (profWatch == null) { UpdateBody(); return; }
        double p0 = profWatch.Elapsed.TotalMilliseconds;
        UpdateBody();
        profMs += profWatch.Elapsed.TotalMilliseconds - p0;
    }

    void UpdateBody()
    {
        if (game == null) return;
        // an empty slot (most of the 32): parked once, then nothing each frame (Cor: profiling, the slots were half the Udon time)
        if (game.slotPlayer[index] == 0)
        {
            if (emptyDone && proxy == null) return;
            UpdateRemote();
            UpdateProxy(false);
            emptyDone = true;
            return;
        }
        emptyDone = false;
        int myId = Networking.LocalPlayer.playerId;
        // the player the host gave this slot takes it over (the host owns every slot until then)
        if (game.slotPlayer[index] == myId && myId != 0 && !Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        bool mine = Networking.IsOwner(gameObject) && game.slotPlayer[index] == myId && myId != 0;
        if (mine) UpdateOwner(); else UpdateRemote();
        UpdateProxy(mine);
        if (mine && dirty && Time.time >= nextSend)
        {
            dirty = false;
            nextSend = Time.time + 0.12f;
            RequestSerialization();
        }
    }

    void UpdateOwner()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        bool z = game.InLobbyZone(me.GetPosition());
        if (z != inZone) { inZone = z; Send(); }
        PHHiderBody body = game.local != null ? game.local.body : null;
        if (hiding && body != null && body.active)
        {
            // the hider's body moves the station (and the seated player with it)
            // (after a correction the seat stays where it was until the prop makes its way back: PHHiderBody.lag)
            station.transform.SetPositionAndRotation(body.pos + body.lag, Quaternion.Euler(0f, body.stationYaw, 0f));
            if ((body.pos - propPos).sqrMagnitude > 0.0004f) { propPos = body.pos; Send(); }
            if (Mathf.Abs(Mathf.DeltaAngle(body.yaw, propYaw)) > 1f) { propYaw = body.yaw; Send(); }
        }
        // otherwise the station follows the player, so sitting down leaves them where they are
        else station.transform.SetPositionAndRotation(me.GetPosition(), me.GetRotation());
        if (ghost)
        {
            Vector3 p = me.GetPosition();
            float y = me.GetRotation().eulerAngles.y;
            if ((p - walkPos).sqrMagnitude > 0.0025f || Mathf.Abs(Mathf.DeltaAngle(y, walkYaw)) > 5f) { walkPos = p; walkYaw = y; Send(); }
        }
        if ((hiding || ghost) && !seated && Time.time >= nextSit)
        {
            nextSit = Time.time + 3f;
            station.UseStation(me);
        }
    }

    void UpdateRemote()
    {
        // everyone else's copy of a hiding player's station is parked out of sight
        station.PlayerMobility = VRCStation.Mobility.Immobilize;
        Vector3 park = parkAt + new Vector3(index * 3f, 0f, 0f);
        if (hiding) station.transform.SetPositionAndRotation(park, Quaternion.identity);
        else if (ghost)
        {
            // ghosts see ghosts; nobody else does (Cor: the dead see only the dead and the hunters)
            if (game.local != null && game.local.IsGhost())
            {
                Vector3 at = station.transform.position;
                if ((at - walkPos).sqrMagnitude > 25f) at = walkPos;
                float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
                station.transform.SetPositionAndRotation(Vector3.Lerp(at, walkPos, k), Quaternion.Euler(0f, walkYaw, 0f));
            }
            else station.transform.SetPositionAndRotation(park, Quaternion.identity);
        }
    }

    // ------------------------------------------------------------------ the prop
    void UpdateProxy(bool mine)
    {
        int want = (hiding && def >= 0 && game.slotPlayer[index] != 0) ? def : -1;
        if (want != proxyDef)
        {
            proxyDef = want;
            if (proxy != null) Destroy(proxy);
            proxy = null;
            if (want >= 0 && game.builder.propPrefabs != null && want < game.builder.propPrefabs.Length && game.builder.propPrefabs[want] != null)
            {
                proxy = Instantiate(game.builder.propPrefabs[want]);
                game.builder.TintProp(proxy, want);   // the map's foliage colour, as the props it hides among
                proxy.transform.SetParent(proxyRoot, true);
                proxy.transform.position = propPos;
                proxy.name = "Hider" + index;
                shownPos = propPos; shownYaw = propYaw;
                // the squash through a doorway starts from the prefab's own scale
                proxyScale = proxy.transform.localScale;
                squashX = 1f; squashZ = 1f; squashVX = 0f; squashVZ = 0f; wantX = 1f; wantZ = 1f; squashNext = 0f;
                proxyHalf = new Vector3(game.gen.defSx[want], game.gen.defSy[want], game.gen.defSz[want]) / 2000f;
                // its shade patch (the prefab's last child), shown only while it stands on a floor (ShadeCheck)
                proxyShade = null; shadeNext = 0f;
                Transform pt = proxy.transform;
                if (pt.childCount > 0)
                {
                    Transform last = pt.GetChild(pt.childCount - 1);
                    if (last.name == "Shade") proxyShade = last.gameObject;
                }
                // the owner's own prop must not block the owner; for everyone else its colliders are the target
                Collider[] cols = proxy.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++) cols[i].enabled = !mine;
                // the hider never sees their own prop in first person (it would fill the view): it goes on the
                // HIDER_SELF layer, which the reference camera leaves out and only the third-person camera draws
                if (mine)
                {
                    Transform[] parts = proxy.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < parts.Length; i++) parts[i].gameObject.layer = HIDER_SELF;
                    game.builder.SelfOnly(proxy, want);   // ...and the shader keeps it out of any other camera too
                }
            }
        }
        if (proxy == null) return;
        float k = mine ? 1f : 1f - Mathf.Exp(-14f * Time.deltaTime);
        if ((propPos - shownPos).sqrMagnitude > 25f) k = 1f;
        shownPos = Vector3.Lerp(shownPos, propPos, k);
        shownYaw = Mathf.LerpAngle(shownYaw, propYaw, k);
        proxy.transform.SetPositionAndRotation(shownPos, Quaternion.Euler(0f, shownYaw, 0f));
        if (proxyShade != null && Time.time >= shadeNext) { shadeNext = Time.time + 0.15f; ShadeCheck(); }
        Squash();
    }

    // A big prop squeezes through a doorway (PHHiderBody moves it as a narrower box there). What everyone sees (Cor:
    // a bouncy squeeze, only visually): where the walls to its sides are closer than it is wide, the prop is squashed
    // across to fit between them and stretched the other way, on a spring that overshoots a little; clear of the
    // walls it springs back. Measured on each client against the architecture's collider: nothing is synced.
    Vector3 proxyScale = Vector3.one, proxyHalf;
    float squashX = 1f, squashZ = 1f, squashVX, squashVZ, wantX = 1f, wantZ = 1f, squashNext;

    // how far the architecture is from the prop's middle along dir, at most reach
    float WallAt(Vector3 from, Vector3 dir, float reach)
    {
        Collider arch = game.builder.collisionCollider;
        if (arch == null) return reach;
        RaycastHit r;
        if (arch.Raycast(new Ray(from, dir), out r, reach)) return r.distance;
        return reach;
    }

    // The room there is for the prop across (along 'across', its half width there 'half'), the least of: at its
    // middle, and past its front and its back by about its own half width and a little (along 'along', its half length 'reach'), so it is
    // already squashed when its front comes to a doorway (Cor: or it starts clipping). An end is only measured
    // where nothing stands between the middle and it (a wall beside the prop is not a doorway ahead of it).
    float GapAcross(Vector3 mid, Vector3 across, Vector3 along, float half, float reach)
    {
        float gap = WallAt(mid, across, half) + WallAt(mid, -across, half);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 dir = along * side;
            // how far ahead it looks is the prop's own (Cor: per object, not one distance): the wider it is across,
            // the more it has to give and the sooner it starts. Places 0.2 m apart (a wall is 0.2 m thick: one of
            // them is in the doorway when it is near)
            int looks = Mathf.Clamp(Mathf.CeilToInt((0.1f + 1.1f * half) / 0.2f), 1, 8);
            for (int k = 0; k < looks; k++)
            {
                float ahead = reach + 0.1f + 0.2f * k;
                if (WallAt(mid, dir, ahead) < ahead - 0.001f) break;
                Vector3 at = mid + dir * ahead;
                // only a gap counts there: a wall on both sides. (A wall on one side of a place beside the prop is the
                // wall the doorway is in: the prop squashed along its way instead of across it, Cor)
                float ga = WallAt(at, across, half), gb = WallAt(at, -across, half);
                if (ga < half - 0.001f && gb < half - 0.001f && ga + gb < gap) gap = ga + gb;
            }
        }
        return gap;
    }

    void Squash()
    {
        if (proxyHalf.x <= 0.3f && proxyHalf.z <= 0.3f) return;   // it never squeezes (PHHiderBody.SQUEEZE_HALF)
        if (Time.time >= squashNext)
        {
            squashNext = Time.time + 0.08f;
            Quaternion q = Quaternion.Euler(0f, shownYaw, 0f);
            Vector3 mid = shownPos + Vector3.up * Mathf.Min(proxyHalf.y, 0.9f);
            Vector3 ax = q * Vector3.right, az = q * Vector3.forward;
            // (a doorway's frame stands 6 cm into the opening on each side and is not in the collider)
            float gx = GapAcross(mid, ax, az, proxyHalf.x, proxyHalf.z), gz = GapAcross(mid, az, ax, proxyHalf.z, proxyHalf.x);
            float fx = gx < 2f * proxyHalf.x - 0.001f ? (gx - 0.14f) / (2f * proxyHalf.x) : 1f;
            float fz = gz < 2f * proxyHalf.z - 0.001f ? (gz - 0.14f) / (2f * proxyHalf.z) : 1f;
            fx = Mathf.Clamp(fx, 0.35f, 1f); fz = Mathf.Clamp(fz, 0.35f, 1f);
            // squashed one way, it stretches the other (its volume roughly kept, to a limit)
            wantX = fx < 0.999f ? fx : Mathf.Min(1.35f, 1f / Mathf.Sqrt(fz));
            wantZ = fz < 0.999f ? fz : Mathf.Min(1.35f, 1f / Mathf.Sqrt(fx));
        }
        if (wantX == 1f && wantZ == 1f && Mathf.Abs(squashX - 1f) < 0.002f && Mathf.Abs(squashZ - 1f) < 0.002f && Mathf.Abs(squashVX) < 0.01f && Mathf.Abs(squashVZ) < 0.01f)
        {
            if (squashX != 1f || squashZ != 1f) { squashX = 1f; squashZ = 1f; proxy.transform.localScale = proxyScale; }
            return;
        }
        // a spring, a little under-damped: it overshoots once and settles
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        squashVX += ((wantX - squashX) * 260f - squashVX * 17f) * dt; squashX += squashVX * dt;
        squashVZ += ((wantZ - squashZ) * 260f - squashVZ * 17f) * dt; squashZ += squashVZ * dt;
        squashX = Mathf.Clamp(squashX, 0.25f, 1.6f); squashZ = Mathf.Clamp(squashZ, 0.25f, 1.6f);
        proxy.transform.localScale = new Vector3(proxyScale.x * squashX, proxyScale.y, proxyScale.z * squashZ);
    }

    // A real prop has its shade patch only where it stands on a floor (not on a table or a shelf, never in the
    // air). A hider's prop the same, or the patch would give it away (Cor): what is right under it decides.
    GameObject proxyShade;
    float shadeNext;

    void ShadeCheck()
    {
        RaycastHit r;
        bool floor = false;
        if (Physics.Raycast(shownPos + Vector3.up * 0.04f, Vector3.down, out r, 0.16f, ~(1 << 9 | 1 << 10 | 1 << 18 | 1 << HIDER_SELF), QueryTriggerInteraction.Ignore))
        {
            floor = true;
            Transform t = r.collider.transform, props = game.builder.propRoot;
            for (int up = 0; up < 4 && t != null; up++)
            {
                if (t == props || t == proxyRoot) { floor = false; break; }
                t = t.parent;
            }
        }
        if (proxyShade.activeSelf != floor) proxyShade.SetActive(floor);
    }

    public override void OnStationEntered(VRCPlayerApi player)
    {
        if (player.isLocal) seated = true;
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (Utilities.IsValid(player) && player.isLocal)
        {
            seated = false;
            if (hiding || ghost) nextSit = Time.time + 0.3f;
        }
    }

    public override void OnDeserialization()
    {
        if (game != null && game.local != null) game.local.OnSlotChanged(this);
    }
}
