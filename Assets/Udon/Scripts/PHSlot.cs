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
    public PHWeapons weapons;          // the held weapons and their bursts (Swung)

    // ---- synced, written by the owner
    [UdonSynced] public bool inZone;
    [UdonSynced] public int readyRound = -1;
    [UdonSynced] public int def = -1;                 // disguise, -1 = none
    [UdonSynced] public int lastDef = -1;             // the disguise worn last (for the reveal, after a catch)
    [UdonSynced] public Vector3 propPos;
    [UdonSynced] public float propYaw;
    [UdonSynced] public bool airborne;                // the prop is off the ground (a hop, a fall): its stretch and squash (Squash)
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
    [UdonSynced] public int hunterHealth = 100;       // a hunter's weapon's condition (PHWeapons draws its wear; 0 = broken, out)
    [UdonSynced] public bool hunterOut;
    // the weapons this player picked on the selection wall (PHWeapons model indices; -1 = the map's default)
    [UdonSynced] public int meleeChoice = -1, gunChoice = -1;
    [UdonSynced] public bool armed;                  // a hider has shot in the final stand: their gun is seen by all (PHWeapons)
    [UdonSynced] public int wrongProps, hits, catches;
    [UdonSynced] public int round = -1;               // the round these figures belong to

    [HideInInspector] public bool seated;             // owner: in the station now
    GameObject proxy;
    int proxyDef = -2;
    float nextSend, nextSit;
    Vector3 shownPos;
    float shownYaw;
    bool dirty;
    GameObject placedProxy;    // the prop, place and facing UpdateProxy put last (not put again while they hold)
    Vector3 placedPos;
    float placedYaw;
    bool gapMeasured;          // the last look's place and facing (Squash: not measured again until it moves)
    Vector3 gapPos;
    float gapYaw;

    public bool IsMine { get { return Networking.IsOwner(gameObject) && game.SlotOf(Networking.LocalPlayer.playerId) == index; } }

    // ------------------------------------------------------------------ owner
    public void Claim()
    {
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        // (before the caller writes its own state: the takeover's clearing must not wipe it next frame)
        int myId = Networking.LocalPlayer.playerId;
        if (game.slotPlayer[index] == myId && myId != 0 && freshFor != myId) { freshFor = myId; Fresh(); }
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
        hunterHealth = 100; hunterOut = false; armed = false; wrongProps = 0; hits = 0; catches = 0;
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

    // A slot is reused when its player leaves and someone else joins: everything the last player left in it
    // (a disguise and hiding, so a stray prop stood where they left; caught; a scrap vote that counted for the
    // newcomer; inZone, so they counted as in the zone) is cleared the first time this player owns it.
    int freshFor;

    void Fresh()
    {
        inZone = false; readyRound = -1;
        def = -1; lastDef = -1; airborne = false; hiding = false; ghost = false;
        scrapVote = 0; undoVote = 0;
        health = 0; maxHealth = 0; caught = false; survivedMs = 0;
        hunterHealth = 100; hunterOut = false; armed = false; wrongProps = 0; hits = 0; catches = 0;
        round = -1;
        Send();
    }

    // a hunter's swing landed (or, from a desktop player, was made): sent to everyone by the hunter, who owns this
    // slot. kind: 0 a desktop slash that met nothing, 1 a wall or the like, 2 a prop, 3 a hider (PHWeapons.OnSwung)
    [NetworkCallable]
    public void Swung(Vector3 point, int kind)
    {
        if (weapons != null) weapons.OnSwung(index, point, kind);
    }

    // a hunter's blow on a heavy prop (sent to everyone by the hunter): it rocks where it stands (PHLocal.Rock)
    [NetworkCallable]
    public void Rocked(int rockEmit, Vector3 rockDir)
    {
        if (game != null && game.local != null) game.local.Rock(rockEmit, rockDir);
    }

    // a hider's final-stand shot, sent to everyone by the hider: where it ended and the hunter slot it hit (-1 none)
    [NetworkCallable]
    public void Shot(Vector3 end, int hitSlot)
    {
        if (weapons != null) weapons.OnShot(index, end, hitSlot);
    }

    // ...and on the hunter it hit (sent to the owner): weapon wear
    [NetworkCallable]
    public void TakeShot(int wear, int shooterPlayerId)
    {
        if (!IsMine) return;
        game.local.OnShot(wear, shooterPlayerId);
    }

    // a dart from a hunter (sent to the owner). Returns nothing: the owner's synced figures say what happened.
    // (only during the hunt: a dart still on its way when the round ended does not catch anyone in the reveal)
    // Every hider has 100 health (Cor); the size of the disguise scales what a dart takes: small x2, medium x1, large
    // x2/3, largest x1/2, so 2, 4, 6 and 8 darts catch them (25 a dart; the endgame's doubled darts halve those).
    // Whole numbers, rounded up: 17 a dart for a large prop (6 x 17 = 102, 5 x 17 = 85) and 13 for the largest
    // (8 x 13 = 104, 7 x 13 = 91), so the counts are exact. Worked out from the disguise the hider has when the dart
    // arrives, on the hider's own client (TakeHit), so changing disguise while it flies gains nothing.
    public const int HIDER_HEALTH = 100;
    // the disguise's size class: 0 small, 1 medium, 2 large, 3 largest (the shields over the health bar, PHView)
    public int SizeClass()
    {
        if (def < 0 || game == null || game.gen == null || def >= game.gen.defDisguise.Length) return 1;
        return Mathf.Clamp(game.gen.defDisguise[def] - 1, 0, 3);
    }

    public int HitDamage(int damage)
    {
        if (def < 0 || game == null || game.gen == null || def >= game.gen.defDisguise.Length) return damage;
        int cls = SizeClass();
        int num = cls == 0 ? 2 : (cls == 2 ? 2 : 1), den = cls == 2 ? 3 : (cls == 3 ? 2 : 1);
        return (damage * num + den - 1) / den;
    }

    [NetworkCallable]
    public void TakeHit(int damage, int shooterPlayerId)
    {
        if (!IsMine || caught || def < 0 || game.phase != PHGame.PH_HUNT) return;
        health -= HitDamage(damage);
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
        // (a taunt arriving late, after the hunt or the hider's catch, stays silent: taunts are sent only then)
        if (game.phase != PHGame.PH_HUNT || caught) return;
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
        if (mine && freshFor != myId) { freshFor = myId; Fresh(); }
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
        remoteSet = false; parked = false;   // (UpdateRemote sets them up again if this slot goes back to someone else)
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
            if (body.grounded == airborne) { airborne = !body.grounded; Send(); }
        }
        // otherwise the station follows the player, so sitting down leaves them where they are
        else station.transform.SetPositionAndRotation(me.GetPosition(), me.GetRotation());
        if (ghost)
        {
            Vector3 p = me.GetPosition();
            float y = me.GetRotation().eulerAngles.y;
            if ((p - walkPos).sqrMagnitude > 0.0025f || Mathf.Abs(Mathf.DeltaAngle(y, walkYaw)) > 5f) { walkPos = p; walkYaw = y; Send(); }
        }
        // (not during the END reveal: everyone walks free then, Cor)
        if ((hiding || ghost) && !seated && Time.time >= nextSit && game.phase != PHGame.PH_END)
        {
            nextSit = Time.time + 3f;
            station.UseStation(me);
        }
    }

    // Perf: the mobility and the parking were set every frame for every slot; nothing else moves a remote slot's
    // station on this client, so each is set once until the owner's path (UpdateOwner) or a ghost's walk moves it
    bool remoteSet, parked;

    void UpdateRemote()
    {
        // everyone else's copy of a hiding player's station is parked out of sight
        if (!remoteSet) { station.PlayerMobility = VRCStation.Mobility.Immobilize; remoteSet = true; }
        if (hiding) Park();
        else if (ghost)
        {
            // ghosts see ghosts; nobody else does (Cor: the dead see only the dead and the hunters)
            if (game.local != null && game.local.IsGhost())
            {
                Vector3 at = station.transform.position;
                if ((at - walkPos).sqrMagnitude > 25f) at = walkPos;
                float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
                station.transform.SetPositionAndRotation(Vector3.Lerp(at, walkPos, k), Quaternion.Euler(0f, walkYaw, 0f));
                parked = false;
            }
            else Park();
        }
    }

    void Park()
    {
        if (parked) return;
        parked = true;
        station.transform.SetPositionAndRotation(parkAt + new Vector3(index * 3f, 0f, 0f), Quaternion.identity);
    }

    // ------------------------------------------------------------------ the prop
    // A second copy of the prop inside the first, in the reveal's striped overlay material (PHGame.revealMat, drawn over
    // everything): it moves and squashes with the prop, blocks nothing, and has no shade patch
    GameObject revealCopy;
    void MakeRevealCopy()
    {
        if (proxyDef < 0 || proxyDef >= game.builder.propPrefabs.Length || game.builder.propPrefabs[proxyDef] == null) return;
        revealCopy = Instantiate(game.builder.propPrefabs[proxyDef]);
        revealCopy.name = "Reveal" + index;
        revealCopy.transform.SetParent(proxy.transform, false);
        revealCopy.transform.localPosition = Vector3.zero; revealCopy.transform.localRotation = Quaternion.identity; revealCopy.transform.localScale = Vector3.one;
        Collider[] cols = revealCopy.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
        Renderer[] rs = revealCopy.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            int n = rs[i].sharedMaterials.Length;
            Material[] ms = new Material[n];
            for (int j = 0; j < n; j++) ms[j] = game.revealMat;
            rs[i].sharedMaterials = ms;
        }
        Transform t = revealCopy.transform;
        if (t.childCount > 0 && t.GetChild(t.childCount - 1).name == "Shade") t.GetChild(t.childCount - 1).gameObject.SetActive(false);
        // (on the default layer: the owner's own prop is on HIDER_SELF, which their own camera leaves out)
        Transform[] parts = revealCopy.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < parts.Length; i++) parts[i].gameObject.layer = 0;
    }

    // the END reveal: out of the seat, walking as themselves; their prop stays where it is (PHLocal freezes the body)
    public void LeaveSeatForReveal()
    {
        if (seated) station.ExitStation(Networking.LocalPlayer);
        seated = false;
    }

    void UpdateProxy(bool mine)
    {
        // (only a hider of this round: a slot handed to a newcomer still carries the last player's prop until the
        // newcomer's client takes it over, and that stray prop could be seen and shot)
        int want = (hiding && def >= 0 && game.slotPlayer[index] != 0 && game.slotRole[index] == PHGame.ROLE_HIDER) ? def : -1;
        if (want != proxyDef)
        {
            proxyDef = want;
            if (revealCopy != null) { Destroy(revealCopy); revealCopy = null; }
            if (proxy != null) { game.builder.Uncage(proxy); Destroy(proxy); }   // the cage is lent, not the prop's
            proxy = null;
            if (want >= 0 && game.builder.propPrefabs != null && want < game.builder.propPrefabs.Length && game.builder.propPrefabs[want] != null)
            {
                proxy = Instantiate(game.builder.propPrefabs[want]);
                game.builder.TintProp(proxy, want);   // the map's foliage colour, as the props it hides among
                if (squashRig != null)
                {
                    squashRig.SetPositionAndRotation(propPos, Quaternion.identity);
                    squashRig.localScale = Vector3.one;
                    proxy.transform.SetParent(squashRig, true);
                }
                else proxy.transform.SetParent(proxyRoot, true);
                proxy.transform.position = propPos;
                proxy.name = "Hider" + index;
                shownPos = propPos; shownYaw = propYaw;
                // the squash through a doorway starts from the prefab's own scale
                proxyScale = proxy.transform.localScale;
                squashX = 1f; squashZ = 1f; squashVX = 0f; squashVZ = 0f; wantX = 1f; wantZ = 1f;
                // the slots' looks spread over the 0.08 s between them, never all in one frame
                squashNext = Time.time + (index % 32) * 0.0025f; gapMeasured = false;
                squashY = 1f; squashVY = 0f; wantY = 1f;
                hopS = 1f; hopV = 0f; wasAirborne = airborne; lastY = propPos.y; hopVY = 0f; hopFall = 0f; hopDipped = true;
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
                    // what the hider sees of it instead: the cage of its edges (and PHView's outline on the ground)
                    game.builder.Cage(proxy, want, proxyHalf);
                }
                else game.builder.FadeProp(proxy);   // another hider's prop is a prop: see-through in the prop viewer's cutout, not cut
            }
        }
        if (proxy == null) return;
        // the END reveal (Cor): every hider's prop highlighted through walls, for everyone (a late joiner too)
        bool reveal = game.phase == PHGame.PH_END && game.revealMat != null;
        if (reveal && revealCopy == null) MakeRevealCopy();
        else if (!reveal && revealCopy != null) { Destroy(revealCopy); revealCopy = null; }
        float k = mine ? 1f : 1f - Mathf.Exp(-14f * Time.deltaTime);
        if ((propPos - shownPos).sqrMagnitude > 25f) k = 1f;
        shownPos = Vector3.Lerp(shownPos, propPos, k);
        shownYaw = Mathf.LerpAngle(shownYaw, propYaw, k);
        // Perf: the ease never quite arrives, so a still prop was moved every frame (its colliders with it). Within a
        // millimetre and a tenth of a degree it arrives, and a prop where it was put last frame is not put again.
        if ((propPos - shownPos).sqrMagnitude < 1e-6f) shownPos = propPos;
        if (Mathf.Abs(Mathf.DeltaAngle(shownYaw, propYaw)) < 0.1f) shownYaw = propYaw;
        bool still = placedProxy == proxy && shownPos == placedPos && shownYaw == placedYaw;
        placedProxy = proxy; placedPos = shownPos; placedYaw = shownYaw;
        if (!still && squashRig != null)
        {
            // the rig at the prop's place, the prop turned inside it (local: the rig may be squashed)
            squashRig.position = shownPos;
            proxy.transform.localPosition = Vector3.zero;
            proxy.transform.localRotation = Quaternion.Euler(0f, shownYaw, 0f);
        }
        else if (!still) proxy.transform.SetPositionAndRotation(shownPos, Quaternion.Euler(0f, shownYaw, 0f));
        if (proxyShade != null && Time.time >= shadeNext) { shadeNext = Time.time + 0.15f; ShadeCheck(); }
        Squash();
    }

    // A big prop squeezes through a doorway (PHHiderBody moves it as a narrower box there). What everyone sees (Cor:
    // a bouncy squeeze, only visually): where there is something on both sides closer than the prop is wide, it is
    // squashed across that gap and stretched along it, on a spring that overshoots a little; clear of the gap it
    // springs back. The gap's direction is the building's (walls run along the world's x and z), not the prop's:
    // the prop hangs in a rig that keeps the world's axes and is turned inside it, so it narrows across the opening
    // whatever way it faces (Cor). The gap is looked for all along the prop, from before its front to past its
    // back, so it is one squeeze from the front going in until the back is out. Measured on each client against the
    // architecture's collider: nothing is synced.
    // And pressed down under a lintel lower than it is tall (Cor: squeeze vertically as well): the architecture
    // over its footprint, as low as PHHiderBody lets it go (SQUEEZE_LOW of its height), on the same spring.
    public Transform squashRig;
    Vector3 proxyScale = Vector3.one, proxyHalf;
    float squashX = 1f, squashZ = 1f, squashVX, squashVZ, wantX = 1f, wantZ = 1f, squashNext;
    float squashY = 1f, squashVY, wantY = 1f;

    // A hop's stretch and squash (Cor: "a bouncy stretch and squash for jumping"), the cartoon way: a quick dip as it
    // leaves the ground (the hop itself is not held back, the dip is only seen), tall and thin while it rises fast, near its own shape at
    // the top, a little tall while it falls, then flat and wide where it lands, springing back with a bounce or two.
    // Its volume is kept (across = 1 / sqrt(up)); about its base, so it never sinks into the floor or leaves it. Only
    // what is seen: the body is moved as its own shape all the while. Everyone sees it alike: it follows the synced
    // airborne flag and the prop's shown height, nothing else. At rest it is exactly its own scale.
    const float HOP_STRETCH = 0.13f;     // most it stretches up, rising fast (a little more with the spring's overshoot)
    const float HOP_RISE = 0.035f;       // stretch for each m/s it rises (a full hop leaves at 4-5 m/s)
    const float HOP_FALL = 0.02f;        // and for each m/s it falls
    const float HOP_DIP = 0.08f;         // squashed this much the moment it is seen rising off the ground
    const float HOP_LAND = 0.2f;         // squashed this much where it lands from a full hop (to 0.8)
    const float HOP_LAND_FULL = 4.5f;    // the fall speed (m/s) of a full landing; slower ones squash less (squared)
    const float HOP_K = 1000f, HOP_DAMP = 26f;   // the spring: overshoots once a little, settled in about 0.3 s
    float hopS = 1f, hopV, hopVY, hopFall, lastY;
    float hopCap = 2f;   // the most the hop may stretch it this frame (the room over it, Hop)
    bool wasAirborne, hopDipped;

    // the hop's stretch (up) at this frame, on its spring; exactly 1 at rest
    void Hop()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        if (dt <= 0f) return;
        // how fast the shown prop rises or falls (smoothed: a remote prop's height comes in steps); a jump of more
        // than half a metre in a frame is a correction or a new place, not movement
        float dy = shownPos.y - lastY;
        lastY = shownPos.y;
        if (Mathf.Abs(dy) > 0.5f) dy = 0f;
        // (over the frame's real length: after a hitch dt is held to 0.033 and dy / dt read as a fast hop or fall)
        hopVY = Mathf.Lerp(hopVY, Mathf.Clamp(dy / Time.deltaTime, -12f, 12f), 1f - Mathf.Exp(-25f * dt));
        // (in the air until the shown prop is down too: elsewhere it follows the synced place a little behind)
        bool air = airborne || shownPos.y - propPos.y > 0.015f;
        if (air)
        {
            hopFall = Mathf.Max(hopFall, -hopVY);
            // the dip as it leaves the ground, once it is seen to rise (stepping off an edge has none); elsewhere
            // as soon as the synced place is above the shown one, while the shown prop is still on the ground
            if (!hopDipped && (hopVY > 1.5f || propPos.y - shownPos.y > 0.05f)) { hopDipped = true; hopS = 1f - HOP_DIP; hopV = 0f; return; }
        }
        if (air != wasAirborne)
        {
            wasAirborne = air;
            if (air) { hopFall = 0f; hopDipped = false; }
            else
            {
                // where it lands: flat at once, by how fast it came down (a step down barely shows)
                float k = Mathf.Clamp01(hopFall / HOP_LAND_FULL);
                if (k > 0.05f) { hopS = 1f - HOP_LAND * k * k; hopV = 0f; return; }
            }
        }
        float want = 1f, cap = 1f + HOP_STRETCH + 0.05f;
        if (air)
        {
            want = 1f + Mathf.Min(HOP_STRETCH, hopVY > 0f ? hopVY * HOP_RISE : -hopVY * HOP_FALL);
            // no taller than the room over it: a hop under a ceiling does not stretch through it
            float tall = 2f * proxyHalf.y;
            if (tall > 0.01f) cap = Mathf.Min(cap, Mathf.Max(1f, (WallAt(shownPos + Vector3.up * 0.05f, Vector3.up, tall * cap + 0.1f) + 0.03f) / tall));
            want = Mathf.Min(want, cap);
        }
        hopCap = cap;
        if (!air && Mathf.Abs(hopS - 1f) < 0.003f && Mathf.Abs(hopV) < 0.03f) { hopS = 1f; hopV = 0f; return; }
        hopV += ((want - hopS) * HOP_K - hopV * HOP_DAMP) * dt;
        hopS = Mathf.Clamp(hopS + hopV * dt, 0.7f, cap);
    }

    // how far the architecture is from a place along dir, at most reach
    float WallAt(Vector3 from, Vector3 dir, float reach)
    {
        Collider arch = game.builder.collisionCollider;
        if (arch == null) return reach;
        RaycastHit r;
        if (arch.Raycast(new Ray(from, dir), out r, reach)) return r.distance;
        return reach;
    }

    // The narrowest gap across the prop (along 'across', the prop's half width that way 'half'), 2 * half when
    // there is none: looked for at its middle and every 0.2 m (a wall's thickness) along 'along' to both ends
    // (half length 'reach') and on past each end by about the prop's own half width (per object: the wider it
    // is, the sooner it starts), as far as the way there is clear. Only something on both sides is a gap.
    float GapAcross(Vector3 mid, Vector3 across, Vector3 along, float half, float reach)
    {
        float gap = 2f * half;
        float far = reach + 0.1f + 1.1f * half;
        float clearA = WallAt(mid, along, far), clearB = WallAt(mid, -along, far);
        float look = half + 0.03f;
        for (float o = -clearB; o <= clearA + 0.001f; o += 0.2f)
        {
            Vector3 at = mid + along * o;
            float ga = WallAt(at, across, look);
            if (ga >= look - 0.001f) continue;
            float gb = WallAt(at, -across, look);
            if (gb >= look - 0.001f) continue;
            if (ga + gb < gap) gap = ga + gb;
        }
        return gap;
    }

    void Squash()
    {
        if (squashRig == null) return;
        Hop();
        // it never squeezes (PHHiderBody.SQUEEZE_HALF, SQUEEZE_HALF_Y): the hop alone
        if (proxyHalf.x <= 0.3f && proxyHalf.z <= 0.3f && proxyHalf.y <= 1f) { Scale(); return; }
        // a prop that has not moved or turned since the last look has the same gap and the same lintel over it (the
        // architecture does not move; doors are not in its collider): measured again only once it has (perf: a still
        // big prop cast about 75 rays every 0.08 s on every client)
        if (Time.time >= squashNext && gapMeasured && (shownPos - gapPos).sqrMagnitude < 1e-4f && Mathf.Abs(Mathf.DeltaAngle(shownYaw, gapYaw)) < 0.5f)
            squashNext = Time.time + 0.08f;
        if (Time.time >= squashNext)
        {
            squashNext = Time.time + 0.08f;
            gapMeasured = true; gapPos = shownPos; gapYaw = shownYaw;
            // the prop's half extents along the world's x and z, as it is turned
            float rad = shownYaw * Mathf.Deg2Rad, c = Mathf.Abs(Mathf.Cos(rad)), sn = Mathf.Abs(Mathf.Sin(rad));
            float ex = c * proxyHalf.x + sn * proxyHalf.z, ez = sn * proxyHalf.x + c * proxyHalf.z;
            Vector3 mid = shownPos + Vector3.up * Mathf.Min(proxyHalf.y, 0.9f);
            float gx = GapAcross(mid, Vector3.right, Vector3.forward, ex, ez), gz = GapAcross(mid, Vector3.forward, Vector3.right, ez, ex);
            // (a doorway's frame stands 6 cm into the opening on each side and is not in the collider)
            float fx = gx < 2f * ex - 0.001f ? (gx - 0.14f) / (2f * ex) : 1f;
            float fz = gz < 2f * ez - 0.001f ? (gz - 0.14f) / (2f * ez) : 1f;
            fx = Mathf.Clamp(fx, 0.3f, 1f); fz = Mathf.Clamp(fz, 0.3f, 1f);
            // squashed one way, it stretches the other (its volume roughly kept, to a limit)
            wantX = fx < 0.999f ? fx : Mathf.Min(1.35f, 1f / Mathf.Sqrt(fz));
            wantZ = fz < 0.999f ? fz : Mathf.Min(1.35f, 1f / Mathf.Sqrt(fx));
            // the lowest of the architecture over its middle and towards its four sides
            float tall = 2f * proxyHalf.y, low = tall;
            if (tall > 1.2f)
            {
                Vector3 b = shownPos + Vector3.up * 0.3f;
                low = Mathf.Min(low, 0.3f + WallAt(b, Vector3.up, tall - 0.3f));
                low = Mathf.Min(low, 0.3f + WallAt(b + Vector3.right * ex * 0.7f, Vector3.up, tall - 0.3f));
                low = Mathf.Min(low, 0.3f + WallAt(b - Vector3.right * ex * 0.7f, Vector3.up, tall - 0.3f));
                low = Mathf.Min(low, 0.3f + WallAt(b + Vector3.forward * ez * 0.7f, Vector3.up, tall - 0.3f));
                low = Mathf.Min(low, 0.3f + WallAt(b - Vector3.forward * ez * 0.7f, Vector3.up, tall - 0.3f));
            }
            wantY = low < tall - 0.001f ? Mathf.Clamp((low - 0.04f) / tall, 0.6f, 1f) : 1f;
        }
        if (wantX == 1f && wantZ == 1f && wantY == 1f && Mathf.Abs(squashX - 1f) < 0.002f && Mathf.Abs(squashZ - 1f) < 0.002f && Mathf.Abs(squashY - 1f) < 0.002f
            && Mathf.Abs(squashVX) < 0.01f && Mathf.Abs(squashVZ) < 0.01f && Mathf.Abs(squashVY) < 0.01f)
        {
            if (squashX != 1f || squashZ != 1f || squashY != 1f) { squashX = 1f; squashZ = 1f; squashY = 1f; squashVY = 0f; }
            Scale();
            return;
        }
        // a spring, a little under-damped: it overshoots once and settles
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        squashVX += ((wantX - squashX) * 260f - squashVX * 17f) * dt; squashX += squashVX * dt;
        squashVZ += ((wantZ - squashZ) * 260f - squashVZ * 17f) * dt; squashZ += squashVZ * dt;
        squashVY += ((wantY - squashY) * 260f - squashVY * 17f) * dt; squashY += squashVY * dt;
        squashX = Mathf.Clamp(squashX, 0.25f, 1.6f); squashZ = Mathf.Clamp(squashZ, 0.25f, 1.6f); squashY = Mathf.Clamp(squashY, 0.5f, 1.2f);
        Scale();
    }

    // the doorway's squash and the hop's together, both about the base; exactly the prop's own scale at rest
    void Scale()
    {
        Vector3 s = Vector3.one;
        if (hopS != 1f)
        {
            float across = 1f / Mathf.Sqrt(hopS);
            s = new Vector3(across, hopS, across);
        }
        // (the hop's stretch held to the room over it with the doorway's spring on top too: that spring's overshoot,
        // times the stretch, took the top 2 cm into a corridor's ceiling; never lower than the doorway's alone)
        float sy = s.y * squashY;
        if (hopS > 1f) sy = Mathf.Min(sy, Mathf.Max(squashY, hopCap));
        s = new Vector3(s.x * squashX, sy, s.z * squashZ);
        if (squashRig.localScale != s) squashRig.localScale = s;
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
