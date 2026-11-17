using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

// The lobby's look (Cor's "Board Game" design, 2026-11-16): the room is the inside of a game box. Called by
// WorldSceneBuilder.Build; the room's size, the zone, the spawn and the panel's place stay where that puts them.
//   BuildPanel     the host panel: four coloured tracks with a meeple on the pick, a die for START, the bench
//   BuildScorePad  the last round's results as a spiral notepad on the west wall
//   DressRoom      board floor with a track of coloured spaces, the PLAY space, wall bands, the PROPHUNT name,
//                  a prop shelf on the east wall and giant game pieces (KayKit models, CC0)
// Assets live in Assets/Lobby (icons are 256 px renders from cc0.cor.home with the background keyed out).
public static class LobbyBuilder
{
    const string Root = "Assets/Lobby/";
    static TMP_FontAsset future, rocket;
    static Sprite sTile, sRound, sDashed, sDash;

    static readonly Color Cream = C("f6ecd4"), BoardEdge = C("e1cfa5"), Felt = C("21573f"), Ink = C("6d5524");
    static readonly Color[] RowColour = { C("7fb3e6"), C("f2c45a"), C("9bd18a"), C("f19a8a") };

    static Color C(string hex, float a = 1f)
    {
        Color c; ColorUtility.TryParseHtmlString("#" + hex, out c); c.a = a; return c;
    }

