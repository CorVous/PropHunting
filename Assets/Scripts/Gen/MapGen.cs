// The map generator core (04-map-generation-v3, simplest faithful form for the browser prototype).
//
//   outer grid of 4 m cells: plots along a lane, a building from the plan library on each, a wood  ->  edges
//   (walls, doors, windows, fences, gates)  ->  layout tests
//   ->  inner grid of 0.5 m cells with placement tags  ->  every room composed by the plan of its type
//   ->  slots (chairs at tables, settings on top, items on shelves)  ->  wrong-category spawns (leaks)
//   ->  validation (reachability, repair, seed re-roll)  ->  emit list
//
// A place is composed, not filled (owner decision G28): the plan of a room type decides its activity areas from
// the room's floor area, places the anchors by rule (which wall, which corner, facing what, clear of doors,
// windows and walkways), then what belongs with each anchor relative to it, then a few small named groups, then a
// small capped remainder, and stops on a count budget (G24). Nothing is placed without a host; facing comes from
// a relation. Every prop is an obstacle for the reachability check (G26). Any prop can be a disguise (G25).
//
// Deterministic: integer maths, flat arrays, its own PRNG with substreams; no UnityEngine, no physics, no time.
// The same (seed, size, GEN_VERSION) gives the same emit list on every machine. Step() does one small unit
// of work per call so the caller can spread generation over frames; results do not depend on the slicing.
//
// This file: state and stepping. MapGenSite.cs: the outer grid (plots, buildings from the plan library, the wood),
// its walls and doors, and the layout tests. MapGenRooms.cs: rooms and inner tags.
// MapGenPlace.cs: low-level placement. MapGenCompose.cs: the composition primitives (wall runs, relations,
// corners, open spots). MapGenPlans.cs: the plans of indoor rooms. MapGenOutdoor.cs: lawn and forest.
// MapGenGroups.cs: prop groups. MapGenSlots.cs: slots and wrong-category spawns. MapGenFinish.cs: validation.
public partial class MapGen
{
    // ---- stages (also the PRNG substream ids) ----
    const int ST_LAYOUT = 1, ST_EDGES = 2, ST_ROOM = 3, ST_SLOT = 4, ST_SCENERY = 5, ST_LEAK = 6, ST_SPAWN = 8;

    // ---- emit flags ----
    public const int EF_LEAK = 1, EF_COMEDIC = 2, EF_LOOSE = 4, EF_REMOVED = 8, EF_GROUP = 16, EF_INSIDE = 32, EF_ON_TOP = 64, EF_ADJACENT = 128, EF_UNDER = 256;
    public const int EF_MANDATORY = 512;

    // ---- inputs ----
    public uint seed;
    public int season;   // revision 9: 0 summer, 1 autumn, 2 deep summer green: the broadleaf trees of the whole map
    public int holiday;  // revision 12: 0 none, 1 winter, 2 Halloween: the map's seasonal decorations (one map in five)
    public int mapTheme; // revision 12: GenData.THEME_*: a space base or a medieval village now and then
    public static int ForceTheme = -1;   // the console diagnostics only: every map of this theme (-1: drawn)
    public int size;
    public int density;          // index into GenData.DensityPercent (0 = Normal); scales the prop ranges, not the layout
    public int attempt;          // seed re-rolls after a failed validation (deterministic)

    // ---- outer grid ----
    public int W, H;
    public int[] cellInst, cellZone, cellRoom;
    public int nInst;
    public int[] instZone = new int[160];   // per instance (a building, a plot's outdoor cells, the lane, the wood, the release): its zone
    public int releaseX, releaseZ, releaseDir;   // release cell and the direction of its exit
    public int[] edgeH, edgeV, edgeHOff, edgeVOff;

