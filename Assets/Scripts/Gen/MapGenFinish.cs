// MapGen, part 5: validation (04 section 4.7.1) and the final results (walk grid, hider spawns, menu, hash).
public partial class MapGen
{
    // may an agent step from inner cell (ix, iz) one cell in direction dir? Walls block; doorways do not.
    public bool CanStep(int ix, int iz, int dir)
    {
        int jx = ix + DX[dir], jz = iz + DZ[dir];
        if (jx < 0 || jz < 0 || jx >= IW || jz >= IH) return false;
        if ((tags[jz * IW + jx] & GenData.T_FLOOR) == 0) return false;
        int cx = ix >> 3, cz = iz >> 3;
        if ((jx >> 3) == cx && (jz >> 3) == cz) return true;
        int et = EdgeType(cx, cz, dir);
        if (et == GenData.E_NONE) return true;
        if (et != GenData.E_DOOR) return false;
        int along = (dir == 0 || dir == 2) ? (ix & 7) : (iz & 7);
        int off = EdgeOff(cx, cz, dir);
        return along >= off && along < off + GenData.DOOR_CELLS;
    }

    // Every prop is an obstacle, whatever its size (G26): a cell is blocked when any prop's real outline
    // reaches more than 5 cm into it (MapGen.Account). Rugs and grass do not count.
    public bool SolidAt(int i)
    {
        return solid[i] > 0;
    }

    // flood fill over inner cells from the hunter release
    void Flood()
    {
        int n = IW * IH;
        for (int i = 0; i < n; i++) reach[i] = 0;
        int start = (releaseZ * 8 + 4) * IW + releaseX * 8 + 4;
        int qh = 0, qt = 0;
        reach[start] = 1; queue[qt++] = start;
        while (qh < qt)
        {
            int i = queue[qh++];
            int ix = i % IW, iz = i / IW;
            for (int d = 0; d < 4; d++)
            {
                if (!CanStep(ix, iz, d)) continue;
                int j = (iz + DZ[d]) * IW + ix + DX[d];
                if (reach[j] != 0 || SolidAt(j)) continue;
                reach[j] = 1; queue[qt++] = j;
            }
        }
    }

    // Pass: in every room no more free cells are unreachable from the hunter release than a room may have sealed
    // off (SealedAllowance). Every placement has already kept its own room walkable (RoomWalkable), so this
    // only fails when rooms do not connect. Repair: remove the last object placed on the
    // room's floor that is not one of its mandatory pieces (a whole group at once), else the last mandatory one,
    // and re-run, up to 10 times; then the seed is re-rolled (attempt + 1, still deterministic).
    bool Validate()
    {
        for (int repair = 0; repair <= 10; repair++)
        {
            Flood();
            int bad = -1;
            for (int r = 0; r < nRooms && bad < 0; r++)
            {
                int s = roomCellStart[r], n = roomCellCount[r], free = 0, ok = 0;
                for (int k = s; k < s + n; k++)
                {
                    int i = roomCells[k];
                    if (SolidAt(i)) continue;
                    free++;
                    if (reach[i] != 0) ok++;
                }
                if (free - ok > SealedAllowance(r)) bad = r;
            }
            if (bad < 0) return true;
            int victim = -1;
            for (int pass = 0; pass < 2 && victim < 0; pass++)
                for (int e = nEmit - 1; e >= 0 && victim < 0; e--)
                {
                    if (emitRoom[e] != bad || emitParent[e] >= 0 || (emitFlags[e] & EF_REMOVED) != 0) continue;
                    if (Has(emitDef[e], GenData.FL_FLAT) || Has(emitDef[e], GenData.FL_DECOR)) continue;
                    if (pass == 0 && (emitFlags[e] & EF_MANDATORY) != 0) continue;
                    victim = e;
                }
            if (victim < 0) return false;
            if (TraceRoom == -2)
                for (int z = roomZ1[bad] + 1; z >= roomZ0[bad] - 1; z--)
                {
                    string line = "    ";
                    for (int x = roomX0[bad] - 1; x <= roomX1[bad] + 1; x++)
                    {
                        if (x < 0 || z < 0 || x >= IW || z >= IH) { line += ' '; continue; }
                        int i = z * IW + x;
                        char c = (tags[i] & GenData.T_FLOOR) == 0 ? ' ' : (solid[i] > 0 ? '#' : (reach[i] != 0 ? '.' : 'o'));
                        if (innerRoom[i] != bad && c == '.') c = ',';
                        if (innerRoom[i] != bad && c == '#') c = '+';
                        line += c;
                    }
                    System.Console.Error.WriteLine(line);
                }
            if (TraceRoom == -2) System.Console.Error.WriteLine("  repair in room " + bad + " (" + GenData.RoomName(roomType[bad]) + "): removing " + GenData.defId[emitDef[victim]] + " flags " + emitFlags[victim] + " attempt " + attempt);
            Remove(victim);
            statRepairs++;
        }
        return false;
    }

