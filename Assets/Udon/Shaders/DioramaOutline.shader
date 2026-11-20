// The lobby diorama's hiding spots (PHMiniMap): a second copy of the prop a hider hid as, drawn as its outline in the
// player's colour (Cor: "the prop they were trying to hide as, outlined"). The classic inverted hull: the back faces,
// pushed out along their normals by _Width (world metres), so only a rim shows round the prop. _Color comes from a
// property block per copy.
Shader "PropHunt/DioramaOutline"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.86, 0.35, 1)
        _Width ("Width (world m)", Float) = 0.004
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" }
        Cull Front
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _Width;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz + normalize(UnityObjectToWorldNormal(v.normal)) * _Width;
                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
                return o;
            }
            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
}
