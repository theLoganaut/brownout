namespace TPSBR.UI
{
	using TMPro;
	using UnityEngine;
	using UnityEngine.UI;

	/// <summary>
	/// Bottom of the HUD (phase 6a): squad list (box 6), health (7, no portrait: Logan, 6 Oct), healing item (8), armor (9), the Gear widget
	/// in the quick slot (10, Logan, 6 Oct), equipment (11), ammo (12), caliber and mag pips (13), gun (14). The bottom hides while downed.
	/// </summary>
	public sealed partial class BrownoutHud
	{
		// CONSTANTS

		private const int MAX_PIPS = 10;

		// PRIVATE MEMBERS

		private sealed class SquadRow
		{
			public RectTransform   Root;
			public TextMeshProUGUI Name;
			public Image           HealthFill;
			public Image           ArmorFill;
			public TextMeshProUGUI Cross;
			public Image           BleedBack;
			public Image           BleedFill;
		}

		private readonly SquadRow[] _squad = new SquadRow[2];

		private Agent           _trackedTeammate;
		private bool            _trackedTeammateDowned;
		private string          _trackedTeammateName;

		private TextMeshProUGUI _health;

		private RectTransform   _healSlot;
		private Image           _healIcon;
		private TextMeshProUGUI _healName;
		private TextMeshProUGUI _healCount;

		private Image           _armorIcon;
		private TextMeshProUGUI _armorTier;
		private Image           _armorWear;

		private Image           _gearIcon;
		private Image           _gearRing;
		private TextMeshProUGUI _gearName;
		private TextMeshProUGUI _gearSeconds;
		private CanvasGroup     _gearGroup;

		private Image           _equipmentIcon;
		private TextMeshProUGUI _equipmentName;
		private TextMeshProUGUI _equipmentCount;
		private CanvasGroup     _equipmentGroup;

		private RectTransform   _gunGroup;
		private TextMeshProUGUI _mag;
		private TextMeshProUGUI _reserve;
		private TextMeshProUGUI _caliber;
		private Image           _loadedPip;
		private Image           _loadedPipFill;
		private readonly Image[] _sparePips = new Image[MAX_PIPS];
		private Image           _gunSilhouette;
		private TextMeshProUGUI _gunName;
		private TextMeshProUGUI _fireMode;

		// PRIVATE METHODS

		private void BuildBottom()
		{
			HudSettings s = _settings;

			// 6 Squad list: number, name, segmented health bar with armor as a yellow tail; downed adds a cross and a red bleed-out bar
			RectTransform squad = HudKit.Box(_root, "Squad", s.GetBox(HudSettings.Squad));
			for (int i = 0; i < _squad.Length; ++i)
			{
				_squad[i] = BuildSquadRow(squad, i);
			}

			// Phase 11a (reference): the bottom widgets are dark boxes joined into two strips, each strip on one band of the screen
			// (left 918-990 px, right 915-980 px at 1080p), with the key in a small dark square above the item boxes
			HudSettings.Box leftBand  = s.GetBox(HudSettings.Health);
			HudSettings.Box rightBand = s.GetBox(HudSettings.QuickSlot);
			rightBand.Y1 = s.GetBox(HudSettings.Equipment).Y1;

			// 7 Health: just the number (150, up to 200; Logan, 10 Oct: no bar)
			RectTransform health = BandBox(_bottom, "Health", HudSettings.Health, leftBand);
			HudKit.Panel(health, "Back", s.WidgetBack);
			_health = HudKit.Text(health, "Number", 46.0f, TextAlignmentOptions.Center, s.HealthColor, true);
			HudKit.Stretch(_health.rectTransform, Vector2.zero, Vector2.one);

			// 8 Healing item: Q above, icon, count
			_healSlot = BandBox(_bottom, "HealItem", HudSettings.HealItem, leftBand);
			KeyHint(_healSlot, "Q");
			_healIcon  = SlotIcon(_healSlot, out _healName, out _healCount);

			// 9 Armor: the vest's icon by tier with the tier in the corner, and a bar under it for the wear (Logan, 10 Oct); dim when there's none
			RectTransform armor = BandBox(_bottom, "Armor", HudSettings.Armor, leftBand);
			HudKit.Panel(armor, "Back", s.WidgetBack);
			_armorIcon = HudKit.Image(armor, "Vest", s.ArmorTail);
			HudKit.Stretch(_armorIcon.rectTransform, new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.94f));
			_armorTier = HudKit.Text(armor, "Tier", 13.0f, TextAlignmentOptions.TopRight, s.Text, true);
			HudKit.Stretch(_armorTier.rectTransform, new Vector2(0.0f, 0.0f), new Vector2(0.94f, 0.97f));
			Image wearBack = HudKit.Image(armor, "WearBack", new Color(1.0f, 1.0f, 1.0f, 0.12f));
			HudKit.Stretch(wearBack.rectTransform, new Vector2(0.12f, 0.07f), new Vector2(0.88f, 0.13f));
			_armorWear = HudKit.Fill(wearBack.rectTransform, "Wear", s.ArmorTail);
			HudKit.Stretch(_armorWear.rectTransform, Vector2.zero, Vector2.one);

			// 10 Quick slot: the Gear widget (seconds left as a ring, lit while on, Left Ctrl above)
			RectTransform gear = BandBox(_bottom, "Gear", HudSettings.QuickSlot, rightBand);
			_gearGroup = gear.gameObject.AddComponent<CanvasGroup>();
			HudKit.Panel(gear, "Back", s.WidgetBack);
			KeyHint(gear, "CTRL");
			_gearRing = HudKit.Fill(gear, "Ring", s.Gear, HudKit.Ring, true);
			HudKit.Fixed(_gearRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(46.0f, 46.0f), new Vector2(0.5f, 0.5f));
			_gearIcon = HudKit.Image(_gearRing.rectTransform, "Icon", Color.white);
			_gearIcon.rectTransform.anchorMin = new Vector2(0.24f, 0.24f);
			_gearIcon.rectTransform.anchorMax = new Vector2(0.76f, 0.76f);
			_gearIcon.rectTransform.offsetMin = Vector2.zero;
			_gearIcon.rectTransform.offsetMax = Vector2.zero;
			_gearSeconds = HudKit.Text(gear, "Seconds", 13.0f, TextAlignmentOptions.TopRight, s.Text, true);
			HudKit.Stretch(_gearSeconds.rectTransform, Vector2.zero, new Vector2(0.94f, 0.97f));
			_gearName = HudKit.Text(gear, "Name", 11.0f, TextAlignmentOptions.Bottom, s.Text);
			HudKit.Stretch(_gearName.rectTransform, new Vector2(0.0f, 0.0f), new Vector2(1.0f, 0.2f));
			_gearName.gameObject.SetActive(false);

			// 11 Equipment: E above, icon and count; greyed while it can't be used
			RectTransform equipment = BandBox(_bottom, "Equipment", HudSettings.Equipment, rightBand);
			_equipmentGroup = equipment.gameObject.AddComponent<CanvasGroup>();
			KeyHint(equipment, "E");
			_equipmentIcon = SlotIcon(equipment, out _equipmentName, out _equipmentCount);

			// 12-14 Ammo, caliber with pips, gun
			_gunGroup = HudKit.Rect(_bottom, "GunGroup");
			HudKit.Stretch(_gunGroup, Vector2.zero, Vector2.one);

			RectTransform ammo = BandBox(_gunGroup, "Ammo", HudSettings.Ammo, rightBand);
			HudKit.Panel(ammo, "Back", s.WidgetBack);
			_mag = HudKit.Text(ammo, "Mag", 34.0f, TextAlignmentOptions.Bottom, s.Text, true);
			HudKit.Stretch(_mag.rectTransform, new Vector2(0.0f, 0.36f), new Vector2(1.0f, 0.98f));
			_reserve = HudKit.Text(ammo, "Reserve", 16.0f, TextAlignmentOptions.Top, s.Dim, true);
			HudKit.Stretch(_reserve.rectTransform, new Vector2(0.0f, 0.04f), new Vector2(1.0f, 0.36f));

			RectTransform type = BandBox(_gunGroup, "AmmoType", HudSettings.AmmoType, rightBand);
			HudKit.Panel(type, "Back", s.WidgetBack);
			_caliber = HudKit.Label(type, "Caliber", 15.0f, TextAlignmentOptions.TopLeft, s.Text);
			HudKit.Stretch(_caliber.rectTransform, new Vector2(0.08f, 0.5f), new Vector2(1.0f, 0.92f));
			RectTransform pips = HudKit.Rect(type, "Pips");
			HudKit.Stretch(pips, new Vector2(0.08f, 0.14f), new Vector2(1.0f, 0.5f));

			_loadedPip = HudKit.Image(pips, "Loaded", s.Dim);
			_loadedPip.rectTransform.anchorMin = new Vector2(0.0f, 0.0f);
			_loadedPip.rectTransform.anchorMax = new Vector2(0.0f, 1.0f);
			_loadedPip.rectTransform.pivot     = new Vector2(0.0f, 0.0f);
			_loadedPip.rectTransform.sizeDelta = new Vector2(6.0f, 0.0f);
			_loadedPipFill = HudKit.Fill(_loadedPip.rectTransform, "Fill", s.Text);
			_loadedPipFill.fillMethod = Image.FillMethod.Vertical;
			_loadedPipFill.fillOrigin = (int)Image.OriginVertical.Bottom;
			HudKit.Stretch(_loadedPipFill.rectTransform, Vector2.zero, Vector2.one);

			for (int i = 0; i < MAX_PIPS; ++i)
			{
				Image pip = HudKit.Image(pips, $"Spare{i}", s.Text);
				pip.rectTransform.anchorMin = new Vector2(0.0f, 0.0f);
				pip.rectTransform.anchorMax = new Vector2(0.0f, 0.6f);
				pip.rectTransform.pivot     = new Vector2(0.0f, 0.0f);
				pip.rectTransform.sizeDelta = new Vector2(5.0f, 0.0f);
				pip.rectTransform.anchoredPosition = new Vector2(12.0f + i * 9.0f, 0.0f);
				pip.gameObject.SetActive(false);
				_sparePips[i] = pip;
			}

			// The gun: its white silhouette in the box, the name and fire mode small along the bottom (reference)
			RectTransform gun = BandBox(_gunGroup, "Gun", HudSettings.Gun, rightBand);
			HudKit.Panel(gun, "Back", s.WidgetBack);
			_gunSilhouette = HudKit.Image(gun, "Silhouette", s.Text);
			_gunSilhouette.preserveAspect = true;
			HudKit.Stretch(_gunSilhouette.rectTransform, new Vector2(0.06f, 0.3f), new Vector2(0.94f, 0.96f));
			_gunName = HudKit.Label(gun, "Name", 12.0f, TextAlignmentOptions.BottomRight, s.Text);
			HudKit.Stretch(_gunName.rectTransform, new Vector2(0.3f, 0.04f), new Vector2(0.95f, 0.32f));
			HudKit.Fit(_gunName, 8.0f);
			_fireMode = HudKit.Label(gun, "FireMode", 10.0f, TextAlignmentOptions.BottomLeft, new Color(s.Text.r, s.Text.g, s.Text.b, 0.55f));
			HudKit.Stretch(_fireMode.rectTransform, new Vector2(0.05f, 0.04f), new Vector2(0.4f, 0.32f));
		}

		/// <summary>An element's box from hud_rects.json, with its top and bottom moved onto the strip's band.</summary>
		private RectTransform BandBox(RectTransform parent, string name, int id, HudSettings.Box band)
		{
			HudSettings.Box box = _settings.GetBox(id);
			box.Y0 = band.Y0;
			box.Y1 = band.Y1;
			return HudKit.Box(parent, name, box);
		}

		private SquadRow BuildSquadRow(RectTransform parent, int index)
		{
			HudSettings s = _settings;
			var row = new SquadRow();

			row.Root = HudKit.Rect(parent, $"Row{index}");
			HudKit.Stretch(row.Root, new Vector2(0.0f, 1.0f - (index + 1) * 0.5f), new Vector2(1.0f, 1.0f - index * 0.5f));

			// Phase 11a (reference): the number in a colored square, then the name
			Image badge = HudKit.Image(row.Root, "Badge", s.Team);
			HudKit.Fixed(badge.rectTransform, new Vector2(0.0f, 0.72f), new Vector2(20.0f, 20.0f), new Vector2(0.0f, 0.5f));
			TextMeshProUGUI number = HudKit.Text(badge.rectTransform, "Number", 16.0f, TextAlignmentOptions.Center, new Color(0.08f, 0.08f, 0.08f, 1.0f), true);
			HudKit.Stretch(number.rectTransform, Vector2.zero, Vector2.one);
			number.text = (index + 1).ToString();

			row.Name = HudKit.Text(row.Root, "Name", 16.0f, TextAlignmentOptions.BottomLeft, s.Text, true);
			HudKit.Stretch(row.Name.rectTransform, new Vector2(0.0f, 0.45f), Vector2.one);
			row.Name.rectTransform.offsetMin = new Vector2(27.0f, 0.0f);

			row.Cross = HudKit.Text(row.Root, "Cross", 18.0f, TextAlignmentOptions.BottomRight, s.Text, true);
			HudKit.Stretch(row.Cross.rectTransform, new Vector2(0.0f, 0.45f), Vector2.one);
			row.Cross.text = "+";

			Image back = HudKit.Image(row.Root, "BarBack", s.Panel);
			HudKit.Stretch(back.rectTransform, new Vector2(0.0f, 0.2f), new Vector2(1.0f, 0.42f));

			// Health over the first 80% (three 50 HP segments), the armor tail over the last 20%
			row.HealthFill = HudKit.Fill(back.rectTransform, "Health", s.HealthColor);
			HudKit.Stretch(row.HealthFill.rectTransform, Vector2.zero, new Vector2(0.8f, 1.0f));
			row.ArmorFill = HudKit.Fill(back.rectTransform, "Armor", s.ArmorTail);
			HudKit.Stretch(row.ArmorFill.rectTransform, new Vector2(0.81f, 0.0f), Vector2.one);

			for (int i = 1; i < 3; ++i)
			{
				Image divider = HudKit.Image(back.rectTransform, $"Divider{i}", Color.black);
				divider.rectTransform.anchorMin = new Vector2(0.8f * i / 3.0f, 0.0f);
				divider.rectTransform.anchorMax = new Vector2(0.8f * i / 3.0f, 1.0f);
				divider.rectTransform.sizeDelta = new Vector2(2.0f, 0.0f);
			}

			row.BleedBack = HudKit.Image(row.Root, "BleedBack", s.Panel);
			HudKit.Stretch(row.BleedBack.rectTransform, new Vector2(0.0f, 0.02f), new Vector2(1.0f, 0.14f));
			row.BleedFill = HudKit.Fill(row.BleedBack.rectTransform, "Bleed", s.BleedOut);
			HudKit.Stretch(row.BleedFill.rectTransform, Vector2.zero, Vector2.one);

			return row;
		}

		private void KeyHint(RectTransform parent, string key)
		{
			// Phase 11a (reference): the key in a small dark square centered above the box
			RectTransform chip = HudKit.Rect(parent, "Key");
			HudKit.Fixed(chip, new Vector2(0.5f, 1.0f), new Vector2(key.Length > 1 ? 50.0f : 32.0f, 30.0f), new Vector2(0.5f, 0.0f)).anchoredPosition = new Vector2(0.0f, 6.0f);
			HudKit.Panel(chip, "Back", _settings.WidgetBack);
			TextMeshProUGUI hint = HudKit.Text(chip, "Text", 17.0f, TextAlignmentOptions.Center, _settings.Text, true);
			HudKit.Stretch(hint.rectTransform, Vector2.zero, Vector2.one);
			hint.text = key;
		}

		/// <summary>The item's icon filling a dark box and the count on a black badge in the bottom-right corner (the name stays hidden).</summary>
		private Image SlotIcon(RectTransform parent, out TextMeshProUGUI name, out TextMeshProUGUI count)
		{
			HudKit.Panel(parent, "Back", _settings.WidgetBack);

			Image icon = HudKit.Image(parent, "Icon", Color.white);
			HudKit.Stretch(icon.rectTransform, new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.9f));

			name = HudKit.Text(parent, "Name", 11.0f, TextAlignmentOptions.Bottom, _settings.Text);
			HudKit.Stretch(name.rectTransform, new Vector2(0.0f, 0.02f), new Vector2(1.0f, 0.28f));
			name.gameObject.SetActive(false);

			Image badge = HudKit.Image(parent, "CountBack", new Color(0.0f, 0.0f, 0.0f, 0.92f));
			HudKit.Fixed(badge.rectTransform, new Vector2(1.0f, 0.0f), new Vector2(20.0f, 20.0f), new Vector2(1.0f, 0.0f)).anchoredPosition = new Vector2(-2.0f, 2.0f);
			count = HudKit.Text(badge.rectTransform, "Count", 16.0f, TextAlignmentOptions.Center, _settings.Text, true);
			HudKit.Stretch(count.rectTransform, Vector2.zero, Vector2.one);

			return icon;
		}

		/// <summary>Sets a SlotIcon count and fits its badge (hidden when there's no count).</summary>
		private static void SetCount(TextMeshProUGUI count, string value)
		{
			HudKit.Set(count, value);
			var badge = (RectTransform)count.rectTransform.parent;
			bool shown = string.IsNullOrEmpty(value) == false;
			HudKit.Show(badge, shown);
			if (shown == true)
			{
				badge.sizeDelta = new Vector2(Mathf.Max(20.0f, count.preferredWidth + 7.0f), 20.0f);
			}
		}

		private void UpdateBottom()
		{
			Health health = _agent.Health;

			UpdateSquad();

			// Phase 6b: the inventory and the full map cover the bottom, and the victory banner clears it
			bool overlay = _context.Input != null && _context.Input.IsOverlayOpen == true && _agent.HasInputAuthority == true;
			HudKit.Show(_bottom, health.IsDowned == false && overlay == false && IsVictoryShown == false);
			if (health.IsDowned == true)
				return;

			// Health
			int hp = Mathf.CeilToInt(health.CurrentHealth);
			HudKit.Set(_health, hp.ToString());
			Readout["health"] = hp.ToString();

			// Healing item (the Health slot's kind; the count is every one carried)
			LootStack healSlot = _agent.HealthSlot;
			bool      hasHeal  = healSlot.Kind == EItemKind.Heal && healSlot.IsEmpty == false;
			int       heals    = hasHeal == true ? health.GetHealItemCount((EHealItem)healSlot.Index) : 0;
			HudKit.SetIcon(_healIcon, hasHeal == true ? IconSettings.Get(healSlot) : null, hasHeal == true ? HealColor((EHealItem)healSlot.Index) : _settings.Panel);
			_healIcon.enabled = hasHeal;
			HudKit.Set(_healName, hasHeal == true ? ItemCatalog.GetName(healSlot) : string.Empty);
			SetCount(_healCount, hasHeal == true ? heals.ToString() : string.Empty);
			Readout["heal"] = hasHeal == true ? $"{ItemCatalog.GetName(healSlot)} x{heals}" : "none";

			// Armor: the vest by tier; the bar under it shows the wear
			bool  hasArmor = health.ArmorTier > 0;
			float wear     = hasArmor == true ? Mathf.Clamp01(health.ArmorLeft / Mathf.Max(1.0f, health.ArmorDurability)) : 0.0f;
			HudKit.SetIcon(_armorIcon, hasArmor == true ? IconSettings.GetArmor(health.ArmorTier) : null, hasArmor == true ? _settings.ArmorTail : _settings.Panel);
			_armorIcon.enabled = hasArmor;
			HudKit.Set(_armorTier, hasArmor == true ? $"T{health.ArmorTier}" : string.Empty);
			_armorWear.fillAmount = wear;
			Readout["armor"] = hasArmor == true ? $"T{health.ArmorTier} {health.ArmorLeft:F0}/{health.ArmorDurability:F0}" : "none";

			// Gear (quick slot)
			LootStack gearSlot = _agent.GearSlot;
			bool      hasGear  = gearSlot.Kind == EItemKind.Gear && gearSlot.IsEmpty == false;
			bool      gearOn   = hasGear == true && _agent.Gear.IsOn == true;
			float     total    = Mathf.Max(0.1f, EquipmentSettings.Current.GearSeconds);
			_gearGroup.alpha     = hasGear == false ? 0.35f : (gearOn == true ? 1.0f : 0.6f);
			_gearRing.fillAmount = hasGear == true ? Mathf.Clamp01(gearSlot.SecondsLeft / total) : 0.0f;
			_gearRing.color      = gearOn == true ? _settings.Gear : _settings.Dim;
			HudKit.SetIcon(_gearIcon, hasGear == true ? IconSettings.Get(gearSlot) : null, hasGear == true ? EquipmentSettings.Current.GetGearColor(gearSlot.GearItem) : _settings.Panel);
			_gearIcon.color      = hasGear == false ? _settings.Panel : (gearOn == true ? _settings.Gear : _settings.Text);
			HudKit.Set(_gearSeconds, hasGear == true ? gearSlot.SecondsLeft.ToString("0.0") : string.Empty);
			HudKit.Set(_gearName, hasGear == true ? ItemCatalog.GetName(gearSlot) : string.Empty);
			Readout["gear"] = hasGear == true ? $"{ItemCatalog.GetName(gearSlot)} {(gearOn ? "on" : "off")} {gearSlot.SecondsLeft:0.0}" : "none";

			// Equipment: grenades one per slot, the grapple its pulls
			LootStack equipSlot = _agent.EquipmentSlot;
			bool      hasEquip  = equipSlot.Kind == EItemKind.Equipment && equipSlot.IsEmpty == false;
			int       uses      = hasEquip == false ? 0 : (equipSlot.Equipment == EEquipment.Grapple ? equipSlot.Charges : equipSlot.Count);
			bool      usable    = hasEquip == true && health.IsHandsBusy == false && (_agent.Weapons.CurrentWeapon is BulletWeapon reloading ? reloading.IsReloading == false : true);
			_equipmentGroup.alpha = hasEquip == false ? 0.35f : (usable == true ? 1.0f : 0.5f);
			HudKit.SetIcon(_equipmentIcon, hasEquip == true ? IconSettings.Get(equipSlot) : null, hasEquip == true ? EquipmentSettings.Current.GetEquipmentColor(equipSlot.Equipment) : _settings.Panel);
			_equipmentIcon.enabled = hasEquip;
			HudKit.Set(_equipmentName, hasEquip == true ? ItemCatalog.GetName(equipSlot) : string.Empty);
			SetCount(_equipmentCount, hasEquip == true ? uses.ToString() : string.Empty);
			Readout["equipment"] = hasEquip == true ? $"{ItemCatalog.GetName(equipSlot)} x{uses}" : "none";

			// Ammo, caliber with pips, gun (hidden with only the punch)
			var gun = _agent.Weapons.CurrentWeapon as BulletWeapon;
			HudKit.Show(_gunGroup, gun != null && gun.GunData != null);
			if (gun == null || gun.GunData == null)
			{
				Readout["ammo"] = "none";
				return;
			}

			int magSize = Mathf.Max(1, gun.MaxMagazineAmmo);
			int reserve = gun.WeaponAmmo;
			int spares  = Mathf.Min(MAX_PIPS, reserve / magSize);

			HudKit.Set(_mag, gun.MagazineAmmo.ToString());
			HudKit.Set(_reserve, reserve.ToString());
			HudKit.Set(_caliber, GunData.GetCaliberName(gun.GunData.Caliber));
			_loadedPipFill.fillAmount = gun.IsReloading == true ? Mathf.Clamp01(gun.ReloadElapsed / Mathf.Max(0.01f, gun.ReloadTime)) : Mathf.Clamp01(gun.MagazineAmmo / (float)magSize);

			for (int i = 0; i < MAX_PIPS; ++i)
			{
				HudKit.Show(_sparePips[i], i < spares);
			}

			HudKit.Set(_gunName, gun.GunData.GunName);
			Sprite silhouette = IconSettings.GetGunSilhouette(ItemCatalog.GetGunIndex(gun.GunData));
			_gunSilhouette.enabled = silhouette != null;
			if (silhouette != null && _gunSilhouette.sprite != silhouette)
			{
				_gunSilhouette.sprite = silhouette;
			}
			HudKit.Set(_fireMode, gun.GunData.FireMode.ToString().ToUpperInvariant());

			Readout["ammo"]    = $"{gun.MagazineAmmo}/{reserve}";
			Readout["caliber"] = GunData.GetCaliberName(gun.GunData.Caliber);
			Readout["pips"]    = spares.ToString();
			Readout["gun"]     = gun.GunData.GunName;
		}

		private void UpdateSquad()
		{
			Agent teammate = GetTeammate();

			FillSquadRow(_squad[0], _agent, 1);
			FillSquadRow(_squad[1], teammate, 2);

			// Phase 11a: the teammate going down, getting up and dying go in the system feed
			if (teammate != null)
			{
				bool downed = teammate.Health.IsDowned;
				if (teammate == _trackedTeammate && downed != _trackedTeammateDowned)
				{
					AddSystemRow(downed == true ? $"{GetName(teammate)} is down" : $"{GetName(teammate)} is back up");
				}

				_trackedTeammate       = teammate;
				_trackedTeammateDowned = downed;
				_trackedTeammateName   = GetName(teammate);
			}
			else if (_trackedTeammate != null)
			{
				bool died = _trackedTeammate.Object != null && _trackedTeammate.Object.IsValid == true && _trackedTeammate.Health.IsAlive == false;
				if (died == true)
				{
					AddSystemRow($"{_trackedTeammateName} was killed");
				}
				_trackedTeammate = null;
			}

			Readout["squad.teammate"] = teammate != null ? $"{GetName(teammate)} {teammate.Health.CurrentHealth:F0}{(teammate.Health.IsDowned ? " downed" : "")}" : "none";
		}

		private void FillSquadRow(SquadRow row, Agent agent, int number)
		{
			bool visible = agent != null && agent.Object != null && agent.Object.IsValid == true;
			HudKit.Show(row.Root, visible);
			if (visible == false)
				return;

			Health health = agent.Health;
			HudKit.Set(row.Name, GetName(agent));

			row.HealthFill.fillAmount = Mathf.Clamp01(health.CurrentHealth / Mathf.Max(1.0f, health.MaxHealth));
			row.ArmorFill.fillAmount  = health.ArmorTier > 0 ? Mathf.Clamp01(health.ArmorLeft / Mathf.Max(1.0f, health.ArmorDurability)) : 0.0f;

			bool downed = health.IsDowned;
			HudKit.Show(row.Cross, downed);
			HudKit.Show(row.BleedBack, downed);
			if (downed == true)
			{
				float bleedTotal = Mathf.Max(0.1f, HealthSettings.Current.GetBleedOutTime(health.DownCount));
				row.BleedFill.fillAmount = Mathf.Clamp01(health.BleedOutLeft / bleedTotal);
			}
		}

		private static Color HealColor(EHealItem item)
		{
			switch (item)
			{
				case EHealItem.Bandage: return new Color(0.9f, 0.9f, 0.9f);
				default:                return new Color(0.85f, 0.25f, 0.25f);
			}
		}
	}
}