    // ---- rooms ----
    public int nRooms;
    public int[] roomZone = new int[400], roomType = new int[400], roomInst = new int[400], roomOuterCells = new int[400];
    public int[] roomX0 = new int[400], roomZ0 = new int[400], roomX1 = new int[400], roomZ1 = new int[400];
    public int[] roomCellStart = new int[400], roomCellCount = new int[400];
    public int[] roomCells;      // inner cell indices grouped by room
    public int[] roomDoorStart = new int[400], roomDoorCount = new int[400];
    public int[] roomFloorMin = new int[400];    // outdoor areas (G29): the fewest floor props the area may end with
    public int[] roomDoors = new int[3200];      // inner cell just inside each door, 8 per room
    public int[] roomDoorDir = new int[3200];    // the side of that cell the doorway is on (0 N, 1 E, 2 S, 3 W)
    public bool[] roomDoorExt = new bool[3200];  // the doorway leads outdoors (or, for an outdoor room, into a building)
    public int[] roomLeaks = new int[400];
    // prop counts per room (G24): what stands on the floor, what sits on, in or under furniture, trees; and the
    // room's rolled floor target, its floor maximum and its on-furniture cap at the map's density
    public int[] roomFloor = new int[400], roomTop = new int[400], roomTrees = new int[400];
    public int[] roomFloorTarget = new int[400], roomFloorMax = new int[400], roomTopCap = new int[400], roomTopFactor = new int[400];
    public int[] roomCoverMax = new int[400];   // floor cells of the room that props and trees may block

    // ---- inner grid ----
    public int IW, IH;
    public int[] tags;
    public int[] innerRoom;
    public int[] occ;            // emit index + 1 of the object standing on the cell, 0 = free
    public int[] solid;          // how many props' real outlines reach into the cell: every prop is an obstacle (G26)
    public int[] flatOcc;        // rugs
    public int[] reserve;        // kept clear (front clearance, door swing): emit index + 1 of the object it belongs to, 0 = free
    public byte[] walk;          // after Finish(): 0 no floor, 1 walkable, 2 blocked by a solid prop
    public byte[] reach;         // after validation: 1 = reachable from the hunter release

    // ---- emit list: one entry per generated object; the index is the object's identity ----
    public const int MAX_EMIT = 8000;
    public int nEmit;
    public int[] emitDef = new int[MAX_EMIT], emitX = new int[MAX_EMIT], emitY = new int[MAX_EMIT], emitZ = new int[MAX_EMIT];
    public int[] emitYaw = new int[MAX_EMIT], emitParent = new int[MAX_EMIT], emitRoom = new int[MAX_EMIT], emitFlags = new int[MAX_EMIT];
    public int[] emitRX = new int[MAX_EMIT], emitRZ = new int[MAX_EMIT], emitRW = new int[MAX_EMIT], emitRD = new int[MAX_EMIT];
    public int[] emitRot = new int[MAX_EMIT], emitDepth = new int[MAX_EMIT], emitSlot = new int[MAX_EMIT];
    public int[] emitGroup = new int[MAX_EMIT];   // prop group: the emit index of the group's first member, -1 = not in a group
    public int[] emitGrp = new int[MAX_EMIT];     // which prop group (GenData.grpId index), -1 = none
    public int[] emitSet = new int[MAX_EMIT];     // composition set: the emit index of the anchor whose cells this object may share, -1 = none
    public int[] emitHost = new int[MAX_EMIT];    // what the object was placed against: an emit index, or H_WALL, H_FREE, H_NATURE, H_GROUND

    // free slots and hiding sockets left by the slot pass
    public int nFree;
    public int[] freeHost = new int[6000], freeSlot = new int[6000];
    public int nSockets;
    public int[] socketHost = new int[2000], socketSlot = new int[2000];

    // ---- results ----
    public bool[] menu;          // per def: a possible disguise on this map (every disguisable type the map holds)
    public int[] defCopies;
    public int menuCount;
    public int nSpawns;
    public int[] spawnCell = new int[64];
    public uint mapHash;
    public bool done, failed;
    public string failReason = "";

    // ---- statistics ----
    public int statProps, statScenery, statDecor, statSlotItems, statLeakPlausible, statLeakComedic, statLoose;
    public int statRepairs, statGroundCells, statSteps, statMandatoryFail, statGroups, statFloor, statTop, statWalkRejects;
    public int[] statZoneCells = new int[16], statZoneRooms = new int[16], statZoneFloor = new int[16], statZoneTop = new int[16];
    // per room type (index = GenData.R_*): rooms, floor cells, floor props, props on furniture
    public int[] statTypeRooms = new int[128], statTypeCells = new int[128], statTypeFloor = new int[128], statTypeTop = new int[128];   // revision 12: 128 room types (was 64)

