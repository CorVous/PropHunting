using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// The diorama's full round replay (PHReplay; Cor: "a full round replay as well", "I'd like it to be synced"): a
// recording object per slot, a marker, burst, tail and name per slot on the miniature, and a row of controls under
// the diorama's buttons: watch / pause, speed, a scrub bar (pressable in pieces, VR and desktop alike), the clock and
// back to the summary. Built after BuildMiniMap, on its table.
public static partial class LobbyBuilder
{
    public static PHReplay BuildReplay(PHMiniMap mm, PHGame game, MapBuilderUdon builder)
    {
        const float top = 0.85f, side = 2.6f;   // (BuildMiniMap's table)
        Transform table = mm.transform;
        var root = new GameObject("Replay");
        root.transform.SetParent(table, false);
        var rp = root.AddUdonSharpComponent<PHReplay>();
        rp.game = game; rp.builder = builder; rp.mini = mm;
        game.replay = rp;

        int n = game.slots != null && game.slots.Length > 0 ? game.slots.Length : 32;
        // the recordings: one synced object per slot (PHReplayTrack), sent once at the END reveal
        var trackRoot = new GameObject("Tracks");
        trackRoot.transform.SetParent(root.transform, false);
        rp.tracks = new PHReplayTrack[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Track" + i);
            go.transform.SetParent(trackRoot.transform, false);
            var tr = go.AddUdonSharpComponent<PHReplayTrack>();
            tr.replay = rp; tr.index = i;
            UdonSharpEditorUtility.CopyProxyToUdon(tr);
            rp.tracks[i] = tr;
        }

        // the markers, in the model's space (the map's metres): a pawn for a hunter or a hider out of a prop, a burst,
        // a tail and the name
        var pawnMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/ReplayPawn.mat");
        if (pawnMat == null) { pawnMat = new Material(Shader.Find("Unlit/Color")) { name = "ReplayPawn" }; AssetDatabase.CreateAsset(pawnMat, "Assets/Udon/Generated/ReplayPawn.mat"); }
        pawnMat.color = Color.white; EditorUtility.SetDirty(pawnMat);
        var lineMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/MiniMapLine.mat");
        rp.pawns = new Transform[n]; rp.pawnRenderer = new Renderer[n]; rp.pops = new Transform[n]; rp.popRenderer = new Renderer[n];
        rp.tails = new LineRenderer[n]; rp.tags = new Transform[n]; rp.tagLabel = new TextMeshPro[n];
        for (int i = 0; i < n; i++)
        {
            var pawn = new GameObject("ReplayPawn" + i);
            pawn.transform.SetParent(mm.model, false);
            rp.pawnRenderer[i] = Shape(pawn.transform, PrimitiveType.Capsule, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), pawnMat);
            pawn.SetActive(false);
            rp.pawns[i] = pawn.transform;

            var pop = new GameObject("ReplayPop" + i);
            pop.transform.SetParent(mm.model, false);
            rp.popRenderer[i] = Shape(pop.transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one, pawnMat);
            pop.SetActive(false);
            rp.pops[i] = pop.transform;

            var tail = new GameObject("ReplayTail" + i);
            tail.transform.SetParent(mm.model, false);
            var lr = tail.AddComponent<LineRenderer>();
            lr.useWorldSpace = false; lr.alignment = LineAlignment.View;
            lr.widthMultiplier = 0.2f; lr.numCapVertices = 2; lr.numCornerVertices = 2;
            lr.sharedMaterial = lineMat; lr.positionCount = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            tail.SetActive(false);
            rp.tails[i] = lr;

            var tag = new GameObject("ReplayName" + i);
            tag.transform.SetParent(mm.model, false);
            var tmp = tag.AddComponent<TextMeshPro>();
            tmp.font = future; tmp.fontSize = 12f; tmp.alignment = TextAlignmentOptions.Center; tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(20f, 3f); tmp.text = "";
            tag.SetActive(false);
            rp.tags[i] = tag.transform; rp.tagLabel[i] = tmp;
        }

