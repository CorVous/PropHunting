// MapGen, part 3b: the plans of the house's room types (owner decision G28; rules from research reports 14 and 15).
// MapGenPlansWork.cs holds the warehouse and the diner, MapGenOutdoor.cs the lawn and the forest.
//
// Every plan composes its room in the same order:
//   (a) the activity areas the room has, from its type and floor area
//   (b) the anchor of each area, placed by rule: which wall, which corner, facing what, clear of doors, windows
//       and walkways
//   (c) what belongs with each anchor, placed relative to it and facing it
//   (d) a few small named groups where such a room has them
//   (e) a small, capped remainder of loose things, never alone in the open
// and stops when the room's floor budget is used (G24): empty floor is a correct result. The pieces a room cannot
// do without are placed whatever the budget (EF_MANDATORY). Each step draws from its own sub-stream (SR(k)), so
// a step that is skipped for the budget does not change the steps after it.
// Wrong-category spawns (f) come after all rooms, in MapGenSlots.cs.
public partial class MapGen
{
    public const int EF_BOOTH = 1024;   // a table whose seats are benches placed by the plan: no chairs, settings still appear

    static int Sx(int def) { return GenData.defSx[def]; }
    static int Sz(int def) { return GenData.defSz[def]; }
    int AreaM2(int room) { return roomCellCount[room] / 4; }

    // mm from a host's centre to the wall ahead of it (the wall's surface), or to the end of its room
    int FrontDepth(int host)
    {
        int rot = emitRot[host], room = emitRoom[host];
        int cx = FloorDiv(emitX[host], GenData.INNER_MM), cz = FloorDiv(emitZ[host], GenData.INNER_MM);
        int coord = (rot == 0 || rot == 2) ? emitZ[host] : emitX[host];
        for (int k = 0; k < 80; k++)
        {
            int edge = rot == 0 ? (cz + 1) * GenData.INNER_MM : (rot == 2 ? cz * GenData.INNER_MM : (rot == 1 ? (cx + 1) * GenData.INNER_MM : cx * GenData.INNER_MM));
            if ((tags[cz * IW + cx] & WallBit(rot)) != 0) return Abs(edge - coord) - GenData.WALL_HALF_MM;
            int nx = cx + DX[rot], nz = cz + DZ[rot];
            if (nx < 0 || nz < 0 || nx >= IW || nz >= IH || innerRoom[nz * IW + nx] != room) return Abs(edge - coord);
            cx = nx; cz = nz;
        }
        return 0;
    }

    // distance between two objects along the first one's front axis, mm
    int AlongFront(int a, int b)
    {
        return (emitRot[a] == 0 || emitRot[a] == 2) ? Abs(emitZ[a] - emitZ[b]) : Abs(emitX[a] - emitX[b]);
    }

    // which side of a host (in its own frame: +1 its right, -1 its left) points away from (cx, cz)
    int OutwardSide(int host, int cx, int cz)
    {
        int a = emitYaw[host];
        int sn = sinTab[a], cs = sinTab[(a + 90) % 360];
        long dot = (long)(emitX[host] - cx) * cs - (long)(emitZ[host] - cz) * sn;
        return dot >= 0 ? 1 : -1;
    }

    // the chairs or seats that stand at a host
    int SeatsOf(int host)
    {
        int n = 0;
        for (int e = host + 1; e < nEmit; e++)
            if (emitParent[e] == host && (emitFlags[e] & (EF_ADJACENT | EF_REMOVED)) == EF_ADJACENT) n++;
        return n;
    }

    // The first of a list of pieces that finds a place against a wall (the largest first: a wide cabinet if the
    // wall has the length, else a narrower one). Returns the emit index or -1.
    int WallAny(int mode, int tx, int tz, int win, int flags, ref GenRng rng, string a, string b, string c)
    {
        for (int k = 0; k < 3; k++)
        {
            string id = k == 0 ? a : (k == 1 ? b : c);
            if (id == null) continue;
            int e = WallKeep(Dn(id), mode, tx, tz, win, flags, ref rng);
            if (e >= 0) return e;
        }
        return -1;
    }

    // What a room takes on at its walls and in its corners once its areas stand, while the budget lasts. Each
    // item is a letter and a def id: c = in a corner, m = in the middle of a wall, w = at any wall, d = at the
    // wall near the main door, v = at a window if there is one. Each has its own sub-stream and a chance.
    void Extras(int k0, int chance, params string[] items)
    {
        int room = pRoom, dx, dz;
        MainDoor(room, out dx, out dz);
        for (int i = 0; i < items.Length && Fit(1); i++)
        {
            GenRng r = SR(k0 + i);
            if (!r.Chance(chance)) continue;
            char how = items[i][0];
            int def = Dn(items[i].Substring(1));
            if (CountInRoom(def, room) >= (GenData.defRoomCap[def] < 2 ? GenData.defRoomCap[def] : 2)) continue;
            int first = nEmit;
            if (how == 'c') { if (CornerPlace(def, WM_ANY, 0, 0, false, 0, ref r) >= 0) Keep(first, false); }
            else WallKeep(def, how == 'm' ? WM_CENTRE : (how == 'd' ? WM_NEAR : WM_ANY), dx, dz, how == 'v' ? WIN_PREFER : WIN_AVOID, 0, ref r);
        }
    }

    // a corner piece, a plant or a lamp: the usual end of a plan
    void CornerExtra(int k, int chance, string a, int wa, string b, int wb, bool angled)
    {
        GenRng r = SR(k);
        if (!r.Chance(chance) || !Fit(1)) return;
        int first = nEmit;
        if (CornerPlace(Pick(ref r, a, wa, b, wb), WM_ANY, 0, 0, angled, 0, ref r) >= 0) Keep(first, false);
    }

