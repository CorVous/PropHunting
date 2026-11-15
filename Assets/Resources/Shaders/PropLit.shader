// One lit shader for every generated prop and for the architecture: vertex colour x tint,
// one directional light plus a sky/ground ambient, computed per vertex. It has no shadow pass, so
// props cast no shadows (C3). Vertex colours are sRGB bytes baked by prototype/tools/convert_models.py.
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
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

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
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(0)
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
                float3 light = amb + _LightColor0.rgb * ndl;
                float3 albedo = v.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                albedo = GammaToLinearSpace(albedo);
                #endif
                fixed4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                o.color = fixed4(albedo * tint.rgb * light, 1);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = i.color;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return fixed4(c.rgb, 1);
            }
            ENDCG
        }
    }
}
