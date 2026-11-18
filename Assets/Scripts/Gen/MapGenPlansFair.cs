// MapGen, revision 12 plans: the funfair (research 18, pick 4). The arcade hall on the lane (machines along the walls
// and back to back in the middle, the prize counter by the door), its snack bar and prize store; in front the midway
// of stalls, behind and beside it the rides yard, where the coaster cars stand in the station between queue rails.
public partial class MapGen
{
    // ============================================================================================ ARCADE HALL
    void PlanArcade()
    {
        int room = pRoom, cells = roomOuterCells[room];
        if (Ph(0))
        {
            int dx, dz;
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            // the prize counter by the door: the till, the prize shelves behind it, the ticket machine
            hp_PlanArcade_first = nEmit;
            int till = WallPlace(Dn("ma_register"), WM_NEAR, dx, dz, WIN_ANY, EF_MANDATORY, ref r);
            if (till < 0 || !Keep(hp_PlanArcade_first, true)) { till = -1; MandFail(1); }
            if (till >= 0 && Fit(1)) { hp_PlanArcade_first = nEmit; if (RelBeside(Dn("ma_prizes"), till, r.Chance(500) ? 1 : -1, 100, 0, ref r) >= 0) Keep(hp_PlanArcade_first, false); }
            GenRng r2 = SR(2);
            if (Fit(1)) { hp_PlanArcade_first = nEmit; if (WallPlace(Dn("ma_tickets"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r2) >= 0) Keep(hp_PlanArcade_first, false); }
            // machines along the walls, a run at a time, of different kinds
            hp_PlanArcade_wall = new string[] { "ma_pinball", "ma_slots", "ma_arcade", "ma_dance", "ma_basketball", "ma_claw", "ma_prizeWheel", "ma_vending", "ms_gacha" };
        }
        if (Ph(1))
        {
            hp_PlanArcade_r3 = SR(3);
            hp_PlanArcade_w0 = hp_PlanArcade_r3.Range(hp_PlanArcade_wall.Length);
        }
        if (PhLoop(2, 100000))
        {
            int k = planIter;
            if (!(k < 2 + cells && Fit(2))) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: "continue" leaves it (UdonSharp sends a do-while's continue back to the top)
            {
                hp_PlanArcade_first = nEmit;
                rowN = 2; rowDef[0] = Dn(hp_PlanArcade_wall[(hp_PlanArcade_w0 + 2 * k) % hp_PlanArcade_wall.Length]); rowDef[1] = Dn(hp_PlanArcade_wall[(hp_PlanArcade_w0 + 2 * k + 1) % hp_PlanArcade_wall.Length]);
                if (WallRow(0, 2, WM_ANY, 0, 0, WIN_AVOID, 0, ref hp_PlanArcade_r3) >= 0) Keep(hp_PlanArcade_first, false);
            }
        }
        if (Ph(3))
        {
            // the middle: air hockey tables, and machines standing free with room round them
            hp_PlanArcade_r4 = SR(4);
        }
        if (PhLoop(4, 100000))
        {
            int k = planIter;
            if (!(k < 1 + cells / 2 && Fit(1))) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: "continue" leaves it (UdonSharp sends a do-while's continue back to the top)
            {
                int cell = OpenSpot(3, WM_ANY, 0, 0, 0, ref hp_PlanArcade_r4);
                if (cell < 0) { PhBreak(); break; }
                hp_PlanArcade_first = nEmit;
                if (FreePlace(Dn("ma_airHockey"), cell, hp_PlanArcade_r4.Range(2) * 90, 1, 0, H_FREE) >= 0) Keep(hp_PlanArcade_first, false);
            }
        }
        if (Ph(5))
        {
            GenRng r5 = SR(5);
            if (Fit(2)) { hp_PlanArcade_first = nEmit; if (PlaceGroup(GenData.FindGrp("plush"), room, GenData.A_WALL, 2, 0, ref r5)) Keep(hp_PlanArcade_first, false); }
    
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(6, PoolLen(new string[] { "ma_pinball", "ma_slots", "ma_arcade", "ma_dance", "ma_basketball", "ma_claw", "ma_prizeWheel", "ma_vending", "ms_gacha", "hb_stool",
            "k_trashcan", "ck_bench", "k_plant" })) && !PoolStep(50, new string[] { "ma_pinball", "ma_slots", "ma_arcade", "ma_dance", "ma_basketball", "ma_claw", "ma_prizeWheel", "ma_vending", "ms_gacha", "hb_stool",
            "k_trashcan", "ck_bench", "k_plant" })) PhBreak();
    }

    // ============================================================================================ SNACK BAR
    void PlanSnackbar()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            hp_PlanSnackbar_first = nEmit;
            // the counter and the cooker along one wall (three pieces of 1.4 m are longer than a 4 m wall), the sink at another
            rowN = 2; rowDef[0] = Dn("r_counter"); rowDef[1] = Dn("r_stove");
            if (WallRow(0, 1, WM_ANY, 0, 0, WIN_ANY, EF_MANDATORY, ref r) >= 0) Keep(hp_PlanSnackbar_first, true); else MandFail(1);
            if (Fit(1)) { hp_PlanSnackbar_first = nEmit; if (WallPlace(Dn("r_counterSink"), WM_ANY, 0, 0, WIN_ANY, 0, ref r) >= 0) Keep(hp_PlanSnackbar_first, false); }
        }
        if (Ph(1))
        {
            GenRng r2 = SR(2);
            if (Fit(1)) { hp_PlanSnackbar_first = nEmit; if (WallPlace(Dn(r2.Chance(500) ? "r_fridge" : "ms_freezers"), WM_ANY, 0, 0, WIN_ANY, 0, ref r2) >= 0) Keep(hp_PlanSnackbar_first, false); }
        }
        if (Ph(2))
        {
            GenRng r3 = SR(3);
            if (Fit(1))
            {
                int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r3);
                hp_PlanSnackbar_first = nEmit;
                if (cell >= 0 && FreePlace(Dn("r_tableRound"), cell, 0, 1, 0, H_FREE) >= 0) Keep(hp_PlanSnackbar_first, false);
            }
        }
        if (Ph(3))
        {
            GenRng r4 = SR(4);
            if (r4.Chance(600) && Fit(2)) { hp_PlanSnackbar_first = nEmit; if (PlaceGroup(GenData.FindGrp("crates"), room, GenData.A_WALL, 2, 0, ref r4)) Keep(hp_PlanSnackbar_first, false); }
    
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(4, PoolLen(new string[] { "ms_freezer", "ma_vending", "ms_dispCans", "k_trashcan", "r_stool", "ms_freezers", "r_fridge" })) && !PoolStep(50, new string[] { "ms_freezer", "ma_vending", "ms_dispCans", "k_trashcan", "r_stool", "ms_freezers", "r_fridge" })) PhBreak();
    }

