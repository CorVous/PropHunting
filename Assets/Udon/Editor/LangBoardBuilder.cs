using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// The lobby's language board (Board Game style, minimal text): a little lectern beside the spawn disc. A globe, then
// a tile per language bearing the language's name in itself; the meeple stands on the player's pick; the "Auto" tile
// (a dashed tile with a small globe) follows VRChat's own language, and while it is picked a dashed ring marks the
// language VRChat chose. Local to each player (PHText, PHLangBoard, PHLangTile).
public static partial class LobbyBuilder
{
    const float LW = 1170f, LH = 330f;   // canvas units at 3 mm: 3.51 x 0.99 m
    static Vector2 LP(float x, float y) { return new Vector2(x - LW / 2, LH / 2 - y); }

    // the lectern stands at localPos (its foot), turned by yaw; the board leans back by 22 degrees
    public static PHLangBoard BuildLanguageBoard(Transform lobby, PHGame game, Vector3 localPos, float yaw)
    {
        EnsureAssets();
        var root = new GameObject("LanguageBoard").transform;
        root.SetParent(lobby, false);
        root.localPosition = localPos; root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        var wood = Mat("LangStandWood", C("b07c3e"));
        Box(root, "Foot", new Vector3(0f, 0.03f, 0f), new Vector3(0.9f, 0.06f, 0.6f), wood);
        Slab(root, "Post", new Vector3(0f, 0.45f, 0.24f), new Vector3(0.12f, 0.84f, 0.12f), wood);   // behind the leaning board
        Slab(root, "Back", new Vector3(0f, 1.05f, 0.13f), new Vector3(3.6f, 1.06f, 0.05f), wood);
        root.Find("Back").localRotation = Quaternion.Euler(22f, 0f, 0f);

        var cv = Canvas(root, "Board", new Vector3(0f, 1.05f, 0.13f) + Quaternion.Euler(22f, 0f, 0f) * new Vector3(0f, 0f, -0.03f),
            Quaternion.Euler(22f, 0f, 0f), new Vector2(LW, LH), 0.003f, true);
        Img(cv, "BoardShadow", LP(LW / 2, LH / 2 + 5), new Vector2(LW - 8, LH - 8), sRound, C("173d2c"), 1.6f);
        Img(cv, "BoardEdge", LP(LW / 2, LH / 2), new Vector2(LW - 8, LH - 8), sRound, BoardEdge, 1.6f);
        Img(cv, "Board", LP(LW / 2, LH / 2), new Vector2(LW - 18, LH - 18), sRound, Cream, 1.6f);
        Img(cv, "Globe", LP(98, LH / 2), new Vector2(170, 170), Icon("globe"), Color.white);

        var board = root.gameObject.AddUdonSharpComponent<PHLangBoard>();
        board.text = game != null ? game.text : null;
        board.sound = game != null && game.local != null ? game.local.sound : null;
        int n = TextBaker.Codes.Length;
        board.tiles = new RectTransform[n];
        var tiles = new PHLangTile[n + 1];
        const float TW = 86f, TH = 72f, GAP = 10f, X0 = 186f;
        // three rows of ten: Auto, then the languages in TextBaker's order
        for (int i = -1; i < n; i++)
        {
            int slot = i + 1;
            int row = slot / 10, col = slot % 10;
            float cx = X0 + col * (TW + GAP) + TW / 2;
            float cy = 30 + row * (TH + 26) + TH / 2;
            Image tile;
            if (i < 0)
            {
                tile = Img(cv, "Tile Auto", LP(cx, cy), new Vector2(TW, TH), sDashed, C("b8892b"), 2f);
                Img(tile.transform, "Globe", new Vector2(0, 9), new Vector2(40, 40), Icon("globe"), Color.white);
                Txt(tile.transform, "Caption", new Vector2(0, -22), new Vector2(TW, 18), "Auto", future, 13, C("1e190f", 0.85f), TextAlignmentOptions.Center);
                board.autoTile = tile.rectTransform;
            }
            else
            {
                tile = Img(cv, "Tile " + TextBaker.Codes[i], LP(cx, cy), new Vector2(TW, TH), sTile, RowColour[i % 4], 2f);
                var name = Txt(tile.transform, "Name", new Vector2(0, 2), new Vector2(TW - 8, 30), TextBaker.DisplayName(i), future, 15, C("1e190f"), TextAlignmentOptions.Center);
                name.enableAutoSizing = true; name.fontSizeMin = 9; name.fontSizeMax = 17;
                board.tiles[i] = tile.rectTransform;
            }
            tile.raycastTarget = true;
            var b = tile.gameObject.AddComponent<Button>();
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            b.targetGraphic = tile;
            var cb = b.colors; cb.highlightedColor = new Color(1.06f, 1.06f, 1.06f); cb.pressedColor = new Color(0.85f, 0.85f, 0.85f); b.colors = cb;
            var lt = tile.gameObject.AddUdonSharpComponent<PHLangTile>();
            lt.index = i; lt.board = board; lt.text = board.text;
            UdonSharpEditorUtility.CopyProxyToUdon(lt);
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(lt);
            UnityEditor.Events.UnityEventTools.AddStringPersistentListener(b.onClick, udon.SendCustomEvent, nameof(PHLangTile.Press));
            tiles[slot] = lt;
        }
        var ring = Img(board.tiles[0], "VRChatPick", Vector2.zero, new Vector2(TW + 16, TH + 16), sDashed, C("b8892b"), 2f);
        board.autoRing = ring.gameObject;
        var mk = Img(board.tiles[0], "Marker", new Vector2(TW / 2 - 12, TH / 2 - 6), new Vector2(46, 46), Icon("meeple_hunt"), Color.white);
        mk.raycastTarget = false; ring.raycastTarget = false;
        board.marker = mk.rectTransform;
        UdonSharpEditorUtility.CopyProxyToUdon(board);
        return board;
    }
}

public static class LangBoardMenu
{
    // the language board alone, in the open scene (where WorldSceneBuilder.Build puts it), then the text bake
    [MenuItem("PropHunt/Rebuild language board")]
    public static void Rebuild()
    {
        var lobby = GameObject.Find("World/Lobby");
        var game = Object.FindObjectOfType<PHGame>(true);
        if (lobby == null || game == null) { Debug.LogWarning("[prophunt] no World/Lobby or PHGame in the open scene"); return; }
        var old = lobby.transform.Find("LanguageBoard");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        LobbyBuilder.BuildLanguageBoard(lobby.transform, game, new Vector3(2.7f, 0f, -22f / 2 + 3.6f), 28f);
        TextBaker.Bake();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lobby.scene);
    }
}
