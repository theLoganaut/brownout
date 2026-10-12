namespace TPSBR
{
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 6a: Brownout → Setup → Build HUD creates the HudSettings asset if missing, links it from GlobalSettings and reads every
	/// element's box from References/hud_rects.json into it. The HUD itself (UI/Brownout/BrownoutHud.cs) is built in code from those boxes
	/// when the gameplay view starts, so no prefab or scene is edited; re-run this after changing hud_rects.json.
	/// </summary>
	public static class BrownoutHudSetup
	{
		// CONSTANTS

		public const string SettingsPath = "Assets/Brownout/Resources/Settings/HudSettings.asset";
		public const string RectsPath    = "References/hud_rects.json";
		public const string ControlsPath = "Assets/Brownout/Resources/Settings/ControlsSettings.asset";

		[System.Serializable]
		private sealed class JsonBox
		{
			public int     id;
			public string  element;
			public float[] rect_norm;
		}

		[System.Serializable]
		private sealed class JsonBoxes
		{
			public JsonBox[] items;
		}

		// MENU

		[MenuItem("Brownout/Setup/Build HUD", priority = 6)]
		private static void BuildMenu() => Debug.Log(BuildHud());

		// PUBLIC METHODS

		public static string BuildHud()
		{
			var log = new StringBuilder();

			var settings = AssetDatabase.LoadAssetAtPath<HudSettings>(SettingsPath);
			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<HudSettings>();
				AssetDatabase.CreateAsset(settings, SettingsPath);
				log.AppendLine($"Created {SettingsPath}.");
			}

			string globalPath = AssetDatabase.FindAssets("t:GlobalSettings").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
			var    global     = globalPath != null ? AssetDatabase.LoadAssetAtPath<GlobalSettings>(globalPath) : null;
			if (global == null)
				return "GlobalSettings asset not found.";

			if (global.Hud != settings)
			{
				global.Hud = settings;
				EditorUtility.SetDirty(global);
				log.AppendLine($"Linked HudSettings from {globalPath}.");
			}

			SetUpControls(global, log);
			SetUpFonts(settings, log);

			string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, RectsPath);
			if (File.Exists(fullPath) == false)
				return log.AppendLine($"{RectsPath} not found; boxes not changed.").ToString();

			JsonBoxes parsed = JsonUtility.FromJson<JsonBoxes>("{\"items\":" + File.ReadAllText(fullPath) + "}");
			var boxes = new List<HudSettings.Box>();

			for (int i = 0; parsed != null && parsed.items != null && i < parsed.items.Length; ++i)
			{
				JsonBox item = parsed.items[i];
				if (item.rect_norm == null || item.rect_norm.Length < 4)
				{
					log.AppendLine($"Box {item.id} ({item.element}) has no rect_norm; skipped.");
					continue;
				}

				boxes.Add(new HudSettings.Box
				{
					Id      = item.id,
					Element = item.element,
					X0      = item.rect_norm[0],
					Y0      = item.rect_norm[1],
					X1      = item.rect_norm[2],
					Y1      = item.rect_norm[3],
				});
			}

			settings.Boxes = boxes.ToArray();
			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssets();

			log.AppendLine($"Read {boxes.Count} HUD boxes from {RectsPath}.");
			return log.ToString();
		}

		/// <summary>
		/// Phase 11a: TextMesh Pro font assets (dynamic SDF) for Barlow Condensed Medium and SemiBold, made once next to the font files and
		/// linked in HudSettings. Delete a font asset to remake it.
		/// </summary>
		private static void SetUpFonts(HudSettings settings, StringBuilder log)
		{
			settings.Font     = MakeFont(FontsFolder + "BarlowCondensed-Medium.ttf", log);
			settings.FontBold = MakeFont(FontsFolder + "BarlowCondensed-SemiBold.ttf", log);
			EditorUtility.SetDirty(settings);
		}

		public const string FontsFolder = "Assets/Brownout/UI/Fonts/";

		private static TMPro.TMP_FontAsset MakeFont(string fontPath, StringBuilder log)
		{
			string assetPath = Path.ChangeExtension(fontPath, null) + " SDF.asset";
			var    existing  = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(assetPath);
			if (existing != null)
				return existing;

			var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
			if (font == null)
			{
				log.AppendLine($"Font {fontPath} not found; the HUD keeps the default font.");
				return null;
			}

			TMPro.TMP_FontAsset asset = TMPro.TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, TMPro.AtlasPopulationMode.Dynamic, true);
			asset.name = Path.GetFileNameWithoutExtension(assetPath);
			AssetDatabase.CreateAsset(asset, assetPath);

			asset.material.name = asset.name + " Material";
			AssetDatabase.AddObjectToAsset(asset.material, asset);
			for (int i = 0; i < asset.atlasTextures.Length; ++i)
			{
				asset.atlasTextures[i].name = asset.name + " Atlas";
				AssetDatabase.AddObjectToAsset(asset.atlasTextures[i], asset);
			}

			// Common characters up front; anything else is added on first use
			asset.TryAddCharacters(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~°×·•–—…");
			EditorUtility.SetDirty(asset);
			AssetDatabase.SaveAssets();

			log.AppendLine($"Made {assetPath}.");
			return asset;
		}

		/// <summary>
		/// Phase 6b: makes and links ControlsSettings (mouse turn per count, default keys), and sets the sensitivity sliders to multipliers:
		/// both default to 1 (the ADS slider used to be a 0.7 scale of the hip; aiming is now scaled by the zoom in code, AgentInput).
		/// </summary>
		private static void SetUpControls(GlobalSettings global, StringBuilder log)
		{
			var controls = AssetDatabase.LoadAssetAtPath<ControlsSettings>(ControlsPath);
			if (controls == null)
			{
				controls = ScriptableObject.CreateInstance<ControlsSettings>();
				AssetDatabase.CreateAsset(controls, ControlsPath);
				log.AppendLine($"Created {ControlsPath}.");
			}

			if (global.Controls != controls)
			{
				global.Controls = controls;
				EditorUtility.SetDirty(global);
				log.AppendLine("Linked ControlsSettings from GlobalSettings.");
			}

			var serialized = new SerializedObject(global);
			SerializedProperty values = serialized.FindProperty("DefaultOptions._values");
			for (int i = 0; values != null && i < values.arraySize; ++i)
			{
				SerializedProperty value = values.GetArrayElementAtIndex(i);
				string key = value.FindPropertyRelative("Key").stringValue;
				if (key != RuntimeSettings.KEY_SENSITIVITY && key != RuntimeSettings.KEY_AIM_SENSITIVITY)
					continue;

				SerializedProperty floatValue = value.FindPropertyRelative("FloatValue");
				float min = key == RuntimeSettings.KEY_SENSITIVITY ? 0.1f : 0.25f;
				float max = key == RuntimeSettings.KEY_SENSITIVITY ? 5.0f : 2.0f;

				if (floatValue.FindPropertyRelative("Value").floatValue != 1.0f || floatValue.FindPropertyRelative("MinValue").floatValue != min || floatValue.FindPropertyRelative("MaxValue").floatValue != max)
				{
					floatValue.FindPropertyRelative("Value").floatValue    = 1.0f;
					floatValue.FindPropertyRelative("MinValue").floatValue = min;
					floatValue.FindPropertyRelative("MaxValue").floatValue = max;
					log.AppendLine($"{key} slider: default 1, {min} to {max} (a multiplier).");
				}
			}

			serialized.ApplyModifiedPropertiesWithoutUndo();
		}
	}
}
