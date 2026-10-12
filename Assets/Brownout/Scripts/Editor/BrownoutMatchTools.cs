namespace TPSBR
{
	using System.Collections;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text.RegularExpressions;
	using Fusion;
	using Fusion.Addons.KCC;
	using TPSBR.UI;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Match flow tools (phase 7). Brownout → Setup → Build Match Settings makes Resources/Settings/MatchSettings.asset and links it from
	/// GlobalSettings. Brownout → Test → Match: Play Battle Royale (an editor preference StandaloneManager reads, so GenArea5 starts in
	/// Battle Royale without a scene change), Fast Match (zone clock x4), Start Match, Skip Zone Step, Put Player Outside Zone / Past Map
	/// Edge, Kill All Dummies, Log Match Status / Events, and the Match Check: run it in GenArea5 in Battle Royale with 2 peers, from the
	/// lagged client (switch to the client first), during the warm-up. It starts the match, rides the helicopter, jumps both players and
	/// times the drop, checks the zone stages and damage, the map edge, placement and the victory. About 3 minutes; the match is over
	/// afterwards (restart Play mode). Read GetMatchCheckLog() or Logs/MatchCheck/MatchCheck.txt; pictures in Logs/MatchCheck/.
	/// </summary>
	public static class BrownoutMatchTools
	{
		// CONSTANTS

		private const string MENU          = "Brownout/Test/Match/";
		private const string SettingsPath  = "Assets/Brownout/Resources/Settings/MatchSettings.asset";
		public  const string Folder        = "Logs/MatchCheck";

		// MENU

		[MenuItem("Brownout/Setup/Build Match Settings", priority = 7)]
		private static void BuildMenu() => Debug.Log(BuildMatchSettings());

		[MenuItem(MENU + "Play Battle Royale", priority = 1)]
		private static void TogglePlayBattleRoyale()
		{
			bool value = EditorPrefs.GetBool(StandaloneManager.PlayBattleRoyaleKey, false) == false;
			EditorPrefs.SetBool(StandaloneManager.PlayBattleRoyaleKey, value);
			Debug.Log(value == true ? "Play Battle Royale on: the next Play starts the scene in Battle Royale (warm-up, host starts with Enter)." : "Play Battle Royale off: scenes start in their own mode (GenArea5: Deathmatch).");
		}

		[MenuItem(MENU + "Play Battle Royale", true)]
		private static bool ValidatePlayBattleRoyale()
		{
			UnityEditor.Menu.SetChecked(MENU + "Play Battle Royale", EditorPrefs.GetBool(StandaloneManager.PlayBattleRoyaleKey, false));
			return true;
		}

		[MenuItem(MENU + "Start Match On Play", priority = 1)]
		private static void ToggleStartOnPlay()
		{
			bool value = EditorPrefs.GetBool(StandaloneManager.StartOnPlayKey, false) == false;
			EditorPrefs.SetBool(StandaloneManager.StartOnPlayKey, value);
			Debug.Log(value == true ? "Start Match On Play on: from the next Play, the match starts by itself once every peer is in (straight into the helicopter). Turn it off before checks that need the warm-up (Map, Bot, Match, HUD checks)." : "Start Match On Play off: the warm-up waits for Enter on the host.");
		}

		[MenuItem(MENU + "Start Match On Play", true)]
		private static bool ValidateStartOnPlay()
		{
			UnityEditor.Menu.SetChecked(MENU + "Start Match On Play", EditorPrefs.GetBool(StandaloneManager.StartOnPlayKey, false));
			return true;
		}

		[MenuItem(MENU + "Fast Match (zone clock x4)", priority = 2)]
		private static void ToggleFastMatch()
		{
			MatchSettings.DebugTimeScale = MatchSettings.DebugTimeScale > 1.0f ? 1.0f : 4.0f;
			Debug.Log($"Zone clock x{MatchSettings.DebugTimeScale:0} (takes effect from the next zone step).");
		}

		[MenuItem(MENU + "Fast Match (zone clock x4)", true)]
		private static bool ValidateFastMatch()
		{
			UnityEditor.Menu.SetChecked(MENU + "Fast Match (zone clock x4)", MatchSettings.DebugTimeScale > 1.0f);
			return true;
		}

		[MenuItem(MENU + "Match Check", priority = 20)]
		private static void MatchCheckMenu() => Debug.Log(RunMatchCheck());

		[MenuItem(MENU + "Start Match", priority = 40)]
		private static void StartMenu() => Debug.Log(StartMatch());

		[MenuItem(MENU + "Skip Zone Step", priority = 41)]
		private static void SkipMenu() => Debug.Log(SkipZoneStep());

		[MenuItem(MENU + "Put Player Outside Zone", priority = 42)]
		private static void OutsideMenu() => Debug.Log(PutPlayerOutsideZone());

		[MenuItem(MENU + "Put Player Past Map Edge", priority = 43)]
		private static void EdgeMenu() => Debug.Log(PutPlayerPastMapEdge());

		[MenuItem(MENU + "Kill All Dummies", priority = 44)]
		private static void KillMenu() => Debug.Log(KillAllDummies());

		[MenuItem(MENU + "Log Match Status", priority = 60)]
		private static void StatusMenu() => Debug.Log(GetMatchStatus());

		[MenuItem(MENU + "Log Match Events", priority = 61)]
		private static void EventsMenu() => Debug.Log(string.Join("\n", MatchDebug.EventLog.Skip(Mathf.Max(0, MatchDebug.EventLog.Count - 80))));

		[MenuItem(MENU + "Match Check", true)]
		[MenuItem(MENU + "Start Match", true)]
		[MenuItem(MENU + "Skip Zone Step", true)]
		[MenuItem(MENU + "Put Player Outside Zone", true)]
		[MenuItem(MENU + "Put Player Past Map Edge", true)]
		[MenuItem(MENU + "Kill All Dummies", true)]
		[MenuItem(MENU + "Log Match Status", true)]
		private static bool ValidatePlaying() => Application.isPlaying;

		// SETUP

		public static string BuildMatchSettings()
		{
			var settings = AssetDatabase.LoadAssetAtPath<MatchSettings>(SettingsPath);
			string result = string.Empty;

			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<MatchSettings>();
				AssetDatabase.CreateAsset(settings, SettingsPath);
				result += $"Created {SettingsPath}. ";
			}

			string globalPath = AssetDatabase.FindAssets("t:GlobalSettings").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
			var    global     = globalPath != null ? AssetDatabase.LoadAssetAtPath<GlobalSettings>(globalPath) : null;
			if (global == null)
				return result + "GlobalSettings asset not found.";

			if (global.Match != settings)
			{
				global.Match = settings;
				EditorUtility.SetDirty(global);
				result += $"Linked MatchSettings from {globalPath}. ";
			}

			AssetDatabase.SaveAssets();
			return result + "Match settings ready.";
		}

		// TOOLS

		public static string StartMatch()
		{
			BattleRoyaleGameplayMode mode = ServerMode();
			if (mode == null)
				return "No Battle Royale match on a running host (turn on Brownout → Test → Match → Play Battle Royale and restart Play).";
			if (mode.Phase != EMatchPhase.Warmup)
				return $"The match has already started ({mode.Phase}).";

			mode.StartMatch();
			return $"Match started: {mode.Phase}.";
		}

		public static string SkipZoneStep()
		{
			BattleRoyaleGameplayMode mode = ServerMode();
			ShrinkingArea zone = mode != null ? mode.ShrinkingArea : null;
			if (zone == null || zone.IsScheduled == false)
				return "No zone running.";

			zone.SkipStep();
			return $"Zone step {zone.ScheduleStep + 1}: {zone.StepPhase}, radius {zone.Radius:F0}, damage {zone.StepDamage:F0}.";
		}

		public static string PutPlayerOutsideZone()
		{
			BattleRoyaleGameplayMode mode = ServerMode();
			ShrinkingArea zone = mode != null ? mode.ShrinkingArea : null;
			Agent agent = ObservedServerAgent();
			if (zone == null || zone.IsScheduled == false || agent == null)
				return "Need a running zone and an observed player.";

			Vector3 direction = (agent.transform.position - zone.Center).OnlyXZ();
			direction = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.forward;
			Vector3 target = zone.Center + direction * (zone.Radius + 20.0f);
			Move(agent, Ground(agent.Runner, target));
			return $"{agent.name} put 20 m outside the zone at {agent.transform.position:F0}.";
		}

		public static string PutPlayerPastMapEdge()
		{
			Agent agent = ObservedServerAgent();
			if (agent == null || MapBounds.TryGet(agent.Context, out Vector3 center, out Vector2 half) == false)
				return "Need an observed player on a map.";

			// The level's edge is often open water: a test floor at the player's own height, straddling the edge
			float   height = agent.transform.position.y + 1.0f;
			Vector3 target = new Vector3(center.x + half.x + 15.0f, height, Mathf.Clamp(agent.transform.position.z, center.z - half.y + 20.0f, center.z + half.y - 20.0f));
			BrownoutHudCheck.BuildFloor(new Vector3(center.x + half.x, height, target.z));
			Move(agent, target + Vector3.up * 0.1f);
			return $"{agent.name} put 15 m past the map edge at {agent.transform.position:F0} (on a test floor).";
		}

		public static string KillAllDummies()
		{
			BattleRoyaleGameplayMode mode = ServerMode();
			GameplayMode gameplay = mode != null ? (GameplayMode)mode : Object.FindObjectsByType<GameplayMode>(FindObjectsSortMode.None).FirstOrDefault(m => m.HasStateAuthority == true);
			if (gameplay == null)
				return "No match on a running host.";

			int killed = 0;
			foreach (Agent dummy in gameplay.GetDummies().ToList())
			{
				if (Kill(dummy) == true)
				{
					++killed;
				}
			}

			return $"Killed {killed} dummies.";
		}

		public static string GetMatchStatus()
		{
			BattleRoyaleGameplayMode mode = ServerMode();
			if (mode == null)
				return "No Battle Royale match on a running host.";

			ShrinkingArea zone = mode.ShrinkingArea;
			var lines = new List<string>
			{
				$"Phase {mode.Phase}, teams alive {mode.TeamsAlive}, players alive {mode.PlayersAlive}, aboard {mode.Aboard}, winner {mode.WinningTeam}, ends in {mode.EndTimeLeft:F1} s, zone clock x{MatchSettings.TimeScale:0}",
			};

			if (mode.Airplane != null)
			{
				lines.Add($"Helicopter at {mode.Airplane.transform.position:F0}, {mode.Airplane.PathProgress * 100.0f:F0}% along, drop {(mode.Airplane.DropEnabled ? "open" : "closed")}");
			}

			if (zone != null && zone.IsScheduled == true)
			{
				lines.Add($"Zone step {zone.ScheduleStep + 1} {zone.StepPhase}, {zone.StepTimeLeft:F0} s left, radius {zone.Radius:F0} on {zone.Center:F0}, next {(zone.IsAnnounced ? $"{zone.ShrinkRadius:F0} on {zone.ShrinkCenter:F0}" : "none")}, damage {zone.StepDamage:F0}/s");
			}

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None).Where(a => a.Object != null && a.Object.IsValid == true && a.HasStateAuthority == true))
			{
				int team = GameplayMode.GetTeam(agent);
				lines.Add($"  {agent.name,-14} team {team,3} place {mode.GetTeamPlace(team),2} health {agent.Health.CurrentHealth,5:F0} {(agent.Health.IsDowned ? "downed" : agent.Health.IsAlive ? "up" : "dead"),-6} {agent.Character.CharacterController.FixedData.MoveMode,-9} at {agent.transform.position:F0}{(agent.EdgeOutTick > 0 ? $" past the edge, {agent.EdgeTimeLeft:F1} s" : string.Empty)}");
			}

			return string.Join("\n", lines);
		}

		// THE CHECK

		private static readonly List<string>       _log     = new List<string>(128);
		private static readonly Stack<IEnumerator> _routine = new Stack<IEnumerator>();
		private static bool  _running;
		private static int   _passed;
		private static int   _checked;
		private static float _savedTimeScale = 1.0f;

		public static string RunMatchCheck()
		{
			if (Application.isPlaying == false)
				return "Enter Play mode first (GenArea5 with Play Battle Royale on, 2 peers).";
			if (_running == true)
				return "The Match Check is already running.";

			_log.Clear();
			_routine.Clear();
			_routine.Push(CheckRoutine());
			_running = true;

			EditorApplication.update -= UpdateRoutine;
			EditorApplication.update += UpdateRoutine;

			return "Match Check started (about 3 minutes). Read GetMatchCheckLog().";
		}

		public static string GetMatchCheckLog()
		{
			return (_running == true ? "(running)\n" : "") + string.Join("\n", _log);
		}

		private static IEnumerator CheckRoutine()
		{
			BattleRoyaleGameplayMode mode = ServerMode();
			BrownoutHud              hud  = FindInputHud();

			if (mode == null || hud == null || hud.Agent == null)
			{
				Log("Need a Battle Royale warm-up (Play Battle Royale on, GenArea5, 2 peers) and the HUD of the peer with input.");
				yield break;
			}

			if (mode.Phase != EMatchPhase.Warmup)
			{
				Log($"The match has already started ({mode.Phase}); restart Play mode for a fresh warm-up.");
				yield break;
			}

			MatchSettings settings  = MatchSettings.Current;
			NetworkRunner server    = mode.Runner;
			NetworkRunner client    = hud.Agent.Runner;
			PlayerRef     localRef  = hud.Agent.Object.InputAuthority;
			PlayerRef     otherRef  = server.ActivePlayers.FirstOrDefault(p => p != localRef);
			bool          onClient  = client.IsServer == false;

			_passed  = 0;
			_checked = 0;
			_savedTimeScale = MatchSettings.DebugTimeScale;
			MatchSettings.DebugTimeScale = 4.0f;
			// Phase 9: the check's numbers are for 3 dummy duos and no bots
			MatchSettings.DebugDummyDuos = 3;
			MatchSettings.DebugBotDuos   = 0;
			MatchDebug.Clear();
			GameplayMode.DebugNoTeams = false;
			KeyBindings.DebugHeld.Clear();

			Log($"Match Check: viewed from {(onClient ? "the client (simulated lag in play)" : "the host")}, {server.ActivePlayers.Count()} players, zone clock x4.");

			// 1. Warm-up
			Check(Read(hud, "warmup") == "on" && Read(hud, "match.phase") == "Warmup", $"Warm-up banner on the HUD: {Read(hud, "warmup")}, phase {Read(hud, "match.phase")}");
			Screenshot("01_warmup");
			yield return Wait(0.5f);

			// 2. The host starts: everyone aboard, loot rolled again, dummies placed
			mode.StartMatch();
			yield return Wait(1.0f);

			Airplane heli = mode.Airplane;
			Check(mode.Phase == EMatchPhase.Flight, $"Start: phase {mode.Phase}");
			Check(heli != null && Mathf.Abs(heli.transform.position.y - settings.HelicopterHeight) < 0.5f, $"Helicopter at {(heli != null ? heli.transform.position.y : 0.0f):F1} m (table {settings.HelicopterHeight:F0} m)");
			Check(mode.Aboard == server.ActivePlayers.Count(), $"Everyone aboard: {mode.Aboard} of {server.ActivePlayers.Count()}");
			Check(mode.DummyCount == settings.DummyDuoCount * 2, $"Dummies placed: {mode.DummyCount} (table {settings.DummyDuoCount} duos)");
			Check(BrownoutHudCheck.ServerCopy(localRef) == null && BrownoutHudCheck.ServerCopy(otherRef) == null, "Nobody is left on the ground (both agents gone)");

			if (heli == null)
				yield break;

			// Straight line at the table speed
			Vector3 p0 = heli.transform.position;
			int     t0 = server.Tick;
			yield return Wait(1.5f);
			Vector3 p1 = heli.transform.position;
			float   dt = (server.Tick - t0) * server.DeltaTime;
			float   speed = dt > 0.0f ? Vector3.Distance(p0, p1) / dt : 0.0f;
			Vector3 pathDirection = (heli.PathEnd - heli.PathStart).normalized;
			float   offLine = Vector3.Cross(pathDirection, p1 - heli.PathStart).magnitude;
			Check(Mathf.Abs(speed - settings.HelicopterSpeed) < 1.0f && offLine < 0.5f, $"Helicopter flies straight at {speed:F1} m/s (table {settings.HelicopterSpeed:F0}), {offLine:F2} m off its line");
			Check(Read(hud, "heli.prompt") != "hidden" && Read(hud, "minimap.path") == "shown", $"Helicopter HUD: prompt {Read(hud, "heli.prompt")}, flight path on the minimap {Read(hud, "minimap.path")}");
			Screenshot("02_helicopter");

			// 3. Over the map: both jump (the client with its Jump key, the host through the server)
			double waitUntil = EditorApplication.timeSinceStartup + 20.0;
			while (heli != null && heli.DropEnabled == false && EditorApplication.timeSinceStartup < waitUntil)
			{
				yield return null;
			}

			Check(heli != null && heli.DropEnabled == true, $"The drop opens over the map ({(heli != null ? heli.PathProgress * 100.0f : 0.0f):F0}% along)");
			Check(Read(hud, "heli.prompt") == "jump", $"Jump prompt: {Read(hud, "heli.prompt")}");

			KeyBindings.DebugHeld.Add(EBinding.Jump);
			yield return Wait(0.3f);
			KeyBindings.DebugHeld.Remove(EBinding.Jump);
			mode.RequestAirplaneJump(otherRef, heli.transform.forward);

			waitUntil = EditorApplication.timeSinceStartup + 5.0;
			while (BrownoutHudCheck.ServerCopy(localRef) == null && EditorApplication.timeSinceStartup < waitUntil)
			{
				yield return null;
			}

			Agent serverAgent = BrownoutHudCheck.ServerCopy(localRef);
			int   jumpTick    = server.Tick;
			Check(serverAgent != null && serverAgent.Character.CharacterController.FixedData.MoveMode == EMoveMode.Wingsuit, $"Jumped with the Jump key: {(serverAgent != null ? serverAgent.Character.CharacterController.FixedData.MoveMode.ToString() : "still aboard")}");

			if (serverAgent == null)
				yield break;

			string agentName = serverAgent.name;
			float  jumpY     = serverAgent.transform.position.y;
			Vector3 jumpPosition = serverAgent.transform.position;

			// Mid-glide: no gun, third person, altimeter, same on both peers
			yield return Wait(4.0f);
			serverAgent = BrownoutHudCheck.ServerCopy(localRef);
			Agent clientAgent = ClientCopy(client, localRef);
			Agent otherView   = OtherPeerCopy(server, client, localRef);

			Check(serverAgent != null && serverAgent.Movement.CanFire() == false && serverAgent.Movement.CanAim() == false, "No shooting or aiming mid-air");
			Check(clientAgent != null && clientAgent.Character.IsThirdPersonView == true, $"Third-person camera in the wingsuit: {(clientAgent != null && clientAgent.Character.IsThirdPersonView)}");
			Check(Read(hud, "altimeter") != "hidden", $"Altimeter: {Read(hud, "altimeter")}");
			if (serverAgent != null && clientAgent != null)
			{
				// Same tick on both (phase 8): the client predicts ahead of the server, so comparing them at the same moment only measured
				// the lag. Take the client's predicted position at its tick, then the server's when it simulates that tick
				KCC     clientKcc  = clientAgent.Character.CharacterController;
				KCC     serverKcc  = serverAgent.Character.CharacterController;
				int     clientTick = clientKcc.FixedData.Tick;
				Vector3 clientAt   = clientKcc.FixedData.TargetPosition;
				float   velocity   = serverKcc.FixedData.DropVelocity.magnitude;
				Vector3 serverAt   = serverKcc.FixedData.TargetPosition;
				int     closest    = int.MaxValue;
				double  until      = EditorApplication.timeSinceStartup + 3.0;

				while (EditorApplication.timeSinceStartup < until)
				{
					int serverTick = serverKcc.FixedData.Tick;
					if (Mathf.Abs(serverTick - clientTick) < closest)
					{
						closest  = Mathf.Abs(serverTick - clientTick);
						serverAt = serverKcc.FixedData.TargetPosition + serverKcc.FixedData.DropVelocity * ((clientTick - serverTick) * server.DeltaTime);
					}

					if (serverTick >= clientTick)
						break;

					yield return null;
				}

				float apart = Vector3.Distance(serverAt, clientAt);
				Check(apart < 1.5f, $"Client and server agree mid-glide: {apart:F2} m apart at the same tick ({velocity:F0} m/s)");
			}
			Check(otherView != null && otherView.Character.CharacterController.Data.MoveMode == EMoveMode.Wingsuit, $"The other peer sees the wingsuit: {(otherView != null ? otherView.Character.CharacterController.Data.MoveMode.ToString() : "not visible")}");
			Screenshot("03_wingsuit");

			// Wait for the landing
			waitUntil = EditorApplication.timeSinceStartup + 45.0;
			bool sawWingsOff = false;
			bool sawRoll     = false;
			while (EditorApplication.timeSinceStartup < waitUntil)
			{
				serverAgent = BrownoutHudCheck.ServerCopy(localRef);
				if (serverAgent == null)
					break;

				EMoveMode mode2 = serverAgent.Character.CharacterController.FixedData.MoveMode;
				sawWingsOff |= mode2 == EMoveMode.WingsOff;

				if (mode2 == EMoveMode.LandRoll && sawRoll == false)
				{
					sawRoll = true;
					yield return Wait(0.2f);
					clientAgent = ClientCopy(client, localRef);
					Check(clientAgent != null && clientAgent.Character.IsThirdPersonView == true, "Still third person early in the landing roll");
					Screenshot("04_landing_roll");
				}

				if (AgentMovement.IsDropMode(mode2) == false && mode2 != EMoveMode.LandRoll)
					break;

				yield return null;
			}

			serverAgent = BrownoutHudCheck.ServerCopy(localRef);
			float dropTime = (server.Tick - jumpTick) * server.DeltaTime - (sawRoll == true ? settings.LandRollTime : 0.0f);
			float glided   = serverAgent != null ? (serverAgent.transform.position - jumpPosition).OnlyXZ().magnitude : 0.0f;
			Check(sawWingsOff == true, "The wings popped off by themselves");
			Check(sawRoll == true, "Landed in the landing roll");
			Check(serverAgent != null && serverAgent.Character.CharacterController.FixedData.MoveMode == EMoveMode.Walk, $"On your feet after the roll: {(serverAgent != null ? serverAgent.Character.CharacterController.FixedData.MoveMode.ToString() : "gone")}");
			// About 20 s to sea level (the generated level's ground). Our map's ground is 0 to 100 m up (phase 8), so the target comes from
			// the height actually fallen at the level glide's sink (the few meters after the wings pop off take well under a second)
			float landY      = serverAgent != null ? serverAgent.transform.position.y : 0.0f;
			float dropTarget = Mathf.Max(0.0f, jumpY - landY) / Mathf.Max(1.0f, settings.GlideSink);
			Check(Mathf.Abs(dropTime - dropTarget) <= 3.0f, $"Straight glide from {jumpY:F0} m to the ground at {landY:F0} m in {dropTime:F1} s (target {dropTarget:F1} ± 3 s from the glide sink), {glided:F0} m across");

			Match wingsOff = Regex.Match(string.Join("\n", MatchDebug.EventLog), $@"WINGS OFF {Regex.Escape(agentName)} ([0-9.]+) m");
			float offHeight = wingsOff.Success == true ? float.Parse(wingsOff.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : -1.0f;
			// The wings come off on the first tick the ground straight below is within WingsOffHeight; at the glide's speed a tick covers
			// under a meter, and a treetop or slope edge can come up between two ticks
			Check(offHeight >= 0.0f && offHeight <= settings.WingsOffHeight + 1.0f, $"Wings popped off {offHeight:F1} m above the ground (table {settings.WingsOffHeight:F0} m)");
			Check(serverAgent != null && serverAgent.Health.CurrentHealth >= HealthSettings.Current.MaxHealth - 0.5f, $"No damage from the landing: health {(serverAgent != null ? serverAgent.Health.CurrentHealth : 0.0f):F0}");

			yield return Wait(0.5f);
			clientAgent = ClientCopy(client, localRef);
			Check(clientAgent != null && clientAgent.Character.IsThirdPersonView == false, "First person again on the ground");
			Screenshot("05_landed");

			// 4. The helicopter leaves the map: the zone clock starts
			waitUntil = EditorApplication.timeSinceStartup + 40.0;
			while (mode.Phase == EMatchPhase.Flight && EditorApplication.timeSinceStartup < waitUntil)
			{
				yield return null;
			}

			yield return Wait(0.5f);
			ShrinkingArea zone = mode.ShrinkingArea;
			Check(mode.Phase == EMatchPhase.Match && zone != null && zone.IsScheduled == true, $"The helicopter left the map and the zone clock started: phase {mode.Phase}");
			Check(zone != null && Mathf.Abs(zone.Radius - settings.ZoneStartRadius) < 0.5f && zone.IsAnnounced == true && Mathf.Abs(zone.ShrinkRadius - settings.ZoneStartRadius * settings.ZoneShrink) < 0.5f, $"First circle shown at once: {zone.Radius:F0} m now, next {zone.ShrinkRadius:F0} m");
			Check(mode.Aboard == 0 && BrownoutHudCheck.ServerCopy(otherRef) != null, $"Nobody left aboard: {mode.Aboard}");

			// 5. Map edge (no zone damage yet): 10 s, then 20 a second, cleared on the way back
			serverAgent = BrownoutHudCheck.ServerCopy(localRef);
			Vector3 landedAt = serverAgent.transform.position;
			Log(PutPlayerPastMapEdge());
			yield return Wait(1.5f);
			serverAgent = BrownoutHudCheck.ServerCopy(localRef);
			Check(serverAgent.EdgeOutTick > 0 && Read(hud, "edge") != "inside", $"Past the edge: countdown {Read(hud, "edge")} s on the HUD");
			Screenshot("06_map_edge");
			float edgeHealth = serverAgent.Health.CurrentHealth;
			yield return Wait(settings.EdgeCountdown - 2.5f);
			serverAgent = BrownoutHudCheck.ServerCopy(localRef);
			Check(Mathf.Abs(serverAgent.Health.CurrentHealth - edgeHealth) < 0.5f, $"No damage during the countdown: {edgeHealth:F0} -> {serverAgent.Health.CurrentHealth:F0}");
			yield return Wait(4.0f);
			serverAgent = BrownoutHudCheck.ServerCopy(localRef);
			float edgeLost = edgeHealth - serverAgent.Health.CurrentHealth;
			Check(edgeLost >= settings.EdgeDamage * 2.0f - 0.5f && edgeLost <= settings.EdgeDamage * 4.0f + 0.5f, $"Then {settings.EdgeDamage:F0} a second: lost {edgeLost:F0} in the first ~3 s after the countdown");
			Move(serverAgent, landedAt + Vector3.up * 0.2f);
			BrownoutHudCheck.DestroyFloor();
			yield return Wait(1.5f);
			serverAgent = BrownoutHudCheck.ServerCopy(localRef);
			Check(serverAgent.EdgeOutTick == 0 && Read(hud, "edge") == "inside", $"Back on the map clears it: {Read(hud, "edge")}");
			HealBoth(localRef, otherRef);

			// 6. The zone stages: radii, every circle inside the last, damage per stage on a dummy left outside
			Agent target = mode.GetDummies().FirstOrDefault(d => d != null && d.Health.IsAlive == true);
			Vector3 previousCenter = zone.Center;
			float   previousRadius = zone.Radius;
			bool    allInside      = true;
			string  radii          = $"{zone.Radius:F0}";

			for (int step = 0; step < settings.ZoneSteps.Length; ++step)
			{
				// Players and every other dummy stand inside the next circle; one dummy in the map corner farthest from the zone, outside it
				KeepPlayersInside(zone, localRef, otherRef);
				target = mode.GetDummies().FirstOrDefault(d => d != null && d.Health.IsAlive == true && d.Health.IsDowned == false);
				KeepDummiesInside(mode, zone, target);

				if (target != null)
				{
					Move(target, Ground(server, FarCorner(mode, zone.Center)) + Vector3.up * 0.1f);
					HealFull(target);
				}

				yield return Wait(0.6f);

				MatchSettings.ZoneStep expected = settings.ZoneSteps[step];
				Check(zone.ScheduleStep == step && zone.StepPhase == expected.Phase && Mathf.Abs(zone.StepDamage - expected.Damage) < 0.01f, $"Step {step + 1}: {zone.StepPhase} {zone.StepTimeLeft:F0} s left (x4), damage {zone.StepDamage:F0}/s (table {expected.Phase} {expected.Duration / 4.0f:F0} s, {expected.Damage:F0}/s); HUD timer {Read(hud, "zone.timer")}");

				if (step == 1 || step == 3 || step == 5 || step == 7 || step == 0)
				{
					if (target != null)
					{
						float before = target.Health.CurrentHealth;
						yield return Wait(2.2f);
						float lost = before - (target.Health.IsAlive == true ? target.Health.CurrentHealth : 0.0f);
						bool  downedOrDead = target.Health.IsAlive == false || target.Health.IsDowned == true;
						bool  ok = expected.Damage <= 0.0f ? lost < 0.5f : (lost >= expected.Damage * 1.0f - 0.5f || downedOrDead == true);
						Check(ok, $"Dummy outside the circle: lost {lost:F0} in ~2 s at {expected.Damage:F0}/s{(downedOrDead ? " (went down)" : string.Empty)}");
					}

					if (step == 1)
					{
						Check(zone.IsShrinking == true && zone.Radius < previousRadius - 0.1f, $"Closing: {previousRadius:F0} m -> {zone.Radius:F1} m and shrinking");
						Screenshot("07_zone_closing");
					}
				}

				if (step == 0)
				{
					Check(Read(hud, "zone.timer").StartsWith("Wait") == true, $"Zone timer on the HUD: {Read(hud, "zone.timer")}");
				}

				zone.SkipStep();
				yield return Wait(0.3f);

				if (expected.Phase == EZonePhase.Close)
				{
					float inside = Vector3.Distance(zone.Center.OnlyXZ(), previousCenter.OnlyXZ()) + zone.Radius;
					allInside &= inside <= previousRadius + 0.5f;
					radii += $" -> {zone.Radius:F0}";
					previousCenter = zone.Center;
					previousRadius = zone.Radius;
				}
			}

			float shrink = settings.ZoneShrink;
			float r0     = settings.ZoneStartRadius;
			Check(radii == $"{r0:F0} -> {r0 * shrink:F0} -> {r0 * shrink * shrink:F0} -> {r0 * shrink * shrink * shrink:F0} -> 0", $"Radius halves each close, the last to nothing: {radii}");
			Check(allInside == true, "Every circle is fully inside the one before");

			// 7. Placement and the victory: the dummy teams go out, the players' team is last
			int localTeam = mode.GetTeamOfPlayer(localRef);
			zone.StopSchedule(); // Nothing else hurts while the last dummies go out
			foreach (Agent dummy in mode.GetDummies().ToList())
			{
				Kill(dummy);
				yield return Wait(0.4f);
			}

			yield return Wait(1.0f);

			var dummyPlaces = new List<int>();
			foreach (string line in MatchDebug.EventLog.Where(l => l.StartsWith("PLACED") == true))
			{
				Match placed = Regex.Match(line, @"#(\d+)");
				if (placed.Success == true)
				{
					dummyPlaces.Add(int.Parse(placed.Groups[1].Value));
				}
			}

			Check(dummyPlaces.Count == settings.DummyDuoCount && dummyPlaces.All(p => p >= 2 && p <= settings.DummyDuoCount + 1), $"Each dummy team placed as it went out: #{string.Join(", #", dummyPlaces)}");
			Check(mode.Phase == EMatchPhase.Ended && mode.WinningTeam == localTeam && mode.GetTeamPlace(localTeam) == 1, $"Last team standing wins: phase {mode.Phase}, winner team {mode.WinningTeam} (players' team {localTeam})");
			yield return Wait(0.6f);
			Check(Read(hud, "victory").Contains("VICTORY") == true, $"VICTORY banner on the HUD: {Read(hud, "victory")}");
			Check(Read(hud, "end").StartsWith("winner") == true, $"Match ends in: {Read(hud, "end")}");
			Screenshot("08_victory");

			yield return Wait(settings.EndCountdown + 1.5f);
			Check(mode.State == GameplayMode.EState.Finished, $"After the countdown the match is over: {mode.State}");

			Log($"Match Check: {_passed} of {_checked} pass.");
			Log("Server events:\n" + string.Join("\n", MatchDebug.EventLog));
			SaveLog();
		}

		// HELPERS

		private static BattleRoyaleGameplayMode ServerMode()
		{
			return Object.FindObjectsByType<BattleRoyaleGameplayMode>(FindObjectsSortMode.None).FirstOrDefault(m => m.Object != null && m.Object.IsValid == true && m.HasStateAuthority == true);
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

		private static Agent ClientCopy(NetworkRunner runner, PlayerRef playerRef)
		{
			return Object.FindObjectsByType<Agent>(FindObjectsSortMode.None).FirstOrDefault(a => a.Object != null && a.Object.IsValid == true && a.Runner == runner && a.Object.InputAuthority == playerRef);
		}

		/// <summary>How the other peer sees this player: the host's copy when viewing from the client, else the client's.</summary>
		private static Agent OtherPeerCopy(NetworkRunner server, NetworkRunner viewer, PlayerRef playerRef)
		{
			NetworkRunner other = viewer == server ? NetworkRunner.Instances.FirstOrDefault(r => r != null && r.IsRunning == true && r != server) : server;
			return other != null ? ClientCopy(other, playerRef) : null;
		}

		private static void KeepPlayersInside(ShrinkingArea zone, PlayerRef a, PlayerRef b)
		{
			Vector3 center = zone.IsAnnounced == true ? zone.ShrinkCenter : zone.Center;

			Agent first  = BrownoutHudCheck.ServerCopy(a);
			Agent second = BrownoutHudCheck.ServerCopy(b);
			if (first  != null) { Move(first,  Ground(first.Runner,  center + Vector3.left  * 1.5f) + Vector3.up * 0.1f); HealFull(first);  }
			if (second != null) { Move(second, Ground(second.Runner, center + Vector3.right * 1.5f) + Vector3.up * 0.1f); HealFull(second); }
		}

		/// <summary>Every dummy but the target stands up inside the next circle, around its center.</summary>
		private static void KeepDummiesInside(BattleRoyaleGameplayMode mode, ShrinkingArea zone, Agent target)
		{
			Vector3 center = zone.IsAnnounced == true ? zone.ShrinkCenter : zone.Center;
			int     index  = 0;

			foreach (Agent dummy in mode.GetDummies())
			{
				if (dummy == null || dummy == target || dummy.Health.IsAlive == false || dummy.Health.IsDowned == true)
					continue;

				float   angle  = index * 60.0f * Mathf.Deg2Rad;
				Vector3 offset = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * 6.0f;
				Move(dummy, Ground(dummy.Runner, center + offset) + Vector3.up * 0.1f);
				HealFull(dummy);
				++index;
			}
		}

		/// <summary>The map corner farthest from a point, 15 m in from the edges.</summary>
		private static Vector3 FarCorner(BattleRoyaleGameplayMode mode, Vector3 from)
		{
			if (MapBounds.TryGet(mode.Context, out Vector3 center, out Vector2 half) == false)
				return from + Vector3.right * 600.0f;

			float x = from.x > center.x ? center.x - half.x + 15.0f : center.x + half.x - 15.0f;
			float z = from.z > center.z ? center.z - half.y + 15.0f : center.z + half.y - 15.0f;
			return new Vector3(x, 0.0f, z);
		}

		private static void HealBoth(PlayerRef a, PlayerRef b)
		{
			HealFull(BrownoutHudCheck.ServerCopy(a));
			HealFull(BrownoutHudCheck.ServerCopy(b));
		}

		private static void HealFull(Agent agent)
		{
			if (agent == null || agent.Health.IsAlive == false || agent.Health.IsDowned == true)
				return;

			float missing = agent.Health.MaxHealth - agent.Health.CurrentHealth;
			if (missing <= 0.0f)
				return;

			var hitData = new HitData
			{
				Action   = EHitAction.Heal,
				Amount   = missing,
				Position = agent.transform.position,
				Normal   = Vector3.up,
				Target   = agent.Health,
			};

			HitUtility.ProcessHit(ref hitData);
		}

		/// <summary>Server: downs (if a teammate is up) and finishes the agent.</summary>
		internal static bool Kill(Agent agent)
		{
			if (agent == null || agent.Object == null || agent.Object.IsValid == false || agent.Health.IsAlive == false)
				return false;

			for (int i = 0; i < 2 && agent.Health.IsAlive == true; ++i)
			{
				if (agent.Health.IsDowned == true)
				{
					agent.Health.KillDowned("Match Check");
					break;
				}

				var hitData = new HitData
				{
					Action   = EHitAction.Damage,
					HitType  = EHitType.Suicide,
					Amount   = 10000.0f,
					Position = agent.transform.position,
					Normal   = Vector3.up,
					Target   = agent.Health,
				};

				HitUtility.ProcessHit(ref hitData);
			}

			return agent.Health.IsAlive == false;
		}

		/// <summary>The ground under a point (the runner's own physics scene), or the point at y 0.</summary>
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

		private static string Read(BrownoutHud hud, string key)
		{
			return hud != null && hud.Readout.TryGetValue(key, out string value) == true ? value : "none";
		}

		private static IEnumerator Wait(float seconds) => BrownoutHudCheck.Wait(seconds);

		private static void Screenshot(string name)
		{
			string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, Folder);
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
			string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, Folder);
			Directory.CreateDirectory(folder);
			File.WriteAllText(Path.Combine(folder, "MatchCheck.txt"), string.Join("\n", _log));
		}

		private static void UpdateRoutine()
		{
			if (Application.isPlaying == false)
			{
				Log("Match Check stopped: Play mode ended.");
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
				Log($"Match Check failed: {exception}");
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
			KeyBindings.DebugHeld.Clear();
			MatchSettings.DebugTimeScale = _savedTimeScale;
			MatchSettings.DebugDummyDuos = -1;
			MatchSettings.DebugBotDuos   = -1;
			EditorApplication.update -= UpdateRoutine;
		}
	}
}
