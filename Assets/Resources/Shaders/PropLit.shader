// One lit shader for every generated prop and for the architecture: vertex colour x texture x tint,
// one directional light plus flat ambient, computed per vertex. No shadow pass, so props cast none (C3).
Shader "PropHunt/Lit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
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
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(1)
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                float3 n = UnityObjectToWorldNormal(v.normal);
                float ndl = max(0, dot(n, _WorldSpaceLightPos0.xyz));
                // sky/ground ambient blend keeps unlit faces readable
                float up = n.y * 0.5 + 0.5;
                fixed3 amb = lerp(unity_AmbientGround.rgb, unity_AmbientSky.rgb, up);
                fixed3 light = amb + _LightColor0.rgb * ndl;
                fixed4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                o.color = fixed4(v.color.rgb * tint.rgb * light, 1);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return fixed4(c.rgb, 1);
            }
            ENDCG
        }
    }
}
