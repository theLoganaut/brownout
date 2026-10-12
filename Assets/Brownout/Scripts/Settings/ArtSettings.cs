using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Phase 10 step 6: Logan's models for the things that were blocks: loot on the ground, grenades in hand and thrown, the grappling gun,
	/// the equipment case (gear items, and scaled up as the player loot bag), the helicopter. Filled from the files in Assets/Brownout/Art/
	/// by Brownout → Setup → Link Art Models; an empty slot falls back to the old block. Lives in Resources/Settings/ArtSettings.asset.
	/// </summary>
	[CreateAssetMenu(fileName = "ArtSettings", menuName = "TPSBR/Art Settings")]
	public sealed class ArtSettings : ScriptableObject
	{
		[Header("Loot on the ground")]
		[Tooltip("By EHealItem: bandage, med kit, trauma kit.")]
		public GameObject[] HealModels;
		[Tooltip("By armor tier 1..3.")]
		public GameObject[] ArmorModels;
		public GameObject   BackpackModel;
		[Tooltip("By ECaliber: 9mm, .45, 5.56, 7.62, .338, .50.")]
		public GameObject[] AmmoModels;

		[Header("Equipment")]
		[Tooltip("By EEquipment: frag, smoke, flash, concussion, grapple (the grappling gun). On the ground, in hand and thrown.")]
		public GameObject[] EquipmentModels;
		[Tooltip("Gear items lie on the ground as this case at GearCaseScale; the player loot bag is the same case at LootBagScale (Logan, 9 Oct).")]
		public GameObject   EquipmentCaseModel;
		public float        GearCaseScale = 0.35f;
		public float        LootBagScale  = 1.5f;

		[Header("Loot outline (phase 11a)")]
		[Tooltip("Brownout/Item Outline: the thin white line round loot on the ground (color, width in pixels, fade distances on the material). Empty = no outline.")]
		public Material     ItemOutline;

		[Header("Scope lens (phase 11a)")]
		[Tooltip("Brownout/Scope Lens: the magnified optics' glass (the numbers are in GunplaySettings' Scope lens section). Empty = the whole view zooms as before.")]
		public Material     ScopeLens;

		[Header("Helicopter")]
		public GameObject   HelicopterModel;
		[Tooltip("Children of the helicopter model that spin about their own up axis.")]
		public string[]     RotorNames  = { "VEH_Osprey_Rotor_L", "VEH_Osprey_Rotor_R" };
		[Tooltip("Rotor speed in turns per second; the two rotors turn opposite ways.")]
		public float        RotorTurnsPerSecond = 4.0f;

		// PUBLIC MEMBERS

		private static ArtSettings _current;

		/// <summary>The asset in Resources/Settings (null until Link Art Models has made it; everything then keeps its block).</summary>
		public static ArtSettings Current
		{
			get
			{
				if (_current == null)
				{
					_current = Resources.Load<ArtSettings>("Settings/ArtSettings");
				}

				return _current;
			}
		}

		// PUBLIC METHODS

		public static GameObject Get(GameObject[] models, int index)
		{
			return models != null && index >= 0 && index < models.Length ? models[index] : null;
		}

		public GameObject GetEquipmentModel(EEquipment equipment) => Get(EquipmentModels, (int)equipment);
	}
}
