// MapGen, revision 12 plans: the chapel and its graveyard (research 18, pick 7). A small stone chapel: the nave with its
// pews in rows either side of the aisle, the altar at the far end, the shrine and candles; the vestry behind. In
// front the churchyard by the lane, round it the graveyard: rows of gravestones and crosses with paths between, a
// crypt, a freshly dug grave with its spade, a bench, lamp posts.
public partial class MapGen
{
    // ============================================================================================ NAVE
    void PlanNave()
    {
        int room = pRoom, dx = 0, dz = 0;
        if (Ph(0))
        {
            dx = hq_PlanNave_dx;
            dz = hq_PlanNave_dz;
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            int first = nEmit;
            hq_PlanNave_dx = dx;
            hq_PlanNave_dz = dz;
            hq_PlanNave_r = r;
            hq_PlanNave_first = first;
        }
        if (Ph(1))
        {
            dx = hq_PlanNave_dx;
            dz = hq_PlanNave_dz;
            GenRng r = hq_PlanNave_r;
            int altar = WallPlace(Dn(r.Chance(550) ? "gk_altarStone" : "gk_altarWood"), WM_FAR, dx, dz, WIN_NEVER, EF_MANDATORY, ref r);
            hq_PlanNave_dx = dx;
            hq_PlanNave_dz = dz;
            hq_PlanNave_r = r;
            hq_PlanNave_altar = altar;
        }
        if (Ph(2))
        {
            int first = hq_PlanNave_first;
            int altar = hq_PlanNave_altar;
            if (altar < 0 || !Keep(first, true)) { altar = -1; MandFail(1); }
            int ax = altar >= 0 ? emitX[altar] : RoomMidX(room), az = altar >= 0 ? emitZ[altar] : RoomMidZ(room);
            // the aisle: a walkway from the door to the altar along the nave's long axis, 1 m wide
            bool alongX = RoomLongAxis(room) == 0;
            int mid = alongX ? (roomZ0[room] + roomZ1[room]) / 2 : (roomX0[room] + roomX1[room]) / 2;
            for (int k = roomCellStart[room]; k < roomCellStart[room] + roomCellCount[room]; k++)
            {
                int i = roomCells[k], a = alongX ? i / IW : i % IW;
                if (a >= mid && a <= mid + 1 && occ[i] == 0 && solid[i] == 0) tags[i] |= GenData.T_PATH;   // up to the altar
            }
            hq_PlanNave_first = first;
            hq_PlanNave_altar = altar;
            hq_PlanNave_ax = ax;
            hq_PlanNave_az = az;
        }
        if (Ph(3))
        {
            RoomWalkable(room);
            // the pews, in rows either side of the aisle, all facing the altar
            GenRng r2 = SR(2);
            int pews = 0;
            hq_PlanNave_r2 = r2;
            hq_PlanNave_pews = pews;
        }
        if (PhLoop(4, 100000))
        {
            int first = hq_PlanNave_first;
            int ax = hq_PlanNave_ax;
            int az = hq_PlanNave_az;
            GenRng r2 = hq_PlanNave_r2;
            int pews = hq_PlanNave_pews;
            int k = planIter;
            if (!(k < 4 + roomOuterCells[room] * 2 && Fit(1))) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                int cell = OpenSpot(1, WM_FAR, ax, az, 0, ref r2);
                if (cell < 0) { PhBreak(); break; }
                int px = cell % IW * GenData.INNER_MM + 250, pz = cell / IW * GenData.INNER_MM + 250;
                int yaw = (int)(System.Math.Atan2(ax - px, az - pz) * 180 / System.Math.PI);
                yaw = ((yaw + 45 + 360) % 360) / 90 * 90;   // square to the nave
                first = nEmit;
                if (FreePlace(Dn("gk_pew"), cell, yaw, 0, 0, H_FREE) >= 0 && Keep(first, false)) pews++;
            }
            hq_PlanNave_first = first;
            hq_PlanNave_ax = ax;
            hq_PlanNave_az = az;
            hq_PlanNave_r2 = r2;
            hq_PlanNave_pews = pews;
        }
        if (Ph(5))
        {
            int pews = hq_PlanNave_pews;
            if (pews == 0) MandFail(1);
            hq_PlanNave_pews = pews;
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(6, PoolLen(new string[] { "hw_shrine", "hw_plaque", "gk_lanternGlass", "gk_lantern", "gk_pillarSmall", "gk_pillarSquare", "gk_urnRound", "gk_urnSquare",
            "k_bookcaseClosed", "k_plant", "gk_benchOld", "k_coatRack" })) && !PoolStep(50, new string[] { "hw_shrine", "hw_plaque", "gk_lanternGlass", "gk_lantern", "gk_pillarSmall", "gk_pillarSquare", "gk_urnRound", "gk_urnSquare",
            "k_bookcaseClosed", "k_plant", "gk_benchOld", "k_coatRack" })) PhBreak();
}

    // ============================================================================================ VESTRY
    void PlanVestry()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            hp_PlanVestry_first = nEmit;
            if (WallPlace(Dn("gen_wardrobe"), WM_ANY, 0, 0, WIN_NEVER, EF_MANDATORY, ref r) >= 0) Keep(hp_PlanVestry_first, true); else MandFail(1);
        }
        if (Ph(1))
        {
            GenRng r2 = SR(2);
            if (Fit(2)) { hp_PlanVestry_first = nEmit; if (WallPlace(Dn("k_desk"), WM_ANY, 0, 0, WIN_PREFER, 0, ref r2) >= 0) Keep(hp_PlanVestry_first, false); }
    
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(2, PoolLen(new string[] { "q_cabinet", "gk_trunk", "k_bookcaseClosed", "k_bookcase", "k_coatRack", "k_lampSquare", "gk_lantern", "hw_shrine", "q_cabinetSmall",
            "k_trashcan", "gk_urnRound" })) && !PoolStep(50, new string[] { "q_cabinet", "gk_trunk", "k_bookcaseClosed", "k_bookcase", "k_coatRack", "k_lampSquare", "gk_lantern", "hw_shrine", "q_cabinetSmall",
            "k_trashcan", "gk_urnRound" })) PhBreak();
    }

    // ============================================================================================ CHURCHYARD
    void PlanChurchyard()
    {
        if (Ph(0))
        {
            int dx = 0, dz = 0;
            hp_PlanChurchyard_room = pRoom;
            FrontMarkers(30);
            MainDoor(hp_PlanChurchyard_room, out dx, out dz);
            GenRng rp = SR(39);
            StonePaths(40, ref rp);
            hp_PlanChurchyard_r = SR(1);
            hq_PlanChurchyard_dx = dx;
            hq_PlanChurchyard_dz = dz;
        }
        if (PhLoop(1, 100000))
        {
            int dx = hq_PlanChurchyard_dx;
            int dz = hq_PlanChurchyard_dz;
            int k = planIter;
            if (!(k < 2 && Fit(1))) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                int cell = OpenSpot(1, WM_NEAR, dx, dz, 0, ref hp_PlanChurchyard_r);
                int first = nEmit;
                if (cell >= 0 && FreePlace(Dn("gk_lightpost"), cell, 0, 0, 0, H_FREE) >= 0) Keep(first, false);
            }
            hq_PlanChurchyard_dx = dx;
            hq_PlanChurchyard_dz = dz;
        }
        if (Ph(2))
        {
            GenRng r2 = SR(2);
            if (r2.Chance(600) && Fit(2)) { int first = nEmit; if (PlaceGroup(GenData.FindGrp("graves"), hp_PlanChurchyard_room, GenData.A_OUT_OPEN, 3, 0, ref r2)) Keep(first, false); }
        }
        if (Ph(3))
        {
            int trees = TreeCount(hp_PlanChurchyard_room, ref hp_PlanChurchyard_r);
            hq_PlanChurchyard_trees = trees;
        }
        if (Ph(4))
        {
            int trees = hq_PlanChurchyard_trees;
            for (int t = 0; t < trees; t++) LawnTree(60 + t);
            hq_PlanChurchyard_trees = trees;
        }
        if (Ph(5))
        {
            FillFromPool(50, new string[] { "gk_benchOld", "k_bench", "hk_bench", "gk_urnRound", "gk_urnSquare", "gk_cross", "f_hedge", "n_bushLarge", "ck_trash", "gk_lightpostDouble" });
            ClearBans();
        }
        if (Ph(6))
        {
            Borders(30, 3, true);
        }
    }

    // ============================================================================================ GRAVEYARD
    void PlanGraveyard()
    {
        int room = pRoom;
        // a step a part (Cor: build freezes): each row of graves, the crypt and the dug grave, each tree, each of the pool
        // the rows of graves, a few rows of a few, then the crosses
        if (PhLoop(0, 2 + AreaM2(room) / 16))
        {
            int k = planIter;
            if (k == 0) planA = 0;
            if (!Fit(3)) PhBreak();
            else
            {
                GenRng rk = SR(2 + k);
                int first = nEmit;
                if (PlaceGroup(GenData.FindGrp(k % 3 == 2 ? "crosses" : "graves"), room, GenData.A_OUT_OPEN, rk.Between(3, 4), 0, ref rk) && Keep(first, false)) planA++;
            }
        }
        if (Ph(1))
        {
            if (planA == 0) MandFail(1);
            GenRng r2 = SR(20);
            if (r2.Chance(550) && Fit(1)) { int first = nEmit; if (WallPlace(Dn(r2.Chance(500) ? "gk_crypt" : "gk_cryptSmall"), WM_ANY, 0, 0, WIN_ANY, 0, ref r2) >= 0) Keep(first, false); }
            GenRng r3 = SR(21);
            hq_PlanGraveyard_sel1 = 0;
            if (r3.Chance(450) && Fit(2)) hq_PlanGraveyard_sel1 = 1;
            hq_PlanGraveyard_r3 = r3;
        }
        if (Ph(2))
        {
            if (hq_PlanGraveyard_sel1 == 1)
            {
                GenRng r3 = hq_PlanGraveyard_r3;
                int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r3);
                int first = nEmit;
                int dug = cell >= 0 ? FreePlace(Dn("gk_dug"), cell, r3.Range(4) * 90, 0, 0, H_FREE) : -1;
                hq_PlanGraveyard_r3 = r3;
                hq_PlanGraveyard_first = first;
                hq_PlanGraveyard_dug = dug;
            }
        }
        if (Ph(3))
        {
            if (hq_PlanGraveyard_sel1 == 1)
            {
                GenRng r3 = hq_PlanGraveyard_r3;
                int first = hq_PlanGraveyard_first;
                int dug = hq_PlanGraveyard_dug;
                if (dug >= 0 && Keep(first, false) && Fit(1)) { first = nEmit; if (RelBeside(Dn("gk_mound"), dug, 1, 100, 0, ref r3) >= 0) Keep(first, false); }
                hq_PlanGraveyard_r3 = r3;
                hq_PlanGraveyard_first = first;
                hq_PlanGraveyard_dug = dug;
            }
        }
        if (Ph(4))
        {
            GenRng r = SR(1);
            planN = TreeCount(room, ref r);
        }
        if (PhLoop(5, planN)) LawnTree(60 + planIter);
        string[] pool = { "gk_gsDebris", "gk_graveBorder", "gk_lightpost", "gk_lightpostDouble", "gk_fireBasket", "gk_benchOld", "gk_urnRound", "gk_urnSquare",
            "gk_lantern", "gk_crossWood", "gk_crossColumn", "n_bushLarge", "hk_bench", "m_wheelbarrow" };
        if (PhLoop(6, pool.Length) && !PoolStep(50, pool)) PhBreak();
    }

    // phase state of the split plans (Tools: autophase2)
    int hp_PlanVestry_first;
    int hp_PlanChurchyard_room;
    GenRng hp_PlanChurchyard_r;

    // locals of the split plans, kept from one phase to the next (autophase3)
    int hq_PlanChurchyard_dx;
    int hq_PlanChurchyard_dz;
    int hq_PlanChurchyard_trees;
    int hq_PlanGraveyard_dug;
    int hq_PlanGraveyard_first;
    GenRng hq_PlanGraveyard_r3;
    int hq_PlanGraveyard_sel1;
    int hq_PlanNave_altar;
    int hq_PlanNave_ax;
    int hq_PlanNave_az;
    int hq_PlanNave_dx;
    int hq_PlanNave_dz;
    int hq_PlanNave_first;
    int hq_PlanNave_pews;
    GenRng hq_PlanNave_r;
    GenRng hq_PlanNave_r2;
}
