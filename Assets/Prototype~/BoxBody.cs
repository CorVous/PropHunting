using UnityEngine;

// A kinematic body built from casts (05 section 3.6.2): a box turned with the body's yaw, moved only where
// its sweeps say it may. It applies no force to anything, so walking never moves a prop (D12), and it uses
// no CharacterController (not allowed in VRChat worlds). Used for the walking player and for a hider's prop.
//
//   SlideMove: lift by the step height, sweep, slide along what is hit, settle down
//   vertical move with gravity, a hop, a snap down over small steps
//   ledge assist: during a hop, a surface up to 0.35 m above the base is climbed if the box fits there
public class BoxBody
{
    public Vector3 pos;                 // centre of the base
    public float yaw;
    public Vector3 half;                // half extents of the box (already shrunk by the skin)
    public float velY;
    public bool grounded;
    public float stepHeight = 0.3f;
    public float hopHeight;             // 0 = cannot hop
    public float jumpSpeed;             // used instead of hopHeight when > 0 (the walking body)
    public float ledge;                 // ledge assist reach, 0 = none
    public int mask = Layers.MoveMask;
    public bool blocked;                // the last horizontal move hit something
    public bool lifting;                // a ledge assist is in progress
    public float overlapTime;           // how long the body has been stuck inside something

    Vector3 liftFrom, liftTo;
    float liftT;
    readonly GameObject helper;
    readonly BoxCollider helperCol;
    static readonly Collider[] overlaps = new Collider[16];
    const float Gap = 0.005f;           // casts stop this far from what they hit
    const float Raise = 0.03f;          // horizontal sweeps ride this far above the base so floors do not catch

    public BoxBody(string name)
    {
        helper = new GameObject(name + " body");
        helper.layer = Layers.Body;
        helperCol = helper.AddComponent<BoxCollider>();
        helperCol.isTrigger = true;
    }

    public void Destroy()
    {
        if (helper != null) Object.Destroy(helper);
    }

    // size: the prop type's box at its scale (or the walking body); shrunk by the 2 cm skin
    public void SetShape(Vector3 size)
    {
        float s = Balance.BodySkin;
        half = new Vector3(Mathf.Max(0.02f, size.x / 2 - s), Mathf.Max(0.02f, size.y / 2 - s / 2), Mathf.Max(0.02f, size.z / 2 - s));
        helperCol.size = half * 2f;
    }

    public Quaternion Rot { get { return Quaternion.Euler(0f, yaw, 0f); } }
    public Vector3 Centre { get { return pos + Vector3.up * half.y; } }
    public float Height { get { return half.y * 2f; } }

    bool SweepH(Vector3 from, Vector3 dir, float dist, out RaycastHit hit)
    {
        Vector3 h = new Vector3(half.x, half.y - Raise * 0.5f, half.z);
        Vector3 c = from + Vector3.up * (half.y + Raise * 0.5f);
        return Physics.BoxCast(c, h, dir, out hit, Rot, dist + Gap, mask, QueryTriggerInteraction.Ignore);
    }

    // the flat surface under the body within `dist` below the base: true and its height
    bool GroundBelow(Vector3 from, float dist, out float surfaceY, out Vector3 normal)
    {
        RaycastHit hit;
        Vector3 h = new Vector3(half.x - 0.005f, half.y - Raise * 0.5f, half.z - 0.005f);
        Vector3 c = from + Vector3.up * (half.y + Raise * 0.5f);
        if (Physics.BoxCast(c, h, Vector3.down, out hit, Rot, dist + Raise, mask, QueryTriggerInteraction.Ignore))
        {
            surfaceY = from.y + Raise - hit.distance;
            normal = hit.normal;
            return true;
        }
        surfaceY = 0f; normal = Vector3.up;
        return false;
    }

    float CeilingAbove(Vector3 from, float dist)
    {
        RaycastHit hit;
        Vector3 h = new Vector3(half.x - 0.005f, half.y - Raise * 0.5f, half.z - 0.005f);
        Vector3 c = from + Vector3.up * (half.y + Raise * 0.5f);
        if (Physics.BoxCast(c, h, Vector3.up, out hit, Rot, dist + Gap, mask, QueryTriggerInteraction.Ignore))
            return Mathf.Max(0f, hit.distance - Gap);
        return dist;
    }

    public bool Fits(Vector3 at, float atYaw)
    {
        Vector3 h = new Vector3(half.x, half.y - Raise * 0.5f, half.z);
        return !Physics.CheckBox(at + Vector3.up * (half.y + Raise * 0.5f), h, Quaternion.Euler(0, atYaw, 0), mask, QueryTriggerInteraction.Ignore);
    }

