using UnityEditor;
using UnityEngine;

// The how-to-play wall (Cor, onboarding): three looping picture panels, hide / hunt / final stand, with no words. The
// art is Tools/make_howto.py's flipbook atlases (Assets/Lobby/HowTo, 8 x 8 cells of 512 x 320, read left to right,
// top to bottom); PropHunt/Flipbook plays them from _Time, so no Udon runs. They hang on the spawn wall (south) at
// eye height, centred behind the spawn disc, under the PROPHUNT name: the stretch of wall left free between the
// armchair and lamp (west end) and the cactus and lamp (east end).
public static partial class LobbyBuilder
{
    const string HowToDir = Root + "HowTo/";
    static readonly string[] HowToNames = { "hide", "hunt", "final_stand" };
    static readonly float[] HowToLoop = { 5.32f, 5.46f, 5.04f };   // make_howto.py prints these

    public static void BuildHowTo(Transform lobby, Vector3 centre, float yaw, float panelW = 1.6f, float gap = 0.15f)
    {
        var root = new GameObject("HowTo");
        root.transform.SetParent(lobby, false);
        root.transform.localPosition = centre;
        root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);   // the panels face the root's +z
        var shader = Shader.Find("PropHunt/Flipbook");
        float panelH = panelW * 320f / 512f;
        for (int i = 0; i < HowToNames.Length; i++)
        {
            string tex = HowToDir + "howto_" + HowToNames[i] + ".png";
            ImportHowTo(tex);
            string path = "Assets/Udon/Generated/HowTo_" + HowToNames[i] + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
            m.SetFloat("_Columns", 8f); m.SetFloat("_Rows", 8f); m.SetFloat("_Loop", HowToLoop[i]);
            m.SetFloat("_Cutoff", 0.5f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "HowTo_" + HowToNames[i];
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            // left to right as the viewer sees them (the viewer stands on the root's +z side, so their left is +x)
            float x = (i - (HowToNames.Length - 1) / 2f) * (panelW + gap);
            go.transform.localPosition = new Vector3(-x, 0f, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = new Vector3(panelW, panelH, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
    }

    // sRGB, mipmaps that keep the cut-out's coverage at a distance, clamped; 4096 on PC, 2048 on Android, compressed
    static void ImportHowTo(string path)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null) { Debug.LogWarning("[prophunt] missing " + path); return; }
        var pc = ti.GetPlatformTextureSettings("Standalone");
        var an = ti.GetPlatformTextureSettings("Android");
        bool ok = ti.textureType == TextureImporterType.Default && ti.sRGBTexture && ti.alphaIsTransparency && ti.mipmapEnabled
            && ti.mipMapsPreserveCoverage && ti.wrapMode == TextureWrapMode.Clamp && ti.maxTextureSize == 4096
            && ti.npotScale == TextureImporterNPOTScale.None
            && pc.overridden && pc.maxTextureSize == 4096 && pc.format == TextureImporterFormat.DXT5
            && an.overridden && an.maxTextureSize == 2048 && an.format == TextureImporterFormat.ASTC_6x6;
        if (ok) return;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.mipmapEnabled = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f;
        ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear; ti.anisoLevel = 2;
        ti.maxTextureSize = 4096;
        ti.textureCompression = TextureImporterCompression.Compressed;
        pc.overridden = true; pc.maxTextureSize = 4096; pc.format = TextureImporterFormat.DXT5;
        ti.SetPlatformTextureSettings(pc);
        an.overridden = true; an.maxTextureSize = 2048; an.format = TextureImporterFormat.ASTC_6x6;
        ti.SetPlatformTextureSettings(an);
        ti.SaveAndReimport();
    }
}
