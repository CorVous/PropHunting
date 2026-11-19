using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

// The round's flow, owned by the instance master (the host): LOBBY -> LOADING (every client generates and builds
// the map from the synced seed) -> BRIEF -> HIDE -> HUNT -> END (the reveal) -> LOBBY. Phase deadlines are server
// times, so every client runs the same clock without per-frame sync.
// Players have one PHSlot each (assigned here, on join); a slot carries the player's own synced state (role
// progress, disguise, health) and owns their station for hiding (station parking, Station Lab).
// The host's settings (round length, taunt interval, wrong-prop penalty, size, density) are synced here too;
// HostPanel calls the Set* methods, which only act for the host.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PHGame : UdonSharpBehaviour
{
    public const int PH_LOBBY = 0, PH_LOADING = 1, PH_BRIEF = 2, PH_HIDE = 3, PH_HUNT = 4, PH_END = 5;
    public const int ROLE_NONE = 0, ROLE_HIDER = 1, ROLE_HUNTER = 2;

    public MapGenUdon gen;
    public MapBuilderUdon builder;
    public PHSlot[] slots;
    public PHLocal local;
    public PHKnock[] knocks;            // the pool of synced prop knocks
    public PHSeed seedTool;             // seed codes (encode, decode)

    // ---- seeds (04 section 6.1): the code of the map being (or last) played, and a code the host entered for
    // the next map. A code for another size than the players fit, or from an older generator, waits for the
    // host's "play anyway" or "discard".
    [UdonSynced] public string seedCode = "";
    [UdonSynced] public string pendingCode = "";
    [UdonSynced] public string seedStatus = "";
    [UdonSynced] public bool seedNeedsConfirm;
    string confirmCode = "";

    // ---- map rotation (C1, C6, D2-D6, D8, G49): three maps per size, played in turn; each keeps its run count
    // while the group plays other sizes. After runCap runs (host setting, default 10) a map retires and a new
    // one takes its place. Between rounds, more than half of everyone here can vote to scrap the last map
    // (replaced at once) or to undo the last scrap (one level). A shared seed plays next as a one-off.
    public const int ROT_PER_SIZE = 3;
    [UdonSynced] public uint[] rotSeed = new uint[12];        // size * 3 + i; 0 = not made yet
    [UdonSynced] public int[] rotRuns = new int[12];
    [UdonSynced] public int[] rotDensity = new int[12];
    [UdonSynced] public int[] rotNext = new int[4];
    [UdonSynced] public int runCap = 10;
    [UdonSynced] public int lastRot = -1;                     // the rotation slot the last round played (-1: a one-off)
    [UdonSynced] public int undoRot = -1;                     // the slot the last scrap replaced, and what it held
    [UdonSynced] public uint undoSeed;
    [UdonSynced] public int undoRuns, undoDensity;
    [UdonSynced] public int voteId = 1;                       // votes count only for the current id (PHSlot.scrapVote/undoVote)
    public BoxCollider lobbyZone;
    public Transform lobbySpawn;
    public int minPlayers = 2;          // START needs this many in the zone (1 for solo tests: everyone hides)
    public bool soloHides = true;       // with one player, that player hides (a test aid)

    // ---- synced: the round
    [UdonSynced] public int phase;
    [UdonSynced] public int round;              // counts up with every START; slots report readiness for this round
    [UdonSynced] public int phaseEndMs;          // server time when the phase ends (0 = no deadline)
    [UdonSynced] public int huntStartMs;
    [UdonSynced] public uint seed;
    [UdonSynced] public int mapSize, mapDensity;
    [UdonSynced] public int mapEvo;             // the map's evolution step (0 to 3, MapGen.evo)
    [UdonSynced] public int[] slotPlayer = new int[32];   // player id per slot, 0 = free
    [UdonSynced] public int[] slotRole = new int[32];     // ROLE_* for this round
    // the host is whoever owns this object (the instance master to begin with). Locked, only the host keeps
    // it; unlocked, anyone may claim it (the host panel's Unlock host / Claim host)
    [UdonSynced] public bool hostLocked = true;
    // handover (C5): an unlock lapses after 60 s; a host idle for 5 minutes can be replaced by anyone; when the
    // host leaves there is a 30 s claim window, then the player who has been here longest becomes host
    [UdonSynced] public int unlockedAtMs;
    [UdonSynced] public int hostActiveMs;
    [UdonSynced] public int claimWindowEndMs;
    [UdonSynced] public bool handoverPending;
    [UdonSynced] public int lastHostId;
    [UdonSynced] public int[] slotJoinMs = new int[32];
    public const int UNLOCK_LAPSE_MS = 60000, HOST_IDLE_MS = 300000, CLAIM_WINDOW_MS = 30000;
    [UdonSynced] public bool hidersWin;
    [UdonSynced] public string endReason = "";
    [UdonSynced] public bool timeUp;   // the round ended on the clock (the "time over" call is heard first)

    // ---- synced: the host's settings (G14, G44)
    [UdonSynced] public bool sizeAuto = true;
    [UdonSynced] public int sizeChoice = 1;
    [UdonSynced] public int densityChoice;
    [UdonSynced] public int roundLength = 1;           // Short, Normal, Long
    [UdonSynced] public int tauntInterval = 40;
    [UdonSynced] public int wrongPenalty = 10;

    // ---- balance (Balance.cs)
    readonly float[] hidePhase = { 35f, 40f, 45f, 50f };
    readonly float[] huntPhase = { 180f, 210f, 240f, 270f };
    readonly float[] roundLengthFactor = { 0.75f, 1f, 1.5f };
    public const float BRIEF = 8f, ROUND_END = 12f, ENDGAME_AT = 45f, LOADING_TIMEOUT = 120f;

    int builtRound = -1, genRound = -1;
    [System.NonSerialized] public bool building;   // a map is being generated or built
    VRCPlayerApi[] players = new VRCPlayerApi[90];

    public bool IsHost { get { return Networking.IsOwner(gameObject); } }
    public int Now { get { return Networking.GetServerTimeInMilliseconds(); } }
    public float Left { get { return phaseEndMs == 0 ? 0f : Mathf.Max(0f, (phaseEndMs - Now) / 1000f); } }
    public float HuntLength { get { return Mathf.Round(huntPhase[mapSize] * roundLengthFactor[roundLength]); } }
    public bool Endgame { get { return phase == PH_HUNT && Left <= ENDGAME_AT; } }
    public bool MapReady { get { return builtRound == round && !building; } }

    void Start()
    {
        if (IsHost) { AssignSlots(); RequestSerialization(); }
    }

    // ------------------------------------------------------------------ slots
    public int SlotOf(int playerId)
    {
        for (int i = 0; i < slotPlayer.Length; i++) if (slotPlayer[i] == playerId && playerId != 0) return i;
        return -1;
    }

    // Perf: PHView and PHLocal ask for the local slot several times a frame, each a 32-slot search. The last answer
    // is kept and checked against the table (one read), searched again only when it no longer holds.
    int localSlotIdx = -1, localId;

    public PHSlot LocalSlot()
    {
        if (localId == 0) localId = Networking.LocalPlayer.playerId;
        if (localSlotIdx < 0 || slotPlayer[localSlotIdx] != localId) localSlotIdx = SlotOf(localId);
        return localSlotIdx >= 0 ? slots[localSlotIdx] : null;
    }

    void AssignSlots()
    {
        int n = VRCPlayerApi.GetPlayerCount();
        players = VRCPlayerApi.GetPlayers(players);
        // free the slots of players who left
        for (int i = 0; i < slotPlayer.Length; i++)
        {
            if (slotPlayer[i] == 0) continue;
            bool present = false;
            for (int p = 0; p < n; p++) if (Utilities.IsValid(players[p]) && players[p].playerId == slotPlayer[i]) present = true;
            if (!present) { slotPlayer[i] = 0; slotRole[i] = ROLE_NONE; }
        }
        for (int p = 0; p < n; p++)
        {
            if (!Utilities.IsValid(players[p])) continue;
            int id = players[p].playerId;
            if (SlotOf(id) >= 0) continue;
            for (int i = 0; i < slotPlayer.Length && i < slots.Length; i++)
                if (slotPlayer[i] == 0) { slotPlayer[i] = id; slotRole[i] = ROLE_NONE; slotJoinMs[i] = Now; break; }
        }
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (IsHost) { AssignSlots(); RequestSerialization(); }
        // seated hiders re-seat so the newcomer gets their station events (Station Lab, late joiners)
        if (!player.isLocal && local != null) SendCustomEventDelayedSeconds(nameof(ReseatLocal), 1.5f);
    }

    public void ReseatLocal() { if (local != null) local.Reseat(); }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (IsHost) { AssignSlots(); RequestSerialization(); }
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        // a new host: tidy the slot table and carry on with the round's clock. If the old host left (VRChat handed
        // the game on by itself), open a 30 s claim window; if this was a claim or a handover, lock it again.
        if (!IsHost) return;
        VRCPlayerApi old = VRCPlayerApi.GetPlayerById(lastHostId);
        bool oldLeft = lastHostId != 0 && !Utilities.IsValid(old);
        if (oldLeft)
        {
            hostLocked = false; handoverPending = true; claimWindowEndMs = Now + CLAIM_WINDOW_MS;
        }
        else { hostLocked = true; handoverPending = false; }
        lastHostId = Networking.LocalPlayer.playerId;
        hostActiveMs = Now;
        AssignSlots();
        RequestSerialization();
    }

    // ------------------------------------------------------------------ who hosts
    public void UnlockHost() { if (!IsHost) return; hostLocked = false; unlockedAtMs = Now; RequestSerialization(); }
    public void LockHost() { if (!IsHost) return; hostLocked = true; handoverPending = false; RequestSerialization(); }

    // may someone else take the host now? It is unlocked (by the host, or while a departed host's claim window
    // runs), or the host has been idle for 5 minutes
    public bool CanClaim() { return !hostLocked || (hostActiveMs != 0 && Now - hostActiveMs > HOST_IDLE_MS); }

    // anyone, while it can be claimed: take over the game (OnOwnershipTransferred locks it again)
    public void ClaimHost()
    {
        if (IsHost || !CanClaim()) return;
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
    }

    // a claim is only granted while it can be claimed (the master leaving still hands the game on: VRChat does that)
    public override bool OnOwnershipRequest(VRCPlayerApi requester, VRCPlayerApi newOwner)
    {
        return CanClaim();
    }

    // the host's own clock for the handover: activity, the unlock lapsing, the end of a claim window
    Vector3 hostSeenPos;
    Quaternion hostSeenRot;
    float nextActiveSend;

    void HostHandover()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (lastHostId != me.playerId) { lastHostId = me.playerId; RequestSerialization(); }
        // activity: moving or looking around (a host who is away keeps still)
        Vector3 p = me.GetPosition();
        Quaternion r = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
        if ((p - hostSeenPos).sqrMagnitude > 0.04f || Quaternion.Angle(r, hostSeenRot) > 10f || hostActiveMs == 0)
        {
            hostSeenPos = p; hostSeenRot = r;
            if (Time.time >= nextActiveSend) { nextActiveSend = Time.time + 20f; hostActiveMs = Now; RequestSerialization(); }
        }
        if (handoverPending)
        {
            if (Now < claimWindowEndMs) return;
            // nobody claimed it: the player who has been here longest takes it
            handoverPending = false;
            int best = -1;
            for (int i = 0; i < slotPlayer.Length; i++)
                if (slotPlayer[i] != 0 && Utilities.IsValid(VRCPlayerApi.GetPlayerById(slotPlayer[i])) && (best < 0 || slotJoinMs[i] < slotJoinMs[best])) best = i;
            if (best >= 0 && slotPlayer[best] != me.playerId)
            {
                VRCPlayerApi heir = VRCPlayerApi.GetPlayerById(slotPlayer[best]);
                RequestSerialization();
                Networking.SetOwner(heir, gameObject);
                return;
            }
            hostLocked = true;
            RequestSerialization();
            return;
        }
        if (!hostLocked && Now - unlockedAtMs > UNLOCK_LAPSE_MS) { hostLocked = true; RequestSerialization(); }
    }

    public string HostName()
    {
        VRCPlayerApi h = Networking.GetOwner(gameObject);
        return Utilities.IsValid(h) ? h.displayName : "";
    }

    // ------------------------------------------------------------------ the host's settings
    // the host panel's buttons (PHButton sets panelArg first)
    [HideInInspector] public int panelArg;
    public void PanelSize() { if (panelArg < 0) SetSizeAuto(true); else SetSize(panelArg); }
    public void PanelDensity() { SetDensity(panelArg); }
    public void PanelRoundLength() { SetRoundLength(panelArg); }
    public void PanelTaunt() { SetTauntInterval(panelArg); }
    public void PanelPenalty() { SetWrongPenalty(panelArg); }

    public readonly string[] sizeName = { "S", "M", "L", "XL" };
    public readonly string[] densityName = { "Normal", "Sparse", "Dense", "Very sparse", "Very dense" };
    public readonly string[] roundLengthName = { "Short", "Normal", "Long" };

    public string SettingsText()
    {
        return "Size: " + (sizeAuto ? "Auto (" + sizeName[RoundSize()] + ")" : sizeName[sizeChoice])
            + "\nProps: " + densityName[densityChoice]
            + "\nRound: " + roundLengthName[roundLength]
            + "\nForced taunt every " + tauntInterval + " s"
            + "\nIn the zone: " + Participants() + " (" + Hunters(Mathf.Max(1, Participants())) + " hunt)"
            + (phase != PH_LOBBY ? "\nA round is on." : "");
    }

    public void SetSizeAuto(bool v) { if (!IsHost || phase != PH_LOBBY) return; sizeAuto = v; RequestSerialization(); }
    public void SetSize(int v) { if (!IsHost || phase != PH_LOBBY) return; sizeChoice = Mathf.Clamp(v, 0, 3); sizeAuto = false; RequestSerialization(); }
    public void SetDensity(int v) { if (!IsHost || phase != PH_LOBBY) return; densityChoice = Mathf.Clamp(v, 0, 4); RequestSerialization(); }
    public void SetRoundLength(int v) { if (!IsHost || phase != PH_LOBBY) return; roundLength = Mathf.Clamp(v, 0, 2); RequestSerialization(); }
    public void SetTauntInterval(int v) { if (!IsHost || phase != PH_LOBBY) return; tauntInterval = v; RequestSerialization(); }
    public void SetWrongPenalty(int v) { if (!IsHost || phase != PH_LOBBY) return; wrongPenalty = v; RequestSerialization(); }

    // the host enters a seed code for the next map (the panel's text field)
    public void SubmitSeed(string text)
    {
        if (!IsHost || phase != PH_LOBBY || seedTool == null) return;
        seedNeedsConfirm = false; confirmCode = "";
        int st = seedTool.Decode(text, GenData.GEN_VERSION);
        if (st == PHSeed.TYPO) { seedStatus = "Typo in the code. A code looks like v" + GenData.GEN_VERSION + "-XXXX-XXXX-XXXX."; }
        else if (st == PHSeed.NEWER) { seedStatus = "This code needs a newer world (generator v" + seedTool.genVersion + ")."; }
        else
        {
            string code = seedTool.Encode(seedTool.genVersion, seedTool.size, seedTool.seed, seedTool.flags);
            int fits = RoundSize();
            if (st == PHSeed.OLDER) { seedStatus = "Made with generator v" + seedTool.genVersion + ": the map will differ."; seedNeedsConfirm = true; confirmCode = code; }
            else if (seedTool.size != fits) { seedStatus = "This map is size " + sizeName[seedTool.size] + "; the players here fit " + sizeName[fits] + "."; seedNeedsConfirm = true; confirmCode = code; }
            else { pendingCode = code; seedStatus = "Next map: " + code; }
        }
        RequestSerialization();
    }

    public void SeedPlayAnyway()
    {
        if (!IsHost || !seedNeedsConfirm) return;
        pendingCode = confirmCode; seedNeedsConfirm = false;
        seedStatus = "Next map: " + pendingCode;
        RequestSerialization();
    }

    public void SeedDiscard()
    {
        if (!IsHost) return;
        pendingCode = ""; confirmCode = ""; seedNeedsConfirm = false; seedStatus = "";
        RequestSerialization();
    }

    // Maps evolve (Cor): with the default cap of 10 a map changes after runs 2, 5 and 8, each step bigger; other
    // caps spread the three steps the same way (after round(0.3 k cap) - 1 runs)
    public int EvoStep(int runs)
    {
        int step = 0;
        for (int k = 1; k <= 3; k++) if (runs >= Mathf.Max(1, Mathf.RoundToInt(k * runCap * 0.3f) - 1)) step = k;
        return step;
    }

    void NewMap(int r)
    {
        rotSeed[r] = (uint)Random.Range(1, int.MaxValue);
        rotRuns[r] = 0;
        rotDensity[r] = densityChoice;
    }

    public void SetRunCap(int v) { if (!IsHost || phase != PH_LOBBY) return; runCap = Mathf.Clamp(v, 1, 99); RequestSerialization(); }

    // ---- votes (D2-D4, G49): anyone, between rounds; PHSlot keeps each player's vote
    public void VoteScrap() { PHSlot s = LocalSlot(); if (s == null || phase != PH_LOBBY || lastRot < 0) return; s.scrapVote = s.scrapVote == voteId ? 0 : voteId; s.Send(); }
    public void VoteUndo() { PHSlot s = LocalSlot(); if (s == null || phase != PH_LOBBY || undoRot < 0) return; s.undoVote = s.undoVote == voteId ? 0 : voteId; s.Send(); }

    public int ScrapVotes() { int n = 0; for (int i = 0; i < slots.Length; i++) if (slotPlayer[i] != 0 && slots[i].scrapVote == voteId) n++; return n; }
    public int UndoVotes() { int n = 0; for (int i = 0; i < slots.Length; i++) if (slotPlayer[i] != 0 && slots[i].undoVote == voteId) n++; return n; }
    public int VotesNeeded() { return VRCPlayerApi.GetPlayerCount() / 2 + 1; }
    public bool CanScrap() { return phase == PH_LOBBY && lastRot >= 0; }
    public bool CanUndo() { return phase == PH_LOBBY && undoRot >= 0; }

    // the host applies a vote once more than half of everyone here agrees
    void HostVotes()
    {
        if (phase != PH_LOBBY) return;
        int need = VotesNeeded();
        if (lastRot >= 0 && ScrapVotes() >= need)
        {
            undoRot = lastRot; undoSeed = rotSeed[lastRot]; undoRuns = rotRuns[lastRot]; undoDensity = rotDensity[lastRot];
            NewMap(lastRot);
            lastRot = -1;
            voteId++;
            RequestSerialization();
        }
        else if (undoRot >= 0 && UndoVotes() >= need)
        {
            rotSeed[undoRot] = undoSeed; rotRuns[undoRot] = undoRuns; rotDensity[undoRot] = undoDensity;
            lastRot = undoRot;
            undoRot = -1;
            voteId++;
            RequestSerialization();
        }
    }

    public int Participants()
    {
        int n = 0;
        for (int i = 0; i < slots.Length; i++) if (slotPlayer[i] != 0 && slots[i].inZone) n++;
        return n;
    }

    // Auto size (G44): S up to 8 players, M up to 16, L up to 24, XL up to 32
    public int RoundSize() { int n = Participants(); return sizeAuto ? (n <= 8 ? 0 : (n <= 16 ? 1 : (n <= 24 ? 2 : 3))) : sizeChoice; }

    public static int Hunters(int participants) { int h = (participants + 2) / 4; return h < 1 ? 1 : h; }

    // START (host, from the panel): the players standing in the zone take part; about 1 in 4 hunt
    public void StartRound()
    {
        if (!IsHost || phase != PH_LOBBY) return;
        int n = Participants();
        if (n < minPlayers) { Debug.Log("[PHGame] START needs " + minPlayers + " players in the zone"); return; }
        AssignSlots();
        int[] idx = new int[slots.Length];
        int k = 0;
        for (int i = 0; i < slots.Length; i++) { slotRole[i] = ROLE_NONE; if (slotPlayer[i] != 0 && slots[i].inZone) idx[k++] = i; }
        for (int i = k - 1; i > 0; i--) { int j = Random.Range(0, i + 1); int t = idx[i]; idx[i] = idx[j]; idx[j] = t; }
        int hunters = (k == 1 && soloHides) ? 0 : Hunters(k);
        for (int i = 0; i < k; i++) slotRole[idx[i]] = i < hunters ? ROLE_HUNTER : ROLE_HIDER;
        debugRound = false; debugQuick = false;
        BeginRound();
    }

    // ------------------------------------------------------------------ debug rounds (the lobby's DEBUG plaque)
    // SPAWN AS HUNTER / SPAWN AS HIDER: the host starts a round at once in that role, wherever they stand and
    // however few players there are. Anyone else standing in the zone plays the other side. Such a round does not
    // end when a side runs out (a lone hunter can walk the map), only on the clock or with END ROUND; a lone
    // hunter's wait while the hiders hide is cut to a few seconds; the map's run is not counted.
    [UdonSynced] public bool debugRound;
    bool debugQuick;

    public void DebugHunter() { DebugStart(ROLE_HUNTER); }
    public void DebugHider() { DebugStart(ROLE_HIDER); }

    void DebugStart(int role)
    {
        if (!IsHost || phase != PH_LOBBY) return;
        AssignSlots();
        int me = SlotOf(Networking.LocalPlayer.playerId);
        if (me < 0) return;
        int others = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            slotRole[i] = ROLE_NONE;
            if (slotPlayer[i] == 0) continue;
            if (i == me) slotRole[i] = role;
            else if (slots[i].inZone) { slotRole[i] = role == ROLE_HUNTER ? ROLE_HIDER : ROLE_HUNTER; others++; }
        }
        debugRound = true; debugQuick = role == ROLE_HUNTER && others == 0;
        BeginRound();
    }

    // THEME: the host picks the theme of the maps to come (any, town, space base, village). A map of a chosen
    // theme is a new one-off seed that comes out in that theme (so its code is a normal code), not one of the
    // rotation's; Explore a new map follows the choice too.
    [UdonSynced] public int debugTheme = -1;
    public TMPro.TextMeshProUGUI themeLabel;

    public void DebugTheme()
    {
        if (!IsHost) return;
        debugTheme = debugTheme >= 2 ? -1 : debugTheme + 1;
        ThemeLabel();
        RequestSerialization();
    }

    void ThemeLabel()
    {
        if (themeLabel == null) return;
        themeLabel.text = "THEME: " + (debugTheme == 0 ? "TOWN" : (debugTheme == 1 ? "SPACE BASE" : (debugTheme == 2 ? "VILLAGE" : "ANY")));
    }

    // a random seed whose map has the chosen theme (any seed when none is chosen)
    public uint ThemedSeed()
    {
        uint s = (uint)Random.Range(1, int.MaxValue);
        for (int k = 0; k < 300 && debugTheme >= 0 && gen.ThemeOfSeed(s, RoundSize()) != debugTheme; k++) s = (uint)Random.Range(1, int.MaxValue);
        return s;
    }

    // END ROUND: the host ends a round at once, from anywhere (a debug aid; nobody wins)
    public void DebugEnd()
    {
        if (!IsHost || phase == PH_LOBBY) return;
        BackToLobby();
    }

    // the map and the round's start, the roles being set
    void BeginRound()
    {
        mapSize = RoundSize();
        mapDensity = densityChoice;
        // the next map of this size's rotation, or a shared seed as a one-off (D8)
        lastRot = -1;
        mapEvo = 0;   // a shared seed always plays as generated
        bool shared = false;
        if (pendingCode != "" && seedTool != null)
        {
            int st = seedTool.Decode(pendingCode, GenData.GEN_VERSION);
            if (st == PHSeed.OK || st == PHSeed.OLDER) { seed = seedTool.seed; mapSize = seedTool.size; mapDensity = seedTool.flags & 7; shared = true; }
        }
        if (!shared && debugTheme >= 0)
        {
            seed = ThemedSeed(); mapDensity = densityChoice;   // a one-off of the chosen theme (the debug THEME button)
        }
        else if (!shared)
        {
            int r = mapSize * ROT_PER_SIZE + rotNext[mapSize];
            if (rotSeed[r] == 0) NewMap(r);
            seed = rotSeed[r]; mapDensity = rotDensity[r];
            mapEvo = EvoStep(rotRuns[r]);
            lastRot = r;
            rotNext[mapSize] = (rotNext[mapSize] + 1) % ROT_PER_SIZE;
        }
        pendingCode = ""; seedStatus = ""; seedNeedsConfirm = false;
        if (seedTool != null) seedCode = seedTool.Encode(GenData.GEN_VERSION, mapSize, seed, mapDensity);
        round++;
        hidersWin = false; endReason = "";
        SetPhase(PH_LOADING, LOADING_TIMEOUT);
    }

    public void BackToLobby()
    {
        if (!IsHost) return;
        for (int i = 0; i < slotRole.Length; i++) slotRole[i] = ROLE_NONE;
        SetPhase(PH_LOBBY, 0f);
    }

    void SetPhase(int p, float seconds)
    {
        phase = p;
        phaseEndMs = seconds > 0f ? Now + Mathf.RoundToInt(seconds * 1000f) : 0;
        if (p == PH_HUNT) huntStartMs = Now;
        RequestSerialization();
        OnPhaseChanged();
    }

    public override void OnDeserialization() { OnPhaseChanged(); ThemeLabel(); }

    int seenPhase = -1, seenRound = -1;

    void OnPhaseChanged()
    {
        if (phase == seenPhase && round == seenRound) return;
        seenPhase = phase; seenRound = round;
        // a mid-round joiner builds the round's map too
        if (phase != PH_LOBBY && genRound != round) BeginMap();
        if (local != null) local.OnPhase(phase);
    }

    // ------------------------------------------------------------------ the map: every client builds it
    public bool logSlowSteps = false;  // profiling: name the generator steps that take a frame's time
    public float slowStepMs = 20f;     // ...and from how many milliseconds a step counts as slow
    [System.NonSerialized] public float genMs, genWorstMs;   // the last map's generation: time in steps, and its longest step
    // ...and, while logSlowSteps is on (the profiler): the steps' count, the time per generator stage, the steps by
    // length (under 2, 5, 10, 15, 20, 30, 50 ms, and longer), where the longest one was
    [System.NonSerialized] public int genSteps;
    [System.NonSerialized] public float[] genStageMs = new float[16];
    [System.NonSerialized] public int[] genHist = new int[8];
    [System.NonSerialized] public string genWorstAt = "";
    System.Diagnostics.Stopwatch genWatch;

    void BeginMap()
    {
        genRound = round;
        building = true;
        if (genWatch == null) genWatch = new System.Diagnostics.Stopwatch();
        genWatch.Reset(); genWatch.Start();
        builder.Clear();
        double clearMs = genWatch.Elapsed.TotalMilliseconds;
        gen.Begin(seed, mapSize, mapDensity, mapEvo);
        // (these two are not steps: the profiler's log says what they cost)
        if (logSlowSteps) Debug.Log("[PHProf] map begin: clearing the last map " + clearMs.ToString("0.0") + "ms, the generator's start " + (genWatch.Elapsed.TotalMilliseconds - clearMs).ToString("0.0") + "ms");
        genMs = 0f; genWorstMs = 0f; genSteps = 0; genWorstAt = "";
        for (int i = 0; i < genStageMs.Length; i++) genStageMs[i] = 0f;
        for (int i = 0; i < genHist.Length; i++) genHist[i] = 0;
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
        if (building)
        {
            if (!builder.busy && !builder.built)
            {
                // generation, a few steps per frame within a budget
                if (genWatch == null) genWatch = new System.Diagnostics.Stopwatch();
                genWatch.Reset(); genWatch.Start();
                bool finished = false;
                double s0 = 0.0;
                while (!finished && s0 < 6.0)
                {
                    int st = 0, cu = 0, ph = 0;
                    string sub = "";
                    if (logSlowSteps) { st = gen.CurrentStage(); cu = gen.CurrentCursor(); ph = gen.CurrentPhase(); sub = gen.CurrentSub(); }
                    finished = gen.Step();
                    double s1 = genWatch.Elapsed.TotalMilliseconds;
                    float one = (float)(s1 - s0);
                    s0 = s1;
                    genMs += one;
                    if (one > genWorstMs) { genWorstMs = one; if (logSlowSteps) genWorstAt = "st" + st + "/cu" + cu + "/ph" + ph + "/sub" + sub; }
                    if (logSlowSteps)
                    {
                        // for the profiler (PHProfiler): how many steps, how long, where the time went
                        genSteps++; genStageMs[st & 15] += one;
                        genHist[one < 2f ? 0 : (one < 5f ? 1 : (one < 10f ? 2 : (one < 15f ? 3 : (one < 20f ? 4 : (one < 30f ? 5 : (one < 50f ? 6 : 7))))))]++;
                        if (one > slowStepMs) Debug.Log("[PHGame] slow generator step: stage " + st + " cursor " + cu + " phase " + ph + " sub " + sub + " " + one.ToString("0") + " ms");
                    }
                }
                if (finished)
                {
                    if (gen.failed) { building = false; Debug.LogError("[PHGame] map generation failed: " + gen.failReason); }
                    else
                    {
                        builder.BeginBuild();
                        if (logSlowSteps) Debug.Log("[PHProf] build begin: " + (genWatch.Elapsed.TotalMilliseconds - s0).ToString("0.0") + "ms");
                    }
                }
            }
            else if (builder.built)
            {
                building = false; builtRound = genRound;
                PHSlot me = LocalSlot();
                if (me != null) me.ReportReady(round);
            }
        }
        if (IsHost) { HostTick(); HostHandover(); HostVotes(); }
    }

    // ------------------------------------------------------------------ the host's clock
    void HostTick()
    {
        switch (phase)
        {
            case PH_LOADING:
                if (AllReady() || Left <= 0f) SetPhase(PH_BRIEF, BRIEF);
                break;
            case PH_BRIEF:
                if (Left <= 0f) SetPhase(PH_HIDE, debugQuick ? 3f : hidePhase[mapSize]);
                break;
            case PH_HIDE:
                if (Left <= 0f) SetPhase(PH_HUNT, HuntLength);
                break;
            case PH_HUNT:
                int hiders = 0, hunters = 0;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slotPlayer[i] == 0) continue;
                    if (slotRole[i] == ROLE_HIDER && !slots[i].caught) hiders++;
                    if (slotRole[i] == ROLE_HUNTER && !slots[i].hunterOut) hunters++;
                }
                if (debugRound) { if (Left <= 0f) End(hiders > 0, "Debug round: time's up", true); }
                else if (hiders == 0) End(false, "Every hider was caught", false);
                else if (hunters == 0) End(true, "Every hunter is out", false);
                else if (Left <= 0f) End(true, "Time's up: a hider was still uncaught", true);
                break;
            case PH_END:
                if (Left <= 0f) BackToLobby();
                break;
        }
    }

    bool AllReady()
    {
        for (int i = 0; i < slots.Length; i++)
            if (slotPlayer[i] != 0 && slotRole[i] != ROLE_NONE && slots[i].readyRound != round) return false;
        return true;
    }

    void End(bool hiders, string why, bool clock)
    {
        hidersWin = hiders; endReason = why; timeUp = clock;
        // the run counts; a map at its cap retires and a new one takes its slot (D6)
        if (lastRot >= 0 && !debugRound)
        {
            rotRuns[lastRot]++;
            if (rotRuns[lastRot] >= runCap) NewMap(lastRot);
        }
        voteId++;
        SetPhase(PH_END, ROUND_END);
    }

    // ------------------------------------------------------------------ helpers for everyone
    public int RoleOf(int slot) { return slot >= 0 && slot < slotRole.Length ? slotRole[slot] : ROLE_NONE; }

    // a knock for a new prop: a free one, else the one used longest ago
    public PHKnock FreeKnock()
    {
        PHKnock best = null;
        for (int i = 0; i < knocks.Length; i++)
        {
            PHKnock k = knocks[i];
            if (k.Busy) continue;
            if (best == null || k.lastUsed < best.lastUsed) best = k;
        }
        return best;
    }

    public PHKnock KnockOf(int emit)
    {
        for (int i = 0; i < knocks.Length; i++) if (knocks[i].emit == emit && knocks[i].round == round) return knocks[i];
        return null;
    }

    public bool InLobbyZone(Vector3 p)
    {
        if (lobbyZone == null) return false;
        // the square on the floor counts, whatever the height (a player stands at the box's bottom face)
        Bounds b = lobbyZone.bounds;
        return p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z && p.y >= b.min.y - 1f && p.y <= b.max.y;
    }
}
