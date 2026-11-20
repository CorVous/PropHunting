using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// The LAST ROUND board (Cor's group playtest, item 40): a free-standing easel in the lobby with a top-down photo of
// the last round's map, everyone's paths over it and a short list (PHMiniMap). Where it stands is one constant
// (MiniMapAt / MiniMapYaw in WorldSceneBuilder's call) so it is easy to move.
public static partial class LobbyBuilder
{
    public static PHMiniMap BuildMiniMap(Transform lobby, PHGame game, MapBuilderUdon builder, Vector3 floorPos, float yaw)
    {
        // the stand: two legs and a ledge, the board 2.4 x 1.6 m, its bottom 0.8 m up
        var stand = new GameObject("MiniMapEasel");
        stand.transform.SetParent(lobby, false);
        stand.transform.localPosition = floorPos;
        stand.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/PropLit.mat");
        for (int side = -1; side <= 1; side += 2)
        {
            var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leg.name = "Leg"; leg.transform.SetParent(stand.transform, false);
            leg.transform.localPosition = new Vector3(side * 0.95f, 1.2f, 0.06f); leg.transform.localScale = new Vector3(0.07f, 2.4f, 0.07f);
            leg.transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);
            Object.DestroyImmediate(leg.GetComponent<Collider>());
            var lr = leg.GetComponent<MeshRenderer>(); lr.sharedMaterial = wood; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetColour(leg, C("8a5a34"));
        }
        var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
        back.name = "Board"; back.transform.SetParent(stand.transform, false);
        back.transform.localPosition = new Vector3(0f, 1.6f, 0.02f); back.transform.localScale = new Vector3(2.5f, 1.7f, 0.04f);
        back.layer = 11;   // solid, as the walls
        var br = back.GetComponent<MeshRenderer>(); br.sharedMaterial = wood; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        SetColour(back, C("6b4a2e"));

        // the face: 1200 x 800 canvas units at 2 mm
        var cv = Canvas(stand.transform, "MiniMap", new Vector3(0f, 1.6f, -0.005f), Quaternion.identity, new Vector2(1200f, 800f), 0.002f, false);
        var bg = cv.gameObject.AddComponent<Image>(); bg.color = Felt; bg.raycastTarget = false;
        var title = Txt(cv, "Title", new Vector2(0, 360), new Vector2(1100, 50), "LAST ROUND", rocket, 40, C("e1cfa5"), TextAlignmentOptions.Center);
        title.characterSpacing = 4f;
        var waiting = Txt(cv, "Waiting", new Vector2(0, 0), new Vector2(1100, 60), "Play a round to see its map here", future, 30, C("b8ae98"), TextAlignmentOptions.Center);
        var content = Rect(cv, "Content", Vector2.zero, new Vector2(1200f, 800f));
        // the photo, 660 square on the left, in a cream frame
        Img(content, "Frame", new Vector2(-250, -30), new Vector2(684, 684), sRound, Cream, 2f);
        var photo = Rect(content, "Map", new Vector2(-250, -30), new Vector2(660, 660));
        var raw = photo.gameObject.AddComponent<RawImage>(); raw.raycastTarget = false;
        string rtPath = "Assets/Udon/Generated/MiniMap.renderTexture";
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
        if (rt == null) { rt = new RenderTexture(512, 512, 16) { name = "MiniMap" }; AssetDatabase.CreateAsset(rt, rtPath); }
        raw.texture = rt;
        // the list on the right
        var list = Txt(content, "List", new Vector2(320, -30), new Vector2(500, 660), "", future, 24, C("f6ecd4"), TextAlignmentOptions.TopLeft);
        list.enableWordWrapping = false; list.overflowMode = TextOverflowModes.Truncate; list.richText = true;

        var mm = cv.gameObject.AddUdonSharpComponent<PHMiniMap>();
        mm.game = game; mm.builder = builder; mm.content = content.gameObject; mm.mapRect = photo;
        mm.list = list; mm.title = title; mm.waiting = waiting;
        mm.markEnd = Icon("meeple_hide"); mm.markCaught = Icon("lock");

        // a path and an end mark per slot; the lines lie flat on the photo (alignment along the board)
        int n = game.slots != null && game.slots.Length > 0 ? game.slots.Length : 32;
        var lineMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/MiniMapLine.mat");
        if (lineMat == null) { lineMat = new Material(Shader.Find("Sprites/Default")) { name = "MiniMapLine" }; AssetDatabase.CreateAsset(lineMat, "Assets/Udon/Generated/MiniMapLine.mat"); }
        mm.paths = new LineRenderer[n]; mm.marks = new RectTransform[n]; mm.markImage = new Image[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Path" + i);
            go.transform.SetParent(photo, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false; lr.alignment = LineAlignment.TransformZ;
            lr.widthMultiplier = 0.008f; lr.numCapVertices = 2; lr.numCornerVertices = 2;
            lr.sharedMaterial = lineMat; lr.positionCount = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            go.SetActive(false);
            mm.paths[i] = lr;
            var mark = Img(photo, "Mark" + i, Vector2.zero, new Vector2(34, 34), mm.markEnd, Color.white);
            mark.gameObject.SetActive(false);
            mm.marks[i] = mark.rectTransform; mm.markImage[i] = mark;
        }

        // the camera that takes the photo: straight down, off (PHMiniMap renders it by hand once a map); no players,
        // no UI, not the hider's own prop (as the map trail's)
        var camGo = new GameObject("MiniMapCamera");
        camGo.transform.SetParent(lobby, false);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 40f; cam.nearClipPlane = 1f; cam.farClipPlane = 600f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = C("4f7a47"); cam.allowHDR = false; cam.allowMSAA = false;
        cam.cullingMask = ~((1 << 5) | (1 << 9) | (1 << 10) | (1 << 12) | (1 << 18) | (1 << 19) | (1 << 22) | (1 << 23));
        cam.targetTexture = rt;
        cam.enabled = false;
        mm.cam = cam;
        UdonSharpEditorUtility.CopyProxyToUdon(mm);
        return mm;
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
