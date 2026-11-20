// The how-to-play wall's panels (LobbyBuilder.BuildHowTo): plays a flipbook atlas (cells left to right, top to
// bottom) from _Time, so no Udon runs for it. Unlit, alpha cutout, no fog; cheap enough for Quest. Each cell is
// sampled with a half-texel inset so its neighbours do not bleed in at the edges.
Shader "PropHunt/Flipbook"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Columns ("Columns", Float) = 8
        _Rows ("Rows", Float) = 8
        _Loop ("Loop seconds", Float) = 5
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Columns, _Rows, _Loop, _Cutoff;
            fixed4 _Tint;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 cell : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                // the frame is the same for the whole quad: work it out once per vertex
                float n = _Columns * _Rows;
                float f = floor(frac(_Time.y / max(_Loop, 0.01)) * n);
                f = min(f, n - 1.0);
                float col = fmod(f, _Columns);
                float row = floor(f / _Columns);
                o.cell = float2(col, _Rows - 1.0 - row);   // row 0 is the top of the picture, v = 1
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float2 cellSize = float2(1.0 / _Columns, 1.0 / _Rows);
                float2 inset = 0.5 * _MainTex_TexelSize.xy;
                float2 uv = clamp(i.uv * cellSize, inset, cellSize - inset) + i.cell * cellSize;
                fixed4 c = tex2D(_MainTex, uv) * _Tint;
                clip(c.a - _Cutoff);
                return fixed4(c.rgb, 1);
            }
            ENDCG
        }
    }
}
