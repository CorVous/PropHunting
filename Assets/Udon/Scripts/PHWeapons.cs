using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// What the players hold (Cor): every hunter carries a melee weapon instead of a dart gun, and a hider's selector
// is a little blaster. Nothing here is synced: each client draws every hunter's weapon in that player's right
// hand from what it already has (the slot table, the roles, PHSlot.hunterHealth), and the swings and their hits
// arrive as one network event each (PHSlot.Swung).
//   the swing: VR = swing the hand (the weapon's head sweeps through what it meets); desktop = click, and the
//              weapon in the lower right of the view slashes across it (a trail and sparks along the arc), and
//              whatever is within reach in front of the eyes is struck
//   wear (Cor: no hunter health bar): a wrong prop takes a chunk out of the weapon. The chunks are holes cut
//              by the weapon shader (PropHunt/Weapon) at spots on the model's head, the same spots for everyone
//              (from the slot and the round); the more wear, the more and the bigger. At no condition left the
//              weapon breaks apart and the hunter is out.
//   bursts: a hit puffs where it struck (dust and chips on a prop, sparks on a wall, confetti on a hider), heard
//              there by everyone near.
//   the selector (a hider's): desktop shows it in the lower right of the view; VR keeps PHView's in the hand.
//              Use kicks it back with a flash at the muzzle.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHWeapons : UdonSharpBehaviour
{
    public PHGame game;
    public PHLocal local;
    public PHAudio sound;
    public PHHand hand;                // each player's own hold: hand, move, turn and size for the melee weapon and the gun (PHHand)

    // ---- the models (WeaponBuilder, from Resources/PropHunt/weapons.bytes)
    public string[] meleeId;           // Tools/weapon_catalog.json ids ("melee:<pack>:<model>")
    public Mesh[] meleeMesh;
    public string[] gunId;
    public Mesh[] gunMesh;             // the selectors (the hider's gun); gunMuzzle: the muzzle's z
    public float[] gunMuzzle;
    public Vector3[] gunMuzzlePoint;   // the barrel's end in the model's own space (WeaponBuilder; may be missing in an older scene)
    public float[] meleeTip;           // the head's far end along +Z (metres, grip at 0)
    public float[] meleeHead;          // where the head begins along +Z (the swing sweeps from here to the tip)
    public Vector3[] chipSpots;        // SPOTS per model, on the head's surface, in the model's own space
    public Color[] meleeColor;         // the head's main colour (the chips that fly off)
    // the nibbled wear (Cor): per model, a 3D texture of the wear at which each bit of it is eaten (Tools/bake_wear.py),
    // where its grid starts in the model's space, and 1 / its size; the PropHunt/Weapon shader draws what is left
    public Texture[] meleeWear;
    public Vector4[] meleeWearLo, meleeWearInv;
    // each model's grip (Tools/weapon_grips.tsv, set by WeaponBuilder; each player's own hold goes on top of it, PHHand):
    // the point of the model that sits in the middle of the closed fist (model space, metres), the model's turn about it
    // (degrees, Quaternion.Euler) and its size (1 = as made). Missing (an older scene): the model's origin, no turn, 1.
    public Vector3[] meleeGrip, meleeTurn;
    public float[] meleeSize;
    public Vector3[] gunGrip, gunTurn;
    public float[] gunSize;
    // Cor's drawn weapons (WeaponBuilder, Tools/make_card_weapons.py): flat cards that wear by the atlas, not a grid;
    // a two-piece set's other piece in the off hand (null: none), which strikes too unless it is a shield. Per melee model.
    public bool[] meleeCard;
    public Mesh[] offMesh;
    public bool[] offShield;
    public float[] offTip, offHead;
    public int[] themeMelee = { 0, 2, 1 };   // the model by map theme (GenData.THEME_NONE, SPACE, MEDIEVAL)
    public const int SPOTS = 24;

    // ---- one held weapon per slot (made by WeaponBuilder; no colliders)
    public Transform[] held;
    public MeshFilter[] heldFilter;
    public MeshRenderer[] heldRenderer;
    public TrailRenderer[] heldTrail;  // at the tip; it draws only while the weapon swings
    public Transform[] heldOff;        // the off hand's piece of a two-piece set, one per slot (may be missing in an older scene)
    public MeshFilter[] heldOffFilter;
    public MeshRenderer[] heldOffRenderer;

    // ---- effects (world space particle systems, moved where they are needed and told to emit)
    public ParticleSystem fxDust;      // a prop struck: a puff
    public ParticleSystem fxChips;     // ...and splinters; also the chunks off a weapon, and the pieces of a broken one
    public ParticleSystem fxWear;      // the weapon's own pieces when it wears: little tumbling blocks
    public ParticleSystem fxSpark;     // a wall struck; the sparks along a desktop slash
    public ParticleSystem fxHider;     // a hider struck: confetti
    public ParticleSystem fxMuzzle;    // the selector's flash

    // ---- the selector
    public Transform deskSelector;     // desktop: in the view's lower right
    public Transform deskSelectorModel;
    public Transform vrSelectorModel;  // VR: the model in PHView's Selector (only its kick is ours)
    public Vector3 deskSelectorAt = new Vector3(0.11f, -0.10f, 0.30f);   // clear of the third-person view in the corner
    public Vector3 deskSelectorEuler = new Vector3(-3f, -8f, 0f);
    public float selectorMuzzle = 0.21f;
    // the final stand (Cor): a hider who has shot (PHSlot.armed) shows their gun at their prop to everyone
    public Transform[] hiderGun;       // one per slot
    // a shot's bullet (Cor: bullets leave a trail): a glowing pellet that flies from the muzzle to where the shot
    // ended at BULLET_SPEED, a trail behind it; a small pool, reused
    public Transform[] bullets;
    public TrailRenderer[] bulletTrails;
    public const float GUN_SCALE = 1.8f, BULLET_SPEED = 120f, BULLET_TRAIL = 1.5f;   // the trail's time is set on the TrailRenderers (WeaponBuilder)
    // the halo (Cor, hop playtest): a glowing ring over the head of a caught hider or a hunter who is out, seen only by
    // those who can see the dead (the other ghosts during the round; everyone in the END reveal); one per slot
    // (WeaponBuilder), put over the avatar's head bone each frame
    public Transform[] halo;
    public const float HALO_UP = 0.3f;   // above the head bone, metres at avatar size 1

    // ---- how weapons sit and move (tunable)
    public Vector3 handEuler = new Vector3(-50f, 0f, 0f);     // VR and remote players: the weapon against the hand's tracking
    public Vector3 deskAt = new Vector3(0.30f, -0.38f, 0.62f); // desktop: in the view (metres at avatar size 1), low in the right corner
    public float deskScale = 0.75f;                             // desktop: drawn smaller (Cor: it hid too much of the view)
    public Vector3 deskEuler = new Vector3(-40f, -25f, 15f);
    public const float SWING_TIME = 0.42f, SWING_HIT_AT = 0.12f, SWING_SPEED = 3.0f, VR_COOLDOWN = 0.45f;
    public const float WHOOSH_SPEED = 7.0f, WHOOSH_HAND = 2.5f, WHOOSH_REARM = 1.0f;   // VR: the swing sound needs the tip AND the hand moving fast; it re-arms once the tip is nearly still
    public const float SWING_RADIUS = 0.08f, SWING_EXTRA = 0.5f;   // VR: the sweeps' radius (was 0.06) and the reach beyond the tip (hop playtest)
    public const float REACH = 2.2f;   // desktop: how far in front of the eyes a slash strikes (metres at avatar size 1)
    public const int MASK = ~(1 << 9 | 1 << 10 | 1 << 18);    // as PHLocal.Aim: not players

    int[] heldModel;                   // the model each held weapon shows (-1 hidden)
    int[] heldOffModel;                // the model whose off-hand piece heldOff shows (-1 hidden)
    int[] heldWear;                    // the condition it was last drawn with
    bool[] heldOut, heldBroke;
    float[] swingAt;                   // when its last slash began (desktop players, seen by everyone)
    float[] trailUntil;
    MaterialPropertyBlock block;
    int idWear, idWearTex, idWearLo, idWearInv, idDither;
    bool ready;

    // the local VR swing (per piece: 0 the weapon hand's, 1 the off hand's)
    Vector3[] prevTip = new Vector3[2], prevMid = new Vector3[2], prevExt = new Vector3[2], prevGrip = new Vector3[2];
    bool[] prevValid = new bool[2];
    bool whooshArmed = true;
    float nextVrHit;
    // the local desktop slash
    float mySwingAt = -99f;
    bool mySwingStruck = true;
    // the selector's kick
    float kickAt = -99f;
    // the final stand's guns: where each looks (its last shot's end, for a moment), and the streak
    float[] shotAt;
    Vector3[] shotEnd;
    Vector3[] gunShown;
    int[] hiderGunModel;   // the gun mesh each hider gun shows (-1 none yet)
    Vector3[] bFrom, bTo;
    float[] bStart, bTime, bTimeLast;
    int[] bHit;
    int nextBullet;

    void Start() { Setup(); }

    void Setup()
    {
        if (ready) return;
        ready = true;
        int n = held.Length;
        heldModel = new int[n]; heldWear = new int[n]; heldOut = new bool[n]; heldBroke = new bool[n]; swingAt = new float[n]; trailUntil = new float[n];
        for (int i = 0; i < n; i++) { heldModel[i] = -2; heldWear[i] = -1; swingAt[i] = -99f; }
        block = new MaterialPropertyBlock();
        idWear = VRCShader.PropertyToID("_Wear"); idWearTex = VRCShader.PropertyToID("_WearTex");
        idWearLo = VRCShader.PropertyToID("_WearLo"); idWearInv = VRCShader.PropertyToID("_WearInv");
        idDither = VRCShader.PropertyToID("_Dither");
        for (int i = 0; i < n; i++) { held[i].gameObject.SetActive(false); heldTrail[i].emitting = false; }
        heldOffModel = new int[n];
        for (int i = 0; i < n; i++) { heldOffModel[i] = -1; if (HasOff(i)) heldOff[i].gameObject.SetActive(false); }
        if (deskSelector != null) deskSelector.gameObject.SetActive(false);
        shotAt = new float[n]; shotEnd = new Vector3[n]; gunShown = new Vector3[n];
        hiderGunModel = new int[n];
        for (int i = 0; i < n; i++) hiderGunModel[i] = -1;
        for (int i = 0; i < n; i++) { shotAt[i] = -99f; if (hiderGun != null && i < hiderGun.Length) hiderGun[i].gameObject.SetActive(false); }
        int nb = bullets != null ? bullets.Length : 0;
        bFrom = new Vector3[nb]; bTo = new Vector3[nb]; bStart = new float[nb]; bTime = new float[nb]; bTimeLast = new float[nb]; bHit = new int[nb];
        for (int b = 0; b < nb; b++) { bullets[b].gameObject.SetActive(false); bTime[b] = -1f; }
        if (halo != null) for (int i = 0; i < halo.Length; i++) halo[i].gameObject.SetActive(false);
    }

    // profiling (PHProfiler)
    [System.NonSerialized] public System.Diagnostics.Stopwatch profWatch;
    [System.NonSerialized] public double profMs;

    public override void PostLateUpdate()
    {
        if (profWatch == null) { Body(); return; }
        double p0 = profWatch.Elapsed.TotalMilliseconds;
        Body();
        profMs += profWatch.Elapsed.TotalMilliseconds - p0;
    }

    // the model a hunter holds on this map
    public int ModelNow()
    {
        int t = game.gen.mapTheme;
        int m = t >= 0 && t < themeMelee.Length ? themeMelee[t] : 0;
        return m >= 0 && m < meleeMesh.Length ? m : 0;
    }

    void Body()
    {
        Setup();
        int phase = game.phase;
        bool round = phase == PHGame.PH_BRIEF || phase == PHGame.PH_HIDE || phase == PHGame.PH_HUNT || phase == PHGame.PH_END;
        VRCPlayerApi me = Networking.LocalPlayer;
        int mine = game.SlotOf(me.playerId);
        // Perf (Cor's profile: about 1 ms every frame, the lobby too): between rounds nobody holds a weapon or a gun,
        // so the slots are only looked over a few times a second (what a round left out is put away within 0.2 s)
        if (!round && (Time.frameCount % 12) != 0) { UpdateSelector(me, mine); UpdateBullets(); return; }
        int model = ModelNow();
        // who sees the dead: a ghost of this round, and everyone in the reveal
        PHSlot meS = mine >= 0 && mine < game.slots.Length ? game.slots[mine] : null;
        bool seeDead = round && (phase == PHGame.PH_END || (meS != null && meS.ghost));
        for (int i = 0; i < held.Length; i++)
        {
            PHSlot s = i < game.slots.Length ? game.slots[i] : null;
            int pid = game.slotPlayer[i];
            // (Perf: an empty slot, most of the 32, once its weapon and gun are put away)
            if (pid <= 0 && heldModel[i] == -1 && (hiderGun == null || i >= hiderGun.Length || !hiderGun[i].gameObject.activeSelf)
                && (halo == null || i >= halo.Length || !halo[i].gameObject.activeSelf)) { heldOut[i] = false; continue; }
            bool show = round && s != null && pid > 0 && game.RoleOf(i) == PHGame.ROLE_HUNTER && s.round == game.round;
            VRCPlayerApi p = show ? VRCPlayerApi.GetPlayerById(pid) : null;
            if (p == null || !Utilities.IsValid(p)) show = false;
            // the break: everyone sees it fall apart where it was
            // a weapon that breaks and grows back (not out): the same shatter, and it stays in the hand
            if (show && s.weaponRecovering && !heldBroke[i] && heldModel[i] >= 0) ShatterFx(i);
            if (show) heldBroke[i] = s.weaponRecovering;
            if (show && s.hunterOut != heldOut[i])
            {
                heldOut[i] = s.hunterOut;
                if (s.hunterOut && heldModel[i] >= 0) Shatter(i);
                if (s.hunterOut && i == watchSlot && Time.time < watchUntil) { local.statWeaponsBroken++; watchSlot = -1; }
            }
            UpdateHiderGun(i, s, i == mine);
            UpdateHalo(i, s, pid, i == mine, seeDead);
            if (show && s.hunterOut) show = false;
            if (!show)
            {
                if (heldModel[i] != -1) { heldModel[i] = -1; held[i].gameObject.SetActive(false); heldTrail[i].emitting = false; }
                ShowOff(i, -1);
                if (s == null || s.round != game.round) heldOut[i] = false;
                continue;
            }
            int mm = s.meleeChoice >= 0 && s.meleeChoice < meleeMesh.Length ? s.meleeChoice : model;
            if (heldModel[i] != mm)
            {
                heldModel[i] = mm;
                heldFilter[i].sharedMesh = meleeMesh[mm];
                heldWear[i] = -1;
                held[i].gameObject.SetActive(true);
                heldTrail[i].Clear();
            }
            ShowOff(i, mm);
            int key = s.hunterHealth + (s.weaponRecovering ? 1000 : 0);
            if (key != heldWear[i])
            {
                bool more = heldWear[i] >= 0 && heldWear[i] < 1000 && !s.weaponRecovering && s.hunterHealth < heldWear[i];
                heldWear[i] = key;
                SetWear(heldRenderer[i], mm, s.hunterHealth, s.weaponRecovering);
                if (heldOffModel[i] >= 0) SetWear(heldOffRenderer[i], mm, s.hunterHealth, s.weaponRecovering);
                if (more) ChipOff(i, s, mm);
            }
            if (i == mine) PlaceMine(i, p, mm);
            else PlaceOther(i, p);
            bool trail = Time.time < trailUntil[i];
            if (heldTrail[i].emitting != trail) heldTrail[i].emitting = trail;
        }
        UpdateSelector(me, mine);
        UpdateBullets();
    }

    void UpdateBullets()
    {
        if (bullets == null) return;
        for (int b = 0; b < bullets.Length; b++)
        {
            if (bTime[b] < 0f) continue;
            float k = (Time.time - bStart[b]) / bTime[b];
            if (k >= 1f)
            {
                // it arrives: sparks, and a ring on metal if it hit a hunter; the pellet stops, its trail fades out
                bullets[b].position = bTo[b];
                Burst(fxSpark, bTo[b], bHit[b] >= 0 ? 16 : 6);
                if (bHit[b] >= 0) sound.PlayAt("impactMetal_light_002", bTo[b], 14f, 0.9f, 1f);
                bTime[b] = -1f;
                bulletTrails[b].emitting = false;
                SendCustomEventDelayedSeconds(nameof(_HideBullets), BULLET_TRAIL + 0.1f);
                continue;
            }
            bullets[b].position = Vector3.Lerp(bFrom[b], bTo[b], k);
        }
    }

    // pellets that have landed are put away once their trails have faded
    public void _HideBullets()
    {
        if (bullets == null) return;
        for (int b = 0; b < bullets.Length; b++)
            if (bTime[b] < 0f && bullets[b].gameObject.activeSelf && Time.time - bStart[b] - bTimeLast[b] > BULLET_TRAIL) bullets[b].gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ the final stand
    // a hunter this player just shot: if their weapon breaks within a moment, this player broke it (PHLocal.statWeaponsBroken)
    int watchSlot = -1;
    float watchUntil;
    public void WatchBreak(int slot) { watchSlot = slot; watchUntil = Time.time + 2.5f; }

    // a hider's gun, at their prop: above its top, turned as the prop faces, or at what it last shot for a moment.
    // Never shown to its owner, not even in their third-person view (Cor, 11-22: the hider should not see it); the
    // HUD line on the first shot (say.gunSeen) still tells them the others can
    int[] hiderGunLayer;
    void UpdateHiderGun(int i, PHSlot s, bool mine)
    {
        if (hiderGun == null || i >= hiderGun.Length) return;
        bool show = s != null && !mine && s.armed && !s.caught && s.def >= 0 && game.phase == PHGame.PH_HUNT
            && game.RoleOf(i) == PHGame.ROLE_HIDER && s.round == game.round;
        Transform g = hiderGun[i];
        if (show) SetGunLayer(i, 0);
        if (g.gameObject.activeSelf != show)
        {
            g.gameObject.SetActive(show);
            if (show) gunShown[i] = GunPlace(s);
        }
        if (!show) return;
        int gi = GunOf(s);
        if (hiderGunModel[i] != gi && g.childCount > 0) { hiderGunModel[i] = gi; SetGunMesh(g.GetChild(0), gi); }
        Vector3 want = GunPlace(s);
        gunShown[i] = (want - gunShown[i]).sqrMagnitude > 9f ? want : Vector3.Lerp(gunShown[i], want, Mathf.Min(1f, Time.deltaTime * 12f));
        Quaternion r = Quaternion.Euler(0f, s.propYaw, 0f);
        if (Time.time - shotAt[i] < 0.8f)
        {
            Vector3 d = shotEnd[i] - gunShown[i];
            if (d.sqrMagnitude > 1e-4f) r = Quaternion.LookRotation(d, Vector3.up);
        }
        // a slow bob, so it reads as floating
        g.SetPositionAndRotation(gunShown[i] + Vector3.up * (0.04f * Mathf.Sin(Time.time * 2.4f + i)), r);
        g.localScale = Vector3.one * GUN_SCALE;
    }

    // ------------------------------------------------------------------ the halo
    // over a dead player's head (a caught hider, a hunter who is out; not a mid-round joiner), for those who see the
    // dead; never one's own (it would hang over the view)
    void UpdateHalo(int i, PHSlot s, int pid, bool mine, bool seeDead)
    {
        if (halo == null || i >= halo.Length) return;
        Transform h = halo[i];
        bool show = seeDead && !mine && s != null && pid > 0 && s.ghost && (s.caught || s.hunterOut) && s.round == game.round;
        VRCPlayerApi p = show ? VRCPlayerApi.GetPlayerById(pid) : null;
        if (show && (p == null || !Utilities.IsValid(p))) show = false;
        if (h.gameObject.activeSelf != show) h.gameObject.SetActive(show);
        if (!show) return;
        float sc = AvatarScale(p);
        Vector3 head = p.GetBonePosition(HumanBodyBones.Head);
        if (head == Vector3.zero) head = p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        head = game.SeenHere(i, head);   // (PHGame.layers: in this client's copy of the map)
        // a slow bob and a slight tilt, turning, so it reads as floating
        h.SetPositionAndRotation(head + Vector3.up * (sc * (HALO_UP + 0.015f * Mathf.Sin(Time.time * 2f + i))),
            Quaternion.Euler(0f, Time.time * 30f + i * 40f, 0f) * Quaternion.Euler(8f, 0f, 0f));
        h.localScale = Vector3.one * sc;
    }

    // the layer a hider gun is drawn on (and its model's), set only when it changes
    void SetGunLayer(int i, int layer)
    {
        if (hiderGunLayer == null || hiderGunLayer.Length != hiderGun.Length)
        {
            hiderGunLayer = new int[hiderGun.Length];
            for (int k = 0; k < hiderGunLayer.Length; k++) hiderGunLayer[k] = -1;
        }
        if (hiderGunLayer[i] == layer) return;
        hiderGunLayer[i] = layer;
        Transform g = hiderGun[i];
        g.gameObject.layer = layer;
        for (int k = 0; k < g.childCount; k++) g.GetChild(k).gameObject.layer = layer;
    }

    Vector3 GunPlace(PHSlot s)
    {
        float h = s.def >= 0 && s.def < game.gen.defSy.Length ? game.gen.defSy[s.def] / 1000f : 1f;
        return game.builder.ToWorld(s.propPos) + Vector3.up * (h + 0.25f);   // (propPos is map-local)
    }

    // a final-stand shot, on every client (PHSlot.Shot): the flash, and a bullet with a trail that sparks where it lands
    public void OnShot(int slot, Vector3 end, int hitSlot)
    {
        Setup();
        end = game.builder.ToWorld(end);   // (sent map-local: PHLocal.HiderShoot)
        if (slot < 0 || slot >= held.Length) return;
        shotAt[slot] = Time.time; shotEnd[slot] = end;
        VRCPlayerApi p = VRCPlayerApi.GetPlayerById(game.slotPlayer[slot]);
        bool mine = p != null && Utilities.IsValid(p) && p.isLocal;
        Vector3 from;
        if (mine)
        {
            Transform m = Networking.LocalPlayer.IsUserInVR() ? vrSelectorModel : deskSelectorModel;
            from = m != null ? m.position + m.rotation * (Vector3.forward * Muzzle(game.slots[slot]) * m.lossyScale.x) : end;
        }
        else
        {
            PHSlot s = game.slots[slot];
            if (hiderGun != null && !hiderGun[slot].gameObject.activeSelf) { SetGunLayer(slot, 0); hiderGun[slot].gameObject.SetActive(true); gunShown[slot] = GunPlace(s); }
            Vector3 g = gunShown[slot];
            Vector3 d = end - g;
            from = g + (d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward) * (Muzzle(s) * GUN_SCALE);
            if (fxMuzzle != null) { fxMuzzle.transform.position = from; fxMuzzle.Emit(6); }
            sound.PlayAt("laserSmall_000", from, 18f, 0.6f, 1f);
        }
        if (bullets != null && bullets.Length > 0)
        {
            int b = nextBullet; nextBullet = (nextBullet + 1) % bullets.Length;
            bFrom[b] = from; bTo[b] = end; bStart[b] = Time.time; bHit[b] = hitSlot;
            bTime[b] = Mathf.Max(0.03f, (end - from).magnitude / BULLET_SPEED);
            bTimeLast[b] = bTime[b];
            bullets[b].gameObject.SetActive(true);
            bullets[b].position = from;
            bulletTrails[b].Clear();
            bulletTrails[b].emitting = true;
        }
    }

    // a catalog id's model index (the selection wall writes the index into PHSlot.meleeChoice / gunChoice); -1 unknown
    public int MeleeIndexOf(string id) { for (int i = 0; i < meleeId.Length; i++) if (meleeId[i] == id) return i; return -1; }
    public int GunIndexOf(string id) { for (int i = 0; i < gunId.Length; i++) if (gunId[i] == id) return i; return -1; }

    int GunOf(PHSlot s) { return s != null && gunMesh != null && s.gunChoice >= 0 && s.gunChoice < gunMesh.Length ? s.gunChoice : 0; }

    // a gun model's mesh onto a model object (its MeshFilter), only when it changes
    void SetGunMesh(Transform model, int g)
    {
        if (model == null || gunMesh == null || gunMesh.Length == 0) return;
        MeshFilter f = model.GetComponent<MeshFilter>();
        if (f != null && f.sharedMesh != gunMesh[g]) f.sharedMesh = gunMesh[g];
    }

    // (Perf: the same, for the selectors every frame: their filters looked up once, a mesh set only on a change)
    MeshFilter vrSelFilter, deskSelFilter;
    int selGunShown = -1;
    void SetSelectorGuns(int g)
    {
        if (g == selGunShown || gunMesh == null || gunMesh.Length == 0) return;
        selGunShown = g;
        if (vrSelFilter == null && vrSelectorModel != null) vrSelFilter = vrSelectorModel.GetComponent<MeshFilter>();
        if (deskSelFilter == null && deskSelectorModel != null) deskSelFilter = deskSelectorModel.GetComponent<MeshFilter>();
        if (vrSelFilter != null) vrSelFilter.sharedMesh = gunMesh[g];
        if (deskSelFilter != null) deskSelFilter.sharedMesh = gunMesh[g];
    }

    // ------------------------------------------------------------------ the hand
    // Where a weapon sits in a player's right hand, from the avatar's own hand bones (Cor, VR playtest: the weapons did
    // not line up with the hand; the controller's tracking angle differs per controller and avatar). The fist's
    // knuckle line runs from the little finger to the index finger; the hand points from the wrist to the middle
    // knuckle; the palm is on the side the fingers close towards.
    //   melee: the handle runs through the fist along the knuckle line, leaning a little forward, the head's broad
    //          side towards the knuckles
    //   gun:   the barrel along the hand, its top on the thumb side
    // HandPose fills handPos / handMelee / handGun and says whether the avatar has the bones (else: tracking data).
    [System.NonSerialized] public Vector3 handPos;
    [System.NonSerialized] public Quaternion handMelee, handGun;
    public float gripLean = 0.35f;
    [System.NonSerialized] public string handWhy = "";   // why HandPose last failed (the grip station shows it; logged once for this player in VR)
    bool handWhyLogged;     // how far the handle leans towards the fingers (0 = straight across the fist)

    public bool HandPose(VRCPlayerApi p, bool left)
    {
        if (p == null || !Utilities.IsValid(p)) return false;
        Vector3 h = p.GetBonePosition(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        Vector3 mid = p.GetBonePosition(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        Vector3 idx = p.GetBonePosition(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
        Vector3 lit = p.GetBonePosition(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
        if (h == Vector3.zero || mid == Vector3.zero || idx == Vector3.zero || lit == Vector3.zero)
        {
            handWhy = h == Vector3.zero ? "no hand bone" : "missing finger bones:" + (idx == Vector3.zero ? " index" : "") + (mid == Vector3.zero ? " middle" : "") + (lit == Vector3.zero ? " little" : "");
            return false;
        }
        handWhy = "";
        Vector3 fingers = mid - h, across = idx - lit;
        if (fingers.sqrMagnitude < 1e-6f || across.sqrMagnitude < 1e-6f) return false;
        float size = fingers.magnitude;
        fingers.Normalize(); across.Normalize();
        across = (across - fingers * Vector3.Dot(across, fingers));
        if (across.sqrMagnitude < 1e-6f) return false;
        across.Normalize();
        Vector3 palm = Vector3.Cross(fingers, across);   // (right hand: the palm side; the left hand's is the other way)
        if (left) palm = -palm;
        handPos = h + fingers * (size * 0.9f) + palm * (size * 0.45f);   // inside the closed fist
        Vector3 grip = (across + fingers * gripLean).normalized;
        handMelee = Quaternion.LookRotation(grip, fingers);
        handGun = Quaternion.LookRotation(fingers, across);
        return true;
    }

    // the hold before the player's own setting: the fist from the bones, else the controller's point and angle.
    // Fills basePos / baseRot; true when from the bones
    [System.NonSerialized] public Vector3 basePos;
    [System.NonSerialized] public Quaternion baseRot = Quaternion.identity;
    public bool BasePose(VRCPlayerApi p, bool left, bool gun)
    {
        if (HandPose(p, left)) { basePos = handPos; baseRot = gun ? handGun : handMelee; return true; }
        VRCPlayerApi.TrackingData t = p.GetTrackingData(left ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand);
        basePos = t.position;
        baseRot = t.rotation * Quaternion.Euler(gun ? (local != null ? local.vrAimEuler : Vector3.zero) : handEuler);
        return false;
    }

    // the weapon (gun false: the melee weapon) in player p's hand, with their own setting (PHHand): holdPos, holdRot,
    // holdSize (times the avatar's size); true when from the bones
    [System.NonSerialized] public Vector3 holdPos;
    [System.NonSerialized] public Quaternion holdRot = Quaternion.identity;
    [System.NonSerialized] public float holdSize = 1f;
    public bool Hold(VRCPlayerApi p, int slot, bool gun)
    {
        if (hand == null)
        {
            bool b = BasePose(p, false, gun);
            holdPos = basePos; holdRot = baseRot; holdSize = 1f;
            return b;
        }
        hand.Apply(p, slot, gun);
        holdPos = hand.holdPos; holdRot = hand.holdRot; holdSize = hand.holdSize;
        return hand.holdBones;
    }

    public int MeleeIndex(PHSlot s) { return s != null && s.meleeChoice >= 0 && s.meleeChoice < meleeMesh.Length ? s.meleeChoice : ModelNow(); }
    public int GunIndex(PHSlot s) { return GunOf(s); }

    // ------------------------------------------------------------------ grips
    public Vector3 MeleeGrip(int m) { return meleeGrip != null && m >= 0 && m < meleeGrip.Length ? meleeGrip[m] : Vector3.zero; }
    public Quaternion MeleeTurn(int m) { return meleeTurn != null && m >= 0 && m < meleeTurn.Length ? Quaternion.Euler(meleeTurn[m]) : Quaternion.identity; }
    public float MeleeSize(int m) { return meleeSize != null && m >= 0 && m < meleeSize.Length && meleeSize[m] > 0.01f ? meleeSize[m] : 1f; }
    public Vector3 GunGrip(int g) { return gunGrip != null && g >= 0 && g < gunGrip.Length ? gunGrip[g] : Vector3.zero; }
    public Quaternion GunTurn(int g) { return gunTurn != null && g >= 0 && g < gunTurn.Length ? Quaternion.Euler(gunTurn[g]) : Quaternion.identity; }
    public float GunSize(int g) { return gunSize != null && g >= 0 && g < gunSize.Length && gunSize[g] > 0.01f ? gunSize[g] : 1f; }

    // a melee model put in a hand: its grip point at 'at', turned by r and then by its own turn, at sc times its size
    public void PutMelee(Transform t, int m, Vector3 at, Quaternion r, float sc)
    {
        float s = sc * MeleeSize(m);
        Quaternion q = r * MeleeTurn(m);
        t.SetPositionAndRotation(at - q * (MeleeGrip(m) * s), q);
        t.localScale = Vector3.one * s;
    }

    // a gun model under its holder (the selector's 'gun', which sits at the hand): its grip at the holder's origin (plus
    // the kick), turned by its own turn, at its size
    public void PutGunModel(Transform model, int g, Vector3 kickPos, Quaternion kickRot, float mul)
    {
        float s = GunSize(g) * mul;
        Quaternion q = kickRot * GunTurn(g);
        model.localPosition = kickPos - q * (GunGrip(g) * s);
        model.localRotation = q;
        model.localScale = Vector3.one * s;
    }

    // a gun model on its own in a hand (the store's show, PHHand): its grip at 'at', turned by r and its own turn
    public void PutGun(Transform t, int g, Vector3 at, Quaternion r, float sc)
    {
        float s = sc * GunSize(g);
        Quaternion q = r * GunTurn(g);
        t.SetPositionAndRotation(at - q * (GunGrip(g) * s), q);
        t.localScale = Vector3.one * s;
    }

    // ------------------------------------------------------------------ the off hand (two-piece sets)
    bool HasOff(int i) { return heldOff != null && i < heldOff.Length && heldOff[i] != null; }

    // the model's off-hand piece onto slot i's heldOff (m -1, or a model without one: hidden), only on a change
    void ShowOff(int i, int m)
    {
        if (!HasOff(i)) return;
        int want = m >= 0 && offMesh != null && m < offMesh.Length && offMesh[m] != null ? m : -1;
        if (heldOffModel[i] == want) return;
        heldOffModel[i] = want;
        if (want >= 0) { heldOffFilter[i].sharedMesh = offMesh[want]; heldWear[i] = -1; }   // (its wear is set with the weapon's)
        heldOff[i].gameObject.SetActive(want >= 0);
    }

    // where player p holds the off-hand piece: the other hand, with the player's own hold mirrored (PHHand.ApplyOff)
    void HoldOff(VRCPlayerApi p, int slot)
    {
        if (hand != null) { hand.ApplyOff(p, slot); holdPos = hand.holdPos; holdRot = hand.holdRot; holdSize = hand.holdSize; return; }
        BasePose(p, true, false);
        holdPos = basePos; holdRot = baseRot; holdSize = 1f;
    }

    // the off-hand piece at the fist: its grip (the origin, as the drawing tool makes it) at 'at', at the weapon's size
    void PutOff(int i, Vector3 at, Quaternion r, float sc)
    {
        int m = heldOffModel[i];
        float s = sc * MeleeSize(m);
        heldOff[i].SetPositionAndRotation(at, r);
        heldOff[i].localScale = Vector3.one * s;
    }

    // ------------------------------------------------------------------ placing
    public float AvatarScale(VRCPlayerApi p)
    {
        float eye = p.GetAvatarEyeHeightAsMeters();
        return Mathf.Clamp(eye > 0.01f ? eye / 1.6f : 1f, 0.2f, 5f);
    }

    void PlaceOther(int i, VRCPlayerApi p)
    {
        Hold(p, i, false);
        Vector3 at = holdPos; Quaternion r = holdRot; float size = holdSize;
        // a desktop player's slash, seen from outside: a chop about the hand
        float t = Time.time - swingAt[i];
        if (t >= 0f && t < SWING_TIME) r = r * Quaternion.Euler(Chop(t), 0f, 0f);
        // (PHGame.layers: a hunter seen from another copy of the map holds it at their stand-in, PHLocal.UpdateStandIns)
        PutMelee(held[i], heldModel[i], game.SeenHere(i, at), r, AvatarScale(p) * size);
        if (heldOffModel[i] >= 0)
        {
            HoldOff(p, i);
            Quaternion ro = holdRot;
            if (t >= 0f && t < SWING_TIME) ro = ro * Quaternion.Euler(Chop(t), 0f, 0f);
            PutOff(i, game.SeenHere(i, holdPos), ro, AvatarScale(p) * holdSize);
        }
    }

    // the chop seen on a remote desktop player: up, then down hard, then back (degrees about the hand's x)
    float Chop(float t)
    {
        if (t < SWING_HIT_AT) return Mathf.Lerp(0f, -55f, t / SWING_HIT_AT);
        if (t < SWING_HIT_AT + 0.1f) return Mathf.Lerp(-55f, 70f, (t - SWING_HIT_AT) / 0.1f);
        return Mathf.Lerp(70f, 0f, (t - SWING_HIT_AT - 0.1f) / (SWING_TIME - SWING_HIT_AT - 0.1f));
    }

    void PlaceMine(int i, VRCPlayerApi me, int model)
    {
        float sc = AvatarScale(me);
        if (me.IsUserInVR())
        {
            // (Cor, VR: the weapon sat in the wrist; at the controller's point, say why the bones were not used)
            bool bones = Hold(me, i, false);
            if (!bones && !handWhyLogged) { handWhyLogged = true; Debug.Log("[prophunt] VR weapon held at the controller, not the hand bones: " + handWhy); }
            Vector3 vAt = holdPos; Quaternion r = holdRot; float size = holdSize;
            // a trigger slash (DesktopSwing): the weapon chops in the hand, and strikes as it comes down
            float tv = Time.time - mySwingAt;
            if (tv >= 0f && tv < SWING_TIME)
            {
                r = r * Quaternion.Euler(Chop(tv), 0f, 0f);
                if (!mySwingStruck && tv >= SWING_HIT_AT) { mySwingStruck = true; local.MeleeDesktopStrike(); }
            }
            PutMelee(held[i], model, vAt, r, sc * size);
            VrSwing(0, held[i], meleeTip[model], meleeHead[model], i, sc * size * MeleeSize(model));
            if (heldOffModel[i] >= 0)
            {
                HoldOff(me, i);
                Quaternion ro = holdRot;
                if (tv >= 0f && tv < SWING_TIME) ro = ro * Quaternion.Euler(Chop(tv), 0f, 0f);
                PutOff(i, holdPos, ro, sc * holdSize);
                // both pieces strike, but not a shield (Cor)
                if (!offShield[model]) VrSwing(1, heldOff[i], offTip[model], offHead[model], i, sc * holdSize * MeleeSize(model));
                else prevValid[1] = false;
            }
            else prevValid[1] = false;
            return;
        }
        VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 at = deskAt;
        Quaternion rot = Quaternion.Euler(deskEuler);
        float t = Time.time - mySwingAt;
        if (t >= 0f && t < SWING_TIME)
        {
            // the slash: raised up and to the right, then across and down to the left, then back to rest
            Vector3 upAt = new Vector3(0.34f, -0.02f, 0.58f), endAt = new Vector3(-0.24f, -0.42f, 0.62f);
            Quaternion upRot = Quaternion.Euler(-95f, 20f, 30f), endRot = Quaternion.Euler(5f, -60f, -35f);
            if (t < SWING_HIT_AT * 0.6f)
            {
                float k = Smooth(t / (SWING_HIT_AT * 0.6f));
                at = Vector3.Lerp(deskAt, upAt, k); rot = Quaternion.Slerp(rot, upRot, k);
            }
            else if (t < SWING_HIT_AT + 0.08f)
            {
                float k = Smooth((t - SWING_HIT_AT * 0.6f) / (SWING_HIT_AT * 0.4f + 0.08f));
                at = Vector3.Lerp(upAt, endAt, k); rot = Quaternion.Slerp(upRot, endRot, k);
            }
            else
            {
                float k = Smooth((t - SWING_HIT_AT - 0.08f) / (SWING_TIME - SWING_HIT_AT - 0.08f));
                at = Vector3.Lerp(endAt, deskAt, k); rot = Quaternion.Slerp(endRot, rot, k);
            }
            if (!mySwingStruck && t >= SWING_HIT_AT) { mySwingStruck = true; local.MeleeDesktopStrike(); }
        }
        PutMelee(held[i], model, head.position + head.rotation * (at * sc), head.rotation * rot, sc * deskScale);
        // the off hand's piece: in the view's lower left, as the weapon at rest is in the right (mirrored), still
        if (heldOffModel[i] >= 0)
            PutOff(i, head.position + head.rotation * (new Vector3(-deskAt.x, deskAt.y, deskAt.z) * sc),
                head.rotation * Quaternion.Euler(deskEuler.x, -deskEuler.y, -deskEuler.z), sc * deskScale);
        // sparks along the slash's arc
        if (t >= SWING_HIT_AT * 0.6f && t < SWING_HIT_AT + 0.08f && fxSpark != null)
        {
            fxSpark.transform.position = held[i].position + held[i].rotation * (Vector3.forward * meleeTip[model] * held[i].localScale.x);
            fxSpark.Emit(2);
        }
    }

    float Smooth(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

    // ------------------------------------------------------------------ swings
    // desktop: a click starts a slash (PHLocal.InputUse); it strikes a moment later, as the weapon comes across
    public bool DesktopSwing()
    {
        if (!local.CanStrike()) return false;   // out (the weapon broke), or not hunting: no slash, no sound
        if (Time.time - mySwingAt < SWING_TIME * 0.85f) return false;
        mySwingAt = Time.time;
        local.statSwings++;
        mySwingStruck = false;
        int mine = game.SlotOf(Networking.LocalPlayer.playerId);
        if (mine >= 0) { trailUntil[mine] = Time.time + SWING_HIT_AT + 0.12f; }
        sound.Play(Random.Range(0, 2) == 0 ? "knifeSlice" : "knifeSlice2", 0.55f);   // (a clicked slash is heard, Cor; a VR hand swing is not)
        return true;
    }

    // VR: the head of the weapon sweeps; a fast enough sweep strikes the first thing it passes through
    void VrSwing(int pc, Transform w, float tipZ, float headZ, int i, float sc)
    {
        Vector3 tip = w.position + w.rotation * (Vector3.forward * tipZ * sc);
        Vector3 mid = w.position + w.rotation * (Vector3.forward * (headZ + tipZ) * 0.5f * sc);
        // the reach beyond the tip, short of anything solid along the weapon (looked for from the hand, which never
        // sits inside a wall, so a tip stuck in one does not reach out of its far side)
        Vector3 fwd = w.rotation * Vector3.forward;
        float tipLen = tipZ * sc, extra = SWING_EXTRA * sc;
        RaycastHit eh;
        if (Physics.Raycast(w.position, fwd, out eh, tipLen + extra + SWING_RADIUS * sc, MASK, QueryTriggerInteraction.Ignore))
            extra = Mathf.Max(0f, eh.distance - tipLen - SWING_RADIUS * sc - 0.02f);
        Vector3 ext = tip + fwd * extra;
        float dt = Mathf.Max(Time.deltaTime, 0.004f);
        if (prevValid[pc])
        {
            Vector3 d = tip - prevTip[pc];
            float speed = d.magnitude / dt;
            bool fast = speed > SWING_SPEED * sc;
            if (fast) trailUntil[i] = Time.time + 0.12f;
            // the whoosh (Cor: it played whenever the weapon moved): only a real swing, and once for it; it is heard
            // again only after the hand has slowed right down
            float handSpeed = (w.position - prevGrip[pc]).magnitude / dt;
            if (pc == 0 && whooshArmed && speed > WHOOSH_SPEED * sc && handSpeed > WHOOSH_HAND * sc)
            {
                whooshArmed = false;   // (a real swing; no sound for it any more, Cor's group playtest)
            }
            else if (pc == 0 && speed < WHOOSH_REARM * sc) whooshArmed = true;
            // (not while a trigger slash chops the weapon in the hand: that one strikes by itself, and the chop's own
            // speed would strike again, a hider hit twice)
            if (fast && Time.time >= nextVrHit && Time.time - mySwingAt >= SWING_TIME && local.CanStrike())
            {
                // (hop playtest, Cor: low props were hard to hit by hand) three sweeps: the tip, the head's middle,
                // and a point SWING_EXTRA beyond the tip (kept short of anything solid in front of the tip, so it
                // never reaches through a wall); a hider's prop beats a map prop, which beats a wall or the floor
                float rad = SWING_RADIUS * sc;
                strikeC = null; strikeK = 0;
                SweepFor(prevTip[pc], tip, rad);
                SweepFor(prevMid[pc], mid, rad);
                SweepFor(prevExt[pc], ext, rad);
                // the floor or a wall right by a low prop: the prop the swing was meant for (PHLocal.Assist)
                if (strikeK == 1 && local.Assist(strikeP, strikeN, 0.25f * sc)) { strikeC = local.assistCol; strikeP = local.assistPoint; strikeK = 2; }
                if (strikeC != null)
                {
                    nextVrHit = Time.time + VR_COOLDOWN;
                    local.MeleeStrike(strikeC, strikeP, d.normalized, true);
                }
            }
        }
        prevTip[pc] = tip; prevMid[pc] = mid; prevExt[pc] = ext; prevGrip[pc] = w.position; prevValid[pc] = true;
    }

    // one sweep of the swing (from where a point of the weapon was last frame to where it is now): keeps the best
    // thing met so far in strikeC / strikeP / strikeN / strikeK (PHLocal.StrikeKind: 3 hider, 2 prop, 1 other)
    Collider strikeC;
    Vector3 strikeP, strikeN;
    int strikeK;

    void SweepFor(Vector3 from, Vector3 to, float rad)
    {
        Vector3 d = to - from;
        if (d.sqrMagnitude < 1e-6f) return;
        RaycastHit h;
        if (!Physics.SphereCast(from, rad, d.normalized, out h, d.magnitude, MASK, QueryTriggerInteraction.Ignore)) return;
        int k = local.StrikeKind(h.collider);
        if (k <= strikeK) return;
        strikeK = k; strikeC = h.collider; strikeP = h.point; strikeN = h.normal;
    }

    // a swing's outcome, on every client (PHSlot.Swung): kind 0 a desktop slash that met nothing, 1 a wall or
    // the like, 2 a prop, 3 a hider. A desktop player's weapon is seen to slash.
    public void OnSwung(int slot, Vector3 point, int kind)
    {
        Setup();
        point = game.builder.ToWorld(point);   // (sent map-local: PHLocal.MeleeStrike)
        VRCPlayerApi p = slot >= 0 && slot < game.slotPlayer.Length ? VRCPlayerApi.GetPlayerById(game.slotPlayer[slot]) : null;
        bool mine = p != null && p.isLocal;
        if (!mine && p != null && Utilities.IsValid(p) && !p.IsUserInVR() && slot < swingAt.Length)
        {
            swingAt[slot] = Time.time;
            trailUntil[slot] = Time.time + SWING_HIT_AT + 0.12f;
        }
        if (kind == 1) Burst(fxSpark, point, 10);
        else if (kind == 2) { Burst(fxDust, point, 8); Burst(fxChips, point, 10); sound.PlayAt("chop", point, 14f, 0.8f, Random.Range(0.92f, 1.08f)); }
        else if (kind == 3) { Burst(fxHider, point, 24); Burst(fxSpark, point, 8); sound.PlayAt("impactPunch_medium_000", point, 16f, 0.9f, 1f); }
    }

    void Burst(ParticleSystem ps, Vector3 at, int n)
    {
        if (ps == null) return;
        ps.transform.position = at;
        ps.Emit(n);
    }

    // ------------------------------------------------------------------ wear
    // the wear the shader draws: 0 whole .. 1 used up (the hunter's weapon condition, PHSlot.hunterHealth)
    void SetWear(MeshRenderer r, int model, int condition, bool recovering)
    {
        float lost = Mathf.Clamp01((PHLocal.HUNTER_HEALTH - condition) / (float)PHLocal.HUNTER_HEALTH);
        r.GetPropertyBlock(block);
        bool grid = meleeWear != null && model < meleeWear.Length && meleeWear[model] != null;
        bool card = meleeCard != null && model < meleeCard.Length && meleeCard[model];   // (worn by the atlas: CardWeaponsWear)
        if (grid)
        {
            block.SetTexture(idWearTex, meleeWear[model]);
            block.SetVector(idWearLo, meleeWearLo[model]);
            block.SetVector(idWearInv, meleeWearInv[model]);
        }
        block.SetFloat(idWear, grid || card ? lost : 0f);
        block.SetFloat(idDither, recovering ? 1f : 0f);   // growing back: see-through dither (Cor)
        r.SetPropertyBlock(block);
    }

    // the k-th bite's spot on a model, the same on every client for this slot and round
    int SpotOf(int slot, PHSlot s, int k)
    {
        int h = (slot * 7919 + (s.round + 13) * 104729 + k * 2654435) & 0x7fffffff;
        h = (h ^ (h >> 13)) * 1274126177 & 0x7fffffff;
        return h % SPOTS;
    }

    // a bite comes off: bits fly from a spot on the head, with a small crack
    void ChipOff(int i, PHSlot s, int model)
    {
        Vector3 c = chipSpots[model * SPOTS + SpotOf(i, s, PHLocal.HUNTER_HEALTH - s.hunterHealth)];
        Vector3 at = held[i].position + held[i].rotation * (c * held[i].localScale.x);
        Burst(fxChips, at, 6);
        Burst(fxWear, at, 5);   // (seen by everyone: every client follows the synced condition)
        sound.PlayAt("impactMetal_light_002", at, 8f, 0.6f, 1.2f);
    }

    // the weapon breaks and stays in the hand to grow back: the pieces and the crack only
    void ShatterFx(int i)
    {
        int model = heldModel[i];
        float sc = held[i].localScale.x;
        for (int k = 0; k < 4; k++)
            Burst(fxChips, held[i].position + held[i].rotation * (Vector3.forward * meleeTip[model] * sc * (0.25f + 0.25f * k)), 10);
        sound.PlayAt("impactMetal_heavy_000", held[i].position, 18f, 1f, 1f);
    }

    // the weapon breaks: pieces all along it, a loud crack; then it is gone
    void Shatter(int i)
    {
        int model = heldModel[i];
        float sc = held[i].localScale.x;
        for (int k = 0; k < 4; k++)
            Burst(fxChips, held[i].position + held[i].rotation * (Vector3.forward * meleeTip[model] * sc * (0.25f + 0.25f * k)), 10);
        sound.PlayAt("impactMetal_heavy_000", held[i].position, 18f, 1f, 1f);
        held[i].gameObject.SetActive(false);
        heldModel[i] = -1;
        ShowOff(i, -1);
    }

    // ------------------------------------------------------------------ the hider's selector
    // Use kicks it back with a flash (PHLocal.InputUse)
    public void FireSelector()
    {
        kickAt = Time.time;
        sound.Play("laserSmall_000", 0.35f);
        VRCPlayerApi me = Networking.LocalPlayer;
        Transform model = me.IsUserInVR() ? vrSelectorModel : deskSelectorModel;
        if (model != null && fxMuzzle != null)
        {
            fxMuzzle.transform.position = model.position + model.rotation * (Vector3.forward * Muzzle(game.LocalSlot()) * model.lossyScale.x);
            fxMuzzle.Emit(6);
        }
    }

    // VR: the end of the selector's barrel in the world, as it is drawn now (the laser and the pick ray start there)
    public Vector3 VrMuzzlePoint()
    {
        int g = GunOf(game.LocalSlot());
        Vector3 local = gunMuzzlePoint != null && g >= 0 && g < gunMuzzlePoint.Length ? gunMuzzlePoint[g] : new Vector3(0f, 0f, Muzzle(game.LocalSlot()));
        return vrSelectorModel.TransformPoint(local);
    }

    // desktop: the same for the selector in the corner of the view
    public Vector3 DeskMuzzlePoint()
    {
        int g = GunOf(game.LocalSlot());
        Vector3 local = gunMuzzlePoint != null && g >= 0 && g < gunMuzzlePoint.Length ? gunMuzzlePoint[g] : new Vector3(0f, 0f, Muzzle(game.LocalSlot()));
        return deskSelectorModel.TransformPoint(local);
    }

    // VR: how far the muzzle is in front of the selector's grip, in metres as it is drawn now
    public float VrMuzzleLen()
    {
        if (vrSelectorModel == null) return selectorMuzzle;
        return Muzzle(game.LocalSlot()) * vrSelectorModel.lossyScale.x;
    }

    float Muzzle(PHSlot s) { int g = GunOf(s); return gunMuzzle != null && g < gunMuzzle.Length ? gunMuzzle[g] : selectorMuzzle; }

    void UpdateSelector(VRCPlayerApi me, int mine)
    {
        PHSlot ms = mine >= 0 && mine < game.slots.Length ? game.slots[mine] : null;
        int gm = GunOf(ms);
        SetSelectorGuns(gm);
        // the kick: back and up at once, then home again over a quarter second
        float t = Time.time - kickAt;
        float k = t < 0.25f ? 1f - Smooth(t / 0.25f) : 0f;
        Vector3 kickPos = new Vector3(0f, 0.01f * k, -0.05f * k);
        Quaternion kickRot = Quaternion.Euler(-18f * k, 0f, 0f);
        float mineSize = 1f;   // (VR: the player's own size for the gun, PHHand)
        if (vrSelectorModel != null && hand != null && me.IsUserInVR()) { hand.Apply(me, mine, true); mineSize = hand.holdSize; }
        if (vrSelectorModel != null) PutGunModel(vrSelectorModel, gm, kickPos, kickRot, mineSize);
        if (deskSelector == null) return;
        bool show = !me.IsUserInVR() && local.ShowsSelector();
        if (deskSelector.gameObject.activeSelf != show) deskSelector.gameObject.SetActive(show);
        if (!show) return;
        VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        float sc = AvatarScale(me);
        deskSelector.SetPositionAndRotation(head.position + head.rotation * (deskSelectorAt * sc), head.rotation * Quaternion.Euler(deskSelectorEuler));
        deskSelector.localScale = Vector3.one * sc;
        PutGunModel(deskSelectorModel, gm, kickPos, kickRot, 1f);
    }
}