    // ------------------------------------------------------------------ assets
    public static void EnsureAssets()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "Icons" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            string n = System.IO.Path.GetFileNameWithoutExtension(p);
            Vector4 border = n == "ui_tile" ? new Vector4(30, 40, 30, 30) : n == "ui_round" ? new Vector4(30, 30, 30, 30) : n == "ui_dashed" ? new Vector4(38, 38, 38, 38) : Vector4.zero;
            bool change = ti.textureType != TextureImporterType.Sprite || ti.spriteBorder != border;
            if (!change) continue;
            ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.spriteBorder = border;
            ti.wrapMode = n == "ui_dash" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            var s = new TextureImporterSettings(); ti.ReadTextureSettings(s); s.spriteMeshType = SpriteMeshType.FullRect; ti.SetTextureSettings(s);
            ti.SaveAndReimport();
        }
        sTile = Icon("ui_tile"); sRound = Icon("ui_round"); sDashed = Icon("ui_dashed"); sDash = Icon("ui_dash");
        const string chars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]_abcdefghijklmnopqrstuvwxyz·…";
        future = Font("Assets/Resources/PropHunt/Fonts/KenneyFuture.ttf", Root + "Fonts/KenneyFuture SDF.asset", chars);
        rocket = Font(Root + "Fonts/KenneyRocket.ttf", Root + "Fonts/KenneyRocket SDF.asset", chars);
    }

    static Sprite Icon(string n) { return AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Icons/" + n + ".png"); }

    static TMP_FontAsset Font(string ttf, string path, string chars)
    {
        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (fa != null) return fa;
        var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
        fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
        fa.TryAddCharacters(chars);
        fa.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(fa, path);
        fa.atlasTextures[0].name = System.IO.Path.GetFileNameWithoutExtension(path) + " Atlas";
        AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        fa.material.name = System.IO.Path.GetFileNameWithoutExtension(path) + " Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        return fa;
    }

    // ------------------------------------------------------------------ UI helpers (positions are centres)
    static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = 0;
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    static Image Img(Transform parent, string name, Vector2 pos, Vector2 size, Sprite s, Color c, float ppuMul = 1f)
    {
        var img = Rect(parent, name, pos, size).gameObject.AddComponent<Image>();
        img.sprite = s; img.color = c; img.raycastTarget = false; img.preserveAspect = s != null && s.border == Vector4.zero && s != sDash;
        if (s != null && s.border != Vector4.zero) { img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = ppuMul; }
        if (s == sDash) { img.type = Image.Type.Tiled; img.pixelsPerUnitMultiplier = ppuMul; }
        return img;
    }

    static TextMeshProUGUI Txt(Transform parent, string name, Vector2 pos, Vector2 size, string s, TMP_FontAsset f, float fs, Color c, TextAlignmentOptions al)
    {
        var t = Rect(parent, name, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
        if (f != null) t.font = f;
        t.text = s; t.fontSize = fs; t.color = c; t.alignment = al; t.raycastTarget = false; t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    static Transform Canvas(Transform parent, string name, Vector3 localPos, Quaternion rot, Vector2 size, float scale, bool interactive)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos; go.transform.localRotation = rot;
        go.layer = 0;
        var c = go.AddComponent<UnityEngine.Canvas>(); c.renderMode = RenderMode.WorldSpace;
        var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = size;
        go.transform.localScale = Vector3.one * scale;
        if (interactive)
        {
            go.AddComponent<GraphicRaycaster>();
            var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(size.x, size.y, 1f);
            go.AddComponent<VRC.SDK3.Components.VRCUiShape>();
        }
        return go.transform;
    }

    // ------------------------------------------------------------------ the host panel
    // 1200 x 560 canvas units at 6 mm: 7.2 x 3.36 m. Mock-up coordinates are top-left based; P() converts.
    const float PW = 1200f, PH = 560f;   // the board is the top 440; the host bar and the map bar are under it
    static Vector2 P(float x, float y) { return new Vector2(x - PW / 2, PH / 2 - y); }

    public static PHPanel BuildPanel(Transform lobby, PHGame game, Vector3 localPos)
    {
        var cv = Canvas(lobby, "HostPanel", localPos, Quaternion.identity, new Vector2(PW, PH), 0.006f, true);
        var bg = cv.gameObject.AddComponent<Image>(); bg.color = Felt; bg.raycastTarget = false;

        Img(cv, "BoardShadow", P(600, 226), new Vector2(1168, 408), sRound, C("173d2c"), 1.6f);
        Img(cv, "BoardEdge", P(600, 220), new Vector2(1168, 408), sRound, BoardEdge, 1.6f);
        Img(cv, "Board", P(600, 220), new Vector2(1158, 398), sRound, Cream, 1.6f);
        var div = Img(cv, "Divider", P(894, 220), new Vector2(380, 4), sDash, C("d9c49a"), 5f);
        div.rectTransform.localRotation = Quaternion.Euler(0, 0, 90);

        var panel = cv.gameObject.AddUdonSharpComponent<PHPanel>();
        panel.game = game;
        panel.spinner = Spinner;

        string[] titles = { "MAP", "PROPS", "ROUND", "TAUNT" };
        int[][] args = { new[] { -1, 0, 1, 2, 3 }, new[] { 3, 1, 0, 2, 4 }, new[] { 0, 1, 2 }, new[] { 30, 40, 60 } };
        string[][] caps = { new[] { "Auto · S", "S", "M", "L", "XL" }, new[] { "V. sparse", "Sparse", "Normal", "Dense", "V. dense" },
                            new[] { "2:15", "3:00", "4:30" }, new[] { "30 s", "40 s", "60 s" } };
        int total = 16;
        panel.tileKind = new int[total]; panel.tileArg = new int[total];
        panel.tileMarker = new GameObject[total]; panel.tileAuto = new GameObject[total]; panel.tileRect = new RectTransform[total];
        panel.roundCaption = new TextMeshProUGUI[3];
        var marker = Icon("meeple_hunt");
        var tiles = new PHPanelTile[total];
        int t = 0;
        for (int row = 0; row < 4; row++)
        {
            float cy = 30 + row * 97 + 44.5f;
            Txt(cv, titles[row], P(80, cy), new Vector2(96, 30), titles[row], future, 17, Ink, TextAlignmentOptions.Right);
            Img(cv, "Path" + row, P(508, cy), new Vector2(708, 10), sDash, C("dcc89e"), 2f);
            for (int j = 0; j < args[row].Length; j++, t++)
            {
                float cx = 140 + j * 120 + 55;
                var tile = Img(cv, "Tile " + titles[row] + " " + caps[row][j], P(cx, cy), new Vector2(110, 88), sTile, RowColour[row], 2f);
                tile.raycastTarget = true;
                var b = tile.gameObject.AddComponent<Button>();
                var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
                b.targetGraphic = tile;
                var cb = b.colors; cb.highlightedColor = new Color(1.06f, 1.06f, 1.06f); cb.pressedColor = new Color(0.85f, 0.85f, 0.85f); b.colors = cb;
                var auto = Img(tile.transform, "AutoPick", Vector2.zero, new Vector2(126, 104), sDashed, C("b8892b"), 2f);
                TileIcon(tile.transform, row, j);
                var cap = Txt(tile.transform, "Caption", new Vector2(0, -32), new Vector2(110, 16), caps[row][j], future, 13, C("1e190f", 0.85f), TextAlignmentOptions.Center);
                if (row == 0 && j == 0) panel.autoCaption = cap;
                if (row == 2) panel.roundCaption[j] = cap;
                var mk = Img(tile.transform, "Marker", new Vector2(42, 47), new Vector2(54, 54), marker, Color.white);
                panel.tileKind[t] = row; panel.tileArg[t] = args[row][j];
                panel.tileMarker[t] = mk.gameObject; panel.tileAuto[t] = auto.gameObject; panel.tileRect[t] = tile.rectTransform;
                tiles[t] = AddTile(tile.gameObject, b, t);
                mk.gameObject.SetActive(false); auto.gameObject.SetActive(false);
            }
        }

        // the die (START) and its caption
        var dice = Img(cv, "Die", P(1039, 116), new Vector2(172, 172), Icon("dice"), Color.white);
        dice.raycastTarget = true;
        var db = dice.gameObject.AddComponent<Button>();
        var dn = db.navigation; dn.mode = Navigation.Mode.None; db.navigation = dn; db.targetGraphic = dice;
        var diceTile = AddTile(dice.gameObject, db, -1);
        panel.dice = dice.rectTransform; panel.diceImage = dice; panel.diceY = dice.rectTransform.anchoredPosition.y;
        panel.rollCaption = Txt(cv, "RollToStart", P(1039, 214), new Vector2(270, 30), "ROLL TO START", rocket, 20, Felt, TextAlignmentOptions.Center);

        // the bench: who stands on PLAY
        Img(cv, "Bench", P(1039, 345), new Vector2(262, 126), sRound, C("e9dab6"), 2.2f);
        panel.benchText = Txt(cv, "BenchText", P(1039, 300), new Vector2(250, 22), "0 Props · 0 Hunters", future, 14, Ink, TextAlignmentOptions.Center);
        var meeples = Rect(cv, "Meeples", P(1039, 330), new Vector2(250, 30));
        panel.benchMeeples = new Image[32];
        for (int i = 0; i < 32; i++)
        {
            var m = Img(meeples, "Meeple" + i, Vector2.zero, new Vector2(30, 30), Icon("meeple_hide"), Color.white);
            m.gameObject.SetActive(false);
            panel.benchMeeples[i] = m;
        }
        panel.meepleHunt = Icon("meeple_hunt"); panel.meepleHide = Icon("meeple_hide");

        // the lock during a round: dims the board, blocks the buttons
        var veil = Img(cv, "Veil", P(600, 220), new Vector2(PW, 440), null, new Color(0.06f, 0.06f, 0.09f, 0.62f));
        veil.raycastTarget = true;
        var hg = Img(veil.transform, "Hourglass", new Vector2(-190, 0), new Vector2(86, 86), Icon("hourglass"), Color.white);
        Txt(veil.transform, "Text", new Vector2(70, 0), new Vector2(420, 50), "Round in progress...", rocket, 32, Color.white, TextAlignmentOptions.Left);
        panel.veil = veil.gameObject; panel.veilHourglass = hg.rectTransform;
        veil.gameObject.SetActive(false);

        // the host bar: who hosts, and Unlock / Claim (the host can always be handed over, even during a round)
        Img(cv, "HostBar", P(600, 470), new Vector2(1168, 48), sRound, C("173d2c"), 3f);
        Img(cv, "HostIcon", P(46, 468), new Vector2(40, 40), Icon("meeple_hunt"), Color.white);
        Txt(cv, "HostLabel", P(110, 470), new Vector2(80, 26), "HOST", future, 16, C("e1cfa5"), TextAlignmentOptions.Left);
        panel.hostName = Txt(cv, "HostName", P(330, 470), new Vector2(360, 34), "", null, 24, Color.white, TextAlignmentOptions.Left);
        panel.hostName.fontStyle = FontStyles.Bold;
        panel.hostButton = new RectTransform[9]; panel.hostButtonImage = new Image[9]; panel.hostButtonText = new TextMeshProUGUI[9];
        string[] hb = { "UNLOCK HOST", "CLAIM HOST" };
        var hostTiles = new PHPanelTile[9];
        for (int k = 0; k < 2; k++)
        {
            var bimg = Img(cv, "HostButton " + hb[k], P(880 + k * 200, 470), new Vector2(186, 38), sRound, new Color(1f, 1f, 1f, 0.01f), 4f);
            bimg.raycastTarget = true;
            var bt = bimg.gameObject.AddComponent<Button>();
            var bn = bt.navigation; bn.mode = Navigation.Mode.None; bt.navigation = bn; bt.targetGraphic = bimg;
            var face = Img(bimg.transform, "Face", Vector2.zero, new Vector2(186, 38), sRound, k == 0 ? C("f2c45a") : C("9bd18a"), 4f);
            panel.hostButtonText[k] = Txt(bimg.transform, "Text", Vector2.zero, new Vector2(180, 30), hb[k], future, 15, C("1e190f"), TextAlignmentOptions.Center);
            panel.hostButton[k] = bimg.rectTransform; panel.hostButtonImage[k] = face;
            hostTiles[k] = AddTile(bimg.gameObject, bt, -2 - k);
            if (k == 1)
            {
                var lk = Img(bimg.transform, "Lock", Vector2.zero, new Vector2(28, 28), Icon("lock"), C("1e190f", 0.8f));
                panel.claimLock = lk.gameObject;
            }
        }

        // the map bar: the map's code, a code entry for the host, the seed message (or Play anyway / Discard), the
        // rotation's run count and cap, and the scrap / undo votes
        Img(cv, "MapBar", P(600, 530), new Vector2(1168, 48), sRound, C("173d2c"), 3f);
        Txt(cv, "MapLabel", P(64, 530), new Vector2(80, 26), "MAP", future, 16, C("e1cfa5"), TextAlignmentOptions.Left);
        panel.seedCodeText = Txt(cv, "SeedCode", P(205, 530), new Vector2(200, 30), "-", future, 15, Color.white, TextAlignmentOptions.Left);
        // host-only code entry (a TMP input field: VRChat opens its keyboard)
        var fieldBg = Img(cv, "SeedField", P(400, 530), new Vector2(180, 38), sRound, C("f6ecd4"), 4f);
        fieldBg.raycastTarget = true;
        panel.seedInputRoot = fieldBg.gameObject;
        var area = Rect(fieldBg.transform, "Text Area", Vector2.zero, new Vector2(164, 32));
        area.gameObject.AddComponent<RectMask2D>();
        var ph = Txt(area, "Placeholder", Vector2.zero, new Vector2(164, 32), "ENTER CODE", future, 14, C("6d5524", 0.55f), TextAlignmentOptions.Center);
        var tx = Txt(area, "Text", Vector2.zero, new Vector2(164, 32), "", future, 16, C("1e190f"), TextAlignmentOptions.Center);
        var input = fieldBg.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area; input.textComponent = tx; input.placeholder = ph; input.fontAsset = future;
        input.characterLimit = 24; input.lineType = TMP_InputField.LineType.SingleLine; input.targetGraphic = fieldBg;
        var inav = input.navigation; inav.mode = Navigation.Mode.None; input.navigation = inav;
        panel.seedInput = input;
        panel.hostButton[2] = fieldBg.rectTransform; panel.hostButtonImage[2] = fieldBg;
        var panelUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(input.onEndEdit, panelUdon.SendCustomEvent, nameof(PHPanel.SeedEntered));
        panel.seedStatusText = Txt(cv, "SeedStatus", P(612, 530), new Vector2(232, 40), "", future, 11, C("c9d8c9"), TextAlignmentOptions.Center);
        panel.seedStatusText.enableWordWrapping = true;
        var confirm = Rect(cv, "SeedConfirm", P(600, 530), new Vector2(240, 40));
        panel.confirmRoot = confirm.gameObject;
        // the remaining bar buttons: k, label, centre x, width, colour, parent
        object[][] bar = {
            new object[] { 3, "PLAY ANYWAY", 545f, 116f, C("9bd18a") }, new object[] { 4, "DISCARD", 662f, 104f, C("f19a8a") },
            new object[] { 5, "-", 765f, 38f, C("e1cfa5") }, new object[] { 6, "+", 905f, 38f, C("e1cfa5") },
            new object[] { 7, "SCRAP", 1002f, 124f, C("f19a8a") }, new object[] { 8, "UNDO", 1122f, 108f, C("7fb3e6") } };
        foreach (var b in bar)
        {
            int k = (int)b[0]; float x = (float)b[2], w = (float)b[3];
            Transform parent = k == 3 || k == 4 ? confirm : cv;
            Vector2 at = k == 3 || k == 4 ? new Vector2(x - 600f, 0f) : P(x, 530);
            var bimg = Img(parent, "BarButton " + b[1], at, new Vector2(w, 36), sRound, new Color(1f, 1f, 1f, 0.01f), 4f);
            bimg.raycastTarget = true;
            var bt = bimg.gameObject.AddComponent<Button>();
            var bn = bt.navigation; bn.mode = Navigation.Mode.None; bt.navigation = bn; bt.targetGraphic = bimg;
            var face = Img(bimg.transform, "Face", Vector2.zero, new Vector2(w, 36), sRound, (Color)b[4], 4f);
            panel.hostButtonText[k] = Txt(bimg.transform, "Text", Vector2.zero, new Vector2(w - 6, 30), (string)b[1], future, k == 5 || k == 6 ? 22 : 13, C("1e190f"), TextAlignmentOptions.Center);
            panel.hostButton[k] = bimg.rectTransform; panel.hostButtonImage[k] = face;
            hostTiles[k] = AddTile(bimg.gameObject, bt, -2 - k);
        }
        panel.runsText = Txt(cv, "Runs", P(835, 530), new Vector2(96, 30), "RUN 0/10", future, 14, Color.white, TextAlignmentOptions.Center);
        panel.scrapText = panel.hostButtonText[7]; panel.undoText = panel.hostButtonText[8];
        confirm.gameObject.SetActive(false);

        // sounds, for everyone near the panel
        var src = cv.gameObject.AddComponent<AudioSource>();
        src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 4f; src.maxDistance = 30f; src.playOnAwake = false; src.volume = 0.7f;
        panel.sfx = src;
        panel.pluck = Clip("pluck_001"); panel.select = Clip("select_003"); panel.drop = Clip("drop_002"); panel.tick = Clip("tick_002");
        panel.bong = Clip("bong_001"); panel.error = Clip("error_004"); panel.jingle = Clip("jingles_NES03");

        UdonSharpEditorUtility.CopyProxyToUdon(panel);
        foreach (var tl in tiles) { tl.panel = panel; UdonSharpEditorUtility.CopyProxyToUdon(tl); }
        diceTile.panel = panel; UdonSharpEditorUtility.CopyProxyToUdon(diceTile);
        foreach (var ht in hostTiles) if (ht != null) { ht.panel = panel; UdonSharpEditorUtility.CopyProxyToUdon(ht); }
        return panel;
    }

    // Debug: a small sign beside the panel that builds a fresh random map for the presser and takes them there (PHExplore)
    public static PHExplore BuildExplore(Transform lobby, PHGame game, Vector3 localPos)
    {
        var cv = Canvas(lobby, "ExploreDebug", localPos, Quaternion.identity, new Vector2(300f, 130f), 0.006f, true);
        var bg = cv.gameObject.AddComponent<Image>(); bg.color = Felt; bg.raycastTarget = false;
        Txt(cv, "Label", new Vector2(0, 42), new Vector2(280, 24), "DEBUG", future, 14, C("e1cfa5"), TextAlignmentOptions.Center);
        var bimg = Img(cv, "Button", new Vector2(0, -12), new Vector2(270, 64), sRound, new Color(1f, 1f, 1f, 0.01f), 3f);
        bimg.raycastTarget = true;
        var bt = bimg.gameObject.AddComponent<Button>();
        var bn = bt.navigation; bn.mode = Navigation.Mode.None; bt.navigation = bn; bt.targetGraphic = bimg;
        Img(bimg.transform, "Face", Vector2.zero, new Vector2(270, 64), sTile, C("9bd18a"), 2f);
        var ex = cv.gameObject.AddUdonSharpComponent<PHExplore>();
        ex.game = game;
        ex.label = Txt(bimg.transform, "Text", new Vector2(0, 3), new Vector2(256, 40), "EXPLORE A NEW MAP", future, 16, C("1e190f"), TextAlignmentOptions.Center);
        var src = cv.gameObject.AddComponent<AudioSource>();
        src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 2f; src.maxDistance = 15f; src.playOnAwake = false; src.volume = 0.7f;
        ex.sfx = src; ex.select = Clip("select_003"); ex.error = Clip("error_004"); ex.done = Clip("jingles_NES03");
        UdonSharpEditorUtility.CopyProxyToUdon(ex);
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(ex);
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(bt.onClick, udon.SendCustomEvent, nameof(PHExplore.Press));
        return ex;
    }

    static AudioClip Clip(string n) { return AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/" + n + ".ogg"); }

    static PHPanelTile AddTile(GameObject go, Button b, int index)
    {
        var tile = go.AddUdonSharpComponent<PHPanelTile>();
        tile.index = index;
        UdonSharpEditorUtility.CopyProxyToUdon(tile);
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(tile);
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(b.onClick, udon.SendCustomEvent, nameof(PHPanelTile.Press));
        return tile;
    }

    static readonly float[][] Pile = {
        new[] { 19f, 22f }, new[] { 6f, 24f, 32f, 22f }, new[] { 2f, 26f, 36f, 26f, 19f, 4f },
        new[] { 0f, 26f, 24f, 28f, 46f, 24f, 12f, 6f }, new[] { 0f, 28f, 24f, 30f, 46f, 26f, 10f, 8f, 34f, 6f } };

    // the picture on a tile, in the 56-unit-tall icon area above the caption (its centre is 8 units above the tile's)
    static void TileIcon(Transform tile, int row, int j)
    {
        var ic = Rect(tile, "Icon", new Vector2(0, 8), new Vector2(76, 56));
        if (row == 0)
        {
            if (j == 0)
            {
                var hide = Icon("meeple_hide");
                Img(ic, "M1", new Vector2(-19, -9), new Vector2(34, 34), hide, Color.white);
                Img(ic, "M2", new Vector2(0, 5), new Vector2(34, 34), hide, Color.white);
                Img(ic, "M3", new Vector2(19, -9), new Vector2(34, 34), hide, Color.white);
                return;
            }
            string[] b = { "home", "tavern", "church", "castle" };
            float[] s = { 0.72f, 0.86f, 1f, 1.18f };
            float h = 58 * s[j - 1];
            Img(ic, "Building", new Vector2(0, -28 + h / 2), new Vector2(h, h), Icon(b[j - 1]), Color.white);
        }
        else if (row == 1)
        {
            int n = new[] { 1, 2, 3, 4, 5 }[j];
            var p = Pile[n - 1];
            for (int k = 0; k < n; k++)
                Img(ic, "Crate" + k, new Vector2(-35 + p[2 * k] + 16, 28 - p[2 * k + 1] - 16), new Vector2(32, 32), Icon("crate"), Color.white);
        }
        else if (row == 2)
        {
            float h = 58 * new[] { 0.6f, 0.8f, 1f }[j];
            Img(ic, "Hourglass", new Vector2(0, -28 + h / 2), new Vector2(h, h), Icon("hourglass"), Color.white);
        }
        else
        {
            int n = 3 - j;
            for (int k = 0; k < n; k++)
                Img(ic, "Bell" + k, new Vector2((k - (n - 1) * 0.5f) * 24f, -11), new Vector2(34, 34), Icon("bell2"), Color.white);
        }
    }

    // ------------------------------------------------------------------ the score pad (west wall)
    // 1400 x 980 canvas units at 4.3 mm: 6 x 4.2 m; facing east (into the room)
    const float SW = 1400f, SH = 980f;
    static Vector2 S(float x, float y) { return new Vector2(x - SW / 2, SH / 2 - y); }

    public static PHScorePad BuildScorePad(Transform lobby, PHGame game, PHLocal local, Vector3 localPos)
    {
        var cv = Canvas(lobby, "ScorePad", localPos, Quaternion.Euler(0f, -90f, 0f), new Vector2(SW, SH), 0.0043f, false);
        Img(cv, "Shadow", S(700, 500), new Vector2(SW, SH), sRound, C("c9b98f"), 3f);
        Img(cv, "Paper", S(700, 490), new Vector2(SW, SH - 20), sRound, C("fbf4df"), 3f);
        for (int i = 0; i < 22; i++) Img(cv, "Line" + i, S(700, 64 + i * 41), new Vector2(SW - 24, 2), null, C("d8e3ef"));
        Img(cv, "Margin", S(92, 490), new Vector2(4, SH - 24), null, C("e7887a"));
        for (int i = 0; i < 16; i++) Img(cv, "Ring" + i, S(60 + i * 85.3f, 10), new Vector2(18, 46), sRound, C("a9adb5"), 8f);

        var pad = cv.gameObject.AddUdonSharpComponent<PHScorePad>();
        pad.game = game; pad.local = local;
        pad.hidersColour = C("c48a12"); pad.huntersColour = C("c63a2c"); pad.idleColour = C("6b5d40");

        var hide = Icon("meeple_hide"); var hunt = Icon("meeple_hunt");
        var wh = Rect(cv, "WinHiders", S(200, 112), new Vector2(200, 90));
        for (int i = 0; i < 3; i++) Img(wh, "M" + i, new Vector2(-62 + i * 62, 0), new Vector2(86, 86), hide, Color.white);
        var wt = Rect(cv, "WinHunters", S(200, 112), new Vector2(200, 90));
        for (int i = 0; i < 2; i++) Img(wt, "M" + i, new Vector2(-32 + i * 64, 0), new Vector2(86, 86), hunt, Color.white);
        pad.winHiders = wh.gameObject; pad.winHunters = wt.gameObject;
        pad.title = Txt(cv, "Title", S(700, 96), new Vector2(560, 64), "NO ROUNDS YET", rocket, 50, pad.idleColour, TextAlignmentOptions.Left);
        pad.reason = Txt(cv, "Reason", S(700, 150), new Vector2(560, 30), "", future, 22, C("6b5d40"), TextAlignmentOptions.Left);
        var share = Rect(cv, "Survival", S(1205, 120), new Vector2(260, 80));
        pad.shareRoot = share.gameObject;
        Txt(share, "Label", new Vector2(-50, 14), new Vector2(160, 30), "SURVIVAL", future, 18, C("6b5d40"), TextAlignmentOptions.Left);
        pad.shareText = Txt(share, "Value", new Vector2(80, 16), new Vector2(100, 40), "0%", rocket, 32, C("2b2416"), TextAlignmentOptions.Right);
        Img(share, "Bar", new Vector2(0, -22), new Vector2(260, 18), sRound, C("e6d9b4"), 8f);
        var fill = Img(share, "Fill", new Vector2(0, -22), new Vector2(260, 18), null, C("f2c45a"));
        fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillAmount = 0.5f;
        pad.shareFill = fill;

        var cols = Rect(cv, "Columns", Vector2.zero, new Vector2(SW, SH));
        pad.columns = cols.gameObject;
        var ink = C("2b2416");
        Img(cols, "HidersIcon", S(140, 210), new Vector2(46, 46), hide, Color.white);
        Txt(cols, "HidersHead", S(290, 212), new Vector2(260, 36), "HIDERS", rocket, 28, ink, TextAlignmentOptions.Left);
        pad.hiderRow = new GameObject[24]; pad.hiderName = new TextMeshProUGUI[24]; pad.hiderProp = new TextMeshProUGUI[24]; pad.hiderResult = new TextMeshProUGUI[24];
        for (int i = 0; i < 24; i++)
        {
            int col = i / 8, r = i % 8;
            var row = Rect(cols, "Hider" + i, S(120 + col * 285 + 135, 266 + r * 82 + 30), new Vector2(270, 72));
            pad.hiderRow[i] = row.gameObject;
            pad.hiderName[i] = Txt(row, "Name", new Vector2(-30, 14), new Vector2(210, 30), "Player", null, 24, ink, TextAlignmentOptions.Left);
            pad.hiderName[i].fontStyle = FontStyles.Bold;
            pad.hiderProp[i] = Txt(row, "Prop", new Vector2(-30, -16), new Vector2(210, 24), "", future, 15, C("8a7b5a"), TextAlignmentOptions.Left);
            pad.hiderResult[i] = Txt(row, "Result", new Vector2(50, -16), new Vector2(170, 24), "", future, 15, C("2f7a3a"), TextAlignmentOptions.Right);
        }
        float hx = 1000;
        Img(cols, "HuntersIcon", S(hx + 20, 210), new Vector2(46, 46), hunt, Color.white);
        Txt(cols, "HuntersHead", S(hx + 170, 212), new Vector2(260, 36), "HUNTERS", rocket, 28, ink, TextAlignmentOptions.Left);
        string[] sub = { "HITS", "CATCHES", "WRONG" };
        float[] sx = { hx + 190, hx + 270, hx + 350 };
        for (int k = 0; k < 3; k++) Txt(cols, "Sub" + k, S(sx[k], 250), new Vector2(90, 20), sub[k], future, 13, C("8a7b5a"), TextAlignmentOptions.Center);
        pad.hunterRow = new GameObject[8]; pad.hunterName = new TextMeshProUGUI[8];
        pad.hunterHits = new TextMeshProUGUI[8]; pad.hunterCatches = new TextMeshProUGUI[8]; pad.hunterWrong = new TextMeshProUGUI[8];
        for (int i = 0; i < 8; i++)
        {
            float y = 286 + i * 60;
            var row = Rect(cols, "Hunter" + i, S(hx + 190, y), new Vector2(380, 50));
            pad.hunterRow[i] = row.gameObject;
            pad.hunterName[i] = Txt(row, "Name", new Vector2(-95, 0), new Vector2(190, 40), "Player", null, 24, ink, TextAlignmentOptions.Left);
            pad.hunterName[i].fontStyle = FontStyles.Bold;
            pad.hunterHits[i] = Txt(row, "Hits", new Vector2(sx[0] - hx - 190, 0), new Vector2(70, 40), "0", future, 22, ink, TextAlignmentOptions.Center);
            pad.hunterCatches[i] = Txt(row, "Catches", new Vector2(sx[1] - hx - 190, 0), new Vector2(70, 40), "0", future, 22, ink, TextAlignmentOptions.Center);
            pad.hunterWrong[i] = Txt(row, "Wrong", new Vector2(sx[2] - hx - 190, 0), new Vector2(70, 40), "0", future, 22, ink, TextAlignmentOptions.Center);
        }
        wh.gameObject.SetActive(false); wt.gameObject.SetActive(false); share.gameObject.SetActive(false); cols.gameObject.SetActive(false);
        UdonSharpEditorUtility.CopyProxyToUdon(pad);
        return pad;
    }

    // ------------------------------------------------------------------ the room
    // Plan coordinates: x 0..W from the west wall, y 0..D from the north wall (as in the mock-up); L() converts to the
    // lobby's local space (x east, z north, centred).
    static float RW, RD;
    static Vector3 L(float x, float y, float h = 0f) { return new Vector3(x - RW / 2, h, RD / 2 - y); }

    public static void DressRoom(Transform lobby, float roomW, float roomD, float wallH, float zoneHalf)
    {
        RW = roomW; RD = roomD;
        var root = new GameObject("Dressing").transform;
        root.SetParent(lobby, false);

        // the track of coloured spaces round the floor, and the path from the spawn to PLAY
        var tileMats = new Material[4];
        string[] tn = { "Blue", "Yellow", "Green", "Coral" };
        for (int i = 0; i < 4; i++) tileMats[i] = Mat("LobbySpace" + tn[i], RowColour[i]);
        int ci = 0;
        const float T = 1.4f, gap = 0.12f;
        System.Action<float, float, float, float> space = (x, y, w, d) =>
            Slab(root, "Space", L(x + w / 2, y + d / 2, 0.01f), new Vector3(w - 2 * gap, 0.02f, d - 2 * gap), tileMats[(ci++) % 4]);
        for (float x = 0; x < roomW - 0.01f; x += 2) space(x, roomD - T, 2, T);
        for (float y = roomD - T - 2; y > -1.99f; y -= 2) space(0, Mathf.Max(0, y), T, y < 0 ? 2 + y : 2);
        for (float y = 0; y < roomD - T - 0.01f; y += 2) space(roomW - T, y, T, Mathf.Min(2, roomD - T - y));
        space(11, 17.6f, 2, 2); space(11, 15.4f, 2, 2); space(11, 13.2f, 2, 2);

        // the PLAY space: a cream inset ring on the green zone floor, and the word
        var cream = Mat("LobbyCream", C("f3e7c9"));
        var zc = L(roomW / 2, 0.15f + zoneHalf);
        float zi = zoneHalf - 0.45f;
        Slab(root, "PlayRingN", zc + new Vector3(0, 0.012f, zi), new Vector3(2 * zi + 0.3f, 0.01f, 0.3f), cream);
        Slab(root, "PlayRingS", zc + new Vector3(0, 0.012f, -zi), new Vector3(2 * zi + 0.3f, 0.01f, 0.3f), cream);
        Slab(root, "PlayRingE", zc + new Vector3(zi, 0.012f, 0), new Vector3(0.3f, 0.01f, 2 * zi), cream);
        Slab(root, "PlayRingW", zc + new Vector3(-zi, 0.012f, 0), new Vector3(0.3f, 0.01f, 2 * zi), cream);
        var play = WorldText(root, "PLAY", zc + new Vector3(0, 0.02f, 0.6f), Quaternion.Euler(90f, 0f, 0f), "PLAY", 18f, C("e9f7df"), new Vector2(8f, 2.6f));
        play.characterSpacing = 4f;
        play.enableAutoSizing = true; play.fontSizeMin = 4f; play.fontSizeMax = 18f;   // stays inside the cream ring

        // the spawn disc
        var red = Mat("LobbySpawnRed", C("e05a48"));
        Disc(root, "SpawnDisc", L(roomW / 2, roomD - 2.2f, 0.012f), 1.8f, red);
        Disc(root, "SpawnRing", L(roomW / 2, roomD - 2.2f, 0.016f), 1.5f, cream);
        Disc(root, "SpawnDot", L(roomW / 2, roomD - 2.2f, 0.02f), 1.1f, red);

        // the walls: a cream band with a dashed rule along the top, and the name over the spawn
        var band = Mat("LobbyWallBand", C("f3e7c9"));
        var rule = Mat("LobbyWallRule", C("c9a46a"));
        Slab(root, "BandN", L(roomW / 2, 0.02f, wallH - 0.75f), new Vector3(roomW, 0.7f, 0.04f), band);
        Slab(root, "BandS", L(roomW / 2, roomD - 0.02f, wallH - 0.75f), new Vector3(roomW, 0.7f, 0.04f), band);
        Slab(root, "BandE", L(roomW - 0.02f, roomD / 2, wallH - 0.75f), new Vector3(0.04f, 0.7f, roomD), band);
        Slab(root, "BandW", L(0.02f, roomD / 2, wallH - 0.75f), new Vector3(0.04f, 0.7f, roomD), band);
        for (float x = 0.4f; x < roomW; x += 1.2f)
        {
            Slab(root, "Rule", L(x, 0.05f, wallH - 0.78f), new Vector3(0.7f, 0.12f, 0.02f), rule);
            Slab(root, "Rule", L(x, roomD - 0.05f, wallH - 0.78f), new Vector3(0.7f, 0.12f, 0.02f), rule);
        }
        for (float y = 0.4f; y < roomD; y += 1.2f)
        {
            Slab(root, "Rule", L(0.05f, y, wallH - 0.78f), new Vector3(0.02f, 0.12f, 0.7f), rule);
            Slab(root, "Rule", L(roomW - 0.05f, y, wallH - 0.78f), new Vector3(0.02f, 0.12f, 0.7f), rule);
        }
        var name = WorldText(root, "Name", L(roomW / 2, roomD - 0.03f, 4.3f), Quaternion.Euler(0f, 180f, 0f), "PROPHUNT", 26f, C("f6d773"), new Vector2(20f, 2.2f));
        name.characterSpacing = 6f;

        // the prop shelf on the east wall
        var wood = Mat("LobbyShelfWood", C("b07848"));
        float[] shelfH = { 0.85f, 2.1f, 3.35f };
        string[][] shelf = {
            new[] { "kaykit-dungeon-pack/barrel_small", "furniture-bits/lamp_standing", "furniture-bits/armchair", "kaykit-dungeon-pack/chest", "furniture-bits/cactus_medium_A", "furniture-bits/book_set", "kaykit-dungeon-pack/box_small" },
            new[] { "furniture-bits/chair_stool_wood", "furniture-bits/couch_pillows", "furniture-bits/lamp_table", "kaykit-dungeon-pack/barrel_small", "board-game-bits/D6_A_red", "kaykit-dungeon-pack/chest", "furniture-bits/book_set" },
            new[] { "kaykit-dungeon-pack/box_small", "furniture-bits/cactus_medium_A", "furniture-bits/armchair", "furniture-bits/lamp_table", "furniture-bits/lamp_standing", "board-game-bits/coin_gold", "kaykit-dungeon-pack/barrel_small" } };
        float[][] shelfSize = { new[] { 0.8f, 1.1f, 0.9f, 0.8f, 0.8f, 0.6f, 0.7f }, new[] { 0.7f, 1.0f, 0.7f, 0.7f, 0.6f, 0.7f, 0.6f }, new[] { 0.7f, 0.8f, 0.8f, 0.7f, 1.0f, 0.6f, 0.7f } };
        for (int s = 0; s < 3; s++)
        {
            Box(root, "Shelf" + s, L(roomW - 0.3f, roomD / 2, shelfH[s] - 0.05f), new Vector3(0.6f, 0.1f, 12f), wood);
            for (int k = 0; k < shelf[s].Length; k++)
                Piece(root, shelf[s][k], L(roomW - 0.32f, roomD / 2 - 5.1f + k * 1.7f, shelfH[s]), shelfSize[s][k], -90f + (k % 2 == 0 ? 15f : -10f), false);
        }

        // giant pieces (feet on the floor; sizes are the largest dimension in metres)
        Piece(root, "board-game-bits/D6_A_red", L(20.8f, 2.2f), 2.3f, 20f, true);
        Piece(root, "board-game-bits/D20_blue", L(22.2f, 5.4f), 1.8f, -15f, true);
        Stack(root, L(2.6f, 2.2f), 3, 1.6f); Stack(root, L(4.6f, 3.4f), 2, 1.5f); Stack(root, L(2.4f, 5.0f), 1, 1.4f);
        Piece(root, "furniture-bits/couch_pillows", L(2.3f, 15.0f), 2.6f, 90f, true);
        Piece(root, "furniture-bits/armchair", L(2.3f, 18.3f), 1.5f, 90f, true);
        Piece(root, "furniture-bits/lamp_standing", L(1.7f, 16.9f), 2.4f, 0f, false);
        Piece(root, "board-game-bits/pawn_A_blue", L(9.2f, 20.3f), 2.4f, 0f, true);
        Piece(root, "board-game-bits/flag_A_red", L(14.8f, 20.3f), 2.6f, 200f, true);
        Piece(root, "board-game-bits/domino_tile_6-6", L(19.4f, 19.5f), 2.4f, 25f, true);
        Piece(root, "furniture-bits/cactus_medium_A", L(22.2f, 20.0f), 1.6f, 0f, true);
        Piece(root, "furniture-bits/lamp_standing", L(22.3f, 18.4f), 2.4f, 0f, false);
        Piece(root, "board-game-bits/meeple_blue", L(18.8f, 9.0f), 1.7f, -30f, true);

        // the two standing lamps light the room around them (Point Light Volumes: Cor, VRC Light Volumes); the shelf's
        // small lamps get a faint glow
        Color warm = new Color(1f, 0.82f, 0.58f);
        LampBuilder.MakeLight(root, "LampLightCouch", L(1.7f, 16.9f, 2.05f), warm, 22f, 0.3f);
        LampBuilder.MakeLight(root, "LampLightCorner", L(22.3f, 18.4f, 2.05f), warm, 22f, 0.3f);
        LampBuilder.MakeLight(root, "LampLightShelf", L(roomW - 0.6f, roomD / 2 - 0.0f, 2.6f), warm, 6f, 0.2f);

        // the sun lights the room through the open box; the walls and the lid cast no shadows into it
        foreach (Transform c in lobby)
            if (c.name.StartsWith("Wall")) c.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        BuildLid(root, roomW, roomD, wallH);
    }

    public static Transform Spinner;   // the lid's spinner arrow (PHPanel spins it when a round starts)

    // the ceiling: the underside of the game box's lid, printed with a giant four-colour spinner
    static void BuildLid(Transform root, float roomW, float roomD, float wallH)
    {
        int first = root.childCount;
        var lidMat = Glow("LobbyLid", C("efe2bf"));
        var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lid.name = "Lid";
        lid.transform.SetParent(root, false);
        lid.transform.localPosition = new Vector3(0f, wallH + 0.15f, 0f);
        lid.transform.localScale = new Vector3(roomW + 0.6f, 0.3f, roomD + 0.6f);
        lid.GetComponent<MeshRenderer>().sharedMaterial = lidMat;
        lid.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lid.layer = 11;
        // the lid's rim, a felt band with gold dashes, just under the lid
        var felt = Glow("LobbyLidRim", C("8c2a2a"));
        var gold = Glow("LobbySpinnerHub", C("e2b24a"));
        float y = wallH - 0.12f;   // a slim rim above the walls' dashed band
        Slab(root, "RimN", new Vector3(0, y, roomD / 2 - 0.175f), new Vector3(roomW, 0.24f, 0.35f), felt);
        Slab(root, "RimS", new Vector3(0, y, -roomD / 2 + 0.175f), new Vector3(roomW, 0.24f, 0.35f), felt);
        Slab(root, "RimE", new Vector3(roomW / 2 - 0.175f, y, 0), new Vector3(0.35f, 0.24f, roomD), felt);
        Slab(root, "RimW", new Vector3(-roomW / 2 + 0.175f, y, 0), new Vector3(0.35f, 0.24f, roomD), felt);

        // the spinner: an outer felt ring, a cream ring, four coloured quarters, a gold hub and a red arrow
        float sy = wallH - 0.01f;
        Disc(root, "SpinnerEdge", new Vector3(0, sy, 0), 13.2f, Glow("LobbySpinnerEdge", C("21573f")));
        Disc(root, "SpinnerRing", new Vector3(0, sy - 0.008f, 0), 12.4f, lidMat);
        for (int q = 0; q < 4; q++)
        {
            var w = new GameObject("Quarter" + q);
            w.transform.SetParent(root, false);
            w.transform.localPosition = new Vector3(0, sy - 0.016f, 0);
            w.AddComponent<MeshFilter>().sharedMesh = Wedge(q * 90f + 2f, q * 90f + 88f, 5.9f);
            var mr = w.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Glow("LobbySpinner" + new[] { "Blue", "Yellow", "Green", "Coral" }[q], RowColour[q]);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var pivot = new GameObject("SpinnerArrow").transform;
        pivot.SetParent(root, false);
        pivot.localPosition = new Vector3(0, sy - 0.25f, 0);
        pivot.localRotation = Quaternion.Euler(0f, 35f, 0f);
        var red = Glow("LobbySpinnerArrow", C("e05a48"));
        Slab(pivot, "Shaft", new Vector3(0, 0, 1.7f), new Vector3(0.45f, 0.14f, 4.6f), red);
        var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tip.name = "Tip"; Object.DestroyImmediate(tip.GetComponent<Collider>());
        tip.transform.SetParent(pivot, false);
        tip.transform.localPosition = new Vector3(0, 0, 4.1f);
        tip.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        tip.transform.localScale = new Vector3(1.3f, 0.14f, 1.3f);
        tip.GetComponent<MeshRenderer>().sharedMaterial = red;
        Slab(pivot, "Tail", new Vector3(0, 0, -1.1f), new Vector3(0.9f, 0.14f, 0.9f), red);
        var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        hub.name = "Hub"; Object.DestroyImmediate(hub.GetComponent<Collider>());
        hub.transform.SetParent(root, false);
        hub.transform.localPosition = new Vector3(0, sy - 0.3f, 0);
        hub.transform.localScale = new Vector3(1.2f, 0.3f, 1.2f);
        hub.GetComponent<MeshRenderer>().sharedMaterial = gold;
        Spinner = pivot;
        // the sun shines in through the open box in the game's story: nothing up here casts shadows into the room
        for (int i = first; i < root.childCount; i++)
            foreach (var r in root.GetChild(i).GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // the lid faces down, away from the sun: a little emission keeps its print bright
    static Material Glow(string name, Color c)
    {
        var m = Mat(name, c);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 0.55f);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(m);
        return m;
    }

    // a flat pie slice (both faces), in the XZ plane
    static Mesh Wedge(float fromDeg, float toDeg, float r)
    {
        int seg = 24;
        var v = new Vector3[(seg + 2) * 2];
        var tris = new int[seg * 6];
        for (int side = 0; side < 2; side++)
        {
            int o = side * (seg + 2);
            v[o] = Vector3.zero;
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, i / (float)seg);
                v[o + 1 + i] = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
            }
            for (int i = 0; i < seg; i++)
            {
                int t = side * seg * 3 + i * 3;
                tris[t] = o; tris[t + 1] = side == 0 ? o + 1 + i : o + 2 + i; tris[t + 2] = side == 0 ? o + 2 + i : o + 1 + i;
            }
        }
        var m = new Mesh { name = "Wedge" };
        m.vertices = v; m.triangles = tris; m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static void Stack(Transform root, Vector3 at, int n, float dia)
    {
        float y = 0f;
        for (int i = 0; i < n; i++)
        {
            var go = Piece(root, "board-game-bits/coin_10_gold", at + new Vector3(i * 0.05f, y, -i * 0.04f), dia, i * 37f, true);
            y += Bounds(go).size.y;
        }
    }

    // a KayKit model, scaled so its largest dimension is `size` metres, standing on `at`, turned by `yaw`
    static GameObject Piece(Transform root, string model, Vector3 at, float size, float yaw, bool collider)
    {
        string path = Root + "Models/" + model + ".fbx";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) { Debug.LogWarning("[lobby] missing model " + path); return new GameObject("Missing"); }
        var go = (GameObject)Object.Instantiate(asset);
        go.name = System.IO.Path.GetFileName(model);
        go.transform.SetParent(root, false);
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        var mat = PackMat(model);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var ms = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < ms.Length; i++) ms[i] = mat;
            r.sharedMaterials = ms;
        }
        var b = Bounds(go);
        float k = size / Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        go.transform.localScale *= k;
        go.transform.localPosition = Vector3.zero;
        b = Bounds(go);
        go.transform.position += root.TransformPoint(at) - new Vector3(b.center.x, b.min.y, b.center.z);
        if (collider)
        {
            b = Bounds(go);
            var col = go.AddComponent<BoxCollider>();
            col.center = go.transform.InverseTransformPoint(b.center);
            var ls = go.transform.lossyScale;
            col.size = new Vector3(b.size.x / ls.x, b.size.y / ls.y, b.size.z / ls.z);
            go.layer = 11;
        }
        return go;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Material PackMat(string model)
    {
        string pack = model.Substring(0, model.IndexOf('/'));
        string tex = model.Contains("D20_blue") ? "dice_blue" :
                     pack == "board-game-bits" ? "boardgame_bits_texture" : pack == "furniture-bits" ? "furniturebits_texture" : "dungeon_texture";
        string path = Root + "Models/" + pack + "/" + tex + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(LitShader()); AssetDatabase.CreateAsset(m, path); }
        m.shader = LitShader();
        m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Models/" + pack + "/" + tex + ".png");
        Matte(m, 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static TextMeshPro WorldText(Transform root, string name, Vector3 pos, Quaternion rot, string s, float fontSize, Color c, Vector2 box)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos; go.transform.localRotation = rot;
        var t = go.AddComponent<TextMeshPro>();
        t.font = rocket; t.text = s; t.fontSize = fontSize; t.color = c; t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false; t.rectTransform.sizeDelta = box;
        return t;
    }

    static void Slab(Transform root, string name, Vector3 pos, Vector3 size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos; go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // flat trim
        Object.DestroyImmediate(go.GetComponent<BoxCollider>());
    }

    static void Box(Transform root, string name, Vector3 pos, Vector3 size, Material m)
    {
        Slab(root, name, pos, size, m);
        var go = root.GetChild(root.childCount - 1).gameObject;
        go.AddComponent<BoxCollider>();
        go.layer = 11;
    }

    static void Disc(Transform root, string name, Vector3 pos, float dia, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos; go.transform.localScale = new Vector3(dia, 0.006f, dia);
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    // the lobby's lit shader: Light Volumes' PBR (Standard's properties, and it takes Light Volumes' light), Standard
    // when the package is missing
    static Shader LitShader() { var s = Shader.Find("Light Volume Samples/Light Volume PBR"); return s != null ? s : Shader.Find("Standard"); }

    // matte (Cor: walls are not shiny unless they are metal): no specular highlights, no reflections
    public static void Matte(Material m, float smoothness)
    {
        m.SetFloat("_Glossiness", smoothness);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Speculars", 0f); m.DisableKeyword("_SPECULARS_ON");
        m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.SetFloat("_GlossyReflections", 0f); m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
    }

    public static Material Mat(string name, Color c)
    {
        string path = "Assets/Udon/Generated/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(LitShader()); AssetDatabase.CreateAsset(m, path); }
        m.shader = LitShader();
        m.color = c; Matte(m, 0f);
        EditorUtility.SetDirty(m);
        return m;
    }
}
