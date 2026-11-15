// The map generator core (04-map-generation-v3, simplest faithful form for the browser prototype).
//
//   outer grid of 4 m cells filled with irregular zone pieces  ->  edges (walls, doors, windows, fences)
//   ->  inner grid of 0.5 m cells with placement tags  ->  rooms populated from spawn tables
//   ->  slots (chairs at tables, settings on top, items on shelves)  ->  wrong-category spawns (leaks)
//   ->  disguise quotas  ->  validation (reachability, repair, seed re-roll)  ->  emit list
//
// Deterministic: integer maths, flat arrays, its own PRNG with substreams; no UnityEngine, no physics, no time.
// The same (seed, size, GEN_VERSION) gives the same emit list on every machine. Step() does one small unit
// of work per call so the caller can spread generation over frames; results do not depend on the slicing.
//
// This file: state, stepping and the outer grid. MapGenRooms.cs: edges, rooms and inner tags.
// MapGenPlace.cs: placement. MapGenSlots.cs: slots, leaks, quotas. MapGenFinish.cs: validation and results.
public partial class MapGen
{
    // ---- stages (also the PRNG substream ids) ----
    const int ST_LAYOUT = 1, ST_EDGES = 2, ST_ROOM = 3, ST_SLOT = 4, ST_CLUTTER = 5, ST_LEAK = 6, ST_QUOTA = 7, ST_SPAWN = 8, ST_ROOMTYPE = 9;

    // ---- emit flags ----
    public const int EF_LEAK = 1, EF_COMEDIC = 2, EF_QUOTA = 4, EF_REMOVED = 8, EF_CLUTTER = 16, EF_INSIDE = 32, EF_ON_TOP = 64, EF_ADJACENT = 128, EF_UNDER = 256;

    // ---- inputs ----
    public uint seed;
    public int size;
    public int attempt;          // seed re-rolls after a failed validation (deterministic)

    // ---- outer grid ----
    public int W, H;
    public int[] cellInst, cellZone, cellRoom;
    public int nInst;
    public int[] instPiece = new int[160], instRot = new int[160], instX = new int[160], instZ = new int[160], instZone = new int[160];
    public int releaseX, releaseZ, releaseDir;   // release cell and the direction of its exit
    public int[] edgeH, edgeV, edgeHOff, edgeVOff;

    // ---- rooms ----
    public int nRooms;
    public int[] roomZone = new int[400], roomType = new int[400], roomInst = new int[400], roomOuterCells = new int[400];
    public int[] roomX0 = new int[400], roomZ0 = new int[400], roomX1 = new int[400], roomZ1 = new int[400];
    public int[] roomCellStart = new int[400], roomCellCount = new int[400];
    public int[] roomCells;      // inner cell indices grouped by room
    public int[] roomDoorStart = new int[400], roomDoorCount = new int[400];
    public int[] roomDoors = new int[3200];      // inner cell just inside each door, 8 per room
    public int[] roomLeaks = new int[400], roomSlotItems = new int[400], roomCovered = new int[400], roomEligible = new int[400];

    // ---- inner grid ----
    public int IW, IH;
    public int[] tags;
    public int[] innerRoom;
    public int[] occ;            // emit index + 1 of the object standing on the cell, 0 = free
    public int[] flatOcc;        // rugs
    public byte[] reserve;       // 1 = kept clear (front clearance, door swing)
    public byte[] walk;          // after Finish(): 0 no floor, 1 walkable, 2 blocked by a solid prop
    public byte[] reach;         // after validation: 1 = reachable from the hunter release

    // ---- emit list: one entry per generated object; the index is the object's identity ----
    public const int MAX_EMIT = 8000;
    public int nEmit;
    public int[] emitDef = new int[MAX_EMIT], emitX = new int[MAX_EMIT], emitY = new int[MAX_EMIT], emitZ = new int[MAX_EMIT];
    public int[] emitYaw = new int[MAX_EMIT], emitParent = new int[MAX_EMIT], emitRoom = new int[MAX_EMIT], emitFlags = new int[MAX_EMIT];
    public int[] emitRX = new int[MAX_EMIT], emitRZ = new int[MAX_EMIT], emitRW = new int[MAX_EMIT], emitRD = new int[MAX_EMIT];
    public int[] emitRot = new int[MAX_EMIT], emitDepth = new int[MAX_EMIT], emitSlot = new int[MAX_EMIT];

