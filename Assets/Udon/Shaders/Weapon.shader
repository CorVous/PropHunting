// A held weapon or selector (PHWeapons): vertex colour, a sky/ground ambient and the sun in the props' toon bands,
// and the wear: up to 12 spheres (_Chip0.._Chip11: centre in the mesh's own space, radius; radius 0 = none, set
// per weapon by property block) cut chunks out of it. Faces are drawn on both sides, so a cut shows the inside
// (_Inner, a dark core); the surface darkens a little towards each cut's edge, a bitten rim.
Shader "PropHunt/Weapon"
{
    Properties
    {
        _Inner ("Inside", Color) = (0.16, 0.13, 0.11, 1)
        _Chip0 ("", Vector) = (0,0,0,0)  _Chip1 ("", Vector) = (0,0,0,0)  _Chip2 ("", Vector) = (0,0,0,0)
        _Chip3 ("", Vector) = (0,0,0,0)  _Chip4 ("", Vector) = (0,0,0,0)  _Chip5 ("", Vector) = (0,0,0,0)
        _Chip6 ("", Vector) = (0,0,0,0)  _Chip7 ("", Vector) = (0,0,0,0)  _Chip8 ("", Vector) = (0,0,0,0)
        _Chip9 ("", Vector) = (0,0,0,0)  _Chip10 ("", Vector) = (0,0,0,0) _Chip11 ("", Vector) = (0,0,0,0)
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
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            fixed4 _Inner;
            float4 _Chip0, _Chip1, _Chip2, _Chip3, _Chip4, _Chip5, _Chip6, _Chip7, _Chip8, _Chip9, _Chip10, _Chip11;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 obj : TEXCOORD0;
                half3 normal : TEXCOORD1;
                half3 albedo : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.obj = v.vertex.xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                float3 a = v.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                a = GammaToLinearSpace(a);
                #endif
                o.albedo = a;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // how near a cut's edge (0 far, 1 at the edge); a point inside a cut is clipped
            float Cut(float3 p, float4 c)
            {
                if (c.w <= 0.0) return 0.0;
                float d = distance(p, c.xyz);
                clip(d - c.w);
                return 1.0 - saturate((d - c.w) / (c.w * 0.4));
            }

            float Ramp(float x) { return 0.5 * smoothstep(0.03, 0.22, x) + 0.5 * smoothstep(0.34, 0.66, x); }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                float rim = 0.0;
                rim = max(rim, Cut(i.obj, _Chip0)); rim = max(rim, Cut(i.obj, _Chip1)); rim = max(rim, Cut(i.obj, _Chip2));
                rim = max(rim, Cut(i.obj, _Chip3)); rim = max(rim, Cut(i.obj, _Chip4)); rim = max(rim, Cut(i.obj, _Chip5));
                rim = max(rim, Cut(i.obj, _Chip6)); rim = max(rim, Cut(i.obj, _Chip7)); rim = max(rim, Cut(i.obj, _Chip8));
                rim = max(rim, Cut(i.obj, _Chip9)); rim = max(rim, Cut(i.obj, _Chip10)); rim = max(rim, Cut(i.obj, _Chip11));
                bool inside = facing < 0;
                float3 n = normalize(i.normal) * (inside ? -1.0 : 1.0);
                float3 albedo = inside ? _Inner.rgb * (0.6 + 0.4 * i.albedo) : i.albedo * lerp(1.0, 0.45, rim * rim);
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
