using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The host panel on the lobby's north wall, right behind the play zone (owner decisions G50, G51, G55). Every
// setting and START is a button, always visible to everyone and used in place by the host: point at it and click
// (in VR: point and pull the trigger, or touch it). Nothing opens an overlay. Everyone else sees the same panel and
// gets no hover and no click. The text reads from anywhere in the zone (capitals about 19 cm high, up to 12 m away).
//
// G55: the panel is a world-space Unity UI canvas (uGUI, TextMeshPro text), set up the way VRChat's VRC_UiShape page
// asks, so that in the VRChat world it only needs a VRC_UiShape component and its buttons wired to Udon:
//   render mode World Space; scale 0.01 (1 canvas unit = 1 cm); layer Default, not UI; a BoxCollider the size of the
//   canvas on the canvas object; a GraphicRaycaster; Navigation None on every button; text is never a raycast target;
//   the canvas's z-forward points into the wall, away from the people reading it. Nothing scrolls.
// No VRChat type is referenced here: the WebGL player leaves those assemblies out. In the browser an EventSystem with
// CentrePointerModule points with the centre of the screen while the mouse is captured, and only for the host.
// In this single-player prototype the player is the host unless the prototype aid "You are the host" is switched
// off. The prototype's own aids (role, players in the zone, typing a seed, volume, mouse speed) are not here: they
// are in the lobby's screen overlay (GameUI.LobbyAids), shown while the mouse is free.
public class HostWall
{
    class Entry
    {
        public string name;
        public Button button;
        public Image image;
        public Func<bool> on;                  // drawn as selected
        public Color32 off, shown;
    }

    class Live
    {
        public TextMeshProUGUI tm;
        public Func<string> get;
    }

    readonly List<Entry> entries = new List<Entry>();
    readonly List<Live> texts = new List<Live>();
    Canvas canvas;
    float midY;              // height of the canvas's centre above the floor, metres

    public const float HalfW = 6.5f, Y0 = 2.0f, Y1 = 5.35f;
    // the buttons' colours are tinted by 0.82 when not pointed at (ColorBlock.normalColor), so these are a little
    // brighter than the colours they show
    static readonly Color32 Off = new Color32(71, 88, 112, 255), On = new Color32(85, 183, 107, 255), StartCol = new Color32(56, 171, 85, 255);
    static readonly Color Info = new Color(0.85f, 0.9f, 1f);
    // sizes as before G55, in the old text meshes' character size; Em turns one into a TextMeshPro font size in cm
    const float LabelSize = 0.06f, SmallSize = 0.055f, Em = 470f;

    // wallCentre: world point on the board's face at x = 0, y = 0; the room is on its -z side. Two columns of
    // settings under a full-width START row, all above head height (the zone lies right in front of the wall, so the
    // players standing in it must not hide the lower rows).
    public void Build(Transform parent, Vector3 wallCentre)
    {
        midY = (Y0 + Y1) / 2f;
        var go = new GameObject("HostPanelCanvas", typeof(RectTransform));
        go.layer = 0;                                                   // Default: VRChat ignores canvases on UI
        go.transform.SetParent(parent, false);
        go.transform.position = wallCentre + new Vector3(0f, midY, -0.02f);
        go.transform.rotation = Quaternion.identity;                    // z-forward into the wall, away from the reader
        go.transform.localScale = Vector3.one * 0.01f;
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Game.I.cam;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(HalfW * 200f, (Y1 - Y0) * 100f);
        go.AddComponent<GraphicRaycaster>();
        // the collider VRChat's pointer looks for: the canvas's own size, 2 cm deep, behind its face (a trigger, so
        // nothing walks into it and no physics query of the game sees it)
        var bc = go.AddComponent<BoxCollider>();
        bc.size = new Vector3(rt.sizeDelta.x, rt.sizeDelta.y, 2f);
        bc.center = new Vector3(0f, 0f, 1f);
        bc.isTrigger = true;

        var g = Game.I;
        Dynamic(0f, 5.08f, 0.07f, () => g.isHost ? "HOST: YOU" : "HOST: PLAYER 1", new Color(1f, 0.85f, 0.4f));

        // START on top, under the host's name, where the players standing at the front do not hide it
        float r6 = 4.55f, r1 = 3.98f, r2 = 3.56f, r3 = 3.14f, r4 = 2.72f, r5 = 2.3f;
        Label("NEXT MAP", -5.4f, r1, LabelSize, Color.white);
        Add("new map", -3.3f, r1, 1.9f, () => false, () => { g.NewRandomSeed(); g.keepSeed = false; }, "NEW MAP");
        Add("keep seed", -1.15f, r1, 2.2f, () => g.keepSeed, () => g.keepSeed = !g.keepSeed, "KEEP SEED");
        Dynamic(3.4f, r1, SmallSize, () => g.seedText.Trim() + " " + (g.seedText.Trim() == g.lastPlayed && g.lastPlayed.Length > 0 ? "(AGAIN)" : (g.keepSeed ? "(KEPT)" : "(NEW)")), Info);

        Label("SIZE", -5.4f, r2, LabelSize, Color.white);
        Add("size auto", -3.65f, r2, 1.1f, () => g.sizeAuto, () => g.sizeAuto = true, "AUTO");
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            Add("size " + GenData.SizeName[k], -2.75f + k * 0.7f, r2, 0.6f + (k == 3 ? 0.1f : 0f), () => !g.sizeAuto && g.size == k, () => { g.sizeAuto = false; g.SetSize(k); }, GenData.SizeName[k]);
        }
        Dynamic(3.4f, r2, SmallSize, () => "NEXT: " + GenData.SizeName[Lobby.I.RoundSize] + (g.sizeAuto ? " (" + Lobby.I.Participants + " PLAYERS)" : ""), Info);

