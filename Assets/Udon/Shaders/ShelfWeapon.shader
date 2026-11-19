// A weapon standing on the weapon wall (PHShelf): lit like a held weapon (PropHunt/Weapon: vertex colour, a
// sky/ground ambient and the sun in the props' toon bands), and dithered when the player doesn't own it yet
// (_Dither 1, set per weapon by property block): every other pixel in a 2 x 2 checker is dropped.
Shader "PropHunt/ShelfWeapon"
{
    Properties
    {
        _Dither ("Dithered", Float) = 0
        _Fade ("Dithered brightness", Range(0, 1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            float _Dither, _Fade;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 normal : TEXCOORD0;
                half3 albedo : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                float3 a = v.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                a = GammaToLinearSpace(a);
                #endif
                o.albedo = a;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float Ramp(float x) { return 0.5 * smoothstep(0.03, 0.22, x) + 0.5 * smoothstep(0.34, 0.66, x); }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                if (_Dither > 0.5)
                {
                    float2 p = floor(i.pos.xy * 0.5);
                    clip(fmod(p.x + p.y, 2.0) - 0.5);
                }
                float3 n = normalize(i.normal) * (facing < 0 ? -1.0 : 1.0);
                float3 albedo = i.albedo * (_Dither > 0.5 ? _Fade : 1.0);
                float d = dot(n, _WorldSpaceLightPos0.xyz);
                float3 amb = n.y >= 0 ? lerp(unity_AmbientEquator.rgb, unity_AmbientSky.rgb, n.y)
                                      : lerp(unity_AmbientEquator.rgb, unity_AmbientGround.rgb, -n.y * 0.5);
                float3 sun = _LightColor0.rgb * Ramp(max(0, d)) * 0.8;
                float3 col = albedo * (amb + sun + _LightColor0.rgb * (d * 0.5 + 0.5) * 0.2);
                fixed4 o = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }
    }
}