    // free slots and hiding sockets left by the slot pass
    public int nFree;
    public int[] freeHost = new int[6000], freeSlot = new int[6000];
    public int nSockets;
    public int[] socketHost = new int[2000], socketSlot = new int[2000];

    // ---- results ----
    public bool[] menu;          // per def: on this map's disguise menu (04 section 4.3.4)
    public int[] defCopies;
    public int menuCount;
    public int nSpawns;
    public int[] spawnCell = new int[64];
    public uint mapHash;
    public bool done, failed;
    public string failReason = "";

    // ---- statistics ----
    public int statProps, statScenery, statDecor, statSlotItems, statLeakPlausible, statLeakComedic, statQuotaAdded;
    public int statRepairs, statGroundCells, statSteps, statMandatoryFail, statClutter;
    public int[] statZoneCells = new int[8], statZoneRooms = new int[8];

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

    public void Begin(uint seedValue, int sizeBucket)
    {
        seed = seedValue; size = sizeBucket;
        attempt = 0; done = false; failed = false; failReason = "";
        stage = 0; cursor = 0; progressPermille = 0; statSteps = 0;
        W = GenData.SizeGrid[size]; H = W;
        IW = W * 8; IH = H * 8;
        cellInst = new int[W * H]; cellZone = new int[W * H]; cellRoom = new int[W * H];
        edgeH = new int[W * (H + 1)]; edgeHOff = new int[W * (H + 1)];
        edgeV = new int[(W + 1) * H]; edgeVOff = new int[(W + 1) * H];
        tags = new int[IW * IH]; innerRoom = new int[IW * IH]; occ = new int[IW * IH]; flatOcc = new int[IW * IH];
        reserve = new byte[IW * IH]; walk = new byte[IW * IH]; reach = new byte[IW * IH];
        roomCells = new int[IW * IH];
        queue = new int[IW * IH]; dist = new int[IW * IH];
        menu = new bool[GenData.nDefs]; defCopies = new int[GenData.nDefs];
    }

