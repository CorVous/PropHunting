using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// The lobby's audio board (Cor): on the north wall over the how-to-play panels, Board Game style like the language
// board. A big speaker and a Reset tile on the left, then a cell per kind of sound (PHAudio.C_*, two rows of four):
// its name, a speaker tile (mute) and a track of ten spaces filled up to the level. Local to each player (PHAudio
// keeps the levels in PlayerData; PHAudioBoard shows them; PHAudioTile is each button).
public static partial class LobbyBuilder
{
    const float AW = 1400f, AH = 200f;   // canvas units at 3 mm: 4.2 x 0.6 m
    static Vector2 AP(float x, float y) { return new Vector2(x - AW / 2, AH / 2 - y); }

    // the board's names, in PHAudio's kind order (rows audio.* in strings_audio.tsv hold them in every language)
    public static readonly string[] AudioNames = { "Master", "Announcer", "Effects", "Buttons", "Jingles", "Taunts", "Music" };

    // centre: the board's centre on the wall (lobby space), facing the room (-z)
    public static PHAudioBoard BuildAudioBoard(Transform lobby, PHAudio sound, Vector3 centre)
    {
        EnsureAssets();
        var cv = Canvas(lobby, "AudioBoard", centre, Quaternion.identity, new Vector2(AW, AH), 0.003f, true);
        Img(cv, "BoardShadow", AP(AW / 2, AH / 2 + 5), new Vector2(AW - 8, AH - 8), sRound, C("173d2c"), 1.6f);
        Img(cv, "BoardEdge", AP(AW / 2, AH / 2), new Vector2(AW - 8, AH - 8), sRound, BoardEdge, 1.6f);
        Img(cv, "Board", AP(AW / 2, AH / 2), new Vector2(AW - 18, AH - 18), sRound, Cream, 1.6f);
        Img(cv, "Speaker", AP(78, 72), new Vector2(96, 96), Icon("speaker_on"), Color.white);

        var board = cv.gameObject.AddUdonSharpComponent<PHAudioBoard>();
        board.sound = sound;
        board.speakerOn = Icon("speaker_on"); board.speakerOff = Icon("speaker_off");
        int n = PHAudio.KINDS, steps = PHAudio.STEPS;
        board.spaces = new Image[n * steps];
        board.speakers = new Image[n];
        board.fill = new Color[n];
        board.empty = C("e3d4b0");
        board.mutedFill = C("b7ad98");

        // Reset, under the big speaker
        var reset = Img(cv, "Tile Reset", AP(78, 154), new Vector2(104, 40), sTile, C("e1cfa5"), 2f);
        var rt = Txt(reset.transform, "Text", new Vector2(0, 1), new Vector2(96, 30), "Reset", future, 14, C("1e190f"), TextAlignmentOptions.Center);
        rt.enableAutoSizing = true; rt.fontSizeMin = 8; rt.fontSizeMax = 14;
        AudioButton(reset, sound, -1, 0);

        const float X0 = 140f, CW = 312f, Y0 = 18f, RH = 86f, SW = 23f, SG = 3f;
        for (int k = 0; k < n; k++)
        {
            int row = k / 4, col = k % 4;
            float x = X0 + col * CW, y = Y0 + row * RH;
            Color c = k == PHAudio.C_MASTER ? C("f2c45a") : RowColour[(col + row * 2) % 4];
            board.fill[k] = c;
            var name = Txt(cv, "Name " + k, AP(x + 4 + 150, y + 15), new Vector2(300, 26), AudioNames[k], future, 15, C("1e190f"), TextAlignmentOptions.Left);
            name.enableAutoSizing = true; name.fontSizeMin = 8; name.fontSizeMax = 15;
            // the speaker: mute
            var sp = Img(cv, "Mute " + k, AP(x + 4 + 20, y + 52), new Vector2(40, 40), sTile, Cream, 2f);
            var icon = Img(sp.transform, "Icon", Vector2.zero, new Vector2(34, 34), board.speakerOn, Color.white);
            board.speakers[k] = icon;
            AudioButton(sp, sound, k, 0);
            // the track
            for (int s = 0; s < steps; s++)
            {
                float h = 22f + 18f * s / (steps - 1);   // rising, like a volume wedge
                var sq = Img(cv, "Space " + k + "." + (s + 1), AP(x + 50 + s * (SW + SG) + SW / 2, y + 72 - h / 2), new Vector2(SW, h), sTile, c, 2f);
                board.spaces[k * steps + s] = sq;
                // (the button covers the space's full height, so a short space is as easy to hit as a tall one)
                var hit = Img(sq.transform, "Hit", new Vector2(0, (40f - h) / 2), new Vector2(SW + SG, 40f), null, new Color(1f, 1f, 1f, 0f));
                AudioButton(hit, sound, k, s + 1, sq);
            }
        }
        UdonSharpEditorUtility.CopyProxyToUdon(board);
        if (sound != null) { sound.board = board; UdonSharpEditorUtility.CopyProxyToUdon(sound); }
        return board;
    }

