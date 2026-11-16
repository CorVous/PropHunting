using UnityEngine;

// Doors and lids of an openable prop (04 section 4.9): a click toggles them with a 0.3 s eased swing (C7).
// The door colliders are solid, so a closed door stops darts and prop bodies; a hider inside is really shut in.
public class Openable : MonoBehaviour
{
    public Transform[] doors;
    public float[] sign;
    public bool open;
    public float lastOpenedByHunter = -999f;
    float t = 0f;        // 0 closed .. 1 open
    Bounds inside;       // in the prop's local space

    public void Init(Transform[] doorTransforms, float[] signs, Bounds localInside)
    {
        doors = doorTransforms; sign = signs; inside = localInside;
        enabled = false;
        Apply();
    }

    public void Toggle() { SetOpen(!open, false); }

    public void SetOpen(bool value, bool instantly)
    {
        open = value;
        if (instantly) { t = open ? 1f : 0f; Apply(); enabled = false; }
        else enabled = true;
    }

    void Update()
    {
        float target = open ? 1f : 0f;
        t = Mathf.MoveTowards(t, target, Time.deltaTime / Balance.DoorSwingTime);
        Apply();
        if (Mathf.Approximately(t, target)) enabled = false;
    }

    void Apply()
    {
        float e = t * t * (3f - 2f * t);
        for (int i = 0; i < doors.Length; i++)
            doors[i].localRotation = Quaternion.Euler(0f, sign[i] * Balance.DoorOpenAngle * e, 0f);
    }

    public bool Moving { get { return enabled; } }

    // is this world point inside the furniture's hollow?
    public bool Contains(Vector3 world)
    {
        return inside.Contains(transform.InverseTransformPoint(world));
    }
}
