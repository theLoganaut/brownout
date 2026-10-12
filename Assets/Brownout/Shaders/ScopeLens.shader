// Phase 11a: a magnified optic's lens (ScopeLens). Shows the lens camera's picture inside a circle on the viewmodel, with a dark ring
// at the edge and a thin crosshair with a center dot, so only the glass is zoomed and the scope body around it stays at normal size.
Shader "Brownout/Scope Lens"
{
	Properties
	{
		[NoScaleOffset] _LensTex ("Lens picture", 2D) = "black" {}
		_Vignette     ("Edge ring (share of the radius)", Float) = 0.18
		_ReticleColor ("Reticle color", Color) = (0.05, 0.05, 0.05, 0.95)
		_ReticleWidth ("Reticle width (share of the radius)", Float) = 0.012
	}

	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

		Pass
		{
			Name "Lens"
			Tags { "LightMode" = "UniversalForwardOnly" }

			Cull Off

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_LensTex);
			SAMPLER(sampler_LensTex);

			CBUFFER_START(UnityPerMaterial)
				float  _Vignette;
				half4  _ReticleColor;
				float  _ReticleWidth;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv         : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv         : TEXCOORD0;
			};

			Varyings vert(Attributes input)
			{
				Varyings output;
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uv         = input.uv;
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				float2 p = input.uv * 2.0 - 1.0;
				float  r = length(p);
				clip(1.0 - r);

				half3 color = SAMPLE_TEXTURE2D(_LensTex, sampler_LensTex, input.uv).rgb;

				// Dark ring at the edge of the glass
				float ring = smoothstep(1.0 - _Vignette, 1.0, r);
				color *= 1.0 - ring;

				// Crosshair, thicker toward the edge (like a duplex reticle), and a center dot
				float fine  = _ReticleWidth;
				float thick = _ReticleWidth * 3.0;
				float width = lerp(fine, thick, step(0.55, r));
				float lines = max(1.0 - smoothstep(width * 0.5, width, abs(p.x)), 1.0 - smoothstep(width * 0.5, width, abs(p.y)));
				float dot   = 1.0 - smoothstep(_ReticleWidth * 1.5, _ReticleWidth * 2.5, r);
				float gap   = smoothstep(0.08, 0.12, r);
				float mark  = max(lines * gap, dot) * _ReticleColor.a;
				color = lerp(color, _ReticleColor.rgb, mark);

				return half4(color, 1.0);
			}
			ENDHLSL
		}
	}
}
