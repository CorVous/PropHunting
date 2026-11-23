// The generator's data tables: sizes, zone pieces, object definitions, slots and spawn tables.
// In the VRChat world these would be authored as assets and baked to arrays (04 section 4.5); for the
// prototype they are written here in code and stored the same way: flat arrays, integer units
// (millimetres, per-mille chances, inner cells of 0.5 m). No UnityEngine.
public static partial class GenData
{
    // Bump whenever any seed would produce a different map (04 section 6.3).
    public const int GEN_VERSION = 33;   // 33: the dungeon long table's chairs stand at its long sides (its places turn with it); 32: small props grow evenly to at least the volume of a 20 cm cube (Cor: small props bigger); 31: every stair has open floor of its own room at its foot on both levels, two rows of it kept clear; a doorway in its way moves (Cor: the stairs leading straight into a wall); 30: the lawn nobody needs is scenery ground (trees, bushes, rocks; walkable, no props): a plot keeps its front, patio, purposes, yards, the cells at its doors and gates and one passage to the back; a funfair keeps its midway and one rides yard (Cor: fewer outdoor areas, each with a purpose; the ones with just trees or rocks are ok inside the fence); 29: house gardens get a purpose (picnic ground, vegetable garden, drying yard, fire pit, pond, campsite), sets in countable patterns, a verge along the lane, a camp in a smaller wood (Cor: outdoors is just rocks); 28: what no person placed is scenery, no disguise and no wrong-category spawn (Cor); 27: fewer mostly-outdoor plots, smaller warehouses (Cor: playtest); 26: basements about twice as often (Cor: playtest); 25: no torch on a village's walls, nothing on a space base's display tables (Cor: playtest); 24: three keep-clear cells straight through a doorway (bugfix 4: a bed at two stood over the door); 23: stairs with open floor at their foot; 22: banners are not props, nothing on chests, trunks and ready-laid tables (Cor: playtest); 21: the lane and big lawns in rooms of six cells, furnishing in steps (Cor: build freezes); 20: upstairs room types by room size (review 7); 19: fewer extra floors, rarely both (Cor: varied); 18: upper floors and basements (MapGenLevels.cs); 17: the mixed theme off again (Cor: three distinct themes work better), a third each
    // The zone layout and the walls and doors have a version of their own, so a seed keeps its buildings when only
    // the prop rules change. Bump it when the layout, the walls and doors or the room types change.
    // Layout 1 (generators 1 to 4): zone pieces glued to a lawn hub, room types by room size (AssignRoomTypes).
    // Layout 2 (generator 5): plots along a lane, buildings from the plan library, a wood (MapGenSite.cs, G32).
    // Layout 3 (generator 6): the same, at the smaller sizes of G36 "B" (no front garden at S, fewer and smaller plots).
    // Layout 4 (generator 9): more kinds of plot (shop, garage, farm, park) drawn into a mix per map, more plans.
    // Layout 5 (generator 10): barns with a farm workshop, a two-cell paddock, parks with a skate corner, a pond and a cafe terrace.
    // Layout 6 (generator 11): house plans with a pantry or a box room; kids' rooms and second bedrooms drawn as playroom, teen room or box room.
    // Layout 7 (generator 12): two more kinds of plot: the zoo (plaza, walk, fenced enclosures with gates) and the log cabin.
    // Layout 8 (generator 13): a space-base or medieval-village map draws only the kinds of plot its theme allows.
    // Layout 9 (generator 14): main's site shapes (bend, T, cul-de-sac) with these plots; main's open plots dropped.
    // Layout 10 (generator 15): bigger plans, smaller gardens and wood, buildings at the edge, three more site shapes.
    // Layout 11 (generator 18): upper floors and basements, joined by stairs (Cor).
    // Layout 12 (generator 19): fewer of them, a basement under an upper floor only now and then.
    // Layout 13 (generator 20): an upper floor's bathroom in its smallest room, big rooms living rooms or playrooms.
    // Layout 14 (generator 21): the lane and the plots' outdoor areas in rooms of at most six cells.
    public const int LAYOUT_VERSION = 14;

    public const int CELL_MM = 4000;      // outer cell
    public const int INNER_MM = 500;      // inner cell
    public const int INNER_PER_CELL = 8;
    public const int WALL_HALF_MM = 100;  // walls are 0.2 m thick, centred on the cell edge
    public const int DOOR_CELLS = 2;      // doorway width in inner cells (1.0 m)
    public const int DOOR_HEIGHT_MM = 2100;

    // ---- size buckets (04 section 4.7.7) ----
    // Index 4 is XS (Cor, playtest 2026-11-20: 9 players on Auto got an M map): one house and a small wood, for up
    // to about 5 players. It comes last so the seed codes and trail entries of S to XL keep their size numbers.
    public const int SIZE_XS = 4;
    public static readonly string[] SizeName = { "S", "M", "L", "XL", "XS" };
    // Layout 2 (G32, G36): the map is a lane with plots on both sides and a wood, so its box follows the site; the
    // sizes give the plots, the ground cells the wood tops the map up to, and the wood's own minimum. Layout 1 (the
    // zone pieces) filled a fixed square of SizeGrid cells to 28, 52, 85 and 108 ground cells.
    public static readonly int[] SizeGrid = { 7, 9, 12, 14, 6 };            // layout 1: bounding box, outer cells per side
    public static readonly int[] SizeGroundCells = { 0, 0, 0, 0, 0 }; // ground cells the wood tops the map up to (layout 1: 28, 52, 85, 108; layout 2: 46, 80, 124, 164)
    public static readonly int[] SizeWoodMin = { 3, 3, 4, 5, 3 };        // the wood is never smaller than this (layout 2: 6, 10, 16, 22)
    // Plots per map (G36 counts: 2, 3, 4, 5). Layout 4 (revision 9): H a house, * any kind of plot (a house more
    // often than not), + any plot that is not a house, never two of one kind on a map: a diner, a warehouse, a shop, a
    // garage, a farm or a park, by the weights below. Layout 2 and 3: "H+", "HH+", "HHDW", "HHHDW" (+ a diner or a
    // warehouse, or a house at S): every L and XL map had the same four or five plots.
    public static readonly string[] SizePlots = { "H*", "H*+", "H*++", "HH*++", "H" };
    public static readonly int[] PlotPoolZone = { Z_DINER, Z_WAREHOUSE, Z_SHOP, Z_GARAGE, Z_BARN, Z_PARK, Z_ZOO, Z_CABIN, Z_FAIR, Z_CHAPEL };   // revision 12: zoo, cabin, funfair, chapel
    public static readonly int[] PlotPoolWeight = { 13, 9, 15, 12, 4, 3, 3, 6, 4, 5 };   // generator 27 (Cor: indoors is more fun, fewer outdoor areas): the farm, park, zoo, funfair and chapel, mostly open ground, less often (were 8, 9, 8, 8, 8, 7)
    public const int PLOT_ANY_HOUSE = 350;                                // per mille: "*" is a house
    // cells of the new kinds' plans by size (least, most); a park's plot in outer cells along the lane and deep
    public static readonly int[] SizeShopCellsMin = { 4, 4, 4, 5, 4 }, SizeShopCellsMax = { 9, 11, 13, 13, 8 };
    public static readonly int[] SizeGarageCellsMin = { 4, 4, 5, 5, 4 }, SizeGarageCellsMax = { 10, 12, 14, 16, 8 };
    // revision 10: barns of 3 cells or more (room for the farm workshop), parks larger (a lawn and feature areas)
    public static readonly int[] SizeBarnCellsMin = { 3, 3, 4, 4, 3 }, SizeBarnCellsMax = { 8, 10, 12, 12, 7 };   // revision 9: 2, 2, 3, 4 and 3, 4, 6, 6
    public static readonly int[] SizeParkAlong = { 3, 4, 4, 5, 3 }, SizeParkDeep = { 3, 3, 3, 3, 3 };          // revision 9: 2, 2, 3, 3 and 2, 3, 3, 3
    // revision 12: the zoo is three cells along the lane (enclosures, the keepers' walk, enclosures) and 3 or 4 deep;
    // the log cabin's plans by size
    public static readonly int[] SizeZooDeep = { 3, 3, 4, 4, 3 };
    public static readonly int[] SizeCabinCellsMin = { 2, 2, 3, 3, 2 }, SizeCabinCellsMax = { 6, 8, 10, 10, 5 };
    public static readonly int[] SizeFairCellsMin = { 4, 4, 5, 5, 4 }, SizeFairCellsMax = { 10, 12, 15, 15, 8 };
    public static readonly int[] SizeChapelCellsMin = { 3, 3, 3, 4, 3 }, SizeChapelCellsMax = { 8, 10, 12, 12, 7 };
    public static readonly int[] SizeExtraHouse = { 0, 0, 0, 0, 0 };        // chance (per mille) of one more house plot (layout 2: 0, 300, 400, 500)
    // the plans a plot may take, by their cells: houses, warehouses, diners (the least and the most)
    public static readonly int[] SizeHouseCellsMin = { 5, 6, 6, 7, 6 };
    public static readonly int[] SizeHouseCellsMax = { 14, 18, 22, 24, 12 };
    public static readonly int[] SizeWarehouseCellsMin = { 6, 6, 6, 7, 6 };   // generator 27 (Cor: the warehouse and its storerooms too big): were 6, 6, 8, 8 and 12, 14, 16, 16
    public static readonly int[] SizeWarehouseCellsMax = { 8, 9, 10, 11, 8 };
    public static readonly int[] SizeDinerCellsMax = { 10, 12, 14, 16, 9 };
    // a house plot's gardens in cells: the front garden (and the chance of one more row), the back garden (and the
    // chance of one more row); a warehouse's forecourt and its chance of a yard two deep at the back; the chance of
    // a side yard. G36 "B" brought the maps down to about 40 to 60% above layout 1: at S the house's front door and
    // the warehouse's office door open straight onto the lane (report 16, 4.1: "S: 0 allowed"), no extra house plot,
    // smaller commercial plans at M and XL, a back garden one row deeper less often. Layout 2: front { 1, 1, 1, 1 }
    // and one more row 25% at L and XL, back extra { 300, 300, 350, 350 }, warehouse front 1 at every size.
    public static readonly int[] SizeHouseFront = { 0, 0, 0, 0, 0 };
    public static readonly int[] SizeHouseFront2 = { 0, 0, 0, 0, 0 };
    public static readonly int[] SizeHouseBack = { 1, 1, 1, 1, 1 };
    public static readonly int[] SizeHouseBackExtra = { 0, 0, 0, 0, 0 };
    public static readonly int[] SizeWarehouseFront = { 0, 0, 0, 0, 0 };
    public static readonly int[] SizeWarehouseBack2 = { 0, 0, 0, 0, 0 };
    public static readonly int[] SizeSideYard = { 0, 0, 0, 0, 0 };
    public static readonly int[] SizeLevelsMax = { 1, 2, 2, 3, 1 };      // generator 18: upper floors and basements per map at most
    public static readonly int[] SizeLeaks = { 8, 11, 15, 18, 6 };          // wrong-category spawns (04 section 4.3.5)
    public static readonly int[] SizePlayers = { 8, 16, 24, 32, 5 };
    public const int LEAK_COMEDIC_PERMILLE = 300;                        // D10
    public const int MIXED_SET_PERMILLE = 150;                           // 04 section 4.3.6
    public const int HIDING_SOCKET_PERMILLE = 250;

    // ---- density setting (G24): scales the per-room ranges below. The index is stored in the seed code's flags,
    // so 0 has to stay Normal. DensityOrder is the order on the start panel. 5, Minimal (Cor, playtest 2026-11-20: Sparse is
    // the default now, and one tier sparser than Very sparse).
    public static readonly string[] DensityName = { "Normal", "Sparse", "Dense", "Very sparse", "Very dense", "Minimal" };
    public static readonly int[] DensityPercent = { 100, 70, 135, 45, 200, 25 };
    public static readonly int[] DensityOrder = { 5, 3, 1, 0, 2, 4 };

    // ---- disguises: any prop on the map can be a disguise (G25: no copies rule, no quota pass). The only types
    // that are never a disguise are technical: fixed dressing (FL_FIXTURE), trees (FL_SCENERY), ground cover
    // (FL_DECOR) and anything longer than 2.5 m (Z3).
    public const int LEAK_ROOM_MAX = 2;            // wrong-category spawns per room
    public const int LOOSE_SHARE = 15;             // most of a room's floor props (%) that may be loose remainder (never fewer than 1 allowed)

    // ---- walkways (G26): every prop is solid, so every placement must leave its room's free floor in one piece
    public const int SEALED_POCKET = 4;     // largest piece of free floor (cells, 1 m2) a placement may cut off: the gap behind a sofa
    public const int SEALED_SHARE = 10;     // most of a room's cells (%) that may be cut off in all

    // ---- zones ----
    public const int Z_NONE = 0, Z_HOUSE = 1, Z_WAREHOUSE = 2, Z_DINER = 3, Z_LAWN = 4, Z_FOREST = 5, Z_RELEASE = 6;
    // revision 9 (the owner's "more zones and blobs"): a corner shop, a garage with its workshop, a farm's barn; a
    // park is a plot with no building (its cells are lawn, the plot's kind is Z_PARK)
    public const int Z_SHOP = 7, Z_GARAGE = 8, Z_BARN = 9, Z_PARK = 10;
    // revision 12, the whole-map themes (G58): now and then a seed's map is a space base or a medieval village. The
    // same generator lays it out; every prop is replaced by its theme twin (BuildThemeTwins), the buildings, the
    // ground, the fences and the sky take the theme's colours, and only the kinds of plot that suit it are drawn.
    public const int THEME_NONE = 0, THEME_SPACE = 1, THEME_MEDIEVAL = 2;
    public static readonly string[] ThemeName = { "", "Space base", "Medieval village" };
    public const int THEME_SPACE_PERMILLE = 333, THEME_MEDIEVAL_PERMILLE = 333;   // generator 17, Cor: three distinct themes, a third each (a mixed map was tried in 16 and removed)
    public static readonly int[][] ThemeKinds = { null,
        new[] { Z_DINER, Z_WAREHOUSE, Z_SHOP, Z_GARAGE },   // space: mess hall, cargo bay, supply depot, rover garage (revision 13: the park held back, G58: 54 models per place against 59)
        new[] { Z_DINER, Z_WAREHOUSE, Z_SHOP, Z_BARN, Z_PARK, Z_CABIN, Z_CHAPEL } };     // medieval: tavern, granary, market, farm, green, woodcutter's hut, chapel
    public const int Z_ZOO = 11, Z_CABIN = 12, Z_FAIR = 13, Z_CHAPEL = 14;   // revision 12: a zoo (no main building, like the park), a log cabin, a funfair, a chapel
    public static readonly string[] ZoneName = { "-", "House", "Warehouse", "Diner", "Lawn", "Forest", "Hunter release", "Shop", "Garage", "Farm", "Park", "Zoo", "Cabin", "Funfair", "Chapel" };
    public static readonly int[] ZoneWallMm = { 0, 2600, 2800, 2800, 0, 0, 0, 2800, 3000, 3400, 0, 0, 2700, 3200, 3800 };   // KayKit buildings at the reduced height (Z4 option R); a barn is taller

    public static bool IsBuilding(int zone) { return zone == Z_HOUSE || zone == Z_WAREHOUSE || zone == Z_DINER || zone == Z_SHOP || zone == Z_GARAGE || zone == Z_BARN || zone == Z_CABIN || zone == Z_FAIR || zone == Z_CHAPEL; }

    // ---- room types ----
    public const int R_LIVING = 1, R_KITCHEN = 2, R_DINING = 3, R_BEDROOM = 4, R_BATH = 5, R_OFFICE = 6, R_KIDS = 7, R_HALL = 8;
    public const int R_LAWN_OPEN = 10, R_GARDEN = 11, R_PATIO = 12, R_FIREPIT = 13, R_LANE = 14, R_YARD = 15;
    public const int R_STORAGE = 20, R_LOADING = 21, R_WH_OFFICE = 22;
    // revision 9: the shop (floor, stock room, back office), the garage (bay, parts room; its office is a back
    // office), the barn (hall, feed store); outdoors a forecourt (shop and garage), the farmyard, a field, a
    // paddock, a park
    public const int R_SHOP_FLOOR = 24, R_STOCKROOM = 25, R_BACK_OFFICE = 26, R_GARAGE_BAY = 27, R_PARTS = 28, R_BARN = 34, R_FEED_STORE = 35;
    public const int R_FORECOURT = 16, R_FARMYARD = 17, R_FIELD = 18, R_PADDOCK = 19, R_PARK = 42;
    // revision 10: the farm's workshop in the barn; the park's feature areas behind its lawn
    public const int R_FARM_WORK = 36, R_PLAYGROUND = 43, R_POND = 44, R_TERRACE = 45;
    // revision 11: a house's box room (the attic's things: the generator has one storey), teen room, playroom, pantry
    public const int R_BOXROOM = 9, R_TEEN = 37, R_PLAYROOM = 38, R_PANTRY = 39;
    // revision 12: the zoo (entrance plaza, keepers' walk, six kinds of enclosure, the keepers' yard) and the log cabin
    // (main room, bunk room; outdoors the porch, the wood yard, the campground)
    public const int R_ZOO_PLAZA = 46, R_ZOO_WALK = 47, R_ENC_FARM = 48, R_ENC_SAVANNA = 49, R_ENC_ARCTIC = 51, R_ENC_WOODS = 52, R_ENC_JUNGLE = 53, R_AVIARY = 54, R_KEEPER = 55;
    public const int R_CABIN_MAIN = 29, R_BUNKROOM = 33, R_PORCH = 56, R_WOODYARD = 57, R_CAMPSITE = 58;
    // the funfair: the arcade hall, its snack bar and prize store; outdoors the midway of stalls and the rides yard
    public const int R_ARCADE = 59, R_SNACKBAR = 60, R_PRIZE_STORE = 61, R_MIDWAY = 62, R_RIDES = 63;
    // the chapel: the nave and the vestry; the churchyard in front, the graveyard round it
    public const int R_NAVE = 64, R_VESTRY = 65, R_CHURCHYARD = 66, R_GRAVEYARD = 67;
    // revision 29 (Cor: outdoors is just rocks): a house garden's purposes besides the existing garden, fire pit, pond
    // and campsite: the picnic ground (tables with their benches) and the drying yard (racks in lines)
    public const int R_PICNIC = 68, R_DRYING = 69;
    // revision 30 (Cor: the outside areas with just trees or rocks are ok inside the fence): the lawn nobody needs, and
    // the funfair's ring past its rides yard, is scenery ground: walkable, planted with trees, bushes and rocks only
    // (no prop, no leak, no hider spawn)
    public const int R_SCENERY = 70;
    public static bool IsEnclosure(int r) { return (r >= R_ENC_FARM && r <= R_AVIARY && r != R_RELEASE); }
    public const int R_DINER_DINING = 30, R_DINER_KITCHEN = 31, R_DINER_STORE = 32;
    public const int R_FOREST_DENSE = 40, R_FOREST_CLEARING = 41;
    public const int R_RELEASE = 50;

    public static string RoomName(int r)
    {
        switch (r)
        {
            case R_LIVING: return "Living room";
            case R_KITCHEN: return "Kitchen";
            case R_DINING: return "Dining room";
            case R_BEDROOM: return "Bedroom";
            case R_BATH: return "Bathroom";
            case R_OFFICE: return "Office";
            case R_KIDS: return "Kids' room";
            case R_HALL: return "Hall";
            case R_LAWN_OPEN: return "Open lawn";
            case R_GARDEN: return "Garden bed";
            case R_PATIO: return "Patio";
            case R_FIREPIT: return "Fire pit";
            case R_STORAGE: return "Storage aisles";
            case R_LOADING: return "Loading dock";
            case R_WH_OFFICE: return "Warehouse office";
            case R_DINER_DINING: return "Diner seating";
            case R_DINER_KITCHEN: return "Diner kitchen";
            case R_DINER_STORE: return "Diner store room";
            case R_FOREST_DENSE: return "Dense forest";
            case R_FOREST_CLEARING: return "Forest camp";
            case R_PICNIC: return "Picnic ground";
            case R_SCENERY: return "Scenery ground";
            case R_DRYING: return "Drying yard";
            case R_LANE: return "Lane";
            case R_YARD: return "Yard";
            case R_SHOP_FLOOR: return "Shop floor";
            case R_STOCKROOM: return "Stock room";
            case R_BACK_OFFICE: return "Back office";
            case R_GARAGE_BAY: return "Workshop bay";
            case R_PARTS: return "Parts room";
            case R_BARN: return "Barn";
            case R_FEED_STORE: return "Feed store";
            case R_FORECOURT: return "Forecourt";
            case R_FARMYARD: return "Farmyard";
            case R_FIELD: return "Field";
            case R_PADDOCK: return "Paddock";
            case R_PARK: return "Park";
            case R_FARM_WORK: return "Farm workshop";
            case R_BOXROOM: return "Box room";
            case R_TEEN: return "Teen room";
            case R_PLAYROOM: return "Playroom";
            case R_PANTRY: return "Pantry";
            case R_ZOO_PLAZA: return "Zoo plaza";
            case R_ZOO_WALK: return "Keepers' walk";
            case R_ENC_FARM: return "Petting farm";
            case R_ENC_SAVANNA: return "Savanna";
            case R_ENC_ARCTIC: return "Arctic pool";
            case R_ENC_WOODS: return "Woodland pen";
            case R_ENC_JUNGLE: return "Jungle pen";
            case R_AVIARY: return "Aviary";
            case R_KEEPER: return "Keepers' yard";
            case R_CABIN_MAIN: return "Cabin room";
            case R_BUNKROOM: return "Bunk room";
            case R_PORCH: return "Porch";
            case R_WOODYARD: return "Wood yard";
            case R_CAMPSITE: return "Campground";
            case R_ARCADE: return "Arcade hall";
            case R_SNACKBAR: return "Snack bar";
            case R_PRIZE_STORE: return "Prize store";
            case R_MIDWAY: return "Midway";
            case R_RIDES: return "Rides yard";
            case R_NAVE: return "Nave";
            case R_VESTRY: return "Vestry";
            case R_CHURCHYARD: return "Churchyard";
            case R_GRAVEYARD: return "Graveyard";
            case R_PLAYGROUND: return "Skate corner";
            case R_POND: return "Pond";
            case R_TERRACE: return "Cafe terrace";
            case R_RELEASE: return "Hunter release";
        }
        return "Room";
    }

    // ---- inner-cell tags (04 section 4.2) ----
    public const int T_FLOOR = 1 << 0, T_BLOCKED = 1 << 1, T_WALL_N = 1 << 2, T_WALL_E = 1 << 3, T_WALL_S = 1 << 4, T_WALL_W = 1 << 5;
    public const int T_CORNER = 1 << 6, T_CENTER = 1 << 7, T_DOOR_CLEAR = 1 << 8, T_PATH = 1 << 9, T_OUTDOOR = 1 << 10, T_COVERED = 1 << 11;
    public const int T_WINDOW_WALL = 1 << 12, T_EDGE_OUTDOOR = 1 << 13, T_SOFT_GROUND = 1 << 14, T_NO_SPAWN = 1 << 15;
    public const int T_BY_BUILDING = 1 << 16;   // outdoor cell against a building wall (not a fence)
    public const int T_SWING = 1 << 17;
    public const int T_DOOR_SIDE = 1 << 18;     // against the wall right beside a doorway's keep-clear cells
    public const int T_GLADE = 1 << 19;         // a clearing: no trees
    public const int T_EARTH = 1 << 20;         // trodden earth around a fire
    public const int T_WATER = 1 << 21;         // a pond's water (revision 10): no prop stands on it but lilies and ducks
    public const int T_WALL_ANY = T_WALL_N | T_WALL_E | T_WALL_S | T_WALL_W;
    // T_SOFT_GROUND: forest floor, and the planting beds of a garden. T_COVERED: the paved part of a patio.
    // T_PATH: the walkway between a room's doors; outdoors the approach to a building door and a forest path.

    // ---- edges between outer cells ----
    public const int E_NONE = 0, E_WALL = 1, E_DOOR = 2, E_WINDOW = 3, E_FENCE = 4;

    // ---- anchors, facing ----
    public const int A_WALL = 0, A_CORNER = 1, A_CENTER = 2, A_ANY = 3, A_OUT_OPEN = 4, A_OUT_EDGE = 5, A_SOFT = 6, A_COVERED = 7, A_SLOT_ONLY = 8;
    public const int A_BESIDE = 9;      // next to an object of one of the def's "beside" kinds, in line with it
    public const int A_DOOR_SIDE = 10;  // against the wall beside a doorway
    public const int A_BUILDING = 11;   // outdoors, against a building wall
    public const int F_AWAY_WALL = 0, F_TO_CENTER = 1, F_ALIGN_GRID = 2, F_RANDOM = 3;

    // ---- item tags (slots accept by tag) ----
    public const int I_CHAIR = 1 << 0, I_SETTING = 1 << 1, I_CENTERPIECE = 1 << 2, I_RUG = 1 << 3, I_LAMP = 1 << 4, I_DECOR = 1 << 5;
    public const int I_SCREEN = 1 << 6, I_KITCHENWARE = 1 << 7, I_FOOD = 1 << 8, I_BOX = 1 << 9, I_CRATE = 1 << 10, I_PILLOW = 1 << 11;
    public const int I_TOY = 1 << 12, I_BEDROLL = 1 << 13, I_DESKCHAIR = 1 << 14, I_TABLEWARE = 1 << 15, I_STOOL = 1 << 16, I_TOOL = 1 << 17;
    public const int I_SEAT_OUT = 1 << 18, I_MUSHROOM = 1 << 19;
    public const int I_STORE = 1 << 20;   // warehouse goods (buckets, jerrycans, rolls, bars): on racks, pallets and workbenches, not on a bookcase
    public const int I_CHOP = 1 << 21;    // the axe: the one tool that sticks in a stump
    public const int I_COOK = 1 << 22;    // pots and pans: on a hob
    public const int I_LINEN = 1 << 23;   // towels: on a bathroom cabinet, a washer or a dryer

