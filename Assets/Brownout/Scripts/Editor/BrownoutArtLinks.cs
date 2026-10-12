namespace TPSBR
{
	using System.Text;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 10 step 6: Brownout → Setup → Link Art Models. Links Logan's models in Assets/Brownout/Art/ (Items/, Armor/, Vehicles/) into
	/// Resources/Settings/ArtSettings.asset by file name, puts his grenade meshes on the four thrown-grenade prefabs, and turns the
	/// nature prefabs into his trees, bushes and rocks (then rebuild the map's Nature to place them). Safe to run again.
	/// </summary>
	public static class BrownoutArtLinks
	{
		// CONSTANTS

		public const string AssetPath = "Assets/Brownout/Resources/Settings/ArtSettings.asset";

		private const string ITEMS    = BrownoutModelImport.ArtFolder + "Items/";
		private const string ARMOR    = BrownoutModelImport.ArtFolder + "Armor/";
		private const string VEHICLES = BrownoutModelImport.ArtFolder + "Vehicles/";

		// MENU

		[MenuItem("Brownout/Setup/Link Art Models", priority = 15)]
		private static void LinkMenu() => Debug.Log(LinkAll());

		// PUBLIC METHODS

		public static string LinkAll()
		{
			var log = new StringBuilder("Link Art Models:\n");

			ArtSettings settings = AssetDatabase.LoadAssetAtPath<ArtSettings>(AssetPath);
			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<ArtSettings>();
				AssetDatabase.CreateAsset(settings, AssetPath);
				log.AppendLine($"Made {AssetPath}.");
			}

			settings.HealModels         = Load(log, ITEMS + "ITM_Bandage.fbx", ITEMS + "ITM_MedKit.fbx", ITEMS + "ITM_TraumaKit.fbx");
			settings.ArmorModels        = Load(log, ARMOR + "ITM_Armor_T1.fbx", ARMOR + "ITM_Armor_T2.fbx", ARMOR + "ITM_Armor_T3.fbx");
			settings.BackpackModel      = Load(log, ARMOR + "ITM_Backpack.fbx")[0];
			settings.AmmoModels         = Load(log, ITEMS + "ITM_Ammo_9mm.fbx", ITEMS + "ITM_Ammo_45Cal.fbx", ITEMS + "ITM_Ammo_556mm.fbx", ITEMS + "ITM_Ammo_762mm.fbx", ITEMS + "ITM_Ammo_338Cal.fbx", ITEMS + "ITM_Ammo_50Cal.fbx");
			settings.EquipmentModels    = Load(log, ITEMS + "ITM_Grenade_Frag.fbx", ITEMS + "ITM_Grenade_Smoke.fbx", ITEMS + "ITM_Grenade_Flash.fbx", ITEMS + "ITM_Grenade_Concussion.fbx", ITEMS + "ITM_GrapplingGun.fbx");
			settings.EquipmentCaseModel = Load(log, ITEMS + "ITM_EquipmentCase.fbx")[0];
			settings.HelicopterModel    = Load(log, VEHICLES + "VEH_Osprey.fbx")[0];
			settings.ItemOutline        = MakeMaterial(log, "Assets/Brownout/Art/Materials/ItemOutline.mat", "Brownout/Item Outline");
			settings.ScopeLens          = MakeMaterial(log, "Assets/Brownout/Art/Materials/ScopeLens.mat", "Brownout/Scope Lens");

			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssets();
			log.AppendLine("ArtSettings linked (heal 3, armor 3, backpack, ammo 6, equipment 5, equipment case, helicopter).");

			log.AppendLine(SetGrenadeMeshes(settings));
			log.AppendLine(BrownoutNatureBuilder.UseModels());

			return log.ToString();
		}

		// PRIVATE METHODS

		/// <summary>Phase 11a: a material for one of our shaders (loot outline, scope lens), made once; tune it on the material.</summary>
		public static Material MakeMaterial(StringBuilder log, string path, string shaderName)
		{
			var material = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (material != null)
				return material;

			Shader shader = Shader.Find(shaderName);
			if (shader == null)
			{
				log.AppendLine($"  {shaderName} shader not found; {path} not made.");
				return null;
			}

			System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
			material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
			AssetDatabase.CreateAsset(material, path);
			log.AppendLine($"Made {path}.");
			return material;
		}

		private static GameObject[] Load(StringBuilder log, params string[] paths)
		{
			var models = new GameObject[paths.Length];
			for (int i = 0; i < paths.Length; ++i)
			{
				models[i] = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
				if (models[i] == null)
				{
					log.AppendLine($"  missing {paths[i]} (keeps its block)");
				}
			}
			return models;
		}

		/// <summary>
		/// The thrown grenades (EquipmentSettings.GrenadePrefabs, by EEquipment) draw Logan's model: its mesh and material go on the sample's
		/// spinning Grenade mesh, centered on the spin point.
		/// </summary>
		private static string SetGrenadeMeshes(ArtSettings settings)
		{
			var equipment = AssetDatabase.LoadAssetAtPath<EquipmentSettings>("Assets/Brownout/Resources/Settings/EquipmentSettings.asset");
			if (equipment == null || equipment.GrenadePrefabs == null)
				return "Grenades: no EquipmentSettings grenade prefabs.";

			int done = 0;

			for (int i = 0; i < equipment.GrenadePrefabs.Length; ++i)
			{
				GameObject prefab = equipment.GrenadePrefabs[i] != null ? equipment.GrenadePrefabs[i].gameObject : null;
				GameObject model  = settings.GetEquipmentModel((EEquipment)i);
				if (prefab == null || model == null)
					continue;

				MeshFilter   modelFilter   = model.GetComponentInChildren<MeshFilter>();
				MeshRenderer modelRenderer = modelFilter != null ? modelFilter.GetComponent<MeshRenderer>() : null;
				if (modelFilter == null || modelRenderer == null)
					continue;

				string     path = AssetDatabase.GetAssetPath(prefab);
				GameObject root = PrefabUtility.LoadPrefabContents(path);
				try
				{
					Transform grenade = root.transform.Find("Visual/DummyRotationTarget/Grenade");
					if (grenade == null)
						continue;

					grenade.GetComponent<MeshFilter>().sharedMesh        = modelFilter.sharedMesh;
					grenade.GetComponent<MeshRenderer>().sharedMaterials = modelRenderer.sharedMaterials;
					grenade.localRotation = Quaternion.identity;
					grenade.localScale    = Vector3.one;
					grenade.localPosition = -modelFilter.sharedMesh.bounds.center;

					PrefabUtility.SaveAsPrefabAsset(root, path);
					++done;
				}
				finally
				{
					PrefabUtility.UnloadPrefabContents(root);
				}
			}

			return $"Grenades: {done} thrown-grenade prefabs draw Logan's models.";
		}
	}
}
