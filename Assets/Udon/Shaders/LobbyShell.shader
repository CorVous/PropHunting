// The lobby seen from the map (Cor: keep the lobby's walls visible so players can see where their friends spawned).
// A plain box round the lobby, far off in the sky: unlit, and no fog, so it still shows past where the map's fog
// ends. Its faces point outwards, so from inside the lobby it is culled and never seen. Walls in _Side, the lid in
// _Top, darkened with the ambient light (night) and eased a little towards the fog colour so it sits in the haze.
Shader "PropHunt/LobbyShell"
{
    Properties
    {
        _Side ("Walls", Color) = (0.141, 0.376, 0.290, 1)
        _Top ("Lid", Color) = (0.937, 0.886, 0.749, 1)
        _Haze ("Towards the fog colour", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Side, _Top;
            half _Haze;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = UnityObjectToWorldNormal(v.normal);
                fixed3 c = n.y > 0.5 ? _Top.rgb : _Side.rgb * (0.8 + 0.2 * abs(n.x));   // two wall tones, so the corner reads
                // the ambient light the map's mood set (dim at night), never quite black
                half amb = saturate(dot(unity_AmbientEquator.rgb, half3(0.3, 0.59, 0.11)) * 1.8);
                c *= lerp(0.25, 1.0, amb);
                c = lerp(c, unity_FogColor.rgb, _Haze);
                o.color = fixed4(c, 1);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
