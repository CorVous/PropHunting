// The soft dark patch under a prop (Cor: more cohesion between the assets, at no real cost): one flat fan per prop,
// its darkness in the vertex alpha (1 at the centre, 0 at the rim), multiplied onto whatever floor it lies on. No
// texture, no light, no depth write; instanced, so all the patches in view are a draw call or two.
Shader "PropHunt/Shade"
{
    Properties
    {
        _Shade ("Shade colour", Color) = (0.50, 0.53, 0.66, 1)
        // as PropLit: 1 on a hider's own prop, which only the third-person view draws
        [HideInInspector] _PipOnly ("Third-person view only", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Geometry+450" "IgnoreProjector"="True" }
        Pass
        {
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "PipCut.cginc"

            fixed4 _Shade;
            float _PipOnly;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half a : TEXCOORD0;
                float3 worldPos : TEXCOORD2;
                UNITY_FOG_COORDS(1)
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                if (_PipOnly > 0.5 && abs(_ScreenParams.x - 384.0) > 0.5) o.pos = float4(2.0, 2.0, 2.0, 1.0);
                o.a = v.color.a;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // a patch on an upper floor goes with the floor in the prop viewer's cutout (PipCut.cginc); its rim stays unshaded
                if (_PipOnly < 0.5 && PipCut(i.worldPos, i.pos.xy, 0) > 0.5) return fixed4(1, 1, 1, 1);
                half a = i.a * i.a * (3.0 - 2.0 * i.a);
                fixed4 col = fixed4(lerp(float3(1, 1, 1), _Shade.rgb, a), 1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(1, 1, 1, 1));   // far off it fades out with the fog
                return col;
            }
            ENDCG
        }
    }
}
