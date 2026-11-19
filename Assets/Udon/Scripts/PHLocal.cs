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
//   Hunter: strikes with a melee weapon (PHWeapons; desktop click, VR swing): a hider's prop takes a hit (sent to
//          its owner), a real prop wears the weapon (G17) and gets knocked, anything else costs nothing. Hunters walk
//          through props (G45).
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
    public const float TAKE_RANGE = 4f, WRONG_SAME_WINDOW = 5f, TAUNT_COOLDOWN = 8f;
    public const int HIT_DAMAGE = 25, HUNTER_HEALTH = 100, HUNT_CHANGES = 3;   // HUNTER_HEALTH: a new weapon's condition
    public const float HUNT_CHANGE_COOLDOWN = 5f;
    public const int LAYER_PROPS_SOLID = 11, LAYER_PROPS_WALKTHROUGH = 17;

    int role, slotIndex = -1;
    float lastChange = -99f, lastTaunt = -99f;
    int changesLeft;
    float originalEye = -1f;
    int lastCharged = -1;
    float lastChargedAt = -99f;
    string notice = "";
    float noticeUntil;
    int propLayer = -1;
    GameObject[] layerObjs;   // the map's objects the layer was last set on
    bool layerDone;           // ...and whether that map was finished then
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
                Say(T(role == PHGame.ROLE_NONE ? "say.watching" : "say.building"), 5f);
                break;
            case PHGame.PH_BRIEF:
                sound.Play("ready", 0.8f);
                if (role == PHGame.ROLE_HUNTER)
                {
                    // hunters wait in the lobby while the hiders hide (Cor); the hunt brings them to the release
                    SetPropLayer(LAYER_PROPS_WALKTHROUGH);
                    SetFrozen(false);
                    Say(T("say.youHunt"), 6f);
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
                    Say(T("say.youHide"), 6f);
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
                    Say(T("say.hunt"), 3f);
                }
                else if (role == PHGame.ROLE_HIDER)
                {
                    Say(T("say.released"), 3f);
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
                // the reveal (Cor): every hider's prop stays where it is, highlighted through walls (PHSlot), and
                // everyone is out of the seats, walking as themselves, until the lobby (PHGame.ROUND_END, 30 s)
                if (body.active) body.frozen = true;
                if (s != null) s.LeaveSeatForReveal();
                RestoreEye();
                SetFrozen(false);
                Say(game.text.F1(game.hidersWin ? "say.hidersWin" : "say.huntersWin", game.text.En(game.endReason)), 10f);
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
        s.Claim();   // (first: the slot's takeover clearing, PHSlot.Fresh, must not wipe the ghost state written next)
        Networking.LocalPlayer.TeleportTo(game.builder.ReleasePoint() + Vector3.up * 0.1f, Quaternion.Euler(0f, game.builder.ReleaseYaw(), 0f));
        s.StartGhost();
        Say(T("say.ghostJoin"), 5f);
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
            // (a slot that also lists this player is theirs too: never fight its own re-seat)
            if (i == mine || o == null || !o.seated || game.slotPlayer[i] == Networking.LocalPlayer.playerId) continue;
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
        // Perf: a player's slot was a 32-slot search per player (about 1,000 rounds with 32 players), and all five
        // voice settings were set every half second whether they had changed or not (about 155 calls): a 1.5-3 ms
        // hitch every 0.5 s. Now the slots go into a small table keyed by player id once a pass, and a player's
        // settings are set only when what they should be differs from what was set last. Every few seconds they are
        // all set again regardless (VRChat may reset a player's voice, e.g. on an avatar change).
        if (Time.time >= voiceResetAt || voiceSet == null) { voiceResetAt = Time.time + VOICE_RESET; voiceSetId = new int[VOICE_HASH]; voiceSet = new int[VOICE_HASH]; }
        int[] slotOfKey = new int[VOICE_HASH], slotOfSlot = new int[VOICE_HASH];
        for (int s = 0; s < game.slotPlayer.Length; s++)
        {
            int id = game.slotPlayer[s];
            if (id == 0) continue;
            int h = id & (VOICE_HASH - 1);
            while (slotOfKey[h] != 0) h = (h + 1) & (VOICE_HASH - 1);
            slotOfKey[h] = id; slotOfSlot[h] = s;
        }
        bool iHide = round && myRole == PHGame.ROLE_HIDER && me != null && me.hiding && !meGhost;
        bool iHunt = round && myRole == PHGame.ROLE_HUNTER && !meGhost;
        for (int i = 0; i < n; i++)
        {
            VRCPlayerApi p = voicePlayers[i];
            if (!Utilities.IsValid(p) || p.isLocal) continue;
            int pid = p.playerId;
            int slot = -1;
            for (int h = pid & (VOICE_HASH - 1); slotOfKey[h] != 0; h = (h + 1) & (VOICE_HASH - 1))
                if (slotOfKey[h] == pid) { slot = slotOfSlot[h]; break; }
            PHSlot ps = slot >= 0 ? game.slots[slot] : null;
            bool theyHide = round && ps != null && game.RoleOf(slot) == PHGame.ROLE_HIDER && ps.hiding && !ps.ghost;
            // 1: VRChat's default, 2: a hunter does not hear a hider, 3: hiders hear each other like a radio
            int want = (iHunt && theyHide) ? 2 : ((iHide && theyHide) ? 3 : 1);
            // what was set for this player last (an open-addressed table keyed by player id; a full one starts over)
            int at = pid & (VOICE_HASH - 1), probes = 0;
            while (voiceSetId[at] != 0 && voiceSetId[at] != pid && probes < VOICE_HASH) { at = (at + 1) & (VOICE_HASH - 1); probes++; }
            if (probes >= VOICE_HASH) { voiceSetId = new int[VOICE_HASH]; voiceSet = new int[VOICE_HASH]; at = pid & (VOICE_HASH - 1); }
            if (voiceSetId[at] == pid && voiceSet[at] == want) continue;
            voiceSetId[at] = pid; voiceSet[at] = want;
            float gain = 15f, near = 0f, far = 25f, radius = 0f;
            bool lowpass = true;
            if (want == 2) gain = 0f;
            else if (want == 3) { near = 10000f; far = 10001f; radius = 1000f; lowpass = false; }
            p.SetVoiceGain(gain);
            p.SetVoiceDistanceNear(near);
            p.SetVoiceDistanceFar(far);
            p.SetVoiceVolumetricRadius(radius);
            p.SetVoiceLowpass(lowpass);
        }
    }

    const int VOICE_HASH = 128;     // a power of two, more than the 90 players an instance can hold
    const float VOICE_RESET = 5f;   // seconds between setting everyone's voice again regardless
    int[] voiceSetId, voiceSet;
    float voiceResetAt;

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
        return Utilities.IsValid(p) ? p.displayName : game.text.F1("res.player", playerId.ToString());
    }

    string T(string key) { return game.text.Get(key); }

    string Clock(int ms) { int t = Mathf.Max(0, Mathf.RoundToInt(ms / 1000f)); return (t / 60) + ":" + (t % 60).ToString("00"); }

    string Results()
    {
        string r = game.text.F1(game.hidersWin ? "res.hidersWin" : "res.huntersWin", game.text.En(game.endReason));
        float huntMs = game.HuntLength * 1000f, sum = 0f;
        int n = 0;
        for (int i = 0; i < game.slots.Length; i++)
        {
            if (game.slotPlayer[i] == 0 || game.slotRole[i] != PHGame.ROLE_HIDER) continue;
            PHSlot s = game.slots[i];
            string what = s.lastDef >= 0 ? game.text.Prop(s.lastDef) : T("res.prop");
            r += "\n" + (s.caught ? game.text.F3("res.hiderCaught", Name(game.slotPlayer[i]), what, Clock(s.survivedMs)) : game.text.F2("res.hiderSurvived", Name(game.slotPlayer[i]), what));
            sum += s.caught ? s.survivedMs : huntMs; n++;
        }
        for (int i = 0; i < game.slots.Length; i++)
        {
            if (game.slotPlayer[i] == 0 || game.slotRole[i] != PHGame.ROLE_HUNTER) continue;
            PHSlot s = game.slots[i];
            r += "\n" + game.text.F3(s.hunterOut ? "res.hunterOut" : "res.hunter", Name(game.slotPlayer[i]), s.hits.ToString(), s.catches.ToString()).Replace("{3}", s.wrongProps.ToString()).Replace("{4}", s.hunterHealth.ToString());
        }
        // survival share (05 section 8.1)
        if (n > 0) r += "\n" + game.text.F1("res.share", Mathf.RoundToInt(100f * sum / (n * huntMs)).ToString());
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
        // (done again for a new map, and once the map is finished: props built after the switch kept their layer, so a
        // hunter whose BRIEF came before their map was done bumped into them, and the next map was never switched)
        GameObject[] objs = game.builder.objects;
        if (propLayer == layer && layerObjs == objs && layerDone) return;
        propLayer = layer;
        layerObjs = objs; layerDone = game.builder.built;
        if (objs == null) return;
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
            if (!body.FitHere()) { body.SetProp(old); body.pos = was; body.yaw = wasYaw; Say(T("say.noRoom"), 2f); return false; }
        }
        // Every hider has 100 health whatever they are (Cor); size changes how much a dart takes (PHSlot.HitDamage), so
        // a new disguise keeps the health it had
        if (s.maxHealth <= 0) { s.maxHealth = PHSlot.HIDER_HEALTH; s.health = PHSlot.HIDER_HEALTH; }
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
        if (vr && args.handType == VRC.Udon.Common.HandType.LEFT) return;
        if (role == PHGame.ROLE_HIDER && (phase == PHGame.PH_BRIEF || phase == PHGame.PH_HIDE || phase == PHGame.PH_HUNT))
        {
            // the final stand (Cor): no new disguises; the selector shoots hunters instead
            if (game.Endgame) HiderShoot();
            else
            {
                if (canPick && weapons != null) weapons.FireSelector();   // the selector kicks with every pull
                TryTakeDisguise();
            }
        }
        // a hunter: desktop clicks to slash (PHWeapons strikes as the weapon comes across); VR swings the hand instead
        else if (role == PHGame.ROLE_HUNTER && phase == PHGame.PH_HUNT && !vr && weapons != null) weapons.DesktopSwing();
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
        if (propLayer >= 0 && game.builder.built && (!layerDone || layerObjs != game.builder.objects)) SetPropLayer(propLayer);
        bool eg = game.Endgame;
        if (eg && !wasEndgame && role != PHGame.ROLE_NONE)
        {
            PlayThen("hurry_up", 0.8f, "jingles_HIT03", 0.6f);
            Say(T("say.final"), 4f);
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
        bool can = role == PHGame.ROLE_HIDER && (phase == PHGame.PH_BRIEF || phase == PHGame.PH_HIDE || (phase == PHGame.PH_HUNT && !game.Endgame)) && !aimOverride && !IsGhost();
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
            Say(T("say.forced"), 3f);
        }
        if (t > 0f) return;
        checkPos = Networking.LocalPlayer.GetPosition();
        nextForced = Time.time + ForcedInterval();
        warned = false;
        if (skip) { Say(T("say.moved"), 2f); return; }
        lastTaunt = -99f;
        Taunt();
    }

    // the test pilot aims for its client (PHTestPilot): a ray instead of the head or hand
    [HideInInspector] public bool aimOverride;
    [HideInInspector] public string lastHitName = "";
    [HideInInspector] public Vector3 aimOrigin, aimDir;

    public void PilotFire() { MeleeDesktopStrike(); }
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
        if (objs == null) return -1;
        // Perf: a scan of every prop (hundreds to ~1,500, 1-3 ms in Udon) each time the pointer moved onto another
        // prop and on every dart. A prop found once is remembered by its place among propRoot's children (they stay
        // in place while the map stands), checked against objects before it is trusted
        GameObject go = t.gameObject;
        int at = t.GetSiblingIndex();
        if (emitHintOf != objs) { emitHintOf = objs; emitHint = new int[Mathf.Max(objs.Length, root != null ? root.childCount : 0)]; }
        if (at < emitHint.Length)
        {
            int e = emitHint[at];
            if (e >= 0 && e < objs.Length && objs[e] == go) return e;
        }
        for (int i = 0; i < objs.Length; i++)
            if (objs[i] == go)
            {
                if (at < emitHint.Length) emitHint[at] = i;
                return i;
            }
        return -1;
    }

    int[] emitHint;          // emit index by the prop's place among propRoot's children (EmitOf)
    GameObject[] emitHintOf; // the objects array it is for (a new map, a new table)

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
        if (game.Endgame) return;   // the final stand: the disguise is kept to the end (Cor)
        if (!Aim(TAKE_RANGE)) return;
        int e = EmitOf(hit.collider.transform);
        if (e < 0) return;
        int d = game.gen.emitDef[e];
        // a prop that cannot be taken says so, and a change that took has its own cue; both heard only by the hider (Cor)
        if (game.gen.defDisguise[d] <= 0) { sound.Play("error_004", 0.5f); Say(T("say.notDisguise"), 2f); return; }
        if (game.phase == PHGame.PH_HUNT)
        {
            if (changesLeft <= 0) { sound.Play("error_004", 0.5f); Say(T("say.noChanges"), 2f); return; }
            if (Time.time - lastChange < HUNT_CHANGE_COOLDOWN) { sound.Play("error_004", 0.5f); return; }
            changesLeft--;
        }
        if (!SetDisguise(d)) { sound.Play("error_004", 0.5f); if (game.phase == PHGame.PH_HUNT) changesLeft++; return; }
        lastChange = Time.time;
        statPropChanges++;
        // one soft pluck, at once (Cor: the three rising tones were not immediate)
        sound.Play("pluck_002", 0.5f);
        Say(game.text.F1("say.youAre", game.text.Prop(d)), 2f);
    }

    // ------------------------------------------------------------------ the final stand: hiders shoot back (Cor)
    // In the endgame a hider's selector fires at hunters: a hit takes a small chunk out of the hunter's weapon
    // (SHOT_WEAR of its condition; it can break it). Nobody sees a hider's gun until their first shot; from then on
    // it hangs at their prop for everyone (PHSlot.armed, PHWeapons). A shot is a ray from the eyes (desktop) or the
    // right hand (VR); the hunters are found along it by their bodies' place (Udon cannot tell whose collider a ray
    // met), nearer than the first wall or prop.
    public const float SHOT_RANGE = 30f, SHOT_INTERVAL = 1.6f, SHOT_BODY = 0.35f;
    public const int SHOT_WEAR = 5;
    float nextShot;   // (Cor: a relatively low rate of fire)

    public bool CanShoot()
    {
        PHSlot s = Me();
        return s != null && role == PHGame.ROLE_HIDER && game.Endgame && s.def >= 0 && !s.caught && !s.ghost;
    }

    public void PilotShoot() { HiderShoot(); }

    void HiderShoot()
    {
        if (!CanShoot() || Time.time < nextShot) return;
        nextShot = Time.time + SHOT_INTERVAL;
        PHSlot s = Me();
        Vector3 o, dir;
        VRCPlayerApi me = Networking.LocalPlayer;
        if (aimOverride) { o = aimOrigin; dir = aimDir.normalized; }
        else
        {
            VRCPlayerApi.TrackingData t = me.IsUserInVR() ? me.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand) : me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            o = t.position; dir = (me.IsUserInVR() ? AimRotation(t.rotation) : t.rotation) * Vector3.forward;
        }
        float wall = SHOT_RANGE;
        RaycastHit r;
        if (Physics.Raycast(o, dir, out r, SHOT_RANGE, ~(1 << 9 | 1 << 10 | 1 << 18), QueryTriggerInteraction.Ignore)) wall = r.distance;
        // the nearest hunter whose body (a 0.35 m thick upright segment, feet to head) the ray passes, before the wall
        int best = -1; float bestT = wall;
        for (int i = 0; i < game.slots.Length; i++)
        {
            if (game.RoleOf(i) != PHGame.ROLE_HUNTER || game.slots[i].hunterOut) continue;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(game.slotPlayer[i]);
            if (p == null || !Utilities.IsValid(p) || p.isLocal) continue;
            Vector3 feet = p.GetPosition();
            float top = Mathf.Max(0.5f, p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position.y - feet.y + 0.15f);
            float tAt = RayToUpright(o, dir, feet, top);
            if (tAt < 0f || tAt >= bestT) continue;
            best = i; bestT = tAt;
        }
        Vector3 end = o + dir * bestT;
        if (!s.armed) { s.armed = true; s.Send(); }
        if (weapons != null) weapons.FireSelector();
        statShots++;
        s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.Shot), end, best);
        if (best >= 0)
        {
            statShotHits++;
            if (weapons != null) weapons.WatchBreak(best);
            game.slots[best].SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(PHSlot.TakeShot), SHOT_WEAR, Networking.LocalPlayer.playerId);
            sound.Play("confirmation_002", 0.6f);
        }
    }

    // how far along the ray it passes within SHOT_BODY of the upright segment feet .. feet + up * height; -1 if not
    float RayToUpright(Vector3 o, Vector3 dir, Vector3 feet, float height)
    {
        // closest approach in the ground plane, then check the height there
        Vector2 o2 = new Vector2(o.x - feet.x, o.z - feet.z), d2 = new Vector2(dir.x, dir.z);
        float dd = d2.sqrMagnitude;
        float t = dd > 1e-6f ? Mathf.Max(0f, -Vector2.Dot(o2, d2) / dd) : 0f;
        Vector2 c = o2 + d2 * t;
        if (c.sqrMagnitude > SHOT_BODY * SHOT_BODY) return -1f;
        float y = o.y + dir.y * t - feet.y;
        if (y < -0.1f || y > height) return -1f;
        return t;
    }

    // a hider's shot hit this player (PHSlot.TakeShot, on the owner): a chunk off the weapon, and out if it breaks
    public void OnShot(int wear, int shooter)
    {
        PHSlot s = Me();
        if (s == null || role != PHGame.ROLE_HUNTER || game.phase != PHGame.PH_HUNT || s.hunterOut) return;
        s.hunterHealth -= wear;
        sound.Play("impactMetal_light_002", 0.8f);
        if (s.hunterHealth <= 0)
        {
            s.hunterHealth = 0; s.hunterOut = true;
            myWeaponBroken++; lastBrokenBy = shooter;
            myWeaponBroken++; lastBrokenBy = 0;
            sound.Play("you_lose", 0.7f);
            s.StartGhost();
        }
        s.Send();
    }

    // ------------------------------------------------------------------ the hunter's weapon (PHWeapons)
    // A strike, from a desktop slash (MeleeDesktopStrike) or a VR swing (PHWeapons.VrSwing): a hider's prop takes a
    // hit (sent to its owner), a real prop wears the weapon (G17: a chunk comes off it; at nothing left it breaks and
    // the hunter is out) and gets knocked, anything else costs nothing. No healing on a hider hit (Cor): just no wear.
    public PHWeapons weapons;
    public const float MELEE_REACH = 2.2f;
    // this player's own tallies for the session (the trophies read them; nothing synced, rounds do not reset them)
    [HideInInspector] public int statSwings, statPropHits, statHiderHits, statShots, statShotHits, statWeaponsBroken, statPropChanges, myWeaponBroken;
    [HideInInspector] public int lastBrokenBy;   // whose final-stand shot broke this player's weapon last (player id; 0 = a prop did)
    int lastShotSlot = -1, lastShotHealth;
    float lastShotAt = -99f;

    public bool CanStrike()
    {
        PHSlot s = Me();
        return s != null && role == PHGame.ROLE_HUNTER && game.phase == PHGame.PH_HUNT && !s.hunterOut && !aimOverride;
    }

    // a hider's selector shows (PHWeapons: desktop, in the view) while they can pick
    public bool ShowsSelector() { return canPick || CanShoot(); }

    // desktop: what lies within reach in front of the eyes, as the slash comes across
    public void MeleeDesktopStrike()
    {
        PHSlot s = Me();
        if (s == null || role != PHGame.ROLE_HUNTER || game.phase != PHGame.PH_HUNT || s.hunterOut) return;
        float reach = MELEE_REACH * Mathf.Clamp(Networking.LocalPlayer.GetAvatarEyeHeightAsMeters() / 1.6f, 0.5f, 2f);
        lastHitName = "nothing";
        if (!Reach(reach)) { s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.Swung), Aim0(), 0); return; }
        MeleeStrike(hit.collider, hit.point, hit.point - Aim0(), false);
    }

    // a wide, short sphere cast from the eyes (a slash is easier to land than a dart)
    bool Reach(float range)
    {
        RaycastHit r;
        bool any;
        Vector3 o, dir;
        if (aimOverride) { o = aimOrigin; dir = aimDir; }
        else
        {
            VRCPlayerApi.TrackingData t = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            o = t.position; dir = t.rotation * Vector3.forward;
        }
        any = Physics.SphereCast(o, 0.15f, dir, out r, range, ~(1 << 9 | 1 << 10 | 1 << 18), QueryTriggerInteraction.Ignore);
        hit = r;
        return any;
    }

    public void MeleeStrike(Collider c, Vector3 point, Vector3 dir, bool vr)
    {
        PHSlot s = Me();
        if (s == null || role != PHGame.ROLE_HUNTER || game.phase != PHGame.PH_HUNT || s.hunterOut || c == null) return;
        lastHitName = c.gameObject.name + " (" + c.transform.root.name + ")";
        int h = HiderOf(c.transform);
        if (h >= 0)
        {
            PHSlot target = game.slots[h];
            if (target.caught) return;
            int damage = game.Endgame ? HIT_DAMAGE * 2 : HIT_DAMAGE;
            // the target's health as synced, less this hunter's own hits it may not have heard of yet (a second hit
            // within the sync delay counted a second catch)
            // (the base damage is sent; the hider's own client scales it by their size, PHSlot.TakeHit, and the
            // same scaling here predicts the catch)
            int was = target.health, takes = target.HitDamage(damage);
            if (h == lastShotSlot && Time.time - lastShotAt < 2f && lastShotHealth < was) was = lastShotHealth;
            lastShotSlot = h; lastShotAt = Time.time; lastShotHealth = was - takes;
            target.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(PHSlot.TakeHit), damage, Networking.LocalPlayer.playerId);
            s.hits++; statHiderHits++;
            if (was > 0 && was - takes <= 0) s.catches++;
            s.Send();
            sound.Play("confirmation_002", 0.8f);
            s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.Swung), point, 3);
            return;
        }
        int e = EmitOf(c.transform);
        if (e < 0)
        {
            // a wall or a door: nothing happens but sparks (a VR swing through the air never gets here)
            s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.Swung), point, 1);
            return;
        }
        KnockProp(e, dir, point);
        statPropHits++;
        s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.Swung), point, 2);
        // a real prop: it wears the weapon (G17), but the same prop again within 5 s costs nothing
        if (e == lastCharged && Time.time - lastChargedAt < WRONG_SAME_WINDOW) return;
        lastCharged = e; lastChargedAt = Time.time;
        int penalty = game.wrongPenalty;
        if (game.Participants() < 6) penalty = (penalty + 1) / 2;
        if (game.Endgame) penalty *= 2;
        s.wrongProps++;
        s.hunterHealth -= penalty;
        if (s.hunterHealth <= 0)
        {
            // the weapon breaks (PHWeapons shows it for everyone): out, a ghost until the round ends
            s.hunterHealth = 0; s.hunterOut = true;
            sound.Play("you_lose", 0.7f);
            s.StartGhost();
        }
        s.Send();
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
        // a taunt of their own counts as the forced one (the warning says "or taunt now"): its clock starts again
        if (nextForced >= 0f) { nextForced = Time.time + ForcedInterval(); warned = false; checkPos = Networking.LocalPlayer.GetPosition(); }
    }

    // a hunter's dart reached this player's prop (PHSlot.TakeHit, on the owner)
    public void OnHit(int damage, bool nowCaught, int shooter)
    {
        PHSlot s = Me();
        if (s == null) return;   // (the slot table may not have this player yet)
        if (nowCaught)
        {
            Say(T("say.caught"), 5f);
            sound.Play("you_lose", 0.8f);
            s.def = -1; s.Send();
            body.End();
            RestoreEye();
            s.StartGhost();
            SetFrozen(false);
        }
        else Say(game.text.F1("say.hitLeft", s.health.ToString()), 2.5f);
    }

    // ------------------------------------------------------------------ HUD
    void UpdateHud()
    {
        if (hud == null) return;
        PHSlot s = Me();
        string t = "";
        int phase = game.phase;
        if (phase == PHGame.PH_LOBBY)
            t = game.text.F1("hud.lobby", game.Participants().ToString());
        else if (phase == PHGame.PH_LOADING)
            t = game.text.F1("hud.loading", Mathf.RoundToInt(game.gen.progressPermille / 10f).ToString());
        else
        {
            string ph = T(phase == PHGame.PH_BRIEF ? "hud.phase.brief" : (phase == PHGame.PH_HIDE ? "hud.phase.hide" : (phase == PHGame.PH_HUNT ? (game.Endgame ? "hud.phase.endgame" : "hud.phase.hunt") : "hud.phase.over")));
            int left = Mathf.CeilToInt(game.Left);
            t = ph + "  " + (left / 60) + ":" + (left % 60).ToString("00");
            if (s != null && role == PHGame.ROLE_HIDER) t += "\n" + game.text.F3(s.caught ? "hud.hiderCaught" : "hud.hider", s.health.ToString(), s.maxHealth.ToString(), changesLeft.ToString());
            if (s != null && role == PHGame.ROLE_HUNTER) t += "\n" + game.text.F3(s.hunterOut ? "hud.hunterOut" : "hud.hunter", s.hunterHealth.ToString(), s.hits.ToString(), s.wrongProps.ToString());
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
