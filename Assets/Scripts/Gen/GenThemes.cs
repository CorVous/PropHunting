// GenData, revision 12: the whole-map themes' twins (G58). On a space-base or medieval-village map every prop the plans
// name is replaced by its twin here (MapGen.TwinIn); a prop with no twin stays as it is (food, books, plants, rugs and
// the small things that belong to any age). A twin made with Like() takes the anchor, flags, kinds, clearance, caps
// and jitter of the piece it stands in for, so the plans place it the same way; a host (a table, a desk, a bed, a
// counter) gets the same kind of slots as the piece it replaces, so the rooms keep their chairs and table settings.
public static partial class GenData
{
    static int[] likeBase = new int[MAXD];

    // a twin of `of`: its own id and model at its own scale, placed like `of`
    static int Like(string of, string id, string model, int scale)
    {
        int b = FindDef(of);
        // never much wider or taller than the piece it stands in for (it must fit where that one fits): at most a
        // quarter wider (+15 cm) and 40% taller (+30 cm); trees and the small things in slots keep their scale
        if ((defFlags[b] & FL_SCENERY) == 0 && defAnchor[b] != A_SLOT_ONLY)
        {
            int m = Model(model);
            int w = GenModels.SizeMm[m * 3] > GenModels.SizeMm[m * 3 + 2] ? GenModels.SizeMm[m * 3] : GenModels.SizeMm[m * 3 + 2], h = GenModels.SizeMm[m * 3 + 1];
            int wb = defSx[b] > defSz[b] ? defSx[b] : defSz[b];
            if (w > 0 && w * scale / 1000 > wb * 5 / 4 + 150) scale = (wb * 5 / 4 + 150) * 1000 / w;
            if (h > 0 && h * scale / 1000 > defSy[b] * 7 / 5 + 300) scale = (defSy[b] * 7 / 5 + 300) * 1000 / h;
            // a piece against a wall keeps its depth: no more than half again the base's (+15 cm)
            int dz = GenModels.SizeMm[m * 3 + 2];
            if ((defAnchor[b] == A_WALL || defAnchor[b] == A_CORNER) && dz > 0 && dz * scale / 1000 > defSz[b] * 3 / 2 + 150) scale = (defSz[b] * 3 / 2 + 150) * 1000 / dz;
        }
        int d = D(id, model, scale, defTags[b], defAnchor[b], defFlags[b] & ~FL_GENERATED, defAbsurd[b]);
        defFacing[d] = defFacing[b]; defClear[d] = defClear[b]; defSpacing[d] = defSpacing[b];
        defJitPos[d] = defJitPos[b]; defJitYaw[d] = defJitYaw[b]; defLeakWeight[d] = defLeakWeight[b];
        defCollider[d] = defCollider[b]; defKind[d] = defKind[b]; defBeside[d] = defBeside[b]; defRoomCap[d] = defRoomCap[b];
        defSlotWeight[d] = defSlotWeight[b];
        if ((defFlags[b] & FL_SCENERY) != 0) { defW[d] = defW[b]; defD[d] = defD[b]; defCanopyY[d] = defSy[d] * 45 / 100; }
        likeBase[d] = b + 1;
        CopySlots(b, d);
        return d;
    }

    // the base's slot groups and slots on the twin: a position inside the base's footprint scales with the twin's size,
    // one outside it (a chair at a table) keeps its distance from the edge; a slot on the base's top goes on the twin's
    static void CopySlots(int b, int d)
    {
        for (int g = 0; g < defGroupCount[b]; g++)
        {
            int bg = defGroupStart[b] + g;
            Group(groupMode[bg], groupChance[bg], groupRef[bg]);
        }
        for (int k = 0; k < defSlotCount[b]; k++)
        {
            int bs = defSlotStart[b] + k;
            int y = slotY[bs] == defTopY[b] ? defTopY[d] : (defSy[b] > 0 ? slotY[bs] * defSy[d] / defSy[b] : slotY[bs]);
            Slot(slotGroup[bs], slotType[bs], Fit(slotX[bs], defSx[b], defSx[d]), y, Fit(slotZ[bs], defSz[b], defSz[d]), slotYaw[bs], slotSize[bs], slotAccept[bs], slotFill[bs]);
            if (slotHiding[bs]) Hiding();
        }
    }

    static int Fit(int v, int sizeB, int sizeD)
    {
        int half = sizeB / 2, a = v < 0 ? -v : v;
        int r = a <= half ? (sizeB > 0 ? a * sizeD / sizeB : a) : a - half + sizeD / 2;
        return v < 0 ? -r : r;
    }

    // the twin of each base in a theme; a twin "=x" is the existing def x
    static void Twins(int theme, params string[] pairs)
    {
        for (int k = 0; k < pairs.Length; k += 2) TTwin(theme, pairs[k], pairs[k + 1].TrimStart('='));
    }

    // ================================================================================== the theme defs (Build)
    // Model names are the converter's keys: a name another pack already has takes its pack's prefix
    // (convert_models.pack_prefix: space_, station_, dungeon_, fantasy_).
    static void BuildThemeDefs()
    {
        int first = nDefs;
        BuildThemePieces();
        // a theme's pieces stand only where TwinIn puts them on a map of that theme: never picked for a slot or as a
        // wrong-category thing on their own (they would turn up on ordinary maps)
        for (int d = first; d < nDefs; d++)
        {
            defMapTheme[d] = defId[d].StartsWith("sp_") ? THEME_SPACE : THEME_MEDIEVAL;
            defLeakWeight[d] = 0;
        }
    }

