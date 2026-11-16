// The hider's own prop in first person: a faint, two-sided see-through shell (05 section 3.10).
// A screen-door pattern keeps about a quarter of the pixels, so it needs no blending order.
// The eye is inside the prop (G5: at 0.9 of its height), so without more the shell's inner faces fill the view
// (a mushroom's cap, a wardrobe's doors). Nothing of it is drawn within _Clear metres of the eye (the player sets
// it to the prop's half diagonal) unless it lies more than _Below metres under the eye (a third of the prop's
// height): looking ahead the view is clear, looking down the prop shows faintly below you.
Shader "PropHunt/Ghost"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Clear ("Clear radius around the eye (m)", Float) = 0
        _Below ("Drawn anyway this far under the eye (m)", Float) = 0
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
            float _Clear, _Below;

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
                float3 world : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
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
                clip(max(distance(i.world, _WorldSpaceCameraPos) - _Clear, _WorldSpaceCameraPos.y - _Below - i.world.y));
                float2 p = floor(i.pos.xy);
                // keep one pixel in four
                clip(0.5 - (fmod(p.x, 2.0) + fmod(p.y, 2.0)));
                return fixed4(i.color.rgb, 1);
            }
            ENDCG
        }
    }
}
