// Phase 11a: the thin outline round loot on the ground (HUD reference: "items lie as 3D models with a thin white outline so they read
// against any floor"). Drawn on a copy of each mesh (LootVisuals) in two passes: the first marks the item's own pixels in the stencil,
// the second draws the mesh grown outward from its center (_CenterOS, set per copy) by a fixed number of pixels, only outside the mark,
// so just the silhouette's edge shows (no lines across the model, no gaps at hard edges). Faded out past _FadeEnd.
Shader "Brownout/Item Outline"
{
	Properties
	{
		_Color     ("Color", Color)                   = (1, 1, 1, 0.75)
		_Width     ("Width (pixels at 1080p)", Float) = 1.4
		_FadeStart ("Fade start (m)", Float)          = 12
		_FadeEnd   ("Fade end (m)", Float)            = 30
		[HideInInspector] _CenterOS ("Center (object space)", Vector) = (0, 0, 0, 0)
	}

	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

		CBUFFER_START(UnityPerMaterial)
			half4  _Color;
			float  _Width;
			float  _FadeStart;
			float  _FadeEnd;
			float4 _CenterOS;
		CBUFFER_END

		struct Attributes
		{
			float4 positionOS : POSITION;
		};
		ENDHLSL

		// 1: mark the item's visible pixels
		Pass
		{
			Name "OutlineMask"
			Tags { "LightMode" = "SRPDefaultUnlit" }

			ColorMask 0
			ZWrite Off
			Stencil { Ref 64 WriteMask 64 Comp Always Pass Replace }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4 vert(Attributes input) : SV_POSITION
			{
				return TransformObjectToHClip(input.positionOS.xyz);
			}

			half4 frag() : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}

		// 2: the grown silhouette, outside the mark
		Pass
		{
			Name "Outline"
			Tags { "LightMode" = "UniversalForwardOnly" }

			ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha
			Stencil { Ref 64 ReadMask 64 Comp NotEqual }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float  fade       : TEXCOORD0;
			};

			Varyings vert(Attributes input)
			{
				Varyings output;

				float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
				float4 positionCS = TransformWorldToHClip(positionWS);
				float4 centerCS   = TransformObjectToHClip(_CenterOS.xyz);

				// Away from the center on screen, a fixed width in pixels (scaled from 1080p)
				float2 fromCenter = positionCS.xy / positionCS.w - centerCS.xy / centerCS.w;
				float2 direction  = fromCenter * _ScreenParams.xy;
				direction = length(direction) > 1e-4 ? normalize(direction) : float2(0, 0);

				float pixels = _Width * _ScreenParams.y / 1080.0;
				positionCS.xy += direction * pixels * 2.0 / _ScreenParams.xy * positionCS.w;

				output.positionCS = positionCS;

				float distance = length(positionWS - GetCameraPositionWS());
				output.fade = 1.0 - saturate((distance - _FadeStart) / max(0.01, _FadeEnd - _FadeStart));
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				return half4(_Color.rgb, _Color.a * input.fade);
			}
			ENDHLSL
		}
	}
}