    static void BuildThemePieces()
    {
        // ---- the space base: the Space Station Kit (x2, as the Furniture Kit), the Space Kit (x2.3: its astronaut is
        // 0.79 units tall), Space Base Bits (KayKit, x1.1 to x3)
        Like("k_sofa", "sp_seatHigh", "chair-armrest-headrest", 2400);
        Like("k_loungeChair", "sp_seatArm", "chair-armrest", 2000);
        Like("k_tableCoffee", "sp_tableLow", "table-inset-small", 1400);
        Like("k_table", "sp_table", "station_table", 2000);
        Like("q_table", "sp_tableLarge", "table-large", 1700);
        Like("r_tableRound", "sp_tablePlanet", "table-display-planet", 2000);
        Like("k_chair", "sp_chair", "station_chair", 2000);
        Like("k_chairCushion", "sp_chairCushion", "chair-cushion", 2000);
        Like("k_chairDesk", "sp_chairDesk", "chair-headrest", 2000);
        Like("q_stool", "sp_stool", "desk_chairStool", 2300);
        Like("k_bedDouble", "sp_bedDouble", "bed-double", 2000);
        Like("k_bedSingle", "sp_bedSingle", "bed-single", 2000);
        Like("k_bedside", "sp_bedside", "container", 1000);
        Like("k_sideTable", "sp_sideTable", "table-display-small", 1100);
        Like("k_bookcase", "sp_lockerTall", "container-tall", 2000);
        Like("k_bookcaseLow", "sp_locker", "container", 2000);
        Like("q_cabinet", "sp_lockerWide", "container-wide", 2000);
        Like("gen_wardrobe", "sp_cargoFlat", "container-flat", 2000);
        Like("k_tvCabinet", "sp_holoTable", "table-display", 2000);
        Like("k_tv", "sp_display", "display-wall", 1600);
        Like("k_tvVintage", "sp_displayWide", "display-wall-wide", 1600);
        Like("k_screen", "sp_screen", "desk_computerScreen", 1500);
        Like("k_laptop", "sp_terminal", "desk_computer", 1300);
        Like("k_desk", "sp_desk", "table-inset-small", 2000);
        Like("s_workbench", "sp_bench", "table-inset", 2000);
        Like("k_fridge", "sp_fridge", "machine_barrel", 2300);
        Like("k_counter", "sp_counter", "computer-wide", 2000);
        Like("k_stove", "sp_cooker", "computer", 2000);
        Like("k_sink", "sp_sink", "computer-screen", 2000);
        Like("k_bar", "sp_bar", "table-display-small", 2000);
        Like("k_lampFloor", "sp_lamp", "lights", 1400);
        Like("k_trashcan", "sp_bin", "space_barrel", 2300);
        Like("k_speaker", "sp_speaker", "machine_wireless", 1000);
        Like("w_boxA", "sp_cargoA", "cargo_A", 1100);
        Like("w_boxB", "sp_cargoB", "cargo_B", 1100);
        Like("k_boxClosed", "sp_cargoAPacked", "cargo_A_packed", 1100);
        Like("k_boxOpen", "sp_cargoBPacked", "cargo_B_packed", 1100);
        Like("s_boxLarge", "sp_cargoStack", "cargo_A_stacked", 1100);
        Like("w_palletLoaded", "sp_cargoStackB", "cargo_B_stacked", 1100);
        Like("w_pallet", "sp_crateFlat", "containers_A", 2000);
        Like("w_fuelBarrel", "sp_barrels", "barrels", 1600);
        Like("s_barrel", "sp_barrel", "space_barrel", 2600);
        Like("gen_dumpster", "sp_skip", "skip-rocks", 2000);
        Like("gen_shed", "sp_pod", "roofmodule_cargo_A", 1300);
        Like("gen_tap", "sp_extractor", "machine_wireless", 1300);
        Like("gen_mailbox", "sp_dish", "satelliteDish", 1600);
        Like("m_wheelbarrow", "sp_roverSmall", "rover", 2200);
        Like("c_sedan", "sp_truck", "spacetruck", 3000);
        Like("c_suv", "sp_truckLarge", "spacetruck_large", 3000);
        Like("c_van", "sp_trailer", "spacetruck_trailer", 3000);
        Like("c_hatch", "sp_racer", "craft_racer", 1200);
        Like("c_sports", "sp_speeder", "craft_speederB", 1200);
        Like("c_ambulance", "sp_cargoCraft", "craft_cargoA", 1200);
        Like("f_lamp", "sp_lightPost", "lights", 1700);
        Like("n_campfire", "sp_heater", "machine_generator", 1300);
        Like("n_tentClosed", "sp_lander", "lander_A", 1500);
        Like("n_tentOpen", "sp_landerB", "lander_B", 1500);
        Like("n_statueHead", "sp_dishLarge", "satelliteDish_large", 2000);
        Like("n_obelisk", "sp_turbine", "windturbine_low", 1400);
        Like("n_column", "sp_drill", "drill_structure", 900);
        Like("n_statueRing", "sp_turret", "turret_single", 1800);
        Like("f_fountain", "sp_pad", "landingpad_small", 1300);
        Like("n_logLarge", "sp_bones", "bones", 2300);
        Like("n_planter", "sp_solar", "solarpanel", 2000);
        Like("n_bush", "sp_crystals", "rock_crystals", 1600);
        Like("n_bushLarge", "sp_crystalsBig", "rock_crystalsLargeA", 1500);
        Like("n_bushDetailed", "sp_crystalsBigB", "rock_crystalsLargeB", 1500);
        Like("n_rockLarge", "sp_rockA", "space_rock_largeA", 1700);
        Like("n_rockTall", "sp_rockB", "space_rock_largeB", 1700);
        Like("n_rockSmall", "sp_pebble", "rock_A", 1200);
        Like("n_rockSmallB", "sp_pebbleB", "rock_B", 1100);
        Like("n_stump", "sp_rubble", "rocks_A", 1100);
        Like("n_flowerRed", "sp_shard", "rock_crystals", 450);
        Like("n_flowerYellow", "sp_shardB", "rock_crystalsLargeA", 400);
        Like("n_treeDefault", "sp_spireA", "rock_crystalsLargeA", 3200);
        Like("n_treeOak", "sp_spireB", "rock_crystalsLargeB", 3200);
        Like("n_treePineTall", "sp_boulderA", "space_rock_largeA", 3400);
        Like("n_treeFat", "sp_boulderB", "rocks_B", 1800);
        Like("n_grass", "sp_grit", "rock_A", 600);

        // ---- the medieval village: the KayKit Dungeon Pack (x0.45 to x0.9: its raw sizes are about twice real), the
        // Fantasy Town Kit's pines and rocks (x1.6 to x1.8), the Medieval Hexagon well (x1.8)
        Like("k_sofa", "md_settle", "stall-bench", 2400);
        Like("k_loungeChair", "md_chair", "dungeon_chair", 900);
        Like("k_chair", "md_chairSlot", "dungeon_chair", 800);
        Like("k_chairDesk", "md_chairDesk", "dungeon_chair", 800);
        Like("q_stool", "md_stool", "dungeon_stool", 700);
        Like("k_table", "md_tableLong", "table_long_tablecloth", 600);
        Like("q_table", "md_tableMed", "dungeon_table_medium", 650);
        Like("r_tableRound", "md_tableCloth", "table_medium_tablecloth", 600);
        Like("k_tableCoffee", "md_tableSmall", "dungeon_table_small", 600);
        Like("k_sideTable", "md_tableDeco", "table_small_decorated_A", 600);
        Like("k_bedDouble", "md_bed", "bed_decorated", 700);
        Like("k_bedSingle", "md_bedFrame", "bed_frame", 700);
        Like("n_cot", "md_strawBed", "dungeon_bed_floor", 700);
        Like("k_bedside", "md_bedTrunk", "trunk_small_A", 700);
        Like("k_bookcase", "md_shelves", "shelves", 900);
        Like("k_bookcaseLow", "md_shelfCandles", "shelf_small_candles", 900);
        Like("gen_wardrobe", "md_cratesStack", "crates_stacked", 600);
        Like("k_tv", "md_candleLit", "candle_triple", 450);
        Like("k_laptop", "md_book", "book_single", 500);
        Like("k_desk", "md_desk", "dungeon_table_medium", 600);
        Like("k_fridge", "md_keg", "keg", 450);
        Like("k_counter", "md_counter", "box_small_decorated", 600);
        Like("k_sink", "md_washTub", "barrel_small", 700);
        Like("k_bathtub", "md_tub", "barrel_large", 500);
        Like("k_washer", "md_barrelStack", "barrel_small_stack", 500);
        Like("k_lampFloor", "md_torch", "torch_lit", 1300);
        Like("k_lampTable", "md_candle", "candle_lit", 450);
        Like("k_speaker", "md_barrelSmall", "barrel_small", 500);
        Like("k_boxClosed", "md_box", "box_small_decorated", 500);
        Like("s_boxLarge", "md_boxLarge", "box_large", 500);
        Like("w_fuelBarrel", "md_barrelLarge", "barrel_large_decorated", 450);
        Like("gen_rack", "md_rack", "shelves", 950);
        Like("gen_dumpster", "md_crates", "crates_stacked", 650);
        Like("gen_tap", "md_well", "building_well_blue", 1800);
        Like("ms_gacha", "md_coins", "coin_stack_large", 400);
        Like("ma_claw", "md_kegDeco", "keg_decorated", 350);
        Like("sk_halfPipe", "md_barrier", "barrier", 500);
        Like("sk_scooter", "md_sword", "sword_shield", 500);
        Like("tc_racer", "md_toyCart", "cart", 500);
        Like("n_treePineTall", "md_pine", "tree-high", 1800);
        Like("n_treePineRound", "md_pineCrooked", "tree-crooked", 1800);
        Like("n_rockLarge", "md_rock", "rock-large", 1600);
        Like("n_rockTall", "md_rockWide", "rock-wide", 1600);

        // more space pieces, so a themed place is as varied as an ordinary one (G58): one twin per base where the
        // packs allow
        Like("q_couch", "sp_seatCush", "chair-cushion-headrest", 2400);
        Like("q_armchair", "sp_chairArms", "desk_chairArms", 2600);
        Like("k_recliner", "sp_chairPilot", "desk_chair", 2800);
        Like("k_bookcase", "sp_frame", "supports_high", 1800);
        Like("k_bookcase", "sp_server", "computer-system", 2000);
        Like("q_cabinet", "sp_containerOpen", "container-flat-open", 2000);
        Like("k_fridge", "sp_tank", "machine_barrelLarge", 2000);
        Like("k_fridge", "sp_relay", "machine_wirelessCable", 2000);
        Like("k_counter", "sp_console", "desk_computerCorner", 2000);
        Like("k_stove", "sp_generator", "machine_generatorLarge", 1000);
        Like("k_trashcan", "sp_fuelA", "rocket_fuelA", 1000);
        Like("w_fuelBarrel", "sp_fuelB", "rocket_fuelB", 900);
        Like("w_fuelBarrel", "sp_barrelsRail", "barrels_rail", 1600);
        Like("w_boxA", "sp_canB", "containers_B", 2000);
        Like("w_boxA", "sp_canC", "containers_C", 2000);
        Like("w_boxA", "sp_canD", "containers_D", 2000);
        Like("c_sedan", "sp_miner", "craft_miner", 1200);
        Like("c_sedan", "sp_cargoCraftB", "craft_cargoB", 1200);
        Like("c_sports", "sp_speederA", "craft_speederA", 1100);
        Like("c_sports", "sp_speederD", "craft_speederD", 1100);
        Like("n_statueRing", "sp_turretDouble", "turret_double", 1800);
        Like("f_lamp", "sp_dishSmall", "satelliteDish_detailed", 1500);
        Like("f_fountain", "sp_padLarge", "landingpad_large", 1000);
        Like("n_column", "sp_rocketBase", "rocket_baseA", 1000);
        Like("f_hedge", "sp_solarRoof", "roofmodule_solarpanels", 1300);
        Like("n_logLarge", "sp_supportsLow", "supports_low", 1800);
        Like("n_mushroomRed", "sp_stone", "rock", 800);
        Like("n_mushroomGroup", "sp_stones", "rocks", 900);

        Like("n_flowerRed", "sp_screeA", "rocks_smallA", 700);
        Like("n_flowerRed", "sp_screeB", "rocks_smallB", 800);
        Like("n_flowerRed", "sp_meteor", "meteor", 350);
        Like("n_flowerRed", "sp_meteorD", "meteor_detailed", 350);
        Like("n_flowerRed", "sp_meteorHalf", "meteor_half", 400);
        Like("n_rockSmall", "sp_crater", "crater", 900);
        Like("k_bedside", "sp_bedsideB", "rocket_sidesA", 550);
        Like("n_bush", "sp_fins", "rocket_finsB", 800);

        // more medieval pieces (as above)
        Like("k_sofa", "md_trestle", "stall", 2400);
        Like("k_bookcase", "md_trunkLarge", "trunk_large_B", 700);
        Like("k_bookcase", "md_boxStacked", "box_stacked", 450);
        Like("q_cabinet", "md_chestGold", "chest_gold", 500);
        Like("k_bedside", "md_trunkMed", "trunk_medium_C", 700);
        Like("k_sideTable", "md_tableDecoB", "table_small_decorated_B", 600);
        Like("k_tableCoffee", "md_tableMedDeco", "table_medium_decorated_A", 600);
        Like("k_boxClosed", "md_trunkSmallB", "trunk_small_B", 700);
        Like("hk_lantern", "md_torchStand", "torch", 1200);
        Like("w_fuelBarrel", "md_drum", "barrel_small", 600);
        Like("gen_mailbox", "md_banner", "banner-red", 1800);
        Like("gen_mailbox", "md_bannerGreen", "banner-green", 1800);
        Like("gen_toolwall", "md_swordWall", "sword_shield_broken", 600);
        Like("ma_vending", "md_coinsM", "coin_stack_medium", 500);
        Like("n_treeFat", "md_treeRound", "tree-high-round", 1800);
        Like("n_rockSmall", "md_rockSmall", "rock-small", 600);
        Like("k_bar", "md_tableBare", "table_long", 600);
        Like("sk_halfPipe", "md_barrierHalf", "barrier_half", 500);
        Like("sk_halfPipe", "md_barrierColumn", "barrier_column", 500);
        Like("sk_halfPipe", "md_barrierCorner", "barrier_corner", 500);
        // the small modern things on furniture: bottles, candles, coins, plates, keys
        string[] mdSlot = { "md_bottleLab", "bottle_A_labeled_brown", "md_bottleBig", "bottle_C_brown", "md_candleMelted", "candle_melted", "md_coinsS", "coin_stack_small",
            "md_plateFood", "plate_food_B", "md_plateStack", "plate_stack", "md_keys", "keyring_hanging", "md_candleThin", "candle_thin_lit" };
        for (int k = 0; k < mdSlot.Length; k += 2) { D(mdSlot[k], mdSlot[k + 1], 400, I_DECOR | I_FOOD | I_TABLEWARE | I_KITCHENWARE | I_SCREEN | I_LAMP, A_SLOT_ONLY, FL_LEAK_IN | FL_LEAK_OUT, 0); }
    }