    // a doormat just inside a doorway (flat: it is walked over, so it may lie on the door's clear floor)
    int Doormat(int room, int k)
    {
        int cell = roomDoors[roomDoorStart[room] + k], d = roomDoorDir[roomDoorStart[room] + k];
        int cx = cell % IW, cz = cell / IW;
        int mat = Dn("k_doormat");
        int inward = GenData.WALL_HALF_MM + 60 + Sz(mat) / 2;
        int px, pz, yaw;
        // the doorway is two cells wide; the door list holds its first cell
        if (d == 0) { px = (cx + 1) * GenData.INNER_MM; pz = (cz + 1) * GenData.INNER_MM - inward; yaw = 180; }
        else if (d == 2) { px = (cx + 1) * GenData.INNER_MM; pz = cz * GenData.INNER_MM + inward; yaw = 0; }
        else if (d == 1) { px = (cx + 1) * GenData.INNER_MM - inward; pz = (cz + 1) * GenData.INNER_MM; yaw = 270; }
        else { px = cx * GenData.INNER_MM + inward; pz = (cz + 1) * GenData.INNER_MM; yaw = 90; }
        return StampAt(mat, room, px, pz, yaw, 0, -1, H_WALL, false);
    }

    // the room's main doorway, mm: its entrance (RoomEntries), else an outside one; the middle of the room if it has no door
    void MainDoor(int room, out int dx, out int dz)
    {
        int ext = roomEntry[room] >= 0 ? roomEntry[room] : ExtDoor(room), dk = ext >= 0 ? ext : 0;
        dx = roomDoorCount[room] > 0 ? DoorX(room, dk) : RoomMidX(room);
        dz = roomDoorCount[room] > 0 ? DoorZ(room, dk) : RoomMidZ(room);
    }

    // ============================================================================================ LIVING ROOM
    // Areas: a seating group that faces the media wall; a storage wall; a reading corner (large rooms); plants.
    void PlanLiving()
    {
        int room = pRoom, area = AreaM2(room);
        GenRng r = SR(1);
        int tvDef = Dn("k_tvCabinet");
        int sofaDef = Pick(ref r, "k_sofa", 5, "q_couch", 4, "k_designSofa", 3);
        int dx, dz;
        MainDoor(room, out dx, out dz);
        // (b) the focal point: the TV cabinet centred on a wall without a window, the sofa facing it
        int tv = -1, sofa = -1;
        ClearBans();
        for (int t = 0; t < 4 && sofa < 0; t++)
        {
            int first = nEmit;
            tv = WallPlace(tvDef, WM_CENTRE, 0, 0, WIN_AVOID, EF_MANDATORY, ref r);
            if (tv < 0) break;
            int seg = wrSeg;
            sofa = SofaFacing(tv, sofaDef, ref r);
            if (sofa >= 0 && Keep(first, true)) break;
            if (sofa < 0) TakeBack(first);
            sofa = -1; tv = -1; segBan[seg] = true;
        }
        ClearBans();
        if (sofa < 0)
        {
            // no wall with room in front of it: the sofa against a wall, the cabinet on another
            int first = nEmit;
            sofa = WallPlace(sofaDef, WM_CENTRE, 0, 0, WIN_AVOID, EF_MANDATORY, ref r);
            if (sofa >= 0 && !Keep(first, true)) sofa = -1;
            if (sofa < 0) MandFail(1);
            first = nEmit;
            tv = WallPlace(tvDef, sofa >= 0 ? WM_FAR : WM_CENTRE, sofa >= 0 ? emitX[sofa] : 0, sofa >= 0 ? emitZ[sofa] : 0, WIN_AVOID, 0, ref r);
            if (tv >= 0 && !Keep(first, false)) tv = -1;
        }
        // (c) what belongs with the seating: coffee table, rug, chairs at right angles, side table, lamp
        if (sofa >= 0)
        {
            int sd = emitDef[sofa], ssz = Sz(sd);
            GenRng r2 = SR(2);
            int ctDef = Pick(ref r2, "k_tableCoffee", 5, "q_tableLow", 3);
            int ctZ = ssz / 2 + 380 + Sz(ctDef) / 2;        // 0.38 m between the seat and the table (0.36 to 0.46 m)
            bool faces = tv >= 0 && emitSet[sofa] == tv;
            int view = faces ? AlongFront(tv, sofa) - ssz / 2 - Sz(tvDef) / 2 : 9000;
            if (Fit(1) && view - 380 - Sz(ctDef) >= 950)
            {
                int first = nEmit;
                if (Rel(ctDef, sofa, 0, ctZ, 0, 0) >= 0) Keep(first, false);
            }
            GenRng r3 = SR(3);
            if (r3.Chance(750) && Fit(1))
            {
                string rug = area >= 28 && r3.Chance(500) ? "k_rugLong" : "q_rug";
                if (RelFlat(Dn(rug), sofa, 0, ctZ - 120, 0, 0) < 0 && rug != "q_rug") RelFlat(Dn("q_rug"), sofa, 0, ctZ - 120, 0, 0);
            }
            GenRng r4 = SR(4);
            int chairs = area >= 26 ? r4.Between(1, 2) : r4.Between(0, 1);
            int chairDef = r4.Chance(350) ? Pick(ref r4, "k_designChair", 5, "k_recliner", 4) : Pick(ref r4, "q_armchair", 5, "k_loungeChair", 4);
            int side0 = r4.Chance(500) ? 1 : -1;
            // at right angles to the sofa, facing the table; where the room is narrow the chair moves back beside the
            // sofa's end, so a metre stays free in front of the screen (K1)
            int farMax = view - 950 - Sx(chairDef) / 2 + ssz / 2;
            int chZ = ctZ < farMax ? ctZ : farMax;
            int minZ = ssz / 2 - Sx(chairDef) / 2 + 150;
            if (chZ < minZ) chZ = minZ;
            int lxTable = (Sx(ctDef) > 1300 ? Sx(ctDef) : 1300) / 2 + 330 + Sz(chairDef) / 2, lxSofa = Sx(sd) / 2 + 80 + Sz(chairDef) / 2;
            for (int k = 0; k < chairs && Fit(1); k++)
            {
                int sgn = k == 0 ? side0 : -side0, first = nEmit;
                int e = Rel(chairDef, sofa, sgn * (chZ < ssz / 2 + Sx(chairDef) / 2 + 60 && lxSofa > lxTable ? lxSofa : lxTable), chZ, sgn > 0 ? 270 : 90, 0);
                if (e < 0) e = Rel(chairDef, sofa, sgn * lxSofa, chZ, sgn > 0 ? 270 : 90, 0);
                if (e >= 0) Keep(first, false);
            }
            GenRng r5 = SR(5);
            int tableSide = r5.Chance(500) ? 1 : -1;
            if (r5.Chance(650) && Fit(1))
            {
                int first = nEmit;
                if (RelBeside(Dn("q_tableSmall"), sofa, tableSide, 70, 0, ref r5) >= 0) Keep(first, false);
            }
            GenRng r6 = SR(6);
            if (r6.Chance(520) && Fit(1))
            {
                int first = nEmit;
                if (RelBeside(Pick(ref r6, "k_lampFloor", 5, "q_lampStanding", 3), sofa, -tableSide, 60, 0, ref r6) >= 0) Keep(first, false);
            }
        }
        // speakers flank the cabinet as a pair
        GenRng r7 = SR(7);
        if (tv >= 0 && r7.Chance(420) && Fit(2))
        {
            int first = nEmit, sp = Dn("k_speaker"), td = emitDef[tv];
            int lx = Sx(td) / 2 + 70 + Sx(sp) / 2, lz = -(Sz(td) - Sz(sp)) / 2;
            if (Rel(sp, tv, lx, lz, 0, 0) < 0 || Rel(sp, tv, -lx, lz, 0, 0) < 0) TakeBack(first);
            else Keep(first, false);
        }
        // the storage wall: one or two bookcases side by side, or a cabinet, centred on a free wall
        GenRng r8 = SR(8);
        if (r8.Chance(780) && Fit(1))
        {
            int first = nEmit;
            int pick = r8.Range(10);
            if (pick < 5)
            {
                rowN = area >= 24 && Fit(2) && r8.Chance(550) ? 2 : 1;
                for (int i = 0; i < rowN; i++) rowDef[i] = Dn("k_bookcase");
            }
            else { rowN = 1; rowDef[0] = Dn(pick < 8 ? "q_cabinet" : "k_bookcaseLow"); }
            if (WallRow(0, 1, WM_CENTRE, 0, 0, WIN_AVOID, 0, ref r8) >= 0) Keep(first, false);
        }
        // a reading corner in a large room: an armchair turned to the room, a lamp beside it
        GenRng r9 = SR(9);
        if (area >= 24 && r9.Chance(520) && Fit(2))
        {
            int first = nEmit;
            int chair = CornerPlace(Dn("q_armchair"), WM_FAR, dx, dz, true, 0, ref r9);
            if (chair >= 0 && Keep(first, false))
            {
                first = nEmit;
                if (RelBeside(Dn("k_lampFloor"), chair, 0, 60, 0, ref r9) >= 0) Keep(first, false);
            }
        }
        // (d) plants and lamps in corners, a low bookcase or a side table at a free wall
        Extras(20, 720, "ck_plant", "vk_bookcaseLow", "cq_cactus", "wk_sideTable", "ck_lampFloor", "mq_cabinet");
        // (e) the loose remainder: a box of toys in a corner, or a couple of moving boxes
        GenRng r12 = SR(12);
        int roll = r12.Range(1000);
        if (roll < 170 && LooseFit(3))
        {
            int first = nEmit;
            int box = CornerPlace(Dn("k_boxOpen"), WM_FAR, dx, dz, false, 0, ref r12);
            if (box >= 0 && Keep(first, false))
            {
                MarkLoose(first);
                first = nEmit;
                if (GroupBeside(GenData.FindGrp("toys"), box, 2, 0, ref r12) && Keep(first, false)) MarkLoose(first);
            }
        }
        else if (roll < 330 && LooseFit(2))
        {
            int first = nEmit;
            if (GroupAt(GenData.FindGrp("cardboard"), GenData.A_CORNER, 2, WM_FAR, dx, dz, 0, ref r12) && Keep(first, false)) MarkLoose(first);
        }
    }

