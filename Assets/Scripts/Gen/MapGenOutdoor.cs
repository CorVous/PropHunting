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
        hq_StonePaths_k0 = k0;
        hq_StonePaths_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); StonePaths_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_StonePaths_rng;
    }

    // StonePaths in phases, a step a call (autophase4); StonePaths() above runs them to the end
    void StonePaths_Body()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int room = pRoom;
            hq_StonePaths_room = room;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int k0 = hq_StonePaths_k0;
            int room = hq_StonePaths_room;
            int k = planIter;
            if (!(k < roomDoorCount[room])) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng r = SR(k0 + k);
                int i = roomDoorStart[room] + k, cell = roomDoors[i], d = roomDoorDir[i];
                int ox = cell % IW + DX[d], oz = cell / IW + DZ[d];
                bool front = ox >= 0 && oz >= 0 && ox < IW && oz < IH && IsFrontDoor(oz * IW + ox, (d + 2) & 3);
                StonePath(room, k, front ? 24 : 7 + r.Range(6), ref r);
            }
            hq_StonePaths_k0 = k0;
            hq_StonePaths_room = room;
        }
    }

    // ---- planting borders -------------------------------------------------------------------------------
    int borderColour;   // the flower colour of the last drift, so the next one differs

    // revision 30 (Cor: gardens can just have veggies): a vegetable garden's bed takes a row of one crop, the next
    // bed another
    static readonly string[] CropRows = { "vegCarrot", "vegPumpkin", "vegMelon", "rowLettuce", "rowTurnip", "rowLettuceYoung", "rowCornYoung" };

    int CropGroup(ref GenRng rng)
    {
        borderColour = (borderColour + 1 + rng.Range(2)) % CropRows.Length;
        return GenData.FindGrp(CropRows[borderColour]);
    }

    int FlowerGroup(ref GenRng rng)
    {
        borderColour = (borderColour + 1 + rng.Range(2)) % 3;
        return GenData.FindGrp(borderColour == 0 ? "flowersRed" : (borderColour == 1 ? "flowersYellow" : "flowersPurple"));
    }

    // A planted border along a free stretch of fence or wall, up to maxLen mm long: bushes, drifts of flowers of
    // one colour and groups of rocks side by side, with soil under it if wanted. Returns the props it added.
    int Border(int maxLen, bool soil, int mode, int tx, int tz, ref GenRng rng)
    {
        hq_Border_maxLen = maxLen;
        hq_Border_soil = soil;
        hq_Border_mode = mode;
        hq_Border_tx = tx;
        hq_Border_tz = tz;
        hq_Border_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); Border_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_Border_rng;
        return hq_Border_ret;
    }

    // Border in phases, a step a call (autophase4); Border() above runs them to the end
    void Border_Body()
    {
        planMax = 2;
        if (planPhase == 0)
        {
            int maxLen = hq_Border_maxLen;
            bool soil = hq_Border_soil;
            int mode = hq_Border_mode;
            int tx = hq_Border_tx;
            int tz = hq_Border_tz;
            GenRng rng = hq_Border_rng;
            int room = pRoom, added = 0;
            int len = maxLen;
            bool found = false;
            for (; len >= 1500 && !found; len -= 500)
                found = FindWallRun(room, len, 1300, 0, mode, tx, tz, WIN_ANY, ref rng);
            if (!found) { hq_Border_ret = 0; hq_Border_maxLen = maxLen; hq_Border_soil = soil; hq_Border_mode = mode; hq_Border_tx = tx; hq_Border_tz = tz; hq_Border_rng = rng; hq_Border_room = room; hq_Border_added = added; hq_Border_len = len; PhEnd(); return; }
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
            hq_Border_maxLen = maxLen;
            hq_Border_soil = soil;
            hq_Border_mode = mode;
            hq_Border_tx = tx;
            hq_Border_tz = tz;
            hq_Border_rng = rng;
            hq_Border_room = room;
            hq_Border_added = added;
            hq_Border_len = len;
            hq_Border_d = d;
            hq_Border_back = back;
            hq_Border_start = start;
            hq_Border_rot = rot;
            hq_Border_cursor = cursor;
            hq_Border_last = last;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int len = hq_Border_len;
            int start = hq_Border_start;
            int cursor = hq_Border_cursor;
            if (!PhSubActive())
            {
                if (!(cursor < start + len - 400 && Fit(1))) PhBreak();
                else { PhSubBegin(); }
            }
            hq_Border_len = len;
            hq_Border_start = start;
            hq_Border_cursor = cursor;
            if (PhSubActive())
            {
                PhPush(); Border_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 2)
        {
            int added = hq_Border_added;
            { hq_Border_ret = added; hq_Border_added = added; PhEnd(); return; }
            hq_Border_added = added;
        }
    }
    void Border_L1()
    {
        planMax = 5;
        if (planPhase == 0)
        {
            GenRng rng = hq_Border_rng;
            int last = hq_Border_last;
            int kind = rng.Range(10);
            kind = kind < 4 ? 0 : (kind < 8 ? 1 : 2);     // bush, drift, rocks
            if (kind == last && kind != 0) kind = 0;
            if (kind == 1 && !Fit(3)) kind = 0;
            if (kind == 2 && (!Fit(2) || !RocksAllowed())) kind = Fit(3) ? 1 : 0;
            if (hq_Border_soil && roomType[pRoom] == GenData.R_GARDEN) kind = 1;   // revision 30: a vegetable garden's beds hold crop rows
            int first = nEmit;
            hq_Border_sel1 = 0;
            if (kind == 0) hq_Border_sel1 = 1;
            else hq_Border_sel1 = 2;
            hq_Border_rng = rng;
            hq_Border_last = last;
            hq_Border_kind = kind;
            hq_Border_first = first;
        }
        if (planPhase == 1)
        {
            if (hq_Border_sel1 == 1)
            {
                GenRng rng = hq_Border_rng;
                int len = hq_Border_len;
                int d = hq_Border_d;
                int back = hq_Border_back;
                int start = hq_Border_start;
                int rot = hq_Border_rot;
                int cursor = hq_Border_cursor;
                int def = Pick(ref rng, "n_bushLarge", 4, "n_bush", 4, "n_bushDetailed", 3);
                int yaw = rot * 90 + Jitter(ref rng, 25);
                int hx, hz;
                FullHalf(def, yaw, out hx, out hz);
                int halfAlong = (d == 0 || d == 2) ? hx : hz, halfDeep = (d == 0 || d == 2) ? hz : hx;
                if (cursor + 2 * halfAlong > start + len) { hq_Border_rng = rng; hq_Border_len = len; hq_Border_d = d; hq_Border_back = back; hq_Border_start = start; hq_Border_rot = rot; hq_Border_cursor = cursor; hq_Border_def = def; hq_Border_yaw = yaw; hq_Border_halfAlong = halfAlong; phBreak = true; PhEnd(); return; }
                int px = 0, pz = 0, r2;
                WallPos(d, back, cursor + halfAlong, 2 * halfDeep + 60, out px, out pz, out r2);
                hq_Border_rng = rng;
                hq_Border_len = len;
                hq_Border_d = d;
                hq_Border_back = back;
                hq_Border_start = start;
                hq_Border_rot = rot;
                hq_Border_cursor = cursor;
                hq_Border_def = def;
                hq_Border_yaw = yaw;
                hq_Border_halfAlong = halfAlong;
                hq_Border_px = px;
                hq_Border_pz = pz;
            }
        }
        if (planPhase == 2)
        {
            if (hq_Border_sel1 == 1)
            {
                GenRng rng = hq_Border_rng;
                int room = hq_Border_room;
                int added = hq_Border_added;
                int cursor = hq_Border_cursor;
                int first = hq_Border_first;
                int def = hq_Border_def;
                int yaw = hq_Border_yaw;
                int halfAlong = hq_Border_halfAlong;
                int px = hq_Border_px;
                int pz = hq_Border_pz;
                if (StampAt(def, room, px, pz, yaw, 0, -1, H_WALL, false) >= 0 && Keep(first, false))
                {
                    added++;
                }
                cursor += 2 * halfAlong + 250 + rng.Range(350);
                hq_Border_rng = rng;
                hq_Border_room = room;
                hq_Border_added = added;
                hq_Border_cursor = cursor;
                hq_Border_first = first;
                hq_Border_def = def;
                hq_Border_yaw = yaw;
                hq_Border_halfAlong = halfAlong;
                hq_Border_px = px;
                hq_Border_pz = pz;
            }
        }
        if (planPhase == 3)
        {
            if (hq_Border_sel1 == 2)
            {
                GenRng rng = hq_Border_rng;
                int room = hq_Border_room;
                int len = hq_Border_len;
                int start = hq_Border_start;
                int cursor = hq_Border_cursor;
                int last = hq_Border_last;
                int kind = hq_Border_kind;
                int g = kind == 1 ? (hq_Border_soil && roomType[room] == GenData.R_GARDEN ? CropGroup(ref rng) : FlowerGroup(ref rng)) : GenData.FindGrp("rocks");
                int n = kind == 1 ? rng.Between(3, Fit(5) ? 5 : 3) : rng.Between(2, Fit(3) ? 3 : 2);
                if (ChooseMembers(g, room, n, ref rng) < 2) { cursor += 500; last = kind; { hq_Border_rng = rng; hq_Border_room = room; hq_Border_len = len; hq_Border_start = start; hq_Border_cursor = cursor; hq_Border_last = last; hq_Border_kind = kind; hq_Border_g = g; PhEnd(); return; } }
                BuildFormation(GenData.grpLayout[g], GenData.grpGapMm[g], ref rng);
                if (cursor + fLen > start + len) { hq_Border_rng = rng; hq_Border_room = room; hq_Border_len = len; hq_Border_start = start; hq_Border_cursor = cursor; hq_Border_last = last; hq_Border_kind = kind; hq_Border_g = g; phBreak = true; PhEnd(); return; }
                int before = roomFloor[room];
                hq_Border_rng = rng;
                hq_Border_room = room;
                hq_Border_len = len;
                hq_Border_start = start;
                hq_Border_cursor = cursor;
                hq_Border_last = last;
                hq_Border_kind = kind;
                hq_Border_g = g;
                hq_Border_before = before;
            }
        }
        if (planPhase == 4)
        {
            if (hq_Border_sel1 == 2)
            {
                GenRng rng = hq_Border_rng;
                int room = hq_Border_room;
                int added = hq_Border_added;
                int d = hq_Border_d;
                int back = hq_Border_back;
                int rot = hq_Border_rot;
                int cursor = hq_Border_cursor;
                int first = hq_Border_first;
                int g = hq_Border_g;
                int before = hq_Border_before;
                if (StampFormation(room, rot, cursor, back + (d == 2 || d == 3 ? 40 : -40), d, 0, g, -1) >= 0 && Keep(first, false))
                {
                    added += roomFloor[room] - before;
                }
                cursor += fLen + 250 + rng.Range(350);
                hq_Border_rng = rng;
                hq_Border_room = room;
                hq_Border_added = added;
                hq_Border_d = d;
                hq_Border_back = back;
                hq_Border_rot = rot;
                hq_Border_cursor = cursor;
                hq_Border_first = first;
                hq_Border_g = g;
                hq_Border_before = before;
            }
        }
        if (planPhase == 5)
        {
            int last = hq_Border_last;
            int kind = hq_Border_kind;
            last = kind;
            hq_Border_last = last;
            hq_Border_kind = kind;
        }
    }


    // borders until the room's floor budget is used or no stretch is left
    // one stretch of Borders: false when it stops
    bool BorderStep(int k0, int b, bool soil)
    {
        if (!Fit(1)) return false;
        GenRng r = SR(k0 + b);
        int longest = 2500 + r.Range(4) * 500;
        return Border(longest, soil, WM_ANY, 0, 0, ref r) != 0;
    }

    void Borders(int k0, int max, bool soil)
    {
        hq_Borders_k0 = k0;
        hq_Borders_max = max;
        hq_Borders_soil = soil;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); Borders_Body(); fin = PhAdvance(); PhPop(); }
    }

    // Borders in phases, a step a call (autophase4); Borders() above runs them to the end
    void Borders_Body()
    {
        planMax = 0;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int max = hq_Borders_max;
            if (!PhSubActive())
            {
                int b = planIter;
                if (!(b < max && Fit(1))) PhBreak();
                else { hq_Borders_b = b; PhSubBegin(); }
            }
            hq_Borders_max = max;
            if (PhSubActive())
            {
                PhPush(); Borders_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
    }
    void Borders_L1()
    {
        planMax = 2;
        if (planPhase == 0)
        {
            int k0 = hq_Borders_k0;
            int b = hq_Borders_b;
            GenRng r = SR(k0 + b);
            int longest = 2500 + r.Range(4) * 500;
            hq_Borders_k0 = k0;
            hq_Borders_r = r;
            hq_Borders_longest = longest;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            bool soil = hq_Borders_soil;
            GenRng r = hq_Borders_r;
            int longest = hq_Borders_longest;
            int ht1 = 0;
            if (planIter == 0) { hq_Border_maxLen = longest; hq_Border_soil = soil; hq_Border_mode = WM_ANY; hq_Border_tx = 0; hq_Border_tz = 0; hq_Border_rng = r; PhSubReset(); }
            PhPush(); Border_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_Border_rng; ht1 = hq_Border_ret; PhBreak(); }
            hq_Borders_soil = soil;
            hq_Borders_r = r;
            hq_Borders_longest = longest;
            hq_Borders_ht1 = ht1;
        }
        if (planPhase == 2)
        {
            int ht1 = hq_Borders_ht1;
            if (ht1 == 0)
            {
                { hq_Borders_ht1 = ht1; phBreak = true; PhEnd(); return; }
            }
            hq_Borders_ht1 = ht1;
        }
    }


    // A planting island for a stretch of open ground with no edge to plant along: a bush with rocks beside it.
    bool Island(ref GenRng rng)
    {
        hq_Island_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); Island_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_Island_rng;
        return hq_Island_ret;
    }

    // Island in phases, a step a call (autophase4); Island() above runs them to the end
    void Island_Body()
    {
        planMax = 6;
        if (planPhase == 0)
        {
            if (!Fit(1)) { hq_Island_ret = false; PhEnd(); return; }
            // revision 29 (Cor: nothing loose in the middle): a house's lawn and its purposes plant along their
            // fences only, never an island in the open
            int it = roomType[pRoom];
            if (it == GenData.R_LAWN_OPEN || it == GenData.R_FIREPIT || it == GenData.R_PICNIC || it == GenData.R_DRYING || it == GenData.R_CAMPSITE)
            { hq_Island_ret = false; PhEnd(); return; }
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            GenRng rng = hq_Island_rng;
            int cell = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = rng; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { rng = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
            hq_Island_rng = rng;
            hq_Island_cell = cell;
        }
        if (planPhase == 2)
        {
            GenRng rng = hq_Island_rng;
            int cell = hq_Island_cell;
            if (cell < 0) { hq_Island_ret = false; hq_Island_rng = rng; hq_Island_cell = cell; PhEnd(); return; }
            int first = nEmit;
            int bush = FreePlace(Pick(ref rng, "n_bush", 5, "n_bushDetailed", 4), cell, rng.Range(360), 0, 0, H_NATURE);
            hq_Island_rng = rng;
            hq_Island_cell = cell;
            hq_Island_first = first;
            hq_Island_bush = bush;
        }
        if (planPhase == 3)
        {
            int first = hq_Island_first;
            int bush = hq_Island_bush;
            if (bush < 0 || !Keep(first, false))
            {
                { hq_Island_ret = false; hq_Island_first = first; hq_Island_bush = bush; PhEnd(); return; }
            }
            hq_Island_sel1 = 0;
            if (Fit(2)) hq_Island_sel1 = 1;
            hq_Island_first = first;
            hq_Island_bush = bush;
        }
        if (planPhase == 4)
        {
            if (hq_Island_sel1 == 1)
            {
                GenRng rng = hq_Island_rng;
                int first = hq_Island_first;
                int bush = hq_Island_bush;
                first = nEmit;
                hq_Island_sel2 = 0;
                if (GroupBeside(RocksAllowed() ? GenData.FindGrp("rocks") : FlowerGroup(ref rng), bush, rng.Between(2, 3), 0, ref rng)) hq_Island_sel2 = 1;
                hq_Island_rng = rng;
                hq_Island_first = first;
                hq_Island_bush = bush;
            }
        }
        if (planPhase == 5 && PhLoop(5, 100000))
        {
            if (!(hq_Island_sel1 == 1 && hq_Island_sel2 == 1)) PhBreak();
            else
            {
                int first = hq_Island_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_Island_first = first;
            }
        }
        if (planPhase == 6)
        {
            { hq_Island_ret = true; PhEnd(); return; }
        }
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
    // one try of FillPass (k): FillToMinimum a try a step (Cor: build freezes)
    void FillOne(int pass, int k, bool forest)
    {
        hq_FillOne_pass = pass;
        hq_FillOne_k = k;
        hq_FillOne_forest = forest;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); FillOne_Body(); fin = PhAdvance(); PhPop(); }
    }

    // FillOne in phases, a step a call (autophase4); FillOne() above runs them to the end
    void FillOne_Body()
    {
        planMax = 16;
        if (planPhase == 0)
        {
            int pass = hq_FillOne_pass;
            int k = hq_FillOne_k;
            int room = pRoom, type = roomType[room];
            walkNarrow = k >= 8;   // first with the full walkway width, then passable is enough
            GenRng r = SR(200 + pass * 24 + k);
            int first = nEmit, how = k % 4;
            hq_FillOne_sel1 = 0;
            if (type == GenData.R_YARD) hq_FillOne_sel1 = 1;
            hq_FillOne_pass = pass;
            hq_FillOne_k = k;
            hq_FillOne_room = room;
            hq_FillOne_type = type;
            hq_FillOne_r = r;
            hq_FillOne_first = first;
            hq_FillOne_how = how;
        }
        if (planPhase == 1)
        {
            if (hq_FillOne_sel1 == 1)
            {
                int pass = hq_FillOne_pass;
                int k = hq_FillOne_k;
                GenRng r = hq_FillOne_r;
                int first = hq_FillOne_first;
                int how = hq_FillOne_how;
                // a yard fills up with what a yard holds: goods in rows at the fence and the wall, a pallet
                string[] rows = { "crates", "barrels", "boxes", "sacks" };
                if (how < 3 && GroupAt(GenData.FindGrp(rows[(k + pass) % 4]), GenData.A_OUT_EDGE, r.Between(2, 3), WM_ANY, 0, 0, 0, ref r) && Keep(first, false))
                {
                    { hq_FillOne_pass = pass; hq_FillOne_k = k; hq_FillOne_r = r; hq_FillOne_first = first; hq_FillOne_how = how; PhEnd(); return; }
                }
                first = nEmit;
                hq_FillOne_pass = pass;
                hq_FillOne_k = k;
                hq_FillOne_r = r;
                hq_FillOne_first = first;
                hq_FillOne_how = how;
            }
        }
        if (planPhase == 2)
        {
            if (hq_FillOne_sel1 == 1)
            {
                GenRng r = hq_FillOne_r;
                hq_FillOne_sel2 = 0;
                if (WallPlace(Pick(ref r, "w_pallet", 5, "w_palletLoaded", 3, "s_boxLarge", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r) >= 0) hq_FillOne_sel2 = 1;
                hq_FillOne_r = r;
            }
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (!(hq_FillOne_sel1 == 1 && hq_FillOne_sel2 == 1)) PhBreak();
            else
            {
                int first = hq_FillOne_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_FillOne_first = first;
            }
        }
        if (planPhase == 4)
        {
            if (hq_FillOne_sel1 == 1)
            {
                { PhEnd(); return; }
            }
        }
        if (planPhase == 5)
        {
            bool forest = hq_FillOne_forest;
            int type = hq_FillOne_type;
            GenRng r = hq_FillOne_r;
            int how = hq_FillOne_how;
            if (how == 0 && !forest) { if (Border(2500, type != GenData.R_FIREPIT, WM_ANY, 0, 0, ref r) > 0) { hq_FillOne_forest = forest; hq_FillOne_type = type; hq_FillOne_r = r; hq_FillOne_how = how; PhEnd(); return; } }
            hq_FillOne_forest = forest;
            hq_FillOne_type = type;
            hq_FillOne_r = r;
            hq_FillOne_how = how;
        }
        if (planPhase == 6)
        {
            int room = hq_FillOne_room;
            GenRng r = hq_FillOne_r;
            int first = hq_FillOne_first;
            int how = hq_FillOne_how;
            if (how <= 1)
            {
                first = nEmit;
                if (WallPlace(Pick(ref r, "n_bush", 5, "n_bushDetailed", 4, "n_bushLarge", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r) >= 0 && Keep(first, false)) { hq_FillOne_room = room; hq_FillOne_r = r; hq_FillOne_first = first; hq_FillOne_how = how; PhEnd(); return; }
            }
            hq_FillOne_sel3 = 0;
            if (how <= 2 && roomFloor[room] + 2 <= roomFloorTarget[room]) hq_FillOne_sel3 = 1;
            hq_FillOne_room = room;
            hq_FillOne_r = r;
            hq_FillOne_first = first;
            hq_FillOne_how = how;
        }
        if (planPhase == 7)
        {
            if (hq_FillOne_sel3 == 1)
            {
                GenRng r = hq_FillOne_r;
                int first = hq_FillOne_first;
                // rocks, or flowers once the area has its rocks
                first = nEmit;
                int g = RocksAllowed() ? GenData.FindGrp("rocks") : FlowerGroup(ref r);
                hq_FillOne_r = r;
                hq_FillOne_first = first;
                hq_FillOne_g = g;
            }
        }
        if (planPhase == 8)
        {
            if (hq_FillOne_sel3 == 1)
            {
                bool forest = hq_FillOne_forest;
                GenRng r = hq_FillOne_r;
                int first = hq_FillOne_first;
                int g = hq_FillOne_g;
                if (GroupAt(g, forest ? GenData.A_SOFT : GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r) && Keep(first, false))
                {
                    { hq_FillOne_forest = forest; hq_FillOne_r = r; hq_FillOne_first = first; hq_FillOne_g = g; PhEnd(); return; }
                }
                hq_FillOne_forest = forest;
                hq_FillOne_r = r;
                hq_FillOne_first = first;
                hq_FillOne_g = g;
            }
        }
        if (planPhase == 9)
        {
            bool forest = hq_FillOne_forest;
            GenRng r = hq_FillOne_r;
            hq_FillOne_sel4 = 0;
            if (forest) hq_FillOne_sel4 = 1;
            else if (!Island(ref r)) hq_FillOne_sel4 = 2;
            hq_FillOne_forest = forest;
            hq_FillOne_r = r;
        }
        if (planPhase == 10)
        {
            if (hq_FillOne_sel4 == 1)
            {
                int room = hq_FillOne_room;
                GenRng r = hq_FillOne_r;
                int first = hq_FillOne_first;
                int how = hq_FillOne_how;
                // mushrooms at the foot of a tree, a clump of flowers, a stump or a bush on open ground
                first = nEmit;
                if (how == 3 && roomFloor[room] + 2 <= roomFloorTarget[room] && PlaceGroup(GenData.FindGrp("mushrooms"), room, GenData.A_BESIDE, 2, 0, ref r) && Keep(first, false))
                {
                    { hq_FillOne_room = room; hq_FillOne_r = r; hq_FillOne_first = first; hq_FillOne_how = how; PhEnd(); return; }
                }
                first = nEmit;
                hq_FillOne_room = room;
                hq_FillOne_r = r;
                hq_FillOne_first = first;
                hq_FillOne_how = how;
            }
        }
        if (planPhase == 11)
        {
            if (hq_FillOne_sel4 == 1)
            {
                int room = hq_FillOne_room;
                GenRng r = hq_FillOne_r;
                int first = hq_FillOne_first;
                int how = hq_FillOne_how;
                if (how == 2 && roomFloor[room] + 3 <= roomFloorTarget[room] && GroupAt(FlowerGroup(ref r), GenData.A_SOFT, 3, WM_ANY, 0, 0, 0, ref r) && Keep(first, false))
                {
                    { hq_FillOne_room = room; hq_FillOne_r = r; hq_FillOne_first = first; hq_FillOne_how = how; PhEnd(); return; }
                }
                hq_FillOne_room = room;
                hq_FillOne_r = r;
                hq_FillOne_first = first;
                hq_FillOne_how = how;
            }
        }
        if (planPhase == 12)
        {
            if (hq_FillOne_sel4 == 1)
            {
                GenRng r = hq_FillOne_r;
                int first = hq_FillOne_first;
                int cell = SoftSpot(1, ref r);
                first = nEmit;
                hq_FillOne_r = r;
                hq_FillOne_first = first;
                hq_FillOne_cell = cell;
            }
        }
        if (planPhase == 13)
        {
            if (hq_FillOne_sel4 == 1)
            {
                GenRng r = hq_FillOne_r;
                int cell = hq_FillOne_cell;
                hq_FillOne_sel5 = 0;
                if (cell >= 0 && FreePlace(Pick(ref r, "n_stump", 5, "n_stumpRound", 4, "n_bush", 4), cell, r.Range(360), 0, 0, H_NATURE) >= 0) hq_FillOne_sel5 = 1;
                hq_FillOne_r = r;
                hq_FillOne_cell = cell;
            }
        }
        if (planPhase == 14 && PhLoop(14, 100000))
        {
            if (!(hq_FillOne_sel4 == 1 && hq_FillOne_sel5 == 1)) PhBreak();
            else
            {
                int first = hq_FillOne_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_FillOne_first = first;
            }
        }
        if (planPhase == 15)
        {
            if (hq_FillOne_sel4 == 2)
            {
                int room = hq_FillOne_room;
                GenRng r = hq_FillOne_r;
                int first = hq_FillOne_first;
                // no open ground for an island: a pot or a small rock group wherever an edge is free
                first = nEmit;
                FSet(Pick(ref r, "n_potSmall", 5, "n_planter", 5));
                hq_FillOne_sel6 = 0;
                if (CountInRoom(fDef[0], room) < ScaledCap(GenData.defRoomCap[fDef[0]], room) && FormAt(GenData.GL_ROW, 0, GenData.A_OUT_EDGE, WM_ANY, 0, 0, 0, ref r) >= 0) hq_FillOne_sel6 = 1;
                hq_FillOne_room = room;
                hq_FillOne_r = r;
                hq_FillOne_first = first;
            }
        }
        if (planPhase == 16 && PhLoop(16, 100000))
        {
            if (!(hq_FillOne_sel4 == 2 && hq_FillOne_sel6 == 1)) PhBreak();
            else
            {
                int first = hq_FillOne_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_FillOne_first = first;
            }
        }
    }

    int ftmPass, ftmK = -1, ftmSaved;
    bool ftmForest, ftmRun;

    // FillToMinimum a try a step; true when done. ftmK = -1 starts it.
    bool FillToMinimumStep()
    {
        int room = pRoom, type = roomType[room];
        if (ftmK < 0)
        {
            ftmForest = type == GenData.R_FOREST_DENSE || type == GenData.R_FOREST_CLEARING;
            ftmSaved = roomFloorTarget[room];
            if (roomFloorTarget[room] < roomFloorMin[room] + 2) roomFloorTarget[room] = roomFloorMin[room] + 2;
            coverExempt = true;
            ftmPass = 0; ftmK = 0; ftmRun = false;
            ClearBans();
            return false;
        }
        if (ftmRun || (ftmK < 24 && roomFloor[room] < roomFloorMin[room]))
        {
            // one try, in its own phases (FillOne_Body), a step a call
            if (!ftmRun) { hq_FillOne_pass = ftmPass; hq_FillOne_k = ftmK; hq_FillOne_forest = ftmForest; PhSubReset(); ftmRun = true; }
            PhPush(); FillOne_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { ftmRun = false; ftmK++; }
            return false;
        }
        bool stop = roomFloor[room] >= roomFloorMin[room] || !ftmForest || roomTrees[room] <= 1;
        if (!stop)
        {
            int tree = -1;
            for (int e = nEmit - 1; e >= roomEmitStart[room] && tree < 0; e--)
                if (emitRoom[e] == room && (emitFlags[e] & EF_REMOVED) == 0 && Has(emitDef[e], GenData.FL_SCENERY)) tree = e;
            if (tree < 0) stop = true;
            else Remove(tree);
        }
        if (!stop && ftmPass + 1 < 3) { ftmPass++; ftmK = 0; ClearBans(); return false; }
        coverExempt = false; walkNarrow = false;
        roomFloorTarget[room] = ftmSaved > roomFloor[room] ? ftmSaved : roomFloor[room];
        return true;
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
        hq_LawnTree_k = k;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); LawnTree_Body(); fin = PhAdvance(); PhPop(); }
        return hq_LawnTree_ret;
    }

    // LawnTree in phases, a step a call (autophase4); LawnTree() above runs them to the end
    void LawnTree_Body()
    {
        planMax = 5;
        if (planPhase == 0)
        {
            int k = hq_LawnTree_k;
            GenRng r = SR(k);
            osAvoid = GenData.T_COVERED | GenData.T_SOFT_GROUND | GenData.T_EARTH;
            hq_LawnTree_k = k;
            hq_LawnTree_r = r;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            GenRng r = hq_LawnTree_r;
            int cell = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = 4; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
            hq_LawnTree_r = r;
            hq_LawnTree_cell = cell;
        }
        if (planPhase == 2)
        {
            GenRng r = hq_LawnTree_r;
            int cell = hq_LawnTree_cell;
            if (cell < 0) { hq_LawnTree_ret = -1; hq_LawnTree_r = r; hq_LawnTree_cell = cell; PhEnd(); return; }
            int tree = TreeAt(Pick(ref r, "n_treeFat", 6, "n_treeOak", 4), cell % IW * GenData.INNER_MM + 250, cell / IW * GenData.INNER_MM + 250, ref r);
            hq_LawnTree_sel1 = 0;
            if (tree >= 0 && r.Chance(550) && Fit(2) && RocksAllowed()) hq_LawnTree_sel1 = 1;
            hq_LawnTree_r = r;
            hq_LawnTree_cell = cell;
            hq_LawnTree_tree = tree;
        }
        if (planPhase == 3)
        {
            if (hq_LawnTree_sel1 == 1)
            {
                GenRng r = hq_LawnTree_r;
                int tree = hq_LawnTree_tree;
                int first = nEmit;
                hq_LawnTree_sel2 = 0;
                if (GroupBeside(GenData.FindGrp("rocks"), tree, r.Between(2, 3), 0, ref r)) hq_LawnTree_sel2 = 1;
                hq_LawnTree_r = r;
                hq_LawnTree_tree = tree;
                hq_LawnTree_first = first;
            }
        }
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (!(hq_LawnTree_sel1 == 1 && hq_LawnTree_sel2 == 1)) PhBreak();
            else
            {
                int first = hq_LawnTree_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_LawnTree_first = first;
            }
        }
        if (planPhase == 5)
        {
            int tree = hq_LawnTree_tree;
            { hq_LawnTree_ret = tree; hq_LawnTree_tree = tree; PhEnd(); return; }
            hq_LawnTree_tree = tree;
        }
    }

    // ---- revision 30: scenery ground (Cor: the outside areas with just trees or rocks are ok inside the fence) -------
    // The lawn nobody needs, and the funfair's ring past its rides yard, planted with natural things only: trees,
    // bushes, big rocks (a space base's twins: crystals and rocks), two or three an outer cell, each kept only
    // if the area stays walkable, and now and then a few small rocks at a foot. The open edges to the next area stay
    // clear (YardOpenings). No prop, no leak, no hider spawn, no decorations (PopulateRoomStep, LeakAttempt, spawns).
    static readonly string[] SceneryPlants = { "n_treeFat", "n_treeOak", "n_treeDefault", "n_treePineRound", "n_bush", "n_bushLarge", "n_bushDetailed", "n_bush", "n_rockLarge", "n_rockTall" };
    int hq_PlanScenery_n, hq_PlanScenery_placed, hq_PlanScenery_last;
    GenRng hq_PlanScenery_r;

    void PlanScenery()
    {
        planMax = 2;
        int room = pRoom;
        if (planPhase == 0)
        {
            YardOpenings(room);
            GenRng r = SR(1);
            int cells = roomCellCount[room] / 64;
            if (cells < 1) cells = 1;
            hq_PlanScenery_n = cells * 2 + r.Range(cells + 1);
            hq_PlanScenery_placed = 0; hq_PlanScenery_last = -1;
            hq_PlanScenery_r = r;
        }
        // one plant a step (each a walkway test)
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            GenRng r = hq_PlanScenery_r;
            if (!(planIter < hq_PlanScenery_n * 4 && hq_PlanScenery_placed < hq_PlanScenery_n)) PhBreak();
            else
            {
                int cell = roomCells[roomCellStart[room] + r.Range(roomCellCount[room])];
                // a space base: no spires or boulders (a tree's twin is solid to its top, two metres and more across: in a
                // four-metre strip it closes the way), its crystals and rocks only
                int roll = mapTheme == GenData.THEME_SPACE ? 4 + r.Range(SceneryPlants.Length - 4) : r.Range(SceneryPlants.Length);
                int def = Dn(SceneryPlants[roll]);
                int e = TreeAt(def, cell % IW * GenData.INNER_MM + 250, cell / IW * GenData.INNER_MM + 250, ref r);
                if (e >= 0) { hq_PlanScenery_placed++; hq_PlanScenery_last = e; }
            }
            hq_PlanScenery_r = r;
        }
        // a few small rocks at the foot of the last one, now and then
        if (planPhase == 2)
        {
            GenRng r = hq_PlanScenery_r;
            if (hq_PlanScenery_last >= 0 && r.Chance(450))
            {
                int first = nEmit;
                if (GroupBeside(GenData.FindGrp("rocks"), hq_PlanScenery_last, r.Between(2, 3), 0, ref r)) Keep(first, false);
            }
        }
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
        hq_BenchAtEdge_k = k;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); BenchAtEdge_Body(); fin = PhAdvance(); PhPop(); }
    }

    // BenchAtEdge in phases, a step a call (autophase4); BenchAtEdge() above runs them to the end
    void BenchAtEdge_Body()
    {
        planMax = 3;
        if (planPhase == 0)
        {
            int k = hq_BenchAtEdge_k;
            GenRng r = SR(k);
            if (!Fit(1)) { hq_BenchAtEdge_k = k; hq_BenchAtEdge_r = r; PhEnd(); return; }
            int first = nEmit;
            int bench = WallPlace(Pick(ref r, "k_bench", 6, "k_benchCushion", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r);
            hq_BenchAtEdge_k = k;
            hq_BenchAtEdge_r = r;
            hq_BenchAtEdge_first = first;
            hq_BenchAtEdge_bench = bench;
        }
        if (planPhase == 1)
        {
            GenRng r = hq_BenchAtEdge_r;
            int first = hq_BenchAtEdge_first;
            int bench = hq_BenchAtEdge_bench;
            if (bench < 0 || !Keep(first, false))
            {
                { hq_BenchAtEdge_r = r; hq_BenchAtEdge_first = first; hq_BenchAtEdge_bench = bench; PhEnd(); return; }
            }
            int pot = Dn("n_potSmall");
            hq_BenchAtEdge_sel1 = 0;
            if (r.Chance(450) && Fit(1) && CountInRoom(pot, pRoom) < ScaledCap(GenData.defRoomCap[pot], pRoom)) hq_BenchAtEdge_sel1 = 1;
            hq_BenchAtEdge_r = r;
            hq_BenchAtEdge_first = first;
            hq_BenchAtEdge_bench = bench;
            hq_BenchAtEdge_pot = pot;
        }
        if (planPhase == 2)
        {
            if (hq_BenchAtEdge_sel1 == 1)
            {
                GenRng r = hq_BenchAtEdge_r;
                int first = hq_BenchAtEdge_first;
                int bench = hq_BenchAtEdge_bench;
                int pot = hq_BenchAtEdge_pot;
                first = nEmit;
                hq_BenchAtEdge_sel2 = 0;
                if (RelBeside(pot, bench, 0, 90, 0, ref r) >= 0) hq_BenchAtEdge_sel2 = 1;
                hq_BenchAtEdge_r = r;
                hq_BenchAtEdge_first = first;
                hq_BenchAtEdge_bench = bench;
                hq_BenchAtEdge_pot = pot;
            }
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (!(hq_BenchAtEdge_sel1 == 1 && hq_BenchAtEdge_sel2 == 1)) PhBreak();
            else
            {
                int first = hq_BenchAtEdge_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_BenchAtEdge_first = first;
            }
        }
    }

    // ============================================================================================ OPEN LAWN
    // in phases a step each (Cor: build freezes; MapGenPlace.PlanStep): the same pieces in the same order
    void PlanLawn()
    {
        planMax = 13;
        int room = pRoom;
        if (planPhase == 0)
        {
            planR = SR(1);
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (planIter == 0) { hq_StonePaths_k0 = 40; hq_StonePaths_rng = planR; PhSubReset(); }
            PhPush(); StonePaths_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { planR = hq_StonePaths_rng; PhBreak(); }
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (planIter == 0) { hq_FrontMarkers_k0 = 30; PhSubReset(); }
            PhPush(); FrontMarkers_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 3)
        {
            DoorSides(50);
            planN = TreeCount(room, ref planR);
            if (planN < 1 && AreaM2(room) >= 30) planN = 1;
        }
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (!PhSubActive())
            {
                int lt = planIter;
                if (!(lt < planN)) PhBreak();
                else { hq_PlanLawn_lt = lt; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanLawn_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 5)
        {
            hq_PlanLawn_sel1 = 0;
            if (planR.Chance(650)) hq_PlanLawn_sel1 = 1;
        }
        if (planPhase == 6 && PhLoop(6, 100000))
        {
            if (!(hq_PlanLawn_sel1 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_BenchAtEdge_k = 2; PhSubReset(); }
                PhPush(); BenchAtEdge_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
        if (planPhase == 7)
        {
            GenRng r3 = SR(3);
            hq_PlanLawn_sel2 = 0;
            if (r3.Chance(350) && Fit(1)) hq_PlanLawn_sel2 = 1;
            hq_PlanLawn_r3 = r3;
        }
        if (planPhase == 8)
        {
            if (hq_PlanLawn_sel2 == 1)
            {
                GenRng r3 = hq_PlanLawn_r3;
                int first = nEmit;
                hq_PlanLawn_sel3 = 0;
                if (WallPlace(Dn("n_logStack"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0) hq_PlanLawn_sel3 = 1;
                hq_PlanLawn_r3 = r3;
                hq_PlanLawn_first = first;
            }
        }
        if (planPhase == 9 && PhLoop(9, 100000))
        {
            if (!(hq_PlanLawn_sel2 == 1 && hq_PlanLawn_sel3 == 1)) PhBreak();
            else
            {
                int first = hq_PlanLawn_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanLawn_first = first;
            }
        }
        if (planPhase == 10)
        {
            // planted borders along the fences fill the rest of the budget: no lawn is left bare (G29)
            ClearBans();
        }
        if (planPhase == 11 && PhLoop(11, 100000))
        {
            if (planIter == 0) { hq_Borders_k0 = 10; hq_Borders_max = 6; hq_Borders_soil = true; PhSubReset(); }
            PhPush(); Borders_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 12)
        {
            GenRng r4 = SR(4);
            hq_PlanLawn_r4 = r4;
        }
        if (planPhase == 13 && PhLoop(13, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 3 && Fit(1))) PhBreak();
                else { hq_PlanLawn_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanLawn_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
    }
    void PlanLawn_L1()
    {
        int room = pRoom;
        planMax = 0;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int lt = hq_PlanLawn_lt;
            if (planIter == 0) { hq_LawnTree_k = 60 + lt; PhSubReset(); }
            PhPush(); LawnTree_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }

    void PlanLawn_L2()
    {
        int room = pRoom;
        planMax = 1;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            GenRng r4 = hq_PlanLawn_r4;
            bool ht1 = false;
            if (planIter == 0) { hq_Island_rng = r4; PhSubReset(); }
            PhPush(); Island_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r4 = hq_Island_rng; ht1 = hq_Island_ret; PhBreak(); }
            hq_PlanLawn_r4 = r4;
            hq_PlanLawn_ht1 = ht1;
        }
        if (planPhase == 1)
        {
            bool ht1 = hq_PlanLawn_ht1;
            if (!ht1)
            {
                { hq_PlanLawn_ht1 = ht1; phBreak = true; PhEnd(); return; }
            }
            hq_PlanLawn_ht1 = ht1;
        }
    }


    // ============================================================================================ GARDEN
    // Areas: soil beds along the fences with their planting; a vegetable plot, one crop to a row; the water point
    // (tap, watering can, buckets); a shed with the barrow and the sacks; tools leaning at the fence; a bench.
    void PlanGarden()
    {
        planMax = 35;
        int room = pRoom, cells = roomOuterCells[room];
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            hq_PlanGarden_r = r;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            GenRng r = hq_PlanGarden_r;
            if (planIter == 0) { hq_StonePaths_k0 = 40; hq_StonePaths_rng = r; PhSubReset(); }
            PhPush(); StonePaths_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_StonePaths_rng; PhBreak(); }
            hq_PlanGarden_r = r;
        }
        if (planPhase == 2)
        {
            GenRng r = hq_PlanGarden_r;
            // the shed in a corner of a larger garden, the barrow and the sacks beside it
            int shed = -1;
            hq_PlanGarden_sel1 = 0;
            if (cells >= 2 && r.Chance(650)) hq_PlanGarden_sel1 = 1;
            hq_PlanGarden_r = r;
            hq_PlanGarden_shed = shed;
        }
        if (planPhase == 3)
        {
            if (hq_PlanGarden_sel1 == 1)
            {
                GenRng r = hq_PlanGarden_r;
                int shed = hq_PlanGarden_shed;
                int first = nEmit;
                shed = WallPlace(Dn("gen_shed"), WM_CORNER, 0, 0, WIN_ANY, 0, ref r);
                hq_PlanGarden_r = r;
                hq_PlanGarden_shed = shed;
                hq_PlanGarden_first = first;
            }
        }
        if (planPhase == 4)
        {
            if (hq_PlanGarden_sel1 == 1)
            {
                int shed = hq_PlanGarden_shed;
                int first = hq_PlanGarden_first;
                if (shed >= 0 && !Keep(first, false))
                {
                    shed = -1;
                }
                hq_PlanGarden_sel2 = 0;
                if (shed >= 0) hq_PlanGarden_sel2 = 1;
                hq_PlanGarden_shed = shed;
                hq_PlanGarden_first = first;
            }
        }
        if (planPhase == 5)
        {
            if (hq_PlanGarden_sel1 == 1 && hq_PlanGarden_sel2 == 1)
            {
                GenRng r = hq_PlanGarden_r;
                hq_PlanGarden_sel3 = 0;
                if (r.Chance(700) && Fit(1)) hq_PlanGarden_sel3 = 1;
                hq_PlanGarden_r = r;
            }
        }
        if (planPhase == 6)
        {
            if (hq_PlanGarden_sel1 == 1 && hq_PlanGarden_sel2 == 1 && hq_PlanGarden_sel3 == 1)
            {
                GenRng r = hq_PlanGarden_r;
                int shed = hq_PlanGarden_shed;
                int first = hq_PlanGarden_first;
                first = nEmit;
                hq_PlanGarden_sel4 = 0;
                if (RelBeside(Dn("m_wheelbarrow"), shed, 0, 150, 0, ref r) >= 0) hq_PlanGarden_sel4 = 1;
                hq_PlanGarden_r = r;
                hq_PlanGarden_shed = shed;
                hq_PlanGarden_first = first;
            }
        }
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            if (!(hq_PlanGarden_sel1 == 1 && hq_PlanGarden_sel2 == 1 && hq_PlanGarden_sel3 == 1 && hq_PlanGarden_sel4 == 1)) PhBreak();
            else
            {
                int first = hq_PlanGarden_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanGarden_first = first;
            }
        }
        if (planPhase == 8)
        {
            if (hq_PlanGarden_sel1 == 1 && hq_PlanGarden_sel2 == 1)
            {
                GenRng r = hq_PlanGarden_r;
                hq_PlanGarden_sel5 = 0;
                if (r.Chance(550) && Fit(2)) hq_PlanGarden_sel5 = 1;
                hq_PlanGarden_r = r;
            }
        }
        if (planPhase == 9)
        {
            if (hq_PlanGarden_sel1 == 1 && hq_PlanGarden_sel2 == 1 && hq_PlanGarden_sel5 == 1)
            {
                GenRng r = hq_PlanGarden_r;
                int shed = hq_PlanGarden_shed;
                int first = hq_PlanGarden_first;
                first = nEmit;
                hq_PlanGarden_sel6 = 0;
                if (GroupBeside(GenData.FindGrp("sacks"), shed, r.Between(2, 3), 0, ref r)) hq_PlanGarden_sel6 = 1;
                hq_PlanGarden_r = r;
                hq_PlanGarden_shed = shed;
                hq_PlanGarden_first = first;
            }
        }
        if (planPhase == 10 && PhLoop(10, 100000))
        {
            if (!(hq_PlanGarden_sel1 == 1 && hq_PlanGarden_sel2 == 1 && hq_PlanGarden_sel5 == 1 && hq_PlanGarden_sel6 == 1)) PhBreak();
            else
            {
                int first = hq_PlanGarden_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanGarden_first = first;
            }
        }
        if (planPhase == 11)
        {
            // the water point: a tap at the fence, the can and the buckets beside it
            GenRng r2 = SR(2);
            int tap = -1;
            hq_PlanGarden_r2 = r2;
            hq_PlanGarden_tap = tap;
        }
        if (planPhase == 12)
        {
            int shed = hq_PlanGarden_shed;
            GenRng r2 = hq_PlanGarden_r2;
            int tap = hq_PlanGarden_tap;
            int first = nEmit;
            tap = WallPlace(Dn("gen_tap"), shed >= 0 ? WM_NEAR : WM_ANY, shed >= 0 ? emitX[shed] : 0, shed >= 0 ? emitZ[shed] : 0, WIN_ANY, 0, ref r2);
            hq_PlanGarden_shed = shed;
            hq_PlanGarden_r2 = r2;
            hq_PlanGarden_tap = tap;
            hq_PlanGarden_first = first;
        }
        if (planPhase == 13)
        {
            int tap = hq_PlanGarden_tap;
            int first = hq_PlanGarden_first;
            if (tap >= 0 && !Keep(first, false))
            {
                tap = -1;
            }
            hq_PlanGarden_sel7 = 0;
            if (tap >= 0 && Fit(2)) hq_PlanGarden_sel7 = 1;
            hq_PlanGarden_tap = tap;
            hq_PlanGarden_first = first;
        }
        if (planPhase == 14)
        {
            if (hq_PlanGarden_sel7 == 1)
            {
                GenRng r2 = hq_PlanGarden_r2;
                int tap = hq_PlanGarden_tap;
                int first = hq_PlanGarden_first;
                first = nEmit;
                hq_PlanGarden_sel8 = 0;
                if (GroupBeside(GenData.FindGrp("watering"), tap, r2.Between(2, 3), 0, ref r2)) hq_PlanGarden_sel8 = 1;
                hq_PlanGarden_r2 = r2;
                hq_PlanGarden_tap = tap;
                hq_PlanGarden_first = first;
            }
        }
        if (planPhase == 15 && PhLoop(15, 100000))
        {
            if (!(hq_PlanGarden_sel7 == 1 && hq_PlanGarden_sel8 == 1)) PhBreak();
            else
            {
                int first = hq_PlanGarden_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanGarden_first = first;
            }
        }
        if (planPhase == 16)
        {
            // the vegetable plot: a bed of soil in the open, two rows, one crop to a row
            GenRng r3 = SR(3);
            // revision 30 (Cor: gardens can just have veggies): a vegetable garden always has its plot, on top of
            // the budget if the shed and the water point used it
            hq_PlanGarden_sel9 = 1;
            if (!Fit(6)) roomFloorTarget[room] = roomFloor[room] + 6;
            hq_PlanGarden_r3 = r3;
        }
        if (planPhase == 17 && PhLoop(17, 100000))
        {
            if (!(hq_PlanGarden_sel9 == 1)) PhBreak();
            else
            {
                GenRng r3 = hq_PlanGarden_r3;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 4; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r3; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r3 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PlanGarden_r3 = r3;
                hq_PlanGarden_cell = cell;
            }
        }
        if (planPhase == 18)
        {
            if (hq_PlanGarden_sel9 == 1)
            {
                int cell = hq_PlanGarden_cell;
                hq_PlanGarden_sel10 = 0;
                if (cell >= 0) hq_PlanGarden_sel10 = 1;
                hq_PlanGarden_cell = cell;
            }
        }
        if (planPhase == 19 && PhLoop(19, 100000))
        {
            if (!(hq_PlanGarden_sel9 == 1 && hq_PlanGarden_sel10 == 1)) PhBreak();
            else
            {
                GenRng r3 = hq_PlanGarden_r3;
                int cell = hq_PlanGarden_cell;
                if (planIter == 0) { hq_VegPlot_cell = cell; hq_VegPlot_rng = r3; PhSubReset(); }
                PhPush(); VegPlot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r3 = hq_VegPlot_rng; PhBreak(); }
                hq_PlanGarden_r3 = r3;
                hq_PlanGarden_cell = cell;
            }
        }
        if (planPhase == 20)
        {
            // tools leaning at the fence near the water point
            GenRng r4 = SR(4);
            hq_PlanGarden_sel11 = 0;
            if (r4.Chance(750) && Fit(2)) hq_PlanGarden_sel11 = 1;
            hq_PlanGarden_r4 = r4;
        }
        if (planPhase == 21)
        {
            if (hq_PlanGarden_sel11 == 1)
            {
                int shed = hq_PlanGarden_shed;
                GenRng r4 = hq_PlanGarden_r4;
                int first = nEmit;
                bool ok = shed >= 0 && GroupBeside(GenData.FindGrp("tools"), shed, r4.Between(2, 3), 0, ref r4);
                hq_PlanGarden_shed = shed;
                hq_PlanGarden_r4 = r4;
                hq_PlanGarden_first = first;
                hq_PlanGarden_ok = ok;
            }
        }
        if (planPhase == 22)
        {
            if (hq_PlanGarden_sel11 == 1)
            {
                int tap = hq_PlanGarden_tap;
                GenRng r4 = hq_PlanGarden_r4;
                bool ok = hq_PlanGarden_ok;
                if (!ok) ok = GroupAt(GenData.FindGrp("tools"), GenData.A_OUT_EDGE, r4.Between(2, 3), tap >= 0 ? WM_NEAR : WM_ANY, tap >= 0 ? emitX[tap] : 0, tap >= 0 ? emitZ[tap] : 0, 0, ref r4);
                hq_PlanGarden_sel12 = 0;
                if (ok) hq_PlanGarden_sel12 = 1;
                hq_PlanGarden_tap = tap;
                hq_PlanGarden_r4 = r4;
                hq_PlanGarden_ok = ok;
            }
        }
        if (planPhase == 23 && PhLoop(23, 100000))
        {
            if (!(hq_PlanGarden_sel11 == 1 && hq_PlanGarden_sel12 == 1)) PhBreak();
            else
            {
                int first = hq_PlanGarden_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanGarden_first = first;
            }
        }
        if (planPhase == 24)
        {
            DoorSides(50);
        }
        if (planPhase == 25)
        {
            GenRng r5 = SR(5);
            int trees = TreeCount(room, ref r5);
            trees = 0;   // revision 30 (Cor: gardens can just have veggies): no tree over the beds
            hq_PlanGarden_r5 = r5;
            hq_PlanGarden_trees = trees;
        }
        if (planPhase == 26 && PhLoop(26, 100000))
        {
            int trees = hq_PlanGarden_trees;
            if (!PhSubActive())
            {
                int t = planIter;
                if (!(t < trees)) PhBreak();
                else { hq_PlanGarden_t = t; PhSubBegin(); }
            }
            hq_PlanGarden_trees = trees;
            if (PhSubActive())
            {
                PhPush(); PlanGarden_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 27)
        {
            GenRng r5 = hq_PlanGarden_r5;
            hq_PlanGarden_sel13 = 0;
            if (r5.Chance(450)) hq_PlanGarden_sel13 = 1;
            hq_PlanGarden_r5 = r5;
        }
        if (planPhase == 28 && PhLoop(28, 100000))
        {
            if (!(hq_PlanGarden_sel13 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_BenchAtEdge_k = 6; PhSubReset(); }
                PhPush(); BenchAtEdge_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
        if (planPhase == 29)
        {
            GenRng r7 = SR(7);
            hq_PlanGarden_sel14 = 0;
            if (r7.Chance(500) && Fit(2)) hq_PlanGarden_sel14 = 1;
            hq_PlanGarden_r7 = r7;
        }
        if (planPhase == 30)
        {
            if (hq_PlanGarden_sel14 == 1)
            {
                GenRng r7 = hq_PlanGarden_r7;
                int first = nEmit;
                hq_PlanGarden_sel15 = 0;
                if (GroupAt(GenData.FindGrp("pots"), GenData.A_OUT_EDGE, r7.Between(2, 3), WM_ANY, 0, 0, 0, ref r7)) hq_PlanGarden_sel15 = 1;
                hq_PlanGarden_r7 = r7;
                hq_PlanGarden_first = first;
            }
        }
        if (planPhase == 31 && PhLoop(31, 100000))
        {
            if (!(hq_PlanGarden_sel14 == 1 && hq_PlanGarden_sel15 == 1)) PhBreak();
            else
            {
                int first = hq_PlanGarden_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanGarden_first = first;
            }
        }
        if (planPhase == 32)
        {
            // the beds along the fences (revision 30: crop rows, on top of the budget the rest used)
            ClearBans();
            if (roomFloorTarget[room] < roomFloor[room] + 9) roomFloorTarget[room] = roomFloor[room] + 9;
        }
        if (planPhase == 33 && PhLoop(33, 100000))
        {
            if (planIter == 0) { hq_Borders_k0 = 10; hq_Borders_max = 8; hq_Borders_soil = true; PhSubReset(); }
            PhPush(); Borders_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 34)
        {
            GenRng r8 = SR(8);
            hq_PlanGarden_r8 = r8;
        }
        if (planPhase == 35 && PhLoop(35, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 3 && Fit(1))) PhBreak();
                else { hq_PlanGarden_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanGarden_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
    }
    void PlanGarden_L1()
    {
        int room = pRoom, cells = roomOuterCells[room];
        planMax = 0;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int t = hq_PlanGarden_t;
            if (planIter == 0) { hq_LawnTree_k = 60 + t; PhSubReset(); }
            PhPush(); LawnTree_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }

    void PlanGarden_L2()
    {
        int room = pRoom, cells = roomOuterCells[room];
        planMax = 1;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            GenRng r8 = hq_PlanGarden_r8;
            bool ht1 = false;
            if (planIter == 0) { hq_Island_rng = r8; PhSubReset(); }
            PhPush(); Island_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r8 = hq_Island_rng; ht1 = hq_Island_ret; PhBreak(); }
            hq_PlanGarden_r8 = r8;
            hq_PlanGarden_ht1 = ht1;
        }
        if (planPhase == 1)
        {
            bool ht1 = hq_PlanGarden_ht1;
            if (!ht1)
            {
                { hq_PlanGarden_ht1 = ht1; phBreak = true; PhEnd(); return; }
            }
            hq_PlanGarden_ht1 = ht1;
        }
    }


    // a vegetable plot around a cell: a bed of soil 3 x 2 m with two rows of crops along it
    void VegPlot(int cell, ref GenRng rng)
    {
        hq_VegPlot_cell = cell;
        hq_VegPlot_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); VegPlot_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_VegPlot_rng;
    }

    // VegPlot in phases, a step a call (autophase4); VegPlot() above runs them to the end
    void VegPlot_Body()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int cell = hq_VegPlot_cell;
            GenRng rng = hq_VegPlot_rng;
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
            hq_VegPlot_cell = cell;
            hq_VegPlot_rng = rng;
            hq_VegPlot_room = room;
            hq_VegPlot_alongX = alongX;
            hq_VegPlot_crops = crops;
            hq_VegPlot_c0 = c0;
            hq_VegPlot_mx = mx;
            hq_VegPlot_mz = mz;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!PhSubActive())
            {
                int row = planIter;
                if (!(row < 2 && Fit(3))) PhBreak();
                else { hq_VegPlot_row = row; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); VegPlot_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
    }
    void VegPlot_L1()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            GenRng rng = hq_VegPlot_rng;
            int room = hq_VegPlot_room;
            bool alongX = hq_VegPlot_alongX;
            string[] crops = hq_VegPlot_crops;
            int c0 = hq_VegPlot_c0;
            int mx = hq_VegPlot_mx;
            int mz = hq_VegPlot_mz;
            int row = hq_VegPlot_row;
            int g = GenData.FindGrp(crops[(c0 + row) % 3]);
            if (ChooseMembers(g, room, rng.Between(3, 4), ref rng) < 3) { hq_VegPlot_rng = rng; hq_VegPlot_room = room; hq_VegPlot_alongX = alongX; hq_VegPlot_crops = crops; hq_VegPlot_c0 = c0; hq_VegPlot_mx = mx; hq_VegPlot_mz = mz; PhEnd(); return; }
            BuildFormation(GenData.GL_ROW, GenData.grpGapMm[g], ref rng);
            int first = nEmit;
            int across = (row == 0 ? -1 : 1) * 450 - fDep / 2;
            int rot = alongX ? 0 : 1;
            fHost = H_NATURE;
            int e = StampFormation(room, rot, (alongX ? mx : mz) - fLen / 2, (alongX ? mz : mx) + across, -1, 0, g, -1);
            fHost = H_NONE;
            hq_VegPlot_sel1 = 0;
            if (e >= 0) hq_VegPlot_sel1 = 1;
            hq_VegPlot_rng = rng;
            hq_VegPlot_room = room;
            hq_VegPlot_alongX = alongX;
            hq_VegPlot_crops = crops;
            hq_VegPlot_c0 = c0;
            hq_VegPlot_mx = mx;
            hq_VegPlot_mz = mz;
            hq_VegPlot_first = first;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!(hq_VegPlot_sel1 == 1)) PhBreak();
            else
            {
                int first = hq_VegPlot_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_VegPlot_first = first;
            }
        }
    }


    // ============================================================================================ PATIO
    // Areas: the paved terrace at the door with the table set on it; loungers at its edge; planters; the rest
    // is lawn with its borders.
    void PlanPatio()
    {
        planMax = 24;
        if (planPhase == 0)
        {
            int room = pRoom;
            hq_PlanPatio_room = room;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (planIter == 0) { hq_FrontMarkers_k0 = 30; PhSubReset(); }
            PhPush(); FrontMarkers_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 2)
        {
            int room = hq_PlanPatio_room;
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
            hq_PlanPatio_room = room;
            hq_PlanPatio_r = r;
            hq_PlanPatio_midX = midX;
            hq_PlanPatio_midZ = midZ;
        }
        if (planPhase == 3)
        {
            int room = hq_PlanPatio_room;
            for (int q = 1; q < roomDoorCount[room]; q++) { GenRng rq = SR(40 + q); StonePath(room, q, 7 + rq.Range(6), ref rq); }
            hq_PlanPatio_room = room;
        }
        if (planPhase == 4)
        {
            GenRng r = hq_PlanPatio_r;
            // (b) the table set on the paving
            int setDef = Pick(ref r, "k_table", 5, "r_tableRound", 5);
            hq_PlanPatio_r = r;
            hq_PlanPatio_setDef = setDef;
        }
        if (planPhase == 5 && PhLoop(5, 100000))
        {
            GenRng r = hq_PlanPatio_r;
            int midX = hq_PlanPatio_midX;
            int midZ = hq_PlanPatio_midZ;
            int setDef = hq_PlanPatio_setDef;
            int set = 0;
            if (planIter == 0) { hq_CentrePiece_def = setDef; hq_CentrePiece_yaw = 0; hq_CentrePiece_bothYaws = true; hq_CentrePiece_ringWant = 2; hq_CentrePiece_ringMin = 1; hq_CentrePiece_mode = WM_NEAR; hq_CentrePiece_tx = midX; hq_CentrePiece_tz = midZ; hq_CentrePiece_need = GenData.T_COVERED; hq_CentrePiece_flags = EF_MANDATORY; hq_CentrePiece_rng = r; PhSubReset(); }
            PhPush(); CentrePiece_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_CentrePiece_rng; set = hq_CentrePiece_ret; PhBreak(); }
            hq_PlanPatio_r = r;
            hq_PlanPatio_midX = midX;
            hq_PlanPatio_midZ = midZ;
            hq_PlanPatio_setDef = setDef;
            hq_PlanPatio_set = set;
        }
        if (planPhase == 6)
        {
            int set = hq_PlanPatio_set;
            hq_PlanPatio_sel1 = 0;
            if (set < 0) hq_PlanPatio_sel1 = 1;
            hq_PlanPatio_set = set;
        }
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            if (!(hq_PlanPatio_sel1 == 1)) PhBreak();
            else
            {
                GenRng r = hq_PlanPatio_r;
                int midX = hq_PlanPatio_midX;
                int midZ = hq_PlanPatio_midZ;
                int set = hq_PlanPatio_set;
                if (planIter == 0) { hq_CentrePiece_def = Dn("r_tableRound"); hq_CentrePiece_yaw = 0; hq_CentrePiece_bothYaws = false; hq_CentrePiece_ringWant = 1; hq_CentrePiece_ringMin = 1; hq_CentrePiece_mode = WM_NEAR; hq_CentrePiece_tx = midX; hq_CentrePiece_tz = midZ; hq_CentrePiece_need = 0; hq_CentrePiece_flags = EF_MANDATORY; hq_CentrePiece_rng = r; PhSubReset(); }
                PhPush(); CentrePiece_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r = hq_CentrePiece_rng; set = hq_CentrePiece_ret; PhBreak(); }
                hq_PlanPatio_r = r;
                hq_PlanPatio_midX = midX;
                hq_PlanPatio_midZ = midZ;
                hq_PlanPatio_set = set;
            }
        }
        if (planPhase == 8)
        {
            GenRng r = hq_PlanPatio_r;
            int midX = hq_PlanPatio_midX;
            int midZ = hq_PlanPatio_midZ;
            int set = hq_PlanPatio_set;
            if (set < 0)
            {
                // a paving too narrow beside the door's walkway for a table set: a bench against the wall by the door
                int first = nEmit;
                if (WallPlace(Pick(ref r, "k_bench", 6, "k_benchCushion", 4), WM_NEAR, midX, midZ, WIN_ANY, EF_MANDATORY, ref r) < 0 || !Keep(first, true)) MandFail(1);
            }
            // (c) loungers side by side on the paving, planters at the door, a bench against the wall
            GenRng r2 = SR(2);
            hq_PlanPatio_sel2 = 0;
            if (r2.Chance(600) && Fit(2)) hq_PlanPatio_sel2 = 1;
            hq_PlanPatio_r = r;
            hq_PlanPatio_midX = midX;
            hq_PlanPatio_midZ = midZ;
            hq_PlanPatio_set = set;
            hq_PlanPatio_r2 = r2;
        }
        if (planPhase == 9)
        {
            if (hq_PlanPatio_sel2 == 1)
            {
                int midX = hq_PlanPatio_midX;
                int midZ = hq_PlanPatio_midZ;
                int set = hq_PlanPatio_set;
                GenRng r2 = hq_PlanPatio_r2;
                int first = nEmit;
                fHost = H_FREE;
                bool ok = GroupAt(GenData.FindGrp("loungers"), GenData.A_COVERED, 2, WM_FAR, set >= 0 ? emitX[set] : midX, set >= 0 ? emitZ[set] : midZ, 0, ref r2);
                fHost = H_NONE;
                hq_PlanPatio_sel3 = 0;
                if (ok) hq_PlanPatio_sel3 = 1;
                hq_PlanPatio_midX = midX;
                hq_PlanPatio_midZ = midZ;
                hq_PlanPatio_set = set;
                hq_PlanPatio_r2 = r2;
                hq_PlanPatio_first = first;
            }
        }
        if (planPhase == 10 && PhLoop(10, 100000))
        {
            if (!(hq_PlanPatio_sel2 == 1 && hq_PlanPatio_sel3 == 1)) PhBreak();
            else
            {
                int first = hq_PlanPatio_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanPatio_first = first;
            }
        }
        if (planPhase == 11)
        {
            GenRng r2 = hq_PlanPatio_r2;
            DoorSides(50);
            hq_PlanPatio_sel4 = 0;
            if (r2.Chance(550)) hq_PlanPatio_sel4 = 1;
            hq_PlanPatio_r2 = r2;
        }
        if (planPhase == 12 && PhLoop(12, 100000))
        {
            if (!(hq_PlanPatio_sel4 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_BenchAtEdge_k = 3; PhSubReset(); }
                PhPush(); BenchAtEdge_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
        if (planPhase == 13)
        {
            GenRng r4 = SR(4);
            hq_PlanPatio_sel5 = 0;
            if (r4.Chance(500) && Fit(2)) hq_PlanPatio_sel5 = 1;
            hq_PlanPatio_r4 = r4;
        }
        if (planPhase == 14)
        {
            if (hq_PlanPatio_sel5 == 1)
            {
                int midX = hq_PlanPatio_midX;
                int midZ = hq_PlanPatio_midZ;
                GenRng r4 = hq_PlanPatio_r4;
                int first = nEmit;
                hq_PlanPatio_sel6 = 0;
                if (GroupAt(GenData.FindGrp("pots"), GenData.A_OUT_EDGE, r4.Between(2, 3), WM_NEAR, midX, midZ, 0, ref r4)) hq_PlanPatio_sel6 = 1;
                hq_PlanPatio_midX = midX;
                hq_PlanPatio_midZ = midZ;
                hq_PlanPatio_r4 = r4;
                hq_PlanPatio_first = first;
            }
        }
        if (planPhase == 15 && PhLoop(15, 100000))
        {
            if (!(hq_PlanPatio_sel5 == 1 && hq_PlanPatio_sel6 == 1)) PhBreak();
            else
            {
                int first = hq_PlanPatio_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanPatio_first = first;
            }
        }
        if (planPhase == 16)
        {
            GenRng r5 = SR(5);
            hq_PlanPatio_sel7 = 0;
            if (r5.Chance(300) && Fit(1)) hq_PlanPatio_sel7 = 1;
            hq_PlanPatio_r5 = r5;
        }
        if (planPhase == 17)
        {
            if (hq_PlanPatio_sel7 == 1)
            {
                GenRng r5 = hq_PlanPatio_r5;
                int first = nEmit;
                FSet(Dn("s_barrel"));
                hq_PlanPatio_sel8 = 0;
                if (FormAt(GenData.GL_ROW, 0, GenData.A_BUILDING, WM_ANY, 0, 0, 0, ref r5) >= 0) hq_PlanPatio_sel8 = 1;
                hq_PlanPatio_r5 = r5;
                hq_PlanPatio_first = first;
            }
        }
        if (planPhase == 18 && PhLoop(18, 100000))
        {
            if (!(hq_PlanPatio_sel7 == 1 && hq_PlanPatio_sel8 == 1)) PhBreak();
            else
            {
                int first = hq_PlanPatio_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanPatio_first = first;
            }
        }
        if (planPhase == 19)
        {
            int room = hq_PlanPatio_room;
            GenRng r5 = hq_PlanPatio_r5;
            int trees = TreeCount(room, ref r5);
            hq_PlanPatio_room = room;
            hq_PlanPatio_r5 = r5;
            hq_PlanPatio_trees = trees;
        }
        if (planPhase == 20 && PhLoop(20, 100000))
        {
            int trees = hq_PlanPatio_trees;
            if (!PhSubActive())
            {
                int t = planIter;
                if (!(t < trees)) PhBreak();
                else { hq_PlanPatio_t = t; PhSubBegin(); }
            }
            hq_PlanPatio_trees = trees;
            if (PhSubActive())
            {
                PhPush(); PlanPatio_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 21)
        {
            ClearBans();
        }
        if (planPhase == 22 && PhLoop(22, 100000))
        {
            if (planIter == 0) { hq_Borders_k0 = 10; hq_Borders_max = 6; hq_Borders_soil = true; PhSubReset(); }
            PhPush(); Borders_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 23)
        {
            GenRng r6 = SR(6);
            hq_PlanPatio_r6 = r6;
        }
        if (planPhase == 24 && PhLoop(24, 100000))
        {
            if (!PhSubActive())
            {
                int j = planIter;
                if (!(j < 2 && Fit(1))) PhBreak();
                else { hq_PlanPatio_j = j; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanPatio_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
    }
    void PlanPatio_L1()
    {
        planMax = 0;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int t = hq_PlanPatio_t;
            if (planIter == 0) { hq_LawnTree_k = 60 + t; PhSubReset(); }
            PhPush(); LawnTree_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }

    void PlanPatio_L2()
    {
        planMax = 1;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            GenRng r6 = hq_PlanPatio_r6;
            bool ht1 = false;
            if (planIter == 0) { hq_Island_rng = r6; PhSubReset(); }
            PhPush(); Island_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r6 = hq_Island_rng; ht1 = hq_Island_ret; PhBreak(); }
            hq_PlanPatio_r6 = r6;
            hq_PlanPatio_ht1 = ht1;
        }
        if (planPhase == 1)
        {
            bool ht1 = hq_PlanPatio_ht1;
            if (!ht1)
            {
                { hq_PlanPatio_ht1 = ht1; phBreak = true; PhEnd(); return; }
            }
            hq_PlanPatio_ht1 = ht1;
        }
    }


    // Markers at a plot's front (G42: a sign stands where it has a reason to, facing the people it is for). Seen
    // from the lane: a house's mailbox beside its garden gate (or beside its front door when the door opens onto the
    // lane), against the fence or the wall, facing the lane; a shop's name board beside its customer door when that
    // door opens onto the lane. Seen from a shop's forecourt: the name board beside the customer door against the
    // front wall, facing the forecourt and the lane. Nothing else places signs or mailboxes; they are never
    // wrong-category spawns (GenData.NoLeak).
    void FrontMarkers(int k0)
    {
        hq_FrontMarkers_k0 = k0;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); FrontMarkers_Body(); fin = PhAdvance(); PhPop(); }
    }

    // FrontMarkers in phases, a step a call (autophase4); FrontMarkers() above runs them to the end
    void FrontMarkers_Body()
    {
        planMax = 3;
        if (planPhase == 0)
        {
            int room = pRoom, type = roomType[room];
            bool lane = type == GenData.R_LANE;
            if (!lane && roomPlace[room] != PL_FRONT) { hq_FrontMarkers_room = room; hq_FrontMarkers_lane = lane; PhEnd(); return; }
            int n = 0;
            hq_FrontMarkers_room = room;
            hq_FrontMarkers_lane = lane;
            hq_FrontMarkers_n = n;
        }
        // doorways of this room into a building: a shop's customer door, or (from the lane) a house's front door
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int room = hq_FrontMarkers_room;
            if (!PhSubActive())
            {
                int q = planIter;
                if (!(q < roomDoorCount[room])) PhBreak();
                else { hq_FrontMarkers_q = q; PhSubBegin(); }
            }
            hq_FrontMarkers_room = room;
            if (PhSubActive())
            {
                PhPush(); FrontMarkers_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 2)
        {
            bool lane = hq_FrontMarkers_lane;
            if (!lane) { hq_FrontMarkers_lane = lane; PhEnd(); return; }
            hq_FrontMarkers_lane = lane;
        }
        // a house's garden opens onto the lane through a gap in the fence: the mailbox beside it
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (!PhSubActive())
            {
                int c = planIter;
                if (!(c < W * H)) PhBreak();
                else { hq_FrontMarkers_c = c; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); FrontMarkers_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
    }
    void FrontMarkers_L1()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int room = hq_FrontMarkers_room;
            bool lane = hq_FrontMarkers_lane;
            int q = hq_FrontMarkers_q;
            int i = roomDoorStart[room] + q, cell = roomDoors[i], d = roomDoorDir[i];
            int ox = cell % IW + DX[d], oz = cell / IW + DZ[d];
            if (ox < 0 || oz < 0 || ox >= IW || oz >= IH || !IsFrontDoor(oz * IW + ox, (d + 2) & 3)) { hq_FrontMarkers_room = room; hq_FrontMarkers_lane = lane; hq_FrontMarkers_cell = cell; PhEnd(); return; }
            int zone = roomZone[innerRoom[oz * IW + ox]];
            if (zone == GenData.Z_HOUSE && !lane) { hq_FrontMarkers_room = room; hq_FrontMarkers_lane = lane; hq_FrontMarkers_cell = cell; hq_FrontMarkers_zone = zone; PhEnd(); return; }
            hq_FrontMarkers_room = room;
            hq_FrontMarkers_lane = lane;
            hq_FrontMarkers_cell = cell;
            hq_FrontMarkers_zone = zone;
        }
        if (planPhase == 1)
        {
            int k0 = hq_FrontMarkers_k0;
            int n = hq_FrontMarkers_n;
            int cell = hq_FrontMarkers_cell;
            int zone = hq_FrontMarkers_zone;
            Marker(zone == GenData.Z_HOUSE ? "gen_mailbox" : "n_sign", cell % IW * GenData.INNER_MM + 250, cell / IW * GenData.INNER_MM + 250, k0 + n++);
            hq_FrontMarkers_k0 = k0;
            hq_FrontMarkers_n = n;
            hq_FrontMarkers_cell = cell;
            hq_FrontMarkers_zone = zone;
        }
    }

    void FrontMarkers_L2()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int room = hq_FrontMarkers_room;
            int c = hq_FrontMarkers_c;
            if (cellRoom[c] != room) { hq_FrontMarkers_room = room; PhEnd(); return; }
            int cx = c % W, cz = c / W;
            hq_FrontMarkers_room = room;
            hq_FrontMarkers_cx = cx;
            hq_FrontMarkers_cz = cz;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int k0 = hq_FrontMarkers_k0;
            int n = hq_FrontMarkers_n;
            int cx = hq_FrontMarkers_cx;
            int cz = hq_FrontMarkers_cz;
            int d = planIter;
            if (!(d < 4)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
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
            hq_FrontMarkers_k0 = k0;
            hq_FrontMarkers_n = n;
            hq_FrontMarkers_cx = cx;
            hq_FrontMarkers_cz = cz;
        }
    }


    // a marker by the wall or fence nearest (px, pz), its back to it and its face to the people coming (the models'
    // readable face is +z: convert_models.py turns the sign board, whose face was its back), SignGap clear of it
    const int SignGap = 150;
    void Marker(string id, int px, int pz, int k)
    {
        if (!Fit(1)) return;
        GenRng r = SR(k);
        int first = nEmit;
        if (WallPlace(Dn(id), SignGap, WM_NEAR, px, pz, WIN_ANY, 0, ref r) >= 0) Keep(first, false);
    }

    // ============================================================================================ LANE
    // The way in from the hunter release past the plots' gates to the wood (G32): a track kept open down its
    // middle. What stands on it stands against the fences: the sign at its start, a bench facing across, a bin by
    // a shop's front, planting between the gates.
    void PlanLane()
    {
        planMax = 11;
        int room = pRoom;
        if (planPhase == 0)
        {
            int rx = (releaseX * 8 + 4) * GenData.INNER_MM, rz = (releaseZ * 8 + 4) * GenData.INNER_MM;
            GenRng r = SR(1);
            // the sign by the release, in the lane's first room only (layout 14: the lane is in rooms of six cells)
            int fx = releaseX + DX[releaseDir], fz = releaseZ + DZ[releaseDir];
            bool firstRoom = InGrid(fx, fz) && cellRoom[fz * W + fx] == room;
            hq_PlanLane_sel1 = 0;
            if (firstRoom && Fit(1)) hq_PlanLane_sel1 = 1;
            LaneVergePath(room);
            hq_PlanLane_rx = rx;
            hq_PlanLane_rz = rz;
            hq_PlanLane_r = r;
        }
        if (planPhase == 1)
        {
            if (hq_PlanLane_sel1 == 1)
            {
                int rx = hq_PlanLane_rx;
                int rz = hq_PlanLane_rz;
                GenRng r = hq_PlanLane_r;
                int first = nEmit;
                hq_PlanLane_sel2 = 0;
                if (WallPlace(Dn("n_sign"), SignGap, WM_NEAR, rx, rz, WIN_ANY, 0, ref r) >= 0) hq_PlanLane_sel2 = 1;
                hq_PlanLane_rx = rx;
                hq_PlanLane_rz = rz;
                hq_PlanLane_r = r;
                hq_PlanLane_first = first;
            }
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (!(hq_PlanLane_sel1 == 1 && hq_PlanLane_sel2 == 1)) PhBreak();
            else
            {
                int first = hq_PlanLane_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanLane_first = first;
            }
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (planIter == 0) { hq_FrontMarkers_k0 = 30; PhSubReset(); }
            PhPush(); FrontMarkers_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (planIter == 0) { hq_BenchAtEdge_k = 2; PhSubReset(); }
            PhPush(); BenchAtEdge_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 5)
        {
            hq_PlanLane_sel3 = 0;
            if (AreaM2(room) >= 100) hq_PlanLane_sel3 = 1;
        }
        if (planPhase == 6 && PhLoop(6, 100000))
        {
            if (!(hq_PlanLane_sel3 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_BenchAtEdge_k = 3; PhSubReset(); }
                PhPush(); BenchAtEdge_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
        if (planPhase == 7)
        {
            GenRng r4 = SR(4);
            hq_PlanLane_sel4 = 0;
            if (r4.Chance(600) && Fit(1)) hq_PlanLane_sel4 = 1;
            hq_PlanLane_r4 = r4;
        }
        if (planPhase == 8)
        {
            if (hq_PlanLane_sel4 == 1)
            {
                GenRng r4 = hq_PlanLane_r4;
                int first = nEmit;
                hq_PlanLane_sel5 = 0;
                if (WallPlace(Dn("k_trashcan"), WM_ANY, 0, 0, WIN_ANY, 0, ref r4) >= 0) hq_PlanLane_sel5 = 1;
                hq_PlanLane_r4 = r4;
                hq_PlanLane_first = first;
            }
        }
        if (planPhase == 9 && PhLoop(9, 100000))
        {
            if (!(hq_PlanLane_sel4 == 1 && hq_PlanLane_sel5 == 1)) PhBreak();
            else
            {
                int first = hq_PlanLane_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanLane_first = first;
            }
        }
        if (planPhase == 10)
        {
            ClearBans();
        }
        if (planPhase == 11 && PhLoop(11, 100000))
        {
            // revision 29: the verge along both sides of the lane (Cor: the lane was wide): more of it than before
            if (planIter == 0) { hq_Borders_k0 = 10; hq_Borders_max = 6 + AreaM2(room) / 12; hq_Borders_soil = false; PhSubReset(); }
            PhPush(); Borders_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }

    // Revision 29 (Cor: the 4 m lane was wide): a clear path 2 m wide down the lane's middle is walkway, so nothing
    // stands there and the verge the borders plant stays at the sides. A cell where the lane turns or meets
    // another keeps its middle square clear both ways.
    void LaneVergePath(int room)
    {
        for (int c = 0; c < W * H; c++)
        {
            if (cellRoom[c] != room || cellZone[c] == GenData.Z_NONE) continue;
            int cx = c % W, cz = c / W;
            bool alongZ = false, alongX = false;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + DX[d], nz = cz + DZ[d];
                if (!InGrid(nx, nz)) continue;
                int nr = cellRoom[nz * W + nx];
                if (nr < 0 || roomType[nr] != GenData.R_LANE && roomType[nr] != GenData.R_RELEASE) continue;
                if (d == 0 || d == 2) alongZ = true; else alongX = true;
            }
            if (!alongZ && !alongX) { alongZ = true; alongX = true; }
            for (int lz = 0; lz < 8; lz++)
                for (int lx = 0; lx < 8; lx++)
                {
                    bool mid = (alongZ && lx >= 2 && lx < 6) || (alongX && lz >= 2 && lz < 6);
                    if (!mid) continue;
                    int i = (cz * 8 + lz) * IW + cx * 8 + lx;
                    if (innerRoom[i] == room && occ[i] == 0) tags[i] |= GenData.T_PATH;
                }
        }
    }

    // ============================================================================================ YARD
    // A warehouse's loading yard or a diner's service yard (G32): paved. The goods wait against the building and
    // the fence near the loading or service door: pallets, then rows of crates, boxes, barrels, sacks, fuel; the
    // hand truck and the dumpster (or a bin) by the door. The way from the door to the gates stays open.
    void PlanYard()
    {
        planMax = 16;
        if (planPhase == 0)
        {
            int room = pRoom, dx = 0, dz = 0;
            hq_PlanYard_room = room;
            hq_PlanYard_dx = dx;
            hq_PlanYard_dz = dz;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (planIter == 0) { hq_FrontMarkers_k0 = 30; PhSubReset(); }
            PhPush(); FrontMarkers_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 2)
        {
            int room = hq_PlanYard_room;
            int dx = hq_PlanYard_dx;
            int dz = hq_PlanYard_dz;
            MainDoor(room, out dx, out dz);
            bool diner = false;
            for (int k = 0; k < roomDoorCount[room]; k++) if (DoorZone(room, k) == GenData.Z_DINER) diner = true;
            GenRng r = SR(1);
            hq_PlanYard_sel1 = 0;
            if (!diner && Fit(2)) hq_PlanYard_sel1 = 1;
            hq_PlanYard_room = room;
            hq_PlanYard_dx = dx;
            hq_PlanYard_dz = dz;
            hq_PlanYard_diner = diner;
            hq_PlanYard_r = r;
        }
        if (planPhase == 3)
        {
            if (hq_PlanYard_sel1 == 1)
            {
                int dx = hq_PlanYard_dx;
                int dz = hq_PlanYard_dz;
                GenRng r = hq_PlanYard_r;
                // pallets side by side against the building beside the dock door
                int first = nEmit;
                FSet(Dn("w_pallet"), Dn(r.Chance(500) ? "w_palletLoaded" : "w_pallet"));
                hq_PlanYard_sel2 = 0;
                if (FormAt(GenData.GL_ROW, 100, GenData.A_BUILDING, WM_NEAR, dx, dz, 0, ref r) >= 0) hq_PlanYard_sel2 = 1;
                hq_PlanYard_dx = dx;
                hq_PlanYard_dz = dz;
                hq_PlanYard_r = r;
                hq_PlanYard_first = first;
            }
        }
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (!(hq_PlanYard_sel1 == 1 && hq_PlanYard_sel2 == 1)) PhBreak();
            else
            {
                int first = hq_PlanYard_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanYard_first = first;
            }
        }
        if (planPhase == 5)
        {
            bool diner = hq_PlanYard_diner;
            string[] rows = diner ? new string[] { "crates", "barrels", "boxes", "sacks" } : new string[] { "crates", "barrels", "fuel", "boxes", "sacks" };
            hq_PlanYard_diner = diner;
            hq_PlanYard_rows = rows;
        }
        if (planPhase == 6 && PhLoop(6, 100000))
        {
            string[] rows = hq_PlanYard_rows;
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < rows.Length)) PhBreak();
                else { hq_PlanYard_k = k; PhSubBegin(); }
            }
            hq_PlanYard_rows = rows;
            if (PhSubActive())
            {
                PhPush(); PlanYard_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 7)
        {
            bool diner = hq_PlanYard_diner;
            GenRng r8 = SR(8);
            hq_PlanYard_sel4 = 0;
            if (!diner && r8.Chance(700) && Fit(1)) hq_PlanYard_sel4 = 1;
            hq_PlanYard_diner = diner;
            hq_PlanYard_r8 = r8;
        }
        if (planPhase == 8)
        {
            if (hq_PlanYard_sel4 == 1)
            {
                int dx = hq_PlanYard_dx;
                int dz = hq_PlanYard_dz;
                GenRng r8 = hq_PlanYard_r8;
                int first = nEmit;
                hq_PlanYard_sel5 = 0;
                if (WallPlace(Dn("ms_handTruck"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r8) >= 0) hq_PlanYard_sel5 = 1;
                hq_PlanYard_dx = dx;
                hq_PlanYard_dz = dz;
                hq_PlanYard_r8 = r8;
                hq_PlanYard_first = first;
            }
        }
        if (planPhase == 9 && PhLoop(9, 100000))
        {
            if (!(hq_PlanYard_sel4 == 1 && hq_PlanYard_sel5 == 1)) PhBreak();
            else
            {
                int first = hq_PlanYard_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanYard_first = first;
            }
        }
        if (planPhase == 10)
        {
            GenRng r9 = SR(9);
            hq_PlanYard_sel6 = 0;
            if (r9.Chance(750) && Fit(1)) hq_PlanYard_sel6 = 1;
            hq_PlanYard_r9 = r9;
        }
        if (planPhase == 11)
        {
            if (hq_PlanYard_sel6 == 1)
            {
                int dx = hq_PlanYard_dx;
                int dz = hq_PlanYard_dz;
                GenRng r9 = hq_PlanYard_r9;
                // the waste: a dumpster at the fence or the wall near the door, else a bin
                int first = nEmit;
                hq_PlanYard_sel7 = 0;
                if (WallPlace(Dn("gen_dumpster"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r9) < 0 || !Keep(first, false)) hq_PlanYard_sel7 = 1;
                hq_PlanYard_dx = dx;
                hq_PlanYard_dz = dz;
                hq_PlanYard_r9 = r9;
                hq_PlanYard_first = first;
            }
        }
        if (planPhase == 12)
        {
            if (hq_PlanYard_sel6 == 1 && hq_PlanYard_sel7 == 1)
            {
                int dx = hq_PlanYard_dx;
                int dz = hq_PlanYard_dz;
                GenRng r9 = hq_PlanYard_r9;
                int first = hq_PlanYard_first;
                first = nEmit;
                hq_PlanYard_sel8 = 0;
                if (WallPlace(Dn("k_trashcan"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r9) >= 0) hq_PlanYard_sel8 = 1;
                hq_PlanYard_dx = dx;
                hq_PlanYard_dz = dz;
                hq_PlanYard_r9 = r9;
                hq_PlanYard_first = first;
            }
        }
        if (planPhase == 13 && PhLoop(13, 100000))
        {
            if (!(hq_PlanYard_sel6 == 1 && hq_PlanYard_sel7 == 1 && hq_PlanYard_sel8 == 1)) PhBreak();
            else
            {
                int first = hq_PlanYard_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanYard_first = first;
            }
        }
        if (planPhase == 14)
        {
            GenRng r10 = SR(10);
            hq_PlanYard_sel9 = 0;
            if (r10.Chance(450) && Fit(1)) hq_PlanYard_sel9 = 1;
            hq_PlanYard_r10 = r10;
        }
        if (planPhase == 15)
        {
            if (hq_PlanYard_sel9 == 1)
            {
                GenRng r10 = hq_PlanYard_r10;
                int first = nEmit;
                hq_PlanYard_sel10 = 0;
                if (WallPlace(Pick(ref r10, "s_boxLarge", 5, "w_planks", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r10) >= 0) hq_PlanYard_sel10 = 1;
                hq_PlanYard_r10 = r10;
                hq_PlanYard_first = first;
            }
        }
        if (planPhase == 16 && PhLoop(16, 100000))
        {
            if (!(hq_PlanYard_sel9 == 1 && hq_PlanYard_sel10 == 1)) PhBreak();
            else
            {
                int first = hq_PlanYard_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanYard_first = first;
            }
        }
    }
    void PlanYard_L1()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int dx = hq_PlanYard_dx;
            int dz = hq_PlanYard_dz;
            string[] rows = hq_PlanYard_rows;
            int k = hq_PlanYard_k;
            GenRng rk = SR(2 + k);
            if (!rk.Chance(k < 2 ? 800 : 500) || !Fit(2)) { hq_PlanYard_dx = dx; hq_PlanYard_dz = dz; hq_PlanYard_rows = rows; PhEnd(); return; }
            int first = nEmit;
            hq_PlanYard_sel3 = 0;
            if (GroupAt(GenData.FindGrp(rows[k]), GenData.A_OUT_EDGE, rk.Between(2, 3), WM_NEAR, dx, dz, 0, ref rk)) hq_PlanYard_sel3 = 1;
            hq_PlanYard_dx = dx;
            hq_PlanYard_dz = dz;
            hq_PlanYard_rows = rows;
            hq_PlanYard_first = first;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!(hq_PlanYard_sel3 == 1)) PhBreak();
            else
            {
                int first = hq_PlanYard_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanYard_first = first;
            }
        }
    }


    // ============================================================================================ FIRE PIT
    // Areas: the fire on trodden earth with its ring of seats; the wood pile and the chopping block at the edge;
    // a bucket of water by the fire.
    int FireRing(int minClr, int tx, int tz, bool soft, ref GenRng rng)
    {
        hq_FireRing_minClr = minClr;
        hq_FireRing_tx = tx;
        hq_FireRing_tz = tz;
        hq_FireRing_soft = soft;
        hq_FireRing_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); FireRing_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_FireRing_rng;
        return hq_FireRing_ret;
    }

    // FireRing in phases, a step a call (autophase4); FireRing() above runs them to the end
    void FireRing_Body()
    {
        planMax = 4;
        if (planPhase == 0)
        {
            int room = pRoom;
            hq_FireRing_room = room;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int tx = hq_FireRing_tx;
            int tz = hq_FireRing_tz;
            GenRng rng = hq_FireRing_rng;
            int fire = 0;
            if (planIter == 0) { hq_CentrePiece_def = Pick(ref rng, "n_campfire", 5, "s_campfirePit", 3); hq_CentrePiece_yaw = rng.Range(4) * 90; hq_CentrePiece_bothYaws = false; hq_CentrePiece_ringWant = 3; hq_CentrePiece_ringMin = 1; hq_CentrePiece_mode = WM_NEAR; hq_CentrePiece_tx = tx; hq_CentrePiece_tz = tz; hq_CentrePiece_need = 0; hq_CentrePiece_flags = EF_MANDATORY; hq_CentrePiece_rng = rng; PhSubReset(); }
            PhPush(); CentrePiece_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { rng = hq_CentrePiece_rng; fire = hq_CentrePiece_ret; PhBreak(); }
            hq_FireRing_tx = tx;
            hq_FireRing_tz = tz;
            hq_FireRing_rng = rng;
            hq_FireRing_fire = fire;
        }
        if (planPhase == 2)
        {
            bool soft = hq_FireRing_soft;
            int room = hq_FireRing_room;
            int fire = hq_FireRing_fire;
            if (fire < 0) { MandFail(1); { hq_FireRing_ret = -1; hq_FireRing_soft = soft; hq_FireRing_room = room; hq_FireRing_fire = fire; PhEnd(); return; } }
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
            hq_FireRing_sel1 = 0;
            if (!soft) hq_FireRing_sel1 = 1;
            hq_FireRing_soft = soft;
            hq_FireRing_room = room;
            hq_FireRing_fire = fire;
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (!(hq_FireRing_sel1 == 1)) PhBreak();
            else
            {
                GenRng rng = hq_FireRing_rng;
                int fire = hq_FireRing_fire;
                if (planIter == 0) { hq_FireGear_fire = fire; hq_FireGear_rng = rng; PhSubReset(); }
                PhPush(); FireGear_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { rng = hq_FireGear_rng; PhBreak(); }
                hq_FireRing_rng = rng;
                hq_FireRing_fire = fire;
            }
        }
        if (planPhase == 4)
        {
            int fire = hq_FireRing_fire;
            { hq_FireRing_ret = fire; hq_FireRing_fire = fire; PhEnd(); return; }
            hq_FireRing_fire = fire;
        }
    }

    // a few logs beside the fire, and the bucket of water
    void FireGear(int fire, ref GenRng rng)
    {
        hq_FireGear_fire = fire;
        hq_FireGear_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); FireGear_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_FireGear_rng;
    }

    // FireGear in phases, a step a call (autophase4); FireGear() above runs them to the end
    void FireGear_Body()
    {
        planMax = 5;
        if (planPhase == 0)
        {
            GenRng rng = hq_FireGear_rng;
            hq_FireGear_sel1 = 0;
            if (rng.Chance(650) && Fit(2)) hq_FireGear_sel1 = 1;
            hq_FireGear_rng = rng;
        }
        if (planPhase == 1)
        {
            if (hq_FireGear_sel1 == 1)
            {
                int fire = hq_FireGear_fire;
                GenRng rng = hq_FireGear_rng;
                int first = nEmit;
                hq_FireGear_sel2 = 0;
                if (GroupBeside(GenData.FindGrp("logpile"), fire, 2, 0, ref rng)) hq_FireGear_sel2 = 1;
                hq_FireGear_fire = fire;
                hq_FireGear_rng = rng;
                hq_FireGear_first = first;
            }
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (!(hq_FireGear_sel1 == 1 && hq_FireGear_sel2 == 1)) PhBreak();
            else
            {
                int first = hq_FireGear_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_FireGear_first = first;
            }
        }
        if (planPhase == 3)
        {
            GenRng rng = hq_FireGear_rng;
            hq_FireGear_sel3 = 0;
            if (rng.Chance(600) && Fit(1)) hq_FireGear_sel3 = 1;
            hq_FireGear_rng = rng;
        }
        if (planPhase == 4)
        {
            if (hq_FireGear_sel3 == 1)
            {
                int fire = hq_FireGear_fire;
                GenRng rng = hq_FireGear_rng;
                int first = nEmit;
                FSet(Pick(ref rng, "m_bucketWater", 6, "s_bucket", 3));
                hq_FireGear_sel4 = 0;
                if (FormBeside(GenData.GL_ROW, 0, fire, 0, ref rng) >= 0) hq_FireGear_sel4 = 1;
                hq_FireGear_fire = fire;
                hq_FireGear_rng = rng;
                hq_FireGear_first = first;
            }
        }
        if (planPhase == 5 && PhLoop(5, 100000))
        {
            if (!(hq_FireGear_sel3 == 1 && hq_FireGear_sel4 == 1)) PhBreak();
            else
            {
                int first = hq_FireGear_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_FireGear_first = first;
            }
        }
    }

    // Something that belongs to a fire but stands back from its ring of seats (the tent, the wood pile): at one
    // of eight bearings from the fire, the nearest distance that is free, turned by relYaw to the bearing (180 =
    // facing the fire). offGates: not on the line from an entrance to the fire. Kept, or -1.
    int AroundFire(int def, int fire, int relYaw, int distMin, int distMax, int flags, bool must, bool offGates, ref GenRng rng)
    {
        hq_AroundFire_def = def;
        hq_AroundFire_fire = fire;
        hq_AroundFire_relYaw = relYaw;
        hq_AroundFire_distMin = distMin;
        hq_AroundFire_distMax = distMax;
        hq_AroundFire_flags = flags;
        hq_AroundFire_must = must;
        hq_AroundFire_offGates = offGates;
        hq_AroundFire_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); AroundFire_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_AroundFire_rng;
        return hq_AroundFire_ret;
    }

    // AroundFire in phases, a step a call (autophase4); AroundFire() above runs them to the end
    void AroundFire_Body()
    {
        planMax = 2;
        if (planPhase == 0)
        {
            int fire = hq_AroundFire_fire;
            GenRng rng = hq_AroundFire_rng;
            int room = pRoom, fx = emitX[fire], fz = emitZ[fire];
            int a0 = rng.Range(8);
            hq_AroundFire_fire = fire;
            hq_AroundFire_rng = rng;
            hq_AroundFire_room = room;
            hq_AroundFire_fx = fx;
            hq_AroundFire_fz = fz;
            hq_AroundFire_a0 = a0;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 8)) PhBreak();
                else { hq_AroundFire_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); AroundFire_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 2)
        {
            { hq_AroundFire_ret = -1; PhEnd(); return; }
        }
    }
    void AroundFire_L2()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int def = hq_AroundFire_def;
            int fire = hq_AroundFire_fire;
            int relYaw = hq_AroundFire_relYaw;
            int flags = hq_AroundFire_flags;
            bool offGates = hq_AroundFire_offGates;
            int room = hq_AroundFire_room;
            int fx = hq_AroundFire_fx;
            int fz = hq_AroundFire_fz;
            int a = hq_AroundFire_a;
            int dist = hq_AroundFire_dist;
            int px = fx + dist * sinTab[a] / 4096, pz = fz + dist * sinTab[(a + 90) % 360] / 4096;
            int ix = FloorDiv(px, GenData.INNER_MM), iz = FloorDiv(pz, GenData.INNER_MM);
            if (ix < 0 || iz < 0 || ix >= IW || iz >= IH || innerRoom[iz * IW + ix] != room) { hq_AroundFire_def = def; hq_AroundFire_fire = fire; hq_AroundFire_relYaw = relYaw; hq_AroundFire_flags = flags; hq_AroundFire_offGates = offGates; hq_AroundFire_room = room; hq_AroundFire_fx = fx; hq_AroundFire_fz = fz; hq_AroundFire_a = a; phBreak = true; PhEnd(); return; }
            if (offGates && NearGateLine(px, pz, fx, fz)) { hq_AroundFire_def = def; hq_AroundFire_fire = fire; hq_AroundFire_relYaw = relYaw; hq_AroundFire_flags = flags; hq_AroundFire_offGates = offGates; hq_AroundFire_room = room; hq_AroundFire_fx = fx; hq_AroundFire_fz = fz; hq_AroundFire_a = a; phBreak = true; PhEnd(); return; }
            int first = nEmit;
            int e = StampAt(def, room, px, pz, (a + relYaw) % 360, flags, -1, fire, false);
            hq_AroundFire_def = def;
            hq_AroundFire_fire = fire;
            hq_AroundFire_relYaw = relYaw;
            hq_AroundFire_flags = flags;
            hq_AroundFire_offGates = offGates;
            hq_AroundFire_room = room;
            hq_AroundFire_fx = fx;
            hq_AroundFire_fz = fz;
            hq_AroundFire_a = a;
            hq_AroundFire_first = first;
            hq_AroundFire_e = e;
        }
        if (planPhase == 1)
        {
            bool must = hq_AroundFire_must;
            int first = hq_AroundFire_first;
            int e = hq_AroundFire_e;
            if (e >= 0 && Keep(first, must))
            {
                { hq_AroundFire_ret = e; hq_AroundFire_must = must; hq_AroundFire_first = first; hq_AroundFire_e = e; phReturn = true; PhEnd(); return; }
            }
            hq_AroundFire_must = must;
            hq_AroundFire_first = first;
            hq_AroundFire_e = e;
        }
    }

    void AroundFire_L1()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int a0 = hq_AroundFire_a0;
            int k = hq_AroundFire_k;
            int a = (a0 + k) % 8 * 45;
            hq_AroundFire_a0 = a0;
            hq_AroundFire_a = a;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int distMin = hq_AroundFire_distMin;
            int distMax = hq_AroundFire_distMax;
            if (!PhSubActive())
            {
                if (planIter == 0) hq_AroundFire_dist0 = distMin;
                int dist = hq_AroundFire_dist0 + planIter * 300;
                if (!(dist <= distMax)) PhBreak();
                else { hq_AroundFire_dist = dist; PhSubBegin(); }
            }
            hq_AroundFire_distMin = distMin;
            hq_AroundFire_distMax = distMax;
            if (PhSubActive())
            {
                PhPush(); AroundFire_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { PhEnd(); return; }
            }
        }
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
        hq_WoodPile_fire = fire;
        hq_WoodPile_atEdge = atEdge;
        hq_WoodPile_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); WoodPile_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_WoodPile_rng;
    }

    // WoodPile in phases, a step a call (autophase4); WoodPile() above runs them to the end
    void WoodPile_Body()
    {
        planMax = 10;
        if (planPhase == 0)
        {
            bool atEdge = hq_WoodPile_atEdge;
            GenRng rng = hq_WoodPile_rng;
            if (!Fit(1)) { hq_WoodPile_atEdge = atEdge; hq_WoodPile_rng = rng; PhEnd(); return; }
            int first = nEmit;
            int stackDef = Pick(ref rng, "n_logStack", 6, "n_logStackLarge", 3);
            int stack = -1;
            hq_WoodPile_sel1 = 0;
            if (atEdge) hq_WoodPile_sel1 = 1;
            hq_WoodPile_atEdge = atEdge;
            hq_WoodPile_rng = rng;
            hq_WoodPile_first = first;
            hq_WoodPile_stackDef = stackDef;
            hq_WoodPile_stack = stack;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!(hq_WoodPile_sel1 == 1)) PhBreak();
            else
            {
                int fire = hq_WoodPile_fire;
                GenRng rng = hq_WoodPile_rng;
                int stackDef = hq_WoodPile_stackDef;
                int stack = hq_WoodPile_stack;
                if (planIter == 0) { hq_WallKeep_def = stackDef; hq_WallKeep_mode = fire >= 0 ? WM_NEAR : WM_ANY; hq_WallKeep_tx = fire >= 0 ? emitX[fire] : 0; hq_WallKeep_tz = fire >= 0 ? emitZ[fire] : 0; hq_WallKeep_win = WIN_ANY; hq_WallKeep_flags = 0; hq_WallKeep_rng = rng; PhSubReset(); }
                PhPush(); WallKeep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { rng = hq_WallKeep_rng; stack = hq_WallKeep_ret; PhBreak(); }
                hq_WoodPile_fire = fire;
                hq_WoodPile_rng = rng;
                hq_WoodPile_stackDef = stackDef;
                hq_WoodPile_stack = stack;
            }
        }
        if (planPhase == 2)
        {
            int fire = hq_WoodPile_fire;
            int stack = hq_WoodPile_stack;
            hq_WoodPile_sel2 = 0;
            if (stack < 0 && fire >= 0) hq_WoodPile_sel2 = 1;
            hq_WoodPile_fire = fire;
            hq_WoodPile_stack = stack;
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (!(hq_WoodPile_sel2 == 1)) PhBreak();
            else
            {
                int fire = hq_WoodPile_fire;
                GenRng rng = hq_WoodPile_rng;
                int stackDef = hq_WoodPile_stackDef;
                int stack = hq_WoodPile_stack;
                if (planIter == 0) { hq_AroundFire_def = stackDef; hq_AroundFire_fire = fire; hq_AroundFire_relYaw = 90; hq_AroundFire_distMin = 2700; hq_AroundFire_distMax = 3600; hq_AroundFire_flags = 0; hq_AroundFire_must = false; hq_AroundFire_offGates = false; hq_AroundFire_rng = rng; PhSubReset(); }
                PhPush(); AroundFire_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { rng = hq_AroundFire_rng; stack = hq_AroundFire_ret; PhBreak(); }
                hq_WoodPile_fire = fire;
                hq_WoodPile_rng = rng;
                hq_WoodPile_stackDef = stackDef;
                hq_WoodPile_stack = stack;
            }
        }
        if (planPhase == 4)
        {
            int fire = hq_WoodPile_fire;
            int stackDef = hq_WoodPile_stackDef;
            int stack = hq_WoodPile_stack;
            hq_WoodPile_sel3 = 0;
            if (stack < 0 && fire >= 0 && stackDef != Dn("n_logStack")) hq_WoodPile_sel3 = 1;
            hq_WoodPile_fire = fire;
            hq_WoodPile_stackDef = stackDef;
            hq_WoodPile_stack = stack;
        }
        if (planPhase == 5 && PhLoop(5, 100000))
        {
            if (!(hq_WoodPile_sel3 == 1)) PhBreak();
            else
            {
                int fire = hq_WoodPile_fire;
                GenRng rng = hq_WoodPile_rng;
                int stack = hq_WoodPile_stack;
                if (planIter == 0) { hq_AroundFire_def = Dn("n_logStack"); hq_AroundFire_fire = fire; hq_AroundFire_relYaw = 90; hq_AroundFire_distMin = 2400; hq_AroundFire_distMax = 3600; hq_AroundFire_flags = 0; hq_AroundFire_must = false; hq_AroundFire_offGates = false; hq_AroundFire_rng = rng; PhSubReset(); }
                PhPush(); AroundFire_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { rng = hq_AroundFire_rng; stack = hq_AroundFire_ret; PhBreak(); }
                hq_WoodPile_fire = fire;
                hq_WoodPile_rng = rng;
                hq_WoodPile_stack = stack;
            }
        }
        if (planPhase == 6)
        {
            bool atEdge = hq_WoodPile_atEdge;
            int stack = hq_WoodPile_stack;
            hq_WoodPile_sel4 = 0;
            if (stack < 0 && !atEdge) hq_WoodPile_sel4 = 1;
            hq_WoodPile_atEdge = atEdge;
            hq_WoodPile_stack = stack;
        }
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            if (!(hq_WoodPile_sel4 == 1)) PhBreak();
            else
            {
                GenRng rng = hq_WoodPile_rng;
                int stack = hq_WoodPile_stack;
                if (planIter == 0) { hq_WallKeep_def = Dn("n_logStack"); hq_WallKeep_mode = WM_ANY; hq_WallKeep_tx = 0; hq_WallKeep_tz = 0; hq_WallKeep_win = WIN_ANY; hq_WallKeep_flags = 0; hq_WallKeep_rng = rng; PhSubReset(); }
                PhPush(); WallKeep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { rng = hq_WallKeep_rng; stack = hq_WallKeep_ret; PhBreak(); }
                hq_WoodPile_rng = rng;
                hq_WoodPile_stack = stack;
            }
        }
        if (planPhase == 8)
        {
            GenRng rng = hq_WoodPile_rng;
            int stack = hq_WoodPile_stack;
            if (stack < 0) { hq_WoodPile_rng = rng; hq_WoodPile_stack = stack; PhEnd(); return; }
            hq_WoodPile_sel5 = 0;
            if (rng.Chance(750) && Fit(1)) hq_WoodPile_sel5 = 1;
            hq_WoodPile_rng = rng;
            hq_WoodPile_stack = stack;
        }
        if (planPhase == 9)
        {
            if (hq_WoodPile_sel5 == 1)
            {
                GenRng rng = hq_WoodPile_rng;
                int first = hq_WoodPile_first;
                int stack = hq_WoodPile_stack;
                first = nEmit;
                hq_WoodPile_sel6 = 0;
                if (RelBeside(Pick(ref rng, "n_stump", 6, "n_stumpRound", 4), stack, 0, 250, 0, ref rng) >= 0) hq_WoodPile_sel6 = 1;
                hq_WoodPile_rng = rng;
                hq_WoodPile_first = first;
                hq_WoodPile_stack = stack;
            }
        }
        if (planPhase == 10 && PhLoop(10, 100000))
        {
            if (!(hq_WoodPile_sel5 == 1 && hq_WoodPile_sel6 == 1)) PhBreak();
            else
            {
                int first = hq_WoodPile_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_WoodPile_first = first;
            }
        }
    }

    // The ring of seats round a fire: two or three, 1.3 to 1.9 m from it, each facing it (a bench), lying across the
    // bearing (a log) or standing (a stump); in a garden a bench is likeliest, in the wood logs and stumps.
    void FireSeats(int fire, bool wood, int k)
    {
        hq_FireSeats_fire = fire;
        hq_FireSeats_wood = wood;
        hq_FireSeats_k = k;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); FireSeats_Body(); fin = PhAdvance(); PhPop(); }
    }

    // FireSeats in phases, a step a call (autophase4); FireSeats() above runs them to the end
    void FireSeats_Body()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int fire = hq_FireSeats_fire;
            int k = hq_FireSeats_k;
            if (fire < 0) { hq_FireSeats_fire = fire; hq_FireSeats_k = k; PhEnd(); return; }
            GenRng rng = SR(k);
            int n = rng.Between(2, 3);
            if (roomType[pRoom] == GenData.R_FIREPIT) n = 3;   // revision 30: a fire pit seats three at the least
            int s = 0;
            hq_FireSeats_fire = fire;
            hq_FireSeats_k = k;
            hq_FireSeats_rng = rng;
            hq_FireSeats_n = n;
            hq_FireSeats_s = s;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int n = hq_FireSeats_n;
            int s = hq_FireSeats_s;
            if (!PhSubActive())
            {
                int tries = planIter;
                if (!(s < n && tries < 9 && (Fit(1) || roomType[pRoom] == GenData.R_FIREPIT))) PhBreak();
                else { hq_FireSeats_tries = tries; PhSubBegin(); }
            }
            hq_FireSeats_n = n;
            hq_FireSeats_s = s;
            if (PhSubActive())
            {
                PhPush(); FireSeats_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
    }
    void FireSeats_L1()
    {
        planMax = 2;
        if (planPhase == 0)
        {
            bool wood = hq_FireSeats_wood;
            GenRng rng = hq_FireSeats_rng;
            int def = wood ? Pick(ref rng, "w_logA", 5, "n_stumpRound", 3, "n_stump", 2) : Pick(ref rng, "k_bench", 4, "w_logA", 3, "n_stumpRound", 3);
            if (CountInRoom(def, pRoom) >= ScaledCap(GenData.defRoomCap[def], pRoom)) { hq_FireSeats_wood = wood; hq_FireSeats_rng = rng; hq_FireSeats_def = def; PhEnd(); return; }
            int rel = def == Dn("k_bench") ? 180 : (def == Dn("w_logA") ? 90 : 0);
            hq_FireSeats_wood = wood;
            hq_FireSeats_rng = rng;
            hq_FireSeats_def = def;
            hq_FireSeats_rel = rel;
        }
        // a log is a small loose thing: it keeps within a step of the fire (G25: nothing loose alone in the open)
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int fire = hq_FireSeats_fire;
            GenRng rng = hq_FireSeats_rng;
            int def = hq_FireSeats_def;
            int rel = hq_FireSeats_rel;
            int ht1 = 0;
            if (planIter == 0) { hq_AroundFire_def = def; hq_AroundFire_fire = fire; hq_AroundFire_relYaw = rel; hq_AroundFire_distMin = 1300; hq_AroundFire_distMax = def == Dn("w_logA") ? 1600 : 1900; hq_AroundFire_flags = 0; hq_AroundFire_must = false; hq_AroundFire_offGates = false; hq_AroundFire_rng = rng; PhSubReset(); }
            PhPush(); AroundFire_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { rng = hq_AroundFire_rng; ht1 = hq_AroundFire_ret; PhBreak(); }
            hq_FireSeats_fire = fire;
            hq_FireSeats_rng = rng;
            hq_FireSeats_def = def;
            hq_FireSeats_rel = rel;
            hq_FireSeats_ht1 = ht1;
        }
        if (planPhase == 2)
        {
            int s = hq_FireSeats_s;
            int ht1 = hq_FireSeats_ht1;
            if (ht1 >= 0)
            {
                s++;
            }
            hq_FireSeats_s = s;
            hq_FireSeats_ht1 = ht1;
        }
    }


    void PlanFirepit()
    {
        planMax = 14;
        int room = pRoom;
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            hq_PlanFirepit_r = r;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            GenRng r = hq_PlanFirepit_r;
            if (planIter == 0) { hq_StonePaths_k0 = 40; hq_StonePaths_rng = r; PhSubReset(); }
            PhPush(); StonePaths_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_StonePaths_rng; PhBreak(); }
            hq_PlanFirepit_r = r;
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            GenRng r = hq_PlanFirepit_r;
            int fire = 0;
            if (planIter == 0) { hq_FireRing_minClr = 6; hq_FireRing_tx = RoomMidX(room); hq_FireRing_tz = RoomMidZ(room); hq_FireRing_soft = false; hq_FireRing_rng = r; PhSubReset(); }
            PhPush(); FireRing_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_FireRing_rng; fire = hq_FireRing_ret; PhBreak(); }
            hq_PlanFirepit_r = r;
            hq_PlanFirepit_fire = fire;
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            int fire = hq_PlanFirepit_fire;
            if (planIter == 0) { hq_FireSeats_fire = fire; hq_FireSeats_wood = false; hq_FireSeats_k = 7; PhSubReset(); }
            PhPush(); FireSeats_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
            hq_PlanFirepit_fire = fire;
        }
        if (planPhase == 4)
        {
            GenRng r2 = SR(2);
            hq_PlanFirepit_sel1 = 0;
            if (r2.Chance(850)) hq_PlanFirepit_sel1 = 1;
            hq_PlanFirepit_r2 = r2;
        }
        if (planPhase == 5 && PhLoop(5, 100000))
        {
            if (!(hq_PlanFirepit_sel1 == 1)) PhBreak();
            else
            {
                int fire = hq_PlanFirepit_fire;
                GenRng r2 = hq_PlanFirepit_r2;
                if (planIter == 0) { hq_WoodPile_fire = fire; hq_WoodPile_atEdge = true; hq_WoodPile_rng = r2; PhSubReset(); }
                PhPush(); WoodPile_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r2 = hq_WoodPile_rng; PhBreak(); }
                hq_PlanFirepit_fire = fire;
                hq_PlanFirepit_r2 = r2;
            }
        }
        if (planPhase == 6)
        {
            DoorSides(50);
        }
        if (planPhase == 7)
        {
            GenRng r3 = SR(3);
            int trees = TreeCount(room, ref r3);
            hq_PlanFirepit_r3 = r3;
            hq_PlanFirepit_trees = trees;
        }
        if (planPhase == 8 && PhLoop(8, 100000))
        {
            int trees = hq_PlanFirepit_trees;
            if (!PhSubActive())
            {
                int t = planIter;
                if (!(t < trees)) PhBreak();
                else { hq_PlanFirepit_t = t; PhSubBegin(); }
            }
            hq_PlanFirepit_trees = trees;
            if (PhSubActive())
            {
                PhPush(); PlanFirepit_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 9)
        {
            GenRng r3 = hq_PlanFirepit_r3;
            hq_PlanFirepit_sel2 = 0;
            if (r3.Chance(350)) hq_PlanFirepit_sel2 = 1;
            hq_PlanFirepit_r3 = r3;
        }
        if (planPhase == 10 && PhLoop(10, 100000))
        {
            if (!(hq_PlanFirepit_sel2 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_BenchAtEdge_k = 4; PhSubReset(); }
                PhPush(); BenchAtEdge_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
        if (planPhase == 11)
        {
            ClearBans();
        }
        if (planPhase == 12 && PhLoop(12, 100000))
        {
            if (planIter == 0) { hq_Borders_k0 = 10; hq_Borders_max = 6; hq_Borders_soil = false; PhSubReset(); }
            PhPush(); Borders_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 13)
        {
            GenRng r5 = SR(5);
            hq_PlanFirepit_r5 = r5;
        }
        if (planPhase == 14 && PhLoop(14, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 2 && Fit(1))) PhBreak();
                else { hq_PlanFirepit_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanFirepit_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
    }
    void PlanFirepit_L1()
    {
        int room = pRoom;
        planMax = 0;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int t = hq_PlanFirepit_t;
            if (planIter == 0) { hq_LawnTree_k = 60 + t; PhSubReset(); }
            PhPush(); LawnTree_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }

    void PlanFirepit_L2()
    {
        int room = pRoom;
        planMax = 1;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            GenRng r5 = hq_PlanFirepit_r5;
            bool ht1 = false;
            if (planIter == 0) { hq_Island_rng = r5; PhSubReset(); }
            PhPush(); Island_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r5 = hq_Island_rng; ht1 = hq_Island_ret; PhBreak(); }
            hq_PlanFirepit_r5 = r5;
            hq_PlanFirepit_ht1 = ht1;
        }
        if (planPhase == 1)
        {
            bool ht1 = hq_PlanFirepit_ht1;
            if (!ht1)
            {
                { hq_PlanFirepit_ht1 = ht1; phBreak = true; PhEnd(); return; }
            }
            hq_PlanFirepit_ht1 = ht1;
        }
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
        hq_Stand_cx = cx;
        hq_Stand_cz = cz;
        hq_Stand_n = n;
        hq_Stand_main = main;
        hq_Stand_accent = accent;
        hq_Stand_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); Stand_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_Stand_rng;
        return hq_Stand_ret;
    }

    // Stand in phases, a step a call (autophase4); Stand() above runs them to the end
    void Stand_Body()
    {
        planMax = 2;
        if (planPhase == 0)
        {
            int placed = 0;
            hq_Stand_placed = placed;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int cx = hq_Stand_cx;
            int cz = hq_Stand_cz;
            int n = hq_Stand_n;
            int main = hq_Stand_main;
            int accent = hq_Stand_accent;
            GenRng rng = hq_Stand_rng;
            int placed = hq_Stand_placed;
            if (planIter == 0) hq_Stand_tries0 = n * 6;
            int tries = hq_Stand_tries0 - planIter;
            if (!(tries > 0 && placed < n)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                int a = rng.Range(360), rad = placed == 0 ? rng.Range(500) : 1200 + rng.Range(2000);
                int px = cx + rad * sinTab[a] / 4096, pz = cz + rad * sinTab[(a + 90) % 360] / 4096;
                int def = (accent >= 0 && rng.Chance(250)) ? accent : main;
                if (TreeAt(def, px, pz, ref rng) >= 0) placed++;
            }
            hq_Stand_cx = cx;
            hq_Stand_cz = cz;
            hq_Stand_n = n;
            hq_Stand_main = main;
            hq_Stand_accent = accent;
            hq_Stand_rng = rng;
            hq_Stand_placed = placed;
        }
        if (planPhase == 2)
        {
            int placed = hq_Stand_placed;
            { hq_Stand_ret = placed; hq_Stand_placed = placed; PhEnd(); return; }
            hq_Stand_placed = placed;
        }
    }

    int standN;
    int[] standX = new int[16], standZ = new int[16];

    void PlanForest(bool camp)
    {
        hq_PlanForest_camp = camp;
        planMax = 56;
        int room = pRoom, area = AreaM2(room);
        // in three parts a step each (Cor: build freezes): the clearing and the camp, the stands of trees, the rest
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            hq_PlanForest_r = r;
        }
        // (a) the middle of the area and the path to it from the entrances
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            GenRng r = hq_PlanForest_r;
            int hub = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = WM_NEAR; hq_OpenSpot_tx = RoomMidX(room); hq_OpenSpot_tz = RoomMidZ(room); hq_OpenSpot_need = 0; hq_OpenSpot_rng = r; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_OpenSpot_rng; hub = hq_OpenSpot_ret; PhBreak(); }
            hq_PlanForest_r = r;
            hq_PlanForest_hub = hub;
        }
        if (planPhase == 2)
        {
            int hub = hq_PlanForest_hub;
            if (hub < 0) hub = roomCells[roomCellStart[room] + roomCellCount[room] / 2];
            int hubX = hub % IW * GenData.INNER_MM + 250, hubZ = hub / IW * GenData.INNER_MM + 250;
            int fire = -1, tent = -1;
            hq_PlanForest_sel1 = 0;
            if (camp) hq_PlanForest_sel1 = 1;
            hq_PlanForest_hub = hub;
            hq_PlanForest_hubX = hubX;
            hq_PlanForest_hubZ = hubZ;
            hq_PlanForest_fire = fire;
            hq_PlanForest_tent = tent;
        }
        // (b) the camp in its clearing: the fire in the middle, the tent facing it
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (!(hq_PlanForest_sel1 == 1)) PhBreak();
            else
            {
                GenRng r = hq_PlanForest_r;
                int hubX = hq_PlanForest_hubX;
                int hubZ = hq_PlanForest_hubZ;
                int fire = hq_PlanForest_fire;
                if (planIter == 0) { hq_FireRing_minClr = 7; hq_FireRing_tx = hubX; hq_FireRing_tz = hubZ; hq_FireRing_soft = true; hq_FireRing_rng = r; PhSubReset(); }
                PhPush(); FireRing_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r = hq_FireRing_rng; fire = hq_FireRing_ret; PhBreak(); }
                hq_PlanForest_r = r;
                hq_PlanForest_hubX = hubX;
                hq_PlanForest_hubZ = hubZ;
                hq_PlanForest_fire = fire;
            }
        }
        if (planPhase == 4)
        {
            if (hq_PlanForest_sel1 == 1)
            {
                int hub = hq_PlanForest_hub;
                int hubX = hq_PlanForest_hubX;
                int hubZ = hq_PlanForest_hubZ;
                int fire = hq_PlanForest_fire;
                if (fire >= 0) { hubX = emitX[fire]; hubZ = emitZ[fire]; hub = FloorDiv(hubZ, GenData.INNER_MM) * IW + FloorDiv(hubX, GenData.INNER_MM); }
                hq_PlanForest_hub = hub;
                hq_PlanForest_hubX = hubX;
                hq_PlanForest_hubZ = hubZ;
                hq_PlanForest_fire = fire;
            }
        }
        if (planPhase == 5)
        {
            int fire = hq_PlanForest_fire;
            FindGates(room);
            hq_PlanForest_sel2 = 0;
            if (camp && fire >= 0) hq_PlanForest_sel2 = 1;
            hq_PlanForest_fire = fire;
        }
        if (planPhase == 6)
        {
            if (hq_PlanForest_sel2 == 1)
            {
                // the tent across the fire from the way in, its door to the fire; the small tent if the large one has no room
                GenRng r2 = SR(2);
                int tentDef = Pick(ref r2, "n_tentOpen", 5, "n_tentClosed", 3);
                hq_PlanForest_r2 = r2;
                hq_PlanForest_tentDef = tentDef;
            }
        }
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            if (!(hq_PlanForest_sel2 == 1)) PhBreak();
            else
            {
                int fire = hq_PlanForest_fire;
                int tent = hq_PlanForest_tent;
                GenRng r2 = hq_PlanForest_r2;
                int tentDef = hq_PlanForest_tentDef;
                if (planIter == 0) { hq_AroundFire_def = tentDef; hq_AroundFire_fire = fire; hq_AroundFire_relYaw = 180; hq_AroundFire_distMin = 2600; hq_AroundFire_distMax = 3800; hq_AroundFire_flags = EF_MANDATORY; hq_AroundFire_must = true; hq_AroundFire_offGates = true; hq_AroundFire_rng = r2; PhSubReset(); }
                PhPush(); AroundFire_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r2 = hq_AroundFire_rng; tent = hq_AroundFire_ret; PhBreak(); }
                hq_PlanForest_fire = fire;
                hq_PlanForest_tent = tent;
                hq_PlanForest_r2 = r2;
                hq_PlanForest_tentDef = tentDef;
            }
        }
        if (planPhase == 8)
        {
            if (hq_PlanForest_sel2 == 1)
            {
                int tent = hq_PlanForest_tent;
                int tentDef = hq_PlanForest_tentDef;
                hq_PlanForest_sel3 = 0;
                if (tent < 0 && tentDef != Dn("n_tentClosed")) hq_PlanForest_sel3 = 1;
                hq_PlanForest_tent = tent;
                hq_PlanForest_tentDef = tentDef;
            }
        }
        if (planPhase == 9 && PhLoop(9, 100000))
        {
            if (!(hq_PlanForest_sel2 == 1 && hq_PlanForest_sel3 == 1)) PhBreak();
            else
            {
                int fire = hq_PlanForest_fire;
                int tent = hq_PlanForest_tent;
                GenRng r2 = hq_PlanForest_r2;
                if (planIter == 0) { hq_AroundFire_def = Dn("n_tentClosed"); hq_AroundFire_fire = fire; hq_AroundFire_relYaw = 180; hq_AroundFire_distMin = 2600; hq_AroundFire_distMax = 3800; hq_AroundFire_flags = EF_MANDATORY; hq_AroundFire_must = true; hq_AroundFire_offGates = true; hq_AroundFire_rng = r2; PhSubReset(); }
                PhPush(); AroundFire_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r2 = hq_AroundFire_rng; tent = hq_AroundFire_ret; PhBreak(); }
                hq_PlanForest_fire = fire;
                hq_PlanForest_tent = tent;
                hq_PlanForest_r2 = r2;
            }
        }
        if (planPhase == 10)
        {
            if (hq_PlanForest_sel2 == 1)
            {
                int tent = hq_PlanForest_tent;
                // a narrow clearing: the tent with its back to the fence, as near the fire as the fence allows
                hq_PlanForest_sel4 = 0;
                if (tent < 0) hq_PlanForest_sel4 = 1;
                hq_PlanForest_tent = tent;
            }
        }
        if (planPhase == 11 && PhLoop(11, 100000))
        {
            if (!(hq_PlanForest_sel2 == 1 && hq_PlanForest_sel4 == 1)) PhBreak();
            else
            {
                int hubX = hq_PlanForest_hubX;
                int hubZ = hq_PlanForest_hubZ;
                int tent = hq_PlanForest_tent;
                GenRng r2 = hq_PlanForest_r2;
                int tentDef = hq_PlanForest_tentDef;
                if (planIter == 0) { hq_WallKeep_def = tentDef; hq_WallKeep_mode = WM_NEAR; hq_WallKeep_tx = hubX; hq_WallKeep_tz = hubZ; hq_WallKeep_win = WIN_ANY; hq_WallKeep_flags = EF_MANDATORY; hq_WallKeep_rng = r2; PhSubReset(); }
                PhPush(); WallKeep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r2 = hq_WallKeep_rng; tent = hq_WallKeep_ret; PhBreak(); }
                hq_PlanForest_hubX = hubX;
                hq_PlanForest_hubZ = hubZ;
                hq_PlanForest_tent = tent;
                hq_PlanForest_r2 = r2;
                hq_PlanForest_tentDef = tentDef;
            }
        }
        if (planPhase == 12)
        {
            if (hq_PlanForest_sel2 == 1)
            {
                int tent = hq_PlanForest_tent;
                hq_PlanForest_sel5 = 0;
                if (tent < 0) hq_PlanForest_sel5 = 1;
                hq_PlanForest_tent = tent;
            }
        }
        if (planPhase == 13 && PhLoop(13, 100000))
        {
            if (!(hq_PlanForest_sel2 == 1 && hq_PlanForest_sel5 == 1)) PhBreak();
            else
            {
                int hubX = hq_PlanForest_hubX;
                int hubZ = hq_PlanForest_hubZ;
                int tent = hq_PlanForest_tent;
                GenRng r2 = hq_PlanForest_r2;
                if (planIter == 0) { hq_WallKeep_def = Dn("n_tentClosed"); hq_WallKeep_mode = WM_NEAR; hq_WallKeep_tx = hubX; hq_WallKeep_tz = hubZ; hq_WallKeep_win = WIN_ANY; hq_WallKeep_flags = EF_MANDATORY; hq_WallKeep_rng = r2; PhSubReset(); }
                PhPush(); WallKeep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r2 = hq_WallKeep_rng; tent = hq_WallKeep_ret; PhBreak(); }
                hq_PlanForest_hubX = hubX;
                hq_PlanForest_hubZ = hubZ;
                hq_PlanForest_tent = tent;
                hq_PlanForest_r2 = r2;
            }
        }
        if (planPhase == 14)
        {
            if (hq_PlanForest_sel2 == 1)
            {
                int tent = hq_PlanForest_tent;
                hq_PlanForest_sel6 = 0;
                if (tent < 0) hq_PlanForest_sel6 = 1;
                hq_PlanForest_tent = tent;
            }
        }
        if (planPhase == 15 && PhLoop(15, 100000))
        {
            if (!(hq_PlanForest_sel2 == 1 && hq_PlanForest_sel6 == 1)) PhBreak();
            else
            {
                int fire = hq_PlanForest_fire;
                int tent = hq_PlanForest_tent;
                GenRng r2 = hq_PlanForest_r2;
                if (planIter == 0) { hq_AroundFire_def = Dn("n_tentClosed"); hq_AroundFire_fire = fire; hq_AroundFire_relYaw = 180; hq_AroundFire_distMin = 2300; hq_AroundFire_distMax = 3800; hq_AroundFire_flags = EF_MANDATORY; hq_AroundFire_must = true; hq_AroundFire_offGates = false; hq_AroundFire_rng = r2; PhSubReset(); }
                PhPush(); AroundFire_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r2 = hq_AroundFire_rng; tent = hq_AroundFire_ret; PhBreak(); }
                hq_PlanForest_fire = fire;
                hq_PlanForest_tent = tent;
                hq_PlanForest_r2 = r2;
            }
        }
        if (planPhase == 16)
        {
            if (hq_PlanForest_sel2 == 1)
            {
                int tent = hq_PlanForest_tent;
                // a clearing too narrow for any tent (a strip of wood two cells long): a bedroll laid out by the fire,
                // its foot to the fire, the gear beside it as beside a tent
                hq_PlanForest_sel7 = 0;
                if (tent < 0) hq_PlanForest_sel7 = 1;
                hq_PlanForest_tent = tent;
            }
        }
        if (planPhase == 17 && PhLoop(17, 100000))
        {
            if (!(hq_PlanForest_sel2 == 1 && hq_PlanForest_sel7 == 1)) PhBreak();
            else
            {
                int fire = hq_PlanForest_fire;
                int tent = hq_PlanForest_tent;
                GenRng r2 = hq_PlanForest_r2;
                if (planIter == 0) { hq_AroundFire_def = Dn("s_bedroll"); hq_AroundFire_fire = fire; hq_AroundFire_relYaw = 0; hq_AroundFire_distMin = 1500; hq_AroundFire_distMax = 2700; hq_AroundFire_flags = EF_MANDATORY; hq_AroundFire_must = true; hq_AroundFire_offGates = false; hq_AroundFire_rng = r2; PhSubReset(); }
                PhPush(); AroundFire_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r2 = hq_AroundFire_rng; tent = hq_AroundFire_ret; PhBreak(); }
                hq_PlanForest_fire = fire;
                hq_PlanForest_tent = tent;
                hq_PlanForest_r2 = r2;
            }
        }
        if (planPhase == 18)
        {
            if (hq_PlanForest_sel2 == 1)
            {
                int tent = hq_PlanForest_tent;
                hq_PlanForest_sel8 = 0;
                if (tent < 0) hq_PlanForest_sel8 = 1;
                hq_PlanForest_tent = tent;
            }
        }
        if (planPhase == 19 && PhLoop(19, 100000))
        {
            if (!(hq_PlanForest_sel2 == 1 && hq_PlanForest_sel8 == 1)) PhBreak();
            else
            {
                int hubX = hq_PlanForest_hubX;
                int hubZ = hq_PlanForest_hubZ;
                int tent = hq_PlanForest_tent;
                GenRng r2 = hq_PlanForest_r2;
                if (planIter == 0) { hq_WallKeep_def = Dn("s_bedroll"); hq_WallKeep_mode = WM_NEAR; hq_WallKeep_tx = hubX; hq_WallKeep_tz = hubZ; hq_WallKeep_win = WIN_ANY; hq_WallKeep_flags = EF_MANDATORY; hq_WallKeep_rng = r2; PhSubReset(); }
                PhPush(); WallKeep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r2 = hq_WallKeep_rng; tent = hq_WallKeep_ret; PhBreak(); }
                hq_PlanForest_hubX = hubX;
                hq_PlanForest_hubZ = hubZ;
                hq_PlanForest_tent = tent;
                hq_PlanForest_r2 = r2;
            }
        }
        if (planPhase == 20)
        {
            if (hq_PlanForest_sel2 == 1)
            {
                int tent = hq_PlanForest_tent;
                if (tent < 0) MandFail(1);
                hq_PlanForest_tent = tent;
            }
        }
        if (planPhase == 21 && PhLoop(21, 100000))
        {
            if (!(hq_PlanForest_sel2 == 1)) PhBreak();
            else
            {
                int fire = hq_PlanForest_fire;
                if (planIter == 0) { hq_FireSeats_fire = fire; hq_FireSeats_wood = true; hq_FireSeats_k = 8; PhSubReset(); }
                PhPush(); FireSeats_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanForest_fire = fire;
            }
        }
        if (planPhase == 22)
        {
            if (hq_PlanForest_sel2 == 1)
            {
                GenRng rgear = SR(9);
                hq_PlanForest_rgear = rgear;
            }
        }
        if (planPhase == 23 && PhLoop(23, 100000))
        {
            if (!(hq_PlanForest_sel2 == 1)) PhBreak();
            else
            {
                int fire = hq_PlanForest_fire;
                GenRng rgear = hq_PlanForest_rgear;
                if (planIter == 0) { hq_FireGear_fire = fire; hq_FireGear_rng = rgear; PhSubReset(); }
                PhPush(); FireGear_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { rgear = hq_FireGear_rng; PhBreak(); }
                hq_PlanForest_fire = fire;
                hq_PlanForest_rgear = rgear;
            }
        }
        if (planPhase == 24)
        {
            int hub = hq_PlanForest_hub;
            int fire = hq_PlanForest_fire;
            // a piece of forest of one cell (4 x 4 m) is too small for a path of its own
            if (camp || roomOuterCells[room] >= 2)
                for (int g = 0; g < nGate; g++) { GenRng rg = SR(20 + g); Track(room, gateCell[g], hub, ref rg); }
            hq_PlanForest_sel9 = 0;
            if (camp && fire >= 0) hq_PlanForest_sel9 = 1;
            hq_PlanForest_hub = hub;
            hq_PlanForest_fire = fire;
        }
        if (planPhase == 25)
        {
            if (hq_PlanForest_sel9 == 1)
            {
                int tent = hq_PlanForest_tent;
                GenRng r3 = SR(3);
                hq_PlanForest_sel10 = 0;
                if (tent >= 0 && Fit(2)) hq_PlanForest_sel10 = 1;
                hq_PlanForest_tent = tent;
                hq_PlanForest_r3 = r3;
            }
        }
        if (planPhase == 26)
        {
            if (hq_PlanForest_sel9 == 1 && hq_PlanForest_sel10 == 1)
            {
                int tent = hq_PlanForest_tent;
                GenRng r3 = hq_PlanForest_r3;
                // the gear beside the tent: a box or a chest with a barrel
                int first = nEmit;
                FSet(Pick(ref r3, "s_box", 5, "s_chest", 4), Dn("s_barrel"));
                hq_PlanForest_sel11 = 0;
                if (FormBeside(GenData.GL_ROW, 80, tent, 0, ref r3) >= 0) hq_PlanForest_sel11 = 1;
                hq_PlanForest_tent = tent;
                hq_PlanForest_r3 = r3;
                hq_PlanForest_first = first;
            }
        }
        if (planPhase == 27 && PhLoop(27, 100000))
        {
            if (!(hq_PlanForest_sel9 == 1 && hq_PlanForest_sel10 == 1 && hq_PlanForest_sel11 == 1)) PhBreak();
            else
            {
                int first = hq_PlanForest_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanForest_first = first;
            }
        }
        if (planPhase == 28)
        {
            if (hq_PlanForest_sel9 == 1)
            {
                int tent = hq_PlanForest_tent;
                GenRng r3 = hq_PlanForest_r3;
                hq_PlanForest_sel12 = 0;
                if (tent >= 0 && r3.Chance(600) && Fit(2)) hq_PlanForest_sel12 = 1;
                hq_PlanForest_tent = tent;
                hq_PlanForest_r3 = r3;
            }
        }
        if (planPhase == 29)
        {
            if (hq_PlanForest_sel9 == 1 && hq_PlanForest_sel12 == 1)
            {
                int tent = hq_PlanForest_tent;
                GenRng r3 = hq_PlanForest_r3;
                int first = nEmit;
                hq_PlanForest_sel13 = 0;
                if (GroupBeside(GenData.FindGrp("tools"), tent, 2, 0, ref r3)) hq_PlanForest_sel13 = 1;
                hq_PlanForest_tent = tent;
                hq_PlanForest_r3 = r3;
                hq_PlanForest_first = first;
            }
        }
        if (planPhase == 30 && PhLoop(30, 100000))
        {
            if (!(hq_PlanForest_sel9 == 1 && hq_PlanForest_sel12 == 1 && hq_PlanForest_sel13 == 1)) PhBreak();
            else
            {
                int first = hq_PlanForest_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanForest_first = first;
            }
        }
        if (planPhase == 31)
        {
            if (hq_PlanForest_sel9 == 1)
            {
                GenRng r4 = SR(4);
                hq_PlanForest_sel14 = 0;
                if (r4.Chance(800)) hq_PlanForest_sel14 = 1;
                hq_PlanForest_r4 = r4;
            }
        }
        if (planPhase == 32 && PhLoop(32, 100000))
        {
            if (!(hq_PlanForest_sel9 == 1 && hq_PlanForest_sel14 == 1)) PhBreak();
            else
            {
                int fire = hq_PlanForest_fire;
                GenRng r4 = hq_PlanForest_r4;
                if (planIter == 0) { hq_WoodPile_fire = fire; hq_WoodPile_atEdge = false; hq_WoodPile_rng = r4; PhSubReset(); }
                PhPush(); WoodPile_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r4 = hq_WoodPile_rng; PhBreak(); }
                hq_PlanForest_fire = fire;
                hq_PlanForest_r4 = r4;
            }
        }
        if (planPhase == 33)
        {
            int hubX = hq_PlanForest_hubX;
            int hubZ = hq_PlanForest_hubZ;
            planHX = hubX; planHZ = hubZ;
            hq_PlanForest_hubX = hubX;
            hq_PlanForest_hubZ = hubZ;
        }
        if (planPhase == 34)
        {
            // (b) trees in stands, away from the path and the clearing; open ground between the stands
            GenRng rt = SR(5);
            int trees = TreeCount(room, ref rt);
            if (trees < 3) trees = 3;
            int stands = (trees + 3) / 4;
            if (stands > standX.Length) stands = standX.Length;
            standN = 0;
            int main0 = Pick(ref rt, "n_treeDefault", 6, "n_treePineTall", 6, "n_treeOak", 5);
            int left = trees;
            hq_PlanForest_rt = rt;
            hq_PlanForest_stands = stands;
            hq_PlanForest_main0 = main0;
            hq_PlanForest_left = left;
        }
        if (planPhase == 35 && PhLoop(35, 100000))
        {
            int stands = hq_PlanForest_stands;
            int left = hq_PlanForest_left;
            if (!PhSubActive())
            {
                int s = planIter;
                if (!(s < stands && left > 0)) PhBreak();
                else { hq_PlanForest_s = s; PhSubBegin(); }
            }
            hq_PlanForest_stands = stands;
            hq_PlanForest_left = left;
            if (PhSubActive())
            {
                PhPush(); PlanForest_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        // crowns have to stay inside the fence (G31), so a stand near an edge comes out short: the trees still
        // missing join the stands that stand, wherever a crown has the room
        if (planPhase == 36 && PhLoop(36, 100000))
        {
            GenRng rt = hq_PlanForest_rt;
            int main0 = hq_PlanForest_main0;
            int left = hq_PlanForest_left;
            if (planIter == 0) hq_PlanForest_tries0 = left * 12;
            int tries = hq_PlanForest_tries0 - planIter;
            if (!(tries > 0 && left > 0 && standN > 0)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                int s = rt.Range(standN), a = rt.Range(360), rad = 1200 + rt.Range(2400);
                if (TreeAt(main0, standX[s] + rad * sinTab[a] / 4096, standZ[s] + rad * sinTab[(a + 90) % 360] / 4096, ref rt) >= 0) left--;
            }
            hq_PlanForest_rt = rt;
            hq_PlanForest_main0 = main0;
            hq_PlanForest_left = left;
        }
        // (c) undergrowth: bushes at the edge of a stand and along the fence
        if (planPhase == 37)
        {
            GenRng r6 = SR(6);
            int bushes = 1 + area / 28;
            hq_PlanForest_r6 = r6;
            hq_PlanForest_bushes = bushes;
        }
        if (planPhase == 38 && PhLoop(38, 100000))
        {
            int bushes = hq_PlanForest_bushes;
            if (!PhSubActive())
            {
                int b = planIter;
                if (!(b < bushes && Fit(1))) PhBreak();
                else { hq_PlanForest_b = b; PhSubBegin(); }
            }
            hq_PlanForest_bushes = bushes;
            if (PhSubActive())
            {
                PhPush(); PlanForest_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 39)
        {
            GenRng r7 = SR(7);
            hq_PlanForest_sel16 = 0;
            if (r7.Chance(camp ? 350 : 800) && Fit(2)) hq_PlanForest_sel16 = 1;
            hq_PlanForest_r7 = r7;
        }
        if (planPhase == 40 && PhLoop(40, 100000))
        {
            if (!(hq_PlanForest_sel16 == 1)) PhBreak();
            else
            {
                GenRng r7 = hq_PlanForest_r7;
                if (planIter == 0) { hq_DeadWood_rng = r7; PhSubReset(); }
                PhPush(); DeadWood_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r7 = hq_DeadWood_rng; PhBreak(); }
                hq_PlanForest_r7 = r7;
            }
        }
        if (planPhase == 41)
        {
            GenRng r8 = SR(8);
            hq_PlanForest_sel17 = 0;
            if (r8.Chance(camp ? 300 : 600) && Fit(2)) hq_PlanForest_sel17 = 1;
            hq_PlanForest_r8 = r8;
        }
        if (planPhase == 42)
        {
            if (hq_PlanForest_sel17 == 1)
            {
                GenRng r8 = hq_PlanForest_r8;
                int cell = SoftSpot(ref r8);
                int first = nEmit;
                hq_PlanForest_r8 = r8;
                hq_PlanForest_cell = cell;
                hq_PlanForest_first = first;
            }
        }
        if (planPhase == 43)
        {
            if (hq_PlanForest_sel17 == 1)
            {
                GenRng r8 = hq_PlanForest_r8;
                int cell = hq_PlanForest_cell;
                int rock = cell >= 0 ? FreePlace(Pick(ref r8, "n_rockLarge", 5, "n_rockTall", 3), cell, r8.Range(360), 0, 0, H_NATURE) : -1;
                hq_PlanForest_r8 = r8;
                hq_PlanForest_cell = cell;
                hq_PlanForest_rock = rock;
            }
        }
        if (planPhase == 44)
        {
            if (hq_PlanForest_sel17 == 1)
            {
                int first = hq_PlanForest_first;
                int rock = hq_PlanForest_rock;
                hq_PlanForest_sel18 = 0;
                if (rock >= 0 && Keep(first, false) && Fit(2)) hq_PlanForest_sel18 = 1;
                hq_PlanForest_first = first;
                hq_PlanForest_rock = rock;
            }
        }
        if (planPhase == 45)
        {
            if (hq_PlanForest_sel17 == 1 && hq_PlanForest_sel18 == 1)
            {
                GenRng r8 = hq_PlanForest_r8;
                int first = hq_PlanForest_first;
                int rock = hq_PlanForest_rock;
                first = nEmit;
                hq_PlanForest_sel19 = 0;
                if (GroupBeside(GenData.FindGrp("rocks"), rock, r8.Between(2, 3), 0, ref r8)) hq_PlanForest_sel19 = 1;
                hq_PlanForest_r8 = r8;
                hq_PlanForest_first = first;
                hq_PlanForest_rock = rock;
            }
        }
        if (planPhase == 46 && PhLoop(46, 100000))
        {
            if (!(hq_PlanForest_sel17 == 1 && hq_PlanForest_sel18 == 1 && hq_PlanForest_sel19 == 1)) PhBreak();
            else
            {
                int first = hq_PlanForest_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanForest_first = first;
            }
        }
        if (planPhase == 47)
        {
            GenRng r9 = SR(9);
            int clumps = camp ? 1 : 1 + area / 50;
            hq_PlanForest_r9 = r9;
            hq_PlanForest_clumps = clumps;
        }
        if (planPhase == 48 && PhLoop(48, 100000))
        {
            int clumps = hq_PlanForest_clumps;
            if (!PhSubActive())
            {
                int m = planIter;
                if (!(m < clumps && Fit(2))) PhBreak();
                else { hq_PlanForest_m = m; PhSubBegin(); }
            }
            hq_PlanForest_clumps = clumps;
            if (PhSubActive())
            {
                PhPush(); PlanForest_L3(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 49)
        {
            GenRng r10 = SR(10);
            hq_PlanForest_sel21 = 0;
            if (r10.Chance(600) && Fit(3)) hq_PlanForest_sel21 = 1;
            hq_PlanForest_r10 = r10;
        }
        if (planPhase == 50)
        {
            if (hq_PlanForest_sel21 == 1)
            {
                GenRng r10 = hq_PlanForest_r10;
                int first = nEmit;
                hq_PlanForest_sel22 = 0;
                if (GroupAt(FlowerGroup(ref r10), GenData.A_SOFT, r10.Between(3, Fit(5) ? 5 : 3), WM_NEAR, planHX, planHZ, 0, ref r10)) hq_PlanForest_sel22 = 1;
                hq_PlanForest_r10 = r10;
                hq_PlanForest_first = first;
            }
        }
        if (planPhase == 51 && PhLoop(51, 100000))
        {
            if (!(hq_PlanForest_sel21 == 1 && hq_PlanForest_sel22 == 1)) PhBreak();
            else
            {
                int first = hq_PlanForest_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanForest_first = first;
            }
        }
        if (planPhase == 52)
        {
            GenRng r11 = SR(11);
            hq_PlanForest_sel23 = 0;
            if (nGate > 0 && r11.Chance(400) && Fit(1)) hq_PlanForest_sel23 = 1;
            hq_PlanForest_r11 = r11;
        }
        if (planPhase == 53 && PhLoop(53, 100000))
        {
            if (!(hq_PlanForest_sel23 == 1)) PhBreak();
            else
            {
                GenRng r11 = hq_PlanForest_r11;
                if (planIter == 0) { hq_Signpost_gate = gateCell[0]; hq_Signpost_rng = r11; PhSubReset(); }
                PhPush(); Signpost_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r11 = hq_Signpost_rng; PhBreak(); }
                hq_PlanForest_r11 = r11;
            }
        }
        if (planPhase == 54)
        {
            GenRng r12 = SR(12);
            hq_PlanForest_r12 = r12;
        }
        if (planPhase == 55 && PhLoop(55, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 10 && Fit(1))) PhBreak();
                else { hq_PlanForest_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanForest_L4(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 56)
        {
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
    }
    void PlanForest_L1()
    {
        bool camp = hq_PlanForest_camp;
        int room = pRoom, area = AreaM2(room);
        planMax = 2;
        if (planPhase == 0)
        {
            GenRng rt = hq_PlanForest_rt;
            int main0 = hq_PlanForest_main0;
            int left = hq_PlanForest_left;
            int s = hq_PlanForest_s;
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
            if (best < 0) { hq_PlanForest_rt = rt; hq_PlanForest_main0 = main0; hq_PlanForest_left = left; phBreak = true; PhEnd(); return; }
            int sx = best % IW * GenData.INNER_MM + 250, sz = best / IW * GenData.INNER_MM + 250;
            int kind = s == 0 ? main0 : (rt.Chance(190) ? Dn("n_treePineRound") : Pick(ref rt, "n_treeDefault", 6, "n_treePineTall", 6, "n_treeOak", 5));
            int accent = rt.Chance(400) ? Pick(ref rt, "n_treePineRound", 4, "n_treeOak", 4, "n_treeDefault", 4) : -1;
            int want = left < 3 ? left : rt.Between(3, left < 5 ? left : 5);
            hq_PlanForest_rt = rt;
            hq_PlanForest_main0 = main0;
            hq_PlanForest_left = left;
            hq_PlanForest_sx = sx;
            hq_PlanForest_sz = sz;
            hq_PlanForest_kind = kind;
            hq_PlanForest_accent = accent;
            hq_PlanForest_want = want;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            GenRng rt = hq_PlanForest_rt;
            int sx = hq_PlanForest_sx;
            int sz = hq_PlanForest_sz;
            int kind = hq_PlanForest_kind;
            int accent = hq_PlanForest_accent;
            int want = hq_PlanForest_want;
            int got = 0;
            if (planIter == 0) { hq_Stand_cx = sx; hq_Stand_cz = sz; hq_Stand_n = want; hq_Stand_main = kind; hq_Stand_accent = accent; hq_Stand_rng = rt; PhSubReset(); }
            PhPush(); Stand_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { rt = hq_Stand_rng; got = hq_Stand_ret; PhBreak(); }
            hq_PlanForest_rt = rt;
            hq_PlanForest_sx = sx;
            hq_PlanForest_sz = sz;
            hq_PlanForest_kind = kind;
            hq_PlanForest_accent = accent;
            hq_PlanForest_want = want;
            hq_PlanForest_got = got;
        }
        if (planPhase == 2)
        {
            int left = hq_PlanForest_left;
            int sx = hq_PlanForest_sx;
            int sz = hq_PlanForest_sz;
            int got = hq_PlanForest_got;
            left -= got;
            if (got > 0 && standN < standX.Length) { standX[standN] = sx; standZ[standN] = sz; standN++; }
            hq_PlanForest_left = left;
            hq_PlanForest_sx = sx;
            hq_PlanForest_sz = sz;
            hq_PlanForest_got = got;
        }
    }

    void PlanForest_L2()
    {
        bool camp = hq_PlanForest_camp;
        int room = pRoom, area = AreaM2(room);
        planMax = 1;
        if (planPhase == 0)
        {
            GenRng r6 = hq_PlanForest_r6;
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
            hq_PlanForest_sel15 = 0;
            if (ok) hq_PlanForest_sel15 = 1;
            hq_PlanForest_r6 = r6;
            hq_PlanForest_first = first;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!(hq_PlanForest_sel15 == 1)) PhBreak();
            else
            {
                int first = hq_PlanForest_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanForest_first = first;
            }
        }
    }

    void PlanForest_L3()
    {
        bool camp = hq_PlanForest_camp;
        int room = pRoom, area = AreaM2(room);
        planMax = 1;
        if (planPhase == 0)
        {
            GenRng r9 = hq_PlanForest_r9;
            int first = nEmit;
            hq_PlanForest_sel20 = 0;
            if (PlaceGroup(GenData.FindGrp("mushrooms"), room, GenData.A_BESIDE, r9.Between(2, Fit(4) ? 4 : 2), 0, ref r9)) hq_PlanForest_sel20 = 1;
            hq_PlanForest_r9 = r9;
            hq_PlanForest_first = first;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!(hq_PlanForest_sel20 == 1)) PhBreak();
            else
            {
                int first = hq_PlanForest_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanForest_first = first;
            }
        }
    }

    void PlanForest_L4()
    {
        bool camp = hq_PlanForest_camp;
        int room = pRoom, area = AreaM2(room);
        planMax = 1;
        if (planPhase == 0)
        {
            GenRng r12 = hq_PlanForest_r12;
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
            hq_PlanForest_sel24 = 0;
            if (ok) hq_PlanForest_sel24 = 1;
            hq_PlanForest_r12 = r12;
            hq_PlanForest_first = first;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!(hq_PlanForest_sel24 == 1)) PhBreak();
            else
            {
                int first = hq_PlanForest_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanForest_first = first;
            }
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
        hq_Signpost_gate = gate;
        hq_Signpost_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); Signpost_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_Signpost_rng;
    }

    // Signpost in phases, a step a call (autophase4); Signpost() above runs them to the end
    void Signpost_Body()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int gate = hq_Signpost_gate;
            GenRng rng = hq_Signpost_rng;
            int room = pRoom, def = Dn("s_signpost");
            int gx = gate % IW, gz = gate / IW, a0 = rng.Range(4);
            hq_Signpost_gate = gate;
            hq_Signpost_rng = rng;
            hq_Signpost_room = room;
            hq_Signpost_def = def;
            hq_Signpost_gx = gx;
            hq_Signpost_gz = gz;
            hq_Signpost_a0 = a0;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!PhSubActive())
            {
                int dist = 1 + planIter;
                if (!(dist <= 4)) PhBreak();
                else { hq_Signpost_dist = dist; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); Signpost_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
    }
    void Signpost_L4()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int room = hq_Signpost_room;
            int def = hq_Signpost_def;
            int a0 = hq_Signpost_a0;
            int x = hq_Signpost_x;
            int z = hq_Signpost_z;
            int k = hq_Signpost_k;
            int d = (a0 + k) & 3, px = x + DX[d], pz = z + DZ[d];
            if (px < 0 || pz < 0 || px >= IW || pz >= IH || (tags[pz * IW + px] & GenData.T_PATH) == 0 || !CanStep(x, z, d)) { hq_Signpost_room = room; hq_Signpost_def = def; hq_Signpost_a0 = a0; hq_Signpost_x = x; hq_Signpost_z = z; PhEnd(); return; }
            int first = nEmit;
            int e = StampAt(def, room, x * GenData.INNER_MM + 250, z * GenData.INNER_MM + 250, DirYaw(d), 0, -1, H_GROUND, false);
            hq_Signpost_room = room;
            hq_Signpost_def = def;
            hq_Signpost_a0 = a0;
            hq_Signpost_x = x;
            hq_Signpost_z = z;
            hq_Signpost_first = first;
            hq_Signpost_e = e;
        }
        if (planPhase == 1)
        {
            int room = hq_Signpost_room;
            int first = hq_Signpost_first;
            int e = hq_Signpost_e;
            if (e >= 0 && KeepIfWalkable(room, first, false))
            {
                { hq_Signpost_room = room; hq_Signpost_first = first; hq_Signpost_e = e; phReturn = true; PhEnd(); return; }
            }
            hq_Signpost_room = room;
            hq_Signpost_first = first;
            hq_Signpost_e = e;
        }
    }

    void Signpost_L3()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int room = hq_Signpost_room;
            int gx = hq_Signpost_gx;
            int gz = hq_Signpost_gz;
            int dist = hq_Signpost_dist;
            int dz = hq_Signpost_dz;
            int dx = hq_Signpost_dx;
            if (Abs(dx) != dist && Abs(dz) != dist) { hq_Signpost_room = room; hq_Signpost_gx = gx; hq_Signpost_gz = gz; PhEnd(); return; }
            int x = gx + dx, z = gz + dz;
            if (x < 0 || z < 0 || x >= IW || z >= IH) { hq_Signpost_room = room; hq_Signpost_gx = gx; hq_Signpost_gz = gz; hq_Signpost_x = x; hq_Signpost_z = z; PhEnd(); return; }
            int c = z * IW + x;
            if (innerRoom[c] != room || (tags[c] & GenData.T_PATH) != 0) { hq_Signpost_room = room; hq_Signpost_gx = gx; hq_Signpost_gz = gz; hq_Signpost_x = x; hq_Signpost_z = z; PhEnd(); return; }
            hq_Signpost_room = room;
            hq_Signpost_gx = gx;
            hq_Signpost_gz = gz;
            hq_Signpost_x = x;
            hq_Signpost_z = z;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 4)) PhBreak();
                else { hq_Signpost_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); Signpost_L4(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { PhEnd(); return; }
            }
        }
    }

    void Signpost_L2()
    {
        planMax = 0;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int dist = hq_Signpost_dist;
            if (!PhSubActive())
            {
                if (planIter == 0) hq_Signpost_dx0 = -dist;
                int dx = hq_Signpost_dx0 + planIter;
                if (!(dx <= dist)) PhBreak();
                else { hq_Signpost_dx = dx; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); Signpost_L3(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { PhEnd(); return; }
            }
        }
    }

    void Signpost_L1()
    {
        planMax = 0;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int dist = hq_Signpost_dist;
            if (!PhSubActive())
            {
                if (planIter == 0) hq_Signpost_dz0 = -dist;
                int dz = hq_Signpost_dz0 + planIter;
                if (!(dz <= dist)) PhBreak();
                else { hq_Signpost_dz = dz; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); Signpost_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { PhEnd(); return; }
            }
        }
    }


    // the yaw (degrees) whose front looks along direction d (0 north +z, 1 east +x, 2 south, 3 west)
    static int DirYaw(int d) { return d == 0 ? 0 : (d == 1 ? 90 : (d == 2 ? 180 : 270)); }

    // a fallen log with a stump beside it and mushrooms at the stump (report 15, F-log and fungi, F-felled spot)
    void DeadWood(ref GenRng rng)
    {
        hq_DeadWood_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); DeadWood_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_DeadWood_rng;
    }

    // DeadWood in phases, a step a call (autophase4); DeadWood() above runs them to the end
    void DeadWood_Body()
    {
        planMax = 7;
        if (planPhase == 0)
        {
            GenRng rng = hq_DeadWood_rng;
            int cell = SoftSpot(ref rng);
            if (cell < 0) { hq_DeadWood_rng = rng; hq_DeadWood_cell = cell; PhEnd(); return; }
            int first = nEmit;
            hq_DeadWood_rng = rng;
            hq_DeadWood_cell = cell;
            hq_DeadWood_first = first;
        }
        if (planPhase == 1)
        {
            GenRng rng = hq_DeadWood_rng;
            int cell = hq_DeadWood_cell;
            int logDef = Pick(ref rng, "n_log", 5, "n_logLarge", 3, "s_treeLog", 3);
            int log = FreePlace(logDef, cell, rng.Range(360), 0, 0, H_NATURE);
            hq_DeadWood_rng = rng;
            hq_DeadWood_cell = cell;
            hq_DeadWood_logDef = logDef;
            hq_DeadWood_log = log;
        }
        if (planPhase == 2)
        {
            int first = hq_DeadWood_first;
            int log = hq_DeadWood_log;
            if (log < 0 || !Keep(first, false))
            {
                { hq_DeadWood_first = first; hq_DeadWood_log = log; PhEnd(); return; }
            }
            int stump = -1;
            hq_DeadWood_sel1 = 0;
            if (Fit(1)) hq_DeadWood_sel1 = 1;
            hq_DeadWood_first = first;
            hq_DeadWood_log = log;
            hq_DeadWood_stump = stump;
        }
        if (planPhase == 3)
        {
            if (hq_DeadWood_sel1 == 1)
            {
                GenRng rng = hq_DeadWood_rng;
                int first = hq_DeadWood_first;
                int logDef = hq_DeadWood_logDef;
                int log = hq_DeadWood_log;
                int stump = hq_DeadWood_stump;
                first = nEmit;
                int sd = Pick(ref rng, "n_stump", 6, "n_stumpRound", 4);
                int side = rng.Chance(500) ? 1 : -1;
                stump = Rel(sd, log, side * (Sx(logDef) / 2 + 350 + Sx(sd) / 2), rng.Between(-400, 400), rng.Range(360), 0);
                if (stump < 0) stump = Rel(sd, log, -side * (Sx(logDef) / 2 + 350 + Sx(sd) / 2), 0, rng.Range(360), 0);
                hq_DeadWood_rng = rng;
                hq_DeadWood_first = first;
                hq_DeadWood_logDef = logDef;
                hq_DeadWood_log = log;
                hq_DeadWood_stump = stump;
            }
        }
        if (planPhase == 4)
        {
            if (hq_DeadWood_sel1 == 1)
            {
                int first = hq_DeadWood_first;
                int stump = hq_DeadWood_stump;
                if (stump >= 0 && !Keep(first, false))
                {
                    stump = -1;
                }
                hq_DeadWood_first = first;
                hq_DeadWood_stump = stump;
            }
        }
        if (planPhase == 5)
        {
            hq_DeadWood_sel2 = 0;
            if (Fit(2)) hq_DeadWood_sel2 = 1;
        }
        if (planPhase == 6)
        {
            if (hq_DeadWood_sel2 == 1)
            {
                GenRng rng = hq_DeadWood_rng;
                int first = hq_DeadWood_first;
                int log = hq_DeadWood_log;
                int stump = hq_DeadWood_stump;
                first = nEmit;
                hq_DeadWood_sel3 = 0;
                if (GroupBeside(GenData.FindGrp("mushrooms"), stump >= 0 ? stump : log, rng.Between(2, 3), 0, ref rng)) hq_DeadWood_sel3 = 1;
                hq_DeadWood_rng = rng;
                hq_DeadWood_first = first;
                hq_DeadWood_log = log;
                hq_DeadWood_stump = stump;
            }
        }
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            if (!(hq_DeadWood_sel2 == 1 && hq_DeadWood_sel3 == 1)) PhBreak();
            else
            {
                int first = hq_DeadWood_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_DeadWood_first = first;
            }
        }
    }

    // locals of the split plans and helpers, kept from one phase to the next (autophase4)
    int hq_AroundFire_a;
    int hq_AroundFire_a0;
    int hq_AroundFire_def;
    int hq_AroundFire_dist;
    int hq_AroundFire_dist0;
    int hq_AroundFire_distMax;
    int hq_AroundFire_distMin;
    int hq_AroundFire_e;
    int hq_AroundFire_fire;
    int hq_AroundFire_first;
    int hq_AroundFire_flags;
    int hq_AroundFire_fx;
    int hq_AroundFire_fz;
    int hq_AroundFire_k;
    bool hq_AroundFire_must;
    bool hq_AroundFire_offGates;
    int hq_AroundFire_relYaw;
    int hq_AroundFire_ret;
    GenRng hq_AroundFire_rng;
    int hq_AroundFire_room;
    int hq_BenchAtEdge_bench;
    int hq_BenchAtEdge_first;
    int hq_BenchAtEdge_k;
    int hq_BenchAtEdge_pot;
    GenRng hq_BenchAtEdge_r;
    int hq_BenchAtEdge_sel1;
    int hq_BenchAtEdge_sel2;
    int hq_Border_added;
    int hq_Border_back;
    int hq_Border_before;
    int hq_Border_cursor;
    int hq_Border_d;
    int hq_Border_def;
    int hq_Border_first;
    int hq_Border_g;
    int hq_Border_halfAlong;
    int hq_Border_kind;
    int hq_Border_last;
    int hq_Border_len;
    int hq_Border_maxLen;
    int hq_Border_mode;
    int hq_Border_px;
    int hq_Border_pz;
    int hq_Border_ret;
    GenRng hq_Border_rng;
    int hq_Border_room;
    int hq_Border_rot;
    int hq_Border_sel1;
    bool hq_Border_soil;
    int hq_Border_start;
    int hq_Border_tx;
    int hq_Border_tz;
    int hq_Border_yaw;
    int hq_Borders_b;
    int hq_Borders_ht1;
    int hq_Borders_k0;
    int hq_Borders_longest;
    int hq_Borders_max;
    GenRng hq_Borders_r;
    bool hq_Borders_soil;
    int hq_DeadWood_cell;
    int hq_DeadWood_first;
    int hq_DeadWood_log;
    int hq_DeadWood_logDef;
    GenRng hq_DeadWood_rng;
    int hq_DeadWood_sel1;
    int hq_DeadWood_sel2;
    int hq_DeadWood_sel3;
    int hq_DeadWood_stump;
    int hq_FillOne_cell;
    int hq_FillOne_first;
    bool hq_FillOne_forest;
    int hq_FillOne_g;
    int hq_FillOne_how;
    int hq_FillOne_k;
    int hq_FillOne_pass;
    GenRng hq_FillOne_r;
    int hq_FillOne_room;
    int hq_FillOne_sel1;
    int hq_FillOne_sel2;
    int hq_FillOne_sel3;
    int hq_FillOne_sel4;
    int hq_FillOne_sel5;
    int hq_FillOne_sel6;
    int hq_FillOne_type;
    int hq_FireGear_fire;
    int hq_FireGear_first;
    GenRng hq_FireGear_rng;
    int hq_FireGear_sel1;
    int hq_FireGear_sel2;
    int hq_FireGear_sel3;
    int hq_FireGear_sel4;
    int hq_FireRing_fire;
    int hq_FireRing_minClr;
    int hq_FireRing_ret;
    GenRng hq_FireRing_rng;
    int hq_FireRing_room;
    int hq_FireRing_sel1;
    bool hq_FireRing_soft;
    int hq_FireRing_tx;
    int hq_FireRing_tz;
    int hq_FireSeats_def;
    int hq_FireSeats_fire;
    int hq_FireSeats_ht1;
    int hq_FireSeats_k;
    int hq_FireSeats_n;
    int hq_FireSeats_rel;
    GenRng hq_FireSeats_rng;
    int hq_FireSeats_s;
    int hq_FireSeats_tries;
    bool hq_FireSeats_wood;
    int hq_FrontMarkers_c;
    int hq_FrontMarkers_cell;
    int hq_FrontMarkers_cx;
    int hq_FrontMarkers_cz;
    int hq_FrontMarkers_k0;
    bool hq_FrontMarkers_lane;
    int hq_FrontMarkers_n;
    int hq_FrontMarkers_q;
    int hq_FrontMarkers_room;
    int hq_FrontMarkers_zone;
    int hq_Island_bush;
    int hq_Island_cell;
    int hq_Island_first;
    bool hq_Island_ret;
    GenRng hq_Island_rng;
    int hq_Island_sel1;
    int hq_Island_sel2;
    int hq_LawnTree_cell;
    int hq_LawnTree_first;
    int hq_LawnTree_k;
    GenRng hq_LawnTree_r;
    int hq_LawnTree_ret;
    int hq_LawnTree_sel1;
    int hq_LawnTree_sel2;
    int hq_LawnTree_tree;
    int hq_PlanFirepit_fire;
    bool hq_PlanFirepit_ht1;
    int hq_PlanFirepit_k;
    GenRng hq_PlanFirepit_r;
    GenRng hq_PlanFirepit_r2;
    GenRng hq_PlanFirepit_r3;
    GenRng hq_PlanFirepit_r5;
    int hq_PlanFirepit_sel1;
    int hq_PlanFirepit_sel2;
    int hq_PlanFirepit_t;
    int hq_PlanFirepit_trees;
    int hq_PlanForest_accent;
    int hq_PlanForest_b;
    int hq_PlanForest_bushes;
    bool hq_PlanForest_camp;
    int hq_PlanForest_cell;
    int hq_PlanForest_clumps;
    int hq_PlanForest_fire;
    int hq_PlanForest_first;
    int hq_PlanForest_got;
    int hq_PlanForest_hub;
    int hq_PlanForest_hubX;
    int hq_PlanForest_hubZ;
    int hq_PlanForest_k;
    int hq_PlanForest_kind;
    int hq_PlanForest_left;
    int hq_PlanForest_m;
    int hq_PlanForest_main0;
    GenRng hq_PlanForest_r;
    GenRng hq_PlanForest_r10;
    GenRng hq_PlanForest_r11;
    GenRng hq_PlanForest_r12;
    GenRng hq_PlanForest_r2;
    GenRng hq_PlanForest_r3;
    GenRng hq_PlanForest_r4;
    GenRng hq_PlanForest_r6;
    GenRng hq_PlanForest_r7;
    GenRng hq_PlanForest_r8;
    GenRng hq_PlanForest_r9;
    GenRng hq_PlanForest_rgear;
    int hq_PlanForest_rock;
    GenRng hq_PlanForest_rt;
    int hq_PlanForest_s;
    int hq_PlanForest_sel1;
    int hq_PlanForest_sel10;
    int hq_PlanForest_sel11;
    int hq_PlanForest_sel12;
    int hq_PlanForest_sel13;
    int hq_PlanForest_sel14;
    int hq_PlanForest_sel15;
    int hq_PlanForest_sel16;
    int hq_PlanForest_sel17;
    int hq_PlanForest_sel18;
    int hq_PlanForest_sel19;
    int hq_PlanForest_sel2;
    int hq_PlanForest_sel20;
    int hq_PlanForest_sel21;
    int hq_PlanForest_sel22;
    int hq_PlanForest_sel23;
    int hq_PlanForest_sel24;
    int hq_PlanForest_sel3;
    int hq_PlanForest_sel4;
    int hq_PlanForest_sel5;
    int hq_PlanForest_sel6;
    int hq_PlanForest_sel7;
    int hq_PlanForest_sel8;
    int hq_PlanForest_sel9;
    int hq_PlanForest_stands;
    int hq_PlanForest_sx;
    int hq_PlanForest_sz;
    int hq_PlanForest_tent;
    int hq_PlanForest_tentDef;
    int hq_PlanForest_tries0;
    int hq_PlanForest_want;
    int hq_PlanGarden_cell;
    int hq_PlanGarden_first;
    bool hq_PlanGarden_ht1;
    int hq_PlanGarden_k;
    bool hq_PlanGarden_ok;
    GenRng hq_PlanGarden_r;
    GenRng hq_PlanGarden_r2;
    GenRng hq_PlanGarden_r3;
    GenRng hq_PlanGarden_r4;
    GenRng hq_PlanGarden_r5;
    GenRng hq_PlanGarden_r7;
    GenRng hq_PlanGarden_r8;
    int hq_PlanGarden_sel1;
    int hq_PlanGarden_sel10;
    int hq_PlanGarden_sel11;
    int hq_PlanGarden_sel12;
    int hq_PlanGarden_sel13;
    int hq_PlanGarden_sel14;
    int hq_PlanGarden_sel15;
    int hq_PlanGarden_sel2;
    int hq_PlanGarden_sel3;
    int hq_PlanGarden_sel4;
    int hq_PlanGarden_sel5;
    int hq_PlanGarden_sel6;
    int hq_PlanGarden_sel7;
    int hq_PlanGarden_sel8;
    int hq_PlanGarden_sel9;
    int hq_PlanGarden_shed;
    int hq_PlanGarden_t;
    int hq_PlanGarden_tap;
    int hq_PlanGarden_trees;
    int hq_PlanLane_first;
    GenRng hq_PlanLane_r;
    GenRng hq_PlanLane_r4;
    int hq_PlanLane_rx;
    int hq_PlanLane_rz;
    int hq_PlanLane_sel1;
    int hq_PlanLane_sel2;
    int hq_PlanLane_sel3;
    int hq_PlanLane_sel4;
    int hq_PlanLane_sel5;
    int hq_PlanLawn_first;
    bool hq_PlanLawn_ht1;
    int hq_PlanLawn_k;
    int hq_PlanLawn_lt;
    GenRng hq_PlanLawn_r3;
    GenRng hq_PlanLawn_r4;
    int hq_PlanLawn_sel1;
    int hq_PlanLawn_sel2;
    int hq_PlanLawn_sel3;
    int hq_PlanPatio_first;
    bool hq_PlanPatio_ht1;
    int hq_PlanPatio_j;
    int hq_PlanPatio_midX;
    int hq_PlanPatio_midZ;
    GenRng hq_PlanPatio_r;
    GenRng hq_PlanPatio_r2;
    GenRng hq_PlanPatio_r4;
    GenRng hq_PlanPatio_r5;
    GenRng hq_PlanPatio_r6;
    int hq_PlanPatio_room;
    int hq_PlanPatio_sel1;
    int hq_PlanPatio_sel2;
    int hq_PlanPatio_sel3;
    int hq_PlanPatio_sel4;
    int hq_PlanPatio_sel5;
    int hq_PlanPatio_sel6;
    int hq_PlanPatio_sel7;
    int hq_PlanPatio_sel8;
    int hq_PlanPatio_set;
    int hq_PlanPatio_setDef;
    int hq_PlanPatio_t;
    int hq_PlanPatio_trees;
    bool hq_PlanYard_diner;
    int hq_PlanYard_dx;
    int hq_PlanYard_dz;
    int hq_PlanYard_first;
    int hq_PlanYard_k;
    GenRng hq_PlanYard_r;
    GenRng hq_PlanYard_r10;
    GenRng hq_PlanYard_r8;
    GenRng hq_PlanYard_r9;
    int hq_PlanYard_room;
    string[] hq_PlanYard_rows;
    int hq_PlanYard_sel1;
    int hq_PlanYard_sel10;
    int hq_PlanYard_sel2;
    int hq_PlanYard_sel3;
    int hq_PlanYard_sel4;
    int hq_PlanYard_sel5;
    int hq_PlanYard_sel6;
    int hq_PlanYard_sel7;
    int hq_PlanYard_sel8;
    int hq_PlanYard_sel9;
    int hq_Signpost_a0;
    int hq_Signpost_def;
    int hq_Signpost_dist;
    int hq_Signpost_dx;
    int hq_Signpost_dx0;
    int hq_Signpost_dz;
    int hq_Signpost_dz0;
    int hq_Signpost_e;
    int hq_Signpost_first;
    int hq_Signpost_gate;
    int hq_Signpost_gx;
    int hq_Signpost_gz;
    int hq_Signpost_k;
    GenRng hq_Signpost_rng;
    int hq_Signpost_room;
    int hq_Signpost_x;
    int hq_Signpost_z;
    int hq_Stand_accent;
    int hq_Stand_cx;
    int hq_Stand_cz;
    int hq_Stand_main;
    int hq_Stand_n;
    int hq_Stand_placed;
    int hq_Stand_ret;
    GenRng hq_Stand_rng;
    int hq_Stand_tries0;
    int hq_StonePaths_k0;
    GenRng hq_StonePaths_rng;
    int hq_StonePaths_room;
    bool hq_VegPlot_alongX;
    int hq_VegPlot_c0;
    int hq_VegPlot_cell;
    string[] hq_VegPlot_crops;
    int hq_VegPlot_first;
    int hq_VegPlot_mx;
    int hq_VegPlot_mz;
    GenRng hq_VegPlot_rng;
    int hq_VegPlot_room;
    int hq_VegPlot_row;
    int hq_VegPlot_sel1;
    bool hq_WoodPile_atEdge;
    int hq_WoodPile_fire;
    int hq_WoodPile_first;
    GenRng hq_WoodPile_rng;
    int hq_WoodPile_sel1;
    int hq_WoodPile_sel2;
    int hq_WoodPile_sel3;
    int hq_WoodPile_sel4;
    int hq_WoodPile_sel5;
    int hq_WoodPile_sel6;
    int hq_WoodPile_stack;
    int hq_WoodPile_stackDef;
}
