// MapGen, revision 12 plans: the zoo and the log cabin (research 18, picks 2 and 3). A zoo is an entrance plaza on
// the lane, the keepers' walk up the middle and fenced enclosures either side, each with a gate onto the walk and
// the beasts of one part of the world; one of them is the keepers' yard. The cabin is a log cabin with its porch on
// the lane, its wood yard at the side and the campground behind, towards the wood.
public partial class MapGen
{
    // water in a round patch (radius in inner cells) round (cx, cz), inside the room, off its walkways
    int WaterPatch(int room, int cx, int cz, int rad)
    {
        int ix = FloorDiv(cx, GenData.INNER_MM), iz = FloorDiv(cz, GenData.INNER_MM), n = 0;
        for (int z = iz - rad; z <= iz + rad; z++)
            for (int x = ix - rad; x <= ix + rad; x++)
            {
                if (x < 0 || z < 0 || x >= IW || z >= IH) continue;
                int i = z * IW + x;
                if (innerRoom[i] != room || (x - ix) * (x - ix) + (z - iz) * (z - iz) > rad * rad) continue;
                if ((tags[i] & (GenData.T_PATH | GenData.T_DOOR_CLEAR | GenData.T_WALL_ANY)) != 0 || occ[i] != 0) continue;
                tags[i] |= GenData.T_WATER; n++;
            }
        return n;
    }

    // after the things that live on it are placed, nothing else stands on the water
    void CloseWater(int room)
    {
        for (int k = roomCellStart[room]; k < roomCellStart[room] + roomCellCount[room]; k++)
            if ((tags[roomCells[k]] & GenData.T_WATER) != 0) tags[roomCells[k]] |= GenData.T_NO_SPAWN;
    }