    // a member of a prop group takes the whole group with it
    void Remove(int e)
    {
        if ((emitFlags[e] & EF_REMOVED) != 0) return;
        int g = emitGroup[e];
        if (g >= 0 && g != e) { Remove(g); return; }
        RemoveOne(e);
        if (g == e)
            for (int c = e + 1; c < nEmit; c++)
                if (emitGroup[c] == e) RemoveOne(c);
    }

    void RemoveOne(int e)
    {
        if ((emitFlags[e] & EF_REMOVED) != 0) return;
        Account(e, -1);
        emitFlags[e] |= EF_REMOVED;
        if (GenData.defCanopyY[emitDef[e]] > 0) RebuildCanopy();
        // its front clearance is free again (it reaches at most 4 cells from its own cells)
        for (int z = emitRZ[e] - 4; z < emitRZ[e] + emitRD[e] + 4; z++)
            for (int x = emitRX[e] - 4; x < emitRX[e] + emitRW[e] + 4; x++)
                if (x >= 0 && z >= 0 && x < IW && z < IH && reserve[z * IW + x] == e + 1) reserve[z * IW + x] = 0;
        for (int z = emitRZ[e]; z < emitRZ[e] + emitRD[e]; z++)
            for (int x = emitRX[e]; x < emitRX[e] + emitRW[e]; x++)
            {
                int i = z * IW + x;
                if (occ[i] == e + 1) occ[i] = 0;
                if (flatOcc[i] == e + 1) flatOcc[i] = 0;
                // a set-mate that shared the cell (a lamp beside its armchair) holds it now, so later neighbours see it
                if (occ[i] == 0)
                    for (int o = roomEmitStart[emitRoom[e]]; o < nEmit; o++)
                    {
                        if (o == e || (emitFlags[o] & EF_REMOVED) != 0 || (emitParent[o] >= 0 && (emitFlags[o] & EF_ADJACENT) == 0) || Has(emitDef[o], GenData.FL_FLAT) || Has(emitDef[o], GenData.FL_DECOR)) continue;
                        if (x >= emitRX[o] && x < emitRX[o] + emitRW[o] && z >= emitRZ[o] && z < emitRZ[o] + emitRD[o]) { occ[i] = o + 1; break; }
                    }
            }
        // children (and theirs) go with it; they always come later in the list
        for (int c = e + 1; c < nEmit; c++)
            if (emitParent[c] == e && (emitFlags[c] & EF_REMOVED) == 0) Remove(c);
    }

