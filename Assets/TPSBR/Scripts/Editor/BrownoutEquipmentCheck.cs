namespace TPSBR
{
	using System.Collections;
	using System.Collections.Generic;
	using System.Linq;
	using System.Reflection;
	using System.Text.RegularExpressions;
	using Fusion;
	using Fusion.Addons.KCC;
	using UnityEditor;
	using UnityEngine;
	using UnityEngine.SceneManagement;

	/// <summary>
	/// The Equipment Check (phase 5b), Brownout → Test → Equipment → Equipment Check. Run it in GenArea5 with 2 peers, viewed from the
	/// lagged client. It builds a test floor 300 m above the level in every peer's scene and checks: throw timing with real E presses
	/// (tap, cook, held too long), frag damage by distance, behind a wall, on the thrower, a teammate and armor; flash and concussion by
	/// distance and facing; smoke timing; E refused while reloading, healing or downed; the grapple onto a roof and a ledge, a miss, the
	/// rope from the other peer; and all six gear items. Pictures from the second peer go to Logs/EquipmentCheck/. About 3 minutes.
	/// Read it with GetEquipmentCheckLog().
	/// </summary>
	public static partial class BrownoutEquipmentTools
	{
		// CONSTANTS

		private const float FLOOR_HEIGHT = 300.0f;
		private const float TICK         = 1.0f / 60.0f;

		// MENU

		[MenuItem(MENU + "Equipment Check", priority = 90)]
		private static void EquipmentCheckMenu() => Debug.Log(RunEquipmentCheck());

		[MenuItem(MENU + "Equipment Check", true)]
		private static bool ValidateEquipmentCheck() => Application.isPlaying;

		// PRIVATE MEMBERS

		private static readonly List<string>       _log      = new List<string>(256);
		private static readonly Stack<IEnumerator> _routine  = new Stack<IEnumerator>();
		private static readonly List<GameObject>   _geometry = new List<GameObject>(16);
		private static bool _running;
		private static int  _passed;
		private static int  _checked;

		// PUBLIC METHODS

		public static string RunEquipmentCheck()
		{
			if (Application.isPlaying == false)
				return "Enter Play mode first (GenArea5, 2 peers).";
			if (_running == true)
				return "The Equipment Check is already running.";

			_log.Clear();
			_routine.Clear();
			_routine.Push(CheckRoutine());
			_running = true;

			EditorApplication.update -= UpdateRoutine;
			EditorApplication.update += UpdateRoutine;

			return "Equipment Check started (about 3 minutes). Read GetEquipmentCheckLog().";
		}

		public static string GetEquipmentCheckLog()
		{
			return (_running == true ? "(running)\n" : "") + string.Join("\n", _log);
		}

		// THE CHECK

		private static IEnumerator CheckRoutine()
		{
			Agent observed = FindObservedAgent();
			if (observed == null || ServerCopy(observed.Object.InputAuthority) == null)
			{
				Log("Need a running host with the observed player, viewed from the peer with input.");
				yield break;
			}

			PlayerRef     playerRef  = observed.Object.InputAuthority;
			NetworkRunner viewRunner = observed.Runner;
			Agent         server     = ServerCopy(playerRef);
			NetworkRunner runner     = server.Runner;
			Agent         host       = FindOtherPlayer(runner, playerRef);

			_passed  = 0;
			_checked = 0;

			Log($"Equipment Check: {observed.name} on {(viewRunner.IsServer == true ? "the host" : "the client (simulated lag in play)")}, other player {(host != null ? host.name : "none")}.");

			Vector3 originalPosition = server.transform.position;
			Vector3 center           = new Vector3(originalPosition.x, FLOOR_HEIGHT, originalPosition.z);

			Health.DebugGodMode       = false;
			GameplayMode.DebugNoTeams = false;
			EquipmentDebug.Clear();
			ReleaseInputs();

			try
			{
				BuildTestGeometry(center);
				PlaceAll(playerRef, center);
				if (host != null)
				{
					PlaceAll(host.Object.InputAuthority, center + new Vector3(0.0f, 0.0f, -8.0f));
					LookAt(host.Object.InputAuthority, center + Vector3.up * 1.0f);
				}

				EmptyAll(ServerCopy(playerRef));
				yield return Wait(2.0f);

				yield return CheckThrowTiming(playerRef, center);
				yield return CheckFrag(playerRef, host, center);
				yield return CheckFlashAndConcussion(playerRef, viewRunner, center);
				yield return CheckRefusals(playerRef, center);
				yield return CheckGrapple(playerRef, viewRunner, host, center);
				yield return CheckGearTimerAndDoubleTime(playerRef, center);
				yield return CheckDoubleJump(playerRef, center);
				yield return CheckNinja(playerRef, viewRunner, center);
				yield return CheckSixthSense(playerRef, viewRunner, center);
				yield return CheckCamouflage(playerRef, host, center);
				CheckO2Max(playerRef);
				yield return CheckSmokeFade();
			}
			finally
			{
				ReleaseInputs();

				Agent left = ServerCopy(playerRef);
				if (left != null)
				{
					left.Context.GameplayMode.RemoveDummies();
					EmptyAll(left);
					left.Health.DebugSetHealth(150.0f);
				}

				PlaceAll(playerRef, originalPosition);
				if (host != null && host.Object != null && host.Object.IsValid == true)
				{
					PlaceAll(host.Object.InputAuthority, originalPosition + new Vector3(2.0f, 0.0f, 0.0f));
				}

				DestroyTestGeometry();

				foreach (InventoryScreen screen in Object.FindObjectsByType<InventoryScreen>(FindObjectsSortMode.None))
				{
					screen.SetOpen(false);
				}
			}

			Log($"Equipment Check done: {_passed} of {_checked} passed.");
			SaveLog();
		}

		// 1. Throwing with real E presses on the observed peer

		private static float _smokeReleaseTick = -1.0f;

		private static IEnumerator CheckThrowTiming(PlayerRef playerRef, Vector3 center)
		{
			EquipmentSettings settings = EquipmentSettings.Current;
			Agent             server   = ServerCopy(playerRef);
			string            name     = server.name;

			// Tap: released PressToRelease after the press, gun ready PressToGunReady after it. Thrown away from the test blocks (+X, +Z)
			LookAt(playerRef, center + new Vector3(-25.0f, 9.0f, -25.0f));
			GiveEquipment(EEquipment.Frag);
			yield return Wait(0.5f);

			int start = EquipmentDebug.EventLog.Count;
			yield return HoldEquipment(0.1f);
			yield return WaitFor(start, t => t.StartsWith($"THROW {name} gun ready"), 4.0f);

			float release = ReadSeconds(start, $"THROW {name} Frag Grenade released", @"released ([\d.]+) s");
			float ready   = ReadSeconds(start, $"THROW {name} gun ready", @"ready ([\d.]+) s");
			server = ServerCopy(playerRef);
			Check(Near(release, settings.PressToRelease, TICK + 0.001f) && Near(ready, settings.PressToGunReady, TICK + 0.001f) && server.EquipmentSlot.IsEmpty == true,
				$"Real E tap on a frag: left the hand {release:F3} s after the press (data {settings.PressToRelease:F2}), gun ready at {ready:F3} s (data {settings.PressToGunReady:F2}), Equipment slot empty");

			yield return Wait(3.0f); // Let it go off far away

			// Cook: held 2 s, released then with the rest of the fuse
			GiveEquipment(EEquipment.Frag);
			yield return Wait(0.5f);
			start = EquipmentDebug.EventLog.Count;
			yield return HoldEquipment(2.0f);
			yield return WaitFor(start, t => t.StartsWith($"THROW {name} gun ready"), 4.0f);

			release = ReadSeconds(start, $"THROW {name} Frag Grenade released", @"released ([\d.]+) s");
			float fuse = ReadSeconds(start, $"THROW {name} Frag Grenade released", @"goes off in ([\d.]+) s");
			Check(release >= 1.9f && release <= 2.25f && Near(release + fuse, settings.FragFuse, 0.02f),
				$"Cooked frag: E held about 2 s, left the hand at {release:F3} s with {fuse:F2} s of fuse left ({release + fuse:F2} s in all, data {settings.FragFuse:F1})");

			yield return Wait(2.0f);

			// Held through the whole fuse: it goes off in your hand
			server = ServerCopy(playerRef);
			server.Health.DebugSetHealth(150.0f);
			server.Health.SetArmor(0, 0.0f);
			GiveEquipment(EEquipment.Frag);
			yield return Wait(0.5f);
			start = EquipmentDebug.EventLog.Count;
			yield return HoldEquipment(settings.FragFuse + 0.4f);
			yield return Wait(0.6f);

			server = ServerCopy(playerRef);
			bool dropped = EquipmentDebug.EventLog.Skip(start).Any(t => t.StartsWith($"THROW {name} Frag Grenade dropped at the feet"));
			float health = server.Health.CurrentHealth;
			Check(dropped == true && Near(health, 150.0f - settings.FragDamage, 0.6f),
				$"Frag held for its whole {settings.FragFuse:F1} s fuse: went off in the hand, thrower's health 150 -> {health:F1} (the thrower takes the full {settings.FragDamage:F0})");

			server.Health.DebugSetHealth(150.0f);

			// Smoke: pops SmokeFuse after the release (its fade is checked at the end)
			GiveEquipment(EEquipment.Smoke);
			yield return Wait(0.5f);
			start = EquipmentDebug.EventLog.Count;
			yield return HoldEquipment(0.1f);
			yield return WaitFor(start, t => t.StartsWith("DETONATE smoke") && t.Contains("popped"), 4.0f);

			int releaseTick = ReadTick(start, $"THROW {name} Smoke Grenade released");
			int popTick     = ReadTick(start, "DETONATE smoke");
			float pop       = (popTick - releaseTick) * TICK;
			_smokeReleaseTick = popTick;
			Check(releaseTick > 0 && popTick > 0 && Near(pop, settings.SmokeFuse, 2.0f * TICK + 0.001f),
				$"Smoke: popped {pop:F3} s after it left the hand (data {settings.SmokeFuse:F1} s), grows to {settings.SmokeDiameter:F0} m over {settings.SmokeGrowTime:F1} s");

			yield return CaptureFromOtherPeer(playerRef, "Smoke", ServerCopy(playerRef).transform.position + new Vector3(-12.0f, 1.0f, -12.0f), 1.5f);
		}

		// 2. Frag damage

		private static IEnumerator CheckFrag(PlayerRef playerRef, Agent host, Vector3 center)
		{
			EquipmentSettings settings = EquipmentSettings.Current;
			Agent             server   = ServerCopy(playerRef);
			GameplayMode      mode     = server.Context.GameplayMode;
			byte              team     = mode.NewDummyTeam();
			Vector3           blast    = center + new Vector3(-22.0f, 0.0f, 22.0f); // Clear of the test blocks at +X and +Z

			float[] distances = { 3.0f, 6.0f, 8.0f, 10.0f, 12.0f };
			var     dummies   = new List<Agent>();

			for (int i = 0; i < distances.Length; ++i)
			{
				Vector3 direction = Quaternion.Euler(0.0f, 30.0f + i * 60.0f, 0.0f) * Vector3.forward;
				dummies.Add(mode.SpawnDummy(blast + direction * distances[i], Quaternion.identity, team));
			}

			// Behind a wall at 5 m, and tier 2 armor at 3 m
			Vector3 wallDirection = Quaternion.Euler(0.0f, 330.0f, 0.0f) * Vector3.forward;
			Agent   walled        = mode.SpawnDummy(blast + wallDirection * 5.0f, Quaternion.identity, team);
			BuildBox(blast + wallDirection * 2.5f + Vector3.up * 1.5f, new Vector3(3.0f, 3.0f, 0.3f), Quaternion.LookRotation(wallDirection));

			Vector3 armoredSpot = blast + Quaternion.Euler(0.0f, 270.0f, 0.0f) * Vector3.forward * 3.0f;
			Agent   armored     = mode.SpawnDummy(armoredSpot, Quaternion.identity, team);

			// The thrower at 4 m and the teammate at 5 m
			PlaceAll(playerRef, blast + Quaternion.Euler(0.0f, 210.0f, 0.0f) * Vector3.forward * 4.0f);
			bool teammates = host != null && GameplayMode.AreTeammates(server, host) == true;
			if (host != null)
			{
				PlaceAll(host.Object.InputAuthority, blast + Quaternion.Euler(0.0f, 150.0f, 0.0f) * Vector3.forward * 5.0f);
			}

			yield return Wait(2.0f);

			server = ServerCopy(playerRef);
			foreach (Agent dummy in dummies.Append(walled))
			{
				dummy.Health.SetArmor(0, 0.0f);
				dummy.Health.DebugSetHealth(150.0f);
			}

			armored.Health.SetArmor(2, HealthSettings.Current.GetArmorTier(2).Durability);
			armored.Health.DebugSetHealth(150.0f);
			server.Health.SetArmor(0, 0.0f);
			server.Health.DebugSetHealth(150.0f);
			if (host != null)
			{
				host.Health.DebugSetHealth(150.0f);
			}

			SpawnGrenade(server, EEquipment.Frag, blast + Vector3.up * 0.15f, 0.3f);
			yield return Wait(1.0f);

			for (int i = 0; i < distances.Length; ++i)
			{
				float expected = settings.FragDamage * settings.GetFalloff(distances[i]);
				float taken    = 150.0f - dummies[i].Health.CurrentHealth;
				Check(Near(taken, expected, 1.0f), $"Frag at {distances[i]:F0} m: {taken:F1} damage (expected {expected:F1})");
			}

			Check(Near(walled.Health.CurrentHealth, 150.0f, 0.01f), $"Frag behind a wall (5 m): {150.0f - walled.Health.CurrentHealth:F1} damage (expected 0)");

			float armoredTaken = 150.0f - armored.Health.CurrentHealth;
			float armorCut     = HealthSettings.Current.GetArmorTier(2).BodyCut;
			float armorLeft    = armored.Health.ArmorLeft;
			Check(Near(armoredTaken, settings.FragDamage * (1.0f - armorCut), 1.0f) && Near(armorLeft, HealthSettings.Current.GetArmorTier(2).Durability - settings.FragDamage, 1.0f),
				$"Frag on tier 2 armor at 3 m: {armoredTaken:F1} damage (cut {armorCut * 100.0f:F0}%), armor {armorLeft:F0} left (wears by the full {settings.FragDamage:F0})");

			server = ServerCopy(playerRef);
			float selfExpected = settings.FragDamage * settings.GetFalloff(4.0f);
			Check(Near(150.0f - server.Health.CurrentHealth, selfExpected, 1.0f), $"Frag on its thrower at 4 m: {150.0f - server.Health.CurrentHealth:F1} damage (expected {selfExpected:F1})");

			if (host != null && teammates == true)
			{
				Check(Near(host.Health.CurrentHealth, 150.0f, 0.01f), $"Frag on the thrower's teammate at 5 m: {150.0f - host.Health.CurrentHealth:F1} damage (expected 0, no friendly fire)");
			}
			else
			{
				Log("  (the other player isn't a teammate here; teammate step skipped)");
			}

			yield return CaptureFromOtherPeer(playerRef, "Frag", blast, 0.0f);

			mode.RemoveDummies();
			server.Health.DebugSetHealth(150.0f);
			if (host != null)
			{
				host.Health.DebugSetHealth(150.0f);
				PlaceAll(host.Object.InputAuthority, center + new Vector3(0.0f, 0.0f, -8.0f));
			}

			PlaceAll(playerRef, center);
			yield return Wait(1.0f);
		}

		// 3. Flash and concussion

		private static IEnumerator CheckFlashAndConcussion(PlayerRef playerRef, NetworkRunner viewRunner, Vector3 center)
		{
			EquipmentSettings settings = EquipmentSettings.Current;
			Agent             server   = ServerCopy(playerRef);
			GameplayMode      mode     = server.Context.GameplayMode;
			byte              team     = mode.NewDummyTeam();
			Vector3           blast    = center + new Vector3(-20.0f, 0.0f, 0.0f);

			// Facing it at 3, 8 and 12 m; facing away at 3 m
			float[] distances = { 3.0f, 8.0f, 12.0f, 3.0f };
			bool[]  facing    = { true, true, true, false };
			var     dummies   = new List<Agent>();

			for (int i = 0; i < distances.Length; ++i)
			{
				Vector3 direction = Quaternion.Euler(0.0f, i * 90.0f, 0.0f) * Vector3.forward;
				Agent   dummy     = mode.SpawnDummy(blast + direction * distances[i], Quaternion.identity, team);
				dummies.Add(dummy);
			}

			PlaceAll(playerRef, blast + Quaternion.Euler(0.0f, 45.0f, 0.0f) * Vector3.forward * 4.0f);
			yield return Wait(1.5f);

			for (int i = 0; i < dummies.Count; ++i)
			{
				Vector3 toBlast = blast - dummies[i].transform.position;
				SetLook(dummies[i], facing[i] == true ? toBlast : -toBlast);
			}

			server = ServerCopy(playerRef);
			LookAt(playerRef, blast);
			yield return Wait(0.5f);

			SpawnGrenade(server, EEquipment.Flash, blast + Vector3.up * 0.15f, 0.2f);
			yield return Wait(0.45f);

			for (int i = 0; i < dummies.Count; ++i)
			{
				float expected = settings.GetFalloff(distances[i]) * (facing[i] == true ? 1.0f : settings.FlashFacingAwayMultiplier);
				float value    = dummies[i].Senses.EyesFlashValue;
				Check(Near(value, expected, 0.03f), $"Flash at {distances[i]:F0} m {(facing[i] ? "facing it" : "facing away")}: whiteout {value:F2} (expected {expected:F2})");
			}

			server = ServerCopy(playerRef);
			Agent view = ViewCopy(playerRef, viewRunner);
			Check(server.Senses.EyesFlashValue > 0.9f && view != null && view.Senses.EyesFlashValue > 0.5f,
				$"Flash on its thrower at 4 m looking at it: whiteout {server.Senses.EyesFlashValue:F2} on the server, {(view != null ? view.Senses.EyesFlashValue : 0.0f):F2} on the thrower's screen");

			yield return Wait(4.5f);

			// Concussion, same spots (all facing it doesn't matter)
			server = ServerCopy(playerRef);
			SpawnGrenade(server, EEquipment.Concussion, blast + Vector3.up * 0.15f, 0.2f);
			yield return Wait(0.6f);

			for (int i = 0; i < 3; ++i)
			{
				float strength = settings.GetFalloff(distances[i]);
				AgentSenses senses = dummies[i].Senses;
				float duration = senses.IsConcussed == true ? (senses.ConcussEndTick - senses.ConcussStartTick) * TICK : 0.0f;
				float slow     = senses.GetConcussionSlow();
				float expectedSlow = Mathf.Lerp(1.0f, settings.ConcussionSlow, strength);
				Check(Near(senses.IsConcussed ? senses.ConcussStrength : 0.0f, strength, 0.03f) && Near(duration, settings.ConcussionMaxDuration * strength, 2.0f * TICK) && Near(slow, strength > 0.0f ? expectedSlow : 1.0f, 0.03f),
					$"Concussion at {distances[i]:F0} m: strength {(senses.IsConcussed ? senses.ConcussStrength : 0.0f):F2} (expected {strength:F2}), dizzy {duration:F2} s, look and movement at {slow * 100.0f:F0}% (expected {(strength > 0.0f ? expectedSlow : 1.0f) * 100.0f:F0}%)");
			}

			server = ServerCopy(playerRef);
			view   = ViewCopy(playerRef, viewRunner);
			Check(server.Senses.IsConcussed == true && view != null && view.Senses.GetConcussionValue(false) > 0.0f,
				$"Concussion on its thrower at 4 m: dizzy on the server and on the thrower's screen ({(view != null ? view.Senses.GetConcussionValue(false) : 0.0f):F2})");

			yield return CaptureFromOtherPeer(playerRef, "Concussed", blast, 0.0f, true);

			// Slowed: walking speed in the first 2 s
			AgentInput.DebugMoveDirection = Vector2.up;
			yield return Wait(0.8f);
			server = ServerCopy(playerRef);
			float speed = server.Character.CharacterController.FixedData.RealVelocity.OnlyXZ().magnitude;
			AgentInput.DebugMoveDirection = Vector2.zero;
			float expectedSpeed = MovementSettings.Current.WalkSpeed * Mathf.Lerp(1.0f, settings.ConcussionSlow, settings.GetFalloff(4.0f));
			Check(Near(speed, expectedSpeed, 0.3f), $"Concussed walking: {speed:F2} m/s (expected {expectedSpeed:F2})");

			mode.RemoveDummies();
			yield return Wait(4.0f);
			PlaceAll(playerRef, center);
			yield return Wait(1.0f);
		}

		// 4. E refused while reloading, healing or downed

		private static IEnumerator CheckRefusals(PlayerRef playerRef, Vector3 center)
		{
			Agent  server = ServerCopy(playerRef);
			string name   = server.name;
			int    icarus = ItemCatalog.GetGunIndex("Icarus");

			server.Weapons.AddGunFromLoot(LootStack.Gun(icarus, 3, 0), Weapons.FIRST_PRIMARY_SLOT, false);
			server.Weapons.AddAmmo(ItemCatalog.GetGunData(icarus).Caliber, 150);
			GiveEquipment(EEquipment.Frag);
			yield return Wait(1.0f);

			// Reloading
			AgentInput.DebugHoldReload = true;
			yield return Wait(0.3f);
			AgentInput.DebugHoldReload = false;
			int start = EquipmentDebug.EventLog.Count;
			yield return HoldEquipment(0.15f);
			yield return Wait(0.5f);
			bool refusedReload = EquipmentDebug.EventLog.Skip(start).Any(t => t.StartsWith($"REFUSED {name} E: reloading"));
			bool threwReload   = EquipmentDebug.EventLog.Skip(start).Any(t => t.StartsWith($"THROW {name}"));
			Check(refusedReload == true && threwReload == false, "E while reloading: refused, nothing thrown");
			yield return Wait(3.0f);

			// Healing
			server = ServerCopy(playerRef);
			server.Health.DebugSetHealth(100.0f);
			server.Inventory.DebugSetHealthSlot(EHealItem.MedKit);
			yield return Wait(0.5f);
			AgentInput.DebugHoldHeal = true;
			yield return Wait(0.5f);
			start = EquipmentDebug.EventLog.Count;
			yield return HoldEquipment(0.15f);
			yield return Wait(0.4f);
			AgentInput.DebugHoldHeal = false;
			bool refusedHeal = EquipmentDebug.EventLog.Skip(start).Any(t => t.StartsWith($"REFUSED {name} E: healing"));
			bool threwHeal   = EquipmentDebug.EventLog.Skip(start).Any(t => t.StartsWith($"THROW {name}"));
			Check(refusedHeal == true && threwHeal == false, "E while healing: refused, nothing thrown");
			yield return Wait(0.5f);

			// Downed: E does nothing until revived
			server = ServerCopy(playerRef);
			if (server.Context.GameplayMode.CanBeDowned(server) == true)
			{
				server.Health.DebugSetHealth(150.0f);
				server.Health.SetArmor(0, 0.0f);
				var hit = new HitData { Action = EHitAction.Damage, Amount = 160.0f, Position = server.transform.position, Normal = Vector3.up, Direction = Vector3.forward, Target = server.Health, HitType = EHitType.None };
				HitUtility.ProcessHit(ref hit);
				yield return Wait(1.0f);

				start = EquipmentDebug.EventLog.Count;
				yield return HoldEquipment(0.15f);
				yield return Wait(1.0f);

				server = ServerCopy(playerRef);
				bool threwDowned = EquipmentDebug.EventLog.Skip(start).Any(t => t.StartsWith($"THROW {name}"));
				Check(server.Health.IsDowned == true && threwDowned == false && server.EquipmentSlot.IsEmpty == false, "E while downed: nothing happens, the frag stays in the slot");

				server.Health.Revive();
				yield return Wait(1.0f);
				server.Health.DebugSetHealth(150.0f);
			}
			else
			{
				Log("  (this player can't be downed here; downed step skipped)");
			}

			EmptyAll(ServerCopy(playerRef));
			yield return Wait(1.0f);
		}

		// 5. Grapple

		private static IEnumerator CheckGrapple(PlayerRef playerRef, NetworkRunner viewRunner, Agent host, Vector3 center)
		{
			EquipmentSettings settings = EquipmentSettings.Current;
			string            name     = ServerCopy(playerRef).name;

			// A roof: 4 m tall block 9..15 m ahead; hook its top 0.5 m past the edge
			Vector3 roofCenter = center + new Vector3(0.0f, 2.0f, 12.0f);
			Vector3 roofSpot   = center + new Vector3(0.0f, 4.0f, 9.5f);

			PlaceAll(playerRef, center);
			yield return Wait(1.0f);
			LookAt(playerRef, roofSpot);
			GiveEquipment(EEquipment.Grapple);
			yield return Wait(0.8f);

			int  start      = EquipmentDebug.EventLog.Count;
			bool ropeSeen   = false;
			float maxError  = 0.0f;
			yield return HoldEquipment(0.1f);

			double end = EditorApplication.timeSinceStartup + 5.0;
			while (EditorApplication.timeSinceStartup < end && EquipmentDebug.EventLog.Skip(start).Any(t => t.StartsWith($"GRAPPLE {name} gun ready")) == false)
			{
				ropeSeen |= IsRopeShownOnPeer(playerRef, host != null ? host.Runner : null);
				yield return null;
			}

			yield return Wait(1.0f);

			Agent server = ServerCopy(playerRef);
			Agent view   = ViewCopy(playerRef, viewRunner);
			float total  = ReadSeconds(start, $"GRAPPLE {name} gun ready", @"ready ([\d.]+) s");
			float height = server.transform.position.y - FLOOR_HEIGHT;
			maxError     = view != null ? Vector3.Distance(view.transform.position, server.transform.position) : -1.0f;
			bool hooked  = EquipmentDebug.EventLog.Skip(start).Any(t => t.StartsWith($"GRAPPLE {name} hooked"));
			int  pulls   = server.EquipmentSlot.Kind == EItemKind.Equipment ? server.EquipmentSlot.Charges : 0;

			Check(hooked == true && height > 3.8f && height < 4.4f && total > 2.0f && total < 3.2f && pulls == settings.GrapplePulls - 1,
				$"Grapple onto a 4 m roof: hooked, standing {height:F2} m up, gun ready {total:F2} s after the press (reference 2.5), {pulls} of {settings.GrapplePulls} pulls left");
			Check(maxError >= 0.0f && maxError < 0.2f, $"Grapple: the lagged client's copy settles {maxError:F3} m from the server's after the pull");
			Check(host == null || ropeSeen == true, $"Grapple: the rope shows on the other peer ({(host != null ? (ropeSeen ? "seen" : "not seen") : "no other peer")})");

			// Off the roof, then a 2 m ledge: hook the wall face 0.2 m below its top
			PlaceAll(playerRef, center);
			yield return Wait(1.5f);

			Vector3 ledgeSpot = center + new Vector3(9.0f, 1.8f, 0.0f);
			LookAt(playerRef, ledgeSpot);
			yield return Wait(0.5f);

			start = EquipmentDebug.EventLog.Count;
			yield return HoldEquipment(0.1f);
			yield return WaitFor(start, t => t.StartsWith($"GRAPPLE {name} gun ready"), 5.0f);
			yield return Wait(1.2f);

			server = ServerCopy(playerRef);
			height = server.transform.position.y - FLOOR_HEIGHT;
			bool onLedge = height > 1.8f && height < 2.4f;
			pulls  = server.EquipmentSlot.Kind == EItemKind.Equipment ? server.EquipmentSlot.Charges : 0;
			Check(onLedge == true && pulls == settings.GrapplePulls - 2, $"Grapple onto a 2 m ledge (hooked on its face): standing {height:F2} m up, {pulls} pulls left");

			// A miss: the sky
			PlaceAll(playerRef, center);
			yield return Wait(1.5f);
			LookAt(playerRef, center + new Vector3(0.0f, 100.0f, -20.0f));
			yield return Wait(0.5f);

			start = EquipmentDebug.EventLog.Count;
			yield return HoldEquipment(0.1f);
			yield return WaitFor(start, t => t.StartsWith($"GRAPPLE {name} gun ready"), 4.0f);

			server = ServerCopy(playerRef);
			float missReady = ReadSeconds(start, $"GRAPPLE {name} gun ready", @"ready ([\d.]+) s");
			bool  missed    = EquipmentDebug.EventLog.Skip(start).Any(t => t.StartsWith($"GRAPPLE {name} missed"));
			int   kept      = server.EquipmentSlot.Kind == EItemKind.Equipment ? server.EquipmentSlot.Charges : 0;
			Check(missed == true && kept == pulls && Near(missReady, settings.GrappleDeployTime + settings.GrappleMissRecover, 2.0f * TICK + 0.001f),
				$"Grapple miss: nothing used ({kept} pulls left), gun back {missReady:F2} s after the press");

			// A picture of the rope from the other peer
			LookAt(playerRef, roofSpot);
			yield return Wait(0.5f);
			start = EquipmentDebug.EventLog.Count;
			yield return HoldEquipment(0.1f);
			yield return WaitFor(start, t => t.StartsWith($"GRAPPLE {name} pull start"), 3.0f);
			yield return CaptureFromOtherPeer(playerRef, "Grapple", roofSpot, 0.0f);
			yield return WaitFor(start, t => t.StartsWith($"GRAPPLE {name} gun ready"), 4.0f);

			EmptyAll(ServerCopy(playerRef));
			PlaceAll(playerRef, center);
			yield return Wait(1.0f);
		}

		// 6. Gear: the timer and Double Time

		private static IEnumerator CheckGearTimerAndDoubleTime(PlayerRef playerRef, Vector3 center)
		{
			EquipmentSettings settings = EquipmentSettings.Current;
			MovementSettings  movement = MovementSettings.Current;
			string            name     = ServerCopy(playerRef).name;

			PlaceAll(playerRef, center + new Vector3(0.0f, 0.0f, -10.0f));
			yield return Wait(1.0f);
			LookAt(playerRef, center + new Vector3(0.0f, 1.7f, -60.0f)); // Away from the blocks
			GiveGear(EGear.DoubleTime);
			yield return Wait(0.5f);

			// On for about 1.5 s, then off: the rest is kept
			int start = EquipmentDebug.EventLog.Count;
			yield return HoldGear();
			yield return Wait(1.5f);
			yield return HoldGear();
			yield return Wait(0.5f);

			int   onTick  = ReadTick(start, $"GEAR ON {name}");
			int   offTick = ReadTick(start, $"GEAR OFF {name}");
			Agent server  = ServerCopy(playerRef);
			float left    = server.GearSlot.SecondsLeft;
			float used    = (offTick - onTick) * TICK;
			Check(onTick > 0 && offTick > onTick && Near(left, settings.GearSeconds - used, 2.0f * TICK) && server.GearOn == false,
				$"Gear timer: on for {used:F2} s, then off with {left:F2} s left (of {settings.GearSeconds:F0}), and it stays at that while off");

			yield return Wait(1.0f);
			server = ServerCopy(playerRef);
			Check(Near(server.GearSlot.SecondsLeft, left, 0.001f), $"Gear timer: still {server.GearSlot.SecondsLeft:F2} s left after a second off");

			// On again: walking and sprinting at 1.4x, until it breaks
			start = EquipmentDebug.EventLog.Count;
			yield return HoldGear();
			yield return Wait(0.3f);

			AgentInput.DebugMoveDirection = Vector2.up;
			yield return Wait(1.2f);
			server = ServerCopy(playerRef);
			float walk = server.Character.CharacterController.FixedData.RealVelocity.OnlyXZ().magnitude;

			AgentInput.DebugHoldSprint = true;
			yield return Wait(1.5f);
			server = ServerCopy(playerRef);
			float sprint = server.Character.CharacterController.FixedData.RealVelocity.OnlyXZ().magnitude;
			AgentInput.DebugHoldSprint    = false;
			AgentInput.DebugMoveDirection = Vector2.zero;

			float expectedWalk   = movement.WalkSpeed * settings.DoubleTimeMultiplier;
			float expectedSprint = movement.SprintSpeed * settings.DoubleTimeMultiplier;
			Check(Near(walk, expectedWalk, 0.1f) && Near(sprint, expectedSprint, 0.1f),
				$"Double Time: walking {walk:F2} m/s (expected {expectedWalk:F2}), sprinting {sprint:F2} m/s (expected {expectedSprint:F2})");

			yield return WaitFor(start, t => t.StartsWith($"GEAR BROKE {name}"), left + 2.0f);

			int   breakTick = ReadTick(start, $"GEAR BROKE {name}");
			int   onAgain   = ReadTick(start, $"GEAR ON {name}");
			server = ServerCopy(playerRef);
			Check(breakTick > 0 && Near((breakTick - onAgain) * TICK, left, 2.0f * TICK) && server.GearSlot.IsEmpty == true && server.GearOn == false,
				$"Gear item breaks when its seconds are spent: {(breakTick - onAgain) * TICK:F2} s after switching on again (had {left:F2} s), Gear slot empty");

			// Walking speed is back to normal (from a fresh spot, so the earlier runs don't reach the floor's edge)
			PlaceAll(playerRef, center + new Vector3(0.0f, 0.0f, -10.0f));
			yield return Wait(1.0f);
			AgentInput.DebugMoveDirection = Vector2.up;
			yield return Wait(1.2f);
			server = ServerCopy(playerRef);
			walk = server.Character.CharacterController.FixedData.RealVelocity.OnlyXZ().magnitude;
			AgentInput.DebugMoveDirection = Vector2.zero;
			Check(Near(walk, movement.WalkSpeed, 0.1f), $"After Double Time: walking {walk:F2} m/s (normal {movement.WalkSpeed:F2})");

			PlaceAll(playerRef, center);
			yield return Wait(1.0f);
		}

		// 7. Double Jump

		private static IEnumerator CheckDoubleJump(PlayerRef playerRef, Vector3 center)
		{
			MovementSettings movement = MovementSettings.Current;

			LookAt(playerRef, center + new Vector3(0.0f, 1.7f, -60.0f));
			GiveGear(EGear.DoubleJump);
			yield return Wait(0.5f);
			yield return HoldGear();
			yield return Wait(0.5f);

			float single = 0.0f;
			float doubled = 0.0f;

			// First a single jump for reference, then jump again near the top
			yield return MeasureJump(playerRef, false, h => single = h);
			yield return Wait(1.0f);
			yield return MeasureJump(playerRef, true, h => doubled = h);

			Check(single > movement.JumpHeight - 0.1f && single < movement.JumpHeight + 0.15f && doubled > movement.JumpHeight * 1.75f,
				$"Double Jump: one jump {single:F2} m (data {movement.JumpHeight:F1}), jumping again near the top reaches {doubled:F2} m");

			yield return HoldGear();
			yield return Wait(0.5f);
			yield return MeasureJump(playerRef, true, h => doubled = h);
			Check(doubled < movement.JumpHeight + 0.15f, $"Double Jump switched off: the second press does nothing ({doubled:F2} m)");

			EmptyAll(ServerCopy(playerRef));
			yield return Wait(0.5f);
		}

		private static IEnumerator MeasureJump(PlayerRef playerRef, bool second, System.Action<float> result)
		{
			float floor = ServerCopy(playerRef).transform.position.y;
			float peak  = floor;

			AgentInput.DebugHoldJump = true;
			yield return Wait(0.1f);
			AgentInput.DebugHoldJump = false;

			// The second press goes in near the top of the first jump, as the client sees it (its rise has almost stopped)
			double end       = EditorApplication.timeSinceStartup + 2.2;
			double releaseAt = 0.0;
			bool   pressed   = false;
			Agent  view      = ViewCopy(playerRef, FindObservedAgent()?.Runner);

			while (EditorApplication.timeSinceStartup < end)
			{
				float rise = view != null ? view.Character.CharacterController.FixedData.RealVelocity.y : 0.0f;
				float up   = view != null ? view.transform.position.y - floor : 0.0f;

				if (second == true && pressed == false && up > 0.8f && rise < 1.0f)
				{
					pressed   = true;
					releaseAt = EditorApplication.timeSinceStartup + 0.1;
					AgentInput.DebugHoldJump = true;
				}
				else if (pressed == true && AgentInput.DebugHoldJump == true && EditorApplication.timeSinceStartup >= releaseAt)
				{
					AgentInput.DebugHoldJump = false;
				}

				peak = Mathf.Max(peak, ServerCopy(playerRef).transform.position.y);
				yield return null;
			}

			AgentInput.DebugHoldJump = false;
			result(peak - floor);
		}

		// 8. Ninja

		private static IEnumerator CheckNinja(PlayerRef playerRef, NetworkRunner viewRunner, Vector3 center)
		{
			EquipmentSettings settings = EquipmentSettings.Current;

			PlaceAll(playerRef, center + new Vector3(0.0f, 0.0f, -10.0f));
			LookAt(playerRef, center + new Vector3(0.0f, 1.7f, -60.0f));
			yield return Wait(0.5f);

			float walkNormal = 0.0f, walkNinja = 0.0f, sprintNinja = 0.0f;

			yield return MeasureFootstepRange(playerRef, viewRunner, false, r => walkNormal = r);

			GiveGear(EGear.Ninja);
			yield return Wait(0.5f);
			yield return HoldGear();
			yield return Wait(0.3f);

			PlaceAll(playerRef, center + new Vector3(0.0f, 0.0f, -10.0f));
			yield return Wait(0.3f);
			yield return MeasureFootstepRange(playerRef, viewRunner, false, r => walkNinja = r);
			PlaceAll(playerRef, center + new Vector3(0.0f, 0.0f, -10.0f));
			yield return Wait(0.3f);
			yield return MeasureFootstepRange(playerRef, viewRunner, true, r => sprintNinja = r);

			Check(Near(walkNormal, settings.FootstepRange, 0.01f) && Near(walkNinja, settings.FootstepRange * settings.NinjaRangeMultiplier, 0.01f) && Near(sprintNinja, settings.SprintRange * settings.NinjaRangeMultiplier, 0.01f),
				$"Ninja: footsteps heard to {walkNormal:F1} m normally, {walkNinja:F1} m with Ninja, sprinting {sprintNinja:F1} m (expected {settings.FootstepRange:F0} / {settings.FootstepRange * settings.NinjaRangeMultiplier:F0} / {settings.SprintRange * settings.NinjaRangeMultiplier:F0})");

			EmptyAll(ServerCopy(playerRef));
			PlaceAll(playerRef, center);
			yield return Wait(0.5f);
		}

		/// <summary>
		/// The range a step gets while walking or sprinting, as the other peer would play it (the other peer's copy of the player; its
		/// footsteps only play while that peer is on screen, so the range is applied directly the way a step applies it).
		/// </summary>
		private static IEnumerator MeasureFootstepRange(PlayerRef playerRef, NetworkRunner viewRunner, bool sprint, System.Action<float> result)
		{
			AgentInput.DebugMoveDirection = Vector2.up;
			AgentInput.DebugHoldSprint    = sprint;
			yield return Wait(1.2f);

			Agent          server    = ServerCopy(playerRef);
			Agent          other     = FindOtherPlayer(server.Runner, playerRef) != null ? ViewCopy(playerRef, server.Runner) : ViewCopy(playerRef, viewRunner);
			AgentFootsteps footsteps = other != null ? other.GetComponent<AgentFootsteps>() : null;
			AudioEffect    effect    = GetFootEffect(other, "_leftFootEffect");
			MethodInfo     apply     = typeof(AgentFootsteps).GetMethod("ApplyRange", BindingFlags.Instance | BindingFlags.NonPublic);

			float range = -1.0f;
			if (footsteps != null && effect != null && apply != null)
			{
				apply.Invoke(footsteps, new object[] { effect });
				range = effect.AudioSource.maxDistance;
			}

			AgentInput.DebugMoveDirection = Vector2.zero;
			AgentInput.DebugHoldSprint    = false;

			result(range);
			yield return Wait(0.5f);
		}

		private static AudioEffect GetFootEffect(Agent agent, string field)
		{
			AgentFootsteps footsteps = agent != null ? agent.GetComponent<AgentFootsteps>() : null;
			FieldInfo      info      = typeof(AgentFootsteps).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
			return footsteps != null && info != null ? info.GetValue(footsteps) as AudioEffect : null;
		}

		// 9. Sixth Sense

		private static IEnumerator CheckSixthSense(PlayerRef playerRef, NetworkRunner viewRunner, Vector3 center)
		{
			EquipmentSettings settings = EquipmentSettings.Current;
			Agent             server   = ServerCopy(playerRef);
			string            name     = server.name;
			GameplayMode      mode     = server.Context.GameplayMode;
			byte              team     = mode.NewDummyTeam();

			Agent watcher = mode.SpawnDummy(center + new Vector3(6.0f, 0.0f, -6.0f), Quaternion.identity, team);
			yield return Wait(1.0f);
			watcher.GearSlot = LootStack.Gear(EGear.SixthSense);
			watcher.GearOn   = true;

			// The player fires an unsuppressed gun
			int icarus = ItemCatalog.GetGunIndex("Icarus");
			server.Weapons.AddGunFromLoot(LootStack.Gun(icarus, 30, 0), Weapons.FIRST_PRIMARY_SLOT, false);
			LookAt(playerRef, center + new Vector3(0.0f, 30.0f, 40.0f));
			yield return Wait(1.2f);

			int start = EquipmentDebug.EventLog.Count;
			AgentInput.DebugHoldAttack = true;
			yield return Wait(0.3f);
			AgentInput.DebugHoldAttack = false;
			yield return Wait(4.6f);

			bool marked = EquipmentDebug.EventLog.Skip(start).Any(t => t.StartsWith($"MARK {watcher.name} Sixth Sense marked {name}"));
			var  blinks = EquipmentDebug.EventLog.Skip(start).Where(t => t.StartsWith($"MARK {watcher.name} Sixth Sense blink")).Select(TickOf).ToList();
			float interval = blinks.Count >= 2 ? (blinks[1] - blinks[0]) * TICK : 0.0f;
			Check(marked == true && blinks.Count >= 2 && Near(interval, settings.SixthSenseInterval, TICK + 0.001f),
				$"Sixth Sense: an unsuppressed shot marks the shooter; {blinks.Count} blinks in 4.6 s, {interval:F2} s apart (data {settings.SixthSenseInterval:F0})");

			// Suppressed: a second watcher doesn't mark
			watcher.GearOn = false;
			watcher.Gear.SwitchOff(null);
			Agent quiet = mode.SpawnDummy(center + new Vector3(-6.0f, 0.0f, -6.0f), Quaternion.identity, team);
			yield return Wait(1.0f);
			quiet.GearSlot = LootStack.Gear(EGear.SixthSense);
			quiet.GearOn   = true;

			server = ServerCopy(playerRef);
			GunplaySettings gunplay    = GunplaySettings.Current;
			AttachmentData  suppressor = gunplay.FindAttachment("Suppressor");
			BulletWeapon    gun        = server.Weapons.GetWeapon(Weapons.FIRST_PRIMARY_SLOT) as BulletWeapon;
			bool            fitted     = gun != null && suppressor != null && gun.SetAttachment(suppressor.Slot, suppressor, out _) == true;
			yield return Wait(1.5f);

			start = EquipmentDebug.EventLog.Count;
			AgentInput.DebugHoldAttack = true;
			yield return Wait(0.3f);
			AgentInput.DebugHoldAttack = false;
			yield return Wait(1.0f);

			bool quietMarked = EquipmentDebug.EventLog.Skip(start).Any(t => t.StartsWith($"MARK {quiet.name} Sixth Sense marked"));
			Check(fitted == true && quietMarked == false, $"Sixth Sense: a suppressed shot marks nobody{(fitted ? "" : " (couldn't fit the Suppressor)")}");

			// The player with Sixth Sense: a dummy's unsuppressed shot blinks on the player's minimap (RPC to the player only)
			GiveGear(EGear.SixthSense);
			yield return Wait(0.5f);
			yield return HoldGear();
			yield return Wait(0.5f);

			Agent view     = ViewCopy(playerRef, viewRunner);
			float before   = view != null ? view.SixthSenseBlinkTime : 0.0f;
			AgentGear.OnShotFired(watcher.Object);
			yield return Wait(1.0f);

			view = ViewCopy(playerRef, viewRunner);
			bool  blinked = view != null && view.SixthSenseBlinkTime > before && view.SixthSenseSpotCount >= 1;
			float offset  = blinked == true ? Vector3.Distance(view.SixthSenseSpots[0], watcher.transform.position) : -1.0f;
			Check(blinked == true && offset < 1.0f, $"Sixth Sense on the player: a dummy's shot blinks on the player's screen at the dummy's spot ({offset:F2} m off)");

			mode.RemoveDummies();
			EmptyAll(ServerCopy(playerRef));
			yield return Wait(0.5f);
		}

		// 10. Camouflage

		private static IEnumerator CheckCamouflage(PlayerRef playerRef, Agent host, Vector3 center)
		{
			EquipmentSettings settings = EquipmentSettings.Current;

			PlaceAll(playerRef, center + new Vector3(0.0f, 0.0f, 3.0f));
			LookAt(playerRef, center + new Vector3(0.0f, 1.7f, 60.0f));
			GiveGear(EGear.Camouflage);
			yield return Wait(1.0f);
			yield return HoldGear();
			yield return Wait(settings.CamouflageStillTime + 0.6f);

			Agent server   = ServerCopy(playerRef);
			Agent hostCopy = host != null ? ViewCopy(playerRef, host.Runner) : null;
			bool  faded    = hostCopy != null && IsFaded(hostCopy);
			Check(server.IsCamouflaged == true && (host == null || faded == true),
				$"Camouflage: standing still with it on makes the player see-through on the other peer ({(host != null ? (faded ? "faded" : "not faded") : "no other peer")})");

			yield return CaptureFromOtherPeer(playerRef, "Camouflage", server.transform.position + Vector3.up * 1.0f, 0.0f);

			AgentInput.DebugMoveDirection = Vector2.up;
			yield return Wait(0.6f);
			server   = ServerCopy(playerRef);
			hostCopy = host != null ? ViewCopy(playerRef, host.Runner) : null;
			bool camouflagedMoving = server.IsCamouflaged;
			bool fadedMoving       = hostCopy != null && IsFaded(hostCopy);
			AgentInput.DebugMoveDirection = Vector2.zero;

			Check(camouflagedMoving == false && fadedMoving == false, "Camouflage: moving brings the player back at once");

			EmptyAll(ServerCopy(playerRef));
			PlaceAll(playerRef, center);
			yield return Wait(0.5f);
		}

		private static bool IsFaded(Agent agent)
		{
			return agent.GetComponentsInChildren<Renderer>(false).Any(r => r.sharedMaterials.Any(m => m != null && m.name.EndsWith("(Camouflage)")));
		}

		// 11. O2 Max

		private static void CheckO2Max(PlayerRef playerRef)
		{
			EquipmentSettings settings = EquipmentSettings.Current;

			GiveGear(EGear.O2Max);
			Agent server = ServerCopy(playerRef);
			float breathOff = server.Weapons.HoldBreathTime;
			float recoilOff = AgentGear.GetRecoilMultiplier(server);

			server.GearOn = true;
			float breathOn = server.Weapons.HoldBreathTime;
			float recoilOn = AgentGear.GetRecoilMultiplier(server);
			server.Gear.SwitchOff(null);

			Check(Near(breathOff, GunplaySettings.Current.HoldBreathTime, 0.001f) && Near(breathOn, settings.O2MaxBreathTime, 0.001f) && Near(recoilOff, 1.0f, 0.001f) && Near(recoilOn, settings.O2MaxRecoil, 0.001f),
				$"O2 Max: hold breath {breathOff:F0} s -> {breathOn:F0} s, recoil x{recoilOff:F2} -> x{recoilOn:F2} while it's on");

			EmptyAll(server);
		}

		// 12. The smoke from step 1 fades on time

		private static IEnumerator CheckSmokeFade()
		{
			EquipmentSettings settings = EquipmentSettings.Current;
			float expected = settings.SmokeGrowTime + settings.SmokeDuration;

			yield return WaitFor(0, t => t.StartsWith("DETONATE smoke") && t.Contains("fading"), 20.0f);

			int fadeTick = EquipmentDebug.EventLog.Where(t => t.StartsWith("DETONATE smoke") && t.Contains("fading")).Select(TickOf).LastOrDefault();
			float lasted = fadeTick > 0 && _smokeReleaseTick > 0 ? (fadeTick - _smokeReleaseTick) * TICK : 0.0f;
			Check(Near(lasted, expected, 2.0f * TICK + 0.001f), $"Smoke: started fading {lasted:F2} s after it popped (grow {settings.SmokeGrowTime:F1} + last {settings.SmokeDuration:F0} = {expected:F1} s)");
		}

		// HELPERS

		private static IEnumerator HoldEquipment(float seconds)
		{
			AgentInput.DebugHoldEquipment = true;
			yield return Wait(seconds);
			AgentInput.DebugHoldEquipment = false;
			yield return Wait(0.1f);
		}

		/// <summary>Taps Left Ctrl and waits for the server to switch the gear item (once more if the tap didn't arrive).</summary>
		private static IEnumerator HoldGear()
		{
			for (int attempt = 0; attempt < 2; ++attempt)
			{
				int start = EquipmentDebug.EventLog.Count;

				AgentInput.DebugHoldGear = true;
				yield return Wait(0.15f);
				AgentInput.DebugHoldGear = false;

				yield return WaitFor(start, t => t.StartsWith("GEAR ON") || t.StartsWith("GEAR OFF"), 1.5f);
				if (EquipmentDebug.EventLog.Skip(start).Any(t => t.StartsWith("GEAR ON") || t.StartsWith("GEAR OFF")) == true)
					break;

				Log("  (a Left Ctrl tap didn't reach the server; tapping again)");
			}

			yield return Wait(0.15f);
		}

		/// <summary>Server: a grenade lying at a spot, thrown by this agent, going off after fuse seconds.</summary>
		private static void SpawnGrenade(Agent owner, EEquipment kind, Vector3 position, float fuse)
		{
			KinematicProjectile prefab     = EquipmentSettings.Current.GetGrenadePrefab(kind);
			KinematicProjectile projectile = owner.Runner.Spawn(prefab, position, Quaternion.identity, owner.Object.InputAuthority);
			projectile.Fire(owner.Object, position, Vector3.zero, owner.Weapons.HitMask, EHitType.Grenade);
			projectile.SetDespawnCooldown(fuse);
		}

		private static void SetLook(Agent agent, Vector3 direction)
		{
			var kcc = agent.Character.CharacterController;
			float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
			kcc.FixedData.LookYaw    = yaw;
			kcc.RenderData.LookYaw   = yaw;
			kcc.FixedData.LookPitch  = 0.0f;
			kcc.RenderData.LookPitch = 0.0f;
		}

		/// <summary>True when the rope is drawn on that peer's copy of the player.</summary>
		private static bool IsRopeShownOnPeer(PlayerRef playerRef, NetworkRunner runner)
		{
			Agent copy = ViewCopy(playerRef, runner);
			if (copy == null)
				return false;

			LineRenderer line = copy.GetComponentsInChildren<LineRenderer>(false).FirstOrDefault(t => t.name == "GrappleRope");
			return line != null && line.gameObject.activeInHierarchy == true;
		}

		/// <summary>Takes away guns, ammo, armor, equipment and gear, and refills health.</summary>
		private static void EmptyAll(Agent server)
		{
			if (server == null)
				return;

			for (int slot = Weapons.FIRST_PRIMARY_SLOT; slot <= Weapons.LAST_PRIMARY_SLOT; ++slot)
			{
				server.Weapons.TakeGun(slot);
			}

			for (int i = 0; i < GunData.CaliberCount; ++i)
			{
				server.Weapons.TakeAmmo((ECaliber)i, 10000);
			}

			server.Health.SetArmor(0, 0.0f);
			server.Inventory.Clear();
			server.Health.DebugSetHealth(150.0f);
		}

		/// <summary>A floor 300 m above the level, a 4 m roof block ahead and a 2 m ledge to the right, in every peer's scene.</summary>
		private static void BuildTestGeometry(Vector3 center)
		{
			DestroyTestGeometry();

			BuildBox(center + Vector3.down * 0.25f, new Vector3(90.0f, 0.5f, 90.0f), Quaternion.identity);
			BuildBox(center + new Vector3(0.0f, 2.0f, 12.0f), new Vector3(6.0f, 4.0f, 6.0f), Quaternion.identity);
			BuildBox(center + new Vector3(12.0f, 1.0f, 0.0f), new Vector3(6.0f, 2.0f, 6.0f), Quaternion.identity);
		}

		private static void BuildBox(Vector3 center, Vector3 size, Quaternion rotation)
		{
			foreach (NetworkRunner runner in NetworkRunner.Instances)
			{
				if (runner == null || runner.IsRunning == false)
					continue;

				GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
				box.name  = "EquipmentCheckBox";
				box.layer = ObjectLayer.Default;
				box.transform.SetPositionAndRotation(center, rotation);
				box.transform.localScale = size;

				SceneManager.MoveGameObjectToScene(box, runner.SimulationUnityScene);

				if (runner.Config.PeerMode != NetworkProjectConfig.PeerModes.Single)
				{
					runner.AddVisibilityNodes(box);
				}

				_geometry.Add(box);
			}

			Physics.SyncTransforms();
		}

		private static void DestroyTestGeometry()
		{
			foreach (GameObject box in _geometry)
			{
				if (box != null)
				{
					Object.Destroy(box);
				}
			}

			_geometry.Clear();
		}

		/// <summary>
		/// A picture from the other peer: the host's view (its player stands 8 m behind the check spot, looking at it), saved to
		/// Logs/EquipmentCheck/. withThrowerScreen: also a picture of the thrower's own screen.
		/// </summary>
		private static IEnumerator CaptureFromOtherPeer(PlayerRef playerRef, string label, Vector3 lookAt, float delay, bool withThrowerScreen = false)
		{
			if (delay > 0.0f)
			{
				yield return Wait(delay);
			}

			System.IO.Directory.CreateDirectory("Logs/EquipmentCheck");

			if (withThrowerScreen == true)
			{
				ScreenCapture.CaptureScreenshot($"Logs/EquipmentCheck/{label}_ThrowerScreen.png");
				yield return Wait(0.2f);
			}

			Agent observed = ViewCopy(playerRef, FindObservedAgent()?.Runner);
			Agent server   = ServerCopy(playerRef);
			Agent host     = server != null ? FindOtherPlayer(server.Runner, playerRef) : null;
			if (host == null || observed == null)
				yield break;

			NetworkRunner viewRunner = observed.Runner;
			Vector3       spot       = lookAt + (host.transform.position - lookAt).OnlyXZ().normalized * 10.0f;
			PlaceAll(host.Object.InputAuthority, new Vector3(spot.x, FLOOR_HEIGHT, spot.z));
			LookAt(host.Object.InputAuthority, lookAt);

			SetVisiblePeer(host.Runner);
			yield return Wait(0.35f);
			ScreenCapture.CaptureScreenshot($"Logs/EquipmentCheck/{label}_OtherPeer.png");
			yield return Wait(0.25f);
			SetVisiblePeer(viewRunner);

			Log($"  (picture: Logs/EquipmentCheck/{label}_OtherPeer.png)");
		}

		/// <summary>Shows this peer's camera (input stays where it is).</summary>
		private static void SetVisiblePeer(NetworkRunner runner)
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
				context.IsVisible = context.Runner == runner;
			}
		}

		private static Agent FindObservedAgent()
		{
			Agent observed = null;
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object != null && agent.Object.IsValid == true && agent.IsObserved == true && agent.Context.HasInput == true && agent.Object.InputAuthority.IsRealPlayer == true)
				{
					observed = agent;
				}
			}

			return observed;
		}

		/// <summary>The server's copy of the other real player, if there is one.</summary>
		private static Agent FindOtherPlayer(NetworkRunner serverRunner, PlayerRef playerRef)
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Runner == serverRunner && agent.Object != null && agent.Object.IsValid == true && agent.Object.InputAuthority.IsRealPlayer == true && agent.Object.InputAuthority != playerRef)
					return agent;
			}

			return null;
		}

		private static Agent ViewCopy(PlayerRef playerRef, NetworkRunner runner)
		{
			if (runner == null)
				return null;

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Runner == runner && agent.Object != null && agent.Object.IsValid == true && agent.Object.InputAuthority == playerRef)
					return agent;
			}

			return null;
		}

		/// <summary>Teleports every peer's copy of this player.</summary>
		private static void PlaceAll(PlayerRef playerRef, Vector3 position)
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object == null || agent.Object.IsValid == false || agent.Object.InputAuthority != playerRef)
					continue;

				var kcc = agent.Character.CharacterController;
				kcc.FixedData.BasePosition      = position;
				kcc.FixedData.DesiredPosition   = position;
				kcc.FixedData.TargetPosition    = position;
				kcc.FixedData.DynamicVelocity   = Vector3.zero;
				kcc.FixedData.KinematicVelocity = Vector3.zero;
				kcc.RenderData.BasePosition     = position;
				kcc.RenderData.DesiredPosition  = position;
				kcc.RenderData.TargetPosition   = position;
				kcc.SetPosition(position, true, false, false);
				agent.transform.position = position;
			}
		}

		private static void LookAt(PlayerRef playerRef, Vector3 point)
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object == null || agent.Object.IsValid == false || agent.Object.InputAuthority != playerRef)
					continue;

				var     kcc       = agent.Character.CharacterController;
				Vector3 direction = point - agent.Character.GetCameraTransform(false).Position;

				float yaw   = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
				float pitch = -Mathf.Asin(direction.normalized.y) * Mathf.Rad2Deg;

				kcc.FixedData.LookYaw    = yaw;
				kcc.RenderData.LookYaw   = yaw;
				kcc.FixedData.LookPitch  = pitch;
				kcc.RenderData.LookPitch = pitch;
			}
		}

		private static void ReleaseInputs()
		{
			AgentInput.DebugHoldEquipment = false;
			AgentInput.DebugHoldGear      = false;
			AgentInput.DebugHoldAttack    = false;
			AgentInput.DebugHoldReload    = false;
			AgentInput.DebugHoldHeal      = false;
			AgentInput.DebugHoldJump      = false;
			AgentInput.DebugHoldSprint    = false;
			AgentInput.DebugMoveDirection = Vector2.zero;
		}

		private static IEnumerator WaitFor(int start, System.Func<string, bool> match, float timeout)
		{
			double end = EditorApplication.timeSinceStartup + timeout;
			while (EditorApplication.timeSinceStartup < end && EquipmentDebug.EventLog.Skip(start).Any(match) == false)
				yield return null;
		}

		private static float ReadSeconds(int start, string prefix, string pattern)
		{
			string line = EquipmentDebug.EventLog.Skip(start).LastOrDefault(t => t.StartsWith(prefix));
			if (line == null)
				return -1.0f;

			Match match = Regex.Match(line, pattern);
			return match.Success == true ? float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : -1.0f;
		}

		private static int ReadTick(int start, string prefix)
		{
			string line = EquipmentDebug.EventLog.Skip(start).LastOrDefault(t => t.StartsWith(prefix));
			return line != null ? TickOf(line) : -1;
		}

		private static int TickOf(string line)
		{
			Match match = Regex.Match(line, @"\| tick (\d+)");
			return match.Success == true ? int.Parse(match.Groups[1].Value) : -1;
		}

		private static bool Near(float value, float expected, float tolerance)
		{
			return Mathf.Abs(value - expected) <= tolerance;
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
			System.IO.Directory.CreateDirectory("Logs/EquipmentCheck");
			System.IO.File.WriteAllText("Logs/EquipmentCheck/EquipmentCheck.txt", string.Join("\n", _log) + "\n\nEquipment events:\n" + string.Join("\n", EquipmentDebug.EventLog));
		}

		private static IEnumerator Wait(float seconds)
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
				Log("Equipment Check stopped: Play mode ended.");
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
				Log($"Equipment Check failed: {exception}");
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

			_running = false;
			EditorApplication.update -= UpdateRoutine;
			ReleaseInputs();
			SaveLog();
			Debug.Log("Equipment Check finished.");
		}
	}
}
