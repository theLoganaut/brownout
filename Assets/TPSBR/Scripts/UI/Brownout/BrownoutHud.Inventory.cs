namespace TPSBR.UI
{
	using System.Collections.Generic;
	using TMPro;
	using UnityEngine;
	using UnityEngine.InputSystem;
	using UnityEngine.UI;

	/// <summary>
	/// The Tab inventory and the stash (phase 6b), in uGUI on the HUD's canvas, laid out like the Blackout reference: AMMO top-left (our six
	/// calibers, empty ones dimmed), WEAPON 1 / 2 tabs with the gun and its attachment slots top-right (only the slots the gun takes are
	/// lit), Health / Armor / Gear bottom-left, the 5 (10) slots bottom-center, Storage and Equipment bottom-right, and the STASH panel
	/// across the top center (8 per row, scrolls). Hover draws corner brackets and the tooltip. InventoryScreen (on the agent) keeps the
	/// state and sends the clicks to the server, with the same rules as phase 5a.
	/// </summary>
	public sealed partial class BrownoutHud
	{
		// PRIVATE MEMBERS

		private sealed class Cell
		{
			public string          Id;
			public RectTransform   Root;
			public Image           Back;
			public Image           Icon;
			public TextMeshProUGUI Name;
			public TextMeshProUGUI Count;
			public Image           CountBack;
			public TextMeshProUGUI Caption;
			public Image           Frame;

			public int             Slot;
			public bool            InStash;
			public bool            Equipped;
			public bool            Lit;
			public LootStack       Stack;
		}

		// Phase 11a (Logan, 10 Oct): a little bigger than the reference's 92 / 120 / 88
		private const float CELL       = 100.0f;
		private const float BIG_CELL   = 128.0f;
		private const float STASH_CELL = 96.0f;
		private const float CELL_PITCH = 124.0f;
		private const float BIG_PITCH  = 148.0f;

		private RectTransform   _inventory;
		private readonly List<Cell> _cells = new List<Cell>(64);

		private Cell[]          _ammoCells;
		private Cell            _gunCell;
		private TextMeshProUGUI _gunTitle;
		private TextMeshProUGUI _gunInfo;
		private Image[]         _tabBacks;
		private TextMeshProUGUI[] _tabTexts;
		private Cell[]          _attachmentCells;
		private Cell            _healthCell;
		private Cell            _armorCell;
		private Cell            _gearCell;
		private Cell[]          _slotCells;
		private TextMeshProUGUI _slotsLabel;
		private RectTransform   _slotsLabelBox;
		private Cell            _storageCell;
		private Cell            _equipmentCell;

		private RectTransform   _stash;
		private TextMeshProUGUI _stashTitle;
		private RectTransform   _stashGrid;
		private Cell[]          _stashCells;
		private RectTransform   _stashScrollbar;
		private RectTransform   _stashThumb;
		private readonly List<int> _stashIndices = new List<int>(LootBag.Capacity);

		private RectTransform   _brackets;
		private RectTransform   _tooltip;
		private TextMeshProUGUI _tooltipName;
		private TextMeshProUGUI _tooltipText;
		private TextMeshProUGUI _tooltipHints;

		private Cell            _hovered;
		private readonly Vector3[] _corners = new Vector3[4];

		// Phase 11a: drag and drop. A left press on an item arms a drag; moving past DRAG_PIXELS picks it up (the ghost follows the
		// mouse) and the release drops it on whatever is under the mouse. A press and release without moving is the old left click.
		private const float     DRAG_PIXELS = 8.0f;
		private Cell            _pressed;
		private Vector2         _pressPoint;
		private bool            _dragging;
		private Image           _dragGhost;
		private RectTransform   _panels;
		private readonly System.Text.StringBuilder _slotNames = new System.Text.StringBuilder(64);

		// PUBLIC METHODS

		/// <summary>Test hook (phase 6b check): a click on a cell by its id (slot0.., health, armor, gear, storage, equipment, gun, att0.., ammo0.., stash0..).</summary>
		public bool TestClick(string id, bool rightButton)
		{
			InventoryScreen screen = GetInventoryScreen();
			for (int i = 0; i < _cells.Count; ++i)
			{
				Cell cell = _cells[i];
				if (cell.Id == id && cell.Root.gameObject.activeInHierarchy == true && cell.Lit == true && screen != null)
				{
					screen.Click(cell.Slot, cell.Stack, rightButton, cell.InStash);
					return true;
				}
			}

			return false;
		}

		/// <summary>Test hook (phase 11a): drags one cell onto another by id ("tab1" / "tab2" for the weapon tabs, "out" for empty screen).</summary>
		public string TestDrag(string fromId, string toId)
		{
			InventoryScreen screen = GetInventoryScreen();
			Cell from = FindCell(fromId);
			Cell to   = FindCell(toId);
			if (screen == null || from == null || from.Stack.IsEmpty == true)
				return $"no item at {fromId}";

			int tab = toId == "tab1" ? 1 : (toId == "tab2" ? 2 : 0);
			return DropOn(screen, from, to, tab, toId == "out");
		}

		private Cell FindCell(string id)
		{
			for (int i = 0; i < _cells.Count; ++i)
			{
				if (_cells[i].Id == id && _cells[i].Root.gameObject.activeInHierarchy == true)
					return _cells[i];
			}

			return null;
		}

		// PRIVATE METHODS

		private void BuildInventory()
		{
			HudSettings s = _settings;

			// Phase 11a (Logan, 10 Oct): a darker screen behind it; the rest of the HUD hides while it's open (Tick)
			_inventory = HudKit.Rect(transform, "Inventory");
			HudKit.Stretch(_inventory, Vector2.zero, Vector2.one);
			HudKit.Stretch(HudKit.Image(_inventory, "Shade", new Color(0.0f, 0.0f, 0.0f, s.InventoryShade)).rectTransform, Vector2.zero, Vector2.one);
			HudKit.Stretch(HudKit.Image(_inventory, "Dim", new Color(0.0f, 0.0f, 0.0f, 0.35f), HudKit.Vignette).rectTransform, Vector2.zero, Vector2.one);

			// Ammo: top-left, 2 columns, the caliber in the cell's top-left corner
			RectTransform ammo = Group(_inventory, "Ammo", new Vector2(0.0f, 1.0f), new Vector2(52.0f, -96.0f));
			Chip(ammo, "AMMO", Vector2.zero, 100.0f);
			_ammoCells = new Cell[GunData.CaliberCount];
			for (int i = 0; i < _ammoCells.Length; ++i)
			{
				Cell cell = NewCell(ammo, $"ammo{i}", new Vector2((i % 2) * 118.0f, -30.0f - (i / 2) * 116.0f), new Vector2(CELL, CELL), string.Empty, InventorySlot.Ammo((ECaliber)i));
				HudKit.Show(cell.Name, true);
				HudKit.Stretch(cell.Icon.rectTransform, new Vector2(0.12f, 0.1f), new Vector2(0.88f, 0.76f));
				_ammoCells[i] = cell;
			}

			// Weapon: top-right, tabs, the gun, then the attachment slots
			RectTransform weapon = Group(_inventory, "Weapon", new Vector2(1.0f, 1.0f), new Vector2(-470.0f, -96.0f));
			_tabBacks = new Image[2];
			_tabTexts = new TextMeshProUGUI[2];
			for (int i = 0; i < 2; ++i)
			{
				RectTransform tab = HudKit.Rect(weapon, $"Tab{i + 1}");
				Place(tab, new Vector2(i * 226.0f, 0.0f), new Vector2(224.0f, 24.0f));
				_tabBacks[i] = HudKit.Panel(tab, "Back", s.SlotBack);
				_tabTexts[i] = HudKit.Label(tab, "Text", 15.0f, TextAlignmentOptions.Center, s.Text);
				HudKit.Stretch(_tabTexts[i].rectTransform, Vector2.zero, Vector2.one);
				_tabTexts[i].text = $"WEAPON {i + 1}";
			}

			_gunCell = NewCell(weapon, "gun", new Vector2(0.0f, -28.0f), new Vector2(450.0f, 190.0f), string.Empty, InventorySlot.Gun(1));
			HudKit.Stretch(_gunCell.Icon.rectTransform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.76f));
			_gunTitle = HudKit.Label(_gunCell.Root, "Title", 20.0f, TextAlignmentOptions.Center, s.Text);
			HudKit.Stretch(_gunTitle.rectTransform, new Vector2(0.0f, 0.78f), new Vector2(1.0f, 0.96f));
			_gunInfo = HudKit.Text(_gunCell.Root, "Info", 14.0f, TextAlignmentOptions.Center, s.Dim);
			HudKit.Stretch(_gunInfo.rectTransform, new Vector2(0.0f, 0.02f), new Vector2(1.0f, 0.14f));
			HudKit.Show(_gunInfo, false);

			_attachmentCells = new Cell[AttachmentData.SlotCount];
			for (int i = 0; i < _attachmentCells.Length; ++i)
			{
				EAttachmentSlot part = (EAttachmentSlot)i;
				_attachmentCells[i] = NewCell(weapon, $"att{i}", new Vector2(50.0f + (i % 3) * 134.0f, -246.0f - (i / 3) * 136.0f), new Vector2(CELL, CELL), AttachmentCaption(part), 0);
			}

			// Bottom-left: Health, Armor, Gear (big slots with the light frame)
			RectTransform left = Group(_inventory, "Equipped", new Vector2(0.0f, 0.0f), new Vector2(170.0f, 125.0f + BIG_CELL));
			_healthCell = NewCell(left, "health", new Vector2(0.0f, 0.0f),             new Vector2(BIG_CELL, BIG_CELL), "Health", InventorySlot.Health);
			_armorCell  = NewCell(left, "armor",  new Vector2(BIG_PITCH, 0.0f),        new Vector2(BIG_CELL, BIG_CELL), "Armor",  InventorySlot.Armor);
			_gearCell   = NewCell(left, "gear",   new Vector2(BIG_PITCH * 2.0f, 0.0f), new Vector2(BIG_CELL, BIG_CELL), "Gear",   InventorySlot.Gear);
			_healthCell.Equipped = _armorCell.Equipped = _gearCell.Equipped = true;

			// Bottom-center: the 5 (10) slots, positioned every frame (one or two rows), the chip with a line along the row
			RectTransform center = Group(_inventory, "Slots", new Vector2(0.5f, 0.0f), Vector2.zero);
			int maxSlots = Mathf.Max(LootSettings.Current.GetSlotCount(true), LootSettings.Current.GetSlotCount(false));
			_slotCells = new Cell[maxSlots];
			for (int i = 0; i < maxSlots; ++i)
			{
				_slotCells[i] = NewCell(center, $"slot{i}", Vector2.zero, new Vector2(CELL, CELL), string.Empty, i);
			}
			_slotsLabelBox = Chip(center, "INVENTORY", Vector2.zero, 170.0f);
			_slotsLabel    = _slotsLabelBox.GetComponentInChildren<TextMeshProUGUI>();
			Image slotsLine = HudKit.Image(_slotsLabelBox, "Line", new Color(s.Chip.r, s.Chip.g, s.Chip.b, 0.35f));
			slotsLine.rectTransform.anchorMin = slotsLine.rectTransform.anchorMax = new Vector2(1.0f, 1.0f);
			slotsLine.rectTransform.pivot     = new Vector2(0.0f, 1.0f);
			slotsLine.rectTransform.sizeDelta = new Vector2(5.0f * CELL_PITCH - (CELL_PITCH - CELL) - 170.0f, 1.0f);

			// Bottom-right: Storage, Equipment
			RectTransform right = Group(_inventory, "Storage", new Vector2(1.0f, 0.0f), new Vector2(-260.0f - BIG_PITCH - BIG_CELL, 125.0f + BIG_CELL));
			_storageCell   = NewCell(right, "storage",   new Vector2(0.0f, 0.0f),      new Vector2(BIG_CELL, BIG_CELL), "Storage",   InventorySlot.Backpack);
			_equipmentCell = NewCell(right, "equipment", new Vector2(BIG_PITCH, 0.0f), new Vector2(BIG_CELL, BIG_CELL), "Equipment", InventorySlot.Equipment);
			_storageCell.Equipped = _equipmentCell.Equipped = true;

			foreach (Cell cell in new[] { _healthCell, _armorCell, _gearCell, _storageCell, _equipmentCell })
			{
				HudKit.Show(cell.Frame, true);
			}

			// Stash: across the top center, a dark panel with the chip centered above
			int   columns = Mathf.Max(1, s.StashColumns);
			int   rows    = Mathf.Max(1, s.StashRows);
			float pitch   = STASH_CELL + 22.0f;
			float width   = columns * pitch;

			_stash = Group(_inventory, "Stash", new Vector2(0.5f, 1.0f), new Vector2(-width * 0.5f, -96.0f));
			RectTransform stashChip = Chip(_stash, "STASH", new Vector2(width * 0.5f - 130.0f, 0.0f), 260.0f);
			_stashTitle = stashChip.GetComponentInChildren<TextMeshProUGUI>();

			_stashGrid = HudKit.Rect(_stash, "Grid");
			Place(_stashGrid, new Vector2(0.0f, -32.0f), new Vector2(width + 14.0f, rows * pitch + 2.0f));
			HudKit.Panel(_stashGrid, "Back", new Color(0.0f, 0.0f, 0.0f, 0.45f));

			_stashCells = new Cell[columns * rows];
			for (int i = 0; i < _stashCells.Length; ++i)
			{
				_stashCells[i] = NewCell(_stashGrid, $"stash{i}", new Vector2(12.0f + (i % columns) * pitch, -12.0f - (i / columns) * pitch), new Vector2(STASH_CELL, STASH_CELL), string.Empty, 0);
				_stashCells[i].InStash = true;
			}

			_stashScrollbar = HudKit.Image(_stashGrid, "Scrollbar", new Color(1.0f, 1.0f, 1.0f, 0.12f)).rectTransform;
			_stashScrollbar.anchorMin = new Vector2(1.0f, 0.0f);
			_stashScrollbar.anchorMax = new Vector2(1.0f, 1.0f);
			_stashScrollbar.pivot     = new Vector2(1.0f, 0.5f);
			_stashScrollbar.sizeDelta = new Vector2(6.0f, -8.0f);
			_stashScrollbar.anchoredPosition = new Vector2(-3.0f, 0.0f);
			_stashThumb = HudKit.Image(_stashScrollbar, "Thumb", new Color(1.0f, 1.0f, 1.0f, 0.6f)).rectTransform;

			// Hover: white corner brackets just outside the cell (reference), then the tooltip
			_brackets = HudKit.Rect(_inventory, "Brackets");
			Image brackets = HudKit.Image(_brackets, "Corners", s.Text, HudKit.Brackets);
			brackets.type = Image.Type.Sliced;
			HudKit.Stretch(brackets.rectTransform, Vector2.zero, Vector2.one);
			brackets.rectTransform.offsetMin = new Vector2(-5.0f, -5.0f);
			brackets.rectTransform.offsetMax = new Vector2(5.0f, 5.0f);
			_brackets.gameObject.SetActive(false);

			_tooltip = HudKit.Rect(_inventory, "Tooltip");
			_tooltip.anchorMin = Vector2.zero;
			_tooltip.anchorMax = Vector2.zero;
			_tooltip.pivot     = new Vector2(0.0f, 1.0f);
			_tooltip.sizeDelta = new Vector2(380.0f, 150.0f);
			HudKit.Panel(_tooltip, "Back", new Color(0.03f, 0.035f, 0.04f, 0.94f));

			_tooltipName = HudKit.Label(_tooltip, "Name", 21.0f, TextAlignmentOptions.TopLeft, s.Team);
			Pad(_tooltipName.rectTransform, new Vector2(12.0f, -10.0f), 30.0f);
			_tooltipText = HudKit.Text(_tooltip, "Text", 16.0f, TextAlignmentOptions.TopLeft, s.Text);
			_tooltipText.textWrappingMode = TextWrappingModes.Normal;
			Pad(_tooltipText.rectTransform, new Vector2(12.0f, -42.0f), 70.0f);
			_tooltipHints = HudKit.Label(_tooltip, "Hints", 15.0f, TextAlignmentOptions.BottomLeft, s.Text);
			_tooltipHints.rectTransform.anchorMin = Vector2.zero;
			_tooltipHints.rectTransform.anchorMax = new Vector2(1.0f, 0.0f);
			_tooltipHints.rectTransform.pivot     = Vector2.zero;
			_tooltipHints.rectTransform.sizeDelta = new Vector2(-24.0f, 24.0f);
			_tooltipHints.rectTransform.anchoredPosition = new Vector2(12.0f, 8.0f);
			_tooltip.gameObject.SetActive(false);

			// The drag ghost: the picked-up item under the mouse
			_dragGhost = HudKit.Image(_inventory, "DragGhost", new Color(1.0f, 1.0f, 1.0f, 0.85f));
			_dragGhost.preserveAspect = true;
			_dragGhost.rectTransform.anchorMin = _dragGhost.rectTransform.anchorMax = Vector2.zero;
			_dragGhost.rectTransform.pivot     = new Vector2(0.5f, 0.5f);
			_dragGhost.rectTransform.sizeDelta = new Vector2(CELL, CELL);
			_dragGhost.gameObject.SetActive(false);

			_inventory.gameObject.SetActive(false);
		}

		private void UpdateInventory()
		{
			InventoryScreen screen = GetInventoryScreen();
			bool open = screen != null && screen.IsOpen == true && _context.HasInput == true;

			HudKit.Show(_inventory, open);
			Readout["inventory.open"] = open == true ? "open" : "closed";
			if (open == false)
			{
				_hovered  = null;
				_pressed  = null;
				_dragging = false;
				HudKit.Show(_dragGhost, false);
				return;
			}

			HudSettings s         = _settings;
			Inventory   inventory = _agent.Inventory;
			Health      health    = _agent.Health;
			int         tab       = screen.WeaponTab;

			// Ammo
			for (int i = 0; i < _ammoCells.Length; ++i)
			{
				ECaliber caliber = (ECaliber)i;
				int      rounds  = _agent.Weapons.GetAmmo(caliber);
				Cell     cell    = _ammoCells[i];

				FillCell(cell, rounds > 0 ? LootStack.Ammo(caliber, rounds) : default, rounds > 0, rounds > 0 ? rounds.ToString() : string.Empty);
				HudKit.Set(cell.Name, GunData.GetCaliberName(caliber));
				cell.Name.color = rounds > 0 ? s.Text : s.Dim;
			}

			// Weapon tabs and the gun
			for (int i = 0; i < 2; ++i)
			{
				bool selected = tab == i + 1;
				// Reference: the selected tab is a light chip with dark text, the other a dark outline with dim text
				_tabBacks[i].color = selected == true ? s.Chip : new Color(0.0f, 0.0f, 0.0f, 0.45f);
				_tabTexts[i].color = selected == true ? new Color(0.08f, 0.08f, 0.08f, 1.0f) : s.Dim;
			}

			BulletWeapon gun = _agent.Weapons.GetWeapon(tab) as BulletWeapon;
			bool hasGun = gun != null && gun.GunData != null;
			_gunCell.Slot = InventorySlot.Gun(tab);
			FillCell(_gunCell, hasGun == true ? LootStack.Gun(ItemCatalog.GetGunIndex(gun.GunData), gun.MagazineAmmo, gun.AttachmentKey) : default, true, string.Empty);
			HudKit.Set(_gunTitle, hasGun == true ? $"{gun.GunData.GunName.ToUpperInvariant()} / {GunData.GetCaliberName(gun.GunData.Caliber).ToUpperInvariant()}" : "EMPTY");
			HudKit.Set(_gunInfo, hasGun == true ? $"{gun.GunData.Class} · {gun.GunData.FireMode} · mag {gun.MagazineAmmo} / {gun.MaxMagazineAmmo}{(_agent.Weapons.CurrentWeaponSlot == tab ? "  · in hand" : string.Empty)}" : string.Empty);

			for (int i = 0; i < _attachmentCells.Length; ++i)
			{
				EAttachmentSlot part       = (EAttachmentSlot)i;
				Cell            cell       = _attachmentCells[i];
				bool            hasSlot    = hasGun == true && GunTakes(gun.GunData, part);
				AttachmentData  attachment = hasGun == true ? gun.GetAttachment(part) : null;

				cell.Slot = InventorySlot.Attachment(tab, part);
				FillCell(cell, attachment != null ? LootStack.Attachment(gun.GetAttachmentID(part)) : default, hasSlot, string.Empty);
				cell.Caption.color = hasSlot == true ? s.Text : s.Dim;
			}

			// Health, Armor, Gear
			LootStack healthSlot = _agent.HealthSlot;
			FillCell(_healthCell, healthSlot, true, healthSlot.IsEmpty == false ? health.GetHealItemCount((EHealItem)healthSlot.Index).ToString() : string.Empty);
			FillCell(_armorCell, health.ArmorTier > 0 ? LootStack.ArmorVest(health.ArmorTier, health.ArmorLeft) : default, true, health.ArmorTier > 0 ? $"T{health.ArmorTier}" : string.Empty);
			LootStack gearSlot = _agent.GearSlot;
			FillCell(_gearCell, gearSlot, true, gearSlot.IsEmpty == false ? gearSlot.SecondsLeft.ToString("0") : string.Empty);

			// The slots: one row of 5, or two with a backpack
			int   count   = inventory.SlotCount;
			int   rowSize = 5;
			int   rows    = (count + rowSize - 1) / rowSize;
			float pitch   = CELL_PITCH;
			float rowLeft = -(rowSize * pitch - (pitch - CELL)) * 0.5f;
			float bottom  = rows > 1 ? 98.0f : 162.0f;

			var slotNames = _slotNames.Clear();
			for (int i = 0; i < _slotCells.Length; ++i)
			{
				Cell cell    = _slotCells[i];
				bool visible = i < count;
				HudKit.Show(cell.Root, visible);
				if (visible == false)
					continue;

				int row = i / rowSize;
				cell.Root.anchoredPosition = new Vector2(rowLeft + (i % rowSize) * pitch, bottom + (rows - 1 - row) * pitch + CELL);

				LootStack stack = inventory.GetSlot(i);
				FillCell(cell, stack, true, stack.IsEmpty == false && ItemCatalog.GetStackSize(stack) > 1 ? CountOf(stack) : string.Empty);
				slotNames.Append(i > 0 ? "," : string.Empty).Append(stack.IsEmpty == true ? "-" : ItemCatalog.GetName(stack));
			}

			_slotsLabelBox.anchoredPosition = new Vector2(rowLeft, bottom + (rows - 1) * pitch + CELL + 32.0f);
			HudKit.Set(_slotsLabel, inventory.HasBackpack == true ? "BACKPACK" : "INVENTORY");
			Readout["inventory.slots"] = slotNames.ToString();

			// Storage, Equipment
			FillCell(_storageCell, _agent.HasBackpack == true ? LootStack.Backpack() : default, true, string.Empty);
			LootStack equipment = _agent.EquipmentSlot;
			FillCell(_equipmentCell, equipment, true, equipment.IsEmpty == false ? CountOf(equipment) : string.Empty);

			UpdateStash(screen);
			UpdateHover(screen);

			Readout["inventory.tab"]       = tab.ToString();
			Readout["inventory.equipment"] = equipment.IsEmpty == true ? "none" : ItemCatalog.GetName(equipment);
		}

		private void UpdateStash(InventoryScreen screen)
		{
			LootBag stash = _agent.Inventory.OpenStash;
			HudKit.Show(_stash, stash != null);
			if (stash == null)
			{
				Readout["inventory.stash"] = "closed";
				return;
			}

			_stashIndices.Clear();
			for (int i = 0; i < LootBag.Capacity; ++i)
			{
				if (stash.GetItem(i).IsEmpty == false)
				{
					_stashIndices.Add(i);
				}
			}

			int columns   = Mathf.Max(1, _settings.StashColumns);
			int rowsShown = Mathf.Max(1, _settings.StashRows);
			int rows      = Mathf.Max(1, (_stashIndices.Count + columns - 1) / columns);
			int maxScroll = Mathf.Max(0, rows - rowsShown);

			// The wheel scrolls it while the mouse is over it
			Mouse mouse = Mouse.current;
			if (mouse != null && maxScroll > 0 && RectTransformUtility.RectangleContainsScreenPoint(_stashGrid, mouse.position.ReadValue(), GetUICamera()) == true)
			{
				float wheel = mouse.scroll.ReadValue().y;
				if (wheel < 0.0f) { screen.StashScroll += 1; }
				if (wheel > 0.0f) { screen.StashScroll -= 1; }
			}
			screen.StashScroll = Mathf.Clamp(screen.StashScroll, 0, maxScroll);

			int first = screen.StashScroll * columns;
			for (int i = 0; i < _stashCells.Length; ++i)
			{
				Cell cell = _stashCells[i];
				int  n    = first + i;
				bool used = n < _stashIndices.Count;

				cell.Slot = used == true ? _stashIndices[n] : 0;
				FillCell(cell, used == true ? stash.GetItem(_stashIndices[n]) : default, used, used == true && ItemCatalog.GetStackSize(cell.Stack) > 1 ? CountOf(stash.GetItem(_stashIndices[n])) : string.Empty);
				cell.Back.color = used == true ? _settings.SlotBack : new Color(0.0f, 0.0f, 0.0f, 0.18f);
			}

			HudKit.Show(_stashScrollbar, maxScroll > 0);
			if (maxScroll > 0)
			{
				float share = rowsShown / (float)rows;
				float start = screen.StashScroll / (float)rows;
				_stashThumb.anchorMin = new Vector2(0.0f, 1.0f - start - share);
				_stashThumb.anchorMax = new Vector2(1.0f, 1.0f - start);
				_stashThumb.offsetMin = Vector2.zero;
				_stashThumb.offsetMax = Vector2.zero;
			}

			HudKit.Set(_stashTitle, string.IsNullOrEmpty(stash.OwnerName) == true ? "STASH" : $"STASH  |  {stash.OwnerName}");
			Readout["inventory.stash"] = $"{_stashIndices.Count} items, row {screen.StashScroll} of {maxScroll}";
		}

		/// <summary>The cell under the mouse (corner brackets, tooltip) and the clicks: on the cells, and on the weapon tabs.</summary>
		private void UpdateHover(InventoryScreen screen)
		{
			Mouse  mouse  = Mouse.current;
			Camera camera = GetUICamera();
			Vector2 point = mouse != null ? mouse.position.ReadValue() : new Vector2(-1000.0f, -1000.0f);

			_hovered = null;
			for (int i = 0; i < _cells.Count; ++i)
			{
				Cell cell = _cells[i];
				if (cell.Lit == true && cell.Root.gameObject.activeInHierarchy == true && RectTransformUtility.RectangleContainsScreenPoint(cell.Root, point, camera) == true)
				{
					_hovered = cell;
				}
			}

			bool left  = mouse != null && mouse.leftButton.wasPressedThisFrame == true;
			bool right = mouse != null && mouse.rightButton.wasPressedThisFrame == true;

			for (int i = 0; i < _tabBacks.Length; ++i)
			{
				if (left == true && RectTransformUtility.RectangleContainsScreenPoint(_tabBacks[i].rectTransform, point, camera) == true)
				{
					screen.SetWeaponTab(i + 1);
				}
			}

			// Right click acts at once; a left press waits for the release (a click) or a drag
			if (_hovered != null && right == true && _dragging == false)
			{
				screen.Click(_hovered.Slot, _hovered.Stack, true, _hovered.InStash);
			}

			UpdateDrag(screen, mouse, point, camera, left);

			// Corner brackets
			bool showHover = _hovered != null && _hovered.Stack.IsEmpty == false && _dragging == false;
			HudKit.Show(_brackets, _hovered != null);
			if (_hovered != null)
			{
				_hovered.Root.GetWorldCorners(_corners);
				_brackets.anchorMin = _brackets.anchorMax = _brackets.pivot = new Vector2(0.5f, 0.5f);
				_brackets.sizeDelta = _hovered.Root.rect.size;
				_brackets.position  = (_corners[0] + _corners[2]) * 0.5f;
			}

			// Tooltip: name in yellow, one line, use time and what's left, then the mouse hints
			HudKit.Show(_tooltip, showHover);
			Readout["inventory.hover"] = showHover == true ? $"{_hovered.Id} {ItemCatalog.GetName(_hovered.Stack)}" : "none";
			if (showHover == false)
				return;

			LootStack stack = _hovered.Stack;
			HudKit.Set(_tooltipName, ItemCatalog.GetName(stack).ToUpperInvariant());
			HudKit.Set(_tooltipText, GetTooltipText(stack, _hovered));
			HudKit.Set(_tooltipHints, GetTooltipHints(stack, _hovered));

			RectTransformUtility.ScreenPointToLocalPointInRectangle(_inventory, point, camera, out Vector2 local);
			Vector2 size     = _inventory.rect.size;
			Vector2 position = local + size * 0.5f + new Vector2(18.0f, -18.0f);
			_tooltip.sizeDelta = new Vector2(380.0f, 82.0f + _tooltipText.preferredHeight);
			if (position.x + _tooltip.sizeDelta.x > size.x) { position.x -= _tooltip.sizeDelta.x + 36.0f; }
			if (position.y - _tooltip.sizeDelta.y < 0.0f)   { position.y += _tooltip.sizeDelta.y + 36.0f; }
			_tooltip.anchoredPosition = position;
			_tooltip.SetAsLastSibling();
		}

		/// <summary>Phase 11a: the left button. A press arms, moving picks the item up, the release clicks or drops it.</summary>
		private void UpdateDrag(InventoryScreen screen, Mouse mouse, Vector2 point, Camera camera, bool pressed)
		{
			bool held     = mouse != null && mouse.leftButton.isPressed == true;
			bool released = mouse != null && mouse.leftButton.wasReleasedThisFrame == true;

			if (pressed == true)
			{
				_pressed    = _hovered != null && _hovered.Stack.IsEmpty == false ? _hovered : null;
				_pressPoint = point;
				_dragging   = false;
			}

			if (_pressed != null && held == true && _dragging == false && (point - _pressPoint).sqrMagnitude > DRAG_PIXELS * DRAG_PIXELS * _scale * _scale)
			{
				_dragging = true;
			}

			if (_dragging == true && _pressed != null)
			{
				Sprite icon = IconSettings.Get(_pressed.Stack);
				HudKit.SetIcon(_dragGhost, icon, ItemColor(_pressed.Stack));
				_dragGhost.color = new Color(1.0f, 1.0f, 1.0f, 0.85f) * (icon != null && _pressed.Stack.Kind != EItemKind.Gear ? Color.white : ItemColor(_pressed.Stack));
				_dragGhost.rectTransform.sizeDelta = _pressed.Stack.Kind == EItemKind.Gun ? new Vector2(CELL * 2.0f, CELL) : new Vector2(CELL, CELL);
				_dragGhost.rectTransform.anchoredPosition = point / _scale;
				_dragGhost.transform.SetAsLastSibling();
			}
			HudKit.Show(_dragGhost, _dragging == true && _pressed != null);

			if (released == false && (held == true || _pressed == null))
				return;

			Cell source = _pressed;
			bool drag   = _dragging;
			_pressed  = null;
			_dragging = false;
			HudKit.Show(_dragGhost, false);

			if (source == null)
				return;

			if (drag == false)
			{
				// No movement: the old left click
				if (_hovered == source)
				{
					screen.Click(source.Slot, source.Stack, false, source.InStash);
				}
				return;
			}

			int tab = 0;
			for (int i = 0; i < _tabBacks.Length; ++i)
			{
				if (RectTransformUtility.RectangleContainsScreenPoint(_tabBacks[i].rectTransform, point, camera) == true)
				{
					tab = i + 1;
				}
			}

			bool outside = _hovered == null && tab == 0 && IsOverPanel(point, camera) == false;
			string result = DropOn(screen, source, _hovered, tab, outside);
			Readout["inventory.drag"] = result;
		}

		/// <summary>Over any of the screen's groups (a release there that isn't on a cell does nothing; anywhere else drops the item).</summary>
		private bool IsOverPanel(Vector2 point, Camera camera)
		{
			if (_stash.gameObject.activeInHierarchy == true && RectTransformUtility.RectangleContainsScreenPoint(_stashGrid, point, camera) == true)
				return true;
			if (_tooltip.gameObject.activeInHierarchy == true && RectTransformUtility.RectangleContainsScreenPoint(_tooltip, point, camera) == true)
				return true;
			return false;
		}

		/// <summary>
		/// What a drop does, with the server actions that already exist (phase 5a) plus Move:
		/// stash → anywhere else: take it; anything → the stash: drop it in; slot → slot: Move (swap or top up); an attachment → the gun,
		/// one of its slots or a weapon tab: attach to that gun; a gun's attachment → an inventory slot: detach; an item → its equipped
		/// slot (Health, Armor, Gear, Equipment, Storage): equip; released over empty screen: drop at your feet.
		/// </summary>
		private string DropOn(InventoryScreen screen, Cell source, Cell target, int tab, bool outside)
		{
			if (target == source)
				return "same cell";

			LootStack stack    = source.Stack;
			bool      fromSlot = source.InStash == false && source.Slot >= 0 && source.Slot < LootSettings.MaxInventorySlots && source.Id.StartsWith("slot") == true;
			bool      onGun    = source.Id.StartsWith("att") == true;

			if (source.InStash == true)
			{
				if (target != null && target.InStash == true)
					return "stash to stash";

				screen.Request(EInventoryAction.TakeFromStash, source.Slot, 0);
				return $"take {ItemCatalog.GetName(stack)} from the stash";
			}

			if (target != null && target.InStash == true)
			{
				screen.Request(EInventoryAction.Drop, source.Slot, 0);
				return $"{ItemCatalog.GetName(stack)} into the stash";
			}

			if (outside == true)
			{
				screen.Request(EInventoryAction.Drop, source.Slot, 0);
				return $"drop {ItemCatalog.GetName(stack)}";
			}

			// An attachment onto a weapon tab (attach to that gun)
			if (tab > 0)
			{
				if (stack.Kind != EItemKind.Attachment || onGun == true)
					return "only a loose attachment goes on a tab";

				screen.Request(EInventoryAction.Use, source.Slot, tab);
				return $"attach {ItemCatalog.GetName(stack)} to weapon {tab}";
			}

			if (target == null)
				return "nothing there";

			bool toSlot = target.Id.StartsWith("slot") == true;

			if (fromSlot == true && toSlot == true)
			{
				screen.Request(EInventoryAction.Move, source.Slot, target.Slot);
				return $"move slot {source.Slot} to {target.Slot}";
			}

			if (onGun == true && toSlot == true)
			{
				screen.Request(EInventoryAction.Use, source.Slot, screen.WeaponTab);
				return $"detach {ItemCatalog.GetName(stack)}";
			}

			if (stack.Kind == EItemKind.Attachment && (target.Id == "gun" || target.Id.StartsWith("att") == true))
			{
				screen.Request(EInventoryAction.Use, source.Slot, screen.WeaponTab);
				return $"attach {ItemCatalog.GetName(stack)} to weapon {screen.WeaponTab}";
			}

			if (fromSlot == true && (target.Equipped == true || target.Id == "gun"))
			{
				screen.Request(EInventoryAction.Use, source.Slot, screen.WeaponTab);
				return $"equip {ItemCatalog.GetName(stack)}";
			}

			return $"{source.Id} onto {target.Id} does nothing";
		}

		private string GetTooltipText(LootStack stack, Cell cell)
		{
			string description = ItemCatalog.GetDescription(stack);
			int    newline     = description.IndexOf('\n');
			string text        = newline >= 0 ? description.Substring(0, newline) : description;

			switch (stack.Kind)
			{
				case EItemKind.Heal:
					text += $"\nUse time  {HealthSettings.Current.GetHealItem((EHealItem)stack.Index).UseTime:0.#} s\nCarried  {_agent.Health.GetHealItemCount((EHealItem)stack.Index)}";
					break;
				case EItemKind.Equipment:
					EquipmentSettings settings = EquipmentSettings.Current;
					text += stack.Equipment == EEquipment.Grapple
						? $"\nUse time  {settings.GrappleDeployTime + settings.GrappleHookTime + settings.GrapplePullTime + settings.GrappleRecoverTime:0.#} s\nPulls left  {stack.Charges} of {settings.GrapplePulls}"
						: $"\nUse time  {settings.PressToGunReady:0.#} s\nOne use";
					break;
				case EItemKind.Gear:
					text += $"\nDuration  {stack.SecondsLeft:0.#} of {EquipmentSettings.Current.GearSeconds:0} s{(cell.Id == "gear" && _agent.GearOn == true ? "  (on)" : string.Empty)}";
					break;
				case EItemKind.Armor:
					float durability = HealthSettings.Current.GetArmorTier(Mathf.Clamp(stack.Index, 1, 3)).Durability;
					text += $"\nTier {stack.Index}  ·  {stack.Armor:0} of {durability:0} left";
					break;
				case EItemKind.Ammo:
					text += $"\n{stack.Count} rounds  ·  carry up to {GunplaySettings.Current.GetCarryCap((ECaliber)stack.Index)}";
					break;
				case EItemKind.Gun:
					GunData gun = ItemCatalog.GetGunData(stack.Index);
					text += gun != null ? $"\n{gun.Class}  ·  {gun.FireMode}  ·  {GunData.GetCaliberName(gun.Caliber)}\nMag  {stack.Mag}" : string.Empty;
					break;
			}

			return text;
		}

		/// <summary>Blackout's mouse hints: what the left and right buttons do on this cell.</summary>
		private string GetTooltipHints(LootStack stack, Cell cell)
		{
			Color  key       = _settings.Team;
			bool   stashOpen = _agent.Inventory.OpenStash != null;
			string drop      = stashOpen == true ? "STASH" : "DROP";
			string leftHint  = null;
			string rightHint = drop;

			if (cell.InStash == true)
			{
				leftHint  = "TAKE";
				rightHint = null;
			}
			else if (cell.Id.StartsWith("att") == true)
			{
				leftHint = "DETACH";
			}
			else if (cell.Id.StartsWith("ammo") == true)
			{
				rightHint = $"{drop} A BOX";
			}
			else if (cell.Equipped == true || cell.Id == "gun")
			{
				leftHint = null;
			}
			else
			{
				switch (stack.Kind)
				{
					case EItemKind.Attachment: leftHint = $"ATTACH TO WEAPON {GetInventoryScreen()?.WeaponTab ?? 1}"; break;
					case EItemKind.Equipment:
					case EItemKind.Gear:
					case EItemKind.Gun:        leftHint = "EQUIP"; break;
					default:                   leftHint = "USE";   break;
				}
			}

			string hex = Hex(key);
			string left  = leftHint  != null ? $"<color=#{hex}>[LMB]</color> {leftHint}" : string.Empty;
			string right = rightHint != null ? $"<color=#{hex}>[RMB]</color> {rightHint}" : string.Empty;
			return left.Length > 0 && right.Length > 0 ? $"{left}     {right}" : left + right;
		}

		private void FillCell(Cell cell, LootStack stack, bool lit, string count)
		{
			cell.Stack = stack;
			cell.Lit   = lit;

			bool hasItem = stack.IsEmpty == false;
			cell.Back.color = lit == true ? _settings.SlotBack : _settings.SlotDim;
			HudKit.Show(cell.Icon, hasItem);
			if (hasItem == true)
			{
				// Phase 11a: the item's icon (its colored block until Bake Icons has made one); gear symbols in the gear color
				HudKit.SetIcon(cell.Icon, IconSettings.Get(stack), ItemColor(stack));
				Color color = stack.Kind == EItemKind.Gear || cell.Icon.sprite == HudKit.White ? ItemColor(stack) : Color.white;
				color.a = lit == true ? 1.0f : 0.35f;
				// The picked-up item fades in its cell while it's dragged
				if (_dragging == true && _pressed == cell)
				{
					color.a *= 0.3f;
				}
				cell.Icon.color = color;
			}

			// The count: white on a small black badge in the bottom-right corner (reference)
			HudKit.Set(cell.Count, count);
			bool hasCount = string.IsNullOrEmpty(count) == false;
			HudKit.Show(cell.CountBack, hasCount);
			if (hasCount == true)
			{
				cell.CountBack.rectTransform.sizeDelta = new Vector2(Mathf.Max(22.0f, cell.Count.preferredWidth + 8.0f), 24.0f);
			}
		}

		private Cell NewCell(RectTransform parent, string id, Vector2 topLeft, Vector2 size, string caption, int slot)
		{
			HudSettings s = _settings;
			var cell = new Cell { Id = id, Slot = slot, Lit = true };

			cell.Root = HudKit.Rect(parent, id);
			Place(cell.Root, topLeft, size);

			cell.Back = HudKit.Panel(cell.Root, "Back", s.SlotBack);

			// The light frame of the big equipped slots (shown for those only)
			cell.Frame = HudKit.Image(cell.Root, "Frame", s.SlotFrame, HudKit.Frame);
			cell.Frame.type = Image.Type.Sliced;
			HudKit.Stretch(cell.Frame.rectTransform, Vector2.zero, Vector2.one);
			cell.Frame.gameObject.SetActive(false);

			// Phase 11a (reference): no item names in the cells, the icon fills it; only the ammo cells name their caliber
			cell.Icon = HudKit.Image(cell.Root, "Icon", Color.white);
			HudKit.Stretch(cell.Icon.rectTransform, new Vector2(0.09f, 0.09f), new Vector2(0.91f, 0.91f));

			cell.Name = HudKit.Text(cell.Root, "Name", 15.0f, TextAlignmentOptions.TopLeft, s.Text);
			HudKit.Stretch(cell.Name.rectTransform, new Vector2(0.06f, 0.74f), new Vector2(0.96f, 0.97f));
			HudKit.Fit(cell.Name, 8.0f);
			cell.Name.gameObject.SetActive(false);

			cell.CountBack = HudKit.Image(cell.Root, "CountBack", new Color(0.0f, 0.0f, 0.0f, 0.92f));
			HudKit.Fixed(cell.CountBack.rectTransform, new Vector2(1.0f, 0.0f), new Vector2(24.0f, 24.0f), new Vector2(1.0f, 0.0f)).anchoredPosition = new Vector2(-3.0f, 3.0f);
			cell.CountBack.gameObject.SetActive(false);
			cell.Count = HudKit.Text(cell.CountBack.rectTransform, "Count", 20.0f, TextAlignmentOptions.Center, s.Text, true);
			HudKit.Stretch(cell.Count.rectTransform, Vector2.zero, Vector2.one);

			// The caption under the cell, in plain words like the reference ("Health", "Optic", "Under Barrel")
			cell.Caption = HudKit.Text(cell.Root, "Caption", 17.0f, TextAlignmentOptions.TopLeft, s.Text);
			cell.Caption.rectTransform.anchorMin = new Vector2(0.0f, 0.0f);
			cell.Caption.rectTransform.anchorMax = new Vector2(1.0f, 0.0f);
			cell.Caption.rectTransform.pivot     = new Vector2(0.0f, 1.0f);
			cell.Caption.rectTransform.sizeDelta = new Vector2(40.0f, 22.0f);
			cell.Caption.rectTransform.anchoredPosition = new Vector2(0.0f, -4.0f);
			cell.Caption.text = caption;

			_cells.Add(cell);
			return cell;
		}

		/// <summary>A group anchored at a screen point, laid out from its top-left in pixels at 1080p.</summary>
		private static RectTransform Group(RectTransform parent, string name, Vector2 anchor, Vector2 position)
		{
			RectTransform group = HudKit.Rect(parent, name);
			group.anchorMin = anchor;
			group.anchorMax = anchor;
			group.pivot     = new Vector2(0.0f, 1.0f);
			group.sizeDelta = Vector2.zero;
			group.anchoredPosition = position;
			return group;
		}

		private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
		{
			rect.anchorMin = new Vector2(0.0f, 1.0f);
			rect.anchorMax = new Vector2(0.0f, 1.0f);
			rect.pivot     = new Vector2(0.0f, 1.0f);
			rect.sizeDelta = size;
			rect.anchoredPosition = topLeft;
		}

		private static void Pad(RectTransform rect, Vector2 topLeft, float height)
		{
			rect.anchorMin = new Vector2(0.0f, 1.0f);
			rect.anchorMax = new Vector2(1.0f, 1.0f);
			rect.pivot     = new Vector2(0.0f, 1.0f);
			rect.sizeDelta = new Vector2(-topLeft.x * 2.0f, height);
			rect.anchoredPosition = topLeft;
		}

		/// <summary>A light label chip with spaced capitals, like the reference's AMMO / INVENTORY / STASH tags.</summary>
		private RectTransform Chip(RectTransform parent, string label, Vector2 topLeft, float width)
		{
			Image back = HudKit.Image(parent, $"{label}Chip", _settings.Chip);
			Place(back.rectTransform, topLeft, new Vector2(width, 24.0f));
			TextMeshProUGUI text = HudKit.Label(back.rectTransform, "Text", 16.0f, TextAlignmentOptions.Center, new Color(0.08f, 0.08f, 0.08f, 1.0f));
			HudKit.Stretch(text.rectTransform, Vector2.zero, Vector2.one);
			text.text = label;
			return back.rectTransform;
		}

		private InventoryScreen GetInventoryScreen()
		{
			return _agent != null && _agent.HasInputAuthority == true ? _agent.GetComponent<InventoryScreen>() : null;
		}

		private Camera GetUICamera()
		{
			return _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
		}

		private static string AttachmentCaption(EAttachmentSlot slot)
		{
			switch (slot)
			{
				case EAttachmentSlot.Optic:       return "Optic";
				case EAttachmentSlot.Muzzle:      return "Muzzle";
				case EAttachmentSlot.Underbarrel: return "Under Barrel";
				case EAttachmentSlot.Magazine:    return "Magazine";
				case EAttachmentSlot.Stock:       return "Stock";
				case EAttachmentSlot.Grip:        return "Grip";
				default:                          return slot.ToString();
			}
		}

		private static bool GunTakes(GunData gun, EAttachmentSlot slot)
		{
			AttachmentData[] attachments = GunplaySettings.Current.Attachments;
			for (int i = 0; gun != null && attachments != null && i < attachments.Length; ++i)
			{
				if (attachments[i] != null && attachments[i].Slot == slot && gun.Allows(attachments[i]) == true)
					return true;
			}

			return false;
		}

		private static string CountOf(LootStack stack)
		{
			return stack.Kind == EItemKind.Equipment && stack.Equipment == EEquipment.Grapple ? stack.Charges.ToString() : stack.Count.ToString();
		}

		/// <summary>The item's color: the gear symbols' tint, and the block drawn when an item has no icon yet.</summary>
		private static Color ItemColor(LootStack stack)
		{
			switch (stack.Kind)
			{
				case EItemKind.Gun:        return new Color(0.42f, 0.43f, 0.45f);
				case EItemKind.Attachment: return new Color(0.20f, 0.65f, 0.85f);
				case EItemKind.Heal:       return HealColor((EHealItem)stack.Index);
				case EItemKind.Armor:      return stack.Index == 1 ? new Color(0.50f, 0.55f, 0.42f) : (stack.Index == 2 ? new Color(0.25f, 0.40f, 0.75f) : new Color(0.45f, 0.20f, 0.60f));
				case EItemKind.Ammo:       return LootSettings.Current.GetAmmoBoxColor((ECaliber)stack.Index);
				case EItemKind.Equipment:  return EquipmentSettings.Current.GetEquipmentColor(stack.Equipment);
				case EItemKind.Gear:       return EquipmentSettings.Current.GetGearColor(stack.GearItem);
				case EItemKind.Backpack:   return new Color(0.45f, 0.32f, 0.18f);
				default:                   return Color.gray;
			}
		}
	}
}
