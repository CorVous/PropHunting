// MapGen, revision 9 plans: the rooms and outdoor areas of the new kinds of plot (the owner's "more zones and blobs
// so that maps can be much more varied"). Each is set up like the real place (G28): what a corner shop, a garage,
// a farm and a park hold, where it stands and what it stands next to. Same tools and rules as the other plans:
// the anchors first, then what belongs with them, a few groups, a small remainder, within the room's budget.
public partial class MapGen
{
    // the direction (0 north +z, 1 east, 2 south, 3 west) from room's cells to a neighbouring outer cell whose room
    // is of type t, or -1
    int SideToward(int room, int t)
    {
        int best = -1, bestN = 0;
        for (int d = 0; d < 4; d++)
        {
            int n = 0;
            for (int c = 0; c < W * H; c++)
            {
                if (cellZone[c] == GenData.Z_NONE || cellRoom[c] != room) continue;
                int nx = c % W + DX[d], nz = c / W + DZ[d];
                if (!InGrid(nx, nz) || cellZone[nz * W + nx] == GenData.Z_NONE) continue;
                if (roomType[cellRoom[nz * W + nx]] == t) n++;
            }
            if (n > bestN) { bestN = n; best = d; }
        }
        return best;
    }

    // the zone of the building a doorway of this outdoor room leads into, the first that is one of the given
    int DoorZoneAny(int room)
    {
        for (int k = 0; k < roomDoorCount[room]; k++) { int z = DoorZone(room, k); if (GenData.IsBuilding(z)) return z; }
        // no door: the plot's building
        int pl = roomPlot[room];
        return pl >= 0 ? plotZone[pl] : GenData.Z_NONE;
    }

    void TagRoom(int room, int tag)
    {
        for (int k = roomCellStart[room]; k < roomCellStart[room] + roomCellCount[room]; k++) tags[roomCells[k]] |= tag;
    }