    void Finish()
    {
        // compact: emit indices are final from here on
        int[] remap = new int[nEmit];
        int m = 0;
        for (int e = 0; e < nEmit; e++)
        {
            if ((emitFlags[e] & EF_REMOVED) != 0) { remap[e] = -1; continue; }
            remap[e] = m;
            if (m != e)
            {
                emitDef[m] = emitDef[e]; emitX[m] = emitX[e]; emitY[m] = emitY[e]; emitZ[m] = emitZ[e]; emitYaw[m] = emitYaw[e];
                emitParent[m] = emitParent[e]; emitRoom[m] = emitRoom[e]; emitFlags[m] = emitFlags[e];
                emitRX[m] = emitRX[e]; emitRZ[m] = emitRZ[e]; emitRW[m] = emitRW[e]; emitRD[m] = emitRD[e];
                emitRot[m] = emitRot[e]; emitDepth[m] = emitDepth[e]; emitSlot[m] = emitSlot[e];
                emitGroup[m] = emitGroup[e]; emitGrp[m] = emitGrp[e];
                emitSet[m] = emitSet[e]; emitHost[m] = emitHost[e];
            }
            m++;
        }
        for (int e = 0; e < m; e++)
        {
            if (emitParent[e] >= 0) emitParent[e] = remap[emitParent[e]];
            if (emitGroup[e] >= 0) emitGroup[e] = remap[emitGroup[e]];
            if (emitSet[e] >= 0) emitSet[e] = remap[emitSet[e]];
            // a host that was taken away afterwards: the object stays where it stood, against nothing in particular
            if (emitHost[e] >= 0) emitHost[e] = remap[emitHost[e]] >= 0 ? remap[emitHost[e]] : H_FREE;
        }
        int n = IW * IH;
        for (int i = 0; i < n; i++)
        {
            if (occ[i] > 0) occ[i] = remap[occ[i] - 1] + 1;
            if (flatOcc[i] > 0) flatOcc[i] = remap[flatOcc[i] - 1] + 1;
            if (reserve[i] > 0) reserve[i] = remap[reserve[i] - 1] + 1;
        }
        int nf = 0;
        for (int k = 0; k < nFree; k++)
        {
            int h = freeHost[k] < 0 ? -1 : remap[freeHost[k]];
            if (h >= 0) { freeHost[nf] = h; freeSlot[nf] = freeSlot[k]; nf++; }
        }
        nFree = nf;
        int ns = 0;
        for (int k = 0; k < nSockets; k++)
        {
            int h = remap[socketHost[k]];
            if (h >= 0) { socketHost[ns] = h; socketSlot[ns] = socketSlot[k]; ns++; }
        }
        nSockets = ns;
        nEmit = m;

        // Disguises (G25): any prop on the map can be one. No copies are counted and none are ever added; only the
        // technical exclusions stay off the list (GenData.defDisguise == 0: fixed dressing, trees, ground cover,
        // anything longer than 2.5 m).
        for (int d = 0; d < GenData.nDefs; d++) defCopies[d] = 0;
        for (int e = 0; e < nEmit; e++) defCopies[emitDef[e]]++;
        menuCount = 0;
        for (int d = 0; d < GenData.nDefs; d++)
        {
            menu[d] = GenData.defDisguise[d] > 0 && defCopies[d] > 0;
            if (menu[d]) menuCount++;
        }
        BuildGround();

        Flood();
        for (int i = 0; i < n; i++)
            walk[i] = (byte)((tags[i] & GenData.T_FLOOR) == 0 ? 0 : (SolidAt(i) ? 2 : 1));

        // hider spawn points: reachable free cells at least 6 m from the release, spread over the map
        GenRng rng = Rng(ST_SPAWN, 0, 0);
        int rx = releaseX * 8 + 4, rz = releaseZ * 8 + 4;
        int nc = 0;
        for (int i = 0; i < n; i++)
        {
            bool keep = (rng.NextU() & 7) == 0;   // thin out evenly
            if (reach[i] == 0 || occ[i] != 0 || solid[i] != 0 || !keep || nc >= cand.Length) continue;
            if ((tags[i] & (GenData.T_NO_SPAWN | GenData.T_DOOR_CLEAR)) != 0) continue;
            int dx = i % IW - rx, dz = i / IW - rz;
            if (dx * dx + dz * dz < 12 * 12) continue;
            cand[nc++] = i;
        }
        Shuffle(cand, nc, ref rng);
        nSpawns = nc < spawnCell.Length ? nc : spawnCell.Length;
        for (int k = 0; k < nSpawns; k++) spawnCell[k] = cand[k];

        // counts for the statistics and the tuning overlay (G24): floor and on-furniture props per room type and zone
        statProps = 0; statScenery = 0; statDecor = 0; statSlotItems = 0; statFloor = 0; statTop = 0; statGroups = 0; statLoose = 0;
        for (int i = 0; i < 64; i++) { statTypeRooms[i] = 0; statTypeCells[i] = 0; statTypeFloor[i] = 0; statTypeTop[i] = 0; }
        for (int i = 0; i < 8; i++) { statZoneFloor[i] = 0; statZoneTop[i] = 0; }
        for (int r = 0; r < nRooms; r++)
        {
            int t = roomType[r] & 63;
            statTypeRooms[t]++; statTypeCells[t] += roomCellCount[r];
        }
        for (int e = 0; e < nEmit; e++)
        {
            int d = emitDef[e];
            if (Has(d, GenData.FL_DECOR)) { statDecor++; continue; }
            if (Has(d, GenData.FL_SCENERY)) { statScenery++; continue; }
            statProps++;
            if (emitGroup[e] == e) statGroups++;
            if ((emitFlags[e] & EF_LOOSE) != 0) statLoose++;
            if (emitParent[e] >= 0) statSlotItems++;
            int r = emitRoom[e], t = roomType[r] & 63, z = roomZone[r] & 7;
            if (OnFloor(e)) { statFloor++; statTypeFloor[t]++; statZoneFloor[z]++; }
            else { statTop++; statTypeTop[t]++; statZoneTop[z]++; }
        }

        // FNV-1a over everything that defines the map: the golden-seed hash of 04 section 6.3
        uint h2 = 2166136261u;
        h2 = Fnv(h2, W); h2 = Fnv(h2, size); h2 = Fnv(h2, releaseX); h2 = Fnv(h2, releaseZ);
        for (int c = 0; c < W * H; c++) { h2 = Fnv(h2, cellZone[c]); h2 = Fnv(h2, cellRoom[c]); }
        for (int i = 0; i < edgeH.Length; i++) h2 = Fnv(h2, edgeH[i] * 16 + edgeHOff[i]);
        for (int i = 0; i < edgeV.Length; i++) h2 = Fnv(h2, edgeV[i] * 16 + edgeVOff[i]);
        for (int r = 0; r < nRooms; r++) h2 = Fnv(h2, roomType[r]);
        for (int i = 0; i < n; i++) h2 = Fnv(h2, ground[i]);
        for (int e = 0; e < nEmit; e++)
        {
            h2 = Fnv(h2, emitDef[e]); h2 = Fnv(h2, emitX[e]); h2 = Fnv(h2, emitY[e]); h2 = Fnv(h2, emitZ[e]);
            h2 = Fnv(h2, emitYaw[e]); h2 = Fnv(h2, emitParent[e]); h2 = Fnv(h2, emitFlags[e]);
        }
        mapHash = h2;
    }