    // the sofa opposite a TV cabinet, facing it: against the wall across the room when that is near enough,
    // else standing free 2.3 to 2.7 m from the screen with its back to the walkway
    int SofaFacing(int tv, int sofaDef, ref GenRng rng)
    {
        int tsz = Sz(emitDef[tv]), ssz = Sz(sofaDef);
        int wall = FrontDepth(tv);
        int gapAtWall = wall - 20 - ssz - tsz / 2;
        if (gapAtWall < 1500) return -1;                    // a screen needs its distance
        int s = gapAtWall <= 3300 ? wall - 20 - ssz / 2 : tsz / 2 + 2300 + rng.Range(5) * 100 + ssz / 2;
        for (int k = 0; k < 5; k++)
        {
            int lx = k == 0 ? 0 : (k == 1 ? 250 : (k == 2 ? -250 : (k == 3 ? 500 : -500)));
            int e = Rel(sofaDef, tv, lx, s, 180, EF_MANDATORY);
            if (e >= 0) return e;
        }
        return -1;
    }

    // ============================================================================================ DINING ROOM
    // Areas: the table set on the room's long axis; a sideboard wall; a plant or lamp corner.
    void PlanDining()
    {
        int room = pRoom;
        GenRng r = SR(1);
        // a narrow table in a room of one cell, where a square one leaves no way round it
        int table = AreaM2(room) >= 24 ? Pick(ref r, "k_table", 5, "q_table", 5) : Dn("k_table");
        int yaw = RoomLongAxis(room) == 0 ? 0 : 90;
        int first0 = nEmit;
        int t = CentrePiece(table, yaw, true, 2, 1, WM_NEAR, RoomMidX(room), RoomMidZ(room), 0, EF_MANDATORY, ref r);
        if (t >= 0)
        {
            // In a small room the walkway takes one side: the table is tried one cell off the middle each way, and
            // stands where it keeps the most chairs (report 15, K3: a seat with its back to a wall needs less room).
            int bestSeats = SeatsOf(t), bestX = emitX[t], bestZ = emitZ[t], tyaw = emitYaw[t];
            int k = 0;
            while (k < 4 && bestSeats < 4)
            {
                int nx = bestX + DX[k] * GenData.INNER_MM, nz = bestZ + DZ[k] * GenData.INNER_MM;
                TakeBack(first0);
                first0 = nEmit;
                int alt = FreePlaceAt(table, nx, nz, tyaw, 1, EF_MANDATORY, H_FREE);
                if (alt >= 0 && Keep(first0, true) && SeatsOf(alt) > bestSeats) { bestSeats = SeatsOf(alt); bestX = nx; bestZ = nz; k = 0; t = alt; continue; }
                if (alt >= 0) TakeBack(first0);
                first0 = nEmit;
                t = FreePlaceAt(table, bestX, bestZ, tyaw, 1, EF_MANDATORY, H_FREE);
                if (t < 0 || !Keep(first0, true)) { t = -1; break; }
                k++;
            }
        }
        if (t < 0) MandFail(1);
        // the sideboard in the middle of a wall: the widest that fits
        GenRng r2 = SR(2);
        if (r2.Chance(900) && Fit(1)) WallAny(WM_CENTRE, 0, 0, WIN_AVOID, 0, ref r2, "q_cabinet", "k_sideTable", "k_bookcaseLow");
        Extras(20, 750, "ck_plant", "cq_lampStanding", "wk_bookcaseLow", "cq_cactus", "ck_lampFloor");
    }

