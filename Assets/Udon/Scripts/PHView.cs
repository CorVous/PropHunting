using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon.Common;

// What the local player sees besides the world: as little as possible (most cues are sounds, PHAudio).
//   health: a hider's health bar only. Desktop: low on the screen. VR: on the left wrist, never in front of
//           the eyes.
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
    public Transform wristRoot;        // follows the left hand, VR only
    public Image wristHealth;
    public GameObject wristHealthBar;
    public Camera pipCam;
    public Transform vrPanel;          // the grabbable third-person panel (VRCPickup)
    public VRCPickup vrPanelPickup;
    public LayerMask pipBlockers = 1 << 11;
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
    public TextMeshProUGUI legend;     // desktop
    public Transform outline;          // the own prop's footprint on the ground
    public Transform[] outlineParts;   // front, back, right, left edges and the nub on the front

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
    float legendSince;
    int outlineDef = -2;
    const float OUTLINE_W = 0.02f, OUTLINE_H = 0.004f;

    // The panel keeps its place in the real room (Cor): its pose is kept relative to the playspace's origin, in
    // units of the avatar's size, so walking with the stick or turning with it does not move it about the room.
    Vector3 panelLocal;
    Quaternion panelLocalRot = Quaternion.identity;
    bool panelPlaced;
    Vector3 panelBaseScale = new Vector3(0.36f, 0.24f, 1f);
    Vector3 wristBaseScale = Vector3.one * 0.0004f;
    bool vr;
    int frame;

    void Start()
    {
        vr = Networking.LocalPlayer.IsUserInVR();
        if (desktopRoot != null) desktopRoot.gameObject.SetActive(!vr);
        if (wristRoot != null) wristRoot.gameObject.SetActive(vr);
        if (vrPanel != null) { panelBaseScale = vrPanel.localScale; vrPanel.gameObject.SetActive(false); }
        if (wristRoot != null) wristBaseScale = wristRoot.localScale;
        // the third-person camera does not draw the panel that shows its picture (Cor: the screen saw itself)
        if (pipCam != null && vrPanel != null) pipCam.cullingMask &= ~(1 << vrPanel.gameObject.layer);
        ShowDevices(false);
        devicesOn = false;
        if (legend != null) legend.text = "";
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
        string t = "";
        if (want == 1) t = "W A S D  move      Shift  run      Space  jump\nQ  E  turn the prop      Click  become a prop      T  taunt";
        else if (want == 2) t = "Q  E  turn the prop      T  taunt";
        else if (want == 3) t = "Click  shoot a prop";
        legend.text = t;
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
        if (vr)
        {
            wristHealthBar.SetActive(hider);
            if (hider) wristHealth.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(share), 1f);
            if (vrPanel.gameObject.activeSelf != hider) vrPanel.gameObject.SetActive(hider);
            if (devicesOn != hider) ShowDevices(hider);
            UpdateMini(s, hider);
        }
        else
        {
            desktopHealthBar.SetActive(hider);
            if (hider) desktopHealth.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(share), 1f);
            desktopPip.SetActive(hider);
            UpdateLegend(s, hider);
        }
        UpdateOutline(s, hider);
        hiderNow = hider;
        // the third-person camera renders every second frame while there is a prop to show
        frame++;
        bool pip = hider;
        pipCam.enabled = pip && (frame & 1) == 0;
        if (pip) PlaceCamera(s);
    }

    // the own prop's outline: shown while hiding, its edges sized to the prop's footprint when the prop changes
    void UpdateOutline(PHSlot s, bool hider)
    {
        if (outline == null) return;
        if (outline.gameObject.activeSelf != hider) outline.gameObject.SetActive(hider);
        if (!hider || s.def == outlineDef) return;
        outlineDef = s.def;
        float hx = game.gen.defSx[s.def] / 2000f, hz = game.gen.defSz[s.def] / 2000f;
        outlineParts[0].localPosition = new Vector3(0f, 0f, hz); outlineParts[0].localScale = new Vector3(2f * hx + OUTLINE_W, OUTLINE_H, OUTLINE_W);
        outlineParts[1].localPosition = new Vector3(0f, 0f, -hz); outlineParts[1].localScale = new Vector3(2f * hx + OUTLINE_W, OUTLINE_H, OUTLINE_W);
        outlineParts[2].localPosition = new Vector3(hx, 0f, 0f); outlineParts[2].localScale = new Vector3(OUTLINE_W, OUTLINE_H, 2f * hz + OUTLINE_W);
        outlineParts[3].localPosition = new Vector3(-hx, 0f, 0f); outlineParts[3].localScale = new Vector3(OUTLINE_W, OUTLINE_H, 2f * hz + OUTLINE_W);
        float nub = Mathf.Clamp(hx * 0.5f, 0.03f, 0.08f);
        outlineParts[4].localPosition = new Vector3(0f, 0f, hz + nub * 0.5f); outlineParts[4].localScale = new Vector3(nub, OUTLINE_H, nub);
    }

    // behind the prop, looking at it; pulled in front of walls and ceilings
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
        Vector3 dir = want - target;
        RaycastHit r;
        if (Physics.Raycast(target, dir.normalized, out r, dir.magnitude, pipBlockers, QueryTriggerInteraction.Ignore))
            want = target + dir.normalized * Mathf.Max(0.3f, r.distance - 0.15f);
        pipCam.transform.SetPositionAndRotation(want, Quaternion.LookRotation(target - want, Vector3.up));
    }

    public override void PostLateUpdate()
    {
        if (profWatch == null) { PostLateBody(); return; }
        double p0 = profWatch.Elapsed.TotalMilliseconds;
        PostLateBody();
        profMs += profWatch.Elapsed.TotalMilliseconds - p0;
    }

    void PostLateBody()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (hiderNow && outline != null)
        {
            // on the ground under the prop: in a hop, where it will come down
            Vector3 foot = local.body.pos;
            RaycastHit g;
            if (!local.body.grounded && Physics.Raycast(foot + Vector3.up * 0.05f, Vector3.down, out g, 4f, 1 << 11, QueryTriggerInteraction.Ignore)) foot = g.point;
            outline.SetPositionAndRotation(foot + Vector3.up * 0.012f, Quaternion.Euler(0f, local.body.yaw, 0f));
        }
        if (!vr)
        {
            VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            desktopRoot.SetPositionAndRotation(head.position + head.rotation * new Vector3(0f, 0f, 0.5f), head.rotation);
            return;
        }
        float sc = AvatarScale();
        VRCPlayerApi.TrackingData headT = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        // The health bar lies along the left forearm, near the wrist, and faces the eyes (Cor: it floated off the
        // arm and kept one size). From the avatar's bones, so it fits any avatar; from the hand's tracking where
        // an avatar has no such bones.
        Vector3 elbow = me.GetBonePosition(HumanBodyBones.LeftLowerArm), wrist = me.GetBonePosition(HumanBodyBones.LeftHand);
        Vector3 arm = wrist - elbow;
        if (elbow.sqrMagnitude > 1e-6f && wrist.sqrMagnitude > 1e-6f && arm.sqrMagnitude > 1e-6f)
        {
            float armLen = arm.magnitude;
            Vector3 along = arm / armLen;
            Vector3 at = elbow + arm * 0.72f;
            Vector3 toEye = headT.position - at;
            Vector3 up = toEye - along * Vector3.Dot(toEye, along);   // off the arm, towards the eyes
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            up.Normalize();
            // the canvas's right runs along the arm; its face looks at the eyes
            wristRoot.SetPositionAndRotation(at + up * (0.2f * armLen), Quaternion.LookRotation(-up, Vector3.Cross(-up, along)));
            wristRoot.localScale = wristBaseScale * (armLen / 0.25f) * 0.75f;
        }
        else
        {
            VRCPlayerApi.TrackingData hand = me.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
            wristRoot.SetPositionAndRotation(hand.position + hand.rotation * new Vector3(0f, 0.03f, -0.05f) * sc, hand.rotation * Quaternion.Euler(90f, 0f, 0f));
            wristRoot.localScale = wristBaseScale * sc;
        }
        if (!vrPanel.gameObject.activeSelf) return;
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