    // n of the given beasts on open ground, by turns: how many stood
    int Beasts(string[] kinds, int n, int k0)
    {
        hq_Beasts_kinds = kinds;
        hq_Beasts_n = n;
        hq_Beasts_k0 = k0;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); Beasts_Body(); fin = PhAdvance(); PhPop(); }
        return hq_Beasts_ret;
    }

    // Beasts in phases, a step a call (autophase4); Beasts() above runs them to the end
    void Beasts_Body()
    {
        planMax = 2;
        if (planPhase == 0)
        {
            int got = 0;
            hq_Beasts_got = got;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int n = hq_Beasts_n;
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < n && Fit(1))) PhBreak();
                else { hq_Beasts_k = k; PhSubBegin(); }
            }
            hq_Beasts_n = n;
            if (PhSubActive())
            {
                PhPush(); Beasts_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 2)
        {
            int got = hq_Beasts_got;
            { hq_Beasts_ret = got; hq_Beasts_got = got; PhEnd(); return; }
            hq_Beasts_got = got;
        }
    }
    void Beasts_L2()
    {
        planMax = 2;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            GenRng rk = hq_Beasts_rk;
            int ring = hq_Beasts_ring;
            int cell = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = ring; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = rk; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { rk = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
            hq_Beasts_rk = rk;
            hq_Beasts_cell = cell;
        }
        if (planPhase == 1)
        {
            GenRng rk = hq_Beasts_rk;
            int def = hq_Beasts_def;
            int e = hq_Beasts_e;
            int cell = hq_Beasts_cell;
            if (cell < 0) { hq_Beasts_rk = rk; hq_Beasts_def = def; hq_Beasts_e = e; hq_Beasts_cell = cell; PhEnd(); return; }
            int first = nEmit;
            e = FreePlace(def, cell, rk.Range(360), 0, 0, H_FREE);
            if (e < 0) e = FreePlace(def, cell, rk.Range(4) * 90, 0, 0, H_FREE);
            hq_Beasts_rk = rk;
            hq_Beasts_def = def;
            hq_Beasts_e = e;
            hq_Beasts_cell = cell;
            hq_Beasts_first = first;
        }
        if (planPhase == 2)
        {
            int e = hq_Beasts_e;
            int first = hq_Beasts_first;
            if (e >= 0 && !Keep(first, false))
            {
                e = -1;
            }
            hq_Beasts_e = e;
            hq_Beasts_first = first;
        }
    }

    void Beasts_L1()
    {
        planMax = 2;
        if (planPhase == 0)
        {
            string[] kinds = hq_Beasts_kinds;
            int k0 = hq_Beasts_k0;
            int k = hq_Beasts_k;
            GenRng rk = SR(k0 + k);
            // a clear spot two cells round, else one; turned any way, else square to the fence
            int def = Dn(kinds[k % kinds.Length]), e = -1;
            hq_Beasts_kinds = kinds;
            hq_Beasts_k0 = k0;
            hq_Beasts_rk = rk;
            hq_Beasts_def = def;
            hq_Beasts_e = e;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            int e = hq_Beasts_e;
            if (!PhSubActive())
            {
                int ring = 2 - planIter;
                if (!(ring >= 1 && e < 0)) PhBreak();
                else { hq_Beasts_ring = ring; PhSubBegin(); }
            }
            hq_Beasts_e = e;
            if (PhSubActive())
            {
                PhPush(); Beasts_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { PhEnd(); return; }
            }
        }
        if (planPhase == 2)
        {
            int got = hq_Beasts_got;
            int e = hq_Beasts_e;
            if (e >= 0) got++;
            hq_Beasts_got = got;
            hq_Beasts_e = e;
        }
    }


    // ============================================================================================ ZOO PLAZA
    // The entrance from the lane: the ticket booth by the gate, the food and drinks kiosks, the toilets, the gift
    // stall with its soft toys, café tables, the vending and claw machines, benches, bins, flower beds, a lamp.
    void PlanZooPlaza()
    {
        planMax = 8;
        int room = pRoom, dx = 0, dz = 0;
        if (planPhase == 0)
        {
            dx = hq_PlanZooPlaza_dx;
            dz = hq_PlanZooPlaza_dz;
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            int first = nEmit;
            hq_PlanZooPlaza_dx = dx;
            hq_PlanZooPlaza_dz = dz;
            hq_PlanZooPlaza_r = r;
            hq_PlanZooPlaza_first = first;
        }
        if (planPhase == 1)
        {
            dx = hq_PlanZooPlaza_dx;
            dz = hq_PlanZooPlaza_dz;
            GenRng r = hq_PlanZooPlaza_r;
            int first = hq_PlanZooPlaza_first;
            if (WallPlace(Dn("ck_info"), WM_NEAR, dx, dz, WIN_ANY, EF_MANDATORY, ref r) >= 0) Keep(first, true); else MandFail(1);
            string[] kiosks = { "ck_food", "ck_drinks", "ck_toilets" };
            hq_PlanZooPlaza_dx = dx;
            hq_PlanZooPlaza_dz = dz;
            hq_PlanZooPlaza_r = r;
            hq_PlanZooPlaza_first = first;
            hq_PlanZooPlaza_kiosks = kiosks;
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            string[] kiosks = hq_PlanZooPlaza_kiosks;
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < kiosks.Length && Fit(1))) PhBreak();
                else { hq_PlanZooPlaza_k = k; PhSubBegin(); }
            }
            hq_PlanZooPlaza_kiosks = kiosks;
            if (PhSubActive())
            {
                PhPush(); PlanZooPlaza_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 3)
        {
            GenRng r5 = SR(5);
            hq_PlanZooPlaza_sel2 = 0;
            if (r5.Chance(700) && Fit(2)) hq_PlanZooPlaza_sel2 = 1;
            hq_PlanZooPlaza_r5 = r5;
        }
        if (planPhase == 4)
        {
            if (hq_PlanZooPlaza_sel2 == 1)
            {
                int first = hq_PlanZooPlaza_first;
                GenRng r5 = hq_PlanZooPlaza_r5;
                first = nEmit;
                int st = WallPlace(Dn(r5.Chance(500) ? "f_stallRed" : "f_stallGreen"), WM_ANY, 0, 0, WIN_ANY, 0, ref r5);
                hq_PlanZooPlaza_first = first;
                hq_PlanZooPlaza_r5 = r5;
                hq_PlanZooPlaza_st = st;
            }
        }
        if (planPhase == 5)
        {
            if (hq_PlanZooPlaza_sel2 == 1)
            {
                int first = hq_PlanZooPlaza_first;
                GenRng r5 = hq_PlanZooPlaza_r5;
                int st = hq_PlanZooPlaza_st;
                if (st >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp("plush"), st, 2, 0, ref r5)) Keep(first, false); }
                hq_PlanZooPlaza_first = first;
                hq_PlanZooPlaza_r5 = r5;
                hq_PlanZooPlaza_st = st;
            }
        }
        if (planPhase == 6)
        {
            GenRng r6 = SR(6);
            hq_PlanZooPlaza_r6 = r6;
        }
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 1 + AreaM2(room) / 20 && Fit(2))) PhBreak();
                else { hq_PlanZooPlaza_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanZooPlaza_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 8 && PhLoop(8, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "ma_vending", "ma_claw", "ms_gacha", "ck_bench", "k_bench", "ck_trash", "ck_flowers", "f_lamp", "f_hedge", "k_plant", "ms_cart", "n_planter", "hk_bench", "hk_lantern", "sk_scooter", "ck_toilets", "f_stallBench", "f_stallStool", "ma_prizeWheel", "ma_tickets", "n_statueRing", "f_hedgeCurve", "k_trashcan", "hk_benchShort" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
}
    void PlanZooPlaza_L1()
    {
        int room = pRoom, dx = 0, dz = 0;
        planMax = 1;
        if (planPhase == 0)
        {
            dx = hq_PlanZooPlaza_dx;
            dz = hq_PlanZooPlaza_dz;
            int first = hq_PlanZooPlaza_first;
            string[] kiosks = hq_PlanZooPlaza_kiosks;
            int k = hq_PlanZooPlaza_k;
            GenRng rk = SR(2 + k);
            if (!rk.Chance(k == 0 ? 900 : 650)) { hq_PlanZooPlaza_dx = dx; hq_PlanZooPlaza_dz = dz; hq_PlanZooPlaza_first = first; hq_PlanZooPlaza_kiosks = kiosks; PhEnd(); return; }
            first = nEmit;
            hq_PlanZooPlaza_sel1 = 0;
            if (WallPlace(Dn(kiosks[k]), WM_FAR, dx, dz, WIN_ANY, 0, ref rk) >= 0) hq_PlanZooPlaza_sel1 = 1;
            hq_PlanZooPlaza_dx = dx;
            hq_PlanZooPlaza_dz = dz;
            hq_PlanZooPlaza_first = first;
            hq_PlanZooPlaza_kiosks = kiosks;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!(hq_PlanZooPlaza_sel1 == 1)) PhBreak();
            else
            {
                int first = hq_PlanZooPlaza_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanZooPlaza_first = first;
            }
        }
    }

    void PlanZooPlaza_L2()
    {
        int room = pRoom, dx = 0, dz = 0;
        planMax = 2;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            GenRng r6 = hq_PlanZooPlaza_r6;
            int cell = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = 3; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r6; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r6 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
            hq_PlanZooPlaza_r6 = r6;
            hq_PlanZooPlaza_cell = cell;
        }
        if (planPhase == 1)
        {
            int first = hq_PlanZooPlaza_first;
            int cell = hq_PlanZooPlaza_cell;
            if (cell < 0) { hq_PlanZooPlaza_first = first; hq_PlanZooPlaza_cell = cell; phBreak = true; PhEnd(); return; }
            first = nEmit;
            hq_PlanZooPlaza_sel3 = 0;
            if (FreePlace(Dn("r_tableRound"), cell, 0, 1, 0, H_FREE) >= 0) hq_PlanZooPlaza_sel3 = 1;
            hq_PlanZooPlaza_first = first;
            hq_PlanZooPlaza_cell = cell;
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (!(hq_PlanZooPlaza_sel3 == 1)) PhBreak();
            else
            {
                int first = hq_PlanZooPlaza_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanZooPlaza_first = first;
            }
        }
    }


    // ============================================================================================ KEEPERS' WALK
    // The path between the enclosures: a gravel strip down the middle, benches and bins at the fences, lamps, the
    // keepers' cart or barrow with a bale and buckets, an ice-cream kiosk, flower beds and shrubs.
    void PlanZooWalk()
    {
        planMax = 6;
        int room = pRoom;
        if (planPhase == 0)
        {
            int x0 = roomX0[room], z0 = roomZ0[room], x1 = roomX1[room], z1 = roomZ1[room];
            bool alongX = (x1 - x0) > (z1 - z0);
            int mid = alongX ? (z0 + z1) / 2 : (x0 + x1) / 2;
            for (int k = roomCellStart[room]; k < roomCellStart[room] + roomCellCount[room]; k++)
            {
                int i = roomCells[k], a = alongX ? i / IW : i % IW;
                if (a >= mid - 1 && a <= mid) tags[i] |= GenData.T_PATH;
            }
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (planIter == 0) { hq_RoomWalkable_room = room; PhSubReset(); }
            PhPush(); RoomWalkable_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 2)
        {
            GenRng r = SR(1);
            hp_PlanZooWalk_first = nEmit;
            hq_PlanZooWalk_sel1 = 0;
            if (WallPlace(Dn(r.Chance(500) ? "f_cart" : "m_wheelbarrow"), WM_ANY, 0, 0, WIN_ANY, 0, ref r) >= 0 && Keep(hp_PlanZooWalk_first, false) && Fit(2)) hq_PlanZooWalk_sel1 = 1;
            hq_PlanZooWalk_r = r;
        }
        if (planPhase == 3)
        {
            if (hq_PlanZooWalk_sel1 == 1)
            {
                GenRng r = hq_PlanZooWalk_r;
                hp_PlanZooWalk_first = nEmit;
                hq_PlanZooWalk_sel2 = 0;
                if (GroupAt(GenData.FindGrp(r.Chance(500) ? "hay" : "buckets"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r)) hq_PlanZooWalk_sel2 = 1;
                hq_PlanZooWalk_r = r;
            }
        }
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (!(hq_PlanZooWalk_sel1 == 1 && hq_PlanZooWalk_sel2 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_Keep_first = hp_PlanZooWalk_first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
        if (planPhase == 5)
        {
            GenRng r2 = SR(2);
            if (r2.Chance(600) && Fit(1)) { hp_PlanZooWalk_first = nEmit; if (WallPlace(Dn(r2.Chance(500) ? "ck_food" : "ck_drinks"), WM_ANY, 0, 0, WIN_ANY, 0, ref r2) >= 0) Keep(hp_PlanZooWalk_first, false); }
    
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 6 && PhLoop(6, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "ck_bench", "k_bench", "ck_trash", "f_lamp", "ck_flowers", "f_hedge", "n_planter", "ma_vending", "n_bush", "n_bushSmall", "ck_info", "ck_toilets", "f_hedgeCurve", "n_bushDetailed", "k_benchCushion", "n_statueBlock", "hk_bench", "hk_lantern", "f_cartHigh", "hk_benchShort" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }

    // ============================================================================================ ENCLOSURES
    // One part of the world each: its beasts on the open ground, the water they need, what they climb on and shelter
    // under, the keepers' trough and hay; trees and rocks round the fence.
    void PlanEnclosure()
    {
        int room = pRoom, t = roomType[room];
        planMax = 20;
        if (planPhase == 0)
        {
            int mx = RoomMidX(room), mz = RoomMidZ(room);
            GenRng r = SR(1);
            int wx = mx + (r.Chance(500) ? 600 : -600), wz = mz + (r.Chance(500) ? 600 : -600);
            if (t == GenData.R_ENC_ARCTIC) WaterPatch(room, wx, wz, 3);   // a pool, with room on its bank for the bears
            else if (t == GenData.R_ENC_SAVANNA || t == GenData.R_ENC_WOODS) WaterPatch(room, wx, wz, 2);
            string[] big = null;
            string grp = null;
            string[] pool = null;
            string[] rock = { "n_rockLarge", "n_rockTall", "s_stoneL", "s_rockFlat", "n_bushLarge", "n_bushDetailed" };
            switch (t)
            {
                case GenData.R_ENC_FARM:
                    big = new[] { "p_cow", "p_pig", "p_hog", "p_dog" }; grp = "rabbits";
                    pool = new[] { "gen_coop", "gen_trough", "f_cart", "m_wheelbarrow", "n_stump", "k_bench", "w_pallet", "n_bush" }; break;
                case GenData.R_ENC_SAVANNA:
                    big = new[] { "z_giraffe", "z_elephant", "z_lion" };
                    pool = new[] { "gen_shed", "gen_trough", "n_rockLarge", "s_rockFlat", "n_logLarge", "s_stoneL", "n_bushLarge", "n_grassLarge" }; break;
                case GenData.R_ENC_ARCTIC:
                    big = new[] { "z_polar", "z_polar" }; grp = "penguins";
                    pool = rock; break;
                case GenData.R_ENC_WOODS:
                    big = new[] { "z_deer", "z_fox", "p_beaver" }; grp = "rabbits";
                    pool = new[] { "n_logLarge", "n_stump", "s_logSmall", "n_logStack", "n_bushLarge", "n_bushDetailed", "n_rockLarge", "n_grassLarge" }; break;
                case GenData.R_ENC_JUNGLE:
                    big = new[] { "z_tiger", "z_panda", "z_koala" }; grp = "monkeys";
                    pool = new[] { "gen_shed", "n_logLarge", "s_logSmall", "n_stump", "n_bushLarge", "n_bushDetailed", "n_rockTall", "gen_trough" }; break;
                default:   // the aviary
                    big = new[] { "z_parrot" }; grp = "parrots";
                    pool = new[] { "n_stump", "s_logSmall", "n_planter", "ck_flowers", "n_bushLarge", "n_bushDetailed", "n_bushSmall", "n_rockTall" }; break;
            }
            hq_PlanEnclosure_r = r;
            hq_PlanEnclosure_wx = wx;
            hq_PlanEnclosure_wz = wz;
            hq_PlanEnclosure_big = big;
            hq_PlanEnclosure_grp = grp;
            hq_PlanEnclosure_pool = pool;
        }
        if (planPhase == 1)
        {
            GenRng r = hq_PlanEnclosure_r;
            int wx = hq_PlanEnclosure_wx;
            int wz = hq_PlanEnclosure_wz;
            // what lives on the water first
            if (t == GenData.R_ENC_ARCTIC && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("shore"), GenData.A_OUT_OPEN, 3, WM_NEAR, wx, wz, 0, ref r)) Keep(first, false); }
            hq_PlanEnclosure_r = r;
            hq_PlanEnclosure_wx = wx;
            hq_PlanEnclosure_wz = wz;
        }
        if (planPhase == 2)
        {
            CloseWater(room);
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            string[] big = hq_PlanEnclosure_big;
            int got = 0;
            if (planIter == 0) { hq_Beasts_kinds = big; hq_Beasts_n = t == GenData.R_AVIARY ? 1 : 2 + AreaM2(room) / 12; hq_Beasts_k0 = 10; PhSubReset(); }
            PhPush(); Beasts_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { got = hq_Beasts_ret; PhBreak(); }
            hq_PlanEnclosure_big = big;
            hq_PlanEnclosure_got = got;
        }
        if (planPhase == 4)
        {
            string grp = hq_PlanEnclosure_grp;
            int got = hq_PlanEnclosure_got;
            if (grp != null && Fit(2))
            {
                GenRng rg = SR(20);
                int first = nEmit;
                if (PlaceGroup(GenData.FindGrp(grp), room, GenData.A_OUT_OPEN, rg.Between(GenData.grpMin[GenData.FindGrp(grp)], 3), 0, ref rg) && Keep(first, false)) got++;
            }
            hq_PlanEnclosure_grp = grp;
            hq_PlanEnclosure_got = got;
        }
        if (planPhase == 5)
        {
            int got = hq_PlanEnclosure_got;
            if (t == GenData.R_AVIARY && Fit(2)) { GenRng rb = SR(21); int first = nEmit; if (PlaceGroup(GenData.FindGrp("bugs"), room, GenData.A_OUT_OPEN, 2, 0, ref rb)) Keep(first, false); }
            if (got == 0) MandFail(1);
            GenRng r3 = SR(3);
            hq_PlanEnclosure_got = got;
            hq_PlanEnclosure_r3 = r3;
        }
        if (planPhase == 6)
        {
            GenRng r3 = hq_PlanEnclosure_r3;
            int trees = TreeCount(room, ref r3);
            hq_PlanEnclosure_r3 = r3;
            hq_PlanEnclosure_trees = trees;
        }
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            int trees = hq_PlanEnclosure_trees;
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < trees && t != GenData.R_ENC_ARCTIC)) PhBreak();
                else { hq_PlanEnclosure_k = k; PhSubBegin(); }
            }
            hq_PlanEnclosure_trees = trees;
            if (PhSubActive())
            {
                PhPush(); PlanEnclosure_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 8)
        {
            GenRng r4 = SR(4);
            hq_PlanEnclosure_sel1 = 0;
            if (t != GenData.R_ENC_ARCTIC && t != GenData.R_AVIARY && r4.Chance(700) && Fit(2)) hq_PlanEnclosure_sel1 = 1;
            hq_PlanEnclosure_r4 = r4;
        }
        if (planPhase == 9)
        {
            if (hq_PlanEnclosure_sel1 == 1)
            {
                GenRng r4 = hq_PlanEnclosure_r4;
                int first = nEmit;
                int tr = WallPlace(Dn("gen_trough"), WM_ANY, 0, 0, WIN_ANY, 0, ref r4);
                hq_PlanEnclosure_r4 = r4;
                hq_PlanEnclosure_first = first;
                hq_PlanEnclosure_tr = tr;
            }
        }
        if (planPhase == 10)
        {
            if (hq_PlanEnclosure_sel1 == 1)
            {
                GenRng r4 = hq_PlanEnclosure_r4;
                int first = hq_PlanEnclosure_first;
                int tr = hq_PlanEnclosure_tr;
                if (tr >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp(t == GenData.R_ENC_FARM || t == GenData.R_ENC_SAVANNA ? "hay" : "buckets"), tr, 2, 0, ref r4)) Keep(first, false); }
                hq_PlanEnclosure_r4 = r4;
                hq_PlanEnclosure_first = first;
                hq_PlanEnclosure_tr = tr;
            }
        }
        if (planPhase == 11)
        {
            hq_PlanEnclosure_sel2 = 0;
            if (t == GenData.R_ENC_WOODS && Fit(2)) hq_PlanEnclosure_sel2 = 1;
        }
        if (planPhase == 12)
        {
            if (hq_PlanEnclosure_sel2 == 1)
            {
                GenRng r5 = SR(5);
                hq_PlanEnclosure_r5 = r5;
            }
        }
        if (planPhase == 13 && PhLoop(13, 100000))
        {
            if (!(hq_PlanEnclosure_sel2 == 1)) PhBreak();
            else
            {
                GenRng r5 = hq_PlanEnclosure_r5;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 1; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r5; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r5 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PlanEnclosure_r5 = r5;
                hq_PlanEnclosure_cell = cell;
            }
        }
        if (planPhase == 14)
        {
            if (hq_PlanEnclosure_sel2 == 1)
            {
                GenRng r5 = hq_PlanEnclosure_r5;
                int cell = hq_PlanEnclosure_cell;
                int first = nEmit;
                int st = cell >= 0 ? FreePlace(Dn("n_stump"), cell, r5.Range(360), 0, 0, H_NATURE) : -1;
                hq_PlanEnclosure_r5 = r5;
                hq_PlanEnclosure_cell = cell;
                hq_PlanEnclosure_first = first;
                hq_PlanEnclosure_st = st;
            }
        }
        if (planPhase == 15)
        {
            if (hq_PlanEnclosure_sel2 == 1)
            {
                GenRng r5 = hq_PlanEnclosure_r5;
                int first = hq_PlanEnclosure_first;
                int st = hq_PlanEnclosure_st;
                if (st >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp("mushrooms"), st, 2, 0, ref r5)) Keep(first, false); }
                hq_PlanEnclosure_r5 = r5;
                hq_PlanEnclosure_first = first;
                hq_PlanEnclosure_st = st;
            }
        }
        if (planPhase == 16)
        {
            if (t == GenData.R_AVIARY) { ClearBans(); Borders(30, 3, true); }
            else if (t == GenData.R_ENC_FARM && Fit(2)) { GenRng r6 = SR(6); int first = nEmit; if (PlaceGroup(GenData.FindGrp("hens"), room, GenData.A_OUT_OPEN, 2, 0, ref r6)) Keep(first, false); }
        }
        if (planPhase == 17 && PhLoop(17, 100000))
        {
            string[] pool = hq_PlanEnclosure_pool;
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = pool; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
            hq_PlanEnclosure_pool = pool;
        }
        if (planPhase == 18)
        {
            // and the habitat every enclosure shares: grass tussocks, small shrubs, a flat rock, a log, a stone, a stump
            hq_PlanEnclosure_sel3 = 0;
            if (t != GenData.R_ENC_FARM) hq_PlanEnclosure_sel3 = 1;
            else hq_PlanEnclosure_sel3 = 2;
        }
        if (planPhase == 19 && PhLoop(19, 100000))
        {
            if (!(hq_PlanEnclosure_sel3 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_FillFromPool_k0 = 51; hq_FillFromPool_pool = new string[] { "n_grassLarge", "n_bushSmall", "s_rockFlat", "n_logLarge", "s_stoneL", "n_stump", "n_bush", "s_logSmall", "n_rockTall", "s_block", "n_rockLarge", "n_bushTriangle", "n_stumpRound", "n_rockLargeB", "m_bucketWater", "g_hayBundled", "n_logStack", "n_log" }; PhSubReset(); }
                PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
        if (planPhase == 20 && PhLoop(20, 100000))
        {
            if (!(hq_PlanEnclosure_sel3 == 2)) PhBreak();
            else
            {
                if (planIter == 0) { hq_FillFromPool_k0 = 51; hq_FillFromPool_pool = new string[] { "p_cat", "m_sack", "n_bush", "n_stump", "k_bench" }; PhSubReset(); }
                PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
    }
    void PlanEnclosure_L1()
    {
        int room = pRoom, t = roomType[room];
        planMax = 0;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int k = hq_PlanEnclosure_k;
            if (planIter == 0) { hq_LawnTree_k = 60 + k; PhSubReset(); }
            PhPush(); LawnTree_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }


    // ============================================================================================ KEEPERS' YARD
    void PlanKeeper()
    {
        planMax = 2;
        int room = pRoom;
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            hp_PlanKeeper_first = nEmit;
            if (PlaceGroup(GenData.FindGrp("hay"), room, GenData.A_WALL, 3, EF_MANDATORY, ref r)) Keep(hp_PlanKeeper_first, true); else MandFail(1);
            hp_PlanKeeper_rows = new string[] { "buckets", "sacks", "crates", "tools", "barrels" };
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < hp_PlanKeeper_rows.Length)) PhBreak();
                else { hq_PlanKeeper_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanKeeper_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "gen_shed", "m_wheelbarrow", "f_cart", "f_cartHigh", "gen_trough", "w_pallet", "w_palletLoaded", "gen_tap", "gen_coop", "ms_handTruck", "s_boxLarge" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }
    void PlanKeeper_L1()
    {
        int room = pRoom;
        planMax = 1;
        if (planPhase == 0)
        {
            int k = hq_PlanKeeper_k;
            GenRng rk = SR(2 + k);
            if (!rk.Chance(k < 2 ? 850 : 550) || !Fit(2)) { PhEnd(); return; }
            hp_PlanKeeper_first = nEmit;
            hq_PlanKeeper_sel1 = 0;
            if (PlaceGroup(GenData.FindGrp(hp_PlanKeeper_rows[k]), room, GenData.A_WALL, 2, 0, ref rk)) hq_PlanKeeper_sel1 = 1;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!(hq_PlanKeeper_sel1 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_Keep_first = hp_PlanKeeper_first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
    }


    // ============================================================================================ CABIN ROOM
    // One room for living: the wood stove against a wall with the log basket beside it and an armchair on a rug in
    // front; the kitchen corner (counter, cooker, sink, a small fridge); the table with its chairs; closed bookcases,
    // the coat rack by the door, a sled and a paddle on the wall, a chest, a lamp.
    void PlanCabinMain()
    {
        planMax = 15;
        int room = pRoom, dx = 0, dz = 0;
        if (planPhase == 0)
        {
            dx = hq_PlanCabinMain_dx;
            dz = hq_PlanCabinMain_dz;
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            int first = nEmit;
            int stove = WallPlace(Dn("cb_woodstove"), WM_FAR, dx, dz, WIN_NEVER, EF_MANDATORY, ref r);
            hq_PlanCabinMain_dx = dx;
            hq_PlanCabinMain_dz = dz;
            hq_PlanCabinMain_r = r;
            hq_PlanCabinMain_first = first;
            hq_PlanCabinMain_stove = stove;
        }
        if (planPhase == 1)
        {
            int first = hq_PlanCabinMain_first;
            int stove = hq_PlanCabinMain_stove;
            if (stove < 0 || !Keep(first, true))
            {
                stove = -1;
                MandFail(1);
            }
            hq_PlanCabinMain_first = first;
            hq_PlanCabinMain_stove = stove;
        }
        if (planPhase == 2)
        {
            GenRng r = hq_PlanCabinMain_r;
            int first = hq_PlanCabinMain_first;
            int stove = hq_PlanCabinMain_stove;
            if (stove >= 0 && Fit(1)) { first = nEmit; if (RelBeside(Dn("n_logStack"), stove, r.Chance(500) ? 1 : -1, 120, 0, ref r) >= 0) Keep(first, false); }
            GenRng r2 = SR(2);
            hq_PlanCabinMain_sel1 = 0;
            if (Fit(3)) hq_PlanCabinMain_sel1 = 1;
            hq_PlanCabinMain_r = r;
            hq_PlanCabinMain_first = first;
            hq_PlanCabinMain_stove = stove;
            hq_PlanCabinMain_r2 = r2;
        }
        if (planPhase == 3)
        {
            if (hq_PlanCabinMain_sel1 == 1)
            {
                int first = hq_PlanCabinMain_first;
                GenRng r2 = hq_PlanCabinMain_r2;
                first = nEmit;
                rowN = 3; rowDef[0] = Dn("k_counter"); rowDef[1] = Dn("k_stoveElectric"); rowDef[2] = Dn("k_sink");
                hq_PlanCabinMain_sel2 = 0;
                if (WallRow(0, 2, WM_ANY, 0, 0, WIN_ANY, 0, ref r2) >= 0) hq_PlanCabinMain_sel2 = 1;
                hq_PlanCabinMain_first = first;
                hq_PlanCabinMain_r2 = r2;
            }
        }
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (!(hq_PlanCabinMain_sel1 == 1 && hq_PlanCabinMain_sel2 == 1)) PhBreak();
            else
            {
                int first = hq_PlanCabinMain_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanCabinMain_first = first;
            }
        }
        if (planPhase == 5)
        {
            int first = hq_PlanCabinMain_first;
            GenRng r3 = SR(3);
            if (r3.Chance(650) && Fit(1)) { first = nEmit; if (WallPlace(Dn("k_fridgeSmall"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0) Keep(first, false); }
            GenRng r4 = SR(4);
            hq_PlanCabinMain_sel3 = 0;
            if (Fit(1)) hq_PlanCabinMain_sel3 = 1;
            hq_PlanCabinMain_first = first;
            hq_PlanCabinMain_r4 = r4;
        }
        if (planPhase == 6 && PhLoop(6, 100000))
        {
            if (!(hq_PlanCabinMain_sel3 == 1)) PhBreak();
            else
            {
                GenRng r4 = hq_PlanCabinMain_r4;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 3; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r4; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r4 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PlanCabinMain_r4 = r4;
                hq_PlanCabinMain_cell = cell;
            }
        }
        if (planPhase == 7)
        {
            if (hq_PlanCabinMain_sel3 == 1)
            {
                int first = hq_PlanCabinMain_first;
                GenRng r4 = hq_PlanCabinMain_r4;
                int cell = hq_PlanCabinMain_cell;
                first = nEmit;
                hq_PlanCabinMain_sel4 = 0;
                if (cell >= 0 && FreePlace(Dn(r4.Chance(600) ? "k_table" : "q_table"), cell, r4.Range(2) * 90, 1, 0, H_FREE) >= 0) hq_PlanCabinMain_sel4 = 1;
                hq_PlanCabinMain_first = first;
                hq_PlanCabinMain_r4 = r4;
                hq_PlanCabinMain_cell = cell;
            }
        }
        if (planPhase == 8 && PhLoop(8, 100000))
        {
            if (!(hq_PlanCabinMain_sel3 == 1 && hq_PlanCabinMain_sel4 == 1)) PhBreak();
            else
            {
                int first = hq_PlanCabinMain_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanCabinMain_first = first;
            }
        }
        if (planPhase == 9)
        {
            GenRng r5 = SR(5);
            hq_PlanCabinMain_sel5 = 0;
            if (Fit(1)) hq_PlanCabinMain_sel5 = 1;
            hq_PlanCabinMain_r5 = r5;
        }
        if (planPhase == 10)
        {
            if (hq_PlanCabinMain_sel5 == 1)
            {
                int first = hq_PlanCabinMain_first;
                int stove = hq_PlanCabinMain_stove;
                GenRng r5 = hq_PlanCabinMain_r5;
                int sx = stove >= 0 ? emitX[stove] : RoomMidX(room), sz = stove >= 0 ? emitZ[stove] : RoomMidZ(room);
                first = nEmit;
                hq_PlanCabinMain_sel6 = 0;
                if (WallPlace(Dn(r5.Chance(500) ? "k_recliner" : "k_loungeChair"), WM_NEAR, sx, sz, WIN_AVOID, 0, ref r5) >= 0) hq_PlanCabinMain_sel6 = 1;
                hq_PlanCabinMain_first = first;
                hq_PlanCabinMain_stove = stove;
                hq_PlanCabinMain_r5 = r5;
                hq_PlanCabinMain_sx = sx;
                hq_PlanCabinMain_sz = sz;
            }
        }
        if (planPhase == 11 && PhLoop(11, 100000))
        {
            if (!(hq_PlanCabinMain_sel5 == 1 && hq_PlanCabinMain_sel6 == 1)) PhBreak();
            else
            {
                int first = hq_PlanCabinMain_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanCabinMain_first = first;
            }
        }
        if (planPhase == 12 && PhLoop(12, 100000))
        {
            if (!(hq_PlanCabinMain_sel5 == 1)) PhBreak();
            else
            {
                GenRng r5 = hq_PlanCabinMain_r5;
                int sx = hq_PlanCabinMain_sx;
                int sz = hq_PlanCabinMain_sz;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = WM_NEAR; hq_OpenSpot_tx = sx; hq_OpenSpot_tz = sz; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r5; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r5 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PlanCabinMain_r5 = r5;
                hq_PlanCabinMain_sx = sx;
                hq_PlanCabinMain_sz = sz;
                hq_PlanCabinMain_cell = cell;
            }
        }
        if (planPhase == 13)
        {
            if (hq_PlanCabinMain_sel5 == 1)
            {
                GenRng r5 = hq_PlanCabinMain_r5;
                int cell = hq_PlanCabinMain_cell;
                if (cell >= 0) FreePlace(Dn(r5.Chance(500) ? "k_rug" : "q_rug"), cell, r5.Range(2) * 90, 0, 0, H_FREE);
                hq_PlanCabinMain_r5 = r5;
                hq_PlanCabinMain_cell = cell;
            }
        }
        if (planPhase == 14)
        {
            dx = hq_PlanCabinMain_dx;
            dz = hq_PlanCabinMain_dz;
            int first = hq_PlanCabinMain_first;
            GenRng r6 = SR(6);
            if (r6.Chance(700) && Fit(1)) { first = nEmit; if (WallPlace(Dn("k_coatRack"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r6) >= 0) Keep(first, false); }
            hq_PlanCabinMain_dx = dx;
            hq_PlanCabinMain_dz = dz;
            hq_PlanCabinMain_first = first;
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 15 && PhLoop(15, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "k_bookcaseClosed", "k_bookcaseWide", "hk_sled", "n_paddle", "s_chest", "k_lampSquare", "k_bench", "hk_benchShort", "s_barrel", "k_plant", "q_cabinet", "k_trashcan" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
}

    // ============================================================================================ BUNK ROOM
    void PlanBunkRoom()
    {
        planMax = 7;
        int room = pRoom;
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            int first = nEmit;
            int bunk = WallPlace(Dn("k_bedBunk"), WM_CORNER, 0, 0, WIN_AVOID, EF_MANDATORY, ref r);
            hq_PlanBunkRoom_r = r;
            hq_PlanBunkRoom_first = first;
            hq_PlanBunkRoom_bunk = bunk;
        }
        if (planPhase == 1)
        {
            int first = hq_PlanBunkRoom_first;
            int bunk = hq_PlanBunkRoom_bunk;
            if (bunk < 0 || !Keep(first, true))
            {
                bunk = -1;
                MandFail(1);
            }
            hq_PlanBunkRoom_first = first;
            hq_PlanBunkRoom_bunk = bunk;
        }
        if (planPhase == 2)
        {
            GenRng r = hq_PlanBunkRoom_r;
            int first = hq_PlanBunkRoom_first;
            int bunk = hq_PlanBunkRoom_bunk;
            if (bunk >= 0 && Fit(1)) { first = nEmit; if (RelBeside(Dn("k_bedsideDrawer"), bunk, 0, 50, 0, ref r) >= 0) Keep(first, false); }
            GenRng r2 = SR(2);
            hq_PlanBunkRoom_r = r;
            hq_PlanBunkRoom_first = first;
            hq_PlanBunkRoom_bunk = bunk;
            hq_PlanBunkRoom_r2 = r2;
        }
        if (planPhase == 3)
        {
            int first = hq_PlanBunkRoom_first;
            int bunk = hq_PlanBunkRoom_bunk;
            GenRng r2 = hq_PlanBunkRoom_r2;
            if (Fit(1)) { first = nEmit; if (WallPlace(Dn(r2.Chance(600) ? "k_bedBunk" : "n_cot"), WM_FAR, bunk >= 0 ? emitX[bunk] : 0, bunk >= 0 ? emitZ[bunk] : 0, WIN_AVOID, 0, ref r2) >= 0) Keep(first, false); }
            hq_PlanBunkRoom_first = first;
            hq_PlanBunkRoom_bunk = bunk;
            hq_PlanBunkRoom_r2 = r2;
        }
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "s_chest", "dg_trunkL", "dg_trunkM", "s_bedrollPacked", "k_coatRack", "gen_wardrobe", "k_lampSquare", "hk_sledLong", "n_paddle", "k_bookcaseClosed", "hk_benchShort" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 5)
        {
            GenRng r3 = SR(3);
            hq_PlanBunkRoom_sel1 = 0;
            if (r3.Chance(500)) hq_PlanBunkRoom_sel1 = 1;
            hq_PlanBunkRoom_r3 = r3;
        }
        if (planPhase == 6 && PhLoop(6, 100000))
        {
            if (!(hq_PlanBunkRoom_sel1 == 1)) PhBreak();
            else
            {
                GenRng r3 = hq_PlanBunkRoom_r3;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r3; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r3 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PlanBunkRoom_r3 = r3;
                hq_PlanBunkRoom_cell = cell;
            }
        }
        if (planPhase == 7)
        {
            if (hq_PlanBunkRoom_sel1 == 1)
            {
                int cell = hq_PlanBunkRoom_cell;
                if (cell >= 0) FreePlace(Dn("k_rug"), cell, 0, 0, 0, H_FREE);
                hq_PlanBunkRoom_cell = cell;
            }
        }
    }

    // ============================================================================================ PORCH
    // The decking in front of the cabin: a bench against the cabin wall and the rocking chair, lantern posts at the
    // steps, the log pile, a sled and the canoe with its paddle, a barrel, buckets, pots.
    void PlanPorch()
    {
        planMax = 7;
        int room = pRoom, dx = 0, dz = 0;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            if (planIter == 0) { hq_FrontMarkers_k0 = 30; PhSubReset(); }
            PhPush(); FrontMarkers_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 1)
        {
            dx = hq_PlanPorch_dx;
            dz = hq_PlanPorch_dz;
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            int first = nEmit;
            hq_PlanPorch_dx = dx;
            hq_PlanPorch_dz = dz;
            hq_PlanPorch_r = r;
            hq_PlanPorch_first = first;
        }
        if (planPhase == 2)
        {
            GenRng r = hq_PlanPorch_r;
            hq_PlanPorch_sel1 = 0;
            if (GroupAt(GenData.FindGrp("woodpile"), GenData.A_BUILDING, 2, WM_ANY, 0, 0, 0, ref r)) hq_PlanPorch_sel1 = 1;
            hq_PlanPorch_r = r;
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (!(hq_PlanPorch_sel1 == 1)) PhBreak();
            else
            {
                int first = hq_PlanPorch_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanPorch_first = first;
            }
        }
        if (planPhase == 4)
        {
            GenRng r2 = SR(2);
            hq_PlanPorch_r2 = r2;
        }
        if (planPhase == 5 && PhLoop(5, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 2 && Fit(1))) PhBreak();
                else { hq_PlanPorch_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanPorch_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 6)
        {
            int first = hq_PlanPorch_first;
            GenRng r3 = SR(3);
            if (r3.Chance(600) && Fit(1)) { first = nEmit; if (WallPlace(Dn("n_canoe"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0 && Keep(first, false) && Fit(1)) { first = nEmit; if (WallPlace(Dn("n_paddle"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0) Keep(first, false); } }
            hq_PlanPorch_first = first;
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "hk_bench", "hk_benchShort", "k_recliner", "hk_sled", "hk_sledLong", "s_barrel", "n_logStack", "s_bedrollPacked", "gen_tap", "n_planter" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
}
    void PlanPorch_L1()
    {
        int room = pRoom, dx = 0, dz = 0;
        planMax = 2;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            dx = hq_PlanPorch_dx;
            dz = hq_PlanPorch_dz;
            GenRng r2 = hq_PlanPorch_r2;
            int cell = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = 1; hq_OpenSpot_mode = WM_NEAR; hq_OpenSpot_tx = dx; hq_OpenSpot_tz = dz; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r2; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r2 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
            hq_PlanPorch_dx = dx;
            hq_PlanPorch_dz = dz;
            hq_PlanPorch_r2 = r2;
            hq_PlanPorch_cell = cell;
        }
        if (planPhase == 1)
        {
            int first = hq_PlanPorch_first;
            int cell = hq_PlanPorch_cell;
            first = nEmit;
            hq_PlanPorch_sel2 = 0;
            if (cell >= 0 && FreePlace(Dn("hk_lantern"), cell, 0, 0, 0, H_FREE) >= 0) hq_PlanPorch_sel2 = 1;
            hq_PlanPorch_first = first;
            hq_PlanPorch_cell = cell;
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (!(hq_PlanPorch_sel2 == 1)) PhBreak();
            else
            {
                int first = hq_PlanPorch_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanPorch_first = first;
            }
        }
    }


    // ============================================================================================ WOOD YARD
    void PlanWoodyard()
    {
        planMax = 7;
        int room = pRoom;
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            hq_PlanWoodyard_r = r;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            GenRng r = hq_PlanWoodyard_r;
            int cell = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
            hq_PlanWoodyard_r = r;
            hq_PlanWoodyard_cell = cell;
        }
        if (planPhase == 2)
        {
            GenRng r = hq_PlanWoodyard_r;
            int cell = hq_PlanWoodyard_cell;
            hp_PlanWoodyard_first = nEmit;
            int block = cell >= 0 ? FreePlace(Dn("s_block"), cell, r.Range(360), 0, 0, H_FREE) : -1;
            hq_PlanWoodyard_r = r;
            hq_PlanWoodyard_cell = cell;
            hq_PlanWoodyard_block = block;
        }
        if (planPhase == 3)
        {
            int block = hq_PlanWoodyard_block;
            if (block < 0 || !Keep(hp_PlanWoodyard_first, true))
            {
                block = -1;
                MandFail(1);
            }
            hq_PlanWoodyard_block = block;
        }
        if (planPhase == 4)
        {
            GenRng r = hq_PlanWoodyard_r;
            int block = hq_PlanWoodyard_block;
            if (block >= 0 && Fit(1)) { hp_PlanWoodyard_first = nEmit; if (RelBeside(Dn(r.Chance(500) ? "s_axe" : "s_axeB"), block, r.Chance(500) ? 1 : -1, 60, 0, ref r) >= 0) Keep(hp_PlanWoodyard_first, false); }
            hq_PlanWoodyard_r = r;
            hq_PlanWoodyard_block = block;
        }
        if (planPhase == 5)
        {
            GenRng r2 = SR(2);
            if (Fit(2)) { hp_PlanWoodyard_first = nEmit; if (PlaceGroup(GenData.FindGrp("woodpile"), room, GenData.A_WALL, 3, 0, ref r2)) Keep(hp_PlanWoodyard_first, false); }
        }
        if (planPhase == 6)
        {
            GenRng r3 = SR(3);
            if (r3.Chance(650) && Fit(2)) { hp_PlanWoodyard_first = nEmit; if (PlaceGroup(GenData.FindGrp("tools"), room, GenData.A_WALL, 2, 0, ref r3)) Keep(hp_PlanWoodyard_first, false); }
    
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "n_logStackLarge", "gen_shed", "s_anvil", "s_grind", "s_workbench", "m_wheelbarrow", "s_planks", "w_logs", "s_barrel", "s_shovelB", "s_logSmall" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }

    // ============================================================================================ CAMPGROUND
    // The fire ring with its seats and wood pile, the spit and the fish stand by the fire, tents round it with
    // bedrolls in front, a camp cot, the chest, the canoe at the fence, a lantern post, buckets.
    // Revision 29 (Cor: purposes outdoors): a campsite is laid out by one of three rules, its tents in a pattern a
    // hunter can count (helpers in MapGenPlansYard.cs):
    //   ARC       2 to 4 tents in an arc facing one fire, across it from the gate; log seats on the gate's side of
    //             the fire; a lantern and a bedroll (or a cot) beside each tent; the wood pile and the block at the edge
    //   TWO ROWS  tents in two rows facing across a path down the middle, the fire in the middle of the path
    //   PITCHES   each tent with its own small fire and seat in front of it, at the same offset, in a grid
    void PlanCampsite()
    {
        planMax = 8;
        int room = pRoom;
        string[] tents = { "s_tentCanvas", "s_tentHalf", "n_tentBig", "n_tentSmallOpen" };   // revision 30: not the bare frame (s_tent)
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            YardAnchor(room, ref r);   // (FindGates too: the fire ring keeps its seats off the gates' lines)
            hq_Yard_rule = r.Range(3);
            planB = r.Range(tents.Length);
            int tent = Dn(tents[planB]);
            hq_Yard_a = tent; hq_Yard_got = 0; planA = -1;
            hq_Yard_want = 2 + AreaM2(room) / 24;
            if (hq_Yard_want > 4) hq_Yard_want = 4;
            yardN = 0;
            if (hq_Yard_rule == 1)
            {
                // TWO ROWS along the room's long axis, facing the path down the middle
                bool alongX = RoomLongAxis(room) == 0;
                int a0 = alongX ? YardBox(room, 0, 700) : YardBox(room, 2, 700), a1 = alongX ? YardBox(room, 1, 700) : YardBox(room, 3, 700);
                int m = alongX ? RoomMidZ(room) : RoomMidX(room), ma = (a0 + a1) / 2;
                int p = Sx(tent) + 1300, n = (a1 - a0 - Sx(tent)) / p + 1, off = 1300 + Sz(tent) / 2;
                if (n < 1) n = 1;
                if (n > 4) n = 4;
                for (int side = -1; side <= 1; side += 2)
                    for (int j = 0; j < n; j++)
                    {
                        int a = ma + (2 * j - (n - 1)) * p / 2;
                        yardX[yardN] = alongX ? a : m + side * off; yardZ[yardN] = alongX ? m + side * off : a;
                        // facing the path: the tent's front (+z) towards the middle line
                        yardYaw[yardN] = alongX ? (side > 0 ? 180 : 0) : (side > 0 ? 270 : 90);
                        yardRow[yardN] = side < 0 ? 0 : 1; yardSet[yardN] = -1; yardN++;
                    }
                hq_Yard_want = yardN;
            }
            else if (hq_Yard_rule == 2)
            {
                // PITCHES: a tent, its fire and its seat in front, the same each time
                GridSpots(room, Sx(tent) + 300, Sz(tent) + 2700, 1100, 900);
            }
            hq_Yard_r = r;
            if (TraceRoom == room) System.Console.Error.WriteLine("  campsite rule " + hq_Yard_rule + " spots " + yardN + " want " + hq_Yard_want);
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            // the fire: ARC rings it in the middle; TWO ROWS sets it in the path's middle
            if (hq_Yard_rule == 2) PhBreak();
            else if (hq_Yard_rule == 1) { planA = YardOne(Dn("s_campfirePit"), RoomMidX(room), RoomMidZ(room), 0); PhBreak(); }
            else
            {
                GenRng r = hq_Yard_r;
                if (planIter == 0) { hq_FireRing_minClr = 6; hq_FireRing_tx = RoomMidX(room); hq_FireRing_tz = RoomMidZ(room); hq_FireRing_soft = false; hq_FireRing_rng = r; PhSubReset(); }
                PhPush(); FireRing_Body(); bool fin = PhAdvance(); PhPop();
                if (fin)
                {
                    r = hq_FireRing_rng; planA = hq_FireRing_ret; PhBreak();
                    if (planA < 0) planA = YardOne(Dn("s_campfirePit"), RoomMidX(room), RoomMidZ(room), 0);   // revision 30: a camp has its fire
                }
                hq_Yard_r = r;
            }
        }
        if (planPhase == 2)
        {
            // ARC: the spots, across the fire from the gate
            if (hq_Yard_rule == 0)
            {
                GenRng r = hq_Yard_r;
                int fx = planA >= 0 ? emitX[planA] : RoomMidX(room), fz = planA >= 0 ? emitZ[planA] : RoomMidZ(room);
                int b0 = (int)(System.Math.Atan2(fx - yardGX, fz - yardGZ) * 180 / System.Math.PI);
                if (yardGX == fx && yardGZ == fz) b0 = r.Range(4) * 90;
                int n = hq_Yard_want, spread = n <= 2 ? 70 : 50, rad = 2400 + Sz(hq_Yard_a) / 2;
                // revision 30: in a small camp the ring comes in so the tents stay inside the fence (an arc of 3.2 m
                // round a fire in the middle of an 8 m square put them over it)
                int roomMin = (roomX0[room]) * GenData.INNER_MM, roomMax = (roomX1[room] + 1) * GenData.INNER_MM;
                int room2Min = (roomZ0[room]) * GenData.INNER_MM, room2Max = (roomZ1[room] + 1) * GenData.INNER_MM;
                int edge = fx - roomMin;
                if (roomMax - fx < edge) edge = roomMax - fx;
                if (fz - room2Min < edge) edge = fz - room2Min;
                if (room2Max - fz < edge) edge = room2Max - fz;
                int fit = edge - Sz(hq_Yard_a) / 2 - Sx(hq_Yard_a) / 2 - 200;
                if (rad > fit) rad = fit;
                if (rad < 1500 + Sz(hq_Yard_a) / 2) rad = 1500 + Sz(hq_Yard_a) / 2;
                for (int k = 0; k < n; k++)
                {
                    int a = Norm(b0 + spread * (2 * k - (n - 1)) / 2);
                    yardX[yardN] = fx + rad * sinTab[a] / 4096; yardZ[yardN] = fz + rad * sinTab[(a + 90) % 360] / 4096;
                    yardYaw[yardN] = Norm(a + 180); yardRow[yardN] = 0; yardSet[yardN] = -1; yardN++;
                }
                hq_Yard_r = r;
            }
        }
        // the tents, one a step, each with its own things beside it (or, PITCHES, its fire and its seat in front)
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (planIter >= yardN || hq_Yard_got >= hq_Yard_want) PhBreak();
            else
            {
                int i = planIter, tent = hq_Yard_a, first = nEmit;
                GenRng r = SR(10 + i);
                int px = yardX[i], pz = yardZ[i], a = Norm(yardYaw[i]);
                if (hq_Yard_rule == 2)
                {
                    // the tent at the back of its pitch
                    int back = (Sz(tent) + 2700) / 2 - Sz(tent) / 2;
                    px -= back * sinTab[a] / 4096; pz -= back * sinTab[(a + 90) % 360] / 4096;
                }
                int t = StampAt(tent, room, px, pz, a, 0, -1, H_FREE, true);
                bool ok = t >= 0;
                if (ok && hq_Yard_rule == 2)
                {
                    ok = Rel(Dn("s_campfirePit"), t, 0, Sz(tent) / 2 + 1100, 0, 0) >= 0;
                    if (ok) ok = Rel(Dn("s_logSmall"), t, 0, Sz(tent) / 2 + 2100, 90, 0) >= 0;
                }
                else if (ok)
                {
                    // the lantern on one side, the bedroll (or a cot) on the other: the same sides at every tent
                    RelBeside(Dn("hk_lantern"), t, -1, 150, 0, ref r);
                    RelBeside(Dn(r.Chance(650) ? "s_bedroll" : "n_cot"), t, 1, 150, 0, ref r);
                }
                if (!ok) TakeBack(first);
                else if (Keep(first, false)) { hq_Yard_got++; yardSet[i] = t; }
            }
        }
        // revision 30 (Cor: they don't really look like campsites): the tents the rule could not place go round the
        // fire where there is room, so a camp has two at the least
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (!PhSubActive())
            {
                if (!(planIter < 6 && hq_Yard_got < 2 && planA >= 0 && hq_Yard_rule != 2)) PhBreak();
                else
                {
                    hq_AroundFire_def = hq_Yard_a; hq_AroundFire_fire = planA; hq_AroundFire_relYaw = 180;
                    hq_AroundFire_distMin = 1700 + Sz(hq_Yard_a) / 2; hq_AroundFire_distMax = 4200; hq_AroundFire_flags = 0;
                    hq_AroundFire_must = false; hq_AroundFire_offGates = true; hq_AroundFire_rng = SR(30 + planIter);
                    PhSubBegin();
                }
            }
            if (PhSubActive())
            {
                PhPush(); AroundFire_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { PhSubEnd(); if (hq_AroundFire_ret >= 0) hq_Yard_got++; } else PhStay();
            }
        }
        if (planPhase == 5)
        {
            // ARC: log seats on the gate's side of the fire, along its ring
            if (hq_Yard_rule == 0 && planA >= 0)
            {
                int fx = emitX[planA], fz = emitZ[planA];
                int b0 = (int)(System.Math.Atan2(yardGX - fx, yardGZ - fz) * 180 / System.Math.PI);
                int seat = Dn("s_logSmall");
                for (int k = -1; k <= 1; k += 2)
                {
                    int a = Norm(b0 + 28 * k);
                    YardOne(seat, fx + 1500 * sinTab[a] / 4096, fz + 1500 * sinTab[(a + 90) % 360] / 4096, Norm(a + 90));
                }
            }
        }
        if (planPhase == 6 && PhLoop(6, 100000))
        {
            // the wood pile and the chopping block at the edge (not on PITCHES: each has its own fire)
            if (hq_Yard_rule == 2 || planA < 0) PhBreak();
            else
            {
                GenRng r2 = hq_Yard_r;
                if (planIter == 0) { hq_WoodPile_fire = planA; hq_WoodPile_atEdge = true; hq_WoodPile_rng = r2; PhSubReset(); }
                PhPush(); WoodPile_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r2 = hq_WoodPile_rng; PhBreak(); }
                hq_Yard_r = r2;
            }
        }
        if (planPhase == 7)
        {
            string[] pool = { "n_canoe", "s_chest", "s_barrel" };
            GenRng r = SR(6);
            YardAccent(pool, false, ref r);
            ClearBans();
        }
        if (planPhase == 8 && PhLoop(8, 100000))
        {
            if (planIter == 0) { hq_Borders_k0 = 10; hq_Borders_max = 3; hq_Borders_soil = false; PhSubReset(); }
            PhPush(); Borders_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) PhBreak();
        }
    }

    // phase state of the split plans (Tools: autophase2)
    int hp_PlanZooWalk_first;
    int hp_PlanKeeper_first;
    string[] hp_PlanKeeper_rows;
    int hp_PlanWoodyard_first;

    // locals of the split plans and helpers, kept from one phase to the next (autophase4)
    int hq_Beasts_cell;
    int hq_Beasts_def;
    int hq_Beasts_e;
    int hq_Beasts_first;
    int hq_Beasts_got;
    int hq_Beasts_k;
    int hq_Beasts_k0;
    string[] hq_Beasts_kinds;
    int hq_Beasts_n;
    int hq_Beasts_ret;
    int hq_Beasts_ring;
    GenRng hq_Beasts_rk;
    int hq_PlanBunkRoom_bunk;
    int hq_PlanBunkRoom_cell;
    int hq_PlanBunkRoom_first;
    GenRng hq_PlanBunkRoom_r;
    GenRng hq_PlanBunkRoom_r2;
    GenRng hq_PlanBunkRoom_r3;
    int hq_PlanBunkRoom_sel1;
    int hq_PlanCabinMain_cell;
    int hq_PlanCabinMain_dx;
    int hq_PlanCabinMain_dz;
    int hq_PlanCabinMain_first;
    GenRng hq_PlanCabinMain_r;
    GenRng hq_PlanCabinMain_r2;
    GenRng hq_PlanCabinMain_r4;
    GenRng hq_PlanCabinMain_r5;
    int hq_PlanCabinMain_sel1;
    int hq_PlanCabinMain_sel2;
    int hq_PlanCabinMain_sel3;
    int hq_PlanCabinMain_sel4;
    int hq_PlanCabinMain_sel5;
    int hq_PlanCabinMain_sel6;
    int hq_PlanCabinMain_stove;
    int hq_PlanCabinMain_sx;
    int hq_PlanCabinMain_sz;
    string[] hq_PlanEnclosure_big;
    int hq_PlanEnclosure_cell;
    int hq_PlanEnclosure_first;
    int hq_PlanEnclosure_got;
    string hq_PlanEnclosure_grp;
    int hq_PlanEnclosure_k;
    string[] hq_PlanEnclosure_pool;
    GenRng hq_PlanEnclosure_r;
    GenRng hq_PlanEnclosure_r3;
    GenRng hq_PlanEnclosure_r4;
    GenRng hq_PlanEnclosure_r5;
    int hq_PlanEnclosure_sel1;
    int hq_PlanEnclosure_sel2;
    int hq_PlanEnclosure_sel3;
    int hq_PlanEnclosure_st;
    int hq_PlanEnclosure_tr;
    int hq_PlanEnclosure_trees;
    int hq_PlanEnclosure_wx;
    int hq_PlanEnclosure_wz;
    int hq_PlanKeeper_k;
    int hq_PlanKeeper_sel1;
    int hq_PlanPorch_cell;
    int hq_PlanPorch_dx;
    int hq_PlanPorch_dz;
    int hq_PlanPorch_first;
    int hq_PlanPorch_k;
    GenRng hq_PlanPorch_r;
    GenRng hq_PlanPorch_r2;
    int hq_PlanPorch_sel1;
    int hq_PlanPorch_sel2;
    int hq_PlanWoodyard_block;
    int hq_PlanWoodyard_cell;
    GenRng hq_PlanWoodyard_r;
    int hq_PlanZooPlaza_cell;
    int hq_PlanZooPlaza_dx;
    int hq_PlanZooPlaza_dz;
    int hq_PlanZooPlaza_first;
    int hq_PlanZooPlaza_k;
    string[] hq_PlanZooPlaza_kiosks;
    GenRng hq_PlanZooPlaza_r;
    GenRng hq_PlanZooPlaza_r5;
    GenRng hq_PlanZooPlaza_r6;
    int hq_PlanZooPlaza_sel1;
    int hq_PlanZooPlaza_sel2;
    int hq_PlanZooPlaza_sel3;
    int hq_PlanZooPlaza_st;
    GenRng hq_PlanZooWalk_r;
    int hq_PlanZooWalk_sel1;
    int hq_PlanZooWalk_sel2;
}
