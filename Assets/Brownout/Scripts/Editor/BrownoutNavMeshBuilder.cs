namespace TPSBR
{
	using System.Collections.Generic;
	using System.Linq;
	using UnityEditor;
	using UnityEngine;
	using UnityEngine.AI;

	/// <summary>
	/// The bots' NavMesh (phase 9): Build Brownout Map → NavMesh (also in Build All). Baked from the map's colliders (terrain, buildings,
	/// bridges, rocks, the river wall and the solid bushes on layer 15) plus a box per tree trunk, for a walker a little narrower than the
	/// player so 0.75 m doors stay open. Doors are left out so paths go through doorways (a bot opens a door by walking into it, like a
	/// player); power doors stay in, so the sealed underground is off the mesh while there's no power. Saved as an asset next to the map
	/// and linked from BrownoutMap, which loads it at runtime. The build log checks that every POI and cabin area reaches the others,
	/// that every river crossing goes over a bridge, and how many loot points a bot can walk to.
	/// </summary>
	public static class BrownoutNavMeshBuilder
	{
		// CONSTANTS

		public const string NavMeshPath = "Assets/Brownout/Art/Map/BrownoutNavMesh.asset";

		// PUBLIC METHODS

		public static void Build(BrownoutMapBuilder.Context c)
		{
			BrownoutMapSettings s = c.S;
			float start = (float)EditorApplication.timeSinceStartup;

			// The solid bushes exist only at runtime; make them for the bake
			BushColliders bushes = c.Terrain != null ? c.Terrain.GetComponent<BushColliders>() : null;
			int bushCount = bushes != null ? bushes.Build() : 0;

			Physics.SyncTransforms();

			// Sources: every collider under the map on the Default layer and layer 15 (river wall, bushes)
			int mask = (1 << LayerMask.NameToLayer("Default")) | (1 << BrownoutMapBuilder.RiverWallLayerIndex);

			var markups = new List<NavMeshBuildMarkup>();
			int doors = 0;
			foreach (Door door in c.Root.GetComponentsInChildren<Door>(true))
			{
				if (door.IsPower == true)
					continue;

				markups.Add(new NavMeshBuildMarkup { root = door.transform, ignoreFromBuild = true });
				++doors;
			}

			var sources = new List<NavMeshBuildSource>(4096);
			NavMeshBuilder.CollectSources(c.Root, mask, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);

			// Tree trunks (the terrain's tree colliders aren't collected)
			int trunks = 0;
			if (c.Terrain != null)
			{
				TerrainData data   = c.Terrain.terrainData;
				Vector3     origin = c.Terrain.transform.position;
				float       reach  = c.HalfMap + s.NavMargin;

				foreach (TreeInstance instance in data.treeInstances)
				{
					// Pine, broadleaf, dead (the terrain prototypes with a trunk)
					if (instance.prototypeIndex > 2)
						continue;

					Vector3 position = origin + Vector3.Scale(instance.position, data.size);
					if (Mathf.Abs(position.x) > reach || Mathf.Abs(position.z) > reach)
						continue;

					position.y = c.Terrain.SampleHeight(position) + origin.y;
					float width = 0.6f * Mathf.Max(0.5f, instance.widthScale);

					sources.Add(new NavMeshBuildSource
					{
						shape     = NavMeshBuildSourceShape.Box,
						size      = new Vector3(width, 4.0f, width),
						transform = Matrix4x4.TRS(position + Vector3.up * 2.0f, Quaternion.identity, Vector3.one),
						area      = 0,
					});
					++trunks;
				}
			}

			// The river channel is never walkable: under a bridge deck the bank wall is lowered, and a path must not drop into the water
			int channel = 0;
			if (c.RiverLine != null && c.RiverWater != null)
			{
				float width = c.RiverHalfWidth * 2.0f + 0.8f;
				for (int i = 0; i < c.RiverLine.Count - 1 && i < c.RiverWater.Length - 1; ++i)
				{
					Vector2 a = c.RiverLine[i];
					Vector2 b = c.RiverLine[i + 1];
					Vector2 ab = b - a;
					float   length = ab.magnitude;
					if (length < 0.01f)
						continue;

					float   water  = Mathf.Min(c.RiverWater[i], c.RiverWater[i + 1]);
					Vector3 middle = new Vector3((a.x + b.x) * 0.5f, water - 1.5f, (a.y + b.y) * 0.5f);

					sources.Add(new NavMeshBuildSource
					{
						shape     = NavMeshBuildSourceShape.ModifierBox,
						size      = new Vector3(width, 5.0f, length + 0.5f),
						transform = Matrix4x4.TRS(middle, Quaternion.LookRotation(new Vector3(ab.x, 0.0f, ab.y)), Vector3.one),
						area      = 1,   // Not Walkable
					});
					++channel;
				}
			}

			NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
			settings.agentRadius       = s.NavAgentRadius;
			settings.agentHeight       = s.NavAgentHeight;
			settings.agentClimb        = s.NavStepHeight;
			settings.agentSlope        = s.NavMaxSlope;
			settings.overrideVoxelSize = true;
			settings.voxelSize         = s.NavVoxelSize;
			settings.overrideTileSize  = true;
			settings.tileSize          = s.NavTileSize;
			settings.minRegionArea     = 2.0f;

			float half   = c.HalfMap + s.NavMargin;
			var   bounds = new Bounds(new Vector3(0.0f, 40.0f, 0.0f), new Vector3(half * 2.0f, 160.0f, half * 2.0f));

			NavMeshData navMesh = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);

			if (bushes != null)
			{
				bushes.Clear();
			}
			if (navMesh == null)
			{
				c.Log.Add("NavMesh: the bake failed.");
				return;
			}

			navMesh.name = "BrownoutNavMesh";

			AssetDatabase.DeleteAsset(NavMeshPath);
			AssetDatabase.CreateAsset(navMesh, NavMeshPath);
			c.Map.EditorSetNavMesh(navMesh);
			EditorUtility.SetDirty(c.Map);

			float bakeTime = (float)EditorApplication.timeSinceStartup - start;
			c.Log.Add($"NavMesh: baked in {bakeTime:F0} s from {sources.Count} sources ({trunks} tree trunks, {bushCount} solid bushes, {channel} river channel blocks, {doors} doors left open, power doors shut), walker radius {s.NavAgentRadius:F2} m, height {s.NavAgentHeight:F2} m, step {s.NavStepHeight:F2} m, slope {s.NavMaxSlope:F0}°, voxel {s.NavVoxelSize:F3} m. Saved {NavMeshPath}.");

			Check(c, navMesh);
		}

		/// <summary>The build log's checks: areas connected, river crossings on bridges, loot within reach.</summary>
		public static void Check(BrownoutMapBuilder.Context c, NavMeshData navMesh)
		{
			NavMeshDataInstance instance = NavMesh.AddNavMeshData(navMesh);

			try
			{
				NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
				c.Log.Add($"NavMesh: {triangulation.indices.Length / 3} triangles.");

				var areas = c.Layout.AllAreas().ToList();
				var points = new List<(string Name, Vector3 Point)>();
				var missing = new List<string>();

				SpawnPoint[] spawns = c.Root.GetComponentsInChildren<SpawnPoint>(true);

				foreach (BrownoutMapData.Box area in areas)
				{
					if (FindAreaPoint(c, area, spawns, out Vector3 point) == true)
					{
						points.Add((area.Name, point));
					}
					else
					{
						missing.Add(area.Name);
					}
				}

				if (missing.Count > 0)
				{
					c.Log.Add($"NavMesh WARNING: no mesh near the center of {string.Join(", ", missing)}.");
				}

				// Every area reaches every other; crossings of the river go over a bridge
				var path        = new NavMeshPath();
				var corners     = new Vector3[512];
				var unreachable = new List<string>();
				var offBridge   = new List<string>();
				int pairs = 0, crossings = 0;

				for (int i = 0; i < points.Count; ++i)
				{
					for (int j = i + 1; j < points.Count; ++j)
					{
						++pairs;

						// Long paths stop at the pathfinder's search limit; carry on from where each leg ends, like the bots do
						var legs = new List<Vector3>();
						if (ChainPath(points[i].Point, points[j].Point, path, legs) == false)
						{
							unreachable.Add($"{points[i].Name} → {points[j].Name}");
							continue;
						}

						int count = legs.Count;
						legs.CopyTo(0, corners, 0, Mathf.Min(count, corners.Length));
						count = Mathf.Min(count, corners.Length);
						for (int k = 0; k < count - 1; ++k)
						{
							if (CrossesRiver(c, corners[k], corners[k + 1], out Vector2 crossing) == false)
								continue;

							++crossings;
							if (IsOnBridge(c, crossing) == false)
							{
								offBridge.Add($"{points[i].Name} → {points[j].Name} at E{BrownoutMapData.ToFile(crossing).x:F0} N{BrownoutMapData.ToFile(crossing).y:F0}");
							}
						}
					}
				}

				c.Log.Add(unreachable.Count == 0
					? $"NavMesh: all {points.Count} POIs and cabin areas reach each other ({pairs} paths)."
					: $"NavMesh WARNING: {unreachable.Count} of {pairs} area-to-area paths don't connect: {string.Join("; ", unreachable.Take(12))}.");
				c.Log.Add(offBridge.Count == 0
					? $"NavMesh: every river crossing on those paths ({crossings}) is on a bridge."
					: $"NavMesh WARNING: {offBridge.Count} river crossings off the bridges: {string.Join("; ", offBridge.Take(8))}.");

				// Loot points a bot can walk to from its area's center
				int reachable = 0, total = 0;
				var shortAreas = new Dictionary<string, int>();
				Transform loot = c.Root.Find("Loot");
				if (loot != null)
				{
					foreach (Transform areaGroup in loot)
					{
						(string Name, Vector3 Point) from = points.FirstOrDefault(p => areaGroup.name.Contains(p.Name) || p.Name.Contains(areaGroup.name));
						foreach (LootPoint lootPoint in areaGroup.GetComponentsInChildren<LootPoint>(true))
						{
							++total;
							Vector3 start = from.Name != null ? from.Point : points.Count > 0 ? points[0].Point : Vector3.zero;
							bool ok = NavMesh.SamplePosition(lootPoint.transform.position, out NavMeshHit hit, 1.5f, NavMesh.AllAreas) == true
								&& ChainPath(start, hit.position, path, null) == true;

							if (ok == true)
							{
								++reachable;
							}
							else
							{
								shortAreas[areaGroup.name] = shortAreas.TryGetValue(areaGroup.name, out int n) == true ? n + 1 : 1;
							}
						}
					}
				}

				c.Log.Add($"NavMesh: {reachable} of {total} loot points can be walked to{(shortAreas.Count > 0 ? $" (not: {string.Join(", ", shortAreas.Select(t => $"{t.Key} {t.Value}"))})" : string.Empty)}.");
			}
			finally
			{
				instance.Remove();
			}
		}

		// PRIVATE METHODS

		/// <summary>A full path in up to 8 legs (each long leg stops at the search limit); its corners when asked.</summary>
		private static bool ChainPath(Vector3 from, Vector3 to, NavMeshPath path, List<Vector3> corners)
		{
			for (int leg = 0; leg < 8; ++leg)
			{
				if (NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) == false || path.status == NavMeshPathStatus.PathInvalid)
					return false;

				Vector3[] legCorners = path.corners;
				corners?.AddRange(legCorners);

				if (path.status == NavMeshPathStatus.PathComplete)
					return true;

				Vector3 end = legCorners.Length > 0 ? legCorners[legCorners.Length - 1] : from;
				if ((end - from).sqrMagnitude < 1.0f)
					return false;

				from = end;
			}

			return false;
		}

		private static bool FindAreaPoint(BrownoutMapBuilder.Context c, BrownoutMapData.Box area, SpawnPoint[] spawns, out Vector3 point)
		{
			// The area's warm-up spawn point (open ground by construction)
			foreach (SpawnPoint spawn in spawns)
			{
				Vector2 p = new Vector2(spawn.transform.position.x, spawn.transform.position.z);
				if (p.x >= area.Min.x - 10.0f && p.x <= area.Max.x + 10.0f && p.y >= area.Min.y - 10.0f && p.y <= area.Max.y + 10.0f
					&& NavMesh.SamplePosition(spawn.transform.position, out NavMeshHit spawnHit, 2.0f, NavMesh.AllAreas) == true)
				{
					point = spawnHit.position;
					return true;
				}
			}

			// Otherwise the center, or the nearest open ground around it
			for (float radius = 0.0f; radius <= 40.0f; radius += 8.0f)
			{
				for (int k = 0; k < (radius == 0.0f ? 1 : 12); ++k)
				{
					float   angle = k * Mathf.PI * 2.0f / 12.0f;
					Vector2 p     = area.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
					Vector3 probe = new Vector3(p.x, c.GroundHeight(p) + 1.0f, p.y);

					if (NavMesh.SamplePosition(probe, out NavMeshHit hit, 3.0f, NavMesh.AllAreas) == true)
					{
						point = hit.position;
						return true;
					}
				}
			}

			point = default;
			return false;
		}

		private static bool CrossesRiver(BrownoutMapBuilder.Context c, Vector3 a, Vector3 b, out Vector2 crossing)
		{
			crossing = default;

			// Sample along the segment for a point inside the channel
			float length = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
			int   steps  = Mathf.Max(1, Mathf.CeilToInt(length / 1.0f));
			for (int i = 0; i <= steps; ++i)
			{
				Vector3 p = Vector3.Lerp(a, b, i / (float)steps);
				Vector2 flat = new Vector2(p.x, p.z);
				if (c.RiverDistance(flat) < c.RiverHalfWidth)
				{
					crossing = flat;
					return true;
				}
			}

			return false;
		}

		private static bool IsOnBridge(BrownoutMapBuilder.Context c, Vector2 point)
		{
			foreach (BrownoutMapBuilder.Bridge bridge in c.Bridges)
			{
				Vector2 middle = (bridge.A + bridge.B) * 0.5f;
				float   length = Vector2.Distance(bridge.A, bridge.B);
				if (Vector2.Distance(middle, point) < length * 0.5f + 5.0f)
					return true;
			}

			return false;
		}
	}
}
