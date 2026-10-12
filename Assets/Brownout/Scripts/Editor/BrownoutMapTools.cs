namespace TPSBR
{
	using System.Collections;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using Fusion;
	using Fusion.Addons.KCC;
	using TPSBR.UI;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Map tools (phase 8a). Brownout → Test → Map: Teleport To (each POI and cabin area), Log Map Status, and the Map Check: run it in the
	/// Brownout scene with 2 peers during the warm-up, from the lagged client (switch to the client first). It checks the map's size and
	/// edge, the grid and labels on the full map, the loot points and spawn points, the river wall (walking and jumping into it stops you;
	/// the bullet, grenade and grapple masks pass it; a real shot across hits a dummy), standing in the river puts you on the bank, a
	/// parachute crosses it, every bridge can be walked; since 8b a door opens away from you both ways and F closes it, a window
	/// mantle-through both ways, sight through a window (crouched hidden, standing seen), a stair flight, a power door shut without power
	/// and open with it, down into the underground; and records the frame rate. About 3 minutes. Read GetMapCheckLog() or
	/// Logs/MapCheck/MapCheck.txt; pictures in Logs/MapCheck/.
	/// </summary>
	public static class BrownoutMapTools
	{
		// CONSTANTS

		private const string MENU   = "Brownout/Test/Map/";
		public  const string Folder = "Logs/MapCheck";

		// MENU

		[MenuItem(MENU + "Map Check", priority = 1)]
		private static void CheckMenu() => Debug.Log(RunMapCheck());

		[MenuItem(MENU + "Log Map Status", priority = 2)]
		private static void StatusMenu() => Debug.Log(GetMapStatus());

		[MenuItem(MENU + "Teleport To/Power Plant", priority = 20)]   private static void T1() => Debug.Log(TeleportTo("power_plant"));
		[MenuItem(MENU + "Teleport To/Radio Array", priority = 21)]   private static void T2() => Debug.Log(TeleportTo("radio_array"));
		[MenuItem(MENU + "Teleport To/Data Center", priority = 22)]   private static void T3() => Debug.Log(TeleportTo("data_center"));
		[MenuItem(MENU + "Teleport To/Train Station", priority = 23)] private static void T4() => Debug.Log(TeleportTo("train_station"));
		[MenuItem(MENU + "Teleport To/Warehouses", priority = 24)]    private static void T5() => Debug.Log(TeleportTo("warehouses"));
		[MenuItem(MENU + "Teleport To/Factory", priority = 25)]       private static void T6() => Debug.Log(TeleportTo("factory"));
		[MenuItem(MENU + "Teleport To/Cabins, northwest", priority = 40)] private static void T7() => Debug.Log(TeleportTo("cabins_northwest"));
		[MenuItem(MENU + "Teleport To/Cabins, ridge", priority = 41)]     private static void T8() => Debug.Log(TeleportTo("cabins_ridge"));
		[MenuItem(MENU + "Teleport To/Cabins, center", priority = 42)]    private static void T9() => Debug.Log(TeleportTo("cabins_center"));
		[MenuItem(MENU + "Teleport To/Cabins, south", priority = 43)]     private static void T10() => Debug.Log(TeleportTo("cabins_south"));

		[MenuItem(MENU + "Power All POIs On", priority = 60)]
		private static void PowerOnMenu() => Debug.Log(SetPowerAll(true));

		[MenuItem(MENU + "Power All POIs Off", priority = 61)]
		private static void PowerOffMenu() => Debug.Log(SetPowerAll(false));

		[MenuItem(MENU + "Show Loot Points", priority = 62)]
		private static void ShowLootMenu() => Debug.Log(ShowLootPoints());

		[MenuItem(MENU + "Map Check", true)]
		[MenuItem(MENU + "Log Map Status", true)]
		[MenuItem(MENU + "Power All POIs On", true)]
		[MenuItem(MENU + "Power All POIs Off", true)]
		private static bool ValidatePlaying() => Application.isPlaying;

		// TOOLS

		public static string TeleportTo(string areaId)
		{
			Agent agent = ObservedServerAgent();
			BrownoutMap map = agent != null ? BrownoutMap.Get(agent.gameObject.scene) : null;
			if (agent == null || map == null)
				return "Need a player on the Brownout map (Play mode).";

			if (map.TryGetArea(areaId, out BrownoutMap.Area area) == false)
				return $"No area '{areaId}'.";

			Vector3 position = Ground(agent.Runner, new Vector3(area.Center.x, 0.0f, area.Center.y)) + Vector3.up * 0.1f;
			Move(agent, position);
			return $"{agent.name} put at {area.Name}, {position:F0}.";
		}

		/// <summary>Server: every POI's power on or off (phase 8b test switch: power doors open or shut, lights on or off; phase 11 picks 2-3 POIs).</summary>
		public static string SetPowerAll(bool powered)
		{
			Agent       agent = ObservedServerAgent();
			MapState    state = agent != null ? MapState.Get(agent.gameObject.scene) : null;
			BrownoutMap map   = agent != null ? BrownoutMap.Get(agent.gameObject.scene) : null;
			if (state == null || map == null)
				return "Need a player on the Brownout map (Play mode).";

			for (int area = 0; area < map.Areas.Count; ++area)
			{
				state.SetPower(area, powered);
			}

			return $"Power {(powered ? "on" : "off")} at all {map.Areas.Count} areas: {map.Doors.Count(d => d != null && d.IsPower)} power doors {(powered ? "open" : "shut and locked")}, {map.PowerProps.Count(p => p != null && p.Kind == EPowerProp.Light)} lights {(powered ? "on" : "off")}.";
		}

		/// <summary>Selects every loot point in the open scenes (they show in the Scene view) and counts them by area.</summary>
		public static string ShowLootPoints()
		{
			LootPoint[] points = Object.FindObjectsByType<LootPoint>(FindObjectsSortMode.None);
			Selection.objects = points.Select(t => (Object)t.gameObject).ToArray();

			var lines = new List<string> { $"{points.Length} loot points selected." };
			foreach (var group in points.GroupBy(t => t.transform.parent != null && t.transform.parent.parent != null ? t.transform.parent.parent.name : "?"))
			{
				int top = group.Count(t => t.TopTier);
				lines.Add($"  {group.Key}: {group.Count()}{(top > 0 ? $" ({top} top tier)" : string.Empty)}");
			}

			return string.Join("\n", lines);
		}

		public static string GetMapStatus()
		{
			Agent agent = ObservedServerAgent();
			if (agent == null)
				return "No observed player on a running host.";

			BrownoutMap map = BrownoutMap.Get(agent.gameObject.scene);
			var lines = new List<string>();

			MapBounds.TryGet(agent.Context, out Vector3 center, out Vector2 half);
			lines.Add($"Map {half.x * 2.0f:F0} × {half.y * 2.0f:F0} m on {center:F0}; Brownout map {(map != null ? "found" : "missing")}");

			if (map != null)
			{
				float river = map.GetRiverDistance(agent.transform.position, out _, out float water, out _);
				Vector2 file = BrownoutMapData.ToFile(new Vector2(agent.transform.position.x, agent.transform.position.z));
				lines.Add($"{agent.name} at E{file.x:F0} N{file.y:F0}, height {agent.transform.position.y:F1} m, river {river:F1} m away (water at {water:F1} m)");

				foreach (BrownoutMap.Area area in map.Areas)
				{
					lines.Add($"  {area.Name,-30} {Vector2.Distance(area.Center, new Vector2(agent.transform.position.x, agent.transform.position.z)),6:F0} m away");
				}
			}

			lines.Add($"Loot points rolled: {LootSpawner.GetPoints(agent.Runner).Count}, spawn points: {Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None).Count(s => s.gameObject.scene == agent.gameObject.scene)}");
			return string.Join("\n", lines);
		}

		// THE CHECK

		private static readonly List<string>       _log     = new List<string>(128);
		private static readonly Stack<IEnumerator> _routine = new Stack<IEnumerator>();
		private static bool _running;
		private static int  _passed;
		private static int  _checked;

		public static string RunMapCheck()
		{
			if (Application.isPlaying == false)
				return "Enter Play mode first (the Brownout scene, 2 peers).";
			if (_running == true)
				return "The Map Check is already running.";

			_log.Clear();
			_routine.Clear();
			_routine.Push(CheckRoutine());
			_running = true;

			EditorApplication.update -= UpdateRoutine;
			EditorApplication.update += UpdateRoutine;

			return "Map Check started (about 3 minutes). Read GetMapCheckLog().";
		}

		public static string GetMapCheckLog()
		{
			return (_running == true ? "(running)\n" : "") + string.Join("\n", _log);
		}

		private static IEnumerator CheckRoutine()
		{
			BrownoutHud hud = FindInputHud();
			if (hud == null || hud.Agent == null)
			{
				Log("Need the Brownout scene in Play mode with a player on the ground (warm-up) and the HUD of the peer with input.");
				yield break;
			}

			NetworkRunner client   = hud.Agent.Runner;
			PlayerRef     localRef = hud.Agent.Object.InputAuthority;
			Agent         agent    = BrownoutHudCheck.ServerCopy(localRef);
			NetworkRunner server   = agent != null ? agent.Runner : null;
			BrownoutMap   map      = agent != null ? BrownoutMap.Get(agent.gameObject.scene) : null;
			GameplayMode  mode     = agent != null ? agent.Context.GameplayMode : null;

			if (agent == null || map == null || mode == null)
			{
				Log("No Brownout map on the host (open Assets/Scenes/Brownout.unity and press Play).");
				yield break;
			}

			_passed  = 0;
			_checked = 0;
			MatchDebug.Clear();
			ReleaseInput();

			BrownoutMapSettings settings = BrownoutMapSettings.Current;
			float halfWidth = map.RiverHalfWidth;
			Log($"Map Check: viewed from {(client.IsServer == false ? "the client (simulated lag in play)" : "the host")}, mode {mode.Type}.");

			// 1. The map's square, the grid and the labels
			MapBounds.TryGet(agent.Context, out Vector3 mapCenter, out Vector2 mapHalf);
			Check(Mathf.Approximately(mapHalf.x, 500.0f) && Mathf.Approximately(mapHalf.y, 500.0f) && mapCenter.sqrMagnitude < 0.01f, $"Map is {mapHalf.x * 2.0f:F0} × {mapHalf.y * 2.0f:F0} m on {mapCenter:F0} (edge at ±500 m)");
			Check(BrownoutMap.Get(client.SimulationUnityScene) != null, "Both peers have the map's data");

			string square = hud.GetGridSquare(new Vector3(776.0f - 500.0f, 0.0f, 876.0f - 500.0f));
			Check(square == "H2", $"Grid: the Power Plant's center (E776 N876) is in {square} (Map & POIs: H1–H3), 10 × 10 cells of 100 m");

			hud.SetMapOpen(true);
			yield return Wait(0.5f);
			Check(Read(hud, "map.labels") == "6", $"Full map shows the six POI names: {Read(hud, "map.labels")}");
			Screenshot("01_full_map");
			hud.SetMapOpen(false);
			yield return Wait(0.3f);

			// 2. Loot points and spawn points
			BrownoutMapData.LootFile lootFile = BrownoutMapData.LoadLoot();
			int planned  = lootFile.areas.Sum(a => a.Total);
			int inScene  = Object.FindObjectsByType<LootPoint>(FindObjectsSortMode.None).Count(p => p.gameObject.scene == agent.gameObject.scene);
			int rolled   = LootSpawner.GetPoints(server).Count;
			Check(rolled == inScene && inScene >= planned * 0.95f, $"Loot points: {rolled} rolled, {inScene} in the scene, {planned} planned in map_loot.json");

			int spawns = Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None).Count(s => s.gameObject.scene == agent.gameObject.scene);
			Check(spawns == 16, $"Warm-up spawn points: {spawns} (two per POI, one per cabin area)");

			// Every area got its loot
			var missing = new List<string>();
			foreach (BrownoutMapData.LootArea area in lootFile.areas)
			{
				int near = LootSpawner.GetPoints(server).Count(p => area.spots.Any(s => Vector2.Distance(s.World, new Vector2(p.Position.x, p.Position.z)) <= s.radius + 0.5f));
				if (near < area.Total * 0.9f)
				{
					missing.Add($"{area.name} {near}/{area.Total}");
				}
			}
			Check(missing.Count == 0, missing.Count == 0 ? "Every POI and cabin area has its loot points" : $"Areas short of loot: {string.Join(", ", missing)}");

			// 3. The river wall. A stretch of river on the map, away from the bridges and roads
			if (FindRiverStretch(server, map, out Vector2 riverPoint, out Vector2 across) == false)
			{
				Log("No clear stretch of river found on the map; skipping the river checks.");
			}
			else
			{
				Vector2 fileRiver = BrownoutMapData.ToFile(riverPoint);
				Log($"River checks at E{fileRiver.x:F0} N{fileRiver.y:F0}.");

				Vector3 nearBank = Ground(server, To3(riverPoint + across * (halfWidth + 3.5f)));
				Vector3 farBank  = Ground(server, To3(riverPoint - across * (halfWidth + 3.5f)));

				// Walking into it
				Move(agent, nearBank + Vector3.up * 0.1f);
				yield return Wait(1.5f);
				BrownoutTestTools.AimObservedAt(farBank + Vector3.up * 1.6f);
				yield return Wait(0.3f);
				AgentInput.DebugMoveDirection = new Vector2(0.0f, 1.0f);
				AgentInput.DebugHoldSprint    = true;
				float closest = float.MaxValue;
				for (float t = 0.0f; t < 2.5f; t += 0.1f)
				{
					yield return Wait(0.1f);
					closest = Mathf.Min(closest, map.GetRiverDistance(agent.transform.position, out _, out _, out _));
				}
				AgentInput.DebugHoldJump = true;
				for (float t = 0.0f; t < 2.0f; t += 0.1f)
				{
					yield return Wait(0.1f);
					closest = Mathf.Min(closest, map.GetRiverDistance(agent.transform.position, out _, out _, out _));
				}
				ReleaseInput();
				bool sameSide = Vector2.Dot(Flat(agent.transform.position) - riverPoint, across) > 0.0f;
				Check(sameSide == true && closest > halfWidth + 0.4f, $"Sprinting and jumping into the river: stopped {closest:F2} m from its middle (the wall is {halfWidth + 0.5f:F1} m out), still on the near bank");
				Screenshot("02_river_wall");

				// What crosses it: bullet and grenade masks, the grapple ray; the wall itself is there for an everything-mask ray
				Vector3 from = nearBank + Vector3.up * 1.5f;
				Vector3 to   = farBank  + Vector3.up * 1.5f;
				PhysicsScene physics = server.GetPhysicsScene();
				bool wallThere   = HitsRiverWall(physics, from, to, ~0);
				bool bulletsPass = HitsRiverWall(physics, from, to, agent.Weapons.HitMask) == false;
				bool grapplePass = HitsRiverWall(physics, from, to, ObjectLayerMask.Environment) == false;
				Check(wallThere == true && bulletsPass == true && grapplePass == true, $"The wall is there (an all-layer ray hits it: {wallThere}); the bullet and grenade mask passes it: {bulletsPass}; the grapple ray passes it: {grapplePass}");

				// A real shot across the river at a dummy
				Agent dummy = mode.SpawnDummy(farBank + Vector3.up * 0.1f, Quaternion.LookRotation(nearBank - farBank), mode.NewDummyTeam());
				Log(BrownoutTestTools.GiveGun("Icarus"));
				BrownoutTestTools.SetGunplayDebug(true, true, false);
				yield return Wait(2.0f);
				float before = dummy != null ? dummy.Health.CurrentHealth + dummy.Health.ArmorLeft : 0.0f;
				BrownoutTestTools.AimObservedAt(dummy != null ? dummy.transform.position + Vector3.up * 1.3f : to);
				yield return Wait(0.3f);
				AgentInput.DebugHoldAim = true;
				yield return Wait(0.6f);
				AgentInput.DebugHoldAttack = true;
				yield return Wait(0.5f);
				ReleaseInput();
				yield return Wait(0.5f);
				float after = dummy != null ? dummy.Health.CurrentHealth + dummy.Health.ArmorLeft : 0.0f;
				Check(dummy != null && after < before, $"A shot across the river hits a dummy on the far bank: health and armor {before:F0} → {after:F0}");
				Screenshot("03_shot_across");
				BrownoutTestTools.SetGunplayDebug(false, false, false);
				mode.RemoveDummies();

				// Standing in the river: put on the nearer bank
				MatchDebug.Clear();
				Vector3 inWater = To3(riverPoint + across * 0.6f);
				map.GetRiverDistance(inWater, out _, out float water, out _);
				Move(agent, new Vector3(inWater.x, water - settings.WaterDepth + 0.1f, inWater.z));
				yield return Wait(1.5f);
				float bankDistance = map.GetRiverDistance(agent.transform.position, out _, out _, out _);
				bool  rule = MatchDebug.EventLog.Any(l => l.Contains("RIVER"));
				Check(rule == true && bankDistance > halfWidth + 0.5f && Vector2.Dot(Flat(agent.transform.position) - riverPoint, across) > 0.0f, $"Standing in the river puts you on the nearer bank: {bankDistance:F1} m from the middle, event {(rule ? "RIVER" : "none")}");

				// A parachute passes over the wall (drops ignore it)
				Move(agent, nearBank + Vector3.up * 18.0f);
				BrownoutTestTools.AimObservedAt(farBank + Vector3.up * 18.0f);
				yield return Wait(0.2f);
				Vector3 velocity = To3(-across) * 10.0f + Vector3.down * 2.0f;
				agent.Movement.StartDrop(velocity);
				yield return Wait(4.0f);
				bool crossed = Vector2.Dot(Flat(agent.transform.position) - riverPoint, across) < 0.0f;
				bool putBack = MatchDebug.EventLog.Count(l => l.Contains("RIVER")) > 1;
				Check(crossed == true || putBack == true, $"A drop passes over the wall: landed on the {(crossed ? "far bank" : "near bank")}{(putBack ? " after the river rule" : string.Empty)}, mode {agent.Character.CharacterController.FixedData.MoveMode}");
			}

			// 4. Every bridge can be walked across
			Transform bridges = FindSection(server, "Bridges");
			int walked = 0;
			int total  = bridges != null ? bridges.childCount : 0;
			for (int i = 0; i < total; ++i)
			{
				Transform deck = bridges.GetChild(i).Find("Deck");
				if (deck == null)
					continue;

				float   length = deck.localScale.z;
				Vector3 start  = deck.position - deck.forward * (length * 0.5f + 1.5f);
				Vector3 end    = deck.position + deck.forward * (length * 0.5f + 1.5f);

				Move(agent, Ground(server, start) + Vector3.up * 0.2f);
				yield return Wait(1.5f);
				BrownoutTestTools.AimObservedAt(Ground(server, end) + Vector3.up * 1.6f);
				yield return Wait(0.2f);
				AgentInput.DebugMoveDirection = new Vector2(0.0f, 1.0f);
				AgentInput.DebugHoldSprint    = true;
				int riverEvents = MatchDebug.EventLog.Count(l => l.Contains("RIVER"));

				float limit = (length + 6.0f) / 3.5f + 2.0f;
				bool  made  = false;
				for (float t = 0.0f; t < limit; t += 0.25f)
				{
					yield return Wait(0.25f);
					if (Vector3.Dot(agent.transform.position - deck.position, deck.forward) > length * 0.5f + 0.5f)
					{
						made = true;
						break;
					}
				}

				ReleaseInput();
				bool moved = MatchDebug.EventLog.Count(l => l.Contains("RIVER")) > riverEvents;
				made &= moved == false;
				walked += made == true ? 1 : 0;
				Log($"  {bridges.GetChild(i).name}: {length:F0} m, {(made ? "crossed" : $"stopped at {agent.transform.position:F0}")}{(moved ? " (the river rule moved the player)" : string.Empty)}");
				if (i == 0) Screenshot("04_bridge");
			}
			Check(total == 5 && walked == total, $"Bridges walked end to end: {walked} of {total}");

			// 5. Buildings (8b): doors, windows, stairs, power doors and the underground
			yield return BuildingChecks(client, server, agent, map);

			// 6. Pictures of each POI from the other peer's view and the frame rate
			string[] pois = { "power_plant", "radio_array", "data_center", "train_station", "warehouses", "factory" };
			for (int i = 0; i < pois.Length; ++i)
			{
				if (map.TryGetArea(pois[i], out BrownoutMap.Area area) == false)
					continue;

				Move(agent, Ground(server, To3(area.Center)) + Vector3.up * 0.1f);
				yield return Wait(1.2f);
				BrownoutTestTools.AimObservedAt(Ground(server, To3(area.Center + new Vector2(60.0f, 30.0f))) + Vector3.up * 2.0f);
				yield return Wait(0.5f);
				Screenshot($"05_{pois[i]}");
			}

			// Frame rate: standing at the map center, looking across it
			Move(agent, Ground(server, Vector3.zero) + Vector3.up * 0.1f);
			yield return Wait(1.5f);
			int    frames = Time.frameCount;
			double clock  = EditorApplication.timeSinceStartup;
			yield return Wait(8.0f);
			double fps = (Time.frameCount - frames) / (EditorApplication.timeSinceStartup - clock);
			Log($"Frame rate at the map center with 2 peers: {fps:F0} fps over 8 s (target 60; editor, measured not judged).");

			Log($"Map Check done: {_passed} of {_checked} passed.");
			SaveLog();
		}

		// BUILDING CHECKS (8b)

		private static IEnumerator BuildingChecks(NetworkRunner client, NetworkRunner server, Agent agent, BrownoutMap map)
		{
			MapState state       = MapState.Get(server.SimulationUnityScene);
			MapState clientState = MapState.Get(client.SimulationUnityScene);
			Check(state != null && clientState != null && map.Doors.Count > 0, $"Map state on both peers; {map.Doors.Count} doors ({map.Doors.Count(d => d.IsPower)} power doors), {map.PowerProps.Count} power props");
			if (state == null || clientState == null)
				yield break;

			for (int area = 0; area < map.Areas.Count; ++area)
			{
				state.SetPower(area, false);
			}
			state.CloseAllDoors();

			Transform buildings = FindSection(server, "Buildings");

			// A cabin's front door, walked through both ways, closed with F after each
			Door door = map.Doors.FirstOrDefault(d => d != null && d.IsPower == false && d.transform.parent != null && d.transform.parent.name == "Cabin");
			if (door == null)
			{
				Log("No cabin door found; skipping the door checks.");
			}
			else
			{
				Vector3 normal = door.Normal;
				Vector3 middle = door.transform.position + door.transform.right * 0.375f;
				float   floorY = door.transform.position.y;
				Log($"Door checks at {door.name} of {PathOf(door.transform.parent)}.");

				for (int pass = 0; pass < 2; ++pass)
				{
					float   side  = pass == 0 ? 1.0f : -1.0f;
					Vector3 start = Floor(server, middle + normal * side * 1.6f, floorY + 1.0f) + Vector3.up * 0.1f;
					Vector3 goal  = middle - normal * side * 2.0f;

					MatchDebug.Clear();
					Move(agent, start);
					yield return Wait(1.5f);
					BrownoutTestTools.AimObservedAt(new Vector3(goal.x, floorY + 1.6f, goal.z));
					yield return Wait(0.3f);
					AgentInput.DebugMoveDirection = new Vector2(0.0f, 1.0f);

					bool through = false;
					for (float t = 0.0f; t < 4.0f && through == false; t += 0.1f)
					{
						yield return Wait(0.1f);
						through = Vector3.Dot(agent.transform.position - middle, normal * side) < -0.8f;
					}
					ReleaseInput();
					yield return Wait(0.6f);

					Vector3 leaf   = door.transform.TransformPoint(new Vector3(0.375f, 1.0f, 0.0f));
					bool    away   = Vector3.Dot(leaf - door.transform.position, normal * side) < -0.2f;
					bool    open   = state.IsDoorOpen(door.Index);
					bool    seen   = clientState.IsDoorOpen(door.Index);
					bool    logged = MatchDebug.EventLog.Any(l => l.Contains("DOOR") && l.Contains("opened"));
					Check(open && seen && away && through && logged, $"Walking into the door from the {(pass == 0 ? "inside" : "outside")}: open {open} (the client sees it {seen}), swung away from the player {away}, walked through {through}");
					if (pass == 0) Screenshot("06_door_open");

					// F closes it, looking at the leaf
					BrownoutTestTools.AimObservedAt(leaf);
					yield return Wait(0.4f);
					AgentInput.DebugHoldInteract = true;
					yield return Wait(0.3f);
					AgentInput.DebugHoldInteract = false;
					yield return Wait(0.8f);
					Check(state.IsDoorOpen(door.Index) == false && clientState.IsDoorOpen(door.Index) == false && Mathf.Abs(door.Angle) < 1.0f, $"F closes it ({(pass == 0 ? "from the outside" : "from the inside")}): open {state.IsDoorOpen(door.Index)}, angle on the host {door.Angle:F0}°");
				}
			}

			// A cabin window: mantle through it from outside, then back out; a crouched player behind it is hidden, a standing one isn't
			Transform sill = buildings != null ? buildings.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Wall E below" && t.parent != null && t.parent.name == "Cabin") : null;
			if (sill == null)
			{
				Log("No cabin window found; skipping the window checks.");
			}
			else
			{
				Transform cabin  = sill.parent;
				Vector3   normal = cabin.right;
				Vector3   window = sill.position + Vector3.up * (sill.localScale.y * 0.5f);   // Middle of the sill's top
				float     floorY = cabin.position.y;
				Log($"Window checks at {PathOf(cabin)}, sill {window.y - floorY:F2} m above the floor.");

				for (int pass = 0; pass < 2; ++pass)
				{
					float   side  = pass == 0 ? 1.0f : -1.0f;
					Vector3 start = Floor(server, window + normal * side * 0.75f, window.y + 0.5f) + Vector3.up * 0.1f;
					Vector3 goal  = window - normal * side * 3.0f;

					Move(agent, start);
					yield return Wait(1.5f);
					BrownoutTestTools.AimObservedAt(new Vector3(goal.x, window.y + 0.5f, goal.z));
					yield return Wait(0.3f);
					AgentInput.DebugMoveDirection = new Vector2(0.0f, 1.0f);
					AgentInput.DebugHoldJump      = true;

					bool through  = false;
					bool mantled  = false;
					for (float t = 0.0f; t < 3.0f && through == false; t += 0.1f)
					{
						yield return Wait(0.1f);
						mantled |= agent.Character.CharacterController.FixedData.MoveMode == EMoveMode.Mantle;
						through  = Vector3.Dot(agent.transform.position - window, normal * side) < -0.4f;
					}
					ReleaseInput();
					yield return Wait(0.5f);
					Check(through == true && mantled == true, $"Window mantle-through from {(pass == 0 ? "outside" : "inside")}: through {through}, mantled {mantled}");
					if (pass == 0) Screenshot("07_window_mantle");
				}

				// Sight through the window: eyes outside at 3, 6 and 10 m; a crouched player (0.92 m) and a standing one 0.6 m behind the wall
				PhysicsScene physics = server.GetPhysicsScene();
				Vector3 behind  = window - normal * (0.6f + 0.25f);
				int     blocked = 0, rays = 0, closeSeen = 0;
				bool    standingSeen = false;
				foreach (float distance in new[] { 3.0f, 6.0f, 10.0f, 20.0f })
				{
					Vector3 eye = Floor(server, window + normal * distance, window.y + 3.0f) + Vector3.up * 1.7f;
					for (float h = 0.2f; h <= 0.901f; h += 0.1f)
					{
						bool hidden = Blocked(physics, eye, new Vector3(behind.x, floorY + h, behind.z));
						if (distance < 5.0f)
						{
							closeSeen += hidden == false ? 1 : 0;   // Looking down from right outside, the top of a head close behind the sill shows
							continue;
						}

						++rays;
						blocked += hidden == true ? 1 : 0;
					}
					standingSeen |= Blocked(physics, eye, new Vector3(behind.x, floorY + 1.6f, behind.z)) == false;
				}
				Check(blocked == rays && standingSeen == true, $"Behind a window: a crouched player is hidden from 6, 10 and 20 m outside ({blocked} of {rays} sight lines blocked), a standing player's head is seen: {standingSeen} (from 3 m, looking down, {closeSeen} of 8 points up to 0.9 m show)");
			}

			// Stairs: up the Reactor hall's flight
			Transform flight = buildings != null ? buildings.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Stairs 1" && t.parent != null && t.parent.name == "Reactor hall") : null;
			if (flight != null)
			{
				Vector3 along  = flight.forward;
				Vector3 flat   = new Vector3(along.x, 0.0f, along.z).normalized;
				Vector3 bottom = flight.position - along * (flight.localScale.z * 0.5f);
				float   floorY = flight.parent.position.y;
				Vector3 start  = Floor(server, bottom - flat * 1.0f, floorY + 1.0f) + Vector3.up * 0.1f;
				Move(agent, start);
				yield return Wait(1.5f);
				BrownoutTestTools.AimObservedAt(start + flat * 20.0f + Vector3.up * 6.0f);
				yield return Wait(0.3f);
				AgentInput.DebugMoveDirection = new Vector2(0.0f, 1.0f);
				float highest = start.y;
				for (float t = 0.0f; t < 3.5f; t += 0.1f)
				{
					yield return Wait(0.1f);
					highest = Mathf.Max(highest, agent.transform.position.y);
				}
				ReleaseInput();
				Check(highest - start.y > 4.0f, $"Stairs: walked up the Reactor hall's flight, {highest - start.y:F1} m (the upper floor is 4.5 m up)");
			}

			// Power doors and the underground: the Power Plant's first entrance
			Door powerDoor = map.Doors.FirstOrDefault(d => d != null && d.IsPower == true && d.transform.parent != null && d.transform.parent.name.StartsWith("Entrance"));
			if (powerDoor == null)
			{
				Log("No underground entrance found; skipping the power door checks.");
			}
			else
			{
				Transform hut    = powerDoor.transform.parent;
				Vector3   down   = hut.forward;
				Vector3   middle = powerDoor.transform.position + powerDoor.transform.right * 0.375f;
				Vector3   start  = Floor(server, middle - down * 1.5f, hut.position.y + 1.0f) + Vector3.up * 0.1f;

				Move(agent, start);
				yield return Wait(1.5f);
				BrownoutTestTools.AimObservedAt(middle + down * 6.0f + Vector3.up * 1.0f);
				yield return Wait(0.3f);
				AgentInput.DebugMoveDirection = new Vector2(0.0f, 1.0f);
				yield return Wait(1.5f);
				ReleaseInput();
				bool shut = state.IsDoorOpen(powerDoor.Index) == false && Vector3.Dot(agent.transform.position - middle, down) < 0.0f;
				Check(shut == true, $"Without power the underground's door stays shut and locked: open {state.IsDoorOpen(powerDoor.Index)}, the player is still outside");

				state.SetPower(powerDoor.Area, true);
				yield return Wait(1.0f);
				Check(state.IsDoorOpen(powerDoor.Index) == true && clientState.IsDoorOpen(powerDoor.Index) == true && clientState.IsPowered(powerDoor.Area) == true, $"Powering the Power Plant opens its power doors (the client sees it {clientState.IsDoorOpen(powerDoor.Index)}) and switches its lights on");
				Screenshot("08_power_on");

				Move(agent, start);
				yield return Wait(1.5f);
				BrownoutTestTools.AimObservedAt(middle + down * 20.0f - Vector3.up * 4.0f);
				yield return Wait(0.3f);
				AgentInput.DebugMoveDirection = new Vector2(0.0f, 1.0f);
				float lowest = start.y;
				for (float t = 0.0f; t < 7.0f; t += 0.1f)
				{
					yield return Wait(0.1f);
					lowest = Mathf.Min(lowest, agent.transform.position.y);
				}
				ReleaseInput();
				Screenshot("09_underground");
				Check(start.y - lowest > 7.0f, $"Through the power door and down the ramp: {start.y - lowest:F1} m below the entrance (the reactor level is 8 m under the ground)");

				Terrain terrain = map.GetComponentInChildren<Terrain>();
				int underground = terrain != null ? LootSpawner.GetPoints(server).Count(p => p.Position.y < terrain.SampleHeight(p.Position) + terrain.transform.position.y - 3.0f) : 0;
				Check(underground >= 27 && underground <= 30, $"Loot underground: {underground} points (30 planned, top tier)");

				// Back out and power off again (the match default)
				Move(agent, start);
				state.SetPower(powerDoor.Area, false);
				yield return Wait(1.0f);
				Check(state.IsDoorOpen(powerDoor.Index) == false && clientState.IsPowered(powerDoor.Area) == false, "Power off shuts and locks the door again");
			}

			int indoors = LootSpawner.GetPoints(server).Count(p => server.GetPhysicsScene().Raycast(p.Position + Vector3.up * 0.5f, Vector3.up, 40.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore));
			Log($"Loot points under a roof: {indoors} of {LootSpawner.GetPoints(server).Count}.");
		}

		private static bool Blocked(PhysicsScene physics, Vector3 from, Vector3 to)
		{
			Vector3 direction = to - from;
			return physics.Raycast(from, direction.normalized, direction.magnitude - 0.05f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore);
		}

		/// <summary>The floor under a point, looking down from a height (so a roof above doesn't count).</summary>
		private static Vector3 Floor(NetworkRunner runner, Vector3 point, float fromY)
		{
			if (runner.GetPhysicsScene().Raycast(new Vector3(point.x, fromY, point.z), Vector3.down, out RaycastHit hit, 30.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
				return hit.point;

			return Ground(runner, point);
		}

		private static string PathOf(Transform transform)
		{
			return transform.parent != null ? $"{transform.parent.name} / {transform.name}" : transform.name;
		}

		// HELPERS

		/// <summary>A river point on the map, at least 80 m from the bridges and 25 m from roads, on gentle ground.</summary>
		private static bool FindRiverStretch(NetworkRunner server, BrownoutMap map, out Vector2 point, out Vector2 across)
		{
			point  = default;
			across = default;

			Transform bridges = FindSection(server, "Bridges");
			var bridgePoints = new List<Vector2>();
			if (bridges != null)
			{
				foreach (Transform bridge in bridges)
				{
					bridgePoints.Add(Flat(bridge.Find("Deck") != null ? bridge.Find("Deck").position : bridge.position));
				}
			}

			IReadOnlyList<Vector2> line = map.RiverLine;
			int middle = line.Count / 2;
			for (int offset = 0; offset < line.Count / 2; ++offset)
			{
				for (int sign = -1; sign <= 1; sign += 2)
				{
					int i = middle + offset * sign;
					if (i <= 0 || i >= line.Count - 1)
						continue;

					Vector2 p = line[i];
					if (Mathf.Abs(p.x) > 420.0f || Mathf.Abs(p.y) > 420.0f)
						continue;
					if (bridgePoints.Any(b => Vector2.Distance(b, p) < 80.0f))
						continue;

					Vector2 tangent = (line[i + 1] - line[i - 1]).normalized;
					Vector2 normal  = new Vector2(-tangent.y, tangent.x);
					Vector3 a = Ground(server, To3(p + normal * 6.0f));
					Vector3 b = Ground(server, To3(p - normal * 6.0f));
					if (Mathf.Abs(a.y - b.y) > 1.5f)
						continue;

					point  = p;
					across = normal;
					return true;
				}
			}

			return false;
		}

		private static bool HitsRiverWall(PhysicsScene physics, Vector3 from, Vector3 to, int mask)
		{
			var hits = new RaycastHit[16];
			int count = physics.Raycast(from, (to - from).normalized, hits, Vector3.Distance(from, to), mask, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < count; ++i)
			{
				if (hits[i].collider != null && hits[i].collider.gameObject.layer == Agent.RiverWallLayer)
					return true;
			}

			return false;
		}

		private static Transform FindSection(NetworkRunner runner, string name)
		{
			// Multipeer nests each peer's scene under a "[Scene]" root, so go through the map component
			BrownoutMap map = BrownoutMap.Get(runner.SimulationUnityScene);
			return map != null ? map.transform.Find(name) : null;
		}

		private static void ReleaseInput()
		{
			AgentInput.DebugMoveDirection = Vector2.zero;
			AgentInput.DebugHoldSprint    = false;
			AgentInput.DebugHoldJump      = false;
			AgentInput.DebugHoldAttack    = false;
			AgentInput.DebugHoldAim       = false;
		}

		private static Agent ObservedServerAgent()
		{
			BrownoutHud hud = FindInputHud();
			return hud != null && hud.Agent != null ? BrownoutHudCheck.ServerCopy(hud.Agent.Object.InputAuthority) : null;
		}

		private static BrownoutHud FindInputHud()
		{
			foreach (BrownoutHud candidate in Object.FindObjectsByType<BrownoutHud>(FindObjectsSortMode.None))
			{
				Agent agent = candidate.Agent;
				if (agent != null && agent.Object != null && agent.Object.IsValid == true && agent.Context != null && agent.Context.HasInput == true && agent.HasInputAuthority == true)
					return candidate;
			}

			return null;
		}

		private static Vector3 Ground(NetworkRunner runner, Vector3 point)
		{
			if (runner.GetPhysicsScene().Raycast(new Vector3(point.x, 400.0f, point.z), Vector3.down, out RaycastHit hit, 600.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
				return hit.point;

			return new Vector3(point.x, 0.0f, point.z);
		}

		private static void Move(Agent agent, Vector3 position)
		{
			KCC kcc = agent.Character.CharacterController;

			kcc.FixedData.BasePosition     = position;
			kcc.FixedData.DesiredPosition  = position;
			kcc.FixedData.TargetPosition   = position;
			kcc.RenderData.BasePosition    = position;
			kcc.RenderData.DesiredPosition = position;
			kcc.RenderData.TargetPosition  = position;

			kcc.SetPosition(position, true, false, false);
			agent.transform.position = position;
		}

		private static Vector3 To3(Vector2 point) => new Vector3(point.x, 0.0f, point.y);
		private static Vector2 Flat(Vector3 point) => new Vector2(point.x, point.z);

		private static string Read(BrownoutHud hud, string key)
		{
			return hud != null && hud.Readout.TryGetValue(key, out string value) == true ? value : "none";
		}

		private static IEnumerator Wait(float seconds) => BrownoutHudCheck.Wait(seconds);

		private static void Screenshot(string name)
		{
			string folder = Path.Combine(BrownoutMapData.ProjectFolder, Folder);
			Directory.CreateDirectory(folder);
			ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"{name}.png"));
		}

		private static void Check(bool passed, string line)
		{
			++_checked;
			_passed += passed == true ? 1 : 0;
			Log($"{(passed ? "PASS" : "FAIL")} {line}");
		}

		private static void Log(string line)
		{
			_log.Add(line);
		}

		private static void SaveLog()
		{
			string folder = Path.Combine(BrownoutMapData.ProjectFolder, Folder);
			Directory.CreateDirectory(folder);
			File.WriteAllText(Path.Combine(folder, "MapCheck.txt"), string.Join("\n", _log));
		}

		private static void UpdateRoutine()
		{
			if (Application.isPlaying == false)
			{
				Log("Map Check stopped: Play mode ended.");
				Stop();
				return;
			}

			try
			{
				while (_routine.Count > 0)
				{
					IEnumerator top = _routine.Peek();
					if (top.MoveNext() == false)
					{
						_routine.Pop();
						continue;
					}

					if (top.Current is IEnumerator nested)
					{
						_routine.Push(nested);
						continue;
					}

					break;
				}
			}
			catch (System.Exception exception)
			{
				Log($"Map Check failed: {exception}");
				Debug.LogException(exception);
				SaveLog();
				Stop();
				return;
			}

			if (_routine.Count == 0)
			{
				Stop();
			}
		}

		private static void Stop()
		{
			_running = false;
			_routine.Clear();
			ReleaseInput();
			EditorApplication.update -= UpdateRoutine;
		}
	}
}
