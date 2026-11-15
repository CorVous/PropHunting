using UnityEngine;

// The third-person screen (owner decision G27): a small screen in front of the player that shows the player from
// outside, so a hider can see how the prop sits in the scene. In the VRChat world it is a floating screen that only
// its owner sees and that can be grabbed and moved; here it is a picture-in-picture panel. No keys (G38): the
// wrist panel (GameUI.Wrist) shows or hides it (remembered; on by default for hiders and in explore, off for
// hunters), steps its size (small, medium, large) and the side it looks from (behind, front, right, left), and
// while the wrist panel has freed the mouse the panel is dragged with it: the stand-in for grabbing it in VR.
// Its place and size are kept in PlayerPrefs. It is a second camera rendering into a texture every second frame;
// the camera is pulled in front of walls and ceilings the same way the V third-person view is. The V view stays
// as it was.
public class PipScreen : MonoBehaviour
{
    public static PipScreen I;

    public Vector2 centre = new Vector2(0.84f, 0.76f);     // of the panel, as a share of the screen (y from the top)
    public int sizeIndex = 1, view;

    static readonly float[] Widths = { 250f, 360f, 520f };   // panel width at a screen height of 860
    static readonly float[] ViewYaw = { 0f, 180f, 90f, 270f };
    static readonly string[] ViewName = { "from behind", "from the front", "from the right", "from the left" };
    static readonly float[] Pitches = { 24f, 46f, 66f };
    const float ASPECT = 1.6f;

    bool onHunter, onOther = true;
    Camera pipCam;
    RenderTexture rt;
    Texture2D frameTex, barTex;
    GUIStyle caption;
    bool dragging;
    Vector2 dragOffset;
    float hintUntil;
    string hint = "";

    void Awake()
    {
        I = this;
        onOther = PlayerPrefs.GetInt("pip.on.other", 1) != 0;
        onHunter = PlayerPrefs.GetInt("pip.on.hunter", 0) != 0;
        centre = new Vector2(PlayerPrefs.GetFloat("pip.x", centre.x), PlayerPrefs.GetFloat("pip.y", centre.y));
        sizeIndex = Mathf.Clamp(PlayerPrefs.GetInt("pip.size", sizeIndex), 0, Widths.Length - 1);
        view = Mathf.Clamp(PlayerPrefs.GetInt("pip.view", 0), 0, ViewYaw.Length - 1);

        var go = new GameObject("PipCamera");
        pipCam = go.AddComponent<Camera>();
        pipCam.enabled = false;                             // rendered by hand, every second frame
        pipCam.clearFlags = CameraClearFlags.SolidColor;
        pipCam.backgroundColor = new Color(0.56f, 0.76f, 0.93f);
        pipCam.nearClipPlane = 0.05f; pipCam.farClipPlane = 120f; pipCam.fieldOfView = 55f;
        pipCam.allowMSAA = false; pipCam.allowHDR = false;
    }

    bool Hunter { get { return Game.I.player.role == Player.ROLE_HUNTER; } }

    public bool On
    {
        get { return Hunter ? onHunter : onOther; }
        set
        {
            if (Hunter) { onHunter = value; PlayerPrefs.SetInt("pip.on.hunter", value ? 1 : 0); }
            else { onOther = value; PlayerPrefs.SetInt("pip.on.other", value ? 1 : 0); }
        }
    }

    // shown only while a map is played, never over the hunter's blindfold or the debug view from above
    bool Visible
    {
        get
        {
            var g = Game.I;
            if (g.state != Game.State.Playing || !g.player.active || g.topView || !On) return false;
            var rd = g.round;
            if (rd.phase == Round.PH_HIDE && Hunter) return false;
            return rd.phase != Round.PH_RESULT;
        }
    }

    void Save()
    {
        PlayerPrefs.SetFloat("pip.x", centre.x); PlayerPrefs.SetFloat("pip.y", centre.y);
        PlayerPrefs.SetInt("pip.size", sizeIndex); PlayerPrefs.SetInt("pip.view", view);
    }

    void Say(string text) { hint = text; hintUntil = Time.unscaledTime + 2.5f; }

    // the panel in screen pixels (y from the top), kept inside the screen
    Rect PanelRect()
    {
        float s = Mathf.Max(0.7f, Screen.height / 860f);
        float w = Mathf.Min(Widths[sizeIndex] * s, Screen.width * 0.6f), h = w / ASPECT;
        float x = Mathf.Clamp(centre.x * Screen.width - w / 2, 4f, Mathf.Max(4f, Screen.width - w - 4f));
        float y = Mathf.Clamp(centre.y * Screen.height - h / 2, 4f, Mathf.Max(4f, Screen.height - h - 4f));
        return new Rect(x, y, w, h);
    }

    void MoveBy(float dx, float dy)
    {
        Rect r = PanelRect();
        // from where the panel really is, so it does not stick at a screen edge
        centre = new Vector2((r.x + r.width / 2 + dx) / Screen.width, (r.y + r.height / 2 + dy) / Screen.height);
        r = PanelRect();
        centre = new Vector2((r.x + r.width / 2) / Screen.width, (r.y + r.height / 2) / Screen.height);
    }


    // the wrist panel's buttons
    public void Toggle() { On = !On; Say(On ? "Third-person screen on: drag it to move it" : "Third-person screen off"); }
    public void NextSize() { sizeIndex = (sizeIndex + 1) % Widths.Length; Save(); }
    public void NextView() { view = (view + 1) % ViewYaw.Length; Save(); Say("Third-person screen: " + ViewName[view]); }