    // ---- room themes (report 15, M4 and M5): an item that sits on, in or under furniture only appears in rooms of
    // its themes (0 = anywhere). Wrong-category spawns ignore them.
    public const int TH_LIVING = 1 << 0, TH_KITCHEN = 1 << 1, TH_DINING = 1 << 2, TH_BED = 1 << 3, TH_BATH = 1 << 4, TH_OFFICE = 1 << 5;
    public const int TH_KIDS = 1 << 6, TH_HALL = 1 << 7, TH_STORE = 1 << 8, TH_DINER = 1 << 9, TH_PRO_KITCHEN = 1 << 10, TH_PANTRY = 1 << 11;
    public const int TH_PATIO = 1 << 12, TH_GARDEN = 1 << 13, TH_CAMP = 1 << 14, TH_FOREST = 1 << 15;
    public const int TH_SHOP = 1 << 16, TH_FARM = 1 << 17;
    public const int TH_TEEN = 1 << 18, TH_PLAY = 1 << 19, TH_ATTIC = 1 << 20;   // revision 11
    public const int TH_ZOO = 1 << 21, TH_CABIN = 1 << 22, TH_FAIR = 1 << 23;      // revision 12
    // revision 12, seasonal decorations: a theme no room has; a map with that holiday adds it to every room's theme, so
    // the holiday's small things fill slots only then
    public const int TH_WINTER = 1 << 24, TH_HALLOWEEN = 1 << 25;
    public const int TH_CHAPEL = 1 << 26;
    public const int TH_HOME = TH_LIVING | TH_BED | TH_OFFICE | TH_KIDS | TH_HALL | TH_DINING | TH_TEEN | TH_PLAY | TH_ATTIC;

    public static int RoomTheme(int roomType)
    {
        switch (roomType)
        {
            case R_LIVING: return TH_LIVING;
            case R_KITCHEN: return TH_KITCHEN;
            case R_DINING: return TH_DINING;
            case R_BEDROOM: return TH_BED;
            case R_BATH: return TH_BATH;
            case R_OFFICE: case R_WH_OFFICE: case R_BACK_OFFICE: return TH_OFFICE;
            case R_SHOP_FLOOR: return TH_SHOP;
            case R_STOCKROOM: return TH_SHOP | TH_STORE;
            case R_GARAGE_BAY: case R_PARTS: case R_FORECOURT: return TH_STORE;
            case R_BARN: case R_FEED_STORE: case R_FARMYARD: case R_FIELD: case R_PADDOCK: return TH_FARM;
            case R_PARK: case R_POND: case R_PLAYGROUND: return TH_GARDEN;
            case R_TERRACE: return TH_PATIO | TH_DINER;
            case R_FARM_WORK: return TH_FARM | TH_STORE;
            case R_TEEN: return TH_TEEN | TH_BED;
            case R_PLAYROOM: return TH_PLAY | TH_KIDS;
            case R_BOXROOM: return TH_ATTIC | TH_STORE;
            case R_PANTRY: return TH_PANTRY;
            case R_ZOO_PLAZA: return TH_ZOO | TH_PATIO | TH_DINER;
            case R_ZOO_WALK: case R_ENC_FARM: case R_ENC_SAVANNA: case R_ENC_ARCTIC: case R_ENC_WOODS: case R_ENC_JUNGLE: case R_AVIARY: return TH_ZOO;
            case R_KEEPER: return TH_ZOO | TH_FARM | TH_STORE;
            case R_CABIN_MAIN: return TH_CABIN | TH_LIVING | TH_KITCHEN | TH_DINING;
            case R_BUNKROOM: return TH_CABIN | TH_BED;
            case R_PORCH: return TH_CABIN | TH_PATIO;
            case R_WOODYARD: return TH_CABIN | TH_STORE | TH_CAMP;
            case R_CAMPSITE: return TH_CAMP | TH_CABIN;
            case R_ARCADE: case R_MIDWAY: case R_RIDES: return TH_FAIR;
            case R_SNACKBAR: return TH_FAIR | TH_DINER | TH_PRO_KITCHEN;
            case R_PRIZE_STORE: return TH_FAIR | TH_STORE;
            case R_NAVE: case R_CHURCHYARD: case R_GRAVEYARD: return TH_CHAPEL;
            case R_VESTRY: return TH_CHAPEL | TH_OFFICE;
            case R_KIDS: return TH_KIDS;
            case R_HALL: return TH_HALL;
            case R_STORAGE: case R_LOADING: case R_YARD: return TH_STORE;
            case R_DINER_DINING: return TH_DINER;
            case R_DINER_KITCHEN: return TH_PRO_KITCHEN;
            case R_DINER_STORE: return TH_PANTRY;
            case R_PATIO: return TH_PATIO;
            case R_PICNIC: return TH_PATIO | TH_GARDEN;
            case R_DRYING: return TH_GARDEN | TH_STORE;
            case R_LAWN_OPEN: case R_GARDEN: case R_LANE: return TH_GARDEN;
            case R_FIREPIT: case R_FOREST_CLEARING: return TH_CAMP;
            case R_FOREST_DENSE: return TH_FOREST;
        }
        return 0;
    }

    // ---- def flags ----
    public const int FL_SOLID = 1 << 0;      // furniture: also blocks the third-person camera and bots' sight. Every prop blocks movement (G26)
    public const int FL_FIXTURE = 1 << 1;    // fixed dressing: never a disguise, never moves
    public const int FL_SCENERY = 1 << 2;    // trees: architecture layer, never a disguise
    public const int FL_FLAT = 1 << 3;       // rugs: take no floor cells
    public const int FL_DECOR = 1 << 4;      // grass tufts: ground cover, not a prop (no collider, never a disguise)
    public const int FL_SEEINTO = 1 << 5;    // open shelves, tents
    public const int FL_COMMON = 1 << 6;     // common small type (unused since the copies rule was dropped)
    public const int FL_HEAVY = 1 << 7;      // STATIC_HEAVY: a dart does not move it
    public const int FL_LIGHT = 1 << 8;      // LIGHT: flies further
    public const int FL_LEAK_IN = 1 << 9;    // may leak into indoor rooms
    public const int FL_LEAK_OUT = 1 << 10;  // may leak into outdoor rooms
    public const int FL_OPENABLE = 1 << 11;  // has door or lid parts
    public const int FL_GENERATED = 1 << 12; // built in-house from simple shapes (Z5)
    public const int FL_LOOSE = 1 << 13;     // small loose thing: never alone in the open, only at a wall, beside its kinds or in a group (G25)

    // ---- what an object is to its neighbours ("appears with", G25): defKind says what it is, defBeside what it stands next to
    public const int K_TREE = 1 << 0, K_SEAT = 1 << 1, K_TABLE = 1 << 2, K_WORK = 1 << 3, K_FIRE = 1 << 4, K_STORE = 1 << 5;
    public const int K_TENT = 1 << 6, K_STUMP = 1 << 7, K_PLANT = 1 << 8, K_BED = 1 << 9, K_COUNTER = 1 << 10, K_WET = 1 << 11, K_TV = 1 << 12;

    // ---- group layouts (G25): ROW = side by side with their backs on one line, CLUSTER = two short rows, a clump
    public const int GL_ROW = 0, GL_CLUSTER = 1;

    // ---- collider kinds (the def's collider set, 05 N16, simplified to boxes) ----
    public const int C_BOX = 0, C_TABLE = 1, C_SEAT = 2, C_HOLLOW = 3, C_SHELF = 4, C_NONE = 5, C_TRUNK = 6, C_TENT = 7;

    // ---- slot types, sizes, group modes ----
    public const int S_ON_TOP = 0, S_ADJACENT = 1, S_INSIDE = 2, S_UNDER = 3;
    public const int SZ_XS = 0, SZ_S = 1, SZ_M = 2, SZ_L = 3, SZ_XL = 4;
    public const int G_INDEPENDENT = 0, G_MATCHED = 1, G_MIRROR_OF = 2, G_ELSE_OF = 3;

    // ---- object definitions ----
    const int MAXD = 1024;
    public static int nDefs;
    public static string[] defId = new string[MAXD];
    public static int[] defModel = new int[MAXD];
    public static int[] defScale = new int[MAXD];      // per mille; one scale per def, everywhere (04 section 4.3.6)
    public static int[] defSx = new int[MAXD], defSy = new int[MAXD], defSz = new int[MAXD];   // size in mm at that scale
    public static int[] defW = new int[MAXD], defD = new int[MAXD];                            // footprint in inner cells
    public static int[] defTopY = new int[MAXD];       // height of the main top surface, mm
    public static int[] defTags = new int[MAXD];
    public static int[] defFlags = new int[MAXD];
    public static int[] defAnchor = new int[MAXD], defFacing = new int[MAXD], defClear = new int[MAXD], defSpacing = new int[MAXD];
    public static int[] defJitPos = new int[MAXD], defJitYaw = new int[MAXD];
    public static int[] defDisguise = new int[MAXD];   // 0 not a disguise, 1 tiny, 2 medium, 3 large
    public static int[] defCanopyY = new int[MAXD];    // trees: the height (mm) at which the crown begins; below it only the trunk is in the way (G31)
    public static int[] defThemes = new int[MAXD];     // rooms in which the type appears on, in or under furniture (0 = anywhere)
    public static int[] defLeakWeight = new int[MAXD], defAbsurd = new int[MAXD];
    public static int[] defCollider = new int[MAXD];
    public static int[] defSizeClass = new int[MAXD];
    public static int[] defSlotWeight = new int[MAXD];
    public static int[] defFamily = new int[MAXD];     // 0 Kenney, 1 KayKit, 2 generated/composite
    public static int[] defMassKg = new int[MAXD];
    public static int[] defSlotStart = new int[MAXD], defSlotCount = new int[MAXD];
    public static int[] defGroupStart = new int[MAXD], defGroupCount = new int[MAXD];
    public static int[] defKind = new int[MAXD], defBeside = new int[MAXD];
    public static int[] defRoomCap = new int[MAXD];    // copies of the type standing on the floor of one room (16 m2; larger rooms scale it)
    public static int[] defKin = new int[MAXD];        // the prop group the type is a kind of (a hider bucket fits any row of buckets), -1 = none
    public static int[] defTwin = new int[MAXD];       // revision 11: the type's twin in a KayKit-furnished house, -1 = none
    public static int[] defSeason = new int[MAXD * 3];
    public static int[] defThemeTwin = new int[MAXD * 3]; // revision 12: per def and map theme its twin, -1 = itself
    public static int[] defMapTheme = new int[MAXD];      // revision 12: a map theme's own piece (THEME_*), 0 = any map // revision 9: per def and season (0 summer, 1 autumn, 2 deep green) its twin, -1 = itself

    // ---- prop groups (G25): related props placed together as one tidy unit
    const int MAXGR = 96, MAXGM = 512;
    public static int nGrps;
    public static string[] grpId = new string[MAXGR];
    public static int[] grpLayout = new int[MAXGR], grpAnchor = new int[MAXGR], grpMin = new int[MAXGR], grpMax = new int[MAXGR];
    public static int[] grpGapMm = new int[MAXGR], grpBeside = new int[MAXGR], grpMemberStart = new int[MAXGR], grpMemberCount = new int[MAXGR];
    public static int nGrpMembers;
    public static int[] grpMemberDef = new int[MAXGM], grpMemberWeight = new int[MAXGM];

    // ---- prop counts per room type (G24), per 100 m2 of the room's floor, at Normal density
    public static int[] rangeFloorMin = new int[128], rangeFloorMax = new int[128];   // props standing on the floor
    public static int[] rangeTopMax = new int[128];                                  // props on, in or under furniture (a cap)
    public static int[] rangeTreeMin = new int[128], rangeTreeMax = new int[128];     // trees (scenery, not props)
    public static int[] rangeDecorMin = new int[128], rangeDecorMax = new int[128];   // grass tufts (ground cover, not props)
    public static int[] rangeCover = new int[128];   // most of the room's floor (%) that props and trees may block; not per 100 m2
    public static int[] rangeFloorAbs = new int[128]; // outdoor areas (G29): the fewest floor props one area of the type gets, whatever its size

    const int MAXS = 1024;
    public static int nSlots;
    public static int[] slotType = new int[MAXS], slotX = new int[MAXS], slotY = new int[MAXS], slotZ = new int[MAXS];
    public static int[] slotYaw = new int[MAXS], slotSize = new int[MAXS], slotAccept = new int[MAXS], slotFill = new int[MAXS];
    public static int[] slotGroup = new int[MAXS];
    public static bool[] slotHiding = new bool[MAXS];
    const int MAXG = 512;
    public static int nGroups;
    public static int[] groupMode = new int[MAXG], groupChance = new int[MAXG], groupRef = new int[MAXG];

    // ---- native lists: what belongs in a room type (the plans place it; everything else is a wrong-category spawn) ----
    const int MAXT = 128, MAXE = 4096;   // revision 12: 128 tables (was 64)
    public const int MAX_TABLES = MAXT;
    public static int nTables;
    public static int[] tableZone = new int[MAXT], tableRoom = new int[MAXT];
    public static int[] tableEntryStart = new int[MAXT], tableEntryCount = new int[MAXT];
    public static int nEntries;
    public static int[] entryDef = new int[MAXE];      // a type that is native to the room type
    public static int[] entryGrp = new int[MAXE];      // or a prop group whose kinds all are, -1 = a single def (entryDef)


    static bool built;
    static int cur;

    // ------------------------------------------------------------------ helpers
    public static int Model(string name)
    {
        for (int i = 0; i < GenModels.Name.Length; i++)
            if (GenModels.Name[i] == name) return i;
        throw new System.Exception("GenData: unknown model " + name);
    }

    public static int FindDef(string id)
    {
        for (int i = 0; i < nDefs; i++)
            if (defId[i] == id) return i;
        throw new System.Exception("GenData: unknown def " + id);
    }

    static int Cells(int mm)
    {
        int c = (mm - 100 + INNER_MM - 1) / INNER_MM;
        return c < 1 ? 1 : c;
    }

    // Height of the main top surface of a model at scale 1 (mm): the highest up-facing surface that has at
    // least half the area of the largest one.
    static int ModelTop(int model)
    {
        int a = GenModels.SurfStart[model], b = GenModels.SurfStart[model + 1];
        if (a == b) return GenModels.SizeMm[model * 3 + 1];
        int maxArea = 0;
        for (int i = a; i < b; i++) if (GenModels.Surf[i * 6 + 5] > maxArea) maxArea = GenModels.Surf[i * 6 + 5];
        int top = 0;
        for (int i = a; i < b; i++)
            if (GenModels.Surf[i * 6 + 5] * 2 >= maxArea && GenModels.Surf[i * 6] > top) top = GenModels.Surf[i * 6];
        return top;
    }

    static int D(string id, string model, int scale, int tags, int anchor, int flags, int absurd)
    {
        int d = nDefs++;
        cur = d;
        int m = Model(model);
        defId[d] = id; defModel[d] = m; defScale[d] = scale;
        defSx[d] = GenModels.SizeMm[m * 3] * scale / 1000;
        defSy[d] = GenModels.SizeMm[m * 3 + 1] * scale / 1000;
        defSz[d] = GenModels.SizeMm[m * 3 + 2] * scale / 1000;
        defW[d] = Cells(defSx[d]); defD[d] = Cells(defSz[d]);
        defTopY[d] = ModelTop(m) * scale / 1000;
        defTags[d] = tags; defFlags[d] = flags; defAnchor[d] = anchor;
        defFacing[d] = (anchor == A_WALL || anchor == A_OUT_EDGE) ? F_AWAY_WALL : (anchor == A_CORNER ? F_TO_CENTER : (anchor == A_CENTER ? F_ALIGN_GRID : F_RANDOM));
        defClear[d] = 0; defSpacing[d] = 0;
        defJitPos[d] = 4; defJitYaw[d] = (anchor == A_WALL || anchor == A_CENTER) ? 3 : 12;
        defAbsurd[d] = absurd;
        defLeakWeight[d] = ((flags & (FL_LEAK_IN | FL_LEAK_OUT)) != 0) ? 10 : 0;
        defCollider[d] = ((flags & FL_FLAT) != 0 || (flags & FL_DECOR) != 0) ? C_NONE : C_BOX;
        string pack = GenModels.Pack[m];
        defFamily[d] = pack.StartsWith("kenney") ? 0 : ((pack == "generated" || pack.IndexOf('+') >= 0) ? 2 : 1);
        if (pack == "generated") defFlags[d] |= FL_GENERATED;
        int longest = defSx[d] > defSz[d] ? defSx[d] : defSz[d];
        if (defSy[d] > longest) longest = defSy[d];
        defSizeClass[d] = longest < 100 ? SZ_XS : (longest < 300 ? SZ_S : (longest < 600 ? SZ_M : (longest < 1000 ? SZ_L : SZ_XL)));
        defSlotWeight[d] = 10;
        // disguise class (04 section 4.3.1; G46 replaces Z3's 2.5 m limit): every object a player can point at is a
        // disguise, fixtures and trees included; only ground cover (grass tufts, stepping stones: FL_DECOR, which
        // has no collider and cannot be pointed at) is not. 1 tiny, 2 medium, 3 large, 4 huge (over 2.5 m: trees,
        // the open tent, the long rug)
        bool can = (flags & FL_DECOR) == 0;
        if (!can) defDisguise[d] = 0;
        else if (longest < 350) defDisguise[d] = 1;
        else if (longest > 2500) defDisguise[d] = 4;
        else if (longest >= 1800 || defSy[d] >= 1600) defDisguise[d] = 3;
        else defDisguise[d] = 2;
        defThemes[d] = 0; defCanopyY[d] = 0;
        defKind[d] = 0; defBeside[d] = 0; defKin[d] = -1;
        defRoomCap[d] = 2;
        // rough mass for knocks: volume of the bounding box at 35 kg/m3 (furniture is mostly air), clamped
        long vol = (long)defSx[d] * defSy[d] / 1000 * defSz[d] / 1000;   // litres
        int kg = (int)(vol * 35 / 1000);
        defMassKg[d] = kg < 1 ? 1 : (kg > 40 ? 40 : kg);
        defSlotStart[d] = nSlots; defSlotCount[d] = 0;
        defGroupStart[d] = nGroups; defGroupCount[d] = 0;
        return d;
    }

    static void Col(int kind) { defCollider[cur] = kind; }
    static void Clear(int cells) { defClear[cur] = cells; }
    static void Spacing(int cells) { defSpacing[cur] = cells; }
    static void Facing(int f) { defFacing[cur] = f; }
    static void Jit(int posCm, int yawDeg) { defJitPos[cur] = posCm; defJitYaw[cur] = yawDeg; }
    static void Fp(int w, int d) { defW[cur] = w; defD[cur] = d; }
    static void LeakW(int w) { defLeakWeight[cur] = w; }
    static void SlotW(int w) { defSlotWeight[cur] = w; }
    static void NoDisguise() { defDisguise[cur] = 0; }
    // signs and mailboxes stand only where a plan puts them, at a front or an entrance (G42): never out of place
    static void NoLeak() { defLeakWeight[cur] = 0; }
    static void Kind(int kinds) { defKind[cur] = kinds; }
    static void Beside(int kinds) { defBeside[cur] = kinds; }
    static void Cap(int perRoom) { defRoomCap[cur] = perRoom; }
    static void Loose() { defFlags[cur] |= FL_LOOSE; }
    static void Th(int themes) { defThemes[cur] = themes; }
    static void Canopy(int mm) { defCanopyY[cur] = mm; }
    static void ThOf(string id, int themes) { defThemes[FindDef(id)] = themes; }

    // A prop group: id, layout, where it stands, how many members, the gap between them (mm), then the member
    // defs. Members are drawn without repeating a def while there are unused ones (different kinds side by side).
    static void Grp(string id, int layout, int anchor, int min, int max, int gapMm, params string[] members)
    {
        int g = nGrps++;
        grpId[g] = id; grpLayout[g] = layout; grpAnchor[g] = anchor; grpMin[g] = min; grpMax[g] = max; grpGapMm[g] = gapMm;
        grpBeside[g] = 0;
        grpMemberStart[g] = nGrpMembers; grpMemberCount[g] = members.Length;
        for (int i = 0; i < members.Length; i++)
        {
            int d = FindDef(members[i]);
            grpMemberDef[nGrpMembers] = d; grpMemberWeight[nGrpMembers] = 10; nGrpMembers++;
            if (defKin[d] < 0 && members.Length > 1) defKin[d] = g;
        }
    }

    static void GrpBeside(int kinds) { grpBeside[nGrps - 1] = kinds; }

    public static int FindGrp(string id)
    {
        for (int i = 0; i < nGrps; i++)
            if (grpId[i] == id) return i;
        throw new System.Exception("GenData: unknown prop group " + id);
    }

    public static bool GrpHas(int g, int def)
    {
        for (int i = grpMemberStart[g]; i < grpMemberStart[g] + grpMemberCount[g]; i++)
            if (grpMemberDef[i] == def) return true;
        return false;
    }

    // one row of the range table: room type, floor props min and max, on-furniture max, then optional trees and grass
    static void Range(int room, int floorMin, int floorMax, int topMax, int cover) { Range(room, floorMin, floorMax, topMax, cover, 0, 0, 0, 0); }

    static void Range(int room, int floorMin, int floorMax, int topMax, int cover, int treeMin, int treeMax, int decorMin, int decorMax)
    {
        rangeFloorMin[room] = floorMin; rangeFloorMax[room] = floorMax; rangeTopMax[room] = topMax; rangeCover[room] = cover;
        rangeTreeMin[room] = treeMin; rangeTreeMax[room] = treeMax; rangeDecorMin[room] = decorMin; rangeDecorMax[room] = decorMax;
    }

    static int Group(int mode, int chance, int refGroup)
    {
        int g = nGroups++;
        groupMode[g] = mode; groupChance[g] = chance; groupRef[g] = refGroup;
        defGroupCount[cur]++;
        return g - defGroupStart[cur];
    }

    static void Slot(int group, int type, int x, int y, int z, int yaw, int size, int accept, int fill)
    {
        int s = nSlots++;
        slotType[s] = type; slotX[s] = x; slotY[s] = y; slotZ[s] = z; slotYaw[s] = yaw;
        slotSize[s] = size; slotAccept[s] = accept; slotFill[s] = fill; slotGroup[s] = group; slotHiding[s] = false;
        defSlotCount[cur]++;
    }

    static void Hiding() { slotHiding[nSlots - 1] = true; }

    // A dining table: chairs on the floor next to it (MATCHED), place settings that follow the chairs
    // (MIRROR_OF), a centrepiece and a rug under it (04 section 4.4.2).
    static void SlotsDining(bool fourSides, int chairFill)
    {
        int d = cur;
        int sx = defSx[d], sz = defSz[d], top = defTopY[d];
        int gChairs = Group(G_MATCHED, 970, 0);
        int gSet = Group(G_MIRROR_OF, 1000, gChairs);
        int gMisc = Group(G_INDEPENDENT, 1000, 0);
        int reach = 230;   // chair centre this far outside the table edge
        int inset = 190;   // setting centre this far inside the table edge
        if (!fourSides)
        {
            int[] xs = { -sx / 4, sx / 4 };
            for (int i = 0; i < 2; i++)
            {
                Slot(gChairs, S_ADJACENT, xs[i], 0, sz / 2 + reach, 180, SZ_XL, I_CHAIR, chairFill); Hiding();
                Slot(gChairs, S_ADJACENT, xs[i], 0, -sz / 2 - reach, 0, SZ_XL, I_CHAIR, chairFill);
            }
            for (int i = 0; i < 2; i++)
            {
                Slot(gSet, S_ON_TOP, xs[i], top, sz / 2 - inset, 0, SZ_M, I_SETTING, 1000);
                Slot(gSet, S_ON_TOP, xs[i], top, -sz / 2 + inset, 180, SZ_M, I_SETTING, 1000);
            }
        }
        else
        {
            Slot(gChairs, S_ADJACENT, 0, 0, sz / 2 + reach, 180, SZ_XL, I_CHAIR, chairFill); Hiding();
            Slot(gChairs, S_ADJACENT, 0, 0, -sz / 2 - reach, 0, SZ_XL, I_CHAIR, chairFill);
            Slot(gChairs, S_ADJACENT, sx / 2 + reach, 0, 0, 270, SZ_XL, I_CHAIR, chairFill);
            Slot(gChairs, S_ADJACENT, -sx / 2 - reach, 0, 0, 90, SZ_XL, I_CHAIR, chairFill);
            Slot(gSet, S_ON_TOP, 0, top, sz / 2 - inset, 0, SZ_M, I_SETTING, 1000);
            Slot(gSet, S_ON_TOP, 0, top, -sz / 2 + inset, 180, SZ_M, I_SETTING, 1000);
            Slot(gSet, S_ON_TOP, sx / 2 - inset, top, 0, 90, SZ_M, I_SETTING, 1000);
            Slot(gSet, S_ON_TOP, -sx / 2 + inset, top, 0, 270, SZ_M, I_SETTING, 1000);
        }
        Slot(gMisc, S_ON_TOP, 0, top, 0, 0, SZ_M, I_CENTERPIECE | I_FOOD, 600);
        Slot(gMisc, S_UNDER, 0, 0, 0, 0, SZ_XL, I_RUG, 300);
    }

    // n places along the top, left to right
    static void SlotsTop(int n, int accept, int size, int fill)
    {
        int d = cur;
        int g = Group(G_INDEPENDENT, 1000, 0);
        for (int i = 0; i < n; i++)
        {
            int x = (2 * i + 1) * defSx[d] * 8 / 10 / (2 * n) - defSx[d] * 4 / 10;
            Slot(g, S_ON_TOP, x, defTopY[d], 0, 0, size, accept, fill);
        }
    }

    // places on every shelf board below the top (open bookcases, racks)
    static void SlotsShelves(int perShelf, int accept, int fill, bool alsoTop)
    {
        int d = cur, m = defModel[d], sc = defScale[d];
        int g = Group(G_INDEPENDENT, 1000, 0);
        int a = GenModels.SurfStart[m], b = GenModels.SurfStart[m + 1];
        int maxArea = 0;
        for (int i = a; i < b; i++) if (GenModels.Surf[i * 6 + 5] > maxArea) maxArea = GenModels.Surf[i * 6 + 5];
        // boards sorted by height
        int[] ys = new int[8];
        int n = 0;
        for (int i = a; i < b; i++)
        {
            if (GenModels.Surf[i * 6 + 5] * 2 < maxArea) continue;
            int y = GenModels.Surf[i * 6] * sc / 1000;
            if (!alsoTop && y >= defTopY[d] - 20 && y >= defSy[d] - 150) continue;
            int k = n++;
            while (k > 0 && ys[k - 1] > y) { ys[k] = ys[k - 1]; k--; }
            ys[k] = y;
        }
        bool first = true;
        for (int i = 0; i < n; i++)
        {
            int gap = (i + 1 < n ? ys[i + 1] : defSy[d]) - ys[i];
            int size = gap >= 620 ? SZ_L : (gap >= 330 ? SZ_M : (gap >= 150 ? SZ_S : SZ_XS));
            if (i == n - 1 && alsoTop) size = SZ_M;
            for (int k = 0; k < perShelf; k++)
            {
                int x = (2 * k + 1) * defSx[d] * 8 / 10 / (2 * perShelf) - defSx[d] * 4 / 10;
                Slot(g, S_INSIDE, x, ys[i], 0, 0, size, accept, fill);
                if (first) { Hiding(); first = false; }
            }
        }
    }

    // ------------------------------------------------------------------ tables and pieces
    static int Table(int zone, int room)
    {
        int t = nTables++;
        tableZone[t] = zone; tableRoom[t] = room;
        tableEntryStart[t] = nEntries; tableEntryCount[t] = 0;
        return t;
    }

