// LobbyShadowBaker's depth pass: each caster's distance from the sun (0 near, 1 far), into a float target.
// Editor only (an Editor folder: it is not in the world).
Shader "Hidden/PropHunt/LobbyShadowDepth"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _PHLobbyBakeSM2;
            struct v2f { float4 pos : SV_POSITION; float d : TEXCOORD0; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.d = dot(_PHLobbyBakeSM2, float4(mul(unity_ObjectToWorld, v.vertex).xyz, 1));
                return o;
            }
            float4 frag(v2f i) : SV_Target { return float4(i.d, 0, 0, 1); }
            ENDCG
        }
    }
}
