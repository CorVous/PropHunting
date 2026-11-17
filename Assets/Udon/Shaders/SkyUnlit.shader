// The sky's own things (MapBuilderUdon's sky dressing): the night's stars and the moon or a space base's planet.
// Unlit vertex colour x tint, and no fog, so they stay visible far beyond where the map's fog ends.
Shader "PropHunt/SkyUnlit"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                // a little shading on a sphere (the moon, the planet): lit from above
                float3 n = UnityObjectToWorldNormal(v.normal);
                float shade = 0.65 + 0.35 * saturate(dot(n, normalize(float3(0.3, 0.8, 0.2))));
                o.color = v.color * _Color * fixed4(shade, shade, shade, 1);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
