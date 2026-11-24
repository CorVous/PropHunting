// The lobby's lit shader: VRC Light Volumes' "Light Volume PBR" sample (red.sim.lightvolumes, MIT), with one change:
// the sun's light is darkened by a shadow map baked once when the scene is built (LobbyShadowBaker), so the lobby's
// dressing keeps its shadows on the floor while the sun's realtime shadows are off in the lobby (PHLobbyCull; Cor,
// lobby profiling 2026-11-24: they cost about 130 draw calls there). The real sun still lights everything, avatars
// included, so nothing else changes. The shadow map and its projection are globals PHLobbyCull sets at Start.
Shader "PropHunt/LobbyLit"
{
	Properties
	{
		_MainTex( "Albedo", 2D ) = "white" {}
		_Color( "Color", Color ) = ( 1, 1, 1, 1 )
		[NoScaleOffset] _MetallicGlossMap( "Metal AO Smoothness", 2D ) = "white" {}
		_Metallic( "Metallic", Range( 0, 1 ) ) = 0
		_Glossiness( "Smoothness", Range( 0, 1 ) ) = 1
		_OcclusionStrength( "AO", Range( 0, 1 ) ) = 1
		[NoScaleOffset] _BumpMap( "Normal", 2D ) = "bump" {}
		_BumpScale( "Normal Power", Float ) = 1
		[HDR][NoScaleOffset] _EmissionMap( "Emission Map", 2D ) = "white" {}
		[HDR] _EmissionColor( "Emission Color", Color ) = ( 0, 0, 0, 1 )
		_LightVolumesBias( "Light Volumes Bias", Float ) = 0
		_PointLightVolumesShading( "Point Light Volumes Shading", Float ) = 2
		[Toggle( _SPECULARS_ON )] _Speculars( "Speculars", Float ) = 1
		[Enum(UnityEngine.Rendering.CullMode)] _Culling( "Culling", Float ) = 2
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
		[Header(Forward Rendering Options)]
		[ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
		[ToggleOff] _GlossyReflections("Reflections", Float) = 1.0
	}

	SubShader
	{
		Tags{ "RenderType" = "Opaque"  "Queue" = "Geometry+0" "IsEmissive" = "true"  }
		Cull [_Culling]
		CGINCLUDE
		#include "UnityStandardUtils.cginc"
		#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
		#include "UnityStandardBRDF.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.5
		#pragma shader_feature _SPECULARHIGHLIGHTS_OFF
		#pragma shader_feature _GLOSSYREFLECTIONS_OFF
		#pragma shader_feature_local _SPECULARS_ON
		#define ASE_VERSION 19909
		#ifdef UNITY_PASS_SHADOWCASTER
			#undef INTERNAL_DATA
			#undef WorldReflectionVector
			#undef WorldNormalVector
			#define INTERNAL_DATA half3 internalSurfaceTtoW0; half3 internalSurfaceTtoW1; half3 internalSurfaceTtoW2;
			#define WorldReflectionVector(data,normal) reflect (data.worldRefl, half3(dot(data.internalSurfaceTtoW0,normal), dot(data.internalSurfaceTtoW1,normal), dot(data.internalSurfaceTtoW2,normal)))
			#define WorldNormalVector(data,normal) half3(dot(data.internalSurfaceTtoW0,normal), dot(data.internalSurfaceTtoW1,normal), dot(data.internalSurfaceTtoW2,normal))
		#endif
		struct Input
		{
			float2 uv_texcoord;
			float3 worldNormal;
			INTERNAL_DATA
			float3 worldPos;
		};

		uniform float _Culling;
		uniform sampler2D _BumpMap;
		uniform sampler2D _MainTex;
		uniform float4 _MainTex_ST;
		uniform float _BumpScale;
		uniform float4 _Color;
		uniform float4 _EmissionColor;
		uniform sampler2D _EmissionMap;
		uniform sampler2D _MetallicGlossMap;
		uniform float _Glossiness;
		uniform float _Metallic;
		uniform float _LightVolumesBias;
		uniform float _PointLightVolumesShading;
		uniform float _OcclusionStrength;

		// ---- the baked lobby shadow (LobbyShadowBaker writes it, PHLobbyCull sets these globals)
		sampler2D _UdonPHLobbyShadow;                                   // depth from the sun, 0 at the light, 1 far
		float4 _UdonPHLobbySM0, _UdonPHLobbySM1, _UdonPHLobbySM2;        // world -> (u, v, depth): one row each
		float4 _UdonPHLobbySP;                                          // x texel size, y depth bias, z on (1)
		float4 LobbyTapRow( float2 b, float t, float d, float y )
		{
			float4 r;
			r.x = d <= tex2Dlod( _UdonPHLobbyShadow, float4( b + float2( -1, y ) * t, 0, 0 ) ).r ? 1 : 0;
			r.y = d <= tex2Dlod( _UdonPHLobbyShadow, float4( b + float2( 0, y ) * t, 0, 0 ) ).r ? 1 : 0;
			r.z = d <= tex2Dlod( _UdonPHLobbyShadow, float4( b + float2( 1, y ) * t, 0, 0 ) ).r ? 1 : 0;
			r.w = d <= tex2Dlod( _UdonPHLobbyShadow, float4( b + float2( 2, y ) * t, 0, 0 ) ).r ? 1 : 0;
			return r;
		}
		half LobbyShadow( float3 worldPos, float3 worldNormal )
		{
			[branch] if ( _UdonPHLobbySP.z < 0.5 ) return 1;
			float4 w = float4( worldPos + worldNormal * 0.03, 1 );
			float3 s = float3( dot( _UdonPHLobbySM0, w ), dot( _UdonPHLobbySM1, w ), dot( _UdonPHLobbySM2, w ) );
			[branch] if ( any( s.xy < 0.0 ) || any( s.xy > 1.0 ) ) return 1;
			float d = s.z - _UdonPHLobbySP.y;
			float t = _UdonPHLobbySP.x;
			#if defined( SHADER_API_MOBILE )
			// Quest: 2 x 2 taps, bilinear between them
			float2 fq = frac( s.xy / t - 0.5 );
			float2 bq = ( floor( s.xy / t - 0.5 ) + 0.5 ) * t;
			half a00 = d <= tex2Dlod( _UdonPHLobbyShadow, float4( bq, 0, 0 ) ).r ? 1 : 0;
			half a10 = d <= tex2Dlod( _UdonPHLobbyShadow, float4( bq + float2( t, 0 ), 0, 0 ) ).r ? 1 : 0;
			half a01 = d <= tex2Dlod( _UdonPHLobbyShadow, float4( bq + float2( 0, t ), 0, 0 ) ).r ? 1 : 0;
			half a11 = d <= tex2Dlod( _UdonPHLobbyShadow, float4( bq + float2( t, t ), 0, 0 ) ).r ? 1 : 0;
			return lerp( lerp( a00, a10, fq.x ), lerp( a01, a11, fq.x ), fq.y );
			#else
			// 3 x 3 taps, bilinear between them: soft edges a few centimetres wide
			float2 f = frac( s.xy / t - 0.5 );
			float2 b = ( floor( s.xy / t - 0.5 ) + 0.5 ) * t;
			float4 r0 = LobbyTapRow( b, t, d, -1 ), r1 = LobbyTapRow( b, t, d, 0 ), r2 = LobbyTapRow( b, t, d, 1 ), r3 = LobbyTapRow( b, t, d, 2 );
			// each row's three bilinear pairs, then the three rows' pairs
			float3 h0 = lerp( r0.xyz, r0.yzw, f.x ), h1 = lerp( r1.xyz, r1.yzw, f.x ), h2 = lerp( r2.xyz, r2.yzw, f.x ), h3 = lerp( r3.xyz, r3.yzw, f.x );
			float sum = dot( lerp( h0, h1, f.y ) + lerp( h1, h2, f.y ) + lerp( h2, h3, f.y ), float3( 1, 1, 1 ) );
			return sum / 9.0;
			#endif
		}

		struct SurfaceOutputLobby
		{
			fixed3 Albedo; float3 Normal; half3 Emission; half Metallic; half Smoothness; half Occlusion; fixed Alpha;
			half Shadow;
		};
		SurfaceOutputStandard LobbyToStandard( SurfaceOutputLobby s )
		{
			SurfaceOutputStandard o;
			o.Albedo = s.Albedo; o.Normal = s.Normal; o.Emission = s.Emission; o.Metallic = s.Metallic;
			o.Smoothness = s.Smoothness; o.Occlusion = s.Occlusion; o.Alpha = s.Alpha;
			return o;
		}
		inline half4 LightingLobby( SurfaceOutputLobby s, float3 viewDir, UnityGI gi )
		{
			gi.light.color *= s.Shadow;
			return LightingStandard( LobbyToStandard( s ), viewDir, gi );
		}
		inline void LightingLobby_GI( SurfaceOutputLobby s, UnityGIInput data, inout UnityGI gi )
		{
			LightingStandard_GI( LobbyToStandard( s ), data, gi );
		}

		void surf( Input i , inout SurfaceOutputLobby o )
		{
			float2 uv_MainTex = i.uv_texcoord * _MainTex_ST.xy + _MainTex_ST.zw;
			float3 tex2DNode5 = UnpackScaleNormal( tex2D( _BumpMap, uv_MainTex ), _BumpScale );
			float3 Normal437 = tex2DNode5;
			o.Normal = Normal437;
			float3 Albedo337 = ( _Color.rgb * tex2D( _MainTex, uv_MainTex ).rgb );
			o.Albedo = Albedo337;
			float3 normalizeResult349 = normalize( (WorldNormalVector( i , tex2DNode5 )) );
			float3 World_Normal112 = normalizeResult349;
			float3 worldNormal2_g222 = World_Normal112;
			float localLightVolumeSHSpecular1_g244 = ( 0.0 );
			float3 ase_positionWS = i.worldPos;
			float3 temp_output_6_0_g244 = ase_positionWS;
			float3 worldPos1_g244 = temp_output_6_0_g244;
			float3 L01_g244 = float3( 0,0,0 );
			float3 L1r1_g244 = float3( 0,0,0 );
			float3 L1g1_g244 = float3( 0,0,0 );
			float3 L1b1_g244 = float3( 0,0,0 );
			float3 Specular1_g244 = float3( 0,0,0 );
			float3 temp_output_21_0_g244 = Albedo337;
			float3 Albedo1_g244 = temp_output_21_0_g244;
			float4 tex2DNode50 = tex2D( _MetallicGlossMap, uv_MainTex );
			float Smoothness109 = ( tex2DNode50.a * _Glossiness );
			float temp_output_23_0_g244 = Smoothness109;
			float Smoothness1_g244 = temp_output_23_0_g244;
			float temp_output_54_0 = ( tex2DNode50.r * _Metallic );
			float Metallic334 = ( temp_output_54_0 * temp_output_54_0 );
			float temp_output_24_0_g244 = Metallic334;
			float Metallic1_g244 = temp_output_24_0_g244;
			float3 temp_output_18_0_g244 = World_Normal112;
			float3 WorldNormal1_g244 = temp_output_18_0_g244;
			float3 ase_viewVectorWS = ( ( unity_OrthoParams.w == 0 ) ? _WorldSpaceCameraPos - ase_positionWS : UNITY_MATRIX_V[ 2 ].xyz );
			float3 ase_viewDirSafeWS = Unity_SafeNormalize( ase_viewVectorWS );
			float3 normalizeResult26_g244 = normalize( ase_viewDirSafeWS );
			float3 temp_output_20_0_g244 = normalizeResult26_g244;
			float3 ViewDir1_g244 = temp_output_20_0_g244;
			float3 temp_output_475_0 = ( _LightVolumesBias * World_Normal112 );
			float3 temp_output_17_0_g244 = temp_output_475_0;
			float3 WorldPosOffset1_g244 = temp_output_17_0_g244;
			float temp_output_30_0_g244 = _PointLightVolumesShading;
			float PointLightShading1_g244 = temp_output_30_0_g244;
			LightVolumeSHSpecular( worldPos1_g244 , L01_g244 , L1r1_g244 , L1g1_g244 , L1b1_g244 , Specular1_g244 , Albedo1_g244 , Smoothness1_g244 , Metallic1_g244 , WorldNormal1_g244 , ViewDir1_g244 , WorldPosOffset1_g244 , PointLightShading1_g244 );
			float localLightVolumeAdditiveSHSpecular9_g245 = ( 0.0 );
			float3 temp_output_6_0_g245 = ase_positionWS;
			float3 worldPos9_g245 = temp_output_6_0_g245;
			float3 L09_g245 = float3( 0,0,0 );
			float3 L1r9_g245 = float3( 0,0,0 );
			float3 L1g9_g245 = float3( 0,0,0 );
			float3 L1b9_g245 = float3( 0,0,0 );
			float3 Specular9_g245 = float3( 0,0,0 );
			float3 temp_output_21_0_g245 = Albedo337;
			float3 Albedo9_g245 = temp_output_21_0_g245;
			float temp_output_23_0_g245 = Smoothness109;
			float Smoothness9_g245 = temp_output_23_0_g245;
			float temp_output_24_0_g245 = Metallic334;
			float Melallic9_g245 = temp_output_24_0_g245;
			float3 temp_output_18_0_g245 = World_Normal112;
			float3 WorldNormal9_g245 = temp_output_18_0_g245;
			float3 normalizeResult26_g245 = normalize( ase_viewDirSafeWS );
			float3 temp_output_20_0_g245 = normalizeResult26_g245;
			float3 ViewDir9_g245 = temp_output_20_0_g245;
			float3 temp_output_17_0_g245 = temp_output_475_0;
			float3 WorldPosOffset9_g245 = temp_output_17_0_g245;
			float temp_output_30_0_g245 = _PointLightVolumesShading;
			float PointLightShading9_g245 = temp_output_30_0_g245;
			LightVolumeAdditiveSHSpecular( worldPos9_g245 , L09_g245 , L1r9_g245 , L1g9_g245 , L1b9_g245 , Specular9_g245 , Albedo9_g245 , Smoothness9_g245 , Melallic9_g245 , WorldNormal9_g245 , ViewDir9_g245 , WorldPosOffset9_g245 , PointLightShading9_g245 );
			#ifdef LIGHTMAP_ON
				float3 staticSwitch472 = L09_g245;
			#else
				float3 staticSwitch472 = L01_g244;
			#endif
			float3 L02_g222 = staticSwitch472;
			#ifdef LIGHTMAP_ON
				float3 staticSwitch93 = L1r9_g245;
			#else
				float3 staticSwitch93 = L1r1_g244;
			#endif
			float3 L1r2_g222 = staticSwitch93;
			#ifdef LIGHTMAP_ON
				float3 staticSwitch94 = L1g9_g245;
			#else
				float3 staticSwitch94 = L1g1_g244;
			#endif
			float3 L1g2_g222 = staticSwitch94;
			#ifdef LIGHTMAP_ON
				float3 staticSwitch95 = L1b9_g245;
			#else
				float3 staticSwitch95 = L1b1_g244;
			#endif
			float3 L1b2_g222 = staticSwitch95;
			float3 localLightVolumeEvaluate2_g222 = LightVolumeEvaluate( worldNormal2_g222 , L02_g222 , L1r2_g222 , L1g2_g222 , L1b2_g222 );
			float3 temp_output_406_0 = ( localLightVolumeEvaluate2_g222 * Albedo337 * ( 1.0 - Metallic334 ) );
			#ifdef LIGHTMAP_ON
				float3 staticSwitch512 = Specular9_g245;
			#else
				float3 staticSwitch512 = Specular1_g244;
			#endif
			#ifdef _SPECULARS_ON
				float3 staticSwitch361 = ( temp_output_406_0 + staticSwitch512 );
			#else
				float3 staticSwitch361 = temp_output_406_0;
			#endif
			float lerpResult57 = lerp( 1.0 , tex2DNode50.g , _OcclusionStrength);
			float AO357 = lerpResult57;
			float3 IndirectAndSpeculars444 = ( staticSwitch361 * AO357 );
			float3 Emission452 = ( ( _EmissionColor.rgb * tex2D( _EmissionMap, uv_MainTex ).rgb ) + IndirectAndSpeculars444 );
			o.Emission = Emission452;
			o.Metallic = Metallic334;
			o.Smoothness = Smoothness109;
			o.Occlusion = AO357;
			o.Alpha = 1;
			o.Shadow = LobbyShadow( i.worldPos, World_Normal112 );
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf Lobby keepalpha fullforwardshadows exclude_path:deferred noambient 

		ENDCG
		UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
	}
	Fallback Off
}
