// The weapons standing on one shelf row of the weapon wall (PHShelf): lit like a held weapon (PropHunt/Weapon:
// vertex colour, a sky/ground ambient and the sun in the props' toon bands), and dithered when the player doesn't own
// it yet: every other pixel in a 2 x 2 checker is dropped. The one the player is carrying glows (Cor): a warm rim
// light that breathes, and a soft halo shell around it (the second pass).
// A row's sixteen weapons are one mesh (LobbyBuilder.Store bakes one per shelf row; lobby profiling 2026-11-24: one
// draw call per weapon and pass was about 400 of the lobby's 700): each vertex carries its place on the row (0-15) in
// uv2.x. Per row, by property block: _DitherMask, bit n set when place n is dithered, and _GlowCol, the glowing place
// (-1: none).
Shader "PropHunt/ShelfWeapon"
{
    Properties
    {
        _DitherMask ("Dithered places (bits)", Float) = 0
        _Fade ("Dithered brightness", Range(0, 1)) = 0.8
        _GlowCol ("Glowing place (-1 none)", Float) = -1
        _GlowColor ("Glow colour", Color) = (1, 0.82, 0.35, 1)
        _Halo ("Halo width (m)", Float) = 0.016
        _CardTex ("Drawn weapons (atlas)", 2D) = "white" {}
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

            float _DitherMask, _Fade, _GlowCol;
            // place n's bit of the row's mask (exact in a float up to 2^24)
            float Bit(float mask, float n) { return fmod(floor(mask / exp2(n) + 0.001), 2.0); }
            fixed4 _GlowColor;
            sampler2D _CardTex;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 normal : TEXCOORD0;
                half3 albedo : TEXCOORD1;
                float3 view : TEXCOORD3;
                float3 card : TEXCOORD4;   // Cor's drawn weapons (PropHunt/Weapon): xy the atlas uv, z 1 on a card
                float2 state : TEXCOORD5;  // x dithered, y glowing (the same on all of a weapon's vertices)
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
                o.view = UnityWorldSpaceViewDir(mul(unity_ObjectToWorld, v.vertex).xyz);
                float3 a = v.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                a = GammaToLinearSpace(a);
                #endif
                o.albedo = a;
                o.card = float3(v.uv, v.color.a < 0.5 ? 1 : 0);
                float place = floor(v.uv2.x + 0.5);
                o.state = float2(Bit(_DitherMask, place), abs(place - _GlowCol) < 0.5 ? 1 : 0);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float Ramp(float x) { return 0.5 * smoothstep(0.03, 0.22, x) + 0.5 * smoothstep(0.34, 0.66, x); }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                if (i.state.x > 0.5)
                {
                    float2 p = floor(i.pos.xy * 0.5);
                    clip(fmod(p.x + p.y, 2.0) - 0.5);
                }
                float3 n = normalize(i.normal) * (facing < 0 ? -1.0 : 1.0);
                float3 albedo = i.albedo;
                if (i.card.z > 0.5)
                {
                    fixed4 c = tex2D(_CardTex, i.card.xy);
                    clip(c.a - 0.5);
                    albedo = c.rgb;   // (the atlas is an sRGB texture: already linear here)
                    n = normalize(i.view);   // (flat: lit as if facing whoever looks)
                }
                albedo *= (i.state.x > 0.5 ? _Fade : 1.0);
                float d = dot(n, _WorldSpaceLightPos0.xyz);
                float3 amb = n.y >= 0 ? lerp(unity_AmbientEquator.rgb, unity_AmbientSky.rgb, n.y)
                                      : lerp(unity_AmbientEquator.rgb, unity_AmbientGround.rgb, -n.y * 0.5);
                float3 sun = _LightColor0.rgb * Ramp(max(0, d)) * 0.8;
                float3 col = albedo * (amb + sun + _LightColor0.rgb * (d * 0.5 + 0.5) * 0.2);
                if (i.state.y > 0.5)
                {
                    float rim = 1.0 - saturate(abs(dot(n, normalize(i.view))));
                    float pulse = 0.75 + 0.25 * sin(_Time.y * 3.0);
                    col = col * 1.2 + _GlowColor.rgb * (0.18 + rim * rim * 1.4) * pulse;
                }
                fixed4 o = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }
        // the halo: the weapon's back faces pushed out a little along their normals in a flat warm colour (only the
        // glowing one; the others collapse to nothing)
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float _GlowCol, _Halo;
            fixed4 _GlowColor;
            sampler2D _CardTex;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 card : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz + UnityObjectToWorldNormal(v.normal) * _Halo;
                o.pos = abs(floor(v.uv2.x + 0.5) - _GlowCol) < 0.5 ? UnityWorldToClipPos(w) : float4(0, 0, 0, 0);
                o.card = float3(v.uv, v.color.a < 0.5 ? 1 : 0);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (i.card.z > 0.5) clip(tex2D(_CardTex, i.card.xy).a - 0.5);
                float pulse = 0.85 + 0.15 * sin(_Time.y * 3.0);
                return fixed4(_GlowColor.rgb * pulse, 1);
            }
            ENDCG
        }
    }
}
