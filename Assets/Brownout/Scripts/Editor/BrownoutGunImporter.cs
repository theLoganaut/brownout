namespace TPSBR
{
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using Newtonsoft.Json.Linq;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 3a: gun data from References/brownout_guns.json (the file stays the place to tune; re-running overwrites the assets).
	/// Writes one GunData asset per gun, refreshes GunplaySettings from the file's shared values, links Logan's models
	/// and builds a weapon prefab for every gun (all 17 since 3b; a new gun in the file only needs a re-run).
	/// Phase 3c: also one AttachmentData asset per attachment, listed in GunplaySettings in the file's order (ID = index + 1).
	/// </summary>
	public static class BrownoutGunImporter
	{
		// CONSTANTS

		public const string JsonPath         = "References/brownout_guns.json";
		public const string DataFolder       = "Assets/Brownout/Data/Guns";
		public const string SettingsPath     = "Assets/Brownout/Resources/Settings/GunplaySettings.asset";
		public const string PrefabFolder     = "Assets/Brownout/Prefabs/Weapons/Brownout";
		public const string ModelFolder      = "Assets/Brownout/Art/Weapons";
		public const string AttachmentFolder = "Assets/Brownout/Data/Attachments";
		public const string AttachmentModelFolder = "Assets/Brownout/Art/Attachments";
		public const string RifleBasePrefab  = "Assets/Brownout/Prefabs/Weapons/Rifle_02.prefab";
		public const string SniperBasePrefab = "Assets/Brownout/Prefabs/Weapons/Sniper_01.prefab";

		/// <summary>Phase 3a decision 4: Deathmatch spawns with these.</summary>
		public static readonly string[] LoadoutGuns = { "Icarus", "Omicron" };

		private const string MENU = "Brownout/Data/";

		// MENU

		[MenuItem(MENU + "Import Guns", priority = 1)]
		private static void ImportGunsMenu()
		{
			Debug.Log(ImportGuns());
		}

		// PUBLIC METHODS

		public static string ImportGuns()
		{
			string fullJsonPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), JsonPath);
			if (File.Exists(fullJsonPath) == false)
				return $"Not found: {fullJsonPath}";

			JObject root = JObject.Parse(File.ReadAllText(fullJsonPath));
			var     log  = new System.Text.StringBuilder();

			EnsureFolder(DataFolder);
			EnsureFolder(PrefabFolder);

			GunplaySettings settings = GetOrCreateSettings(log);
			ApplySharedValues(root, settings);
			ImportAttachments(root, settings, log);

			// Every gun in the file gets its data asset and its prefab (3b), in the file's order
			var gunDatas = new List<GunData>();
			foreach (JObject gun in root["guns"])
			{
				gunDatas.Add(WriteGunData(root, gun, settings, log));
			}

			var prefabs = new Dictionary<string, Weapon>();
			foreach (GunData data in gunDatas)
			{
				Weapon prefab = BuildPrefab(data, log);
				if (prefab != null)
				{
					prefabs[data.GunName] = prefab;
				}
			}

			settings.SpawnLoadout = LoadoutGuns.Where(prefabs.ContainsKey).Select(n => prefabs[n]).ToArray();
			settings.TestGuns     = gunDatas.Where(t => prefabs.ContainsKey(t.GunName)).Select(t => prefabs[t.GunName]).ToArray();
			EditorUtility.SetDirty(settings);

			AssetDatabase.SaveAssets();

			// New gun prefabs only become spawnable once Fusion's prefab table includes them. Fusion labels prefabs on its own
			// import pass, which can come after this, so label them here and then rebuild the table
			foreach (Weapon prefab in prefabs.Values)
			{
				string[] labels = AssetDatabase.GetLabels(prefab.gameObject);
				if (labels.Contains(Fusion.Editor.NetworkProjectConfigImporter.FusionPrefabTag) == false)
				{
					AssetDatabase.SetLabels(prefab.gameObject, labels.Append(Fusion.Editor.NetworkProjectConfigImporter.FusionPrefabTag).ToArray());
				}
			}

			Fusion.Editor.NetworkProjectConfigUtilities.ImportGlobalConfig();

			log.Insert(0, $"Imported {gunDatas.Count} guns into {DataFolder} and {settings.Attachments.Length} attachments into {AttachmentFolder}, built {prefabs.Count} prefabs in {PrefabFolder}.\n");
			return log.ToString();
		}

		// PRIVATE METHODS

		private static GunplaySettings GetOrCreateSettings(System.Text.StringBuilder log)
		{
			var settings = AssetDatabase.LoadAssetAtPath<GunplaySettings>(SettingsPath);
			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<GunplaySettings>();
				AssetDatabase.CreateAsset(settings, SettingsPath);
				log.AppendLine($"Created {SettingsPath}.");
			}

			string globalPath = AssetDatabase.FindAssets("t:GlobalSettings").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
			var    global     = globalPath != null ? AssetDatabase.LoadAssetAtPath<GlobalSettings>(globalPath) : null;
			if (global != null && global.Gunplay != settings)
			{
				global.Gunplay = settings;
				EditorUtility.SetDirty(global);
				log.AppendLine($"Linked GunplaySettings from {globalPath}.");
			}

			return settings;
		}

		private static void ApplySharedValues(JObject root, GunplaySettings settings)
		{
			JToken ballistics = root["ballistics"];
			if (ballistics != null && ballistics["gravity_mps2"] != null)
			{
				settings.Gravity = ballistics.Value<float>("gravity_mps2");
			}

			JObject caps = root["ammo"]?["carry_caps_proposed"] as JObject;
			if (caps != null)
			{
				var list = new List<GunplaySettings.CaliberCap>();
				foreach (JProperty cap in caps.Properties())
				{
					if (GunData.TryParseCaliber(cap.Name, out ECaliber caliber) == true)
					{
						list.Add(new GunplaySettings.CaliberCap { Caliber = caliber, Cap = cap.Value.Value<int>() });
					}
				}

				settings.CarryCaps = list.ToArray();
			}

			EditorUtility.SetDirty(settings);
		}

		/// <summary>
		/// The file's attachments section: one AttachmentData per entry, in the file's order (that order is the ID, so keep it).
		/// Checks every gun's allowed lists name a known attachment.
		/// </summary>
		private static void ImportAttachments(JObject root, GunplaySettings settings, System.Text.StringBuilder log)
		{
			EnsureFolder(AttachmentFolder);

			settings.IronSightEyeDistance = root["iron_sights"]?.Value<float?>("eye_distance_m") ?? 0.0f;

			var attachments = new List<AttachmentData>();
			JArray entries = root["attachments"] as JArray;
			if (entries == null)
			{
				log.AppendLine("No attachments section in the data file.");
				settings.Attachments = new AttachmentData[0];
				return;
			}

			foreach (JObject entry in entries)
			{
				string attachmentName = entry.Value<string>("name");
				string path           = $"{AttachmentFolder}/{FileSafe(attachmentName)}.asset";

				var data = AssetDatabase.LoadAssetAtPath<AttachmentData>(path);
				if (data == null)
				{
					data = ScriptableObject.CreateInstance<AttachmentData>();
					AssetDatabase.CreateAsset(data, path);
				}

				data.AttachmentName = attachmentName;
				data.Slot           = ParseSlot(entry.Value<string>("slot"), attachmentName, log);
				data.UsesGunMagExt  = entry.Value<bool?>("uses_gun_mag_ext") ?? false;
				data.LeftHandPose   = entry.Value<string>("left_hand_pose");
				data.Zoom           = entry.Value<float?>("zoom") ?? 0.0f;
				data.Magnified      = entry.Value<bool?>("magnified") ?? false;
				data.EyeDistance    = entry.Value<float?>("eye_distance_m") ?? 0.0f;

				JToken effects = entry["effects"];
				data.AdsTime       = effects?.Value<float?>("ads_time") ?? 1.0f;
				data.AimIdleMotion = effects?.Value<float?>("aim_idle_motion") ?? 1.0f;
				data.BulletSpeed   = effects?.Value<float?>("bullet_speed") ?? 1.0f;
				data.FalloffRange  = effects?.Value<float?>("falloff_range") ?? 1.0f;
				data.Recoil        = effects?.Value<float?>("recoil") ?? 1.0f;
				data.HipSpread     = effects?.Value<float?>("hip_spread") ?? 1.0f;
				data.Magazine      = effects?.Value<float?>("mag") ?? 1.0f;
				data.ReloadTime    = effects?.Value<float?>("reload") ?? 1.0f;
				data.AimMoveSpeed  = effects?.Value<float?>("aim_move_speed") ?? 1.0f;
				data.GunshotRange  = effects?.Value<float?>("gunshot_range") ?? 1.0f;
				data.HidesFlash    = effects?.Value<bool?>("hides_flash") ?? false;
				data.Suppressed    = effects?.Value<bool?>("suppressed") ?? false;

				// Logan's model: ATT_<Name>.fbx or ATT_<Name>_Placeholder.fbx
				string modelName = entry.Value<string>("model");
				data.Model = null;
				if (string.IsNullOrEmpty(modelName) == false)
				{
					string modelGuid = AssetDatabase.FindAssets("t:Model", new[] { AttachmentModelFolder }).FirstOrDefault(guid =>
					{
						string file = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
						return file == modelName || file.StartsWith(modelName + "_");
					});

					data.Model = modelGuid != null ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(modelGuid)) : null;
					if (data.Model == null)
					{
						log.AppendLine($"{attachmentName}: no model {modelName} in {AttachmentModelFolder}.");
					}
				}

				EditorUtility.SetDirty(data);
				attachments.Add(data);
			}

			settings.Attachments = attachments.ToArray();
			EditorUtility.SetDirty(settings);

			// Every name a gun allows must be a known attachment
			var known = new HashSet<string>(attachments.Select(t => t.AttachmentName));
			foreach (JObject gun in root["guns"])
			{
				IEnumerable<string> allowed = (gun["optics_allowed"]?.Values<string>() ?? Enumerable.Empty<string>()).Concat(gun["attachments_allowed"]?.Values<string>() ?? Enumerable.Empty<string>());
				foreach (string name in allowed.Where(t => known.Contains(t) == false))
				{
					log.AppendLine($"{gun.Value<string>("name")} allows {name}, which isn't in the attachments section.");
				}
			}
		}

		private static EAttachmentSlot ParseSlot(string value, string attachmentName, System.Text.StringBuilder log)
		{
			switch (value)
			{
				case "optic":       return EAttachmentSlot.Optic;
				case "muzzle":      return EAttachmentSlot.Muzzle;
				case "underbarrel": return EAttachmentSlot.Underbarrel;
				case "magazine":    return EAttachmentSlot.Magazine;
				case "stock":       return EAttachmentSlot.Stock;
				case "grip":        return EAttachmentSlot.Grip;
				default:
					log.AppendLine($"{attachmentName}: unknown slot {value}.");
					return EAttachmentSlot.Optic;
			}
		}

		private static GunData WriteGunData(JObject root, JObject gun, GunplaySettings settings, System.Text.StringBuilder log)
		{
			string gunName = gun.Value<string>("name");
			string path    = $"{DataFolder}/{FileSafe(gunName)}.asset";

			var data = AssetDatabase.LoadAssetAtPath<GunData>(path);
			if (data == null)
			{
				data = ScriptableObject.CreateInstance<GunData>();
				AssetDatabase.CreateAsset(data, path);
			}

			data.GunName  = gunName;
			data.BasedOn  = gun.Value<string>("based_on");
			data.Class    = ParseClass(gun.Value<string>("class"));
			data.FireMode = ParseFireMode(gun.Value<string>("fire_mode"));

			data.BurstCount               = gun.Value<int?>("burst_count") ?? 1;
			data.Magazine                 = gun.Value<int>("mag");
			data.Damage                   = gun.Value<float>("damage");
			data.HeadMultiplier           = gun.Value<float>("headshot_multiplier");
			data.FireInterval             = gun.Value<float>("fire_interval_ms") / 1000.0f;
			data.BurstCycle               = (gun.Value<float?>("burst_cycle_ms") ?? 0.0f) / 1000.0f;
			data.FireIntervalIsLowerBound = gun.Value<bool?>("fire_interval_is_lower_bound") ?? false;
			data.ReloadTime               = gun.Value<float>("reload_s");
			data.AmmoInTime               = gun.Value<float>("ammo_in_s");
			data.AmmoInEstimated          = gun.Value<bool?>("ammo_in_estimated") ?? false;
			data.AdsTime                  = gun.Value<float>("ads_s");
			data.AdsZoom                  = gun.Value<float?>("ads_zoom_measured") ?? 0.0f;
			data.BulletSpeed              = gun.Value<float>("bullet_speed_mps_start");

			if (GunData.TryParseCaliber(gun.Value<string>("caliber"), out ECaliber caliber) == false)
			{
				log.AppendLine($"{gunName}: unknown caliber {gun.Value<string>("caliber")}.");
			}

			data.Caliber = caliber;

			JToken swap = gun["swap"];
			data.SwapWeight  = ParseWeight(swap?.Value<string>("weight"));
			data.HolsterTime = swap?.Value<float>("holster_s") ?? 0.25f;
			data.DrawTime    = swap?.Value<float>("draw_s") ?? 0.35f;

			data.OpticsAllowed      = gun["optics_allowed"]?.Values<string>().ToArray() ?? new string[0];
			data.AttachmentsAllowed = gun["attachments_allowed"]?.Values<string>().ToArray() ?? new string[0];

			// Parts built into the model (phase 10): a fixed scope instead of the optic slot, and stocks / foregrips whose effects always apply
			JToken scope = gun["built_in_scope"];
			data.BuiltInScopeZoom      = scope?.Value<float?>("zoom") ?? 0.0f;
			data.BuiltInScopeMagnified = scope?.Value<bool?>("magnified") ?? false;
			data.BuiltInScopeEyeDistance = scope?.Value<float?>("eye_distance_m") ?? 0.0f;

			var builtIn = new List<AttachmentData>();
			foreach (string name in gun["built_in_attachments"]?.Values<string>() ?? Enumerable.Empty<string>())
			{
				AttachmentData attachment = settings.Attachments.FirstOrDefault(t => t.AttachmentName == name);
				if (attachment != null)
				{
					builtIn.Add(attachment);
				}
				else
				{
					log.AppendLine($"{gunName}: built-in {name} isn't in the attachments section.");
				}
			}

			data.BuiltInAttachments = builtIn.ToArray();

			JToken recoil = gun["recoil_measured"];
			var    steps  = new List<Vector2>();
			if (recoil != null && recoil.Type == JTokenType.Object)
			{
				JToken pattern = recoil["ads_pattern_deg_first12"];
				if (pattern != null && pattern.Type == JTokenType.Array)
				{
					// Pairs are [side, up] in degrees
					foreach (JToken pair in pattern)
					{
						steps.Add(new Vector2(pair[0].Value<float>(), pair[1].Value<float>()));
					}
				}

				data.AdsUpMean       = recoil.Value<float?>("ads_up_mean_deg") ?? 0.0f;
				data.AdsUpMax        = recoil.Value<float?>("ads_up_max_deg") ?? 0.0f;
				data.AdsSideMean     = recoil.Value<float?>("ads_side_mean_deg") ?? 0.0f;
				data.AdsSideRange    = recoil.Value<float?>("ads_side_range_deg") ?? 0.0f;
				data.AdsSettleTime   = recoil.Value<float?>("ads_settle_after_release_s") ?? 0.0f;
				data.HipClimbPerShot = recoil.Value<float?>("hip_climb_deg_per_shot") ?? 0.0f;
				data.HipSideRange    = recoil.Value<float?>("hip_side_range_deg") ?? 0.0f;
			}
			else
			{
				data.AdsUpMean = data.AdsUpMax = data.AdsSideMean = data.AdsSideRange = data.AdsSettleTime = data.HipClimbPerShot = data.HipSideRange = 0.0f;
			}

			data.AdsRecoilSteps = steps.ToArray();

			// Phase 11e: the kickback feel by gun class (recoil_feel), render only
			JToken feel = root["recoil_feel"]?["classes"]?[gun.Value<string>("class")];
			if (feel == null)
			{
				log.AppendLine($"{gunName}: no recoil_feel entry for class {gun.Value<string>("class")}; kept the defaults.");
			}
			data.KickBack     = feel?.Value<float?>("kickback_m") ?? 0.03f;
			data.KickPitch    = feel?.Value<float?>("gun_pitch_deg") ?? 1.0f;
			data.PunchPitch   = feel?.Value<float?>("punch_pitch_deg") ?? 0.35f;
			data.PunchRoll    = feel?.Value<float?>("punch_roll_deg") ?? 0.25f;
			data.KickRecovery = feel?.Value<float?>("recovery") ?? 14.0f;

			// Logan's model, if there is one: WPN_<Name>.fbx, else an older WPN_<Name>_Placeholder.fbx
			string   modelName  = $"WPN_{FileSafe(gunName)}";
			string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { ModelFolder });
			string   modelGuid  = modelGuids.FirstOrDefault(guid => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)) == modelName)
			                   ?? modelGuids.FirstOrDefault(guid => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)).StartsWith(modelName + "_"));
			data.Model = modelGuid != null ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(modelGuid)) : null;

			EditorUtility.SetDirty(data);
			return data;
		}

		/// <summary>
		/// Copies the sample's rifle or sniper (third-person model, pickup, sounds, effects, hip spread) and swaps the hitscan
		/// component for BulletWeapon with the gun's data.
		/// </summary>
		private static Weapon BuildPrefab(GunData data, System.Text.StringBuilder log)
		{
			string basePath   = data.Class == EGunClass.Sniper ? SniperBasePrefab : RifleBasePrefab;
			var    basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
			if (basePrefab == null)
			{
				log.AppendLine($"Base prefab missing: {basePath}");
				return null;
			}

			string prefabPath = $"{PrefabFolder}/Gun_{FileSafe(data.GunName)}.prefab";

			var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
			try
			{
				PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
				instance.name = $"Gun_{FileSafe(data.GunName)}";

				var hitscan = instance.GetComponent<HitscanWeapon>();
				var weapon  = instance.GetComponent<BulletWeapon>();

				if (weapon == null)
				{
					weapon = instance.AddComponent<BulletWeapon>();
				}

				if (hitscan != null)
				{
					// Same field names in Weapon / FirearmWeapon, plus _projectile and _impactSetup. Copied property by property,
					// so references to the prefab's own children (fire transform, left hand target, audio) survive
					var source = new SerializedObject(hitscan);
					var target = new SerializedObject(weapon);

					SerializedProperty property = source.GetIterator();
					bool enterChildren = true;
					while (property.NextVisible(enterChildren) == true)
					{
						enterChildren = false;
						if (property.name == "m_Script")
							continue;

						if (target.FindProperty(property.propertyPath) != null)
						{
							target.CopyFromSerializedProperty(property);
						}
					}

					target.ApplyModifiedPropertiesWithoutUndo();
					Object.DestroyImmediate(hitscan);
				}

				var serialized = new SerializedObject(weapon);
				serialized.FindProperty("_gunData").objectReferenceValue = data;
				serialized.FindProperty("_weaponID").stringValue         = $"Weapons.Brownout.{FileSafe(data.GunName)}";
				serialized.FindProperty("_displayName").stringValue      = data.GunName;
				serialized.FindProperty("_nameShortcut").stringValue     = Shortcut(data.GunName);
				serialized.FindProperty("_hitType").intValue             = (int)(data.Class == EGunClass.Sniper ? EHitType.Sniper : data.Class == EGunClass.SMG ? EHitType.SMG : EHitType.Rifle);
				serialized.FindProperty("_maxMagazineAmmo").intValue     = data.Magazine;
				serialized.FindProperty("_initialAmmo").intValue         = data.Magazine;
				serialized.FindProperty("_maxWeaponAmmo").intValue       = 0;
				serialized.FindProperty("_reloadTime").floatValue        = data.ReloadTime;
				serialized.FindProperty("_fireOnKeyDownOnly").boolValue  = data.FireMode == EFireMode.Semi || data.FireMode == EFireMode.Bolt;
				serialized.FindProperty("_cadence").intValue             = Mathf.RoundToInt(60.0f / Mathf.Max(0.01f, data.FireInterval));
				serialized.FindProperty("_recoilPattern").objectReferenceValue = null;
				serialized.ApplyModifiedPropertiesWithoutUndo();

				if (data.Model != null)
				{
					Transform model = ReplaceVisuals(instance.transform, data.Model, log);

					// Phase 3c: attachments on the third-person and dropped gun
					if (model != null)
					{
						var visuals = instance.GetComponent<WeaponAttachmentVisuals>() ?? instance.AddComponent<WeaponAttachmentVisuals>();
						var visualsSerialized = new SerializedObject(visuals);
						visualsSerialized.FindProperty("_model").objectReferenceValue = model;
						visualsSerialized.ApplyModifiedPropertiesWithoutUndo();
					}
				}

				var saved = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool success);
				if (success == false || saved == null)
				{
					log.AppendLine($"Saving {prefabPath} failed.");
					return null;
				}

				log.AppendLine($"{data.GunName}: {prefabPath} (from {Path.GetFileName(basePath)}){(data.Model != null ? $", model {data.Model.name}" : ", block placeholder")}.");
				return saved.GetComponent<Weapon>();
			}
			finally
			{
				Object.DestroyImmediate(instance);
			}
		}

		/// <summary>
		/// Third-person and dropped gun: Logan's model in place of the sample's meshes under Visuals. The model's trigger goes
		/// where the sample's trigger was, so the sample's hand animations still hold it by the grip. The fire point moves to
		/// the model's _Muzzle, the left hand target slides along the gun to its _LeftHand, and the pickup collider fits the model.
		/// </summary>
		private static Transform ReplaceVisuals(Transform root, GameObject model, System.Text.StringBuilder log)
		{
			Transform visuals = root.Find("Visuals");
			if (visuals == null)
			{
				log.AppendLine($"{root.name}: no Visuals object; sample model kept.");
				return null;
			}

			Vector3? sampleTrigger = FindTriggerPoint(visuals, root);

			for (int i = visuals.childCount - 1; i >= 0; --i)
			{
				Object.DestroyImmediate(visuals.GetChild(i).gameObject);
			}

			// Nested model prefab, so a re-export from Blender shows up without re-running the import
			var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model, visuals);
			modelInstance.transform.localPosition = Vector3.zero;
			modelInstance.transform.localRotation = Quaternion.identity;
			modelInstance.transform.localScale    = Vector3.one;

			Vector3 modelTrigger = FindTriggerPoint(modelInstance.transform, root) ?? Vector3.zero;
			if (sampleTrigger.HasValue == true)
			{
				modelInstance.transform.position += root.TransformVector(sampleTrigger.Value - modelTrigger);
			}

			Transform muzzle   = null;
			Transform leftHand = null;

			foreach (Transform node in modelInstance.GetComponentsInChildren<Transform>(true))
			{
				if (node.name.EndsWith("_Muzzle")   == true) { muzzle   = node; }
				if (node.name.EndsWith("_LeftHand") == true) { leftHand = node; }

				// Default parts show; the extended mag shows only with that attachment (WeaponAttachmentVisuals)
				if (node.name.EndsWith("_MagExt") == true)
				{
					node.gameObject.SetActive(false);
				}
			}

			Transform fireTransform = root.Find("FireTransform");
			if (fireTransform != null && muzzle != null)
			{
				fireTransform.position = muzzle.position;
			}

			// The target is the hand bone, which sits beside the handguard, so only its distance along the gun changes
			Transform leftHandTarget = root.Find("LeftHandTarget");
			if (leftHandTarget != null && leftHand != null)
			{
				Vector3 target = root.InverseTransformPoint(leftHandTarget.position);
				target.z = root.InverseTransformPoint(leftHand.position).z;
				leftHandTarget.position = root.TransformPoint(target);
			}

			var pickupCollider = root.Find("Collider") != null ? root.Find("Collider").GetComponent<BoxCollider>() : null;
			if (pickupCollider != null)
			{
				Bounds? bounds = null;
				foreach (Renderer renderer in modelInstance.GetComponentsInChildren<Renderer>(false))
				{
					Bounds rendererBounds = renderer.bounds;
					Vector3 min = pickupCollider.transform.InverseTransformPoint(rendererBounds.min);
					Vector3 max = pickupCollider.transform.InverseTransformPoint(rendererBounds.max);
					var local = new Bounds((min + max) * 0.5f, Vector3.zero);
					local.Encapsulate(min);
					local.Encapsulate(max);

					if (bounds.HasValue == true)
					{
						Bounds combined = bounds.Value;
						combined.Encapsulate(local);
						bounds = combined;
					}
					else
					{
						bounds = local;
					}
				}

				if (bounds.HasValue == true)
				{
					pickupCollider.center = bounds.Value.center;
					pickupCollider.size   = bounds.Value.size;
				}
			}

			if (muzzle == null || leftHand == null)
			{
				log.AppendLine($"{root.name}: {model.name} is missing its _Muzzle or _LeftHand empty; the sample's points are kept for those.");
			}

			return modelInstance.transform;
		}

		/// <summary>Centre of the renderer named *Trigger*, in root space (where the hand animations hold the gun).</summary>
		private static Vector3? FindTriggerPoint(Transform parent, Transform root)
		{
			foreach (Renderer renderer in parent.GetComponentsInChildren<Renderer>(true))
			{
				if (renderer.name.Contains("Trigger") == true)
					return root.InverseTransformPoint(renderer.bounds.center);
			}

			return null;
		}

		private static EGunClass ParseClass(string value)
		{
			switch (value)
			{
				case "SMG":      return EGunClass.SMG;
				case "Tactical": return EGunClass.Tactical;
				case "Sniper":   return EGunClass.Sniper;
				default:         return EGunClass.AR;
			}
		}

		private static EFireMode ParseFireMode(string value)
		{
			switch (value)
			{
				case "semi":  return EFireMode.Semi;
				case "burst": return EFireMode.Burst;
				case "bolt":  return EFireMode.Bolt;
				default:      return EFireMode.Auto;
			}
		}

		private static ESwapWeight ParseWeight(string value)
		{
			switch (value)
			{
				case "light":        return ESwapWeight.Light;
				case "heavy":        return ESwapWeight.Heavy;
				case "sniper":       return ESwapWeight.Sniper;
				case "heavy_sniper": return ESwapWeight.HeavySniper;
				default:             return ESwapWeight.Medium;
			}
		}

		private static string FileSafe(string gunName)
		{
			return gunName.Replace(" ", string.Empty);
		}

		private static string Shortcut(string gunName)
		{
			string letters = FileSafe(gunName).ToUpperInvariant();
			return letters.Length > 4 ? letters.Substring(0, 4) : letters;
		}

		private static void EnsureFolder(string folder)
		{
			if (AssetDatabase.IsValidFolder(folder) == true)
				return;

			string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
		}
	}
}
