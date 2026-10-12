namespace TPSBR.Editor
{
	using System.Collections.Generic;
	using System.IO;
	using UnityEditor;
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.Rendering.Universal;

	/// <summary>
	/// Phase 11c: Brownout → Setup → Build Feel. Makes Resources/Settings/FeelSettings.asset (kept if it exists), the particle textures
	/// and materials for the gun effects (Assets/Brownout/Art/Feel/), the sun shafts material, and adds two features to every URP renderer
	/// the quality levels use: Brownout sun shafts and URP's screen space ambient occlusion (FeelSettings AO numbers). Safe to run again.
	/// </summary>
	public static class BrownoutFeelSetup
	{
		private const string SettingsPath = "Assets/Brownout/Resources/Settings/FeelSettings.asset";
		private const string Folder       = "Assets/Brownout/Art/Feel";
		private const string ShaftShader  = "Brownout/Sun Shafts";

		[MenuItem("Brownout/Setup/Build Feel", priority = 17)]
		public static void BuildFeel()
		{
			Debug.Log(Build());
		}

		public static string Build()
		{
			var log = new System.Text.StringBuilder("[Feel] ");

			Directory.CreateDirectory(Folder);

			FeelSettings settings = AssetDatabase.LoadAssetAtPath<FeelSettings>(SettingsPath);
			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<FeelSettings>();
				AssetDatabase.CreateAsset(settings, SettingsPath);
				log.Append("made FeelSettings; ");
			}

			Texture2D puff  = Texture(Folder + "/FX_Puff.png", 64, (x, y) => SoftPuff(x, y));
			Texture2D spark = Texture(Folder + "/FX_Spark.png", 32, (x, y) => Mathf.Pow(Mathf.Clamp01(1.0f - Mathf.Sqrt(x * x + y * y)), 1.5f));

			settings.SmokeMaterial = ParticleMaterial("FX_Smoke", "Universal Render Pipeline/Particles/Unlit", puff, 0, Color.white);
			settings.SparkMaterial = ParticleMaterial("FX_Spark", "Universal Render Pipeline/Particles/Unlit", spark, 2, new Color(2.0f, 1.6f, 0.9f, 1.0f));
			settings.ChipMaterial  = ParticleMaterial("FX_Chip", "Universal Render Pipeline/Particles/Lit", null, -1, Color.white);
			settings.ShellMaterial = ParticleMaterial("FX_Shell", "Universal Render Pipeline/Particles/Lit", null, -1, new Color(0.78f, 0.6f, 0.25f, 1.0f));
			settings.ShellMaterial.SetFloat("_Metallic", 0.85f);
			settings.ShellMaterial.SetFloat("_Smoothness", 0.7f);

			settings.ShellMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
			settings.ChipMesh  = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

			Shader shaftShader = Shader.Find(ShaftShader);
			if (shaftShader != null)
			{
				settings.SunShaftMaterial = Material(Folder + "/FX_SunShafts.mat", shaftShader);
			}
			else
			{
				log.Append("SUN SHAFT SHADER MISSING; ");
			}

			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssets();

			// The renderers every quality level uses
			var renderers = new HashSet<ScriptableRendererData>();
			for (int i = 0; i < QualitySettings.names.Length; ++i)
			{
				AddRenderers(QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset, renderers);
			}
			AddRenderers(GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset, renderers);

			foreach (ScriptableRendererData renderer in renderers)
			{
				var shafts = AddFeature<SunShaftsFeature>(renderer, "Brownout Sun Shafts");
				shafts.Material = settings.SunShaftMaterial;
				EditorUtility.SetDirty(shafts);

				var ssao = AddFeature<ScreenSpaceAmbientOcclusion>(renderer, "Screen Space Ambient Occlusion");
				var so   = new SerializedObject(ssao);
				SetFloat(so, "m_Settings.Intensity", settings.AoIntensity);
				SetFloat(so, "m_Settings.Radius", settings.AoRadius);
				SetFloat(so, "m_Settings.DirectLightingStrength", 0.25f);
				SetBool(so, "m_Settings.Downsample", true);
				so.ApplyModifiedPropertiesWithoutUndo();

				EditorUtility.SetDirty(renderer);
				log.Append($"{renderer.name}: sun shafts + SSAO; ");
			}

			AssetDatabase.SaveAssets();
			return log.ToString();
		}

		// PRIVATE METHODS

		private static void AddRenderers(UniversalRenderPipelineAsset asset, HashSet<ScriptableRendererData> renderers)
		{
			if (asset == null)
				return;

			var list = new SerializedObject(asset).FindProperty("m_RendererDataList");
			for (int i = 0; list != null && i < list.arraySize; ++i)
			{
				if (list.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableRendererData data)
				{
					renderers.Add(data);
				}
			}
		}

		private static T AddFeature<T>(ScriptableRendererData renderer, string name) where T : ScriptableRendererFeature
		{
			foreach (ScriptableRendererFeature existing in renderer.rendererFeatures)
			{
				if (existing is T found)
					return found;
			}

			T feature = ScriptableObject.CreateInstance<T>();
			feature.name = name;
			AssetDatabase.AddObjectToAsset(feature, renderer);
			renderer.rendererFeatures.Add(feature);

			// Keep the feature map (local file ids) in step with the list, as the renderer's inspector does
			var so  = new SerializedObject(renderer);
			var map = so.FindProperty("m_RendererFeatureMap");
			AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
			map.arraySize += 1;
			map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
			so.ApplyModifiedPropertiesWithoutUndo();

			feature.Create();
			return feature;
		}

		private static void SetFloat(SerializedObject so, string path, float value)
		{
			SerializedProperty property = so.FindProperty(path);
			if (property != null)
			{
				property.floatValue = value;
			}
		}

		private static void SetBool(SerializedObject so, string path, bool value)
		{
			SerializedProperty property = so.FindProperty(path);
			if (property != null)
			{
				property.boolValue = value;
			}
		}

		/// <summary>blend: -1 opaque, 0 alpha, 2 additive (URP particle shader's _Blend).</summary>
		private static Material ParticleMaterial(string name, string shaderName, Texture2D texture, int blend, Color color)
		{
			Shader   shader   = Shader.Find(shaderName);
			Material material = Material($"{Folder}/{name}.mat", shader);

			material.SetColor("_BaseColor", color);
			if (texture != null)
			{
				material.SetTexture("_BaseMap", texture);
			}

			if (blend < 0)
			{
				material.SetFloat("_Surface", 0.0f);
				material.SetFloat("_SrcBlend", (float)BlendMode.One);
				material.SetFloat("_DstBlend", (float)BlendMode.Zero);
				material.SetFloat("_ZWrite", 1.0f);
				material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
				material.SetOverrideTag("RenderType", "Opaque");
				material.renderQueue = (int)RenderQueue.Geometry;
			}
			else
			{
				material.SetFloat("_Surface", 1.0f);
				material.SetFloat("_Blend", blend);
				material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
				material.SetFloat("_DstBlend", blend == 2 ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
				material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
				material.SetFloat("_DstBlendAlpha", blend == 2 ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
				material.SetFloat("_ZWrite", 0.0f);
				material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
				material.SetOverrideTag("RenderType", "Transparent");
				material.renderQueue = (int)RenderQueue.Transparent;
			}

			// Soft particles off (no fade near walls needed for tiny puffs); vertex color multiplies the base
			material.SetFloat("_SoftParticlesEnabled", 0.0f);
			material.DisableKeyword("_SOFTPARTICLES_ON");

			EditorUtility.SetDirty(material);
			return material;
		}

		private static Material Material(string path, Shader shader)
		{
			Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (material == null)
			{
				material = new Material(shader);
				AssetDatabase.CreateAsset(material, path);
			}
			else if (material.shader != shader)
			{
				material.shader = shader;
			}

			return material;
		}

		private static float SoftPuff(float x, float y)
		{
			float r     = Mathf.Sqrt(x * x + y * y);
			float noise = Mathf.PerlinNoise(x * 3.0f + 7.0f, y * 3.0f + 3.0f) * 0.5f + 0.5f;
			return Mathf.Pow(Mathf.Clamp01(1.0f - r), 1.6f) * noise;
		}

		private static Texture2D Texture(string path, int size, System.Func<float, float, float> alpha)
		{
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
			var pixels  = new Color32[size * size];
			for (int y = 0; y < size; ++y)
			{
				for (int x = 0; x < size; ++x)
				{
					float u = (x + 0.5f) / size * 2.0f - 1.0f;
					float v = (y + 0.5f) / size * 2.0f - 1.0f;
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(u, v)) * 255.0f));
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply();

			File.WriteAllBytes(path, texture.EncodeToPNG());
			Object.DestroyImmediate(texture);
			AssetDatabase.ImportAsset(path);

			var importer = (TextureImporter)AssetImporter.GetAtPath(path);
			importer.alphaIsTransparency = true;
			importer.wrapMode            = TextureWrapMode.Clamp;
			importer.mipmapEnabled       = true;
			importer.SaveAndReimport();

			return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
		}
	}
}