    // =====================================================================================================
    // Walkways (G26): with every prop solid, each placement has to leave its room walkable
    // =====================================================================================================
    int walkStamp = 1 << 20;
    int walkBlocked;   // blocked cells of the room at the last RoomWalkable call

    // free cells of a room that may be cut off from the rest in all
    public int SealedAllowance(int room)
    {
        int a = roomCellCount[room] * GenData.SEALED_SHARE / 100;
        return a > GenData.SEALED_POCKET ? a : GenData.SEALED_POCKET;
    }

    // Is the room still walkable? The test is from a doorway cell (or any free cell outdoors) over the cells no
    // prop reaches into, in two steps (report 15, K1: a walkway is 0.91 m wide):
    //   1. the wide floor: cells that lie in a free square of 2 x 2 cells (1 m), reached from the door through
    //      such cells only;
    //   2. the narrow leftovers that hang on it: the strip beside a bed, the gap between two chairs.
    // Free floor that neither step reaches is cut off, or reached only by squeezing through a half-metre gap.
    // Small pockets of it are allowed (the corner behind a plant, the gap between a fire and its seats): none
    // larger than SEALED_POCKET cells, and SealedAllowance in all.
    bool RoomWalkable(int room)
    {
        int s = roomCellStart[room], n = roomCellCount[room];
        int start = -1, free = 0;
        for (int k = s; k < s + n; k++)
        {
            int i = roomCells[k];
            if (solid[i] > 0) continue;
            free++;
            bool better = start < 0;
            if (!better)
            {
                bool dNew = (tags[i] & GenData.T_DOOR_CLEAR) != 0, dOld = (tags[start] & GenData.T_DOOR_CLEAR) != 0;
                better = dNew && !dOld;
                if (!better && dNew == dOld && !WideCell(room, start) && WideCell(room, i)) better = true;
            }
            if (better) start = i;
        }
        walkBlocked = n - free;
        if (start < 0) return false;
        int seen = 0;
        for (int round = 0; round < 3; round++)
        {
            walkStamp++;
            seen = FloodRoom(room, start, walkNarrow ? 0 : 1);
            // the narrow leftovers that hang on the wide floor
            int qh = 0, qt = 0;
            for (int k = s; k < s + n; k++) if (dist[roomCells[k]] == walkStamp) queue[qt++] = roomCells[k];
            while (qh < qt)
            {
                int i = queue[qh++];
                int ix = i % IW, iz = i / IW;
                for (int d = 0; d < 4; d++)
                {
                    int jx = ix + DX[d], jz = iz + DZ[d];
                    if (jx < 0 || jz < 0 || jx >= IW || jz >= IH) continue;
                    int j = jz * IW + jx;
                    if (innerRoom[j] != room || solid[j] > 0 || dist[j] == walkStamp || WideCell(room, j)) continue;
                    dist[j] = walkStamp; queue[qt++] = j; seen++;
                }
            }
            if (seen * 2 >= free || (tags[start] & GenData.T_DOOR_CLEAR) != 0) break;
            // an area without a door (outdoors) was entered in a small corner: start again in the rest of it
            int other = -1;
            for (int k = s; k < s + n && other < 0; k++)
            {
                int i = roomCells[k];
                if (solid[i] == 0 && dist[i] != walkStamp && WideCell(room, i)) other = i;
            }
            if (other < 0) break;
            start = other;
        }
        if (!ExitsJoined(room)) return false;
        if (seen == free) return true;
        if (free - seen > SealedAllowance(room)) return false;
        for (int k = s; k < s + n; k++)
        {
            int i = roomCells[k];
            if (solid[i] > 0 || dist[i] == walkStamp) continue;
            if (FloodRoom(room, i, 0) > GenData.SEALED_POCKET) return false;
        }
        return true;
    }