        Label("ROUND", -5.4f, r3, LabelSize, Color.white);
        float[] rx = { -3.55f, -2.0f, -0.6f }, rw = { 1.35f, 1.55f, 1.1f };
        for (int i = 0; i < Balance.RoundLengthName.Length; i++)
        {
            int k = i;
            Add("round " + Balance.RoundLengthName[k].ToLowerInvariant(), rx[k], r3, rw[k], () => g.roundLength == k, () => g.roundLength = k, Balance.RoundLengthName[k].ToUpperInvariant());
        }
        Label("WRONG PROP", 1.3f, r3, LabelSize, Color.white);
        for (int i = 0; i < Balance.WrongPropChoices.Length; i++)
        {
            int pn = Balance.WrongPropChoices[i];
            Add("penalty " + pn, 3.0f + i * 1.07f, r3, i == 0 ? 0.9f : 1.0f, () => g.wrongPenalty == pn, () => g.wrongPenalty = pn, "-" + pn);
        }

        Label("TAUNT", -5.4f, r4, LabelSize, Color.white);
        for (int i = 0; i < Balance.ForcedTauntChoices.Length; i++)
        {
            int t = Balance.ForcedTauntChoices[i];
            Add("taunt " + t, -3.6f + i * 1.25f, r4, 1.1f, () => g.tauntInterval == t, () => g.tauntInterval = t, t + " S");
        }
        Label("RUN CAP", 1.1f, r4, LabelSize, Color.white);
        Add("run cap -", 2.75f, r4, 0.55f, () => false, () => g.runCap = Mathf.Max(1, g.runCap - 1), "-");
        Dynamic(3.45f, r4, LabelSize, () => g.runCap.ToString(), Color.white);
        Add("run cap +", 4.15f, r4, 0.55f, () => false, () => g.runCap = Mathf.Min(50, g.runCap + 1), "+");
        Dynamic(5.45f, r4, SmallSize, () => "(OFF)", Info);

        Label("DENSITY", -5.4f, r5, LabelSize, Color.white);
        string[] shortName = { "NORMAL", "SPARSE", "DENSE", "V.SPARSE", "V.DENSE" };
        for (int k = 0; k < GenData.DensityOrder.Length; k++)
        {
            int d = GenData.DensityOrder[k];
            Add("density " + GenData.DensityName[d].ToLowerInvariant(), -3.6f + k * 1.36f, r5, 1.3f, () => g.density == d, () => g.SetDensity(d), shortName[d], 0.045f);
        }
        Dynamic(4.3f, r5, SmallSize, () => "(TEST AID)", Info);

