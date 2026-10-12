namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using Fusion;

	/// <summary>
	/// Server-side loot spawning (phase 5a): loot points roll once at match start (no refills), a gun comes with a full mag and boxes
	/// of its caliber beside it, dropped items land at the dropper's feet, and death leaves a loot bag. Also the test layouts
	/// (every gun, every attachment, every other item in a row in front of the player, 2 m apart).
	/// </summary>
	public static class LootSpawner
	{
		// PUBLIC MEMBERS

		public struct Point
		{
			public Vector3 Position;
			public bool    TopTier;
		}

		// PRIVATE MEMBERS

		private struct Entry
		{
			public LootStack Item;
			public float     Weight;
		}

		private static readonly Dictionary<NetworkRunner, List<Point>> _points = new Dictionary<NetworkRunner, List<Point>>();
		private static readonly List<LootStack> _items   = new List<LootStack>(64);
		private static readonly List<Entry>     _entries = new List<Entry>(64);
		private static readonly List<LootItem>  _lootBuffer = new List<LootItem>(1024);

		// PUBLIC METHODS

		/// <summary>The loot points this runner rolled at match start.</summary>
		public static IReadOnlyList<Point> GetPoints(NetworkRunner runner)
		{
			return _points.TryGetValue(runner, out List<Point> points) == true ? points : (IReadOnlyList<Point>)System.Array.Empty<Point>();
		}

		/// <summary>
		/// Server, at match start: the generated level's spots (the sample's item box spots) plus any hand-placed LootPoints in the scene,
		/// each rolled once.
		/// </summary>
		public static int SpawnLevelLoot(NetworkRunner runner, List<Vector3> generatedPoints)
		{
			if (runner == null || runner.IsServer == false)
				return 0;

			var points = new List<Point>(generatedPoints != null ? generatedPoints.Count : 0);

			if (generatedPoints != null)
			{
				int max = Mathf.Min(generatedPoints.Count, Mathf.Max(0, LootSettings.Current.MaxGeneratedPoints));
				for (int i = 0; i < max; ++i)
				{
					points.Add(new Point { Position = generatedPoints[i] });
				}
			}

			foreach (GameObject root in runner.SimulationUnityScene.GetRootGameObjects())
			{
				foreach (LootPoint lootPoint in root.GetComponentsInChildren<LootPoint>())
				{
					points.Add(new Point { Position = lootPoint.transform.position, TopTier = lootPoint.TopTier });
				}
			}

			_points[runner] = points;

			int spawned = 0;
			for (int i = 0; i < points.Count; ++i)
			{
				spawned += SpawnPoint(runner, points[i]);
			}

			LootDebug.Log(runner, $"SPAWN {points.Count} loot points rolled, {spawned} items on the ground ({LootSettings.Current.Weighting})");
			return spawned;
		}

		/// <summary>Server, test tool: despawns every item on the ground and rolls every loot point again.</summary>
		public static int Reroll(NetworkRunner runner)
		{
			ClearGround(runner);

			int spawned = 0;
			IReadOnlyList<Point> points = GetPoints(runner);
			for (int i = 0; i < points.Count; ++i)
			{
				spawned += SpawnPoint(runner, points[i]);
			}

			LootDebug.Log(runner, $"SPAWN rerolled {points.Count} loot points, {spawned} items");
			return spawned;
		}

		/// <summary>Server, test tool: despawns every item on the ground. Returns how many.</summary>
		public static int ClearGround(NetworkRunner runner)
		{
			runner.GetAllBehaviours(_lootBuffer);
			int count = _lootBuffer.Count;

			for (int i = 0; i < _lootBuffer.Count; ++i)
			{
				runner.Despawn(_lootBuffer[i].Object);
			}

			_lootBuffer.Clear();
			return count;
		}

		/// <summary>Rolls one item from the loot table (server).</summary>
		public static LootStack Roll(bool topTier)
		{
			LootSettings settings = LootSettings.Current;

			_items.Clear();
			ItemCatalog.GetAllItems(_items);

			// Weights: every item equally likely, or every kind equally likely and then every item within it
			_entries.Clear();
			float total = 0.0f;

			for (int i = 0; i < _items.Count; ++i)
			{
				LootStack item   = _items[i];
				float     weight = 1.0f;

				if (settings.Weighting == ELootWeighting.EveryKind)
				{
					int sameKind = 0;
					for (int j = 0; j < _items.Count; ++j)
					{
						if (_items[j].Kind == item.Kind)
						{
							++sameKind;
						}
					}

					weight = 1.0f / Mathf.Max(1, sameKind);
				}

				if (topTier == true && IsTopTierItem(item) == true)
				{
					weight *= Mathf.Max(0.0f, settings.TopTierBonus);
				}

				_entries.Add(new Entry { Item = item, Weight = weight });
				total += weight;
			}

			float pick = Random.value * total;
			for (int i = 0; i < _entries.Count; ++i)
			{
				pick -= _entries[i].Weight;
				if (pick <= 0.0f)
					return _entries[i].Item;
			}

			return _entries.Count > 0 ? _entries[_entries.Count - 1].Item : default;
		}

		/// <summary>Tier 3 armor and trauma kits: better odds at top-tier points.</summary>
		public static bool IsTopTierItem(LootStack item)
		{
			return (item.Kind == EItemKind.Armor && item.Index == 3) || (item.Kind == EItemKind.Heal && item.Index == (byte)EHealItem.TraumaKit);
		}

		/// <summary>Server: one item on the ground. Sits on the ground below the position.</summary>
		public static LootItem SpawnItem(NetworkRunner runner, LootStack stack, Vector3 position, float yaw)
		{
			LootItem prefab = LootSettings.Current.LootItemPrefab;
			if (runner == null || runner.IsServer == false || prefab == null || stack.IsEmpty == true)
			{
				if (prefab == null)
				{
					Debug.LogError("LootSettings has no LootItem prefab. Run Brownout → Setup → Build Loot Prefabs.");
				}

				return null;
			}

			position = FindGround(runner, position);

			return runner.Spawn(prefab, position, Quaternion.Euler(0.0f, yaw, 0.0f), PlayerRef.None, (r, obj) => obj.GetComponent<LootItem>().Init(stack));
		}

		/// <summary>Server: a gun with its boxes beside it, or any other item, at one spot. Returns how many items it made.</summary>
		public static int SpawnWithBoxes(NetworkRunner runner, LootStack stack, Vector3 position, float yaw)
		{
			if (SpawnItem(runner, stack, position, yaw) == null)
				return 0;

			int spawned = 1;

			GunData gun = stack.Kind == EItemKind.Gun ? ItemCatalog.GetGunData(stack.Index) : null;
			if (gun != null)
			{
				int     boxes = Mathf.Max(0, LootSettings.Current.BoxesBesideGun);
				Vector3 side  = Quaternion.Euler(0.0f, yaw, 0.0f) * Vector3.right;
				Vector3 back  = Quaternion.Euler(0.0f, yaw, 0.0f) * Vector3.back;

				for (int i = 0; i < boxes; ++i)
				{
					Vector3 offset = side * (i % 2 == 0 ? 0.45f : -0.45f) + back * (0.35f * (i / 2));
					if (SpawnItem(runner, ItemCatalog.GetAmmoBox(gun), position + offset, yaw + Random.Range(-20.0f, 20.0f)) != null)
					{
						++spawned;
					}
				}
			}

			return spawned;
		}

		/// <summary>Server: drops an item at the agent's feet, a little in front and to the side so drops don't stack.</summary>
		public static LootItem DropNear(Agent agent, LootStack stack)
		{
			if (agent == null || stack.IsEmpty == true)
				return null;

			Quaternion facing = Quaternion.Euler(0.0f, agent.Character.CharacterController.FixedData.LookYaw, 0.0f);
			Vector2    spread = Random.insideUnitCircle * 0.35f;
			Vector3    spot   = agent.transform.position + facing * new Vector3(spread.x, 0.0f, 0.8f + spread.y);

			// Not through a wall: if something is between the player and the spot, drop it at the feet
			Vector3 from = agent.transform.position + Vector3.up * 0.5f;
			Vector3 to   = spot + Vector3.up * 0.5f;
			Vector3 path = to - from;
			if (agent.Runner.GetPhysicsScene().Raycast(from, path.normalized, out RaycastHit hit, path.magnitude, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
			{
				spot = agent.transform.position;
			}

			return SpawnItem(agent.Runner, stack, spot, Random.Range(0.0f, 360.0f));
		}

		/// <summary>Server: a loot bag holding these items, on the ground at the position.</summary>
		public static LootBag SpawnBag(NetworkRunner runner, Vector3 position, string ownerName, List<LootStack> items)
		{
			LootBag prefab = LootSettings.Current.LootBagPrefab;
			if (runner == null || runner.IsServer == false || prefab == null)
			{
				if (prefab == null)
				{
					Debug.LogError("LootSettings has no LootBag prefab. Run Brownout → Setup → Build Loot Prefabs.");
				}

				return null;
			}

			// Anything past the bag's capacity goes on the ground around it
			for (int i = LootBag.Capacity; i < items.Count; ++i)
			{
				Vector2 spread = Random.insideUnitCircle * 0.8f;
				SpawnItem(runner, items[i], position + new Vector3(spread.x, 0.0f, spread.y), Random.Range(0.0f, 360.0f));
			}

			position = FindGround(runner, position);
			return runner.Spawn(prefab, position, Quaternion.Euler(0.0f, Random.Range(0.0f, 360.0f), 0.0f), PlayerRef.None, (r, obj) => obj.GetComponent<LootBag>().Init(ownerName, items));
		}

		/// <summary>
		/// Server, test tool: lays items out in a row across the view, `distance` in front of the agent, `spacing` apart, centered on
		/// where it looks (guns with their ammo boxes beside them). Returns how many items it made.
		/// </summary>
		public static int LayOut(Agent agent, List<LootStack> items, float distance, float spacing, bool boxesBesideGuns)
		{
			if (agent == null || agent.Runner == null || agent.Runner.IsServer == false || items.Count == 0)
				return 0;

			float      yaw     = agent.Character.CharacterController.FixedData.LookYaw;
			Quaternion facing  = Quaternion.Euler(0.0f, yaw, 0.0f);
			Vector3    forward = facing * Vector3.forward;
			Vector3    right   = facing * Vector3.right;
			Vector3    center  = agent.transform.position + forward * distance;

			int spawned = 0;
			for (int i = 0; i < items.Count; ++i)
			{
				Vector3 spot = center + right * ((i - (items.Count - 1) * 0.5f) * spacing);

				// Guns point away from the player, so they're seen side-on
				float itemYaw = items[i].Kind == EItemKind.Gun ? yaw + 90.0f : yaw + 180.0f;

				spawned += boxesBesideGuns == true ? SpawnWithBoxes(agent.Runner, items[i], spot, itemYaw) : (SpawnItem(agent.Runner, items[i], spot, itemYaw) != null ? 1 : 0);
			}

			LootDebug.Log(agent.Runner, $"SPAWN laid out {items.Count} items ({spawned} with boxes) {distance:F0} m in front of {agent.name}, {spacing:F1} m apart");
			return spawned;
		}

		// PRIVATE METHODS

		private static int SpawnPoint(NetworkRunner runner, Point point)
		{
			return SpawnWithBoxes(runner, Roll(point.TopTier), point.Position, Random.Range(0.0f, 360.0f));
		}

		/// <summary>The ground under a position (searching from a little above it), or the position itself.</summary>
		private static Vector3 FindGround(NetworkRunner runner, Vector3 position)
		{
			if (runner.GetPhysicsScene().Raycast(position + Vector3.up * 1.0f, Vector3.down, out RaycastHit hit, 4.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
				return hit.point;

			return position;
		}
	}
}
