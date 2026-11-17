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
        int dx, dz;
        MainDoor(room, out dx, out dz);
        GenRng r = SR(1);
        // what the shop sells: groceries, clothes, or plants (a garden centre)
        int theme = r.Range(1000);
        bool clothes = theme < 200, garden = theme >= 200 && theme < 400;
        int first = nEmit;
        int till = WallPlace(Dn("ms_checkout"), WM_NEAR, dx, dz, WIN_ANY, EF_MANDATORY, ref r);
        if (till < 0 || !Keep(first, true))
        {
            first = nEmit;
            till = CentrePiece(Dn("ms_checkout"), 0, true, 1, 0, WM_NEAR, dx, dz, 0, EF_MANDATORY, ref r);
        }
        if (till < 0) MandFail(1);
        else if (Fit(1))
        {
            GenRng rb = SR(2);
            first = nEmit;
            if (GroupBeside(GenData.FindGrp("baskets"), till, rb.Between(2, 3), 0, ref rb)) Keep(first, false);
        }
        if (garden)
        {
            // a garden centre: flower stands and pots in rows, plants at the walls, compost sacks, watering cans
            GenRng r3 = SR(3);
            for (int k = 0; k < 1 + cells && Fit(1); k++)
            {
                int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r3);
                if (cell < 0) break;
                first = nEmit;
                if (FreePlace(Dn("ms_dispFlowers"), cell, r3.Range(4) * 90, 1, 0, H_FREE) >= 0) Keep(first, false);
            }
            string[] rows = { "pots", "watering", "sacks" };
            for (int k = 0; k < rows.Length; k++)
            {
                GenRng rk = SR(4 + k);
                if (!rk.Chance(k == 0 ? 900 : 600) || !Fit(2)) continue;
                first = nEmit;
                if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(first, false);
            }
            GenRng r7 = SR(7);
            for (int k = 0; k < 2 && Fit(1); k++) WallAny(WM_ANY, 0, 0, WIN_PREFER, 0, ref r7, "k_plant", "n_planter", "k_plant");
        }
        else if (!clothes)
        {
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
            if (r4.Chance(600) && Fit(1)) { first = nEmit; if (WallPlace(Dn("ms_freezer"), WM_ANY, 0, 0, WIN_ANY, 0, ref r4) >= 0) Keep(first, false); }
            // shelves at the walls, then the gondola rows
            GenRng r5 = SR(5);
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
            int gondola = Pick(ref r6, "ms_shelfBoxes", 5, "ms_shelfBags", 4), axisYaw = RoomLongAxis(room) == 0 ? 0 : 90;
            for (int k = 0; k < 1 + cells && Fit(1); k++)
            {
                int cell = OpenSpot(3, WM_ANY, 0, 0, 0, ref r6);
                if (cell < 0) break;
                first = nEmit;
                if (FreePlace(gondola, cell, axisYaw, 1, 0, H_FREE) >= 0) Keep(first, false);
            }
            // fresh things near the door
            GenRng r7 = SR(7);
            if (Fit(1)) { first = nEmit; if (WallPlace(Dn("ms_dispBread"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r7) >= 0) Keep(first, false); }
            GenRng r8 = SR(8);
            if (r8.Chance(700) && Fit(1))
            {
                int cell = OpenSpot(2, WM_NEAR, dx, dz, 0, ref r8);
                first = nEmit;
                if (cell >= 0 && FreePlace(Pick(ref r8, "ms_dispFruit", 6, "ms_dispCans", 3), cell, r8.Range(4) * 90, 1, 0, H_FREE) >= 0) Keep(first, false);
            }
        }
        else
        {
            GenRng r3 = SR(3);
            for (int k = 0; k < 2 + cells && Fit(1); k++)
            {
                int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r3);
                if (cell < 0) break;
                first = nEmit;
                if (FreePlace(Pick(ref r3, "ms_clothesRack", 6, "ms_clothesTable", 3), cell, r3.Range(2) * 90, 1, 0, H_FREE) >= 0) Keep(first, false);
            }
            GenRng r4 = SR(4);
            for (int k = 0; k < 2 && Fit(1); k++) { first = nEmit; if (WallPlace(Dn("ms_mannequin"), WM_ANY, 0, 0, WIN_PREFER, 0, ref r4) >= 0) Keep(first, false); }
            GenRng r5 = SR(5);
            if (Fit(2)) { first = nEmit; rowN = 2; rowDef[0] = rowDef[1] = Dn("ms_shelfEnd"); if (WallRow(0, 2, WM_FAR, dx, dz, WIN_AVOID, 0, ref r5) >= 0) Keep(first, false); }
        }
        // by the door: trolleys at the wall, a machine, a plant and the bin
        GenRng r9 = SR(9);
        if (!clothes && r9.Chance(650) && Fit(2))
        {
            first = nEmit;
            FSet(Dn("ms_cart"), Dn("ms_cart"));
            if (FormAt(GenData.GL_ROW, 60, GenData.A_WALL, WM_NEAR, dx, dz, 0, ref r9) >= 0) Keep(first, false);
        }
        GenRng r10 = SR(10);
        if (r10.Chance(450) && Fit(1)) { first = nEmit; if (WallPlace(Pick(ref r10, "ms_gacha", 5, "ms_bottleReturn", 4), WM_NEAR, dx, dz, WIN_ANY, 0, ref r10) >= 0) Keep(first, false); }
        GenRng r11 = SR(11);
        if (r11.Chance(500) && Fit(1)) WallAny(WM_NEAR, dx, dz, WIN_ANY, 0, ref r11, "k_plant", "k_trashcan", "ms_dispSmall");
    }

    // ============================================================================================ STOCK ROOM
    // Racks of stock at the walls, a pallet of deliveries inside the service door, boxes and crates, the hand truck.
    void PlanStockroom()
    {
        int room = pRoom;
        int dx, dz;
        MainDoor(room, out dx, out dz);
        GenRng r = SR(1);
        if (!RackRun(2, Dn("gen_rack"), Dn(r.Chance(500) ? "gen_rackPantry" : "gen_rack"), WM_FAR, dx, dz, ref r)) MandFail(1);
        int first;
        int ext = ExtDoor(room);
        GenRng r2 = SR(2);
        if (Fit(1))
        {
            int tx = ext >= 0 ? DoorX(room, ext) : dx, tz = ext >= 0 ? DoorZ(room, ext) : dz;
            int cell = OpenSpot(2, WM_NEAR, tx, tz, 0, ref r2);
            if (cell >= 0) PalletBlock(cell, 1, 1, r2.Range(2) * 90, ref r2);
        }
        string[] rows = { "boxes", "crates", "cardboard", "sacks" };
        for (int k = 0; k < rows.Length; k++)
        {
            GenRng rk = SR(3 + k);
            if (!rk.Chance(k < 2 ? 800 : 450) || !Fit(2)) continue;
            first = nEmit;
            if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(first, false);
        }
        GenRng r8 = SR(8);
        if (r8.Chance(600) && Fit(1)) { first = nEmit; if (WallPlace(Pick(ref r8, "ms_handTruck", 5, "ms_cart", 3), WM_NEAR, dx, dz, WIN_ANY, 0, ref r8) >= 0) Keep(first, false); }
        GenRng r9 = SR(9);
        if (r9.Chance(350) && Fit(1)) { first = nEmit; if (WallPlace(Pick(ref r9, "ms_freezer", 5, "ms_dispCans", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r9) >= 0) Keep(first, false); }
    }

    // ============================================================================================ WORKSHOP BAY
    // A car in for repair, its nose to the roller door; the workbench and its tools at a side wall, tyres stacked
    // against the walls, parts taken off the car, oil drums, a rack of spares.
    void PlanGarageBay()
    {
        int room = pRoom, cells = roomOuterCells[room];
        int dx, dz;
        MainDoor(room, out dx, out dz);
        GenRng r = SR(1);
        int front = SideToward(room, GenData.R_FORECOURT);
        int yaw = front >= 0 ? DirYawSite(front) : 0;
        string[] inRepair = { "c_sedan", "c_suv", "c_hatch", "c_sports", "c_luxury", "c_taxi" };
        int car = CentrePiece(Dn(inRepair[r.Range(inRepair.Length)]), yaw, false, 1, 0, WM_CENTRE, RoomMidX(room), RoomMidZ(room), 0, EF_MANDATORY, ref r);
        if (car < 0) car = CentrePiece(Dn("c_hatch"), yaw + 90, false, 0, 0, WM_ANY, 0, 0, 0, EF_MANDATORY, ref r);
        if (car < 0) MandFail(1);
        int cx = car >= 0 ? emitX[car] : RoomMidX(room), cz = car >= 0 ? emitZ[car] : RoomMidZ(room);
        GenRng r2 = SR(2);
        if (Fit(1))
        {
            int first = nEmit;
            int wb = WallPlace(Dn("s_workbench"), WM_ANY, 0, 0, WIN_ANY, 0, ref r2);
            if (wb >= 0 && Keep(first, false) && Fit(2))
            {
                first = nEmit;
                if (GroupBeside(GenData.FindGrp("tools"), wb, r2.Between(2, 3), 0, ref r2)) Keep(first, false);
            }
        }
        string[] rows = { "tyres", "scrap", "fuel", "barrels" };
        int[] chance = { 900, 750, 550, 350 };
        for (int k = 0; k < rows.Length; k++)
        {
            GenRng rk = SR(3 + k);
            if (!rk.Chance(chance[k]) || !Fit(2)) continue;
            int first = nEmit;
            if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, rk.Between(2, k == 0 ? 4 : 3), 0, ref rk)) Keep(first, false);
        }
        GenRng r8 = SR(8);
        if (cells >= 2 && r8.Chance(600) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("gen_rack"), WM_FAR, cx, cz, WIN_NEVER, 0, ref r8) >= 0) Keep(first, false); }
        GenRng r9 = SR(9);
        if (r9.Chance(500) && LooseFit(1) && car >= 0)
        {
            int first = nEmit;
            if (RelBeside(Dn("c_cone"), car, r9.Chance(500) ? 1 : -1, 250, 0, ref r9) >= 0 && Keep(first, false)) MarkLoose(first);
        }
        GenRng r10 = SR(10);
        if (r10.Chance(400) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ms_handTruck"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r10) >= 0) Keep(first, false); }
    }

    // a run of up to n racks along a wall (a, then b, a, ...), fewer if no wall is that long: kept, or false
    bool RackRun(int n, int a, int b, int mode, int tx, int tz, ref GenRng rng)
    {
        for (int m = n; m >= 1; m--)
        {
            int first = nEmit;
            rowN = m;
            for (int i = 0; i < m; i++) rowDef[i] = (i & 1) == 0 ? a : b;
            if (WallRow(0, 2, mode, tx, tz, WIN_NEVER, EF_MANDATORY, ref rng) >= 0 && Keep(first, true)) return true;
        }
        return false;
    }

    // yaw (degrees) whose front looks along outer direction d (0 north +z, 1 east +x, 2 south, 3 west)
    static int DirYawSite(int d) { return d == 0 ? 0 : (d == 1 ? 90 : (d == 2 ? 180 : 270)); }

    // ============================================================================================ PARTS ROOM
    void PlanParts()
    {
        int room = pRoom, cells = roomOuterCells[room];
        GenRng r = SR(1);
        if (!RackRun(cells >= 2 ? 3 : 2, Dn("gen_rack"), Dn("gen_rack"), WM_CENTRE, 0, 0, ref r)) MandFail(1);
        int first;
        GenRng r2 = SR(2);
        if (cells >= 2) RackRows(room, Dn("gen_rack"), cells, ref r2);
        string[] rows = { "tyres", "scrap", "boxes", "fuel" };
        for (int k = 0; k < rows.Length; k++)
        {
            GenRng rk = SR(3 + k);
            if (!rk.Chance(k < 2 ? 850 : 500) || !Fit(2)) continue;
            first = nEmit;
            if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(first, false);
        }
        GenRng r7 = SR(7);
        if (r7.Chance(450) && Fit(1)) { first = nEmit; if (WallPlace(Pick(ref r7, "s_workbench", 4, "s_boxLarge", 4, "w_parts", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r7) >= 0) Keep(first, false); }
    }

    // ============================================================================================ BARN
    // Hay stacked at the walls, the animals in the back half, the cart or the tractor inside the big door, the
    // wheelbarrow, feed sacks, buckets and tools.
    void PlanBarn()
    {
        int room = pRoom, cells = roomOuterCells[room];
        int dx, dz;
        MainDoor(room, out dx, out dz);
        GenRng r = SR(1);
        int first = nEmit;
        if (PlaceGroup(GenData.FindGrp("hay"), room, GenData.A_WALL, Fit(4) ? r.Between(3, 4) : 2, EF_MANDATORY, ref r)) Keep(first, true); else MandFail(1);
        GenRng r2 = SR(2);
        if (Fit(1))
        {
            int cell = OpenSpot(3, WM_NEAR, dx, dz, 0, ref r2);
            first = nEmit;
            if (cell >= 0 && FreePlace(Pick(ref r2, "f_cart", 5, "c_tractor", cells >= 3 ? 4 : 0, "f_cartHigh", 3), cell, r2.Range(4) * 90, 0, 0, H_FREE) >= 0) Keep(first, false);
        }
        GenRng r3 = SR(3);
        for (int k = 0; k < (cells >= 3 ? 2 : 1) && Fit(1); k++)
        {
            int cell = OpenSpot(2, WM_FAR, dx, dz, 0, ref r3);
            first = nEmit;
            if (cell >= 0 && FreePlace(Pick(ref r3, "p_cow", 5, "p_pig", 4), cell, r3.Range(360), 0, 0, H_FREE) >= 0) Keep(first, false);
        }
        GenRng r4 = SR(4);
        if (r4.Chance(500) && Fit(2)) { first = nEmit; if (PlaceGroup(GenData.FindGrp("hens"), room, GenData.A_OUT_OPEN, 2, 0, ref r4)) Keep(first, false); }
        string[] rows = { "sacks", "buckets", "tools", "hay" };
        for (int k = 0; k < rows.Length; k++)
        {
            GenRng rk = SR(5 + k);
            if (!rk.Chance(k < 2 ? 700 : 450) || !Fit(2)) continue;
            first = nEmit;
            if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(first, false);
        }
        GenRng r9 = SR(9);
        if (r9.Chance(600) && Fit(1)) { first = nEmit; if (WallPlace(Dn("m_wheelbarrow"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r9) >= 0) Keep(first, false); }
        // the bench where things get mended, a rack of feed and fuel, produce in crates, timber, the farm cat
        GenRng r10 = SR(10);
        if (cells >= 2 && r10.Chance(650) && Fit(1))
        {
            first = nEmit;
            int wb = WallPlace(Dn("s_workbench"), WM_ANY, 0, 0, WIN_ANY, 0, ref r10);
            if (wb >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp("tools"), wb, 2, 0, ref r10)) Keep(first, false); }
        }
        GenRng r11 = SR(11);
        if (r11.Chance(600) && Fit(1)) { first = nEmit; if (WallPlace(Dn("gen_rack"), WM_FAR, dx, dz, WIN_NEVER, 0, ref r11) >= 0) Keep(first, false); }
        string[] more = { "crates", "fuel" };
        for (int k = 0; k < more.Length; k++)
        {
            GenRng rk = SR(12 + k);
            if (!rk.Chance(k == 0 ? 650 : 400) || !Fit(2)) continue;
            first = nEmit;
            if (PlaceGroup(GenData.FindGrp(more[k]), room, GenData.A_WALL, 2, 0, ref rk)) Keep(first, false);
        }
        GenRng r14 = SR(14);
        if (r14.Chance(500) && Fit(1)) { first = nEmit; if (WallPlace(Pick(ref r14, "w_planks", 4, "w_logs", 4, "w_pallet", 3), WM_ANY, 0, 0, WIN_ANY, 0, ref r14) >= 0) Keep(first, false); }
        GenRng r15 = SR(15);
        if (r15.Chance(450) && Fit(1))
        {
            int cell = OpenSpot(1, WM_ANY, 0, 0, 0, ref r15);
            first = nEmit;
            if (cell >= 0 && FreePlace(Dn("p_cat"), cell, r15.Range(360), 0, 0, H_FREE) >= 0) Keep(first, false);
        }
    }

    // ============================================================================================ FEED STORE
    void PlanFeedStore()
    {
        int room = pRoom;
        GenRng r = SR(1);
        int first = nEmit;
        if (PlaceGroup(GenData.FindGrp("sacks"), room, GenData.A_WALL, 3, EF_MANDATORY, ref r)) Keep(first, true); else MandFail(1);
        string[] rows = { "hay", "barrels", "buckets", "crates" };
        for (int k = 0; k < rows.Length; k++)
        {
            GenRng rk = SR(2 + k);
            if (!rk.Chance(k < 2 ? 750 : 450) || !Fit(2)) continue;
            first = nEmit;
            if (PlaceGroup(GenData.FindGrp(rows[k]), room, GenData.A_WALL, rk.Between(2, 3), 0, ref rk)) Keep(first, false);
        }
        GenRng r6 = SR(6);
        if (r6.Chance(500) && Fit(1)) { first = nEmit; if (WallPlace(Pick(ref r6, "gen_rack", 4, "w_pallet", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r6) >= 0) Keep(first, false); }
        GenRng r7 = SR(7);
        if (r7.Chance(400) && Fit(2)) { first = nEmit; if (PlaceGroup(GenData.FindGrp("fuel"), room, GenData.A_WALL, 2, 0, ref r7)) Keep(first, false); }
    }

    // ============================================================================================ FORECOURT
    // In front of a shop: the sign by the door, a bench and a bin, flowers and fruit set out by the door, a trolley
    // bay. In front of a garage: cars waiting at the fence, nose out, tyres against the wall, cones, oil drums.
    void PlanForecourt()
    {
        int room = pRoom, dx, dz;
        FrontMarkers(30);
        MainDoor(room, out dx, out dz);
        bool garage = DoorZoneAny(room) == GenData.Z_GARAGE;
        if (garage)
        {
            int cars = roomOuterCells[room] >= 3 ? 2 : 1;
            for (int k = 0; k < cars && Fit(1); k++)
            {
                GenRng rk = SR(1 + k);
                int first = nEmit;
                string[] waiting = { "c_sedan", "c_van", "c_truck", "c_taxi", "c_suv", "c_sports", "c_luxury", "c_police", "c_flatbed" };
                int car = Dn(waiting[rk.Range(waiting.Length)]);
                if (WallPlace(car, WM_FAR, dx, dz, WIN_ANY, 0, ref rk) >= 0) Keep(first, false);
            }
            GenRng r3 = SR(3);
            if (Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("tyres"), GenData.A_BUILDING, r3.Between(2, 4), WM_NEAR, dx, dz, 0, ref r3)) Keep(first, false); }
            GenRng r4 = SR(4);
            if (r4.Chance(700) && Fit(2))
            {
                int first = nEmit;
                FSet(Dn("c_cone"), Dn("c_cone"));
                if (FormAt(GenData.GL_ROW, 300, GenData.A_OUT_OPEN, WM_NEAR, dx, dz, 0, ref r4) >= 0) Keep(first, false);
            }
            GenRng r5 = SR(5);
            if (r5.Chance(450) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("fuel"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r5)) Keep(first, false); }
        }
        else
        {
            GenRng r1 = SR(1);
            if (Fit(1)) { int first = nEmit; if (WallPlace(Dn("ms_dispFlowers"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r1) >= 0) Keep(first, false); }
            GenRng r2 = SR(2);
            if (r2.Chance(500) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ms_dispFruit"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r2) >= 0) Keep(first, false); }
            BenchAtEdge(3);
            GenRng r4 = SR(4);
            if (r4.Chance(700) && Fit(2))
            {
                int first = nEmit;
                FSet(Dn("ms_cart"), Dn("ms_cart"), Dn("ms_cart"));
                if (FormAt(GenData.GL_ROW, 60, GenData.A_OUT_EDGE, WM_ANY, 0, 0, 0, ref r4) >= 0) Keep(first, false);
            }
            GenRng r5 = SR(5);
            if (r5.Chance(400) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("ms_bottleReturn"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r5) >= 0) Keep(first, false); }
        }
        GenRng r6 = SR(6);
        if (r6.Chance(650) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("k_trashcan"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r6) >= 0) Keep(first, false); }
    }

    // ============================================================================================ FARMYARD
    // Trodden earth in front of the barn: the cart or the tractor parked at the fence, hay against the barn, hens
    // in the open, the wheelbarrow, buckets at the water tap, the wood pile.
    void PlanFarmyard()
    {
        int room = pRoom, dx, dz;
        FrontMarkers(30);
        MainDoor(room, out dx, out dz);
        GenRng r = SR(1);
        if (Fit(1)) { int first = nEmit; if (WallPlace(Pick(ref r, "c_tractor", 5, "f_cart", 4, "f_cartHigh", 3), WM_FAR, dx, dz, WIN_ANY, 0, ref r) >= 0) Keep(first, false); }
        GenRng r2 = SR(2);
        if (Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("hay"), GenData.A_BUILDING, r2.Between(2, 3), WM_ANY, 0, 0, 0, ref r2)) Keep(first, false); }
        GenRng r3 = SR(3);
        if (r3.Chance(750) && Fit(2)) { int first = nEmit; if (PlaceGroup(GenData.FindGrp("hens"), room, GenData.A_OUT_OPEN, r3.Between(2, Fit(4) ? 4 : 2), 0, ref r3)) Keep(first, false); }
        GenRng r4 = SR(4);
        if (r4.Chance(600) && Fit(2))
        {
            int first = nEmit;
            int tap = WallPlace(Dn("gen_tap"), WM_ANY, 0, 0, WIN_ANY, 0, ref r4);
            if (tap >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp("buckets"), tap, 2, 0, ref r4)) Keep(first, false); }
        }
        GenRng r5 = SR(5);
        if (r5.Chance(550) && Fit(1)) { int first = nEmit; if (WallPlace(Pick(ref r5, "m_wheelbarrow", 5, "n_logStack", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r5) >= 0) Keep(first, false); }
        GenRng r6 = SR(6);
        if (r6.Chance(400) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("sacks"), GenData.A_BUILDING, 2, WM_NEAR, dx, dz, 0, ref r6)) Keep(first, false); }
        // the farm stand at the lane: crates of produce and a bench; the farm dog; the wood pile
        GenRng r7 = SR(7);
        if (r7.Chance(650) && Fit(3))
        {
            int first = nEmit;
            if (GroupAt(GenData.FindGrp("crates"), GenData.A_OUT_EDGE, 3, WM_FAR, dx, dz, 0, ref r7) && Keep(first, false) && Fit(1)) BenchAtEdge(8);
        }
        GenRng r9 = SR(9);
        if (r9.Chance(600) && Fit(1))
        {
            int cell = OpenSpot(2, WM_NEAR, dx, dz, 0, ref r9);
            int first = nEmit;
            if (cell >= 0 && FreePlace(Dn("p_dog"), cell, r9.Range(360), 0, 0, H_FREE) >= 0) Keep(first, false);
        }
        GenRng r10 = SR(10);
        if (r10.Chance(450) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("logpile"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r10)) Keep(first, false); }
    }

    // ============================================================================================ FIELD
    // Ploughed ground in rows of one crop each, the rows along the field's long side; the wheelbarrow and the
    // tools at the fence, a bale at the gate.
    void PlanField()
    {
        int room = pRoom;
        TagRoom(room, GenData.T_SOFT_GROUND);
        GenRng r = SR(1);
        string[] crops = { "rowCorn", "rowWheat", "rowLettuce", "rowTurnip", "vegCarrot", "vegPumpkin", "vegMelon" };
        int c0 = r.Range(crops.Length), placedRows = 0;
        for (int k = 0; k < 14 && Fit(3); k++)
        {
            GenRng rk = SR(2 + k);
            int g = GenData.FindGrp(crops[(c0 + k / 2) % crops.Length]);
            int first = nEmit;
            if (PlaceGroup(g, room, GenData.A_SOFT, rk.Between(3, Fit(5) ? 5 : 3), 0, ref rk) && Keep(first, false)) placedRows++;
        }
        if (placedRows == 0) MandFail(1);
        GenRng r20 = SR(20);
        if (r20.Chance(600) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("m_wheelbarrow"), WM_ANY, 0, 0, WIN_ANY, 0, ref r20) >= 0) Keep(first, false); }
        GenRng r21 = SR(21);
        if (r21.Chance(500) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("tools"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r21)) Keep(first, false); }
        // water for the crops: a barrel or the tap with its buckets at the fence; a bale left at the edge
        GenRng r22 = SR(22);
        if (r22.Chance(650) && Fit(2))
        {
            int first = nEmit;
            int w = WallPlace(Pick(ref r22, "gen_tap", 4, "s_barrel", 4), WM_ANY, 0, 0, WIN_ANY, 0, ref r22);
            if (w >= 0 && Keep(first, false) && Fit(2)) { first = nEmit; if (GroupBeside(GenData.FindGrp(r22.Chance(500) ? "buckets" : "watering"), w, 2, 0, ref r22)) Keep(first, false); }
        }
        GenRng r23 = SR(23);
        if (r23.Chance(450) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("hay"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r23)) Keep(first, false); }
    }

    // ============================================================================================ PADDOCK
    // Grass beside the barn with a few animals grazing, hay put out for them at the fence, the water bucket, a
    // shade tree.
    void PlanPaddock()
    {
        int room = pRoom;
        GenRng r = SR(1);
        int animals = 1 + AreaM2(room) / 24, got = 0;
        string kind = r.Chance(550) ? "p_cow" : "p_pig";
        for (int k = 0; k < animals && Fit(1); k++)
        {
            GenRng rk = SR(2 + k);
            int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref rk);
            int first = nEmit;
            if (cell >= 0 && FreePlace(Dn(kind), cell, rk.Range(360), 0, 0, H_FREE) >= 0 && Keep(first, false)) got++;
        }
        if (got == 0) MandFail(1);
        GenRng r8 = SR(8);
        if (Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("hay"), GenData.A_OUT_EDGE, 2, WM_ANY, 0, 0, 0, ref r8)) Keep(first, false); }
        GenRng r9 = SR(9);
        if (r9.Chance(650) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("m_bucketWater"), WM_ANY, 0, 0, WIN_ANY, 0, ref r9) >= 0) Keep(first, false); }
        GenRng r10 = SR(10);
        if (r10.Chance(450) && Fit(2)) { int first = nEmit; if (PlaceGroup(GenData.FindGrp("hens"), room, GenData.A_OUT_OPEN, 2, 0, ref r10)) Keep(first, false); }
        GenRng r11 = SR(11);
        if (r11.Chance(450) && Fit(1))
        {
            int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r11);
            int first = nEmit;
            if (cell >= 0 && FreePlace(Dn("p_dog"), cell, r11.Range(360), 0, 0, H_FREE) >= 0) Keep(first, false);
        }
        GenRng r12 = SR(12);
        if (r12.Chance(400) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("n_logStack"), WM_ANY, 0, 0, WIN_ANY, 0, ref r12) >= 0) Keep(first, false); }
        int trees = TreeCount(room, ref r);
        for (int t = 0; t < trees; t++) LawnTree(60 + t);
    }

    // ============================================================================================ PARK
    // A small public garden off the lane: a fountain in the middle of a big one, gravel paths from it to the
    // lane and across, benches facing the paths, lamp posts at the paths, trees, flower borders, bins, a
    // picnic table on the grass.
    void PlanPark()
    {
        int room = pRoom;
        GenRng r = SR(1);
        int mx = RoomMidX(room), mz = RoomMidZ(room);
        int fountain = -1;
        if (AreaM2(room) >= 60 && Fit(1)) fountain = CentrePiece(Dn("f_fountain"), 0, false, 2, 1, WM_NEAR, mx, mz, 0, 0, ref r);
        int fx = fountain >= 0 ? emitX[fountain] : mx, fz = fountain >= 0 ? emitZ[fountain] : mz;
        // the gravel paths: from the middle straight out to each side of the park, 1 m wide
        int cx = FloorDiv(fx, GenData.INNER_MM), cz = FloorDiv(fz, GenData.INNER_MM);
        for (int d = 0; d < 4; d++)
            for (int s = 0; s < 40; s++)
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
        RoomWalkable(room);
        for (int k = 0; k < 2 + AreaM2(room) / 40; k++) BenchAtEdge(10 + k);
        GenRng r3 = SR(3);
        for (int k = 0; k < 1 + AreaM2(room) / 50 && Fit(1); k++)
        {
            int cell = OpenSpot(1, WM_NEAR, fx, fz, GenData.T_PATH, ref r3);
            if (cell < 0) break;
            // the lamp beside the path, not on it
            int lx = cell % IW, lz = cell / IW, best = -1;
            for (int d = 0; d < 4 && best < 0; d++)
            {
                int x = lx + DX[d], z = lz + DZ[d];
                if (x >= 0 && z >= 0 && x < IW && z < IH && innerRoom[z * IW + x] == room && (tags[z * IW + x] & GenData.T_PATH) == 0 && occ[z * IW + x] == 0) best = z * IW + x;
            }
            int first = nEmit;
            if (best >= 0 && FreePlace(Dn("f_lamp"), best, 0, 0, 0, H_FREE) >= 0) Keep(first, false);
        }
        int trees = TreeCount(room, ref r);
        for (int t = 0; t < trees; t++) LawnTree(60 + t);
        GenRng r4 = SR(4);
        if (r4.Chance(500) && Fit(1))
        {
            int cell = OpenSpot(3, WM_FAR, fx, fz, 0, ref r4);
            int first = nEmit;
            if (cell >= 0 && FreePlace(Dn("r_tableRound"), cell, 0, 1, 0, H_FREE) >= 0) Keep(first, false);
        }
        GenRng r5 = SR(5);
        if (r5.Chance(700) && Fit(1)) { int first = nEmit; if (WallPlace(Dn("k_trashcan"), WM_NEAR, fx, fz, WIN_ANY, 0, ref r5) >= 0) Keep(first, false); }
        // statues on the grass beside the paths, turned to them; planters and pots at the fence
        GenRng r6 = SR(6);
        string[] statues = { "n_statueHead", "n_obelisk", "n_column", "n_statueRing" };
        int s0 = r6.Range(statues.Length);
        for (int k = 0; k < 1 + AreaM2(room) / 60 && Fit(1); k++)
        {
            int cell = OpenSpot(2, WM_FAR, fx, fz, 0, ref r6);
            if (cell < 0) break;
            int first = nEmit, yaw = (int)(System.Math.Atan2(fx - (cell % IW * GenData.INNER_MM + 250), fz - (cell / IW * GenData.INNER_MM + 250)) * 180 / System.Math.PI);
            if (FreePlace(Dn(statues[(s0 + k) % statues.Length]), cell, (yaw + 360) % 360, 1, 0, H_FREE) >= 0) Keep(first, false);
        }
        GenRng r7 = SR(7);
        if (r7.Chance(700) && Fit(2)) { int first = nEmit; if (GroupAt(GenData.FindGrp("pots"), GenData.A_OUT_EDGE, r7.Between(2, 3), WM_ANY, 0, 0, 0, ref r7)) Keep(first, false); }
        ClearBans();
        Borders(30, 6, true);
    }
}
