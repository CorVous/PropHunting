// MapGen, part 2: edges between outer cells (walls, doors, windows, fences), rooms, and the inner-grid tags.
public partial class MapGen
{
    // =====================================================================================================
    // Edges: the socket rules of 04 section 4.1.1, generated from the layout instead of authored per piece
    // =====================================================================================================
    public int EdgeIndex(int x, int z, int dir, out bool horizontal)
    {
        if (dir == 0) { horizontal = true; return (z + 1) * W + x; }
        if (dir == 2) { horizontal = true; return z * W + x; }
        if (dir == 1) { horizontal = false; return z * (W + 1) + x + 1; }
        horizontal = false; return z * (W + 1) + x;
    }

    public int EdgeType(int x, int z, int dir)
    {
        bool h; int i = EdgeIndex(x, z, dir, out h);
        return h ? edgeH[i] : edgeV[i];
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

    void BuildEdges()
    {
        GenRng rng = Rng(ST_EDGES, 0, 0);
        for (int i = 0; i < edgeH.Length; i++) { edgeH[i] = GenData.E_NONE; edgeHOff[i] = 0; }
        for (int i = 0; i < edgeV.Length; i++) { edgeV[i] = GenData.E_NONE; edgeVOff[i] = 0; }
        // base type of every edge of every occupied cell
        for (int c = 0; c < W * H; c++)
        {
            int za = cellZone[c];
            if (za == GenData.Z_NONE) continue;
            int x = c % W, z = c / W;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DX[d], nz = z + DZ[d];
                int zb = ZoneAt(nx, nz);
                int type;
                if (zb == GenData.Z_NONE) type = GenData.IsBuilding(za) ? GenData.E_WALL : GenData.E_FENCE;
                else
                {
                    int n = nz * W + nx;
                    if (cellInst[n] == cellInst[c]) type = cellRoom[n] == cellRoom[c] ? GenData.E_NONE : GenData.E_WALL;
                    else if (GenData.IsBuilding(za) || GenData.IsBuilding(zb)) type = GenData.E_WALL;
                    else if (za == GenData.Z_RELEASE || zb == GenData.Z_RELEASE) type = GenData.E_FENCE;
                    else type = GenData.E_NONE;
                }
                SetEdge(x, z, d, type, 0);
            }
        }
        // connections made while assembling: a doorway where a building is involved, open otherwise
        for (int k = 0; k < nConn; k++)
        {
            int c = connCell[k], d = connDir[k];
            int x = c % W, z = c / W;
            int za = cellZone[c], zb = ZoneAt(x + DX[d], z + DZ[d]);
            if (GenData.IsBuilding(za) || GenData.IsBuilding(zb)) SetEdge(x, z, d, GenData.E_DOOR, DoorOff(ref rng));
            else SetEdge(x, z, d, GenData.E_NONE, 0);
        }
        // interior doors: a spanning tree over each building's rooms, plus a few extra
        int[] parent = new int[26];
        bool[] pairDone = new bool[26 * 26];
        for (int inst = 0; inst < nInst; inst++)
        {
            if (!GenData.IsBuilding(instZone[inst])) continue;
            int ne = 0;
            for (int c = 0; c < W * H; c++)
            {
                if (cellInst[c] != inst) continue;
                int x = c % W, z = c / W;
                for (int d = 0; d < 2; d++)   // N and E only: each interior edge once
                {
                    int nx = x + DX[d], nz = z + DZ[d];
                    if (!InGrid(nx, nz)) continue;
                    int n = nz * W + nx;
                    if (cellInst[n] == inst && cellRoom[n] != cellRoom[c]) cand[ne++] = c * 4 + d;
                }
            }
            Shuffle(cand, ne, ref rng);
            for (int i = 0; i < 26; i++) parent[i] = i;
            for (int i = 0; i < pairDone.Length; i++) pairDone[i] = false;
            for (int i = 0; i < ne; i++)
            {
                int c = cand[i] / 4, d = cand[i] % 4;
                int x = c % W, z = c / W;
                int ra = cellRoom[c], rb = cellRoom[(z + DZ[d]) * W + x + DX[d]];
                int fa = ra, fb = rb;
                while (parent[fa] != fa) fa = parent[fa];
                while (parent[fb] != fb) fb = parent[fb];
                int pair = ra < rb ? ra * 26 + rb : rb * 26 + ra;
                bool extra = rng.Chance(200);
                if (fa != fb)
                {
                    parent[fa] = fb;
                    SetEdge(x, z, d, GenData.E_DOOR, DoorOff(ref rng));
                    pairDone[pair] = true;
                }
                else if (!pairDone[pair] && extra)
                {
                    SetEdge(x, z, d, GenData.E_DOOR, DoorOff(ref rng));
                    pairDone[pair] = true;
                }
            }
        }
        // exterior doors: two or more exits per building where the layout allows (04 section 4.1.2), a few loops
        int loops = size == 0 ? 1 : (size == 1 ? 2 : 3);
        for (int inst = 0; inst < nInst; inst++)
        {
            if (!GenData.IsBuilding(instZone[inst])) continue;
            int ne = 0, doors = 0, doorCell = -1;
            for (int c = 0; c < W * H; c++)
            {
                if (cellInst[c] != inst) continue;
                int x = c % W, z = c / W;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], nz = z + DZ[d];
                    if (!InGrid(nx, nz)) continue;
                    int n = nz * W + nx;
                    if (cellInst[n] == inst || cellInst[n] < 0) continue;
                    int zb = cellZone[n];
                    if (zb != GenData.Z_LAWN && !GenData.IsBuilding(zb)) continue;
                    int t = EdgeType(x, z, d);
                    if (t == GenData.E_DOOR) { doors++; doorCell = c; }
                    else if (t == GenData.E_WALL) cand[ne++] = c * 4 + d;
                }
            }
            Shuffle(cand, ne, ref rng);
            if (doors < 2 && ne > 0)
            {
                // the candidate farthest from the existing door, preferring doors that open onto lawn
                int best = 0, bestD = -1;
                for (int i = 0; i < ne; i++)
                {
                    int c = cand[i] / 4;
                    int dd = doorCell < 0 ? 0 : Abs(c % W - doorCell % W) + Abs(c / W - doorCell / W);
                    int cx = c % W + DX[cand[i] % 4], cz = c / W + DZ[cand[i] % 4];
                    if (cellZone[cz * W + cx] == GenData.Z_LAWN) dd += 2;
                    if (dd > bestD) { bestD = dd; best = i; }
                }
                int cc = cand[best] / 4, d2 = cand[best] % 4;
                SetEdge(cc % W, cc / W, d2, GenData.E_DOOR, DoorOff(ref rng));
                cand[best] = -1;
            }
            for (int i = 0; i < ne; i++)
            {
                bool roll = rng.Chance(120);
                if (cand[i] < 0 || !roll || loops <= 0) continue;
                int cc = cand[i] / 4, d2 = cand[i] % 4;
                if (EdgeType(cc % W, cc / W, d2) != GenData.E_WALL) continue;
                SetEdge(cc % W, cc / W, d2, GenData.E_DOOR, DoorOff(ref rng));
                loops--;
            }
        }
        // windows on outside walls (WINDOW behaves like WALL for movement)
        for (int c = 0; c < W * H; c++)
        {
            int za = cellZone[c];
            if (!GenData.IsBuilding(za)) continue;
            int x = c % W, z = c / W;
            for (int d = 0; d < 4; d++)
            {
                if (EdgeType(x, z, d) != GenData.E_WALL) continue;
                int zb = ZoneAt(x + DX[d], z + DZ[d]);
                if (GenData.IsBuilding(zb)) continue;
                bool roll = rng.Chance(za == GenData.Z_WAREHOUSE ? 200 : 450);
                if (roll) SetEdge(x, z, d, GenData.E_WINDOW, 2);
            }
        }
    }

    // =====================================================================================================
    // Rooms and their types
    // =====================================================================================================
    void BuildRooms()
    {
        nRooms = 0;
        for (int i = 0; i < 8; i++) { statZoneCells[i] = 0; statZoneRooms[i] = 0; }
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
                    roomLeaks[nRooms] = 0; roomSlotItems[nRooms] = 0; roomCovered[nRooms] = 0; roomEligible[nRooms] = 0;
                    nRooms++;
                }
                roomOuterCells[map[l]]++;
                statZoneCells[zone]++;
            }
            for (int c = 0; c < W * H; c++)
                if (cellInst[c] == inst) cellRoom[c] = map[cellRoom[c]];
            AssignRoomTypes(inst, first, nRooms - first);
            statZoneRooms[zone] += nRooms - first;
        }
    }

    void AssignRoomTypes(int inst, int first, int n)
    {
        GenRng rng = Rng(ST_ROOMTYPE, inst, 0);
        int zone = instZone[inst];
        // rooms of this piece by size, largest first
        int[] order = new int[n];
        for (int i = 0; i < n; i++) order[i] = first + i;
        for (int i = 1; i < n; i++)
        {
            int k = i, v = order[i];
            while (k > 0 && roomOuterCells[order[k - 1]] < roomOuterCells[v]) { order[k] = order[k - 1]; k--; }
            order[k] = v;
        }
        if (zone == GenData.Z_HOUSE)
        {
            // living room first, then kitchen, bedroom, bathroom, then a shuffled rest
            int[] rest = { GenData.R_OFFICE, GenData.R_DINING, GenData.R_KIDS, GenData.R_HALL, GenData.R_BEDROOM };
            Shuffle(rest, rest.Length, ref rng);
            int ri = 0;
            bool r600 = rng.Chance(600), r500 = rng.Chance(500);
            for (int i = 0; i < n; i++)
            {
                int r = order[i], t;
                if (i == 0) t = (n == 1 || roomOuterCells[r] >= 2 || r600) ? GenData.R_LIVING : GenData.R_DINING;
                else if (i == 1) t = n == 2 ? (r500 ? GenData.R_KITCHEN : GenData.R_BEDROOM) : GenData.R_KITCHEN;
                else if (i == 2) t = GenData.R_BEDROOM;
                else if (i == 3) t = roomOuterCells[r] == 1 ? GenData.R_BATH : rest[ri++ % rest.Length];
                else t = rest[ri++ % rest.Length];
                roomType[r] = t;
            }
        }
        else if (zone == GenData.Z_WAREHOUSE)
        {
            bool loading = rng.Chance(500);
            for (int i = 0; i < n; i++)
                roomType[order[i]] = i == 0 ? GenData.R_STORAGE : (i == 1 ? (loading ? GenData.R_LOADING : GenData.R_WH_OFFICE) : (loading ? GenData.R_WH_OFFICE : GenData.R_LOADING));
        }
        else if (zone == GenData.Z_DINER)
        {
            for (int i = 0; i < n; i++)
                roomType[order[i]] = i == 0 ? GenData.R_DINER_DINING : (i == 1 ? GenData.R_DINER_KITCHEN : GenData.R_DINER_STORE);
        }
        else if (zone == GenData.Z_LAWN)
        {
            bool hub = inst == 1;
            for (int i = 0; i < n; i++)
            {
                int roll = rng.Range(100);
                roomType[order[i]] = hub ? (roll < 70 ? GenData.R_LAWN_OPEN : GenData.R_PATIO)
                    : (roll < 45 ? GenData.R_LAWN_OPEN : (roll < 70 ? GenData.R_GARDEN : (roll < 88 ? GenData.R_PATIO : GenData.R_FIREPIT)));
            }
        }
        else if (zone == GenData.Z_FOREST)
        {
            for (int i = 0; i < n; i++)
            {
                bool camp = rng.Chance(350);
                roomType[order[i]] = (roomOuterCells[order[i]] >= 2 && camp) ? GenData.R_FOREST_CLEARING : GenData.R_FOREST_DENSE;
            }
        }
        else
        {
            for (int i = 0; i < n; i++) roomType[order[i]] = GenData.R_RELEASE;
        }
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
        for (int i = 0; i < n; i++) { tags[i] = 0; innerRoom[i] = -1; occ[i] = 0; flatOcc[i] = 0; reserve[i] = 0; walk[i] = 0; reach[i] = 0; }
        for (int r = 0; r < nRooms; r++) { roomDoorCount[r] = 0; roomDoorStart[r] = r * 8; roomX0[r] = 1 << 20; roomZ0[r] = 1 << 20; roomX1[r] = -1; roomZ1[r] = -1; }
        nEmit = 0; nFree = 0; nSockets = 0;

        for (int c = 0; c < W * H; c++)
        {
            int zone = cellZone[c];
            if (zone == GenData.Z_NONE) continue;
            int cx = c % W, cz = c / W, room = cellRoom[c], rt = roomType[room];
            int baseTags = GenData.T_FLOOR;
            if (!GenData.IsBuilding(zone)) baseTags |= GenData.T_OUTDOOR;
            if (zone == GenData.Z_FOREST || rt == GenData.R_GARDEN) baseTags |= GenData.T_SOFT_GROUND;
            if (rt == GenData.R_PATIO) baseTags |= GenData.T_COVERED;
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
                int room = cellRoom[c];
                if (roomDoorCount[room] < 8)
                {
                    int lx2, lz2;
                    if (d == 0) { lx2 = off; lz2 = 7; }
                    else if (d == 2) { lx2 = off; lz2 = 0; }
                    else if (d == 1) { lx2 = 7; lz2 = off; }
                    else { lx2 = 0; lz2 = off; }
                    roomDoors[roomDoorStart[room] + roomDoorCount[room]] = (cz * 8 + lz2) * IW + cx * 8 + lx2;
                    roomDoorCount[room]++;
                }
            }
        }
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
        // PATH: keep-clear corridor between a room's doors (shortest path, widened by one cell)
        for (int r = 0; r < nRooms; r++)
        {
            if (!GenData.IsBuilding(roomZone[r]) || roomDoorCount[r] < 2) continue;
            int d0 = roomDoors[roomDoorStart[r]];
            for (int k = 1; k < roomDoorCount[r]; k++)
                MarkPath(r, d0, roomDoors[roomDoorStart[r] + k]);
        }
    }

    void MarkPath(int room, int from, int to)
    {
        int s = roomCellStart[room], e = s + roomCellCount[room];
        for (int k = s; k < e; k++) dist[roomCells[k]] = -1;
        int qh = 0, qt = 0;
        dist[from] = 0; queue[qt++] = from;
        while (qh < qt && dist[to] < 0)
        {
            int i = queue[qh++];
            int ix = i % IW, iz = i / IW;
            for (int d = 0; d < 4; d++)
            {
                int jx = ix + DX[d], jz = iz + DZ[d];
                if (jx < 0 || jz < 0 || jx >= IW || jz >= IH) continue;
                int j = jz * IW + jx;
                if (innerRoom[j] != room || dist[j] >= 0) continue;
                dist[j] = dist[i] + 1; queue[qt++] = j;
            }
        }
        if (dist[to] < 0) return;
        int cur = to;
        while (true)
        {
            int cx = cur % IW, cz = cur / IW;
            for (int dz = -1; dz <= 0; dz++)
                for (int dx = -1; dx <= 0; dx++)
                {
                    int jx = cx + dx, jz = cz + dz;
                    if (jx < 0 || jz < 0) continue;
                    int j = jz * IW + jx;
                    if (innerRoom[j] == room) tags[j] |= GenData.T_PATH;
                }
            if (cur == from) break;
            int next = -1;
            for (int d = 0; d < 4; d++)
            {
                int jx = cx + DX[d], jz = cz + DZ[d];
                if (jx < 0 || jz < 0 || jx >= IW || jz >= IH) continue;
                int j = jz * IW + jx;
                if (innerRoom[j] == room && dist[j] == dist[cur] - 1) { next = j; break; }
            }
            if (next < 0) break;
            cur = next;
        }
    }
}
