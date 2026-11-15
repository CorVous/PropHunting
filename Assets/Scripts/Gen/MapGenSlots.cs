// MapGen, part 4: the slot system (04 section 4.4), wrong-category spawns (4.3.5) and disguise quotas (4.7.3).
public partial class MapGen
{
    // world position of a host-local point, using the host's exact yaw (final transform only)
    void Local(int host, int lx, int lz, out int wx, out int wz)
    {
        int a = emitYaw[host];
        int sn = sinTab[a], cs = sinTab[(a + 90) % 360];
        // yaw turns +Z towards +X (clockwise seen from above)
        wx = emitX[host] + (lx * cs + lz * sn) / 4096;
        wz = emitZ[host] + (-lx * sn + lz * cs) / 4096;
    }

    // weighted pick among the defs a slot accepts
    int PickDef(int accept, int sizeClass, ref GenRng rng)
    {
        int total = 0;
        for (int d = 0; d < GenData.nDefs; d++)
            if ((GenData.defTags[d] & accept) != 0 && GenData.defSizeClass[d] <= sizeClass) total += GenData.defSlotWeight[d];
        if (total == 0) return -1;
        int r = rng.Range(total);
        for (int d = 0; d < GenData.nDefs; d++)
        {
            if ((GenData.defTags[d] & accept) == 0 || GenData.defSizeClass[d] > sizeClass) continue;
            if (r < GenData.defSlotWeight[d]) return d;
            r -= GenData.defSlotWeight[d];
        }
        return -1;
    }

    bool[] slotFilled = new bool[64];
    bool[] groupUsed = new bool[16];

    void AddFree(int host, int sl)
    {
        if (nFree < freeHost.Length) { freeHost[nFree] = host; freeSlot[nFree] = sl; nFree++; }
    }

    // 04 section 4.4.2 fillSlots, without recursion: children are appended to the emit list and the caller's
    // loop reaches them later (depth is limited to 2, chances are damped by 0.7 per level).
    void FillSlots(int host)
    {
        int def = emitDef[host];
        int ns = GenData.defSlotCount[def];
        if (ns == 0) return;
        int depth = emitDepth[host];
        if (depth > 2) return;
        int room = emitRoom[host];
        GenRng rng = Rng(ST_SLOT, host, 0);
        int s0 = GenData.defSlotStart[def], g0 = GenData.defGroupStart[def], ng = GenData.defGroupCount[def];
        int damp = depth == 0 ? 1000 : (depth == 1 ? 700 : 490);
        for (int i = 0; i < ns && i < slotFilled.Length; i++) slotFilled[i] = false;
        for (int g = 0; g < ng && g < groupUsed.Length; g++)
        {
            groupUsed[g] = false;
            int mode = GenData.groupMode[g0 + g];
            bool roll = rng.Range(1000) < GenData.groupChance[g0 + g] * damp / 1000;
            if (mode == GenData.G_ELSE_OF && groupUsed[GenData.groupRef[g0 + g]]) roll = false;
            int occasion = rng.Range(1000);          // settings: 45% dinner for everyone, 30% partial, 25% bare
            bool mixed = rng.Chance(GenData.MIXED_SET_PERMILLE);
            int chosen = -1;
            int ordinal = 0;
            for (int i = 0; i < ns; i++)
            {
                int sl = s0 + i;
                if (GenData.slotGroup[sl] != g) continue;
                int k = ordinal++;
                int p = GenData.slotFill[sl] * damp / 1000;
                int hideRoll = rng.Range(1000), fillRoll = rng.Range(1000);
                if (!roll)
                {
                    if (mode == GenData.G_INDEPENDENT) AddFree(host, sl);
                    continue;
                }
                if (mode == GenData.G_MIRROR_OF)
                {
                    // the k-th setting follows the k-th chair
                    int rg = GenData.groupRef[g0 + g], seen = 0, mirror = -1;
                    for (int j = 0; j < ns; j++)
                        if (GenData.slotGroup[s0 + j] == rg) { if (seen == k) { mirror = j; break; } seen++; }
                    bool present = mirror >= 0 && slotFilled[mirror];
                    p = !present ? 0 : (occasion < 450 ? 1000 : (occasion < 750 ? 500 : 0));
                }
                if (GenData.slotHiding[sl] && hideRoll < GenData.HIDING_SOCKET_PERMILLE)
                {
                    // left empty on purpose for a hider to complete the scene (04 section 4.7.4)
                    if (nSockets < socketHost.Length) { socketHost[nSockets] = host; socketSlot[nSockets] = sl; nSockets++; }
                    continue;
                }
                if (fillRoll >= p || roomSlotItems[room] >= GenData.ROOM_SLOT_ITEM_CAP)
                {
                    if (mode != GenData.G_MIRROR_OF) AddFree(host, sl);
                    continue;
                }
                int cd;
                if (mode == GenData.G_MATCHED && !mixed)
                {
                    if (chosen < 0) chosen = PickDef(GenData.slotAccept[sl], GenData.slotSize[sl], ref rng);
                    cd = chosen;
                }
                else cd = PickDef(GenData.slotAccept[sl], GenData.slotSize[sl], ref rng);
                if (cd < 0) continue;
                int child = EmitInSlot(host, sl, cd, 0, ref rng);
                if (child >= 0)
                {
                    slotFilled[i] = true; groupUsed[g] = true;
                    roomSlotItems[room]++;
                }
                else if (GenData.slotType[sl] != GenData.S_ADJACENT) AddFree(host, sl);
            }
        }
    }

