using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// Runs the generator and materializes its result, both in time slices (04 section 4.6.2 steps 5 and 6):
// ground and floors, walls with doorways and windows, roofs, boundary fences, then the props of the emit list.
// Architecture is simple generated geometry; props are the KayKit and Kenney models at one scale per type.
public class MapBuilder
{
    public MapGen gen = new MapGen();
    public GameObject root;
    public Transform archRoot, roofRoot, propRoot;
    public PropInstance[] props;            // by emit index; null for scenery and decoration
    public GameObject[] objects;            // by emit index
    public bool built, failed;
    public float progress;
    public string phase = "";
    public double genMs, buildMs, wallMs;   // time spent generating and building, and wall-clock time from the press
    public int colliderCount, rendererCount;

    int stage, cursor;
    MeshBuf ground, walls, roofs;
    readonly Stopwatch watch = new Stopwatch();
    readonly Stopwatch total = new Stopwatch();

    const float CELL = 4f, INNER = 0.5f, WALL_HALF = 0.1f;

    // ---- colours (sRGB) ----
    static Color32 C(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }
    static readonly Color32 Frame = C(122, 88, 62), WindowFrame = C(238, 238, 232), Ceiling = C(236, 234, 226);

    static Color32 Vary(Color32 c, int ix, int iz, int amount)
    {
        uint h = GenRng.Mix((uint)(ix * 73856093) ^ (uint)(iz * 19349663));
        int d = (int)(h % (uint)(2 * amount + 1)) - amount;
        return C(Mathf.Clamp(c.r + d, 0, 255), Mathf.Clamp(c.g + d, 0, 255), Mathf.Clamp(c.b + d, 0, 255));
    }

