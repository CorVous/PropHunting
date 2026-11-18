// MapGen, part 3c: the plans of the warehouse and the diner (see MapGenPlans.cs for the order every plan follows).
public partial class MapGen
{
    // ============================================================================================ WAREHOUSE
    // Storage field: racks in rows with aisles and along a wall; a block of pallets near a door; a row of drums
    // against a wall away from the doors; rows of crates, boxes and barrels at the walls and beside the racks;
    // timber and stacks at a wall; a workbench corner with its tools.

    // racks along the long axis in rows 2.5 m apart, two to a stretch, then a cross aisle (04 section 4.3.3)
    int RackRows(int room, int def, int max, ref GenRng rng)
    {
        int placed = 0;
        int x0 = roomX0[room], z0 = roomZ0[room], x1 = roomX1[room], z1 = roomZ1[room];
        bool alongX = (x1 - x0) >= (z1 - z0);
        int len = GenData.defW[def];
        int rowStart = (alongX ? z0 : x0) + 5, rowEnd = (alongX ? z1 : x1) - 5;
        int row = 0;
        for (int rr = rowStart; rr <= rowEnd && placed < max; rr += 5, row++)
        {
            int a0 = (alongX ? x0 : z0) + 3 + rng.Range(2), a1 = (alongX ? x1 : z1) - 3;
            int inRow = 0;
            for (int a = a0; a + len - 1 <= a1 && placed < max; a += len)
            {
                if (inRow == 2) { a += 2; inRow = 0; if (a + len - 1 > a1) break; }
                int rx = alongX ? a : rr, rz = alongX ? rr : a;
                int gw = alongX ? len : 1, gd = alongX ? 1 : len;
                if (!RectFree(rx, rz, gw, gd, room, def)) { inRow = 0; continue; }
                int rot = alongX ? ((row & 1) == 0 ? 0 : 2) : ((row & 1) == 0 ? 1 : 3);
                int px = (2 * rx + gw) * GenData.INNER_MM / 2, pz = (2 * rz + gd) * GenData.INNER_MM / 2;
                int first = nEmit;
                curHost = H_FREE;
                if (StampObject(def, room, rx, rz, gw, gd, rot, px, pz, rot * 90, 0) < 0) { inRow = 0; continue; }
                if (!KeepIfWalkable(room, first, false)) { inRow = 0; continue; }
                placed++; inRow++;
            }
        }
        return placed;
    }

