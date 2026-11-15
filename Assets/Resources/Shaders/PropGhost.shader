// The hider's own prop in first person: a faint, two-sided see-through shell (05 section 3.10).
// A screen-door pattern keeps about a quarter of the pixels, so it needs no blending order.
Shader "PropHunt/Ghost"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Off
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = UnityObjectToWorldNormal(v.normal);
                float d = abs(dot(n, _WorldSpaceLightPos0.xyz));
                float3 light = unity_AmbientSky.rgb + _LightColor0.rgb * (d * 0.6 + 0.2);
                float3 albedo = v.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                albedo = GammaToLinearSpace(albedo);
                #endif
                o.color = fixed4(albedo * _Color.rgb * light, 1);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = floor(i.pos.xy);
                // keep one pixel in four
                clip(0.5 - (fmod(p.x, 2.0) + fmod(p.y, 2.0)));
                return fixed4(i.color.rgb, 1);
            }
            ENDCG
        }
    }
}
