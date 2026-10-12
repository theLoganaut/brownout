namespace TPSBR
{
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using UnityEditor;
	using UnityEngine;
	using UnityEngine.Rendering;

	/// <summary>
	/// Nature for the Brownout map (phase 8a, Logan's call: placeholders made in code until Blender models replace them). The prefabs live
	/// in Assets/Brownout/Art/Nature/ (Tree_Pine, Tree_Broadleaf, Tree_Dead, Bush, Rock_Small as terrain trees; Rock_Medium, Rock_Large
	/// as solid objects). They're only made when missing, so swapping in a real model is a matter of replacing the prefab and rebuilding
	/// Nature. Placement keeps trees off roads, trails, the river, bridges and buildings (8b), and thins them inside POIs and cabin areas.
	/// </summary>
	public static class BrownoutNatureBuilder
	{
		// CONSTANTS

		public const string NatureFolder      = "Assets/Brownout/Art/Nature";
		public const string PlaceholderFolder = "Assets/Brownout/Art/Nature/Placeholder";

		// Phase 10: Bush_02 and Bush_03 (Logan's other two bushes) come after the first five, so older indices stay the same
		private static readonly string[] TreeNames = { "Tree_Pine", "Tree_Broadleaf", "Tree_Dead", "Bush", "Rock_Small", "Bush_02", "Bush_03" };
		private const int PineIndex = 0, BroadleafIndex = 1, DeadIndex = 2, BushIndex = 3, SmallRockIndex = 4;
		public static readonly int[] BushIndices = { 3, 5, 6 };

		public const string ModelFolder    = "Assets/Brownout/Art/Nature/Models";
		public const string MaterialFolder = "Assets/Brownout/Art/Nature/Materials";

		// Prefab name ← Logan's model (phase 10). The prefab names are the placeholders' (the map builder knows them), the looks are his
		private static readonly (string Prefab, string Model, ENatureKind Kind)[] ModelPrefabs =
		{
			("Tree_Pine",      "ENV_Tree_02", ENatureKind.Tree),
			("Tree_Broadleaf", "ENV_Tree_01", ENatureKind.Tree),
			("Tree_Dead",      "ENV_Tree_03", ENatureKind.Tree),
			("Bush",           "ENV_Bush_01", ENatureKind.Bush),
			("Bush_02",        "ENV_Bush_02", ENatureKind.Bush),
			("Bush_03",        "ENV_Bush_03", ENatureKind.Bush),
			("Rock_Small",     "ENV_Rock_01", ENatureKind.SmallRock),
			("Rock_Medium",    "ENV_Rock_02", ENatureKind.SolidRock),
			("Rock_Large",     "ENV_Rock_03", ENatureKind.SolidRock),
		};

		private enum ENatureKind { Tree, Bush, SmallRock, SolidRock }

		// PUBLIC METHODS

		/// <summary>Places trees, bushes, rocks and grass on the map's terrain.</summary>
		public static void Build(BrownoutMapBuilder.Context c)
		{
			BrownoutMapSettings s = c.S;
			MakePlaceholders(s, false);

			Terrain     terrain = c.Terrain;
			TerrainData data    = terrain.terrainData;
			float       size    = data.size.x;
			Vector3     origin  = terrain.transform.position;

			// Lookup grids (2 m) for distances to roads, trails and the river
			float gridStep   = 2.0f;
			int   gridRes    = Mathf.CeilToInt(size / gridStep);
			float gridOrigin = -size * 0.5f + gridStep * 0.5f;
			float[,] roads  = BrownoutMapBuilder.DistanceField(gridRes, gridStep, gridOrigin, c.RoadLines,  c.Layout.RoadWidth * 0.5f + 8.0f);
			float[,] trails = BrownoutMapBuilder.DistanceField(gridRes, gridStep, gridOrigin, c.TrailLines, c.Layout.TrailWidth * 0.5f + 6.0f);
			float[,] river  = BrownoutMapBuilder.DistanceField(gridRes, gridStep, gridOrigin, new List<List<Vector2>> { c.RiverLine }, s.ValleyHalfWidth + 6.0f);

			float Lookup(float[,] field, Vector2 p)
			{
				int x = Mathf.Clamp(Mathf.RoundToInt((p.x - gridOrigin) / gridStep), 0, gridRes - 1);
				int z = Mathf.Clamp(Mathf.RoundToInt((p.y - gridOrigin) / gridStep), 0, gridRes - 1);
				return field[z, x];
			}

			float roadHalf  = c.Layout.RoadWidth * 0.5f;
			float trailHalf = c.Layout.TrailWidth * 0.5f;

			bool Clear(Vector2 p, float roadClearance, float trailClearance, float riverClearance)
			{
				return Lookup(roads, p) - roadHalf > roadClearance && Lookup(trails, p) - trailHalf > trailClearance && Lookup(river, p) > riverClearance && c.IsOnBridge(p, 3.0f) == false && c.IsInBuilding(p) == false;
			}

			// Terrain trees: trees, bushes and small rocks
			// A missing bush variant (no model for it) stands in with the first bush
			GameObject bushPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureFolder}/Bush.prefab");
			var prototypes = TreeNames.Select(name => new TreePrototype { prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureFolder}/{name}.prefab") ?? bushPrefab, bendFactor = 0.0f }).ToArray();
			data.treePrototypes = prototypes;

			var rng       = new System.Random(s.Seed * 31 + 1);
			var instances = new List<TreeInstance>(30000);
			int trees = 0, bushes = 0, smallRocks = 0;

			void Scatter(float cell, float chance, System.Func<Vector2, float> density, System.Func<Vector2, int> pick, System.Func<Vector2, bool> allowed, ref int counter, float minScale, float maxScale)
			{
				int cells = Mathf.CeilToInt(size / cell);
				for (int cz = 0; cz < cells; ++cz)
				{
					for (int cx = 0; cx < cells; ++cx)
					{
						Vector2 p = new Vector2(origin.x + (cx + (float)rng.NextDouble()) * cell, origin.z + (cz + (float)rng.NextDouble()) * cell);
						double roll = rng.NextDouble();
						float  scale = Mathf.Lerp(minScale, maxScale, (float)rng.NextDouble());
						float  width = scale * Mathf.Lerp(0.9f, 1.1f, (float)rng.NextDouble());
						float  rotation = (float)(rng.NextDouble() * Mathf.PI * 2.0);

						if (roll > chance * density(p) || allowed(p) == false)
							continue;

						instances.Add(new TreeInstance
						{
							position       = new Vector3((p.x - origin.x) / size, 0.0f, (p.y - origin.z) / size),
							widthScale     = width,
							heightScale    = scale,
							rotation       = rotation,
							color          = Color.white,
							lightmapColor  = Color.white,
							prototypeIndex = pick(p),
						});
						++counter;
					}
				}
			}

			Scatter(s.TreeCell, s.TreeChance, c.AreaDensity, p =>
			{
				float height = c.GroundHeight(p) / 100.0f;
				float pine   = 0.30f + 0.50f * height;
				float broad  = 0.55f - 0.35f * height;
				float roll   = (float)rng.NextDouble() * (pine + broad + 0.15f);
				return roll < pine ? PineIndex : (roll < pine + broad ? BroadleafIndex : DeadIndex);
			},
			p => Clear(p, s.RoadClearance, s.TrailClearance, c.RiverHalfWidth + 4.0f) && c.GroundSlope(p) < 35.0f, ref trees, 0.8f, 1.3f);

			Scatter(s.BushCell, s.BushChance, p => Mathf.Lerp(0.5f, 1.0f, c.AreaDensity(p)), p => BushIndices[rng.Next(0, BushIndices.Length)],
				p => Clear(p, 1.0f, 0.5f, c.RiverHalfWidth + 2.0f) && c.GroundSlope(p) < 40.0f, ref bushes, 0.7f, 1.3f);

			Scatter(s.SmallRockCell, s.SmallRockChance, p => 1.0f, p => SmallRockIndex,
				p => Clear(p, 0.5f, 0.3f, c.RiverHalfWidth + 1.0f), ref smallRocks, 0.6f, 1.6f);

			data.SetTreeInstances(instances.ToArray(), true);
			data.RefreshPrototypes();
			terrain.Flush();

			NatureInstancer instancer = terrain.GetComponent<NatureInstancer>();
			if (instancer == null)
			{
				instancer = terrain.gameObject.AddComponent<NatureInstancer>();
			}
			instancer.Rebuild();

			// Solid bushes (phase 9)
			int bushColliders = BuildBushColliders(c);

			// Medium and large rocks: solid objects, outside POIs and cabin areas
			Transform rocks = c.Section("Rocks");
			GameObject medium = AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureFolder}/Rock_Medium.prefab");
			GameObject large  = AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureFolder}/Rock_Large.prefab");
			int placedRocks = PlaceRocks(c, rocks, medium, s.MediumRocks, rng, Clear) + PlaceRocks(c, rocks, large, s.LargeRocks, rng, Clear);

			// Grass
			PlaceGrass(c, data, roads, trails, river, gridRes, gridStep, gridOrigin);

			c.Log.Add($"Nature: {trees} trees, {bushes} bushes ({bushColliders} solid), {smallRocks} small rocks (terrain trees), {placedRocks} medium and large rocks, grass to {s.GrassDistance:F0} m.");
		}

		/// <summary>
		/// Bushes are solid since phase 9 (Logan: no hiding inside them). Terrain tree colliders take the terrain's layer, which bullets hit,
		/// so each bush gets its own sphere on layer 15 (RiverWall), which only the player's capsule collides with: bullets, grenades and the
		/// grapple pass through the leaves. Bots' sight checks treat these spheres as blocking. Returns how many were made.
		/// </summary>
		public static int BuildBushColliders(BrownoutMapBuilder.Context c)
		{
			// The colliders are made when the scene loads (BushColliders on the terrain), not saved: 8,000+ objects would triple the scene
			Transform old = c.Root.Find("BushColliders");
			if (old != null)
			{
				Object.DestroyImmediate(old.gameObject);
			}

			if (c.Terrain == null)
				return 0;

			BushColliders bushes = c.Terrain.GetComponent<BushColliders>();
			if (bushes == null)
			{
				bushes = c.Terrain.gameObject.AddComponent<BushColliders>();
			}

			int count = bushes.Build();
			bushes.Clear();
			return count;
		}

		/// <summary>Bush centers (world X / Z) and their collider radius, so loot points keep clear of them.</summary>
		public static List<Vector3> GetBushes(BrownoutMapBuilder.Context c)
		{
			var bushes = new List<Vector3>();
			if (c.Terrain == null)
				return bushes;

			TerrainData data   = c.Terrain.terrainData;
			Vector3     origin = c.Terrain.transform.position;

			foreach (TreeInstance instance in data.treeInstances)
			{
				if (System.Array.IndexOf(BushIndices, instance.prototypeIndex) >= 0)
				{
					bushes.Add(new Vector3(origin.x + instance.position.x * data.size.x, origin.z + instance.position.z * data.size.z, c.S.BushColliderRadius * Mathf.Max(instance.widthScale, instance.heightScale)));
				}
			}

			return bushes;
		}

		/// <summary>Tree trunk positions (world X / Z), so loot points keep clear of them.</summary>
		public static List<Vector2> GetTrunks(BrownoutMapBuilder.Context c)
		{
			var trunks = new List<Vector2>();
			if (c.Terrain == null)
				return trunks;

			TerrainData data   = c.Terrain.terrainData;
			Vector3     origin = c.Terrain.transform.position;

			foreach (TreeInstance instance in data.treeInstances)
			{
				if (instance.prototypeIndex <= DeadIndex)
				{
					trunks.Add(new Vector2(origin.x + instance.position.x * data.size.x, origin.z + instance.position.z * data.size.z));
				}
			}

			return trunks;
		}

		/// <summary>
		/// Phase 10: the nature prefabs become Logan's models (ModelPrefabs), keeping what the map needs from each: one LOD group (terrain
		/// trees on URP), a trunk capsule on trees (the terrain's tree collider), a convex mesh collider on the solid rocks, nothing on bushes
		/// and small rocks (bushes get their own player-only colliders). The GPU instancer needs instancing on the materials, so each model's
		/// material is copied once into Art/Nature/Materials. Rebuild the map's Nature afterwards.
		/// </summary>
		public static string UseModels()
		{
			BrownoutMapBuilder.EnsureFolder(MaterialFolder);

			var made    = new List<string>();
			var missing = new List<string>();

			foreach (var entry in ModelPrefabs)
			{
				var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelFolder}/{entry.Model}.fbx");
				MeshFilter modelFilter = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
				if (modelFilter == null || modelFilter.sharedMesh == null)
				{
					missing.Add(entry.Model);
					continue;
				}

				Mesh       mesh      = modelFilter.sharedMesh;
				Material[] materials = modelFilter.GetComponent<MeshRenderer>().sharedMaterials.Select(InstancedCopy).ToArray();

				var root  = new GameObject(entry.Prefab);
				var child = new GameObject("LOD0");
				child.transform.SetParent(root.transform, false);
				child.transform.localRotation = modelFilter.transform.localRotation;
				child.AddComponent<MeshFilter>().sharedMesh = mesh;

				var renderer = child.AddComponent<MeshRenderer>();
				renderer.sharedMaterials   = materials;
				renderer.shadowCastingMode = entry.Kind == ENatureKind.SmallRock ? ShadowCastingMode.Off : ShadowCastingMode.On;

				var lodGroup = root.AddComponent<LODGroup>();
				lodGroup.SetLODs(new[] { new LOD(entry.Kind == ENatureKind.Tree ? 0.012f : 0.02f, new Renderer[] { renderer }) });
				lodGroup.RecalculateBounds();

				switch (entry.Kind)
				{
					case ENatureKind.Tree:
					{
						// The trunk: a thin capsule up the middle, as tall as the tree
						float height  = mesh.bounds.max.y;
						var   capsule = root.AddComponent<CapsuleCollider>();
						capsule.radius = Mathf.Clamp(Mathf.Min(mesh.bounds.size.x, mesh.bounds.size.z) * 0.04f, 0.2f, 0.45f);
						capsule.height = height;
						capsule.center = new Vector3(mesh.bounds.center.x, height * 0.5f, mesh.bounds.center.z);
						break;
					}
					case ENatureKind.SolidRock:
					{
						var collider = child.AddComponent<MeshCollider>();
						collider.sharedMesh = mesh;
						collider.convex     = true;
						break;
					}
				}

				PrefabUtility.SaveAsPrefabAsset(root, $"{NatureFolder}/{entry.Prefab}.prefab");
				Object.DestroyImmediate(root);
				made.Add($"{entry.Prefab} ← {entry.Model}");
			}

			AssetDatabase.SaveAssets();
			return $"Nature prefabs from Logan's models: {string.Join(", ", made)}{(missing.Count > 0 ? $"; no model for {string.Join(", ", missing)} (placeholder kept)" : "")}. Rebuild the map's Nature to place them.";
		}

		private static Material InstancedCopy(Material source)
		{
			if (source == null)
				return null;

			string path = $"{MaterialFolder}/{source.name}.mat";
			var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (copy == null)
			{
				copy = new Material(source);
				AssetDatabase.CreateAsset(copy, path);
			}
			else
			{
				copy.CopyPropertiesFromMaterial(source);
			}

			copy.enableInstancing = true;
			EditorUtility.SetDirty(copy);
			return copy;
		}

		/// <summary>Makes the placeholder models and prefabs that are missing (all of them when forced).</summary>
		public static string MakePlaceholders(BrownoutMapSettings s, bool force)
		{
			BrownoutMapBuilder.EnsureFolder(PlaceholderFolder);

			Material trunk  = Material("Trunk",    s.Trunk);
			Material pine   = Material("Pine",     s.Pine);
			Material leaves = Material("Leaves",   s.Leaves);
			Material dead   = Material("DeadWood", s.DeadWood);
			Material bush   = Material("Bush",     s.Bush);
			Material stone  = Material("Stone",    s.Stone);

			var made = new List<string>();

			if (Needs("Tree_Pine", force) == true)
			{
				var lod0 = new MeshBuilder(2);
				lod0.Frustum(0, Vector3.zero, Quaternion.identity, 0.28f, 0.18f, 2.6f, 6);
				lod0.Frustum(1, new Vector3(0.0f, 1.6f, 0.0f), Quaternion.identity, 2.3f, 0.0f, 3.4f, 7);
				lod0.Frustum(1, new Vector3(0.0f, 3.6f, 0.0f), Quaternion.Euler(0.0f, 25.0f, 0.0f), 1.8f, 0.0f, 3.0f, 7);
				lod0.Frustum(1, new Vector3(0.0f, 5.6f, 0.0f), Quaternion.Euler(0.0f, 50.0f, 0.0f), 1.2f, 0.0f, 2.6f, 6);
				var lod1 = new MeshBuilder(2);
				lod1.Frustum(0, Vector3.zero, Quaternion.identity, 0.28f, 0.18f, 2.0f, 4);
				lod1.Frustum(1, new Vector3(0.0f, 1.6f, 0.0f), Quaternion.identity, 2.2f, 0.0f, 6.6f, 5);
				SaveTree("Tree_Pine", lod0, lod1, new[] { trunk, pine }, 0.3f, 8.2f);
				made.Add("Tree_Pine");
			}

			if (Needs("Tree_Broadleaf", force) == true)
			{
				var jitter = new System.Random(11);
				var lod0 = new MeshBuilder(2);
				lod0.Frustum(0, Vector3.zero, Quaternion.identity, 0.32f, 0.22f, 3.6f, 6);
				lod0.Blob(1, new Vector3(0.0f, 5.2f, 0.0f), new Vector3(2.8f, 2.2f, 2.8f), 4, 7, jitter, 0.25f);
				lod0.Blob(1, new Vector3(1.0f, 6.4f, 0.5f), new Vector3(1.7f, 1.5f, 1.7f), 3, 6, jitter, 0.2f);
				var lod1 = new MeshBuilder(2);
				lod1.Frustum(0, Vector3.zero, Quaternion.identity, 0.32f, 0.22f, 3.4f, 4);
				lod1.Blob(1, new Vector3(0.0f, 5.4f, 0.0f), new Vector3(3.0f, 2.5f, 3.0f), 2, 5, jitter, 0.1f);
				SaveTree("Tree_Broadleaf", lod0, lod1, new[] { trunk, leaves }, 0.35f, 7.8f);
				made.Add("Tree_Broadleaf");
			}

			if (Needs("Tree_Dead", force) == true)
			{
				var lod0 = new MeshBuilder(1);
				lod0.Frustum(0, Vector3.zero, Quaternion.identity, 0.26f, 0.06f, 6.5f, 5);
				lod0.Frustum(0, new Vector3(0.0f, 3.0f, 0.0f), Quaternion.Euler(0.0f, 0.0f, -50.0f), 0.10f, 0.03f, 2.0f, 4);
				lod0.Frustum(0, new Vector3(0.0f, 4.1f, 0.0f), Quaternion.Euler(45.0f, 120.0f, 0.0f), 0.09f, 0.03f, 1.7f, 4);
				lod0.Frustum(0, new Vector3(0.0f, 5.0f, 0.0f), Quaternion.Euler(0.0f, 240.0f, 40.0f), 0.07f, 0.02f, 1.3f, 4);
				var lod1 = new MeshBuilder(1);
				lod1.Frustum(0, Vector3.zero, Quaternion.identity, 0.26f, 0.06f, 6.5f, 4);
				SaveTree("Tree_Dead", lod0, lod1, new[] { dead }, 0.26f, 6.5f);
				made.Add("Tree_Dead");
			}

			if (Needs("Bush", force) == true)
			{
				var jitter = new System.Random(23);
				var lod0 = new MeshBuilder(1);
				lod0.Blob(0, new Vector3(0.0f, 0.5f, 0.0f), new Vector3(1.1f, 0.7f, 1.1f), 3, 6, jitter, 0.2f);
				lod0.Blob(0, new Vector3(0.6f, 0.7f, 0.3f), new Vector3(0.7f, 0.5f, 0.7f), 3, 5, jitter, 0.2f);
				var lod1 = new MeshBuilder(1);
				lod1.Blob(0, new Vector3(0.0f, 0.5f, 0.0f), new Vector3(1.2f, 0.75f, 1.2f), 2, 4, jitter, 0.1f);
				SaveTree("Bush", lod0, lod1, new[] { bush }, 0.0f, 1.3f);
				made.Add("Bush");
			}

			if (Needs("Rock_Small", force) == true)
			{
				made.Add(SaveRock("Rock_Small", 0.45f, 31, stone, false));
			}

			if (Needs("Rock_Medium", force) == true)
			{
				made.Add(SaveRock("Rock_Medium", 1.0f, 37, stone, true));
			}

			if (Needs("Rock_Large", force) == true)
			{
				made.Add(SaveRock("Rock_Large", 2.4f, 41, stone, true));
			}

			AssetDatabase.SaveAssets();
			return made.Count > 0 ? $"Placeholder nature made: {string.Join(", ", made)} (in {NatureFolder})." : "Placeholder nature: all prefabs already there.";
		}

		// PRIVATE METHODS

		private static int PlaceRocks(BrownoutMapBuilder.Context c, Transform parent, GameObject prefab, int count, System.Random rng, System.Func<Vector2, float, float, float, bool> clear)
		{
			if (prefab == null)
				return 0;

			float half   = c.S.TerrainSize * 0.5f - 20.0f;
			int   placed = 0;

			for (int attempt = 0; attempt < count * 8 && placed < count; ++attempt)
			{
				Vector2 p = new Vector2(Mathf.Lerp(-half, half, (float)rng.NextDouble()), Mathf.Lerp(-half, half, (float)rng.NextDouble()));
				float   scale = Mathf.Lerp(0.75f, 1.4f, (float)rng.NextDouble());
				float   yaw   = (float)rng.NextDouble() * 360.0f;
				float   tilt  = Mathf.Lerp(-12.0f, 12.0f, (float)rng.NextDouble());

				if (clear(p, 2.5f, 1.5f, c.S.ValleyHalfWidth + 2.0f) == false || c.GroundSlope(p) > 30.0f)
					continue;

				bool inArea = false;
				foreach (BrownoutMapData.Box box in c.Layout.AllAreas())
				{
					if (BrownoutMapBuilder.BoxDistance(box, p) < 6.0f)
					{
						inArea = true;
						break;
					}
				}

				if (inArea == true)
					continue;

				var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
				rock.transform.position   = new Vector3(p.x, c.GroundHeight(p) - 0.25f * scale, p.y);
				rock.transform.rotation   = Quaternion.Euler(tilt, yaw, tilt * 0.5f);
				rock.transform.localScale = Vector3.one * scale;
				rock.isStatic = true;
				++placed;
			}

			return placed;
		}

		private static void PlaceGrass(BrownoutMapBuilder.Context c, TerrainData data, float[,] roads, float[,] trails, float[,] river, int gridRes, float gridStep, float gridOrigin)
		{
			BrownoutMapSettings s = c.S;

			data.detailPrototypes = new[]
			{
				new DetailPrototype
				{
					prototypeTexture = GrassTexture(),
					renderMode       = DetailRenderMode.Grass,
					usePrototypeMesh = false,
					healthyColor     = s.Grass * 1.15f,
					dryColor         = s.GrassDry,
					minWidth         = 0.6f,
					maxWidth         = 1.0f,
					minHeight        = 0.35f,
					maxHeight        = 0.65f,
					noiseSpread      = 0.3f,
				},
			};

			int res = s.DetailResolution;
			data.SetDetailResolution(res, 32);
			data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

			float size   = data.size.x;
			float step   = size / res;
			float origin = -size * 0.5f + step * 0.5f;
			var   layer  = new int[res, res];
			var   offset = new Vector2(s.Seed * 3.3f, s.Seed * 9.1f);

			for (int z = 0; z < res; ++z)
			{
				for (int x = 0; x < res; ++x)
				{
					Vector2 p = new Vector2(origin + x * step, origin + z * step);
					int gx = Mathf.Clamp(Mathf.RoundToInt((p.x - gridOrigin) / gridStep), 0, gridRes - 1);
					int gz = Mathf.Clamp(Mathf.RoundToInt((p.y - gridOrigin) / gridStep), 0, gridRes - 1);

					if (roads[gz, gx] - c.Layout.RoadWidth * 0.5f < 0.6f || trails[gz, gx] - c.Layout.TrailWidth * 0.5f < 0.3f || river[gz, gx] < c.RiverHalfWidth + 1.0f)
						continue;

					if (data.GetSteepness((x + 0.5f) / res, (z + 0.5f) / res) > 32.0f)
						continue;

					if (c.IsInBuilding(p) == true)
						continue;

					float patch = Mathf.PerlinNoise(offset.x + p.x / 30.0f, offset.y + p.y / 30.0f);
					float density = s.GrassDensity * Mathf.Lerp(0.5f, 1.0f, c.AreaDensity(p)) * Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(0.25f, 0.55f, patch));
					layer[z, x] = Mathf.Clamp(Mathf.RoundToInt(density), 0, 16);
				}
			}

			data.SetDetailLayer(0, 0, 0, layer);
		}

		private static Texture2D GrassTexture()
		{
			string path = $"{PlaceholderFolder}/Grass.png";
			if (File.Exists(path) == false)
			{
				const int size = 64;
				var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
				var clear   = new Color(0.0f, 0.0f, 0.0f, 0.0f);
				for (int y = 0; y < size; ++y)
					for (int x = 0; x < size; ++x)
						texture.SetPixel(x, y, clear);

				var rng = new System.Random(5);
				for (int blade = 0; blade < 9; ++blade)
				{
					float baseX  = 6 + blade * 6.2f + (float)rng.NextDouble() * 3.0f;
					float lean   = ((float)rng.NextDouble() - 0.5f) * 14.0f;
					int   height = 34 + rng.Next(0, 28);
					for (int y = 0; y < height; ++y)
					{
						float t     = y / (float)height;
						float width = Mathf.Lerp(2.6f, 0.4f, t);
						float cx    = baseX + lean * t * t;
						float shade = Mathf.Lerp(0.75f, 1.0f, t);
						for (int x = Mathf.FloorToInt(cx - width); x <= Mathf.CeilToInt(cx + width); ++x)
						{
							if (x >= 0 && x < size)
							{
								texture.SetPixel(x, y, new Color(shade, shade, shade, 1.0f));
							}
						}
					}
				}

				File.WriteAllBytes(path, texture.EncodeToPNG());
				Object.DestroyImmediate(texture);
				AssetDatabase.ImportAsset(path);

				var importer = (TextureImporter)AssetImporter.GetAtPath(path);
				importer.alphaIsTransparency = true;
				importer.wrapMode = TextureWrapMode.Clamp;
				importer.SaveAndReimport();
			}

			return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
		}

		private static bool Needs(string name, bool force)
		{
			return force == true || File.Exists($"{NatureFolder}/{name}.prefab") == false;
		}

		private static Material Material(string name, Color color)
		{
			string path = $"{PlaceholderFolder}/{name}.mat";
			var material = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (material == null)
			{
				material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
				AssetDatabase.CreateAsset(material, path);
			}

			material.SetColor("_BaseColor", color);
			material.SetFloat("_Smoothness", 0.08f);
			material.enableInstancing = true;
			EditorUtility.SetDirty(material);
			return material;
		}

		/// <summary>A prefab with two LODs and, for trees, a trunk capsule the terrain uses as the tree's collider.</summary>
		private static void SaveTree(string name, MeshBuilder lod0, MeshBuilder lod1, Material[] materials, float trunkRadius, float height)
		{
			string meshPath = $"{PlaceholderFolder}/{name}.asset";
			AssetDatabase.DeleteAsset(meshPath);

			Mesh mesh0 = lod0.ToMesh($"{name}_LOD0");
			Mesh mesh1 = lod1.ToMesh($"{name}_LOD1");
			AssetDatabase.CreateAsset(mesh0, meshPath);
			AssetDatabase.AddObjectToAsset(mesh1, meshPath);

			var root = new GameObject(name);
			var renderers = new Renderer[2];
			Mesh[] meshes = { mesh0, mesh1 };

			for (int i = 0; i < 2; ++i)
			{
				var child = new GameObject($"LOD{i}");
				child.transform.SetParent(root.transform, false);
				child.AddComponent<MeshFilter>().sharedMesh = meshes[i];
				var renderer = child.AddComponent<MeshRenderer>();
				renderer.sharedMaterials   = materials;
				renderer.shadowCastingMode = i == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
				renderers[i] = renderer;
			}

			var lodGroup = root.AddComponent<LODGroup>();
			lodGroup.SetLODs(new[] { new LOD(0.10f, new[] { renderers[0] }), new LOD(0.012f, new[] { renderers[1] }) });
			lodGroup.RecalculateBounds();

			if (trunkRadius > 0.0f)
			{
				var capsule = root.AddComponent<CapsuleCollider>();
				capsule.radius = trunkRadius;
				capsule.height = height;
				capsule.center = new Vector3(0.0f, height * 0.5f, 0.0f);
			}

			PrefabUtility.SaveAsPrefabAsset(root, $"{NatureFolder}/{name}.prefab");
			Object.DestroyImmediate(root);
		}

		private static string SaveRock(string name, float radius, int seed, Material material, bool solid)
		{
			string meshPath = $"{PlaceholderFolder}/{name}.asset";
			AssetDatabase.DeleteAsset(meshPath);

			var builder = new MeshBuilder(1);
			builder.Blob(0, new Vector3(0.0f, radius * 0.35f, 0.0f), new Vector3(radius, radius * 0.7f, radius * 0.85f), 3, 6, new System.Random(seed), 0.22f);
			Mesh mesh = builder.ToMesh(name);
			AssetDatabase.CreateAsset(mesh, meshPath);

			var root = new GameObject(name);
			root.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = root.AddComponent<MeshRenderer>();
			renderer.sharedMaterial    = material;
			renderer.shadowCastingMode = solid == true ? ShadowCastingMode.On : ShadowCastingMode.Off;

			if (solid == true)
			{
				var collider = root.AddComponent<MeshCollider>();
				collider.sharedMesh = mesh;
				collider.convex     = true;
			}
			else
			{
				// A terrain tree prototype without a LOD group wants the old Nature shaders; one LOD keeps it on URP Lit
				var lodGroup = root.AddComponent<LODGroup>();
				lodGroup.SetLODs(new[] { new LOD(0.02f, new Renderer[] { renderer }) });
				lodGroup.RecalculateBounds();
			}

			PrefabUtility.SaveAsPrefabAsset(root, $"{NatureFolder}/{name}.prefab");
			Object.DestroyImmediate(root);
			return name;
		}

		// MESH BUILDER

		/// <summary>Flat-shaded low-poly shapes; every triangle is wound to face away from an inside point.</summary>
		private sealed class MeshBuilder
		{
			private readonly List<Vector3> _vertices = new List<Vector3>();
			private readonly List<Vector3> _normals  = new List<Vector3>();
			private readonly List<int>[]   _indices;

			public MeshBuilder(int submeshes)
			{
				_indices = new List<int>[submeshes];
				for (int i = 0; i < submeshes; ++i) _indices[i] = new List<int>();
			}

			public void Triangle(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
			{
				Vector3 normal = Vector3.Cross(b - a, c - a);
				if (normal.sqrMagnitude < 1e-10f)
					return;

				if (Vector3.Dot(normal, (a + b + c) / 3.0f - inside) < 0.0f)
				{
					Vector3 swap = b;
					b = c;
					c = swap;
					normal = -normal;
				}

				normal.Normalize();
				int index = _vertices.Count;
				_vertices.Add(a);
				_vertices.Add(b);
				_vertices.Add(c);
				_normals.Add(normal);
				_normals.Add(normal);
				_normals.Add(normal);
				_indices[submesh].Add(index);
				_indices[submesh].Add(index + 1);
				_indices[submesh].Add(index + 2);
			}

			/// <summary>A cone or tapered cylinder (topRadius 0 = cone) standing on its base, turned by rotation.</summary>
			public void Frustum(int submesh, Vector3 basePosition, Quaternion rotation, float baseRadius, float topRadius, float height, int sides)
			{
				Vector3 up     = rotation * Vector3.up;
				Vector3 top    = basePosition + up * height;
				Vector3 inside = basePosition + up * (height * 0.35f);

				for (int k = 0; k < sides; ++k)
				{
					float   a0 = k * Mathf.PI * 2.0f / sides;
					float   a1 = (k + 1) * Mathf.PI * 2.0f / sides;
					Vector3 d0 = rotation * new Vector3(Mathf.Cos(a0), 0.0f, Mathf.Sin(a0));
					Vector3 d1 = rotation * new Vector3(Mathf.Cos(a1), 0.0f, Mathf.Sin(a1));
					Vector3 b0 = basePosition + d0 * baseRadius;
					Vector3 b1 = basePosition + d1 * baseRadius;

					if (topRadius <= 0.0f)
					{
						Triangle(submesh, b0, top, b1, inside);
					}
					else
					{
						Vector3 t0 = top + d0 * topRadius;
						Vector3 t1 = top + d1 * topRadius;
						Triangle(submesh, b0, t0, t1, inside);
						Triangle(submesh, b0, t1, b1, inside);
						Triangle(submesh, top, t0, t1, inside);
					}

					Triangle(submesh, basePosition, b0, b1, inside);
				}
			}

			/// <summary>A lumpy ellipsoid: a latitude/longitude ball with each ring point pushed in or out a little.</summary>
			public void Blob(int submesh, Vector3 center, Vector3 radii, int rings, int segments, System.Random jitter, float amount)
			{
				var points = new Vector3[rings + 1, segments];
				for (int r = 0; r <= rings; ++r)
				{
					float phi = Mathf.PI * r / rings;
					for (int k = 0; k < segments; ++k)
					{
						float   theta = Mathf.PI * 2.0f * (k + (r % 2) * 0.5f) / segments;
						float   push  = 1.0f + ((float)jitter.NextDouble() - 0.5f) * 2.0f * amount;
						Vector3 unit  = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
						points[r, k] = center + Vector3.Scale(unit, radii) * (r == 0 || r == rings ? 1.0f : push);
					}
				}

				for (int r = 0; r < rings; ++r)
				{
					for (int k = 0; k < segments; ++k)
					{
						int     next = (k + 1) % segments;
						Vector3 a = points[r, k];
						Vector3 b = points[r, next];
						Vector3 c = points[r + 1, k];
						Vector3 d = points[r + 1, next];

						// At the poles one of the two is a sliver of zero area and gets skipped
						Triangle(submesh, a, b, c, center);
						Triangle(submesh, b, d, c, center);
					}
				}
			}

			public Mesh ToMesh(string name)
			{
				var mesh = new Mesh { name = name };
				mesh.SetVertices(_vertices);
				mesh.SetNormals(_normals);
				mesh.subMeshCount = _indices.Length;
				for (int i = 0; i < _indices.Length; ++i)
				{
					mesh.SetTriangles(_indices[i], i);
				}
				mesh.RecalculateBounds();
				return mesh;
			}
		}
	}
}