    static Color32 WallColour(int roomType)
    {
        switch (roomType)
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

    static Color32 ExteriorColour(int zone)
    {
        return zone == GenData.Z_HOUSE ? C(232, 212, 180) : (zone == GenData.Z_DINER ? C(214, 92, 80) : C(122, 142, 160));
    }

    static Color32 RoofColour(int zone)
    {
        return zone == GenData.Z_HOUSE ? C(172, 94, 80) : (zone == GenData.Z_DINER ? C(232, 230, 222) : C(100, 106, 116));
    }

    Color32 FloorColour(int roomType, int ix, int iz)
    {
        switch (roomType)
        {
            case GenData.R_LIVING: case GenData.R_BEDROOM: case GenData.R_OFFICE: case GenData.R_DINING: case GenData.R_KIDS: case GenData.R_HALL:
            case GenData.R_WH_OFFICE:
                // wood planks: strips two cells wide, alternating shade
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

    public void Begin(uint seed, int size, int density)
    {
        Clear();
        ModelLibrary.Load();
        root = new GameObject("Map");
        archRoot = new GameObject("Architecture").transform; archRoot.SetParent(root.transform, false);
        roofRoot = new GameObject("Roofs").transform; roofRoot.SetParent(root.transform, false);
        propRoot = new GameObject("Props").transform; propRoot.SetParent(root.transform, false);
        gen.Begin(seed, size, density);
        stage = 0; cursor = 0; built = false; failed = false; progress = 0f;
        genMs = 0; buildMs = 0; colliderCount = 0; rendererCount = 0;
        total.Reset(); total.Start();
        phase = "Generating the layout";
    }

    public void Clear()
    {
        PropInstance.simulating.Clear();
        if (root != null) Object.Destroy(root);
        root = null; props = null; objects = null; built = false;
    }

    // Does work for about budgetMs, then returns. True when the map is complete (or generation failed).
    public bool Step(float budgetMs)
    {
        if (built || failed) return true;
        // a time budget, and a cap on units of work so the slicing also holds where the browser's timer is coarse
        watch.Reset(); watch.Start();
        bool finished = false;
        bool wasGen = stage == 0;
        for (int units = 0; units < 48 && watch.Elapsed.TotalMilliseconds < budgetMs; units++)
        {
            if (StepOne()) { finished = true; break; }
            if (wasGen != (stage == 0)) break;   // generation and building are timed apart
        }
        watch.Stop();
        if (wasGen) genMs += watch.Elapsed.TotalMilliseconds; else buildMs += watch.Elapsed.TotalMilliseconds;
        if (finished) { built = !failed; total.Stop(); wallMs = total.Elapsed.TotalMilliseconds; }
        return finished;
    }

    bool StepOne()
    {
        switch (stage)
        {
            case 0:
                // logical generation, one unit per call
                if (gen.Step())
                {
                    if (gen.failed) { failed = true; phase = "Generation failed: " + gen.failReason; return true; }
                    props = new PropInstance[gen.nEmit]; objects = new GameObject[gen.nEmit];
                    ground = new MeshBuf(); walls = new MeshBuf(); roofs = new MeshBuf();
                    stage = 1; cursor = 0; phase = "Laying floors";
                }
                progress = 0.25f * gen.progressPermille / 1000f;
                break;
            case 1:
                // ground and floors: one row of inner cells per call
                if (cursor < gen.IH) { GroundRow(cursor); cursor++; progress = 0.25f + 0.1f * cursor / gen.IH; }
                else { FinishGround(); stage = 2; cursor = 0; phase = "Raising walls"; }
                break;
            case 2:
                // walls: one outer cell (its north and east edges, plus south/west on the border) per call
                if (cursor < gen.W * gen.H) { CellWalls(cursor); cursor++; progress = 0.35f + 0.15f * cursor / (gen.W * gen.H); }
                else
                {
                    walls.ToObject("Walls", archRoot, ModelLibrary.lit);
                    roofs.ToObject("RoofMesh", roofRoot, ModelLibrary.lit);
                    rendererCount += 2;
                    stage = 3; cursor = 0; phase = "Placing props";
                }
                break;
            case 3:
                if (cursor < gen.nEmit) { MakeProp(cursor); cursor++; progress = 0.5f + 0.48f * cursor / Mathf.Max(1, gen.nEmit); }
                else { stage = 4; phase = "Finishing"; }
                break;
            case 4:
                Physics.SyncTransforms();
                progress = 1f;
                phase = "Ready";
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ ground
    // Outdoors the ground is drawn by kind (MapGen.BuildGround): lawn, forest floor, soil, paving, trodden earth.
    // Where two kinds meet they blend (G30), in two ways that work together: the generator frays the border (cells
    // within two of it take the other kind by a seeded hash), and here the corners of the cells along a border
    // take the mean colour of the cells around them, so the colour runs over half a metre instead of stepping.
    // Indoor floors keep their hard edges at the walls.
    Color32[] cellColour;

    Color32 CellColour(int i, int ix, int iz)
    {
        switch (gen.ground[i])
        {
            case MapGen.GR_LAWN: return Vary(C(116, 170, 86), ix, iz, 7);
            case MapGen.GR_FOREST: return Vary(C(88, 138, 74), ix, iz, 9);
            case MapGen.GR_SOIL: return Vary(C(138, 104, 74), ix, iz, 8);
            case MapGen.GR_PAVING: return (((ix / 2 + iz / 2) & 1) == 0) ? C(196, 190, 178) : C(182, 176, 166);
            case MapGen.GR_TRACK: return Vary(C(158, 134, 100), ix, iz, 6);
        }
        return FloorColour(gen.roomType[gen.innerRoom[i]], ix, iz);
    }

    bool Blends(int i)
    {
        int g = gen.ground[i];
        return g >= MapGen.GR_LAWN && g != MapGen.GR_RELEASE;
    }

    // the colour of the grid corner at (cx, cz) for the cell `self`: the mean of the outdoor cells around the
    // corner if they are of more than one kind, else the cell's own colour
    Color32 CornerColour(int cx, int cz, int self)
    {
        int n = 0, r = 0, g = 0, b = 0, kind = -1;
        bool mixed = false;
        for (int k = 0; k < 4; k++)
        {
            int x = cx - (k & 1), z = cz - (k >> 1);
            if (x < 0 || z < 0 || x >= gen.IW || z >= gen.IH) continue;
            int i = z * gen.IW + x;
            if (!Blends(i)) continue;
            if (kind >= 0 && gen.ground[i] != kind) mixed = true;
            kind = gen.ground[i];
            Color32 c = cellColour[i];
            r += c.r; g += c.g; b += c.b; n++;
        }
        if (!mixed) return cellColour[self];
        return C(r / n, g / n, b / n);
    }

    void GroundRow(int iz)
    {
        if (iz == 0 || cellColour == null || cellColour.Length != gen.IW * gen.IH)
        {
            cellColour = new Color32[gen.IW * gen.IH];
            for (int i = 0; i < cellColour.Length; i++)
                if ((gen.tags[i] & GenData.T_FLOOR) != 0) cellColour[i] = CellColour(i, i % gen.IW, i / gen.IW);
        }
        for (int ix = 0; ix < gen.IW; ix++)
        {
            int i = iz * gen.IW + ix;
            if ((gen.tags[i] & GenData.T_FLOOR) == 0) continue;
            float x = ix * INNER, z = iz * INNER;
            Vector3 a = new Vector3(x, 0, z), b = new Vector3(x, 0, z + INNER), c = new Vector3(x + INNER, 0, z + INNER), d = new Vector3(x + INNER, 0, z);
            if (Blends(i)) ground.Quad(a, b, c, d, Vector3.up, CornerColour(ix, iz, i), CornerColour(ix, iz + 1, i), CornerColour(ix + 1, iz + 1, i), CornerColour(ix + 1, iz, i));
            else ground.Quad(a, b, c, d, Vector3.up, cellColour[i]);
        }
    }

    void FinishGround()
    {
        // the land outside the fence, a little lower so it never fights with the floors
        float m = 60f, w = gen.W * CELL, h = gen.H * CELL;
        ground.Quad(new Vector3(-m, -0.03f, -m), new Vector3(-m, -0.03f, h + m), new Vector3(w + m, -0.03f, h + m), new Vector3(w + m, -0.03f, -m), Vector3.up, C(98, 150, 78));
        ground.ToObject("Ground", archRoot, ModelLibrary.lit);
        rendererCount++;
        var col = new GameObject("GroundCollider");
        col.transform.SetParent(archRoot, false);
        col.layer = Layers.Arch;
        var bc = col.AddComponent<BoxCollider>();
        bc.center = new Vector3(w / 2, -0.5f, h / 2);
        bc.size = new Vector3(w + 2 * m, 1f, h + 2 * m);
        colliderCount++;
    }

    // ------------------------------------------------------------------ walls
    void Collider(Vector3 mn, Vector3 mx, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(archRoot, false);
        go.layer = Layers.Arch;
        var bc = go.AddComponent<BoxCollider>();
        bc.center = (mn + mx) * 0.5f; bc.size = mx - mn;
        colliderCount++;
    }

    // one straight piece of wall between a0 and a1 metres along the edge, from y0 to y1.
    // Side A is the low side (south or west of the edge), side B the high side; each has its own colour.
    void WallPiece(bool horizontal, float ex, float ez, float a0, float a1, float y0, float y1, Color32 colA, Color32 colB, bool collider)
    {
        Vector3 mnA, mxA, mnB, mxB;
        if (horizontal)
        {
            mnA = new Vector3(ex + a0, y0, ez - WALL_HALF); mxA = new Vector3(ex + a1, y1, ez);
            mnB = new Vector3(ex + a0, y0, ez); mxB = new Vector3(ex + a1, y1, ez + WALL_HALF);
            walls.Box(mnA, mxA, colA, MeshBuf.FACE_ALL & ~MeshBuf.FACE_ZP);
            walls.Box(mnB, mxB, colB, MeshBuf.FACE_ALL & ~MeshBuf.FACE_ZN);
        }
        else
        {
            mnA = new Vector3(ex - WALL_HALF, y0, ez + a0); mxA = new Vector3(ex, y1, ez + a1);
            mnB = new Vector3(ex, y0, ez + a0); mxB = new Vector3(ex + WALL_HALF, y1, ez + a1);
            walls.Box(mnA, mxA, colA, MeshBuf.FACE_ALL & ~MeshBuf.FACE_XP);
            walls.Box(mnB, mxB, colB, MeshBuf.FACE_ALL & ~MeshBuf.FACE_XN);
        }
        if (collider) Collider(mnA, mxB, "Wall");
    }

    // trim around an opening: sticks out 2 cm on both faces
    void Trim(bool horizontal, float ex, float ez, float a0, float a1, float y0, float y1, Color32 col)
    {
        float t = WALL_HALF + 0.02f;
        if (horizontal) walls.Box(new Vector3(ex + a0, y0, ez - t), new Vector3(ex + a1, y1, ez + t), col);
        else walls.Box(new Vector3(ex - t, y0, ez + a0), new Vector3(ex + t, y1, ez + a1), col);
    }

    void Edge(bool horizontal, int cx, int cz, int type, int off, int cellA, int cellB)
    {
        // cellA: the cell on the low side (south/west), cellB: on the high side; -1 = outside the map
        int zoneA = cellA >= 0 ? gen.cellZone[cellA] : GenData.Z_NONE, zoneB = cellB >= 0 ? gen.cellZone[cellB] : GenData.Z_NONE;
        float ex = cx * CELL, ez = cz * CELL;
        if (type == GenData.E_FENCE)
        {
            Fence(horizontal, ex, ez);
            return;
        }
        float hA = GenData.ZoneWallMm[zoneA] / 1000f, hB = GenData.ZoneWallMm[zoneB] / 1000f;
        float h = Mathf.Max(hA, hB);
        Color32 colA = GenData.IsBuilding(zoneA) ? WallColour(gen.roomType[gen.cellRoom[cellA]]) : ExteriorColour(zoneB);
        Color32 colB = GenData.IsBuilding(zoneB) ? WallColour(gen.roomType[gen.cellRoom[cellB]]) : ExteriorColour(zoneA);
        float lo = -WALL_HALF, hi = CELL + WALL_HALF;   // walls run 10 cm past the cell corners so corners close
        if (type == GenData.E_WALL)
        {
            WallPiece(horizontal, ex, ez, lo, hi, 0f, h, colA, colB, true);
        }
        else if (type == GenData.E_DOOR)
        {
            float d0 = off * INNER, d1 = (off + GenData.DOOR_CELLS) * INNER, dh = GenData.DOOR_HEIGHT_MM / 1000f;
            WallPiece(horizontal, ex, ez, lo, d0, 0f, h, colA, colB, true);
            WallPiece(horizontal, ex, ez, d1, hi, 0f, h, colA, colB, true);
            WallPiece(horizontal, ex, ez, d0, d1, dh, h, colA, colB, true);
            float e = 0.004f;   // trims stand a little proud of the wall pieces they frame
            Trim(horizontal, ex, ez, d0 - 0.06f, d0 + e, 0f, dh, Frame);
            Trim(horizontal, ex, ez, d1 - e, d1 + 0.06f, 0f, dh, Frame);
            Trim(horizontal, ex, ez, d0 - 0.06f, d1 + 0.06f, dh - e, dh + 0.06f, Frame);
        }
        else if (type == GenData.E_WINDOW)
        {
            float w0 = 1.0f, w1 = 3.0f, sill = 0.9f, head = 2.05f;
            WallPiece(horizontal, ex, ez, lo, w0, 0f, h, colA, colB, false);
            WallPiece(horizontal, ex, ez, w1, hi, 0f, h, colA, colB, false);
            WallPiece(horizontal, ex, ez, w0, w1, 0f, sill, colA, colB, false);
            WallPiece(horizontal, ex, ez, w0, w1, head, h, colA, colB, false);
            float e = 0.004f;
            Trim(horizontal, ex, ez, w0 - 0.05f, w1 + 0.05f, sill - 0.05f, sill + e, WindowFrame);
            Trim(horizontal, ex, ez, w0 - 0.05f, w1 + 0.05f, head - e, head + 0.05f, WindowFrame);
            Trim(horizontal, ex, ez, w0 - 0.05f, w0 + e, sill, head, WindowFrame);
            Trim(horizontal, ex, ez, w1 - e, w1 + 0.05f, sill, head, WindowFrame);
            Trim(horizontal, ex, ez, (w0 + w1) / 2 - 0.02f, (w0 + w1) / 2 + 0.02f, sill, head, WindowFrame);
            // a window is a wall for movement and for darts (04 section 4.1.1): one collider for the whole edge
            if (horizontal) Collider(new Vector3(ex + lo, 0, ez - WALL_HALF), new Vector3(ex + hi, h, ez + WALL_HALF), "WindowWall");
            else Collider(new Vector3(ex - WALL_HALF, 0, ez + lo), new Vector3(ex + WALL_HALF, h, ez + hi), "WindowWall");
        }
    }

    int fenceModel = -1;

    void Fence(bool horizontal, float ex, float ez)
    {
        if (fenceModel < 0) fenceModel = ModelLibrary.Find("fence_planks");
        // the Nature Kit fence, stretched to one cell: 4 m long, 1.1 m high
        Bounds b = ModelLibrary.bounds[fenceModel];
        var go = new GameObject("Fence");
        go.transform.SetParent(archRoot, false);
        go.transform.position = horizontal ? new Vector3(ex + CELL / 2, 0, ez) : new Vector3(ex, 0, ez + CELL / 2);
        go.transform.rotation = Quaternion.Euler(0, horizontal ? 0 : 90, 0);
        go.transform.localScale = new Vector3((CELL + 0.1f) / b.size.x, 1.1f / b.size.y, 0.16f / b.size.z);
        go.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.mesh[fenceModel];
        go.AddComponent<MeshRenderer>().sharedMaterial = ModelLibrary.lit;
        rendererCount++;
        // the boundary itself: nothing gets over it
        if (horizontal) Collider(new Vector3(ex - 0.1f, 0, ez - 0.08f), new Vector3(ex + CELL + 0.1f, 6f, ez + 0.08f), "FenceWall");
        else Collider(new Vector3(ex - 0.08f, 0, ez - 0.1f), new Vector3(ex + 0.08f, 6f, ez + CELL + 0.1f), "FenceWall");
    }

    void CellWalls(int c)
    {
        int x = c % gen.W, z = c / gen.W;
        int zone = gen.cellZone[c];
        // north and east edges belong to this cell; south and west only on the grid border or next to an empty cell
        for (int d = 0; d < 4; d++)
        {
            int nx = x + MapGen.DX[d], nz = z + MapGen.DZ[d];
            bool inGrid = gen.InGrid(nx, nz);
            int n = inGrid ? nz * gen.W + nx : -1;
            if ((d == 2 || d == 3) && inGrid) continue;        // the neighbour's north/east edge covers it
            if (zone == GenData.Z_NONE && (n < 0 || gen.cellZone[n] == GenData.Z_NONE)) continue;
            int type = gen.EdgeType(x, z, d);
            if (type == GenData.E_NONE) continue;
            int off = gen.EdgeOff(x, z, d);
            bool horizontal = d == 0 || d == 2;
            int ecx = d == 1 ? x + 1 : x, ecz = d == 0 ? z + 1 : z;
            int cellLow = (d == 0 || d == 1) ? c : n, cellHigh = (d == 0 || d == 1) ? n : c;
            if (cellLow >= 0 && gen.cellZone[cellLow] == GenData.Z_NONE) cellLow = -1;
            if (cellHigh >= 0 && gen.cellZone[cellHigh] == GenData.Z_NONE) cellHigh = -1;
            Edge(horizontal, ecx, ecz, type, off, cellLow, cellHigh);
        }
        if (GenData.IsBuilding(zone))
        {
            // flat roof with a ceiling colour underneath
            float h = GenData.ZoneWallMm[zone] / 1000f;
            Vector3 mn = new Vector3(x * CELL - WALL_HALF, h, z * CELL - WALL_HALF), mx = new Vector3((x + 1) * CELL + WALL_HALF, h + 0.14f, (z + 1) * CELL + WALL_HALF);
            roofs.Box(mn, mx, RoofColour(zone), RoofColour(zone), Ceiling);
            var go = new GameObject("Roof");
            go.transform.SetParent(roofRoot, false);
            go.layer = Layers.Arch;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = (mn + mx) * 0.5f; bc.size = mx - mn;
            colliderCount++;
        }
    }

    // ------------------------------------------------------------------ props
    void MakeProp(int e)
    {
        int def = gen.emitDef[e];
        int model = GenData.defModel[def];
        int fl = GenData.defFlags[def];
        float sc = GenData.defScale[def] / 1000f;
        var go = new GameObject(GenData.defId[def]);
        go.transform.SetParent(propRoot, false);
        go.transform.position = new Vector3(gen.emitX[e], gen.emitY[e], gen.emitZ[e]) / 1000f;
        go.transform.rotation = Quaternion.Euler(0, gen.emitYaw[e], 0);
        go.transform.localScale = new Vector3(sc, sc, sc);
        go.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.mesh[model];
        go.AddComponent<MeshRenderer>().sharedMaterial = ModelLibrary.lit;
        rendererCount++;
        objects[e] = go;
        go.layer = PropColliders.LayerFor(def);
        PropColliders.Build(go, def);
        colliderCount += go.GetComponents<BoxCollider>().Length;
        if ((fl & (GenData.FL_DECOR | GenData.FL_SCENERY)) != 0) return;
        var pi = go.AddComponent<PropInstance>();
        pi.Init(e, def);
        props[e] = pi;
        int parent = gen.emitParent[e];
        if (parent >= 0 && props[parent] != null)
        {
            pi.host = props[parent];
            if ((gen.emitFlags[e] & MapGen.EF_ON_TOP) != 0)
            {
                if (props[parent].topChildren == null) props[parent].topChildren = new List<PropInstance>();
                props[parent].topChildren.Add(pi);
            }
        }
        if ((fl & GenData.FL_OPENABLE) != 0) MakeDoors(go, pi, def, model);
    }

    void MakeDoors(GameObject go, PropInstance pi, int def, int model)
    {
        int[] parts = ModelLibrary.parts[model];
        if (parts.Length == 0) return;
        var doors = new Transform[parts.Length];
        var sign = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            int pm = parts[i];
            var d = new GameObject(GenModels.Name[pm]);
            d.transform.SetParent(go.transform, false);
            d.transform.localPosition = ModelLibrary.pivot[pm];
            d.layer = Layers.Door;
            d.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.mesh[pm];
            d.AddComponent<MeshRenderer>().sharedMaterial = ModelLibrary.lit;
            var bc = d.AddComponent<BoxCollider>();
            Bounds b = ModelLibrary.bounds[pm];
            bc.center = b.center;
            bc.size = new Vector3(b.size.x, b.size.y, Mathf.Max(b.size.z, 0.03f));
            doors[i] = d.transform;
            // a door hinged on its +x side swings out with a positive turn, on its -x side with a negative one
            sign[i] = ModelLibrary.pivot[pm].x > 0 ? 1f : -1f;
            rendererCount++; colliderCount++;
        }
        var op = go.AddComponent<Openable>();
        op.Init(doors, sign, PropColliders.InsideBounds(def));
        pi.openable = op;
    }

    // ------------------------------------------------------------------ helpers for the game
    public Vector3 CellCentre(int innerIndex)
    {
        return new Vector3((innerIndex % gen.IW + 0.5f) * INNER, 0f, (innerIndex / gen.IW + 0.5f) * INNER);
    }

    public int CellAt(Vector3 p)
    {
        int ix = Mathf.FloorToInt(p.x / INNER), iz = Mathf.FloorToInt(p.z / INNER);
        if (ix < 0 || iz < 0 || ix >= gen.IW || iz >= gen.IH) return -1;
        return iz * gen.IW + ix;
    }

    public Vector3 ReleasePoint()
    {
        return new Vector3((gen.releaseX + 0.5f) * CELL, 0f, (gen.releaseZ + 0.5f) * CELL);
    }

    public float ReleaseYaw()
    {
        return gen.releaseDir * 90f;
    }

    public void ResetProps()
    {
        if (props == null) return;
        for (int i = 0; i < props.Length; i++) if (props[i] != null) props[i].ResetHome();
    }

    public void ShowRoofs(bool show)
    {
        if (roofRoot == null) return;
        var r = roofRoot.GetComponentInChildren<MeshRenderer>();
        if (r != null) r.enabled = show;
    }

    // shortest walkable path over inner cells (4 neighbours); returns cell indices from start to goal, or null
    int[] pathPrev;
    int[] pathQueue;

    public List<int> FindPath(int from, int to, bool throughSolid)
    {
        if (from < 0 || to < 0) return null;
        int n = gen.IW * gen.IH;
        if (pathPrev == null || pathPrev.Length != n) { pathPrev = new int[n]; pathQueue = new int[n]; }
        for (int i = 0; i < n; i++) pathPrev[i] = -2;
        int qh = 0, qt = 0;
        pathPrev[from] = -1; pathQueue[qt++] = from;
        while (qh < qt)
        {
            int i = pathQueue[qh++];
            if (i == to) break;
            int ix = i % gen.IW, iz = i / gen.IW;
            for (int d = 0; d < 4; d++)
            {
                if (!gen.CanStep(ix, iz, d)) continue;
                int j = (iz + MapGen.DZ[d]) * gen.IW + ix + MapGen.DX[d];
                if (pathPrev[j] != -2) continue;
                if (!throughSolid && gen.walk[j] != 1 && j != to) continue;
                pathPrev[j] = i; pathQueue[qt++] = j;
            }
        }
        if (pathPrev[to] == -2) return null;
        var path = new List<int>();
        for (int c = to; c != -1; c = pathPrev[c]) path.Add(c);
        path.Reverse();
        return path;
    }
}