    // ================================================================================== the theme twin tables
    static void BuildSpaceTwins()
    {
        int S = THEME_SPACE;
        Twins(S, "k_sofa", "sp_seatHigh", "q_couch", "sp_seatHigh", "q_couchPillows", "sp_seatHigh", "k_designSofa", "sp_seatHigh",
            "k_loungeChair", "sp_seatArm", "q_armchair", "sp_seatArm", "q_armchairPillows", "sp_seatArm", "k_designChair", "sp_seatArm", "k_recliner", "sp_seatArm",
            "hb_armchairRed", "sp_seatArm", "hb_armchairBlue", "sp_seatArm",
            "k_tableCoffee", "sp_tableLow", "q_tableLow", "sp_tableLow", "k_table", "sp_table", "q_tableLong", "sp_table", "r_table", "sp_table",
            "q_table", "sp_tableLarge", "r_tableRound", "sp_tablePlanet",
            "k_chair", "sp_chair", "q_chair", "sp_chair", "q_chairB", "sp_chair", "r_chair", "sp_chair", "q_chairWood", "sp_chair",
            "k_chairCushion", "sp_chairCushion", "q_chairC", "sp_chairCushion", "k_chairDesk", "sp_chairDesk", "mb_gamerChairA", "sp_chairDesk", "mb_gamerChairB", "sp_chairDesk",
            "q_stool", "sp_stool", "k_stool", "sp_stool", "r_stool", "sp_stool", "q_stoolWood", "sp_stool", "hb_stool", "sp_stool",
            "k_bedDouble", "sp_bedDouble", "q_bedDouble", "sp_bedDouble", "k_bedSingle", "sp_bedSingle", "q_bed", "sp_bedSingle", "q_bedSingleB", "sp_bedSingle",
            "k_bedBunk", "sp_bedSingle", "n_cot", "sp_bedSingle",
            "k_bedside", "sp_bedside", "q_bedside", "sp_bedside", "k_bedsideDrawer", "sp_bedside",
            "k_sideTable", "sp_sideTable", "q_sideTable", "sp_sideTable",
            "k_bookcase", "sp_lockerTall", "k_bookcaseClosed", "sp_lockerTall", "k_bookcaseWide", "sp_lockerWide", "k_bookcaseLow", "sp_locker", "q_cabinetSmall", "sp_locker",
            "q_cabinetSmallDeco", "sp_locker", "q_cabinet", "sp_lockerWide", "q_cabinetDeco", "sp_lockerWide", "gen_wardrobe", "sp_cargoFlat",
            "k_tvCabinet", "sp_holoTable", "k_tv", "sp_display", "k_tvVintage", "sp_displayWide", "k_screen", "sp_screen", "k_laptop", "sp_terminal",
            "k_desk", "sp_desk", "s_workbench", "sp_bench",
            "k_fridge", "sp_fridge", "r_fridge", "sp_fridge", "k_fridgeSmall", "sp_fridge",
            "k_counter", "sp_counter", "k_counterDrawer", "sp_counter", "r_counter", "sp_counter",
            "k_stove", "sp_cooker", "r_stove", "sp_cooker", "k_stoveElectric", "sp_cooker", "k_sink", "sp_sink", "r_counterSink", "sp_sink", "k_bar", "sp_bar",
            "k_lampFloor", "sp_lamp", "q_lampStanding", "sp_lamp", "k_lampSquare", "sp_lamp", "k_trashcan", "sp_bin", "ck_trash", "sp_bin", "k_speaker", "sp_speaker",
            "w_boxA", "sp_cargoA", "w_boxB", "sp_cargoB", "w_boxC", "sp_cargoA", "k_boxClosed", "sp_cargoAPacked", "k_boxOpen", "sp_cargoBPacked",
            "dg_boxS", "sp_cargoAPacked", "dg_boxL", "sp_cargoStack",
            "r_crate", "sp_cargoA", "r_crateTomatoes", "sp_cargoB", "s_boxLarge", "sp_cargoStack", "s_boxLargeOpen", "sp_cargoStack", "w_palletLoaded", "sp_cargoStackB",
            "w_pallet", "sp_crateFlat", "w_fuelBarrel", "sp_barrels", "w_fuelBarrelB", "sp_barrels", "w_fuelBarrelC", "sp_barrels",
            "s_barrel", "sp_barrel", "s_barrelOpen", "sp_barrel", "dg_barrel", "sp_barrel", "dg_keg", "sp_fridge",
            // the racks stay the generated metal shelving: no space model is a long, shallow shelf
            "gen_dumpster", "sp_skip", "gen_shed", "sp_pod", "gen_tap", "sp_extractor", "gen_mailbox", "sp_dish", "m_wheelbarrow", "sp_roverSmall", "ms_handTruck", "sp_roverSmall",
            "c_sedan", "sp_truck", "c_taxi", "sp_truck", "c_suv", "sp_truckLarge", "c_luxury", "sp_truckLarge", "c_truck", "sp_truckLarge", "c_delivery", "sp_truckLarge",
            "c_van", "sp_trailer", "c_flatbed", "sp_trailer", "c_deliveryFlat", "sp_trailer", "c_hatch", "sp_racer", "c_police", "sp_racer",
            "c_sports", "sp_speeder", "c_ambulance", "sp_cargoCraft", "c_firetruck", "sp_cargoCraft", "c_binLorry", "sp_cargoCraft",
            "f_lamp", "sp_lightPost", "hk_lantern", "sp_lightPost", "gk_lightpost", "sp_lightPost",
            "n_campfire", "sp_heater", "n_tentClosed", "sp_lander", "n_tentOpen", "sp_landerB",
            "n_statueHead", "sp_dishLarge", "n_obelisk", "sp_turbine", "n_column", "sp_drill", "n_columnBroken", "sp_drill", "n_statueRing", "sp_turret", "n_statueBlock", "sp_turret",
            "f_fountain", "sp_pad", "f_fountainSq", "sp_pad", "n_logLarge", "sp_bones", "n_log", "sp_rubble", "n_planter", "sp_solar",
            "n_bush", "sp_crystals", "n_bushSmall", "sp_crystals", "n_bushTriangle", "sp_crystals", "n_bushLarge", "sp_crystalsBig", "n_bushDetailed", "sp_crystalsBigB",
            "f_hedge", "sp_crystalsBig", "f_hedgeCurve", "sp_crystalsBigB",
            "n_rockLarge", "sp_rockA", "n_rockTall", "sp_rockB", "n_rockSmall", "sp_pebble", "n_rockSmallB", "sp_pebbleB", "n_rockSmallC", "sp_pebble",
            "n_stump", "sp_rubble", "n_stumpRound", "sp_rubble",
            "n_flowerRed", "sp_shard", "n_flowerRedB", "sp_shard", "n_flowerPurple", "sp_shard", "n_flowerPurpleB", "sp_shardB", "n_flowerYellow", "sp_shardB", "n_flowerYellowB", "sp_shardB",
            "n_mushroomRed", "sp_shard", "n_mushroomRedTall", "sp_shard", "n_mushroomTan", "sp_shardB", "n_mushroomTanTall", "sp_shardB", "n_mushroomGroup", "sp_pebbleB", "n_mushroomTanGroup", "sp_pebbleB",
            "n_treeDefault", "sp_spireA", "n_treeOak", "sp_spireB", "n_treePineTall", "sp_boulderA", "n_treeFat", "sp_boulderB",
            "n_grass", "sp_grit", "n_grassLarge", "sp_grit");
        // one twin per base where the packs allow (overrides the shared ones above)
        Twins(S, "q_couch", "sp_seatCush", "k_designSofa", "sp_seatCush", "q_armchair", "sp_chairArms", "k_designChair", "sp_chairArms", "k_recliner", "sp_chairPilot",
            "hb_armchairRed", "sp_chairPilot", "k_bookcaseClosed", "sp_frame", "k_bookcaseWide", "sp_server", "q_cabinetDeco", "sp_server", "q_cabinetSmall", "sp_containerOpen",
            "k_fridgeSmall", "sp_tank", "dg_keg", "sp_tank", "r_fridge", "sp_relay", "k_counterDrawer", "sp_console", "r_counter", "sp_console",
            "r_stove", "sp_generator", "k_stoveElectric", "sp_generator", "ck_trash", "sp_fuelA", "w_barrel", "sp_fuelA", "w_barrelB", "sp_fuelB", "w_fuelBarrelB", "sp_fuelB",
            "w_barrelC", "sp_barrelsRail", "s_barrelOpen", "sp_barrelsRail", "w_fuelBarrelC", "sp_barrelsRail",
            "w_boxC", "sp_canB", "r_crateCheese", "sp_canB", "s_box", "sp_canB", "r_crateLettuce", "sp_canC", "s_boxOpen", "sp_canC", "r_crateCarrots", "sp_canD", "dg_boxS", "sp_canD",
            "r_cratePotatoes", "sp_cargoAPacked", "r_crateOnions", "sp_cargoBPacked", "r_crateBuns", "sp_cargoA", "r_crateHam", "sp_crateFlat",
            "c_taxi", "sp_miner", "c_luxury", "sp_cargoCraftB", "c_police", "sp_speederA", "c_firetruck", "sp_speederD",
            "n_statueBlock", "sp_turretDouble", "hk_lantern", "sp_dishSmall", "gk_lightpost", "sp_dishSmall", "f_fountainSq", "sp_padLarge", "n_columnBroken", "sp_rocketBase",
            "f_hedge", "sp_solarRoof", "n_log", "sp_supportsLow",
            "n_flowerRed", "sp_screeA", "n_flowerRedB", "sp_screeB", "n_flowerPurple", "sp_meteor", "n_flowerPurpleB", "sp_meteorD", "n_flowerYellow", "sp_meteorHalf",
            "n_bushTriangle", "sp_fins",
            "r_crate", "=r_crate", "r_crateTomatoes", "=r_crateTomatoes", "r_crateCheese", "=r_crateCheese", "r_crateLettuce", "=r_crateLettuce", "r_crateCarrots", "=r_crateCarrots",
            "r_cratePotatoes", "=r_cratePotatoes", "r_crateOnions", "=r_crateOnions", "r_crateBuns", "=r_crateBuns", "r_crateHam", "=r_crateHam",
            "s_box", "=s_box", "s_boxOpen", "=s_boxOpen",
            "n_flowerYellowB", "sp_meteorHalf", "n_rockSmallC", "sp_crater", "k_bedside", "sp_bedsideB", "q_bedside", "sp_bedsideB", "q_cabinetSmallDeco", "sp_lockerWide",
            "n_mushroomRed", "sp_stone", "n_mushroomRedTall", "sp_stone", "n_mushroomTan", "sp_stone", "n_mushroomTanTall", "sp_stone", "n_mushroomGroup", "sp_stones", "n_mushroomTanGroup", "sp_stones");
    }

