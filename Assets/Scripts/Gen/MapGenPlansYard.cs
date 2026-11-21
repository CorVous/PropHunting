// Revision 29 (Cor, 21 Nov: "outdoors is just rocks"; nothing outdoors had an order a hunter could check): the house
// gardens' purposes. Every prop a plan here puts down belongs to a set laid out in a pattern a hunter can count (a
// grid, a line, a ring, an arc, pairs); natural things only stand along the fences, as borders.
//   picnic ground  tables with a bench along each long side: GRID (rows and columns, evenly spaced), LONG TABLE
//                  (tables end to end, a serving table at one end), RING (sets at even angles round a tree or a
//                  fire); a bin at the end of each row, a tap at the gate
//   drying yard    clothes racks in lines with a laundry basket at the end of each: PARALLEL, ALONG THE FENCE,
//                  PAIRS (two racks face to face); a bench and a bin by the door
//   campsite       (MapGenPlansZooCabin.cs, PlanCampsite) ARC, TWO ROWS, PITCHES
// What varies in an area: its rule, its size's number of sets, which side the anchor lies on (the gate, the far
// fence, the middle) with the sets turned to face it, and one accent set from a small pool against an edge.
// The plans run in phases like the rest (Cor: build freezes): a set a step.
public partial class MapGen
{
    // the spots a plan worked out for its sets, best first (mm, degrees), and the row each belongs to
    int[] yardX = new int[32], yardZ = new int[32], yardYaw = new int[32], yardRow = new int[32], yardSet = new int[32];
    int yardN;
    // the anchor: where it is (mm), the side of the room it lies on (0 +z, 1 +x, 2 -z, 3 -x), its kind (0 the gate,
    // 1 the far fence, 2 the middle), and the gate itself
    int yardAX, yardAZ, yardSide, yardKind, yardGX, yardGZ;

    static int Norm(int yaw) { return ((yaw % 360) + 360) % 360; }

    // the anchor of the area (sets yard*): a gate is an opening to another outdoor area (FindGates), else the door
    // of the building, else the middle
    void YardAnchor(int room, ref GenRng r)
    {
        int mx = RoomMidX(room), mz = RoomMidZ(room);
        FindGates(room);
        YardOpenings(room);
        yardGX = mx; yardGZ = mz;
        int k = ExtDoor(room);
        if (nGate > 0) { yardGX = gateCell[0] % IW * GenData.INNER_MM + 250; yardGZ = gateCell[0] / IW * GenData.INNER_MM + 250; }
        else if (k >= 0) { yardGX = DoorX(room, k); yardGZ = DoorZ(room, k); }
        yardKind = r.Range(3);
        if (yardKind == 0) { yardAX = yardGX; yardAZ = yardGZ; }
        else if (yardKind == 1) { yardAX = 2 * mx - yardGX; yardAZ = 2 * mz - yardGZ; }
        else { yardAX = mx; yardAZ = mz; }
        // the side: from the middle towards the anchor (the middle: towards the gate, the sets then face the middle)
        int dx = (yardKind == 2 ? yardGX : yardAX) - mx, dz = (yardKind == 2 ? yardGZ : yardAZ) - mz;
        if (dx == 0 && dz == 0) { if (RoomLongAxis(room) == 0) dz = 1; else dx = 1; }
        if (Abs(dx) > Abs(dz)) yardSide = dx > 0 ? 1 : 3; else yardSide = dz > 0 ? 0 : 2;
    }

