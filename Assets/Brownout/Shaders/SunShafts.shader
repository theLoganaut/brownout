// Phase 11c: light shafts from the sun. A full-screen pass (SunShaftsFeature) before post-processing: each pixel marches toward the sun
// on screen and gathers bright sky it passes over, so rays fan out from the sky between trees and buildings. ScreenLook sets the sun's
// screen position and the strength each frame (0 = the pass is skipped).
Shader "Brownout/Sun Shafts"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
		ZWrite Off ZTest Always Cull Off Blend Off

		Pass
		{
			Name "SunShafts"

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

			#define SAMPLES 24

			float  _BrownoutSunShafts;
			float4 _BrownoutSunScreen;     // xy = sun in viewport, z = ray length share
			float4 _BrownoutSunShaftColor;

			// Bright sky lets light through; anything in front of the sky blocks it
			half Light(float2 uv)
			{
				float depth = Linear01Depth(SampleSceneDepth(uv), _ZBufferParams);
				half  sky   = step(0.9995, depth);
				half3 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
				half  luma  = dot(color, half3(0.3, 0.59, 0.11));
				return sky * saturate(luma - 0.55);
			}

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float2 uv    = input.texcoord;
				half4  color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

				if (_BrownoutSunShafts <= 0.0)
					return color;

				float2 march  = (_BrownoutSunScreen.xy - uv) * _BrownoutSunScreen.z / SAMPLES;
				// Per-pixel jitter (interleaved gradient noise) hides the steps between samples
				float  jitter = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715))));
				float2 p      = uv + march * jitter;
				half   weight = 1.0;
				half   light  = 0.0;

				[unroll]
				for (int i = 0; i < SAMPLES; ++i)
				{
					light  += Light(p) * weight;
					weight *= 0.94;
					p      += march;
				}

				light /= SAMPLES;

				// Stronger near the sun
				float2 aspect = float2(_ScreenParams.x / _ScreenParams.y, 1.0);
				half   near   = saturate(1.0 - length((uv - _BrownoutSunScreen.xy) * aspect) * 0.9);

				color.rgb += light * near * _BrownoutSunShaftColor.rgb * _BrownoutSunShafts * 6.0;
				return color;
			}
			ENDHLSL
		}
	}
}