        Add("start", 0f, r6, 5f, () => false, () => Lobby.I.TryGenerate(false), "START", 0.11f, 0.56f, StartCol);
    }

    // a position on the wall (metres along it, metres above the floor) in canvas units
    Vector2 At(float x, float y) { return new Vector2(x * 100f, (y - midY) * 100f); }

    static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos; r.sizeDelta = size;
        return r;
    }

    void Add(string name, float x, float y, float w, Func<bool> on, Action act, string label, float size = LabelSize, float h = 0.36f, Color32 off = default(Color32))
    {
        if (off.a == 0) off = Off;
        var r = Rect("Button " + name, canvas.transform, At(x, y), new Vector2(w * 100f, h * 100f));
        var img = r.gameObject.AddComponent<Image>();
        img.color = off;
        var b = r.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.navigation = new Navigation { mode = Navigation.Mode.None };    // VRChat: moving must never work the UI
        var cb = b.colors;
        cb.normalColor = cb.selectedColor = cb.disabledColor = new Color(0.82f, 0.82f, 0.82f);
        cb.highlightedColor = Color.white; cb.pressedColor = new Color(0.68f, 0.68f, 0.68f);
        cb.fadeDuration = 0.05f;
        b.colors = cb;
        b.onClick.AddListener(() => { act(); AudioHub.I.Play("select_001", 0.5f); });
        entries.Add(new Entry { name = name, button = b, image = img, on = on, off = off, shown = off });
        Text(r, label, size, Color.white, w);
    }

    TextMeshProUGUI Label(string text, float x, float y, float size, Color col)
    {
        var r = Rect("Text", canvas.transform, At(x, y), new Vector2(400f, 60f));
        return Text(r, text, size, col, 0f);
    }

    void Dynamic(float x, float y, float size, Func<string> get, Color col)
    {
        texts.Add(new Live { tm = Label(get(), x, y, size, col), get = get });
    }

    // a text on rect r (a button's label is a child of the button, filling it)
    static TextMeshProUGUI Text(RectTransform r, string text, float size, Color col, float w)
    {
        RectTransform tr = r;
        if (w > 0f) { tr = Rect("Label", r, Vector2.zero, r.sizeDelta); }
        var t = tr.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = Font;
        t.fontSize = size * Em;
        t.color = col;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;          // text never takes the pointer from a button under it
        t.text = text;
        return t;
    }

    // Kenney Future as a TextMeshPro font, made once at run time from the font file (no editor to bake an SDF asset
    // in this headless pipeline); TextMeshPro's own default font if that fails
    static TMP_FontAsset font;
    public static TMP_FontAsset Font
    {
        get
        {
            if (font == null)
            {
                var f = Resources.Load<Font>("PropHunt/Fonts/KenneyFuture");
                if (f != null) font = TMP_FontAsset.CreateFontAsset(f);
                if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            }
            return font;
        }
    }

    // A text in the lobby that is not part of the panel (the zone's floor, the sign by the way in): its own small
    // world-space canvas, the same text and scale as the panel's. pos: world position; a canvas is read from -z,
    // rot turns it to face the room or up from the floor; size in the old text meshes' character size.
    public static TextMeshProUGUI WorldLabel(Transform parent, string text, Vector3 pos, Quaternion rot, float size, Color col)
    {
        var go = new GameObject("LobbyLabel", typeof(RectTransform));
        go.layer = 0;
        go.transform.SetParent(parent, false);
        go.transform.position = pos; go.transform.rotation = rot; go.transform.localScale = Vector3.one * 0.01f;
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1000f, 120f);
        return Text(rt, text, size, col, 0f);
    }

    // every frame in the lobby: the selected settings lit, the texts brought up to date; a guest's buttons do not
    // take the pointer (CentrePointerModule only points for the host; disabled buttons look the same)
    public void Tick()
    {
        bool host = Game.I.isHost;
        foreach (var e in entries)
        {
            if (e.button.interactable != host) e.button.interactable = host;
            Color32 c = e.on() ? On : e.off;
            if (!c.Equals(e.shown)) { e.image.color = c; e.shown = c; }
        }
        foreach (var t in texts) { string s = t.get(); if (t.tm.text != s) t.tm.text = s; }
    }

    // is a game object one of this panel's buttons?
    public bool Owns(GameObject go)
    {
        if (go == null) return false;
        foreach (var e in entries) if (e.button.gameObject == go) return true;
        return false;
    }

    // test hook: the yaw and pitch from an eye position to a named button (the aim is then clicked for real)
    public bool AimAt(string name, Vector3 eye, out float yaw, out float pitch)
    {
        yaw = pitch = 0f;
        foreach (var e in entries)
        {
            if (e.name != name) continue;
            Vector3 d = e.button.transform.position - eye;
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            return true;
        }
        return false;
    }

    public string Names() { var s = new List<string>(); foreach (var e in entries) s.Add(e.name); return string.Join(", ", s.ToArray()); }
}
