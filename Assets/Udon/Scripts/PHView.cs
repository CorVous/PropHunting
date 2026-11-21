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
//           right hand: the selector, with a beam to what a tap of the trigger would pick; a held trigger grabs
//                       the prop and turns it (PHLocal, PHHiderBody). Shown from the landing, before the first prop.
//           the screen device appears on the left of the view (Cor; the selector is in the right hand)
//           on the screen device's casing: the taunt button, pushed with a hand; never by grabbing
//           (no left-hand device any more, Cor)
//           the grab ring (Cor's experiment): a thin ring round the seat at hip height, a little out from the avatar;
//                       a grip on it holds the prop, and moving that hand round the ring turns the prop
//   desktop: the controls, in the lower left corner; after a while only the two that are easy to forget
//   the own prop's outline (Cor), desktop and VR: the hider does not see their own prop in first person, so its
//           footprint lies on the ground under it as a thin rectangle, with a nub on the front; seen only by the hider.
//           It changes colour while the prop is grabbed for turning (Cor)
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
    // over each bar, 0 to 3 shields: how tough the disguise is (small 0, medium 1, large 2, largest 3: PHSlot.HitDamage)
    public GameObject[] desktopShields, vrShields;
    public UnityEngine.UI.RawImage[] desktopSizes, vrSizes;   // the prop-size pictogram beside them: small to largest crates
    int shieldsShown = -2;
    float shieldPopAt = -99f;
    bool shieldPopping;
    public Camera pipCam;
    public Transform vrPanel;          // the grabbable third-person panel (VRCPickup)
    public VRCPickup vrPanelPickup;
    public Transform gun;              // right hand
    public Transform beam;
    public MeshRenderer beamRenderer;
    public Material beamIdle, beamHit;
    public Transform casing;           // the screen device's body, drawn around the panel
    public Transform tauntButton, tauntCap;
    public MeshRenderer tauntCapRenderer;
    public Transform[] tauntBolts;      // little yellow bolts that fly out of the casing's speaker grill (the chin's slots, under the screen) when the local hider taunts
    float boltsAt = -99f;
    bool boltsOn;
    public Material capReady, capWait;
    // desktop: the controls, drawn as VRChat's own panels are (Cor): a picture for each state, bottom left, clear
    // of VRChat's rows (top left, top right)
    public RawImage legend;
    public Texture2D legendHider, legendHiderShort, legendHunter;
    public RectTransform desktopCanvas;
    const float LEGEND_H = 120f / 1342f;   // a VRChat panel's height against the screen's
    public Transform outline;          // the own prop's footprint on the ground
    public Transform[] outlineParts;   // short pieces of the edges, and the nub on the front (the last)
    public Material outlineTurnMat;    // the outline while the prop is grabbed for turning (Cor)
    Material outlineMat;               // ...and otherwise (the pieces' own, as built)
    public Transform grabRing;         // unit radius, flat; scaled to RING_R and put round the seat while hiding (VR)
    public Material ringIdle, ringHeld;
    public Transform[] ringHands;       // the hand icons on the ring: they pulse until this player's first grab of it this session
    Vector3[] ringHandScale;
    bool ringGrabbedOnce;
    // the final stand (group playtest: hiders did not know when the gun shoots): the hider's own gun burns while it can
    // shoot, flames at its muzzle, VR and desktop; nobody else sees them
    public ParticleSystem gunFire;
    float fireNext;
    MeshRenderer[] ringRenderers;
    bool ringIsHeld;
    MeshRenderer[] outlineRenderers;
    bool outlineTurning;

    // the taunt button, in the casing's own metres (z towards the back of the device): where it sits, and its press
    readonly Vector3 BUTTON_AT = new Vector3(-0.12f, -0.175f, -0.010f);
    const float CAP_REST = -0.022f, CAP_FRONT = -0.034f, CAP_IN = -0.012f, PRESS_AT = -0.02f;
    const float GRAB_GUARD = 0.5f, ARM_TIME = 0.7f;
    const float PRESS_SHOW = 0.45f;   // the cap stays down this long after a press (group playtest: a longer visible press)
    const float CONTACT_R = 0.055f;   // how far round a hand's contact points it counts as touching the cap (Cor: easy to hit)
    float pressShownUntil;
    bool[] gripHeld = new bool[2];         // 0 left, 1 right
    float[] gripUpAt = new float[2];
    float[] armedUntil = new float[2];
    bool[] latched = new bool[2];
    float panelDropAt = -99f;
    bool hiderNow, devicesOn, gunOn, capIsReady = true, beamIsHit;
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
        // the screen device's carry handle (Cor: "an unknown blue bar on top of the screen"): gone (the scene build no
        // longer makes it; this hides it in a scene built before)
        if (casing != null)
        {
            Transform handle = casing.Find("Handle");
            if (handle != null) handle.gameObject.SetActive(false);
        }
        if (grabRing != null)
        {
            ringRenderers = grabRing.GetComponentsInChildren<MeshRenderer>(true);
            grabRing.gameObject.SetActive(false);
            if (ringHands != null)
            {
                ringHandScale = new Vector3[ringHands.Length];
                for (int i = 0; i < ringHands.Length; i++) if (ringHands[i] != null) ringHandScale[i] = ringHands[i].localScale;
            }
        }
        if (outlineParts != null)
        {
            outlineRenderers = new MeshRenderer[outlineParts.Length];
            for (int i = 0; i < outlineParts.Length; i++) if (outlineParts[i] != null) outlineRenderers[i] = outlineParts[i].GetComponent<MeshRenderer>();
            if (outlineRenderers.Length > 0 && outlineRenderers[0] != null) outlineMat = outlineRenderers[0].sharedMaterial;
        }
    }

    // the outline in the grab's colour while the prop is held for turning
    void TintOutline(bool turning)
    {
        if (turning == outlineTurning) return;
        outlineTurning = turning;
        game.builder.CageTint(turning);   // the first-person wireframe too (Cor)
        if (outlineRenderers == null || outlineTurnMat == null || outlineMat == null) return;
        Material m = turning ? outlineTurnMat : outlineMat;
        for (int i = 0; i < outlineRenderers.Length; i++) if (outlineRenderers[i] != null) outlineRenderers[i].sharedMaterial = m;
    }

    void ShowDevices(bool on)
    {
        devicesOn = on;
        if (gun == null) return;
        casing.gameObject.SetActive(on);
        tauntButton.gameObject.SetActive(on);
    }

    // the selector: while hiding, and before the first prop while there is one to pick (Cor: hiders land as themselves)
    void ShowGun(bool on)
    {
        gunOn = on;
        if (gun == null) return;
        gun.gameObject.SetActive(on);
        if (!on) beam.gameObject.SetActive(false);
    }

    // a hand that grabs (the screen device, a door) must never press the taunt button
    public override void InputGrab(bool value, UdonInputEventArgs args)
    {
        int h = args.handType == HandType.LEFT ? 0 : 1;
        gripHeld[h] = value;
        if (!value) gripUpAt[h] = Time.time;
        // the grab ring: a grip near it holds the prop, let go lets go
        PHHiderBody b = local.body;
        if (!value) { if (b.ringHand == h) b.ringHand = -1; return; }
        if (vr && hiderNow && b.ringHand < 0)
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            Vector3 hand = me.GetTrackingData(h == 0 ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand).position;
            if (b.NearRing(hand, AvatarScale(), me.GetAvatarEyeHeightAsMeters()))
            {
                b.ringHand = h;
                if (!ringGrabbedOnce) { ringGrabbedOnce = true; PulseRingHands(1f); }
                me.PlayHapticEventInHand(h == 0 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right, 0.06f, 0.7f, 120f);
            }
        }
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

    // (a scene built before the shields has an empty array here, not null)
    void ShowShields(GameObject[] list, int n)
    {
        if (list == null) return;
        for (int i = 0; i < list.Length; i++) if (list[i] != null) list[i].SetActive(i < n);
    }

    // n: the size class, -1 when not hiding (the crates go)
    void ShowSizes(UnityEngine.UI.RawImage[] list, int n)
    {
        if (list == null) return;
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] == null) continue;
            list[i].gameObject.SetActive(n >= 0);
            list[i].color = i == n ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        }
    }

    // a short pop of the shields when their count changes
    void PopShields(GameObject[] list, float k)
    {
        if (list == null) return;
        Vector3 sc = new Vector3(k, k, 1f);
        for (int i = 0; i < list.Length; i++) if (list[i] != null) list[i].transform.localScale = sc;
    }

    void UpdateBody()
    {
        PHSlot s = Me();
        bool hider = s != null && game.RoleOf(s.index) == PHGame.ROLE_HIDER && s.hiding && !s.caught && s.def >= 0
            && (game.phase == PHGame.PH_BRIEF || game.phase == PHGame.PH_HIDE || game.phase == PHGame.PH_HUNT);
        float share = hider && s.maxHealth > 0 ? Mathf.Clamp01((float)s.health / s.maxHealth) : 0f;
        int shields = hider ? s.SizeClass() : -1;
        if (shields != shieldsShown)
        {
            if (shields > 0 && shieldsShown >= -1) { shieldPopAt = Time.time; shieldPopping = true; }
            shieldsShown = shields;
            ShowShields(desktopShields, shields);
            ShowShields(vrShields, shields);
            ShowSizes(desktopSizes, shields);
            ShowSizes(vrSizes, shields);
        }
        if (shieldPopping)
        {
            float pt = (Time.time - shieldPopAt) / 0.35f;
            float k = pt >= 1f ? 1f : 1f + 0.45f * Mathf.Sin(pt * Mathf.PI);
            if (pt >= 1f) shieldPopping = false;
            PopShields(desktopShields, k);
            PopShields(vrShields, k);
        }
        // (Perf: switched only when they change, here and for the pip camera below)
        if (vr)
        {
            if (vrHealthBar.activeSelf != hider) vrHealthBar.SetActive(hider);
            if (hider) vrHealth.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(share), 1f);
            if (vrPanel.gameObject.activeSelf != hider)
            {
                // (group playtest: one player's screen never showed for a whole round. It kept the place it had in the
                // room from the round before, wherever that now was: it starts beside the view each time hiding begins)
                if (hider) panelPlaced = false;
                // still in the hand when the hiding ends (caught, round over): let go first, so the hand is not left holding a hidden pickup
                if (!hider && vrPanelPickup != null && vrPanelPickup.IsHeld) vrPanelPickup.Drop();
                vrPanel.gameObject.SetActive(hider);
            }
            if (devicesOn != hider) ShowDevices(hider);
            bool gunWant = hider || local.canPick || local.CanShoot();
            if (gunOn != gunWant) ShowGun(gunWant);
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
        // (rendered by hand at the end of the frame, RenderPip; the camera itself stays off)
        pipWant = pip && (frame & 1) == 0;
        if (pipCam.enabled) pipCam.enabled = false;
        if (pip) PlaceCamera(s);
        SetCut(s, pip);
        // Perf (Cor's profile: a 115 ms frame in the hide, with little script time): the prop viewer's first picture of
        // a session draws every map material in its own shader variant, which the client readies on first use. One
        // picture is taken while the map is new and nobody is hiding yet, so that cost lands in the briefing.
        if (!pip && game.round != pipWarmRound && game.MapReady && (game.phase == PHGame.PH_LOADING || game.phase == PHGame.PH_BRIEF))
        {
            pipWarmRound = game.round;
            Vector3 at = game.builder.ReleasePoint();
            pipCam.transform.SetPositionAndRotation(at + new Vector3(0f, 3f, 0f), Quaternion.Euler(30f, game.builder.ReleaseYaw(), 0f));
            pipWant = true;
        }
    }
    int pipWarmRound = -1;

    // the own prop's outline: shown while hiding, laid out anew when the prop changes. It is the footprint of the
    // prop's own collider boxes as the hider's body is moved (Cor: the projection should match the actual collider),
    // so an L-shaped sofa shows as an L; the one box of its size where the body has no such shape.
    void UpdateOutline(PHSlot s, bool hider)
    {
        if (outline == null) return;
        if (outline.gameObject.activeSelf != hider) outline.gameObject.SetActive(hider);
        TintOutline(hider && local.body.Turning());
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
            fenceDirty = true;   // the map may change before the next hiding
            if (cutOn) { VRCShader.SetGlobalVector(cutId, Vector4.zero); cutOn = false; }
            return;
        }
        if (fenceDirty) LearnFences();
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
        if (BlockedRay(cam, d, len)) return true;
        return BlockedRay(cam + d * len, -d, len);
    }

    // one way of Blocked. A fence does not hide (Cor: a low prop behind a fence opened the hole): the fence is pickets
    // (or a space base's rails) that the camera sees through, and its collider is a solid box (6 m high at the map's
    // edge and round the release pen, over a fence drawn 1.1 m high), so a hit on a fence's stretch of the map's
    // collision mesh goes on past it. A ray that starts inside the box leaves it without a hit (a mesh collider's
    // faces stop only rays from outside), so a step of a few centimetres past the hit is enough.
    bool BlockedRay(Vector3 from, Vector3 d, float len)
    {
        RaycastHit hit;
        for (int k = 0; k < 4; k++)
        {
            if (!Physics.Raycast(from, d, out hit, len, cutMask, QueryTriggerInteraction.Ignore)) return false;
            if (fenceArch == null || hit.collider != fenceArch || !OnFence(hit.point, hit.normal)) return true;
            float step = hit.distance + 0.02f;
            from += d * step;
            len -= step;
            if (len <= 0.05f) return false;
        }
        return true;
    }

    // The fences' stretches of the map's grid, learnt from the builder's fence objects when a hiding begins. A fence
    // along x stands at (ex + CELL/2, 0, ez) unturned, one along z at (ex, 0, ez + CELL/2) turned 90 degrees
    // (MapBuilderUdon.Fence); a stretch is keyed by its edge: along x (floor(x / CELL), round(z / CELL)), along z
    // (round(x / CELL), floor(z / CELL)).
    const float FENCE_CELL = 4f;      // MapBuilderUdon.CELL
    const float FENCE_NEAR = 0.14f;   // how far off its edge's line a hit on a fence's collider lies at most (it is 0.16 m thick)
    bool fenceDirty = true;
    bool[] fenceX, fenceZ;            // [i + j * fenceNx], i, j from fenceI0, fenceJ0
    int fenceI0, fenceJ0, fenceNx, fenceNz;
    Collider fenceArch;

    void LearnFences()
    {
        fenceDirty = false;
        fenceX = null; fenceZ = null; fenceArch = null;
        MapBuilderUdon b = game.builder;
        if (b == null || b.fenceRoot == null || b.collisionCollider == null) return;
        Transform root = b.fenceRoot;
        int n = root.childCount;
        if (n == 0) return;
        int i0 = 1 << 20, j0 = 1 << 20, i1 = -(1 << 20), j1 = -(1 << 20);
        for (int k = 0; k < n; k++)
        {
            Vector3 p = root.GetChild(k).position;
            int i = Mathf.FloorToInt(p.x / FENCE_CELL), j = Mathf.FloorToInt(p.z / FENCE_CELL);
            i0 = Mathf.Min(i0, i); j0 = Mathf.Min(j0, j); i1 = Mathf.Max(i1, i + 1); j1 = Mathf.Max(j1, j + 1);
        }
        fenceI0 = i0; fenceJ0 = j0; fenceNx = i1 - i0 + 1; fenceNz = j1 - j0 + 1;
        fenceX = new bool[fenceNx * fenceNz];
        fenceZ = new bool[fenceNx * fenceNz];
        for (int k = 0; k < n; k++)
        {
            Transform t = root.GetChild(k);
            Vector3 p = t.position;
            bool alongX = Mathf.Abs(Vector3.Dot(t.right, Vector3.right)) > 0.7f;
            if (alongX) SetFence(fenceX, Mathf.FloorToInt(p.x / FENCE_CELL), Mathf.RoundToInt(p.z / FENCE_CELL));
            else SetFence(fenceZ, Mathf.RoundToInt(p.x / FENCE_CELL), Mathf.FloorToInt(p.z / FENCE_CELL));
        }
        fenceArch = b.collisionCollider;
    }

    void SetFence(bool[] a, int i, int j)
    {
        i -= fenceI0; j -= fenceJ0;
        if (i < 0 || j < 0 || i >= fenceNx || j >= fenceNz) return;
        a[i + j * fenceNx] = true;
    }

    bool HasFence(bool[] a, int i, int j)
    {
        i -= fenceI0; j -= fenceJ0;
        if (i < 0 || j < 0 || i >= fenceNx || j >= fenceNz) return false;
        return a[i + j * fenceNx];
    }

    // whether a point of the collision mesh lies on a fence's stretch (not a building's wall, a hedge or a low wall)
    // (only a fence's own face: a wall's end or the ground next to a fence line still hides)
    bool OnFence(Vector3 p, Vector3 n)
    {
        if (fenceX == null || Mathf.Abs(n.y) > 0.5f) return false;
        float gz = Mathf.Round(p.z / FENCE_CELL), gx = Mathf.Round(p.x / FENCE_CELL);
        if (Mathf.Abs(p.z - gz * FENCE_CELL) <= FENCE_NEAR && Mathf.Abs(n.z) > 0.9f && HasFence(fenceX, Mathf.FloorToInt(p.x / FENCE_CELL), Mathf.RoundToInt(gz))) return true;
        if (Mathf.Abs(p.x - gx * FENCE_CELL) <= FENCE_NEAR && Mathf.Abs(n.x) > 0.9f && HasFence(fenceZ, Mathf.RoundToInt(gx), Mathf.FloorToInt(p.z / FENCE_CELL))) return true;
        return false;
    }

    // flames at the muzzle while the hider can shoot (the final stand)
    void UpdateFire()
    {
        if (gunFire == null || !local.CanShoot() || Time.time < fireNext) return;
        fireNext = Time.time + 0.035f;
        PHWeapons w = local.weapons;
        Vector3 at;
        if (vr) { if (!gunOn) return; at = local.VrMuzzle(); }
        else
        {
            if (w == null || w.deskSelectorModel == null || !w.deskSelectorModel.gameObject.activeInHierarchy) return;
            at = w.DeskMuzzlePoint();
        }
        gunFire.transform.position = at;
        gunFire.Emit(2);
    }

    public override void PostLateUpdate()
    {
        if (profWatch == null) { PostLateBody(); UpdateFire(); RenderPip(); return; }
        double p0 = profWatch.Elapsed.TotalMilliseconds;
        PostLateBody();
        UpdateFire();
        RenderPip();
        profMs += profWatch.Elapsed.TotalMilliseconds - p0;
    }

    // The prop viewer's picture. Its cutout (PipCut.cginc) needs clip(), and a shader that can discard keeps a tile GPU
    // (Quest) from testing depth early for every draw of it, in every view. So the cut is compiled only into the PH_PIP
    // variant of PropLit and PropShade: the materials that use them are switched to it for this camera's render alone,
    // and the main view draws the plain variant, without clip(). The camera is rendered here, after everything has
    // moved for the frame (it used to render with the frame's cameras), every second frame while there is a prop.
    bool pipWant;
    Material[] pipMats;

    void RenderPip()
    {
        if (!pipWant) return;
        pipWant = false;
        if (pipMats == null) CollectPipMats();
        for (int i = 0; i < pipMats.Length; i++) if (pipMats[i] != null) pipMats[i].EnableKeyword(PIP_KEYWORD);
        pipCam.Render();
        for (int i = 0; i < pipMats.Length; i++) if (pipMats[i] != null) pipMats[i].DisableKeyword(PIP_KEYWORD);
    }
    const string PIP_KEYWORD = "PH_PIP";

    // every material drawn with PropLit or PropShade: the builder's (WorldSceneBuilder sets them) and the shade
    // patch's, as a prop prefab's last child ("Shade") has it
    void CollectPipMats()
    {
        MapBuilderUdon b = game.builder;
        Material shade = null;
        GameObject[] prefabs = b.propPrefabs;
        for (int i = 0; prefabs != null && i < prefabs.Length && shade == null; i++)
        {
            if (prefabs[i] == null) continue;
            Transform t = prefabs[i].transform;
            if (t.childCount == 0) continue;
            Transform last = t.GetChild(t.childCount - 1);
            if (last.name != "Shade") continue;
            MeshRenderer r = last.GetComponent<MeshRenderer>();
            if (r != null) shade = r.sharedMaterial;
        }
        pipMats = new Material[] { b.propLitMat, b.selfMat, b.hoverMat, b.fadeMat, b.shadeSelfMat, shade };
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
            if (desktopRoot == null) return;
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
        if (gunOn && !vrPanel.gameObject.activeSelf) { PlaceGun(me, AvatarScale()); return; }   // (picking the first prop)
        bool ringOn = hiderNow && grabRing != null;
        if (grabRing != null && grabRing.gameObject.activeSelf != ringOn) grabRing.gameObject.SetActive(ringOn);
        local.body.ringShown = ringOn;
        if (!ringOn && local.body.ringHand >= 0) local.body.ringHand = -1;
        if (!hiderNow) local.body.tauntReach = 0f;
        if (!vrPanel.gameObject.activeSelf) return;   // (before the avatar and head queries: not hiding, nothing to place. Perf)
        float sc = AvatarScale();
        if (ringOn) PlaceRing(me, sc);
        VRCPlayerApi.TrackingData headT = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        vrPanel.localScale = new Vector3(panelBaseScale.x * sc, panelBaseScale.y * sc, panelBaseScale.z);
        VRCPlayerApi.TrackingData origin = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
        Quaternion inv = Quaternion.Inverse(origin.rotation);
        // brought back beside the view when it is far from the head (put down and walked away from, a play space reset)
        if (panelPlaced && !vrPanelPickup.IsHeld && (vrPanel.position - headT.position).sqrMagnitude > (2.5f * sc) * (2.5f * sc)) panelPlaced = false;
        if (!panelPlaced)
        {
            // where it first appears: to the left of the view (Cor: away from the selector's hand), a little below the
            // eyes, turned to the player
            panelPlaced = true;
            Quaternion yawOnly = Quaternion.Euler(0f, headT.rotation.eulerAngles.y, 0f);
            Vector3 p = headT.position + yawOnly * (new Vector3(-0.38f, -0.22f, 0.55f) * sc);
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
        VRCPlayerApi.TrackingData rh = PlaceGun(me, sc);
        VRCPlayerApi.TrackingData lh = me.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
        TauntButton(me, held, panelPos, panelRot, one, sc, lh, rh);
    }

    void PulseRingHands(float k)
    {
        if (ringHands == null || ringHandScale == null) return;
        for (int i = 0; i < ringHands.Length && i < ringHandScale.Length; i++) if (ringHands[i] != null) ringHands[i].localScale = ringHandScale[i] * k;
    }

    // the grab ring round the seat (the station: the prop's place and the seat's lag), level, at hip height
    void PlaceRing(VRCPlayerApi me, float sc)
    {
        PHHiderBody b = local.body;
        float eye = me.GetAvatarEyeHeightAsMeters();
        grabRing.SetPositionAndRotation(b.RingCentre(eye), Quaternion.Euler(0f, b.stationYaw, 0f));   // (its hand icons in front)
        grabRing.localScale = Vector3.one * (PHHiderBody.RING_R * sc);
        bool held = b.ringHand >= 0;
        if (!ringGrabbedOnce) PulseRingHands(1f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 5f)));   // (a gentle beat: grab here)
        if (held != ringIsHeld && ringRenderers != null && ringIdle != null && ringHeld != null)
        {
            ringIsHeld = held;
            for (int i = 0; i < ringRenderers.Length; i++) if (ringRenderers[i] != null) ringRenderers[i].sharedMaterial = held ? ringHeld : ringIdle;
        }
    }

    // ---- right hand: the selector and its beam, along the very ray that picks (PHLocal.Aim)
    VRCPlayerApi.TrackingData PlaceGun(VRCPlayerApi me, float sc)
    {
        VRCPlayerApi.TrackingData rh = me.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
        if (!gunOn) return rh;
        Vector3 one = Vector3.one * sc;
        Quaternion aim = local.VrAimRot();
        gun.SetPositionAndRotation(local.VrAimPos(), aim);
        gun.localScale = one;
        // the laser (Cor: keep a laser pointer on the gun, and out of it): from the muzzle along the barrel, to what a
        // pick would take, else to the first thing in the way (the final stand's shots go that way too)
        Vector3 from = local.VrMuzzle(), dir = aim * Vector3.forward;
        float len = local.aimDist;
        if (!local.canPick)
        {
            RaycastHit lh;
            len = Physics.Raycast(from, dir, out lh, PHLocal.SHOT_RANGE, ~(1 << 9 | 1 << 10 | 1 << 18 | 1 << 5 | 1 << 13), QueryTriggerInteraction.Ignore) ? lh.distance : PHLocal.SHOT_RANGE;
        }
        bool showBeam = len > 0.02f;
        if (beam.gameObject.activeSelf != showBeam) beam.gameObject.SetActive(showBeam);
        if (showBeam)
        {
            float w = Mathf.Max(0.002f, 0.004f * sc);
            beam.SetPositionAndRotation(from + dir * (len * 0.5f), aim);
            beam.localScale = new Vector3(w, w, len);
            bool valid = local.canPick && local.AimValid();
            if (valid != beamIsHit) { beamIsHit = valid; beamRenderer.sharedMaterial = valid ? beamHit : beamIdle; }
        }
        return rh;
    }

    void TauntButton(VRCPlayerApi me, bool held, Vector3 panelPos, Quaternion panelRot, Vector3 one, float sc, VRCPlayerApi.TrackingData lh, VRCPlayerApi.TrackingData rh)
    {
        // ---- the taunt button on the casing. A press is a hand that came from in front of the button and pushed
        // its cap in. A hand that is gripping, or let go a moment ago, does not count, and nothing counts while the
        // device is carried or was just put down: grabbing the device can never taunt (Cor).
        Vector3 bPos = panelPos + panelRot * (BUTTON_AT * sc);
        tauntButton.SetPositionAndRotation(bPos, panelRot);
        // (a trigger pressed with a hand this near the button presses it too, PHLocal.TriggerButton)
        local.body.tauntAt = bPos; local.body.tauntReach = 0.08f * sc;
        tauntButton.localScale = one;
        Quaternion bInv = Quaternion.Inverse(panelRot);
        bool carried = held || Time.time - panelDropAt < GRAB_GUARD;
        float deepest = -1f;
        for (int h = 0; h < 2; h++)
        {
            if (carried || gripHeld[h] || Time.time - gripUpAt[h] < GRAB_GUARD) { armedUntil[h] = 0f; continue; }
            // the hand's contact (Cor: put a contact in the hand): the index fingertip, the middle fingertip and the
            // hand itself; whichever is nearest the cap counts, within CONTACT_R of its face
            Vector3 tip = NearestContact(me, h, h == 0 ? lh.position : rh.position, bPos);
            Vector3 p = bInv * (tip - bPos) / sc;
            float r = Mathf.Sqrt(p.x * p.x + p.y * p.y);
            if (r < 0.09f && p.z < CAP_FRONT - 0.004f && p.z > -0.30f) armedUntil[h] = Time.time + ARM_TIME;
            if (latched[h] && (p.z < CAP_FRONT - 0.02f || r > 0.10f)) latched[h] = false;
            if (Time.time >= armedUntil[h] || r > CONTACT_R || p.z < CAP_FRONT - 0.012f || p.z > 0.05f) continue;
            if (p.z > deepest) deepest = p.z;
            if (!latched[h] && p.z > CAP_FRONT + 0.004f) { latched[h] = true; PressTaunt(h); }
        }
        float capZ = deepest > CAP_FRONT ? Mathf.Clamp(deepest + 0.012f, CAP_REST, CAP_IN) : CAP_REST;
        if (Time.time < pressShownUntil) capZ = CAP_IN;
        tauntCap.localPosition = new Vector3(0f, 0f, capZ);
        UpdateBolts();
        bool ready = local.TauntReady();
        if (ready != capIsReady) { capIsReady = ready; tauntCapRenderer.sharedMaterial = ready ? capReady : capWait; }
    }

    Vector3 NearestContact(VRCPlayerApi me, int h, Vector3 hand, Vector3 at)
    {
        Vector3 best = hand;
        Vector3 a = me.GetBonePosition(h == 0 ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
        Vector3 b = me.GetBonePosition(h == 0 ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal);
        if (a.sqrMagnitude > 1e-6f && (a - at).sqrMagnitude < (best - at).sqrMagnitude) best = a;
        if (b.sqrMagnitude > 1e-6f && (b - at).sqrMagnitude < (best - at).sqrMagnitude) best = b;
        return best;
    }

    // a taunt of the local hider's own (the button, T, or a forced one: PHLocal.Taunt): bolts fly out of the speaker grill
    public void ShowTauntBolts() { boltsAt = Time.time; }

    const float GRILL_X = 0.103f, GRILL_Y = -0.175f;   // the speaker grill's centre on the casing (WorldSceneBuilder: Slot0..3)

    void UpdateBolts()
    {
        if (tauntBolts == null) return;
        const float LIFE = 0.65f;
        float t = Time.time - boltsAt;
        bool on = t >= 0f && t < LIFE;
        if (on != boltsOn)
        {
            boltsOn = on;
            for (int i = 0; i < tauntBolts.Length; i++) if (tauntBolts[i] != null) tauntBolts[i].gameObject.SetActive(on);
        }
        if (!on) return;
        float k = t < 0.1f ? t / 0.1f : 1f - (t - 0.1f) / (LIFE - 0.1f);   // pop in, then shrink away
        float sz = 0.034f * (0.4f + 0.6f * k);
        for (int i = 0; i < tauntBolts.Length; i++)
        {
            if (tauntBolts[i] == null) continue;
            float side = i == 0 ? -1f : 1f;
            // out of the grill (its centre at GRILL_X, GRILL_Y on the chin), towards the viewer, one up-left, one up-right
            tauntBolts[i].localPosition = new Vector3(GRILL_X + side * (0.012f + 0.05f * t) + 0.02f * t, GRILL_Y + 0.012f + 0.06f * t, -0.02f - 0.07f * t);
            tauntBolts[i].localRotation = Quaternion.Euler(0f, 0f, -side * 22f);
            tauntBolts[i].localScale = new Vector3(sz, sz, sz);
        }
    }

    // the cap shows a press for PRESS_SHOW (also a trigger press at the button, PHLocal.TriggerButton)
    public void ShowPress() { pressShownUntil = Time.time + PRESS_SHOW; }

    void PressTaunt(int h)
    {
        ShowPress();
        bool done = local.ButtonTaunt();
        Networking.LocalPlayer.PlayHapticEventInHand(h == 0 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right, 0.08f, 0.8f, 120f);
        if (local.sound != null) local.sound.Play(done ? "click_001" : "error_004", done ? 0.5f : 0.3f);
    }
}
