using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

// The lobby's sun shadows, baked once when the scene is built (Cor, lobby profiling 2026-11-24: "could we get some
// shadows back?" once the sun's realtime shadows went off in the lobby, where they cost about 130 draw calls).
// A depth map seen from the sun over the whole room, saved as Generated/LobbyShadow.asset; PropHunt/LobbyLit darkens
// the sun with it, and PHLobbyCull hands the shader the map and its projection at Start.
// Casters: the room's dressing that casts shadows today (walls and lid don't). Left out: the weapon wall (its
// shelves turn) and the diorama's model (filled in game), and text.
public static class LobbyShadowBaker
{
    const string TexPath = "Assets/Udon/Generated/LobbyShadow.asset";
    const int Res = 1024;
    const int BakeLayer = 31;   // a free user layer, only for the bake's frame

    public static void Bake(Transform lobby, Light sun, PHLobbyCull cull)
    {
        // what is lit: the room, from the floor to the lid
        Bounds room = new Bounds(lobby.position, Vector3.zero);
        bool any = false;
        var casters = new List<Renderer>();
        foreach (var r in lobby.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r.GetComponent<TMP_Text>() != null) continue;
            if (any) room.Encapsulate(r.bounds); else { room = r.bounds; any = true; }
            if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) continue;
            if (Under(r.transform, lobby, "Store") || Under(r.transform, lobby, "Diorama/Content")) continue;
            casters.Add(r);
        }
        if (!any || casters.Count == 0) { Debug.LogWarning("[lobby shadow] nothing to bake"); return; }

        // the sun's frame: x right, y up, z along the light
        Quaternion rot = sun.transform.rotation;
        Vector3 right = rot * Vector3.right, up = rot * Vector3.up, fwd = rot * Vector3.forward;
        float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue, w0 = float.MaxValue, w1 = float.MinValue;
        for (int k = 0; k < 8; k++)
        {
            Vector3 c = room.center + Vector3.Scale(room.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
            float u = Vector3.Dot(c, right), v = Vector3.Dot(c, up), w = Vector3.Dot(c, fwd);
            u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u); v0 = Mathf.Min(v0, v); v1 = Mathf.Max(v1, v); w0 = Mathf.Min(w0, w); w1 = Mathf.Max(w1, w);
        }
        float size = Mathf.Max(u1 - u0, v1 - v0) + 1f;
        float uc = (u0 + u1) * 0.5f, vc = (v0 + v1) * 0.5f;
        w0 -= 1f; w1 += 1f;
        float range = w1 - w0;
        // world -> (u, v, depth), one row each
        var sm0 = new Vector4(right.x / size, right.y / size, right.z / size, -uc / size + 0.5f);
        var sm1 = new Vector4(up.x / size, up.y / size, up.z / size, -vc / size + 0.5f);
        var sm2 = new Vector4(fwd.x / range, fwd.y / range, fwd.z / range, -w0 / range);
        float texel = 1f / Res;
        var sp = new Vector4(texel, 0.04f / range, 1f, 0f);   // depth bias: 4 cm (the receivers also step 3 cm out along their normal)

        // the depth frame: the casters alone on a spare layer, an orthographic camera along the sun
        var depthShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Udon/Editor/LobbyShadowDepth.shader");
        var layers = new int[casters.Count];
        for (int i = 0; i < casters.Count; i++) { layers[i] = casters[i].gameObject.layer; casters[i].gameObject.layer = BakeLayer; }
        var camGo = new GameObject("LobbyShadowBakeCam") { hideFlags = HideFlags.HideAndDontSave };
        var rt = new RenderTexture(Res, Res, 24, RenderTextureFormat.RFloat) { filterMode = FilterMode.Point };
        Texture2D tex;
        try
        {
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true; cam.orthographicSize = size * 0.5f; cam.aspect = 1f;
            cam.transform.SetPositionAndRotation(right * uc + up * vc + fwd * (w0 - 1f), rot);
            cam.nearClipPlane = 0.5f; cam.farClipPlane = range + 2f;
            cam.cullingMask = 1 << BakeLayer;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(1f, 1f, 1f, 1f);
            cam.allowHDR = false; cam.allowMSAA = false;
            cam.targetTexture = rt;
            Shader.SetGlobalVector("_PHLobbyBakeSM2", sm2);
            cam.RenderWithShader(depthShader, "");
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(Res, Res, TextureFormat.RFloat, false, true);
            read.ReadPixels(new Rect(0, 0, Res, Res), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            // stored in half precision (2 MB): about 1.5 mm steps over the room's depth
            tex = new Texture2D(Res, Res, TextureFormat.RHalf, false, true) { name = "LobbyShadow", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(read.GetPixels());
            tex.Apply(false, false);
            Object.DestroyImmediate(read);
        }
        finally
        {
            for (int i = 0; i < casters.Count; i++) casters[i].gameObject.layer = layers[i];
            Object.DestroyImmediate(camGo);
            rt.Release(); Object.DestroyImmediate(rt);
        }

        var old = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        if (old != null) { EditorUtility.CopySerialized(tex, old); Object.DestroyImmediate(tex); tex = old; EditorUtility.SetDirty(tex); }
        else AssetDatabase.CreateAsset(tex, TexPath);
        AssetDatabase.SaveAssets();

        cull.sun = sun;
        cull.shadowTex = tex;
        cull.sm0 = sm0; cull.sm1 = sm1; cull.sm2 = sm2; cull.sp = sp;
        UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(cull);
        // the editor's views show it too
        Shader.SetGlobalTexture("_UdonPHLobbyShadow", tex);
        Shader.SetGlobalVector("_UdonPHLobbySM0", sm0); Shader.SetGlobalVector("_UdonPHLobbySM1", sm1);
        Shader.SetGlobalVector("_UdonPHLobbySM2", sm2); Shader.SetGlobalVector("_UdonPHLobbySP", sp);
        Debug.Log("[lobby shadow] baked " + casters.Count + " casters, " + size.ToString("F1") + " m square, " + (size / Res * 100f).ToString("F1") + " cm a texel");
    }

    static bool Under(Transform t, Transform root, string path)
    {
        Transform p = root.Find(path);
        return p != null && (t == p || t.IsChildOf(p));
    }

    // menu: bake again without rebuilding the scene (after moving the lobby's dressing by hand)
    [MenuItem("PropHunt/Bake lobby shadows")]
    static void BakeMenu()
    {
        var lobby = GameObject.Find("World/Lobby");
        var sun = GameObject.Find("World/Sun");
        if (lobby == null || sun == null) { Debug.LogError("[lobby shadow] no World/Lobby or World/Sun"); return; }
        Bake(lobby.transform, sun.GetComponent<Light>(), lobby.GetComponent<PHLobbyCull>());
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lobby.scene);
    }
}
