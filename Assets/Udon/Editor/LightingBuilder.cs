using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRCLightVolumes;

// The map's sun shadows (PHSun): a VRC Light Volumes spot with in-game shadow baking, and the PHSun behaviour that
// aims it and bakes it after each map build. Called by WorldSceneBuilder.Build. The Light Volumes manager is created
// by the package in the scene root (outside "World"), so it survives rebuilds.
public static class LightingBuilder
{
    public const int CASTER_LAYER = 22;
    public const int SHADOW_RES = 1024;   // a user layer: walls, roofs and fences sit on it only during the bake

    public static PHSun Build(Transform world, MapBuilderUdon builder, Light sun)
    {
        var root = new GameObject("MapSun");
        root.transform.SetParent(world, false);

        Selection.activeGameObject = null;
        EditorApplication.ExecuteMenuItem("GameObject/Point Light Volume");
        var go = Selection.activeGameObject;
        if (go == null || go.GetComponent<PointLightVolumeInstance>() == null)
        {
            Debug.LogError("[lighting] could not create a Point Light Volume (is VRC Light Volumes installed?)");
            return null;
        }
        go.name = "SunSpot";
        go.transform.SetParent(root.transform, false);
        var spot = go.GetComponent<PointLightVolumeInstance>();
        spot.IsDynamic = true;
        spot.LightType = 1;                   // spot
        spot.Shadows = true;
        spot.BakeInGame = true;               // PHSun calls BakeShadows after each map build
        spot.LayerMask = 1 << CASTER_LAYER;
        spot.Color = sun.color;
        spot.Intensity = sun.intensity;       // PHSun scales it with the spot's height
        spot.Blur = 0.1f;
        spot.RuntimeShadowBlurSamplePreset = 0;
        spot.Bias = 0.05f;
        UdonSharpEditorUtility.CopyProxyToUdon(spot);
        Sync(spot);

        // the shadow atlas: one 1024 slice in half precision (8 MB) is enough for the largest map and fits Quest
        var mgr = spot.LightVolumeManager != null ? spot.LightVolumeManager : Object.FindObjectOfType<LightVolumeManager>(true);
        if (mgr != null)
        {
            mgr.ShadowTexturesWidth = SHADOW_RES; mgr.ShadowTexturesHeight = SHADOW_RES;
            mgr.ShadowTextureFormat = 1;   // float
            UdonSharpEditorUtility.CopyProxyToUdon(mgr);
            EditorUtility.SetDirty(mgr);
        }
        foreach (var setup in Object.FindObjectsOfType<LightVolumeSetup>(true))
        {
            setup.ShadowTextureFormat = LightVolumeSetup.ShadowTexturePrecision.Float;
            EditorUtility.SetDirty(setup);
        }

        var ph = root.AddUdonSharpComponent<PHSun>();
        ph.builder = builder; ph.spot = spot; ph.sun = sun; ph.casterLayer = CASTER_LAYER; ph.resolutionPc = SHADOW_RES; ph.resolutionQuest = SHADOW_RES;
        UdonSharpEditorUtility.CopyProxyToUdon(ph);
        return ph;
    }

    // the package's own editor sync (internal), so the authoring and runtime data agree
    static void Sync(PointLightVolumeInstance spot)
    {
        System.Type t = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType("VRCLightVolumes.PointLightVolumeEditorUtility");
            if (t != null) break;
        }
        var m = t?.GetMethod("Sync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                             null, new[] { typeof(PointLightVolumeInstance), typeof(bool), typeof(bool), typeof(bool) }, null);
        if (m != null) m.Invoke(null, new object[] { spot, false, true, false });
        else Debug.LogWarning("[lighting] PointLightVolumeEditorUtility.Sync not found");
    }
}
