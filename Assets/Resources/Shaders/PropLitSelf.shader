// A hider's own prop (PropLitSelf, WorldSceneBuilder; set by MapBuilderUdon.SelfOnly): PropHunt/Lit, drawn only into the
// third-person view (_PipOnly), plus a see-through pass first (Cor: the prop viewer flies free, with an effect to see
// the prop through walls). Where a wall or a ceiling hides the prop from that view, its shape shows as a soft teal
// silhouette, the ground outline's hue, brighter at the rim. Only this material has the pass, so no other prop is
// drawn twice or seen through walls, and no other camera and no other player ever sees it.
Shader "PropHunt/LitSelf"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Toon ("Toon ramp", Range(0, 1)) = 1
        [HideInInspector] _PipOnly ("Third-person view only", Float) = 1
        [HideInInspector] _Hover ("Pointed at", Float) = 0
        _XRay ("Through walls", Color) = (0.30, 0.95, 0.85, 0.45)   // the ground outline's teal (OutlineTeal), about 45% opaque
    }
    SubShader
    {
        // after the walls and floors (Geometry), so the see-through pass tests against them
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }

        // 1. behind something: only where the depth already drawn is nearer than the prop. The stencil bit lets each
        // pixel take the silhouette once, so the prop's own overlapping faces do not stack up darker. The lit pass comes
        // after and covers whatever of the prop is not hidden. (The pip's render texture has a stencil: 24-bit depth.)
        Pass
        {
            Name "XRAY"
            Tags { "LightMode"="ForwardBase" }
            ZTest Greater
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 128 ReadMask 128 WriteMask 128 Comp NotEqual Pass Replace }
            CGPROGRAM
            #pragma vertex vertX
            #pragma fragment fragX
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"

            float _PipOnly;
            fixed4 _XRay;

            struct appdataX
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2fX
            {
                float4 pos : SV_POSITION;
                half3 normal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            v2fX vertX (appdataX v)
            {
                v2fX o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                // as PropLit: in any view but the third-person one (384 pixels wide), off screen
                if (_PipOnly > 0.5 && abs(_ScreenParams.x - 384.0) > 0.5) o.pos = float4(2.0, 2.0, 2.0, 1.0);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 fragX (v2fX i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float rim = 1.0 - saturate(dot(n, normalize(_WorldSpaceCameraPos - i.worldPos)));
                rim *= rim;
                // a flat fill, lighter and more opaque towards the edges so the outline of the shape reads
                float3 c = lerp(_XRay.rgb * 0.75, lerp(_XRay.rgb, float3(1, 1, 1), 0.45), rim);
                float a = saturate(_XRay.a * (0.75 + 0.75 * rim));
                return fixed4(c, a);
            }
            ENDCG
        }

        // 2. the prop itself, exactly as PropHunt/Lit
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
