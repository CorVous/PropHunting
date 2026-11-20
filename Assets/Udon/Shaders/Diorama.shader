// The lobby diorama's walls (PHMiniMap): the map's walls mesh drawn small on the table, plainly lit by its vertex
// colours, and cut off above _ClipY (world height) when the upper floors are switched off, so the ground floor's
// rooms show. Only the diorama uses it.
Shader "PropHunt/Diorama"
{
    Properties
    {
        _ClipY ("Cut above (world y)", Float) = 100000
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            float _ClipY;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float3 n : TEXCOORD1; fixed4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.n = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                return o;
            }
            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                clip(_ClipY - i.wp.y);
                float3 n = normalize(i.n) * (facing > 0 ? 1.0 : -1.0);
                float l = 0.62 + 0.38 * saturate(dot(n, normalize(float3(0.4, 0.85, -0.35))));
                fixed3 c = i.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                c = GammaToLinearSpace(c);
                #endif
                return fixed4(c * l, 1);
            }
            ENDCG
        }
    }
}
