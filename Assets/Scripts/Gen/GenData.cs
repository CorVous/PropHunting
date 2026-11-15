// The generator's data tables: sizes, zone pieces, object definitions, slots and spawn tables.
// In the VRChat world these would be authored as assets and baked to arrays (04 section 4.5); for the
// prototype they are written here in code and stored the same way: flat arrays, integer units
// (millimetres, per-mille chances, inner cells of 0.5 m). No UnityEngine.
public static class GenData
{
    // Bump whenever any seed would produce a different map (04 section 6.3).
    public const int GEN_VERSION = 1;

    public const int CELL_MM = 4000;      // outer cell
    public const int INNER_MM = 500;      // inner cell
    public const int INNER_PER_CELL = 8;
    public const int WALL_HALF_MM = 100;  // walls are 0.2 m thick, centred on the cell edge
    public const int DOOR_CELLS = 2;      // doorway width in inner cells (1.0 m)
    public const int DOOR_HEIGHT_MM = 2100;

    // ---- size buckets (04 section 4.7.7) ----
    public static readonly string[] SizeName = { "S", "M", "L", "XL" };
    public static readonly int[] SizeGrid = { 7, 9, 12, 14 };            // bounding box, outer cells per side
    public static readonly int[] SizeGroundCells = { 28, 52, 85, 108 };  // ground cells to fill
    public static readonly int[] SizeLeaks = { 8, 11, 15, 18 };          // wrong-category spawns (04 section 4.3.5)
    public static readonly int[] SizeHiders = { 5, 11, 16, 21 };         // hiders the quota is sized for
    public static readonly int[] SizePlayers = { 8, 16, 24, 32 };
    public const int LEAK_COMEDIC_PERMILLE = 300;                        // D10
    public const int MIXED_SET_PERMILLE = 150;                           // 04 section 4.3.6
    public const int HIDING_SOCKET_PERMILLE = 250;
    public const int ROOM_SLOT_ITEM_CAP = 70;

    // ---- zones ----
    public const int Z_NONE = 0, Z_HOUSE = 1, Z_WAREHOUSE = 2, Z_DINER = 3, Z_LAWN = 4, Z_FOREST = 5, Z_RELEASE = 6;
    public static readonly string[] ZoneName = { "-", "House", "Warehouse", "Diner", "Lawn", "Forest", "Hunter release" };
    public static readonly int[] ZoneWallMm = { 0, 2600, 2800, 2800, 0, 0, 0 };   // KayKit buildings at the reduced height (Z4 option R)

    public static bool IsBuilding(int zone) { return zone == Z_HOUSE || zone == Z_WAREHOUSE || zone == Z_DINER; }

    // ---- room types ----
    public const int R_LIVING = 1, R_KITCHEN = 2, R_DINING = 3, R_BEDROOM = 4, R_BATH = 5, R_OFFICE = 6, R_KIDS = 7, R_HALL = 8;
    public const int R_LAWN_OPEN = 10, R_GARDEN = 11, R_PATIO = 12, R_FIREPIT = 13;
    public const int R_STORAGE = 20, R_LOADING = 21, R_WH_OFFICE = 22;
    public const int R_DINER_DINING = 30, R_DINER_KITCHEN = 31, R_DINER_STORE = 32;
    public const int R_FOREST_DENSE = 40, R_FOREST_CLEARING = 41;
    public const int R_RELEASE = 50;

    public static string RoomName(int r)
    {
        switch (r)
        {
            case R_LIVING: return "Living room";
            case R_KITCHEN: return "Kitchen";
            case R_DINING: return "Dining room";
            case R_BEDROOM: return "Bedroom";
            case R_BATH: return "Bathroom";
            case R_OFFICE: return "Office";
            case R_KIDS: return "Kids' room";
            case R_HALL: return "Hall";
            case R_LAWN_OPEN: return "Open lawn";
            case R_GARDEN: return "Garden bed";
            case R_PATIO: return "Patio";
            case R_FIREPIT: return "Fire pit";
            case R_STORAGE: return "Storage aisles";
            case R_LOADING: return "Loading dock";
            case R_WH_OFFICE: return "Warehouse office";
            case R_DINER_DINING: return "Diner seating";
            case R_DINER_KITCHEN: return "Diner kitchen";
            case R_DINER_STORE: return "Diner store room";
            case R_FOREST_DENSE: return "Dense forest";
            case R_FOREST_CLEARING: return "Forest camp";
            case R_RELEASE: return "Hunter release";
        }
        return "Room";
    }

    // ---- inner-cell tags (04 section 4.2) ----
    public const int T_FLOOR = 1 << 0, T_BLOCKED = 1 << 1, T_WALL_N = 1 << 2, T_WALL_E = 1 << 3, T_WALL_S = 1 << 4, T_WALL_W = 1 << 5;
    public const int T_CORNER = 1 << 6, T_CENTER = 1 << 7, T_DOOR_CLEAR = 1 << 8, T_PATH = 1 << 9, T_OUTDOOR = 1 << 10, T_COVERED = 1 << 11;
    public const int T_WINDOW_WALL = 1 << 12, T_EDGE_OUTDOOR = 1 << 13, T_SOFT_GROUND = 1 << 14, T_NO_SPAWN = 1 << 15;
    public const int T_SWING = 1 << 17;
    public const int T_WALL_ANY = T_WALL_N | T_WALL_E | T_WALL_S | T_WALL_W;

    // ---- edges between outer cells ----
    public const int E_NONE = 0, E_WALL = 1, E_DOOR = 2, E_WINDOW = 3, E_FENCE = 4;

    // ---- anchors, facing ----
    public const int A_WALL = 0, A_CORNER = 1, A_CENTER = 2, A_ANY = 3, A_OUT_OPEN = 4, A_OUT_EDGE = 5, A_SOFT = 6, A_COVERED = 7, A_SLOT_ONLY = 8;
    public const int F_AWAY_WALL = 0, F_TO_CENTER = 1, F_ALIGN_GRID = 2, F_RANDOM = 3;

    // ---- item tags (slots accept by tag) ----
    public const int I_CHAIR = 1 << 0, I_SETTING = 1 << 1, I_CENTERPIECE = 1 << 2, I_RUG = 1 << 3, I_LAMP = 1 << 4, I_DECOR = 1 << 5;
    public const int I_SCREEN = 1 << 6, I_KITCHENWARE = 1 << 7, I_FOOD = 1 << 8, I_BOX = 1 << 9, I_CRATE = 1 << 10, I_PILLOW = 1 << 11;
    public const int I_TOY = 1 << 12, I_BEDROLL = 1 << 13, I_DESKCHAIR = 1 << 14, I_TABLEWARE = 1 << 15, I_STOOL = 1 << 16, I_TOOL = 1 << 17;
    public const int I_SEAT_OUT = 1 << 18, I_MUSHROOM = 1 << 19;

    // ---- def flags ----
    public const int FL_SOLID = 1 << 0;      // blocks players and prop bodies (05 section 3.6.2)
    public const int FL_FIXTURE = 1 << 1;    // fixed dressing: never a disguise, never moves
    public const int FL_SCENERY = 1 << 2;    // trees: architecture layer, never a disguise
    public const int FL_FLAT = 1 << 3;       // rugs: take no floor cells
    public const int FL_DECOR = 1 << 4;      // grass tufts: no collider, not a prop
    public const int FL_SEEINTO = 1 << 5;    // open shelves, tents
    public const int FL_COMMON = 1 << 6;     // common small type: quota of 8 copies
    public const int FL_HEAVY = 1 << 7;      // STATIC_HEAVY: a dart does not move it
    public const int FL_LIGHT = 1 << 8;      // LIGHT: flies further
    public const int FL_LEAK_IN = 1 << 9;    // may leak into indoor rooms
    public const int FL_LEAK_OUT = 1 << 10;  // may leak into outdoor rooms
    public const int FL_OPENABLE = 1 << 11;  // has door or lid parts
    public const int FL_GENERATED = 1 << 12; // built in-house from simple shapes (Z5)

    // ---- collider kinds (the def's collider set, 05 N16, simplified to boxes) ----
    public const int C_BOX = 0, C_TABLE = 1, C_SEAT = 2, C_HOLLOW = 3, C_SHELF = 4, C_NONE = 5, C_TRUNK = 6, C_TENT = 7;

    // ---- slot types, sizes, group modes ----
    public const int S_ON_TOP = 0, S_ADJACENT = 1, S_INSIDE = 2, S_UNDER = 3;
    public const int SZ_XS = 0, SZ_S = 1, SZ_M = 2, SZ_L = 3, SZ_XL = 4;
    public const int G_INDEPENDENT = 0, G_MATCHED = 1, G_MIRROR_OF = 2, G_ELSE_OF = 3;