    // is a point (GUI pixels, y from the top) on the screen? A click there drags it instead of capturing the mouse
    public bool Contains(Vector2 p) { return Visible && PanelRect().Contains(p); }

    void LateUpdate()
    {
        if (!Visible) return;
        if ((Time.frameCount & 1) != 0 && rt != null && rt.IsCreated()) return;
        Rect r = PanelRect();
        int w = Mathf.Max(64, Mathf.RoundToInt(r.width)), h = Mathf.Max(40, Mathf.RoundToInt(r.height));
        if (rt == null || rt.width != w || rt.height != h)
        {
            if (rt != null) { pipCam.targetTexture = null; rt.Release(); Destroy(rt); }
            rt = new RenderTexture(w, h, 16);
            rt.antiAliasing = 1;
            pipCam.targetTexture = rt;
        }
        var p = Game.I.player;
        // the camera looks down at the player from the chosen side, far enough to show the whole prop
        Vector3 size = p.def < 0 ? Balance.WalkBody : Player.DefSize(p.def);
        float big = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        Vector3 pivot = p.body.pos + Vector3.up * Mathf.Max(size.y * 0.6f, 0.3f);
        float want = Mathf.Clamp(1.5f + 1.3f * big, 1.9f, 5.5f), dist = 0f;
        Quaternion rot = Quaternion.identity;
        Vector3 back = Vector3.back;
        // A wall behind the player pulls the camera in. If that leaves less than three quarters of the distance,
        // the camera looks from higher up instead, where the room has more depth; the best of three heights is used.
        for (int k = 0; k < Pitches.Length; k++)
        {
            Quaternion q = Quaternion.Euler(Pitches[k], p.camYaw + ViewYaw[view], 0f);
            Vector3 b = q * Vector3.back;
            float d = want;
            RaycastHit hit;
            if (Physics.SphereCast(pivot, 0.16f, b, out hit, want, Layers.CameraMask, QueryTriggerInteraction.Ignore))
                d = Mathf.Max(0.35f, hit.distance);
            if (d > dist + 0.05f) { dist = d; rot = q; back = b; }
            if (d >= 0.75f * want) break;
        }
        pipCam.transform.position = pivot + back * dist;
        pipCam.transform.rotation = rot;
        p.PipRender(true);
        pipCam.Render();
        p.PipRender(false);
    }

    void OnGUI()
    {
        if (!Visible || rt == null) return;
        GUI.depth = -10;   // over the other panels, so it can be picked up while the menu is open
        if (frameTex == null)
        {
            frameTex = new Texture2D(1, 1); frameTex.SetPixel(0, 0, new Color(0.06f, 0.07f, 0.09f, 0.95f)); frameTex.Apply();
            barTex = new Texture2D(1, 1); barTex.SetPixel(0, 0, new Color(0.95f, 0.78f, 0.25f, 1f)); barTex.Apply();
            caption = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            caption.normal.textColor = Color.white;
        }
        Rect r = PanelRect();
        var e = Event.current;
        bool free = Cursor.lockState != CursorLockMode.Locked;
        if (free && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
        {
            dragging = true; dragOffset = e.mousePosition - new Vector2(r.x, r.y); e.Use();
        }
        else if (dragging && e.type == EventType.MouseDrag)
        {
            Vector2 to = e.mousePosition - dragOffset;
            MoveBy(to.x - r.x, to.y - r.y);
            r = PanelRect(); e.Use();
        }
        else if (dragging && (e.rawType == EventType.MouseUp || !free)) { dragging = false; Save(); }
        bool held = dragging;
        float b = held ? 3f : 2f;
        GUI.DrawTexture(new Rect(r.x - b, r.y - b, r.width + 2 * b, r.height + 2 * b), held ? barTex : frameTex);
        GUI.DrawTexture(r, rt, ScaleMode.StretchToFill, false);
        if (Time.unscaledTime < hintUntil || held)
        {
            Rect c = new Rect(r.x, r.yMax - 20f, r.width, 20f);
            GUI.DrawTexture(c, frameTex);
            GUI.Label(c, held ? "moving the screen" : hint, caption);
        }
    }

    // for the build checks (Game.Cmd): pip on|off, pip pos <x> <y>, pip size <0..2>, pip view <0..3>
    public string Command(string[] a)
    {
        if (a.Length > 1)
        {
            if (a[1] == "on") On = true;
            else if (a[1] == "off") On = false;
            else if (a[1] == "pos" && a.Length > 3) { centre = new Vector2(float.Parse(a[2]), float.Parse(a[3])); MoveBy(0f, 0f); Save(); }
            else if (a[1] == "size" && a.Length > 2) { sizeIndex = Mathf.Clamp(int.Parse(a[2]), 0, Widths.Length - 1); Save(); }
            else if (a[1] == "view" && a.Length > 2) { view = Mathf.Clamp(int.Parse(a[2]), 0, ViewYaw.Length - 1); Save(); }
        }
        Rect r = PanelRect();
        return "pip " + (On ? "on" : "off") + " visible " + Visible + " rect " + r.x.ToString("0") + "," + r.y.ToString("0") + " " + r.width.ToString("0") + "x" + r.height.ToString("0")
            + " size " + sizeIndex + " view " + view + " cam " + pipCam.transform.position.x.ToString("0.00") + "," + pipCam.transform.position.y.ToString("0.00") + "," + pipCam.transform.position.z.ToString("0.00");
    }
}
