using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

// The hider's body: the prop's own box, moved by casts (the prototype's BoxBody, 05 section 3.6.2), so a hider
// as a chair fits where a chair fits and a hider as a lamp slips through gaps a person could not.
// The hider sits in their own Immobilize station (station parking); this moves that station on the hider's
// client, and VRChat keeps the seated player with it. Every other client parks its copy of the station and
// draws the synced prop instead (PHSlot).
//   input: Udon's move events (VR thumbstick, desktop WASD) along the facing; Shift runs on
//          desktop (55% without it, as the prototype); jump hops (tiny and medium props), with a ledge assist;
//          the view and the prop turn apart (Cor): the VR right stick (the desktop mouse) turns only the view,
//          and the prop keeps its facing in the world, so looking about never gives a hider away. The prop
//          turns only when asked: VR, the right stick while the left trigger is held; desktop, Q and E.
//          The head and the playspace never move or turn it
//   the prop turns in 15 degree steps, each with a click, and only where its box fits (TryYaw)
// A caught hider (a ghost) moves as a person-sized box through everything but the architecture.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHHiderBody : UdonSharpBehaviour
{
    public PHGame game;
    public PHLocal local;

    // ---- Balance.cs
    readonly float[] hiderRun = { 3.2f, 3.6f, 2.8f, 2.2f };
    readonly float[] hopHeight = { 1.3f, 1.25f, 1.15f, 0.8f };   // every prop hops (Cor), a little higher, over a fence (1.05 m, MapBuilderUdon.FENCE_TOP) but for the very largest
    public const float DESKTOP_WALK = 0.55f, LEDGE = 0.35f, LEDGE_TIME = 0.15f, GRAVITY = 9.81f, MAX_FALL = 12f, SKIN = 0.02f;
    const float GAP = 0.005f, RAISE = 0.03f;

    [HideInInspector] public bool active;
    [HideInInspector] public Vector3 pos;       // centre of the base
    [HideInInspector] public float yaw;         // the prop's facing
    [HideInInspector] public float stationYaw;  // the seated player's facing
    [HideInInspector] public bool grounded, frozen, ghost;

    Vector3 half = new Vector3(0.2f, 0.85f, 0.2f);
    float velY, stepHeight = 0.3f, hop, ledge, speed = 3.6f;
    int mask = 1 << 11;
    bool lifting, blocked;
    Vector3 liftFrom, liftTo;
    float liftT;

    float moveV, moveH, look;
    // turning the prop: VR, the left trigger held makes the right stick turn the prop instead of the view
    [HideInInspector] public bool turnHeld;
    bool wasTurning;
    float propAim, propDetent;            // where the stick has turned the prop to, and the step it stands on
    const float PROP_TURN = 100f;         // degrees a second at full stick
    int keyDir;                           // desktop: Q (-1) or E (+1) held
    float keyNext;
    const float KEY_FIRST = 0.35f, KEY_REPEAT = 0.09f;
    const float EDGE_DEG = 55f;           // how far the desktop view may turn from the station before the station follows
    public float desktopTurn = 4f;        // degrees the station turns per unit of mouse movement at that edge
    bool jumpPressed;
    RaycastHit hit;

    // A correction (a nudge to where the prop fits, out of a gap, aside into a doorway) moves the prop at once but
    // not the player's view (Cor: being displaced is disorienting): the seat stays where it was, and the prop makes
    // its way back under it as the player moves (Move), as far as walls let it. lag is how far the seat is from the
    // prop; PHSlot seats the player at pos + lag. Only its height eases away by itself (the seat ends at the prop's
    // floor). Nudges look for room around the seat, not the prop, so repeated ones do not walk the prop away.
    [HideInInspector] public Vector3 lag;
    const float LAG_MAX = 1.5f;
    void Shift(Vector3 to)
    {
        lag += pos - to;
        if (lag.sqrMagnitude > LAG_MAX * LAG_MAX) lag = Vector3.zero;   // too far to glide: the view goes with it
        pos = to;
    }

    // The prop's own shape (Cor: the fitted colliders, not the box around them): the boxes of its prefab's
    // colliders, in the prop's frame from the middle of its base. shape*: as they are (the outline draws their
    // footprint, PHView); box*: a skin smaller, what the body is moved as. Wherever the box around the prop meets
    // something, these are asked instead, so a table walks over a stool and a chair's legs pass a rug's edge.
    // nBox 0: the one box (a ghost, a prop with other colliders than boxes).
    const int SHAPE_MAX = 12;
    [HideInInspector] public int shapeN, shapeSerial, shapeDef = -1;
    [System.NonSerialized] public Vector3[] shapeMin, shapeMax;
    Vector3[] boxC, boxH;
    int nBox;

    void SetShape(int def, Vector3 size)
    {
        if (shapeMin == null || shapeMin.Length < SHAPE_MAX)
        {
            shapeMin = new Vector3[SHAPE_MAX]; shapeMax = new Vector3[SHAPE_MAX];
            boxC = new Vector3[SHAPE_MAX]; boxH = new Vector3[SHAPE_MAX];
        }
        nBox = 0; shapeN = 0; shapeDef = def; shapeSerial++;
        Vector3 lo = new Vector3(-size.x * 0.5f, 0f, -size.z * 0.5f), hi = new Vector3(size.x * 0.5f, size.y, size.z * 0.5f);
        GameObject pf = null;
        if (game.builder != null && game.builder.propPrefabs != null && def >= 0 && def < game.builder.propPrefabs.Length) pf = game.builder.propPrefabs[def];
        bool ok = pf != null;
        if (ok)
        {
            Transform root = pf.transform;
            Quaternion inv = Quaternion.Inverse(root.rotation);
            Vector3 origin = root.position;
            Collider[] all = pf.GetComponentsInChildren<Collider>(true);
            BoxCollider[] boxes = pf.GetComponentsInChildren<BoxCollider>(true);
            int solid = 0, solidBoxes = 0;
            for (int i = 0; i < all.Length; i++) if (all[i] != null && !all[i].isTrigger) solid++;
            for (int i = 0; i < boxes.Length; i++) if (boxes[i] != null && !boxes[i].isTrigger) solidBoxes++;
            if (solid != solidBoxes || solidBoxes == 0 || solidBoxes > SHAPE_MAX) ok = false;
            for (int i = 0; ok && i < boxes.Length; i++)
            {
                BoxCollider b = boxes[i];
                if (b == null || b.isTrigger) continue;
                Transform t = b.transform;
                Vector3 ce = b.center, ex = b.size * 0.5f;
                Vector3 mn = new Vector3(1e6f, 1e6f, 1e6f), mx = new Vector3(-1e6f, -1e6f, -1e6f);
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = ce + new Vector3((k & 1) == 0 ? -ex.x : ex.x, (k & 2) == 0 ? -ex.y : ex.y, (k & 4) == 0 ? -ex.z : ex.z);
                    Vector3 w = inv * (t.TransformPoint(corner) - origin);
                    mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
                }
                mn = Vector3.Max(mn, lo); mx = Vector3.Min(mx, hi);
                if (mx.x - mn.x < 0.004f || mx.y - mn.y < 0.004f || mx.z - mn.z < 0.004f) continue;
                shapeMin[shapeN] = mn; shapeMax[shapeN] = mx; shapeN++;
            }
        }
        if (!ok || shapeN == 0) { shapeN = 1; shapeMin[0] = lo; shapeMax[0] = hi; return; }
        // the lowest boxes reach down to the base: the fitting leaves off a prop's feet (a chair's, a bookcase's
        // plinth start some centimetres up), and the base is where the prop stands
        float minY = 1e6f;
        for (int i = 0; i < shapeN; i++) minY = Mathf.Min(minY, shapeMin[i].y);
        for (int i = 0; i < shapeN; i++) if (shapeMin[i].y <= minY + 0.02f) shapeMin[i].y = 0f;
        for (int i = 0; i < shapeN; i++)
        {
            Vector3 mn = shapeMin[i], mx = shapeMax[i];
            Vector3 c = (mn + mx) * 0.5f;
            float y0 = mn.y + RAISE, y1 = Mathf.Max(y0 + 0.01f, mx.y - SKIN);
            c.y = (y0 + y1) * 0.5f;
            boxC[i] = c;
            boxH[i] = new Vector3(Mathf.Max(0.01f, (mx.x - mn.x) * 0.5f - SKIN), (y1 - y0) * 0.5f, Mathf.Max(0.01f, (mx.z - mn.z) * 0.5f - SKIN));
        }
        nBox = shapeN;
    }

    // moved as its own shape: not while it is the narrow box of a squeeze, or the thin one of a look for room
    bool Fitted() { return nBox > 0 && half == fullHalf; }

    // ------------------------------------------------------------------ shape
    public void Begin(Vector3 at, float facing)
    {
        pos = at; yaw = facing; stationYaw = facing; lag = Vector3.zero;
        velY = 0f; grounded = false; lifting = false;
        active = true; ghost = false;
        turnHeld = false; wasTurning = false; keyDir = 0;
    }

    public void End() { active = false; }

    // the def's box at its scale (mm in the tables), shrunk by the 2 cm skin; speed and hop by disguise class
    public void SetProp(int def)
    {
        ghost = false;
        Vector3 size = new Vector3(game.gen.defSx[def], game.gen.defSy[def], game.gen.defSz[def]) / 1000f;
        SetBox(size);
        SetShape(def, size);
        int cls = Mathf.Clamp(game.gen.defDisguise[def] - 1, 0, 3);
        stepHeight = Mathf.Clamp(0.4f * size.y, 0.04f, 0.30f);
        hop = hopHeight[cls];
        ledge = cls < 2 ? LEDGE : 0f;
        speed = hiderRun[cls];
        mask = 1 << 11;
    }

    public void SetGhost()
    {
        ghost = true;
        SetBox(new Vector3(0.4f, 1.7f, 0.4f));
        nBox = 0; shapeN = 0; shapeDef = -1; shapeSerial++;
        stepHeight = 0.35f; hop = 0.45f; ledge = 0f; speed = 4f;
        mask = 0;   // through everything; the ground is kept by the height clamp below
    }

    void SetBox(Vector3 size)
    {
        half = new Vector3(Mathf.Max(0.02f, size.x / 2 - SKIN), Mathf.Max(0.02f, size.y / 2 - SKIN / 2), Mathf.Max(0.02f, size.z / 2 - SKIN));
        fullHalf = half;
        narrowHalf = new Vector3(Mathf.Min(half.x, SQUEEZE_HALF), Mathf.Min(half.y, SQUEEZE_HALF_Y), Mathf.Min(half.z, SQUEEZE_HALF));
        canSqueeze = narrowHalf != fullHalf;
        narrow = false; squeezeHalf = Vector3.zero;
    }

    // A big prop squeezes through a doorway (Cor: make it easier for large props to get through doors). Where the
    // prop is stopped but a box no wider than SQUEEZE_HALF could go on for a good way (a doorway, a gap: not a wall),
    // it moves as that narrower box until its own box fits again. The prop looks as it always does: in the doorway
    // it overlaps the frame. Left standing in a gap for a while, it is moved to the nearest place where it fits.
    const float SQUEEZE_HALF = 0.28f, SQUEEZE_HALF_Y = 0.99f, SQUEEZE_PASS = 0.9f, SQUEEZE_TIME = 1.2f;
    Vector3 fullHalf, narrowHalf;
    bool canSqueeze, narrow;
    Vector3 squeezeAxis;   // the way through the gap it is squeezing through
    float narrowSince;
    // The narrow box is turned to the way through, not with the prop, its narrow side across (Cor: "going into a
    // doorway with a long object diagonally seems to get the object stuck"; turned with the prop, 45 degrees made the
    // box about 40% wider across the door). squeezeHalf is that box in the way through's frame (x across, z along).
    Vector3 squeezeHalf;
    float squeezeYaw;
    // where the gap is: gapAt on the way through, and how far along it its sides go on (a wall's thickness)
    Vector3 gapAt;
    float gapDepth, gapFound;

    float BoxYaw() { return half == squeezeHalf ? squeezeYaw : yaw; }

    // the narrow box for squeezing along dir
    void SqueezeBox(Vector3 dir)
    {
        squeezeYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        squeezeHalf = new Vector3(Mathf.Min(narrowHalf.x, narrowHalf.z), narrowHalf.y, Mathf.Max(narrowHalf.x, narrowHalf.z));
        half = squeezeHalf;
    }

    // Held to the way through only while in the gap (Cor: hiders must not get stuck): until the narrow box's back is
    // past the far end of the gap's sides. Out of it the prop moves freely as the narrow box until its own fits:
    // a long prop through a door at a slant needs more room beyond than a corridor or a furnished room has, and held
    // to the axis there it could only go on into what stopped it, or back.
    // (and only in line with it: pushed aside out there, walking back held to the axis ran it into a jamb; not held,
    // it looks for the gap again when stopped)
    bool Held()
    {
        if (!narrow || loose) return false;
        Vector3 off = pos - gapAt; off.y = 0f;
        float along = Vector3.Dot(off, squeezeAxis);
        return along - squeezeHalf.z < gapDepth + 0.02f && (off - squeezeAxis * along).sqrMagnitude < 0.08f * 0.08f;
    }

    // Is what lies ahead (along dir, from 'from') a gap narrower than the prop: something within the prop's own half
    // width on both sides, from the narrow box's back to a little past the prop's own front? A doorway's two jambs,
    // two cupboards. Without this a prop walking along a wall took a table against that wall, or a door frame's
    // edge, for a gap, squeezed "through" along the wall, and could then only move along it (Cor: stuck near a
    // doorway until it let go). Looked for along the narrow box and not only past the prop's front: a long prop at a
    // slant is stopped by a jamb with its centre far back and its front already past the wall, where nothing is on
    // either side, and one wedged in the doorway has the jambs beside its centre.
    // Sets gapFound (how far ahead the sides start) and gapDepth (how far on they go).
    bool GapAhead(Vector3 from, Vector3 dir, Vector3 side)
    {
        Quaternion q = Rot();
        Vector3 r = q * Vector3.right, f = q * Vector3.forward;
        // (and a hand's width more: a slanted prop no wider than the door straight across still wedges in it, across
        // the wall's depth, and stands off the middle of it)
        float across = Mathf.Abs(Vector3.Dot(side, r)) * fullHalf.x + Mathf.Abs(Vector3.Dot(side, f)) * fullHalf.z + 0.15f;
        float front = Mathf.Abs(Vector3.Dot(dir, r)) * fullHalf.x + Mathf.Abs(Vector3.Dot(dir, f)) * fullHalf.z;
        Vector3 mid = from + Vector3.up * Mathf.Min(fullHalf.y, 0.9f);
        // (or a lintel lower than the prop's top: a tall thin prop meets a door's head and not its jambs)
        float top = from.y + 2f * fullHalf.y + RAISE;
        for (float s = -squeezeHalf.z; s < front + 0.55f; s += 0.1f)
        {
            if (!SidesAt(mid + dir * s, side, across) && !OverAt(from + dir * s, top)) continue;
            gapFound = s; gapDepth = 0f;
            for (float d = 0.05f; d < 1f && (SidesAt(mid + dir * (s + d), side, across) || OverAt(from + dir * (s + d), top)); d += 0.05f) gapDepth = d;
            return true;
        }
        return false;
    }

    // the architecture overhead (a door's head, the ceiling over a stair) lower than 'top'; not a prop: a tall prop
    // is not to crawl under a table, a shelf or a stall's roof
    bool OverAt(Vector3 at, float top)
    {
        RaycastHit h;
        return Physics.Raycast(at + Vector3.up * 0.3f, Vector3.up, out h, top - at.y - 0.3f, 1 << 11, QueryTriggerInteraction.Ignore)
            && h.collider == game.builder.collisionCollider;
    }

    // Squeezing vertically too (Cor: "can we squeeze vertically as well if needed?"): under a lintel lower than the
    // narrow box, the box is lowered to the opening's clear height, less a mat's step and a margin. Never below
    // SQUEEZE_LOW of the prop's own height, nor below SQUEEZE_MIN_Y (a 1.2 m box: no opening lower than that lets a
    // prop through), and only under the architecture (a prop overhead is not looked at: BlockedOn stops the box).
    // False where the opening is too low. The doors are 2.1 m high, so this is mostly for props taller than about
    // 2 m (a lamp post, a tall cabinet) and the ways down to basements.
    const float SQUEEZE_LOW = 0.6f, SQUEEZE_MIN_Y = 0.6f, HEAD_MARGIN = 0.06f;
    bool Headroom(Vector3 from, Vector3 dir)
    {
        squeezeHalf.y = narrowHalf.y; half = squeezeHalf;
        float clear = 1e6f;
        RaycastHit h;
        for (float s = gapFound - 0.1f; s <= gapFound + gapDepth + 0.15f; s += 0.1f)
            if (Physics.Raycast(from + dir * s + Vector3.up * 0.3f, Vector3.up, out h, 2f * narrowHalf.y, 1 << 11, QueryTriggerInteraction.Ignore)
                && h.collider == game.builder.collisionCollider)
                clear = Mathf.Min(clear, h.point.y - from.y);
        float y = (clear - RAISE - HEAD_MARGIN) * 0.5f;
        if (y >= narrowHalf.y) return true;
        if (y < Mathf.Min(narrowHalf.y, Mathf.Max(SQUEEZE_LOW * fullHalf.y, SQUEEZE_MIN_Y))) return false;
        squeezeHalf.y = y; half = squeezeHalf;
        return true;
    }

    // Squeezing, out of the gap and stopped while going back towards it (or on towards it): lined up with the gap it
    // came through again and held to it, where the narrow box has room to step aside onto its line and gets through.
    // Pushed aside out there, the look for a gap may not see this one again (Cor: hiders must not get stuck).
    bool BackIntoGap(Vector3 start, Vector3 delta, bool step)
    {
        Vector3 off = start - gapAt; off.y = 0f;
        float along = Vector3.Dot(off, squeezeAxis);
        Vector3 dir = along > 0f ? -squeezeAxis : squeezeAxis;
        if (Vector3.Dot(dir, delta) < 0.25f * delta.magnitude) return false;
        Vector3 aside = off - squeezeAxis * along;
        float d = aside.magnitude;
        if (d > 1f) return false;
        if (d > 1e-4f && SweepH(start, -aside / d, d)) return false;
        Vector3 from = start - aside;
        if (BlockedOn(from, dir, Mathf.Abs(along) + gapDepth + squeezeHalf.z)) return false;
        // going back: the gap's far end is now its near one
        if (dir != squeezeAxis) { gapAt += squeezeAxis * gapDepth; squeezeAxis = dir; squeezeYaw += 180f; }
        narrowSince = Time.time; loose = false;
        Shift(from);
        SlideMove(dir * delta.magnitude, step);
        return true;
    }

    bool SidesAt(Vector3 at, Vector3 side, float across)
    {
        RaycastHit h;
        return Physics.Raycast(at, side, out h, across, mask, QueryTriggerInteraction.Ignore)
            && Physics.Raycast(at, -side, out h, across, mask, QueryTriggerInteraction.Ignore);
    }

    // A long prop at a slant can fit with its own shape inside a doorway, wedged from jamb to jamb, and from there it
    // can only move along its own length: straight on or straight back the jambs stop it (Cor: the player walks in
    // straight, the prop is at an angle). So the squeeze ends where the prop fits only once its own box is
    // clear of the gap, through or back out, or when it stands square to the way through and can slide along it.
    bool SqueezeDone()
    {
        Quaternion q = Rot();
        float ext = Mathf.Abs(Vector3.Dot(squeezeAxis, q * Vector3.right)) * fullHalf.x + Mathf.Abs(Vector3.Dot(squeezeAxis, q * Vector3.forward)) * fullHalf.z;
        float along = Vector3.Dot(pos - gapAt, squeezeAxis);
        if (along - ext > gapDepth || along + ext < 0f) return true;
        return Mathf.Abs(Mathf.Sin(2f * (yaw - squeezeYaw) * Mathf.Deg2Rad)) < 0.1f;
    }

    void SqueezeAfterMove()
    {
        half = fullHalf;
        if (Fits(pos, yaw) && SqueezeDone()) { narrow = false; return; }
        half = squeezeHalf;
        if (Time.time - narrowSince < SQUEEZE_TIME) return;
        // too long in the gap: out to the nearest place its own box fits
        half = fullHalf;
        float reach = Mathf.Max(fullHalf.x, fullHalf.z) + 0.5f;
        for (int ring = 1; ring <= 4; ring++)
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f, d = reach * ring / 4f;
                Vector3 at = pos + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                if (!Fits(at, yaw)) continue;
                // not through a wall: the narrow box can get there
                half = squeezeHalf;
                bool wall = SweepH(pos, (at - pos).normalized, d);
                half = fullHalf;
                if (wall) continue;
                Shift(at); narrow = false;
                return;
            }
        half = squeezeHalf;
        narrowSince = Time.time;   // nowhere yet: look again later
    }

    // ------------------------------------------------------------------ input
    public override void InputMoveVertical(float value, UdonInputEventArgs args) { moveV = value; }
    public override void InputMoveHorizontal(float value, UdonInputEventArgs args) { moveH = value; }
    public override void InputLookHorizontal(float value, UdonInputEventArgs args) { look = value; }
    public override void InputJump(bool value, UdonInputEventArgs args) { if (value) jumpPressed = true; }
    // VR: pushing the right stick up hops too (seated in the prop's station, some controllers' jump does nothing)
    bool lookUpHeld;
    public override void InputLookVertical(float value, UdonInputEventArgs args)
    {
        if (!Networking.LocalPlayer.IsUserInVR()) return;
        // not while the stick turns the prop: a slanted push would hop
        if (turnHeld) { lookUpHeld = value > 0.4f; return; }
        if (value > 0.75f && !lookUpHeld) { lookUpHeld = true; jumpPressed = true; }
        else if (value < 0.4f) lookUpHeld = false;
    }

    // VR: the left trigger, held, hands the right stick to the prop
    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (args.handType == HandType.LEFT) turnHeld = value;
    }

    // one step of the prop's turning took: a click, and in VR a tap in the hand that holds the trigger
    void TurnClick(bool vr)
    {
        if (local != null && local.sound != null) local.sound.Play("click_001", 0.35f);
        if (vr) Networking.LocalPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, 0.04f, 0.6f, 160f);
    }

    // the prop to this facing, if its box fits there; false where it does not
    bool TurnPropTo(float target, bool vr)
    {
        float before = yaw;
        if (!TryYaw(Mathf.Repeat(target, 360f))) return false;
        if (Mathf.Abs(Mathf.DeltaAngle(before, yaw)) > 0.5f) TurnClick(vr);
        return true;
    }

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
        if (!active) return;
        VRCPlayerApi me = Networking.LocalPlayer;
        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        bool vr = me.IsUserInVR();
        float v = moveV, h = moveH;
        if (!vr)
        {
            float kv = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float kh = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            if (Mathf.Abs(kv) > 0f) v = kv;
            if (Mathf.Abs(kh) > 0f) h = kh;
        }
        bool jump = jumpPressed || (!vr && Input.GetKeyDown(KeyCode.Space));
        jumpPressed = false;
        if (frozen) { v = 0f; h = 0f; jump = false; }

        // Moving always goes where the player looks. The view and the prop turn apart (Cor): in VR the right stick
        // turns the station (and the seated player's view with it), and the prop only while the left trigger is
        // held; on desktop the mouse turns the view (VRChat turns a seated player's head with it) and Q and E turn
        // the prop. Head turning and playspace movement never turn the prop.
        Vector3 hf = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation * Vector3.forward;
        hf.y = 0f;
        float headYaw = hf.sqrMagnitude > 1e-4f ? Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg : stationYaw;
        bool turning = vr && turnHeld && !frozen && !ghost;
        if (turning && !wasTurning) { propAim = yaw; propDetent = Mathf.Round(yaw / 15f) * 15f; }
        wasTurning = turning;
        if (vr && !frozen && Mathf.Abs(look) > 0.1f)
        {
            if (turning) propAim += look * PROP_TURN * dt;
            else stationYaw += look * 120f * dt;
        }
        if (turning)
        {
            float detent = Mathf.Round(propAim / 15f) * 15f;
            if (Mathf.Abs(detent - propDetent) > 0.5f)
            {
                propDetent = detent;
                // no room to turn that way: the stick starts again from where the prop stands
                if (!TurnPropTo(detent, true)) { propAim = yaw; propDetent = Mathf.Round(yaw / 15f) * 15f; }
            }
        }
        // desktop: a tap of Q or E is one step; held, the steps repeat
        if (!vr && !frozen && !ghost)
        {
            int dir = (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
            if (dir == 0) keyDir = 0;
            else if (dir != keyDir || Time.time >= keyNext)
            {
                keyNext = Time.time + (dir != keyDir ? KEY_FIRST : KEY_REPEAT);
                keyDir = dir;
                TurnPropTo((Mathf.Round(yaw / 15f) + dir) * 15f, false);
            }
        }
        // desktop: VRChat lets a seated player's view turn only about 90 degrees either way from the station (Cor: a
        // hider could only turn 180 degrees). Near that limit, mouse movement that keeps turning the same way turns the
        // station instead, so the view (and the prop with it) can go all the way round. Only mouse movement does
        // this: turning the station to chase the view would spin it.
        if (!vr && !frozen)
        {
            float rel = Mathf.DeltaAngle(stationYaw, headYaw);
            float mx = Input.GetAxis("Mouse X");
            if ((rel > EDGE_DEG && mx > 0f) || (rel < -EDGE_DEG && mx < 0f)) stationYaw += mx * desktopTurn;
        }
        stationYaw = Mathf.Repeat(stationYaw, 360f);
        float faceYaw = headYaw;
        Quaternion facing = Quaternion.Euler(0f, faceYaw, 0f);
        Vector3 fwd = facing * Vector3.forward;
        Vector3 right = facing * Vector3.right;
        Vector3 wish = fwd * v + right * h;
        if (wish.sqrMagnitude > 1f) wish.Normalize();
        bool run = vr || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        wish *= speed * (run ? 1f : DESKTOP_WALK);

        float left = dt;
        while (left > 1e-5f)
        {
            float step = Mathf.Min(left, 0.034f);
            Move(wish, jump, step);
            jump = false;
            left -= step;
        }
        if (ghost && pos.y < -12f) { pos.y = 0f; velY = 0f; grounded = true; }   // fell out of the world
        // the seat's height settles on the prop's floor: at least half a metre a second, quicker the further it is
        float lagY = Mathf.Abs(lag.y);
        if (lagY > 0f) lag.y = lagY < 0.002f ? 0f : Mathf.MoveTowards(lag.y, 0f, Mathf.Max(0.5f, lagY * 5f) * dt);
    }

    // ------------------------------------------------------------------ casts (BoxBody)
    Quaternion Rot() { return Quaternion.Euler(0f, yaw, 0f); }

    bool SweepH(Vector3 from, Vector3 dir, float dist)
    {
        if (mask == 0) return false;
        Vector3 h = new Vector3(half.x, half.y - RAISE * 0.5f, half.z);
        Vector3 c = from + Vector3.up * (half.y + RAISE * 0.5f);
        RaycastHit r;   // (an out parameter into a field is not written back in Udon)
        // A tenth of a degree off: at exactly 90 or 270 degrees PhysX sweeps a box that is longer than wide as if it
        // were not turned, and short sweeps miss the walls (a prop facing east or west walked through them)
        Quaternion q = Quaternion.Euler(0f, BoxYaw() + 0.1f, 0f);
        bool any = Physics.BoxCast(c, h, dir, out r, q, dist + GAP, mask, QueryTriggerInteraction.Ignore);
        if (Fitted() && (any || Physics.CheckBox(c, h, q, mask, QueryTriggerInteraction.Ignore)))
        {
            // the box around the prop meets something, or already holds something (a cast does not see what it starts
            // in: an open box's flaps over a crate let its walls walk into the crate, and through a bookcase): what its
            // own boxes meet, the nearest
            any = false;
            float best = 1e9f;
            for (int i = 0; i < nBox; i++)
            {
                RaycastHit s;
                if (Physics.BoxCast(from + q * boxC[i], boxH[i], dir, out s, q, dist + GAP, mask, QueryTriggerInteraction.Ignore) && s.distance < best)
                { best = s.distance; r = s; any = true; }
            }
        }
        hit = r;
        return any;
    }

    // Is the way on blocked, by more than a step? A thing low enough to step onto (a door mat, a rug, a threshold)
    // is no wall: the sweep is tried again raised by the step, where the ceiling allows. Without this a mat in a
    // doorway stopped big props: the look for a way through counted it as a wall (Cor: squished objects could not
    // step onto mats in front of doorways). The move itself still steps up as ever (SlideMove).
    // Raised no higher than the thing needs, too: a tall prop raised by the whole step met a door's lintel over the
    // mat, and the squeeze never started (Cor: stuck in a doorway with a mat beyond it, a 1.9 m cabinet under a 2.1 m
    // lintel). Each try is half the one before, down to a few centimetres.
    bool BlockedOn(Vector3 from, Vector3 dir, float dist)
    {
        if (!SweepH(from, dir, dist)) return false;
        float up = CeilingAbove(from, stepHeight);
        for (float r = up; r > 0.02f; r *= 0.5f)
            if (!SweepH(from + Vector3.up * r, dir, dist)) return false;
        return true;
    }

    float groundY;
    Vector3 groundN;

    bool GroundBelow(Vector3 from, float dist)
    {
        if (mask == 0)
        {
            // a ghost passes through walls and props but keeps to the floors (the architecture, layer 11), so it can go
            // up a stair to an upper floor or down into a basement (generator 18); the ground when there is none
            RaycastHit g;
            groundY = 0f; groundN = Vector3.up;
            if (Physics.Raycast(from + Vector3.up * 0.5f, Vector3.down, out g, dist + 0.5f, 1 << 11, QueryTriggerInteraction.Ignore))
            {
                groundY = g.point.y; groundN = g.normal;
                return true;
            }
            return from.y - dist <= 0f && from.y >= -0.05f;
        }
        Vector3 h = new Vector3(half.x - 0.005f, half.y - RAISE * 0.5f, half.z - 0.005f);
        Vector3 c = from + Vector3.up * (half.y + RAISE * 0.5f);
        RaycastHit r;
        Quaternion q = Quaternion.Euler(0f, BoxYaw(), 0f);
        bool any = Physics.BoxCast(c, h, Vector3.down, out r, q, dist + RAISE, mask, QueryTriggerInteraction.Ignore);
        if (Fitted() && (any || Physics.CheckBox(c, h, q, mask, QueryTriggerInteraction.Ignore)))   // (as SweepH)
        {
            any = false;
            float best = 1e9f;
            for (int i = 0; i < nBox; i++)
            {
                RaycastHit s;
                Vector3 bh = boxH[i];
                bh.x = Mathf.Max(0.004f, bh.x - 0.005f); bh.z = Mathf.Max(0.004f, bh.z - 0.005f);
                if (Physics.BoxCast(from + q * boxC[i], bh, Vector3.down, out s, q, dist + RAISE, mask, QueryTriggerInteraction.Ignore) && s.distance < best)
                { best = s.distance; r = s; any = true; }
            }
        }
        if (any)
        {
            hit = r;
            groundY = from.y + RAISE - hit.distance;
            groundN = hit.normal;
            return true;
        }
        return false;
    }

    float CeilingAbove(Vector3 from, float dist)
    {
        if (mask == 0) return dist;
        Vector3 h = new Vector3(half.x - 0.005f, half.y - RAISE * 0.5f, half.z - 0.005f);
        Vector3 c = from + Vector3.up * (half.y + RAISE * 0.5f);
        RaycastHit r;
        Quaternion q = Quaternion.Euler(0f, BoxYaw(), 0f);
        bool any = Physics.BoxCast(c, h, Vector3.up, out r, q, dist + GAP, mask, QueryTriggerInteraction.Ignore);
        if (!Fitted()) return any ? Mathf.Max(0f, r.distance - GAP) : dist;
        if (!any && !Physics.CheckBox(c, h, q, mask, QueryTriggerInteraction.Ignore)) return dist;   // (as SweepH)
        float best = dist + GAP;
        for (int i = 0; i < nBox; i++)
        {
            RaycastHit s;
            Vector3 bh = boxH[i];
            bh.x = Mathf.Max(0.004f, bh.x - 0.005f); bh.z = Mathf.Max(0.004f, bh.z - 0.005f);
            if (Physics.BoxCast(from + q * boxC[i], bh, Vector3.up, out s, q, dist + GAP, mask, QueryTriggerInteraction.Ignore) && s.distance < best) best = s.distance;
        }
        return Mathf.Max(0f, best - GAP);
    }

    public bool Fits(Vector3 at, float atYaw)
    {
        if (mask == 0) return true;
        Vector3 h = new Vector3(half.x, half.y - RAISE * 0.5f, half.z);
        Quaternion q = Quaternion.Euler(0, atYaw, 0);
        if (!Physics.CheckBox(at + Vector3.up * (half.y + RAISE * 0.5f), h, q, mask, QueryTriggerInteraction.Ignore)) return true;
        if (!Fitted()) return false;
        for (int i = 0; i < nBox; i++)
            if (Physics.CheckBox(at + q * boxC[i], boxH[i], q, mask, QueryTriggerInteraction.Ignore)) return false;
        return true;
    }

    bool hitSomething;
    Vector3 firstNormal;   // of the first thing a Slide met
    bool firstArch;        // and whether that was the architecture (a wall, a jamb) and not a prop

    Vector3 Slide(Vector3 from, Vector3 delta)
    {
        Vector3 p = from, remaining = delta;
        hitSomething = false;
        for (int i = 0; i < 3; i++)
        {
            float dist = remaining.magnitude;
            if (dist < 1e-5f) break;
            Vector3 dir = remaining / dist;
            if (!SweepH(p, dir, dist)) { p += remaining; break; }
            if (!hitSomething) { firstNormal = hit.normal; firstArch = hit.collider == game.builder.collisionCollider; }
            hitSomething = true;
            float travel = Mathf.Max(0f, hit.distance - GAP);
            p += dir * travel;
            Vector3 n = hit.normal; n.y = 0f;
            if (n.sqrMagnitude < 1e-6f) break;
            n.Normalize();
            Vector3 rest = remaining - dir * travel;
            rest -= n * Vector3.Dot(rest, n);
            if (Vector3.Dot(rest, delta) <= 0f) break;
            remaining = rest;
        }
        return p;
    }

    void SlideMove(Vector3 delta, bool allowStep)
    {
        Vector3 start = pos;
        Vector3 a = Slide(start, delta);
        bool hitA = hitSomething;
        blocked = hitA;
        if (hitA && allowStep && stepHeight > 0f)
        {
            // raised by the whole step, then by less (as BlockedOn: over a mat under a lintel); the one that gets
            // furthest
            float up = CeilingAbove(start, stepHeight);
            float da = new Vector2(a.x - start.x, a.z - start.z).sqrMagnitude, best = da + 1e-4f;
            Vector3 got = a;
            bool gotHit = hitA;
            for (float r = up; r > 0.02f; r *= 0.5f)
            {
                Vector3 b = Slide(start + Vector3.up * r, delta);
                bool hitB = hitSomething;
                if (!GroundBelow(b, r + 0.01f) || groundN.y <= 0.64f) continue;
                b.y = groundY;
                float db = new Vector2(b.x - start.x, b.z - start.z).sqrMagnitude;
                // (and it fits where it comes down: with its own shape, a box raised over a thing can find the
                // floor beyond it with another, and would be set down through that thing)
                if (db > best && b.y - start.y <= stepHeight + 0.01f && (!Fitted() || Fits(b, yaw))) { best = db; got = b; gotHit = hitB; }
                if (!hitB) break;   // (the whole way: no lower try gets further)
            }
            pos = got; blocked = gotHit;
            return;
        }
        pos = a;
    }

    void Move(Vector3 wish, bool jump, float dt)
    {
        sqFailLeft -= dt; caFailLeft -= dt;
        if (lifting)
        {
            liftT += dt / LEDGE_TIME;
            float t = Mathf.Clamp01(liftT);
            Vector3 p = Vector3.Lerp(liftFrom, liftTo, t);
            p.y = Mathf.Lerp(liftFrom.y, liftTo.y, Mathf.Clamp01(t * 1.6f));
            pos = p;
            if (t >= 1f) { lifting = false; grounded = true; velY = 0f; }
            return;
        }
        bool wasGrounded = grounded;
        if (grounded)
        {
            velY = 0f;
            float up = hop > 0f ? Mathf.Sqrt(2f * GRAVITY * hop) : 0f;
            if (jump && up > 0f) { velY = up; grounded = false; wasGrounded = false; }
        }
        else velY = Mathf.Max(velY - GRAVITY * dt, -MAX_FALL);
        blocked = false;
        Vector3 delta = new Vector3(wish.x, 0f, wish.z) * dt;
        // squeezing through a gap: only along it, forwards or back (walking at a slant slid the narrow box off
        // along the wall beside the door), while in it (Held)
        if (narrow && delta.sqrMagnitude > 1e-10f && Held())
        {
            float along = Vector3.Dot(delta, squeezeAxis), len = delta.magnitude;
            delta = Mathf.Abs(along) > 0.2f * len ? squeezeAxis * (along > 0f ? len : -len) : Vector3.zero;
        }
        if (delta.sqrMagnitude > 1e-10f)
        {
            Vector3 start = pos;
            SlideMove(delta, wasGrounded);
            // (also when squeezing but out of the gap, as the narrow box: back into the doorway it came through, or on
            // into the next)
            // (not again where it just found none: same place, facing, wall and way, for a moment; see SqueezeFailedHere)
            if (blocked && canSqueeze && !Held() && mask != 0 && !SqueezeFailedHere(start, delta))
            {
                // straight through what stopped it (not along the way it was going: one seldom walks square at a
                // door), from where it stands or from up to half a metre to either side along the wall
                // (and, when that is a thing standing by the door and not the wall, square along the way it is going;
                // last, straight through what stopped it as it faces, for a gap between props at a slant)
                bool was = narrow, found = narrow && BackIntoGap(start, delta, wasGrounded);
                Vector3 keepHalf = squeezeHalf, keepAxis = squeezeAxis, keepAt = gapAt;
                float keepYaw = squeezeYaw, keepDepth = gapDepth;
                for (int tryN = 0; tryN < 3 && !found; tryN++)
                {
                Vector3 n = firstNormal; n.y = 0f;
                // a doorway is always square to the map's grid; a slanted prop's corner meets a jamb's edge, whose
                // normal points anywhere, and squeezing that way slid the narrow box along the wall
                if (tryN == 0) n = Mathf.Abs(n.x) > Mathf.Abs(n.z) ? new Vector3(n.x > 0f ? 1f : -1f, 0f, 0f) : new Vector3(0f, 0f, n.z > 0f ? 1f : -1f);
                if (tryN == 1) n = Mathf.Abs(delta.x) > Mathf.Abs(delta.z) ? new Vector3(delta.x > 0f ? -1f : 1f, 0f, 0f) : new Vector3(0f, 0f, delta.z > 0f ? -1f : 1f);
                // (not at the architecture: a doorway is square, and squeezing at a slant there slid along the wall)
                if (tryN == 2 && (firstArch || Mathf.Abs(n.x) < 0.02f || Mathf.Abs(n.z) < 0.02f)) continue;
                // never across the way the player walks: held to it, they could not move on at all
                if (n.sqrMagnitude > 1e-6f && Vector3.Dot(-n, delta) > 0.25f * delta.magnitude)
                {
                    n.Normalize();
                    Vector3 along = new Vector3(-n.z, 0f, n.x);
                    SqueezeBox(-n);
                    for (int k = 0; k < 7 && !found; k++)
                    {
                        float off = ((k + 1) / 2) * 0.16f * ((k & 1) == 1 ? 1f : -1f);   // 0, then 0.16, 0.32, 0.48 to either side
                        if (off != 0f && SweepH(start, off > 0f ? along : -along, Mathf.Abs(off))) continue;
                        Vector3 from = start + along * off;
                        if (!GapAhead(from, -n, along)) continue;   // a way past a thing on one side is no gap: walk round it
                        if (!Headroom(from, -n)) continue;          // (and lower under a lintel, if it needs to be)
                        // and the narrow box gets into it (a slanted long prop is stopped with its centre over a metre
                        // short of the wall: a fixed reach ended before the jambs, and it squeezed off the gap's line)
                        if (BlockedOn(from, -n, Mathf.Max(SQUEEZE_PASS, gapFound + 0.1f))) continue;
                        found = true; narrow = true; loose = false; narrowSince = Time.time; squeezeAxis = -n;
                        gapAt = from - n * gapFound;
                        Shift(from);
                        SlideMove(-n * delta.magnitude, wasGrounded);
                    }
                }
                }
                if (!found)
                {
                    squeezeHalf = keepHalf; squeezeAxis = keepAxis; gapAt = keepAt; squeezeYaw = keepYaw; gapDepth = keepDepth;
                    half = was ? squeezeHalf : fullHalf;
                    if (!was) { sqFailPos = start; sqFailYaw = yaw; sqFailDir = delta.normalized; sqFailN = failN; sqFailLeft = FAIL_HOLD; }
                }
            }
            if (blocked && !narrow && mask != 0)
            {
                Vector3 got = pos - start; got.y = 0f;
                float len = delta.magnitude;
                if (got.magnitude < 0.35f * len)
                {
                    Vector3 dir = delta / len;
                    bool tried = caFailLeft > 0f && SameTry(start, dir, caFailPos, caFailYaw, caFailDir);
                    if (!tried && !CornerAssist(start, dir, len, wasGrounded)) { caFailPos = start; caFailYaw = yaw; caFailDir = dir; caFailLeft = FAIL_HOLD; }
                }
            }
            if (mask != 0) Unstick(start, delta, dt);
            // a corrected prop makes its way back under the player while they move: alone, flat, no further than
            // they moved this step (or 1.5 m/s), as far as walls let it; the seat does not move from this
            if (mask != 0 && !Held() && (lag.x != 0f || lag.z != 0f))
            {
                Vector3 back = new Vector3(lag.x, 0f, lag.z);
                float most = Mathf.Max(delta.magnitude, 1.5f * dt);
                if (back.sqrMagnitude > most * most) back = back.normalized * most;
                bool keepBlocked = blocked, keepHit = hitSomething, keepArch = firstArch;
                Vector3 keepNormal = firstNormal;
                Vector3 p2 = Slide(pos, back);
                p2.y = pos.y;
                lag -= p2 - pos;
                pos = p2;
                if (lag.x * lag.x + lag.z * lag.z < 1e-6f) { lag.x = 0f; lag.z = 0f; }
                blocked = keepBlocked; hitSomething = keepHit; firstNormal = keepNormal; firstArch = keepArch;
            }
        }
        if (narrow) SqueezeAfterMove();
        if (velY > 0f)
        {
            float want = velY * dt;
            float can = CeilingAbove(pos, want);
            pos.y += can;
            if (can < want - 1e-4f) velY = 0f;
            grounded = false;
        }
        else
        {
            float fall = -velY * dt;
            if (GroundBelow(pos, fall + (wasGrounded ? stepHeight : 0f) + 0.01f) && groundN.y > 0.64f) { pos.y = groundY; grounded = true; velY = 0f; }
            else { pos.y -= fall; grounded = false; }
        }
        if (!grounded && blocked && ledge > 0f && delta.sqrMagnitude > 1e-10f) TryLedge(delta.normalized);
        if (pos.y < -20f) { pos.y = 2f; velY = 0f; }
    }

    void TryLedge(Vector3 forward)
    {
        float reach = Mathf.Clamp(Mathf.Max(half.x, half.z) * 1.3f + 0.06f, 0.12f, 0.6f);
        Vector3 top = pos + forward * reach + Vector3.up * ledge;
        if (!Fits(top, yaw)) return;
        if (!GroundBelow(top, ledge) || groundN.y < 0.64f) return;
        float sy = groundY;
        if (sy <= pos.y + 0.02f) return;
        if (!Fits(new Vector3(pos.x, sy + 0.01f, pos.z), yaw)) return;
        lifting = true; liftT = 0f;
        liftFrom = pos; liftTo = new Vector3(top.x, sy, top.z);
        velY = 0f;
    }

    public bool TryYaw(float newYaw)
    {
        if (Mathf.Abs(Mathf.DeltaAngle(yaw, newYaw)) < 0.01f) return true;
        if (Mathf.Abs(half.x - half.z) < 0.03f || Fits(pos, newYaw)) { yaw = newYaw; return true; }
        for (float d = 3f; d <= 15f; d += 3f)
        {
            if (Fits(pos, newYaw + d)) { yaw = newYaw + d; return true; }
            if (Fits(pos, newYaw - d)) { yaw = newYaw - d; return true; }
        }
        // no room to turn where it stands: a little aside there may be (Cor: nudge it so it can turn). As far as
        // its long side sticks out past its short one, and a hand's width
        if (!narrow && !lifting && Nudge(newYaw, Mathf.Min(0.8f, Mathf.Abs(half.x - half.z) + 0.12f))) { yaw = newYaw; return true; }
        return false;
    }

    // ------------------------------------------------------------------ forgiving (Cor: hiders must not get stuck)
    // The nearest place within reach where the prop's own box fits at this facing, with something to stand on, and
    // no wall between here and there (a thin box gets there). The prop is moved to it.
    public bool Nudge(float atYaw, float reach)
    {
        if (mask == 0) return true;
        Vector3 keep = half;
        half = fullHalf;
        // around the seat (where the player is), the seat itself first
        Vector3 centre = pos + new Vector3(lag.x, 0f, lag.z);
        bool off = lag.x != 0f || lag.z != 0f;
        if (off && PlaceAt(centre, atYaw)) return true;
        for (int ring = 1; ring <= 5; ring++)
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.PI / 6f, d = reach * ring / 5f;
                if (PlaceAt(centre + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), atYaw)) return true;
            }
        half = keep;
        return false;
    }

    // the prop there (with half its full box), if it fits at this facing, has something to stand on, and no wall
    // lies between it and there (a thin box gets there)
    bool PlaceAt(Vector3 at, float atYaw)
    {
        if (!Fits(at, atYaw)) return false;
        Vector3 to = at - pos; to.y = 0f;
        float d = to.magnitude;
        if (d > 1e-4f)
        {
            half = new Vector3(Mathf.Min(fullHalf.x, 0.08f), fullHalf.y, Mathf.Min(fullHalf.z, 0.08f));
            bool wall = SweepH(pos, to / d, d);
            half = fullHalf;
            if (wall) return false;
        }
        if (!GroundBelow(at, 0.5f) || groundN.y < 0.64f) return false;   // not out over a drop
        at.y = groundY;
        Shift(at); narrow = false;
        return true;
    }

    // A new disguise where the hider stands; where its box has no room there, the nearest place it has, as it faces
    // or turned a quarter (Cor: a large thing that does not fit looks for a spot where it does)
    public bool FitHere()
    {
        if (lag.x != 0f || lag.z != 0f)
        {
            // where the player is, if it fits there
            Vector3 keep = half;
            half = fullHalf;
            if (PlaceAt(pos + new Vector3(lag.x, 0f, lag.z), yaw)) return true;
            half = keep;
        }
        if (Fits(pos, yaw)) return true;
        float reach = Mathf.Max(fullHalf.x, fullHalf.z) + 1f;
        if (Nudge(yaw, reach)) return true;
        float turned = Mathf.Repeat(yaw + 90f, 360f);
        if (Fits(pos, turned) || Nudge(turned, reach)) { yaw = turned; return true; }
        return false;
    }

    // Stopped almost dead by the corner of a thing (a table's leg, a door's jamb, the end of a wall): it steps
    // aside, up to a hand's width or three, to where the way on is clear, at its own pace. Walking square into a
    // wall finds no such place and stays.
    bool CornerAssist(Vector3 start, Vector3 dir, float len, bool step)
    {
        Vector3 side = new Vector3(-dir.z, 0f, dir.x);
        for (int k = 1; k <= 6; k++)
        {
            float off = ((k + 1) / 2) * 0.08f;
            Vector3 s = (k & 1) == 1 ? side : -side;
            if (SweepH(start, s, off)) continue;                 // no room to step aside that far
            if (BlockedOn(start + s * off, dir, 0.3f)) continue; // nor a clear way on from there
            pos = start;
            SlideMove(s * Mathf.Min(len, off), step);
            return true;
        }
        return false;
    }

    // Perf: pushing into a plain wall, the look for a way through (up to 21 tries of several hundred casts) and the
    // step aside found nothing, and every frame they looked again from the same place: about 4-10 ms a frame. A look
    // that found nothing is not made again for FAIL_HOLD seconds of moving while the prop is within 3 mm of where it
    // stood, faces within half a degree, pushes the same way and meets the same wall. The world does not change
    // under a still prop but for doors and knocked props, and those wait at most FAIL_HOLD. The hold runs down with
    // the move's own time (dt), not the clock, so it ends inside a test that drives Move many times at once too.
    const float FAIL_HOLD = 0.25f;
    Vector3 sqFailPos, sqFailDir, sqFailN, failN;
    float sqFailYaw, sqFailLeft;
    Vector3 caFailPos, caFailDir;
    float caFailYaw, caFailLeft;

    bool SameTry(Vector3 at, Vector3 dir, Vector3 wasAt, float wasYaw, Vector3 wasDir)
    {
        return (at - wasAt).sqrMagnitude < 0.003f * 0.003f && Mathf.Abs(Mathf.DeltaAngle(yaw, wasYaw)) < 0.5f && Vector3.Dot(dir, wasDir) > 0.97f;
    }

    // whether the look for a way through found nothing a moment ago in this same spot (and the wall it met, square to
    // the map's grid, kept in failN for the record)
    bool SqueezeFailedHere(Vector3 start, Vector3 delta)
    {
        Vector3 n = firstNormal; n.y = 0f;
        failN = Mathf.Abs(n.x) > Mathf.Abs(n.z) ? new Vector3(n.x > 0f ? 1f : -1f, 0f, 0f) : new Vector3(0f, 0f, n.z > 0f ? 1f : -1f);
        if (narrow || sqFailLeft <= 0f) return false;
        return failN == sqFailN && SameTry(start, delta.normalized, sqFailPos, sqFailYaw, sqFailDir);
    }

    // Standing inside something (a door swung onto it, a knocked prop came to rest in it) and getting nowhere for
    // half a second of trying: out to the nearest place where it fits
    // And last, whatever the reason (Cor: hiders must never be trapped), half a second of getting nowhere lets the
    // prop go on as the narrow box, free of the way through: squeezing, it is let off the gap's line; inside something
    // with nowhere near to go, it squeezes the way the player pushes, if the narrow box can go there. Where it fits
    // again, or after a while (SqueezeAfterMove), it is its own shape once more. A wall straight ahead stops the
    // narrow box too, and a prop that fits where it stands is only stopped (a wall, a cupboard), not trapped.
    float stuckFor;
    bool loose;   // squeezing, but not held to the way through
    void Unstick(Vector3 start, Vector3 delta, float dt)
    {
        Vector3 got = pos - start; got.y = 0f;
        if (lifting || !grounded || got.sqrMagnitude > 0.01f * delta.sqrMagnitude) { stuckFor = 0f; return; }
        stuckFor += dt;
        if (stuckFor < 0.5f) return;
        stuckFor = 0f;
        if (narrow) { loose = true; return; }
        if (Fits(pos, yaw) || Nudge(yaw, Mathf.Max(fullHalf.x, fullHalf.z) + 0.6f) || !canSqueeze) return;
        Vector3 dir = delta; dir.y = 0f;
        dir.Normalize();
        SqueezeBox(dir);
        if (BlockedOn(pos, dir, 0.3f)) { half = fullHalf; return; }
        narrow = true; loose = true; narrowSince = Time.time;
        squeezeAxis = dir; gapAt = pos; gapDepth = 0f;
    }
}