    // ---- object definitions ----
    const int MAXD = 256;
    public static int nDefs;
    public static string[] defId = new string[MAXD];
    public static int[] defModel = new int[MAXD];
    public static int[] defScale = new int[MAXD];      // per mille; one scale per def, everywhere (04 section 4.3.6)
    public static int[] defSx = new int[MAXD], defSy = new int[MAXD], defSz = new int[MAXD];   // size in mm at that scale
    public static int[] defW = new int[MAXD], defD = new int[MAXD];                            // footprint in inner cells
    public static int[] defTopY = new int[MAXD];       // height of the main top surface, mm
    public static int[] defTags = new int[MAXD];
    public static int[] defFlags = new int[MAXD];
    public static int[] defAnchor = new int[MAXD], defFacing = new int[MAXD], defClear = new int[MAXD], defSpacing = new int[MAXD];
    public static int[] defJitPos = new int[MAXD], defJitYaw = new int[MAXD];
    public static int[] defDisguise = new int[MAXD];   // 0 not a disguise, 1 tiny, 2 medium, 3 large
    public static int[] defMinCopies = new int[MAXD];
    public static int[] defLeakWeight = new int[MAXD], defAbsurd = new int[MAXD];
    public static int[] defCollider = new int[MAXD];
    public static int[] defSizeClass = new int[MAXD];
    public static int[] defSlotWeight = new int[MAXD];
    public static int[] defFamily = new int[MAXD];     // 0 Kenney, 1 KayKit, 2 generated/composite
    public static int[] defMassKg = new int[MAXD];
    public static int[] defSlotStart = new int[MAXD], defSlotCount = new int[MAXD];
    public static int[] defGroupStart = new int[MAXD], defGroupCount = new int[MAXD];

    const int MAXS = 1024;
    public static int nSlots;
    public static int[] slotType = new int[MAXS], slotX = new int[MAXS], slotY = new int[MAXS], slotZ = new int[MAXS];
    public static int[] slotYaw = new int[MAXS], slotSize = new int[MAXS], slotAccept = new int[MAXS], slotFill = new int[MAXS];
    public static int[] slotGroup = new int[MAXS];
    public static bool[] slotHiding = new bool[MAXS];
    const int MAXG = 512;
    public static int nGroups;
    public static int[] groupMode = new int[MAXG], groupChance = new int[MAXG], groupRef = new int[MAXG];

    // ---- spawn tables ----
    const int MAXT = 64, MAXE = 1024;
    public static int nTables;
    public static int[] tableZone = new int[MAXT], tableRoom = new int[MAXT], tableDensity = new int[MAXT];
    public static int[] tableEntryStart = new int[MAXT], tableEntryCount = new int[MAXT];
    public static int[] tableClutterStart = new int[MAXT], tableClutterCount = new int[MAXT];
    public static int[] tableClutterMin = new int[MAXT], tableClutterMax = new int[MAXT];
    public static int nEntries;
    public static int[] entryDef = new int[MAXE], entryWeight = new int[MAXE], entryMin = new int[MAXE], entryMax = new int[MAXE];
    public static int[] entryGroup = new int[MAXE];    // entries sharing a group id > 0 share one "max 1" budget
    public static int[] entryAnchor = new int[MAXE];   // -1 = the def's own anchor

    // ---- zone pieces (04 section 4.1.1) ----
    const int MAXP = 64;
    public static int nPieces;
    public static int[] pieceZone = new int[MAXP], pieceW = new int[MAXP], pieceH = new int[MAXP], pieceWeight = new int[MAXP];
    public static string[] pieceRows = new string[MAXP];   // rows joined by '/', a letter per cell = room, '.' = empty
    public static bool[] pieceHub = new bool[MAXP];

    static bool built;
    static int cur;

    // ------------------------------------------------------------------ helpers
    public static int Model(string name)
    {
        for (int i = 0; i < GenModels.Name.Length; i++)
            if (GenModels.Name[i] == name) return i;
        throw new System.Exception("GenData: unknown model " + name);
    }

    public static int FindDef(string id)
    {
        for (int i = 0; i < nDefs; i++)
            if (defId[i] == id) return i;
        throw new System.Exception("GenData: unknown def " + id);
    }

    static int Cells(int mm)
    {
        int c = (mm - 200 + INNER_MM - 1) / INNER_MM;
        return c < 1 ? 1 : c;
    }

    // Height of the main top surface of a model at scale 1 (mm): the highest up-facing surface that has at
    // least half the area of the largest one.
    static int ModelTop(int model)
    {
        int a = GenModels.SurfStart[model], b = GenModels.SurfStart[model + 1];
        if (a == b) return GenModels.SizeMm[model * 3 + 1];
        int maxArea = 0;
        for (int i = a; i < b; i++) if (GenModels.Surf[i * 6 + 5] > maxArea) maxArea = GenModels.Surf[i * 6 + 5];
        int top = 0;
        for (int i = a; i < b; i++)
            if (GenModels.Surf[i * 6 + 5] * 2 >= maxArea && GenModels.Surf[i * 6] > top) top = GenModels.Surf[i * 6];
        return top;
    }

    static int D(string id, string model, int scale, int tags, int anchor, int flags, int absurd)
    {
        int d = nDefs++;
        cur = d;
        int m = Model(model);
        defId[d] = id; defModel[d] = m; defScale[d] = scale;
        defSx[d] = GenModels.SizeMm[m * 3] * scale / 1000;
        defSy[d] = GenModels.SizeMm[m * 3 + 1] * scale / 1000;
        defSz[d] = GenModels.SizeMm[m * 3 + 2] * scale / 1000;
        defW[d] = Cells(defSx[d]); defD[d] = Cells(defSz[d]);
        defTopY[d] = ModelTop(m) * scale / 1000;
        defTags[d] = tags; defFlags[d] = flags; defAnchor[d] = anchor;
        defFacing[d] = (anchor == A_WALL || anchor == A_OUT_EDGE) ? F_AWAY_WALL : (anchor == A_CORNER ? F_TO_CENTER : (anchor == A_CENTER ? F_ALIGN_GRID : F_RANDOM));
        defClear[d] = 0; defSpacing[d] = 0;
        defJitPos[d] = 4; defJitYaw[d] = (anchor == A_WALL || anchor == A_CENTER) ? 3 : 12;
        defAbsurd[d] = absurd;
        defLeakWeight[d] = ((flags & (FL_LEAK_IN | FL_LEAK_OUT)) != 0) ? 10 : 0;
        defCollider[d] = ((flags & FL_FLAT) != 0 || (flags & FL_DECOR) != 0) ? C_NONE : C_BOX;
        string pack = GenModels.Pack[m];
        defFamily[d] = pack.StartsWith("kenney") ? 0 : ((pack == "generated" || pack.IndexOf('+') >= 0) ? 2 : 1);
        if (pack == "generated") defFlags[d] |= FL_GENERATED;
        int longest = defSx[d] > defSz[d] ? defSx[d] : defSz[d];
        if (defSy[d] > longest) longest = defSy[d];
        defSizeClass[d] = longest < 100 ? SZ_XS : (longest < 300 ? SZ_S : (longest < 600 ? SZ_M : (longest < 1000 ? SZ_L : SZ_XL)));
        defSlotWeight[d] = 10;
        // disguise class (04 section 4.3.1, Z3)
        bool can = (flags & (FL_FIXTURE | FL_SCENERY | FL_DECOR)) == 0 && longest <= 2500;
        if (!can) defDisguise[d] = 0;
        else if (longest < 350) defDisguise[d] = 1;
        else if (longest >= 1800 || defSy[d] >= 1600) defDisguise[d] = 3;
        else defDisguise[d] = 2;
        defMinCopies[d] = defDisguise[d] == 3 ? 3 : ((flags & FL_COMMON) != 0 ? 8 : 4);
        // rough mass for knocks: volume of the bounding box at 35 kg/m3 (furniture is mostly air), clamped
        long vol = (long)defSx[d] * defSy[d] / 1000 * defSz[d] / 1000;   // litres
        int kg = (int)(vol * 35 / 1000);
        defMassKg[d] = kg < 1 ? 1 : (kg > 40 ? 40 : kg);
        defSlotStart[d] = nSlots; defSlotCount[d] = 0;
        defGroupStart[d] = nGroups; defGroupCount[d] = 0;
        return d;
    }

    static void Col(int kind) { defCollider[cur] = kind; }
    static void Clear(int cells) { defClear[cur] = cells; }
    static void Spacing(int cells) { defSpacing[cur] = cells; }
    static void Facing(int f) { defFacing[cur] = f; }
    static void Jit(int posCm, int yawDeg) { defJitPos[cur] = posCm; defJitYaw[cur] = yawDeg; }
    static void Fp(int w, int d) { defW[cur] = w; defD[cur] = d; }
    static void LeakW(int w) { defLeakWeight[cur] = w; }
    static void SlotW(int w) { defSlotWeight[cur] = w; }
    static void NoDisguise() { defDisguise[cur] = 0; }

    static int Group(int mode, int chance, int refGroup)
    {
        int g = nGroups++;
        groupMode[g] = mode; groupChance[g] = chance; groupRef[g] = refGroup;
        defGroupCount[cur]++;
        return g - defGroupStart[cur];
    }

