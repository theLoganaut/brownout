namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.Rendering.RenderGraphModule;
	using UnityEngine.Rendering.RenderGraphModule.Util;
	using UnityEngine.Rendering.Universal;

	/// <summary>
	/// Phase 11c: the sun shafts pass (Brownout/Sun Shafts) on the URP renderers, added by Brownout → Setup → Build Feel. Runs before
	/// post-processing on game cameras that draw the world (not the viewmodel overlay), and only while ScreenLook turns it on.
	/// </summary>
	public sealed class SunShaftsFeature : ScriptableRendererFeature
	{
		// PUBLIC MEMBERS

		public Material Material;

		// PRIVATE MEMBERS

		private SunShaftsPass _pass;

		// ScriptableRendererFeature INTERFACE

		public override void Create()
		{
			_pass = new SunShaftsPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (Material == null || Shader.GetGlobalFloat(ScreenLook.SunShaftsID) <= 0.0f)
				return;

			_pass.Material = Material;
			_pass.ConfigureInput(ScriptableRenderPassInput.Depth);
			renderer.EnqueuePass(_pass);
		}

		// HELPERS

		private sealed class SunShaftsPass : ScriptableRenderPass
		{
			public Material Material;

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				UniversalCameraData   cameraData   = frameData.Get<UniversalCameraData>();
				UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

				if (cameraData.renderType != CameraRenderType.Base || cameraData.cameraType != CameraType.Game || resourceData.isActiveTargetBackBuffer == true)
					return;

				TextureHandle source = resourceData.activeColorTexture;
				TextureDesc   desc   = renderGraph.GetTextureDesc(source);
				desc.name            = "_BrownoutSunShafts";
				desc.clearBuffer     = false;

				TextureHandle destination = renderGraph.CreateTexture(desc);
				renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, Material, 0), "Brownout Sun Shafts");
				resourceData.cameraColor = destination;
			}
		}
	}
}
