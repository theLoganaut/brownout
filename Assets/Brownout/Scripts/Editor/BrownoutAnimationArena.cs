namespace TPSBR
{
	using UnityEditor;
	using UnityEditor.SceneManagement;
	using UnityEngine;

	/// <summary>
	/// Phase 10 Animation Arena: a scene for trying the soldier's animations by hand (Logan, 8 Oct). Copies the Template scene and
	/// builds pads (gun, no gun, heal, grenade, down, get up, drop; AnimationArena.cs runs them), mantle and vault walls, a window,
	/// a slide lane with a slope, and a platform with a ramp for falls and landings. F7 in Play cycles the camera behind / in front / first person.
	/// </summary>
	public static class BrownoutAnimationArena
	{
		// CONSTANTS

		public const string ArenaScene = "Assets/Scenes/AnimationArena.unity";
		public const string ArenaMapID = "AnimationArena";

		private const string MENU = "Brownout/Setup/";

		// MENU

		[MenuItem(MENU + "Build Animation Arena", priority = 3)]
		private static void BuildMenu()
		{
			if (System.IO.File.Exists(ArenaScene) == true && EditorUtility.DisplayDialog("Animation Arena", "AnimationArena.unity exists. Rebuild it from the Template scene?", "Rebuild", "Cancel") == false)
				return;

			Debug.Log(BuildAnimationArena());
		}

		// PUBLIC METHODS

		public static string BuildAnimationArena()
		{
			if (EditorApplication.isPlaying == true)
				return "Stop Play mode first.";

			EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

			if (System.IO.File.Exists(ArenaScene) == true)
			{
				AssetDatabase.DeleteAsset(ArenaScene);
			}

			if (AssetDatabase.CopyAsset(BrownoutMovementSetup.TemplateScene, ArenaScene) == false)
				return $"Couldn't copy {BrownoutMovementSetup.TemplateScene} to {ArenaScene}.";

			var scene     = EditorSceneManager.OpenScene(ArenaScene, OpenSceneMode.Single);
			var root      = new GameObject("AnimationArena").transform;
			var materials = new BrownoutMovementSetup.Materials();

			BuildPads(root, materials);
			BuildWalls(root, materials);
			BuildSlideLane(root, materials);
			BuildFallPlatform(root, materials);
			BuildScopeBoards(root, materials);
			Label(root,"Keys", new Vector3(0.0f, 3.2f, 3.0f), "F7: camera behind / front / first person\nCtrl+Shift+1 / 2: switch player\nStep on a pad to use it", 0.25f);

			PlaceSpawnPoints();
			BrownoutMovementSetup.SetExtraPeers(1);

			EditorSceneManager.MarkSceneDirty(scene);
			EditorSceneManager.SaveScene(scene);

			string registered = BrownoutMovementSetup.RegisterScene(ArenaScene, ArenaMapID, "Animation Arena", "Phase 10 test scene: pads for each move, mantle walls, slide lane, fall platform.");
			return $"Built {ArenaScene}. {registered}";
		}

		// PRIVATE METHODS

		/// <summary>A row of 3 x 3 m pads 8 m ahead of the spawn, 4 m apart.</summary>
		private static void BuildPads(Transform root, BrownoutMovementSetup.Materials materials)
		{
			var group = BrownoutMovementSetup.Group(root, "Pads");

			(EArenaPad action, string label, Material material)[] pads =
			{
				(EArenaPad.Gun,     "GUN\n(Icarus)",            materials.Mantle),
				(EArenaPad.NoGun,   "NO GUN",                   materials.Frame),
				(EArenaPad.Heal,    "HEAL\n(Q to heal)",        materials.Water),
				(EArenaPad.Grenade, "GRENADE\n(E to throw)",    materials.JumpMantle),
				(EArenaPad.Down,    "DOWN ME",                  materials.Fail),
				(EArenaPad.GetUp,   "GET UP\n(crawl on)",       materials.Mantle),
				(EArenaPad.Drop,    "DROP\n(400 m)",            materials.MarkerMajor),
				(EArenaPad.Gear,    "GEAR\n(armor, backpack)",  materials.Water),
				// Phase 11a HUD Gallery
				(EArenaPad.Items,   "ITEMS\n(every item ahead)",  materials.MarkerMajor),
				(EArenaPad.FullKit, "FULL KIT\n(Tab inventory)",  materials.Mantle),
				(EArenaPad.Scope,   "SCOPE\n(next scope; boards\nbehind the spawn)", materials.JumpMantle),
			};

			var arena = group.gameObject.AddComponent<AnimationArena>();
			arena.Pads = new AnimationArena.Pad[pads.Length];

			for (int i = 0; i < pads.Length; ++i)
			{
				Vector3 center = new Vector3(-12.0f + i * 4.0f, 0.0f, 8.0f);

				BrownoutMovementSetup.Visual(group, $"Pad {pads[i].action}", center + Vector3.up * 0.02f, new Vector3(3.0f, 0.04f, 3.0f), pads[i].material);
				Label(group, $"Label {pads[i].action}", center + new Vector3(0.0f, 2.2f, 1.5f), pads[i].label, 0.3f);

				arena.Pads[i] = new AnimationArena.Pad { Action = pads[i].action, Center = center, Size = new Vector2(3.0f, 3.0f) };
			}

			// Phase 11e: the Recoil pad sits 25 m in front of the 25 m board (behind the spawn, to the left)
			Vector3 recoilCenter = new Vector3(-6.0f, 0.0f, 0.0f);
			BrownoutMovementSetup.Visual(group, "Pad Recoil", recoilCenter + Vector3.up * 0.02f, new Vector3(3.0f, 0.04f, 3.0f), materials.Fail);
			Label(group, "Label Recoil", recoilCenter + new Vector3(0.0f, 2.2f, 1.5f), "RECOIL\n(next gun; faces\nthe 25 m board)", 0.3f);
			arena.RecoilTarget = new Vector3(-6.0f, 1.8f + (1.0f + 25.0f / 100.0f) * 0.5f, -25.0f); // BuildScopeBoards' 25 m board center

			System.Array.Resize(ref arena.Pads, arena.Pads.Length + 1);
			arena.Pads[arena.Pads.Length - 1] = new AnimationArena.Pad { Action = EArenaPad.Recoil, Center = recoilCenter, Size = new Vector2(3.0f, 3.0f) };
		}

		/// <summary>
		/// Walls 3 m wide and 2 m deep at z = 22: vaults up to 1.3 m (MovesLayer), mantles above up to MovementSettings.MantleMaxHeight
		/// (4.0 m since 9 Oct), higher ones need a jump (up to jump height + mantle height, 6.4 m). A window (sill 1.0 m, 0.75 x 0.92) and a door to the side.
		/// </summary>
		private static void BuildWalls(Transform root, BrownoutMovementSetup.Materials materials)
		{
			var group = BrownoutMovementSetup.Group(root, "Walls");

			MovementSettings settings = AssetDatabase.LoadAssetAtPath<MovementSettings>("Assets/Brownout/Resources/Settings/MovementSettings.asset");
			float standMantle = settings.MantleMaxHeight + 0.05f;

			float[] heights = { 0.8f, 1.3f, 2.0f, 3.2f, 4.0f, 5.2f, 6.4f };

			for (int i = 0; i < heights.Length; ++i)
			{
				float    h        = heights[i];
				float    x        = -15.0f + i * 5.0f;
				Material material = h > standMantle ? materials.JumpMantle : (h > 1.35f ? materials.Mantle : materials.Ramp);

				BrownoutMovementSetup.Solid(group, $"Wall {h:F1} m", new Vector3(x, h * 0.5f, 23.0f), new Vector3(3.0f, h, 2.0f), material);
				Label(group, $"Label {h:F1} m", new Vector3(x, h + 0.6f, 21.9f), $"{h:F1} m{(h <= 1.3f ? "\nvault" : (h > standMantle ? "\njump + mantle" : "\nmantle"))}", 0.5f);
			}

			const float wallHeight = 3.0f;
			const float thickness  = 0.2f;
			const float openWidth  = 0.75f;
			const float side       = 1.6f;
			const float z          = 23.0f;

			// Window
			{
				float x      = 23.0f;
				float sill   = 1.0f;
				float lintel = sill + 0.92f;
				var   window = BrownoutMovementSetup.Group(group, "Window");

				BrownoutMovementSetup.Solid(window, "Left",   new Vector3(x - (openWidth + side) * 0.5f, wallHeight * 0.5f, z), new Vector3(side, wallHeight, thickness), materials.Frame);
				BrownoutMovementSetup.Solid(window, "Right",  new Vector3(x + (openWidth + side) * 0.5f, wallHeight * 0.5f, z), new Vector3(side, wallHeight, thickness), materials.Frame);
				BrownoutMovementSetup.Solid(window, "Sill",   new Vector3(x, sill * 0.5f, z), new Vector3(openWidth, sill, thickness), materials.Frame);
				BrownoutMovementSetup.Solid(window, "Lintel", new Vector3(x, (lintel + wallHeight) * 0.5f, z), new Vector3(openWidth, wallHeight - lintel, thickness), materials.Frame);
				Label(group, "Label Window", new Vector3(x, 3.5f, z - 0.2f), "window\nsill 1.0 m", 0.5f);
			}

			// Door
			{
				float x    = 29.0f;
				float top  = 2.29f;
				var   door = BrownoutMovementSetup.Group(group, "Door");

				BrownoutMovementSetup.Solid(door, "Left",  new Vector3(x - (openWidth + side) * 0.5f, wallHeight * 0.5f, z), new Vector3(side, wallHeight, thickness), materials.Frame);
				BrownoutMovementSetup.Solid(door, "Right", new Vector3(x + (openWidth + side) * 0.5f, wallHeight * 0.5f, z), new Vector3(side, wallHeight, thickness), materials.Frame);
				BrownoutMovementSetup.Solid(door, "Top",   new Vector3(x, (top + wallHeight) * 0.5f, z), new Vector3(openWidth, wallHeight - top, thickness), materials.Frame);
				Label(group, "Label Door", new Vector3(x, 3.5f, z - 0.2f), "door", 0.5f);
			}
		}

		/// <summary>A 50 m lane to the right (sprint, slide, dash), then a 10° slope down for long slides.</summary>
		private static void BuildSlideLane(Transform root, BrownoutMovementSetup.Materials materials)
		{
			var group = BrownoutMovementSetup.Group(root, "SlideLane");
			const float x = 40.0f;

			BrownoutMovementSetup.Visual(group, "Lane", new Vector3(x, 0.005f, 25.0f), new Vector3(4.0f, 0.01f, 50.0f), materials.Strip);
			for (int i = 0; i <= 5; ++i)
			{
				BrownoutMovementSetup.Visual(group, $"Line {i * 10} m", new Vector3(x, 0.01f, i * 10.0f), new Vector3(4.0f, 0.012f, 0.15f), i % 5 == 0 ? materials.MarkerMajor : materials.Marker);
			}
			Label(group, "Label Lane", new Vector3(x, 2.5f, -1.0f), "sprint / slide / dash\n(slope at the far end)", 0.3f);

			// Up a 10° slope from the lane's end, a flat top, then back down to the lane: slide down it toward the spawn
			float length = 25.0f;
			float rise   = length * Mathf.Tan(10.0f * Mathf.Deg2Rad);
			float x2     = x + 6.0f;

			BrownoutMovementSetup.Solid(group, "Slope Top", new Vector3(x2, rise * 0.5f, 60.0f), new Vector3(6.0f, rise, 10.0f), materials.Ramp);
			Slab(group, "Slope Down", new Vector3(x2, rise, 55.0f), new Vector3(x2, 0.0f, 55.0f - length), 6.0f, materials.Ramp);
			Slab(group, "Slope Up",   new Vector3(x2, rise, 65.0f), new Vector3(x2, 0.0f, 65.0f + length), 6.0f, materials.Ramp);
		}

		/// <summary>To the left: a ramp up to a 6 m platform with a 3 m step, for falls and landings.</summary>
		private static void BuildFallPlatform(Transform root, BrownoutMovementSetup.Materials materials)
		{
			var group = BrownoutMovementSetup.Group(root, "FallPlatform");
			const float x = -32.0f;

			float length = 6.0f / Mathf.Tan(15.0f * Mathf.Deg2Rad);

			Slab(group, "Ramp", new Vector3(x, 0.0f, 0.0f), new Vector3(x, 6.0f, length), 4.0f, materials.Ramp);
			BrownoutMovementSetup.Solid(group, "Platform 6 m", new Vector3(x, 3.0f, length + 3.0f), new Vector3(4.0f, 6.0f, 6.0f), materials.Ramp);
			BrownoutMovementSetup.Solid(group, "Step 3 m", new Vector3(x - 5.0f, 1.5f, length + 3.0f), new Vector3(6.0f, 3.0f, 6.0f), materials.Frame);
			Label(group, "Label Platform", new Vector3(x, 7.5f, length + 3.0f), "6 m drop\n(3 m step to the left)", 0.3f);
		}

		/// <summary>Phase 11a: target boards behind the spawn at 25, 100 and 200 m (the Scope pad's guns), each on a post with its distance.</summary>
		private static void BuildScopeBoards(Transform root, BrownoutMovementSetup.Materials materials)
		{
			var group = BrownoutMovementSetup.Group(root, "ScopeBoards");
			float[] distances = { 25.0f, 100.0f, 200.0f };

			for (int i = 0; i < distances.Length; ++i)
			{
				float d    = distances[i];
				float size = 1.0f + d / 100.0f;     // bigger further away, so each reads in its scope
				Vector3 at = new Vector3((i - 1) * 6.0f, 0.0f, -d);

				BrownoutMovementSetup.Solid(group, $"Post {d:F0} m", at + Vector3.up * 0.9f, new Vector3(0.2f, 1.8f, 0.2f), materials.Frame);
				BrownoutMovementSetup.Solid(group, $"Board {d:F0} m", at + Vector3.up * (1.8f + size * 0.5f), new Vector3(size, size, 0.1f), materials.MarkerMajor);
				BrownoutMovementSetup.Visual(group, $"Ring {d:F0} m", at + new Vector3(0.0f, 1.8f + size * 0.5f, 0.06f), new Vector3(size * 0.5f, size * 0.5f, 0.02f), materials.Fail);
				BrownoutMovementSetup.Visual(group, $"Bull {d:F0} m", at + new Vector3(0.0f, 1.8f + size * 0.5f, 0.08f), new Vector3(size * 0.15f, size * 0.15f, 0.02f), materials.Frame);

				var label = new GameObject($"Label {d:F0} m");
				label.transform.SetParent(group, false);
				label.transform.localPosition = at + new Vector3(0.0f, 1.9f + size, 0.0f);
				label.transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f); // readable from the spawn
				var mesh = label.AddComponent<TextMesh>();
				mesh.text = $"{d:F0} m"; mesh.characterSize = 0.3f + d / 200.0f; mesh.fontSize = 48; mesh.anchor = TextAnchor.LowerCenter; mesh.color = Color.white;
				mesh.transform.localScale = Vector3.one * 0.25f;
			}
		}

		private static void PlaceSpawnPoints()
		{
			var spawnPoints = Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
			Vector3[] positions = { new Vector3(-1.5f, 0.0f, 0.0f), new Vector3(1.5f, 0.0f, 0.0f), new Vector3(-1.5f, 0.0f, -3.0f), new Vector3(1.5f, 0.0f, -3.0f) };

			for (int i = 0; i < spawnPoints.Length; ++i)
			{
				spawnPoints[i].transform.position = positions[i % positions.Length];
				spawnPoints[i].transform.rotation = Quaternion.identity; // Facing +Z, toward the pads
			}
		}

		/// <summary>A slab whose top surface runs from one point to another (1 m thick).</summary>
		private static void Slab(Transform parent, string objectName, Vector3 from, Vector3 to, float width, Material material)
		{
			Vector3    direction = to - from;
			Quaternion rotation  = Quaternion.LookRotation(direction.normalized, Vector3.up);
			Vector3    center    = (from + to) * 0.5f - rotation * Vector3.up * 0.5f;

			var slab = BrownoutMovementSetup.Solid(parent, objectName, center, new Vector3(width, 1.0f, direction.magnitude + 0.2f), material);
			slab.localRotation = rotation;
		}

		/// <summary>Floating text facing the spawn (-Z side readable).</summary>
		private static void Label(Transform parent, string objectName, Vector3 position, string text, float size)
		{
			var label = new GameObject(objectName);
			label.transform.SetParent(parent, false);
			label.transform.localPosition = position;

			var mesh = label.AddComponent<TextMesh>();
			mesh.text          = text;
			mesh.characterSize = size;
			mesh.fontSize      = 48;
			mesh.anchor        = TextAnchor.LowerCenter;
			mesh.alignment     = TextAlignment.Center;
			mesh.color         = Color.white;
			mesh.transform.localScale = Vector3.one * 0.25f;
		}
	}
}
