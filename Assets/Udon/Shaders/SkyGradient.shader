// The map's sky (Cor: profiling found Unity's procedural skybox cost a third to half of the GPU frame). A three-colour
// gradient (zenith, horizon, below the horizon) and a soft sun disc: a few multiplies a pixel. MapBuilderUdon.ApplyMood sets the colours from the map's mood.
Shader "PropHunt/SkyGradient"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.36, 0.52, 0.80, 1)
        _Horizon ("Horizon", Color) = (0.72, 0.80, 0.90, 1)
        _GroundColor ("Below the horizon", Color) = (0.50, 0.50, 0.50, 1)
        _SunColor ("Sun", Color) = (1, 0.95, 0.85, 1)
        _SunSize ("Sun size", Range(0, 1)) = 0.035
        _Exposure ("Exposure", Range(0, 4)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _Zenith, _Horizon, _GroundColor, _SunColor;
            half _SunSize, _Exposure;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert (float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.dir = vertex.xyz;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half y = d.y;
                half3 up = lerp(_Horizon.rgb, _Zenith.rgb, sqrt(saturate(y)));
                half3 down = lerp(_Horizon.rgb, _GroundColor.rgb, saturate(-y * 6));
                half3 c = y >= 0 ? up : down;
                half s = dot(d, _WorldSpaceLightPos0.xyz);
                half sun = smoothstep(1 - _SunSize * 0.02, 1 - _SunSize * 0.012, s) + s * s * s * s * saturate(s) * 0.12;
                return half4((c + _SunColor.rgb * sun) * _Exposure, 1);
            }
            ENDCG
        }
    }
}
