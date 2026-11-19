// The prop viewer's cutout (Cor: "a stylized cutout in the geometry so you can see the prop in full, along with the
// things around it"). Only in the third-person view (the one 384 pixels wide, as _PipOnly), whatever lies between that
// camera and the hider's own prop is cut away: a hole along the line from the camera to the prop, a little wider near
// the camera, made of bubbles fixed in the world (Cor: "a bit more of a bubbly/fractal hole instead of just a porthole",
// then bubbles that swell in place rather than a cutout dragged around) and a thin rim in the ground outline's teal.
// A prop in the way (PropLitFade, _Fade) is not cut but drawn see-through, a screen-door
// dither over the same shape (Cor: "fade nearby props instead of cutting them").
// PHView sets the globals every frame while hiding (radius 0 otherwise). Not cut: the hider's own prop (callers skip
// _PipOnly materials), anything at or behind the prop, and anything at or below the floor it stands on (the ground
// around it stays whole). In every other view the cost is one uniform branch.
#ifndef PROPHUNT_PIPCUT_INCLUDED
#define PROPHUNT_PIPCUT_INCLUDED

float4 _UdonPHCut;        // xyz: the own prop's centre; w: the hole's radius there (0: no hole)
float4 _UdonPHCutFloor;   // x: the height of the floor the prop stands on

// The hole's shape (Cor: "set location of bubbles expanding and overlapping with a noise texture", so it does not look
// like a cutout dragged around): bubbles sit at fixed places in the WORLD, one in each cell of a jittered 3D grid. A
// bubble grows while its centre is well inside the hole's region (near the camera-to-prop line) and shrinks away as the
// region leaves it, so moving makes bubbles swell and pop where they are. A plain core around the line keeps the middle
// open; a little world-anchored noise roughens the edge. Distances are in metres, so the rim is as thick everywhere.
// The 2x2x2 cells around a pixel are enough while a bubble's reach (jitter + radius) stays under 1.5 cells.
#define PIPCUT_CELL 0.45      // the grid's cell, metres
#define PIPCUT_JITTER 0.5     // how far a bubble strays in its cell (0.5: a quarter cell either way)
#define PIPCUT_RMAX 0.32      // the biggest bubble's radius, metres (under 0.75 of a cell)
#define PIPCUT_RFRAC 0.6      // ...and never more than this share of the hole's radius (they grow in as it opens)
#define PIPCUT_CORE 0.55      // the open core, as a share of the hole's radius
#define PIPCUT_NOISE 0.03     // the edge's roughness, metres
#define PIPCUT_RIM 0.035      // the teal rim's width, metres

// hashes of a cell (Dave Hoskins' hash without sine): three jitters and a fourth for the size
float4 PipCutHash4(float3 p)
{
    float4 q = frac(float4(p.xyzx) * float4(0.1031, 0.1030, 0.0973, 0.1099));
    q += dot(q, q.wzxy + 33.33);
    return frac((q.xxyz + q.yzzw) * q.zywx);
}

// value noise, 0..1, of a world position
float PipCutNoise(float3 p)
{
    float3 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = PipCutHash4(i).x, n100 = PipCutHash4(i + float3(1, 0, 0)).x;
    float n010 = PipCutHash4(i + float3(0, 1, 0)).x, n110 = PipCutHash4(i + float3(1, 1, 0)).x;
    float n001 = PipCutHash4(i + float3(0, 0, 1)).x, n101 = PipCutHash4(i + float3(1, 0, 1)).x;
    float n011 = PipCutHash4(i + float3(0, 1, 1)).x, n111 = PipCutHash4(i + float3(1, 1, 1)).x;
    return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
}

// the hole's radius at depth t along the line (a little wider near the camera)
float PipCutRadius(float t, float L) { return _UdonPHCut.w * lerp(1.3, 1.0, saturate(t / L)); }

