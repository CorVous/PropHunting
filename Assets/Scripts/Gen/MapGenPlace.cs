// MapGen, part 3: low-level placement: the emit list, the cells an object takes, free-cell tests, and the
// room loop that hands each room to the plan of its type (MapGenPlans.cs, MapGenOutdoor.cs).
public partial class MapGen
{
    const int SOLID_TOL = 50;   // mm a prop may reach into a cell and leave it walkable (the walking body is 0.4 m, a cell 0.5 m)

    int Emit(int def, int x, int y, int z, int yaw, int parent, int room, int flags)
    {
        if (nEmit >= MAX_EMIT) return -1;
        int e = nEmit++;
        emitDef[e] = def; emitX[e] = x; emitY[e] = y; emitZ[e] = z;
        emitYaw[e] = ((yaw % 360) + 360) % 360;
        emitParent[e] = parent; emitRoom[e] = room; emitFlags[e] = flags;
        emitRX[e] = 0; emitRZ[e] = 0; emitRW[e] = 0; emitRD[e] = 0; emitRot[e] = 0; emitSlot[e] = -1;
        emitGroup[e] = -1; emitGrp[e] = -1; emitSet[e] = -1;
        emitHost[e] = parent >= 0 ? parent : curHost;
        emitDepth[e] = parent < 0 ? 0 : emitDepth[parent] + 1;
        return e;
    }

    // what stands on the floor: placed objects, chairs and seats next to their host, rugs under a table
    public bool OnFloor(int e)
    {
        return emitParent[e] < 0 || (emitFlags[e] & (EF_ADJACENT | EF_UNDER)) != 0;
    }

    // half the size of the def's outline along x and z when turned by yaw (a tree counts as its trunk)
    void HalfExtents(int def, int yaw, out int hx, out int hz)
    {
        if (Has(def, GenData.FL_SCENERY)) { hx = 220; hz = 220; return; }
        int a = ((yaw % 360) + 360) % 360;
        int sn = Abs(sinTab[a]), cs = Abs(sinTab[(a + 90) % 360]);
        int sx = GenData.defSx[def], sz = GenData.defSz[def];
        hx = (sx * cs + sz * sn) / 8192;
        hz = (sx * sn + sz * cs) / 8192;
    }

