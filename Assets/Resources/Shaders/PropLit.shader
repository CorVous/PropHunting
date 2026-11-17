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
            float4 _UdonPHSunArea;  // the map's rectangle (centre x, z, half width, half depth): the spot sun inside, the base sun 6 m past it

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
                half emit : TEXCOORD6;       // 1 for a lamp's bulb (vertex alpha 0): drawn at full brightness
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
                o.emit = 1 - v.color.a;
                // the wrap part of the base sun (the 0.2) stays as fill when the spot sun takes over the direct part
                o.ambient = albedo * (amb + _LightColor0.rgb * (d * 0.5 + 0.5) * 0.2);
                o.sun = albedo * _LightColor0.rgb * max(0, d) * 0.8;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = n;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // inside one of the scene's baked (regular) Light Volumes, e.g. a Bakery-baked lobby
            bool InBakedVolume(float3 worldPos)
            {
                #ifndef VRCLV_DISABLE_REGULAR_VOLUMES
                uint volumesCount = min((uint) _UdonLightVolumeCount, VRCLV_MAX_VOLUMES_COUNT);
                uint additiveCount = min((uint) _UdonLightVolumeAdditiveCount, volumesCount);
                for (uint id = additiveCount; id < volumesCount; id++)
                    if (LV_PointLocalAABB(LV_LocalFromVolume(id, worldPos))) return true;
                #endif
                return false;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 c;
                float3 n = normalize(i.normal);
                bool lvOn = LightVolumesEnabled() > 0;
                // additive volumes and point light volumes: the lamps (MapBuilderUdon's light points), and the spot sun
                // once PHSun has baked it
                float3 add = 0;
                if (lvOn)
                {
                    float3 A0, A1r, A1g, A1b;
                    LightVolumeAdditiveSH(i.worldPos, A0, A1r, A1g, A1b, 0, n);
                    add = i.albedo * max(0, LightVolumeEvaluate(n, A0, A1r, A1g, A1b));
                    add = min(add, i.albedo * 1.3);   // never blown out next to a lamp (a hot spot reads as shine)
                }
                if (lvOn && InBakedVolume(i.worldPos))
                {
                    // a baked volume carries its own sky, sun and bounce: it replaces the ambient and the base sun
                    float3 L0 = 0, L1r = 0, L1g = 0, L1b = 0;
                    LV_LightVolumeRegularSH(i.worldPos, L0, L1r, L1g, L1b);
                    c = i.albedo * max(0, LightVolumeEvaluate(n, L0, L1r, L1g, L1b)) + add;
                }
                else if (_UdonPHSunSpot > 0.5)
                {
                    // the spot sun (with the lamps) over the map, handing back to the base sun at the spot's edge
                    float2 o = abs(i.worldPos.xz - _UdonPHSunArea.xy) - _UdonPHSunArea.zw;
                    float r = max(o.x, o.y);   // metres past the map's edge
                    c = i.ambient + lerp(add, i.sun, smoothstep(0.5, 6.0, r));   // the base sun from 6 m past the fence
                }
                else c = i.ambient + i.sun + add;   // before the spot is baked: the base sun, the lamps on top
                c = lerp(c, i.albedo * 1.4, i.emit);
                fixed4 col = fixed4(c, 1);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