    // ============================================================================================ KITCHEN
    // Areas: the work run along one wall (round a corner if it must): fridge at an end, sink, cooker, worktops;
    // an eat-in table or a breakfast bar; the waste point at the end of the run; a pantry corner.
    int runFirst, runLast, runCx, runCz;   // the units of the last run placed: emit range, and its middle (mm)

    // Places a run of kitchen units along the room's walls: fridge, sink, cooker and worktops between them.
    // Returns how many of the three work centres (fridge, sink, cooker) stand.
    int KitchenRun(int want, ref GenRng rng)
    {
        int room = pRoom;
        int fr = Dn("k_fridge"), sk = Dn("k_sink"), sv = Dn("k_stove"), c1 = Dn("k_counter"), c2 = Dn("k_counterDrawer");
        int[] order = new int[8];
        bool haveF = false, haveS = false, haveV = false;
        runFirst = nEmit; runLast = -1; runCx = RoomMidX(room); runCz = RoomMidZ(room);
        int nearX = 0, nearZ = 0;
        for (int leg = 0; leg < 3 && !(haveF && haveS && haveV); leg++)
        {
            // the units still to come
            int n = 0;
            if (leg == 0)
            {
                if (want >= 5) { order[n++] = fr; order[n++] = c1; order[n++] = sk; order[n++] = c2; order[n++] = sv; if (want >= 6) order[n++] = c1; if (want >= 7) order[n++] = c2; }
                else if (want == 4) { order[n++] = fr; order[n++] = sk; order[n++] = c1; order[n++] = sv; }
                else { order[n++] = fr; order[n++] = sk; order[n++] = sv; }
            }
            else
            {
                if (!haveS) order[n++] = sk;
                if (!haveV) order[n++] = sv;
                if (!haveF) order[n++] = fr;
            }
            bool placed = false;
            for (int m = n; m >= 1 && !placed; m--)
            {
                // the longest stretch that fits: drop worktops first, then the far end of the list
                rowN = 0;
                int drop = n - m;
                for (int i = 0; i < n; i++)
                {
                    bool worktop = order[i] == c1 || order[i] == c2;
                    if (drop > 0 && worktop) { drop--; continue; }
                    rowDef[rowN++] = order[i];
                }
                if (rowN > m) rowN = m;
                if (rowN == 0) break;
                if (rng.Chance(500)) for (int i = 0; i < rowN / 2; i++) { int tmp = rowDef[i]; rowDef[i] = rowDef[rowN - 1 - i]; rowDef[rowN - 1 - i] = tmp; }
                int len = 0;
                for (int i = 0; i < rowN; i++) len += Sx(rowDef[i]);
                if (!FindWallRun(room, len, 900, 2, leg == 0 ? WM_CORNER : WM_NEAR, nearX, nearZ, WIN_ANY, ref rng)) continue;
                ArrangeForWindows(wrSeg, wrDir, wrCentre - len / 2, len, fr, sk, sv);
                int first = nEmit;
                if (StampRow(wrDir, wrBack, wrCentre - len / 2, 0, EF_MANDATORY) < 0) continue;
                if (!Keep(first, true)) continue;
                placed = true;
                for (int e = first; e < nEmit; e++)
                {
                    if (emitParent[e] >= 0) continue;
                    if (emitDef[e] == fr) haveF = true;
                    if (emitDef[e] == sk) haveS = true;
                    if (emitDef[e] == sv) haveV = true;
                    runLast = e;
                }
                int rot;
                WallPos(wrDir, wrBack, wrCentre, 900, out nearX, out nearZ, out rot);
                if (leg == 0) { runCx = nearX; runCz = nearZ; }
            }
        }
        return (haveF ? 1 : 0) + (haveS ? 1 : 0) + (haveV ? 1 : 0);
    }

    // The sink goes under the window if the run passes one; the fridge and the cooker do not stand at a window
    // (report 15, K10 and K11). Swaps units of rowDef; the run itself stays where it was found.
    void ArrangeForWindows(int seg, int d, int start, int len, int fr, int sk, int sv)
    {
        bool alongX = d == 0 || d == 2, reversed = d == 0 || d == 3;
        int line = segLine[seg];
        int at = 0, iF = -1, iS = -1, iV = -1, winMask = 0;
        for (int i = 0; i < rowN; i++)
        {
            int sx = Sx(rowDef[i]);
            int along = reversed ? start + len - at - sx / 2 : start + at + sx / 2;
            int cell = FloorDiv(along, GenData.INNER_MM);
            if (WindowAt(alongX ? cell : line, alongX ? line : cell, d)) winMask |= 1 << i;
            if (rowDef[i] == fr) iF = i; else if (rowDef[i] == sk) iS = i; else if (rowDef[i] == sv) iV = i;
            at += sx;
        }
        if (winMask == 0) return;
        // the fridge to an end without a window
        if (iF >= 0 && (winMask & (1 << iF)) != 0)
            for (int i = 0; i < rowN; i++)
                if ((winMask & (1 << i)) == 0 && rowDef[i] != sk && rowDef[i] != sv && (i == 0 || i == rowN - 1)) { rowDef[iF] = rowDef[i]; rowDef[i] = fr; iF = i; break; }
        // the sink under the window, the cooker away from it
        if (iS >= 0 && (winMask & (1 << iS)) == 0)
            for (int i = 0; i < rowN; i++)
                if ((winMask & (1 << i)) != 0 && rowDef[i] != fr) { int tmp = rowDef[i]; rowDef[i] = sk; rowDef[iS] = tmp; if (tmp == sv) iV = iS; iS = i; break; }
        if (iV >= 0 && (winMask & (1 << iV)) != 0)
            for (int i = 0; i < rowN; i++)
                if ((winMask & (1 << i)) == 0 && rowDef[i] != fr && rowDef[i] != sk) { rowDef[iV] = rowDef[i]; rowDef[i] = sv; break; }
    }

