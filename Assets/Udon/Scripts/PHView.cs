using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.Rendering;
using VRC.Udon.Common;

// What the local player sees besides the world: as little as possible (most cues are sounds, PHAudio).
//   health: a hider's health bar only. Desktop: low on the screen. VR: just above the screen device (below), carried with
//           it; never in front of the eyes.
//   third-person view (G27, the prototype's PipScreen): a camera behind the hider's prop shows how the prop
//           sits in the scene (the hider's own prop is on the MirrorReflection layer, which only this camera
//           draws). Desktop: a picture in the lower right corner. VR: a floating device (a screen in a casing)
//           beside the player that can be grabbed and put where it suits; it keeps that place in the room.
//   a VR hider's devices (Cor), seen by nobody else and sized with the avatar:
//           right hand: the selector, with a beam to what the trigger would pick
//           left hand:  the turner (its trigger hands the right stick to the prop), and above it a small copy of
//                       the prop, standing as the prop stands in the world
//           on the screen device's casing: the taunt button, pushed with a hand; never by grabbing
//   desktop: the controls, in the lower left corner; after a while only the two that are easy to forget
//   the own prop's outline (Cor), desktop and VR: the hider does not see their own prop in first person, so its
//           footprint lies on the ground under it as a thin rectangle, with a nub on the front; seen only by the hider
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHView : UdonSharpBehaviour
{
    public PHGame game;
    public PHLocal local;
    public Transform desktopRoot;      // head-locked, desktop only
    public Image desktopHealth;        // its right edge (anchorMax.x) = health share
    public GameObject desktopHealthBar;
    public GameObject desktopPip;
    public Image vrHealth;             // VR: on the screen device's casing, above the screen
    public GameObject vrHealthBar;
    public Camera pipCam;
    public Transform vrPanel;          // the grabbable third-person panel (VRCPickup)
    public VRCPickup vrPanelPickup;
    public Transform gun;              // right hand
    public Transform beam;
    public MeshRenderer beamRenderer;
    public Material beamIdle, beamHit;
    public Transform dial;             // left hand
    public Transform pad;              // floats above it, level; the small prop stands on it
    public MeshRenderer padRenderer;
    public Material padIdle, padLit;
    public Transform casing;           // the screen device's body, drawn around the panel
    public Transform tauntButton, tauntCap;
    public MeshRenderer tauntCapRenderer;
    public Material capReady, capWait;
    // desktop: the controls, drawn as VRChat's own panels are (Cor): a picture for each state, bottom left, clear
    // of VRChat's rows (top left, top right)
    public RawImage legend;
    public Texture2D legendHider, legendHiderShort, legendHunter;
    public RectTransform desktopCanvas;
    const float LEGEND_H = 120f / 1342f;   // a VRChat panel's height against the screen's
    public Transform outline;          // the own prop's footprint on the ground
    public Transform[] outlineParts;   // short pieces of the edges, and the nub on the front (the last)

    // the taunt button, in the casing's own metres (z towards the back of the device): where it sits, and its press
    readonly Vector3 BUTTON_AT = new Vector3(-0.12f, -0.175f, -0.010f);
    const float CAP_REST = -0.022f, CAP_FRONT = -0.034f, CAP_IN = -0.012f, PRESS_AT = -0.02f;
    const float GRAB_GUARD = 0.5f, ARM_TIME = 0.7f;
    bool[] gripHeld = new bool[2];         // 0 left, 1 right
    float[] gripUpAt = new float[2];
    float[] armedUntil = new float[2];
    bool[] latched = new bool[2];
    float panelDropAt = -99f;
    bool hiderNow, devicesOn, padIsLit, capIsReady = true, beamIsHit;
    GameObject mini;
    int miniDef = -2;
    float miniFit = 1f;
    Vector3 miniBase = Vector3.one;
    int legendMode = -1;
    const float HUD_DIST = 0.5f;   // metres from the screen's camera to the desktop canvas
    bool hudLogged;
    float legendSince;
    int outlineDef = -2, outlineSerial = -1;
    // the footprint of the prop's own boxes: their edges cut the ground plan into cells, the covered ones marked,
    // and the outline runs wherever a covered cell lies beside one that is not
    const int CUTS = 26, RUNS = 48;
    float[] cutX, cutZ, runA, runB, runAt;
    bool[] runAlongX, cellIn;
    float[] fMinX, fMaxX, fMinZ, fMaxZ;
    int nRuns;
    const float OUTLINE_W = 0.02f, OUTLINE_H = 0.004f, OUTLINE_STEP = 0.12f;
    // the pieces in use, where each lies in the prop's own frame, and what they were last laid for
    int nPieces;
    float[] pieceX, pieceZ;
    float nubZ;
    Vector3 outlinePos;
    float outlineYaw = -999f, outlineNext;

    // The panel keeps its place in the real room (Cor): its pose is kept relative to the playspace's origin, in
    // units of the avatar's size, so walking with the stick or turning with it does not move it about the room.
    Vector3 panelLocal;
    Quaternion panelLocalRot = Quaternion.identity;
    bool panelPlaced;
    Vector3 panelBaseScale = new Vector3(0.36f, 0.24f, 1f);
    bool vr;
    int frame;

    void Start()
    {
        vr = Networking.LocalPlayer.IsUserInVR();
        if (desktopRoot != null) desktopRoot.gameObject.SetActive(!vr);
        if (vrPanel != null) { panelBaseScale = vrPanel.localScale; vrPanel.gameObject.SetActive(false); }
        // the third-person camera does not draw the panel that shows its picture (Cor: the screen saw itself)
        if (pipCam != null && vrPanel != null) pipCam.cullingMask &= ~(1 << vrPanel.gameObject.layer);
        ShowDevices(false);
        devicesOn = false;
        if (legend != null) legend.gameObject.SetActive(false);
        if (outline != null) outline.gameObject.SetActive(false);
    }

    void ShowDevices(bool on)
    {
        devicesOn = on;
        if (gun == null) return;
        gun.gameObject.SetActive(on);
        dial.gameObject.SetActive(on);
        pad.gameObject.SetActive(on);
        casing.gameObject.SetActive(on);
        tauntButton.gameObject.SetActive(on);
        if (!on) beam.gameObject.SetActive(false);
    }

    // a hand that grabs (the screen device, a door) must never press the taunt button
    public override void InputGrab(bool value, UdonInputEventArgs args)
    {
        int h = args.handType == HandType.LEFT ? 0 : 1;
        gripHeld[h] = value;
        if (!value) gripUpAt[h] = Time.time;
    }

    // the small copy of the hider's prop that stands on the pad over the left hand
    void UpdateMini(PHSlot s, bool hider)
    {
        int want = hider ? s.def : -1;
        if (want == miniDef) return;
        miniDef = want;
        if (mini != null) Destroy(mini);
        mini = null;
        GameObject[] prefabs = game.builder.propPrefabs;
        if (want < 0 || prefabs == null || want >= prefabs.Length || prefabs[want] == null) return;
        mini = Instantiate(prefabs[want]);
        game.builder.TintProp(mini, want);
        mini.name = "MiniProp";
        Collider[] cols = mini.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
        Transform[] parts = mini.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i].gameObject.layer = 5;   // not in the third-person view
            if (parts[i].name == "Shade") parts[i].gameObject.SetActive(false);
        }
        float big = Mathf.Max(game.gen.defSx[want], Mathf.Max(game.gen.defSy[want], game.gen.defSz[want])) / 1000f;
        miniFit = 0.09f / Mathf.Max(0.05f, big);
        miniBase = mini.transform.localScale;
    }

    // desktop: the controls. In full for the first while of a round, then only the two easy to forget
    void UpdateLegend(PHSlot s, bool hider)
    {
        if (legend == null) return;
        int want = 0;
        bool hunter = !hider && s != null && game.RoleOf(s.index) == PHGame.ROLE_HUNTER && game.phase == PHGame.PH_HUNT && !s.hunterOut;
        if (hider)
        {
            if (legendMode != 1 && legendMode != 2) legendSince = Time.time;
            want = Time.time - legendSince < 20f ? 1 : 2;
        }
        else if (hunter)
        {
            if (legendMode != 3 && legendMode != 4) legendSince = Time.time;
            want = Time.time - legendSince < 12f ? 3 : 4;
        }
        if (want == legendMode) return;
        legendMode = want;
        Texture2D tex = want == 1 ? legendHider : (want == 2 ? legendHiderShort : (want == 3 ? legendHunter : null));
        legend.gameObject.SetActive(tex != null);
        if (tex == null) return;
        legend.texture = tex;
        float h = 900f * LEGEND_H;   // the canvas is 900 high (PostLateBody)
        legend.rectTransform.sizeDelta = new Vector2(h * tex.width / tex.height, h);
    }

    // the avatar's size against a 1.6 m eye height: what is worn or held scales with it
    float AvatarScale()
    {
        float eye = Networking.LocalPlayer.GetAvatarEyeHeightAsMeters();
        return Mathf.Clamp(eye > 0.01f ? eye / 1.6f : 1f, 0.2f, 5f);
    }

    PHSlot Me() { return game.LocalSlot(); }

    // profiling (PHProfiler): while it runs, the time this script's updates take adds up in profMs
    [System.NonSerialized] public System.Diagnostics.Stopwatch profWatch;
    [System.NonSerialized] public double profMs;

    void Update()
    {
        if (profWatch == null) { UpdateBody(); return; }
        double p0 = profWatch.Elapsed.TotalMilliseconds;
        UpdateBody();
        profMs += profWatch.Elapsed.TotalMilliseconds - p0;
    }

    void UpdateBody()
    {
        PHSlot s = Me();
        bool hider = s != null && game.RoleOf(s.index) == PHGame.ROLE_HIDER && s.hiding && !s.caught && s.def >= 0
            && (game.phase == PHGame.PH_BRIEF || game.phase == PHGame.PH_HIDE || game.phase == PHGame.PH_HUNT);
        float share = hider && s.maxHealth > 0 ? Mathf.Clamp01((float)s.health / s.maxHealth) : 0f;
        // (Perf: switched only when they change, here and for the pip camera below)
        if (vr)
        {
            if (vrHealthBar.activeSelf != hider) vrHealthBar.SetActive(hider);
            if (hider) vrHealth.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(share), 1f);
            if (vrPanel.gameObject.activeSelf != hider) vrPanel.gameObject.SetActive(hider);
            if (devicesOn != hider) ShowDevices(hider);
            UpdateMini(s, hider);
        }
        else
        {
            if (desktopHealthBar.activeSelf != hider) desktopHealthBar.SetActive(hider);
            if (hider) desktopHealth.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(share), 1f);
            if (desktopPip.activeSelf != hider) desktopPip.SetActive(hider);
            UpdateLegend(s, hider);
        }
        UpdateOutline(s, hider);
        hiderNow = hider;
        // the third-person camera renders every second frame while there is a prop to show
        frame++;
        bool pip = hider;
        bool pipOn = pip && (frame & 1) == 0;
        if (pipCam.enabled != pipOn) pipCam.enabled = pipOn;
        if (pip) PlaceCamera(s);
        SetCut(s, pip);
    }

    // the own prop's outline: shown while hiding, laid out anew when the prop changes. It is the footprint of the
    // prop's own collider boxes as the hider's body is moved (Cor: the projection should match the actual collider),
    // so an L-shaped sofa shows as an L; the one box of its size where the body has no such shape.
    void UpdateOutline(PHSlot s, bool hider)
    {
        if (outline == null) return;
        if (outline.gameObject.activeSelf != hider) outline.gameObject.SetActive(hider);
        if (!hider) return;
        PHHiderBody b = local.body;
        if (s.def == outlineDef && b.shapeSerial == outlineSerial) return;
        outlineDef = s.def; outlineSerial = b.shapeSerial;
        int pool = outlineParts.Length - 1;
        if (pieceX == null)
        {
            pieceX = new float[pool]; pieceZ = new float[pool];
            cutX = new float[CUTS]; cutZ = new float[CUTS];
            runA = new float[RUNS]; runB = new float[RUNS]; runAt = new float[RUNS]; runAlongX = new bool[RUNS];
            cellIn = new bool[CUTS * CUTS];
            fMinX = new float[CUTS / 2]; fMaxX = new float[CUTS / 2]; fMinZ = new float[CUTS / 2]; fMaxZ = new float[CUTS / 2];
        }
        int n = 0;
        if (b.shapeDef == s.def && b.shapeN > 0 && b.shapeMin != null)
        {
            n = Mathf.Min(b.shapeN, CUTS / 2);
            for (int i = 0; i < n; i++)
            {
                fMinX[i] = b.shapeMin[i].x; fMaxX[i] = b.shapeMax[i].x;
                fMinZ[i] = b.shapeMin[i].z; fMaxZ[i] = b.shapeMax[i].z;
            }
        }
        else
        {
            float dx = game.gen.defSx[s.def] / 2000f, dz = game.gen.defSz[s.def] / 2000f;
            n = 1; fMinX[0] = -dx; fMaxX[0] = dx; fMinZ[0] = -dz; fMaxZ[0] = dz;
        }
        // the cuts, in order, those within a centimetre of each other as one
        int nx = 0, nz = 0;
        for (int i = 0; i < n; i++)
        {
            nx = AddCut(cutX, nx, fMinX[i]); nx = AddCut(cutX, nx, fMaxX[i]);
            nz = AddCut(cutZ, nz, fMinZ[i]); nz = AddCut(cutZ, nz, fMaxZ[i]);
        }
        int cx = nx - 1, cz = nz - 1;   // cells each way
        for (int i = 0; i < cx * cz; i++) cellIn[i] = false;
        for (int k = 0; k < n; k++)
        {
            int i0 = CutAt(cutX, nx, fMinX[k]), i1 = CutAt(cutX, nx, fMaxX[k]);
            int j0 = CutAt(cutZ, nz, fMinZ[k]), j1 = CutAt(cutZ, nz, fMaxZ[k]);
            for (int i = i0; i < i1; i++)
                for (int j = j0; j < j1; j++) cellIn[i * cz + j] = true;
        }
        // the runs of edge: along x on each cut in z, along z on each cut in x
        nRuns = 0;
        float total = 0f;
        for (int j = 0; j <= cz; j++)
        {
            int from = -1;
            for (int i = 0; i <= cx; i++)
            {
                bool edge = i < cx && ((j > 0 && cellIn[i * cz + j - 1]) != (j < cz && cellIn[i * cz + j]));
                if (edge && from < 0) from = i;
                if (!edge && from >= 0)
                {
                    if (nRuns < RUNS) { runA[nRuns] = cutX[from]; runB[nRuns] = cutX[i]; runAt[nRuns] = cutZ[j]; runAlongX[nRuns] = true; total += cutX[i] - cutX[from]; nRuns++; }
                    from = -1;
                }
            }
        }
        for (int i = 0; i <= cx; i++)
        {
            int from = -1;
            for (int j = 0; j <= cz; j++)
            {
                bool edge = j < cz && ((i > 0 && cellIn[(i - 1) * cz + j]) != (i < cx && cellIn[i * cz + j]));
                if (edge && from < 0) from = j;
                if (!edge && from >= 0)
                {
                    if (nRuns < RUNS) { runA[nRuns] = cutZ[from]; runB[nRuns] = cutZ[j]; runAt[nRuns] = cutX[i]; runAlongX[nRuns] = false; total += cutZ[j] - cutZ[from]; nRuns++; }
                    from = -1;
                }
            }
        }
        // pieces about 12 cm long; longer on a big prop, so the pool is enough
        float step = Mathf.Max(OUTLINE_STEP, total / Mathf.Max(4, pool - nRuns));
        nPieces = 0;
        for (int r = 0; r < nRuns; r++)
        {
            float len = runB[r] - runA[r];
            int parts = Mathf.Max(1, Mathf.CeilToInt(len / step - 0.001f));
            float l = len / parts;
            for (int i = 0; i < parts; i++)
            {
                float m = runA[r] + (i + 0.5f) * l;
                if (runAlongX[r]) Piece(m, runAt[r], l, true); else Piece(runAt[r], m, l, false);
            }
        }
        for (int i = nPieces; i < pool; i++) outlineParts[i].gameObject.SetActive(false);
        // the nub on the front: at the foremost edge of the footprint (not the foremost edge across the middle: an
        // open tent's is its back wall)
        float hx = Mathf.Max(-cutX[0], cutX[nx - 1]), front = cutZ[nz - 1];
        float nub = Mathf.Clamp(hx * 0.5f, 0.03f, 0.08f);
        outlineParts[pool].localScale = new Vector3(nub, OUTLINE_H, nub);
        nubZ = front + nub * 0.5f;
        outlineYaw = -999f;   // lay them anew
    }

    // a value into the ordered cuts (unless one within a centimetre is there); the new count
    int AddCut(float[] cuts, int n, float v)
    {
        int at = 0;
        while (at < n && cuts[at] < v - 0.01f) at++;
        if (at < n && Mathf.Abs(cuts[at] - v) <= 0.01f) return n;
        if (n >= cuts.Length) return n;
        for (int i = n; i > at; i--) cuts[i] = cuts[i - 1];
        cuts[at] = v;
        return n + 1;
    }

    // the cut nearest a value
    int CutAt(float[] cuts, int n, float v)
    {
        int best = 0;
        for (int i = 1; i < n; i++) if (Mathf.Abs(cuts[i] - v) < Mathf.Abs(cuts[best] - v)) best = i;
        return best;
    }

    void Piece(float x, float z, float len, bool alongX)
    {
        if (nPieces >= pieceX.Length) return;
        Transform t = outlineParts[nPieces];
        t.gameObject.SetActive(true);
        t.localScale = alongX ? new Vector3(len + OUTLINE_W, OUTLINE_H, OUTLINE_W) : new Vector3(OUTLINE_W, OUTLINE_H, len + OUTLINE_W);
        pieceX[nPieces] = x; pieceZ[nPieces] = z;
        nPieces++;
    }

    // behind the prop, looking at it, at full distance whatever is in the way (Cor: free flying); whatever lies between
    // is cut away in this view (SetCut, PipCut.cginc)
    void PlaceCamera(PHSlot s)
    {
        float h = game.gen.defSy[s.def] / 1000f;
        Vector3 target = s.propPos + Vector3.up * (h * 0.6f);
        Quaternion head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
        Vector3 back = head * Vector3.back; back.y = 0f;
        if (back.sqrMagnitude < 1e-4f) back = Vector3.back;
        back.Normalize();
        float dist = Mathf.Clamp(2.2f + h, 2.2f, 5f);
        Vector3 want = target + back * dist + Vector3.up * (0.8f + h * 0.5f);
        pipCam.transform.SetPositionAndRotation(want, Quaternion.LookRotation(target - want, Vector3.up));
    }

    // the prop viewer's cutout (PipCut.cginc, Cor: "a stylized cutout in the geometry so you can see the prop in full,
    // along with the things around it"): the own prop's centre and the hole's radius there, its bounding radius and half
    // a metre, and the floor it stands on. Radius 0 while not hiding, so no view cuts anything.
    // The hole opens only while the prop is well hidden from the viewer's camera (Cor: "only expand the cutout if the
    // prop is being significantly obscured"): a few times a second rays go from the camera to points on the prop, and
    // the share of them that something stops is how hidden it is. It opens at CUT_OPEN and closes below CUT_CLOSE (so
    // it does not flicker at one value), and grows in or shrinks away over CUT_EASE.
    const float CUT_OPEN = 0.4f;      // hidden share that opens the hole
    const float CUT_CLOSE = 0.2f;     // hidden share under which it closes again
    const float CUT_EASE = 0.25f;     // seconds for the hole to grow to full size or shrink to nothing
    const float CUT_EVERY = 0.2f;     // seconds between rounds of rays
    const float CUT_SHORT = 0.15f;    // the rays stop this short of the prop: what stands right behind or under it does not count
    const float CUT_PULL = 0.7f;      // the corner points, pulled in towards the middle (on the prop, not on its box's air)
    const int CUT_RAYS = 9, CUT_PER_FRAME = 3;   // rays a round, spread over frames
    int cutId, cutFloorId;
    bool cutOn = true, cutWant;   // on at first, so the first frame clears whatever a value was left (the editor keeps globals between plays)
    int cutRay = CUT_RAYS, cutBlocked, cutMask;
    float cutNext, cutAmt;
    Vector3 cutLo, cutHi;
    [System.NonSerialized] public float cutHidden;   // the last round's hidden share (also read by tests)

    void SetCut(PHSlot s, bool on)
    {
        if (cutId == 0) { cutId = VRCShader.PropertyToID("_UdonPHCut"); cutFloorId = VRCShader.PropertyToID("_UdonPHCutFloor"); }
        if (!on)
        {
            cutAmt = 0f; cutWant = false; cutRay = CUT_RAYS; cutNext = 0f; cutHidden = 0f;
            if (cutOn) { VRCShader.SetGlobalVector(cutId, Vector4.zero); cutOn = false; }
            return;
        }
        UpdateHidden(s);
        cutAmt = Mathf.MoveTowards(cutAmt, cutWant ? 1f : 0f, Time.deltaTime / CUT_EASE);
        if (cutAmt <= 0f)
        {
            if (cutOn) { VRCShader.SetGlobalVector(cutId, Vector4.zero); cutOn = false; }
            return;
        }
        cutOn = true;
        float sx = game.gen.defSx[s.def] / 1000f, sy = game.gen.defSy[s.def] / 1000f, sz = game.gen.defSz[s.def] / 1000f;
        float r = Mathf.Clamp(0.5f * Mathf.Sqrt(sx * sx + sy * sy + sz * sz) + 0.5f, 0.8f, 2.5f);
        r *= cutAmt * cutAmt * (3f - 2f * cutAmt);
        Vector3 c = s.propPos + Vector3.up * (sy * 0.5f);
        VRCShader.SetGlobalVector(cutId, new Vector4(c.x, c.y, c.z, r));
        VRCShader.SetGlobalVector(cutFloorId, new Vector4(s.propPos.y, 0f, 0f, 0f));
    }

    // a round of CUT_RAYS rays every CUT_EVERY seconds, CUT_PER_FRAME of them a frame; at the round's end the share
    // that something stopped, and whether the hole wants to be open
    void UpdateHidden(PHSlot s)
    {
        if (cutRay >= CUT_RAYS)
        {
            if (Time.time < cutNext) return;
            cutNext = Time.time + CUT_EVERY;
            cutRay = 0; cutBlocked = 0;
            // what the cut affects: what the camera draws, but not the own prop, pickups or the panel
            cutMask = pipCam.cullingMask & ~((1 << PHSlot.HIDER_SELF) | (1 << 13));
            // the prop's fitted shape (the hider's body), its own box where it has none
            PHHiderBody b = local.body;
            if (b.shapeDef == s.def && b.shapeN > 0 && b.shapeMin != null)
            {
                cutLo = b.shapeMin[0]; cutHi = b.shapeMax[0];
                for (int i = 1; i < b.shapeN; i++) { cutLo = Vector3.Min(cutLo, b.shapeMin[i]); cutHi = Vector3.Max(cutHi, b.shapeMax[i]); }
            }
            else
            {
                Vector3 half = new Vector3(game.gen.defSx[s.def], 0f, game.gen.defSz[s.def]) / 2000f;
                cutLo = -half; cutHi = half; cutHi.y = game.gen.defSy[s.def] / 1000f;
            }
        }
        Vector3 cam = pipCam.transform.position;
        Quaternion q = Quaternion.Euler(0f, s.propYaw, 0f);
        int end = Mathf.Min(CUT_RAYS, cutRay + CUT_PER_FRAME);
        while (cutRay < end)
        {
            if (Blocked(cam, s.propPos + q * CutPoint(cutRay))) cutBlocked++;
            cutRay++;
        }
        if (cutRay < CUT_RAYS) return;
        cutHidden = (float)cutBlocked / CUT_RAYS;
        if (!cutWant && cutHidden >= CUT_OPEN) cutWant = true;
        else if (cutWant && cutHidden < CUT_CLOSE) cutWant = false;
    }

    // point k on the prop, in its own frame: the middle, the top, then the corners of its box (upper four, lower three),
    // pulled in towards the middle
    Vector3 CutPoint(int k)
    {
        Vector3 mid = (cutLo + cutHi) * 0.5f;
        float h = cutHi.y - cutLo.y;
        if (k == 0) return mid;
        if (k == 1) return new Vector3(mid.x, cutLo.y + h * 0.9f, mid.z);
        int c = (k - 2) & 3;
        float x = (c & 1) == 0 ? cutLo.x : cutHi.x, z = (c & 2) == 0 ? cutLo.z : cutHi.z;
        float y = cutLo.y + h * (k < 6 ? 0.75f : 0.3f);
        if (k >= 6) x = (c & 1) == 0 ? cutHi.x : cutLo.x;   // the lower three mirrored, so they do not sit under the upper ones' first three
        return new Vector3(mid.x + (x - mid.x) * CUT_PULL, y, mid.z + (z - mid.z) * CUT_PULL);
    }

    // whether something lies between the camera and a point, stopping CUT_SHORT before it. Both ways: a ray that
    // starts inside a building's walls does not see them from inside, the one coming back from the prop does
    bool Blocked(Vector3 cam, Vector3 p)
    {
        Vector3 d = p - cam;
        float dist = d.magnitude;
        float len = dist - CUT_SHORT;
        if (len <= 0.05f) return false;
        d = d / dist;
        RaycastHit hit;
        if (Physics.Raycast(cam, d, out hit, len, cutMask, QueryTriggerInteraction.Ignore)) return true;
        return Physics.Raycast(cam + d * len, -d, out hit, len, cutMask, QueryTriggerInteraction.Ignore);
    }

    public override void PostLateUpdate()
    {
        if (profWatch == null) { PostLateBody(); return; }
        double p0 = profWatch.Elapsed.TotalMilliseconds;
        PostLateBody();
        profMs += profWatch.Elapsed.TotalMilliseconds - p0;
    }

    Vector3 outlineShownPos;   // where the outline was last put (PostLateBody), and the heights its rays found
    float outlineShownYaw;
    float[] pieceY;

    void PostLateBody()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (hiderNow && outline != null && nPieces > 0)
        {
            // The outline falls from above onto whatever is there (Cor): each piece lies on the first thing under
            // it, seen from the height of the eyes, so a prop or a step in the way carries the line instead of
            // covering it. Laid anew when the prop has moved or turned, and a few times a second besides (a door,
            // a knocked prop).
            Vector3 foot = local.body.pos;
            float yaw = local.body.yaw;
            // (Perf: the rays down, about 41 of them, are cast again after 2 cm or a degree and a few times a second; in
            // between the pieces follow the prop at the heights the last rays found)
            bool cast = (foot - outlinePos).sqrMagnitude > 0.02f * 0.02f || Mathf.Abs(Mathf.DeltaAngle(yaw, outlineYaw)) > 1f || Time.time >= outlineNext;
            if (cast || (foot - outlineShownPos).sqrMagnitude > 1e-6f || Mathf.Abs(Mathf.DeltaAngle(yaw, outlineShownYaw)) > 0.1f)
            {
                outlineShownPos = foot; outlineShownYaw = yaw;
                if (cast) { outlinePos = foot; outlineYaw = yaw; outlineNext = Time.time + 0.25f; }
                if (pieceY == null || pieceY.Length != outlineParts.Length) { pieceY = new float[outlineParts.Length]; cast = true; }
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                bool grounded = local.body.grounded;
                float top = 0f, reach = 0f, baseY = foot.y;
                RaycastHit g;
                if (cast)
                {
                    top = Mathf.Max(0.3f, me.GetAvatarEyeHeightAsMeters());
                    reach = top + (grounded ? 0.35f : 4f);
                    // in a hop: the ground it will come down on, where nothing else is under a piece
                    if (!grounded && Physics.Raycast(foot + Vector3.up * 0.05f, Vector3.down, out g, 4f, 1 << 11, QueryTriggerInteraction.Ignore)) baseY = g.point.y;
                }
                int last = outlineParts.Length - 1;
                for (int i = 0; i <= nPieces; i++)
                {
                    bool isNub = i == nPieces;
                    Vector3 p = foot + q * (isNub ? new Vector3(0f, 0f, nubZ) : new Vector3(pieceX[i], 0f, pieceZ[i]));
                    int slot = isNub ? last : i;
                    if (cast)
                    {
                        float y = baseY;
                        if (Physics.Raycast(new Vector3(p.x, foot.y + top, p.z), Vector3.down, out g, reach, 1 << 11, QueryTriggerInteraction.Ignore)) y = g.point.y;
                        pieceY[slot] = y;
                    }
                    outlineParts[slot].SetPositionAndRotation(new Vector3(p.x, pieceY[slot] + 0.012f, p.z), q);
                }
            }
        }
        if (!vr)
        {
            // The canvas fills the screen, 900 units high whatever the field of view and as wide as the screen is,
            // so what is anchored to a corner sits in the screen's corner (Cor: the controls ran into VRChat's own).
            // Placed from the screen's own camera (the head's tracking data where there is none); its size and its
            // distance come from HUD_DIST alone. Where the camera's numbers are missing or make no sense, the fixed
            // layout of before (a 1600 x 900 canvas 0.36 m high at 0.5 m: inside any screen) instead.
            VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 eye = head.position;
            Quaternion look = head.rotation;
            float fov = 0f, aspect = 0f;
            int pw = 0, ph = 0;
            bool fromCam = false;
            VRCCameraSettings cam = VRCCameraSettings.ScreenCamera;
            if (cam != null)
            {
                fov = cam.FieldOfView;
                pw = cam.PixelWidth; ph = cam.PixelHeight;
                aspect = pw > 0 && ph > 0 ? (float)pw / ph : cam.Aspect;
                Vector3 cp = cam.Position;
                if (cp.sqrMagnitude > 1e-6f && (cp - eye).sqrMagnitude < 4f) { eye = cp; look = cam.Rotation; fromCam = true; }
            }
            bool sane = fov >= 20f && fov <= 120f && aspect >= 1f && aspect <= 4f;
            float scale = sane ? 2f * HUD_DIST * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / 900f : HUD_DIST * 0.0008f;
            float width = sane ? 900f * aspect : 1600f;
            desktopRoot.localScale = Vector3.one * scale;
            if (desktopCanvas != null && Mathf.Abs(desktopCanvas.sizeDelta.x - width) > 0.5f) desktopCanvas.sizeDelta = new Vector2(width, 900f);
            desktopRoot.SetPositionAndRotation(eye + look * new Vector3(0f, 0f, HUD_DIST), look);
            // once, a little after the start, what the screen camera said (evidence in the VRChat log)
            if (!hudLogged && Time.timeSinceLevelLoad > 5f)
            {
                hudLogged = true;
                Debug.Log("[prophunt] hud cam=" + (cam != null) + " fov=" + fov + " aspect=" + aspect + " px=" + pw + "x" + ph
                    + (cam != null ? " camAspect=" + cam.Aspect + " near=" + cam.NearClipPlane + " camToHead=" + (cam.Position - head.position).magnitude : "")
                    + " fromCam=" + fromCam + " sane=" + sane + " canvas=" + (desktopCanvas != null ? desktopCanvas.sizeDelta.ToString() : "none")
                    + " pivot=" + (desktopCanvas != null ? desktopCanvas.pivot.ToString() : "") + " scale=" + scale + " eyeHeight=" + me.GetAvatarEyeHeightAsMeters());
            }
            return;
        }
        if (!vrPanel.gameObject.activeSelf) return;   // (before the avatar and head queries: not hiding, nothing to place. Perf)
        float sc = AvatarScale();
        VRCPlayerApi.TrackingData headT = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        vrPanel.localScale = new Vector3(panelBaseScale.x * sc, panelBaseScale.y * sc, panelBaseScale.z);
        VRCPlayerApi.TrackingData origin = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
        Quaternion inv = Quaternion.Inverse(origin.rotation);
        if (!panelPlaced)
        {
            // where it first appears: to the right of the view, a little below the eyes, turned to the player
            panelPlaced = true;
            Quaternion yawOnly = Quaternion.Euler(0f, headT.rotation.eulerAngles.y, 0f);
            Vector3 p = headT.position + yawOnly * (new Vector3(0.38f, -0.22f, 0.55f) * sc);
            Quaternion r = Quaternion.LookRotation(p - headT.position + Vector3.up * (0.1f * sc), Vector3.up);
            panelLocal = inv * (p - origin.position) / sc;
            panelLocalRot = inv * r;
        }
        bool held = vrPanelPickup.IsHeld;
        if (held)
        {
            // remember where the player put it, in the room
            panelLocal = inv * (vrPanel.position - origin.position) / sc;
            panelLocalRot = inv * vrPanel.rotation;
            panelDropAt = Time.time;
        }
        else vrPanel.SetPositionAndRotation(origin.position + origin.rotation * (panelLocal * sc), origin.rotation * panelLocalRot);
        if (gun == null || !hiderNow) return;
        Vector3 one = Vector3.one * sc;
        Vector3 panelPos = vrPanel.position;
        Quaternion panelRot = vrPanel.rotation;
        casing.SetPositionAndRotation(panelPos, panelRot);
        casing.localScale = one;

        // ---- right hand: the selector and its beam, along the very ray that picks (PHLocal.Aim)
        VRCPlayerApi.TrackingData rh = me.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
        VRCPlayerApi.TrackingData lh = me.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
        Quaternion aim = local.AimRotation(rh.rotation);
        gun.SetPositionAndRotation(rh.position, aim);
        gun.localScale = one;
        float muzzle = 0.135f * sc;
        float len = local.aimDist - muzzle;
        bool showBeam = local.canPick && len > 0.02f;
        if (beam.gameObject.activeSelf != showBeam) beam.gameObject.SetActive(showBeam);
        if (showBeam)
        {
            float w = Mathf.Max(0.002f, 0.004f * sc);
            beam.SetPositionAndRotation(rh.position + aim * Vector3.forward * (muzzle + len * 0.5f), aim);
            beam.localScale = new Vector3(w, w, len);
            bool valid = local.AimValid();
            if (valid != beamIsHit) { beamIsHit = valid; beamRenderer.sharedMaterial = valid ? beamHit : beamIdle; }
        }

        // ---- left hand: the turner; above it the pad, level, with the small prop standing as the prop stands
        dial.SetPositionAndRotation(lh.position, lh.rotation);
        dial.localScale = one;
        Vector3 padAt = lh.position + Vector3.up * (0.085f * sc);
        pad.SetPositionAndRotation(padAt, Quaternion.identity);
        pad.localScale = new Vector3(0.11f * sc, 0.003f * sc, 0.11f * sc);
        bool lit = local.body.turnHeld;
        if (lit != padIsLit) { padIsLit = lit; padRenderer.sharedMaterial = lit ? padLit : padIdle; }
        if (mini != null)
        {
            mini.transform.SetPositionAndRotation(padAt + Vector3.up * (0.004f * sc), Quaternion.Euler(0f, local.body.yaw, 0f));
            mini.transform.localScale = miniBase * (miniFit * sc);
        }

        // ---- the taunt button on the casing. A press is a hand that came from in front of the button and pushed
        // its cap in. A hand that is gripping, or let go a moment ago, does not count, and nothing counts while the
        // device is carried or was just put down: grabbing the device can never taunt (Cor).
        Vector3 bPos = panelPos + panelRot * (BUTTON_AT * sc);
        tauntButton.SetPositionAndRotation(bPos, panelRot);
        tauntButton.localScale = one;
        Quaternion bInv = Quaternion.Inverse(panelRot);
        bool carried = held || Time.time - panelDropAt < GRAB_GUARD;
        float deepest = -1f;
        for (int h = 0; h < 2; h++)
        {
            if (carried || gripHeld[h] || Time.time - gripUpAt[h] < GRAB_GUARD) { armedUntil[h] = 0f; continue; }
            Vector3 tip = me.GetBonePosition(h == 0 ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
            if (tip.sqrMagnitude < 1e-6f) tip = h == 0 ? lh.position : rh.position;
            Vector3 p = bInv * (tip - bPos) / sc;
            float r = Mathf.Sqrt(p.x * p.x + p.y * p.y);
            if (r < 0.06f && p.z < CAP_FRONT - 0.006f && p.z > -0.30f) armedUntil[h] = Time.time + ARM_TIME;
            if (latched[h] && (p.z < CAP_FRONT - 0.006f || r > 0.08f)) latched[h] = false;
            if (Time.time >= armedUntil[h] || r > 0.045f || p.z < CAP_FRONT || p.z > 0.03f) continue;
            if (p.z > deepest) deepest = p.z;
            if (!latched[h] && p.z > PRESS_AT) { latched[h] = true; PressTaunt(h); }
        }
        float capZ = deepest > CAP_FRONT ? Mathf.Clamp(deepest + 0.012f, CAP_REST, CAP_IN) : CAP_REST;
        tauntCap.localPosition = new Vector3(0f, 0f, capZ);
        bool ready = local.TauntReady();
        if (ready != capIsReady) { capIsReady = ready; tauntCapRenderer.sharedMaterial = ready ? capReady : capWait; }
    }

    void PressTaunt(int h)
    {
        bool done = local.ButtonTaunt();
        Networking.LocalPlayer.PlayHapticEventInHand(h == 0 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right, 0.08f, 0.8f, 120f);
        if (local.sound != null) local.sound.Play(done ? "click_001" : "error_004", done ? 0.5f : 0.3f);
    }
}
