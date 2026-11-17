using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Turns the prototype's model blob (Resources/PropHunt/models.bytes) into assets Udon can use, since Udon cannot
// read the blob or add components at runtime:
//   Assets/Udon/Generated/Models.asset          every model as a Mesh (sub-assets, named after GenModels.Name)
//   Assets/Udon/Generated/PropLit.mat           the one vertex-coloured material (shader PropHunt/Lit)
//   Assets/Udon/Generated/Props/<def>.prefab    one prefab per prop definition: mesh, scale, the box colliders of
//                                               PropColliders.Build, and the door or lid parts of openables as
//                                               children named "door0", "door1", ... (hinge at the child's origin)
// MapBuilderUdon instantiates the prefabs by def index (its propPrefabs array, filled by this baker).
// Menu PropHunt > Bake prop prefabs. Run it after the models or the defs change.
public static class PropPrefabBaker
{
    const string OUT = "Assets/Udon/Generated";
    public const int LAYER_PROP = 11;   // VRChat "Environment"; switched per client at runtime (hunters walk through)

    static Mesh[] mesh;
    static Bounds[] bounds;
    static Vector3[] pivot;
    static int[] parent;

    [MenuItem("PropHunt/Bake prop prefabs")]
    public static void Bake()
    {
        GenData.Build();
        Directory.CreateDirectory(OUT + "/Props");
        Directory.CreateDirectory(OUT + "/Fixtures");
        AssetDatabase.Refresh();
        LoadModels();

        var mat = AssetDatabase.LoadAssetAtPath<Material>(OUT + "/PropLit.mat");
        if (mat == null)
        {
            mat = new Material(Shader.Find("PropHunt/Lit")) { enableInstancing = true };
            AssetDatabase.CreateAsset(mat, OUT + "/PropLit.mat");
        }

        // all meshes in one asset file
        string modelsPath = OUT + "/Models.asset";
        AssetDatabase.DeleteAsset(modelsPath);
        AssetDatabase.CreateAsset(mesh[0], modelsPath);
        for (int m = 1; m < mesh.Length; m++) AssetDatabase.AddObjectToAsset(mesh[m], mesh[0]);
        AssetDatabase.SaveAssets();

        var prefabs = new GameObject[GenData.nDefs];
        for (int d = 0; d < GenData.nDefs; d++)
            prefabs[d] = MakePrefab(d, mat);

        var fence = new GameObject("fence");
        int fm = Array.IndexOf(GenModels.Name, "fence_planks");
        fence.AddComponent<MeshFilter>().sharedMesh = mesh[fm];
        fence.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var fencePrefab = PrefabUtility.SaveAsPrefabAsset(fence, OUT + "/Fence.prefab");
        UnityEngine.Object.DestroyImmediate(fence);

        // a space base's fence (more-places): the "rail" model, fitted inside the plank fence's bounds so the builder
        // stretches it the same way
        GameObject railPrefab = null;
        int rm = Array.IndexOf(GenModels.Name, "rail");
        if (rm >= 0)
        {
            var rail = new GameObject("rail");
            var inner = new GameObject("mesh");
            inner.transform.SetParent(rail.transform, false);
            Bounds fb = bounds[fm], rb = bounds[rm];
            Vector3 s = new Vector3(fb.size.x / Mathf.Max(1e-4f, rb.size.x), fb.size.y / Mathf.Max(1e-4f, rb.size.y), fb.size.z / Mathf.Max(1e-4f, rb.size.z));
            inner.transform.localScale = s;
            inner.transform.localPosition = fb.center - Vector3.Scale(rb.center, s);
            inner.AddComponent<MeshFilter>().sharedMesh = mesh[rm];
            inner.AddComponent<MeshRenderer>().sharedMaterial = mat;
            railPrefab = PrefabUtility.SaveAsPrefabAsset(rail, OUT + "/Rail.prefab");
            UnityEngine.Object.DestroyImmediate(rail);
        }

        AssetDatabase.SaveAssets();
        // the light fixtures (fixtures.bytes, Tools/make_fixtures.py): ceiling, wall, street x ordinary, space, medieval
        var fixtures = BakeFixtures(mat);
        AssetDatabase.SaveAssets();
        MapBuilderBaker.Assign(prefabs, fencePrefab, mat, bounds[fm], railPrefab, fixtures);
        Debug.Log("[prophunt] baked " + mesh.Length + " meshes and " + prefabs.Length + " prop prefabs");
    }