    // ============================================================================================ SHOP FLOOR
    // The checkout by the door, the freezers and the chilled shelves along the walls, gondola rows between, the
    // bread and the fruit near the door, trolleys and baskets at the entrance. One shop in four sells clothes
    // instead: racks and tables of clothes, mannequins, the till.
    void PlanShopFloor()
    {
        int room = pRoom, cells = roomOuterCells[room];
        if (Ph(0))
        {
            int dx = 0, dz = 0;
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            // what the shop sells: groceries, clothes, or plants (a garden centre)
            int theme = r.Range(1000);
            bool clothes = theme < 170, garden = theme >= 170 && theme < 340, toys = theme >= 340 && theme < 520;   // revision 11: a toy shop
            int first = nEmit;
            int till = WallPlace(Dn("ms_checkout"), WM_NEAR, dx, dz, WIN_ANY, EF_MANDATORY, ref r);
            hq_PlanShopFloor_dx = dx;
            hq_PlanShopFloor_dz = dz;
            hq_PlanShopFloor_r = r;
            hq_PlanShopFloor_clothes = clothes;
            hq_PlanShopFloor_garden = garden;
            hq_PlanShopFloor_toys = toys;
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_till = till;
        }
        if (Ph(1))
        {
            int dx = hq_PlanShopFloor_dx;
            int dz = hq_PlanShopFloor_dz;
            GenRng r = hq_PlanShopFloor_r;
            int first = hq_PlanShopFloor_first;
            int till = hq_PlanShopFloor_till;
            if (till < 0 || !Keep(first, true))
            {
                first = nEmit;
                till = CentrePiece(Dn("ms_checkout"), 0, true, 1, 0, WM_NEAR, dx, dz, 0, EF_MANDATORY, ref r);
            }
            hq_PlanShopFloor_dx = dx;
            hq_PlanShopFloor_dz = dz;
            hq_PlanShopFloor_r = r;
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_till = till;
        }
        if (Ph(2))
        {
            bool clothes = hq_PlanShopFloor_clothes;
            bool garden = hq_PlanShopFloor_garden;
            bool toys = hq_PlanShopFloor_toys;
            int first = hq_PlanShopFloor_first;
            int till = hq_PlanShopFloor_till;
            if (till < 0) MandFail(1);
            else if (Fit(1))
            {
                GenRng rb = SR(2);
                first = nEmit;
                if (GroupBeside(GenData.FindGrp("baskets"), till, rb.Between(2, 3), 0, ref rb)) Keep(first, false);
            }
            hq_PlanShopFloor_sel1 = 0;
            if (toys) hq_PlanShopFloor_sel1 = 1;
            else if (garden) hq_PlanShopFloor_sel1 = 2;
            else if (!clothes) hq_PlanShopFloor_sel1 = 3;
            else hq_PlanShopFloor_sel1 = 4;
            hq_PlanShopFloor_clothes = clothes;
            hq_PlanShopFloor_garden = garden;
            hq_PlanShopFloor_toys = toys;
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_till = till;
        }
        if (Ph(3))
        {
            if (hq_PlanShopFloor_sel1 == 1)
            {
                // a toy shop: display tables in the open with toys round them, shelves of boxed toys at the walls, the
                // wooden train, plush toys and balls in bins by the walls
                GenRng r3 = SR(3);
                hq_PlanShopFloor_r3 = r3;
            }
        }
        if (PhLoop(4, 100000))
        {
            if (!(hq_PlanShopFloor_sel1 == 1)) PhBreak();
            else
            {
                if (!PhSubActive())
                {
                    int k = planIter;
                    if (!(k < 1 + cells && Fit(1))) PhBreak();
                    else { hq_PlanShopFloor_k = k; PhSubBegin(); }
                }
                if (PhSubActive())
                {
                    PhPush(); PlanShopFloor_L1(); bool fin = PhAdvance(); PhPop();
                    if (fin) PhSubEnd(); else PhStay();
                    if (phBreak) { phBreak = false; PhBreak(); }
                    if (phReturn) { phReturn = false; PhEnd(); return; }
                }
            }
        }
        if (Ph(5))
        {
            if (hq_PlanShopFloor_sel1 == 1)
            {
                bool toys = hq_PlanShopFloor_toys;
                string[] rows = { "plush", "balls", "toys", "boardToys" };
                hq_PlanShopFloor_toys = toys;
                hq_PlanShopFloor_rows = rows;
            }
        }
        if (PhLoop(6, 100000))
        {
            if (!(hq_PlanShopFloor_sel1 == 1)) PhBreak();
            else
            {
                int first = hq_PlanShopFloor_first;
                string[] rows = hq_PlanShopFloor_rows;
                int k = planIter;
                if (!(k < rows.Length)) PhBreak();
                else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
                {
                    GenRng rk = SR(4 + k);
                    if (!rk.Chance(k < 2 ? 850 : 550) || !Fit(2)) continue;
                    first = nEmit;
                    if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(first, false);
                }
                hq_PlanShopFloor_first = first;
                hq_PlanShopFloor_rows = rows;
            }
        }
        if (Ph(7))
        {
            if (hq_PlanShopFloor_sel1 == 1)
            {
                int first = hq_PlanShopFloor_first;
                GenRng r8 = SR(8);
                if (Fit(2)) { first = nEmit; FSet(Dn("hb_train"), Dn("hb_tender")); if (FormAt(GenData.GL_ROW, 40, GenData.A_WALL, WM_ANY, 0, 0, 0, ref r8) >= 0) Keep(first, false); }
                GenRng r9b = SR(19);
                hq_PlanShopFloor_first = first;
                hq_PlanShopFloor_r9b = r9b;
            }
        }
        if (Ph(8))
        {
            if (hq_PlanShopFloor_sel1 == 1)
            {
                int dx = hq_PlanShopFloor_dx;
                int dz = hq_PlanShopFloor_dz;
                int first = hq_PlanShopFloor_first;
                GenRng r9b = hq_PlanShopFloor_r9b;
                if (Fit(2)) { first = nEmit; rowN = 2; rowDef[0] = rowDef[1] = Dn("ms_shelfEnd"); if (WallRow(0, 2, WM_FAR, dx, dz, WIN_AVOID, 0, ref r9b) >= 0) Keep(first, false); }
                hq_PlanShopFloor_dx = dx;
                hq_PlanShopFloor_dz = dz;
                hq_PlanShopFloor_first = first;
                hq_PlanShopFloor_r9b = r9b;
            }
        }
        if (Ph(9))
        {
            if (hq_PlanShopFloor_sel1 == 2)
            {
                // a garden centre: flower stands and pots in rows, plants at the walls, compost sacks, watering cans
                GenRng r3 = SR(3);
                hq_PlanShopFloor_r3 = r3;
            }
        }
        if (PhLoop(10, 100000))
        {
            if (!(hq_PlanShopFloor_sel1 == 2)) PhBreak();
            else
            {
                if (!PhSubActive())
                {
                    int k = planIter;
                    if (!(k < 1 + cells && Fit(1))) PhBreak();
                    else { hq_PlanShopFloor_k = k; PhSubBegin(); }
                }
                if (PhSubActive())
                {
                    PhPush(); PlanShopFloor_L2(); bool fin = PhAdvance(); PhPop();
                    if (fin) PhSubEnd(); else PhStay();
                    if (phBreak) { phBreak = false; PhBreak(); }
                    if (phReturn) { phReturn = false; PhEnd(); return; }
                }
            }
        }
        if (Ph(11))
        {
            if (hq_PlanShopFloor_sel1 == 2)
            {
                string[] rows = { "pots", "watering", "sacks" };
                hq_PlanShopFloor_rows = rows;
            }
        }
        if (PhLoop(12, 100000))
        {
            if (!(hq_PlanShopFloor_sel1 == 2)) PhBreak();
            else
            {
                int first = hq_PlanShopFloor_first;
                string[] rows = hq_PlanShopFloor_rows;
                int k = planIter;
                if (!(k < rows.Length)) PhBreak();
                else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
                {
                    GenRng rk = SR(4 + k);
                    if (!rk.Chance(k == 0 ? 900 : 600) || !Fit(2)) continue;
                    first = nEmit;
                    if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(first, false);
                }
                hq_PlanShopFloor_first = first;
                hq_PlanShopFloor_rows = rows;
            }
        }
        if (Ph(13))
        {
            if (hq_PlanShopFloor_sel1 == 2)
            {
                GenRng r7 = SR(7);
                for (int k = 0; k < 2 && Fit(1); k++) WallAny(WM_ANY, 0, 0, WIN_PREFER, 0, ref r7, "k_plant", "n_planter", "k_plant");
            }
        }
        if (Ph(14))
        {
            if (hq_PlanShopFloor_sel1 == 3)
            {
                int dx = hq_PlanShopFloor_dx;
                int dz = hq_PlanShopFloor_dz;
                int first = hq_PlanShopFloor_first;
                // the cold wall: freezers away from the door, the chest freezer beside them
                GenRng r3 = SR(3);
                if (Fit(2))
                {
                    first = nEmit;
                    rowN = cells >= 3 && Fit(3) ? 2 : 1;
                    for (int i = 0; i < rowN; i++) rowDef[i] = Dn("ms_freezers");
                    if (WallRow(0, 2, WM_FAR, dx, dz, WIN_NEVER, 0, ref r3) >= 0) Keep(first, false);
                }
                GenRng r4 = SR(4);
                hq_PlanShopFloor_dx = dx;
                hq_PlanShopFloor_dz = dz;
                hq_PlanShopFloor_first = first;
                hq_PlanShopFloor_r4 = r4;
            }
        }
        if (Ph(15))
        {
            if (hq_PlanShopFloor_sel1 == 3)
            {
                int first = hq_PlanShopFloor_first;
                GenRng r4 = hq_PlanShopFloor_r4;
                if (r4.Chance(600) && Fit(1)) { first = nEmit; if (WallPlace(Dn("ms_freezer"), WM_ANY, 0, 0, WIN_ANY, 0, ref r4) >= 0) Keep(first, false); }
                // shelves at the walls, then the gondola rows
                GenRng r5 = SR(5);
                hq_PlanShopFloor_first = first;
                hq_PlanShopFloor_r4 = r4;
                hq_PlanShopFloor_r5 = r5;
            }
        }
        if (Ph(16))
        {
            if (hq_PlanShopFloor_sel1 == 3)
            {
                int first = hq_PlanShopFloor_first;
                GenRng r5 = hq_PlanShopFloor_r5;
                if (Fit(2))
                {
                    first = nEmit;
                    rowN = Fit(3) && cells >= 3 ? 3 : 2;
                    for (int i = 0; i < rowN; i++) rowDef[i] = Dn("ms_shelfEnd");
                    if (WallRow(0, 2, WM_ANY, 0, 0, WIN_AVOID, 0, ref r5) >= 0) Keep(first, false);
                }
                // the gondolas (1.4 m deep, too deep for RackRows): free-standing on the open floor, along the room's long
                // side, with an aisle round each
                GenRng r6 = SR(6);
                hq_PlanShopFloor_first = first;
                hq_PlanShopFloor_r5 = r5;
                hq_PlanShopFloor_r6 = r6;
            }
        }
        if (Ph(17))
        {
            if (hq_PlanShopFloor_sel1 == 3)
            {
                GenRng r6 = hq_PlanShopFloor_r6;
                int gondola = Pick(ref r6, "ms_shelfBoxes", 5, "ms_shelfBags", 4), axisYaw = RoomLongAxis(room) == 0 ? 0 : 90;
                hq_PlanShopFloor_r6 = r6;
                hq_PlanShopFloor_gondola = gondola;
                hq_PlanShopFloor_axisYaw = axisYaw;
            }
        }
        if (PhLoop(18, 100000))
        {
            if (!(hq_PlanShopFloor_sel1 == 3)) PhBreak();
            else
            {
                if (!PhSubActive())
                {
                    int k = planIter;
                    if (!(k < 1 + cells && Fit(1))) PhBreak();
                    else { hq_PlanShopFloor_k = k; PhSubBegin(); }
                }
                if (PhSubActive())
                {
                    PhPush(); PlanShopFloor_L3(); bool fin = PhAdvance(); PhPop();
                    if (fin) PhSubEnd(); else PhStay();
                    if (phBreak) { phBreak = false; PhBreak(); }
                    if (phReturn) { phReturn = false; PhEnd(); return; }
                }
            }
        }
        if (Ph(19))
        {
            if (hq_PlanShopFloor_sel1 == 3)
            {
                int dx = hq_PlanShopFloor_dx;
                int dz = hq_PlanShopFloor_dz;
                int first = hq_PlanShopFloor_first;
                // fresh things near the door
                GenRng r7 = SR(7);
                if (Fit(1)) { first = nEmit; if (WallPlace(Dn("ms_dispBread"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r7) >= 0) Keep(first, false); }
                GenRng r8 = SR(8);
                hq_PlanShopFloor_sel2 = 0;
                if (r8.Chance(700) && Fit(1)) hq_PlanShopFloor_sel2 = 1;
                hq_PlanShopFloor_dx = dx;
                hq_PlanShopFloor_dz = dz;
                hq_PlanShopFloor_first = first;
                hq_PlanShopFloor_r8 = r8;
            }
        }
        if (Ph(20))
        {
            if (hq_PlanShopFloor_sel1 == 3 && hq_PlanShopFloor_sel2 == 1)
            {
                int dx = hq_PlanShopFloor_dx;
                int dz = hq_PlanShopFloor_dz;
                int first = hq_PlanShopFloor_first;
                GenRng r8 = hq_PlanShopFloor_r8;
                int cell = OpenSpot(2, WM_NEAR, dx, dz, 0, ref r8);
                first = nEmit;
                hq_PlanShopFloor_dx = dx;
                hq_PlanShopFloor_dz = dz;
                hq_PlanShopFloor_first = first;
                hq_PlanShopFloor_r8 = r8;
                hq_PlanShopFloor_cell = cell;
            }
        }
        if (Ph(21))
        {
            if (hq_PlanShopFloor_sel1 == 3 && hq_PlanShopFloor_sel2 == 1)
            {
                int first = hq_PlanShopFloor_first;
                GenRng r8 = hq_PlanShopFloor_r8;
                int cell = hq_PlanShopFloor_cell;
                if (cell >= 0 && FreePlace(Pick(ref r8, "ms_dispFruit", 6, "ms_dispCans", 3), cell, r8.Range(4) * 90, 1, 0, H_FREE) >= 0) Keep(first, false);
                hq_PlanShopFloor_first = first;
                hq_PlanShopFloor_r8 = r8;
                hq_PlanShopFloor_cell = cell;
            }
        }
        if (Ph(22))
        {
            if (hq_PlanShopFloor_sel1 == 4)
            {
                GenRng r3 = SR(3);
                hq_PlanShopFloor_r3 = r3;
            }
        }
        if (PhLoop(23, 100000))
        {
            if (!(hq_PlanShopFloor_sel1 == 4)) PhBreak();
            else
            {
                if (!PhSubActive())
                {
                    int k = planIter;
                    if (!(k < 2 + cells && Fit(1))) PhBreak();
                    else { hq_PlanShopFloor_k = k; PhSubBegin(); }
                }
                if (PhSubActive())
                {
                    PhPush(); PlanShopFloor_L4(); bool fin = PhAdvance(); PhPop();
                    if (fin) PhSubEnd(); else PhStay();
                    if (phBreak) { phBreak = false; PhBreak(); }
                    if (phReturn) { phReturn = false; PhEnd(); return; }
                }
            }
        }
        if (Ph(24))
        {
            if (hq_PlanShopFloor_sel1 == 4)
            {
                int first = hq_PlanShopFloor_first;
                GenRng r4 = SR(4);
                for (int k = 0; k < 2 && Fit(1); k++) { first = nEmit; if (WallPlace(Dn("ms_mannequin"), WM_ANY, 0, 0, WIN_PREFER, 0, ref r4) >= 0) Keep(first, false); }
                GenRng r5 = SR(5);
                hq_PlanShopFloor_first = first;
                hq_PlanShopFloor_r5 = r5;
            }
        }
        if (Ph(25))
        {
            if (hq_PlanShopFloor_sel1 == 4)
            {
                int dx = hq_PlanShopFloor_dx;
                int dz = hq_PlanShopFloor_dz;
                int first = hq_PlanShopFloor_first;
                GenRng r5 = hq_PlanShopFloor_r5;
                if (Fit(2)) { first = nEmit; rowN = 2; rowDef[0] = rowDef[1] = Dn("ms_shelfEnd"); if (WallRow(0, 2, WM_FAR, dx, dz, WIN_AVOID, 0, ref r5) >= 0) Keep(first, false); }
                hq_PlanShopFloor_dx = dx;
                hq_PlanShopFloor_dz = dz;
                hq_PlanShopFloor_first = first;
                hq_PlanShopFloor_r5 = r5;
            }
        }
        if (Ph(26))
        {
            int dx = hq_PlanShopFloor_dx;
            int dz = hq_PlanShopFloor_dz;
            bool clothes = hq_PlanShopFloor_clothes;
            int first = hq_PlanShopFloor_first;
            // by the door: trolleys at the wall, a machine, a plant and the bin
            GenRng r9 = SR(9);
            if (!clothes && r9.Chance(650) && Fit(2))
            {
                first = nEmit;
                FSet(Dn("ms_cart"), Dn("ms_cart"));
                if (FormAt(GenData.GL_ROW, 60, GenData.A_WALL, WM_NEAR, dx, dz, 0, ref r9) >= 0) Keep(first, false);
            }
            GenRng r10 = SR(10);
            hq_PlanShopFloor_dx = dx;
            hq_PlanShopFloor_dz = dz;
            hq_PlanShopFloor_clothes = clothes;
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r10 = r10;
        }
        if (Ph(27))
        {
            int dx = hq_PlanShopFloor_dx;
            int dz = hq_PlanShopFloor_dz;
            int first = hq_PlanShopFloor_first;
            GenRng r10 = hq_PlanShopFloor_r10;
            if (r10.Chance(450) && Fit(1)) { first = nEmit; if (WallPlace(Pick(ref r10, "ms_gacha", 5, "ms_bottleReturn", 4), WM_NEAR, dx, dz, WIN_ANY, 0, ref r10) >= 0) Keep(first, false); }
            GenRng r11 = SR(11);
            hq_PlanShopFloor_dx = dx;
            hq_PlanShopFloor_dz = dz;
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r10 = r10;
            hq_PlanShopFloor_r11 = r11;
        }
        if (Ph(28))
        {
            int dx = hq_PlanShopFloor_dx;
            int dz = hq_PlanShopFloor_dz;
            GenRng r11 = hq_PlanShopFloor_r11;
            if (r11.Chance(500) && Fit(1)) WallAny(WM_NEAR, dx, dz, WIN_ANY, 0, ref r11, "k_plant", "k_trashcan", "ms_dispSmall");
            // revision 11: the machines by the door (a claw machine, a vending machine, an arcade cabinet), and a corner of
            // another trade: a corner shop sells a bit of everything (toys and balls in a grocer's, a flower stand in a toy
            // shop, sweets and drinks in a clothes shop)
            GenRng r14 = SR(14);
            hq_PlanShopFloor_dx = dx;
            hq_PlanShopFloor_dz = dz;
            hq_PlanShopFloor_r11 = r11;
            hq_PlanShopFloor_r14 = r14;
        }
        if (PhLoop(29, 100000))
        {
            int dx = hq_PlanShopFloor_dx;
            int dz = hq_PlanShopFloor_dz;
            int first = hq_PlanShopFloor_first;
            GenRng r14 = hq_PlanShopFloor_r14;
            int k = planIter;
            if (!(k < 2 && Fit(1))) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                if (!r14.Chance(k == 0 ? 750 : 450)) continue;
                first = nEmit;
                if (WallPlace(Dn(k == 0 ? (r14.Chance(500) ? "ma_claw" : "ma_vending") : (r14.Chance(500) ? "ma_arcade" : "ms_gacha")), WM_NEAR, dx, dz, WIN_ANY, 0, ref r14) >= 0) Keep(first, false);
            }
            hq_PlanShopFloor_dx = dx;
            hq_PlanShopFloor_dz = dz;
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r14 = r14;
        }
        if (Ph(30))
        {
            bool clothes = hq_PlanShopFloor_clothes;
            bool garden = hq_PlanShopFloor_garden;
            bool toys = hq_PlanShopFloor_toys;
            GenRng r15 = SR(15);
            string[] side = toys ? new string[] { "pots", "baskets" } : (clothes ? new string[] { "baskets", "balls" } : (garden ? new string[] { "crates", "baskets" } : new string[] { "plush", "balls", "toyCars" }));
            hq_PlanShopFloor_clothes = clothes;
            hq_PlanShopFloor_garden = garden;
            hq_PlanShopFloor_toys = toys;
            hq_PlanShopFloor_r15 = r15;
            hq_PlanShopFloor_side = side;
        }
        if (PhLoop(31, 100000))
        {
            int first = hq_PlanShopFloor_first;
            GenRng r15 = hq_PlanShopFloor_r15;
            string[] side = hq_PlanShopFloor_side;
            int k = planIter;
            if (!(k < side.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                if (!r15.Chance(650) || !Fit(2)) continue;
                first = nEmit;
                if (PlaceGroup(GenData.FindGrp(side[k]), room, GenData.A_WALL, 2, 0, ref r15)) Keep(first, false);
            }
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r15 = r15;
            hq_PlanShopFloor_side = side;
        }
        if (Ph(32))
        {
            int dx = hq_PlanShopFloor_dx;
            int dz = hq_PlanShopFloor_dz;
            bool clothes = hq_PlanShopFloor_clothes;
            bool garden = hq_PlanShopFloor_garden;
            bool toys = hq_PlanShopFloor_toys;
            int first = hq_PlanShopFloor_first;
            GenRng r16 = SR(16);
            if (r16.Chance(500) && Fit(1)) { first = nEmit; if (WallPlace(Dn(toys || clothes ? "ms_dispCans" : "ms_dispFlowers"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r16) >= 0) Keep(first, false); }
            // revision 10: whatever the shop's own steps could not place is made up from the rest of its fittings, each tried
            // once in a shuffled order: at a wall, else free-standing with room round it
            string[] pool = toys ? new string[] { "ms_clothesTable", "ms_shelfEnd", "ms_dispSmall", "hb_train", "k_plant", "ms_gacha", "ms_cart", "hb_stool" }
                : clothes ? new string[] { "ms_clothesRack", "ms_clothesTable", "ms_mannequin", "ms_dispSmall", "k_plant", "ms_shelfEnd", "k_trashcan", "ms_basket" }
                : (garden ? new string[] { "ms_dispFlowers", "n_planter", "k_plant", "q_cactus", "ms_dispFruit", "ms_dispSmall", "m_wheelbarrow", "ms_cart" }
                : new string[] { "ms_dispSmall", "ms_dispCans", "ms_dispFruit", "ms_dispBread", "ms_freezer", "ms_shelfEnd", "ms_bottleReturn", "ms_gacha", "k_plant", "ms_cart",
                    "k_trashcan", "ms_freezers", "ms_basket", "ms_handTruck" });
            GenRng r12 = SR(12);
            int p0 = r12.Range(pool.Length);
            hq_PlanShopFloor_dx = dx;
            hq_PlanShopFloor_dz = dz;
            hq_PlanShopFloor_clothes = clothes;
            hq_PlanShopFloor_garden = garden;
            hq_PlanShopFloor_toys = toys;
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_pool = pool;
            hq_PlanShopFloor_r12 = r12;
            hq_PlanShopFloor_p0 = p0;
        }
        if (PhLoop(33, 100000))
        {
            string[] pool = hq_PlanShopFloor_pool;
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 2 * pool.Length && Fit(1))) PhBreak();
                else { hq_PlanShopFloor_k = k; PhSubBegin(); }
            }
            hq_PlanShopFloor_pool = pool;
            if (PhSubActive())
            {
                PhPush(); PlanShopFloor_L5(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (Ph(34))
        {
            bool clothes = hq_PlanShopFloor_clothes;
            int first = hq_PlanShopFloor_first;
            GenRng r13 = SR(13);
            if (!clothes && r13.Chance(600) && Fit(2)) { first = nEmit; if (PlaceGroup(GenData.FindGrp("crates"), room, GenData.A_WALL, 2, 0, ref r13)) Keep(first, false); }
            hq_PlanShopFloor_clothes = clothes;
            hq_PlanShopFloor_first = first;
        }
    }
    void PlanShopFloor_L1()
    {
        int room = pRoom, cells = roomOuterCells[room];
        if (Ph(0))
        {
            int first = hq_PlanShopFloor_first;
            GenRng r3 = hq_PlanShopFloor_r3;
            int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r3);
            if (cell < 0) { hq_PlanShopFloor_first = first; hq_PlanShopFloor_r3 = r3; hq_PlanShopFloor_cell = cell; phBreak = true; PhEnd(); return; }
            first = nEmit;
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r3 = r3;
            hq_PlanShopFloor_cell = cell;
        }
        if (Ph(1))
        {
            GenRng r3 = hq_PlanShopFloor_r3;
            int cell = hq_PlanShopFloor_cell;
            int t = FreePlace(Dn("ms_clothesTable"), cell, r3.Range(2) * 90, 1, 0, H_FREE);
            hq_PlanShopFloor_r3 = r3;
            hq_PlanShopFloor_cell = cell;
            hq_PlanShopFloor_t = t;
        }
        if (Ph(2))
        {
            int first = hq_PlanShopFloor_first;
            GenRng r3 = hq_PlanShopFloor_r3;
            int k = hq_PlanShopFloor_k;
            int t = hq_PlanShopFloor_t;
            if (t >= 0 && Keep(first, false) && Fit(2))
            {
                first = nEmit;
                if (GroupBeside(GenData.FindGrp(k % 2 == 0 ? "boardToys" : "toyCars"), t, 2, 0, ref r3)) Keep(first, false);
            }
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r3 = r3;
            hq_PlanShopFloor_t = t;
        }
    }

    void PlanShopFloor_L2()
    {
        int room = pRoom, cells = roomOuterCells[room];
        if (Ph(0))
        {
            int first = hq_PlanShopFloor_first;
            GenRng r3 = hq_PlanShopFloor_r3;
            int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r3);
            if (cell < 0) { hq_PlanShopFloor_first = first; hq_PlanShopFloor_r3 = r3; hq_PlanShopFloor_cell = cell; phBreak = true; PhEnd(); return; }
            first = nEmit;
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r3 = r3;
            hq_PlanShopFloor_cell = cell;
        }
        if (Ph(1))
        {
            int first = hq_PlanShopFloor_first;
            GenRng r3 = hq_PlanShopFloor_r3;
            int cell = hq_PlanShopFloor_cell;
            if (FreePlace(Dn("ms_dispFlowers"), cell, r3.Range(4) * 90, 1, 0, H_FREE) >= 0) Keep(first, false);
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r3 = r3;
            hq_PlanShopFloor_cell = cell;
        }
    }

