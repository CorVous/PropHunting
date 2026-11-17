// MapGen, part 2: edges between outer cells (the edge arrays; MapGenSite.cs decides them), rooms, and the inner-grid tags.
public partial class MapGen
{
    // =====================================================================================================
    // Edges: wall, door, window, fence or nothing on every side of every outer cell
    // =====================================================================================================
    public int EdgeIndex(int x, int z, int dir, out bool horizontal)
    {
        if (dir == 0) { horizontal = true; return (z + 1) * W + x; }
        if (dir == 2) { horizontal = true; return z * W + x; }
        if (dir == 1) { horizontal = false; return z * (W + 1) + x + 1; }
        horizontal = false; return z * (W + 1) + x;
    }

    // EdgeIndex written out: this runs thousands of times per room, and in Udon every call costs
    public int EdgeType(int x, int z, int dir)
    {
        if (dir == 0) return edgeH[(z + 1) * W + x];
        if (dir == 2) return edgeH[z * W + x];
        if (dir == 1) return edgeV[z * (W + 1) + x + 1];
        return edgeV[z * (W + 1) + x];
    }

    public int EdgeOff(int x, int z, int dir)
    {
        bool h; int i = EdgeIndex(x, z, dir, out h);
        return h ? edgeHOff[i] : edgeVOff[i];
    }

    void SetEdge(int x, int z, int dir, int type, int off)
    {
        bool h; int i = EdgeIndex(x, z, dir, out h);
        if (h) { edgeH[i] = type; edgeHOff[i] = off; } else { edgeV[i] = type; edgeVOff[i] = off; }
    }

    int DoorOff(ref GenRng rng) { return 1 + 2 * rng.Range(3); }   // door starts 0.5, 1.5 or 2.5 m along the edge

    // =====================================================================================================
    // Rooms and their types
    // =====================================================================================================
    void BuildRooms()
    {
        nRooms = 0;
        for (int i = 0; i < statZoneCells.Length; i++) { statZoneCells[i] = 0; statZoneRooms[i] = 0; }
        int[] map = new int[26];
        for (int inst = 0; inst < nInst; inst++)
        {
            int zone = instZone[inst];
            for (int i = 0; i < 26; i++) map[i] = -1;
            int first = nRooms;
            for (int c = 0; c < W * H; c++)
            {
                if (cellInst[c] != inst) continue;
                int l = cellRoom[c];
                if (map[l] < 0)
                {
                    map[l] = nRooms;
                    roomZone[nRooms] = zone; roomInst[nRooms] = inst; roomType[nRooms] = 0; roomOuterCells[nRooms] = 0;
                    roomLeaks[nRooms] = 0; roomFloor[nRooms] = 0; roomTop[nRooms] = 0; roomTrees[nRooms] = 0;
                    roomFloorTarget[nRooms] = 0; roomFloorMax[nRooms] = 0; roomTopCap[nRooms] = 0; roomTopFactor[nRooms] = 1000;
                    nRooms++;
                }
                roomOuterCells[map[l]]++;
                statZoneCells[zone]++;
            }
            for (int c = 0; c < W * H; c++)
                if (cellInst[c] == inst) { roomPlot[map[cellRoom[c]]] = cellPlot[c]; cellRoom[c] = map[cellRoom[c]]; }
            // the types and places the site gave the room letters (MapGenSite.cs)
            for (int l = 0; l < 26; l++)
                if (map[l] >= 0) { roomType[map[l]] = siteType[inst * 26 + l]; roomPlace[map[l]] = sitePlace[inst * 26 + l]; }
            statZoneRooms[zone] += nRooms - first;
        }
    }

    // =====================================================================================================
    // Entrances (G32): which of a room's doors people come in by, from the roles of the rooms it joins
    // =====================================================================================================
    public int[] roomEntry = new int[400];   // index into the room's door list, -1 = none

