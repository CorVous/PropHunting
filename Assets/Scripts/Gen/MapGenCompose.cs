// MapGen, part 3a: the composition primitives (owner decision G28). A room plan never draws from a table and
// drops the result on a free cell: it asks for a place by rule. The rules are:
//
//   WallPlace / WallRow   an anchor (or a contiguous row) with its back against a wall or a fence, at the best
//                         stretch of wall for a purpose: centred on a wall, in a corner, near or far from a point,
//                         at or away from a window, with its front clearance free
//   Rel                   something that belongs with an anchor, at a position and facing given in the anchor's
//                         own frame (a coffee table in front of the sofa, bedside tables at the bed's head)
//   CornerPlace           a corner piece, turned towards the room
//   OpenSpot / FreePlace  a centre piece (a table with its chairs, a fire) at the most open spot of the room
//   groups                MapGenGroups.cs: a small named group against a wall or beside a host
//
// Every object records what it was placed against (emitHost): a wall, another object, a group, or, for the few
// pieces that may stand free, H_FREE. Nothing is placed without one. Facing comes from the relation; only the
// short list of natural things (rocks, logs, bushes, flowers, mushrooms, trees) is turned freely.
// Positions are in millimetres, so pieces of a run touch; the cells an object's real outline covers are derived
// from it. The whole bounding box of an object, a tree's canopy included, has to stay clear of every wall and
// fence (G31).
public partial class MapGen
{
    // ---- what an object was placed against ----
    public const int H_NONE = -1, H_WALL = -2, H_FREE = -3, H_NATURE = -4, H_GROUND = -5;
    int curHost = H_NONE;

    // ---- the room being composed ----
    int pRoom;
    int looseUsed;

    // the sub-stream of step k of the room's plan: a step's outcome does not depend on which other steps ran
    GenRng SR(int k) { return Rng(ST_ROOM, pRoom, k); }

    // room for n more props on the floor under the room's budget
    bool Fit(int n) { return roomFloor[pRoom] + n <= roomFloorTarget[pRoom]; }

    // the loose remainder: at most LOOSE_SHARE of the room's budget, never fewer than one allowed (report 15, M2)
    bool LooseFit(int n)
    {
        int cap = roomFloorTarget[pRoom] * GenData.LOOSE_SHARE / 100;
        if (cap < 1) cap = 1;
        return looseUsed + n <= cap && Fit(n);
    }

    // a piece the room cannot do without found no place
    public int[] statTypeMandFail = new int[128];
    void MandFail(int n) { statMandatoryFail += n; statTypeMandFail[roomType[pRoom] & 127] += n; }

    // def lookup by id, remembered (in the VRChat world the ids are resolved when the tables are baked)
    string[] dnId = new string[400];
    int[] dnDef = new int[400];
    int dnN;

    int Dn(string id)
    {
        for (int i = 0; i < dnN; i++) if ((object)dnId[i] == (object)id) return TwinIn(pRoom, dnDef[i]);
        int d = GenData.FindDef(id);
        if (dnN < dnId.Length) { dnId[dnN] = id; dnDef[dnN] = d; dnN++; }
        return TwinIn(pRoom, d);
    }

    // revision 11: a house is furnished from the Kenney family or, two in five, from KayKit (GenData.BuildTwins). The
    // plans name the Kenney pieces; in a KayKit house Dn and the slots give the twin instead.
    public bool KayHouse(int inst)
    {
        return inst >= 0 && mapTheme == GenData.THEME_NONE && instZone[inst] == GenData.Z_HOUSE && GenRng.Hash32(seed, (uint)inst, 7717, 11, 0) % 1000 < 400;
    }


    int TwinIn(int room, int d)
    {
        if (d >= 0 && mapTheme != GenData.THEME_NONE) { int t = GenData.defThemeTwin[d * 3 + mapTheme]; return t >= 0 ? t : d; }   // a themed map (revision 12)
        if (d < 0 || room < 0 || GenData.defTwin[d] < 0 || !KayHouse(roomInst[room])) return d;
        return GenData.defTwin[d];
    }

    // one of two or three ids by weight
    int Pick(ref GenRng rng, string a, int wa, string b, int wb) { return Dn(rng.Range(wa + wb) < wa ? a : b); }

    int Pick(ref GenRng rng, string a, int wa, string b, int wb, string c, int wc)
    {
        int r = rng.Range(wa + wb + wc);
        return Dn(r < wa ? a : (r < wa + wb ? b : c));
    }

    // =====================================================================================================
    // Full extent (G31): the whole bounding box of an object, turned, against walls, fences and buildings
    // =====================================================================================================
    // half the size of the def's whole bounding box along x and z when turned by yaw (a tree with its canopy)
    public void FullHalf(int def, int yaw, out int hx, out int hz)
    {
        int sx = GenData.defSx[def], sz = GenData.defSz[def];
        if (Has(def, GenData.FL_SCENERY))
        {
            // a tree's crown is round: the circle around its longer side holds it whatever way it is turned
            hx = hz = ((sx > sz ? sx : sz) + 1) / 2;
            return;
        }
        int a = ((yaw % 360) + 360) % 360;
        int sn = Abs(sinTab[a]), cs = Abs(sinTab[(a + 90) % 360]);
        hx = (sx * cs + sz * sn + 4095) / 8192;
        hz = (sx * sn + sz * cs + 4095) / 8192;
    }

    // Does the box stay clear of every wall and fence, and off every cell that is not floor? An outdoor object
    // may not reach over a building either (a canopy over a roof). Walls and fences are taken WALL_HALF thick
    // on each side of the cell edge they stand on.
    public bool WallsClear(int minX, int minZ, int maxX, int maxZ, bool outdoor)
    {
        int cx0 = FloorDiv(minX, GenData.INNER_MM), cx1 = FloorDiv(maxX - 1, GenData.INNER_MM);
        int cz0 = FloorDiv(minZ, GenData.INNER_MM), cz1 = FloorDiv(maxZ - 1, GenData.INNER_MM);
        if (cx0 < 0 || cz0 < 0 || cx1 >= IW || cz1 >= IH) return false;
        for (int z = cz0; z <= cz1; z++)
            for (int x = cx0; x <= cx1; x++)
            {
                int t = tags[z * IW + x];
                if ((t & GenData.T_FLOOR) == 0) return false;
                if (outdoor && (t & GenData.T_OUTDOOR) == 0) return false;
                if ((t & GenData.T_WALL_ANY) == 0) continue;
                if ((t & GenData.T_WALL_N) != 0 && maxZ > (z + 1) * GenData.INNER_MM - GenData.WALL_HALF_MM) return false;
                if ((t & GenData.T_WALL_S) != 0 && minZ < z * GenData.INNER_MM + GenData.WALL_HALF_MM) return false;
                if ((t & GenData.T_WALL_E) != 0 && maxX > (x + 1) * GenData.INNER_MM - GenData.WALL_HALF_MM) return false;
                if ((t & GenData.T_WALL_W) != 0 && minX < x * GenData.INNER_MM + GenData.WALL_HALF_MM) return false;
            }
        return true;
    }

