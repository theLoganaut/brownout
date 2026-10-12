namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using Fusion;

	/// <summary>What an item is. Equipment and gear items joined in phase 5b.</summary>
	public enum EItemKind : byte
	{
		None       = 0,
		Gun        = 1,
		Attachment = 2,
		Heal       = 3,
		Armor      = 4,
		Ammo       = 5,
		Backpack   = 6,
		Equipment  = 7,
		Gear       = 8,
	}

	/// <summary>
	/// One item or stack (phase 5a): on the ground (LootItem), in a stash (LootBag) or in an inventory slot. Index is the gun's index
	/// in GunplaySettings.TestGuns, the attachment ID (GunplaySettings.Attachments index + 1), the EHealItem, the armor tier (1..3),
	/// the ECaliber, the EEquipment or the EGear. A gun carries its mag and attachments, armor its damage left, the grapple its pulls
	/// left and a gear item its seconds left.
	/// </summary>
	public struct LootStack : INetworkStruct
	{
		// PUBLIC MEMBERS

		public byte   KindValue;
		public byte   Index;
		/// <summary>Stack size: rounds for ammo, items for healing, 1 for everything else.</summary>
		public ushort Count;
		/// <summary>Gun: rounds in the mag. Grapple: pulls left (Charges).</summary>
		public ushort Mag;
		/// <summary>Armor: incoming damage left before it breaks. Gear: seconds of use left (SecondsLeft).</summary>
		public float  Armor;
		/// <summary>Gun: its six attachment IDs (BulletWeapon.AttachmentKey).</summary>
		public long   Attachments;

		public EItemKind  Kind        { get { return (EItemKind)KindValue; } set { KindValue = (byte)value; } }
		public bool       IsEmpty     => KindValue == 0 || Count == 0;
		public EEquipment Equipment   => (EEquipment)Index;
		public EGear      GearItem    => (EGear)Index;
		/// <summary>Grapple: pulls left.</summary>
		public int        Charges     { get { return Mag; } set { Mag = (ushort)Mathf.Clamp(value, 0, ushort.MaxValue); } }
		/// <summary>Gear: seconds of use left.</summary>
		public float      SecondsLeft { get { return Armor; } set { Armor = value; } }

		// PUBLIC METHODS

		/// <summary>Same kind of item (stacks with it).</summary>
		public bool IsSameItem(LootStack other) => KindValue == other.KindValue && Index == other.Index;

		public static LootStack Gun(int gunIndex, int mag, long attachments) => new LootStack { Kind = EItemKind.Gun, Index = (byte)gunIndex, Count = 1, Mag = (ushort)Mathf.Max(0, mag), Attachments = attachments };
		public static LootStack Attachment(int id)                           => new LootStack { Kind = EItemKind.Attachment, Index = (byte)id, Count = 1 };
		public static LootStack Heal(EHealItem item, int count)              => new LootStack { Kind = EItemKind.Heal, Index = (byte)item, Count = (ushort)Mathf.Max(0, count) };
		public static LootStack ArmorVest(int tier, float left)              => new LootStack { Kind = EItemKind.Armor, Index = (byte)tier, Count = 1, Armor = left };
		public static LootStack Ammo(ECaliber caliber, int rounds)           => new LootStack { Kind = EItemKind.Ammo, Index = (byte)caliber, Count = (ushort)Mathf.Clamp(rounds, 0, ushort.MaxValue) };
		public static LootStack Backpack()                                   => new LootStack { Kind = EItemKind.Backpack, Count = 1 };
		/// <summary>One grenade, or a full grapple (EquipmentSettings.GrapplePulls).</summary>
		public static LootStack EquipmentItem(EEquipment equipment)          => new LootStack { Kind = EItemKind.Equipment, Index = (byte)equipment, Count = 1, Mag = (ushort)(equipment == EEquipment.Grapple ? Mathf.Max(1, EquipmentSettings.Current.GrapplePulls) : 0) };
		/// <summary>A gear item with this many seconds left (a new one: EquipmentSettings.GearSeconds).</summary>
		public static LootStack Gear(EGear gear, float secondsLeft)          => new LootStack { Kind = EItemKind.Gear, Index = (byte)gear, Count = 1, Armor = secondsLeft };
		public static LootStack Gear(EGear gear)                             => Gear(gear, EquipmentSettings.Current.GearSeconds);

		public LootStack WithCount(int count)
		{
			LootStack stack = this;
			stack.Count = (ushort)Mathf.Clamp(count, 0, ushort.MaxValue);
			return stack;
		}

		public override string ToString()
		{
			return IsEmpty == true ? "empty" : ItemCatalog.GetLabel(this);
		}
	}

	/// <summary>
	/// Names, stack sizes and the list of every item (phase 5a), built from the data that already exists: GunData, AttachmentData,
	/// HealthSettings (healing and armor) and the six calibers.
	/// </summary>
	public static class ItemCatalog
	{
		// PUBLIC METHODS

		public static int GunCount => GunplaySettings.Current.TestGuns != null ? GunplaySettings.Current.TestGuns.Length : 0;

		/// <summary>The gun prefab for a gun index (GunplaySettings.TestGuns, all 17 guns in the file's order).</summary>
		public static BulletWeapon GetGunPrefab(int gunIndex)
		{
			Weapon[] guns = GunplaySettings.Current.TestGuns;
			return guns != null && gunIndex >= 0 && gunIndex < guns.Length ? guns[gunIndex] as BulletWeapon : null;
		}

		public static GunData GetGunData(int gunIndex)
		{
			BulletWeapon prefab = GetGunPrefab(gunIndex);
			return prefab != null ? prefab.GunData : null;
		}

		/// <summary>The gun index of this gun's data, or -1.</summary>
		public static int GetGunIndex(GunData data)
		{
			Weapon[] guns = GunplaySettings.Current.TestGuns;
			if (guns == null || data == null)
				return -1;

			for (int i = 0; i < guns.Length; ++i)
			{
				if (guns[i] is BulletWeapon gun && gun.GunData == data)
					return i;
			}

			return -1;
		}

		public static int GetGunIndex(string gunName)
		{
			Weapon[] guns = GunplaySettings.Current.TestGuns;
			if (guns == null)
				return -1;

			for (int i = 0; i < guns.Length; ++i)
			{
				if (guns[i] is BulletWeapon gun && gun.GunData != null && string.Equals(gun.GunData.GunName, gunName, System.StringComparison.OrdinalIgnoreCase) == true)
					return i;
			}

			return -1;
		}

		public static AttachmentData GetAttachment(LootStack stack)
		{
			return stack.Kind == EItemKind.Attachment ? GunplaySettings.Current.GetAttachment(stack.Index) : null;
		}

		/// <summary>The most of this item one slot holds (healing items stack; everything else is one per slot).</summary>
		public static int GetStackSize(LootStack stack)
		{
			switch (stack.Kind)
			{
				case EItemKind.Heal: return Mathf.Max(1, HealthSettings.Current.GetHealItem((EHealItem)stack.Index).Stack);
				case EItemKind.Ammo: return ushort.MaxValue;
				default:             return 1;
			}
		}

		public static string GetName(LootStack stack)
		{
			switch (stack.Kind)
			{
				case EItemKind.Gun:
					GunData gun = GetGunData(stack.Index);
					return gun != null ? gun.GunName : $"Gun {stack.Index}";
				case EItemKind.Attachment:
					AttachmentData attachment = GetAttachment(stack);
					return attachment != null ? attachment.AttachmentName : $"Attachment {stack.Index}";
				case EItemKind.Heal:
					return HealthSettings.Current.GetHealItem((EHealItem)stack.Index).Name;
				case EItemKind.Armor:
					return $"Armor tier {stack.Index}";
				case EItemKind.Ammo:
					return $"{GunData.GetCaliberName((ECaliber)stack.Index)} ammo";
				case EItemKind.Backpack:
					return "Backpack";
				case EItemKind.Equipment:
					return EquipmentSettings.GetName(stack.Equipment);
				case EItemKind.Gear:
					return EquipmentSettings.GetName(stack.GearItem);
				default:
					return "Nothing";
			}
		}

		/// <summary>Name with the count where it matters (ammo rounds, healing stacks, armor left).</summary>
		public static string GetLabel(LootStack stack)
		{
			switch (stack.Kind)
			{
				case EItemKind.Ammo:  return $"{GetName(stack)} ({stack.Count})";
				case EItemKind.Heal:  return stack.Count > 1 ? $"{GetName(stack)} x{stack.Count}" : GetName(stack);
				case EItemKind.Armor: return $"{GetName(stack)} ({stack.Armor:F0}/{HealthSettings.Current.GetArmorTier(stack.Index).Durability:F0})";
				case EItemKind.Equipment when stack.Equipment == EEquipment.Grapple: return $"{GetName(stack)} ({stack.Charges} {(stack.Charges == 1 ? "pull" : "pulls")})";
				case EItemKind.Gear:  return $"{GetName(stack)} ({stack.SecondsLeft:0.0} s)";
				default:              return GetName(stack);
			}
		}

		/// <summary>One line for the inventory tooltip, from the data.</summary>
		public static string GetDescription(LootStack stack)
		{
			switch (stack.Kind)
			{
				case EItemKind.Gun:
				{
					GunData gun = GetGunData(stack.Index);
					if (gun == null)
						return string.Empty;

					string attachments = string.Empty;
					for (int i = 0; i < AttachmentData.SlotCount; ++i)
					{
						AttachmentData attachment = GunplaySettings.Current.GetAttachment((int)((stack.Attachments >> (8 * i)) & 0xFF));
						if (attachment != null)
						{
							attachments += (attachments.Length > 0 ? ", " : ", with ") + attachment.AttachmentName;
						}
					}

					return $"{gun.Class} {gun.FireMode}, {GunData.GetCaliberName(gun.Caliber)}, {stack.Mag} in the mag{attachments}.";
				}
				case EItemKind.Attachment:
				{
					AttachmentData attachment = GetAttachment(stack);
					return attachment != null ? $"{attachment.Slot} attachment. Left click puts it on the gun shown." : string.Empty;
				}
				case EItemKind.Heal:
				{
					HealthSettings.HealItem item = HealthSettings.Current.GetHealItem((EHealItem)stack.Index);
					string amount = item.Amount >= item.Cap ? $"Heals to {item.Cap:F0}" : $"+{item.Amount:F0} health, up to {item.Cap:F0}";
					return $"{amount}. Use time {item.UseTime:0.#} s. Stacks to {item.Stack}.";
				}
				case EItemKind.Armor:
				{
					HealthSettings.ArmorTier tier = HealthSettings.Current.GetArmorTier(stack.Index);
					string head = tier.HeadCut > 0.0f ? $", head {tier.HeadCut * 100.0f:F0}%" : string.Empty;
					return $"Cuts bullet damage: body {tier.BodyCut * 100.0f:F0}%{head}. {stack.Armor:F0} of {tier.Durability:F0} left before it breaks.";
				}
				case EItemKind.Ammo:
					return $"{stack.Count} rounds of {GunData.GetCaliberName((ECaliber)stack.Index)}.";
				case EItemKind.Backpack:
					return $"Inventory slots go from {LootSettings.Current.InventorySlots} to {LootSettings.Current.BackpackSlots}.";
				case EItemKind.Equipment:
					return EquipmentSettings.Current.GetDescription(stack.Equipment) + " Used with E.";
				case EItemKind.Gear:
					return $"{EquipmentSettings.Current.GetDescription(stack.GearItem)} Left Ctrl switches it on and off; {stack.SecondsLeft:0.0} of {EquipmentSettings.Current.GearSeconds:0} s left.";
				default:
					return string.Empty;
			}
		}

		/// <summary>
		/// One of every item: 17 guns (full mag, no attachments), 11 attachments, 3 healing items, 3 armor tiers, 6 ammo boxes, the backpack,
		/// 5 equipment items and 6 gear items (phase 5b).
		/// </summary>
		public static void GetAllItems(List<LootStack> items)
		{
			items.Clear();
			GetGuns(items);
			GetAttachments(items);
			GetOtherItems(items);
			GetEquipmentAndGear(items);
		}

		public static void GetGuns(List<LootStack> items)
		{
			for (int i = 0; i < GunCount; ++i)
			{
				GunData gun = GetGunData(i);
				if (gun != null)
				{
					items.Add(LootStack.Gun(i, gun.Magazine, 0));
				}
			}
		}

		public static void GetAttachments(List<LootStack> items)
		{
			AttachmentData[] attachments = GunplaySettings.Current.Attachments;
			for (int i = 0; attachments != null && i < attachments.Length; ++i)
			{
				if (attachments[i] != null)
				{
					items.Add(LootStack.Attachment(i + 1));
				}
			}
		}

		/// <summary>Healing (one of each), armor (each tier, full), ammo boxes (one per caliber) and the backpack.</summary>
		public static void GetOtherItems(List<LootStack> items)
		{
			HealthSettings health = HealthSettings.Current;

			for (int i = 0; i < HealthSettings.HealItemCount; ++i)
			{
				items.Add(LootStack.Heal((EHealItem)i, 1));
			}

			for (int tier = 1; tier <= 3; ++tier)
			{
				items.Add(LootStack.ArmorVest(tier, health.GetArmorTier(tier).Durability));
			}

			for (int i = 0; i < GunData.CaliberCount; ++i)
			{
				items.Add(LootStack.Ammo((ECaliber)i, LootSettings.Current.GetAmmoBoxRounds((ECaliber)i)));
			}

			items.Add(LootStack.Backpack());
		}

		/// <summary>Phase 5b: the four grenades, the grapple and the six gear items.</summary>
		public static void GetEquipmentAndGear(List<LootStack> items)
		{
			for (int i = 0; i < EquipmentSettings.EquipmentCount; ++i)
			{
				items.Add(LootStack.EquipmentItem((EEquipment)i));
			}

			for (int i = 0; i < EquipmentSettings.GearCount; ++i)
			{
				items.Add(LootStack.Gear((EGear)i));
			}
		}

		/// <summary>A full ammo box of this gun's caliber.</summary>
		public static LootStack GetAmmoBox(GunData gun)
		{
			return LootStack.Ammo(gun.Caliber, LootSettings.Current.GetAmmoBoxRounds(gun.Caliber));
		}
	}
}
