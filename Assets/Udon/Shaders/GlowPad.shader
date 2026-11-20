// The warm pool of light under the weapon the player is carrying on the weapon wall (PHShelf, Cor: the carried one
// glows): an additive soft ellipse on a quad, breathing with the weapon's rim light (PropHunt/ShelfWeapon).
Shader "PropHunt/GlowPad"
{
    Properties
    {
        _GlowColor ("Glow colour", Color) = (1, 0.82, 0.35, 1)
        _Strength ("Strength", Float) = 0.9
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _GlowColor;
            float _Strength;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * 2.0 - 1.0;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float d = length(i.uv);
                float a = saturate(1.0 - d);
                a = a * a * _Strength * (0.8 + 0.2 * sin(_Time.y * 3.0));
                return fixed4(_GlowColor.rgb * a, 1);
            }
            ENDCG
        }
    }
}