    // put def cd into slot sl of the host. Returns the emit index, or -1 if an ADJACENT slot is blocked.
    int EmitInSlot(int host, int sl, int cd, int flags, ref GenRng rng)
    {
        int type = GenData.slotType[sl];
        int room = emitRoom[host];
        int lx = GenData.slotX[sl], lz = GenData.slotZ[sl];
        if (type == GenData.S_ADJACENT)
        {
            // pulled out 0 to 25 cm away from the host
            int pull = rng.Between(0, 250);
            int yawJ = Jitter(ref rng, 12);
            int len = ISqrt(lx * lx + lz * lz);
            if (len > 0) { lx += lx * pull / len; lz += lz * pull / len; }
            // the cell is the one the chair's centre really lands in (the host has a few cm and degrees of jitter)
            int wx, wz;
            Local(host, lx, lz, out wx, out wz);
            int cx = FloorDiv(wx, GenData.INNER_MM), cz = FloorDiv(wz, GenData.INNER_MM);
            if (cx < 0 || cz < 0 || cx >= IW || cz >= IH) return -1;
            int ci = cz * IW + cx;
            int t = tags[ci];
            if ((t & GenData.T_FLOOR) == 0 || (t & (GenData.T_BLOCKED | GenData.T_NO_SPAWN | GenData.T_DOOR_CLEAR)) != 0) return -1;
            if (innerRoom[ci] != room || occ[ci] != 0 || reserve[ci] != 0) return -1;
            int e = Emit(cd, wx, 0, wz, emitYaw[host] + GenData.slotYaw[sl] + yawJ, host, room, flags | EF_ADJACENT);
            if (e < 0) return -1;
            emitSlot[e] = sl;
            emitRX[e] = cx; emitRZ[e] = cz; emitRW[e] = 1; emitRD[e] = 1; emitRot[e] = (emitRot[host] + GenData.slotYaw[sl] / 90) & 3;
            occ[ci] = e + 1;
            return e;
        }
        else
        {
            int f = type == GenData.S_ON_TOP ? EF_ON_TOP : (type == GenData.S_INSIDE ? EF_INSIDE : EF_UNDER);
            int j = type == GenData.S_UNDER ? 0 : Min(GenData.defJitPos[cd] * 10, 30);
            int jx = Jitter(ref rng, j), jz = Jitter(ref rng, j);
            bool upright = (GenData.defTags[cd] & (GenData.I_SETTING | GenData.I_SCREEN | GenData.I_LAMP)) != 0;
            int yawJ = type == GenData.S_UNDER ? 0 : Jitter(ref rng, upright ? 6 : 25);
            int wx, wz;
            Local(host, lx + jx, lz + jz, out wx, out wz);
            int e = Emit(cd, wx, emitY[host] + GenData.slotY[sl], wz, emitYaw[host] + GenData.slotYaw[sl] + yawJ, host, room, flags | f);
            if (e < 0) return -1;
            emitSlot[e] = sl;
            emitRX[e] = FloorDiv(wx, GenData.INNER_MM); emitRZ[e] = FloorDiv(wz, GenData.INNER_MM); emitRW[e] = 0; emitRD[e] = 0;
            emitRot[e] = emitRot[host];
            return e;
        }
    }