    // every open edge to another area keeps its doorstep clear: the middle 2 m of the edge, 1 m deep, is walkway
    // (the walkway test keeps a room in one piece, but a row of sets along an open edge closed the way through it
    // to the lawn behind)
    void YardOpenings(int room)
    {
        for (int c = 0; c < W * H; c++)
        {
            if (cellRoom[c] != room || cellZone[c] == GenData.Z_NONE) continue;
            int cx = c % W, cz = c / W;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + DX[d], nz = cz + DZ[d];
                if (!InGrid(nx, nz) || cellZone[nz * W + nx] == GenData.Z_NONE || cellRoom[nz * W + nx] == room) continue;
                if (EdgeType(cx, cz, d) != GenData.E_NONE) continue;
                for (int t = 2; t < 6; t++)
                    for (int k = 0; k < 2; k++)
                    {
                        int lx = d == 1 ? 7 - k : (d == 3 ? k : t), lz = d == 0 ? 7 - k : (d == 2 ? k : t);
                        int i = (cz * 8 + lz) * IW + cx * 8 + lx;
                        if (innerRoom[i] == room && occ[i] == 0) tags[i] |= GenData.T_PATH;
                    }
            }
        }
    }

    // the room's box in mm, less a margin
    int YardBox(int room, int which, int margin)
    {
        if (which == 0) return roomX0[room] * GenData.INNER_MM + margin;
        if (which == 1) return (roomX1[room] + 1) * GenData.INNER_MM - margin;
        if (which == 2) return roomZ0[room] * GenData.INNER_MM + margin;
        return (roomZ1[room] + 1) * GenData.INNER_MM - margin;
    }

    // GRID: sets of setW x setD (setW along the set's own x) in rows, evenly spaced, centred in the room's box; the
    // rows run across the anchor's side (the sets face it) unless the other way round fits more of them (a narrow
    // strip); the rows nearest the anchor first
    void GridSpots(int room, int setW, int setD, int gapW, int gapD)
    {
        yardN = 0;
        bool alongX = yardSide == 0 || yardSide == 2;   // the anchor lies on a z side: the rows run along x
        if (GridCount(room, !alongX, setW, setD, gapW, gapD) > GridCount(room, alongX, setW, setD, gapW, gapD)) alongX = !alongX;
        int a0 = alongX ? YardBox(room, 0, 600) : YardBox(room, 2, 600), a1 = alongX ? YardBox(room, 1, 600) : YardBox(room, 3, 600);
        int b0 = alongX ? YardBox(room, 2, 600) : YardBox(room, 0, 600), b1 = alongX ? YardBox(room, 3, 600) : YardBox(room, 1, 600);
        int pa = setW + gapW, pb = setD + gapD;
        int na = (a1 - a0 - setW) / pa + 1, nb = (b1 - b0 - setD) / pb + 1;
        if (na < 1) na = 1;
        if (nb < 1) nb = 1;
        if (na > 6) na = 6;
        if (nb > 5) nb = 5;
        int ma = (a0 + a1) / 2, mb = (b0 + b1) / 2;
        // the row nearest the anchor first (facing the middle: the far rows first)
        int anchorB = alongX ? yardAZ : yardAX;
        bool nearHigh = anchorB > mb;
        if (yardKind == 2) nearHigh = !nearHigh;
        int yaw = alongX ? 0 : 90;
        for (int jb = 0; jb < nb; jb++)
        {
            int rowIdx = nearHigh ? nb - 1 - jb : jb;
            int b = mb + (2 * rowIdx - (nb - 1)) * pb / 2;
            for (int ja = 0; ja < na && yardN < yardX.Length; ja++)
            {
                int a = ma + (2 * ja - (na - 1)) * pa / 2;
                yardX[yardN] = alongX ? a : b; yardZ[yardN] = alongX ? b : a;
                yardYaw[yardN] = yaw; yardRow[yardN] = jb; yardSet[yardN] = -1; yardN++;
            }
        }
    }

    // how many sets a grid holds with its rows along x (or z)
    int GridCount(int room, bool alongX, int setW, int setD, int gapW, int gapD)
    {
        int la = alongX ? YardBox(room, 1, 600) - YardBox(room, 0, 600) : YardBox(room, 3, 600) - YardBox(room, 2, 600);
        int lb = alongX ? YardBox(room, 3, 600) - YardBox(room, 2, 600) : YardBox(room, 1, 600) - YardBox(room, 0, 600);
        if (la < setW || lb < setD) return 0;
        return ((la - setW) / (setW + gapW) + 1) * ((lb - setD) / (setD + gapD) + 1);
    }

    // LONG TABLE: sets end to end in one line down the room's long axis through the middle, from the end nearer the
    // anchor; that first spot stays free for the serving table
    void LineSpots(int room, int setW, int gap)
    {
        yardN = 0;
        bool alongZ = RoomLongAxis(room) == 1;
        int a0 = alongZ ? YardBox(room, 2, 600) : YardBox(room, 0, 600), a1 = alongZ ? YardBox(room, 3, 600) : YardBox(room, 1, 600);
        int m = alongZ ? RoomMidX(room) : RoomMidZ(room);
        int p = setW + gap;
        int n = (a1 - a0 - setW) / p + 1;
        if (n < 1) n = 1;
        if (n > 8) n = 8;
        int mid = (a0 + a1) / 2;
        bool fromHigh = (alongZ ? yardAZ : yardAX) > mid;
        yardSide = alongZ ? (fromHigh ? 0 : 2) : (fromHigh ? 1 : 3);   // the serving table's end
        int yaw = alongZ ? 90 : 0;
        for (int j = 0; j < n && yardN < yardX.Length; j++)
        {
            int idx = fromHigh ? n - 1 - j : j;
            int a = mid + (2 * idx - (n - 1)) * p / 2;
            yardX[yardN] = alongZ ? m : a; yardZ[yardN] = alongZ ? a : m;
            yardYaw[yardN] = yaw; yardRow[yardN] = 0; yardSet[yardN] = -1; yardN++;
        }
    }

    // RING: n sets at even angles round (cx, cz), radius rad, each turned along the ring (its benches face in and out)
    void RingSpots(int cx, int cz, int rad, int n, int a0)
    {
        yardN = 0;
        for (int k = 0; k < n && yardN < yardX.Length; k++)
        {
            int a = Norm(a0 + 360 * k / n);
            yardX[yardN] = cx + rad * sinTab[a] / 4096; yardZ[yardN] = cz + rad * sinTab[(a + 90) % 360] / 4096;
            yardYaw[yardN] = a; yardRow[yardN] = k; yardSet[yardN] = -1; yardN++;
        }
    }

    // a picnic set: the table (a booth: its seats are the benches, its places still laid now and then) and a bench
    // or two along each long side, facing it; kept whole or not at all. Returns the table, or -1.
    int PicnicSet(int table, int bench, int px, int pz, int yaw)
    {
        int first = nEmit;
        int t = StampAt(table, pRoom, px, pz, Norm(yaw), EF_BOOTH, -1, H_FREE, false);
        if (t < 0) return -1;
        int per = Sx(table) >= 2 * Sx(bench) - 100 ? 2 : 1;
        for (int side = -1; side <= 1; side += 2)
            for (int k = 0; k < per; k++)
            {
                int lx = per == 1 ? 0 : (k == 0 ? -1 : 1) * (Sx(bench) / 2 + 20);
                if (Rel(bench, t, lx, side * (Sz(table) / 2 + 60 + Sz(bench) / 2), side > 0 ? 180 : 0, 0) < 0) { TakeBack(first); return -1; }
            }
        if (!Keep(first, false)) return -1;
        return t;
    }

    // one thing at an exact spot, kept or not
    int YardOne(int def, int px, int pz, int yaw)
    {
        int first = nEmit;
        int e = StampAt(def, pRoom, px, pz, Norm(yaw), 0, -1, H_FREE, true);
        if (e < 0) return -1;
        return Keep(first, false) ? e : -1;
    }

    // an accent set from a small pool against an edge, as far from the anchor as it goes: one piece, or a pair
    void YardAccent(string[] pool, bool pair, ref GenRng r)
    {
        int def = Dn(pool[r.Range(pool.Length)]);
        int first = nEmit;
        int e = WallPlace(def, WM_FAR, yardAX, yardAZ, WIN_ANY, 0, ref r);
        if (e < 0) return;
        if (pair) RelBeside(def, e, 0, 120, 0, ref r);
        Keep(first, false);
    }

    // ============================================================================================ PICNIC GROUND
    void PlanPicnic()
    {
        planMax = 9;
        int room = pRoom;
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            YardAnchor(room, ref r);
            int rule = r.Range(3);
            int table = Dn("k_table"), bench = Dn("k_bench");
            int setD = Sz(table) + 2 * (60 + Sz(bench));
            int want = 2 + AreaM2(room) / 14;   // revision 30: two sets at the least (a picnic ground, not a table)
            if (want > 6) want = 6;
            if (rule == 0) GridSpots(room, Sx(table), setD, 1300, 1100);
            else if (rule == 1) LineSpots(room, Sx(table), 40);
            else
            {
                // round a tree, or a fire on a quarter of them, in the middle of the box
                int n = want < 3 ? 3 : (want > 5 ? 5 : want);
                int rad = 1500 + setD / 2;
                RingSpots(RoomMidX(room), RoomMidZ(room), rad, n, r.Range(4) * 90 + 45 * r.Range(2));
                want = n;
            }
            hq_Yard_rule = rule; hq_Yard_a = table; hq_Yard_b = bench; hq_Yard_want = want; hq_Yard_got = 0; hq_Yard_r = r;
            if (TraceRoom == room) System.Console.Error.WriteLine("  picnic rule " + rule + " anchor kind " + yardKind + " side " + yardSide + " spots " + yardN + " want " + want);
        }
        if (planPhase == 1)
        {
            // RING: the middle first (a tree to sit round, or the fire)
            if (hq_Yard_rule == 2)
            {
                GenRng r = hq_Yard_r;
                int cx = RoomMidX(room), cz = RoomMidZ(room);
                if (r.Chance(750)) TreeAt(Dn(r.Chance(500) ? "n_treeOak" : "n_treeFat"), cx, cz, ref r);
                else YardOne(Dn("n_campfire"), cx, cz, r.Range(4) * 90);
                hq_Yard_r = r;
            }
        }
        // the sets, one a step; LONG TABLE keeps the first spot (nearest the anchor) for the serving table
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (planIter == 0) hq_Yard_first = nEmit;
            int i = planIter + (hq_Yard_rule == 1 ? 1 : 0);
            if (i >= yardN || hq_Yard_got >= hq_Yard_want + (hq_Yard_rule == 1 ? 2 : 0)) PhBreak();
            else
            {
                int t = PicnicSet(hq_Yard_a, hq_Yard_b, yardX[i], yardZ[i], yardYaw[i]);
                yardSet[i] = t;
                if (t >= 0) hq_Yard_got++;
            }
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            // a LONG TABLE or a RING that holds fewer than two sets (a room cut by the house's corner, a doorstep in
            // the way) is taken back and laid out as a GRID instead, a set a step
            if (planIter == 0)
            {
                if (hq_Yard_rule == 0 || hq_Yard_got >= 2 || hq_Yard_got >= hq_Yard_want) PhBreak();
                else
                {
                    TakeBack(hq_Yard_first);
                    hq_Yard_rule = 0; hq_Yard_got = 0;
                    GridSpots(room, Sx(hq_Yard_a), Sz(hq_Yard_a) + 2 * (60 + Sz(hq_Yard_b)), 1300, 1100);
                }
            }
            else
            {
                int i = planIter - 1;
                if (i >= yardN || hq_Yard_got >= hq_Yard_want) PhBreak();
                else
                {
                    int t = PicnicSet(hq_Yard_a, hq_Yard_b, yardX[i], yardZ[i], yardYaw[i]);
                    yardSet[i] = t;
                    if (t >= 0) hq_Yard_got++;
                }
            }
        }
        if (planPhase == 4)
        {
            // LONG TABLE: the serving table across the line's end, with nothing round it but its things
            if (hq_Yard_rule == 1 && yardN > 0 && hq_Yard_got > 0)
            {
                int first = nEmit;
                int sv = Dn("k_table");
                bool alongZ = yardSide == 0 || yardSide == 2;
                int sgn = (yardSide == 0 || yardSide == 1) ? 1 : -1;
                int off = Sx(hq_Yard_a) / 2 + 900 + Sz(sv) / 2;
                int px = alongZ ? yardX[1 < yardN ? 1 : 0] : yardX[1 < yardN ? 1 : 0] + sgn * off;
                int pz = alongZ ? yardZ[1 < yardN ? 1 : 0] + sgn * off : yardZ[1 < yardN ? 1 : 0];
                if (StampAt(sv, room, px, pz, alongZ ? 0 : 90, EF_BOOTH, -1, H_FREE, false) >= 0) Keep(first, false);
            }
        }
        if (planPhase == 5)
        {
            // a bin at the end of each row (GRID), at the far end of the line (LONG TABLE), by the gate (RING)
            int bin = Dn("k_trashcan");
            GenRng r = hq_Yard_r;
            if (hq_Yard_rule == 2) { int first = nEmit; if (WallPlace(bin, WM_NEAR, yardGX, yardGZ, WIN_ANY, 0, ref r) >= 0) Keep(first, false); }
            else
            {
                for (int row = 0; row < 6; row++)
                {
                    int last = -1;
                    for (int i = 0; i < yardN; i++) if (yardRow[i] == row && yardSet[i] >= 0) last = i;
                    if (last < 0) continue;
                    int t = yardSet[last];
                    int lx = Sx(emitDef[t]) / 2 + 450;
                    int wx, wz;
                    Local(t, (hq_Yard_rule == 1 ? -1 : 1) * lx, 0, out wx, out wz);
                    YardOne(bin, wx, wz, emitYaw[t]);
                }
            }
            hq_Yard_r = r;
        }
        if (planPhase == 6)
        {
            // the tap at the gate
            int first = nEmit;
            GenRng r = hq_Yard_r;
            if (WallPlace(Dn("gen_tap"), WM_NEAR, yardGX, yardGZ, WIN_ANY, 0, ref r) >= 0) Keep(first, false);
            hq_Yard_r = r;
        }
        if (planPhase == 7)
        {
            string[] pool = { "s_barrel", "s_chest", "r_tableRound" };
            GenRng r = SR(6);
            YardAccent(pool, r.Chance(500), ref r);
            ClearBans();
        }
        if (planPhase == 8 && PhLoop(8, 100000))
        {
            if (planIter == 0) { hq_Borders_k0 = 10; hq_Borders_max = 4; hq_Borders_soil = false; PhSubReset(); }
            PhPush(); Borders_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) PhBreak();
        }
        if (planPhase == 9)
        {
            if (TraceRoom == room) System.Console.Error.WriteLine("  picnic sets " + hq_Yard_got + " of " + hq_Yard_want + " floor " + roomFloor[room]);
        }
    }

    // ============================================================================================ DRYING YARD
    // racks end to end in a line along the set's own x from (px, pz), turned yaw; a laundry basket at its end
    int RackLine(int rack, int basket, int px, int pz, int yaw, int n, ref GenRng r)
    {
        int got = 0, first = nEmit, prev = -1;
        for (int k = 0; k < n; k++)
        {
            int e;
            if (prev < 0) e = StampAt(rack, pRoom, px, pz, Norm(yaw), 0, -1, H_FREE, true);
            else e = Rel(rack, prev, Sx(rack) + 150, 0, 0, 0);
            if (e < 0) break;
            prev = e; got++;
        }
        if (got == 0) return 0;
        Rel(basket, prev, Sx(rack) / 2 + 150 + Sx(basket) / 2, 0, r.Range(4) * 90, 0);
        if (!Keep(first, false)) return 0;
        return got;
    }

    void PlanDrying()
    {
        planMax = 5;
        int room = pRoom;
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            YardAnchor(room, ref r);
            int rule = r.Range(3);
            int rack = Dn("ms_clothesRack");
            // the lines: across the anchor's side; PARALLEL lines a pace apart, PAIRS two lines face to face, a
            // little closer; ALONG THE FENCE one line at the far fence (worked out in phase 1)
            int lineD = Sz(rack);
            if (rule == 0) GridSpots(room, Sx(rack) * 2 + 150, lineD, 1400, 1500);
            else if (rule == 2) GridSpots(room, Sx(rack) + 0, lineD * 2 + 700, 1600, 1500);
            hq_Yard_rule = rule; hq_Yard_a = rack; hq_Yard_b = Dn("gen_laundryBasket"); hq_Yard_got = 0; hq_Yard_r = r;
            hq_Yard_want = 1 + AreaM2(room) / 12;
            if (hq_Yard_want > 4) hq_Yard_want = 4;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            GenRng r = hq_Yard_r;
            int rack = hq_Yard_a, basket = hq_Yard_b;
            if (hq_Yard_rule == 1)
            {
                // ALONG THE FENCE: one line, its back to the far fence or wall
                if (planIter > 0) PhBreak();
                else
                {
                    int first = nEmit;
                    int e = WallPlace(rack, WM_FAR, yardGX, yardGZ, WIN_ANY, 0, ref r);
                    if (e >= 0)
                    {
                        int prev = e, n = 2 + AreaM2(room) / 16;   // revision 30: a line of two racks at the least
                        for (int k = 1; k < n && k < 4; k++) { int f = Rel(rack, prev, Sx(rack) + 150, 0, 0, 0); if (f < 0) break; prev = f; }
                        Rel(basket, prev, Sx(rack) / 2 + 150 + Sx(basket) / 2, 0, r.Range(4) * 90, 0);
                        if (Keep(first, false)) hq_Yard_got++;
                    }
                }
            }
            else if (planIter >= yardN || hq_Yard_got >= hq_Yard_want) PhBreak();
            else
            {
                int i = planIter;
                if (hq_Yard_rule == 0)
                {
                    // PARALLEL: two racks end to end, the basket at the end
                    int lx = -(Sx(rack) + 150) / 2;
                    int a = yardYaw[i], sn = sinTab[a], cs = sinTab[(a + 90) % 360];
                    if (RackLine(rack, basket, yardX[i] + lx * cs / 4096, yardZ[i] - lx * sn / 4096, a, 2, ref r) > 0) hq_Yard_got++;
                }
                else
                {
                    // PAIRS: two racks face to face, a basket between their ends
                    int first = nEmit;
                    int e = StampAt(rack, room, yardX[i], yardZ[i], yardYaw[i], 0, -1, H_FREE, true);
                    if (e >= 0 && Rel(rack, e, 0, Sz(rack) + 700, 180, 0) >= 0)
                    {
                        Rel(basket, e, Sx(rack) / 2 + 150 + Sx(basket) / 2, Sz(rack) / 2 + 350, 0, 0);
                        if (Keep(first, false)) hq_Yard_got++;
                    }
                    else TakeBack(first);
                }
            }
            hq_Yard_r = r;
        }
        if (planPhase == 2)
        {
            // a bench and a bin by the door (the house's), else by the gate
            GenRng r = SR(2);
            int k = ExtDoor(room);
            int dx = k >= 0 ? DoorX(room, k) : yardGX, dz = k >= 0 ? DoorZ(room, k) : yardGZ;
            int first = nEmit;
            int b = WallPlace(Dn("k_bench"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r);
            if (b >= 0) RelBeside(Dn("k_trashcan"), b, 0, 150, 0, ref r);
            else WallPlace(Dn("k_trashcan"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r);
            Keep(first, false);
        }
        if (planPhase == 3)
        {
            string[] pool = { "s_bucket", "gen_tap", "m_wheelbarrow" };
            GenRng r = SR(3);
            YardAccent(pool, false, ref r);
            ClearBans();
        }
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (planIter == 0) { hq_Borders_k0 = 10; hq_Borders_max = 3; hq_Borders_soil = false; PhSubReset(); }
            PhPush(); Borders_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) PhBreak();
        }
        if (planPhase == 5)
        {
            if (TraceRoom == room) System.Console.Error.WriteLine("  drying rule " + hq_Yard_rule + " lines " + hq_Yard_got + " floor " + roomFloor[room]);
        }
    }

    int hq_Yard_rule, hq_Yard_a, hq_Yard_b, hq_Yard_want, hq_Yard_got, hq_Yard_first;
    GenRng hq_Yard_r;
}
