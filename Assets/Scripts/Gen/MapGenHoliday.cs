// MapGen, revision 12: seasonal decorations (research 18, pick 6; G58). One map in five has a holiday (MapGen.holiday,
// drawn with the seed): winter or Halloween. After a room's own plan, a few of the holiday's things are added on top of
// its budget: in winter the decorated tree in the living room's corner with presents round it, presents by a child's
// bed, the nutcracker in the hall, snowmen, a reindeer and candy canes outdoors; at Halloween carved pumpkins and
// jack-o'-lanterns by the doors, candle lanterns, a joke gravestone on a front lawn. The small things (a wreath, the
// gingerbread house, hot chocolate; skulls, candles, small pumpkins) come through the slots, whose theme takes the
// holiday on such a map. None of it is ever a wrong-category spawn.
public partial class MapGen
{
    void HolidayDecor()
    {
        int room = pRoom, t = roomType[room];
        bool building = GenData.IsBuilding(roomZone[room]);
        int saved = roomFloorTarget[room];
        roomFloorTarget[room] = roomFloor[room] + 3;   // up to three things on top of what the room holds
        GenRng r = SR(70);
        int dx, dz;
        MainDoor(room, out dx, out dz);
        if (holiday == 1)
        {
            if (t == GenData.R_LIVING || t == GenData.R_CABIN_MAIN || t == GenData.R_ZOO_PLAZA || t == GenData.R_SHOP_FLOOR)
            {
                int first = nEmit;
                int tree = building ? CornerPlace(Dn("hb_xmasTree"), WM_FAR, dx, dz, false, 0, ref r) : -1;
                if (tree < 0 && building) tree = WallPlace(Dn("hb_xmasTree"), WM_ANY, 0, 0, WIN_ANY, 0, ref r);
                if (tree >= 0 && Keep(first, false)) { first = nEmit; if (GroupBeside(GenData.FindGrp("presents"), tree, r.Between(2, 3), 0, ref r)) Keep(first, false); }
            }
            else if (t == GenData.R_KIDS || t == GenData.R_PLAYROOM || t == GenData.R_TEEN || t == GenData.R_BEDROOM)
            {
                if (r.Chance(500)) { int first = nEmit; if (PlaceGroup(GenData.FindGrp("presents"), room, GenData.A_WALL, 2, 0, ref r)) Keep(first, false); }
            }
            else if (t == GenData.R_HALL || t == GenData.R_PORCH)
            {
                int first = nEmit;
                if (WallPlace(Dn("hk_nutcracker"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r) >= 0) Keep(first, false);
            }
            else if (t == GenData.R_LAWN_OPEN || t == GenData.R_PARK || t == GenData.R_GARDEN || t == GenData.R_FARMYARD || t == GenData.R_CAMPSITE || t == GenData.R_PADDOCK)
            {
                string[] pool = { "hk_snowman", "hb_snowman", "hk_reindeer", "hk_candyCane" };
                int k0 = r.Range(pool.Length);
                for (int k = 0; k < 2 && Fit(1); k++)
                {
                    int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r);
                    if (cell < 0) break;
                    int first = nEmit;
                    if (FreePlace(Dn(pool[(k0 + k) % pool.Length]), cell, r.Range(360), 0, 0, H_FREE) >= 0) Keep(first, false);
                }
            }
        }
        else
        {
            bool doorway = t == GenData.R_LAWN_OPEN || t == GenData.R_PORCH || t == GenData.R_PATIO || t == GenData.R_HALL || t == GenData.R_LIVING
                || t == GenData.R_FORECOURT || t == GenData.R_FARMYARD || t == GenData.R_ZOO_PLAZA || t == GenData.R_MIDWAY || t == GenData.R_SHOP_FLOOR;
            if (doorway)
            {
                int first = nEmit;
                if (GroupAt(GenData.FindGrp("jacks"), GenData.A_WALL, 2, WM_NEAR, dx, dz, 0, ref r)) Keep(first, false);
                if (r.Chance(450) && Fit(1)) { first = nEmit; if (WallPlace(Dn("gk_lanternCandle"), WM_NEAR, dx, dz, WIN_ANY, 0, ref r) >= 0) Keep(first, false); }
            }
            if ((t == GenData.R_LAWN_OPEN || t == GenData.R_PARK) && r.Chance(500) && Fit(1))
            {
                int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r);
                int first = nEmit;
                if (cell >= 0 && FreePlace(Dn("hw_gravestone"), cell, r.Range(4) * 90, 0, 0, H_FREE) >= 0) Keep(first, false);
            }
        }
        roomFloorTarget[room] = saved > roomFloor[room] ? saved : roomFloor[room];
    }

    // revision 13, the map themes: a themed map shows its age everywhere, not only where a modern piece had a twin.
    // After a room's plan, up to two of the theme's own pieces are added on top of its budget, of kinds the room
    // does not hold yet: inside a space base its machines, tanks and cargo cans, outside its solar panels, dishes,
    // fuel tanks and craters; in a medieval village torches, kegs, chests and a sword on the wall inside, banners,
    // barriers, the well and crates outside.
    static readonly string[] SpaceIn = { "sp_speaker", "sp_relay", "sp_frame", "sp_canB", "sp_canC", "sp_fuelB", "sp_barrelsRail", "sp_lamp", "sp_tank", "sp_cargoStackB" };
    static readonly string[] SpaceOut = { "sp_solar", "sp_dishSmall", "sp_supportsLow", "sp_fuelA", "sp_turretDouble", "sp_crater", "sp_meteor", "sp_lightPost", "sp_skipRocks", "sp_canD",
        "sp_rocketBase", "sp_pad", "sp_fins", "sp_stones", "sp_screeB", "sp_extractor", "sp_dish", "sp_barrelsRail", "sp_roverSmall", "sp_truck", "sp_miner",
        "sp_cargoCraft", "sp_racer", "sp_generator", "sp_tank", "sp_relay", "sp_monoBox", "sp_monoCargo", "sp_monoFlat", "sp_monoPass", "sp_monoFront", "sp_monoEnd",
        "sp_vent", "sp_ventB", "sp_gate", "sp_gateB", "sp_podB", "sp_podC", "sp_frameLow" };
    static readonly string[] MedievalIn = { "md_torch", "md_barrelSmall", "md_kegDeco", "md_boxStacked", "md_trunkSmallB", "md_swordWall", "md_coinsM", "md_trunkLargeC", "md_drum",
        "md_torchWall", "md_bannerThin", "md_bannerShield", "md_weaponRack", "md_crateA", "md_crateLongA", "md_oakBarrel", "md_trunkMedA", "md_arrows" };
    static readonly string[] MedievalOut = { "md_banner", "md_bannerGreen", "md_torchStand", "md_barrierHalf", "md_crates", "md_well", "md_barrelLarge", "md_rockSmall", "md_drum",
        "md_target", "md_flag", "md_tent", "md_lumber", "md_stone", "md_crateOpen", "md_crateLongC", "md_ladder" };

    void ThemeDecor()
    {
        int room = pRoom;
        bool building = GenData.IsBuilding(roomZone[room]);
        string[] pool = RoomThemeOf(room) == GenData.THEME_SPACE ? (building ? SpaceIn : SpaceOut) : (building ? MedievalIn : MedievalOut);
        int saved = roomFloorTarget[room];
        int most = !building && AreaM2(room) >= 24 ? 3 : 2;   // a large outdoor area takes one more
        roomFloorTarget[room] = roomFloor[room] + most;
        GenRng r = SR(75);
        int p0 = r.Range(pool.Length), added = 0, plot = roomPlot[room];
        // first the kinds the whole place does not hold yet, then those only this room lacks
        for (int k = 0; k < 2 * pool.Length && added < most && Fit(1); k++)
        {
            int def = GenData.FindDef(pool[(p0 + k) % pool.Length]);
            if (CountInRoom(def, room) > 0 || (k < pool.Length && plot >= 0 && InPlot(def, plot))) continue;
            int first = nEmit, e;
            // a loose thing (a meteor, a small rock) never alone in the open: at a wall or the fence
            if (GenData.defAnchor[def] == GenData.A_WALL || GenData.defAnchor[def] == GenData.A_CORNER || GenData.defAnchor[def] == GenData.A_OUT_EDGE || Has(def, GenData.FL_LOOSE))
                e = WallPlace(def, WM_ANY, 0, 0, WIN_ANY, 0, ref r);
            else
            {
                int cell = OpenSpot(2, WM_ANY, 0, 0, 0, ref r);
                e = cell >= 0 ? FreePlace(def, cell, r.Range(4) * 90, 0, 0, H_FREE) : -1;
            }
            if (e >= 0 && Keep(first, false)) added++;
        }
        roomFloorTarget[room] = saved > roomFloor[room] ? saved : roomFloor[room];
    }

    bool InPlot(int def, int plot)
    {
        for (int e = 0; e < nEmit; e++)
            if (emitDef[e] == def && (emitFlags[e] & EF_REMOVED) == 0 && roomPlot[emitRoom[e]] == plot) return true;
        return false;
    }
}
