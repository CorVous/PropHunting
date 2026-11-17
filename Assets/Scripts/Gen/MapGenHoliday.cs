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
}