    // the whole of def at (px, pz, yaw) is clear of walls; and a tree's canopy has nothing tall under it,
    // nothing tall stands under a canopy
    bool ExtentClear(int def, int px, int pz, int yaw, int ignore)
    {
        int hx, hz;
        FullHalf(def, yaw, out hx, out hz);
        bool outdoor = (tags[FloorDiv(pz, GenData.INNER_MM) * IW + FloorDiv(px, GenData.INNER_MM)] & GenData.T_OUTDOOR) != 0;
        if (!WallsClear(px - hx, pz - hz, px + hx, pz + hz, outdoor)) return false;
        if (!outdoor) return true;
        int cx0 = FloorDiv(px - hx, GenData.INNER_MM), cx1 = FloorDiv(px + hx - 1, GenData.INNER_MM);
        int cz0 = FloorDiv(pz - hz, GenData.INNER_MM), cz1 = FloorDiv(pz + hz - 1, GenData.INNER_MM);
        int under = GenData.defCanopyY[def];
        for (int z = cz0; z <= cz1; z++)
            for (int x = cx0; x <= cx1; x++)
            {
                int i = z * IW + x;
                if (under > 0)
                {
                    // a tree: whatever stands under its canopy has to be lower than the canopy
                    int o = occ[i];
                    if (o != 0 && o != ignore + 1 && !Has(emitDef[o - 1], GenData.FL_SCENERY) && GenData.defSy[emitDef[o - 1]] > under - 50) return false;
                }
                else if (canopy[i] != 0 && GenData.defSy[def] > canopy[i] - 50) return false;
            }
        return true;
    }

    // Props against each other (G31): cells are owned with a tolerance of 5 cm, so two neighbours could reach
    // into each other by up to 10 cm. The outline is therefore tested exactly against everything that owns a
    // cell in or next to the object's cells. Touching is allowed; set-mates are tested by OverlapsSet.
    bool NeighboursClear(int def, int px, int pz, int yaw, int x0, int z0, int gw, int gd, int self)
    {
        if (Has(def, GenData.FL_FLAT) || Has(def, GenData.FL_DECOR)) return true;
        int hx, hz;
        HalfExtents(def, yaw, out hx, out hz);
        int last = -1;
        for (int z = z0 - 1; z <= z0 + gd; z++)
            for (int x = x0 - 1; x <= x0 + gw; x++)
            {
                if (x < 0 || z < 0 || x >= IW || z >= IH) continue;
                int o = occ[z * IW + x] - 1;
                if (o < 0 || o == last || o == self) continue;
                last = o;
                // a group's cells are held by its first member: test every member
                int g0 = emitGroup[o] == o ? o : -1;
                for (int e = o; e < nEmit; e++)
                {
                    if (e != o && (g0 < 0 || emitGroup[e] != g0)) { if (g0 < 0) break; continue; }
                    if ((emitFlags[e] & EF_REMOVED) != 0 || e == self) continue;
                    int ox, oz;
                    HalfExtents(emitDef[e], emitYaw[e], out ox, out oz);
                    if (Abs(emitX[e] - px) < hx + ox - 10 && Abs(emitZ[e] - pz) < hz + oz - 10) return false;
                    if (g0 < 0) break;
                }
                // the members of its composition set may share its cells without holding one (a lamp beside a sofa)
                int key = emitSet[o] >= 0 ? emitSet[o] : o;
                for (int e = key; e < nEmit; e++)
                {
                    if ((e != key && emitSet[e] != key) || e == o || e == self || emitParent[e] >= 0 || (emitFlags[e] & EF_REMOVED) != 0) continue;
                    if (Has(emitDef[e], GenData.FL_FLAT) || Has(emitDef[e], GenData.FL_DECOR)) continue;
                    int ox, oz;
                    HalfExtents(emitDef[e], emitYaw[e], out ox, out oz);
                    if (Abs(emitX[e] - px) < hx + ox - 10 && Abs(emitZ[e] - pz) < hz + oz - 10) return false;
                }
            }
        return true;
    }

    public int[] canopy;      // lowest canopy over the cell (mm above the ground), 0 = open sky

    void StampCanopy(int e)
    {
        int def = emitDef[e], under = GenData.defCanopyY[def];
        if (under <= 0) return;
        int hx, hz;
        FullHalf(def, emitYaw[e], out hx, out hz);
        int cx0 = FloorDiv(emitX[e] - hx, GenData.INNER_MM), cx1 = FloorDiv(emitX[e] + hx - 1, GenData.INNER_MM);
        int cz0 = FloorDiv(emitZ[e] - hz, GenData.INNER_MM), cz1 = FloorDiv(emitZ[e] + hz - 1, GenData.INNER_MM);
        for (int z = cz0; z <= cz1; z++)
            for (int x = cx0; x <= cx1; x++)
            {
                if (x < 0 || z < 0 || x >= IW || z >= IH) continue;
                int i = z * IW + x;
                if (canopy[i] == 0 || canopy[i] > under) canopy[i] = under;
            }
    }

    void RebuildCanopy()
    {
        for (int i = 0; i < canopy.Length; i++) canopy[i] = 0;
        for (int e = 0; e < nEmit; e++)
            if ((emitFlags[e] & EF_REMOVED) == 0 && emitParent[e] < 0) StampCanopy(e);
    }

    // =====================================================================================================
    // Stamping at an exact position
    // =====================================================================================================
    int lastX0, lastZ0, lastGW, lastGD;

    // Puts def at (px, pz) mm turned by yaw, if the cells its outline covers are free. set: an anchor whose cells
    // and front clearance the object may share (its own composition); host: what it is placed against.
    // Returns the emit index or -1.
    int StampAt(int def, int room, int px, int pz, int yaw, int flags, int set, int host, bool reserveFront)
    {
        bool flat = Has(def, GenData.FL_FLAT) || Has(def, GenData.FL_DECOR);
        int hx, hz;
        HalfExtents(def, yaw, out hx, out hz);
        int tol = flat ? 0 : SOLID_TOL;
        int x0 = FloorDiv(px - hx + tol, GenData.INNER_MM), x1 = FloorDiv(px + hx - tol - 1, GenData.INNER_MM);
        int z0 = FloorDiv(pz - hz + tol, GenData.INNER_MM), z1 = FloorDiv(pz + hz - tol - 1, GenData.INNER_MM);
        if (x1 < x0) { x0 = FloorDiv(px, GenData.INNER_MM); x1 = x0; }
        if (z1 < z0) { z0 = FloorDiv(pz, GenData.INNER_MM); z1 = z0; }
        int gw = x1 - x0 + 1, gd = z1 - z0 + 1;
        if (!RectFree(x0, z0, gw, gd, room, def, set))
        {
            if (TraceRoom == room) System.Console.Error.WriteLine("  no room for " + GenData.defId[def] + " at " + px + "," + pz + " yaw " + yaw + " cells " + x0 + "," + z0 + " " + gw + "x" + gd);
            return -1;
        }
        if (set >= 0 && !flat && OverlapsSet(def, px, pz, hx, hz, set))
        {
            if (TraceRoom == room) System.Console.Error.WriteLine("  " + GenData.defId[def] + " at " + px + "," + pz + " would overlap its own group");
            return -1;
        }
        int a = ((yaw % 360) + 360) % 360;
        int rot = ((a + 45) / 90) & 3;
        curHost = host;
        lastX0 = x0; lastZ0 = z0; lastGW = gw; lastGD = gd;
        int e = StampObject(def, room, x0, z0, gw, gd, rot, px, pz, yaw, flags, reserveFront);
        if (e >= 0) emitSet[e] = set;
        else if (TraceRoom == room) System.Console.Error.WriteLine("  " + GenData.defId[def] + " at " + px + "," + pz + " yaw " + yaw + " would touch a wall or a neighbour");
        return e;
    }

