// One lit shader for every generated prop and for the architecture: vertex colour x tint, a sky/ground ambient
// and the sun. It has no shadow pass, so props cast no shadows (C3, G48).
// The sun comes two ways (D1, "A and C"): the base is the directional light, shadowless, per vertex. Once the map's
// spot sun (a VRC Light Volumes spot far above the map, whose shadows of walls, roofs and fences are baked in-game
// after each map build, PHSun) is ready, PHSun sets _UdonPHSunSpot to 1 and the direct sunlight comes from Light
// Volumes instead, per pixel and shadowed; the ambient stays. Light Volumes' other point lights (lamps) add on top.
// Vertex colours are sRGB bytes baked by prototype/tools/convert_models.py.
Shader "PropHunt/Lit"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Toon ("Toon ramp", Range(0, 1)) = 1   // Cor: the direct light in three flat bands (lit, half, shade)
        // 1 in the material of a hider's own prop (PropLitSelf, MapBuilderUdon.SelfOnly; never by property block): drawn only into the third-person view's texture,
        // which is _PipWidth pixels wide; no other camera shows it (Cor: you could see your own prop)
        [HideInInspector] _PipOnly ("Third-person view only", Float) = 0
        // 1 in the material of the prop a hider points at and could become (PropLitHover, MapBuilderUdon.Hover): a warm glow, strongest at its rim
        [HideInInspector] _Hover ("Pointed at", Float) = 0
        // 1 in the material of a map's props (PropLitFade, MapBuilderUdon.MakeProp; not the architecture or the merged scenery): in the
        // prop viewer's cutout they are drawn see-through instead of cut away (PipCut.cginc)
        [HideInInspector] _Fade ("See-through in the cutout", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "PropLit.cginc"
            ENDCG
        }
    }
}
