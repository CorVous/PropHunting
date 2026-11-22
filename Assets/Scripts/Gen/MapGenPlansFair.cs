// MapGen, revision 12 plans: the funfair (research 18, pick 4). The arcade hall on the lane (machines along the walls
// and back to back in the middle, the prize counter by the door), its snack bar and prize store; in front the midway
// of stalls, behind and beside it the rides yard, where the coaster cars stand in the station between queue rails.
public partial class MapGen
{
    // ============================================================================================ ARCADE HALL
    void PlanArcade()
    {
        planMax = 9;
        int room = pRoom, cells = roomOuterCells[room];
        if (planPhase == 0)
        {
            int dx = 0, dz = 0;
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            // the prize counter by the door: the till, the prize shelves behind it, the ticket machine
            hp_PlanArcade_first = nEmit;
            int till = WallPlace(Dn("ma_register"), WM_NEAR, dx, dz, WIN_ANY, EF_MANDATORY, ref r);
            hq_PlanArcade_dx = dx;
            hq_PlanArcade_dz = dz;
            hq_PlanArcade_r = r;
            hq_PlanArcade_till = till;
        }
        if (planPhase == 1)
        {
            int till = hq_PlanArcade_till;
            if (till < 0 || !Keep(hp_PlanArcade_first, true))
            {
                till = -1;
                MandFail(1);
            }
            hq_PlanArcade_till = till;
        }
        if (planPhase == 2)
        {
            GenRng r = hq_PlanArcade_r;
            int till = hq_PlanArcade_till;
            if (till >= 0 && Fit(1)) { hp_PlanArcade_first = nEmit; if (RelBeside(Dn("ma_prizes"), till, r.Chance(500) ? 1 : -1, 100, 0, ref r) >= 0) Keep(hp_PlanArcade_first, false); }
            GenRng r2 = SR(2);
            hq_PlanArcade_r = r;
            hq_PlanArcade_till = till;
            hq_PlanArcade_r2 = r2;
        }
        if (planPhase == 3)
        {
            int dx = hq_PlanArcade_dx;
            int dz = hq_PlanArcade_dz;
            GenRng r2 = hq_PlanArcade_r2;
            if (Fit(1)) { hp_PlanArcade_first = nEmit; if (WallPlace(Dn("ma_tickets"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r2) >= 0) Keep(hp_PlanArcade_first, false); }
            // machines along the walls, a run at a time, of different kinds
            hp_PlanArcade_wall = new string[] { "ma_pinball", "ma_slots", "ma_arcade", "ma_dance", "ma_basketball", "ma_claw", "ma_prizeWheel", "ma_vending", "ms_gacha" };
            hq_PlanArcade_dx = dx;
            hq_PlanArcade_dz = dz;
            hq_PlanArcade_r2 = r2;
        }
        if (planPhase == 4)
        {
            hp_PlanArcade_r3 = SR(3);
            hp_PlanArcade_w0 = hp_PlanArcade_r3.Range(hp_PlanArcade_wall.Length);
        }
        if (planPhase == 5 && PhLoop(5, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 2 + cells && Fit(2))) PhBreak();
                else { hq_PlanArcade_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanArcade_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 6)
        {
            // the middle: air hockey tables, and machines standing free with room round them
            hp_PlanArcade_r4 = SR(4);
        }
        if (planPhase == 7 && PhLoop(7, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 1 + cells / 2 && Fit(1))) PhBreak();
                else { hq_PlanArcade_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanArcade_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 8)
        {
            GenRng r5 = SR(5);
            if (Fit(2)) { hp_PlanArcade_first = nEmit; if (PlaceGroup(GenData.FindGrp("plush"), room, GenData.A_WALL, 2, 0, ref r5)) Keep(hp_PlanArcade_first, false); }
    
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 9 && PhLoop(9, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "ma_pinball", "ma_slots", "ma_arcade", "ma_dance", "ma_basketball", "ma_claw", "ma_prizeWheel", "ma_vending", "ms_gacha", "hb_stool", "k_trashcan", "ck_bench", "k_plant" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }
    void PlanArcade_L1()
    {
        int room = pRoom, cells = roomOuterCells[room];
        planMax = 1;
        if (planPhase == 0)
        {
            int k = hq_PlanArcade_k;
            hp_PlanArcade_first = nEmit;
            rowN = 2; rowDef[0] = Dn(hp_PlanArcade_wall[(hp_PlanArcade_w0 + 2 * k) % hp_PlanArcade_wall.Length]); rowDef[1] = Dn(hp_PlanArcade_wall[(hp_PlanArcade_w0 + 2 * k + 1) % hp_PlanArcade_wall.Length]);
            hq_PlanArcade_sel1 = 0;
            if (WallRow(0, 2, WM_ANY, 0, 0, WIN_AVOID, 0, ref hp_PlanArcade_r3) >= 0) hq_PlanArcade_sel1 = 1;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!(hq_PlanArcade_sel1 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_Keep_first = hp_PlanArcade_first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
    }

    void PlanArcade_L2()
    {
        int room = pRoom, cells = roomOuterCells[room];
        planMax = 2;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            int cell = 0;
            if (planIter == 0) { hq_OpenSpot_minClr = 3; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = hp_PlanArcade_r4; PhSubReset(); }
            PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { hp_PlanArcade_r4 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
            hq_PlanArcade_cell = cell;
        }
        if (planPhase == 1)
        {
            int cell = hq_PlanArcade_cell;
            if (cell < 0) { hq_PlanArcade_cell = cell; phBreak = true; PhEnd(); return; }
            hp_PlanArcade_first = nEmit;
            hq_PlanArcade_sel2 = 0;
            if (FreePlace(Dn("ma_airHockey"), cell, hp_PlanArcade_r4.Range(2) * 90, 1, 0, H_FREE) >= 0) hq_PlanArcade_sel2 = 1;
            hq_PlanArcade_cell = cell;
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (!(hq_PlanArcade_sel2 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_Keep_first = hp_PlanArcade_first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
    }


    // ============================================================================================ SNACK BAR
    void PlanSnackbar()
    {
        planMax = 8;
        int room = pRoom;
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            hp_PlanSnackbar_first = nEmit;
            // the counter and the cooker along one wall (three pieces of 1.4 m are longer than a 4 m wall), the sink at another
            rowN = 2; rowDef[0] = Dn("r_counter"); rowDef[1] = Dn("r_stove");
            if (WallRow(0, 1, WM_ANY, 0, 0, WIN_ANY, EF_MANDATORY, ref r) >= 0) Keep(hp_PlanSnackbar_first, true); else MandFail(1);
            hq_PlanSnackbar_r = r;
        }
        if (planPhase == 1)
        {
            GenRng r = hq_PlanSnackbar_r;
            if (Fit(1)) { hp_PlanSnackbar_first = nEmit; if (WallPlace(Dn("r_counterSink"), WM_ANY, 0, 0, WIN_ANY, 0, ref r) >= 0) Keep(hp_PlanSnackbar_first, false); }
            hq_PlanSnackbar_r = r;
        }
        if (planPhase == 2)
        {
            GenRng r2 = SR(2);
            if (Fit(1)) { hp_PlanSnackbar_first = nEmit; if (WallPlace(Dn(r2.Chance(500) ? "r_fridge" : "ms_freezers"), WM_ANY, 0, 0, WIN_ANY, 0, ref r2) >= 0) Keep(hp_PlanSnackbar_first, false); }
        }
        if (planPhase == 3)
        {
            GenRng r3 = SR(3);
            hq_PlanSnackbar_sel1 = 0;
            if (Fit(1)) hq_PlanSnackbar_sel1 = 1;
            hq_PlanSnackbar_r3 = r3;
        }
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (!(hq_PlanSnackbar_sel1 == 1)) PhBreak();
            else
            {
                GenRng r3 = hq_PlanSnackbar_r3;
                int cell = 0;
                if (planIter == 0) { hq_OpenSpot_minClr = 2; hq_OpenSpot_mode = WM_ANY; hq_OpenSpot_tx = 0; hq_OpenSpot_tz = 0; hq_OpenSpot_need = 0; hq_OpenSpot_rng = r3; PhSubReset(); }
                PhPush(); OpenSpot_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { r3 = hq_OpenSpot_rng; cell = hq_OpenSpot_ret; PhBreak(); }
                hq_PlanSnackbar_r3 = r3;
                hq_PlanSnackbar_cell = cell;
            }
        }
        if (planPhase == 5)
        {
            if (hq_PlanSnackbar_sel1 == 1)
            {
                int cell = hq_PlanSnackbar_cell;
                hp_PlanSnackbar_first = nEmit;
                hq_PlanSnackbar_sel2 = 0;
                if (cell >= 0 && FreePlace(Dn("r_tableRound"), cell, 0, 1, 0, H_FREE) >= 0) hq_PlanSnackbar_sel2 = 1;
                hq_PlanSnackbar_cell = cell;
            }
        }
        if (planPhase == 6 && PhLoop(6, 100000))
        {
            if (!(hq_PlanSnackbar_sel1 == 1 && hq_PlanSnackbar_sel2 == 1)) PhBreak();
            else
            {
                if (planIter == 0) { hq_Keep_first = hp_PlanSnackbar_first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
        if (planPhase == 7)
        {
            GenRng r4 = SR(4);
            if (r4.Chance(600) && Fit(2)) { hp_PlanSnackbar_first = nEmit; if (PlaceGroup(GenData.FindGrp("crates"), room, GenData.A_WALL, 2, 0, ref r4)) Keep(hp_PlanSnackbar_first, false); }
    
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 8 && PhLoop(8, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "ms_freezer", "ma_vending", "ms_dispCans", "k_trashcan", "r_stool", "ms_freezers", "r_fridge" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }

    // ============================================================================================ PRIZE STORE
    void PlanPrizeStore()
    {
        planMax = 4;
        int room = pRoom;
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            hq_PlanPrizeStore_r = r;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            GenRng r = hq_PlanPrizeStore_r;
            bool ht1 = false;
            if (planIter == 0) { hq_RackRun_n = 2; hq_RackRun_a = Dn("gen_rack"); hq_RackRun_b = Dn("gen_rack"); hq_RackRun_mode = WM_CENTRE; hq_RackRun_tx = 0; hq_RackRun_tz = 0; hq_RackRun_rng = r; PhSubReset(); }
            PhPush(); RackRun_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_RackRun_rng; ht1 = hq_RackRun_ret; PhBreak(); }
            hq_PlanPrizeStore_r = r;
            hq_PlanPrizeStore_ht1 = ht1;
        }
        if (planPhase == 2)
        {
            bool ht1 = hq_PlanPrizeStore_ht1;
            if (!ht1)
            {
                MandFail(1);
            }
            hp_PlanPrizeStore_rows = new string[] { "plush", "boxes", "balls", "toyCars", "boardToys", "cardboard" };
            hq_PlanPrizeStore_ht1 = ht1;
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < hp_PlanPrizeStore_rows.Length)) PhBreak();
                else { hq_PlanPrizeStore_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanPrizeStore_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "ma_prizes", "s_boxLarge", "ms_handTruck", "dg_boxL", "gen_rack" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }
    void PlanPrizeStore_L1()
    {
        int room = pRoom;
        planMax = 1;
        if (planPhase == 0)
        {
            int k = hq_PlanPrizeStore_k;
            GenRng rk = SR(2 + k);
            if (!rk.Chance(k < 2 ? 850 : 550) || !Fit(2)) { PhEnd(); return; }
            int first = nEmit;
            hq_PlanPrizeStore_sel1 = 0;
            if (PlaceGroup(GenData.FindGrp(hp_PlanPrizeStore_rows[k]), room, GenData.A_WALL, 2, 0, ref rk)) hq_PlanPrizeStore_sel1 = 1;
            hq_PlanPrizeStore_first = first;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            if (!(hq_PlanPrizeStore_sel1 == 1)) PhBreak();
            else
            {
                int first = hq_PlanPrizeStore_first;
                if (planIter == 0) { hq_Keep_first = first; hq_Keep_must = false; PhSubReset(); }
                PhPush(); Keep_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
                hq_PlanPrizeStore_first = first;
            }
        }
    }


    // ============================================================================================ MIDWAY
    // The paved front: game and food stalls in a row along the fence, the ticket booth and the drinks kiosk by the
    // lane, a claw machine and the prize wheel set out, benches, bins, lantern posts, flower beds, the circus tent.
    void PlanMidway()
    {
        planMax = 3;
        int room = pRoom, dx = 0, dz = 0;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            if (planIter == 0) { hq_FrontMarkers_k0 = 30; PhSubReset(); }
            PhPush(); FrontMarkers_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (planPhase == 1)
        {
            dx = hq_PlanMidway_dx;
            dz = hq_PlanMidway_dz;
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            string[] stalls = { "f_stallRed", "f_stallGreen", "ck_food", "ck_drinks", "ck_info" };
            int s0 = r.Range(stalls.Length);
            // revision 30 (Cor: there can be variation): one stall to as many as fit, in a row along one fence or
            // spread round the midway
            hq_Mid_want = 1 + r.Range(2 + AreaM2(room) / 24);
            hq_Mid_row = r.Chance(500) ? 1 : 0;
            hq_Mid_last = -1;
            hq_PlanMidway_dx = dx;
            hq_PlanMidway_dz = dz;
            hq_PlanMidway_stalls = stalls;
            hq_PlanMidway_s0 = s0;
        }
        if (planPhase == 2 && PhLoop(2, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < hq_Mid_want && Fit(1))) PhBreak();
                else { hq_PlanMidway_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanMidway_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "ma_claw", "ma_prizeWheel", "ma_basketball", "ma_vending", "ck_toilets", "ck_bench", "ck_trash", "ck_flowers", "f_lamp", "hk_lantern", "mb_circus", "r_tableRound", "n_planter" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
}
    void PlanMidway_L1()
    {
        int room = pRoom, dx = 0, dz = 0;
        planMax = 2;
        if (planPhase == 0)
        {
            string[] stalls = hq_PlanMidway_stalls;
            int s0 = hq_PlanMidway_s0;
            int k = hq_PlanMidway_k;
            GenRng rk = SR(2 + k);
            int first = nEmit;
            int near = hq_Mid_row != 0 && hq_Mid_last >= 0 ? hq_Mid_last : -1;
            int st = WallPlace(Dn(stalls[(s0 + k) % stalls.Length]), near >= 0 ? WM_NEAR : WM_ANY, near >= 0 ? emitX[near] : 0, near >= 0 ? emitZ[near] : 0, WIN_ANY, 0, ref rk);
            hq_PlanMidway_stalls = stalls;
            hq_PlanMidway_s0 = s0;
            hq_PlanMidway_rk = rk;
            hq_PlanMidway_first = first;
            hq_PlanMidway_st = st;
        }
        if (planPhase == 1)
        {
            int first = hq_PlanMidway_first;
            int st = hq_PlanMidway_st;
            if (st < 0 || !Keep(first, false))
            {
                { hq_PlanMidway_first = first; hq_PlanMidway_st = st; PhEnd(); return; }
            }
            hq_Mid_last = st;
            hq_PlanMidway_first = first;
            hq_PlanMidway_st = st;
        }
        if (planPhase == 2)
        {
            GenRng rk = hq_PlanMidway_rk;
            int first = hq_PlanMidway_first;
            int st = hq_PlanMidway_st;
            // a game stall's prizes beside it
            if (GenData.defId[emitDef[st]].StartsWith("f_stall") && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp(rk.Chance(500) ? "plush" : "balls"), st, 2, 0, ref rk)) Keep(first, false); }
            hq_PlanMidway_rk = rk;
            hq_PlanMidway_first = first;
            hq_PlanMidway_st = st;
        }
    }


    // ============================================================================================ RIDES YARD
    // Where the coaster stands: its cars in a row at the station, the station fence and gate, the ride's entrance and
    // exit at the edge, queue rails in rows, the log-flume boat and the monorail car, a circus tent, benches, bins.
    void PlanRides()
    {
        planMax = 4;
        int room = pRoom;
        if (planPhase == 0)
        {
            GenRng r = SR(1);
            hp_PlanRides_first = nEmit;
            if (PlaceGroup(GenData.FindGrp("coasterCars"), room, GenData.A_OUT_OPEN, Fit(3) ? 3 : 2, EF_MANDATORY, ref r)) Keep(hp_PlanRides_first, true); else MandFail(1);
        }
        if (planPhase == 1)
        {
            GenRng r2 = SR(2);
            // revision 30 (Cor: there can be variation): no queue rails now and then, else one to four
            if (Fit(2) && r2.Chance(800)) { hp_PlanRides_first = nEmit; if (PlaceGroup(GenData.FindGrp("queue"), room, GenData.A_OUT_OPEN, r2.Between(1, 4), 0, ref r2)) Keep(hp_PlanRides_first, false); }
        }
        if (planPhase == 2)
        {
            // the go-kart track's karts parked in a row, now and then
            GenRng r4 = SR(4);
            if (r4.Chance(500) && Fit(2)) { hp_PlanRides_first = nEmit; if (PlaceGroup(GenData.FindGrp("karts"), room, GenData.A_OUT_OPEN, r4.Between(2, 3), 0, ref r4)) Keep(hp_PlanRides_first, false); }
        }
        if (planPhase == 3)
        {
            GenRng r3 = SR(3);
            if (Fit(1)) { hp_PlanRides_first = nEmit; if (WallPlace(Dn("ck_rideIn"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0 && Keep(hp_PlanRides_first, false) && Fit(1)) { hp_PlanRides_first = nEmit; if (WallPlace(Dn("ck_rideOut"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0) Keep(hp_PlanRides_first, false); } }
    
        }
        // the pool a piece a step (Cor: build freezes)
        if (planPhase == 4 && PhLoop(4, 100000))
        {
            if (planIter == 0) { hq_FillFromPool_k0 = 50; hq_FillFromPool_pool = new string[] { "ck_stationFence", "ck_stationGate", "ck_trainWood", "ck_flume", "ck_monorail", "ck_trainHanging", "mb_circus", "ck_bench", "ck_trash", "f_lamp", "hk_lantern", "ck_food", "ck_drinks", "c_cone", "k_bench" }; PhSubReset(); }
            PhPush(); FillFromPool_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }

    // phase state of the split plans (Tools: autophase2)
    int hp_PlanArcade_first;
    string[] hp_PlanArcade_wall;
    GenRng hp_PlanArcade_r3;
    int hp_PlanArcade_w0;
    GenRng hp_PlanArcade_r4;
    int hp_PlanSnackbar_first;
    string[] hp_PlanPrizeStore_rows;
    int hp_PlanRides_first;
    int hq_Mid_want, hq_Mid_row, hq_Mid_last;

    // locals of the split plans and helpers, kept from one phase to the next (autophase4)
    int hq_PlanArcade_cell;
    int hq_PlanArcade_dx;
    int hq_PlanArcade_dz;
    int hq_PlanArcade_k;
    GenRng hq_PlanArcade_r;
    GenRng hq_PlanArcade_r2;
    int hq_PlanArcade_sel1;
    int hq_PlanArcade_sel2;
    int hq_PlanArcade_till;
    int hq_PlanMidway_dx;
    int hq_PlanMidway_dz;
    int hq_PlanMidway_first;
    int hq_PlanMidway_k;
    GenRng hq_PlanMidway_rk;
    int hq_PlanMidway_s0;
    int hq_PlanMidway_st;
    string[] hq_PlanMidway_stalls;
    int hq_PlanPrizeStore_first;
    bool hq_PlanPrizeStore_ht1;
    int hq_PlanPrizeStore_k;
    GenRng hq_PlanPrizeStore_r;
    int hq_PlanPrizeStore_sel1;
    int hq_PlanSnackbar_cell;
    GenRng hq_PlanSnackbar_r;
    GenRng hq_PlanSnackbar_r3;
    int hq_PlanSnackbar_sel1;
    int hq_PlanSnackbar_sel2;
}
