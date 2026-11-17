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
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            float _UdonPHSunSpot;   // 1 once the spot sun's shadows are baked (PHSun)
            float4 _UdonPHSunArea;  // the spot's footprint: centre x, z, then the radii where it hands back to the base

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 albedo : TEXCOORD0;
                half3 ambient : TEXCOORD1;   // sky/ground ambient, already x albedo
                half3 sun : TEXCOORD2;       // the base's shadowless sun, already x albedo
                float3 worldPos : TEXCOORD3;
                half3 normal : TEXCOORD4;
                UNITY_FOG_COORDS(5)
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = UnityObjectToWorldNormal(v.normal);
                float d = dot(n, _WorldSpaceLightPos0.xyz);
                float ndl = max(0, d) * 0.8 + (d * 0.5 + 0.5) * 0.2;
                float up = n.y * 0.5 + 0.5;
                float3 amb = lerp(unity_AmbientGround.rgb, unity_AmbientSky.rgb, up);
                float3 albedo = v.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                albedo = GammaToLinearSpace(albedo);
                #endif
                fixed4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                albedo *= tint.rgb;
                o.albedo = albedo;
                // the wrap part of the base sun (the 0.2) stays as fill when the spot sun takes over the direct part
                o.ambient = albedo * (amb + _LightColor0.rgb * (d * 0.5 + 0.5) * 0.2);
                o.sun = albedo * _LightColor0.rgb * max(0, d) * 0.8;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = n;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 c;
                if (_UdonPHSunSpot > 0.5)
                {
                    float3 n = normalize(i.normal);
                    float3 L0, L1r, L1g, L1b;
                    LightVolumeAdditiveSH(i.worldPos, L0, L1r, L1g, L1b, 0, n);
                    float3 lv = i.albedo * max(0, LightVolumeEvaluate(n, L0, L1r, L1g, L1b));
                    float r = length(i.worldPos.xz - _UdonPHSunArea.xy);
                    c = i.ambient + lerp(lv, i.sun, smoothstep(_UdonPHSunArea.z, _UdonPHSunArea.w, r));
                }
                else c = i.ambient + i.sun;
                fixed4 col = fixed4(c, 1);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
