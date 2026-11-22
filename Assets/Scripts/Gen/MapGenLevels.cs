// MapGen, part 1c (generator 18, Cor: multi-storey buildings and basements; design log Q10, Q11, C8).
//
// Some buildings get an upper floor, some a basement (a house, a warehouse or a diner, C8), a few both. Each level is
// laid out as a building of its own beside the map: the same footprint and the same room letters as the ground floor
// (its walls stand on the walls below), its own room types (bedrooms upstairs, a flat over a diner or a shop, a loft
// in a cabin or a barn, store rooms in a basement), the ground floor's interior doors, no outside doors. Everything
// after the layout (edges, rooms, tags, props, validation) then works on it unchanged; the builder lifts or sinks it
// into place over its footprint (instLevel, instDX, instDZ).
//
// A stair joins the two levels: a half-turn stair in one outer cell, 2 m wide against one of the cell's sides. Its
// first flight climbs on the room's side from the cell's entry row to a landing at the far end, the second comes back
// along the wall to the entry row of the level above, where the floor has a hole over the stair. In the inner grid both
// levels lose the stair's floor (rows 1 to 7: no floor, no room) and the two entry rows are joined by a link
// (link[]): the reachability flood and every room's exits step through it. Both flights end facing the cell's foot
// edge, so on both levels that edge is open floor into the next cell of the same room (generator 31, Cor: the stairs
// leading straight into a wall).
public partial class MapGen
{
    public int siteW, siteH;                    // the map itself; W x H also holds the levels laid out beside it
    public int[] instLevel = new int[160];      // 0 on the ground, 1 an upper floor, -1 a basement
    public int[] instBase = new int[160];       // the ground floor's instance (itself on the ground)
    public int[] instDX = new int[160], instDZ = new int[160];   // a level's cell = the ground floor's cell + this
    public int nStairs;
    public int[] stairLow = new int[16], stairHigh = new int[16];   // the stair's outer cell on the lower and the upper level
    public int[] stairRun = new int[16], stairSide = new int[16];   // the way up the first flight; the side of its wall
    int[] link;                                 // per inner cell: the entry cell at the stair's other end, -1
    int nLvl;
    int[] lvlPlot = new int[16], lvlUp = new int[16], lvlInst = new int[16];

    public const int STAIR_W = 4, STAIR_ROWS = 8;   // inner cells across (two flights of 1 m) and along (the whole cell)

    // per mille: an upper floor, a basement, by the building's zone
    int LevelChance(int zone, bool up)
    {
        if (up)
            switch (zone)
            {
                case GenData.Z_HOUSE: return mapTheme == GenData.THEME_MEDIEVAL ? 450 : 350;
                case GenData.Z_DINER: return 250;
                case GenData.Z_SHOP: return 300;
                case GenData.Z_GARAGE: return 150;
                case GenData.Z_WAREHOUSE: return 150;
                case GenData.Z_CABIN: return 250;
                case GenData.Z_BARN: return 200;
                default: return 0;
            }
        // (generator 26, Cor: no basements turned up in a playtest; they were on a quarter of the M maps)
        switch (zone)
        {
            case GenData.Z_HOUSE: return 420;
            case GenData.Z_WAREHOUSE: return 450;
            case GenData.Z_DINER: return 400;
            default: return 0;
        }
    }

    // the site's doorway on edge d of cell c, either side of it: its kind, -1 none
    int SdAt(int c, int d)
    {
        int n = (c / W + DZ[d]) * W + c % W + DX[d], o = (d + 2) & 3;
        for (int k = 0; k < nSd; k++)
            if ((sdCell[k] == c && sdDir[k] == d) || (sdCell[k] == n && sdDir[k] == o)) return sdKind[k];
        return -1;
    }

