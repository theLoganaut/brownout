namespace TPSBR
{
	using Fusion;
	using UnityEngine;

	/// <summary>
	/// The inventory's networked state (phase 5a), on the agent so the agent prefabs stay untouched. Only the server changes it
	/// (Inventory); the client asks with RequestInventoryAction (a click in the inventory screen) and sees the result when it arrives.
	/// Guns and ammo stay in Weapons, armor in Health.
	/// </summary>
	public sealed partial class Agent
	{
		// PUBLIC MEMBERS

		public Inventory Inventory => _inventory;

		/// <summary>Inventory slots (5, or 10 with a backpack). Set only by Inventory on the server.</summary>
		[Networked, Capacity(LootSettings.MaxInventorySlots), HideInInspector]
		public NetworkArray<LootStack> InventorySlots { get; }
		/// <summary>The healing stack Q uses.</summary>
		[Networked, HideInInspector]
		public LootStack     HealthSlot          { get; set; }
		/// <summary>Grenades and the grapple (5b).</summary>
		[Networked, HideInInspector]
		public LootStack     EquipmentSlot       { get; set; }
		/// <summary>The toggled gear item (5b).</summary>
		[Networked, HideInInspector]
		public LootStack     GearSlot            { get; set; }
		[Networked, HideInInspector]
		public NetworkBool   HasBackpack         { get; set; }
		/// <summary>The stash this player has open (F on a loot bag), or none.</summary>
		[Networked, HideInInspector]
		public NetworkObject OpenStashObject     { get; set; }
		/// <summary>Counts up when G found no room, so the client opens its inventory.</summary>
		[Networked, HideInInspector]
		public byte          OpenInventoryCount  { get; set; }

		// PRIVATE MEMBERS

		private Inventory _inventory;

		// PUBLIC METHODS

		/// <summary>A click in the inventory screen: done on the server (directly on the host, by RPC from a client).</summary>
		public void RequestInventoryAction(EInventoryAction action, int slot, int arg)
		{
			if (Object == null || Object.IsValid == false)
				return;

			if (HasStateAuthority == true)
			{
				_inventory.HandleAction(action, slot, arg);
			}
			else if (HasInputAuthority == true)
			{
				RPC_InventoryAction((byte)action, (byte)slot, (byte)arg);
			}
		}

		// RPCs

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
		private void RPC_InventoryAction(byte action, byte slot, byte arg)
		{
			_inventory.HandleAction((EInventoryAction)action, slot, arg);
		}
	}
}
