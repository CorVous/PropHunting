using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// The LAST ROUND diorama (Cor's group playtest, item 40, then "a 3D diorama style instead ... on a table inside the
// play area"): a table in the ready area with the last round's map in miniature on it (PHMiniMap). Where it stands is
// one constant (DioramaAt / DioramaYaw in WorldSceneBuilder's call) so it is easy to move.
public static partial class LobbyBuilder
{
    public static PHMiniMap BuildMiniMap(Transform lobby, PHGame game, MapBuilderUdon builder, Vector3 floorPos, float yaw)
    {
        const float top = 0.85f, side = 2.6f;
        var table = new GameObject("Diorama");
        table.transform.SetParent(lobby, false);
        table.transform.localPosition = floorPos;
        table.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/PropLit.mat");
        // the table: a green felt top in a wooden frame on four legs (solid, as the lobby's furniture)
        Block(table.transform, "Top", new Vector3(0f, top - 0.03f, 0f), new Vector3(side, 0.06f, side), wood, C("6fae5a"), true);   // (grass: the map's ground)
        Block(table.transform, "Rim", new Vector3(0f, top - 0.06f, 0f), new Vector3(side + 0.12f, 0.06f, side + 0.12f), wood, C("7a5030"), false);
        for (int i = 0; i < 4; i++)
        {
            float x = (i & 1) == 0 ? -side / 2 + 0.1f : side / 2 - 0.1f, z = (i & 2) == 0 ? -side / 2 + 0.1f : side / 2 - 0.1f;
            Block(table.transform, "Leg", new Vector3(x, (top - 0.06f) / 2f, z), new Vector3(0.1f, top - 0.06f, 0.1f), wood, C("6b4a2e"), false);
        }

        var mm = table.AddUdonSharpComponent<PHMiniMap>();
        mm.game = game; mm.builder = builder; mm.tableSize = side - 0.3f;
        var content = new GameObject("Content");
        content.transform.SetParent(table.transform, false);
        mm.content = content;
        var model = new GameObject("Model");
        model.transform.SetParent(content.transform, false);
        model.transform.localPosition = new Vector3(0f, top + 0.002f, 0f);
        mm.model = model.transform;

        // the map's meshes go into these (the builder's own material); the scenery tiles too
        Material mapMat = builder.wallsFilter != null ? builder.wallsFilter.GetComponent<MeshRenderer>().sharedMaterial : wood;
        // the walls in their own material (PropHunt/Diorama): cut above the ground floor when the upper floors are off
        var dioMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/Diorama.mat");
        if (dioMat == null) { dioMat = new Material(Shader.Find("PropHunt/Diorama")) { name = "Diorama" }; AssetDatabase.CreateAsset(dioMat, "Assets/Udon/Generated/Diorama.mat"); }
        dioMat.shader = Shader.Find("PropHunt/Diorama"); dioMat.SetFloat("_ClipY", 100000f); dioMat.SetVector("_Box", new Vector4(-100000f, -100000f, 100000f, 100000f)); dioMat.enableInstancing = true; EditorUtility.SetDirty(dioMat);
        mm.dioMat = dioMat;
        mm.dioWalls = Holder(model.transform, "Walls", dioMat);
        mm.dioGround = Holder(model.transform, "Ground", dioMat);   // (cut to the map's rectangle by the same material)
        mm.dioScenery = new MeshFilter[16];
        for (int i = 0; i < mm.dioScenery.Length; i++) mm.dioScenery[i] = Holder(model.transform, "Scenery" + i, mapMat);

        // a path and a pin per slot (in the model's space: the map's metres)
        int n = game.slots != null && game.slots.Length > 0 ? game.slots.Length : 32;
        var lineMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/MiniMapLine.mat");
        if (lineMat == null) { lineMat = new Material(Shader.Find("Sprites/Default")) { name = "MiniMapLine" }; AssetDatabase.CreateAsset(lineMat, "Assets/Udon/Generated/MiniMapLine.mat"); }
        lineMat.renderQueue = 3000; EditorUtility.SetDirty(lineMat);
        mm.paths = new LineRenderer[n]; mm.pins = new Transform[n]; mm.pinLabel = new TextMeshPro[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Path" + i);
            go.transform.SetParent(model.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false; lr.alignment = LineAlignment.View;
            lr.widthMultiplier = 0.15f; lr.numCapVertices = 2; lr.numCornerVertices = 2;
            lr.sharedMaterial = lineMat; lr.positionCount = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            go.SetActive(false);
            mm.paths[i] = lr;
            // the pin: a thin post 5 map-metres tall from the prop up, the name over it, turned to the room's south
            var pin = new GameObject("Pin" + i);
            pin.transform.SetParent(model.transform, false);
            var post = Block(pin.transform, "Post", new Vector3(0f, 5f, 0f), new Vector3(0.15f, 6f, 0.15f), wood, C("f6ecd4"), false);
            var label = new GameObject("Name");
            label.transform.SetParent(pin.transform, false);
            label.transform.localPosition = new Vector3(0f, 8.6f, 0f);
            label.transform.localRotation = Quaternion.Euler(0f, -yaw, 0f);   // (read from the room's south, where the spawn is)
            var tmp = label.AddComponent<TextMeshPro>();
            tmp.font = future; tmp.fontSize = 14f; tmp.alignment = TextAlignmentOptions.Center; tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(20f, 3f); tmp.text = "";

            pin.SetActive(false);
            mm.pins[i] = pin.transform; mm.pinLabel[i] = tmp;
        }

        // (no card: Cor, the names are on the pins). Two buttons on the near edge: the upper floors on or off, and
        // everyone or one player alone (Cor)
        var cv = Canvas(table.transform, "Buttons", new Vector3(0f, top + 0.12f, -side / 2 - 0.1f), Quaternion.Euler(35f, 0f, 0f), new Vector2(600f, 80f), 0.002f, true);
        mm.upperLabel = DebugButton(cv, game, "Upper floors: shown", nameof(PHMiniMap.ToggleUpper), new Vector2(-150, 0), C("f2c45a"), mm);
        mm.soloLabel = DebugButton(cv, game, "Everyone", nameof(PHMiniMap.NextSolo), new Vector2(150, 0), C("8fc7e8"), mm);
        UdonSharpEditorUtility.CopyProxyToUdon(mm);
        return mm;
    }

    static GameObject Block(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, Color c, bool solid)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = name; b.transform.SetParent(parent, false);
        b.transform.localPosition = pos; b.transform.localScale = size;
        if (!solid) Object.DestroyImmediate(b.GetComponent<Collider>()); else b.layer = 11;
        var r = b.GetComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        SetColour(b, c);
        return b;
    }

    static MeshFilter Holder(Transform parent, string name, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var f = go.AddComponent<MeshFilter>();
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        go.SetActive(false);
        return f;
    }

    static void SetColour(GameObject go, Color c)
    {
        var mf = go.GetComponent<MeshFilter>();
        var m = Object.Instantiate(mf.sharedMesh);
        var cols = new Color32[m.vertexCount];
        for (int i = 0; i < cols.Length; i++) cols[i] = c;
        m.colors32 = cols;
        mf.sharedMesh = m;
    }
}
