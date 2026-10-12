namespace TPSBR
{
	using UnityEngine;

	public enum ELootWeighting
	{
		/// <summary>Every item equally likely (Logan, 5 Oct: for now).</summary>
		EveryItem,
		/// <summary>Each kind (guns, attachments, ammo, healing, armor, backpack) equally likely first, then each item within it.</summary>
		EveryKind,
	}

	/// <summary>
	/// Loot and inventory numbers (phase 5a), from the Items, Gear & Inventory page and gameplay_timings.json (looting).
	/// Lives in Resources/Settings/LootSettings.asset, linked from GlobalSettings.Loot (Brownout → Setup → Build Loot Prefabs).
	/// </summary>
	[CreateAssetMenu(fileName = "LootSettings", menuName = "TPSBR/Loot Settings")]
	public sealed class LootSettings : ScriptableObject
	{
		// CONSTANTS

		/// <summary>Inventory slots with a backpack, and the size of the networked slot array.</summary>
		public const int MaxInventorySlots = 10;

		[Header("Inventory")]
		[Tooltip("Inventory slots without a backpack.")]
		public int   InventorySlots = 5;
		[Tooltip("Inventory slots with a backpack (at most MaxInventorySlots).")]
		public int   BackpackSlots  = 10;
		[Tooltip("How far from the eye an item on the ground or a stash can be reached (m).")]
		public float PickupReach    = 3.0f;

		[Header("Pickup timing (gameplay_timings.json, looting)")]
		[Tooltip("A picked-up or swapped gun is raised for this long before it can fire or aim (gun_first_raise_s). Ammo, healing and armor have no lock (Logan, 5 Oct).")]
		public float GunRaiseTime      = 1.1f;
		[Tooltip("Fitting an attachment locks the gun for this long (attachment_equip_anim_s).")]
		public float AttachmentFitTime = 0.75f;

		[Header("Ammo boxes")]
		[Tooltip("A box holds this share of its caliber's carry cap: 1/5, so 40 rounds of 9mm or .45, 30 of 5.56 or 7.62, 10 of .338 or .50.")]
		public float   AmmoBoxShare   = 0.2f;
		[Tooltip("Boxes of its caliber beside a gun on a loot point.")]
		public int     BoxesBesideGun = 2;
		[Tooltip("One color per caliber (9mm, .45, 5.56, 7.62, .338, .50), so boxes can be told apart from a distance.")]
		public Color[] AmmoBoxColors  =
		{
			new Color(0.95f, 0.80f, 0.20f), // 9mm: yellow
			new Color(0.95f, 0.50f, 0.15f), // .45: orange
			new Color(0.25f, 0.70f, 0.30f), // 5.56: green
			new Color(0.80f, 0.20f, 0.20f), // 7.62: red
			new Color(0.25f, 0.45f, 0.90f), // .338: blue
			new Color(0.60f, 0.30f, 0.80f), // .50: purple
		};

		[Header("Loot table")]
		[Tooltip("EveryItem: each item equally likely (Logan, 5 Oct, to be changed later). EveryKind: each kind first, then each item within it.")]
		public ELootWeighting Weighting = ELootWeighting.EveryItem;
		[Tooltip("At top-tier loot points, tier 3 armor and trauma kits are this many times as likely.")]
		public float TopTierBonus       = 2.0f;
		[Tooltip("Loot points on a generated level (the sample's item box spots).")]
		public int   MaxGeneratedPoints = 500;

		[Header("Spawn kits")]
		[Tooltip("Players spawn with only the punch (Logan, 5 Oct). Off: the GunplaySettings loadout with full ammo and HealthSettings' healing items, as in phases 3a-4.")]
		public bool  PlayersSpawnEmptyHanded = true;
		[Tooltip("Test dummies spawn with the GunplaySettings loadout, full ammo, HealthSettings' healing items and this armor tier, so their stash has something in it.")]
		public int   DummyArmorTier = 2;

		[Header("Prefabs (Brownout → Setup → Build Loot Prefabs)")]
		public LootItem LootItemPrefab;
		public LootBag  LootBagPrefab;

		// PUBLIC MEMBERS

		private static LootSettings _fallback;

		/// <summary>
		/// The asset linked from GlobalSettings. Falls back to the defaults above (with a warning) if it isn't linked yet.
		/// </summary>
		public static LootSettings Current
		{
			get
			{
				LootSettings settings = Global.Settings != null ? Global.Settings.Loot : null;
				if (settings != null)
					return settings;

				if (_fallback == null)
				{
					_fallback = CreateInstance<LootSettings>();
					Debug.LogWarning("LootSettings isn't linked in GlobalSettings; using the code defaults. Run Brownout → Setup → Build Loot Prefabs.");
				}

				return _fallback;
			}
		}

		// PUBLIC METHODS

		public int GetSlotCount(bool hasBackpack)
		{
			return Mathf.Clamp(hasBackpack == true ? BackpackSlots : InventorySlots, 0, MaxInventorySlots);
		}

		/// <summary>Rounds in one ammo box of this caliber.</summary>
		public int GetAmmoBoxRounds(ECaliber caliber)
		{
			return Mathf.Max(1, Mathf.RoundToInt(GunplaySettings.Current.GetCarryCap(caliber) * AmmoBoxShare));
		}

		public Color GetAmmoBoxColor(ECaliber caliber)
		{
			int index = (int)caliber;
			return AmmoBoxColors != null && index >= 0 && index < AmmoBoxColors.Length ? AmmoBoxColors[index] : Color.gray;
		}
	}
}