    static void BuildMedievalTwins()
    {
        int M = THEME_MEDIEVAL;
        Twins(M, "k_sofa", "md_settle", "q_couch", "md_settle", "q_couchPillows", "md_settle", "k_designSofa", "md_settle",
            "k_loungeChair", "md_chair", "q_armchair", "md_chair", "q_armchairPillows", "md_chair", "k_designChair", "md_chair", "k_recliner", "md_chair",
            "hb_armchairRed", "md_chair", "hb_armchairBlue", "md_chair",
            "k_chair", "md_chairSlot", "k_chairCushion", "md_chairSlot", "q_chair", "md_chairSlot", "q_chairB", "md_chairSlot", "q_chairC", "md_chairSlot", "q_chairWood", "md_chairSlot", "r_chair", "md_chairSlot",
            "k_chairDesk", "md_chairDesk", "mb_gamerChairA", "md_chairDesk", "mb_gamerChairB", "md_chairDesk",
            "q_stool", "md_stool", "k_stool", "md_stool", "r_stool", "md_stool", "q_stoolWood", "md_stool", "hb_stool", "md_stool",
            "k_table", "md_tableLong", "q_tableLong", "md_tableLong", "r_table", "md_tableLong", "q_table", "md_tableMed", "r_tableRound", "md_tableCloth",
            "k_tableCoffee", "md_tableSmall", "q_tableLow", "md_tableSmall", "k_sideTable", "md_tableDeco", "q_sideTable", "md_tableDeco",
            "k_bedDouble", "md_bed", "q_bedDouble", "md_bed", "k_bedSingle", "md_bedFrame", "q_bed", "md_bedFrame", "q_bedSingleB", "md_bedFrame", "k_bedBunk", "md_bedFrame",
            "n_cot", "md_strawBed", "k_bedside", "md_bedTrunk", "q_bedside", "md_bedTrunk", "k_bedsideDrawer", "md_bedTrunk",
            "k_bookcase", "md_shelves", "k_bookcaseClosed", "md_shelves", "k_bookcaseWide", "md_shelves", "k_bookcaseLow", "md_shelfCandles", "q_cabinetSmallDeco", "md_shelfCandles",
            "q_cabinet", "=dg_chest", "q_cabinetDeco", "=dg_chest", "q_cabinetSmall", "=dg_trunkM", "gen_wardrobe", "md_cratesStack",
            "k_stove", "=cb_woodstove", "r_stove", "=cb_woodstove", "k_stoveElectric", "=cb_woodstove",
            "k_tv", "md_candleLit", "k_tvVintage", "md_candleLit", "k_screen", "md_candleLit", "k_laptop", "md_book",
            "k_desk", "md_desk", "k_fridge", "md_keg", "r_fridge", "md_keg", "k_fridgeSmall", "md_keg", "ms_freezers", "md_keg", "ms_freezer", "=dg_chest",
            "k_counter", "md_counter", "k_counterDrawer", "md_counter", "r_counter", "md_counter", "k_bar", "md_tableLong",
            "k_sink", "md_washTub", "r_counterSink", "md_washTub", "k_bathSink", "=m_bucketWater", "k_toilet", "=m_bucketWood", "k_bathtub", "md_tub",
            "k_washer", "md_barrelStack", "k_dryer", "md_barrelStack",
            "k_lampFloor", "md_torch", "q_lampStanding", "md_torch", "k_lampSquare", "md_torch", "k_lampTable", "md_candle", "q_lampTable", "md_candle",
            "k_trashcan", "=m_bucketWood", "ck_trash", "=m_bucketWood", "k_speaker", "md_barrelSmall",
            "k_boxClosed", "md_box", "k_boxOpen", "=dg_boxS", "w_boxA", "=dg_boxS", "w_boxB", "md_box", "s_boxLarge", "md_boxLarge", "s_boxLargeOpen", "md_boxLarge",
            "w_fuelBarrel", "md_barrelLarge", "w_fuelBarrelB", "=dg_barrel", "w_fuelBarrelC", "=dg_barrel",
            "gen_rack", "md_rack", "gen_rackPantry", "md_rack", "gen_dumpster", "md_crates", "gen_tap", "md_well",
            "ms_cart", "=m_wheelbarrow", "ms_handTruck", "=m_wheelbarrow", "ms_checkout", "md_tableSmall", "ms_shelfBoxes", "=f_stallGreen", "ms_shelfBags", "=f_stallRed",
            "ms_shelfEnd", "md_shelves", "ms_gacha", "md_coins", "ma_claw", "md_kegDeco", "ma_vending", "md_kegDeco", "ma_arcade", "md_barrelLarge",
            "ck_food", "=f_stallRed", "ck_drinks", "=f_stallGreen", "ck_info", "=f_stallGreen", "ck_toilets", "md_crates", "ck_bench", "=k_bench", "hk_lantern", "=f_lamp", "gk_lightpost", "=f_lamp",
            "c_digger", "=f_cartHigh", "c_tractorWheel", "=f_wheel",
            "sk_halfPipe", "md_barrier", "sk_halfPipeLow", "md_barrier", "sk_rail", "md_barrier", "sk_railLow", "md_barrier", "sk_ramp", "=g_hay", "sk_rampRail", "=g_hayBundled",
            "sk_box", "md_crates", "sk_grind", "md_crates", "sk_steps", "=g_hay", "sk_platform", "md_cratesStack", "sk_benches", "=k_bench",
            "sk_scooter", "md_sword", "sk_board", "md_barrelSmall", "mb_skates", "=dg_trunkS", "mb_helmet", "md_coins",
            "tc_racer", "md_toyCart", "tc_monster", "md_toyCart", "tc_truck", "md_toyCart", "tc_suv", "md_toyCart",
            "n_treePineTall", "md_pine", "n_treePineRound", "md_pineCrooked", "n_rockLarge", "md_rock", "n_rockTall", "md_rockWide");
        // one twin per base where the packs allow (overrides the shared ones above)
        Twins(M, "q_couch", "md_trestle", "k_designSofa", "md_trestle", "k_benchCushion", "md_trestle", "k_bookcaseClosed", "md_trunkLarge", "k_bookcaseWide", "md_boxStacked",
            "w_palletLoaded", "md_boxStacked", "q_cabinet", "md_chestGold", "k_tvCabinet", "md_chestGold", "k_bedsideDrawer", "md_trunkMed", "q_bedside", "md_trunkMed",
            "q_sideTable", "md_tableDecoB", "q_tableLow", "md_tableMedDeco", "w_boxC", "md_trunkSmallB", "s_box", "md_trunkSmallB",
            "w_barrel", "=dg_barrel", "w_barrelB", "md_barrelLarge", "w_barrelC", "md_barrelStack", "c_cone", "md_torchStand", "gen_mailbox", "md_banner", "n_sign", "md_bannerGreen",
            "gen_toolwall", "md_swordWall", "ma_vending", "md_coinsM", "n_treeFat", "md_treeRound", "n_rockSmallC", "md_rockSmall",
            "k_microwave", "md_plateStack", "k_toaster", "md_bottleLab", "k_coffeeMachine", "md_bottleBig", "f_sodaCan", "=dg_bottleB", "fk_soda", "md_bottleLab",
            "k_radio", "md_candleMelted", "mb_console", "md_coinsS", "mb_headphonesA", "=dg_bottleA", "mb_headphonesC", "md_plateFood", "mb_camera", "md_coinsS",
            "k_screen", "md_keys", "k_tvVintage", "md_candleThin", "mb_bottle", "md_bottleBig");
        // the KayKit wooden chairs and armchairs suit a village as they are; the modern Kenney seats become them
        Twins(M, "q_chairWood", "=q_chairWood", "q_chair", "=q_chair", "r_chair", "=r_chair", "q_armchair", "=q_armchair", "hb_armchairRed", "=hb_armchairRed",
            "hb_armchairBlue", "=hb_armchairBlue", "k_loungeChair", "=q_armchair", "k_designChair", "=hb_armchairRed", "k_recliner", "=hb_armchairBlue",
            "k_chairCushion", "=q_chairWood", "q_chairB", "=r_chair", "mb_gamerChairA", "md_stool", "mb_gamerChairB", "md_stool",
            "k_trashcan", "=s_barrelOpen", "k_toilet", "=s_barrel", "k_bathSink", "=s_bucket", "k_bar", "md_tableBare", "r_table", "md_tableBare",
            "w_barrel", "=w_barrel", "w_fuelBarrelB", "md_drum", "w_fuelBarrelC", "md_drum", "w_barrelB", "=w_barrelB", "w_barrelC", "=w_barrelC",
            "k_counterDrawer", "md_counter", "w_boxB", "=w_boxB", "k_tvCabinet", "md_shelfCandles", "k_bookcase", "md_trunkLarge", "k_bookcaseClosed", "md_shelves",
            "ck_bench", "md_settle", "sk_benches", "md_trestle", "ck_info", "md_tableDecoB", "ck_drinks", "=dg_keg", "sk_railLow", "md_barrierHalf", "sk_halfPipeLow", "md_barrierColumn",
            "sk_halfPipe", "md_barrierCorner", "hk_lantern", "md_torchStand", "gk_lightpost", "=gk_lightpost", "ck_toilets", "md_boxStacked", "sk_grind", "md_boxLarge", "sk_box", "=dg_boxL",
            "ms_shelfEnd", "md_tableMedDeco", "k_boxOpen", "=s_boxOpen", "w_boxA", "=s_box", "ms_handTruck", "=f_cart", "ms_cart", "=m_wheelbarrow", "k_trashcan", "=m_bucketWood",
            "w_boxC", "md_trunkSmallB");
    }

    // after the themes of the slot items are set: a twin is used where its base is
    static void ThemeTwinThemes()
    {
        for (int d = 0; d < nDefs; d++) if (likeBase[d] > 0 && defThemes[d] == 0) defThemes[d] = defThemes[likeBase[d] - 1];
    }
}