    static GameObject MakePrefab(int def, Material mat)
    {
        int model = GenData.defModel[def];
        float sc = GenData.defScale[def] / 1000f;
        var go = new GameObject(GenData.defId[def]);
        go.transform.localScale = new Vector3(sc, sc, sc);
        go.AddComponent<MeshFilter>().sharedMesh = mesh[model];
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.layer = LAYER_PROP;
        BuildColliders(go, def, model, sc);
        // knockable props carry a kinematic rigidbody that a dart's knock switches on (PHKnock)
        int fl = GenData.defFlags[def];
        if ((fl & (GenData.FL_DECOR | GenData.FL_HEAVY | GenData.FL_FIXTURE | GenData.FL_SCENERY)) == 0)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.mass = GenData.defMassKg[def];
            rb.drag = 0.2f; rb.angularDrag = 0.6f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.interpolation = RigidbodyInterpolation.None;   // interpolation would pull a fresh instance back to the origin
        }
        int k = 0;
        if ((GenData.defFlags[def] & GenData.FL_OPENABLE) != 0)
            for (int pm = 0; pm < mesh.Length; pm++)
            {
                if (parent[pm] != model) continue;
                var d = new GameObject("door" + k++);
                d.transform.SetParent(go.transform, false);
                d.transform.localPosition = pivot[pm];
                d.layer = LAYER_PROP;
                d.AddComponent<MeshFilter>().sharedMesh = mesh[pm];
                d.AddComponent<MeshRenderer>().sharedMaterial = mat;
                var bc = d.AddComponent<BoxCollider>();
                Bounds b = bounds[pm];
                bc.center = b.center;
                bc.size = new Vector3(b.size.x, b.size.y, Mathf.Max(b.size.z, 0.03f));
            }
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, OUT + "/Props/" + def.ToString("000") + "_" + GenData.defId[def] + ".prefab");
        UnityEngine.Object.DestroyImmediate(go);
        return prefab;
    }

    // ------------------------------------------------------------------ the blob (as ModelLibrary.Load)
    static readonly string[] FixtureNames = {
        "fx_ceiling_modern", "fx_ceiling_space", "fx_ceiling_medieval",
        "fx_wall_modern", "fx_wall_space", "fx_wall_medieval",
        "fx_street_modern", "fx_street_space", "fx_street_medieval" };

    static GameObject[] BakeFixtures(Material mat)
    {
        var result = new GameObject[FixtureNames.Length];
        var asset = Resources.Load<TextAsset>("PropHunt/fixtures");
        if (asset == null) { Debug.LogWarning("[prophunt] fixtures.bytes is missing: fixtures are drawn from boxes"); return result; }
        byte[] b = asset.bytes;
        int p = 4, n = BitConverter.ToInt32(b, p); p += 4;
        string meshPath = OUT + "/Fixtures.asset";
        AssetDatabase.DeleteAsset(meshPath);
        Mesh first = null;
        for (int m = 0; m < n; m++)
        {
            int nameLen = b[p++];
            string name = System.Text.Encoding.UTF8.GetString(b, p, nameLen); p += nameLen;
            int parLen = b[p++]; p += parLen;
            Vector3 light = V(b, p); p += 12;
            Vector3 mn = V(b, p); p += 12;
            Vector3 mx = V(b, p); p += 12;
            int nv = BitConverter.ToInt32(b, p); p += 4;
            int ni = BitConverter.ToInt32(b, p); p += 4;
            var verts = new Vector3[nv]; var norms = new Vector3[nv]; var cols = new Color32[nv];
            for (int i = 0; i < nv; i++)
            {
                verts[i] = V(b, p);
                norms[i] = new Vector3((sbyte)b[p + 12] / 127f, (sbyte)b[p + 13] / 127f, (sbyte)b[p + 14] / 127f);
                cols[i] = new Color32(b[p + 15], b[p + 16], b[p + 17], 255);
                p += 19;
            }
            var tris = new int[ni];
            for (int i = 0; i < ni; i++) { tris[i] = BitConverter.ToUInt16(b, p); p += 2; }
            var me = new Mesh { name = name };
            me.vertices = verts; me.normals = norms; me.colors32 = cols; me.triangles = tris;
            me.RecalculateBounds();
            if (first == null) { first = me; AssetDatabase.CreateAsset(me, meshPath); } else AssetDatabase.AddObjectToAsset(me, first);

            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = me;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var lp = new GameObject("LightPoint");
            lp.transform.SetParent(go.transform, false);
            lp.transform.localPosition = light;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, OUT + "/Fixtures/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(go);
            int k = Array.IndexOf(FixtureNames, name);
            if (k >= 0) result[k] = prefab;
        }
        return result;
    }

    static void LoadModels()
    {
        var asset = Resources.Load<TextAsset>("PropHunt/models");
        if (asset == null) throw new Exception("models.bytes is missing from Resources/PropHunt");
        byte[] b = asset.bytes;
        if (b[0] != 'P' || b[1] != 'H' || b[2] != 'M' || b[3] != '1') throw new Exception("models.bytes: bad header");
        int p = 4;
        int n = BitConverter.ToInt32(b, p); p += 4;
        // models after the blob's are made here from boxes (GenModels lists them last: the gas station's)
        int total = GenModels.Name.Length;
        if (n > total) throw new Exception("models.bytes does not match GenModels.cs");
        mesh = new Mesh[total]; bounds = new Bounds[total]; pivot = new Vector3[total]; parent = new int[total];
        var parName = new string[n];
        for (int m = 0; m < n; m++)
        {
            int nameLen = b[p++];
            string name = System.Text.Encoding.UTF8.GetString(b, p, nameLen); p += nameLen;
            int parLen = b[p++];
            parName[m] = System.Text.Encoding.UTF8.GetString(b, p, parLen); p += parLen;
            pivot[m] = V(b, p); p += 12;
            Vector3 mn = V(b, p); p += 12;
            Vector3 mx = V(b, p); p += 12;
            int nv = BitConverter.ToInt32(b, p); p += 4;
            int ni = BitConverter.ToInt32(b, p); p += 4;
            var verts = new Vector3[nv]; var norms = new Vector3[nv]; var cols = new Color32[nv];
            for (int i = 0; i < nv; i++)
            {
                verts[i] = V(b, p);
                norms[i] = new Vector3((sbyte)b[p + 12] / 127f, (sbyte)b[p + 13] / 127f, (sbyte)b[p + 14] / 127f);
                cols[i] = new Color32(b[p + 15], b[p + 16], b[p + 17], 255);
                p += 19;
            }
            var tris = new int[ni];
            if (nv < 65536) { for (int i = 0; i < ni; i++) { tris[i] = BitConverter.ToUInt16(b, p); p += 2; } }
            else { for (int i = 0; i < ni; i++) { tris[i] = BitConverter.ToInt32(b, p); p += 4; } }
            var me = new Mesh { name = name };
            if (nv >= 65536) me.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            me.vertices = verts; me.normals = norms; me.colors32 = cols; me.triangles = tris;
            var bd = new Bounds(); bd.SetMinMax(mn, mx);
            me.bounds = bd;
            mesh[m] = me; bounds[m] = bd;
        }
        for (int m = 0; m < n; m++) parent[m] = parName[m].Length == 0 ? -1 : Array.IndexOf(GenModels.Name, parName[m]);
        for (int m = n; m < total; m++)
        {
            mesh[m] = MakeModel(GenModels.Name[m]);
            bounds[m] = mesh[m].bounds;
            pivot[m] = Vector3.zero; parent[m] = -1;
        }
    }

    // ------------------------------------------------------------------ generated models (vertex colours, base at y 0)
    static System.Collections.Generic.List<Vector3> gv, gn;
    static System.Collections.Generic.List<Color32> gc;
    static System.Collections.Generic.List<int> gt;

    static void GQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color32 col)
    {
        int s = gv.Count;
        gv.Add(a); gv.Add(b); gv.Add(c); gv.Add(d);
        for (int i = 0; i < 4; i++) { gn.Add(n); gc.Add(col); }
        gt.Add(s); gt.Add(s + 1); gt.Add(s + 2); gt.Add(s); gt.Add(s + 2); gt.Add(s + 3);
    }

    // an axis-aligned box from min to max (metres)
    static void GBox(Vector3 mn, Vector3 mx, Color32 col)
    {
        float x0 = mn.x, y0 = mn.y, z0 = mn.z, x1 = mx.x, y1 = mx.y, z1 = mx.z;
        GQuad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, col);
        GQuad(new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), Vector3.back, col);
        GQuad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up, col);
        GQuad(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), Vector3.down, col);
        GQuad(new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), Vector3.right, col);
        GQuad(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left, col);
    }

    static Color32 Col(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }

    static Mesh MakeModel(string name)
    {
        gv = new System.Collections.Generic.List<Vector3>(); gn = new System.Collections.Generic.List<Vector3>();
        gc = new System.Collections.Generic.List<Color32>(); gt = new System.Collections.Generic.List<int>();
        if (name == "gen_fuelPump")
        {
            // 0.6 x 1.7 x 0.4: a plinth, a red body with a white head, a dark screen, a hose and nozzle on the side
            GBox(new Vector3(-0.30f, 0f, -0.20f), new Vector3(0.30f, 0.12f, 0.20f), Col(150, 150, 150));
            GBox(new Vector3(-0.26f, 0.12f, -0.16f), new Vector3(0.26f, 1.25f, 0.16f), Col(200, 56, 48));
            GBox(new Vector3(-0.28f, 1.25f, -0.18f), new Vector3(0.28f, 1.70f, 0.18f), Col(236, 236, 232));
            GBox(new Vector3(-0.16f, 1.34f, -0.185f), new Vector3(0.16f, 1.58f, -0.18f), Col(40, 44, 52));
            GBox(new Vector3(-0.12f, 0.70f, -0.165f), new Vector3(0.12f, 0.90f, -0.16f), Col(230, 200, 60));
            GBox(new Vector3(0.26f, 0.55f, -0.04f), new Vector3(0.30f, 1.10f, 0.04f), Col(30, 30, 34));
            GBox(new Vector3(0.26f, 1.02f, -0.06f), new Vector3(0.36f, 1.12f, 0.06f), Col(30, 30, 34));
        }
        else if (name == "gen_car")
        {
            // 1.8 x 1.4 x 4.0: wheels, a body, a cabin with glass, lights
            Color32 paint = Col(60, 110, 170), glass = Col(150, 190, 210), tyre = Col(28, 28, 30), trim = Col(210, 210, 214);
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? -0.90f : 0.62f, z = (i & 2) == 0 ? -1.45f : 0.95f;
                GBox(new Vector3(x, 0f, z), new Vector3(x + 0.28f, 0.62f, z + 0.62f), tyre);
            }
            GBox(new Vector3(-0.88f, 0.28f, -1.98f), new Vector3(0.88f, 0.86f, 1.98f), paint);
            GBox(new Vector3(-0.80f, 0.86f, -0.95f), new Vector3(0.80f, 1.40f, 0.85f), paint);
            GBox(new Vector3(-0.81f, 0.92f, -0.90f), new Vector3(0.81f, 1.32f, 0.80f), glass);
            GBox(new Vector3(-0.75f, 0.94f, 0.80f), new Vector3(0.75f, 1.30f, 0.86f), glass);
            GBox(new Vector3(-0.75f, 0.94f, -0.96f), new Vector3(0.75f, 1.30f, -0.90f), glass);
            GBox(new Vector3(-0.70f, 0.58f, 1.98f), new Vector3(-0.40f, 0.72f, 2.00f), Col(250, 240, 200));
            GBox(new Vector3(0.40f, 0.58f, 1.98f), new Vector3(0.70f, 0.72f, 2.00f), Col(250, 240, 200));
            GBox(new Vector3(-0.70f, 0.58f, -2.00f), new Vector3(-0.40f, 0.72f, -1.98f), Col(200, 40, 40));
            GBox(new Vector3(0.40f, 0.58f, -2.00f), new Vector3(0.70f, 0.72f, -1.98f), Col(200, 40, 40));
            GBox(new Vector3(-0.90f, 0.30f, -2.00f), new Vector3(0.90f, 0.42f, 2.00f), trim);
        }
        else if (name == "gen_tireStack")
        {
            // 0.65 x 0.8 x 0.65: four tyres, each two crossed boxes, slightly off-centre
            for (int i = 0; i < 4; i++)
            {
                float y0 = i * 0.2f, o = (i % 2 == 0 ? 0.01f : -0.01f);
                GBox(new Vector3(-0.32f + o, y0, -0.22f), new Vector3(0.32f + o, y0 + 0.19f, 0.22f), Col(32, 32, 34));
                GBox(new Vector3(-0.22f + o, y0, -0.32f), new Vector3(0.22f + o, y0 + 0.19f, 0.32f), Col(32, 32, 34));
                GBox(new Vector3(-0.12f + o, y0 + 0.19f, -0.12f), new Vector3(0.12f + o, y0 + 0.191f, 0.12f), Col(70, 70, 74));
            }
        }
        var m = new Mesh { name = name };
        m.SetVertices(gv); m.SetNormals(gn); m.SetColors(gc); m.SetTriangles(gt, 0);
        m.RecalculateBounds();
        return m;
    }

    static Vector3 V(byte[] b, int p) { return new Vector3(BitConverter.ToSingle(b, p), BitConverter.ToSingle(b, p + 4), BitConverter.ToSingle(b, p + 8)); }

    // ------------------------------------------------------------------ colliders (as PropColliders.Build)
    static void Add(GameObject go, Vector3 mn, Vector3 mx)
    {
        var c = go.AddComponent<BoxCollider>();
        c.center = (mn + mx) * 0.5f;
        c.size = new Vector3(Mathf.Max(0.001f, mx.x - mn.x), Mathf.Max(0.001f, mx.y - mn.y), Mathf.Max(0.001f, mx.z - mn.z));
    }

    static void BuildColliders(GameObject go, int def, int model, float sc)
    {
        int kind = GenData.defCollider[def];
        int fl = GenData.defFlags[def];
        Bounds b = bounds[model];
        Vector3 mn = b.min, mx = b.max;
        float top = GenData.defTopY[def] / 1000f / sc;
        float cm = 0.01f / sc;
        if ((fl & GenData.FL_DECOR) != 0) return;
        switch (kind)
        {
            case GenData.C_TABLE:
            {
                float slab = Mathf.Max(0.1f * (mx.y - mn.y), 5f * cm);
                float bottom = Mathf.Max(mn.y, top - slab);
                Add(go, new Vector3(mn.x, bottom, mn.z), new Vector3(mx.x, Mathf.Max(top, mx.y), mx.z));
                float leg = Mathf.Max(0.09f * Mathf.Min(mx.x - mn.x, mx.z - mn.z), 5f * cm);
                float inset = 2f * cm;
                for (int i = 0; i < 4; i++)
                {
                    float x0 = (i & 1) == 0 ? mn.x + inset : mx.x - inset - leg;
                    float z0 = (i & 2) == 0 ? mn.z + inset : mx.z - inset - leg;
                    Add(go, new Vector3(x0, mn.y, z0), new Vector3(x0 + leg, bottom, z0 + leg));
                }
                break;
            }
            case GenData.C_SEAT:
            {
                float seat = Mathf.Clamp(top, mn.y + 5f * cm, mx.y);
                Add(go, mn, new Vector3(mx.x, seat, mx.z));
                Add(go, new Vector3(mn.x, seat, mn.z), new Vector3(mx.x, mx.y, mn.z + 0.25f * (mx.z - mn.z)));
                break;
            }
            case GenData.C_HOLLOW:
            {
                float t = Mathf.Max(4f * cm, 0.04f * (mx.x - mn.x));
                Add(go, mn, new Vector3(mn.x + t, mx.y, mx.z));
                Add(go, new Vector3(mx.x - t, mn.y, mn.z), mx);
                Add(go, mn, new Vector3(mx.x, mx.y, mn.z + t));
                Add(go, new Vector3(mn.x, mx.y - t, mn.z), mx);
                Add(go, mn, new Vector3(mx.x, mn.y + Mathf.Max(t, InsideFloor(model)), mx.z));
                break;
            }
            case GenData.C_SHELF:
            {
                bool generated = (fl & GenData.FL_GENERATED) != 0;
                int a = GenModels.SurfStart[model], e = GenModels.SurfStart[model + 1];
                int maxArea = 0;
                for (int i = a; i < e; i++) if (GenModels.Surf[i * 6 + 5] > maxArea) maxArea = GenModels.Surf[i * 6 + 5];
                for (int i = a; i < e; i++)
                {
                    if (GenModels.Surf[i * 6 + 5] * 2 < maxArea) continue;
                    float y = GenModels.Surf[i * 6] / 1000f;
                    Add(go, new Vector3(mn.x, y - 3f * cm, mn.z), new Vector3(mx.x, y, mx.z));
                }
                if (!generated)
                {
                    float t = 3f * cm;
                    Add(go, mn, new Vector3(mn.x + t, mx.y, mx.z));
                    Add(go, new Vector3(mx.x - t, mn.y, mn.z), mx);
                    Add(go, mn, new Vector3(mx.x, mx.y, mn.z + t));
                }
                break;
            }
            case GenData.C_TRUNK:
            {
                float r = 0.22f / sc;
                Add(go, new Vector3(-r, mn.y, -r), new Vector3(r, mx.y, r));
                break;
            }
            case GenData.C_TENT:
            {
                float t = 6f * cm;
                float h = mx.y - mn.y;
                Add(go, mn, new Vector3(mx.x, mn.y + 0.75f * h, mn.z + t));
                Add(go, mn, new Vector3(mn.x + t, mn.y + 0.5f * h, mx.z));
                Add(go, new Vector3(mx.x - t, mn.y, mn.z), new Vector3(mx.x, mn.y + 0.5f * h, mx.z));
                break;
            }
            case GenData.C_NONE:
                Add(go, mn, new Vector3(mx.x, mn.y + 3f * cm, mx.z));
                break;
            default:
                Add(go, mn, new Vector3(mx.x, Mathf.Max(mx.y, mn.y + 2f * cm), mx.z));
                break;
        }
    }

    static float InsideFloor(int model)
    {
        int a = GenModels.SurfStart[model], e = GenModels.SurfStart[model + 1];
        float best = 0f;
        float h = GenModels.SizeMm[model * 3 + 1] / 1000f;
        for (int i = a; i < e; i++)
        {
            float y = GenModels.Surf[i * 6] / 1000f;
            if (y > 0.01f && y < 0.3f * h && y > best) best = y;
        }
        return best;
    }
}
