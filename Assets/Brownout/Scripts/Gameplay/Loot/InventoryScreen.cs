namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.InputSystem;
	using Fusion;

	/// <summary>
	/// The Tab inventory's state (phase 5a; drawn by the HUD in uGUI since phase 6b, UI/Brownout/BrownoutHud.Inventory.cs): open or closed,
	/// the weapon tab, and the clicks, which go to the server (Agent.RequestInventoryAction). The game keeps running while it's open: the
	/// player can move, but the mouse drives a cursor (AgentInput drops look, fire and aim). Left click equips, attaches, detaches or takes
	/// from the stash; right click drops (into the stash while one is open). Added to every agent at runtime; only the local player's opens.
	/// </summary>
	public sealed class InventoryScreen : MonoBehaviour
	{
		// PUBLIC MEMBERS

		public bool IsOpen => _open;

		/// <summary>Which gun's attachments the screen shows and attachments go on (1 or 2).</summary>
		public int  WeaponTab => _weaponTab;

		/// <summary>The stash panel's scroll, in rows (the view keeps it in range).</summary>
		public int  StashScroll { get; set; }

		// PRIVATE MEMBERS

		private Agent          _agent;
		private bool           _open;
		private SceneContext   _cursorContext;
		private NetworkId      _trackedAgent;
		private int            _seenOpenCount;
		private NetworkObject  _seenStash;
		private int            _weaponTab = 1;

		// PUBLIC METHODS

		public void SetOpen(bool open)
		{
			if (_open == open)
				return;

			_open = open;

			SceneContext context = _agent != null ? _agent.Context : null;
			if (open == true)
			{
				_cursorContext = context;
				_weaponTab     = _agent.Weapons.CurrentWeaponSlot == Weapons.LAST_PRIMARY_SLOT ? Weapons.LAST_PRIMARY_SLOT : Weapons.FIRST_PRIMARY_SLOT;
			}
			else if (_agent != null && _agent.Object != null && _agent.Object.IsValid == true)
			{
				// Always (phase 6b): the client's copy of OpenStashObject can lag behind the server's
				_agent.RequestInventoryAction(EInventoryAction.CloseStash, 0, 0);
			}

			if (_cursorContext != null && _cursorContext.Input != null)
			{
				// Phase 6b: the inventory and the full map don't stay open together
				if (open == true)
				{
					_cursorContext.Input.RequestCursorVisibility(false, ECursorStateSource.Map);
				}

				_cursorContext.Input.RequestCursorVisibility(open, ECursorStateSource.Inventory);
			}

			if (open == false)
			{
				_cursorContext = null;
			}
		}

		public void SetWeaponTab(int weaponSlot)
		{
			_weaponTab = Mathf.Clamp(weaponSlot, Weapons.FIRST_PRIMARY_SLOT, Weapons.LAST_PRIMARY_SLOT);
		}

		/// <summary>
		/// A click on a slot (InventorySlot numbers, or a stash index). Left click: Use with the weapon tab (TakeFromStash in the stash);
		/// right click: Drop (nothing in the stash). Empty slots do nothing.
		/// </summary>
		public void Click(int slot, LootStack stack, bool rightButton, bool inStash)
		{
			if (_open == false || stack.IsEmpty == true || _agent == null)
				return;

			if (inStash == true)
			{
				if (rightButton == false)
				{
					_agent.RequestInventoryAction(EInventoryAction.TakeFromStash, slot, 0);
				}
				return;
			}

			if (rightButton == false)
			{
				_agent.RequestInventoryAction(EInventoryAction.Use, slot, _weaponTab);
			}
			else
			{
				_agent.RequestInventoryAction(EInventoryAction.Drop, slot, 0);
			}
		}

		/// <summary>Phase 11a: a drag and drop in the screen, sent as one action (see BrownoutHud.Inventory's DropOn).</summary>
		public void Request(EInventoryAction action, int slot, int arg)
		{
			if (_open == false || _agent == null)
				return;

			_agent.RequestInventoryAction(action, slot, arg);
		}

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_agent = GetComponent<Agent>();
		}

		private void OnDisable()
		{
			SetOpen(false);
		}

		private void Update()
		{
			if (_agent == null || _agent.Object == null || _agent.Object.IsValid == false || _agent.HasInputAuthority == false)
			{
				SetOpen(false);
				return;
			}

			SceneContext context = _agent.Context;
			if (context == null || context.HasInput == false || _agent.Health.IsAlive == false)
			{
				SetOpen(false);
				return;
			}

			Inventory inventory = _agent.Inventory;

			// A new agent (respawn): don't open for requests from an old life
			if (_trackedAgent != _agent.Object.Id)
			{
				_trackedAgent  = _agent.Object.Id;
				_seenOpenCount = inventory.OpenInventoryCount;
				_seenStash     = _agent.OpenStashObject;
			}

			// Closed from outside (Esc, or the full map opening): follow the cursor state
			if (_open == true && context.Input.IsInventoryOpen == false)
			{
				SetOpen(false);
			}

			// The game's own menus take the cursor first
			if (context.Input.IsCursorVisibleOutsideInventory == true && _open == false)
				return;

			if (KeyBindings.WasPressedThisFrame(EBinding.Inventory) == true)
			{
				SetOpen(_open == false);
			}

			// G found no room: the server asks for the inventory
			if (inventory.OpenInventoryCount != _seenOpenCount)
			{
				_seenOpenCount = inventory.OpenInventoryCount;
				SetOpen(true);
			}

			// F on a loot bag: the stash opens with the inventory
			NetworkObject stash = _agent.OpenStashObject;
			if (stash != _seenStash)
			{
				_seenStash  = stash;
				StashScroll = 0;

				if (stash != null)
				{
					SetOpen(true);
				}
			}
		}
	}
}