    // the unit at one end of the kitchen run (the lowest or the highest along the wall)
    int RunEnd(bool high)
    {
        int best = -1, bv = 0;
        for (int e = runFirst; e <= runLast; e++)
        {
            if (emitParent[e] >= 0 || (emitFlags[e] & EF_REMOVED) != 0 || emitRoom[e] != pRoom) continue;
            if (!Has(emitDef[e], GenData.FL_FIXTURE)) continue;
            int v = emitX[e] + emitZ[e];
            if (best < 0 || (high ? v > bv : v < bv)) { best = e; bv = v; }
        }
        return best;
    }

    void PlanKitchen()
    {
        int room = pRoom, area = AreaM2(room);
        GenRng r = SR(1);
        int want = area >= 28 ? 6 : (area >= 20 ? 5 : 4);
        if (r.Chance(400)) want++;
        int centres = KitchenRun(want, ref r);
        if (centres < 3) MandFail(3 - centres);
        // (c) eating in the kitchen: a table with chairs where the floor is open (a metre from the run, K2), or a
        // breakfast bar with stools against a free wall
        GenRng r2 = SR(2);
        int eat = r2.Range(1000);
        if (eat < (area >= 20 ? 700 : 480) && Fit(3))
        {
            int first = nEmit;
            int def = area >= 26 && r2.Chance(500) ? Dn("k_table") : Dn("r_tableRound");
            CentrePiece(def, RoomLongAxis(room) == 0 ? 0 : 90, false, 2, 1, WM_FAR, runCx, runCz, 0, 0, ref r2);
        }
        else if (eat < 850 && Fit(4))
        {
            int first = nEmit;
            rowN = 2; rowDef[0] = Dn("k_bar"); rowDef[1] = Dn("k_bar");
            int bar = WallRow(0, 2, WM_FAR, runCx, runCz, WIN_ANY, 0, ref r2);
            if (bar >= 0)
            {
                int stool = Dn("k_stool"), last = nEmit;
                for (int e = bar; e < last; e++)
                    if (emitDef[e] == rowDef[0] && emitParent[e] < 0) Rel(stool, e, 0, Sz(rowDef[0]) / 2 + 60 + Sz(stool) / 2, 180, 0);
                Keep(first, false);
            }
        }
        // the waste point: the bin at the end of the run, a box for recycling beside it
        GenRng r3 = SR(3);
        int bin = -1;
        if (runLast >= 0 && r3.Chance(900) && Fit(1))
        {
            int first = nEmit;
            bool highFirst = r3.Chance(500);
            for (int k = 0; k < 2 && bin < 0; k++)
            {
                int end = RunEnd(k == 0 ? highFirst : !highFirst);
                if (end < 0) continue;
                bin = RelBeside(Dn("k_trashcan"), end, OutwardSide(end, runCx, runCz), 50, 0, ref r3);
            }
            if (bin < 0) bin = WallPlace(Dn("k_trashcan"), WM_NEAR, runCx, runCz, WIN_ANY, 0, ref r3);
            if (bin >= 0 && !Keep(first, false)) bin = -1;
        }
        Extras(20, 450, "ck_plant", "wq_tableSmall", "cq_cactus");
        // (d) the pantry corner: two crates of vegetables at a wall away from the run
        GenRng r5 = SR(5);
        if (area >= 16 && r5.Chance(280) && Fit(2))
        {
            int first = nEmit;
            if (GroupAt(GenData.FindGrp("crates"), GenData.A_WALL, 2, WM_FAR, runCx, runCz, 0, ref r5)) Keep(first, false);
        }
        // (e) loose: the recycling box beside the bin
        GenRng r6 = SR(6);
        if (bin >= 0 && r6.Chance(300) && LooseFit(1))
        {
            int first = nEmit;
            if (RelBeside(Dn("k_boxOpen"), bin, 0, 40, 0, ref r6) >= 0 && Keep(first, false)) MarkLoose(first);
        }
    }