    static int FloorDiv(int a, int b) { int q = a / b; if ((a % b != 0) && ((a < 0) != (b < 0))) q--; return q; }

    static int ISqrt(int v)
    {
        if (v <= 0) return 0;
        int r = 0;
        for (int bit = 1 << 15; bit > 0; bit >>= 1)
        {
            int t = r | bit;
            if ((long)t * t <= v) r = t;
        }
        return r;
    }

    // =====================================================================================================
    // Wrong-category spawns (04 section 4.3.5; Q13, Q14, D10)
    // =====================================================================================================
    bool[] nativeIn;   // [def * 64 + table]: the def is in that room type's spawn table
    int[] nativeZones; // per def: bit per zone

    void BuildNative()
    {
        if (nativeIn != null) return;
        nativeIn = new bool[GenData.nDefs * 64];
        nativeZones = new int[GenData.nDefs];
        for (int t = 0; t < GenData.nTables; t++)
        {
            int es = GenData.tableEntryStart[t], n = GenData.tableEntryCount[t] + GenData.tableClutterCount[t];
            for (int e = es; e < es + n; e++)
            {
                nativeIn[GenData.entryDef[e] * 64 + t] = true;
                nativeZones[GenData.entryDef[e]] |= 1 << GenData.tableZone[t];
            }
        }
    }

    int LeakWeight(int d, int table, int zone, bool outdoor, bool comedic)
    {
        int w = GenData.defLeakWeight[d];
        if (w == 0) return 0;
        if (!Has(d, outdoor ? GenData.FL_LEAK_OUT : GenData.FL_LEAK_IN)) return 0;
        if (table >= 0 && nativeIn[d * 64 + table]) return 0;                       // native here: not a leak
        if (comedic)
        {
            if (GenData.defAbsurd[d] < 2) return 0;
            if ((nativeZones[d] & (1 << zone)) != 0) return 0;                       // comedic leaks come from another zone
        }
        else if (GenData.defAbsurd[d] > 1) return 0;
        return w;
    }