    // FillToMinimum: a small outdoor area that is still below its minimum only has to stay passable (gaps of one
    // cell count): it is open to its neighbours, and the map-wide check (Validate) keeps every part reachable
    bool walkNarrow;

    // Every way out of the room (a doorway, an open side towards another outdoor area) leads from the floor the
    // walkway test has just marked, not from a cut-off pocket: a room is entered from its neighbours, so a pocket
    // that holds the only way in would seal the rest of the room off from the map.
    int[] exitAll = new int[24], exitMain = new int[24];

    bool ExitsJoined(int room)
    {
        int nAll = 0, nMain = 0;
        int s = roomCellStart[room], n = roomCellCount[room];
        for (int k = s; k < s + n; k++)
        {
            int i = roomCells[k];
            if (solid[i] > 0) continue;
            // only the room's border cells can have a way out
            int ix = i % IW, iz = i / IW;
            for (int d = 0; d < 4; d++)
            {
                int jx = ix + DX[d], jz = iz + DZ[d];
                if (jx < 0 || jz < 0 || jx >= IW || jz >= IH) continue;
                int j = jz * IW + jx, other = innerRoom[j];
                if (other == room || other < 0 || solid[j] > 0 || !CanStep(ix, iz, d)) continue;
                bool main = dist[i] == walkStamp, have = false;
                for (int q = 0; q < nAll && !have; q++) have = exitAll[q] == other;
                if (!have && nAll < exitAll.Length) exitAll[nAll++] = other;
                if (!main) continue;
                have = false;
                for (int q = 0; q < nMain && !have; q++) have = exitMain[q] == other;
                if (!have && nMain < exitMain.Length) exitMain[nMain++] = other;
            }
        }
        return nMain == nAll;
    }

