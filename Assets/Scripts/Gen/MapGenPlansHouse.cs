// MapGen, revision 11 plans: the house's new rooms (research 18, pick 1). A teen's room, a playroom, the box room
// (what an attic holds, on the ground floor: the generator has one storey) and the pantry off the kitchen. Same tools
// and rules as the other house plans; in a KayKit-furnished house Dn gives each piece's twin.
public partial class MapGen
{
    // a bed against a wall, in a corner where it can be, its bedside cabinet beside it: the bed, or -1
    int CornerBed(int bedDef, ref GenRng r)
    {
        hq_CornerBed_bedDef = bedDef;
        hq_CornerBed_r = r;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); CornerBed_Body(); fin = PhAdvance(); PhPop(); }
        r = hq_CornerBed_r;
        return hq_CornerBed_ret;
    }

    // CornerBed in phases, a step a call (autophase4); CornerBed() above runs them to the end
    void CornerBed_Body()
    {
        if (Ph(0))
        {
            int bedDef = hq_CornerBed_bedDef;
            GenRng r = hq_CornerBed_r;
            int first = nEmit;
            int bed = WallPlace(bedDef, WM_CORNER, 0, 0, WIN_NEVER, EF_MANDATORY, ref r);
            if (bed < 0) bed = WallPlace(bedDef, WM_CORNER, 0, 0, WIN_AVOID, EF_MANDATORY, ref r);
            hq_CornerBed_bedDef = bedDef;
            hq_CornerBed_r = r;
            hq_CornerBed_first = first;
            hq_CornerBed_bed = bed;
        }
        if (Ph(1))
        {
            int first = hq_CornerBed_first;
            int bed = hq_CornerBed_bed;
            if (bed >= 0 && !Keep(first, true)) bed = -1;
            if (bed < 0) { MandFail(1); { hq_CornerBed_ret = -1; hq_CornerBed_first = first; hq_CornerBed_bed = bed; PhEnd(); return; } }
            hq_CornerBed_first = first;
            hq_CornerBed_bed = bed;
        }
        if (Ph(2))
        {
            GenRng r = hq_CornerBed_r;
            int first = hq_CornerBed_first;
            int bed = hq_CornerBed_bed;
            if (Fit(1))
            {
                first = nEmit;
                if (RelBeside(Dn("k_bedside"), bed, 0, 50, 0, ref r) >= 0) Keep(first, false);
            }
            { hq_CornerBed_ret = bed; hq_CornerBed_r = r; hq_CornerBed_first = first; hq_CornerBed_bed = bed; PhEnd(); return; }
            hq_CornerBed_r = r;
            hq_CornerBed_first = first;
            hq_CornerBed_bed = bed;
        }
    }

    // ============================================================================================ TEEN ROOM
    // Areas: the bed in a corner with a plush on it; the gaming desk under the window (screen, gaming chair,
    // headphones from the slots); the music corner (a guitar and its case leaning at the wall); the floor of things
    // left lying: comics, skates, a skateboard and its helmet, balls; an armchair or a lounge chair with the TV.
    void PlanTeen()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            hq_PlanTeen_r = r;
        }
        if (PhLoop(1, 100000))
        {
            GenRng r = hq_PlanTeen_r;
            if (planIter == 0) { hq_CornerBed_bedDef = Pick(ref r, "k_bedSingle", 6, "q_bed", 4); hq_CornerBed_r = r; PhSubReset(); }
            PhPush(); CornerBed_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_CornerBed_r; hp_PlanTeen_bed = hq_CornerBed_ret; PhBreak(); }
            hq_PlanTeen_r = r;
        }
        if (Ph(2))
        {
            hp_PlanTeen_bx = hp_PlanTeen_bed >= 0 ? emitX[hp_PlanTeen_bed] : RoomMidX(room);
            hp_PlanTeen_bz = hp_PlanTeen_bed >= 0 ? emitZ[hp_PlanTeen_bed] : RoomMidZ(room);
        }
        if (Ph(3))
        {
            GenRng r2 = SR(2);
            if (Fit(2))
            {
                int first = nEmit;
                if (WallPlace(Dn("k_desk"), WM_FAR, hp_PlanTeen_bx, hp_PlanTeen_bz, WIN_PREFER, 0, ref r2) >= 0) Keep(first, false);
            }
        }
        if (Ph(4))
        {
            GenRng r3 = SR(3);
            hq_PlanTeen_sel1 = 0;
            if (r3.Chance(800) && Fit(1)) hq_PlanTeen_sel1 = 1;
            hq_PlanTeen_r3 = r3;
        }
        if (Ph(5))
        {
            if (hq_PlanTeen_sel1 == 1)
            {
                GenRng r3 = hq_PlanTeen_r3;
                int first = nEmit;
                int g = WallPlace(Dn(r3.Chance(500) ? "mb_guitarA" : "mb_guitarB"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3);
                hq_PlanTeen_r3 = r3;
                hq_PlanTeen_first = first;
                hq_PlanTeen_g = g;
            }
        }
        if (Ph(6))
        {
            if (hq_PlanTeen_sel1 == 1)
            {
                GenRng r3 = hq_PlanTeen_r3;
                int first = hq_PlanTeen_first;
                int g = hq_PlanTeen_g;
                if (g >= 0 && Keep(first, false) && r3.Chance(550) && Fit(1)) { first = nEmit; if (RelBeside(Dn("mb_guitarCase"), g, 0, 80, 0, ref r3) >= 0) Keep(first, false); }
                hq_PlanTeen_r3 = r3;
                hq_PlanTeen_first = first;
                hq_PlanTeen_g = g;
            }
        }
        if (Ph(7))
        {
            GenRng r4 = SR(4);
            if (r4.Chance(750) && Fit(2)) { int first = nEmit; if (PlaceGroup(GenData.FindGrp("comics"), room, GenData.A_WALL, r4.Between(2, 3), 0, ref r4)) Keep(first, false); }
        }
        if (Ph(8))
        {
            GenRng r5 = SR(5);
            if (r5.Chance(650) && LooseFit(2))
            {
                int first = nEmit;
                FSet(Dn("sk_board"), Dn(r5.Chance(500) ? "mb_helmet" : "mb_skates"));
                if (FormAt(GenData.GL_ROW, 120, GenData.A_WALL, WM_ANY, 0, 0, 0, ref r5) >= 0 && Keep(first, false)) MarkLoose(first);
            }
        }
        if (Ph(9))
        {
            GenRng r6 = SR(6);
            hq_PlanTeen_sel2 = 0;
            if (r6.Chance(600) && Fit(1)) hq_PlanTeen_sel2 = 1;
            hq_PlanTeen_r6 = r6;
        }
        if (Ph(10))
        {
            if (hq_PlanTeen_sel2 == 1)
            {
                GenRng r6 = hq_PlanTeen_r6;
                int first = nEmit;
                int seat = WallPlace(Dn(r6.Chance(550) ? "hb_armchairBlue" : "k_loungeChair"), WM_ANY, 0, 0, WIN_AVOID, 0, ref r6);
                hq_PlanTeen_r6 = r6;
                hq_PlanTeen_first = first;
                hq_PlanTeen_seat = seat;
            }
        }
        if (Ph(11))
        {
            if (hq_PlanTeen_sel2 == 1)
            {
                GenRng r6 = hq_PlanTeen_r6;
                int first = hq_PlanTeen_first;
                int seat = hq_PlanTeen_seat;
                if (seat >= 0 && Keep(first, false) && r6.Chance(450) && Fit(1)) { first = nEmit; if (RelBeside(Dn("k_speaker"), seat, 0, 120, 0, ref r6) >= 0) Keep(first, false); }
                hq_PlanTeen_r6 = r6;
                hq_PlanTeen_first = first;
                hq_PlanTeen_seat = seat;
            }
        }
        if (Ph(12))
        {
            GenRng r7 = SR(7);
            if (r7.Chance(650) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("gen_wardrobe"), WM_ANY, 0, 0, WIN_NEVER, 0, ref r7) >= 0) Keep(first, false); }
        }
        if (Ph(13))
        {
            GenRng r8 = SR(8);
            if (r8.Chance(500) && Fit(2)) { int first = nEmit; if (PlaceGroup(GenData.FindGrp("balls"), room, GenData.A_WALL, 2, 0, ref r8)) Keep(first, false); }
        }
        if (Ph(14))
        {
            GenRng r9 = SR(9);
            if (r9.Chance(500) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("k_bookcaseLow"), WM_ANY, 0, 0, WIN_AVOID, 0, ref r9) >= 0) Keep(first, false); }
        }
        if (Ph(15))
        {
            GenRng r10 = SR(10);
            hq_PlanTeen_sel3 = 0;
            if (r10.Chance(550) && Fit(1)) hq_PlanTeen_sel3 = 1;
            hq_PlanTeen_r10 = r10;
        }
        if (Ph(16))
        {
            if (hq_PlanTeen_sel3 == 1)
            {
                GenRng r10 = hq_PlanTeen_r10;
                int cell = OpenSpot(2, WM_NEAR, hp_PlanTeen_bx, hp_PlanTeen_bz, 0, ref r10);
                hq_PlanTeen_r10 = r10;
                hq_PlanTeen_cell = cell;
            }
        }
        if (Ph(17))
        {
            if (hq_PlanTeen_sel3 == 1)
            {
                GenRng r10 = hq_PlanTeen_r10;
                int cell = hq_PlanTeen_cell;
                if (cell >= 0) FreePlace(Dn(r10.Chance(500) ? "k_rug" : "q_rug"), cell, r10.Range(2) * 90, 0, 0, H_FREE);
                hq_PlanTeen_r10 = r10;
                hq_PlanTeen_cell = cell;
            }
        }
        if (Ph(18))
        {
            if (hp_PlanTeen_bed >= 0)
            {
                GenRng r11 = SR(11);
                if (r11.Chance(450) && Fit(1)) { int first = nEmit; if (GroupBeside(GenData.FindGrp("plush"), hp_PlanTeen_bed, 1, 0, ref r11)) Keep(first, false); }
            }
        }
        if (PhLoop(19, 100000))
        {
            if (planIter == 0) { hq_Extras_k0 = 20; hq_Extras_chance = 500; hq_Extras_items = new string[] { "ck_lampFloor", "wk_trashcan", "ck_plant" }; PhSubReset(); }
            PhPush(); Extras_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }

    // ============================================================================================ PLAYROOM
    // Areas: the round carpet in the middle with the board-game giants, the toy cars and the wooden train on it; the
    // footstools round it; the toy box in a corner, the bookcase of toys; plush toys and balls at the walls; a little
    // table; a reading armchair.
    void PlanPlayroom()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            int mx = RoomMidX(room), mz = RoomMidZ(room);
            int cell = OpenSpot(2, WM_NEAR, mx, mz, 0, ref r);
            hq_PlanPlayroom_r = r;
            hq_PlanPlayroom_mx = mx;
            hq_PlanPlayroom_mz = mz;
            hq_PlanPlayroom_cell = cell;
        }
        if (Ph(1))
        {
            GenRng r = hq_PlanPlayroom_r;
            int mx = hq_PlanPlayroom_mx;
            int mz = hq_PlanPlayroom_mz;
            int cell = hq_PlanPlayroom_cell;
            int carpet = cell >= 0 ? FreePlace(Dn(r.Chance(600) ? "hb_carpet" : "k_rug"), cell, 0, 0, 0, H_FREE) : -1;
            int cx = cell >= 0 ? cell % IW * GenData.INNER_MM + 250 : mx, cz = cell >= 0 ? cell / IW * GenData.INNER_MM + 250 : mz;
            string[] play = { "boardToys", "toyCars", "toys" };
            int got = 0;
            hq_PlanPlayroom_r = r;
            hq_PlanPlayroom_mx = mx;
            hq_PlanPlayroom_mz = mz;
            hq_PlanPlayroom_cell = cell;
            hq_PlanPlayroom_carpet = carpet;
            hq_PlanPlayroom_cx = cx;
            hq_PlanPlayroom_cz = cz;
            hq_PlanPlayroom_play = play;
            hq_PlanPlayroom_got = got;
        }
        if (PhLoop(2, 100000))
        {
            string[] play = hq_PlanPlayroom_play;
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < play.Length)) PhBreak();
                else { hq_PlanPlayroom_k = k; PhSubBegin(); }
            }
            hq_PlanPlayroom_play = play;
            if (PhSubActive())
            {
                PhPush(); PlanPlayroom_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (Ph(3))
        {
            int cx = hq_PlanPlayroom_cx;
            int cz = hq_PlanPlayroom_cz;
            int got = hq_PlanPlayroom_got;
            if (got == 0) MandFail(1);
            GenRng r5 = SR(5);
            if (r5.Chance(700) && Fit(2))
            {
                int first = nEmit;
                FSet(Dn("hb_train"), Dn("hb_tender"));
                if (FormAt(GenData.GL_ROW, 40, GenData.A_ANY, WM_NEAR, cx, cz, 0, ref r5) >= 0) Keep(first, false);
            }
            GenRng r6 = SR(6);
            hq_PlanPlayroom_cx = cx;
            hq_PlanPlayroom_cz = cz;
            hq_PlanPlayroom_got = got;
            hq_PlanPlayroom_r6 = r6;
        }
        if (PhLoop(4, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 2 && Fit(1))) PhBreak();
                else { hq_PlanPlayroom_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanPlayroom_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (Ph(5))
        {
            GenRng r7 = SR(7);
            hq_PlanPlayroom_sel1 = 0;
            if (Fit(2)) hq_PlanPlayroom_sel1 = 1;
            hq_PlanPlayroom_r7 = r7;
        }
        if (Ph(6))
        {
            if (hq_PlanPlayroom_sel1 == 1)
            {
                GenRng r7 = hq_PlanPlayroom_r7;
                int first = nEmit;
                int box = CornerPlace(Pick(ref r7, "s_chest", 5, "k_boxOpen", 4), WM_ANY, 0, 0, false, 0, ref r7);
                hq_PlanPlayroom_r7 = r7;
                hq_PlanPlayroom_first = first;
                hq_PlanPlayroom_box = box;
            }
        }
        if (Ph(7))
        {
            if (hq_PlanPlayroom_sel1 == 1)
            {
                GenRng r7 = hq_PlanPlayroom_r7;
                int first = hq_PlanPlayroom_first;
                int box = hq_PlanPlayroom_box;
                if (box >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp("plush"), box, 2, 0, ref r7)) Keep(first, false); }
                hq_PlanPlayroom_r7 = r7;
                hq_PlanPlayroom_first = first;
                hq_PlanPlayroom_box = box;
            }
        }
        if (Ph(8))
        {
            GenRng r8 = SR(8);
            if (r8.Chance(750) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("k_bookcaseLow"), WM_CENTRE, 0, 0, WIN_AVOID, 0, ref r8) >= 0) Keep(first, false); }
        }
        if (Ph(9))
        {
            GenRng r9 = SR(9);
            if (r9.Chance(600) && Fit(2)) { int first = nEmit; if (PlaceGroup(GenData.FindGrp("balls"), room, GenData.A_WALL, 2, 0, ref r9)) Keep(first, false); }
        }
        if (Ph(10))
        {
            GenRng r10 = SR(10);
            if (r10.Chance(500) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("hb_armchairBlue"), WM_ANY, 0, 0, WIN_PREFER, 0, ref r10) >= 0) Keep(first, false); }
        }
        if (Ph(11))
        {
            GenRng r11 = SR(11);
            if (r11.Chance(450) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("hb_stool"), WM_ANY, 0, 0, WIN_ANY, 0, ref r11) >= 0) Keep(first, false); }
        }
        if (Ph(12))
        {
            GenRng r12 = SR(12);
            if (r12.Chance(400) && LooseFit(1)) { int first = nEmit; if (WallPlace(Dn("mb_skates"), WM_ANY, 0, 0, WIN_ANY, 0, ref r12) >= 0 && Keep(first, false)) MarkLoose(first); }
        }
        if (PhLoop(13, 100000))
        {
            if (planIter == 0) { hq_Extras_k0 = 20; hq_Extras_chance = 450; hq_Extras_items = new string[] { "ck_lampFloor", "ck_plant", "wq_cabinetSmall" }; PhSubReset(); }
            PhPush(); Extras_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }
    void PlanPlayroom_L1()
    {
        int room = pRoom;
        if (Ph(0))
        {
            int carpet = hq_PlanPlayroom_carpet;
            int cx = hq_PlanPlayroom_cx;
            int cz = hq_PlanPlayroom_cz;
            string[] play = hq_PlanPlayroom_play;
            int k = hq_PlanPlayroom_k;
            GenRng rk = SR(2 + k);
            if (!Fit(2)) { hq_PlanPlayroom_carpet = carpet; hq_PlanPlayroom_cx = cx; hq_PlanPlayroom_cz = cz; hq_PlanPlayroom_play = play; phBreak = true; PhEnd(); return; }
            int first = nEmit;
            fHost = carpet >= 0 ? carpet : H_FREE;
            bool ok = GroupAt(GenData.FindGrp(play[k]), GenData.A_ANY, rk.Between(2, 3), WM_NEAR, cx, cz, 0, ref rk);
            fHost = H_NONE;
            hq_PlanPlayroom_carpet = carpet;
            hq_PlanPlayroom_cx = cx;
            hq_PlanPlayroom_cz = cz;
            hq_PlanPlayroom_play = play;
            hq_PlanPlayroom_first = first;
            hq_PlanPlayroom_ok = ok;
        }
        if (Ph(1))
        {
            int got = hq_PlanPlayroom_got;
            int first = hq_PlanPlayroom_first;
            bool ok = hq_PlanPlayroom_ok;
            if (ok && Keep(first, false)) got++;
            hq_PlanPlayroom_got = got;
            hq_PlanPlayroom_first = first;
            hq_PlanPlayroom_ok = ok;
        }
    }

    void PlanPlayroom_L2()
    {
        int room = pRoom;
        if (Ph(0))
        {
            int cx = hq_PlanPlayroom_cx;
            int cz = hq_PlanPlayroom_cz;
            GenRng r6 = hq_PlanPlayroom_r6;
            int c2 = OpenSpot(1, WM_NEAR, cx, cz, 0, ref r6);
            int first = nEmit;
            hq_PlanPlayroom_cx = cx;
            hq_PlanPlayroom_cz = cz;
            hq_PlanPlayroom_r6 = r6;
            hq_PlanPlayroom_c2 = c2;
            hq_PlanPlayroom_first = first;
        }
        if (Ph(1))
        {
            GenRng r6 = hq_PlanPlayroom_r6;
            int k = hq_PlanPlayroom_k;
            int c2 = hq_PlanPlayroom_c2;
            int first = hq_PlanPlayroom_first;
            if (c2 >= 0 && FreePlace(Dn(k == 0 ? "hb_footstoolG" : "hb_footstoolR"), c2, r6.Range(4) * 90, 0, 0, H_FREE) >= 0) Keep(first, false);
            hq_PlanPlayroom_r6 = r6;
            hq_PlanPlayroom_c2 = c2;
            hq_PlanPlayroom_first = first;
        }
    }


    // ============================================================================================ BOX ROOM
    // What an attic holds: trunks and a chest in a row at a wall, stacks of boxes, old pictures and the big mirror
    // leaning at the walls, the sled, a guitar case, an old wardrobe or cabinet, a standard lamp and an armchair put
    // away, comics in their boxes, skates, a helmet, and the odd book that is not a book.
    void PlanBoxRoom()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            hp_PlanBoxRoom_first = nEmit;
            if (PlaceGroup(GenData.FindGrp("trunks"), room, GenData.A_WALL, Fit(3) ? 3 : 2, EF_MANDATORY, ref r)) Keep(hp_PlanBoxRoom_first, true); else MandFail(1);
            hp_PlanBoxRoom_rows = new string[] { "oldBoxes", "frames", "cardboard", "comics", "oldBoxes" };
            hp_PlanBoxRoom_chance = new int[] { 900, 850, 650, 500, 500 };
        }
        if (PhLoop(1, 100000))
        {
            int k = planIter;
            if (!(k < hp_PlanBoxRoom_rows.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(2 + k);
                if (!rk.Chance(hp_PlanBoxRoom_chance[k]) || !Fit(2)) continue;
                hp_PlanBoxRoom_first = nEmit;
                if (PlaceGroup(GenData.FindGrp(hp_PlanBoxRoom_rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(hp_PlanBoxRoom_first, false);
            }
        }
        if (Ph(2))
        {
            hp_PlanBoxRoom_one = new string[] { "mb_mirror", "mb_sled", "mb_guitarCase", "gen_wardrobe", "q_cabinet", "q_armchair", "k_bookcaseLow", "hb_stool" };
            hp_PlanBoxRoom_ch = new int[] { 700, 550, 500, 450, 450, 400, 400, 300 };
        }
        if (PhLoop(3, 100000))
        {
            int k = planIter;
            if (!(k < hp_PlanBoxRoom_one.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(10 + k);
                if (!rk.Chance(hp_PlanBoxRoom_ch[k]) || !Fit(1)) continue;
                hp_PlanBoxRoom_first = nEmit;
                if (WallPlace(Dn(hp_PlanBoxRoom_one[k]), WM_ANY, 0, 0, WIN_ANY, 0, ref rk) >= 0) Keep(hp_PlanBoxRoom_first, false);
            }
        }
        if (Ph(4))
        {
            GenRng r20 = SR(20);
            if (r20.Chance(500) && LooseFit(2))
            {
                hp_PlanBoxRoom_first = nEmit;
                FSet(Dn("mb_skates"), Dn(r20.Chance(500) ? "mb_helmet" : "mb_mimic"));
                if (FormAt(GenData.GL_ROW, 100, GenData.A_WALL, WM_ANY, 0, 0, 0, ref r20) >= 0 && Keep(hp_PlanBoxRoom_first, false)) MarkLoose(hp_PlanBoxRoom_first);
            }
        }
        if (PhLoop(5, 100000))
        {
            if (planIter == 0) { hq_Extras_k0 = 30; hq_Extras_chance = 600; hq_Extras_items = new string[] { "cq_lampStanding", "ck_lampFloor", "cs_chest" }; PhSubReset(); }
            PhPush(); Extras_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
    }

    // ============================================================================================ PANTRY
    // Shelves of jars, bottles and food along the walls (the slots fill them), the chest freezer, crates of
    // vegetables and sacks of flour at the foot of the shelves, a keg and small barrels, a stool to reach the top.
    void PlanPantry()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            hq_PlanPantry_r = r;
        }
        if (PhLoop(1, 100000))
        {
            GenRng r = hq_PlanPantry_r;
            bool ht1 = false;
            if (planIter == 0) { hq_RackRun_n = 2; hq_RackRun_a = Dn("dg_shelves"); hq_RackRun_b = Dn("gen_rackPantry"); hq_RackRun_mode = WM_FAR; hq_RackRun_tx = RoomMidX(room); hq_RackRun_tz = RoomMidZ(room); hq_RackRun_rng = r; PhSubReset(); }
            PhPush(); RackRun_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_RackRun_rng; ht1 = hq_RackRun_ret; PhBreak(); }
            hq_PlanPantry_r = r;
            hq_PlanPantry_ht1 = ht1;
        }
        if (Ph(2))
        {
            bool ht1 = hq_PlanPantry_ht1;
            if (!ht1)
            {
                MandFail(1);
            }
            hq_PlanPantry_ht1 = ht1;
        }
        if (Ph(3))
        {
            GenRng r2 = SR(2);
            if (r2.Chance(650) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("dg_shelves"), WM_ANY, 0, 0, WIN_NEVER, 0, ref r2) >= 0) Keep(first, false); }
        }
        if (Ph(4))
        {
            GenRng r3 = SR(3);
            if (r3.Chance(650) && Fit(1)) { int first = nEmit; if (WallPlace(Dn(r3.Chance(550) ? "ms_freezer" : "k_fridge"), WM_ANY, 0, 0, WIN_ANY, 0, ref r3) >= 0) Keep(first, false); }
            string[] rows = { "crates", "sacks", "kegs", "crates", "cardboard" };
            int[] chance = { 900, 750, 600, 500, 350 };
            hq_PlanPantry_rows = rows;
            hq_PlanPantry_chance = chance;
        }
        if (PhLoop(5, 100000))
        {
            string[] rows = hq_PlanPantry_rows;
            int[] chance = hq_PlanPantry_chance;
            int k = planIter;
            if (!(k < rows.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(4 + k);
                if (!rk.Chance(chance[k]) || !Fit(2)) continue;
                int first = nEmit;
                if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(first, false);
            }
            hq_PlanPantry_rows = rows;
            hq_PlanPantry_chance = chance;
        }
        if (Ph(6))
        {
            GenRng r10 = SR(10);
            if (r10.Chance(450) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("hb_stool"), WM_ANY, 0, 0, WIN_ANY, 0, ref r10) >= 0) Keep(first, false); }
        }
        if (Ph(7))
        {
            GenRng r11 = SR(11);
            if (r11.Chance(400) && Fit(2)) { int first = nEmit; if (PlaceGroup(GenData.FindGrp("cleaning"), room, GenData.A_WALL, 2, 0, ref r11)) Keep(first, false); }
        }
    }

    // phase state of the split plans (Tools: autophase2)
    int hp_PlanTeen_bed;
    int hp_PlanTeen_bx;
    int hp_PlanTeen_bz;
    int hp_PlanBoxRoom_first;
    string[] hp_PlanBoxRoom_rows;
    int[] hp_PlanBoxRoom_chance;
    string[] hp_PlanBoxRoom_one;
    int[] hp_PlanBoxRoom_ch;

    // locals of the split plans and helpers, kept from one phase to the next (autophase4)
    int hq_CornerBed_bed;
    int hq_CornerBed_bedDef;
    int hq_CornerBed_first;
    GenRng hq_CornerBed_r;
    int hq_CornerBed_ret;
    int[] hq_PlanPantry_chance;
    bool hq_PlanPantry_ht1;
    GenRng hq_PlanPantry_r;
    string[] hq_PlanPantry_rows;
    int hq_PlanPlayroom_box;
    int hq_PlanPlayroom_c2;
    int hq_PlanPlayroom_carpet;
    int hq_PlanPlayroom_cell;
    int hq_PlanPlayroom_cx;
    int hq_PlanPlayroom_cz;
    int hq_PlanPlayroom_first;
    int hq_PlanPlayroom_got;
    int hq_PlanPlayroom_k;
    int hq_PlanPlayroom_mx;
    int hq_PlanPlayroom_mz;
    bool hq_PlanPlayroom_ok;
    string[] hq_PlanPlayroom_play;
    GenRng hq_PlanPlayroom_r;
    GenRng hq_PlanPlayroom_r6;
    GenRng hq_PlanPlayroom_r7;
    int hq_PlanPlayroom_sel1;
    int hq_PlanTeen_cell;
    int hq_PlanTeen_first;
    int hq_PlanTeen_g;
    GenRng hq_PlanTeen_r;
    GenRng hq_PlanTeen_r10;
    GenRng hq_PlanTeen_r3;
    GenRng hq_PlanTeen_r6;
    int hq_PlanTeen_seat;
    int hq_PlanTeen_sel1;
    int hq_PlanTeen_sel2;
    int hq_PlanTeen_sel3;
}
