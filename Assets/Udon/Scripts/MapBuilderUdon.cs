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
    // the sky (Cor: fill the sky): hills and mountains round the map (rocky ridges on a space base), drifting clouds, the
    // stars and the moon at night, a planet over a space base (PropPrefabBaker.SkyNames, same order)
    public GameObject[] skyPrefabs;
    public GameObject starDomePrefab, moonPrefab;
    public Transform lobby;   // the lobby out past the ring: the ring leaves a gap facing it so the map can see it (Cor)
    const int SKY_HILL_A = 0, SKY_HILL_B = 1, SKY_MOUNT_A = 2, SKY_MOUNT_B = 3, SKY_MOUNT_C = 4, SKY_CLOUD_BIG = 5, SKY_CLOUD_SMALL = 6, SKY_ROCK_A = 7, SKY_ROCK_B = 8;
    Transform[] clouds;
    int nClouds;
    Vector3 skyCentre;
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
                bv[b][nv[b]] = lv + org + a * (p.x * w) + Vector3.up * (p.y * h) + o * p.z;
                bn[b][nv[b]] = a * nn.x + Vector3.up * nn.y + o * nn.z;
                Color32 col = c[vi];
                float cl = Mathf.Sqrt((float)col.r * col.r + (float)col.g * col.g + (float)col.b * col.b) + 0.001f;
                bool bodyShade = (col.r * pbr + col.g * pbg + col.b * pbb) / cl > 0.998f;
                if ((theme == GenData.THEME_NONE || partLimewash) && bodyShade)   // the space kit keeps its own colours
                {
                    float s = cl / pbl;
                    bc[b][nv[b]] = new Color32((byte)Mathf.Min(255, (int)(body.r * s)), (byte)Mathf.Min(255, (int)(body.g * s)), (byte)Mathf.Min(255, (int)(body.b * s)), 255);
                }
                // a medieval house: limewash between dark timbers, as inside (review 6); the kit's pale strips become the timbers
                else if (partLimewash && col.r + col.g + col.b > 420) bc[b][nv[b]] = new Color32(84, 58, 38, 255);
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
        int li = cellInst[cellIn];
        if (li >= 0 && li < instLevelB.Length && instLevelB[li] < 0) return false;   // nobody sees a basement's outside
        if (cellOut >= 0 && cellRoom[cellOut] >= 0)
        {
            int rt = roomType[cellRoom[cellIn]], ot = roomType[cellRoom[cellOut]];
            if ((rt == GenData.R_GARAGE_BAY && ot == GenData.R_FORECOURT) || (rt == GenData.R_BARN && ot == GenData.R_FARMYARD)) return false;   // the big doors
        }
        int k0 = (theme >= 0 && theme < 3 ? theme : 0) * 3;
        if (!HasFacade(k0)) return false;
        partLimewash = theme == GenData.THEME_MEDIEVAL && zone != GenData.Z_CHAPEL && zone != GenData.Z_WAREHOUSE;   // stone stays stone
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
        else if (type == GenData.E_DOOR)
        {
            // a door, in every theme: the wall's own square doorway in its timber frame (Edge draws the frame out to the
            // facade's face); the kit's arched door stood in front of it as a second doorway (Cor: layered)
            float d0 = off * INNER, d1 = (off + GenData.DOOR_CELLS) * INNER, dh = GenData.DOOR_HEIGHT_MM / 1000f;
            if (d0 > 0.01f) AppendPart(k0, horizontal, ex, ez, sgn, 0f, d0, 0f, h, body, 0f, 0f, 0f, 0f);
            if (dh < h - 0.01f) AppendPart(k0, horizontal, ex, ez, sgn, d0, d1, dh, h, body, 0f, 0f, 0f, 0f);
            if (d1 < CELL - 0.01f) AppendPart(k0, horizontal, ex, ez, sgn, d1, CELL, 0f, h, body, 0f, 0f, 0f, 0f);
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
    int cellLowOf;
    bool partLimewash;   // Facade: a medieval building's parts take limewash and timber   // Edge's low-side cell (the facade on a building on that side faces +)
    const int DECOR_SOLAR = 0, DECOR_CARGO = 1, DECOR_DISH = 2, DECOR_CRATER = 3, DECOR_CRATER_S = 4, DECOR_METEOR = 5, DECOR_CRYSTAL = 6,
        DECOR_FOUNTAIN = 7, DECOR_WELL = 8, DECOR_STALL_R = 9, DECOR_STALL_G = 10, DECOR_BANNER = 11, DECOR_HEDGE = 12;
    public Transform fixtureRoot;
    public Vector3 fenceModelSize = Vector3.one;
    public Transform propRoot, fenceRoot;
    public MeshFilter groundFilter, wallsFilter, roofsFilter;
    public MeshFilter sceneryFilter;        // Cor (batching): the static scenery merged into one mesh after the build
    public MeshCollider collisionCollider;
    public float budgetMs = 6f;
    public Light sun;                       // the scene's directional light: the map's mood turns and tints it

    // ---- the map's mood (Cor: maps should not all look alike), from its seed like the palette: a time of day
    // (morning, noon, afternoon, evening, overcast) for the sun, the ambient light and the fog, and a season
    // (summer, spring, autumn, winter) for the ground. PHSun bakes the shadows from the sun after the build, so
    // it follows. The lobby shares the sun.
    [HideInInspector] public int moodTime, moodSeason;
    public readonly string[] moodTimeName = { "Morning", "Noon", "Afternoon", "Evening", "Overcast", "Night" };
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

    [System.NonSerialized] public int stage, cursor;   // the build's stage and where it is in it (public: the profiler names them)
    System.Diagnostics.Stopwatch watch;
    // for the profiler (PHProfiler): steps from this length are logged; the last build's longest step and its time per stage
    public float slowStepMs = 20f;
    [System.NonSerialized] public double worstStepMs;
    [System.NonSerialized] public int worstStage, worstCursor;
    [System.NonSerialized] public double[] stageMs = new double[32];

    const float CELL = 4f, INNER = 0.5f, WALL_HALF = 0.1f;
    const int FACE_XN = 1, FACE_XP = 2, FACE_YN = 4, FACE_YP = 8, FACE_ZN = 16, FACE_ZP = 32, FACE_ALL = 63;
    const int B_GROUND = 0, B_WALLS = 1, B_ROOFS = 2, B_COLL = 3, B_SCENERY = 4;

    // the generator's arrays, cached once per build (reading another behaviour's field is slow)
    int W, H, IW, IH;
    // generator 18 (Cor: upper floors and basements): each level lies beside the map in the generator's grid and is drawn
    // where it belongs, lifted or sunk over its ground floor. lv is the offset of whatever is being drawn (Quad4,
    // AppendPart, PlaceFixture, PlaceDecor and AddLight add it); siteW x siteH is the map without the levels.
    int siteW, siteH;
    Vector3 lv;
    Vector3[] instOff;
    int[] instLevelB, instBaseB;
    bool[] instHasUpper;
    int[] stairLowB, stairHighB, stairRunB, stairSideB;
    int nStairsB;
    int[] cellZone, cellRoom, roomType, tags, innerRoom, emitDef, emitX, emitY, emitZ, emitYaw, emitParent, defFlags, zoneWallMm;
    byte[] groundKind;

    // ------------------------------------------------------------------ mesh buffers (MeshBuf without a class)
    Vector3[][] bv;
    Vector3[][] bn;
    Color32[][] bc;
    int[][] bt;
    int[] nv, nt;

    void BufInit(int b, int quads)
    {
        if (bv == null || bv.Length < 5) { bv = new Vector3[5][]; bn = new Vector3[5][]; bc = new Color32[5][]; bt = new int[5][]; nv = new int[5]; nt = new int[5]; }
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
        v[s] = a + lv; v[s + 1] = p1 + lv; v[s + 2] = p2 + lv; v[s + 3] = d + lv;
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

    Mesh collMesh;

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
        else if (moodSeason == 3) { palGrass = Blend(palGrass, C(236, 236, 234), 85); palForest = Blend(palForest, C(222, 224, 224), 75); palSoil = Blend(palSoil, C(204, 202, 200), 60); }
    }

    // a % b for uint (Udon has no uint remainder)
    uint UModB(uint a, uint b) { return a - (a / b) * b; }

    Color32 Blend(Color32 a, Color32 b, int pct)
    {
        return C(a.r + (b.r - a.r) * pct / 100, a.g + (b.g - a.g) * pct / 100, a.b + (b.b - a.b) * pct / 100);
    }

    // colour c at the brightness of colour b
    Color HueOf(Color c, Color b)
    {
        float lc = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f, lb = b.r * 0.3f + b.g * 0.59f + b.b * 0.11f;
        if (lc < 0.0001f) return b;
        float s = lb / lc;
        return new Color(c.r * s, c.g * s, c.b * s, 1f);
    }

    Color moodSun = Color.white, moodFog = Color.grey;   // ApplyMood's light and haze, for the clouds

    void ApplyMood()
    {
        float elev = 52f, intensity = 1f;
        Color sunCol = new Color(1f, 0.96f, 0.88f), sky = new Color(0.62f, 0.66f, 0.72f), eq = new Color(0.52f, 0.53f, 0.54f), gr = new Color(0.50f, 0.48f, 0.45f);
        Color fog = new Color(0.56f, 0.76f, 0.93f);
        float fogStart = 45f, fogEnd = 160f;
        float azim = 38f + (int)UModB(Mix(gen.seed ^ 0x2003u), 60u) - 30f;
        if (moodTime == 0) { elev = 22f; sunCol = new Color(1f, 0.86f, 0.70f); intensity = 0.9f; sky = new Color(0.66f, 0.66f, 0.74f); fog = new Color(0.78f, 0.80f, 0.86f); fogStart = 45f; fogEnd = 160f; azim -= 90f; }
        else if (moodTime == 1) { elev = 72f; sunCol = new Color(1f, 0.99f, 0.95f); intensity = 1.1f; sky = new Color(0.66f, 0.72f, 0.80f); fog = new Color(0.60f, 0.78f, 0.95f); fogStart = 60f; fogEnd = 200f; }
        else if (moodTime == 3) { elev = 14f; sunCol = new Color(1f, 0.80f, 0.62f); intensity = 0.85f; sky = new Color(0.46f, 0.50f, 0.66f); eq = new Color(0.58f, 0.46f, 0.44f); gr = new Color(0.40f, 0.34f, 0.34f); fog = new Color(0.80f, 0.66f, 0.62f); fogStart = 50f; fogEnd = 170f; azim += 120f; }
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
            // the time of day still shows (review 8): a brighter, bluer noon, a warm dim evening, a flat grey overcast
            if (moodTime == 1) { fog = new Color(0.30f, 0.26f, 0.40f); sky *= 1.12f; }
            else if (moodTime == 3) { fog = new Color(0.30f, 0.16f, 0.22f); sunCol = new Color(1f, 0.78f, 0.62f); }
            else if (moodTime == 4) { fog = new Color(0.26f, 0.24f, 0.28f); sunCol = new Color(0.80f, 0.80f, 0.84f); }
            else if (moodTime == 0) { fog = new Color(0.26f, 0.18f, 0.30f); sunCol = new Color(1f, 0.86f, 0.80f); }
        }
        else if (gen.mapTheme == GenData.THEME_MEDIEVAL)
        {
            fog = Color.Lerp(fog, new Color(0.88f, 0.80f, 0.64f), 0.55f);
            sunCol = Color.Lerp(sunCol, new Color(1f, 0.84f, 0.60f), 0.5f);
            sky = Color.Lerp(sky, new Color(0.74f, 0.66f, 0.52f), 0.35f);
        }
        // night (Cor): the sun is the moon, high and faint and blue; a dark sky; the lamps do the work (PHLamps)
        if (moodTime == 5)
        {
            elev = 48f; sunCol = new Color(0.62f, 0.72f, 1f); intensity = 0.16f;
            sky = sky * 0.22f + new Color(0.03f, 0.05f, 0.12f); eq = eq * 0.18f + new Color(0.02f, 0.03f, 0.07f); gr = gr * 0.15f + new Color(0.02f, 0.02f, 0.04f);
            fog = new Color(0.04f, 0.06f, 0.12f); fogStart = 25f; fogEnd = 110f;
            // a space base at night was too dark to read props against the ground (review 8)
            if (gen.mapTheme == GenData.THEME_SPACE) { sky += new Color(0.06f, 0.05f, 0.10f); eq += new Color(0.05f, 0.04f, 0.08f); gr += new Color(0.04f, 0.03f, 0.04f); }
        }
        // the sky (the procedural skybox the scene builder set): its tint, ground and brightness follow the mood
        Material skyMat = RenderSettings.skybox;
        if (skyMat != null)
        {
            // the gradient sky (PropHunt/SkyGradient, profiling: the procedural sky cost a third of the GPU frame): a
            // zenith by mood, the haze at the horizon so the land's far edge melts into it, the sun's own colour
            Color skyGround = fog * 0.92f, horizon = Color.Lerp(fog, Color.white, 0.12f);
            Color zen = new Color(0.36f, 0.54f, 0.84f);
            float sunSize = 0.035f;
            if (moodTime == 0) { zen = new Color(0.48f, 0.56f, 0.80f); horizon = Color.Lerp(fog, new Color(0.98f, 0.82f, 0.70f), 0.5f); sunSize = 0.05f; }   // dawn
            else if (moodTime == 1) zen = new Color(0.30f, 0.50f, 0.86f);
            else if (moodTime == 3) { zen = new Color(0.32f, 0.36f, 0.60f); horizon = Color.Lerp(fog, new Color(1f, 0.66f, 0.46f), 0.55f); sunSize = 0.06f; }   // dusk
            else if (moodTime == 4) { zen = new Color(0.58f, 0.60f, 0.64f); sunSize = 0f; }   // overcast: grey, no sun disc
            else if (moodTime == 5) { zen = new Color(0.02f, 0.03f, 0.08f); horizon = fog * 1.3f; sunSize = 0f; }   // night: the moon is its own object
            if (gen.mapTheme == GenData.THEME_SPACE && moodTime != 5) { zen = Color.Lerp(zen, new Color(0.26f, 0.18f, 0.34f), 0.7f); horizon = Color.Lerp(horizon, new Color(0.70f, 0.42f, 0.36f), 0.5f); }
            skyMat.SetColor("_Zenith", zen);
            skyMat.SetColor("_Horizon", horizon);
            skyMat.SetColor("_GroundColor", skyGround);
            skyMat.SetColor("_SunColor", sunCol * 1.2f);
            skyMat.SetFloat("_SunSize", sunSize);
            skyMat.SetFloat("_Exposure", 1f);
            // the shadows take the sky's colour (Cor): the ambient from above takes the hue of the sky's zenith and the
            // ambient from the sides the hue of the horizon haze; each keeps the brightness the mood set
            Color zenith = zen;
            sky = HueOf(zenith, sky);
            eq = HueOf(Color.Lerp(fog, zenith, 0.3f), eq);
            // winter: snow under a blue sky light read as an ice planet (review 4): the shade nearly neutral
            if (moodSeason == 3) { sky = Color.Lerp(sky, HueOf(new Color(1f, 0.98f, 0.95f), sky), 0.7f); eq = Color.Lerp(eq, HueOf(new Color(1f, 0.98f, 0.95f), eq), 0.6f); }
        }
        moodSun = sunCol; moodFog = fog;
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

    int Style(int inst)
    {
        if (inst < 0) return 0;
        if (instBaseB != null && inst < instBaseB.Length) inst = instBaseB[inst];   // an upper floor or a basement looks like its ground floor
        return (int)(Mix(gen.seed ^ U32(inst * 9091 + 77)) & 3u) + (gen.KayHouse(inst) ? 4 : 0);
    }
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
        moodTime = (int)UModB(Mix(gen.seed ^ 0x2001u), 6u);   // 5: night (Cor), drawn like the others
        // the ground's season follows the generator's (its trees and decorations): autumn trees, winter decorations
        // and deep-summer green come from it; an ordinary summer map is summer or spring
        theme = gen.mapTheme;
        if (gen.holiday == 1) moodSeason = 3;
        else if (gen.season == 1) moodSeason = 2;
        else if (gen.season == 2 || theme != GenData.THEME_NONE) moodSeason = 0;
        else moodSeason = (int)UModB(Mix(gen.seed ^ 0x2002u), 2u);
        InitPalette(gen.seed);
        fenceTint = new Color(0, 0, 0, 0); defTint = null; defTintDone = null;   // a new map, a new palette
        InitSchemes();
        InitParts();
        ApplyMood();
        ListBuildings();
        lv = Vector3.zero;
        cellZone = gen.cellZone; cellRoom = gen.cellRoom; roomType = gen.roomType; tags = gen.tags; innerRoom = gen.innerRoom;
        emitDef = gen.emitDef; emitX = gen.emitX; emitY = gen.emitY; emitZ = gen.emitZ; emitYaw = gen.emitYaw; emitParent = gen.emitParent;
        defFlags = gen.defFlags; zoneWallMm = gen.ZoneWallMm; groundKind = gen.ground;
        LevelOffsets();
        IndoorMap();
        objects = new GameObject[gen.nEmit];
        lightPos = new Vector3[64]; lightKind = new int[64]; lightFollow = new Transform[64]; nLights = 0;
        glow = new Color32(255, 240, 196, 0);   // alpha 0: PropHunt/Lit draws it glowing
        if (lampDefs == null) lampDefs = new int[] { DefIndex("k_lampFloor"), DefIndex("k_lampTable"), DefIndex("q_lampStanding"), DefIndex("q_lampTable") };
        BufInit(B_GROUND, IW * IH + 8);
        BufInit(B_WALLS, W * H * 40 + 64);
        BufInit(B_ROOFS, W * H * 20 + 64);
        BufInit(B_COLL, W * H * 24 + 64);
        stage = 6; cursor = 0; built = false; failed = false; busy = true; progress = 0f; buildMs = 0; worstStepMs = 0; worstStage = 0; worstCursor = 0;
        for (int i = 0; i < stageMs.Length; i++) stageMs[i] = 0;
        cellColour = new Color32[IW * IH];
        phase = "Laying floors";
    }

    // a mesh this builder made is destroyed with what showed it (a runtime mesh is not freed by itself: every round's
    // ground, walls, roofs and scenery stayed in memory)
    void DropMesh(MeshFilter f)
    {
        if (f == null) return;
        Mesh m = f.sharedMesh;
        f.sharedMesh = null;
        if (m != null) Destroy(m);
    }

    // The buildings' footprints for the shader (PropLit: a dimmer, warmer light indoors, Cor): one texel a cell of
    // the site, its red the height of the building's top there in steps of 5 cm (0: no building).
    public Texture2D indoorTex;
    Color32[] indoorPix;
    int indoorId, indoorAreaId;

    void IndoorMap()
    {
        if (indoorTex == null) return;
        if (indoorId == 0) { indoorId = VRC.SDKBase.VRCShader.PropertyToID("_UdonPHIndoor"); indoorAreaId = VRC.SDKBase.VRCShader.PropertyToID("_UdonPHIndoorArea"); }
        int tw = indoorTex.width, th = indoorTex.height;
        indoorPix = new Color32[tw * th];
        for (int c = 0; c < W * H; c++)
        {
            int zone = cellZone[c];
            if (!IsBuilding(zone)) continue;
            Vector3 o = CellOff(c);
            int sx = (c % W) + Mathf.RoundToInt(o.x / CELL), sz = (c / W) + Mathf.RoundToInt(o.z / CELL);
            if (sx < 0 || sz < 0 || sx >= tw || sz >= th) continue;
            int top = Mathf.Clamp(Mathf.RoundToInt((o.y + LevelHeight(zone)) * 20f), 1, 255);
            int k = sz * tw + sx;
            if (top > indoorPix[k].r) indoorPix[k] = new Color32((byte)top, 0, 0, 255);
        }
        indoorTex.SetPixels32(indoorPix);
        indoorTex.Apply(false);
        VRC.SDKBase.VRCShader.SetGlobalTexture(indoorId, indoorTex);
        VRC.SDKBase.VRCShader.SetGlobalVector(indoorAreaId, new Vector4(1f / (tw * CELL), 1f / (th * CELL), 1f, 0f));
    }

    public void Clear()
    {
        if (indoorAreaId != 0) VRC.SDKBase.VRCShader.SetGlobalVector(indoorAreaId, Vector4.zero);
        if (objects != null)
            for (int i = 0; i < objects.Length; i++) if (objects[i] != null) Destroy(objects[i]);
        objects = null;
        if (fenceRoot != null)
            for (int i = fenceRoot.childCount - 1; i >= 0; i--) Destroy(fenceRoot.GetChild(i).gameObject);
        nClouds = 0;
        if (fixtureRoot != null)
            for (int i = fixtureRoot.childCount - 1; i >= 0; i--) Destroy(fixtureRoot.GetChild(i).gameObject);
        DropMesh(groundFilter); DropMesh(wallsFilter); DropMesh(roofsFilter); DropMesh(sceneryFilter);
        if (tiles != null) for (int i = 1; i < tiles.Length; i++) if (tiles[i] != null) { DropMesh(tiles[i].GetComponent<MeshFilter>()); Destroy(tiles[i]); tiles[i] = null; }
        nTiles = 0;
        if (collisionCollider != null)
        {
            Mesh cm = collisionCollider.sharedMesh;
            collisionCollider.sharedMesh = null;
            if (cm != null) Destroy(cm);
        }
        built = false; busy = false; stage = 0;
    }

    // profiling (PHProfiler): while it runs, the time this script's updates take adds up in profMs
    [System.NonSerialized] public System.Diagnostics.Stopwatch profWatch;
    [System.NonSerialized] public double profMs;

    void Update()
    {
        if (profWatch == null) { UpdateBody(); return; }
        double p0 = profWatch.Elapsed.TotalMilliseconds;
        UpdateBody();
        profMs += profWatch.Elapsed.TotalMilliseconds - p0;
    }

    void UpdateBody()
    {
        if (built && nClouds > 0)
        {
            // the clouds drift round the map, one turn in about twenty minutes
            Quaternion q = Quaternion.Euler(0f, Time.deltaTime * 0.3f, 0f);
            for (int i = 0; i < nClouds; i++)
            {
                if (clouds[i] == null) continue;
                clouds[i].position = skyCentre + q * (clouds[i].position - skyCentre);
            }
        }
        if (!busy) return;
        watch.Reset(); watch.Start();
        bool finished = false;
        while (watch.Elapsed.TotalMilliseconds < budgetMs)
        {
            double s0 = watch.Elapsed.TotalMilliseconds;
            int st = stage, cu = cursor;
            if (StepOne()) { finished = true; break; }
            double one = watch.Elapsed.TotalMilliseconds - s0;
            if (one > worstStepMs) { worstStepMs = one; worstStage = st; worstCursor = cu; }
            stageMs[st & 31] += one;
            if (one > slowStepMs) Debug.Log("[MapBuilder] slow step: stage " + st + " cursor " + cu + " " + one.ToString("0") + " ms");
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
            case 6:
                // the floor colours, a row at a time (all at once took a frame of 160 ms: Cor, build freezes)
                if (cursor < IH)
                {
                    for (int ix = 0; ix < IW; ix++)
                    {
                        int i = cursor * IW + ix;
                        if ((tags[i] & GenData.T_FLOOR) != 0) cellColour[i] = CellColour(i, ix, cursor);
                    }
                    cursor++;
                }
                else { stage = 1; cursor = 0; }
                break;
            case 1:
                if (cursor < IH) { GroundRow(cursor); cursor++; progress = 0.2f * cursor / IH; }
                else { FinishGround(); stage = 2; cursor = 0; phase = "Raising walls"; }
                break;
            case 2:
                if (cursor < W * H) { CellWalls(cursor); cursor++; progress = 0.2f + 0.2f * cursor / (W * H); }
                else { stage = 20; cursor = 0; }
                break;
            // the rest of the architecture, a piece a step (Cor: build freezes)
            case 20: Fixtures(); stage = 21; break;
            case 21: Stairs(); Landmark(); stage = 22; break;
            case 22: wallsFilter.sharedMesh = ToMesh(B_WALLS, "Walls"); stage = 23; break;
            case 23: Canopies(); PitchedRoofs(); stage = 24; break;
            case 24: Dressing(); stage = 25; break;
            case 25: roofsFilter.sharedMesh = ToMesh(B_ROOFS, "Roofs"); stage = 26; break;
            case 26: collMesh = ToMesh(B_COLL, "ArchCollision"); stage = 27; break;
            case 27:
                collisionCollider.sharedMesh = collMesh;   // the engine prepares the collider here: a step of its own
                collMesh = null;
                stage = 3; cursor = 0; phase = "Placing props";
                break;
            case 3:
                if (cursor < objects.Length) { MakeProp(cursor); cursor++; progress = 0.4f + 0.55f * cursor / objects.Length; }
                else { stage = 5; cursor = 0; MergeBegin(); phase = "Merging scenery"; }
                break;
            case 5:
                if (mergeList != null && cursor < mergeList.Length) { MergeOne(cursor); cursor++; progress = 0.95f + 0.05f * cursor / mergeList.Length; }
                else { MergeEnd(); stage = 4; }
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
        if (theme == GenData.THEME_NONE && LaneAt(ix, iz))
        {
            bool edge = !LaneAt(ix - 1, iz) || !LaneAt(ix + 1, iz) || !LaneAt(ix, iz - 1) || !LaneAt(ix, iz + 1);
            return edge ? C(138, 116, 88) : Vary(C(160, 138, 104), ix / 2, iz / 2, 2);   // review 10: the checker was busy
        }
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
        for (int ix = 0; ix < IW; ix++)
        {
            int i = iz * IW + ix;
            if ((tags[i] & GenData.T_FLOOR) == 0) continue;
            float x = ix * INNER, z = iz * INNER;
            lv = InnerOff(i);
            Vector3 a = new Vector3(x, 0, z), b = new Vector3(x, 0, z + INNER), c = new Vector3(x + INNER, 0, z + INNER), d = new Vector3(x + INNER, 0, z);
            if (Blends(i)) Quad4(B_GROUND, a, b, c, d, Vector3.up, CornerColour(ix, iz, i), CornerColour(ix, iz + 1, i), CornerColour(ix + 1, iz + 1, i), CornerColour(ix + 1, iz, i));
            else Quad(B_GROUND, a, b, c, d, Vector3.up, cellColour[i]);
        }
        lv = Vector3.zero;
    }

    void FinishGround()
    {
        lv = Vector3.zero;
        float m = 260f, w = siteW * CELL, h = siteH * CELL;   // out past the hills round the map (SkyDressing)
        Color32 land = theme == GenData.THEME_SPACE ? C(140, 80, 62) : palGrass;   // the grass itself, so the map's edge does not show as a square (review 2)   // a red planet beyond a space base's fence
        // round the map, then under it a row of cells at a time, open over a basement's stair
        GroundPiece(-m, 0f, -m, h + m, land, -1f); GroundPiece(w, w + m, -m, h + m, land, -1f);
        GroundPiece(0f, w, -m, 0f, land, -1f); GroundPiece(0f, w, h, h + m, land, -1f);
        // over a basement the ground is a thin skin (the basement's ceiling is just under it)
        bool[] cellar = new bool[instOff.Length];
        for (int i = 0; i < instOff.Length; i++) if (instLevelB[i] < 0) cellar[instBaseB[i]] = true;
        for (int z = 0; z < siteH; z++)
        {
            int run = 0;
            for (int x = 0; x <= siteW; x++)
            {
                int c = z * W + x;
                int s = x < siteW ? StairAbove(c) : -1;
                int ci = x < siteW ? cellInst[c] : -1;
                bool thin = ci >= 0 && ci < cellar.Length && cellar[ci];
                if (x < siteW && s < 0 && !thin) continue;
                if (x > run) GroundPiece(run * CELL, x * CELL, z * CELL, (z + 1) * CELL, land, -1f);
                run = x + 1;
                if (x >= siteW) continue;
                float x0 = x * CELL, x1 = (x + 1) * CELL, z0 = z * CELL, z1 = (z + 1) * CELL;
                if (s < 0) { GroundPiece(x0, x1, z0, z1, land, -0.015f); continue; }
                float hx0, hx1, hz0, hz1;
                StairHole(s, c, out hx0, out hx1, out hz0, out hz1);
                GroundPiece(x0, x1, z0, hz0, land, -0.015f); GroundPiece(x0, x1, hz1, z1, land, -0.015f);
                GroundPiece(x0, hx0, hz0, hz1, land, -0.015f); GroundPiece(hx1, x1, hz0, hz1, land, -0.015f);
            }
        }
        Track(land);
        groundFilter.sharedMesh = ToMesh(B_GROUND, "Ground");
        cellColour = null;
    }

    // the lane carries on past the release as a track into the country, fading into the grass (review 2: a way in)
    void Track(Color32 land)
    {
        if (theme == GenData.THEME_SPACE) return;
        int d = (gen.releaseDir + 2) & 3;   // out of the map
        float cx = (gen.releaseX + 0.5f) * CELL, cz = (gen.releaseZ + 0.5f) * CELL;
        Vector3 o = new Vector3(DX[d], 0f, DZ[d]), side = new Vector3(DZ[d], 0f, -DX[d]);
        Color32 dirt = theme == GenData.THEME_MEDIEVAL ? C(146, 116, 82) : C(158, 134, 100);
        Vector3 start = new Vector3(cx, -0.02f, cz) + o * (CELL * 0.5f);
        int n = 14;
        for (int k = 0; k < n; k++)
        {
            float a0 = k * 5f, a1 = (k + 1) * 5f;
            // the track wanders a little and narrows
            float w0 = 1.6f - k * 0.05f, w1 = 1.6f - (k + 1) * 0.05f;
            float s0 = Mathf.Sin(k * 0.7f + Rand01(9001u) * 6f) * 1.2f * k / n, s1 = Mathf.Sin((k + 1) * 0.7f + Rand01(9001u) * 6f) * 1.2f * (k + 1) / n;
            Color32 c0 = Blend(dirt, land, k * 100 / n), c1 = Blend(dirt, land, (k + 1) * 100 / n);
            Vector3 p0 = start + o * a0 + side * (s0 - w0), p1 = start + o * a0 + side * (s0 + w0), p2 = start + o * a1 + side * (s1 + w1), p3 = start + o * a1 + side * (s1 - w1);
            Vector3 nrm = Vector3.Cross(p1 - p0, p3 - p0).y >= 0 ? Vector3.up : Vector3.down;
            if (nrm.y > 0) Quad4(B_GROUND, p0, p1, p2, p3, Vector3.up, c0, c0, c1, c1);
            else Quad4(B_GROUND, p3, p2, p1, p0, Vector3.up, c1, c1, c0, c0);
        }
    }

    void GroundPiece(float x0, float x1, float z0, float z1, Color32 col, float bottom)
    {
        if (x1 - x0 < 0.001f || z1 - z0 < 0.001f) return;
        if (bottom < -0.5f) Quad(B_GROUND, new Vector3(x0, -0.03f, z0), new Vector3(x0, -0.03f, z1), new Vector3(x1, -0.03f, z1), new Vector3(x1, -0.03f, z0), Vector3.up, col);
        Collider(new Vector3(x0, bottom, z0), new Vector3(x1, 0f, z1));
    }

    // ------------------------------------------------------------------ levels and stairs (generator 18)
    void LevelOffsets()
    {
        siteW = gen.siteW > 0 ? gen.siteW : W; siteH = gen.siteH > 0 ? gen.siteH : H;
        int n = gen.nInst;
        instLevelB = gen.instLevel; instBaseB = gen.instBase;
        instOff = new Vector3[n]; instHasUpper = new bool[n];
        int[] dx = gen.instDX, dz = gen.instDZ, iz = gen.instZone;
        for (int i = 0; i < n; i++)
        {
            int l = instLevelB[i];
            if (l == 0) continue;
            int b = instBaseB[i];
            float lh = LevelHeight(iz[b]);
            instOff[i] = new Vector3(-dx[i] * CELL, l > 0 ? lh + 0.01f : -(lh + 0.02f), -dz[i] * CELL);   // an upper floor 1 cm over the slab below (they flickered: review 1)
            if (l > 0) instHasUpper[b] = true;
        }
        nStairsB = gen.nStairs; stairLowB = gen.stairLow; stairHighB = gen.stairHigh; stairRunB = gen.stairRun; stairSideB = gen.stairSide;
    }

    // floor to floor: the walls and the slab over them
    float LevelHeight(int zone) { return zoneWallMm[zone] / 1000f + 0.14f; }

    Vector3 CellOff(int c)
    {
        if (c < 0 || c >= W * H) return Vector3.zero;
        int i = cellInst[c];
        return i >= 0 && i < instOff.Length ? instOff[i] : Vector3.zero;
    }

    Vector3 InnerOff(int i) { return CellOff((i / IW / 8) * W + (i % IW) / 8); }

    // the topmost level of a building carries its roof
    bool IsTop(int inst) { return inst < 0 || inst >= instOff.Length || (instLevelB[inst] >= 0 && !instHasUpper[inst]); }

    // the stair whose lower end is in cell c (the slab over it has the stair's hole), whose upper end is (a ground floor
    // over a basement: the ground has the hole), -1
    int StairBelow(int c) { for (int s = 0; s < nStairsB; s++) if (stairLowB[s] == c) return s; return -1; }
    int StairAbove(int c) { for (int s = 0; s < nStairsB; s++) if (stairHighB[s] == c) return s; return -1; }

    // stair-local (a metres across from its wall, b metres along from its entry edge) to cell-local metres
    float StairLX(int run, int side, float a, float b) { return side == 1 ? CELL - a : (side == 3 ? a : (run == 1 ? b : (run == 3 ? CELL - b : 0f))); }
    float StairLZ(int run, int side, float a, float b) { return side == 0 ? CELL - a : (side == 2 ? a : (run == 0 ? b : (run == 2 ? CELL - b : 0f))); }

    // the hole over a stair in cell c (rows 1 to 7, the 2 m beside its wall), in metres
    void StairHole(int s, int c, out float x0, out float x1, out float z0, out float z1)
    {
        int run = stairRunB[s], side = stairSideB[s];
        float ax = StairLX(run, side, 0f, 0.5f), bx = StairLX(run, side, 2f, CELL), az = StairLZ(run, side, 0f, 0.5f), bz = StairLZ(run, side, 2f, CELL);
        float cx = (c % W) * CELL, cz = (c / W) * CELL;
        x0 = cx + Mathf.Min(ax, bx); x1 = cx + Mathf.Max(ax, bx); z0 = cz + Mathf.Min(az, bz); z1 = cz + Mathf.Max(az, bz);
    }

    // a box between stair-local corners (a, b, y): the current stair's cell, at lv
    int stCell, stRun, stSide;
    Vector3 SP(float a, float b, float y) { return new Vector3((stCell % W) * CELL + StairLX(stRun, stSide, a, b), y, (stCell / W) * CELL + StairLZ(stRun, stSide, a, b)); }
    void SBox(int buf, float a0, float a1, float b0, float b1, float y0, float y1, Color32 side, Color32 top)
    {
        Vector3 p = SP(a0, b0, y0), q = SP(a1, b1, y1);
        Vector3 mn = Vector3.Min(p, q), mx = Vector3.Max(p, q);
        if (buf == B_COLL) Collider(mn, mx);
        else Box3(buf, mn, mx, side, top, side, FACE_ALL);
    }

    // a sloped walking surface for the collision mesh, both faces: across a0..a1, from (b0, y0) to (b1, y1)
    void SRamp(float a0, float a1, float b0, float y0, float b1, float y1)
    {
        Vector3 p0 = SP(a0, b0, y0), p1 = SP(a0, b1, y1), p2 = SP(a1, b1, y1), p3 = SP(a1, b0, y0);
        Vector3 n = Vector3.Cross(p1 - p0, p3 - p0).normalized;
        Color32 k = new Color32(0, 0, 0, 255);
        Quad(B_COLL, p0, p1, p2, p3, n, k);
        Quad(B_COLL, p3, p2, p1, p0, -n, k);
    }

    // Every stair: a half-turn stair in one cell (MapGenLevels). The first flight climbs 2.5 m on the room's side from
    // the entry row to a landing at the far end, the second comes back along the wall to the level above. Solid steps
    // down to the floor, a smooth ramp over them for walking (the collision mesh), rails where there is a drop.
    void Stairs()
    {
        Color32 tread = theme == GenData.THEME_SPACE ? C(150, 156, 166) : (theme == GenData.THEME_MEDIEVAL ? C(150, 140, 128) : C(156, 112, 72));
        Color32 rail = theme == GenData.THEME_SPACE ? C(224, 122, 40) : (theme == GenData.THEME_MEDIEVAL ? C(84, 58, 38) : C(92, 66, 46));
        for (int s = 0; s < nStairsB; s++)
        {
            int lo = stairLowB[s], hi = stairHighB[s];
            stRun = stairRunB[s]; stSide = stairSideB[s];
            Vector3 oLo = CellOff(lo), oHi = CellOff(hi);
            float dy = oHi.y - oLo.y, half = dy * 0.5f;
            Color32 wall = WallColourAt(lo);
            int n = Mathf.Max(4, Mathf.RoundToInt(half / 0.19f));
            float r = half / n, tr = 2.5f / n;
            // the lower level: the flights and the landing
            stCell = lo; lv = oLo;
            for (int k = 0; k < n; k++)
            {
                float b0 = 0.5f + k * tr;
                SBox(B_WALLS, 1f, 2f, b0, b0 + tr, 0f, (k + 1) * r, wall, tread);              // up on the room's side
                if (k > 0) SBox(B_COLL, 1f, 2f, b0, b0 + tr, 0f, k * r, wall, wall);
                float c1 = 3.0f - k * tr;
                SBox(B_WALLS, 0f, 1f, c1 - tr, c1, 0f, half + (k + 1) * r, wall, tread);       // and back along the wall
                SBox(B_COLL, 0f, 1f, c1 - tr, c1, 0f, half + k * r, wall, wall);
            }
            SBox(B_WALLS, 0f, 2f, 3.0f, CELL, 0f, half, wall, tread);
            SBox(B_COLL, 0f, 2f, 3.0f, CELL, 0f, half, wall, wall);
            SRamp(1f, 2f, 0.5f, 0f, 3.0f, half);
            SRamp(0f, 1f, 3.0f, half, 0.5f, dy);
            // rails: on the open side of the first flight and the landing, and between the flights
            Rail(1.97f, 2.03f, 0.5f, 3.0f, 0f, half, rail);
            Rail(1.97f, 2.03f, 3.0f, CELL, half, half, rail);
            Rail(0.97f, 1.03f, 3.0f, 0.5f, half, dy, rail);
            // the upper level: rails round the hole (not where the second flight arrives)
            stCell = hi; lv = oHi;
            Rail(1f, 2f, 0.47f, 0.53f, 0f, 0f, rail);
            Rail(1.97f, 2.03f, 0.5f, CELL, 0f, 0f, rail);
            if (gen.EdgeType(hi % W, hi / W, stSide) == GenData.E_NONE) Rail(0.0f, 0.06f, 0.5f, CELL, 0f, 0f, rail);
            if (gen.EdgeType(hi % W, hi / W, stRun) == GenData.E_NONE) Rail(0f, 2f, CELL - 0.06f, CELL, 0f, 0f, rail);
        }
        lv = Vector3.zero;
    }

    // a rail 0.95 m over a line of the stair (across a0..a1, along b0 to b1, its foot rising y0 to y1 along it): posts,
    // a handrail and a solid collider
    void Rail(float a0, float a1, float b0, float b1, float y0, float y1, Color32 col)
    {
        bool alongB = Mathf.Abs(b1 - b0) >= Mathf.Abs(a1 - a0);
        float len = alongB ? Mathf.Abs(b1 - b0) : Mathf.Abs(a1 - a0);
        int posts = Mathf.Max(2, Mathf.CeilToInt(len / 0.55f) + 1);
        float am = (a0 + a1) * 0.5f, bm = (b0 + b1) * 0.5f;
        for (int k = 0; k < posts; k++)
        {
            float t = k / (float)(posts - 1);
            float a = alongB ? am : Mathf.Lerp(a0, a1, t), b = alongB ? Mathf.Lerp(b0, b1, t) : bm, y = Mathf.Lerp(y0, y1, t);
            a = Mathf.Clamp(a, Mathf.Min(a0, a1) + 0.03f, Mathf.Max(a0, a1) - 0.03f);
            b = Mathf.Clamp(b, Mathf.Min(b0, b1) + 0.03f, Mathf.Max(b0, b1) - 0.03f);
            SBox(B_WALLS, a - 0.025f, a + 0.025f, b - 0.025f, b + 0.025f, y + 0.003f, y + 0.95f, col, col);   // its foot just off the floor's plane (ZFight: seen from the stair below)
        }
        // the handrail (sloped with the stair), and the collider in steps where the rail climbs
        if (alongB && Mathf.Abs(y1 - y0) > 0.01f)
        {
            float ra0 = am - 0.035f, ra1 = am + 0.035f, h0 = y0 + 0.9f, h1 = y1 + 0.9f;   // level with the flat rails it meets
            Vector3 p0 = SP(ra0, b0, h0), p1 = SP(ra1, b0, h0), p2 = SP(ra1, b1, h1), p3 = SP(ra0, b1, h1);
            Vector3 up = new Vector3(0f, 0.06f, 0f);
            Beam(p0, p1, p2, p3, up, col);
        }
        else SBox(B_WALLS, a0, a1, b0, b1, y0 + 0.9f, y0 + 0.96f, col, col);
        int seg = Mathf.Abs(y1 - y0) > 0.01f ? posts - 1 : 1;
        for (int k = 0; k < seg; k++)
        {
            float t0 = k / (float)seg, t1 = (k + 1) / (float)seg;
            float ya = Mathf.Lerp(y0, y1, t0), yb = Mathf.Lerp(y0, y1, t1), y = Mathf.Max(ya, yb);
            float sa0 = alongB ? a0 : Mathf.Lerp(a0, a1, t0), sa1 = alongB ? a1 : Mathf.Lerp(a0, a1, t1);
            float sb0 = alongB ? Mathf.Lerp(b0, b1, t0) : b0, sb1 = alongB ? Mathf.Lerp(b0, b1, t1) : b1;
            SBox(B_COLL, sa0, sa1, sb0, sb1, Mathf.Min(ya, yb), y + 0.96f, col, col);
        }
    }

    // a sloped bar: the bottom face p0 p1 p2 p3 and the same lifted by up, all four long faces from both sides
    void Beam(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 up, Color32 col)
    {
        WQuad(p0 + up, p1 + up, p2 + up, p3 + up, col);
        WQuad(p0, p1, p2, p3, col);
        WQuad(p0, p0 + up, p3 + up, p3, col);
        WQuad(p1, p1 + up, p2 + up, p2, col);
    }

    void WQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color32 c)
    {
        Vector3 n = Vector3.Cross(p1 - p0, p3 - p0);
        if (n.sqrMagnitude < 1e-10f) return;
        n.Normalize();
        Quad(B_WALLS, p0, p1, p2, p3, n, c);
        Quad(B_WALLS, p3, p2, p1, p0, -n, c);
    }

    // a slab from (x0, z0) to (x1, z1), y0 to y1, with the hole hx0..hx1 x hz0..hz1 left open (none when hx1 <= hx0)
    void Slab(int buf, float x0, float x1, float z0, float z1, float y0, float y1, float hx0, float hx1, float hz0, float hz1, Color32 side, Color32 top, Color32 bottom)
    {
        if (hx1 <= hx0) { SlabBox(buf, x0, x1, z0, z1, y0, y1, side, top, bottom); return; }
        SlabBox(buf, x0, x1, z0, hz0, y0, y1, side, top, bottom);
        SlabBox(buf, x0, x1, hz1, z1, y0, y1, side, top, bottom);
        SlabBox(buf, x0, hx0, Mathf.Max(z0, hz0), Mathf.Min(z1, hz1), y0, y1, side, top, bottom);
        SlabBox(buf, hx1, x1, Mathf.Max(z0, hz0), Mathf.Min(z1, hz1), y0, y1, side, top, bottom);
    }

    void SlabBox(int buf, float x0, float x1, float z0, float z1, float y0, float y1, Color32 side, Color32 top, Color32 bottom)
    {
        if (x1 - x0 < 0.001f || z1 - z0 < 0.001f) return;
        if (buf == B_COLL) Collider(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));
        else Box3(buf, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), side, top, bottom, FACE_ALL);
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
        lv = CellOff(cellA >= 0 ? cellA : cellB);   // an upper floor's or a basement's walls where they belong
        if (type == GenData.E_FENCE) { Boundary(horizontal, ex, ez, cellA, cellB); return; }
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
            if (IsBuilding(zoneA) != IsBuilding(zoneB))
            {
                float sd = IsBuilding(zoneA) ? 1f : -1f;
                if (facade)
                {
                    // the door's frame out to the facade's face: it closes the gap between the wall and the facade
                    TrimOut(horizontal, ex, ez, d0 - 0.08f, d0 + e, 0f, dh - e, frame, sd);
                    TrimOut(horizontal, ex, ez, d1 - e, d1 + 0.08f, 0f, dh - e, frame, sd);
                    TrimOut(horizontal, ex, ez, d0 - 0.08f, d1 + 0.08f, dh - e, dh + 0.08f, frame, sd);
                }
                Porch(horizontal, ex, ez, d1 + 0.3f, dh, sd);
                Awning(horizontal, ex, ez, d0, d1, dh, sd, IsBuilding(zoneA) ? cellA : cellB);
            }
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
        if (theme == GenData.THEME_NONE)
        {
            // an ordinary room: a skirting board and a thin cornice (review 7), on each indoor face
            for (int side = 0; side < 2; side++)
            {
                int toward = side == 0 ? cellB : cellA;
                if (toward < 0 || !IsBuilding(cellZone[toward])) continue;
                float sgn = side == 0 ? 1f : -1f, face = WALL_HALF * sgn;
                Color32 wc = WallColourAt(toward);
                Color32 skirt = wc.r + wc.g + wc.b > 600 ? palWoodB : C(242, 240, 234);
                Panel(horizontal, ex, ez, 0.003f, CELL - 0.003f, 0.0f, 0.12f, face, face + 0.018f * sgn, skirt);   // its ends just off the cell's edge: a wall's face can lie there (ZFight)
                Panel(horizontal, ex, ez, 0.0f, CELL, h - 0.09f, h, face, face + 0.03f * sgn, C(246, 244, 238));
            }
            return;
        }
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
        int faces = FACE_ALL & ~(y0 <= 0f ? FACE_YN : 0);   // on the floor: no underside (never seen; it lay in the slab's plane)
        if (horizontal) Box(B_WALLS, new Vector3(ex + a0, y0, ez + lo), new Vector3(ex + a1, y1, ez + hi), col, faces & ~(outPlus ? FACE_ZN : FACE_ZP));
        else Box(B_WALLS, new Vector3(ex + lo, y0, ez + a0), new Vector3(ex + hi, y1, ez + a1), col, faces & ~(outPlus ? FACE_XN : FACE_XP));
    }

    // Review 9: every lane was a fence corridor. A plot's side to the lane is chosen per plot: a shop, a diner or a
    // garage opens onto it (no fence), a house now and then too; otherwise a fence, a hedge or a low wall (stone in a
    // medieval village). Between plots: a fence or a hedge. The map's edge keeps its fence; a space base its rails.
    // what a boundary between two cells is drawn as: 0 a fence, 1 a hedge, 2 a low wall, 3 nothing (open)
    int BoundaryKind(int cellA, int cellB)
    {
        if (theme == GenData.THEME_SPACE || cellA < 0 || cellB < 0) return 0;
        int[] cp = gen.cellPlot;
        int pa = cp[cellA], pb = cp[cellB];
        bool laneA = IsLaneCell(cellA % W, cellA / W), laneB = IsLaneCell(cellB % W, cellB / W);
        int plot = laneA ? pb : (laneB ? pa : (pa >= 0 ? pa : pb));
        if (plot < 0 || (pa < 0 && pb < 0)) return 0;   // the wood, the release
        int pz = gen.plotZone[plot];
        uint hsh = Mix(gen.seed ^ U32(plot * 977 + 41));
        int kind;
        if (laneA || laneB)
        {
            if (pz == GenData.Z_SHOP || pz == GenData.Z_DINER || pz == GenData.Z_GARAGE || pz == GenData.Z_FAIR) kind = 3;
            else kind = (int)UModB(hsh, 4u);
            if (theme == GenData.THEME_MEDIEVAL && kind == 0) kind = 2;
        }
        else kind = (hsh & 8u) == 0u ? 0 : 1;
        return kind;
    }

    // is the boundary stretch at grid edge (cx, cz) a hedge? (horizontal: between cells (cx, cz - 1) and (cx, cz);
    // else between (cx - 1, cz) and (cx, cz))
    bool HedgeAt(bool horizontal, int cx, int cz)
    {
        int ax = horizontal ? cx : cx - 1, az = horizontal ? cz - 1 : cz;
        if (!InGrid(ax, az) || !InGrid(cx, cz)) return false;
        if (gen.EdgeType(cx, cz, horizontal ? 2 : 3) != GenData.E_FENCE) return false;
        return BoundaryKind(az * W + ax, cz * W + cx) == 1;
    }

    void Boundary(bool horizontal, float ex, float ez, int cellA, int cellB)
    {
        // Inside the map a fence, a hedge or a low wall stops what is as high as it is drawn: a hider's prop can hop
        // over it (Cor). The map's own edge and the hunters' release pen keep a wall 6 m high.
        bool keepIn = cellA < 0 || cellB < 0 || cellZone[cellA] == GenData.Z_RELEASE || cellZone[cellB] == GenData.Z_RELEASE;
        if (theme == GenData.THEME_SPACE || cellA < 0 || cellB < 0) { Fence(horizontal, ex, ez, keepIn ? 6f : FENCE_TOP); return; }
        int kind = BoundaryKind(cellA, cellB);   // 0 fence, 1 hedge, 2 low wall, 3 open
        if (kind == 3) return;   // open: no fence, no collider
        if (kind == 0) { Fence(horizontal, ex, ez, keepIn ? 6f : FENCE_TOP); return; }
        float top = keepIn ? 6f : (kind == 2 ? 0.7f : 0.8f);
        if (horizontal) Collider(new Vector3(ex - 0.1f, 0, ez - 0.12f), new Vector3(ex + CELL + 0.1f, top, ez + 0.12f));
        else Collider(new Vector3(ex - 0.12f, 0, ez - 0.1f), new Vector3(ex + 0.12f, top, ez + CELL + 0.1f));
        if (kind == 1 && decorPrefabs != null && DECOR_HEDGE < decorPrefabs.Length && decorPrefabs[DECOR_HEDGE] != null)
        {
            // the row's ends: on unbroken where the next stretch is hedge too; short of a building (and its facade);
            // short of whatever else begins there, a fence's post or a wall's end (Cor: the hedge ran into them)
            int gx = Mathf.RoundToInt(ex / CELL), gz = Mathf.RoundToInt(ez / CELL);
            float lo = 0f, hi = CELL;
            if (horizontal)
            {
                if (Bld(gx - 1, gz - 1) || Bld(gx - 1, gz)) lo = 0.55f; else if (!HedgeAt(true, gx - 1, gz)) lo = 0.16f;
                if (Bld(gx + 1, gz - 1) || Bld(gx + 1, gz)) hi = CELL - 0.55f; else if (!HedgeAt(true, gx + 1, gz)) hi = CELL - 0.16f;
            }
            else
            {
                if (Bld(gx - 1, gz - 1) || Bld(gx, gz - 1)) lo = 0.55f; else if (!HedgeAt(false, gx, gz - 1)) lo = 0.16f;
                if (Bld(gx - 1, gz + 1) || Bld(gx, gz + 1)) hi = CELL - 0.55f; else if (!HedgeAt(false, gx, gz + 1)) hi = CELL - 0.16f;
            }
            float run = hi - lo;
            int n = hedgeLen > 0.1f ? Mathf.Clamp(Mathf.RoundToInt(run / hedgeLen), 1, 6) : 2;
            for (int q = 0; q < n; q++)
            {
                float a = lo + run * (q + 0.5f) / n;
                // the hedge model is long along its own z: turned to run along the boundary (Cor: they stood across it)
                GameObject h = PlaceDecor(DECOR_HEDGE, horizontal ? new Vector3(ex + a, 0f, ez) : new Vector3(ex, 0f, ez + a), horizontal ? 90f : 0f);
                if (h == null) continue;
                Collider[] cs = h.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cs.Length; i++) cs[i].enabled = false;
                if (hedgeLen <= 0.1f)
                {
                    // the first hedge of all: its length along the boundary says how many fill a cell
                    MeshRenderer mr = h.GetComponentInChildren<MeshRenderer>();
                    if (mr != null) hedgeLen = horizontal ? mr.bounds.size.x : mr.bounds.size.z;
                    if (hedgeLen > 0.1f)
                    {
                        n = Mathf.Clamp(Mathf.RoundToInt(run / hedgeLen), 1, 6);
                        a = lo + run * 0.5f / n;
                        h.transform.position = (horizontal ? new Vector3(ex + a, 0f, ez) : new Vector3(ex, 0f, ez + a)) + lv;
                    }
                }
                if (hedgeLen > 0.1f)
                {
                    Vector3 hs = h.transform.localScale;
                    hs.z *= (run / n) / hedgeLen;   // end to end: an overlap put two shades in one plane (ZFight)
                    h.transform.localScale = hs;
                }
                TintHedge(h);
            }
            return;
        }
        // a low wall
        // one colour along a run: the stretches overlap at their ends, and two shades in one plane flickered (ZFight)
        Color32 wc = theme == GenData.THEME_MEDIEVAL ? C(146, 142, 132) : C(158, 96, 78);
        Color32 cap = theme == GenData.THEME_MEDIEVAL ? C(124, 120, 112) : C(196, 190, 180);
        if (horizontal)
        {
            Box(B_WALLS, new Vector3(ex - 0.12f, 0f, ez - 0.13f), new Vector3(ex + CELL + 0.12f, 0.62f, ez + 0.13f), wc, FACE_ALL & ~FACE_YN);
            Box(B_WALLS, new Vector3(ex - 0.15f, 0.62f, ez - 0.16f), new Vector3(ex + CELL + 0.15f, 0.7f, ez + 0.16f), cap, FACE_ALL);
        }
        else
        {
            Box(B_WALLS, new Vector3(ex - 0.13f, 0f, ez - 0.12f), new Vector3(ex + 0.13f, 0.62f, ez + CELL + 0.12f), wc, FACE_ALL & ~FACE_YN);
            Box(B_WALLS, new Vector3(ex - 0.16f, 0.62f, ez - 0.15f), new Vector3(ex + 0.16f, 0.7f, ez + CELL + 0.15f), cap, FACE_ALL);
        }
    }

    float hedgeLen;   // the hedge model's length along a boundary, as placed (measured on the first one)

    // a hedge in the map's greens
    void TintHedge(GameObject h)
    {
        Color avg = AverageGreen(h);
        if (avg.a <= 0f) return;
        Color32 want = Blend(palGrass, C(54, 112, 58), 50);
        Tint(h, new Color(Mathf.Clamp(want.r / 255f / Mathf.Max(0.05f, avg.r), 0.3f, 1.6f), Mathf.Clamp(want.g / 255f / Mathf.Max(0.05f, avg.g), 0.3f, 1.6f), Mathf.Clamp(want.b / 255f / Mathf.Max(0.05f, avg.b), 0.3f, 1.6f), 1f));
    }

    const float FENCE_TOP = 1.05f;   // a fence is drawn 1.1 m high; a hop of 1.15 m clears it (PHHiderBody.hopHeight)

    void Fence(bool horizontal, float ex, float ez, float top)
    {
        GameObject go = Instantiate(theme == GenData.THEME_SPACE && railPrefab != null ? railPrefab : fencePrefab);
        Transform t = go.transform;
        t.SetParent(fenceRoot, false);
        t.position = horizontal ? new Vector3(ex + CELL / 2, 0, ez) : new Vector3(ex, 0, ez + CELL / 2);
        t.rotation = Quaternion.Euler(0, horizontal ? 0 : 90, 0);
        t.localScale = new Vector3((CELL + 0.1f) / fenceModelSize.x, 1.1f / fenceModelSize.y, 0.16f / fenceModelSize.z);
        if (theme != GenData.THEME_SPACE) Tint(go, FenceTint());
        if (horizontal) Collider(new Vector3(ex - 0.1f, 0, ez - 0.08f), new Vector3(ex + CELL + 0.1f, top, ez + 0.08f));
        else Collider(new Vector3(ex - 0.08f, 0, ez - 0.1f), new Vector3(ex + 0.08f, top, ez + CELL + 0.1f));
    }

    bool Bld(int x, int z) { return InGrid(x, z) && IsBuilding(cellZone[z * W + x]); }

    // ------------------------------------------------------------------ the palette on models (review 5)
    // Broadleaf trees and plants came cyan and fences salmon from their models, whatever the map's palette. A tint
    // (PropLit's _Color) carries a model's foliage to the map's greens and the fence to its wood. Every client, and
    // a hider's own prop (PHSlot), take the same tint, so a disguise matches what it hides among.
    Color[] defTint;
    bool[] defTintDone;
    MaterialPropertyBlock tintBlock;
    Color fenceTint = new Color(0, 0, 0, 0);

    void Tint(GameObject go, Color c)
    {
        if (tintBlock == null) tintBlock = new MaterialPropertyBlock();
        tintBlock.SetColor("_Color", c);
        MeshRenderer[] rs = go.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < rs.Length; i++) rs[i].SetPropertyBlock(tintBlock);
    }

    // A hider's own prop (PHSlot) and the prop a hider points at (PHLocal) have materials of their own: PropLit with
    // _PipOnly set (drawn only in the third-person view), PropLit with _Hover set (the glow), and the shade patch's
    // with _PipOnly. Not a mark in a property block (Cor: tables went missing while the hider was a table, and
    // props vanished when pointed at): props of one model are drawn in one instanced batch, and a mark that one
    // prop's block carried reached the others in it. Another material is never in the same batch. The tint stays
    // in the block (TintProp), per instance.
    // A map's props (MakeProp) are drawn with PropLit with _Fade set: in the prop viewer's cutout they turn see-through
    // instead of being cut away (Cor), while the architecture and the merged scenery (PropLit itself) are cut.
    public Material propLitMat, selfMat, hoverMat, shadeSelfMat, fadeMat;   // WorldSceneBuilder

    public void FadeProp(GameObject go)
    {
        if (go == null || fadeMat == null) return;
        MeshRenderer[] rs = go.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < rs.Length; i++) if (rs[i].sharedMaterial == propLitMat) rs[i].sharedMaterial = fadeMat;
    }

    public void SelfOnly(GameObject go, int def)
    {
        if (go == null || selfMat == null) return;
        MeshRenderer[] rs = go.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < rs.Length; i++) rs[i].sharedMaterial = rs[i].gameObject.name == "Shade" ? shadeSelfMat : selfMat;
    }

    // The local hider's own prop in first person: the cage of its edges (shader PropHunt/Cage; Cor chose it with the
    // ground outline, PHView). One object (WorldSceneBuilder) is lent to the prop: it hangs under the prop's root, so
    // it follows its place, its turn and the doorway squash, and is given back before the prop goes. Only the local
    // hider's own prop ever has it (PHSlot), so nobody else draws anything. The shader keeps it out of the
    // third-person view. Its fade near the eye goes with the prop's size: gone within 0.2 m and full from 0.5 m for a
    // small prop, 0.35 and 0.8 m for a big one (1.5 m and more). The eye sits at 0.9 of the prop's height (PHLocal
    // .SetEye), so on a small or low prop the base is near the eye too: the fade is also kept within 0.45 and 0.9 of
    // the eye's distance to the middle of the base's nearer sides, so the whole base outline is at full strength.
    public Mesh[] cageMeshes;          // by def (PropPrefabBaker.BakeCages); null where a model has no lines
    public GameObject cage;            // WorldSceneBuilder
    public MeshFilter cageFilter;
    public MeshRenderer cageRenderer;
    Transform cageHome;
    MaterialPropertyBlock cageBlock;

    public void Cage(GameObject go, int def, Vector3 half)
    {
        if (cage == null || go == null) return;
        if (cageHome == null) cageHome = cage.transform.parent;
        Mesh m = cageMeshes != null && def >= 0 && def < cageMeshes.Length ? cageMeshes[def] : null;
        if (m == null) { Uncage(null); return; }
        cageFilter.sharedMesh = m;
        if (cageBlock == null) cageBlock = new MaterialPropertyBlock();
        float h = 2f * half.y;
        float t = Mathf.Clamp01((2f * Mathf.Max(half.x, Mathf.Max(half.y, half.z)) - 0.3f) / 1.2f);
        float eye = h > 2.5f ? 1.6f : Mathf.Clamp(Mathf.Round(0.9f * h / 0.05f) * 0.05f, 0.25f, 2.0f);   // as PHLocal.SetEye
        float side = Mathf.Min(half.x, half.z);
        float toBase = Mathf.Sqrt(eye * eye + side * side);
        cageBlock.SetFloat("_FadeNear", Mathf.Min(Mathf.Lerp(0.2f, 0.35f, t), 0.45f * toBase));
        cageBlock.SetFloat("_FadeFar", Mathf.Min(Mathf.Lerp(0.5f, 0.8f, t), 0.9f * toBase));
        cageRenderer.SetPropertyBlock(cageBlock);
        Transform ct = cage.transform;
        ct.SetParent(go.transform, false);
        ct.localPosition = Vector3.zero; ct.localRotation = Quaternion.identity; ct.localScale = Vector3.one;
        cage.SetActive(true);
    }

    // takes the cage back from go (from anywhere when go is null)
    public void Uncage(GameObject go)
    {
        if (cage == null) return;
        Transform ct = cage.transform;
        if (go != null && ct.parent != go.transform) return;
        cage.SetActive(false);
        if (cageHome != null) ct.SetParent(cageHome, false);
    }

    public void Hover(GameObject go, int def, bool on)
    {
        if (go == null || hoverMat == null) return;
        MeshRenderer[] rs = go.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            Material m = rs[i].sharedMaterial;
            Material plain = fadeMat != null ? fadeMat : propLitMat;
            if (on && (m == plain || m == propLitMat)) rs[i].sharedMaterial = hoverMat;
            else if (!on && m == hoverMat) rs[i].sharedMaterial = plain;
        }
    }

    // the average of a model's vertex colours that pass the test (green-ish: g over r), 0 alpha when none
    Color AverageGreen(GameObject go)
    {
        MeshFilter[] fs = go.GetComponentsInChildren<MeshFilter>(true);
        float r = 0, g = 0, b = 0; int n = 0, total = 0;
        for (int f = 0; f < fs.Length; f++)
        {
            Mesh m = fs[f].sharedMesh;
            if (m == null) continue;
            Color32[] cs = m.colors32;
            total += cs.Length;
            for (int i = 0; i < cs.Length; i++)
            {
                if (cs[i].g < cs[i].r + 20) continue;
                r += cs[i].r; g += cs[i].g; b += cs[i].b; n++;
            }
        }
        greenShare = total > 0 ? (float)n / total : 0f;
        if (n == 0) return new Color(0, 0, 0, 0);
        return new Color(r / n / 255f, g / n / 255f, b / n / 255f, 1f);
    }
    float greenShare;   // AverageGreen: the share of the model's vertices that passed

    public void TintProp(GameObject go, int def)
    {
        if (go == null || def < 0) return;
        if (theme == GenData.THEME_SPACE) return;   // a space base's twins keep theirs (autumn's orange trees are not green: untouched)
        if (defTint == null || defTint.Length != propPrefabs.Length) { defTint = new Color[propPrefabs.Length]; defTintDone = new bool[propPrefabs.Length]; }
        if (!defTintDone[def])
        {
            defTintDone[def] = true;
            Color avg = propPrefabs[def] != null ? AverageGreen(propPrefabs[def]) : new Color(0, 0, 0, 0);
            Color t = new Color(0, 0, 0, 0);
            // only the cyan greens: blue close to green; and only a thing that is mostly foliage (the tint is on the
            // whole prop: a tan basket with a blue towel in it turned olive)
            if (avg.a > 0f && avg.b > avg.g * 0.72f && greenShare >= 0.2f)
            {
                Color32 want = Blend(palGrass, C(54, 112, 58), 45);   // the map's green, darker and fuller than its grass
                t = new Color(Mathf.Clamp(want.r / 255f / Mathf.Max(0.05f, avg.r), 0.3f, 1.6f), Mathf.Clamp(want.g / 255f / Mathf.Max(0.05f, avg.g), 0.3f, 1.6f),
                              Mathf.Clamp(want.b / 255f / Mathf.Max(0.05f, avg.b), 0.3f, 1.6f), 0.5f);   // alpha 0.5: the leaves only (PropLit, MergeOne). The trunk took the tint too and turned orange
            }
            defTint[def] = t;
        }
        if (defTint[def].a > 0f) Tint(go, defTint[def]);
    }

    // the fence in the map's wood (dark weathered timber in a medieval village)
    Color FenceTint()
    {
        if (fenceTint.a > 0f) return fenceTint;
        if (fencePrefab == null) return Color.white;
        MeshFilter[] fs = fencePrefab.GetComponentsInChildren<MeshFilter>(true);
        float r = 0, g = 0, b = 0; int n = 0;
        for (int f = 0; f < fs.Length; f++)
        {
            if (fs[f].sharedMesh == null) continue;
            Color32[] cs = fs[f].sharedMesh.colors32;
            for (int i = 0; i < cs.Length; i++) { r += cs[i].r; g += cs[i].g; b += cs[i].b; n++; }
        }
        if (n == 0) { fenceTint = Color.white; return fenceTint; }
        Color32 want = theme == GenData.THEME_MEDIEVAL ? C(112, 86, 62) : palWoodB;
        fenceTint = new Color(Mathf.Clamp(want.r / Mathf.Max(1f, r / n), 0.2f, 1.6f), Mathf.Clamp(want.g / Mathf.Max(1f, g / n), 0.2f, 1.6f), Mathf.Clamp(want.b / Mathf.Max(1f, b / n), 0.2f, 1.6f), 1f);
        return fenceTint;
    }

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
        tr.position = p + lv;
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
            if (c < 0 || !IsTop(inst)) continue;
            lv = instOff[inst];
            if (theme == GenData.THEME_SPACE) RoofModules(inst);
            else if (theme == GenData.THEME_MEDIEVAL && (cellZone[c] == GenData.Z_HOUSE || cellZone[c] == GenData.Z_CABIN)) Chimney(inst, cellZone[c]);
            else if (theme == GenData.THEME_NONE && cellZone[c] == GenData.Z_HOUSE && (Mix(gen.seed ^ U32(instBaseB[inst] * 71 + 5)) & 1u) == 0u) Chimney(inst, cellZone[c]);   // review 3
        }
        lv = Vector3.zero;
        SkyDressing();
        Outskirts();
        if (theme == GenData.THEME_SPACE) SpacePlain();
        else if (theme == GenData.THEME_MEDIEVAL) MedievalSquare();
    }

    GameObject PlaceSky(GameObject prefab, Vector3 p, float yaw, float scale)
    {
        if (prefab == null || fixtureRoot == null) return null;
        GameObject go = Instantiate(prefab);
        Transform tr = go.transform;
        tr.SetParent(fixtureRoot, false);
        tr.position = p;
        tr.rotation = Quaternion.Euler(0f, yaw, 0f);
        tr.localScale = new Vector3(scale, scale, scale);
        return go;
    }

    // an angle round the map (radians) moved out of the gap facing the lobby, half that many radians each side
    float LobbyGap(float a, float half)
    {
        if (lobby == null) return a;
        Vector3 d = lobby.position - skyCentre;
        float la = Mathf.Atan2(d.z, d.x);
        float diff = Mathf.DeltaAngle(la * Mathf.Rad2Deg, a * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        if (Mathf.Abs(diff) >= half) return a;
        return la + (diff >= 0f ? half : -half);
    }

    void SkyDressing()
    {
        nClouds = 0;
        if (skyPrefabs == null || skyPrefabs.Length < 9) return;
        float w = siteW * CELL, h = siteH * CELL;
        skyCentre = new Vector3(w * 0.5f, 0f, h * 0.5f);
        float r0 = Mathf.Max(w, h) * 0.5f + 75f;
        bool space = theme == GenData.THEME_SPACE;
        // the ring: hills nearer, mountains behind them (a space base: big rocks)
        int ring = 16;
        for (int k = 0; k < ring; k++)
        {
            float a = LobbyGap((k + Rand01(U32(k + 5001)) * 0.6f) / ring * Mathf.PI * 2f, 0.42f);
            float rr = r0 + Rand01(U32(k + 5101)) * 18f;
            Vector3 p = skyCentre + new Vector3(Mathf.Cos(a) * rr, -0.5f, Mathf.Sin(a) * rr);
            int kind = space ? (k % 2 == 0 ? SKY_ROCK_A : SKY_ROCK_B) : (k % 2 == 0 ? SKY_HILL_A : SKY_HILL_B);
            PlaceSky(skyPrefabs[kind], p, Rand01(U32(k + 5201)) * 360f, space ? 1.3f + Rand01(U32(k + 5301)) * 0.6f : 2.2f + Rand01(U32(k + 5301)) * 1.2f);
            if (k % 2 == 0)
            {
                float a2 = LobbyGap(a + Mathf.PI / ring, 0.32f);
                float r2 = rr + 40f + Rand01(U32(k + 5401)) * 25f;
                Vector3 p2 = skyCentre + new Vector3(Mathf.Cos(a2) * r2, -1f, Mathf.Sin(a2) * r2);
                int m = space ? SKY_ROCK_A : SKY_MOUNT_A + (k / 2) % 3;
                PlaceSky(skyPrefabs[m], p2, Rand01(U32(k + 5501)) * 360f, space ? 2.6f : 1.6f + Rand01(U32(k + 5601)) * 0.8f);
            }
        }
        // clouds (none on a space base), high over and around the map
        if (!space)
        {
            if (clouds == null || clouds.Length < 22) clouds = new Transform[22];
            // the clouds take the mood's light and haze (review 4: they went purple against a warm sky)
            Color cloud = Color.Lerp(Color.Lerp(Color.white, moodSun, 0.35f), moodFog, 0.25f);
            MaterialPropertyBlock cmpb = new MaterialPropertyBlock();
            cmpb.SetColor("_Color", cloud);
            for (int k = 0; k < 22; k++)
            {
                // the first twelve high over the map, the rest low over the hills where players look
                bool low = k >= 12;
                float a = Rand01(U32(k + 6001)) * Mathf.PI * 2f, rr = low ? r0 - 10f + Rand01(U32(k + 6101)) * 40f : Rand01(U32(k + 6101)) * (r0 + 20f);
                Vector3 p = skyCentre + new Vector3(Mathf.Cos(a) * rr, (low ? 26f : 55f) + Rand01(U32(k + 6201)) * (low ? 12f : 25f), Mathf.Sin(a) * rr);
                GameObject c = PlaceSky(skyPrefabs[k % 3 == 0 ? SKY_CLOUD_BIG : SKY_CLOUD_SMALL], p, Rand01(U32(k + 6301)) * 360f, (low ? 2.2f : 1.6f) + Rand01(U32(k + 6401)) * 1.4f);
                if (c == null) continue;
                clouds[nClouds++] = c.transform;
                MeshRenderer[] rs = c.GetComponentsInChildren<MeshRenderer>();
                for (int q = 0; q < rs.Length; q++) rs[q].SetPropertyBlock(cmpb);
            }
        }
        // at night the stars and the moon; over a space base its planet, day and night
        bool night = moodTime == 5;
        if (night && starDomePrefab != null) PlaceSky(starDomePrefab, skyCentre, Rand01(7001u) * 360f, 0.85f);
        if ((night || space) && moonPrefab != null)
        {
            Vector3 dir = new Vector3(0.45f, space ? (moodTime == 1 ? 0.55f : (moodTime == 3 || moodTime == 0 ? 0.16f : 0.32f)) : 0.55f, 0.75f).normalized;   // the planet rides higher at noon
            GameObject moon = PlaceSky(moonPrefab, skyCentre + dir * 280f, 0f, space ? 48f : 16f);
            if (moon != null && space)
            {
                // review 10: the sphere read as a lumpy blob; a flat disc with bands suits the style
                MeshFilter mf = moon.GetComponent<MeshFilter>();
                if (mf != null) mf.sharedMesh = PlanetDisc();
                moon.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            }
            if (moon != null)
            {
                MeshRenderer mr = moon.GetComponent<MeshRenderer>();
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                mpb.SetColor("_Color", space ? new Color(0.55f, 0.66f, 0.85f) : new Color(0.92f, 0.93f, 0.86f));
                mr.SetPropertyBlock(mpb);
            }
        }
    }

    // The country round the map (review 2: the town sat on an empty plain): clumps of trees, bushes and boulders 5 to
    // 30 m beyond the fence, hay in a medieval village's fields, and a nearer, lower ring of hills at about 40 m.
    // Not props: no colliders, never disguises.
    GameObject PlaceScenery(string id, Vector3 p, float yaw, float scale)
    {
        int def = DefIndex(id);
        if (def < 0 || def >= propPrefabs.Length || propPrefabs[def] == null || fixtureRoot == null) return null;
        GameObject go = Instantiate(propPrefabs[def]);
        Transform t = go.transform;
        t.SetParent(fixtureRoot, false);
        t.position = p; t.rotation = Quaternion.Euler(0f, yaw, 0f); t.localScale = t.localScale * scale;
        Collider[] cs = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cs.Length; i++) cs[i].enabled = false;
        TintProp(go, def);
        return go;
    }

    void Outskirts()
    {
        if (theme == GenData.THEME_SPACE) return;   // the plain has its craters and rocks (SpacePlain)
        float w = siteW * CELL, h = siteH * CELL;
        int rd = (gen.releaseDir + 2) & 3;
        float tx = (gen.releaseX + 0.5f) * CELL, tz = (gen.releaseZ + 0.5f) * CELL;
        string[] trees = { "n_treeOak", "n_treeDefault", "n_treeFat", "n_treePineTall", "n_treePineRound" };
        string[] low = { "n_bushLarge", "n_bush", "n_rockLarge", "n_rockLargeB", "n_bushDetailed" };
        bool medieval = theme == GenData.THEME_MEDIEVAL;
        int clumps = Mathf.RoundToInt((w + h) * 2f / 11f);
        for (int k = 0; k < clumps; k++)
        {
            float along = Rand01(U32(k * 7 + 8101)), out1 = 5f + 25f * Rand01(U32(k * 7 + 8102)) * Rand01(U32(k * 7 + 8103)) + 2f;
            int edge = (int)(Rand01(U32(k * 7 + 8104)) * 3.999f);
            Vector3 c;
            if (edge == 0) c = new Vector3(-out1, 0f, along * (h + 20f) - 10f);
            else if (edge == 1) c = new Vector3(w + out1, 0f, along * (h + 20f) - 10f);
            else if (edge == 2) c = new Vector3(along * (w + 20f) - 10f, 0f, -out1);
            else c = new Vector3(along * (w + 20f) - 10f, 0f, h + out1);
            // keep the track clear
            Vector3 rel = c - new Vector3(tx, 0f, tz);
            float alongT = rel.x * DX[rd] + rel.z * DZ[rd], acrossT = Mathf.Abs(rel.x * DZ[rd] - rel.z * DX[rd]);
            if (alongT > 0f && acrossT < 7f) continue;
            // and the view of the lobby (Cor: the map can see it)
            float ca = Mathf.Atan2(c.z - skyCentre.z, c.x - skyCentre.x);
            if (LobbyGap(ca, 0.3f) != ca) continue;
            int kind = (int)(Rand01(U32(k * 7 + 8105)) * 10f);
            if (medieval && kind >= 8)
            {
                // a field of hay
                for (int q = 0; q < 3; q++)
                    PlaceScenery(q == 0 ? "g_hayBundled" : "g_hay", c + new Vector3(Rand01(U32(k * 31 + q)) * 6f - 3f, 0f, Rand01(U32(k * 37 + q)) * 6f - 3f), Rand01(U32(k * 41 + q)) * 360f, 1.6f);
                continue;
            }
            int n = kind < 6 ? 2 + (int)(Rand01(U32(k * 7 + 8106)) * 3f) : 1;
            for (int q = 0; q < n; q++)
            {
                Vector3 p = c + new Vector3(Rand01(U32(k * 13 + q * 3 + 1)) * 7f - 3.5f, 0f, Rand01(U32(k * 13 + q * 3 + 2)) * 7f - 3.5f);
                float yaw = Rand01(U32(k * 13 + q * 3 + 3)) * 360f;
                if (kind < 6) PlaceScenery(trees[(k + q) % trees.Length], p, yaw, 1.1f + Rand01(U32(k * 17 + q)) * 0.6f);
                else PlaceScenery(low[(k + q) % low.Length], p, yaw, 1.4f + Rand01(U32(k * 19 + q)) * 0.8f);
            }
        }
        // the nearer hills
        if (skyPrefabs == null || skyPrefabs.Length < 9) return;
        float r1 = Mathf.Max(w, h) * 0.5f + 42f;
        for (int k = 0; k < 10; k++)
        {
            float a = LobbyGap((k + Rand01(U32(k + 8301)) * 0.5f) / 10f * Mathf.PI * 2f, 0.3f);   // these hid the lobby too
            Vector3 p = skyCentre + new Vector3(Mathf.Cos(a) * r1, -3.2f, Mathf.Sin(a) * r1);   // sunk: their cliffs read as floating plateaus this close
            Vector3 rel = p - new Vector3(tx, 0f, tz);
            if (rel.x * DX[rd] + rel.z * DZ[rd] > 0f && Mathf.Abs(rel.x * DZ[rd] - rel.z * DX[rd]) < 14f) continue;   // the track runs out between them
            PlaceSky(skyPrefabs[k % 2 == 0 ? SKY_HILL_A : SKY_HILL_B], p, Rand01(U32(k + 8401)) * 360f, 1.0f + Rand01(U32(k + 8501)) * 0.5f);
        }
    }

    Mesh PlanetDisc()
    {
        int rows = 24;
        Vector3[] v = new Vector3[rows * 4]; Vector3[] n = new Vector3[rows * 4]; Color32[] c = new Color32[rows * 4]; int[] t = new int[rows * 6];
        for (int i = 0; i < rows; i++)
        {
            float y0 = -0.5f + i / (float)rows, y1 = y0 + 1f / rows;
            float w0 = Mathf.Sqrt(Mathf.Max(0f, 0.25f - y0 * y0)), w1 = Mathf.Sqrt(Mathf.Max(0f, 0.25f - y1 * y1));
            bool band = i == 9 || i == 10 || i == 15;
            Color32 col = band ? new Color32(200, 196, 214, 255) : (i < 12 ? new Color32(255, 255, 255, 255) : new Color32(236, 240, 250, 255));
            int k = i * 4;
            v[k] = new Vector3(-w0, y0, 0f); v[k + 1] = new Vector3(-w1, y1, 0f); v[k + 2] = new Vector3(w1, y1, 0f); v[k + 3] = new Vector3(w0, y0, 0f);
            for (int q = 0; q < 4; q++) { n[k + q] = Vector3.back; c[k + q] = col; }
            int j = i * 6;
            t[j] = k; t[j + 1] = k + 1; t[j + 2] = k + 2; t[j + 3] = k; t[j + 4] = k + 2; t[j + 5] = k + 3;
        }
        Mesh m = new Mesh();
        m.vertices = v; m.normals = n; m.colors32 = c; m.triangles = t;
        m.RecalculateBounds();
        return m;
    }

    // ---- review 3: one landmark per map, beyond the far end of the lane and taller than any roof, so every lane has
    // something on its skyline: a water tower on an ordinary map, a windmill in a medieval village, a mast on a space base
    void Landmark()
    {
        float w = siteW * CELL, h = siteH * CELL;
        int fd = gen.releaseDir;   // into the map: the far side is that way
        Vector3 c = new Vector3(w * 0.5f, 0f, h * 0.5f) + new Vector3(DX[fd] * (w * 0.5f + 16f), 0f, DZ[fd] * (h * 0.5f + 16f));
        c.x += (Rand01(9101u) - 0.5f) * Mathf.Min(w, 20f) * (DX[fd] == 0 ? 1f : 0f);
        c.z += (Rand01(9102u) - 0.5f) * Mathf.Min(h, 20f) * (DZ[fd] == 0 ? 1f : 0f);
        lv = Vector3.zero;
        if (theme == GenData.THEME_SPACE)
        {
            Color32 m = C(150, 156, 166), o = C(224, 122, 40);
            Prism(c, 1.2f, 0.7f, 0f, 3f, 8, C(110, 116, 128));
            Prism(c, 0.35f, 0.18f, 3f, 24f, 6, m);
            for (int k = 1; k <= 3; k++) Prism(c, 0.9f - k * 0.15f, 0.9f - k * 0.15f, 3f + k * 5f, 3.4f + k * 5f, 8, o);
            Box(B_WALLS, c + new Vector3(-0.25f, 24f, -0.25f), c + new Vector3(0.25f, 24.5f, 0.25f), new Color32(255, 80, 60, 0), FACE_ALL & ~FACE_YN);   // a red beacon, glowing (no underside: it lay on the mast's top)
            PlaceDecor(DECOR_DISH, c + new Vector3(0f, 17f, 0f), Rand01(9103u) * 360f);
            return;
        }
        if (theme == GenData.THEME_MEDIEVAL)
        {
            // a windmill: a tapered stone tower, a conical cap, four sails turned to the map
            Color32 st = Vary(C(170, 164, 150), 3, 7, 6);
            Prism(c, 3.0f, 2.2f, 0f, 11f, 10, st);
            Cone(c, 2.6f, 11f, 14.5f, 10, C(120, 84, 58));
            Vector3 toMap = new Vector3(-DX[fd], 0f, -DZ[fd]);
            Vector3 hub = c + toMap * 2.7f + Vector3.up * 11.5f, right = new Vector3(toMap.z, 0f, -toMap.x);
            float spin = Rand01(9104u) * 90f;
            for (int k = 0; k < 4; k++)
            {
                float a = (spin + k * 90f) * Mathf.Deg2Rad;
                Vector3 dirS = right * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a), nrm = Vector3.Cross(dirS, toMap).normalized;
                // the cloth begins where the spar ends, and the spar stands a little proud of it: the two lay in one
                // plane and overlapped, which flickered (Cor: z-fighting)
                Vector3 p0 = hub + dirS * 0.6f + nrm * 0.12f, p1 = hub + dirS * 0.6f + nrm * 0.75f, p2 = hub + dirS * 7.5f + nrm * 0.75f, p3 = hub + dirS * 7.5f + nrm * 0.12f;
                WQuad(p0, p1, p2, p3, C(226, 216, 196));
                Vector3 proud = toMap * 0.04f;
                WQuad(hub - nrm * 0.12f + proud, hub + nrm * 0.12f + proud, hub + dirS * 7.6f + nrm * 0.12f + proud, hub + dirS * 7.6f - nrm * 0.12f + proud, C(96, 70, 48));
            }
            return;
        }
        // a water tower: four legs and braces, a round tank, a pointed cap
        Color32 leg = C(120, 124, 132), tank = Blend(palTrim, C(200, 204, 210), 40);
        for (int k = 0; k < 4; k++)
        {
            float lx = (k & 1) == 0 ? -1.8f : 1.8f, lz = (k & 2) == 0 ? -1.8f : 1.8f;
            Box(B_WALLS, c + new Vector3(lx - 0.15f, 0f, lz - 0.15f), c + new Vector3(lx + 0.15f, 13f, lz + 0.15f), leg, FACE_ALL & ~FACE_YN);
        }
        for (int y = 4; y <= 10; y += 6)
        {
            Box(B_WALLS, c + new Vector3(-1.8f, y, -1.85f), c + new Vector3(1.8f, y + 0.15f, -1.75f), leg, FACE_ALL);
            Box(B_WALLS, c + new Vector3(-1.8f, y, 1.75f), c + new Vector3(1.8f, y + 0.15f, 1.85f), leg, FACE_ALL);
            Box(B_WALLS, c + new Vector3(-1.85f, y, -1.8f), c + new Vector3(-1.75f, y + 0.15f, 1.8f), leg, FACE_ALL);
            Box(B_WALLS, c + new Vector3(1.75f, y, -1.8f), c + new Vector3(1.85f, y + 0.15f, 1.8f), leg, FACE_ALL);
        }
        Prism(c, 3.0f, 3.0f, 13f, 17.5f, 12, tank);
        Prism(c, 3.1f, 3.1f, 14.9f, 15.2f, 12, palTrim);
        Cone(c, 3.2f, 17.5f, 19.5f, 12, C(88, 92, 100));
    }

    // a closed prism of `sides` faces from radius r0 at y0 to r1 at y1 round c, both sides drawn, top capped
    void Prism(Vector3 c, float r0, float r1, float y0, float y1, int sides, Color32 col)
    {
        for (int k = 0; k < sides; k++)
        {
            float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            WQuad(c + d0 * r0 + Vector3.up * y0, c + d0 * r1 + Vector3.up * y1, c + d1 * r1 + Vector3.up * y1, c + d1 * r0 + Vector3.up * y0, col);
            WQuad(c + Vector3.up * y1, c + d0 * r1 + Vector3.up * y1, c + d1 * r1 + Vector3.up * y1, c + Vector3.up * y1 + (d0 + d1) * 0.001f, col);
        }
    }

    void Cone(Vector3 c, float r, float y0, float y1, int sides, Color32 col)
    {
        for (int k = 0; k < sides; k++)
        {
            float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 apex = c + Vector3.up * y1;
            WQuad(c + d0 * r + Vector3.up * y0, apex + d0 * 0.01f, apex + d1 * 0.01f, c + d1 * r + Vector3.up * y0, col);
        }
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
        float w = siteW * CELL, h = siteH * CELL;
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
        float half = (alongX ? bz1 - bz0 : bx1 - bx0) * 0.5f, rise = theme == GenData.THEME_MEDIEVAL ? Mathf.Min(half, 3.6f) : Mathf.Min(half * PITCH, MAX_RISE);
        float cx = alongX ? bx0 + 1.2f : (bx0 + bx1) * 0.5f + 0.6f, cz = alongX ? (bz0 + bz1) * 0.5f + 0.6f : bz0 + 1.2f;
        int cc = (int)(cz / CELL) * W + (int)(cx / CELL);
        if (cc < 0 || cc >= W * H || cellInst[cc] != inst) return;
        Box(B_ROOFS, new Vector3(cx - 0.35f, t, cz - 0.35f), new Vector3(cx + 0.35f, t + rise + 0.9f, cz + 0.35f), theme == GenData.THEME_NONE ? C(150, 88, 72) : C(128, 122, 114), FACE_ALL & ~FACE_YN);
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
        lightPos[nLights] = follow == null ? p + lv : p; lightKind[nLights] = kind; lightFollow[nLights] = follow;
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
        t.position = p + lv;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        Transform lp = t.Find("LightPoint");
        AddLight(lp != null ? lp.position - lv : p, lightKind, null);
        return true;
    }

    int DefIndex(string id)
    {
        string[] ids = gen.defId;
        for (int i = 0; i < ids.Length; i++) if (ids[i] == id) return i;
        return -1;
    }

    // a small roof over an outside door (review 3: long walls with nothing on them): 0.7 m deep, brackets under it
    void Awning(bool horizontal, float ex, float ez, float d0, float d1, float dh, float side, int cell)
    {
        Color32 col = theme == GenData.THEME_SPACE ? C(84, 90, 102) : (theme == GenData.THEME_MEDIEVAL ? C(84, 58, 38) : RoofColour(cellZone[cell], StyleAt(cell)));
        Color32 br = theme == GenData.THEME_SPACE ? C(224, 122, 40) : (theme == GenData.THEME_MEDIEVAL ? C(84, 58, 38) : palTrim);
        float n0 = WALL_HALF + facadeDepth, n1 = n0 + 0.75f, y0 = dh + 0.18f, y1 = dh + 0.26f, a0 = d0 - 0.3f, a1 = d1 + 0.3f;
        AwBox(horizontal, ex, ez, a0, a1, n0, n1, y0, y1, side, col);
        AwBox(horizontal, ex, ez, a0, a0 + 0.06f, n0, n1 - 0.05f, y0 - 0.3f, y0, side, br);
        AwBox(horizontal, ex, ez, a1 - 0.06f, a1, n0, n1 - 0.05f, y0 - 0.3f, y0, side, br);
    }

    void AwBox(bool horizontal, float ex, float ez, float a0, float a1, float n0, float n1, float y0, float y1, float side, Color32 col)
    {
        float m0 = side > 0 ? n0 : -n1, m1 = side > 0 ? n1 : -n0;
        if (horizontal) Box(B_WALLS, new Vector3(ex + a0, y0, ez + m0), new Vector3(ex + a1, y1, ez + m1), col, FACE_ALL);
        else Box(B_WALLS, new Vector3(ex + m0, y0, ez + a0), new Vector3(ex + m1, y1, ez + a1), col, FACE_ALL);
    }

    // a lantern on the outside wall beside a door: `a` metres along the edge, `side` +1/-1 the outside's direction
    // a frame piece on the outside of a wall that has a facade: from the wall's face to just past the facade's
    void TrimOut(bool horizontal, float ex, float ez, float a0, float a1, float y0, float y1, Color32 col, float side)
    {
        int faces = y0 <= 0f ? FACE_ALL & ~FACE_YN : FACE_ALL;
        float n0 = WALL_HALF, n1 = WALL_HALF + facadeDepth + 0.03f;
        float lo = side > 0 ? n0 : -n1, hi = side > 0 ? n1 : -n0;
        if (horizontal) Box(B_WALLS, new Vector3(ex + a0, y0, ez + lo), new Vector3(ex + a1, y1, ez + hi), col, faces);
        else Box(B_WALLS, new Vector3(ex + lo, y0, ez + a0), new Vector3(ex + hi, y1, ez + a1), col, faces);
    }

    void Porch(bool horizontal, float ex, float ez, float a, float doorTop, float side)
    {
        if (a > CELL - 0.2f) return;
        float yaw = horizontal ? (side > 0 ? 0f : 180f) : (side > 0 ? 90f : -90f);
        float fd = WALL_HALF + facadeDepth;
        // the light (a village's torch) hangs below the door's awning and clear of its end, not in it (Cor: the torch
        // clipped the beam)
        float py = doorTop - 0.35f;
        a = Mathf.Min(a + 0.15f, CELL - 0.2f);
        if (a > CELL - 0.2f) return;
        Vector3 at = horizontal ? new Vector3(ex + a, py, ez + side * fd) : new Vector3(ex + side * fd, py, ez + a);
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
            lv = CellOff(c);
            int n = count[r], k = Mathf.Clamp((n + 3) / 5, 1, 4), i = seen[r];
            seen[r] = i + 1;
            // the middle cell of each of k equal runs of the room's cells
            bool place = false;
            for (int j = 0; j < k; j++) if (i == ((2 * j + 1) * n) / (2 * k)) place = true;
            if (place && StairBelow(c) >= 0 && i + 1 < n) { place = false; seen[r] = i; }   // under a stair's hole: the room's next cell takes it
            if (!place) continue;
            float h = zoneWallMm[cellZone[c]] / 1000f;
            float cx = (c % W + 0.5f) * CELL, cz = (c / W + 0.5f) * CELL;
            if (theme != GenData.THEME_SPACE && PlaceFixture(0, new Vector3(cx, h, cz), 0f, LK_CEILING)) continue;   // a space base: the flat panel below (review 8)
            Box(B_ROOFS, new Vector3(cx - 0.28f, h - 0.05f, cz - 0.28f), new Vector3(cx + 0.28f, h, cz + 0.28f), rim, FACE_ALL & ~FACE_YP);
            Box(B_ROOFS, new Vector3(cx - 0.21f, h - 0.09f, cz - 0.21f), new Vector3(cx + 0.21f, h - 0.05f, cz + 0.21f), glow, FACE_ALL & ~FACE_YP);
            AddLight(new Vector3(cx, h - 0.25f, cz), LK_CEILING, null);
        }
        lv = Vector3.zero;

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
            bool atHouse = false;
            // first a side that is open ground (a garden, a yard, the map's edge), then a building's wall
            for (int pass = 0; pass < 2 && side < 0; pass++)
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], nz = z + DZ[d];
                    bool house = InGrid(nx, nz) && IsBuilding(cellZone[nz * W + nx]);
                    if (house != (pass == 1)) continue;
                    if (!InGrid(nx, nz)) { side = d; break; }
                    int rn = cellRoom[nz * W + nx];
                    if (rn < 0 || rn >= nr || roomType[rn] != GenData.R_LANE) { side = d; atHouse = house; break; }
                }
            if (side < 0) continue;
            float cx = (x + 0.5f) * CELL, cz = (z + 0.5f) * CELL;
            float dx = DX[side], dz = DZ[side];
            float inset = atHouse ? 1.0f : 0.35f;   // clear of the eaves (0.35 m past the wall) and the lamp's own head
            float px = cx + dx * (CELL * 0.5f - inset), pz = cz + dz * (CELL * 0.5f - inset);
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
            if (first < 0 || !IsBuilding(cellZone[first]) || !IsTop(inst)) continue;
            int zone = cellZone[first];
            if (theme == GenData.THEME_SPACE) continue;   // flat roofs
            lv = instOff[inst];
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
                GableRoof((x0 + rx) * CELL - WALL_HALF, (x0 + rx + rw) * CELL + WALL_HALF, (z0 + rz) * CELL - WALL_HALF, (z0 + rz + rd) * CELL + WALL_HALF, t, roof, wall,
                          (Mix(gen.seed ^ U32(inst * 313 + guard)) & 1u) == 1u);
            }
        }
        lv = Vector3.zero;
    }

    void GableRoof(float bx0, float bx1, float bz0, float bz1, float t, Color32 roof, Color32 wall, bool flip)
    {
        float lx = bx1 - bx0, lz = bz1 - bz0;
        bool alongX = lx >= lz;
        if (flip && Mathf.Max(lx, lz) < Mathf.Min(lx, lz) * 1.5f) alongX = !alongX;   // a squarish block: the ridge either way (review 3)
        float a0 = alongX ? bx0 : bz0, a1 = alongX ? bx1 : bz1, c0 = alongX ? bz0 : bx0, c1 = alongX ? bz1 : bx1;
        float mid = (c0 + c1) * 0.5f, top = RoofY(mid, c0, c1, t);
        // eaves (review 3: the roofs ended flush with the walls): the slopes run on 0.35 m past the walls, and past the
        // gable ends; a fascia board along the eaves and barge boards up the gables, in the trim colour
        float ov = 0.35f, k = (top - t) / Mathf.Max(0.01f, (c1 - c0) * 0.5f), ye = t - ov * k;
        float A0 = a0 - ov * 0.8f, A1 = a1 + ov * 0.8f, C0 = c0 - ov, C1 = c1 + ov;
        QuadBoth(RP(alongX, A0, ye, C0), RP(alongX, A0, top, mid), RP(alongX, A1, top, mid), RP(alongX, A1, ye, C0), roof);
        QuadBoth(RP(alongX, A0, top, mid), RP(alongX, A0, ye, C1), RP(alongX, A1, ye, C1), RP(alongX, A1, top, mid), roof);
        Color32 fascia = theme == GenData.THEME_MEDIEVAL ? C(84, 58, 38) : palTrim;
        float fd = 0.16f;
        QuadBoth(RP(alongX, A0, ye - fd, C0), RP(alongX, A0, ye, C0), RP(alongX, A1, ye, C0), RP(alongX, A1, ye - fd, C0), fascia);
        QuadBoth(RP(alongX, A0, ye - fd, C1), RP(alongX, A0, ye, C1), RP(alongX, A1, ye, C1), RP(alongX, A1, ye - fd, C1), fascia);
        for (int e = 0; e < 2; e++)
        {
            float A = e == 0 ? A0 : A1;
            QuadBoth(RP(alongX, A, ye - fd, C0), RP(alongX, A, ye, C0), RP(alongX, A, top, mid), RP(alongX, A, top - fd, mid), fascia);
            QuadBoth(RP(alongX, A, top - fd, mid), RP(alongX, A, top, mid), RP(alongX, A, ye, C1), RP(alongX, A, ye - fd, C1), fascia);
        }
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
            lv = CellOff(c);
            int ci = cellInst[c];
            bool top = IsTop(ci);
            float h = zoneWallMm[zone] / 1000f, t = h + 0.14f;
            bool bw = Bld(x - 1, z), be = Bld(x + 1, z), bs = Bld(x, z - 1), bn2 = Bld(x, z + 1);
            float x0 = x * CELL - (bw ? 0f : WALL_HALF), x1 = (x + 1) * CELL + (be ? 0f : WALL_HALF);
            // the ceiling by theme (review 7): pale grey panels on a space base, warm plaster in a village, off-white
            Color32 rc = RoofColour(zone, Style(ci)), ceiling = theme == GenData.THEME_SPACE ? C(206, 212, 220) : (theme == GenData.THEME_MEDIEVAL ? C(224, 210, 184) : C(244, 242, 236));
            // the slab over the room: the roof, or the floor of the level above, open over a stair
            float hx0 = 0f, hx1 = 0f, hz0 = 0f, hz1 = 0f;
            int sb = StairBelow(c);
            if (sb >= 0) StairHole(sb, c, out hx0, out hx1, out hz0, out hz1);
            if (!top) rc = ceiling;
            Slab(B_ROOFS, x0, x1, z * CELL, (z + 1) * CELL, h, t, hx0, hx1, hz0, hz1, rc, rc, ceiling);
            // a basement's floor
            if (ci >= 0 && ci < instLevelB.Length && instLevelB[ci] < 0) Collider(new Vector3(x * CELL - WALL_HALF, -0.4f, z * CELL - WALL_HALF), new Vector3((x + 1) * CELL + WALL_HALF, 0f, (z + 1) * CELL + WALL_HALF));
            for (int side = -1; side <= 1; side += 2)
            {
                if (side < 0 ? bs : bn2) continue;
                int dz = side < 0 ? z - 1 : z + 1;
                float s0 = bw ? (Bld(x - 1, dz) ? x * CELL + WALL_HALF : x * CELL) : x * CELL - WALL_HALF;
                float s1 = be ? (Bld(x + 1, dz) ? (x + 1) * CELL - WALL_HALF : (x + 1) * CELL) : (x + 1) * CELL + WALL_HALF;
                float zz0 = side < 0 ? z * CELL - WALL_HALF : (z + 1) * CELL, zz1 = side < 0 ? z * CELL : (z + 1) * CELL + WALL_HALF;
                Box3(B_ROOFS, new Vector3(s0, h, zz0), new Vector3(s1, t, zz1), rc, rc, ceiling, FACE_ALL);
            }
            Slab(B_COLL, x * CELL - WALL_HALF, (x + 1) * CELL + WALL_HALF, z * CELL - WALL_HALF, (z + 1) * CELL + WALL_HALF, h, t, hx0, hx1, hz0, hz1, rc, rc, rc);
            // houses get a pitched roof on top of the flat one (the flat slab stays: the ceiling and the collider);
            // a space base keeps its roofs flat, with an orange parapet round the edge
            if (theme == GenData.THEME_SPACE && top)
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
        lv = Vector3.zero;
    }

    // ------------------------------------------------------------------ props
    void MakeProp(int e)
    {
        GameObject prefab = propPrefabs[emitDef[e]];
        if (prefab == null) return;
        GameObject go = Instantiate(prefab);
        Transform t = go.transform;
        t.SetParent(propRoot, true);
        int ix = Mathf.Clamp(emitX[e] / 500, 0, IW - 1), iz = Mathf.Clamp(emitZ[e] / 500, 0, IH - 1);
        t.position = new Vector3(emitX[e], emitY[e], emitZ[e]) / 1000f + InnerOff(iz * IW + ix);   // upstairs or in a basement: where it belongs
        t.rotation = Quaternion.Euler(0, emitYaw[e], 0);
        objects[e] = go;
        // the dark patch under a prop (the prefab's last child, "Shade") only where it stands on a floor
        if (emitParent[e] >= 0 && t.childCount > 0)
        {
            Transform sh = t.GetChild(t.childCount - 1);
            if (sh.name == "Shade") sh.gameObject.SetActive(false);
        }
        TintProp(go, emitDef[e]);
        FadeProp(go);
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

    // ------------------------------------------------------------------ scenery batching (Cor: batching)
    // Everything under the fixture and fence roots that never moves (hills, decor, the country's trees and bushes,
    // light fixtures, fences, hedges) is drawn by one merged mesh instead of one renderer each: its renderers are
    // switched off (their colliders and light points stay). Clouds drift and the sky's own objects (stars, the moon, the
    // planet) have their own shader, so they stay as they are. One renderer per step, so the build stays time-sliced.
    public bool mergeScenery = true;   // the static scenery merged in tiles (profiling)
    MeshRenderer[] mergeList;
    int[] mergeKey;
    int mergeCur, nTiles;
    GameObject[] tiles;
    Material mergeMat;
    MaterialPropertyBlock mergeBlock;

    // The scenery is merged in tiles 24 m square (one mesh for all of it was never culled: profiling), and everything
    // far out (the hill rings, mountains) in one more, which is on screen whenever the horizon is
    int TileKey(Vector3 p)
    {
        float far = Mathf.Max(siteW, siteH) * CELL * 0.5f + 35f;
        if ((p - new Vector3(siteW * CELL * 0.5f, 0f, siteH * CELL * 0.5f)).magnitude > far) return 0;
        int tx = Mathf.FloorToInt((p.x + 64f) / 24f), tz = Mathf.FloorToInt((p.z + 64f) / 24f);
        return 1 + Mathf.Clamp(tx, 0, 63) + 64 * Mathf.Clamp(tz, 0, 63);
    }

    void MergeBegin()
    {
        mergeList = null; nTiles = 0;
        if (!mergeScenery || sceneryFilter == null || fixtureRoot == null || fenceRoot == null) return;
        mergeMat = wallsFilter.GetComponent<MeshRenderer>().sharedMaterial;
        MeshRenderer[] a = fixtureRoot.GetComponentsInChildren<MeshRenderer>(), b = fenceRoot.GetComponentsInChildren<MeshRenderer>();
        int n = a.Length + b.Length;
        mergeList = new MeshRenderer[n];
        System.Array.Copy(a, mergeList, a.Length);
        System.Array.Copy(b, 0, mergeList, a.Length, b.Length);
        mergeKey = new int[n];
        for (int i = 0; i < n; i++) mergeKey[i] = mergeList[i] != null ? TileKey(mergeList[i].bounds.center) : 0;
        // by tile (insertion sort: a few hundred renderers)
        for (int i = 1; i < n; i++)
        {
            int k = mergeKey[i]; MeshRenderer r = mergeList[i]; int j = i - 1;
            while (j >= 0 && mergeKey[j] > k) { mergeKey[j + 1] = mergeKey[j]; mergeList[j + 1] = mergeList[j]; j--; }
            mergeKey[j + 1] = k; mergeList[j + 1] = r;
        }
        if (tiles == null) tiles = new GameObject[96];
        mergeCur = n > 0 ? mergeKey[0] : -1;
        BufInit(B_SCENERY, 4096);
        if (mergeBlock == null) mergeBlock = new MaterialPropertyBlock();
    }

    bool IsCloud(Transform t)
    {
        for (int i = 0; i < nClouds; i++) if (clouds[i] != null && t.IsChildOf(clouds[i])) return true;
        return false;
    }

    void MergeOne(int idx)
    {
        if (mergeKey[idx] != mergeCur) { FlushTile(); mergeCur = mergeKey[idx]; }
        MeshRenderer mr = mergeList[idx];
        if (mr == null || !mr.enabled || mr.sharedMaterial != mergeMat || IsCloud(mr.transform)) return;
        MeshFilter mf = mr.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        Mesh m = mf.sharedMesh;
        Vector3[] v = m.vertices, n = m.normals;
        Color32[] c = m.colors32;
        int[] tri = m.triangles;
        Color tint = Color.white;
        mr.GetPropertyBlock(mergeBlock);
        Color pc = mergeBlock.GetColor("_Color");
        if (pc.a > 0f) tint = pc;
        bool leafOnly = pc.a > 0f && pc.a < 0.75f;   // a foliage tint (TintProp): not on the trunk
        Matrix4x4 M = mr.transform.localToWorldMatrix;
        int b = B_SCENERY, s = nv[b];
        while (nv[b] + v.Length > bv[b].Length || nt[b] + tri.Length > bt[b].Length) BufGrow(b);
        bool hasN = n != null && n.Length == v.Length, hasC = c != null && c.Length == v.Length;
        for (int i = 0; i < v.Length; i++)
        {
            bv[b][s + i] = M.MultiplyPoint3x4(v[i]);
            bn[b][s + i] = hasN ? M.MultiplyVector(n[i]).normalized : Vector3.up;
            Color32 k = hasC ? c[i] : new Color32(255, 255, 255, 255);
            if (leafOnly && k.g < k.r + 20) bc[b][s + i] = k;
            else bc[b][s + i] = new Color32((byte)Mathf.Min(255, (int)(k.r * tint.r)), (byte)Mathf.Min(255, (int)(k.g * tint.g)), (byte)Mathf.Min(255, (int)(k.b * tint.b)), k.a);
        }
        int t0 = nt[b];
        for (int i = 0; i < tri.Length; i++) bt[b][t0 + i] = s + tri[i];
        nv[b] = s + v.Length; nt[b] = t0 + tri.Length;
        mr.enabled = false;
    }

    // the tile gathered so far becomes a mesh of its own: the template holder for the first, a copy of it after
    void FlushTile()
    {
        if (nv[B_SCENERY] == 0) return;
        MeshFilter f = sceneryFilter;
        if (nTiles > 0)
        {
            if (nTiles >= tiles.Length) { GameObject[] g = new GameObject[tiles.Length * 2]; System.Array.Copy(tiles, g, tiles.Length); tiles = g; }
            GameObject go = Instantiate(sceneryFilter.gameObject);
            go.transform.SetParent(sceneryFilter.transform.parent, false);
            tiles[nTiles] = go;
            f = go.GetComponent<MeshFilter>();
        }
        f.sharedMesh = ToMesh(B_SCENERY, "Scenery" + nTiles);
        nTiles++;
        nv[B_SCENERY] = 0; nt[B_SCENERY] = 0;
    }

    void MergeEnd()
    {
        if (mergeList != null && sceneryFilter != null) FlushTile();
        mergeList = null;
    }

    // the scenery tiles (PHSun bakes their shadows with the rest)
    public int SceneryTileCount() { return nTiles; }
    public GameObject SceneryTile(int i) { return i == 0 ? (sceneryFilter != null ? sceneryFilter.gameObject : null) : tiles[i]; }

    // ------------------------------------------------------------------ helpers for the game
    // an inner cell's centre in the world (a hider's spawn may be upstairs or in a basement)
    public Vector3 InnerWorld(int i) { return new Vector3((i % IW + 0.5f) * INNER, 0f, (i / IW + 0.5f) * INNER) + InnerOff(i); }
    public float SiteWidth() { return siteW * CELL; }
    public float SiteDepth() { return siteH * CELL; }
    public Vector3 ReleasePoint() { return new Vector3((gen.releaseX + 0.5f) * CELL, 0f, (gen.releaseZ + 0.5f) * CELL); }
    public float ReleaseYaw() { return gen.releaseDir * 90f; }
    public void ShowRoofs(bool show) { if (roofsFilter != null) roofsFilter.GetComponent<MeshRenderer>().enabled = show; }
}
