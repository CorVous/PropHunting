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
    [UdonSynced] public int[] slotPlayer = new int[32];   // player id per slot, 0 = free
    [UdonSynced] public int[] slotRole = new int[32];     // ROLE_* for this round
    // the host is whoever owns this object (the instance master to begin with). Locked, only the host keeps
    // it; unlocked, anyone may claim it (the host panel's Unlock host / Claim host)
    [UdonSynced] public bool hostLocked = true;
    [UdonSynced] public bool hidersWin;
    [UdonSynced] public string endReason = "";

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
    bool building;
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

    public PHSlot LocalSlot()
    {
        int s = SlotOf(Networking.LocalPlayer.playerId);
        return s >= 0 ? slots[s] : null;
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
                if (slotPlayer[i] == 0) { slotPlayer[i] = id; slotRole[i] = ROLE_NONE; break; }
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
        // a new host: tidy the slot table, lock the host again, and carry on with the round's clock
        if (IsHost) { hostLocked = true; AssignSlots(); RequestSerialization(); }
    }

    // ------------------------------------------------------------------ who hosts
    public void UnlockHost() { if (!IsHost) return; hostLocked = false; RequestSerialization(); }
    public void LockHost() { if (!IsHost) return; hostLocked = true; RequestSerialization(); }

    // anyone, while the host is unlocked: take over the game (OnOwnershipTransferred locks it again)
    public void ClaimHost()
    {
        if (IsHost || hostLocked) return;
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
    }

    // a claim is only granted while unlocked (the master leaving still hands the game on: VRChat does that)
    public override bool OnOwnershipRequest(VRCPlayerApi requester, VRCPlayerApi newOwner)
    {
        return !hostLocked;
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
        mapSize = RoundSize();
        mapDensity = densityChoice;
        seed = (uint)Random.Range(1, int.MaxValue);
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

    public override void OnDeserialization() { OnPhaseChanged(); }

    int seenPhase = -1, seenRound = -1;

    void OnPhaseChanged()
    {
        if (phase == seenPhase && round == seenRound) return;
        seenPhase = phase; seenRound = round;
        if (phase == PH_LOADING && genRound != round) BeginMap();
        if (local != null) local.OnPhase(phase);
    }

    // ------------------------------------------------------------------ the map: every client builds it
    void BeginMap()
    {
        genRound = round;
        building = true;
        builder.Clear();
        gen.Begin(seed, mapSize, mapDensity);
    }

    void Update()
    {
        if (building)
        {
            if (!builder.busy && !builder.built)
            {
                // generation, a few steps per frame within a budget
                float t0 = Time.realtimeSinceStartup;
                bool finished = false;
                while (!finished && Time.realtimeSinceStartup - t0 < 0.006f) finished = gen.Step();
                if (finished)
                {
                    if (gen.failed) { building = false; Debug.LogError("[PHGame] map generation failed: " + gen.failReason); }
                    else builder.BeginBuild();
                }
            }
            else if (builder.built)
            {
                building = false; builtRound = genRound;
                PHSlot me = LocalSlot();
                if (me != null) me.ReportReady(round);
            }
        }
        if (IsHost) HostTick();
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
                if (Left <= 0f) SetPhase(PH_HIDE, hidePhase[mapSize]);
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
                if (hiders == 0) End(false, "Every hider was caught");
                else if (hunters == 0) End(true, "Every hunter is out");
                else if (Left <= 0f) End(true, "Time's up: a hider was still uncaught");
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

    void End(bool hiders, string why)
    {
        hidersWin = hiders; endReason = why;
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