    // Before the levels: may building bi's ground floor hold a stair (PlaceStairs, StairFits)? A cell with a cell of
    // its own room at the foot, and a wall to stand against with no doorway (but one the two rooms have twice), nor
    // an outside door on the far edge.
    bool StairPossible(int bi)
    {
        for (int c = 0; c < W * H; c++)
        {
            if (cellInst[c] != bi) continue;
            int cx = c % W, cz = c / W;
            for (int run = 0; run < 4; run++)
            {
                int foot = (run + 2) & 3, fx = cx + DX[foot], fz = cz + DZ[foot];
                if (!InGrid(fx, fz) || cellInst[fz * W + fx] != bi || cellRoom[fz * W + fx] != cellRoom[c]) continue;
                int far = SdAt(c, run);
                if (far >= 0 && far != SD_DOOR) continue;
                for (int t = 0; t < 2; t++)
                {
                    int side = (run + 1 + 2 * t) & 3, sd = SdAt(c, side);
                    if (sd < 0) return true;
                    if (sd != SD_DOOR) continue;
                    // the two rooms have another doorway
                    int nx = cx + DX[side], nz = cz + DZ[side];
                    if (!InGrid(nx, nz)) continue;
                    int ra = cellRoom[c], rb = cellRoom[nz * W + nx], ways = 0;
                    for (int e = 0; e < W * H; e++)
                    {
                        if (cellInst[e] != bi || cellRoom[e] != ra) continue;
                        for (int k = 0; k < 4; k++)
                        {
                            int ex = e % W + DX[k], ez = e / W + DZ[k];
                            if (InGrid(ex, ez) && cellInst[ez * W + ex] == bi && cellRoom[ez * W + ex] == rb && SdAt(e, k) == SD_DOOR) ways++;
                        }
                    }
                    if (ways >= 2) return true;
                }
            }
        }
        return false;
    }

    // the room types of a level: the first ones in order, then drawn from the rest
    int LevelRoomType(int zone, bool up, int k, ref GenRng rng)
    {
        if (!up)
        {
            if (zone == GenData.Z_WAREHOUSE) { int[] w = { GenData.R_STORAGE, GenData.R_PARTS, GenData.R_STORAGE, GenData.R_BOXROOM }; return w[k < 2 ? k : 2 + rng.Range(2)]; }
            if (zone == GenData.Z_DINER) { int[] d = { GenData.R_DINER_STORE, GenData.R_PANTRY, GenData.R_STORAGE, GenData.R_BOXROOM }; return d[k < 2 ? k : 2 + rng.Range(2)]; }
            int[] h = { GenData.R_STORAGE, GenData.R_FARM_WORK, GenData.R_BOXROOM, GenData.R_PANTRY, GenData.R_STOCKROOM };   // a cellar: stores, a workshop, the box room
            return h[k < 2 ? k : 2 + rng.Range(3)];
        }
        switch (zone)
        {
            case GenData.Z_HOUSE:
            {
                int[] h = { GenData.R_BEDROOM, GenData.R_BATH, GenData.R_KIDS, GenData.R_PLAYROOM, GenData.R_TEEN, GenData.R_OFFICE, GenData.R_BOXROOM, GenData.R_BEDROOM };
                return h[k < 2 ? k : 2 + rng.Range(6)];
            }
            case GenData.Z_DINER: case GenData.Z_SHOP: case GenData.Z_GARAGE:
            {
                // a flat over the business
                int[] f = { GenData.R_LIVING, GenData.R_BEDROOM, GenData.R_BATH, GenData.R_KITCHEN, GenData.R_OFFICE, GenData.R_BEDROOM };
                return f[k < 3 ? k : 3 + rng.Range(3)];
            }
            case GenData.Z_WAREHOUSE: { int[] w = { GenData.R_WH_OFFICE, GenData.R_STORAGE, GenData.R_STORAGE, GenData.R_BOXROOM }; return w[k < 2 ? k : 2 + rng.Range(2)]; }
            case GenData.Z_CABIN: { int[] c = { GenData.R_BUNKROOM, GenData.R_BOXROOM, GenData.R_BUNKROOM }; return c[k < 1 ? k : 1 + rng.Range(2)]; }
            case GenData.Z_BARN: { int[] b = { GenData.R_FEED_STORE, GenData.R_BARN, GenData.R_FEED_STORE }; return b[k < 1 ? k : 1 + rng.Range(2)]; }
        }
        return GenData.R_BOXROOM;
    }