    // Every object passes through here when it is placed (+1) and when a repair removes it (-1): the room's
    // counts, and the cells its real outline reaches into. Those cells are obstacles for the reachability
    // check and for bots, whatever the size of the prop (G26).
    void Account(int e, int sign)
    {
        int def = emitDef[e], room = emitRoom[e];
        if (Has(def, GenData.FL_DECOR)) return;
        bool floor = OnFloor(e);
        if (Has(def, GenData.FL_SCENERY)) roomTrees[room] += sign;
        else if (floor) roomFloor[room] += sign;
        else roomTop[room] += sign;
        if (!floor || Has(def, GenData.FL_FLAT)) return;   // rugs are walked over
        // the cells the turned outline reaches into by more than the tolerance: the cell's centre lies inside
        // the outline grown by half a cell less the tolerance
        int hx, hz;
        HalfExtents(def, emitYaw[e], out hx, out hz);
        // only cells the turned outline's box reaches into by more than the tolerance (the cells the placement tested)
        int x0 = FloorDiv(emitX[e] - hx + SOLID_TOL, GenData.INNER_MM), x1 = FloorDiv(emitX[e] + hx - SOLID_TOL - 1, GenData.INNER_MM);
        int z0 = FloorDiv(emitZ[e] - hz + SOLID_TOL, GenData.INNER_MM), z1 = FloorDiv(emitZ[e] + hz - SOLID_TOL - 1, GenData.INNER_MM);
        bool tree = Has(def, GenData.FL_SCENERY);
        int grow = GenData.INNER_MM / 2 - SOLID_TOL;
        int lim_x = (tree ? 220 : GenData.defSx[def] / 2) + grow, lim_z = (tree ? 220 : GenData.defSz[def] / 2) + grow;
        int a = tree ? 0 : emitYaw[e], sn = sinTab[a], cs = sinTab[(a + 90) % 360];   // a trunk is a square post whatever way the tree is turned
        int marked = 0;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (x < 0 || z < 0 || x >= IW || z >= IH) continue;
                int dx = x * GenData.INNER_MM + GenData.INNER_MM / 2 - emitX[e], dz = z * GenData.INNER_MM + GenData.INNER_MM / 2 - emitZ[e];
                int lx = (dx * cs - dz * sn) / 4096, lz = (dx * sn + dz * cs) / 4096;
                if (Abs(lx) >= lim_x || Abs(lz) >= lim_z) continue;   // reaching exactly 5 cm in still leaves the cell walkable
                solid[z * IW + x] += sign;
                marked++;
            }
        if (marked == 0)
        {
            // smaller than the tolerance: it still takes its own cell
            int cx = FloorDiv(emitX[e], GenData.INNER_MM), cz = FloorDiv(emitZ[e], GenData.INNER_MM);
            if (cx >= 0 && cz >= 0 && cx < IW && cz < IH) solid[cz * IW + cx] += sign;
        }
    }

    // every cell of the rectangle is free floor of this room
    bool RectFree(int x0, int z0, int gw, int gd, int room, int def) { return RectFree(x0, z0, gw, gd, room, def, -1); }

    // ignore: an emit whose own cells, front clearance and composition set count as free (the host an object
    // is placed in relation to: a coffee table stands in front of its sofa, a run's units stand side by side)
    bool RectFree(int x0, int z0, int gw, int gd, int room, int def, int ignore)
    {
        if (x0 < 0 || z0 < 0 || x0 + gw > IW || z0 + gd > IH) return false;
        bool flat = Has(def, GenData.FL_FLAT);
        for (int z = z0; z < z0 + gd; z++)
            for (int x = x0; x < x0 + gw; x++)
            {
                int i = z * IW + x;
                int t = tags[i];
                if ((t & GenData.T_FLOOR) == 0 || innerRoom[i] != room) return false;
                if ((t & (GenData.T_BLOCKED | GenData.T_NO_SPAWN)) != 0) return false;
                if (flat) { if (flatOcc[i] != 0) return false; continue; }
                if ((t & (GenData.T_DOOR_CLEAR | GenData.T_SWING)) != 0) return false;
                int o = occ[i];
                if (o != 0)
                {
                    if (ignore < 0 || (o != ignore + 1 && emitSet[o - 1] != ignore)) return false;
                }
                else if (solid[i] > 0 && ignore < 0) return false;   // a pulled-out chair or a turned object reaches into the cell
                int rs = reserve[i];
                if (rs != 0 && (ignore < 0 || (rs != ignore + 1 && emitSet[rs - 1] != ignore))) return false;
                // nothing stands on PATH: every prop is solid, so the walkway between a room's doors stays clear
                if ((t & GenData.T_PATH) != 0) return false;
            }
        return true;
    }

    // cells of the k-th row in front of a rectangle (direction rot): origin and size
    void FrontRow(int x0, int z0, int gw, int gd, int rot, int k, out int ax0, out int az0, out int aw, out int ad)
    {
        if (rot == 0) { ax0 = x0; az0 = z0 + gd - 1 + k; aw = gw; ad = 1; }
        else if (rot == 2) { ax0 = x0; az0 = z0 - k; aw = gw; ad = 1; }
        else if (rot == 1) { ax0 = x0 + gw - 1 + k; az0 = z0; aw = 1; ad = gd; }
        else { ax0 = x0 - k; az0 = z0; aw = 1; ad = gd; }
    }

    // the strip of `depth` cells in front of the rectangle is open floor
    bool FrontFree(int x0, int z0, int gw, int gd, int rot, int depth)
    {
        for (int k = 1; k <= depth; k++)
        {
            int ax0, az0, aw, ad;
            FrontRow(x0, z0, gw, gd, rot, k, out ax0, out az0, out aw, out ad);
            for (int z = az0; z < az0 + ad; z++)
                for (int x = ax0; x < ax0 + aw; x++)
                {
                    if (x < 0 || z < 0 || x >= IW || z >= IH) return false;
                    int i = z * IW + x;
                    if ((tags[i] & GenData.T_FLOOR) == 0 || occ[i] != 0) return false;
                }
        }
        return true;
    }

    void ReserveFront(int x0, int z0, int gw, int gd, int rot, int depth, bool swing, int owner)
    {
        for (int k = 1; k <= depth; k++)
        {
            int ax0, az0, aw, ad;
            FrontRow(x0, z0, gw, gd, rot, k, out ax0, out az0, out aw, out ad);
            for (int z = az0; z < az0 + ad; z++)
                for (int x = ax0; x < ax0 + aw; x++)
                {
                    if (x < 0 || z < 0 || x >= IW || z >= IH) continue;
                    int i = z * IW + x;
                    if (reserve[i] == 0) reserve[i] = owner + 1;
                    if (swing) tags[i] |= GenData.T_SWING;
                }
        }
    }

    // a free ring of `depth` cells around the rectangle: room for the chairs of a table and for passing behind
    // them (report 15, K3: 0.81 to 1.12 m behind a seat)
    bool RingFree(int x0, int z0, int gw, int gd, int room, int depth)
    {
        for (int z = z0 - depth; z < z0 + gd + depth; z++)
            for (int x = x0 - depth; x < x0 + gw + depth; x++)
            {
                if (x >= x0 && x < x0 + gw && z >= z0 && z < z0 + gd) continue;
                if (x < 0 || z < 0 || x >= IW || z >= IH) return false;
                int i = z * IW + x;
                if ((tags[i] & GenData.T_FLOOR) == 0 || innerRoom[i] != room || occ[i] != 0 || solid[i] > 0) return false;
            }
        return true;
    }

    bool SpacingOk(int def, int x0, int z0)
    {
        int sp = GenData.defSpacing[def];
        if (sp <= 0) return true;
        bool scenery = Has(def, GenData.FL_SCENERY);
        for (int e = 0; e < nEmit; e++)
        {
            int d2 = emitDef[e];
            if (d2 != def && !(scenery && Has(d2, GenData.FL_SCENERY))) continue;
            if (emitParent[e] >= 0 || (emitFlags[e] & EF_REMOVED) != 0) continue;
            if (Abs(emitRX[e] - x0) < sp && Abs(emitRZ[e] - z0) < sp) return false;
        }
        return true;
    }

    bool HasChairSlots(int def)
    {
        int s = GenData.defSlotStart[def], n = GenData.defSlotCount[def];
        for (int i = s; i < s + n; i++)
            if (GenData.slotType[i] == GenData.S_ADJACENT) return true;
        return false;
    }

    int StampObject(int def, int room, int x0, int z0, int gw, int gd, int rot, int px, int pz, int yaw, int flags)
    {
        return StampObject(def, room, x0, z0, gw, gd, rot, px, pz, yaw, flags, true);
    }

    // reserveFront = false: the caller reserves the front clearance itself, once a whole run stands
    int StampObject(int def, int room, int x0, int z0, int gw, int gd, int rot, int px, int pz, int yaw, int flags, bool reserveFront)
    {
        // a broadleaf tree takes the map's season (its twin has the same size)
        if (season > 0 && GenData.defSeason[def * 3 + season] >= 0) def = GenData.defSeason[def * 3 + season];
        // the whole object, not only the cells its base stands on, has to be clear of walls and fences (G31)
        if (!ExtentClear(def, px, pz, yaw, -1)) return -1;
        if (!NeighboursClear(def, px, pz, yaw, x0, z0, gw, gd, -1)) return -1;
        int e = Emit(def, px, 0, pz, yaw, -1, room, flags);
        if (e < 0) return -1;
        if (GenData.defCanopyY[def] > 0) StampCanopy(e);
        emitRX[e] = x0; emitRZ[e] = z0; emitRW[e] = gw; emitRD[e] = gd; emitRot[e] = rot;
        Account(e, 1);
        bool flat = Has(def, GenData.FL_FLAT);
        bool decor = Has(def, GenData.FL_DECOR);
        for (int z = z0; z < z0 + gd; z++)
            for (int x = x0; x < x0 + gw; x++)
            {
                if (flat) flatOcc[z * IW + x] = e + 1;
                else if (!decor && occ[z * IW + x] == 0) occ[z * IW + x] = e + 1;
            }
        int clear = GenData.defClear[def];
        if (clear > 0 && reserveFront) ReserveFront(x0, z0, gw, gd, rot, clear, Has(def, GenData.FL_OPENABLE), e);
        return e;
    }

    int Jitter(ref GenRng rng, int range) { return range <= 0 ? 0 : rng.Between(-range, range); }

    // the tag a cell needs for an anchor
    static int NeedTags(int anchor)
    {
        switch (anchor)
        {
            case GenData.A_WALL: return GenData.T_WALL_ANY;
            case GenData.A_OUT_EDGE: return GenData.T_EDGE_OUTDOOR;
            case GenData.A_DOOR_SIDE: return GenData.T_DOOR_SIDE;
            case GenData.A_BUILDING: return GenData.T_BY_BUILDING;
            case GenData.A_CORNER: return GenData.T_CORNER;
            case GenData.A_CENTER: return GenData.T_CENTER;
            case GenData.A_SOFT: return GenData.T_SOFT_GROUND;
            case GenData.A_COVERED: return GenData.T_COVERED;
        }
        return GenData.T_FLOOR;
    }

    // anchors where the object's back stands against a wall, a fence or a building
    static bool WallAnchor(int anchor)
    {
        return anchor == GenData.A_WALL || anchor == GenData.A_OUT_EDGE || anchor == GenData.A_DOOR_SIDE || anchor == GenData.A_BUILDING || anchor == GenData.A_CORNER;
    }

    // 04 section 4.3.3 tryPlace: up to 24 shuffled cells matching the anchor. Returns the emit index or -1.
    int TryPlace(int def, int room, int anchor, int flags, ref GenRng rng)
    {
        int w = GenData.defW[def], d = GenData.defD[def];
        int sx = GenData.defSx[def], sz = GenData.defSz[def];
        bool tall = GenData.defSy[def] >= 1200;
        bool flatDef = Has(def, GenData.FL_FLAT) || Has(def, GenData.FL_DECOR);
        bool fixture = Has(def, GenData.FL_FIXTURE) && anchor == GenData.A_WALL;
        int s = roomCellStart[room], n = roomCellCount[room];
        int need = NeedTags(anchor);
        bool wallAnchor = WallAnchor(anchor) && anchor != GenData.A_CORNER;
        int nc = 0;
        for (int k = s; k < s + n && nc < cand.Length; k++)
        {
            int i = roomCells[k];
            int t = tags[i];
            if ((t & need) == 0) continue;
            if (anchor == GenData.A_OUT_OPEN && ((t & GenData.T_OUTDOOR) == 0 || (t & GenData.T_WALL_ANY) != 0)) continue;
            if (!flatDef && occ[i] != 0) continue;
            cand[nc++] = i;
        }
        if (nc == 0) return -1;
        Shuffle(cand, nc, ref rng);
        if (fixture)
        {
            // kitchen fixtures form a run: cells next to an earlier fixture are tried first
            int nPri = 0;
            for (int k = 0; k < nc; k++)
            {
                int i = cand[k], ix = i % IW, iz = i / IW;
                bool near = false;
                for (int dd = 0; dd < 4 && !near; dd++)
                {
                    int jx = ix + DX[dd], jz = iz + DZ[dd];
                    if (jx < 0 || jz < 0 || jx >= IW || jz >= IH) continue;
                    int o = occ[jz * IW + jx];
                    if (o > 0 && Has(emitDef[o - 1], GenData.FL_FIXTURE)) near = true;
                }
                if (near) { int t2 = cand[nPri]; cand[nPri] = cand[k]; cand[k] = t2; nPri++; }
            }
        }
        int tries = nc < 24 ? nc : 24;
        for (int k = 0; k < tries; k++)
        {
            int i = cand[k], cx = i % IW, cz = i / IW, t = tags[i];
            if (wallAnchor)
            {
                int d0 = rng.Range(4);
                for (int di = 0; di < 4; di++)
                {
                    int wd = (d0 + di) & 3;
                    if ((t & WallBit(wd)) == 0) continue;
                    int rot = (wd + 2) & 3;                    // front faces away from the wall
                    int gw = (rot & 1) == 0 ? w : d, gd = (rot & 1) == 0 ? d : w;
                    int span = (rot & 1) == 0 ? gw : gd;       // cells along the wall
                    int o0 = rng.Range(span);
                    for (int oi = 0; oi < span; oi++)
                    {
                        int o = (o0 + oi) % span;
                        int x0, z0;
                        if (wd == 2) { x0 = cx - o; z0 = cz; }
                        else if (wd == 0) { x0 = cx - o; z0 = cz - gd + 1; }
                        else if (wd == 3) { x0 = cx; z0 = cz - o; }
                        else { x0 = cx - gw + 1; z0 = cz - o; }
                        if (!RectFree(x0, z0, gw, gd, room, def)) continue;
                        // the whole back row must stand against the wall; nothing tall in front of a window
                        bool ok = true;
                        for (int a = 0; a < span && ok; a++)
                        {
                            int bx = (wd == 2 || wd == 0) ? x0 + a : (wd == 3 ? x0 : x0 + gw - 1);
                            int bz = (wd == 2) ? z0 : (wd == 0 ? z0 + gd - 1 : z0 + a);
                            int bt = tags[bz * IW + bx];
                            if ((bt & WallBit(wd)) == 0) ok = false;
                            if (tall && (bt & GenData.T_WINDOW_WALL) != 0) ok = false;
                        }
                        if (!ok) continue;
                        int clear = GenData.defClear[def];
                        if (clear > 0 && !FrontFree(x0, z0, gw, gd, rot, clear)) continue;
                        if (!SpacingOk(def, x0, z0)) continue;
                        // position: centre of the rectangle along the wall, back face 2 cm off the wall surface
                        int px = (2 * x0 + gw) * GenData.INNER_MM / 2, pz = (2 * z0 + gd) * GenData.INNER_MM / 2;
                        int yaw = rot * 90 + Jitter(ref rng, Min(GenData.defJitYaw[def], 4));
                        int fhx, fhz;
                        FullHalf(def, yaw, out fhx, out fhz);
                        int halfAlong = (wd == 0 || wd == 2) ? fhx : fhz, halfDeep = (wd == 0 || wd == 2) ? fhz : fhx;
                        int slack = span * GenData.INNER_MM / 2 - halfAlong - 20;
                        int jit = Jitter(ref rng, Min(slack < 0 ? 0 : slack, GenData.defJitPos[def] * 10));
                        int off = GenData.WALL_HALF_MM + 20 + halfDeep;
                        if (wd == 2) { pz = z0 * GenData.INNER_MM + off; px += jit; }
                        else if (wd == 0) { pz = (z0 + gd) * GenData.INNER_MM - off; px += jit; }
                        else if (wd == 3) { px = x0 * GenData.INNER_MM + off; pz += jit; }
                        else { px = (x0 + gw) * GenData.INNER_MM - off; pz += jit; }
                        curHost = H_WALL;
                        int placed = StampObject(def, room, x0, z0, gw, gd, rot, px, pz, yaw, flags);
                        if (placed >= 0) return placed;
                    }
                }
            }
            else if (anchor == GenData.A_CORNER)
            {
                bool wn = (t & GenData.T_WALL_N) != 0, we = (t & GenData.T_WALL_E) != 0;
                bool ws = (t & GenData.T_WALL_S) != 0, ww = (t & GenData.T_WALL_W) != 0;
                if (!(wn || ws) || !(we || ww)) continue;
                int wz = ws ? 2 : 0, wx = ww ? 3 : 1;          // the two walls of the corner
                bool pickZ = rng.Chance(500);
                int turn = rng.Between(10, 40);
                int rot = pickZ ? ((wz + 2) & 3) : ((wx + 2) & 3);
                int gw = (rot & 1) == 0 ? w : d, gd = (rot & 1) == 0 ? d : w;
                int x0 = wx == 3 ? cx : cx - gw + 1, z0 = wz == 2 ? cz : cz - gd + 1;
                if (!RectFree(x0, z0, gw, gd, room, def)) continue;
                if (tall && (t & GenData.T_WINDOW_WALL) != 0) continue;
                if (!SpacingOk(def, x0, z0)) continue;
                // turned a little towards the middle of the room; the turned outline keeps clear of both walls
                int towardX = wx == 3 ? 1 : -1, towardZ = wz == 2 ? 1 : -1;
                int sign = pickZ ? towardX * towardZ : -towardX * towardZ;
                int cyaw = rot * 90 + sign * turn;
                int ex, ez;
                FullHalf(def, cyaw, out ex, out ez);
                int gap = GenData.WALL_HALF_MM + 30;
                int px = wx == 3 ? x0 * GenData.INNER_MM + gap + ex : (x0 + gw) * GenData.INNER_MM - gap - ex;
                int pz = wz == 2 ? z0 * GenData.INNER_MM + gap + ez : (z0 + gd) * GenData.INNER_MM - gap - ez;
                curHost = H_WALL;
                int placed = StampObject(def, room, x0, z0, gw, gd, rot, px, pz, cyaw, flags);
                if (placed >= 0) return placed;
                continue;
            }
            else
            {
                int rot;
                int r4 = rng.Range(4);
                if (GenData.defFacing[def] == GenData.F_ALIGN_GRID)
                {
                    // the object's long side follows the room's long side
                    int rw = roomX1[room] - roomX0[room], rd = roomZ1[room] - roomZ0[room];
                    bool roomWide = rw >= rd, defWide = w >= d;
                    rot = (roomWide == defWide ? 0 : 1) + ((r4 & 1) != 0 ? 2 : 0);
                }
                else if (GenData.defFacing[def] == GenData.F_TO_CENTER)
                {
                    int mx = (roomX0[room] + roomX1[room]) / 2, mz = (roomZ0[room] + roomZ1[room]) / 2;
                    int ddx = mx - cx, ddz = mz - cz;
                    rot = Abs(ddx) > Abs(ddz) ? (ddx > 0 ? 1 : 3) : (ddz > 0 ? 0 : 2);
                }
                else rot = r4;
                int gw = (rot & 1) == 0 ? w : d, gd = (rot & 1) == 0 ? d : w;
                int x0 = anchor == GenData.A_CENTER ? cx - gw / 2 : cx, z0 = anchor == GenData.A_CENTER ? cz - gd / 2 : cz;
                int px = (2 * x0 + gw) * GenData.INNER_MM / 2, pz = (2 * z0 + gd) * GenData.INNER_MM / 2;
                int jr = GenData.defJitPos[def] * 10;
                px += Jitter(ref rng, jr); pz += Jitter(ref rng, jr);
                int yaw = rot * 90 + Jitter(ref rng, GenData.defJitYaw[def]);
                if (!flatDef)
                {
                    // the cells the object really covers once it is turned and shifted, so that a turned tent
                    // or log never reaches through a wall or into the next room
                    int hx, hz;
                    HalfExtents(def, yaw, out hx, out hz);
                    x0 = FloorDiv(px - hx + SOLID_TOL, GenData.INNER_MM); z0 = FloorDiv(pz - hz + SOLID_TOL, GenData.INNER_MM);
                    int x1 = FloorDiv(px + hx - SOLID_TOL - 1, GenData.INNER_MM), z1 = FloorDiv(pz + hz - SOLID_TOL - 1, GenData.INNER_MM);
                    if (x1 < x0) { x0 = FloorDiv(px, GenData.INNER_MM); x1 = x0; }
                    if (z1 < z0) { z0 = FloorDiv(pz, GenData.INNER_MM); z1 = z0; }
                    gw = x1 - x0 + 1; gd = z1 - z0 + 1;
                }
                if (!RectFree(x0, z0, gw, gd, room, def)) continue;
                if (HasChairSlots(def) && !RingFree(x0, z0, gw, gd, room, 1)) continue;
                int clear = GenData.defClear[def];
                if (clear > 0 && !FrontFree(x0, z0, gw, gd, rot, clear)) continue;
                if (!SpacingOk(def, x0, z0)) continue;
                curHost = SceneryOrDecor(def) ? H_NATURE : H_FREE;
                int placed = StampObject(def, room, x0, z0, gw, gd, rot, px, pz, yaw, flags);
                if (placed >= 0) return placed;
            }
        }
        return -1;
    }

    // A cap written for a 16 m2 room, for this room: larger rooms hold more, and a density above Normal loosens
    // the caps that allow more than one, so the higher floor targets can be met.
    public int ScaledCap(int cap, int room)
    {
        int c = cap * (3 + roomOuterCells[room]) / 4;
        int pct = GenData.DensityPercent[density];
        if (cap > 1 && pct > 100) c = (c * pct + 50) / 100;
        return c < 1 ? 1 : c;
    }

    // a number per 100 m2 of the range table, for this room's floor area (4 inner cells are 1 m2)
    int PerArea(int per100, int room, bool scaled)
    {
        int pct = scaled ? GenData.DensityPercent[density] : 100;
        return (per100 * roomCellCount[room] * pct + 20000) / 40000;
    }

    // copies of a type standing on the floor of a room
    int CountInRoom(int def, int room)
    {
        int n = 0;
        for (int e = roomEmitStart[room]; e < nEmit; e++)
            if (emitDef[e] == def && emitRoom[e] == room && (emitFlags[e] & EF_REMOVED) == 0 && OnFloor(e)) n++;
        return n;
    }

    int[] roomEmitStart = new int[400];   // every emit of a room has an index at or above this

    static bool SceneryOrDecor(int def)
    {
        return (GenData.defFlags[def] & (GenData.FL_SCENERY | GenData.FL_DECOR)) != 0;
    }

    // Places one single def by its "where" rule. Returns the emit index or -1.
    int PlaceSingle(int def, int room, int anchor, int flags, ref GenRng rng)
    {
        if (anchor == GenData.A_SLOT_ONLY) anchor = GenData.A_WALL;
        // a small loose thing on its own keeps to a wall, a fence or a neighbour, whatever its table says (G25)
        if (Has(def, GenData.FL_LOOSE) && !WallAnchor(anchor) && anchor != GenData.A_BESIDE) anchor = GenData.A_WALL;
        if (anchor == GenData.A_BESIDE)
        {
            fN = 1; fDef[0] = def;
            int e = PlaceFormationBeside(GenData.GL_ROW, 0, GenData.defBeside[def], room, flags, -1, ref rng);
            if (e >= 0) return e;
            anchor = GenData.A_WALL;   // nothing to stand beside: a wall or the fence, never alone in the open
        }
        return TryPlace(def, room, anchor, flags, ref rng);
    }

    // One room: its numbers from the range table (G24), then the plan of its type composes it (G28), then the
    // things that sit on, in and under the furniture.
    void PopulateRoom(int room)
    {
        roomEmitStart[room] = nEmit;
        pRoom = room; looseUsed = 0; borderColour = 0;   // nothing carries over from the last room or the last map
        int type = roomType[room];
        if (type == GenData.R_RELEASE || GenData.FindTable(roomZone[room], type) < 0) return;
        GenRng rng = Rng(ST_ROOM, room, 0);
        // the room's floor budget inside its type's range, for its floor area and the map's density
        int lo = PerArea(GenData.rangeFloorMin[type], room, true);
        roomFloorMax[room] = PerArea(GenData.rangeFloorMax[type], room, true);
        roomFloorTarget[room] = rng.Between(lo, roomFloorMax[room]);
        // an outdoor area is never bare (G29): its type's minimum per area, with the density setting but never below 4
        int abs = GenData.rangeFloorAbs[type];
        roomFloorMin[room] = 0;
        if (abs > 0)
        {
            abs = (abs * GenData.DensityPercent[density] + 50) / 100;
            if (abs < 4) abs = 4;
            // an area of one cell (a patio at a back door, a fire pit in a back garden: G32) needs 4 at most
            if (roomOuterCells[room] == 1 && abs > 4) abs = 4;
            if (roomFloorTarget[room] < abs) roomFloorTarget[room] = abs;
            if (roomFloorMax[room] < abs) roomFloorMax[room] = abs;
            // the area's minimum: its type's number per 100 m2, and never fewer than the type's fewest per area
            roomFloorMin[room] = lo > abs ? lo : abs;
        }
        roomTopCap[room] = PerArea(GenData.rangeTopMax[type], room, true);
        int cover = GenData.rangeCover[type] * GenData.DensityPercent[density] / 100;
        roomCoverMax[room] = roomCellCount[room] * (cover > 70 ? 70 : cover) / 100;
        int firstEmit = nEmit;
        popFirst = firstEmit;
        BuildSegs(room);
        RoomWalkable(room);
        planPhase = 0; planIter = 0; planBroken = false;
        PhResetAll();
    }

    // A plan in phases (Cor: build freezes). A plan that marks its parts with Ph(k) (once) and PhLoop(k, n) (n
    // times, planIter counting) runs one part a call; one that marks none runs whole on its first call. The parts
    // run in the same order as the plan's code, with the same random streams, so a map comes out the same.
    // Returns true when the plan is done.
    int planPhase, planMax, planIter, planN, planLoopN, planHX, planHZ, planA, planB;
    bool planBroken, planLooped;
    GenRng planR;

    bool Ph(int k)
    {
        if (k > planMax) planMax = k;
        return k == planPhase;
    }

    bool PhLoop(int k, int n)
    {
        if (k > planMax) planMax = k;
        if (k != planPhase) return false;
        planLooped = true; planLoopN = n;
        return planIter < n && !planBroken;
    }

    void PhBreak() { planBroken = true; }

    // a plan's `return`: the plan is over, whatever phases follow
    void PhEnd() { planBroken = true; planPhase = 100000; }

    bool PlanStep(int room)
    {
        int type = roomType[room];
        planMax = 0; planLooped = false;
        switch (type)
        {
            case GenData.R_LIVING: PlanLiving(); break;
            case GenData.R_DINING: PlanDining(); break;
            case GenData.R_KITCHEN: PlanKitchen(); break;
            case GenData.R_BEDROOM: PlanBedroom(); break;
            case GenData.R_KIDS: PlanKids(); break;
            case GenData.R_BATH: PlanBath(); break;
            case GenData.R_OFFICE: PlanOffice(false); break;
            case GenData.R_HALL: PlanHall(); break;
            case GenData.R_STORAGE: PlanStorage(); break;
            case GenData.R_LOADING: PlanLoading(); break;
            case GenData.R_WH_OFFICE: PlanOffice(true); break;
            case GenData.R_DINER_DINING: PlanDinerDining(); break;
            case GenData.R_DINER_KITCHEN: PlanDinerKitchen(); break;
            case GenData.R_DINER_STORE: PlanDinerStore(); break;
            case GenData.R_LAWN_OPEN: PlanLawn(); break;
            case GenData.R_GARDEN: PlanGarden(); break;
            case GenData.R_PATIO: PlanPatio(); break;
            case GenData.R_FIREPIT: PlanFirepit(); break;
            case GenData.R_FOREST_DENSE: PlanForest(false); break;
            case GenData.R_FOREST_CLEARING: PlanForest(true); break;
            case GenData.R_LANE: PlanLane(); break;
            case GenData.R_YARD: PlanYard(); break;
            // revision 9
            case GenData.R_SHOP_FLOOR: PlanShopFloor(); break;
            case GenData.R_STOCKROOM: PlanStockroom(); break;
            case GenData.R_BACK_OFFICE: PlanOffice(true); break;
            case GenData.R_GARAGE_BAY: PlanGarageBay(); break;
            case GenData.R_PARTS: PlanParts(); break;
            case GenData.R_BARN: PlanBarn(); break;
            case GenData.R_FEED_STORE: PlanFeedStore(); break;
            case GenData.R_FORECOURT: PlanForecourt(); break;
            case GenData.R_FARMYARD: PlanFarmyard(); break;
            case GenData.R_FIELD: PlanField(); break;
            case GenData.R_PADDOCK: PlanPaddock(); break;
            case GenData.R_PARK: PlanPark(); break;
            case GenData.R_FARM_WORK: PlanFarmWork(); break;
            case GenData.R_PLAYGROUND: PlanPlayground(); break;
            case GenData.R_POND: PlanPond(); break;
            case GenData.R_TERRACE: PlanTerrace(); break;
            case GenData.R_TEEN: PlanTeen(); break;
            case GenData.R_PLAYROOM: PlanPlayroom(); break;
            case GenData.R_BOXROOM: PlanBoxRoom(); break;
            case GenData.R_PANTRY: PlanPantry(); break;
            // revision 12
            case GenData.R_ZOO_PLAZA: PlanZooPlaza(); break;
            case GenData.R_ZOO_WALK: PlanZooWalk(); break;
            case GenData.R_ENC_FARM: case GenData.R_ENC_SAVANNA: case GenData.R_ENC_ARCTIC: case GenData.R_ENC_WOODS: case GenData.R_ENC_JUNGLE: case GenData.R_AVIARY: PlanEnclosure(); break;
            case GenData.R_KEEPER: PlanKeeper(); break;
            case GenData.R_CABIN_MAIN: PlanCabinMain(); break;
            case GenData.R_BUNKROOM: PlanBunkRoom(); break;
            case GenData.R_PORCH: PlanPorch(); break;
            case GenData.R_WOODYARD: PlanWoodyard(); break;
            case GenData.R_CAMPSITE: PlanCampsite(); break;
            case GenData.R_ARCADE: PlanArcade(); break;
            case GenData.R_SNACKBAR: PlanSnackbar(); break;
            case GenData.R_PRIZE_STORE: PlanPrizeStore(); break;
            case GenData.R_MIDWAY: PlanMidway(); break;
            case GenData.R_RIDES: PlanRides(); break;
            case GenData.R_NAVE: PlanNave(); break;
            case GenData.R_VESTRY: PlanVestry(); break;
            case GenData.R_CHURCHYARD: PlanChurchyard(); break;
            case GenData.R_GRAVEYARD: PlanGraveyard(); break;
        }
        return PhAdvance();
    }

    // The end of a call in phases: the loop phase goes on to its next round, or the next phase is due. True when the
    // last phase is done.
    bool PhAdvance()
    {
        if (planStay) { planStay = false; return false; }
        if (planLooped && !planBroken && planIter + 1 < planLoopN) { planIter++; return false; }
        planPhase++; planIter = 0; planBroken = false;
        return planPhase > planMax;
    }

    // Phases a level down (Cor: build freezes): a loop's body, or a helper, that is itself in phases runs between
    // PhPush and PhPop with phase counters of its own; the caller's wait in phsX meanwhile. A level's phase and
    // round are kept in phsPhase/phsIter from one step to the next.
    int phD;
    bool planStay, phReturn, phBreak;
    int[] phsPhase = new int[12], phsIter = new int[12], phsMax = new int[12], phsLoopN = new int[12];
    bool[] phsBroken = new bool[12], phsLooped = new bool[12], phsStay = new bool[12], phsActive = new bool[12];

    void PhPush()
    {
        phsPhase[phD] = planPhase; phsIter[phD] = planIter; phsMax[phD] = planMax; phsLoopN[phD] = planLoopN;
        phsBroken[phD] = planBroken; phsLooped[phD] = planLooped; phsStay[phD] = planStay;
        phD++;
        planPhase = phsPhase[phD]; planIter = phsIter[phD];
        planMax = 0; planLoopN = 0; planBroken = false; planLooped = false; planStay = false;
    }

    void PhPop()
    {
        phsPhase[phD] = planPhase; phsIter[phD] = planIter;
        phD--;
        planPhase = phsPhase[phD]; planIter = phsIter[phD]; planMax = phsMax[phD]; planLoopN = phsLoopN[phD];
        planBroken = phsBroken[phD]; planLooped = phsLooped[phD]; planStay = phsStay[phD];
    }

    // the level below: from its first phase / is a loop's body under way / a body begins, ends / the caller's
    // round is not over yet
    void PhSubReset() { phsPhase[phD + 1] = 0; phsIter[phD + 1] = 0; phsActive[phD + 2] = false; }
    bool PhSubActive() { return phsActive[phD + 1]; }
    void PhSubBegin() { PhSubReset(); phsActive[phD + 1] = true; }
    void PhSubEnd() { phsActive[phD + 1] = false; }
    void PhStay() { planStay = true; }

    void PhResetAll()
    {
        phD = 0; planStay = false; phReturn = false; phBreak = false;
        for (int i = 0; i < phsActive.Length; i++) phsActive[i] = false;
    }

    // The rest of a room's furnishing, a piece a step (Cor: build freezes), in the order PopulateRoom used to do it
    // all at once: the holiday's decorations, the theme's, the outdoor minimum, then the slots, one host a step.
    // Returns true when the room is done.
    int popFirst, popPhase, popHost;

    bool PopulateRoomStep(int room)
    {
        pRoom = room;
        int type = roomType[room];
        if (popPhase == 0)
        {
            popPhase = 10;
            if (type == GenData.R_RELEASE || GenData.FindTable(roomZone[room], type) < 0) return true;
            PopulateRoom(room);
            return false;
        }
        if (popPhase == 10) { if (PlanStep(room)) popPhase = 1; return false; }
        if (popPhase == 1) { popPhase = 2; if (holiday != 0) HolidayDecor(); return false; }
        if (popPhase == 2) { if (!(mapTheme != GenData.THEME_NONE && type != GenData.R_RELEASE && type != GenData.R_LANE) || ThemeDecorStep()) { popPhase = 3; tdK = -1; } return false; }
        if (popPhase == 3) { if (roomFloorMin[room] <= 0 || FillToMinimumStep()) { popPhase = 4; ftmK = -1; } return false; }
        if (popPhase == 4)
        {
            // what sits on, in and under the furniture, thinned evenly to the room's cap
            SetTopFactor(room, popFirst);
            popHost = popFirst; popPhase = 5;
            return false;
        }
        // one host's slots a step (the hosts this room has so far; slots add none that hold slots of their own)
        int guard = 0;
        while (popHost < nEmit && guard < 4)
        {
            int h = popHost++;
            if (emitRoom[h] != room) continue;
            FillSlots(h, 1);
            guard++;
        }
        return popHost >= nEmit;
    }
}
