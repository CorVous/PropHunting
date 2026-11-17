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
            float4 _UdonPHSunArea;
            float _Toon;

            // the toon ramp: three flat bands with short soft edges; above full light (a bright lamp) it stays linear
            float Ramp(float x)
            {
                return 0.5 * smoothstep(0.08, 0.14, x) + 0.5 * smoothstep(0.45, 0.52, x) + max(x - 1.0, 0.0);
            }

            // the direct light, quantised by its strength against the sun's full light on this surface
            float3 Toon(float3 direct, float3 albedo)
            {
                float ref = dot(albedo * _LightColor0.rgb * 0.8, float3(0.3, 0.59, 0.11)) + 1e-4;
                float f = dot(direct, float3(0.3, 0.59, 0.11)) / ref;
                return lerp(direct, direct * (Ramp(f) / max(f, 1e-4)), _Toon);
            }  // the map's rectangle (centre x, z, half width, half depth): the spot sun inside, the base sun 6 m past it

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

            // every Point Light Volume reaching this pixel, split in two: spot lights (the spot sun) and the rest (the lamps).
            // Our own loop, not LightVolumeAdditiveSH's: that one stops after AdditiveMaxOverdraw lights, so where several
            // lamp pools overlap, the extra lamps drop out with a hard edge (Cor: the overlaps looked wrong).
            void PointLights(float3 worldPos, float3 n, out float3 sun, out float3 lamps)
            {
                float3 S0 = 0, S1r = 0, S1g = 0, S1b = 0;
                float3 P0 = 0, P1r = 0, P1g = 0, P1b = 0;
                #if VRCLV_LIGHTS_SUPPORTED
                uint count = min((uint) _UdonPointLightVolumeCount, VRCLV_MAX_LIGHTS_COUNT);
                float3 shadingNormal = n * 1.5;   // the package's default point light shading (3), as LightVolumeAdditiveSH
                for (uint id = 0; id < count; id++)
                {
                    if (_UdonPointLightVolumePosition[id].w < 0) LV_AccumulatePointLightVolumeSH(id, worldPos, shadingNormal, 0.5, S0, S1r, S1g, S1b);
                    else LV_AccumulatePointLightVolumeSH(id, worldPos, shadingNormal, 0.5, P0, P1r, P1g, P1b);
                }
                #endif
                // additive volumes (none on a map yet) count with the lamps
                LV_LightVolumeAdditiveSH(worldPos, P0, P1r, P1g, P1b);
                sun = max(0, LightVolumeEvaluate(n, S0, S1r, S1g, S1b));
                lamps = max(0, LightVolumeEvaluate(n, P0, P1r, P1g, P1b));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 c;
                float3 n = normalize(i.normal);
                bool lvOn = LightVolumesEnabled() > 0;
                // the spot sun (once PHSun has baked it) and the lamps (MapBuilderUdon's light points)
                float3 spot = 0, lamps = 0;
                if (lvOn)
                {
                    PointLights(i.worldPos, n, spot, lamps);
                    spot *= i.albedo;
                    // the lamps stay out of the toon bands: summed pools would cut each other into rings. Soft light,
                    // never blown out next to a lamp (a hot spot reads as shine)
                    lamps = min(i.albedo * lamps, i.albedo * 1.3);
                }
                if (lvOn && InBakedVolume(i.worldPos))
                {
                    // a baked volume carries its own sky, sun and bounce: it replaces the ambient and the base sun
                    float3 L0 = 0, L1r = 0, L1g = 0, L1b = 0;
                    LV_LightVolumeRegularSH(i.worldPos, L0, L1r, L1g, L1b);
                    c = i.albedo * max(0, LightVolumeEvaluate(n, L0, L1r, L1g, L1b)) + spot + lamps;
                }
                else if (_UdonPHSunSpot > 0.5)
                {
                    // the spot sun over the map, handing back to the base sun at the spot's edge
                    float2 o = abs(i.worldPos.xz - _UdonPHSunArea.xy) - _UdonPHSunArea.zw;
                    float r = max(o.x, o.y);   // metres past the map's edge
                    c = i.ambient + Toon(lerp(spot, i.sun, smoothstep(0.5, 6.0, r)), i.albedo) + lamps;   // the base sun from 6 m past the fence
                }
                else c = i.ambient + Toon(i.sun, i.albedo) + lamps;   // before the spot is baked: the base sun, the lamps on top
                c = lerp(c, i.albedo * 1.4, i.emit);
                fixed4 col = fixed4(c, 1);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