    // ============================================================================================ PRIZE STORE
    void PlanPrizeStore()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            if (!RackRun(2, Dn("gen_rack"), Dn("gen_rack"), WM_CENTRE, 0, 0, ref r)) MandFail(1);
            hp_PlanPrizeStore_rows = new string[] { "plush", "boxes", "balls", "toyCars", "boardToys", "cardboard" };
        }
        if (PhLoop(1, 100000))
        {
            int k = planIter;
            if (!(k < hp_PlanPrizeStore_rows.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: "continue" leaves it (UdonSharp sends a do-while's continue back to the top)
            {
                GenRng rk = SR(2 + k);
                if (!rk.Chance(k < 2 ? 850 : 550) || !Fit(2)) continue;
                int first = nEmit;
                if (PlaceGroup(GenData.FindGrp(hp_PlanPrizeStore_rows[k]), room, GenData.A_WALL, 2, 0, ref rk)) Keep(first, false);
            }
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(2, PoolLen(new string[] { "ma_prizes", "s_boxLarge", "ms_handTruck", "dg_boxL", "gen_rack" })) && !PoolStep(50, new string[] { "ma_prizes", "s_boxLarge", "ms_handTruck", "dg_boxL", "gen_rack" })) PhBreak();
    }

    // ============================================================================================ MIDWAY
    // The paved front: game and food stalls in a row along the fence, the ticket booth and the drinks kiosk by the
    // lane, a claw machine and the prize wheel set out, benches, bins, lantern posts, flower beds, the circus tent.
    void PlanMidway()
    {
        int room = pRoom, dx, dz;
        if (Ph(0))
        {
            FrontMarkers(30);
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            string[] stalls = { "f_stallRed", "f_stallGreen", "ck_food", "ck_drinks", "ck_info" };
            int s0 = r.Range(stalls.Length);
            for (int k = 0; k < 2 + AreaM2(room) / 24 && Fit(1); k++)
            {
                GenRng rk = SR(2 + k);
                int first = nEmit;
                int st = WallPlace(Dn(stalls[(s0 + k) % stalls.Length]), WM_ANY, 0, 0, WIN_ANY, 0, ref rk);
                if (st < 0 || !Keep(first, false)) continue;
                // a game stall's prizes beside it
                if (GenData.defId[emitDef[st]].StartsWith("f_stall") && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp(rk.Chance(500) ? "plush" : "balls"), st, 2, 0, ref rk)) Keep(first, false); }
            }
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(1, PoolLen(new string[] { "ma_claw", "ma_prizeWheel", "ma_basketball", "ma_vending", "ck_toilets", "ck_bench", "ck_trash", "ck_flowers", "f_lamp", "hk_lantern",
            "mb_circus", "r_tableRound", "n_planter" })) && !PoolStep(50, new string[] { "ma_claw", "ma_prizeWheel", "ma_basketball", "ma_vending", "ck_toilets", "ck_bench", "ck_trash", "ck_flowers", "f_lamp", "hk_lantern",
            "mb_circus", "r_tableRound", "n_planter" })) PhBreak();
}

    // ============================================================================================ RIDES YARD
    // Where the coaster stands: its cars in a row at the station, the station fence and gate, the ride's entrance and
    // exit at the edge, queue rails in rows, the log-flume boat and the monorail car, a circus tent, benches, bins.
    void PlanRides()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            hp_PlanRides_first = nEmit;
            if (PlaceGroup(GenData.FindGrp("coasterCars"), room, GenData.A_OUT_OPEN, Fit(3) ? 3 : 2, EF_MANDATORY, ref r)) Keep(hp_PlanRides_first, true); else MandFail(1);
        }
        if (Ph(1))
        {
            GenRng r2 = SR(2);
            if (Fit(2)) { hp_PlanRides_first = nEmit; if (PlaceGroup(GenData.FindGrp("queue"), room, GenData.A_OUT_OPEN, r2.Between(2, 3), 0, ref r2)) Keep(hp_PlanRides_first, false); }
        }
        if (Ph(2))
        {
            // the go-kart track's karts parked in a row, now and then
            GenRng r4 = SR(4);
            if (r4.Chance(500) && Fit(2)) { hp_PlanRides_first = nEmit; if (PlaceGroup(GenData.FindGrp("karts"), room, GenData.A_OUT_OPEN, r4.Between(2, 3), 0, ref r4)) Keep(hp_PlanRides_first, false); }
        }
        if (Ph(3))
        {
            GenRng r3 = SR(3);
            if (Fit(1)) { hp_PlanRides_first = nEmit; if (WallPlace(Dn("ck_rideIn"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0 && Keep(hp_PlanRides_first, false) && Fit(1)) { hp_PlanRides_first = nEmit; if (WallPlace(Dn("ck_rideOut"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0) Keep(hp_PlanRides_first, false); } }
    
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(4, PoolLen(new string[] { "ck_stationFence", "ck_stationGate", "ck_trainWood", "ck_flume", "ck_monorail", "ck_trainHanging", "mb_circus", "ck_bench", "ck_trash", "f_lamp",
            "hk_lantern", "ck_food", "ck_drinks", "c_cone", "k_bench" })) && !PoolStep(50, new string[] { "ck_stationFence", "ck_stationGate", "ck_trainWood", "ck_flume", "ck_monorail", "ck_trainHanging", "mb_circus", "ck_bench", "ck_trash", "f_lamp",
            "hk_lantern", "ck_food", "ck_drinks", "c_cone", "k_bench" })) PhBreak();
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
}