    // how public a room is: the side of a door that people come from
    static int PublicRank(int rt)
    {
        switch (rt)
        {
            case GenData.R_HALL: return 1;
            case GenData.R_LIVING: case GenData.R_DINER_DINING: case GenData.R_STORAGE: return 2;
            case GenData.R_DINING: return 3;
            case GenData.R_KITCHEN: case GenData.R_DINER_KITCHEN: return 4;
            default: return 5;
        }
    }

    // A building's entrance room (the hall, else the living room; the dining room of a diner; a warehouse's
    // office) is entered by its front door; every other room by the door from its most public neighbour (a
    // bedroom from the hall, a kitchen from the dining room, a store room from the kitchen). An outdoor area is
    // entered from the building door it has, if any (a patio from the back door).
    void RoomEntries()
    {
        for (int r = 0; r < nRooms; r++)
        {
            roomEntry[r] = -1;
            int best = 99;
            bool building = GenData.IsBuilding(roomZone[r]);
            for (int k = 0; k < roomDoorCount[r]; k++)
            {
                int i = roomDoorStart[r] + k, cell = roomDoors[i], d = roomDoorDir[i];
                int ix = cell % IW + DX[d], iz = cell / IW + DZ[d];
                if (ix < 0 || iz < 0 || ix >= IW || iz >= IH) continue;
                int other = innerRoom[ix + iz * IW];
                int rank;
                if (!building) rank = 0;                              // outdoors: the building's door
                else if (roomDoorExt[i]) rank = IsFrontDoor(cell, d) ? 0 : 50;   // the front door, else a back door
                else rank = other < 0 ? 60 : PublicRank(roomType[other]);
                if (rank < best) { best = rank; roomEntry[r] = k; }
            }
        }
    }

    // is the doorway at inner cell `cell`, side d, the front door of its building (MapGenSite)?
    bool IsFrontDoor(int cell, int d)
    {
        int c = (cell / IW >> 3) * W + (cell % IW >> 3);
        for (int k = 0; k < nSd; k++) if (sdKind[k] == SD_FRONT && sdCell[k] == c && sdDir[k] == d) return true;
        return false;
    }

    // =====================================================================================================
    // Inner grid tags (04 section 4.2), derived instead of painted
    // =====================================================================================================
    public static int WallBit(int dir)
    {
        return dir == 0 ? GenData.T_WALL_N : (dir == 1 ? GenData.T_WALL_E : (dir == 2 ? GenData.T_WALL_S : GenData.T_WALL_W));
    }