    // does the free cell lie in a free square of 2 x 2 cells of its room?
    bool WideCell(int room, int i)
    {
        int ix = i % IW, iz = i / IW;
        for (int oz = -1; oz <= 0; oz++)
            for (int ox = -1; ox <= 0; ox++)
            {
                int x = ix + ox, z = iz + oz;
                if (x < 0 || z < 0 || x + 1 >= IW || z + 1 >= IH) continue;
                int a = z * IW + x;
                if (innerRoom[a] == room && innerRoom[a + 1] == room && innerRoom[a + IW] == room && innerRoom[a + IW + 1] == room
                    && solid[a] == 0 && solid[a + 1] == 0 && solid[a + IW] == 0 && solid[a + IW + 1] == 0) return true;
            }
        return false;
    }

    // marks the free cells of the room connected to `start` with walkStamp; returns how many.
    // wide = 1: only through cells of the wide floor (the start cell counts as one)
    int FloodRoom(int room, int start, int wide)
    {
        int qh = 0, qt = 0;
        dist[start] = walkStamp; queue[qt++] = start;
        while (qh < qt)
        {
            int i = queue[qh++];
            int ix = i % IW, iz = i / IW;
            for (int d = 0; d < 4; d++)
            {
                int jx = ix + DX[d], jz = iz + DZ[d];
                if (jx < 0 || jz < 0 || jx >= IW || jz >= IH) continue;
                int j = jz * IW + jx;
                if (innerRoom[j] != room || solid[j] > 0 || dist[j] == walkStamp) continue;
                if (wide != 0 && !WideCell(room, j)) continue;
                dist[j] = walkStamp; queue[qt++] = j;
            }
        }
        return qt;
    }

    // Keeps what was placed since emit index `first` if the room is still walkable and, unless the piece is
    // exempt (mandatory pieces and wrong-category spawns), not blocked beyond its cover. If not, the chairs and
    // seats that came with a new host are given up one by one, last first; if that does not help, everything
    // placed since `first` is taken away again.
    bool KeepIfWalkable(int room, int first, bool exempt)
    {
        if (RoomWalkable(room) && (exempt || walkBlocked <= roomCoverMax[room])) return true;
        // is it one chair that is in the way? Then only that one goes
        for (int c = nEmit - 1; c > first; c--)
        {
            if (emitParent[c] < first || (emitFlags[c] & (EF_REMOVED | EF_ADJACENT)) != EF_ADJACENT) continue;
            Account(c, -1);
            bool fixedIt = RoomWalkable(room) && (exempt || walkBlocked <= roomCoverMax[room]);
            Account(c, 1);
            if (fixedIt) { Remove(c); RoomWalkable(room); return true; }
        }
        for (int c = nEmit - 1; c > first; c--)
        {
            if (emitParent[c] < first || (emitFlags[c] & EF_REMOVED) != 0) continue;
            Remove(c);
            if (RoomWalkable(room) && (exempt || walkBlocked <= roomCoverMax[room])) return true;
        }
        if (TraceRoom == room)
        {
            // the room as the walkway test saw it: # blocked, . free, d doorway floor, digits = cut-off pocket cells
            RoomWalkable(room);
            for (int z = roomZ1[room]; z >= roomZ0[room]; z--)
            {
                string line = "    ";
                for (int x = roomX0[room]; x <= roomX1[room]; x++)
                {
                    int i = z * IW + x;
                    line += innerRoom[i] != room ? ' ' : (solid[i] > 0 ? '#' : (dist[i] != walkStamp ? 'o' : ((tags[i] & GenData.T_DOOR_CLEAR) != 0 ? 'd' : '.')));
                }
                System.Console.Error.WriteLine(line);
            }
        }
        for (int e = first; e < nEmit; e++)
            if (emitParent[e] < 0) Remove(e);
        statWalkRejects++;
        RoomWalkable(room);   // walkBlocked is the room's again, without what was taken away
        return false;
    }

