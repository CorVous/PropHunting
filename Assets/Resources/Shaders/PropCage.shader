// The hider's own prop as its outline, in first person only (Cor: "the cage plus the outline"; since 2026-11-24
// "outline only", not every feature edge). The hider does not see their own prop in first person (it would fill the
// view), so the ground outline (PHView) shows its footprint and this shows its shape: the model's edges that can be on
// its outline (PropPrefabBaker.MakeCage), each a quad widened in screen space, so a line is the same few pixels wide
// near and far. An edge is drawn only where one of its two faces turns toward the viewpoint and the other away
// (Outline), or where the mesh ends. The viewpoint is the eye pulled back along the view by the model's largest side
// and half a metre: the eye sits at 0.9 of the prop's height, often inside it, where every face is turned away.
// Its core is the outline's teal and its rim a dark teal, unlit, so it reads on light and dark floors alike. It is blended, _Keep its opacity (Cor: the dithered lines
// looked messy), less near the eye (gone inside _FadeNear, full from _FadeFar; MapBuilderUdon.Cage sets both
// by the prop's size), and a faint _KeepBehind where something stands in front of the line. Nothing at or above
// the eye is drawn (_EyeMargin below the camera), nor anything in the third-person view (384 pixels wide, as
// PropLit's _PipOnly, the other way round).
// Mesh: per vertex, position (half) = this end, w = the model's largest side, negative on an edge always drawn;
// TEXCOORD1 (half) = the other end (both in the prop's space), w = the side (-1, +1); TEXCOORD2 (signed bytes) = the
// two faces' normals, octahedron-packed (PropPrefabBaker.Octa).
// Desktop and VR alike (single-pass instanced stereo: per-eye matrices, camera and screen size), no geometry shader.
Shader "PropHunt/Cage"
{
    Properties
    {
        _Core ("Core colour", Color) = (0.30, 0.95, 0.85, 1)
        _Rim ("Rim colour", Color) = (0.02, 0.30, 0.27, 1)
        _RimShare ("Rim share of the width", Range(0, 1)) = 0.4
        _WidthPx ("Width in pixels at 720 lines", Float) = 4
        _Keep ("Share kept at full strength", Range(0, 1)) = 0.85
        _KeepBehind ("Share kept behind things", Range(0, 1)) = 0.2
        _FadeNear ("Gone within (m)", Float) = 0.35
        _FadeFar ("Full from (m)", Float) = 0.8
        _EyeMargin ("Cut this far below the eye (m)", Float) = 0.03
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    fixed4 _Core, _Rim;
    float _RimShare, _WidthPx, _Keep, _KeepBehind, _FadeNear, _FadeFar, _EyeMargin;
    struct appdata
    {
        float4 vertex : POSITION;
        float4 other : TEXCOORD1;   // xyz: the other end; w: the side
        float4 faces : TEXCOORD2;   // xy, zw: the two faces' normals, packed
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
    struct v2f
    {
        float4 pos : SV_POSITION;
        float3 world : TEXCOORD0;
        float side : TEXCOORD1;
        UNITY_VERTEX_OUTPUT_STEREO
    };
    // How small the prop is, 0 to 1, read off the fade MapBuilderUdon.Cage sets by its size: _FadeFar 0.8 m (a prop of
    // 1.5 m and more; also the material's own value) gives 0, which leaves its cage exactly as it was; 0.65 m or less
    // (a chair, or smaller) gives 1.
    float Small() { return saturate((0.8 - _FadeFar) / 0.15); }
    float3 Unocta(float2 f)
    {
        float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
        float t = saturate(-n.z);
        n.x += n.x >= 0.0 ? -t : t;
        n.y += n.y >= 0.0 ? -t : t;
        return normalize(n);
    }
    // is the edge on the outline, seen from the eye pulled back along the view?
    bool Outline(float4 v, float3 wa, float3 wb, float4 faces)
    {
        if (v.w < 0.0) return true;   // the mesh ends here
        float scale = length(unity_ObjectToWorld._m00_m10_m20);
        float3 fwd = -UNITY_MATRIX_V[2].xyz;   // the camera's forward in the world
        float3 to = _WorldSpaceCameraPos - fwd * (v.w * scale + 0.5) - 0.5 * (wa + wb);
        float fa = dot(mul(Unocta(faces.xy), (float3x3)unity_WorldToObject), to);
        float fb = dot(mul(Unocta(faces.zw), (float3x3)unity_WorldToObject), to);
        return (fa > 0.0) != (fb > 0.0);   // one face toward, one away
    }
    float Bayer(float2 px)
    {
        static const float b[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        int2 p = int2(fmod(floor(px), 4.0));
        return (b[p.y * 4 + p.x] + 0.5) / 16.0;
    }
    v2f vert (appdata i)
    {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(i);
        UNITY_INITIALIZE_OUTPUT(v2f, o);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.pos = float4(2.0, 2.0, 2.0, 1.0);   // dropped (outside the view)
        if (abs(_ScreenParams.x - 384.0) < 0.5) return o;   // never in the third-person view
        float3 wa = mul(unity_ObjectToWorld, float4(i.vertex.xyz, 1.0)).xyz;
        float3 wb = mul(unity_ObjectToWorld, float4(i.other.xyz, 1.0)).xyz;
        if (!Outline(i.vertex, wa, wb, i.faces)) return o;
        float3 a = mul(UNITY_MATRIX_V, float4(wa, 1.0)).xyz, b = mul(UNITY_MATRIX_V, float4(wb, 1.0)).xyz;
        a *= 0.993; b *= 0.993;   // a little toward the eye, so edges lying on the floor or a wall are not hidden by it
        float nearZ = -(_ProjectionParams.y + 0.005);
        // keep both ends in front of the near plane, so the direction on screen is sane
        if (a.z > nearZ && b.z > nearZ) return o;
        if (a.z > nearZ) { float t = (nearZ - a.z) / (b.z - a.z); a = lerp(a, b, t); wa = lerp(wa, wb, t); }
        if (b.z > nearZ) b = lerp(b, a, (nearZ - b.z) / (a.z - b.z));
        float4 ca = mul(UNITY_MATRIX_P, float4(a, 1.0)), cb = mul(UNITY_MATRIX_P, float4(b, 1.0));
        float2 sa = ca.xy / ca.w * _ScreenParams.xy, sb = cb.xy / cb.w * _ScreenParams.xy;
        float2 d = sb - sa;
        d = dot(d, d) > 1e-6 ? normalize(d) : float2(1.0, 0.0);
        float2 n = float2(-d.y, d.x);
        // the eye's own height in pixels (per eye in VR); a small prop's lines a little wider (Small)
        float hw = 0.5 * _WidthPx * (1.0 + 0.35 * Small()) * _ScreenParams.y / 720.0;
        float2 off = (n * i.other.w - d) * hw;                    // sideways, and half a width past the end (closed corners)
        ca.xy += off * 2.0 / _ScreenParams.xy * ca.w;          // pixels to clip space
        o.pos = ca;
        o.world = wa;
        o.side = i.other.w;
        return o;
    }
    // the share of pixels kept here: fewer near the eye. On a small prop the eye (at 0.9 of its height) is within a
    // few tens of centimetres of every edge, so by distance alone most of a chair's cage was dithered down to a third
    // (Cor: faint and thin). What lies well below the eye is looked down at, not across the view, so on a small prop
    // a point's drop under the eye counts as distance too: the seat and legs stand at full strength, while what is
    // level with the eye (the top of a chair's back) still fades away.
    float Keep(v2f i, float share)
    {
        float dist = distance(i.world, _WorldSpaceCameraPos);
        dist += Small() * max(0.0, _WorldSpaceCameraPos.y - i.world.y);
        return share * saturate((dist - _FadeNear) / max(1e-3, _FadeFar - _FadeNear));
    }
    fixed4 Shade(v2f i) { return abs(i.side) > 1.0 - _RimShare ? _Rim : _Core; }
    ENDCG
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "DisableBatching"="True" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="Always" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(_WorldSpaceCameraPos.y - _EyeMargin - i.world.y);
                fixed4 c = Shade(i);
                c.a = Keep(i, _Keep);   // a soft fade, no dither (Cor: the wireframe looked messy)
                return c;
            }
            ENDCG
        }
        // where the line lies behind something (a wall, a table the prop is pushed into): a sparse dither, so it still shows faintly
        Pass
        {
            Tags { "LightMode"="Always" }
            ZTest Greater ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(_WorldSpaceCameraPos.y - _EyeMargin - i.world.y);
                fixed4 c = Shade(i);
                c.a = Keep(i, _KeepBehind) * 0.6;   // faint behind things
                return c;
            }
            ENDCG
        }
    }
}
