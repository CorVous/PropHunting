using System.Reflection;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Copies the generator's tables (the plain C# GenData, GenModels and GenPlans, built by GenData.Build) into the
// MapGenUdon in the open scene: every [HideInInspector] public field of MapGenUdon takes the static field of the
// same name. Udon has no static fields and building the tables at runtime would cost seconds, so they are baked.
// Run it after any change to the tables (menu PropHunt > Bake map generator tables).
public static class MapGenTableBaker
{
    [MenuItem("PropHunt/Bake map generator tables")]
    public static void Bake()
    {
        GenData.Build();
        var gen = Object.FindObjectOfType<MapGenUdon>(true);
        if (gen == null)
        {
            var go = new GameObject("MapGen");
            gen = go.AddUdonSharpComponent<MapGenUdon>();
            Debug.Log("[prophunt] created MapGen object");
        }
        int n = 0;
        foreach (var f in typeof(MapGenUdon).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.GetCustomAttribute<HideInInspector>() == null) continue;
            FieldInfo src = null;
            foreach (var t in new[] { typeof(GenData), typeof(GenModels), typeof(GenPlans) })
            {
                src = t.GetField(f.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (src != null) break;
            }
            if (src == null) { Debug.LogWarning("[prophunt] no table for MapGenUdon." + f.Name); continue; }
            object v = src.GetValue(null);
            if (v is System.Array a) v = a.Clone();
            f.SetValue(gen, v);
            n++;
        }
        UdonSharpEditorUtility.CopyProxyToUdon(gen);
        EditorUtility.SetDirty(gen);

        // the bench's expected result, from the plain C# generator
        var bench = Object.FindObjectOfType<MapGenBench>(true);
        if (bench != null)
        {
            var g = new MapGen();
            g.RunAll(bench.benchSeed, bench.benchSize, bench.benchDensity);
            bench.expectedHash = g.mapHash;
            bench.gen = gen;
            UdonSharpEditorUtility.CopyProxyToUdon(bench);
            EditorUtility.SetDirty(bench);
        }
        EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
        Debug.Log("[prophunt] baked " + n + " generator tables into " + gen.name);
    }

    [MenuItem("PropHunt/Add map generator bench")]
    public static void AddBench()
    {
        if (Object.FindObjectOfType<MapGenBench>(true) == null)
            new GameObject("MapGenBench").AddUdonSharpComponent<MapGenBench>();
        Bake();
    }
}
