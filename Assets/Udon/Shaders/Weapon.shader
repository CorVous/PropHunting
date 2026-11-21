// A held weapon or selector (PHWeapons): vertex colour, a sky/ground ambient and the sun in the props' toon bands.
// Wear (Cor: "like a mouse nibbled along the entire weapon, leaving a stick near the end"): a melee weapon carries a
// small 3D texture (Tools/bake_wear.py, WeaponBuilder) holding, for each bit of it, the wear at which it is eaten or
// comes loose (r x 255/254; 1 and over = never: the grip). Where the model's surface is already eaten at the current
// _Wear, the view ray is followed into the texture to where the weapon is still whole, and that point is drawn (a
// solid gnawed face, its depth written), or nothing if the ray passes right through. _Wear 0 (or no texture) draws the
// model as it is. Set per weapon by property block: _WearTex, _WearLo (the grid's corner, model space; w = its voxel size), _WearInv
// (1 / the grid's size in metres), _Wear.
// Cor's drawn weapons (Tools/make_card_weapons.py): flat cards whose vertices are white with alpha 0; they draw
// _CardTex (the atlas) at their uv, cut out along its alpha, and wear by _CardWear (the same layout: the wear at which
// each bit goes, 255 never) instead of the grid.
Shader "PropHunt/Weapon"
{
    Properties
    {
        _Wear ("Wear", Range(0, 1)) = 0
        _WearTex ("Wear grid", 3D) = "white" {}
        _WearLo ("Grid corner", Vector) = (0,0,0,0)
        _WearInv ("1 / grid size", Vector) = (1,1,1,0)
        _Fresh ("Gnawed face tint", Color) = (0.82, 0.78, 0.72, 1)
        _Dither ("Dithered (growing back)", Float) = 0
        _CardTex ("Drawn weapons (atlas)", 2D) = "white" {}
        _CardWear ("Drawn weapons' wear", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            sampler3D _WearTex;
            float _Wear;
            float4 _WearLo, _WearInv;
            fixed4 _Fresh;
            float _Dither;
            sampler2D _CardTex, _CardWear;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 obj : TEXCOORD0;
                float3 objN : TEXCOORD1;
                half3 albedo : TEXCOORD2;
                float3 card : TEXCOORD4;   // xy the atlas uv, z 1 on a drawn weapon
                UNITY_FOG_COORDS(3)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.obj = v.vertex.xyz;
                o.objN = v.normal;
                float3 a = v.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                a = GammaToLinearSpace(a);
                #endif
                o.albedo = a;
                o.card = float3(v.uv, v.color.a < 0.5 ? 1 : 0);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // the wear at which this point goes (over 1: never)
            float Gone(float3 p)
            {
                float3 uvw = (p - _WearLo.xyz) * _WearInv.xyz;
                if (any(uvw < 0.0) || any(uvw > 1.0)) return 0.0;
                return tex3Dlod(_WearTex, float4(uvw, 0)).r * (255.0 / 254.0);
            }

            float Ramp(float x) { return 0.5 * smoothstep(0.03, 0.22, x) + 0.5 * smoothstep(0.34, 0.66, x); }

            float3 Light(float3 albedo, float3 n)
            {
                float d = dot(n, _WorldSpaceLightPos0.xyz);
                float3 amb = n.y >= 0 ? lerp(unity_AmbientEquator.rgb, unity_AmbientSky.rgb, n.y)
                                      : lerp(unity_AmbientEquator.rgb, unity_AmbientGround.rgb, -n.y * 0.5);
                return albedo * (amb + _LightColor0.rgb * Ramp(max(0, d)) * 0.8 + _LightColor0.rgb * (d * 0.5 + 0.5) * 0.2);
            }

            fixed4 frag (v2f i, out float depth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // growing back after it broke (PHWeapons): every other pixel, a see-through screen
                if (_Dither > 0.5 && (((int)i.pos.x + (int)i.pos.y) & 1) == 0) discard;
                depth = i.pos.z;
                float3 nObj = normalize(i.objN);
                float3 albedo = i.albedo;
                float3 nWorld = normalize(UnityObjectToWorldNormal(nObj));
                if (i.card.z > 0.5)
                {
                    // a drawn weapon: the picture, cut out; worn away where its wear is reached (no grid)
                    fixed4 c = tex2D(_CardTex, i.card.xy);
                    clip(c.a - 0.5);
                    if (_Wear > 0.001 && tex2D(_CardWear, i.card.xy).r * (255.0 / 254.0) <= _Wear) discard;
                    float3 ca = c.rgb;   // (the atlas is an sRGB texture: already linear here)
                    // (flat: lit mostly by the sky, so the drawing reads as drawn from either side)
                    fixed4 oc = fixed4(ca * (0.75 + 0.35 * Light(1, nWorld)), 1);
                    UNITY_APPLY_FOG(i.fogCoord, oc);
                    return oc;
                }
                if (_Wear > 0.001)
                {
                    float cell = _WearLo.w;   // the grid's voxel size
                    if (Gone(i.obj - nObj * cell * 0.5) <= _Wear)
                    {
                        // eaten here: follow the view ray in until the weapon is whole again
                        float3 cam = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
                        float3 rd = normalize(i.obj - cam);
                        float stepLen = cell * 0.6;
                        float3 p = i.obj;
                        bool hit = false;
                        [loop] for (int k = 1; k <= 64; k++)
                        {
                            p = i.obj + rd * (stepLen * k);
                            if (Gone(p) > _Wear) { hit = true; break; }
                        }
                        if (!hit) discard;
                        // refine between the last two steps
                        float3 a = p - rd * stepLen, b = p;
                        [unroll] for (int r = 0; r < 4; r++)
                        {
                            float3 m = (a + b) * 0.5;
                            if (Gone(m) > _Wear) b = m; else a = m;
                        }
                        p = b;
                        // the gnawed face's normal: away from where the weapon lasts longer
                        float e = cell * 0.8;
                        float3 g = float3(Gone(p + float3(e, 0, 0)) - Gone(p - float3(e, 0, 0)),
                                          Gone(p + float3(0, e, 0)) - Gone(p - float3(0, e, 0)),
                                          Gone(p + float3(0, 0, e)) - Gone(p - float3(0, 0, e)));
                        float3 gn = dot(g, g) > 1e-8 ? -normalize(g) : -rd;
                        nWorld = normalize(UnityObjectToWorldNormal(gn));
                        albedo = albedo * _Fresh.rgb + 0.04;
                        float4 clip = UnityObjectToClipPos(float4(p, 1));
                        depth = clip.z / clip.w;
                    }
                }
                fixed4 o = fixed4(Light(albedo, nWorld), 1);
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }
    }
}