    // After PlaceSite: draw the levels and lay them out beside the map, one column of cells apart.
    void AddLevels()
    {
        siteW = W; siteH = H; nLvl = 0; nStairs = 0;
        for (int i = 0; i < 160; i++) { instLevel[i] = 0; instBase[i] = i; instDX[i] = 0; instDZ[i] = 0; }
        GenRng rng = Rng(ST_LAYOUT, 31, 0);
        int cap = GenData.SizeLevelsMax[size];
        int[] order = new int[16];
        for (int k = 0; k < nPlots; k++) order[k] = k;
        Shuffle(order, nPlots, ref rng);
        for (int q = 0; q < nPlots; q++)
        {
            int k = order[q], bi = plotInstB[k];
            if (bi < 0) continue;
            int cells = 0;
            for (int c = 0; c < W * H; c++) if (cellInst[c] == bi) cells++;
            if (cells < 2) continue;
            // a stair needs a room of two cells in a row, the stair in one and open floor at its foot in the other
            // (Cor: the stairs leading straight into a wall), and the ground floor a place for it: see StairPossible
            if (!StairPossible(bi)) continue;
            int zone = plotZone[k];
            bool up = nLvl < cap && rng.Chance(LevelChance(zone, true));
            // Cor: varied, not every building with both: a building with an upper floor gets a basement too only now and then
            bool down = nLvl + (up ? 1 : 0) < cap && rng.Chance(LevelChance(zone, false) * (up ? 3 : 10) / 10);
            if (up) { lvlPlot[nLvl] = k; lvlUp[nLvl] = 1; nLvl++; }
            if (down) { lvlPlot[nLvl] = k; lvlUp[nLvl] = 0; nLvl++; }
        }
        if (nLvl == 0) return;
        // where each level goes: in a row beside the map
        int[] bx0 = new int[16], bz0 = new int[16];
        int cursor = siteW + 1, newH = H;
        for (int j = 0; j < nLvl; j++)
        {
            int bi = plotInstB[lvlPlot[j]], x0 = W, x1 = -1, z0 = H, z1 = -1;
            for (int c = 0; c < W * H; c++)
            {
                if (cellInst[c] != bi) continue;
                int x = c % W, z = c / W;
                if (x < x0) x0 = x; if (x > x1) x1 = x; if (z < z0) z0 = z; if (z > z1) z1 = z;
            }
            bx0[j] = cursor - x0; bz0[j] = -z0;
            cursor += x1 - x0 + 2;
            if (z1 - z0 + 1 > newH) newH = z1 - z0 + 1;
        }
        // a bigger grid, the map copied over
        int oldW = W, oldH = H;
        int[] oInst = cellInst, oZone = cellZone, oRoom = cellRoom, oPlot = cellPlot;
        Alloc(cursor - 1, newH);
        for (int c = 0; c < W * H; c++) { cellInst[c] = -1; cellZone[c] = GenData.Z_NONE; cellRoom[c] = -1; cellPlot[c] = -1; }
        for (int c = 0; c < oldW * oldH; c++)
        {
            int n = (c / oldW) * W + c % oldW;
            cellInst[n] = oInst[c]; cellZone[n] = oZone[c]; cellRoom[n] = oRoom[c]; cellPlot[n] = oPlot[c];
        }
        for (int s = 0; s < nSd; s++) sdCell[s] = (sdCell[s] / oldW) * W + sdCell[s] % oldW;
        int nSd0 = nSd;
        for (int j = 0; j < nLvl; j++)
        {
            int k = lvlPlot[j], bi = plotInstB[k], zone = plotZone[k];
            bool up = lvlUp[j] != 0;
            int li = NewInst(zone);
            lvlInst[j] = li;
            instLevel[li] = up ? 1 : -1; instBase[li] = bi; instDX[li] = bx0[j]; instDZ[li] = bz0[j];
            bool[] seen = new bool[26];
            int[] letters = new int[26];
            int nl = 0;
            for (int c = 0; c < oldW * oldH; c++)
            {
                if (oInst[c] != bi) continue;
                int x = c % oldW + bx0[j], z = c / oldW + bz0[j], n = z * W + x, l = oRoom[c];
                cellInst[n] = li; cellZone[n] = zone; cellRoom[n] = l; cellPlot[n] = k;
                if (l >= 0 && l < 26 && !seen[l]) { seen[l] = true; letters[nl++] = l; }
            }
            Shuffle(letters, nl, ref rng);
            for (int q = 0; q < nl; q++)
            {
                siteType[li * 26 + letters[q]] = LevelRoomType(zone, up, q, ref rng);
                sitePlace[li * 26 + letters[q]] = PL_BUILDING;
            }
            // the room sizes: a bathroom takes the smallest room, a room of three cells or more is a playroom or a living
            // room, not a bedroom or a bathroom (review 7: big rooms upstairs held three to five props)
            int[] size = new int[26];
            for (int c = 0; c < oldW * oldH; c++) if (oInst[c] == bi && oRoom[c] >= 0 && oRoom[c] < 26) size[oRoom[c]]++;
            if (up && nl >= 2)
            {
                int small = letters[0], bath = -1;
                for (int q = 0; q < nl; q++)
                {
                    if (size[letters[q]] < size[small]) small = letters[q];
                    if (siteType[li * 26 + letters[q]] == GenData.R_BATH) bath = letters[q];
                }
                if (bath >= 0 && bath != small) { siteType[li * 26 + bath] = siteType[li * 26 + small]; siteType[li * 26 + small] = GenData.R_BATH; }
                for (int q = 0; q < nl; q++)
                {
                    int l = letters[q], t = siteType[li * 26 + l];
                    if (size[l] < 3 || l == small) continue;
                    if (t == GenData.R_BEDROOM || t == GenData.R_BATH || t == GenData.R_OFFICE || t == GenData.R_BOXROOM)
                        siteType[li * 26 + l] = zone == GenData.Z_HOUSE ? (rng.Chance(500) ? GenData.R_PLAYROOM : GenData.R_LIVING) : GenData.R_LIVING;
                }
            }
            // the ground floor's interior doors
            for (int s = 0; s < nSd0; s++)
            {
                if (sdKind[s] != SD_DOOR || cellInst[sdCell[s]] != bi || nSd >= sdCell.Length) continue;
                int x = sdCell[s] % W + bx0[j], z = sdCell[s] / W + bz0[j];
                sdCell[nSd] = z * W + x; sdDir[nSd] = sdDir[s]; sdKind[nSd] = SD_DOOR; nSd++;
            }
        }
    }

