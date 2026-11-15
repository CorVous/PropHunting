// MapGen, part 3: placing objects in rooms from the spawn tables (04 section 4.3.3), up to each room type's
// range of props (G24). MapGenGroups.cs places prop groups and things that stand beside other things (G25).
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
        emitGroup[e] = -1; emitGrp[e] = -1;
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
        int x0 = FloorDiv(emitX[e] - hx, GenData.INNER_MM), x1 = FloorDiv(emitX[e] + hx, GenData.INNER_MM);
        int z0 = FloorDiv(emitZ[e] - hz, GenData.INNER_MM), z1 = FloorDiv(emitZ[e] + hz, GenData.INNER_MM);
        bool tree = Has(def, GenData.FL_SCENERY);
        int grow = GenData.INNER_MM / 2 - SOLID_TOL;
        int lim_x = (tree ? 220 : GenData.defSx[def] / 2) + grow, lim_z = (tree ? 220 : GenData.defSz[def] / 2) + grow;
        int a = emitYaw[e], sn = sinTab[a], cs = sinTab[(a + 90) % 360];
        int marked = 0;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (x < 0 || z < 0 || x >= IW || z >= IH) continue;
                int dx = x * GenData.INNER_MM + GenData.INNER_MM / 2 - emitX[e], dz = z * GenData.INNER_MM + GenData.INNER_MM / 2 - emitZ[e];
                int lx = (dx * cs - dz * sn) / 4096, lz = (dx * sn + dz * cs) / 4096;
                if (Abs(lx) > lim_x || Abs(lz) > lim_z) continue;
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

    // ignore: an emit whose own cells count as free (the host a group stands right next to)
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
                if ((occ[i] != 0 && occ[i] != ignore + 1) || reserve[i] != 0) return false;
                // nothing stands on PATH: every prop is solid now, so the walkway between a room's doors stays clear
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

    void ReserveFront(int x0, int z0, int gw, int gd, int rot, int depth, bool swing)
    {
        for (int k = 1; k <= depth; k++)
        {
            int ax0, az0, aw, ad;
            FrontRow(x0, z0, gw, gd, rot, k, out ax0, out az0, out aw, out ad);
            for (int z = az0; z < az0 + ad; z++)
                for (int x = ax0; x < ax0 + aw; x++)
                {
                    int i = z * IW + x;
                    reserve[i] = 1;
                    if (swing) tags[i] |= GenData.T_SWING;
                }
        }
    }

    // a free ring of one cell around the rectangle (for the chairs of a table)
    bool RingFree(int x0, int z0, int gw, int gd, int room)
    {
        for (int z = z0 - 1; z <= z0 + gd; z++)
            for (int x = x0 - 1; x <= x0 + gw; x++)
            {
                if (x >= x0 && x < x0 + gw && z >= z0 && z < z0 + gd) continue;
                if (x < 0 || z < 0 || x >= IW || z >= IH) return false;
                int i = z * IW + x;
                if ((tags[i] & GenData.T_FLOOR) == 0 || innerRoom[i] != room || occ[i] != 0) return false;
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
        int e = Emit(def, px, 0, pz, yaw, -1, room, flags);
        if (e < 0) return -1;
        emitRX[e] = x0; emitRZ[e] = z0; emitRW[e] = gw; emitRD[e] = gd; emitRot[e] = rot;
        Account(e, 1);
        bool flat = Has(def, GenData.FL_FLAT);
        bool decor = Has(def, GenData.FL_DECOR);
        for (int z = z0; z < z0 + gd; z++)
            for (int x = x0; x < x0 + gw; x++)
            {
                if (flat) flatOcc[z * IW + x] = e + 1;
                else if (!decor) occ[z * IW + x] = e + 1;
            }
        int clear = GenData.defClear[def];
        if (clear > 0) ReserveFront(x0, z0, gw, gd, rot, clear, Has(def, GenData.FL_OPENABLE));
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
                        int slack = (span * GenData.INNER_MM - sx) / 2 - 20;
                        int jit = Jitter(ref rng, Min(slack < 0 ? 0 : slack, GenData.defJitPos[def] * 10));
                        int off = GenData.WALL_HALF_MM + 20 + sz / 2;
                        if (wd == 2) { pz = z0 * GenData.INNER_MM + off; px += jit; }
                        else if (wd == 0) { pz = (z0 + gd) * GenData.INNER_MM - off; px += jit; }
                        else if (wd == 3) { px = x0 * GenData.INNER_MM + off; pz += jit; }
                        else { px = (x0 + gw) * GenData.INNER_MM - off; pz += jit; }
                        int yaw = rot * 90 + Jitter(ref rng, Min(GenData.defJitYaw[def], 4));
                        return StampObject(def, room, x0, z0, gw, gd, rot, px, pz, yaw, flags);
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
                int ex = ((rot & 1) == 0 ? sx : sz) / 2, ez = ((rot & 1) == 0 ? sz : sx) / 2;
                int gap = GenData.WALL_HALF_MM + 40;
                int px = wx == 3 ? x0 * GenData.INNER_MM + gap + ex : (x0 + gw) * GenData.INNER_MM - gap - ex;
                int pz = wz == 2 ? z0 * GenData.INNER_MM + gap + ez : (z0 + gd) * GenData.INNER_MM - gap - ez;
                // turned a little towards the middle of the room
                int towardX = wx == 3 ? 1 : -1, towardZ = wz == 2 ? 1 : -1;
                int sign = pickZ ? towardX * towardZ : -towardX * towardZ;
                return StampObject(def, room, x0, z0, gw, gd, rot, px, pz, rot * 90 + sign * turn, flags);
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
                if (HasChairSlots(def) && !RingFree(x0, z0, gw, gd, room)) continue;
                int clear = GenData.defClear[def];
                if (clear > 0 && !FrontFree(x0, z0, gw, gd, rot, clear)) continue;
                if (!SpacingOk(def, x0, z0)) continue;
                return StampObject(def, room, x0, z0, gw, gd, rot, px, pz, yaw, flags);
            }
        }
        return -1;
    }

    // Warehouse racks follow a row template with aisles (04 section 4.3.3)
    int PlaceRackRows(int room, int def, int max, ref GenRng rng)
    {
        int placed = 0;
        int alongWalls = rng.Between(2, 4);
        for (int i = 0; i < alongWalls && placed < max; i++)
        {
            int first = nEmit;
            if (TryPlace(def, room, GenData.A_WALL, 0, ref rng) >= 0 && KeepIfWalkable(room, first, false)) placed++;
        }
        int x0 = roomX0[room], z0 = roomZ0[room], x1 = roomX1[room], z1 = roomZ1[room];
        bool alongX = (x1 - x0) >= (z1 - z0);
        int len = GenData.defW[def];
        int rowStart = (alongX ? z0 : x0) + 5, rowEnd = (alongX ? z1 : x1) - 5;
        int row = 0;
        for (int r = rowStart; r <= rowEnd && placed < max; r += 5, row++)
        {
            int a0 = (alongX ? x0 : z0) + 2 + rng.Range(2), a1 = (alongX ? x1 : z1) - 2;
            int inRow = 0;
            for (int a = a0; a + len - 1 <= a1 && placed < max; a += len)
            {
                if (inRow == 2) { a += 2; inRow = 0; if (a + len - 1 > a1) break; }   // a gap every two racks
                int rx = alongX ? a : r, rz = alongX ? r : a;
                int gw = alongX ? len : 1, gd = alongX ? 1 : len;
                if (!RectFree(rx, rz, gw, gd, room, def)) { inRow = 0; continue; }
                int rot = alongX ? ((row & 1) == 0 ? 0 : 2) : ((row & 1) == 0 ? 1 : 3);
                int px = (2 * rx + gw) * GenData.INNER_MM / 2, pz = (2 * rz + gd) * GenData.INNER_MM / 2;
                int first = nEmit;
                StampObject(def, room, rx, rz, gw, gd, rot, px, pz, rot * 90, 0);
                if (!KeepIfWalkable(room, first, false)) { inRow = 0; continue; }
                placed++; inRow++;
            }
        }
        return placed;
    }

    int rackDefCache = -1;

    // A cap written for a 16 m2 room, for this room: larger rooms hold more, and a density above Normal loosens
    // the caps that allow more than one, so the higher floor targets can be met.
    public int ScaledCap(int cap, int room)
    {
        int c = cap * (3 + roomOuterCells[room]) / 4;
        int pct = GenData.DensityPercent[density];
        if (cap > 1 && pct > 100) c = (c * pct + 50) / 100;
        return c < 1 ? 1 : c;
    }

    // the cap of a table entry in this room; one-of-a-kind pieces (one table, one bed, one sofa) never scale
    int EntryCap(int e, int room)
    {
        return GenData.entryGroup[e] > 0 ? GenData.entryMax[e] : ScaledCap(GenData.entryMax[e], room);
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

    // floor props an entry adds at least: a host brings its chairs, a group its smallest size
    int UnitSize(int e)
    {
        int g = GenData.entryGrp[e];
        if (g >= 0) return GenData.grpMin[g];
        int def = GenData.entryDef[e], n = 1;
        for (int i = GenData.defSlotStart[def]; i < GenData.defSlotStart[def] + GenData.defSlotCount[def]; i++)
            if (GenData.slotType[i] == GenData.S_ADJACENT) n++;
        return n;
    }

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

    // Places entry i of the room's table: a single def or a prop group of at most `room for` members.
    // Chairs and seats of a new host are placed at once, because they stand on the floor and count there.
    // Returns how many floor props were added (0 = no place found).
    int PlaceEntry(int e, int room, int flags, int roomFor, ref GenRng rng)
    {
        int before = roomFloor[room], first = nEmit;
        int g = GenData.entryGrp[e];
        bool ok;
        if (g >= 0)
        {
            int n = rng.Between(GenData.grpMin[g], GenData.grpMax[g]);
            if (n > roomFor) n = roomFor;
            ok = PlaceGroup(g, room, GenData.entryAnchor[e] >= 0 ? GenData.entryAnchor[e] : GenData.grpAnchor[g], n, flags, ref rng);
        }
        else
        {
            int def = GenData.entryDef[e];
            ok = PlaceSingle(def, room, GenData.entryAnchor[e] >= 0 ? GenData.entryAnchor[e] : GenData.defAnchor[def], flags, ref rng) >= 0;
        }
        if (!ok) return 0;
        for (int h = first; h < nEmit; h++) FillSlots(h, 0);
        if (!KeepIfWalkable(room, first, (flags & EF_MANDATORY) != 0)) return 0;
        int added = roomFloor[room] - before;
        return added > 0 ? added : 1;
    }

    // weighted pick among the table's scenery (trees) or decoration (grass) entries
    void PlaceScenery(int room, int table, int flagBit, int count, ref GenRng rng)
    {
        int es = GenData.tableEntryStart[table], ec = GenData.tableEntryCount[table];
        int total = 0;
        for (int i = 0; i < ec; i++) if (Has(GenData.entryDef[es + i], flagBit) && GenData.entryGrp[es + i] < 0) total += GenData.entryWeight[es + i];
        if (total == 0) return;
        int placed = 0;
        for (int tries = count * 3; tries > 0 && placed < count; tries--)
        {
            int r = rng.Range(total), pick = -1;
            for (int i = 0; i < ec; i++)
            {
                int e = es + i;
                if (!Has(GenData.entryDef[e], flagBit) || GenData.entryGrp[e] >= 0) continue;
                if (r < GenData.entryWeight[e]) { pick = e; break; }
                r -= GenData.entryWeight[e];
            }
            if (pick < 0) break;
            int def = GenData.entryDef[pick], first = nEmit;
            if (TryPlace(def, room, GenData.entryAnchor[pick] >= 0 ? GenData.entryAnchor[pick] : GenData.defAnchor[def], 0, ref rng) < 0) continue;
            if (flagBit == GenData.FL_DECOR || KeepIfWalkable(room, first, false)) placed++;
        }
    }

    void PopulateRoom(int room)
    {
        roomEmitStart[room] = nEmit;
        int table = GenData.FindTable(roomZone[room], roomType[room]);
        if (table < 0) return;
        if (rackDefCache < 0) rackDefCache = GenData.FindDef("gen_rack");
        GenRng rng = Rng(ST_ROOM, room, 0);
        GenRng srng = Rng(ST_SCENERY, room, 0);
        int type = roomType[room];
        // the room's numbers from the range table (G24), for its floor area and the map's density
        int lo = PerArea(GenData.rangeFloorMin[type], room, true);
        roomFloorMax[room] = PerArea(GenData.rangeFloorMax[type], room, true);
        roomFloorTarget[room] = rng.Between(lo, roomFloorMax[room]);
        roomTopCap[room] = PerArea(GenData.rangeTopMax[type], room, true);
        int cover = GenData.rangeCover[type] * GenData.DensityPercent[density] / 100;
        roomCoverMax[room] = roomCellCount[room] * (cover > 70 ? 70 : cover) / 100;
        int trees = srng.Between(PerArea(GenData.rangeTreeMin[type], room, false), PerArea(GenData.rangeTreeMax[type], room, false));
        int grass = srng.Between(PerArea(GenData.rangeDecorMin[type], room, false), PerArea(GenData.rangeDecorMax[type], room, false));

        int es = GenData.tableEntryStart[table], ec = GenData.tableEntryCount[table];
        if (ec > entryCount.Length) ec = entryCount.Length;
        for (int i = 0; i < ec; i++) entryCount[i] = 0;
        for (int i = 0; i < groupCount.Length; i++) groupCount[i] = 0;
        int firstEmit = nEmit;

        // exclusive groups (one table, one bed, one sofa): one member is chosen up front by weight
        for (int g = 1; g < groupCount.Length; g++)
        {
            int total = 0;
            for (int i = 0; i < ec; i++) if (GenData.entryGroup[es + i] == g) total += GenData.entryWeight[es + i];
            if (total == 0) continue;
            int r = rng.Range(total), pick = -1;
            for (int i = 0; i < ec; i++)
            {
                if (GenData.entryGroup[es + i] != g) continue;
                if (r < GenData.entryWeight[es + i]) { pick = i; break; }
                r -= GenData.entryWeight[es + i];
            }
            groupCount[g] = pick + 1;   // remembered as the allowed entry + 1
        }
        // 1. mandatory entries, biggest footprint first
        int[] order = new int[ec];
        for (int i = 0; i < ec; i++) order[i] = i;
        for (int i = 1; i < ec; i++)
        {
            int k = i, v = order[i];
            int av = GenData.defW[GenData.entryDef[es + v]] * GenData.defD[GenData.entryDef[es + v]];
            while (k > 0 && GenData.defW[GenData.entryDef[es + order[k - 1]]] * GenData.defD[GenData.entryDef[es + order[k - 1]]] < av) { order[k] = order[k - 1]; k--; }
            order[k] = v;
        }
        for (int oi = 0; oi < ec; oi++)
        {
            int i = order[oi], e = es + i, def = GenData.entryDef[e];
            int g = GenData.entryGroup[e];
            if (g > 0 && groupCount[g] != i + 1) continue;
            if (GenData.entryGrp[e] < 0 && SceneryOrDecor(def)) continue;
            if (GenData.entryGrp[e] < 0 && def == rackDefCache && roomType[room] == GenData.R_STORAGE)
            {
                entryCount[i] += PlaceRackRows(room, def, ScaledCap(GenData.entryMax[e], room), ref rng);
                continue;
            }
            for (int k = 0; k < GenData.entryMin[e]; k++)
            {
                if (PlaceEntry(e, room, EF_MANDATORY, 99, ref rng) > 0) entryCount[i]++;
                else { statMandatoryFail++; break; }
            }
        }
        // 2. trees, before the rest: other things stand around and beside them
        PlaceScenery(room, table, GenData.FL_SCENERY, trees, ref srng);
        // 3. weighted fill up to the room's floor target; an entry is skipped when its type is at its cap or
        //    when what it brings (a table and its chairs, a whole group) no longer fits under the target
        int attempts = 4 * ec + 8;
        RoomWalkable(room);
        while (attempts-- > 0 && roomFloor[room] < roomFloorTarget[room] && walkBlocked < roomCoverMax[room])
        {
            int roomFor = roomFloorTarget[room] - roomFloor[room];
            int total = 0;
            for (int i = 0; i < ec; i++)
                if (Fillable(es + i, i, room, roomFor)) total += GenData.entryWeight[es + i];
            if (total == 0) break;
            int r = rng.Range(total), pick = -1;
            for (int i = 0; i < ec; i++)
            {
                if (!Fillable(es + i, i, room, roomFor)) continue;
                if (r < GenData.entryWeight[es + i]) { pick = i; break; }
                r -= GenData.entryWeight[es + i];
            }
            if (pick < 0) break;
            if (PlaceEntry(es + pick, room, 0, roomFor, ref rng) > 0) entryCount[pick]++;
        }
        // 4. what sits on, in and under the furniture, thinned evenly to the room's cap
        SetTopFactor(room, firstEmit);
        for (int h = firstEmit; h < nEmit; h++)
            if (emitRoom[h] == room) FillSlots(h, 1);
        // 5. grass tufts
        PlaceScenery(room, table, GenData.FL_DECOR, grass, ref srng);
    }

    bool Fillable(int e, int i, int room, int roomFor)
    {
        int g = GenData.entryGroup[e];
        if (GenData.entryWeight[e] <= 0 || (g > 0 && groupCount[g] != i + 1)) return false;
        if (GenData.entryGrp[e] < 0 && SceneryOrDecor(GenData.entryDef[e])) return false;
        if (entryCount[i] >= EntryCap(e, room)) return false;
        return UnitSize(e) <= roomFor;
    }
}