    // does the outline overlap another member of the composition set (by more than a centimetre)?
    bool OverlapsSet(int def, int px, int pz, int hx, int hz, int set)
    {
        for (int e = set; e < nEmit; e++)
        {
            if ((emitFlags[e] & EF_REMOVED) != 0) continue;
            int p = emitParent[e];
            if (p >= 0)
            {
                // the chairs and seats of a member count too: they may stand on cells their host holds
                if ((emitFlags[e] & EF_ADJACENT) == 0 || (p != set && emitSet[p] != set)) continue;
            }
            else if (e != set && emitSet[e] != set) continue;
            if (Has(emitDef[e], GenData.FL_FLAT)) continue;
            int ox, oz;
            HalfExtents(emitDef[e], emitYaw[e], out ox, out oz);
            if (Abs(emitX[e] - px) < hx + ox - 10 && Abs(emitZ[e] - pz) < hz + oz - 10) return true;
        }
        return false;
    }

    // =====================================================================================================
    // Walls: the straight stretches of wall or fence of a room, and the best place along them
    // =====================================================================================================
    const int MAXSEG = 96;
    int nSeg;
    int[] segDir = new int[MAXSEG], segLine = new int[MAXSEG], segA0 = new int[MAXSEG], segA1 = new int[MAXSEG];
    bool[] segBan = new bool[MAXSEG];

    // a doorway interrupts a wall, so each side of a door is a stretch of its own
    void BuildSegs(int room)
    {
        nSeg = 0;
        int x0 = roomX0[room], z0 = roomZ0[room], x1 = roomX1[room], z1 = roomZ1[room];
        for (int d = 0; d < 4; d++)
        {
            bool alongX = d == 0 || d == 2;
            int l0 = alongX ? z0 : x0, l1 = alongX ? z1 : x1, a0 = alongX ? x0 : z0, a1 = alongX ? x1 : z1;
            int bit = WallBit(d);
            for (int line = l0; line <= l1; line++)
            {
                int start = -1;
                for (int a = a0; a <= a1 + 1; a++)
                {
                    bool on = false;
                    if (a <= a1)
                    {
                        int i = (alongX ? line : a) * IW + (alongX ? a : line);
                        on = innerRoom[i] == room && (tags[i] & bit) != 0;
                    }
                    if (on && start < 0) start = a;
                    if (!on && start >= 0)
                    {
                        if (nSeg < MAXSEG) { segDir[nSeg] = d; segLine[nSeg] = line; segA0[nSeg] = start; segA1[nSeg] = a - 1; segBan[nSeg] = false; nSeg++; }
                        start = -1;
                    }
                }
            }
        }
    }

    void ClearBans() { for (int i = 0; i < nSeg; i++) segBan[i] = false; }

    // is the wall on side d of this inner cell a window's opening? (1 m to 3 m along a 4 m edge)
    bool WindowAt(int ix, int iz, int d)
    {
        int x = ix >> 3, z = iz >> 3;
        int et = d == 0 ? edgeH[(z + 1) * W + x] : (d == 2 ? edgeH[z * W + x] : (d == 1 ? edgeV[z * (W + 1) + x + 1] : edgeV[z * (W + 1) + x]));
        if (et != GenData.E_WINDOW) return false;
        int along = (d == 0 || d == 2) ? (ix & 7) : (iz & 7);
        return along >= 2 && along <= 5;
    }

    // wall modes: where along the room's walls
    public const int WM_CENTRE = 0, WM_CORNER = 1, WM_NEAR = 2, WM_FAR = 3, WM_ANY = 4;
    // windows: avoid if possible, never, prefer, do not care
    public const int WIN_AVOID = 0, WIN_NEVER = 1, WIN_PREFER = 2, WIN_ANY = 3;

    int wrSeg, wrDir, wrBack, wrCentre;   // the place found: wall stretch, its side, the back line and the centre along it (mm)

    // The best free place for something lenMm long and depMm deep with its back against a wall of the room.
    // clearCells: free floor wanted in front. Returns false if there is none.
    bool FindWallRun(int room, int lenMm, int depMm, int clearCells, int mode, int tx, int tz, int win, ref GenRng rng)
    {
        int best = -2000000000;
        bool found = false;
        int cell = GenData.INNER_MM, off = GenData.WALL_HALF_MM + 20;
        int nd = FloorDiv(off + depMm - SOLID_TOL - 1, cell) + 1;
        for (int s = 0; s < nSeg; s++)
        {
            if (segBan[s]) continue;
            int d = segDir[s], line = segLine[s];
            bool alongX = d == 0 || d == 2;
            // the usable stretch: a real corner at an end costs the wall's thickness
            int iLow = (alongX ? line : segA0[s]) * IW + (alongX ? segA0[s] : line);
            int iHigh = (alongX ? line : segA1[s]) * IW + (alongX ? segA1[s] : line);
            bool cornerLow = (tags[iLow] & WallBit(alongX ? 3 : 2)) != 0, cornerHigh = (tags[iHigh] & WallBit(alongX ? 1 : 0)) != 0;
            int lo = segA0[s] * cell + (cornerLow ? off : 0), hi = (segA1[s] + 1) * cell - (cornerHigh ? off : 0);
            if (hi - lo < lenMm) continue;
            int mid = (lo + hi) / 2;
            int steps = (hi - lo - lenMm) / 250;
            for (int k = -2; k <= steps; k++)
            {
                int c = k == -2 ? mid : (k == -1 ? hi - lenMm / 2 : lo + lenMm / 2 + k * 250);
                int ca0 = FloorDiv(c - lenMm / 2 + SOLID_TOL, cell), ca1 = FloorDiv(c + lenMm / 2 - SOLID_TOL - 1, cell);
                if (ca1 < ca0) ca1 = ca0;
                int x0, z0, gw, gd;
                if (d == 2) { x0 = ca0; z0 = line; gw = ca1 - ca0 + 1; gd = nd; }
                else if (d == 0) { x0 = ca0; z0 = line - nd + 1; gw = ca1 - ca0 + 1; gd = nd; }
                else if (d == 3) { x0 = line; z0 = ca0; gw = nd; gd = ca1 - ca0 + 1; }
                else { x0 = line - nd + 1; z0 = ca0; gw = nd; gd = ca1 - ca0 + 1; }
                if (!RectFree(x0, z0, gw, gd, room, 0, -1)) continue;
                int rot = (d + 2) & 3;
                if (clearCells > 0 && !FrontFree(x0, z0, gw, gd, rot, clearCells)) continue;
                int windows = 0;
                for (int a = ca0; a <= ca1; a++)
                    if (WindowAt(alongX ? a : line, alongX ? line : a, d)) windows++;
                if (windows > 0 && win == WIN_NEVER) continue;
                int back = d == 2 ? line * cell + off : (d == 0 ? (line + 1) * cell - off : (d == 3 ? line * cell + off : (line + 1) * cell - off));
                int wx = alongX ? c : (d == 3 ? back + depMm / 2 : back - depMm / 2);
                int wz = alongX ? (d == 2 ? back + depMm / 2 : back - depMm / 2) : c;
                int score = rng.Range(240);
                if (mode == WM_CENTRE) score += -Abs(c - mid) * 2 + (hi - lo) / 4;
                else if (mode == WM_CORNER)
                {
                    int dl = c - lenMm / 2 - lo, dh = hi - (c + lenMm / 2);
                    int dEnd = dl < dh ? dl : dh;
                    bool real = dl < dh ? cornerLow : cornerHigh;
                    score += -dEnd * 3 - (real ? 0 : 900);
                }
                else if (mode == WM_NEAR) score += -(Abs(wx - tx) + Abs(wz - tz)) * 2;
                else if (mode == WM_FAR) score += (Abs(wx - tx) + Abs(wz - tz)) * 2;
                else score += rng.Range(4000);
                if (windows > 0) score += win == WIN_PREFER ? 1500 : (win == WIN_AVOID ? -2500 : 0);
                if (score > best)
                {
                    best = score; found = true;
                    wrSeg = s; wrDir = d; wrBack = back; wrCentre = c;
                }
            }
        }
        if (!found && TraceRoom == room) System.Console.Error.WriteLine("  no stretch of wall for " + lenMm + " x " + depMm + " mm with " + clearCells + " cells clear in front");
        return found;
    }