    static void Slot(int group, int type, int x, int y, int z, int yaw, int size, int accept, int fill)
    {
        int s = nSlots++;
        slotType[s] = type; slotX[s] = x; slotY[s] = y; slotZ[s] = z; slotYaw[s] = yaw;
        slotSize[s] = size; slotAccept[s] = accept; slotFill[s] = fill; slotGroup[s] = group; slotHiding[s] = false;
        defSlotCount[cur]++;
    }

    static void Hiding() { slotHiding[nSlots - 1] = true; }

    // A dining table: chairs on the floor next to it (MATCHED), place settings that follow the chairs
    // (MIRROR_OF), a centrepiece and a rug under it (04 section 4.4.2).
    static void SlotsDining(bool fourSides, int chairFill)
    {
        int d = cur;
        int sx = defSx[d], sz = defSz[d], top = defTopY[d];
        int gChairs = Group(G_MATCHED, 970, 0);
        int gSet = Group(G_MIRROR_OF, 1000, gChairs);
        int gMisc = Group(G_INDEPENDENT, 1000, 0);
        int reach = 230;   // chair centre this far outside the table edge
        int inset = 190;   // setting centre this far inside the table edge
        if (!fourSides)
        {
            int[] xs = { -sx / 4, sx / 4 };
            for (int i = 0; i < 2; i++)
            {
                Slot(gChairs, S_ADJACENT, xs[i], 0, sz / 2 + reach, 180, SZ_XL, I_CHAIR, chairFill); Hiding();
                Slot(gChairs, S_ADJACENT, xs[i], 0, -sz / 2 - reach, 0, SZ_XL, I_CHAIR, chairFill);
            }
            for (int i = 0; i < 2; i++)
            {
                Slot(gSet, S_ON_TOP, xs[i], top, sz / 2 - inset, 0, SZ_M, I_SETTING, 1000);
                Slot(gSet, S_ON_TOP, xs[i], top, -sz / 2 + inset, 180, SZ_M, I_SETTING, 1000);
            }
        }
        else
        {
            Slot(gChairs, S_ADJACENT, 0, 0, sz / 2 + reach, 180, SZ_XL, I_CHAIR, chairFill); Hiding();
            Slot(gChairs, S_ADJACENT, 0, 0, -sz / 2 - reach, 0, SZ_XL, I_CHAIR, chairFill);
            Slot(gChairs, S_ADJACENT, sx / 2 + reach, 0, 0, 270, SZ_XL, I_CHAIR, chairFill);
            Slot(gChairs, S_ADJACENT, -sx / 2 - reach, 0, 0, 90, SZ_XL, I_CHAIR, chairFill);
            Slot(gSet, S_ON_TOP, 0, top, sz / 2 - inset, 0, SZ_M, I_SETTING, 1000);
            Slot(gSet, S_ON_TOP, 0, top, -sz / 2 + inset, 180, SZ_M, I_SETTING, 1000);
            Slot(gSet, S_ON_TOP, sx / 2 - inset, top, 0, 90, SZ_M, I_SETTING, 1000);
            Slot(gSet, S_ON_TOP, -sx / 2 + inset, top, 0, 270, SZ_M, I_SETTING, 1000);
        }
        Slot(gMisc, S_ON_TOP, 0, top, 0, 0, SZ_M, I_CENTERPIECE | I_FOOD, 600);
        Slot(gMisc, S_UNDER, 0, 0, 0, 0, SZ_XL, I_RUG, 300);
    }

    // n places along the top, left to right
    static void SlotsTop(int n, int accept, int size, int fill)
    {
        int d = cur;
        int g = Group(G_INDEPENDENT, 1000, 0);
        for (int i = 0; i < n; i++)
        {
            int x = (2 * i + 1) * defSx[d] * 8 / 10 / (2 * n) - defSx[d] * 4 / 10;
            Slot(g, S_ON_TOP, x, defTopY[d], 0, 0, size, accept, fill);
        }
    }

    // places on every shelf board below the top (open bookcases, racks)
    static void SlotsShelves(int perShelf, int accept, int fill, bool alsoTop)
    {
        int d = cur, m = defModel[d], sc = defScale[d];
        int g = Group(G_INDEPENDENT, 1000, 0);
        int a = GenModels.SurfStart[m], b = GenModels.SurfStart[m + 1];
        int maxArea = 0;
        for (int i = a; i < b; i++) if (GenModels.Surf[i * 6 + 5] > maxArea) maxArea = GenModels.Surf[i * 6 + 5];
        // boards sorted by height
        int[] ys = new int[8];
        int n = 0;
        for (int i = a; i < b; i++)
        {
            if (GenModels.Surf[i * 6 + 5] * 2 < maxArea) continue;
            int y = GenModels.Surf[i * 6] * sc / 1000;
            if (!alsoTop && y >= defTopY[d] - 20 && y >= defSy[d] - 150) continue;
            int k = n++;
            while (k > 0 && ys[k - 1] > y) { ys[k] = ys[k - 1]; k--; }
            ys[k] = y;
        }
        bool first = true;
        for (int i = 0; i < n; i++)
        {
            int gap = (i + 1 < n ? ys[i + 1] : defSy[d]) - ys[i];
            int size = gap >= 620 ? SZ_L : (gap >= 330 ? SZ_M : (gap >= 150 ? SZ_S : SZ_XS));
            if (i == n - 1 && alsoTop) size = SZ_M;
            for (int k = 0; k < perShelf; k++)
            {
                int x = (2 * k + 1) * defSx[d] * 8 / 10 / (2 * perShelf) - defSx[d] * 4 / 10;
                Slot(g, S_INSIDE, x, ys[i], 0, 0, size, accept, fill);
                if (first) { Hiding(); first = false; }
            }
        }
    }

    // ------------------------------------------------------------------ tables and pieces
    static int Table(int zone, int room, int densityPermille, int clutterMin, int clutterMax)
    {
        int t = nTables++;
        // densities and clutter are tuned up from the first values so prop counts reach 04 section 4.7.7's ranges
        tableZone[t] = zone; tableRoom[t] = room; tableDensity[t] = densityPermille * 130 / 100;
        clutterMin += 1; clutterMax += 3;
        tableEntryStart[t] = nEntries; tableEntryCount[t] = 0;
        tableClutterMin[t] = clutterMin; tableClutterMax[t] = clutterMax;
        tableClutterStart[t] = 0; tableClutterCount[t] = 0;
        return t;
    }

    static void E(string def, int weight, int min, int max) { E(def, weight, min, max, 0, -1); }

    static void E(string def, int weight, int min, int max, int group, int anchor)
    {
        int e = nEntries++;
        entryDef[e] = FindDef(def); entryWeight[e] = weight; entryMin[e] = min; entryMax[e] = max;
        entryGroup[e] = group; entryAnchor[e] = anchor;
        tableEntryCount[nTables - 1]++;
    }

    // clutter entries follow the table's ordinary entries
    static void Clutter(string def, int weight)
    {
        int t = nTables - 1;
        int e = nEntries++;
        if (tableClutterCount[t] == 0) tableClutterStart[t] = e;
        entryDef[e] = FindDef(def); entryWeight[e] = weight; entryMin[e] = 0; entryMax[e] = 99;
        entryGroup[e] = 0; entryAnchor[e] = -1;
        tableClutterCount[t]++;
    }

    public static int FindTable(int zone, int room)
    {
        for (int t = 0; t < nTables; t++)
            if (tableZone[t] == zone && tableRoom[t] == room) return t;
        for (int t = 0; t < nTables; t++)
            if (tableZone[t] == zone) return t;   // per-zone fallback
        return -1;
    }

    static void Piece(int zone, string rows, int weight, bool hub)
    {
        int p = nPieces++;
        pieceZone[p] = zone; pieceRows[p] = rows; pieceWeight[p] = weight; pieceHub[p] = hub;
        string[] r = rows.Split('/');
        pieceH[p] = r.Length; pieceW[p] = r[0].Length;
    }

    // room letter at (x, y) of piece p before rotation, or '.'
    public static char PieceCell(int p, int x, int y)
    {
        return pieceRows[p][y * (pieceW[p] + 1) + x];
    }