// signed distance (metres) to the hole's edge at world position wp: negative inside the hole
float PipCutShape(float3 wp, float3 u, float L)
{
    float3 v = wp - _WorldSpaceCameraPos;
    float t = dot(v, u);
    float R = PipCutRadius(t, L);
    float sd = length(v - u * t) - PIPCUT_CORE * R;   // the open core
    float3 base = floor(wp / PIPCUT_CELL - 0.5);
    float rmax = min(PIPCUT_RMAX, PIPCUT_RFRAC * _UdonPHCut.w);
    for (int k = 0; k < 8; k++)
    {
        float3 cell = base + float3(k & 1, (k >> 1) & 1, (k >> 2) & 1);
        float4 h = PipCutHash4(cell);
        float3 c = (cell + 0.5 + (h.xyz - 0.5) * PIPCUT_JITTER) * PIPCUT_CELL;
        float3 vc = c - _WorldSpaceCameraPos;
        float tc = dot(vc, u);
        float dc = length(vc - u * tc) / max(PipCutRadius(tc, L), 1e-4);
        // full size well inside the region, nothing outside it; a slow, gentle breathing
        float r = rmax * lerp(0.6, 1.0, h.w) * smoothstep(1.1, 0.55, dc) * (1.0 + 0.05 * sin(_Time.y * 0.8 + h.w * 6.2831853));
        r *= step(0.0, tc);   // none behind the camera
        sd = min(sd, length(wp - c) - r);
    }
    // roughness, fading in as the hole opens (nothing left at radius 0)
    sd += (PipCutNoise(wp * 6.0) - 0.5) * 2.0 * PIPCUT_NOISE * saturate(_UdonPHCut.w * 2.0);
    return sd;
}

// Where the architecture's surface at wp (world normal n) reads the hole's shape. A wall is layers one behind the
// other along the view: the facade's kit part in front (up to 0.3 m proud), the wall's own box face behind it (and a
// door's trim 2 cm proud). Each layer cut by the shape at its own point showed the rim twice (Cor: a second outline
// on the wall behind): where the hole is wider at the front layer the back layer's rim band showed through it. An
// upright surface near a wall's line on the map's grid (walls stand on the cells' edges, PIPCUT_GRID apart, to
// PIPCUT_LAYERS either side) reads the shape where its view ray crosses that line instead, so every layer of one wall
// is cut by one outline and its front layer's rim covers the rest. Toward a grazing view (the ray along the wall)
// it eases back to its own point, which keeps the shift short and the shape continuous.
#define PIPCUT_GRID 4.0       // MapBuilderUdon.CELL
#define PIPCUT_LAYERS 0.45    // a wall's half thickness (0.1) and the deepest facade (0.3), and a little
float3 PipCutLayerPoint(float3 wp, float3 n)
{
    float3 vd = normalize(wp - _WorldSpaceCameraPos);
    float3 an = abs(n);
    if (an.x > 0.9)
    {
        float dx = round(wp.x / PIPCUT_GRID) * PIPCUT_GRID - wp.x;
        if (abs(dx) < PIPCUT_LAYERS && abs(vd.x) > 0.05) return wp + vd * (dx / vd.x) * saturate((abs(vd.x) - 0.25) / 0.2);
    }
    else if (an.z > 0.9)
    {
        float dz = round(wp.z / PIPCUT_GRID) * PIPCUT_GRID - wp.z;
        if (abs(dz) < PIPCUT_LAYERS && abs(vd.z) > 0.05) return wp + vd * (dz / vd.z) * saturate((abs(vd.z) - 0.25) / 0.2);
    }
    return wp;
}

// what lies in the hole: architecture (fade 0) is clipped and returns 1 in the rim band (draw it in PipCutRim());
// a prop (fade 1) keeps three pixels in eight there, in a fixed 4x4 pattern, growing to all of them across the rim.
// n: the surface's world normal (PipCutLayerPoint, architecture only); (0, 0, 0) where there is none.
// (Call it inside an if, never ?: or &&: its clip() must not run for what skips it.)
half PipCutN(float3 wp, float3 n, float2 px, float fade)
{
    if (_UdonPHCut.w <= 0.0 || abs(_ScreenParams.x - 384.0) > 0.5) return 0;
    if (wp.y < _UdonPHCutFloor.x + 0.05) return 0;
    float3 d = _UdonPHCut.xyz - _WorldSpaceCameraPos;
    float L = length(d);
    float3 u = d / max(L, 1e-4);
    float3 v = wp - _WorldSpaceCameraPos;
    float t = dot(v, u);
    if (t > L - 0.1) return 0;
    // past the farthest a bubble reaches: the region's edge (1.1 R) and the biggest bubble, and the roughness
    float far = 1.1 * PipCutRadius(t, L) + PIPCUT_RMAX + PIPCUT_NOISE;
    float3 off = v - u * t;
    if (dot(off, off) >= far * far) return 0;
    float s = PipCutShape(fade > 0.5 ? wp : PipCutLayerPoint(wp, n), u, L);
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

half PipCut(float3 wp, float2 px, float fade) { return PipCutN(wp, float3(0, 0, 0), px, fade); }

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