    // ---- stepping ----
    int stage, cursor;
    public int progressPermille;

    static int[] sinTab;         // sin(deg) * 4096

    public static readonly int[] DX = { 0, 1, 0, -1 };   // N, E, S, W
    public static readonly int[] DZ = { 1, 0, -1, 0 };

    // scratch
    int[] cand = new int[4096];
    int[] queue;
    int[] dist;
    int[] entryCount = new int[64];
    int[] groupCount = new int[16];

    public MapGen()
    {
        GenData.Build();
        if (sinTab == null)
        {
            sinTab = new int[360];
            for (int i = 0; i < 360; i++) sinTab[i] = (int)System.Math.Round(System.Math.Sin(i * System.Math.PI / 180.0) * 4096.0);
        }
    }

    public void Begin(uint seedValue, int sizeBucket) { Begin(seedValue, sizeBucket, 0); }

    public void Begin(uint seedValue, int sizeBucket, int densityIndex) { Begin(seedValue, sizeBucket, densityIndex, 0); }

    public void Begin(uint seedValue, int sizeBucket, int densityIndex, int evoStep) { Begin(seedValue, sizeBucket, densityIndex, evoStep, 0); }

    // evoVariant: which replay this is (the map's run count). Every replay of an evolved map draws its changes anew,
    // in other rooms; variant 0 is the step as it always was.
    public void Begin(uint seedValue, int sizeBucket, int densityIndex, int evoStep, int evoVariant)
    {
        evo = evoStep < 0 ? 0 : (evoStep > 3 ? 3 : evoStep);
        evoVar = evo > 0 && evoVariant > 0 ? (evoVariant & 0xFFFF) : 0;
        seed = seedValue; size = sizeBucket;
        density = densityIndex < 0 || densityIndex >= GenData.DensityPercent.Length ? 0 : densityIndex;
        attempt = 0; done = false; failed = false; failReason = "";
        stage = 0; cursor = 0; progressPermille = 0; statSteps = 0; popPhase = 0; ftmK = -1; tdK = -1; stRun = false;
        Alloc(GenData.SizeGrid[size], GenData.SizeGrid[size]);
        menu = new bool[GenData.nDefs]; defCopies = new int[GenData.nDefs];
        int roll = Rng(ST_SCENERY, 9, 9).Range(1000);
        season = roll < 550 ? 0 : (roll < 800 ? 1 : 2);
        int hroll = Rng(ST_SCENERY, 9, 10).Range(1000);
        holiday = hroll < 110 ? 1 : (hroll < 200 ? 2 : 0);
        if (holiday == 2) season = 1;   // Halloween comes in the autumn
        int troll = Rng(ST_SCENERY, 9, 11).Range(1000);
        mapTheme = troll < GenData.THEME_SPACE_PERMILLE ? GenData.THEME_SPACE : (troll < GenData.THEME_SPACE_PERMILLE + GenData.THEME_MEDIEVAL_PERMILLE ? GenData.THEME_MEDIEVAL : GenData.THEME_NONE);
        if (ForceTheme >= 0) mapTheme = ForceTheme;
        if (mapTheme != GenData.THEME_NONE) { holiday = 0; if (mapTheme == GenData.THEME_SPACE) season = 0; }
    }

    // the theme a seed's map comes out in (as Begin draws it): for picking a seed of a wanted theme (PHGame's debug
    // THEME button), so the map's code still says everything
    public int ThemeOfSeed(uint seedValue, int sizeBucket)
    {
        uint keep = seed;
        int keepSize = size, keepAttempt = attempt, keepEvo = evo, keepVar = evoVar;
        seed = seedValue; size = sizeBucket; attempt = 0; evo = 0; evoVar = 0;
        int troll = Rng(ST_SCENERY, 9, 11).Range(1000);
        seed = keep; size = keepSize; attempt = keepAttempt; evo = keepEvo; evoVar = keepVar;
        return troll < GenData.THEME_SPACE_PERMILLE ? GenData.THEME_SPACE : (troll < GenData.THEME_SPACE_PERMILLE + GenData.THEME_MEDIEVAL_PERMILLE ? GenData.THEME_MEDIEVAL : GenData.THEME_NONE);
    }

