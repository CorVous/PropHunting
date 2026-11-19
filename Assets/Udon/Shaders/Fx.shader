// Particles and swing trails (PHWeapons): vertex colour x tint, unlit, alpha blended or additive (_Add 1), round
// soft dots when _Round is 1 (dust, sparks), plain squares otherwise (chips, confetti). A trail's u runs along it:
// _Trail 1 fades it towards its old end.
Shader "PropHunt/Fx"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Round ("Round dots", Float) = 1
        _Trail ("Trail fade", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _Src ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _Dst ("Dst", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend [_Src] [_Dst]
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _Round, _Trail;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = i.color;
                if (_Round > 0.5) c.a *= saturate((1.0 - length(i.uv * 2.0 - 1.0)) * 3.0);
                if (_Trail > 0.5) c.a *= (1.0 - i.uv.x) * smoothstep(0.0, 0.25, i.uv.y) * smoothstep(1.0, 0.75, i.uv.y);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