    // ------------------------------------------------------------------ the content
    public static void Build()
    {
        if (built) return;
        built = true;

        const int IN = FL_LEAK_IN, OUT = FL_LEAK_OUT, BOTH = FL_LEAK_IN | FL_LEAK_OUT;
        const int K = 2000;   // Kenney Furniture Kit (02 section 4.1)
        const int F = 300;    // Kenney Food Kit

        // ===== Kenney Furniture Kit (House) =====
        D("k_table", "table", K, 0, A_CENTER, FL_SOLID | IN, 1); Col(C_TABLE); SlotsDining(false, 900);
        D("k_tableCoffee", "tableCoffee", K, 0, A_CENTER, FL_SOLID | IN, 1); Col(C_TABLE); SlotsTop(2, I_DECOR | I_FOOD | I_TABLEWARE, SZ_M, 450);
        D("k_chair", "chair", K, I_CHAIR, A_SLOT_ONLY, FL_COMMON | FL_LIGHT | BOTH, 0); Col(C_SEAT);
        D("k_chairCushion", "chairCushion", K, I_CHAIR, A_SLOT_ONLY, FL_COMMON | FL_LIGHT | BOTH, 0); Col(C_SEAT);
        D("k_chairDesk", "chairDesk", K, I_DESKCHAIR, A_SLOT_ONLY, FL_LIGHT | IN, 1); Col(C_SEAT); Fp(1, 1);
        D("k_sofa", "loungeSofa", K, 0, A_WALL, FL_SOLID | BOTH, 2); Col(C_SEAT); Clear(1);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, -defSx[cur] / 4, defTopY[cur], 60, 0, SZ_M, I_PILLOW | I_TOY, 350); Slot(g, S_ON_TOP, defSx[cur] / 4, defTopY[cur], 60, 0, SZ_M, I_PILLOW | I_TOY, 350); }
        D("k_loungeChair", "loungeChair", K, 0, A_WALL, FL_SOLID | BOTH, 1); Col(C_SEAT); Clear(1);
        D("k_bedDouble", "bedDouble", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defTopY[cur], 300, 0, SZ_M, I_TOY | I_PILLOW, 200); }
        D("k_bedSingle", "bedSingle", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defTopY[cur], 300, 0, SZ_M, I_TOY | I_PILLOW, 250); }
        D("k_bookcase", "bookcaseOpen", K, 0, A_WALL, FL_SOLID | FL_HEAVY | FL_SEEINTO | IN, 1); Col(C_SHELF); Clear(1); SlotsShelves(2, I_DECOR | I_BOX | I_TOY, 550, false);
        D("k_bookcaseLow", "bookcaseOpenLow", K, 0, A_WALL, FL_SOLID | FL_SEEINTO | IN, 1); Col(C_SHELF); Clear(1); SlotsShelves(2, I_DECOR | I_BOX | I_TOY, 500, true);
        D("k_tvCabinet", "cabinetTelevision", K, 0, A_WALL, FL_SOLID | IN, 1); Clear(2);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defTopY[cur], 0, 0, SZ_XL, I_SCREEN, 850); Slot(g, S_ON_TOP, -defSx[cur] * 4 / 10, defTopY[cur], 0, 0, SZ_S, I_DECOR, 300); }
        D("k_tv", "televisionModern", K, I_SCREEN, A_SLOT_ONLY, BOTH, 2);
        D("k_tvVintage", "televisionVintage", K, I_SCREEN, A_SLOT_ONLY, BOTH, 2);
        D("k_fridge", "kitchenFridge", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1);
        D("k_stove", "kitchenStove", K, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(1);
        D("k_sink", "kitchenSink", K, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(1);
        D("k_counter", "kitchenCabinet", K, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(1); SlotsTop(1, I_KITCHENWARE | I_FOOD, SZ_M, 700);
        D("k_counterDrawer", "kitchenCabinetDrawer", K, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(1); SlotsTop(1, I_KITCHENWARE | I_FOOD, SZ_M, 700);
        D("k_desk", "desk", K, 0, A_WALL, FL_SOLID | IN, 1); Col(C_TABLE); Clear(2);
        {
            int g = Group(G_INDEPENDENT, 1000, 0);
            Slot(g, S_ADJACENT, 0, 0, defSz[cur] / 2 + 260, 180, SZ_XL, I_DESKCHAIR, 850); Hiding();
            Slot(g, S_ON_TOP, -defSx[cur] / 6, defTopY[cur], -defSz[cur] / 6, 0, SZ_L, I_SCREEN, 750);
            Slot(g, S_ON_TOP, defSx[cur] * 35 / 100, defTopY[cur], -defSz[cur] / 6, 0, SZ_M, I_LAMP | I_DECOR, 500);
        }
        D("k_screen", "computerScreen", K, I_SCREEN, A_SLOT_ONLY, IN, 1); SlotW(14);
        D("k_laptop", "laptop", K, I_SCREEN | I_DECOR, A_SLOT_ONLY, BOTH, 1); SlotW(8);
        D("k_lampFloor", "lampRoundFloor", K, 0, A_CORNER, FL_LIGHT | BOTH, 1);
        D("k_lampTable", "lampRoundTable", K, I_LAMP, A_SLOT_ONLY, BOTH, 1);
        D("k_plant", "pottedPlant", K, 0, A_CORNER, FL_COMMON | BOTH, 0);
        D("k_plantSmall", "plantSmall1", K, I_DECOR, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("k_bathtub", "bathtub", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2);
        D("k_toilet", "toilet", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1);
        D("k_bathSink", "bathroomSink", K, 0, A_WALL, FL_SOLID | FL_HEAVY | IN, 2); Clear(1);
        D("k_washer", "washer", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1);
        D("k_sideTable", "sideTable", K, 0, A_WALL, FL_SOLID | IN, 1); Col(C_TABLE); SlotsTop(2, I_DECOR | I_LAMP, SZ_M, 500);
        D("k_bedside", "cabinetBed", K, 0, A_WALL, IN, 1); SlotsTop(1, I_LAMP | I_DECOR, SZ_M, 650);
        D("k_rug", "rugRound", K, I_RUG, A_CENTER, FL_FLAT | IN, 1); Fp(4, 4);
        D("k_rugLong", "rugRectangle", K, I_RUG, A_SLOT_ONLY, FL_FLAT | IN, 1); Fp(6, 4);
        D("k_trashcan", "trashcan", K, 0, A_WALL, FL_COMMON | FL_LIGHT | BOTH, 0);
        D("k_boxClosed", "cardboardBoxClosed", K, I_BOX, A_ANY, FL_COMMON | FL_LIGHT | BOTH, 0);
        D("k_boxOpen", "cardboardBoxOpen", K, I_BOX, A_ANY, FL_LIGHT | BOTH, 0);
        D("k_books", "books", K, I_DECOR, A_SLOT_ONLY, FL_COMMON | BOTH, 0); SlotW(16);
        D("k_radio", "radio", K, I_DECOR, A_SLOT_ONLY, BOTH, 1); SlotW(4);
        D("k_toaster", "toaster", K, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 1);
        D("k_microwave", "kitchenMicrowave", K, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 1);
        D("k_coffeeMachine", "kitchenCoffeeMachine", K, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 1);
        D("k_bench", "bench", K, 0, A_WALL, BOTH, 0); Col(C_SEAT);
        D("k_coatRack", "coatRackStanding", K, 0, A_CORNER, FL_LIGHT | IN, 1);
        D("k_pillow", "pillow", K, I_PILLOW, A_SLOT_ONLY, FL_LIGHT | BOTH, 0);
        D("k_speaker", "speaker", K, 0, A_WALL, FL_LIGHT | BOTH, 1);
        D("k_stool", "stoolBar", K, I_STOOL, A_ANY, FL_LIGHT | BOTH, 0);

        // ===== Kenney Food Kit (tableware and food; composites are one def, 04 section 4.4.1) =====
        D("setting_basic", "setting_basic", 1000, I_SETTING, A_SLOT_ONLY, FL_COMMON | BOTH, 0); SlotW(18);
        D("setting_food", "setting_food", 1000, I_SETTING, A_SLOT_ONLY, BOTH, 0); SlotW(8);
        D("condiments", "condiments", 1000, I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("f_plate", "plate", F, I_TABLEWARE, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("f_apple", "apple", F, I_FOOD, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("f_loaf", "loaf", F, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("f_cake", "cake", F, I_FOOD | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("f_pizzaBox", "pizza-box", F, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("f_sodaCan", "soda-can", F, I_FOOD, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("f_ketchup", "bottle-ketchup", F, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("f_mug", "mug", F, I_TABLEWARE | I_DECOR, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("f_bowl", "bowl", F, I_TABLEWARE | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("f_pan", "frying-pan", F, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 0);
        D("f_pot", "pot", F, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 0);
        D("f_cuttingBoard", "cutting-board", F, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 0);
        D("f_watermelon", "watermelon", F, I_FOOD | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("f_wine", "wine-red", F, I_FOOD | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("f_donut", "donut", F, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("f_pie", "pie", F, I_FOOD | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);

        // ===== KayKit Furniture Bits (base x0.7, then the per-prop normaliser, 04 section 4.3.6) =====
        D("q_armchair", "armchair", 550, 0, A_WALL, FL_SOLID | BOTH, 1); Col(C_SEAT); Clear(1);
        D("q_couch", "couch", 700, 0, A_WALL, FL_SOLID | BOTH, 2); Col(C_SEAT); Clear(1);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, -defSx[cur] / 4, defTopY[cur], 80, 0, SZ_M, I_PILLOW | I_TOY, 400); Slot(g, S_ON_TOP, defSx[cur] / 4, defTopY[cur], 80, 0, SZ_M, I_PILLOW | I_TOY, 300); }
        D("q_chair", "chair_A", 750, I_CHAIR, A_SLOT_ONLY, FL_COMMON | FL_LIGHT | BOTH, 0); Col(C_SEAT);
        D("q_stool", "chair_stool", 700, I_STOOL | I_SEAT_OUT, A_ANY, FL_LIGHT | BOTH, 0);
        D("q_table", "table_medium", 750, 0, A_CENTER, FL_SOLID | IN, 1); Col(C_TABLE); SlotsDining(true, 850);
        D("q_tableLow", "table_low", 600, 0, A_CENTER, FL_SOLID | IN, 1); Col(C_TABLE); SlotsTop(2, I_DECOR | I_FOOD | I_TABLEWARE, SZ_M, 450);
        D("q_tableSmall", "table_small", 650, 0, A_WALL, FL_SOLID | BOTH, 1); Col(C_TABLE); SlotsTop(1, I_LAMP | I_DECOR, SZ_M, 700);
        D("q_lampStanding", "lamp_standing", 700, 0, A_CORNER, FL_LIGHT | BOTH, 1);
        D("q_lampTable", "lamp_table", 500, I_LAMP, A_SLOT_ONLY, BOTH, 1);
        D("q_cabinet", "cabinet_medium", 800, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Clear(1); SlotsTop(3, I_LAMP | I_DECOR, SZ_M, 500);
        D("q_cabinetSmall", "cabinet_small", 800, 0, A_WALL, FL_SOLID | BOTH, 1); Clear(1); SlotsTop(1, I_LAMP | I_DECOR, SZ_M, 600);
        D("q_bed", "bed_single_A", 700, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defTopY[cur], 250, 0, SZ_M, I_TOY | I_PILLOW, 250); }
        D("q_rug", "rug_rectangle_A", 800, I_RUG, A_CENTER, FL_FLAT | IN, 1); Fp(5, 3);
        D("q_frame", "pictureframe_standing_A", 450, I_DECOR, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("q_pillow", "pillow_A", 600, I_PILLOW, A_SLOT_ONLY, FL_LIGHT | BOTH, 0);
        D("q_cactusSmall", "cactus_small_A", 500, I_DECOR, A_SLOT_ONLY, BOTH, 0);
        D("q_cactus", "cactus_medium_A", 650, 0, A_CORNER, BOTH, 0);
        D("q_books", "book_set", 450, I_DECOR, A_SLOT_ONLY, BOTH, 0);

        // ===== KayKit Restaurant Bits (Diner) =====
        D("r_chair", "restaurant_chair_A", 750, I_CHAIR, A_SLOT_ONLY, FL_COMMON | FL_LIGHT | BOTH, 0); Col(C_SEAT);
        D("r_stool", "restaurant_chair_stool", 700, I_STOOL | I_SEAT_OUT, A_ANY, FL_LIGHT | BOTH, 0);
        D("r_table", "kitchentable_A", 750, 0, A_CENTER, FL_SOLID | IN, 1); Col(C_TABLE); SlotsDining(true, 900);
        D("r_tableRound", "table_round_A_small", 750, 0, A_CENTER, FL_SOLID | BOTH, 1); Col(C_TABLE); SlotsDining(true, 600);
        D("r_plate", "restaurant_plate", 280, I_TABLEWARE, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defTopY[cur] > 12 ? 12 : defTopY[cur], 0, 0, SZ_S, I_FOOD, 500); }
        D("r_bowl", "restaurant_bowl", 220, I_TABLEWARE | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("r_burger", "food_burger", 180, I_FOOD, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("r_dinner", "food_dinner", 250, I_FOOD | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("r_ketchup", "ketchup", 300, I_FOOD, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("r_mustard", "mustard", 300, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("r_crate", "crate", 350, I_CRATE, A_ANY, FL_COMMON | BOTH, 0);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defSy[cur], 0, 0, SZ_L, I_CRATE | I_BOX, 300); }
        D("r_crateTomatoes", "crate_tomatoes", 350, I_CRATE, A_ANY, BOTH, 0);
        D("r_crateCheese", "crate_cheese", 350, I_CRATE, A_ANY, BOTH, 0);
        D("r_fridge", "fridge_A", 700, 0, A_WALL, FL_SOLID | FL_HEAVY | FL_OPENABLE | BOTH, 2); Col(C_HOLLOW); Clear(3);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_INSIDE, -250, 256 * 700 / 1000, 0, 0, SZ_M, I_FOOD | I_BOX | I_KITCHENWARE, 500); Slot(g, S_INSIDE, 250, 256 * 700 / 1000, 0, 0, SZ_M, I_FOOD | I_BOX, 350); Hiding(); }
        D("r_counter", "kitchencounter_straight_A", 800, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(1); SlotsTop(2, I_KITCHENWARE | I_FOOD | I_TABLEWARE, SZ_M, 650);
        D("r_counterSink", "kitchencounter_sink", 800, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(1);
        D("r_stove", "stove_single", 800, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(1);
        D("r_dishrack", "dishrack_plates", 400, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 0);
        D("r_pot", "pot_A", 300, I_KITCHENWARE, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("r_pan", "pan_A", 300, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 0);
        D("r_menu", "menu", 350, I_CENTERPIECE | I_DECOR, A_SLOT_ONLY, BOTH, 0);
        D("r_papertowel", "papertowel", 300, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 0);
        D("r_jar", "jar_A_large", 400, I_KITCHENWARE | I_FOOD, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("r_cuttingboard", "cuttingboard", 300, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 0);

        // ===== KayKit Prototype and Resource Bits, Kenney Survival Kit (Warehouse) =====
        D("w_barrel", "Barrel_A", 850, I_CRATE, A_ANY, FL_SOLID | FL_COMMON | BOTH, 0);
        D("w_barrelB", "Barrel_B", 850, I_CRATE, A_ANY, FL_SOLID | BOTH, 0);
        D("w_boxA", "Box_A", 1000, I_BOX, A_ANY, FL_COMMON | FL_LIGHT | BOTH, 0);
        D("w_boxB", "Box_B", 900, I_BOX, A_ANY, FL_COMMON | FL_LIGHT | BOTH, 0);
        D("w_boxC", "Box_C", 800, I_BOX, A_ANY, FL_LIGHT | BOTH, 0);
        D("w_pallet", "Pallet_Wood", 750, 0, A_ANY, FL_SOLID | FL_HEAVY | BOTH, 0); Facing(F_ALIGN_GRID); Jit(4, 4);
        {
            int gBig = Group(G_INDEPENDENT, 450, 0);
            Slot(gBig, S_ON_TOP, 0, defTopY[cur], 0, 0, SZ_XL, I_CRATE, 1000);
            int gSmall = Group(G_ELSE_OF, 900, gBig);
            int q = 290;
            Slot(gSmall, S_ON_TOP, -q, defTopY[cur], -q, 0, SZ_M, I_BOX, 700);
            Slot(gSmall, S_ON_TOP, q, defTopY[cur], -q, 0, SZ_M, I_BOX, 700);
            Slot(gSmall, S_ON_TOP, -q, defTopY[cur], q, 0, SZ_M, I_BOX, 600);
            Slot(gSmall, S_ON_TOP, q, defTopY[cur], q, 0, SZ_M, I_BOX, 500); Hiding();
        }
        D("w_palletLoaded", "Pallet_Small_Decorated_A", 600, 0, A_ANY, FL_SOLID | FL_HEAVY | BOTH, 1); Facing(F_ALIGN_GRID);
        D("w_fuelBarrel", "Fuel_A_Barrel", 900, I_CRATE, A_ANY, FL_SOLID | FL_COMMON | BOTH, 0);
        D("w_fuelBarrelB", "Fuel_B_Barrel", 900, I_CRATE, A_ANY, FL_SOLID | BOTH, 0);
        D("w_jerrycan", "Fuel_A_Jerrycan", 600, I_BOX, A_ANY, FL_COMMON | FL_LIGHT | BOTH, 0);
        D("w_planks", "Wood_Planks_Stack_Medium", 700, 0, A_ANY, FL_SOLID | FL_HEAVY | BOTH, 0); Facing(F_ALIGN_GRID);
        D("w_logs", "Wood_Log_Stack", 700, 0, A_ANY, FL_SOLID | FL_HEAVY | BOTH, 1); Facing(F_ALIGN_GRID);
        D("w_textiles", "Textiles_Stack_Small", 700, 0, A_ANY, FL_SOLID | FL_HEAVY | BOTH, 1); Facing(F_ALIGN_GRID);
        D("w_textileRoll", "Textiles_A", 700, I_BOX, A_ANY, FL_LIGHT | BOTH, 0);
        D("w_bricks", "Stone_Bricks_Stack_Small", 800, 0, A_ANY, FL_SOLID | FL_HEAVY | BOTH, 0);
        D("w_goldBars", "Gold_Bars", 400, I_BOX, A_ANY, BOTH, 2);
        D("w_ironStack", "Iron_Bars_Stack_Small", 700, I_BOX, A_ANY, FL_HEAVY | BOTH, 0);
        D("w_parts", "Parts_Pile_Small", 700, 0, A_ANY, BOTH, 0);
        D("w_cog", "Parts_Cog", 600, I_TOOL | I_BOX, A_SLOT_ONLY, BOTH, 0);
        D("w_nugget", "Copper_Nugget_Large", 500, I_BOX | I_TOOL, A_SLOT_ONLY, BOTH, 1);
        D("gen_rack", "gen_rack", 1000, 0, A_WALL, FL_SOLID | FL_HEAVY | FL_SEEINTO | FL_FIXTURE, 1); Col(C_SHELF); Clear(1); Fp(4, 1);
        SlotsShelves(3, I_BOX | I_TOOL | I_KITCHENWARE, 600, true);
        D("s_barrel", "barrel", 2600, I_CRATE, A_ANY, FL_SOLID | BOTH, 0);
        D("s_box", "box", 2400, I_BOX, A_ANY, FL_COMMON | BOTH, 0);
        D("s_boxLarge", "box-large", 2400, 0, A_ANY, FL_SOLID | BOTH, 0);
        D("s_bucket", "bucket", 2000, I_BOX | I_TOOL, A_ANY, FL_COMMON | FL_LIGHT | BOTH, 0);
        D("s_chest", "chest", 2500, I_TOY, A_ANY, BOTH, 2); Jit(4, 20);
        D("s_bedroll", "bedroll", 2600, I_BEDROLL, A_ANY, FL_LIGHT | BOTH, 1);
        D("s_axe", "tool-axe", 2500, I_TOOL, A_SLOT_ONLY, BOTH, 1);
        D("s_signpost", "signpost", 3000, 0, A_OUT_EDGE, BOTH, 1);
        D("s_workbench", "workbench", 2800, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Clear(1); SlotsTop(2, I_TOOL | I_BOX, SZ_M, 600);
        D("s_treeLog", "tree-log", 2000, I_SEAT_OUT, A_SOFT, FL_SOLID | FL_HEAVY | OUT, 0);
        D("s_bottle", "bottle", 1500, I_FOOD | I_DECOR, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("s_campfirePit", "campfire-pit", 3000, 0, A_OUT_OPEN, FL_HEAVY | BOTH, 2);

        // ===== generated wardrobe (Z5: the packs have none) =====
        D("gen_wardrobe", "gen_wardrobe", 1000, 0, A_WALL, FL_SOLID | FL_HEAVY | FL_OPENABLE | BOTH, 2); Col(C_HOLLOW); Clear(3); Fp(2, 1);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_INSIDE, -250, 120, 20, 0, SZ_M, I_BOX | I_PILLOW | I_TOY, 400); Slot(g, S_INSIDE, 250, 120, 20, 0, SZ_M, I_BOX | I_PILLOW | I_TOY, 300); Hiding(); }

        // ===== Kenney Nature Kit (Lawn, Forest) =====
        D("n_treeDefault", "tree_default", 3500, 0, A_SOFT, FL_SOLID | FL_SCENERY, 0); Col(C_TRUNK); Fp(1, 1); Spacing(4); Jit(15, 180);
        D("n_treeOak", "tree_oak", 3500, 0, A_SOFT, FL_SOLID | FL_SCENERY, 0); Col(C_TRUNK); Fp(1, 1); Spacing(4); Jit(15, 180);
        D("n_treePineTall", "tree_pineTallA", 3500, 0, A_SOFT, FL_SOLID | FL_SCENERY, 0); Col(C_TRUNK); Fp(1, 1); Spacing(3); Jit(15, 180);
        D("n_treePineRound", "tree_pineRoundA", 3500, 0, A_SOFT, FL_SOLID | FL_SCENERY, 0); Col(C_TRUNK); Fp(1, 1); Spacing(4); Jit(15, 180);
        D("n_treeFat", "tree_fat", 3500, 0, A_OUT_OPEN, FL_SOLID | FL_SCENERY, 0); Col(C_TRUNK); Fp(1, 1); Spacing(5); Jit(15, 180);
        D("n_bush", "plant_bush", 3000, 0, A_OUT_OPEN, FL_COMMON | OUT, 0); Jit(10, 180);
        D("n_bushLarge", "plant_bushLarge", 3000, 0, A_OUT_EDGE, OUT, 0); Jit(6, 20);
        D("n_bushDetailed", "plant_bushDetailed", 2000, 0, A_OUT_OPEN, OUT, 0); Jit(10, 180);
        D("n_flowerRed", "flower_redA", 2500, 0, A_SOFT, FL_COMMON | FL_LIGHT | OUT, 0); Jit(12, 180);
        D("n_flowerYellow", "flower_yellowA", 2500, 0, A_SOFT, FL_COMMON | FL_LIGHT | OUT, 0); Jit(12, 180);
        D("n_flowerPurple", "flower_purpleA", 2500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(12, 180);
        D("n_rockLarge", "rock_largeA", 2000, 0, A_SOFT, FL_SOLID | FL_HEAVY | OUT, 0); Jit(10, 180);
        D("n_rockLargeB", "rock_largeB", 1800, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 0); Jit(10, 180);
        D("n_rockSmall", "rock_smallA", 1800, 0, A_OUT_OPEN, FL_COMMON | FL_HEAVY | BOTH, 1); Jit(12, 180);
        D("n_rockSmallB", "rock_smallD", 1600, 0, A_SOFT, FL_HEAVY | OUT, 0); Jit(12, 180);
        D("n_rockTall", "rock_tallA", 1500, 0, A_SOFT, FL_SOLID | FL_HEAVY | OUT, 0); Jit(10, 180);
        D("n_stump", "stump_old", 2500, I_SEAT_OUT, A_SOFT, FL_SOLID | FL_HEAVY | OUT, 0); Jit(10, 180);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defSy[cur] * 75 / 100, 0, 0, SZ_L, I_TOOL | I_MUSHROOM, 300); }
        D("n_stumpRound", "stump_round", 2500, I_SEAT_OUT, A_SOFT, FL_SOLID | FL_HEAVY | OUT, 0); Jit(10, 180);
        D("n_log", "log", 3000, I_SEAT_OUT, A_SOFT, FL_COMMON | FL_HEAVY | OUT, 0); Jit(10, 25);
        D("n_logStack", "log_stack", 2500, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | BOTH, 0);
        D("n_logLarge", "log_large", 2000, 0, A_SOFT, FL_SOLID | FL_HEAVY | OUT, 0); Jit(10, 25);
        D("n_mushroomRed", "mushroom_red", 2500, I_MUSHROOM, A_SOFT, FL_COMMON | FL_LIGHT | BOTH, 1); Jit(15, 180);
        D("n_mushroomGroup", "mushroom_redGroup", 2500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(15, 180);
        D("n_mushroomTan", "mushroom_tan", 2500, I_MUSHROOM, A_SOFT, FL_COMMON | FL_LIGHT | OUT, 0); Jit(15, 180);
        D("n_campfire", "campfire_stones", 2500, 0, A_OUT_OPEN, FL_HEAVY | BOTH, 2); Fp(3, 3);
        {
            int g = Group(G_INDEPENDENT, 1000, 0);
            int r = 1350;
            Slot(g, S_ADJACENT, 0, 0, r, 180, SZ_XL, I_SEAT_OUT, 700); Hiding();
            Slot(g, S_ADJACENT, 0, 0, -r, 0, SZ_XL, I_SEAT_OUT, 700);
            Slot(g, S_ADJACENT, r, 0, 0, 270, SZ_XL, I_SEAT_OUT, 600);
            Slot(g, S_ADJACENT, -r, 0, 0, 90, SZ_XL, I_SEAT_OUT, 600);
        }
        D("n_tentOpen", "tent_detailedOpen", 3000, 0, A_SOFT, FL_SOLID | FL_HEAVY | FL_SEEINTO | FL_FIXTURE, 2); Col(C_TENT); Facing(F_TO_CENTER); Jit(5, 6);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_INSIDE, 0, 40, -150, 0, SZ_XL, I_BEDROLL, 800); Hiding(); }
        D("n_tentClosed", "tent_smallClosed", 3000, 0, A_SOFT, FL_SOLID | FL_HEAVY | BOTH, 2); Jit(5, 30);
        D("n_fence", "fence_planks", 2000, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 0); Jit(2, 2);
        D("n_sign", "sign", 2500, 0, A_OUT_EDGE, BOTH, 1);
        D("n_planter", "pot_large", 2000, 0, A_COVERED, FL_SOLID | FL_HEAVY | BOTH, 0);
        D("n_canoe", "canoe", 2000, 0, A_OUT_EDGE, FL_HEAVY | BOTH, 2);
        D("n_sleepingMat", "bed_floor", 2500, I_BEDROLL, A_SLOT_ONLY, FL_LIGHT | BOTH, 1);
        D("n_grass", "grass", 3000, 0, A_SOFT, FL_DECOR, 0); Jit(20, 180);
        D("n_grassLarge", "grass_large", 3000, 0, A_SOFT, FL_DECOR, 0); Jit(20, 180);

        // ===== Kenney Cube Pets (toy props, Z6) =====
        D("toy_cat", "animal-cat", 200, I_TOY, A_ANY, FL_LIGHT | BOTH, 1); Jit(8, 180);
        D("toy_dog", "animal-dog", 200, I_TOY, A_ANY, FL_LIGHT | BOTH, 1); Jit(8, 180);
        D("toy_bunny", "animal-bunny", 200, I_TOY, A_ANY, FL_LIGHT | BOTH, 1); Jit(8, 180);
        D("toy_chick", "animal-chick", 180, I_TOY, A_ANY, FL_LIGHT | BOTH, 1); Jit(8, 180);
        D("toy_panda", "animal-panda", 200, I_TOY, A_ANY, FL_LIGHT | BOTH, 1); Jit(8, 180);

        BuildTables();
        BuildPieces();
    }

    static void BuildTables()
    {
        // entry: def, weight, min, max [, exclusive group, anchor override]
        // ---------------- HOUSE ----------------
        Table(Z_HOUSE, R_LIVING, 260, 1, 4);
        E("k_sofa", 6, 1, 1, 1, -1); E("q_couch", 4, 1, 1, 1, -1);
        E("k_tableCoffee", 5, 0, 1, 2, -1); E("q_tableLow", 3, 0, 1, 2, -1);
        E("k_tvCabinet", 6, 0, 1); E("q_armchair", 4, 0, 2); E("k_loungeChair", 4, 0, 2);
        E("k_bookcase", 4, 0, 2); E("k_bookcaseLow", 3, 0, 1); E("q_cabinet", 3, 0, 1);
        E("k_lampFloor", 3, 0, 1); E("q_lampStanding", 3, 0, 1); E("k_plant", 4, 0, 2); E("q_cactus", 2, 0, 1);
        E("k_sideTable", 3, 0, 1); E("q_tableSmall", 2, 0, 1); E("k_speaker", 2, 0, 2); E("k_rug", 3, 0, 1); E("q_rug", 3, 0, 1);
        Clutter("k_boxClosed", 3); Clutter("toy_cat", 1); Clutter("toy_dog", 1); Clutter("k_stool", 2);

        Table(Z_HOUSE, R_DINING, 230, 0, 2);
        E("k_table", 6, 1, 1, 1, -1); E("q_table", 4, 1, 1, 1, -1);
        E("q_cabinet", 4, 0, 1); E("k_bookcase", 2, 0, 1); E("k_bookcaseLow", 3, 0, 1); E("k_sideTable", 3, 0, 1);
        E("k_plant", 4, 0, 2); E("q_lampStanding", 2, 0, 1); E("k_lampFloor", 2, 0, 1);
        Clutter("k_stool", 2); Clutter("k_boxClosed", 1);

        Table(Z_HOUSE, R_KITCHEN, 240, 0, 2);
        E("k_stove", 5, 1, 1); E("k_sink", 5, 1, 1); E("k_fridge", 5, 1, 1);
        E("k_counter", 6, 1, 3); E("k_counterDrawer", 4, 0, 2);
        E("k_trashcan", 4, 0, 1); E("k_stool", 3, 0, 2); E("q_tableSmall", 2, 0, 1); E("k_plant", 2, 0, 1);
        E("r_tableRound", 3, 0, 1, 1, -1);
        Clutter("k_boxClosed", 2); Clutter("r_crate", 1); Clutter("k_stool", 1);

        Table(Z_HOUSE, R_BEDROOM, 250, 0, 3);
        E("k_bedDouble", 5, 1, 1, 1, -1); E("k_bedSingle", 3, 1, 1, 1, -1); E("q_bed", 3, 1, 1, 1, -1);
        E("gen_wardrobe", 7, 0, 1); E("k_bedside", 6, 0, 2); E("q_cabinet", 3, 0, 1); E("q_cabinetSmall", 2, 0, 1);
        E("k_desk", 3, 0, 1); E("k_lampFloor", 2, 0, 1); E("k_plant", 3, 0, 1); E("k_rug", 2, 0, 1); E("q_rug", 2, 0, 1);
        E("k_bookcaseLow", 2, 0, 1); E("q_armchair", 2, 0, 1);
        Clutter("k_boxClosed", 3); Clutter("k_boxOpen", 2); Clutter("toy_bunny", 1);

        Table(Z_HOUSE, R_BATH, 220, 0, 1);
        E("k_toilet", 5, 1, 1); E("k_bathSink", 5, 1, 1); E("k_bathtub", 5, 0, 1); E("k_washer", 4, 0, 1);
        E("k_trashcan", 3, 0, 1); E("k_plant", 2, 0, 1); E("q_cabinetSmall", 2, 0, 1);
        Clutter("s_bucket", 2); Clutter("k_boxClosed", 1);

        Table(Z_HOUSE, R_OFFICE, 240, 1, 4);
        E("k_desk", 6, 1, 2); E("k_bookcase", 5, 1, 2); E("q_cabinet", 3, 0, 1); E("k_bookcaseLow", 3, 0, 1);
        E("k_plant", 3, 0, 1); E("k_lampFloor", 3, 0, 1); E("k_sideTable", 2, 0, 1); E("q_armchair", 2, 0, 1); E("k_trashcan", 3, 0, 1);
        E("gen_wardrobe", 2, 0, 1);
        Clutter("k_boxClosed", 4); Clutter("k_boxOpen", 2); Clutter("w_boxA", 2);

        Table(Z_HOUSE, R_KIDS, 240, 2, 5);
        E("k_bedSingle", 5, 1, 2); E("q_bed", 3, 0, 1); E("k_bookcaseLow", 4, 0, 2); E("k_desk", 3, 0, 1);
        E("gen_wardrobe", 4, 0, 1); E("k_rug", 4, 0, 1); E("k_bedside", 3, 0, 1); E("k_plant", 1, 0, 1);
        Clutter("toy_cat", 3); Clutter("toy_dog", 3); Clutter("toy_bunny", 3); Clutter("toy_chick", 3); Clutter("toy_panda", 3); Clutter("k_boxOpen", 2);

        Table(Z_HOUSE, R_HALL, 180, 0, 2);
        E("k_bench", 5, 0, 1); E("k_coatRack", 5, 0, 1); E("gen_wardrobe", 4, 0, 1); E("k_plant", 4, 0, 2);
        E("q_cabinetSmall", 3, 0, 1); E("k_sideTable", 3, 0, 1); E("k_lampFloor", 2, 0, 1);
        Clutter("k_boxClosed", 3); Clutter("s_bucket", 1);

        // ---------------- WAREHOUSE ----------------
        Table(Z_WAREHOUSE, R_STORAGE, 360, 3, 8);
        E("gen_rack", 0, 0, 10);   // placed by the row template, not by weight
        E("w_pallet", 8, 2, 8); E("w_palletLoaded", 4, 0, 3); E("w_barrel", 5, 1, 6); E("w_barrelB", 3, 0, 3);
        E("w_fuelBarrel", 4, 0, 4); E("w_fuelBarrelB", 2, 0, 2); E("r_crate", 5, 1, 6); E("w_planks", 3, 0, 2);
        E("w_logs", 2, 0, 1); E("w_textiles", 3, 0, 2); E("w_bricks", 3, 0, 2); E("w_ironStack", 2, 0, 2);
        E("w_parts", 2, 0, 2); E("s_boxLarge", 3, 0, 3); E("s_barrel", 3, 0, 3); E("s_workbench", 3, 0, 1);
        Clutter("w_boxA", 4); Clutter("w_boxB", 4); Clutter("w_boxC", 3); Clutter("w_jerrycan", 3); Clutter("s_box", 3);
        Clutter("s_bucket", 2); Clutter("w_textileRoll", 2); Clutter("k_boxClosed", 2);

        Table(Z_WAREHOUSE, R_LOADING, 300, 2, 5);
        E("w_pallet", 8, 1, 4); E("w_palletLoaded", 5, 0, 2); E("r_crate", 5, 0, 4); E("r_crateTomatoes", 2, 0, 2);
        E("w_barrel", 4, 0, 3); E("w_fuelBarrel", 4, 0, 3); E("s_boxLarge", 3, 0, 2); E("w_planks", 2, 0, 1);
        E("s_workbench", 2, 0, 1); E("k_trashcan", 2, 0, 1);
        Clutter("w_boxA", 4); Clutter("w_boxB", 3); Clutter("w_jerrycan", 3); Clutter("s_box", 3); Clutter("s_bucket", 2);

        Table(Z_WAREHOUSE, R_WH_OFFICE, 230, 1, 4);
        E("k_desk", 6, 1, 1); E("q_cabinet", 4, 0, 1); E("k_bookcase", 4, 0, 2); E("k_trashcan", 3, 0, 1);
        E("k_plant", 2, 0, 1); E("s_workbench", 3, 0, 1); E("q_tableSmall", 2, 0, 1); E("k_lampFloor", 2, 0, 1);
        Clutter("w_boxA", 4); Clutter("k_boxClosed", 4); Clutter("w_boxB", 3);

        // ---------------- DINER ----------------
        Table(Z_DINER, R_DINER_DINING, 240, 0, 3);
        E("r_table", 8, 2, 5); E("r_tableRound", 5, 0, 3); E("k_table", 2, 0, 1);
        E("k_plant", 3, 0, 2); E("q_cactus", 2, 0, 1); E("q_cabinet", 2, 0, 1); E("q_lampStanding", 2, 0, 1); E("k_trashcan", 2, 0, 1);
        E("k_bench", 2, 0, 2);
        Clutter("r_stool", 4); Clutter("q_stool", 2); Clutter("k_stool", 2);

        Table(Z_DINER, R_DINER_KITCHEN, 260, 1, 3);
        E("r_counter", 7, 1, 3); E("r_counterSink", 5, 1, 1); E("r_stove", 5, 1, 1); E("r_fridge", 6, 1, 1);
        E("r_crate", 4, 0, 2); E("r_crateTomatoes", 3, 0, 2); E("r_crateCheese", 3, 0, 1); E("k_trashcan", 3, 0, 1);
        Clutter("r_crate", 3); Clutter("s_bucket", 2); Clutter("w_boxA", 2); Clutter("r_stool", 1);

        Table(Z_DINER, R_DINER_STORE, 300, 1, 4);
        E("r_fridge", 4, 0, 1); E("r_crate", 6, 1, 4); E("r_crateTomatoes", 4, 0, 2); E("r_crateCheese", 4, 0, 2);
        E("w_barrel", 3, 0, 2); E("gen_rack", 5, 0, 2, 0, A_WALL); E("s_barrel", 2, 0, 2); E("w_pallet", 3, 0, 1);
        Clutter("w_boxA", 3); Clutter("w_boxB", 3); Clutter("s_box", 2); Clutter("w_jerrycan", 1);

        // ---------------- LAWN ----------------
        Table(Z_LAWN, R_LAWN_OPEN, 70, 0, 2);
        E("n_bush", 6, 0, 3); E("n_bushDetailed", 3, 0, 2); E("n_rockSmall", 4, 0, 2); E("n_treeFat", 3, 0, 1);
        E("n_flowerRed", 4, 0, 3, 0, A_OUT_OPEN); E("n_flowerYellow", 4, 0, 3, 0, A_OUT_OPEN); E("n_sign", 2, 0, 1);
        E("n_bushLarge", 3, 0, 2); E("k_bench", 2, 0, 1, 0, A_OUT_EDGE); E("n_logStack", 1, 0, 1);
        Clutter("n_mushroomRed", 1); Clutter("s_bucket", 1); Clutter("n_rockSmall", 2);

        Table(Z_LAWN, R_GARDEN, 150, 0, 2);
        E("n_flowerRed", 6, 2, 6); E("n_flowerYellow", 6, 1, 6); E("n_flowerPurple", 5, 1, 5);
        E("n_bush", 4, 0, 3); E("n_bushLarge", 3, 0, 2); E("n_rockSmallB", 2, 0, 2); E("n_treeFat", 2, 0, 1, 0, A_SOFT);
        E("n_planter", 3, 0, 2, 0, A_SOFT); E("n_sign", 2, 0, 1);
        Clutter("s_bucket", 3); Clutter("k_boxOpen", 1); Clutter("n_mushroomTan", 1);

        Table(Z_LAWN, R_PATIO, 130, 0, 2);
        E("k_table", 5, 1, 1, 1, A_OUT_OPEN); E("r_tableRound", 4, 1, 1, 1, A_OUT_OPEN);
        E("n_planter", 5, 0, 2, 0, A_OUT_OPEN); E("k_loungeChair", 3, 0, 2, 0, A_OUT_OPEN); E("k_bench", 3, 0, 1, 0, A_OUT_EDGE);
        E("n_bush", 3, 0, 2); E("k_plant", 3, 0, 2, 0, A_OUT_OPEN); E("s_barrel", 2, 0, 1, 0, A_OUT_OPEN);
        Clutter("q_stool", 2); Clutter("s_bucket", 1); Clutter("k_stool", 1);

        Table(Z_LAWN, R_FIREPIT, 110, 0, 2);
        E("n_campfire", 5, 1, 1, 1, -1); E("s_campfirePit", 3, 1, 1, 1, -1);
        E("n_logStack", 4, 0, 1); E("n_bush", 4, 0, 3); E("n_rockSmall", 3, 0, 2); E("n_stump", 3, 0, 2, 0, A_OUT_OPEN);
        E("n_log", 3, 0, 2, 0, A_OUT_OPEN);
        Clutter("s_bucket", 2); Clutter("n_mushroomRed", 1);

        // ---------------- FOREST ----------------
        Table(Z_FOREST, R_FOREST_DENSE, 170, 2, 6);
        E("n_treeDefault", 6, 2, 5); E("n_treeOak", 5, 1, 4); E("n_treePineTall", 6, 1, 5); E("n_treePineRound", 4, 0, 3);
        E("n_bush", 5, 1, 4, 0, A_SOFT); E("n_bushDetailed", 3, 0, 2, 0, A_SOFT); E("n_rockLarge", 3, 0, 1); E("n_rockSmallB", 4, 0, 3);
        E("n_rockTall", 2, 0, 1); E("n_stump", 4, 0, 2); E("n_stumpRound", 3, 0, 2); E("n_log", 4, 0, 2); E("n_logLarge", 2, 0, 1);
        E("s_treeLog", 2, 0, 1); E("n_mushroomRed", 5, 0, 4); E("n_mushroomTan", 5, 0, 4); E("n_mushroomGroup", 3, 0, 2);
        E("n_flowerPurple", 2, 0, 2); E("n_grass", 6, 2, 8); E("n_grassLarge", 4, 1, 5);
        Clutter("n_mushroomRed", 3); Clutter("n_mushroomTan", 3); Clutter("n_rockSmall", 1);

        Table(Z_FOREST, R_FOREST_CLEARING, 150, 1, 4);
        E("n_tentOpen", 5, 1, 1, 1, -1); E("n_tentClosed", 3, 1, 1, 1, -1);
        E("n_campfire", 5, 1, 1, 2, A_SOFT); E("s_campfirePit", 3, 1, 1, 2, A_SOFT);
        E("n_treeDefault", 4, 1, 3); E("n_treePineTall", 4, 1, 3); E("n_treeOak", 3, 0, 2);
        E("n_stump", 4, 0, 2); E("n_log", 4, 0, 2); E("n_logStack", 3, 0, 1, 0, A_SOFT); E("s_bedroll", 3, 0, 1, 0, A_SOFT);
        E("s_chest", 2, 0, 1, 0, A_SOFT); E("s_barrel", 2, 0, 1, 0, A_SOFT); E("n_bush", 4, 0, 3, 0, A_SOFT);
        E("n_rockSmallB", 3, 0, 2); E("n_grass", 4, 1, 5); E("n_mushroomRed", 3, 0, 2);
        Clutter("s_box", 3); Clutter("s_bucket", 3); Clutter("n_mushroomTan", 2); Clutter("k_boxClosed", 1);

        Table(Z_RELEASE, R_RELEASE, 0, 0, 0);
    }

    static void BuildPieces()
    {
        // rows are listed north to south; letters are rooms; hub pieces may be the lawn hub
        // HOUSE
        Piece(Z_HOUSE, "AB/A.", 6, false);
        Piece(Z_HOUSE, "AA/BC", 6, false);
        Piece(Z_HOUSE, "AB", 3, false);
        Piece(Z_HOUSE, "ABB/A.C", 4, false);
        Piece(Z_HOUSE, "AAB/CDB", 5, false);
        Piece(Z_HOUSE, ".AB/CAD", 3, false);
        // WAREHOUSE
        Piece(Z_WAREHOUSE, "AA/AA", 5, false);
        Piece(Z_WAREHOUSE, "AAB/AAC", 5, false);
        Piece(Z_WAREHOUSE, "AA/AB", 3, false);
        Piece(Z_WAREHOUSE, "AAA/AA.", 3, false);
        // DINER
        Piece(Z_DINER, "AAB", 5, false);
        Piece(Z_DINER, "AA/BC", 5, false);
        Piece(Z_DINER, "AA/AB", 4, false);
        // LAWN
        Piece(Z_LAWN, "AA/AA", 6, true);
        Piece(Z_LAWN, "AAA/AA.", 4, true);
        Piece(Z_LAWN, "AA/A.", 4, true);
        Piece(Z_LAWN, "AAA", 3, true);
        Piece(Z_LAWN, "AA", 5, false);
        Piece(Z_LAWN, "A", 5, false);
        // FOREST
        Piece(Z_FOREST, "AA/AA", 5, false);
        Piece(Z_FOREST, "AA/A.", 5, false);
        Piece(Z_FOREST, "AAA/.A.", 3, false);
        Piece(Z_FOREST, "AA", 5, false);
        Piece(Z_FOREST, "A", 4, false);
        // HUNTER RELEASE
        Piece(Z_RELEASE, "A", 1, false);
    }
}
