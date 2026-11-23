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
        if (pawnMat == null) { pawnMat = new Material(Shader.Find("PropHunt/DitherPulse")) { name = "ReplayPawn" }; AssetDatabase.CreateAsset(pawnMat, "Assets/Udon/Generated/ReplayPawn.mat"); }
        // (flat colour, solid at alpha 1; at 0.5 a see-through screen: a pawn up on a floor switched off, PHReplay.Screen)
        pawnMat.shader = Shader.Find("PropHunt/DitherPulse"); pawnMat.SetFloat("_Flat", 1f); pawnMat.color = Color.white; EditorUtility.SetDirty(pawnMat);
        var lineMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/MiniMapLine.mat");
        // a hider's meeple: flat in their colour (block), shaded by a fixed light so its shape reads (DitherPulse _Lit)
        var hiderMeepleMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/ReplayHiderMeeple.mat");
        if (hiderMeepleMat == null) { hiderMeepleMat = new Material(Shader.Find("PropHunt/DitherPulse")) { name = "ReplayHiderMeeple" }; AssetDatabase.CreateAsset(hiderMeepleMat, "Assets/Udon/Generated/ReplayHiderMeeple.mat"); }
        hiderMeepleMat.shader = Shader.Find("PropHunt/DitherPulse"); hiderMeepleMat.SetFloat("_Flat", 1f); hiderMeepleMat.SetFloat("_Lit", 1f); hiderMeepleMat.color = Color.white; EditorUtility.SetDirty(hiderMeepleMat);
        rp.pawns = new Transform[n]; rp.pawnRenderer = new Renderer[n]; rp.pops = new Transform[n]; rp.popRenderer = new Renderer[n];
        rp.tails = new LineRenderer[n]; rp.tags = new Transform[n]; rp.tagLabel = new TextMeshPro[n];
        // a hider's taunt (Cor, 2026-11-22): a dithered pulse around them, in their colour (PropHunt/DitherPulse)
        var tauntMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/ReplayTaunt.mat");
        if (tauntMat == null) { tauntMat = new Material(Shader.Find("PropHunt/DitherPulse")) { name = "ReplayTaunt" }; AssetDatabase.CreateAsset(tauntMat, "Assets/Udon/Generated/ReplayTaunt.mat"); }
        tauntMat.shader = Shader.Find("PropHunt/DitherPulse"); tauntMat.enableInstancing = true; EditorUtility.SetDirty(tauntMat);
        rp.taunts = new Transform[n]; rp.tauntRenderer = new Renderer[n];
        rp.guns = new Transform[n]; rp.gunModel = new MeshFilter[n]; rp.gunFlash = new Transform[n]; rp.gunFlashRenderer = new Renderer[n];
        var flashMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/ReplayGunFlash.mat");
        if (flashMat == null) { flashMat = new Material(Shader.Find("PropHunt/DitherPulse")) { name = "ReplayGunFlash" }; AssetDatabase.CreateAsset(flashMat, "Assets/Udon/Generated/ReplayGunFlash.mat"); }
        flashMat.shader = Shader.Find("PropHunt/DitherPulse"); flashMat.SetFloat("_Flat", 1f); flashMat.color = new Color(1f, 0.85f, 0.35f, 1f); EditorUtility.SetDirty(flashMat);
        // a hunter (Cor, 2026-11-22): the red meeple (the board game toys' meeple_red, as the props bake it), holding the
        // weapon they had (PHReplay fills the mesh), swinging it hard each time they swung or hit something. A body
        // to lean the swing, a hand each side to hold the weapon and a two-piece set's other piece. No colliders.
        var meepleSrc = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Udon/Generated/Props/445_bg_meeple.prefab");
        Mesh meepleMesh = null; Material meepleMat = null;
        if (meepleSrc != null)
            foreach (var mf in meepleSrc.GetComponentsInChildren<MeshFilter>(true))
                if (mf.gameObject.name != "Shade" && mf.sharedMesh != null) { meepleMesh = mf.sharedMesh; meepleMat = mf.GetComponent<MeshRenderer>().sharedMaterial; break; }
        if (meepleMesh == null) Debug.LogWarning("[prophunt] replay: the meeple prop is missing; hunters stay capsules");
        var weaponMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/Weapon.mat");
        const float MEEPLE_H = 1.8f;
        float msc = 1f; Vector3 mc = Vector3.zero, me = Vector3.one;
        if (meepleMesh != null) { var bb = meepleMesh.bounds; msc = MEEPLE_H / Mathf.Max(0.01f, bb.size.y); mc = bb.center; me = bb.extents; }
        rp.meeples = new Transform[n]; rp.meepleBody = new Transform[n]; rp.meepleHand = new Transform[n]; rp.meepleOffHand = new Transform[n];
        rp.meepleWeapon = new MeshFilter[n]; rp.meepleOff = new MeshFilter[n]; rp.meepleFig = new MeshRenderer[n];
        for (int i = 0; i < n && meepleMesh != null; i++)
        {
            var mp = new GameObject("ReplayMeeple" + i);
            mp.transform.SetParent(mm.model, false);
            var body = new GameObject("Body").transform;
            body.SetParent(mp.transform, false);
            var fig = new GameObject("Meeple");
            fig.transform.SetParent(body, false);
            fig.transform.localScale = Vector3.one * msc;
            fig.transform.localPosition = new Vector3(-mc.x, me.y - mc.y, -mc.z) * msc;   // (feet at the origin, centred)
            fig.AddComponent<MeshFilter>().sharedMesh = meepleMesh;
            var fr = fig.AddComponent<MeshRenderer>(); fr.sharedMaterial = meepleMat;
            fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; fr.receiveShadows = false;
            rp.meepleFig[i] = fr;
            // the hands at the arms' ends, a little over half way up
            float hx = me.x * msc * 0.85f, hy = MEEPLE_H * 0.58f;
            for (int h = 0; h < 2; h++)
            {
                var hand = new GameObject(h == 0 ? "Hand" : "OffHand").transform;
                hand.SetParent(body, false);
                hand.localPosition = new Vector3(h == 0 ? hx : -hx, hy, 0.05f);
                var wgo = new GameObject("Weapon");
                wgo.transform.SetParent(hand, false);
                var wf = wgo.AddComponent<MeshFilter>();
                var wr = wgo.AddComponent<MeshRenderer>(); wr.sharedMaterial = weaponMat;
                wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; wr.receiveShadows = false;
                if (h == 0) { rp.meepleHand[i] = hand; rp.meepleWeapon[i] = wf; }
                else { rp.meepleOffHand[i] = hand; rp.meepleOff[i] = wf; wgo.SetActive(false); }
            }
            mp.SetActive(false);
            rp.meeples[i] = mp.transform; rp.meepleBody[i] = body;
        }
        for (int i = 0; i < n; i++)
        {
            var pawn = new GameObject("ReplayPawn" + i);
            pawn.transform.SetParent(mm.model, false);
            // (Cor: a hider before they take a prop is a meeple too, in their own colour; a capsule only without the meeple)
            if (meepleMesh != null)
            {
                var hf = new GameObject("Meeple");
                hf.transform.SetParent(pawn.transform, false);
                hf.transform.localScale = Vector3.one * msc;
                hf.transform.localPosition = new Vector3(-mc.x, me.y - mc.y, -mc.z) * msc;
                hf.AddComponent<MeshFilter>().sharedMesh = meepleMesh;
                var hr = hf.AddComponent<MeshRenderer>(); hr.sharedMaterial = hiderMeepleMat;
                hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; hr.receiveShadows = false;
                rp.pawnRenderer[i] = hr;
            }
            else rp.pawnRenderer[i] = Shape(pawn.transform, PrimitiveType.Capsule, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), pawnMat);
            pawn.SetActive(false);
            rp.pawns[i] = pawn.transform;

            var pop = new GameObject("ReplayPop" + i);
            pop.transform.SetParent(mm.model, false);
            rp.popRenderer[i] = Shape(pop.transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one, pawnMat);
            pop.SetActive(false);
            rp.pops[i] = pop.transform;

            var taunt = new GameObject("ReplayTaunt" + i);
            taunt.transform.SetParent(mm.model, false);
            rp.tauntRenderer[i] = Shape(taunt.transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one, tauntMat);
            taunt.SetActive(false);
            rp.taunts[i] = taunt.transform;

            // a hider's gun in the final stand (PHReplay.Gun): a holder turned to the shot, the model under it (its grip,
            // turn and size set at run time, PHWeapons.PutGunModel), and a flash at the muzzle
            var gun = new GameObject("ReplayGun" + i);
            gun.transform.SetParent(mm.model, false);
            var gmo = new GameObject("Gun");
            gmo.transform.SetParent(gun.transform, false);
            var gmf = gmo.AddComponent<MeshFilter>();
            var gmr = gmo.AddComponent<MeshRenderer>(); gmr.sharedMaterial = weaponMat;
            gmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; gmr.receiveShadows = false;
            gun.SetActive(false);
            rp.guns[i] = gun.transform; rp.gunModel[i] = gmf;
            var flash = new GameObject("ReplayGunFlash" + i);
            flash.transform.SetParent(mm.model, false);
            rp.gunFlashRenderer[i] = Shape(flash.transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one, flashMat);
            flash.SetActive(false);
            rp.gunFlash[i] = flash.transform;

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
        // (Cor: a seek bar on every side of the table) the same row on all four edges, each turned to its side; any
        // row's press is shared (PHReplay), the near edge's is index 0
        float down = 0.18f, a = 35f * Mathf.Deg2Rad;
        const float barW = 760f; const int segs = 20; const int sides = 4;
        rp.bar = new GameObject[sides];
        rp.playLabel = new TextMeshProUGUI[sides]; rp.speedLabel = new TextMeshProUGUI[sides];
        rp.clock = new TextMeshProUGUI[sides];
        rp.playhead = new Transform[sides]; rp.huntMark = new Transform[sides];
        var only = new System.Collections.Generic.List<GameObject>();
        for (int sd = 0; sd < sides; sd++)
        {
            var turn = Quaternion.Euler(0f, sd * 90f, 0f);
            var near = new Vector3(0f, top + 0.12f - down * Mathf.Cos(a), -side / 2 - 0.1f - down * Mathf.Sin(a));
            var cv = Canvas(table, "ReplayControls" + sd, turn * near, turn * Quaternion.Euler(35f, 0f, 0f), new Vector2(1400f, 80f), 0.002f, true);
            cv.GetComponent<BoxCollider>().isTrigger = true;   // (Cor: no colliders on the seek bars; still pressable, nothing bumps or strikes it)
            var bar = new GameObject("Bar", typeof(RectTransform));
            bar.transform.SetParent(cv, false);
            ((RectTransform)bar.transform).sizeDelta = new Vector2(1400f, 80f);
            rp.bar[sd] = bar;
            rp.playLabel[sd] = ReplayButton(bar.transform, game, "Play", new Vector2(-580f, 0f), new Vector2(220f, 64f), C("f2c45a"), rp, nameof(PHReplay.PlayPause), 0);
            rp.speedLabel[sd] = ReplayButton(bar.transform, game, "1×", new Vector2(-415f, 0f), new Vector2(90f, 64f), C("e8dcc0"), rp, nameof(PHReplay.Speed), 0);
            only.Add(rp.speedLabel[sd].transform.parent.parent.gameObject);
            // the scrub bar: a track, the hunt's start on it, the playhead, and pressable pieces over it
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
                pb.game = game; pb.target = rp; pb.action = nameof(PHReplay.Seek); pb.arg = sd * PHReplay.SIDE_ARG + s + 1;
                UdonSharpEditorUtility.CopyProxyToUdon(pb);
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(bt.onClick, UdonSharpEditorUtility.GetBackingUdonBehaviour(pb).SendCustomEvent, nameof(PHButton.Press));
            }
            rp.playhead[sd] = head.transform; rp.huntMark[sd] = hunt.transform;
            head.transform.SetAsLastSibling();
            only.Add(track.gameObject);
            rp.clock[sd] = Txt(bar.transform, "Clock", new Vector2(470f, 3f), new Vector2(120f, 40f), "0:00 / 0:00", future, 16, C("f6ecd4"), TextAlignmentOptions.Center);
            only.Add(rp.clock[sd].gameObject);
            // (no summary button any more, Cor 2026-11-22: the replay is the diorama's view)
            bar.SetActive(false);
        }
        rp.barWidth = barW; rp.segments = segs;
        rp.playingOnly = only.ToArray();
        foreach (var g in rp.playingOnly) g.SetActive(false);

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
