using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Phase 11a: item icons rendered from Logan's models by Brownout → Setup → Bake Icons (Editor/BrownoutIconBaker.cs) into
	/// Assets/Brownout/UI/Icons/, so the HUD, inventory, ground prompts and kill feed show the real items instead of colored squares.
	/// Guns also get a white side silhouette (weapon slot, kill feed). Gear items have no models: their symbols are drawn by the baker.
	/// Re-run the bake after a model changes. Lives in Resources/Settings/IconSettings.asset; an empty slot keeps the old colored block.
	/// </summary>
	[CreateAssetMenu(fileName = "IconSettings", menuName = "TPSBR/Icon Settings")]
	public sealed class IconSettings : ScriptableObject
	{
		[Tooltip("By gun index (GunplaySettings.TestGuns).")]
		public Sprite[] Guns           = new Sprite[0];
		[Tooltip("White side silhouettes, by gun index.")]
		public Sprite[] GunSilhouettes = new Sprite[0];
		[Tooltip("By attachment ID - 1 (GunplaySettings.Attachments order).")]
		public Sprite[] Attachments    = new Sprite[0];
		[Tooltip("By EHealItem.")]
		public Sprite[] Heals          = new Sprite[0];
		[Tooltip("By armor tier - 1.")]
		public Sprite[] Armor          = new Sprite[0];
		public Sprite   Backpack;
		[Tooltip("By ECaliber.")]
		public Sprite[] Ammo           = new Sprite[0];
		[Tooltip("By EEquipment.")]
		public Sprite[] Equipment      = new Sprite[0];
		[Tooltip("By EGear (drawn symbols).")]
		public Sprite[] Gear           = new Sprite[0];
		[Tooltip("The loot bag / stash.")]
		public Sprite   Case;

		// PUBLIC MEMBERS

		private static IconSettings _current;
		private static bool         _loaded;

		/// <summary>The asset in Resources/Settings (null until Bake Icons has made it).</summary>
		public static IconSettings Current
		{
			get
			{
				if (_loaded == false || _current == null)
				{
					_current = Resources.Load<IconSettings>("Settings/IconSettings");
					_loaded  = true;
				}

				return _current;
			}
		}

		// PUBLIC METHODS

		/// <summary>The item's icon, or null (the caller draws its colored block).</summary>
		public static Sprite Get(LootStack stack)
		{
			IconSettings icons = Current;
			if (icons == null || stack.IsEmpty == true)
				return null;

			switch (stack.Kind)
			{
				case EItemKind.Gun:        return Pick(icons.Guns, stack.Index);
				case EItemKind.Attachment: return Pick(icons.Attachments, stack.Index - 1);
				case EItemKind.Heal:       return Pick(icons.Heals, stack.Index);
				case EItemKind.Armor:      return Pick(icons.Armor, stack.Index - 1);
				case EItemKind.Ammo:       return Pick(icons.Ammo, stack.Index);
				case EItemKind.Backpack:   return icons.Backpack;
				case EItemKind.Equipment:  return Pick(icons.Equipment, stack.Index);
				case EItemKind.Gear:       return Pick(icons.Gear, stack.Index);
			}

			return null;
		}

		public static Sprite GetGunSilhouette(int gunIndex)
		{
			IconSettings icons = Current;
			return icons != null ? Pick(icons.GunSilhouettes, gunIndex) : null;
		}

		public static Sprite GetArmor(int tier)
		{
			IconSettings icons = Current;
			return icons != null ? Pick(icons.Armor, tier - 1) : null;
		}

		public static Sprite Pick(Sprite[] sprites, int index)
		{
			return sprites != null && index >= 0 && index < sprites.Length ? sprites[index] : null;
		}
	}
}
