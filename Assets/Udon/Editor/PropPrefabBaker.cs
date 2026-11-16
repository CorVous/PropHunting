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

        AssetDatabase.SaveAssets();
        MapBuilderBaker.Assign(prefabs, fencePrefab, mat, bounds[fm]);
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
    static void LoadModels()
    {
        var asset = Resources.Load<TextAsset>("PropHunt/models");
        if (asset == null) throw new Exception("models.bytes is missing from Resources/PropHunt");
        byte[] b = asset.bytes;
        if (b[0] != 'P' || b[1] != 'H' || b[2] != 'M' || b[3] != '1') throw new Exception("models.bytes: bad header");
        int p = 4;
        int n = BitConverter.ToInt32(b, p); p += 4;
        if (n != GenModels.Name.Length) throw new Exception("models.bytes does not match GenModels.cs");
        mesh = new Mesh[n]; bounds = new Bounds[n]; pivot = new Vector3[n]; parent = new int[n];
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
