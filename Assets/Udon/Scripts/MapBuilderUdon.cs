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
    public GameObject railPrefab;           // a space base's fence (more-places, revision 12): the station's rails
    // light fixtures from CC0 models (Tools/make_fixtures.py, PropHunt > Bake prop prefabs): ceiling, wall, street,
    // each for an ordinary map, a space base and a medieval village (index kind * 3 + GenData.THEME_*). Each has a
    // "LightPoint" child. A missing one is drawn from boxes instead.
    public GameObject[] fixturePrefabs;
    // the themes' set dressing (PropPrefabBaker.DecorModel, same order): roof modules and dishes on a space base, craters
    // and rocks beyond its fence; a fountain or a well and market stalls on a medieval village's square
    public GameObject[] decorPrefabs;
    // CC0 building parts on the outside walls (Tools/make_parts.py -> parts.bytes, baked by PropPrefabBaker): per theme
    // (index theme * 3) a plain wall, a wall with a window, a wall with a door, normalised to 0..1 along and up the
    // wall; partOpening is each window's or door's hole (x0, y0, x1, y1, normalised), partBody the colour the builder
    // repaints with the building's own
    public Mesh[] partMeshes;
    public Vector4[] partOpening;
    public Color[] partBody;
    Vector3[][] pv, pn;
    Color32[][] pc;
    int[][] pt;
    Color32[] pBody;
    bool partsReady;
    float facadeDepth;   // how far the facade on the wall being drawn stands out (the porch light sits on it)

    void InitParts()
    {
        if (partsReady || partMeshes == null) return;
        int n = partMeshes.Length;
        pv = new Vector3[n][]; pn = new Vector3[n][]; pc = new Color32[n][]; pt = new int[n][]; pBody = new Color32[n];
        for (int i = 0; i < n; i++)
        {
            if (partMeshes[i] == null) continue;
            pv[i] = partMeshes[i].vertices; pn[i] = partMeshes[i].normals; pc[i] = partMeshes[i].colors32; pt[i] = partMeshes[i].triangles;
            Color b = partBody[i];
            pBody[i] = new Color32((byte)Mathf.RoundToInt(b.r * 255f), (byte)Mathf.RoundToInt(b.g * 255f), (byte)Mathf.RoundToInt(b.b * 255f), 255);
        }
        partsReady = true;
    }

    bool HasFacade(int k) { return partsReady && k < pv.Length && pv[k] != null; }

    // part k over the wall from s0 to s1 along the edge (metres from its start) and y0 to y1 up, on the side outSign
    // (+1: the +z or +x side); triangles whose middle lies in the hole (c0..c1, cy0..cy1) are left out
    void AppendPart(int k, bool horizontal, float ex, float ez, float outSign, float s0, float s1, float y0, float y1, Color32 body,
                    float c0, float c1, float cy0, float cy1)
    {
        Vector3[] v = pv[k], n = pn[k];
        Color32[] c = pc[k];
        int[] tri = pt[k];
        // the wall's own shades (the kit's plain wall's colour, lighter or darker) take the building's colour, shade for
        // shade; frames, doors and trims keep theirs
        Color32 pb = pBody[(k / 3) * 3];
        float pbl = Mathf.Sqrt((float)pb.r * pb.r + (float)pb.g * pb.g + (float)pb.b * pb.b) + 0.001f;
        float pbr = pb.r / pbl, pbg = pb.g / pbl, pbb = pb.b / pbl;
        float w = s1 - s0, h = y1 - y0;
        // a rotation (never a mirror): along x up = out
        Vector3 a, o, org;
        if (horizontal)
        {
            o = new Vector3(0f, 0f, outSign);
            a = outSign > 0 ? Vector3.right : Vector3.left;
            org = new Vector3(ex + (outSign > 0 ? s0 : s1), y0, ez + outSign * WALL_HALF);
        }
        else
        {
            o = new Vector3(outSign, 0f, 0f);
            a = outSign > 0 ? Vector3.back : Vector3.forward;
            org = new Vector3(ex + outSign * WALL_HALF, y0, ez + (outSign > 0 ? s1 : s0));
        }
        bool cull = c1 > c0;
        int b = B_WALLS;
        for (int i = 0; i + 2 < tri.Length; i += 3)
        {
            Vector3 p0 = v[tri[i]], p1 = v[tri[i + 1]], p2 = v[tri[i + 2]];
            if (cull)
            {
                float mx = (p0.x + p1.x + p2.x) / 3f, my = (p0.y + p1.y + p2.y) / 3f;
                float e = s0 + mx * w, yy = y0 + my * h;
                if (e > c0 && e < c1 && yy > cy0 && yy < cy1) continue;
            }
            while (nv[b] + 3 > bv[b].Length || nt[b] + 3 > bt[b].Length) BufGrow(b);
            for (int q = 0; q < 3; q++)
            {
                int vi = tri[i + q];
                Vector3 p = v[vi], nn = n[vi];
                bv[b][nv[b]] = org + a * (p.x * w) + Vector3.up * (p.y * h) + o * p.z;
                bn[b][nv[b]] = a * nn.x + Vector3.up * nn.y + o * nn.z;
                Color32 col = c[vi];
                float cl = Mathf.Sqrt((float)col.r * col.r + (float)col.g * col.g + (float)col.b * col.b) + 0.001f;
                if (theme == GenData.THEME_NONE && (col.r * pbr + col.g * pbg + col.b * pbb) / cl > 0.998f)   // the space and medieval kits keep their own colours
                {
                    float s = cl / pbl;
                    bc[b][nv[b]] = new Color32((byte)Mathf.Min(255, (int)(body.r * s)), (byte)Mathf.Min(255, (int)(body.g * s)), (byte)Mathf.Min(255, (int)(body.b * s)), 255);
                }
                else bc[b][nv[b]] = col;
                bt[b][nt[b]] = nv[b];
                nv[b]++; nt[b]++;
            }
        }
    }

    // the facade over one outside wall: plain parts, the window part over a window, the door part round a doorway.
    // Returns the window's hole (x0, sill, x1, head) through w0..head when the wall has a window part.
    float fW0, fW1, fSill, fHead;
    bool Facade(bool horizontal, float ex, float ez, int type, int off, int cellIn, int cellOut, float h, Color32 body)
    {
        facadeDepth = 0f;
        if (!partsReady || cellIn < 0) return false;
        int zone = cellZone[cellIn];
        if (zone == GenData.Z_CABIN) return false;   // the log cabin keeps its logs
        if (cellOut >= 0 && cellRoom[cellOut] >= 0)
        {
            int rt = roomType[cellRoom[cellIn]], ot = roomType[cellRoom[cellOut]];
            if ((rt == GenData.R_GARAGE_BAY && ot == GenData.R_FORECOURT) || (rt == GenData.R_BARN && ot == GenData.R_FARMYARD)) return false;   // the big doors
        }
        int k0 = (theme >= 0 && theme < 3 ? theme : 0) * 3;
        if (!HasFacade(k0)) return false;
        float sgn = cellIn == cellLowOf ? 1f : -1f;
        if (type == GenData.E_WALL)
        {
            AppendPart(k0, horizontal, ex, ez, sgn, 0f, CELL, 0f, h, body, 0f, 0f, 0f, 0f);
        }
        else if (type == GenData.E_WINDOW && HasFacade(k0 + 1))
        {
            Vector4 op = partOpening[k0 + 1];
            fW0 = 1f + op.x * 2f; fW1 = 1f + op.z * 2f; fSill = op.y * h; fHead = op.w * h;
            AppendPart(k0, horizontal, ex, ez, sgn, 0f, 1f, 0f, h, body, 0f, 0f, 0f, 0f);
            AppendPart(k0 + 1, horizontal, ex, ez, sgn, 1f, 3f, 0f, h, body, fW0, fW1, fSill, fHead);
            AppendPart(k0, horizontal, ex, ez, sgn, 3f, CELL, 0f, h, body, 0f, 0f, 0f, 0f);
        }
        else if (type == GenData.E_DOOR && HasFacade(k0 + 2))
        {
            Vector4 op = partOpening[k0 + 2];
            float d0 = off * INNER, d1 = (off + GenData.DOOR_CELLS) * INNER, dh = GenData.DOOR_HEIGHT_MM / 1000f;
            float pw = (d1 - d0) / Mathf.Max(0.05f, op.z - op.x), ph = Mathf.Min(h, dh / Mathf.Max(0.05f, op.w - op.y));
            float p0 = d0 - op.x * pw, p1 = p0 + pw;
            if (p0 < 0f) { p1 -= p0; p0 = 0f; }
            if (p1 > CELL) { p0 -= p1 - CELL; p1 = CELL; }
            if (p0 > 0.01f) AppendPart(k0, horizontal, ex, ez, sgn, 0f, p0, 0f, h, body, 0f, 0f, 0f, 0f);
            AppendPart(k0 + 2, horizontal, ex, ez, sgn, p0, p1, 0f, ph, body, d0, d1, 0f, dh);
            if (ph < h - 0.01f) AppendPart(k0, horizontal, ex, ez, sgn, p0, p1, ph, h, body, 0f, 0f, 0f, 0f);
            if (p1 < CELL - 0.01f) AppendPart(k0, horizontal, ex, ez, sgn, p1, CELL, 0f, h, body, 0f, 0f, 0f, 0f);
        }
        else return false;
        facadeDepth = theme == GenData.THEME_SPACE ? 0.3f : (theme == GenData.THEME_MEDIEVAL ? 0.15f : 0.2f);
        return true;
    }
    int cellLowOf;   // Edge's low-side cell (the facade on a building on that side faces +)
    const int DECOR_SOLAR = 0, DECOR_CARGO = 1, DECOR_DISH = 2, DECOR_CRATER = 3, DECOR_CRATER_S = 4, DECOR_METEOR = 5, DECOR_CRYSTAL = 6,
        DECOR_FOUNTAIN = 7, DECOR_WELL = 8, DECOR_STALL_R = 9, DECOR_STALL_G = 10, DECOR_BANNER = 11, DECOR_HEDGE = 12;
    public Transform fixtureRoot;
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
        // the map themes' skies (more-places, revision 12): a red planet's dusk, a softer blue over a medieval village
        // Cor: the themes should look distinct. A space base: a dark violet sky, cold light, the ground lit dimly; a
        // medieval village: warm golden light and a warm haze
        if (gen.mapTheme == GenData.THEME_SPACE)
        {
            fog = new Color(0.22f, 0.17f, 0.30f); fogStart = 40f; fogEnd = 150f;
            sky = new Color(0.42f, 0.40f, 0.58f); eq = new Color(0.40f, 0.34f, 0.44f); gr = new Color(0.36f, 0.26f, 0.24f);
            sunCol = new Color(0.86f, 0.90f, 1f); intensity *= 0.9f;
        }
        else if (gen.mapTheme == GenData.THEME_MEDIEVAL)
        {
            fog = Color.Lerp(fog, new Color(0.88f, 0.80f, 0.64f), 0.55f);
            sunCol = Color.Lerp(sunCol, new Color(1f, 0.84f, 0.60f), 0.5f);
            sky = Color.Lerp(sky, new Color(0.74f, 0.66f, 0.52f), 0.35f);
        }
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

    // ---- more-places (revision 9): a colour scheme per building on top of the map's palette, so two houses on one map
    // do not look alike. From the seed and the building's instance: every client paints it the same. A KayKit-furnished
    // house takes schemes 4 to 7 (warmer, older). Revision 12/13: a space base or a medieval village has its own look.
    int theme;          // the map's theme

    int Style(int inst) { return inst < 0 ? 0 : (int)(Mix(gen.seed ^ U32(inst * 9091 + 77)) & 3u) + (gen.KayHouse(inst) ? 4 : 0); }
    int StyleAt(int cell) { return cell >= 0 && IsBuilding(cellZone[cell]) ? Style(cellInst[cell]) : 0; }

    readonly int[] TintR = { 0, 10, -14, 4, 14, -6, 18, 6 }, TintG = { 0, -6, 2, 8, 2, 10, -8, -4 }, TintB = { 0, -14, 10, -10, -20, -14, -2, 14 };
    Color32 Tint(Color32 c, int style) { return C(Mathf.Clamp(c.r + TintR[style], 0, 255), Mathf.Clamp(c.g + TintG[style], 0, 255), Mathf.Clamp(c.b + TintB[style], 0, 255)); }

    Color32 WallColourAt(int cell)
    {
        Color32 c = Tint(Tinted(BaseWallColour(roomType[cellRoom[cell]])), StyleAt(cell));
        if (theme == GenData.THEME_SPACE) return Blend(c, C(214, 220, 228), 70);       // pale panels
        if (theme == GenData.THEME_MEDIEVAL) return Blend(c, C(232, 222, 198), 60);    // limewash
        return c;
    }

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
            case GenData.R_SHOP_FLOOR: return C(240, 240, 232);
            case GenData.R_STOCKROOM: case GenData.R_PARTS: return C(196, 198, 200);
            case GenData.R_BACK_OFFICE: return C(222, 214, 200);
            case GenData.R_GARAGE_BAY: return C(178, 182, 186);
            case GenData.R_BARN: case GenData.R_FEED_STORE: case GenData.R_FARM_WORK: return C(176, 132, 92);
            case GenData.R_TEEN: return C(178, 196, 226);
            case GenData.R_PLAYROOM: return C(246, 222, 150);
            case GenData.R_BOXROOM: return C(206, 194, 176);
            case GenData.R_PANTRY: return C(232, 226, 206);
            case GenData.R_CABIN_MAIN: return C(186, 138, 92);
            case GenData.R_BUNKROOM: return C(196, 150, 104);
            case GenData.R_ARCADE: return C(70, 60, 96);
            case GenData.R_SNACKBAR: return C(240, 236, 226);
            case GenData.R_PRIZE_STORE: return C(200, 196, 190);
            case GenData.R_NAVE: return C(232, 228, 214);
            case GenData.R_VESTRY: return C(214, 206, 190);
        }
        return C(220, 220, 220);
    }

    Color32[] houseOut, houseRoof, shopOut, garageOut, spaceOut, medievalOut, fairOut, cabinOut, barnOut, plankA, plankB;

    void InitSchemes()
    {
        if (houseOut != null) return;
        houseOut = new Color32[] { C(232, 212, 180), C(196, 214, 222), C(204, 216, 186), C(184, 112, 90), C(150, 170, 140), C(226, 190, 150), C(170, 120, 110), C(214, 214, 196) };
        houseRoof = new Color32[] { C(172, 94, 80), C(92, 100, 112), C(84, 104, 92), C(122, 90, 70), C(70, 74, 82), C(140, 78, 62), C(60, 70, 64), C(104, 70, 60) };
        shopOut = new Color32[] { C(240, 222, 150), C(140, 196, 190), C(236, 236, 228), C(200, 160, 196) };
        garageOut = new Color32[] { C(150, 160, 170), C(176, 104, 84), C(196, 190, 170), C(120, 136, 120) };
        spaceOut = new Color32[] { C(96, 102, 114), C(110, 116, 128), C(86, 92, 104), C(120, 124, 132) };   // gunmetal hulls
        medievalOut = new Color32[] { C(236, 226, 200), C(222, 206, 172), C(214, 214, 204), C(240, 232, 214) };
        fairOut = new Color32[] { C(250, 210, 80), C(120, 200, 230), C(240, 140, 170), C(150, 220, 140) };
        cabinOut = new Color32[] { C(140, 96, 62), C(120, 82, 54), C(158, 112, 72), C(110, 86, 64) };
        barnOut = new Color32[] { C(170, 60, 50), C(150, 108, 72), C(186, 82, 58), C(132, 92, 64) };
        plankA = new Color32[] { C(198, 154, 106), C(150, 104, 70), C(214, 190, 150), C(170, 160, 150), C(128, 86, 58), C(178, 128, 84), C(110, 78, 60), C(190, 160, 120) };
        plankB = new Color32[] { C(188, 144, 98), C(140, 96, 64), C(204, 178, 140), C(160, 150, 140), C(118, 78, 52), C(166, 118, 76), C(100, 70, 54), C(178, 148, 110) };
    }

    // the map's palette colour for a house, the diner and the warehouse, blended with the building's own scheme
    Color32 ExteriorColour(int zone, int style)
    {
        if (theme == GenData.THEME_SPACE) return spaceOut[style & 3];
        if (theme == GenData.THEME_MEDIEVAL) return zone == GenData.Z_CHAPEL || zone == GenData.Z_WAREHOUSE ? C(176, 170, 158) : medievalOut[style & 3];
        switch (zone)
        {
            case GenData.Z_HOUSE: return Blend(palHouse, houseOut[style], 55);
            case GenData.Z_DINER: return palDiner;
            case GenData.Z_WAREHOUSE: return palWh;
            case GenData.Z_SHOP: return shopOut[style & 3];
            case GenData.Z_GARAGE: return garageOut[style & 3];
            case GenData.Z_BARN: return barnOut[style & 3];
            case GenData.Z_CABIN: return cabinOut[style & 3];
            case GenData.Z_FAIR: return fairOut[style & 3];
            case GenData.Z_CHAPEL: return (style & 1) == 0 ? C(170, 166, 156) : C(196, 186, 168);
        }
        return palWh;
    }

    Color32 RoofColour(int zone, int style)
    {
        if (theme == GenData.THEME_SPACE) return (style & 1) == 0 ? C(62, 66, 76) : C(74, 78, 88);
        if (theme == GenData.THEME_MEDIEVAL) return (style & 1) == 0 ? C(176, 146, 86) : C(150, 76, 58);   // thatch, red tiles
        switch (zone)
        {
            case GenData.Z_HOUSE: return Blend(palHouseRoof, houseRoof[style], 55);
            case GenData.Z_DINER: return palDinerRoof;
            case GenData.Z_WAREHOUSE: return palWhRoof;
            case GenData.Z_SHOP: return C(84, 88, 96);
            case GenData.Z_GARAGE: return C(110, 116, 124);
            case GenData.Z_BARN: return (style & 1) == 0 ? C(78, 82, 90) : C(112, 86, 66);
            case GenData.Z_CABIN: return (style & 1) == 0 ? C(70, 92, 70) : C(96, 70, 56);
            case GenData.Z_FAIR: return (style & 1) == 0 ? C(200, 60, 60) : C(60, 90, 170);
            case GenData.Z_CHAPEL: return C(74, 80, 92);
        }
        return palWhRoof;
    }

    Color32 FloorColour(int rt, int ix, int iz, int style)
    {
        // a space base's floors are deck plates; a medieval village's kitchens and halls stone flags, dark boards elsewhere
        if (theme == GenData.THEME_SPACE) return (((ix / 2 + iz / 2) & 1) == 0) ? C(132, 138, 148) : C(120, 126, 136);
        if (theme == GenData.THEME_MEDIEVAL)
        {
            if (rt == GenData.R_KITCHEN || rt == GenData.R_HALL || rt == GenData.R_DINER_KITCHEN || rt == GenData.R_PANTRY || rt == GenData.R_BATH
                || rt == GenData.R_NAVE || rt == GenData.R_VESTRY || rt == GenData.R_STORAGE || rt == GenData.R_LOADING || rt == GenData.R_DINER_STORE)
                return Vary((((ix / 2 + iz / 2) & 1) == 0) ? C(160, 154, 142) : C(146, 140, 130), ix / 2, iz / 2, 5);
            return Vary(((ix & 1) == 0) ? C(118, 84, 56) : C(108, 76, 50), ix, iz / 3, 4);
        }
        switch (rt)
        {
            case GenData.R_LIVING: case GenData.R_BEDROOM: case GenData.R_OFFICE: case GenData.R_DINING: case GenData.R_KIDS: case GenData.R_HALL:
            case GenData.R_WH_OFFICE: case GenData.R_BACK_OFFICE: case GenData.R_FEED_STORE: case GenData.R_FARM_WORK:
            case GenData.R_TEEN: case GenData.R_PLAYROOM: case GenData.R_BOXROOM: case GenData.R_CABIN_MAIN: case GenData.R_BUNKROOM:
                // wood planks: the map's wood blended with the building's
                return Vary((((iz / 2) & 1) == 0) ? Blend(palWoodA, plankA[style], 50) : Blend(palWoodB, plankB[style], 50), ix / 4, iz / 2, 4);
            case GenData.R_SHOP_FLOOR:
                return (((ix + iz) & 1) == 0) ? C(232, 232, 226) : C(196, 204, 208);
            case GenData.R_STOCKROOM: case GenData.R_PARTS: case GenData.R_GARAGE_BAY:
                return Vary(C(150, 152, 156), ix, iz, 7);
            case GenData.R_BARN:
                return Vary(C(184, 156, 104), ix, iz, 10);
            case GenData.R_ARCADE:
                return ((((ix / 2) + (iz / 2)) & 1) == 0) ? C(150, 46, 60) : C(44, 50, 96);
            case GenData.R_SNACKBAR: case GenData.R_PRIZE_STORE:
                return (((ix + iz) & 1) == 0) ? C(230, 230, 222) : C(200, 206, 210);
            case GenData.R_NAVE: case GenData.R_VESTRY:
                return Vary((((ix / 2 + iz / 2) & 1) == 0) ? C(178, 172, 160) : C(164, 158, 148), ix / 2, iz / 2, 5);
            case GenData.R_KITCHEN: case GenData.R_DINER_KITCHEN: case GenData.R_PANTRY:
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
        // the ground's season follows the generator's (its trees and decorations): autumn trees, winter decorations
        // and deep-summer green come from it; an ordinary summer map is summer or spring
        theme = gen.mapTheme;
        if (gen.holiday == 1) moodSeason = 3;
        else if (gen.season == 1) moodSeason = 2;
        else if (gen.season == 2 || theme != GenData.THEME_NONE) moodSeason = 0;
        else moodSeason = (int)UModB(Mix(gen.seed ^ 0x2002u), 2u);
        InitPalette(gen.seed);
        InitSchemes();
        InitParts();
        ApplyMood();
        ListBuildings();
        cellZone = gen.cellZone; cellRoom = gen.cellRoom; roomType = gen.roomType; tags = gen.tags; innerRoom = gen.innerRoom;
        emitDef = gen.emitDef; emitX = gen.emitX; emitY = gen.emitY; emitZ = gen.emitZ; emitYaw = gen.emitYaw;
        defFlags = gen.defFlags; zoneWallMm = gen.ZoneWallMm; groundKind = gen.ground;
        objects = new GameObject[gen.nEmit];
        lightPos = new Vector3[64]; lightKind = new int[64]; lightFollow = new Transform[64]; nLights = 0;
        glow = new Color32(255, 240, 196, 0);   // alpha 0: PropHunt/Lit draws it glowing
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
        if (fixtureRoot != null)
            for (int i = fixtureRoot.childCount - 1; i >= 0; i--) Destroy(fixtureRoot.GetChild(i).gameObject);
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
                    PitchedRoofs();
                    Dressing();
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

    bool LaneAt(int ix, int iz)
    {
        if (ix < 0 || iz < 0 || ix >= IW || iz >= IH) return false;
        int r = innerRoom[iz * IW + ix];
        return r >= 0 && r < roomType.Length && roomType[r] == GenData.R_LANE;
    }

    Color32 CellColour(int i, int ix, int iz)
    {
        if (theme != GenData.THEME_NONE && LaneAt(ix, iz))
        {
            if (theme == GenData.THEME_MEDIEVAL) return Vary(C(146, 116, 82), ix, iz, 9);
            bool edge = !LaneAt(ix - 1, iz) || !LaneAt(ix + 1, iz) || !LaneAt(ix, iz - 1) || !LaneAt(ix, iz + 1);
            if (edge) return C(90, 210, 230);   // the walkway's light strip
            return ((ix + iz) & 1) == 0 ? C(158, 166, 178) : C(146, 154, 166);
        }
        if (theme == GenData.THEME_SPACE)
            switch (groundKind[i])
            {
                // a red planet: regolith for grass, dark scree for the wood, metal deck for paving, ice for water
                case MapGenUdon.GR_LAWN: return Vary(C(150, 84, 64), ix, iz, 9);
                case MapGenUdon.GR_FOREST: return Vary(C(104, 64, 56), ix, iz, 9);
                case MapGenUdon.GR_SOIL: return Vary(C(124, 76, 58), ix, iz, 8);
                case MapGenUdon.GR_PAVING: case MapGenUdon.GR_DECK: return (((ix / 2 + iz / 2) & 1) == 0) ? C(150, 156, 166) : C(138, 144, 154);
                case MapGenUdon.GR_TRACK: return Vary(C(204, 152, 112), ix, iz, 6);
                case MapGenUdon.GR_WATER: return Vary(C(120, 196, 210), ix, iz, 5);
            }
        else if (theme == GenData.THEME_MEDIEVAL)
            switch (groundKind[i])
            {
                case MapGenUdon.GR_PAVING: return Vary(((ix + iz) % 3 == 0) ? C(150, 146, 138) : C(166, 160, 150), ix, iz, 8);   // cobbles
                case MapGenUdon.GR_TRACK: return Vary(C(150, 120, 86), ix, iz, 7);
            }
        switch (groundKind[i])
        {
            case MapGenUdon.GR_WATER: return Vary(C(86, 150, 196), ix, iz, 5);
            case MapGenUdon.GR_DECK: return Vary((((ix / 2) & 1) == 0) ? C(170, 124, 82) : C(156, 112, 74), ix / 2, iz / 4, 4);   // decking boards
            case MapGenUdon.GR_LAWN: return Vary(palGrass, ix, iz, 7);
            case MapGenUdon.GR_FOREST: return Vary(palForest, ix, iz, 9);
            case MapGenUdon.GR_SOIL: return Vary(palSoil, ix, iz, 8);
            case MapGenUdon.GR_PAVING: return (((ix / 2 + iz / 2) & 1) == 0) ? C(196, 190, 178) : C(182, 176, 166);
            case MapGenUdon.GR_TRACK: return Vary(C(158, 134, 100), ix, iz, 6);
        }
        return FloorColour(roomType[innerRoom[i]], ix, iz, StyleAt(ix / GenData.INNER_PER_CELL + iz / GenData.INNER_PER_CELL * W));
    }

    bool Blends(int i)
    {
        int g = groundKind[i];
        return g >= MapGenUdon.GR_LAWN && g != MapGenUdon.GR_RELEASE && g != MapGenUdon.GR_PAVING && g != MapGenUdon.GR_WATER && g != MapGenUdon.GR_DECK;
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
        Quad(B_GROUND, new Vector3(-m, -0.03f, -m), new Vector3(-m, -0.03f, h + m), new Vector3(w + m, -0.03f, h + m), new Vector3(w + m, -0.03f, -m), Vector3.up, theme == GenData.THEME_SPACE ? C(140, 80, 62) : C(palGrass.r * 85 / 100, palGrass.g * 88 / 100, palGrass.b * 90 / 100));   // a red planet beyond a space base's fence
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

    bool IsBuilding(int zone) { return zone == GenData.Z_HOUSE || zone == GenData.Z_WAREHOUSE || zone == GenData.Z_DINER || zone == GenData.Z_SHOP || zone == GenData.Z_GARAGE || zone == GenData.Z_BARN || zone == GenData.Z_CABIN || zone == GenData.Z_FAIR || zone == GenData.Z_CHAPEL; }

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
            if (IsBuilding(cellZone[c])) return WallColourAt(c);
        }
        return ExteriorColour(buildingZone, capStyle);
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
        int styleA = StyleAt(cellA), styleB = StyleAt(cellB);
        Color32 colA = IsBuilding(zoneA) ? WallColourAt(cellA) : ExteriorColour(zoneB, styleB);
        Color32 colB = IsBuilding(zoneB) ? WallColourAt(cellB) : ExteriorColour(zoneA, styleA);
        capStyle = IsBuilding(zoneA) ? styleA : styleB;
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
        // dark timber in a medieval village, gunmetal on a space base
        if (theme == GenData.THEME_MEDIEVAL) { frame = C(84, 58, 38); windowFrame = frame; }
        else if (theme == GenData.THEME_SPACE) { frame = C(84, 90, 102); windowFrame = frame; }
        // the CC0 facade on a building's outside wall (Cor: more interesting buildings)
        bool facade = false;
        facadeDepth = 0f;
        cellLowOf = cellA;
        if (IsBuilding(zoneA) != IsBuilding(zoneB))
        {
            int inC = IsBuilding(zoneA) ? cellA : cellB, outC = IsBuilding(zoneA) ? cellB : cellA;
            // the outside colour is what the generated wall shows on the outside face
            facade = Facade(horizontal, ex, ez, type, off, inC, outC, h, IsBuilding(zoneA) ? colB : colA);
        }
        if (type == GenData.E_WALL)
        {
            WallPiece(horizontal, ex, ez, lo, hi, 0f, h, colA, colB, true, loA, loB, hiA, hiB);
            BigDoor(horizontal, ex, ez, cellA, cellB);
            LogCourses(horizontal, ex, ez, cellA, cellB, h);
            ThemeTrim(horizontal, ex, ez, cellA, cellB, h);
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
            if (facade) { w0 = fW0; w1 = fW1; sill = fSill; head = fHead; }   // the hole of the facade's window
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
            if (theme == GenData.THEME_MEDIEVAL)
            {
                // leaded lights: a transom either side of the mullion
                float m = (w0 + w1) / 2, ty = (sill + head) / 2;
                Trim(horizontal, ex, ez, w0 + 2 * e, m - 0.02f - e, ty - 0.015f, ty + 0.015f, windowFrame);
                Trim(horizontal, ex, ez, m + 0.02f + e, w1 - 2 * e, ty - 0.015f, ty + 0.015f, windowFrame);
            }
            if (horizontal) Collider(new Vector3(ex + lo, 0, ez - WALL_HALF), new Vector3(ex + hi, h, ez + WALL_HALF));
            else Collider(new Vector3(ex - WALL_HALF, 0, ez + lo), new Vector3(ex + WALL_HALF, h, ez + hi));
        }
    }

    int capStyle;   // the scheme of the building whose wall Edge is drawing (CapColour's outside colour)

    // more-places (revision 9): a garage bay's roller door on its wall to the forecourt, a barn's double doors on its
    // wall to the farmyard, drawn on the outside face 3 cm proud of it; the people's door is the plan's own beside it
    void BigDoor(bool horizontal, float ex, float ez, int cellA, int cellB)
    {
        for (int side = 0; side < 2; side++)
        {
            int inC = side == 0 ? cellA : cellB, outC = side == 0 ? cellB : cellA;
            if (inC < 0 || outC < 0 || !IsBuilding(cellZone[inC]) || IsBuilding(cellZone[outC])) continue;
            int rt = roomType[cellRoom[inC]], ot = roomType[cellRoom[outC]];
            bool roller = rt == GenData.R_GARAGE_BAY && ot == GenData.R_FORECOURT, barn = rt == GenData.R_BARN && ot == GenData.R_FARMYARD;
            if (!roller && !barn) continue;
            float s = side == 0 ? 1f : -1f, face = WALL_HALF * s, t = 0.03f * s;
            float a0 = 0.45f, a1 = CELL - 0.45f, top = roller ? 2.6f : 2.9f;
            Color32 panel = roller ? C(206, 210, 214) : C(150, 54, 44), rib = roller ? C(170, 176, 182) : C(236, 230, 214);
            Panel(horizontal, ex, ez, a0, a1, 0f, top, face, face + t, panel);
            if (roller)
            {
                for (float y = 0.3f; y < top - 0.05f; y += 0.25f) Panel(horizontal, ex, ez, a0, a1, y, y + 0.04f, face + t, face + t + 0.01f * s, rib);
            }
            else
            {
                float mid = (a0 + a1) / 2;
                float[] xs = { a0, mid - 0.04f, mid + 0.04f, a1 - 0.08f };
                for (int k = 0; k < xs.Length; k++)
                {
                    Panel(horizontal, ex, ez, xs[k], xs[k] + 0.08f, 0.08f, top / 2 - 0.05f, face + t, face + t + 0.015f * s, rib);
                    Panel(horizontal, ex, ez, xs[k], xs[k] + 0.08f, top / 2 + 0.05f, top - 0.08f, face + t, face + t + 0.015f * s, rib);
                }
                Panel(horizontal, ex, ez, a0, a1, top - 0.08f, top, face + t, face + t + 0.015f * s, rib);
                Panel(horizontal, ex, ez, a0, a1, 0f, 0.08f, face + t, face + t + 0.015f * s, rib);
                Panel(horizontal, ex, ez, a0, a1, top / 2 - 0.05f, top / 2 + 0.05f, face + t, face + t + 0.015f * s, rib);
            }
        }
    }

    // more-places (revision 12): a log cabin's outside walls show their logs, a course every 0.32 m
    void LogCourses(bool horizontal, float ex, float ez, int cellA, int cellB, float h)
    {
        for (int side = 0; side < 2; side++)
        {
            int inC = side == 0 ? cellA : cellB, outC = side == 0 ? cellB : cellA;
            if (inC < 0 || cellZone[inC] != GenData.Z_CABIN || (outC >= 0 && IsBuilding(cellZone[outC]))) continue;
            float s = side == 0 ? 1f : -1f, face = WALL_HALF * s;
            Color32 log = C(112, 76, 48);
            for (float y = 0.16f; y < h - 0.1f; y += 0.32f) Panel(horizontal, ex, ez, 0.05f, CELL - 0.05f, y, y + 0.09f, face, face + 0.025f * s, log);
        }
    }

    // more-places (revision 13): the themes' wall finish on a plain wall, inside and out (not the log cabin's
    // outside): a medieval village's half-timbering, a space base's panel seams and dark skirting
    void ThemeTrim(bool horizontal, float ex, float ez, int cellA, int cellB, float h)
    {
        if (theme == GenData.THEME_NONE) return;
        for (int side = 0; side < 2; side++)
        {
            int toward = side == 0 ? cellB : cellA, other = side == 0 ? cellA : cellB;
            bool tb = toward >= 0 && IsBuilding(cellZone[toward]), ob = other >= 0 && IsBuilding(cellZone[other]);
            if (!tb && !ob) continue;
            if (!tb && cellZone[other] == GenData.Z_CABIN) continue;
            if (!tb && toward >= 0 && other >= 0)
            {
                int rt = roomType[cellRoom[other]], ot = roomType[cellRoom[toward]];
                if ((rt == GenData.R_GARAGE_BAY && ot == GenData.R_FORECOURT) || (rt == GenData.R_BARN && ot == GenData.R_FARMYARD)) continue;
            }
            float sgn = side == 0 ? 1f : -1f, face = WALL_HALF * sgn, e = 0.004f;
            if (theme == GenData.THEME_MEDIEVAL)
            {
                Color32 tc = C(84, 58, 38);
                float d = face + 0.03f * sgn, a0 = 0.17f, a1 = CELL - 0.17f, post = 0.16f;
                // outside: a rubble-stone base, standing out past the timbers
                if (!tb) Panel(horizontal, ex, ez, 0.02f, CELL - 0.02f, 0f, 0.75f, face, face + 0.05f * sgn, Vary(C(146, 142, 132), (int)ex, (int)ez, 8));
                Panel(horizontal, ex, ez, a0, a1, 0.02f, 0.18f, face, d, tc);
                Panel(horizontal, ex, ez, a0, a1, h - 0.26f, h - 0.1f, face, d, tc);
                Panel(horizontal, ex, ez, a0, a0 + post, 0.18f + e, h - 0.26f - e, face, d, tc);
                Panel(horizontal, ex, ez, a1 - post, a1, 0.18f + e, h - 0.26f - e, face, d, tc);
                Panel(horizontal, ex, ez, CELL / 2 - post / 2, CELL / 2 + post / 2, 0.18f + e, h - 0.26f - e, face, d, tc);
                float my = h * 0.48f;
                Panel(horizontal, ex, ez, a0 + post + e, CELL / 2 - post / 2 - e, my, my + 0.12f, face, d, tc);
                Panel(horizontal, ex, ez, CELL / 2 + post / 2 + e, a1 - post - e, my, my + 0.12f, face, d, tc);
            }
            else
            {
                Color32 seam = C(150, 158, 170), skirt = C(70, 76, 88);
                float d = face + 0.012f * sgn;
                Panel(horizontal, ex, ez, 0.12f, CELL - 0.12f, 0.02f, 0.14f, face, face + 0.02f * sgn, skirt);
                if (!tb)
                {
                    // outside: an orange hazard stripe and a lit band under the eaves
                    Panel(horizontal, ex, ez, 0.02f, CELL - 0.02f, h - 0.75f, h - 0.55f, face, face + 0.025f * sgn, C(224, 122, 40));
                    Panel(horizontal, ex, ez, 0.3f, CELL - 0.3f, h - 0.3f, h - 0.22f, face, face + 0.025f * sgn, C(90, 210, 230));
                }
                else for (int k = 1; k < 4; k++) Panel(horizontal, ex, ez, k - 0.015f, k + 0.015f, 0.14f + e, h - 0.06f, face, d, seam);
            }
        }
    }

    // a flat box on a wall's face: a0..a1 along the edge, y0..y1 up, n0..n1 off the wall line (no face toward the wall)
    void Panel(bool horizontal, float ex, float ez, float a0, float a1, float y0, float y1, float n0, float n1, Color32 col)
    {
        float lo = Mathf.Min(n0, n1), hi = Mathf.Max(n0, n1);
        bool outPlus = n1 > n0;
        if (horizontal) Box(B_WALLS, new Vector3(ex + a0, y0, ez + lo), new Vector3(ex + a1, y1, ez + hi), col, FACE_ALL & ~(outPlus ? FACE_ZN : FACE_ZP));
        else Box(B_WALLS, new Vector3(ex + lo, y0, ez + a0), new Vector3(ex + hi, y1, ez + a1), col, FACE_ALL & ~(outPlus ? FACE_XN : FACE_XP));
    }

    void Fence(bool horizontal, float ex, float ez)
    {
        GameObject go = Instantiate(theme == GenData.THEME_SPACE && railPrefab != null ? railPrefab : fencePrefab);
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
            if (roomType[r] != GenData.R_FORECOURT || !HasPump(r)) continue;
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

    // a forecourt with a fuel pump (a garage's, now and then): the canopy goes over it
    bool HasPump(int r)
    {
        int pump = DefIndex("gen_fuelPump");
        if (pump < 0) return false;
        for (int e = 0; e < emitDef.Length && e < gen.nEmit; e++)
        {
            if (emitDef[e] != pump) continue;
            int cx = emitX[e] / 4000, cz = emitZ[e] / 4000;
            if (InGrid(cx, cz) && cellRoom[cz * W + cx] == r) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ the themes' dressing (Cor: make them distinct)
    GameObject PlaceDecor(int k, Vector3 p, float yaw)
    {
        if (decorPrefabs == null || fixtureRoot == null || k >= decorPrefabs.Length || decorPrefabs[k] == null) return null;
        GameObject go = Instantiate(decorPrefabs[k]);
        Transform tr = go.transform;
        tr.SetParent(fixtureRoot, false);
        tr.position = p;
        tr.rotation = Quaternion.Euler(0f, yaw, 0f);
        return go;
    }

    float Rand01(uint salt) { return (Mix(gen.seed ^ salt) & 0xFFFFu) / 65535f; }

    void Dressing()
    {
        int n = instMinX == null ? 0 : instMinX.Length;
        for (int inst = 0; inst < n; inst++)
        {
            if (instMaxX[inst] < 0) continue;
            int c = -1;
            for (int q = 0; q < W * H; q++) if (cellInst[q] == inst && IsBuilding(cellZone[q])) { c = q; break; }
            if (c < 0) continue;
            if (theme == GenData.THEME_SPACE) RoofModules(inst);
            else if (theme == GenData.THEME_MEDIEVAL && (cellZone[c] == GenData.Z_HOUSE || cellZone[c] == GenData.Z_CABIN)) Chimney(inst, cellZone[c]);
        }
        if (theme == GenData.THEME_SPACE) SpacePlain();
        else if (theme == GenData.THEME_MEDIEVAL) MedievalSquare();
    }

    // roof modules: one to three per building, on cells of its own roof
    void RoofModules(int inst)
    {
        int placed = 0, want = 1 + (int)UModB(Mix(gen.seed ^ U32(inst * 131 + 7)), 3u);
        for (int z = instMinZ[inst]; z <= instMaxZ[inst] && placed < want; z++)
            for (int x = instMinX[inst]; x <= instMaxX[inst] && placed < want; x++)
            {
                int c = z * W + x;
                if (cellInst[c] != inst || !IsBuilding(cellZone[c])) continue;
                if (UModB(Mix(gen.seed ^ U32(c * 977 + 3)), 3u) != 0) continue;
                float t = zoneWallMm[cellZone[c]] / 1000f + 0.14f;
                int kind = placed == 0 ? DECOR_SOLAR : (placed == 1 ? DECOR_DISH : DECOR_CARGO);
                PlaceDecor(kind, new Vector3((x + 0.5f) * CELL, t, (z + 0.5f) * CELL), 90f * (int)UModB(Mix(gen.seed ^ U32(c)), 4u));
                placed++;
            }
    }

    // craters, meteors and crystals out on the plain beyond a space base's fence
    void SpacePlain()
    {
        float w = W * CELL, h = H * CELL;
        for (int k = 0; k < 26; k++)
        {
            float a = Rand01(U32(k * 2 + 1001)), b = Rand01(U32(k * 2 + 1002));
            float px, pz, out1 = 4f + 36f * b;
            int edge = (int)(a * 4f) & 3;
            float along = Rand01(U32(k + 2003));
            if (edge == 0) { px = -out1; pz = along * (h + 40f) - 20f; }
            else if (edge == 1) { px = w + out1; pz = along * (h + 40f) - 20f; }
            else if (edge == 2) { pz = -out1; px = along * (w + 40f) - 20f; }
            else { pz = h + out1; px = along * (w + 40f) - 20f; }
            int kind = k % 4 == 0 ? DECOR_CRATER : (k % 4 == 1 ? DECOR_CRATER_S : (k % 4 == 2 ? DECOR_METEOR : DECOR_CRYSTAL));
            PlaceDecor(kind, new Vector3(px, kind <= DECOR_CRATER_S ? -0.05f : 0f, pz), Rand01(U32(k + 3001)) * 360f);
        }
    }

    // a medieval village's square: a lane cell with lane all round it is the square's middle
    void MedievalSquare()
    {
        for (int c = 0; c < W * H; c++)
        {
            if (!IsLaneCell(c % W, c / W)) continue;
            bool middle = true;
            for (int dz = -1; dz <= 1 && middle; dz++) for (int dx = -1; dx <= 1 && middle; dx++) if (!IsLaneCell(c % W + dx, c / W + dz)) middle = false;
            if (!middle) continue;
            float cx = (c % W + 0.5f) * CELL, cz = (c / W + 0.5f) * CELL;
            PlaceDecor(Rand01(41u) < 0.6f ? DECOR_FOUNTAIN : DECOR_WELL, new Vector3(cx, 0f, cz), 0f);
            PlaceDecor(DECOR_STALL_R, new Vector3(cx - 4.6f, 0f, cz + 4.6f), 135f);
            PlaceDecor(DECOR_STALL_G, new Vector3(cx + 4.6f, 0f, cz - 4.6f), -45f);
            PlaceDecor(DECOR_BANNER, new Vector3(cx + 5.2f, 0f, cz + 5.2f), 45f);
            return;
        }
    }

    // a stone chimney at a gable end, above the ridge
    void Chimney(int inst, int zone)
    {
        float bx0 = instMinX[inst] * CELL, bx1 = (instMaxX[inst] + 1) * CELL, bz0 = instMinZ[inst] * CELL, bz1 = (instMaxZ[inst] + 1) * CELL;
        bool alongX = (bx1 - bx0) >= (bz1 - bz0);
        float t = zoneWallMm[zone] / 1000f + 0.14f;
        float half = (alongX ? bz1 - bz0 : bx1 - bx0) * 0.5f, rise = Mathf.Min(half, 3.6f);
        float cx = alongX ? bx0 + 1.2f : (bx0 + bx1) * 0.5f + 0.6f, cz = alongX ? (bz0 + bz1) * 0.5f + 0.6f : bz0 + 1.2f;
        int cc = (int)(cz / CELL) * W + (int)(cx / CELL);
        if (cc < 0 || cc >= W * H || cellInst[cc] != inst) return;
        Box(B_ROOFS, new Vector3(cx - 0.35f, t, cz - 0.35f), new Vector3(cx + 0.35f, t + rise + 0.9f, cz + 0.35f), C(128, 122, 114), FACE_ALL & ~FACE_YN);
        Box(B_ROOFS, new Vector3(cx - 0.42f, t + rise + 0.9f, cz - 0.42f), new Vector3(cx + 0.42f, t + rise + 1.05f, cz + 0.42f), C(100, 96, 90), FACE_ALL);
    }

    bool IsLaneCell(int x, int z)
    {
        if (!InGrid(x, z)) return false;
        int r = cellRoom[z * W + x];
        return r >= 0 && r < roomType.Length && roomType[r] == GenData.R_LANE;
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

    // a fixture model at p, turned yaw degrees; its light point joins the list. False when there is no model.
    bool PlaceFixture(int kind, Vector3 p, float yaw, int lightKind)
    {
        if (fixturePrefabs == null || fixtureRoot == null) return false;
        int k = kind * 3 + (theme >= 0 && theme < 3 ? theme : 0);
        if (k >= fixturePrefabs.Length || fixturePrefabs[k] == null) return false;
        GameObject go = Instantiate(fixturePrefabs[k]);
        Transform t = go.transform;
        t.SetParent(fixtureRoot, false);
        t.position = p;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        Transform lp = t.Find("LightPoint");
        AddLight(lp != null ? lp.position : p, lightKind, null);
        return true;
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
        float yaw = horizontal ? (side > 0 ? 0f : 180f) : (side > 0 ? 90f : -90f);
        float fd = WALL_HALF + facadeDepth;
        Vector3 at = horizontal ? new Vector3(ex + a, doorTop - 0.05f, ez + side * fd) : new Vector3(ex + side * fd, doorTop - 0.05f, ez + a);
        if (PlaceFixture(1, at, yaw, LK_PORCH)) return;
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
            if (PlaceFixture(0, new Vector3(cx, h, cz), 0f, LK_CEILING)) continue;
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
            Collider(mn, mx);
            sx[ns] = x; sz[ns] = z; ns++;
            // the model's arm reaches along its +z: turn it over the lane
            if (PlaceFixture(2, new Vector3(px, 0f, pz), Mathf.Atan2(-dx, -dz) * Mathf.Rad2Deg, LK_STREET)) continue;
            Box(B_WALLS, mn, mx, pole, FACE_ALL & ~FACE_YN);
            Box(B_WALLS, new Vector3(px - 0.12f, 0f, pz - 0.12f), new Vector3(px + 0.12f, 0.3f, pz + 0.12f), pole, FACE_ALL & ~FACE_YN);
            // the arm reaches over the lane; the head hangs at its end with its glass underneath
            float hx = px - dx * 1.0f, hz = pz - dz * 1.0f;
            Box(B_WALLS, new Vector3(Mathf.Min(px, hx) - 0.04f, 4.42f, Mathf.Min(pz, hz) - 0.04f), new Vector3(Mathf.Max(px, hx) + 0.04f, 4.5f, Mathf.Max(pz, hz) + 0.04f), pole, FACE_ALL);
            Box(B_WALLS, new Vector3(hx - 0.2f, 4.3f, hz - 0.2f), new Vector3(hx + 0.2f, 4.44f, hz + 0.2f), pole, FACE_ALL);
            Box(B_WALLS, new Vector3(hx - 0.16f, 4.27f, hz - 0.16f), new Vector3(hx + 0.16f, 4.3f, hz + 0.16f), glow, FACE_ALL & ~FACE_YP);
            AddLight(new Vector3(hx, 4.1f, hz), LK_STREET, null);
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
        float rise = theme == GenData.THEME_MEDIEVAL ? Mathf.Min(half * 1.0f, 3.6f) : Mathf.Min(half * PITCH, MAX_RISE);   // steep thatch
        return t + rise * Mathf.Clamp01(1f - Mathf.Abs(b - mid) / half);
    }

    // a point from (along the ridge, height, across it)
    Vector3 RP(bool alongX, float a, float y, float b) { return alongX ? new Vector3(a, y, b) : new Vector3(b, y, a); }

    // Pitched roofs (Cor: there were holes where an L's roof met itself). A building's footprint is cut into
    // rectangles, the largest first; each gets its own gabled roof along its longer side, gable ends closed. Where two
    // meet they cross like real cross-gables, and every roof is closed on all sides.
    void PitchedRoofs()
    {
        int n = instMinX == null ? 0 : instMinX.Length;
        for (int inst = 0; inst < n; inst++)
        {
            if (instMaxX[inst] < 0) continue;
            int x0 = instMinX[inst], x1 = instMaxX[inst], z0 = instMinZ[inst], z1 = instMaxZ[inst];
            int first = -1;
            for (int z = z0; z <= z1 && first < 0; z++) for (int x = x0; x <= x1; x++) if (cellInst[z * W + x] == inst) { first = z * W + x; break; }
            if (first < 0 || !IsBuilding(cellZone[first])) continue;
            int zone = cellZone[first];
            if (theme == GenData.THEME_SPACE) continue;   // flat roofs
            if (!(zone == GenData.Z_HOUSE || zone == GenData.Z_CABIN || zone == GenData.Z_BARN || zone == GenData.Z_CHAPEL || theme == GenData.THEME_MEDIEVAL)) continue;
            float t = zoneWallMm[zone] / 1000f + 0.14f;
            int st = Style(inst);
            Color32 roof = RoofColour(zone, st), wall = ExteriorColour(zone, st);
            int bw = x1 - x0 + 1, bd = z1 - z0 + 1;
            bool[] done = new bool[bw * bd];
            for (int guard = 0; guard < 16; guard++)
            {
                // the largest rectangle of this building's cells not yet roofed
                int best = 0, rx = 0, rz = 0, rw = 0, rd = 0;
                for (int sz = 0; sz < bd; sz++)
                    for (int sx = 0; sx < bw; sx++)
                    {
                        if (done[sz * bw + sx] || cellInst[(z0 + sz) * W + x0 + sx] != inst) continue;
                        int maxW = bw - sx;
                        for (int d = 1; sz + d - 1 < bd; d++)
                        {
                            int row = 0;
                            while (row < maxW && !done[(sz + d - 1) * bw + sx + row] && cellInst[(z0 + sz + d - 1) * W + x0 + sx + row] == inst) row++;
                            if (row == 0) break;
                            if (row < maxW) maxW = row;
                            if (maxW * d > best) { best = maxW * d; rx = sx; rz = sz; rw = maxW; rd = d; }
                        }
                    }
                if (best == 0) break;
                for (int dz = 0; dz < rd; dz++) for (int dx = 0; dx < rw; dx++) done[(rz + dz) * bw + rx + dx] = true;
                GableRoof((x0 + rx) * CELL - WALL_HALF, (x0 + rx + rw) * CELL + WALL_HALF, (z0 + rz) * CELL - WALL_HALF, (z0 + rz + rd) * CELL + WALL_HALF, t, roof, wall);
            }
        }
    }

    void GableRoof(float bx0, float bx1, float bz0, float bz1, float t, Color32 roof, Color32 wall)
    {
        bool alongX = (bx1 - bx0) >= (bz1 - bz0);
        float a0 = alongX ? bx0 : bz0, a1 = alongX ? bx1 : bz1, c0 = alongX ? bz0 : bx0, c1 = alongX ? bz1 : bx1;
        float mid = (c0 + c1) * 0.5f, top = RoofY(mid, c0, c1, t);
        // the two slopes
        QuadBoth(RP(alongX, a0, t, c0), RP(alongX, a0, top, mid), RP(alongX, a1, top, mid), RP(alongX, a1, t, c0), roof);
        QuadBoth(RP(alongX, a0, top, mid), RP(alongX, a0, t, c1), RP(alongX, a1, t, c1), RP(alongX, a1, top, mid), roof);
        // the gable ends
        QuadBoth(RP(alongX, a0, t, c0), RP(alongX, a0, top, mid), RP(alongX, a0, top, mid), RP(alongX, a0, t, c1), wall);
        QuadBoth(RP(alongX, a1, t, c0), RP(alongX, a1, top, mid), RP(alongX, a1, top, mid), RP(alongX, a1, t, c1), wall);
    }

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
        int zn = cellZone[z * W + x], st = Style(inst);
        Color32 roof = RoofColour(zn, st), wall = ExteriorColour(zn, st);
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
            Color32 rc = RoofColour(zone, Style(cellInst[c])), ceiling = C(236, 234, 226);
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
            // houses get a pitched roof on top of the flat one (the flat slab stays: the ceiling and the collider);
            // a space base keeps its roofs flat, with an orange parapet round the edge
            if (theme == GenData.THEME_SPACE)
            {
                Color32 rim = C(224, 122, 40);
                float rx0 = x * CELL - WALL_HALF, rx1 = (x + 1) * CELL + WALL_HALF, rz0 = z * CELL - WALL_HALF, rz1 = (z + 1) * CELL + WALL_HALF;
                if (!bw) Box(B_ROOFS, new Vector3(rx0, t, rz0), new Vector3(rx0 + 0.15f, t + 0.32f, rz1), rim, FACE_ALL & ~FACE_YN);
                if (!be) Box(B_ROOFS, new Vector3(rx1 - 0.15f, t, rz0), new Vector3(rx1, t + 0.32f, rz1), rim, FACE_ALL & ~FACE_YN);
                if (!bs) Box(B_ROOFS, new Vector3(rx0, t, rz0), new Vector3(rx1, t + 0.32f, rz0 + 0.15f), rim, FACE_ALL & ~FACE_YN);
                if (!bn2) Box(B_ROOFS, new Vector3(rx0, t, rz1 - 0.15f), new Vector3(rx1, t + 0.32f, rz1), rim, FACE_ALL & ~FACE_YN);
            }
            // pitched roofs: PitchedRoofs, once per building after the walls
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
