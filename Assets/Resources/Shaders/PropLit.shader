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
        // 1 on a hider's own prop (PHSlot, by property block): drawn only into the third-person view's texture,
        // which is _PipWidth pixels wide; no other camera shows it (Cor: you could see your own prop)
        [HideInInspector] _PipOnly ("Third-person view only", Float) = 0
        // 1 on the prop a hider points at and could become (PHLocal, by property block): a warm glow, strongest at its rim
        [HideInInspector] _Hover ("Pointed at", Float) = 0
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
                // per instance, as the tint: props of one model are drawn in one instanced batch, and a value that
                // is not per instance is taken from one of them for all (Cor: as a hider, the props near you vanished:
                // they were drawn with the hider's own prop and took its _PipOnly)
                UNITY_DEFINE_INSTANCED_PROP(float, _PipOnly)
                UNITY_DEFINE_INSTANCED_PROP(float, _Hover)
            UNITY_INSTANCING_BUFFER_END(Props)

            // Indoors (Cor: a separate, dimmer indoor light): MapBuilderUdon draws the buildings' footprints into a small
            // texture, one texel a cell, its red the height of the building's top in steps of 5 cm. Under a roof the
            // sky's ambient is dimmer and warmer and the lamps count for more.
            sampler2D _UdonPHIndoor;
            float4 _UdonPHIndoorArea;   // x, y: 1 / the texture's size in metres; z: 1 while a map is up
            float _UdonPHSunSpot;   // 1 once the spot sun's shadows are baked (PHSun)
            float4 _UdonPHSunArea;
            float _Toon;

            // the toon ramp: three flat bands with short soft edges; above full light (a bright lamp) it stays linear
            float Ramp(float x)
            {
                // the edges are wide enough to hide the shadow map's pixels (Cor: the shadows looked jagged); faces are flat
                // shaded, so the bands still read as flat on the models
                return 0.5 * smoothstep(0.03, 0.22, x) + 0.5 * smoothstep(0.34, 0.66, x) + max(x - 1.0, 0.0);
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
                half hover : TEXCOORD7;      // 1 on the prop a hider points at
                half3 emit : TEXCOORD6;      // x: 1 for a lamp's bulb (vertex alpha 0): drawn at full brightness; y: 1 indoors; z: the roof's height there (0 outdoors)
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                // a hider's own prop, in any view but the third-person one (384 pixels wide, WorldSceneBuilder): off screen
                if (UNITY_ACCESS_INSTANCED_PROP(Props, _PipOnly) > 0.5 && abs(_ScreenParams.x - 384.0) > 0.5) o.pos = float4(2.0, 2.0, 2.0, 1.0);
                o.hover = UNITY_ACCESS_INSTANCED_PROP(Props, _Hover);
                float3 n = UnityObjectToWorldNormal(v.normal);
                float d = dot(n, _WorldSpaceLightPos0.xyz);
                float ndl = max(0, d) * 0.8 + (d * 0.5 + 0.5) * 0.2;
                // three-colour ambient: the sky from above, the horizon from the sides, the ground below; a face turned
                // down (a ceiling) takes half the horizon, so ceilings are not the ground's brown (review 7)
                float3 amb = n.y >= 0 ? lerp(unity_AmbientEquator.rgb, unity_AmbientSky.rgb, n.y)
                                      : lerp(unity_AmbientEquator.rgb, unity_AmbientGround.rgb, -n.y * 0.5);
                float3 albedo = v.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                albedo = GammaToLinearSpace(albedo);
                #endif
                fixed4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                // alpha 0.5 marks a foliage tint (MapBuilderUdon.TintProp): only on what is green in the model, so a
                // tree's trunk keeps its wood (it turned orange under the leaves' tint)
                float leaf = tint.a < 0.75 ? step(v.color.r + 0.078, v.color.g) : 1.0;
                albedo *= lerp(float3(1.0, 1.0, 1.0), tint.rgb, leaf);
                // grounding (Cor: more cohesion): an upright surface is a little darker at its foot, where it meets
                // what it stands on. In the mesh's own space: a prop's base, a wall's foot.
                albedo *= lerp(0.74, 1.0, saturate(v.vertex.y * 2.2 + max(n.y, 0.0) * 2.0));
                o.albedo = albedo;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float indoor = 0.0, roofTop = 0.0;
                if (_UdonPHIndoorArea.z > 0.5)
                {
                    float2 uv = o.worldPos.xz * _UdonPHIndoorArea.xy;
                    if (uv.x > 0.0 && uv.y > 0.0 && uv.x < 1.0 && uv.y < 1.0)
                    {
                        float top = tex2Dlod(_UdonPHIndoor, float4(uv, 0, 0)).r * 12.75;
                        indoor = step(0.02, top) * step(o.worldPos.y, top - 0.02);
                        roofTop = top * indoor;
                    }
                }
                o.emit = half3(1 - v.color.a, indoor, roofTop);
                // indoors the sky reaches less far and what there is has bounced off walls and floors: dimmer, warmer
                amb *= lerp(float3(1.0, 1.0, 1.0), float3(0.66, 0.60, 0.54), indoor);
                // the wrap part of the base sun (the 0.2) stays as fill when the spot sun takes over the direct part
                o.ambient = albedo * (amb + _LightColor0.rgb * (d * 0.5 + 0.5) * 0.2 * (1.0 - 0.6 * indoor));
                o.sun = albedo * _LightColor0.rgb * max(0, d) * 0.8;
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
                // every light, not Light Volumes' clusters: those are built for the player's camera, and any other camera
                // (the hider's third-person view) saw lamps and the sun cut off in blocks (tried while profiling)
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
                    // under the roof's edge the shadow map lets a ragged strip of sun in along the top of the walls
                    // (Cor: jagged shadows): no sun in the top half metre of a building's top storey
                    if (i.emit.z > 0.01) spot *= saturate((i.emit.z - 0.5 - i.worldPos.y) * 4.0);
                    spot *= i.albedo;
                    // the lamps stay out of the toon bands: summed pools would cut each other into rings. Soft light,
                    // never blown out next to a lamp (a hot spot reads as shine)
                    lamps = min(i.albedo * lamps * (1.0 + 0.3 * i.emit.y), i.albedo * 1.3);   // indoors the lamps do more of the work
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
                    c = i.ambient + Toon(lerp(spot, i.sun, smoothstep(0.5, 10.0, r)), i.albedo) + lamps;   // the base sun from 10 m past the fence, feathered (review 2)
                }
                else c = i.ambient + Toon(i.sun, i.albedo) + lamps;   // before the spot is baked: the base sun, the lamps on top
                c = lerp(c, i.albedo * 1.4, i.emit.x);
                if (i.hover > 0.5)
                {
                    float rim = 1.0 - saturate(dot(n, normalize(_WorldSpaceCameraPos - i.worldPos)));
                    float pulse = 0.85 + 0.15 * sin(_Time.y * 6.0);
                    c = lerp(c, float3(1.0, 0.86, 0.35), (0.28 + 0.6 * rim * rim) * pulse);
                }
                fixed4 col = fixed4(c, 1);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
