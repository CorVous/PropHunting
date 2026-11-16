// Hand-written part of MapGenUdon: the lookups of GenData.cs the generator calls while it runs. The tables they
// read are baked instance fields (MapGenUdon.Tables.cs).
public partial class MapGenUdon
{
    // the baked tables' sizes (not referenced by the generator itself, so not generated)
    [UnityEngine.HideInInspector] public int nGrps;
    [UnityEngine.HideInInspector] public int[] tableRoom;
    [UnityEngine.HideInInspector] public string[] defId;

    bool IsBuilding(int zone) { return zone == GenData.Z_HOUSE || zone == GenData.Z_WAREHOUSE || zone == GenData.Z_DINER; }

    string RoomName(int r) { return "room type " + r; }

    int FindDef(string id)
    {
        for (int i = 0; i < nDefs; i++)
            if (defId[i] == id) return i;
        UnityEngine.Debug.LogError("[MapGen] unknown def " + id);
        return 0;
    }

    int FindGrp(string id)
    {
        for (int i = 0; i < nGrps; i++)
            if (grpId[i] == id) return i;
        UnityEngine.Debug.LogError("[MapGen] unknown prop group " + id);
        return 0;
    }

    int FindTable(int zone, int room)
    {
        for (int t = 0; t < nTables; t++)
            if (tableZone[t] == zone && tableRoom[t] == room) return t;
        for (int t = 0; t < nTables; t++)
            if (tableZone[t] == zone) return t;   // per-zone fallback
        return -1;
    }

    int RoomTheme(int roomType)
    {
        switch (roomType)
        {
            case GenData.R_LIVING: return GenData.TH_LIVING;
            case GenData.R_KITCHEN: return GenData.TH_KITCHEN;
            case GenData.R_DINING: return GenData.TH_DINING;
            case GenData.R_BEDROOM: return GenData.TH_BED;
            case GenData.R_BATH: return GenData.TH_BATH;
            case GenData.R_OFFICE: case GenData.R_WH_OFFICE: return GenData.TH_OFFICE;
            case GenData.R_KIDS: return GenData.TH_KIDS;
            case GenData.R_HALL: return GenData.TH_HALL;
            case GenData.R_STORAGE: case GenData.R_LOADING: case GenData.R_YARD: return GenData.TH_STORE;
            case GenData.R_DINER_DINING: return GenData.TH_DINER;
            case GenData.R_DINER_KITCHEN: return GenData.TH_PRO_KITCHEN;
            case GenData.R_DINER_STORE: return GenData.TH_PANTRY;
            case GenData.R_PATIO: return GenData.TH_PATIO;
            case GenData.R_LAWN_OPEN: case GenData.R_GARDEN: case GenData.R_LANE: return GenData.TH_GARDEN;
            case GenData.R_FIREPIT: case GenData.R_FOREST_CLEARING: return GenData.TH_CAMP;
            case GenData.R_FOREST_DENSE: return GenData.TH_FOREST;
        }
        return 0;
    }
}