    // sweep and slide along what is hit, up to 3 times; returns the end position
    Vector3 Slide(Vector3 from, Vector3 delta, out bool hitSomething)
    {
        Vector3 p = from, remaining = delta;
        hitSomething = false;
        for (int i = 0; i < 3; i++)
        {
            float dist = remaining.magnitude;
            if (dist < 1e-5f) break;
            Vector3 dir = remaining / dist;
            RaycastHit hit;
            if (!SweepH(p, dir, dist, out hit)) { p += remaining; break; }
            hitSomething = true;
            float travel = Mathf.Max(0f, hit.distance - Gap);
            p += dir * travel;
            Vector3 n = hit.normal; n.y = 0f;
            if (n.sqrMagnitude < 1e-6f) break;
            n.Normalize();
            Vector3 left = remaining - dir * travel;
            left -= n * Vector3.Dot(left, n);
            // never slide back against the direction of travel
            if (Vector3.Dot(left, delta) <= 0f) break;
            remaining = left;
        }
        return p;
    }

    void SlideMove(Vector3 delta, bool allowStep)
    {
        Vector3 start = pos;
        bool hitA;
        Vector3 a = Slide(start, delta, out hitA);
        blocked = hitA;
        if (hitA && allowStep && stepHeight > 0f)
        {
            // lift, sweep, settle down
            float up = CeilingAbove(start, stepHeight);
            if (up > 0.01f)
            {
                bool hitB;
                Vector3 raised = start + Vector3.up * up;
                Vector3 b = Slide(raised, delta, out hitB);
                float sy; Vector3 n;
                if (GroundBelow(b, up + 0.01f, out sy, out n) && n.y > 0.64f)
                {
                    b.y = sy;
                    float da = new Vector2(a.x - start.x, a.z - start.z).sqrMagnitude;
                    float db = new Vector2(b.x - start.x, b.z - start.z).sqrMagnitude;
                    if (db > da + 1e-4f && b.y - start.y <= stepHeight + 0.01f) { pos = b; blocked = hitB; return; }
                }
            }
        }
        pos = a;
    }

    // One frame of movement. wish: horizontal velocity in world space.
    public void Move(Vector3 wish, bool jump, float dt)
    {
        if (lifting)
        {
            liftT += dt / Balance.LedgeLiftTime;
            float t = Mathf.Clamp01(liftT);
            // up first, then forward
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
            float up = jumpSpeed > 0f ? jumpSpeed : (hopHeight > 0f ? Mathf.Sqrt(2f * Balance.Gravity * hopHeight) : 0f);
            if (jump && up > 0f) { velY = up; grounded = false; wasGrounded = false; }
        }
        else velY = Mathf.Max(velY - Balance.Gravity * dt, -Balance.MaxFall);

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
            float sy; Vector3 n;
            // snap down over small steps while walking; plain fall otherwise
            if (GroundBelow(pos, fall + (wasGrounded ? stepHeight : 0f) + 0.01f, out sy, out n) && n.y > 0.64f)
            {
                pos.y = sy; grounded = true; velY = 0f;
            }
            else { pos.y -= fall; grounded = false; }
        }

        if (!grounded && blocked && ledge > 0f && delta.sqrMagnitude > 1e-10f) TryLedge(delta.normalized);
        if (pos.y < -20f) { pos.y = 2f; velY = 0f; }
    }

    // ledge assist (05 section 3.6.2): airborne and pressing into a solid face whose top is within reach
    void TryLedge(Vector3 forward)
    {
        float reach = Mathf.Clamp(Mathf.Max(half.x, half.z) * 1.3f + 0.06f, 0.12f, 0.6f);
        Vector3 top = pos + forward * reach + Vector3.up * ledge;
        if (!Fits(top, yaw)) return;
        float sy; Vector3 n;
        if (!GroundBelow(top, ledge, out sy, out n) || n.y < 0.64f) return;
        if (sy <= pos.y + 0.02f) return;
        Vector3 above = new Vector3(pos.x, sy + 0.01f, pos.z);
        if (!Fits(above, yaw)) return;
        lifting = true; liftT = 0f;
        liftFrom = pos; liftTo = new Vector3(top.x, sy, top.z);
        velY = 0f;
    }

    // Turning in place: refused if the box would not fit; the nearest yaw within 15 degrees is used instead.
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

    // Push the body out of anything that moved into it (a door swung by someone else): up to 0.3 m.
    public void Depenetrate(float dt)
    {
        Vector3 c = Centre + Vector3.up * (Raise * 0.5f);
        Vector3 h = new Vector3(half.x, half.y - Raise * 0.5f, half.z);
        int n = Physics.OverlapBoxNonAlloc(c, h, overlaps, Rot, mask, QueryTriggerInteraction.Ignore);
        if (n == 0) { overlapTime = 0f; return; }
        helperCol.size = h * 2f;
        Vector3 total = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            Vector3 dir; float dist;
            Collider o = overlaps[i];
            if (Physics.ComputePenetration(helperCol, c, Rot, o, o.transform.position, o.transform.rotation, out dir, out dist))
                total += dir * (dist + 0.002f);
        }
        total.y = Mathf.Max(0f, total.y);
        if (total.magnitude > 0.3f) total = total.normalized * 0.3f;
        pos += total;
        overlapTime += dt;
    }
}
