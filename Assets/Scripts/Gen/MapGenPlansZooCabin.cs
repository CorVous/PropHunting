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
        int got = 0;
        for (int k = 0; k < n && Fit(1); k++)
        {
            GenRng rk = SR(k0 + k);
            // a clear spot two cells round, else one; turned any way, else square to the fence
            int def = Dn(kinds[k % kinds.Length]), e = -1;
            for (int ring = 2; ring >= 1 && e < 0; ring--)
            {
                int cell = OpenSpot(ring, WM_ANY, 0, 0, 0, ref rk);
                if (cell < 0) continue;
                int first = nEmit;
                e = FreePlace(def, cell, rk.Range(360), 0, 0, H_FREE);
                if (e < 0) e = FreePlace(def, cell, rk.Range(4) * 90, 0, 0, H_FREE);
                if (e >= 0 && !Keep(first, false)) e = -1;
            }
            if (e >= 0) got++;
        }
        return got;
    }

    // ============================================================================================ ZOO PLAZA
    // The entrance from the lane: the ticket booth by the gate, the food and drinks kiosks, the toilets, the gift
    // stall with its soft toys, café tables, the vending and claw machines, benches, bins, flower beds, a lamp.
    void PlanZooPlaza()
    {
        int room = pRoom, dx, dz;
        if (Ph(0))
        {
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            int first = nEmit;
            if (WallPlace(Dn("ck_info"), WM_NEAR, dx, dz, WIN_ANY, EF_MANDATORY, ref r) >= 0) Keep(first, true); else MandFail(1);
            string[] kiosks = { "ck_food", "ck_drinks", "ck_toilets" };
            for (int k = 0; k < kiosks.Length && Fit(1); k++)
            {
                GenRng rk = SR(2 + k);
                if (!rk.Chance(k == 0 ? 900 : 650)) continue;
                first = nEmit;
                if (WallPlace(Dn(kiosks[k]), WM_FAR, dx, dz, WIN_ANY, 0, ref rk) >= 0) Keep(first, false);
            }
            GenRng r5 = SR(5);
            if (r5.Chance(700) && Fit(2))
            {
                first = nEmit;
                int st = WallPlace(Dn(r5.Chance(500) ? "f_stallRed" : "f_stallGreen"), WM_ANY, 0, 0, WIN_ANY, 0, ref r5);
                if (st >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp("plush"), st, 2, 0, ref r5)) Keep(first, false); }
            }
            GenRng r6 = SR(6);
            for (int k = 0; k < 1 + AreaM2(room) / 20 && Fit(2); k++)
            {
                int cell = OpenSpot(3, WM_ANY, 0, 0, 0, ref r6);
                if (cell < 0) break;
                first = nEmit;
                if (FreePlace(Dn("r_tableRound"), cell, 0, 1, 0, H_FREE) >= 0) Keep(first, false);
            }
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(1, PoolLen(new string[] { "ma_vending", "ma_claw", "ms_gacha", "ck_bench", "k_bench", "ck_trash", "ck_flowers", "f_lamp", "f_hedge", "k_plant", "ms_cart", "n_planter", "hk_bench", "hk_lantern", "sk_scooter", "ck_toilets",
            "f_stallBench", "f_stallStool", "ma_prizeWheel", "ma_tickets", "n_statueRing", "f_hedgeCurve", "k_trashcan", "hk_benchShort" })) && !PoolStep(50, new string[] { "ma_vending", "ma_claw", "ms_gacha", "ck_bench", "k_bench", "ck_trash", "ck_flowers", "f_lamp", "f_hedge", "k_plant", "ms_cart", "n_planter", "hk_bench", "hk_lantern", "sk_scooter", "ck_toilets",
            "f_stallBench", "f_stallStool", "ma_prizeWheel", "ma_tickets", "n_statueRing", "f_hedgeCurve", "k_trashcan", "hk_benchShort" })) PhBreak();
}

    // ============================================================================================ KEEPERS' WALK
    // The path between the enclosures: a gravel strip down the middle, benches and bins at the fences, lamps, the
    // keepers' cart or barrow with a bale and buckets, an ice-cream kiosk, flower beds and shrubs.
    void PlanZooWalk()
    {
        int room = pRoom;
        if (Ph(0))
        {
            int x0 = roomX0[room], z0 = roomZ0[room], x1 = roomX1[room], z1 = roomZ1[room];
            bool alongX = (x1 - x0) > (z1 - z0);
            int mid = alongX ? (z0 + z1) / 2 : (x0 + x1) / 2;
            for (int k = roomCellStart[room]; k < roomCellStart[room] + roomCellCount[room]; k++)
            {
                int i = roomCells[k], a = alongX ? i / IW : i % IW;
                if (a >= mid - 1 && a <= mid) tags[i] |= GenData.T_PATH;
            }
            RoomWalkable(room);
            GenRng r = SR(1);
            int first = nEmit;
            if (WallPlace(Dn(r.Chance(500) ? "f_cart" : "m_wheelbarrow"), WM_ANY, 0, 0, WIN_ANY, 0, ref r) >= 0 && Keep(first, false) && Fit(2))
            {
                first = nEmit;
                if (GroupAt(GenData.FindGrp(r.Chance(500) ? "hay" : "buckets"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r)) Keep(first, false);
            }
            GenRng r2 = SR(2);
            if (r2.Chance(600) && Fit(1)) { first = nEmit; if (WallPlace(Dn(r2.Chance(500) ? "ck_food" : "ck_drinks"), WM_ANY, 0, 0, WIN_ANY, 0, ref r2) >= 0) Keep(first, false); }
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(1, PoolLen(new string[] { "ck_bench", "k_bench", "ck_trash", "f_lamp", "ck_flowers", "f_hedge", "n_planter", "ma_vending", "n_bush", "n_bushSmall",
            "ck_info", "ck_toilets", "f_hedgeCurve", "n_bushDetailed", "k_benchCushion", "n_statueBlock", "hk_bench", "hk_lantern", "f_cartHigh", "hk_benchShort" })) && !PoolStep(50, new string[] { "ck_bench", "k_bench", "ck_trash", "f_lamp", "ck_flowers", "f_hedge", "n_planter", "ma_vending", "n_bush", "n_bushSmall",
            "ck_info", "ck_toilets", "f_hedgeCurve", "n_bushDetailed", "k_benchCushion", "n_statueBlock", "hk_bench", "hk_lantern", "f_cartHigh", "hk_benchShort" })) PhBreak();
}

    // ============================================================================================ ENCLOSURES
    // One part of the world each: its beasts on the open ground, the water they need, what they climb on and shelter
    // under, the keepers' trough and hay; trees and rocks round the fence.
    void PlanEnclosure()
    {
        int room = pRoom, t = roomType[room];
        int mx = RoomMidX(room), mz = RoomMidZ(room);
        GenRng r = SR(1);
        int wx = mx + (r.Chance(500) ? 600 : -600), wz = mz + (r.Chance(500) ? 600 : -600);
        if (t == GenData.R_ENC_ARCTIC) WaterPatch(room, wx, wz, 3);   // a pool, with room on its bank for the bears
        else if (t == GenData.R_ENC_SAVANNA || t == GenData.R_ENC_WOODS) WaterPatch(room, wx, wz, 2);
        string[] big; string grp = null; string[] pool;
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
        // what lives on the water first
        if (t == GenData.R_ENC_ARCTIC && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("shore"), GenData.A_OUT_OPEN, 3, WM_NEAR, wx, wz, 0, ref r)) Keep(first, false); }
        CloseWater(room);
        int got = Beasts(big, t == GenData.R_AVIARY ? 1 : 2 + AreaM2(room) / 12, 10);
        if (grp != null && Fit(2))
        {
            GenRng rg = SR(20);
            int first = nEmit;
            if (PlaceGroup(GenData.FindGrp(grp), room, GenData.A_OUT_OPEN, rg.Between(GenData.grpMin[GenData.FindGrp(grp)], 3), 0, ref rg) && Keep(first, false)) got++;
        }
        if (t == GenData.R_AVIARY && Fit(2)) { GenRng rb = SR(21); int first = nEmit; if (PlaceGroup(GenData.FindGrp("bugs"), room, GenData.A_OUT_OPEN, 2, 0, ref rb)) Keep(first, false); }
        if (got == 0) MandFail(1);
        GenRng r3 = SR(3);
        int trees = TreeCount(room, ref r3);
        for (int k = 0; k < trees && t != GenData.R_ENC_ARCTIC; k++) LawnTree(60 + k);
        GenRng r4 = SR(4);
        if (t != GenData.R_ENC_ARCTIC && t != GenData.R_AVIARY && r4.Chance(700) && Fit(2))
        {
            int first = nEmit;
            int tr = WallPlace(Dn("gen_trough"), WM_ANY, 0, 0, WIN_ANY, 0, ref r4);
            if (tr >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp(t == GenData.R_ENC_FARM || t == GenData.R_ENC_SAVANNA ? "hay" : "buckets"), tr, 2, 0, ref r4)) Keep(first, false); }
        }
        if (t == GenData.R_ENC_WOODS && Fit(2))
        {
            GenRng r5 = SR(5);
            int cell = OpenSpot(1, WM_ANY, 0, 0, 0, ref r5);
            int first = nEmit;
            int st = cell >= 0 ? FreePlace(Dn("n_stump"), cell, r5.Range(360), 0, 0, H_NATURE) : -1;
            if (st >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp("mushrooms"), st, 2, 0, ref r5)) Keep(first, false); }
        }
        if (t == GenData.R_AVIARY) { ClearBans(); Borders(30, 3, true); }
        else if (t == GenData.R_ENC_FARM && Fit(2)) { GenRng r6 = SR(6); int first = nEmit; if (PlaceGroup(GenData.FindGrp("hens"), room, GenData.A_OUT_OPEN, 2, 0, ref r6)) Keep(first, false); }
        FillFromPool(50, pool);
        // and the habitat every enclosure shares: grass tussocks, small shrubs, a flat rock, a log, a stone, a stump
        if (t != GenData.R_ENC_FARM) FillFromPool(51, new string[] { "n_grassLarge", "n_bushSmall", "s_rockFlat", "n_logLarge", "s_stoneL", "n_stump", "n_bush", "s_logSmall", "n_rockTall",
            "s_block", "n_rockLarge", "n_bushTriangle", "n_stumpRound", "n_rockLargeB", "m_bucketWater", "g_hayBundled", "n_logStack", "n_log" });
        else FillFromPool(51, new string[] { "p_cat", "m_sack", "n_bush", "n_stump", "k_bench" });
    }

    // ============================================================================================ KEEPERS' YARD
    void PlanKeeper()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            int first = nEmit;
            if (PlaceGroup(GenData.FindGrp("hay"), room, GenData.A_WALL, 3, EF_MANDATORY, ref r)) Keep(first, true); else MandFail(1);
            string[] rows = { "buckets", "sacks", "crates", "tools", "barrels" };
            for (int k = 0; k < rows.Length; k++)
            {
                GenRng rk = SR(2 + k);
                if (!rk.Chance(k < 2 ? 850 : 550) || !Fit(2)) continue;
                first = nEmit;
                if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, 2, 0, ref rk)) Keep(first, false);
            }
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(1, PoolLen(new string[] { "gen_shed", "m_wheelbarrow", "f_cart", "f_cartHigh", "gen_trough", "w_pallet", "w_palletLoaded", "gen_tap", "gen_coop", "ms_handTruck", "s_boxLarge" })) && !PoolStep(50, new string[] { "gen_shed", "m_wheelbarrow", "f_cart", "f_cartHigh", "gen_trough", "w_pallet", "w_palletLoaded", "gen_tap", "gen_coop", "ms_handTruck", "s_boxLarge" })) PhBreak();
}

    // ============================================================================================ CABIN ROOM
    // One room for living: the wood stove against a wall with the log basket beside it and an armchair on a rug in
    // front; the kitchen corner (counter, cooker, sink, a small fridge); the table with its chairs; closed bookcases,
    // the coat rack by the door, a sled and a paddle on the wall, a chest, a lamp.
    void PlanCabinMain()
    {
        int room = pRoom, dx, dz;
        if (Ph(0))
        {
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            int first = nEmit;
            int stove = WallPlace(Dn("cb_woodstove"), WM_FAR, dx, dz, WIN_NEVER, EF_MANDATORY, ref r);
            if (stove < 0 || !Keep(first, true)) { stove = -1; MandFail(1); }
            if (stove >= 0 && Fit(1)) { first = nEmit; if (RelBeside(Dn("n_logStack"), stove, r.Chance(500) ? 1 : -1, 120, 0, ref r) >= 0) Keep(first, false); }
            GenRng r2 = SR(2);
            if (Fit(3))
            {
                first = nEmit;
                rowN = 3; rowDef[0] = Dn("k_counter"); rowDef[1] = Dn("k_stoveElectric"); rowDef[2] = Dn("k_sink");
                if (WallRow(0, 2, WM_ANY, 0, 0, WIN_ANY, 0, ref r2) >= 0) Keep(first, false);
            }
            GenRng r3 = SR(3);
            if (r3.Chance(650) && Fit(1)) { first = nEmit; if (WallPlace(Dn("k_fridgeSmall"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0) Keep(first, false); }
            GenRng r4 = SR(4);
            if (Fit(1))
            {
                int cell = OpenSpot(3, WM_ANY, 0, 0, 0, ref r4);
                first = nEmit;
                if (cell >= 0 && FreePlace(Dn(r4.Chance(600) ? "k_table" : "q_table"), cell, r4.Range(2) * 90, 1, 0, H_FREE) >= 0) Keep(first, false);
            }
            GenRng r5 = SR(5);
            if (Fit(1))
            {
                int sx = stove >= 0 ? emitX[stove] : RoomMidX(room), sz = stove >= 0 ? emitZ[stove] : RoomMidZ(room);
                first = nEmit;
                if (WallPlace(Dn(r5.Chance(500) ? "k_recliner" : "k_loungeChair"), WM_NEAR, sx, sz, WIN_AVOID, 0, ref r5) >= 0) Keep(first, false);
                int cell = OpenSpot(2, WM_NEAR, sx, sz, 0, ref r5);
                if (cell >= 0) FreePlace(Dn(r5.Chance(500) ? "k_rug" : "q_rug"), cell, r5.Range(2) * 90, 0, 0, H_FREE);
            }
            GenRng r6 = SR(6);
            if (r6.Chance(700) && Fit(1)) { first = nEmit; if (WallPlace(Dn("k_coatRack"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r6) >= 0) Keep(first, false); }
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(1, PoolLen(new string[] { "k_bookcaseClosed", "k_bookcaseWide", "hk_sled", "n_paddle", "s_chest", "k_lampSquare", "k_bench", "hk_benchShort", "s_barrel", "k_plant",
            "q_cabinet", "k_trashcan" })) && !PoolStep(50, new string[] { "k_bookcaseClosed", "k_bookcaseWide", "hk_sled", "n_paddle", "s_chest", "k_lampSquare", "k_bench", "hk_benchShort", "s_barrel", "k_plant",
            "q_cabinet", "k_trashcan" })) PhBreak();
}

    // ============================================================================================ BUNK ROOM
    void PlanBunkRoom()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            int first = nEmit;
            int bunk = WallPlace(Dn("k_bedBunk"), WM_CORNER, 0, 0, WIN_AVOID, EF_MANDATORY, ref r);
            if (bunk < 0 || !Keep(first, true)) { bunk = -1; MandFail(1); }
            if (bunk >= 0 && Fit(1)) { first = nEmit; if (RelBeside(Dn("k_bedsideDrawer"), bunk, 0, 50, 0, ref r) >= 0) Keep(first, false); }
            GenRng r2 = SR(2);
            if (Fit(1)) { first = nEmit; if (WallPlace(Dn(r2.Chance(600) ? "k_bedBunk" : "n_cot"), WM_FAR, bunk >= 0 ? emitX[bunk] : 0, bunk >= 0 ? emitZ[bunk] : 0, WIN_AVOID, 0, ref r2) >= 0) Keep(first, false); }
            FillFromPool(50, new string[] { "s_chest", "dg_trunkL", "dg_trunkM", "s_bedrollPacked", "k_coatRack", "gen_wardrobe", "k_lampSquare", "hk_sledLong", "n_paddle",
                "k_bookcaseClosed", "hk_benchShort" });
        }
        if (Ph(1))
        {
            GenRng r3 = SR(3);
            if (r3.Chance(500))
            {
                int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r3);
                if (cell >= 0) FreePlace(Dn("k_rug"), cell, 0, 0, 0, H_FREE);
            }
        }
    }

    // ============================================================================================ PORCH
    // The decking in front of the cabin: a bench against the cabin wall and the rocking chair, lantern posts at the
    // steps, the log pile, a sled and the canoe with its paddle, a barrel, buckets, pots.
    void PlanPorch()
    {
        int room = pRoom, dx, dz;
        if (Ph(0))
        {
            FrontMarkers(30);
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            int first = nEmit;
            if (GroupAt(GenData.FindGrp("woodpile"), GenData.A_BUILDING, 2, WM_ANY, 0, 0, 0, ref r)) Keep(first, false);
            GenRng r2 = SR(2);
            for (int k = 0; k < 2 && Fit(1); k++)
            {
                int cell = OpenSpot(1, WM_NEAR, dx, dz, 0, ref r2);
                first = nEmit;
                if (cell >= 0 && FreePlace(Dn("hk_lantern"), cell, 0, 0, 0, H_FREE) >= 0) Keep(first, false);
            }
            GenRng r3 = SR(3);
            if (r3.Chance(600) && Fit(1)) { first = nEmit; if (WallPlace(Dn("n_canoe"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0 && Keep(first, false) && Fit(1)) { first = nEmit; if (WallPlace(Dn("n_paddle"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0) Keep(first, false); } }
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(1, PoolLen(new string[] { "hk_bench", "hk_benchShort", "k_recliner", "hk_sled", "hk_sledLong", "s_barrel", "n_logStack", "s_bedrollPacked", "gen_tap", "n_planter" })) && !PoolStep(50, new string[] { "hk_bench", "hk_benchShort", "k_recliner", "hk_sled", "hk_sledLong", "s_barrel", "n_logStack", "s_bedrollPacked", "gen_tap", "n_planter" })) PhBreak();
}

    // ============================================================================================ WOOD YARD
    void PlanWoodyard()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r);
            int first = nEmit;
            int block = cell >= 0 ? FreePlace(Dn("s_block"), cell, r.Range(360), 0, 0, H_FREE) : -1;
            if (block < 0 || !Keep(first, true)) { block = -1; MandFail(1); }
            if (block >= 0 && Fit(1)) { first = nEmit; if (RelBeside(Dn(r.Chance(500) ? "s_axe" : "s_axeB"), block, r.Chance(500) ? 1 : -1, 60, 0, ref r) >= 0) Keep(first, false); }
            GenRng r2 = SR(2);
            if (Fit(2)) { first = nEmit; if (PlaceGroup(GenData.FindGrp("woodpile"), room, GenData.A_WALL, 3, 0, ref r2)) Keep(first, false); }
            GenRng r3 = SR(3);
            if (r3.Chance(650) && Fit(2)) { first = nEmit; if (PlaceGroup(GenData.FindGrp("tools"), room, GenData.A_WALL, 2, 0, ref r3)) Keep(first, false); }
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(1, PoolLen(new string[] { "n_logStackLarge", "gen_shed", "s_anvil", "s_grind", "s_workbench", "m_wheelbarrow", "s_planks", "w_logs", "s_barrel", "s_shovelB", "s_logSmall" })) && !PoolStep(50, new string[] { "n_logStackLarge", "gen_shed", "s_anvil", "s_grind", "s_workbench", "m_wheelbarrow", "s_planks", "w_logs", "s_barrel", "s_shovelB", "s_logSmall" })) PhBreak();
}

    // ============================================================================================ CAMPGROUND
    // The fire ring with its seats and wood pile, the spit and the fish stand by the fire, tents round it with
    // bedrolls in front, a camp cot, the chest, the canoe at the fence, a lantern post, buckets.
    void PlanCampsite()
    {
        int room = pRoom;
        string[] tents = { "s_tent", "s_tentCanvas", "s_tentHalf", "n_tentBig", "n_tentSmallOpen" };
        // a part a step (Cor: build freezes): the fire and what is round it, each tent, each of the pool
        if (Ph(0))
        {
            GenRng r = SR(1);
            FindGates(room);   // AroundFire keeps the tents off the lines from the gates to the fire
            planA = FireRing(6, RoomMidX(room), RoomMidZ(room), false, ref r);
        }
        if (Ph(1)) FireSeats(planA, false, 7);
        if (Ph(2))
        {
            int fire = planA;
            if (fire >= 0)
            {
                GenRng r2 = SR(2);
                if (r2.Chance(850)) WoodPile(fire, true, ref r2);
                GenRng r3 = SR(3);
                if (Fit(1)) { int first = nEmit; if (RelBeside(Dn(r3.Chance(500) ? "s_spit" : "s_fishStand"), fire, r3.Chance(500) ? 1 : -1, 200, 0, ref r3) >= 0) Keep(first, false); }
                planR = SR(4);
                planB = planR.Range(tents.Length);
            }
        }
        if (PhLoop(3, planA >= 0 ? 1 + AreaM2(room) / 24 : 0))
        {
            if (!Fit(1)) PhBreak();
            else
            {
                int k = planIter;
                int tent = AroundFire(Dn(tents[(planB + k) % tents.Length]), planA, 180, 1800, 3400, 0, false, true, ref planR);
                if (tent >= 0 && Fit(1)) { int first = nEmit; if (RelBeside(Dn(planR.Chance(500) ? "s_bedroll" : "s_bedrollFrame"), tent, planR.Chance(500) ? 1 : -1, 80, 0, ref planR) >= 0) Keep(first, false); }
            }
        }
        string[] pool = { "n_cot", "s_chest", "n_canoe", "hk_lantern", "s_barrel", "s_logSmall", "s_rockFlat", "n_paddle", "s_treeLog" };
        if (PhLoop(4, pool.Length) && !PoolStep(50, pool)) PhBreak();
    }
}
