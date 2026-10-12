namespace TPSBR
{
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using Fusion;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 5a tools: Brownout → Setup → Build Loot Prefabs (the LootSettings asset and the LootItem / LootBag prefabs), and
	/// Brownout → Test → Loot: lay out every gun, every attachment or every other item in a row in front of the player (2 m apart,
	/// guns with their ammo boxes), reroll the loot points, give a full kit, log the inventory and the loot events.
	/// The Loot Check is in BrownoutLootCheck.cs.
	/// </summary>
	public static partial class BrownoutLootTools
	{
		// CONSTANTS

		public const string SettingsPath = "Assets/TPSBR/Resources/Settings/LootSettings.asset";
		public const string PrefabFolder = "Assets/TPSBR/Prefabs/Loot";

		private const string MENU        = "Brownout/Test/Loot/";
		private const string ITEMBOX     = "Assets/TPSBR/Prefabs/Gameplay/ItemBox.prefab";
		private const float  SPACING     = 2.0f;
		// Each layout has its own row, so guns, attachments and the rest can all be out at once
		private const float  GUN_ROW        = 4.0f;
		private const float  ATTACHMENT_ROW = 7.0f;
		private const float  OTHER_ROW      = 10.0f;

		// MENU

		[MenuItem("Brownout/Setup/Build Loot Prefabs", priority = 4)]
		private static void BuildMenu() => Debug.Log(BuildLootPrefabs());

		[MenuItem(MENU + "Lay Out All Guns", priority = 100)]        private static void LayOutGunsMenu()        => Debug.Log(LayOutGuns());
		[MenuItem(MENU + "Lay Out All Attachments", priority = 101)] private static void LayOutAttachmentsMenu() => Debug.Log(LayOutAttachments());
		[MenuItem(MENU + "Lay Out Other Items", priority = 102)]     private static void LayOutOtherMenu()       => Debug.Log(LayOutOtherItems());
		[MenuItem(MENU + "Reroll Loot Points", priority = 120)]      private static void RerollMenu()            => Debug.Log(Reroll());
		[MenuItem(MENU + "Clear Ground Loot", priority = 121)]       private static void ClearMenu()             => Debug.Log(ClearGround());
		[MenuItem(MENU + "Give Full Kit", priority = 140)]           private static void GiveKitMenu()           => Debug.Log(GiveFullKit());
		[MenuItem(MENU + "Empty Inventory", priority = 141)]         private static void EmptyMenu()             => Debug.Log(EmptyInventory());
		[MenuItem(MENU + "Log Inventory", priority = 160)]           private static void LogInventoryMenu()      => Debug.Log(GetInventoryStatus());
		[MenuItem(MENU + "Log Loot Events", priority = 161)]         private static void LogEventsMenu()         => Debug.Log(GetLootEvents(60));
		[MenuItem(MENU + "Clear Loot Events", priority = 162)]       private static void ClearEventsMenu()       { LootDebug.Clear(); Debug.Log("Loot events cleared."); }
		[MenuItem(MENU + "Print Loot Events To Console", priority = 163)]
		private static void ToggleConsoleMenu() => LootDebug.LogToConsole = !LootDebug.LogToConsole;
		[MenuItem(MENU + "Print Loot Events To Console", true)]
		private static bool ValidateConsoleMenu()
		{
			UnityEditor.Menu.SetChecked(MENU + "Print Loot Events To Console", LootDebug.LogToConsole);
			return true;
		}

		[MenuItem(MENU + "Lay Out All Guns", true)]
		[MenuItem(MENU + "Lay Out All Attachments", true)]
		[MenuItem(MENU + "Lay Out Other Items", true)]
		[MenuItem(MENU + "Reroll Loot Points", true)]
		[MenuItem(MENU + "Clear Ground Loot", true)]
		[MenuItem(MENU + "Give Full Kit", true)]
		[MenuItem(MENU + "Empty Inventory", true)]
		[MenuItem(MENU + "Log Inventory", true)]
		private static bool ValidatePlaying() => Application.isPlaying;

		// SETUP

		/// <summary>
		/// Creates the LootSettings asset if missing, links it from GlobalSettings, and (re)builds the LootItem and LootBag prefabs: a
		/// NetworkObject (interest settings copied from the sample's item box) plus the script; everything else is made at runtime.
		/// The prefabs are labeled for Fusion and the prefab table is rebuilt so they can spawn.
		/// </summary>
		public static string BuildLootPrefabs()
		{
			var log = new StringBuilder();

			var  settings = AssetDatabase.LoadAssetAtPath<LootSettings>(SettingsPath);
			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<LootSettings>();
				AssetDatabase.CreateAsset(settings, SettingsPath);
				log.AppendLine($"Created {SettingsPath}.");
			}

			string globalPath = AssetDatabase.FindAssets("t:GlobalSettings").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
			var    global     = globalPath != null ? AssetDatabase.LoadAssetAtPath<GlobalSettings>(globalPath) : null;
			if (global == null)
				return "GlobalSettings asset not found.";

			if (global.Loot != settings)
			{
				global.Loot = settings;
				EditorUtility.SetDirty(global);
				log.AppendLine($"Linked LootSettings from {globalPath}.");
			}

			if (AssetDatabase.IsValidFolder(PrefabFolder) == false)
			{
				AssetDatabase.CreateFolder("Assets/TPSBR/Prefabs", "Loot");
			}

			int flags = GetItemBoxFlags();

			settings.LootItemPrefab = BuildPrefab<LootItem>("LootItem", flags, log);
			settings.LootBagPrefab  = BuildPrefab<LootBag>("LootBag", flags, log);
			EditorUtility.SetDirty(settings);

			AssetDatabase.SaveAssets();

			foreach (string path in new[] { $"{PrefabFolder}/LootItem.prefab", $"{PrefabFolder}/LootBag.prefab" })
			{
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				string[]   labels = AssetDatabase.GetLabels(prefab);
				if (labels.Contains(Fusion.Editor.NetworkProjectConfigImporter.FusionPrefabTag) == false)
				{
					AssetDatabase.SetLabels(prefab, labels.Append(Fusion.Editor.NetworkProjectConfigImporter.FusionPrefabTag).ToArray());
				}
			}

			Fusion.Editor.NetworkProjectConfigUtilities.ImportGlobalConfig();

			log.Insert(0, $"Loot prefabs built in {PrefabFolder} (NetworkObject flags {flags} from the item box), linked from {SettingsPath}.\n");
			return log.ToString();
		}

		// TOOLS

		/// <summary>All 17 guns in a row in front of the observed player, 2 m apart, each with its ammo boxes.</summary>
		public static string LayOutGuns()
		{
			var items = new List<LootStack>();
			ItemCatalog.GetGuns(items);
			return LayOut(items, true, "guns", GUN_ROW);
		}

		/// <summary>All 11 attachments in a row in front of the observed player, 2 m apart.</summary>
		public static string LayOutAttachments()
		{
			var items = new List<LootStack>();
			ItemCatalog.GetAttachments(items);
			return LayOut(items, false, "attachments", ATTACHMENT_ROW);
		}

		/// <summary>Healing, armor, ammo boxes and the backpack in a row in front of the observed player, 2 m apart.</summary>
		public static string LayOutOtherItems()
		{
			var items = new List<LootStack>();
			ItemCatalog.GetOtherItems(items);
			return LayOut(items, false, "other items", OTHER_ROW);
		}

		public static string Reroll()
		{
			Agent server = FindObservedServerAgent();
			if (server == null)
				return "No observed player on a running host.";

			int points  = LootSpawner.GetPoints(server.Runner).Count;
			int spawned = LootSpawner.Reroll(server.Runner);
			return $"Rerolled {points} loot points: {spawned} items on the ground.";
		}

		public static string ClearGround()
		{
			Agent server = FindObservedServerAgent();
			if (server == null)
				return "No observed player on a running host.";

			return $"Removed {LootSpawner.ClearGround(server.Runner)} items from the ground.";
		}

		/// <summary>Two guns (Icarus with a 2x and Foregrip, Alibi DMR), full ammo for both, tier 3 armor, the healing kit, a backpack.</summary>
		public static string GiveFullKit()
		{
			Agent server = FindObservedServerAgent();
			if (server == null)
				return "No observed player on a running host.";

			Inventory inventory = server.Inventory;
			var       weapons   = server.Weapons;

			for (int slot = Weapons.FIRST_PRIMARY_SLOT; slot <= Weapons.LAST_PRIMARY_SLOT; ++slot)
			{
				weapons.TakeGun(slot);
			}

			GunplaySettings gunplay = GunplaySettings.Current;
			int  icarus = ItemCatalog.GetGunIndex("Icarus");
			int  alibi  = ItemCatalog.GetGunIndex("Alibi DMR");
			long key    = (long)gunplay.GetAttachmentID(gunplay.FindAttachment("2x")) << (8 * (int)EAttachmentSlot.Optic) |
			              (long)gunplay.GetAttachmentID(gunplay.FindAttachment("Foregrip")) << (8 * (int)EAttachmentSlot.Underbarrel);

			if (alibi >= 0)  { weapons.AddGunFromLoot(LootStack.Gun(alibi, ItemCatalog.GetGunData(alibi).Magazine, 0), Weapons.LAST_PRIMARY_SLOT, false); }
			if (icarus >= 0) { weapons.AddGunFromLoot(LootStack.Gun(icarus, ItemCatalog.GetGunData(icarus).Magazine, key), Weapons.FIRST_PRIMARY_SLOT, false); }

			for (int i = 0; i < GunData.CaliberCount; ++i)
			{
				weapons.AddAmmo((ECaliber)i, gunplay.GetCarryCap((ECaliber)i));
			}

			server.Health.SetArmor(3, HealthSettings.Current.GetArmorTier(3).Durability);
			server.HasBackpack = true;
			inventory.GiveHealKit();

			return $"{server.name}: full kit (Icarus with 2x and Foregrip, Alibi DMR, full ammo, tier 3 armor, healing kit, backpack).";
		}

		public static string EmptyInventory()
		{
			Agent server = FindObservedServerAgent();
			if (server == null)
				return "No observed player on a running host.";

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
			return $"{server.name}: inventory, guns, ammo and armor emptied.";
		}

		/// <summary>Every player's inventory as each peer sees it.</summary>
		public static string GetInventoryStatus()
		{
			var builder = new StringBuilder();

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object == null || agent.Object.IsValid == false || agent.Object.InputAuthority.IsRealPlayer == false)
					continue;

				string role = agent.HasStateAuthority == true ? "server" : (agent.HasInputAuthority == true ? "local" : "proxy");
				builder.AppendLine($"{agent.Runner.name} {agent.name} ({role}): {DescribeInventory(agent)}");
			}

			return builder.Length > 0 ? builder.ToString() : "No players.";
		}

		public static string DescribeInventory(Agent agent)
		{
			Inventory inventory = agent.Inventory;
			Weapons   weapons   = agent.Weapons;
			var       parts     = new List<string>();

			for (int slot = Weapons.FIRST_PRIMARY_SLOT; slot <= Weapons.LAST_PRIMARY_SLOT; ++slot)
			{
				BulletWeapon gun = weapons.GetWeapon(slot) as BulletWeapon;
				string attachments = gun != null ? string.Join("+", Enumerable.Range(0, AttachmentData.SlotCount).Select(i => gun.GetAttachment((EAttachmentSlot)i)).Where(a => a != null).Select(a => a.AttachmentName)) : "";
				parts.Add(gun != null ? $"gun {slot} {gun.GunData.GunName} {gun.MagazineAmmo}/{gun.MaxMagazineAmmo}{(attachments.Length > 0 ? $" [{attachments}]" : "")}{(weapons.CurrentWeaponSlot == slot ? " (in hand)" : "")}" : $"gun {slot} -");
			}

			parts.Add("ammo " + string.Join("/", Enumerable.Range(0, GunData.CaliberCount).Select(i => weapons.GetAmmo((ECaliber)i))));
			parts.Add(agent.Health.ArmorTier > 0 ? $"armor T{agent.Health.ArmorTier} {agent.Health.ArmorLeft:F0}" : "no armor");
			parts.Add($"health slot {inventory.HealthSlot}");
			parts.Add(inventory.HasBackpack == true ? "backpack" : "no backpack");

			var slots = new List<string>();
			for (int i = 0; i < inventory.SlotCount; ++i)
			{
				slots.Add(inventory.GetSlot(i).IsEmpty == true ? "-" : inventory.GetSlot(i).ToString());
			}

			parts.Add($"slots [{string.Join(", ", slots)}]");

			LootBag stash = inventory.OpenStash;
			if (stash != null)
			{
				parts.Add($"stash open ({stash.OwnerName}, {stash.ItemCount} items)");
			}

			return string.Join(", ", parts);
		}

		public static string GetLootEvents(int last)
		{
			int start = Mathf.Max(0, LootDebug.EventLog.Count - last);
			return LootDebug.EventLog.Count > 0 ? string.Join("\n", LootDebug.EventLog.Skip(start)) : "No loot events.";
		}

		// PRIVATE METHODS

		private static string LayOut(List<LootStack> items, bool boxes, string what, float distance)
		{
			Agent server = FindObservedServerAgent();
			if (server == null)
				return "No observed player on a running host.";

			int spawned = LootSpawner.LayOut(server, items, distance, SPACING, boxes);
			return $"Laid out {items.Count} {what} ({spawned} items) {distance:F0} m in front of {server.name}, {SPACING:F0} m apart.";
		}

		private static T BuildPrefab<T>(string name, int flags, StringBuilder log) where T : NetworkBehaviour
		{
			string path = $"{PrefabFolder}/{name}.prefab";

			var root = new GameObject(name);
			try
			{
				var networkObject = root.AddComponent<NetworkObject>();
				root.AddComponent<T>();

				if (flags != 0)
				{
					var serialized = new SerializedObject(networkObject);
					SerializedProperty property = serialized.FindProperty("Flags");
					if (property != null)
					{
						property.intValue = flags;
						serialized.ApplyModifiedPropertiesWithoutUndo();
					}
				}

				PrefabUtility.SaveAsPrefabAsset(root, path);
				log.AppendLine($"Saved {path}.");
			}
			finally
			{
				Object.DestroyImmediate(root);
			}

			return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<T>();
		}

		/// <summary>The sample's item box NetworkObject flags (area of interest), so ground loot replicates the way the boxes did.</summary>
		private static int GetItemBoxFlags()
		{
			GameObject itemBox = AssetDatabase.LoadAssetAtPath<GameObject>(ITEMBOX);
			NetworkObject networkObject = itemBox != null ? itemBox.GetComponent<NetworkObject>() : null;
			if (networkObject == null)
				return 0;

			SerializedProperty property = new SerializedObject(networkObject).FindProperty("Flags");
			return property != null ? property.intValue : 0;
		}

		/// <summary>The server's copy of the player the peer with input is watching.</summary>
		private static Agent FindObservedServerAgent()
		{
			Agent observed = null;
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object != null && agent.Object.IsValid == true && agent.IsObserved == true && agent.Context.HasInput == true && agent.Object.InputAuthority.IsRealPlayer == true)
				{
					observed = agent;
				}
			}

			if (observed == null)
				return null;

			return ServerCopy(observed.Object.InputAuthority);
		}

		private static Agent ServerCopy(PlayerRef playerRef)
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Runner != null && agent.Runner.IsServer == true && agent.Object != null && agent.Object.IsValid == true && agent.Object.InputAuthority == playerRef)
					return agent;
			}

			return null;
		}
	}
}
