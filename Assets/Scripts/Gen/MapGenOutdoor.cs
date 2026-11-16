// MapGen, part 3d: the plans of the outdoor areas: lawn (open lawn, garden, patio, fire pit) and forest (dense
// forest, camp). Outdoors a place is set up by its edges and its entrances (report 15, section 3.11 and 3.12):
//   - a walkway of stepping stones leads out of every building door and stays clear
//   - planting stands in borders along fences and walls: bushes, drifts of flowers of one colour, groups of rocks
//   - the things people put outside stand at an entrance or against an edge: pots flanking a door, a bench
//     against a wall, a bin by the building, pallets and barrels in a warehouse's yard, a table outside a diner
//   - a garden has soil beds along its fences, a vegetable plot with one crop to a row, a water point with the
//     cans and buckets, a shed with the barrow and the sacks
//   - a patio is a paved area at the door with the table set on it; a fire pit has its ring of seats on trodden
//     earth, the wood pile and the chopping block at the edge
//   - a forest has a path from its entrances to its middle, trees in stands of one or two kinds with open
//     ground between, undergrowth at the stands' edges and along the fence, dead wood as one feature; a camp
//     sits in a clearing: the fire in the middle, the tent facing it, the gear beside the tent
// No outdoor area is left bare (owner decision G29): every type has a minimum number of props per area as well
// as a range per 100 m2 (GenData.rangeFloorAbs), and the borders fill up to it.
public partial class MapGen
{
    // the zone on the other side of door k of a room
    int DoorZone(int room, int k)
    {
        int cell = roomDoors[roomDoorStart[room] + k], d = roomDoorDir[roomDoorStart[room] + k];
        return ZoneAt((cell % IW >> 3) + DX[d], (cell / IW >> 3) + DZ[d]);
    }

    // Stepping stones out of door k of an outdoor room, `cells` deep, on a walkway that stays clear (K1).
    void StonePath(int room, int k, int cells, ref GenRng rng)
    {
        int cell = roomDoors[roomDoorStart[room] + k], d = roomDoorDir[roomDoorStart[room] + k];
        int o = (d + 2) & 3;                       // away from the building
        int cx = cell % IW, cz = cell / IW;
        int stone = Dn("n_pathStone");
        bool alongZ = o == 0 || o == 2;
        int next = 700;                             // mm from the wall to the first stone
        for (int depth = 0; depth < cells; depth++)
        {
            int ax = cx + DX[o] * depth, az = cz + DZ[o] * depth;
            int bx = ax + (alongZ ? 1 : 0), bz = az + (alongZ ? 0 : 1);   // the doorway's second cell
            if (ax < 0 || az < 0 || bx >= IW || bz >= IH) break;
            int ia = az * IW + ax, ib = bz * IW + bx;
            if (innerRoom[ia] != room || innerRoom[ib] != room) break;
            if ((tags[ia] & GenData.T_NO_SPAWN) != 0 || occ[ia] != 0 || occ[ib] != 0) break;
            if (depth > 0 && ((tags[ia] & WallBit(o)) != 0)) { tags[ia] |= GenData.T_PATH; tags[ib] |= GenData.T_PATH; break; }
            tags[ia] |= GenData.T_PATH; tags[ib] |= GenData.T_PATH;
            int at = depth * GenData.INNER_MM + 250;
            if (at < next) continue;
            next = at + 1000 + rng.Range(200);
            int px = alongZ ? (cx + 1) * GenData.INNER_MM : ax * GenData.INNER_MM + 250;
            int pz = alongZ ? az * GenData.INNER_MM + 250 : (cz + 1) * GenData.INNER_MM;
            StampAt(stone, room, px + Jitter(ref rng, 40), pz + Jitter(ref rng, 40), (alongZ ? 0 : 90) + Jitter(ref rng, 8), 0, -1, H_GROUND, false);
        }
    }

    // From the front door the path runs on to the garden gate at the lane (G32); from other doors 3.5 to 6 m.
    void StonePaths(int k0, ref GenRng rng)
    {
        int room = pRoom;
        for (int k = 0; k < roomDoorCount[room]; k++)
        {
            GenRng r = SR(k0 + k);
            int i = roomDoorStart[room] + k, cell = roomDoors[i], d = roomDoorDir[i];
            int ox = cell % IW + DX[d], oz = cell / IW + DZ[d];
            bool front = ox >= 0 && oz >= 0 && ox < IW && oz < IH && IsFrontDoor(oz * IW + ox, (d + 2) & 3);
            StonePath(room, k, front ? 24 : 7 + r.Range(6), ref r);
        }
    }

    // ---- planting borders -------------------------------------------------------------------------------
    int borderColour;   // the flower colour of the last drift, so the next one differs

    int FlowerGroup(ref GenRng rng)
    {
        borderColour = (borderColour + 1 + rng.Range(2)) % 3;
        return GenData.FindGrp(borderColour == 0 ? "flowersRed" : (borderColour == 1 ? "flowersYellow" : "flowersPurple"));
    }

    // A planted border along a free stretch of fence or wall, up to maxLen mm long: bushes, drifts of flowers of
    // one colour and groups of rocks side by side, with soil under it if wanted. Returns the props it added.
    int Border(int maxLen, bool soil, int mode, int tx, int tz, ref GenRng rng)
    {
        int room = pRoom, added = 0;
        int len = maxLen;
        bool found = false;
        for (; len >= 1500 && !found; len -= 500)
            found = FindWallRun(room, len, 1300, 0, mode, tx, tz, WIN_ANY, ref rng);
        if (!found) return 0;
        len += 500;
        int d = wrDir, back = wrBack, start = wrCentre - len / 2, rot = (d + 2) & 3;
        segBan[wrSeg] = true;                       // one border to a stretch
        if (soil)
        {
            // the bed: three cells deep along the stretch
            bool alongX = d == 0 || d == 2;
            int line = segLine[wrSeg];
            for (int a = FloorDiv(start, GenData.INNER_MM); a <= FloorDiv(start + len - 1, GenData.INNER_MM); a++)
                for (int depth = 0; depth < 3; depth++)
                {
                    int x = alongX ? a : (d == 3 ? line + depth : line - depth), z = alongX ? (d == 2 ? line + depth : line - depth) : a;
                    if (x < 0 || z < 0 || x >= IW || z >= IH) continue;
                    int i = z * IW + x;
                    if (innerRoom[i] == room && (tags[i] & (GenData.T_PATH | GenData.T_DOOR_CLEAR | GenData.T_COVERED)) == 0) tags[i] |= GenData.T_SOFT_GROUND;
                }
        }
        int cursor = start + 100 + rng.Range(200), last = -1;
        while (cursor < start + len - 400 && Fit(1))
        {
            int kind = rng.Range(10);
            kind = kind < 4 ? 0 : (kind < 8 ? 1 : 2);     // bush, drift, rocks
            if (kind == last && kind != 0) kind = 0;
            if (kind == 1 && !Fit(3)) kind = 0;
            if (kind == 2 && (!Fit(2) || !RocksAllowed())) kind = Fit(3) ? 1 : 0;
            int first = nEmit;
            if (kind == 0)
            {
                int def = Pick(ref rng, "n_bushLarge", 4, "n_bush", 4, "n_bushDetailed", 3);
                int yaw = rot * 90 + Jitter(ref rng, 25);
                int hx, hz;
                FullHalf(def, yaw, out hx, out hz);
                int halfAlong = (d == 0 || d == 2) ? hx : hz, halfDeep = (d == 0 || d == 2) ? hz : hx;
                if (cursor + 2 * halfAlong > start + len) break;
                int px, pz, r2;
                WallPos(d, back, cursor + halfAlong, 2 * halfDeep + 60, out px, out pz, out r2);
                if (StampAt(def, room, px, pz, yaw, 0, -1, H_WALL, false) >= 0 && Keep(first, false)) added++;
                cursor += 2 * halfAlong + 250 + rng.Range(350);
            }
            else
            {
                int g = kind == 1 ? FlowerGroup(ref rng) : GenData.FindGrp("rocks");
                int n = kind == 1 ? rng.Between(3, Fit(5) ? 5 : 3) : rng.Between(2, Fit(3) ? 3 : 2);
                if (ChooseMembers(g, room, n, ref rng) < 2) { cursor += 500; last = kind; continue; }
                BuildFormation(GenData.grpLayout[g], GenData.grpGapMm[g], ref rng);
                if (cursor + fLen > start + len) break;
                int before = roomFloor[room];
                if (StampFormation(room, rot, cursor, back + (d == 2 || d == 3 ? 40 : -40), d, 0, g, -1) >= 0 && Keep(first, false)) added += roomFloor[room] - before;
                cursor += fLen + 250 + rng.Range(350);
            }
            last = kind;
        }
        return added;
    }

