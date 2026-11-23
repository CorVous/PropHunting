// The replay's taunt pulse (PHReplay, Cor 2026-11-22: "hiders emit a dithered pulse around them when they taunt"):
// a shell drawn as a see-through screen of dots, thickest at its rim so it reads as a ring from any side, thinning out
// as _Color.a falls to 0. Opaque (no blending): the dots are whole pixels, cut by a 4x4 ordered pattern.
Shader "PropHunt/DitherPulse"
{
    Properties
    {
        _Color ("Colour (a: how full)", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float rim : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 view = normalize(WorldSpaceViewDir(v.vertex));
                o.rim = 1.0 - abs(dot(n, view));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 4x4 Bayer thresholds
                int2 p = int2(i.pos.xy) & 3;
                const float b[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                float th = (b[p.y * 4 + p.x] + 0.5) / 16.0;
                float fill = _Color.a * lerp(0.25, 1.0, saturate(i.rim * 1.4));
                if (fill <= th) discard;
                return fixed4(_Color.rgb, 1);
            }
            ENDCG
        }
    }
}
