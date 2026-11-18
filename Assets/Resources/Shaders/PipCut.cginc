// The prop viewer's cutout (Cor: "a stylized cutout in the geometry so you can see the prop in full, along with the
// things around it"). Only in the third-person view (the one 384 pixels wide, as _PipOnly), whatever lies between that
// camera and the hider's own prop is cut away: a round hole along the line from the camera to the prop, a little wider
// near the camera, edged with a hatched band and a thin rim in the ground outline's teal so it reads as a porthole.
// PHView sets the globals every frame while hiding (radius 0 otherwise). Not cut: the hider's own prop (callers skip
// _PipOnly materials), anything at or behind the prop, and anything at or below the floor it stands on (the ground
// around it stays whole). In every other view the cost is one uniform branch.
#ifndef PROPHUNT_PIPCUT_INCLUDED
#define PROPHUNT_PIPCUT_INCLUDED

float4 _UdonPHCut;        // xyz: the own prop's centre; w: the hole's radius there (0: no hole)
float4 _UdonPHCutFloor;   // x: the height of the floor the prop stands on

// clips what lies in the hole; returns 1 in the rim band (draw it in PipCutRim()), 0 elsewhere
half PipCut(float3 wp, float2 px)
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
    float f = length(v - u * t) / R;
    if (f >= 1.0) return 0;
    // the hole, then a hatched band (diagonal screen lines, two pixels in four), then a solid rim
    float hatch = frac((px.x + px.y) * 0.25);
    clip(f < 0.84 || (f < 0.92 && hatch >= 0.5) ? -1.0 : 1.0);
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