    static void AudioButton(Image img, PHAudio sound, int kind, int value, Image target = null)
    {
        img.raycastTarget = true;
        var b = img.gameObject.AddComponent<Button>();
        var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
        b.targetGraphic = target != null ? target : img;
        var cb = b.colors; cb.highlightedColor = new Color(1.06f, 1.06f, 1.06f); cb.pressedColor = new Color(0.85f, 0.85f, 0.85f); b.colors = cb;
        var t = img.gameObject.AddUdonSharpComponent<PHAudioTile>();
        t.sound = sound; t.kind = kind; t.value = value;
        UdonSharpEditorUtility.CopyProxyToUdon(t);
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(t);
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(b.onClick, udon.SendCustomEvent, nameof(PHAudioTile.Press));
        Pressy(target != null ? target.gameObject : img.gameObject, b);
    }

    // a clip's kind for the audio board, from its name or folder: the announcer's lines (the old voiced calls by
    // name; new ones under Audio/Voice/ or named vo_*), jingles, taunt sounds, button clicks, else effects
    static readonly string[] AnnouncerClips = { "ready", "go", "time_over", "you_win", "you_lose", "game_over", "hurry_up", "congratulations" };
    static readonly string[] ButtonPrefixes = { "click_", "tick_", "error_", "confirmation_", "select_", "switch_", "pluck_", "toggle_", "back_", "open_", "close_", "question_", "drop_", "bong_" };
    static readonly string[] TauntClips = { "pepSound", "threeTone", "lowRandom", "phaseJump", "zapThreeTone", "highUp", "lowDown", "powerUp" };
    public static int AudioKind(string name, string path)
    {
        string p = path.Replace('\\', '/');
        if (System.Array.IndexOf(AnnouncerClips, name) >= 0 || p.Contains("/Audio/Voice/") || name.StartsWith("vo_")) return PHAudio.C_ANNOUNCER;
        if (name.StartsWith("jingles_")) return PHAudio.C_JINGLES;
        foreach (var t in TauntClips) if (name.StartsWith(t)) return PHAudio.C_TAUNTS;
        foreach (var b in ButtonPrefixes) if (name.StartsWith(b)) return PHAudio.C_BUTTONS;
        return PHAudio.C_EFFECTS;
    }

    // the sources outside PHAudio that play the game's sounds, with their kind and own volume (PHAudio.Apply)
    public static void ListAudioSources(PHAudio sound)
    {
        var src = new System.Collections.Generic.List<AudioSource>();
        var cat = new System.Collections.Generic.List<int>();
        var vol = new System.Collections.Generic.List<float>();
        foreach (var p in Object.FindObjectsOfType<PHPanel>(true)) if (p.sfx != null) { src.Add(p.sfx); cat.Add(PHAudio.C_BUTTONS); vol.Add(p.sfx.volume); }
        foreach (var e in Object.FindObjectsOfType<PHExplore>(true)) if (e.sfx != null) { src.Add(e.sfx); cat.Add(PHAudio.C_BUTTONS); vol.Add(e.sfx.volume); }
        foreach (var s in Object.FindObjectsOfType<PHSlot>(true)) if (s.voice != null) { src.Add(s.voice); cat.Add(PHAudio.C_TAUNTS); vol.Add(s.voice.volume); }
        sound.extra = src.ToArray(); sound.extraCat = cat.ToArray(); sound.extraBase = vol.ToArray();
        UdonSharpEditorUtility.CopyProxyToUdon(sound);
        Debug.Log("[prophunt] audio board: " + src.Count + " extra sources");
    }
}

public static class AudioBoardMenu
{
    // the audio board alone, in the open scene (where WorldSceneBuilder.Build puts it), then the text bake
    [MenuItem("PropHunt/Rebuild audio board")]
    public static void Rebuild()
    {
        var lobby = GameObject.Find("World/Lobby");
        var sound = Object.FindObjectOfType<PHAudio>(true);
        if (lobby == null || sound == null) { Debug.LogWarning("[prophunt] no World/Lobby or PHAudio in the open scene"); return; }
        var old = lobby.transform.Find("AudioBoard");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        LobbyBuilder.BuildAudioBoard(lobby.transform, sound, WorldSceneBuilder.AudioBoardAt);
        LobbyBuilder.ListAudioSources(sound);
        TextBaker.Bake();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lobby.scene);
    }
}