    // borders until the room's floor budget is used or no stretch is left
    void Borders(int k0, int max, bool soil)
    {
        for (int b = 0; b < max && Fit(1); b++)
        {
            GenRng r = SR(k0 + b);
            int longest = 2500 + r.Range(4) * 500;
            if (Border(longest, soil, WM_ANY, 0, 0, ref r) == 0) break;
        }
    }

    // A planting island for a stretch of open ground with no edge to plant along: a bush with rocks beside it.
    bool Island(ref GenRng rng)
    {
        if (!Fit(1)) return false;
        int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref rng);
        if (cell < 0) return false;
        int first = nEmit;
        int bush = FreePlace(Pick(ref rng, "n_bush", 5, "n_bushDetailed", 4), cell, rng.Range(360), 0, 0, H_NATURE);
        if (bush < 0 || !Keep(first, false)) return false;
        if (Fit(2))
        {
            first = nEmit;
            if (GroupBeside(RocksAllowed() ? GenData.FindGrp("rocks") : FlowerGroup(ref rng), bush, rng.Between(2, 3), 0, ref rng)) Keep(first, false);
        }
        return true;
    }

    // Small rocks are a feature of a garden, not its filler: outside the wood an area takes at most two clumps of
    // them (the v4 gallery's big lawns held a dozen rocks or more in five clumps). Borders, islands and trees take
    // flowers or nothing instead.
    bool RocksAllowed()
    {
        int t = roomType[pRoom];
        if (t == GenData.R_FOREST_DENSE || t == GenData.R_FOREST_CLEARING) return true;
        return CountInRoom(Dn("n_rockSmall"), pRoom) + CountInRoom(Dn("n_rockSmallB"), pRoom) + CountInRoom(Dn("n_rockSmallC"), pRoom) <= 3;
    }

    // No outdoor area is left bare (G29). An area whose plan ended below its minimum (a small one whose edges
    // were taken, or one where pieces were taken back for the walkway) is filled up with what stands at the edge
    // of such a place: a planted stretch of fence, a bush, a group of rocks, an island of planting on open ground;
    // in a forest undergrowth, rocks and stumps. These may pass the area's cover limit, never its walkway.
    // The fill may end up to 2 over the minimum, so a pair of rocks can close the last gap. A forest pocket whose
    // trees leave no room for its undergrowth gives up its last tree (never its only one) and tries again.
    void FillToMinimum()
    {
        int room = pRoom, type = roomType[room];
        bool forest = type == GenData.R_FOREST_DENSE || type == GenData.R_FOREST_CLEARING;
        int saved = roomFloorTarget[room];
        if (roomFloorTarget[room] < roomFloorMin[room] + 2) roomFloorTarget[room] = roomFloorMin[room] + 2;
        coverExempt = true;
        for (int pass = 0; pass < 3; pass++)
        {
            FillPass(pass, forest);
            if (roomFloor[room] >= roomFloorMin[room] || !forest || roomTrees[room] <= 1) break;
            int tree = -1;
            for (int e = nEmit - 1; e >= roomEmitStart[room] && tree < 0; e--)
                if (emitRoom[e] == room && (emitFlags[e] & EF_REMOVED) == 0 && Has(emitDef[e], GenData.FL_SCENERY)) tree = e;
            if (tree < 0) break;
            Remove(tree);
        }
        coverExempt = false; walkNarrow = false;
        roomFloorTarget[room] = saved > roomFloor[room] ? saved : roomFloor[room];
    }

    void FillPass(int pass, bool forest)
    {
        int room = pRoom, type = roomType[room];
        ClearBans();
        for (int k = 0; k < 24 && roomFloor[room] < roomFloorMin[room]; k++)
        {
            walkNarrow = k >= 8;   // first with the full walkway width, then passable is enough
            GenRng r = SR(200 + pass * 24 + k);
            int first = nEmit, how = k % 4;
            if (type == GenData.R_YARD)
            {
                // a yard fills up with what a yard holds: goods in rows at the fence and the wall, a pallet
                string[] rows = { "crates", "barrels", "boxes", "sacks" };
                if (how < 3 && GroupAt(GenData.FindGrp(rows[(k + pass) % 4]), GenData.A_OUT_EDGE, r.Between(2, 3), WM_ANY, 0, 0, 0, ref r) && Keep(first, false)) continue;
                first = nEmit;
                if (WallPlace(Pick(ref r, "w_pallet", 5, "w_palletLoaded", 3, "s_boxLarge", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r) >= 0) Keep(first, false);
                continue;
            }
            if (how == 0 && !forest) { if (Border(2500, type != GenData.R_FIREPIT, WM_ANY, 0, 0, ref r) > 0) continue; }
            if (how <= 1)
            {
                first = nEmit;
                if (WallPlace(Pick(ref r, "n_bush", 5, "n_bushDetailed", 4, "n_bushLarge", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r) >= 0 && Keep(first, false)) continue;
            }
            if (how <= 2 && roomFloor[room] + 2 <= roomFloorTarget[room])
            {
                // rocks, or flowers once the area has its rocks
                first = nEmit;
                int g = RocksAllowed() ? GenData.FindGrp("rocks") : FlowerGroup(ref r);
                if (GroupAt(g, forest ? GenData.A_SOFT : GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r) && Keep(first, false)) continue;
            }
            if (forest)
            {
                // mushrooms at the foot of a tree, a clump of flowers, a stump or a bush on open ground
                first = nEmit;
                if (how == 3 && roomFloor[room] + 2 <= roomFloorTarget[room] && PlaceGroup(GenData.FindGrp("mushrooms"), room, GenData.A_BESIDE, 2, 0, ref r) && Keep(first, false)) continue;
                first = nEmit;
                if (how == 2 && roomFloor[room] + 3 <= roomFloorTarget[room] && GroupAt(FlowerGroup(ref r), GenData.A_SOFT, 3, WM_ANY, 0, 0, 0, ref r) && Keep(first, false)) continue;
                int cell = SoftSpot(1, ref r);
                first = nEmit;
                if (cell >= 0 && FreePlace(Pick(ref r, "n_stump", 5, "n_stumpRound", 4, "n_bush", 4), cell, r.Range(360), 0, 0, H_NATURE) >= 0) Keep(first, false);
            }
            else if (!Island(ref r))
            {
                // no open ground for an island: a pot or a small rock group wherever an edge is free
                first = nEmit;
                FSet(Pick(ref r, "n_potSmall", 5, "n_planter", 5));
                if (CountInRoom(fDef[0], room) < ScaledCap(GenData.defRoomCap[fDef[0]], room) && FormAt(GenData.GL_ROW, 0, GenData.A_OUT_EDGE, WM_ANY, 0, 0, 0, ref r) >= 0) Keep(first, false);
            }
        }
    }

    // ---- trees -------------------------------------------------------------------------------------------
    // one tree at an exact spot: trunk cell free, canopy clear of walls, fences and buildings (G31), spacing kept
    int TreeAt(int def, int px, int pz, ref GenRng rng)
    {
        int room = pRoom;
        int cx = FloorDiv(px, GenData.INNER_MM), cz = FloorDiv(pz, GenData.INNER_MM);
        if (cx < 0 || cz < 0 || cx >= IW || cz >= IH || innerRoom[cz * IW + cx] != room) return -1;
        if ((tags[cz * IW + cx] & GenData.T_GLADE) != 0) return -1;
        if (!SpacingOk(def, cx, cz)) return -1;
        int first = nEmit;
        int e = StampAt(def, room, px, pz, rng.Range(360), 0, -1, H_NATURE, false);
        if (e >= 0 && !KeepIfWalkable(room, first, false)) e = -1;
        return e;
    }

    // a specimen tree where the lawn is open, with rocks at its foot now and then (report 15, Ol-specimen and rocks)
    int LawnTree(int k)
    {
        GenRng r = SR(k);
        osAvoid = GenData.T_COVERED | GenData.T_SOFT_GROUND | GenData.T_EARTH;
        int cell = OpenSpot(4, WM_ANY, 0, 0, 0, ref r);
        if (cell < 0) return -1;
        int tree = TreeAt(Pick(ref r, "n_treeFat", 6, "n_treeOak", 4), cell % IW * GenData.INNER_MM + 250, cell / IW * GenData.INNER_MM + 250, ref r);
        if (tree >= 0 && r.Chance(550) && Fit(2) && RocksAllowed())
        {
            int first = nEmit;
            if (GroupBeside(GenData.FindGrp("rocks"), tree, r.Between(2, 3), 0, ref r)) Keep(first, false);
        }
        return tree;
    }

    int TreeCount(int room, ref GenRng rng)
    {
        int type = roomType[room];
        return rng.Between(PerArea(GenData.rangeTreeMin[type], room, false), PerArea(GenData.rangeTreeMax[type], room, false));
    }

    // ---- what stands at a building's door and against its wall ---------------------------------------------
    // By the zone behind each door (report 15, section 3.11, "lawn role by neighbour").
    void DoorSides(int k0)
    {
        int room = pRoom;
        for (int k = 0; k < roomDoorCount[room]; k++)
        {
            GenRng r = SR(k0 + k);
            int zone = DoorZone(room, k), dx = DoorX(room, k), dz = DoorZ(room, k);
            int roll = r.Range(1000);
            if (zone == GenData.Z_HOUSE || zone == GenData.Z_DINER)
            {
                // planters flanking the door
                int pot = Pick(ref r, "n_planter", 4, "n_potSmall", 4, "k_plant", 3);
                if (roll < 700 && Fit(1) && CountInRoom(pot, room) + 2 <= ScaledCap(GenData.defRoomCap[pot], room))
                {
                    for (int side = 0; side < 2 && Fit(1); side++)
                    {
                        int first = nEmit;
                        FSet(pot);
                        if (FormAt(GenData.GL_ROW, 0, GenData.A_DOOR_SIDE, WM_NEAR, dx, dz, 0, ref r) >= 0) Keep(first, false);
                    }
                }
                if (zone == GenData.Z_HOUSE && r.Chance(450) && Fit(1))
                {
                    int first = nEmit;
                    int what = Pick(ref r, "k_bench", 6, "k_trashcan", 4);
                    FSet(what);
                    if (CountInRoom(what, room) < ScaledCap(GenData.defRoomCap[what], room) && FormAt(GenData.GL_ROW, 0, GenData.A_BUILDING, WM_NEAR, dx, dz, 0, ref r) >= 0) Keep(first, false);
                }
                // a table outside a diner
                if (zone == GenData.Z_DINER && r.Chance(650) && Fit(3))
                {
                    CentrePiece(Dn("r_tableRound"), 0, false, 2, 1, WM_NEAR, dx, dz, 0, 0, ref r);
                }
            }
            else if (zone == GenData.Z_WAREHOUSE)
            {
                // the yard: pallets against the wall, a row of barrels
                if (roll < 650 && Fit(2))
                {
                    int first = nEmit;
                    FSet(Dn("w_pallet"), Dn(r.Chance(500) ? "w_palletLoaded" : "w_pallet"));
                    if (FormAt(GenData.GL_ROW, 100, GenData.A_BUILDING, WM_NEAR, dx, dz, 0, ref r) >= 0) Keep(first, false);
                }
                if (r.Chance(500) && Fit(2))
                {
                    int first = nEmit;
                    if (GroupAt(GenData.FindGrp("barrels"), GenData.A_BUILDING, r.Between(2, 3), WM_NEAR, dx, dz, 0, ref r)) Keep(first, false);
                }
            }
        }
    }

    // a bench with its back to the fence or a wall, a small pot beside it (report 15, Pa-bench, Ol-garden bench)
    void BenchAtEdge(int k)
    {
        GenRng r = SR(k);
        if (!Fit(1)) return;
        int first = nEmit;
        int bench = WallPlace(Pick(ref r, "k_bench", 6, "k_benchCushion", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r);
        if (bench < 0 || !Keep(first, false)) return;
        int pot = Dn("n_potSmall");
        if (r.Chance(450) && Fit(1) && CountInRoom(pot, pRoom) < ScaledCap(GenData.defRoomCap[pot], pRoom))
        {
            first = nEmit;
            if (RelBeside(pot, bench, 0, 90, 0, ref r) >= 0) Keep(first, false);
        }
    }

    // ============================================================================================ OPEN LAWN
    void PlanLawn()
    {
        int room = pRoom;
        GenRng r = SR(1);
        StonePaths(40, ref r);
        FrontMarkers(30);
        DoorSides(50);
        int trees = TreeCount(room, ref r);
        if (trees < 1 && AreaM2(room) >= 30) trees = 1;
        for (int t = 0; t < trees; t++) LawnTree(60 + t);
        if (r.Chance(650)) BenchAtEdge(2);
        GenRng r3 = SR(3);
        if (r3.Chance(350) && Fit(1))
        {
            int first = nEmit;
            if (WallPlace(Dn("n_logStack"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0) Keep(first, false);
        }
        // planted borders along the fences fill the rest of the budget: no lawn is left bare (G29)
        ClearBans();
        Borders(10, 6, true);
        GenRng r4 = SR(4);
        for (int k = 0; k < 3 && Fit(1); k++) if (!Island(ref r4)) break;
    }

    // ============================================================================================ GARDEN
    // Areas: soil beds along the fences with their planting; a vegetable plot, one crop to a row; the water point
    // (tap, watering can, buckets); a shed with the barrow and the sacks; tools leaning at the fence; a bench.
    void PlanGarden()
    {
        int room = pRoom, cells = roomOuterCells[room];
        GenRng r = SR(1);
        StonePaths(40, ref r);
        // the shed in a corner of a larger garden, the barrow and the sacks beside it
        int shed = -1;
        if (cells >= 2 && r.Chance(650))
        {
            int first = nEmit;
            shed = WallPlace(Dn("gen_shed"), WM_CORNER, 0, 0, WIN_ANY, 0, ref r);
            if (shed >= 0 && !Keep(first, false)) shed = -1;
            if (shed >= 0)
            {
                if (r.Chance(700) && Fit(1))
                {
                    first = nEmit;
                    if (RelBeside(Dn("m_wheelbarrow"), shed, 0, 150, 0, ref r) >= 0) Keep(first, false);
                }
                if (r.Chance(550) && Fit(2))
                {
                    first = nEmit;
                    if (GroupBeside(GenData.FindGrp("sacks"), shed, r.Between(2, 3), 0, ref r)) Keep(first, false);
                }
            }
        }
        // the water point: a tap at the fence, the can and the buckets beside it
        GenRng r2 = SR(2);
        int tap = -1;
        {
            int first = nEmit;
            tap = WallPlace(Dn("gen_tap"), shed >= 0 ? WM_NEAR : WM_ANY, shed >= 0 ? emitX[shed] : 0, shed >= 0 ? emitZ[shed] : 0, WIN_ANY, 0, ref r2);
            if (tap >= 0 && !Keep(first, false)) tap = -1;
            if (tap >= 0 && Fit(2))
            {
                first = nEmit;
                if (GroupBeside(GenData.FindGrp("watering"), tap, r2.Between(2, 3), 0, ref r2)) Keep(first, false);
            }
        }
        // the vegetable plot: a bed of soil in the open, two rows, one crop to a row
        GenRng r3 = SR(3);
        if (Fit(6) && r3.Chance(800))
        {
            int cell = OpenSpot(4, WM_ANY, 0, 0, 0, ref r3);
            if (cell >= 0) VegPlot(cell, ref r3);
        }
        // tools leaning at the fence near the water point
        GenRng r4 = SR(4);
        if (r4.Chance(750) && Fit(2))
        {
            int first = nEmit;
            bool ok = shed >= 0 && GroupBeside(GenData.FindGrp("tools"), shed, r4.Between(2, 3), 0, ref r4);
            if (!ok) ok = GroupAt(GenData.FindGrp("tools"), GenData.A_OUT_EDGE, r4.Between(2, 3), tap >= 0 ? WM_NEAR : WM_ANY, tap >= 0 ? emitX[tap] : 0, tap >= 0 ? emitZ[tap] : 0, 0, ref r4);
            if (ok) Keep(first, false);
        }
        DoorSides(50);
        GenRng r5 = SR(5);
        int trees = TreeCount(room, ref r5);
        for (int t = 0; t < trees; t++) LawnTree(60 + t);
        if (r5.Chance(450)) BenchAtEdge(6);
        GenRng r7 = SR(7);
        if (r7.Chance(500) && Fit(2))
        {
            int first = nEmit;
            if (GroupAt(GenData.FindGrp("pots"), GenData.A_OUT_EDGE, r7.Between(2, 3), WM_ANY, 0, 0, 0, ref r7)) Keep(first, false);
        }
        // the beds along the fences
        ClearBans();
        Borders(10, 8, true);
        GenRng r8 = SR(8);
        for (int k = 0; k < 3 && Fit(1); k++) if (!Island(ref r8)) break;
    }

    // a vegetable plot around a cell: a bed of soil 3 x 2 m with two rows of crops along it
    void VegPlot(int cell, ref GenRng rng)
    {
        int room = pRoom;
        int cx = cell % IW, cz = cell / IW;
        bool alongX = RoomLongAxis(room) == 0;
        int hw = alongX ? 3 : 2, hd = alongX ? 2 : 3;
        for (int z = cz - hd; z < cz + hd; z++)
            for (int x = cx - hw; x < cx + hw; x++)
            {
                if (x < 0 || z < 0 || x >= IW || z >= IH) continue;
                int i = z * IW + x;
                if (innerRoom[i] == room && occ[i] == 0 && (tags[i] & (GenData.T_PATH | GenData.T_DOOR_CLEAR | GenData.T_COVERED)) == 0) tags[i] |= GenData.T_SOFT_GROUND;
            }
        string[] crops = { "vegCarrot", "vegPumpkin", "vegMelon" };
        int c0 = rng.Range(3);
        int mx = cx * GenData.INNER_MM, mz = cz * GenData.INNER_MM;
        for (int row = 0; row < 2 && Fit(3); row++)
        {
            int g = GenData.FindGrp(crops[(c0 + row) % 3]);
            if (ChooseMembers(g, room, rng.Between(3, 4), ref rng) < 3) continue;
            BuildFormation(GenData.GL_ROW, GenData.grpGapMm[g], ref rng);
            int first = nEmit;
            int across = (row == 0 ? -1 : 1) * 450 - fDep / 2;
            int rot = alongX ? 0 : 1;
            fHost = H_NATURE;
            int e = StampFormation(room, rot, (alongX ? mx : mz) - fLen / 2, (alongX ? mz : mx) + across, -1, 0, g, -1);
            fHost = H_NONE;
            if (e >= 0) Keep(first, false);
        }
    }

    // ============================================================================================ PATIO
    // Areas: the paved terrace at the door with the table set on it; loungers at its edge; planters; the rest
    // is lawn with its borders.
    void PlanPatio()
    {
        int room = pRoom;
        FrontMarkers(30);
        GenRng r = SR(1);
        // (a) the paving: 4 x 4 m in front of the first door, or against an edge, or in the middle
        int px0, pz0, px1, pz1;
        int k = roomDoorCount[room] > 0 ? 0 : -1;
        if (k >= 0)
        {
            int cell = roomDoors[roomDoorStart[room]], d = roomDoorDir[roomDoorStart[room]], o = (d + 2) & 3;
            int cx = cell % IW, cz = cell / IW;
            // 6 m along the wall, 4 m deep; the door near one end, so the table has the wider part beside the way out
            int lowSide = r.Chance(500) ? 2 : 9;
            if (o == 0 || o == 2) { px0 = cx - lowSide; px1 = px0 + 11; pz0 = o == 0 ? cz : cz - 7; pz1 = o == 0 ? cz + 7 : cz; }
            else { pz0 = cz - lowSide; pz1 = pz0 + 11; px0 = o == 1 ? cx : cx - 7; px1 = o == 1 ? cx + 7 : cx; }
        }
        else
        {
            int mx = RoomMidX(room) / GenData.INNER_MM, mz = RoomMidZ(room) / GenData.INNER_MM;
            if (FindWallRun(room, 4000, 3500, 0, WM_ANY, 0, 0, WIN_ANY, ref r))
            {
                int x, z, rot;
                WallPos(wrDir, wrBack, wrCentre, 3500, out x, out z, out rot);
                mx = x / GenData.INNER_MM; mz = z / GenData.INNER_MM;
            }
            px0 = mx - 4; px1 = mx + 3; pz0 = mz - 4; pz1 = mz + 3;
        }
        int paved = 0;
        for (int z = pz0; z <= pz1; z++)
            for (int x = px0; x <= px1; x++)
            {
                if (x < 0 || z < 0 || x >= IW || z >= IH) continue;
                int i = z * IW + x;
                if (innerRoom[i] == room) { tags[i] |= GenData.T_COVERED; paved++; }
            }
        int midX = (px0 + px1 + 1) * GenData.INNER_MM / 2, midZ = (pz0 + pz1 + 1) * GenData.INNER_MM / 2;
        for (int q = 1; q < roomDoorCount[room]; q++) { GenRng rq = SR(40 + q); StonePath(room, q, 7 + rq.Range(6), ref rq); }
        // (b) the table set on the paving
        int setDef = Pick(ref r, "k_table", 5, "r_tableRound", 5);
        int set = CentrePiece(setDef, 0, true, 2, 1, WM_NEAR, midX, midZ, GenData.T_COVERED, EF_MANDATORY, ref r);
        if (set < 0) set = CentrePiece(Dn("r_tableRound"), 0, false, 1, 1, WM_NEAR, midX, midZ, 0, EF_MANDATORY, ref r);
        if (set < 0)
        {
            // a paving too narrow beside the door's walkway for a table set: a bench against the wall by the door
            int first = nEmit;
            if (WallPlace(Pick(ref r, "k_bench", 6, "k_benchCushion", 4), WM_NEAR, midX, midZ, WIN_ANY, EF_MANDATORY, ref r) < 0 || !Keep(first, true)) MandFail(1);
        }
        // (c) loungers side by side on the paving, planters at the door, a bench against the wall
        GenRng r2 = SR(2);
        if (r2.Chance(600) && Fit(2))
        {
            int first = nEmit;
            fHost = H_FREE;
            bool ok = GroupAt(GenData.FindGrp("loungers"), GenData.A_COVERED, 2, WM_FAR, set >= 0 ? emitX[set] : midX, set >= 0 ? emitZ[set] : midZ, 0, ref r2);
            fHost = H_NONE;
            if (ok) Keep(first, false);
        }
        DoorSides(50);
        if (r2.Chance(550)) BenchAtEdge(3);
        GenRng r4 = SR(4);
        if (r4.Chance(500) && Fit(2))
        {
            int first = nEmit;
            if (GroupAt(GenData.FindGrp("pots"), GenData.A_OUT_EDGE, r4.Between(2, 3), WM_NEAR, midX, midZ, 0, ref r4)) Keep(first, false);
        }
        GenRng r5 = SR(5);
        if (r5.Chance(300) && Fit(1))
        {
            int first = nEmit;
            FSet(Dn("s_barrel"));
            if (FormAt(GenData.GL_ROW, 0, GenData.A_BUILDING, WM_ANY, 0, 0, 0, ref r5) >= 0) Keep(first, false);
        }
        int trees = TreeCount(room, ref r5);
        for (int t = 0; t < trees; t++) LawnTree(60 + t);
        ClearBans();
        Borders(10, 6, true);
        GenRng r6 = SR(6);
        for (int j = 0; j < 2 && Fit(1); j++) if (!Island(ref r6)) break;
    }

    // Markers at a plot's front (G42: a sign stands where it has a reason to, facing the people it is for). Seen
    // from the lane: a house's mailbox beside its garden gate (or beside its front door when the door opens onto the
    // lane), against the fence or the wall, facing the lane; a shop's name board beside its customer door when that
    // door opens onto the lane. Seen from a shop's forecourt: the name board beside the customer door against the
    // front wall, facing the forecourt and the lane. Nothing else places signs or mailboxes; they are never
    // wrong-category spawns (GenData.NoLeak).
    void FrontMarkers(int k0)
    {
        int room = pRoom, type = roomType[room];
        bool lane = type == GenData.R_LANE;
        if (!lane && roomPlace[room] != PL_FRONT) return;
        int n = 0;
        // doorways of this room into a building: a shop's customer door, or (from the lane) a house's front door
        for (int q = 0; q < roomDoorCount[room]; q++)
        {
            int i = roomDoorStart[room] + q, cell = roomDoors[i], d = roomDoorDir[i];
            int ox = cell % IW + DX[d], oz = cell / IW + DZ[d];
            if (ox < 0 || oz < 0 || ox >= IW || oz >= IH || !IsFrontDoor(oz * IW + ox, (d + 2) & 3)) continue;
            int zone = roomZone[innerRoom[oz * IW + ox]];
            if (zone == GenData.Z_HOUSE && !lane) continue;
            Marker(zone == GenData.Z_HOUSE ? "gen_mailbox" : "n_sign", cell % IW * GenData.INNER_MM + 250, cell / IW * GenData.INNER_MM + 250, k0 + n++);
        }
        if (!lane) return;
        // a house's garden opens onto the lane through a gap in the fence: the mailbox beside it
        for (int c = 0; c < W * H; c++)
        {
            if (cellRoom[c] != room) continue;
            int cx = c % W, cz = c / W;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + DX[d], nz = cz + DZ[d];
                if (!InGrid(nx, nz) || EdgeType(cx, cz, d) != GenData.E_NONE) continue;
                int nc = nz * W + nx, pl = cellPlot[nc];
                if (cellZone[nc] != GenData.Z_LAWN || pl < 0 || plotZone[pl] != GenData.Z_HOUSE || roomPlace[cellRoom[nc]] != PL_FRONT) continue;
                // the middle of the opening, half a metre into the lane
                int px = cx * GenData.CELL_MM + GenData.CELL_MM / 2 + DX[d] * (GenData.CELL_MM / 2 - 500);
                int pz = cz * GenData.CELL_MM + GenData.CELL_MM / 2 + DZ[d] * (GenData.CELL_MM / 2 - 500);
                Marker("gen_mailbox", px, pz, k0 + n++);
            }
        }
    }

    // a marker against the wall or fence nearest (px, pz), its back to it
    void Marker(string id, int px, int pz, int k)
    {
        if (!Fit(1)) return;
        GenRng r = SR(k);
        int first = nEmit;
        if (WallPlace(Dn(id), WM_NEAR, px, pz, WIN_ANY, 0, ref r) >= 0) Keep(first, false);
    }

    // ============================================================================================ LANE
    // The way in from the hunter release past the plots' gates to the wood (G32): a track kept open down its
    // middle. What stands on it stands against the fences: the sign at its start, a bench facing across, a bin by
    // a shop's front, planting between the gates.
    void PlanLane()
    {
        int room = pRoom;
        int rx = (releaseX * 8 + 4) * GenData.INNER_MM, rz = (releaseZ * 8 + 4) * GenData.INNER_MM;
        GenRng r = SR(1);
        if (Fit(1))
        {
            int first = nEmit;
            if (WallPlace(Dn("n_sign"), WM_NEAR, rx, rz, WIN_ANY, 0, ref r) >= 0) Keep(first, false);
        }
        FrontMarkers(30);
        BenchAtEdge(2);
        if (AreaM2(room) >= 100) BenchAtEdge(3);
        GenRng r4 = SR(4);
        if (r4.Chance(600) && Fit(1))
        {
            int first = nEmit;
            if (WallPlace(Dn("k_trashcan"), WM_ANY, 0, 0, WIN_ANY, 0, ref r4) >= 0) Keep(first, false);
        }
        ClearBans();
        Borders(10, 6, false);
    }

    // ============================================================================================ YARD
    // A warehouse's loading yard or a diner's service yard (G32): paved. The goods wait against the building and
    // the fence near the loading or service door: pallets, then rows of crates, boxes, barrels, sacks, fuel; the
    // hand truck and the dumpster (or a bin) by the door. The way from the door to the gates stays open.
    void PlanYard()
    {
        int room = pRoom, dx, dz;
        FrontMarkers(30);
        MainDoor(room, out dx, out dz);
        bool diner = false;
        for (int k = 0; k < roomDoorCount[room]; k++) if (DoorZone(room, k) == GenData.Z_DINER) diner = true;
        GenRng r = SR(1);
        if (!diner && Fit(2))
        {
            // pallets side by side against the building beside the dock door
            int first = nEmit;
            FSet(Dn("w_pallet"), Dn(r.Chance(500) ? "w_palletLoaded" : "w_pallet"));
            if (FormAt(GenData.GL_ROW, 100, GenData.A_BUILDING, WM_NEAR, dx, dz, 0, ref r) >= 0) Keep(first, false);
        }
        string[] rows = diner ? new string[] { "crates", "barrels", "boxes", "sacks" } : new string[] { "crates", "barrels", "fuel", "boxes", "sacks" };
        for (int k = 0; k < rows.Length; k++)
        {
            GenRng rk = SR(2 + k);
            if (!rk.Chance(k < 2 ? 800 : 500) || !Fit(2)) continue;
            int first = nEmit;
            if (GroupAt(GenData.FindGrp(rows[k]), GenData.A_OUT_EDGE, rk.Between(2, 3), WM_NEAR, dx, dz, 0, ref rk)) Keep(first, false);
        }
        GenRng r8 = SR(8);
        if (!diner && r8.Chance(700) && Fit(1))
        {
            int first = nEmit;
            if (WallPlace(Dn("ms_handTruck"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r8) >= 0) Keep(first, false);
        }
        GenRng r9 = SR(9);
        if (r9.Chance(750) && Fit(1))
        {
            // the waste: a dumpster at the fence or the wall near the door, else a bin
            int first = nEmit;
            if (WallPlace(Dn("gen_dumpster"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r9) < 0 || !Keep(first, false))
            {
                first = nEmit;
                if (WallPlace(Dn("k_trashcan"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r9) >= 0) Keep(first, false);
            }
        }
        GenRng r10 = SR(10);
        if (r10.Chance(450) && Fit(1))
        {
            int first = nEmit;
            if (WallPlace(Pick(ref r10, "s_boxLarge", 5, "w_planks", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r10) >= 0) Keep(first, false);
        }
    }

    // ============================================================================================ FIRE PIT
    // Areas: the fire on trodden earth with its ring of seats; the wood pile and the chopping block at the edge;
    // a bucket of water by the fire.
    int FireRing(int minClr, int tx, int tz, bool soft, ref GenRng rng)
    {
        int room = pRoom;
        int fire = CentrePiece(Pick(ref rng, "n_campfire", 5, "s_campfirePit", 3), rng.Range(4) * 90, false, 3, 1, WM_NEAR, tx, tz, 0, EF_MANDATORY, ref rng);
        if (fire < 0) { MandFail(1); return -1; }
        // trodden earth around the fire, about 2 m out; in a forest the clearing where no tree stands is wider
        int fx = FloorDiv(emitX[fire], GenData.INNER_MM), fz = FloorDiv(emitZ[fire], GenData.INNER_MM);
        for (int z = fz - 6; z <= fz + 6; z++)
            for (int x = fx - 6; x <= fx + 6; x++)
            {
                if (x < 0 || z < 0 || x >= IW || z >= IH || innerRoom[z * IW + x] != room) continue;
                int dd = (x - fx) * (x - fx) + (z - fz) * (z - fz);
                if (dd <= 14) tags[z * IW + x] |= GenData.T_EARTH;
                if (dd <= (soft ? 36 : 14)) tags[z * IW + x] |= GenData.T_GLADE;
            }
        // in the wood the tent comes first (a narrow clearing has room for little else), then the gear (PlanForest)
        if (!soft) FireGear(fire, ref rng);
        return fire;
    }

    // a few logs beside the fire, and the bucket of water
    void FireGear(int fire, ref GenRng rng)
    {
        if (rng.Chance(650) && Fit(2))
        {
            int first = nEmit;
            if (GroupBeside(GenData.FindGrp("logpile"), fire, 2, 0, ref rng)) Keep(first, false);
        }
        if (rng.Chance(600) && Fit(1))
        {
            int first = nEmit;
            FSet(Pick(ref rng, "m_bucketWater", 6, "s_bucket", 3));
            if (FormBeside(GenData.GL_ROW, 0, fire, 0, ref rng) >= 0) Keep(first, false);
        }
    }

    // Something that belongs to a fire but stands back from its ring of seats (the tent, the wood pile): at one
    // of eight bearings from the fire, the nearest distance that is free, turned by relYaw to the bearing (180 =
    // facing the fire). offGates: not on the line from an entrance to the fire. Kept, or -1.
    int AroundFire(int def, int fire, int relYaw, int distMin, int distMax, int flags, bool must, bool offGates, ref GenRng rng)
    {
        int room = pRoom, fx = emitX[fire], fz = emitZ[fire];
        int a0 = rng.Range(8);
        for (int k = 0; k < 8; k++)
        {
            int a = (a0 + k) % 8 * 45;
            for (int dist = distMin; dist <= distMax; dist += 300)
            {
                int px = fx + dist * sinTab[a] / 4096, pz = fz + dist * sinTab[(a + 90) % 360] / 4096;
                int ix = FloorDiv(px, GenData.INNER_MM), iz = FloorDiv(pz, GenData.INNER_MM);
                if (ix < 0 || iz < 0 || ix >= IW || iz >= IH || innerRoom[iz * IW + ix] != room) break;
                if (offGates && NearGateLine(px, pz, fx, fz)) break;
                int first = nEmit;
                int e = StampAt(def, room, px, pz, (a + relYaw) % 360, flags, -1, fire, false);
                if (e >= 0 && Keep(first, must)) return e;
            }
        }
        return -1;
    }

    // is the point within 1.6 m of the straight line from one of the area's entrances to (hx, hz)?
    bool NearGateLine(int px, int pz, int hx, int hz)
    {
        for (int g = 0; g < nGate; g++)
        {
            int gx = gateCell[g] % IW * GenData.INNER_MM + 250, gz = gateCell[g] / IW * GenData.INNER_MM + 250;
            long vx = hx - gx, vz = hz - gz, wx = px - gx, wz = pz - gz;
            long len2 = vx * vx + vz * vz;
            if (len2 == 0) continue;
            long t = wx * vx + wz * vz;
            if (t < 0) t = 0; else if (t > len2) t = len2;
            long cx = gx + vx * t / len2 - px, cz = gz + vz * t / len2 - pz;
            if (cx * cx + cz * cz < 1600L * 1600L) return true;
        }
        return false;
    }

    // the wood pile: a stack of logs with the chopping block beside it (the axe may stick in the block)
    void WoodPile(int fire, bool atEdge, ref GenRng rng)
    {
        if (!Fit(1)) return;
        int first = nEmit;
        int stackDef = Pick(ref rng, "n_logStack", 6, "n_logStackLarge", 3);
        int stack = -1;
        if (atEdge) stack = WallKeep(stackDef, fire >= 0 ? WM_NEAR : WM_ANY, fire >= 0 ? emitX[fire] : 0, fire >= 0 ? emitZ[fire] : 0, WIN_ANY, 0, ref rng);
        if (stack < 0 && fire >= 0) stack = AroundFire(stackDef, fire, 90, 2700, 3600, 0, false, false, ref rng);
        if (stack < 0 && fire >= 0 && stackDef != Dn("n_logStack")) stack = AroundFire(Dn("n_logStack"), fire, 90, 2400, 3600, 0, false, false, ref rng);
        if (stack < 0 && !atEdge) stack = WallKeep(Dn("n_logStack"), WM_ANY, 0, 0, WIN_ANY, 0, ref rng);
        if (stack < 0) return;
        if (rng.Chance(750) && Fit(1))
        {
            first = nEmit;
            if (RelBeside(Pick(ref rng, "n_stump", 6, "n_stumpRound", 4), stack, 0, 250, 0, ref rng) >= 0) Keep(first, false);
        }
    }

    // The ring of seats round a fire: two or three, 1.3 to 1.9 m from it, each facing it (a bench), lying across the
    // bearing (a log) or standing (a stump); in a garden a bench is likeliest, in the wood logs and stumps.
    void FireSeats(int fire, bool wood, int k)
    {
        if (fire < 0) return;
        GenRng rng = SR(k);
        int n = rng.Between(2, 3);
        for (int s = 0, tries = 0; s < n && tries < 6 && Fit(1); tries++)
        {
            int def = wood ? Pick(ref rng, "w_logA", 5, "n_stumpRound", 3, "n_stump", 2) : Pick(ref rng, "k_bench", 4, "w_logA", 3, "n_stumpRound", 3);
            if (CountInRoom(def, pRoom) >= ScaledCap(GenData.defRoomCap[def], pRoom)) continue;
            int rel = def == Dn("k_bench") ? 180 : (def == Dn("w_logA") ? 90 : 0);
            // a log is a small loose thing: it keeps within a step of the fire (G25: nothing loose alone in the open)
            if (AroundFire(def, fire, rel, 1300, def == Dn("w_logA") ? 1600 : 1900, 0, false, false, ref rng) >= 0) s++;
        }
    }

    void PlanFirepit()
    {
        int room = pRoom;
        GenRng r = SR(1);
        StonePaths(40, ref r);
        int fire = FireRing(6, RoomMidX(room), RoomMidZ(room), false, ref r);
        FireSeats(fire, false, 7);
        GenRng r2 = SR(2);
        if (r2.Chance(850)) WoodPile(fire, true, ref r2);
        DoorSides(50);
        GenRng r3 = SR(3);
        int trees = TreeCount(room, ref r3);
        for (int t = 0; t < trees; t++) LawnTree(60 + t);
        if (r3.Chance(350)) BenchAtEdge(4);
        ClearBans();
        Borders(10, 6, false);
        GenRng r5 = SR(5);
        for (int k = 0; k < 2 && Fit(1); k++) if (!Island(ref r5)) break;
    }

    // ============================================================================================ FOREST
    // the open edges of a forest room towards other outdoor areas: every second one is an entrance. The choice
    // hashes the edge, so the two areas that share it agree. Returns the entrances' inner cells (inside this room).
    int nGate;
    int[] gateCell = new int[4];

    void FindGates(int room)
    {
        nGate = 0;
        int firstOpen = -1;
        for (int c = 0; c < W * H; c++)
        {
            if (cellRoom[c] != room || cellZone[c] == GenData.Z_NONE) continue;
            int cx = c % W, cz = c / W;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + DX[d], nz = cz + DZ[d];
                if (!InGrid(nx, nz) || cellZone[nz * W + nx] == GenData.Z_NONE || cellRoom[nz * W + nx] == room) continue;
                if (EdgeType(cx, cz, d) != GenData.E_NONE) continue;
                int lx = d == 1 ? 7 : (d == 3 ? 0 : 3), lz = d == 0 ? 7 : (d == 2 ? 0 : 3);
                int inner = (cz * 8 + lz) * IW + cx * 8 + lx;
                if (firstOpen < 0) firstOpen = inner;
                bool h;
                int ei = EdgeIndex(cx, cz, d, out h);
                if ((GenRng.Hash32(seed, (uint)GenData.GEN_VERSION, 91, (uint)(ei * 2 + (h ? 1 : 0)), (uint)size) & 1) != 0) continue;
                if (nGate < gateCell.Length) gateCell[nGate++] = inner;
            }
        }
        if (nGate == 0 && firstOpen >= 0) gateCell[nGate++] = firstOpen;
    }

    // a walked path, three cells wide, from one cell to another by way of a point a little off the straight line
    void Track(int room, int from, int to, ref GenRng rng)
    {
        int x0 = from % IW, z0 = from / IW, x1 = to % IW, z1 = to / IW;
        int mx = (x0 + x1) / 2 + rng.Between(-2, 2), mz = (z0 + z1) / 2 + rng.Between(-2, 2);
        TrackLine(room, x0, z0, mx, mz);
        TrackLine(room, mx, mz, x1, z1);
    }

    void TrackLine(int room, int x0, int z0, int x1, int z1)
    {
        int steps = Abs(x1 - x0) > Abs(z1 - z0) ? Abs(x1 - x0) : Abs(z1 - z0);
        if (steps == 0) steps = 1;
        for (int s = 0; s <= steps; s++)
        {
            int x = x0 + (x1 - x0) * s / steps, z = z0 + (z1 - z0) * s / steps;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx != 0 && dz != 0) continue;
                    int jx = x + dx, jz = z + dz;
                    if (jx < 0 || jz < 0 || jx >= IW || jz >= IH) continue;
                    int j = jz * IW + jx;
                    if (innerRoom[j] != room || solid[j] > 0 || occ[j] != 0) continue;   // the path ends where the camp begins
                    if ((tags[j] & (GenData.T_NO_SPAWN | GenData.T_EARTH)) == 0) tags[j] |= GenData.T_PATH;
                }
        }
    }

    // a stand of n trees around a point: one kind, now and then a second among them
    int Stand(int cx, int cz, int n, int main, int accent, ref GenRng rng)
    {
        int placed = 0;
        for (int tries = n * 6; tries > 0 && placed < n; tries--)
        {
            int a = rng.Range(360), rad = placed == 0 ? rng.Range(500) : 1200 + rng.Range(2000);
            int px = cx + rad * sinTab[a] / 4096, pz = cz + rad * sinTab[(a + 90) % 360] / 4096;
            int def = (accent >= 0 && rng.Chance(250)) ? accent : main;
            if (TreeAt(def, px, pz, ref rng) >= 0) placed++;
        }
        return placed;
    }

    int standN;
    int[] standX = new int[16], standZ = new int[16];

    void PlanForest(bool camp)
    {
        int room = pRoom, area = AreaM2(room);
        GenRng r = SR(1);
        // (a) the middle of the area and the path to it from the entrances
        int hub = OpenSpot(2, WM_NEAR, RoomMidX(room), RoomMidZ(room), 0, ref r);
        if (hub < 0) hub = roomCells[roomCellStart[room] + roomCellCount[room] / 2];
        int hubX = hub % IW * GenData.INNER_MM + 250, hubZ = hub / IW * GenData.INNER_MM + 250;
        int fire = -1, tent = -1;
        if (camp)
        {
            // (b) the camp in its clearing: the fire in the middle, the tent facing it
            fire = FireRing(7, hubX, hubZ, true, ref r);
            if (fire >= 0) { hubX = emitX[fire]; hubZ = emitZ[fire]; hub = FloorDiv(hubZ, GenData.INNER_MM) * IW + FloorDiv(hubX, GenData.INNER_MM); }
        }
        FindGates(room);
        if (camp && fire >= 0)
        {
            // the tent across the fire from the way in, its door to the fire; the small tent if the large one has no room
            GenRng r2 = SR(2);
            int tentDef = Pick(ref r2, "n_tentOpen", 5, "n_tentClosed", 3);
            tent = AroundFire(tentDef, fire, 180, 2600, 3800, EF_MANDATORY, true, true, ref r2);
            if (tent < 0 && tentDef != Dn("n_tentClosed")) tent = AroundFire(Dn("n_tentClosed"), fire, 180, 2600, 3800, EF_MANDATORY, true, true, ref r2);
            // a narrow clearing: the tent with its back to the fence, as near the fire as the fence allows
            if (tent < 0) tent = WallKeep(tentDef, WM_NEAR, hubX, hubZ, WIN_ANY, EF_MANDATORY, ref r2);
            if (tent < 0) tent = WallKeep(Dn("n_tentClosed"), WM_NEAR, hubX, hubZ, WIN_ANY, EF_MANDATORY, ref r2);
            if (tent < 0) tent = AroundFire(Dn("n_tentClosed"), fire, 180, 2300, 3800, EF_MANDATORY, true, false, ref r2);
            // a clearing too narrow for any tent (a strip of wood two cells long): a bedroll laid out by the fire,
            // its foot to the fire, the gear beside it as beside a tent
            if (tent < 0) tent = AroundFire(Dn("s_bedroll"), fire, 0, 1500, 2700, EF_MANDATORY, true, false, ref r2);
            if (tent < 0) tent = WallKeep(Dn("s_bedroll"), WM_NEAR, hubX, hubZ, WIN_ANY, EF_MANDATORY, ref r2);
            if (tent < 0) MandFail(1);
            FireSeats(fire, true, 8);
            GenRng rgear = SR(9);
            FireGear(fire, ref rgear);
        }
        // a piece of forest of one cell (4 x 4 m) is too small for a path of its own
        if (camp || roomOuterCells[room] >= 2)
            for (int g = 0; g < nGate; g++) { GenRng rg = SR(20 + g); Track(room, gateCell[g], hub, ref rg); }
        if (camp && fire >= 0)
        {
            GenRng r3 = SR(3);
            if (tent >= 0 && Fit(2))
            {
                // the gear beside the tent: a box or a chest with a barrel
                int first = nEmit;
                FSet(Pick(ref r3, "s_box", 5, "s_chest", 4), Dn("s_barrel"));
                if (FormBeside(GenData.GL_ROW, 80, tent, 0, ref r3) >= 0) Keep(first, false);
            }
            if (tent >= 0 && r3.Chance(600) && Fit(2))
            {
                int first = nEmit;
                if (GroupBeside(GenData.FindGrp("tools"), tent, 2, 0, ref r3)) Keep(first, false);
            }
            GenRng r4 = SR(4);
            if (r4.Chance(800)) WoodPile(fire, false, ref r4);
        }
        // (b) trees in stands, away from the path and the clearing; open ground between the stands
        GenRng rt = SR(5);
        int trees = TreeCount(room, ref rt);
        if (trees < 3) trees = 3;
        int stands = (trees + 3) / 4;
        if (stands > standX.Length) stands = standX.Length;
        standN = 0;
        int main0 = Pick(ref rt, "n_treeDefault", 6, "n_treePineTall", 6, "n_treeOak", 5);
        int left = trees;
        for (int s = 0; s < stands && left > 0; s++)
        {
            // the candidate farthest from the stands so far, with room for crowns
            int best = -1, bestD = -1;
            for (int k = 0; k < 14; k++)
            {
                int i = roomCells[roomCellStart[room] + rt.Range(roomCellCount[room])];
                if ((tags[i] & (GenData.T_PATH | GenData.T_GLADE | GenData.T_NO_SPAWN)) != 0 || occ[i] != 0) continue;
                int x = i % IW * GenData.INNER_MM + 250, z = i / IW * GenData.INNER_MM + 250;
                int dmin = 1 << 28;
                for (int q = 0; q < standN; q++) { int dd = Abs(standX[q] - x) + Abs(standZ[q] - z); if (dd < dmin) dmin = dd; }
                int hx, hz;
                FullHalf(main0, 0, out hx, out hz);
                if (!WallsClear(x - hx, z - hz, x + hx, z + hz, true)) dmin /= 8;   // too near an edge for a crown
                if (dmin > bestD) { bestD = dmin; best = i; }
            }
            if (best < 0) break;
            int sx = best % IW * GenData.INNER_MM + 250, sz = best / IW * GenData.INNER_MM + 250;
            int kind = s == 0 ? main0 : (rt.Chance(190) ? Dn("n_treePineRound") : Pick(ref rt, "n_treeDefault", 6, "n_treePineTall", 6, "n_treeOak", 5));
            int accent = rt.Chance(400) ? Pick(ref rt, "n_treePineRound", 4, "n_treeOak", 4, "n_treeDefault", 4) : -1;
            int want = left < 3 ? left : rt.Between(3, left < 5 ? left : 5);
            int got = Stand(sx, sz, want, kind, accent, ref rt);
            left -= got;
            if (got > 0 && standN < standX.Length) { standX[standN] = sx; standZ[standN] = sz; standN++; }
        }
        // crowns have to stay inside the fence (G31), so a stand near an edge comes out short: the trees still
        // missing join the stands that stand, wherever a crown has the room
        for (int tries = left * 12; tries > 0 && left > 0 && standN > 0; tries--)
        {
            int s = rt.Range(standN), a = rt.Range(360), rad = 1200 + rt.Range(2400);
            if (TreeAt(main0, standX[s] + rad * sinTab[a] / 4096, standZ[s] + rad * sinTab[(a + 90) % 360] / 4096, ref rt) >= 0) left--;
        }
        // (c) undergrowth: bushes at the edge of a stand and along the fence
        GenRng r6 = SR(6);
        int bushes = 1 + area / 28;
        for (int b = 0; b < bushes && Fit(1); b++)
        {
            int first = nEmit;
            bool ok = false;
            if (standN > 0 && r6.Chance(600))
            {
                int s = r6.Range(standN), a = r6.Range(360), rad = 1500 + r6.Range(1200);
                int px = standX[s] + rad * sinTab[a] / 4096, pz = standZ[s] + rad * sinTab[(a + 90) % 360] / 4096;
                int ix = FloorDiv(px, GenData.INNER_MM), iz = FloorDiv(pz, GenData.INNER_MM);
                if (ix >= 0 && iz >= 0 && ix < IW && iz < IH && innerRoom[iz * IW + ix] == room && (tags[iz * IW + ix] & GenData.T_GLADE) == 0)
                    ok = StampAt(Pick(ref r6, "n_bush", 5, "n_bushDetailed", 4), room, px, pz, r6.Range(360), 0, -1, H_NATURE, false) >= 0;
            }
            if (!ok) ok = WallPlace(Pick(ref r6, "n_bushLarge", 5, "n_bush", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r6) >= 0;
            if (ok) Keep(first, false);
        }
        // (d) dead wood as one feature: a fallen log, a stump beside it, mushrooms at the stump
        GenRng r7 = SR(7);
        if (r7.Chance(camp ? 350 : 800) && Fit(2)) DeadWood(ref r7);
        // a boulder with smaller rocks beside it
        GenRng r8 = SR(8);
        if (r8.Chance(camp ? 300 : 600) && Fit(2))
        {
            int cell = SoftSpot(ref r8);
            int first = nEmit;
            int rock = cell >= 0 ? FreePlace(Pick(ref r8, "n_rockLarge", 5, "n_rockTall", 3), cell, r8.Range(360), 0, 0, H_NATURE) : -1;
            if (rock >= 0 && Keep(first, false) && Fit(2))
            {
                first = nEmit;
                if (GroupBeside(GenData.FindGrp("rocks"), rock, r8.Between(2, 3), 0, ref r8)) Keep(first, false);
            }
        }
        // mushrooms at the foot of a tree, a drift of flowers by the path, a signpost at an entrance
        GenRng r9 = SR(9);
        int clumps = camp ? 1 : 1 + area / 50;
        for (int m = 0; m < clumps && Fit(2); m++)
        {
            int first = nEmit;
            if (PlaceGroup(GenData.FindGrp("mushrooms"), room, GenData.A_BESIDE, r9.Between(2, Fit(4) ? 4 : 2), 0, ref r9)) Keep(first, false);
        }
        GenRng r10 = SR(10);
        if (r10.Chance(600) && Fit(3))
        {
            int first = nEmit;
            if (GroupAt(FlowerGroup(ref r10), GenData.A_SOFT, r10.Between(3, Fit(5) ? 5 : 3), WM_NEAR, hubX, hubZ, 0, ref r10)) Keep(first, false);
        }
        GenRng r11 = SR(11);
        if (nGate > 0 && r11.Chance(400) && Fit(1)) Signpost(gateCell[0], ref r11);
        // fill up to the area's budget: more undergrowth at the fence, rocks and stumps on open ground (G29)
        GenRng r12 = SR(12);
        for (int k = 0; k < 10 && Fit(1); k++)
        {
            int first = nEmit;
            int roll = r12.Range(10);
            bool ok;
            if (roll < 4) ok = WallPlace(Pick(ref r12, "n_bushLarge", 5, "n_bush", 4, "n_bushDetailed", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r12) >= 0;
            else if (roll < 7 && Fit(2)) ok = GroupAt(GenData.FindGrp("rocks"), GenData.A_SOFT, r12.Between(2, 3), WM_ANY, 0, 0, 0, ref r12);
            else
            {
                int cell = SoftSpot(ref r12);
                ok = cell >= 0 && FreePlace(Pick(ref r12, "n_stump", 5, "n_stumpRound", 4, "n_bush", 3), cell, r12.Range(360), 0, 0, H_NATURE) >= 0;
            }
            if (ok) Keep(first, false);
        }
        // grass tufts in patches under the stands (ground cover, not props)
        GenRng r13 = SR(13);
        int type = roomType[room];
        int grass = r13.Between(PerArea(GenData.rangeDecorMin[type], room, false), PerArea(GenData.rangeDecorMax[type], room, false));
        for (int tries = grass * 4; tries > 0 && grass > 0 && standN > 0; tries--)
        {
            int s = r13.Range(standN), a = r13.Range(360), rad = 400 + r13.Range(1800);
            int px = standX[s] + rad * sinTab[a] / 4096, pz = standZ[s] + rad * sinTab[(a + 90) % 360] / 4096;
            int ix = FloorDiv(px, GenData.INNER_MM), iz = FloorDiv(pz, GenData.INNER_MM);
            if (ix < 0 || iz < 0 || ix >= IW || iz >= IH || innerRoom[iz * IW + ix] != room) continue;
            if ((tags[iz * IW + ix] & (GenData.T_PATH | GenData.T_GLADE)) != 0) continue;
            if (StampAt(Dn(r13.Chance(600) ? "n_grass" : "n_grassLarge"), room, px, pz, r13.Range(360), 0, -1, H_NATURE, false) >= 0) grass--;
        }
    }

    // a free cell of forest floor off the path and out of the clearing, with a little room around it
    int SoftSpot(ref GenRng rng) { return SoftSpot(2, ref rng); }

    int SoftSpot(int minClr, ref GenRng rng)
    {
        int room = pRoom;
        BuildClearance(room);
        for (int k = 0; k < 60; k++)
        {
            int i = roomCells[roomCellStart[room] + rng.Range(roomCellCount[room])];
            if (clr[i] < minClr || (tags[i] & (GenData.T_GLADE | GenData.T_PATH)) != 0) continue;
            return i;
        }
        return -1;
    }

    // A signpost where the wood's path starts (G42): on the forest floor right beside the path, within 2 m of the
    // entrance, turned to face the path, so whoever comes in along it reads it. None if no such spot is free.
    void Signpost(int gate, ref GenRng rng)
    {
        int room = pRoom, def = Dn("s_signpost");
        int gx = gate % IW, gz = gate / IW, a0 = rng.Range(4);
        for (int dist = 1; dist <= 4; dist++)
            for (int dz = -dist; dz <= dist; dz++)
                for (int dx = -dist; dx <= dist; dx++)
                {
                    if (Abs(dx) != dist && Abs(dz) != dist) continue;
                    int x = gx + dx, z = gz + dz;
                    if (x < 0 || z < 0 || x >= IW || z >= IH) continue;
                    int c = z * IW + x;
                    if (innerRoom[c] != room || (tags[c] & GenData.T_PATH) != 0) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int d = (a0 + k) & 3, px = x + DX[d], pz = z + DZ[d];
                        if (px < 0 || pz < 0 || px >= IW || pz >= IH || (tags[pz * IW + px] & GenData.T_PATH) == 0 || !CanStep(x, z, d)) continue;
                        int first = nEmit;
                        int e = StampAt(def, room, x * GenData.INNER_MM + 250, z * GenData.INNER_MM + 250, DirYaw(d), 0, -1, H_GROUND, false);
                        if (e >= 0 && KeepIfWalkable(room, first, false)) return;
                    }
                }
    }

    // the yaw (degrees) whose front looks along direction d (0 north +z, 1 east +x, 2 south, 3 west)
    static int DirYaw(int d) { return d == 0 ? 0 : (d == 1 ? 90 : (d == 2 ? 180 : 270)); }

    // a fallen log with a stump beside it and mushrooms at the stump (report 15, F-log and fungi, F-felled spot)
    void DeadWood(ref GenRng rng)
    {
        int cell = SoftSpot(ref rng);
        if (cell < 0) return;
        int first = nEmit;
        int logDef = Pick(ref rng, "n_log", 5, "n_logLarge", 3, "s_treeLog", 3);
        int log = FreePlace(logDef, cell, rng.Range(360), 0, 0, H_NATURE);
        if (log < 0 || !Keep(first, false)) return;
        int stump = -1;
        if (Fit(1))
        {
            first = nEmit;
            int sd = Pick(ref rng, "n_stump", 6, "n_stumpRound", 4);
            int side = rng.Chance(500) ? 1 : -1;
            stump = Rel(sd, log, side * (Sx(logDef) / 2 + 350 + Sx(sd) / 2), rng.Between(-400, 400), rng.Range(360), 0);
            if (stump < 0) stump = Rel(sd, log, -side * (Sx(logDef) / 2 + 350 + Sx(sd) / 2), 0, rng.Range(360), 0);
            if (stump >= 0 && !Keep(first, false)) stump = -1;
        }
        if (Fit(2))
        {
            first = nEmit;
            if (GroupBeside(GenData.FindGrp("mushrooms"), stump >= 0 ? stump : log, rng.Between(2, 3), 0, ref rng)) Keep(first, false);
        }
    }
}