        // the controls: a row on the same slope as the diorama's buttons, just under them (BuildMiniMap's canvas is
        // at top + 0.12, 0.1 m past the near edge, 0.16 m tall, tipped back 35 degrees)
        float down = 0.18f, a = 35f * Mathf.Deg2Rad;
        var cv = Canvas(table, "ReplayControls", new Vector3(0f, top + 0.12f - down * Mathf.Cos(a), -side / 2 - 0.1f - down * Mathf.Sin(a)),
            Quaternion.Euler(35f, 0f, 0f), new Vector2(1400f, 80f), 0.002f, true);
        var bar = new GameObject("Bar", typeof(RectTransform));
        bar.transform.SetParent(cv, false);
        ((RectTransform)bar.transform).sizeDelta = new Vector2(1400f, 80f);
        rp.bar = bar;
        rp.playLabel = ReplayButton(bar.transform, game, "Watch replay", new Vector2(-580f, 0f), new Vector2(220f, 64f), C("f2c45a"), rp, nameof(PHReplay.PlayPause), 0);
        rp.speedLabel = ReplayButton(bar.transform, game, "1×", new Vector2(-415f, 0f), new Vector2(90f, 64f), C("e8dcc0"), rp, nameof(PHReplay.Speed), 0);
        var speedGo = rp.speedLabel.transform.parent.parent.gameObject;
        // the scrub bar: a track, the hunt's start on it, the playhead, and pressable pieces over it
        const float barW = 760f; const int segs = 20;
        var track = Img(bar.transform, "Scrub", new Vector2(25f, 0f), new Vector2(barW, 18f), sRound, C("6d5524"), 2f);
        var hunt = Img(track.transform, "Hunt", Vector2.zero, new Vector2(6f, 40f), sTile, C("eb3a2e"), 1f);
        var head = Img(track.transform, "Head", new Vector2(-barW / 2, 0f), new Vector2(14f, 52f), sRound, C("f6ecd4"), 2f);
        for (int s = 0; s < segs; s++)
        {
            float x = -barW / 2 + barW * s / (segs - 1);
            var piece = Img(track.transform, "Piece" + s, new Vector2(x, 0f), new Vector2(barW / (segs - 1), 64f), null, new Color(1f, 1f, 1f, 0f), 1f);   // (clear: still pressable, Cor saw faint circles at 0.01)
            piece.raycastTarget = true;
            var bt = piece.gameObject.AddComponent<Button>();
            var nav = bt.navigation; nav.mode = Navigation.Mode.None; bt.navigation = nav; bt.targetGraphic = piece;
            var pb = piece.gameObject.AddUdonSharpComponent<PHButton>();
            pb.game = game; pb.target = rp; pb.action = nameof(PHReplay.Seek); pb.arg = s + 1;
            UdonSharpEditorUtility.CopyProxyToUdon(pb);
            UnityEditor.Events.UnityEventTools.AddStringPersistentListener(bt.onClick, UdonSharpEditorUtility.GetBackingUdonBehaviour(pb).SendCustomEvent, nameof(PHButton.Press));
        }
        rp.barWidth = barW; rp.segments = segs; rp.playhead = head.transform; rp.huntMark = hunt.transform;
        head.transform.SetAsLastSibling();
        rp.clock = Txt(bar.transform, "Clock", new Vector2(470f, 3f), new Vector2(120f, 40f), "0:00 / 0:00", future, 16, C("f6ecd4"), TextAlignmentOptions.Center);
        rp.summaryLabel = ReplayButton(bar.transform, game, "Summary", new Vector2(610f, 0f), new Vector2(160f, 64f), C("e8dcc0"), rp, nameof(PHReplay.Close), 0);
        var summaryGo = rp.summaryLabel.transform.parent.parent.gameObject;
        rp.playingOnly = new GameObject[] { speedGo, track.gameObject, rp.clock.gameObject, summaryGo };
        foreach (var g in rp.playingOnly) g.SetActive(false);
        bar.SetActive(false);