    // world position and facing of something depMm deep whose back is on the found wall line, centred at `along`
    void WallPos(int d, int back, int along, int depMm, out int px, out int pz, out int rot)
    {
        rot = (d + 2) & 3;
        if (d == 2) { px = along; pz = back + depMm / 2; }
        else if (d == 0) { px = along; pz = back - depMm / 2; }
        else if (d == 3) { px = back + depMm / 2; pz = along; }
        else { px = back - depMm / 2; pz = along; }
    }

    // WallPlace, kept: if the place found cuts the room's walkway, that stretch of wall is given up and the next
    // best tried (up to four). Returns the emit index of the piece, which stands and is kept, or -1.
    int[] wtBan = new int[4];

    int WallKeep(int def, int mode, int tx, int tz, int win, int flags, ref GenRng rng)
    {
        hq_WallKeep_def = def;
        hq_WallKeep_mode = mode;
        hq_WallKeep_tx = tx;
        hq_WallKeep_tz = tz;
        hq_WallKeep_win = win;
        hq_WallKeep_flags = flags;
        hq_WallKeep_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); WallKeep_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_WallKeep_rng;
        return hq_WallKeep_ret;
    }

    // WallKeep in phases, a step a call (autophase4); WallKeep() above runs them to the end
    void WallKeep_Body()
    {
        planMax = 2;
        if (planPhase == 0)
        {
            int nb = 0, e = -1;
            hq_WallKeep_nb = nb;
            hq_WallKeep_e = e;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int e = hq_WallKeep_e;
            if (!PhSubActive())
            {
                int t = planIter;
                if (!(t < 4 && e < 0)) PhBreak();
                else { hq_WallKeep_t = t; PhSubBegin(); }
            }
            hq_WallKeep_e = e;
            if (PhSubActive())
            {
                PhPush(); WallKeep_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 2)
        {
            int nb = hq_WallKeep_nb;
            int e = hq_WallKeep_e;
            for (int k = 0; k < nb; k++) segBan[wtBan[k]] = false;
            { hq_WallKeep_ret = e; hq_WallKeep_nb = nb; hq_WallKeep_e = e; PhEnd(); return; }
            hq_WallKeep_nb = nb;
            hq_WallKeep_e = e;
        }
    }
    void WallKeep_L1()
    {
        planMax = 2;
        if (planPhase == 0)
        {
            int def = hq_WallKeep_def;
            int mode = hq_WallKeep_mode;
            int tx = hq_WallKeep_tx;
            int tz = hq_WallKeep_tz;
            int win = hq_WallKeep_win;
            int flags = hq_WallKeep_flags;
            GenRng rng = hq_WallKeep_rng;
            int e = hq_WallKeep_e;
            int first = nEmit;
            e = WallPlace(def, mode, tx, tz, win, flags, ref rng);
            if (e < 0) { hq_WallKeep_def = def; hq_WallKeep_mode = mode; hq_WallKeep_tx = tx; hq_WallKeep_tz = tz; hq_WallKeep_win = win; hq_WallKeep_flags = flags; hq_WallKeep_rng = rng; hq_WallKeep_e = e; hq_WallKeep_first = first; phBreak = true; PhEnd(); return; }
            int seg = wrSeg;
            hq_WallKeep_def = def;
            hq_WallKeep_mode = mode;
            hq_WallKeep_tx = tx;
            hq_WallKeep_tz = tz;
            hq_WallKeep_win = win;
            hq_WallKeep_flags = flags;
            hq_WallKeep_rng = rng;
            hq_WallKeep_e = e;
            hq_WallKeep_first = first;
            hq_WallKeep_seg = seg;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int flags = hq_WallKeep_flags;
            int first = hq_WallKeep_first;
            bool ht1 = false;
            if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = (flags & EF_MANDATORY) != 0; PhSubReset(); }
            PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { ht1 = hq_Keep_ret; PhBreak(); }
            hq_WallKeep_flags = flags;
            hq_WallKeep_first = first;
            hq_WallKeep_ht1 = ht1;
        }
        if (planPhase == 2)
        {
            int nb = hq_WallKeep_nb;
            int e = hq_WallKeep_e;
            int seg = hq_WallKeep_seg;
            bool ht1 = hq_WallKeep_ht1;
            if (ht1)
            {
                { hq_WallKeep_nb = nb; hq_WallKeep_e = e; hq_WallKeep_seg = seg; hq_WallKeep_ht1 = ht1; phBreak = true; PhEnd(); return; }
            }
            e = -1;
            if (segBan[seg]) { hq_WallKeep_nb = nb; hq_WallKeep_e = e; hq_WallKeep_seg = seg; hq_WallKeep_ht1 = ht1; phBreak = true; PhEnd(); return; }
            segBan[seg] = true; wtBan[nb++] = seg;
            hq_WallKeep_nb = nb;
            hq_WallKeep_e = e;
            hq_WallKeep_seg = seg;
            hq_WallKeep_ht1 = ht1;
        }
    }


    // One object with its back against a wall, facing the room. Returns the emit index or -1.
    int WallPlace(int def, int mode, int tx, int tz, int win, int flags, ref GenRng rng)
    {
        return WallPlace(def, 0, mode, tx, tz, win, flags, ref rng);
    }

    // gapMm: how far its back stands off the wall or fence (signs, G42: clear of the fence, not touching it)
    int WallPlace(int def, int gapMm, int mode, int tx, int tz, int win, int flags, ref GenRng rng)
    {
        int sx = GenData.defSx[def], sz = GenData.defSz[def];
        if (GenData.defSy[def] >= 1200 && win != WIN_ANY) win = WIN_NEVER;   // nothing tall in front of a window (K10)
        if (!FindWallRun(pRoom, sx, sz + gapMm, GenData.defClear[def], mode, tx, tz, win, ref rng)) return -1;
        int px, pz, rot;
        WallPos(wrDir, wrBack, wrCentre, sz + 2 * gapMm, out px, out pz, out rot);
        return StampAt(def, pRoom, px, pz, rot * 90, flags, -1, H_WALL, true);   // parallel to its wall
    }

    int[] rowDef = new int[12];
    int rowN;

    // A contiguous row along a wall (a kitchen run, two bookcases, a bar): rowDef[0 .. rowN-1] left to right as
    // seen from the room, gapMm apart. All or nothing. Returns the first emit (the set the others belong to) or -1.
    int WallRow(int gapMm, int clearCells, int mode, int tx, int tz, int win, int flags, ref GenRng rng)
    {
        int len = 0, dep = 0;
        bool tall = false;
        for (int i = 0; i < rowN; i++)
        {
            len += GenData.defSx[rowDef[i]] + (i > 0 ? gapMm : 0);
            if (GenData.defSz[rowDef[i]] > dep) dep = GenData.defSz[rowDef[i]];
            if (GenData.defSy[rowDef[i]] >= 1200) tall = true;
        }
        if (tall && win != WIN_ANY) win = WIN_NEVER;
        if (!FindWallRun(pRoom, len, dep, clearCells, mode, tx, tz, win, ref rng)) return -1;
        return StampRow(wrDir, wrBack, wrCentre - len / 2, gapMm, flags);
    }

    // stamps rowDef along a wall line from `start` (the low end along the wall, mm); backs on the wall
    int StampRow(int d, int back, int start, int gapMm, int flags)
    {
        int first = nEmit, set = -1;
        // left to right as seen from the room: along +x for a south wall, -x for a north wall, and so on
        bool reversed = d == 0 || d == 3;
        int len = 0;
        for (int i = 0; i < rowN; i++) len += GenData.defSx[rowDef[i]] + (i > 0 ? gapMm : 0);
        int at = 0;
        for (int i = 0; i < rowN; i++)
        {
            int def = rowDef[i], sx = GenData.defSx[def];
            int along = reversed ? start + len - at - sx / 2 : start + at + sx / 2;
            int px, pz, rot;
            WallPos(d, back, along, GenData.defSz[def], out px, out pz, out rot);
            int e = StampAt(def, pRoom, px, pz, rot * 90, flags, set, H_WALL, false);
            if (e < 0)
            {
                for (int r = nEmit - 1; r >= first; r--) Remove(r);
                return -1;
            }
            if (set < 0) set = e;
            at += sx + gapMm;
        }
        // the front clearance of the whole row, now that it stands
        for (int e = first; e < nEmit; e++)
        {
            int clear = GenData.defClear[emitDef[e]];
            if (clear > 0 && emitParent[e] < 0) ReserveFront(emitRX[e], emitRZ[e], emitRW[e], emitRD[e], emitRot[e], clear, Has(emitDef[e], GenData.FL_OPENABLE), e);
        }
        return set;
    }

    // =====================================================================================================
    // Relations: placed in the frame of a host (x to its right, z to its front), facing by the relation
    // =====================================================================================================
    int Rel(int def, int host, int lx, int lz, int relYaw, int flags)
    {
        int wx, wz;
        Local(host, lx, lz, out wx, out wz);
        int set = emitSet[host] >= 0 ? emitSet[host] : host;
        return StampAt(def, emitRoom[host], wx, wz, emitYaw[host] + relYaw, flags, set, host, true);
    }

    // the same for flat things (a rug under a seating group): only the room and other rugs matter
    int RelFlat(int def, int host, int lx, int lz, int relYaw, int flags)
    {
        int wx, wz;
        Local(host, lx, lz, out wx, out wz);
        int hx, hz;
        FullHalf(def, emitYaw[host] + relYaw, out hx, out hz);
        if (!WallsClear(wx - hx, wz - hz, wx + hx, wz + hz, false)) return -1;
        return StampAt(def, emitRoom[host], wx, wz, emitYaw[host] + relYaw, flags, -1, host, false);
    }

    // beside a host that stands at a wall: in line with it on one side (side = -1 its left, +1 its right, 0 = either),
    // backs on the same line, gapMm away
    int RelBeside(int def, int host, int side, int gapMm, int flags, ref GenRng rng)
    {
        int hd = emitDef[host];
        int s0 = side != 0 ? side : (rng.Chance(500) ? 1 : -1);
        for (int k = 0; k < (side != 0 ? 1 : 2); k++)
        {
            int s = k == 0 ? s0 : -s0;
            int lx = s * (GenData.defSx[hd] / 2 + gapMm + GenData.defSx[def] / 2);
            int lz = -(GenData.defSz[hd] - GenData.defSz[def]) / 2;
            int e = Rel(def, host, lx, lz, 0, flags);
            if (e >= 0) return e;
        }
        return -1;
    }

    // =====================================================================================================
    // Corners
    // =====================================================================================================
    // A piece in a corner of the room: the nearest to (or farthest from) a point, or any. angled = turned 45
    // degrees to face the room (a reading chair); otherwise its back is on one of the two walls.
    int CornerPlace(int def, int mode, int tx, int tz, bool angled, int flags, ref GenRng rng)
    {
        int room = pRoom;
        int s = roomCellStart[room], n = roomCellCount[room];
        int nc = 0;
        for (int k = s; k < s + n && nc < cand.Length; k++)
        {
            int i = roomCells[k], t = tags[i];
            if ((t & GenData.T_CORNER) == 0 || occ[i] != 0 || (t & (GenData.T_DOOR_CLEAR | GenData.T_PATH | GenData.T_NO_SPAWN)) != 0) continue;
            cand[nc++] = i;
        }
        Shuffle(cand, nc, ref rng);
        bool tall = GenData.defSy[def] >= 1200;
        for (int tries = 0; tries < 6 && nc > 0; tries++)
        {
            // the best remaining corner
            int bi = 0, bs = -2000000000;
            for (int k = 0; k < nc; k++)
            {
                int ix = cand[k] % IW, iz = cand[k] / IW;
                int dd = Abs(ix * GenData.INNER_MM + 250 - tx) + Abs(iz * GenData.INNER_MM + 250 - tz);
                int sc = mode == WM_NEAR ? -dd : (mode == WM_FAR ? dd : 0);
                if (sc > bs) { bs = sc; bi = k; }
            }
            int i = cand[bi];
            cand[bi] = cand[--nc];
            int cx = i % IW, cz = i / IW, t = tags[i];
            bool ws = (t & GenData.T_WALL_S) != 0, ww = (t & GenData.T_WALL_W) != 0;
            bool wn = (t & GenData.T_WALL_N) != 0, we = (t & GenData.T_WALL_E) != 0;
            if (!(wn || ws) || !(we || ww)) continue;
            int towardX = ww ? 1 : -1, towardZ = ws ? 1 : -1;
            if (tall && (WindowAt(cx, cz, ws ? 2 : 0) || WindowAt(cx, cz, ww ? 3 : 1))) continue;
            int yaw;
            if (angled) yaw = towardZ > 0 ? (towardX > 0 ? 45 : 315) : (towardX > 0 ? 135 : 225);
            else
            {
                bool backOnZ = rng.Chance(500);   // back on the south or north wall, else on the west or east wall
                yaw = backOnZ ? (towardZ > 0 ? 0 : 180) : (towardX > 0 ? 90 : 270);
            }
            int hx, hz;
            FullHalf(def, yaw, out hx, out hz);
            int gap = GenData.WALL_HALF_MM + 30;
            int px = ww ? cx * GenData.INNER_MM + gap + hx : (cx + 1) * GenData.INNER_MM - gap - hx;
            int pz = ws ? cz * GenData.INNER_MM + gap + hz : (cz + 1) * GenData.INNER_MM - gap - hz;
            int e = StampAt(def, room, px, pz, yaw, flags, -1, H_WALL, true);
            if (e >= 0) return e;
        }
        return -1;
    }

    // =====================================================================================================
    // Open spots: the centre pieces of a room stand where the floor is most open
    // =====================================================================================================
    int[] clr;   // per cell: steps to the nearest wall, object, door clearance or walkway (0 = not free)

    void BuildClearance(int room)
    {
        hq_BuildClearance_room = room;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); BuildClearance_Body(); fin = PhAdvance(); PhPop(); }
    }

    // BuildClearance in phases, a step a call (autophase4); BuildClearance() above runs them to the end
    void BuildClearance_Body()
    {
        planMax = 3;
        if (planPhase == 0)
        {
            int room = hq_BuildClearance_room;
            int s = roomCellStart[room], n = roomCellCount[room];
            int qh = 0, qt = 0;
            hq_BuildClearance_room = room;
            hq_BuildClearance_s = s;
            hq_BuildClearance_n = n;
            hq_BuildClearance_qh = qh;
            hq_BuildClearance_qt = qt;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int room = hq_BuildClearance_room;
            int s = hq_BuildClearance_s;
            int n = hq_BuildClearance_n;
            int qt = hq_BuildClearance_qt;
            if (planIter == 0) hq_BuildClearance_kAt = s;
            int k = hq_BuildClearance_kAt;
            for (int chunkN = 0; chunkN < 60; chunkN++, k += 1)   // so many rounds a step
            {
                if (!(k < s + n)) { PhBreak(); break; }
                int i = roomCells[k], t = tags[i];
                bool blocked = occ[i] != 0 || solid[i] > 0 || reserve[i] != 0 || (t & (GenData.T_DOOR_CLEAR | GenData.T_PATH | GenData.T_NO_SPAWN | GenData.T_BLOCKED | GenData.T_SWING)) != 0;
                if (blocked) { clr[i] = 0; continue; }
                bool edge = (t & GenData.T_WALL_ANY) != 0;
                int ix = i % IW, iz = i / IW;
                for (int d = 0; d < 8 && !edge; d++)
                {
                    int jx = ix + DX8[d], jz = iz + DZ8[d];
                    if (jx < 0 || jz < 0 || jx >= IW || jz >= IH) { edge = true; break; }
                    int j = jz * IW + jx;
                    if (innerRoom[j] != room) { edge = true; break; }
                    int tj = tags[j];
                    if (occ[j] != 0 || solid[j] > 0 || reserve[j] != 0 || (tj & (GenData.T_DOOR_CLEAR | GenData.T_PATH | GenData.T_NO_SPAWN | GenData.T_BLOCKED | GenData.T_SWING)) != 0) edge = true;
                }
                if (edge) { clr[i] = 1; queue[qt++] = i; }
                else clr[i] = -1;
            }
            hq_BuildClearance_kAt = k;
            hq_BuildClearance_room = room;
            hq_BuildClearance_s = s;
            hq_BuildClearance_n = n;
            hq_BuildClearance_qt = qt;
        }
        // steps count diagonals too, so a clearance of c means the square of 2c - 1 cells around the cell is free
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            int room = hq_BuildClearance_room;
            int qh = hq_BuildClearance_qh;
            int qt = hq_BuildClearance_qt;
            for (int chunkN = 0; chunkN < 60; chunkN++)   // so many rounds a step
            {
                if (!(qh < qt)) { PhBreak(); break; }
                int i = queue[qh++];
                int ix = i % IW, iz = i / IW;
                for (int d = 0; d < 8; d++)
                {
                    int jx = ix + DX8[d], jz = iz + DZ8[d];
                    if (jx < 0 || jz < 0 || jx >= IW || jz >= IH) continue;
                    int j = jz * IW + jx;
                    if (innerRoom[j] != room || clr[j] != -1) continue;
                    clr[j] = clr[i] + 1; queue[qt++] = j;
                }
            }
            hq_BuildClearance_room = room;
            hq_BuildClearance_qh = qh;
            hq_BuildClearance_qt = qt;
        }
        if (planPhase == 3)
        {
            int s = hq_BuildClearance_s;
            int n = hq_BuildClearance_n;
            for (int k = s; k < s + n; k++) if (clr[roomCells[k]] == -1) clr[roomCells[k]] = 99;   // a room without any edge
            hq_BuildClearance_s = s;
            hq_BuildClearance_n = n;
        }
    }

    // The cell of the room with the most clearance, at least minClr; among the open ones the nearest to (tx, tz)
    // (WM_NEAR), the farthest (WM_FAR) or a random one (WM_ANY). need: a tag the cell must have (0 = none).
    static readonly int[] DX8 = { 0, 1, 0, -1, 1, 1, -1, -1 };
    static readonly int[] DZ8 = { 1, 0, -1, 0, 1, -1, -1, 1 };

    int osAvoid;   // tags OpenSpot stays off (set by the caller, reset to 0 after the call)
    int[] osTried = new int[64];   // cells OpenSpot has offered and that did not work out (CentrePiece)
    int osTriedN;

    int OpenSpot(int minClr, int mode, int tx, int tz, int need, ref GenRng rng)
    {
        hq_OpenSpot_minClr = minClr;
        hq_OpenSpot_mode = mode;
        hq_OpenSpot_tx = tx;
        hq_OpenSpot_tz = tz;
        hq_OpenSpot_need = need;
        hq_OpenSpot_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); OpenSpot_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_OpenSpot_rng;
        return hq_OpenSpot_ret;
    }

    // OpenSpot in phases, a step a call (autophase4); OpenSpot() above runs them to the end
    void OpenSpot_Body()
    {
        planMax = 4;
        if (planPhase == 0)
        {
            int room = pRoom;
            hq_OpenSpot_room = room;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int room = hq_OpenSpot_room;
            if (planIter == 0) { hq_BuildClearance_room = room; PhSubReset(); }
            PhPush(); BuildClearance_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
            hq_OpenSpot_room = room;
        }
        if (planPhase == 2)
        {
            int room = hq_OpenSpot_room;
            int s = roomCellStart[room], n = roomCellCount[room];
            int best = -1, bestScore = -2000000000;
            hq_OpenSpot_room = room;
            hq_OpenSpot_s = s;
            hq_OpenSpot_n = n;
            hq_OpenSpot_best = best;
            hq_OpenSpot_bestScore = bestScore;
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            int minClr = hq_OpenSpot_minClr;
            int mode = hq_OpenSpot_mode;
            int tx = hq_OpenSpot_tx;
            int tz = hq_OpenSpot_tz;
            int need = hq_OpenSpot_need;
            GenRng rng = hq_OpenSpot_rng;
            int s = hq_OpenSpot_s;
            int n = hq_OpenSpot_n;
            int best = hq_OpenSpot_best;
            int bestScore = hq_OpenSpot_bestScore;
            if (planIter == 0) hq_OpenSpot_kAt = s;
            int k = hq_OpenSpot_kAt;
            for (int chunkN = 0; chunkN < 150; chunkN++, k += 1)   // so many rounds a step
            {
                if (!(k < s + n)) { PhBreak(); break; }
                int i = roomCells[k];
                if (clr[i] < minClr || (need != 0 && (tags[i] & need) == 0) || (tags[i] & osAvoid) != 0) continue;
                bool tried = false;
                for (int q = 0; q < osTriedN && !tried; q++)
                    if (Abs(osTried[q] % IW - i % IW) <= 1 && Abs(osTried[q] / IW - i / IW) <= 1) tried = true;
                if (tried) continue;
                int c = clr[i] > minClr + 2 ? minClr + 2 : clr[i];   // open enough is enough: do not always sit in the exact middle
                int dd = Abs(i % IW * GenData.INNER_MM + 250 - tx) + Abs(i / IW * GenData.INNER_MM + 250 - tz);
                int score = c * 4000 + (mode == WM_NEAR ? -dd : (mode == WM_FAR ? dd : 0)) + rng.Range(mode == WM_ANY ? 6000 : 400);
                if (score > bestScore) { bestScore = score; best = i; }
            }
            hq_OpenSpot_kAt = k;
            hq_OpenSpot_minClr = minClr;
            hq_OpenSpot_mode = mode;
            hq_OpenSpot_tx = tx;
            hq_OpenSpot_tz = tz;
            hq_OpenSpot_need = need;
            hq_OpenSpot_rng = rng;
            hq_OpenSpot_s = s;
            hq_OpenSpot_n = n;
            hq_OpenSpot_best = best;
            hq_OpenSpot_bestScore = bestScore;
        }
        if (planPhase == 4)
        {
            int minClr = hq_OpenSpot_minClr;
            int room = hq_OpenSpot_room;
            int best = hq_OpenSpot_best;
            osAvoid = 0;
            if (best < 0 && TraceRoom == room) System.Console.Error.WriteLine("  no open spot with clearance " + minClr);
            { hq_OpenSpot_ret = best; hq_OpenSpot_minClr = minClr; hq_OpenSpot_room = room; hq_OpenSpot_best = best; PhEnd(); return; }
            hq_OpenSpot_minClr = minClr;
            hq_OpenSpot_room = room;
            hq_OpenSpot_best = best;
        }
    }

    // A centre piece (a table with its chairs, a fire with its seats) where the room is most open: up to ten
    // spots are tried, with ringWant free cells around the piece first and fewer if no spot has that much.
    // bothYaws: also turned a quarter. A spot where the piece would cut the room's walkway is given up and the
    // next one tried. Returns the emit index of the piece, which stands and is kept, or -1.
    int CentrePiece(int def, int yaw, bool bothYaws, int ringWant, int ringMin, int mode, int tx, int tz, int need, int flags, ref GenRng rng)
    {
        hq_CentrePiece_def = def;
        hq_CentrePiece_yaw = yaw;
        hq_CentrePiece_bothYaws = bothYaws;
        hq_CentrePiece_ringWant = ringWant;
        hq_CentrePiece_ringMin = ringMin;
        hq_CentrePiece_mode = mode;
        hq_CentrePiece_tx = tx;
        hq_CentrePiece_tz = tz;
        hq_CentrePiece_need = need;
        hq_CentrePiece_flags = flags;
        hq_CentrePiece_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); CentrePiece_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_CentrePiece_rng;
        return hq_CentrePiece_ret;
    }

    // CentrePiece in phases, a step a call (autophase4); CentrePiece() above runs them to the end
    void CentrePiece_Body()
    {
        planMax = 1;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int ringWant = hq_CentrePiece_ringWant;
            int ringMin = hq_CentrePiece_ringMin;
            if (!PhSubActive())
            {
                if (planIter == 0) hq_CentrePiece_ring0 = ringWant;
                int ring = hq_CentrePiece_ring0 - planIter;
                if (!(ring >= ringMin)) PhBreak();
                else { hq_CentrePiece_ring = ring; PhSubBegin(); }
            }
            hq_CentrePiece_ringWant = ringWant;
            hq_CentrePiece_ringMin = ringMin;
            if (PhSubActive())
            {
                PhPush(); CentrePiece_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 1)
        {
            osTriedN = 0;
            { hq_CentrePiece_ret = -1; PhEnd(); return; }
        }
    }
    void CentrePiece_L3()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int def = hq_CentrePiece_def;
            int yaw = hq_CentrePiece_yaw;
            int flags = hq_CentrePiece_flags;
            int ring = hq_CentrePiece_ring;
            int cell = hq_CentrePiece_cell;
            int k = hq_CentrePiece_k;
            int e = FreePlace(def, cell, yaw + k * 90, ring, flags, H_FREE);
            hq_CentrePiece_def = def;
            hq_CentrePiece_yaw = yaw;
            hq_CentrePiece_flags = flags;
            hq_CentrePiece_cell = cell;
            hq_CentrePiece_e = e;
        }
        if (planPhase == 1)
        {
            int flags = hq_CentrePiece_flags;
            int e = hq_CentrePiece_e;
            if (e >= 0 && Keep(e, (flags & EF_MANDATORY) != 0))
            {
                osTriedN = 0;
                { hq_CentrePiece_ret = e; hq_CentrePiece_flags = flags; hq_CentrePiece_e = e; phReturn = true; PhEnd(); return; }
            }
            hq_CentrePiece_flags = flags;
            hq_CentrePiece_e = e;
        }
    }

    void CentrePiece_L2()
    {
        planMax = 3;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int mode = hq_CentrePiece_mode;
            int tx = hq_CentrePiece_tx;
            int tz = hq_CentrePiece_tz;
            int need = hq_CentrePiece_need;
            GenRng rng = hq_CentrePiece_rng;
            int cell = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = mode; hq_OpenSpot_tx = tx; hq_OpenSpot_tz = tz; hq_OpenSpot_need = need; hq_OpenSpot_rng = rng; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { rng = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
            hq_CentrePiece_mode = mode;
            hq_CentrePiece_tx = tx;
            hq_CentrePiece_tz = tz;
            hq_CentrePiece_need = need;
            hq_CentrePiece_rng = rng;
            hq_CentrePiece_cell = cell;
        }
        if (planPhase == 1)
        {
            int cell = hq_CentrePiece_cell;
            if (cell < 0) { hq_CentrePiece_cell = cell; phBreak = true; PhEnd(); return; }
            hq_CentrePiece_cell = cell;
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            bool bothYaws = hq_CentrePiece_bothYaws;
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < (bothYaws ? 2 : 1))) PhBreak();
                else { hq_CentrePiece_k = k; PhSubBegin(); }
            }
            hq_CentrePiece_bothYaws = bothYaws;
            if (PhSubActive())
            {
                PhPush(); CentrePiece_L3(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { PhEnd(); return; }
            }
        }
        if (planPhase == 3)
        {
            int cell = hq_CentrePiece_cell;
            if (osTriedN < osTried.Length) osTried[osTriedN++] = cell;
            hq_CentrePiece_cell = cell;
        }
    }

    void CentrePiece_L1()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            osTriedN = 0;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!PhSubActive())
            {
                int attempt = planIter;
                if (!(attempt < 10)) PhBreak();
                else { hq_CentrePiece_attempt = attempt; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); CentrePiece_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { PhEnd(); return; }
            }
        }
    }


    // A centre piece on a cell: turned by yaw, with `ring` free cells around it. H_FREE unless a host is given.
    int FreePlace(int def, int cellIndex, int yaw, int ring, int flags, int host)
    {
        // centred on the cell if the piece spans an odd number of cells that way, on the cell's edge if an even
        // number: its outline then sits in the middle of the cells it takes, with the same margin on both sides
        int hx, hz;
        HalfExtents(def, yaw, out hx, out hz);
        int nx = (2 * hx - 100 + GenData.INNER_MM - 1) / GenData.INNER_MM, nz = (2 * hz - 100 + GenData.INNER_MM - 1) / GenData.INNER_MM;
        int px = cellIndex % IW * GenData.INNER_MM + ((nx & 1) != 0 ? 250 : 0), pz = cellIndex / IW * GenData.INNER_MM + ((nz & 1) != 0 ? 250 : 0);
        return FreePlaceAt(def, px, pz, yaw, ring, flags, host);
    }

    int FreePlaceAt(int def, int px, int pz, int yaw, int ring, int flags, int host)
    {
        int e = StampAt(def, pRoom, px, pz, yaw, flags, -1, host, true);
        if (e < 0) return -1;
        if (ring > 0)
        {
            // the ring is tested with the object's own cells taken out of the picture
            bool ok = true;
            for (int z = lastZ0 - ring; z < lastZ0 + lastGD + ring && ok; z++)
                for (int x = lastX0 - ring; x < lastX0 + lastGW + ring && ok; x++)
                {
                    if (x >= lastX0 && x < lastX0 + lastGW && z >= lastZ0 && z < lastZ0 + lastGD) continue;
                    if (x < 0 || z < 0 || x >= IW || z >= IH) { ok = false; break; }
                    int i = z * IW + x;
                    if ((tags[i] & GenData.T_FLOOR) == 0 || innerRoom[i] != pRoom || occ[i] != 0 || solid[i] > 0) ok = false;
                }
            if (!ok) { Remove(e); return -1; }
        }
        return e;
    }

    // the long side of the room's box: 0 = along x, 1 = along z
    int RoomLongAxis(int room) { return (roomX1[room] - roomX0[room]) >= (roomZ1[room] - roomZ0[room]) ? 0 : 1; }

    int RoomMidX(int room) { return (roomX0[room] + roomX1[room] + 1) * GenData.INNER_MM / 2; }
    int RoomMidZ(int room) { return (roomZ0[room] + roomZ1[room] + 1) * GenData.INNER_MM / 2; }

    // position of the room's k-th doorway (the cell just inside it), mm
    int DoorX(int room, int k) { return roomDoors[roomDoorStart[room] + k] % IW * GenData.INNER_MM + 250; }
    int DoorZ(int room, int k) { return roomDoors[roomDoorStart[room] + k] / IW * GenData.INNER_MM + 250; }

    // the room's first doorway that leads outdoors (for an outdoor room: into a building), or -1
    int ExtDoor(int room)
    {
        for (int k = 0; k < roomDoorCount[room]; k++) if (roomDoorExt[roomDoorStart[room] + k]) return k;
        return -1;
    }

    // =====================================================================================================
    // Keeping a step: chairs and seats of new hosts first, then the walkway test
    // =====================================================================================================
    // Everything placed since emit index `first` stays if the room is still walkable (and, unless the piece is
    // one the room cannot do without, not blocked beyond its cover limit). Returns false if it was taken back.
    bool Keep(int first, bool must)
    {
        hq_Keep_first = first;
        hq_Keep_must = must;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); Keep_Body(); fin = PhAdvance(); PhPop(); }
        return hq_Keep_ret;
    }

    // Keep in phases, a step a call (autophase4); Keep() above runs them to the end
    void Keep_Body()
    {
        planMax = 2;
        if (planPhase == 0)
        {
            int first = hq_Keep_first;
            if (first >= nEmit) { hq_Keep_ret = false; hq_Keep_first = first; PhEnd(); return; }
            for (int h = first; h < nEmit; h++) FillSlots(h, 0);
            int def = emitDef[first];
            hq_Keep_first = first;
            hq_Keep_def = def;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int first = hq_Keep_first;
            bool must = hq_Keep_must;
            bool ok = false;
            if (planIter == 0) { hq_KeepIfWalkable_room = pRoom; hq_KeepIfWalkable_first = first; hq_KeepIfWalkable_exempt = must || coverExempt; PhSubReset(); }
            PhPush(); KeepIfWalkable_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { ok = hq_KeepIfWalkable_ret; PhBreak(); }
            hq_Keep_first = first;
            hq_Keep_must = must;
            hq_Keep_ok = ok;
        }
        if (planPhase == 2)
        {
            int def = hq_Keep_def;
            bool ok = hq_Keep_ok;
            if (TraceRoom == pRoom) System.Console.Error.WriteLine("  keep " + GenData.defId[def] + (ok ? " ok" : " TAKEN BACK (walkway or cover: blocked " + walkBlocked + " of " + roomCoverMax[pRoom] + ")") + " floor " + roomFloor[pRoom] + "/" + roomFloorTarget[pRoom]);
            { hq_Keep_ret = ok; hq_Keep_def = def; hq_Keep_ok = ok; PhEnd(); return; }
            hq_Keep_def = def;
            hq_Keep_ok = ok;
        }
    }

    bool coverExempt;   // FillToMinimum: an area below its minimum may pass its cover limit (never its walkway)

    // debugging aid for the console tools: the room whose plan is traced to the error stream (-1 = none)
    public static int TraceRoom = -1;

    void TakeBack(int first)
    {
        for (int e = nEmit - 1; e >= first; e--)
            if (emitParent[e] < 0) Remove(e);
    }

    // marks what a step placed as the loose remainder
    void MarkLoose(int first)
    {
        for (int e = first; e < nEmit; e++)
            if ((emitFlags[e] & EF_REMOVED) == 0 && OnFloor(e)) { emitFlags[e] |= EF_LOOSE; looseUsed++; }
    }

    // locals of the split plans and helpers, kept from one phase to the next (autophase4)
    int hq_BuildClearance_kAt;
    int hq_BuildClearance_n;
    int hq_BuildClearance_qh;
    int hq_BuildClearance_qt;
    int hq_BuildClearance_room;
    int hq_BuildClearance_s;
    int hq_CentrePiece_attempt;
    bool hq_CentrePiece_bothYaws;
    int hq_CentrePiece_cell;
    int hq_CentrePiece_def;
    int hq_CentrePiece_e;
    int hq_CentrePiece_flags;
    int hq_CentrePiece_k;
    int hq_CentrePiece_mode;
    int hq_CentrePiece_need;
    int hq_CentrePiece_ret;
    int hq_CentrePiece_ring;
    int hq_CentrePiece_ring0;
    int hq_CentrePiece_ringMin;
    int hq_CentrePiece_ringWant;
    GenRng hq_CentrePiece_rng;
    int hq_CentrePiece_tx;
    int hq_CentrePiece_tz;
    int hq_CentrePiece_yaw;
    int hq_Keep_def;
    int hq_Keep_first;
    bool hq_Keep_must;
    bool hq_Keep_ok;
    bool hq_Keep_ret;
    int hq_OpenSpot_best;
    int hq_OpenSpot_bestScore;
    int hq_OpenSpot_kAt;
    int hq_OpenSpot_minClr;
    int hq_OpenSpot_mode;
    int hq_OpenSpot_n;
    int hq_OpenSpot_need;
    int hq_OpenSpot_ret;
    GenRng hq_OpenSpot_rng;
    int hq_OpenSpot_room;
    int hq_OpenSpot_s;
    int hq_OpenSpot_tx;
    int hq_OpenSpot_tz;
    int hq_WallKeep_def;
    int hq_WallKeep_e;
    int hq_WallKeep_first;
    int hq_WallKeep_flags;
    bool hq_WallKeep_ht1;
    int hq_WallKeep_mode;
    int hq_WallKeep_nb;
    int hq_WallKeep_ret;
    GenRng hq_WallKeep_rng;
    int hq_WallKeep_seg;
    int hq_WallKeep_t;
    int hq_WallKeep_tx;
    int hq_WallKeep_tz;
    int hq_WallKeep_win;
}
