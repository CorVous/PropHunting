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

    // ---- the models (WeaponBuilder, from Resources/PropHunt/weapons.bytes)
    public string[] meleeId;           // Tools/weapon_catalog.json ids ("melee:<pack>:<model>")
    public Mesh[] meleeMesh;
    public string[] gunId;
    public Mesh[] gunMesh;             // the selectors (the hider's gun); gunMuzzle: the muzzle's z
    public float[] gunMuzzle;
    public float[] meleeTip;           // the head's far end along +Z (metres, grip at 0)
    public float[] meleeHead;          // where the head begins along +Z (the swing sweeps from here to the tip)
    public Vector3[] chipSpots;        // SPOTS per model, on the head's surface, in the model's own space
    public Color[] meleeColor;         // the head's main colour (the chips that fly off)
    public int[] themeMelee = { 0, 2, 1 };   // the model by map theme (GenData.THEME_NONE, SPACE, MEDIEVAL)
    public const int SPOTS = 24, CHIPS = 12;

    // ---- one held weapon per slot (made by WeaponBuilder; no colliders)
    public Transform[] held;
    public MeshFilter[] heldFilter;
    public MeshRenderer[] heldRenderer;
    public TrailRenderer[] heldTrail;  // at the tip; it draws only while the weapon swings

    // ---- effects (world space particle systems, moved where they are needed and told to emit)
    public ParticleSystem fxDust;      // a prop struck: a puff
    public ParticleSystem fxChips;     // ...and splinters; also the chunks off a weapon, and the pieces of a broken one
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
    public const float GUN_SCALE = 1.8f, BULLET_SPEED = 40f;

    // ---- how weapons sit and move (tunable)
    public Vector3 handEuler = new Vector3(-50f, 0f, 0f);     // VR and remote players: the weapon against the hand's tracking
    public Vector3 deskAt = new Vector3(0.30f, -0.40f, 0.62f); // desktop: in the view (metres at avatar size 1)
    public Vector3 deskEuler = new Vector3(-40f, -25f, 15f);
    public const float SWING_TIME = 0.42f, SWING_HIT_AT = 0.12f, SWING_SPEED = 2.4f, VR_COOLDOWN = 0.45f;
    public const float REACH = 2.2f;   // desktop: how far in front of the eyes a slash strikes (metres at avatar size 1)
    public const int MASK = ~(1 << 9 | 1 << 10 | 1 << 18);    // as PHLocal.Aim: not players

    int[] heldModel;                   // the model each held weapon shows (-1 hidden)
    int[] heldChips;                   // the wear it was last drawn with
    bool[] heldOut;
    float[] swingAt;                   // when its last slash began (desktop players, seen by everyone)
    float[] trailUntil;
    MaterialPropertyBlock block;
    int[] chipIds;
    Vector4[] chipVals;
    bool ready;

    // the local VR swing
    Vector3 prevTip, prevMid;
    bool prevValid;
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
    Vector3[] bFrom, bTo;
    float[] bStart, bTime;
    int[] bHit;
    int nextBullet;

    void Start() { Setup(); }

    void Setup()
    {
        if (ready) return;
        ready = true;
        int n = held.Length;
        heldModel = new int[n]; heldChips = new int[n]; heldOut = new bool[n]; swingAt = new float[n]; trailUntil = new float[n];
        for (int i = 0; i < n; i++) { heldModel[i] = -2; heldChips[i] = -1; swingAt[i] = -99f; }
        block = new MaterialPropertyBlock();
        chipIds = new int[CHIPS];
        chipVals = new Vector4[CHIPS];
        for (int k = 0; k < CHIPS; k++) chipIds[k] = VRCShader.PropertyToID("_Chip" + k);
        for (int i = 0; i < n; i++) { held[i].gameObject.SetActive(false); heldTrail[i].emitting = false; }
        if (deskSelector != null) deskSelector.gameObject.SetActive(false);
        shotAt = new float[n]; shotEnd = new Vector3[n]; gunShown = new Vector3[n];
        for (int i = 0; i < n; i++) { shotAt[i] = -99f; if (hiderGun != null && i < hiderGun.Length) hiderGun[i].gameObject.SetActive(false); }
        int nb = bullets != null ? bullets.Length : 0;
        bFrom = new Vector3[nb]; bTo = new Vector3[nb]; bStart = new float[nb]; bTime = new float[nb]; bHit = new int[nb];
        for (int b = 0; b < nb; b++) { bullets[b].gameObject.SetActive(false); bTime[b] = -1f; }
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
        int model = ModelNow();
        for (int i = 0; i < held.Length; i++)
        {
            PHSlot s = i < game.slots.Length ? game.slots[i] : null;
            int pid = game.slotPlayer[i];
            bool show = round && s != null && pid > 0 && game.RoleOf(i) == PHGame.ROLE_HUNTER && s.round == game.round;
            VRCPlayerApi p = show ? VRCPlayerApi.GetPlayerById(pid) : null;
            if (p == null || !Utilities.IsValid(p)) show = false;
            // the break: everyone sees it fall apart where it was
            if (show && s.hunterOut != heldOut[i])
            {
                heldOut[i] = s.hunterOut;
                if (s.hunterOut && heldModel[i] >= 0) Shatter(i);
                if (s.hunterOut && i == watchSlot && Time.time < watchUntil) { local.statWeaponsBroken++; watchSlot = -1; }
            }
            UpdateHiderGun(i, s, i == mine);
            if (show && s.hunterOut) show = false;
            if (!show)
            {
                if (heldModel[i] != -1) { heldModel[i] = -1; held[i].gameObject.SetActive(false); heldTrail[i].emitting = false; }
                if (s == null || s.round != game.round) heldOut[i] = false;
                continue;
            }
            int mm = s.meleeChoice >= 0 && s.meleeChoice < meleeMesh.Length ? s.meleeChoice : model;
            if (heldModel[i] != mm)
            {
                heldModel[i] = mm;
                heldFilter[i].sharedMesh = meleeMesh[mm];
                heldChips[i] = -1;
                held[i].gameObject.SetActive(true);
                heldTrail[i].Clear();
            }
            int chips = ChipsFor(s.hunterHealth);
            if (chips != heldChips[i])
            {
                bool more = heldChips[i] >= 0 && chips > heldChips[i];
                heldChips[i] = chips;
                SetChips(i, s, mm, chips, s.hunterHealth);
                if (more) ChipOff(i, s, mm, chips - 1);
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
                SendCustomEventDelayedSeconds(nameof(_HideBullets), 0.4f);
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
            if (bTime[b] < 0f && bullets[b].gameObject.activeSelf && Time.time - bStart[b] > 0.3f) bullets[b].gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ the final stand
    // a hunter this player just shot: if their weapon breaks within a moment, this player broke it (PHLocal.statWeaponsBroken)
    int watchSlot = -1;
    float watchUntil;
    public void WatchBreak(int slot) { watchSlot = slot; watchUntil = Time.time + 2.5f; }

    // a hider's gun, at their prop: above its top, turned as the prop faces, or at what it last shot for a moment
    void UpdateHiderGun(int i, PHSlot s, bool mine)
    {
        if (hiderGun == null || i >= hiderGun.Length) return;
        bool show = s != null && !mine && s.armed && !s.caught && s.def >= 0 && game.phase == PHGame.PH_HUNT
            && game.RoleOf(i) == PHGame.ROLE_HIDER && s.round == game.round;
        Transform g = hiderGun[i];
        if (g.gameObject.activeSelf != show)
        {
            g.gameObject.SetActive(show);
            if (show) gunShown[i] = GunPlace(s);
        }
        if (!show) return;
        if (g.childCount > 0) SetGunMesh(g.GetChild(0), GunOf(s));
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

    Vector3 GunPlace(PHSlot s)
    {
        float h = s.def >= 0 && s.def < game.gen.defSy.Length ? game.gen.defSy[s.def] / 1000f : 1f;
        return s.propPos + Vector3.up * (h + 0.25f);
    }

    // a final-stand shot, on every client (PHSlot.Shot): the flash, and a bullet with a trail that sparks where it lands
    public void OnShot(int slot, Vector3 end, int hitSlot)
    {
        Setup();
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
            if (hiderGun != null && !hiderGun[slot].gameObject.activeSelf) { hiderGun[slot].gameObject.SetActive(true); gunShown[slot] = GunPlace(s); }
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

    // ------------------------------------------------------------------ placing
    float AvatarScale(VRCPlayerApi p)
    {
        float eye = p.GetAvatarEyeHeightAsMeters();
        return Mathf.Clamp(eye > 0.01f ? eye / 1.6f : 1f, 0.2f, 5f);
    }

    void PlaceOther(int i, VRCPlayerApi p)
    {
        VRCPlayerApi.TrackingData rh = p.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
        Quaternion r = rh.rotation * Quaternion.Euler(handEuler);
        // a desktop player's slash, seen from outside: a chop about the hand
        float t = Time.time - swingAt[i];
        if (t >= 0f && t < SWING_TIME) r = r * Quaternion.Euler(Chop(t), 0f, 0f);
        held[i].SetPositionAndRotation(rh.position, r);
        held[i].localScale = Vector3.one * AvatarScale(p);
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
        held[i].localScale = Vector3.one * sc;
        if (me.IsUserInVR())
        {
            VRCPlayerApi.TrackingData rh = me.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
            held[i].SetPositionAndRotation(rh.position, rh.rotation * Quaternion.Euler(handEuler));
            VrSwing(i, model, sc);
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
        held[i].SetPositionAndRotation(head.position + head.rotation * (at * sc), head.rotation * rot);
        // sparks along the slash's arc
        if (t >= SWING_HIT_AT * 0.6f && t < SWING_HIT_AT + 0.08f && fxSpark != null)
        {
            fxSpark.transform.position = held[i].position + held[i].rotation * (Vector3.forward * meleeTip[model] * sc);
            fxSpark.Emit(2);
        }
    }

    float Smooth(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

    // ------------------------------------------------------------------ swings
    // desktop: a click starts a slash (PHLocal.InputUse); it strikes a moment later, as the weapon comes across
    public bool DesktopSwing()
    {
        if (Time.time - mySwingAt < SWING_TIME * 0.85f) return false;
        mySwingAt = Time.time;
        local.statSwings++;
        mySwingStruck = false;
        int mine = game.SlotOf(Networking.LocalPlayer.playerId);
        if (mine >= 0) { trailUntil[mine] = Time.time + SWING_HIT_AT + 0.12f; }
        sound.Play(Random.Range(0, 2) == 0 ? "knifeSlice" : "knifeSlice2", 0.55f);
        return true;
    }

    // VR: the head of the weapon sweeps; a fast enough sweep strikes the first thing it passes through
    void VrSwing(int i, int model, float sc)
    {
        Transform w = held[i];
        Vector3 tip = w.position + w.rotation * (Vector3.forward * meleeTip[model] * sc);
        Vector3 mid = w.position + w.rotation * (Vector3.forward * (meleeHead[model] + meleeTip[model]) * 0.5f * sc);
        float dt = Mathf.Max(Time.deltaTime, 0.004f);
        if (prevValid)
        {
            Vector3 d = tip - prevTip;
            float speed = d.magnitude / dt;
            bool fast = speed > SWING_SPEED * sc;
            if (fast)
            {
                if (Time.time >= trailUntil[i]) sound.PlayAt(Random.Range(0, 2) == 0 ? "knifeSlice" : "knifeSlice2", tip, 6f, 0.35f, 1f);
                trailUntil[i] = Time.time + 0.12f;
            }
            if (fast && Time.time >= nextVrHit && local.CanStrike())
            {
                RaycastHit h;
                bool any = Physics.SphereCast(prevTip, 0.06f * sc, d.normalized, out h, d.magnitude, MASK, QueryTriggerInteraction.Ignore);
                if (!any)
                {
                    Vector3 dm = mid - prevMid;
                    if (dm.sqrMagnitude > 1e-6f) any = Physics.SphereCast(prevMid, 0.06f * sc, dm.normalized, out h, dm.magnitude, MASK, QueryTriggerInteraction.Ignore);
                }
                if (any)
                {
                    nextVrHit = Time.time + VR_COOLDOWN;
                    local.MeleeStrike(h.collider, h.point, d.normalized, true);
                }
            }
        }
        prevTip = tip; prevMid = mid; prevValid = true;
    }

    // a swing's outcome, on every client (PHSlot.Swung): kind 0 a desktop slash that met nothing, 1 a wall or
    // the like, 2 a prop, 3 a hider. A desktop player's weapon is seen to slash.
    public void OnSwung(int slot, Vector3 point, int kind)
    {
        Setup();
        VRCPlayerApi p = slot >= 0 && slot < game.slotPlayer.Length ? VRCPlayerApi.GetPlayerById(game.slotPlayer[slot]) : null;
        bool mine = p != null && p.isLocal;
        if (!mine && p != null && Utilities.IsValid(p) && !p.IsUserInVR() && slot < swingAt.Length)
        {
            swingAt[slot] = Time.time;
            trailUntil[slot] = Time.time + SWING_HIT_AT + 0.12f;
            sound.PlayAt(Random.Range(0, 2) == 0 ? "knifeSlice" : "knifeSlice2", p.GetPosition() + Vector3.up, 8f, 0.4f, 1f);
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
    // chunks shown for a condition: none while whole, one at the first loss, up to CHIPS just before it breaks
    public int ChipsFor(int condition)
    {
        int lost = Mathf.Clamp(PHLocal.HUNTER_HEALTH - condition, 0, PHLocal.HUNTER_HEALTH);
        if (lost <= 0) return 0;
        return Mathf.Clamp(Mathf.CeilToInt(lost * (float)CHIPS / PHLocal.HUNTER_HEALTH), 1, CHIPS);
    }

    // the k-th chunk's spot on a model, the same on every client for this slot and round
    int SpotOf(int slot, PHSlot s, int k)
    {
        int h = (slot * 7919 + (s.round + 13) * 104729 + k * 2654435) & 0x7fffffff;
        h = (h ^ (h >> 13)) * 1274126177 & 0x7fffffff;
        return h % SPOTS;
    }

    void SetChips(int i, PHSlot s, int model, int chips, int condition)
    {
        float len = meleeTip[model] - meleeHead[model];
        // the chunks grow with the wear: small first bites, bigger as it nears breaking
        float lost = Mathf.Clamp01((PHLocal.HUNTER_HEALTH - condition) / (float)PHLocal.HUNTER_HEALTH);
        for (int k = 0; k < CHIPS; k++)
        {
            if (k >= chips) { chipVals[k] = Vector4.zero; continue; }
            Vector3 c = chipSpots[model * SPOTS + SpotOf(i, s, k)];
            float r = Mathf.Max(0.025f, len * (0.13f + 0.12f * lost) * (0.8f + 0.4f * ((k * 37) % 10) / 10f));
            chipVals[k] = new Vector4(c.x, c.y, c.z, r);
        }
        heldRenderer[i].GetPropertyBlock(block);
        for (int k = 0; k < CHIPS; k++) block.SetVector(chipIds[k], chipVals[k]);
        heldRenderer[i].SetPropertyBlock(block);
    }

    // a chunk comes off: bits of the weapon's colour fly from it, with a small crack
    void ChipOff(int i, PHSlot s, int model, int k)
    {
        Vector3 c = chipSpots[model * SPOTS + SpotOf(i, s, k)];
        Vector3 at = held[i].position + held[i].rotation * (c * held[i].localScale.x);
        Burst(fxChips, at, 6);
        sound.PlayAt("impactMetal_light_002", at, 8f, 0.6f, 1.2f);
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

    float Muzzle(PHSlot s) { int g = GunOf(s); return gunMuzzle != null && g < gunMuzzle.Length ? gunMuzzle[g] : selectorMuzzle; }

    void UpdateSelector(VRCPlayerApi me, int mine)
    {
        PHSlot ms = mine >= 0 && mine < game.slots.Length ? game.slots[mine] : null;
        int gm = GunOf(ms);
        SetGunMesh(vrSelectorModel, gm);
        SetGunMesh(deskSelectorModel, gm);
        // the kick: back and up at once, then home again over a quarter second
        float t = Time.time - kickAt;
        float k = t < 0.25f ? 1f - Smooth(t / 0.25f) : 0f;
        Vector3 kickPos = new Vector3(0f, 0.01f * k, -0.05f * k);
        Quaternion kickRot = Quaternion.Euler(-18f * k, 0f, 0f);
        if (vrSelectorModel != null) vrSelectorModel.localPosition = kickPos;
        if (vrSelectorModel != null) vrSelectorModel.localRotation = kickRot;
        if (deskSelector == null) return;
        bool show = !me.IsUserInVR() && local.ShowsSelector();
        if (deskSelector.gameObject.activeSelf != show) deskSelector.gameObject.SetActive(show);
        if (!show) return;
        VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        float sc = AvatarScale(me);
        deskSelector.SetPositionAndRotation(head.position + head.rotation * (deskSelectorAt * sc), head.rotation * Quaternion.Euler(deskSelectorEuler));
        deskSelector.localScale = Vector3.one * sc;
        deskSelectorModel.localPosition = kickPos;
        deskSelectorModel.localRotation = kickRot;
    }
}
