// MapGen, revision 10 plans: the farm's workshop and the park's feature areas (G58: each new kind as rich as the
// original places). The park's lawn is PlanPark (MapGenPlansNew.cs); behind it stand a skate corner, a pond and a
// cafe terrace, a column each.
public partial class MapGen
{
    // ============================================================================================ FARM WORKSHOP
    // Where the farm's machines are mended: the workbench with tools on it, the anvil and the grindstone, hand tools
    // leaning at the walls, a tractor wheel and tyres, fuel, a rack of spares, rope and the lantern.
    void PlanFarmWork()
    {
        planMax = 11;
        int room = pRoom;
        if (planPhase == 0)
        {
            int dx, dz;
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            hp_PlanFarmWork_first = nEmit;
            hp_PlanFarmWork_wb = WallPlace(Dn("s_workbench"), WM_FAR, dx, dz, WIN_PREFER, EF_MANDATORY, ref r);
        }
        if (planPhase == 1)
        {
            if (hp_PlanFarmWork_wb < 0 || !Keep(hp_PlanFarmWork_first, true))
            {
                hp_PlanFarmWork_wb = -1;
                MandFail(1);
            }
        }
        if (planPhase == 2)
        {
            GenRng r2 = SR(2);
            hq_PlanFarmWork_sel1 = 0;
            if (Fit(1)) hq_PlanFarmWork_sel1 = 1;
            hq_PlanFarmWork_r2 = r2;
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (!(hq_PlanFarmWork_sel1 == 1)) PhBreak();
            else
            {
                GenRng r2 = hq_PlanFarmWork_r2;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r2; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r2 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PlanFarmWork_r2 = r2;
                hq_PlanFarmWork_cell = cell;
            }
        }
        if (planPhase == 4)
        {
            if (hq_PlanFarmWork_sel1 == 1)
            {
                GenRng r2 = hq_PlanFarmWork_r2;
                int cell = hq_PlanFarmWork_cell;
                hp_PlanFarmWork_first = nEmit;
                hq_PlanFarmWork_sel2 = 0;
                if (cell >= 0 && FreePlace(Dn("g_anvil"), cell, r2.Range(4) * 90, 1, 0, H_FREE) >= 0) hq_PlanFarmWork_sel2 = 1;
                hq_PlanFarmWork_r2 = r2;
                hq_PlanFarmWork_cell = cell;
            }
        }
        if (planPhase == 5 && PhLoop(5, 100000))
        {
            if (!(hq_PlanFarmWork_sel1 == 1 && hq_PlanFarmWork_sel2 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_Keep_first = hp_PlanFarmWork_first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
        if (planPhase == 6)
        {
            GenRng r3 = SR(3);
            if (r3.Chance(700) && Fit(1)) { hp_PlanFarmWork_first = nEmit; if (WallPlace(Dn("g_grindstone"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0) Keep(hp_PlanFarmWork_first, false); }
            hp_PlanFarmWork_rows = new string[] { "handtools", "smallTools", "tyres", "fuel", "boxes" };
            hp_PlanFarmWork_chance = new int[] { 950, 800, 650, 500, 400 };
        }
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < hp_PlanFarmWork_rows.Length)) PhBreak();
                else { hq_PlanFarmWork_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanFarmWork_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 8)
        {
            GenRng r9 = SR(9);
            if (r9.Chance(650) && Fit(1)) { hp_PlanFarmWork_first = nEmit; if (WallPlace(Pick(ref r9, "c_tractorWheel", 5, "f_wheel", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r9) >= 0) Keep(hp_PlanFarmWork_first, false); }
        }
        if (planPhase == 9)
        {
            GenRng r10 = SR(10);
            if (r10.Chance(550) && Fit(1)) { hp_PlanFarmWork_first = nEmit; if (WallPlace(Dn("gen_rack"), WM_ANY, 0, 0, WIN_NEVER, 0, ref r10) >= 0) Keep(hp_PlanFarmWork_first, false); }
        }
        if (planPhase == 10)
        {
            GenRng r11 = SR(11);
            if (r11.Chance(400) && Fit(1)) { hp_PlanFarmWork_first = nEmit; if (WallPlace(Pick(ref r11, "w_planks", 4, "w_logs", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r11) >= 0) Keep(hp_PlanFarmWork_first, false); }
    
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 11 && PhLoop(11, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "gen_toolwall", "gen_rack", "ms_handTruck", "w_planks", "w_logs", "c_tractorWheel", "f_wheel", "g_grindstone", "g_anvil", "dg_barrel" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }
    void PlanFarmWork_L1()
    {
        int room = pRoom;
        planMax = 2;
        if (planPhase == 0)
        {
            int k = hq_PlanFarmWork_k;
            GenRng rk = SR(4 + k);
            if (!rk.Chance(hp_PlanFarmWork_chance[k]) || !Fit(2)) { hq_PlanFarmWork_rk = rk; PhEnd(); return; }
            hp_PlanFarmWork_first = nEmit;
            int g = GenData.FindGrp(hp_PlanFarmWork_rows[k]);
            bool ok = k < 2 && hp_PlanFarmWork_wb >= 0 ? GroupBeside(g, hp_PlanFarmWork_wb, rk.Between(2, 3), 0, ref rk) : false;
            hq_PlanFarmWork_rk = rk;
            hq_PlanFarmWork_g = g;
            hq_PlanFarmWork_ok = ok;
        }
        if (planPhase == 1)
        {
            GenRng rk = hq_PlanFarmWork_rk;
            int g = hq_PlanFarmWork_g;
            bool ok = hq_PlanFarmWork_ok;
            if (!ok) ok = PlaceGroup(g, room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk);
            hq_PlanFarmWork_sel3 = 0;
            if (ok) hq_PlanFarmWork_sel3 = 1;
            hq_PlanFarmWork_rk = rk;
            hq_PlanFarmWork_g = g;
            hq_PlanFarmWork_ok = ok;
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (!(hq_PlanFarmWork_sel3 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_Keep_first = hp_PlanFarmWork_first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
    }


    // ============================================================================================ SKATE CORNER
    // A paved corner of the park with ramps: a half-pipe or a ramp at one end, a rail and a box in the middle, a
    // bench to watch from, a scooter and boards left lying, the bin and a lamp, shrubs at the fence.
    void PlanPlayground()
    {
        planMax = 21;
        int room = pRoom;
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            int axis = RoomLongAxis(room) == 0 ? 90 : 0;
            string[] big = { "sk_halfPipe", "sk_halfPipeLow", "sk_ramp", "sk_rampRail", "sk_box", "sk_steps", "sk_rail", "sk_grind", "sk_railLow", "sk_platform" };
            int s0 = r.Range(big.Length), got = 0;
            hq_PlanPlayground_r = r;
            hq_PlanPlayground_axis = axis;
            hq_PlanPlayground_big = big;
            hq_PlanPlayground_s0 = s0;
            hq_PlanPlayground_got = got;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            string[] big = hq_PlanPlayground_big;
            int got = hq_PlanPlayground_got;
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < big.Length && Fit(1) && got < 3 + AreaM2(room) / 12)) PhBreak();
                else { hq_PlanPlayground_k = k; PhSubBegin(); }
            }
            hq_PlanPlayground_big = big;
            hq_PlanPlayground_got = got;
            if (PhSubActive())
            {
                PhPush(); PlanPlayground_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 2)
        {
            int got = hq_PlanPlayground_got;
            if (got == 0) MandFail(1);
            hq_PlanPlayground_got = got;
        }
        if (planPhase == 3)
        {
            GenRng r2 = SR(2);
            if (Fit(1)) { int first = nEmit; if (WallPlace(Pick(ref r2, "sk_benches", 5, "ck_bench", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r2) >= 0) Keep(first, false); }
        }
        if (planPhase == 4)
        {
            GenRng r3 = SR(3);
            hq_PlanPlayground_r3 = r3;
        }
        // the scooter and a board leaning at the fence (a loose thing never lies alone in the open)
        if (planPhase == 5 && PhLoop(5, 100000))
        {
            GenRng r3 = hq_PlanPlayground_r3;
            int k = planIter;
            if (!(k < 2 && LooseFit(1))) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                int first = nEmit;
                if (WallPlace(Dn(k == 0 ? "sk_scooter" : "sk_board"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0 && Keep(first, false)) MarkLoose(first);
            }
            hq_PlanPlayground_r3 = r3;
        }
        if (planPhase == 6)
        {
            GenRng r4 = SR(4);
            if (r4.Chance(700) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ck_trash"), WM_ANY, 0, 0, WIN_ANY, 0, ref r4) >= 0) Keep(first, false); }
        }
        if (planPhase == 7)
        {
            GenRng r5 = SR(5);
            if (Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("shrubs"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r5)) Keep(first, false); }
        }
        if (planPhase == 8)
        {
            GenRng r6 = SR(6);
            hq_PlanPlayground_sel1 = 0;
            if (r6.Chance(600) && Fit(1)) hq_PlanPlayground_sel1 = 1;
            hq_PlanPlayground_r6 = r6;
        }
        if (planPhase == 9 && PhLoop(9, 100000))
        {
            if (!(hq_PlanPlayground_sel1 == 1)) PhBreak();
            else
            {
                GenRng r6 = hq_PlanPlayground_r6;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 1; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r6; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r6 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PlanPlayground_r6 = r6;
                hq_PlanPlayground_cell = cell;
            }
        }
        if (planPhase == 10)
        {
            if (hq_PlanPlayground_sel1 == 1)
            {
                int cell = hq_PlanPlayground_cell;
                int first = nEmit;
                hq_PlanPlayground_sel2 = 0;
                if (cell >= 0 && FreePlace(Dn("f_lamp"), cell, 0, 0, 0, H_FREE) >= 0) hq_PlanPlayground_sel2 = 1;
                hq_PlanPlayground_cell = cell;
                hq_PlanPlayground_first = first;
            }
        }
        if (planPhase == 11 && PhLoop(11, 100000))
        {
            if (!(hq_PlanPlayground_sel1 == 1 && hq_PlanPlayground_sel2 == 1)) PhBreak();
            else
            {
                int first = hq_PlanPlayground_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanPlayground_first = first;
            }
        }
        if (planPhase == 12)
        {
            GenRng r7 = SR(7);
            if (Fit(1)) { int first = nEmit; if (WallPlace(Dn(r7.Chance(500) ? "k_bench" : "ck_bench"), WM_ANY, 0, 0, WIN_ANY, 0, ref r7) >= 0) Keep(first, false); }
        }
        if (planPhase == 13)
        {
            GenRng r8 = SR(8);
            if (r8.Chance(450) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ck_drinks"), WM_ANY, 0, 0, WIN_ANY, 0, ref r8) >= 0) Keep(first, false); }
        }
        if (planPhase == 14)
        {
            GenRng r9 = SR(9);
            hq_PlanPlayground_sel3 = 0;
            if (r9.Chance(600) && Fit(2)) hq_PlanPlayground_sel3 = 1;
            hq_PlanPlayground_r9 = r9;
        }
        if (planPhase == 15)
        {
            if (hq_PlanPlayground_sel3 == 1)
            {
                GenRng r9 = hq_PlanPlayground_r9;
                int first = nEmit;
                FSet(Dn("c_cone"), Dn("c_cone"));
                hq_PlanPlayground_sel4 = 0;
                if (FormAt(GenData.GL_ROW, 500, GenData.A_OUT_OPEN, WM_ANY, 0, 0, 0, ref r9) >= 0) hq_PlanPlayground_sel4 = 1;
                hq_PlanPlayground_r9 = r9;
                hq_PlanPlayground_first = first;
            }
        }
        if (planPhase == 16 && PhLoop(16, 100000))
        {
            if (!(hq_PlanPlayground_sel3 == 1 && hq_PlanPlayground_sel4 == 1)) PhBreak();
            else
            {
                int first = hq_PlanPlayground_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanPlayground_first = first;
            }
        }
        if (planPhase == 17)
        {
            GenRng r10 = SR(10);
            if (r10.Chance(500) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("pots"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r10)) Keep(first, false); }
            // revision 11: a pallet and a plank left by the ramps (things the skaters build with)
        }
        if (planPhase == 18)
        {
            GenRng r11 = SR(11);
            if (r11.Chance(500) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("sk_pallet"), WM_ANY, 0, 0, WIN_ANY, 0, ref r11) >= 0) Keep(first, false); }
        }
        if (planPhase == 19 && PhLoop(19, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "sk_benches", "ck_bench", "k_bench", "ck_trash", "c_cone", "f_lamp", "sk_pallet", "ck_drinks", "n_bush", "sk_railLow", "sk_grind", "hk_benchShort", "hk_lantern", "n_planter" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 20)
        {
            ClearBans();
        }
        if (planPhase == 21 && PhLoop(21, 100000))
        {
            if (planIter == 0) { hq_Borders_k0 = 30; hq_Borders_max = 3; hq_Borders_soil = true; PhSubReset(); }
            PhPush(); Borders_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }
    void PlanPlayground_L1()
    {
        int room = pRoom;
        planMax = 1;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            GenRng r = hq_PlanPlayground_r;
            int cell = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
            hq_PlanPlayground_r = r;
            hq_PlanPlayground_cell = cell;
        }
        if (planPhase == 1)
        {
            GenRng r = hq_PlanPlayground_r;
            int axis = hq_PlanPlayground_axis;
            string[] big = hq_PlanPlayground_big;
            int s0 = hq_PlanPlayground_s0;
            int got = hq_PlanPlayground_got;
            int k = hq_PlanPlayground_k;
            int cell = hq_PlanPlayground_cell;
            if (cell < 0) { hq_PlanPlayground_r = r; hq_PlanPlayground_axis = axis; hq_PlanPlayground_big = big; hq_PlanPlayground_s0 = s0; hq_PlanPlayground_got = got; hq_PlanPlayground_cell = cell; phBreak = true; PhEnd(); return; }
            int first = nEmit;
            if (FreePlace(Dn(big[(s0 + k) % big.Length]), cell, axis + (r.Chance(500) ? 180 : 0), 1, 0, H_FREE) >= 0 && Keep(first, false))
            {
                got++;
            }
            hq_PlanPlayground_r = r;
            hq_PlanPlayground_axis = axis;
            hq_PlanPlayground_big = big;
            hq_PlanPlayground_s0 = s0;
            hq_PlanPlayground_got = got;
            hq_PlanPlayground_cell = cell;
        }
    }


    // ============================================================================================ POND
    // Water in the middle of the area with a grass bank round it: ducks and lily pads on the water, a footbridge
    // across it, a canoe pulled up on the bank, rocks and shrubs at the edge, reeds, a bench to look at it, a tree.
    void PlanPond()
    {
        int room = pRoom;
        planMax = 10;
        if (planPhase == 0)
        {
            // the water: every cell of the area at least 1.5 m from its edge
            int water = 0, cx = 0, cz = 0;
            for (int k = roomCellStart[room]; k < roomCellStart[room] + roomCellCount[room]; k++)
            {
                int i = roomCells[k], x = i % IW, z = i / IW;
                bool inner = true;
                for (int dz = -3; dz <= 3 && inner; dz++)
                    for (int dx = -3; dx <= 3 && inner; dx++)
                    {
                        int jx = x + dx, jz = z + dz;
                        if (jx < 0 || jz < 0 || jx >= IW || jz >= IH || innerRoom[jz * IW + jx] != room) inner = false;
                    }
                if (!inner || (tags[i] & (GenData.T_PATH | GenData.T_DOOR_CLEAR)) != 0) continue;
                tags[i] |= GenData.T_WATER; water++; cx += x; cz += z;
            }
            hq_PlanPond_sel1 = 0;
            if (water == 0) hq_PlanPond_sel1 = 1;
            hq_PlanPond_water = water;
            hq_PlanPond_cx = cx;
            hq_PlanPond_cz = cz;
        }
        if (planPhase == 1)
        {
            if (hq_PlanPond_sel1 == 1)
            {
                MandFail(1);
            }
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (!(hq_PlanPond_sel1 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_PondBank_room = room; hq_PondBank_wx = RoomMidX(room); hq_PondBank_wz = RoomMidZ(room); PhSubReset(); }
                PhPush(); PondBank_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
        if (planPhase == 3)
        {
            if (hq_PlanPond_sel1 == 1)
            {
                { PhEnd(); return; }
            }
        }
        if (planPhase == 4)
        {
            int water = hq_PlanPond_water;
            int cx = hq_PlanPond_cx;
            int cz = hq_PlanPond_cz;
            int wx = cx * GenData.INNER_MM / water + 250, wz = cz * GenData.INNER_MM / water + 250;
            // on the water first: the footbridge, the ducks, the lilies
            GenRng r = SR(1);
            hq_PlanPond_sel2 = 0;
            if (water >= 12 && Fit(1)) hq_PlanPond_sel2 = 1;
            hq_PlanPond_water = water;
            hq_PlanPond_cx = cx;
            hq_PlanPond_cz = cz;
            hq_PlanPond_wx = wx;
            hq_PlanPond_wz = wz;
        }
        if (planPhase == 5)
        {
            if (hq_PlanPond_sel2 == 1)
            {
                int wx = hq_PlanPond_wx;
                int wz = hq_PlanPond_wz;
                int first = nEmit;
                hq_PlanPond_sel3 = 0;
                if (FreePlaceAt(Dn("n_footbridge"), wx, wz, RoomLongAxis(room) == 0 ? 0 : 90, 0, 0, H_GROUND) >= 0) hq_PlanPond_sel3 = 1;
                hq_PlanPond_wx = wx;
                hq_PlanPond_wz = wz;
                hq_PlanPond_first = first;
            }
        }
        if (planPhase == 6 && PhLoop(6, 100000))
        {
            if (!(hq_PlanPond_sel2 == 1 && hq_PlanPond_sel3 == 1)) PhBreak();
            else
            {
                int first = hq_PlanPond_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanPond_first = first;
            }
        }
        if (planPhase == 7)
        {
            int wx = hq_PlanPond_wx;
            int wz = hq_PlanPond_wz;
            GenRng r2 = SR(2);
            if (Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("ducks"), GenData.A_OUT_OPEN, r2.Between(2, 4), WM_NEAR, wx, wz, 0, ref r2)) Keep(first, false); }
            GenRng r3 = SR(3);
            hq_PlanPond_wx = wx;
            hq_PlanPond_wz = wz;
            hq_PlanPond_r3 = r3;
        }
        if (planPhase == 8 && PhLoop(8, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 2 && Fit(1))) PhBreak();
                else { hq_PlanPond_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanPond_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 9)
        {
            // then nothing else stands on the water
            for (int k = roomCellStart[room]; k < roomCellStart[room] + roomCellCount[room]; k++)
                if ((tags[roomCells[k]] & GenData.T_WATER) != 0) tags[roomCells[k]] |= GenData.T_NO_SPAWN;
        }
        if (planPhase == 10 && PhLoop(10, 100000))
        {
            int wx = hq_PlanPond_wx;
            int wz = hq_PlanPond_wz;
            if (planIter == 0) { hq_PondBank_room = room; hq_PondBank_wx = wx; hq_PondBank_wz = wz; PhSubReset(); }
            PhPush(); PondBank_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
            hq_PlanPond_wx = wx;
            hq_PlanPond_wz = wz;
        }
    }
    void PlanPond_L1()
    {
        int room = pRoom;
        planMax = 1;
        if (planPhase == 0)
        {
            int wx = hq_PlanPond_wx;
            int wz = hq_PlanPond_wz;
            GenRng r3 = hq_PlanPond_r3;
            int k = hq_PlanPond_k;
            int first = nEmit;
            hq_PlanPond_sel4 = 0;
            if (GroupAt(GenData.FindGrp("lilies"), GenData.A_OUT_OPEN, 3, WM_NEAR, wx + (k == 0 ? 900 : -900), wz, 0, ref r3)) hq_PlanPond_sel4 = 1;
            hq_PlanPond_wx = wx;
            hq_PlanPond_wz = wz;
            hq_PlanPond_r3 = r3;
            hq_PlanPond_first = first;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!(hq_PlanPond_sel4 == 1)) PhBreak();
            else
            {
                int first = hq_PlanPond_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanPond_first = first;
            }
        }
    }


    void PondBank(int room, int wx, int wz)
    {
        hq_PondBank_room = room;
        hq_PondBank_wx = wx;
        hq_PondBank_wz = wz;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); PondBank_Body(); fin = PhAdvance(); PhPop(); }
    }

    // PondBank in phases, a step a call (autophase4); PondBank() above runs them to the end
    void PondBank_Body()
    {
        planMax = 26;
        if (planPhase == 0)
        {
            GenRng r4 = SR(4);
            if (r4.Chance(600) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("n_canoe"), WM_ANY, 0, 0, WIN_ANY, 0, ref r4) >= 0) Keep(first, false); }
            GenRng r5 = SR(5);
            hq_PondBank_r5 = r5;
        }
        if (planPhase == 1)
        {
            int wx = hq_PondBank_wx;
            int wz = hq_PondBank_wz;
            GenRng r5 = hq_PondBank_r5;
            if (Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("rocks"), GenData.A_OUT_OPEN, 3, WM_NEAR, wx, wz, 0, ref r5)) Keep(first, false); }
            GenRng r6 = SR(6);
            hq_PondBank_wx = wx;
            hq_PondBank_wz = wz;
            hq_PondBank_r5 = r5;
            hq_PondBank_r6 = r6;
        }
        if (planPhase == 2)
        {
            GenRng r6 = hq_PondBank_r6;
            if (Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("shrubs"), GenData.A_OUT_EDGE, r6.Between(2, 3), WM_ANY, 0, 0, 0, ref r6)) Keep(first, false); }
            GenRng r7 = SR(7);
            hq_PondBank_r6 = r6;
            hq_PondBank_r7 = r7;
        }
        if (planPhase == 3)
        {
            int wx = hq_PondBank_wx;
            int wz = hq_PondBank_wz;
            GenRng r7 = hq_PondBank_r7;
            if (Fit(1)) { int first = nEmit; if (WallPlace(Pick(ref r7, "ck_bench", 5, "k_bench", 4), WM_NEAR, wx, wz, WIN_ANY, 0, ref r7) >= 0) Keep(first, false); }
            GenRng r8 = SR(8);
            hq_PondBank_sel1 = 0;
            if (r8.Chance(600) && Fit(1)) hq_PondBank_sel1 = 1;
            hq_PondBank_wx = wx;
            hq_PondBank_wz = wz;
            hq_PondBank_r7 = r7;
            hq_PondBank_r8 = r8;
        }
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (!(hq_PondBank_sel1 == 1)) PhBreak();
            else
            {
                int wx = hq_PondBank_wx;
                int wz = hq_PondBank_wz;
                GenRng r8 = hq_PondBank_r8;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 1; hq_OpenSpot_mode = WM_NEAR; hq_OpenSpot_tx = wx; hq_OpenSpot_tz = wz; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r8; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r8 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PondBank_wx = wx;
                hq_PondBank_wz = wz;
                hq_PondBank_r8 = r8;
                hq_PondBank_cell = cell;
            }
        }
        if (planPhase == 5)
        {
            if (hq_PondBank_sel1 == 1)
            {
                GenRng r8 = hq_PondBank_r8;
                int cell = hq_PondBank_cell;
                int first = nEmit;
                hq_PondBank_sel2 = 0;
                if (cell >= 0 && FreePlace(Pick(ref r8, "n_rockLarge", 4, "n_rockTall", 3, "n_logLarge", 3), cell, r8.Range(360), 0, 0, H_NATURE) >= 0) hq_PondBank_sel2 = 1;
                hq_PondBank_r8 = r8;
                hq_PondBank_cell = cell;
                hq_PondBank_first = first;
            }
        }
        if (planPhase == 6 && PhLoop(6, 100000))
        {
            if (!(hq_PondBank_sel1 == 1 && hq_PondBank_sel2 == 1)) PhBreak();
            else
            {
                int first = hq_PondBank_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PondBank_first = first;
            }
        }
        if (planPhase == 7)
        {
            GenRng r9 = SR(9);
            hq_PondBank_r9 = r9;
        }
        if (planPhase == 8 && PhLoop(8, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 2 && Fit(1))) PhBreak();
                else { hq_PondBank_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PondBank_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 9)
        {
            GenRng r10 = SR(10);
            hq_PondBank_sel4 = 0;
            if (r10.Chance(500) && Fit(1)) hq_PondBank_sel4 = 1;
            hq_PondBank_r10 = r10;
        }
        if (planPhase == 10 && PhLoop(10, 100000))
        {
            if (!(hq_PondBank_sel4 == 1)) PhBreak();
            else
            {
                GenRng r10 = hq_PondBank_r10;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 1; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r10; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r10 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PondBank_r10 = r10;
                hq_PondBank_cell = cell;
            }
        }
        if (planPhase == 11)
        {
            if (hq_PondBank_sel4 == 1)
            {
                int cell = hq_PondBank_cell;
                int first = nEmit;
                hq_PondBank_sel5 = 0;
                if (cell >= 0 && FreePlace(Dn("f_lamp"), cell, 0, 0, 0, H_FREE) >= 0) hq_PondBank_sel5 = 1;
                hq_PondBank_cell = cell;
                hq_PondBank_first = first;
            }
        }
        if (planPhase == 12 && PhLoop(12, 100000))
        {
            if (!(hq_PondBank_sel4 == 1 && hq_PondBank_sel5 == 1)) PhBreak();
            else
            {
                int first = hq_PondBank_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PondBank_first = first;
            }
        }
        if (planPhase == 13)
        {
            int room = hq_PondBank_room;
            GenRng r11 = SR(11);
            int trees = TreeCount(room, ref r11);
            hq_PondBank_room = room;
            hq_PondBank_trees = trees;
        }
        if (planPhase == 14 && PhLoop(14, 100000))
        {
            int trees = hq_PondBank_trees;
            if (!PhSubActive())
            {
                int t = planIter;
                if (!(t < trees && t < 1)) PhBreak();
                else { hq_PondBank_t = t; PhSubBegin(); }
            }
            hq_PondBank_trees = trees;
            if (PhSubActive())
            {
                PhPush(); PondBank_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 15)
        {
            // a stump with mushrooms on the bank, the bin, more planting along the fence
            GenRng r12 = SR(12);
            hq_PondBank_sel6 = 0;
            if (r12.Chance(550) && Fit(2)) hq_PondBank_sel6 = 1;
            hq_PondBank_r12 = r12;
        }
        if (planPhase == 16 && PhLoop(16, 100000))
        {
            if (!(hq_PondBank_sel6 == 1)) PhBreak();
            else
            {
                GenRng r12 = hq_PondBank_r12;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 1; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r12; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r12 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PondBank_r12 = r12;
                hq_PondBank_cell = cell;
            }
        }
        if (planPhase == 17)
        {
            if (hq_PondBank_sel6 == 1)
            {
                GenRng r12 = hq_PondBank_r12;
                int cell = hq_PondBank_cell;
                int first = nEmit;
                int st = cell >= 0 ? FreePlace(Dn("n_stump"), cell, r12.Range(360), 0, 0, H_NATURE) : -1;
                hq_PondBank_r12 = r12;
                hq_PondBank_cell = cell;
                hq_PondBank_first = first;
                hq_PondBank_st = st;
            }
        }
        if (planPhase == 18)
        {
            if (hq_PondBank_sel6 == 1)
            {
                GenRng r12 = hq_PondBank_r12;
                int first = hq_PondBank_first;
                int st = hq_PondBank_st;
                if (st >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp("mushrooms"), st, 2, 0, ref r12)) Keep(first, false); }
                hq_PondBank_r12 = r12;
                hq_PondBank_first = first;
                hq_PondBank_st = st;
            }
        }
        if (planPhase == 19)
        {
            GenRng r13 = SR(13);
            if (r13.Chance(500) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ck_trash"), WM_ANY, 0, 0, WIN_ANY, 0, ref r13) >= 0) Keep(first, false); }
            // revision 11: the beaver on the bank by its stump, a log to sit on
            GenRng r14 = SR(14);
            hq_PondBank_sel7 = 0;
            if (r14.Chance(550) && Fit(1)) hq_PondBank_sel7 = 1;
            hq_PondBank_r14 = r14;
        }
        if (planPhase == 20 && PhLoop(20, 100000))
        {
            if (!(hq_PondBank_sel7 == 1)) PhBreak();
            else
            {
                int wx = hq_PondBank_wx;
                int wz = hq_PondBank_wz;
                GenRng r14 = hq_PondBank_r14;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 1; hq_OpenSpot_mode = WM_NEAR; hq_OpenSpot_tx = wx; hq_OpenSpot_tz = wz; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r14; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r14 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PondBank_wx = wx;
                hq_PondBank_wz = wz;
                hq_PondBank_r14 = r14;
                hq_PondBank_cell = cell;
            }
        }
        if (planPhase == 21)
        {
            if (hq_PondBank_sel7 == 1)
            {
                GenRng r14 = hq_PondBank_r14;
                int cell = hq_PondBank_cell;
                int first = nEmit;
                hq_PondBank_sel8 = 0;
                if (cell >= 0 && FreePlace(Dn("p_beaver"), cell, r14.Range(360), 0, 0, H_FREE) >= 0) hq_PondBank_sel8 = 1;
                hq_PondBank_r14 = r14;
                hq_PondBank_cell = cell;
                hq_PondBank_first = first;
            }
        }
        if (planPhase == 22 && PhLoop(22, 100000))
        {
            if (!(hq_PondBank_sel7 == 1 && hq_PondBank_sel8 == 1)) PhBreak();
            else
            {
                int first = hq_PondBank_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PondBank_first = first;
            }
        }
        if (planPhase == 23)
        {
            GenRng r15 = SR(15);
            if (r15.Chance(450) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("n_log"), WM_ANY, 0, 0, WIN_ANY, 0, ref r15) >= 0) Keep(first, false); }
        }
        if (planPhase == 24 && PhLoop(24, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "n_rockLarge", "n_rockTall", "n_logLarge", "n_bushLarge", "n_bushDetailed", "ck_bench", "k_bench", "f_lamp", "n_stump", "ck_trash", "n_grassLarge", "n_log", "p_beaver", "hk_bench", "n_paddle", "s_stoneL", "s_rockFlat" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 25)
        {
            ClearBans();
        }
        if (planPhase == 26 && PhLoop(26, 100000))
        {
            if (planIter == 0) { hq_Borders_k0 = 40; hq_Borders_max = 4; hq_Borders_soil = true; PhSubReset(); }
            PhPush(); Borders_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }
    void PondBank_L1()
    {
        planMax = 2;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int wx = hq_PondBank_wx;
            int wz = hq_PondBank_wz;
            GenRng r9 = hq_PondBank_r9;
            int cell = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = 1; hq_OpenSpot_mode = WM_NEAR; hq_OpenSpot_tx = wx; hq_OpenSpot_tz = wz; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r9; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r9 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
            hq_PondBank_wx = wx;
            hq_PondBank_wz = wz;
            hq_PondBank_r9 = r9;
            hq_PondBank_cell = cell;
        }
        if (planPhase == 1)
        {
            GenRng r9 = hq_PondBank_r9;
            int cell = hq_PondBank_cell;
            int first = nEmit;
            hq_PondBank_sel3 = 0;
            if (cell >= 0 && FreePlace(Dn("n_grassLarge"), cell, r9.Range(360), 0, 0, H_NATURE) >= 0) hq_PondBank_sel3 = 1;
            hq_PondBank_r9 = r9;
            hq_PondBank_cell = cell;
            hq_PondBank_first = first;
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (!(hq_PondBank_sel3 == 1)) PhBreak();
            else
            {
                int first = hq_PondBank_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PondBank_first = first;
            }
        }
    }

    void PondBank_L2()
    {
        planMax = 0;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int t = hq_PondBank_t;
            if (planIter == 0) { hq_LawnTree_k = 60 + t; PhSubReset(); }
            PhPush(); LawnTree_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }


    // ============================================================================================ CAFE TERRACE
    // A paved terrace: kiosks at the edge (food, drinks, the information booth, the toilets), a market stall with its
    // bench, round tables with chairs and what was ordered on them, flower beds, pots, bins and a lamp.
    void PlanTerrace()
    {
        planMax = 21;
        int room = pRoom;
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            string[] kiosks = { "ck_food", "ck_drinks", "ck_info", "ck_toilets", "f_stallGreen", "f_stallRed" };
            int k0 = r.Range(kiosks.Length), got = 0;
            hq_PlanTerrace_kiosks = kiosks;
            hq_PlanTerrace_k0 = k0;
            hq_PlanTerrace_got = got;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            string[] kiosks = hq_PlanTerrace_kiosks;
            int got = hq_PlanTerrace_got;
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < kiosks.Length && got < 2 + AreaM2(room) / 16 && Fit(1))) PhBreak();
                else { hq_PlanTerrace_k = k; PhSubBegin(); }
            }
            hq_PlanTerrace_kiosks = kiosks;
            hq_PlanTerrace_got = got;
            if (PhSubActive())
            {
                PhPush(); PlanTerrace_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 2)
        {
            int got = hq_PlanTerrace_got;
            if (got == 0) MandFail(1);
            GenRng r10 = SR(10);
            int tables = 2 + AreaM2(room) / 14;
            hq_PlanTerrace_got = got;
            hq_PlanTerrace_r10 = r10;
            hq_PlanTerrace_tables = tables;
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            int tables = hq_PlanTerrace_tables;
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < tables && Fit(2))) PhBreak();
                else { hq_PlanTerrace_k = k; PhSubBegin(); }
            }
            hq_PlanTerrace_tables = tables;
            if (PhSubActive())
            {
                PhPush(); PlanTerrace_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 4)
        {
            GenRng r11 = SR(11);
            hq_PlanTerrace_sel2 = 0;
            if (Fit(1)) hq_PlanTerrace_sel2 = 1;
            hq_PlanTerrace_r11 = r11;
        }
        if (planPhase == 5 && PhLoop(5, 100000))
        {
            if (!(hq_PlanTerrace_sel2 == 1)) PhBreak();
            else
            {
                GenRng r11 = hq_PlanTerrace_r11;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r11; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r11 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PlanTerrace_r11 = r11;
                hq_PlanTerrace_cell = cell;
            }
        }
        if (planPhase == 6)
        {
            if (hq_PlanTerrace_sel2 == 1)
            {
                GenRng r11 = hq_PlanTerrace_r11;
                int cell = hq_PlanTerrace_cell;
                int first = nEmit;
                hq_PlanTerrace_sel3 = 0;
                if (cell >= 0 && FreePlace(Dn("ck_flowers"), cell, r11.Range(4) * 90, 0, 0, H_FREE) >= 0) hq_PlanTerrace_sel3 = 1;
                hq_PlanTerrace_r11 = r11;
                hq_PlanTerrace_cell = cell;
                hq_PlanTerrace_first = first;
            }
        }
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            if (!(hq_PlanTerrace_sel2 == 1 && hq_PlanTerrace_sel3 == 1)) PhBreak();
            else
            {
                int first = hq_PlanTerrace_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanTerrace_first = first;
            }
        }
        if (planPhase == 8)
        {
            GenRng r12 = SR(12);
            if (r12.Chance(700) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("pots"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r12)) Keep(first, false); }
            GenRng r13 = SR(13);
            hq_PlanTerrace_r13 = r13;
        }
        if (planPhase == 9)
        {
            GenRng r13 = hq_PlanTerrace_r13;
            if (Fit(1)) { int first = nEmit; if (WallPlace(Pick(ref r13, "ck_trash", 5, "k_trashcan", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r13) >= 0) Keep(first, false); }
            GenRng r14 = SR(14);
            hq_PlanTerrace_r13 = r13;
            hq_PlanTerrace_r14 = r14;
        }
        if (planPhase == 10)
        {
            GenRng r14 = hq_PlanTerrace_r14;
            if (r14.Chance(500) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ck_bench"), WM_ANY, 0, 0, WIN_ANY, 0, ref r14) >= 0) Keep(first, false); }
            GenRng r15 = SR(15);
            hq_PlanTerrace_sel4 = 0;
            if (r15.Chance(600) && Fit(1)) hq_PlanTerrace_sel4 = 1;
            hq_PlanTerrace_r14 = r14;
            hq_PlanTerrace_r15 = r15;
        }
        if (planPhase == 11 && PhLoop(11, 100000))
        {
            if (!(hq_PlanTerrace_sel4 == 1)) PhBreak();
            else
            {
                GenRng r15 = hq_PlanTerrace_r15;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 1; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r15; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r15 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PlanTerrace_r15 = r15;
                hq_PlanTerrace_cell = cell;
            }
        }
        if (planPhase == 12)
        {
            if (hq_PlanTerrace_sel4 == 1)
            {
                int cell = hq_PlanTerrace_cell;
                int first = nEmit;
                hq_PlanTerrace_sel5 = 0;
                if (cell >= 0 && FreePlace(Dn("f_lamp"), cell, 0, 0, 0, H_FREE) >= 0) hq_PlanTerrace_sel5 = 1;
                hq_PlanTerrace_cell = cell;
                hq_PlanTerrace_first = first;
            }
        }
        if (planPhase == 13 && PhLoop(13, 100000))
        {
            if (!(hq_PlanTerrace_sel4 == 1 && hq_PlanTerrace_sel5 == 1)) PhBreak();
            else
            {
                int first = hq_PlanTerrace_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanTerrace_first = first;
            }
        }
        if (planPhase == 14)
        {
            GenRng r18 = SR(18);
            if (r18.Chance(550) && Fit(1)) { int first = nEmit; if (WallPlace(Dn(r18.Chance(600) ? "ma_vending" : "ma_claw"), WM_ANY, 0, 0, WIN_ANY, 0, ref r18) >= 0) Keep(first, false); }
        }
        if (planPhase == 15)
        {
            GenRng r19 = SR(19);
            if (r19.Chance(500) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("crates"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r19)) Keep(first, false); }
            // fruit set out by a kiosk, a plant, a second flower bed
        }
        if (planPhase == 16)
        {
            GenRng r16 = SR(16);
            if (r16.Chance(500) && Fit(1)) { int first = nEmit; if (WallPlace(Pick(ref r16, "ms_dispFruit", 4, "k_plant", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r16) >= 0) Keep(first, false); }
        }
        if (planPhase == 17)
        {
            GenRng r17 = SR(17);
            hq_PlanTerrace_sel6 = 0;
            if (r17.Chance(500) && Fit(1)) hq_PlanTerrace_sel6 = 1;
            hq_PlanTerrace_r17 = r17;
        }
        if (planPhase == 18 && PhLoop(18, 100000))
        {
            if (!(hq_PlanTerrace_sel6 == 1)) PhBreak();
            else
            {
                GenRng r17 = hq_PlanTerrace_r17;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r17; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r17 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PlanTerrace_r17 = r17;
                hq_PlanTerrace_cell = cell;
            }
        }
        if (planPhase == 19)
        {
            if (hq_PlanTerrace_sel6 == 1)
            {
                GenRng r17 = hq_PlanTerrace_r17;
                int cell = hq_PlanTerrace_cell;
                int first = nEmit;
                hq_PlanTerrace_sel7 = 0;
                if (cell >= 0 && FreePlace(Dn("ck_flowers"), cell, r17.Range(4) * 90, 0, 0, H_FREE) >= 0) hq_PlanTerrace_sel7 = 1;
                hq_PlanTerrace_r17 = r17;
                hq_PlanTerrace_cell = cell;
                hq_PlanTerrace_first = first;
            }
        }
        if (planPhase == 20 && PhLoop(20, 100000))
        {
            if (!(hq_PlanTerrace_sel6 == 1 && hq_PlanTerrace_sel7 == 1)) PhBreak();
            else
            {
                int first = hq_PlanTerrace_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanTerrace_first = first;
            }
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 21 && PhLoop(21, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "ck_bench", "ck_trash", "k_plant", "ms_dispFruit", "ck_flowers", "f_lamp", "ma_vending", "ck_info", "ck_food", "ck_drinks", "n_planter", "hk_bench", "hk_lantern", "ma_claw" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
}
    void PlanTerrace_L1()
    {
        int room = pRoom;
        planMax = 1;
        if (planPhase == 0)
        {
            string[] kiosks = hq_PlanTerrace_kiosks;
            int k0 = hq_PlanTerrace_k0;
            int k = hq_PlanTerrace_k;
            GenRng rk = SR(2 + k);
            int first = nEmit;
            int e = WallPlace(Dn(kiosks[(k0 + k) % kiosks.Length]), WM_ANY, 0, 0, WIN_ANY, 0, ref rk);
            hq_PlanTerrace_kiosks = kiosks;
            hq_PlanTerrace_k0 = k0;
            hq_PlanTerrace_rk = rk;
            hq_PlanTerrace_first = first;
            hq_PlanTerrace_e = e;
        }
        if (planPhase == 1)
        {
            int got = hq_PlanTerrace_got;
            GenRng rk = hq_PlanTerrace_rk;
            int first = hq_PlanTerrace_first;
            int e = hq_PlanTerrace_e;
            if (e >= 0 && Keep(first, false))
            {
                got++;
                if (GenData.defId[emitDef[e]].StartsWith("f_stall") && Fit(1)) { first = nEmit; if (RelBeside(Dn(rk.Chance(500) ? "f_stallBench" : "f_stallStool"), e, rk.Chance(500) ? 1 : -1, 150, 0, ref rk) >= 0) Keep(first, false); }
            }
            hq_PlanTerrace_got = got;
            hq_PlanTerrace_rk = rk;
            hq_PlanTerrace_first = first;
            hq_PlanTerrace_e = e;
        }
    }

    void PlanTerrace_L2()
    {
        int room = pRoom;
        planMax = 2;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            GenRng r10 = hq_PlanTerrace_r10;
            int cell = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = 3; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r10; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r10 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
            hq_PlanTerrace_r10 = r10;
            hq_PlanTerrace_cell = cell;
        }
        if (planPhase == 1)
        {
            int cell = hq_PlanTerrace_cell;
            if (cell < 0) { hq_PlanTerrace_cell = cell; phBreak = true; PhEnd(); return; }
            int first = nEmit;
            hq_PlanTerrace_sel1 = 0;
            if (FreePlace(Dn("r_tableRound"), cell, 0, 1, 0, H_FREE) >= 0) hq_PlanTerrace_sel1 = 1;
            hq_PlanTerrace_cell = cell;
            hq_PlanTerrace_first = first;
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (!(hq_PlanTerrace_sel1 == 1)) PhBreak();
            else
            {
                int first = hq_PlanTerrace_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanTerrace_first = first;
            }
        }
    }


    // revision 11: what an area's own steps could not place is made up from the rest of what belongs there, each kind
    // tried once in a shuffled order: at the fence or a wall, else (a thing that may stand alone) in the open
    int PoolLen(string[] pool) { return pool.Length; }

    // FillFromPool one piece a step (planIter: which of the pool; Cor, build freezes). False when it stops.
    bool PoolStep(int k0, string[] pool)
    {
        int room = pRoom;
        if (planIter == 0) { planR = SR(k0); planB = planR.Range(pool.Length); }
        if (!Fit(1)) return false;
        int def = Dn(pool[(planB + planIter) % pool.Length]);
        if (CountInRoom(def, room) >= ScaledCap(GenData.defRoomCap[def], room)) return true;
        int first = nEmit;
        if (WallPlace(def, WM_ANY, 0, 0, WIN_ANY, 0, ref planR) >= 0 && Keep(first, false)) return true;
        if (Has(def, GenData.FL_LOOSE)) return true;
        int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref planR);
        first = nEmit;
        if (cell >= 0 && FreePlace(def, cell, planR.Range(4) * 90, 1, 0, H_FREE) >= 0) Keep(first, false);
        return true;
    }

    void FillFromPool(int k0, string[] pool)
    {
        hq_FillFromPool_k0 = k0;
        hq_FillFromPool_pool = pool;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); FillFromPool_Body(); fin = PhAdvance(); PhPop(); }
    }

    // FillFromPool in phases, a step a call (autophase4); FillFromPool() above runs them to the end
    void FillFromPool_Body()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int k0 = hq_FillFromPool_k0;
            string[] pool = hq_FillFromPool_pool;
            int room = pRoom;
            GenRng r = SR(k0);
            int p0 = r.Range(pool.Length);
            hq_FillFromPool_k0 = k0;
            hq_FillFromPool_pool = pool;
            hq_FillFromPool_room = room;
            hq_FillFromPool_r = r;
            hq_FillFromPool_p0 = p0;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            string[] pool = hq_FillFromPool_pool;
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < pool.Length && Fit(1))) PhBreak();
                else { hq_FillFromPool_k = k; PhSubBegin(); }
            }
            hq_FillFromPool_pool = pool;
            if (PhSubActive())
            {
                PhPush(); FillFromPool_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
    }
    void FillFromPool_L1()
    {
        planMax = 3;
        if (planPhase == 0)
        {
            string[] pool = hq_FillFromPool_pool;
            int room = hq_FillFromPool_room;
            GenRng r = hq_FillFromPool_r;
            int p0 = hq_FillFromPool_p0;
            int k = hq_FillFromPool_k;
            int def = Dn(pool[(p0 + k) % pool.Length]);
            if (CountInRoom(def, room) >= ScaledCap(GenData.defRoomCap[def], room)) { hq_FillFromPool_pool = pool; hq_FillFromPool_room = room; hq_FillFromPool_r = r; hq_FillFromPool_p0 = p0; hq_FillFromPool_def = def; PhEnd(); return; }
            int first = nEmit;
            if (WallPlace(def, WM_ANY, 0, 0, WIN_ANY, 0, ref r) >= 0 && Keep(first, false))
            {
                { hq_FillFromPool_pool = pool; hq_FillFromPool_room = room; hq_FillFromPool_r = r; hq_FillFromPool_p0 = p0; hq_FillFromPool_def = def; hq_FillFromPool_first = first; PhEnd(); return; }
            }
            if (Has(def, GenData.FL_LOOSE)) { hq_FillFromPool_pool = pool; hq_FillFromPool_room = room; hq_FillFromPool_r = r; hq_FillFromPool_p0 = p0; hq_FillFromPool_def = def; hq_FillFromPool_first = first; PhEnd(); return; }
            hq_FillFromPool_pool = pool;
            hq_FillFromPool_room = room;
            hq_FillFromPool_r = r;
            hq_FillFromPool_p0 = p0;
            hq_FillFromPool_def = def;
            hq_FillFromPool_first = first;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            GenRng r = hq_FillFromPool_r;
            int cell = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
            hq_FillFromPool_r = r;
            hq_FillFromPool_cell = cell;
        }
        if (planPhase == 2)
        {
            GenRng r = hq_FillFromPool_r;
            int def = hq_FillFromPool_def;
            int first = hq_FillFromPool_first;
            int cell = hq_FillFromPool_cell;
            first = nEmit;
            hq_FillFromPool_sel1 = 0;
            if (cell >= 0 && FreePlace(def, cell, r.Range(4) * 90, 1, 0, H_FREE) >= 0) hq_FillFromPool_sel1 = 1;
            hq_FillFromPool_r = r;
            hq_FillFromPool_def = def;
            hq_FillFromPool_first = first;
            hq_FillFromPool_cell = cell;
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (!(hq_FillFromPool_sel1 == 1)) PhBreak();
            else
            {
                int first = hq_FillFromPool_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_FillFromPool_first = first;
            }
        }
    }


    // ============================================================================================ FARM ADDITIONS
    // the coop with its hens, the trough, rabbits: in a farmyard or a paddock
    void FarmAnimals(int k0, bool yard)
    {
        hq_FarmAnimals_k0 = k0;
        hq_FarmAnimals_yard = yard;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); FarmAnimals_Body(); fin = PhAdvance(); PhPop(); }
    }

    // FarmAnimals in phases, a step a call (autophase4); FarmAnimals() above runs them to the end
    void FarmAnimals_Body()
    {
        planMax = 7;
        if (planPhase == 0)
        {
            int k0 = hq_FarmAnimals_k0;
            bool yard = hq_FarmAnimals_yard;
            int room = pRoom;
            GenRng r = SR(k0);
            hq_FarmAnimals_sel1 = 0;
            if (r.Chance(yard ? 550 : 650) && Fit(1)) hq_FarmAnimals_sel1 = 1;
            hq_FarmAnimals_k0 = k0;
            hq_FarmAnimals_yard = yard;
            hq_FarmAnimals_room = room;
            hq_FarmAnimals_r = r;
        }
        if (planPhase == 1)
        {
            if (hq_FarmAnimals_sel1 == 1)
            {
                GenRng r = hq_FarmAnimals_r;
                int first = nEmit;
                int coop = WallPlace(Dn("gen_coop"), WM_ANY, 0, 0, WIN_ANY, 0, ref r);
                hq_FarmAnimals_r = r;
                hq_FarmAnimals_first = first;
                hq_FarmAnimals_coop = coop;
            }
        }
        if (planPhase == 2)
        {
            if (hq_FarmAnimals_sel1 == 1)
            {
                GenRng r = hq_FarmAnimals_r;
                int first = hq_FarmAnimals_first;
                int coop = hq_FarmAnimals_coop;
                if (coop >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp("hens"), coop, 2, 0, ref r)) Keep(first, false); }
                hq_FarmAnimals_r = r;
                hq_FarmAnimals_first = first;
                hq_FarmAnimals_coop = coop;
            }
        }
        if (planPhase == 3)
        {
            int k0 = hq_FarmAnimals_k0;
            bool yard = hq_FarmAnimals_yard;
            GenRng r2 = SR(k0 + 1);
            if (r2.Chance(yard ? 450 : 750) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("gen_trough"), WM_ANY, 0, 0, WIN_ANY, 0, ref r2) >= 0) Keep(first, false); }
            GenRng r3 = SR(k0 + 2);
            hq_FarmAnimals_k0 = k0;
            hq_FarmAnimals_yard = yard;
            hq_FarmAnimals_r3 = r3;
        }
        if (planPhase == 4)
        {
            int k0 = hq_FarmAnimals_k0;
            bool yard = hq_FarmAnimals_yard;
            int room = hq_FarmAnimals_room;
            GenRng r3 = hq_FarmAnimals_r3;
            if (r3.Chance(450) && Fit(2)) { int first = nEmit; if (PlaceGroup(GenData.FindGrp("rabbits"), room, GenData.A_OUT_OPEN, 2, 0, ref r3)) Keep(first, false); }
            GenRng r4 = SR(k0 + 3);
            hq_FarmAnimals_sel2 = 0;
            if (!yard && r4.Chance(500) && Fit(1)) hq_FarmAnimals_sel2 = 1;
            hq_FarmAnimals_k0 = k0;
            hq_FarmAnimals_yard = yard;
            hq_FarmAnimals_room = room;
            hq_FarmAnimals_r3 = r3;
            hq_FarmAnimals_r4 = r4;
        }
        if (planPhase == 5 && PhLoop(5, 100000))
        {
            if (!(hq_FarmAnimals_sel2 == 1)) PhBreak();
            else
            {
                GenRng r4 = hq_FarmAnimals_r4;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r4; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r4 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_FarmAnimals_r4 = r4;
                hq_FarmAnimals_cell = cell;
            }
        }
        if (planPhase == 6)
        {
            if (hq_FarmAnimals_sel2 == 1)
            {
                GenRng r4 = hq_FarmAnimals_r4;
                int cell = hq_FarmAnimals_cell;
                int first = nEmit;
                hq_FarmAnimals_sel3 = 0;
                if (cell >= 0 && FreePlace(Dn("p_hog"), cell, r4.Range(360), 0, 0, H_FREE) >= 0) hq_FarmAnimals_sel3 = 1;
                hq_FarmAnimals_r4 = r4;
                hq_FarmAnimals_cell = cell;
                hq_FarmAnimals_first = first;
            }
        }
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            if (!(hq_FarmAnimals_sel2 == 1 && hq_FarmAnimals_sel3 == 1)) PhBreak();
            else
            {
                int first = hq_FarmAnimals_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_FarmAnimals_first = first;
            }
        }
    }

    // the farm stand at the lane: a market stall with the produce on it (FillSlots), its bench or stool
    void FarmStand(int k0)
    {
        hq_FarmStand_k0 = k0;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); FarmStand_Body(); fin = PhAdvance(); PhPop(); }
    }

    // FarmStand in phases, a step a call (autophase4); FarmStand() above runs them to the end
    void FarmStand_Body()
    {
        planMax = 1;
        if (planPhase == 0)
        {
            int k0 = hq_FarmStand_k0;
            GenRng r = SR(k0);
            if (!r.Chance(750) || !Fit(1)) { hq_FarmStand_k0 = k0; hq_FarmStand_r = r; PhEnd(); return; }
            int dx, dz;
            MainDoor(pRoom, out dx, out dz);
            int first = nEmit;
            int st = WallPlace(Dn(r.Chance(500) ? "f_stallGreen" : "f_stallRed"), WM_FAR, dx, dz, WIN_ANY, 0, ref r);
            hq_FarmStand_k0 = k0;
            hq_FarmStand_r = r;
            hq_FarmStand_first = first;
            hq_FarmStand_st = st;
        }
        if (planPhase == 1)
        {
            GenRng r = hq_FarmStand_r;
            int first = hq_FarmStand_first;
            int st = hq_FarmStand_st;
            if (st >= 0 && Keep(first, false) && Fit(1)) { first = nEmit; if (RelBeside(Dn(r.Chance(500) ? "f_stallBench" : "f_stallStool"), st, r.Chance(500) ? 1 : -1, 150, 0, ref r) >= 0) Keep(first, false); }
            hq_FarmStand_r = r;
            hq_FarmStand_first = first;
            hq_FarmStand_st = st;
        }
    }

    // phase state of the split plans (Tools: autophase2)
    int hp_PlanFarmWork_first;
    int hp_PlanFarmWork_wb;
    string[] hp_PlanFarmWork_rows;
    int[] hp_PlanFarmWork_chance;

    // locals of the split plans and helpers, kept from one phase to the next (autophase4)
    int hq_FarmAnimals_cell;
    int hq_FarmAnimals_coop;
    int hq_FarmAnimals_first;
    int hq_FarmAnimals_k0;
    GenRng hq_FarmAnimals_r;
    GenRng hq_FarmAnimals_r3;
    GenRng hq_FarmAnimals_r4;
    int hq_FarmAnimals_room;
    int hq_FarmAnimals_sel1;
    int hq_FarmAnimals_sel2;
    int hq_FarmAnimals_sel3;
    bool hq_FarmAnimals_yard;
    int hq_FarmStand_first;
    int hq_FarmStand_k0;
    GenRng hq_FarmStand_r;
    int hq_FarmStand_st;
    int hq_FillFromPool_cell;
    int hq_FillFromPool_def;
    int hq_FillFromPool_first;
    int hq_FillFromPool_k;
    int hq_FillFromPool_k0;
    int hq_FillFromPool_p0;
    string[] hq_FillFromPool_pool;
    GenRng hq_FillFromPool_r;
    int hq_FillFromPool_room;
    int hq_FillFromPool_sel1;
    int hq_PlanFarmWork_cell;
    int hq_PlanFarmWork_g;
    int hq_PlanFarmWork_k;
    bool hq_PlanFarmWork_ok;
    GenRng hq_PlanFarmWork_r2;
    GenRng hq_PlanFarmWork_rk;
    int hq_PlanFarmWork_sel1;
    int hq_PlanFarmWork_sel2;
    int hq_PlanFarmWork_sel3;
    int hq_PlanPlayground_axis;
    string[] hq_PlanPlayground_big;
    int hq_PlanPlayground_cell;
    int hq_PlanPlayground_first;
    int hq_PlanPlayground_got;
    int hq_PlanPlayground_k;
    GenRng hq_PlanPlayground_r;
    GenRng hq_PlanPlayground_r3;
    GenRng hq_PlanPlayground_r6;
    GenRng hq_PlanPlayground_r9;
    int hq_PlanPlayground_s0;
    int hq_PlanPlayground_sel1;
    int hq_PlanPlayground_sel2;
    int hq_PlanPlayground_sel3;
    int hq_PlanPlayground_sel4;
    int hq_PlanPond_cx;
    int hq_PlanPond_cz;
    int hq_PlanPond_first;
    int hq_PlanPond_k;
    GenRng hq_PlanPond_r3;
    int hq_PlanPond_sel1;
    int hq_PlanPond_sel2;
    int hq_PlanPond_sel3;
    int hq_PlanPond_sel4;
    int hq_PlanPond_water;
    int hq_PlanPond_wx;
    int hq_PlanPond_wz;
    int hq_PlanTerrace_cell;
    int hq_PlanTerrace_e;
    int hq_PlanTerrace_first;
    int hq_PlanTerrace_got;
    int hq_PlanTerrace_k;
    int hq_PlanTerrace_k0;
    string[] hq_PlanTerrace_kiosks;
    GenRng hq_PlanTerrace_r10;
    GenRng hq_PlanTerrace_r11;
    GenRng hq_PlanTerrace_r13;
    GenRng hq_PlanTerrace_r14;
    GenRng hq_PlanTerrace_r15;
    GenRng hq_PlanTerrace_r17;
    GenRng hq_PlanTerrace_rk;
    int hq_PlanTerrace_sel1;
    int hq_PlanTerrace_sel2;
    int hq_PlanTerrace_sel3;
    int hq_PlanTerrace_sel4;
    int hq_PlanTerrace_sel5;
    int hq_PlanTerrace_sel6;
    int hq_PlanTerrace_sel7;
    int hq_PlanTerrace_tables;
    int hq_PondBank_cell;
    int hq_PondBank_first;
    int hq_PondBank_k;
    GenRng hq_PondBank_r10;
    GenRng hq_PondBank_r12;
    GenRng hq_PondBank_r14;
    GenRng hq_PondBank_r5;
    GenRng hq_PondBank_r6;
    GenRng hq_PondBank_r7;
    GenRng hq_PondBank_r8;
    GenRng hq_PondBank_r9;
    int hq_PondBank_room;
    int hq_PondBank_sel1;
    int hq_PondBank_sel2;
    int hq_PondBank_sel3;
    int hq_PondBank_sel4;
    int hq_PondBank_sel5;
    int hq_PondBank_sel6;
    int hq_PondBank_sel7;
    int hq_PondBank_sel8;
    int hq_PondBank_st;
    int hq_PondBank_t;
    int hq_PondBank_trees;
    int hq_PondBank_wx;
    int hq_PondBank_wz;
}
