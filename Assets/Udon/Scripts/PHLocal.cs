using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using TMPro;

// The local player's side of the game (the prototype's Player and PlayerActions, without its own movement:
// VRChat moves the player). One per world; it acts for whoever is running this client.
//   Hider: lands on the map as themselves (Cor) and walks as usual until they pick a prop. Use (desktop click, a tap
//          of the VR right trigger) on a prop within 4 m becomes it (G5, G25); the avatar shrinks to the prop's eye
//          height, sits in its parked station and the synced prop follows them. Holding the VR right trigger grabs the
//          prop and turns it with the hand (PHHiderBody), at once when nothing is aimed at (Cor). A hider still
//          without a prop when the hunt starts gets a random one where they stand. Taunt: T on desktop, the button on
//          the screen device in VR (PHView); no cooldown (Cor).
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
    public PHHiderBody body;
    public PHView view;                  // (the screen device's button shows a trigger's press)             // the hider's prop-shaped body (custom controller in the station)
    public TextMeshPro panelText;
    public TextMeshProUGUI panelUiText;  // the same, on the host panel's canvas
    public TextMeshPro resultsText;      // the last round's results, on the lobby wall
    public Material beaconGreen, beaconRed;
    public string[] defNames;            // readable names of the defs ("k_chairCushion" -> "Chair cushion"), baked        // the host panel's settings, for everyone to read
    float nextPanel;

    // ---- balance (Balance.cs)
    public const float TAKE_RANGE = 4f, WRONG_SAME_WINDOW = 5f, TAUNT_COOLDOWN = 3f;   // (group playtest: a short cooldown, taunts back to back got annoying)
    public const int HIT_DAMAGE = 25, HUNTER_HEALTH = 100, HUNT_CHANGES = 3;   // HUNTER_HEALTH: a new weapon's condition
    public const float HUNT_CHANGE_COOLDOWN = SHOT_INTERVAL;   // a prop change in the hunt waits as long as a final stand shot (Cor; was 5 s)
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
    int hidersAtStart;        // uncaught hiders when the hunt began (the "one left" call)
    bool oneLeftSaid;
    float caughtAt = -100f;
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
        huntJump = role == PHGame.ROLE_HUNTER && phase == PHGame.PH_HUNT;
        game.Log("phase " + phase + " round " + game.round + " slot " + slotIndex + " role " + role);
        if (s != null) s.Claim();
        UpdateOrigin();   // (first: the landing, the release and the reveal below are places in the team's copy of the map)
        switch (phase)
        {
            case PHGame.PH_LOBBY:
                ShowReveal(false);
                if (s != null) s.StopHiding();
                body.End();
                RestoreEye();
                SetPropLayer(LAYER_PROPS_SOLID);
                SetFrozen(false);
                if (role == PHGame.ROLE_NONE && game.lobbySpawn != null && game.round > 0) { movePending = false; me.TeleportTo(game.lobbySpawn.position, game.lobbySpawn.rotation); }
                game.builder.ResetMood();   // the lobby's own light, not the last map's (Cor); UpdateMood keeps it right after
                break;
            case PHGame.PH_LOADING:
                sound.Play("confirmation_001", 0.6f);
                // "get ready" as the round starts, while the map is still being built (Cor)
                sound.Play(sound.Voice("briefing", "ready"), 0.8f);   // (Cor's own voice, a random take)
                if (s != null && role != PHGame.ROLE_NONE) s.ResetFor(game.round, role);
                Say(T(role == PHGame.ROLE_NONE ? "say.watching" : "say.building"), 5f);
                break;
            case PHGame.PH_BRIEF:
                // "hide!" once the hiders land on the map (Cor), for everyone in the round
                if (role != PHGame.ROLE_NONE) sound.Play(sound.Voice("hiders_go", ""), 0.8f);
                if (role == PHGame.ROLE_HUNTER)
                {
                    // hunters wait in the lobby while the hiders hide (Cor); the hunt brings them to the release
                    SetPropLayer(LAYER_PROPS_WALKTHROUGH);
                    SetFrozen(false);
                    game.builder.ResetMood();   // (the map was built meanwhile: its mood stays off the lobby, Cor)
                    Say(T("say.youHunt"), 6f);
                }
                else if (role == PHGame.ROLE_HIDER)
                {
                    SetPropLayer(LAYER_PROPS_SOLID);
                    LandHider();
                    game.builder.WriteMood();
                    // they land as themselves (Cor), on their own feet, and move, pick and open doors at once; the
                    // first prop they pick seats them in it (SetDisguise). The hunters wait in the lobby until the
                    // hunt, so nobody who must not sees them meanwhile.
                    body.End();
                    SetFrozen(false);
                    changesLeft = HUNT_CHANGES;
                    Say(T("say.youHide"), 6f);
                }
                break;
            case PHGame.PH_HIDE:
                if (role == PHGame.ROLE_HIDER) body.frozen = false;
                break;
            case PHGame.PH_HUNT:
                PlayThen(sound.Voice("hunters_go", "go"), 0.8f, "jingles_PIZZI07", 0.5f);
                hidersAtStart = game.HidersLeft(); oneLeftSaid = false;
                if (role == PHGame.ROLE_HUNTER)
                {
                    movePending = false;   // (an explicit move wins over a copy move still waiting, ShiftLocal)
                    me.TeleportTo(game.builder.ToWorld(game.builder.ReleasePoint()) + Vector3.up * 0.1f, Quaternion.Euler(0f, game.builder.ReleaseYaw(), 0f));
                    game.builder.WriteMood();   // out of the lobby: the map's mood
                    SetFrozen(false);
                    Say(T("say.hunt"), 3f);
                }
                else if (role == PHGame.ROLE_HIDER)
                {
                    // still themselves when the hunters come: a random prop, where they stand
                    if (s != null && s.def < 0 && !s.caught && !s.ghost) StartingDisguise();
                    Say(T("say.released"), 5f);
                    nextForced = Time.time + ForcedInterval() * Random.Range(0.35f, 1f);
                    checkPos = me.GetPosition(); warned = false;
                }
                break;
            case PHGame.PH_END:
                ShowReveal(true);
                // the result line only (Cor: the old "time over" call before it is gone)
                // everyone hears the winning side's line (Cor), whatever their own side
                string result = game.hidersWin ? sound.Voice("hiders_win", "you_win") : sound.Voice("hunters_win", "game_over");
                if (result != "") sound.Play(result, 0.8f);
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

    // ------------------------------------------------------------------ landing on the map (Cor, playtest 2026-11-20)
    // A hider lands on one of the map's spawn cells (MapGenUdon: cells with room round them, off the walls), facing the
    // longest clear way from it (MapGenUdon.SpawnFacing) instead of a random way that could look into a wall.
    void LandHider()
    {
        Vector3 at = game.builder.ReleasePoint();
        float facing = Random.Range(0, 24) * 15f;
        if (game.gen.nSpawns > 0)
        {
            int c = game.gen.spawnCell[Random.Range(0, game.gen.nSpawns)];
            at = game.builder.InnerWorld(c);   // upstairs or in a basement too
            facing = game.gen.SpawnFacing(c);
        }
        movePending = false;
        Networking.LocalPlayer.TeleportTo(game.builder.ToWorld(at) + Vector3.up * 0.1f, Quaternion.Euler(0f, facing, 0f));
    }

    // A respawn (VRChat's menu) during a round sent a hider to the lobby, where the hunt then made them a prop (Cor,
    // playtest 2026-11-20). Now a hider goes back onto the map: to a spawn cell, or, hiding in a prop, to the prop (its
    // seat takes them again, PHSlot). A hunter in the hunt goes back to the release. Ghosts, the caught and anyone
    // between rounds respawn in the lobby as before. A frame later: the respawn's own move comes first.
    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;
        SendCustomEventDelayedFrames(nameof(BackOntoMap), 1);
    }

    public void BackOntoMap()
    {
        int phase = game.phase;
        if (phase != PHGame.PH_BRIEF && phase != PHGame.PH_HIDE && phase != PHGame.PH_HUNT) return;
        if (!game.MapReady) return;
        PHSlot s = Me();
        if (s == null || s.caught || s.ghost) return;
        int r = game.RoleOf(slotIndex);
        VRCPlayerApi me = Networking.LocalPlayer;
        movePending = false;   // (this move wins over a copy move still waiting, ShiftLocal)
        if (r == PHGame.ROLE_HIDER)
        {
            if (s.hiding && body.active) me.TeleportTo(body.pos + Vector3.up * 0.1f, Quaternion.Euler(0f, body.yaw, 0f));
            else LandHider();
            game.Log("respawned in the round: back onto the map");
        }
        else if (r == PHGame.ROLE_HUNTER && phase == PHGame.PH_HUNT)
        {
            me.TeleportTo(game.builder.ToWorld(game.builder.ReleasePoint()) + Vector3.up * 0.1f, Quaternion.Euler(0f, game.builder.ReleaseYaw(), 0f));
            game.Log("respawned in the hunt: back to the release");
        }
        else return;
        game.builder.WriteMood();
    }

    // ------------------------------------------------------------------ the light where the player stands (Cor, 2026-11-20)
    // The sun, the ambient light, the fog and the sky are global, and the lobby kept the last map's dusk, night or space
    // sky. Now the lobby has the scene's own light (MapBuilderUdon.ResetMood) and the map its mood (WriteMood), by
    // which of the two the local player is nearer to (the lobby is some 400 m off the map): explore, a respawn, a
    // hunter waiting in the lobby. Twice a second, written only on a change; the phases above also set it at once.
    float nextMood;

    void UpdateMood()
    {
        if (Time.time < nextMood) return;
        nextMood = Time.time + 0.5f;
        MapBuilderUdon b = game.builder;
        if (b == null) return;
        b.moodByZone = true;   // (from now on BeginBuild leaves the mood to this)
        VRCPlayerApi me = Networking.LocalPlayer;
        if (!Utilities.IsValid(me)) return;
        Vector3 p = me.GetPosition();
        Vector3 lobby = game.lobbySpawn != null ? game.lobbySpawn.position : (game.lobbyZone != null ? game.lobbyZone.bounds.center : Vector3.zero);
        Vector3 map = b.ToWorld(new Vector3(b.SiteWidth() * 0.5f, 0f, b.SiteDepth() * 0.5f));   // (this client's copy)
        bool inLobby = !b.built || (p - lobby).sqrMagnitude < (p - map).sqrMagnitude;
        if (inLobby) { if (b.moodShown) b.ResetMood(); }
        else if (!b.moodShown) b.WriteMood();
    }

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
        // a ghost first, so the release below is the one in the ghosts' copy of the map (PHGame.layers)
        s.StartGhost();
        UpdateOrigin();
        movePending = false;
        Networking.LocalPlayer.TeleportTo(game.builder.ToWorld(game.builder.ReleasePoint()) + Vector3.up * 0.1f, Quaternion.Euler(0f, game.builder.ReleaseYaw(), 0f));
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
    // wherever they are (no fall-off, a volumetric radius bigger than the map and the parking, no lowpass). With SEE
    // HIDERS on, hiders and ghosts hear each hider at that same volume but from the hider's prop (want 4).
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
        // (the audio board changed a level: everyone's voice again, at the new gain)
        if (sound != null && sound.ver != voiceVer) { voiceVer = sound.ver; voiceSet = null; }
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
        // (PHGame.layers: a player of another team is on another copy of the map, LAYER_GAP away; their voice falls off
        // by how far they are on the map instead, below)
        bool layered = game.LayersOn();
        int myTeam = me != null ? game.TeamOf(me.index) : PHGame.TEAM_SHARED;
        Vector3 myPos = Networking.LocalPlayer.GetPosition(), myLocal = myPos - game.TeamOrigin(myTeam);
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
            // 1: VRChat's default, 2: a hunter does not hear a hider, 3: hiders hear each other like a radio,
            // 4: SEE HIDERS (Cor, 2026-11-22): hiders and ghosts hear a hider at full volume anywhere on the map, but from
            // where their prop is (PHSlot.UpdateRemote puts the hider's station there on this client)
            int want = (iHunt && theyHide) ? 2 : ((iHide && theyHide) ? 3 : 1);
            if (theyHide && seeMates && (iHide || meGhost)) want = 4;
            // another copy of the map (a hunter heard by a hider or a ghost): VRChat's fall-off over 25 m, begun as far
            // out as the copies lie apart, so they sound as near as they are on the map (only the loudness: the way they
            // are heard from is no guide; the wide volumetric radius keeps it from pointing at the other copy). Coded as
            // 10 + the near distance in whole metres; one that comes out at 0 is VRChat's default
            if (want == 1 && layered && slot >= 0 && game.TeamOf(slot) != myTeam)
            {
                Vector3 pp = p.GetPosition();
                float dw = Vector3.Distance(pp, myPos), dl = Vector3.Distance(pp - game.SlotOrigin(slot), myLocal);
                int nearM = Mathf.Clamp(Mathf.RoundToInt(dw - dl), 0, 999);
                if (nearM > 0) want = 10 + nearM;
            }
            // what was set for this player last (an open-addressed table keyed by player id; a full one starts over)
            int at = pid & (VOICE_HASH - 1), probes = 0;
            while (voiceSetId[at] != 0 && voiceSetId[at] != pid && probes < VOICE_HASH) { at = (at + 1) & (VOICE_HASH - 1); probes++; }
            if (probes >= VOICE_HASH) { voiceSetId = new int[VOICE_HASH]; voiceSet = new int[VOICE_HASH]; at = pid & (VOICE_HASH - 1); }
            if (voiceSetId[at] == pid && voiceSet[at] == want) continue;
            voiceSetId[at] = pid; voiceSet[at] = want;
            float gain = 15f, near = 0f, far = 25f, radius = 0f;
            bool lowpass = true;
            if (want == 2) gain = 0f;
            else if (want == 3) { gain = 9f; near = 10000f; far = 10001f; radius = 1000f; lowpass = false; }   // (quieter: Cor's group playtest)
            else if (want == 4) { gain = 9f; near = 10000f; far = 10001f; radius = 0f; lowpass = false; }      // (the radio's level, but from a point)
            else if (want >= 10) { near = want - 10; far = near + 25f; radius = near; }
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
    int voiceVer = -1;
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
            s.beacon.transform.position = game.builder.ToWorld(new Vector3(s.propPos.x, 0f, s.propPos.z));
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
        me.SetJumpImpulse(frozen ? 0f : (huntJump ? HUNTER_JUMP : 3f));
    }

    // Cor (playtest 2026-11-22): hunters jump higher during the hunt. VRChat's default 3 lifts about 0.46 m; 5 about
    // 1.27 m, a hider's hop (PHHiderBody.hopHeight 1.3) and over a fence (1.05). Everyone else, and the lobby, keep 3.
    // The roofs' boxes and skirts (MapBuilderUdon.CellBody) stop a hunter as they stop a hider.
    const float HUNTER_JUMP = 5f;
    bool huntJump;

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
        // (only one that has room where they stand; a few tries a call, then smaller ones: UpdatePropless asks again)
        int n = game.gen.defDisguise.Length, tried = 0;
        for (int k = 0; k < 60 && tried < 4; k++)
        {
            int d = Random.Range(0, n);
            if (d < game.gen.menu.Length && game.gen.menu[d] && game.gen.defDisguise[d] == 2) { tried++; if (SetDisguise(d)) return; }
        }
        for (int k = 0; k < 60 && tried < 8; k++)
        {
            int d = Random.Range(0, n);
            if (d < game.gen.menu.Length && game.gen.menu[d] && game.gen.defDisguise[d] == 1) { tried++; if (SetDisguise(d)) return; }
        }
    }

    // Become a prop of this type, unless its box has no room here (05 section 3.7)
    bool SetDisguise(int d)
    {
        PHSlot s = Me();
        if (s == null) return false;
        if (!body.active)
        {
            // the first prop: the body starts where the player stands, facing their way, and they sit in it. Only
            // where its box has room (or room close by, FitHere): Cor's prop ended up inside a wall when a player
            // standing against one picked something big, as the landing's short settle could not free it
            VRCPlayerApi me = Networking.LocalPlayer;
            body.Begin(me.GetPosition(), Mathf.Round(me.GetRotation().eulerAngles.y / 15f) * 15f);
            body.frozen = false;
            body.SetProp(d);
            if (!body.FitHere()) { body.End(); Say(T("say.noRoom"), 2f); return false; }
            s.StartHiding();
        }
        else if (s.def >= 0)
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
        s.propPos = game.builder.ToLocal(body.pos);   // (map-local: PHSlot)
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
        if (!value)
        {
            if (!Networking.LocalPlayer.IsUserInVR()) return;
            int h = args.handType == VRC.Udon.Common.HandType.LEFT ? 0 : 1;
            if (ringByTrigger[h]) { ringByTrigger[h] = false; if (body.ringHand == h) body.ringHand = -1; return; }
            if (h == GunHand()) TriggerUp();
            return;
        }
        int phase = game.phase;
        bool vr = Networking.LocalPlayer.IsUserInVR();
        // desktop: a click on a door within reach works it (C7), before anything else
        if (!vr && TryDoor()) return;
        // VR: the other hand's trigger does nothing (Cor: no left-hand device; left-handed players swap, PHHand); a hider's right trigger is a tap or a hold,
        // told apart while it is held and as it is let go (TriggerDown, UpdateTrigger, TriggerUp)
        if (vr && role == PHGame.ROLE_HIDER)
        {
            int h = args.handType == VRC.Udon.Common.HandType.LEFT ? 0 : 1;
            // a trigger with the hand at the screen's button presses it (Cor: the button was hard to press)
            // (the ring only by a grip, PHView: Cor wants the trigger kept for shooting and picking)
            if (TriggerButton(h)) return;
            if (h == GunHand()) TriggerDown();
            return;
        }
        if (vr && (args.handType == VRC.Udon.Common.HandType.LEFT ? 0 : 1) != GunHand()) return;
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
        // a hunter: a click (desktop) or the right trigger (VR) slashes, and strikes what lies within reach in front of
        // the eyes as the weapon comes across (Cor: in VR too, low things were hard to reach by swinging); VR can also
        // swing the hand itself
        else if (role == PHGame.ROLE_HUNTER && phase == PHGame.PH_HUNT && weapons != null) weapons.DesktopSwing();
    }


    bool[] ringByTrigger = new bool[2];

    Vector3 HandAt(int h) { return Networking.LocalPlayer.GetTrackingData(h == 0 ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand).position; }

    float AvatarScale()
    {
        float eye = Networking.LocalPlayer.GetAvatarEyeHeightAsMeters();
        return Mathf.Clamp(eye > 0.01f ? eye / 1.6f : 1f, 0.2f, 5f);
    }

    bool TriggerButton(int h)
    {
        if (body.tauntReach <= 0f) return false;
        VRCPlayerApi me = Networking.LocalPlayer;
        Vector3 tip = me.GetBonePosition(h == 0 ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
        Vector3 hand = HandAt(h);
        bool near = (hand - body.tauntAt).sqrMagnitude < body.tauntReach * body.tauntReach
            || (tip.sqrMagnitude > 1e-6f && (tip - body.tauntAt).sqrMagnitude < body.tauntReach * body.tauntReach);
        if (!near) return false;
        bool done = ButtonTaunt();
        if (view != null) view.ShowPress();
        me.PlayHapticEventInHand(h == 0 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right, 0.08f, 0.8f, 120f);
        sound.Play(done ? "click_001" : "error_004", done ? 0.5f : 0.3f);
        return true;
    }

    bool TriggerRing(int h)
    {
        if (body.ringHand >= 0) return false;
        VRCPlayerApi me = Networking.LocalPlayer;
        if (!body.NearRing(HandAt(h), AvatarScale(), me.GetAvatarEyeHeightAsMeters())) return false;
        body.ringHand = h; ringByTrigger[h] = true;
        me.PlayHapticEventInHand(h == 0 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right, 0.06f, 0.7f, 120f);
        return true;
    }

    // ---- the gun in a VR hider's right hand (Cor: its grip was wrong, and the laser must come out of the gun): posed
    // from the avatar's own hand bones (PHWeapons.HandPose, shared with the hunters' weapons; tracking data for an
    // avatar without them). The pick and shot rays and the laser all start at its muzzle and run along its barrel.
    // (the player's own hold on top, PHHand: the hand it is in, its move and turn)
    public Quaternion VrAimRot()
    {
        if (weapons != null && weapons.hand != null) { weapons.Hold(Networking.LocalPlayer, -1, true); return weapons.holdRot; }
        return AimRotation(Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).rotation);
    }

    public Vector3 VrAimPos()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (weapons != null) { weapons.Hold(me, -1, true); return weapons.holdPos; }
        return me.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
    }

    // the hand that holds the weapon or the gun (0 left, 1 right): the right, unless the player chose left-handed (PHHand)
    public int GunHand() { return weapons != null && weapons.hand != null && weapons.hand.myLeft ? 0 : 1; }

    public Vector3 VrMuzzle()
    {
        if (weapons != null && weapons.vrSelectorModel != null && weapons.vrSelectorModel.gameObject.activeInHierarchy) return weapons.VrMuzzlePoint();
        float len = weapons != null ? weapons.VrMuzzleLen() : 0.135f * AvatarScale();
        return VrAimPos() + VrAimRot() * Vector3.forward * len;
    }

    // the right trigger only picks and shoots, at once on the pull (Cor: turning is the grab ring's alone, by a grip)
    void TriggerDown()
    {
        int phase = game.phase;
        if (role != PHGame.ROLE_HIDER || !(phase == PHGame.PH_BRIEF || phase == PHGame.PH_HIDE || phase == PHGame.PH_HUNT)) return;
        if (game.Endgame) { HiderShoot(); return; }
        if (canPick && weapons != null) weapons.FireSelector();   // the selector kicks with every pick
        TryTakeDisguise();
    }

    void TriggerUp() { }

    void UpdateTrigger() { if (body.turnHeld) body.turnHeld = false; }

    // ------------------------------------------------------------------ the watchdog (Cor: hiders seen by hunters)
    // A seated hider is drawn at this client's copy of their station only once this client has had their enter event
    // (Station Lab), and only while that copy is parked (PHSlot.UpdateRemote). Two ways one viewer can see hiders for a
    // whole session: their copy of a slot's behaviour stopped (an exception halts an UdonBehaviour for good, and it then
    // never parks that station again), or this client missed or misordered an enter event (it then draws the hider at
    // the last place it knew). Twice a second, from this behaviour, for every other player's slot:
    //   - a slot whose behaviour stopped counting its frames is logged once (the reason is in the log just before)
    //   - a hidden player's station (a hider; a ghost, unless this player is one too) is put back at its parking spot
    //     if it is anywhere else
    //   - a hidden player drawn far from that spot for two looks in a row: they are asked to sit again (a fresh enter
    //     event for everyone), at most every WATCH_RETRY seconds, and it is logged
    const float WATCH_EVERY = 0.5f, WATCH_LEAK = 3f, WATCH_RETRY = 4f;
    float watchNext;
    int[] watchBeat, watchLeak;
    float[] watchBeatAt, watchAskAt;
    bool[] watchDead, watchMoved;

    void UpdateWatch()
    {
        if (Time.time < watchNext) return;
        watchNext = Time.time + WATCH_EVERY;
        int n = game.slots.Length;
        if (watchBeat == null || watchBeat.Length != n)
        {
            watchBeat = new int[n]; watchLeak = new int[n]; watchBeatAt = new float[n]; watchAskAt = new float[n];
            watchDead = new bool[n]; watchMoved = new bool[n];
            for (int i = 0; i < n; i++) { watchBeatAt[i] = Time.time; watchAskAt[i] = -99f; }
        }
        int myId = Networking.LocalPlayer.playerId;
        bool meGhost = IsGhost();
        bool reveal = game.phase == PHGame.PH_END;
        for (int i = 0; i < n; i++)
        {
            PHSlot s = game.slots[i];
            if (s == null) continue;
            int pid = game.slotPlayer[i];
            if (s.beat != watchBeat[i]) { watchBeat[i] = s.beat; watchBeatAt[i] = Time.time; }
            else if (!watchDead[i] && Time.time - watchBeatAt[i] > 3f)
            {
                watchDead[i] = true;
                game.Warn("watch: slot " + i + " stopped running (halted?); its station is parked from here now");
            }
            // (never the station this player sits in: for a moment its slot may not count as theirs, and parking it then
            // carried them 400 m under the map; PHLocal.UpdateStrayStation lets them out if it really is someone else's)
            if (pid == 0 || pid == myId || s.seated) { watchLeak[i] = 0; continue; }
            bool hidden = (s.hiding || (s.ghost && !meGhost)) && !reveal;
            if (!hidden) { watchLeak[i] = 0; continue; }
            Vector3 park = s.parkAt + new Vector3(i * 3f, 0f, 0f);
            Transform st = s.station.transform;
            if ((st.position - park).sqrMagnitude > 1f)
            {
                st.SetPositionAndRotation(park, Quaternion.identity);
                if (!watchMoved[i]) { watchMoved[i] = true; game.Warn("watch: slot " + i + " station was not parked; parked it"); }
            }
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
            if (p == null || !Utilities.IsValid(p)) { watchLeak[i] = 0; continue; }
            float d = Vector3.Distance(p.GetPosition(), park);
            if (d < WATCH_LEAK) { watchLeak[i] = 0; continue; }
            watchLeak[i]++;
            if (watchLeak[i] < 2 || Time.time - watchAskAt[i] < WATCH_RETRY) continue;
            watchAskAt[i] = Time.time;
            s.leaksAsked++;
            game.Warn("leak: slot " + i + " (player " + pid + ") drawn " + d.ToString("F1") + " m from its parking spot; asking them to sit again");
            if (game.leakHeal) s.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(PHSlot.RequestReseat));
        }
    }

    // a hider still without a prop in the hunt (the phase change came before their slot, or the pick there failed)
    // gets the random one where they stand; looked at twice a second
    float nextPropless;
    void UpdatePropless()
    {
        if (role != PHGame.ROLE_HIDER || game.phase != PHGame.PH_HUNT || Time.time < nextPropless) return;
        nextPropless = Time.time + 0.5f;
        PHSlot s = Me();
        if (s != null && s.def < 0 && !s.caught && !s.ghost) StartingDisguise();
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
        UpdateOrigin();
        UpdateSeeMates();
        UpdatePlacement();
        UpdateStandIns();
        UpdateWeaponRepair();
        if (Input.GetKeyDown(KeyCode.T)) Taunt();
        if (propLayer >= 0 && game.builder.built && (!layerDone || layerObjs != game.builder.objects)) SetPropLayer(propLayer);
        bool eg = game.Endgame;
        if (eg && !wasEndgame && role != PHGame.ROLE_NONE)
        {
            sound.Play("jingles_HIT03", 0.6f);   // (the old synthesized "hurry up" is gone: Cor's takes only)
            Say(T("say.final"), 4f);
            float iv = game.tauntInterval * 0.5f;
            if (nextForced > Time.time + iv) nextForced = Time.time + iv;
        }
        wasEndgame = eg;
        // one hider left (Cor's "One left!" takes), once a round, when it began with two or more; not for the one
        // just caught, who hears their own line
        if (game.phase == PHGame.PH_HUNT && !oneLeftSaid && hidersAtStart >= 2 && game.HidersLeft() == 1)
        {
            oneLeftSaid = true;
            if (Time.time - caughtAt > 3f) sound.Play(sound.Voice("one_left", ""), 0.8f);
        }
        UpdateTrigger();
        UpdateWatch();
        UpdatePropless();
        UpdateForcedTaunt();
        UpdateReleaseCount();
        UpdateVoices();
        UpdateStrayStation();
        UpdateJoiner();
        UpdateMood();
        UpdateHud();
        UpdateHover();
        UpdateRocks();
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
    // (the gun as the avatar's hand holds it, PHWeapons.HandPose, so picks and shots go where the blaster points;
    // the tracking angle only for an avatar without hand bones)
    public Quaternion AimRotation(Quaternion hand)
    {
        if (weapons != null && weapons.HandPose(Networking.LocalPlayer, GunHand() == 0)) return weapons.handGun;
        return hand * Quaternion.Euler(vrAimEuler);
    }

    void UpdateHover()
    {
        int phase = game.phase;
        bool can = role == PHGame.ROLE_HIDER && (phase == PHGame.PH_BRIEF || phase == PHGame.PH_HIDE || (phase == PHGame.PH_HUNT && !game.Endgame)) && !aimOverride && !IsGhost();
        // in the hunt only while a change can be made now (not within HUNT_CHANGE_COOLDOWN of the last; no limit on how
        // many, Cor after the group playtest)
        if (can && phase == PHGame.PH_HUNT && Time.time - lastChange < HUNT_CHANGE_COOLDOWN) can = false;
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
            else
            {
                // another hider's prop: its disguise can be copied (TryTakeDisguise); it glows the same way
                int h = CopyFrom(c);
                if (h >= 0 && game.slots[h].squashRig != null) { o = game.slots[h].squashRig.gameObject; d = game.slots[h].def; }
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

    // how far the forced taunt's clock has run for this hider: 0 just after a taunt (their own, or a forced one, or a
    // skip for having moved) .. 1 as the forced one comes; -1 while there is none (not a hider in the hunt). PHView
    // draws it as the ring round the taunt button (VR) and the small ring on the desktop HUD (hop playtest, Cor)
    public float ForcedProgress(PHSlot s)
    {
        if (role != PHGame.ROLE_HIDER || game.phase != PHGame.PH_HUNT || nextForced < 0f || s == null || s.caught || s.ghost) return -1f;
        float iv = ForcedInterval();
        if (iv <= 0.01f) return -1f;
        return Mathf.Clamp01(1f - (nextForced - Time.time) / iv);
    }

    // the last five seconds before the hunters come, counted out loud for everyone in the round (Cor: an indicator for
    // when the hunters spawn in)
    int releaseShown = -1;
    void UpdateReleaseCount()
    {
        if (game.phase != PHGame.PH_HIDE || role == PHGame.ROLE_NONE) { releaseShown = -1; return; }
        int n = Mathf.CeilToInt(game.Left);
        if (n > 5 || n < 1 || n == releaseShown) return;
        releaseShown = n;
        sound.Play("tick_001", 0.8f);
        Say(game.text.F1("say.releaseIn", n.ToString()), 1.2f);
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
        Vector3 o, dir;
        if (me.IsUserInVR()) { o = VrMuzzle(); dir = VrAimRot() * Vector3.forward; }
        else { VRCPlayerApi.TrackingData t = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head); o = t.position; dir = t.rotation * Vector3.forward; }
        // (not the screen device in the hand or beside them: it is a pickup, layer 13)
        any = Physics.SphereCast(o, 0.04f, dir, out r, range, ~(1 << 9 | 1 << 10 | 1 << 18 | 1 << 13), QueryTriggerInteraction.Ignore);
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

    // another hider whose disguise this hider can copy, from a collider of their prop: a hider of this round, hiding,
    // not caught, wearing a prop that can be a disguise; -1 if not
    int CopyFrom(Collider c)
    {
        if (c == null) return -1;
        int h = HiderOf(c.transform);
        if (h < 0 || h >= game.slots.Length || h == slotIndex || game.RoleOf(h) != PHGame.ROLE_HIDER) return -1;
        PHSlot o = game.slots[h];
        if (o == null || !o.hiding || o.caught || o.ghost || o.def < 0 || o.def >= game.gen.defDisguise.Length || game.gen.defDisguise[o.def] <= 0) return -1;
        return h;
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
        if (game.Endgame) return;   // the final stand: the disguise is kept to the end (Cor)
        if (!Aim(TAKE_RANGE)) return;
        int e = EmitOf(hit.collider.transform);
        int d;
        if (e >= 0) d = game.gen.emitDef[e];
        else
        {
            // another hider's prop (Cor, hop playtest: "copy disguise"): the same pick copies their disguise; they keep
            // theirs, and nothing is asked of them
            int h = CopyFrom(hit.collider);
            if (h < 0) return;
            d = game.slots[h].def;
            if (d == s.def) { sound.Play("error_004", 0.5f); return; }
        }
        // a prop that cannot be taken says so, and a change that took has its own cue; both heard only by the hider (Cor)
        if (game.gen.defDisguise[d] <= 0) { sound.Play("error_004", 0.5f); Say(T("say.notDisguise"), 2f); return; }
        if (game.phase == PHGame.PH_HUNT)
        {
            if (Time.time - lastChange < HUNT_CHANGE_COOLDOWN) { sound.Play("error_004", 0.5f); return; }
        }
        if (!SetDisguise(d)) { sound.Play("error_004", 0.5f); return; }
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
    public const int SHOT_WEAR = 15;   // (Cor, hop playtest: 5 was too little)
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
            if (me.IsUserInVR()) { o = VrMuzzle(); dir = VrAimRot() * Vector3.forward; }
            else { VRCPlayerApi.TrackingData t = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head); o = t.position; dir = t.rotation * Vector3.forward; }
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
            Vector3 feet = game.SeenHere(i, p.GetPosition());   // (where this client draws them: their stand-in, PHGame.layers)
            float top = Mathf.Max(0.5f, p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position.y - feet.y + 0.15f);
            float tAt = RayToUpright(o, dir, feet, top);
            if (tAt < 0f || tAt >= bestT) continue;
            best = i; bestT = tAt;
        }
        Vector3 end = o + dir * bestT;
        // the first shot gives the hider away: their gun floats over their prop for everyone from now on (PHWeapons;
        // they see it too, in their third-person view), and they are told so (hop playtest)
        if (!s.armed) { s.armed = true; s.Send(); Say(T("say.gunSeen"), 4f); }
        if (weapons != null) weapons.FireSelector();
        statShots++;
        s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.Shot), game.builder.ToLocal(end), best);   // (map-local)
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
        if (s == null || role != PHGame.ROLE_HUNTER || game.phase != PHGame.PH_HUNT || s.hunterOut || s.weaponRecovering) return;
        s.hunterHealth -= wear;
        sound.Play("impactMetal_light_002", 0.8f);
        lastWearAt = Time.time;
        if (s.hunterHealth <= 0)
        {
            // the weapon breaks (Cor): it shatters for everyone (PHWeapons) and grows back from nothing, unusable and
            // dithered meanwhile; in the final stand it stays broken and the hunter is out, a ghost to the end
            WeaponBreaks(s, shooter);
        }
        s.Send();
    }

    // ---- the weapon's own repair (Cor, group playtest: hunters were out too fast): it grows back a point at a time
    // while no wrong prop or shot has worn it for a while; a broken one grows back from nothing faster, and is usable
    // again once whole
    public const float REGROW_DELAY = 3f, REGROW_EVERY = 3f, RECOVER_EVERY = 1f;
    public const int RECOVER_STEP = 5;   // a broken weapon: 5 points a second, whole again in 20 s
    float lastWearAt = -99f, nextRegrow;

    void WeaponBreaks(PHSlot s, int brokenBy)
    {
        s.hunterHealth = 0;
        myWeaponBroken++; lastBrokenBy = brokenBy;
        lastWearAt = Time.time; nextRegrow = Time.time + RECOVER_EVERY;
        if (game.Endgame)
        {
            s.hunterOut = true;
            s.StartGhost();
        }
        else s.weaponRecovering = true;
    }

    void UpdateWeaponRepair()
    {
        if (role != PHGame.ROLE_HUNTER || game.phase != PHGame.PH_HUNT || Time.time < nextRegrow) return;
        PHSlot s = Me();
        if (s == null || s.hunterOut) return;
        if (s.weaponRecovering)
        {
            nextRegrow = Time.time + RECOVER_EVERY;
            s.hunterHealth = Mathf.Min(HUNTER_HEALTH, s.hunterHealth + RECOVER_STEP);
            if (s.hunterHealth >= HUNTER_HEALTH) s.weaponRecovering = false;
            s.Send();
            return;
        }
        if (s.hunterHealth >= HUNTER_HEALTH || Time.time - lastWearAt < REGROW_DELAY) return;
        nextRegrow = Time.time + REGROW_EVERY;
        s.hunterHealth++;
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
        return s != null && role == PHGame.ROLE_HUNTER && game.phase == PHGame.PH_HUNT && !s.hunterOut && !s.weaponRecovering && !aimOverride;
    }

    // a hider's selector shows (PHWeapons: desktop, in the view) while they can pick
    // desktop's selector in the corner: shown the whole time hiding, as VR keeps it in the hand (PHView); it used to
    // show only while a pick was allowed, so it vanished for the wait after every prop change in the hunt (Cor: never hide the gun)
    public bool ShowsSelector()
    {
        if (canPick || CanShoot()) return true;
        PHSlot s = Me();
        int ph = game.phase;
        return s != null && role == PHGame.ROLE_HIDER && s.hiding && !s.caught && !s.ghost && s.def >= 0
            && (ph == PHGame.PH_BRIEF || ph == PHGame.PH_HIDE || ph == PHGame.PH_HUNT);
    }

    // desktop: what lies within reach in front of the eyes, as the slash comes across
    public void MeleeDesktopStrike()
    {
        PHSlot s = Me();
        if (s == null || role != PHGame.ROLE_HUNTER || game.phase != PHGame.PH_HUNT || s.hunterOut) return;
        // (hop playtest, Cor: low props were hard to hit): the reach always gets past the player's own feet, whatever
        // the avatar's eye height (a tall avatar's slash ended above the floor), looking down at a prop on the floor
        // about half a metre in front of them
        float eye = Networking.LocalPlayer.GetAvatarEyeHeightAsMeters();
        float reach = Mathf.Max(MELEE_REACH * Mathf.Clamp(eye / 1.6f, 0.5f, 3f), eye + 0.6f);
        lastHitName = "nothing";
        if (!Reach(reach)) { s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.Swung), game.builder.ToLocal(Aim0()), 0); return; }
        Collider c = hit.collider; Vector3 at = hit.point;
        // the slash met the floor or a wall right by a low prop (the wide cast grazes the floor before the prop):
        // the prop it was meant for, if one is that close and in plain sight of the contact
        if (StrikeKind(c) == 1 && Assist(hit.point, hit.normal, 0.3f * AvatarScale())) { c = assistCol; at = assistPoint; }
        MeleeStrike(c, at, at - Aim0(), false);
    }

    // what a strike would meet: 3 a hider's prop, 2 a map prop, 1 anything else (a wall, the floor), 0 nothing
    public int StrikeKind(Collider c)
    {
        if (c == null) return 0;
        if (HiderOf(c.transform) >= 0) return 3;
        if (EmitOf(c.transform) >= 0) return 2;
        return 1;
    }

    // a strike that met the floor or a wall at 'point': the nearest hider's prop (else map prop) within 'radius' of it
    // that the contact can see (nothing solid in between, so never through a wall). Sets assistCol / assistPoint.
    [System.NonSerialized] public Collider assistCol;
    [System.NonSerialized] public Vector3 assistPoint;

    public bool Assist(Vector3 point, Vector3 normal, float radius)
    {
        assistCol = null;
        Vector3 from = point + normal * 0.05f;
        Collider[] near = Physics.OverlapSphere(from, radius, ~(1 << 9 | 1 << 10 | 1 << 18 | 1 << 13), QueryTriggerInteraction.Ignore);
        if (near == null) return false;
        int bestK = 1; float bestD = 1e9f;
        for (int i = 0; i < near.Length; i++)
        {
            Collider c = near[i];
            if (c == null) continue;
            int k = StrikeKind(c);
            if (k < 2) continue;
            Vector3 to = c.bounds.center;
            float d = (to - from).sqrMagnitude;
            if (k < bestK || (k == bestK && d >= bestD)) continue;
            // in plain sight of the contact: the first thing on the way there is this prop itself
            RaycastHit r;
            Vector3 way = to - from;
            float len = way.magnitude;
            if (len > 1e-3f && Physics.Raycast(from, way / len, out r, len, ~(1 << 9 | 1 << 10 | 1 << 18 | 1 << 13), QueryTriggerInteraction.Ignore) && StrikeKind(r.collider) < 2) continue;
            bestK = k; bestD = d; assistCol = c;
            assistPoint = Vector3.Lerp(from, to, 0.5f);
        }
        return assistCol != null;
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
            // in third person the view (and its tracking) sits a few metres behind the avatar, so the reach would end
            // before it got past the player (Cor's group playtest: no attacks in third person): reach from the
            // avatar's own head, along the view
            Vector3 hb = Networking.LocalPlayer.GetBonePosition(HumanBodyBones.Head);
            if (hb != Vector3.zero && (hb - o).sqrMagnitude > 0.25f) o = hb;
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
            s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.Swung), game.builder.ToLocal(point), 3);   // (map-local)
            return;
        }
        int e = EmitOf(c.transform);
        if (e < 0)
        {
            // a wall or a door: nothing happens but sparks (a VR swing through the air never gets here)
            s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.Swung), game.builder.ToLocal(point), 1);
            return;
        }
        KnockProp(e, dir, point);
        statPropHits++;
        s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.Swung), game.builder.ToLocal(point), 2);
        // scenery (Cor): what no person placed is no disguise, and a swing at it wears nothing
        int ed = game.gen.emitDef[e];
        if (ed >= 0 && ed < game.gen.defDisguise.Length && game.gen.defDisguise[ed] <= 0) return;
        // a real prop: it wears the weapon (G17), but the same prop again within 5 s costs nothing
        if (e == lastCharged && Time.time - lastChargedAt < WRONG_SAME_WINDOW) return;
        lastCharged = e; lastChargedAt = Time.time;
        int penalty = game.wrongPenalty;
        if (game.Participants() < 6) penalty = (penalty + 1) / 2;
        if (game.Endgame) penalty *= 2;
        s.wrongProps++;
        s.hunterHealth -= penalty;
        lastWearAt = Time.time;
        if (s.hunterHealth <= 0)
        {
            // the weapon breaks (Cor): it shatters for everyone (PHWeapons) and grows back from nothing, unusable and
            // dithered meanwhile; in the final stand it stays broken and the hunter is out, a ghost to the end
            WeaponBreaks(s, 0);
        }
        s.Send();
    }

    Vector3 Aim0()
    {
        if (aimOverride) return aimOrigin;
        VRCPlayerApi me = Networking.LocalPlayer;
        return (me.IsUserInVR() ? me.GetTrackingData(GunHand() == 0 ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand) : me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head)).position;
    }

    // a blow knocks a real prop (04 section 4.8): light ones fly, heavy ones and trees are shoved (Cor: most things
    // knockable, trees too); only the fixed dressing (kitchen units, fountains) rocks where it stands
    void KnockProp(int e, Vector3 dir, Vector3 point)
    {
        int fl = game.gen.defFlags[game.gen.emitDef[e]];
        GameObject[] objs = game.builder.objects;
        bool body = objs != null && e < objs.Length && objs[e] != null && objs[e].GetComponent<Rigidbody>() != null;
        if ((fl & GenData.FL_FIXTURE) != 0 || !body)
        {
            // a fixed thing does not move, but it rocks where it stands, for everyone (Cor: some props did not move
            // when hit)
            PHSlot s = Me();
            if (s != null) s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.Rocked), e, dir);
            return;
        }
        PHKnock k = game.KnockOf(e);
        if (k != null && k.Busy) return;   // already flying
        if (k == null) k = game.FreeKnock();
        if (k != null) k.Knock(e, dir, point);
    }

    // ------------------------------------------------------------------ a heavy prop rocks when hit
    // It tips a few degrees away from the blow about its own foot and settles back in under a second, with what stands
    // on it. Only the look: it ends exactly where it was (the synced map is unchanged). Up to ROCK_MAX at once.
    const int ROCK_MAX = 4, ROCK_RIDERS = 8;
    const float ROCK_TIME = 0.9f;
    Transform[] rockT;
    Vector3[] rockPos, rockAxis;
    Quaternion[] rockRot;
    float[] rockAt, rockDeg;
    Transform[] riderT;
    Vector3[] riderPos;
    Quaternion[] riderRot;
    int[] riderOf;

    public void Rock(int e, Vector3 dir)
    {
        GameObject[] objs = game.builder.objects;
        if (objs == null || e < 0 || e >= objs.Length || objs[e] == null) return;
        if (rockT == null)
        {
            rockT = new Transform[ROCK_MAX]; rockPos = new Vector3[ROCK_MAX]; rockAxis = new Vector3[ROCK_MAX]; rockRot = new Quaternion[ROCK_MAX];
            rockAt = new float[ROCK_MAX]; rockDeg = new float[ROCK_MAX];
            riderT = new Transform[ROCK_MAX * ROCK_RIDERS]; riderPos = new Vector3[ROCK_MAX * ROCK_RIDERS]; riderRot = new Quaternion[ROCK_MAX * ROCK_RIDERS];
        }
        Transform t = objs[e].transform;
        int slot = -1;
        for (int i = 0; i < ROCK_MAX; i++) if (rockT[i] == t) { slot = i; break; }
        if (slot >= 0) { rockAt[slot] = Time.time; return; }   // already rocking: once more from where it began
        for (int i = 0; i < ROCK_MAX && slot < 0; i++) if (rockT[i] == null) slot = i;
        if (slot < 0) return;
        Vector3 h = new Vector3(dir.x, 0f, dir.z);
        if (h.sqrMagnitude < 1e-4f) h = Vector3.forward;
        rockT[slot] = t; rockPos[slot] = t.position; rockRot[slot] = t.rotation;
        rockAxis[slot] = Vector3.Cross(Vector3.up, h.normalized);
        rockAt[slot] = Time.time;
        int fl = game.gen.defFlags[game.gen.emitDef[e]];
        rockDeg[slot] = (fl & GenData.FL_SCENERY) != 0 ? 2.5f : 4f;
        // what stands on it rocks with it
        int[] par = game.gen.emitParent;
        int n = 0, b = slot * ROCK_RIDERS;
        for (int r = 0; r < ROCK_RIDERS; r++) riderT[b + r] = null;
        int count = Mathf.Min(objs.Length, par.Length);
        for (int r = 0; r < count && n < ROCK_RIDERS; r++)
        {
            if (par[r] != e || objs[r] == null) continue;
            PHKnock k = game.KnockOf(r);
            if (k != null) continue;   // knocked off already: it lies where it fell
            Transform rt = objs[r].transform;
            riderT[b + n] = rt; riderPos[b + n] = rt.position; riderRot[b + n] = rt.rotation; n++;
        }
        // (no sound of its own: the blow already sounds, PHWeapons.OnSwung)
    }

    void UpdateRocks()
    {
        if (rockT == null) return;
        for (int i = 0; i < ROCK_MAX; i++)
        {
            Transform t = rockT[i];
            if (t == null) continue;
            float a = Time.time - rockAt[i];
            bool done = a >= ROCK_TIME || game.builder.objects == null;
            float deg = done ? 0f : rockDeg[i] * Mathf.Exp(-5f * a) * Mathf.Sin(a * 22f + 0.6f) / 0.565f;
            Quaternion q = Quaternion.AngleAxis(deg, rockAxis[i]);
            Vector3 pivot = rockPos[i];
            t.SetPositionAndRotation(pivot, q * rockRot[i]);
            int b = i * ROCK_RIDERS;
            for (int r = 0; r < ROCK_RIDERS; r++)
            {
                Transform rt = riderT[b + r];
                if (rt == null) continue;
                rt.SetPositionAndRotation(pivot + q * (riderPos[b + r] - pivot), q * riderRot[b + r]);
                if (done) riderT[b + r] = null;
            }
            if (done) rockT[i] = null;
        }
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
        if (view != null) view.ShowTauntBolts();   // (VR: bolts fly out of the screen device's speaker grill)
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
            sound.Play(sound.Voice("caught", "you_lose"), 0.8f);
            caughtAt = Time.time;
            s.def = -1; s.Send();
            body.End();
            RestoreEye();
            s.StartGhost();
            SetFrozen(false);
        }
        else Say(game.text.F1("say.hitLeft", s.health.ToString()), 2.5f);
    }

    // ------------------------------------------------------------------ the team's copy of the map (PHGame.layers)
    // Each frame (and at once on a phase change): the copy this player's team plays on (PHGame.TeamOf, TeamOrigin) is
    // given to the builder, which moves the map there (at once if it stands, else once built). When the copy this
    // player stands in moves (a hider caught, a hunter out, the END reveal back on the shared copy), everything this
    // client holds as a world place moves with it (ShiftLocal), and the player too, when they are on the map.
    Vector3 lastOrigin;          // the copy the player's place was last relative to
    Vector3 moveTo;              // a move waiting for the seat to be left (ShiftLocal)
    Quaternion moveRot;
    bool movePending;
    [System.NonSerialized] public float moveUntil;   // until then the player's place is in flux (PHSlot: no ghost walk sent)

    void UpdateOrigin()
    {
        MapBuilderUdon b = game.builder;
        PHSlot s = game.LocalSlot();
        b.SetOrigin(game.TeamOrigin(game.TeamOf(s != null ? s.index : -1)));
        if (b.origin == lastOrigin) return;
        Vector3 was = lastOrigin;
        lastOrigin = b.origin;
        ShiftLocal(b.origin - was, was);
    }

    void ShiftLocal(Vector3 d, Vector3 was)
    {
        if (body.active) body.MoveBy(d);
        checkPos += d;
        ShiftRocks(d);
        VRCPlayerApi me = Networking.LocalPlayer;
        // (a move still waiting: from where it goes, so two moves close together end on the right copy)
        Vector3 p = movePending ? moveTo : me.GetPosition(), at = p - was;
        float w = game.builder.SiteWidth(), dp = game.builder.SiteDepth();
        // (on the map: within its site and a margin; not in the lobby, not parked under it)
        bool onMap = at.x > -80f && at.z > -80f && at.x < w + 80f && at.z < dp + 80f && at.y > -150f;
        game.Log("map copy moved by " + d.x.ToString("F0") + " m" + (onMap ? ", the player with it" : ""));
        if (!onMap) return;
        PHSlot s = game.LocalSlot();
        if (s != null)
        {
            Transform st = s.station.transform;
            st.position = st.position + d;   // (a seat left now drops them where it is)
            // hiding: the seat (an Immobilize station, at the moved body: PHSlot.UpdateOwner) carries them
            if (s.seated && s.hiding && body.active) return;
        }
        if (!movePending) moveRot = me.GetRotation();
        moveTo = p + d;
        if (movePending) return;   // (the waiting move goes there instead)
        // a teleport does not take a seated player: out of the seat first (a ghost's), the move two frames later
        if (s != null && (s.seated || s.hiding || s.ghost))
        {
            s.LeaveSeatForMove();
            movePending = true; moveUntil = Time.time + 1f;
            SendCustomEventDelayedFrames(nameof(_ShiftMove), 2);
        }
        else me.TeleportTo(moveTo, moveRot);
    }

    public void _ShiftMove()
    {
        if (!movePending) return;
        movePending = false;
        moveUntil = Time.time + 0.2f;
        Networking.LocalPlayer.TeleportTo(moveTo, moveRot);
    }

    void ShiftRocks(Vector3 d)
    {
        if (rockT == null) return;
        for (int i = 0; i < ROCK_MAX; i++) if (rockT[i] != null) rockPos[i] = rockPos[i] + d;
        for (int r = 0; r < riderT.Length; r++) if (riderT[r] != null) riderPos[r] = riderPos[r] + d;
    }

    // ---- hiders see each other (PHGame.seeHiders): the other hiders' props carry an edge cage (PHSlot.UpdateMateCage)
    // while this player is a hider or a ghost in the round
    [System.NonSerialized] public bool seeMates;

    void UpdateSeeMates()
    {
        PHSlot s = game.LocalSlot();
        int ph = game.phase;
        seeMates = game.seeHiders && s != null && (ph == PHGame.PH_BRIEF || ph == PHGame.PH_HIDE || ph == PHGame.PH_HUNT)
            && (s.ghost || game.RoleOf(s.index) == PHGame.ROLE_HIDER);
    }

    // ---- where players may place VRChat's own items (Cor, 2026-11-22: instead of ANTI TROLL; VRCPlacementZone). Placed
    // things stand at the player's true position, so a caught hider's sticker or item could mark a live hider's spot
    // (memory: vrchat-station-info-leak). Two zones on one box (WorldSceneBuilder) that this fits round the built map:
    // portalBlock always blocks user portals there (the lobby keeps them); placeBlock blocks stickers, sharing pedestals
    // and attached items while the host's NO PLACING switch is on (PHGame.noPlacing). Every client derives the same box
    // and switch from the synced map and setting, as VRChat asks of zones changed at runtime; nothing here is synced.
    public Transform placementBox;
    public GameObject placeBlock;
    const float PLACE_MARGIN = 30f, PLACE_BELOW = 30f, PLACE_HEIGHT = 110f;   // past the fence; under the basements, over the roofs
    int placeW = -1, placeH = -1;
    Vector3 placeOrigin;

    void UpdatePlacement()
    {
        if (placementBox == null) return;
        MapBuilderUdon b = game.builder;
        int w = b.built ? b.siteW : 0, h = b.built ? b.siteH : 0;
        if (w != placeW || h != placeH || b.origin != placeOrigin)
        {
            placeW = w; placeH = h; placeOrigin = b.origin;
            // before the first map: a box over where maps stand (from the world's origin out), well clear of the lobby
            float x = w > 0 ? w * 4f : 200f, z = h > 0 ? h * 4f : 200f;
            placementBox.position = b.origin + new Vector3(x * 0.5f, PLACE_HEIGHT * 0.5f - PLACE_BELOW, z * 0.5f);
            placementBox.localScale = new Vector3(x + 2f * PLACE_MARGIN, PLACE_HEIGHT, z + 2f * PLACE_MARGIN);
        }
        if (placeBlock != null && placeBlock.activeSelf != game.noPlacing) placeBlock.SetActive(game.noPlacing);
    }

    // ---- the hunters' stand-ins (PHGame.layers): a hider or a ghost plays on another copy of the map than the hunters,
    // so the hunters' avatars are not there. Each hunter in the hunt is drawn there instead as a plain body of their
    // height (one per slot, WorldSceneBuilder), where they stand on the map, facing their way; their weapon is drawn
    // in the same place (PHWeapons.PlaceOther). No collider: nobody can be blocked by one.
    public Transform[] standIns;
    bool standInsOut;

    void UpdateStandIns()
    {
        if (standIns == null || standIns.Length == 0) return;
        PHSlot ms = game.LocalSlot();
        bool on = game.phase == PHGame.PH_HUNT && game.LayersOn() && game.TeamOf(ms != null ? ms.index : -1) != PHGame.TEAM_SHARED;
        if (!on)
        {
            if (!standInsOut) return;
            standInsOut = false;
            for (int i = 0; i < standIns.Length; i++) if (standIns[i] != null && standIns[i].gameObject.activeSelf) standIns[i].gameObject.SetActive(false);
            return;
        }
        standInsOut = true;
        int n = Mathf.Min(standIns.Length, game.slots.Length);
        for (int i = 0; i < n; i++)
        {
            Transform t = standIns[i];
            if (t == null) continue;
            int pid = game.slotPlayer[i];
            bool show = pid != 0 && game.slotRole[i] == PHGame.ROLE_HUNTER && !game.slots[i].hunterOut && game.TeamOf(i) == PHGame.TEAM_SHARED;
            VRCPlayerApi p = show ? VRCPlayerApi.GetPlayerById(pid) : null;
            if (show && (p == null || !Utilities.IsValid(p) || p.isLocal)) show = false;
            if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
            if (!show) continue;
            Vector3 feet = p.GetPosition();
            float h = Mathf.Clamp(p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position.y - feet.y + 0.12f, 0.5f, 3f);
            t.SetPositionAndRotation(game.SeenHere(i, feet) + Vector3.up * (h * 0.5f), Quaternion.Euler(0f, p.GetRotation().eulerAngles.y, 0f));
            t.localScale = new Vector3(0.45f, h * 0.5f, 0.45f);
        }
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
