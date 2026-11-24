using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// The lobby stops drawing while the local player is out on the map (Cor: profiling found the lobby, 400 m away, still
// drawn whenever it was in view: about 120 shader switches and 100 draw calls). Its renderers and canvases are switched
// off, nothing else: its colliders and behaviours keep running. Each client decides for itself; nothing is synced.
// The sun's realtime shadows go off while the player is in the lobby (lobby profiling 2026-11-24: about 130 draw
// calls there, for a few small shadows); the lobby's dressing keeps its shadows from a map baked with the scene
// (LobbyShadowBaker), which PropHunt/LobbyLit reads through the globals set at Start.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHLobbyCull : UdonSharpBehaviour
{
    public float radius = 45f;          // the lobby is drawn while the player is this close to its centre
    public Light sun;                   // its realtime shadows are off while the lobby is shown
    public Texture shadowTex;           // the lobby's baked sun shadow (LobbyShadowBaker) and its projection
    public Vector4 sm0, sm1, sm2, sp;
    LightShadows sunShadows;
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
        if (shadowTex != null)
        {
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_UdonPHLobbyShadow"), shadowTex);
            VRCShader.SetGlobalVector(VRCShader.PropertyToID("_UdonPHLobbySM0"), sm0);
            VRCShader.SetGlobalVector(VRCShader.PropertyToID("_UdonPHLobbySM1"), sm1);
            VRCShader.SetGlobalVector(VRCShader.PropertyToID("_UdonPHLobbySM2"), sm2);
            VRCShader.SetGlobalVector(VRCShader.PropertyToID("_UdonPHLobbySP"), sp);
        }
        // everyone starts in the lobby (shown): the sun's shadows off from the start
        if (sun != null) { sunShadows = sun.shadows; sun.shadows = LightShadows.None; }
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
            if (sun != null) sun.shadows = sunShadows;
        }
        else
        {
            for (int i = 0; i < rs.Length; i++) if (rs[i] != null && was[i]) rs[i].enabled = true;
            for (int i = 0; i < cs.Length; i++) if (cs[i] != null && wasC[i]) cs[i].enabled = true;
            if (sun != null) sun.shadows = LightShadows.None;
        }
    }
}