    // ---- stairs ----------------------------------------------------------------------------------------------------

    // inner cell of stair-local (a across from the wall, b along from the entry row) in outer cell c
    int StairInner(int c, int run, int side, int a, int b)
    {
        int lx = 0, lz = 0;
        if (run == 0) lz = b; else if (run == 2) lz = 7 - b; else if (run == 1) lx = b; else lx = 7 - b;
        if (side == 1) lx = 7 - a; else if (side == 3) lx = a; else if (side == 0) lz = 7 - a; else lz = a;
        return ((c / W) * 8 + lz) * IW + (c % W) * 8 + lx;
    }

    // may a stair stand in outer cell c this way round? Open floor of the same room at its foot, and no doorway of the
    // cell, nor its keep-clear floor, reaching the flights or the landing. An inside doorway in the way may move: one
    // on the wall the stair stands against to the same wall of the foot cell (the same two rooms), one on the far edge
    // along that edge to the room's side. moves counts them; apply makes them (once the stair is chosen). -1: no fit.
    int StairFits(int c, int run, int side, bool apply)
    {
        if (!GenData.IsBuilding(cellZone[c])) return -1;
        int cx = c % W, cz = c / W;
        // the foot: both flights end on the entry row facing the cell's edge at b = 0, so that edge must be open floor
        // of the same room (Cor: the stairs leading straight into a wall). A wall, a window or a doorway beside the
        // flights left 0.5 m before the wall; another stair's cell beyond would put its flights there.
        int foot = (run + 2) & 3;
        if (EdgeType(cx, cz, foot) != GenData.E_NONE) return -1;
        int fx = cx + DX[foot], fz = cz + DZ[foot];
        if (!InGrid(fx, fz) || cellInst[fz * W + fx] != cellInst[c] || IsStairCell(fz * W + fx)) return -1;
        int moves = 0;
        for (int d = 0; d < 4; d++)
        {
            if (EdgeType(cx, cz, d) != GenData.E_DOOR || !DoorHitsStair(run, side, d, EdgeOff(cx, cz, d))) continue;
            int nx = cx + DX[d], nz = cz + DZ[d];
            // an outside door stays where the site put it; nothing moves next to another stair
            if (!InGrid(nx, nz) || cellInst[nz * W + nx] != cellInst[c] || IsStairCell(nz * W + nx)) return -1;
            if (d == run)
            {
                int to = -1;
                for (int off = 1; off <= 5; off += 2) if (!DoorHitsStair(run, side, d, off)) to = off;
                if (to < 0) return -1;
                if (apply) SetEdge(cx, cz, d, GenData.E_DOOR, to);
            }
            else if (d == side)
            {
                if (OtherDoor(c, d))
                {
                    // the two rooms have another doorway: this one becomes wall
                    if (apply) SetEdge(cx, cz, d, GenData.E_WALL, 0);
                }
                else
                {
                    int ox = fx + DX[d], oz = fz + DZ[d];
                    if (InGrid(ox, oz) && cellInst[oz * W + ox] == cellInst[c] && cellRoom[oz * W + ox] == cellRoom[nz * W + nx]
                        && !IsStairCell(oz * W + ox) && EdgeType(fx, fz, d) == GenData.E_WALL)
                    {
                        if (apply) { SetEdge(fx, fz, d, GenData.E_DOOR, EdgeOff(cx, cz, d)); SetEdge(cx, cz, d, GenData.E_WALL, 0); }
                    }
                    else
                    {
                        // on an upper floor or in a basement: walled up, and JoinLevel then gives the room across a
                        // doorway somewhere else. The ground floor keeps its doors (the house tests, T6)
                        if (instLevel[cellInst[c]] == 0) return -1;
                        if (apply) SetEdge(cx, cz, d, GenData.E_WALL, 0);
                        moves++;
                    }
                }
            }
            else return -1;
            moves++;
        }
        return moves;
    }

