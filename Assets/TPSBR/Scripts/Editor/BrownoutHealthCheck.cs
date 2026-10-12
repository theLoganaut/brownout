namespace TPSBR
{
	using System.Collections;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;
	using System.Text;
	using Fusion;
	using Fusion.Addons.KCC;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 4 tools: the HealthSettings asset, Brownout → Test → Health and Dummies, the Health Check (armor, healing, downed,
	/// revive, friendly fire, team out, spectating) and the Performance Check (22 standing dummies). Run the checks on the lagged
	/// client (Numpad 2) in GenArea5 with 2 peers. Read them with GetHealthCheckLog() / GetPerformanceLog().
	/// </summary>
	public static class BrownoutHealthCheck
	{
		// CONSTANTS

		public const string SettingsPath = "Assets/TPSBR/Resources/Settings/HealthSettings.asset";

		private const string MENU     = "Brownout/Test/";
		private const string LOG_DIR  = "Logs/Performance";
		private const float  TICK     = 1.0f / 60.0f;

		// MENU

		[MenuItem("Brownout/Setup/Create Health Settings", priority = 3)]
		private static void CreateHealthSettingsMenu() => Debug.Log(CreateHealthSettings());

		[MenuItem(MENU + "Health Check", priority = 36)]
		private static void HealthCheckMenu() => Debug.Log(RunHealthCheck());
		[MenuItem(MENU + "Performance Check", priority = 37)]
		private static void PerformanceCheckMenu() => Debug.Log(RunPerformanceCheck(20.0f, 60.0f));

		[MenuItem(MENU + "Health/Give Armor Tier 1", priority = 60)]  private static void GiveArmor1Menu() => Debug.Log(GiveArmor(1));
		[MenuItem(MENU + "Health/Give Armor Tier 2", priority = 61)]  private static void GiveArmor2Menu() => Debug.Log(GiveArmor(2));
		[MenuItem(MENU + "Health/Give Armor Tier 3", priority = 62)]  private static void GiveArmor3Menu() => Debug.Log(GiveArmor(3));
		[MenuItem(MENU + "Health/Remove Armor", priority = 63)]       private static void RemoveArmorMenu() => Debug.Log(GiveArmor(0));
		[MenuItem(MENU + "Health/Refill Healing Items", priority = 70)] private static void RefillMenu() => Debug.Log(RefillHealItems());
		[MenuItem(MENU + "Health/Damage Observed (30, bullet)", priority = 71)] private static void DamageMenu() => Debug.Log(DamageObserved(30.0f));
		[MenuItem(MENU + "Health/Down Observed", priority = 72)]      private static void DownMenu() => Debug.Log(DownObserved());
		[MenuItem(MENU + "Health/Log Health Status", priority = 80)]  private static void StatusMenu() => Debug.Log(GetHealthStatus());
		[MenuItem(MENU + "Health/Log Health Events", priority = 81)]  private static void EventsMenu() => Debug.Log(GetHealthEvents(60));
		[MenuItem(MENU + "Health/Clear Health Events", priority = 82)] private static void ClearMenu() { HealthDebug.Clear(); Debug.Log("Health events cleared."); }
		[MenuItem(MENU + "Health/Print Health Events To Console", priority = 83)]
		private static void ToggleConsoleMenu() => HealthDebug.LogToConsole = !HealthDebug.LogToConsole;
		[MenuItem(MENU + "Health/Print Health Events To Console", true)]
		private static bool ValidateConsoleMenu()
		{
			UnityEditor.Menu.SetChecked(MENU + "Health/Print Health Events To Console", HealthDebug.LogToConsole);
			return true;
		}

		[MenuItem(MENU + "Dummies/Spawn Dummy Duo In Front", priority = 90)] private static void SpawnDuoMenu() => Debug.Log(SpawnDummyDuo(15.0f));
		[MenuItem(MENU + "Dummies/Spawn 22 Dummies Around", priority = 91)] private static void Spawn22Menu() => Debug.Log(SpawnDummies(22));
		[MenuItem(MENU + "Dummies/Remove All Dummies", priority = 92)]       private static void RemoveDummiesMenu() => Debug.Log(RemoveDummies());

		[MenuItem(MENU + "Health/Give Armor Tier 1", true)]
		[MenuItem(MENU + "Health/Give Armor Tier 2", true)]
		[MenuItem(MENU + "Health/Give Armor Tier 3", true)]
		[MenuItem(MENU + "Health/Remove Armor", true)]
		[MenuItem(MENU + "Health/Refill Healing Items", true)]
		[MenuItem(MENU + "Health/Damage Observed (30, bullet)", true)]
		[MenuItem(MENU + "Health/Down Observed", true)]
		[MenuItem(MENU + "Health/Log Health Status", true)]
		[MenuItem(MENU + "Dummies/Spawn Dummy Duo In Front", true)]
		[MenuItem(MENU + "Dummies/Spawn 22 Dummies Around", true)]
		[MenuItem(MENU + "Dummies/Remove All Dummies", true)]
		[MenuItem(MENU + "Health Check", true)]
		[MenuItem(MENU + "Performance Check", true)]
		private static bool ValidatePlaying() => Application.isPlaying;

		// SETUP

		/// <summary>Creates the HealthSettings asset (defaults = the Items, Gear & Inventory page) if missing and links it from GlobalSettings.</summary>
		public static string CreateHealthSettings()
		{
			var  settings = AssetDatabase.LoadAssetAtPath<HealthSettings>(SettingsPath);
			bool created  = false;

			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<HealthSettings>();
				AssetDatabase.CreateAsset(settings, SettingsPath);
				created = true;
			}

			string globalPath = AssetDatabase.FindAssets("t:GlobalSettings").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
			var    global     = globalPath != null ? AssetDatabase.LoadAssetAtPath<GlobalSettings>(globalPath) : null;
			if (global == null)
				return "GlobalSettings asset not found.";

			if (global.Health != settings)
			{
				global.Health = settings;
				EditorUtility.SetDirty(global);
			}

			AssetDatabase.SaveAssets();

			return $"{(created == true ? "Created" : "Found")} {SettingsPath}, linked from {globalPath}.";
		}

		// TOOLS

		public static string GiveArmor(int tier)
		{
			if (FindPlayers(out _, out Agent server, out _) == false)
				return "No observed player on a running host.";

			server.Health.DebugSetArmor(tier);
			return tier > 0 ? $"{server.name}: armor tier {tier} ({server.Health.ArmorLeft:F0})." : $"{server.name}: armor removed.";
		}

		public static string RefillHealItems()
		{
			if (FindPlayers(out _, out Agent server, out _) == false)
				return "No observed player on a running host.";

			server.Health.DebugRefillHealItems();
			return $"{server.name}: healing items refilled.";
		}

		/// <summary>A bullet body hit from no one (not a teammate) on the observed player.</summary>
		public static string DamageObserved(float amount)
		{
			if (FindPlayers(out _, out Agent server, out _) == false)
				return "No observed player on a running host.";

			float before = server.Health.IsDowned == true ? server.Health.DownedHealth : server.Health.CurrentHealth;
			ServerHit(server, amount, true, false, null);
			return $"{server.name}: {amount:F0} bullet damage, {before:F1} -> {(server.Health.IsDowned == true ? $"downed {server.Health.DownedHealth:F1}" : server.Health.CurrentHealth.ToString("F1"))}.";
		}

		/// <summary>Takes the observed player to 0 health (they go down if their teammate is up).</summary>
		public static string DownObserved()
		{
			if (FindPlayers(out _, out Agent server, out _) == false)
				return "No observed player on a running host.";
			if (server.Health.IsUp == false)
				return $"{server.name} isn't up.";

			ServerHit(server, server.Health.CurrentHealth + 1.0f, false, false, null);
			return server.Health.IsDowned == true ? $"{server.name} is down (bleed-out {server.Health.BleedOutLeft:F0} s)." : $"{server.name} died (no teammate up).";
		}

		public static string GetHealthStatus()
		{
			var builder = new StringBuilder();

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object == null || agent.Object.IsValid == false)
					continue;

				Health  health = agent.Health;
				KCC     kcc    = agent.Character.CharacterController;
				KCCData data   = kcc.IsProxy == true ? kcc.RenderData : kcc.FixedData;
				string  role   = agent.HasStateAuthority == true ? "server" : (agent.HasInputAuthority == true ? "local" : "proxy");

				builder.Append($"{agent.Runner.name} {agent.name} ({role}) team {agent.Team}: HP {health.CurrentHealth:F1}");
				builder.Append(health.ArmorTier > 0 ? $", armor T{health.ArmorTier} {health.ArmorLeft:F1}" : ", no armor");
				builder.Append(health.IsDowned == true ? $", DOWNED {health.DownedHealth:F1} bleed-out {health.BleedOutLeft:F2}{(health.IsBeingRevived ? " (being revived)" : "")}" : (health.IsAlive == false ? ", dead" : ""));
				builder.Append($", heals {health.GetHealItemCount(EHealItem.Bandage)}/{health.GetHealItemCount(EHealItem.MedKit)}/{health.GetHealItemCount(EHealItem.TraumaKit)} (Q: {health.EquippedHealItem})");
				builder.Append(health.IsHealing == true ? $", healing {health.HealProgress:P0}" : "");
				builder.Append(health.IsReviving == true ? $", reviving {health.ReviveProgress:P0}" : "");
				builder.Append($", mode {data.MoveMode} {data.Stance}, capsule {kcc.Settings.Height:F2}");
				if (agent.IsObserved == true)
				{
					builder.Append($", observed{(agent.Character.IsThirdPersonView ? " (third person)" : "")}");
				}

				builder.AppendLine();
			}

			return builder.ToString();
		}

		public static string GetHealthEvents(int last)
		{
			int start = Mathf.Max(0, HealthDebug.EventLog.Count - last);
			return HealthDebug.EventLog.Count > 0 ? string.Join("\n", HealthDebug.EventLog.Skip(start)) : "No health events.";
		}

		/// <summary>A dummy duo (their own team) standing this far in front of the observed player, facing them.</summary>
		public static string SpawnDummyDuo(float distance)
		{
			if (FindPlayers(out _, out Agent server, out _) == false)
				return "No observed player on a running host.";

			GameplayMode mode    = server.Context.GameplayMode;
			Quaternion   facing  = Quaternion.Euler(0.0f, server.Character.CharacterController.FixedData.LookYaw, 0.0f);
			Vector3      forward = facing * Vector3.forward;
			Vector3      right   = facing * Vector3.right;
			byte         team    = mode.NewDummyTeam();

			int spawned = 0;
			for (int i = 0; i < 2; ++i)
			{
				if (TryGetGround(server.Runner, server.transform.position + forward * distance + right * (i == 0 ? -0.8f : 0.8f), out Vector3 spot) == true && mode.SpawnDummy(spot, Quaternion.LookRotation(-forward), team) != null)
				{
					++spawned;
				}
			}

			return $"Spawned {spawned} dummies (team {team}) {distance:F0} m in front of {server.name}; {mode.DummyCount} dummies in all.";
		}

		/// <summary>Standing dummies in duos on rings around the observed player.</summary>
		public static string SpawnDummies(int count)
		{
			if (FindPlayers(out _, out Agent server, out _) == false)
				return "No observed player on a running host.";

			int spawned = SpawnRing(server, count);
			return $"Spawned {spawned} of {count} dummies around {server.name}; {server.Context.GameplayMode.DummyCount} dummies in all.";
		}

		public static string RemoveDummies()
		{
			Agent server = FindServerAgent();
			if (server == null)
				return "No running host.";

			return $"Removed {server.Context.GameplayMode.RemoveDummies()} dummies.";
		}

		// HEALTH CHECK

		private static readonly List<string>       _log     = new List<string>(256);
		private static readonly Stack<IEnumerator> _routine = new Stack<IEnumerator>();
		private static string _routineName;
		private static string _waitFailed;
		private static int    _passed;
		private static int    _checked;

		public static string RunHealthCheck()
		{
			if (Application.isPlaying == false)
				return "Enter Play mode first (GenArea5, 2 peers).";
			if (_routine.Count > 0)
				return $"{_routineName} is already running.";

			_log.Clear();
			StartRoutine("Health Check", HealthCheckRoutine());
			return "Health Check started (about 3 minutes). Read GetHealthCheckLog().";
		}

		public static string GetHealthCheckLog()
		{
			return (_routine.Count > 0 && _routineName == "Health Check" ? "(running)\n" : "") + string.Join("\n", _log);
		}

		private static IEnumerator HealthCheckRoutine()
		{
			if (FindPlayers(out Agent observed, out Agent server, out Agent other) == false)
			{
				Log("Need a running host with two players, viewed from the peer with input.");
				yield break;
			}

			PlayerRef     playerRef    = server.Object.InputAuthority;
			PlayerRef     teammateRef  = other.Object.InputAuthority;
			NetworkRunner viewRunner   = observed.Runner;
			NetworkRunner serverRunner = server.Runner;
			GameplayMode  mode         = server.Context.GameplayMode;
			HealthSettings settings    = HealthSettings.Current;

			_passed  = 0;
			_checked = 0;

			Log($"Health Check: observed {observed.name} on {(viewRunner.IsServer == true ? "the host" : "the client (lag compensation in play)")}, teammate {other.name}.");

			GameplayMode.DebugNoTeams = false;
			Health.DebugGodMode       = false;
			GunplayDebug.ZeroSpread   = true;
			GunplayDebug.NoRecoil     = true;
			HealthDebug.Clear();
			ReleaseInputs();

			try
			{
				// Layout: the two players side by side, a dummy duo 12 m ahead on clear level ground
				mode.RemoveDummies();

				if (FindClearSpot(server, mode, 12.0f, out Vector3 origin, out float yaw) == false)
				{
					Log("No open level ground 12 m across near the player or any spawn point; move somewhere open and run again.");
					yield break;
				}

				Quaternion facing  = Quaternion.Euler(0.0f, yaw, 0.0f);
				Vector3    forward = facing * Vector3.forward;
				Vector3    right   = facing * Vector3.right;

				PlaceAll(playerRef, origin, yaw);
				PlaceAll(teammateRef, Ground(serverRunner, origin - right * 2.0f), yaw);

				byte  dummyTeam = mode.NewDummyTeam();
				Agent dummyA    = mode.SpawnDummy(Ground(serverRunner, origin + forward * 12.0f), Quaternion.LookRotation(-forward), dummyTeam);
				Agent dummyB    = mode.SpawnDummy(Ground(serverRunner, origin + forward * 12.0f + right * 3.0f), Quaternion.LookRotation(-forward), dummyTeam);

				// Players spawn empty-handed since phase 5a: the real-bullet and fire-cancel steps need a gun in hand, and the revive
				// step checks the reviver keeps theirs
				foreach (PlayerRef armed in new[] { playerRef, teammateRef })
				{
					if (ServerCopy(armed).Weapons.CurrentWeapon is BulletWeapon == false)
					{
						ServerCopy(armed).Weapons.DebugGiveWeapon(ItemCatalog.GetGunPrefab(ItemCatalog.GetGunIndex("Icarus")));
					}
				}

				yield return WaitSeconds(2.0f);

				server = ServerCopy(playerRef);
				other  = ServerCopy(teammateRef);

				// 0. Teams
				Check(server.Team > 0 && server.Team == other.Team && dummyA.Team == dummyB.Team && dummyA.Team != server.Team,
					$"Teams: players {server.Team} and {other.Team} (join order), dummies {dummyA.Team} and {dummyB.Team}");

				// 1. Armor, server-side hits on a dummy (exact numbers)
				for (int tier = 1; tier <= 3; ++tier)
				{
					CheckArmorTier(dummyA, server, tier, settings);
				}

				// 2. Real bullets from the lagged client into a tier 2 dummy
				yield return CheckRealBullets(observed, playerRef, dummyA, settings);

				// 3. Healing on the observed player (predicted on the client, owned by the server)
				PlaceAll(playerRef, origin, yaw + 180.0f); // Facing away from everyone, for the fire-cancel shot
				yield return WaitSeconds(1.0f);

				yield return CheckHeal(playerRef, EHealItem.Bandage,   60.0f,  85.0f,  settings);
				yield return CheckHeal(playerRef, EHealItem.MedKit,    100.0f, 150.0f, settings);
				yield return CheckHeal(playerRef, EHealItem.TraumaKit, 150.0f, 200.0f, settings);
				yield return CheckHealRefused(playerRef);
				yield return CheckHealCancel(playerRef, "let go", origin, yaw + 180.0f);
				yield return CheckHealCancel(playerRef, "sprint", origin, yaw + 180.0f);
				yield return CheckHealCancel(playerRef, "fire",   origin, yaw + 180.0f);
				yield return CheckHealCancel(playerRef, "jump",   origin, yaw + 180.0f);
				yield return CheckHealCrouched(playerRef, origin, yaw + 180.0f, settings);

				// 4. Friendly fire
				server = ServerCopy(playerRef);
				other  = ServerCopy(teammateRef);
				server.Health.DebugSetHealth(150.0f);
				other.Health.DebugSetHealth(150.0f);
				ServerHit(server, 30.0f, true, false, other);
				ServerHit(other,  30.0f, true, false, server);
				Check(Mathf.Approximately(server.Health.CurrentHealth, 150.0f) && Mathf.Approximately(other.Health.CurrentHealth, 150.0f),
					$"Friendly fire: a teammate's 30-damage bullet does nothing ({server.Health.CurrentHealth:F0} / {other.Health.CurrentHealth:F0})");

				// 5. Downed, crawl, third-person camera, revive
				PlaceAll(playerRef, origin, yaw);
				PlaceAll(teammateRef, Ground(serverRunner, origin - right * 2.0f), yaw);
				yield return WaitSeconds(1.0f);

				yield return CheckDowned(playerRef, viewRunner, dummyA, settings, 1);
				yield return CheckCrawlAndNoFire(playerRef, settings);
				yield return CheckRevive(playerRef, teammateRef, viewRunner, settings, true);

				// 6. Second down: 20 s bleed-out, then spectate the teammate
				yield return CheckDowned(playerRef, viewRunner, dummyA, settings, 2);
				yield return CheckBleedOut(playerRef, teammateRef, viewRunner, settings, 2);

				yield return WaitForRespawn(playerRef, mode);

				// 7. After respawn: the down count starts again; a revive that's let go; 50 damage finishes
				PlaceAll(playerRef, origin, yaw);
				PlaceAll(teammateRef, Ground(serverRunner, origin - right * 2.0f), yaw);
				yield return WaitSeconds(1.5f);

				yield return CheckDowned(playerRef, viewRunner, dummyA, settings, 1);
				yield return CheckRevive(playerRef, teammateRef, viewRunner, settings, false);
				CheckFinish(playerRef, dummyA, settings);

				yield return WaitForRespawn(playerRef, mode);

				// 8. Team out: players, then dummies
				PlaceAll(playerRef, origin, yaw);
				PlaceAll(teammateRef, Ground(serverRunner, origin - right * 2.0f), yaw);
				yield return WaitSeconds(1.5f);

				CheckTeamOut(ServerCopy(playerRef), ServerCopy(teammateRef), dummyA, "Players");

				yield return WaitForRespawn(playerRef, mode);
				yield return WaitForRespawn(teammateRef, mode);

				dummyA = mode.GetDummies().FirstOrDefault(t => t.Team == dummyTeam && t.Health.IsUp == true);
				dummyB = mode.GetDummies().LastOrDefault(t => t.Team == dummyTeam && t.Health.IsUp == true && t != dummyA);
				if (dummyA != null && dummyB != null)
				{
					CheckTeamOut(dummyA, dummyB, ServerCopy(playerRef), "Dummies");
				}
				else
				{
					Check(false, "Dummies: team out (dummy duo not found)");
				}

				Log($"{_passed} of {_checked} checks pass.");
				Log($"Events:\n{GetHealthEvents(400)}");
			}
			finally
			{
				ReleaseInputs();
				SetInputPeer(viewRunner);
				GunplayDebug.ZeroSpread = false;
				GunplayDebug.NoRecoil   = false;
			}
		}

		private static void CheckArmorTier(Agent dummy, Agent instigator, int tier, HealthSettings settings)
		{
			HealthSettings.ArmorTier armor  = settings.GetArmorTier(tier);
			Health                   health = dummy.Health;

			health.DebugSetArmor(tier);

			health.DebugSetHealth(150.0f);
			ServerHit(dummy, 30.0f, true, false, instigator);
			float body      = health.CurrentHealth;
			float bodyArmor = health.ArmorLeft;

			health.DebugSetHealth(150.0f);
			float headArmorBefore = health.ArmorLeft;
			ServerHit(dummy, 30.0f, true, true, instigator);
			float head      = health.CurrentHealth;
			float headArmor = health.ArmorLeft;

			health.DebugSetHealth(150.0f);
			float punchArmorBefore = health.ArmorLeft;
			ServerHit(dummy, 50.0f, false, false, instigator);
			float punch      = health.CurrentHealth;
			float punchArmor = health.ArmorLeft;

			float expectedBody = 150.0f - 30.0f * (1.0f - armor.BodyCut);
			float expectedHead = 150.0f - 30.0f * (1.0f - armor.HeadCut);
			float expectedHeadArmor = headArmorBefore - (armor.HeadCut > 0.0f ? 30.0f : 0.0f);

			// Break: 100-damage body bullets until it goes; the breaking hit is still cut, the next one isn't
			health.DebugSetArmor(tier);
			int   hits        = 0;
			float breakHealth = 0.0f;
			while (health.ArmorTier > 0 && hits < 20)
			{
				health.DebugSetHealth(150.0f);
				ServerHit(dummy, 100.0f, true, false, instigator);
				++hits;
				breakHealth = health.CurrentHealth;
			}

			health.DebugSetHealth(150.0f);
			ServerHit(dummy, 100.0f, true, false, instigator);
			float afterBreak = health.CurrentHealth;

			int  expectedHits = Mathf.CeilToInt(armor.Durability / 100.0f - 0.0001f);
			bool ok =
				Near(body, expectedBody) && Near(bodyArmor, armor.Durability - 30.0f) &&
				Near(head, expectedHead) && Near(headArmor, expectedHeadArmor) &&
				Near(punch, 100.0f) && Near(punchArmor, punchArmorBefore) &&
				hits == expectedHits && Near(breakHealth, 150.0f - 100.0f * (1.0f - armor.BodyCut)) && Near(afterBreak, 50.0f);

			Check(ok, $"Armor tier {tier}: body bullet 30 -> {150.0f - body:F1} (expected {150.0f - expectedBody:F1}), armor {armor.Durability:F0} -> {bodyArmor:F0}; " +
				$"head bullet 30 -> {150.0f - head:F1} (expected {150.0f - expectedHead:F1}), armor {headArmorBefore:F0} -> {headArmor:F0}; punch 50 -> {150.0f - punch:F0}, armor unchanged {Near(punchArmor, punchArmorBefore)}; " +
				$"breaks on hit {hits} of 100 (expected {expectedHits}), that hit took {150.0f - breakHealth:F0}, the next {150.0f - afterBreak:F0}");

			health.DebugSetArmor(0);
			health.DebugSetHealth(150.0f);
		}

		private static IEnumerator CheckRealBullets(Agent observed, PlayerRef playerRef, Agent dummy, HealthSettings settings)
		{
			Agent        server = ServerCopy(playerRef);
			BulletWeapon gun    = server.Weapons.CurrentWeapon as BulletWeapon;
			if (gun == null || gun.GunData == null)
			{
				Check(false, "Real bullets: the observed player has no gun in hand");
				yield break;
			}

			dummy.Health.DebugSetArmor(2);
			dummy.Health.DebugSetHealth(150.0f);

			HealthSettings.ArmorTier armor = settings.GetArmorTier(2);
			GunData data  = gun.GunData;
			float   cut   = armor.BodyCut;

			// Body
			LookAll(playerRef, dummy.transform.position + Vector3.up * 1.2f);
			yield return WaitSeconds(1.0f);

			// Shots are counted on the shooting peer's own copy: the server's count arrives a round trip later, and under lag the extra
			// shots fired meanwhile could down the dummy before the head shot
			int logStart = HealthDebug.EventLog.Count;
			int shots    = observed.Weapons.CurrentWeapon is BulletWeapon own ? own.ShotCount : gun.ShotCount;
			AgentInput.DebugHoldAttack = true;
			yield return WaitFor(() => observed.Weapons.CurrentWeapon is BulletWeapon g && g.ShotCount >= shots + 3, 3.0f, "3 shots");
			AgentInput.DebugHoldAttack = false;
			yield return WaitSeconds(1.0f);

			List<string> bodyHits = HealthDebug.EventLog.Skip(logStart).Where(t => t.StartsWith($"HIT {dummy.name} bullet body")).ToList();
			string expectedBody   = $"{data.Damage:F1} -> {data.Damage * (1.0f - cut):F1}";
			Check(bodyHits.Count >= 3 && bodyHits.All(t => t.Contains(expectedBody)), $"Real bullets, {data.GunName} into a tier 2 dummy's body: {bodyHits.Count} hits, each {(bodyHits.Count > 0 ? Between(bodyHits[0], "bullet body ", ",") : "-")} (expected {expectedBody})");

			// Head (tier 2 doesn't cover it). Standing and at full health first, in case the body shots took it down
			if (dummy.Health.IsDowned == true)
			{
				dummy.Health.Revive();
			}

			dummy.Health.DebugSetHealth(150.0f);

			BodyPart head = dummy.GetComponentsInChildren<BodyPart>().FirstOrDefault(t => t.IsCritical == true);
			if (head != null)
			{
				LookAll(playerRef, head.transform.TransformPoint(head.Offset));
				yield return WaitSeconds(1.0f);

				logStart = HealthDebug.EventLog.Count;
				shots    = observed.Weapons.CurrentWeapon is BulletWeapon ownGun ? ownGun.ShotCount : 0;
				AgentInput.DebugHoldAttack = true;
				yield return WaitFor(() => observed.Weapons.CurrentWeapon is BulletWeapon g && g.ShotCount >= shots + 1, 3.0f, "1 shot");
				AgentInput.DebugHoldAttack = false;
				yield return WaitSeconds(1.0f);

				string headHit      = HealthDebug.EventLog.Skip(logStart).FirstOrDefault(t => t.StartsWith($"HIT {dummy.name} bullet head"));
				float  headDamage   = data.Damage * data.HeadMultiplier;
				string expectedHead = $"{headDamage:F1} -> {headDamage:F1}";
				Check(headHit != null && headHit.Contains(expectedHead), $"Real bullets, a head shot on the tier 2 dummy isn't cut: {(headHit != null ? Between(headHit, "bullet head ", ",") : "no head hit")} (expected {expectedHead})");
			}

			dummy.Health.DebugSetArmor(0);
			dummy.Health.DebugSetHealth(150.0f);
		}

		private static IEnumerator CheckHeal(PlayerRef playerRef, EHealItem item, float startHealth, float expectedHealth, HealthSettings settings)
		{
			Agent server = ServerCopy(playerRef);
			HealthSettings.HealItem data = settings.GetHealItem(item);

			server.Health.DebugRefillHealItems();
			server.Health.DebugEquipHealItem(item);
			server.Health.DebugSetHealth(startHealth);
			yield return WaitSeconds(0.8f);

			int countBefore = server.Health.GetHealItemCount(item);
			int logStart    = HealthDebug.EventLog.Count;

			AgentInput.DebugHoldHeal = true;
			yield return WaitFor(() => FindEvent(logStart, $"HEAL {ServerCopy(playerRef).name} {data.Name} done") != null, data.UseTime + 4.0f, $"{data.Name} done");
			AgentInput.DebugHoldHeal = false;

			server = ServerCopy(playerRef);
			string done   = FindEvent(logStart, $"HEAL {server.name} {data.Name} done");
			float  time   = done != null ? ParseFloat(Between(done, "done after ", " s")) : -1.0f;
			float  health = done != null ? ParseFloat(Between(done, "-> ", ",")) : -1.0f;

			yield return WaitSeconds(0.5f);

			Check(done != null && Mathf.Abs(time - data.UseTime) <= TICK + 0.001f && Near(health, expectedHealth) && server.Health.GetHealItemCount(item) == countBefore - 1,
				$"Heal {data.Name}: {startHealth:F0} -> {health:F0} (expected {expectedHealth:F0}) in {time:F3} s (data {data.UseTime:F1}), {countBefore} -> {server.Health.GetHealItemCount(item)} left");
		}

		private static IEnumerator CheckHealRefused(PlayerRef playerRef)
		{
			Agent server = ServerCopy(playerRef);
			server.Health.DebugEquipHealItem(EHealItem.Bandage);
			yield return WaitSeconds(0.5f);

			int   logStart = HealthDebug.EventLog.Count;
			float before   = server.Health.CurrentHealth;

			AgentInput.DebugHoldHeal = true;
			yield return WaitSeconds(1.5f);
			AgentInput.DebugHoldHeal = false;
			yield return WaitSeconds(0.3f);

			bool started = FindEvent(logStart, $"HEAL {server.name} Bandage start") != null;
			Check(started == false && Near(server.Health.CurrentHealth, before), $"Bandage at {before:F0} health: refused (only the trauma kit heals above 150), health stays {server.Health.CurrentHealth:F0}");
		}

		private static IEnumerator CheckHealCancel(PlayerRef playerRef, string cancel, Vector3 position, float yaw)
		{
			PlaceAll(playerRef, position, yaw);

			Agent server = ServerCopy(playerRef);
			server.Health.DebugRefillHealItems();
			server.Health.DebugEquipHealItem(EHealItem.Bandage);
			server.Health.DebugSetHealth(100.0f);
			yield return WaitSeconds(1.0f);

			int count    = server.Health.GetHealItemCount(EHealItem.Bandage);
			int logStart = HealthDebug.EventLog.Count;

			AgentInput.DebugHoldHeal = true;
			yield return WaitFor(() => FindEvent(logStart, $"HEAL {server.name} Bandage start") != null, 2.0f, "heal start");
			yield return WaitSeconds(0.3f);

			switch (cancel)
			{
				case "let go": AgentInput.DebugHoldHeal = false; break;
				case "sprint": AgentInput.DebugMoveDirection = Vector2.up; AgentInput.DebugHoldSprint = true; break;
				case "fire":   AgentInput.DebugHoldAttack = true; break;
				case "jump":   AgentInput.DebugHoldJump = true; break;
			}

			// An editor stall can drop the client's inputs for a tick and read as letting go, so look for this cancel's own reason
			yield return WaitFor(() => FindEvent(logStart, $"HEAL {server.name} Bandage cancelled ({cancel})") != null, 2.0f, "cancel");
			ReleaseInputs();
			yield return WaitSeconds(0.8f);

			string cancelled = FindEvent(logStart, $"HEAL {server.name} Bandage cancelled ({cancel})");
			string reason    = cancelled != null ? Between(cancelled, "cancelled (", ")") : "none";
			bool   healed    = FindEvent(logStart, $"HEAL {server.name} Bandage done") != null;

			Check(cancelled != null && reason == cancel && healed == false && server.Health.GetHealItemCount(EHealItem.Bandage) == count && Near(server.Health.CurrentHealth, 100.0f),
				$"Heal cancel by {cancel}: cancelled ({reason}), bandages {count} -> {server.Health.GetHealItemCount(EHealItem.Bandage)}, health {server.Health.CurrentHealth:F0}");
		}

		private static IEnumerator CheckHealCrouched(PlayerRef playerRef, Vector3 position, float yaw, HealthSettings settings)
		{
			PlaceAll(playerRef, position, yaw);

			Agent server = ServerCopy(playerRef);
			server.Health.DebugRefillHealItems();
			server.Health.DebugEquipHealItem(EHealItem.Bandage);
			server.Health.DebugSetHealth(100.0f);
			yield return WaitSeconds(1.0f);

			int logStart = HealthDebug.EventLog.Count;
			AgentInput.DebugHoldCrouch = true;
			yield return WaitSeconds(0.5f);
			AgentInput.DebugHoldHeal = true;
			yield return WaitFor(() => FindEvent(logStart, $"HEAL {server.name} Bandage done") != null, 3.0f, "crouched heal");
			ReleaseInputs();
			yield return WaitSeconds(0.5f);

			bool done = FindEvent(logStart, $"HEAL {server.name} Bandage done") != null;
			Check(done == true && Near(server.Health.CurrentHealth, 125.0f), $"Heal while crouched: {(done ? "done" : "not done")}, health {server.Health.CurrentHealth:F0} (expected 125)");
		}

		private static IEnumerator CheckDowned(PlayerRef playerRef, NetworkRunner viewRunner, Agent enemy, HealthSettings settings, int downNumber)
		{
			Agent server = ServerCopy(playerRef);
			server.Health.DebugSetArmor(0);
			server.Health.DebugSetHealth(150.0f);
			yield return WaitSeconds(0.5f);

			// From the dummy (an enemy): a non-bullet hit of everything left
			double downTime = EditorApplication.timeSinceStartup;
			ServerHit(server, 160.0f, false, false, enemy);

			Health health = server.Health;
			float  bleed  = settings.GetBleedOutTime(downNumber);
			Check(health.IsDowned == true && health.DownCount == downNumber && Near(health.BleedOutLeft, bleed, 0.1f) && Near(health.DownedHealth, settings.DownedHealth),
				$"Down {downNumber}: downed {health.IsDowned} (teammate up), down count {health.DownCount}, bleed-out {health.BleedOutLeft:F2} s (data {bleed:F0}), downed health {health.DownedHealth:F0}");

			if (downNumber != 1 || health.IsDowned == false)
				yield break;

			// The view: over the shoulder after the delay, no viewmodel, body shown
			Agent  view           = ViewCopy(playerRef, viewRunner);
			double seenDowned     = -1.0;
			double thirdPerson    = -1.0;
			yield return WaitFor(() =>
			{
				view = ViewCopy(playerRef, viewRunner);
				if (view == null)
					return false;
				if (seenDowned < 0.0 && view.Health.IsDowned == true)
				{
					seenDowned = EditorApplication.timeSinceStartup;
				}
				if (thirdPerson < 0.0 && view.Character.IsThirdPersonView == true)
				{
					thirdPerson = EditorApplication.timeSinceStartup;
				}
				return thirdPerson > 0.0;
			}, 3.0f, "third-person camera");

			yield return WaitSeconds(0.5f);

			KCC     kcc  = server.Character.CharacterController;
			KCCData data = kcc.FixedData;
			float   headHeight = GetHeadHeight(server);
			Check(thirdPerson > 0.0 && data.MoveMode == EMoveMode.Downed && data.Stance == EStance.Downed && Near(kcc.Settings.Height, MovementSettings.Current.DownedHeight) && headHeight < MovementSettings.Current.DownedHeight,
				$"Downed view and body: over the shoulder {(thirdPerson > 0.0 ? $"{(thirdPerson - downTime) * 1000.0:F0} ms after the server's down ({(thirdPerson - seenDowned) * 1000.0:F0} ms after the client saw it)" : "never")}, " +
				$"mode {data.MoveMode} {data.Stance}, capsule {kcc.Settings.Height:F2} m, head hitbox {headHeight:F2} m above the feet");
		}

		private static IEnumerator CheckCrawlAndNoFire(PlayerRef playerRef, HealthSettings settings)
		{
			// Crawl forward at crouch speed
			AgentInput.DebugMoveDirection = Vector2.up;
			AgentInput.DebugHoldSprint    = true; // Sprint does nothing while downed
			yield return WaitSeconds(1.2f);

			// Median of the frames, so a bump in the ground or one dropped input doesn't skew it
			var    samples = new List<float>(64);
			double end     = EditorApplication.timeSinceStartup + 0.6;
			while (EditorApplication.timeSinceStartup < end)
			{
				samples.Add(ServerCopy(playerRef).Character.CharacterController.FixedData.RealVelocity.OnlyXZ().magnitude);
				yield return null;
			}

			ReleaseInputs();
			samples.Sort();
			float speed    = samples.Count > 0 ? samples[samples.Count / 2] : 0.0f;
			float expected = MovementSettings.Current.CrouchSpeed;
			Check(Near(speed, expected, 0.1f), $"Crawl (holding sprint too): {speed:F2} m/s (crouch speed {expected:F2})");

			// No firing, aiming or swapping while downed
			Agent        server = ServerCopy(playerRef);
			BulletWeapon gun    = server.Weapons.CurrentWeapon as BulletWeapon;
			int          shots  = gun != null ? gun.ShotCount : 0;
			int          slot   = server.Weapons.CurrentWeaponSlot;

			AgentInput.DebugHoldAttack = true;
			AgentInput.DebugHoldAim    = true;
			AgentInput.DebugSwitchSlot = slot == 1 ? 2 : 1;
			yield return WaitSeconds(1.0f);
			ReleaseInputs();

			server = ServerCopy(playerRef);
			gun    = server.Weapons.CurrentWeapon as BulletWeapon;
			Check((gun != null ? gun.ShotCount : 0) == shots && server.Character.CharacterController.FixedData.Aim == false && server.Weapons.PendingWeaponSlot == slot,
				$"Downed: no shots ({shots} -> {(gun != null ? gun.ShotCount : 0)}), no aim, no swap (slot {slot} -> {server.Weapons.PendingWeaponSlot})");
		}

		private static IEnumerator CheckRevive(PlayerRef playerRef, PlayerRef teammateRef, NetworkRunner viewRunner, HealthSettings settings, bool complete)
		{
			Agent downed = ServerCopy(playerRef);
			Agent mate   = ServerCopy(teammateRef);

			// The teammate kneels in: 1.2 m to the side, looking at the downed player
			Vector3 side = Ground(mate.Runner, downed.transform.position + Quaternion.Euler(0.0f, mate.Character.CharacterController.FixedData.LookYaw, 0.0f) * Vector3.left * 1.2f);
			PlaceAll(teammateRef, side, 0.0f);
			yield return WaitSeconds(0.2f);
			LookAll(teammateRef, downed.transform.position + Vector3.up * 0.3f);

			// Input to the host's peer (the teammate), like Numpad 1
			SetInputPeer(mate.Runner);
			yield return WaitSeconds(1.0f);

			int logStart = HealthDebug.EventLog.Count;
			AgentInput.DebugHoldInteract = true;

			if (complete == true)
			{
				yield return WaitFor(() => FindEvent(logStart, $"REVIVE {ServerCopy(teammateRef).name} -> {downed.name} done") != null, 6.0f, "revive done");
			}
			else
			{
				yield return WaitFor(() => FindEvent(logStart, $"REVIVE {ServerCopy(teammateRef).name} -> {downed.name} start") != null, 3.0f, "revive start");
				yield return WaitSeconds(1.0f);
			}

			AgentInput.DebugHoldInteract = false;
			yield return WaitSeconds(0.6f);

			SetInputPeer(viewRunner);
			yield return WaitSeconds(0.5f);

			mate = ServerCopy(teammateRef);
			string start = FindEvent(logStart, $"REVIVE {mate.name} -> {downed.name} start");

			if (complete == true)
			{
				string done      = FindEvent(logStart, $"REVIVE {mate.name} -> {downed.name} done");
				float  time      = done  != null ? ParseFloat(Between(done, "done after ", " s")) : -1.0f;
				float  bleedFrom = start != null ? ParseFloat(Between(start, "bleed-out left ", " s")) : -1.0f;
				float  bleedTo   = done  != null ? ParseFloat(Between(done, "bleed-out left ", " s")) : -2.0f;
				bool   dropped   = mate.Weapons.CurrentWeapon == null;

				Check(done != null && Mathf.Abs(time - settings.ReviveTime) <= TICK + 0.001f && Near(downed.Health.CurrentHealth, settings.RevivedHealth) && downed.Health.IsUp == true && Mathf.Abs(bleedFrom - bleedTo) <= TICK * 2.0f && dropped == false,
					$"Revive (F held by the teammate): {time:F3} s (data {settings.ReviveTime:F1}), back at {downed.Health.CurrentHealth:F0} HP (data {settings.RevivedHealth:F0}), bleed-out paused {bleedFrom:F2} -> {bleedTo:F2} s, reviver kept the gun {dropped == false}");

				yield return WaitSeconds(0.8f);
				Agent view = ViewCopy(playerRef, viewRunner);
				Check(view != null && view.Health.IsUp == true && view.Character.IsThirdPersonView == false && view.Character.CharacterController.FixedData.MoveMode != EMoveMode.Downed,
					$"After the revive on the client: up {view != null && view.Health.IsUp}, first person again {view != null && view.Character.IsThirdPersonView == false}, mode {(view != null ? view.Character.CharacterController.FixedData.MoveMode.ToString() : "-")}");
			}
			else
			{
				string cancelled = FindEvent(logStart, $"REVIVE {mate.name} cancelled (let go)");
				Check(start != null && cancelled != null && downed.Health.IsDowned == true, $"Revive let go after 1 s: cancelled {cancelled != null}, still downed {downed.Health.IsDowned}");
			}
		}

		private static IEnumerator CheckBleedOut(PlayerRef playerRef, PlayerRef teammateRef, NetworkRunner viewRunner, HealthSettings settings, int downNumber)
		{
			Agent server   = ServerCopy(playerRef);
			int   logStart = HealthDebug.EventLog.Count;
			float bleed    = settings.GetBleedOutTime(downNumber);

			yield return WaitFor(() => FindEvent(logStart, $"BLEED OUT {server.name}") != null, bleed + 5.0f, "bleed-out");

			string line = FindEvent(logStart, $"BLEED OUT {server.name}");
			float  time = line != null ? ParseFloat(Between(line, "after ", " s downed")) : -1.0f;

			yield return WaitSeconds(1.5f);

			// Spectating: the client now looks through the teammate
			Agent  view      = ViewCopy(playerRef, viewRunner);
			var    context   = view != null ? view.Context : null;
			bool   spectates = context != null && context.ObservedPlayerRef == teammateRef;

			Check(line != null && Mathf.Abs(time - bleed) <= TICK + 0.001f && server.Health.IsAlive == false,
				$"Bleed-out (down {downNumber}): dead after {time:F3} s (data {bleed:F0})");
			Check(spectates == true, $"Spectate: after dying, the client watches {(context != null ? context.ObservedPlayerRef.ToString() : "-")} (teammate {teammateRef})");
		}

		private static void CheckFinish(PlayerRef playerRef, Agent enemy, HealthSettings settings)
		{
			Agent server = ServerCopy(playerRef);

			ServerHit(server, 30.0f, true, false, enemy);
			float afterFirst = server.Health.DownedHealth;
			bool  stillDown  = server.Health.IsDowned;

			ServerHit(server, settings.DownedHealth - 30.0f, true, false, enemy);

			Check(stillDown == true && Near(afterFirst, settings.DownedHealth - 30.0f) && server.Health.IsAlive == false,
				$"Finishing a downed player: 30 damage leaves {afterFirst:F0} of {settings.DownedHealth:F0} downed health, {settings.DownedHealth - 30.0f:F0} more kills ({(server.Health.IsAlive ? "alive" : "dead")})");
		}

		private static void CheckTeamOut(Agent first, Agent second, Agent enemy, string label)
		{
			first.Health.DebugSetHealth(150.0f);
			second.Health.DebugSetHealth(150.0f);

			int logStart = HealthDebug.EventLog.Count;

			ServerHit(first, 160.0f, false, false, enemy);
			bool firstDowned = first.Health.IsDowned;

			ServerHit(second, 160.0f, false, false, enemy);
			bool secondDowned = second.Health.IsDowned;

			bool teamOut = FindEvent(logStart, "TEAM OUT") != null;
			Check(firstDowned == true && secondDowned == false && first.Health.IsAlive == false && second.Health.IsAlive == false && teamOut == true,
				$"{label}: team out: {first.name} downed {firstDowned}; {second.name} (last one up) dies instead of going down {secondDowned == false && second.Health.IsAlive == false}; then {first.name} dies too {first.Health.IsAlive == false}");
		}

		private static IEnumerator WaitForRespawn(PlayerRef playerRef, GameplayMode mode)
		{
			yield return WaitFor(() => { Agent agent = ServerCopy(playerRef); return agent != null && agent.Health.IsUp == true; }, mode.RespawnTime + 10.0f, $"{playerRef} respawn");
			yield return WaitSeconds(1.5f);
		}

		// PERFORMANCE CHECK

		private static readonly List<string> _perfLog = new List<string>(64);

		/// <summary>
		/// Frame rate and the host's simulation time per tick: baselineSeconds with the 2 players, then seconds with 22 standing dummies
		/// around them. Read GetPerformanceLog(); also written to Logs/Performance/PerformanceCheck.txt.
		/// </summary>
		public static string RunPerformanceCheck(float baselineSeconds, float seconds)
		{
			if (Application.isPlaying == false)
				return "Enter Play mode first (GenArea5, 2 peers).";
			if (_routine.Count > 0)
				return $"{_routineName} is already running.";

			_perfLog.Clear();
			StartRoutine("Performance Check", PerformanceRoutine(baselineSeconds, seconds));
			return $"Performance Check started ({baselineSeconds + seconds + 10.0f:F0} s). Read GetPerformanceLog().";
		}

		public static string GetPerformanceLog()
		{
			return (_routine.Count > 0 && _routineName == "Performance Check" ? "(running)\n" : "") + string.Join("\n", _perfLog);
		}

		private static IEnumerator PerformanceRoutine(float baselineSeconds, float seconds)
		{
			if (FindPlayers(out Agent observed, out Agent server, out _) == false)
			{
				_perfLog.Add("Need a running host with two players.");
				yield break;
			}

			GameplayMode     mode  = server.Context.GameplayMode;
			PerformanceProbe probe = PerformanceProbe.Attach(server.Runner);

			mode.RemoveDummies();

			_perfLog.Add($"Performance Check {System.DateTime.Now:yyyy-MM-dd HH:mm}: scene {UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(0).path}, {NetworkRunner.Instances.Count} peer(s) in the editor, viewed from {(observed.Runner.IsServer ? "the host" : "the client")}, " +
				$"tick rate {1.0f / server.Runner.DeltaTime:F0} Hz, screen {Screen.width}x{Screen.height}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, vSync {QualitySettings.vSyncCount}.");

			yield return WaitSeconds(3.0f);

			probe.Begin();
			yield return WaitSeconds(baselineSeconds);
			probe.End();
			_perfLog.Add(Summarize("Baseline, 2 players", probe));

			int spawned = SpawnRing(server, 22);
			_perfLog.Add($"Spawned {spawned} standing dummies (11 duos) around the players; {CountAgents(server.Runner)} agents on the host.");

			yield return WaitSeconds(5.0f);

			probe.Begin();
			yield return WaitSeconds(seconds);
			probe.End();
			_perfLog.Add(Summarize($"With {spawned} dummies", probe));

			mode.RemoveDummies();

			System.IO.Directory.CreateDirectory(LOG_DIR);
			System.IO.File.WriteAllText($"{LOG_DIR}/PerformanceCheck.txt", string.Join("\n", _perfLog));
			_perfLog.Add($"Written to {LOG_DIR}/PerformanceCheck.txt.");
		}

		private static string Summarize(string label, PerformanceProbe probe)
		{
			List<float> frames = probe.FrameTimes.Where(t => t > 0.0f).ToList();
			List<float> ticks  = probe.TickTimes.ToList();
			if (frames.Count == 0)
				return $"{label}: no frames recorded.";

			frames.Sort();
			float total   = frames.Sum();
			float average = frames.Count / total;
			int   worst   = Mathf.Max(1, frames.Count / 100);
			float low1    = worst / frames.Skip(frames.Count - worst).Sum();
			float median  = 1.0f / frames[frames.Count / 2];
			float longest = frames[frames.Count - 1] * 1000.0f;

			string tickText = "no ticks recorded";
			if (ticks.Count > 0)
			{
				ticks.Sort();
				tickText = $"host simulation {ticks.Average():F2} ms per tick (median {ticks[ticks.Count / 2]:F2}, 99th percentile {ticks[Mathf.Min(ticks.Count - 1, ticks.Count * 99 / 100)]:F2}, max {ticks[ticks.Count - 1]:F2}) over {ticks.Count} ticks";
			}

			return $"{label}: {average:F1} fps average (median {median:F1}, 1% low {low1:F1}, longest frame {longest:F0} ms) over {frames.Count} frames; {tickText}.";
		}

		private static int SpawnRing(Agent center, int count)
		{
			GameplayMode mode    = center.Context.GameplayMode;
			int          duos    = Mathf.CeilToInt(count / 2.0f);
			int          spawned = 0;

			for (int i = 0; i < duos && spawned < count; ++i)
			{
				byte    team   = mode.NewDummyTeam();
				float   angle  = i * 360.0f / duos;
				float   radius = 10.0f + (i % 3) * 7.0f;
				Vector3 dir    = Quaternion.Euler(0.0f, angle, 0.0f) * Vector3.forward;
				Vector3 side   = Vector3.Cross(Vector3.up, dir);

				for (int j = 0; j < 2 && spawned < count; ++j)
				{
					// Blocked (a wall, a tree): try further out
					for (int attempt = 0; attempt < 8; ++attempt)
					{
						Vector3 wanted = center.transform.position + dir * (radius + attempt * 2.5f) + side * (j == 0 ? -0.8f : 0.8f);
						if (TryGetGround(center.Runner, wanted, out Vector3 spot) == false)
							continue;

						if (mode.SpawnDummy(spot, Quaternion.LookRotation(-dir), team) != null)
						{
							++spawned;
						}

						break;
					}
				}
			}

			return spawned;
		}

		// PICTURES

		/// <summary>
		/// Downs the observed player and saves two pictures to Logs/HealthCheck/: their own over-the-shoulder view, and the other peer's
		/// view of the crawl pose from 3 m. Then revives them. Needs the teammate up.
		/// </summary>
		public static string CaptureDownedViews()
		{
			if (Application.isPlaying == false)
				return "Enter Play mode first.";
			if (_routine.Count > 0)
				return $"{_routineName} is already running.";

			_log.Clear();
			StartRoutine("Downed Pictures", CaptureRoutine());
			return "Capturing; read GetHealthCheckLog().";
		}

		private static IEnumerator CaptureRoutine()
		{
			if (FindPlayers(out Agent observed, out Agent server, out Agent other) == false || other == null)
			{
				Log("Need a running host with two players.");
				yield break;
			}

			PlayerRef     playerRef   = server.Object.InputAuthority;
			PlayerRef     teammateRef = other.Object.InputAuthority;
			NetworkRunner viewRunner  = observed.Runner;

			float   yaw     = server.Character.CharacterController.FixedData.LookYaw;
			Vector3 right   = Quaternion.Euler(0.0f, yaw, 0.0f) * Vector3.right;
			Vector3 watcher = Ground(server.Runner, server.transform.position + right * 3.0f);

			PlaceAll(teammateRef, watcher, yaw - 90.0f);
			yield return WaitSeconds(1.0f);

			ServerHit(server, server.Health.CurrentHealth + 10.0f, false, false, null);
			yield return WaitSeconds(1.5f);

			System.IO.Directory.CreateDirectory("Logs/HealthCheck");

			Agent view = ViewCopy(playerRef, viewRunner);
			SaveCapture(view.Context.Camera.Camera, "Logs/HealthCheck/Downed_OwnView.png");
			Log($"Own view: third person {view.Character.IsThirdPersonView}, downed {view.Health.IsDowned}.");

			// The teammate's peer looks at the crawling player
			Agent mate = ServerCopy(teammateRef);
			LookAll(teammateRef, server.transform.position + Vector3.up * 0.3f);
			SetInputPeer(mate.Runner);
			yield return WaitSeconds(1.0f);

			Agent mateView = ViewCopy(teammateRef, mate.Runner);
			SaveCapture(mateView.Context.Camera.Camera, "Logs/HealthCheck/Downed_SeenByTeammate.png");
			Log("Saved Logs/HealthCheck/Downed_OwnView.png and Downed_SeenByTeammate.png.");

			SetInputPeer(viewRunner);
			ServerCopy(playerRef).Health.Revive();
		}

		private static void SaveCapture(Camera camera, string path)
		{
			const int width  = 960;
			const int height = 540;

			RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
			try
			{
				var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = target };
				if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request) == true)
				{
					UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
				}
				else
				{
					RenderTexture previous = camera.targetTexture;
					camera.targetTexture = target;
					camera.Render();
					camera.targetTexture = previous;
				}

				RenderTexture active = RenderTexture.active;
				RenderTexture.active = target;
				var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
				picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				picture.Apply();
				RenderTexture.active = active;

				System.IO.File.WriteAllBytes(path, picture.EncodeToPNG());
				Object.DestroyImmediate(picture);
			}
			finally
			{
				RenderTexture.ReleaseTemporary(target);
			}
		}

		// PROFILING (finding where the frame goes)

		/// <summary>Clears and starts the Unity Profiler (play mode only, not the editor).</summary>
		public static string StartProfiling()
		{
			UnityEditorInternal.ProfilerDriver.ClearAllFrames();
			UnityEditorInternal.ProfilerDriver.profileEditor = false;
			UnityEditorInternal.ProfilerDriver.enabled       = true;
			return "Profiler recording.";
		}

		/// <summary>
		/// Stops the profiler and sums the last frames' main-thread samples by marker name: average self and total ms per frame.
		/// Markers that nest under themselves count their total more than once; self times always add up.
		/// </summary>
		public static string ReadProfile(int maxFrames, int top)
		{
			UnityEditorInternal.ProfilerDriver.enabled = false;

			int last  = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
			int first = Mathf.Max(UnityEditorInternal.ProfilerDriver.firstFrameIndex, last - maxFrames + 1);
			if (last < 0 || first > last)
				return "No profiler frames.";

			var selfTimes  = new Dictionary<string, double>(1024);
			var totalTimes = new Dictionary<string, double>(1024);
			var calls      = new Dictionary<string, double>(1024);
			var children   = new List<int>(64);
			var stack      = new Stack<int>(256);
			int    frames  = 0;
			double frameMs = 0.0;

			for (int frame = first; frame <= last; ++frame)
			{
				using (var view = UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(frame, 0, UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, UnityEditor.Profiling.HierarchyFrameDataView.columnSelfTime, false))
				{
					if (view == null || view.valid == false)
						continue;

					++frames;
					frameMs += view.frameTimeMs;

					stack.Clear();
					stack.Push(view.GetRootItemID());

					while (stack.Count > 0)
					{
						int id = stack.Pop();
						if (id != view.GetRootItemID())
						{
							string name = view.GetItemName(id);
							selfTimes[name]  = (selfTimes.TryGetValue(name, out double a) ? a : 0.0)  + view.GetItemColumnDataAsFloat(id, UnityEditor.Profiling.HierarchyFrameDataView.columnSelfTime);
							totalTimes[name] = (totalTimes.TryGetValue(name, out double b) ? b : 0.0) + view.GetItemColumnDataAsFloat(id, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime);
							calls[name]      = (calls.TryGetValue(name, out double c) ? c : 0.0)      + view.GetItemColumnDataAsFloat(id, UnityEditor.Profiling.HierarchyFrameDataView.columnCalls);
						}

						children.Clear();
						view.GetItemChildren(id, children);
						for (int i = 0; i < children.Count; ++i)
						{
							stack.Push(children[i]);
						}
					}
				}
			}

			if (frames == 0)
				return "No valid profiler frames.";

			var builder = new StringBuilder();
			builder.AppendLine($"{frames} frames, {frameMs / frames:F2} ms per frame ({frames * 1000.0 / frameMs:F1} fps).");
			builder.AppendLine("Top self time (ms per frame, calls per frame):");
			foreach (var pair in selfTimes.OrderByDescending(t => t.Value).Take(top))
			{
				builder.AppendLine($"  {pair.Value / frames,7:F2}  {calls[pair.Key] / frames,6:F0}  {pair.Key}");
			}

			builder.AppendLine("Top total time (ms per frame):");
			foreach (var pair in totalTimes.OrderByDescending(t => t.Value).Take(top))
			{
				builder.AppendLine($"  {pair.Value / frames,7:F2}  {pair.Key}");
			}

			return builder.ToString();
		}

		private static int CountAgents(NetworkRunner runner)
		{
			var agents = new List<Agent>();
			runner.GetAllBehaviours(agents);
			return agents.Count;
		}

		// HELPERS

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

		private static bool Near(float a, float b, float tolerance = 0.05f)
		{
			return Mathf.Abs(a - b) <= tolerance;
		}

		private static string FindEvent(int from, string prefix)
		{
			for (int i = Mathf.Max(0, from); i < HealthDebug.EventLog.Count; ++i)
			{
				if (HealthDebug.EventLog[i].StartsWith(prefix) == true)
					return HealthDebug.EventLog[i];
			}

			return null;
		}

		private static string Between(string text, string from, string to)
		{
			int start = text.IndexOf(from);
			if (start < 0)
				return string.Empty;

			start += from.Length;
			int end = text.IndexOf(to, start);
			return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
		}

		private static float ParseFloat(string text)
		{
			return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) == true ? value : -1.0f;
		}

		/// <summary>A server-side hit, as if from instigator (null = nobody, which is never a teammate).</summary>
		private static void ServerHit(Agent target, float amount, bool bullet, bool head, Agent instigator)
		{
			var hitData = new HitData
			{
				Action        = EHitAction.Damage,
				Amount        = amount,
				IsBullet      = bullet,
				IsCritical    = head,
				Position      = target.transform.position + Vector3.up * (head == true ? 1.6f : 1.2f),
				Normal        = Vector3.up,
				Direction     = Vector3.forward,
				Target        = target.Health,
				InstigatorRef = instigator != null ? instigator.Object.InputAuthority : PlayerRef.None,
				Instigator    = instigator != null ? instigator.Health : null,
				HitType       = bullet == true ? EHitType.Rifle : EHitType.None,
			};

			HitUtility.ProcessHit(ref hitData);
		}

		private static float GetHeadHeight(Agent agent)
		{
			BodyPart head = agent.GetComponentsInChildren<BodyPart>().FirstOrDefault(t => t.IsCritical == true);
			if (head == null)
				return -1.0f;

			return head.transform.TransformPoint(head.Offset).y - agent.transform.position.y;
		}

		private static void ReleaseInputs()
		{
			AgentInput.DebugHoldAttack    = false;
			AgentInput.DebugHoldAim       = false;
			AgentInput.DebugHoldSprint    = false;
			AgentInput.DebugHoldCrouch    = false;
			AgentInput.DebugHoldJump      = false;
			AgentInput.DebugHoldMelee     = false;
			AgentInput.DebugHoldReload    = false;
			AgentInput.DebugHoldHeal      = false;
			AgentInput.DebugHoldInteract  = false;
			AgentInput.DebugSwitchSlot    = 0;
			AgentInput.DebugMoveDirection = Vector2.zero;
		}

		/// <summary>Gives input (and the camera) to the peer running this runner, like Numpad 1/2.</summary>
		private static void SetInputPeer(NetworkRunner runner)
		{
			var contexts = new HashSet<SceneContext>();
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Context != null)
				{
					contexts.Add(agent.Context);
				}
			}

			foreach (SceneContext context in contexts)
			{
				bool isPeer = context.Runner == runner;
				context.HasInput  = isPeer;
				context.IsVisible = isPeer;
			}
		}

		private static bool FindPlayers(out Agent observed, out Agent server, out Agent other)
		{
			observed = null;
			server   = null;
			other    = null;

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object != null && agent.Object.IsValid == true && agent.IsObserved == true && agent.Context.HasInput == true && agent.Object.InputAuthority.IsRealPlayer == true)
				{
					observed = agent;
				}
			}

			if (observed == null)
				return false;

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Runner == null || agent.Runner.IsServer == false || agent.Object == null || agent.Object.IsValid == false || agent.Object.InputAuthority.IsRealPlayer == false)
					continue;

				if (agent.Object.InputAuthority == observed.Object.InputAuthority)
				{
					server = agent;
				}
				else
				{
					other = agent;
				}
			}

			return server != null;
		}

		private static Agent FindServerAgent()
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Runner != null && agent.Runner.IsServer == true && agent.Object != null && agent.Object.IsValid == true)
					return agent;
			}

			return null;
		}

		/// <summary>The server's copy of this player's current agent (a new one after a respawn).</summary>
		private static Agent ServerCopy(PlayerRef playerRef)
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Runner != null && agent.Runner.IsServer == true && agent.Object != null && agent.Object.IsValid == true && agent.Object.InputAuthority == playerRef)
					return agent;
			}

			return null;
		}

		private static Agent ViewCopy(PlayerRef playerRef, NetworkRunner runner)
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Runner == runner && agent.Object != null && agent.Object.IsValid == true && agent.Object.InputAuthority == playerRef)
					return agent;
			}

			return null;
		}

		/// <summary>Teleports every peer's copy of this player and sets a level look at the given yaw.</summary>
		private static void PlaceAll(PlayerRef playerRef, Vector3 position, float yaw)
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object == null || agent.Object.IsValid == false || agent.Object.InputAuthority != playerRef)
					continue;

				KCC kcc = agent.Character.CharacterController;

				kcc.FixedData.BasePosition     = position;
				kcc.FixedData.DesiredPosition  = position;
				kcc.FixedData.TargetPosition   = position;
				kcc.RenderData.BasePosition    = position;
				kcc.RenderData.DesiredPosition = position;
				kcc.RenderData.TargetPosition  = position;
				kcc.SetPosition(position, true, false, false);
				agent.transform.position = position;

				kcc.FixedData.LookYaw    = yaw;
				kcc.RenderData.LookYaw   = yaw;
				kcc.FixedData.LookPitch  = 0.0f;
				kcc.RenderData.LookPitch = 0.0f;
			}
		}

		/// <summary>Points every peer's copy of this player at a world point.</summary>
		private static void LookAll(PlayerRef playerRef, Vector3 point)
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object == null || agent.Object.IsValid == false || agent.Object.InputAuthority != playerRef)
					continue;

				KCC     kcc       = agent.Character.CharacterController;
				Vector3 direction = point - agent.Character.GetCameraTransform(false).Position;

				float yaw   = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
				float pitch = -Mathf.Asin(direction.normalized.y) * Mathf.Rad2Deg;

				kcc.FixedData.LookYaw    = yaw;
				kcc.RenderData.LookYaw   = yaw;
				kcc.FixedData.LookPitch  = pitch;
				kcc.RenderData.LookPitch = pitch;
			}
		}

		private static bool TryGetGround(NetworkRunner runner, Vector3 point, out Vector3 ground)
		{
			ground = point;

			PhysicsScene physics = runner.GetPhysicsScene();
			if (physics.Raycast(point + Vector3.up * 30.0f, Vector3.down, out RaycastHit hit, 60.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == false)
				return false;

			ground = hit.point;

			// Room to stand
			var overlaps = new Collider[1];
			return physics.OverlapCapsule(ground + Vector3.up * 0.5f, ground + Vector3.up * 1.5f, 0.35f, overlaps, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == 0;
		}

		private static Vector3 Ground(NetworkRunner runner, Vector3 point)
		{
			TryGetGround(runner, point, out Vector3 ground);
			return ground;
		}

		/// <summary>Open, roughly level ground for the check's layout: around the player first, then around each spawn point.</summary>
		private static bool FindClearSpot(Agent agent, GameplayMode mode, float distance, out Vector3 origin, out float yaw)
		{
			origin = agent.transform.position;
			if (FindClearDirection(agent, origin, distance, out yaw) == true)
				return true;

			foreach (SpawnPoint spawnPoint in mode.SpawnPoints)
			{
				if (spawnPoint == null || TryGetGround(agent.Runner, spawnPoint.transform.position, out origin) == false)
					continue;

				if (FindClearDirection(agent, origin, distance, out yaw) == true)
					return true;
			}

			origin = default;
			return false;
		}

		/// <summary>A direction with roughly level, open ground out to this distance and a clear line of sight, for the dummy duo.</summary>
		private static bool FindClearDirection(Agent agent, Vector3 origin, float distance, out float yaw)
		{
			PhysicsScene physics = agent.Runner.GetPhysicsScene();
			float        start   = agent.Character.CharacterController.FixedData.LookYaw;

			for (int i = 0; i < 16; ++i)
			{
				yaw = start + i * 22.5f;
				Quaternion facing  = Quaternion.Euler(0.0f, yaw, 0.0f);
				Vector3    forward = facing * Vector3.forward;
				Vector3    right   = facing * Vector3.right;

				bool ok = true;
				Vector3[] spots = { origin + forward * distance, origin + forward * distance + right * 3.0f, origin - right * 2.0f };
				foreach (Vector3 wanted in spots)
				{
					if (TryGetGround(agent.Runner, wanted, out Vector3 spot) == false || Mathf.Abs(spot.y - origin.y) > 1.0f)
					{
						ok = false;
						break;
					}

					Vector3 eye     = origin + Vector3.up * 1.7f;
					Vector3 toChest = spot + Vector3.up * 1.2f - eye;
					if (physics.Raycast(eye, toChest.normalized, toChest.magnitude, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
					{
						ok = false;
						break;
					}
				}

				if (ok == true)
					return true;
			}

			yaw = 0.0f;
			return false;
		}

		// ROUTINE

		private static void StartRoutine(string routineName, IEnumerator routine)
		{
			_routine.Clear();
			_routine.Push(routine);
			_routineName = routineName;

			EditorApplication.update -= UpdateRoutine;
			EditorApplication.update += UpdateRoutine;
		}

		private static void UpdateRoutine()
		{
			if (Application.isPlaying == false)
			{
				Log($"{_routineName} stopped: Play mode ended.");
				StopRoutine();
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
				Log($"{_routineName} failed: {exception}");
				Debug.LogException(exception);
				StopRoutine();
				return;
			}

			if (_routine.Count == 0)
			{
				StopRoutine();
			}
		}

		private static void StopRoutine()
		{
			while (_routine.Count > 0)
			{
				(_routine.Pop() as System.IDisposable)?.Dispose();
			}

			EditorApplication.update -= UpdateRoutine;
			Debug.Log($"{_routineName} finished.");
		}

		private static IEnumerator WaitFor(System.Func<bool> condition, float timeout, string what)
		{
			_waitFailed = null;
			double end = EditorApplication.timeSinceStartup + timeout;

			while (condition() == false)
			{
				if (EditorApplication.timeSinceStartup > end)
				{
					_waitFailed = $"timed out waiting for {what}";
					Log($"  ({_waitFailed})");
					yield break;
				}

				yield return null;
			}
		}

		private static IEnumerator WaitSeconds(float seconds)
		{
			double end = EditorApplication.timeSinceStartup + seconds;
			while (EditorApplication.timeSinceStartup < end)
			{
				yield return null;
			}
		}
	}
}