    // =====================================================================================================
    // Ground (G30): what the floor of every inner cell is made of. Outdoors the kinds blend at their borders.
    // =====================================================================================================
    public const int GR_NONE = 0, GR_INDOOR = 1, GR_LAWN = 2, GR_FOREST = 3, GR_SOIL = 4, GR_PAVING = 5, GR_TRACK = 6, GR_RELEASE = 7;
    public byte[] ground;

    int BaseGround(int i)
    {
        int t = tags[i];
        if ((t & GenData.T_FLOOR) == 0) return GR_NONE;
        if ((t & GenData.T_OUTDOOR) == 0) return GR_INDOOR;
        int room = innerRoom[i], zone = roomZone[room];
        if (zone == GenData.Z_RELEASE) return GR_RELEASE;
        if (roomType[room] == GenData.R_LANE) return GR_TRACK;    // the lane is a track (G32)
        if (roomType[room] == GenData.R_YARD) return GR_PAVING;   // a yard is paved
        if ((t & GenData.T_EARTH) != 0) return GR_TRACK;                 // trodden earth around a fire
        if (zone == GenData.Z_FOREST) return (t & GenData.T_PATH) != 0 ? GR_TRACK : GR_FOREST;
        if ((t & GenData.T_COVERED) != 0) return GR_PAVING;
        if ((t & GenData.T_SOFT_GROUND) != 0) return GR_SOIL;
        return GR_LAWN;
    }

    // The kind of every cell, then the borders between two kinds of natural ground (lawn, forest floor, soil, the
    // trodden track) are frayed: a cell next to another kind takes that kind with a chance of 38%, a cell two away
    // with 14%. The chance comes from a hash of the seed and the cell, so it is the same on every machine and needs
    // no state. Paving (a patio, a forecourt, a yard) never blends (the G30 amendment: "outside tile areas don't
    // necessarily need blending"): its edge stays where its area ends, and the natural ground beside it frays only
    // with other natural ground. Walls and fences are not borders.
    void BuildGround()
    {
        int n = IW * IH;
        if (ground == null || ground.Length != n) ground = new byte[n];
        for (int i = 0; i < n; i++) { ground[i] = (byte)BaseGround(i); dist[i] = ground[i]; }
        for (int i = 0; i < n; i++)
        {
            int g = dist[i];
            if (g < GR_LAWN || g == GR_RELEASE || g == GR_PAVING) continue;
            int ix = i % IW, iz = i / IW;
            int other = 0, away = 3;
            for (int d = 0; d < 4 && away > 1; d++)
                for (int step = 1; step <= 2; step++)
                {
                    int jx = ix + DX[d] * step, jz = iz + DZ[d] * step;
                    if (jx < 0 || jz < 0 || jx >= IW || jz >= IH) break;
                    // a wall or a fence between the two cells is not a border to blend across
                    if (!CanStep(ix + DX[d] * (step - 1), iz + DZ[d] * (step - 1), d)) break;
                    int gj = dist[jz * IW + jx];
                    if (gj < GR_LAWN || gj == GR_RELEASE || gj == GR_PAVING) break;
                    if (gj != g && step < away) { away = step; other = gj; }
                    if (gj != g) break;
                }
            if (other == 0) continue;
            uint h = GenRng.Hash32(seed, (uint)GenData.GEN_VERSION, 77, (uint)i, (uint)size) % 100u;
            if (h < (away == 1 ? 38u : 14u)) ground[i] = (byte)other;
        }
    }

    static uint Fnv(uint h, int v)
    {
        uint u = (uint)v;
        for (int i = 0; i < 4; i++) { h ^= (u >> (i * 8)) & 0xFF; h *= 16777619u; }
        return h;
    }
}
