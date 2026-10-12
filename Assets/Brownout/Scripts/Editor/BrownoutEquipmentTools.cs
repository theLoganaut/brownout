namespace TPSBR
{
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using Fusion;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 5b tools: Brownout → Setup → Build Equipment Prefabs (the EquipmentSettings asset, linked from GlobalSettings, and the
	/// concussion grenade made from the sample's flash grenade), and Brownout → Test → Equipment: give any equipment or gear item, lay
	/// them all out, set a gear item's seconds, log the equipment events. The Equipment Check is in BrownoutEquipmentCheck.cs.
	/// </summary>
	public static partial class BrownoutEquipmentTools
	{
		// CONSTANTS

		public const string SettingsPath     = "Assets/Brownout/Resources/Settings/EquipmentSettings.asset";
		public const string ProjectileFolder = "Assets/Brownout/Prefabs/Projectiles";
		public const string ExplosionFolder  = "Assets/Brownout/Prefabs/Explosions";

		private const string MENU           = "Brownout/Test/Equipment/";
		private const float  EQUIPMENT_ROW  = 13.0f;
		private const float  SPACING        = 2.0f;

		// MENU

		[MenuItem("Brownout/Setup/Build Equipment Prefabs", priority = 5)]
		private static void BuildMenu() => Debug.Log(BuildEquipmentPrefabs());

		[MenuItem(MENU + "Give Frag", priority = 100)]               private static void GiveFragMenu()       => Debug.Log(GiveEquipment(EEquipment.Frag));
		[MenuItem(MENU + "Give Smoke", priority = 101)]              private static void GiveSmokeMenu()      => Debug.Log(GiveEquipment(EEquipment.Smoke));
		[MenuItem(MENU + "Give Flash", priority = 102)]              private static void GiveFlashMenu()      => Debug.Log(GiveEquipment(EEquipment.Flash));
		[MenuItem(MENU + "Give Concussion", priority = 103)]         private static void GiveConcussionMenu() => Debug.Log(GiveEquipment(EEquipment.Concussion));
		[MenuItem(MENU + "Give Grapple", priority = 104)]            private static void GiveGrappleMenu()    => Debug.Log(GiveEquipment(EEquipment.Grapple));
		[MenuItem(MENU + "Give Double Jump", priority = 120)]        private static void GiveDoubleJumpMenu() => Debug.Log(GiveGear(EGear.DoubleJump));
		[MenuItem(MENU + "Give Double Time", priority = 121)]        private static void GiveDoubleTimeMenu() => Debug.Log(GiveGear(EGear.DoubleTime));
		[MenuItem(MENU + "Give Ninja", priority = 122)]              private static void GiveNinjaMenu()      => Debug.Log(GiveGear(EGear.Ninja));
		[MenuItem(MENU + "Give Sixth Sense", priority = 123)]        private static void GiveSixthSenseMenu() => Debug.Log(GiveGear(EGear.SixthSense));
		[MenuItem(MENU + "Give Camouflage", priority = 124)]         private static void GiveCamouflageMenu() => Debug.Log(GiveGear(EGear.Camouflage));
		[MenuItem(MENU + "Give O2 Max", priority = 125)]             private static void GiveO2MaxMenu()      => Debug.Log(GiveGear(EGear.O2Max));
		[MenuItem(MENU + "Lay Out Equipment And Gear", priority = 140)] private static void LayOutMenu()      => Debug.Log(LayOutEquipmentAndGear());
		[MenuItem(MENU + "Log Equipment Events", priority = 160)]    private static void LogEventsMenu()      => Debug.Log(GetEquipmentEvents(60));
		[MenuItem(MENU + "Clear Equipment Events", priority = 161)]  private static void ClearEventsMenu()    { EquipmentDebug.Clear(); Debug.Log("Equipment events cleared."); }
		[MenuItem(MENU + "Print Equipment Events To Console", priority = 162)]
		private static void ToggleConsoleMenu() => EquipmentDebug.LogToConsole = !EquipmentDebug.LogToConsole;
		[MenuItem(MENU + "Print Equipment Events To Console", true)]
		private static bool ValidateConsoleMenu()
		{
			UnityEditor.Menu.SetChecked(MENU + "Print Equipment Events To Console", EquipmentDebug.LogToConsole);
			return true;
		}

		[MenuItem(MENU + "Give Frag", true)]
		[MenuItem(MENU + "Give Smoke", true)]
		[MenuItem(MENU + "Give Flash", true)]
		[MenuItem(MENU + "Give Concussion", true)]
		[MenuItem(MENU + "Give Grapple", true)]
		[MenuItem(MENU + "Give Double Jump", true)]
		[MenuItem(MENU + "Give Double Time", true)]
		[MenuItem(MENU + "Give Ninja", true)]
		[MenuItem(MENU + "Give Sixth Sense", true)]
		[MenuItem(MENU + "Give Camouflage", true)]
		[MenuItem(MENU + "Give O2 Max", true)]
		[MenuItem(MENU + "Lay Out Equipment And Gear", true)]
		private static bool ValidatePlaying() => Application.isPlaying;

		// SETUP

		/// <summary>
		/// Creates the EquipmentSettings asset if missing, links it from GlobalSettings, builds the concussion grenade (a copy of the
		/// sample's flash grenade whose blast is ConcussionExplosion, a copy of the flash's blast with Concussion in place of Flashbang),
		/// lists the four grenades in EquipmentSettings and rebuilds Fusion's prefab table.
		/// </summary>
		public static string BuildEquipmentPrefabs()
		{
			var log = new StringBuilder();

			var settings = AssetDatabase.LoadAssetAtPath<EquipmentSettings>(SettingsPath);
			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<EquipmentSettings>();
				AssetDatabase.CreateAsset(settings, SettingsPath);
				log.AppendLine($"Created {SettingsPath}.");
			}

			string globalPath = AssetDatabase.FindAssets("t:GlobalSettings").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
			var    global     = globalPath != null ? AssetDatabase.LoadAssetAtPath<GlobalSettings>(globalPath) : null;
			if (global == null)
				return "GlobalSettings asset not found.";

			if (global.Equipment != settings)
			{
				global.Equipment = settings;
				EditorUtility.SetDirty(global);
				log.AppendLine($"Linked EquipmentSettings from {globalPath}.");
			}

			// The concussion blast: a copy of the flash's, with Concussion in place of Flashbang
			string flashBlastPath      = $"{ExplosionFolder}/FlashbangExplosion.prefab";
			string concussionBlastPath = $"{ExplosionFolder}/ConcussionExplosion.prefab";

			if (AssetDatabase.LoadAssetAtPath<GameObject>(concussionBlastPath) == null)
			{
				AssetDatabase.CopyAsset(flashBlastPath, concussionBlastPath);
				log.AppendLine($"Copied {flashBlastPath} to {concussionBlastPath}.");
			}

			GameObject blastRoot = PrefabUtility.LoadPrefabContents(concussionBlastPath);
			try
			{
				foreach (Flashbang flashbang in blastRoot.GetComponents<Flashbang>())
				{
					Object.DestroyImmediate(flashbang, true);
				}

				Concussion concussion = blastRoot.GetComponent<Concussion>();
				if (concussion == null)
				{
					concussion = blastRoot.AddComponent<Concussion>();
				}

				Transform effects = blastRoot.transform.Find("EffectsRoot");
				concussion.SetEffectRoot(effects != null ? effects.gameObject : null);

				PrefabUtility.SaveAsPrefabAsset(blastRoot, concussionBlastPath);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(blastRoot);
			}

			// The concussion grenade: a copy of the flash grenade that spawns the concussion blast
			string flashGrenadePath      = $"{ProjectileFolder}/FlashGrenade.prefab";
			string concussionGrenadePath = $"{ProjectileFolder}/ConcussionGrenade.prefab";

			if (AssetDatabase.LoadAssetAtPath<GameObject>(concussionGrenadePath) == null)
			{
				AssetDatabase.CopyAsset(flashGrenadePath, concussionGrenadePath);
				log.AppendLine($"Copied {flashGrenadePath} to {concussionGrenadePath}.");
			}

			GameObject grenadeRoot = PrefabUtility.LoadPrefabContents(concussionGrenadePath);
			try
			{
				var projectile = grenadeRoot.GetComponent<KinematicProjectile>();
				var serialized = new SerializedObject(projectile);
				serialized.FindProperty("_impactEffect").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(concussionBlastPath);
				serialized.ApplyModifiedPropertiesWithoutUndo();

				PrefabUtility.SaveAsPrefabAsset(grenadeRoot, concussionGrenadePath);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(grenadeRoot);
			}

			settings.GrenadePrefabs = new KinematicProjectile[]
			{
				LoadProjectile("ExplosiveGrenade"),
				LoadProjectile("SmokeGrenade"),
				LoadProjectile("FlashGrenade"),
				LoadProjectile("ConcussionGrenade"),
			};
			EditorUtility.SetDirty(settings);

			AssetDatabase.SaveAssets();

			foreach (string path in new[] { concussionBlastPath, concussionGrenadePath })
			{
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				string[]   labels = AssetDatabase.GetLabels(prefab);
				if (labels.Contains(Fusion.Editor.NetworkProjectConfigImporter.FusionPrefabTag) == false)
				{
					AssetDatabase.SetLabels(prefab, labels.Append(Fusion.Editor.NetworkProjectConfigImporter.FusionPrefabTag).ToArray());
				}
			}

			Fusion.Editor.NetworkProjectConfigUtilities.ImportGlobalConfig();

			log.Insert(0, $"Equipment prefabs built: frag, smoke, flash and concussion grenades listed in {SettingsPath}.\n");
			return log.ToString();
		}

		// TOOLS

		/// <summary>Puts this equipment in the observed player's Equipment slot (what was there is dropped).</summary>
		public static string GiveEquipment(EEquipment equipment)
		{
			Agent server = FindObservedServerAgent();
			if (server == null)
				return "No observed player on a running host.";

			server.Inventory.DebugGive(LootStack.EquipmentItem(equipment), true);
			return $"{server.name}: {EquipmentSettings.GetName(equipment)} in the Equipment slot.";
		}

		public static string GiveEquipment(string name)
		{
			for (int i = 0; i < EquipmentSettings.EquipmentCount; ++i)
			{
				if (Matches(EquipmentSettings.GetName((EEquipment)i), name) == true || Matches(((EEquipment)i).ToString(), name) == true)
					return GiveEquipment((EEquipment)i);
			}

			return $"No equipment called {name}.";
		}

		/// <summary>Puts this gear item in the observed player's Gear slot (seconds &lt; 0: a new one), switched off.</summary>
		public static string GiveGear(EGear gear, float seconds = -1.0f)
		{
			Agent server = FindObservedServerAgent();
			if (server == null)
				return "No observed player on a running host.";

			LootStack stack = seconds >= 0.0f ? LootStack.Gear(gear, seconds) : LootStack.Gear(gear);
			server.Inventory.DebugGive(stack, true);
			return $"{server.name}: {EquipmentSettings.GetName(gear)} ({stack.SecondsLeft:F1} s) in the Gear slot.";
		}

		public static string GiveGear(string name, float seconds = -1.0f)
		{
			for (int i = 0; i < EquipmentSettings.GearCount; ++i)
			{
				if (Matches(EquipmentSettings.GetName((EGear)i), name) == true || Matches(((EGear)i).ToString(), name) == true)
					return GiveGear((EGear)i, seconds);
			}

			return $"No gear item called {name}.";
		}

		/// <summary>The four grenades, the grapple and the six gear items in a row 13 m in front of the observed player, 2 m apart.</summary>
		public static string LayOutEquipmentAndGear()
		{
			Agent server = FindObservedServerAgent();
			if (server == null)
				return "No observed player on a running host.";

			var items = new List<LootStack>();
			ItemCatalog.GetEquipmentAndGear(items);

			int spawned = LootSpawner.LayOut(server, items, EQUIPMENT_ROW, SPACING, false);
			return $"Laid out {items.Count} equipment and gear items ({spawned}) {EQUIPMENT_ROW:F0} m in front of {server.name}, {SPACING:F0} m apart.";
		}

		public static string GetEquipmentEvents(int last)
		{
			int start = Mathf.Max(0, EquipmentDebug.EventLog.Count - last);
			return EquipmentDebug.EventLog.Count > 0 ? string.Join("\n", EquipmentDebug.EventLog.Skip(start)) : "No equipment events.";
		}

		// PRIVATE METHODS

		private static KinematicProjectile LoadProjectile(string name)
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ProjectileFolder}/{name}.prefab");
			return prefab != null ? prefab.GetComponent<KinematicProjectile>() : null;
		}

		private static bool Matches(string a, string b)
		{
			return string.Equals(a.Replace(" ", ""), (b ?? string.Empty).Replace(" ", ""), System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>The server's copy of the player the peer with input is watching.</summary>
		public static Agent FindObservedServerAgent()
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

		public static Agent ServerCopy(PlayerRef playerRef)
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
