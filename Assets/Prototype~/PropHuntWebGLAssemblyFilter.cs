#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// The browser prototype is plain Unity C#. The VRChat SDK's runtime assemblies cannot work in a WebGL
// player (their start-up code kept the page's main thread busy and no frame was ever drawn), so they are
// left out of WebGL players only. Builds for VRChat's own platforms are not affected.
// Assembly-CSharp is left out too: the game's own code is in the PropHunt assembly (Assets/Scripts/PropHunt.asmdef),
// and the SDK copies UdonSharp utility scripts into Assets/, which land in Assembly-CSharp and need the SDK.
public class PropHuntWebGLAssemblyFilter : IFilterBuildAssemblies
{
    public int callbackOrder { get { return 0; } }

    static readonly string[] dropPrefixes =
    {
        "VRC", "Udon", "UdonSharp", "WorldQualitySettings", "UniTask", "DOTween", "Collections.Pooled",
        "Assembly-CSharp", "0Harmony", "librsync", "Blake2Sharp",
    };

    public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            return assemblies;
        var keep = new List<string>();
        var dropped = new List<string>();
        foreach (string a in assemblies)
        {
            string file = Path.GetFileNameWithoutExtension(a);
            bool drop = false;
            foreach (string p in dropPrefixes)
                if (file.StartsWith(p, System.StringComparison.Ordinal)) { drop = true; break; }
            if (drop) dropped.Add(file); else keep.Add(a);
        }
        Debug.Log("[prophunt] WebGL assembly filter: kept " + keep.Count + " [" + string.Join(", ", keep) + "]");
        Debug.Log("[prophunt] WebGL assembly filter: dropped " + dropped.Count + " [" + string.Join(", ", dropped) + "]");
        return keep.ToArray();
    }
}
#endif
