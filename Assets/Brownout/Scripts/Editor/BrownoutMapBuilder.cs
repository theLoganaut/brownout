namespace TPSBR
{
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text;
	using UnityEditor;
	using UnityEditor.SceneManagement;
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.SceneManagement;
	using UnityScene = UnityEngine.SceneManagement.Scene;

	/// <summary>
	/// Map v0 (phase 8a): builds the Brownout scene from References/map_layout.json and map_loot.json, in the editor, only when asked
	/// (a match just loads the saved scene). Brownout → Setup → Build Brownout Map has one entry per part:
	///   Build All / Ground: terrain heights (the height zones from Logan's sketch with rolling hills, POI pads, smooth roads, the river valley), paint, the river
	///     (water, the invisible bank wall on the RiverWall layer) and the five bridges; then everything that sits on the ground again;
	///   Nature: trees, bushes, rocks and grass (BrownoutNatureBuilder); Loot Spots: the loot points; Spawn Points; Map Picture.
	/// Numbers in Resources/Settings/BrownoutMapSettings.asset. The scene is never hand-edited: Logan sends changes, Claude edits the data
	/// files and rebuilds the parts they touch.
	/// </summary>
	public static class BrownoutMapBuilder
	{
		// CONSTANTS

		public const string ScenePath     = "Assets/Scenes/Brownout.unity";
		public const string MapID         = "Map.Brownout";
		public const string SettingsPath  = "Assets/Brownout/Resources/Settings/BrownoutMapSettings.asset";
		public const string ArtFolder     = "Assets/Brownout/Art/Map";
		public const string TerrainFolder = "Assets/Brownout/Art/Map/Terrain";
		public const string TerrainPath   = "Assets/Brownout/Art/Map/BrownoutTerrain.asset";
		public const string WaterPath     = "Assets/Brownout/Art/Map/RiverWater.asset";
		public const string PicturePath   = "Assets/Brownout/Art/Map/BrownoutMap.jpg";
		public const string RootName      = "BrownoutMap";
		public const int    RiverWallLayerIndex = 15;

		private const string MENU = "Brownout/Setup/Build Brownout Map/";

		// MENU

		[MenuItem(MENU + "Build All", priority = 1)]
		private static void BuildAllMenu() => Debug.Log(BuildAll());

		[MenuItem(MENU + "Ground (terrain, river, bridges)", priority = 20)]
		private static void GroundMenu() => Debug.Log(BuildAll());

		[MenuItem(MENU + "Buildings", priority = 21)]
		private static void BuildingsMenu() => Debug.Log(Run(BuildBuildings));

		[MenuItem(MENU + "Nature", priority = 21)]
		private static void NatureMenu() => Debug.Log(Run(context => { BrownoutNatureBuilder.Build(context); BuildLoot(context); BuildPicture(context); BrownoutNavMeshBuilder.Build(context); }));

		[MenuItem(MENU + "Loot Spots", priority = 22)]
		private static void LootMenu() => Debug.Log(Run(BuildLoot));

		[MenuItem(MENU + "Spawn Points", priority = 23)]
		private static void SpawnMenu() => Debug.Log(Run(BuildSpawns));

		[MenuItem(MENU + "Map Picture", priority = 24)]
		private static void PictureMenu() => Debug.Log(Run(BuildPicture));

		[MenuItem(MENU + "NavMesh (bots)", priority = 25)]
		private static void NavMeshMenu() => Debug.Log(Run(BrownoutNavMeshBuilder.Build));

		[MenuItem(MENU + "Remake Placeholder Nature Models", priority = 40)]
		private static void NatureModelsMenu() => Debug.Log(BrownoutNatureBuilder.MakePlaceholders(Settings(), true));

		[MenuItem("Brownout/Setup/Build Brownout Map Settings", priority = 8)]
		private static void SettingsMenu() => Debug.Log(BuildSettings());

		// PUBLIC METHODS

		public static string BuildAll()
		{
			return Run(context =>
			{
				BuildGround(context);
				BrownoutBlockoutBuilder.Build(context);
				BrownoutNatureBuilder.Build(context);
				BuildSpawns(context);
				BuildLoot(context);
				BuildPicture(context);
				BuildLookoutPictures(context);
				BrownoutNavMeshBuilder.Build(context);
			});
		}

		/// <summary>Buildings (phase 8b) and everything that keeps clear of them or sits in them: nature, spawns, loot, the picture.</summary>
		public static void BuildBuildings(Context context)
		{
			BrownoutBlockoutBuilder.Build(context);
			BrownoutNatureBuilder.Build(context);
			BuildSpawns(context);
			BuildLoot(context);
			BuildPicture(context);
			BrownoutNavMeshBuilder.Build(context);
		}

		/// <summary>Opens the Brownout scene (made from the Template scene the first time), runs the parts and saves the scene.</summary>
		public static string Run(System.Action<Context> build)
		{
			if (EditorApplication.isPlaying == true)
				return "Stop Play mode first.";

			string settingsResult = BuildSettings();
			string layerResult    = EnsureRiverWallLayer();

			UnityScene scene = OpenScene(out string sceneResult);
			if (scene.IsValid() == false)
				return sceneResult;

			var context = new Context(Settings(), BrownoutMapData.LoadLayout(), scene);
			context.Log.Add(settingsResult);
			context.Log.Add(layerResult);
			context.Log.Add(sceneResult);

			float start = (float)EditorApplication.timeSinceStartup;
			build(context);

			EditorSceneManager.MarkSceneDirty(scene);
			EditorSceneManager.SaveScene(scene);
			AssetDatabase.SaveAssets();

			context.Log.Add($"Saved {ScenePath} ({(float)EditorApplication.timeSinceStartup - start:F1} s).");

			string log = string.Join("\n", context.Log.Where(t => string.IsNullOrEmpty(t) == false));
			string folder = Path.Combine(BrownoutMapData.ProjectFolder, "Logs", "MapBuild");
			Directory.CreateDirectory(folder);
			File.WriteAllText(Path.Combine(folder, "MapBuild.txt"), log);
			return log;
		}

		/// <summary>Makes Resources/Settings/BrownoutMapSettings.asset if missing and links it from GlobalSettings.</summary>
		public static string BuildSettings()
		{
			var settings = AssetDatabase.LoadAssetAtPath<BrownoutMapSettings>(SettingsPath);
			string result = string.Empty;

			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<BrownoutMapSettings>();
				AssetDatabase.CreateAsset(settings, SettingsPath);
				result += $"Created {SettingsPath}. ";
			}

			string globalPath = AssetDatabase.FindAssets("t:GlobalSettings").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
			var    global     = globalPath != null ? AssetDatabase.LoadAssetAtPath<GlobalSettings>(globalPath) : null;
			if (global == null)
				return result + "GlobalSettings asset not found.";

			if (global.BrownoutMap != settings)
			{
				global.BrownoutMap = settings;
				EditorUtility.SetDirty(global);
				AssetDatabase.SaveAssets();
				result += $"Linked BrownoutMapSettings from {globalPath}. ";
			}

			return result;
		}

		public static BrownoutMapSettings Settings() => AssetDatabase.LoadAssetAtPath<BrownoutMapSettings>(SettingsPath);

		// CONTEXT

		/// <summary>Everything one build shares.</summary>
		public sealed class Context
		{
			public readonly BrownoutMapSettings     S;
			public readonly BrownoutMapData.Layout  Layout;
			public readonly UnityScene                 Scene;
			public readonly List<string>            Log = new List<string>();
			public readonly Transform               Root;
			public readonly BrownoutMap             Map;

			public Terrain       Terrain;
			public List<Vector2> RiverLine;   // World X / Z, every few meters, extended past the map
			public float[]       RiverWater;  // Water surface height per point
			public List<Bridge>  Bridges = new List<Bridge>();
			public List<List<Vector2>> RoadLines;  // Smoothed
			public List<List<Vector2>> TrailLines;
			public BrownoutMapHeights  Heights;     // Ground build only
			public List<(string Name, Vector3 Eye)> Lookouts = new List<(string, Vector3)>();  // Ground build only: each top zone's highest point, at eye height

			private List<BrownoutBlockoutBuilder.Footprint> _footprints;
			private bool[,] _buildingMask;
			private const float MaskCell   = 1.0f;
			private const float MaskMargin = 40.0f;

			public float HalfMap  => Layout.Size * 0.5f;

			/// <summary>Everything the blockout puts on the ground (phase 8b); read from map_blockout.json when the buildings weren't built in this run.</summary>
			public List<BrownoutBlockoutBuilder.Footprint> Footprints
			{
				get
				{
					if (_footprints == null)
					{
						_footprints = BrownoutBlockoutBuilder.LoadFootprints(this);
					}

					return _footprints;
				}
			}

			public void ResetFootprints(List<BrownoutBlockoutBuilder.Footprint> footprints)
			{
				_footprints   = new List<BrownoutBlockoutBuilder.Footprint>(footprints);
				_buildingMask = null;
			}

			/// <summary>On or within BuildingClearance of a building or other blockout piece (a 1 m grid, so it's quick for grass).</summary>
			public bool IsInBuilding(Vector2 point)
			{
				float half = HalfMap + MaskMargin;
				int   res  = Mathf.CeilToInt(half * 2.0f / MaskCell);

				if (_buildingMask == null)
				{
					_buildingMask = new bool[res, res];
					float margin  = S.BuildingClearance;
					foreach (BrownoutBlockoutBuilder.Footprint footprint in Footprints)
					{
						float reach = footprint.Half.magnitude + margin + MaskCell;
						int x0 = Mathf.Max(0,       Mathf.FloorToInt((footprint.Center.x - reach + half) / MaskCell));
						int x1 = Mathf.Min(res - 1, Mathf.CeilToInt ((footprint.Center.x + reach + half) / MaskCell));
						int z0 = Mathf.Max(0,       Mathf.FloorToInt((footprint.Center.y - reach + half) / MaskCell));
						int z1 = Mathf.Min(res - 1, Mathf.CeilToInt ((footprint.Center.y + reach + half) / MaskCell));
						for (int z = z0; z <= z1; ++z)
						{
							for (int x = x0; x <= x1; ++x)
							{
								if (_buildingMask[z, x] == false && footprint.Contains(new Vector2(-half + (x + 0.5f) * MaskCell, -half + (z + 0.5f) * MaskCell), margin + MaskCell * 0.5f) == true)
								{
									_buildingMask[z, x] = true;
								}
							}
						}
					}
				}

				int cx = Mathf.FloorToInt((point.x + half) / MaskCell);
				int cz = Mathf.FloorToInt((point.y + half) / MaskCell);
				return cx >= 0 && cz >= 0 && cx < res && cz < res && _buildingMask[cz, cx] == true;
			}
			public float RiverHalfWidth => Layout.RiverWidth * 0.5f;

			public Context(BrownoutMapSettings settings, BrownoutMapData.Layout layout, UnityScene scene)
			{
				S      = settings;
				Layout = layout;
				Scene  = scene;

				GameObject root = scene.GetRootGameObjects().FirstOrDefault(t => t.name == RootName);
				if (root == null)
				{
					root = new GameObject(RootName);
					SceneManager.MoveGameObjectToScene(root, scene);
				}

				Root = root.transform;
				Map  = root.GetComponent<BrownoutMap>() ?? root.AddComponent<BrownoutMap>();

				Terrain = Root.GetComponentInChildren<Terrain>();

				RoadLines  = layout.Roads.Select(t => Smooth(t, 2.0f)).ToList();
				TrailLines = layout.Trails.Select(t => Smooth(t, 1.0f)).ToList();
				RiverLine  = Smooth(Extend(layout.River, settings.RiverExtension), 3.0f);

				// Bridges sit where their road crosses the river, and the road runs straight across them
				for (int i = 0; i < layout.Bridges.Count; ++i)
				{
					Bridges.Add(MakeBridge(this, layout.Bridges[i], layout.BridgeStyles[i]));
				}
				StraightenRoads(this);

				if (Terrain != null)
				{
					// A part rebuilt on its own reads the river's water heights the last Ground build saved, and the decks from the abutments
					RiverWater = Map.RiverWater.Count == RiverLine.Count ? Map.RiverWater.ToArray() : ComputeWaterFromTerrain();
					for (int i = 0; i < Bridges.Count; ++i)
					{
						Bridge bridge = Bridges[i];
						bridge.Deck    = Mathf.Max(GroundHeight(bridge.A), GroundHeight(bridge.B));
						bridge.HeightA = bridge.Deck;
						bridge.HeightB = bridge.Deck;
						Bridges[i]     = bridge;
					}
				}
			}

			/// <summary>A clean child group for one part of the map.</summary>
			public Transform Section(string name)
			{
				Transform section = Root.Find(name);
				if (section != null)
				{
					Object.DestroyImmediate(section.gameObject);
				}

				section = new GameObject(name).transform;
				section.SetParent(Root, false);
				return section;
			}

			public float GroundHeight(Vector2 world)
			{
				return Terrain != null ? Terrain.SampleHeight(new Vector3(world.x, 0.0f, world.y)) + Terrain.transform.position.y : 0.0f;
			}

			public float GroundSlope(Vector2 world)
			{
				if (Terrain == null)
					return 0.0f;

				Vector3 size = Terrain.terrainData.size;
				Vector3 origin = Terrain.transform.position;
				return Terrain.terrainData.GetSteepness((world.x - origin.x) / size.x, (world.y - origin.z) / size.z);
			}

			public bool IsOnMap(Vector2 world, float margin = 0.0f)
			{
				return Mathf.Abs(world.x) <= HalfMap - margin && Mathf.Abs(world.y) <= HalfMap - margin;
			}

			public float RiverDistance(Vector2 world)
			{
				return DistanceToLine(RiverLine, world, out _, out _);
			}

			public float RoadDistance(Vector2 world, out bool isTrail)
			{
				float best = float.MaxValue;
				isTrail = false;

				foreach (List<Vector2> road in RoadLines)
				{
					float d = DistanceToLine(road, world, out _, out _) - Layout.RoadWidth * 0.5f;
					if (d < best) { best = d; isTrail = false; }
				}

				foreach (List<Vector2> trail in TrailLines)
				{
					float d = DistanceToLine(trail, world, out _, out _) - Layout.TrailWidth * 0.5f;
					if (d < best) { best = d; isTrail = true; }
				}

				return best;
			}

			/// <summary>Inside a POI box (1), a cabin area (2) or neither (0), with a soft edge for densities.</summary>
			public float AreaDensity(Vector2 world)
			{
				float density = 1.0f;
				foreach (BrownoutMapData.Box box in Layout.AllAreas())
				{
					float outside = BoxDistance(box, world);
					float inside  = 1.0f - Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(outside / 15.0f));
					float target  = box.IsCabins == true ? S.CabinDensity : S.PoiDensity;
					density = Mathf.Min(density, Mathf.Lerp(1.0f, target, inside));
				}

				return density;
			}

			public bool IsOnBridge(Vector2 world, float margin)
			{
				foreach (Bridge bridge in Bridges)
				{
					if (bridge.Contains(world, margin) == true)
						return true;
				}

				return false;
			}

			private float[] ComputeWaterFromTerrain()
			{
				// Bank heights along the line from the built terrain: the valley's rim on either side, smoothed along the river
				var water = new float[RiverLine.Count];
				var banks = new float[RiverLine.Count];
				for (int i = 0; i < RiverLine.Count; ++i)
				{
					banks[i] = BankHeightAt(i);
				}

				float[] smoothed = SmoothAlong(RiverLine, banks, S.RiverSmoothing);
				for (int i = 0; i < water.Length; ++i)
				{
					water[i] = smoothed[i] - S.RiverDepth + S.WaterDepth;
				}

				return water;
			}

			private float BankHeightAt(int index)
			{
				// The bed is cut a known depth under the smoothed bank, so the bed itself tells us the bank: read the ground at the center
				return GroundHeight(RiverLine[index]) + S.RiverDepth;
			}
		}

		/// <summary>A level bridge deck from A to B (world X / Z) on its road's line, with abutments at both ends.</summary>
		public struct Bridge
		{
			public string  Style;    // concrete, truss, arch, wood, girder
			public int     Road;     // Index into RoadLines, or -1
			public float   Deck;     // Deck top height
			public Vector2 A;
			public Vector2 B;
			public float   HeightA;
			public float   HeightB;
			public float   Width;
			public Vector2 RiverPoint;
			public Vector2 RiverTangent;
			public float   Sin;

			public Vector2 Direction => (B - A).normalized;
			public float   Length    => Vector2.Distance(A, B);

			public bool Contains(Vector2 world, float margin)
			{
				Vector2 direction = Direction;
				Vector2 local     = world - A;
				float   along     = Vector2.Dot(local, direction);
				float   across    = Mathf.Abs(local.x * direction.y - local.y * direction.x);
				return along >= -margin && along <= Length + margin && across <= Width * 0.5f + margin;
			}

			public float DeckHeight(Vector2 world)
			{
				float along = Mathf.Clamp01(Vector2.Dot(world - A, Direction) / Mathf.Max(0.01f, Length));
				return Mathf.Lerp(HeightA, HeightB, along);
			}
		}

		// GROUND

		private static void BuildGround(Context c)
		{
			BrownoutMapSettings s = c.S;
			BrownoutMapData.Layout layout = c.Layout;

			EnsureFolder(ArtFolder);
			EnsureFolder(TerrainFolder);

			int   res    = Mathf.ClosestPowerOfTwo(Mathf.Max(33, s.HeightmapResolution - 1)) + 1;
			float size   = s.TerrainSize;
			float step   = size / (res - 1);
			float origin = -size * 0.5f;
			var   h      = new float[res, res];   // [z, x] world heights
			var   rng    = new System.Random(s.Seed);
			var   noiseOffset = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000));
			var   bumpOffset  = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000));

			// 1. The large shape from the height zones, plus rolling hills scaled to the height and small bumps
			c.Heights = new BrownoutMapHeights(layout, s, size, c.RiverLine);
			for (int z = 0; z < res; ++z)
			{
				for (int x = 0; x < res; ++x)
				{
					h[z, x] = BaseHeight(c, origin + x * step, origin + z * step, noiseOffset, bumpOffset);
				}
			}

			// 2. Pads under each POI and cabin area: a plane through the ground with part of its tilt kept, blended out past the box
			foreach (BrownoutMapData.Box box in layout.AllAreas())
			{
				ApplyPad(c, h, res, step, origin, box, noiseOffset, bumpOffset);
			}

			// 3. Roads and trails: the ground along each line goes to a height averaged along the line
			var roadWeight = new float[res, res];
			var roadHeight = new float[res, res];
			foreach (List<Vector2> road in c.RoadLines)
			{
				StampRoad(h, roadWeight, roadHeight, res, step, origin, road, layout.RoadWidth * 0.5f, s.RoadShoulder, s.RoadSmoothing);
			}
			foreach (List<Vector2> trail in c.TrailLines)
			{
				StampRoad(h, roadWeight, roadHeight, res, step, origin, trail, layout.TrailWidth * 0.5f, s.TrailShoulder, s.RoadSmoothing * 0.5f);
			}
			for (int z = 0; z < res; ++z)
			{
				for (int x = 0; x < res; ++x)
				{
					if (roadWeight[z, x] > 0.0f)
					{
						h[z, x] = Mathf.Lerp(h[z, x], roadHeight[z, x], roadWeight[z, x]);
					}
				}
			}

			// 4. The river valley: a bank height averaged along the river, the bed RiverDepth under it, steep sides at the water
			float[] riverBank = SmoothAlong(c.RiverLine, c.RiverLine.Select(p => SampleGrid(h, res, step, origin, p)).ToArray(), s.RiverSmoothing);
			CutRiver(c, h, res, step, origin, riverBank);

			// 5. Bridges: a level deck above the water, flat abutments at its ends, the road ramped up to them, the ground cut away under it
			c.RiverWater = SmoothAlong(c.RiverLine, riverBank, 1.0f).Select(t => t - s.RiverDepth + s.WaterDepth).ToArray();
			ShapeBridgeGround(c, h, res, step, origin);

			// Write the terrain
			TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
			if (data == null)
			{
				data = new TerrainData();
				AssetDatabase.CreateAsset(data, TerrainPath);
			}

			data.heightmapResolution = res;
			data.size = new Vector3(size, s.TerrainHeight, size);

			var normalized = new float[res, res];
			for (int z = 0; z < res; ++z)
			{
				for (int x = 0; x < res; ++x)
				{
					normalized[z, x] = Mathf.Clamp01((h[z, x] - s.TerrainBottom) / s.TerrainHeight);
				}
			}
			data.SetHeights(0, 0, normalized);

			Transform section = c.Section("Terrain");
			GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
			terrainObject.name = "Terrain";
			terrainObject.transform.SetParent(section, false);
			terrainObject.transform.position = new Vector3(origin, s.TerrainBottom, origin);
			terrainObject.layer = 0;
			GameObjectUtility.SetStaticEditorFlags(terrainObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

			Terrain terrain = terrainObject.GetComponent<Terrain>();
			terrain.materialTemplate      = TerrainMaterial();
			terrain.drawInstanced         = true;
			terrain.heightmapPixelError   = 2.0f; // Higher hides the 4 m river from the helicopter
			terrain.basemapDistance       = 400.0f;
			terrain.treeDistance          = 0.0f; // Trees, bushes and small rocks are drawn by the NatureInstancer; the terrain keeps their colliders
			terrain.treeBillboardDistance = 0.0f;
			terrainObject.AddComponent<NatureInstancer>();
			terrain.detailObjectDistance  = s.GrassDistance;
			terrain.detailObjectDensity   = 1.0f;
			terrain.shadowCastingMode     = ShadowCastingMode.TwoSided;
			c.Terrain = terrain;

			ReportHeights(c, h, res, step, origin, data);

			PaintTerrain(c, data);
			BuildRiver(c);
			BuildBridges(c);
			WriteMapData(c);
			SetUpScene(c);
			CheckCrossings(c);

			float lowest  = float.MaxValue;
			float highest = float.MinValue;
			for (int z = 0; z < res; ++z)
			{
				for (int x = 0; x < res; ++x)
				{
					float world = origin + x * step;
					float north = origin + z * step;
					if (Mathf.Abs(world) > c.HalfMap || Mathf.Abs(north) > c.HalfMap)
						continue;

					lowest  = Mathf.Min(lowest,  h[z, x]);
					highest = Mathf.Max(highest, h[z, x]);
				}
			}

			c.Log.Add($"Ground: terrain {size:F0} m ({res}² heights, {step:F2} m apart), map ground from {lowest:F1} to {highest:F1} m, {c.RoadLines.Count} roads, {c.TrailLines.Count} trails, river {LineLength(c.RiverLine):F0} m with {c.Bridges.Count} bridges.");
		}

		private static float BaseHeight(Context c, float x, float z, Vector2 noiseOffset, Vector2 bumpOffset)
		{
			BrownoutMapSettings s = c.S;

			float shape = c.Heights.Sample(x, z);

			float hills = 0.0f;
			float amplitude = 1.0f;
			float frequency = 1.0f;
			float total = 0.0f;
			for (int octave = 0; octave < 3; ++octave)
			{
				hills += amplitude * Mathf.PerlinNoise(noiseOffset.x + x * frequency / s.HillScale, noiseOffset.y + z * frequency / s.HillScale);
				total += amplitude;
				amplitude *= 0.5f;
				frequency *= 2.0f;
			}
			hills = Mathf.Clamp((hills / total - 0.5f) * 2.0f * s.HillContrast, -1.0f, 1.0f);

			float bump = (Mathf.PerlinNoise(bumpOffset.x + x / s.BumpScale, bumpOffset.y + z / s.BumpScale) - 0.5f) * 2.0f * s.BumpHeight;

			// At least HillMinimum high anywhere, more on high ground; calm next to the river. Dips go no deeper than the ground is high,
			// so low ground (the river valley) gets mounds instead of flat floors at sea level
			float size  = Mathf.Max(s.HillShare * shape, s.HillMinimum);
			float calm  = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(s.ValleyHalfWidth, s.HillRiverClear, c.Heights.RiverDistance(x, z)));
			float hill  = hills >= 0.0f ? size * hills : Mathf.Min(size, shape) * hills;

			// Bank mounds: separate knolls along both banks (cover at the river's chokepoints), from the valley's edge out to BankMoundReach
			float river = c.Heights.RiverDistance(x, z);
			float bank  = calm * (1.0f - Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(s.BankMoundReach * 0.6f, s.BankMoundReach, river)));
			float mound = Mathf.Clamp01((Mathf.PerlinNoise(noiseOffset.y + x / s.BankMoundScale, noiseOffset.x + z / s.BankMoundScale) - 0.45f) * 3.0f);
			mound = Mathf.SmoothStep(0.0f, 1.0f, mound) * s.BankMoundHeight * bank;

			return Mathf.Max(0.0f, shape + hill * calm + mound + bump);
		}

		private static void ApplyPad(Context c, float[,] h, int res, float step, float origin, BrownoutMapData.Box box, Vector2 noiseOffset, Vector2 bumpOffset)
		{
			BrownoutMapSettings s = c.S;
			Vector2 center = box.Center;
			Vector2 half   = box.Size * 0.5f;

			// Least-squares plane over the box from the large slope (a symmetric grid, so the terms separate)
			float sum = 0.0f, sumX = 0.0f, sumZ = 0.0f, sumXX = 0.0f, sumZZ = 0.0f;
			int   count = 0;
			const int samples = 9;
			for (int i = 0; i < samples; ++i)
			{
				for (int j = 0; j < samples; ++j)
				{
					float dx = Mathf.Lerp(-half.x, half.x, i / (samples - 1.0f));
					float dz = Mathf.Lerp(-half.y, half.y, j / (samples - 1.0f));
					float y  = BaseHeight(c, center.x + dx, center.y + dz, noiseOffset, bumpOffset);
					sum   += y;
					sumX  += dx * y;
					sumZ  += dz * y;
					sumXX += dx * dx;
					sumZZ += dz * dz;
					++count;
				}
			}

			float mean   = sum / count;
			float slopeX = sumXX > 0.0f ? sumX / sumXX : 0.0f;
			float slopeZ = sumZZ > 0.0f ? sumZ / sumZZ : 0.0f;

			float reach = s.PadFalloff;
			int x0 = Mathf.Max(0,       Mathf.FloorToInt((center.x - half.x - reach - origin) / step));
			int x1 = Mathf.Min(res - 1, Mathf.CeilToInt ((center.x + half.x + reach - origin) / step));
			int z0 = Mathf.Max(0,       Mathf.FloorToInt((center.y - half.y - reach - origin) / step));
			int z1 = Mathf.Min(res - 1, Mathf.CeilToInt ((center.y + half.y + reach - origin) / step));

			for (int z = z0; z <= z1; ++z)
			{
				for (int x = x0; x <= x1; ++x)
				{
					float dx = origin + x * step - center.x;
					float dz = origin + z * step - center.y;
					float outside = new Vector2(Mathf.Max(0.0f, Mathf.Abs(dx) - half.x), Mathf.Max(0.0f, Mathf.Abs(dz) - half.y)).magnitude;
					float weight  = 1.0f - Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(outside / reach));
					if (weight <= 0.0f)
						continue;

					float target = mean + s.PadTiltKept * (slopeX * dx + slopeZ * dz);
					h[z, x] = Mathf.Lerp(h[z, x], target, weight);
				}
			}
		}

		private static void StampRoad(float[,] h, float[,] weights, float[,] heights, int res, float step, float origin, List<Vector2> line, float halfWidth, float shoulder, float smoothing)
		{
			float[] along = SmoothAlong(line, line.Select(p => SampleGrid(h, res, step, origin, p)).ToArray(), smoothing);
			float   reach = halfWidth + shoulder;
			float   inner = halfWidth + 0.75f;

			for (int i = 0; i < line.Count - 1; ++i)
			{
				Vector2 a = line[i];
				Vector2 b = line[i + 1];

				int x0 = Mathf.Max(0,       Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach - origin) / step));
				int x1 = Mathf.Min(res - 1, Mathf.CeilToInt ((Mathf.Max(a.x, b.x) + reach - origin) / step));
				int z0 = Mathf.Max(0,       Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach - origin) / step));
				int z1 = Mathf.Min(res - 1, Mathf.CeilToInt ((Mathf.Max(a.y, b.y) + reach - origin) / step));

				for (int z = z0; z <= z1; ++z)
				{
					for (int x = x0; x <= x1; ++x)
					{
						Vector2 p = new Vector2(origin + x * step, origin + z * step);
						float   d = DistanceToSegment(p, a, b, out float t);
						if (d > reach)
							continue;

						float w = d <= inner ? 1.0f : 1.0f - Mathf.SmoothStep(0.0f, 1.0f, (d - inner) / Mathf.Max(0.01f, reach - inner));
						if (w > weights[z, x])
						{
							weights[z, x] = w;
							heights[z, x] = Mathf.Lerp(along[i], along[i + 1], t);
						}
					}
				}
			}
		}

		private static void CutRiver(Context c, float[,] h, int res, float step, float origin, float[] bank)
		{
			BrownoutMapSettings s = c.S;
			float halfWidth = c.RiverHalfWidth;
			float valley    = s.ValleyHalfWidth;
			List<Vector2> line = c.RiverLine;

			var best   = new float[res, res];
			var target = new float[res, res];
			for (int z = 0; z < res; ++z)
				for (int x = 0; x < res; ++x)
					best[z, x] = float.MaxValue;

			for (int i = 0; i < line.Count - 1; ++i)
			{
				Vector2 a = line[i];
				Vector2 b = line[i + 1];

				int x0 = Mathf.Max(0,       Mathf.FloorToInt((Mathf.Min(a.x, b.x) - valley - origin) / step));
				int x1 = Mathf.Min(res - 1, Mathf.CeilToInt ((Mathf.Max(a.x, b.x) + valley - origin) / step));
				int z0 = Mathf.Max(0,       Mathf.FloorToInt((Mathf.Min(a.y, b.y) - valley - origin) / step));
				int z1 = Mathf.Min(res - 1, Mathf.CeilToInt ((Mathf.Max(a.y, b.y) + valley - origin) / step));

				for (int z = z0; z <= z1; ++z)
				{
					for (int x = x0; x <= x1; ++x)
					{
						Vector2 p = new Vector2(origin + x * step, origin + z * step);
						float   d = DistanceToSegment(p, a, b, out float t);
						if (d > valley || d >= best[z, x])
							continue;

						best[z, x]   = d;
						target[z, x] = Mathf.Lerp(bank[i], bank[i + 1], t);
					}
				}
			}

			for (int z = 0; z < res; ++z)
			{
				for (int x = 0; x < res; ++x)
				{
					float d = best[z, x];
					if (d > valley)
						continue;

					float bed     = target[z, x] - s.RiverDepth;
					float surface = bed + s.WaterDepth;
					float ground;

					// Bed across the channel, a steep bank that is dry by the wall (halfWidth + 0.5), then the valley side up to the land
					if (d <= halfWidth - 0.5f)
					{
						ground = bed;
					}
					else if (d <= halfWidth + 0.75f)
					{
						ground = Mathf.Lerp(bed, surface + 0.3f, (d - (halfWidth - 0.5f)) / 1.25f);
					}
					else
					{
						float u = Mathf.Clamp01((d - (halfWidth + 0.75f)) / Mathf.Max(0.01f, valley - (halfWidth + 0.75f)));
						ground = Mathf.Lerp(surface + 0.3f, h[z, x], Mathf.SmoothStep(0.0f, 1.0f, u));
					}

					h[z, x] = Mathf.Min(h[z, x], ground);
				}
			}
		}

		// PAINT

		private static void PaintTerrain(Context c, TerrainData data)
		{
			BrownoutMapSettings s = c.S;

			TerrainLayer[] layers =
			{
				Layer("Grass",    s.Grass,    0.10f, 6.0f),
				Layer("GrassDry", s.GrassDry, 0.10f, 6.0f),
				Layer("Dirt",     s.Dirt,     0.12f, 5.0f),
				Layer("Rock",     s.Rock,     0.15f, 8.0f),
				Layer("Road",     s.Road,     0.06f, 4.0f),
				Layer("Trail",    s.Trail,    0.08f, 3.0f),
			};

			int res = s.AlphamapResolution;
			data.terrainLayers       = layers;
			data.alphamapResolution  = res;
			data.baseMapResolution   = 1024;

			float size   = data.size.x;
			float step   = size / res;
			float origin = -size * 0.5f + step * 0.5f;

			float[,] road  = DistanceField(res, step, origin, c.RoadLines,  c.Layout.RoadWidth * 0.5f + s.RoadShoulder + 4.0f);
			float[,] trail = DistanceField(res, step, origin, c.TrailLines, c.Layout.TrailWidth * 0.5f + s.TrailShoulder + 4.0f);
			float[,] river = DistanceField(res, step, origin, new List<List<Vector2>> { c.RiverLine }, s.ValleyHalfWidth + 2.0f);

			var noiseOffset = new Vector2(s.Seed * 13.1f, s.Seed * 7.7f);
			var maps = new float[res, res, layers.Length];

			for (int z = 0; z < res; ++z)
			{
				for (int x = 0; x < res; ++x)
				{
					Vector2 world = new Vector2(origin + x * step, origin + z * step);
					float   slope = data.GetSteepness((x + 0.5f) / res, (z + 0.5f) / res);

					float roadW  = 1.0f - Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(c.Layout.RoadWidth * 0.5f - 0.4f, c.Layout.RoadWidth * 0.5f + 0.6f, road[z, x]));
					float trailW = (1.0f - Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(c.Layout.TrailWidth * 0.5f - 0.3f, c.Layout.TrailWidth * 0.5f + 0.5f, trail[z, x]))) * (1.0f - roadW);
					float rest   = Mathf.Max(0.0f, 1.0f - roadW - trailW);

					float rockF  = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(26.0f, 36.0f, slope));
					float rock   = rest * rockF;
					rest -= rock;

					// A cut bank beside a road or trail is dirt, not rock (grey rock along a road reads as a second road from above)
					float cutF   = Mathf.Max(1.0f - Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(c.Layout.RoadWidth * 0.5f + s.RoadShoulder, c.Layout.RoadWidth * 0.5f + s.RoadShoulder + 3.0f, road[z, x])),
					                         1.0f - Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(c.Layout.TrailWidth * 0.5f + s.TrailShoulder, c.Layout.TrailWidth * 0.5f + s.TrailShoulder + 3.0f, trail[z, x])));
					float cut    = rock * cutF;
					rock -= cut;

					float bankF  = river[z, x] < s.ValleyHalfWidth ? Mathf.Lerp(0.9f, 0.0f, Mathf.InverseLerp(c.RiverHalfWidth, s.ValleyHalfWidth, river[z, x])) : 0.0f;
					float patchF = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(0.64f, 0.78f, Mathf.PerlinNoise(noiseOffset.x + world.x / 45.0f, noiseOffset.y + world.y / 45.0f)));
					float dirt   = rest * Mathf.Clamp01(Mathf.Max(bankF, patchF * 0.6f));
					rest -= dirt;
					dirt += cut;

					float dryF   = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(0.4f, 0.7f, Mathf.PerlinNoise(noiseOffset.y + world.x / 120.0f, noiseOffset.x + world.y / 120.0f)));
					float dry    = rest * dryF * 0.8f;
					float grass  = rest - dry;

					maps[z, x, 0] = grass;
					maps[z, x, 1] = dry;
					maps[z, x, 2] = dirt;
					maps[z, x, 3] = rock;
					maps[z, x, 4] = roadW;
					maps[z, x, 5] = trailW;
				}
			}

			data.SetAlphamaps(0, 0, maps);
		}

		private static TerrainLayer Layer(string name, Color color, float variation, float tile)
		{
			string texturePath = $"{TerrainFolder}/{name}.png";
			string layerPath   = $"{TerrainFolder}/{name}.terrainlayer";

			var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
			var rng     = new System.Random(name.GetHashCode());
			for (int y = 0; y < 64; ++y)
			{
				for (int x = 0; x < 64; ++x)
				{
					float n = Mathf.PerlinNoise(x * 0.15f + name.Length, y * 0.15f) - 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.6f;
					Color pixel = color * (1.0f + n * variation * 2.0f);
					pixel.a = 1.0f;
					texture.SetPixel(x, y, pixel);
				}
			}

			File.WriteAllBytes(texturePath, texture.EncodeToPNG());
			Object.DestroyImmediate(texture);
			AssetDatabase.ImportAsset(texturePath);

			var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
			importer.wrapMode   = TextureWrapMode.Repeat;
			importer.filterMode = FilterMode.Bilinear;
			importer.SaveAndReimport();

			TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
			if (layer == null)
			{
				layer = new TerrainLayer();
				AssetDatabase.CreateAsset(layer, layerPath);
			}

			layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
			layer.tileSize       = new Vector2(tile, tile);
			layer.smoothness     = 0.05f;
			layer.metallic       = 0.0f;
			EditorUtility.SetDirty(layer);
			return layer;
		}

		private static Material TerrainMaterial()
		{
			string path = $"{TerrainFolder}/TerrainLit.mat";
			var material = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (material == null)
			{
				Shader shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
				if (shader == null)
					return null;

				material = new Material(shader);
				AssetDatabase.CreateAsset(material, path);
			}

			return material;
		}

		// RIVER

		private static void BuildRiver(Context c)
		{
			BrownoutMapSettings s = c.S;
			Transform section = c.Section("River");
			List<Vector2> line = c.RiverLine;
			float halfWidth = c.RiverHalfWidth;

			// Water: a ribbon along the line at the water height, a little wider than the channel so it meets the banks
			var mesh     = new Mesh { name = "RiverWater" };
			var vertices = new List<Vector3>();
			var uvs      = new List<Vector2>();
			var indices  = new List<int>();
			float distance = 0.0f;
			float wide     = halfWidth + 1.0f;

			for (int i = 0; i < line.Count; ++i)
			{
				Vector2 normal = LineNormal(line, i);
				if (i > 0) distance += Vector2.Distance(line[i - 1], line[i]);

				Vector2 left  = line[i] + normal * wide;
				Vector2 right = line[i] - normal * wide;
				vertices.Add(new Vector3(left.x,  c.RiverWater[i], left.y));
				vertices.Add(new Vector3(right.x, c.RiverWater[i], right.y));
				uvs.Add(new Vector2(0.0f, distance / 8.0f));
				uvs.Add(new Vector2(1.0f, distance / 8.0f));

				if (i > 0)
				{
					int v = i * 2;
					indices.AddRange(new[] { v - 2, v, v - 1, v - 1, v, v + 1 });
				}
			}

			mesh.SetVertices(vertices);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(indices, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();

			Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(WaterPath);
			if (saved == null)
			{
				AssetDatabase.CreateAsset(mesh, WaterPath);
				saved = mesh;
			}
			else
			{
				saved.Clear();
				saved.SetVertices(vertices);
				saved.SetUVs(0, uvs);
				saved.SetTriangles(indices, 0);
				saved.RecalculateNormals();
				saved.RecalculateBounds();
				EditorUtility.SetDirty(saved);
				Object.DestroyImmediate(mesh);
			}

			var water = new GameObject("Water");
			water.transform.SetParent(section, false);
			water.AddComponent<MeshFilter>().sharedMesh = saved;
			var renderer = water.AddComponent<MeshRenderer>();
			renderer.sharedMaterial    = WaterMaterial(s);
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			GameObjectUtility.SetStaticEditorFlags(water, StaticEditorFlags.BatchingStatic);

			// The bank wall: collider-only boxes along both banks on the RiverWall layer, lowered to below the deck under each bridge
			Transform walls = new GameObject("BankWall (RiverWall layer)").transform;
			walls.SetParent(section, false);
			int count = 0;

			for (int i = 0; i < line.Count - 1; ++i)
			{
				for (int side = -1; side <= 1; side += 2)
				{
					float   offset = halfWidth + 0.5f + s.WallThickness * 0.5f;
					Vector2 a      = line[i]     + LineNormal(line, i)     * offset * side;
					Vector2 b      = line[i + 1] + LineNormal(line, i + 1) * offset * side;
					Vector2 middle = (a + b) * 0.5f;
					float   bottom = Mathf.Min(c.RiverWater[i], c.RiverWater[i + 1]) - s.WaterDepth - 2.0f;
					float   top    = Mathf.Max(c.RiverWater[i], c.RiverWater[i + 1]) + s.WallHeight;

					foreach (Bridge bridge in c.Bridges)
					{
						if (bridge.Contains(middle, 1.0f) == true)
						{
							top = Mathf.Min(top, bridge.DeckHeight(middle) - s.BridgeThickness - 0.05f);
						}
					}

					if (top <= bottom + 0.2f)
						continue;

					WallBox(walls, $"Wall {i} {(side < 0 ? "L" : "R")}", a, b, bottom, top, s.WallThickness, 0.3f);
					++count;
				}
			}

			c.Log.Add($"River: water ribbon {line.Count} points, {count} bank wall pieces on the RiverWall layer (players only; drops pass through).");
		}

		private static Material WaterMaterial(BrownoutMapSettings s)
		{
			// The sample's water shader, if it's there and works in this pipeline; else a plain glossy material
			var sample = AssetDatabase.LoadAssetAtPath<Material>("Assets/3rdParty/Water/Materials/Water.mat");
			if (sample != null && sample.shader != null && sample.shader.isSupported == true && sample.shader.name != "Hidden/InternalErrorShader")
				return sample;

			return ColorMaterial("Water", s.Water, 0.9f);
		}

		private static void WallBox(Transform parent, string name, Vector2 a, Vector2 b, float bottom, float top, float thickness, float overlap)
		{
			var wall = new GameObject(name);
			wall.transform.SetParent(parent, false);
			wall.layer = RiverWallLayerIndex;
			wall.isStatic = true;

			Vector2 direction = b - a;
			float   length    = direction.magnitude;
			Vector2 middle    = (a + b) * 0.5f;

			wall.transform.position = new Vector3(middle.x, (bottom + top) * 0.5f, middle.y);
			wall.transform.rotation = Quaternion.LookRotation(new Vector3(direction.x, 0.0f, direction.y), Vector3.up);

			var box = wall.AddComponent<BoxCollider>();
			box.size = new Vector3(thickness, top - bottom, length + overlap);
		}

		// BRIDGES

		/// <summary>A bridge where a road crosses the river (the crossing nearest the file's point), along the road's direction there.</summary>
		private static Bridge MakeBridge(Context c, Vector2 point, string style)
		{
			BrownoutMapSettings s = c.S;
			List<Vector2> river = c.RiverLine;

			Vector2 center    = point;
			Vector2 direction = Vector2.zero;
			int     roadIndex = -1;
			float   best      = 60.0f;

			for (int r = 0; r < c.RoadLines.Count; ++r)
			{
				List<Vector2> road = c.RoadLines[r];
				for (int i = 0; i < road.Count - 1; ++i)
				{
					if (Vector2.Distance(road[i], point) > best + 10.0f)
						continue;

					for (int j = 0; j < river.Count - 1; ++j)
					{
						if (SegmentsCross(road[i], road[i + 1], river[j], river[j + 1], out Vector2 hit) == false)
							continue;

						float d = Vector2.Distance(hit, point);
						if (d < best)
						{
							best      = d;
							center    = hit;
							roadIndex = r;
							direction = (road[Mathf.Min(i + 5, road.Count - 1)] - road[Mathf.Max(i - 4, 0)]).normalized;
						}
					}
				}
			}

			DistanceToLine(river, center, out Vector2 riverPoint, out int riverIndex);
			Vector2 tangent = (river[Mathf.Min(riverIndex + 1, river.Count - 1)] - river[Mathf.Max(0, riverIndex - 1)]).normalized;
			if (roadIndex < 0)
			{
				center    = riverPoint;
				direction = new Vector2(-tangent.y, tangent.x);
			}

			float sin  = Mathf.Abs(tangent.x * direction.y - tangent.y * direction.x);
			float half = (s.ValleyHalfWidth + s.BridgeOverhang) / Mathf.Max(0.5f, sin);

			return new Bridge
			{
				Style        = style,
				Road         = roadIndex,
				A            = center - direction * half,
				B            = center + direction * half,
				Width        = s.BridgeWidth,
				RiverPoint   = center,
				RiverTangent = tangent,
				Sin          = Mathf.Max(0.5f, sin),
			};
		}

		/// <summary>Each bridge's road runs straight along the deck and a little past both ends, easing back into its curve.</summary>
		private static void StraightenRoads(Context c)
		{
			foreach (Bridge bridge in c.Bridges)
			{
				if (bridge.Road < 0)
					continue;

				List<Vector2> road   = c.RoadLines[bridge.Road];
				Vector2       center = (bridge.A + bridge.B) * 0.5f;
				Vector2       along  = bridge.Direction;
				float         half   = bridge.Length * 0.5f;
				float         inner  = half + c.S.BridgeRampLength * 0.5f;
				float         outer  = half + c.S.BridgeRampLength * 1.5f;

				for (int i = 0; i < road.Count; ++i)
				{
					Vector2 local   = road[i] - center;
					float   a       = Vector2.Dot(local, along);
					float   lateral = Mathf.Abs(local.x * along.y - local.y * along.x);
					if (Mathf.Abs(a) > outer || lateral > 30.0f)
						continue;

					float w = 1.0f - Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(inner, outer, Mathf.Abs(a)));
					road[i] = Vector2.Lerp(road[i], center + along * a, w);
				}
			}
		}

		private static void ShapeBridgeGround(Context c, float[,] h, int res, float step, float origin)
		{
			BrownoutMapSettings s = c.S;

			for (int i = 0; i < c.Bridges.Count; ++i)
			{
				Bridge bridge = c.Bridges[i];
				float  water  = c.RiverWater[Mathf.Clamp(NearestIndex(c.RiverLine, bridge.RiverPoint), 0, c.RiverWater.Length - 1)];
				float  ends   = Mathf.Max(SampleGrid(h, res, step, origin, bridge.A), SampleGrid(h, res, step, origin, bridge.B));

				bridge.Deck    = Mathf.Max(ends, water + s.BridgeClearance + s.BridgeThickness);
				bridge.HeightA = bridge.Deck;
				bridge.HeightB = bridge.Deck;
				c.Bridges[i]   = bridge;

				Vector2 along  = bridge.Direction;
				Vector2 across = new Vector2(along.y, -along.x);
				Vector2 center = (bridge.A + bridge.B) * 0.5f;
				float   half   = bridge.Length * 0.5f;
				float   span   = half - s.BridgeOverhang;            // Between the abutments
				float   edge   = bridge.Width * 0.5f + 0.5f;
				float   reach  = half + s.BridgeRampLength;
				float   side   = edge + s.RoadShoulder;
				float   under  = bridge.Deck - s.BridgeThickness - 1.0f;
				float   box    = reach + side;

				int x0 = Mathf.Max(0,       Mathf.FloorToInt((center.x - box - origin) / step));
				int x1 = Mathf.Min(res - 1, Mathf.CeilToInt ((center.x + box - origin) / step));
				int z0 = Mathf.Max(0,       Mathf.FloorToInt((center.y - box - origin) / step));
				int z1 = Mathf.Min(res - 1, Mathf.CeilToInt ((center.y + box - origin) / step));

				for (int z = z0; z <= z1; ++z)
				{
					for (int x = x0; x <= x1; ++x)
					{
						Vector2 local   = new Vector2(origin + x * step, origin + z * step) - center;
						float   a       = Mathf.Abs(Vector2.Dot(local, along));
						float   lateral = Mathf.Abs(Vector2.Dot(local, across));
						if (a > reach || lateral > side)
							continue;

						float w = lateral <= edge ? 1.0f : 1.0f - Mathf.SmoothStep(0.0f, 1.0f, (lateral - edge) / Mathf.Max(0.01f, s.RoadShoulder));

						if (a <= span)
						{
							// Under the deck: cut away anything that would poke through
							if (lateral <= edge + 1.0f)
							{
								h[z, x] = Mathf.Min(h[z, x], under);
							}
						}
						else if (a <= half)
						{
							// Abutment: flat at the deck's height
							h[z, x] = Mathf.Lerp(h[z, x], bridge.Deck, w);
						}
						else
						{
							// Approach: the road ramps from the deck back to the ground
							float t = Mathf.SmoothStep(0.0f, 1.0f, (a - half) / s.BridgeRampLength);
							h[z, x] = Mathf.Lerp(h[z, x], Mathf.Lerp(bridge.Deck, h[z, x], t), w);
						}
					}
				}
			}
		}

		/// <summary>
		/// The five bridges, each in its style from map_layout.json (placeholder blocks): concrete (solid parapets, piers by the
		/// channel), truss (steel trusses both sides with beams overhead), arch (stone arch under the deck, stone parapets), wood
		/// (plank deck, open rails, trestle bents) and girder (deep steel side girders as walls, one pier mid-river). Every style keeps a
		/// level deck named "Deck" (the Map Check walks it) and abutments at both ends.
		/// </summary>
		private static void BuildBridges(Context c)
		{
			BrownoutMapSettings s = c.S;
			Transform section = c.Section("Bridges");

			Material concrete     = ColorMaterial("Bridge",       s.Bridge,        0.15f);
			Material concreteDark = ColorMaterial("BridgeRail",   s.Bridge * 0.8f, 0.15f);
			Material steel        = ColorMaterial("BridgeSteel",  s.Steel,         0.35f);
			Material stone        = ColorMaterial("BridgeStone",  s.Stone,         0.05f);
			Material stoneDark    = ColorMaterial("BridgeStoneDark", s.Stone * 0.8f, 0.05f);
			Material wood         = ColorMaterial("BridgeWood",   s.Wood,          0.05f);
			Material planks       = ColorMaterial("BridgePlanks", s.Wood * 1.2f,   0.05f);

			for (int i = 0; i < c.Bridges.Count; ++i)
			{
				Bridge  bridge = c.Bridges[i];
				Vector2 file   = BrownoutMapData.ToFile(c.Layout.Bridges[i]);
				Transform group = new GameObject($"Bridge E{file.x:F0} N{file.y:F0} ({bridge.Style})").transform;
				group.SetParent(section, false);

				Vector2    direction = bridge.Direction;
				Vector3    forward   = new Vector3(direction.x, 0.0f, direction.y);
				Quaternion rotation  = Quaternion.LookRotation(forward, Vector3.up);
				Vector3    right     = rotation * Vector3.right;
				Vector2    middle    = (bridge.A + bridge.B) * 0.5f;
				Vector3    origin    = new Vector3(middle.x, bridge.Deck, middle.y);

				float length = bridge.Length;
				float half   = length * 0.5f;
				float width  = bridge.Width;
				float thick  = s.BridgeThickness;
				float under  = -thick;                                           // Deck underside, relative to the deck top
				float water  = c.RiverWater[Mathf.Clamp(NearestIndex(c.RiverLine, bridge.RiverPoint), 0, c.RiverWater.Length - 1)] - bridge.Deck;
				float bed    = water - s.WaterDepth - 0.5f;
				float span   = half - s.BridgeOverhang;                          // Abutment faces
				float bank   = (c.RiverHalfWidth + 1.5f) / bridge.Sin;           // Just outside the channel

				void Part(string name, float along, float side, float up, Vector3 size, Material material, float pitch = 0.0f, float roll = 0.0f)
				{
					Vector3 center = origin + forward * along + right * side + Vector3.up * up;
					Block(group, name, center, rotation * Quaternion.Euler(pitch, 0.0f, roll), size, material);
				}

				// A box from (along0, up0) to (along1, up1) in the bridge's side plane
				void Strut(string name, float along0, float up0, float along1, float up1, float side, float thickness, Material material)
				{
					float da = along1 - along0;
					float du = up1 - up0;
					float pitch = -Mathf.Atan2(du, da) * Mathf.Rad2Deg;
					Part(name, (along0 + along1) * 0.5f, side, (up0 + up1) * 0.5f, new Vector3(thickness, thickness, Mathf.Sqrt(da * da + du * du)), material, pitch);
				}

				Material deckMaterial = bridge.Style == "wood" ? planks : bridge.Style == "arch" ? stone : concrete;
				Material endMaterial  = bridge.Style == "arch" ? stoneDark : bridge.Style == "wood" ? stoneDark : concreteDark;

				// The deck (level, its top at the deck height) and an abutment under each end
				Part("Deck", 0.0f, 0.0f, under * 0.5f, new Vector3(width, thick, length), deckMaterial);
				for (int end = -1; end <= 1; end += 2)
				{
					Part(end < 0 ? "Abutment A" : "Abutment B", end * (half - s.BridgeOverhang * 0.5f), 0.0f, under - 3.0f, new Vector3(width + 1.0f, 6.0f, s.BridgeOverhang), endMaterial);
				}

				switch (bridge.Style)
				{
					case "truss":
					{
						const float height = 5.0f;
						int   panels = Mathf.Max(4, Mathf.RoundToInt((length - 2.0f) / 4.5f));
						float panel  = (length - 2.0f) / panels;
						float first  = -half + 1.0f;

						for (int side = -1; side <= 1; side += 2)
						{
							float x = side * (width * 0.5f - 0.2f);
							Part("Bottom Chord", 0.0f, x, 0.2f,   new Vector3(0.35f, 0.4f, length - 2.0f), steel);
							Part("Top Chord",    0.0f, x, height, new Vector3(0.35f, 0.4f, length - 2.0f), steel);

							for (int k = 0; k <= panels; ++k)
							{
								Part("Post", first + k * panel, x, height * 0.5f, new Vector3(0.3f, height, 0.3f), steel);
							}

							// Pratt diagonals, sloping down toward the middle
							for (int k = 0; k < panels; ++k)
							{
								float a0 = first + k * panel;
								float a1 = a0 + panel;
								if (k < panels / 2) Strut("Diagonal", a0, height, a1, 0.2f, x, 0.25f, steel);
								else                Strut("Diagonal", a0, 0.2f, a1, height, x, 0.25f, steel);
							}
						}

						for (int k = 0; k <= panels; k += 2)
						{
							Part("Cross Beam", first + k * panel, 0.0f, height, new Vector3(width - 0.4f, 0.3f, 0.3f), steel);
						}

						// Floor beams under the deck
						for (int k = 0; k <= panels; ++k)
						{
							Part("Floor Beam", first + k * panel, 0.0f, under - 0.3f, new Vector3(width, 0.6f, 0.4f), steel);
						}
						break;
					}

					case "arch":
					{
						// A stone arch from the abutment faces down to the water, its crown just under the deck, with spandrel walls above
						float crown  = under - 0.1f;
						float spring = water + 0.3f;
						float rise   = crown - spring;
						const int pieces = 16;

						for (int k = 0; k < pieces; ++k)
						{
							float t0 = Mathf.PI * (1.0f - k / (float)pieces);
							float t1 = Mathf.PI * (1.0f - (k + 1) / (float)pieces);
							float a0 = span * Mathf.Cos(t0), u0 = spring + rise * Mathf.Sin(t0);
							float a1 = span * Mathf.Cos(t1), u1 = spring + rise * Mathf.Sin(t1);

							float da = a1 - a0, du = u1 - u0;
							float pitch = -Mathf.Atan2(du, da) * Mathf.Rad2Deg;
							Part("Arch", (a0 + a1) * 0.5f, 0.0f, (u0 + u1) * 0.5f - 0.45f, new Vector3(width - 0.6f, 0.9f, Mathf.Sqrt(da * da + du * du) + 0.2f), stoneDark, pitch);

							// Spandrel walls fill between the arch and the deck on both faces
							float top = under;
							float low = Mathf.Max(u0, u1);
							if (top - low > 0.2f)
							{
								for (int side = -1; side <= 1; side += 2)
								{
									Part("Spandrel", (a0 + a1) * 0.5f, side * (width * 0.5f - 0.3f), (top + low) * 0.5f, new Vector3(0.6f, top - low, Mathf.Abs(da) + 0.05f), stone);
								}
							}
						}

						for (int side = -1; side <= 1; side += 2)
						{
							float x = side * (width * 0.5f - 0.25f);
							Part("Parapet",     0.0f, x, 0.5f,  new Vector3(0.5f, 1.0f, length), stone);
							Part("Parapet Cap", 0.0f, x, 1.07f, new Vector3(0.7f, 0.15f, length), stoneDark);
						}
						break;
					}

					case "wood":
					{
						float bottom = Mathf.Max(bed, water - 1.0f);

						// Open rails: posts, a top rail and a middle rail
						for (int side = -1; side <= 1; side += 2)
						{
							float x = side * (width * 0.5f - 0.1f);
							int posts = Mathf.CeilToInt(length / 2.0f);
							for (int k = 0; k <= posts; ++k)
							{
								Part("Rail Post", -half + k * (length / posts), x, 0.55f, new Vector3(0.18f, 1.1f, 0.18f), wood);
							}
							Part("Top Rail", 0.0f, x, 1.05f, new Vector3(0.12f, 0.12f, length), wood);
							Part("Mid Rail", 0.0f, x, 0.55f, new Vector3(0.10f, 0.10f, length), wood);
						}

						// Stringers under the planks, and trestle bents between the abutments
						for (int k = -1; k <= 1; ++k)
						{
							Part("Stringer", 0.0f, k * (width * 0.5f - 1.0f), under - 0.25f, new Vector3(0.3f, 0.5f, length - 1.0f), wood);
						}

						int bents = Mathf.Max(2, Mathf.RoundToInt(span * 2.0f / 6.0f));
						for (int k = 0; k <= bents; ++k)
						{
							float a   = -span + k * (span * 2.0f / bents);
							float top = under - 0.5f;
							float leg = top - bottom;
							if (leg < 0.5f)
								continue;

							float x = width * 0.5f - 1.0f;
							Part("Bent Post", a, -x, (top + bottom) * 0.5f, new Vector3(0.35f, leg, 0.35f), wood);
							Part("Bent Post", a,  x, (top + bottom) * 0.5f, new Vector3(0.35f, leg, 0.35f), wood);
							Part("Bent Cap",  a, 0.0f, top - 0.15f, new Vector3(width, 0.3f, 0.4f), wood);

							float brace = Mathf.Atan2(leg, x * 2.0f) * Mathf.Rad2Deg;
							float bar   = Mathf.Sqrt(leg * leg + x * x * 4.0f);
							Part("Brace", a, 0.0f, (top + bottom) * 0.5f, new Vector3(bar, 0.2f, 0.2f), wood, 0.0f,  brace);
							Part("Brace", a, 0.0f, (top + bottom) * 0.5f, new Vector3(bar, 0.2f, 0.2f), wood, 0.0f, -brace);
						}
						break;
					}

					case "girder":
					{
						// Deep steel side girders that stand up past the deck as walls, stiffened outside, on one pier mid-river
						const float above = 1.4f;
						const float below = 1.2f;
						for (int side = -1; side <= 1; side += 2)
						{
							float x = side * (width * 0.5f - 0.2f);
							Part("Girder", 0.0f, x, (above + under - below) * 0.5f, new Vector3(0.35f, above - under + below, length - 1.0f), steel);
							Part("Flange", 0.0f, x, above + 0.06f, new Vector3(0.7f, 0.12f, length - 1.0f), steel);

							int ribs = Mathf.CeilToInt((length - 1.0f) / 2.5f);
							for (int k = 0; k <= ribs; ++k)
							{
								float a = -half + 0.5f + k * ((length - 1.0f) / ribs);
								Part("Stiffener", a, side * (width * 0.5f + 0.05f), (above + under - below) * 0.5f, new Vector3(0.15f, above - under + below - 0.2f, 0.15f), steel);
							}
						}

						float pier = under - below - bed;
						Part("Pier", 0.0f, 0.0f, bed + pier * 0.5f, new Vector3(width - 1.0f, pier, 1.5f), concreteDark);
						break;
					}

					default: // concrete
					{
						// Solid parapets (cover on the crossing), two beams under the deck and a pier on each side of the channel
						for (int side = -1; side <= 1; side += 2)
						{
							Part("Parapet", 0.0f, side * (width * 0.5f - 0.2f), 0.55f, new Vector3(0.4f, 1.1f, length), concreteDark);
							Part("Beam",    0.0f, side * (width * 0.5f - 1.5f), under - 0.4f, new Vector3(0.6f, 0.8f, length - 1.0f), concreteDark);
						}

						float pier = under - 0.8f - bed;
						for (int side = -1; side <= 1; side += 2)
						{
							Part("Pier", side * bank, 0.0f, bed + pier * 0.5f, new Vector3(width - 1.0f, pier, 1.2f), concrete);
						}
						break;
					}
				}

				// Over the channel, invisible walls along the deck edges keep anyone from jumping off into the water
				float   overChannel = (c.RiverHalfWidth * 2.0f + 2.0f) / bridge.Sin;
				Vector2 across      = new Vector2(direction.y, -direction.x);
				float   waterWorld  = water + bridge.Deck;

				for (int side = -1; side <= 1; side += 2)
				{
					Vector2 edge = bridge.RiverPoint + across * side * (width * 0.5f + s.WallThickness * 0.5f);
					Vector2 from = edge - direction * overChannel * 0.5f;
					Vector2 to   = edge + direction * overChannel * 0.5f;
					WallBox(group, side < 0 ? "Channel Wall L (RiverWall layer)" : "Channel Wall R (RiverWall layer)", from, to, waterWorld - s.WaterDepth - 1.0f, bridge.Deck + s.WallHeight, s.WallThickness, 0.0f);
				}
			}

			c.Log.Add($"Bridges: {string.Join(", ", c.Bridges.Select(b => $"{b.Style} {b.Length:F0} m, deck {b.Deck:F1} m{(b.Road < 0 ? " (no road crosses here)" : string.Empty)}"))}; {s.BridgeWidth:F0} m wide, centered on their road's crossing.");
		}

		private static void Block(Transform parent, string name, Vector3 center, Quaternion rotation, Vector3 size, Material material)
		{
			var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
			block.name = name;
			block.transform.SetParent(parent, false);
			block.transform.position   = center;
			block.transform.rotation   = rotation;
			block.transform.localScale = size;
			block.layer    = 0;
			block.isStatic = true;
			block.GetComponent<MeshRenderer>().sharedMaterial = material;
		}

		// SPAWNS

		/// <summary>Warm-up spawn points: two at each POI and one per cabin area, facing the map's center (the sample's spawn points go).</summary>
		public static void BuildSpawns(Context c)
		{
			foreach (SpawnPoint old in Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				if (old != null && old.gameObject.scene == c.Scene)
				{
					Object.DestroyImmediate(old.gameObject);
				}
			}

			Transform section = c.Section("Spawns");
			int count = 0;

			foreach (BrownoutMapData.Box box in c.Layout.AllAreas())
			{
				Vector2[] offsets = box.IsCabins == true ? new[] { Vector2.zero } : new[] { new Vector2(-14.0f, 0.0f), new Vector2(14.0f, 0.0f) };
				for (int i = 0; i < offsets.Length; ++i)
				{
					Vector2 point = FindClearGround(c, box.Center + offsets[i], 30.0f);
					var spawn = new GameObject($"Spawn {box.Name} {i + 1}");
					spawn.transform.SetParent(section, false);
					spawn.transform.position = new Vector3(point.x, c.GroundHeight(point) + 0.1f, point.y);
					Vector2 toCenter = -point;
					spawn.transform.rotation = Quaternion.LookRotation(toCenter.sqrMagnitude > 1.0f ? new Vector3(toCenter.x, 0.0f, toCenter.y) : Vector3.forward);
					spawn.AddComponent<SpawnPoint>();
					++count;
				}
			}

			c.Log.Add($"Spawns: {count} warm-up spawn points.");
		}

		private static Vector2 FindClearGround(Context c, Vector2 point, float search)
		{
			for (float radius = 0.0f; radius <= search; radius += 3.0f)
			{
				for (int k = 0; k < 12; ++k)
				{
					float   angle     = k * Mathf.PI / 6.0f;
					Vector2 candidate = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
					if (c.RiverDistance(candidate) > c.S.ValleyHalfWidth + 2.0f && c.GroundSlope(candidate) < 20.0f && c.IsOnBridge(candidate, 2.0f) == false && c.IsInBuilding(candidate) == false && IsClear(candidate, c))
						return candidate;

					if (radius == 0.0f)
						break;
				}
			}

			return point;
		}

		private static bool IsClear(Vector2 point, Context c)
		{
			Vector3 at = new Vector3(point.x, c.GroundHeight(point) + 1.0f, point.y);
			return Physics.CheckSphere(at, 0.6f, 1, QueryTriggerInteraction.Ignore) == false;
		}

		// LOOT

		/// <summary>Scatters each spot's loot points inside its circle on its floors (the ground, a building's floors, the underground), spaced out and clear of walls, trees, rocks and the river.</summary>
		public static void BuildLoot(Context c)
		{
			BrownoutMapSettings s = c.S;
			BrownoutMapData.LootFile file = BrownoutMapData.LoadLoot();
			Transform section = c.Section("Loot");

			Physics.SyncTransforms();
			List<Vector2> trunks = BrownoutNatureBuilder.GetTrunks(c);
			List<Vector3> bushes = BrownoutNatureBuilder.GetBushes(c);

			int total = 0;
			var report = new List<string>();

			foreach (BrownoutMapData.LootArea area in file.areas)
			{
				Transform areaGroup = new GameObject(area.name).transform;
				areaGroup.SetParent(section, false);
				int areaCount = 0;

				foreach (BrownoutMapData.LootSpot spot in area.spots)
				{
					Transform spotGroup = new GameObject($"{spot.name} ({spot.count}{(spot.top_tier ? ", top tier" : "")})").transform;
					spotGroup.SetParent(areaGroup, false);

					var rng    = new System.Random(s.Seed * 7919 + StableHash(area.id + "/" + spot.name));
					var placed = new List<Vector3>(spot.count);
					Vector2 center = spot.World;
					int[]   floors = spot.floors != null && spot.floors.Length > 0 ? spot.floors : new[] { 0 };

					for (int attempt = 0; attempt < spot.count * 80 && placed.Count < spot.count; ++attempt)
					{
						float   angle  = (float)(rng.NextDouble() * Mathf.PI * 2.0);
						float   radius = spot.radius * Mathf.Sqrt((float)rng.NextDouble());
						Vector2 point  = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
						int     floor  = floors[placed.Count % floors.Length];

						if (c.IsOnMap(point, 2.0f) == false)                                                  continue;
						if (c.RiverDistance(point) < c.RiverHalfWidth + 1.5f)                                  continue;
						if (FindFloor(c, point, floor, out float y, out bool roofed, out bool onTerrain) == false) continue;
						if (spot.indoors == true && roofed == false)                                          continue;
						if (onTerrain == true && c.GroundSlope(point) > s.LootMaxSlope)                       continue;
						if (TooClose(placed, new Vector3(point.x, y, point.y), s.LootSpacing) == true)        continue;
						if (onTerrain == true && NearTrunk(trunks, point, s.LootTreeClearance) == true)        continue;
						if (onTerrain == true && NearBush(bushes, point, 0.6f) == true)                          continue;
						if (IsClearAt(new Vector3(point.x, y, point.y)) == false)                              continue;

						placed.Add(new Vector3(point.x, y, point.y));
					}

					for (int i = 0; i < placed.Count; ++i)
					{
						var lootPoint = new GameObject($"Loot {i + 1}");
						lootPoint.transform.SetParent(spotGroup, false);
						lootPoint.transform.position = placed[i] + Vector3.up * 0.05f;
						lootPoint.AddComponent<LootPoint>().TopTier = spot.top_tier;
					}

					if (placed.Count < spot.count)
					{
						report.Add($"  {area.name} / {spot.name}: only {placed.Count} of {spot.count} fit (radius {spot.radius:F0} m)");
					}

					areaCount += placed.Count;
				}

				total += areaCount;
				report.Add($"  {area.name}: {areaCount} points ({area.density})");
			}

			c.Log.Add($"Loot: {total} loot points in {file.areas.Count} areas.\n{string.Join("\n", report)}");
		}

		/// <summary>
		/// A floor at a point (phase 8b): every walkable surface with room above it, found by a ray down. Floor 0 is the lowest one at or
		/// above the ground (the ground itself, or a building's ground floor), 1, 2... the ones above (a roof counts); -1 is the lowest one
		/// well under the ground (the underground). Roofed: something overhead.
		/// </summary>
		private static bool FindFloor(Context c, Vector2 point, int floor, out float y, out bool roofed, out bool onTerrain)
		{
			y         = 0.0f;
			roofed    = false;
			onTerrain = false;

			RaycastHit[] hits    = Physics.RaycastAll(new Vector3(point.x, 400.0f, point.y), Vector3.down, 600.0f, 1, QueryTriggerInteraction.Ignore);
			float        ground  = c.GroundHeight(point);
			var          above   = new List<(float Y, bool Terrain)>();
			var          below   = new List<(float Y, bool Terrain)>();

			foreach (RaycastHit hit in hits)
			{
				if (hit.normal.y < 0.7f)
					continue;

				float top = hit.point.y;
				if (Physics.CheckCapsule(new Vector3(point.x, top + 0.45f, point.y), new Vector3(point.x, top + 1.4f, point.y), 0.3f, 1, QueryTriggerInteraction.Ignore) == true)
					continue;

				bool terrain = hit.collider is TerrainCollider;
				if (top >= ground - 0.6f)      above.Add((top, terrain));
				else if (top < ground - 1.5f)  below.Add((top, terrain));
			}

			above.Sort((a, b) => a.Y.CompareTo(b.Y));
			below.Sort((a, b) => a.Y.CompareTo(b.Y));

			if (floor < 0)
			{
				if (below.Count == 0)
					return false;

				y      = below[0].Y;
				roofed = true;
				return true;
			}

			if (floor >= above.Count)
				return false;

			y         = above[floor].Y;
			onTerrain = above[floor].Terrain;
			roofed    = Physics.Raycast(new Vector3(point.x, y + 0.5f, point.y), Vector3.up, 40.0f, 1, QueryTriggerInteraction.Ignore);
			return true;
		}

		private static bool IsClearAt(Vector3 floorPoint)
		{
			return Physics.CheckSphere(floorPoint + Vector3.up * 1.0f, 0.6f, 1, QueryTriggerInteraction.Ignore) == false;
		}

		private static bool TooClose(List<Vector3> points, Vector3 point, float spacing)
		{
			foreach (Vector3 other in points)
			{
				if (Mathf.Abs(other.y - point.y) < 1.5f && (new Vector2(other.x - point.x, other.z - point.z)).sqrMagnitude < spacing * spacing)
					return true;
			}

			return false;
		}

		/// <summary>Within a solid bush (phase 9) plus clearance; each entry is X, Z and the collider's radius.</summary>
		private static bool NearBush(List<Vector3> bushes, Vector2 point, float clearance)
		{
			foreach (Vector3 bush in bushes)
			{
				float reach = bush.z + clearance;
				float dx = bush.x - point.x;
				float dz = bush.y - point.y;
				if (dx * dx + dz * dz < reach * reach)
					return true;
			}

			return false;
		}

		private static bool NearTrunk(List<Vector2> trunks, Vector2 point, float clearance)
		{
			foreach (Vector2 trunk in trunks)
			{
				if ((trunk - point).sqrMagnitude < clearance * clearance)
					return true;
			}

			return false;
		}

		// MAP PICTURE

		/// <summary>Renders the 1000 m map from straight above into BrownoutMap.jpg, used by the minimap and the full map.</summary>
		public static void BuildPicture(Context c)
		{
			BrownoutMapSettings s = c.S;
			int resolution = Mathf.Clamp(s.MapImageResolution, 256, 4096);

			// The zone wall and other runtime visuals stay out of the picture
			var hidden = new List<GameObject>();
			foreach (ShrinkingArea zone in Object.FindObjectsByType<ShrinkingArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
			{
				if (zone.gameObject.scene == c.Scene)
				{
					hidden.Add(zone.gameObject);
					zone.gameObject.SetActive(false);
				}
			}

			NatureInstancer instancer = c.Terrain.GetComponent<NatureInstancer>();
			if (instancer != null) instancer.Rebuild();
			NatureInstancer.DrawEverything = true;
			float treeDistance = c.Terrain.treeDistance;
			float detailDistance = c.Terrain.detailObjectDistance;
			float basemapDistance = c.Terrain.basemapDistance;
			float pixelError = c.Terrain.heightmapPixelError;
			c.Terrain.heightmapPixelError = 1.0f;
			c.Terrain.detailObjectDistance = 0.0f;
			c.Terrain.basemapDistance = 5000.0f;
			bool fog = RenderSettings.fog;
			RenderSettings.fog = false;

			var cameraObject = new GameObject("MapPictureCamera");
			var camera = cameraObject.AddComponent<Camera>();
			camera.orthographic     = true;
			camera.orthographicSize = c.HalfMap;
			camera.aspect           = 1.0f;
			camera.nearClipPlane    = 1.0f;
			camera.farClipPlane     = 1000.0f;
			camera.clearFlags       = CameraClearFlags.SolidColor;
			camera.backgroundColor  = Color.black;
			camera.cullingMask      = ~((1 << LayerMask.NameToLayer("UI")) | (1 << LayerMask.NameToLayer("FPV")));
			camera.transform.position = new Vector3(0.0f, 500.0f, 0.0f);
			camera.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);

			var target = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
			camera.targetTexture = target;
			camera.Render();

			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = target;
			var picture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
			picture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
			picture.Apply();
			RenderTexture.active = previous;

			File.WriteAllBytes(PicturePath, picture.EncodeToJPG(92));

			camera.targetTexture = null;
			Object.DestroyImmediate(picture);
			Object.DestroyImmediate(cameraObject);
			target.Release();
			Object.DestroyImmediate(target);

			NatureInstancer.DrawEverything = false;
			c.Terrain.treeDistance = treeDistance;
			c.Terrain.detailObjectDistance = detailDistance;
			c.Terrain.basemapDistance = basemapDistance;
			c.Terrain.heightmapPixelError = pixelError;
			RenderSettings.fog = fog;
			foreach (GameObject gameObject in hidden)
			{
				gameObject.SetActive(true);
			}

			AssetDatabase.ImportAsset(PicturePath);
			var importer = (TextureImporter)AssetImporter.GetAtPath(PicturePath);
			importer.textureType    = TextureImporterType.Default;
			importer.wrapMode       = TextureWrapMode.Clamp;
			importer.mipmapEnabled  = true;
			importer.maxTextureSize = Mathf.Max(2048, resolution);
			importer.textureCompression = TextureImporterCompression.CompressedHQ;
			importer.SaveAndReimport();

			SceneMap sceneMap = Object.FindObjectsByType<SceneMap>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.gameObject.scene == c.Scene);
			if (sceneMap != null)
			{
				var serialized = new SerializedObject(sceneMap);
				serialized.FindProperty("_bakedTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(PicturePath);
				serialized.ApplyModifiedPropertiesWithoutUndo();
			}

			c.Log.Add($"Map picture: {PicturePath} ({resolution}², {c.Layout.Size / resolution:F2} m a pixel).");
		}

		// HEIGHT REPORT

		/// <summary>Logs the ground by band, how steep it is, and how much of the map each hilltop sees (bare ground, no trees).</summary>
		private static void ReportHeights(Context c, float[,] h, int res, float step, float origin, TerrainData data)
		{
			float half = c.HalfMap;
			float size = data.size.x;

			var bands = new SortedDictionary<float, (Vector2 Band, float Sum, float Min, float Max, int Count)>();
			int count = 0, over10 = 0, over20 = 0, over30 = 0;
			for (float z = -half + 2.0f; z < half; z += 4.0f)
			{
				for (float x = -half + 2.0f; x < half; x += 4.0f)
				{
					Vector2 p = new Vector2(x, z);
					float   y = SampleGrid(h, res, step, origin, p);
					Vector2 band = c.Heights.BandAt(p);

					bands.TryGetValue(band.x, out var entry);
					if (entry.Count == 0) { entry.Band = band; entry.Min = float.MaxValue; entry.Max = float.MinValue; }
					entry.Sum += y; entry.Min = Mathf.Min(entry.Min, y); entry.Max = Mathf.Max(entry.Max, y); ++entry.Count;
					bands[band.x] = entry;

					float slope = data.GetSteepness((x - origin) / size, (z - origin) / size);
					++count;
					if (slope > 10.0f) ++over10;
					if (slope > 20.0f) ++over20;
					if (slope > 30.0f) ++over30;
				}
			}

			var text = new StringBuilder("Heights by band (ground on the map, every 4 m): ");
			text.Append(string.Join("; ", bands.Values.Select(t => $"{t.Band.x:F0}–{t.Band.y:F0} m zones: average {t.Sum / t.Count:F1} m, {t.Min:F1} to {t.Max:F1} m, {100.0f * t.Count / count:F0}% of the map")));
			text.Append($". Slopes: {100.0f * over10 / count:F0}% steeper than 10°, {100.0f * over20 / count:F0}% than 20°, {100.0f * over30 / count:F1}% than 30°.");
			c.Log.Add(text.ToString());

			// Sight lines from the ground: from random spots on the map (away from the river), eye at 1.7 m, how much of the ground
			// within 200 m (every 10 m) would show a standing player (head at 1.5 m)
			{
				var   rng = new System.Random(c.S.Seed + 77);
				int   spots = 0;
				float shareSum = 0.0f;
				float shareNear = 0.0f;
				while (spots < 400)
				{
					Vector2 from = new Vector2((float)rng.NextDouble() * (2.0f * half - 100.0f) - half + 50.0f, (float)rng.NextDouble() * (2.0f * half - 100.0f) - half + 50.0f);
					if (c.Heights.RiverDistance(from.x, from.y) < 20.0f)
						continue;

					Vector3 eye = new Vector3(from.x, SampleGrid(h, res, step, origin, from) + 1.7f, from.y);
					int seen = 0, all = 0, seenNear = 0, near = 0;
					for (float dz = -200.0f; dz <= 200.0f; dz += 10.0f)
					{
						for (float dx = -200.0f; dx <= 200.0f; dx += 10.0f)
						{
							float distance = Mathf.Sqrt(dx * dx + dz * dz);
							if (distance < 10.0f || distance > 200.0f)
								continue;

							Vector2 p = from + new Vector2(dx, dz);
							bool visible = SeesGround(h, res, step, origin, eye, p);
							++all; if (visible == true) ++seen;
							if (distance <= 100.0f) { ++near; if (visible == true) ++seenNear; }
						}
					}

					shareSum  += seen / (float)all;
					shareNear += seenNear / (float)Mathf.Max(1, near);
					++spots;
				}

				c.Log.Add($"Sight lines from the ground ({spots} random spots, eye 1.7 m, bare ground): a standing player is in sight on {100.0f * shareSum / spots:F0}% of the ground within 200 m, {100.0f * shareNear / spots:F0}% within 100 m.");
			}

			// Lookouts: the highest point of each top zone (those with the highest band), eye at 1.7 m
			c.Lookouts.Clear();
			float topBand = c.Layout.HeightZones.Max(t => t.Band.x);
			foreach (BrownoutMapData.HeightZone zone in c.Layout.HeightZones.Where(t => t.Band.x == topBand))
			{
				Vector2 best = Vector2.zero;
				float   bestY = float.MinValue;
				for (float z = -half + 20.0f; z < half - 20.0f; z += 4.0f)
				{
					for (float x = -half + 20.0f; x < half - 20.0f; x += 4.0f)
					{
						Vector2 p = new Vector2(x, z);
						if (BrownoutMapHeights.Contains(zone.Outline, p) == false)
							continue;

						float y = SampleGrid(h, res, step, origin, p);
						if (y > bestY) { bestY = y; best = p; }
					}
				}

				if (bestY == float.MinValue)
					continue;

				Vector3 eye = new Vector3(best.x, bestY + 1.7f, best.y);
				c.Lookouts.Add((zone.Name, eye));

				// A standing player (head at 1.5 m) at every 10 m of the map: seen when no ground rises above the line between
				int seen = 0, all = 0, seenNear = 0, near = 0, seenMid = 0, mid = 0;
				for (float z = -half + 5.0f; z < half; z += 10.0f)
				{
					for (float x = -half + 5.0f; x < half; x += 10.0f)
					{
						Vector2 p        = new Vector2(x, z);
						float   distance = Vector2.Distance(p, best);
						if (distance < 10.0f)
							continue;

						bool visible = SeesGround(h, res, step, origin, eye, p);

						++all;
						if (visible == true) ++seen;
						if (distance <= 250.0f) { ++near; if (visible == true) ++seenNear; }
						if (distance <= 500.0f) { ++mid;  if (visible == true) ++seenMid; }
					}
				}

				Vector2 file = BrownoutMapData.ToFile(best);
				c.Log.Add($"Lookout {zone.Name} (E{file.x:F0} N{file.y:F0}, ground {bestY:F1} m): a standing player is in sight on {100.0f * seen / all:F0}% of the map ({100.0f * seenNear / Mathf.Max(1, near):F0}% within 250 m, {100.0f * seenMid / Mathf.Max(1, mid):F0}% within 500 m), bare ground without trees.");
			}
		}

		/// <summary>True when no ground rises above the line from the eye to a standing player's head (1.5 m) at the point.</summary>
		private static bool SeesGround(float[,] h, int res, float step, float origin, Vector3 eye, Vector2 point)
		{
			Vector3 target = new Vector3(point.x, SampleGrid(h, res, step, origin, point) + 1.5f, point.y);
			int     steps  = Mathf.CeilToInt(Vector2.Distance(new Vector2(eye.x, eye.z), point) / 2.0f);
			for (int i = 1; i < steps - 1; ++i)
			{
				Vector3 p = Vector3.Lerp(eye, target, i / (float)steps);
				if (SampleGrid(h, res, step, origin, new Vector2(p.x, p.z)) > p.y)
					return false;
			}

			return true;
		}

		/// <summary>After a Ground build: four views (N, E, S, W) from each lookout, side by side, in Logs/MapCheck/lookout_*.png.</summary>
		private static void BuildLookoutPictures(Context c)
		{
			if (c.Lookouts.Count == 0 || c.Terrain == null)
				return;

			const int width  = 1280;
			const int height = 720;

			string folder = Path.Combine(BrownoutMapData.ProjectFolder, "Logs", "MapCheck");
			Directory.CreateDirectory(folder);

			NatureInstancer instancer = c.Terrain.GetComponent<NatureInstancer>();
			if (instancer != null) instancer.Rebuild();
			NatureInstancer.DrawEverything = true;
			float pixelError = c.Terrain.heightmapPixelError;
			c.Terrain.heightmapPixelError = 1.0f;

			var cameraObject = new GameObject("LookoutCamera");
			var camera = cameraObject.AddComponent<Camera>();
			camera.fieldOfView   = 60.0f;
			camera.aspect        = width / (float)height;
			camera.nearClipPlane = 0.1f;
			camera.farClipPlane  = 2000.0f;
			camera.cullingMask   = ~((1 << LayerMask.NameToLayer("UI")) | (1 << LayerMask.NameToLayer("FPV")));

			var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
			camera.targetTexture = target;
			var sheet = new Texture2D(width * 2, height * 2, TextureFormat.RGB24, false);
			var names = new List<string>();

			foreach ((string name, Vector3 eye) in c.Lookouts)
			{
				for (int view = 0; view < 4; ++view)
				{
					camera.transform.SetPositionAndRotation(eye, Quaternion.Euler(4.0f, view * 90.0f, 0.0f));
					camera.Render();

					RenderTexture previous = RenderTexture.active;
					RenderTexture.active = target;
					sheet.ReadPixels(new Rect(0, 0, width, height), (view % 2) * width, (1 - view / 2) * height);
					RenderTexture.active = previous;
				}

				sheet.Apply();
				string file = "lookout_" + name.Replace(' ', '_') + ".png";
				File.WriteAllBytes(Path.Combine(folder, file), sheet.EncodeToPNG());
				names.Add(file);
			}

			camera.targetTexture = null;
			Object.DestroyImmediate(sheet);
			Object.DestroyImmediate(cameraObject);
			target.Release();
			Object.DestroyImmediate(target);
			NatureInstancer.DrawEverything = false;
			c.Terrain.heightmapPixelError = pixelError;

			c.Log.Add($"Lookout pictures (north, east / south, west, from 1.7 m): Logs/MapCheck/{string.Join(", ", names)}.");
		}

		// SCENE

		private static UnityScene OpenScene(out string result)
		{
			result = string.Empty;

			UnityScene active = SceneManager.GetActiveScene();
			if (active.path == ScenePath)
				return active;

			if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false)
			{
				result = "Cancelled.";
				return default;
			}

			if (File.Exists(ScenePath) == false)
			{
				if (AssetDatabase.CopyAsset(BrownoutMovementSetup.TemplateScene, ScenePath) == false)
				{
					result = $"Couldn't copy {BrownoutMovementSetup.TemplateScene} to {ScenePath}.";
					return default;
				}

				result = $"Made {ScenePath} from the Template scene.";
			}

			return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
		}

		/// <summary>The scene map is our 1000 m square, the scene starts in Battle Royale with 2 peers, and it's in the menu's map list.</summary>
		private static void SetUpScene(Context c)
		{
			// The Template's ground plane goes; the terrain replaces it
			foreach (GameObject root in c.Scene.GetRootGameObjects())
			{
				foreach (Transform child in root.GetComponentsInChildren<Transform>(true).ToList())
				{
					if (child != null && child.name == "Ground" && child.GetComponentInParent<BrownoutMap>() == null && PrefabUtility.IsPartOfPrefabInstance(child) == false)
					{
						Object.DestroyImmediate(child.gameObject);
					}
				}
			}

			SceneMap sceneMap = Object.FindObjectsByType<SceneMap>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.gameObject.scene == c.Scene);
			if (sceneMap != null)
			{
				var serialized = new SerializedObject(sceneMap);
				serialized.FindProperty("_worldDimensions").vector2IntValue = new Vector2Int(Mathf.RoundToInt(c.Layout.Size), Mathf.RoundToInt(c.Layout.Size));
				serialized.ApplyModifiedPropertiesWithoutUndo();
				sceneMap.transform.position = Vector3.zero;
			}

			var manager = Object.FindObjectsByType<StandaloneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.gameObject.scene == c.Scene);
			if (manager != null)
			{
				var serialized = new SerializedObject(manager);
				serialized.FindProperty("_defaultConfiguration.ExtraPeers").intValue = 1;
				serialized.FindProperty("_defaultConfiguration.GameplayType").intValue = (int)EGameplayType.BattleRoyale;
				serialized.ApplyModifiedPropertiesWithoutUndo();
			}

			string registered = BrownoutMovementSetup.RegisterScene(ScenePath, MapID, "Brownout", "Our 1 km map: six POIs, four cabin areas, a river crossed only at its five bridges.");
			c.Log.Add($"Scene: map 1000 m on the origin, Battle Royale, 2 peers. {registered}");
		}

		private static void WriteMapData(Context c)
		{
			var areas = c.Layout.AllAreas().Select(box => new BrownoutMap.Area
			{
				Id       = box.Id,
				Name     = box.Name,
				Center   = box.Center,
				Size     = box.Size,
				IsCabins = box.IsCabins,
			}).ToArray();

			c.Map.EditorSet(areas, c.S.GridCells, c.RiverLine.ToArray(), c.RiverWater, c.RiverHalfWidth);
			EditorUtility.SetDirty(c.Map);
		}

		/// <summary>Warns about roads or trails that meet the river away from a bridge (dead ends at the water).</summary>
		private static void CheckCrossings(Context c)
		{
			var warnings = new List<string>();
			void CheckLines(List<List<Vector2>> lines, string kind)
			{
				for (int l = 0; l < lines.Count; ++l)
				{
					bool reported = false;
					foreach (Vector2 point in lines[l])
					{
						if (reported == true || c.IsOnMap(point) == false)
							continue;

						if (c.RiverDistance(point) < c.RiverHalfWidth + 0.5f && c.IsOnBridge(point, 3.0f) == false)
						{
							Vector2 file = BrownoutMapData.ToFile(point);
							warnings.Add($"  {kind} {l + 1} meets the river at E{file.x:F0} N{file.y:F0} with no bridge");
							reported = true;
						}
					}
				}
			}

			CheckLines(c.RoadLines, "Road");
			CheckLines(c.TrailLines, "Trail");

			if (warnings.Count > 0)
			{
				c.Log.Add("Crossings to look at:\n" + string.Join("\n", warnings));
			}
		}

		// PROJECT SETTINGS

		/// <summary>Layer 15 = RiverWall (approved 6 Oct). Nothing physical collides with it; only the KCC's own queries look for it.</summary>
		public static string EnsureRiverWallLayer()
		{
			Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
			if (assets == null || assets.Length == 0)
				return "TagManager not found.";

			var tagManager = new SerializedObject(assets[0]);
			SerializedProperty layers = tagManager.FindProperty("layers");
			SerializedProperty layer  = layers.GetArrayElementAtIndex(RiverWallLayerIndex);
			string result = string.Empty;

			if (layer.stringValue != "RiverWall")
			{
				if (string.IsNullOrEmpty(layer.stringValue) == false)
					return $"Layer {RiverWallLayerIndex} is already '{layer.stringValue}'; the river wall needs a free layer.";

				layer.stringValue = "RiverWall";
				tagManager.ApplyModifiedPropertiesWithoutUndo();
				result = $"Named layer {RiverWallLayerIndex} RiverWall. ";
			}

			bool changed = false;
			for (int i = 0; i < 32; ++i)
			{
				if (Physics.GetIgnoreLayerCollision(RiverWallLayerIndex, i) == false)
				{
					Physics.IgnoreLayerCollision(RiverWallLayerIndex, i, true);
					changed = true;
				}
			}

			if (changed == true)
			{
				AssetDatabase.SaveAssets();
				result += "RiverWall collides with no rigidbodies (the KCC's queries still see it).";
			}

			return result;
		}

		// GEOMETRY

		/// <summary>A Catmull-Rom curve through the points, one point every spacing meters.</summary>
		public static List<Vector2> Smooth(List<Vector2> points, float spacing)
		{
			var result = new List<Vector2>();
			if (points.Count < 2)
			{
				result.AddRange(points);
				return result;
			}

			for (int i = 0; i < points.Count - 1; ++i)
			{
				Vector2 p0 = points[Mathf.Max(0, i - 1)];
				Vector2 p1 = points[i];
				Vector2 p2 = points[i + 1];
				Vector2 p3 = points[Mathf.Min(points.Count - 1, i + 2)];
				int     steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / spacing));

				for (int k = 0; k < steps; ++k)
				{
					float t = k / (float)steps;
					result.Add(0.5f * (2.0f * p1 + (-p0 + p2) * t + (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t * t + (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t * t * t));
				}
			}

			result.Add(points[points.Count - 1]);
			return result;
		}

		/// <summary>Whether segments AB and CD cross, and where.</summary>
		private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 hit)
		{
			hit = default;
			Vector2 r = b - a;
			Vector2 q = d - c;
			float   denominator = r.x * q.y - r.y * q.x;
			if (Mathf.Abs(denominator) < 1.0e-6f)
				return false;

			Vector2 ac = c - a;
			float   t  = (ac.x * q.y - ac.y * q.x) / denominator;
			float   u  = (ac.x * r.y - ac.y * r.x) / denominator;
			if (t < 0.0f || t > 1.0f || u < 0.0f || u > 1.0f)
				return false;

			hit = a + r * t;
			return true;
		}

		/// <summary>The line continued straight past both ends.</summary>
		private static List<Vector2> Extend(List<Vector2> points, float distance)
		{
			var result = new List<Vector2>(points);
			if (points.Count >= 2 && distance > 0.0f)
			{
				Vector2 start = (points[0] - points[1]).normalized;
				Vector2 end   = (points[points.Count - 1] - points[points.Count - 2]).normalized;
				result.Insert(0, points[0] + start * distance);
				result.Add(points[points.Count - 1] + end * distance);
			}

			return result;
		}

		/// <summary>Values averaged along a line over a distance (a moving window by arc length).</summary>
		public static float[] SmoothAlong(List<Vector2> line, float[] values, float window)
		{
			var result = new float[values.Length];
			var arc    = new float[line.Count];
			for (int i = 1; i < line.Count; ++i)
			{
				arc[i] = arc[i - 1] + Vector2.Distance(line[i - 1], line[i]);
			}

			float half = window * 0.5f;
			int   from = 0;
			int   to   = 0;
			float sum  = 0.0f;

			for (int i = 0; i < values.Length; ++i)
			{
				while (to < values.Length && arc[to] <= arc[i] + half) { sum += values[to]; ++to; }
				while (from < to && arc[from] < arc[i] - half)          { sum -= values[from]; ++from; }
				result[i] = to > from ? sum / (to - from) : values[i];
			}

			return result;
		}

		public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
		{
			Vector2 ab = b - a;
			float   length = ab.sqrMagnitude;
			t = length > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / length) : 0.0f;
			return Vector2.Distance(p, a + ab * t);
		}

		public static float DistanceToLine(List<Vector2> line, Vector2 p, out Vector2 closest, out int index)
		{
			float best = float.MaxValue;
			closest = default;
			index   = 0;

			for (int i = 0; i < line.Count - 1; ++i)
			{
				float d = DistanceToSegment(p, line[i], line[i + 1], out float t);
				if (d < best)
				{
					best    = d;
					closest = Vector2.Lerp(line[i], line[i + 1], t);
					index   = i;
				}
			}

			return best;
		}

		private static int NearestIndex(List<Vector2> line, Vector2 p)
		{
			DistanceToLine(line, p, out _, out int index);
			return index;
		}

		private static Vector2 LineNormal(List<Vector2> line, int i)
		{
			Vector2 tangent = (line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(0, i - 1)]).normalized;
			return new Vector2(-tangent.y, tangent.x);
		}

		private static float LineLength(List<Vector2> line)
		{
			float length = 0.0f;
			for (int i = 1; i < line.Count; ++i) length += Vector2.Distance(line[i - 1], line[i]);
			return length;
		}

		private static float SampleGrid(float[,] h, int res, float step, float origin, Vector2 p)
		{
			float fx = Mathf.Clamp((p.x - origin) / step, 0.0f, res - 1.001f);
			float fz = Mathf.Clamp((p.y - origin) / step, 0.0f, res - 1.001f);
			int   x  = (int)fx;
			int   z  = (int)fz;
			float tx = fx - x;
			float tz = fz - z;
			return Mathf.Lerp(Mathf.Lerp(h[z, x], h[z, x + 1], tx), Mathf.Lerp(h[z + 1, x], h[z + 1, x + 1], tx), tz);
		}

		/// <summary>Distance to the nearest of the lines for each cell of a grid (cell centers at origin + i * step), up to reach.</summary>
		public static float[,] DistanceField(int res, float step, float origin, List<List<Vector2>> lines, float reach)
		{
			var field = new float[res, res];
			for (int z = 0; z < res; ++z)
				for (int x = 0; x < res; ++x)
					field[z, x] = float.MaxValue;

			foreach (List<Vector2> line in lines)
			{
				for (int i = 0; i < line.Count - 1; ++i)
				{
					Vector2 a = line[i];
					Vector2 b = line[i + 1];

					int x0 = Mathf.Max(0,       Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach - origin) / step));
					int x1 = Mathf.Min(res - 1, Mathf.CeilToInt ((Mathf.Max(a.x, b.x) + reach - origin) / step));
					int z0 = Mathf.Max(0,       Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach - origin) / step));
					int z1 = Mathf.Min(res - 1, Mathf.CeilToInt ((Mathf.Max(a.y, b.y) + reach - origin) / step));

					for (int z = z0; z <= z1; ++z)
					{
						for (int x = x0; x <= x1; ++x)
						{
							float d = DistanceToSegment(new Vector2(origin + x * step, origin + z * step), a, b, out _);
							if (d < field[z, x])
							{
								field[z, x] = d;
							}
						}
					}
				}
			}

			return field;
		}

		public static float BoxDistance(BrownoutMapData.Box box, Vector2 p)
		{
			Vector2 half = box.Size * 0.5f;
			Vector2 d    = p - box.Center;
			return new Vector2(Mathf.Max(0.0f, Mathf.Abs(d.x) - half.x), Mathf.Max(0.0f, Mathf.Abs(d.y) - half.y)).magnitude;
		}

		// ASSETS

		public static Material ColorMaterial(string name, Color color, float smoothness)
		{
			EnsureFolder(ArtFolder);
			string path = $"{ArtFolder}/{name}.mat";
			var material = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (material == null)
			{
				material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
				AssetDatabase.CreateAsset(material, path);
			}

			material.SetColor("_BaseColor", color);
			material.SetFloat("_Smoothness", smoothness);
			material.enableInstancing = true;
			EditorUtility.SetDirty(material);
			return material;
		}

		public static void EnsureFolder(string path)
		{
			if (AssetDatabase.IsValidFolder(path) == true)
				return;

			string parent = Path.GetDirectoryName(path).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
		}

		public static int StableHash(string text)
		{
			unchecked
			{
				int hash = 23;
				foreach (char character in text)
				{
					hash = hash * 31 + character;
				}
				return hash & 0x7fffffff;
			}
		}
	}
}