    public void RunAll(uint seedValue, int sizeBucket)
    {
        Begin(seedValue, sizeBucket);
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
                statRepairs = 0; statMandatoryFail = 0; statClutter = 0; statLeakComedic = 0; statLeakPlausible = 0; statQuotaAdded = 0;
                if (!Layout()) { Retry("layout"); break; }
                stage = 1; progressPermille = 80;
                break;
            case 1:
                BuildEdges();
                BuildRooms();
                BuildTags();
                stage = 2; cursor = 0; progressPermille = 150;
                break;
            case 2:
                if (cursor < nRooms)
                {
                    PopulateRoom(cursor);
                    cursor++;
                    progressPermille = 150 + 600 * cursor / nRooms;
                }
                else stage = 3;
                break;
            case 3:
                PlaceLeaks();
                stage = 4; progressPermille = 800;
                break;
            case 4:
                Quotas();
                stage = 5; progressPermille = 870;
                break;
            case 5:
                if (!Validate()) { Retry("unreachable rooms"); break; }
                stage = 6; progressPermille = 940;
                break;
            case 6:
                Finish();
                progressPermille = 1000;
                done = true;
                break;
        }
        return done || failed;
    }

    void Retry(string why)
    {
        attempt++;
        if (attempt >= 12)
        {
            failed = true;
            failReason = "no valid map after 12 attempts (" + why + ")";
            return;
        }
        stage = 0; cursor = 0; progressPermille = 0;
    }

    GenRng Rng(int st, int a, int b)
    {
        return GenRng.Sub(seed, (uint)GenData.GEN_VERSION, st * 16 + attempt, a, b + size * 1000);
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

    // =====================================================================================================
    // Outer grid: zone pieces from a flow template (04 sections 4.1.2, 4.1.3)
    // =====================================================================================================
    int[] pcx = new int[36], pcz = new int[36];   // cells of the piece being tried
    char[] pcl = new char[36];
    int pcn;
    bool[] dead;

    // rotated cells of piece p: count in pcn, coordinates relative to the piece's box
    void PieceCells(int p, int rot)
    {
        int w = GenData.pieceW[p], h = GenData.pieceH[p];
        pcn = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char c = GenData.PieceCell(p, x, y);
                if (c == '.') continue;
                int rx, rz;
                if (rot == 0) { rx = x; rz = y; }
                else if (rot == 1) { rx = h - 1 - y; rz = x; }
                else if (rot == 2) { rx = w - 1 - x; rz = h - 1 - y; }
                else { rx = y; rz = w - 1 - x; }
                pcx[pcn] = rx; pcz[pcn] = rz; pcl[pcn] = c; pcn++;
            }
    }

    public bool InGrid(int x, int z) { return x >= 0 && z >= 0 && x < W && z < H; }

    public int ZoneAt(int x, int z) { return InGrid(x, z) ? cellZone[z * W + x] : GenData.Z_NONE; }

    // may the current rotated piece (pcx/pcz) of this zone go at origin (ox, oz)?
    bool Fits(int zone, int ox, int oz, bool isHub)
    {
        bool building = GenData.IsBuilding(zone);
        for (int i = 0; i < pcn; i++)
        {
            int x = ox + pcx[i], z = oz + pcz[i];
            if (!InGrid(x, z) || cellZone[z * W + x] != GenData.Z_NONE) return false;
            for (int d = 0; d < 4; d++)
            {
                int nz = ZoneAt(x + DX[d], z + DZ[d]);
                if (nz == GenData.Z_RELEASE && !isHub) return false;
                // Forest never touches a building edge directly (04 section 4.1.1)
                if (zone == GenData.Z_FOREST && GenData.IsBuilding(nz)) return false;
                if (building && nz == GenData.Z_FOREST) return false;
            }
        }
        return true;
    }

    int StampPiece(int piece, int rot, int ox, int oz)
    {
        int inst = nInst++;
        instPiece[inst] = piece; instRot[inst] = rot; instX[inst] = ox; instZ[inst] = oz; instZone[inst] = GenData.pieceZone[piece];
        for (int i = 0; i < pcn; i++)
        {
            int c = (oz + pcz[i]) * W + ox + pcx[i];
            cellInst[c] = inst; cellZone[c] = instZone[inst];
            cellRoom[c] = pcl[i] - 'A';   // local room letter for now; made global in BuildRooms
        }
        return inst;
    }

    // tree connections: outer cell + direction of the edge that must be walkable
    int nConn;
    int[] connCell = new int[160], connDir = new int[160];

    int[] nodeZone = new int[64];
    int nNodes;

    void Node(int zone) { nodeZone[nNodes++] = zone; }

    int FindPiece(int zone, string rows)
    {
        for (int p = 0; p < GenData.nPieces; p++)
            if (GenData.pieceZone[p] == zone && GenData.pieceRows[p] == rows) return p;
        return -1;
    }

    bool Layout()
    {
        GenRng rng = Rng(ST_LAYOUT, 0, 0);
        for (int i = 0; i < W * H; i++) { cellInst[i] = -1; cellZone[i] = GenData.Z_NONE; cellRoom[i] = -1; }
        nInst = 0; nConn = 0;

        // hunter release on a boundary side chosen by the seed
        int side = rng.Range(4);
        int mid = W / 2 + rng.Between(-1, 1);
        if (side == 0) { releaseX = mid; releaseZ = H - 1; releaseDir = 2; }
        else if (side == 1) { releaseX = W - 1; releaseZ = mid; releaseDir = 3; }
        else if (side == 2) { releaseX = mid; releaseZ = 0; releaseDir = 0; }
        else { releaseX = 0; releaseZ = mid; releaseDir = 1; }
        int relPiece = FindPiece(GenData.Z_RELEASE, "A");
        PieceCells(relPiece, 0);
        StampPiece(relPiece, 0, releaseX, releaseZ);
        int hx = releaseX + DX[releaseDir], hz = releaseZ + DZ[releaseDir];

        // lawn hub: must cover the cell in front of the release, as central as possible
        int bestScore = -1000000, bp = -1, br = 0, bx = 0, bz = 0;
        for (int p = 0; p < GenData.nPieces; p++)
        {
            if (GenData.pieceZone[p] != GenData.Z_LAWN || !GenData.pieceHub[p]) continue;
            for (int rot = 0; rot < 4; rot++)
            {
                PieceCells(p, rot);
                for (int e = 0; e < pcn; e++)
                {
                    int ox = hx - pcx[e], oz = hz - pcz[e];
                    if (!Fits(GenData.Z_LAWN, ox, oz, true)) continue;
                    int sx = 0, sz = 0;
                    for (int i = 0; i < pcn; i++) { sx += (ox + pcx[i]) * 2 + 1; sz += (oz + pcz[i]) * 2 + 1; }
                    int ddx = sx / pcn - W, ddz = sz / pcn - H;
                    int score = -(ddx * ddx + ddz * ddz) * 4 + GenData.pieceWeight[p] + rng.Range(6);
                    if (score > bestScore) { bestScore = score; bp = p; br = rot; bx = ox; bz = oz; }
                }
            }
        }
        if (bp < 0) return false;
        PieceCells(bp, br);
        StampPiece(bp, br, bx, bz);
        connCell[nConn] = releaseZ * W + releaseX; connDir[nConn] = releaseDir; nConn++;

        // flow template for the size bucket (04 section 4.1.2): buildings around the hub, forest, lawn pockets
        nNodes = 0;
        if (size == 0)
        {
            Node(GenData.Z_HOUSE);
            if (rng.Chance(650)) Node(rng.Chance(500) ? GenData.Z_DINER : GenData.Z_WAREHOUSE);
            Node(GenData.Z_FOREST);
        }
        else if (size == 1)
        {
            bool wh = rng.Chance(700), di = rng.Chance(700);
            if (!wh && !di) { if (rng.Chance(500)) wh = true; else di = true; }
            if (wh) Node(GenData.Z_WAREHOUSE);
            Node(GenData.Z_HOUSE);
            if (rng.Chance(500)) Node(GenData.Z_LAWN);
            if (di) Node(GenData.Z_DINER);
            if (rng.Chance(500)) Node(GenData.Z_HOUSE);
            Node(GenData.Z_FOREST);
            if (rng.Chance(500)) Node(GenData.Z_FOREST);
        }
        else if (size == 2)
        {
            Node(GenData.Z_WAREHOUSE); Node(GenData.Z_HOUSE); Node(GenData.Z_LAWN); Node(GenData.Z_DINER); Node(GenData.Z_HOUSE);
            if (rng.Chance(500)) Node(GenData.Z_LAWN);
            if (rng.Chance(500)) Node(GenData.Z_HOUSE);
            Node(GenData.Z_FOREST); Node(GenData.Z_FOREST);
            if (rng.Chance(500)) Node(GenData.Z_FOREST);
        }
        else
        {
            Node(GenData.Z_WAREHOUSE); Node(GenData.Z_HOUSE); Node(GenData.Z_LAWN); Node(GenData.Z_DINER); Node(GenData.Z_HOUSE);
            Node(GenData.Z_LAWN);
            if (rng.Chance(500)) Node(GenData.Z_WAREHOUSE);
            Node(GenData.Z_HOUSE);
            if (rng.Chance(500)) Node(GenData.Z_LAWN);
            if (rng.Chance(500)) Node(GenData.Z_DINER);
            if (rng.Chance(500)) Node(GenData.Z_HOUSE);
            Node(GenData.Z_FOREST); Node(GenData.Z_FOREST); Node(GenData.Z_FOREST);
        }
        bool anyBuilding = false;
        for (int n = 0; n < nNodes; n++)
        {
            bool ok = PlaceNode(nodeZone[n], ref rng);
            if (ok && GenData.IsBuilding(nodeZone[n])) anyBuilding = true;
        }
        if (!anyBuilding) return false;

        FillRemaining(ref rng);
        return true;
    }

    // attach one piece of this zone to the frontier of a lawn (or forest, for forest) piece
    bool PlaceNode(int zone, ref GenRng rng)
    {
        // frontier: (occupied parent cell, direction to an empty cell)
        int nf = 0;
        for (int c = 0; c < W * H; c++)
        {
            int z = cellZone[c];
            bool parentOk = z == GenData.Z_LAWN || (zone == GenData.Z_FOREST && z == GenData.Z_FOREST);
            if (!parentOk) continue;
            for (int d = 0; d < 4; d++)
            {
                int x = c % W + DX[d], zz = c / W + DZ[d];
                if (InGrid(x, zz) && cellZone[zz * W + x] == GenData.Z_NONE && nf < 1024) cand[nf++] = c * 4 + d;
            }
        }
        Shuffle(cand, nf, ref rng);
        // pieces of the zone in weighted random order
        int[] order = new int[32];
        int[] wts = new int[32];
        int np = 0;
        for (int p = 0; p < GenData.nPieces; p++)
            if (GenData.pieceZone[p] == zone) { order[np] = p; wts[np] = GenData.pieceWeight[p]; np++; }
        for (int i = 0; i < np; i++)
        {
            int total = 0;
            for (int k = i; k < np; k++) total += wts[k];
            int r = rng.Range(total), pick = i;
            for (int k = i; k < np; k++) { if (r < wts[k]) { pick = k; break; } r -= wts[k]; }
            int t = order[i]; order[i] = order[pick]; order[pick] = t;
            t = wts[i]; wts[i] = wts[pick]; wts[pick] = t;
        }
        int bestScore = -1000000, bp = -1, br = 0, bx = 0, bz = 0, bconn = 0, found = 0;
        int rot0 = rng.Range(4);
        for (int pi = 0; pi < np && found < 8; pi++)
        {
            int p = order[pi];
            for (int ri = 0; ri < 4 && found < 8; ri++)
            {
                int rot = (rot0 + ri) & 3;
                PieceCells(p, rot);
                for (int f = 0; f < nf && found < 8; f++)
                {
                    int pc = cand[f] / 4, d = cand[f] % 4;
                    int tx = pc % W + DX[d], tz = pc / W + DZ[d];
                    for (int e = 0; e < pcn && found < 8; e++)
                    {
                        int ox = tx - pcx[e], oz = tz - pcz[e];
                        if (!Fits(zone, ox, oz, false)) continue;
                        // score: compact (touching occupied cells), toward the middle, bigger pieces first
                        int touch = 0, sx = 0, sz = 0;
                        for (int i = 0; i < pcn; i++)
                        {
                            int x = ox + pcx[i], z = oz + pcz[i];
                            sx += x * 2 + 1; sz += z * 2 + 1;
                            for (int dd = 0; dd < 4; dd++) if (ZoneAt(x + DX[dd], z + DZ[dd]) != GenData.Z_NONE) touch++;
                        }
                        int ddx = sx / pcn - W, ddz = sz / pcn - H;
                        int score = touch * 6 - (ddx * ddx + ddz * ddz) / 2 + pcn * 3 + rng.Range(8);
                        found++;
                        if (score > bestScore) { bestScore = score; bp = p; br = rot; bx = ox; bz = oz; bconn = pc * 4 + d; }
                    }
                }
            }
        }
        if (bp < 0) return false;
        PieceCells(bp, br);
        StampPiece(bp, br, bx, bz);
        connCell[nConn] = bconn / 4; connDir[nConn] = bconn % 4; nConn++;
        return true;
    }

    // greedy lawn and forest filler up to the size's ground-cell target, then close enclosed holes
    void FillRemaining(ref GenRng rng)
    {
        int target = GenData.SizeGroundCells[size];
        int count = 0;
        for (int c = 0; c < W * H; c++) if (cellZone[c] != GenData.Z_NONE && cellZone[c] != GenData.Z_RELEASE) count++;
        dead = new bool[W * H];
        string[] shapes = { "AA/AA", "AA", "A" };
        int[] shapeCells = { 4, 2, 1 };
        int guard = 400;
        while (count < target && guard-- > 0)
        {
            int best = -1, bestScore = -1;
            for (int c = 0; c < W * H; c++)
            {
                if (cellZone[c] != GenData.Z_NONE || dead[c]) continue;
                int x = c % W, z = c / W;
                int n4 = 0, n8 = 0;
                bool nearRelease = false;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int zn = ZoneAt(x + dx, z + dz);
                        if (zn == GenData.Z_NONE) continue;
                        if (zn == GenData.Z_RELEASE) { if (dx == 0 || dz == 0) nearRelease = true; continue; }
                        n8++;
                        if (dx == 0 || dz == 0) n4++;
                    }
                if (n4 == 0 || nearRelease) continue;
                int score = n4 * 10 + n8 * 3 + rng.Range(6);
                if (score > bestScore) { bestScore = score; best = c; }
            }
            if (best < 0) break;
            int bx = best % W, bz = best / W;
            bool nearBuilding = false, nearForest = false;
            for (int d = 0; d < 4; d++)
            {
                int zn = ZoneAt(bx + DX[d], bz + DZ[d]);
                if (GenData.IsBuilding(zn)) nearBuilding = true;
                if (zn == GenData.Z_FOREST) nearForest = true;
            }
            int zone = nearBuilding ? GenData.Z_LAWN : (rng.Chance(nearForest ? 700 : 450) ? GenData.Z_FOREST : GenData.Z_LAWN);
            // biggest filler piece (2x2, 2x1, 1x1) that fits around the cell and within the budget
            int placed = 0;
            for (int pass = 0; pass < 2 && placed == 0; pass++)
            {
                int zn = pass == 0 ? zone : GenData.Z_LAWN;   // the forest rule can block a forest cell: fall back to lawn
                if (pass == 1 && zone == GenData.Z_LAWN) break;
                for (int si = 0; si < 3 && placed == 0; si++)
                {
                    if (shapeCells[si] > target - count) continue;
                    int p = FindPiece(zn, shapes[si]);
                    if (p < 0) continue;
                    int r0 = rng.Range(4);
                    for (int ri = 0; ri < 4 && placed == 0; ri++)
                    {
                        PieceCells(p, (r0 + ri) & 3);
                        int e0 = rng.Range(pcn);
                        for (int ei = 0; ei < pcn && placed == 0; ei++)
                        {
                            int e = (e0 + ei) % pcn;
                            int ox = bx - pcx[e], oz = bz - pcz[e];
                            if (!Fits(zn, ox, oz, false)) continue;
                            StampPiece(p, (r0 + ri) & 3, ox, oz);
                            placed = pcn;
                        }
                    }
                }
            }
            if (placed == 0) dead[best] = true;
            count += placed;
        }

        // holes: empty cells that the outside cannot reach become lawn
        int qh = 0, qt = 0;
        int[] mark = new int[W * H];
        for (int c = 0; c < W * H; c++)
        {
            int x = c % W, z = c / W;
            if ((x == 0 || z == 0 || x == W - 1 || z == H - 1) && cellZone[c] == GenData.Z_NONE) { mark[c] = 1; queue[qt++] = c; }
        }
        while (qh < qt)
        {
            int c = queue[qh++];
            for (int d = 0; d < 4; d++)
            {
                int x = c % W + DX[d], z = c / W + DZ[d];
                if (!InGrid(x, z)) continue;
                int n = z * W + x;
                if (mark[n] == 0 && cellZone[n] == GenData.Z_NONE) { mark[n] = 1; queue[qt++] = n; }
            }
        }
        int lawn1 = FindPiece(GenData.Z_LAWN, "A");
        for (int c = 0; c < W * H; c++)
        {
            if (cellZone[c] != GenData.Z_NONE || mark[c] != 0) continue;
            PieceCells(lawn1, 0);
            StampPiece(lawn1, 0, c % W, c / W);
        }
        statGroundCells = 0;
        for (int c = 0; c < W * H; c++) if (cellZone[c] != GenData.Z_NONE && cellZone[c] != GenData.Z_RELEASE) statGroundCells++;
    }
}
