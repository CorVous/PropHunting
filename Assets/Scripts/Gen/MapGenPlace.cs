// MapGen, part 3: placing objects in rooms from the spawn tables (04 section 4.3.3).
public partial class MapGen
{
    int Emit(int def, int x, int y, int z, int yaw, int parent, int room, int flags)
    {
        if (nEmit >= MAX_EMIT) return -1;
        int e = nEmit++;
        emitDef[e] = def; emitX[e] = x; emitY[e] = y; emitZ[e] = z;
        emitYaw[e] = ((yaw % 360) + 360) % 360;
        emitParent[e] = parent; emitRoom[e] = room; emitFlags[e] = flags;
        emitRX[e] = 0; emitRZ[e] = 0; emitRW[e] = 0; emitRD[e] = 0; emitRot[e] = 0; emitSlot[e] = -1;
        emitDepth[e] = parent < 0 ? 0 : emitDepth[parent] + 1;
        return e;
    }

    // every cell of the rectangle is free floor of this room
    bool RectFree(int x0, int z0, int gw, int gd, int room, int def)
    {
        if (x0 < 0 || z0 < 0 || x0 + gw > IW || z0 + gd > IH) return false;
        bool flat = Has(def, GenData.FL_FLAT);
        bool small = gw == 1 && gd == 1 && !Has(def, GenData.FL_SOLID);
        for (int z = z0; z < z0 + gd; z++)
            for (int x = x0; x < x0 + gw; x++)
            {
                int i = z * IW + x;
                int t = tags[i];
                if ((t & GenData.T_FLOOR) == 0 || innerRoom[i] != room) return false;
                if ((t & (GenData.T_BLOCKED | GenData.T_NO_SPAWN)) != 0) return false;
                if (flat) { if (flatOcc[i] != 0) return false; continue; }
                if ((t & (GenData.T_DOOR_CLEAR | GenData.T_SWING)) != 0) return false;
                if (occ[i] != 0 || reserve[i] != 0) return false;
                // only low or small items go on PATH
                if ((t & GenData.T_PATH) != 0 && !small) return false;
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
        bool flat = Has(def, GenData.FL_FLAT);
        bool decor = Has(def, GenData.FL_DECOR);
        for (int z = z0; z < z0 + gd; z++)
            for (int x = x0; x < x0 + gw; x++)
            {
                if (flat) flatOcc[z * IW + x] = e + 1;
                else if (!decor) occ[z * IW + x] = e + 1;
            }
        if (!flat && !decor) roomCovered[room] += gw * gd;
        int clear = GenData.defClear[def];
        if (clear > 0) ReserveFront(x0, z0, gw, gd, rot, clear, Has(def, GenData.FL_OPENABLE));
        return e;
    }

    int Jitter(ref GenRng rng, int range) { return range <= 0 ? 0 : rng.Between(-range, range); }

    // 04 section 4.3.3 tryPlace: up to 24 shuffled cells matching the anchor. Returns the emit index or -1.
    int TryPlace(int def, int room, int anchor, int flags, ref GenRng rng)
    {
        int w = GenData.defW[def], d = GenData.defD[def];
        int sx = GenData.defSx[def], sz = GenData.defSz[def];
        bool tall = GenData.defSy[def] >= 1200;
        bool flatDef = Has(def, GenData.FL_FLAT) || Has(def, GenData.FL_DECOR);
        bool fixture = Has(def, GenData.FL_FIXTURE) && anchor == GenData.A_WALL;
        int s = roomCellStart[room], n = roomCellCount[room];
        int need;
        switch (anchor)
        {
            case GenData.A_WALL: need = GenData.T_WALL_ANY; break;
            case GenData.A_OUT_EDGE: need = GenData.T_EDGE_OUTDOOR; break;
            case GenData.A_CORNER: need = GenData.T_CORNER; break;
            case GenData.A_CENTER: need = GenData.T_CENTER; break;
            case GenData.A_SOFT: need = GenData.T_SOFT_GROUND; break;
            case GenData.A_COVERED: need = GenData.T_COVERED; break;
            default: need = GenData.T_FLOOR; break;
        }
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
            if (anchor == GenData.A_WALL || anchor == GenData.A_OUT_EDGE)
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
                if (!RectFree(x0, z0, gw, gd, room, def)) continue;
                if (HasChairSlots(def) && !RingFree(x0, z0, gw, gd, room)) continue;
                int clear = GenData.defClear[def];
                if (clear > 0 && !FrontFree(x0, z0, gw, gd, rot, clear)) continue;
                if (!SpacingOk(def, x0, z0)) continue;
                int px = (2 * x0 + gw) * GenData.INNER_MM / 2, pz = (2 * z0 + gd) * GenData.INNER_MM / 2;
                int jr = GenData.defJitPos[def] * 10;
                px += Jitter(ref rng, jr); pz += Jitter(ref rng, jr);
                int yaw = rot * 90 + Jitter(ref rng, GenData.defJitYaw[def]);
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
            if (TryPlace(def, room, GenData.A_WALL, 0, ref rng) >= 0) placed++;
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
                StampObject(def, room, rx, rz, gw, gd, rot, px, pz, rot * 90, 0);
                placed++; inRow++;
            }
        }
        return placed;
    }

    int rackDefCache = -1;

    int EntryMax(int e, int cellsScale)
    {
        // rooms of several outer cells take proportionally more of the entries that allow more than one
        int max = GenData.entryMax[e];
        return (max > 1 && cellsScale > 2) ? max * ((cellsScale + 1) / 2) : max;
    }

    void PopulateRoom(int room)
    {
        int table = GenData.FindTable(roomZone[room], roomType[room]);
        if (table < 0) return;
        if (rackDefCache < 0) rackDefCache = GenData.FindDef("gen_rack");
        GenRng rng = Rng(ST_ROOM, room, 0);
        int s = roomCellStart[room], n = roomCellCount[room];
        int eligible = 0;
        for (int k = s; k < s + n; k++)
        {
            int t = tags[roomCells[k]];
            if ((t & GenData.T_FLOOR) != 0 && (t & (GenData.T_BLOCKED | GenData.T_DOOR_CLEAR | GenData.T_NO_SPAWN)) == 0) eligible++;
        }
        roomEligible[room] = eligible;
        int es = GenData.tableEntryStart[table], ec = GenData.tableEntryCount[table];
        if (ec > entryCount.Length) ec = entryCount.Length;
        for (int i = 0; i < ec; i++) entryCount[i] = 0;
        for (int i = 0; i < groupCount.Length; i++) groupCount[i] = 0;
        int firstEmit = nEmit;
        int cellsScale = roomOuterCells[room];

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
            int anchor = GenData.entryAnchor[e] >= 0 ? GenData.entryAnchor[e] : GenData.defAnchor[def];
            if (def == rackDefCache && roomType[room] == GenData.R_STORAGE)
            {
                entryCount[i] += PlaceRackRows(room, def, cellsScale >= 4 ? GenData.entryMax[e] : 4, ref rng);
                continue;
            }
            for (int k = 0; k < GenData.entryMin[e]; k++)
            {
                if (TryPlace(def, room, anchor, 0, ref rng) >= 0) entryCount[i]++;
                else { statMandatoryFail++; break; }
            }
        }
        // 2. weighted fill up to the density target
        int attempts = 4 * eligible / 8 + 4;
        int targetCells = eligible * GenData.tableDensity[table] / 1000;
        while (attempts-- > 0 && roomCovered[room] < targetCells)
        {
            int total = 0;
            for (int i = 0; i < ec; i++)
            {
                int e = es + i, g = GenData.entryGroup[e];
                if (entryCount[i] >= EntryMax(e, cellsScale) || (g > 0 && groupCount[g] != i + 1)) continue;
                total += GenData.entryWeight[e];
            }
            if (total == 0) break;
            int r = rng.Range(total), pick = -1;
            for (int i = 0; i < ec; i++)
            {
                int e = es + i, g = GenData.entryGroup[e];
                if (entryCount[i] >= EntryMax(e, cellsScale) || (g > 0 && groupCount[g] != i + 1)) continue;
                if (r < GenData.entryWeight[e]) { pick = i; break; }
                r -= GenData.entryWeight[e];
            }
            if (pick < 0) break;
            int def = GenData.entryDef[es + pick];
            int anchor = GenData.entryAnchor[es + pick] >= 0 ? GenData.entryAnchor[es + pick] : GenData.defAnchor[def];
            if (TryPlace(def, room, anchor, 0, ref rng) >= 0) entryCount[pick]++;
        }
        // 3. slots of every placed host, breadth first (children are appended and reached later in the loop)
        for (int h = firstEmit; h < nEmit; h++)
            if (emitRoom[h] == room) FillSlots(h);
        // 4. clutter: small items at floor edges
        int cc = GenData.tableClutterCount[table];
        if (cc > 0)
        {
            GenRng crng = Rng(ST_CLUTTER, room, 0);
            int count = crng.Between(GenData.tableClutterMin[table], GenData.tableClutterMax[table]) * (cellsScale > 2 ? (cellsScale + 1) / 2 : 1);
            int cs = GenData.tableClutterStart[table];
            int total = 0;
            for (int i = 0; i < cc; i++) total += GenData.entryWeight[cs + i];
            bool outdoor = !GenData.IsBuilding(roomZone[room]);
            for (int k = 0; k < count; k++)
            {
                int r = crng.Range(total), pick = 0;
                for (int i = 0; i < cc; i++) { if (r < GenData.entryWeight[cs + i]) { pick = i; break; } r -= GenData.entryWeight[cs + i]; }
                int def = GenData.entryDef[cs + pick];
                int anchor = GenData.defAnchor[def];
                bool atWall = crng.Chance(650);
                if (anchor == GenData.A_ANY || anchor == GenData.A_SLOT_ONLY) anchor = outdoor ? GenData.A_OUT_OPEN : (atWall ? GenData.A_WALL : GenData.A_ANY);
                int before = nEmit;
                if (TryPlace(def, room, anchor, EF_CLUTTER, ref crng) >= 0)
                {
                    statClutter++;
                    for (int h = before; h < nEmit; h++) FillSlots(h);
                }
            }
        }
    }
}
