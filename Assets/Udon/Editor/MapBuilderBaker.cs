using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Sets up the map objects in the open scene for MapBuilderUdon: a "Map" root with the generator (MapGenUdon), the
// builder, mesh holders for the ground, walls and roofs, the architecture's collision mesh, and parents for props
// and fences. Called by PropHunt > Bake prop prefabs, which hands it the prefabs.
public static class MapBuilderBaker
{
    public static void Assign(GameObject[] prefabs, GameObject fence, Material mat, Bounds fenceBounds, GameObject rail = null, GameObject[] fixtures = null, GameObject[] decor = null, Mesh[] parts = null, Vector4[] partOpening = null, Color[] partBody = null, GameObject[] sky = null, GameObject stars = null, GameObject moon = null, Mesh[] cages = null)
    {
        var builder = Object.FindObjectOfType<MapBuilderUdon>(true);
        GameObject root;
        if (builder == null)
        {
            var gen = Object.FindObjectOfType<MapGenUdon>(true);
            root = gen != null ? gen.gameObject : new GameObject("Map");
            root.name = "Map";
            builder = root.AddUdonSharpComponent<MapBuilderUdon>();
        }
        root = builder.gameObject;
        builder.gen = root.GetComponent<MapGenUdon>();
        // the buildings' footprints for the shader (MapBuilderUdon.IndoorMap): written at runtime
        const string indoorPath = "Assets/Udon/Generated/Indoor.asset";
        var indoor = AssetDatabase.LoadAssetAtPath<Texture2D>(indoorPath);
        if (indoor == null)
        {
            indoor = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { name = "Indoor", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            indoor.SetPixels32(new Color32[64 * 64]); indoor.Apply(false);
            AssetDatabase.CreateAsset(indoor, indoorPath);
        }
        builder.indoorTex = indoor;
        if (builder.gen == null) builder.gen = Object.FindObjectOfType<MapGenUdon>(true);
        if (builder.gen == null) { builder.gen = root.AddUdonSharpComponent<MapGenUdon>(); MapGenTableBaker.Bake(); }

        builder.groundFilter = Holder(root.transform, "Ground", mat, false);
        builder.wallsFilter = Holder(root.transform, "Walls", mat, false);
        builder.roofsFilter = Holder(root.transform, "Roofs", mat, false);
        builder.sceneryFilter = Holder(root.transform, "Scenery", mat, false);   // the static scenery, merged after each build
        var coll = Child(root.transform, "ArchCollision");
        coll.layer = 11;   // Environment
        builder.collisionCollider = Get<MeshCollider>(coll);
        builder.propRoot = Child(root.transform, "Props").transform;
        builder.fenceRoot = Child(root.transform, "Fences").transform;
        builder.fixtureRoot = Child(root.transform, "Fixtures").transform;
        builder.fixturePrefabs = fixtures;
        builder.decorPrefabs = decor;
        builder.partMeshes = parts;
        builder.partOpening = partOpening;
        builder.partBody = partBody;
        builder.skyPrefabs = sky;
        builder.starDomePrefab = stars;
        builder.moonPrefab = moon;
        builder.propPrefabs = prefabs;
        if (cages != null) builder.cageMeshes = cages;   // the hider's own prop's edges (PropPrefabBaker.BakeCages)
        builder.fencePrefab = fence;
        builder.fenceModelSize = fenceBounds.size;
        builder.railPrefab = rail;
        BakeTints(builder);
        UdonSharpEditorUtility.CopyProxyToUdon(builder);
        EditorUtility.SetDirty(builder);

        var bench = Object.FindObjectOfType<MapGenBench>(true);
        if (bench != null) { bench.builder = builder; UdonSharpEditorUtility.CopyProxyToUdon(bench); EditorUtility.SetDirty(bench); }
        EditorSceneManager.MarkSceneDirty(root.scene);
    }

    // The colour averages MapBuilderUdon's tints start from (its AverageGreen and FenceTint, the same sums), so the
    // build does not walk every vertex of a model at runtime (Perf). Run by Assign (Bake prop prefabs) and on its own
    // from the menu, on the open scene's builder, without baking the prefabs again.
    [MenuItem("PropHunt/Bake tint stats (open scene)")]
    static void BakeTintsMenu()
    {
        var builder = Object.FindObjectOfType<MapBuilderUdon>(true);
        if (builder == null) { Debug.LogError("[MapBuilderBaker] no MapBuilderUdon in the open scene"); return; }
        BakeTints(builder);
        UdonSharpEditorUtility.CopyProxyToUdon(builder);
        EditorUtility.SetDirty(builder);
        EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
    }

    const int HEDGE = 12;   // MapBuilderUdon.DECOR_HEDGE

    public static void BakeTints(MapBuilderUdon builder)
    {
        GameObject[] prefabs = builder.propPrefabs;
        int n = prefabs != null ? prefabs.Length : 0;
        var avg = new Color[n];
        var share = new float[n];
        int baked = 0;
        for (int d = 0; d < n; d++)
        {
            if (prefabs[d] == null) { share[d] = -1f; continue; }
            float sh;
            avg[d] = AverageGreen(prefabs[d], out sh);
            share[d] = sh;
            baked++;
        }
        builder.defGreenAvg = avg;
        builder.defGreenShare = share;
        GameObject hedge = builder.decorPrefabs != null && HEDGE < builder.decorPrefabs.Length ? builder.decorPrefabs[HEDGE] : null;
        if (hedge != null) { float sh; builder.hedgeGreenAvg = AverageGreen(hedge, out sh); builder.hedgeGreenShare = sh; }
        else { builder.hedgeGreenAvg = new Color(0, 0, 0, 0); builder.hedgeGreenShare = -1f; }
        builder.fenceColourAvg = new Color(0, 0, 0, 0);
        if (builder.fencePrefab != null)
        {
            float r = 0, g = 0, b = 0; int k = 0;
            foreach (var f in builder.fencePrefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                foreach (var c in f.sharedMesh.colors32) { r += c.r; g += c.g; b += c.b; k++; }
            }
            if (k > 0) builder.fenceColourAvg = new Color(r / k, g / k, b / k, 1f);
        }
        Debug.Log("[MapBuilderBaker] tint stats baked: " + baked + " of " + n + " defs, hedge " + (hedge != null) + ", fence " + (builder.fenceColourAvg.a > 0f));
    }

    // as MapBuilderUdon.AverageGreen: the average colour of the vertices greener than red by 20, and their share
    static Color AverageGreen(GameObject go, out float share)
    {
        float r = 0, g = 0, b = 0; int n = 0, total = 0;
        foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh m = f.sharedMesh;
            if (m == null) continue;
            Color32[] cs = m.colors32;
            total += cs.Length;
            for (int i = 0; i < cs.Length; i++)
            {
                if (cs[i].g < cs[i].r + 20) continue;
                r += cs[i].r; g += cs[i].g; b += cs[i].b; n++;
            }
        }
        share = total > 0 ? (float)n / total : 0f;
        if (n == 0) return new Color(0, 0, 0, 0);
        return new Color(r / n / 255f, g / n / 255f, b / n / 255f, 1f);
    }

    static T Get<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static GameObject Child(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t.gameObject;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    static MeshFilter Holder(Transform parent, string name, Material mat, bool collider)
    {
        var go = Child(parent, name);
        go.layer = 11;
        var mf = Get<MeshFilter>(go);
        var mr = Get<MeshRenderer>(go);
        mr.sharedMaterial = mat;
        return mf;
    }
}