        // the overhead seek bar (Cor: "a global seek bar"): a 1.8 m timeline 1.3 m over the diorama's middle, turned
        // to each viewer by PHReplay, its 40 pieces pressable from any side of the table, the clock over it
        const float overW = 900f; const int overSegs = 40;
        var oc = Canvas(table, "ReplayOverhead", new Vector3(0f, top + 1.3f, 0f), Quaternion.identity, new Vector2(overW + 40f, 110f), 0.002f, true);
        var otrack = Img(oc, "Scrub", new Vector2(0f, -15f), new Vector2(overW, 22f), sRound, C("6d5524"), 2f);
        var ohunt = Img(otrack.transform, "Hunt", Vector2.zero, new Vector2(7f, 46f), sTile, C("eb3a2e"), 1f);
        var ohead = Img(otrack.transform, "Head", new Vector2(-overW / 2, 0f), new Vector2(16f, 60f), sRound, C("f6ecd4"), 2f);
        for (int s = 0; s < overSegs; s++)
        {
            float x = -overW / 2 + overW * s / (overSegs - 1);
            var piece = Img(otrack.transform, "Piece" + s, new Vector2(x, 0f), new Vector2(overW / (overSegs - 1), 70f), null, new Color(1f, 1f, 1f, 0f), 1f);
            piece.raycastTarget = true;
            var bt = piece.gameObject.AddComponent<Button>();
            var nav = bt.navigation; nav.mode = Navigation.Mode.None; bt.navigation = nav; bt.targetGraphic = piece;
            var pb = piece.gameObject.AddUdonSharpComponent<PHButton>();
            pb.game = game; pb.target = rp; pb.action = nameof(PHReplay.Seek); pb.arg = PHReplay.OVER_ARG + s + 1;
            UdonSharpEditorUtility.CopyProxyToUdon(pb);
            UnityEditor.Events.UnityEventTools.AddStringPersistentListener(bt.onClick, UdonSharpEditorUtility.GetBackingUdonBehaviour(pb).SendCustomEvent, nameof(PHButton.Press));
        }
        ohead.transform.SetAsLastSibling();
        rp.overClock = Txt(oc, "Clock", new Vector2(0f, 30f), new Vector2(300f, 40f), "0:00 / 0:00", future, 22, C("f6ecd4"), TextAlignmentOptions.Center);
        rp.overhead = oc; rp.overHead = ohead.transform; rp.overHunt = ohunt.transform; rp.overWidth = overW; rp.overSegments = overSegs;
        oc.gameObject.SetActive(false);

        UdonSharpEditorUtility.CopyProxyToUdon(rp);
        return rp;
    }

    static Renderer Shape(Transform parent, PrimitiveType type, Vector3 pos, Vector3 size, Material mat)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = type.ToString(); g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localScale = size;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return r;
    }

    // a button of the replay's row (DebugButton's look, any size): its label returned (under Button/Face)
    static TextMeshProUGUI ReplayButton(Transform parent, PHGame game, string label, Vector2 at, Vector2 size, Color colour, UdonSharpBehaviour target, string action, int arg)
    {
        var bimg = Img(parent, "Button " + label, at, size, sRound, new Color(1f, 1f, 1f, 0.01f), 3f);
        bimg.raycastTarget = true;
        var bt = bimg.gameObject.AddComponent<Button>();
        var bn = bt.navigation; bn.mode = Navigation.Mode.None; bt.navigation = bn; bt.targetGraphic = bimg;
        var face = Img(bimg.transform, "Face", Vector2.zero, size, sTile, colour, 2f);
        var text = Txt(face.transform, "Text", new Vector2(0, 3), new Vector2(size.x - 14f, 40f), label, future, 16, C("1e190f"), TextAlignmentOptions.Center);
        var btn = bimg.gameObject.AddUdonSharpComponent<PHButton>();
        btn.game = game; btn.action = action; btn.target = target; btn.arg = arg;
        UdonSharpEditorUtility.CopyProxyToUdon(btn);
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(bt.onClick, UdonSharpEditorUtility.GetBackingUdonBehaviour(btn).SendCustomEvent, nameof(PHButton.Press));
        Pressy(bimg.gameObject, bt);
        return text;
    }
}