    void PlanShopFloor_L3()
    {
        int room = pRoom, cells = roomOuterCells[room];
        if (Ph(0))
        {
            int first = hq_PlanShopFloor_first;
            GenRng r6 = hq_PlanShopFloor_r6;
            int gondola = hq_PlanShopFloor_gondola;
            int k = hq_PlanShopFloor_k;
            if (k > 0) gondola = Dn((k & 1) == 0 ? "ms_shelfBoxes" : "ms_shelfBags");   // revision 10: both kinds
            int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r6);
            if (cell < 0) { hq_PlanShopFloor_first = first; hq_PlanShopFloor_r6 = r6; hq_PlanShopFloor_gondola = gondola; hq_PlanShopFloor_cell = cell; phBreak = true; PhEnd(); return; }
            first = nEmit;
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r6 = r6;
            hq_PlanShopFloor_gondola = gondola;
            hq_PlanShopFloor_cell = cell;
        }
        if (Ph(1))
        {
            int first = hq_PlanShopFloor_first;
            int gondola = hq_PlanShopFloor_gondola;
            int axisYaw = hq_PlanShopFloor_axisYaw;
            int cell = hq_PlanShopFloor_cell;
            if (FreePlace(gondola, cell, axisYaw, 1, 0, H_FREE) >= 0) Keep(first, false);
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_gondola = gondola;
            hq_PlanShopFloor_axisYaw = axisYaw;
            hq_PlanShopFloor_cell = cell;
        }
    }

    void PlanShopFloor_L4()
    {
        int room = pRoom, cells = roomOuterCells[room];
        if (Ph(0))
        {
            int first = hq_PlanShopFloor_first;
            GenRng r3 = hq_PlanShopFloor_r3;
            int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r3);
            if (cell < 0) { hq_PlanShopFloor_first = first; hq_PlanShopFloor_r3 = r3; hq_PlanShopFloor_cell = cell; phBreak = true; PhEnd(); return; }
            first = nEmit;
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r3 = r3;
            hq_PlanShopFloor_cell = cell;
        }
        if (Ph(1))
        {
            int first = hq_PlanShopFloor_first;
            GenRng r3 = hq_PlanShopFloor_r3;
            int cell = hq_PlanShopFloor_cell;
            if (FreePlace(Pick(ref r3, "ms_clothesRack", 6, "ms_clothesTable", 3), cell, r3.Range(2) * 90, 1, 0, H_FREE) >= 0) Keep(first, false);
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r3 = r3;
            hq_PlanShopFloor_cell = cell;
        }
    }

    void PlanShopFloor_L5()
    {
        int room = pRoom, cells = roomOuterCells[room];
        if (Ph(0))
        {
            int first = hq_PlanShopFloor_first;
            string[] pool = hq_PlanShopFloor_pool;
            GenRng r12 = hq_PlanShopFloor_r12;
            int p0 = hq_PlanShopFloor_p0;
            int k = hq_PlanShopFloor_k;
            int def = Dn(pool[(p0 + k) % pool.Length]);
            if (CountInRoom(def, room) >= ScaledCap(GenData.defRoomCap[def], room)) { hq_PlanShopFloor_first = first; hq_PlanShopFloor_pool = pool; hq_PlanShopFloor_r12 = r12; hq_PlanShopFloor_p0 = p0; hq_PlanShopFloor_def = def; PhEnd(); return; }
            first = nEmit;
            if (WallPlace(def, WM_ANY, 0, 0, WIN_ANY, 0, ref r12) >= 0 && Keep(first, false)) { hq_PlanShopFloor_first = first; hq_PlanShopFloor_pool = pool; hq_PlanShopFloor_r12 = r12; hq_PlanShopFloor_p0 = p0; hq_PlanShopFloor_def = def; PhEnd(); return; }
            if (Has(def, GenData.FL_LOOSE)) { hq_PlanShopFloor_first = first; hq_PlanShopFloor_pool = pool; hq_PlanShopFloor_r12 = r12; hq_PlanShopFloor_p0 = p0; hq_PlanShopFloor_def = def; PhEnd(); return; }   // a loose thing never stands alone in the open
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_pool = pool;
            hq_PlanShopFloor_r12 = r12;
            hq_PlanShopFloor_p0 = p0;
            hq_PlanShopFloor_def = def;
        }
        if (Ph(1))
        {
            int first = hq_PlanShopFloor_first;
            GenRng r12 = hq_PlanShopFloor_r12;
            int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r12);
            first = nEmit;
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r12 = r12;
            hq_PlanShopFloor_cell = cell;
        }
        if (Ph(2))
        {
            int first = hq_PlanShopFloor_first;
            GenRng r12 = hq_PlanShopFloor_r12;
            int def = hq_PlanShopFloor_def;
            int cell = hq_PlanShopFloor_cell;
            if (cell >= 0 && FreePlace(def, cell, r12.Range(4) * 90, 1, 0, H_FREE) >= 0) Keep(first, false);
            hq_PlanShopFloor_first = first;
            hq_PlanShopFloor_r12 = r12;
            hq_PlanShopFloor_def = def;
            hq_PlanShopFloor_cell = cell;
        }
    }


    // ============================================================================================ STOCK ROOM
    // Racks of stock at the walls, a pallet of deliveries inside the service door, boxes and crates, the hand truck.
    void PlanStockroom()
    {
        int room = pRoom;
        if (Ph(0))
        {
            int dx = 0, dz = 0;
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            hq_PlanStockroom_dx = dx;
            hq_PlanStockroom_dz = dz;
            hq_PlanStockroom_r = r;
        }
        if (PhLoop(1, 100000))
        {
            int dx = hq_PlanStockroom_dx;
            int dz = hq_PlanStockroom_dz;
            GenRng r = hq_PlanStockroom_r;
            bool ht1 = false;
            if (planIter == 0) { hq_RackRun_n = 2; hq_RackRun_a = Dn("gen_rack"); hq_RackRun_b = Dn(r.Chance(500) ? "gen_rackPantry" : "gen_rack"); hq_RackRun_mode = WM_FAR; hq_RackRun_tx = dx; hq_RackRun_tz = dz; hq_RackRun_rng = r; PhSubReset(); }
            PhPush(); RackRun_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_RackRun_rng; ht1 = hq_RackRun_ret; PhBreak(); }
            hq_PlanStockroom_dx = dx;
            hq_PlanStockroom_dz = dz;
            hq_PlanStockroom_r = r;
            hq_PlanStockroom_ht1 = ht1;
        }
        if (Ph(2))
        {
            bool ht1 = hq_PlanStockroom_ht1;
            if (!ht1) MandFail(1);
            hp_PlanStockroom_first = 0;
            int ext = ExtDoor(room);
            GenRng r2 = SR(2);
            hq_PlanStockroom_sel1 = 0;
            if (Fit(1)) hq_PlanStockroom_sel1 = 1;
            hq_PlanStockroom_ht1 = ht1;
            hq_PlanStockroom_ext = ext;
            hq_PlanStockroom_r2 = r2;
        }
        if (Ph(3))
        {
            if (hq_PlanStockroom_sel1 == 1)
            {
                int dx = hq_PlanStockroom_dx;
                int dz = hq_PlanStockroom_dz;
                int ext = hq_PlanStockroom_ext;
                GenRng r2 = hq_PlanStockroom_r2;
                int tx = ext >= 0 ? DoorX(room, ext) : dx, tz = ext >= 0 ? DoorZ(room, ext) : dz;
                int cell = OpenSpot(2, WM_NEAR, tx, tz, 0, ref r2);
                hq_PlanStockroom_dx = dx;
                hq_PlanStockroom_dz = dz;
                hq_PlanStockroom_ext = ext;
                hq_PlanStockroom_r2 = r2;
                hq_PlanStockroom_cell = cell;
            }
        }
        if (Ph(4))
        {
            if (hq_PlanStockroom_sel1 == 1)
            {
                GenRng r2 = hq_PlanStockroom_r2;
                int cell = hq_PlanStockroom_cell;
                if (cell >= 0) PalletBlock(cell, 1, 1, r2.Range(2) * 90, ref r2);
                hq_PlanStockroom_r2 = r2;
                hq_PlanStockroom_cell = cell;
            }
        }
        if (Ph(5))
        {
            string[] rows = { "boxes", "crates", "cardboard", "sacks" };
            hq_PlanStockroom_rows = rows;
        }
        if (PhLoop(6, 100000))
        {
            string[] rows = hq_PlanStockroom_rows;
            int k = planIter;
            if (!(k < rows.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(3 + k);
                if (!rk.Chance(k < 2 ? 800 : 450) || !Fit(2)) continue;
                hp_PlanStockroom_first = nEmit;
                if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(hp_PlanStockroom_first, false);
            }
            hq_PlanStockroom_rows = rows;
        }
        if (Ph(7))
        {
            int dx = hq_PlanStockroom_dx;
            int dz = hq_PlanStockroom_dz;
            GenRng r8 = SR(8);
            if (r8.Chance(600) && Fit(1)) { hp_PlanStockroom_first = nEmit; if (WallPlace(Pick(ref r8, "ms_handTruck", 5, "ms_cart", 3), WM_NEAR, dx, dz, WIN_ANY, 0, ref r8) >= 0) Keep(hp_PlanStockroom_first, false); }
            hq_PlanStockroom_dx = dx;
            hq_PlanStockroom_dz = dz;
        }
        if (Ph(8))
        {
            GenRng r9 = SR(9);
            if (r9.Chance(350) && Fit(1)) { hp_PlanStockroom_first = nEmit; if (WallPlace(Pick(ref r9, "ms_freezer", 5, "ms_dispCans", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r9) >= 0) Keep(hp_PlanStockroom_first, false); }
        }
        if (Ph(9))
        {
            // revision 11: the shelves of dry goods (jars and bottles from the slots), old boxes, drums, the step stool
            GenRng r10 = SR(10);
            if (r10.Chance(700) && Fit(1)) { hp_PlanStockroom_first = nEmit; if (WallPlace(Dn("dg_shelves"), WM_ANY, 0, 0, WIN_NEVER, 0, ref r10) >= 0) Keep(hp_PlanStockroom_first, false); }
            hp_PlanStockroom_more = new string[] { "oldBoxes", "kegs", "boxes" };
        }
        if (PhLoop(10, 100000))
        {
            int k = planIter;
            if (!(k < hp_PlanStockroom_more.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(11 + k);
                if (!rk.Chance(k == 0 ? 650 : 450) || !Fit(2)) continue;
                hp_PlanStockroom_first = nEmit;
                if (PlaceGroup(GenData.FindGrp(hp_PlanStockroom_more[k]), room, GenData.A_WALL, 2, 0, ref rk)) Keep(hp_PlanStockroom_first, false);
            }
        }
        if (Ph(11))
        {
            GenRng r14 = SR(14);
            if (r14.Chance(400) && Fit(1)) { hp_PlanStockroom_first = nEmit; if (WallPlace(Dn(r14.Chance(500) ? "hb_stool" : "ma_vending"), WM_ANY, 0, 0, WIN_ANY, 0, ref r14) >= 0) Keep(hp_PlanStockroom_first, false); }
            // what the steps above could not place is made up from the rest of a stock room's things, each tried once
            hp_PlanStockroom_pool = new string[] { "gen_rack", "dg_shelves", "ms_freezers", "ms_freezer", "w_palletLoaded", "s_boxLarge", "dg_keg", "ms_cart", "k_trashcan", "ms_dispCans",
                "ms_handTruck", "w_pallet", "dg_trunkL", "hb_stool" };
        }
        if (Ph(12))
        {
            hp_PlanStockroom_r15 = SR(15);
            hp_PlanStockroom_p0 = hp_PlanStockroom_r15.Range(hp_PlanStockroom_pool.Length);
        }
        if (PhLoop(13, 100000))
        {
            int k = planIter;
            if (!(k < hp_PlanStockroom_pool.Length && Fit(1))) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                int def = Dn(hp_PlanStockroom_pool[(hp_PlanStockroom_p0 + k) % hp_PlanStockroom_pool.Length]);
                if (CountInRoom(def, room) >= ScaledCap(GenData.defRoomCap[def], room)) continue;
                hp_PlanStockroom_first = nEmit;
                if (WallPlace(def, WM_ANY, 0, 0, WIN_ANY, 0, ref hp_PlanStockroom_r15) >= 0) Keep(hp_PlanStockroom_first, false);
            }
        }
    }

    // ============================================================================================ WORKSHOP BAY
    // A car in for repair, its nose to the roller door; the workbench and its tools at a side wall, tyres stacked
    // against the walls, parts taken off the car, oil drums, a rack of spares.
    void PlanGarageBay()
    {
        int room = pRoom, cells = roomOuterCells[room];
        if (Ph(0))
        {
            int dx = 0, dz = 0;
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            int front = SideToward(room, GenData.R_FORECOURT);
            int yaw = front >= 0 ? DirYawSite(front) : 0;
            string[] inRepair = { "c_sedan", "c_suv", "c_hatch", "c_sports", "c_luxury", "c_taxi", "c_police", "c_ambulance" };   // revision 12: the police car and the ambulance
            hq_PlanGarageBay_dx = dx;
            hq_PlanGarageBay_dz = dz;
            hq_PlanGarageBay_r = r;
            hq_PlanGarageBay_yaw = yaw;
            hq_PlanGarageBay_inRepair = inRepair;
        }
        if (PhLoop(1, 100000))
        {
            GenRng r = hq_PlanGarageBay_r;
            int yaw = hq_PlanGarageBay_yaw;
            string[] inRepair = hq_PlanGarageBay_inRepair;
            int car = 0;
            if (planIter == 0) { hq_CentrePiece_def = Dn(inRepair[r.Range(inRepair.Length)]); hq_CentrePiece_yaw = yaw; hq_CentrePiece_bothYaws = false; hq_CentrePiece_ringWant = 1; hq_CentrePiece_ringMin = 0; hq_CentrePiece_mode = WM_CENTRE; hq_CentrePiece_tx = RoomMidX(room); hq_CentrePiece_tz = RoomMidZ(room); hq_CentrePiece_need = 0; hq_CentrePiece_flags = EF_MANDATORY; hq_CentrePiece_rng = r; PhSubReset(); }
            PhPush(); CentrePiece_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_CentrePiece_rng; car = hq_CentrePiece_ret; PhBreak(); }
            hq_PlanGarageBay_r = r;
            hq_PlanGarageBay_yaw = yaw;
            hq_PlanGarageBay_inRepair = inRepair;
            hq_PlanGarageBay_car = car;
        }
        if (Ph(2))
        {
            GenRng r = hq_PlanGarageBay_r;
            int yaw = hq_PlanGarageBay_yaw;
            int car = hq_PlanGarageBay_car;
            if (car < 0) car = CentrePiece(Dn("c_hatch"), yaw + 90, false, 0, 0, WM_ANY, 0, 0, 0, EF_MANDATORY, ref r);
            if (car < 0) MandFail(1);
            int cx = car >= 0 ? emitX[car] : RoomMidX(room), cz = car >= 0 ? emitZ[car] : RoomMidZ(room);
            GenRng r2 = SR(2);
            hq_PlanGarageBay_sel1 = 0;
            if (Fit(1)) hq_PlanGarageBay_sel1 = 1;
            hq_PlanGarageBay_r = r;
            hq_PlanGarageBay_yaw = yaw;
            hq_PlanGarageBay_car = car;
            hq_PlanGarageBay_cx = cx;
            hq_PlanGarageBay_cz = cz;
            hq_PlanGarageBay_r2 = r2;
        }
        if (Ph(3))
        {
            if (hq_PlanGarageBay_sel1 == 1)
            {
                GenRng r2 = hq_PlanGarageBay_r2;
                int first = nEmit;
                int wb = WallPlace(Dn("s_workbench"), WM_ANY, 0, 0, WIN_ANY, 0, ref r2);
                hq_PlanGarageBay_r2 = r2;
                hq_PlanGarageBay_first = first;
                hq_PlanGarageBay_wb = wb;
            }
        }
        if (Ph(4))
        {
            if (hq_PlanGarageBay_sel1 == 1)
            {
                GenRng r2 = hq_PlanGarageBay_r2;
                int first = hq_PlanGarageBay_first;
                int wb = hq_PlanGarageBay_wb;
                if (wb >= 0 && Keep(first, false) && Fit(2))
                {
                    first = nEmit;
                    if (GroupBeside(GenData.FindGrp("tools"), wb, r2.Between(2, 3), 0, ref r2)) Keep(first, false);
                }
                hq_PlanGarageBay_r2 = r2;
                hq_PlanGarageBay_first = first;
                hq_PlanGarageBay_wb = wb;
            }
        }
        if (Ph(5))
        {
            string[] rows = { "tyres", "scrap", "fuel", "barrels" };
            int[] chance = { 900, 750, 550, 350 };
            hq_PlanGarageBay_rows = rows;
            hq_PlanGarageBay_chance = chance;
        }
        if (PhLoop(6, 100000))
        {
            string[] rows = hq_PlanGarageBay_rows;
            int[] chance = hq_PlanGarageBay_chance;
            int k = planIter;
            if (!(k < rows.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(3 + k);
                if (!rk.Chance(chance[k]) || !Fit(2)) continue;
                int first = nEmit;
                if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, rk.Between(2, k == 0 ? 4 : 3), 0, ref rk)) Keep(first, false);
            }
            hq_PlanGarageBay_rows = rows;
            hq_PlanGarageBay_chance = chance;
        }
        if (Ph(7))
        {
            int cx = hq_PlanGarageBay_cx;
            int cz = hq_PlanGarageBay_cz;
            GenRng r8 = SR(8);
            if (cells >= 2 && r8.Chance(600) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("gen_rack"), WM_FAR, cx, cz, WIN_NEVER, 0, ref r8) >= 0) Keep(first, false); }
            GenRng r9 = SR(9);
            hq_PlanGarageBay_cx = cx;
            hq_PlanGarageBay_cz = cz;
            hq_PlanGarageBay_r9 = r9;
        }
        if (Ph(8))
        {
            int car = hq_PlanGarageBay_car;
            GenRng r9 = hq_PlanGarageBay_r9;
            if (r9.Chance(500) && LooseFit(1) && car >= 0)
            {
                int first = nEmit;
                if (RelBeside(Dn("c_cone"), car, r9.Chance(500) ? 1 : -1, 250, 0, ref r9) >= 0 && Keep(first, false)) MarkLoose(first);
            }
            GenRng r10 = SR(10);
            hq_PlanGarageBay_car = car;
            hq_PlanGarageBay_r9 = r9;
            hq_PlanGarageBay_r10 = r10;
        }
        if (Ph(9))
        {
            int dx = hq_PlanGarageBay_dx;
            int dz = hq_PlanGarageBay_dz;
            GenRng r10 = hq_PlanGarageBay_r10;
            if (r10.Chance(400) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ms_handTruck"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r10) >= 0) Keep(first, false); }
            hq_PlanGarageBay_dx = dx;
            hq_PlanGarageBay_dz = dz;
            hq_PlanGarageBay_r10 = r10;
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(10, PoolLen(new string[] { "gen_toolwall", "s_workbench", "gen_rack", "ms_handTruck", "c_cone", "k_trashcan", "c_engine", "c_bumper", "c_door", "c_plate", "c_tire", "c_wheel",
            "c_spoiler", "c_axle", "c_wheelRacing" })) && !PoolStep(50, new string[] { "gen_toolwall", "s_workbench", "gen_rack", "ms_handTruck", "c_cone", "k_trashcan", "c_engine", "c_bumper", "c_door", "c_plate", "c_tire", "c_wheel",
            "c_spoiler", "c_axle", "c_wheelRacing" })) PhBreak();
}

    // a run of up to n racks along a wall (a, then b, a, ...), fewer if no wall is that long: kept, or false
    bool RackRun(int n, int a, int b, int mode, int tx, int tz, ref GenRng rng)
    {
        hq_RackRun_n = n;
        hq_RackRun_a = a;
        hq_RackRun_b = b;
        hq_RackRun_mode = mode;
        hq_RackRun_tx = tx;
        hq_RackRun_tz = tz;
        hq_RackRun_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); RackRun_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_RackRun_rng;
        return hq_RackRun_ret;
    }

    // RackRun in phases, a step a call (autophase4); RackRun() above runs them to the end
    void RackRun_Body()
    {
        if (PhLoop(0, 100000))
        {
            int n = hq_RackRun_n;
            int a = hq_RackRun_a;
            int b = hq_RackRun_b;
            int mode = hq_RackRun_mode;
            int tx = hq_RackRun_tx;
            int tz = hq_RackRun_tz;
            GenRng rng = hq_RackRun_rng;
            if (planIter == 0) hq_RackRun_m0 = n;
            int m = hq_RackRun_m0 - planIter;
            if (!(m >= 1)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                int first = nEmit;
                rowN = m;
                for (int i = 0; i < m; i++) rowDef[i] = (i & 1) == 0 ? a : b;
                if (WallRow(0, 2, mode, tx, tz, WIN_NEVER, EF_MANDATORY, ref rng) >= 0 && Keep(first, true)) { hq_RackRun_ret = true; hq_RackRun_n = n; hq_RackRun_a = a; hq_RackRun_b = b; hq_RackRun_mode = mode; hq_RackRun_tx = tx; hq_RackRun_tz = tz; hq_RackRun_rng = rng; PhEnd(); return; }
            }
            hq_RackRun_n = n;
            hq_RackRun_a = a;
            hq_RackRun_b = b;
            hq_RackRun_mode = mode;
            hq_RackRun_tx = tx;
            hq_RackRun_tz = tz;
            hq_RackRun_rng = rng;
        }
        if (Ph(1))
        {
            { hq_RackRun_ret = false; PhEnd(); return; }
        }
    }

    // yaw (degrees) whose front looks along outer direction d (0 north +z, 1 east +x, 2 south, 3 west)
    static int DirYawSite(int d) { return d == 0 ? 0 : (d == 1 ? 90 : (d == 2 ? 180 : 270)); }

    // ============================================================================================ PARTS ROOM
    void PlanParts()
    {
        int room = pRoom, cells = roomOuterCells[room];
        if (Ph(0))
        {
            GenRng r = SR(1);
            hq_PlanParts_r = r;
        }
        if (PhLoop(1, 100000))
        {
            GenRng r = hq_PlanParts_r;
            bool ht1 = false;
            if (planIter == 0) { hq_RackRun_n = cells >= 2 ? 3 : 2; hq_RackRun_a = Dn("gen_rack"); hq_RackRun_b = Dn("gen_rack"); hq_RackRun_mode = WM_CENTRE; hq_RackRun_tx = 0; hq_RackRun_tz = 0; hq_RackRun_rng = r; PhSubReset(); }
            PhPush(); RackRun_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { r = hq_RackRun_rng; ht1 = hq_RackRun_ret; PhBreak(); }
            hq_PlanParts_r = r;
            hq_PlanParts_ht1 = ht1;
        }
        if (Ph(2))
        {
            bool ht1 = hq_PlanParts_ht1;
            if (!ht1) MandFail(1);
            hp_PlanParts_first = 0;
            hq_PlanParts_ht1 = ht1;
        }
        if (Ph(3))
        {
            GenRng r2 = SR(2);
            if (cells >= 2) RackRows(room, Dn("gen_rack"), cells, ref r2);
            hp_PlanParts_rows = new string[] { "tyres", "scrap", "boxes", "fuel" };
        }
        if (PhLoop(4, 100000))
        {
            int k = planIter;
            if (!(k < hp_PlanParts_rows.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(3 + k);
                if (!rk.Chance(k < 2 ? 850 : 500) || !Fit(2)) continue;
                hp_PlanParts_first = nEmit;
                if (PlaceGroup(GenData.FindGrp(hp_PlanParts_rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(hp_PlanParts_first, false);
            }
        }
        if (Ph(5))
        {
            GenRng r7 = SR(7);
            if (r7.Chance(450) && Fit(1)) { hp_PlanParts_first = nEmit; if (WallPlace(Pick(ref r7, "s_workbench", 4, "s_boxLarge", 4, "w_parts", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r7) >= 0) Keep(hp_PlanParts_first, false); }
    
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(6, PoolLen(new string[] { "gen_toolwall", "gen_rack", "s_workbench", "w_parts", "s_boxLarge", "ms_handTruck", "c_engine", "c_bumper", "c_door", "c_plate", "c_tire", "c_wheel",
            "c_spoiler", "c_axle", "c_wheelTruck" })) && !PoolStep(50, new string[] { "gen_toolwall", "gen_rack", "s_workbench", "w_parts", "s_boxLarge", "ms_handTruck", "c_engine", "c_bumper", "c_door", "c_plate", "c_tire", "c_wheel",
            "c_spoiler", "c_axle", "c_wheelTruck" })) PhBreak();
    }

    // ============================================================================================ BARN
    // Hay stacked at the walls, the animals in the back half, the cart or the tractor inside the big door, the
    // wheelbarrow, feed sacks, buckets and tools.
    void PlanBarn()
    {
        int room = pRoom, cells = roomOuterCells[room];
        if (Ph(0))
        {
            int dx = 0, dz = 0;
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            hp_PlanBarn_first = nEmit;
            hq_PlanBarn_dx = dx;
            hq_PlanBarn_dz = dz;
            hq_PlanBarn_r = r;
        }
        if (Ph(1))
        {
            GenRng r = hq_PlanBarn_r;
            if (PlaceGroup(GenData.FindGrp("hay"), room, GenData.A_WALL, Fit(4) ? r.Between(3, 4) : 2, EF_MANDATORY, ref r)) Keep(hp_PlanBarn_first, true); else MandFail(1);
            GenRng r2 = SR(2);
            hq_PlanBarn_sel1 = 0;
            if (Fit(1)) hq_PlanBarn_sel1 = 1;
            hq_PlanBarn_r = r;
            hq_PlanBarn_r2 = r2;
        }
        if (Ph(2))
        {
            if (hq_PlanBarn_sel1 == 1)
            {
                int dx = hq_PlanBarn_dx;
                int dz = hq_PlanBarn_dz;
                GenRng r2 = hq_PlanBarn_r2;
                int cell = OpenSpot(3, WM_NEAR, dx, dz, 0, ref r2);
                hp_PlanBarn_first = nEmit;
                hq_PlanBarn_dx = dx;
                hq_PlanBarn_dz = dz;
                hq_PlanBarn_r2 = r2;
                hq_PlanBarn_cell = cell;
            }
        }
        if (Ph(3))
        {
            if (hq_PlanBarn_sel1 == 1)
            {
                GenRng r2 = hq_PlanBarn_r2;
                int cell = hq_PlanBarn_cell;
                if (cell >= 0 && FreePlace(Pick(ref r2, "f_cart", 5, "c_tractor", cells >= 3 ? 4 : 0, "f_cartHigh", 3), cell, r2.Range(4) * 90, 0, 0, H_FREE) >= 0) Keep(hp_PlanBarn_first, false);
                hq_PlanBarn_r2 = r2;
                hq_PlanBarn_cell = cell;
            }
        }
        if (Ph(4))
        {
            GenRng r3 = SR(3);
            hq_PlanBarn_r3 = r3;
        }
        if (PhLoop(5, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < (cells >= 3 ? 2 : 1) && Fit(1))) PhBreak();
                else { hq_PlanBarn_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanBarn_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (Ph(6))
        {
            GenRng r4 = SR(4);
            if (r4.Chance(500) && Fit(2)) { hp_PlanBarn_first = nEmit; if (PlaceGroup(GenData.FindGrp("hens"), room, GenData.A_OUT_OPEN, 2, 0, ref r4)) Keep(hp_PlanBarn_first, false); }
            string[] rows = { "sacks", "buckets", "tools", "hay" };
            hq_PlanBarn_rows = rows;
        }
        if (PhLoop(7, 100000))
        {
            string[] rows = hq_PlanBarn_rows;
            int k = planIter;
            if (!(k < rows.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(5 + k);
                if (!rk.Chance(k < 2 ? 700 : 450) || !Fit(2)) continue;
                hp_PlanBarn_first = nEmit;
                if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(hp_PlanBarn_first, false);
            }
            hq_PlanBarn_rows = rows;
        }
        if (Ph(8))
        {
            int dx = hq_PlanBarn_dx;
            int dz = hq_PlanBarn_dz;
            GenRng r9 = SR(9);
            if (r9.Chance(600) && Fit(1)) { hp_PlanBarn_first = nEmit; if (WallPlace(Dn("m_wheelbarrow"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r9) >= 0) Keep(hp_PlanBarn_first, false); }
            // the bench where things get mended, a rack of feed and fuel, produce in crates, timber, the farm cat
            GenRng r10 = SR(10);
            hq_PlanBarn_sel2 = 0;
            if (cells >= 2 && r10.Chance(650) && Fit(1)) hq_PlanBarn_sel2 = 1;
            hq_PlanBarn_dx = dx;
            hq_PlanBarn_dz = dz;
            hq_PlanBarn_r10 = r10;
        }
        if (Ph(9))
        {
            if (hq_PlanBarn_sel2 == 1)
            {
                GenRng r10 = hq_PlanBarn_r10;
                hp_PlanBarn_first = nEmit;
                int wb = WallPlace(Dn("s_workbench"), WM_ANY, 0, 0, WIN_ANY, 0, ref r10);
                hq_PlanBarn_r10 = r10;
                hq_PlanBarn_wb = wb;
            }
        }
        if (Ph(10))
        {
            if (hq_PlanBarn_sel2 == 1)
            {
                GenRng r10 = hq_PlanBarn_r10;
                int wb = hq_PlanBarn_wb;
                if (wb >= 0 && Keep(hp_PlanBarn_first, false) && Fit(2)) { hp_PlanBarn_first = nEmit; if (GroupBeside(GenData.FindGrp("tools"), wb, 2, 0, ref r10)) Keep(hp_PlanBarn_first, false); }
                hq_PlanBarn_r10 = r10;
                hq_PlanBarn_wb = wb;
            }
        }
        if (Ph(11))
        {
            int dx = hq_PlanBarn_dx;
            int dz = hq_PlanBarn_dz;
            GenRng r11 = SR(11);
            if (r11.Chance(600) && Fit(1)) { hp_PlanBarn_first = nEmit; if (WallPlace(Dn("gen_rack"), WM_FAR, dx, dz, WIN_NEVER, 0, ref r11) >= 0) Keep(hp_PlanBarn_first, false); }
            string[] more = { "crates", "fuel" };
            hq_PlanBarn_dx = dx;
            hq_PlanBarn_dz = dz;
            hq_PlanBarn_more = more;
        }
        if (PhLoop(12, 100000))
        {
            string[] more = hq_PlanBarn_more;
            int k = planIter;
            if (!(k < more.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(12 + k);
                if (!rk.Chance(k == 0 ? 650 : 400) || !Fit(2)) continue;
                hp_PlanBarn_first = nEmit;
                if (PlaceGroup(GenData.FindGrp(more[k]), room, GenData.A_WALL, 2, 0, ref rk)) Keep(hp_PlanBarn_first, false);
            }
            hq_PlanBarn_more = more;
        }
        if (Ph(13))
        {
            GenRng r14 = SR(14);
            if (r14.Chance(500) && Fit(1)) { hp_PlanBarn_first = nEmit; if (WallPlace(Pick(ref r14, "w_planks", 4, "w_logs", 4, "w_pallet", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r14) >= 0) Keep(hp_PlanBarn_first, false); }
            GenRng r15 = SR(15);
            hq_PlanBarn_sel3 = 0;
            if (r15.Chance(450) && Fit(1)) hq_PlanBarn_sel3 = 1;
            hq_PlanBarn_r15 = r15;
        }
        if (Ph(14))
        {
            if (hq_PlanBarn_sel3 == 1)
            {
                GenRng r15 = hq_PlanBarn_r15;
                int cell = OpenSpot(1, WM_ANY, 0, 0, 0, ref r15);
                hp_PlanBarn_first = nEmit;
                hq_PlanBarn_r15 = r15;
                hq_PlanBarn_cell = cell;
            }
        }
        if (Ph(15))
        {
            if (hq_PlanBarn_sel3 == 1)
            {
                GenRng r15 = hq_PlanBarn_r15;
                int cell = hq_PlanBarn_cell;
                if (cell >= 0 && FreePlace(Dn("p_cat"), cell, r15.Range(360), 0, 0, H_FREE) >= 0) Keep(hp_PlanBarn_first, false);
                hq_PlanBarn_r15 = r15;
                hq_PlanBarn_cell = cell;
            }
        }
        if (Ph(16))
        {
            int dx = hq_PlanBarn_dx;
            int dz = hq_PlanBarn_dz;
            // revision 10: the trough by the animals, a hog, tools at the wall, a lantern, a spare wheel
            GenRng r16 = SR(16);
            if (r16.Chance(650) && Fit(1)) { hp_PlanBarn_first = nEmit; if (WallPlace(Dn("gen_trough"), WM_FAR, dx, dz, WIN_ANY, 0, ref r16) >= 0) Keep(hp_PlanBarn_first, false); }
            GenRng r17 = SR(17);
            hq_PlanBarn_sel4 = 0;
            if (cells >= 2 && r17.Chance(500) && Fit(1)) hq_PlanBarn_sel4 = 1;
            hq_PlanBarn_dx = dx;
            hq_PlanBarn_dz = dz;
            hq_PlanBarn_r17 = r17;
        }
        if (Ph(17))
        {
            if (hq_PlanBarn_sel4 == 1)
            {
                int dx = hq_PlanBarn_dx;
                int dz = hq_PlanBarn_dz;
                GenRng r17 = hq_PlanBarn_r17;
                int cell = OpenSpot(2, WM_FAR, dx, dz, 0, ref r17);
                hp_PlanBarn_first = nEmit;
                hq_PlanBarn_dx = dx;
                hq_PlanBarn_dz = dz;
                hq_PlanBarn_r17 = r17;
                hq_PlanBarn_cell = cell;
            }
        }
        if (Ph(18))
        {
            if (hq_PlanBarn_sel4 == 1)
            {
                GenRng r17 = hq_PlanBarn_r17;
                int cell = hq_PlanBarn_cell;
                if (cell >= 0 && FreePlace(Dn("p_hog"), cell, r17.Range(360), 0, 0, H_FREE) >= 0) Keep(hp_PlanBarn_first, false);
                hq_PlanBarn_r17 = r17;
                hq_PlanBarn_cell = cell;
            }
        }
        if (Ph(19))
        {
            GenRng r18 = SR(18);
            if (r18.Chance(600) && Fit(2)) { hp_PlanBarn_first = nEmit; if (PlaceGroup(GenData.FindGrp("handtools"), room, GenData.A_WALL, 2, 0, ref r18)) Keep(hp_PlanBarn_first, false); }
        }
        if (Ph(20))
        {
            GenRng r19 = SR(19);
            if (r19.Chance(450) && Fit(1)) { hp_PlanBarn_first = nEmit; if (WallPlace(Pick(ref r19, "g_lantern", 4, "c_tractorWheel", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r19) >= 0) Keep(hp_PlanBarn_first, false); }
    
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(21, PoolLen(new string[] { "s_workbench", "gen_rack", "w_pallet", "w_planks", "w_logs", "n_logStack", "m_wheelbarrow", "gen_trough", "g_lantern", "c_tractorWheel", "f_cart" })) && !PoolStep(50, new string[] { "s_workbench", "gen_rack", "w_pallet", "w_planks", "w_logs", "n_logStack", "m_wheelbarrow", "gen_trough", "g_lantern", "c_tractorWheel", "f_cart" })) PhBreak();
    }
    void PlanBarn_L1()
    {
        int room = pRoom, cells = roomOuterCells[room];
        if (Ph(0))
        {
            int dx = hq_PlanBarn_dx;
            int dz = hq_PlanBarn_dz;
            GenRng r3 = hq_PlanBarn_r3;
            int cell = OpenSpot(2, WM_FAR, dx, dz, 0, ref r3);
            hp_PlanBarn_first = nEmit;
            hq_PlanBarn_dx = dx;
            hq_PlanBarn_dz = dz;
            hq_PlanBarn_r3 = r3;
            hq_PlanBarn_cell = cell;
        }
        if (Ph(1))
        {
            GenRng r3 = hq_PlanBarn_r3;
            int cell = hq_PlanBarn_cell;
            if (cell >= 0 && FreePlace(Pick(ref r3, "p_cow", 5, "p_pig", 4), cell, r3.Range(360), 0, 0, H_FREE) >= 0) Keep(hp_PlanBarn_first, false);
            hq_PlanBarn_r3 = r3;
            hq_PlanBarn_cell = cell;
        }
    }


    // ============================================================================================ FEED STORE
    void PlanFeedStore()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            hp_PlanFeedStore_first = nEmit;
            if (PlaceGroup(GenData.FindGrp("sacks"), room, GenData.A_WALL, 3, EF_MANDATORY, ref r)) Keep(hp_PlanFeedStore_first, true); else MandFail(1);
            hp_PlanFeedStore_rows = new string[] { "hay", "barrels", "buckets", "crates" };
        }
        if (PhLoop(1, 100000))
        {
            int k = planIter;
            if (!(k < hp_PlanFeedStore_rows.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(2 + k);
                if (!rk.Chance(k < 2 ? 750 : 450) || !Fit(2)) continue;
                hp_PlanFeedStore_first = nEmit;
                if (PlaceGroup(GenData.FindGrp(hp_PlanFeedStore_rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(hp_PlanFeedStore_first, false);
            }
        }
        if (Ph(2))
        {
            GenRng r6 = SR(6);
            if (r6.Chance(500) && Fit(1)) { hp_PlanFeedStore_first = nEmit; if (WallPlace(Pick(ref r6, "gen_rack", 4, "w_pallet", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r6) >= 0) Keep(hp_PlanFeedStore_first, false); }
        }
        if (Ph(3))
        {
            GenRng r7 = SR(7);
            if (r7.Chance(400) && Fit(2)) { hp_PlanFeedStore_first = nEmit; if (PlaceGroup(GenData.FindGrp("fuel"), room, GenData.A_WALL, 2, 0, ref r7)) Keep(hp_PlanFeedStore_first, false); }
            // revision 10: rope and the lantern, hand tools, the wheelbarrow, the farm cat
            hp_PlanFeedStore_more = new string[] { "smallTools", "handtools", "boxes" };
        }
        if (PhLoop(4, 100000))
        {
            int k = planIter;
            if (!(k < hp_PlanFeedStore_more.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(8 + k);
                if (!rk.Chance(k == 0 ? 750 : 500) || !Fit(2)) continue;
                hp_PlanFeedStore_first = nEmit;
                if (PlaceGroup(GenData.FindGrp(hp_PlanFeedStore_more[k]), room, GenData.A_WALL, 2, 0, ref rk)) Keep(hp_PlanFeedStore_first, false);
            }
        }
        if (Ph(5))
        {
            GenRng r11 = SR(11);
            if (r11.Chance(500) && Fit(1)) { hp_PlanFeedStore_first = nEmit; if (WallPlace(Pick(ref r11, "m_wheelbarrow", 4, "gen_trough", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r11) >= 0) Keep(hp_PlanFeedStore_first, false); }
        }
        if (Ph(6))
        {
            GenRng r12 = SR(12);
            hq_PlanFeedStore_sel1 = 0;
            if (r12.Chance(400) && Fit(1)) hq_PlanFeedStore_sel1 = 1;
            hq_PlanFeedStore_r12 = r12;
        }
        if (Ph(7))
        {
            if (hq_PlanFeedStore_sel1 == 1)
            {
                GenRng r12 = hq_PlanFeedStore_r12;
                int cell = OpenSpot(1, WM_ANY, 0, 0, 0, ref r12);
                hp_PlanFeedStore_first = nEmit;
                hq_PlanFeedStore_r12 = r12;
                hq_PlanFeedStore_cell = cell;
            }
        }
        if (Ph(8))
        {
            if (hq_PlanFeedStore_sel1 == 1)
            {
                GenRng r12 = hq_PlanFeedStore_r12;
                int cell = hq_PlanFeedStore_cell;
                if (cell >= 0 && FreePlace(Dn("p_cat"), cell, r12.Range(360), 0, 0, H_FREE) >= 0) Keep(hp_PlanFeedStore_first, false);
                hq_PlanFeedStore_r12 = r12;
                hq_PlanFeedStore_cell = cell;
            }
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(9, PoolLen(new string[] { "gen_rack", "w_pallet", "m_wheelbarrow", "gen_trough", "g_rope", "g_lantern", "dg_barrel" })) && !PoolStep(50, new string[] { "gen_rack", "w_pallet", "m_wheelbarrow", "gen_trough", "g_rope", "g_lantern", "dg_barrel" })) PhBreak();
    }

    // ============================================================================================ FORECOURT
    // In front of a shop: the sign by the door, a bench and a bin, flowers and fruit set out by the door, a trolley
    // bay. In front of a garage: cars waiting at the fence, nose out, tyres against the wall, cones, oil drums.
    void PlanForecourt()
    {
        if (Ph(0))
        {
            int room = pRoom, dx = 0, dz = 0;
            FrontMarkers(30);
            MainDoor(room, out dx, out dz);
            bool garage = DoorZoneAny(room) == GenData.Z_GARAGE;
            hq_PlanForecourt_sel1 = 0;
            if (garage) hq_PlanForecourt_sel1 = 1;
            else hq_PlanForecourt_sel1 = 2;
            hq_PlanForecourt_room = room;
            hq_PlanForecourt_dx = dx;
            hq_PlanForecourt_dz = dz;
            hq_PlanForecourt_garage = garage;
        }
        if (Ph(1))
        {
            if (hq_PlanForecourt_sel1 == 1)
            {
                // a filling station now and then: a pump island out on the forecourt, a stack of tyres at its edge
                GenRng rp = SR(9);
                hq_PlanForecourt_sel2 = 0;
                if (rp.Chance(550) && Fit(2)) hq_PlanForecourt_sel2 = 1;
                hq_PlanForecourt_rp = rp;
            }
        }
        if (Ph(2))
        {
            if (hq_PlanForecourt_sel1 == 1 && hq_PlanForecourt_sel2 == 1)
            {
                int first = nEmit;
                hq_PlanForecourt_first = first;
            }
        }
        if (PhLoop(3, 100000))
        {
            if (!(hq_PlanForecourt_sel1 == 1 && hq_PlanForecourt_sel2 == 1)) PhBreak();
            else
            {
                int dx = hq_PlanForecourt_dx;
                int dz = hq_PlanForecourt_dz;
                GenRng rp = hq_PlanForecourt_rp;
                int ht1 = 0;
                if (planIter == 0) { hq_CentrePiece_def = Dn("gen_fuelPump"); hq_CentrePiece_yaw = rp.Range(2) * 90; hq_CentrePiece_bothYaws = true; hq_CentrePiece_ringWant = 2; hq_CentrePiece_ringMin = 1; hq_CentrePiece_mode = WM_FAR; hq_CentrePiece_tx = dx; hq_CentrePiece_tz = dz; hq_CentrePiece_need = 0; hq_CentrePiece_flags = 0; hq_CentrePiece_rng = rp; PhSubReset(); }
                PhPush(); CentrePiece_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) { rp = hq_CentrePiece_rng; ht1 = hq_CentrePiece_ret; PhBreak(); }
                hq_PlanForecourt_dx = dx;
                hq_PlanForecourt_dz = dz;
                hq_PlanForecourt_rp = rp;
                hq_PlanForecourt_ht1 = ht1;
            }
        }
        if (Ph(4))
        {
            if (hq_PlanForecourt_sel1 == 1 && hq_PlanForecourt_sel2 == 1)
            {
                int first = hq_PlanForecourt_first;
                int ht1 = hq_PlanForecourt_ht1;
                if (ht1 >= 0) Keep(first, false);
                first = nEmit;
                hq_PlanForecourt_first = first;
                hq_PlanForecourt_ht1 = ht1;
            }
        }
        if (Ph(5))
        {
            if (hq_PlanForecourt_sel1 == 1 && hq_PlanForecourt_sel2 == 1)
            {
                GenRng rp = hq_PlanForecourt_rp;
                int first = hq_PlanForecourt_first;
                if (rp.Chance(600) && Fit(1) && WallPlace(Dn("gen_tireStack"), WM_ANY, 0, 0, WIN_ANY, 0, ref rp) >= 0) Keep(first, false);
                hq_PlanForecourt_rp = rp;
                hq_PlanForecourt_first = first;
            }
        }
        if (Ph(6))
        {
            if (hq_PlanForecourt_sel1 == 1)
            {
                int room = hq_PlanForecourt_room;
                int cars = roomOuterCells[room] >= 3 ? 2 : 1;
                hq_PlanForecourt_room = room;
                hq_PlanForecourt_cars = cars;
            }
        }
        if (PhLoop(7, 100000))
        {
            if (!(hq_PlanForecourt_sel1 == 1)) PhBreak();
            else
            {
                int dx = hq_PlanForecourt_dx;
                int dz = hq_PlanForecourt_dz;
                int cars = hq_PlanForecourt_cars;
                int k = planIter;
                if (!(k < cars && Fit(1))) PhBreak();
                else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
                {
                    GenRng rk = SR(1 + k);
                    int first = nEmit;
                    string[] waiting = { "c_sedan", "c_van", "c_truck", "c_taxi", "c_suv", "c_sports", "c_luxury", "c_police", "c_flatbed", "c_ambulance", "c_delivery", "c_deliveryFlat",
                        "c_binLorry", "c_firetruck" };
                    int car = Dn(waiting[rk.Range(waiting.Length)]);
                    if (WallPlace(car, WM_FAR, dx, dz, WIN_ANY, 0, ref rk) >= 0) Keep(first, false);
                }
                hq_PlanForecourt_dx = dx;
                hq_PlanForecourt_dz = dz;
                hq_PlanForecourt_cars = cars;
            }
        }
        if (Ph(8))
        {
            if (hq_PlanForecourt_sel1 == 1)
            {
                int dx = hq_PlanForecourt_dx;
                int dz = hq_PlanForecourt_dz;
                GenRng r3 = SR(3);
                if (Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("tyres"), GenData.A_BUILDING, r3.Between(2, 4), WM_NEAR, dx, dz, 0, ref r3)) Keep(first, false); }
                GenRng r4 = SR(4);
                hq_PlanForecourt_dx = dx;
                hq_PlanForecourt_dz = dz;
                hq_PlanForecourt_r4 = r4;
            }
        }
        if (Ph(9))
        {
            if (hq_PlanForecourt_sel1 == 1)
            {
                int dx = hq_PlanForecourt_dx;
                int dz = hq_PlanForecourt_dz;
                GenRng r4 = hq_PlanForecourt_r4;
                if (r4.Chance(700) && Fit(2))
                {
                    int first = nEmit;
                    FSet(Dn("c_cone"), Dn("c_cone"));
                    if (FormAt(GenData.GL_ROW, 300, GenData.A_OUT_OPEN, WM_NEAR, dx, dz, 0, ref r4) >= 0) Keep(first, false);
                }
                GenRng r5 = SR(5);
                hq_PlanForecourt_dx = dx;
                hq_PlanForecourt_dz = dz;
                hq_PlanForecourt_r4 = r4;
                hq_PlanForecourt_r5 = r5;
            }
        }
        if (Ph(10))
        {
            if (hq_PlanForecourt_sel1 == 1)
            {
                GenRng r5 = hq_PlanForecourt_r5;
                if (r5.Chance(450) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("fuel"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r5)) Keep(first, false); }
                hq_PlanForecourt_r5 = r5;
            }
        }
        if (Ph(11))
        {
            if (hq_PlanForecourt_sel1 == 2)
            {
                int dx = hq_PlanForecourt_dx;
                int dz = hq_PlanForecourt_dz;
                GenRng r1 = SR(1);
                if (Fit(1)) { int first = nEmit; if (WallPlace(Dn("ms_dispFlowers"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r1) >= 0) Keep(first, false); }
                GenRng r2 = SR(2);
                hq_PlanForecourt_dx = dx;
                hq_PlanForecourt_dz = dz;
                hq_PlanForecourt_r2 = r2;
            }
        }
        if (Ph(12))
        {
            if (hq_PlanForecourt_sel1 == 2)
            {
                int dx = hq_PlanForecourt_dx;
                int dz = hq_PlanForecourt_dz;
                GenRng r2 = hq_PlanForecourt_r2;
                if (r2.Chance(500) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ms_dispFruit"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r2) >= 0) Keep(first, false); }
                hq_PlanForecourt_dx = dx;
                hq_PlanForecourt_dz = dz;
                hq_PlanForecourt_r2 = r2;
            }
        }
        if (PhLoop(13, 100000))
        {
            if (!(hq_PlanForecourt_sel1 == 2)) PhBreak();
            else
            {
                if (planIter == 0) { hq_BenchAtEdge_k = 3; PhSubReset(); }
                PhPush(); BenchAtEdge_Body(); bool fin = PhAdvance(); PhPop();
                if (fin) {  PhBreak(); }
            }
        }
        if (Ph(14))
        {
            if (hq_PlanForecourt_sel1 == 2)
            {
                GenRng r4 = SR(4);
                if (r4.Chance(700) && Fit(2))
                {
                    int first = nEmit;
                    FSet(Dn("ms_cart"), Dn("ms_cart"), Dn("ms_cart"));
                    if (FormAt(GenData.GL_ROW, 60, GenData.A_OUT_EDGE, WM_ANY, 0, 0, 0, ref r4) >= 0) Keep(first, false);
                }
                GenRng r5 = SR(5);
                hq_PlanForecourt_r5 = r5;
            }
        }
        if (Ph(15))
        {
            if (hq_PlanForecourt_sel1 == 2)
            {
                int dx = hq_PlanForecourt_dx;
                int dz = hq_PlanForecourt_dz;
                GenRng r5 = hq_PlanForecourt_r5;
                if (r5.Chance(400) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ms_bottleReturn"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r5) >= 0) Keep(first, false); }
                hq_PlanForecourt_dx = dx;
                hq_PlanForecourt_dz = dz;
                hq_PlanForecourt_r5 = r5;
            }
        }
        if (Ph(16))
        {
            int dx = hq_PlanForecourt_dx;
            int dz = hq_PlanForecourt_dz;
            GenRng r6 = SR(6);
            if (r6.Chance(650) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("k_trashcan"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r6) >= 0) Keep(first, false); }
            hq_PlanForecourt_dx = dx;
            hq_PlanForecourt_dz = dz;
        }
        if (Ph(17))
        {
            bool garage = hq_PlanForecourt_garage;
            if (garage) FillFromPool(50, new string[] { "c_cone", "k_bench", "c_tire", "c_wheel", "gen_dumpster", "c_bumper", "ma_vending" });
            else FillFromPool(50, new string[] { "ms_dispFlowers", "ms_dispFruit", "ms_cart", "k_bench", "ms_bottleReturn", "ma_vending", "ma_claw", "gen_dumpster" });
            hq_PlanForecourt_garage = garage;
        }
    }

    // ============================================================================================ FARMYARD
    // Trodden earth in front of the barn: the cart or the tractor parked at the fence, hay against the barn, hens
    // in the open, the wheelbarrow, buckets at the water tap, the wood pile.
    void PlanFarmyard()
    {
        int room = pRoom, dx = 0, dz = 0;
        if (Ph(0))
        {
            dx = hq_PlanFarmyard_dx;
            dz = hq_PlanFarmyard_dz;
            FrontMarkers(30);
            MainDoor(room, out dx, out dz);
            GenRng r = SR(1);
            hq_PlanFarmyard_dx = dx;
            hq_PlanFarmyard_dz = dz;
            hq_PlanFarmyard_r = r;
        }
        if (Ph(1))
        {
            dx = hq_PlanFarmyard_dx;
            dz = hq_PlanFarmyard_dz;
            GenRng r = hq_PlanFarmyard_r;
            if (Fit(1)) { int first = nEmit; if (WallPlace(Pick(ref r, "c_tractor", 5, "f_cart", 4, "f_cartHigh", 3), WM_FAR, dx, dz, WIN_ANY, 0, ref r) >= 0) Keep(first, false); }
            GenRng r2 = SR(2);
            hq_PlanFarmyard_dx = dx;
            hq_PlanFarmyard_dz = dz;
            hq_PlanFarmyard_r = r;
            hq_PlanFarmyard_r2 = r2;
        }
        if (Ph(2))
        {
            GenRng r2 = hq_PlanFarmyard_r2;
            if (Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("hay"), GenData.A_BUILDING, r2.Between(2, 3), WM_ANY, 0, 0, 0, ref r2)) Keep(first, false); }
            GenRng r3 = SR(3);
            hq_PlanFarmyard_r2 = r2;
            hq_PlanFarmyard_r3 = r3;
        }
        if (Ph(3))
        {
            GenRng r3 = hq_PlanFarmyard_r3;
            if (r3.Chance(750) && Fit(2)) { int first = nEmit; if (PlaceGroup(GenData.FindGrp("hens"), room, GenData.A_OUT_OPEN, r3.Between(2, Fit(4) ? 4 : 2), 0, ref r3)) Keep(first, false); }
            GenRng r4 = SR(4);
            hq_PlanFarmyard_sel1 = 0;
            if (r4.Chance(600) && Fit(2)) hq_PlanFarmyard_sel1 = 1;
            hq_PlanFarmyard_r3 = r3;
            hq_PlanFarmyard_r4 = r4;
        }
        if (Ph(4))
        {
            if (hq_PlanFarmyard_sel1 == 1)
            {
                GenRng r4 = hq_PlanFarmyard_r4;
                int first = nEmit;
                int tap = WallPlace(Dn("gen_tap"), WM_ANY, 0, 0, WIN_ANY, 0, ref r4);
                hq_PlanFarmyard_r4 = r4;
                hq_PlanFarmyard_first = first;
                hq_PlanFarmyard_tap = tap;
            }
        }
        if (Ph(5))
        {
            if (hq_PlanFarmyard_sel1 == 1)
            {
                GenRng r4 = hq_PlanFarmyard_r4;
                int first = hq_PlanFarmyard_first;
                int tap = hq_PlanFarmyard_tap;
                if (tap >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp("buckets"), tap, 2, 0, ref r4)) Keep(first, false); }
                hq_PlanFarmyard_r4 = r4;
                hq_PlanFarmyard_first = first;
                hq_PlanFarmyard_tap = tap;
            }
        }
        if (Ph(6))
        {
            GenRng r5 = SR(5);
            if (r5.Chance(550) && Fit(1)) { int first = nEmit; if (WallPlace(Pick(ref r5, "m_wheelbarrow", 5, "n_logStack", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r5) >= 0) Keep(first, false); }
            GenRng r6 = SR(6);
            hq_PlanFarmyard_r6 = r6;
        }
        if (Ph(7))
        {
            dx = hq_PlanFarmyard_dx;
            dz = hq_PlanFarmyard_dz;
            GenRng r6 = hq_PlanFarmyard_r6;
            if (r6.Chance(400) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("sacks"), GenData.A_BUILDING, 2, WM_NEAR, dx, dz, 0, ref r6)) Keep(first, false); }
            // the farm stand at the lane: crates of produce and a bench; the farm dog; the wood pile
            GenRng r7 = SR(7);
            hq_PlanFarmyard_dx = dx;
            hq_PlanFarmyard_dz = dz;
            hq_PlanFarmyard_r6 = r6;
            hq_PlanFarmyard_r7 = r7;
        }
        if (Ph(8))
        {
            dx = hq_PlanFarmyard_dx;
            dz = hq_PlanFarmyard_dz;
            GenRng r7 = hq_PlanFarmyard_r7;
            if (r7.Chance(650) && Fit(3))
            {
                int first = nEmit;
                if (GroupAt(GenData.FindGrp("crates"), GenData.A_OUT_EDGE, 3, WM_FAR, dx, dz, 0, ref r7) && Keep(first, false) && Fit(1)) BenchAtEdge(8);
            }
            GenRng r9 = SR(9);
            hq_PlanFarmyard_sel2 = 0;
            if (r9.Chance(600) && Fit(1)) hq_PlanFarmyard_sel2 = 1;
            hq_PlanFarmyard_dx = dx;
            hq_PlanFarmyard_dz = dz;
            hq_PlanFarmyard_r7 = r7;
            hq_PlanFarmyard_r9 = r9;
        }
        if (Ph(9))
        {
            if (hq_PlanFarmyard_sel2 == 1)
            {
                dx = hq_PlanFarmyard_dx;
                dz = hq_PlanFarmyard_dz;
                GenRng r9 = hq_PlanFarmyard_r9;
                int cell = OpenSpot(2, WM_NEAR, dx, dz, 0, ref r9);
                int first = nEmit;
                hq_PlanFarmyard_dx = dx;
                hq_PlanFarmyard_dz = dz;
                hq_PlanFarmyard_r9 = r9;
                hq_PlanFarmyard_cell = cell;
                hq_PlanFarmyard_first = first;
            }
        }
        if (Ph(10))
        {
            if (hq_PlanFarmyard_sel2 == 1)
            {
                GenRng r9 = hq_PlanFarmyard_r9;
                int cell = hq_PlanFarmyard_cell;
                int first = hq_PlanFarmyard_first;
                if (cell >= 0 && FreePlace(Dn("p_dog"), cell, r9.Range(360), 0, 0, H_FREE) >= 0) Keep(first, false);
                hq_PlanFarmyard_r9 = r9;
                hq_PlanFarmyard_cell = cell;
                hq_PlanFarmyard_first = first;
            }
        }
        if (Ph(11))
        {
            GenRng r10 = SR(10);
            if (r10.Chance(450) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("logpile"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r10)) Keep(first, false); }
        }
        if (Ph(12))
        {
            // revision 10: the farm stand, the coop, the trough, rabbits; the digger now and then
            FarmStand(40);
        }
        if (PhLoop(13, 100000))
        {
            if (planIter == 0) { hq_FarmAnimals_k0 = 41; hq_FarmAnimals_yard = true; PhSubReset(); }
            PhPush(); FarmAnimals_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (Ph(14))
        {
            dx = hq_PlanFarmyard_dx;
            dz = hq_PlanFarmyard_dz;
            GenRng r45 = SR(45);
            if (r45.Chance(300) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("c_digger"), WM_FAR, dx, dz, WIN_ANY, 0, ref r45) >= 0) Keep(first, false); }
            hq_PlanFarmyard_dx = dx;
            hq_PlanFarmyard_dz = dz;
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(15, PoolLen(new string[] { "f_cart", "f_cartHigh", "m_wheelbarrow", "n_logStack", "gen_tap", "k_bench", "w_pallet", "gen_coop", "gen_trough", "c_tractorWheel" })) && !PoolStep(50, new string[] { "f_cart", "f_cartHigh", "m_wheelbarrow", "n_logStack", "gen_tap", "k_bench", "w_pallet", "gen_coop", "gen_trough", "c_tractorWheel" })) PhBreak();
}

    // ============================================================================================ FIELD
    // Ploughed ground in rows of one crop each, the rows along the field's long side; the wheelbarrow and the
    // tools at the fence, a bale at the gate.
    void PlanField()
    {
        int room = pRoom;
        if (Ph(0))
        {
            TagRoom(room, GenData.T_SOFT_GROUND);
        }
        if (Ph(1))
        {
            GenRng r = SR(1);
            hp_PlanField_crops = new string[] { "rowCorn", "rowWheat", "rowLettuce", "rowTurnip", "vegCarrot", "vegPumpkin", "vegMelon", "rowCornYoung", "rowWheatYoung", "rowLettuceYoung", "rowBamboo" };
            hp_PlanField_c0 = r.Range(hp_PlanField_crops.Length);
            hp_PlanField_placedRows = 0;
        }
        if (PhLoop(2, 100000))
        {
            int k = planIter;
            if (!(k < 14 && Fit(3))) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                GenRng rk = SR(2 + k);
                int g = GenData.FindGrp(hp_PlanField_crops[(hp_PlanField_c0 + k) % hp_PlanField_crops.Length]);   // revision 10: a different crop every row
                int first = nEmit;
                if (PlaceGroup(g, room, GenData.A_SOFT, rk.Between(3, Fit(5) ? 5 : 3), 0, ref rk) && Keep(first, false)) hp_PlanField_placedRows++;
            }
        }
        if (Ph(3))
        {
            if (hp_PlanField_placedRows == 0) MandFail(1);
        }
        if (Ph(4))
        {
            GenRng r20 = SR(20);
            if (r20.Chance(600) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("m_wheelbarrow"), WM_ANY, 0, 0, WIN_ANY, 0, ref r20) >= 0) Keep(first, false); }
        }
        if (Ph(5))
        {
            GenRng r21 = SR(21);
            if (r21.Chance(500) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("tools"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r21)) Keep(first, false); }
        }
        if (Ph(6))
        {
            // water for the hp_PlanField_crops: a barrel or the tap with its buckets at the fence; a bale left at the edge
            GenRng r22 = SR(22);
            hq_PlanField_sel1 = 0;
            if (r22.Chance(650) && Fit(2)) hq_PlanField_sel1 = 1;
            hq_PlanField_r22 = r22;
        }
        if (Ph(7))
        {
            if (hq_PlanField_sel1 == 1)
            {
                GenRng r22 = hq_PlanField_r22;
                int first = nEmit;
                int w = WallPlace(Pick(ref r22, "gen_tap", 4, "s_barrel", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r22);
                hq_PlanField_r22 = r22;
                hq_PlanField_first = first;
                hq_PlanField_w = w;
            }
        }
        if (Ph(8))
        {
            if (hq_PlanField_sel1 == 1)
            {
                GenRng r22 = hq_PlanField_r22;
                int first = hq_PlanField_first;
                int w = hq_PlanField_w;
                if (w >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp(r22.Chance(500) ? "buckets" : "watering"), w, 2, 0, ref r22)) Keep(first, false); }
                hq_PlanField_r22 = r22;
                hq_PlanField_first = first;
                hq_PlanField_w = w;
            }
        }
        if (Ph(9))
        {
            GenRng r23 = SR(23);
            if (r23.Chance(450) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("hay"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r23)) Keep(first, false); }
        }
    }

    // ============================================================================================ PADDOCK
    // Grass beside the barn with a few animals grazing, hay put out for them at the fence, the water bucket, a
    // shade tree.
    void PlanPaddock()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            // revision 10: more animals, of two kinds
            int animals = 2 + AreaM2(room) / 16, got = 0;
            string kind = r.Chance(550) ? "p_cow" : "p_pig", kind2 = r.Chance(500) ? "p_hog" : (kind == "p_cow" ? "p_pig" : "p_cow");
            hq_PlanPaddock_r = r;
            hq_PlanPaddock_animals = animals;
            hq_PlanPaddock_got = got;
            hq_PlanPaddock_kind = kind;
            hq_PlanPaddock_kind2 = kind2;
        }
        if (PhLoop(1, 100000))
        {
            int animals = hq_PlanPaddock_animals;
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < animals && Fit(1))) PhBreak();
                else { hq_PlanPaddock_k = k; PhSubBegin(); }
            }
            hq_PlanPaddock_animals = animals;
            if (PhSubActive())
            {
                PhPush(); PlanPaddock_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (Ph(2))
        {
            int got = hq_PlanPaddock_got;
            if (got == 0) MandFail(1);
            GenRng r8 = SR(8);
            if (Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("hay"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r8)) Keep(first, false); }
            GenRng r9 = SR(9);
            hq_PlanPaddock_got = got;
            hq_PlanPaddock_r9 = r9;
        }
        if (Ph(3))
        {
            GenRng r9 = hq_PlanPaddock_r9;
            if (r9.Chance(650) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("m_bucketWater"), WM_ANY, 0, 0, WIN_ANY, 0, ref r9) >= 0) Keep(first, false); }
            GenRng r10 = SR(10);
            hq_PlanPaddock_r9 = r9;
            hq_PlanPaddock_r10 = r10;
        }
        if (Ph(4))
        {
            GenRng r10 = hq_PlanPaddock_r10;
            if (r10.Chance(450) && Fit(2)) { int first = nEmit; if (PlaceGroup(GenData.FindGrp("hens"), room, GenData.A_OUT_OPEN, 2, 0, ref r10)) Keep(first, false); }
            GenRng r11 = SR(11);
            hq_PlanPaddock_sel1 = 0;
            if (r11.Chance(450) && Fit(1)) hq_PlanPaddock_sel1 = 1;
            hq_PlanPaddock_r10 = r10;
            hq_PlanPaddock_r11 = r11;
        }
        if (Ph(5))
        {
            if (hq_PlanPaddock_sel1 == 1)
            {
                GenRng r11 = hq_PlanPaddock_r11;
                int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r11);
                int first = nEmit;
                hq_PlanPaddock_r11 = r11;
                hq_PlanPaddock_cell = cell;
                hq_PlanPaddock_first = first;
            }
        }
        if (Ph(6))
        {
            if (hq_PlanPaddock_sel1 == 1)
            {
                GenRng r11 = hq_PlanPaddock_r11;
                int cell = hq_PlanPaddock_cell;
                int first = hq_PlanPaddock_first;
                if (cell >= 0 && FreePlace(Dn("p_dog"), cell, r11.Range(360), 0, 0, H_FREE) >= 0) Keep(first, false);
                hq_PlanPaddock_r11 = r11;
                hq_PlanPaddock_cell = cell;
                hq_PlanPaddock_first = first;
            }
        }
        if (Ph(7))
        {
            GenRng r12 = SR(12);
            if (r12.Chance(400) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("n_logStack"), WM_ANY, 0, 0, WIN_ANY, 0, ref r12) >= 0) Keep(first, false); }
        }
        if (PhLoop(8, 100000))
        {
            if (planIter == 0) { hq_FarmAnimals_k0 = 40; hq_FarmAnimals_yard = false; PhSubReset(); }
            PhPush(); FarmAnimals_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) {  PhBreak(); }
        }
        if (Ph(9))
        {
            GenRng r = hq_PlanPaddock_r;
            int trees = TreeCount(room, ref r);
            hq_PlanPaddock_r = r;
            hq_PlanPaddock_trees = trees;
        }
        if (Ph(10))
        {
            int trees = hq_PlanPaddock_trees;
            for (int t = 0; t < trees; t++) LawnTree(60 + t);
            hq_PlanPaddock_trees = trees;
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(11, PoolLen(new string[] { "n_stump", "f_cart", "m_wheelbarrow", "gen_trough", "gen_coop", "n_logStack", "n_bushLarge" })) && !PoolStep(50, new string[] { "n_stump", "f_cart", "m_wheelbarrow", "gen_trough", "gen_coop", "n_logStack", "n_bushLarge" })) PhBreak();
}
    void PlanPaddock_L1()
    {
        int room = pRoom;
        if (Ph(0))
        {
            int k = hq_PlanPaddock_k;
            GenRng rk = SR(2 + k);
            int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref rk);
            int first = nEmit;
            hq_PlanPaddock_rk = rk;
            hq_PlanPaddock_cell = cell;
            hq_PlanPaddock_first = first;
        }
        if (Ph(1))
        {
            int got = hq_PlanPaddock_got;
            string kind = hq_PlanPaddock_kind;
            string kind2 = hq_PlanPaddock_kind2;
            int k = hq_PlanPaddock_k;
            GenRng rk = hq_PlanPaddock_rk;
            int cell = hq_PlanPaddock_cell;
            int first = hq_PlanPaddock_first;
            if (cell >= 0 && FreePlace(Dn((k & 1) == 0 ? kind : kind2), cell, rk.Range(360), 0, 0, H_FREE) >= 0 && Keep(first, false)) got++;
            hq_PlanPaddock_got = got;
            hq_PlanPaddock_kind = kind;
            hq_PlanPaddock_kind2 = kind2;
            hq_PlanPaddock_rk = rk;
            hq_PlanPaddock_cell = cell;
            hq_PlanPaddock_first = first;
        }
    }


    // ============================================================================================ PARK
    // A small public garden off the lane: a fountain in the middle of a big one, gravel paths from it to the
    // lane and across, benches facing the paths, lamp posts at the paths, trees, flower borders, bins, a
    // picnic table on the grass.
    void PlanPark()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r = SR(1);
            int mx = RoomMidX(room), mz = RoomMidZ(room);
            int fountain = -1;
            if (AreaM2(room) >= 40 && Fit(1)) fountain = CentrePiece(Dn(r.Chance(550) ? "f_fountain" : "f_fountainSq"), 0, false, 1, 0, WM_NEAR, mx, mz, 0, 0, ref r);
            int fx = fountain >= 0 ? emitX[fountain] : mx, fz = fountain >= 0 ? emitZ[fountain] : mz;
            // the gravel path, 1 m wide: across the lawn from the lane to the areas behind it (revision 10: no longer
            // along it too, which cut the 4 m strip into pieces too small for anything but benches)
            int cx = FloorDiv(fx, GenData.INNER_MM), cz = FloorDiv(fz, GenData.INNER_MM);
            for (int d = 0; d < 4; d++)
                for (int s = 0; s < 40 && (((d & 1) == 1) != (RoomLongAxis(room) == 0)); s++)
                {
                    bool stop = false;
                    for (int w = 0; w < 2; w++)
                    {
                        int x = cx + DX[d] * s + ((d & 1) == 0 ? w : 0), z = cz + DZ[d] * s + ((d & 1) == 1 ? w : 0);
                        if (x < 0 || z < 0 || x >= IW || z >= IH || innerRoom[z * IW + x] != room) { stop = true; continue; }
                        int i = z * IW + x;
                        if (occ[i] != 0 || solid[i] > 0) continue;   // round the fountain
                        tags[i] |= GenData.T_PATH;
                    }
                    if (stop) break;
                }
            hq_PlanPark_r = r;
            hq_PlanPark_fx = fx;
            hq_PlanPark_fz = fz;
        }
        if (Ph(1))
        {
            RoomWalkable(room);
            GenRng r3 = SR(3);
            hq_PlanPark_r3 = r3;
        }
        if (PhLoop(2, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 1 + AreaM2(room) / 50 && Fit(1))) PhBreak();
                else { hq_PlanPark_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanPark_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (Ph(3))
        {
            GenRng r = hq_PlanPark_r;
            int fx = hq_PlanPark_fx;
            int fz = hq_PlanPark_fz;
            planR = r; planHX = fx; planHZ = fz;
            hq_PlanPark_r = r;
            hq_PlanPark_fx = fx;
            hq_PlanPark_fz = fz;
        }
        // a part a step (Cor: build freezes): the fountain, the path and its lamps; the trees one a step; the rest
        if (Ph(4))
        {
            planN = TreeCount(room, ref planR);
        }
        if (PhLoop(5, planN)) LawnTree(60 + planIter);
        if (Ph(6))
        {
            int fx = planHX, fz = planHZ;
            GenRng r4 = SR(4);
            hq_PlanPark_sel1 = 0;
            if (r4.Chance(500) && Fit(1)) hq_PlanPark_sel1 = 1;
            hq_PlanPark_fx = fx;
            hq_PlanPark_fz = fz;
            hq_PlanPark_r4 = r4;
        }
        if (Ph(7))
        {
            if (hq_PlanPark_sel1 == 1)
            {
                int fx = hq_PlanPark_fx;
                int fz = hq_PlanPark_fz;
                GenRng r4 = hq_PlanPark_r4;
                int cell = OpenSpot(3, WM_FAR, fx, fz, 0, ref r4);
                int first = nEmit;
                hq_PlanPark_fx = fx;
                hq_PlanPark_fz = fz;
                hq_PlanPark_r4 = r4;
                hq_PlanPark_cell = cell;
                hq_PlanPark_first = first;
            }
        }
        if (Ph(8))
        {
            if (hq_PlanPark_sel1 == 1)
            {
                int cell = hq_PlanPark_cell;
                int first = hq_PlanPark_first;
                if (cell >= 0 && FreePlace(Dn("r_tableRound"), cell, 0, 1, 0, H_FREE) >= 0) Keep(first, false);
                hq_PlanPark_cell = cell;
                hq_PlanPark_first = first;
            }
        }
        if (Ph(9))
        {
            int fx = hq_PlanPark_fx;
            int fz = hq_PlanPark_fz;
            GenRng r5 = SR(5);
            if (r5.Chance(700) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("k_trashcan"), WM_NEAR, fx, fz, WIN_ANY, 0, ref r5) >= 0) Keep(first, false); }
            // statues on the grass beside the paths, turned to them; planters and pots at the fence
            GenRng r6 = SR(6);
            string[] statues = { "n_statueHead", "n_obelisk", "n_column", "n_statueRing", "n_statueBlock", "n_columnBroken" };
            int s0 = r6.Range(statues.Length);
            hq_PlanPark_fx = fx;
            hq_PlanPark_fz = fz;
            hq_PlanPark_r6 = r6;
            hq_PlanPark_statues = statues;
            hq_PlanPark_s0 = s0;
        }
        if (PhLoop(10, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 3 + AreaM2(room) / 60 && Fit(1))) PhBreak();
                else { hq_PlanPark_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanPark_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (Ph(11))
        {
            GenRng r7 = SR(7);
            if (r7.Chance(700) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("pots"), GenData.A_OUT_EDGE, r7.Between(2, 3), WM_ANY, 0, 0, 0, ref r7)) Keep(first, false); }
        }
        if (Ph(12))
        {
            for (int k = 0; k < 1 + AreaM2(room) / 50; k++) BenchAtEdge(10 + k);
            // revision 10: shrubs along the fence, flower beds on the grass, the park's own bench and bin
            GenRng r8 = SR(8);
            hq_PlanPark_r8 = r8;
        }
        if (Ph(13))
        {
            GenRng r8 = hq_PlanPark_r8;
            if (Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("shrubs"), GenData.A_OUT_EDGE, r8.Between(2, 3), WM_ANY, 0, 0, 0, ref r8)) Keep(first, false); }
            GenRng r9 = SR(9);
            hq_PlanPark_r8 = r8;
            hq_PlanPark_r9 = r9;
        }
        if (PhLoop(14, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < 2 && Fit(1))) PhBreak();
                else { hq_PlanPark_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); PlanPark_L3(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (Ph(15))
        {
            GenRng r20 = SR(20);
            if (Fit(1)) { int first = nEmit; if (WallPlace(Dn("ck_bench"), WM_ANY, 0, 0, WIN_ANY, 0, ref r20) >= 0) Keep(first, false); }
            GenRng r21 = SR(21);
            hq_PlanPark_r21 = r21;
        }
        if (Ph(16))
        {
            GenRng r21 = hq_PlanPark_r21;
            if (r21.Chance(600) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ck_trash"), WM_ANY, 0, 0, WIN_ANY, 0, ref r21) >= 0) Keep(first, false); }
            // revision 10: the information kiosk by the way in from the lane, a clipped hedge along the fence, a picnic
            // spot (a table with its chairs and what was brought, from the slots)
            GenRng r22 = SR(22);
            hq_PlanPark_r21 = r21;
            hq_PlanPark_r22 = r22;
        }
        if (Ph(17))
        {
            GenRng r22 = hq_PlanPark_r22;
            if (r22.Chance(600) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ck_info"), WM_ANY, 0, 0, WIN_ANY, 0, ref r22) >= 0) Keep(first, false); }
            GenRng r25 = SR(25);
            hq_PlanPark_r22 = r22;
            hq_PlanPark_r25 = r25;
        }
        if (Ph(18))
        {
            GenRng r25 = hq_PlanPark_r25;
            if (r25.Chance(450) && Fit(1)) { int first = nEmit; if (WallPlace(Pick(ref r25, "ck_drinks", 5, "ck_food", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r25) >= 0) Keep(first, false); }
            // revision 11: a curved hedge in a corner, a dog out for a walk, a bench of the other kind, a vending machine
            GenRng r26 = SR(26);
            hq_PlanPark_r25 = r25;
            hq_PlanPark_r26 = r26;
        }
        if (Ph(19))
        {
            GenRng r26 = hq_PlanPark_r26;
            if (r26.Chance(600) && Fit(1)) { int first = nEmit; if (CornerPlace(Dn("f_hedgeCurve"), WM_ANY, 0, 0, false, 0, ref r26) >= 0) Keep(first, false); }
            GenRng r27 = SR(27);
            hq_PlanPark_sel2 = 0;
            if (r27.Chance(550) && Fit(1)) hq_PlanPark_sel2 = 1;
            hq_PlanPark_r26 = r26;
            hq_PlanPark_r27 = r27;
        }
        if (Ph(20))
        {
            if (hq_PlanPark_sel2 == 1)
            {
                GenRng r27 = hq_PlanPark_r27;
                int cell = OpenSpot(2, WM_ANY, 0, 0, GenData.T_PATH, ref r27);
                hq_PlanPark_r27 = r27;
                hq_PlanPark_cell = cell;
            }
        }
        if (Ph(21))
        {
            if (hq_PlanPark_sel2 == 1)
            {
                GenRng r27 = hq_PlanPark_r27;
                int cell = hq_PlanPark_cell;
                if (cell < 0) cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r27);
                int first = nEmit;
                hq_PlanPark_r27 = r27;
                hq_PlanPark_cell = cell;
                hq_PlanPark_first = first;
            }
        }
        if (Ph(22))
        {
            if (hq_PlanPark_sel2 == 1)
            {
                GenRng r27 = hq_PlanPark_r27;
                int cell = hq_PlanPark_cell;
                int first = hq_PlanPark_first;
                if (cell >= 0 && FreePlace(Dn("p_dog"), cell, r27.Range(360), 0, 0, H_FREE) >= 0) Keep(first, false);
                hq_PlanPark_r27 = r27;
                hq_PlanPark_cell = cell;
                hq_PlanPark_first = first;
            }
        }
        if (Ph(23))
        {
            GenRng r28 = SR(28);
            if (r28.Chance(400) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ma_vending"), WM_ANY, 0, 0, WIN_ANY, 0, ref r28) >= 0) Keep(first, false); }
            GenRng r23 = SR(23);
            hq_PlanPark_r23 = r23;
        }
        if (Ph(24))
        {
            GenRng r23 = hq_PlanPark_r23;
            if (r23.Chance(700) && Fit(2))
            {
                int first = nEmit;
                FSet(Dn("f_hedge"), Dn("f_hedge"));
                if (FormAt(GenData.GL_ROW, 0, GenData.A_OUT_EDGE, WM_ANY, 0, 0, 0, ref r23) >= 0) Keep(first, false);
            }
            GenRng r24 = SR(24);
            hq_PlanPark_sel3 = 0;
            if (Fit(2)) hq_PlanPark_sel3 = 1;
            hq_PlanPark_r23 = r23;
            hq_PlanPark_r24 = r24;
        }
        if (Ph(25))
        {
            if (hq_PlanPark_sel3 == 1)
            {
                int fx = hq_PlanPark_fx;
                int fz = hq_PlanPark_fz;
                GenRng r24 = hq_PlanPark_r24;
                int cell = OpenSpot(3, WM_FAR, fx, fz, 0, ref r24);
                int first = nEmit;
                hq_PlanPark_fx = fx;
                hq_PlanPark_fz = fz;
                hq_PlanPark_r24 = r24;
                hq_PlanPark_cell = cell;
                hq_PlanPark_first = first;
            }
        }
        if (Ph(26))
        {
            if (hq_PlanPark_sel3 == 1)
            {
                GenRng r24 = hq_PlanPark_r24;
                int cell = hq_PlanPark_cell;
                int first = hq_PlanPark_first;
                if (cell >= 0 && FreePlace(Dn(r24.Chance(500) ? "r_table" : "r_tableRound"), cell, r24.Range(2) * 90, 1, 0, H_FREE) >= 0) Keep(first, false);
                hq_PlanPark_r24 = r24;
                hq_PlanPark_cell = cell;
                hq_PlanPark_first = first;
            }
        }
        if (Ph(27))
        {
            ClearBans();
            Borders(30, 6, true);
        }
        // the pool a piece a step (Cor: build freezes)
        if (PhLoop(28, PoolLen(new string[] { "ck_bench", "k_benchCushion", "n_planter", "ck_trash", "f_lamp", "n_bushDetailed", "n_bushLarge", "f_hedge", "ck_flowers", "n_statueBlock",
            "n_obelisk", "n_column", "ck_info", "r_tableRound", "f_hedgeCurve", "hk_bench", "hk_lantern", "n_statueRing", "n_columnBroken" })) && !PoolStep(50, new string[] { "ck_bench", "k_benchCushion", "n_planter", "ck_trash", "f_lamp", "n_bushDetailed", "n_bushLarge", "f_hedge", "ck_flowers", "n_statueBlock",
            "n_obelisk", "n_column", "ck_info", "r_tableRound", "f_hedgeCurve", "hk_bench", "hk_lantern", "n_statueRing", "n_columnBroken" })) PhBreak();
}
    void PlanPark_L1()
    {
        int room = pRoom;
        if (Ph(0))
        {
            int fx = hq_PlanPark_fx;
            int fz = hq_PlanPark_fz;
            GenRng r3 = hq_PlanPark_r3;
            int cell = OpenSpot(1, WM_NEAR, fx, fz, GenData.T_PATH, ref r3);
            if (cell < 0) { hq_PlanPark_fx = fx; hq_PlanPark_fz = fz; hq_PlanPark_r3 = r3; phBreak = true; PhEnd(); return; }
            // the lamp beside the path, not on it
            int lx = cell % IW, lz = cell / IW, best = -1;
            for (int d = 0; d < 4 && best < 0; d++)
            {
                int x = lx + DX[d], z = lz + DZ[d];
                if (x >= 0 && z >= 0 && x < IW && z < IH && innerRoom[z * IW + x] == room && (tags[z * IW + x] & GenData.T_PATH) == 0 && occ[z * IW + x] == 0) best = z * IW + x;
            }
            int first = nEmit;
            hq_PlanPark_fx = fx;
            hq_PlanPark_fz = fz;
            hq_PlanPark_r3 = r3;
            hq_PlanPark_best = best;
            hq_PlanPark_first = first;
        }
        if (Ph(1))
        {
            int best = hq_PlanPark_best;
            int first = hq_PlanPark_first;
            if (best >= 0 && FreePlace(Dn("f_lamp"), best, 0, 0, 0, H_FREE) >= 0) Keep(first, false);
            hq_PlanPark_best = best;
            hq_PlanPark_first = first;
        }
    }

    void PlanPark_L2()
    {
        int room = pRoom;
        if (Ph(0))
        {
            int fx = hq_PlanPark_fx;
            int fz = hq_PlanPark_fz;
            GenRng r6 = hq_PlanPark_r6;
            int cell = OpenSpot(1, WM_FAR, fx, fz, 0, ref r6);
            if (cell < 0) { hq_PlanPark_fx = fx; hq_PlanPark_fz = fz; hq_PlanPark_r6 = r6; hq_PlanPark_cell = cell; phBreak = true; PhEnd(); return; }
            int first = nEmit, yaw = (int)(System.Math.Atan2(fx - (cell % IW * GenData.INNER_MM + 250), fz - (cell / IW * GenData.INNER_MM + 250)) * 180 / System.Math.PI);
            hq_PlanPark_fx = fx;
            hq_PlanPark_fz = fz;
            hq_PlanPark_r6 = r6;
            hq_PlanPark_cell = cell;
            hq_PlanPark_first = first;
            hq_PlanPark_yaw = yaw;
        }
        if (Ph(1))
        {
            string[] statues = hq_PlanPark_statues;
            int s0 = hq_PlanPark_s0;
            int k = hq_PlanPark_k;
            int cell = hq_PlanPark_cell;
            int first = hq_PlanPark_first;
            int yaw = hq_PlanPark_yaw;
            if (FreePlace(Dn(statues[(s0 + k) % statues.Length]), cell, (yaw + 360) % 360, 1, 0, H_FREE) >= 0) Keep(first, false);
            hq_PlanPark_statues = statues;
            hq_PlanPark_s0 = s0;
            hq_PlanPark_cell = cell;
            hq_PlanPark_first = first;
            hq_PlanPark_yaw = yaw;
        }
    }

    void PlanPark_L3()
    {
        int room = pRoom;
        if (Ph(0))
        {
            GenRng r9 = hq_PlanPark_r9;
            int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r9);
            int first = nEmit;
            hq_PlanPark_r9 = r9;
            hq_PlanPark_cell = cell;
            hq_PlanPark_first = first;
        }
        if (Ph(1))
        {
            GenRng r9 = hq_PlanPark_r9;
            int cell = hq_PlanPark_cell;
            int first = hq_PlanPark_first;
            if (cell >= 0 && FreePlace(Dn("ck_flowers"), cell, r9.Range(4) * 90, 0, 0, H_FREE) >= 0) Keep(first, false);
            hq_PlanPark_r9 = r9;
            hq_PlanPark_cell = cell;
            hq_PlanPark_first = first;
        }
    }


    // phase state of the split plans (Tools: autophase2)
    int hp_PlanStockroom_first;
    string[] hp_PlanStockroom_more;
    string[] hp_PlanStockroom_pool;
    GenRng hp_PlanStockroom_r15;
    int hp_PlanStockroom_p0;
    int hp_PlanParts_first;
    string[] hp_PlanParts_rows;
    int hp_PlanBarn_first;
    int hp_PlanFeedStore_first;
    string[] hp_PlanFeedStore_rows;
    string[] hp_PlanFeedStore_more;
    string[] hp_PlanField_crops;
    int hp_PlanField_c0;
    int hp_PlanField_placedRows;

    // locals of the split plans and helpers, kept from one phase to the next (autophase4)
    int hq_PlanBarn_cell;
    int hq_PlanBarn_dx;
    int hq_PlanBarn_dz;
    int hq_PlanBarn_k;
    string[] hq_PlanBarn_more;
    GenRng hq_PlanBarn_r;
    GenRng hq_PlanBarn_r10;
    GenRng hq_PlanBarn_r15;
    GenRng hq_PlanBarn_r17;
    GenRng hq_PlanBarn_r2;
    GenRng hq_PlanBarn_r3;
    string[] hq_PlanBarn_rows;
    int hq_PlanBarn_sel1;
    int hq_PlanBarn_sel2;
    int hq_PlanBarn_sel3;
    int hq_PlanBarn_sel4;
    int hq_PlanBarn_wb;
    int hq_PlanFarmyard_cell;
    int hq_PlanFarmyard_dx;
    int hq_PlanFarmyard_dz;
    int hq_PlanFarmyard_first;
    GenRng hq_PlanFarmyard_r;
    GenRng hq_PlanFarmyard_r2;
    GenRng hq_PlanFarmyard_r3;
    GenRng hq_PlanFarmyard_r4;
    GenRng hq_PlanFarmyard_r6;
    GenRng hq_PlanFarmyard_r7;
    GenRng hq_PlanFarmyard_r9;
    int hq_PlanFarmyard_sel1;
    int hq_PlanFarmyard_sel2;
    int hq_PlanFarmyard_tap;
    int hq_PlanFeedStore_cell;
    GenRng hq_PlanFeedStore_r12;
    int hq_PlanFeedStore_sel1;
    int hq_PlanField_first;
    GenRng hq_PlanField_r22;
    int hq_PlanField_sel1;
    int hq_PlanField_w;
    int hq_PlanForecourt_cars;
    int hq_PlanForecourt_dx;
    int hq_PlanForecourt_dz;
    int hq_PlanForecourt_first;
    bool hq_PlanForecourt_garage;
    int hq_PlanForecourt_ht1;
    GenRng hq_PlanForecourt_r2;
    GenRng hq_PlanForecourt_r4;
    GenRng hq_PlanForecourt_r5;
    int hq_PlanForecourt_room;
    GenRng hq_PlanForecourt_rp;
    int hq_PlanForecourt_sel1;
    int hq_PlanForecourt_sel2;
    int hq_PlanGarageBay_car;
    int[] hq_PlanGarageBay_chance;
    int hq_PlanGarageBay_cx;
    int hq_PlanGarageBay_cz;
    int hq_PlanGarageBay_dx;
    int hq_PlanGarageBay_dz;
    int hq_PlanGarageBay_first;
    string[] hq_PlanGarageBay_inRepair;
    GenRng hq_PlanGarageBay_r;
    GenRng hq_PlanGarageBay_r10;
    GenRng hq_PlanGarageBay_r2;
    GenRng hq_PlanGarageBay_r9;
    string[] hq_PlanGarageBay_rows;
    int hq_PlanGarageBay_sel1;
    int hq_PlanGarageBay_wb;
    int hq_PlanGarageBay_yaw;
    int hq_PlanPaddock_animals;
    int hq_PlanPaddock_cell;
    int hq_PlanPaddock_first;
    int hq_PlanPaddock_got;
    int hq_PlanPaddock_k;
    string hq_PlanPaddock_kind;
    string hq_PlanPaddock_kind2;
    GenRng hq_PlanPaddock_r;
    GenRng hq_PlanPaddock_r10;
    GenRng hq_PlanPaddock_r11;
    GenRng hq_PlanPaddock_r9;
    GenRng hq_PlanPaddock_rk;
    int hq_PlanPaddock_sel1;
    int hq_PlanPaddock_trees;
    int hq_PlanPark_best;
    int hq_PlanPark_cell;
    int hq_PlanPark_first;
    int hq_PlanPark_fx;
    int hq_PlanPark_fz;
    int hq_PlanPark_k;
    GenRng hq_PlanPark_r;
    GenRng hq_PlanPark_r21;
    GenRng hq_PlanPark_r22;
    GenRng hq_PlanPark_r23;
    GenRng hq_PlanPark_r24;
    GenRng hq_PlanPark_r25;
    GenRng hq_PlanPark_r26;
    GenRng hq_PlanPark_r27;
    GenRng hq_PlanPark_r3;
    GenRng hq_PlanPark_r4;
    GenRng hq_PlanPark_r6;
    GenRng hq_PlanPark_r8;
    GenRng hq_PlanPark_r9;
    int hq_PlanPark_s0;
    int hq_PlanPark_sel1;
    int hq_PlanPark_sel2;
    int hq_PlanPark_sel3;
    string[] hq_PlanPark_statues;
    int hq_PlanPark_yaw;
    bool hq_PlanParts_ht1;
    GenRng hq_PlanParts_r;
    int hq_PlanShopFloor_axisYaw;
    int hq_PlanShopFloor_cell;
    bool hq_PlanShopFloor_clothes;
    int hq_PlanShopFloor_def;
    int hq_PlanShopFloor_dx;
    int hq_PlanShopFloor_dz;
    int hq_PlanShopFloor_first;
    bool hq_PlanShopFloor_garden;
    int hq_PlanShopFloor_gondola;
    int hq_PlanShopFloor_k;
    int hq_PlanShopFloor_p0;
    string[] hq_PlanShopFloor_pool;
    GenRng hq_PlanShopFloor_r;
    GenRng hq_PlanShopFloor_r10;
    GenRng hq_PlanShopFloor_r11;
    GenRng hq_PlanShopFloor_r12;
    GenRng hq_PlanShopFloor_r14;
    GenRng hq_PlanShopFloor_r15;
    GenRng hq_PlanShopFloor_r3;
    GenRng hq_PlanShopFloor_r4;
    GenRng hq_PlanShopFloor_r5;
    GenRng hq_PlanShopFloor_r6;
    GenRng hq_PlanShopFloor_r8;
    GenRng hq_PlanShopFloor_r9b;
    string[] hq_PlanShopFloor_rows;
    int hq_PlanShopFloor_sel1;
    int hq_PlanShopFloor_sel2;
    string[] hq_PlanShopFloor_side;
    int hq_PlanShopFloor_t;
    int hq_PlanShopFloor_till;
    bool hq_PlanShopFloor_toys;
    int hq_PlanStockroom_cell;
    int hq_PlanStockroom_dx;
    int hq_PlanStockroom_dz;
    int hq_PlanStockroom_ext;
    bool hq_PlanStockroom_ht1;
    GenRng hq_PlanStockroom_r;
    GenRng hq_PlanStockroom_r2;
    string[] hq_PlanStockroom_rows;
    int hq_PlanStockroom_sel1;
    int hq_RackRun_a;
    int hq_RackRun_b;
    int hq_RackRun_m0;
    int hq_RackRun_mode;
    int hq_RackRun_n;
    bool hq_RackRun_ret;
    GenRng hq_RackRun_rng;
    int hq_RackRun_tx;
    int hq_RackRun_tz;
}
