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
	using UnityEngine.AI;

	/// <summary>
	/// Bot tools (phase 9a). Brownout → Setup → Build Bot Settings; Brownout → Test → Bots: spawn a bot duo ahead, a duo in every POI and
	/// cabin area, remove bots, bots ignore players, freeze bots, show the debug view, log status and events, and the Bot Check. Run the
	/// check in the Brownout scene with 2 peers during the warm-up, from the lagged client (switch to the client first): the NavMesh
	/// connects the map over the bridges, a bot walks POI to POI over a bridge and through a door, loots a gun and ammo, sees and fires,
	/// doesn't see through a closed door, turns toward a shot, hits at 10 / 30 / 75 m, takes cover when shot, heals, follows its partner,
	/// and 22 bots' cost is measured. Phase 9b adds a lone player's bot teammate walking to its player, then a whole match with bots
	/// (placement at match start, teammates, looting, cost with and without thinning, the first close, played out to the victory; it ends
	/// the match, so restart Play afterwards). About 14 minutes. Read GetBotCheckLog() or Logs/BotCheck/BotCheck.txt; pictures in Logs/BotCheck/.
	/// </summary>
	public static class BrownoutBotTools
	{
		// CONSTANTS

		public  const string SettingsPath = "Assets/Brownout/Resources/Settings/BotSettings.asset";
		private const string MENU   = "Brownout/Test/Bots/";
		public  const string Folder = "Logs/BotCheck";

		// MENU

		[MenuItem("Brownout/Setup/Build Bot Settings", priority = 9)]
		private static void SettingsMenu() => Debug.Log(BuildBotSettings());

		[MenuItem(MENU + "Bot Check", priority = 1)]
		private static void CheckMenu() => Debug.Log(RunBotCheck());

		[MenuItem(MENU + "Spawn Bot Duo Ahead", priority = 20)]
		private static void SpawnAheadMenu() => Debug.Log(SpawnBotDuoAhead(25.0f));

		[MenuItem(MENU + "Spawn A Duo In Every Area", priority = 21)]
		private static void SpawnAllMenu() => Debug.Log(SpawnBotDuosInAreas(11));

		[MenuItem(MENU + "Remove Bots", priority = 22)]
		private static void RemoveMenu() => Debug.Log(RemoveBots());

		[MenuItem(MENU + "Bots Ignore Players", priority = 40)]
		private static void IgnoreMenu() { BotDebug.IgnorePlayers = !BotDebug.IgnorePlayers; Debug.Log($"Bots ignore players: {BotDebug.IgnorePlayers}"); }

		[MenuItem(MENU + "Freeze Bots", priority = 41)]
		private static void FreezeMenu() { BotDebug.Freeze = !BotDebug.Freeze; Debug.Log($"Bots frozen: {BotDebug.Freeze}"); }

		[MenuItem(MENU + "Show Bot Debug", priority = 42)]
		private static void ShowMenu() { BotDebug.ShowDebug = !BotDebug.ShowDebug; Debug.Log($"Bot debug view (Scene view gizmos): {BotDebug.ShowDebug}"); }

		[MenuItem(MENU + "Bots Ignore Players", true)]
		private static bool IgnoreValidate() { UnityEditor.Menu.SetChecked(MENU + "Bots Ignore Players", BotDebug.IgnorePlayers); return true; }

		[MenuItem(MENU + "Freeze Bots", true)]
		private static bool FreezeValidate() { UnityEditor.Menu.SetChecked(MENU + "Freeze Bots", BotDebug.Freeze); return true; }

		[MenuItem(MENU + "Show Bot Debug", true)]
		private static bool ShowValidate() { UnityEditor.Menu.SetChecked(MENU + "Show Bot Debug", BotDebug.ShowDebug); return true; }

		[MenuItem(MENU + "Split Players Into Solo Teams", priority = 43)]
		private static void SoloMenu() => Debug.Log(SetSoloTeams(!GameplayMode.DebugSoloTeams));

		[MenuItem(MENU + "Split Players Into Solo Teams", true)]
		private static bool SoloValidate() { UnityEditor.Menu.SetChecked(MENU + "Split Players Into Solo Teams", GameplayMode.DebugSoloTeams); return true; }

		[MenuItem(MENU + "No Animation Thinning", priority = 44)]
		private static void ThinMenu() { BotDebug.NoThinning = !BotDebug.NoThinning; Debug.Log($"Distant bots' pose updated every tick: {BotDebug.NoThinning}"); }

		[MenuItem(MENU + "No Animation Thinning", true)]
		private static bool ThinValidate() { UnityEditor.Menu.SetChecked(MENU + "No Animation Thinning", BotDebug.NoThinning); return true; }

		[MenuItem(MENU + "Log Bot Status", priority = 60)]
		private static void StatusMenu() => Debug.Log(GetBotStatus());

		[MenuItem(MENU + "Log Bot Events", priority = 61)]
		private static void EventsMenu() => Debug.Log(GetBotEvents(60));

		// SETTINGS

		public static string BuildBotSettings()
		{
			var settings = AssetDatabase.LoadAssetAtPath<BotSettings>(SettingsPath);
			string result = string.Empty;

			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<BotSettings>();
				AssetDatabase.CreateAsset(settings, SettingsPath);
				result += $"Created {SettingsPath}. ";
			}

			string globalPath = AssetDatabase.FindAssets("t:GlobalSettings").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
			var    global     = globalPath != null ? AssetDatabase.LoadAssetAtPath<GlobalSettings>(globalPath) : null;
			if (global == null)
				return result + "GlobalSettings asset not found.";

			if (global.Bots != settings)
			{
				global.Bots = settings;
				EditorUtility.SetDirty(global);
				result += $"Linked BotSettings from {globalPath}. ";
			}

			AssetDatabase.SaveAssets();
			return result + "Bot settings ready.";
		}

		// TOOLS

		/// <summary>Server: a bot duo on the NavMesh this far in front of the observed player, facing it.</summary>
		public static string SpawnBotDuoAhead(float distance)
		{
			Agent agent = ObservedServerAgent();
			if (agent == null)
				return "Need a player in Play mode.";

			Vector3 forward = agent.transform.forward;
			forward.y = 0.0f;
			forward.Normalize();

			Vector3 point = agent.transform.position + forward * distance;
			BotNavigation.Sample(point, 10.0f, out point);

			var spawned = new List<Agent>();
			agent.Context.GameplayMode.SpawnBotDuo(point, Quaternion.LookRotation(-forward), spawned);
			return $"Spawned {string.Join(", ", spawned.Select(t => t.name))} {distance:F0} m ahead.";
		}

		/// <summary>Server: bot duos spread over the POIs and cabin areas (the bigger POIs get a second duo), up to count duos.</summary>
		public static string SpawnBotDuosInAreas(int count)
		{
			Agent agent = ObservedServerAgent();
			BrownoutMap map = agent != null ? BrownoutMap.Get(agent.gameObject.scene) : null;
			if (agent == null || map == null)
				return "Need a player on the Brownout map (Play mode).";

			GameplayMode mode    = agent.Context.GameplayMode;
			var          areas   = map.Areas.OrderBy(a => a.IsCabins).ThenByDescending(a => a.Size.x * a.Size.y).ToList();
			var          spawned = new List<Agent>();

			for (int i = 0; i < count; ++i)
			{
				BrownoutMap.Area area = areas[i % areas.Count];
				Vector2 offset = i < areas.Count ? Vector2.zero : new Vector2(area.Size.x * 0.25f, -area.Size.y * 0.25f);
				Vector3 onMesh;
				if (offset == Vector2.zero ? AreaPoint(agent.Runner, area, out onMesh) == false : FindOpenPoint(Ground(agent.Runner, new Vector3(area.Center.x + offset.x, 0.0f, area.Center.y + offset.y)), 30.0f, out onMesh) == false)
					continue;

				mode.SpawnBotDuo(onMesh, Quaternion.Euler(0.0f, Random.Range(0.0f, 360.0f), 0.0f), spawned);
			}

			return $"Spawned {spawned.Count} bots in {Mathf.Min(count, areas.Count)} areas.";
		}

		/// <summary>Server: every player alone on a team (or back to duos in join order), for the lone player's bot teammate. Before the match.</summary>
		public static string SetSoloTeams(bool solo)
		{
			GameplayMode.DebugSoloTeams = solo;

			Agent agent = ObservedServerAgent();
			if (agent == null)
				return $"Solo teams {(solo ? "on" : "off")}; takes effect for players who join from now (no player in Play mode).";

			agent.Context.GameplayMode.ReassignPlayerTeams();
			var teams = agent.Runner.ActivePlayers.Select(p => $"{p}: team {agent.Context.GameplayMode.GetPlayerTeam(p)}");
			return $"Solo teams {(solo ? "on" : "off")}: {string.Join(", ", teams)}. At match start, a team with one player gets a bot teammate.";
		}

		public static string RemoveBots()
		{
			Agent agent = ObservedServerAgent();
			if (agent == null)
				return "Need a player in Play mode.";

			int removed = agent.Context.GameplayMode.RemoveDummies();
			return $"Removed {removed} bots and dummies.";
		}

		public static string GetBotStatus()
		{
			var lines = new List<string>();
			foreach (BotBrain bot in BotBrain.All)
			{
				Agent a = bot.Agent;
				var gun = a.Weapons.CurrentWeapon as BulletWeapon;
				Vector2 file = BrownoutMapData.ToFile(new Vector2(a.transform.position.x, a.transform.position.z));
				lines.Add($"{a.name,-14} team {a.Team,3} {bot.State,-6} skill {bot.SkillValue:F2} hp {a.Health.CurrentHealth,3:F0} armor T{a.Health.ArmorTier} {a.Health.ArmorLeft,3:F0} gun {(gun != null ? $"{gun.GunData.GunName} {gun.MagazineAmmo}/{gun.WeaponAmmo}" : "none")} at E{file.x:F0} N{file.y:F0}: {bot.Goal}");
			}

			return lines.Count > 0 ? string.Join("\n", lines) : "No bots.";
		}

		public static string GetBotEvents(int last)
		{
			return string.Join("\n", BotDebug.EventLog.Skip(Mathf.Max(0, BotDebug.EventLog.Count - last)));
		}

		// THE CHECK

		private static readonly List<string>       _log     = new List<string>(128);
		private static readonly Stack<IEnumerator> _routine = new Stack<IEnumerator>();
		private static bool _running;
		private static int  _passed;
		private static int  _checked;
		private static int  _repeat = 1;
		private static HashSet<string> _only;

		private static bool Runs(string section) => _only == null || _only.Contains(section);

		/// <summary>The whole check, or only some sections ("navmesh,walk,door,loot,sight,bush,hits,cover,follow,performance,teammate,match"), each run <paramref name="repeat"/> times.</summary>
		public static string RunBotCheck(string only = null, int repeat = 1)
		{
			if (Application.isPlaying == false)
				return "Enter Play mode first (the Brownout scene, 2 peers).";
			if (_running == true)
				return "The Bot Check is already running.";

			_only   = string.IsNullOrWhiteSpace(only) == true ? null : new HashSet<string>(only.ToLowerInvariant().Split(',').Select(t => t.Trim()));
			_repeat = Mathf.Max(1, repeat);

			_log.Clear();
			_routine.Clear();
			_routine.Push(CheckRoutine());
			_running = true;

			EditorApplication.update -= UpdateRoutine;
			EditorApplication.update += UpdateRoutine;

			return "Bot Check started (about 6 minutes). Read GetBotCheckLog().";
		}

		public static string GetBotCheckLog()
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
			Agent         player   = BrownoutHudCheck.ServerCopy(localRef);
			NetworkRunner server   = player != null ? player.Runner : null;
			BrownoutMap   map      = player != null ? BrownoutMap.Get(player.gameObject.scene) : null;
			GameplayMode  mode     = player != null ? player.Context.GameplayMode : null;

			if (player == null || map == null || mode == null)
			{
				Log("No Brownout map on the host (open Assets/Scenes/Brownout.unity and press Play).");
				yield break;
			}

			_passed  = 0;
			_checked = 0;
			BotDebug.Clear();
			BotDebug.IgnorePlayers = false;
			BotDebug.Freeze        = false;
			ReleaseInput();
			mode.RemoveDummies();
			Health.DebugGodMode = true;

			Log($"Bot Check: viewed from {(client.IsServer == false ? "the client (simulated lag in play)" : "the host")}, mode {mode.Type}. Players can't be hurt (god mode) while it runs.");

			try
			{
				for (int run = 0; run < _repeat; ++run)
				{
					if (_repeat > 1)
					{
						Log($"-- Run {run + 1} of {_repeat}");
					}

					// 1. The NavMesh
					if (Runs("navmesh")) yield return CheckNavMesh(server, map);

					// 2. A walk from one POI to another over a bridge, 3. through a door
					if (Runs("walk")) yield return CheckWalk(player, map, mode);
					if (Runs("door")) yield return CheckDoor(player, map, mode);

					// 4. Looting
					if (Runs("loot")) yield return CheckLoot(player, map, mode);

					// 5. Sight, closed doors, bushes, hearing
					if (Runs("sight")) yield return CheckSight(player, map, mode);
					if (Runs("bush"))  yield return CheckBush(player, mode);

					// 6. Hit rates
					if (Runs("hits")) yield return CheckHitRates(player, map, mode);

					// 7. Cover and healing
					if (Runs("cover")) yield return CheckCover(player, map, mode);

					// 8. A duo's partner follows
					if (Runs("follow")) yield return CheckFollow(player, map, mode);

					// 9. 22 bots
					if (Runs("performance")) yield return CheckPerformance(player, map, mode);
				}

				// 10. A lone player's bot teammate walks to its player
				if (Runs("teammate")) yield return CheckTeammate(player, mode);

				// 11. A whole match with bots (phase 9b); it ends the match, so it runs last and once
				if (Runs("match")) yield return CheckMatch(hud, player, map, mode);
			}
			finally
			{
				GameplayMode.DebugSoloTeams  = false;
				BotDebug.NoThinning          = false;
				MatchSettings.DebugTimeScale = 1.0f;
				Health.DebugGodMode        = false;
				BotDebug.IgnorePlayers     = false;
				GunplayDebug.ZeroSpread    = false;
				BotDebug.HoldStill         = false;
				GunplayDebug.NoRecoil      = false;
				AgentMovement.DebugStancePlayer = -1;
				ReleaseInput();
			}

			mode.RemoveDummies();

			Log($"Bot Check done: {_passed} of {_checked} passed.");
			SaveLog();
			Debug.Log($"[BotCheck] DONE {_passed} of {_checked} passed (Logs/BotCheck/BotCheck.txt)");
		}

		// 1
		private static IEnumerator CheckNavMesh(NetworkRunner server, BrownoutMap map)
		{
			NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
			Check(triangulation.indices.Length > 1000, $"The map's NavMesh is loaded: {triangulation.indices.Length / 3} triangles");

			var points = new List<(string Name, Vector3 Point)>();
			foreach (BrownoutMap.Area area in map.Areas)
			{
				if (AreaPoint(server, area, out Vector3 point) == true)
				{
					points.Add((area.Name, point));
				}
			}

			var path = new NavMeshPath();
			var corners = new Vector3[512];
			int complete = 0, pairs = 0, crossings = 0, offBridge = 0;
			List<Vector3> decks = BridgeDecks(server);

			for (int i = 0; i < points.Count; ++i)
			{
				for (int j = i + 1; j < points.Count; ++j)
				{
					++pairs;
					if (BotNavigation.CanReach(points[i].Point, points[j].Point, path) == false)
						continue;

					++complete;
					int count = path.GetCornersNonAlloc(corners);
					for (int k = 0; k < count - 1; ++k)
					{
						if (CrossesRiver(map, corners[k], corners[k + 1], out Vector3 at) == true)
						{
							++crossings;
							if (decks.Any(d => Flat(d - at).magnitude < 30.0f) == false)
							{
								++offBridge;
							}
						}
					}
				}
			}

			Check(points.Count == map.Areas.Count && complete == pairs, $"Every POI and cabin area reaches every other on the NavMesh: {complete} of {pairs} paths ({points.Count} of {map.Areas.Count} areas on the mesh)");
			Check(crossings > 0 && offBridge == 0, $"Every river crossing on those paths is on a bridge: {crossings - offBridge} of {crossings}");
			yield return null;
		}

		// 2
		private static IEnumerator CheckWalk(Agent player, BrownoutMap map, GameplayMode mode)
		{
			NetworkRunner server = player.Runner;

			// The closest pair of areas on opposite banks
			(string A, Vector3 From, string B, Vector3 To) best = default;
			float bestDistance = float.MaxValue;
			var path = new NavMeshPath();

			foreach (BrownoutMap.Area a in map.Areas)
			{
				foreach (BrownoutMap.Area b in map.Areas)
				{
					if (a.Id == b.Id)
						continue;

					if (AreaPoint(server, a, out Vector3 f) == false || AreaPoint(server, b, out Vector3 t) == false)
						continue;
					if (SideOfRiver(map, f) == SideOfRiver(map, t))
						continue;

					float distance = Flat(f - t).magnitude;
					if (distance < bestDistance && BotNavigation.CanReach(f, t, path) == true)
					{
						bestDistance = distance;
						best = (a.Name, f, b.Name, t);
					}
				}
			}

			if (best.A == null)
			{
				Check(false, "Found two areas on opposite banks to walk between");
				yield break;
			}

			BotDebug.IgnorePlayers = true;
			Agent bot = SpawnOne(mode, best.From);
			yield return Wait(1.0f);
			bot.Bot.DebugGoTo(best.To);

			float pathLength = PathLength(best.From, best.To);
			float timeout    = pathLength / 3.0f + 30.0f;
			float start      = Time.realtimeSinceStartup;
			int   side       = SideOfRiver(map, best.From);
			bool  crossedOnBridge = false, crossedOff = false;
			List<Vector3> decks = BridgeDecks(server);
			Vector3 last = bot.transform.position;

			Log($"A bot walks from {best.A} to {best.B} ({pathLength:F0} m of path, {best.From:F0} → {best.To:F0}), up to {timeout:F0} s.");

			while (Time.realtimeSinceStartup - start < timeout && Flat(bot.transform.position - best.To).magnitude > 4.0f && bot.Health.IsAlive == true)
			{
				yield return Wait(0.5f);

				int now = SideOfRiver(map, bot.transform.position);
				if (now != side)
				{
					bool onBridge = decks.Any(d => Flat(d - bot.transform.position).magnitude < 30.0f || Flat(d - last).magnitude < 30.0f);
					crossedOnBridge |= onBridge;
					crossedOff      |= onBridge == false;
					side = now;
				}

				last = bot.transform.position;
			}

			float taken = Time.realtimeSinceStartup - start;
			float left  = Flat(bot.transform.position - best.To).magnitude;
			int   stuck = BotDebug.EventLog.Count(l => l.StartsWith("STUCK") && l.Contains(bot.name));
			Check(left <= 4.0f, $"The bot got from {best.A} to {best.B} in {taken:F0} s ({left:F1} m from the goal at the end, {stuck} stuck moments)");
			Check(crossedOnBridge == true && crossedOff == false, $"It crossed the river on a bridge: {(crossedOnBridge ? "yes" : "no")}{(crossedOff ? ", and once off a bridge" : string.Empty)}");
			Screenshot("01_walk_end");

			mode.RemoveDummies();
			BotDebug.IgnorePlayers = false;
		}

		// 3
		private static IEnumerator CheckDoor(Agent player, BrownoutMap map, GameplayMode mode)
		{
			NetworkRunner server = player.Runner;
			MapState state = MapState.Get(player.gameObject.scene);

			// A cabin door with open ground outside and a floor inside
			Door door = null;
			Vector3 outside = default, inside = default;
			foreach (Door candidate in map.Doors)
			{
				if (candidate == null || candidate.IsPower == true || candidate.Area < 0 || candidate.Area >= map.Areas.Count || map.Areas[candidate.Area].IsCabins == false)
					continue;

				Vector3 middle = candidate.transform.position + candidate.transform.right * 0.375f;
				Vector3 a = middle + candidate.Normal * 3.0f;
				Vector3 b = middle - candidate.Normal * 3.0f;
				bool aRoofed = IsRoofed(server, a), bRoofed = IsRoofed(server, b);
				if (aRoofed == bRoofed)
					continue;

				Vector3 o = aRoofed ? b : a;
				Vector3 i = aRoofed ? a : b;
				if (BotNavigation.Sample(o, 1.5f, out o) == false || BotNavigation.Sample(i, 1.5f, out i) == false)
					continue;

				door = candidate; outside = o; inside = i;
				break;
			}

			if (door == null)
			{
				Check(false, "Found a cabin door with ground on the mesh on both sides");
				yield break;
			}

			state.CloseAllDoors();
			yield return Wait(0.5f);

			BotDebug.IgnorePlayers = true;
			Agent bot = SpawnOne(mode, outside);
			bot.Bot.DebugGoTo(outside);
			yield return Wait(1.0f);
			state.CloseAllDoors();
			yield return Wait(0.3f);
			Log($"Door check at {map.Areas[door.Area].Name}: the bot starts {Flat(bot.transform.position - inside).magnitude:F1} m from the spot inside, the door is {(state.IsDoorOpen(door.Index) ? "open" : "shut")}.");
			bot.Bot.DebugGoTo(inside);

			bool opened = false;
			float start = Time.realtimeSinceStartup;
			while (Time.realtimeSinceStartup - start < 25.0f && Flat(bot.transform.position - inside).magnitude > 1.2f)
			{
				yield return Wait(0.25f);
				opened |= state.IsDoorOpen(door.Index);
			}

			opened |= state.IsDoorOpen(door.Index);
			float left = Flat(bot.transform.position - inside).magnitude;
			Check(opened == true && left <= 1.2f, $"A bot walks into a closed cabin door, it opens, and the bot gets inside ({left:F1} m from the spot inside, after {Time.realtimeSinceStartup - start:F0} s, door {(opened ? "opened" : "stayed shut")})");

			mode.RemoveDummies();
			BotDebug.IgnorePlayers = false;
		}

		// 4
		private static IEnumerator CheckLoot(Agent player, BrownoutMap map, GameplayMode mode)
		{
			NetworkRunner server = player.Runner;
			BrownoutMap.Area area = map.Areas.Where(a => a.IsCabins == false).OrderByDescending(a => a.Size.x * a.Size.y).First();

			if (AreaPoint(server, area, out Vector3 point) == false)
			{
				Check(false, $"Found ground in {area.Name}");
				yield break;
			}

			BotDebug.IgnorePlayers = true;
			Agent bot = SpawnOne(mode, point);
			Log($"A bot loots {area.Name}, up to 90 s.");

			float start = Time.realtimeSinceStartup;
			float gunTime = -1.0f, ammoTime = -1.0f;
			while (Time.realtimeSinceStartup - start < 90.0f)
			{
				yield return Wait(1.0f);

				bool hasGun  = HasGun(bot, out int ammo);
				if (hasGun == true && gunTime < 0.0f) { gunTime = Time.realtimeSinceStartup - start; }
				if (hasGun == true && ammo > 0 && ammoTime < 0.0f) { ammoTime = Time.realtimeSinceStartup - start; }

				if (ammoTime >= 0.0f && bot.Health.ArmorTier > 0 && CountHeals(bot) > 0)
					break;
			}

			HasGun(bot, out int rounds);
			int loots = BotDebug.EventLog.Count(l => l.StartsWith("LOOT") && l.Contains(bot.name) && l.Contains(" took "));
			Check(gunTime >= 0.0f && ammoTime >= 0.0f, $"The bot found a gun after {gunTime:F0} s and spare ammo after {ammoTime:F0} s ({loots} items taken)");
			Log($"  It also has armor T{bot.Health.ArmorTier} and {CountHeals(bot)} healing items after {Time.realtimeSinceStartup - start:F0} s; {(bot.Weapons.CurrentWeapon is BulletWeapon g ? $"{g.GunData.GunName} {g.MagazineAmmo}/{g.WeaponAmmo}" : "no gun in hand")}");
			Screenshot("02_looting");

			mode.RemoveDummies();
			BotDebug.IgnorePlayers = false;
		}

		// 5
		private static IEnumerator CheckSight(Agent player, BrownoutMap map, GameplayMode mode)
		{
			NetworkRunner server = player.Runner;

			if (FindOpenLine(server, 30.0f, out Vector3 a, out Vector3 b) == false)
			{
				Check(false, "Found 30 m of open ground for the sight check");
				yield break;
			}

			// In the open at 30 m: seen, then fired at
			Move(player, b + Vector3.up * 0.05f);
			BrownoutTestTools.AimObservedAt(a + Vector3.up * 1.5f);
			Agent bot = SpawnOne(mode, a, b);
			GiveGun(bot, "Icarus");
			BotDebug.Clear();
			yield return Wait(4.0f);

			string see  = BotDebug.EventLog.FirstOrDefault(l => l.StartsWith("SEE") && l.Contains(bot.name));
			string fire = BotDebug.EventLog.FirstOrDefault(l => l.StartsWith("FIRE") && l.Contains(bot.name));
			float reaction = see != null && fire != null ? EventTime(fire) - EventTime(see) : -1.0f;
			Check(see != null && fire != null, $"A bot sees the player 30 m away in the open and fires ({(reaction >= 0.0f ? $"first shot {reaction:F2} s after seeing, its reaction time is {bot.Bot.Skill.Reaction:F2} s" : "no shot")})");
			Screenshot("03_bot_fires");
			mode.RemoveDummies();
			yield return Wait(0.5f);

			// Behind a closed door: not seen; open it: seen
			MapState state = MapState.Get(player.gameObject.scene);
			Door door = null;
			Vector3 inside = default, outside = default;
			foreach (Door candidate in map.Doors)
			{
				if (candidate == null || candidate.IsPower == true)
					continue;

				Vector3 middle = candidate.transform.position + candidate.transform.right * 0.375f;
				Vector3 p = middle + candidate.Normal * 2.5f, q = middle - candidate.Normal * 3.5f;
				if (IsRoofed(server, p) == IsRoofed(server, q))
					continue;
				if (BotNavigation.Sample(p, 1.0f, out p) == false || BotNavigation.Sample(q, 1.0f, out q) == false)
					continue;

				door = candidate; inside = IsRoofed(server, p) ? p : q; outside = IsRoofed(server, p) ? q : p;
				break;
			}

			if (door != null)
			{
				state.CloseAllDoors();
				Move(player, outside + Vector3.up * 0.05f);
				BrownoutTestTools.AimObservedAt(inside + Vector3.up * 1.5f);
				bot = SpawnOne(mode, inside, outside);
				GiveGun(bot, "Icarus");
				bot.Bot.DebugGoTo(inside);
				BotDebug.Clear();
				yield return Wait(3.0f);

				bool seenShut = BotDebug.EventLog.Any(l => l.StartsWith("SEE") && l.Contains(bot.name));
				state.OpenDoor(door, outside, "Bot Check");
				yield return Wait(3.0f);
				bool seenOpen = BotDebug.EventLog.Any(l => l.StartsWith("SEE") && l.Contains(bot.name));
				Check(seenShut == false && seenOpen == true, $"Behind a closed door the player isn't seen ({(seenShut ? "seen" : "hidden")}); with it open, seen ({(seenOpen ? "seen" : "not seen")})");
				mode.RemoveDummies();
				state.CloseAllDoors();
			}
			else
			{
				Check(false, "Found a door for the closed-door sight check");
			}

			// A shot behind it: it turns and sees the shooter
			Move(player, b + Vector3.up * 0.05f);
			Vector3 away = (a - b).normalized;
			bot = SpawnOne(mode, a, a + away * 10.0f);
			GiveGun(bot, "Icarus");
			bot.Bot.DebugGoTo(a);
			Log(BrownoutTestTools.GiveGun("Icarus"));
			yield return Wait(2.0f);
			BotDebug.Clear();
			BrownoutTestTools.AimObservedAt(b + Vector3.up * 30.0f);
			yield return Wait(0.3f);
			AgentInput.DebugHoldAttack = true;
			yield return Wait(0.3f);
			AgentInput.DebugHoldAttack = false;
			yield return Wait(3.0f);
			bool turned = BotDebug.EventLog.Any(l => l.StartsWith("SEE") && l.Contains(bot.name));
			Check(turned == true, $"A bot facing away turns toward an unsuppressed shot 30 m behind it and sees the shooter: {(turned ? "yes" : "no")}");
			mode.RemoveDummies();
			ReleaseInput();
		}

		// 5b
		private static IEnumerator CheckBush(Agent player, GameplayMode mode)
		{
			NetworkRunner server = player.Runner;

			// A bush between: not seen
			if (FindBushBetween(server, out Vector3 bushBot, out Vector3 bushPlayer) == false)
			{
				Log("No open spot with a bush between found; skipped the bush sight check.");
				yield break;
			}

			// Crouched: the bushes are about 1.3 m tall, so a standing head shows over them (as it should)
			Move(player, bushPlayer + Vector3.up * 0.05f);
			AgentMovement.DebugStance       = EStance.Crouch;
			AgentMovement.DebugStancePlayer = player.Object.InputAuthority.AsIndex;
			yield return Wait(1.0f);
			Log($"  Bush sight: the player's stance is {player.Character.CharacterController.FixedData.Stance}, eye {player.Character.CharacterController.FixedData.EyeHeight:F2} m; bush at {(Vector3)_lastBush:F1} radius {_lastBush.w:F2} m.");
			Agent bot = SpawnOne(mode, bushBot, bushPlayer);
			GiveGun(bot, "Icarus");
			bot.Bot.DebugGoTo(bushBot);
			BotDebug.Clear();

			// Watch every tenth of a second, so a sighting can be explained (where both stood, which line was clear)
			string seenWhy = null;
			for (float t = 0.0f; t < 3.0f && seenWhy == null; t += 0.1f)
			{
				yield return Wait(0.1f);
				if (BotDebug.EventLog.Any(l => l.StartsWith("SEE") && l.Contains(bot.name)) == true)
				{
					seenWhy = DescribeSight(bot, player, bushBot);
				}
			}

			Check(seenWhy == null, $"A player crouched behind a bush (11 m away) isn't seen through it: {(seenWhy != null ? "seen" : "hidden")}");
			if (seenWhy != null)
			{
				Log($"  {seenWhy}");
			}

			mode.RemoveDummies();
			AgentMovement.DebugStancePlayer = -1;
			yield return Wait(0.5f);

			// Solid: walking into the bush stops the player outside it
			Vector3 bushCenter = new Vector3(_lastBush.x, bushPlayer.y, _lastBush.z);
			Vector3 approach   = bushPlayer + (bushPlayer - bushCenter).normalized * 2.0f;
			Move(player, approach + Vector3.up * 0.05f);
			BrownoutTestTools.AimObservedAt(bushCenter + Vector3.up * 1.0f);
			yield return Wait(1.0f);
			AgentInput.DebugMoveDirection = new Vector2(0.0f, 1.0f);
			float closestToBush = float.MaxValue;
			for (float t = 0.0f; t < 2.0f; t += 0.1f)
			{
				yield return Wait(0.1f);
				closestToBush = Mathf.Min(closestToBush, Flat(player.transform.position - bushCenter).magnitude);
			}
			ReleaseInput();
			Check(closestToBush > _lastBush.w - 0.1f, $"Walking into a bush stops the player at its edge: {closestToBush:F2} m from its middle (collider radius {_lastBush.w:F2} m; the player's capsule adds 0.30 m)");
		}

		/// <summary>Where a bot and a player stand and which of the bot's two sight lines (head, chest) are open, and what each one hits.</summary>
		private static string DescribeSight(Agent bot, Agent player, Vector3 placedAt)
		{
			KCCData botData    = bot.Character.CharacterController.FixedData;
			KCCData playerData = player.Character.CharacterController.FixedData;
			Vector3 eye   = botData.TargetPosition + Vector3.up * botData.EyeHeight;
			Vector3 feet  = player.transform.position;
			Vector3 head  = feet + Vector3.up * playerData.EyeHeight;
			Vector3 chest = feet + Vector3.up * (playerData.EyeHeight * 0.7f);

			return $"SEEN: bot eye {eye:F2} ({Flat(botData.TargetPosition - placedAt).magnitude:F2} m from where it was placed), player feet {feet:F2} ({playerData.Stance}, eye {playerData.EyeHeight:F2}); " +
				$"head line {(bot.Bot.IsClear(eye, head) ? "open" : "blocked")} [{DescribeLine(bot.Runner, eye, head)}], " +
				$"chest line {(bot.Bot.IsClear(eye, chest) ? "open" : "blocked")} [{DescribeLine(bot.Runner, eye, chest)}]";
		}

		private static string DescribeLine(NetworkRunner runner, Vector3 from, Vector3 to)
		{
			Vector3 offset = to - from;
			var     hits   = new RaycastHit[16];
			int     count  = runner.GetPhysicsScene().Raycast(from, offset.normalized, hits, offset.magnitude, ~0, QueryTriggerInteraction.Ignore);
			var     names  = new List<string>();
			for (int i = 0; i < count; ++i)
			{
				names.Add($"{hits[i].collider.GetType().Name} '{hits[i].collider.name}' layer {hits[i].collider.gameObject.layer} at {hits[i].distance:F1} m");
			}

			float closest = DistanceToLine(new Vector3(_lastBush.x, _lastBush.y, _lastBush.z), from, to);
			return $"{(names.Count > 0 ? string.Join(", ", names) : "no hits")}; passes {closest:F2} m from the bush middle";
		}

		private static float DistanceToLine(Vector3 point, Vector3 from, Vector3 to)
		{
			Vector3 line = to - from;
			float   t    = Mathf.Clamp01(Vector3.Dot(point - from, line) / line.sqrMagnitude);
			return (from + line * t - point).magnitude;
		}

		// 6
		private static IEnumerator CheckHitRates(Agent player, BrownoutMap map, GameplayMode mode)
		{
			NetworkRunner server = player.Runner;
			var lines = new List<string>();
			float near = -1.0f, far = -1.0f;
			bool anyNear = false;

			// The bot holds still (aim alone), at a fixed skill: medium (0) and hard (1)
			BotDebug.HoldStill = true;
			var rates = new Dictionary<string, float>();

			foreach (float distance in new[] { 10.0f, 30.0f, 75.0f })
			{
				if (FindOpenLine(server, distance, out Vector3 a, out Vector3 b) == false)
				{
					lines.Add($"{distance:F0} m: no open line found");
					continue;
				}

				foreach (float skill in new[] { 0.0f, 1.0f })
				foreach (bool strafe in new[] { false, true })
				{
					Move(player, b + Vector3.up * 0.05f);
					BrownoutTestTools.AimObservedAt(a + Vector3.up * 1.5f);
					Agent bot = SpawnOne(mode, a, b);
					bot.Bot.DebugSetSkill(skill);
					GiveGun(bot, "Icarus");
					bot.Bot.DebugGoTo(a);
					yield return Wait(1.5f);

					GunplayDebug.ShotLog.Clear();
					BotDebug.Clear();
					float start = Time.realtimeSinceStartup;
					float offsetSum = 0.0f;
					int   offsetCount = 0;
					while (Time.realtimeSinceStartup - start < 6.0f)
					{
						if (strafe == true)
						{
							AgentInput.DebugMoveDirection = new Vector2(((int)((Time.realtimeSinceStartup - start) / 0.8f) % 2 == 0) ? 1.0f : -1.0f, 0.0f);
						}
						yield return Wait(0.1f);
						if (bot.Bot != null && bot.Bot.TargetVisible == true)
						{
							offsetSum += bot.Bot.AimOffset;
							++offsetCount;
						}
					}
					ReleaseInput();

					int shots = GunplayDebug.ShotLog.Count(l => l.StartsWith("FIRE"));
					int hits  = GunplayDebug.ShotLog.Count(l => l.StartsWith("HIT") && l.Contains($"target {player.name} "));
					float rate = shots > 0 ? hits / (float)shots : 0.0f;
					string level = skill < 0.5f ? "medium" : "hard";
					lines.Add($"{distance:F0} m, {level}, player {(strafe ? "strafing" : "standing")}: {hits}/{shots} hits ({rate * 100.0f:F0}%), in sight {offsetCount * 0.1f:F1} of 6 s, aim off by {(offsetCount > 0 ? offsetSum / offsetCount : 0.0f):F2}° on average");
					rates[$"{distance:F0} {level} {(strafe ? "strafe" : "stand")}"] = shots > 0 ? rate : -1.0f;

					if (distance == 10.0f && strafe == false && skill < 0.5f) { near = rate; anyNear = shots > 0; }
					if (distance == 75.0f && strafe == false && skill < 0.5f) { far = rate; }

					mode.RemoveDummies();
					yield return Wait(0.5f);
				}
			}

			BotDebug.HoldStill = false;

			foreach (string line in lines)
			{
				Log($"  Hit rate {line}");
			}

			float hardNear = rates.TryGetValue("10 hard stand", out float hardValue) == true ? hardValue : -1.0f;
			Check(anyNear == true && near > 0.2f && (far < 0.0f || far <= near), $"A medium bot hits a standing player at 10 m ({near * 100.0f:F0}%) and less at 75 m ({(far >= 0.0f ? $"{far * 100.0f:F0}%" : "n/a")})");
			Check(hardNear >= near - 0.05f, $"A hard bot hits at least as often at 10 m: {hardNear * 100.0f:F0}% against medium's {near * 100.0f:F0}%");
		}

		// 7
		private static IEnumerator CheckCover(Agent player, BrownoutMap map, GameplayMode mode)
		{
			NetworkRunner server = player.Runner;

			// Somewhere in a POI, 20 m apart in sight of each other; up to three POIs until one has cover near the bot
			bool  tookCover = false;
			float coverTime = 0.0f;
			int   tries     = 0;
			Agent bot       = null;

			foreach (BrownoutMap.Area area in map.Areas.Where(t => t.IsCabins == false).OrderBy(t => Random.value).ToList())
			{
				Vector3 center = Ground(server, new Vector3(area.Center.x, 0.0f, area.Center.y));
				if (FindOpenLine(server, 20.0f, out Vector3 a, out Vector3 b, center, 45.0f) == false)
					continue;
				if (++tries > 3)
					break;

				Log($"Cover check in {area.Name}.");

				Move(player, b + Vector3.up * 0.05f);
				bot = SpawnOne(mode, a, b);
				GiveGun(bot, "Icarus");
				bot.Health.SetArmor(3, HealthSettings.Current.GetArmorTier(3).Durability);
				yield return Wait(1.5f);

			// The player hits it (a bullet hit from the player, given on the server: this checks the bot's reaction, not the gunplay)
			BotDebug.Clear();
			var hitData = new HitData
			{
				Action        = EHitAction.Damage,
				Amount        = 30.0f,
				IsBullet      = true,
				Position      = bot.transform.position + Vector3.up * 1.3f,
				Normal        = (player.transform.position - bot.transform.position).normalized,
				Direction     = (bot.transform.position - player.transform.position).normalized,
				InstigatorRef = player.Object.InputAuthority,
				Instigator    = player.Health,
				Target        = bot.Health,
				HitType       = EHitType.Rifle,
			};
				HitUtility.ProcessHit(ref hitData);

				float start = Time.realtimeSinceStartup;
				bool hidden = false;
				Vector3 playerEye = player.transform.position + Vector3.up * 1.7f;
				while (Time.realtimeSinceStartup - start < 8.0f && hidden == false)
				{
					yield return Wait(0.25f);
					hidden = bot.Bot != null && bot.Bot.State == EBotState.Cover && bot.Bot.IsClear(playerEye, bot.transform.position + Vector3.up * 0.65f) == false;
				}

				coverTime = Time.realtimeSinceStartup - start;
				string cover = BotDebug.EventLog.FirstOrDefault(l => l.StartsWith("COVER") && l.Contains(bot.name));
				Log("  " + (cover != null ? cover.Split('|')[0].Trim() : "no COVER event"));

				if (hidden == true)
				{
					tookCover = true;
					Screenshot("04_cover");
					break;
				}

				mode.RemoveDummies();
				bot = null;
				yield return Wait(0.5f);
			}

			Check(tookCover == true, $"Shot by the player, the bot takes cover out of sight within {coverTime:F1} s ({tries} spot(s) tried)");

			if (bot == null || bot.Object == null || bot.Object.IsValid == false)
			{
				Check(false, "A bot for the healing check");
				yield break;
			}

			// Healing when hurt and nobody's around
			BotDebug.IgnorePlayers = true;
			bot.Inventory.DebugSetHealthSlot(EHealItem.MedKit);
			bot.Health.DebugSetHealth(60.0f);
			yield return Wait(8.0f);
			bool healed = HealthDebug.EventLog.Any(l => l.StartsWith("HEAL") && l.Contains(bot.name) && l.Contains("done"));
			Check(healed == true && bot.Health.CurrentHealth > 60.0f, $"Hurt (60 health) with nobody in sight, the bot heals: health {bot.Health.CurrentHealth:F0}");

			mode.RemoveDummies();
			BotDebug.IgnorePlayers = false;
		}

		// 8
		private static IEnumerator CheckFollow(Agent player, BrownoutMap map, GameplayMode mode)
		{
			NetworkRunner server = player.Runner;

			if (FindOpenLine(server, 60.0f, out Vector3 a, out Vector3 b) == false)
			{
				Check(false, "Found 60 m of open ground for the follow check");
				yield break;
			}

			BotDebug.IgnorePlayers = true;
			var duo = new List<Agent>();
			mode.SpawnBotDuo(a, Quaternion.LookRotation(Flat3(b - a)), duo);
			yield return Wait(1.0f);

			Agent leader   = duo.OrderBy(t => t.BotNumber).First();
			Agent follower = duo.OrderBy(t => t.BotNumber).Last();
			leader.Bot.DebugGoTo(b);

			// It follows, and may loot on the way while it stays within 1.5 × the follow distance of its leader (the leash). A pickup
			// locks it for a moment (gun raise, attachment fit), so the leader can get a few meters past the leash before it catches up.
			float leash  = BotSettings.Current.FollowDistance.y * 1.5f;
			float maxGap = 0.0f;
			var timeline = new List<string>();
			for (float t = 0.0f; t < 25.0f; t += 0.5f)
			{
				yield return Wait(0.5f);
				float now = Flat(leader.transform.position - follower.transform.position).magnitude;
				maxGap = Mathf.Max(maxGap, now);
				if (Mathf.Repeat(t, 2.5f) < 0.25f)
				{
					timeline.Add($"{t + 0.5f:F0} s {now:F0} m ({Flat(leader.Character.CharacterController.FixedData.RealVelocity).magnitude:F1} / {Flat(follower.Character.CharacterController.FixedData.RealVelocity).magnitude:F1} m/s)");
				}
			}
			Log($"  Follow: gap (leader / follower speed): {string.Join(", ", timeline)}");

			// Once the leader has stopped, the follower must come back inside the leash (give it a few seconds if it was mid-pickup)
			float gap = Flat(leader.transform.position - follower.transform.position).magnitude;
			for (float t = 0.0f; t < 8.0f && gap > leash + 1.0f; t += 0.5f)
			{
				yield return Wait(0.5f);
				gap = Flat(leader.transform.position - follower.transform.position).magnitude;
			}

			float leaderOff = Flat(leader.transform.position - b).magnitude;
			Check(gap <= leash + 1.0f && leaderOff < 5.0f,
				$"A duo's second bot keeps up with the first over 60 m: {gap:F1} m apart at the end (leash {leash:F0} m; widest on the way {maxGap:F1} m; {follower.Bot.State}); the leader ended {leaderOff:F1} m from its goal ({leader.Bot.State})");

			mode.RemoveDummies();
			BotDebug.IgnorePlayers = false;
		}

		// 9
		private static IEnumerator CheckPerformance(Agent player, BrownoutMap map, GameplayMode mode)
		{
			NetworkRunner server = player.Runner;

			PerformanceProbe probe = PerformanceProbe.Attach(server);
			probe.Begin();
			yield return Wait(10.0f);
			probe.End();
			float fpsBefore  = Fps(probe.FrameTimes);
			float tickBefore = Average(probe.TickTimes);

			// Back to the warm-up spawn, then 11 duos at the areas, fighting each other and the player
			Log(SpawnBotDuosInAreas(11));
			yield return Wait(15.0f);

			BotBrain.DebugTickTime  = 0;
			BotBrain.DebugTickCount = 0;
			probe.Begin();
			yield return Wait(20.0f);
			probe.End();

			float fpsAfter  = Fps(probe.FrameTimes);
			float tickAfter = Average(probe.TickTimes);
			double brainMs  = BotBrain.DebugTickTime * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			int    ticks    = Mathf.Max(1, BotBrain.DebugTickCount / Mathf.Max(1, mode.BotCount));
			float  brainPerTick = (float)(brainMs / ticks);

			int fights = BotDebug.EventLog.Count(l => l.StartsWith("FIRE"));
			Check(mode.BotCount >= 20, $"22 bots running: {mode.BotCount} alive, {fights} bursts fired in the last 35 s");
			Log($"  Frame rate {fpsBefore:F0} → {fpsAfter:F0} fps; host simulation {tickBefore:F2} → {tickAfter:F2} ms per tick; the brains take {brainPerTick:F2} ms of each tick for all bots ({brainPerTick / Mathf.Max(0.01f, tickAfter) * 100.0f:F0}% of the simulation).");
			Screenshot("05_22_bots");

			mode.RemoveDummies();
		}

		// 10
		/// <summary>Phase 9b: a bot on the player's team 150 m away (other bots ignore players, none around) walks to the player and stays with them.</summary>
		private static IEnumerator CheckTeammate(Agent player, GameplayMode mode)
		{
			NetworkRunner server = player.Runner;
			mode.RemoveDummies();

			// The player alone on a team, so the bot has only them to follow
			GameplayMode.DebugSoloTeams = true;
			mode.ReassignPlayerTeams();

			Vector3 start = default;
			bool    found = false;
			for (int i = 0; i < 40 && found == false; ++i)
			{
				float   angle = Random.Range(0.0f, Mathf.PI * 2.0f);
				Vector3 p     = Ground(server, player.transform.position + new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * 150.0f);
				found = BotNavigation.Sample(p, 4.0f, out start) == true && BotNavigation.CanReach(start, player.transform.position, new NavMeshPath(), 3.0f) == true;
			}

			if (found == false)
			{
				Check(false, "Found a spot 150 m from the player with a path to them");
				yield break;
			}

			Agent bot = mode.SpawnBot(start, Quaternion.identity, (byte)player.Team);
			yield return Wait(1.0f);

			float path    = PathLength(bot.transform.position, player.transform.position);
			float leash   = BotSettings.Current.FollowDistance.y * 1.5f + 2.0f;
			float timeout = path / 4.0f + 30.0f;
			float joined  = -1.0f;
			for (float t = 0.0f; t < timeout; t += 1.0f)
			{
				if (IsLive(bot) == true && Flat(bot.transform.position - player.transform.position).magnitude <= leash)
				{
					joined = t;
					break;
				}
				yield return Wait(1.0f);
			}

			string state = IsLive(bot) == true ? $"{bot.Bot.State}, {bot.Bot.Goal}" : "dead";
			Check(joined >= 0.0f, $"A bot on the player's team {path:F0} m away (by path) comes to them: {(joined >= 0.0f ? $"within {leash:F0} m after {joined:F0} s" : $"still {Flat(bot.transform.position - player.transform.position).magnitude:F0} m away after {timeout:F0} s")} ({state})");

			yield return Wait(6.0f);
			float gap = IsLive(bot) == true ? Flat(bot.transform.position - player.transform.position).magnitude : 999.0f;
			Check(joined >= 0.0f && gap <= leash, $"It stays with the player: {gap:F0} m away 6 s later ({(IsLive(bot) ? bot.Bot.State.ToString() : "dead")})");

			mode.RemoveDummies();
			GameplayMode.DebugSoloTeams = false;
			mode.ReassignPlayerTeams();
		}

		// 11
		/// <summary>
		/// Phase 9b: a whole match with bots. Players in solo teams (so each gets a bot teammate), in god mode and ignored by the other
		/// bots. Checks the bots placed at match start, the teammates coming to their players after landing, looting, the cost with and
		/// without thinning, the bots moving into the first close (over a bridge when needed), then plays it out on a fast zone clock:
		/// the bots go out in the zone and fights, the other player is killed, and the observed player's team wins.
		/// </summary>
		private static IEnumerator CheckMatch(BrownoutHud hud, Agent player, BrownoutMap map, GameplayMode gameplay)
		{
			var mode = gameplay as BattleRoyaleGameplayMode;
			if (mode == null || mode.Phase != EMatchPhase.Warmup)
			{
				Check(false, $"The match check starts in the Battle Royale warm-up ({(mode != null ? mode.Phase.ToString() : gameplay.Type.ToString())})");
				yield break;
			}

			NetworkRunner server   = player.Runner;
			PlayerRef     localRef = player.Object.InputAuthority;
			MatchSettings settings = MatchSettings.Current;
			BotSettings   bots     = BotSettings.Current;
			List<PlayerRef> humans = server.ActivePlayers.OrderBy(p => p.AsIndex).ToList();

			Log($"Match with bots: {humans.Count} players, each alone on a team (Split Players Into Solo Teams), in god mode; the other bots ignore them.");

			GameplayMode.DebugSoloTeams = true;
			mode.ReassignPlayerTeams();
			BotDebug.IgnorePlayers       = true;
			MatchSettings.DebugTimeScale = 1.0f;
			MatchDebug.Clear();
			ReleaseInput();

			mode.StartMatch();
			float startTime = Time.time;
			yield return Wait(3.0f);

			// Match start (phase 11f): every bot rides the Osprey with the players and jumps for its spot
			var drops       = mode.BotDrops.ToList();
			var placements  = mode.LastBotPlacements.ToList();
			var duoSpots    = placements.Where(p => p.Teammate == false).ToList();
			int teammates   = drops.Count(d => d.Teammate == true);
			int planned     = drops.Sum(d => d.Numbers.Length);
			List<Agent> all = mode.GetBots().ToList();
			Check(duoSpots.Count == settings.BotDuoCount && planned == settings.BotDuoCount * 2 + teammates,
				$"Match start: {planned} bots planned ({duoSpots.Count} duos, table {settings.BotDuoCount}; {teammates} lone players' teammates)");
			Check(all.Count == 0 && mode.BotsAboard == planned && mode.Aboard == humans.Count,
				$"The bots ride the Osprey with the players: {mode.BotsAboard} bots and {mode.Aboard} players aboard, {all.Count} bots on the ground");

			GameplayMode.DropPath path = mode.BotDropPath;
			var perArea  = duoSpots.GroupBy(p => p.Area).ToDictionary(g => g.Key, g => g.Count());
			int most     = perArea.Count > 0 ? perArea.Values.Max() : 0;
			float farthest = duoSpots.Count > 0 ? duoSpots.Max(p => path.DistanceTo(p.Position)) : 0.0f;
			Check(most <= bots.MaxDuosPerArea || perArea.Count * bots.MaxDuosPerArea < settings.BotDuoCount,
				$"Spread over {perArea.Count} areas near the path, at most {most} duos in one: {string.Join(", ", perArea.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}"))}");
			Check(farthest <= bots.DropReach + 40.0f, $"Every duo's spot is in reach of the Osprey's path: the farthest is {farthest:F0} m from it (DropReach {bots.DropReach:F0} m)");

			float closest = float.MaxValue;
			for (int i = 0; i < duoSpots.Count; ++i)
			{
				for (int j = i + 1; j < duoSpots.Count; ++j)
				{
					closest = Mathf.Min(closest, Vector3.Distance(duoSpots[i].Position, duoSpots[j].Position));
				}
			}
			Check(closest >= 4.0f, $"Each duo at its own loot spot: the closest two duos' spots are {closest:F0} m apart");

			// Everyone jumps as soon as the helicopter is over the map; the bots jump at their own points meanwhile
			bool placedAboard = false;
			for (float t = 0.0f; t < 60.0f && mode.Aboard > 0; t += 0.5f)
			{
				foreach (PlayerRef human in humans)
				{
					mode.RequestAirplaneJump(human, Vector3.zero);
				}
				placedAboard |= drops.Any(d => d.Aboard > 0 && mode.GetTeamPlace(d.Team) > 0);
				yield return Wait(0.5f);
			}

			float landTime = 0.0f;
			for (; landTime < 60.0f; landTime += 1.0f)
			{
				if (humans.All(h => BrownoutHudCheck.ServerCopy(h) is Agent a && a.Movement.IsDropping == false && a.Character.CharacterController.FixedData.IsGrounded == true))
					break;
				placedAboard |= drops.Any(d => d.Aboard > 0 && mode.GetTeamPlace(d.Team) > 0);
				yield return Wait(1.0f);
			}
			Log($"  Players landed: {string.Join(", ", humans.Select(h => BrownoutHudCheck.ServerCopy(h)).Where(a => a != null).Select(a => $"{a.name} at {a.transform.position:F0}"))}");

			// The bots: all out of the Osprey and down, each duo near its spot
			for (float t = 0.0f; t < 90.0f && (mode.BotsAboard > 0 || mode.GetBots().Any(b => b.Health.IsAlive == true && b.Movement.IsDropping == true) == true); t += 1.0f)
			{
				placedAboard |= drops.Any(d => d.Aboard > 0 && mode.GetTeamPlace(d.Team) > 0);
				yield return Wait(1.0f);
			}

			all = mode.GetBots().ToList();
			int stillUp = all.Count(b => b.Health.IsAlive == true && b.Movement.IsDropping == true);
			Check(mode.BotsAboard == 0 && all.Count == planned && stillUp == 0 && placedAboard == false,
				$"Every bot jumped and landed: {all.Count} of {planned} out, {mode.BotsAboard} still aboard, {stillUp} still in the air; {(placedAboard ? "a team aboard was counted out" : "no team aboard was counted out")}");

			var misses = new List<(string Name, float Miss)>();
			foreach (GameplayMode.BotDrop drop in drops.Where(d => d.Teammate == false))
			{
				foreach (Agent bot in all.Where(b => b.Team == drop.Team && b.Health.IsAlive == true && b.Bot != null))
				{
					misses.Add((bot.BotName, Flat(bot.transform.position - drop.Target).magnitude));
				}
			}
			float worst = misses.Count > 0 ? misses.Max(m => m.Miss) : 0.0f;
			Check(misses.Count > 0 && worst <= 40.0f,
				$"The duos landed on their spots: {misses.Count(m => m.Miss <= 40.0f)} of {misses.Count} within 40 m, worst {worst:F0} m, median {(misses.Count > 0 ? misses.OrderBy(m => m.Miss).ElementAt(misses.Count / 2).Miss : 0.0f):F0} m");
			Log($"  Bot drops: {BotDebug.EventLog.Count(l => l.StartsWith("DROP"))} DROP, {BotDebug.EventLog.Count(l => l.StartsWith("LANDED"))} LANDED lines");

			var teammateOf = new Dictionary<PlayerRef, Agent>();
			foreach (PlayerRef human in humans)
			{
				int team = mode.GetPlayerTeam(human);
				Agent mate = all.FirstOrDefault(b => b.Team == team);
				if (mate != null)
				{
					teammateOf[human] = mate;
				}
			}
			Check(teammateOf.Count == humans.Count && teammates == humans.Count,
				$"Each player alone on a team gets a bot teammate that jumps after them: {string.Join(", ", teammateOf.Select(p => $"{p.Key} with {p.Value.BotName}, {(BrownoutHudCheck.ServerCopy(p.Key) is Agent h ? Flat(h.transform.position - p.Value.transform.position).magnitude : -1.0f):F0} m from them on landing"))}");

			var mateNames = teammateOf.ToDictionary(p => p.Key, p => p.Value.BotName);

			// The cost in a real match, most bots still in: with and without the thinning
			yield return MeasureMatch(server, mode);

			// The teammates come to their players (who stand still)
			var startGap = new Dictionary<PlayerRef, float>();
			float longest = 0.0f;
			foreach (var pair in teammateOf)
			{
				Agent human = BrownoutHudCheck.ServerCopy(pair.Key);
				if (human == null)
					continue;

				float length = PathLength(pair.Value.transform.position, human.transform.position);
				startGap[pair.Key] = length;
				longest = Mathf.Max(longest, length);
			}

			float leash   = bots.FollowDistance.y * 1.5f + 2.0f;
			float timeout = longest / 4.5f + 40.0f;
			float joined  = -1.0f;
			for (float t = 0.0f; t < timeout; t += 2.0f)
			{
				// A teammate killed on the way (by other bots) isn't counted; the living ones must all be with their players
				var living = teammateOf.Where(p => IsLive(p.Value) == true).ToList();
				bool all1 = living.Count > 0 && living.All(p => BrownoutHudCheck.ServerCopy(p.Key) is Agent h && Flat(h.transform.position - p.Value.transform.position).magnitude <= leash);
				if (all1 == true)
				{
					joined = t;
					break;
				}
				yield return Wait(2.0f);
			}

			string gaps = string.Join(", ", teammateOf.Select(p => BrownoutHudCheck.ServerCopy(p.Key) is Agent h && IsLive(p.Value) == true ? $"{mateNames[p.Key]} {Flat(h.transform.position - p.Value.transform.position).magnitude:F0} m from {h.name} (path {startGap.GetValueOrDefault(p.Key):F0} m at landing; {p.Value.Bot.State})" : $"{mateNames[p.Key]} killed on the way"));
			// Whether a teammate survives a walk across the map is up to the match (the warm-up's teammate section tests the walk itself)
			int livingMates = teammateOf.Count(p => IsLive(p.Value) == true);
			if (livingMates > 0)
			{
				Check(joined >= 0.0f, joined >= 0.0f ? $"The teammates still alive came to their players after landing within {joined:F0} s: {gaps}" : $"The teammates still alive came to their players within {timeout:F0} s: {gaps}");
			}
			else
			{
				Log($"  Teammates after landing: {gaps} (no check: both were killed by other bots on the way)");
			}

			// Looting by now
			all = mode.GetBots().Where(b => b.Health.IsAlive == true).ToList();
			int armed = all.Count(b => HasGun(b, out _) == true);
			int loaded = all.Count(b => HasGun(b, out int ammo) == true && ammo > 0);
			int armored = all.Count(b => b.Health.ArmorTier > 0);
			int healing = all.Count(b => CountHeals(b) > 0);
			Check(armed >= Mathf.CeilToInt(all.Count * 0.75f), $"Into the match {Time.time - startTime:F0} s, {armed} of {all.Count} bots alive have a gun ({loaded} with ammo left after their fights, {armored} armor, {healing} healing)");

			// The first close: bots outside it go in, over a bridge when the circle is across the river
			ShrinkingArea zone = mode.ShrinkingArea;
			if (zone == null || zone.IsScheduled == false)
			{
				Check(false, "The zone is running");
			}
			else
			{
				if (zone.IsShrinking == false)
				{
					zone.SkipStep();   // The wait ends: Close 1 starts now
				}
				yield return Wait(0.5f);

				Vector3 center = zone.ShrinkCenter;
				float   radius = zone.ShrinkRadius;
				int     circleSide = SideOfRiver(map, center);
				var outside = mode.GetBots().Where(b => b.Health.IsAlive == true && Flat(b.transform.position - center).magnitude > radius).ToList();
				var acrossRiver = outside.Where(b => SideOfRiver(map, b.transform.position) != circleSide).ToList();
				var reached = new HashSet<Agent>();
				var crossed = new HashSet<Agent>();
				Log($"  Close 1: {zone.Radius:F0} m → {radius:F0} m on {center:F0}; {outside.Count} bots outside the new circle, {acrossRiver.Count} of them across the river from it.");

				for (float t = 0.0f; t < 150.0f; t += 2.0f)
				{
					foreach (Agent bot in outside)
					{
						if (IsLive(bot) == false)
							continue;
						if (Flat(bot.transform.position - center).magnitude <= Mathf.Max(radius, zone.Radius))
						{
							reached.Add(bot);
						}
						if (acrossRiver.Contains(bot) == true && SideOfRiver(map, bot.transform.position) == circleSide)
						{
							crossed.Add(bot);
						}
					}

					if (zone.IsShrinking == false && t > 10.0f)
						break;

					yield return Wait(2.0f);
				}

				// Bots that died before getting in don't count against it (fights, mostly); of the rest, three in four must be in
				int stillOut = outside.Count(b => reached.Contains(b) == false && IsLive(b) == true);
				int died     = outside.Count - reached.Count - stillOut;
				Check(reached.Count >= Mathf.CeilToInt((reached.Count + stillOut) * 0.75f),
					$"Bots outside the new circle moved into it by the end of the close: {reached.Count} in, {stillOut} still outside, {died} died on the way");
				Check(acrossRiver.Count == 0 || crossed.Count > 0, $"Bots across the river from the circle crossed over a bridge: {crossed.Count} of {acrossRiver.Count}");
				Log($"  Zone events: {BotDebug.EventLog.Count(l => l.StartsWith("ZONE"))} ZONE, {BotDebug.EventLog.Count(l => l.StartsWith("STUCK"))} STUCK, {BotDebug.EventLog.Count(l => l.StartsWith("DEATH"))} DEATH lines so far");
			}

			// To the end on a fast zone clock: the bots go out in the zone and fights; the god-mode players stay
			MatchSettings.DebugTimeScale = 8.0f;
			if (zone != null && zone.IsScheduled == true && zone.IsShrinking == false)
			{
				zone.SkipStep();   // Next step starts on the fast clock
			}

			bool sawBotInFeed = false;
			for (float t = 0.0f; t < 240.0f; t += 1.0f)
			{
				sawBotInFeed |= Object.FindObjectsByType<BrownoutHud>(FindObjectsSortMode.None).Any(h => h.Readout.TryGetValue("killfeed.top", out string top) == true && top.Contains("BOT") == true);
				if (mode.GetBots().All(b => b.Health.IsAlive == false || b.Team == mode.GetPlayerTeam(localRef)) == true && t > 3.0f)
					break;
				yield return Wait(1.0f);
			}

			int botTeamsLeft = mode.GetBots().Where(b => b.Health.IsAlive == true).Select(b => b.Team).Distinct().Count();
			Log($"  Fast zone done: {botTeamsLeft} bot teams still in, teams alive {mode.TeamsAlive}, zone step {(zone != null ? zone.ScheduleStep + 1 : 0)}");

			// The other player goes out, so the observed player's team is the last one
			foreach (PlayerRef other in humans.Where(h => h != localRef))
			{
				Health.DebugGodMode = false;
				foreach (Agent mate in mode.GetBots().Where(b => b.Team == mode.GetPlayerTeam(other)).ToList())
				{
					BrownoutMatchTools.Kill(mate);
				}
				BrownoutMatchTools.Kill(BrownoutHudCheck.ServerCopy(other));
				Health.DebugGodMode = true;
			}

			// Any bots still in (other than the observed player's teammate) go out the same way
			foreach (Agent bot in mode.GetBots().Where(b => b.Health.IsAlive == true && b.Team != mode.GetPlayerTeam(localRef)).ToList())
			{
				BrownoutMatchTools.Kill(bot);
			}

			for (float t = 0.0f; t < 10.0f && mode.Phase != EMatchPhase.Ended; t += 0.5f)
			{
				sawBotInFeed |= Object.FindObjectsByType<BrownoutHud>(FindObjectsSortMode.None).Any(h => h.Readout.TryGetValue("killfeed.top", out string top) == true && top.Contains("BOT") == true);
				yield return Wait(0.5f);
			}

			int localTeam = mode.GetPlayerTeam(localRef);
			Check(mode.Phase == EMatchPhase.Ended && mode.WinningTeam == localTeam, $"The match ends: {mode.Phase}, winner team {mode.WinningTeam} (the observed player's team {localTeam})");

			var botTeams = placements.Select(p => p.Team).Where(t => t != localTeam).Distinct().ToList();
			var places   = botTeams.Select(t => mode.GetTeamPlace(t)).ToList();
			int playerPlace = humans.Where(h => h != localRef).Select(h => mode.GetTeamPlace(mode.GetPlayerTeam(h))).FirstOrDefault();
			Check(places.All(p => p >= 2) && playerPlace >= 2, $"Every bot team was placed as it went out: #{string.Join(", #", places.OrderBy(p => p))}; the other player's team #{playerPlace}");
			Check(sawBotInFeed == true, $"The kill feed names bots (with the BOT mark): {(sawBotInFeed ? "seen" : "not seen")}");

			yield return Wait(1.0f);
			string end = Object.FindObjectsByType<BrownoutHud>(FindObjectsSortMode.None).Select(h => h.Readout.TryGetValue("end", out string e) ? e : null).FirstOrDefault(e => e != null && e != "none") ?? "none";
			Log($"  End banner: {end}");
			Screenshot("06_match_end");

			Log($"  Match events: {string.Join(" | ", MatchDebug.EventLog.Where(l => l.StartsWith("BOTS") || l.StartsWith("TEAMMATE") || l.StartsWith("VICTORY")).Take(20))}");
			Log("  The match has ended: restart Play before running anything else.");

			GameplayMode.DebugSoloTeams  = false;
			MatchSettings.DebugTimeScale = 1.0f;
			BotDebug.IgnorePlayers       = false;
		}

		/// <summary>Alive, and still spawned (a dead bot's body is removed after BotSettings.BodyTime).</summary>
		private static bool IsLive(Agent agent) => agent != null && agent.Object != null && agent.Object.IsValid == true && agent.Bot != null && agent.Health.IsAlive == true;

		/// <summary>Frame rate, host tick time and the brains' share in the running match, with the thinning off and then on.</summary>
		private static IEnumerator MeasureMatch(NetworkRunner server, GameplayMode mode)
		{
			PerformanceProbe probe = PerformanceProbe.Attach(server);
			var results = new List<string>();
			float[] fps = new float[2];
			float[] tick = new float[2];

			for (int pass = 0; pass < 2; ++pass)
			{
				BotDebug.NoThinning = pass == 0;
				yield return Wait(2.0f);

				BotBrain.DebugTickTime  = 0;
				BotBrain.DebugTickCount = 0;
				probe.Begin();
				yield return Wait(15.0f);
				probe.End();

				int    alive   = mode.GetBots().Count(b => b.Health.IsAlive == true);
				int    thinned = BotBrain.All.Count(b => b.IsThinned == true);
				double brainMs = BotBrain.DebugTickTime * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
				int    ticks   = Mathf.Max(1, BotBrain.DebugTickCount / Mathf.Max(1, BotBrain.All.Count));

				fps[pass]  = Fps(probe.FrameTimes);
				tick[pass] = Average(probe.TickTimes);
				results.Add($"{(pass == 0 ? "every pose every tick" : "thinning on")}: {fps[pass]:F0} fps, host simulation {tick[pass]:F2} ms per tick, brains {brainMs / ticks:F2} ms, {alive} bots alive, {thinned} thinned");
			}

			BotDebug.NoThinning = false;
			Log($"  Cost in the match: {string.Join("; ", results)}");
			Check(tick[1] <= tick[0] * 1.05f, $"Thinning distant bots doesn't cost more: host simulation {tick[0]:F2} → {tick[1]:F2} ms per tick, {fps[0]:F0} → {fps[1]:F0} fps");
		}

		// HELPERS

		private static Agent SpawnOne(GameplayMode mode, Vector3 at, Vector3? lookAt = null)
		{
			Vector3    forward  = lookAt.HasValue == true ? Flat3(lookAt.Value - at) : Vector3.forward;
			Quaternion rotation = Quaternion.LookRotation(forward.sqrMagnitude > 0.001f ? forward : Vector3.forward);
			return mode.SpawnBot(at, rotation, mode.NewDummyTeam());
		}

		private static void GiveGun(Agent bot, string gunName)
		{
			int index = ItemCatalog.GetGunIndex(gunName);
			if (index < 0)
				return;

			GunData data = ItemCatalog.GetGunData(index);
			bot.Inventory.DebugGive(LootStack.Gun(index, data.Magazine, 0), true);
			bot.Weapons.DebugSetAmmo(data.Caliber, GunplaySettings.Current.GetCarryCap(data.Caliber));
		}

		private static bool HasGun(Agent bot, out int ammo)
		{
			ammo = 0;
			bool has = false;
			for (int slot = Weapons.FIRST_PRIMARY_SLOT; slot <= Weapons.LAST_PRIMARY_SLOT; ++slot)
			{
				if (bot.Weapons.GetWeapon(slot) is BulletWeapon gun)
				{
					has = true;
					ammo += gun.WeaponAmmo + gun.MagazineAmmo;
				}
			}
			return has;
		}

		private static int CountHeals(Agent bot)
		{
			int count = 0;
			for (int i = 0; i < HealthSettings.HealItemCount; ++i)
			{
				count += bot.Inventory.CountHeal((EHealItem)i);
			}
			return count;
		}

		/// <summary>An area's warm-up spawn point (open ground on the mesh), else a point on the mesh near its center.</summary>
		private static bool AreaPoint(NetworkRunner server, BrownoutMap.Area area, out Vector3 point)
		{
			foreach (SpawnPoint spawn in Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
			{
				if (spawn.gameObject.scene != server.SimulationUnityScene)
					continue;

				Vector3 p = spawn.transform.position;
				if (Mathf.Abs(p.x - area.Center.x) <= area.Size.x * 0.5f + 10.0f && Mathf.Abs(p.z - area.Center.y) <= area.Size.y * 0.5f + 10.0f && BotNavigation.Sample(p, 2.0f, out point) == true)
					return true;
			}

			return FindOpenPoint(Ground(server, new Vector3(area.Center.x, 0.0f, area.Center.y)), 40.0f, out point);
		}

		/// <summary>A point on the NavMesh near this one with open sky (not inside a building).</summary>
		private static bool FindOpenPoint(Vector3 near, float radius, out Vector3 point)
		{
			for (int i = 0; i < 40; ++i)
			{
				Vector2 offset = i == 0 ? Vector2.zero : Random.insideUnitCircle * radius;
				if (BotNavigation.Sample(near + new Vector3(offset.x, 0.0f, offset.y), 6.0f, out point) == true)
					return true;
			}

			point = near;
			return false;
		}

		/// <summary>Two points on the mesh this far apart, on fairly level open ground, in sight of each other (eye to chest), clear of the river.</summary>
		private static bool FindOpenLine(NetworkRunner server, float distance, out Vector3 a, out Vector3 b, Vector3? around = null, float radius = 350.0f)
		{
			BrownoutMap map = BrownoutMap.Get(server.SimulationUnityScene);
			Vector3 center = around ?? Vector3.zero;

			for (int i = 0; i < 400; ++i)
			{
				Vector2 r = Random.insideUnitCircle * radius;
				Vector3 p = Ground(server, center + new Vector3(r.x, 0.0f, r.y));
				if (BotNavigation.Sample(p, 1.0f, out a) == false || IsRoofed(server, a) == true)
					continue;

				float   angle = Random.Range(0.0f, Mathf.PI * 2.0f);
				Vector3 q = Ground(server, a + new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * distance);
				if (BotNavigation.Sample(q, 1.0f, out b) == false || IsRoofed(server, b) == true)
					continue;
				if (Mathf.Abs(Flat(a - b).magnitude - distance) > 1.5f || Mathf.Abs(a.y - b.y) > distance * 0.15f)
					continue;
				if (map != null && (map.GetRiverDistance(a, out _, out _, out _) < 15.0f || map.GetRiverDistance(b, out _, out _, out _) < 15.0f))
					continue;
				if (LineClear(server, a + Vector3.up * 1.6f, b + Vector3.up * 1.2f) == false || LineClear(server, b + Vector3.up * 1.6f, a + Vector3.up * 1.2f) == false)
					continue;

				return true;
			}

			a = b = default;
			return false;
		}

		/// <summary>A bush with open ground on both sides 6 m out, the line through it otherwise clear.</summary>
		private static bool FindBushBetween(NetworkRunner server, out Vector3 botPoint, out Vector3 playerPoint)
		{
			botPoint = playerPoint = default;
			PhysicsScene physics = server.GetPhysicsScene();
			BrownoutMap  map     = BrownoutMap.Get(server.SimulationUnityScene);
			BushColliders solid  = map != null ? map.GetComponentInChildren<BushColliders>() : null;
			if (solid == null || solid.Root == null)
				return false;

			CapsuleCollider[] capsules = solid.Root.GetComponentsInChildren<CapsuleCollider>();
			if (capsules.Length == 0)
				return false;

			for (int i = 0; i < 300; ++i)
			{
				CapsuleCollider capsule = capsules[Random.Range(0, capsules.Length)];
				var bush = new { position = capsule.transform.TransformPoint(capsule.center) };
				if (Mathf.Abs(bush.position.x) > 420.0f || Mathf.Abs(bush.position.z) > 420.0f)
					continue;

				float   radius = capsule.radius;
				float   angle  = Random.Range(0.0f, Mathf.PI * 2.0f);
				Vector3 dir    = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
				Vector3 p = Ground(server, bush.position + dir * (radius + 0.8f));
				Vector3 q = Ground(server, bush.position - dir * (radius + 10.0f));
				if (BotNavigation.Sample(p, 0.6f, out p) == false || BotNavigation.Sample(q, 0.6f, out q) == false)
					continue;

				// The bush must be the only thing between a standing eye and a crouched player, and block both lines a bot checks (head, chest)
				Vector3 eye = q + Vector3.up * 1.7f;
				Vector3 head  = p + Vector3.up * MovementSettings.Current.CrouchEyeHeight;
				Vector3 chest = p + Vector3.up * (MovementSettings.Current.CrouchEyeHeight * 0.7f);
				if (LineClear(server, eye, head) == false || LineClear(server, eye, chest) == false)
					continue;
				if (BushBlocks(physics, capsule, eye, head) == false || BushBlocks(physics, capsule, eye, chest) == false)
					continue;

				playerPoint = p;
				botPoint    = q;
				_lastBush   = new Vector4(bush.position.x, bush.position.y, bush.position.z, radius);
				return true;
			}

			return false;
		}

		/// <summary>The line hits this bush's collider (the bots' own sight test, on layer 15).</summary>
		private static bool BushBlocks(PhysicsScene physics, CapsuleCollider bush, Vector3 from, Vector3 to)
		{
			Vector3 offset = to - from;
			var     hits   = new RaycastHit[16];
			int     count  = physics.Raycast(from, offset.normalized, hits, offset.magnitude - 0.2f, 1 << Agent.RiverWallLayer, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < count; ++i)
			{
				if (hits[i].collider == bush)
					return true;
			}

			return false;
		}

		private static Vector4 _lastBush;

		private static bool LineClear(NetworkRunner server, Vector3 from, Vector3 to)
		{
			Vector3 offset = to - from;
			return server.GetPhysicsScene().Raycast(from, offset.normalized, out _, offset.magnitude - 0.2f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == false;
		}

		private static bool IsRoofed(NetworkRunner server, Vector3 point)
		{
			PhysicsScene physics = server.GetPhysicsScene();
			if (physics.Raycast(point + Vector3.up * 1.0f, Vector3.up, out RaycastHit hit, 30.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == false)
				return false;

			return hit.collider is TerrainCollider == false;
		}

		private static float PathLength(Vector3 from, Vector3 to)
		{
			var path = new NavMeshPath();
			if (NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) == false)
				return Flat(to - from).magnitude;

			float length = 0.0f;
			for (int i = 0; i < path.corners.Length - 1; ++i)
			{
				length += Vector3.Distance(path.corners[i], path.corners[i + 1]);
			}
			return length;
		}

		private static int SideOfRiver(BrownoutMap map, Vector3 point)
		{
			map.GetRiverDistance(point, out Vector2 closest, out _, out Vector2 across);
			// across points from the line to the point; which side is decided by the line's direction at that spot
			IReadOnlyList<Vector2> line = map.RiverLine;
			int nearest = 0;
			float best = float.MaxValue;
			for (int i = 0; i < line.Count; ++i)
			{
				float d = (line[i] - closest).sqrMagnitude;
				if (d < best) { best = d; nearest = i; }
			}

			Vector2 tangent = line[Mathf.Min(nearest + 1, line.Count - 1)] - line[Mathf.Max(nearest - 1, 0)];
			Vector2 normal  = new Vector2(-tangent.y, tangent.x);
			return Vector2.Dot(new Vector2(point.x, point.z) - closest, normal) >= 0.0f ? 1 : -1;
		}

		private static bool CrossesRiver(BrownoutMap map, Vector3 a, Vector3 b, out Vector3 at)
		{
			at = default;
			int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b)));
			for (int i = 0; i <= steps; ++i)
			{
				Vector3 p = Vector3.Lerp(a, b, i / (float)steps);
				if (map.GetRiverDistance(p, out _, out _, out _) < map.RiverHalfWidth)
				{
					at = p;
					return true;
				}
			}
			return false;
		}

		private static List<Vector3> BridgeDecks(NetworkRunner server)
		{
			var decks = new List<Vector3>();
			BrownoutMap map = BrownoutMap.Get(server.SimulationUnityScene);
			Transform bridges = map != null ? map.transform.Find("Bridges") : null;
			if (bridges != null)
			{
				foreach (Transform bridge in bridges)
				{
					Transform deck = bridge.Find("Deck");
					decks.Add(deck != null ? deck.position : bridge.position);
				}
			}
			return decks;
		}

		private static float EventTime(string line)
		{
			int index = line.LastIndexOf("time ");
			if (index < 0)
				return 0.0f;

			string text = line.Substring(index + 5).Replace(" s", string.Empty).Trim();
			return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : 0.0f;
		}

		private static float Fps(List<float> frameTimes)
		{
			float average = Average(frameTimes);
			return average > 0.0f ? 1.0f / average : 0.0f;   // frame times are in seconds
		}

		private static float Average(List<float> values) => values.Count > 0 ? values.Average() : 0.0f;

		private static void ReleaseInput()
		{
			AgentInput.DebugMoveDirection = Vector2.zero;
			AgentInput.DebugHoldSprint    = false;
			AgentInput.DebugHoldJump      = false;
			AgentInput.DebugHoldAttack    = false;
			AgentInput.DebugHoldAim       = false;
			AgentInput.DebugHoldCrouch    = false;
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

		private static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
		private static Vector3 Flat3(Vector3 v) => new Vector3(v.x, 0.0f, v.z);

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
			File.WriteAllText(Path.Combine(folder, "BotCheck.txt"), string.Join("\n", _log));
		}

		private static void UpdateRoutine()
		{
			if (Application.isPlaying == false)
			{
				Log("Bot Check stopped: Play mode ended.");
				SaveLog();
				Debug.Log("[BotCheck] STOPPED: Play mode ended");
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
				Log($"Bot Check failed: {exception}");
				Debug.Log("[BotCheck] FAILED with an exception (Logs/BotCheck/BotCheck.txt)");
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
			if (_running == true)
			{
				Debug.Log("[BotCheck] ENDED");   // Every way the check ends prints this (a watcher on Editor.log waits for it)
			}

			_running = false;
			_routine.Clear();
			Health.DebugGodMode    = false;
			BotDebug.IgnorePlayers = false;
			ReleaseInput();
			EditorApplication.update -= UpdateRoutine;
		}
	}
}