    // ============================================================================================ BEDROOM
    // Areas: the bed with its bedside tables; clothes storage; a desk nook, a dresser or a reading chair.
    void PlanBedroom()
    {
        int room = pRoom;
        GenRng r = SR(1);
        int bedDef = Pick(ref r, "k_bedDouble", 5, "k_bedSingle", 3, "q_bed", 3);
        bool dbl = bedDef == Dn("k_bedDouble");
        int dx, dz;
        MainDoor(room, out dx, out dz);
        // (b) the bed: its head on a wall without a window, a double centred on the wall, a single in a corner
        int bed = -1;
        for (int t = 0; t < 3 && bed < 0; t++)
        {
            int def = t < 2 ? bedDef : Dn("k_bedSingle");
            int first = nEmit;
            bed = WallPlace(def, (dbl && t == 0) ? WM_CENTRE : WM_CORNER, 0, 0, t == 0 ? WIN_NEVER : WIN_AVOID, EF_MANDATORY, ref r);
            if (bed >= 0 && !Keep(first, true)) bed = -1;
        }
        if (bed < 0) MandFail(1);
        if (bed >= 0)
        {
            // (c) bedside tables at the head: a pair for a double bed, one for a single; lamps come with them
            GenRng r2 = SR(2);
            int bs = Dn("k_bedside");
            int s0 = r2.Chance(500) ? 1 : -1, got = 0;
            dbl = emitDef[bed] == Dn("k_bedDouble");
            for (int k = 0; k < 2 && Fit(1); k++)
            {
                if (!dbl && got > 0) break;
                int first = nEmit;
                if (RelBeside(bs, bed, k == 0 ? s0 : -s0, 50, 0, ref r2) >= 0 && Keep(first, false)) got++;
            }
            // a rug under the foot half of the bed, showing at its sides
            GenRng r3 = SR(3);
            if (r3.Chance(520) && Fit(1))
            {
                int rug = Dn("q_rug"), bz = Sz(emitDef[bed]) / 4;
                if (RelFlat(rug, bed, 0, bz, 0, 0) < 0 && RelFlat(rug, bed, 450, bz, 0, 0) < 0) RelFlat(rug, bed, -450, bz, 0, 0);
            }
            GenRng r4 = SR(4);
            if (dbl && r4.Chance(180) && Fit(1))
            {
                int first = nEmit, bench = Dn("k_bench");
                if (Rel(bench, bed, 0, Sz(emitDef[bed]) / 2 + 70 + Sz(bench) / 2, 0, 0) >= 0) Keep(first, false);
            }
        }
        int bx = bed >= 0 ? emitX[bed] : RoomMidX(room), bz2 = bed >= 0 ? emitZ[bed] : RoomMidZ(room);
        // clothes: the wardrobe on a wall away from the bed, its door free to swing
        GenRng r5 = SR(5);
        if (r5.Chance(900) && Fit(1)) WallAny(WM_FAR, bx, bz2, WIN_NEVER, 0, ref r5, "gen_wardrobe", "q_cabinet", "q_cabinetSmall");
        // a desk under the window with its chair, or a dresser, or a reading chair
        GenRng r6 = SR(6);
        int third = r6.Range(1000);
        if (third < 400 && Fit(2))
        {
            int first = nEmit;
            if (WallPlace(Dn("k_desk"), WM_FAR, bx, bz2, WIN_PREFER, 0, ref r6) >= 0) Keep(first, false);
        }
        else if (third < 760 && Fit(1)) WallAny(WM_CENTRE, 0, 0, WIN_AVOID, 0, ref r6, r6.Chance(500) ? "q_cabinet" : "k_bookcaseLow", "k_bookcaseLow", "q_cabinetSmall");
        else if (third < 900 && Fit(2))
        {
            int first = nEmit;
            int chair = CornerPlace(Dn("q_armchair"), WM_FAR, bx, bz2, true, 0, ref r6);
            if (chair >= 0 && Keep(first, false))
            {
                first = nEmit;
                if (RelBeside(Pick(ref r6, "k_lampFloor", 5, "q_lampStanding", 4), chair, 0, 60, 0, ref r6) >= 0) Keep(first, false);
            }
        }
        Extras(20, 600, "ck_plant", "ck_lampFloor", "wq_cabinetSmall", "wk_bookcaseLow");
        // (e) loose: moving boxes in a corner
        GenRng r8 = SR(8);
        if (r8.Chance(160) && LooseFit(2))
        {
            int first = nEmit;
            if (GroupAt(GenData.FindGrp("cardboard"), GenData.A_CORNER, 2, WM_FAR, dx, dz, 0, ref r8) && Keep(first, false)) MarkLoose(first);
        }
    }

    // ============================================================================================ KIDS' ROOM
    // Areas: one or two single beds; a play rug with toys in the open middle; low storage; a desk.
    void PlanKids()
    {
        int room = pRoom, area = AreaM2(room);
        GenRng r = SR(1);
        int bedDef = Pick(ref r, "k_bedSingle", 6, "q_bed", 4);
        int first0 = nEmit;
        int bed = WallPlace(bedDef, WM_CORNER, 0, 0, WIN_NEVER, EF_MANDATORY, ref r);
        if (bed < 0) bed = WallPlace(bedDef, WM_CORNER, 0, 0, WIN_AVOID, EF_MANDATORY, ref r);
        if (bed >= 0 && !Keep(first0, true)) bed = -1;
        if (bed < 0) MandFail(1);
        GenRng r2 = SR(2);
        if (bed >= 0)
        {
            // a second bed beside the first, a bedside table between them
            int bs = Dn("k_bedside");
            int side = r2.Chance(500) ? 1 : -1;
            bool two = false;
            if (r2.Chance(550) && Fit(2))
                for (int k = 0; k < 2 && !two; k++)
                {
                    int s = k == 0 ? side : -side, first = nEmit;
                    if (Rel(bedDef, bed, s * (Sx(bedDef) + Sx(bs) + 120), 0, 0, 0) >= 0)
                    {
                        Rel(bs, bed, s * (Sx(bedDef) / 2 + 60 + Sx(bs) / 2), -(Sz(bedDef) - Sz(bs)) / 2, 0, 0);
                        if (Keep(first, false)) two = true;
                    }
                }
            if (!two && Fit(1))
            {
                int first = nEmit;
                if (RelBeside(bs, bed, 0, 50, 0, ref r2) >= 0) Keep(first, false);
            }
        }
        // the play rug in the open middle with a clump of toys on it
        GenRng r3 = SR(3);
        if (Fit(3))
        {
            int cell = OpenSpot(2, WM_NEAR, RoomMidX(room), RoomMidZ(room), 0, ref r3);
            if (cell >= 0)
            {
                int rug = FreePlace(Dn("k_rug"), cell, 0, 0, 0, H_FREE);
                int first = nEmit;
                fHost = rug >= 0 ? rug : H_FREE;
                bool ok = GroupAt(GenData.FindGrp("toys"), GenData.A_ANY, r3.Between(2, 3), WM_NEAR, cell % IW * GenData.INNER_MM + 250, cell / IW * GenData.INNER_MM + 250, 0, ref r3);
                fHost = H_NONE;
                if (ok) Keep(first, false);
            }
        }
        GenRng r4 = SR(4);
        if (r4.Chance(800) && Fit(1))
        {
            int first = nEmit;
            if (WallPlace(Dn("k_bookcaseLow"), WM_CENTRE, 0, 0, WIN_AVOID, 0, ref r4) >= 0) Keep(first, false);
        }
        GenRng r5 = SR(5);
        if (r5.Chance(650) && Fit(1))
        {
            int first = nEmit;
            if (WallPlace(Dn("gen_wardrobe"), WM_ANY, 0, 0, WIN_NEVER, 0, ref r5) >= 0) Keep(first, false);
        }
        GenRng r6 = SR(6);
        if (r6.Chance(450) && Fit(2))
        {
            int first = nEmit;
            if (WallPlace(Dn("k_desk"), WM_ANY, 0, 0, WIN_PREFER, 0, ref r6) >= 0) Keep(first, false);
        }
        // the toy box in a corner, a toy or two beside it
        GenRng r7 = SR(7);
        if (r7.Chance(600) && Fit(2))
        {
            int first = nEmit;
            int box = CornerPlace(Pick(ref r7, "k_boxOpen", 6, "s_chest", 4), WM_ANY, 0, 0, false, 0, ref r7);
            if (box >= 0 && Keep(first, false) && Fit(2) && r7.Chance(600))
            {
                first = nEmit;
                if (GroupBeside(GenData.FindGrp("toys"), box, 2, 0, ref r7)) Keep(first, false);
            }
        }
        Extras(20, 500, "ck_lampFloor", "wq_cabinetSmall", "ck_plant");
    }

