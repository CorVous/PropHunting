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
    readonly float[] hopHeight = { 1.0f, 0.8f, 0.55f, 0.4f };   // every prop hops (Cor); the big ones less
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

    // ------------------------------------------------------------------ shape
    public void Begin(Vector3 at, float facing)
    {
        pos = at; yaw = facing; stationYaw = facing;
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
        stepHeight = 0.35f; hop = 0.45f; ledge = 0f; speed = 4f;
        mask = 0;   // through everything; the ground is kept by the height clamp below
    }

    void SetBox(Vector3 size)
    {
        half = new Vector3(Mathf.Max(0.02f, size.x / 2 - SKIN), Mathf.Max(0.02f, size.y / 2 - SKIN / 2), Mathf.Max(0.02f, size.z / 2 - SKIN));
        fullHalf = half;
        narrowHalf = new Vector3(Mathf.Min(half.x, SQUEEZE_HALF), Mathf.Min(half.y, SQUEEZE_HALF_Y), Mathf.Min(half.z, SQUEEZE_HALF));
        canSqueeze = narrowHalf != fullHalf;
        narrow = false;
    }

    // A big prop squeezes through a doorway (Cor: make it easier for large props to get through doors). Where the
    // prop is stopped but a box no wider than SQUEEZE_HALF could go on for a good way (a doorway, a gap: not a wall),
    // it moves as that narrower box until its own box fits again. The prop looks as it always does: in the doorway
    // it overlaps the frame. Left standing in a gap for a while, it is moved to the nearest place where it fits.
    const float SQUEEZE_HALF = 0.28f, SQUEEZE_HALF_Y = 0.99f, SQUEEZE_PASS = 0.9f, SQUEEZE_TIME = 2.5f;
    Vector3 fullHalf, narrowHalf;
    bool canSqueeze, narrow;
    Vector3 squeezeAxis;   // the way through the gap it is squeezing through
    float narrowSince;

    void SqueezeAfterMove()
    {
        half = fullHalf;
        if (Fits(pos, yaw)) { narrow = false; return; }
        half = narrowHalf;
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
                half = narrowHalf;
                bool wall = SweepH(pos, (at - pos).normalized, d);
                half = fullHalf;
                if (wall) continue;
                pos = at; narrow = false;
                return;
            }
        half = narrowHalf;
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
    }

    // ------------------------------------------------------------------ casts (BoxBody)
    Quaternion Rot() { return Quaternion.Euler(0f, yaw, 0f); }

    bool SweepH(Vector3 from, Vector3 dir, float dist)
    {
        if (mask == 0) return false;
        Vector3 h = new Vector3(half.x, half.y - RAISE * 0.5f, half.z);
        Vector3 c = from + Vector3.up * (half.y + RAISE * 0.5f);
        RaycastHit r;   // (an out parameter into a field is not written back in Udon)
        bool any = Physics.BoxCast(c, h, dir, out r, Rot(), dist + GAP, mask, QueryTriggerInteraction.Ignore);
        hit = r;
        return any;
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
        if (Physics.BoxCast(c, h, Vector3.down, out r, Rot(), dist + RAISE, mask, QueryTriggerInteraction.Ignore))
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
        if (Physics.BoxCast(c, h, Vector3.up, out r, Rot(), dist + GAP, mask, QueryTriggerInteraction.Ignore))
            return Mathf.Max(0f, r.distance - GAP);
        return dist;
    }

    public bool Fits(Vector3 at, float atYaw)
    {
        if (mask == 0) return true;
        Vector3 h = new Vector3(half.x, half.y - RAISE * 0.5f, half.z);
        return !Physics.CheckBox(at + Vector3.up * (half.y + RAISE * 0.5f), h, Quaternion.Euler(0, atYaw, 0), mask, QueryTriggerInteraction.Ignore);
    }

    bool hitSomething;
    Vector3 firstNormal;   // of the first thing a Slide met

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
            if (!hitSomething) firstNormal = hit.normal;
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
            float up = CeilingAbove(start, stepHeight);
            if (up > 0.01f)
            {
                Vector3 b = Slide(start + Vector3.up * up, delta);
                bool hitB = hitSomething;
                if (GroundBelow(b, up + 0.01f) && groundN.y > 0.64f)
                {
                    b.y = groundY;
                    float da = new Vector2(a.x - start.x, a.z - start.z).sqrMagnitude;
                    float db = new Vector2(b.x - start.x, b.z - start.z).sqrMagnitude;
                    if (db > da + 1e-4f && b.y - start.y <= stepHeight + 0.01f) { pos = b; blocked = hitB; return; }
                }
            }
        }
        pos = a;
    }

    void Move(Vector3 wish, bool jump, float dt)
    {
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
        // along the wall beside the door)
        if (narrow && delta.sqrMagnitude > 1e-10f)
        {
            float along = Vector3.Dot(delta, squeezeAxis), len = delta.magnitude;
            delta = Mathf.Abs(along) > 0.2f * len ? squeezeAxis * (along > 0f ? len : -len) : Vector3.zero;
        }
        if (delta.sqrMagnitude > 1e-10f)
        {
            Vector3 start = pos;
            SlideMove(delta, wasGrounded);
            if (blocked && canSqueeze && !narrow && mask != 0)
            {
                // straight through what stopped it (not along the way it was going: one seldom walks square at a
                // door), from where it stands or from up to half a metre to either side along the wall
                // (and, when that is a thing standing by the door and not the wall, square along the way it is going)
                for (int tryN = 0; tryN < 2 && !narrow; tryN++)
                {
                Vector3 n = firstNormal; n.y = 0f;
                if (tryN == 1) n = Mathf.Abs(delta.x) > Mathf.Abs(delta.z) ? new Vector3(delta.x > 0f ? -1f : 1f, 0f, 0f) : new Vector3(0f, 0f, delta.z > 0f ? -1f : 1f);
                if (n.sqrMagnitude > 1e-6f)
                {
                    n.Normalize();
                    Vector3 along = new Vector3(-n.z, 0f, n.x);
                    half = narrowHalf;
                    for (int k = 0; k < 7 && !narrow; k++)
                    {
                        float off = ((k + 1) / 2) * 0.16f * ((k & 1) == 1 ? 1f : -1f);   // 0, then 0.16, 0.32, 0.48 to either side
                        if (off != 0f && SweepH(start, off > 0f ? along : -along, Mathf.Abs(off))) continue;
                        Vector3 from = start + along * off;
                        if (SweepH(from, -n, SQUEEZE_PASS)) continue;
                        narrow = true; narrowSince = Time.time; squeezeAxis = -n;
                        pos = from;
                        SlideMove(-n * delta.magnitude, wasGrounded);
                    }
                    if (!narrow) half = fullHalf;
                }
                }
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
        return false;
    }
}