    void BuildTags()
    {
        int n = IW * IH;
        for (int i = 0; i < n; i++) { tags[i] = 0; innerRoom[i] = -1; occ[i] = 0; flatOcc[i] = 0; solid[i] = 0; reserve[i] = 0; walk[i] = 0; reach[i] = 0; canopy[i] = 0; }
        for (int r = 0; r < nRooms; r++) { roomDoorCount[r] = 0; roomDoorStart[r] = r * 8; roomX0[r] = 1 << 20; roomZ0[r] = 1 << 20; roomX1[r] = -1; roomZ1[r] = -1; }
        nEmit = 0; nFree = 0; nSockets = 0;

        for (int c = 0; c < W * H; c++)
        {
            int zone = cellZone[c];
            if (zone == GenData.Z_NONE) continue;
            int cx = c % W, cz = c / W, room = cellRoom[c], rt = roomType[room];
            int baseTags = GenData.T_FLOOR;
            if (!GenData.IsBuilding(zone)) baseTags |= GenData.T_OUTDOOR;
            if (zone == GenData.Z_FOREST) baseTags |= GenData.T_SOFT_GROUND;   // a garden's beds and a patio's paving are laid out by their plans
            if (zone == GenData.Z_RELEASE) baseTags |= GenData.T_NO_SPAWN;
            for (int lz = 0; lz < 8; lz++)
                for (int lx = 0; lx < 8; lx++)
                {
                    int ix = cx * 8 + lx, iz = cz * 8 + lz, i = iz * IW + ix;
                    int t = baseTags;
                    for (int d = 0; d < 4; d++)
                    {
                        bool border = (d == 0 && lz == 7) || (d == 1 && lx == 7) || (d == 2 && lz == 0) || (d == 3 && lx == 0);
                        if (!border) continue;
                        int et = EdgeType(cx, cz, d);
                        if (et == GenData.E_NONE) continue;
                        int along = (d == 0 || d == 2) ? lx : lz;
                        if (et == GenData.E_DOOR)
                        {
                            int off = EdgeOff(cx, cz, d);
                            if (along >= off && along < off + GenData.DOOR_CELLS) continue;   // the doorway itself: no wall
                        }
                        t |= WallBit(d);
                        if (et == GenData.E_WINDOW) t |= GenData.T_WINDOW_WALL;
                        if (et != GenData.E_FENCE && (t & GenData.T_OUTDOOR) != 0) t |= GenData.T_BY_BUILDING;
                    }
                    if ((t & GenData.T_WALL_ANY) != 0 && (t & GenData.T_OUTDOOR) != 0) t |= GenData.T_EDGE_OUTDOOR;
                    if ((t & (GenData.T_WALL_N | GenData.T_WALL_S)) != 0 && (t & (GenData.T_WALL_E | GenData.T_WALL_W)) != 0) t |= GenData.T_CORNER;
                    tags[i] = t; innerRoom[i] = room;
                    if (ix < roomX0[room]) roomX0[room] = ix;
                    if (iz < roomZ0[room]) roomZ0[room] = iz;
                    if (ix > roomX1[room]) roomX1[room] = ix;
                    if (iz > roomZ1[room]) roomZ1[room] = iz;
                }
        }
        // doors: keep-clear cells on both sides (two deep, one wider than the doorway), and the room door lists
        for (int c = 0; c < W * H; c++)
        {
            if (cellZone[c] == GenData.Z_NONE) continue;
            int cx = c % W, cz = c / W;
            for (int d = 0; d < 4; d++)
            {
                if (EdgeType(cx, cz, d) != GenData.E_DOOR) continue;
                int off = EdgeOff(cx, cz, d);
                for (int a = off - 1; a <= off + GenData.DOOR_CELLS; a++)
                {
                    if (a < 0 || a > 7) continue;
                    for (int depth = 0; depth < 2; depth++)
                    {
                        int lx, lz;
                        if (d == 0) { lx = a; lz = 7 - depth; }
                        else if (d == 2) { lx = a; lz = depth; }
                        else if (d == 1) { lx = 7 - depth; lz = a; }
                        else { lx = depth; lz = a; }
                        tags[(cz * 8 + lz) * IW + cx * 8 + lx] |= GenData.T_DOOR_CLEAR;
                    }
                }
                // the wall cells right beside the keep-clear cells: where things flank a doorway
                for (int side = 0; side < 2; side++)
                {
                    int a = side == 0 ? off - 2 : off + GenData.DOOR_CELLS + 1;
                    if (a < 0 || a > 7) continue;
                    int lx, lz;
                    if (d == 0) { lx = a; lz = 7; }
                    else if (d == 2) { lx = a; lz = 0; }
                    else if (d == 1) { lx = 7; lz = a; }
                    else { lx = 0; lz = a; }
                    tags[(cz * 8 + lz) * IW + cx * 8 + lx] |= GenData.T_DOOR_SIDE;
                }
                int room = cellRoom[c];
                if (roomDoorCount[room] < 8)
                {
                    int lx2, lz2;
                    if (d == 0) { lx2 = off; lz2 = 7; }
                    else if (d == 2) { lx2 = off; lz2 = 0; }
                    else if (d == 1) { lx2 = 7; lz2 = off; }
                    else { lx2 = 0; lz2 = off; }
                    int k = roomDoorStart[room] + roomDoorCount[room];
                    roomDoors[k] = (cz * 8 + lz2) * IW + cx * 8 + lx2;
                    roomDoorDir[k] = d;
                    roomDoorExt[k] = GenData.IsBuilding(cellZone[c]) != GenData.IsBuilding(ZoneAt(cx + DX[d], cz + DZ[d]));
                    roomDoorCount[room]++;
                    // outdoors the approach to a building's door stays clear: a walkway two cells wide, 3 m deep (report 15, K1 and K4)
                    if (!GenData.IsBuilding(cellZone[c]))
                        for (int a = off; a < off + GenData.DOOR_CELLS; a++)
                            for (int depth = 0; depth < 6; depth++)
                            {
                                int lx, lz;
                                if (d == 0) { lx = a; lz = 7 - depth; }
                                else if (d == 2) { lx = a; lz = depth; }
                                else if (d == 1) { lx = 7 - depth; lz = a; }
                                else { lx = depth; lz = a; }
                                tags[(cz * 8 + lz) * IW + cx * 8 + lx] |= GenData.T_PATH;
                            }
                }
            }
        }
        RoomEntries();
        // nothing spawns within 1.5 m of the release exit (04 section 4.7.2)
        {
            int hx = releaseX + DX[releaseDir], hz = releaseZ + DZ[releaseDir];
            for (int lz = 0; lz < 8; lz++)
                for (int lx = 0; lx < 8; lx++)
                {
                    int depth = releaseDir == 0 ? lz : (releaseDir == 2 ? 7 - lz : (releaseDir == 1 ? lx : 7 - lx));
                    if (depth < 3) tags[(hz * 8 + lz) * IW + hx * 8 + lx] |= GenData.T_NO_SPAWN;
                }
        }
        // per-room cell lists
        int pos = 0;
        for (int r = 0; r < nRooms; r++) roomCellCount[r] = 0;
        for (int i = 0; i < n; i++) if (innerRoom[i] >= 0) roomCellCount[innerRoom[i]]++;
        for (int r = 0; r < nRooms; r++) { roomCellStart[r] = pos; pos += roomCellCount[r]; roomCellCount[r] = 0; }
        for (int i = 0; i < n; i++)
        {
            int r = innerRoom[i];
            if (r < 0) continue;
            roomCells[roomCellStart[r] + roomCellCount[r]++] = i;
        }
        // CENTER: two or more cells from the nearest wall, within the room
        for (int i = 0; i < n; i++) dist[i] = -1;
        int qh = 0, qt = 0;
        for (int i = 0; i < n; i++)
            if (innerRoom[i] >= 0 && (tags[i] & GenData.T_WALL_ANY) != 0) { dist[i] = 0; queue[qt++] = i; }
        while (qh < qt)
        {
            int i = queue[qh++];
            int ix = i % IW, iz = i / IW;
            for (int d = 0; d < 4; d++)
            {
                int jx = ix + DX[d], jz = iz + DZ[d];
                if (jx < 0 || jz < 0 || jx >= IW || jz >= IH) continue;
                int j = jz * IW + jx;
                if (innerRoom[j] != innerRoom[i] || dist[j] >= 0) continue;
                dist[j] = dist[i] + 1; queue[qt++] = j;
            }
        }
        for (int i = 0; i < n; i++)
            if (innerRoom[i] >= 0 && (dist[i] < 0 || dist[i] >= 2)) tags[i] |= GenData.T_CENTER;
        // No walkway is marked between a room's doors any more (generator 2 kept the shortest path clear, which
        // ran straight through the middle of small rooms, where the table belongs). The walkway test of every
        // placement (RoomWalkable) keeps all doors joined by floor at least 1 m wide instead, wherever the
        // furniture leaves it. T_PATH is now only the approach to a building's door outdoors and a forest path.
    }
}
