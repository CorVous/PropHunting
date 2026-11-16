using UdonSharp;
using UnityEngine;

// Builds the generated map in the scene, in time slices (the prototype's MapBuilder): ground and floors, walls with
// doorways and windows, roofs and fences as vertex-coloured meshes, then one prefab per prop of the emit list.
// Udon cannot add components, so the mesh holders are scene objects (groundFilter, wallsFilter, roofsFilter) and
// all architecture collision is one MeshCollider built from boxes (collisionCollider). Prop prefabs come from
// PropHunt > Bake prop prefabs.
// Every client builds the same map from the same seed; nothing here is synced.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MapBuilderUdon : UdonSharpBehaviour
{
    public MapGenUdon gen;
    public GameObject[] propPrefabs;        // by def index
    public GameObject fencePrefab;
    public Vector3 fenceModelSize = Vector3.one;
    public Transform propRoot, fenceRoot;
    public MeshFilter groundFilter, wallsFilter, roofsFilter;
    public MeshCollider collisionCollider;
    public float budgetMs = 6f;

    [HideInInspector] public GameObject[] objects;   // by emit index
    [HideInInspector] public bool built, failed, busy;
    [HideInInspector] public float progress;
    [HideInInspector] public string phase = "";
    [HideInInspector] public double genMs, buildMs;

    int stage, cursor;
    System.Diagnostics.Stopwatch watch;

    const float CELL = 4f, INNER = 0.5f, WALL_HALF = 0.1f;
    const int FACE_XN = 1, FACE_XP = 2, FACE_YN = 4, FACE_YP = 8, FACE_ZN = 16, FACE_ZP = 32, FACE_ALL = 63;
    const int B_GROUND = 0, B_WALLS = 1, B_ROOFS = 2, B_COLL = 3;

    // the generator's arrays, cached once per build (reading another behaviour's field is slow)
    int W, H, IW, IH;
    int[] cellZone, cellRoom, roomType, tags, innerRoom, emitDef, emitX, emitY, emitZ, emitYaw, defFlags, zoneWallMm;
    byte[] groundKind;

    // ------------------------------------------------------------------ mesh buffers (MeshBuf without a class)
    Vector3[][] bv;
    Vector3[][] bn;
    Color32[][] bc;
    int[][] bt;
    int[] nv, nt;

    void BufInit(int b, int quads)
    {
        if (bv == null) { bv = new Vector3[4][]; bn = new Vector3[4][]; bc = new Color32[4][]; bt = new int[4][]; nv = new int[4]; nt = new int[4]; }
        if (bv[b] == null || bv[b].Length < quads * 4)
        {
            bv[b] = new Vector3[quads * 4]; bn[b] = new Vector3[quads * 4]; bc[b] = new Color32[quads * 4]; bt[b] = new int[quads * 6];
        }
        nv[b] = 0; nt[b] = 0;
    }

    void BufGrow(int b)
    {
        int n = bv[b].Length * 2;
        Vector3[] v = new Vector3[n]; System.Array.Copy(bv[b], v, nv[b]); bv[b] = v;
        Vector3[] no = new Vector3[n]; System.Array.Copy(bn[b], no, nv[b]); bn[b] = no;
        Color32[] c = new Color32[n]; System.Array.Copy(bc[b], c, nv[b]); bc[b] = c;
        int[] t = new int[n / 4 * 6]; System.Array.Copy(bt[b], t, nt[b]); bt[b] = t;
    }

    void Quad4(int b, Vector3 a, Vector3 p1, Vector3 p2, Vector3 d, Vector3 n, Color32 ca, Color32 cb, Color32 cc, Color32 cd)
    {
        int s = nv[b];
        if (s + 4 > bv[b].Length) BufGrow(b);
        Vector3[] v = bv[b]; Vector3[] no = bn[b]; Color32[] c = bc[b]; int[] t = bt[b];
        v[s] = a; v[s + 1] = p1; v[s + 2] = p2; v[s + 3] = d;
        no[s] = n; no[s + 1] = n; no[s + 2] = n; no[s + 3] = n;
        c[s] = ca; c[s + 1] = cb; c[s + 2] = cc; c[s + 3] = cd;
        int k = nt[b];
        t[k] = s; t[k + 1] = s + 1; t[k + 2] = s + 2; t[k + 3] = s; t[k + 4] = s + 2; t[k + 5] = s + 3;
        nv[b] = s + 4; nt[b] = k + 6;
    }

    void Quad(int b, Vector3 a, Vector3 p1, Vector3 p2, Vector3 d, Vector3 n, Color32 col) { Quad4(b, a, p1, p2, d, n, col, col, col, col); }

    void Box3(int b, Vector3 mn, Vector3 mx, Color32 side, Color32 top, Color32 bottom, int faces)
    {
        float x0 = mn.x, y0 = mn.y, z0 = mn.z, x1 = mx.x, y1 = mx.y, z1 = mx.z;
        if ((faces & FACE_ZP) != 0) Quad(b, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, side);
        if ((faces & FACE_ZN) != 0) Quad(b, new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), Vector3.back, side);
        if ((faces & FACE_YP) != 0) Quad(b, new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up, top);
        if ((faces & FACE_YN) != 0) Quad(b, new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), Vector3.down, bottom);
        if ((faces & FACE_XP) != 0) Quad(b, new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), Vector3.right, side);
        if ((faces & FACE_XN) != 0) Quad(b, new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left, side);
    }

    void Box(int b, Vector3 mn, Vector3 mx, Color32 col, int faces) { Box3(b, mn, mx, col, col, col, faces); }

    Mesh ToMesh(int b, string name)
    {
        int n = nv[b];
        Vector3[] v = new Vector3[n]; System.Array.Copy(bv[b], v, n);
        Vector3[] no = new Vector3[n]; System.Array.Copy(bn[b], no, n);
        Color32[] c = new Color32[n]; System.Array.Copy(bc[b], c, n);
        int[] t = new int[nt[b]]; System.Array.Copy(bt[b], t, nt[b]);
        Mesh m = new Mesh();
        m.name = name;
        if (n >= 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = v; m.normals = no; m.colors32 = c; m.triangles = t;
        m.RecalculateBounds();
        return m;
    }

    // a collision box (all six faces, no colour) in the architecture's collision mesh
    void Collider(Vector3 mn, Vector3 mx) { Box(B_COLL, mn, mx, new Color32(0, 0, 0, 255), FACE_ALL); }

    // ------------------------------------------------------------------ colours (sRGB)
    Color32 C(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }

    Color32 Vary(Color32 c, int ix, int iz, int amount)
    {
        uint h = Mix(U32(ix * 73856093) ^ U32(iz * 19349663));
        uint m = (uint)(2 * amount + 1);
        int d = (int)(h - (h / m) * m) - amount;
        return C(Mathf.Clamp(c.r + d, 0, 255), Mathf.Clamp(c.g + d, 0, 255), Mathf.Clamp(c.b + d, 0, 255));
    }

    uint U32(int v) { return v >= 0 ? (uint)v : ((uint)(v & 0x7FFFFFFF) | 0x80000000u); }

    uint Mix(uint h)
    {
        h ^= h >> 16; h *= 0x7FEB352Du;
        h ^= h >> 15; h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }

    Color32 WallColour(int rt)
    {
        switch (rt)
        {
            case GenData.R_LIVING: return C(236, 226, 206);
            case GenData.R_KITCHEN: return C(222, 234, 220);
            case GenData.R_DINING: return C(238, 218, 200);
            case GenData.R_BEDROOM: return C(212, 222, 238);
            case GenData.R_BATH: return C(206, 230, 234);
            case GenData.R_OFFICE: return C(230, 220, 210);
            case GenData.R_KIDS: return C(246, 226, 208);
            case GenData.R_HALL: return C(228, 228, 220);
            case GenData.R_STORAGE: case GenData.R_LOADING: return C(188, 192, 198);
            case GenData.R_WH_OFFICE: return C(214, 210, 200);
            case GenData.R_DINER_DINING: return C(246, 236, 216);
            case GenData.R_DINER_KITCHEN: return C(226, 232, 232);
            case GenData.R_DINER_STORE: return C(206, 204, 198);
        }
        return C(220, 220, 220);
    }

    Color32 ExteriorColour(int zone)
    {
        return zone == GenData.Z_HOUSE ? C(232, 212, 180) : (zone == GenData.Z_DINER ? C(214, 92, 80) : C(122, 142, 160));
    }

    Color32 RoofColour(int zone)
    {
        return zone == GenData.Z_HOUSE ? C(172, 94, 80) : (zone == GenData.Z_DINER ? C(232, 230, 222) : C(100, 106, 116));
    }

    Color32 FloorColour(int rt, int ix, int iz)
    {
        switch (rt)
        {
            case GenData.R_LIVING: case GenData.R_BEDROOM: case GenData.R_OFFICE: case GenData.R_DINING: case GenData.R_KIDS: case GenData.R_HALL:
            case GenData.R_WH_OFFICE:
                return Vary((((iz / 2) & 1) == 0) ? C(198, 154, 106) : C(188, 144, 98), ix / 4, iz / 2, 4);
            case GenData.R_KITCHEN: case GenData.R_DINER_KITCHEN:
                return (((ix + iz) & 1) == 0) ? C(226, 226, 218) : C(204, 210, 212);
            case GenData.R_BATH:
                return (((ix + iz) & 1) == 0) ? C(214, 230, 234) : C(190, 212, 220);
            case GenData.R_DINER_DINING:
                return (((ix + iz) & 1) == 0) ? C(204, 86, 78) : C(238, 230, 212);
            case GenData.R_STORAGE: case GenData.R_LOADING: case GenData.R_DINER_STORE:
                return Vary(C(160, 162, 164), ix, iz, 5);
            case GenData.R_LAWN_OPEN: case GenData.R_FIREPIT:
                return Vary(C(116, 170, 86), ix, iz, 7);
            case GenData.R_GARDEN:
                return Vary(C(138, 104, 74), ix, iz, 8);
            case GenData.R_PATIO:
                return (((ix / 2 + iz / 2) & 1) == 0) ? C(196, 190, 178) : C(182, 176, 166);
            case GenData.R_FOREST_DENSE: case GenData.R_FOREST_CLEARING:
                return Vary(C(88, 138, 74), ix, iz, 9);
            case GenData.R_RELEASE:
                return (((ix + iz) & 1) == 0) ? C(214, 196, 120) : C(196, 178, 108);
        }
        return C(128, 128, 128);
    }

    // ------------------------------------------------------------------ stepping
    // Starts building the map the generator has finished (gen.done).
    public void BeginBuild()
    {
        if (watch == null) watch = new System.Diagnostics.Stopwatch();
        Clear();
        W = gen.W; H = gen.H; IW = gen.IW; IH = gen.IH;
        cellZone = gen.cellZone; cellRoom = gen.cellRoom; roomType = gen.roomType; tags = gen.tags; innerRoom = gen.innerRoom;
        emitDef = gen.emitDef; emitX = gen.emitX; emitY = gen.emitY; emitZ = gen.emitZ; emitYaw = gen.emitYaw;
        defFlags = gen.defFlags; zoneWallMm = gen.ZoneWallMm; groundKind = gen.ground;
        objects = new GameObject[gen.nEmit];
        BufInit(B_GROUND, IW * IH + 8);
        BufInit(B_WALLS, W * H * 40 + 64);
        BufInit(B_ROOFS, W * H * 20 + 64);
        BufInit(B_COLL, W * H * 24 + 64);
        stage = 1; cursor = 0; built = false; failed = false; busy = true; progress = 0f; buildMs = 0;
        phase = "Laying floors";
    }

    public void Clear()
    {
        if (objects != null)
            for (int i = 0; i < objects.Length; i++) if (objects[i] != null) Destroy(objects[i]);
        objects = null;
        if (fenceRoot != null)
            for (int i = fenceRoot.childCount - 1; i >= 0; i--) Destroy(fenceRoot.GetChild(i).gameObject);
        if (groundFilter != null) groundFilter.sharedMesh = null;
        if (wallsFilter != null) wallsFilter.sharedMesh = null;
        if (roofsFilter != null) roofsFilter.sharedMesh = null;
        if (collisionCollider != null) collisionCollider.sharedMesh = null;
        built = false; busy = false; stage = 0;
    }

    void Update()
    {
        if (!busy) return;
        watch.Reset(); watch.Start();
        bool finished = false;
        while (watch.Elapsed.TotalMilliseconds < budgetMs)
        {
            if (StepOne()) { finished = true; break; }
        }
        watch.Stop();
        buildMs += watch.Elapsed.TotalMilliseconds;
        if (finished)
        {
            busy = false; built = true;
            Debug.Log("[MapBuilder] built in " + buildMs.ToString("0") + " ms of work: " + objects.Length + " props, ground " + nv[B_GROUND] + " verts, walls " + nv[B_WALLS] + ", roofs " + nv[B_ROOFS] + ", collision " + nv[B_COLL]);
        }
    }

    bool StepOne()
    {
        switch (stage)
        {
            case 1:
                if (cursor < IH) { GroundRow(cursor); cursor++; progress = 0.2f * cursor / IH; }
                else { FinishGround(); stage = 2; cursor = 0; phase = "Raising walls"; }
                break;
            case 2:
                if (cursor < W * H) { CellWalls(cursor); cursor++; progress = 0.2f + 0.2f * cursor / (W * H); }
                else
                {
                    wallsFilter.sharedMesh = ToMesh(B_WALLS, "Walls");
                    roofsFilter.sharedMesh = ToMesh(B_ROOFS, "Roofs");
                    collisionCollider.sharedMesh = ToMesh(B_COLL, "ArchCollision");
                    stage = 3; cursor = 0; phase = "Placing props";
                }
                break;
            case 3:
                if (cursor < objects.Length) { MakeProp(cursor); cursor++; progress = 0.4f + 0.6f * cursor / objects.Length; }
                else { stage = 4; }
                break;
            case 4:
                progress = 1f;
                phase = "Ready";
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ ground (MapBuilder: blended outdoor ground, G30)
    Color32[] cellColour;

    Color32 CellColour(int i, int ix, int iz)
    {
        switch (groundKind[i])
        {
            case MapGenUdon.GR_LAWN: return Vary(C(116, 170, 86), ix, iz, 7);
            case MapGenUdon.GR_FOREST: return Vary(C(88, 138, 74), ix, iz, 9);
            case MapGenUdon.GR_SOIL: return Vary(C(138, 104, 74), ix, iz, 8);
            case MapGenUdon.GR_PAVING: return (((ix / 2 + iz / 2) & 1) == 0) ? C(196, 190, 178) : C(182, 176, 166);
            case MapGenUdon.GR_TRACK: return Vary(C(158, 134, 100), ix, iz, 6);
        }
        return FloorColour(roomType[innerRoom[i]], ix, iz);
    }

    bool Blends(int i)
    {
        int g = groundKind[i];
        return g >= MapGenUdon.GR_LAWN && g != MapGenUdon.GR_RELEASE && g != MapGenUdon.GR_PAVING;
    }

    Color32 CornerColour(int cx, int cz, int self)
    {
        int n = 0, r = 0, g = 0, b = 0, kind = -1;
        bool mixed = false;
        for (int k = 0; k < 4; k++)
        {
            int x = cx - (k & 1), z = cz - (k >> 1);
            if (x < 0 || z < 0 || x >= IW || z >= IH) continue;
            int i = z * IW + x;
            if (!Blends(i)) continue;
            if (kind >= 0 && groundKind[i] != kind) mixed = true;
            kind = groundKind[i];
            Color32 c = cellColour[i];
            r += c.r; g += c.g; b += c.b; n++;
        }
        if (!mixed) return cellColour[self];
        return C(r / n, g / n, b / n);
    }

    void GroundRow(int iz)
    {
        if (iz == 0)
        {
            cellColour = new Color32[IW * IH];
            for (int i = 0; i < cellColour.Length; i++)
                if ((tags[i] & GenData.T_FLOOR) != 0) cellColour[i] = CellColour(i, i % IW, i / IW);
        }
        for (int ix = 0; ix < IW; ix++)
        {
            int i = iz * IW + ix;
            if ((tags[i] & GenData.T_FLOOR) == 0) continue;
            float x = ix * INNER, z = iz * INNER;
            Vector3 a = new Vector3(x, 0, z), b = new Vector3(x, 0, z + INNER), c = new Vector3(x + INNER, 0, z + INNER), d = new Vector3(x + INNER, 0, z);
            if (Blends(i)) Quad4(B_GROUND, a, b, c, d, Vector3.up, CornerColour(ix, iz, i), CornerColour(ix, iz + 1, i), CornerColour(ix + 1, iz + 1, i), CornerColour(ix + 1, iz, i));
            else Quad(B_GROUND, a, b, c, d, Vector3.up, cellColour[i]);
        }
    }

    void FinishGround()
    {
        float m = 60f, w = W * CELL, h = H * CELL;
        Quad(B_GROUND, new Vector3(-m, -0.03f, -m), new Vector3(-m, -0.03f, h + m), new Vector3(w + m, -0.03f, h + m), new Vector3(w + m, -0.03f, -m), Vector3.up, C(98, 150, 78));
        groundFilter.sharedMesh = ToMesh(B_GROUND, "Ground");
        Collider(new Vector3(-m, -1f, -m), new Vector3(w + m, 0f, h + m));
        cellColour = null;
    }

    // ------------------------------------------------------------------ walls (MapBuilder: corners G40)
    void WallPiece(bool horizontal, float ex, float ez, float a0, float a1, float y0, float y1, Color32 colA, Color32 colB, bool collider,
                   Color32 loA, Color32 loB, Color32 hiA, Color32 hiB)
    {
        Vector3 mnA, mxA, mnB, mxB;
        int lo, hi;
        if (horizontal)
        {
            mnA = new Vector3(ex + a0, y0, ez - WALL_HALF); mxA = new Vector3(ex + a1, y1, ez);
            mnB = new Vector3(ex + a0, y0, ez); mxB = new Vector3(ex + a1, y1, ez + WALL_HALF);
            Box(B_WALLS, mnA, mxA, colA, FACE_ALL & ~(FACE_ZP | FACE_XN | FACE_XP));
            Box(B_WALLS, mnB, mxB, colB, FACE_ALL & ~(FACE_ZN | FACE_XN | FACE_XP));
            lo = FACE_XN; hi = FACE_XP;
        }
        else
        {
            mnA = new Vector3(ex - WALL_HALF, y0, ez + a0); mxA = new Vector3(ex, y1, ez + a1);
            mnB = new Vector3(ex, y0, ez + a0); mxB = new Vector3(ex + WALL_HALF, y1, ez + a1);
            Box(B_WALLS, mnA, mxA, colA, FACE_ALL & ~(FACE_XP | FACE_ZN | FACE_ZP));
            Box(B_WALLS, mnB, mxB, colB, FACE_ALL & ~(FACE_XN | FACE_ZN | FACE_ZP));
            lo = FACE_ZN; hi = FACE_ZP;
        }
        if (loA.a != 0) Box(B_WALLS, mnA, mxA, loA, lo);
        if (loB.a != 0) Box(B_WALLS, mnB, mxB, loB, lo);
        if (hiA.a != 0) Box(B_WALLS, mnA, mxA, hiA, hi);
        if (hiB.a != 0) Box(B_WALLS, mnB, mxB, hiB, hi);
        if (collider) Collider(mnA, mxB);
    }

    bool IsWallType(int t) { return t == GenData.E_WALL || t == GenData.E_DOOR || t == GenData.E_WINDOW; }

    bool InGrid(int x, int z) { return x >= 0 && z >= 0 && x < W && z < H; }

    bool IsBuilding(int zone) { return zone == GenData.Z_HOUSE || zone == GenData.Z_WAREHOUSE || zone == GenData.Z_DINER; }

    bool WallH(int vx, int vz)
    {
        if (InGrid(vx, vz) && cellZone[vz * W + vx] != GenData.Z_NONE) return IsWallType(gen.EdgeType(vx, vz, 2));
        if (InGrid(vx, vz - 1) && cellZone[(vz - 1) * W + vx] != GenData.Z_NONE) return IsWallType(gen.EdgeType(vx, vz - 1, 0));
        return false;
    }

    bool WallV(int vx, int vz)
    {
        if (InGrid(vx, vz) && cellZone[vz * W + vx] != GenData.Z_NONE) return IsWallType(gen.EdgeType(vx, vz, 3));
        if (InGrid(vx - 1, vz) && cellZone[vz * W + vx - 1] != GenData.Z_NONE) return IsWallType(gen.EdgeType(vx - 1, vz, 1));
        return false;
    }

    Color32 CapColour(int cx, int cz, int buildingZone)
    {
        if (InGrid(cx, cz))
        {
            int c = cz * W + cx;
            if (IsBuilding(cellZone[c])) return WallColour(roomType[cellRoom[c]]);
        }
        return ExteriorColour(buildingZone);
    }

    void Trim(bool horizontal, float ex, float ez, float a0, float a1, float y0, float y1, Color32 col)
    {
        int faces = y0 <= 0f ? FACE_ALL & ~FACE_YN : FACE_ALL;
        float t = WALL_HALF + 0.02f;
        if (horizontal) Box(B_WALLS, new Vector3(ex + a0, y0, ez - t), new Vector3(ex + a1, y1, ez + t), col, faces);
        else Box(B_WALLS, new Vector3(ex - t, y0, ez + a0), new Vector3(ex + t, y1, ez + a1), col, faces);
    }

    void Edge(bool horizontal, int cx, int cz, int type, int off, int cellA, int cellB)
    {
        int zoneA = cellA >= 0 ? cellZone[cellA] : GenData.Z_NONE, zoneB = cellB >= 0 ? cellZone[cellB] : GenData.Z_NONE;
        float ex = cx * CELL, ez = cz * CELL;
        if (type == GenData.E_FENCE) { Fence(horizontal, ex, ez); return; }
        float hA = zoneWallMm[zoneA] / 1000f, hB = zoneWallMm[zoneB] / 1000f;
        float h = Mathf.Max(hA, hB);
        Color32 colA = IsBuilding(zoneA) ? WallColour(roomType[cellRoom[cellA]]) : ExteriorColour(zoneB);
        Color32 colB = IsBuilding(zoneB) ? WallColour(roomType[cellRoom[cellB]]) : ExteriorColour(zoneA);
        int vx = cx, vz = cz, bz = IsBuilding(zoneA) ? zoneA : zoneB;
        float lo, hi;
        Color32 none = new Color32(0, 0, 0, 0);
        Color32 loA = none, loB = none, hiA = none, hiB = none;
        if (horizontal)
        {
            lo = -WALL_HALF;
            if (!WallH(vx - 1, vz)) { loA = CapColour(vx - 1, vz - 1, bz); loB = CapColour(vx - 1, vz, bz); }
            bool next = WallH(vx + 1, vz);
            hi = next ? CELL - WALL_HALF : CELL + WALL_HALF;
            if (!next) { hiA = CapColour(vx + 1, vz - 1, bz); hiB = CapColour(vx + 1, vz, bz); }
        }
        else
        {
            bool h0 = WallH(vx, vz) || WallH(vx - 1, vz), h1 = WallH(vx, vz + 1) || WallH(vx - 1, vz + 1);
            bool south = WallV(vx, vz - 1), north = WallV(vx, vz + 1);
            lo = h0 ? WALL_HALF : -WALL_HALF;
            if (!h0 && !south) { loA = CapColour(vx - 1, vz - 1, bz); loB = CapColour(vx, vz - 1, bz); }
            hi = (h1 || north) ? CELL - WALL_HALF : CELL + WALL_HALF;
            if (!h1 && !north) { hiA = CapColour(vx - 1, vz + 1, bz); hiB = CapColour(vx, vz + 1, bz); }
        }
        Color32 frame = C(122, 88, 62), windowFrame = C(238, 238, 232);
        if (type == GenData.E_WALL)
        {
            WallPiece(horizontal, ex, ez, lo, hi, 0f, h, colA, colB, true, loA, loB, hiA, hiB);
        }
        else if (type == GenData.E_DOOR)
        {
            float d0 = off * INNER, d1 = (off + GenData.DOOR_CELLS) * INNER, dh = GenData.DOOR_HEIGHT_MM / 1000f;
            WallPiece(horizontal, ex, ez, lo, d0, 0f, h, colA, colB, true, loA, loB, colA, colB);
            WallPiece(horizontal, ex, ez, d1, hi, 0f, h, colA, colB, true, colA, colB, hiA, hiB);
            WallPiece(horizontal, ex, ez, d0, d1, dh, h, colA, colB, true, none, none, none, none);
            float e = 0.004f;
            Trim(horizontal, ex, ez, d0 - 0.06f, d0 + e, 0f, dh - e, frame);
            Trim(horizontal, ex, ez, d1 - e, d1 + 0.06f, 0f, dh - e, frame);
            Trim(horizontal, ex, ez, d0 - 0.06f, d1 + 0.06f, dh - e, dh + 0.06f, frame);
        }
        else if (type == GenData.E_WINDOW)
        {
            float w0 = 1.0f, w1 = 3.0f, sill = 0.9f, head = 2.05f;
            WallPiece(horizontal, ex, ez, lo, w0, 0f, h, colA, colB, false, loA, loB, colA, colB);
            WallPiece(horizontal, ex, ez, w1, hi, 0f, h, colA, colB, false, colA, colB, hiA, hiB);
            WallPiece(horizontal, ex, ez, w0, w1, 0f, sill, colA, colB, false, none, none, none, none);
            WallPiece(horizontal, ex, ez, w0, w1, head, h, colA, colB, false, none, none, none, none);
            float e = 0.004f;
            Trim(horizontal, ex, ez, w0 - 0.05f, w1 + 0.05f, sill - 0.05f, sill + e, windowFrame);
            Trim(horizontal, ex, ez, w0 - 0.05f, w1 + 0.05f, head - e, head + 0.05f, windowFrame);
            Trim(horizontal, ex, ez, w0 - 0.05f, w0 + e, sill + e, head - e, windowFrame);
            Trim(horizontal, ex, ez, w1 - e, w1 + 0.05f, sill + e, head - e, windowFrame);
            Trim(horizontal, ex, ez, (w0 + w1) / 2 - 0.02f, (w0 + w1) / 2 + 0.02f, sill + e, head - e, windowFrame);
            if (horizontal) Collider(new Vector3(ex + lo, 0, ez - WALL_HALF), new Vector3(ex + hi, h, ez + WALL_HALF));
            else Collider(new Vector3(ex - WALL_HALF, 0, ez + lo), new Vector3(ex + WALL_HALF, h, ez + hi));
        }
    }

    void Fence(bool horizontal, float ex, float ez)
    {
        GameObject go = Instantiate(fencePrefab);
        Transform t = go.transform;
        t.SetParent(fenceRoot, false);
        t.position = horizontal ? new Vector3(ex + CELL / 2, 0, ez) : new Vector3(ex, 0, ez + CELL / 2);
        t.rotation = Quaternion.Euler(0, horizontal ? 0 : 90, 0);
        t.localScale = new Vector3((CELL + 0.1f) / fenceModelSize.x, 1.1f / fenceModelSize.y, 0.16f / fenceModelSize.z);
        if (horizontal) Collider(new Vector3(ex - 0.1f, 0, ez - 0.08f), new Vector3(ex + CELL + 0.1f, 6f, ez + 0.08f));
        else Collider(new Vector3(ex - 0.08f, 0, ez - 0.1f), new Vector3(ex + 0.08f, 6f, ez + CELL + 0.1f));
    }

    bool Bld(int x, int z) { return InGrid(x, z) && IsBuilding(cellZone[z * W + x]); }

    readonly int[] DX = { 0, 1, 0, -1 };
    readonly int[] DZ = { 1, 0, -1, 0 };

    void CellWalls(int c)
    {
        int x = c % W, z = c / W;
        int zone = cellZone[c];
        for (int d = 0; d < 4; d++)
        {
            int nx = x + DX[d], nz = z + DZ[d];
            bool inGrid = InGrid(nx, nz);
            int n = inGrid ? nz * W + nx : -1;
            if ((d == 2 || d == 3) && inGrid) continue;
            if (zone == GenData.Z_NONE && (n < 0 || cellZone[n] == GenData.Z_NONE)) continue;
            int type = gen.EdgeType(x, z, d);
            if (type == GenData.E_NONE) continue;
            int off = gen.EdgeOff(x, z, d);
            bool horizontal = d == 0 || d == 2;
            int ecx = d == 1 ? x + 1 : x, ecz = d == 0 ? z + 1 : z;
            int cellLow = (d == 0 || d == 1) ? c : n, cellHigh = (d == 0 || d == 1) ? n : c;
            if (cellLow >= 0 && cellZone[cellLow] == GenData.Z_NONE) cellLow = -1;
            if (cellHigh >= 0 && cellZone[cellHigh] == GenData.Z_NONE) cellHigh = -1;
            Edge(horizontal, ecx, ecz, type, off, cellLow, cellHigh);
        }
        if (IsBuilding(zone))
        {
            float h = zoneWallMm[zone] / 1000f, t = h + 0.14f;
            bool bw = Bld(x - 1, z), be = Bld(x + 1, z), bs = Bld(x, z - 1), bn2 = Bld(x, z + 1);
            float x0 = x * CELL - (bw ? 0f : WALL_HALF), x1 = (x + 1) * CELL + (be ? 0f : WALL_HALF);
            Color32 rc = RoofColour(zone), ceiling = C(236, 234, 226);
            Box3(B_ROOFS, new Vector3(x0, h, z * CELL), new Vector3(x1, t, (z + 1) * CELL), rc, rc, ceiling, FACE_ALL);
            for (int side = -1; side <= 1; side += 2)
            {
                if (side < 0 ? bs : bn2) continue;
                int dz = side < 0 ? z - 1 : z + 1;
                float s0 = bw ? (Bld(x - 1, dz) ? x * CELL + WALL_HALF : x * CELL) : x * CELL - WALL_HALF;
                float s1 = be ? (Bld(x + 1, dz) ? (x + 1) * CELL - WALL_HALF : (x + 1) * CELL) : (x + 1) * CELL + WALL_HALF;
                float zz0 = side < 0 ? z * CELL - WALL_HALF : (z + 1) * CELL, zz1 = side < 0 ? z * CELL : (z + 1) * CELL + WALL_HALF;
                Box3(B_ROOFS, new Vector3(s0, h, zz0), new Vector3(s1, t, zz1), rc, rc, ceiling, FACE_ALL);
            }
            Collider(new Vector3(x * CELL - WALL_HALF, h, z * CELL - WALL_HALF), new Vector3((x + 1) * CELL + WALL_HALF, t, (z + 1) * CELL + WALL_HALF));
        }
    }

    // ------------------------------------------------------------------ props
    void MakeProp(int e)
    {
        GameObject prefab = propPrefabs[emitDef[e]];
        if (prefab == null) return;
        GameObject go = Instantiate(prefab);
        Transform t = go.transform;
        t.SetParent(propRoot, true);
        t.position = new Vector3(emitX[e], emitY[e], emitZ[e]) / 1000f;
        t.rotation = Quaternion.Euler(0, emitYaw[e], 0);
        objects[e] = go;
    }

    // ------------------------------------------------------------------ helpers for the game
    public Vector3 ReleasePoint() { return new Vector3((gen.releaseX + 0.5f) * CELL, 0f, (gen.releaseZ + 0.5f) * CELL); }
    public float ReleaseYaw() { return gen.releaseDir * 90f; }
    public void ShowRoofs(bool show) { if (roofsFilter != null) roofsFilter.GetComponent<MeshRenderer>().enabled = show; }
}