    public static int FindTable(int zone, int room)
    {
        for (int t = 0; t < nTables; t++)
            if (tableZone[t] == zone && tableRoom[t] == room) return t;
        for (int t = 0; t < nTables; t++)
            if (tableZone[t] == zone) return t;   // per-zone fallback
        return -1;
    }

    // ------------------------------------------------------------------ the content
    public static void Build()
    {
        if (built) return;
        built = true;
        for (int i = 0; i < defSeason.Length; i++) defSeason[i] = -1;
        for (int i = 0; i < defTwin.Length; i++) defTwin[i] = -1;
        for (int i = 0; i < defThemeTwin.Length; i++) defThemeTwin[i] = -1;

        const int IN = FL_LEAK_IN, OUT = FL_LEAK_OUT, BOTH = FL_LEAK_IN | FL_LEAK_OUT;
        const int K = 2000;   // Kenney Furniture Kit (02 section 4.1)
        const int F = 300;    // Kenney Food Kit

        // ===== Kenney Furniture Kit (House) =====
        D("k_table", "table", K, 0, A_CENTER, FL_SOLID | IN, 1); Col(C_TABLE); SlotsDining(false, 900); Kind(K_TABLE);
        D("k_tableCoffee", "tableCoffee", K, 0, A_CENTER, FL_SOLID | IN, 1); Col(C_TABLE); SlotsTop(2, I_DECOR | I_FOOD | I_TABLEWARE, SZ_M, 450); Kind(K_TABLE);
        D("k_chair", "chair", K, I_CHAIR, A_SLOT_ONLY, FL_COMMON | FL_LIGHT | BOTH, 0); Col(C_SEAT);
        D("k_chairCushion", "chairCushion", K, I_CHAIR, A_SLOT_ONLY, FL_COMMON | FL_LIGHT | BOTH, 0); Col(C_SEAT);
        D("k_chairDesk", "chairDesk", K, I_DESKCHAIR, A_SLOT_ONLY, FL_LIGHT | IN, 1); Col(C_SEAT); Fp(1, 1);
        D("k_sofa", "loungeSofa", K, 0, A_WALL, FL_SOLID | BOTH, 2); Col(C_SEAT); Clear(1); Kind(K_SEAT);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, -defSx[cur] / 4, defTopY[cur], 60, 0, SZ_M, I_PILLOW | I_TOY, 350); Slot(g, S_ON_TOP, defSx[cur] / 4, defTopY[cur], 60, 0, SZ_M, I_PILLOW | I_TOY, 350); }
        D("k_loungeChair", "loungeChair", K, 0, A_WALL, FL_SOLID | BOTH, 1); Col(C_SEAT); Clear(1); Kind(K_SEAT); Cap(2);
        // revision 9: more of the Furniture Kit's seats, so two living rooms need not have the same sofa and chairs
        D("k_designSofa", "loungeDesignSofa", K, 0, A_WALL, FL_SOLID | BOTH, 2); Col(C_SEAT); Clear(1); Kind(K_SEAT);
        D("k_designChair", "loungeDesignChair", K, 0, A_WALL, FL_SOLID | BOTH, 1); Col(C_SEAT); Clear(1); Kind(K_SEAT); Cap(2);
        D("k_recliner", "loungeChairRelax", K, 0, A_WALL, FL_SOLID | BOTH, 1); Col(C_SEAT); Clear(1); Kind(K_SEAT); Cap(2);
        D("k_bedDouble", "bedDouble", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Kind(K_BED);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defTopY[cur], 300, 0, SZ_M, I_TOY | I_PILLOW, 200); }
        D("k_bedSingle", "bedSingle", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Kind(K_BED);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defTopY[cur], 300, 0, SZ_M, I_TOY | I_PILLOW, 250); }
        D("k_bookcase", "bookcaseOpen", K, 0, A_WALL, FL_SOLID | FL_HEAVY | FL_SEEINTO | IN, 1); Col(C_SHELF); Clear(1); SlotsShelves(2, I_DECOR | I_BOX | I_TOY, 550, false); Kind(K_STORE);
        D("k_bookcaseLow", "bookcaseOpenLow", K, 0, A_WALL, FL_SOLID | FL_SEEINTO | IN, 1); Col(C_SHELF); Clear(1); SlotsShelves(2, I_DECOR | I_BOX | I_TOY, 500, true); Kind(K_STORE);
        D("k_tvCabinet", "cabinetTelevision", K, 0, A_WALL, FL_SOLID | IN, 1); Clear(2); Kind(K_TV);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defTopY[cur], 0, 0, SZ_XL, I_SCREEN, 950); Slot(g, S_ON_TOP, -defSx[cur] * 4 / 10, defTopY[cur], 0, 0, SZ_S, I_DECOR, 300); }
        D("k_tv", "televisionModern", K, I_SCREEN, A_SLOT_ONLY, BOTH, 2);
        D("k_tvVintage", "televisionVintage", K, I_SCREEN, A_SLOT_ONLY, BOTH, 2);
        D("k_fridge", "kitchenFridge", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(2); Kind(K_STORE);
        D("k_stove", "kitchenStove", K, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(2); Kind(K_COUNTER);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, -190, defTopY[cur], 60, 0, SZ_M, I_COOK, 550); Slot(g, S_ON_TOP, 190, defTopY[cur], 60, 0, SZ_M, I_COOK, 350); }
        D("k_sink", "kitchenSink", K, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(2); Kind(K_COUNTER | K_WET);
        D("k_counter", "kitchenCabinet", K, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(2); SlotsTop(1, I_KITCHENWARE | I_FOOD, SZ_M, 700); Kind(K_COUNTER);
        D("k_counterDrawer", "kitchenCabinetDrawer", K, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(2); SlotsTop(1, I_KITCHENWARE | I_FOOD, SZ_M, 700); Kind(K_COUNTER);
        D("k_desk", "desk", K, 0, A_WALL, FL_SOLID | IN, 1); Col(C_TABLE); Clear(2); Kind(K_WORK);
        {
            int g = Group(G_INDEPENDENT, 1000, 0);
            Slot(g, S_ADJACENT, 0, 0, defSz[cur] / 2 + 260, 180, SZ_XL, I_DESKCHAIR, 850); Hiding();
            Slot(g, S_ON_TOP, -defSx[cur] / 6, defTopY[cur], -defSz[cur] / 6, 0, SZ_L, I_SCREEN, 750);
            Slot(g, S_ON_TOP, defSx[cur] * 35 / 100, defTopY[cur], -defSz[cur] / 6, 0, SZ_M, I_LAMP | I_DECOR, 500);
        }
        D("k_screen", "computerScreen", K, I_SCREEN, A_SLOT_ONLY, IN, 1); SlotW(14);
        D("k_laptop", "laptop", K, I_SCREEN | I_DECOR, A_SLOT_ONLY, BOTH, 1); SlotW(8);
        D("k_lampFloor", "lampRoundFloor", K, 0, A_CORNER, FL_LIGHT | BOTH, 1);
        D("k_lampTable", "lampRoundTable", K, I_LAMP, A_SLOT_ONLY, BOTH, 1);
        D("k_plant", "pottedPlant", K, 0, A_CORNER, FL_COMMON | BOTH, 0);
        D("k_plantSmall", "plantSmall1", K, I_DECOR, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("k_bathtub", "bathtub", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Kind(K_WET);
        D("k_toilet", "toilet", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(2);
        D("k_bathSink", "bathroomSink", K, 0, A_WALL, FL_SOLID | FL_HEAVY | IN, 2); Clear(2); Kind(K_WET);
        D("k_washer", "washer", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Kind(K_WET); SlotsTop(1, I_LINEN, SZ_M, 250);
        D("k_sideTable", "sideTable", K, 0, A_WALL, FL_SOLID | IN, 1); Col(C_TABLE); SlotsTop(2, I_DECOR | I_LAMP, SZ_M, 500);
        D("k_bedside", "cabinetBed", K, 0, A_BESIDE, IN, 1); SlotsTop(1, I_LAMP | I_DECOR, SZ_M, 650); Beside(K_BED);
        D("k_rug", "rugRound", K, I_RUG, A_CENTER, FL_FLAT | IN, 1); Fp(4, 4);
        D("k_rugLong", "rugRectangle", K, I_RUG, A_SLOT_ONLY, FL_FLAT | IN, 1); Fp(6, 4);
        D("k_trashcan", "trashcan", K, 0, A_WALL, FL_COMMON | FL_LIGHT | BOTH, 0); Loose(); Cap(1);
        D("k_boxClosed", "cardboardBoxClosed", K, I_BOX, A_WALL, FL_COMMON | FL_LIGHT | BOTH, 0); Loose();
        D("k_boxOpen", "cardboardBoxOpen", K, I_BOX, A_WALL, FL_LIGHT | BOTH, 0); Loose();
        D("k_books", "books", K, I_DECOR, A_SLOT_ONLY, FL_COMMON | BOTH, 0); SlotW(16);
        D("k_radio", "radio", K, I_DECOR, A_SLOT_ONLY, BOTH, 1); SlotW(4);
        D("k_toaster", "toaster", K, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 1);
        D("k_microwave", "kitchenMicrowave", K, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 1);
        D("k_coffeeMachine", "kitchenCoffeeMachine", K, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 1);
        D("k_bench", "bench", K, 0, A_WALL, BOTH, 0); Col(C_SEAT); Kind(K_SEAT);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, -defSx[cur] / 4, defTopY[cur], 0, 0, SZ_M, I_PILLOW, 250); }
        D("k_coatRack", "coatRackStanding", K, 0, A_CORNER, FL_LIGHT | IN, 1);
        D("k_pillow", "pillow", K, I_PILLOW, A_SLOT_ONLY, FL_LIGHT | BOTH, 0);
        D("k_speaker", "speaker", K, 0, A_BESIDE, FL_LIGHT | BOTH, 1); Beside(K_TV);
        D("k_stool", "stoolBar", K, I_STOOL, A_BESIDE, FL_LIGHT | BOTH, 0); Beside(K_COUNTER); Loose(); Cap(3);

        // ===== Kenney Food Kit (tableware and food; composites are one def, 04 section 4.4.1) =====
        D("setting_basic", "setting_basic", 1000, I_SETTING, A_SLOT_ONLY, FL_COMMON | BOTH, 0); SlotW(18);
        D("setting_food", "setting_food", 1000, I_SETTING, A_SLOT_ONLY, BOTH, 0); SlotW(8);
        D("condiments", "condiments", 1000, I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("f_plate", "plate", F, I_TABLEWARE, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("f_apple", "apple", F, I_FOOD, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("f_loaf", "loaf", F, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("f_cake", "cake", F, I_FOOD | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("f_pizzaBox", "pizza-box", F, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("f_sodaCan", "soda-can", F, I_FOOD, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("f_ketchup", "bottle-ketchup", F, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("f_mug", "mug", F, I_TABLEWARE | I_DECOR, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("f_bowl", "bowl", F, I_TABLEWARE | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("f_pan", "frying-pan", F, I_KITCHENWARE | I_COOK, A_SLOT_ONLY, BOTH, 0);
        D("f_pot", "pot", F, I_KITCHENWARE | I_COOK, A_SLOT_ONLY, BOTH, 0);
        D("f_cuttingBoard", "cutting-board", F, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 0);
        D("f_watermelon", "watermelon", F, I_FOOD | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("f_wine", "wine-red", F, I_FOOD | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("f_donut", "donut", F, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("f_pie", "pie", F, I_FOOD | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);

        // ===== KayKit Furniture Bits (base x0.7, then the per-prop normaliser, 04 section 4.3.6) =====
        D("q_armchair", "armchair", 550, 0, A_WALL, FL_SOLID | BOTH, 1); Col(C_SEAT); Clear(1); Kind(K_SEAT);
        D("q_couch", "couch", 700, 0, A_WALL, FL_SOLID | BOTH, 2); Col(C_SEAT); Clear(1); Kind(K_SEAT);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, -defSx[cur] / 4, defTopY[cur], 80, 0, SZ_M, I_PILLOW | I_TOY, 400); Slot(g, S_ON_TOP, defSx[cur] / 4, defTopY[cur], 80, 0, SZ_M, I_PILLOW | I_TOY, 300); }
        D("q_chair", "chair_A", 750, I_CHAIR, A_SLOT_ONLY, FL_COMMON | FL_LIGHT | BOTH, 0); Col(C_SEAT);
        D("q_stool", "chair_stool", 700, I_STOOL | I_SEAT_OUT, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Cap(3);
        D("q_table", "table_medium", 750, 0, A_CENTER, FL_SOLID | IN, 1); Col(C_TABLE); SlotsDining(true, 850); Kind(K_TABLE);
        D("q_tableLow", "table_low", 600, 0, A_CENTER, FL_SOLID | IN, 1); Col(C_TABLE); SlotsTop(2, I_DECOR | I_FOOD | I_TABLEWARE, SZ_M, 450); Kind(K_TABLE);
        D("q_tableSmall", "table_small", 650, 0, A_BESIDE, FL_SOLID | BOTH, 1); Col(C_TABLE); SlotsTop(1, I_LAMP | I_DECOR, SZ_M, 700); Beside(K_SEAT);
        D("q_lampStanding", "lamp_standing", 700, 0, A_CORNER, FL_LIGHT | BOTH, 1);
        D("q_lampTable", "lamp_table", 500, I_LAMP, A_SLOT_ONLY, BOTH, 1);
        D("q_cabinet", "cabinet_medium", 800, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Clear(1); SlotsTop(3, I_LAMP | I_DECOR, SZ_M, 500); Kind(K_STORE);
        D("q_cabinetSmall", "cabinet_small", 800, 0, A_WALL, FL_SOLID | BOTH, 1); Clear(1); SlotsTop(1, I_LAMP | I_DECOR | I_LINEN, SZ_M, 600); Kind(K_STORE);
        D("q_bed", "bed_single_A", 700, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Kind(K_BED);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defTopY[cur], 250, 0, SZ_M, I_TOY | I_PILLOW, 250); }
        D("q_rug", "rug_rectangle_A", 800, I_RUG, A_CENTER, FL_FLAT | IN, 1); Fp(5, 3);
        D("q_frame", "pictureframe_standing_A", 450, I_DECOR, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("q_pillow", "pillow_A", 600, I_PILLOW, A_SLOT_ONLY, FL_LIGHT | BOTH, 0);
        D("q_cactusSmall", "cactus_small_A", 500, I_DECOR, A_SLOT_ONLY, BOTH, 0);
        D("q_cactus", "cactus_medium_A", 650, 0, A_CORNER, BOTH, 0);
        D("q_books", "book_set", 450, I_DECOR, A_SLOT_ONLY, BOTH, 0);

        // ===== KayKit Restaurant Bits (Diner) =====
        D("r_chair", "restaurant_chair_A", 750, I_CHAIR, A_SLOT_ONLY, FL_COMMON | FL_LIGHT | BOTH, 0); Col(C_SEAT);
        D("r_stool", "restaurant_chair_stool", 700, I_STOOL | I_SEAT_OUT, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Cap(3);
        D("r_table", "kitchentable_A", 750, 0, A_CENTER, FL_SOLID | IN, 1); Col(C_TABLE); SlotsDining(true, 900); Kind(K_TABLE);
        D("r_tableRound", "table_round_A_small", 750, 0, A_CENTER, FL_SOLID | BOTH, 1); Col(C_TABLE); SlotsDining(true, 600); Kind(K_TABLE);
        D("r_plate", "restaurant_plate", 280, I_TABLEWARE, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defTopY[cur] > 12 ? 12 : defTopY[cur], 0, 0, SZ_S, I_FOOD, 500); }
        D("r_bowl", "restaurant_bowl", 220, I_TABLEWARE | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("r_burger", "food_burger", 180, I_FOOD, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("r_dinner", "food_dinner", 250, I_FOOD | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("r_ketchup", "ketchup", 300, I_FOOD, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("r_mustard", "mustard", 300, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("r_crate", "crate", 350, I_CRATE, A_WALL, FL_COMMON | BOTH, 0); Loose();
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defSy[cur], 0, 0, SZ_L, I_CRATE | I_BOX, 300); }
        D("r_crateTomatoes", "crate_tomatoes", 350, I_CRATE, A_WALL, BOTH, 0); Loose();
        D("r_crateCheese", "crate_cheese", 350, I_CRATE, A_WALL, BOTH, 0); Loose();
        D("r_fridge", "fridge_A", 700, 0, A_WALL, FL_SOLID | FL_HEAVY | FL_OPENABLE | BOTH, 2); Col(C_HOLLOW); Clear(2); Kind(K_STORE);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_INSIDE, -250, 256 * 700 / 1000, 0, 0, SZ_M, I_FOOD | I_KITCHENWARE, 500); Slot(g, S_INSIDE, 250, 256 * 700 / 1000, 0, 0, SZ_M, I_FOOD, 350); Hiding(); }
        D("r_counter", "kitchencounter_straight_A", 700, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(1); SlotsTop(2, I_KITCHENWARE | I_FOOD | I_TABLEWARE, SZ_M, 650); Kind(K_COUNTER);
        D("r_counterSink", "kitchencounter_sink", 700, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(1); Kind(K_COUNTER | K_WET);
        D("r_stove", "stove_single", 700, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(1); Kind(K_COUNTER);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, -300, defTopY[cur], 150, 0, SZ_M, I_COOK, 600); Slot(g, S_ON_TOP, 300, defTopY[cur], 150, 0, SZ_M, I_COOK, 400); }
        D("r_dishrack", "dishrack_plates", 400, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 0);
        D("r_pot", "pot_A", 300, I_KITCHENWARE | I_COOK, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("r_pan", "pan_A", 300, I_KITCHENWARE | I_COOK, A_SLOT_ONLY, BOTH, 0);
        D("r_menu", "menu", 350, I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0);
        D("r_papertowel", "papertowel", 300, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 0);
        D("r_jar", "jar_A_large", 400, I_KITCHENWARE | I_FOOD, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("r_cuttingboard", "cuttingboard", 300, I_KITCHENWARE, A_SLOT_ONLY, BOTH, 0);

        // ===== KayKit Prototype and Resource Bits, Kenney Survival Kit (Warehouse) =====
        D("w_barrel", "Barrel_A", 850, I_CRATE, A_WALL, FL_SOLID | FL_COMMON | BOTH, 0); Loose();
        D("w_barrelB", "Barrel_B", 850, I_CRATE, A_WALL, FL_SOLID | BOTH, 0); Loose();
        D("w_boxA", "Box_A", 1000, I_BOX, A_WALL, FL_COMMON | FL_LIGHT | BOTH, 0); Loose();
        D("w_boxB", "Box_B", 900, I_BOX, A_WALL, FL_COMMON | FL_LIGHT | BOTH, 0); Loose();
        D("w_boxC", "Box_C", 800, I_BOX, A_WALL, FL_LIGHT | BOTH, 0); Loose();
        D("w_pallet", "Pallet_Wood", 750, 0, A_ANY, FL_SOLID | FL_HEAVY | BOTH, 0); Facing(F_ALIGN_GRID); Jit(4, 4); Kind(K_STORE);
        {
            int gBig = Group(G_INDEPENDENT, 450, 0);
            Slot(gBig, S_ON_TOP, 0, defTopY[cur], 0, 0, SZ_XL, I_CRATE, 1000);
            int gSmall = Group(G_ELSE_OF, 900, gBig);
            int q = 290;
            Slot(gSmall, S_ON_TOP, -q, defTopY[cur], -q, 0, SZ_M, I_BOX | I_STORE, 700);
            Slot(gSmall, S_ON_TOP, q, defTopY[cur], -q, 0, SZ_M, I_BOX | I_STORE, 700);
            Slot(gSmall, S_ON_TOP, -q, defTopY[cur], q, 0, SZ_M, I_BOX | I_STORE, 600);
            Slot(gSmall, S_ON_TOP, q, defTopY[cur], q, 0, SZ_M, I_BOX | I_STORE, 500); Hiding();
        }
        D("w_palletLoaded", "Pallet_Small_Decorated_A", 600, 0, A_ANY, FL_SOLID | FL_HEAVY | BOTH, 1); Facing(F_ALIGN_GRID); Kind(K_STORE);
        D("w_fuelBarrel", "Fuel_A_Barrel", 900, I_CRATE, A_WALL, FL_SOLID | FL_COMMON | BOTH, 0); Loose(); Cap(5);
        D("w_fuelBarrelB", "Fuel_B_Barrel", 900, I_CRATE, A_WALL, FL_SOLID | BOTH, 0); Loose(); Cap(5);
        D("w_jerrycan", "Fuel_A_Jerrycan", 600, I_STORE, A_WALL, FL_COMMON | FL_LIGHT | BOTH, 0); Loose(); SlotW(5);
        D("w_planks", "Wood_Planks_Stack_Medium", 700, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 0); Facing(F_ALIGN_GRID); Cap(1);
        D("w_logs", "Wood_Log_Stack", 700, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Facing(F_ALIGN_GRID); Cap(1);
        D("w_textiles", "Textiles_Stack_Small", 700, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Facing(F_ALIGN_GRID); Cap(1);
        D("w_textileRoll", "Textiles_A", 700, I_STORE, A_WALL, FL_LIGHT | BOTH, 0); Loose();
        D("w_bricks", "Stone_Bricks_Stack_Small", 800, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 0); Cap(1);
        D("w_goldBars", "Gold_Bars", 400, I_STORE, A_WALL, BOTH, 2); Loose();
        D("w_ironStack", "Iron_Bars_Stack_Small", 700, I_STORE, A_WALL, FL_HEAVY | BOTH, 0); Loose();
        D("w_parts", "Parts_Pile_Small", 700, 0, A_WALL, BOTH, 0); Loose();
        D("w_cog", "Parts_Cog", 600, I_TOOL | I_STORE, A_SLOT_ONLY, BOTH, 0);
        D("w_nugget", "Copper_Nugget_Large", 500, I_STORE, A_SLOT_ONLY, BOTH, 1);
        D("gen_rack", "gen_rack", 1000, 0, A_WALL, FL_SOLID | FL_HEAVY | FL_SEEINTO | FL_FIXTURE, 1); Col(C_SHELF); Clear(1); Fp(4, 1); Kind(K_STORE);
        SlotsShelves(3, I_BOX | I_STORE, 600, true);
        // the same rack as a pantry shelf, for the diner's store room
        D("gen_rackPantry", "gen_rack", 1000, 0, A_WALL, FL_SOLID | FL_HEAVY | FL_SEEINTO | FL_FIXTURE, 1); Col(C_SHELF); Clear(1); Fp(4, 1); Kind(K_STORE);
        SlotsShelves(3, I_KITCHENWARE | I_FOOD | I_BOX, 600, true);
        D("s_barrel", "barrel", 2600, I_CRATE, A_WALL, FL_SOLID | BOTH, 0); Loose(); Beside(K_TENT | K_STORE | K_WORK);
        D("s_box", "box", 2400, I_BOX, A_WALL, FL_COMMON | BOTH, 0); Loose(); Beside(K_TENT | K_STORE | K_WORK);
        D("s_boxLarge", "box-large", 2400, 0, A_WALL, FL_SOLID | BOTH, 0); Cap(1);
        D("s_bucket", "bucket", 2000, I_STORE, A_WALL, FL_COMMON | FL_LIGHT | BOTH, 0); Loose(); Beside(K_WORK | K_WET | K_FIRE | K_TENT); SlotW(2);
        D("s_chest", "chest", 2500, I_TOY, A_WALL, BOTH, 2); Jit(4, 20); Loose(); Beside(K_TENT | K_BED); Cap(1);
        D("s_bedroll", "bedroll", 2600, I_BEDROLL, A_BESIDE, FL_LIGHT | BOTH, 1); Loose(); Beside(K_TENT | K_FIRE); Cap(1);
        D("s_axe", "tool-axe", 2500, I_TOOL | I_CHOP, A_WALL, BOTH, 1); Loose(); Beside(K_WORK | K_TENT | K_STUMP);
        D("s_signpost", "signpost", 3000, 0, A_OUT_EDGE, BOTH, 1); NoLeak(); Jit(2, 3);
        D("s_workbench", "workbench", 2800, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Clear(1); SlotsTop(3, I_TOOL | I_STORE, SZ_M, 600); Kind(K_WORK);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ADJACENT, 0, 0, defSz[cur] / 2 + 330, 180, SZ_XL, I_STOOL, 650); Hiding(); }
        D("s_treeLog", "tree-log", 2000, I_SEAT_OUT, A_SOFT, FL_SOLID | FL_HEAVY | OUT, 0); Kind(K_STUMP); Cap(1);
        D("s_bottle", "bottle", 1500, I_FOOD | I_DECOR, A_SLOT_ONLY, FL_COMMON | BOTH, 0);
        D("s_campfirePit", "campfire-pit", 3000, 0, A_OUT_OPEN, FL_HEAVY | BOTH, 2); Kind(K_FIRE);
        {
            int g = Group(G_INDEPENDENT, 1000, 0);
            int r = 1150;
            Slot(g, S_ADJACENT, 0, 0, r, 180, SZ_XL, I_SEAT_OUT, 700); Hiding();
            Slot(g, S_ADJACENT, 0, 0, -r, 0, SZ_XL, I_SEAT_OUT, 600);
            Slot(g, S_ADJACENT, r, 0, 0, 270, SZ_XL, I_SEAT_OUT, 600);
        }

        // ===== generated wardrobe (Z5: the packs have none) =====
        D("gen_wardrobe", "gen_wardrobe", 1000, 0, A_WALL, FL_SOLID | FL_HEAVY | FL_OPENABLE | BOTH, 2); Col(C_HOLLOW); Clear(3); Fp(2, 1); Kind(K_STORE);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_INSIDE, -250, 120, 20, 0, SZ_M, I_BOX | I_PILLOW | I_TOY, 400); Slot(g, S_INSIDE, 250, 120, 20, 0, SZ_M, I_BOX | I_PILLOW | I_TOY, 300); Hiding(); }

        // ===== Kenney Nature Kit (Lawn, Forest) =====
        D("n_treeDefault", "tree_default", 3500, 0, A_SOFT, FL_SOLID | FL_SCENERY, 0); Col(C_TRUNK); Fp(1, 1); Spacing(4); Jit(15, 180); Kind(K_TREE); Canopy(2800);
        D("n_treeOak", "tree_oak", 3500, 0, A_SOFT, FL_SOLID | FL_SCENERY, 0); Col(C_TRUNK); Fp(1, 1); Spacing(4); Jit(15, 180); Kind(K_TREE); Canopy(2530);
        D("n_treePineTall", "tree_pineTallA", 3500, 0, A_SOFT, FL_SOLID | FL_SCENERY, 0); Col(C_TRUNK); Fp(1, 1); Spacing(3); Jit(15, 180); Kind(K_TREE); Canopy(2200);
        D("n_treePineRound", "tree_pineRoundA", 3500, 0, A_SOFT, FL_SOLID | FL_SCENERY, 0); Col(C_TRUNK); Fp(1, 1); Spacing(4); Jit(15, 180); Kind(K_TREE); Canopy(640);
        D("n_treeFat", "tree_fat", 3500, 0, A_OUT_OPEN, FL_SOLID | FL_SCENERY, 0); Col(C_TRUNK); Fp(1, 1); Spacing(5); Jit(15, 180); Kind(K_TREE); Canopy(850);
        // revision 9: the same broadleaf trees in autumn and in deep summer green; a map takes one season (MapGen.season)
        // and every broadleaf tree on it is of that season. The pines have no twins: they stay green.
        string[] tw = { "n_treeDefault", "tree_default", "2800", "n_treeOak", "tree_oak", "2530", "n_treeFat", "tree_fat", "850" };
        string[] sfx = { "_fall", "_dark" };
        for (int k = 0; k < tw.Length; k += 3)
            for (int s2 = 0; s2 < 2; s2++)
            {
                string model = tw[k + 1] + (tw[k] == "n_treeFat" && s2 == 1 ? "_darkh" : sfx[s2]);   // the pack's own spelling
                int baseDef = FindDef(tw[k]);
                int d = D(tw[k] + (s2 == 0 ? "Fall" : "Dark"), model, 3500, 0, defAnchor[baseDef], FL_SOLID | FL_SCENERY, 0);
                Col(C_TRUNK); Fp(1, 1); Spacing(defSpacing[baseDef]); Jit(15, 180); Kind(K_TREE); Canopy(int.Parse(tw[k + 2]));
                defSeason[baseDef * 3 + 1 + s2] = d;
            }
        D("n_bush", "plant_bush", 3000, 0, A_OUT_OPEN, FL_COMMON | OUT, 0); Jit(10, 180); Kind(K_PLANT);
        D("n_bushLarge", "plant_bushLarge", 3000, 0, A_OUT_EDGE, OUT, 0); Jit(6, 20); Kind(K_PLANT);
        D("n_bushDetailed", "plant_bushDetailed", 2000, 0, A_OUT_OPEN, OUT, 0); Jit(10, 180); Kind(K_PLANT);
        D("n_flowerRed", "flower_redA", 2500, 0, A_SOFT, FL_COMMON | FL_LIGHT | OUT, 0); Jit(12, 180); Loose(); Cap(3);
        D("n_flowerYellow", "flower_yellowA", 2500, 0, A_SOFT, FL_COMMON | FL_LIGHT | OUT, 0); Jit(12, 180); Loose(); Cap(3);
        D("n_flowerPurple", "flower_purpleA", 2500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(12, 180); Loose(); Cap(3);
        D("n_rockLarge", "rock_largeA", 2000, 0, A_SOFT, FL_SOLID | FL_HEAVY | OUT, 0); Jit(10, 180);
        D("n_rockLargeB", "rock_largeB", 1800, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 0); Jit(10, 180);
        D("n_rockSmall", "rock_smallA", 1800, 0, A_OUT_OPEN, FL_COMMON | FL_HEAVY | BOTH, 1); Jit(12, 180); Cap(3);
        D("n_rockSmallB", "rock_smallD", 1600, 0, A_SOFT, FL_HEAVY | OUT, 0); Jit(12, 180); Cap(3);
        D("n_rockTall", "rock_tallA", 1500, 0, A_SOFT, FL_SOLID | FL_HEAVY | OUT, 0); Jit(10, 180);
        D("n_stump", "stump_old", 2500, I_SEAT_OUT, A_SOFT, FL_SOLID | FL_HEAVY | OUT, 0); Jit(10, 180); Kind(K_STUMP); Cap(1);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defSy[cur] * 75 / 100, 0, 0, SZ_L, I_CHOP | I_MUSHROOM, 300); }
        D("n_stumpRound", "stump_round", 2500, I_SEAT_OUT, A_SOFT, FL_SOLID | FL_HEAVY | OUT, 0); Jit(10, 180); Kind(K_STUMP); Cap(1);
        D("n_log", "log", 3000, I_SEAT_OUT, A_SOFT, FL_COMMON | FL_HEAVY | OUT, 0); Jit(10, 25); Cap(1);
        D("n_logStack", "log_stack", 2500, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | BOTH, 0); Beside(K_TENT | K_FIRE); Cap(1);
        D("n_logLarge", "log_large", 2000, 0, A_SOFT, FL_SOLID | FL_HEAVY | OUT, 0); Jit(10, 25); Cap(1);
        D("n_mushroomRed", "mushroom_red", 2500, I_MUSHROOM, A_BESIDE, FL_COMMON | FL_LIGHT | BOTH, 1); Jit(15, 180); Loose(); Beside(K_TREE | K_STUMP);
        D("n_mushroomGroup", "mushroom_redGroup", 2500, 0, A_BESIDE, FL_LIGHT | OUT, 0); Jit(15, 180); Loose(); Beside(K_TREE | K_STUMP);
        D("n_mushroomTan", "mushroom_tan", 2500, I_MUSHROOM, A_BESIDE, FL_COMMON | FL_LIGHT | OUT, 0); Jit(15, 180); Loose(); Beside(K_TREE | K_STUMP);
        D("n_campfire", "campfire_stones", 2500, 0, A_OUT_OPEN, FL_HEAVY | BOTH, 2); Fp(3, 3); Kind(K_FIRE);
        {
            int g = Group(G_INDEPENDENT, 1000, 0);
            int r = 1350;
            Slot(g, S_ADJACENT, 0, 0, r, 180, SZ_XL, I_SEAT_OUT, 700); Hiding();
            Slot(g, S_ADJACENT, 0, 0, -r, 0, SZ_XL, I_SEAT_OUT, 700);
            Slot(g, S_ADJACENT, r, 0, 0, 270, SZ_XL, I_SEAT_OUT, 600);
            Slot(g, S_ADJACENT, -r, 0, 0, 90, SZ_XL, I_SEAT_OUT, 600);
        }
        D("n_tentOpen", "tent_detailedOpen", 3000, 0, A_SOFT, FL_SOLID | FL_HEAVY | FL_SEEINTO | FL_FIXTURE, 2); Col(C_TENT); Facing(F_TO_CENTER); Jit(5, 6); Kind(K_TENT);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_INSIDE, 0, 40, -150, 0, SZ_XL, I_BEDROLL, 800); Hiding(); }
        D("n_tentClosed", "tent_smallClosed", 3000, 0, A_SOFT, FL_SOLID | FL_HEAVY | BOTH, 2); Jit(5, 30); Kind(K_TENT);
        D("n_fence", "fence_planks", 2000, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 0); Jit(2, 2);
        D("n_sign", "sign", 2500, 0, A_OUT_EDGE, BOTH, 1); NoLeak(); Jit(2, 3);   // square to its fence, not turned 12 degrees into it
        D("n_planter", "pot_large", 2000, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 0); Kind(K_PLANT);
        D("n_canoe", "canoe", 2000, 0, A_OUT_EDGE, FL_HEAVY | BOTH, 2);
        D("n_sleepingMat", "bed_floor", 2500, I_BEDROLL, A_SLOT_ONLY, FL_LIGHT | BOTH, 1);
        D("n_grass", "grass", 3000, 0, A_SOFT, FL_DECOR, 0); Jit(20, 180);
        D("n_grassLarge", "grass_large", 3000, 0, A_SOFT, FL_DECOR, 0); Jit(20, 180);

        // ===== Kenney Cube Pets (toy props, Z6) =====
        D("toy_cat", "animal-cat", 200, I_TOY, A_BESIDE, FL_LIGHT | BOTH, 1); Jit(8, 180); Loose(); Beside(K_BED | K_SEAT);
        D("toy_dog", "animal-dog", 200, I_TOY, A_BESIDE, FL_LIGHT | BOTH, 1); Jit(8, 180); Loose(); Beside(K_BED | K_SEAT);
        D("toy_bunny", "animal-bunny", 200, I_TOY, A_BESIDE, FL_LIGHT | BOTH, 1); Jit(8, 180); Loose(); Beside(K_BED | K_SEAT);
        D("toy_chick", "animal-chick", 180, I_TOY, A_BESIDE, FL_LIGHT | BOTH, 1); Jit(8, 180); Loose(); Beside(K_BED | K_SEAT);
        D("toy_panda", "animal-panda", 200, I_TOY, A_BESIDE, FL_LIGHT | BOTH, 1); Jit(8, 180); Loose(); Beside(K_BED | K_SEAT);

        // ===== variants for prop groups (G25): more kinds of the things that stand together =====
        // buckets: Kenney Survival Kit's plastic one is s_bucket above; KayKit has a metal one and two wooden tubs
        D("t_bucketMetal", "bucket_metal", 330, I_STORE, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Beside(K_WORK | K_WET | K_FIRE | K_TENT); SlotW(2);
        D("m_bucketWood", "bucket_empty", 2400, I_STORE, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Beside(K_WORK | K_WET | K_FIRE | K_TENT); SlotW(2);
        D("m_bucketWater", "bucket_water", 2400, I_STORE, A_WALL, BOTH, 0); Loose(); Beside(K_WORK | K_WET | K_FIRE | K_TENT); SlotW(2);
        D("r_crateCarrots", "crate_carrots", 350, I_CRATE, A_WALL, BOTH, 0); Loose();
        D("r_crateLettuce", "crate_lettuce", 350, I_CRATE, A_WALL, BOTH, 0); Loose();
        D("r_cratePotatoes", "crate_potatoes", 350, I_CRATE, A_WALL, BOTH, 0); Loose();
        D("w_barrelC", "Barrel_C", 850, I_CRATE, A_WALL, FL_SOLID | BOTH, 0); Loose();
        D("s_barrelOpen", "barrel-open", 2600, I_CRATE, A_WALL, FL_SOLID | BOTH, 0); Loose();
        D("w_fuelBarrelC", "Fuel_C_Barrel", 900, I_CRATE, A_WALL, FL_SOLID | BOTH, 0); Loose(); Cap(5);
        D("w_jerrycanB", "Fuel_B_Jerrycan", 600, I_STORE, A_WALL, FL_LIGHT | BOTH, 0); Loose(); SlotW(5);
        D("w_jerrycanC", "Fuel_C_Jerrycan", 600, I_STORE, A_WALL, FL_LIGHT | BOTH, 0); Loose(); SlotW(5);
        D("s_boxOpen", "box-open", 2400, I_BOX, A_WALL, BOTH, 0); Loose();
        D("s_boxLargeOpen", "box-large-open", 2400, 0, A_WALL, FL_SOLID | BOTH, 0); Cap(1);
        D("s_bottleLarge", "bottle-large", 1500, I_FOOD | I_DECOR, A_SLOT_ONLY, BOTH, 0);
        D("s_shovel", "tool-shovel", 2500, I_TOOL, A_WALL, BOTH, 1); Loose(); Beside(K_WORK | K_TENT | K_STUMP);
        D("s_hoe", "tool-hoe", 2500, I_TOOL, A_WALL, BOTH, 1); Loose(); Beside(K_WORK | K_TENT | K_STUMP);
        D("s_pickaxe", "tool-pickaxe", 2500, I_TOOL, A_WALL, BOTH, 1); Loose(); Beside(K_WORK | K_TENT | K_STUMP);
        D("s_hammer", "tool-hammer", 2500, I_TOOL, A_WALL, BOTH, 1); Loose(); Beside(K_WORK | K_TENT | K_STUMP);
        D("w_logA", "Wood_Log_A", 700, 0, A_OUT_EDGE, FL_HEAVY | BOTH, 0); Loose(); Beside(K_FIRE | K_TENT);
        D("w_logB", "Wood_Log_B", 700, 0, A_OUT_EDGE, FL_HEAVY | BOTH, 0); Loose(); Beside(K_FIRE | K_TENT);
        D("n_logStackLarge", "log_stackLarge", 2500, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | BOTH, 0); Beside(K_TENT | K_FIRE); Cap(1);
        D("n_potSmall", "pot_small", 1500, 0, A_WALL, FL_HEAVY | BOTH, 0); Kind(K_PLANT); Loose(); Cap(3);
        D("n_flowerRedB", "flower_redB", 2500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(12, 180); Loose(); Cap(3);
        D("n_flowerYellowB", "flower_yellowB", 2500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(12, 180); Loose(); Cap(3);
        D("n_flowerPurpleB", "flower_purpleB", 2500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(12, 180); Loose(); Cap(3);
        D("n_mushroomRedTall", "mushroom_redTall", 2500, I_MUSHROOM, A_BESIDE, FL_LIGHT | OUT, 0); Jit(15, 180); Loose(); Beside(K_TREE | K_STUMP);
        D("n_mushroomTanTall", "mushroom_tanTall", 2500, I_MUSHROOM, A_BESIDE, FL_LIGHT | OUT, 0); Jit(15, 180); Loose(); Beside(K_TREE | K_STUMP);
        D("n_mushroomTanGroup", "mushroom_tanGroup", 2500, 0, A_BESIDE, FL_LIGHT | OUT, 0); Jit(15, 180); Loose(); Beside(K_TREE | K_STUMP);
        D("n_rockSmallC", "rock_smallB", 1800, 0, A_OUT_OPEN, FL_HEAVY | OUT, 0); Jit(12, 180); Cap(3);

        // ===== generator 3 (G28): pieces the room plans need. From the mirror: =====
        D("k_bar", "kitchenBar", K, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); SlotsTop(2, I_TABLEWARE | I_FOOD | I_CENTERPIECE, SZ_M, 380); Kind(K_COUNTER);
        D("k_benchCushion", "benchCushion", K, 0, A_WALL, BOTH, 0); Col(C_SEAT); Kind(K_SEAT);
        D("k_doormat", "rugDoormat", K, 0, A_DOOR_SIDE, FL_FLAT | BOTH, 1);
        D("k_dryer", "dryer", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Kind(K_WET); SlotsTop(1, I_LINEN, SZ_M, 350);
        D("n_pathStone", "path_stone", 1000, 0, A_OUT_OPEN, FL_FLAT | FL_DECOR, 0); Jit(4, 8);
        D("n_cropCarrot", "crop_carrot", 1500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(4, 180); Loose(); Cap(4);
        D("n_cropPumpkin", "crop_pumpkin", 1500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(4, 180); Loose(); Cap(4);
        D("n_cropMelon", "crop_melon", 1500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(4, 180); Loose(); Cap(4);
        D("m_wheelbarrow", "wheelbarrow", 2400, 0, A_OUT_EDGE, BOTH, 1); Beside(K_STORE | K_WORK);
        D("m_sack", "sack", 3200, I_STORE, A_WALL, BOTH, 0); Jit(4, 14); Loose(); Beside(K_STORE | K_WORK); Cap(3); SlotW(4);
        D("ms_handTruck", "hand-truck", 2200, 0, A_WALL, BOTH, 1); Beside(K_STORE);
        // stand-ins built from simple shapes in the packs' style, because the mirror has none (Z5)
        D("gen_wateringCan", "gen_wateringCan", 1000, 0, A_OUT_EDGE, FL_LIGHT | BOTH, 1); Jit(3, 25); Loose(); Beside(K_WET | K_WORK | K_STORE);
        D("gen_mailbox", "gen_mailbox", 1000, 0, A_OUT_EDGE, FL_HEAVY | OUT, 1); Jit(2, 3); Cap(1); NoLeak();
        D("gen_dumpster", "gen_dumpster", 1000, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Jit(2, 3); Cap(1);
        D("gen_tap", "gen_tap", 1000, 0, A_OUT_EDGE, FL_FIXTURE | FL_HEAVY, 1); Kind(K_WET);
        D("gen_mop", "gen_mop", 1000, 0, A_WALL, FL_LIGHT | BOTH, 1); Jit(2, 4); Loose(); Beside(K_WET | K_STORE | K_WORK);
        D("gen_broom", "gen_broom", 1000, 0, A_WALL, FL_LIGHT | BOTH, 1); Jit(2, 4); Loose(); Beside(K_WET | K_STORE | K_WORK);
        D("gen_shed", "gen_shed", 1000, 0, A_OUT_EDGE, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Kind(K_STORE | K_WORK); Jit(0, 0);
        // the gas station's (generated by the prefab baker): pumps, a parked car, a stack of tyres
        D("gen_fuelPump", "gen_fuelPump", 1000, 0, A_CENTER, FL_SOLID | FL_HEAVY, 2); Jit(0, 0); Cap(4); NoLeak();
        D("gen_car", "gen_car", 1000, 0, A_CENTER, FL_SOLID | FL_HEAVY, 3); Jit(4, 6); Cap(2); NoLeak();
        D("gen_tireStack", "gen_tireStack", 1000, 0, A_OUT_EDGE, FL_SOLID | OUT, 1); Jit(3, 20); Cap(4);
        D("gen_laundryBasket", "gen_laundryBasket", 1000, 0, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Beside(K_WET);
        D("gen_towels", "gen_towels", 1000, I_LINEN, A_SLOT_ONLY, FL_LIGHT | BOTH, 0);
        D("gen_shoes", "gen_shoes", 1000, 0, A_WALL, FL_LIGHT | IN, 0); Jit(2, 10); Loose();

        // ===== revision 9: the new kinds of plot =====
        // the shop: Kenney Mini Shop at x2 (its 1-unit wall is 2 m: shelves 1.7 m, freezers 1.8 m, the checkout 1.2 m).
        // The shelves come with their goods modelled on them, so they take no items.
        const int MS = 2000;
        D("ms_shelfBoxes", "shelf-boxes", MS, 0, A_CENTER, FL_SOLID | FL_HEAVY | IN, 1); Clear(1); Kind(K_STORE); Cap(4);
        D("ms_shelfBags", "shelf-bags", MS, 0, A_CENTER, FL_SOLID | FL_HEAVY | IN, 1); Clear(1); Kind(K_STORE); Cap(4);
        D("ms_shelfEnd", "shelf-end", MS, 0, A_WALL, FL_SOLID | FL_HEAVY | IN, 1); Clear(1); Kind(K_STORE); Cap(4);
        D("ms_freezers", "freezers-standing", MS, 0, A_WALL, FL_SOLID | FL_HEAVY | IN, 1); Clear(1); Kind(K_STORE); Cap(3);
        D("ms_freezer", "freezer", MS, 0, A_WALL, FL_SOLID | FL_HEAVY | IN, 1); Clear(1); Kind(K_STORE); SlotsTop(1, I_FOOD, SZ_S, 300);
        D("ms_dispBread", "display-bread", MS, 0, A_WALL, FL_SOLID | IN, 1); Clear(1); Kind(K_STORE);
        D("ms_dispCans", "display-cans", MS, 0, A_ANY, FL_SOLID | BOTH, 1); Kind(K_STORE); Cap(1);
        D("ms_dispFruit", "display-fruit", MS, 0, A_ANY, FL_SOLID | BOTH, 1); Kind(K_STORE); Cap(2);
        D("ms_dispFlowers", "display-flowers", MS, 0, A_OUT_EDGE, FL_SOLID | BOTH, 1); Kind(K_PLANT); Cap(1);
        D("ms_dispSmall", "display-small", MS, 0, A_WALL, BOTH, 1); Kind(K_STORE);
        D("ms_checkout", "cash-register", MS, 0, A_ANY, FL_SOLID | FL_HEAVY | IN, 1); Clear(1); Kind(K_COUNTER); Cap(2); SlotsTop(1, I_FOOD, SZ_S, 350);
        D("ms_clothesRack", "clothing-rack", MS, 0, A_ANY, FL_SOLID | BOTH, 2); Kind(K_STORE); Cap(2);
        D("ms_clothesTable", "clothing-table", MS, 0, A_ANY, FL_SOLID | BOTH, 1); Kind(K_TABLE); Cap(1);
        D("ms_cart", "shopping-cart", 2400, 0, A_WALL, FL_LIGHT | BOTH, 1); Jit(6, 20); Loose(); Beside(K_STORE | K_COUNTER); Cap(3);
        D("ms_basket", "shopping-basket", 1400, I_BOX, A_WALL, FL_LIGHT | BOTH, 1); Jit(4, 30); Loose(); Beside(K_COUNTER | K_STORE); Cap(3);
        D("ms_bottleReturn", "bottle-return", MS, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Cap(1);
        D("ms_gacha", "gacha-machine", MS, 0, A_WALL, FL_SOLID | BOTH, 2); Clear(1); Cap(1);
        D("ms_mannequin", "mannequin", MS, 0, A_WALL, BOTH, 2); Cap(2);
        // the garage: Kenney Car Kit at x1.2 (a saloon 1.8 m wide and 3.1 m long)
        const int CK = 1200;
        D("c_sedan", "sedan", CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1);
        D("c_suv", "suv", CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1);
        D("c_van", "van", CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1);
        D("c_truck", "truck", CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1);
        D("c_hatch", "hatchback-sports", CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1);
        D("c_taxi", "taxi", CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1);
        D("c_tractor", "tractor", CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1);
        D("c_sports", "sedan-sports", CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1);
        D("c_luxury", "suv-luxury", CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1);
        D("c_police", "police", CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1);
        D("c_flatbed", "truck-flat", CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1);
        D("c_wheel", "wheel-default", CK, 0, A_WALL, BOTH, 1); Jit(4, 25); Loose(); Beside(K_WORK | K_STORE);
        D("c_tire", "debris-tire", CK, 0, A_WALL, BOTH, 1); Jit(4, 25); Loose(); Beside(K_WORK | K_STORE);
        D("c_cone", "cone", 900, 0, A_OUT_OPEN, FL_LIGHT | BOTH, 2); Jit(6, 40); Loose(); Beside(K_WORK);
        D("c_bumper", "debris-bumper", CK, 0, A_WALL, BOTH, 1); Jit(4, 12); Beside(K_WORK | K_STORE);
        D("c_door", "debris-door", CK, 0, A_WALL, BOTH, 1); Jit(3, 8); Beside(K_WORK | K_STORE);
        D("c_engine", "debris-drivetrain", 1000, 0, A_WALL, FL_HEAVY | BOTH, 1); Beside(K_WORK | K_STORE); Cap(1);
        D("c_plate", "debris-plate-a", 1000, 0, A_WALL, BOTH, 1); Jit(4, 20); Beside(K_WORK | K_STORE);
        // the farm: Cube Pets, Graveyard Kit hay, Fantasy Town carts, Nature Kit crops
        D("p_cow", "animal-cow", 750, 0, A_OUT_OPEN, FL_SOLID | OUT, 2); Jit(10, 180); Cap(3);
        D("p_pig", "animal-pig", 600, 0, A_OUT_OPEN, FL_SOLID | OUT, 2); Jit(10, 180); Cap(3);
        D("p_hen", "animal-chick", 300, 0, A_OUT_OPEN, FL_LIGHT | OUT, 2); Jit(10, 180); Loose(); Cap(4);
        D("g_hay", "hay-bale", 1800, 0, A_WALL, FL_SOLID | BOTH, 1); Jit(4, 6); Kind(K_STORE);
        D("g_hayBundled", "hay-bale-bundled", 1800, 0, A_WALL, FL_SOLID | BOTH, 1); Jit(4, 6); Kind(K_STORE);
        D("f_cart", "cart", 1800, 0, A_OUT_EDGE, FL_SOLID | OUT, 1); Kind(K_WORK); Cap(1);
        D("f_cartHigh", "cart-high", 1800, 0, A_OUT_EDGE, FL_SOLID | OUT, 1); Kind(K_WORK); Cap(1);
        D("n_cropRow", "crops_dirtDoubleRow", 2000, 0, A_SOFT, FL_DECOR | FL_FLAT, 0);
        D("n_corn", "crops_cornStageD", 1500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(4, 180); Loose(); Cap(8);
        D("n_wheat", "crops_wheatStageB", 1500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(4, 180); Loose(); Cap(8);
        D("n_lettuce", "crops_leafsStageB", 1500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(4, 180); Loose(); Cap(8);
        D("n_cropTurnip", "crop_turnip", 1500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(4, 180); Loose(); Cap(6);
        D("n_cornYoung", "crops_cornStageB", 1500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(4, 180); Loose(); Cap(8);
        D("n_wheatYoung", "crops_wheatStageA", 1500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(4, 180); Loose(); Cap(8);
        D("n_lettuceYoung", "crops_leafsStageA", 1500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(4, 180); Loose(); Cap(8);
        D("n_bamboo", "crops_bambooStageB", 1500, 0, A_SOFT, FL_LIGHT | OUT, 0); Jit(4, 180); Loose(); Cap(8);
        // the park: Fantasy Town fountain and lamp posts
        D("f_fountain", "fountain-round-detail", 1300, 0, A_CENTER, FL_SOLID | FL_HEAVY | FL_FIXTURE, 1); Cap(1); Jit(0, 0);
        D("f_lamp", "fantasy_lantern", 1700, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY, 1); Jit(2, 10); Cap(4);
        // the park's statues (Nature Kit at x2) and the farm's dog and cat (Cube Pets)
        D("n_statueHead", "statue_head", 2000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY, 1); Cap(1); Jit(2, 10);
        D("n_obelisk", "statue_obelisk", 2000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY, 1); Cap(1); Jit(2, 10);
        D("n_column", "statue_column", 2000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY, 1); Cap(2); Jit(2, 10);
        D("n_statueRing", "statue_ring", 2000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY, 1); Cap(1); Jit(2, 10);
        D("p_dog", "animal-dog", 450, 0, A_OUT_OPEN, FL_LIGHT | BOTH, 2); Jit(10, 180); Cap(1);
        D("p_cat", "animal-cat", 330, 0, A_OUT_OPEN, FL_LIGHT | BOTH, 2); Jit(10, 180); Cap(1);

        // ===== revision 10: the farm and the park brought up to the original places (G58) =====
        // farm: more animals, the coop and the trough, more machinery, the farm workshop's tools (RPG Tools Bits at
        // x0.5), the farm stand (Fantasy Town market stalls) and its produce (Food Kit at x0.5)
        D("p_hog", "animal-hog", 600, 0, A_OUT_OPEN, FL_SOLID | OUT, 2); Jit(10, 180); Cap(2);
        D("p_rabbit", "animal-bunny", 330, 0, A_OUT_OPEN, FL_LIGHT | OUT, 2); Jit(10, 180); Loose(); Cap(3);
        D("gen_coop", "gen_coop", 1000, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 1); Kind(K_STORE); Cap(1);
        D("gen_trough", "gen_trough", 1000, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | BOTH, 1); Kind(K_WET); Cap(1);
        D("c_digger", "tractor-shovel", CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1);
        D("c_tractorWheel", "wheel-tractor-back", CK, 0, A_WALL, FL_HEAVY | BOTH, 1); Jit(4, 20); Beside(K_WORK | K_STORE); Cap(2);
        D("g_anvil", "anvil", 600, 0, A_ANY, FL_SOLID | FL_HEAVY | BOTH, 1); Kind(K_WORK); Cap(1);
        D("g_grindstone", "grindstone", 600, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Kind(K_WORK); Cap(1);
        string[] hand = { "g_saw", "saw", "g_mallet", "mallet", "g_hammer", "hammer", "g_axe", "axe", "g_shovel", "shovel", "g_pickaxe", "pickaxe",
            "g_drill", "handdrill", "g_wrench", "wrench_A", "g_tongs", "tongs", "g_trowel", "trowel", "g_plane", "handplane" };
        for (int k = 0; k < hand.Length; k += 2) { D(hand[k], hand[k + 1], 500, I_TOOL, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Jit(4, 20); Beside(K_WORK | K_STORE); }
        D("g_rope", "rope_bundle_A", 500, I_STORE, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Beside(K_WORK | K_STORE);
        D("g_ropeB", "rope_bundle_B", 500, I_STORE, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Beside(K_WORK | K_STORE);
        D("g_lantern", "lantern", 500, I_DECOR | I_STORE, A_WALL, FL_LIGHT | BOTH, 1); Loose(); Beside(K_WORK | K_STORE | K_TENT);
        D("f_stallGreen", "stall-green", 1800, 0, A_OUT_EDGE, FL_SOLID | OUT, 1); Kind(K_COUNTER); Cap(1); SlotsTop(5, I_FOOD, SZ_S, 900);
        D("f_stallRed", "stall-red", 1800, 0, A_OUT_EDGE, FL_SOLID | OUT, 1); Kind(K_COUNTER); Cap(1); SlotsTop(5, I_FOOD, SZ_S, 900);
        D("f_stallBench", "stall-bench", 1800, 0, A_BESIDE, FL_LIGHT | OUT, 1); Beside(K_COUNTER); Cap(2);
        D("f_stallStool", "stall-stool", 1800, 0, A_BESIDE, FL_LIGHT | OUT, 1); Beside(K_COUNTER); Cap(2);
        D("f_wheel", "wheel", 1800, 0, A_WALL, BOTH, 1); Jit(4, 20); Beside(K_WORK | K_STORE); Cap(2);
        string[] produce = { "fk_cabbage", "cabbage", "fk_carrot", "carrot", "fk_corn", "corn", "fk_eggplant", "eggplant", "fk_leek", "leek", "fk_onion", "onion",
            "fk_radish", "radish", "fk_egg", "egg", "fk_honey", "honey", "fk_cheese", "cheese", "fk_milk", "carton", "fk_pear", "pear", "fk_grapes", "grapes",
            "fk_cauliflower", "cauliflower", "fk_broccoli", "broccoli", "fk_pumpkin", "pumpkin-basic" };
        for (int k = 0; k < produce.Length; k += 2) { D(produce[k], produce[k + 1], 500, I_FOOD, A_SLOT_ONLY, BOTH, 0); ThOf(produce[k], TH_FARM | TH_KITCHEN | TH_PANTRY); }
        // park: the cafe terrace's kiosks (Coaster Kit at x1.4: 1.4 m wide, about 2.2 m high) and its food; the skate
        // corner (Mini Skate at x2); the pond's lily pads (flat, like rugs), footbridge, ducks; more planting
        D("ck_food", "stall-food", 1400, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 1); Kind(K_COUNTER); Cap(1);
        D("ck_drinks", "stall-drinks", 1400, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 1); Kind(K_COUNTER); Cap(1);
        D("ck_info", "stall-information", 1400, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 1); Cap(1);
        D("ck_toilets", "stall-toilets", 1400, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 1); Cap(1);
        D("ck_bench", "coaster_bench", 1700, 0, A_OUT_EDGE, FL_SOLID | OUT, 0); Col(C_SEAT); Kind(K_SEAT);
        D("ck_trash", "trash", 1700, 0, A_OUT_EDGE, OUT, 0); Cap(2);
        D("ck_flowers", "flowers", 1700, 0, A_OUT_OPEN, FL_FLAT | OUT, 0); Cap(3);
        string[] cafe = { "fk_iceCream", "ice-cream", "fk_coffee", "cup-coffee", "fk_croissant", "croissant", "fk_sandwich", "sandwich", "fk_muffin", "muffin" };
        for (int k = 0; k < cafe.Length; k += 2) { D(cafe[k], cafe[k + 1], 400, I_FOOD | I_SETTING, A_SLOT_ONLY, BOTH, 0); ThOf(cafe[k], TH_PATIO | TH_DINER); }
        string[] skate = { "sk_halfPipe", "half-pipe", "sk_halfPipeLow", "half-pipe-low", "sk_ramp", "ramp", "sk_rampRail", "ramp-rail", "sk_rail", "rail-high",
            "sk_box", "obstacle-middle-rail", "sk_steps", "steps-rail" };
        for (int k = 0; k < skate.Length; k += 2) { D(skate[k], skate[k + 1], 2000, 0, A_CENTER, FL_SOLID | OUT, 1); Cap(1); Jit(0, 0); }
        D("sk_benches", "benches", 2000, 0, A_OUT_EDGE, FL_SOLID | OUT, 0); Col(C_SEAT); Kind(K_SEAT); Cap(1);
        D("sk_scooter", "scooter", 1600, 0, A_OUT_OPEN, FL_LIGHT | BOTH, 1); Loose(); Jit(6, 40); Cap(1);
        D("sk_board", "skateboard", 1600, 0, A_OUT_OPEN, FL_LIGHT | BOTH, 1); Loose(); Jit(6, 40); Cap(2);
        D("n_lily", "lily_large", 2500, 0, A_OUT_OPEN, FL_FLAT | OUT, 0); Jit(8, 180); Cap(6);
        D("n_lilySmall", "lily_small", 2500, 0, A_OUT_OPEN, FL_FLAT | OUT, 0); Jit(8, 180); Cap(6);
        D("n_footbridge", "bridge_wood", 2400, 0, A_CENTER, FL_SOLID | OUT, 1); Cap(1); Jit(0, 0);
        D("p_duck", "animal-chick", 280, 0, A_OUT_OPEN, FL_LIGHT | OUT, 2); Jit(10, 180); Loose(); Cap(4);
        D("n_bushSmall", "plant_bushSmall", 3000, 0, A_OUT_OPEN, OUT, 0); Jit(10, 180); Kind(K_PLANT);
        D("n_bushTriangle", "plant_bushTriangle", 3000, 0, A_OUT_OPEN, OUT, 0); Jit(10, 180); Kind(K_PLANT);

        // ===== revision 11: the KayKit-furnished house, the box room, teen room, playroom and pantry =====
        // KayKit Furniture Bits twins of the Kenney house furniture (Twin below): same anchors, KayKit scales
        D("q_couchPillows", "couch_pillows", 700, 0, A_WALL, FL_SOLID | BOTH, 2); Col(C_SEAT); Clear(1); Kind(K_SEAT);
        D("q_armchairPillows", "armchair_pillows", 550, 0, A_WALL, FL_SOLID | BOTH, 1); Col(C_SEAT); Clear(1); Kind(K_SEAT); Cap(2);
        D("q_bedDouble", "bed_double_A", 700, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Kind(K_BED);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defTopY[cur], 300, 0, SZ_M, I_TOY | I_PILLOW, 200); }
        D("q_bedSingleB", "bed_single_B", 700, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Kind(K_BED);
        { int g = Group(G_INDEPENDENT, 1000, 0); Slot(g, S_ON_TOP, 0, defTopY[cur], 300, 0, SZ_M, I_TOY | I_PILLOW, 250); }
        D("q_cabinetDeco", "cabinet_medium_decorated", 800, 0, A_WALL, FL_SOLID | FL_HEAVY | IN, 1); Clear(1); Kind(K_STORE);
        D("q_cabinetSmallDeco", "cabinet_small_decorated", 800, 0, A_WALL, FL_SOLID | IN, 1); Clear(1); Kind(K_STORE);
        D("q_cactusB", "cactus_medium_B", 650, 0, A_CORNER, BOTH, 0);
        D("q_cactusSmallB", "cactus_small_B", 500, I_DECOR, A_SLOT_ONLY, BOTH, 0);
        D("q_chairB", "chair_B", 750, I_CHAIR, A_SLOT_ONLY, FL_LIGHT | BOTH, 0); Col(C_SEAT);
        D("q_chairC", "chair_C", 750, I_CHAIR, A_SLOT_ONLY, FL_LIGHT | BOTH, 0); Col(C_SEAT);
        D("q_chairWood", "chair_A_wood", 750, I_CHAIR, A_SLOT_ONLY, FL_LIGHT | BOTH, 0); Col(C_SEAT);
        D("q_stoolWood", "chair_stool_wood", 700, I_STOOL, A_BESIDE, FL_LIGHT | BOTH, 0); Beside(K_COUNTER); Loose(); Cap(3);
        D("q_rugOval", "rug_oval_A", 800, I_RUG, A_CENTER, FL_FLAT | IN, 1); Fp(5, 3);
        D("q_rugStripes", "rug_rectangle_stripes_A", 800, I_RUG, A_SLOT_ONLY, FL_FLAT | IN, 1); Fp(5, 3);
        D("q_tableLong", "table_medium_long", 750, 0, A_CENTER, FL_SOLID | IN, 1); Col(C_TABLE); SlotsDining(false, 900); Kind(K_TABLE);
        D("q_sideTable", "table_small", 650, 0, A_WALL, FL_SOLID | IN, 1); Col(C_TABLE); SlotsTop(1, I_DECOR | I_LAMP, SZ_M, 550);
        D("q_bedside", "cabinet_small", 500, 0, A_BESIDE, IN, 1); SlotsTop(1, I_LAMP | I_DECOR, SZ_M, 650); Beside(K_BED);
        D("q_frameMedium", "pictureframe_medium", 450, I_DECOR, A_SLOT_ONLY, BOTH, 0);
        D("q_pillowB", "pillow_B", 600, I_PILLOW, A_SLOT_ONLY, FL_LIGHT | BOTH, 0);
        D("q_bookSingle", "book_single", 500, I_DECOR, A_SLOT_ONLY, BOTH, 0);
        D("hb_armchairRed", "chair_large_red", 500, 0, A_WALL, FL_SOLID | BOTH, 1); Col(C_SEAT); Clear(1); Kind(K_SEAT); Cap(2);
        D("hb_armchairBlue", "chair_large_blue", 500, 0, A_WALL, FL_SOLID | BOTH, 1); Col(C_SEAT); Clear(1); Kind(K_SEAT); Cap(2);
        // the teen room (Mixed Bag 1 and 2 at about x0.55: their raw sizes are about 1.8 times real)
        D("mb_gamerChairA", "gamer_chair_A", 650, I_DESKCHAIR, A_SLOT_ONLY, FL_LIGHT | IN, 1); Col(C_SEAT); Fp(1, 1);
        D("mb_gamerChairB", "gamer_chair_B", 650, I_DESKCHAIR, A_SLOT_ONLY, FL_LIGHT | IN, 1); Col(C_SEAT); Fp(1, 1);
        D("mb_headphonesA", "cat_headphones_A", 220, I_DECOR, A_SLOT_ONLY, BOTH, 1);
        D("mb_headphonesC", "cat_headphones_C", 220, I_DECOR, A_SLOT_ONLY, BOTH, 1);
        D("mb_console", "handheld_console", 450, I_DECOR | I_TOY, A_SLOT_ONLY, BOTH, 1);
        D("mb_guitarA", "guitar_A", 550, 0, A_WALL, FL_LIGHT | IN, 1); Jit(2, 8); Cap(1);
        D("mb_guitarB", "guitar_B", 550, 0, A_WALL, FL_LIGHT | IN, 1); Jit(2, 8); Cap(1);
        D("mb_guitarCase", "guitarcase", 600, 0, A_WALL, FL_LIGHT | IN, 1); Jit(2, 8); Cap(1);
        D("mb_comics", "comicbooks_stacked", 400, I_BOX | I_DECOR, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Jit(4, 30);
        D("mb_comicBox", "comicbox_closed", 550, I_BOX, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Jit(4, 15);
        D("mb_comicBoxFull", "comicbox_filled", 550, I_BOX, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Jit(4, 15);
        D("mb_skates", "rollerskate_pair", 600, I_TOY, A_WALL, FL_LIGHT | BOTH, 1); Loose(); Jit(4, 30); Cap(1);
        D("mb_plushA", "chicken_plushie_A", 450, I_TOY, A_BESIDE, FL_LIGHT | BOTH, 1); Jit(8, 180); Loose(); Beside(K_BED | K_SEAT);
        D("mb_plushB", "chicken_plushie_B", 450, I_TOY, A_BESIDE, FL_LIGHT | BOTH, 1); Jit(8, 180); Loose(); Beside(K_BED | K_SEAT);
        D("mb_puzzle", "puzzlecube_complete", 450, I_TOY | I_DECOR, A_SLOT_ONLY, BOTH, 0);
        D("mb_bottle", "waterbottle_A", 380, I_DECOR, A_SLOT_ONLY, BOTH, 0);
        D("mb_camera", "instantcamera", 380, I_DECOR, A_SLOT_ONLY, BOTH, 1);
        D("mb_helmet", "bicycle_helmet", 260, 0, A_WALL, FL_LIGHT | BOTH, 1); Loose(); Jit(4, 40); Cap(1);
        // the box room: trunks, chests and boxes (KayKit Dungeon at x0.5), frames and a mirror leaning at the walls
        D("dg_trunkL", "trunk_large_A", 550, 0, A_WALL, FL_SOLID | BOTH, 1); Jit(3, 6); Kind(K_STORE);
        D("dg_trunkM", "trunk_medium_B", 550, 0, A_WALL, FL_SOLID | BOTH, 1); Jit(3, 6);
        D("dg_trunkS", "trunk_small_C", 550, I_BOX, A_WALL, FL_LIGHT | BOTH, 1); Jit(3, 10); Loose();
        D("dg_chest", "dungeon_chest", 500, 0, A_WALL, FL_SOLID | BOTH, 1); Jit(3, 6); Kind(K_STORE);
        D("dg_boxL", "box_large", 450, 0, A_WALL, FL_SOLID | BOTH, 0); Jit(4, 8);
        D("dg_boxS", "box_small", 450, I_BOX, A_WALL, FL_LIGHT | BOTH, 0); Jit(4, 12); Loose();
        D("mb_frameA", "frame_A_large", 600, 0, A_WALL, FL_LIGHT | BOTH, 1); Jit(2, 6);
        D("mb_frameB", "frame_B_large", 500, 0, A_WALL, FL_LIGHT | BOTH, 1); Jit(2, 6);
        D("mb_frameC", "frame_C", 600, 0, A_WALL, FL_LIGHT | BOTH, 1); Jit(2, 6);
        D("q_frameLA", "pictureframe_large_A", 700, 0, A_WALL, FL_LIGHT | BOTH, 1); Jit(2, 6);
        D("q_frameLB", "pictureframe_large_B", 700, 0, A_WALL, FL_LIGHT | BOTH, 1); Jit(2, 6);
        D("mb_mirror", "ornate_mirror", 450, 0, A_WALL, FL_SOLID | IN, 1); Cap(1);
        D("mb_sled", "sled", 650, 0, A_WALL, FL_LIGHT | BOTH, 1); Jit(3, 10); Cap(1);
        D("mb_mimic", "book_mimic", 450, 0, A_WALL, FL_LIGHT | IN, 1); Loose(); Cap(1);
        // the pantry: shelves of jars and bottles, a keg, barrels, more crates of food
        D("dg_shelves", "shelves", 900, 0, A_WALL, FL_SOLID | FL_HEAVY | FL_SEEINTO, 1); Col(C_SHELF); Clear(1); SlotsShelves(3, I_FOOD | I_KITCHENWARE | I_STORE, 650, true); Kind(K_STORE);
        D("dg_keg", "keg", 400, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Cap(1);
        D("dg_barrel", "barrel_small", 600, 0, A_WALL, FL_SOLID | BOTH, 0); Jit(3, 30);
        D("dg_bottleA", "bottle_A_green", 400, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("dg_bottleB", "bottle_B_brown", 400, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("r_jarB", "jar_B_medium", 400, I_KITCHENWARE | I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("r_jarC", "jar_C_small", 400, I_KITCHENWARE | I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("r_jarD", "jar_D_large", 400, I_KITCHENWARE | I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("mb_pickles", "pickle_jar", 500, I_FOOD, A_SLOT_ONLY, BOTH, 0);
        D("r_crateBuns", "crate_buns", 350, I_CRATE, A_WALL, BOTH, 0); Loose();
        D("r_crateOnions", "crate_onions", 350, I_CRATE, A_WALL, BOTH, 0); Loose();
        D("r_crateHam", "crate_ham", 350, I_CRATE, A_WALL, BOTH, 0); Loose();
        // the playroom: soft-play dice and board-game pieces (Board Game Bits at x0.5 to x0.6), toy cars (Toy Car Kit
        // at x0.7), a wooden train, footstools, a round carpet, balls (Holiday Bits)
        string[] bg = { "bg_diceA", "D6_A_red", "600", "bg_diceB", "D6_B_blue", "600", "bg_d20", "D20_yellow", "500", "bg_pawn", "pawn_B_green", "500",
            "bg_meeple", "meeple_red", "500", "bg_house", "building_blue", "500", "bg_hourglass", "hourglass", "300",
            "tc_monster", "vehicle-monster-truck", "700", "tc_racer", "vehicle-racer", "700", "tc_truck", "vehicle-truck", "700", "tc_suv", "vehicle-suv", "700",
            "hb_train", "train_locomotive", "1000", "hb_tender", "train_tender_coal", "1000" };
        for (int k = 0; k < bg.Length; k += 3) { D(bg[k], bg[k + 1], int.Parse(bg[k + 2]), I_TOY, A_ANY, FL_LIGHT | BOTH, 1); Jit(8, 60); Loose(); }
        D("hb_footstoolG", "footstool_green", 700, 0, A_ANY, FL_LIGHT | BOTH, 1); Col(C_SEAT); Jit(6, 30);
        D("hb_footstoolR", "footstool_red", 700, 0, A_ANY, FL_LIGHT | BOTH, 1); Col(C_SEAT); Jit(6, 30);
        D("hb_stool", "stool", 600, I_STOOL, A_WALL, FL_LIGHT | BOTH, 0); Col(C_SEAT); Loose(); Cap(2);
        D("hb_carpet", "carpet_round_large", 700, I_RUG, A_CENTER, FL_FLAT | IN, 1); Fp(4, 4);
        D("hb_basketball", "basketball", 340, I_TOY, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Jit(10, 180);
        D("hb_football", "football", 420, I_TOY, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Jit(10, 180);
        // the shop's machines by the door and the toy shop's stock (Mini Arcade at x2, the Mini Shop's scale)
        D("ma_claw", "claw-machine", 2000, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Cap(1);
        D("ma_vending", "vending-machine", 2000, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Cap(1);
        D("ma_arcade", "arcade-machine", 2000, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Cap(1);
        // the park, more of it: a square fountain, a curved hedge, two statues, the pond's beaver, skate pieces
        D("f_fountainSq", "fountain-square-detail", 1300, 0, A_CENTER, FL_SOLID | FL_HEAVY | FL_FIXTURE, 1); Cap(1); Jit(0, 0);
        D("f_hedgeCurve", "hedge-large-curved", 1800, 0, A_OUT_EDGE, FL_SOLID | OUT, 0); Kind(K_PLANT); Jit(2, 2); Cap(2);
        D("n_statueBlock", "statue_block", 2000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY, 1); Cap(1); Jit(2, 10);
        D("n_columnBroken", "statue_columnDamaged", 2000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY, 1); Cap(1); Jit(2, 10);
        D("p_beaver", "animal-beaver", 400, 0, A_OUT_OPEN, FL_LIGHT | OUT, 2); Jit(10, 180); Cap(1);
        string[] sk2 = { "sk_grind", "obstacle-box-grind", "sk_railLow", "rail-low", "sk_platform", "structure-wood-platform" };
        for (int k = 0; k < sk2.Length; k += 2) { D(sk2[k], sk2[k + 1], 2000, 0, A_CENTER, FL_SOLID | OUT, 1); Cap(1); Jit(0, 0); }
        D("sk_pallet", "pallet", 2000, 0, A_OUT_EDGE, FL_SOLID | OUT, 0); Cap(2);

        // ===== revision 12: the zoo and the log cabin =====
        // the zoo's animals (Cube Pets, scaled to the beast: a giraffe 1.9 m tall, an elephant 1.4 m, a penguin 0.7 m)
        string[] zoo = { "z_giraffe", "animal-giraffe", "1100", "z_elephant", "animal-elephant", "1000", "z_lion", "animal-lion", "700", "z_tiger", "animal-tiger", "700",
            "z_panda", "animal-panda", "700", "z_polar", "animal-polar", "900", "z_deer", "animal-deer", "800", "z_fox", "animal-fox", "500",
            "z_koala", "animal-koala", "450" };
        for (int k = 0; k < zoo.Length; k += 3) { D(zoo[k], zoo[k + 1], int.Parse(zoo[k + 2]), 0, A_OUT_OPEN, FL_SOLID | OUT, 2); Jit(10, 180); Cap(3); }
        string[] zooS = { "z_monkey", "animal-monkey", "450", "z_penguin", "animal-penguin", "450", "z_fish", "animal-fish", "300", "z_crab", "animal-crab", "300",
            "z_parrot", "animal-parrot", "380", "z_bee", "animal-bee", "350", "z_caterpillar", "animal-caterpillar", "350" };
        for (int k = 0; k < zooS.Length; k += 3) { D(zooS[k], zooS[k + 1], int.Parse(zooS[k + 2]), 0, A_OUT_OPEN, FL_LIGHT | OUT, 2); Jit(10, 180); Cap(5); }
        // the log cabin: the KayKit wood stove (the one piece from another family), bunks, closed bookcases, a small
        // kitchen; the porch's Holiday Kit benches and lantern posts, sleds; the campground's Survival Kit tents,
        // bedrolls, the spit and the fish stand by the fire; the wood yard's chopping block, anvil and grindstone
        D("cb_woodstove", "woodstove", 550, 0, A_WALL, FL_SOLID | FL_HEAVY | FL_FIXTURE, 2); Clear(1); Cap(1);
        D("k_bedBunk", "bedBunk", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Kind(K_BED);
        D("k_bookcaseClosed", "bookcaseClosed", K, 0, A_WALL, FL_SOLID | IN, 1); Clear(1); Kind(K_STORE);
        D("k_bookcaseWide", "bookcaseClosedWide", K, 0, A_WALL, FL_SOLID | FL_HEAVY | IN, 1); Clear(1); Kind(K_STORE); SlotsTop(2, I_DECOR | I_LAMP, SZ_M, 500);
        D("k_lampSquare", "lampSquareFloor", K, 0, A_CORNER, FL_LIGHT | BOTH, 1);
        D("k_fridgeSmall", "kitchenFridgeSmall", K, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(2); Kind(K_STORE); SlotsTop(1, I_KITCHENWARE | I_FOOD, SZ_M, 500);
        D("k_stoveElectric", "kitchenStoveElectric", K, 0, A_WALL, FL_SOLID | FL_FIXTURE | FL_HEAVY, 2); Clear(2); Kind(K_COUNTER); SlotsTop(1, I_COOK, SZ_M, 600);
        D("k_bedsideDrawer", "cabinetBedDrawer", K, 0, A_BESIDE, IN, 1); SlotsTop(1, I_LAMP | I_DECOR, SZ_M, 650); Beside(K_BED);
        D("hk_bench", "holiday_bench", 1500, 0, A_WALL, FL_SOLID | BOTH, 0); Col(C_SEAT); Kind(K_SEAT);
        D("hk_benchShort", "bench-short", 1500, 0, A_WALL, FL_SOLID | BOTH, 0); Col(C_SEAT); Kind(K_SEAT);
        D("hk_lantern", "holiday_lantern", 1000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 1); Jit(2, 10); Cap(3);
        D("hk_lanternSmall", "lantern-hanging", 550, I_LAMP | I_DECOR, A_SLOT_ONLY, BOTH, 1);
        D("hk_sled", "holiday_sled", 1200, 0, A_WALL, FL_LIGHT | BOTH, 1); Jit(3, 10); Cap(1);
        D("hk_sledLong", "sled-long", 1200, 0, A_WALL, FL_LIGHT | BOTH, 1); Jit(3, 10); Cap(1);
        D("s_tent", "tent", 3000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 2); Jit(5, 30); Kind(K_TENT); Cap(2);
        D("s_tentCanvas", "tent-canvas", 3000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 2); Jit(5, 30); Kind(K_TENT); Cap(2);
        D("s_tentHalf", "tent-canvas-half", 3000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 2); Jit(5, 30); Kind(K_TENT); Cap(2);
        D("n_tentBig", "tent_detailedClosed", 3000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 2); Jit(5, 30); Kind(K_TENT); Cap(1);
        D("n_tentSmallOpen", "tent_smallOpen", 3000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 2); Jit(5, 30); Kind(K_TENT); Cap(1);
        D("s_bedrollFrame", "bedroll-frame", 2600, I_BEDROLL, A_BESIDE, FL_LIGHT | BOTH, 1); Loose(); Beside(K_TENT | K_FIRE | K_BED);
        D("s_bedrollPacked", "bedroll-packed", 2600, I_BOX, A_WALL, FL_LIGHT | BOTH, 1); Loose(); Jit(4, 20);
        D("n_cot", "bed", 2600, 0, A_WALL, FL_SOLID | BOTH, 2); Kind(K_BED); Cap(2);
        D("s_spit", "campfire-stand", 3000, 0, A_BESIDE, FL_LIGHT | BOTH, 1); Beside(K_FIRE); Cap(1);
        D("s_fishStand", "campfire-fishing-stand", 3000, 0, A_BESIDE, FL_LIGHT | BOTH, 1); Beside(K_FIRE); Cap(1);
        D("s_fish", "fish", 2000, I_FOOD, A_SLOT_ONLY, BOTH, 0); ThOf("s_fish", TH_CABIN | TH_CAMP | TH_KITCHEN);
        D("s_fishLarge", "fish-large", 2000, I_FOOD, A_SLOT_ONLY, BOTH, 0); ThOf("s_fishLarge", TH_CABIN | TH_CAMP | TH_KITCHEN);
        D("s_anvil", "workbench-anvil", 2800, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Kind(K_WORK); Cap(1);
        D("s_grind", "workbench-grind", 2800, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Kind(K_WORK); Cap(1);
        D("s_axeB", "tool-axe-upgraded", 2500, I_TOOL | I_CHOP, A_WALL, BOTH, 1); Loose(); Beside(K_WORK | K_STUMP);
        D("s_shovelB", "tool-shovel-upgraded", 2500, I_TOOL, A_WALL, BOTH, 1); Loose(); Beside(K_WORK | K_STUMP);
        D("s_logSmall", "tree-log-small", 2000, I_SEAT_OUT, A_OUT_OPEN, FL_SOLID | OUT, 0); Kind(K_STUMP);
        D("s_block", "tree-trunk", 2500, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 0); Kind(K_STUMP); Cap(1);
        D("s_planks", "resource-planks", 2400, 0, A_WALL, BOTH, 0); Jit(4, 10);
        D("s_wood", "resource-wood", 3000, 0, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Jit(6, 40);
        D("s_stoneL", "resource-stone-large", 2400, 0, A_OUT_OPEN, FL_SOLID | OUT, 0); Jit(6, 180);
        D("s_rockFlat", "rock-flat-grass", 1400, 0, A_OUT_OPEN, FL_SOLID | OUT, 0); Jit(6, 180); Cap(1);
        D("n_paddle", "canoe_paddle", 2000, 0, A_WALL, FL_LIGHT | BOTH, 0); Loose(); Jit(4, 15);
        // the funfair: the Mini Arcade's machines (x2, as the shop's), the Coaster Kit's ride pieces and cars (x1.4), a
        // circus tent (Mixed Bag 1), fairground food (Food Kit, as slot items)
        string[] ma = { "ma_airHockey", "air-hockey", "ma_basketball", "basketball-game", "ma_dance", "dance-machine", "ma_slots", "gambling-machine",
            "ma_pinball", "pinball", "ma_prizeWheel", "prize-wheel", "ma_prizes", "prizes", "ma_tickets", "ticket-machine" };
        for (int k = 0; k < ma.Length; k += 2) { D(ma[k], ma[k + 1], 2000, 0, k == 0 ? A_CENTER : A_WALL, FL_SOLID | FL_HEAVY | BOTH, 2); Clear(1); Cap(2); }
        D("ma_register", "mini_cash-register", 2000, 0, A_ANY, FL_SOLID | FL_HEAVY | IN, 1); Clear(1); Kind(K_COUNTER); Cap(1); SlotsTop(1, I_FOOD, SZ_S, 350);
        string[] ck = { "ck_rideIn", "ride-entrance", "ck_rideOut", "ride-exit", "ck_stationFence", "station-fence", "ck_stationGate", "station-gate" };
        for (int k = 0; k < ck.Length; k += 2) { D(ck[k], ck[k + 1], 1400, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 1); Cap(2); }
        D("ck_queue", "queue-straight", 1400, 0, A_OUT_OPEN, FL_SOLID | OUT, 0); Cap(4); Jit(0, 0);
        string[] cars = { "ck_train", "coaster-train", "ck_trainFront", "coaster-train-front", "ck_trainWood", "coaster-train-wooden", "ck_trainSafety", "coaster-train-safety",
            "ck_flume", "train-log-flume", "ck_monorail", "train-monorail", "ck_trainHanging", "coaster-train-hanging" };
        for (int k = 0; k < cars.Length; k += 2) { D(cars[k], cars[k + 1], 1400, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 2); Jit(0, 0); Cap(2); }
        D("mb_circus", "circus_tent", 700, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 2); Cap(1); Jit(0, 0);
        // revision 12, more of the Car Kit: the emergency services and the bin lorry in for repair, a delivery van,
        // go-karts for the funfair, spare wheels and parts; the workshop tool wall (generated: the packs have none)
        string[] cars2 = { "c_ambulance", "ambulance", "c_firetruck", "firetruck", "c_binLorry", "garbage-truck", "c_delivery", "delivery", "c_deliveryFlat", "delivery-flat" };
        for (int k = 0; k < cars2.Length; k += 2) { D(cars2[k], cars2[k + 1], CK, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | OUT, 2); Kind(K_WORK); Cap(1); }
        string[] karts = { "c_kartA", "kart-oobi", "c_kartB", "kart-oodi", "c_kartC", "kart-ooli" };
        for (int k = 0; k < karts.Length; k += 2) { D(karts[k], karts[k + 1], 1000, 0, A_OUT_OPEN, FL_SOLID | OUT, 2); Jit(6, 30); Cap(2); }
        D("c_wheelTruck", "wheel-truck", CK, 0, A_WALL, BOTH, 1); Jit(4, 25); Loose(); Beside(K_WORK | K_STORE);
        D("c_wheelRacing", "wheel-racing", CK, 0, A_WALL, BOTH, 1); Jit(4, 25); Loose(); Beside(K_WORK | K_STORE);
        D("c_spoiler", "debris-spoiler-a", CK, 0, A_WALL, BOTH, 1); Jit(4, 12); Beside(K_WORK | K_STORE);
        D("c_axle", "debris-drivetrain-axle", CK, 0, A_WALL, BOTH, 1); Jit(4, 12); Beside(K_WORK | K_STORE);
        D("gen_toolwall", "gen_toolwall", 1000, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Clear(1); Kind(K_WORK); Cap(1);
        // revision 12, seasonal decorations (G58 pick 6): placed only on a map that has the holiday (MapGen.holiday,
        // HolidayDecor), never as a wrong-category spawn. Winter: Holiday Bits' decorated tree and presents (x0.5), the
        // Holiday Kit's snowman, reindeer, nutcracker and candy canes; Halloween: carved pumpkins and the Graveyard
        // Kit's candle lanterns, Halloween Bits' jack-o'-lanterns, skulls, candles and a gravestone for the lawn
        D("hb_xmasTree", "christmas_tree_decorated", 500, 0, A_CORNER, FL_SOLID | FL_HEAVY | BOTH, 1); NoLeak(); Cap(1); Kind(K_STORE);
        string[] presents = { "hb_presentA", "present_A_red", "hb_presentB", "present_B_blue", "hb_presentC", "present_C_green", "hb_presentD", "present_D_yellow",
            "hb_presentE", "present_E_white", "hb_presentS", "present_sphere_A_red" };
        for (int k = 0; k < presents.Length; k += 2) { D(presents[k], presents[k + 1], 500, I_BOX | I_TOY, A_BESIDE, FL_LIGHT | BOTH, 1); NoLeak(); Jit(6, 30); Beside(K_STORE | K_SEAT); ThOf(presents[k], TH_WINTER); }
        D("hb_gingerbread", "gingerbread_house_decorated", 300, I_CENTERPIECE, A_SLOT_ONLY, BOTH, 1); NoLeak(); ThOf("hb_gingerbread", TH_WINTER);
        D("hb_hotChoc", "hot_chocolate_decorated", 300, I_TABLEWARE | I_SETTING, A_SLOT_ONLY, BOTH, 0); NoLeak(); ThOf("hb_hotChoc", TH_WINTER);
        D("hb_snowman", "snowman_B", 550, 0, A_OUT_OPEN, FL_SOLID | OUT, 2); NoLeak(); Cap(2); Jit(4, 40);
        D("hk_snowman", "snowman-hat", 1200, 0, A_OUT_OPEN, FL_SOLID | OUT, 2); NoLeak(); Cap(2); Jit(4, 40);
        D("hk_reindeer", "reindeer", 1300, 0, A_OUT_OPEN, FL_SOLID | OUT, 2); NoLeak(); Cap(2); Jit(4, 180);
        D("hk_nutcracker", "nutcracker", 1300, 0, A_WALL, FL_SOLID | BOTH, 2); NoLeak(); Cap(1);
        D("hk_candyCane", "candy-cane-red", 1600, 0, A_OUT_OPEN, FL_LIGHT | OUT, 1); NoLeak(); Cap(4); Jit(2, 20);
        D("hk_wreath", "wreath-decorated", 600, I_DECOR, A_SLOT_ONLY, BOTH, 1); NoLeak(); ThOf("hk_wreath", TH_WINTER);
        D("gk_pumpkin", "pumpkin-carved", 1800, 0, A_WALL, FL_LIGHT | BOTH, 1); NoLeak(); Cap(4); Jit(4, 40);
        D("gk_pumpkinTall", "pumpkin-tall-carved", 1800, 0, A_WALL, FL_LIGHT | BOTH, 1); NoLeak(); Cap(4); Jit(4, 40);
        D("gk_lanternCandle", "lantern-candle", 1800, 0, A_WALL, FL_LIGHT | BOTH, 1); NoLeak(); Cap(2);
        D("hw_jackOrange", "pumpkin_orange_jackolantern", 450, 0, A_WALL, FL_SOLID | BOTH, 1); NoLeak(); Cap(2); Jit(4, 40);
        D("hw_jackYellow", "pumpkin_yellow_jackolantern", 450, 0, A_WALL, FL_SOLID | BOTH, 1); NoLeak(); Cap(2); Jit(4, 40);
        D("hw_gravestone", "gravestone", 600, 0, A_OUT_OPEN, FL_SOLID | OUT, 1); NoLeak(); Cap(2); Jit(3, 15);
        D("hw_skull", "skull", 380, I_DECOR, A_SLOT_ONLY, BOTH, 1); NoLeak(); ThOf("hw_skull", TH_HALLOWEEN);
        D("hw_candles", "halloween_candle_triple", 400, I_DECOR | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0); NoLeak(); ThOf("hw_candles", TH_HALLOWEEN);
        D("hw_pumpkinSmall", "pumpkin_orange_small", 450, I_DECOR | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0); NoLeak(); ThOf("hw_pumpkinSmall", TH_HALLOWEEN);
        // revision 12, the chapel and its graveyard (G58 pick 7): the Graveyard Kit at x2 (pews, altars, candles,
        // pillars, urns, nine gravestones, crosses, crypts, a dug grave), Halloween Bits' shrine, plaque and graves
        D("gk_pew", "graveyard_bench", 1500, 0, A_CENTER, FL_SOLID | BOTH, 0); Col(C_SEAT); Kind(K_SEAT); Jit(0, 0);
        D("gk_benchOld", "bench-damaged", 2000, 0, A_WALL, FL_SOLID | BOTH, 0); Col(C_SEAT); Kind(K_SEAT);
        D("gk_altarStone", "altar-stone", 2000, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Clear(2); SlotsTop(3, I_DECOR | I_CENTERPIECE, SZ_M, 800); Cap(1);
        D("gk_altarWood", "altar-wood", 2000, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Clear(2); SlotsTop(3, I_DECOR | I_CENTERPIECE, SZ_M, 800); Cap(1);
        string[] chSlot = { "gk_candles", "candle-multiple", "gk_candle", "candle", "gk_chalice", "detail-chalice", "gk_bowl", "detail-bowl" };
        for (int k = 0; k < chSlot.Length; k += 2) { D(chSlot[k], chSlot[k + 1], 1800, I_DECOR | I_CENTERPIECE, A_SLOT_ONLY, BOTH, 0); ThOf(chSlot[k], TH_CHAPEL); }
        string[] chWall = { "gk_lanternGlass", "lantern-glass", "gk_lantern", "lantern-candle", "gk_pillarSmall", "pillar-small", "gk_pillarSquare", "pillar-square",
            "gk_urnRound", "urn-round", "gk_urnSquare", "urn-square", "gk_trunk", "trunk" };
        for (int k = 0; k < chWall.Length; k += 2) { D(chWall[k], chWall[k + 1], 2000, 0, A_WALL, FL_SOLID | BOTH, 1); Jit(2, 6); Cap(2); }
        D("hw_shrine", "shrine_candles", 600, 0, A_WALL, FL_SOLID | FL_HEAVY | BOTH, 1); Cap(1);
        D("hw_plaque", "plaque_candles", 500, 0, A_WALL, FL_SOLID | BOTH, 1); Cap(1);
        string[] stones = { "gk_gsBevel", "gravestone-bevel", "gk_gsBroken", "gravestone-broken", "gk_gsCross", "gravestone-cross", "gk_gsCrossL", "gravestone-cross-large",
            "gk_gsDeco", "gravestone-decorative", "gk_gsRoof", "gravestone-roof", "gk_gsRound", "gravestone-round", "gk_gsWide", "gravestone-wide", "gk_gsDebris", "gravestone-debris",
            "gk_cross", "cross", "gk_crossColumn", "cross-column", "gk_crossWood", "cross-wood" };
        for (int k = 0; k < stones.Length; k += 2) { D(stones[k], stones[k + 1], 2000, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 1); Jit(2, 6); }
        string[] graves = { "hw_graveA", "grave_A", "hw_graveB", "grave_B", "hw_marker", "gravemarker_A", "hw_stone", "gravestone" };
        for (int k = 0; k < graves.Length; k += 2) { D(graves[k], graves[k + 1], 550, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 1); Jit(2, 6); }
        D("gk_crypt", "crypt", 2000, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | FL_FIXTURE, 2); Cap(1);
        D("gk_cryptSmall", "crypt-small", 1600, 0, A_OUT_EDGE, FL_SOLID | FL_HEAVY | FL_FIXTURE, 2); Cap(1);
        D("gk_mound", "grave", 2000, 0, A_OUT_OPEN, FL_FLAT | OUT, 0); Jit(0, 0); Cap(2);
        D("gk_graveBorder", "grave-border", 2000, 0, A_OUT_OPEN, FL_FLAT | OUT, 0); Jit(0, 0); Cap(3);
        D("gk_dug", "shovel-dirt", 2000, 0, A_OUT_OPEN, FL_SOLID | OUT, 1); Cap(1);
        D("gk_shovel", "graveyard_shovel", 2000, I_TOOL, A_WALL, FL_LIGHT | BOTH, 1); Loose(); Beside(K_WORK | K_STUMP);
        D("gk_lightpost", "lightpost-single", 1800, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 1); Cap(3);
        D("gk_lightpostDouble", "lightpost-double", 1800, 0, A_OUT_OPEN, FL_SOLID | FL_HEAVY | OUT, 1); Cap(2);
        D("gk_fireBasket", "fire-basket", 2000, 0, A_OUT_OPEN, FL_SOLID | OUT, 1); Cap(1);
        string[] fairFood = { "fk_hotdog", "hot-dog", "fk_candy", "candy-bar", "fk_sundae", "sundae", "fk_iceCup", "ice-cream-cup", "fk_cupcake", "cupcake",
            "fk_donutS", "donut-sprinkles", "fk_fries", "fries", "fk_soda", "soda", "fk_cornDog", "corn-dog", "fk_popsicle", "popsicle" };
        for (int k = 0; k < fairFood.Length; k += 2) { D(fairFood[k], fairFood[k + 1], 500, I_FOOD, A_SLOT_ONLY, BOTH, 0); ThOf(fairFood[k], TH_FAIR | TH_DINER | TH_PATIO); }
        D("f_hedge", "hedge-large", 1800, 0, A_OUT_EDGE, FL_SOLID | OUT, 0); Kind(K_PLANT); Jit(2, 2);

        BuildThemeDefs();   // revision 12: the map themes' pieces (GenThemes.cs)
        BuildThemes();
        ThemeTwinThemes();
        SceneryRule();
        MinSize();
        BuildGroups();
        BuildRanges();
        BuildTwins();
        BuildThemeTwins();
        BuildTables();
    }

    // ---------------------------------------------------------------- revision 11: the KayKit-furnished house
    // A house is furnished from one of two families (MapGen.KayHouse): the Kenney Furniture Kit (as before) or KayKit
    // (Furniture Bits, Restaurant Bits' kitchen, Holiday Bits' big armchair). The plans name the Kenney pieces; in a
    // KayKit house each is replaced by its twin here before it is placed. The bathroom has no KayKit set and stays.
    static void Twin(string a, string b) { defTwin[FindDef(a)] = FindDef(b); }

    static void TTwin(int theme, string a, string b) { defThemeTwin[FindDef(a) * 3 + theme] = FindDef(b); }

    // the theme twins: filled in GenThemes.cs (one table per theme)
    static void BuildThemeTwins() { BuildSpaceTwins(); BuildMedievalTwins(); ThemeCaps(); BareTops(); }

    // Cor (playtest): nothing is put on a chest or a trunk (it looks odd, and they open), and nothing more on a
    // piece whose model already carries its things (the "decorated" tables, cabinets, boxes, barrels and kegs).
    // Such a def keeps no slots at all. Beds keep theirs (a decorated bed is a made bed).
    static void BareTops()
    {
        for (int d = 0; d < nDefs; d++)
        {
            if (defSlotCount[d] == 0) continue;
            string m = GenModels.Name[defModel[d]].ToLower();
            bool chest = (m.Contains("chest") || m.Contains("trunk")) && !m.Contains("tree");
            bool laid = m.Contains("decorated") && !m.Contains("bed");
            if (chest || laid) defSlotCount[d] = 0;
            // a display table (the space base's hologram and planet tables) keeps its top clear; the seats round it
            // stay (Cor: things stood on the planet table)
            else if (m.StartsWith("table-display"))
            {
                int s0 = defSlotStart[d], n = 0;
                for (int i = s0; i < s0 + defSlotCount[d]; i++)
                {
                    if (slotType[i] == S_ON_TOP) continue;
                    int j = s0 + n++;
                    slotType[j] = slotType[i]; slotX[j] = slotX[i]; slotY[j] = slotY[i]; slotZ[j] = slotZ[i]; slotYaw[j] = slotYaw[i];
                    slotSize[j] = slotSize[i]; slotAccept[j] = slotAccept[i]; slotFill[j] = slotFill[i]; slotGroup[j] = slotGroup[i]; slotHiding[j] = slotHiding[i];
                }
                defSlotCount[d] = n;
            }
        }
    }

    static void BuildTwins()
    {
        Twin("k_sofa", "q_couchPillows"); Twin("k_designSofa", "q_couch"); Twin("k_loungeChair", "q_armchairPillows"); Twin("k_designChair", "q_armchair");
        Twin("k_recliner", "hb_armchairRed"); Twin("k_tableCoffee", "q_tableLow"); Twin("k_table", "q_tableLong");
        Twin("k_chair", "q_chairB"); Twin("k_chairCushion", "q_chairC"); Twin("k_bedDouble", "q_bedDouble"); Twin("k_bedSingle", "q_bedSingleB");
        Twin("k_bookcase", "q_cabinetDeco"); Twin("k_bookcaseLow", "q_cabinetSmallDeco"); Twin("k_lampFloor", "q_lampStanding"); Twin("k_lampTable", "q_lampTable");
        Twin("k_rug", "q_rugOval"); Twin("k_rugLong", "q_rugStripes"); Twin("k_sideTable", "q_sideTable"); Twin("k_bedside", "q_bedside");
        Twin("k_plant", "q_cactusB"); Twin("k_plantSmall", "q_cactusSmallB"); Twin("k_books", "q_bookSingle"); Twin("k_pillow", "q_pillowB");
        // revision 12: only the fridge; the Restaurant Bits units are 1.4 m modules, and three of them do not fit along a
        // house kitchen's walls (revision 11 had the kitchen's run fail in half of the KayKit houses)
        Twin("k_fridge", "r_fridge");
        Twin("k_stool", "q_stoolWood");
    }

    // ---------------------------------------------------------------- room themes of the items that sit on furniture
    // Cor (21 Nov): what no person placed is scenery: it stands on the map but is no disguise (cannot be cloned, is
    // not on the menu, never a wrong-category spawn), and a hunter's swing at it wears nothing (PHLocal: a def that
    // is no disguise costs nothing). Trees, bushes, flowers, rocks, stumps, loose logs, mushrooms, lilies, and
    // the pieces a map theme puts in their place (a boulder, a crystal spire, rubble). Pots, planters, crops in
    // rows and stacked logs were placed by someone and stay props.
    static readonly string[] NaturalPrefix = { "n_tree", "n_bush", "n_flower", "n_rock", "n_stump", "n_mushroom", "n_lily" };
    static readonly string[] NaturalId = { "n_log", "n_logLarge" };

    public static bool IsNatural(int d)
    {
        for (int guard = 0; guard < 8 && d >= 0; guard++)
        {
            if ((defFlags[d] & FL_SCENERY) != 0) return true;
            string id = defId[d];
            for (int i = 0; i < NaturalPrefix.Length; i++) if (id.StartsWith(NaturalPrefix[i])) return true;
            for (int i = 0; i < NaturalId.Length; i++) if (id == NaturalId[i]) return true;
            d = likeBase[d] - 1;   // a theme's twin of a natural piece is natural too
        }
        return false;
    }

    static void SceneryRule()
    {
        for (int d = 0; d < nDefs; d++)
            if (IsNatural(d)) { defDisguise[d] = 0; defLeakWeight[d] = 0; }
    }

    // Cor (22 Nov): small props bigger, at least the volume of a 20 cm cube (8 litres of bounding box). A disguise
    // smaller than that grows evenly (it keeps its shape) until its box holds MIN_VOLUME_L litres: an apple becomes a
    // 20 cm apple, a plate a 43 cm plate. Bigger things keep their size; scenery is no disguise and keeps its size
    // too; so do the place settings, laid one at each seat of a table. Sizes, footprint, top, slots on the prop,
    // class and mass follow the scale.
    public const int MIN_VOLUME_L = 8;

    static void MinSize()
    {
        for (int d = 0; d < nDefs; d++)
        {
            if (defDisguise[d] == 0 || (defTags[d] & I_SETTING) != 0) continue;
            long want = MIN_VOLUME_L * 1000000L;   // mm3 (a litre is 10^6 mm3)
            if (defSx[d] <= 0 || defSy[d] <= 0 || defSz[d] <= 0 || (long)defSx[d] * defSy[d] * defSz[d] >= want) continue;
            int k = 1000;   // per mille, integer steps (no floating point in the generator)
            while ((long)(defSx[d] * k / 1000) * (defSy[d] * k / 1000) * (defSz[d] * k / 1000) < want) k += 5;
            Grow(d, k);
        }
    }

    static void Grow(int d, int k)
    {
        int m = defModel[d];
        int oldTop = defTopY[d];
        defScale[d] = defScale[d] * k / 1000;
        int sc = defScale[d];
        defSx[d] = GenModels.SizeMm[m * 3] * sc / 1000;
        defSy[d] = GenModels.SizeMm[m * 3 + 1] * sc / 1000;
        defSz[d] = GenModels.SizeMm[m * 3 + 2] * sc / 1000;
        int w = Cells(defSx[d]), dd = Cells(defSz[d]);
        if (w > defW[d]) defW[d] = w;
        if (dd > defD[d]) defD[d] = dd;
        defTopY[d] = ModelTop(m) * sc / 1000;
        for (int i = defSlotStart[d]; i < defSlotStart[d] + defSlotCount[d]; i++)
        {
            slotX[i] = slotX[i] * k / 1000; slotZ[i] = slotZ[i] * k / 1000;
            slotY[i] = slotY[i] == oldTop ? defTopY[d] : slotY[i] * k / 1000;
        }
        int longest = defSx[d] > defSz[d] ? defSx[d] : defSz[d];
        if (defSy[d] > longest) longest = defSy[d];
        defSizeClass[d] = longest < 100 ? SZ_XS : (longest < 300 ? SZ_S : (longest < 600 ? SZ_M : (longest < 1000 ? SZ_L : SZ_XL)));
        defDisguise[d] = longest < 350 ? 1 : 2;
        long vol = (long)defSx[d] * defSy[d] / 1000 * defSz[d] / 1000;
        int kg = (int)(vol * 35 / 1000);
        defMassKg[d] = kg < 1 ? 1 : (kg > 40 ? 40 : kg);
    }

    static void BuildThemes()
    {
        ThOf("k_tv", TH_LIVING | TH_BED | TH_KIDS | TH_OFFICE | TH_TEEN | TH_PLAY); ThOf("k_tvVintage", TH_LIVING | TH_BED | TH_KIDS);
        ThOf("k_screen", TH_OFFICE | TH_BED | TH_KIDS | TH_TEEN); ThOf("k_laptop", TH_LIVING | TH_OFFICE | TH_BED | TH_KIDS | TH_TEEN);
        // revision 11
        ThOf("mb_gamerChairA", TH_TEEN); ThOf("mb_gamerChairB", TH_TEEN); ThOf("mb_headphonesA", TH_TEEN); ThOf("mb_headphonesC", TH_TEEN);
        ThOf("mb_console", TH_TEEN | TH_KIDS | TH_PLAY | TH_LIVING); ThOf("mb_puzzle", TH_PLAY | TH_TEEN | TH_KIDS | TH_OFFICE | TH_ATTIC);
        ThOf("mb_bottle", TH_TEEN | TH_OFFICE | TH_BED); ThOf("mb_camera", TH_TEEN | TH_LIVING | TH_ATTIC);
        ThOf("dg_bottleA", TH_PANTRY | TH_KITCHEN | TH_ATTIC); ThOf("dg_bottleB", TH_PANTRY | TH_KITCHEN);
        ThOf("r_jarB", TH_PANTRY | TH_KITCHEN | TH_PRO_KITCHEN); ThOf("r_jarC", TH_PANTRY | TH_KITCHEN); ThOf("r_jarD", TH_PANTRY | TH_KITCHEN | TH_PRO_KITCHEN);
        ThOf("mb_pickles", TH_PANTRY | TH_KITCHEN); ThOf("q_frameMedium", TH_HOME); ThOf("q_cactusSmallB", TH_HOME); ThOf("q_bookSingle", TH_HOME);
        ThOf("q_pillowB", TH_LIVING | TH_BED | TH_KIDS | TH_TEEN | TH_HALL);
        ThOf("k_lampTable", TH_HOME); ThOf("q_lampTable", TH_HOME);
        ThOf("k_books", TH_HOME); ThOf("q_books", TH_HOME); ThOf("q_frame", TH_HOME);
        ThOf("k_radio", TH_LIVING | TH_BED | TH_KITCHEN | TH_OFFICE | TH_STORE);
        ThOf("k_toaster", TH_KITCHEN); ThOf("k_microwave", TH_KITCHEN | TH_PRO_KITCHEN); ThOf("k_coffeeMachine", TH_KITCHEN | TH_OFFICE | TH_PRO_KITCHEN);
        ThOf("k_pillow", TH_LIVING | TH_BED | TH_KIDS | TH_TEEN | TH_HALL | TH_DINER); ThOf("q_pillow", TH_LIVING | TH_BED | TH_KIDS | TH_TEEN | TH_HALL | TH_DINER);
        ThOf("setting_basic", TH_DINING | TH_KITCHEN | TH_PATIO | TH_DINER); ThOf("setting_food", TH_DINER | TH_KITCHEN | TH_PATIO);
        ThOf("condiments", TH_DINER | TH_PATIO | TH_KITCHEN);
        ThOf("f_plate", TH_DINING | TH_KITCHEN | TH_PATIO | TH_LIVING); ThOf("f_apple", TH_KITCHEN | TH_DINING | TH_LIVING | TH_PANTRY);
        ThOf("f_loaf", TH_KITCHEN | TH_DINING | TH_PANTRY); ThOf("f_cake", TH_DINING | TH_KITCHEN | TH_DINER);
        ThOf("f_pizzaBox", TH_LIVING | TH_KITCHEN | TH_OFFICE); ThOf("f_sodaCan", TH_LIVING | TH_KITCHEN | TH_OFFICE | TH_PATIO | TH_DINER | TH_CAMP | TH_KIDS);
        ThOf("f_ketchup", TH_KITCHEN | TH_DINER); ThOf("f_mug", TH_LIVING | TH_KITCHEN | TH_DINING | TH_OFFICE | TH_BED | TH_DINER | TH_CAMP | TH_PATIO);
        ThOf("f_bowl", TH_KITCHEN | TH_DINING | TH_LIVING); ThOf("f_pan", TH_KITCHEN | TH_PRO_KITCHEN | TH_CAMP); ThOf("f_pot", TH_KITCHEN | TH_PRO_KITCHEN | TH_CAMP);
        ThOf("f_cuttingBoard", TH_KITCHEN | TH_PRO_KITCHEN); ThOf("f_watermelon", TH_KITCHEN | TH_DINING | TH_PATIO | TH_PANTRY);
        ThOf("f_wine", TH_DINING | TH_KITCHEN | TH_LIVING | TH_PATIO); ThOf("f_donut", TH_KITCHEN | TH_LIVING | TH_OFFICE | TH_DINER);
        ThOf("f_pie", TH_KITCHEN | TH_DINING | TH_DINER);
        ThOf("r_plate", TH_DINER | TH_PRO_KITCHEN | TH_KITCHEN); ThOf("r_bowl", TH_DINER | TH_PRO_KITCHEN | TH_KITCHEN);
        ThOf("r_burger", TH_DINER | TH_PRO_KITCHEN); ThOf("r_dinner", TH_DINER | TH_DINING | TH_PRO_KITCHEN);
        ThOf("r_ketchup", TH_DINER); ThOf("r_mustard", TH_DINER); ThOf("r_menu", TH_DINER);
        ThOf("r_dishrack", TH_KITCHEN | TH_PRO_KITCHEN); ThOf("r_pot", TH_PRO_KITCHEN | TH_KITCHEN | TH_PANTRY); ThOf("r_pan", TH_PRO_KITCHEN | TH_KITCHEN | TH_PANTRY);
        ThOf("r_papertowel", TH_PRO_KITCHEN | TH_KITCHEN | TH_PANTRY | TH_BATH); ThOf("r_jar", TH_KITCHEN | TH_PRO_KITCHEN | TH_PANTRY);
        ThOf("r_cuttingboard", TH_PRO_KITCHEN | TH_KITCHEN);
        ThOf("w_cog", TH_STORE | TH_OFFICE); ThOf("w_nugget", TH_STORE);
        ThOf("s_bottle", TH_CAMP | TH_STORE | TH_BATH | TH_KITCHEN | TH_PANTRY | TH_PATIO); ThOf("s_bottleLarge", TH_CAMP | TH_STORE | TH_BATH | TH_PANTRY);
        ThOf("n_sleepingMat", TH_CAMP); ThOf("gen_towels", TH_BATH);
        ThOf("toy_cat", TH_KIDS | TH_LIVING | TH_BED); ThOf("toy_dog", TH_KIDS | TH_LIVING | TH_BED); ThOf("toy_bunny", TH_KIDS | TH_LIVING | TH_BED);
        ThOf("toy_chick", TH_KIDS | TH_LIVING | TH_BED | TH_BATH); ThOf("toy_panda", TH_KIDS | TH_LIVING | TH_BED);
        ThOf("s_chest", TH_KIDS | TH_BED | TH_CAMP);
        // store goods on racks, pallets and workbenches
        string[] goods = { "s_bucket", "t_bucketMetal", "m_bucketWood", "m_bucketWater", "w_jerrycan", "w_jerrycanB", "w_jerrycanC", "w_textileRoll", "w_goldBars", "w_ironStack", "m_sack" };
        for (int i = 0; i < goods.Length; i++) ThOf(goods[i], TH_STORE | TH_PANTRY | TH_GARDEN | TH_CAMP | TH_OFFICE);
    }

    // ---------------------------------------------------------------- prop groups (G25)
    // Related props that stand together as one tidy unit. A room plan places a group where such a place has it
    // (MapGenPlans.cs); its members count towards the room's floor range and towards each member type's cap.
    static void BuildGroups()
    {
        //  id           layout      where        members  gap   the kinds to choose from
        Grp("buckets", GL_ROW, A_WALL, 2, 3, 60, "s_bucket", "t_bucketMetal", "m_bucketWood", "m_bucketWater"); GrpBeside(K_WORK | K_WET | K_TENT | K_STORE);
        Grp("crates", GL_ROW, A_WALL, 2, 3, 50, "r_crate", "r_crateTomatoes", "r_crateCheese", "r_crateCarrots", "r_crateLettuce", "r_cratePotatoes", "r_crateBuns", "r_crateOnions", "r_crateHam"); GrpBeside(K_STORE | K_COUNTER);
        Grp("boxes", GL_ROW, A_WALL, 2, 3, 50, "w_boxA", "w_boxB", "w_boxC", "s_box", "s_boxOpen"); GrpBeside(K_STORE | K_WORK);
        Grp("cardboard", GL_ROW, A_WALL, 2, 3, 40, "k_boxClosed", "k_boxOpen", "w_boxB");
        Grp("barrels", GL_ROW, A_WALL, 2, 4, 60, "w_barrel", "w_barrelB", "w_barrelC", "s_barrel", "s_barrelOpen");
        Grp("fuel", GL_ROW, A_WALL, 3, 5, 60, "w_fuelBarrel", "w_fuelBarrelB", "w_fuelBarrelC", "w_jerrycan", "w_jerrycanB", "w_jerrycanC");
        Grp("pots", GL_ROW, A_WALL, 2, 3, 90, "n_planter", "n_potSmall", "k_plant", "q_cactus");
        // flowers stand in drifts of one colour (report 15, C7): two shapes of the colour, 3 to 5 plants
        Grp("flowersRed", GL_CLUSTER, A_SOFT, 3, 5, 40, "n_flowerRed", "n_flowerRedB");
        Grp("flowersYellow", GL_CLUSTER, A_SOFT, 3, 5, 40, "n_flowerYellow", "n_flowerYellowB");
        Grp("flowersPurple", GL_CLUSTER, A_SOFT, 3, 5, 40, "n_flowerPurple", "n_flowerPurpleB");
        Grp("mushrooms", GL_CLUSTER, A_BESIDE, 2, 4, 40, "n_mushroomRed", "n_mushroomTan", "n_mushroomRedTall", "n_mushroomTanTall", "n_mushroomGroup", "n_mushroomTanGroup"); GrpBeside(K_TREE | K_STUMP);
        Grp("logpile", GL_ROW, A_OUT_EDGE, 2, 3, 30, "w_logA", "w_logB"); GrpBeside(K_FIRE | K_TENT);
        Grp("tools", GL_ROW, A_WALL, 2, 3, 130, "s_axe", "s_shovel", "s_hoe", "s_pickaxe", "s_hammer"); GrpBeside(K_WORK | K_TENT | K_STUMP | K_STORE);
        Grp("toys", GL_CLUSTER, A_CENTER, 2, 3, 90, "toy_cat", "toy_dog", "toy_bunny", "toy_chick", "toy_panda"); GrpBeside(K_BED | K_SEAT);
        Grp("rocks", GL_CLUSTER, A_OUT_OPEN, 2, 3, 60, "n_rockSmall", "n_rockSmallB", "n_rockSmallC");
        // one kind repeated: a row of the same stool, two loungers side by side, one crop to a row, a pile of sacks
        Grp("stoolsDiner", GL_ROW, A_WALL, 2, 3, 160, "r_stool");
        Grp("stoolsBar", GL_ROW, A_BESIDE, 2, 2, 160, "k_stool"); GrpBeside(K_COUNTER);
        Grp("loungers", GL_ROW, A_COVERED, 2, 2, 250, "k_loungeChair");
        Grp("vegCarrot", GL_ROW, A_SOFT, 3, 4, 110, "n_cropCarrot");
        Grp("vegPumpkin", GL_ROW, A_SOFT, 3, 4, 90, "n_cropPumpkin");
        Grp("vegMelon", GL_ROW, A_SOFT, 3, 4, 110, "n_cropMelon");
        Grp("sacks", GL_ROW, A_WALL, 2, 3, 40, "m_sack"); GrpBeside(K_STORE | K_WORK);
        // garden things at the water point; a bucket with a mop or a broom (the cleaning corner)
        Grp("watering", GL_ROW, A_OUT_EDGE, 2, 3, 80, "gen_wateringCan", "m_bucketWater", "s_bucket", "m_bucketWood"); GrpBeside(K_WET);
        Grp("cleaning", GL_ROW, A_WALL, 2, 3, 70, "gen_mop", "gen_broom", "s_bucket", "t_bucketMetal"); GrpBeside(K_WET | K_STORE);
        // revision 9: a garage's tyres and scrap, a farm's hay and rows of one crop, hens, a shop's baskets
        Grp("tyres", GL_ROW, A_WALL, 2, 4, 40, "c_tire", "c_wheel", "c_wheelTruck", "c_wheelRacing"); GrpBeside(K_WORK | K_STORE);
        Grp("scrap", GL_ROW, A_WALL, 2, 3, 120, "c_bumper", "c_door", "c_plate", "c_engine", "c_spoiler", "c_axle"); GrpBeside(K_WORK | K_STORE);
        Grp("hay", GL_CLUSTER, A_WALL, 2, 4, 30, "g_hay", "g_hayBundled"); GrpBeside(K_WORK | K_STORE);
        Grp("rowCorn", GL_ROW, A_SOFT, 3, 5, 120, "n_corn");
        Grp("rowWheat", GL_ROW, A_SOFT, 3, 5, 80, "n_wheat");
        Grp("rowLettuce", GL_ROW, A_SOFT, 3, 5, 120, "n_lettuce");
        Grp("rowTurnip", GL_ROW, A_SOFT, 3, 5, 110, "n_cropTurnip");
        // revision 10: rows of young crops (an earlier sowing)
        Grp("rowCornYoung", GL_ROW, A_SOFT, 3, 5, 120, "n_cornYoung");
        Grp("rowWheatYoung", GL_ROW, A_SOFT, 3, 5, 80, "n_wheatYoung");
        Grp("rowLettuceYoung", GL_ROW, A_SOFT, 3, 5, 120, "n_lettuceYoung");
        Grp("rowBamboo", GL_ROW, A_SOFT, 3, 5, 120, "n_bamboo");
        Grp("hens", GL_CLUSTER, A_OUT_OPEN, 2, 4, 220, "p_hen");
        Grp("baskets", GL_ROW, A_WALL, 2, 3, 40, "ms_basket"); GrpBeside(K_COUNTER | K_STORE);
        // revision 10
        Grp("handtools", GL_ROW, A_WALL, 2, 4, 90, "g_saw", "g_mallet", "g_axe", "g_shovel", "g_pickaxe", "g_drill", "g_tongs", "g_trowel"); GrpBeside(K_WORK | K_STORE);
        Grp("smallTools", GL_ROW, A_WALL, 2, 3, 80, "g_hammer", "g_wrench", "g_plane", "g_rope", "g_ropeB", "g_lantern"); GrpBeside(K_WORK | K_STORE);
        Grp("rabbits", GL_CLUSTER, A_OUT_OPEN, 2, 3, 200, "p_rabbit");
        Grp("ducks", GL_CLUSTER, A_OUT_OPEN, 2, 4, 260, "p_duck");
        Grp("lilies", GL_CLUSTER, A_OUT_OPEN, 3, 5, 200, "n_lily", "n_lilySmall");
        Grp("shrubs", GL_ROW, A_OUT_EDGE, 2, 3, 120, "n_bushSmall", "n_bushTriangle", "n_bush");
        // revision 11
        Grp("plush", GL_CLUSTER, A_CENTER, 2, 3, 90, "mb_plushA", "mb_plushB", "toy_cat", "toy_dog", "toy_bunny", "toy_panda"); GrpBeside(K_BED | K_SEAT);
        Grp("boardToys", GL_CLUSTER, A_CENTER, 2, 4, 120, "bg_diceA", "bg_diceB", "bg_d20", "bg_pawn", "bg_meeple", "bg_house", "bg_hourglass");
        Grp("toyCars", GL_CLUSTER, A_CENTER, 2, 3, 140, "tc_monster", "tc_racer", "tc_truck", "tc_suv");
        Grp("comics", GL_ROW, A_WALL, 2, 3, 60, "mb_comics", "mb_comicBox", "mb_comicBoxFull");
        Grp("trunks", GL_ROW, A_WALL, 2, 3, 80, "dg_trunkL", "dg_trunkM", "dg_trunkS", "dg_chest");
        Grp("oldBoxes", GL_ROW, A_WALL, 2, 3, 40, "dg_boxL", "dg_boxS", "k_boxClosed", "k_boxOpen");
        Grp("frames", GL_ROW, A_WALL, 2, 3, 60, "mb_frameA", "mb_frameB", "mb_frameC", "q_frameLA", "q_frameLB");
        Grp("balls", GL_CLUSTER, A_WALL, 2, 3, 80, "hb_basketball", "hb_football");
        Grp("kegs", GL_ROW, A_WALL, 2, 3, 60, "dg_barrel", "dg_keg");
        // revision 12
        Grp("penguins", GL_CLUSTER, A_OUT_OPEN, 3, 5, 200, "z_penguin");
        Grp("monkeys", GL_CLUSTER, A_OUT_OPEN, 2, 3, 220, "z_monkey");
        Grp("parrots", GL_CLUSTER, A_OUT_OPEN, 2, 4, 240, "z_parrot");
        Grp("bugs", GL_CLUSTER, A_OUT_OPEN, 2, 3, 260, "z_bee", "z_caterpillar");
        Grp("shore", GL_CLUSTER, A_OUT_OPEN, 2, 3, 260, "z_crab", "z_fish");
        Grp("woodpile", GL_ROW, A_WALL, 2, 3, 60, "s_wood", "s_planks", "n_logStack");
        Grp("tents", GL_ROW, A_OUT_OPEN, 2, 2, 600, "s_tentCanvas", "s_tentHalf");   // revision 30: not the bare frame (s_tent), it does not read as a tent
        Grp("queue", GL_ROW, A_OUT_OPEN, 2, 4, 0, "ck_queue");
        Grp("coasterCars", GL_ROW, A_OUT_OPEN, 2, 3, 150, "ck_trainFront", "ck_train", "ck_trainSafety");
        Grp("karts", GL_ROW, A_OUT_OPEN, 2, 3, 300, "c_kartA", "c_kartB", "c_kartC");
        Grp("presents", GL_CLUSTER, A_BESIDE, 2, 4, 60, "hb_presentA", "hb_presentB", "hb_presentC", "hb_presentD", "hb_presentE", "hb_presentS"); GrpBeside(K_STORE | K_SEAT);
        Grp("jacks", GL_ROW, A_WALL, 2, 3, 120, "gk_pumpkin", "gk_pumpkinTall", "hw_jackOrange", "hw_jackYellow");
        Grp("graves", GL_ROW, A_OUT_OPEN, 3, 4, 900, "gk_gsBevel", "gk_gsBroken", "gk_gsCross", "gk_gsCrossL", "gk_gsDeco", "gk_gsRoof", "gk_gsRound", "gk_gsWide", "hw_graveA", "hw_graveB",
            "hw_marker", "hw_stone");
        Grp("crosses", GL_ROW, A_OUT_OPEN, 2, 3, 900, "gk_cross", "gk_crossColumn", "gk_crossWood", "gk_gsCross");
        Grp("urns", GL_ROW, A_WALL, 2, 2, 400, "gk_urnRound", "gk_urnSquare");
        // the kinds of a group stand in for each other: a hider's metal bucket fits any row of buckets (bots use this)
        int bk = FindGrp("buckets");
        defKin[FindDef("gen_wateringCan")] = bk;
        int fl = FindGrp("flowersRed");
        string[] flowers = { "n_flowerYellow", "n_flowerYellowB", "n_flowerPurple", "n_flowerPurpleB" };
        for (int i = 0; i < flowers.Length; i++) defKin[FindDef(flowers[i])] = fl;
        ThOf("k_stool", TH_KITCHEN | TH_DINER | TH_STORE | TH_OFFICE); ThOf("r_stool", TH_DINER | TH_STORE | TH_PANTRY | TH_PRO_KITCHEN | TH_OFFICE);
    }

    // ---------------------------------------------------------------- prop counts per room type (G24)
    // Props per 100 m2 of the room's floor at Normal density; the density setting scales every number. A room
    // rolls a floor budget inside its range. The plan of its type then places anchors, what belongs with them,
    // a few groups and a small remainder, and stops when the budget is used: empty floor is a correct result.
    // The pieces that make the room what it is (a kitchen's run, a table and its chairs, a bed) are placed even
    // when a small room's budget is lower. Everything that stands on the floor counts, fixtures and chairs too.
    // "Cover" is the share of the room's floor that props and trees may block. Trees and grass are not props.
    static void BuildRanges()
    {
        //    room type            on the floor    on furniture    cover    trees     grass
        //                         min   max       max             max %    min max   min max
        Range(R_LIVING,            28,   40,       32,             50);
        Range(R_DINING,            40,   56,       40,             50);
        Range(R_KITCHEN,           38,   52,       30,             50);
        Range(R_BEDROOM,           32,   46,       20,             55);
        Range(R_BATH,              25,   40,       10,             50);
        Range(R_OFFICE,            34,   50,       34,             50);
        Range(R_KIDS,              36,   50,       26,             55);
        Range(R_HALL,              25,   38,       12,             40);   // generator 5: 19 to 31 (38% of halls held 3 or fewer)
        Range(R_STORAGE,           38,   54,       40,             50);
        Range(R_LOADING,           34,   50,       16,             50);
        Range(R_WH_OFFICE,         32,   46,       30,             50);
        Range(R_DINER_DINING,      44,   62,       36,             55);
        Range(R_DINER_KITCHEN,     30,   46,       24,             60);
        Range(R_DINER_STORE,       36,   54,       34,             50);
        Range(R_LAWN_OPEN,         10,   18,        2,             34,      1,  2,    0,  0);
        Range(R_GARDEN,            22,   34,        2,             40,      0,  2,    0,  0);
        Range(R_PATIO,             16,   26,        8,             36,      0,  2,    0,  0);
        Range(R_FIREPIT,           14,   22,        3,             34,      0,  2,    0,  0);
        Range(R_LANE,              12,   18,        0,             24);   // revision 29: 6 to 10, room for the verge along both sides
        Range(R_YARD,              18,   28,        6,             36);
        Range(R_FOREST_DENSE,      16,   26,        2,             44,      9,  15,   8, 16);
        Range(R_FOREST_CLEARING,   16,   24,        5,             44,      4,  7,    4,  8);
        // No outdoor area is bare (G29): the fewest props on the floor of one area, whatever its size
        rangeFloorAbs[R_LAWN_OPEN] = 5; rangeFloorAbs[R_GARDEN] = 8; rangeFloorAbs[R_PATIO] = 7; rangeFloorAbs[R_FIREPIT] = 7;
        rangeFloorAbs[R_FOREST_DENSE] = 6; rangeFloorAbs[R_FOREST_CLEARING] = 8; rangeFloorAbs[R_LANE] = 4; rangeFloorAbs[R_YARD] = 6;
        // revision 29: the purposes' sets give their floor props; the minimum is what the smallest area's sets give
        Range(R_PICNIC,            14,   30,        8,             40,      0,  1,    0,  0);
        Range(R_DRYING,            10,   24,        2,             40,      0,  1,    0,  0);
        rangeFloorAbs[R_PICNIC] = 5; rangeFloorAbs[R_DRYING] = 4;
        // revision 30: scenery ground has no floor minimum (its plan plants it; nothing fills it up)
        Range(R_SCENERY,            0,    0,        0,             30);
        rangeFloorAbs[R_SCENERY] = 0;
        // revision 9
        Range(R_SHOP_FLOOR,        42,   58,       18,             58);   // revision 10: 34 to 48
        Range(R_STOCKROOM,         44,   60,       36,             55);   // revision 10: 38 to 54
        Range(R_BACK_OFFICE,       38,   52,       34,             55);   // revision 10: 32 to 46
        Range(R_GARAGE_BAY,        22,   34,       20,             55);
        Range(R_PARTS,             38,   54,       36,             50);
        Range(R_BARN,              28,   40,       10,             52);   // revision 10: 20 to 32
        Range(R_FEED_STORE,        44,   60,       14,             55);   // revision 9: 36 to 52
        Range(R_FORECOURT,         14,   24,        4,             36);
        Range(R_FARMYARD,          26,   36,        6,             40,      0,  1,    0,  0);   // revision 9: 14 to 24
        Range(R_FIELD,             30,   50,        0,             45);
        Range(R_PADDOCK,           24,   34,        2,             36,      0,  1,    0,  0);   // revision 9: 10 to 18
        Range(R_PARK,              30,   56,        8,             48,      1,  3,    0,  0);   // revision 9: 10 to 18
        // revision 10
        Range(R_FARM_WORK,         38,   54,       30,             50);
        Range(R_PLAYGROUND,        30,   58,        2,             58);   // revision 10: 30 to 42
        Range(R_POND,              38,   50,        2,             48,      0,  1,    0,  0);
        Range(R_TERRACE,           32,   62,       34,             58);   // revision 10: 34 to 48
        // revision 11
        Range(R_TEEN,              36,   50,       30,             55);
        Range(R_PLAYROOM,          42,   58,       20,             55);
        Range(R_BOXROOM,           44,   60,       20,             55);
        Range(R_PANTRY,            40,   56,       44,             50);
        // revision 12
        Range(R_ZOO_PLAZA,         34,   62,       30,             55);
        Range(R_ZOO_WALK,          22,   56,        4,             48);
        Range(R_ENC_FARM,          44,   86,        4,             55,      0,  1,    0,  0);
        Range(R_ENC_SAVANNA,       40,   84,        2,             55,      0,  1,    0,  0);
        Range(R_ENC_ARCTIC,        40,   84,        2,             55);
        Range(R_ENC_WOODS,         44,   84,        2,             55,      1,  2,    0,  0);
        Range(R_ENC_JUNGLE,        44,   84,        2,             55,      1,  2,    0,  0);
        Range(R_AVIARY,            46,   86,        2,             55,      0,  1,    0,  0);
        Range(R_KEEPER,            52,   68,       10,             55);
        Range(R_CABIN_MAIN,        36,   50,       36,             55);
        Range(R_BUNKROOM,          38,   52,       24,             55);
        Range(R_PORCH,             30,   44,       10,             50);
        Range(R_WOODYARD,          38,   52,        8,             55);
        Range(R_CAMPSITE,          26,   38,       10,             50,      0,  2,    0,  0);
        rangeFloorAbs[R_ZOO_PLAZA] = 8; rangeFloorAbs[R_ZOO_WALK] = 6; rangeFloorAbs[R_KEEPER] = 6; rangeFloorAbs[R_PORCH] = 6; rangeFloorAbs[R_WOODYARD] = 6; rangeFloorAbs[R_CAMPSITE] = 8;
        Range(R_ARCADE,            44,   60,       10,             60);
        Range(R_SNACKBAR,          40,   56,       36,             55);
        Range(R_PRIZE_STORE,       44,   60,       30,             55);
        Range(R_MIDWAY,            30,   46,       20,             50);
        Range(R_RIDES,             28,   44,        4,             55);
        rangeFloorAbs[R_MIDWAY] = 8; rangeFloorAbs[R_RIDES] = 6;
        Range(R_NAVE,              40,   56,       20,             55);
        Range(R_VESTRY,            40,   56,       30,             55);
        Range(R_CHURCHYARD,        24,   40,        4,             40,      1,  2,    0,  0);
        Range(R_GRAVEYARD,         36,   56,        2,             50,      0,  2,    0,  0);
        rangeFloorAbs[R_CHURCHYARD] = 6; rangeFloorAbs[R_GRAVEYARD] = 8;
        rangeFloorAbs[R_ENC_FARM] = 6; rangeFloorAbs[R_ENC_SAVANNA] = 6; rangeFloorAbs[R_ENC_ARCTIC] = 6; rangeFloorAbs[R_ENC_WOODS] = 6; rangeFloorAbs[R_ENC_JUNGLE] = 6; rangeFloorAbs[R_AVIARY] = 6;
        rangeFloorAbs[R_FARM_WORK] = 6; rangeFloorAbs[R_PLAYGROUND] = 6; rangeFloorAbs[R_POND] = 6; rangeFloorAbs[R_TERRACE] = 7;
        rangeFloorAbs[R_FORECOURT] = 6; rangeFloorAbs[R_FARMYARD] = 6; rangeFloorAbs[R_FIELD] = 10; rangeFloorAbs[R_PADDOCK] = 6; rangeFloorAbs[R_PARK] = 7;
    }

    // ---------------------------------------------------------------- what is native to a room type
    // The types a room's plan may place (MapGenPlans.cs, MapGenOutdoor.cs). A type that is not listed for a room
    // type can only come there as a wrong-category spawn (D10). "@name" lists every kind of a prop group.
    static void Native(int zone, int room, params string[] ids)
    {
        Table(zone, room);
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i][0] == '@')
            {
                int g = FindGrp(ids[i].Substring(1));
                int e = nEntries++;
                entryDef[e] = grpMemberDef[grpMemberStart[g]]; entryGrp[e] = g;
                tableEntryCount[nTables - 1]++;
            }
            else
            {
                int e = nEntries++;
                int d = FindDef(ids[i]);
                entryDef[e] = d; entryGrp[e] = -1;
                tableEntryCount[nTables - 1]++;
                // a tree's seasonal twins belong wherever the tree does, and a piece's KayKit twin wherever the piece does
                for (int s = 1; s < 3; s++)
                    if (defSeason[d * 3 + s] >= 0) { e = nEntries++; entryDef[e] = defSeason[d * 3 + s]; entryGrp[e] = -1; tableEntryCount[nTables - 1]++; }
                if (defTwin[d] >= 0) { e = nEntries++; entryDef[e] = defTwin[d]; entryGrp[e] = -1; tableEntryCount[nTables - 1]++; }
                for (int th = 1; th < 3; th++)
                    if (defThemeTwin[d * 3 + th] >= 0) { e = nEntries++; entryDef[e] = defThemeTwin[d * 3 + th]; entryGrp[e] = -1; tableEntryCount[nTables - 1]++; }
            }
        }
    }

    static string[] Cat(string[] a, params string[] b)
    {
        var r = new string[a.Length + b.Length];
        for (int i = 0; i < a.Length; i++) r[i] = a[i];
        for (int i = 0; i < b.Length; i++) r[a.Length + i] = b[i];
        return r;
    }

    static void BuildTables()
    {
        Native(Z_HOUSE, R_LIVING, "k_sofa", "q_couch", "k_designSofa", "k_designChair", "k_recliner", "k_tableCoffee", "q_tableLow", "k_tvCabinet", "q_armchair", "k_loungeChair", "k_bookcase", "k_bookcaseLow",
            "q_cabinet", "k_lampFloor", "q_lampStanding", "k_plant", "q_cactus", "k_sideTable", "q_tableSmall", "k_speaker", "k_rug", "q_rug", "k_rugLong", "@cardboard", "@toys");
        Native(Z_HOUSE, R_DINING, "k_table", "q_table", "q_cabinet", "k_bookcaseLow", "k_sideTable", "k_plant", "q_cactus", "q_lampStanding", "k_lampFloor", "k_rug", "q_rug");
        Native(Z_HOUSE, R_KITCHEN, "k_stove", "k_sink", "k_fridge", "k_counter", "k_counterDrawer", "k_trashcan", "k_stool", "k_bar", "r_tableRound", "k_table", "k_plant",
            "k_boxOpen", "@crates", "q_tableSmall");
        Native(Z_HOUSE, R_BEDROOM, "k_bedDouble", "k_bedSingle", "q_bed", "gen_wardrobe", "k_bedside", "q_cabinet", "q_cabinetSmall", "k_desk", "k_lampFloor", "q_lampStanding",
            "k_plant", "k_rug", "q_rug", "k_bookcaseLow", "q_armchair", "k_bench", "@cardboard");
        Native(Z_HOUSE, R_BATH, "k_toilet", "k_bathSink", "k_bathtub", "k_washer", "k_dryer", "k_trashcan", "k_plant", "q_cabinetSmall", "@buckets", "@cleaning",
            "gen_laundryBasket", "k_doormat");
        Native(Z_HOUSE, R_OFFICE, "k_desk", "k_bookcase", "q_cabinet", "k_bookcaseLow", "k_plant", "k_lampFloor", "k_sideTable", "q_armchair", "k_trashcan", "@cardboard", "q_tableSmall");
        Native(Z_HOUSE, R_KIDS, "k_bedSingle", "q_bed", "k_bookcaseLow", "k_desk", "gen_wardrobe", "k_rug", "k_bedside", "k_plant", "@toys", "k_boxOpen", "s_chest");
        Native(Z_HOUSE, R_HALL, "k_bench", "k_benchCushion", "k_coatRack", "gen_wardrobe", "k_plant", "q_cabinetSmall", "k_sideTable", "k_lampFloor", "@cardboard", "k_doormat", "gen_shoes");
        // revision 11
        Native(Z_HOUSE, R_TEEN, "k_bedSingle", "q_bed", "k_bedside", "k_desk", "gen_wardrobe", "k_bookcaseLow", "k_rug", "q_rug", "k_lampFloor", "k_speaker", "k_tvCabinet",
            "mb_guitarA", "mb_guitarB", "mb_guitarCase", "@comics", "mb_skates", "mb_helmet", "sk_board", "@balls", "hb_armchairBlue", "k_loungeChair", "@plush", "k_plant", "k_trashcan");
        Native(Z_HOUSE, R_PLAYROOM, "hb_carpet", "k_rug", "@boardToys", "@toyCars", "hb_train", "hb_tender", "@plush", "@toys", "hb_footstoolG", "hb_footstoolR", "hb_stool",
            "@balls", "k_bookcaseLow", "s_chest", "k_boxOpen", "hb_armchairBlue", "q_table", "k_plant", "mb_skates", "k_lampFloor");
        Native(Z_HOUSE, R_BOXROOM, "@trunks", "@oldBoxes", "@frames", "mb_mirror", "mb_sled", "mb_guitarCase", "mb_mimic", "mb_skates", "mb_helmet", "gen_wardrobe", "q_cabinet",
            "k_lampFloor", "q_lampStanding", "s_chest", "@cardboard", "@comics", "q_armchair", "k_bookcaseLow", "hb_stool", "@balls");
        // revision 12: the zoo
        Native(Z_LAWN, R_ZOO_PLAZA, "ck_info", "ck_food", "ck_drinks", "ck_toilets", "f_stallRed", "f_stallGreen", "f_stallBench", "f_stallStool", "@plush", "r_tableRound", "r_chair",
            "k_chair", "ck_bench", "k_bench", "ck_trash", "ck_flowers", "@pots", "f_lamp", "ma_vending", "ma_claw", "ms_gacha", "f_hedge", "f_fountainSq", "ms_cart", "@crates", "k_plant", "hk_bench", "hk_lantern", "sk_scooter", "n_planter", "f_stallBench", "f_stallStool", "ma_prizeWheel", "ma_tickets", "n_statueRing",
            "f_hedgeCurve", "k_trashcan", "hk_benchShort");
        Native(Z_LAWN, R_ZOO_WALK, "ck_bench", "k_bench", "ck_trash", "f_lamp", "f_cart", "m_wheelbarrow", "ck_food", "ck_drinks", "ck_flowers", "@shrubs", "f_hedge", "@pots",
            "@buckets", "@hay", "ms_cart", "n_planter", "ma_vending", "ck_info", "ck_toilets", "f_hedgeCurve", "n_bushDetailed", "k_benchCushion", "n_statueBlock",
            "n_bush", "n_bushSmall", "hk_bench", "hk_lantern", "f_cartHigh", "hk_benchShort");
        string[] habitat = { "@rocks", "n_rockLarge", "n_rockTall", "n_bush", "n_bushLarge", "n_bushDetailed", "@shrubs", "n_logLarge", "n_stump", "s_logSmall", "gen_trough",
            "@buckets", "@hay", "n_treeFat", "n_treeOak", "n_grassLarge", "s_stoneL", "s_rockFlat", "n_bushSmall", "s_block", "n_bushTriangle", "n_stumpRound", "n_rockLargeB", "m_bucketWater", "g_hayBundled", "n_logStack", "n_log" };
        Native(Z_LAWN, R_ENC_FARM, "p_cow", "p_pig", "p_hog", "@hens", "@rabbits", "p_dog", "p_cat", "gen_coop", "gen_trough", "@hay", "@buckets", "f_cart", "m_wheelbarrow",
            "n_bush", "n_stump", "@sacks", "k_bench", "w_pallet", "m_sack");
        Native(Z_LAWN, R_ENC_SAVANNA, Cat(habitat, "z_giraffe", "z_elephant", "z_lion", "gen_shed"));
        Native(Z_LAWN, R_ENC_ARCTIC, Cat(habitat, "z_polar", "@penguins", "@shore", "n_canoe"));
        Native(Z_LAWN, R_ENC_WOODS, Cat(habitat, "z_deer", "z_fox", "p_beaver", "@rabbits", "@mushrooms", "n_logStack"));
        Native(Z_LAWN, R_ENC_JUNGLE, Cat(habitat, "z_tiger", "z_panda", "@monkeys", "z_koala", "n_bamboo", "gen_shed"));
        Native(Z_LAWN, R_AVIARY, Cat(habitat, "@parrots", "@bugs", "@flowersRed", "@flowersYellow", "@flowersPurple", "ck_flowers", "n_planter"));
        Native(Z_LAWN, R_KEEPER, "@hay", "@buckets", "@sacks", "m_wheelbarrow", "f_cart", "f_cartHigh", "gen_trough", "gen_shed", "@crates", "w_pallet", "w_palletLoaded", "@tools",
            "gen_tap", "@barrels", "gen_coop", "ms_handTruck", "@cleaning", "s_boxLarge");
        // the log cabin
        Native(Z_CABIN, R_CABIN_MAIN, "cb_woodstove", "n_logStack", "k_table", "q_table", "k_bench", "hk_benchShort", "k_recliner", "k_loungeChair", "k_rug", "q_rug",
            "k_bookcaseClosed", "k_bookcaseWide", "k_counter", "k_sink", "k_stoveElectric", "k_fridgeSmall", "k_coatRack", "hk_sled", "n_paddle", "k_lampSquare",
            "s_chest", "s_barrel", "@woodpile", "k_trashcan", "@buckets", "k_plant", "k_doormat", "q_cabinet");
        Native(Z_CABIN, R_BUNKROOM, "k_bedBunk", "k_bedSingle", "n_cot", "k_bedsideDrawer", "s_chest", "dg_trunkL", "dg_trunkM", "s_bedrollPacked", "k_coatRack", "gen_wardrobe",
            "k_lampSquare", "k_rug", "hk_sledLong", "n_paddle", "@cardboard", "k_bookcaseClosed", "hk_benchShort");
        Native(Z_LAWN, R_PORCH, "hk_bench", "hk_benchShort", "k_recliner", "hk_lantern", "n_logStack", "hk_sled", "hk_sledLong", "n_canoe", "n_paddle", "s_barrel", "@buckets",
            "@pots", "k_doormat", "s_bedrollPacked", "@woodpile", "gen_tap");
        Native(Z_LAWN, R_WOODYARD, "s_block", "s_axe", "s_axeB", "s_shovelB", "@tools", "@woodpile", "n_logStackLarge", "s_planks", "s_wood", "m_wheelbarrow", "s_anvil", "s_grind",
            "gen_shed", "s_workbench", "@buckets", "s_barrel", "w_logs", "s_logSmall");
        Native(Z_LAWN, R_CAMPSITE, "n_campfire", "s_campfirePit", "@tents", "n_tentBig", "n_tentSmallOpen", "n_tentOpen", "s_bedroll", "s_bedrollFrame", "n_cot", "s_spit",
            "s_fishStand", "s_chest", "n_canoe", "n_paddle", "@logpile", "s_logSmall", "s_treeLog", "@buckets", "hk_lantern", "s_barrel", "s_rockFlat", "@rocks");

        // the funfair
        Native(Z_FAIR, R_ARCADE, "ma_airHockey", "ma_basketball", "ma_dance", "ma_slots", "ma_pinball", "ma_prizeWheel", "ma_prizes", "ma_tickets", "ma_register", "ma_claw",
            "ma_vending", "ma_arcade", "ms_gacha", "@plush", "@balls", "hb_stool", "k_trashcan", "ck_bench", "k_plant", "r_stool", "@boardToys");
        Native(Z_FAIR, R_SNACKBAR, "r_counter", "r_counterSink", "r_stove", "r_fridge", "ms_freezer", "ma_vending", "r_tableRound", "r_stool", "@crates", "k_trashcan", "@cardboard",
            "ms_dispCans", "ms_freezers");
        Native(Z_FAIR, R_PRIZE_STORE, "gen_rack", "@plush", "@balls", "@boxes", "@cardboard", "ma_prizes", "@toyCars", "@boardToys", "s_boxLarge", "ms_handTruck", "@crates", "dg_boxL");
        Native(Z_LAWN, R_MIDWAY, "f_stallRed", "f_stallGreen", "f_stallBench", "f_stallStool", "ck_food", "ck_drinks", "ck_info", "ck_toilets", "ck_bench", "ck_trash", "ck_flowers",
            "f_lamp", "hk_lantern", "ma_claw", "ma_vending", "ma_prizeWheel", "ma_basketball", "@plush", "@balls", "mb_circus", "r_tableRound", "k_chair", "@pots");
        Native(Z_LAWN, R_RIDES, "ck_rideIn", "ck_rideOut", "ck_stationFence", "ck_stationGate", "@queue", "@coasterCars", "ck_trainWood", "ck_flume", "ck_monorail", "ck_trainHanging",
            "mb_circus", "ck_bench", "ck_trash", "f_lamp", "hk_lantern", "ck_food", "ck_drinks", "c_cone", "@shrubs", "k_bench", "@karts");

        // the chapel
        Native(Z_CHAPEL, R_NAVE, "gk_pew", "gk_altarStone", "gk_altarWood", "hw_shrine", "hw_plaque", "gk_lanternGlass", "gk_lantern", "gk_pillarSmall", "gk_pillarSquare", "@urns",
            "k_bookcaseClosed", "k_plant", "k_rugLong", "q_rug", "gk_benchOld", "k_coatRack", "k_doormat");
        Native(Z_CHAPEL, R_VESTRY, "gen_wardrobe", "k_desk", "q_cabinet", "gk_trunk", "k_bookcaseClosed", "k_bookcase", "k_coatRack", "k_lampSquare", "gk_lantern", "hw_shrine",
            "@cardboard", "k_table", "q_cabinetSmall", "k_trashcan", "gk_urnRound");
        Native(Z_LAWN, R_CHURCHYARD, "gk_benchOld", "k_bench", "hk_bench", "gk_lightpost", "gk_lightpostDouble", "@urns", "@graves", "gk_cross", "n_treeOak", "n_treeFat", "n_bushLarge",
            "@flowersRed", "@flowersYellow", "@flowersPurple", "f_hedge", "n_pathStone", "n_sign", "ck_trash");
        Native(Z_LAWN, R_GRAVEYARD, "@graves", "@crosses", "gk_gsDebris", "gk_crypt", "gk_cryptSmall", "gk_mound", "gk_graveBorder", "gk_dug", "gk_shovel", "gk_lightpost",
            "gk_lightpostDouble", "gk_fireBasket", "gk_benchOld", "@urns", "gk_lantern", "n_treeOak", "n_bushLarge", "n_bush", "@rocks", "m_wheelbarrow", "hk_bench");

        Native(Z_HOUSE, R_PANTRY, "dg_shelves", "gen_rackPantry", "@crates", "@sacks", "@kegs", "ms_freezer", "k_fridge", "k_counter", "@cardboard", "k_trashcan", "@cleaning", "hb_stool");

        Native(Z_WAREHOUSE, R_STORAGE, "gen_toolwall", "gen_rack", "w_pallet", "w_palletLoaded", "@crates", "@barrels", "@fuel", "@boxes", "w_planks", "w_logs", "w_textiles", "w_bricks", "w_ironStack",
            "w_parts", "s_boxLarge", "s_boxLargeOpen", "s_workbench", "@buckets", "@tools", "@cleaning", "ms_handTruck", "@sacks", "q_stool", "r_stool");
        Native(Z_WAREHOUSE, R_LOADING, "w_pallet", "w_palletLoaded", "@crates", "@barrels", "@fuel", "@boxes", "s_boxLarge", "w_planks", "k_trashcan", "ms_handTruck", "@sacks", "@cleaning");
        Native(Z_WAREHOUSE, R_WH_OFFICE, "k_desk", "q_cabinet", "k_bookcase", "k_bookcaseLow", "k_trashcan", "k_plant", "s_workbench", "q_tableSmall", "k_lampFloor", "@cardboard", "r_tableRound");

        Native(Z_DINER, R_DINER_DINING, "r_table", "r_tableRound", "k_table", "k_plant", "q_cactus", "q_cabinet", "q_lampStanding", "k_trashcan", "k_bench", "k_benchCushion", "r_stool",
            "k_stool", "k_bar", "k_coatRack", "k_doormat", "q_couch", "q_tableLow", "n_planter", "@pots");
        Native(Z_DINER, R_DINER_KITCHEN, "r_counter", "r_counterSink", "r_stove", "r_fridge", "k_fridge", "k_counter", "k_counterDrawer", "@crates", "k_trashcan", "@cleaning");
        Native(Z_DINER, R_DINER_STORE, "r_fridge", "@crates", "@barrels", "gen_rackPantry", "w_pallet", "@boxes", "@sacks");

        Native(Z_LAWN, R_LAWN_OPEN, "gen_mailbox", "n_treeFat", "n_treeOak", "n_bush", "n_bushDetailed", "n_bushLarge", "@rocks", "@flowersRed", "@flowersYellow", "@flowersPurple", "n_sign", "k_bench",
            "n_logStack", "@pots", "k_trashcan", "n_pathStone", "w_pallet", "@barrels", "r_tableRound", "n_rockLargeB");
        Native(Z_LAWN, R_GARDEN, "n_treeFat", "@flowersRed", "@flowersYellow", "@flowersPurple", "n_bush", "n_bushLarge", "n_bushDetailed", "@rocks", "@pots", "n_sign", "@buckets", "@tools",
            "@watering", "gen_tap", "gen_shed", "m_wheelbarrow", "@sacks", "@vegCarrot", "@vegPumpkin", "@vegMelon", "k_bench", "n_pathStone");
        Native(Z_LAWN, R_PICNIC, "k_table", "k_bench", "k_trashcan", "gen_tap", "n_treeFat", "n_bush", "n_bushLarge", "@rocks", "@flowersYellow", "n_pathStone", "s_barrel", "r_tableRound");
        Native(Z_LAWN, R_SCENERY, "n_treeFat", "n_treeOak", "n_treeDefault", "n_treePineRound", "n_bush", "n_bushDetailed", "n_bushLarge", "n_rockLarge", "n_rockTall", "@rocks");
        Native(Z_LAWN, R_DRYING, "ms_clothesRack", "gen_laundryBasket", "k_bench", "k_trashcan", "@buckets", "n_bush", "@rocks", "@flowersRed", "n_pathStone", "gen_tap");
        Native(Z_LAWN, R_PATIO, "n_treeFat", "k_table", "r_tableRound", "@pots", "k_loungeChair", "k_bench", "k_benchCushion", "n_bushLarge", "n_bush", "s_barrel", "n_pathStone", "@flowersYellow");
        Native(Z_LAWN, R_FIREPIT, "n_treeFat", "n_campfire", "s_campfirePit", "n_logStack", "n_logStackLarge", "@logpile", "n_bush", "n_bushLarge", "@rocks", "n_stump", "n_stumpRound", "n_log",
            "@buckets", "n_pathStone", "s_axe");

        // the lane between the plots (G32): a sign at its start, benches and bins against the fences, planting
        Native(Z_LAWN, R_LANE, "n_sign", "k_bench", "k_benchCushion", "k_trashcan", "@pots", "n_bush", "n_bushDetailed", "@rocks", "@flowersRed", "@flowersYellow",
            "@flowersPurple", "n_pathStone");
        // the yard of a warehouse (loading yard) or of a diner (service yard)
        Native(Z_LAWN, R_YARD, "gen_dumpster", "w_pallet", "w_palletLoaded", "@crates", "@barrels", "@fuel", "@boxes", "s_boxLarge", "w_planks", "ms_handTruck", "k_trashcan",
            "@sacks", "@cleaning", "n_pathStone");

        Native(Z_FOREST, R_FOREST_DENSE, "n_treeDefault", "n_treeOak", "n_treePineTall", "n_treePineRound", "n_bush", "n_bushDetailed", "n_bushLarge", "n_rockLarge", "@rocks", "n_rockTall",
            "n_stump", "n_stumpRound", "n_log", "n_logLarge", "s_treeLog", "@mushrooms", "@flowersRed", "@flowersYellow", "@flowersPurple", "n_grass", "n_grassLarge", "s_signpost", "s_axe");
        Native(Z_FOREST, R_FOREST_CLEARING, "n_tentOpen", "n_tentClosed", "n_campfire", "s_campfirePit", "n_treeDefault", "n_treePineTall", "n_treeOak", "n_stump", "n_stumpRound", "n_log",
            "n_logStack", "@logpile", "s_bedroll", "s_chest", "s_barrel", "s_box", "n_bush", "n_bushLarge", "@rocks", "@mushrooms", "@buckets", "@tools", "n_grass", "n_grassLarge", "s_axe");

        // revision 9: the shop, the garage, the farm, the park
        Native(Z_SHOP, R_SHOP_FLOOR, "ms_shelfBoxes", "ms_shelfBags", "ms_shelfEnd", "ms_freezers", "ms_freezer", "ms_dispBread", "ms_dispCans", "ms_dispFruit", "ms_dispSmall",
            "ms_checkout", "ms_clothesRack", "ms_clothesTable", "ms_cart", "@baskets", "ms_bottleReturn", "ms_gacha", "ms_mannequin", "k_plant", "k_trashcan", "k_doormat", "@crates",
            "ms_dispFlowers", "@pots", "n_planter", "@watering", "@sacks", "ms_handTruck", "m_wheelbarrow",
            "ma_claw", "ma_vending", "ma_arcade", "@plush", "@balls", "@toyCars", "@boardToys", "@toys", "hb_train", "hb_tender", "hb_stool");
        Native(Z_SHOP, R_STOCKROOM, "gen_rack", "gen_rackPantry", "w_pallet", "w_palletLoaded", "@crates", "@boxes", "@cardboard", "ms_handTruck", "s_boxLarge", "@sacks",
            "dg_shelves", "@oldBoxes", "@kegs", "hb_stool", "ma_vending", "ms_freezers", "dg_trunkL",
            "@cleaning", "ms_dispCans", "ms_cart", "ms_freezer", "k_trashcan");
        string[] office = { "k_desk", "q_cabinet", "k_bookcase", "k_bookcaseLow", "k_trashcan", "k_plant", "s_workbench", "q_tableSmall", "k_lampFloor", "@cardboard", "r_tableRound" };
        Native(Z_SHOP, R_BACK_OFFICE, office);
        Native(Z_GARAGE, R_BACK_OFFICE, office);
        Native(Z_GARAGE, R_GARAGE_BAY, "c_sedan", "c_suv", "c_van", "c_truck", "c_hatch", "c_taxi", "c_sports", "c_luxury", "c_police", "c_ambulance", "c_delivery", "gen_toolwall", "@tyres", "@scrap", "s_workbench", "gen_rack", "@tools", "@fuel", "@barrels",
            "ms_handTruck", "c_cone", "@cleaning", "@buckets", "k_trashcan");
        Native(Z_GARAGE, R_PARTS, "gen_toolwall", "gen_rack", "@tyres", "@scrap", "@boxes", "@fuel", "@barrels", "s_workbench", "@tools", "w_parts", "s_boxLarge", "ms_handTruck", "@cleaning");
        Native(Z_BARN, R_BARN, "@hay", "p_cow", "p_pig", "@hens", "f_cart", "f_cartHigh", "c_tractor", "m_wheelbarrow", "@buckets", "@tools", "@sacks", "w_pallet", "@barrels",
            "w_planks", "w_logs", "n_logStack", "p_dog", "p_cat", "s_workbench", "gen_rack", "@crates", "@fuel",
            "p_hog", "gen_trough", "@handtools", "g_lantern", "c_tractorWheel");
        Native(Z_BARN, R_FEED_STORE, "@sacks", "@hay", "@barrels", "@buckets", "@crates", "@tools", "w_pallet", "gen_rack", "m_wheelbarrow", "p_cat", "@fuel",
            "@smallTools", "@handtools", "@boxes", "gen_trough", "dg_barrel");
        Native(Z_LAWN, R_FORECOURT, "c_sedan", "c_suv", "c_van", "c_truck", "c_hatch", "c_taxi", "c_sports", "c_luxury", "c_police", "c_flatbed",
            "c_ambulance", "c_firetruck", "c_binLorry", "c_delivery", "c_deliveryFlat", "c_cone", "@tyres", "k_bench", "k_trashcan", "ms_dispFlowers", "ms_dispFruit",
            "ms_cart", "ms_bottleReturn", "@pots", "n_pathStone", "n_sign", "@fuel", "gen_dumpster", "ma_vending", "ma_claw", "c_bumper", "c_wheel");
        Native(Z_LAWN, R_FARMYARD, "f_cart", "f_cartHigh", "c_tractor", "@hay", "m_wheelbarrow", "@buckets", "@sacks", "@tools", "@hens", "n_logStack", "@barrels", "gen_tap",
            "n_sign", "n_treeFat", "n_treeOak", "n_pathStone", "@crates", "k_bench", "p_dog", "@logpile", "w_pallet", "@watering",
            "gen_coop", "gen_trough", "c_digger", "c_tractorWheel", "f_stallGreen", "f_stallRed", "f_stallBench", "f_stallStool", "@rabbits", "p_hog");
        Native(Z_LAWN, R_FIELD, "@rowCorn", "@rowWheat", "@rowLettuce", "@rowTurnip", "@rowCornYoung", "@rowWheatYoung", "@rowLettuceYoung", "@rowBamboo", "@vegCarrot", "@vegPumpkin", "@vegMelon", "n_cropRow", "m_wheelbarrow", "@tools",
            "@buckets", "@hay", "gen_tap", "s_barrel", "@watering");
        Native(Z_LAWN, R_PADDOCK, "p_cow", "p_pig", "@hens", "@hay", "@buckets", "n_treeFat", "n_treeOak", "n_bush", "@rocks", "n_bushLarge", "p_dog", "n_logStack",
            "p_hog", "@rabbits", "gen_coop", "gen_trough", "n_stump", "f_cart", "m_wheelbarrow", "@sacks");
        Native(Z_BARN, R_FARM_WORK, "gen_toolwall", "s_workbench", "g_anvil", "g_grindstone", "@handtools", "@smallTools", "@tools", "@tyres", "c_tractorWheel", "f_wheel", "@fuel",
            "@barrels", "gen_rack", "@boxes", "w_planks", "w_logs", "ms_handTruck", "@cleaning", "@buckets", "p_cat", "dg_barrel");
        Native(Z_LAWN, R_PLAYGROUND, "sk_halfPipe", "sk_halfPipeLow", "sk_ramp", "sk_rampRail", "sk_rail", "sk_box", "sk_steps", "sk_benches", "sk_scooter", "sk_board",
            "sk_grind", "sk_railLow", "sk_platform", "sk_pallet", "hk_benchShort", "hk_lantern", "n_planter",
            "ck_bench", "ck_trash", "f_lamp", "n_bush", "@shrubs", "k_bench", "c_cone", "ck_drinks", "@pots", "@flowersRed", "@flowersYellow", "@flowersPurple");
        Native(Z_LAWN, R_POND, "@lilies", "@ducks", "n_footbridge", "n_canoe", "@rocks", "n_rockLarge", "n_rockTall", "@shrubs", "n_bushLarge", "n_grassLarge",
            "n_treeFat", "n_treeOak", "k_bench", "ck_bench", "f_lamp", "n_logLarge", "n_stump", "p_beaver", "n_log", "hk_bench", "n_paddle", "s_stoneL", "s_rockFlat", "@mushrooms", "@flowersRed", "@flowersYellow", "@flowersPurple", "ck_trash", "n_bushDetailed");
        Native(Z_LAWN, R_TERRACE, "ck_food", "ck_drinks", "ck_info", "ck_toilets", "f_stallGreen", "f_stallRed", "f_stallBench", "f_stallStool", "r_tableRound", "r_chair",
            "k_chair", "ck_bench", "ck_trash", "ck_flowers", "@pots", "f_lamp", "k_plant", "ms_dispFruit", "ma_vending", "ma_claw", "@crates", "hk_bench", "hk_lantern", "n_planter");
        Native(Z_LAWN, R_PARK, "f_fountain", "f_lamp", "k_bench", "k_benchCushion", "k_trashcan", "n_statueHead", "n_obelisk", "n_column", "n_statueRing", "n_planter",
            "r_chair", "k_chair", "n_treeFat", "n_treeOak", "n_bush", "n_bushLarge", "n_bushDetailed", "@flowersRed",
            "@flowersYellow", "@flowersPurple", "@pots", "n_pathStone", "r_tableRound", "@rocks",
            "@shrubs", "ck_bench", "ck_trash", "ck_flowers", "f_hedge", "ck_info", "r_table", "k_chair", "f_fountainSq", "f_hedgeCurve", "n_statueBlock", "n_columnBroken",
            "p_dog", "ma_vending", "hk_bench", "hk_lantern");

        Table(Z_RELEASE, R_RELEASE);
    }
}
