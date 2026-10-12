namespace TPSBR
{
	using System.Collections.Generic;
	using System.Linq;
	using UnityEditor;
	using UnityEditor.SceneManagement;
	using UnityEngine;
	using UnityEngine.Rendering;

	/// <summary>
	/// Phase 2 setup: the MovementSettings data asset and the Movement Gym test scene (Movement page, "Movement test scene").
	/// Everything here is rebuilt by running the menu again, so the scene is never hand-edited.
	/// </summary>
	public static class BrownoutMovementSetup
	{
		// CONSTANTS

		public const string SettingsPath   = "Assets/Brownout/Resources/Settings/MovementSettings.asset";
		public const string TemplateScene  = "Assets/Scenes/Template.unity";
		public const string GymScene       = "Assets/Scenes/MovementGym.unity";
		public const string GymMapID       = "Map.MovementGym";
		public const string MaterialFolder = "Assets/Brownout/Materials/MovementGym";

		/// <summary>Gun range (phase 3a): one lane per distance, side by side, firing line at z = -100, targets down range along +Z.</summary>
		public static readonly float[] RangeDistances = { 25.0f, 50.0f, 100.0f, 200.0f };
		public const float RangeFirstLaneX = 48.0f;
		public const float RangeLaneSpacing = 8.0f;
		public const float RangeFiringLineZ = -100.0f;

		/// <summary>Where to stand to shoot the target at this distance (feet on the firing line, centered on the lane).</summary>
		public static Vector3 GetRangeLanePosition(float distance)
		{
			int lane = System.Array.IndexOf(RangeDistances, distance);
			return new Vector3(RangeFirstLaneX + Mathf.Max(0, lane) * RangeLaneSpacing, 0.0f, RangeFiringLineZ);
		}
		// Phase 10: from the MovementSettings asset (1.80 m for the 2 m soldier)
		private static float StandEyeHeight => AssetDatabase.LoadAssetAtPath<MovementSettings>("Assets/Brownout/Resources/Settings/MovementSettings.asset") is MovementSettings settings ? settings.StandEyeHeight : 1.80f;

		private const string MENU = "Brownout/Setup/";

		// MENU

		[MenuItem(MENU + "Create Movement Settings", priority = 1)]
		private static void CreateMovementSettingsMenu()
		{
			Debug.Log(CreateMovementSettings());
		}

		[MenuItem(MENU + "Build Movement Gym", priority = 2)]
		private static void BuildMovementGymMenu()
		{
			if (System.IO.File.Exists(GymScene) == true && EditorUtility.DisplayDialog("Movement Gym", "MovementGym.unity exists. Rebuild it from the Template scene?", "Rebuild", "Cancel") == false)
				return;

			Debug.Log(BuildMovementGym());
		}

		// PUBLIC METHODS

		/// <summary>
		/// Creates the MovementSettings asset (defaults = Movement page values) if missing and links it from GlobalSettings.
		/// </summary>
		public static string CreateMovementSettings()
		{
			var settings = AssetDatabase.LoadAssetAtPath<MovementSettings>(SettingsPath);
			bool created = false;

			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<MovementSettings>();
				AssetDatabase.CreateAsset(settings, SettingsPath);
				created = true;
			}

			string globalPath = AssetDatabase.FindAssets("t:GlobalSettings").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
			var    global     = globalPath != null ? AssetDatabase.LoadAssetAtPath<GlobalSettings>(globalPath) : null;
			if (global == null)
				return "GlobalSettings asset not found.";

			if (global.Movement != settings)
			{
				global.Movement = settings;
				EditorUtility.SetDirty(global);
			}

			AssetDatabase.SaveAssets();

			return $"{(created == true ? "Created" : "Found")} {SettingsPath}, linked from {globalPath}.";
		}

		/// <summary>
		/// Copies the Template scene to MovementGym.unity, builds the gym geometry in it, sets 2 peers,
		/// and registers the scene in Build Profiles and the map settings (approved by Logan, 4 Oct).
		/// </summary>
		public static string BuildMovementGym()
		{
			if (EditorApplication.isPlaying == true)
				return "Stop Play mode first.";

			EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

			if (AssetDatabase.CopyAsset(TemplateScene, GymScene) == false)
				return $"Couldn't copy {TemplateScene} to {GymScene}.";

			var scene = EditorSceneManager.OpenScene(GymScene, OpenSceneMode.Single);

			var root = new GameObject("MovementGym").transform;
			var materials = new Materials();

			BuildSpeedStrip(root, materials);
			BuildRamps(root, materials);
			BuildMantleWalls(root, materials);
			BuildDoorAndWindow(root, materials);
			BuildWater(root, materials);
			BuildGunRange(root, materials);
			PlaceSpawnPoints();
			SetExtraPeers(1);

			EditorSceneManager.MarkSceneDirty(scene);
			EditorSceneManager.SaveScene(scene);

			string registered = RegisterScene(GymScene, GymMapID, "Movement Gym", "Phase 2 test scene: speed strip, ramps, mantle walls, door and window frames, water strip.");

			return $"Built {GymScene}. {registered}";
		}

		// PRIVATE METHODS

		private static void BuildSpeedStrip(Transform root, Materials materials)
		{
			// 100 m along +Z from z = -100 to z = 0, 4 m wide, a marker every 10 m
			var strip = Group(root, "SpeedStrip");

			Visual(strip, "Strip", new Vector3(0.0f, 0.005f, -50.0f), new Vector3(4.0f, 0.01f, 100.0f), materials.Strip);

			for (int i = 0; i <= 10; ++i)
			{
				float     z        = -100.0f + i * 10.0f;
				Material  material = i % 5 == 0 ? materials.MarkerMajor : materials.Marker;

				Visual(strip, $"Line {i * 10} m", new Vector3(0.0f, 0.01f, z), new Vector3(4.0f, 0.012f, 0.15f), material);
				Solid (strip, $"Post {i * 10} m L", new Vector3(-2.6f, 0.5f, z), new Vector3(0.15f, 1.0f, 0.15f), material);
				Solid (strip, $"Post {i * 10} m R", new Vector3( 2.6f, 0.5f, z), new Vector3(0.15f, 1.0f, 0.15f), material);
			}
		}

		private static void BuildRamps(Transform root, Materials materials)
		{
			// Each ramp: 40 m flat run-up from the spawn area, 30 m up, 10 m flat top, 30 m down. Walkable both ways
			var ramps = Group(root, "Ramps");

			float[] angles = { 5.0f, 10.0f, 15.0f };
			float[] xs     = { 14.0f, 24.0f, 34.0f };

			for (int i = 0; i < angles.Length; ++i)
			{
				float angle  = angles[i];
				float x      = xs[i];
				float length = 30.0f;
				float rise   = length * Mathf.Tan(angle * Mathf.Deg2Rad);
				float startZ = -60.0f;
				float topZ   = startZ + length;

				var ramp = Group(ramps, $"Ramp {angle:F0} deg");

				Slope(ramp, "Up",   new Vector3(x, 0.0f, startZ),          new Vector3(x, rise, topZ),                   6.0f, materials.Ramp);
				Solid(ramp, "Top",  new Vector3(x, rise * 0.5f, topZ + 5.0f), new Vector3(6.0f, rise, 10.0f),            materials.Ramp);
				Slope(ramp, "Down", new Vector3(x, rise, topZ + 10.0f),    new Vector3(x, 0.0f, topZ + 10.0f + length),  6.0f, materials.Ramp);
				Solid(ramp, "Sign", new Vector3(x - 3.4f, 0.5f + i * 0.5f, startZ - 1.0f), new Vector3(0.3f, 1.0f + i * 1.0f, 0.3f), materials.MarkerMajor);
			}
		}

		private static void BuildMantleWalls(Transform root, Materials materials)
		{
			// Blocks 3 m wide and 2 m deep, so you stand on top after a mantle. The last one must fail (Logan, 9 Oct: jump 2.4 m, mantle 4.0 m,
			// so jump-mantle 6.4 m; both doubled from 4 Oct's 1.2 / 2.0)
			var walls = Group(root, "MantleWalls");

			MovementSettings settings = AssetDatabase.LoadAssetAtPath<MovementSettings>("Assets/Brownout/Resources/Settings/MovementSettings.asset");
			float standMantle = settings.MantleMaxHeight + 0.05f;
			float jumpMantle  = settings.MantleMaxHeight + settings.JumpHeight + 0.05f;

			float[] heights = { 1.0f, 2.0f, 4.0f, 5.2f, 6.4f, 6.8f };

			for (int i = 0; i < heights.Length; ++i)
			{
				float h = heights[i];
				float x = -12.0f - i * 6.0f;

				Material material = h > jumpMantle ? materials.Fail : (h > standMantle ? materials.JumpMantle : materials.Mantle);
				Solid(walls, $"Wall {h:F1} m", new Vector3(x, h * 0.5f, -89.0f), new Vector3(3.0f, h, 2.0f), material);
			}
		}

		private static void BuildDoorAndWindow(Transform root, Materials materials)
		{
			// Movement page metrics: door 0.75 x 2.29 m; window 0.75 x 0.92 m with the sill at 1.0 m. Walls 0.2 m thick, 3.0 m tall, 4 m wide
			var frames = Group(root, "Frames");

			const float wallHeight = 3.0f;
			const float wallWidth  = 4.0f;
			const float thickness  = 0.2f;
			const float openWidth  = 0.75f;
			const float side       = (wallWidth - openWidth) * 0.5f;
			const float z          = -90.0f;

			// Door
			{
				float x          = -46.0f;
				float doorHeight = 2.29f;
				var   door       = Group(frames, "Door 0.75 x 2.29");

				Solid(door, "Left",  new Vector3(x - (openWidth + side) * 0.5f, wallHeight * 0.5f, z), new Vector3(side, wallHeight, thickness), materials.Frame);
				Solid(door, "Right", new Vector3(x + (openWidth + side) * 0.5f, wallHeight * 0.5f, z), new Vector3(side, wallHeight, thickness), materials.Frame);
				Solid(door, "Top",   new Vector3(x, (doorHeight + wallHeight) * 0.5f, z), new Vector3(openWidth, wallHeight - doorHeight, thickness), materials.Frame);
			}

			// Window
			{
				float x          = -54.0f;
				float sill       = 1.0f;
				float openHeight = 0.92f;
				float lintel     = sill + openHeight;
				var   window     = Group(frames, "Window 0.75 x 0.92, sill 1.0");

				Solid(window, "Left",   new Vector3(x - (openWidth + side) * 0.5f, wallHeight * 0.5f, z), new Vector3(side, wallHeight, thickness), materials.Frame);
				Solid(window, "Right",  new Vector3(x + (openWidth + side) * 0.5f, wallHeight * 0.5f, z), new Vector3(side, wallHeight, thickness), materials.Frame);
				Solid(window, "Sill",   new Vector3(x, sill * 0.5f, z), new Vector3(openWidth, sill, thickness), materials.Frame);
				Solid(window, "Lintel", new Vector3(x, (lintel + wallHeight) * 0.5f, z), new Vector3(openWidth, wallHeight - lintel, thickness), materials.Frame);
			}
		}

		private static void BuildWater(Transform root, Materials materials)
		{
			// A 4 m strip of water. The blocker is on the AgentKCC layer: the KCC collides with it, bullets don't
			var water = Group(root, "Water");

			Vector3 center = new Vector3(-24.0f, 0.0f, -70.0f);
			Vector3 size   = new Vector3(20.0f, 0.0f, 4.0f);

			Visual(water, "Surface", center + Vector3.up * 0.02f, new Vector3(size.x, 0.02f, size.z), materials.Water);

			var blocker = new GameObject("PlayerBlocker");
			blocker.transform.SetParent(water, false);
			blocker.transform.localPosition = center + Vector3.up * 1.5f;
			blocker.layer = LayerMask.NameToLayer("AgentKCC");
			blocker.isStatic = true;

			var box = blocker.AddComponent<BoxCollider>();
			box.size = new Vector3(size.x, 3.0f, size.z);
		}

		/// <summary>
		/// Phase 3a gun range: four lanes side by side with targets 25, 50, 100 and 200 m down range, each a 10 cm grid
		/// with a red line at standing eye height (MovementSettings), so a level shot's drop reads straight off the wall.
		/// </summary>
		private static void BuildGunRange(Transform root, Materials materials)
		{
			var range = Group(root, "GunRange");

			Vector3 wallSize = new Vector3(6.0f, 4.0f, 0.3f);
			float   lineFrom = RangeFirstLaneX - RangeLaneSpacing * 0.5f;
			float   lineTo   = RangeFirstLaneX + RangeLaneSpacing * (RangeDistances.Length - 0.5f);

			Visual(range, "Firing Line", new Vector3((lineFrom + lineTo) * 0.5f, 0.01f, RangeFiringLineZ), new Vector3(lineTo - lineFrom, 0.012f, 0.15f), materials.MarkerMajor);

			foreach (float distance in RangeDistances)
			{
				Vector3 lane = GetRangeLanePosition(distance);

				Visual(range, $"Lane {distance:F0} m", lane + new Vector3(0.0f, 0.004f, distance * 0.5f), new Vector3(0.2f, 0.008f, distance), materials.Marker);

				// The wall's front face sits exactly at the distance
				Solid(range, $"Target {distance:F0} m", lane + new Vector3(0.0f, wallSize.y * 0.5f, distance + wallSize.z * 0.5f), wallSize, materials.Grid);

				float face = distance - 0.005f;
				Visual(range, $"Eye Line {distance:F0} m", lane + new Vector3(0.0f, StandEyeHeight, face),   new Vector3(wallSize.x, 0.01f, 0.01f), materials.Fail);
				Visual(range, $"Center {distance:F0} m",   lane + new Vector3(0.0f, wallSize.y * 0.5f, face), new Vector3(0.01f, wallSize.y, 0.01f), materials.Fail);

				// Posts by the firing line: one for 25 m, two for 50 m, ...
				int posts = System.Array.IndexOf(RangeDistances, distance) + 1;
				for (int i = 0; i < posts; ++i)
				{
					Solid(range, $"Lane Post {distance:F0} m {i + 1}", lane + new Vector3(-2.0f + i * 0.4f, 0.4f, -1.0f), new Vector3(0.15f, 0.8f, 0.15f), materials.MarkerMajor);
				}
			}
		}

		private static void PlaceSpawnPoints()
		{
			var spawnPoints = Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
			Vector3[] positions = { new Vector3(-1.0f, 0.0f, -106.0f), new Vector3(1.0f, 0.0f, -106.0f), new Vector3(-1.0f, 0.0f, -110.0f), new Vector3(1.0f, 0.0f, -110.0f) };

			for (int i = 0; i < spawnPoints.Length; ++i)
			{
				Transform spawn = spawnPoints[i].transform;
				spawn.position = positions[i % positions.Length];
				spawn.rotation = Quaternion.identity; // Facing +Z, down the strip
			}
		}

		internal static void SetExtraPeers(int extraPeers)
		{
			var manager = Object.FindFirstObjectByType<StandaloneManager>(FindObjectsInactive.Include);
			if (manager == null)
				return;

			var serialized = new SerializedObject(manager);
			var property   = serialized.FindProperty("_defaultConfiguration.ExtraPeers");
			if (property != null)
			{
				property.intValue = extraPeers;
				serialized.ApplyModifiedPropertiesWithoutUndo();
			}
		}

		/// <summary>Adds a test scene to Build Profiles and the map settings, so the menu and Multipeer can start it.</summary>
		internal static string RegisterScene(string scenePath, string mapID, string displayName, string description)
		{
			var result = new List<string>();

			// Build Profiles scene list
			var scenes = EditorBuildSettings.scenes.ToList();
			if (scenes.Any(t => t.path == scenePath) == false)
			{
				scenes.Add(new EditorBuildSettingsScene(scenePath, true));
				EditorBuildSettings.scenes = scenes.ToArray();
				result.Add("Added to Build Profiles.");
			}

			// Map settings, so the menu can start it
			string mapPath = AssetDatabase.FindAssets("t:MapSettings").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
			var    maps    = mapPath != null ? AssetDatabase.LoadAssetAtPath<MapSettings>(mapPath) : null;
			if (maps != null)
			{
				var serialized = new SerializedObject(maps);
				var array      = serialized.FindProperty("_maps");
				bool exists    = false;

				for (int i = 0; i < array.arraySize; ++i)
				{
					if (array.GetArrayElementAtIndex(i).FindPropertyRelative("_id").stringValue == mapID)
					{
						exists = true;
						break;
					}
				}

				if (exists == false)
				{
					array.InsertArrayElementAtIndex(array.arraySize);
					var entry = array.GetArrayElementAtIndex(array.arraySize - 1);
					entry.FindPropertyRelative("_id").stringValue              = mapID;
					entry.FindPropertyRelative("_scenePath").stringValue       = scenePath.Substring("Assets/".Length, scenePath.Length - "Assets/".Length - ".unity".Length);
					entry.FindPropertyRelative("_displayName").stringValue     = displayName;
					entry.FindPropertyRelative("_description").stringValue     = description;
					entry.FindPropertyRelative("_image").objectReferenceValue  = null;
					entry.FindPropertyRelative("_recommendedPlayers").intValue = 2;
					entry.FindPropertyRelative("_maxPlayers").intValue         = 4;
					entry.FindPropertyRelative("_showInMapSelection").boolValue = true;
					serialized.ApplyModifiedPropertiesWithoutUndo();
					AssetDatabase.SaveAssets();
					result.Add("Added to map settings.");
				}
			}

			return result.Count > 0 ? string.Join(" ", result) : "Already registered.";
		}

		internal static Transform Group(Transform parent, string groupName)
		{
			var group = new GameObject(groupName).transform;
			group.SetParent(parent, false);
			return group;
		}

		internal static Transform Solid(Transform parent, string objectName, Vector3 center, Vector3 size, Material material)
		{
			var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
			block.name = objectName;
			block.transform.SetParent(parent, false);
			block.transform.localPosition = center;
			block.transform.localScale    = size;
			block.layer    = LayerMask.NameToLayer("Default");
			block.isStatic = true;
			block.GetComponent<MeshRenderer>().sharedMaterial = material;
			return block.transform;
		}

		internal static void Visual(Transform parent, string objectName, Vector3 center, Vector3 size, Material material)
		{
			var block = Solid(parent, objectName, center, size, material);
			Object.DestroyImmediate(block.GetComponent<Collider>());
			block.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
		}

		/// <summary>
		/// A slab whose top surface runs from one point to another (1 m thick, extended a little under the ground at both ends).
		/// </summary>
		private static void Slope(Transform parent, string objectName, Vector3 from, Vector3 to, float width, Material material)
		{
			Vector3    direction = to - from;
			float      length    = direction.magnitude;
			Quaternion rotation  = Quaternion.LookRotation(direction.normalized, Vector3.up);
			Vector3    normal    = rotation * Vector3.up;
			Vector3    center    = (from + to) * 0.5f - normal * 0.5f;

			var slab = Solid(parent, objectName, center, new Vector3(width, 1.0f, length + 0.2f), material);
			slab.localRotation = rotation;
		}

		// HELPERS

		internal sealed class Materials
		{
			public readonly Material Strip;
			public readonly Material Marker;
			public readonly Material MarkerMajor;
			public readonly Material Ramp;
			public readonly Material Mantle;
			public readonly Material JumpMantle;
			public readonly Material Fail;
			public readonly Material Frame;
			public readonly Material Water;
			public readonly Material Grid;

			public Materials()
			{
				if (AssetDatabase.IsValidFolder(MaterialFolder) == false)
				{
					AssetDatabase.CreateFolder("Assets/Brownout/Materials", "MovementGym");
				}

				Strip       = Get("Strip",       new Color(0.30f, 0.30f, 0.32f));
				Marker      = Get("Marker",      new Color(0.90f, 0.90f, 0.90f));
				MarkerMajor = Get("MarkerMajor", new Color(0.95f, 0.75f, 0.15f));
				Ramp        = Get("Ramp",        new Color(0.45f, 0.50f, 0.58f));
				Mantle      = Get("Mantle",      new Color(0.35f, 0.62f, 0.35f));
				JumpMantle  = Get("JumpMantle",  new Color(0.85f, 0.60f, 0.20f));
				Fail        = Get("Fail",        new Color(0.75f, 0.25f, 0.22f));
				Frame       = Get("Frame",       new Color(0.55f, 0.45f, 0.35f));
				Water       = Get("Water",       new Color(0.15f, 0.40f, 0.75f));
				Grid        = GetGrid();
			}

			/// <summary>
			/// White with a dark line every 10 cm on a 6 x 4 m wall (the cube's face UVs span the face, so tiling = size / 0.1 m).
			/// </summary>
			private static Material GetGrid()
			{
				string texturePath = $"{MaterialFolder}/Grid10cm.png";
				if (System.IO.File.Exists(texturePath) == false)
				{
					const int size = 64;
					var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
					for (int y = 0; y < size; ++y)
					{
						for (int x = 0; x < size; ++x)
						{
							bool line = x < 2 || y < 2;
							texture.SetPixel(x, y, line == true ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.92f, 0.92f, 0.90f));
						}
					}

					System.IO.File.WriteAllBytes(texturePath, texture.EncodeToPNG());
					Object.DestroyImmediate(texture);
					AssetDatabase.ImportAsset(texturePath);

					var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
					importer.wrapMode   = TextureWrapMode.Repeat;
					importer.filterMode = FilterMode.Point;
					importer.mipmapEnabled = true;
					importer.SaveAndReimport();
				}

				Material material = Get("Grid", Color.white);
				material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
				material.SetTextureScale("_BaseMap", new Vector2(60.0f, 40.0f));
				EditorUtility.SetDirty(material);
				return material;
			}

			private static Material Get(string materialName, Color color)
			{
				string path     = $"{MaterialFolder}/{materialName}.mat";
				var    material = AssetDatabase.LoadAssetAtPath<Material>(path);

				if (material == null)
				{
					material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
					AssetDatabase.CreateAsset(material, path);
				}

				material.SetColor("_BaseColor", color);
				material.SetFloat("_Smoothness", 0.2f);
				EditorUtility.SetDirty(material);
				return material;
			}
		}
	}
}
