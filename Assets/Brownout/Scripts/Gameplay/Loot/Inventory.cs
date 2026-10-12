namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using Fusion;

	/// <summary>A click in the inventory screen, sent to the server.</summary>
	public enum EInventoryAction : byte
	{
		None          = 0,
		/// <summary>Left click: equip an item, attach an attachment (to the gun in Arg's slot), detach one into the inventory.</summary>
		Use           = 1,
		/// <summary>Right click: drop it at your feet (into the stash while one is open).</summary>
		Drop          = 2,
		/// <summary>Left click on a stash item (Slot = its index in the bag).</summary>
		TakeFromStash = 3,
		CloseStash    = 4,
		/// <summary>Phase 11a, dragging one inventory slot onto another (Arg = the target slot): swaps them, or tops up a stack of the same item.</summary>
		Move          = 5,
	}

	/// <summary>Slot numbers for EInventoryAction.</summary>
	public static class InventorySlot
	{
		public const int Health          = 20;
		public const int Armor           = 21;
		public const int Backpack        = 22;
		public const int Equipment       = 23;
		public const int Gear            = 24;
		/// <summary>Gun slot 1 or 2: GunFirst + 0 / 1.</summary>
		public const int GunFirst        = 31;
		/// <summary>An attachment on a gun: AttachmentFirst + (gun slot - 1) * 6 + EAttachmentSlot.</summary>
		public const int AttachmentFirst = 40;
		/// <summary>An ammo pool: AmmoFirst + ECaliber.</summary>
		public const int AmmoFirst       = 60;

		public static int Gun(int weaponSlot)                                => GunFirst + weaponSlot - Weapons.FIRST_PRIMARY_SLOT;
		public static int Attachment(int weaponSlot, EAttachmentSlot slot)   => AttachmentFirst + (weaponSlot - Weapons.FIRST_PRIMARY_SLOT) * AttachmentData.SlotCount + (int)slot;
		public static int Ammo(ECaliber caliber)                             => AmmoFirst + (int)caliber;
	}

	/// <summary>
	/// The Blackout-style inventory (phase 5a, Items, Gear & Inventory page): Health, Armor, Gear and Equipment slots, 5 inventory slots
	/// (10 with a backpack), Storage (the backpack), the two gun slots and the six ammo pools. Every change happens on the server.
	///
	/// F equips or swaps (whatever was in the slot drops); G picks up into the inventory, using an empty equipped slot first, and asks the
	/// client to open its inventory when there's no room. A gun pickup or swap and an attachment fit lock firing (LootSettings); ammo,
	/// healing and armor don't (Logan, 5 Oct). On death everything goes into a loot bag.
	/// </summary>
	public sealed class Inventory
	{
		// PRIVATE MEMBERS

		private readonly Agent           _agent;
		private readonly List<LootStack> _displaced = new List<LootStack>(4);
		private readonly List<LootStack> _removed   = new List<LootStack>(12);

		private NetworkRunner Runner => _agent.Runner;
		private Weapons       Weapons => _agent.Weapons;
		private Health        Health  => _agent.Health;

		// CONSTRUCTORS

		public Inventory(Agent agent)
		{
			_agent = agent;
		}

		// PUBLIC MEMBERS

		public bool      HasBackpack => _agent.HasBackpack;
		public int       SlotCount   => LootSettings.Current.GetSlotCount(_agent.HasBackpack);
		public LootStack HealthSlot  => _agent.HealthSlot;
		public LootStack Equipment   => _agent.EquipmentSlot;
		public LootStack Gear        => _agent.GearSlot;
		public int       OpenInventoryCount => _agent.OpenInventoryCount;

		public LootBag OpenStash
		{
			get
			{
				NetworkObject stash = _agent.OpenStashObject;
				return stash != null && stash.IsValid == true ? stash.GetComponent<LootBag>() : null;
			}
		}

		public LootStack GetSlot(int index)
		{
			return index >= 0 && index < LootSettings.MaxInventorySlots ? _agent.InventorySlots[index] : default;
		}

		/// <summary>Healing items of this kind carried, in the Health slot and the inventory.</summary>
		public int CountHeal(EHealItem item)
		{
			int count = 0;

			LootStack health = _agent.HealthSlot;
			if (health.Kind == EItemKind.Heal && health.Index == (byte)item)
			{
				count += health.Count;
			}

			for (int i = 0; i < LootSettings.MaxInventorySlots; ++i)
			{
				LootStack stack = _agent.InventorySlots[i];
				if (stack.Kind == EItemKind.Heal && stack.Index == (byte)item)
				{
					count += stack.Count;
				}
			}

			return count;
		}

		/// <summary>
		/// A heal finished (server and the predicting client): one item from the Health slot. When the stack runs out the next healing
		/// stack in the inventory moves in.
		/// </summary>
		public void UseHealthSlotItem()
		{
			LootStack health = _agent.HealthSlot;
			if (health.Kind != EItemKind.Heal || health.Count == 0)
				return;

			health.Count -= 1;
			_agent.HealthSlot = health.Count > 0 ? health : default;

			if (health.Count == 0)
			{
				RefillHealthSlot();
			}
		}

		// SPAWN

		/// <summary>
		/// Server, at spawn: players start empty-handed (LootSettings); dummies (and players with the old kit) get HealthSettings' healing
		/// items, and dummies the armor tier from LootSettings, so their stash has something in it.
		/// </summary>
		public void InitForSpawn()
		{
			Clear();

			LootSettings settings = LootSettings.Current;
			// Bots (phase 9) start empty-handed like players and loot
			bool         isPlayer = _agent.Object.InputAuthority.IsRealPlayer == true || _agent.BotNumber > 0;

			if (isPlayer == true && settings.PlayersSpawnEmptyHanded == true)
				return;

			GiveHealKit();

			if (isPlayer == false && settings.DummyArmorTier > 0)
			{
				int tier = Mathf.Clamp(settings.DummyArmorTier, 1, 3);
				Health.SetArmor(tier, HealthSettings.Current.GetArmorTier(tier).Durability);
			}

			// Phase 5b: dummies also carry a grenade and a gear item, so their stash has them
			if (isPlayer == false)
			{
				EquipmentSettings equipment = EquipmentSettings.Current;
				_agent.EquipmentSlot = LootStack.EquipmentItem(equipment.DummyEquipment);
				_agent.GearSlot      = LootStack.Gear(equipment.DummyGear);
			}
		}

		/// <summary>
		/// Server (phase 5b): one use of the equipped equipment. A grenade leaves the slot (the next grenade of the same kind in the
		/// inventory moves in); the grapple loses a pull and goes when it has none left.
		/// </summary>
		public void UseEquipment()
		{
			LootStack slot = _agent.EquipmentSlot;
			if (slot.Kind != EItemKind.Equipment || slot.IsEmpty == true)
				return;

			if (slot.Equipment == EEquipment.Grapple)
			{
				slot.Charges -= 1;
				_agent.EquipmentSlot = slot.Charges > 0 ? slot : default;
				LootDebug.Log(Runner, $"EQUIP {_agent.name} grapple used, {slot.Charges} pulls left");
				return;
			}

			_agent.EquipmentSlot = default;

			for (int i = 0; i < LootSettings.MaxInventorySlots; ++i)
			{
				LootStack stack = _agent.InventorySlots[i];
				if (stack.IsSameItem(slot) == true && stack.IsEmpty == false)
				{
					_agent.EquipmentSlot = stack;
					_agent.InventorySlots.Set(i, default);
					LootDebug.Log(Runner, $"EQUIP {_agent.name} next {stack} moved into the Equipment slot");
					break;
				}
			}
		}

		/// <summary>Server: HealthSettings.SpawnHealItems, first kind in the Health slot, the rest in the inventory.</summary>
		public void GiveHealKit()
		{
			// Take out the healing stacks first, so a refill doesn't pile up
			_agent.HealthSlot = default;
			for (int i = 0; i < LootSettings.MaxInventorySlots; ++i)
			{
				if (_agent.InventorySlots[i].Kind == EItemKind.Heal)
				{
					_agent.InventorySlots.Set(i, default);
				}
			}

			int[] counts = HealthSettings.Current.SpawnHealItems;
			for (int i = 0; counts != null && i < counts.Length && i < HealthSettings.HealItemCount; ++i)
			{
				if (counts[i] > 0)
				{
					Give(LootStack.Heal((EHealItem)i, counts[i]), false, _displaced, out _);
				}
			}

			_displaced.Clear();
		}

		public void Clear()
		{
			for (int i = 0; i < LootSettings.MaxInventorySlots; ++i)
			{
				_agent.InventorySlots.Set(i, default);
			}

			_agent.HealthSlot      = default;
			_agent.EquipmentSlot   = default;
			_agent.GearSlot        = default;
			_agent.GearOn          = false;
			_agent.HasBackpack     = false;
			_agent.OpenStashObject = null;
		}

		// PICKUPS

		/// <summary>Server: F on an item on the ground. Equips or swaps it; whatever it replaces drops where you stand.</summary>
		public bool Equip(LootItem item)
		{
			return Take(item, true);
		}

		/// <summary>Server: G on an item on the ground. Into an empty equipped slot or the inventory; opens the inventory if there's no room.</summary>
		public bool Store(LootItem item)
		{
			return Take(item, false);
		}

		/// <summary>Server: F on a loot bag opens it in the STASH panel.</summary>
		public void OpenStashBag(LootBag bag)
		{
			if (bag == null || bag.Object == null)
				return;

			_agent.OpenStashObject = bag.Object;
			LootDebug.Log(Runner, $"STASH {_agent.name} opened {bag.OwnerName}'s stash ({bag.ItemCount} items)");
		}

		/// <summary>Server, every tick: the stash closes when you walk away from it, die or go down.</summary>
		public void OnFixedUpdate()
		{
			NetworkObject stash = _agent.OpenStashObject;
			if (stash == null)
				return;

			float reach = LootSettings.Current.PickupReach + 1.0f;
			if (stash.IsValid == false || Health.IsUp == false || (stash.transform.position - _agent.transform.position).sqrMagnitude > reach * reach)
			{
				_agent.OpenStashObject = null;
			}
		}

		/// <summary>
		/// Server: gives a stack to this player. swap = F (the equipped item it replaces goes into displaced), else G (an empty equipped
		/// slot, then the inventory). Returns what's left of the stack (empty when all of it was taken); result says why not.
		/// </summary>
		public LootStack Give(LootStack stack, bool swap, List<LootStack> displaced, out string result)
		{
			result = null;

			switch (stack.Kind)
			{
				case EItemKind.Ammo:       return GiveAmmo(stack, out result);
				case EItemKind.Gun:        return GiveGun(stack, swap, displaced, out result);
				case EItemKind.Attachment: return GiveAttachment(stack, swap, displaced, out result);
				case EItemKind.Heal:       return GiveHeal(stack, swap, displaced, out result);
				case EItemKind.Armor:      return GiveArmor(stack, swap, displaced, out result);
				case EItemKind.Backpack:   return GiveBackpack(stack, swap, out result);
				case EItemKind.Equipment:  return GiveEquipped(stack, swap, displaced, false, out result);
				case EItemKind.Gear:       return GiveEquipped(stack, swap, displaced, true,  out result);
				default:
					result = "nothing to take";
					return stack;
			}
		}

		// INVENTORY SCREEN

		/// <summary>Server: a click in the inventory screen.</summary>
		public void HandleAction(EInventoryAction action, int slot, int arg)
		{
			if (_agent.Object == null || _agent.HasStateAuthority == false || Health.IsAlive == false)
				return;

			switch (action)
			{
				case EInventoryAction.Use:           Use(slot, arg);       break;
				case EInventoryAction.Drop:          Drop(slot);           break;
				case EInventoryAction.TakeFromStash: TakeFromStash(slot);  break;
				case EInventoryAction.CloseStash:    _agent.OpenStashObject = null; break;
				case EInventoryAction.Move:          Move(slot, arg);      break;
			}
		}

		/// <summary>Server (phase 11a): one inventory slot dragged onto another. Same item: tops the target up to a full stack; else they swap.</summary>
		private void Move(int from, int to)
		{
			int count = SlotCount;
			if (from < 0 || from >= count || to < 0 || to >= count || from == to)
				return;

			LootStack source = _agent.InventorySlots[from];
			LootStack target = _agent.InventorySlots[to];
			if (source.IsEmpty == true)
				return;

			if (target.IsEmpty == false && target.IsSameItem(source) == true)
			{
				int room  = ItemCatalog.GetStackSize(target) - target.Count;
				int moved = Mathf.Min(room, source.Count);
				if (moved > 0)
				{
					_agent.InventorySlots.Set(to, target.WithCount(target.Count + moved));
					_agent.InventorySlots.Set(from, source.Count - moved > 0 ? source.WithCount(source.Count - moved) : default);
					LootDebug.Log(Runner, $"MOVE {_agent.name} {moved} {ItemCatalog.GetName(source)} from slot {from} onto slot {to}");
					return;
				}
			}

			_agent.InventorySlots.Set(to, source);
			_agent.InventorySlots.Set(from, target);
			LootDebug.Log(Runner, $"MOVE {_agent.name} slot {from} ({source}) <-> slot {to} ({target})");
		}

		// DEATH

		/// <summary>Server, when the agent dies: everything it carries goes into a loot bag at the body.</summary>
		public LootBag DropAllIntoBag()
		{
			_removed.Clear();

			for (int slot = Weapons.FIRST_PRIMARY_SLOT; slot <= Weapons.LAST_PRIMARY_SLOT; ++slot)
			{
				AddIfAny(_removed, Weapons.TakeGun(slot));
			}

			for (int i = 0; i < GunData.CaliberCount; ++i)
			{
				int rounds = Weapons.GetAmmo((ECaliber)i);
				if (rounds > 0)
				{
					Weapons.TakeAmmo((ECaliber)i, rounds);
					_removed.Add(LootStack.Ammo((ECaliber)i, rounds));
				}
			}

			if (Health.ArmorTier > 0)
			{
				_removed.Add(LootStack.ArmorVest(Health.ArmorTier, Health.ArmorLeft));
				Health.SetArmor(0, 0.0f);
			}

			AddIfAny(_removed, _agent.HealthSlot);
			for (int i = 0; i < LootSettings.MaxInventorySlots; ++i)
			{
				AddIfAny(_removed, _agent.InventorySlots[i]);
			}

			if (_agent.HasBackpack == true)
			{
				_removed.Add(LootStack.Backpack());
			}

			AddIfAny(_removed, _agent.EquipmentSlot);
			AddIfAny(_removed, _agent.GearSlot);

			Clear();

			if (_removed.Count == 0)
				return null;

			LootBag bag = LootSpawner.SpawnBag(Runner, _agent.transform.position, _agent.name, _removed);
			LootDebug.Log(Runner, $"STASH {_agent.name} died: {_removed.Count} items in a bag ({string.Join(", ", _removed)})");
			_removed.Clear();
			return bag;
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>Test tool (server): a full stack of this healing item in the Health slot (what was there goes into the inventory if it fits).</summary>
		public void DebugSetHealthSlot(EHealItem item)
		{
			LootStack old = _agent.HealthSlot;
			_agent.HealthSlot = LootStack.Heal(item, HealthSettings.Current.GetHealItem(item).Stack);

			if (old.IsEmpty == false && old.Index != (byte)item)
			{
				StoreInSlots(old);
			}
		}

		/// <summary>Test tool (server): puts a stack straight into the inventory, as G would.</summary>
		public LootStack DebugGive(LootStack stack, bool swap)
		{
			LootStack left = Give(stack, swap, _displaced, out string result);
			DropDisplaced();
			return left;
		}
#endif

		// PRIVATE METHODS

		private bool Take(LootItem item, bool swap)
		{
			if (item == null || item.Object == null || item.Object.IsValid == false || item.Stack.IsEmpty == true)
				return false;

			LootStack stack = item.Stack;

			_displaced.Clear();
			LootStack left = Give(stack, swap, _displaced, out string result);

			if (left.Count == stack.Count && left.IsEmpty == false)
			{
				LootDebug.Log(Runner, $"REFUSED {_agent.name} {(swap ? "F" : "G")} {stack}: {result}");

				if (swap == false && result == "no room")
				{
					_agent.OpenInventoryCount++;
				}

				return false;
			}

			item.SetStack(left);

			LootDebug.Log(Runner, $"{(swap ? "EQUIP" : "PICKUP")} {_agent.name} {(swap ? "F" : "G")} {stack}{(left.IsEmpty == false ? $", {left.Count} left on the ground" : "")}{(_displaced.Count > 0 ? $", dropped {string.Join(", ", _displaced)}" : "")}");

			DropDisplaced();
			return true;
		}

		private LootStack GiveAmmo(LootStack stack, out string result)
		{
			int added = Weapons.AddAmmo((ECaliber)stack.Index, stack.Count);
			result = added == 0 ? "ammo full" : null;
			return stack.WithCount(stack.Count - added);
		}

		private LootStack GiveGun(LootStack stack, bool swap, List<LootStack> displaced, out string result)
		{
			result = null;

			int slot = Weapons.GetEmptyPrimarySlot();
			if (slot < 0)
			{
				if (swap == false)
				{
					result = "both gun slots full";
					return stack;
				}

				// Both full: replace the gun in hand
				slot = Weapons.GetSwapSlot();
				AddIfAny(displaced, Weapons.TakeGun(slot));
			}

			if (Weapons.AddGunFromLoot(stack, slot, true) == false)
			{
				result = "couldn't spawn the gun";
				return stack;
			}

			return default;
		}

		private LootStack GiveAttachment(LootStack stack, bool swap, List<LootStack> displaced, out string result)
		{
			result = null;

			if (swap == true)
			{
				// F: onto the gun in hand, in its slot; the part that was there drops
				if (Attach(stack, Weapons.CurrentWeaponSlot, displaced, out result) == true)
					return default;

				return stack;
			}

			if (StoreInSlots(stack) == false)
			{
				result = "no room";
				return stack;
			}

			return default;
		}

		private LootStack GiveHeal(LootStack stack, bool swap, List<LootStack> displaced, out string result)
		{
			result = null;

			LootStack health = _agent.HealthSlot;
			int       size   = ItemCatalog.GetStackSize(stack);

			// Into the Health slot: empty, or the same item with room
			if (health.IsEmpty == true || health.IsSameItem(stack) == true)
			{
				int room  = size - (health.IsEmpty == true ? 0 : health.Count);
				int moved = Mathf.Min(room, stack.Count);
				if (moved > 0)
				{
					_agent.HealthSlot = stack.WithCount((health.IsEmpty == true ? 0 : health.Count) + moved);
					stack = stack.WithCount(stack.Count - moved);
				}

				if (stack.Count == 0)
					return default;
			}
			else if (swap == true)
			{
				// F on a different healing item: it takes the Health slot and the old stack drops
				displaced.Add(health);
				int moved = Mathf.Min(size, stack.Count);
				_agent.HealthSlot = stack.WithCount(moved);
				return stack.WithCount(stack.Count - moved);
			}

			if (swap == true)
			{
				result = "health slot full";
				return stack;
			}

			// G: top up the same item in the inventory, then a new slot
			for (int i = 0; i < SlotCount && stack.Count > 0; ++i)
			{
				LootStack slot = _agent.InventorySlots[i];
				if (slot.IsSameItem(stack) == true && slot.Count < size)
				{
					int moved = Mathf.Min(size - slot.Count, stack.Count);
					_agent.InventorySlots.Set(i, slot.WithCount(slot.Count + moved));
					stack = stack.WithCount(stack.Count - moved);
				}
			}

			while (stack.Count > 0)
			{
				int moved = Mathf.Min(size, stack.Count);
				if (StoreInSlots(stack.WithCount(moved)) == false)
				{
					result = "no room";
					break;
				}

				stack = stack.WithCount(stack.Count - moved);
			}

			return stack.Count > 0 ? stack : default;
		}

		private LootStack GiveArmor(LootStack stack, bool swap, List<LootStack> displaced, out string result)
		{
			result = null;

			if (Health.ArmorTier == 0)
			{
				Health.SetArmor(stack.Index, stack.Armor);
				return default;
			}

			if (swap == true)
			{
				displaced.Add(LootStack.ArmorVest(Health.ArmorTier, Health.ArmorLeft));
				Health.SetArmor(stack.Index, stack.Armor);
				return default;
			}

			if (StoreInSlots(stack) == false)
			{
				result = "no room";
				return stack;
			}

			return default;
		}

		private LootStack GiveBackpack(LootStack stack, bool swap, out string result)
		{
			result = null;

			if (_agent.HasBackpack == false)
			{
				_agent.HasBackpack = true;
				return default;
			}

			if (swap == true)
			{
				result = "already wearing a backpack";
				return stack;
			}

			if (StoreInSlots(stack) == false)
			{
				result = "no room";
				return stack;
			}

			return default;
		}

		/// <summary>
		/// Phase 5b: equipment or a gear item, one per slot (Logan, 5 Oct). An empty Equipment / Gear slot takes it; F swaps it with what's
		/// there (that drops); G puts it in the inventory. A gear item that leaves the slot switches off with its seconds left.
		/// </summary>
		private LootStack GiveEquipped(LootStack stack, bool swap, List<LootStack> displaced, bool gear, out string result)
		{
			result = null;

			LootStack equipped = gear == true ? _agent.GearSlot : _agent.EquipmentSlot;

			// Mid-throw or mid-grapple the Equipment slot stays as it is
			if (gear == false && swap == true && equipped.IsEmpty == false && _agent.EquipmentActions.IsBusy == true)
			{
				result = "equipment in use";
				return stack;
			}

			if (equipped.IsEmpty == true || swap == true)
			{
				if (equipped.IsEmpty == false)
				{
					displaced.Add(equipped);
				}

				SetEquippedSlot(gear, stack);
				return default;
			}

			if (StoreInSlots(stack) == false)
			{
				result = "no room";
				return stack;
			}

			return default;
		}

		private void SetEquippedSlot(bool gear, LootStack stack)
		{
			if (gear == true)
			{
				if (_agent.GearOn == true)
				{
					_agent.Gear.SwitchOff("gear item swapped");
				}

				_agent.GearSlot = stack;
			}
			else
			{
				_agent.EquipmentSlot = stack;
			}
		}

		/// <summary>Puts an attachment on the gun in a gun slot; the part already in that slot goes into displaced.</summary>
		private bool Attach(LootStack stack, int weaponSlot, List<LootStack> displaced, out string result)
		{
			AttachmentData attachment = ItemCatalog.GetAttachment(stack);
			BulletWeapon   gun        = Weapons.GetWeapon(weaponSlot) as BulletWeapon;

			if (attachment == null || gun == null || gun.GunData == null)
			{
				result = "no gun in hand";
				return false;
			}

			if (gun.GunData.Allows(attachment) == false)
			{
				result = $"{gun.GunData.GunName} doesn't take {attachment.AttachmentName}";
				return false;
			}

			int oldID = gun.GetAttachmentID(attachment.Slot);
			if (gun.SetAttachment(attachment.Slot, attachment, out result) == false)
				return false;

			if (oldID > 0)
			{
				displaced.Add(LootStack.Attachment(oldID));
			}

			if (weaponSlot == Weapons.CurrentWeaponSlot)
			{
				Weapons.StartRaise(LootSettings.Current.AttachmentFitTime);
			}

			result = null;
			LootDebug.Log(Runner, $"ATTACH {_agent.name} {attachment.AttachmentName} on {gun.GunData.GunName}{(oldID > 0 ? $", replaced {GunplaySettings.Current.GetAttachment(oldID).AttachmentName}" : "")}");
			return true;
		}

		/// <summary>Into the first empty inventory slot. False when there's no room.</summary>
		private bool StoreInSlots(LootStack stack)
		{
			for (int i = 0; i < SlotCount; ++i)
			{
				if (_agent.InventorySlots[i].IsEmpty == true)
				{
					_agent.InventorySlots.Set(i, stack);
					return true;
				}
			}

			return false;
		}

		/// <summary>The Health slot is empty: the next healing stack in the inventory moves in.</summary>
		private void RefillHealthSlot()
		{
			if (_agent.HealthSlot.IsEmpty == false)
				return;

			for (int i = 0; i < LootSettings.MaxInventorySlots; ++i)
			{
				LootStack stack = _agent.InventorySlots[i];
				if (stack.Kind == EItemKind.Heal && stack.Count > 0)
				{
					_agent.HealthSlot = stack;
					_agent.InventorySlots.Set(i, default);
					return;
				}
			}
		}

		private void Use(int slot, int arg)
		{
			_displaced.Clear();

			if (slot >= 0 && slot < SlotCount)
			{
				UseInventorySlot(slot, arg);
			}
			else if (slot >= InventorySlot.AttachmentFirst && slot < InventorySlot.AttachmentFirst + 2 * AttachmentData.SlotCount)
			{
				// Left click on a part on a gun: back into the inventory
				int             weaponSlot = Weapons.FIRST_PRIMARY_SLOT + (slot - InventorySlot.AttachmentFirst) / AttachmentData.SlotCount;
				EAttachmentSlot part       = (EAttachmentSlot)((slot - InventorySlot.AttachmentFirst) % AttachmentData.SlotCount);
				BulletWeapon    gun        = Weapons.GetWeapon(weaponSlot) as BulletWeapon;
				int             id         = gun != null ? gun.GetAttachmentID(part) : 0;

				if (id > 0 && FindEmptySlot() >= 0 && gun.SetAttachment(part, null, out _) == true)
				{
					StoreInSlots(LootStack.Attachment(id));
					LootDebug.Log(Runner, $"DETACH {_agent.name} {GunplaySettings.Current.GetAttachment(id).AttachmentName} from {gun.GunData.GunName} into the inventory");
				}
			}

			DropDisplaced();
		}

		private void UseInventorySlot(int slot, int arg)
		{
			LootStack stack = _agent.InventorySlots[slot];
			if (stack.IsEmpty == true)
				return;

			switch (stack.Kind)
			{
				case EItemKind.Heal:
				{
					// Equip it: it swaps places with the Health stack (or tops it up when it's the same item)
					LootStack health = _agent.HealthSlot;
					if (health.IsSameItem(stack) == true)
					{
						int size  = ItemCatalog.GetStackSize(stack);
						int moved = Mathf.Min(size - health.Count, stack.Count);
						_agent.HealthSlot = health.WithCount(health.Count + moved);
						_agent.InventorySlots.Set(slot, stack.Count - moved > 0 ? stack.WithCount(stack.Count - moved) : default);
					}
					else
					{
						_agent.HealthSlot = stack;
						_agent.InventorySlots.Set(slot, health.IsEmpty == true ? default : health);
					}

					LootDebug.Log(Runner, $"EQUIP {_agent.name} {stack} into the Health slot");
					break;
				}
				case EItemKind.Armor:
				{
					LootStack worn = Health.ArmorTier > 0 ? LootStack.ArmorVest(Health.ArmorTier, Health.ArmorLeft) : default;
					Health.SetArmor(stack.Index, stack.Armor);
					_agent.InventorySlots.Set(slot, worn);
					LootDebug.Log(Runner, $"EQUIP {_agent.name} {stack}{(worn.IsEmpty == false ? $", {worn} into the inventory" : "")}");
					break;
				}
				case EItemKind.Attachment:
				{
					int weaponSlot = arg >= Weapons.FIRST_PRIMARY_SLOT && arg <= Weapons.LAST_PRIMARY_SLOT ? arg : Weapons.CurrentWeaponSlot;
					if (Attach(stack, weaponSlot, _displaced, out string result) == true)
					{
						// The part it replaced takes its place in the inventory
						_agent.InventorySlots.Set(slot, _displaced.Count > 0 ? _displaced[0] : default);
						_displaced.Clear();
					}
					else
					{
						LootDebug.Log(Runner, $"REFUSED {_agent.name} attach {stack}: {result}");
					}

					break;
				}
				case EItemKind.Backpack:
				{
					if (_agent.HasBackpack == false)
					{
						_agent.HasBackpack = true;
						_agent.InventorySlots.Set(slot, default);
						LootDebug.Log(Runner, $"EQUIP {_agent.name} backpack");
					}

					break;
				}
				case EItemKind.Equipment:
				case EItemKind.Gear:
				{
					// Equip it: it swaps places with what's in the Equipment / Gear slot (not mid-throw)
					bool gear = stack.Kind == EItemKind.Gear;
					if (gear == false && _agent.EquipmentActions.IsBusy == true)
					{
						LootDebug.Log(Runner, $"REFUSED {_agent.name} equip {stack}: equipment in use");
						break;
					}

					LootStack equipped = gear == true ? _agent.GearSlot : _agent.EquipmentSlot;
					SetEquippedSlot(gear, stack);
					_agent.InventorySlots.Set(slot, equipped);
					LootDebug.Log(Runner, $"EQUIP {_agent.name} {stack} into the {(gear ? "Gear" : "Equipment")} slot{(equipped.IsEmpty == false ? $", {equipped} into the inventory" : "")}");
					break;
				}
			}
		}

		/// <summary>Right click: drops the item at your feet, or into the open stash.</summary>
		private void Drop(int slot)
		{
			_removed.Clear();
			Remove(slot, _removed);

			if (_removed.Count == 0)
				return;

			LootBag stash = OpenStash;

			for (int i = 0; i < _removed.Count; ++i)
			{
				if (stash != null && stash.AddItem(_removed[i]) == true)
				{
					LootDebug.Log(Runner, $"STASH {_agent.name} put {_removed[i]} into {stash.OwnerName}'s stash");
					continue;
				}

				LootSpawner.DropNear(_agent, _removed[i]);
				LootDebug.Log(Runner, $"DROP {_agent.name} {_removed[i]}");
			}

			_removed.Clear();
		}

		/// <summary>Takes whatever is in a slot out of the inventory (one ammo box from a pool; a backpack also loses the slots past 5).</summary>
		private void Remove(int slot, List<LootStack> removed)
		{
			if (slot >= 0 && slot < LootSettings.MaxInventorySlots)
			{
				AddIfAny(removed, _agent.InventorySlots[slot]);
				_agent.InventorySlots.Set(slot, default);
				return;
			}

			if (slot == InventorySlot.Health)
			{
				AddIfAny(removed, _agent.HealthSlot);
				_agent.HealthSlot = default;
				RefillHealthSlot();
			}
			else if (slot == InventorySlot.Armor)
			{
				if (Health.ArmorTier > 0)
				{
					removed.Add(LootStack.ArmorVest(Health.ArmorTier, Health.ArmorLeft));
					Health.SetArmor(0, 0.0f);
				}
			}
			else if (slot == InventorySlot.Backpack)
			{
				if (_agent.HasBackpack == true)
				{
					removed.Add(LootStack.Backpack());
					_agent.HasBackpack = false;

					// The slots it gave go with it
					for (int i = SlotCount; i < LootSettings.MaxInventorySlots; ++i)
					{
						AddIfAny(removed, _agent.InventorySlots[i]);
						_agent.InventorySlots.Set(i, default);
					}
				}
			}
			else if (slot == InventorySlot.Equipment)
			{
				if (_agent.EquipmentActions.IsBusy == false)
				{
					AddIfAny(removed, _agent.EquipmentSlot);
					_agent.EquipmentSlot = default;
				}
			}
			else if (slot == InventorySlot.Gear)
			{
				AddIfAny(removed, _agent.GearSlot);
				SetEquippedSlot(true, default);
			}
			else if (slot >= InventorySlot.GunFirst && slot < InventorySlot.GunFirst + 2)
			{
				AddIfAny(removed, Weapons.TakeGun(Weapons.FIRST_PRIMARY_SLOT + slot - InventorySlot.GunFirst));
			}
			else if (slot >= InventorySlot.AttachmentFirst && slot < InventorySlot.AttachmentFirst + 2 * AttachmentData.SlotCount)
			{
				int             weaponSlot = Weapons.FIRST_PRIMARY_SLOT + (slot - InventorySlot.AttachmentFirst) / AttachmentData.SlotCount;
				EAttachmentSlot part       = (EAttachmentSlot)((slot - InventorySlot.AttachmentFirst) % AttachmentData.SlotCount);
				BulletWeapon    gun        = Weapons.GetWeapon(weaponSlot) as BulletWeapon;
				int             id         = gun != null ? gun.GetAttachmentID(part) : 0;

				if (id > 0 && gun.SetAttachment(part, null, out _) == true)
				{
					removed.Add(LootStack.Attachment(id));
				}
			}
			else if (slot >= InventorySlot.AmmoFirst && slot < InventorySlot.AmmoFirst + GunData.CaliberCount)
			{
				ECaliber caliber = (ECaliber)(slot - InventorySlot.AmmoFirst);
				int      rounds  = Weapons.TakeAmmo(caliber, LootSettings.Current.GetAmmoBoxRounds(caliber));
				if (rounds > 0)
				{
					removed.Add(LootStack.Ammo(caliber, rounds));
				}
			}
		}

		/// <summary>Left click on a stash item: taken with G's rules (guns with F's, so a full pair swaps the gun in hand into the bag).</summary>
		private void TakeFromStash(int index)
		{
			LootBag stash = OpenStash;
			if (stash == null)
				return;

			LootStack stack = stash.GetItem(index);
			if (stack.IsEmpty == true)
				return;

			_displaced.Clear();
			LootStack left = Give(stack, stack.Kind == EItemKind.Gun, _displaced, out string result);

			if (left.Count == stack.Count && left.IsEmpty == false)
			{
				LootDebug.Log(Runner, $"REFUSED {_agent.name} take {stack} from the stash: {result}");
				return;
			}

			LootDebug.Log(Runner, $"STASH {_agent.name} took {stack}{(left.IsEmpty == false ? $", {left.Count} left" : "")}{(_displaced.Count > 0 ? $", swapped in {string.Join(", ", _displaced)}" : "")}");

			// What it replaced goes into the bag in its place
			if (left.IsEmpty == true && _displaced.Count > 0)
			{
				left = _displaced[0];
				_displaced.RemoveAt(0);
			}

			for (int i = _displaced.Count - 1; i >= 0; --i)
			{
				if (stash.AddItem(_displaced[i]) == true)
				{
					_displaced.RemoveAt(i);
				}
			}

			// Last: an emptied bag despawns
			stash.SetItem(index, left);
			DropDisplaced();
		}

		private int FindEmptySlot()
		{
			for (int i = 0; i < SlotCount; ++i)
			{
				if (_agent.InventorySlots[i].IsEmpty == true)
					return i;
			}

			return -1;
		}

		private void DropDisplaced()
		{
			for (int i = 0; i < _displaced.Count; ++i)
			{
				LootSpawner.DropNear(_agent, _displaced[i]);
			}

			_displaced.Clear();
		}

		private static void AddIfAny(List<LootStack> list, LootStack stack)
		{
			if (stack.IsEmpty == false)
			{
				list.Add(stack);
			}
		}
	}
}
