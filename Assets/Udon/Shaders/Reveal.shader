// The END reveal's highlight of every hider's prop (Cor: "highlight the prop through walls"): drawn over everything
// (no depth test), as bold diagonal stripes of a light and a near-black shade, so it reads by its pattern and
// brightness and not by its colour (colour-blind safe), and the prop's own shape shows through it. The stripes are
// fixed in the world, not on the screen (Cor: stripes that followed the eyes were disorienting in VR), and drift
// slowly along.
Shader "PropHunt/Reveal"
{
    Properties
    {
        _Light ("Light stripe", Color) = (1, 0.86, 0.25, 0.85)
        _Dark ("Dark stripe", Color) = (0.05, 0.05, 0.06, 0.75)
        _Width ("Stripe width (m)", Float) = 0.09
    }
    SubShader
    {
        Tags { "Queue" = "Overlay+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZTest Always
        ZWrite Off
        Cull Back
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _Light, _Dark;
            float _Width;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float t = frac((i.wp.x + i.wp.y + i.wp.z) / (2.0 * _Width) - _Time.y * 0.15);
                return t < 0.5 ? _Light : _Dark;
            }
            ENDCG
        }
    }
}
