using System.IO;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

// Keeps one UdonSharpProgramAsset next to every UdonSharpBehaviour script under Assets/Udon, so a new script compiles
// without creating its asset by hand. Runs after every script reload.
[InitializeOnLoad]
static class UdonProgramAssetSync
{
    static UdonProgramAssetSync() { EditorApplication.delayCall += Sync; }

    [MenuItem("PropHunt/Sync Udon program assets")]
    public static void Sync()
    {
        bool made = false;
        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/Udon" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            var type = script != null ? script.GetClass() : null;
            if (type == null || type.IsAbstract || !typeof(UdonSharpBehaviour).IsAssignableFrom(type)) continue;
            string assetPath = Path.ChangeExtension(path, ".asset");
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null) continue;
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, assetPath);
            made = true;
            Debug.Log("[prophunt] created program asset " + assetPath);
        }
        if (made)
        {
            AssetDatabase.SaveAssets();
            UdonSharpProgramAsset.CompileAllCsPrograms(true);
        }
    }
}
