namespace Plugins.Outline
{
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.Rendering.RenderGraphModule;
	using UnityEngine.Rendering.Universal;

	public sealed class OutlinePass : ScriptableRenderPass
	{
		public bool IsActive => _objects.Count > 0;

		private OutlineFeature      _feature;
		private CommandBuffer       _commandBuffer;
		private List<OutlineObject> _objects = new List<OutlineObject>();

		private static readonly List<Material>        _materials          = new List<Material>();
		private static readonly MaterialPropertyBlock _materialProperties = new MaterialPropertyBlock();

		public OutlinePass(OutlineFeature feature)
		{
			_feature = feature;
		}

		public bool HasObject(GameObject gameObject)
		{
			for (int i = 0; i < _objects.Count; ++i)
			{
				if (object.ReferenceEquals(_objects[i].GameObject, gameObject) == true)
					return true;
			}

			return false;
		}

		public void RegisterObject(GameObject gameObject, List<Renderer> renderers, IOutlineSettings settings, bool checkExisting)
		{
			if (checkExisting == true)
			{
				for (int i = 0; i < _objects.Count; ++i)
				{
					if (object.ReferenceEquals(_objects[i].GameObject, gameObject) == true)
						return;
				}
			}

			_objects.Add(new OutlineObject(gameObject, renderers, settings));
		}

		public void UnregisterObject(GameObject gameObject)
		{
			for (int i = 0; i < _objects.Count; ++i)
			{
				if (object.ReferenceEquals(_objects[i].GameObject, gameObject) == true)
				{
					_objects.RemoveAt(i);
					return;
				}
			}
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			if (_objects.Count == 0)
				return;
			if (_feature.Resources == null)
				return;

			UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
			UniversalCameraData   cameraData   = frameData.Get<UniversalCameraData>();

			RenderTextureDescriptor renderTextureDescriptor = cameraData.cameraTargetDescriptor;
			renderTextureDescriptor.shadowSamplingMode = ShadowSamplingMode.None;
			renderTextureDescriptor.depthBufferBits    = 0;
			renderTextureDescriptor.colorFormat        = RenderTextureFormat.R8;
			renderTextureDescriptor.msaaSamples        = 1;

			TextureHandle maskTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, renderTextureDescriptor, OutlineConstants.MaskTexName, false, FilterMode.Bilinear);
			TextureHandle tempTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, renderTextureDescriptor, OutlineConstants.TempTexName, false, FilterMode.Bilinear);

			// The pass switches render targets and draws renderers with a command buffer, which is not
			// allowed in a raster render pass, so it is recorded as an unsafe pass.
			using (IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass("Outline", out PassData passData))
			{
				passData.Pass         = this;
				passData.MaskTexture  = maskTexture;
				passData.TempTexture  = tempTexture;
				passData.ColorTexture = resourceData.activeColorTexture;

				builder.UseTexture(passData.MaskTexture, AccessFlags.ReadWrite);
				builder.UseTexture(passData.TempTexture, AccessFlags.ReadWrite);
				builder.UseTexture(passData.ColorTexture, AccessFlags.ReadWrite);
				builder.AllowGlobalStateModification(true);
				builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) => data.Pass.ExecutePass(data, context));
			}
		}

		private void ExecutePass(PassData data, UnsafeGraphContext context)
		{
			_commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

			for (int i = 0; i < _objects.Count; i++)
			{
				OutlineObject outlineObject = _objects[i];

				if (outlineObject.Settings.UpdateMode != EOutlineUpdateMode.None)
				{
					outlineObject.Renderers.Clear();

					if (outlineObject.Settings.UpdateMode == EOutlineUpdateMode.Self)
					{
						Renderer renderer = outlineObject.GameObject.GetComponent<Renderer>();
						if (renderer != null && outlineObject.GameObject.GetComponentInParent<IgnoreOutline>() == null)
						{
							outlineObject.Renderers.Add(renderer);
						}
					}
					else
					{
						outlineObject.GameObject.GetComponentsInChildren<Renderer>(true, outlineObject.Renderers);

						for (int j = outlineObject.Renderers.Count - 1; j >= 0; --j)
						{
							if (outlineObject.Renderers[j].GetComponentInParent<IgnoreOutline>() != null)
							{
								outlineObject.Renderers.RemoveAt(j);
							}
						}
					}
				}

				if (outlineObject.Renderers.Count == 0)
					continue;

				ClearRenderTarget(data.MaskTexture);

				RenderObject(outlineObject);
				RenderOutline(outlineObject, data.MaskTexture, data.TempTexture, data.ColorTexture);
			}

			_commandBuffer = null;
		}

		private void ClearRenderTarget(RTHandle renderTarget)
		{
			_commandBuffer.SetRenderTarget(renderTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
			_commandBuffer.ClearRenderTarget(false, true, Color.clear);
		}

		private void RenderObject(OutlineObject outlineObject)
		{
			for (int i = 0; i < outlineObject.Renderers.Count; ++i)
			{
				Renderer renderer = outlineObject.Renderers[i];

				if (renderer == null || renderer.enabled == false || renderer.isVisible == false || renderer.gameObject.activeInHierarchy == false)
					continue;

				renderer.GetSharedMaterials(_materials);

				if (_materials.Count > 0)
				{
					for (int j = 0; j < _materials.Count; ++j)
					{
						_commandBuffer.DrawRenderer(renderer, _feature.Resources.RenderMaterial, j, OutlineConstants.RenderShaderDefaultPassId);
					}
				}
				else
				{
					_commandBuffer.DrawRenderer(renderer, _feature.Resources.RenderMaterial, 0, OutlineConstants.RenderShaderDefaultPassId);
				}
			}
		}

		private void RenderOutline(OutlineObject outlineObject, RTHandle maskTexture, RTHandle tempTexture, RTHandle colorTexture)
		{
			_materialProperties.SetFloat(OutlineConstants.WidthId, outlineObject.Settings.Width);
			_materialProperties.SetColor(OutlineConstants.ColorId, outlineObject.Settings.Color);
			_materialProperties.SetFloat(OutlineConstants.IntensityId, outlineObject.Settings.Intensity);

			_commandBuffer.SetGlobalFloatArray(OutlineConstants.SamplesId, OutlineResources.GetSamples(outlineObject.Settings.Width));

			// GetTemporaryRT in the compatibility path bound the mask to the _MaskTex shader global automatically;
			// render graph textures are not bound by name, so the mask sampled by the blend pass is set explicitly.
			_commandBuffer.SetGlobalTexture(OutlineConstants.MaskTexId, maskTexture);

			_commandBuffer.SetRenderTarget(tempTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
			Blit(maskTexture, OutlineConstants.OutlineShaderHPassId, _feature.Resources.OutlineMaterial, _materialProperties);

			_commandBuffer.SetRenderTarget(colorTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
			Blit(tempTexture, OutlineConstants.OutlineShaderVPassId, _feature.Resources.OutlineMaterial, _materialProperties);
		}

		private void Blit(RTHandle source, int shaderPass, Material material, MaterialPropertyBlock materialProperties)
		{
			_commandBuffer.SetGlobalTexture(OutlineConstants.MainTexId, source);
			_commandBuffer.DrawProcedural(Matrix4x4.identity, material, shaderPass, MeshTopology.Triangles, 3, 1, materialProperties);
		}

		private sealed class PassData
		{
			public OutlinePass   Pass;
			public TextureHandle MaskTexture;
			public TextureHandle TempTexture;
			public TextureHandle ColorTexture;
		}
	}
}