    // a block of pallets, side by side, 10 cm apart: cols x rows around a cell. Returns the first emit or -1.
    int PalletBlock(int cell, int cols, int rows, int yaw, ref GenRng rng)
    {
        hq_PalletBlock_cell = cell;
        hq_PalletBlock_cols = cols;
        hq_PalletBlock_rows = rows;
        hq_PalletBlock_yaw = yaw;
        hq_PalletBlock_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); PalletBlock_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_PalletBlock_rng;
        return hq_PalletBlock_ret;
    }

    // PalletBlock in phases, a step a call (autophase4); PalletBlock() above runs them to the end
    void PalletBlock_Body()
    {
        if (Ph(0))
        {
            int cell = hq_PalletBlock_cell;
            int cols = hq_PalletBlock_cols;
            int rows = hq_PalletBlock_rows;
            int yaw = hq_PalletBlock_yaw;
            GenRng rng = hq_PalletBlock_rng;
            int p = Dn("w_pallet"), pl = Dn("w_palletLoaded");
            int pitch = 1164 + 100;
            int cx = cell % IW * GenData.INNER_MM + 250, cz = cell / IW * GenData.INNER_MM + 250;
            int set = -1, first = nEmit;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    int ox = (2 * i - (cols - 1)) * pitch / 2, oz = (2 * j - (rows - 1)) * pitch / 2;
                    int px = yaw == 0 ? cx + ox : cx + oz, pz = yaw == 0 ? cz + oz : cz + ox;
                    int def = rng.Chance(400) ? pl : p;
                    int e = StampAt(def, pRoom, px, pz, yaw, 0, set, set >= 0 ? set : H_FREE, true);
                    if (e >= 0 && set < 0) set = e;
                }
            hq_PalletBlock_cell = cell;
            hq_PalletBlock_cols = cols;
            hq_PalletBlock_rows = rows;
            hq_PalletBlock_yaw = yaw;
            hq_PalletBlock_rng = rng;
            hq_PalletBlock_set = set;
            hq_PalletBlock_first = first;
        }
        if (Ph(1))
        {
            int set = hq_PalletBlock_set;
            int first = hq_PalletBlock_first;
            if (set >= 0 && !Keep(first, false)) set = -1;
            { hq_PalletBlock_ret = set; hq_PalletBlock_set = set; hq_PalletBlock_first = first; PhEnd(); return; }
            hq_PalletBlock_set = set;
            hq_PalletBlock_first = first;
        }
    }

    // a row of fuel drums against a wall with a jerrycan or two at its end, away from the doors (report 15, W-drum row)
    bool DrumRow(int n, ref GenRng rng)
    {
        int room = pRoom;
        string[] drums = { "w_fuelBarrel", "w_fuelBarrelB", "w_fuelBarrelC" };
        string[] cans = { "w_jerrycan", "w_jerrycanB", "w_jerrycanC" };
        int main = rng.Range(3), cansN = n >= 4 ? rng.Between(1, 2) : 1;
        fN = 0;
        for (int i = 0; i < n - cansN; i++) FAdd(Dn(drums[(i == 1 && rng.Chance(400)) ? (main + 1) % 3 : main]));
        for (int i = 0; i < cansN; i++) FAdd(Dn(cans[(main + i) % 3]));
        int first = nEmit;
        return FormAt(GenData.GL_ROW, 60, GenData.A_WALL, WM_FAR, DoorX(room, 0), DoorZ(room, 0), 0, ref rng) >= 0 && Keep(first, false);
    }

    void PlanStorage()
    {
        int room = pRoom, cells = roomOuterCells[room];
        if (Ph(0))
        {
            GenRng r = SR(1);
            int rack = Dn("gen_rack");
            int dx = 0, dz = 0;
            MainDoor(room, out dx, out dz);
            // (b) racks: a run of two or three along a wall, then free-standing rows with aisles
            int first0 = nEmit;
            rowN = cells >= 3 ? 3 : 2;
            for (int i = 0; i < rowN; i++) rowDef[i] = rack;
            hq_PlanStorage_r = r;
            hq_PlanStorage_rack = rack;
            hq_PlanStorage_dx = dx;
            hq_PlanStorage_dz = dz;
            hq_PlanStorage_first0 = first0;
        }
        if (Ph(1))
        {
            GenRng r = hq_PlanStorage_r;
            int first0 = hq_PlanStorage_first0;
            if (WallRow(0, 2, WM_CENTRE, 0, 0, WIN_NEVER, EF_MANDATORY, ref r) >= 0) Keep(first0, true);
            int maxRows = 1 + cells;
            if (maxRows * 3 > roomFloorTarget[room] / 2) maxRows = roomFloorTarget[room] / 6;
            hq_PlanStorage_r = r;
            hq_PlanStorage_first0 = first0;
            hq_PlanStorage_maxRows = maxRows;
        }
        if (Ph(2))
        {
            GenRng r = hq_PlanStorage_r;
            int rack = hq_PlanStorage_rack;
            int maxRows = hq_PlanStorage_maxRows;
            RackRows(room, rack, maxRows, ref r);
            // a block of pallets where the floor is open, near a door
            GenRng r2 = SR(2);
            hq_PlanStorage_sel1 = 0;
            if (Fit(2)) hq_PlanStorage_sel1 = 1;
            hq_PlanStorage_r = r;
            hq_PlanStorage_rack = rack;
            hq_PlanStorage_maxRows = maxRows;
            hq_PlanStorage_r2 = r2;
        }
        if (Ph(3))
        {
            if (hq_PlanStorage_sel1 == 1)
            {
                int dx = hq_PlanStorage_dx;
                int dz = hq_PlanStorage_dz;
                GenRng r2 = hq_PlanStorage_r2;
                int cell = OpenSpot(3, WM_NEAR, dx, dz, 0, ref r2);
                hq_PlanStorage_dx = dx;
                hq_PlanStorage_dz = dz;
                hq_PlanStorage_r2 = r2;
                hq_PlanStorage_cell = cell;
            }
        }
        if (Ph(4))
        {
            if (hq_PlanStorage_sel1 == 1)
            {
                GenRng r2 = hq_PlanStorage_r2;
                int cell = hq_PlanStorage_cell;
                if (cell >= 0) PalletBlock(cell, 2, Fit(4) && cells >= 3 ? 2 : 1, RoomLongAxis(room) == 0 ? 0 : 90, ref r2);
                hq_PlanStorage_r2 = r2;
                hq_PlanStorage_cell = cell;
            }
        }
        if (Ph(5))
        {
            // the workbench corner: bench against a wall, its stool, tools leaning beside it, a pile of parts
            GenRng r3 = SR(3);
            hq_PlanStorage_sel2 = 0;
            if (r3.Chance(650) && Fit(2)) hq_PlanStorage_sel2 = 1;
            hq_PlanStorage_r3 = r3;
        }
        if (Ph(6))
        {
            if (hq_PlanStorage_sel2 == 1)
            {
                GenRng r3 = hq_PlanStorage_r3;
                int first = nEmit;
                int wb = WallPlace(Dn("s_workbench"), WM_CORNER, 0, 0, WIN_ANY, 0, ref r3);
                hq_PlanStorage_r3 = r3;
                hq_PlanStorage_first = first;
                hq_PlanStorage_wb = wb;
            }
        }
        if (Ph(7))
        {
            if (hq_PlanStorage_sel2 == 1)
            {
                int first = hq_PlanStorage_first;
                int wb = hq_PlanStorage_wb;
                hq_PlanStorage_sel3 = 0;
                if (wb >= 0 && Keep(first, false)) hq_PlanStorage_sel3 = 1;
                hq_PlanStorage_first = first;
                hq_PlanStorage_wb = wb;
            }
        }
        if (Ph(8))
        {
            if (hq_PlanStorage_sel2 == 1 && hq_PlanStorage_sel3 == 1)
            {
                GenRng r3 = hq_PlanStorage_r3;
                int first = hq_PlanStorage_first;
                int wb = hq_PlanStorage_wb;
                if (Fit(2))
                {
                    first = nEmit;
                    if (GroupBeside(GenData.FindGrp("tools"), wb, r3.Between(2, 3), 0, ref r3)) Keep(first, false);
                }
                hq_PlanStorage_r3 = r3;
                hq_PlanStorage_first = first;
                hq_PlanStorage_wb = wb;
            }
        }
        if (Ph(9))
        {
            if (hq_PlanStorage_sel2 == 1 && hq_PlanStorage_sel3 == 1)
            {
                GenRng r3 = hq_PlanStorage_r3;
                int first = hq_PlanStorage_first;
                int wb = hq_PlanStorage_wb;
                if (r3.Chance(500) && Fit(1))
                {
                    first = nEmit;
                    if (RelBeside(Dn("w_parts"), wb, 0, 80, 0, ref r3) >= 0) Keep(first, false);
                }
                hq_PlanStorage_r3 = r3;
                hq_PlanStorage_first = first;
                hq_PlanStorage_wb = wb;
            }
        }
        if (Ph(10))
        {
            // revision 12: the tool wall beside the workbench corner, now and then
            GenRng r18 = SR(18);
            if (r18.Chance(400) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("gen_toolwall"), WM_ANY, 0, 0, WIN_NEVER, 0, ref r18) >= 0) Keep(first, false); }
            GenRng r4 = SR(4);
            hq_PlanStorage_r4 = r4;
        }
        if (Ph(11))
        {
            GenRng r4 = hq_PlanStorage_r4;
            if (r4.Chance(700) && Fit(3)) DrumRow(Fit(5) ? r4.Between(3, 5) : 3, ref r4);
            // (d) rows of goods beside the racks and at the walls
            string[] rows = { "crates", "boxes", "barrels", "sacks" };
            int[] chance = { 850, 750, 550, 400 };
            hq_PlanStorage_r4 = r4;
            hq_PlanStorage_rows = rows;
            hq_PlanStorage_chance = chance;
        }
        if (PhLoop(12, 100000))
        {
            string[] rows = hq_PlanStorage_rows;
            int[] chance = hq_PlanStorage_chance;
            int k = planIter;
            if (!(k < rows.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(5 + k);
                if (!rk.Chance(chance[k]) || !Fit(2)) continue;
                int g = GenData.FindGrp(rows[k]), first = nEmit;
                int n = rk.Between(GenData.grpMin[g], Fit(GenData.grpMax[g]) ? GenData.grpMax[g] : GenData.grpMin[g]);
                if (PlaceGroup(g, room, k == 2 ? GenData.A_WALL : GenData.A_BESIDE, n, 0, ref rk)) Keep(first, false);
            }
            hq_PlanStorage_rows = rows;
            hq_PlanStorage_chance = chance;
        }
        if (Ph(13))
        {
            // timber against a wall: planks and logs side by side
            GenRng r9 = SR(9);
            if (r9.Chance(480) && Fit(2))
            {
                int first = nEmit;
                FSet(Dn("w_planks"), Dn("w_logs"));
                if (FormAt(GenData.GL_ROW, 150, GenData.A_WALL, WM_ANY, 0, 0, 0, ref r9) >= 0) Keep(first, false);
            }
            string[] stacks = { "w_bricks", "w_textiles", "w_ironStack", "s_boxLarge", "s_boxLargeOpen" };
            hq_PlanStorage_stacks = stacks;
        }
        if (PhLoop(14, 100000))
        {
            string[] stacks = hq_PlanStorage_stacks;
            int k = planIter;
            if (!(k < stacks.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(10 + k);
                if (!rk.Chance(330) || !Fit(1)) continue;
                int first = nEmit;
                if (WallPlace(Dn(stacks[k]), WM_ANY, 0, 0, WIN_ANY, 0, ref rk) >= 0) Keep(first, false);
            }
            hq_PlanStorage_stacks = stacks;
        }
        if (Ph(15))
        {
            int dx = hq_PlanStorage_dx;
            int dz = hq_PlanStorage_dz;
            GenRng r15 = SR(15);
            if (r15.Chance(450) && Fit(1))
            {
                int first = nEmit;
                if (WallPlace(Dn("ms_handTruck"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r15) >= 0) Keep(first, false);
            }
            // a second block of pallets in a large hall
            GenRng r16 = SR(16);
            hq_PlanStorage_sel4 = 0;
            if (cells >= 4 && r16.Chance(600) && Fit(2)) hq_PlanStorage_sel4 = 1;
            hq_PlanStorage_dx = dx;
            hq_PlanStorage_dz = dz;
            hq_PlanStorage_r16 = r16;
        }
        if (Ph(16))
        {
            if (hq_PlanStorage_sel4 == 1)
            {
                GenRng r16 = hq_PlanStorage_r16;
                int cell = OpenSpot(3, WM_ANY, 0, 0, 0, ref r16);
                hq_PlanStorage_r16 = r16;
                hq_PlanStorage_cell = cell;
            }
        }
        if (Ph(17))
        {
            if (hq_PlanStorage_sel4 == 1)
            {
                GenRng r16 = hq_PlanStorage_r16;
                int cell = hq_PlanStorage_cell;
                if (cell >= 0) PalletBlock(cell, 2, 1, RoomLongAxis(room) == 0 ? 0 : 90, ref r16);
                hq_PlanStorage_r16 = r16;
                hq_PlanStorage_cell = cell;
            }
        }
        if (Ph(18))
        {
            // (e) loose: the broom and bucket by a door
            GenRng r17 = SR(17);
            hq_PlanStorage_sel5 = 0;
            if (r17.Chance(350) && LooseFit(2)) hq_PlanStorage_sel5 = 1;
            hq_PlanStorage_r17 = r17;
        }
        if (Ph(19))
        {
            if (hq_PlanStorage_sel5 == 1)
            {
                GenRng r17 = hq_PlanStorage_r17;
                int first = nEmit;
                FSet(Dn("gen_broom"), Pick(ref r17, "s_bucket", 5, "t_bucketMetal", 5));
                hq_PlanStorage_r17 = r17;
                hq_PlanStorage_first = first;
            }
        }
        if (Ph(20))
        {
            if (hq_PlanStorage_sel5 == 1)
            {
                int dx = hq_PlanStorage_dx;
                int dz = hq_PlanStorage_dz;
                GenRng r17 = hq_PlanStorage_r17;
                int first = hq_PlanStorage_first;
                if (FormAt(GenData.GL_ROW, 70, GenData.A_WALL, WM_NEAR, dx, dz, 0, ref r17) >= 0 && Keep(first, false)) MarkLoose(first);
                hq_PlanStorage_dx = dx;
                hq_PlanStorage_dz = dz;
                hq_PlanStorage_r17 = r17;
                hq_PlanStorage_first = first;
            }
        }
    }

    // Loading dock: a staging lane of pallets in a row inside the door; goods waiting at the walls; the hand truck.
    void PlanLoading()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            int dx = 0, dz = 0;
            MainDoor(room, out dx, out dz);
            int cell = OpenSpot(2, WM_NEAR, RoomMidX(room), RoomMidZ(room), 0, ref r);
            int lane = -1;
            hq_PlanLoading_r = r;
            hq_PlanLoading_dx = dx;
            hq_PlanLoading_dz = dz;
            hq_PlanLoading_cell = cell;
            hq_PlanLoading_lane = lane;
        }
        if (Ph(1))
        {
            GenRng r = hq_PlanLoading_r;
            int cell = hq_PlanLoading_cell;
            int lane = hq_PlanLoading_lane;
            if (cell >= 0) lane = PalletBlock(cell, Fit(3) ? r.Between(2, 3) : 2, 1, r.Chance(500) ? 0 : 90, ref r);
            hq_PlanLoading_r = r;
            hq_PlanLoading_cell = cell;
            hq_PlanLoading_lane = lane;
        }
        if (Ph(2))
        {
            GenRng r = hq_PlanLoading_r;
            int cell = hq_PlanLoading_cell;
            int lane = hq_PlanLoading_lane;
            if (lane < 0 && cell >= 0) lane = PalletBlock(cell, 1, 1, 0, ref r);
            if (lane < 0) MandFail(1);
            GenRng r2 = SR(2);
            hq_PlanLoading_r = r;
            hq_PlanLoading_cell = cell;
            hq_PlanLoading_lane = lane;
            hq_PlanLoading_r2 = r2;
        }
        if (Ph(3))
        {
            int dx = hq_PlanLoading_dx;
            int dz = hq_PlanLoading_dz;
            GenRng r2 = hq_PlanLoading_r2;
            if (r2.Chance(700) && Fit(1))
            {
                int first = nEmit;
                if (WallPlace(Dn("ms_handTruck"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r2) >= 0) Keep(first, false);
            }
            string[] rows = { "crates", "boxes", "barrels", "sacks" };
            int[] chance = { 800, 650, 380, 350 };
            hq_PlanLoading_dx = dx;
            hq_PlanLoading_dz = dz;
            hq_PlanLoading_r2 = r2;
            hq_PlanLoading_rows = rows;
            hq_PlanLoading_chance = chance;
        }
        if (PhLoop(4, 100000))
        {
            string[] rows = hq_PlanLoading_rows;
            int[] chance = hq_PlanLoading_chance;
            int k = planIter;
            if (!(k < rows.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(3 + k);
                if (!rk.Chance(chance[k]) || !Fit(2)) continue;
                int g = GenData.FindGrp(rows[k]), first = nEmit;
                if (PlaceGroup(g, room, GenData.A_WALL, rk.Between(2, Fit(3) ? 3 : 2), 0, ref rk)) Keep(first, false);
            }
            hq_PlanLoading_rows = rows;
            hq_PlanLoading_chance = chance;
        }
        if (Ph(5))
        {
            GenRng r7 = SR(7);
            if (r7.Chance(450) && Fit(1))
            {
                int first = nEmit;
                if (WallPlace(Pick(ref r7, "s_boxLarge", 5, "w_planks", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r7) >= 0) Keep(first, false);
            }
            GenRng r8 = SR(8);
            hq_PlanLoading_r8 = r8;
        }
        if (Ph(6))
        {
            int dx = hq_PlanLoading_dx;
            int dz = hq_PlanLoading_dz;
            GenRng r8 = hq_PlanLoading_r8;
            if (r8.Chance(500) && Fit(1))
            {
                int first = nEmit;
                if (WallPlace(Dn("k_trashcan"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r8) >= 0) Keep(first, false);
            }
            // goods still waiting: while the dock is under its range, another row at a wall or a pallet against it (a
            // dock whose first rows found no room otherwise ends with its staging lane alone)
            GenRng r10 = SR(10);
            hq_PlanLoading_dx = dx;
            hq_PlanLoading_dz = dz;
            hq_PlanLoading_r8 = r8;
            hq_PlanLoading_r10 = r10;
        }
        if (PhLoop(7, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 6 && Fit(2))) PhBreak();
                else { hq_PlanLoading_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanLoading_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (Ph(8))
        {
            GenRng r9 = SR(9);
            hq_PlanLoading_sel2 = 0;
            if (r9.Chance(300) && LooseFit(2)) hq_PlanLoading_sel2 = 1;
            hq_PlanLoading_r9 = r9;
        }
        if (Ph(9))
        {
            if (hq_PlanLoading_sel2 == 1)
            {
                GenRng r9 = hq_PlanLoading_r9;
                int first = nEmit;
                FSet(Dn("gen_broom"), Pick(ref r9, "s_bucket", 5, "t_bucketMetal", 5));
                hq_PlanLoading_r9 = r9;
                hq_PlanLoading_first = first;
            }
        }
        if (Ph(10))
        {
            if (hq_PlanLoading_sel2 == 1)
            {
                GenRng r9 = hq_PlanLoading_r9;
                int first = hq_PlanLoading_first;
                if (FormAt(GenData.GL_ROW, 70, GenData.A_WALL, WM_ANY, 0, 0, 0, ref r9) >= 0 && Keep(first, false)) MarkLoose(first);
                hq_PlanLoading_r9 = r9;
                hq_PlanLoading_first = first;
            }
        }
    }
    void PlanLoading_L1()
    {
        int room = pRoom;
        if (Ph(0))
        {
            int k = hq_PlanLoading_k;
            int first = nEmit;
            hq_PlanLoading_sel1 = 0;
            if (k % 2 == 0) hq_PlanLoading_sel1 = 1;
            else hq_PlanLoading_sel1 = 2;
            hq_PlanLoading_first = first;
        }
        if (Ph(1))
        {
            if (hq_PlanLoading_sel1 == 1)
            {
                string[] rows = hq_PlanLoading_rows;
                GenRng r10 = hq_PlanLoading_r10;
                int first = hq_PlanLoading_first;
                if (PlaceGroup(GenData.FindGrp(rows[r10.Range(rows.Length)]), room, GenData.A_WALL, r10.Between(2, 3), 0, ref r10)) Keep(first, false);
                hq_PlanLoading_rows = rows;
                hq_PlanLoading_r10 = r10;
                hq_PlanLoading_first = first;
            }
        }
        if (Ph(2))
        {
            if (hq_PlanLoading_sel1 == 2)
            {
                GenRng r10 = hq_PlanLoading_r10;
                int def = Pick(ref r10, "w_palletLoaded", 5, "w_pallet", 3, "s_boxLarge", 3);
                hq_PlanLoading_r10 = r10;
                hq_PlanLoading_def = def;
            }
        }
        if (Ph(3))
        {
            if (hq_PlanLoading_sel1 == 2)
            {
                GenRng r10 = hq_PlanLoading_r10;
                int first = hq_PlanLoading_first;
                int def = hq_PlanLoading_def;
                if (CountInRoom(def, room) < ScaledCap(GenData.defRoomCap[def], room) && WallPlace(def, WM_ANY, 0, 0, WIN_ANY, 0, ref r10) >= 0) Keep(first, false);
                hq_PlanLoading_r10 = r10;
                hq_PlanLoading_first = first;
                hq_PlanLoading_def = def;
            }
        }
    }


    // ============================================================================================ DINER
    // Seating: a counter with stools along a wall; booths along the window wall; tables in rows on the floor;
    // the entry (mat, coats, a plant); a waiting corner in a large room.
    void PlanDinerDining()
    {
        int room = pRoom, area = AreaM2(room);
        if (Ph(0))
        {
            GenRng r = SR(1);
            int dx = 0, dz = 0;
            MainDoor(room, out dx, out dz);
            // (b) the counter: two to four bar units along a wall, a stool at each, facing it
            int barDef = Dn("k_bar");
            int first0 = nEmit;
            rowN = area >= 60 ? 4 : (area >= 40 ? 3 : 2);
            for (int i = 0; i < rowN; i++) rowDef[i] = barDef;
            hq_PlanDinerDining_r = r;
            hq_PlanDinerDining_dx = dx;
            hq_PlanDinerDining_dz = dz;
            hq_PlanDinerDining_barDef = barDef;
            hq_PlanDinerDining_first0 = first0;
        }
        if (Ph(1))
        {
            GenRng r = hq_PlanDinerDining_r;
            int bar = WallRow(0, 2, WM_CENTRE, 0, 0, WIN_AVOID, EF_MANDATORY, ref r);
            hq_PlanDinerDining_sel1 = 0;
            if (bar >= 0) hq_PlanDinerDining_sel1 = 1;
            hq_PlanDinerDining_r = r;
            hq_PlanDinerDining_bar = bar;
        }
        if (Ph(2))
        {
            if (hq_PlanDinerDining_sel1 == 1)
            {
                GenRng r = hq_PlanDinerDining_r;
                int barDef = hq_PlanDinerDining_barDef;
                int bar = hq_PlanDinerDining_bar;
                int stool = Pick(ref r, "r_stool", 6, "k_stool", 4), last = nEmit, skip = r.Range(8);
                for (int e = bar; e < last; e++)
                {
                    if (emitDef[e] != barDef || emitParent[e] >= 0) continue;
                    if ((e - bar) == skip) continue;            // one stool is away now and then
                    Rel(stool, e, 0, Sz(barDef) / 2 + 90 + Sz(stool) / 2, 180, 0);
                }
                hq_PlanDinerDining_r = r;
                hq_PlanDinerDining_barDef = barDef;
                hq_PlanDinerDining_bar = bar;
            }
        }
        if (Ph(3))
        {
            if (hq_PlanDinerDining_sel1 == 1)
            {
                int first0 = hq_PlanDinerDining_first0;
                Keep(first0, true);
                hq_PlanDinerDining_first0 = first0;
            }
        }
        if (Ph(4))
        {
            // booths along a wall, at the windows if there are any: a table end-on to the wall, cushioned benches facing each other
            GenRng r2 = SR(2);
            int boothsPlaced = 0;
            int tableB = Dn("k_table"), benchDef = Dn("k_benchCushion");
            int boothW = Sz(tableB) + 2 * (60 + Sz(benchDef));
            if (r2.Chance(850) && Fit(5))
            {
                for (int n = area >= 48 ? 2 : 1; n >= 1; n--)
                {
                    int len = n * boothW + (n - 1) * 120;
                    if (!FindWallRun(room, len, Sx(tableB) + 40, 1, WM_CENTRE, 0, 0, WIN_PREFER, ref r2)) continue;
                    int d = wrDir, back = wrBack, start = wrCentre - len / 2;
                    for (int b = 0; b < n && Fit(5); b++)
                    {
                        int along = start + boothW / 2 + b * (boothW + 120);
                        int px, pz, rot;
                        WallPos(d, back, along, Sx(tableB), out px, out pz, out rot);
                        int first = nEmit;
                        int t = StampAt(tableB, room, px, pz, rot * 90 + 90, EF_BOOTH, -1, H_WALL, false);
                        if (t < 0) continue;
                        // benches along both long sides of the table, two to a side, facing it
                        for (int side = -1; side <= 1; side += 2)
                            for (int k = -1; k <= 1; k += 2)
                                Rel(benchDef, t, k * (Sx(benchDef) / 2 + 20), side * (Sz(tableB) / 2 + 60 + Sz(benchDef) / 2), side > 0 ? 180 : 0, 0);
                        if (Keep(first, false)) boothsPlaced++;
                    }
                    break;
                }
            }
            // floor tables on a lattice in the open part; one kind per room, a cell that is taken stays empty (C13)
            GenRng r3 = SR(3);
            hq_PlanDinerDining_boothsPlaced = boothsPlaced;
            hq_PlanDinerDining_r3 = r3;
        }
        if (Ph(5))
        {
            GenRng r3 = hq_PlanDinerDining_r3;
            int tDef = Pick(ref r3, "r_table", 6, "r_tableRound", 4);
            // the first table where the floor is most open; the others on its lattice, 3.5 m apart both ways: a table,
            // a chair each side and an aisle of a metre between the chair backs
            int tables = 0;
            hq_PlanDinerDining_r3 = r3;
            hq_PlanDinerDining_tDef = tDef;
            hq_PlanDinerDining_tables = tables;
        }
        if (PhLoop(6, 100000))
        {
            GenRng r3 = hq_PlanDinerDining_r3;
            int tDef = hq_PlanDinerDining_tDef;
            int t0 = 0;
            if (planIter == 0) { hq_CentrePiece_def = tDef; hq_CentrePiece_yaw = 0; hq_CentrePiece_bothYaws = false; hq_CentrePiece_ringWant = 2; hq_CentrePiece_ringMin = 1; hq_CentrePiece_mode = WM_NEAR; hq_CentrePiece_tx = RoomMidX(room); hq_CentrePiece_tz = RoomMidZ(room); hq_CentrePiece_need = 0; hq_CentrePiece_flags = EF_MANDATORY; hq_CentrePiece_rng = r3; PhSubReset(); }
            PhPush(); CentrePiece_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r3 = hq_CentrePiece_rng; t0 = hq_CentrePiece_ret; PhBreak(); }
            hq_PlanDinerDining_r3 = r3;
            hq_PlanDinerDining_tDef = tDef;
            hq_PlanDinerDining_t0 = t0;
        }
        if (Ph(7))
        {
            GenRng r3 = hq_PlanDinerDining_r3;
            int tDef = hq_PlanDinerDining_tDef;
            int t0 = hq_PlanDinerDining_t0;
            if (t0 < 0) { tDef = Dn("r_tableRound"); t0 = CentrePiece(tDef, 0, false, 2, 1, WM_ANY, 0, 0, 0, EF_MANDATORY, ref r3); }
            hq_PlanDinerDining_r3 = r3;
            hq_PlanDinerDining_tDef = tDef;
            hq_PlanDinerDining_t0 = t0;
        }
        if (Ph(8))
        {
            int boothsPlaced = hq_PlanDinerDining_boothsPlaced;
            int tDef = hq_PlanDinerDining_tDef;
            int tables = hq_PlanDinerDining_tables;
            int t0 = hq_PlanDinerDining_t0;
            if (t0 >= 0)
            {
                tables = 1;
                int bx = emitX[t0], bz = emitZ[t0], pitch = 7 * GenData.INNER_MM;
                for (int ring = 1; ring <= 3; ring++)
                    for (int j = -ring; j <= ring; j++)
                        for (int i = -ring; i <= ring; i++)
                        {
                            if ((Abs(i) != ring && Abs(j) != ring) || !Fit(3)) continue;
                            int px = bx + i * pitch, pz = bz + j * pitch;
                            int cx = FloorDiv(px, GenData.INNER_MM), cz = FloorDiv(pz, GenData.INNER_MM);
                            if (cx < 0 || cz < 0 || cx >= IW || cz >= IH || innerRoom[cz * IW + cx] != room) continue;
                            int first = nEmit;
                            if (FreePlaceAt(tDef, px, pz, 0, 1, 0, H_FREE) >= 0 && Keep(first, false)) tables++;
                        }
            }
            if (tables == 0 && boothsPlaced == 0) MandFail(1);   // a diner without any table to sit at
            hq_PlanDinerDining_boothsPlaced = boothsPlaced;
            hq_PlanDinerDining_tDef = tDef;
            hq_PlanDinerDining_tables = tables;
            hq_PlanDinerDining_t0 = t0;
        }
        if (Ph(9))
        {
            // the entry: a mat inside each outside door, coats in the nearest corner
            int ext = ExtDoor(room);
            for (int k = 0; k < roomDoorCount[room]; k++)
                if (roomDoorExt[roomDoorStart[room] + k] && Fit(1)) Doormat(room, k);
            GenRng r4 = SR(4);
            hq_PlanDinerDining_ext = ext;
            hq_PlanDinerDining_r4 = r4;
        }
        if (Ph(10))
        {
            int dx = hq_PlanDinerDining_dx;
            int dz = hq_PlanDinerDining_dz;
            int ext = hq_PlanDinerDining_ext;
            GenRng r4 = hq_PlanDinerDining_r4;
            if (ext >= 0 && r4.Chance(600) && Fit(1))
            {
                int first = nEmit;
                if (CornerPlace(Dn("k_coatRack"), WM_NEAR, dx, dz, false, 0, ref r4) >= 0) Keep(first, false);
            }
            // a waiting corner in a large room: couch against the wall, low table in front, a lamp beside
            GenRng r5 = SR(5);
            hq_PlanDinerDining_sel2 = 0;
            if (area >= 44 && r5.Chance(450) && Fit(3)) hq_PlanDinerDining_sel2 = 1;
            hq_PlanDinerDining_dx = dx;
            hq_PlanDinerDining_dz = dz;
            hq_PlanDinerDining_ext = ext;
            hq_PlanDinerDining_r4 = r4;
            hq_PlanDinerDining_r5 = r5;
        }
        if (Ph(11))
        {
            if (hq_PlanDinerDining_sel2 == 1)
            {
                int dx = hq_PlanDinerDining_dx;
                int dz = hq_PlanDinerDining_dz;
                GenRng r5 = hq_PlanDinerDining_r5;
                int first = nEmit;
                int couch = WallPlace(Dn("q_couch"), WM_NEAR, dx, dz, WIN_AVOID, 0, ref r5);
                hq_PlanDinerDining_dx = dx;
                hq_PlanDinerDining_dz = dz;
                hq_PlanDinerDining_r5 = r5;
                hq_PlanDinerDining_first = first;
                hq_PlanDinerDining_couch = couch;
            }
        }
        if (Ph(12))
        {
            if (hq_PlanDinerDining_sel2 == 1)
            {
                int first = hq_PlanDinerDining_first;
                int couch = hq_PlanDinerDining_couch;
                hq_PlanDinerDining_sel3 = 0;
                if (couch >= 0 && Keep(first, false)) hq_PlanDinerDining_sel3 = 1;
                hq_PlanDinerDining_first = first;
                hq_PlanDinerDining_couch = couch;
            }
        }
        if (Ph(13))
        {
            if (hq_PlanDinerDining_sel2 == 1 && hq_PlanDinerDining_sel3 == 1)
            {
                int first = hq_PlanDinerDining_first;
                int couch = hq_PlanDinerDining_couch;
                first = nEmit;
                int low = Dn("q_tableLow");
                if (Rel(low, couch, 0, Sz(emitDef[couch]) / 2 + 380 + Sz(low) / 2, 0, 0) >= 0) Keep(first, false);
                first = nEmit;
                hq_PlanDinerDining_first = first;
                hq_PlanDinerDining_couch = couch;
            }
        }
        if (Ph(14))
        {
            if (hq_PlanDinerDining_sel2 == 1 && hq_PlanDinerDining_sel3 == 1)
            {
                GenRng r5 = hq_PlanDinerDining_r5;
                int first = hq_PlanDinerDining_first;
                int couch = hq_PlanDinerDining_couch;
                if (Fit(1) && RelBeside(Dn("q_lampStanding"), couch, 0, 60, 0, ref r5) >= 0) Keep(first, false);
                hq_PlanDinerDining_r5 = r5;
                hq_PlanDinerDining_first = first;
                hq_PlanDinerDining_couch = couch;
            }
        }
        if (Ph(15))
        {
            CornerExtra(6, 800, "k_plant", 6, "q_cactus", 3, false);
        }
        if (Ph(16))
        {
            CornerExtra(7, 500, "k_plant", 5, "q_lampStanding", 4, false);
            GenRng r8 = SR(8);
            hq_PlanDinerDining_r8 = r8;
        }
        if (Ph(17))
        {
            int dx = hq_PlanDinerDining_dx;
            int dz = hq_PlanDinerDining_dz;
            GenRng r8 = hq_PlanDinerDining_r8;
            if (r8.Chance(450) && Fit(1))
            {
                int first = nEmit;
                if (WallPlace(Dn("k_trashcan"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r8) >= 0) Keep(first, false);
            }
            hq_PlanDinerDining_dx = dx;
            hq_PlanDinerDining_dz = dz;
            hq_PlanDinerDining_r8 = r8;
        }
        if (Ph(18))
        {
            GenRng r9 = SR(9);
            if (r9.Chance(400) && Fit(1))
            {
                int first = nEmit;
                if (WallPlace(Dn("q_cabinet"), WM_ANY, 0, 0, WIN_AVOID, 0, ref r9) >= 0) Keep(first, false);
            }
        }
    }

    // The kitchen line: sink and cooker side by side on one wall, the fridge at a wall near them and a worktop if
    // the room has the walls for them; crates by the door; the bin; mop and bucket.
    void PlanDinerKitchen()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            int sk = Dn("r_counterSink"), sv = Dn("r_stove");
            hp_PlanDinerKitchen_ct = Dn("r_counter");
            hp_PlanDinerKitchen_fr = Dn("r_fridge");
            int line = -1;
            hq_PlanDinerKitchen_r = r;
            hq_PlanDinerKitchen_sk = sk;
            hq_PlanDinerKitchen_sv = sv;
            hq_PlanDinerKitchen_line = line;
        }
        if (PhLoop(1, 100000))
        {
            int line = hq_PlanDinerKitchen_line;
            if (!PhSubActive())
            {
                int n = 3 - planIter;
                if (!(n >= 2 && line < 0)) PhBreak();
                else { hq_PlanDinerKitchen_n = n; PhSubBegin(); }
            }
            hq_PlanDinerKitchen_line = line;
            if (PhSubActive())
            {
                PhPush(); PlanDinerKitchen_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (Ph(2))
        {
            GenRng r = hq_PlanDinerKitchen_r;
            int sk = hq_PlanDinerKitchen_sk;
            int sv = hq_PlanDinerKitchen_sv;
            int line = hq_PlanDinerKitchen_line;
            hp_PlanDinerKitchen_lx = RoomMidX(room);
            hp_PlanDinerKitchen_lz = RoomMidZ(room);
            bool haveS = line >= 0, haveV = line >= 0;
            if (line >= 0) { hp_PlanDinerKitchen_lx = emitX[line]; hp_PlanDinerKitchen_lz = emitZ[line]; }
            else
            {
                // no wall takes both: each on its own stretch
                int e = WallKeep(sk, WM_ANY, 0, 0, WIN_ANY, EF_MANDATORY, ref r);
                if (e >= 0) { haveS = true; hp_PlanDinerKitchen_lx = emitX[e]; hp_PlanDinerKitchen_lz = emitZ[e]; }
                e = WallKeep(sv, WM_NEAR, hp_PlanDinerKitchen_lx, hp_PlanDinerKitchen_lz, WIN_AVOID, EF_MANDATORY, ref r);
                if (e >= 0) haveV = true;
            }
            hq_PlanDinerKitchen_r = r;
            hq_PlanDinerKitchen_sk = sk;
            hq_PlanDinerKitchen_sv = sv;
            hq_PlanDinerKitchen_line = line;
            hq_PlanDinerKitchen_haveS = haveS;
            hq_PlanDinerKitchen_haveV = haveV;
        }
        if (Ph(3))
        {
            GenRng r = hq_PlanDinerKitchen_r;
            bool haveS = hq_PlanDinerKitchen_haveS;
            // no wall has 1.4 m for the diner's units: the narrower domestic sink and stove
            if (!haveS) { int e = WallKeep(Dn("k_sink"), WM_NEAR, hp_PlanDinerKitchen_lx, hp_PlanDinerKitchen_lz, WIN_ANY, EF_MANDATORY, ref r); if (e >= 0) { haveS = true; hp_PlanDinerKitchen_lx = emitX[e]; hp_PlanDinerKitchen_lz = emitZ[e]; } }
            hq_PlanDinerKitchen_r = r;
            hq_PlanDinerKitchen_haveS = haveS;
        }
        if (Ph(4))
        {
            GenRng r = hq_PlanDinerKitchen_r;
            bool haveS = hq_PlanDinerKitchen_haveS;
            bool haveV = hq_PlanDinerKitchen_haveV;
            if (!haveV) haveV = WallKeep(Dn("k_stove"), WM_NEAR, hp_PlanDinerKitchen_lx, hp_PlanDinerKitchen_lz, WIN_AVOID, EF_MANDATORY, ref r) >= 0;
            if (!haveS) MandFail(1);
            if (!haveV) MandFail(1);
            hq_PlanDinerKitchen_r = r;
            hq_PlanDinerKitchen_haveS = haveS;
            hq_PlanDinerKitchen_haveV = haveV;
        }
        if (Ph(5))
        {
            GenRng r2 = SR(2);
            // the fridge near the line if a wall has the room: these units are 1.4 m wide and up to 1.6 m deep, and a
            // kitchen of 4 x 4 m with two doors often has no third place with its front free (the store room has one);
            // then the narrower domestic fridge, and a narrower cabinet for the counter
            if (WallKeep(hp_PlanDinerKitchen_fr, WM_NEAR, hp_PlanDinerKitchen_lx, hp_PlanDinerKitchen_lz, WIN_NEVER, 0, ref r2) < 0 && WallKeep(hp_PlanDinerKitchen_fr, WM_ANY, 0, 0, WIN_ANY, 0, ref r2) < 0)
                WallAny(WM_NEAR, hp_PlanDinerKitchen_lx, hp_PlanDinerKitchen_lz, WIN_ANY, 0, ref r2, "k_fridge", null, null);
        }
        if (Ph(6))
        {
            GenRng r3 = SR(3);
            if (r3.Chance(800) && Fit(1) && WallKeep(hp_PlanDinerKitchen_ct, WM_NEAR, hp_PlanDinerKitchen_lx, hp_PlanDinerKitchen_lz, WIN_ANY, 0, ref r3) < 0)
                WallAny(WM_NEAR, hp_PlanDinerKitchen_lx, hp_PlanDinerKitchen_lz, WIN_ANY, 0, ref r3, "k_counterDrawer", "k_counter", null);
        }
        if (Ph(7))
        {
            GenRng r4 = SR(4);
            int dx = 0, dz = 0;
            MainDoor(room, out dx, out dz);
            hq_PlanDinerKitchen_r4 = r4;
            hq_PlanDinerKitchen_dx = dx;
            hq_PlanDinerKitchen_dz = dz;
        }
        if (Ph(8))
        {
            GenRng r4 = hq_PlanDinerKitchen_r4;
            int dx = hq_PlanDinerKitchen_dx;
            int dz = hq_PlanDinerKitchen_dz;
            if (r4.Chance(700) && Fit(2))
            {
                int first = nEmit;
                if (GroupAt(GenData.FindGrp("crates"), GenData.A_WALL, r4.Between(2, 3), WM_NEAR, dx, dz, 0, ref r4)) Keep(first, false);
            }
            GenRng r5 = SR(5);
            hq_PlanDinerKitchen_r4 = r4;
            hq_PlanDinerKitchen_dx = dx;
            hq_PlanDinerKitchen_dz = dz;
            hq_PlanDinerKitchen_r5 = r5;
        }
        if (Ph(9))
        {
            GenRng r5 = hq_PlanDinerKitchen_r5;
            if (r5.Chance(700) && Fit(1))
            {
                int first = nEmit;
                if (WallPlace(Dn("k_trashcan"), WM_NEAR, hp_PlanDinerKitchen_lx, hp_PlanDinerKitchen_lz, WIN_ANY, 0, ref r5) >= 0) Keep(first, false);
            }
            GenRng r6 = SR(6);
            hq_PlanDinerKitchen_r5 = r5;
            hq_PlanDinerKitchen_r6 = r6;
        }
        if (Ph(10))
        {
            GenRng r6 = hq_PlanDinerKitchen_r6;
            hq_PlanDinerKitchen_sel1 = 0;
            if (r6.Chance(380) && LooseFit(2)) hq_PlanDinerKitchen_sel1 = 1;
            hq_PlanDinerKitchen_r6 = r6;
        }
        if (Ph(11))
        {
            if (hq_PlanDinerKitchen_sel1 == 1)
            {
                GenRng r6 = hq_PlanDinerKitchen_r6;
                int first = nEmit;
                FSet(Dn("gen_mop"), Pick(ref r6, "s_bucket", 5, "t_bucketMetal", 5));
                hq_PlanDinerKitchen_r6 = r6;
                hq_PlanDinerKitchen_first = first;
            }
        }
        if (Ph(12))
        {
            if (hq_PlanDinerKitchen_sel1 == 1)
            {
                int dx = hq_PlanDinerKitchen_dx;
                int dz = hq_PlanDinerKitchen_dz;
                GenRng r6 = hq_PlanDinerKitchen_r6;
                int first = hq_PlanDinerKitchen_first;
                if (FormAt(GenData.GL_ROW, 70, GenData.A_WALL, WM_NEAR, dx, dz, 0, ref r6) >= 0 && Keep(first, false)) MarkLoose(first);
                hq_PlanDinerKitchen_dx = dx;
                hq_PlanDinerKitchen_dz = dz;
                hq_PlanDinerKitchen_r6 = r6;
                hq_PlanDinerKitchen_first = first;
            }
        }
    }
    void PlanDinerKitchen_L1()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = hq_PlanDinerKitchen_r;
            int sk = hq_PlanDinerKitchen_sk;
            int sv = hq_PlanDinerKitchen_sv;
            int line = hq_PlanDinerKitchen_line;
            int n = hq_PlanDinerKitchen_n;
            rowN = 0;
            rowDef[rowN++] = sk;
            if (n == 3) rowDef[rowN++] = hp_PlanDinerKitchen_ct;
            rowDef[rowN++] = sv;
            if (r.Chance(500)) { int tmp = rowDef[0]; rowDef[0] = rowDef[rowN - 1]; rowDef[rowN - 1] = tmp; }
            int first = nEmit;
            line = WallRow(0, 2, WM_CORNER, 0, 0, WIN_ANY, EF_MANDATORY, ref r);
            hq_PlanDinerKitchen_r = r;
            hq_PlanDinerKitchen_sk = sk;
            hq_PlanDinerKitchen_sv = sv;
            hq_PlanDinerKitchen_line = line;
            hq_PlanDinerKitchen_first = first;
        }
        if (Ph(1))
        {
            int line = hq_PlanDinerKitchen_line;
            int first = hq_PlanDinerKitchen_first;
            if (line >= 0 && !Keep(first, true)) line = -1;
            hq_PlanDinerKitchen_line = line;
            hq_PlanDinerKitchen_first = first;
        }
    }


    // The store room: pantry shelving along a wall; crates in a row; a pallet; barrels, boxes and sacks at the walls.
    void PlanDinerStore()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            int first0 = nEmit;
            rowN = Fit(4) && r.Chance(600) ? 2 : 1;
            for (int i = 0; i < rowN; i++) rowDef[i] = Dn("gen_rackPantry");
            int shelf = WallRow(0, 2, WM_CENTRE, 0, 0, WIN_NEVER, EF_MANDATORY, ref r);
            hq_PlanDinerStore_r = r;
            hq_PlanDinerStore_first0 = first0;
            hq_PlanDinerStore_shelf = shelf;
        }
        if (Ph(1))
        {
            GenRng r = hq_PlanDinerStore_r;
            int shelf = hq_PlanDinerStore_shelf;
            if (shelf < 0) { rowN = 1; shelf = WallRow(0, 1, WM_ANY, 0, 0, WIN_NEVER, EF_MANDATORY, ref r); }
            hq_PlanDinerStore_r = r;
            hq_PlanDinerStore_shelf = shelf;
        }
        if (Ph(2))
        {
            int first0 = hq_PlanDinerStore_first0;
            int shelf = hq_PlanDinerStore_shelf;
            if (shelf >= 0 && !Keep(first0, true)) shelf = -1;
            if (shelf < 0) MandFail(1);
            string[] rows = { "crates", "sacks", "barrels", "boxes" };
            int[] chance = { 950, 600, 550, 600 };
            hq_PlanDinerStore_first0 = first0;
            hq_PlanDinerStore_shelf = shelf;
            hq_PlanDinerStore_rows = rows;
            hq_PlanDinerStore_chance = chance;
        }
        if (PhLoop(3, 100000))
        {
            string[] rows = hq_PlanDinerStore_rows;
            int[] chance = hq_PlanDinerStore_chance;
            int k = planIter;
            if (!(k < rows.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(2 + k);
                if (!rk.Chance(chance[k]) || !Fit(2)) continue;
                int g = GenData.FindGrp(rows[k]), first = nEmit;
                if (PlaceGroup(g, room, k <= 1 ? GenData.A_BESIDE : GenData.A_WALL, rk.Between(2, Fit(3) ? 3 : 2), 0, ref rk)) Keep(first, false);
            }
            hq_PlanDinerStore_rows = rows;
            hq_PlanDinerStore_chance = chance;
        }
        if (Ph(4))
        {
            GenRng r6 = SR(6);
            if (r6.Chance(500) && Fit(1))
            {
                int first = nEmit;
                if (WallPlace(Dn("w_pallet"), WM_CORNER, 0, 0, WIN_ANY, 0, ref r6) >= 0) Keep(first, false);
            }
        }
        if (Ph(5))
        {
            GenRng r7 = SR(7);
            if (r7.Chance(450) && Fit(1))
            {
                int first = nEmit;
                if (WallPlace(Dn("r_fridge"), WM_ANY, 0, 0, WIN_NEVER, 0, ref r7) >= 0) Keep(first, false);
            }
        }
    }

    // phase state of the split plans (Tools: autophase2)
    int hp_PlanDinerKitchen_ct;
    int hp_PlanDinerKitchen_fr;
    int hp_PlanDinerKitchen_lx;
    int hp_PlanDinerKitchen_lz;

    // locals of the split plans and helpers, kept from one phase to the next (autophase4)
    int hq_PalletBlock_cell;
    int hq_PalletBlock_cols;
    int hq_PalletBlock_first;
    int hq_PalletBlock_ret;
    GenRng hq_PalletBlock_rng;
    int hq_PalletBlock_rows;
    int hq_PalletBlock_set;
    int hq_PalletBlock_yaw;
    int hq_PlanDinerDining_bar;
    int hq_PlanDinerDining_barDef;
    int hq_PlanDinerDining_boothsPlaced;
    int hq_PlanDinerDining_couch;
    int hq_PlanDinerDining_dx;
    int hq_PlanDinerDining_dz;
    int hq_PlanDinerDining_ext;
    int hq_PlanDinerDining_first;
    int hq_PlanDinerDining_first0;
    GenRng hq_PlanDinerDining_r;
    GenRng hq_PlanDinerDining_r3;
    GenRng hq_PlanDinerDining_r4;
    GenRng hq_PlanDinerDining_r5;
    GenRng hq_PlanDinerDining_r8;
    int hq_PlanDinerDining_sel1;
    int hq_PlanDinerDining_sel2;
    int hq_PlanDinerDining_sel3;
    int hq_PlanDinerDining_t0;
    int hq_PlanDinerDining_tDef;
    int hq_PlanDinerDining_tables;
    int hq_PlanDinerKitchen_dx;
    int hq_PlanDinerKitchen_dz;
    int hq_PlanDinerKitchen_first;
    bool hq_PlanDinerKitchen_haveS;
    bool hq_PlanDinerKitchen_haveV;
    int hq_PlanDinerKitchen_line;
    int hq_PlanDinerKitchen_n;
    GenRng hq_PlanDinerKitchen_r;
    GenRng hq_PlanDinerKitchen_r4;
    GenRng hq_PlanDinerKitchen_r5;
    GenRng hq_PlanDinerKitchen_r6;
    int hq_PlanDinerKitchen_sel1;
    int hq_PlanDinerKitchen_sk;
    int hq_PlanDinerKitchen_sv;
    int[] hq_PlanDinerStore_chance;
    int hq_PlanDinerStore_first0;
    GenRng hq_PlanDinerStore_r;
    string[] hq_PlanDinerStore_rows;
    int hq_PlanDinerStore_shelf;
    int hq_PlanLoading_cell;
    int[] hq_PlanLoading_chance;
    int hq_PlanLoading_def;
    int hq_PlanLoading_dx;
    int hq_PlanLoading_dz;
    int hq_PlanLoading_first;
    int hq_PlanLoading_k;
    int hq_PlanLoading_lane;
    GenRng hq_PlanLoading_r;
    GenRng hq_PlanLoading_r10;
    GenRng hq_PlanLoading_r2;
    GenRng hq_PlanLoading_r8;
    GenRng hq_PlanLoading_r9;
    string[] hq_PlanLoading_rows;
    int hq_PlanLoading_sel1;
    int hq_PlanLoading_sel2;
    int hq_PlanStorage_cell;
    int[] hq_PlanStorage_chance;
    int hq_PlanStorage_dx;
    int hq_PlanStorage_dz;
    int hq_PlanStorage_first;
    int hq_PlanStorage_first0;
    int hq_PlanStorage_maxRows;
    GenRng hq_PlanStorage_r;
    GenRng hq_PlanStorage_r16;
    GenRng hq_PlanStorage_r17;
    GenRng hq_PlanStorage_r2;
    GenRng hq_PlanStorage_r3;
    GenRng hq_PlanStorage_r4;
    int hq_PlanStorage_rack;
    string[] hq_PlanStorage_rows;
    int hq_PlanStorage_sel1;
    int hq_PlanStorage_sel2;
    int hq_PlanStorage_sel3;
    int hq_PlanStorage_sel4;
    int hq_PlanStorage_sel5;
    string[] hq_PlanStorage_stacks;
    int hq_PlanStorage_wb;
}
