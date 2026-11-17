using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRCLightVolumes;

// The map's lamps (PHLamps): a pool of dynamic Point Light Volumes, without shadows, and the PHLamps behaviour that
// puts them on the map builder's light points after each build. Called by WorldSceneBuilder.Build.
public static class LampBuilder
{
    public const int POOL = 24;

    public static PHLamps Build(Transform world, MapBuilderUdon builder)
    {
        var root = new GameObject("MapLamps");
        root.transform.SetParent(world, false);
        var pool = new PointLightVolumeInstance[POOL];
        for (int i = 0; i < POOL; i++)
        {
            Selection.activeGameObject = null;
            EditorApplication.ExecuteMenuItem("GameObject/Point Light Volume");
            var go = Selection.activeGameObject;
            if (go == null || go.GetComponent<PointLightVolumeInstance>() == null)
            {
                Debug.LogError("[lighting] could not create a Point Light Volume (is VRC Light Volumes installed?)");
                Object.DestroyImmediate(root);
                return null;
            }
            go.name = "Lamp" + i;
            go.transform.SetParent(root.transform, false);
            var l = go.GetComponent<PointLightVolumeInstance>();
            l.IsDynamic = true;
            l.LightType = 0;
            l.Shadows = false;
            l.Color = new Color(1f, 0.84f, 0.62f);
            l.Intensity = 20f;
            UdonSharpEditorUtility.CopyProxyToUdon(l);
            Sync(l);
            go.SetActive(false);
            pool[i] = l;
        }
        var ph = root.AddUdonSharpComponent<PHLamps>();
        ph.builder = builder;
        ph.pool = pool;
        UdonSharpEditorUtility.CopyProxyToUdon(ph);
        Selection.activeGameObject = null;
        return ph;
    }

    // a fixed Point Light Volume (no shadows) at a local position under parent: a lamp in the lobby
    public static PointLightVolumeInstance MakeLight(Transform parent, string name, Vector3 localPos, Color color, float intensity, float size)
    {
        Selection.activeGameObject = null;
        EditorApplication.ExecuteMenuItem("GameObject/Point Light Volume");
        var go = Selection.activeGameObject;
        if (go == null || go.GetComponent<PointLightVolumeInstance>() == null) { Debug.LogError("[lighting] could not create a Point Light Volume"); return null; }
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var l = go.GetComponent<PointLightVolumeInstance>();
        l.IsDynamic = false;
        l.LightType = 0;
        l.Shadows = false;
        l.Color = color;
        l.Intensity = intensity;
        l.LightSourceSize = size;
        l.InverseSquaredRange = 1f / (size * size);
        UdonSharpEditorUtility.CopyProxyToUdon(l);
        Sync(l);
        Selection.activeGameObject = null;
        return l;
    }

    // the package's own editor sync (internal), so the authoring and runtime data agree (as LightingBuilder does)
    static void Sync(PointLightVolumeInstance l)
    {
        System.Type t = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType("VRCLightVolumes.PointLightVolumeEditorUtility");
            if (t != null) break;
        }
        var m = t?.GetMethod("Sync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                             null, new[] { typeof(PointLightVolumeInstance), typeof(bool), typeof(bool), typeof(bool) }, null);
        if (m != null) m.Invoke(null, new object[] { l, false, true, false });
    }
}