    // ============================================================================================ OFFICE
    // Areas: the workstation (desk at a wall or window, its chair behind it); a reference wall of bookcases with
    // boxes of paper beside it; a visitor's seat; in a warehouse office a break table.
    void PlanOffice(bool warehouse)
    {
        int room = pRoom, area = AreaM2(room);
        GenRng r = SR(1);
        int first0 = nEmit;
        int desk = WallPlace(Dn("k_desk"), WM_ANY, 0, 0, WIN_PREFER, EF_MANDATORY, ref r);
        if (desk >= 0 && !Keep(first0, true)) desk = -1;
        if (desk < 0) MandFail(1);
        GenRng r2 = SR(2);
        if (desk >= 0 && r2.Chance(800) && Fit(1))
        {
            int first = nEmit;
            if (RelBeside(Dn("k_trashcan"), desk, 0, 70, 0, ref r2) >= 0) Keep(first, false);
        }
        GenRng r3 = SR(3);
        int shelf = -1;
        if (Fit(1))
        {
            int first = nEmit;
            rowN = Fit(2) && r3.Chance(600) ? 2 : 1;
            for (int i = 0; i < rowN; i++) rowDef[i] = Dn("k_bookcase");
            shelf = WallRow(0, 1, WM_CENTRE, 0, 0, WIN_AVOID, 0, ref r3);
            if (shelf >= 0 && !Keep(first, false)) shelf = -1;
        }
        GenRng r4 = SR(4);
        if (shelf >= 0 && r4.Chance(warehouse ? 600 : 420) && Fit(2))
        {
            int first = nEmit;
            if (GroupBeside(GenData.FindGrp("cardboard"), shelf, 2, 0, ref r4) || GroupAt(GenData.FindGrp("cardboard"), GenData.A_WALL, 2, WM_NEAR, emitX[shelf], emitZ[shelf], 0, ref r4)) Keep(first, false);
        }
        GenRng r5 = SR(5);
        if (desk >= 0 && area >= 16 && r5.Chance(warehouse ? 350 : 250) && Fit(2))
        {
            int first = nEmit;
            if (WallPlace(Dn("k_desk"), WM_FAR, emitX[desk], emitZ[desk], WIN_PREFER, 0, ref r5) >= 0) Keep(first, false);
        }
        GenRng r6 = SR(6);
        if (warehouse)
        {
            // the break table: a small round table with chairs in the open part
            if (r6.Chance(450) && Fit(3))
            {
                CentrePiece(Dn("r_tableRound"), 0, false, 2, 1, WM_ANY, 0, 0, 0, 0, ref r6);
            }
            GenRng r7 = SR(7);
            if (r7.Chance(350) && Fit(1))
            {
                int first = nEmit;
                if (WallPlace(Dn("s_workbench"), WM_ANY, 0, 0, WIN_ANY, 0, ref r7) >= 0) Keep(first, false);
            }
        }
        else if (r6.Chance(450) && Fit(2))
        {
            // the visitor's seat: an armchair in a corner, a small table beside it
            int first = nEmit;
            int chair = CornerPlace(Dn("q_armchair"), WM_FAR, desk >= 0 ? emitX[desk] : 0, desk >= 0 ? emitZ[desk] : 0, true, 0, ref r6);
            if (chair >= 0 && Keep(first, false) && Fit(1))
            {
                first = nEmit;
                if (RelBeside(Dn("q_tableSmall"), chair, 0, 60, 0, ref r6) >= 0) Keep(first, false);
            }
        }
        Extras(20, 650, "ck_plant", "wq_cabinet", "ck_lampFloor", "wk_bookcaseLow", "wk_sideTable");
    }

