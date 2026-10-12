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
	/// The Screens Check (phase 6b), Brownout → Test → HUD → Screens Check. Run it in GenArea5 with 2 peers, viewed from the lagged client
	/// (switch to the client first). It opens the inventory with the Tab binding and checks it against the server (slots, tabs, a use and
	/// a drop by click), opens a 30-item stash and takes from it and scrolls it, closes everything with Esc, opens the full map with the M
	/// binding and places and removes a waypoint (seen on the teammate's HUD), checks a rebound key is saved and swapped, toggle crouch
	/// and the mouse numbers, watches the teammate after dying, and shows the victory banner. Saves a picture of each screen to
	/// Logs/ScreensCheck/. About 1 minute. Read it with GetScreensCheckLog(); also written to Logs/ScreensCheck/ScreensCheck.txt.
	/// The player's own key bindings and hold/toggle choices are put back at the end.
	/// </summary>
	public static class BrownoutScreensCheck
	{
		// CONSTANTS

		private const string MENU   = "Brownout/Test/HUD/";
		public  const string Folder = "Logs/ScreensCheck";

		// MENU

		[MenuItem(MENU + "Screens Check", priority = 91)]
		private static void ScreensCheckMenu() => Debug.Log(RunScreensCheck());

		[MenuItem(MENU + "Show Victory Banner", priority = 120)]
		private static void VictoryMenu() => Debug.Log(ShowVictory(1));

		[MenuItem(MENU + "Open Full Map", priority = 121)]
		private static void MapMenu() => Debug.Log(OpenMap());

		[MenuItem(MENU + "Spawn Test Stash (30 items)", priority = 122)]
		private static void StashMenu() => Debug.Log(SpawnTestStash(30));

		[MenuItem(MENU + "Log Key Bindings", priority = 130)]
		private static void BindingsMenu() => Debug.Log(DescribeBindings());

		[MenuItem(MENU + "Screens Check", true)]
		[MenuItem(MENU + "Show Victory Banner", true)]
		[MenuItem(MENU + "Open Full Map", true)]
		[MenuItem(MENU + "Spawn Test Stash (30 items)", true)]
		private static bool ValidatePlaying() => Application.isPlaying;

		// PRIVATE MEMBERS

		private static readonly List<string>       _log     = new List<string>(128);
		private static readonly Stack<IEnumerator> _routine = new Stack<IEnumerator>();
		private static BrownoutHud _hud;
		private static bool _running;
		private static int  _passed;
		private static int  _checked;

		// PUBLIC METHODS

		public static string RunScreensCheck()
		{
			if (Application.isPlaying == false)
				return "Enter Play mode first (GenArea5, 2 peers).";
			if (_running == true)
				return "The Screens Check is already running.";

			_log.Clear();
			_routine.Clear();
			_routine.Push(CheckRoutine());
			_running = true;

			EditorApplication.update -= UpdateRoutine;
			EditorApplication.update += UpdateRoutine;

			return "Screens Check started (about 1 minute). Read GetScreensCheckLog().";
		}

		public static string GetScreensCheckLog()
		{
			return (_running == true ? "(running)\n" : "") + string.Join("\n", _log);
		}

		/// <summary>The victory banner on the HUD with input (place 0 hides it).</summary>
		public static string ShowVictory(int place)
		{
			BrownoutHud hud = FindInputHud();
			if (hud == null)
				return "No HUD with input.";

			hud.ShowVictory(place);
			return place > 0 ? $"Victory banner shown (#{place}) for {HudSettings.Current.VictoryTime:0} s." : "Victory banner hidden.";
		}

		public static string OpenMap()
		{
			BrownoutHud hud = FindInputHud();
			if (hud == null)
				return "No HUD with input.";

			hud.SetMapOpen(hud.IsMapOpen == false);
			return hud.IsMapOpen == true ? "Full map open." : "Full map closed.";
		}

		/// <summary>Server: a loot bag at the observed player's feet holding the given number of items, opened as their stash.</summary>
		public static string SpawnTestStash(int count)
		{
			BrownoutHud hud = FindInputHud();
			Agent server = hud != null && hud.Agent != null ? BrownoutHudCheck.ServerCopy(hud.Agent.Object.InputAuthority) : null;
			if (server == null)
				return "No observed player on a running host.";

			var all = new List<LootStack>(64);
			ItemCatalog.GetAllItems(all);
			var items = new List<LootStack>(count);
			for (int i = 0; i < count && all.Count > 0; ++i)
			{
				items.Add(all[i % all.Count]);
			}

			LootBag bag = LootSpawner.SpawnBag(server.Runner, server.transform.position + server.transform.forward * 0.8f, "Test Stash", items);
			if (bag == null)
				return "Couldn't spawn a bag.";

			server.Inventory.OpenStashBag(bag);
			return $"Test stash with {items.Count} items opened for {server.name}.";
		}

		public static string DescribeBindings()
		{
			var lines = new List<string>(KeyBindings.Count + 2);
			for (int i = 0; i < KeyBindings.Count; ++i)
			{
				EBinding action = (EBinding)i;
				lines.Add($"{ControlsSettings.Current.GetLabel(action),-26} {KeyBindings.GetKeyName(action),-14} {KeyBindings.GetPath(action)}");
			}

			RuntimeSettings settings = Global.RuntimeSettings;
			if (settings != null)
			{
				lines.Add($"Crouch {(settings.ToggleCrouch ? "toggle" : "hold")}, sprint {(settings.ToggleSprint ? "toggle" : "hold")}, aim {(settings.ToggleAim ? "toggle" : "hold")}; sensitivity x{settings.Sensitivity:0.00}, ADS x{settings.AimSensitivity:0.00}");
			}

			lines.Add($"Hip turn: {ControlsSettings.Current.HipDegreesPerCount:0.#####} degrees per count ({ControlsSettings.Current.HipCentimetersPerTurn:0} cm per turn at {ControlsSettings.Current.ReferenceDpi:0} DPI)");
			return string.Join("\n", lines);
		}

		// THE CHECK

		private static IEnumerator CheckRoutine()
		{
			_hud = FindInputHud();
			BrownoutHud hud      = _hud;
			Agent       observed = hud != null ? hud.Agent : null;

			if (observed == null || observed.Object == null || observed.Object.InputAuthority.IsRealPlayer == false || BrownoutHudCheck.ServerCopy(observed.Object.InputAuthority) == null)
			{
				Log("Need a running match with the HUD of the peer with input.");
				yield break;
			}

			PlayerRef playerRef = observed.Object.InputAuthority;
			Agent     server    = BrownoutHudCheck.ServerCopy(playerRef);
			BrownoutHud other   = OtherHud(hud);

			_passed  = 0;
			_checked = 0;
			Log($"Screens Check: {observed.name} on {(observed.Runner.IsServer == true ? "the host" : "the client (simulated lag in play)")}.");

			RuntimeSettings settings     = Global.RuntimeSettings;
			string[]        savedPaths   = KeyBindings.GetAllPaths();
			bool            savedCrouch  = settings.ToggleCrouch;
			bool            savedSprint  = settings.ToggleSprint;
			bool            savedAim     = settings.ToggleAim;
			GameplayMode.DebugNoTeams = false;
			Release();

			try
			{
				// 1. A kit on a clear floor 300 m up
				BrownoutHealthCheck.RemoveDummies();
				Vector3 floor = server.transform.position + Vector3.up * 300.0f;
				BrownoutHudCheck.BuildFloor(floor);
				BrownoutTestTools.PlaceObservedPlayer(floor + Vector3.up * 0.1f, 0.0f);
				yield return Wait(1.5f);

				BrownoutLootTools.EmptyInventory();
				BrownoutLootTools.GiveFullKit();
				BrownoutEquipmentTools.GiveEquipment(EEquipment.Frag);
				BrownoutEquipmentTools.GiveGear(EGear.DoubleTime);
				yield return Wait(1.5f);

				// 2. Tab opens the inventory; it shows the server's slots
				yield return Press(EBinding.Inventory);
				server = Server(playerRef);
				Check(Read("inventory.open") == "open", $"Tab (the Inventory key) opens the inventory: {Read("inventory.open")}");
				string expected = string.Join(",", Enumerable.Range(0, server.Inventory.SlotCount).Select(i => server.Inventory.GetSlot(i).IsEmpty == true ? "-" : ItemCatalog.GetName(server.Inventory.GetSlot(i))));
				Check(Read("inventory.slots") == expected, $"Slots shown: {Read("inventory.slots")} (server {expected})");
				Check(Read("crosshair") == "hidden", "The crosshair hides while the inventory is open");
				Screenshot("01_inventory");
				yield return Wait(0.4f); // The picture is taken when the Game view next draws

				InventoryScreen screen = observed.GetComponent<InventoryScreen>();
				screen.SetWeaponTab(2);
				yield return Wait(0.3f);
				Check(Read("inventory.tab") == "2", $"WEAPON 2 tab: {Read("inventory.tab")}");
				screen.SetWeaponTab(1);

				// A left click on a healing item in the slots equips it into Health; a right click drops one
				int healSlot = FindSlot(server, EItemKind.Heal);
				if (healSlot >= 0)
				{
					LootStack before = server.Inventory.GetSlot(healSlot);
					Check(hud.TestClick($"slot{healSlot}", false) == true, $"Left click on slot {healSlot} ({ItemCatalog.GetName(before)})");
					yield return Wait(1.0f);
					server = Server(playerRef);
					Check(server.HealthSlot.Index == before.Index && server.HealthSlot.Kind == EItemKind.Heal, $"It went into the Health slot: {ItemCatalog.GetName(server.HealthSlot)}");
				}
				else
				{
					Check(false, "No healing item in the slots to click");
				}

				int dropSlot = Enumerable.Range(0, server.Inventory.SlotCount).FirstOrDefault(i => server.Inventory.GetSlot(i).IsEmpty == false);
				LootStack dropped = server.Inventory.GetSlot(dropSlot);
				hud.TestClick($"slot{dropSlot}", true);
				yield return Wait(1.0f);
				server = Server(playerRef);
				Check(server.Inventory.GetSlot(dropSlot).IsSameItem(dropped) == false || server.Inventory.GetSlot(dropSlot).Count < dropped.Count, $"Right click on slot {dropSlot} drops {ItemCatalog.GetName(dropped)}: now {Read("inventory.slots")}");

				// 3. A 30-item stash: shown 8 per row, scrolls, a click takes
				Log(SpawnTestStash(30));
				yield return Wait(1.5f);
				Check(Read("inventory.stash").StartsWith("30 items") == true, $"Stash panel: {Read("inventory.stash")}");
				Screenshot("02_stash");
				yield return Wait(0.4f); // The picture is taken when the Game view next draws

				screen.StashScroll = 1;
				yield return Wait(0.4f);
				Check(Read("inventory.stash").Contains("row 1") == true, $"Stash scrolled: {Read("inventory.stash")}");
				screen.StashScroll = 0;
				yield return Wait(0.3f);

				// Take an attachment (a gun would swap with the one in hand, which goes back into the bag)
				server = Server(playerRef);
				LootBag bag = server.Inventory.OpenStash;
				int bagBefore = bag != null ? bag.ItemCount : 0;
				int shown = 0;
				for (int i = 0; bag != null && i < LootBag.Capacity; ++i)
				{
					if (bag.GetItem(i).IsEmpty == true)
						continue;
					if (bag.GetItem(i).Kind == EItemKind.Attachment)
						break;
					++shown;
				}
				string taken = bag != null && shown < bagBefore ? ItemCatalog.GetName(bag.GetItem(Enumerable.Range(0, LootBag.Capacity).Where(i => bag.GetItem(i).IsEmpty == false).ElementAt(shown))) : "nothing";
				hud.TestClick($"stash{shown}", false);
				yield return Wait(1.0f);
				Check(bag != null && bag.ItemCount == bagBefore - 1, $"Left click on a stash item ({taken}) takes it: {bagBefore} -> {(bag != null ? bag.ItemCount : 0)} in the bag; {Read("inventory.stash")}");

				// Esc closes the inventory and the stash
				observed.Context.Input.TrigggerBackAction();
				yield return Wait(1.0f);
				server = Server(playerRef);
				Check(Read("inventory.open") == "closed" && server.Inventory.OpenStash == null && observed.Context.Input.IsCursorVisible == false, $"Esc closes the inventory and the stash: {Read("inventory.open")}, stash {(server.Inventory.OpenStash != null ? "open" : "closed")}");
				if (bag != null && bag.Object != null && bag.Object.IsValid == true)
				{
					bag.Runner.Despawn(bag.Object);
				}

				// 4. M opens the full map; a waypoint shows for you and your teammate; a click on it removes it
				yield return Press(EBinding.Map);
				Check(Read("map.open") == "open", $"M (the Map key) opens the full map: {Read("map.open")}, you are in {Read("map.square")}");
				server = Server(playerRef);
				Vector3 target = server.transform.position + server.transform.forward * 60.0f;
				hud.TestWaypoint(target, true);
				yield return Wait(1.5f);
				Check(Read("waypoint.own") != "none", $"Waypoint placed: {Read("waypoint.own")}");
				Check(other == null || ReadFrom(other, "waypoint.teammate") != "none", $"The teammate's HUD shows it: {(other != null ? ReadFrom(other, "waypoint.teammate") : "no other HUD")}");
				Screenshot("03_map_waypoint");
				yield return Wait(0.4f); // The picture is taken when the Game view next draws

				yield return Press(EBinding.Inventory);
				Check(Read("map.open") == "closed" && Read("inventory.open") == "open", $"Tab with the map open swaps to the inventory: map {Read("map.open")}, inventory {Read("inventory.open")}");
				observed.Context.Input.TrigggerBackAction();
				yield return Wait(0.5f);

				Screenshot("04_waypoint_compass");
				yield return Wait(0.4f); // The picture is taken when the Game view next draws
				hud.TestWaypoint(target, false);
				yield return Wait(1.5f);
				Check(Read("waypoint.own") == "none" && (other == null || ReadFrom(other, "waypoint.teammate") == "none"), $"Waypoint removed on both HUDs: {Read("waypoint.own")} / {(other != null ? ReadFrom(other, "waypoint.teammate") : "-")}");

				// 5. Settings: a rebound key is saved and swapped; toggle crouch; the mouse numbers
				KeyBindings.SetPath(EBinding.Reload, "<Keyboard>/t");
				KeyBindings.Reload(); // As after a restart
				Check(KeyBindings.GetKeyName(EBinding.Reload) == "T" && PersistentStorage.GetString("Controls.Reload", "") == "<Keyboard>/t", $"Reload rebound to {KeyBindings.GetKeyName(EBinding.Reload)}, saved as {PersistentStorage.GetString("Controls.Reload", "(default)")}");
				string healBefore = KeyBindings.GetPath(EBinding.Heal);
				KeyBindings.SetPath(EBinding.Heal, "<Keyboard>/t");
				Check(KeyBindings.GetPath(EBinding.Heal) == "<Keyboard>/t" && KeyBindings.GetPath(EBinding.Reload) == healBefore, $"Heal set to T takes it from Reload, which gets Heal's old key: Heal {KeyBindings.GetKeyName(EBinding.Heal)}, Reload {KeyBindings.GetKeyName(EBinding.Reload)}");
				KeyBindings.SetAllPaths(savedPaths);

				settings.ToggleCrouch = true;
				yield return Press(EBinding.Crouch);
				yield return Wait(1.0f);
				server = Server(playerRef);
				EMoveMode afterFirst = server.Character.CharacterController.FixedData.MoveMode;
				yield return Press(EBinding.Crouch);
				yield return Wait(1.0f);
				server = Server(playerRef);
				EMoveMode afterSecond = server.Character.CharacterController.FixedData.MoveMode;
				Check(afterFirst == EMoveMode.Crouch && afterSecond != EMoveMode.Crouch, $"Toggle crouch: a tap crouches ({afterFirst}), the next tap stands ({afterSecond})");
				settings.ToggleCrouch = savedCrouch;

				AgentInput input = observed.GetComponent<AgentInput>();
				float hip = input.GetLookDegreesPerCount();
				float cm  = 360.0f / Mathf.Max(0.00001f, hip) / ControlsSettings.Current.ReferenceDpi * 2.54f;
				Check(Mathf.Abs(cm * settings.Sensitivity - ControlsSettings.Current.HipCentimetersPerTurn) < 0.5f, $"Hip: {hip:0.#####} degrees per count = {cm:0.0} cm per turn at {ControlsSettings.Current.ReferenceDpi:0} DPI (sensitivity x{settings.Sensitivity:0.00})");

				AgentInput.DebugHoldAim = true;
				yield return Wait(1.2f);
				float aimed    = input.GetLookDegreesPerCount();
				float hipFov   = observed.Character.GetHipVerticalFOV();
				float viewFov  = observed.Context.Camera.Camera.fieldOfView;
				// Phase 11a: scaled by the zoom you aim through (the 2x lens), not the camera's view zoom around it
				float lensZoom  = observed.Weapons.CurrentWeapon != null ? Mathf.Max(1.0f, observed.Weapons.CurrentWeapon.AdsZoom) : 1.0f;
				float lensFov   = 2.0f * Mathf.Atan(Mathf.Tan(hipFov * 0.5f * Mathf.Deg2Rad) / lensZoom) * Mathf.Rad2Deg;
				float expected2 = hip * settings.AimSensitivity * Mathf.Tan(lensFov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(hipFov * 0.5f * Mathf.Deg2Rad);
				Check(aimed < hip && Mathf.Abs(aimed - expected2) < 0.0001f, $"Aimed (2x optic): {aimed:0.#####} degrees per count, the hip's scaled by the lens zoom x{lensZoom:0.##} ({hipFov:0.0}° -> {lensFov:0.0}°; the view around it {viewFov:0.0}°) and ADS x{settings.AimSensitivity:0.00}");
				AgentInput.DebugHoldAim = false;
				yield return Wait(0.5f);

				// 6. Dying with your teammate up: you watch them, with their values on your HUD
				Agent teammate = other != null ? other.Agent : null;
				BrownoutHealthCheck.DownObserved();
				yield return Wait(1.5f);
				server = Server(playerRef);
				if (server.Health.IsDowned == true)
				{
					server.Health.KillDowned("Screens Check");
				}
				yield return Wait(2.5f);

				Agent watched = hud.Agent;
				bool  isTeammate = watched != null && teammate != null && watched.Object != null && watched.Object.InputAuthority == teammate.Object.InputAuthority && watched.Object.InputAuthority.IsRealPlayer == true;
				Check(Read("spectating") != "none" && isTeammate == true, $"Spectating: \"{Read("spectating")}\", watching {(watched != null ? watched.name : "nobody")} (a real player: {isTeammate})");
				Check(watched != null && Read("health") == Mathf.CeilToInt(watched.Health.CurrentHealth).ToString(), $"The HUD shows the teammate's health: {Read("health")}");
				Screenshot("05_spectating");
				yield return Wait(0.4f); // The picture is taken when the Game view next draws

				// 7. Victory banner (a test tool until phase 7)
				hud.ShowVictory(1);
				yield return Wait(0.5f);
				Check(Read("victory").Contains("VICTORY") == true, $"Victory banner: {Read("victory")}");
				Screenshot("06_victory");
				yield return Wait(0.4f); // The picture is taken when the Game view next draws
				hud.ShowVictory(0);
			}
			finally
			{
				Release();
				KeyBindings.SetAllPaths(savedPaths);
				settings.ToggleCrouch = savedCrouch;
				settings.ToggleSprint = savedSprint;
				settings.ToggleAim    = savedAim;
				if (hud != null)
				{
					hud.SetMapOpen(false);
					hud.ShowVictory(0);
				}
				BrownoutHudCheck.DestroyFloor();
			}

			Log($"Screens Check done: {_passed} of {_checked} passed. Pictures in {Folder}/.");
			SaveLog();
		}

		// PRIVATE METHODS

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

		private static BrownoutHud OtherHud(BrownoutHud hud)
		{
			foreach (BrownoutHud other in Object.FindObjectsByType<BrownoutHud>(FindObjectsSortMode.None))
			{
				if (other != hud)
					return other;
			}

			return null;
		}

		private static Agent Server(PlayerRef playerRef) => BrownoutHudCheck.ServerCopy(playerRef);

		private static int FindSlot(Agent agent, EItemKind kind)
		{
			for (int i = 0; i < agent.Inventory.SlotCount; ++i)
			{
				if (agent.Inventory.GetSlot(i).Kind == kind && agent.Inventory.GetSlot(i).IsEmpty == false)
					return i;
			}

			return -1;
		}

		/// <summary>Taps a key through KeyBindings (as if pressed), so it reads exactly like the real key.</summary>
		private static IEnumerator Press(EBinding binding)
		{
			KeyBindings.DebugHeld.Add(binding);
			yield return Wait(0.2f);
			KeyBindings.DebugHeld.Remove(binding);
			yield return Wait(0.5f);
		}

		private static void Release()
		{
			KeyBindings.DebugHeld.Clear();
			AgentInput.DebugHoldAim = false;
		}

		private static string Read(string key) => ReadFrom(_hud, key);

		private static string ReadFrom(BrownoutHud hud, string key)
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
			File.WriteAllText(Path.Combine(folder, "ScreensCheck.txt"), string.Join("\n", _log));
		}

		private static void UpdateRoutine()
		{
			if (Application.isPlaying == false)
			{
				Log("Screens Check stopped: Play mode ended.");
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
				Log($"Screens Check failed: {exception}");
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
			Release();
			EditorApplication.update -= UpdateRoutine;
		}
	}
}