    // is there another doorway between the room of cell c and the room across its edge d?
    bool OtherDoor(int c, int d)
    {
        int ra = cellRoom[c], rb = cellRoom[(c / W + DZ[d]) * W + c % W + DX[d]];
        for (int e = 0; e < W * H; e++)
        {
            if (cellRoom[e] != ra || cellInst[e] != cellInst[c]) continue;
            for (int k = 0; k < 4; k++)
            {
                if (e == c && k == d) continue;
                int nx = e % W + DX[k], nz = e / W + DZ[k];
                if (InGrid(nx, nz) && cellRoom[nz * W + nx] == rb && cellInst[nz * W + nx] == cellInst[c] && EdgeType(e % W, e / W, k) == GenData.E_DOOR) return true;
            }
        }
        return false;
    }

    bool IsStairCell(int c)
    {
        for (int s = 0; s < nStairs; s++) if (stairLow[s] == c || stairHigh[s] == c) return true;
        return false;
    }

    // does a doorway at offset off on edge d of a stair's cell, or its keep-clear floor, reach the flights or the landing?
    bool DoorHitsStair(int run, int side, int d, int off)
    {
        for (int al = off - 1; al <= off + GenData.DOOR_CELLS; al++)
        {
            if (al < 0 || al > 7) continue;
            for (int depth = 0; depth < 2; depth++)
            {
                int lx, lz;
                if (d == 0) { lx = al; lz = 7 - depth; }
                else if (d == 2) { lx = al; lz = depth; }
                else if (d == 1) { lx = 7 - depth; lz = al; }
                else { lx = depth; lz = al; }
                // stair-local of (lx, lz)
                int b = run == 0 ? lz : (run == 2 ? 7 - lz : (run == 1 ? lx : 7 - lx));
                int a = side == 1 ? 7 - lx : (side == 3 ? lx : (side == 0 ? 7 - lz : lz));
                if (a < STAIR_W && b >= 1) return true;
            }
        }
        return false;
    }

