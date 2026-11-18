// Hand-written part of MapGenUdon: the lookups of GenData.cs the generator calls while it runs. The tables they
// read are baked instance fields (MapGenUdon.Tables.cs).
public partial class MapGenUdon
{
    // the baked tables' sizes (not referenced by the generator itself, so not generated)
    [UnityEngine.HideInInspector] public int nGrps;
    [UnityEngine.HideInInspector] public int[] ZoneWallMm;
    [UnityEngine.HideInInspector] public int[] tableRoom;

    // for the bench and the progress display
    public int CurrentStage() { return stage; }
    public int CurrentCursor() { return cursor; }
    public int CurrentPhase() { return popPhase * 100 + planPhase; }   // profiling: the room's furnishing step

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

}
