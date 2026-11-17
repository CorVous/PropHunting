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
    public Light sun;                       // the scene's directional light: the map's mood turns and tints it

    // ---- the map's mood (Cor: maps should not all look alike), from its seed like the palette: a time of day
    // (morning, noon, afternoon, evening, overcast) for the sun, the ambient light and the fog, and a season
    // (summer, spring, autumn, winter) for the ground. PHSun bakes the shadows from the sun after the build, so
    // it follows. The lobby shares the sun.
    [HideInInspector] public int moodTime, moodSeason;
    public readonly string[] moodTimeName = { "Morning", "Noon", "Afternoon", "Evening", "Overcast" };
    public readonly string[] moodSeasonName = { "Summer", "Spring", "Autumn", "Winter" };

    // ---- light points (Cor: fixtures ready for VRC Light Volumes). Every fixture the builder draws (ceiling lights,
    // porch lights by outside doors, street lamps along the lane, panels under gas station canopies) and every lamp
    // prop adds a point here: lightPos is a world position, or, when lightFollow is set (a lamp prop, which can be
    // knocked over), an offset in that prop's own space. Nothing lights up yet: a Light Volumes pass reads this list.
    public const int LK_CEILING = 0, LK_PORCH = 1, LK_STREET = 2, LK_CANOPY = 3, LK_LAMP = 4;
    [HideInInspector] public Vector3[] lightPos;
    [HideInInspector] public int[] lightKind;
    [HideInInspector] public Transform[] lightFollow;
    [HideInInspector] public int nLights;
    int[] lampDefs;
    Color32 glow;

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

    // ---- the map's palette (Cor: maps should not all look alike): drawn from the seed, so every client paints the
    // same map the same way. Exterior and roof colours per kind of building, the door and window trim, the wood
    // floors, a tint on the indoor walls, and the grass.
    Color32 palHouse, palHouseRoof, palDiner, palDinerRoof, palWh, palWhRoof, palTrim, palWoodA, palWoodB, palWallTint, palGrass, palForest, palSoil;

    Color32 Pick(Color32[] list, uint h) { return list[(int)(h - (h / (uint)list.Length) * (uint)list.Length)]; }

    void InitPalette(uint seed)
    {
        Color32[] houses = { C(232, 212, 180), C(176, 198, 214), C(184, 204, 170), C(236, 222, 160), C(178, 98, 78), C(236, 234, 228), C(196, 184, 214), C(236, 190, 160) };
        Color32[] houseRoofs = { C(172, 94, 80), C(88, 92, 100), C(120, 86, 64), C(84, 100, 122), C(84, 112, 86), C(196, 112, 72) };
        Color32[] diners = { C(214, 92, 80), C(70, 160, 160), C(150, 210, 180), C(230, 190, 70), C(230, 140, 160) };
        Color32[] dinerRoofs = { C(232, 230, 222), C(200, 200, 196), C(198, 70, 64), C(64, 120, 150) };
        Color32[] warehouses = { C(122, 142, 160), C(186, 170, 140), C(130, 150, 130), C(160, 100, 80), C(110, 114, 120) };
        Color32[] warehouseRoofs = { C(100, 106, 116), C(90, 96, 90), C(140, 140, 140), C(120, 80, 60) };
        Color32[] trims = { C(122, 88, 62), C(238, 238, 232), C(54, 54, 58), C(60, 90, 70), C(50, 64, 96) };
        Color32[] woods = { C(198, 154, 106), C(160, 110, 74), C(214, 186, 150), C(130, 96, 70) };
        Color32[] tints = { C(255, 255, 255), C(255, 236, 214), C(214, 232, 255), C(226, 255, 222), C(255, 222, 236) };
        Color32[] grasses = { C(116, 170, 86), C(140, 168, 80), C(100, 150, 96), C(150, 160, 90), C(108, 160, 110) };
        palHouse = Pick(houses, Mix(seed ^ 0x1001u)); palHouseRoof = Pick(houseRoofs, Mix(seed ^ 0x1002u));
        palDiner = Pick(diners, Mix(seed ^ 0x1003u)); palDinerRoof = Pick(dinerRoofs, Mix(seed ^ 0x1004u));
        palWh = Pick(warehouses, Mix(seed ^ 0x1005u)); palWhRoof = Pick(warehouseRoofs, Mix(seed ^ 0x1006u));
        palTrim = Pick(trims, Mix(seed ^ 0x1007u));
        palWoodA = Pick(woods, Mix(seed ^ 0x1008u));
        palWoodB = C(Mathf.Max(0, palWoodA.r - 10), Mathf.Max(0, palWoodA.g - 10), Mathf.Max(0, palWoodA.b - 8));
        palWallTint = Pick(tints, Mix(seed ^ 0x1009u));
        palGrass = Pick(grasses, Mix(seed ^ 0x100Au));
        palForest = C(palGrass.r * 76 / 100, palGrass.g * 81 / 100, palGrass.b * 86 / 100);
        palSoil = C(138 + (palGrass.r - 116) / 3, 104, 74);
        // the season on the ground
        if (moodSeason == 1) { palGrass = Blend(palGrass, C(110, 186, 92), 50); palForest = Blend(palForest, C(84, 150, 80), 50); }
        else if (moodSeason == 2) { palGrass = Blend(palGrass, C(176, 146, 72), 60); palForest = Blend(palForest, C(150, 104, 60), 55); palSoil = Blend(palSoil, C(120, 86, 60), 40); }
        else if (moodSeason == 3) { palGrass = Blend(palGrass, C(228, 232, 238), 80); palForest = Blend(palForest, C(214, 220, 228), 70); palSoil = Blend(palSoil, C(200, 200, 206), 60); }
    }

    // a % b for uint (Udon has no uint remainder)
    uint UModB(uint a, uint b) { return a - (a / b) * b; }

    Color32 Blend(Color32 a, Color32 b, int pct)
    {
        return C(a.r + (b.r - a.r) * pct / 100, a.g + (b.g - a.g) * pct / 100, a.b + (b.b - a.b) * pct / 100);
    }

    void ApplyMood()
    {
        float elev = 52f, intensity = 1f;
        Color sunCol = new Color(1f, 0.96f, 0.88f), sky = new Color(0.62f, 0.66f, 0.72f), eq = new Color(0.52f, 0.53f, 0.54f), gr = new Color(0.50f, 0.48f, 0.45f);
        Color fog = new Color(0.56f, 0.76f, 0.93f);
        float fogStart = 45f, fogEnd = 160f;
        float azim = 38f + (int)UModB(Mix(gen.seed ^ 0x2003u), 60u) - 30f;
        if (moodTime == 0) { elev = 22f; sunCol = new Color(1f, 0.86f, 0.70f); intensity = 0.9f; sky = new Color(0.66f, 0.66f, 0.74f); fog = new Color(0.78f, 0.80f, 0.86f); fogStart = 45f; fogEnd = 160f; azim -= 90f; }
        else if (moodTime == 1) { elev = 72f; sunCol = new Color(1f, 0.99f, 0.95f); intensity = 1.1f; sky = new Color(0.66f, 0.72f, 0.80f); fog = new Color(0.60f, 0.78f, 0.95f); fogStart = 60f; fogEnd = 200f; }
        else if (moodTime == 3) { elev = 14f; sunCol = new Color(1f, 0.66f, 0.42f); intensity = 0.85f; sky = new Color(0.52f, 0.46f, 0.60f); eq = new Color(0.58f, 0.46f, 0.44f); gr = new Color(0.40f, 0.34f, 0.34f); fog = new Color(0.80f, 0.66f, 0.62f); fogStart = 50f; fogEnd = 170f; azim += 120f; }
        else if (moodTime == 4) { elev = 60f; sunCol = new Color(0.82f, 0.84f, 0.88f); intensity = 0.55f; sky = new Color(0.66f, 0.68f, 0.70f); eq = new Color(0.60f, 0.61f, 0.62f); gr = new Color(0.52f, 0.52f, 0.52f); fog = new Color(0.70f, 0.72f, 0.74f); fogStart = 30f; fogEnd = 120f; }
        if (moodSeason == 3) { fog = Color.Lerp(fog, new Color(0.88f, 0.90f, 0.94f), 0.5f); gr = Color.Lerp(gr, new Color(0.80f, 0.82f, 0.86f), 0.6f); }
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(elev, azim, 0f);
            sun.color = sunCol;
            sun.intensity = intensity;
        }
        RenderSettings.ambientSkyColor = sky;
        RenderSettings.ambientEquatorColor = eq;
        RenderSettings.ambientGroundColor = gr;
        RenderSettings.fogColor = fog;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
    }

    Color32 Tinted(Color32 c) { return C(c.r * palWallTint.r / 255, c.g * palWallTint.g / 255, c.b * palWallTint.b / 255); }

    Color32 WallColour(int rt) { return Tinted(BaseWallColour(rt)); }

    Color32 BaseWallColour(int rt)
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
        return zone == GenData.Z_HOUSE ? palHouse : (zone == GenData.Z_DINER ? palDiner : palWh);
    }

    Color32 RoofColour(int zone)
    {
        return zone == GenData.Z_HOUSE ? palHouseRoof : (zone == GenData.Z_DINER ? palDinerRoof : palWhRoof);
    }

    Color32 FloorColour(int rt, int ix, int iz)
    {
        switch (rt)
        {
            case GenData.R_LIVING: case GenData.R_BEDROOM: case GenData.R_OFFICE: case GenData.R_DINING: case GenData.R_KIDS: case GenData.R_HALL:
            case GenData.R_WH_OFFICE:
                return Vary((((iz / 2) & 1) == 0) ? palWoodA : palWoodB, ix / 4, iz / 2, 4);
            case GenData.R_KITCHEN: case GenData.R_DINER_KITCHEN:
                return (((ix + iz) & 1) == 0) ? C(226, 226, 218) : C(204, 210, 212);
            case GenData.R_BATH:
                return (((ix + iz) & 1) == 0) ? C(214, 230, 234) : C(190, 212, 220);
            case GenData.R_DINER_DINING:
                return (((ix + iz) & 1) == 0) ? C(204, 86, 78) : C(238, 230, 212);
            case GenData.R_STORAGE: case GenData.R_LOADING: case GenData.R_DINER_STORE:
                return Vary(C(160, 162, 164), ix, iz, 5);
            case GenData.R_LAWN_OPEN: case GenData.R_FIREPIT:
                return Vary(palGrass, ix, iz, 7);
            case GenData.R_GARDEN:
                return Vary(palSoil, ix, iz, 8);
            case GenData.R_PATIO:
                return (((ix / 2 + iz / 2) & 1) == 0) ? C(196, 190, 178) : C(182, 176, 166);
            case GenData.R_FOREST_DENSE: case GenData.R_FOREST_CLEARING:
                return Vary(palForest, ix, iz, 9);
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
        moodTime = (int)UModB(Mix(gen.seed ^ 0x2001u), 5u);
        moodSeason = (int)UModB(Mix(gen.seed ^ 0x2002u), 4u);
        InitPalette(gen.seed);
        ApplyMood();
        ListBuildings();
        cellZone = gen.cellZone; cellRoom = gen.cellRoom; roomType = gen.roomType; tags = gen.tags; innerRoom = gen.innerRoom;
        emitDef = gen.emitDef; emitX = gen.emitX; emitY = gen.emitY; emitZ = gen.emitZ; emitYaw = gen.emitYaw;
        defFlags = gen.defFlags; zoneWallMm = gen.ZoneWallMm; groundKind = gen.ground;
        objects = new GameObject[gen.nEmit];
        lightPos = new Vector3[64]; lightKind = new int[64]; lightFollow = new Transform[64]; nLights = 0;
        glow = C(255, 240, 196);
        if (lampDefs == null) lampDefs = new int[] { DefIndex("k_lampFloor"), DefIndex("k_lampTable"), DefIndex("q_lampStanding"), DefIndex("q_lampTable") };
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
                    Fixtures();
                    wallsFilter.sharedMesh = ToMesh(B_WALLS, "Walls");
                    Canopies();
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
            case MapGenUdon.GR_LAWN: return Vary(palGrass, ix, iz, 7);
            case MapGenUdon.GR_FOREST: return Vary(palForest, ix, iz, 9);
            case MapGenUdon.GR_SOIL: return Vary(palSoil, ix, iz, 8);
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
        Quad(B_GROUND, new Vector3(-m, -0.03f, -m), new Vector3(-m, -0.03f, h + m), new Vector3(w + m, -0.03f, h + m), new Vector3(w + m, -0.03f, -m), Vector3.up, C(palGrass.r * 85 / 100, palGrass.g * 88 / 100, palGrass.b * 90 / 100));
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
        // door frames take the map's trim; window frames stay white unless the trim is light
        Color32 frame = palTrim, windowFrame = palTrim.r > 200 ? C(54, 54, 58) : C(238, 238, 232);
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
            if (IsBuilding(zoneA) != IsBuilding(zoneB)) Porch(horizontal, ex, ez, d1 + 0.3f, dh, IsBuilding(zoneA) ? 1f : -1f);
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

    // ------------------------------------------------------------------ gas station canopies
    // A flat canopy 4.2 m up over each forecourt (MapGen's R_PUMPS rooms): white, a red fascia, a post near each
    // corner (with a collider). Drawn with the roofs, so the reveal lifts it off as well.
    void Canopies()
    {
        int nr = gen.nRooms;
        for (int r = 0; r < nr; r++)
        {
            if (roomType[r] != GenData.R_PUMPS) continue;
            int x0 = 9999, z0 = 9999, x1 = -1, z1 = -1;
            for (int c = 0; c < W * H; c++)
            {
                if (cellRoom[c] != r) continue;
                int x = c % W, z = c / W;
                if (x < x0) x0 = x; if (x > x1) x1 = x; if (z < z0) z0 = z; if (z > z1) z1 = z;
            }
            if (x1 < 0) continue;
            float ax = x0 * CELL + 0.3f, bx = (x1 + 1) * CELL - 0.3f, az = z0 * CELL + 0.3f, bz = (z1 + 1) * CELL - 0.3f;
            Color32 white = C(238, 238, 234), red = C(200, 56, 48), post = C(170, 172, 176);
            Box3(B_ROOFS, new Vector3(ax, 4.2f, az), new Vector3(bx, 4.32f, bz), white, white, white, FACE_ALL);
            Box(B_ROOFS, new Vector3(ax - 0.02f, 4.32f, az - 0.02f), new Vector3(bx + 0.02f, 4.62f, bz + 0.02f), red, FACE_ALL & ~FACE_YN);
            for (int k = 0; k < 4; k++)
            {
                float px = (k & 1) == 0 ? ax + 0.9f : bx - 0.9f, pz = (k & 2) == 0 ? az + 0.9f : bz - 0.9f;
                Vector3 mn = new Vector3(px - 0.14f, 0f, pz - 0.14f), mx = new Vector3(px + 0.14f, 4.2f, pz + 0.14f);
                Box(B_ROOFS, mn, mx, post, FACE_ALL & ~FACE_YN);
                Collider(mn, mx);
            }
            // a light panel under every other cell of the forecourt
            for (int c = 0; c < W * H; c++)
            {
                if (cellRoom[c] != r) continue;
                int x = c % W, z = c / W;
                if (((x - x0) + (z - z0)) % 2 != 0) continue;
                float cx = (x + 0.5f) * CELL, cz = (z + 0.5f) * CELL;
                Box(B_ROOFS, new Vector3(cx - 0.6f, 4.16f, cz - 0.18f), new Vector3(cx + 0.6f, 4.2f, cz + 0.18f), glow, FACE_ALL & ~FACE_YP);
                AddLight(new Vector3(cx, 4.05f, cz), LK_CANOPY, null);
            }
        }
    }

    // ------------------------------------------------------------------ light fixtures (see lightPos)
    void AddLight(Vector3 p, int kind, Transform follow)
    {
        if (nLights == lightPos.Length)
        {
            int n = nLights * 2;
            Vector3[] p2 = new Vector3[n]; int[] k2 = new int[n]; Transform[] f2 = new Transform[n];
            System.Array.Copy(lightPos, p2, nLights); System.Array.Copy(lightKind, k2, nLights); System.Array.Copy(lightFollow, f2, nLights);
            lightPos = p2; lightKind = k2; lightFollow = f2;
        }
        lightPos[nLights] = p; lightKind[nLights] = kind; lightFollow[nLights] = follow;
        nLights++;
    }

    int DefIndex(string id)
    {
        string[] ids = gen.defId;
        for (int i = 0; i < ids.Length; i++) if (ids[i] == id) return i;
        return -1;
    }

    // a lantern on the outside wall beside a door: `a` metres along the edge, `side` +1/-1 the outside's direction
    void Porch(bool horizontal, float ex, float ez, float a, float doorTop, float side)
    {
        if (a > CELL - 0.2f) return;
        float y0 = doorTop - 0.25f, y1 = doorTop + 0.15f;
        float n0 = WALL_HALF, n1 = WALL_HALF + 0.05f, g1 = WALL_HALF + 0.2f;
        Color32 plate = C(46, 46, 50);
        if (horizontal)
        {
            float z0 = side > 0 ? ez + n0 : ez - n1, z1 = side > 0 ? ez + n1 : ez - n0;
            float q0 = side > 0 ? ez + n1 : ez - g1, q1 = side > 0 ? ez + g1 : ez - n1;
            Box(B_WALLS, new Vector3(ex + a - 0.1f, y0 - 0.04f, z0), new Vector3(ex + a + 0.1f, y1 + 0.04f, z1), plate, FACE_ALL);
            Box(B_WALLS, new Vector3(ex + a - 0.08f, y0, q0), new Vector3(ex + a + 0.08f, y1, q1), glow, FACE_ALL);
            AddLight(new Vector3(ex + a, (y0 + y1) * 0.5f, ez + side * (WALL_HALF + 0.35f)), LK_PORCH, null);
        }
        else
        {
            float x0 = side > 0 ? ex + n0 : ex - n1, x1 = side > 0 ? ex + n1 : ex - n0;
            float q0 = side > 0 ? ex + n1 : ex - g1, q1 = side > 0 ? ex + g1 : ex - n1;
            Box(B_WALLS, new Vector3(x0, y0 - 0.04f, ez + a - 0.1f), new Vector3(x1, y1 + 0.04f, ez + a + 0.1f), plate, FACE_ALL);
            Box(B_WALLS, new Vector3(q0, y0, ez + a - 0.08f), new Vector3(q1, y1, ez + a + 0.08f), glow, FACE_ALL);
            AddLight(new Vector3(ex + side * (WALL_HALF + 0.35f), (y0 + y1) * 0.5f, ez + a), LK_PORCH, null);
        }
    }

    // ceiling lights (one to four per room, spread over its cells) and street lamps along the lane
    void Fixtures()
    {
        int nr = gen.nRooms;
        int[] count = new int[nr], seen = new int[nr];
        for (int c = 0; c < W * H; c++)
        {
            int r = cellRoom[c];
            if (r >= 0 && r < nr && IsBuilding(cellZone[c])) count[r]++;
        }
        Color32 rim = C(226, 226, 220);
        for (int c = 0; c < W * H; c++)
        {
            int r = cellRoom[c];
            if (r < 0 || r >= nr || !IsBuilding(cellZone[c])) continue;
            int n = count[r], k = Mathf.Clamp((n + 3) / 5, 1, 4), i = seen[r];
            seen[r] = i + 1;
            // the middle cell of each of k equal runs of the room's cells
            bool place = false;
            for (int j = 0; j < k; j++) if (i == ((2 * j + 1) * n) / (2 * k)) place = true;
            if (!place) continue;
            float h = zoneWallMm[cellZone[c]] / 1000f;
            float cx = (c % W + 0.5f) * CELL, cz = (c / W + 0.5f) * CELL;
            Box(B_ROOFS, new Vector3(cx - 0.28f, h - 0.05f, cz - 0.28f), new Vector3(cx + 0.28f, h, cz + 0.28f), rim, FACE_ALL & ~FACE_YP);
            Box(B_ROOFS, new Vector3(cx - 0.21f, h - 0.09f, cz - 0.21f), new Vector3(cx + 0.21f, h - 0.05f, cz + 0.21f), glow, FACE_ALL & ~FACE_YP);
            AddLight(new Vector3(cx, h - 0.25f, cz), LK_CEILING, null);
        }

        // street lamps: walk the lane cells, one lamp unless another stands within two cells, on a side that
        // borders something other than lane
        int[] sx = new int[W * H], sz = new int[W * H];
        int ns = 0;
        Color32 pole = C(62, 64, 68);
        for (int c = 0; c < W * H; c++)
        {
            int r = cellRoom[c];
            if (r < 0 || r >= nr || roomType[r] != GenData.R_LANE) continue;
            int x = c % W, z = c / W;
            bool near = false;
            for (int q = 0; q < ns; q++) if (Mathf.Abs(sx[q] - x) <= 2 && Mathf.Abs(sz[q] - z) <= 2) { near = true; break; }
            if (near) continue;
            int side = -1;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DX[d], nz = z + DZ[d];
                if (!InGrid(nx, nz)) { side = d; break; }
                int rn = cellRoom[nz * W + nx];
                if (rn < 0 || rn >= nr || roomType[rn] != GenData.R_LANE) { side = d; break; }
            }
            if (side < 0) continue;
            float cx = (x + 0.5f) * CELL, cz = (z + 0.5f) * CELL;
            float dx = DX[side], dz = DZ[side];
            float px = cx + dx * (CELL * 0.5f - 0.35f), pz = cz + dz * (CELL * 0.5f - 0.35f);
            Vector3 mn = new Vector3(px - 0.07f, 0f, pz - 0.07f), mx = new Vector3(px + 0.07f, 4.5f, pz + 0.07f);
            Box(B_WALLS, mn, mx, pole, FACE_ALL & ~FACE_YN);
            Collider(mn, mx);
            Box(B_WALLS, new Vector3(px - 0.12f, 0f, pz - 0.12f), new Vector3(px + 0.12f, 0.3f, pz + 0.12f), pole, FACE_ALL & ~FACE_YN);
            // the arm reaches over the lane; the head hangs at its end with its glass underneath
            float hx = px - dx * 1.0f, hz = pz - dz * 1.0f;
            Box(B_WALLS, new Vector3(Mathf.Min(px, hx) - 0.04f, 4.42f, Mathf.Min(pz, hz) - 0.04f), new Vector3(Mathf.Max(px, hx) + 0.04f, 4.5f, Mathf.Max(pz, hz) + 0.04f), pole, FACE_ALL);
            Box(B_WALLS, new Vector3(hx - 0.2f, 4.3f, hz - 0.2f), new Vector3(hx + 0.2f, 4.44f, hz + 0.2f), pole, FACE_ALL);
            Box(B_WALLS, new Vector3(hx - 0.16f, 4.27f, hz - 0.16f), new Vector3(hx + 0.16f, 4.3f, hz + 0.16f), glow, FACE_ALL & ~FACE_YP);
            AddLight(new Vector3(hx, 4.1f, hz), LK_STREET, null);
            sx[ns] = x; sz[ns] = z; ns++;
        }
    }

    // ------------------------------------------------------------------ pitched roofs
    // One gable over each house's bounding box, its ridge along the box's long side, 30 degrees. Each roof cell
    // draws its part of the slopes; where the house ends (or an L's inner corner), a gable or side face closes the
    // roof down to the flat slab. Drawn from both sides (the cell's slopes are cut in pieces), no collider.
    int[] instMinX, instMaxX, instMinZ, instMaxZ;
    int[] cellInst;
    const float PITCH = 0.577f, MAX_RISE = 2.4f;

    void ListBuildings()
    {
        int n = gen.nInst;
        cellInst = gen.cellInst;
        instMinX = new int[n]; instMaxX = new int[n]; instMinZ = new int[n]; instMaxZ = new int[n];
        for (int i = 0; i < n; i++) { instMinX[i] = 9999; instMinZ[i] = 9999; instMaxX[i] = -1; instMaxZ[i] = -1; }
        for (int c = 0; c < W * H; c++)
        {
            int i = cellInst[c];
            if (i < 0 || i >= n) continue;
            int x = c % W, z = c / W;
            if (x < instMinX[i]) instMinX[i] = x;
            if (x > instMaxX[i]) instMaxX[i] = x;
            if (z < instMinZ[i]) instMinZ[i] = z;
            if (z > instMaxZ[i]) instMaxZ[i] = z;
        }
    }

    bool SameInst(int x, int z, int inst) { return InGrid(x, z) && cellInst[z * W + x] == inst; }

    // roof height across the ridge at `b` (metres across the box)
    float RoofY(float b, float b0, float b1, float t)
    {
        float mid = (b0 + b1) * 0.5f, half = (b1 - b0) * 0.5f;
        float rise = Mathf.Min(half * PITCH, MAX_RISE);
        return t + rise * Mathf.Clamp01(1f - Mathf.Abs(b - mid) / half);
    }

    // a point from (along the ridge, height, across it)
    Vector3 RP(bool alongX, float a, float y, float b) { return alongX ? new Vector3(a, y, b) : new Vector3(b, y, a); }

    void QuadBoth(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color32 c)
    {
        Vector3 n = Vector3.Cross(p1 - p0, p3 - p0);
        if (n.sqrMagnitude < 1e-8f) return;
        n.Normalize();
        Quad(B_ROOFS, p0, p1, p2, p3, n, c);
        Quad(B_ROOFS, p3, p2, p1, p0, -n, c);
    }

    void PitchedCell(int x, int z, float t)
    {
        int inst = cellInst[z * W + x];
        if (instMinX == null || inst < 0 || inst >= instMinX.Length) return;
        float bx0 = instMinX[inst] * CELL - WALL_HALF, bx1 = (instMaxX[inst] + 1) * CELL + WALL_HALF;
        float bz0 = instMinZ[inst] * CELL - WALL_HALF, bz1 = (instMaxZ[inst] + 1) * CELL + WALL_HALF;
        bool alongX = (bx1 - bx0) >= (bz1 - bz0);
        float b0 = alongX ? bz0 : bx0, b1 = alongX ? bz1 : bx1;
        // this cell's extent, out to the overhang where the house ends
        float cx0 = x * CELL - (SameInst(x - 1, z, inst) ? 0f : WALL_HALF), cx1 = (x + 1) * CELL + (SameInst(x + 1, z, inst) ? 0f : WALL_HALF);
        float cz0 = z * CELL - (SameInst(x, z - 1, inst) ? 0f : WALL_HALF), cz1 = (z + 1) * CELL + (SameInst(x, z + 1, inst) ? 0f : WALL_HALF);
        float a0 = alongX ? cx0 : cz0, a1 = alongX ? cx1 : cz1;   // along the ridge
        float c0 = alongX ? cz0 : cx0, c1 = alongX ? cz1 : cx1;   // across it
        float mid = (b0 + b1) * 0.5f;
        Color32 roof = RoofColour(GenData.Z_HOUSE), wall = ExteriorColour(GenData.Z_HOUSE);
        // the slopes: split at the ridge when it runs through this cell
        float[] cuts = new float[3];
        int nc = 0;
        cuts[nc++] = c0;
        if (mid > c0 + 0.01f && mid < c1 - 0.01f) cuts[nc++] = mid;
        cuts[nc++] = c1;
        for (int k = 0; k + 1 < nc; k++)
        {
            float u0 = cuts[k], u1 = cuts[k + 1];
            float y0 = RoofY(u0, b0, b1, t), y1 = RoofY(u1, b0, b1, t);
            QuadBoth(RP(alongX, a0, y0, u0), RP(alongX, a0, y1, u1), RP(alongX, a1, y1, u1), RP(alongX, a1, y0, u0), roof);
            // gable ends where the house stops along the ridge
            bool endLo = alongX ? !SameInst(x - 1, z, inst) : !SameInst(x, z - 1, inst);
            bool endHi = alongX ? !SameInst(x + 1, z, inst) : !SameInst(x, z + 1, inst);
            if (endLo) QuadBoth(RP(alongX, a0, t, u0), RP(alongX, a0, y0, u0), RP(alongX, a0, y1, u1), RP(alongX, a0, t, u1), wall);
            if (endHi) QuadBoth(RP(alongX, a1, t, u0), RP(alongX, a1, y0, u0), RP(alongX, a1, y1, u1), RP(alongX, a1, t, u1), wall);
        }
        // a side face where the house stops across the ridge but the roof is still high (an L's inner edge)
        bool sideLo = alongX ? !SameInst(x, z - 1, inst) : !SameInst(x - 1, z, inst);
        bool sideHi = alongX ? !SameInst(x, z + 1, inst) : !SameInst(x + 1, z, inst);
        float yl = RoofY(c0, b0, b1, t), yh = RoofY(c1, b0, b1, t);
        if (sideLo && yl > t + 0.02f) QuadBoth(RP(alongX, a0, t, c0), RP(alongX, a0, yl, c0), RP(alongX, a1, yl, c0), RP(alongX, a1, t, c0), wall);
        if (sideHi && yh > t + 0.02f) QuadBoth(RP(alongX, a0, t, c1), RP(alongX, a0, yh, c1), RP(alongX, a1, yh, c1), RP(alongX, a1, t, c1), wall);
    }

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
            // houses get a pitched roof on top of the flat one (the flat slab stays: the ceiling and the collider)
            if (zone == GenData.Z_HOUSE) PitchedCell(x, z, t);
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
        int def = emitDef[e];
        for (int i = 0; i < lampDefs.Length; i++)
        {
            if (def < 0 || lampDefs[i] != def) continue;
            MeshRenderer mr = go.GetComponentInChildren<MeshRenderer>();
            float top = mr != null ? mr.bounds.max.y - t.position.y : 1f;
            // in the prop's own (scaled) space, so the point follows a knock
            AddLight(new Vector3(0f, top * 0.85f / Mathf.Max(0.01f, t.lossyScale.y), 0f), LK_LAMP, t);
            break;
        }
    }

    // ------------------------------------------------------------------ helpers for the game
    public Vector3 ReleasePoint() { return new Vector3((gen.releaseX + 0.5f) * CELL, 0f, (gen.releaseZ + 0.5f) * CELL); }
    public float ReleaseYaw() { return gen.releaseDir * 90f; }
    public void ShowRoofs(bool show) { if (roofsFilter != null) roofsFilter.GetComponent<MeshRenderer>().enabled = show; }
}