    // ============================================================================================ HALL
    // Areas: the door zone (mat, coats, shoes); a console or a bench at a wall. Nothing stands in the middle
    // and nothing deep: a hall is a route (report 15, K6).
    void PlanHall()
    {
        int room = pRoom;
        GenRng r = SR(1);
        int anyMat = 0;
        for (int k = 0; k < roomDoorCount[room]; k++)
            if (roomDoorExt[roomDoorStart[room] + k] && Fit(1) && Doormat(room, k) >= 0) anyMat++;
        if (anyMat == 0 && roomDoorCount[room] > 0 && Fit(1)) Doormat(room, 0);
        int dx, dz;
        MainDoor(room, out dx, out dz);
        // coats in the corner nearest the outside door
        int rack = -1;
        if (r.Chance(900) && Fit(1))
        {
            int first = nEmit;
            rack = CornerPlace(Dn("k_coatRack"), WM_NEAR, dx, dz, false, 0, ref r);
            if (rack >= 0 && !Keep(first, false)) rack = -1;
        }
        GenRng r2 = SR(2);
        int bench = -1;
        if (r2.Chance(700) && Fit(1))
        {
            int first = nEmit;
            bench = WallPlace(Pick(ref r2, "k_bench", 5, "k_benchCushion", 5), WM_NEAR, dx, dz, WIN_ANY, 0, ref r2);
            if (bench >= 0 && !Keep(first, false)) bench = -1;
        }
        // shoes by the bench or by the coats
        GenRng r3 = SR(3);
        if (r3.Chance(750) && Fit(1) && (bench >= 0 || rack >= 0))
        {
            int first = nEmit;
            if (RelBeside(Dn("gen_shoes"), bench >= 0 ? bench : rack, 0, 60, 0, ref r3) >= 0) Keep(first, false);
        }
        GenRng r4 = SR(4);
        if (r4.Chance(700) && Fit(1))
        {
            int first = nEmit;
            if (WallPlace(Pick(ref r4, "k_sideTable", 6, "q_cabinetSmall", 3), WM_CENTRE, 0, 0, WIN_AVOID, 0, ref r4) >= 0) Keep(first, false);
        }
        GenRng r5 = SR(5);
        if (r5.Chance(280) && Fit(1))
        {
            int first = nEmit;
            if (WallPlace(Dn("gen_wardrobe"), WM_FAR, dx, dz, WIN_NEVER, 0, ref r5) >= 0) Keep(first, false);
        }
        Extras(20, 600, "ck_plant", "ck_lampFloor", "wq_cabinetSmall");
        // (e) loose: a parcel left by the door
        GenRng r7 = SR(7);
        if (r7.Chance(260) && LooseFit(1))
        {
            int first = nEmit;
            if (GroupAt(GenData.FindGrp("cardboard"), GenData.A_DOOR_SIDE, r7.Between(1, 2), WM_NEAR, dx, dz, 0, ref r7) && Keep(first, false)) MarkLoose(first);
        }
    }

    // ============================================================================================ BATHROOM
    // Areas: the wet zone (bath along a wall, in a corner); toilet and basin side by side; a laundry corner;
    // a cabinet with towels; the cleaning corner by the door.
    void PlanBath()
    {
        int room = pRoom;
        GenRng r = SR(1);
        int dx, dz;
        MainDoor(room, out dx, out dz);
        int tub = -1;
        if (r.Chance(800))
        {
            int first = nEmit;
            tub = WallPlace(Dn("k_bathtub"), WM_CORNER, 0, 0, WIN_ANY, 0, ref r);
            if (tub >= 0 && !Keep(first, false)) tub = -1;
        }
        GenRng r2 = SR(2);
        int first2 = nEmit;
        int toilet = WallPlace(Dn("k_toilet"), WM_FAR, dx, dz, WIN_ANY, EF_MANDATORY, ref r2);
        if (toilet >= 0 && !Keep(first2, true)) toilet = -1;
        if (toilet < 0) MandFail(1);
        int sink = -1;
        first2 = nEmit;
        if (toilet >= 0) sink = RelBeside(Dn("k_bathSink"), toilet, 0, 320, EF_MANDATORY, ref r2);
        if (sink < 0) sink = WallPlace(Dn("k_bathSink"), WM_NEAR, toilet >= 0 ? emitX[toilet] : dx, toilet >= 0 ? emitZ[toilet] : dz, WIN_ANY, EF_MANDATORY, ref r2);
        if (sink >= 0 && !Keep(first2, true)) sink = -1;
        if (sink < 0) MandFail(1);
        // (c) the bin beside the toilet, a mat in front of the bath or the basin
        GenRng r3 = SR(3);
        if (toilet >= 0 && r3.Chance(750) && Fit(1))
        {
            int first = nEmit;
            if (RelBeside(Dn("k_trashcan"), toilet, 0, 130, 0, ref r3) >= 0) Keep(first, false);
        }
        GenRng r4 = SR(4);
        if (r4.Chance(700) && Fit(1))
        {
            int mat = Dn("k_doormat"), host = tub >= 0 ? tub : sink;
            if (host >= 0) RelFlat(mat, host, 0, Sz(emitDef[host]) / 2 + 80 + Sz(mat) / 2, 0, 0);
        }
        // the laundry corner: washer, perhaps a dryer beside it, the basket
        GenRng r5 = SR(5);
        if (r5.Chance(600) && Fit(1))
        {
            int first = nEmit;
            int washer = WallPlace(Dn("k_washer"), WM_CORNER, 0, 0, WIN_ANY, 0, ref r5);
            if (washer >= 0 && Keep(first, false))
            {
                int side = r5.Chance(500) ? 1 : -1;
                if (r5.Chance(450) && Fit(1))
                {
                    first = nEmit;
                    if (RelBeside(Dn("k_dryer"), washer, side, 20, 0, ref r5) >= 0) Keep(first, false);
                }
                if (r5.Chance(650) && Fit(1))
                {
                    first = nEmit;
                    if (RelBeside(Dn("gen_laundryBasket"), washer, -side, 60, 0, ref r5) >= 0) Keep(first, false);
                }
            }
        }
        GenRng r6 = SR(6);
        if (r6.Chance(600) && Fit(1))
        {
            int first = nEmit;
            if (WallPlace(Dn("q_cabinetSmall"), WM_NEAR, sink >= 0 ? emitX[sink] : dx, sink >= 0 ? emitZ[sink] : dz, WIN_ANY, 0, ref r6) >= 0) Keep(first, false);
        }
        Extras(20, 400, "ck_plant", "wk_trashcan");
        // (e) the cleaning corner by the door: a bucket with a mop or a broom
        GenRng r8 = SR(8);
        if (r8.Chance(330) && LooseFit(2))
        {
            int first = nEmit;
            FSet(Pick(ref r8, "gen_mop", 5, "gen_broom", 5), Pick(ref r8, "s_bucket", 5, "t_bucketMetal", 5));
            if (FormAt(GenData.GL_ROW, 70, GenData.A_WALL, WM_NEAR, dx, dz, 0, ref r8) >= 0 && Keep(first, false)) MarkLoose(first);
        }
    }
}
