using UdonSharp;
using UnityEngine;

// A lobby button visibly going down when it is hit (Cor's playtest): a quick dip and back. A 3D button (the weapon
// wall panel's round keys) moves its `cap` down by `travel` metres along the cap's own down; a flat UI button (no
// cap) shrinks a little towards the board. Runs only while it moves (a delayed event per frame), so it costs nothing
// at rest. Called by PHButton.Interact, or as an extra OnClick listener of a UI button.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHPress : UdonSharpBehaviour
{
    public Transform cap;          // the part that goes down (none: this object shrinks)
    public float travel = 0.025f;  // metres, for a cap
    public float squash = 0.08f;   // fraction, for a UI button

    const float DOWN = 0.06f, HOLD = 0.1f, UP = 0.18f;
    float t = -1f;
    Vector3 rest;
    bool haveRest;

    public void Press()
    {
        Transform tr = cap != null ? cap : transform;
        if (!haveRest) { rest = cap != null ? tr.localPosition : tr.localScale; haveRest = true; }
        bool running = t >= 0f;
        t = 0f;
        if (!running) SendCustomEventDelayedFrames(nameof(_Step), 1);
    }

    public void _Step()
    {
        t += Mathf.Min(Time.deltaTime, 0.04f);   // (a hitch frame still shows the dip)
        float k;
        if (t < DOWN) k = t / DOWN;
        else if (t < DOWN + HOLD) k = 1f;
        else k = 1f - Mathf.Clamp01((t - DOWN - HOLD) / UP);
        bool done = t >= DOWN + HOLD + UP;
        if (done) k = 0f;
        if (cap != null) cap.localPosition = rest - cap.localRotation * Vector3.up * (travel * k);   // (its parent unscaled)
        else transform.localScale = rest * (1f - squash * k);
        if (done) { t = -1f; return; }
        SendCustomEventDelayedFrames(nameof(_Step), 1);
    }
}
