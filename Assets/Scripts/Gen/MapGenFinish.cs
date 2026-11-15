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

    public bool SolidAt(int i)
    {
        int o = occ[i];
        return o > 0 && Has(emitDef[o - 1], GenData.FL_SOLID) && (emitFlags[o - 1] & EF_REMOVED) == 0;
    }

    // flood fill over inner cells from the hunter release; solid props block, small loose props do not
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

    // Pass: at least 60% of every room's free cells are reachable. Repair: remove the last solid object of the
    // room and re-run, up to 10 times; then the seed is re-rolled (attempt + 1, still deterministic).
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
                if (free > 0 && ok * 100 < free * 60) bad = r;
            }
            if (bad < 0) return true;
            int victim = -1;
            for (int e = nEmit - 1; e >= 0; e--)
            {
                if (emitRoom[e] != bad || emitParent[e] >= 0 || (emitFlags[e] & EF_REMOVED) != 0) continue;
                if (!Has(emitDef[e], GenData.FL_SOLID)) continue;
                victim = e; break;
            }
            if (victim < 0) return false;
            Remove(victim);
            statRepairs++;
        }
        return false;
    }

    void Remove(int e)
    {
        emitFlags[e] |= EF_REMOVED;
        for (int z = emitRZ[e]; z < emitRZ[e] + emitRD[e]; z++)
            for (int x = emitRX[e]; x < emitRX[e] + emitRW[e]; x++)
            {
                int i = z * IW + x;
                if (occ[i] == e + 1) occ[i] = 0;
                if (flatOcc[i] == e + 1) flatOcc[i] = 0;
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
            }
            m++;
        }
        for (int e = 0; e < m; e++) if (emitParent[e] >= 0) emitParent[e] = remap[emitParent[e]];
        int n = IW * IH;
        for (int i = 0; i < n; i++)
        {
            if (occ[i] > 0) occ[i] = remap[occ[i] - 1] + 1;
            if (flatOcc[i] > 0) flatOcc[i] = remap[flatOcc[i] - 1] + 1;
        }
        int ns = 0;
        for (int k = 0; k < nSockets; k++)
        {
            int h = remap[socketHost[k]];
            if (h >= 0) { socketHost[ns] = h; socketSlot[ns] = socketSlot[k]; ns++; }
        }
        nSockets = ns;
        nEmit = m;

        // quota recount after repairs, so the menu is exact
        for (int d = 0; d < GenData.nDefs; d++) defCopies[d] = 0;
        for (int e = 0; e < nEmit; e++) defCopies[emitDef[e]]++;
        menuCount = 0;
        for (int d = 0; d < GenData.nDefs; d++)
        {
            if (menu[d] && defCopies[d] < GenData.defMinCopies[d]) menu[d] = false;
            if (menu[d]) menuCount++;
        }

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
            if (reach[i] == 0 || occ[i] != 0 || !keep || nc >= cand.Length) continue;
            if ((tags[i] & (GenData.T_NO_SPAWN | GenData.T_DOOR_CLEAR)) != 0) continue;
            int dx = i % IW - rx, dz = i / IW - rz;
            if (dx * dx + dz * dz < 12 * 12) continue;
            cand[nc++] = i;
        }
        Shuffle(cand, nc, ref rng);
        nSpawns = nc < spawnCell.Length ? nc : spawnCell.Length;
        for (int k = 0; k < nSpawns; k++) spawnCell[k] = cand[k];

        statProps = 0; statScenery = 0; statDecor = 0; statSlotItems = 0;
        for (int e = 0; e < nEmit; e++)
        {
            int d = emitDef[e];
            if (Has(d, GenData.FL_DECOR)) statDecor++;
            else if (Has(d, GenData.FL_SCENERY)) statScenery++;
            else statProps++;
            if (emitParent[e] >= 0) statSlotItems++;
        }

        // FNV-1a over everything that defines the map: the golden-seed hash of 04 section 6.3
        uint h2 = 2166136261u;
        h2 = Fnv(h2, W); h2 = Fnv(h2, size); h2 = Fnv(h2, releaseX); h2 = Fnv(h2, releaseZ);
        for (int c = 0; c < W * H; c++) { h2 = Fnv(h2, cellZone[c]); h2 = Fnv(h2, cellRoom[c]); }
        for (int i = 0; i < edgeH.Length; i++) h2 = Fnv(h2, edgeH[i] * 16 + edgeHOff[i]);
        for (int i = 0; i < edgeV.Length; i++) h2 = Fnv(h2, edgeV[i] * 16 + edgeVOff[i]);
        for (int r = 0; r < nRooms; r++) h2 = Fnv(h2, roomType[r]);
        for (int e = 0; e < nEmit; e++)
        {
            h2 = Fnv(h2, emitDef[e]); h2 = Fnv(h2, emitX[e]); h2 = Fnv(h2, emitY[e]); h2 = Fnv(h2, emitZ[e]);
            h2 = Fnv(h2, emitYaw[e]); h2 = Fnv(h2, emitParent[e]); h2 = Fnv(h2, emitFlags[e]);
        }
        mapHash = h2;
    }

    static uint Fnv(uint h, int v)
    {
        uint u = (uint)v;
        for (int i = 0; i < 4; i++) { h ^= (u >> (i * 8)) & 0xFF; h *= 16777619u; }
        return h;
    }
}