    // the grids for a map of w x h outer cells (the site decides its box: LayoutSite)
    void Alloc(int w, int h)
    {
        if (cellZone != null && W == w && H == h) return;
        W = w; H = h;
        IW = W * 8; IH = H * 8;
        cellInst = new int[W * H]; cellZone = new int[W * H]; cellRoom = new int[W * H]; cellPlot = new int[W * H];
        edgeH = new int[W * (H + 1)]; edgeHOff = new int[W * (H + 1)];
        edgeV = new int[(W + 1) * H]; edgeVOff = new int[(W + 1) * H];
        tags = new int[IW * IH]; innerRoom = new int[IW * IH]; occ = new int[IW * IH]; flatOcc = new int[IW * IH];
        solid = new int[IW * IH];
        reserve = new int[IW * IH]; walk = new byte[IW * IH]; reach = new byte[IW * IH];
        clr = new int[IW * IH]; canopy = new int[IW * IH];
        roomCells = new int[IW * IH];
        queue = new int[IW * IH]; dist = new int[IW * IH];
        wideMark = new int[IW * IH]; wideStamp = 0;
        link = new int[IW * IH];
    }

    public void RunAll(uint seedValue, int sizeBucket) { RunAll(seedValue, sizeBucket, 0); }

    public void RunAll(uint seedValue, int sizeBucket, int densityIndex)
    {
        Begin(seedValue, sizeBucket, densityIndex);
        while (!Step()) { }
    }

    // One unit of work. Returns true when generation has finished (done or failed).
    public bool Step()
    {
        if (done || failed) return true;
        statSteps++;
        switch (stage)
        {
            case 0:
                if (!stRun)
                for (int i = 0; i < statTypeMandFail.Length; i++) statTypeMandFail[i] = 0;
                if (!stRun) { statRepairs = 0; statMandatoryFail = 0; statGroups = 0; statWalkRejects = 0; statLeakComedic = 0; statLeakPlausible = 0; statLoose = 0; }
                if (!stRun) StBegin();
                PhPush(); LayoutSite_Body(); stFin = PhAdvance(); PhPop();
                if (!stFin) break;
                stRun = false;
                if (!hq_LayoutSite_ret) { Retry("layout"); break; }
                stage = 9; progressPermille = 60;
                break;
            case 9:
                PlaceSite(ref layRng);   // the site onto the grid, and its levels: a step of its own (Cor: build freezes)
                stage = 1; progressPermille = 80;
                break;
            // stage 1 in three steps, and Finish in three (Cor: build freezes): the same work in the same order
            case 1:
                BuildRooms();
                BuildEdgesSite();
                stage = 11; progressPermille = 100;
                break;
            case 11:
                if (!PlaceStairs()) { Retry("stairs"); break; }
                stage = 13;
                break;
            case 13:
                if (!stRun) StBegin();
                PhPush(); ValidateSite_Body(); stFin = PhAdvance(); PhPop();
                if (!stFin) break;
                stRun = false;
                if (!hq_ValidateSite_ret) { Retry("layout tests"); break; }
                stage = 12; progressPermille = 120; btPhase = 0; btCursor = 0;
                break;
            case 12:
                if (!BuildTagsStep()) break;
                ResetExits();
                stage = 2; cursor = 0; progressPermille = 150;
                break;
            case 2:
                if (cursor < nRooms)
                {
                    if (PopulateRoomStep(cursor)) { cursor++; popPhase = 0; }
                    progressPermille = 150 + 600 * cursor / nRooms;
                }
                else stage = 3;
                break;
            case 3:
                if (cursor >= 0) { lkPhase = 0; cursor = -1; }   // a few attempts a step (Cor: build freezes)
                if (!PlaceLeaksStep()) break;
                stage = 5; progressPermille = 850; vPhase = 0; vRepair = 0; vFailed = false;
                break;
            case 5:
                if (!ValidateStep()) break;
                if (vFailed) { Retry("unreachable rooms"); break; }
                stage = 6; progressPermille = 940;
                break;
            case 6:
                if (!stRun) StBegin();
                PhPush(); Finish_Body(); stFin = PhAdvance(); PhPop();
                if (!stFin) break;
                stRun = false;
                stage = 7; progressPermille = 960; fsPhase = 0; fsCursor = 0;
                break;
            case 7:
                if (FinishStep()) { progressPermille = 1000; done = true; }
                break;
        }
        return done || failed;
    }

