// The lobby diorama's walls (PHMiniMap): the map's walls mesh drawn small on the table, plainly lit by its vertex
// colours (the ground too, cut to the map's rectangle _Box), and cut off above _ClipY (world height) when the upper floors are switched off, so the ground floor's
// rooms show. Only the diorama uses it.
Shader "PropHunt/Diorama"
{
    Properties
    {
        _ClipY ("Cut above (world y)", Float) = 100000
        _DitherY ("See-through screen above (world y)", Float) = 100000
        _Box ("Kept: world x min, z min, x max, z max", Vector) = (-100000, -100000, 100000, 100000)
        _CutAway ("Cutaway: walls across the view cut to a stub (0 off, 1 on)", Float) = 0
        _CutStub ("Cutaway stub height above each level's floor (map m)", Float) = 0.5
        _CutLevel ("Cutaway level height (map m)", Float) = 2.74
        _CutBand ("Cutaway: half width of a wall line incl. facade (map m)", Float) = 0.47
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
            float _ClipY, _DitherY;
            float4 _Box;
            float _CutAway, _CutStub, _CutLevel, _CutBand;
            sampler2D _UdonPHIndoor;      // (MapBuilderUdon.IndoorMap: a texel a cell, red the building's top in 5 cm steps)
            float4 _UdonPHIndoorArea;     // (x, y: 1 / its size in metres; z: 1 while a map is up)
            // is the cell holding map point p (the map's metres) inside a building at height y?
            bool Indoor(float2 p, float y)
            {
                if (_UdonPHIndoorArea.z < 0.5) return true;
                float2 uv = (floor(p / 4.0) + 0.5) * 4.0 * _UdonPHIndoorArea.xy;
                if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) return false;
                return y < tex2Dlod(_UdonPHIndoor, float4(uv, 0, 0)).r * 12.75 - 0.05;
            }
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float3 n : TEXCOORD1; float3 op : TEXCOORD2; float3 on : TEXCOORD3; fixed4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
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
                o.op = v.vertex.xyz; o.on = v.normal;   // (the map's own metres: the walls stand on the 4 m grid's lines)
                return o;
            }
            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                clip(_ClipY - i.wp.y);
                // (the round's replay with the upper floors off, Cor: the upper floors drawn as a screen of every other pixel)
                if (i.wp.y > _DitherY && (((int)i.pos.x + (int)i.pos.y) & 1) == 0) discard;
                // (Cor's cutaway: the walls that face the viewer are cut down to a stub, so the rooms can be seen into;
                // the walls running away from the viewer stay whole. Which lines are "across" follows the viewer round the table)
                if (_CutAway > 0.5)
                {
                    float3 cam = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
                    float2 d = normalize(i.op.xz - cam.xz + 1e-5);
                    float lv = i.op.y / _CutLevel;
                    float above = (lv - floor(lv)) * _CutLevel;   // (height over this level's floor)
                    if (above > _CutStub)
                    {
                        float2 g = abs(i.op.xz - round(i.op.xz / 4.0) * 4.0);   // (distance to the nearest grid line in x and z)
                        bool cx = abs(d.x) > 0.38, cz = abs(d.y) > 0.38;      // (diagonal views: both)
                        float3 on = normalize(i.on);
                        bool inX = g.x < _CutBand, inZ = g.y < _CutBand;
                        // a wall line across the view: cut, but not where a kept wall line crosses it (corner posts)
                        bool keepX = !cx && (g.x < 0.14 || (inX && abs(on.x) > 0.5));
                        bool keepZ = !cz && (g.y < 0.14 || (inZ && abs(on.z) > 0.5));
                        // a far wall stays (Cor): a line whose far side (away from the viewer) is outdoors at this height
                        float2 gl = round(i.op.xz / 4.0) * 4.0;
                        bool farZ = !Indoor(float2(i.op.x, gl.y + (d.y > 0 ? 2.0 : -2.0)), i.op.y);
                        bool farX = !Indoor(float2(gl.x + (d.x > 0 ? 2.0 : -2.0), i.op.z), i.op.y);
                        if ((cz && inZ && !keepX && !farZ) || (cx && inX && !keepZ && !farX)) discard;
                    }
                }
                clip(float4(i.wp.x - _Box.x, i.wp.z - _Box.y, _Box.z - i.wp.x, _Box.w - i.wp.z));   // (the ground runs far past the map: only the map's own)
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
