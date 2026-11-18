using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// The lobby stops drawing while the local player is out on the map (Cor: profiling found the lobby, 400 m away, still
// drawn whenever it was in view: about 120 shader switches and 100 draw calls). Its renderers and canvases are switched
// off, nothing else: its colliders and behaviours keep running. Each client decides for itself; nothing is synced.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHLobbyCull : UdonSharpBehaviour
{
    public float radius = 45f;          // the lobby is drawn while the player is this close to its centre
    Renderer[] rs;
    bool[] was;
    Canvas[] cs;
    bool[] wasC;
    bool shown = true;
    float next;

    void Start()
    {
        rs = GetComponentsInChildren<Renderer>(true);
        was = new bool[rs.Length];
        cs = GetComponentsInChildren<Canvas>(true);
        wasC = new bool[cs.Length];
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
        if (Time.time < next) return;
        next = Time.time + 0.4f;
        VRCPlayerApi me = Networking.LocalPlayer;
        if (me == null) return;
        bool near = (me.GetPosition() - transform.position).sqrMagnitude < radius * radius;
        if (near == shown) return;
        shown = near;
        if (!near)
        {
            // remember what was on, so a renderer switched off for its own reasons stays off
            for (int i = 0; i < rs.Length; i++) { if (rs[i] == null) continue; was[i] = rs[i].enabled; rs[i].enabled = false; }
            for (int i = 0; i < cs.Length; i++) { if (cs[i] == null) continue; wasC[i] = cs[i].enabled; cs[i].enabled = false; }
        }
        else
        {
            for (int i = 0; i < rs.Length; i++) if (rs[i] != null && was[i]) rs[i].enabled = true;
            for (int i = 0; i < cs.Length; i++) if (cs[i] != null && wasC[i]) cs[i].enabled = true;
        }
    }
}
