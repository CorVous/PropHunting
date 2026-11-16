using System;
using System.Collections.Generic;
using UnityEngine;

// The host panel drawn on the lobby's north wall, right behind the play zone (owner decisions G50, G51: "the start
// zone right next to a wall, with a start button on the ui for the host, and all settings visible (but not
// interactable) for everyone else"). Every setting and START is a button in the world, always visible to everyone
// and used in place by the host: point at it and click (in VR: point and pull the trigger, or touch it, G38).
// Nothing opens an overlay. Everyone else sees the same settings and gets no hover and no click. The text reads
// from anywhere in the zone (letters about 14 cm high, up to 12 m away). In this single-player prototype the
// player is the host unless the prototype aid "You are the host" is switched off. The prototype's own aids (role, players in the zone, typing a seed, volume,
// mouse speed) are not here: they are in the lobby's screen overlay (GameUI.LobbyAids), shown while the mouse is free.
public class HostWall
{
    class Button
    {
        public float x, y, w, h;               // centre and size on the wall, metres (x along the wall, y up)
        public Func<bool> on;                  // drawn as selected
        public Action act;
        public Mesh mesh;
        public Color32 shown, off;
        public string name;
    }

    class Text
    {
        public TextMesh tm;
        public Func<string> get;
    }

    readonly List<Button> buttons = new List<Button>();
    readonly List<Text> texts = new List<Text>();
    Vector3 origin;          // world position of the wall's centre line at the floor
    float faceZ;             // world z of the buttons' faces
    Transform root;
    Button hovered;

    public const float HalfW = 6.5f, Y0 = 2.0f, Y1 = 5.35f, Reach = 20f;
    static readonly Color32 Off = new Color32(58, 72, 92, 255), On = new Color32(70, 150, 88, 255), StartCol = new Color32(46, 140, 70, 255);
    static readonly Color Info = new Color(0.85f, 0.9f, 1f);
    const float LabelSize = 0.06f, SmallSize = 0.055f;

    // wallCentre: world point on the wall's face (z) at x = 0, y = 0; the wall looks towards -z. Two columns of
    // settings over a full-width START row, all above head height (the zone lies right in front of the wall, so the
    // players standing in it must not hide the lower rows), letters about 14 cm high, readable across the zone.
    public void Build(Transform parent, Vector3 wallCentre)
    {
        root = parent; origin = wallCentre; faceZ = wallCentre.z - 0.04f;
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

    void Add(string name, float x, float y, float w, Func<bool> on, Action act, string label, float size = LabelSize, float h = 0.36f, Color32 off = default(Color32))
    {
        if (off.a == 0) off = Off;
        var b = new Button { name = name, x = x, y = y, w = w, h = h, on = on, act = act, off = off };
        var mb = new MeshBuf();
        Vector3 c = origin + new Vector3(x, y, 0f);
        mb.Box(new Vector3(c.x - w / 2, c.y - b.h / 2, faceZ), new Vector3(c.x + w / 2, c.y + b.h / 2, origin.z), off, off, off, MeshBuf.FACE_ALL & ~MeshBuf.FACE_ZP);
        var go = mb.ToObject("HostButton " + name, root, ModelLibrary.lit);
        b.mesh = go.GetComponent<MeshFilter>().sharedMesh;
        b.shown = off;
        buttons.Add(b);
        Label(label, x, y, size, Color.white);
    }

    void Dynamic(float x, float y, float size, Func<string> get, Color col)
    {
        texts.Add(new Text { tm = Label(get(), x, y, size, col), get = get });
    }

    TextMesh Label(string text, float x, float y, float size, Color col)
    {
        var go = new GameObject("HostLabel");
        go.transform.SetParent(root, false);
        go.transform.position = origin + new Vector3(x, y, -0.06f);
        go.transform.rotation = Quaternion.identity;   // a text mesh is read from -z: from the room
        var tm = go.AddComponent<TextMesh>();
        var font = Resources.Load<Font>("PropHunt/Fonts/KenneyFuture");
        if (font != null) { tm.font = font; go.GetComponent<MeshRenderer>().sharedMaterial = TextMaterial(font); }
        tm.text = text; tm.characterSize = size; tm.fontSize = 48; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = col;
        return tm;
    }

    // a font's material that is depth-tested (PropText.shader): Unity's own draws text on top of everything
    static Material textMat;
    public static Material TextMaterial(Font font)
    {
        if (textMat == null)
        {
            textMat = new Material(Shader.Find("PropHunt/Text3D"));
            textMat.mainTexture = font.material.mainTexture;
        }
        return textMat;
    }

    // the button the view points at, within reach, or null
    Button Aimed()
    {
        Transform ct = Game.I.cam.transform;
        Vector3 o = ct.position, f = ct.forward;
        if (f.z < 0.05f) return null;
        float u = (faceZ - o.z) / f.z;
        if (u <= 0f || u > Reach) return null;
        Vector3 h = o + f * u - origin;
        foreach (var b in buttons)
            if (Mathf.Abs(h.x - b.x) <= b.w / 2 && Mathf.Abs(h.y - b.y) <= b.h / 2) return b;
        return null;
    }

    public bool Pointing { get { return Aimed() != null; } }

    // every frame in the lobby: the hovered button lit, states and texts brought up to date; a click presses. Only
    // the host's pointing does anything (G51): everyone else reads the settings but gets no hover and no click.
    public void Tick(bool click)
    {
        hovered = Cursor.lockState == CursorLockMode.Locked && Game.I.isHost ? Aimed() : null;
        if (click && hovered != null)
        {
            hovered.act();
            AudioHub.I.Play("select_001", 0.5f);
        }
        foreach (var b in buttons)
        {
            Color32 c = b.on() ? On : b.off;
            if (b == hovered) c = new Color32((byte)Mathf.Min(255, c.r + 40), (byte)Mathf.Min(255, c.g + 40), (byte)Mathf.Min(255, c.b + 40), 255);
            if (!c.Equals(b.shown))
            {
                var cols = new Color32[b.mesh.vertexCount];
                for (int i = 0; i < cols.Length; i++) cols[i] = c;
                b.mesh.colors32 = cols;
                b.shown = c;
            }
        }
        foreach (var t in texts) { string s = t.get(); if (t.tm.text != s) t.tm.text = s; }
    }

    // test hook: the yaw and pitch from an eye position to a named button (the aim is then clicked for real)
    public bool AimAt(string name, Vector3 eye, out float yaw, out float pitch)
    {
        yaw = pitch = 0f;
        foreach (var b in buttons)
        {
            if (b.name != name) continue;
            Vector3 d = origin + new Vector3(b.x, b.y, faceZ - origin.z) - eye;
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            return true;
        }
        return false;
    }

    public string Names() { var s = new List<string>(); foreach (var b in buttons) s.Add(b.name); return string.Join(", ", s.ToArray()); }
}
