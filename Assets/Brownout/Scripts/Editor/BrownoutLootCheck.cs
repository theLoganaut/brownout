namespace TPSBR
{
	using System.Collections;
	using System.Collections.Generic;
	using System.Linq;
	using Fusion;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// The Loot Check (phase 5a), Brownout → Test → Loot Check. Run it in GenArea5 with 2 peers, viewed from the lagged client:
	/// real F, G and Q presses and inventory clicks (RPC) from the client, then the pickup rules checked exactly on the server
	/// (every item type with F and G, swaps keep their state, stacks, a full inventory, the ammo cap, the backpack, attachment F only
	/// on a gun that takes it, the Health slot refilling), 100 pickups and drops leaving nothing behind, a dummy's stash, and the
	/// loot table's odds over 10,000 rolls. About 2 minutes. Read it with GetLootCheckLog(); also written to Logs/LootCheck/.
	/// </summary>
	public static partial class BrownoutLootTools
	{
		// MENU

		[MenuItem(MENU + "Loot Check", priority = 90)]
		private static void LootCheckMenu() => Debug.Log(RunLootCheck());

		[MenuItem(MENU + "Loot Check", true)]
		private static bool ValidateLootCheck() => Application.isPlaying;

		// PRIVATE MEMBERS

		private static readonly List<string>       _log     = new List<string>(256);
		private static readonly Stack<IEnumerator> _routine = new Stack<IEnumerator>();
		private static bool _running;
		private static int  _passed;
		private static int  _checked;

		// PUBLIC METHODS

		public static string RunLootCheck()
		{
			if (Application.isPlaying == false)
				return "Enter Play mode first (GenArea5, 2 peers).";
			if (_running == true)
				return "The Loot Check is already running.";

			_log.Clear();
			_routine.Clear();
			_routine.Push(LootCheckRoutine());
			_running = true;

			EditorApplication.update -= UpdateRoutine;
			EditorApplication.update += UpdateRoutine;

			return "Loot Check started (about 2 minutes). Read GetLootCheckLog().";
		}

		public static string GetLootCheckLog()
		{
			return (_running == true ? "(running)\n" : "") + string.Join("\n", _log);
		}

		// THE CHECK

		private static IEnumerator LootCheckRoutine()
		{
			Agent observed = null;
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object != null && agent.Object.IsValid == true && agent.IsObserved == true && agent.Context.HasInput == true && agent.Object.InputAuthority.IsRealPlayer == true)
				{
					observed = agent;
				}
			}

			if (observed == null || ServerCopy(observed.Object.InputAuthority) == null)
			{
				Log("Need a running host with the observed player, viewed from the peer with input.");
				yield break;
			}

			PlayerRef     playerRef  = observed.Object.InputAuthority;
			NetworkRunner viewRunner = observed.Runner;
			Agent         server     = ServerCopy(playerRef);
			NetworkRunner runner     = server.Runner;
			LootSettings  settings   = LootSettings.Current;

			_passed  = 0;
			_checked = 0;

			Log($"Loot Check: {observed.name} on {(viewRunner.IsServer == true ? "the host" : "the client (simulated lag in play)")}, {settings.Weighting} loot table.");

			GameplayMode.DebugNoTeams = false;
			LootDebug.Clear();
			ReleaseLootInputs();

			try
			{
				// 0. Ground loot from the match start
				IReadOnlyList<LootSpawner.Point> points = LootSpawner.GetPoints(runner);
				var ground = new List<LootItem>();
				runner.GetAllBehaviours(ground);
				Check(points.Count > 0 && ground.Count >= points.Count, $"Ground loot: {points.Count} loot points rolled at match start, {ground.Count} items on the ground now");

				// Clear space: a flat spot with nothing on the ground around it
				Vector3 origin = server.transform.position;
				float   yaw    = server.Character.CharacterController.FixedData.LookYaw;
				ClearAround(runner, origin, 8.0f);
				EmptyInventory();
				yield return Wait(1.0f);

				// 1. Real presses from the observed peer
				yield return CheckRealPresses(playerRef, viewRunner, origin, yaw);

				// 2. The rules, exactly, on the server
				server = ServerCopy(playerRef);
				EmptyInventory();
				ClearAround(runner, origin, 8.0f);
				CheckGuns(server, origin);
				CheckArmor(server, origin);
				CheckHealing(server, origin);
				CheckAmmo(server, origin);
				CheckAttachments(server, origin);
				CheckFullAndBackpack(server, origin);

				// 3. Healing from the Health slot (real Q on the observed peer) and the next stack moving in
				yield return CheckHealthSlotRefill(playerRef, origin);

				// 4. Inventory clicks from the client (RPC to the server)
				yield return CheckClicks(playerRef, viewRunner, origin);

				// 5. 100 pickups and drops leave nothing behind
				yield return CheckNoLeftovers(playerRef, origin);

				// 6. A dummy's stash
				yield return CheckStash(playerRef, origin, yaw);

				// 7. The loot table
				CheckOdds();
			}
			finally
			{
				ReleaseLootInputs();

				// G with no room and the stash steps open the Tab screen on the observed peer; it blocks firing, so close it for the next check
				foreach (InventoryScreen screen in Object.FindObjectsByType<InventoryScreen>(FindObjectsSortMode.None))
				{
					screen.SetOpen(false);
				}
			}

			Log($"Loot Check done: {_passed} of {_checked} passed.");
			SaveLog();
		}

		private static IEnumerator CheckRealPresses(PlayerRef playerRef, NetworkRunner viewRunner, Vector3 origin, float yaw)
		{
			Agent      server  = ServerCopy(playerRef);
			Quaternion facing  = Quaternion.Euler(0.0f, yaw, 0.0f);
			Vector3    spot    = origin + facing * Vector3.forward * 1.3f;
			int        icarus  = ItemCatalog.GetGunIndex("Icarus");
			GunData    gunData = ItemCatalog.GetGunData(icarus);

			// F on a gun: it goes into the empty slot, in hand, raised for GunRaiseTime
			LootItem gun = LootSpawner.SpawnItem(server.Runner, LootStack.Gun(icarus, gunData.Magazine, 0), spot, yaw + 90.0f);
			yield return Press(playerRef, gun, true);

			server = ServerCopy(playerRef);
			Weapons weapons = server.Weapons;
			BulletWeapon inHand = weapons.CurrentWeapon as BulletWeapon;
			string pickup = LootDebug.EventLog.LastOrDefault(t => t.StartsWith($"EQUIP {server.name} F Icarus"));
			Check(inHand != null && inHand.GunData == gunData && (gun.Object == null || gun.Object.IsValid == false) && pickup != null,
				$"Real F press on a gun: {(inHand != null ? inHand.GunData.GunName : "nothing")} in hand, item gone from the ground");

			float raise = weapons.SwitchDuration;
			Check(Mathf.Abs(raise - settings().GunRaiseTime) <= 1.0f / 60.0f + 0.001f, $"Gun pickup raise: {raise:F3} s locked (data {settings().GunRaiseTime:F2} s), no firing or aiming until it's up");

			yield return Wait(1.5f);

			// The client's copy agrees
			Agent view = ViewCopy(playerRef, viewRunner);
			Check(view != null && view.Weapons.CurrentWeapon is BulletWeapon viewGun && viewGun.GunData == gunData, $"The observed peer sees {gunData.GunName} in hand");

			// F on ammo, G on healing, F on armor: no lock (Logan, 5 Oct)
			LootItem ammo = LootSpawner.SpawnItem(server.Runner, ItemCatalog.GetAmmoBox(gunData), spot, yaw);
			yield return Press(playerRef, ammo, true);
			server = ServerCopy(playerRef);
			int rounds = server.Weapons.GetAmmo(gunData.Caliber);
			Check(rounds == settings().GetAmmoBoxRounds(gunData.Caliber) && server.Weapons.IsDrawing == false, $"Real F on a {GunData.GetCaliberName(gunData.Caliber)} box: pool {rounds} (box {settings().GetAmmoBoxRounds(gunData.Caliber)}), gun not locked");

			LootItem heal = LootSpawner.SpawnItem(server.Runner, LootStack.Heal(EHealItem.MedKit, 2), spot, yaw);
			yield return Press(playerRef, heal, false);
			server = ServerCopy(playerRef);
			Check(server.HealthSlot.Kind == EItemKind.Heal && server.HealthSlot.Index == (byte)EHealItem.MedKit && server.HealthSlot.Count == 2 && server.Weapons.IsDrawing == false,
				$"Real G on 2 med kits: Health slot {server.HealthSlot} (auto-equipped), gun not locked");

			LootItem armor = LootSpawner.SpawnItem(server.Runner, LootStack.ArmorVest(2, 300.0f), spot, yaw);
			yield return Press(playerRef, armor, true);
			server = ServerCopy(playerRef);
			Check(server.Health.ArmorTier == 2 && Mathf.Approximately(server.Health.ArmorLeft, 300.0f) && server.Weapons.IsDrawing == false,
				$"Real F on armor tier 2 with 300 left: wearing tier {server.Health.ArmorTier}, {server.Health.ArmorLeft:F0} left, gun not locked");

			// F on an attachment the gun in hand takes: fitted, locked for AttachmentFitTime
			AttachmentData foregrip = GunplaySettings.Current.FindAttachment("Foregrip");
			LootItem grip = LootSpawner.SpawnItem(server.Runner, LootStack.Attachment(GunplaySettings.Current.GetAttachmentID(foregrip)), spot, yaw);
			yield return Press(playerRef, grip, true);
			server = ServerCopy(playerRef);
			inHand = server.Weapons.CurrentWeapon as BulletWeapon;
			Check(inHand != null && inHand.GetAttachment(EAttachmentSlot.Underbarrel) == foregrip && Mathf.Abs(server.Weapons.SwitchDuration - settings().AttachmentFitTime) <= 1.0f / 60.0f + 0.001f,
				$"Real F on a Foregrip: on the {inHand?.GunData.GunName} in hand, locked {server.Weapons.SwitchDuration:F3} s (data {settings().AttachmentFitTime:F2} s)");

			// G on an attachment and on the backpack
			AttachmentData reflex = GunplaySettings.Current.FindAttachment("Reflex");
			LootItem optic = LootSpawner.SpawnItem(server.Runner, LootStack.Attachment(GunplaySettings.Current.GetAttachmentID(reflex)), spot, yaw);
			yield return Press(playerRef, optic, false);
			LootItem backpack = LootSpawner.SpawnItem(server.Runner, LootStack.Backpack(), spot, yaw);
			yield return Press(playerRef, backpack, false);
			server = ServerCopy(playerRef);
			Check(server.Inventory.GetSlot(0).Kind == EItemKind.Attachment && server.HasBackpack == true && server.Inventory.SlotCount == settings().BackpackSlots,
				$"Real G on a Reflex and a backpack: slot 1 {server.Inventory.GetSlot(0)}, backpack on, {server.Inventory.SlotCount} slots");

			yield return Wait(1.0f);
			view = ViewCopy(playerRef, viewRunner);
			Check(view != null && view.HasBackpack == true && view.HealthSlot.Count == 2 && view.Inventory.GetSlot(0).Kind == EItemKind.Attachment && view.Health.ArmorTier == 2,
				"The observed peer's copy of the inventory matches the server's");
		}

		private static void CheckGuns(Agent server, Vector3 origin)
		{
			Weapons weapons = server.Weapons;
			int     a       = ItemCatalog.GetGunIndex("Icarus");
			int     b       = ItemCatalog.GetGunIndex("Alibi DMR");
			int     c       = ItemCatalog.GetGunIndex("Savant");
			long    key     = (long)GunplaySettings.Current.GetAttachmentID(GunplaySettings.Current.FindAttachment("2x")) << (8 * (int)EAttachmentSlot.Optic);

			Take(server, LootStack.Gun(a, 7, key), origin, true);
			Take(server, LootStack.Gun(b, ItemCatalog.GetGunData(b).Magazine, 0), origin, true);
			bool bothFull = weapons.GetWeapon(1) is BulletWeapon g1 && g1.GunData == ItemCatalog.GetGunData(a) && weapons.GetWeapon(2) is BulletWeapon g2 && g2.GunData == ItemCatalog.GetGunData(b);

			bool refused = Take(server, LootStack.Gun(c, 30, 0), origin, false) == false;

			// F with both full replaces the gun in hand; switch to the Icarus first so it's the one that drops
			weapons.AddGunFromLoot(weapons.TakeGun(1), 1, false);
			int before = CountGround(server.Runner);
			Take(server, LootStack.Gun(c, 30, 0), origin, true);
			LootItem dropped = FindDropped(server.Runner, origin, EItemKind.Gun, (byte)a);

			Check(bothFull == true && refused == true && weapons.GetWeapon(1) is BulletWeapon g3 && g3.GunData == ItemCatalog.GetGunData(c) && dropped != null && dropped.Stack.Mag == 7 && dropped.Stack.Attachments == key,
				$"Guns: F fills slot 1 then 2; G with both full is refused; F swaps the gun in hand and the Icarus drops with its mag {dropped?.Stack.Mag} (was 7) and 2x ({(dropped != null && dropped.Stack.Attachments == key ? "kept" : "lost")})");

			// Picking the dropped Icarus back up gives the same gun back
			Take(server, dropped.Stack, origin, true);
			BulletWeapon back = weapons.CurrentWeapon as BulletWeapon;
			Check(back != null && back.GunData == ItemCatalog.GetGunData(a) && back.MagazineAmmo == 7 && back.GetAttachment(EAttachmentSlot.Optic) != null,
				$"Guns: the Icarus picked up again has {back?.MagazineAmmo} in the mag and its {back?.GetAttachment(EAttachmentSlot.Optic)?.AttachmentName}");

			ClearAround(server.Runner, origin, 8.0f);
		}

		private static void CheckArmor(Agent server, Vector3 origin)
		{
			Health health = server.Health;
			health.SetArmor(0, 0.0f);

			Take(server, LootStack.ArmorVest(1, 300.0f), origin, false);
			bool autoEquip = health.ArmorTier == 1;

			health.SetArmor(1, 120.0f);
			Take(server, LootStack.ArmorVest(3, 600.0f), origin, true);
			LootItem dropped = FindDropped(server.Runner, origin, EItemKind.Armor, 1);

			Take(server, LootStack.ArmorVest(2, 450.0f), origin, false);
			bool stored = server.Inventory.GetSlot(0).Kind == EItemKind.Armor && server.Inventory.GetSlot(0).Index == 2;

			Check(autoEquip == true && health.ArmorTier == 3 && dropped != null && Mathf.Approximately(dropped.Stack.Armor, 120.0f) && stored == true,
				$"Armor: G with none worn equips it; F swaps and the old tier 1 drops with {dropped?.Stack.Armor:F0} left (was 120); G while wearing one stores it");

			EmptyInventory();
			ClearAround(server.Runner, origin, 8.0f);
		}

		private static void CheckHealing(Agent server, Vector3 origin)
		{
			int bandageStack = HealthSettings.Current.GetHealItem(EHealItem.Bandage).Stack;

			// G 12 bandages: 10 in the Health slot, 2 in a slot
			Take(server, LootStack.Heal(EHealItem.Bandage, bandageStack + 2), origin, false);
			bool split = server.HealthSlot.Count == bandageStack && server.Inventory.GetSlot(0).Count == 2;

			// G 2 trauma kits: stack of 1 each
			Take(server, LootStack.Heal(EHealItem.TraumaKit, 2), origin, false);
			bool trauma = server.Inventory.GetSlot(1).Count == 1 && server.Inventory.GetSlot(2).Count == 1 && server.Inventory.GetSlot(2).Index == (byte)EHealItem.TraumaKit;

			// F a med kit: it takes the Health slot, the bandages drop
			Take(server, LootStack.Heal(EHealItem.MedKit, 3), origin, true);
			LootItem dropped = FindDropped(server.Runner, origin, EItemKind.Heal, (byte)EHealItem.Bandage);

			Check(split == true && trauma == true && server.HealthSlot.Index == (byte)EHealItem.MedKit && server.HealthSlot.Count == 3 && dropped != null && dropped.Stack.Count == bandageStack,
				$"Healing: 12 bandages stack as {bandageStack} + 2, trauma kits one per slot, F on med kits takes the Health slot and the {dropped?.Stack.Count} bandages drop");

			EmptyInventory();
			ClearAround(server.Runner, origin, 8.0f);
		}

		private static void CheckAmmo(Agent server, Vector3 origin)
		{
			ECaliber caliber = ECaliber.Cal556;
			int      cap     = GunplaySettings.Current.GetCarryCap(caliber);

			server.Weapons.TakeAmmo(caliber, 10000);
			server.Weapons.AddAmmo(caliber, cap - 10);

			LootItem box = LootSpawner.SpawnItem(server.Runner, LootStack.Ammo(caliber, 30), origin, 0.0f);
			server.Inventory.Store(box);
			int  left = box != null && box.Object != null && box.Object.IsValid == true ? box.Stack.Count : 0;
			bool full = server.Inventory.Store(box) == false;

			Check(server.Weapons.GetAmmo(caliber) == cap && left == 20 && full == true,
				$"Ammo: a 30-round box with 10 of room fills the pool to {server.Weapons.GetAmmo(caliber)} (cap {cap}) and leaves {left} in the box; at the cap it's refused");

			ClearAround(server.Runner, origin, 8.0f);
		}

		private static void CheckAttachments(Agent server, Vector3 origin)
		{
			GunplaySettings gunplay    = GunplaySettings.Current;
			Weapons         weapons    = server.Weapons;
			AttachmentData  suppressor = gunplay.FindAttachment("Suppressor");
			AttachmentData  x2         = gunplay.FindAttachment("2x");
			AttachmentData  x4         = gunplay.FindAttachment("4x");

			// Omicron doesn't take a suppressor: F is refused (and its prompt only offers G), G stores it
			weapons.AddGunFromLoot(LootStack.Gun(ItemCatalog.GetGunIndex("Omicron"), 5, 0), 1, false);
			bool refused = Take(server, LootStack.Attachment(gunplay.GetAttachmentID(suppressor)), origin, true) == false;
			bool stored  = Take(server, LootStack.Attachment(gunplay.GetAttachmentID(suppressor)), origin, false) == true && server.Inventory.GetSlot(0).Kind == EItemKind.Attachment;

			// F a 2x then a 4x: the 2x drops
			Take(server, LootStack.Attachment(gunplay.GetAttachmentID(x2)), origin, true);
			Take(server, LootStack.Attachment(gunplay.GetAttachmentID(x4)), origin, true);
			LootItem dropped = FindDropped(server.Runner, origin, EItemKind.Attachment, gunplay.GetAttachmentID(x2));
			BulletWeapon gun = weapons.CurrentWeapon as BulletWeapon;

			Check(refused == true && stored == true && gun != null && gun.GetAttachment(EAttachmentSlot.Optic) == x4 && dropped != null,
				"Attachments: F on a Suppressor with the Omicron in hand is refused, G stores it; F a 2x then a 4x puts the 4x on and drops the 2x");

			EmptyInventory();
			ClearAround(server.Runner, origin, 8.0f);
		}

		private static void CheckFullAndBackpack(Agent server, Vector3 origin)
		{
			Inventory inventory = server.Inventory;
			int       slots     = settings().InventorySlots;
			AttachmentData stock = GunplaySettings.Current.FindAttachment("Stock");
			byte      stockID   = GunplaySettings.Current.GetAttachmentID(stock);

			for (int i = 0; i < slots; ++i)
			{
				Take(server, LootStack.Attachment(stockID), origin, false);
			}

			int  openBefore = inventory.OpenInventoryCount;
			bool refused    = Take(server, LootStack.Attachment(stockID), origin, false) == false;
			bool opened     = inventory.OpenInventoryCount == openBefore + 1;

			Take(server, LootStack.Backpack(), origin, false);
			for (int i = slots; i < settings().BackpackSlots; ++i)
			{
				Take(server, LootStack.Attachment(stockID), origin, false);
			}

			bool tenFull = Enumerable.Range(0, settings().BackpackSlots).All(i => inventory.GetSlot(i).IsEmpty == false);

			int before = CountGround(server.Runner);
			server.RequestInventoryAction(EInventoryAction.Drop, InventorySlot.Backpack, 0);
			int dropped = CountGround(server.Runner) - before;

			Check(refused == true && opened == true && tenFull == true && inventory.HasBackpack == false && dropped == 1 + settings().BackpackSlots - slots && inventory.SlotCount == slots,
				$"Full inventory: G refused and the inventory asked to open ({opened}); a backpack gives {settings().BackpackSlots} slots; dropping it with all full drops it and the {dropped - 1} items past slot {slots}");

			EmptyInventory();
			ClearAround(server.Runner, origin, 8.0f);
		}

		private static IEnumerator CheckHealthSlotRefill(PlayerRef playerRef, Vector3 origin)
		{
			Agent server = ServerCopy(playerRef);
			Take(server, LootStack.Heal(EHealItem.Bandage, 1), origin, false);
			Take(server, LootStack.Heal(EHealItem.MedKit, 2), origin, false);
			server.Health.DebugSetHealth(100.0f);

			yield return Wait(0.8f);

			int logStart = HealthDebug.EventLog.Count;
			AgentInput.DebugHoldHeal = true;
			double end = EditorApplication.timeSinceStartup + 5.0;
			while (HealthDebug.EventLog.Skip(logStart).Any(t => t.StartsWith($"HEAL {ServerCopy(playerRef).name} Bandage done")) == false && EditorApplication.timeSinceStartup < end)
				yield return null;
			AgentInput.DebugHoldHeal = false;

			server = ServerCopy(playerRef);
			Check(server.Health.CurrentHealth >= 124.9f && server.HealthSlot.Index == (byte)EHealItem.MedKit && server.HealthSlot.Count == 2 && server.Inventory.GetSlot(0).IsEmpty == true,
				$"Q heals from the Health slot: health {server.Health.CurrentHealth:F0}, the last bandage used and the med kits moved in ({server.HealthSlot})");

			EmptyInventory();
			server.Health.DebugSetHealth(150.0f);
			yield return Wait(0.5f);
		}

		private static IEnumerator CheckClicks(PlayerRef playerRef, NetworkRunner viewRunner, Vector3 origin)
		{
			Agent          server  = ServerCopy(playerRef);
			GunplaySettings gunplay = GunplaySettings.Current;
			AttachmentData reflex  = gunplay.FindAttachment("Reflex");
			AttachmentData holo    = gunplay.FindAttachment("Holo");

			server.Weapons.AddGunFromLoot(LootStack.Gun(ItemCatalog.GetGunIndex("Icarus"), 30, 0), 1, false);
			server.Weapons.AddGunFromLoot(LootStack.Gun(ItemCatalog.GetGunIndex("Knocker"), 30, 0), 2, false);
			Take(server, LootStack.Attachment(gunplay.GetAttachmentID(reflex)), origin, false);
			Take(server, LootStack.Attachment(gunplay.GetAttachmentID(holo)), origin, false);
			Take(server, LootStack.ArmorVest(1, 300.0f), origin, false);
			Take(server, LootStack.ArmorVest(3, 600.0f), origin, false);
			yield return Wait(1.0f);

			Agent view = ViewCopy(playerRef, viewRunner);

			// Left click the Reflex with weapon tab 2: onto the Knocker (not in hand)
			view.RequestInventoryAction(EInventoryAction.Use, 0, 2);
			// Left click the Holo with tab 2: replaces the Reflex, which takes its slot
			view.RequestInventoryAction(EInventoryAction.Use, 1, 2);
			// Left click the tier 3 vest: worn, the tier 1 goes into its slot
			view.RequestInventoryAction(EInventoryAction.Use, 2, 0);
			yield return Wait(1.0f);

			server = ServerCopy(playerRef);
			BulletWeapon knocker = server.Weapons.GetWeapon(2) as BulletWeapon;
			bool attached = knocker != null && knocker.GetAttachment(EAttachmentSlot.Optic) == holo && server.Inventory.GetSlot(1).Index == gunplay.GetAttachmentID(reflex);
			bool armor    = server.Health.ArmorTier == 3 && server.Inventory.GetSlot(2).Kind == EItemKind.Armor && server.Inventory.GetSlot(2).Index == 1;

			// Right clicks: drop the Reflex, drop gun 1, drop a 5.56 box
			int before = CountGround(server.Runner);
			server.Weapons.AddAmmo(ECaliber.Cal556, 100);
			yield return Wait(0.5f);
			view = ViewCopy(playerRef, viewRunner);
			view.RequestInventoryAction(EInventoryAction.Drop, 1, 0);
			view.RequestInventoryAction(EInventoryAction.Drop, InventorySlot.Gun(1), 0);
			view.RequestInventoryAction(EInventoryAction.Drop, InventorySlot.Ammo(ECaliber.Cal556), 0);
			// Left click the Holo on gun 2: back into the inventory
			view.RequestInventoryAction(EInventoryAction.Use, InventorySlot.Attachment(2, EAttachmentSlot.Optic), 0);
			yield return Wait(1.0f);

			server = ServerCopy(playerRef);
			knocker = server.Weapons.GetWeapon(2) as BulletWeapon;
			int  dropped  = CountGround(server.Runner) - before;
			bool detached = knocker != null && knocker.GetAttachment(EAttachmentSlot.Optic) == null && Enumerable.Range(0, 5).Any(i => server.Inventory.GetSlot(i).Index == gunplay.GetAttachmentID(holo) && server.Inventory.GetSlot(i).Kind == EItemKind.Attachment);

			Check(attached == true && armor == true && dropped == 3 && server.Weapons.GetWeapon(1) == null && server.Weapons.GetAmmo(ECaliber.Cal556) == 100 - settings().GetAmmoBoxRounds(ECaliber.Cal556) && detached == true,
				$"Inventory clicks from the observed peer: attach to weapon 2 and swap optics ({attached}), wear the other vest ({armor}), drop an item, gun 1 and a box ({dropped} on the ground), detach into the inventory ({detached})");

			EmptyInventory();
			ClearAround(server.Runner, origin, 8.0f);
		}

		private static IEnumerator CheckNoLeftovers(PlayerRef playerRef, Vector3 origin)
		{
			Agent server = ServerCopy(playerRef);
			NetworkRunner runner = server.Runner;

			var guns = new List<BulletWeapon>();
			runner.GetAllBehaviours(guns);
			int gunsBefore   = guns.Count;
			int groundBefore = CountGround(runner);
			int objects      = CountObjects(runner);

			// 25 rounds of: a gun picked up, dropped, picked up, dropped, picked up; a healing stack the same; then everything emptied.
			// 150 pickups and 100 drops in all
			int icarus = ItemCatalog.GetGunIndex("Icarus");
			for (int i = 0; i < 25; ++i)
			{
				Take(server, LootStack.Gun(icarus, 30, 0), origin, true);
				server.RequestInventoryAction(EInventoryAction.Drop, InventorySlot.Gun(1), 0);
				PickUpAll(server, origin);
				server.RequestInventoryAction(EInventoryAction.Drop, InventorySlot.Gun(1), 0);
				PickUpAll(server, origin);

				Take(server, LootStack.Heal(EHealItem.Bandage, 3), origin, false);
				server.RequestInventoryAction(EInventoryAction.Drop, InventorySlot.Health, 0);
				PickUpAll(server, origin);
				server.RequestInventoryAction(EInventoryAction.Drop, InventorySlot.Health, 0);
				PickUpAll(server, origin);

				EmptyInventory();
			}

			yield return Wait(1.0f);

			guns.Clear();
			runner.GetAllBehaviours(guns);
			Check(guns.Count == gunsBefore && CountGround(runner) == groundBefore && CountObjects(runner) == objects,
				$"150 pickups and 100 drops: gun objects {gunsBefore} -> {guns.Count}, ground items {groundBefore} -> {CountGround(runner)}, network objects {objects} -> {CountObjects(runner)}");
		}

		private static IEnumerator CheckStash(PlayerRef playerRef, Vector3 origin, float yaw)
		{
			Agent        server = ServerCopy(playerRef);
			GameplayMode mode   = server.Context.GameplayMode;
			Quaternion   facing = Quaternion.Euler(0.0f, yaw, 0.0f);
			byte         team   = mode.NewDummyTeam();

			Agent a = mode.SpawnDummy(origin + facing * new Vector3(-1.0f, 0.0f, 2.5f), Quaternion.identity, team);
			Agent b = mode.SpawnDummy(origin + facing * new Vector3(1.0f, 0.0f, 2.5f), Quaternion.identity, team);
			yield return Wait(1.5f);

			string carried = DescribeInventory(a);
			var    bags    = new List<LootBag>();
			server.Runner.GetAllBehaviours(bags);
			int bagsBefore = bags.Count;

			foreach (Agent dummy in new[] { a, b })
			{
				// Not a bullet: a 500 bullet would wear out (and break) the armor before the dummy dies, and the armor should reach the bag
				var hit = new HitData { Action = EHitAction.Damage, Amount = 500.0f, Position = dummy.transform.position, Normal = Vector3.up, Direction = Vector3.forward, Target = dummy.Health, InstigatorRef = playerRef, Instigator = server.Health, HitType = EHitType.None };
				HitUtility.ProcessHit(ref hit);
			}

			yield return Wait(0.5f);

			bags.Clear();
			server.Runner.GetAllBehaviours(bags);
			LootBag bag = bags.Where(t => t.OwnerName == a.name).FirstOrDefault();

			var items = new List<LootStack>();
			for (int i = 0; bag != null && i < LootBag.Capacity; ++i)
			{
				if (bag.GetItem(i).IsEmpty == false)
				{
					items.Add(bag.GetItem(i));
				}
			}

			bool everything = items.Count(t => t.Kind == EItemKind.Gun) == 2 && items.Count(t => t.Kind == EItemKind.Ammo) == GunData.CaliberCount &&
			                  items.Any(t => t.Kind == EItemKind.Armor && t.Index == settings().DummyArmorTier) && items.Any(t => t.Kind == EItemKind.Heal);

			Check(bags.Count == bagsBefore + 2 && everything == true, $"Stash: each dummy leaves a bag; {a.name} carried {carried}; its bag holds {string.Join(", ", items)}");

			// Open it (F) and take its gun, armor and ammo
			server = ServerCopy(playerRef);
			EmptyInventory();
			server.Inventory.OpenStashBag(bag);
			int gunIndex   = Enumerable.Range(0, LootBag.Capacity).First(i => bag.GetItem(i).Kind == EItemKind.Gun);
			int armorIndex = Enumerable.Range(0, LootBag.Capacity).First(i => bag.GetItem(i).Kind == EItemKind.Armor);
			int ammoIndex  = Enumerable.Range(0, LootBag.Capacity).First(i => bag.GetItem(i).Kind == EItemKind.Ammo);
			LootStack ammoStack = bag.GetItem(ammoIndex);

			server.RequestInventoryAction(EInventoryAction.TakeFromStash, gunIndex, 0);
			server.RequestInventoryAction(EInventoryAction.TakeFromStash, armorIndex, 0);
			server.RequestInventoryAction(EInventoryAction.TakeFromStash, ammoIndex, 0);

			Check(server.Weapons.CurrentWeapon is BulletWeapon && server.Health.ArmorTier == settings().DummyArmorTier && server.Weapons.GetAmmo((ECaliber)ammoStack.Index) == Mathf.Min(ammoStack.Count, GunplaySettings.Current.GetCarryCap((ECaliber)ammoStack.Index)) &&
			      bag.GetItem(gunIndex).IsEmpty == true && bag.GetItem(armorIndex).IsEmpty == true,
				$"Stash: opened with F, took its gun ({(server.Weapons.CurrentWeapon as BulletWeapon)?.GunData.GunName}), armor (tier {server.Health.ArmorTier}) and {ItemCatalog.GetName(ammoStack)}");

			// Walking away closes it
			server.Inventory.OnFixedUpdate();
			bool stillOpen = server.Inventory.OpenStash != null;
			PlaceAll(playerRef, origin + facing * Vector3.back * 10.0f);
			yield return Wait(0.5f);
			ServerCopy(playerRef).Inventory.OnFixedUpdate();
			yield return Wait(0.5f);
			server = ServerCopy(playerRef);
			Check(stillOpen == true && server.Inventory.OpenStash == null, "Stash: stays open in reach, closes when the player walks away");

			mode.RemoveDummies();
			EmptyInventory();
			foreach (LootBag left in bags)
			{
				if (left != null && left.Object != null && left.Object.IsValid == true)
				{
					server.Runner.Despawn(left.Object);
				}
			}

			yield return Wait(0.5f);
		}

		private static void CheckOdds()
		{
			const int rolls = 10000;

			var all = new List<LootStack>();
			ItemCatalog.GetAllItems(all);

			int guns = 0, heals = 0, trauma = 0, traumaTop = 0;
			for (int i = 0; i < rolls; ++i)
			{
				LootStack item = LootSpawner.Roll(false);
				if (item.Kind == EItemKind.Gun)  { ++guns; }
				if (item.Kind == EItemKind.Heal) { ++heals; }
				if (item.Kind == EItemKind.Heal && item.Index == (byte)EHealItem.TraumaKit) { ++trauma; }

				LootStack top = LootSpawner.Roll(true);
				if (top.Kind == EItemKind.Heal && top.Index == (byte)EHealItem.TraumaKit) { ++traumaTop; }
			}

			int   gunCount  = all.Count(t => t.Kind == EItemKind.Gun);
			int   healCount = all.Count(t => t.Kind == EItemKind.Heal);
			float expectGun, expectHeal;

			if (settings().Weighting == ELootWeighting.EveryItem)
			{
				expectGun  = (float)gunCount / all.Count;
				expectHeal = (float)healCount / all.Count;
			}
			else
			{
				int kinds = all.Select(t => t.Kind).Distinct().Count();
				expectGun  = 1.0f / kinds;
				expectHeal = 1.0f / kinds;
			}

			float bonus = settings().TopTierBonus;
			float ratio = trauma > 0 ? (float)traumaTop / trauma : 0.0f;

			Check(Mathf.Abs(guns / (float)rolls - expectGun) < 0.02f && Mathf.Abs(heals / (float)rolls - expectHeal) < 0.015f && Mathf.Abs(ratio - bonus) < bonus * 0.35f,
				$"Loot table ({settings().Weighting}, {all.Count} items), {rolls} rolls: guns {guns / (float)rolls:P1} (expected {expectGun:P1}), healing {heals / (float)rolls:P1} (expected {expectHeal:P1}); trauma kits at top-tier points {ratio:F2}x as likely (bonus {bonus:F1}x)");
		}

		// HELPERS

		private static LootSettings settings() => LootSettings.Current;

		/// <summary>Spawns the stack at the player's feet and takes it with F (swap) or G. True when anything was taken.</summary>
		private static bool Take(Agent server, LootStack stack, Vector3 origin, bool swap)
		{
			LootItem item = LootSpawner.SpawnItem(server.Runner, stack, origin + Vector3.forward * 0.5f, 0.0f);
			if (item == null)
				return false;

			NetworkId id    = item.Object.Id;
			bool      taken = swap == true ? server.Inventory.Equip(item) : server.Inventory.Store(item);

			// Whatever wasn't taken goes, so it can't be mistaken for a drop. Despawned objects are pooled and reused by the next spawn
			// (a swapped-out item can land in this very instance), so it's matched by its network ID
			if (taken == false && item.Object != null && item.Object.IsValid == true && item.Object.Id == id)
			{
				server.Runner.Despawn(item.Object);
			}

			return taken;
		}

		/// <summary>An item of this kind lying within 3 m of the origin (the newest).</summary>
		private static LootItem FindDropped(NetworkRunner runner, Vector3 origin, EItemKind kind, byte index)
		{
			var items = new List<LootItem>();
			runner.GetAllBehaviours(items);
			return items.LastOrDefault(t => t.Stack.Kind == kind && t.Stack.Index == index && (t.transform.position - origin).sqrMagnitude < 9.0f);
		}

		private static void PickUpAll(Agent server, Vector3 origin)
		{
			var items = new List<LootItem>();
			server.Runner.GetAllBehaviours(items);
			foreach (LootItem item in items)
			{
				if ((item.transform.position - origin).sqrMagnitude < 9.0f && item.Object != null && item.Object.IsValid == true)
				{
					server.Inventory.Equip(item);
				}
			}
		}

		private static void ClearAround(NetworkRunner runner, Vector3 origin, float radius)
		{
			var items = new List<LootItem>();
			runner.GetAllBehaviours(items);
			foreach (LootItem item in items)
			{
				if ((item.transform.position - origin).sqrMagnitude < radius * radius)
				{
					runner.Despawn(item.Object);
				}
			}
		}

		private static int CountGround(NetworkRunner runner)
		{
			var items = new List<LootItem>();
			runner.GetAllBehaviours(items);
			return items.Count(t => t.Object != null && t.Object.IsValid == true);
		}

		private static int CountObjects(NetworkRunner runner)
		{
			int count = 0;
			foreach (NetworkObject unused in runner.GetAllNetworkObjects())
			{
				++count;
			}

			return count;
		}

		/// <summary>
		/// Aims the observed player at an item and holds F or G until the server logs what it did with it (or 4 s pass), then lets go.
		/// Simulated lag and editor stalls make a fixed wait unreliable. A leftover item is removed so it can't catch the next press.
		/// </summary>
		private static IEnumerator Press(PlayerRef playerRef, LootItem item, bool f)
		{
			NetworkId id   = item.Object.Id;
			string    name = ServerCopy(playerRef).name;

			yield return Wait(0.4f);
			LookAt(playerRef, item.transform.position + Vector3.up * 0.1f);
			yield return Wait(0.4f);

			int start = LootDebug.EventLog.Count;
			if (f == true) { AgentInput.DebugHoldInteract = true; } else { AgentInput.DebugHoldPickUp = true; }

			double end = EditorApplication.timeSinceStartup + 4.0;
			while (EditorApplication.timeSinceStartup < end && LootDebug.EventLog.Skip(start).Any(t => t.Contains(name) && (t.StartsWith("EQUIP") || t.StartsWith("PICKUP") || t.StartsWith("REFUSED"))) == false)
				yield return null;

			AgentInput.DebugHoldInteract = false;
			AgentInput.DebugHoldPickUp   = false;

			if (LootDebug.EventLog.Skip(start).Any(t => t.Contains(name)) == false)
			{
				Log($"  (no server event for the {(f ? "F" : "G")} press on {item.Stack} within 4 s)");
			}

			yield return Wait(0.3f);

			if (item.Object != null && item.Object.IsValid == true && item.Object.Id == id)
			{
				item.Runner.Despawn(item.Object);
			}
		}

		/// <summary>Teleports every peer's copy of this player.</summary>
		private static void PlaceAll(PlayerRef playerRef, Vector3 position)
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object == null || agent.Object.IsValid == false || agent.Object.InputAuthority != playerRef)
					continue;

				var kcc = agent.Character.CharacterController;
				kcc.FixedData.BasePosition     = position;
				kcc.FixedData.DesiredPosition  = position;
				kcc.FixedData.TargetPosition   = position;
				kcc.RenderData.BasePosition    = position;
				kcc.RenderData.DesiredPosition = position;
				kcc.RenderData.TargetPosition  = position;
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

		private static void ReleaseLootInputs()
		{
			AgentInput.DebugHoldInteract = false;
			AgentInput.DebugHoldPickUp   = false;
			AgentInput.DebugHoldHeal     = false;
			AgentInput.DebugHoldAttack   = false;
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
			System.IO.Directory.CreateDirectory("Logs/LootCheck");
			System.IO.File.WriteAllText("Logs/LootCheck/LootCheck.txt", string.Join("\n", _log) + "\n\nLoot events:\n" + string.Join("\n", LootDebug.EventLog));
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
				Log("Loot Check stopped: Play mode ended.");
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
				Log($"Loot Check failed: {exception}");
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
			SaveLog();
			Debug.Log("Loot Check finished.");
		}
	}
}