    int StairScore(int c, int side, int run)
    {
        int r = cellRoom[c], rt = roomType[r], s = 0;
        if (rt == GenData.R_HALL || rt == GenData.R_LIVING || rt == GenData.R_STORAGE) s += 30;
        if (roomOuterCells[r] >= 2) s += 20;
        if (rt == GenData.R_BATH || rt == GenData.R_PANTRY) s -= 40;
        if (rt == GenData.R_KITCHEN || rt == GenData.R_DINER_KITCHEN) s -= 10;
        if (EdgeType(c % W, c / W, side) != GenData.E_NONE) s += 10;   // against a wall
        // (the foot is open floor: StairFits)
        return s;
    }

    // After BuildEdgesSite: a stair for every level, then a doorway into any of the level's rooms the stair cannot
    // reach through the ground floor's doors. False when a level has no place for a stair.
    bool PlaceStairs()
    {
        nStairs = 0;
        if (nLvl == 0) return true;
        GenRng rng = Rng(ST_EDGES, 31, 0);
        for (int j = 0; j < nLvl; j++)
        {
            int li = lvlInst[j], bi = instBase[li];
            int low = lvlUp[j] != 0 ? bi : li, high = lvlUp[j] != 0 ? li : bi;
            int ddx = instDX[high] - instDX[low], ddz = instDZ[high] - instDZ[low];
            int best = -1000, bc = -1, brun = 0, bside = 0;
            for (int c = 0; c < W * H; c++)
            {
                if (cellInst[c] != low) continue;
                int ch = (c / W + ddz) * W + c % W + ddx;
                if (IsStairCell(c) || IsStairCell(ch)) continue;
                for (int run = 0; run < 4; run++)
                    for (int t = 0; t < 2; t++)
                    {
                        int side = (run + 1 + 2 * t) & 3;
                        int mc = StairFits(c, run, side, false), mh = StairFits(ch, run, side, false);
                        if (mc < 0 || mh < 0) continue;
                        int score = StairScore(c, side, run) + StairScore(ch, side, run) - 25 * (mc + mh) + rng.Range(10);
                        if (score > best) { best = score; bc = c; brun = run; bside = side; }
                    }
            }
            if (bc < 0) return false;
            StairFits(bc, brun, bside, true);
            StairFits((bc / W + ddz) * W + bc % W + ddx, brun, bside, true);
            stairLow[nStairs] = bc; stairHigh[nStairs] = (bc / W + ddz) * W + bc % W + ddx;
            stairRun[nStairs] = brun; stairSide[nStairs] = bside;
            nStairs++;
            if (!JoinLevel(li, lvlUp[j] != 0 ? stairHigh[nStairs - 1] : stairLow[nStairs - 1], ref rng)) return false;
        }
        return true;
    }

