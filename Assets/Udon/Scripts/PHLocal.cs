using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using TMPro;

// The local player's side of the game (the prototype's Player and PlayerActions, without its own movement:
// VRChat moves the player). One per world; it acts for whoever is running this client.
//   Hider: Use (desktop click, VR right trigger) on a prop within 4 m becomes it (G5, G25); the avatar shrinks
//          to the prop's eye height, sits in its parked station and the synced prop follows them. The VR left
//          trigger turns the prop (PHHiderBody). Taunt: T on desktop, the button on the screen device in VR (PHView).
//   Hunter: Use fires the tagger (05 section 5.3): a hider's prop takes a hit (sent to its owner), a real prop
//          costs health (G17) and gets knocked, anything else is a miss. Hunters walk through props (G45).
//   Everyone: frozen during the reveal; hunters wait in the lobby during BRIEF and HIDE. Hiders are free from the
//   moment they land on the map (BRIEF), so the hiding time is BRIEF + HIDE.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHLocal : UdonSharpBehaviour
{
    public PHGame game;
    public TextMeshProUGUI hud;          // head-locked text
    public Transform hudRoot;
    public PHAudio sound;
    public PHDoors doors;                // openable props' doors and lids
    public const float HANDLE_RANGE = 1.5f, HUNTER_HANDLE_RANGE = 2.5f, GRAB_REACH = 0.25f;
    public PHHiderBody body;             // the hider's prop-shaped body (custom controller in the station)
    public TextMeshPro panelText;
    public TextMeshProUGUI panelUiText;  // the same, on the host panel's canvas
    public TextMeshPro resultsText;      // the last round's results, on the lobby wall
    public Material beaconGreen, beaconRed;
    public string[] defNames;            // readable names of the defs ("k_chairCushion" -> "Chair cushion"), baked        // the host panel's settings, for everyone to read
    float nextPanel;

    // ---- balance (Balance.cs)
    public const float TAKE_RANGE = 4f, DART_RANGE = 25f, DART_INTERVAL = 0.5f, WRONG_SAME_WINDOW = 5f, TAUNT_COOLDOWN = 8f;
    public const int DART_DAMAGE = 25, HIT_REWARD = 5, HUNTER_HEALTH = 100, HUNT_CHANGES = 3;
    public const float HUNT_CHANGE_COOLDOWN = 5f;
    public const int LAYER_PROPS_SOLID = 11, LAYER_PROPS_WALKTHROUGH = 17;
    readonly int[] hiderHealth = { 50, 100, 150, 200 };

    int role, slotIndex = -1;
    float nextDart, lastChange = -99f, lastTaunt = -99f;
    int changesLeft;
    float originalEye = -1f;
    int lastCharged = -1;
    float lastChargedAt = -99f;
    string notice = "";
    float noticeUntil;
    int propLayer = -1;
    bool wasEndgame;
    // forced taunts (6.4, G47): every hider's own clock from the release of the hunters; a hider that has moved
    // 4 m (straight line) since the last check skips it
    float nextForced = -1f;
    bool warned;
    Vector3 checkPos;
    public const float FORCED_WARNING = 3f, FORCED_MOVED = 4f;

    // the slot the host gave this player (it can arrive after the phase that needs it)
    PHSlot Me() { slotIndex = game.SlotOf(Networking.LocalPlayer.playerId); return slotIndex >= 0 ? game.slots[slotIndex] : null; }

    void Say(string text, float seconds) { notice = text; noticeUntil = Time.time + seconds; }

    // ------------------------------------------------------------------ phases (called by PHGame on every client)
    public void OnPhase(int phase)
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        slotIndex = game.SlotOf(me.playerId);
        PHSlot s = Me();
        role = game.RoleOf(slotIndex);
        if (s != null) s.Claim();
        switch (phase)
        {
            case PHGame.PH_LOBBY:
                ShowReveal(false);
                if (s != null) s.StopHiding();
                body.End();
                RestoreEye();
                SetPropLayer(LAYER_PROPS_SOLID);
                SetFrozen(false);
                if (role == PHGame.ROLE_NONE && game.lobbySpawn != null && game.round > 0) me.TeleportTo(game.lobbySpawn.position, game.lobbySpawn.rotation);
                break;
            case PHGame.PH_LOADING:
                sound.Play("confirmation_001", 0.6f);
                if (s != null && role != PHGame.ROLE_NONE) s.ResetFor(game.round, role);
                Say(role == PHGame.ROLE_NONE ? "A round is starting. You are watching." : "Building the map...", 5f);
                break;
            case PHGame.PH_BRIEF:
                sound.Play("ready", 0.8f);
                if (role == PHGame.ROLE_HUNTER)
                {
                    // hunters wait in the lobby while the hiders hide (Cor); the hunt brings them to the release
                    SetPropLayer(LAYER_PROPS_WALKTHROUGH);
                    SetFrozen(false);
                    Say("You hunt. Wait in the lobby while the hiders hide.", 6f);
                }
                else if (role == PHGame.ROLE_HIDER)
                {
                    SetPropLayer(LAYER_PROPS_SOLID);
                    Vector3 at = game.builder.ReleasePoint();
                    if (game.gen.nSpawns > 0)
                    {
                        int c = game.gen.spawnCell[Random.Range(0, game.gen.nSpawns)];
                        at = game.builder.InnerWorld(c);   // upstairs or in a basement too
                    }
                    float facing = Random.Range(0, 24) * 15f;
                    me.TeleportTo(at + Vector3.up * 0.1f, Quaternion.Euler(0f, facing, 0f));
                    body.Begin(at, facing);
                    body.frozen = false;   // a hider moves, picks and opens doors the moment they land (Cor)
                    changesLeft = HUNT_CHANGES;
                    StartingDisguise();
                    if (s != null) s.StartHiding();
                    Say("You hide. Look at a prop and click (trigger) to become it.", 6f);
                }
                break;
            case PHGame.PH_HIDE:
                if (role == PHGame.ROLE_HIDER) body.frozen = false;
                break;
            case PHGame.PH_HUNT:
                PlayThen("go", 0.8f, "jingles_PIZZI07", 0.5f);
                if (role == PHGame.ROLE_HUNTER)
                {
                    me.TeleportTo(game.builder.ReleasePoint() + Vector3.up * 0.1f, Quaternion.Euler(0f, game.builder.ReleaseYaw(), 0f));
                    SetFrozen(false);
                    Say("Hunt! Find the props that are hiders.", 3f);
                }
                else if (role == PHGame.ROLE_HIDER)
                {
                    Say("The hunters are released!", 3f);
                    nextForced = Time.time + ForcedInterval() * Random.Range(0.35f, 1f);
                    checkPos = me.GetPosition(); warned = false;
                }
                break;
            case PHGame.PH_END:
                ShowReveal(true);
                // "time over" only when the clock ended it, then the result once it has been said (Cor: they overlapped)
                string result = role == PHGame.ROLE_NONE ? "" : ((role == PHGame.ROLE_HIDER) == game.hidersWin ? "you_win" : "game_over");
                if (game.timeUp) PlayThen("time_over", 0.8f, result, 0.8f);
                else if (result != "") sound.Play(result, 0.8f);
                SetFrozen(true);
                Say((game.hidersWin ? "Hiders win. " : "Hunters win. ") + game.endReason, 10f);
                break;
        }
    }

    public void OnSlotChanged(PHSlot s) { }

    // ------------------------------------------------------------------ calls one after the other
    // A call and the one that follows it (Cor: "the time over and you win sounds overlap"): the second starts when
    // the first has been heard out, and a little after. Only while the round and the phase are still the ones it was
    // queued in: a round that ended or moved on meanwhile drops it. One waits at a time; a newer one replaces it.
    const float CALL_GAP = 0.15f;
    string queuedCall = "";
    float queuedVolume, queuedAt;
    int queuedRound = -1, queuedPhase = -1;

    void PlayThen(string first, float volume, string then, float thenVolume)
    {
        sound.Play(first, volume);
        if (then == "") { queuedCall = ""; return; }
        AudioClip c = sound.Clip(first);
        float wait = (c != null ? c.length : 0f) + CALL_GAP;
        queuedCall = then; queuedVolume = thenVolume; queuedAt = Time.time + wait;
        queuedRound = game.round; queuedPhase = game.phase;
        SendCustomEventDelayedSeconds(nameof(PlayQueued), wait);
    }

    public void PlayQueued()
    {
        if (queuedCall == "" || Time.time < queuedAt - 0.05f) return;   // (an earlier call's wait, ended early)
        string c = queuedCall;
        queuedCall = "";
        if (game.round != queuedRound || game.phase != queuedPhase) return;
        sound.Play(c, queuedVolume);
    }

    // ------------------------------------------------------------------ ghosts and voices (Cor, G6, G8, G18, J5)
    // A ghost is a caught hider, an eliminated hunter or someone who joined mid-round. Ghosts see each other and
    // the hunters; hiders see only the hunters; hunters see only the hunters. Props are seen by everyone.
    public bool IsGhost()
    {
        PHSlot s = Me();
        return s != null && s.ghost;
    }

    // someone who joins while a round is on walks the arena as a ghost (Cor), once their slot and the map are there
    bool joinChecked;
    float joinCheckAt = 4f;

    void UpdateJoiner()
    {
        if (joinChecked || Time.time < joinCheckAt) return;
        PHSlot s = Me();
        if (s == null) return;
        int phase = game.phase;
        bool round = phase == PHGame.PH_BRIEF || phase == PHGame.PH_HIDE || phase == PHGame.PH_HUNT;
        if (!round || game.RoleOf(s.index) != PHGame.ROLE_NONE) { joinChecked = true; return; }
        if (!game.MapReady) return;
        joinChecked = true;
        Networking.LocalPlayer.TeleportTo(game.builder.ReleasePoint() + Vector3.up * 0.1f, Quaternion.Euler(0f, game.builder.ReleaseYaw(), 0f));
        s.StartGhost();
        Say("A round is on: you walk the map as a ghost until it ends.", 5f);
    }

    // Never trap a player: the stations cannot be left by hand, and only the player's own slot lets them out. Seated
    // in a slot that is no longer theirs (the host's table moved or dropped them mid-round), they would stay in it,
    // parked under the map by its new owner, for good. Checked once a second.
    float nextStray;

    void UpdateStrayStation()
    {
        if (Time.time < nextStray) return;
        nextStray = Time.time + 1f;
        int mine = game.SlotOf(Networking.LocalPlayer.playerId);
        for (int i = 0; i < game.slots.Length; i++)
        {
            PHSlot o = game.slots[i];
            if (i == mine || o == null || !o.seated) continue;
            o.station.ExitStation(Networking.LocalPlayer);
            o.seated = false;
        }
    }

    float nextVoice;
    VRCPlayerApi[] voicePlayers = new VRCPlayerApi[90];

    // Hunters cannot hear hiders. Hiders hear each other like a radio: the same volume and centred left/right
    // wherever they are (no fall-off, a volumetric radius bigger than the map and the parking, no lowpass).
    // Everything else is VRChat's default (the parked stations already put hidden voices far away).
    void UpdateVoices()
    {
        if (Time.time < nextVoice) return;
        nextVoice = Time.time + 0.5f;
        int phase = game.phase;
        bool round = phase == PHGame.PH_BRIEF || phase == PHGame.PH_HIDE || phase == PHGame.PH_HUNT;
        PHSlot me = Me();
        bool meGhost = me != null && me.ghost;
        int myRole = me != null ? game.RoleOf(me.index) : PHGame.ROLE_NONE;
        int n = VRCPlayerApi.GetPlayerCount();
        voicePlayers = VRCPlayerApi.GetPlayers(voicePlayers);
        for (int i = 0; i < n; i++)
        {
            VRCPlayerApi p = voicePlayers[i];
            if (!Utilities.IsValid(p) || p.isLocal) continue;
            int slot = game.SlotOf(p.playerId);
            PHSlot ps = slot >= 0 ? game.slots[slot] : null;
            bool theyHide = round && ps != null && game.RoleOf(slot) == PHGame.ROLE_HIDER && ps.hiding && !ps.ghost;
            bool iHide = round && myRole == PHGame.ROLE_HIDER && me != null && me.hiding && !meGhost;
            bool iHunt = round && myRole == PHGame.ROLE_HUNTER && !meGhost;
            float gain = 15f, near = 0f, far = 25f, radius = 0f;
            bool lowpass = true;
            if (iHunt && theyHide) gain = 0f;
            else if (iHide && theyHide) { near = 10000f; far = 10001f; radius = 1000f; lowpass = false; }
            p.SetVoiceGain(gain);
            p.SetVoiceDistanceNear(near);
            p.SetVoiceDistanceFar(far);
            p.SetVoiceVolumetricRadius(radius);
            p.SetVoiceLowpass(lowpass);
        }
    }

    // ------------------------------------------------------------------ the reveal (prototype Round.EndRound)
    // The roofs come off and a beam marks every hider: green survived, red caught. The results go on the HUD for
    // the reveal and stay on the lobby wall.
    void ShowReveal(bool on)
    {
        game.builder.ShowRoofs(!on);
        for (int i = 0; i < game.slots.Length; i++)
        {
            PHSlot s = game.slots[i];
            if (s.beacon == null) continue;
            bool show = on && game.slotPlayer[i] != 0 && game.slotRole[i] == PHGame.ROLE_HIDER;
            s.beacon.SetActive(show);
            if (!show) continue;
            s.beacon.transform.position = new Vector3(s.propPos.x, 0f, s.propPos.z);
            s.beaconRenderer.sharedMaterial = s.caught ? beaconRed : beaconGreen;
        }
        if (on && resultsText != null) resultsText.text = Results();
    }

    public string DefName(int d) { return defNames != null && d >= 0 && d < defNames.Length ? defNames[d] : game.gen.defId[d]; }

    string Name(int playerId)
    {
        VRCPlayerApi p = VRCPlayerApi.GetPlayerById(playerId);
        return Utilities.IsValid(p) ? p.displayName : "Player " + playerId;
    }

    string Clock(int ms) { int t = Mathf.Max(0, Mathf.RoundToInt(ms / 1000f)); return (t / 60) + ":" + (t % 60).ToString("00"); }

    string Results()
    {
        string r = (game.hidersWin ? "Hiders win" : "Hunters win") + ": " + game.endReason;
        float huntMs = game.HuntLength * 1000f, sum = 0f;
        int n = 0;
        for (int i = 0; i < game.slots.Length; i++)
        {
            if (game.slotPlayer[i] == 0 || game.slotRole[i] != PHGame.ROLE_HIDER) continue;
            PHSlot s = game.slots[i];
            string what = s.lastDef >= 0 ? DefName(s.lastDef).ToLower() : "prop";
            r += "\n" + Name(game.slotPlayer[i]) + ": " + what + (s.caught ? " - caught after " + Clock(s.survivedMs) : " - survived");
            sum += s.caught ? s.survivedMs : huntMs; n++;
        }
        for (int i = 0; i < game.slots.Length; i++)
        {
            if (game.slotPlayer[i] == 0 || game.slotRole[i] != PHGame.ROLE_HUNTER) continue;
            PHSlot s = game.slots[i];
            r += "\n" + Name(game.slotPlayer[i]) + " (hunter): " + s.hits + " hits, " + s.catches + " catches, " + s.wrongProps + " wrong props, " + s.hunterHealth + " health" + (s.hunterOut ? " (out)" : "");
        }
        // survival share (05 section 8.1)
        if (n > 0) r += "\nSurvival share: " + Mathf.RoundToInt(100f * sum / (n * huntMs)) + "%";
        return r;
    }

    public void Reseat() { PHSlot s = Me(); if (s != null) s.Reseat(); }

    // VRChat's defaults, jumping included: a world without the VRCWorld prefab's settings script has jumping off
    void Start() { SetFrozen(false); }

    void SetFrozen(bool frozen)
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        me.SetWalkSpeed(frozen ? 0f : 2f);
        me.SetRunSpeed(frozen ? 0f : 4f);
        me.SetStrafeSpeed(frozen ? 0f : 2f);
        me.SetJumpImpulse(frozen ? 0f : 3f);
    }

    // hunters pass through props (G45); hiders and everyone else collide with them
    void SetPropLayer(int layer)
    {
        if (propLayer == layer || game.builder.objects == null) return;
        propLayer = layer;
        GameObject[] objs = game.builder.objects;
        int[] emitDef = game.gen.emitDef, defFlags = game.gen.defFlags;
        for (int i = 0; i < objs.Length; i++)
        {
            if (objs[i] == null) continue;
            objs[i].layer = layer;
            if ((defFlags[emitDef[i]] & GenData.FL_OPENABLE) == 0) continue;   // only openables have parts with colliders (the shade patch has none)
            Transform t = objs[i].transform;
            for (int k = 0; k < t.childCount; k++)
            {
                Transform ch = t.GetChild(k);
                if (ch.name != "Shade") ch.gameObject.layer = layer;
            }
        }
    }

    // ------------------------------------------------------------------ disguise
    void StartingDisguise()
    {
        // a random medium type from the map's menu (05 section 3.8)
        int n = game.gen.defDisguise.Length;
        for (int k = 0; k < 60; k++)
        {
            int d = Random.Range(0, n);
            if (d < game.gen.menu.Length && game.gen.menu[d] && game.gen.defDisguise[d] == 2) { SetDisguise(d); return; }
        }
        for (int d = 0; d < n; d++) if (d < game.gen.menu.Length && game.gen.menu[d]) { SetDisguise(d); return; }
    }

    // Become a prop of this type, unless its box has no room here (05 section 3.7)
    bool SetDisguise(int d)
    {
        PHSlot s = Me();
        if (s == null) return false;
        if (s.def >= 0 && body.active)
        {
            int old = s.def;
            body.SetProp(d);
            // no room where the hider stands: the nearest place with room (PHHiderBody.FitHere), or not at all
            Vector3 was = body.pos; float wasYaw = body.yaw;
            if (!body.FitHere()) { body.SetProp(old); body.pos = was; body.yaw = wasYaw; Say("No room for that here.", 2f); return false; }
        }
        int cls = Mathf.Clamp(game.gen.defDisguise[d] - 1, 0, 3);
        int oldMax = s.maxHealth;
        s.maxHealth = hiderHealth[cls];
        s.health = oldMax > 0 ? Mathf.Max(1, Mathf.RoundToInt((float)s.health / oldMax * s.maxHealth)) : s.maxHealth;
        s.def = d; s.lastDef = d;
        body.SetProp(d);
        s.propPos = body.pos;
        s.propYaw = body.yaw;
        s.Send();
        float h = game.gen.defSy[d] / 1000f;
        SetEye(h > 2.5f ? 1.6f : Mathf.Clamp(Mathf.Round(0.9f * h / 0.05f) * 0.05f, 0.25f, 2.0f));
        return true;
    }

    void SetEye(float eye)
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (originalEye < 0f) originalEye = me.GetAvatarEyeHeightAsMeters();
        me.SetAvatarEyeHeightByMeters(Mathf.Max(0.1f, eye));
    }

    void RestoreEye()
    {
        if (originalEye < 0f) return;
        Networking.LocalPlayer.SetAvatarEyeHeightByMeters(originalEye);
        originalEye = -1f;
    }

    // ------------------------------------------------------------------ input
    public override void InputUse(bool value, VRC.Udon.Common.UdonInputEventArgs args)
    {
        if (!value) return;
        int phase = game.phase;
        bool vr = Networking.LocalPlayer.IsUserInVR();
        // desktop: a click on a door within reach works it (C7), before anything else
        if (!vr && TryDoor()) return;
        // VR: a hider's left trigger turns the prop (PHHiderBody); the selector in the right hand picks
        if (vr && role == PHGame.ROLE_HIDER && args.handType == VRC.Udon.Common.HandType.LEFT) return;
        if (role == PHGame.ROLE_HIDER && (phase == PHGame.PH_BRIEF || phase == PHGame.PH_HIDE || phase == PHGame.PH_HUNT)) TryTakeDisguise();
        else if (role == PHGame.ROLE_HUNTER && phase == PHGame.PH_HUNT) Fire();
    }

    // VR: the grips work doors (a hider taunts with the button on the screen device, PHView; desktop: T)
    public override void InputGrab(bool value, VRC.Udon.Common.UdonInputEventArgs args)
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (!value || !me.IsUserInVR()) return;
        // VR: a grip with the hand on or close to a door works it (grab-open)
        if (CanWorkDoors())
        {
            bool left = args.handType == VRC.Udon.Common.HandType.LEFT;
            Vector3 hand = me.GetTrackingData(left ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand).position;
            int d = doors.NearPart(hand, GRAB_REACH);
            if (d >= 0) { doors.Toggle(d); return; }
        }
    }

    // the taunt button on the VR screen device (PHView)
    public bool TauntReady()
    {
        PHSlot s = Me();
        return s != null && role == PHGame.ROLE_HIDER && !s.caught && game.phase == PHGame.PH_HUNT && Time.time - lastTaunt >= TAUNT_COOLDOWN;
    }

    public bool ButtonTaunt()
    {
        if (!TauntReady()) return false;
        Taunt();
        return true;
    }

    bool InRound()
    {
        int p = game.phase;
        return p == PHGame.PH_HIDE || p == PHGame.PH_HUNT || p == PHGame.PH_BRIEF;
    }

    // doors are for the living players of the round: a ghost (caught, out, or joined mid-round) is unseen, and a
    // door it opened would give a hider away
    bool CanWorkDoors() { return doors != null && InRound() && role != PHGame.ROLE_NONE && !IsGhost(); }

    // desktop: the door (or the openable prop) under the cross-hair, within the role's reach of the prop
    bool TryDoor()
    {
        if (!CanWorkDoors()) return false;
        float reach = role == PHGame.ROLE_HUNTER ? HUNTER_HANDLE_RANGE : HANDLE_RANGE;
        if (!Aim(reach + 1f)) return false;
        if (!hit.collider.gameObject.name.StartsWith("door")) return false;
        int i = doors.IndexOf(hit.collider.transform);
        if (i < 0) return false;
        GameObject o = game.builder.objects[doors.EmitOf(i)];
        Vector3 me = Networking.LocalPlayer.GetPosition();
        Vector3 d = o.transform.position - me; d.y = 0f;
        if (d.magnitude > reach + 0.5f) return false;
        doors.Toggle(i);
        return true;
    }

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
        if (Input.GetKeyDown(KeyCode.T)) Taunt();
        bool eg = game.Endgame;
        if (eg && !wasEndgame && role != PHGame.ROLE_NONE)
        {
            PlayThen("hurry_up", 0.8f, "jingles_HIT03", 0.6f);
            Say("45 seconds! Taunts come twice as often; wrong props and hits count double.", 4f);
            float iv = game.tauntInterval * 0.5f;
            if (nextForced > Time.time + iv) nextForced = Time.time + iv;
        }
        wasEndgame = eg;
        UpdateForcedTaunt();
        UpdateVoices();
        UpdateStrayStation();
        UpdateJoiner();
        UpdateHud();
        UpdateHover();
        if (Time.time >= nextPanel) { nextPanel = Time.time + 0.5f; string st = game.SettingsText(); if (panelText != null) panelText.text = st; if (panelUiText != null) panelUiText.text = st; }
    }

    // A hider sees which prop the pointer is on (Cor: it was unclear what would be picked, or whether a thing could
    // be picked at all): the prop that Use would take glows; a thing that cannot be a disguise does not.
    Collider hoverCol;
    GameObject hoverObj;
    int hoverDef = -1;
    // for the selector in a VR hider's right hand (PHView): whether it can pick now, and how far its ray goes
    [HideInInspector] public bool canPick;
    [HideInInspector] public float aimDist = TAKE_RANGE;
    public bool AimValid() { return hoverObj != null; }
    // VR: the way the selector points, from the right hand's tracking (the offset, in degrees, is for tuning)
    public Vector3 vrAimEuler = Vector3.zero;
    public Quaternion AimRotation(Quaternion hand) { return hand * Quaternion.Euler(vrAimEuler); }

    void UpdateHover()
    {
        int phase = game.phase;
        bool can = role == PHGame.ROLE_HIDER && (phase == PHGame.PH_BRIEF || phase == PHGame.PH_HIDE || phase == PHGame.PH_HUNT) && !aimOverride && !IsGhost();
        Collider c = null;
        canPick = can;
        aimDist = TAKE_RANGE;
        if (can && Aim(TAKE_RANGE)) { c = hit.collider; aimDist = hit.distance; }
        if (c == hoverCol && (c != null || hoverObj == null)) return;
        hoverCol = c;
        GameObject o = null; int d = -1;
        if (c != null)
        {
            int e = EmitOf(c.transform);
            if (e >= 0)
            {
                d = game.gen.emitDef[e];
                if (game.gen.defDisguise[d] > 0) o = game.builder.objects[e];
            }
        }
        if (o == hoverObj) return;
        if (hoverObj != null) game.builder.Hover(hoverObj, hoverDef, false);
        hoverObj = o; hoverDef = d;
        if (o != null) game.builder.Hover(o, d, true);
    }

    // the host's interval; a small group (under 6) three quarters of it, the endgame half (9.4, G53)
    float ForcedInterval()
    {
        if (game.Endgame) return game.tauntInterval * 0.5f;
        return game.Participants() < 6 ? game.tauntInterval * 0.75f : game.tauntInterval;
    }

    void UpdateForcedTaunt()
    {
        PHSlot s = Me();
        if (role != PHGame.ROLE_HIDER || game.phase != PHGame.PH_HUNT || s == null || s.caught || nextForced < 0f) return;
        Vector3 d = Networking.LocalPlayer.GetPosition() - checkPos; d.y = 0f;
        bool skip = d.magnitude >= FORCED_MOVED;
        float t = nextForced - Time.time;
        if (t <= FORCED_WARNING && !warned && !skip)
        {
            warned = true;
            sound.Play("tick_001", 0.8f);
            Say("Forced taunt in 3 s: move 4 m, or taunt now (T)", 3f);
        }
        if (t > 0f) return;
        checkPos = Networking.LocalPlayer.GetPosition();
        nextForced = Time.time + ForcedInterval();
        warned = false;
        if (skip) { Say("You moved: no forced taunt", 2f); return; }
        lastTaunt = -99f;
        Taunt();
    }

    // the test pilot aims for its client (PHTestPilot): a ray instead of the head or hand
    [HideInInspector] public bool aimOverride;
    [HideInInspector] public string lastHitName = "";
    [HideInInspector] public Vector3 aimOrigin, aimDir;

    public void PilotFire() { Fire(); }
    public void PilotTake() { TryTakeDisguise(); }
    public void PilotTaunt() { Taunt(); }
    public int Role() { return role; }

    // what the last Aim hit (Udon does not write an out parameter through to a field or another out parameter)
    RaycastHit hit;

    bool Aim(float range)
    {
        RaycastHit r;
        bool any;
        if (aimOverride) { any = Physics.SphereCast(aimOrigin, 0.04f, aimDir, out r, range, ~(1 << 9 | 1 << 10 | 1 << 18), QueryTriggerInteraction.Ignore); hit = r; return any; }
        VRCPlayerApi me = Networking.LocalPlayer;
        VRCPlayerApi.TrackingData t = me.IsUserInVR() ? me.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand) : me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 dir = me.IsUserInVR() ? AimRotation(t.rotation) * Vector3.forward : t.rotation * Vector3.forward;
        any = Physics.SphereCast(t.position, 0.04f, dir, out r, range, ~(1 << 9 | 1 << 10 | 1 << 18), QueryTriggerInteraction.Ignore);
        hit = r;
        return any;
    }

    // the emit index of a map prop that was hit, or -1
    int EmitOf(Transform t)
    {
        Transform root = game.builder.propRoot;
        while (t != null && t.parent != root) t = t.parent;
        if (t == null) return -1;
        GameObject[] objs = game.builder.objects;
        for (int i = 0; i < objs.Length; i++) if (objs[i] == t.gameObject) return i;
        return -1;
    }

    // the slot whose hider prop was hit, or -1
    int HiderOf(Transform t)
    {
        Transform root = game.slots.Length > 0 ? game.slots[0].proxyRoot : null;
        while (t != null && t.parent != root) t = t.parent;
        if (t == null) return -1;
        string n = t.gameObject.name;
        if (!n.StartsWith("Hider")) return -1;
        return int.Parse(n.Substring(5));
    }

    void TryTakeDisguise()
    {
        // a caught hider (a ghost) picks nothing: it gave them health 1 and shrank them again (PilotTake comes here too)
        PHSlot s = Me();
        if (s == null || s.caught || s.ghost || role != PHGame.ROLE_HIDER || !InRound()) return;
        if (!Aim(TAKE_RANGE)) return;
        int e = EmitOf(hit.collider.transform);
        if (e < 0) return;
        int d = game.gen.emitDef[e];
        // a prop that cannot be taken says so, and a change that took has its own cue; both heard only by the hider (Cor)
        if (game.gen.defDisguise[d] <= 0) { sound.Play("error_004", 0.5f); Say("That can't be a disguise.", 2f); return; }
        if (game.phase == PHGame.PH_HUNT)
        {
            if (changesLeft <= 0) { sound.Play("error_004", 0.5f); Say("No disguise changes left.", 2f); return; }
            if (Time.time - lastChange < HUNT_CHANGE_COOLDOWN) { sound.Play("error_004", 0.5f); return; }
            changesLeft--;
        }
        if (!SetDisguise(d)) { sound.Play("error_004", 0.5f); if (game.phase == PHGame.PH_HUNT) changesLeft++; return; }
        lastChange = Time.time;
        // one soft pluck, at once (Cor: the three rising tones were not immediate)
        sound.Play("pluck_002", 0.5f);
        Say("You are a " + DefName(d).ToLower(), 2f);
    }

    int lastShotSlot = -1, lastShotHealth;
    float lastShotAt = -99f;

    void Fire()
    {
        PHSlot s = Me();
        // (PilotFire comes here without InputUse's checks)
        if (s == null || role != PHGame.ROLE_HUNTER || game.phase != PHGame.PH_HUNT) return;
        if (s.hunterOut || Time.time < nextDart) return;
        nextDart = Time.time + DART_INTERVAL;
        sound.Play("switch_002", 0.5f);
        lastHitName = "nothing";
        if (!Aim(DART_RANGE)) return;
        lastHitName = hit.collider.gameObject.name + " (" + hit.collider.transform.root.name + ")";
        int damage = game.Endgame ? DART_DAMAGE * 2 : DART_DAMAGE;
        int h = HiderOf(hit.collider.transform);
        if (h >= 0)
        {
            PHSlot target = game.slots[h];
            if (target.caught) return;
            // the target's health as synced, less this hunter's own darts it may not have heard of yet (a second dart
            // within the sync delay counted a second catch)
            int was = target.health;
            if (h == lastShotSlot && Time.time - lastShotAt < 2f && lastShotHealth < was) was = lastShotHealth;
            lastShotSlot = h; lastShotAt = Time.time; lastShotHealth = was - damage;
            target.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(PHSlot.TakeHit), damage, Networking.LocalPlayer.playerId);
            s.hits++;
            s.hunterHealth = Mathf.Min(HUNTER_HEALTH, s.hunterHealth + HIT_REWARD);
            if (was > 0 && was - damage <= 0) s.catches++;
            s.Send();
            sound.Play("confirmation_002", 0.8f);
            sound.PlayAt("impactPunch_medium_000", hit.point, 14f, 0.8f, 1f);
            Say("Hit!", 1.5f);
            return;
        }
        int e = EmitOf(hit.collider.transform);
        if (e < 0) return;   // a wall or a door: nothing happens
        KnockProp(e, hit.point - Aim0(), hit.point);
        // a real prop: it costs health (G17), but the same prop again within 5 s costs nothing
        if (e == lastCharged && Time.time - lastChargedAt < WRONG_SAME_WINDOW) return;
        lastCharged = e; lastChargedAt = Time.time;
        int penalty = game.wrongPenalty;
        if (game.Participants() < 6) penalty = (penalty + 1) / 2;
        if (game.Endgame) penalty *= 2;
        s.wrongProps++;
        s.hunterHealth -= penalty;
        if (s.hunterHealth <= 0) { s.hunterHealth = 0; s.hunterOut = true; Say("Out of health: you walk as a ghost until the round ends.", 4f); s.StartGhost(); }
        else Say("Just a " + DefName(game.gen.emitDef[e]).ToLower() + ". -" + penalty + " health", 2f);
        s.Send();
        sound.Play("error_006", 0.6f);
    }

    Vector3 Aim0()
    {
        if (aimOverride) return aimOrigin;
        VRCPlayerApi me = Networking.LocalPlayer;
        return (me.IsUserInVR() ? me.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand) : me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head)).position;
    }

    // a dart knocks a real prop (04 section 4.8): LIGHT and KNOCKABLE props fly, heavy ones only thud
    void KnockProp(int e, Vector3 dir, Vector3 point)
    {
        int fl = game.gen.defFlags[game.gen.emitDef[e]];
        if ((fl & (GenData.FL_HEAVY | GenData.FL_FIXTURE | GenData.FL_SCENERY)) != 0) return;
        PHKnock k = game.KnockOf(e);
        if (k != null && k.Busy) return;   // already flying
        if (k == null) k = game.FreeKnock();
        if (k != null) k.Knock(e, dir, point);
    }

    // test aid (editor, MCP): knock prop testKnockEmit as if a dart hit it from the south
    [HideInInspector] public int testKnockEmit;
    public void TestKnock()
    {
        GameObject go = game.builder.objects[testKnockEmit];
        if (go != null) KnockProp(testKnockEmit, Vector3.forward, go.transform.position + Vector3.up * 0.2f);
    }

    void Taunt()
    {
        PHSlot s = Me();
        if (s == null || role != PHGame.ROLE_HIDER || s.caught || game.phase != PHGame.PH_HUNT) return;
        if (Time.time - lastTaunt < TAUNT_COOLDOWN) return;
        lastTaunt = Time.time;
        s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.PlayTaunt), Random.Range(0, 1000));
    }

    // a hunter's dart reached this player's prop (PHSlot.TakeHit, on the owner)
    public void OnHit(int damage, bool nowCaught, int shooter)
    {
        PHSlot s = Me();
        if (s == null) return;   // (the slot table may not have this player yet)
        if (nowCaught)
        {
            Say("You were caught! You walk as a ghost until the round ends.", 5f);
            sound.Play("you_lose", 0.8f);
            s.def = -1; s.Send();
            body.End();
            RestoreEye();
            s.StartGhost();
            SetFrozen(false);
        }
        else Say("Hit! " + s.health + " health left. Move!", 2.5f);
    }

    // ------------------------------------------------------------------ HUD
    void UpdateHud()
    {
        if (hud == null) return;
        PHSlot s = Me();
        string t = "";
        int phase = game.phase;
        if (phase == PHGame.PH_LOBBY)
            t = "Lobby: stand in the green zone to play. " + game.Participants() + " in the zone.";
        else if (phase == PHGame.PH_LOADING)
            t = "Loading the map... " + Mathf.RoundToInt(game.gen.progressPermille / 10f) + "%";
        else
        {
            string ph = phase == PHGame.PH_BRIEF ? "Get ready" : (phase == PHGame.PH_HIDE ? "Hide" : (phase == PHGame.PH_HUNT ? (game.Endgame ? "Hunt (endgame)" : "Hunt") : "Round over"));
            int left = Mathf.CeilToInt(game.Left);
            t = ph + "  " + (left / 60) + ":" + (left % 60).ToString("00");
            if (s != null && role == PHGame.ROLE_HIDER) t += "\nHealth " + s.health + "/" + s.maxHealth + (s.caught ? " (caught)" : "") + "  changes " + changesLeft;
            if (s != null && role == PHGame.ROLE_HUNTER) t += "\nHealth " + s.hunterHealth + "  hits " + s.hits + "  wrong " + s.wrongProps + (s.hunterOut ? " (out)" : "");
        }
        if (phase == PHGame.PH_END && resultsText != null) t += "\n" + resultsText.text;
        if (Time.time < noticeUntil) t += "\n" + notice;
        hud.text = t;
    }

    public override void PostLateUpdate()
    {
        if (profWatch == null) { PostLateBody(); return; }
        double p0 = profWatch.Elapsed.TotalMilliseconds;
        PostLateBody();
        profMs += profWatch.Elapsed.TotalMilliseconds - p0;
    }

    void PostLateBody()
    {
        if (hudRoot == null) return;
        VRCPlayerApi.TrackingData head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        hudRoot.SetPositionAndRotation(head.position + head.rotation * new Vector3(0f, -0.18f, 0.6f), head.rotation);
    }
}
