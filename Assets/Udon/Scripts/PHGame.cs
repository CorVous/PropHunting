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
    public bool leakHeal = true;   // PHSlot.CheckLeak asks a hider drawn in the open to sit again (off only to reproduce the leak in a test)
    public PHText text;                  // the world's text in the local language (PHText.Get / F1 / Prop; TextBaker wires it)
    public PHKnock[] knocks;            // the pool of synced prop knocks
    public PHSeed seedTool;             // seed codes (encode, decode)

    // ---- seeds (04 section 6.1): the code of the map being (or last) played, and a code the host entered for
    // the next map. A code for another size than the players fit, or from an older generator, waits for the
    // host's "play anyway" or "discard".
    [UdonSynced] public string seedCode = "";
    [UdonSynced] public string pendingCode = "";
    [UdonSynced] public string seedStatus = "";
    [UdonSynced] public bool seedNeedsConfirm;
    [UdonSynced] string confirmCode = "";   // (synced: a new host can still "play anyway")

    // ---- the map trail (Cor, 2026-11-19; it replaces the rotation of three maps per size and the run cap): up to
    // TRAIL maps that have been played here, each with its size, density and run count. The host starts a round
    // with NEW MAP (a map nobody has seen, or the code they entered; it joins the trail) or with REPLAY (the picked
    // map of the trail again: the last one played unless the host stepped to another). There is no run cap and no
    // retire button (Cor): a new map on a full trail pushes off the one played longest ago. Between rounds, more
    // than half of everyone here can vote to scrap the last map or to undo the last removal (one level); the host's
    // own UNDO does it at once. PHHistory shows the trail on the lobby wall.
    public const int TRAIL = 12;
    [UdonSynced] public uint[] rotSeed = new uint[12];        // per trail slot; 0 = empty
    [UdonSynced] public int[] rotRuns = new int[12];
    [UdonSynced] public int[] rotDensity = new int[12];
    [UdonSynced] public int[] rotSize = new int[12];
    [UdonSynced] public int[] rotPlayed = new int[12];        // the round it was last played in (the wall's order)
    [UdonSynced] public int replayPick = -1;                  // the slot REPLAY plays (-1: nothing to replay)
    [UdonSynced] public int lastRot = -1;                     // the slot the last round played
    [UdonSynced] public int undoRot = -1;                     // 0 while a removed map can be brought back: what it was
    [UdonSynced] public uint undoSeed;
    [UdonSynced] public int undoRuns, undoDensity, undoSize, undoPlayed;
    public PHHistory history;                                 // the wall of played maps (takes the map's photo)
    // ---- map bonuses (Cor, 2026-11-20; they replace the scrap and undo votes, whose code below is no longer
    // called): coins for moving between maps and for going back to them. A map played once or twice always pays
    // 1.5x. When a round ends the host rolls the next offer: one time in four NEW MAP pays 2x; otherwise up to three
    // of the older maps (played three times or more) pay 1.5x. The panel and the wall show the offers; the round's
    // own bonus is fixed when it starts (PHStore pays it).
    [UdonSynced] public bool offerNew;                        // NEW MAP pays 2x
    [UdonSynced] public int offerMask;                        // bit per trail slot: that map pays 1.5x
    [UdonSynced] public int roundBonus;                       // this round: 0 none, 1 = 1.5x, 2 = 2x
    public const float OFFER_NEW_CHANCE = 0.25f;

    // what replaying the trail's map r pays: 1 (1.5x) or 0
    public int SlotBonus(int r)
    {
        if (r < 0 || r >= TRAIL || rotSeed[r] == 0) return 0;
        return rotRuns[r] == 1 || rotRuns[r] == 2 || ((offerMask >> r) & 1) != 0 ? 1 : 0;
    }

    void RollOffers()
    {
        offerMask = 0;
        offerNew = Random.value < OFFER_NEW_CHANCE;
        if (offerNew) return;
        int[] old = new int[TRAIL];
        int n = 0;
        for (int r = 0; r < TRAIL; r++) if (rotSeed[r] != 0 && rotRuns[r] >= 3) old[n++] = r;
        int want = Mathf.Min(n, Random.Range(1, 4));
        for (int k = 0; k < want; k++)
        {
            int j = Random.Range(k, n);
            int t = old[k]; old[k] = old[j]; old[j] = t;
            offerMask |= 1 << old[k];
        }
    }
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
    [UdonSynced] public int mapVar;             // ...and which replay this is (MapGen.evoVar): each one shuffles anew
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
    [UdonSynced] public int densityChoice = 1;          // Sparse (Cor, playtest 2026-11-20: the default now)
    [UdonSynced] public int roundLength = 1;           // Short, Normal, Long
    [UdonSynced] public int tauntInterval = 40;
    [UdonSynced] public int wrongPenalty = 10;

    // ---- balance (Balance.cs)
    readonly float[] hidePhase = { 35f, 40f, 45f, 50f, 30f };       // S, M, L, XL, XS (size 4: GenData.SIZE_XS)
    readonly float[] huntPhase = { 180f, 210f, 240f, 270f, 150f };
    readonly float[] roundLengthFactor = { 0.75f, 1f, 1.5f };
    public const float BRIEF = 8f, ROUND_END = 30f, ENDGAME_AT = 45f, LOADING_TIMEOUT = 120f;

    int builtRound = -1, genRound = -1;
    [System.NonSerialized] public bool building;   // a map is being generated or built
    VRCPlayerApi[] players = new VRCPlayerApi[90];

    public bool IsHost { get { return Networking.IsOwner(gameObject); } }
    public int Now { get { return Networking.GetServerTimeInMilliseconds(); } }
    public float Left { get { return phaseEndMs == 0 ? 0f : Mathf.Max(0f, (phaseEndMs - Now) / 1000f); } }
    public float HuntLength { get { return Mathf.Round(huntPhase[mapSize] * roundLengthFactor[roundLength]); } }
    public bool Endgame { get { return phase == PH_HUNT && Left <= ENDGAME_AT; } }
    // hiders still in the round and not caught, from the synced slots (any client)
    public int HidersLeft()
    {
        int n = 0;
        for (int i = 0; i < slots.Length && i < slotPlayer.Length; i++)
            if (slotPlayer[i] != 0 && slotRole[i] == ROLE_HIDER && slots[i] != null && !slots[i].caught) n++;
        return n;
    }
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

    void AssignSlots() { AssignSlotsBut(0); }

    // goneId: a player who is leaving (during OnPlayerLeft VRChat still lists them in GetPlayers, so their slot
    // would otherwise stay taken: a hider who left kept the round going, a hunter who left during LOADING held it
    // for the whole timeout). Returns whether the table changed.
    bool AssignSlotsBut(int goneId)
    {
        bool changed = false;
        int n = VRCPlayerApi.GetPlayerCount();
        players = VRCPlayerApi.GetPlayers(players);
        if (n > players.Length) n = players.Length;
        // free the slots of players who left
        for (int i = 0; i < slotPlayer.Length; i++)
        {
            if (slotPlayer[i] == 0) continue;
            bool present = false;
            if (slotPlayer[i] != goneId)
                for (int p = 0; p < n; p++) if (Utilities.IsValid(players[p]) && players[p].playerId == slotPlayer[i]) present = true;
            if (!present) { slotPlayer[i] = 0; slotRole[i] = ROLE_NONE; changed = true; }
        }
        for (int p = 0; p < n; p++)
        {
            if (!Utilities.IsValid(players[p])) continue;
            int id = players[p].playerId;
            if (id == goneId || SlotOf(id) >= 0) continue;
            for (int i = 0; i < slotPlayer.Length && i < slots.Length; i++)
                if (slotPlayer[i] == 0) { slotPlayer[i] = id; slotRole[i] = ROLE_NONE; slotJoinMs[i] = Now; changed = true; break; }
        }
        return changed;
    }

    // the host re-checks the slot table now and then, in case a leave was missed (a host handover around it)
    float nextSweep;
    void HostSweep()
    {
        if (Time.time < nextSweep) return;
        nextSweep = Time.time + 2f;
        // (a player who just left may still be listed for a moment: not given a slot again)
        if (AssignSlotsBut(Time.time - lastLeftAt < 5f ? lastLeftId : 0)) RequestSerialization();
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (IsHost) { AssignSlots(); RequestSerialization(); }
        // (No blanket re-seat of every hider on a join any more: each exit and enter goes to everyone, and a viewer that
        // got the pair out of order drew the hiders in the open from then on. The newcomer's own watchdog asks exactly
        // the hiders it sees in the open to sit again, PHLocal.UpdateWatch.)
    }

    public void ReseatLocal() { if (local != null) local.Reseat(); }

    float lastLeftAt = -100f;
    int lastLeftId;   // (local) the last player seen leaving: the host handover tells a departed host from a claim

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        int id = Utilities.IsValid(player) ? player.playerId : 0;
        lastLeftId = id; lastLeftAt = Time.time;
        if (IsHost) { AssignSlotsBut(id); RequestSerialization(); }
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        // a new host: tidy the slot table and carry on with the round's clock. If the old host left (VRChat handed
        // the game on by itself), open a 30 s claim window; if this was a claim or a handover, lock it again.
        if (!IsHost) return;
        VRCPlayerApi old = VRCPlayerApi.GetPlayerById(lastHostId);
        bool oldLeft = lastHostId != 0 && (!Utilities.IsValid(old) || lastHostId == lastLeftId);
        int oldId = lastHostId;
        if (oldLeft)
        {
            hostLocked = false; handoverPending = true; claimWindowEndMs = Now + CLAIM_WINDOW_MS;
        }
        else { hostLocked = true; handoverPending = false; }
        lastHostId = Networking.LocalPlayer.playerId;
        hostActiveMs = Now;
        AssignSlotsBut(oldLeft ? oldId : 0);
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
            if (Now - claimWindowEndMs < 0) return;   // (a difference: server time wraps)
            // nobody claimed it: the player who has been here longest takes it
            handoverPending = false;
            int best = -1;
            for (int i = 0; i < slotPlayer.Length; i++)
                if (slotPlayer[i] != 0 && Utilities.IsValid(VRCPlayerApi.GetPlayerById(slotPlayer[i])) && (best < 0 || slotJoinMs[i] - slotJoinMs[best] < 0)) best = i;
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

    public readonly string[] sizeName = { "S", "M", "L", "XL", "XS" };   // (XS is 4: the codes and trail of S to XL keep their numbers)
    public readonly string[] densityName = { "Normal", "Sparse", "Dense", "Very sparse", "Very dense", "Minimal" };
    public readonly string[] roundLengthName = { "Short", "Normal", "Long" };
    readonly string[] densityKey = { "dens.normal", "dens.sparse", "dens.dense", "dens.vsparse", "dens.vdense", "dens.minimal" };   // PHText rows
    readonly string[] roundLengthKey = { "len.short", "len.normal", "len.long" };

    public string SettingsText()
    {
        return (sizeAuto ? text.F1("opt.sizeAuto", sizeName[RoundSize()]) : text.F1("opt.size", sizeName[sizeChoice]))
            + "\n" + text.F1("opt.props", text.Get(densityKey[densityChoice]))
            + "\n" + text.F1("opt.round", text.Get(roundLengthKey[roundLength]))
            + "\n" + text.F1("opt.taunt", tauntInterval.ToString())
            + "\n" + text.F2("opt.zone", Participants().ToString(), Hunters(Mathf.Max(1, Participants())).ToString())
            + (phase != PH_LOBBY ? "\n" + text.Get("opt.roundOn") : "");
    }

    public void SetSizeAuto(bool v) { if (!IsHost || phase != PH_LOBBY) return; sizeAuto = v; RequestSerialization(); }
    public void SetSize(int v) { if (!IsHost || phase != PH_LOBBY) return; sizeChoice = Mathf.Clamp(v, 0, 4); sizeAuto = false; RequestSerialization(); }
    public void SetDensity(int v) { if (!IsHost || phase != PH_LOBBY) return; densityChoice = Mathf.Clamp(v, 0, 5); RequestSerialization(); }
    public void SetRoundLength(int v) { if (!IsHost || phase != PH_LOBBY) return; roundLength = Mathf.Clamp(v, 0, 2); RequestSerialization(); }
    public void SetTauntInterval(int v) { if (!IsHost || phase != PH_LOBBY) return; tauntInterval = v; RequestSerialization(); }
    public void SetWrongPenalty(int v) { if (!IsHost || phase != PH_LOBBY) return; wrongPenalty = v; RequestSerialization(); }

    // the host enters a seed code for the next map (the panel's text field)
    public void SubmitSeed(string text)
    {
        if (!IsHost || phase != PH_LOBBY || seedTool == null) return;
        seedNeedsConfirm = false; confirmCode = "";
        int st = seedTool.Decode(text, GenData.GEN_VERSION);
        if (st == PHSeed.TYPO) { seedStatus = "seed.typo|" + GenData.GEN_VERSION; }
        else if (st == PHSeed.NEWER) { seedStatus = "seed.newer|" + seedTool.genVersion; }
        else
        {
            string code = seedTool.Encode(seedTool.genVersion, seedTool.size, seedTool.seed, seedTool.flags);
            int fits = RoundSize();
            if (st == PHSeed.OLDER) { seedStatus = "seed.older|" + seedTool.genVersion; seedNeedsConfirm = true; confirmCode = code; }
            else if (seedTool.size != fits) { seedStatus = "seed.size|" + sizeName[seedTool.size] + "|" + sizeName[fits]; seedNeedsConfirm = true; confirmCode = code; }
            else { pendingCode = code; seedStatus = "seed.next|" + CodeFace(code); }
        }
        RequestSerialization();
    }

    // a map code in the clear face of the code field (Cor: 0 and O, 1 and l alike in the Kenney face)
    string CodeFace(string code) { return "<font=\"LiberationSans SDF\">" + code + "</font>"; }

    public void SeedPlayAnyway()
    {
        if (!IsHost || !seedNeedsConfirm) return;
        pendingCode = confirmCode; seedNeedsConfirm = false;
        seedStatus = "seed.next|" + CodeFace(pendingCode);
        RequestSerialization();
    }

    public void SeedDiscard()
    {
        if (!IsHost) return;
        pendingCode = ""; confirmCode = ""; seedNeedsConfirm = false; seedStatus = "";
        RequestSerialization();
    }

    // Maps evolve (Cor): every replay shuffles a map, more the longer it has lived. The first play is the map as
    // generated; replays 1 to 4 take the small step (things on furniture, stray props), 5 to 7 the medium one (rooms
    // refurnished, outdoors regrown), from 8 on the big one (a building's plan changes): the steps of the old run cap
    // of 10, the small one now from the first replay. The run count is passed along as the variant, so each replay
    // changes other rooms.
    public int EvoStep(int runs) { return runs <= 0 ? 0 : (runs < 5 ? 1 : (runs < 8 ? 2 : 3)); }

    // the trail's maps, the one played last first; returns how many (order needs TRAIL elements)
    public int TrailOrder(int[] order)
    {
        int n = 0;
        for (int r = 0; r < TRAIL; r++)
        {
            if (rotSeed[r] == 0) continue;
            int k = n++;
            while (k > 0 && rotPlayed[order[k - 1]] < rotPlayed[r]) { order[k] = order[k - 1]; k--; }
            order[k] = r;
        }
        return n;
    }

    // a slot for a new map: an empty one, else the one played longest ago
    int FreeSlot()
    {
        int best = 0;
        for (int r = 0; r < TRAIL; r++)
        {
            if (rotSeed[r] == 0) return r;
            if (rotPlayed[r] < rotPlayed[best]) best = r;
        }
        return best;
    }

    int FindMap(uint s, int size, int density)
    {
        for (int r = 0; r < TRAIL; r++) if (rotSeed[r] == s && rotSize[r] == size && rotDensity[r] == density) return r;
        return -1;
    }

    // takes a map off the trail; it can be brought back until the next removal (Restore)
    void Retire(int r)
    {
        if (r < 0 || r >= TRAIL || rotSeed[r] == 0) return;
        undoRot = 0; undoSeed = rotSeed[r]; undoRuns = rotRuns[r]; undoDensity = rotDensity[r]; undoSize = rotSize[r]; undoPlayed = rotPlayed[r];
        Drop(r);
        voteId++;
    }

    void Drop(int r)
    {
        rotSeed[r] = 0; rotRuns[r] = 0;
        offerMask &= ~(1 << r);
        if (lastRot == r) lastRot = -1;
        if (replayPick == r)
        {
            // REPLAY goes on to the map played most recently of those left
            replayPick = -1;
            for (int k = 0; k < TRAIL; k++) if (rotSeed[k] != 0 && (replayPick < 0 || rotPlayed[k] > rotPlayed[replayPick])) replayPick = k;
        }
    }

    void Restore()
    {
        if (undoRot < 0) return;
        int r = FreeSlot();
        rotSeed[r] = undoSeed; rotRuns[r] = undoRuns; rotDensity[r] = undoDensity; rotSize[r] = undoSize; rotPlayed[r] = undoPlayed;
        if (lastRot == r) lastRot = -1;
        replayPick = r;
        undoRot = -1;
        voteId++;
    }

    // RETIRE (host): the picked map leaves the trail. UNDO (host): the last map removed comes back, picked.
    public void HostRetire() { if (!IsHost || phase != PH_LOBBY || replayPick < 0) return; Retire(replayPick); RequestSerialization(); }
    public void HostUndo() { if (!IsHost || phase != PH_LOBBY || undoRot < 0) return; Restore(); RequestSerialization(); }

    // the arrows beside REPLAY (host): step to the trail's next map, older (1) or newer (-1)
    public void PickOlder() { PickStep(1); }
    public void PickNewer() { PickStep(-1); }
    void PickStep(int dir)
    {
        if (!IsHost || phase != PH_LOBBY) return;
        int[] order = new int[TRAIL];
        int n = TrailOrder(order);
        if (n == 0) return;
        int at = 0;
        for (int k = 0; k < n; k++) if (order[k] == replayPick) at = k;
        replayPick = order[(at + dir + n) % n];
        RequestSerialization();
    }

    // ---- votes (D2-D4, G49): anyone, between rounds; PHSlot keeps each player's vote
    public void VoteScrap() { PHSlot s = LocalSlot(); if (s == null || phase != PH_LOBBY || lastRot < 0) return; s.scrapVote = s.scrapVote == voteId ? 0 : voteId; s.Send(); }
    public void VoteUndo() { PHSlot s = LocalSlot(); if (s == null || phase != PH_LOBBY || undoRot < 0) return; s.undoVote = s.undoVote == voteId ? 0 : voteId; s.Send(); }

    public int ScrapVotes() { int n = 0; for (int i = 0; i < slots.Length; i++) if (slots[i].scrapVote == voteId && Seated(i)) n++; return n; }
    public int UndoVotes() { int n = 0; for (int i = 0; i < slots.Length; i++) if (slots[i].undoVote == voteId && Seated(i)) n++; return n; }

    // the slot's player has taken it over (until then a reused slot still shows the last player's vote)
    bool Seated(int i)
    {
        if (slotPlayer[i] == 0) return false;
        VRCPlayerApi o = Networking.GetOwner(slots[i].gameObject);
        return Utilities.IsValid(o) && o.playerId == slotPlayer[i];
    }
    public int VotesNeeded() { return VRCPlayerApi.GetPlayerCount() / 2 + 1; }
    public bool CanScrap() { return phase == PH_LOBBY && lastRot >= 0; }
    public bool CanUndo() { return phase == PH_LOBBY && undoRot >= 0; }
    public bool CanReplay() { return phase == PH_LOBBY && replayPick >= 0 && rotSeed[replayPick] != 0; }

    // the host applies a vote once more than half of everyone here agrees
    void HostVotes()
    {
        if (phase != PH_LOBBY) return;
        int need = VotesNeeded();
        if (lastRot >= 0 && ScrapVotes() >= need) { Retire(lastRot); RequestSerialization(); }
        else if (undoRot >= 0 && UndoVotes() >= need) { Restore(); RequestSerialization(); }
    }

    public int Participants()
    {
        int n = 0;
        for (int i = 0; i < slots.Length; i++) if (slotPlayer[i] != 0 && slots[i].inZone) n++;
        return n;
    }

    // Auto size (G44; Cor, playtest 2026-11-20: 9 players on Auto got an M map, too big): XS up to 5 players, S up to 10,
    // M up to 18, L up to 26, XL beyond. About 10 to 15 of the site's 4 m cells per hider at the top of each band (a
    // quarter of the players hunt: XS ~39 cells for 4 hiders, S ~92 for 7, M ~143 for 13, L ~203 for 19, XL ~257 for
    // 24); the old bands gave 9 players an M map, 20 cells each. PHPanel shows the same pick (AutoSize).
    public int AutoSize(int n) { return n <= 5 ? 4 : (n <= 10 ? 0 : (n <= 18 ? 1 : (n <= 26 ? 2 : 3))); }
    public int RoundSize() { return sizeAuto ? AutoSize(Participants()) : sizeChoice; }

    public static int Hunters(int participants) { int h = (participants + 2) / 4; return h < 1 ? 1 : h; }

    // NEW MAP and REPLAY (host, from the panel): the players standing in the zone take part; about 1 in 4 hunt.
    // StartRound is NEW MAP (the test pilot and old buttons call it by that name).
    bool wantReplay;
    public void StartRound() { wantReplay = false; StartPlayers(); }
    public void StartReplay() { if (!CanReplay()) return; wantReplay = true; StartPlayers(); }

    void StartPlayers()
    {
        if (!IsHost || phase != PH_LOBBY) return;
        int n = Participants();
        if (n < minPlayers) { Debug.Log("[PHGame] START needs " + minPlayers + " players in the zone"); return; }
        AssignSlots();
        int[] idx = new int[slots.Length];
        int k = 0;
        for (int i = 0; i < slots.Length; i++) if (slotPlayer[i] != 0 && slots[i].inZone) idx[k++] = i;
        // (counted again after the slot table's tidy: someone may have just left; never a round with nobody in it)
        if (k < Mathf.Max(1, minPlayers)) { RequestSerialization(); return; }
        for (int i = 0; i < slots.Length; i++) slotRole[i] = ROLE_NONE;
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
    [UdonSynced] bool debugQuick;   // (synced: a host who takes over in BRIEF keeps the short hide)

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
        wantReplay = replayPick >= 0 && rotSeed[replayPick] != 0;   // on the picked map if there is one (its run is not counted)
        BeginRound();
    }

    // THEME: the host picks the theme of the maps to come (any, town, space base, village). A map of a chosen
    // theme is a new seed that comes out in that theme (so its code is a normal code); Explore a new map follows
    // the choice too.
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
        // REPLAY: the trail's picked map. NEW MAP: the code the host entered (D8), else a fresh seed (of the debug
        // THEME button's theme if one is chosen), into a slot of the trail; a code already on the trail is that map
        // again, with its runs.
        int r = -1;
        bool fresh = false;   // a map nobody here has played (NEW MAP's 2x counts only for one)
        if (wantReplay && replayPick >= 0 && replayPick < TRAIL && rotSeed[replayPick] != 0) r = replayPick;
        else
        {
            uint s = 0;
            if (pendingCode != "" && seedTool != null)
            {
                int st = seedTool.Decode(pendingCode, GenData.GEN_VERSION);
                if (st == PHSeed.OK || st == PHSeed.OLDER) { s = seedTool.seed; mapSize = seedTool.size; mapDensity = seedTool.flags & 7; if (mapDensity > 5) mapDensity = 0; }
            }
            if (s == 0) s = debugTheme >= 0 ? ThemedSeed() : (uint)Random.Range(1, int.MaxValue);
            r = FindMap(s, mapSize, mapDensity);
            if (r < 0)
            {
                r = FreeSlot();
                offerMask &= ~(1 << r);   // (the slot's old map, if it was pushed off, takes its offer with it)
                rotSeed[r] = s; rotRuns[r] = 0; rotDensity[r] = mapDensity; rotSize[r] = mapSize;
                fresh = true;
            }
            pendingCode = ""; seedStatus = ""; seedNeedsConfirm = false;
        }
        wantReplay = false;
        // the round's bonus, and the offers are spent (a debug round pays nothing and leaves them)
        roundBonus = debugRound ? 0 : (fresh ? (offerNew ? 2 : 0) : SlotBonus(r));
        if (!debugRound) { offerNew = false; offerMask = 0; }
        seed = rotSeed[r]; mapSize = rotSize[r]; mapDensity = rotDensity[r];
        mapEvo = EvoStep(rotRuns[r]); mapVar = rotRuns[r];
        lastRot = r; replayPick = r; rotPlayed[r] = round + 1;
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
        FailNotice();
    }

    public override void OnDeserialization() { OnPhaseChanged(); ThemeLabel(); FailNotice(); }

    // "the map failed to build" over the options board (Cor): in the lobby after the round whose map failed, for
    // FAIL_NOTICE_S at most (from the server time it failed, so a late joiner never sees a stale one), until the next
    // round starts (round goes up then), or until this player closes it (a click on it: local only, Cor)
    public const float FAIL_NOTICE_S = 20f;
    [UdonSynced] public int failedRound = -1;
    [UdonSynced] public int failedAtMs;
    public GameObject failNotice;
    public PHStore store;        // the weapon wall's coins and trophies (LobbyBuilder.BuildStore): taunts count there
    public PHDiag diag;          // the game log on the debug sign's back, for testers to copy (LobbyBuilder.BuildGameLog)

    // a diagnostic line: VRChat's log ("[prophunt] ...") and the game log in the lobby
    public void Log(string s) { Debug.Log("[prophunt] " + s); if (diag != null) diag.Add(s); }
    public void Warn(string s) { Debug.LogWarning("[prophunt] " + s); if (diag != null) diag.Add("! " + s); }
    public Material revealMat;   // the END reveal's highlight of the hiders' props (PHSlot.MakeRevealCopy; WorldSceneBuilder)
    int failDismissed = -1;
    void FailNotice()
    {
        if (failNotice == null) return;
        bool on = phase == PH_LOBBY && failedRound == round && round > 0 && failDismissed != round
            && Now - failedAtMs < Mathf.RoundToInt(FAIL_NOTICE_S * 1000f);
        if (failNotice.activeSelf != on) failNotice.SetActive(on);
    }

    // the notice's own button (LobbyBuilder.BuildFailNotice): closed for this player only
    public void DismissFail() { failDismissed = round; FailNotice(); }

    int seenPhase = -1, seenRound = -1;

    void OnPhaseChanged()
    {
        if (phase == seenPhase && round == seenRound) return;
        // A new round arriving while this client still sees the last one (the host went back to the lobby and on to
        // the next round within one sync): the lobby's tidy-up runs first, or the player stayed seated, frozen, at
        // the prop's eye height, with the roofs off, all through the next round.
        bool missedLobby = round != seenRound && seenRound >= 0 && seenPhase > PH_LOBBY && phase != PH_LOBBY;
        seenPhase = phase; seenRound = round;
        if (missedLobby && local != null) local.OnPhase(PH_LOBBY);
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

    public float genBudgetMs = 6f;      // the generator's time a frame (ms; MapBuilderUdon.budgetMs is the builder's)
    bool beginPending;                  // the generator is done: the builder starts next frame (UpdateBody)
    double[] genStepEst;                // per generator stage: what a step has been costing lately (ms)

    void BeginMap()
    {
        genRound = round;
        building = true;
        beginPending = false;
        if (genWatch == null) genWatch = new System.Diagnostics.Stopwatch();
        genWatch.Reset(); genWatch.Start();
        builder.Clear();
        double clearMs = genWatch.Elapsed.TotalMilliseconds;
        gen.Begin(seed, mapSize, mapDensity, mapEvo, mapVar);
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

    // Perf log (off by default; tick it on PHGame in the inspector, or set it from editor code): "[prophunt] perf"
    // lines in the log. Generator steps of 4 ms or more and the builder's start (here), the builder's slow steps and
    // a per-stage summary (MapBuilderUdon.perfLog, set from this), and every 5 s a frame sample: the mean and worst
    // frame time, and this script's own Update cost, so a before/after can be read from the log alone.
    public bool perfLog = false;
    float perfWindowEnd, perfFrameSum, perfFrameMax, perfSelfMs;
    int perfFrames;
    System.Diagnostics.Stopwatch perfWatch;

    void PerfSample(double selfMs)
    {
        float dt = Time.unscaledDeltaTime * 1000f;
        perfFrames++; perfFrameSum += dt; perfSelfMs += (float)selfMs;
        if (dt > perfFrameMax) perfFrameMax = dt;
        if (Time.time < perfWindowEnd) return;
        if (perfFrames > 0 && perfWindowEnd > 0f)
            Debug.Log("[prophunt] perf frames frame=" + Time.frameCount + " n=" + perfFrames + " meanMs=" + (perfFrameSum / perfFrames).ToString("0.00")
                + " worstMs=" + perfFrameMax.ToString("0.0") + " gameUpdateMs=" + (perfSelfMs / perfFrames).ToString("0.000") + " phase=" + phase + " building=" + building);
        perfWindowEnd = Time.time + 5f;
        perfFrames = 0; perfFrameSum = 0f; perfFrameMax = 0f; perfSelfMs = 0f;
    }

    void UpdateBody()
    {
        if (failNotice != null && failNotice.activeSelf) FailNotice();   // (it runs out after FAIL_NOTICE_S)
        if (perfLog)
        {
            if (perfWatch == null) perfWatch = new System.Diagnostics.Stopwatch();
            perfWatch.Reset(); perfWatch.Start();
            builder.perfLog = true;
            UpdateBodyInner();
            PerfSample(perfWatch.Elapsed.TotalMilliseconds);
            return;
        }
        if (builder.perfLog) builder.perfLog = false;
        UpdateBodyInner();
    }

    void UpdateBodyInner()
    {
        if (building)
        {
            if (!builder.busy && !builder.built && beginPending)
            {
                // Perf: the builder starts in a frame of its own (BeginBuild alone is about 23 ms; it ran in the
                // generator's last frame, after up to 6 ms of steps and the last step, 30-45 ms in all)
                beginPending = false;
                if (genWatch == null) genWatch = new System.Diagnostics.Stopwatch();
                genWatch.Reset(); genWatch.Start();
                builder.BeginBuild();
                if (logSlowSteps) Debug.Log("[PHProf] build begin: " + genWatch.Elapsed.TotalMilliseconds.ToString("0.0") + "ms");
                if (perfLog) Debug.Log("[prophunt] perf build begin frame=" + Time.frameCount + " ms=" + genWatch.Elapsed.TotalMilliseconds.ToString("0.0"));
            }
            else if (!builder.busy && !builder.built)
            {
                // generation, a few steps per frame within a budget
                if (genWatch == null) genWatch = new System.Diagnostics.Stopwatch();
                genWatch.Reset(); genWatch.Start();
                bool finished = false;
                double s0 = 0.0;
                // Perf: a step is begun only if what that stage's steps have been costing still fits the budget (it
                // was checked only before a step, so a frame ran 6 ms and then one whole step more); the first step
                // of a frame always runs
                if (genStepEst == null) genStepEst = new double[16];
                bool firstStep = true;
                while (!finished)
                {
                    int st = gen.CurrentStage(), cu = 0, ph = 0;
                    if (!firstStep && s0 + genStepEst[st & 15] >= genBudgetMs) break;
                    firstStep = false;
                    string sub = "";
                    if (logSlowSteps) { cu = gen.CurrentCursor(); ph = gen.CurrentPhase(); sub = gen.CurrentSub(); }
                    finished = gen.Step();
                    double s1 = genWatch.Elapsed.TotalMilliseconds;
                    float one = (float)(s1 - s0);
                    s0 = s1;
                    genStepEst[st & 15] = genStepEst[st & 15] * 0.75 + one * 0.25;
                    if (perfLog && one >= 4f) Debug.Log("[prophunt] perf gen step frame=" + Time.frameCount + " stage=" + st + " ms=" + one.ToString("0.0"));
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
                    if (gen.failed)
                    {
                        building = false; Debug.LogError("[PHGame] map generation failed: " + gen.failReason);
                        // the same seed fails on every client: back to the lobby at once (not after the 2 min wait,
                        // then a round on no map), and the map leaves the trail so it does not come up again
                        if (IsHost && phase == PH_LOADING && genRound == round)
                        {
                            if (lastRot >= 0) Drop(lastRot);
                            failedRound = round;   // (the lobby says so, over the options board, for everyone)
                            failedAtMs = Now;
                            BackToLobby();
                        }
                    }
                    else
                    {
                        beginPending = true;   // BeginBuild next frame (above)
                    }
                }
            }
            else if (builder.built)
            {
                building = false; builtRound = genRound;
                if (history != null && phase != PH_LOBBY && genRound == round) history.Snap(lastRot);   // the wall's photo of it
                PHSlot me = LocalSlot();
                if (me != null) me.ReportReady(round);
            }
        }
        // readiness again while loading, in case the report was lost (the slot not yet ours when the map was done)
        if (phase == PH_LOADING && builtRound == round && !building && Time.time >= nextReady)
        {
            nextReady = Time.time + 1f;
            PHSlot me = LocalSlot();
            if (me != null && me.readyRound != round && RoleOf(SlotOf(Networking.LocalPlayer.playerId)) != ROLE_NONE) me.ReportReady(round);
        }
        if (IsHost) { HostSweep(); HostTick(); HostHandover(); }   // (HostVotes: the scrap and undo votes are gone, Cor)
    }

    float nextReady;

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
                int hiders = 0, hunters = 0, hidersIn = 0, huntersIn = 0;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slotPlayer[i] == 0) continue;
                    if (slotRole[i] == ROLE_HIDER) hidersIn++;
                    if (slotRole[i] == ROLE_HIDER && !slots[i].caught) hiders++;
                    if (slotRole[i] == ROLE_HUNTER) huntersIn++;
                    if (slotRole[i] == ROLE_HUNTER && !slots[i].hunterOut) hunters++;
                }
                if (debugRound) { if (Left <= 0f) End(hiders > 0, "Debug round: time's up", true); }
                else if (hiders == 0) End(false, hidersIn == 0 ? "Every hider left" : "Every hider was caught", false);
                else if (hunters == 0) End(true, huntersIn == 0 ? "Every hunter left" : "Every hunter is out", false);   // (Cor: hiders win when the hunters leave)
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
        // the run counts (there is no cap), and the next round's bonus offer is rolled
        if (lastRot >= 0 && !debugRound) rotRuns[lastRot]++;
        if (!debugRound) RollOffers();
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