    // every room of level li reachable from the stair's cell: a doorway through a wall where it is not
    bool JoinLevel(int li, int stairCell, ref GenRng rng)
    {
        bool[] got = new bool[400];
        got[cellRoom[stairCell]] = true;
        for (int guard = 0; guard < 32; guard++)
        {
            // spread through the doorways
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int c = 0; c < W * H; c++)
                {
                    if (cellInst[c] != li || !got[cellRoom[c]]) continue;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = c % W + DX[d], nz = c / W + DZ[d];
                        if (!InGrid(nx, nz)) continue;
                        int n = nz * W + nx;
                        if (cellInst[n] != li || got[cellRoom[n]]) continue;
                        int et = EdgeType(c % W, c / W, d);
                        if (et == GenData.E_DOOR || et == GenData.E_NONE) { got[cellRoom[n]] = true; grew = true; }
                    }
                }
            }
            // a wall between a reached room and one that is not
            int nc = 0;
            for (int c = 0; c < W * H; c++)
            {
                if (cellInst[c] != li || !got[cellRoom[c]] || IsStairCell(c)) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = c % W + DX[d], nz = c / W + DZ[d];
                    if (!InGrid(nx, nz)) continue;
                    int n = nz * W + nx;
                    if (IsStairCell(n) || cellInst[n] != li || got[cellRoom[n]] || EdgeType(c % W, c / W, d) != GenData.E_WALL) continue;
                    if (nc < 1024) cand[nc++] = c * 4 + d;
                }
            }
            if (nc == 0)
            {
                for (int c = 0; c < W * H; c++) if (cellInst[c] == li && !got[cellRoom[c]]) return false;
                return true;
            }
            int pick = cand[rng.Range(nc)];
            SetEdge(pick / 4 % W, pick / 4 / W, pick % 4, GenData.E_DOOR, DoorOff(ref rng));
        }
        return false;
    }

    // In BuildTags, before the room cell lists: no floor and no room on the flights and the landing, the entry rows
    // kept clear and joined by the link
    void StairTags()
    {
        for (int i = 0; i < IW * IH; i++) link[i] = -1;
        for (int s = 0; s < nStairs; s++)
        {
            int run = stairRun[s], side = stairSide[s];
            for (int lvl = 0; lvl < 2; lvl++)
            {
                int c = lvl == 0 ? stairLow[s] : stairHigh[s];
                for (int b = 0; b < STAIR_ROWS; b++)
                    for (int a = 0; a < STAIR_W; a++)
                    {
                        int i = StairInner(c, run, side, a, b);
                        if (b >= 1) { tags[i] = 0; innerRoom[i] = -1; }
                        else tags[i] |= GenData.T_DOOR_CLEAR | GenData.T_NO_SPAWN;
                    }
                // the landing beyond the foot: two rows of the next cell in front of the flights kept clear, so no
                // prop stands where a player steps off the stair
                for (int b = -2; b <= -1; b++)
                    for (int a = 0; a < STAIR_W; a++)
                    {
                        int i = StairInner(c, run, side, a, b);
                        if (innerRoom[i] >= 0) tags[i] |= GenData.T_DOOR_CLEAR | GenData.T_NO_SPAWN;
                    }
            }
            // the first flight starts from the lower entry row on the room's side, the second ends on the upper one by the wall
            for (int a = 0; a < 2; a++)
            {
                int lo = StairInner(stairLow[s], run, side, a + 2, 0), hi = StairInner(stairHigh[s], run, side, a, 0);
                link[lo] = hi; link[hi] = lo;
            }
        }
    }

    // the level of a room (0 ground, 1 upstairs, -1 a basement)
    public int RoomLevel(int r) { return instLevel[roomInst[r]]; }
}
