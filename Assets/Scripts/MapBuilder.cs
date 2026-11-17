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
            // revision 9
            case GenData.R_SHOP_FLOOR: return C(240, 240, 232);
            case GenData.R_STOCKROOM: case GenData.R_PARTS: return C(196, 198, 200);
            case GenData.R_BACK_OFFICE: return C(222, 214, 200);
            case GenData.R_GARAGE_BAY: return C(178, 182, 186);
            case GenData.R_BARN: case GenData.R_FEED_STORE: case GenData.R_FARM_WORK: return C(176, 132, 92);
        }
        return C(220, 220, 220);
    }

    // ---- revision 9: a colour scheme per building, so two houses (and their bedrooms) do not look alike. The
    // scheme comes from the seed and the building's instance, so a seed always looks the same. Interior walls are
    // tinted, wooden floors and the outside walls and roof take one of four looks per kind of building.
    int Style(int inst) { return inst < 0 ? 0 : (int)(GenRng.Hash32(gen.seed, (uint)inst, 9091, 0, 0) & 3); }
    int StyleAt(int cell) { return cell >= 0 && GenData.IsBuilding(gen.cellZone[cell]) ? Style(gen.cellInst[cell]) : 0; }

    static readonly int[] TintR = { 0, 10, -14, 4 }, TintG = { 0, -6, 2, 8 }, TintB = { 0, -14, 10, -10 };
    static Color32 Tint(Color32 c, int style) { return C(Mathf.Clamp(c.r + TintR[style], 0, 255), Mathf.Clamp(c.g + TintG[style], 0, 255), Mathf.Clamp(c.b + TintB[style], 0, 255)); }

    Color32 WallColourAt(int cell) { return Tint(WallColour(gen.roomType[gen.cellRoom[cell]]), StyleAt(cell)); }

    static readonly Color32[] HouseOut = { C(232, 212, 180), C(196, 214, 222), C(204, 216, 186), C(184, 112, 90) };
    static readonly Color32[] HouseRoof = { C(172, 94, 80), C(92, 100, 112), C(84, 104, 92), C(122, 90, 70) };
    static readonly Color32[] ShopOut = { C(240, 222, 150), C(140, 196, 190), C(236, 236, 228), C(200, 160, 196) };
    static readonly Color32[] GarageOut = { C(150, 160, 170), C(176, 104, 84), C(196, 190, 170), C(120, 136, 120) };
    static readonly Color32[] BarnOut = { C(170, 60, 50), C(150, 108, 72), C(186, 82, 58), C(132, 92, 64) };

    static Color32 ExteriorColour(int zone, int style)
    {
        switch (zone)
        {
            case GenData.Z_HOUSE: return HouseOut[style];
            case GenData.Z_DINER: return C(214, 92, 80);
            case GenData.Z_SHOP: return ShopOut[style];
            case GenData.Z_GARAGE: return GarageOut[style];
            case GenData.Z_BARN: return BarnOut[style];
        }
        return C(122, 142, 160);
    }

    static Color32 RoofColour(int zone, int style)
    {
        switch (zone)
        {
            case GenData.Z_HOUSE: return HouseRoof[style];
            case GenData.Z_DINER: return C(232, 230, 222);
            case GenData.Z_SHOP: return C(84, 88, 96);
            case GenData.Z_GARAGE: return C(110, 116, 124);
            case GenData.Z_BARN: return (style & 1) == 0 ? C(78, 82, 90) : C(112, 86, 66);
        }
        return C(100, 106, 116);
    }

    static readonly Color32[] PlankA = { C(198, 154, 106), C(150, 104, 70), C(214, 190, 150), C(170, 160, 150) };
    static readonly Color32[] PlankB = { C(188, 144, 98), C(140, 96, 64), C(204, 178, 140), C(160, 150, 140) };

    Color32 FloorColour(int roomType, int ix, int iz, int style = 0)
    {
        switch (roomType)
        {
            case GenData.R_LIVING: case GenData.R_BEDROOM: case GenData.R_OFFICE: case GenData.R_DINING: case GenData.R_KIDS: case GenData.R_HALL:
            case GenData.R_WH_OFFICE: case GenData.R_BACK_OFFICE: case GenData.R_FEED_STORE: case GenData.R_FARM_WORK:
                // wood planks: strips two cells wide, alternating shade; four kinds of wood (the building's scheme)
                return Vary((((iz / 2) & 1) == 0) ? PlankA[style] : PlankB[style], ix / 4, iz / 2, 4);
            case GenData.R_SHOP_FLOOR:
                return (((ix + iz) & 1) == 0) ? C(232, 232, 226) : C(196, 204, 208);
            case GenData.R_STOCKROOM: case GenData.R_PARTS: case GenData.R_GARAGE_BAY:
                return Vary(C(150, 152, 156), ix, iz, 7);
            case GenData.R_BARN:
                return Vary(C(184, 156, 104), ix, iz, 10);
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
    // Paving takes no part (the G30 amendment): its cells keep their own colour to the corner and the natural
    // ground beside it averages only with natural ground, so a patio, forecourt or yard ends in a clean edge.
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
            case MapGen.GR_WATER: return Vary(C(86, 150, 196), ix, iz, 5);
        }
        return FloorColour(gen.roomType[gen.innerRoom[i]], ix, iz, StyleAt(ix / GenData.INNER_PER_CELL + iz / GenData.INNER_PER_CELL * gen.W));
    }

    bool Blends(int i)
    {
        int g = gen.ground[i];
        return g >= MapGen.GR_LAWN && g != MapGen.GR_RELEASE && g != MapGen.GR_PAVING && g != MapGen.GR_WATER;
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
    // Side A is the low side (south or west of the edge), side B the high side; each has its own colour. The end
    // faces are drawn with their own colours (capLo, capHi: one per side; a cap's alpha 0 = not drawn, where the
    // end meets another piece and could never be seen).
    void WallPiece(bool horizontal, float ex, float ez, float a0, float a1, float y0, float y1, Color32 colA, Color32 colB, bool collider)
    {
        WallPiece(horizontal, ex, ez, a0, a1, y0, y1, colA, colB, collider, colA, colB, colA, colB);
    }

    void WallPiece(bool horizontal, float ex, float ez, float a0, float a1, float y0, float y1, Color32 colA, Color32 colB, bool collider,
                   Color32 loA, Color32 loB, Color32 hiA, Color32 hiB)
    {
        Vector3 mnA, mxA, mnB, mxB;
        int lo, hi;
        if (horizontal)
        {
            mnA = new Vector3(ex + a0, y0, ez - WALL_HALF); mxA = new Vector3(ex + a1, y1, ez);
            mnB = new Vector3(ex + a0, y0, ez); mxB = new Vector3(ex + a1, y1, ez + WALL_HALF);
            walls.Box(mnA, mxA, colA, MeshBuf.FACE_ALL & ~(MeshBuf.FACE_ZP | MeshBuf.FACE_XN | MeshBuf.FACE_XP));
            walls.Box(mnB, mxB, colB, MeshBuf.FACE_ALL & ~(MeshBuf.FACE_ZN | MeshBuf.FACE_XN | MeshBuf.FACE_XP));
            lo = MeshBuf.FACE_XN; hi = MeshBuf.FACE_XP;
        }
        else
        {
            mnA = new Vector3(ex - WALL_HALF, y0, ez + a0); mxA = new Vector3(ex, y1, ez + a1);
            mnB = new Vector3(ex, y0, ez + a0); mxB = new Vector3(ex + WALL_HALF, y1, ez + a1);
            walls.Box(mnA, mxA, colA, MeshBuf.FACE_ALL & ~(MeshBuf.FACE_XP | MeshBuf.FACE_ZN | MeshBuf.FACE_ZP));
            walls.Box(mnB, mxB, colB, MeshBuf.FACE_ALL & ~(MeshBuf.FACE_XN | MeshBuf.FACE_ZN | MeshBuf.FACE_ZP));
            lo = MeshBuf.FACE_ZN; hi = MeshBuf.FACE_ZP;
        }
        if (loA.a != 0) walls.Box(mnA, mxA, loA, lo);
        if (loB.a != 0) walls.Box(mnB, mxB, loB, lo);
        if (hiA.a != 0) walls.Box(mnA, mxA, hiA, hi);
        if (hiB.a != 0) walls.Box(mnB, mxB, hiB, hi);
        if (collider) Collider(mnA, mxB, "Wall");
    }

    // ---- corners (G40). Every edge's wall used to run 10 cm past both cell corners, so where walls met the end
    // of one lay in the plane of the other's face, in two colours: they flickered. Now each corner's 20 x 20 cm
    // square of wall belongs to exactly one wall: the horizontal (east-west) wall that starts there, else the one
    // that ends there, else the vertical wall that starts there, else the one that ends there. Every other wall at
    // the corner stops at the square's edge, and the walls of a straight run meet end to end without overlap.
    static bool IsWallType(int t) { return t == GenData.E_WALL || t == GenData.E_DOOR || t == GenData.E_WINDOW; }

    // a wall on the east-west edge from grid corner (vx, vz) to (vx + 1, vz)?
    bool WallH(int vx, int vz)
    {
        if (gen.InGrid(vx, vz) && gen.cellZone[vz * gen.W + vx] != GenData.Z_NONE) return IsWallType(gen.EdgeType(vx, vz, 2));
        if (gen.InGrid(vx, vz - 1) && gen.cellZone[(vz - 1) * gen.W + vx] != GenData.Z_NONE) return IsWallType(gen.EdgeType(vx, vz - 1, 0));
        return false;
    }

    // a wall on the north-south edge from grid corner (vx, vz) to (vx, vz + 1)?
    bool WallV(int vx, int vz)
    {
        if (gen.InGrid(vx, vz) && gen.cellZone[vz * gen.W + vx] != GenData.Z_NONE) return IsWallType(gen.EdgeType(vx, vz, 3));
        if (gen.InGrid(vx - 1, vz) && gen.cellZone[vz * gen.W + vx - 1] != GenData.Z_NONE) return IsWallType(gen.EdgeType(vx - 1, vz, 1));
        return false;
    }

    // the colour of a wall's end face that looks into cell (cx, cz): that room's wall colour, or the building's
    // outside colour when the cell is outdoors
    Color32 CapColour(int cx, int cz, int buildingZone)
    {
        if (gen.InGrid(cx, cz))
        {
            int c = cz * gen.W + cx;
            if (GenData.IsBuilding(gen.cellZone[c])) return WallColourAt(c);
        }
        return ExteriorColour(buildingZone, capStyle);
    }

    // trim around an opening: sticks out 2 cm on both faces
    void Trim(bool horizontal, float ex, float ez, float a0, float a1, float y0, float y1, Color32 col)
    {
        // a jamb that stands on the floor has no bottom face (it would lie in the plane of the wall's)
        int faces = y0 <= 0f ? MeshBuf.FACE_ALL & ~MeshBuf.FACE_YN : MeshBuf.FACE_ALL;
        float t = WALL_HALF + 0.02f;
        if (horizontal) walls.Box(new Vector3(ex + a0, y0, ez - t), new Vector3(ex + a1, y1, ez + t), col, faces);
        else walls.Box(new Vector3(ex - t, y0, ez + a0), new Vector3(ex + t, y1, ez + a1), col, faces);
    }

    int capStyle;   // the scheme of the building whose wall Edge is drawing (CapColour's outside colour)

    // Revision 9: the big doors drawn on an outside wall: a garage bay's roller door on its wall to the forecourt,
    // a barn's double doors on its wall to the farmyard. Drawn on the outside face, standing 3 cm proud of it (no
    // face in the wall's plane, G40); the people's door is the plan's own door beside it.
    void BigDoor(bool horizontal, float ex, float ez, int cellA, int cellB)
    {
        for (int side = 0; side < 2; side++)
        {
            int inC = side == 0 ? cellA : cellB, outC = side == 0 ? cellB : cellA;
            if (inC < 0 || outC < 0 || !GenData.IsBuilding(gen.cellZone[inC]) || GenData.IsBuilding(gen.cellZone[outC])) continue;
            int rt = gen.roomType[gen.cellRoom[inC]], ot = gen.roomType[gen.cellRoom[outC]];
            bool roller = rt == GenData.R_GARAGE_BAY && ot == GenData.R_FORECOURT, barn = rt == GenData.R_BARN && ot == GenData.R_FARMYARD;
            if (!roller && !barn) continue;
            // the outside face: side A (low) is south or west of the edge, so its outside is +, side B's is -
            float s = side == 0 ? 1f : -1f, face = WALL_HALF * s, t = 0.03f * s;
            float a0 = 0.45f, a1 = CELL - 0.45f, top = roller ? 2.6f : 2.9f;
            Color32 panel = roller ? C(206, 210, 214) : C(150, 54, 44), rib = roller ? C(170, 176, 182) : C(236, 230, 214);
            Panel(horizontal, ex, ez, a0, a1, 0f, top, face, face + t, panel);
            if (roller)
            {
                // the slats: a rib every 25 cm, standing 1 cm proud of the door
                for (float y = 0.3f; y < top - 0.05f; y += 0.25f) Panel(horizontal, ex, ez, a0, a1, y, y + 0.04f, face + t, face + t + 0.01f * s, rib);
            }
            else
            {
                // two leaves: a frame round each and a rail across, in white
                float mid = (a0 + a1) / 2;
                float[] xs = { a0, mid - 0.04f, mid + 0.04f, a1 - 0.08f };
                // the uprights stop at the rails (no two strips overlap in one plane, G40)
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

    // a flat box on a wall's face: from a0 to a1 along the edge, y0 to y1, and from n0 to n1 off the wall line (no
    // face toward the wall: it lies on the wall or on the panel under it)
    void Panel(bool horizontal, float ex, float ez, float a0, float a1, float y0, float y1, float n0, float n1, Color32 col)
    {
        float lo = Mathf.Min(n0, n1), hi = Mathf.Max(n0, n1);
        bool outPlus = n1 > n0;
        if (horizontal) walls.Box(new Vector3(ex + a0, y0, ez + lo), new Vector3(ex + a1, y1, ez + hi), col, MeshBuf.FACE_ALL & ~(outPlus ? MeshBuf.FACE_ZN : MeshBuf.FACE_ZP));
        else walls.Box(new Vector3(ex + lo, y0, ez + a0), new Vector3(ex + hi, y1, ez + a1), col, MeshBuf.FACE_ALL & ~(outPlus ? MeshBuf.FACE_XN : MeshBuf.FACE_XP));
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
        int styleA = StyleAt(cellA), styleB = StyleAt(cellB);
        Color32 colA = GenData.IsBuilding(zoneA) ? WallColourAt(cellA) : ExteriorColour(zoneB, styleB);
        Color32 colB = GenData.IsBuilding(zoneB) ? WallColourAt(cellB) : ExteriorColour(zoneA, styleA);
        capStyle = GenData.IsBuilding(zoneA) ? styleA : styleB;
        // the ends: how far past the cell corners this wall runs (it owns the corner square or stops at its edge),
        // and the colours of the end faces that can be seen (alpha 0: none)
        int vx = cx, vz = cz, bz = GenData.IsBuilding(zoneA) ? zoneA : zoneB;
        float lo, hi;
        Color32 none = new Color32(0, 0, 0, 0), loA = none, loB = none, hiA = none, hiB = none;
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
        if (type == GenData.E_WALL)
        {
            WallPiece(horizontal, ex, ez, lo, hi, 0f, h, colA, colB, true, loA, loB, hiA, hiB);
            BigDoor(horizontal, ex, ez, cellA, cellB);
        }
        else if (type == GenData.E_DOOR)
        {
            float d0 = off * INNER, d1 = (off + GenData.DOOR_CELLS) * INNER, dh = GenData.DOOR_HEIGHT_MM / 1000f;
            WallPiece(horizontal, ex, ez, lo, d0, 0f, h, colA, colB, true, loA, loB, colA, colB);
            WallPiece(horizontal, ex, ez, d1, hi, 0f, h, colA, colB, true, colA, colB, hiA, hiB);
            WallPiece(horizontal, ex, ez, d0, d1, dh, h, colA, colB, true, none, none, none, none);
            // trims stand a little proud of the wall pieces they frame; the jambs stop where the head begins, so no
            // two trims share a face
            float e = 0.004f;
            Trim(horizontal, ex, ez, d0 - 0.06f, d0 + e, 0f, dh - e, Frame);
            Trim(horizontal, ex, ez, d1 - e, d1 + 0.06f, 0f, dh - e, Frame);
            Trim(horizontal, ex, ez, d0 - 0.06f, d1 + 0.06f, dh - e, dh + 0.06f, Frame);
        }
        else if (type == GenData.E_WINDOW)
        {
            float w0 = 1.0f, w1 = 3.0f, sill = 0.9f, head = 2.05f;
            WallPiece(horizontal, ex, ez, lo, w0, 0f, h, colA, colB, false, loA, loB, colA, colB);
            WallPiece(horizontal, ex, ez, w1, hi, 0f, h, colA, colB, false, colA, colB, hiA, hiB);
            WallPiece(horizontal, ex, ez, w0, w1, 0f, sill, colA, colB, false, none, none, none, none);
            WallPiece(horizontal, ex, ez, w0, w1, head, h, colA, colB, false, none, none, none, none);
            float e = 0.004f;
            Trim(horizontal, ex, ez, w0 - 0.05f, w1 + 0.05f, sill - 0.05f, sill + e, WindowFrame);
            Trim(horizontal, ex, ez, w0 - 0.05f, w1 + 0.05f, head - e, head + 0.05f, WindowFrame);
            Trim(horizontal, ex, ez, w0 - 0.05f, w0 + e, sill + e, head - e, WindowFrame);
            Trim(horizontal, ex, ez, w1 - e, w1 + 0.05f, sill + e, head - e, WindowFrame);
            Trim(horizontal, ex, ez, (w0 + w1) / 2 - 0.02f, (w0 + w1) / 2 + 0.02f, sill + e, head - e, WindowFrame);
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
            // flat roof with a ceiling colour underneath. The roof of each cell meets its building neighbours' at the
            // cell line and reaches 10 cm out over an outside wall; no two pieces overlap (G40): the cell's own piece
            // takes the east and west overhangs, a strip takes the north or south overhang, and a strip stops 10 cm
            // short where the diagonal cell's piece already covers the corner (an inside corner of an L)
            float h = GenData.ZoneWallMm[zone] / 1000f, t = h + 0.14f;
            bool bw = Bld(x - 1, z), be = Bld(x + 1, z), bs = Bld(x, z - 1), bn = Bld(x, z + 1);
            float x0 = x * CELL - (bw ? 0f : WALL_HALF), x1 = (x + 1) * CELL + (be ? 0f : WALL_HALF);
            Color32 rc = RoofColour(zone, Style(gen.cellInst[c]));
            roofs.Box(new Vector3(x0, h, z * CELL), new Vector3(x1, t, (z + 1) * CELL), rc, rc, Ceiling);
            for (int side = -1; side <= 1; side += 2)
            {
                if (side < 0 ? bs : bn) continue;
                int dz = side < 0 ? z - 1 : z + 1;
                float s0 = bw ? (Bld(x - 1, dz) ? x * CELL + WALL_HALF : x * CELL) : x * CELL - WALL_HALF;
                float s1 = be ? (Bld(x + 1, dz) ? (x + 1) * CELL - WALL_HALF : (x + 1) * CELL) : (x + 1) * CELL + WALL_HALF;
                float zz0 = side < 0 ? z * CELL - WALL_HALF : (z + 1) * CELL, zz1 = side < 0 ? z * CELL : (z + 1) * CELL + WALL_HALF;
                roofs.Box(new Vector3(s0, h, zz0), new Vector3(s1, t, zz1), rc, rc, Ceiling);
            }
            Vector3 mn = new Vector3(x * CELL - WALL_HALF, h, z * CELL - WALL_HALF), mx = new Vector3((x + 1) * CELL + WALL_HALF, t, (z + 1) * CELL + WALL_HALF);
            var go = new GameObject("Roof");
            go.transform.SetParent(roofRoot, false);
            go.layer = Layers.Arch;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = (mn + mx) * 0.5f; bc.size = mx - mn;
            colliderCount++;
        }
    }

    bool Bld(int x, int z) { return gen.InGrid(x, z) && GenData.IsBuilding(gen.cellZone[z * gen.W + x]); }

    // G40 check: coplanar faces of the walls, the roofs and the ground that overlap (see MeshBuf.Coplanar)
    public string ZCheck()
    {
        if (walls == null) return "no map";
        return "walls " + MeshBuf.Coplanar(walls) + " | roofs " + MeshBuf.Coplanar(roofs) + " | ground " + MeshBuf.Coplanar(ground);
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
        // ground cover cannot be pointed at (no collider, no PropInstance); everything else can, and is a disguise (G46)
        if ((fl & GenData.FL_DECOR) != 0) return;
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

    public List<int> FindPath(int from, int to, bool throughSolid) { return FindPath(from, to, throughSolid, false); }

    // throughProps: a hunter's path (G45) crosses cells taken by props, trees included
    public List<int> FindPath(int from, int to, bool throughSolid, bool throughProps)
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
                if (!throughSolid && gen.walk[j] != 1 && j != to && !(throughProps && gen.walk[j] == 2)) continue;
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
