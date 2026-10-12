namespace TPSBR
{
	using System.Collections;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using Fusion;
	using TPSBR.UI;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// The HUD Check (phase 6a), Brownout → Test → HUD → HUD Check. Run it in GenArea5 with 2 peers, viewed from the lagged client. It drives
	/// the observed player through the states the HUD shows (kit, damage, firing and reloading, a dummy downed and killed, healing, gear,
	/// a throw, aiming, a ping, being downed and dying) and checks each value the HUD shows (BrownoutHud.Readout) against the game, on the
	/// observed peer and, for the squad list and pings, on the teammate's peer. Saves a screenshot of each state to Logs/HudCheck/.
	/// About 1 minute. Read it with GetHudCheckLog(); also written to Logs/HudCheck/HudCheck.txt.
	/// </summary>
	public static class BrownoutHudCheck
	{
		// CONSTANTS

		private const string MENU = "Brownout/Test/HUD/";

		// MENU

		[MenuItem(MENU + "HUD Check", priority = 90)]
		private static void HudCheckMenu() => Debug.Log(RunHudCheck());

		[MenuItem(MENU + "HUD Check", true)]
		private static bool ValidateHudCheck() => Application.isPlaying;

		// PRIVATE MEMBERS

		private static readonly List<string>       _log     = new List<string>(128);
		private static readonly Stack<IEnumerator> _routine = new Stack<IEnumerator>();
		private static BrownoutHud _hud;
		private static bool _running;
		private static int  _passed;
		private static int  _checked;

		// PUBLIC METHODS

		public static string RunHudCheck()
		{
			if (Application.isPlaying == false)
				return "Enter Play mode first (GenArea5, 2 peers).";
			if (_running == true)
				return "The HUD Check is already running.";

			_log.Clear();
			_routine.Clear();
			_routine.Push(HudCheckRoutine());
			_running = true;

			EditorApplication.update -= UpdateRoutine;
			EditorApplication.update += UpdateRoutine;

			return "HUD Check started (about 1 minute). Read GetHudCheckLog().";
		}

		public static string GetHudCheckLog()
		{
			return (_running == true ? "(running)\n" : "") + string.Join("\n", _log);
		}

		// THE CHECK

		private static IEnumerator HudCheckRoutine()
		{
			// The HUD of the peer with input (the one the debug inputs drive)
			_hud = null;
			foreach (BrownoutHud candidate in Object.FindObjectsByType<BrownoutHud>(FindObjectsSortMode.None))
			{
				Agent agent = candidate.Agent;
				if (agent != null && agent.Object != null && agent.Object.IsValid == true && agent.Context != null && agent.Context.HasInput == true && agent.HasInputAuthority == true)
				{
					_hud = candidate;
				}
			}

			BrownoutHud hud = _hud;
			Agent observed = hud != null ? hud.Agent : null;

			if (observed == null || observed.Object == null || observed.Object.InputAuthority.IsRealPlayer == false || ServerCopy(observed.Object.InputAuthority) == null)
			{
				Log("Need a running match with the HUD of the peer with input.");
				yield break;
			}

			PlayerRef playerRef = observed.Object.InputAuthority;
			Agent     server    = ServerCopy(playerRef);

			_passed  = 0;
			_checked = 0;
			Log($"HUD Check: {observed.name} on {(observed.Runner.IsServer == true ? "the host" : "the client (simulated lag in play)")}.");

			GameplayMode.DebugNoTeams = false;
			ReleaseInputs();

			try
			{
				// 1. On a clear floor 300 m above the level: a full kit, a frag, a gear item, a dummy duo ahead
				BrownoutHealthCheck.RemoveDummies();
				Vector3 floor = server.transform.position + Vector3.up * 300.0f;
				BuildFloor(floor);
				BrownoutTestTools.PlaceObservedPlayer(floor + Vector3.up * 0.1f, 0.0f);
				yield return Wait(1.5f);

				BrownoutLootTools.EmptyInventory();
				BrownoutLootTools.GiveFullKit();
				BrownoutEquipmentTools.GiveEquipment(EEquipment.Frag);
				BrownoutEquipmentTools.GiveGear(EGear.DoubleTime);
				BrownoutHealthCheck.SpawnDummyDuo(10.0f);
				yield return Wait(2.5f);

				server = ServerCopy(playerRef);
				Check(Read("health") == Mathf.CeilToInt(server.Health.CurrentHealth).ToString(), $"Health shows {Read("health")} (server {server.Health.CurrentHealth:F0})");
				Check(Read("armor").StartsWith($"T{server.Health.ArmorTier} ") == true, $"Armor shows {Read("armor")} (server T{server.Health.ArmorTier} {server.Health.ArmorLeft:F0})");
				Check(Read("heal").Contains(" x") == true, $"Healing item shows {Read("heal")}");
				Check(Read("equipment") == "Frag Grenade x1", $"Equipment shows {Read("equipment")}");
				Check(Read("gear").StartsWith("Double Time off") == true, $"Gear shows {Read("gear")}");

				var gun = server.Weapons.CurrentWeapon as BulletWeapon;
				Check(gun != null && Read("gun") == gun.GunData.GunName && Read("ammo") == $"{gun.MagazineAmmo}/{gun.WeaponAmmo}" && Read("caliber") == GunData.GetCaliberName(gun.GunData.Caliber),
					$"Gun shows {Read("gun")} {Read("ammo")} {Read("caliber")}, {Read("pips")} spare mag pips (server {gun?.GunData.GunName} {gun?.MagazineAmmo}/{gun?.WeaponAmmo})");
				Check(Read("squad.teammate") != "none", $"Squad list shows the teammate: {Read("squad.teammate")}");
				Check(Read("crosshair") == "shown", "Crosshair shown with a gun out at the hip");

				float cameraHeading = Mathf.Repeat(Mathf.Atan2(Camera.main != null ? Camera.main.transform.forward.x : 0.0f, Camera.main != null ? Camera.main.transform.forward.z : 1.0f) * Mathf.Rad2Deg, 360.0f);
				int.TryParse(Read("compass.heading"), out int compass);
				Check(Mathf.Abs(Mathf.DeltaAngle(compass, cameraHeading)) <= 2.0f, $"Compass heading {compass} (camera {cameraHeading:F0})");
				Screenshot("01_kit");

				// 2. Damage
				BrownoutHealthCheck.DamageObserved(40.0f);
				yield return Wait(1.5f);
				server = ServerCopy(playerRef);
				Check(Read("health") == Mathf.CeilToInt(server.Health.CurrentHealth).ToString() && Read("armor").Contains($"{server.Health.ArmorLeft:F0}/") == true,
					$"After 40 damage: health {Read("health")}, armor {Read("armor")} (server {server.Health.CurrentHealth:F0}, armor {server.Health.ArmorLeft:F0})");

				// 3. Aiming hides the crosshair
				AgentInput.DebugHoldAim = true;
				yield return Wait(1.0f);
				Check(Read("crosshair") == "hidden", "Crosshair hidden while aimed");
				AgentInput.DebugHoldAim = false;
				yield return Wait(0.6f);

				// 4. Fire at a dummy: hit marker, ammo, down and kill banners, kill feed
				Agent dummy = server.Context.GameplayMode.GetDummies().FirstOrDefault(d => d != null && d.Health.IsAlive == true);
				if (dummy == null)
				{
					Check(false, "No dummy to shoot");
				}
				else
				{
					BrownoutTestTools.SetGunplayDebug(true, true, false);
					string before = Read("ammo");

					for (int burst = 0; burst < 12 && dummy.Health.IsDowned == false && dummy.Health.IsAlive == true; ++burst)
					{
						BrownoutTestTools.AimObservedAt(dummy.transform.position + Vector3.up * 1.3f);
						AgentInput.DebugHoldAttack = true;
						yield return Wait(0.35f);
						AgentInput.DebugHoldAttack = false;
						yield return Wait(0.35f);
					}

					Check(Read("hitmarker.last") != "none", $"Hit marker after hitting the dummy: {Read("hitmarker.last")}");
					Check(Read("ammo") != before, $"Ammo after firing: {before} -> {Read("ammo")}");
					yield return Wait(0.5f);
					Check(Read("banner.last").StartsWith("DOWNED") == true, $"Banner on the down: {Read("banner.last")}");
					Screenshot("02_downed_dummy");

					for (int burst = 0; burst < 12 && dummy.Health.IsAlive == true; ++burst)
					{
						BrownoutTestTools.AimObservedAt(dummy.transform.position + Vector3.up * 0.3f);
						AgentInput.DebugHoldAttack = true;
						yield return Wait(0.35f);
						AgentInput.DebugHoldAttack = false;
						yield return Wait(0.35f);
					}

					yield return Wait(1.0f);
					Check(Read("killfeed.top").Contains("Dummy") == true, $"Kill feed: {Read("killfeed.top")}");
					Check(Read("counters.kills") != "0", $"Kills counter {Read("counters.kills")}");
					Screenshot("03_kill");
					BrownoutTestTools.SetGunplayDebug(false, false, false);
				}

				// 5. Reload: the loaded-mag pip fills with the reload
				AgentInput.DebugHoldReload = true;
				yield return Wait(0.3f);
				AgentInput.DebugHoldReload = false;
				yield return Wait(3.5f);
				server = ServerCopy(playerRef);
				gun    = server.Weapons.CurrentWeapon as BulletWeapon;
				Check(gun != null && Read("ammo") == $"{gun.MagazineAmmo}/{gun.WeaponAmmo}", $"After a reload: {Read("ammo")} (server {gun?.MagazineAmmo}/{gun?.WeaponAmmo})");

				// 6. Healing: the progress bar
				AgentInput.DebugHoldHeal = true;
				yield return Wait(0.6f);
				Check(Read("progress").StartsWith("USING") == true, $"Progress bar while healing: {Read("progress")}");
				Screenshot("04_healing");
				yield return Wait(4.5f);
				AgentInput.DebugHoldHeal = false;
				yield return Wait(0.5f);

				// 7. Gear on, counting down, off
				AgentInput.DebugHoldGear = true;
				yield return Wait(0.3f);
				AgentInput.DebugHoldGear = false;
				yield return Wait(2.0f);
				string gearOn = Read("gear");
				Check(gearOn.StartsWith("Double Time on") == true && gearOn.EndsWith("10.0") == false, $"Gear switched on and counting down: {gearOn}");
				Screenshot("05_gear_on");
				AgentInput.DebugHoldGear = true;
				yield return Wait(0.3f);
				AgentInput.DebugHoldGear = false;
				yield return Wait(0.8f);
				Check(Read("gear").StartsWith("Double Time off") == true, $"Gear switched off: {Read("gear")}");

				// 8. A throw empties the Equipment slot
				server = ServerCopy(playerRef);
				BrownoutTestTools.AimObservedAt(server.transform.position + Quaternion.Euler(0.0f, server.Character.CharacterController.FixedData.LookYaw + 90.0f, 0.0f) * Vector3.forward * 15.0f + Vector3.up * 3.0f);
				AgentInput.DebugHoldEquipment = true;
				yield return Wait(0.2f);
				AgentInput.DebugHoldEquipment = false;
				yield return Wait(2.0f);
				Check(Read("equipment") == "none", $"Equipment after the throw: {Read("equipment")}");
				yield return Wait(3.0f);

				// 9. Ping: seen on your HUD and your teammate's
				server = ServerCopy(playerRef);
				BrownoutTestTools.AimObservedAt(server.transform.position + Quaternion.Euler(0.0f, server.Character.CharacterController.FixedData.LookYaw, 0.0f) * Vector3.forward * 12.0f);
				yield return Wait(0.5f);
				AgentInput.DebugHoldPing = true;
				yield return Wait(0.3f);
				AgentInput.DebugHoldPing = false;
				yield return Wait(1.5f);
				BrownoutHud teammateHud = OtherHud(hud);
				Check(Read("ping.own") != "none", $"Own ping on the HUD at {Read("ping.own")}");
				Check(teammateHud == null || ReadFrom(teammateHud, "ping.teammate") != "none", $"The teammate's HUD shows the ping: {(teammateHud != null ? ReadFrom(teammateHud, "ping.teammate") : "no other HUD")}");
				Screenshot("06_ping");

				// 10. Downed: the bleed-out bar, the teammate's squad list shows it
				BrownoutHealthCheck.DownObserved();
				yield return Wait(1.5f);
				Check(teammateHud == null || ReadFrom(teammateHud, "squad.teammate").EndsWith("downed") == true, $"The teammate's squad list: {(teammateHud != null ? ReadFrom(teammateHud, "squad.teammate") : "no other HUD")}");
				Screenshot("07_downed");

				// 11. Dying with the teammate up: you watch them, and their spectator counter shows you
				server = ServerCopy(playerRef);
				if (server.Health.IsDowned == true)
				{
					server.Health.KillDowned("HUD Check");
				}
				yield return Wait(2.5f);
				Check(teammateHud == null || ReadFrom(teammateHud, "counters.spectators") == "1", $"The teammate's spectator counter: {(teammateHud != null ? ReadFrom(teammateHud, "counters.spectators") : "no other HUD")}");
				Screenshot("08_spectating");
			}
			finally
			{
				ReleaseInputs();
				BrownoutTestTools.SetGunplayDebug(false, false, false);
				BrownoutHealthCheck.RemoveDummies();
				DestroyFloor();
			}

			Log($"HUD Check done: {_passed} of {_checked} passed. Pictures in {BrownoutHudTools.LogFolder}/.");
			SaveLog();
		}

		// PRIVATE METHODS

		private static string Read(string key) => ReadFrom(_hud, key);

		private static string ReadFrom(BrownoutHud hud, string key)
		{
			return hud != null && hud.Readout.TryGetValue(key, out string value) == true ? value : "none";
		}

		private static BrownoutHud OtherHud(BrownoutHud hud)
		{
			foreach (BrownoutHud other in Object.FindObjectsByType<BrownoutHud>(FindObjectsSortMode.None))
			{
				if (other != hud)
					return other;
			}

			return null;
		}

		internal static Agent ServerCopy(PlayerRef playerRef)
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object != null && agent.Object.IsValid == true && agent.HasStateAuthority == true && agent.Object.InputAuthority == playerRef)
					return agent;
			}

			return null;
		}

		private static readonly List<GameObject> _floor = new List<GameObject>(2);

		/// <summary>A 60 m floor in every peer's scene, so nothing stands between the player and the dummies.</summary>
		internal static void BuildFloor(Vector3 center)
		{
			DestroyFloor();

			foreach (NetworkRunner runner in NetworkRunner.Instances)
			{
				if (runner == null || runner.IsRunning == false)
					continue;

				GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
				box.name  = "HudCheckFloor";
				box.layer = ObjectLayer.Default;
				box.transform.SetPositionAndRotation(center + Vector3.down * 0.25f, Quaternion.identity);
				box.transform.localScale = new Vector3(60.0f, 0.5f, 60.0f);

				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(box, runner.SimulationUnityScene);
				if (runner.Config.PeerMode != NetworkProjectConfig.PeerModes.Single)
				{
					runner.AddVisibilityNodes(box);
				}

				_floor.Add(box);
			}

			Physics.SyncTransforms();
		}

		internal static void DestroyFloor()
		{
			foreach (GameObject box in _floor)
			{
				if (box != null)
				{
					Object.Destroy(box);
				}
			}

			_floor.Clear();
		}

		private static void Screenshot(string name)
		{
			BrownoutHudTools.SaveScreenshot(name);
		}

		private static void ReleaseInputs()
		{
			AgentInput.DebugHoldAttack    = false;
			AgentInput.DebugHoldAim       = false;
			AgentInput.DebugHoldHeal      = false;
			AgentInput.DebugHoldReload    = false;
			AgentInput.DebugHoldGear      = false;
			AgentInput.DebugHoldEquipment = false;
			AgentInput.DebugHoldPing      = false;
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
			Directory.CreateDirectory(BrownoutHudTools.LogFolder);
			File.WriteAllText(Path.Combine(BrownoutHudTools.LogFolder, "HudCheck.txt"), string.Join("\n", _log));
		}

		internal static IEnumerator Wait(float seconds)
		{
			double end = EditorApplication.timeSinceStartup + seconds;
			while (EditorApplication.timeSinceStartup < end)
			{
				yield return null;
			}
		}

		private static void UpdateRoutine()
		{
			if (Application.isPlaying == false)
			{
				Log("HUD Check stopped: Play mode ended.");
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
				Log($"HUD Check failed: {exception}");
				Debug.LogException(exception);
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
			ReleaseInputs();
			EditorApplication.update -= UpdateRoutine;
		}
	}
}