    void PlaceLeaks()
    {
        BuildNative();
        GenRng rng = Rng(ST_LEAK, 0, 0);
        int target = GenData.SizeLeaks[size];
        int placed = 0, attempts = target * 14;
        int totalCells = 0;
        for (int r = 0; r < nRooms; r++) if (roomZone[r] != GenData.Z_RELEASE) totalCells += roomCellCount[r];
        while (placed < target && attempts-- > 0)
        {
            bool comedic = rng.Chance(GenData.LEAK_COMEDIC_PERMILLE);   // D10: about 30% comedic
            // room weighted by area, at most 2 leaks per room
            int pick = rng.Range(totalCells), room = -1;
            for (int r = 0; r < nRooms; r++)
            {
                if (roomZone[r] == GenData.Z_RELEASE) continue;
                if (pick < roomCellCount[r]) { room = r; break; }
                pick -= roomCellCount[r];
            }
            if (room < 0 || roomLeaks[room] >= 2) continue;
            int zone = roomZone[room];
            bool outdoor = !GenData.IsBuilding(zone);
            int table = GenData.FindTable(zone, roomType[room]);
            int total = 0;
            for (int d = 0; d < GenData.nDefs; d++) total += LeakWeight(d, table, zone, outdoor, comedic);
            if (total == 0) continue;
            int rr = rng.Range(total), def = -1;
            for (int d = 0; d < GenData.nDefs; d++)
            {
                int wgt = LeakWeight(d, table, zone, outdoor, comedic);
                if (wgt == 0) continue;
                if (rr < wgt) { def = d; break; }
                rr -= wgt;
            }
            if (def < 0) continue;
            int flags = EF_LEAK | (comedic ? EF_COMEDIC : 0);
            int e = -1;
            // small slot-only things may sit on a free surface instead of the floor
            bool onSurface = rng.Chance(600);
            if (GenData.defAnchor[def] == GenData.A_SLOT_ONLY && GenData.defSizeClass[def] <= GenData.SZ_M && onSurface && nFree > 0)
            {
                int f0 = rng.Range(nFree);
                for (int k = 0; k < nFree && e < 0; k++)
                {
                    int f = (f0 + k) % nFree;
                    if (freeHost[f] < 0 || emitRoom[freeHost[f]] != room) continue;
                    int sl = freeSlot[f];
                    if (GenData.slotType[sl] != GenData.S_ON_TOP || GenData.slotSize[sl] < GenData.defSizeClass[def]) continue;
                    e = EmitInSlot(freeHost[f], sl, def, flags, ref rng);
                    if (e >= 0) freeHost[f] = -1;
                }
            }
            if (e < 0)
            {
                // the def's own anchor first, then the fallback (a wall-anchored def in a forest falls back to
                // any floor); comedic leaks prefer visible cells
                int own = GenData.defAnchor[def];
                int visible = outdoor ? GenData.A_OUT_OPEN : GenData.A_CENTER;
                int fallback = outdoor ? GenData.A_OUT_OPEN : GenData.A_ANY;
                if (own == GenData.A_SLOT_ONLY) own = fallback;
                if (comedic) e = TryPlace(def, room, visible, flags, ref rng);
                if (e < 0) e = TryPlace(def, room, own, flags, ref rng);
                if (e < 0) e = TryPlace(def, room, fallback, flags, ref rng);
            }
            if (e < 0) continue;
            roomLeaks[room]++;
            placed++;
            if (comedic) statLeakComedic++; else statLeakPlausible++;
            for (int h = e; h < nEmit; h++) FillSlots(h);
        }
    }