    public string lastRetry = "";

    // a stage whose function is in phases (autophase4: X_Body) runs a phase a step
    bool stRun, stFin;
    void StBegin() { PhResetAll(); PhSubReset(); stRun = true; }

    void Retry(string why)
    {
        lastRetry = why;
        attempt++;
        if (attempt >= 12)
        {
            failed = true;
            failReason = "no valid map after 12 attempts (" + why + ")";
            return;
        }
        stage = 0; cursor = 0; progressPermille = 0; popPhase = 0; ftmK = -1; tdK = -1; stRun = false;
    }

    // The layout stages draw from their own version, so the buildings of a seed survive a change of the prop
    // rules; the density setting is not part of any stream, so it changes how much is placed and nothing else.
    GenRng Rng(int st, int a, int b)
    {
        int version = (st == ST_LAYOUT || st == ST_EDGES) ? GenData.LAYOUT_VERSION : GenData.GEN_VERSION;
        return GenRng.Sub(seed, (uint)version, st * 16 + attempt, a, b + size * 1000 + EvoSalt(st, a));
    }

    // ---- evolution (Cor: maps evolve over their runs; the rotation passes the step, 0 = as generated)
    //   1 (small):  the things on furniture move in about a third of the rooms, and new wrong-category spawns
    //   2 (medium): about a third of the indoor rooms are furnished afresh, and every outdoor area regrows
    //   3 (big):    one building takes a different plan of its kind (LayoutSite); the rest keeps step 2
    // Each step only changes which numbers some streams draw, so a seed and a step give the same map everywhere.
    public int evo;
    public int evoVar;   // the number of the replay (0: the step as first designed): which rooms change, and what they draw

    bool EvoPick(int room, int salt) { return GenRng.Hash32(seed, 0xE70u, (uint)salt, (uint)room, (uint)evoVar) % 3u == 0u; }

    int EvoSalt(int st, int a)
    {
        if (evo <= 0) return 0;
        if (st == ST_LEAK) return 1000003 + evoVar * 7919;
        if (st == ST_SLOT && (EvoPick(a / 4, 1) || (evo >= 2 && RoomEvolves(a / 4)))) return 2000003 + evoVar * 7919;
        if (st == ST_ROOM && evo >= 2 && RoomEvolves(a)) return 3000017 + evoVar * 7919;
        return 0;
    }

    // from step 2: outdoor areas, and a third of the indoor rooms
    bool RoomEvolves(int room)
    {
        if (room < 0 || room >= nRooms) return false;
        return !GenData.IsBuilding(roomZone[room]) || EvoPick(room, 2);
    }

    static int Abs(int v) { return v < 0 ? -v : v; }
    static int Min(int a, int b) { return a < b ? a : b; }
    static bool Has(int def, int flag) { return (GenData.defFlags[def] & flag) != 0; }

    void Shuffle(int[] a, int n, ref GenRng rng)
    {
        for (int i = n - 1; i > 0; i--)
        {
            int j = rng.Range(i + 1);
            int t = a[i]; a[i] = a[j]; a[j] = t;
        }
    }

    public bool InGrid(int x, int z) { return x >= 0 && z >= 0 && x < W && z < H; }

    public int ZoneAt(int x, int z) { return InGrid(x, z) ? cellZone[z * W + x] : GenData.Z_NONE; }

    // The outer grid: plots along a lane (MapGenSite.cs). Layout 1's zone pieces glued to a lawn hub are gone
    // (generator 4 and its build are kept for comparison: prototype/before-build-v3, git 54693ba).
    bool Layout() { return LayoutSite(); }
}
