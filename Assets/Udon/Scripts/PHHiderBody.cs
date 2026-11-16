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
//          the VR right stick or the desktop mouse turn the seated player and the prop; the head and the
//          playspace never move or turn it
//   the prop turns with the body in 15 degree steps, and only where its box fits (TryYaw)
// A caught hider (a ghost) moves as a person-sized box through everything but the architecture.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHHiderBody : UdonSharpBehaviour
{
    public PHGame game;
    public PHLocal local;

    // ---- Balance.cs
    readonly float[] hiderRun = { 3.2f, 3.6f, 2.8f, 2.2f };
    readonly float[] hopHeight = { 1.0f, 0.8f, 0f, 0f };
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
    bool jumpPressed;
    RaycastHit hit;

    // ------------------------------------------------------------------ shape
    public void Begin(Vector3 at, float facing)
    {
        pos = at; yaw = facing; stationYaw = facing;
        velY = 0f; grounded = false; lifting = false;
        active = true; ghost = false;
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
    }

    // ------------------------------------------------------------------ input
    public override void InputMoveVertical(float value, UdonInputEventArgs args) { moveV = value; }
    public override void InputMoveHorizontal(float value, UdonInputEventArgs args) { moveH = value; }
    public override void InputLookHorizontal(float value, UdonInputEventArgs args) { look = value; }
    public override void InputJump(bool value, UdonInputEventArgs args) { if (value) jumpPressed = true; }

    void Update()
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

        // Only inputs move and turn the prop, never the head or the playspace (Cor): the VR right stick or the
        // desktop mouse turns the station (and the seated player's view with it), and the move input goes along
        // the station's facing.
        if (!frozen)
        {
            if (vr) { if (Mathf.Abs(look) > 0.1f) stationYaw += look * 120f * dt; }
            else stationYaw += Input.GetAxisRaw("Mouse X") * 2.2f;
        }
        stationYaw = Mathf.Repeat(stationYaw, 360f);
        Quaternion facing = Quaternion.Euler(0f, stationYaw, 0f);
        Vector3 fwd = facing * Vector3.forward;
        Vector3 right = facing * Vector3.right;
        Vector3 wish = fwd * v + right * h;
        if (wish.sqrMagnitude > 1f) wish.Normalize();
        bool run = vr || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        wish *= speed * (run ? 1f : DESKTOP_WALK);

        // the prop faces the way the hider has turned, in 15 degree steps, where it fits
        if (!ghost && !frozen) TryYaw(Mathf.Round(stationYaw / 15f) * 15f);

        float left = dt;
        while (left > 1e-5f)
        {
            float step = Mathf.Min(left, 0.034f);
            Move(wish, jump, step);
            jump = false;
            left -= step;
        }
        if (ghost && pos.y < 0f) { pos.y = 0f; velY = 0f; grounded = true; }
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
        if (mask == 0) { groundY = 0f; groundN = Vector3.up; return from.y - dist <= 0f; }
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
        if (delta.sqrMagnitude > 1e-10f) SlideMove(delta, wasGrounded);
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