    // =====================================================================================================
    // Disguise quotas (04 section 4.7.3): enough copies of a type, or it is not on the menu
    // =====================================================================================================
    void Quotas()
    {
        BuildNative();
        GenRng rng = Rng(ST_QUOTA, 0, 0);
        int nd = GenData.nDefs;
        for (int d = 0; d < nd; d++) { defCopies[d] = 0; menu[d] = false; }
        int props = 0;
        for (int e = 0; e < nEmit; e++)
        {
            if ((emitFlags[e] & EF_REMOVED) != 0) continue;
            defCopies[emitDef[e]]++;
            props++;
        }
        int types = 0;
        for (int d = 0; d < nd; d++) if (GenData.defDisguise[d] > 0 && defCopies[d] > 0) types++;
        if (types == 0) return;
        int hiders = GenData.SizeHiders[size];
        int perType = (hiders + types - 1) / types + 2;
        int budget = props * 45 / 100;      // quota copies may add at most 45% to the map
        // types that miss the fewest copies first, so the budget buys as many menu types as possible
        int[] order = new int[nd];
        int no = 0;
        for (int d = 0; d < nd; d++)
        {
            if (GenData.defDisguise[d] == 0 || defCopies[d] == 0) continue;
            int need0 = GenData.defMinCopies[d] > perType ? GenData.defMinCopies[d] : perType;
            if (defCopies[d] >= need0) continue;
            int k = no++;
            int miss = need0 - defCopies[d];
            while (k > 0)
            {
                int o = order[k - 1];
                int needO = GenData.defMinCopies[o] > perType ? GenData.defMinCopies[o] : perType;
                if (needO - defCopies[o] <= miss) break;
                order[k] = o; k--;
            }
            order[k] = d;
        }
        for (int oi = 0; oi < no; oi++)
        {
            int d = order[oi];
            int need = GenData.defMinCopies[d] > perType ? GenData.defMinCopies[d] : perType;
            int tries = (need - defCopies[d]) * 6;
            while (defCopies[d] < need && tries-- > 0 && budget > 0)
            {
                int e = -1;
                if (GenData.defAnchor[d] == GenData.A_SLOT_ONLY)
                {
                    // a free slot that accepts this type
                    if (nFree == 0) break;
                    int f0 = rng.Range(nFree);
                    for (int k = 0; k < nFree && e < 0; k++)
                    {
                        int f = (f0 + k) % nFree;
                        if (freeHost[f] < 0) continue;
                        int sl = freeSlot[f];
                        if ((GenData.slotAccept[sl] & GenData.defTags[d]) == 0 || GenData.slotSize[sl] < GenData.defSizeClass[d]) continue;
                        if (roomSlotItems[emitRoom[freeHost[f]]] >= GenData.ROOM_SLOT_ITEM_CAP + 14) continue;
                        e = EmitInSlot(freeHost[f], sl, d, EF_QUOTA, ref rng);
                        if (e >= 0) { freeHost[f] = -1; roomSlotItems[emitRoom[e]]++; }
                    }
                    if (e < 0) break;
                }
                else
                {
                    // a room where the type is native (density cap relaxed by 20%)
                    int r0 = rng.Range(nRooms);
                    for (int k = 0; k < nRooms && e < 0; k++)
                    {
                        int room = (r0 + k) % nRooms;
                        int table = GenData.FindTable(roomZone[room], roomType[room]);
                        if (table < 0 || !nativeIn[d * 64 + table]) continue;
                        if (roomCovered[room] * 1000 > roomEligible[room] * (GenData.tableDensity[table] * 12 / 10 + 60)) continue;
                        int anchor = GenData.defAnchor[d];
                        int es = GenData.tableEntryStart[table], n = GenData.tableEntryCount[table];
                        for (int q = es; q < es + n; q++)
                            if (GenData.entryDef[q] == d && GenData.entryAnchor[q] >= 0) anchor = GenData.entryAnchor[q];
                        if (anchor == GenData.A_SLOT_ONLY) anchor = GenData.IsBuilding(roomZone[room]) ? GenData.A_ANY : GenData.A_OUT_OPEN;
                        e = TryPlace(d, room, anchor, EF_QUOTA, ref rng);
                    }
                    if (e < 0) break;
                    for (int h = e; h < nEmit; h++) FillSlots(h);
                }
                defCopies[d]++; statQuotaAdded++; budget--;
            }
        }
        // recount (hosts added for a quota bring children) and build the menu
        for (int d = 0; d < nd; d++) defCopies[d] = 0;
        for (int e = 0; e < nEmit; e++) if ((emitFlags[e] & EF_REMOVED) == 0) defCopies[emitDef[e]]++;
        types = 0;
        for (int d = 0; d < nd; d++) if (GenData.defDisguise[d] > 0 && defCopies[d] > 0) types++;
        perType = (hiders + types - 1) / (types > 0 ? types : 1) + 2;
        menuCount = 0;
        for (int d = 0; d < nd; d++)
        {
            if (GenData.defDisguise[d] == 0) continue;
            int need = GenData.defMinCopies[d] > perType ? GenData.defMinCopies[d] : perType;
            if (defCopies[d] >= need) { menu[d] = true; menuCount++; }
        }
    }
}
