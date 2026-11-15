using System.Collections.Generic;
using UnityEngine;

// One generated prop. Its identity is the emit index (04 section 4.6.4). It only moves when a dart knocks
// it (D12), nothing destroys it (G12), and it returns to its generated pose on reset (D11).
public class PropInstance : MonoBehaviour
{
    public int emit, def;
    public Vector3 homePos;
    public Quaternion homeRot;
    public PropInstance host;                                   // the prop this one stands on or in, if any
    public List<PropInstance> topChildren;                      // ON_TOP children, knocked with the host
    public Openable openable;
    public float lastCharged = -999f;                           // wrong-prop window (05 section 5.5)
    public bool displaced;

    Rigidbody rb;
    float knockedAt;
    bool tumbling;
    int restLayer;
    Collider[] cols;

    public static readonly List<PropInstance> simulating = new List<PropInstance>();

    public bool Heavy { get { return (GenData.defFlags[def] & (GenData.FL_HEAVY | GenData.FL_FIXTURE)) != 0; } }
    public bool Light { get { return (GenData.defFlags[def] & GenData.FL_LIGHT) != 0; } }
    public bool Tumbling { get { return tumbling; } }

    public void Init(int emitIndex, int defIndex)
    {
        emit = emitIndex; def = defIndex;
        homePos = transform.position; homeRot = transform.rotation;
        restLayer = gameObject.layer;
        cols = GetComponents<Collider>();
        enabled = false;
    }

    // A dart's knock (04 section 4.8): LIGHT tumbles, KNOCKABLE tips or shoves, STATIC_HEAVY only thuds.
    public bool Knock(Vector3 dir, Vector3 point, float damp)
    {
        if (Heavy) return false;
        if (!tumbling)
        {
            // physics cap: the oldest simulating prop is frozen early
            if (simulating.Count >= Balance.KnockMaxSimulating) simulating[0].Settle();
            rb = gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.mass = GenData.defMassKg[def];
            rb.drag = 0.2f; rb.angularDrag = 0.6f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            SetLayer(Layers.Tumble);
            tumbling = true;
            simulating.Add(this);
            enabled = true;
        }
        knockedAt = Time.time;
        displaced = true;
        float mass = rb.mass;
        float speed = (Light ? Balance.KnockImpulseLight : Balance.KnockImpulse) * damp * Mathf.Clamp(6f / (mass + 2f), 0.35f, 1.6f) * 2.2f;
        Vector3 push = (dir.normalized + Vector3.up * 0.45f).normalized * speed;
        rb.AddForceAtPosition(push * mass, point, ForceMode.Impulse);
        if (topChildren != null && damp > 0.9f)
        {
            int n = 0;
            for (int i = 0; i < topChildren.Count && n < Balance.KnockMaxChildren; i++)
            {
                var c = topChildren[i];
                if (c == null || c.tumbling) continue;
                c.Knock(dir + Random.insideUnitSphere * 0.4f, c.transform.position, 0.55f);
                n++;
            }
        }
        return true;
    }

    void SetLayer(int layer)
    {
        gameObject.layer = layer;
    }

    void Update()
    {
        if (!tumbling) { enabled = false; return; }
        float t = Time.time - knockedAt;
        if (t > Balance.KnockSettleTime || (t > 0.4f && rb.IsSleeping()) || transform.position.y < -3f)
            Settle();
    }

    public void Settle()
    {
        if (!tumbling) return;
        tumbling = false;
        simulating.Remove(this);
        if (rb != null) { rb.isKinematic = true; Destroy(rb); rb = null; }
        if (transform.position.y < -1f) { transform.position = homePos; transform.rotation = homeRot; displaced = false; }
        SetLayer(restLayer);
        enabled = false;
    }

    // D11: knocked props return to their generated poses
    public void ResetHome()
    {
        if (tumbling) Settle();
        transform.position = homePos; transform.rotation = homeRot;
        displaced = false;
        lastCharged = -999f;
        if (openable != null) openable.SetOpen(false, true);
    }
}
