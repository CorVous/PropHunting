// The prop viewer's cutout (Cor: "a stylized cutout in the geometry so you can see the prop in full, along with the
// things around it"). Only in the third-person view (the one 384 pixels wide, as _PipOnly), whatever lies between that
// camera and the hider's own prop is cut away: a hole along the line from the camera to the prop, a little wider near
// the camera, with a bubbly edge (Cor: "a bit more of a bubbly/fractal hole instead of just a porthole") and a thin rim
// in the ground outline's teal. A prop in the way (PropLitFade, _Fade) is not cut but drawn see-through, a screen-door
// dither over the same shape (Cor: "fade nearby props instead of cutting them").
// PHView sets the globals every frame while hiding (radius 0 otherwise). Not cut: the hider's own prop (callers skip
// _PipOnly materials), anything at or behind the prop, and anything at or below the floor it stands on (the ground
// around it stays whole). In every other view the cost is one uniform branch.
#ifndef PROPHUNT_PIPCUT_INCLUDED
#define PROPHUNT_PIPCUT_INCLUDED

float4 _UdonPHCut;        // xyz: the own prop's centre; w: the hole's radius there (0: no hole)
float4 _UdonPHCutFloor;   // x: the height of the floor the prop stands on

// The hole's shape, in units of its radius, in the plane across the camera-to-prop line (so it neither slides nor
// swims as the camera or the prop moves): a core circle, a ring of big lobes on its edge, and smaller bubbles on
// those. Lobe k of a ring of n sits at angle k/n of a turn, its size from a hash of k; only the two lobes either side
// of the pixel's angle are tested. Tuning: PIPCUT_CORE (the core circle; the centre is always clear to about its
// value minus the rim), the counts n and the distance/radius pairs in PipCutShape.
#define PIPCUT_CORE 0.85
#define PIPCUT_RIM 0.07

float PipCutHash(float k) { return frac(sin(k * 12.9898 + 4.1) * 43758.5453); }

// the signed distance to one ring of lobes: centres at dist + jitter * h, radii rad + grow * h' (h, h' per lobe)
float PipCutRing(float2 q, float ang, float n, float dist, float jitter, float rad, float grow, float seed)
{
    float s = ang * (n / 6.2831853);
    float k0 = floor(s);
    float best = 1e4;
    for (int j = 0; j < 2; j++)
    {
        float k = k0 + j;
        float kw = k - n * floor(k / n);   // the lobe's own number, the same either side of the turn's seam
        float h = PipCutHash(kw + seed), h2 = PipCutHash(kw + seed + 31.7);
        float a = k * (6.2831853 / n);
        // a calm wobble: each lobe breathes a few percent, slowly
        float r = (rad + grow * h2) * (1.0 + 0.04 * sin(_Time.y * 0.7 + kw * 1.9 + seed));
        best = min(best, length(q - float2(cos(a), sin(a)) * (dist + jitter * h)) - r);
    }
    return best;
}

float PipCutShape(float2 q)
{
    float ang = atan2(q.y, q.x);
    float d = length(q) - PIPCUT_CORE;
    d = min(d, PipCutRing(q, ang, 7.0, 0.80, 0.05, 0.19, 0.08, 0.0));     // big lobes, out to about 1.12
    d = min(d, PipCutRing(q, ang, 17.0, 0.87, 0.08, 0.09, 0.06, 5.3));   // small bubbles on them, out to about 1.1
    return d;
}

// what lies in the hole: architecture (fade 0) is clipped and returns 1 in the rim band (draw it in PipCutRim());
// a prop (fade 1) keeps three pixels in eight there, in a fixed 4x4 pattern, growing to all of them across the rim
half PipCut(float3 wp, float2 px, float fade)
{
    if (_UdonPHCut.w <= 0.0 || abs(_ScreenParams.x - 384.0) > 0.5) return 0;
    if (wp.y < _UdonPHCutFloor.x + 0.05) return 0;
    float3 d = _UdonPHCut.xyz - _WorldSpaceCameraPos;
    float L = length(d);
    float3 u = d / max(L, 1e-4);
    float3 v = wp - _WorldSpaceCameraPos;
    float t = dot(v, u);
    if (t > L - 0.1) return 0;
    float R = _UdonPHCut.w * lerp(1.3, 1.0, saturate(t / L));
    float3 w = (v - u * t) / R;
    if (dot(w, w) >= 1.3) return 0;   // past the farthest lobe (about 1.12)
    // the hole's own frame: across the line, level and up
    float3 e1 = cross(float3(0, 1, 0), u);
    e1 = dot(e1, e1) > 1e-4 ? normalize(e1) : float3(1, 0, 0);
    float3 e2 = cross(u, e1);
    float s = PipCutShape(float2(dot(w, e1), dot(w, e2)));
    if (s >= 0.0) return 0;
    if (fade > 0.5)
    {
        static const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        uint2 p = uint2(px) & 3;
        float keep = lerp(0.375, 1.0, saturate(1.0 + s / PIPCUT_RIM));
        clip(keep - (bayer[p.y * 4 + p.x] + 0.5) / 16.0);
        return 0;
    }
    clip(s < -PIPCUT_RIM ? -1.0 : 1.0);
    return 1;
}

// the rim's colour: the ground outline's teal (WorldSceneBuilder OutlineTeal), as the framebuffer wants it
float3 PipCutRim()
{
    float3 c = float3(0.30, 0.95, 0.85);
    #ifndef UNITY_COLORSPACE_GAMMA
    c = GammaToLinearSpace(c);
    #endif
    return c;
}

#endif
