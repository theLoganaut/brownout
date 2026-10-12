namespace TPSBR.UI
{
	using System.Collections.Generic;
	using TMPro;
	using UnityEngine;
	using UnityEngine.UI;

	/// <summary>
	/// Center of the HUD (phase 6a): four-line crosshair, hit marker (white X, red on a down or kill, a vest under it on armor), DOWNED /
	/// KILLED banners with the damage dealt, the progress bar (heal, revive, being revived), the bleed-out bar, pickup prompts on a stem,
	/// the dash cooldown arc and red screen edges at low health. Then the world markers: teammate name and distance, and pings.
	/// The red damage arc, hit tint and flash white-out stay the sample's (UIAgentEffects, on the sample's canvas).
	/// </summary>
	public sealed partial class BrownoutHud
	{
		// PRIVATE MEMBERS

		private RectTransform   _crosshair;
		private readonly Image[] _crosshairLines = new Image[4];

		private RectTransform   _hitMarker;
		private readonly Image[] _hitMarkerTicks = new Image[4];
		private Image           _hitMarkerArmor;
		private float           _hitMarkerTime = -100.0f;
		private bool            _hitMarkerFatal;
		private bool            _hitMarkerArmored;
		private bool            _hitMarkerCritical;

		// Phase 11c: armor broken cue, kill confirm, blood and flashes on the one hit
		private TextMeshProUGUI _armorBroke;
		private float           _armorBrokeTime = -100.0f;
		private Image           _killConfirm;
		private float           _killConfirmTime = -100.0f;
		private Image           _blood;
		private float           _bloodAlpha;
		private Image           _flash;
		private float           _flashTime = -100.0f;
		private float           _flashLength = 1.0f;
		private Color           _flashColor;

		private RectTransform   _banner;
		private TextMeshProUGUI _bannerTitle;
		private TextMeshProUGUI _bannerDamage;
		private float           _bannerTime = -100.0f;

		private RectTransform   _progress;
		private TextMeshProUGUI _progressLabel;
		private Image           _progressFill;

		private RectTransform   _bleed;
		private Image           _bleedFill;

		private RectTransform   _prompt;
		private TextMeshProUGUI _promptMain;
		private TextMeshProUGUI _promptSecond;
		private Image           _promptStem;
		private Image           _promptIcon;

		private Image           _dashRing;
		private Image           _lowHealth;

		private RectTransform   _teammateMarker;
		private TextMeshProUGUI _teammateMarkerText;
		private RectTransform   _pingMarker;
		private TextMeshProUGUI _pingMarkerText;
		private RectTransform   _teammatePingMarker;
		private TextMeshProUGUI _teammatePingMarkerText;

		private readonly Dictionary<Health, float> _damageDealt = new Dictionary<Health, float>(8);
		private readonly Dictionary<Health, float> _lastHitTime = new Dictionary<Health, float>(8);
		private readonly List<Health>              _expired     = new List<Health>(8);

		// PRIVATE METHODS

		private void BuildCenter()
		{
			HudSettings s = _settings;

			_lowHealth = HudKit.Image(_center, "LowHealth", new Color(s.Enemy.r, s.Enemy.g, s.Enemy.b, 0.0f), HudKit.Vignette);
			HudKit.Stretch(_lowHealth.rectTransform, Vector2.zero, Vector2.one);

			// Phase 11c: blood at the edges that builds up with damage taken and fades; a flash for the downing hit or broken armor
			_blood = HudKit.Image(_center, "Blood", new Color(0.42f, 0.0f, 0.0f, 0.0f), HudKit.Blood);
			HudKit.Stretch(_blood.rectTransform, Vector2.zero, Vector2.one);
			_flash = HudKit.Image(_center, "Flash", new Color(1.0f, 0.0f, 0.0f, 0.0f), HudKit.Vignette);
			HudKit.Stretch(_flash.rectTransform, new Vector2(-0.15f, -0.15f), new Vector2(1.15f, 1.15f));

			// Crosshair: four short lines that open with spread
			_crosshair = HudKit.Fixed(HudKit.Rect(_center, "Crosshair"), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0.5f, 0.5f));
			for (int i = 0; i < 4; ++i)
			{
				bool vertical = i < 2;
				_crosshairLines[i] = HudKit.Image(_crosshair, $"Line{i}", s.Text);
				HudKit.Fixed(_crosshairLines[i].rectTransform, new Vector2(0.5f, 0.5f), vertical == true ? new Vector2(2.0f, s.CrosshairLine) : new Vector2(s.CrosshairLine, 2.0f), new Vector2(0.5f, 0.5f));
			}

			// Hit marker: four diagonal ticks with a gap around the center
			_hitMarker = HudKit.Fixed(HudKit.Rect(_center, "HitMarker"), new Vector2(0.5f, 0.5f), new Vector2(s.HitMarkerSize, s.HitMarkerSize), new Vector2(0.5f, 0.5f));
			for (int i = 0; i < 4; ++i)
			{
				float   angle = 45.0f + i * 90.0f;
				Vector2 dir   = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
				Image   tick  = HudKit.Image(_hitMarker, $"Tick{i}", Color.white);
				HudKit.Fixed(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(s.HitMarkerSize * 0.3f, 2.5f), new Vector2(0.5f, 0.5f));
				tick.rectTransform.anchoredPosition = dir * s.HitMarkerSize * 0.33f;
				tick.rectTransform.localRotation    = Quaternion.Euler(0.0f, 0.0f, angle);
				_hitMarkerTicks[i] = tick;
			}
			_hitMarkerArmor = HudKit.Image(_hitMarker, "Armor", s.ArmorTail);
			HudKit.Fixed(_hitMarkerArmor.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(10.0f, 12.0f), new Vector2(0.5f, 1.0f));
			_hitMarker.gameObject.SetActive(false);

			// Phase 11c: ARMOR BROKEN under the crosshair, a skull over it on a kill
			_armorBroke = HudKit.Label(_center, "ArmorBroken", 16.0f, TextAlignmentOptions.Center, FeelSettings.Current.ArmorHitColor);
			HudKit.Fixed(_armorBroke.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(300.0f, 24.0f), new Vector2(0.5f, 0.5f)).anchoredPosition = new Vector2(0.0f, -64.0f);
			_armorBroke.text = "ARMOR BROKEN";
			_armorBroke.gameObject.SetActive(false);

			float skull = FeelSettings.Current.KillConfirmSize;
			_killConfirm = HudKit.Image(_center, "KillConfirm", s.Enemy, HudKit.Skull);
			HudKit.Fixed(_killConfirm.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(skull, skull), new Vector2(0.5f, 0.5f)).anchoredPosition = new Vector2(0.0f, 70.0f);
			_killConfirm.gameObject.SetActive(false);

			// DOWNED / KILLED banner: two lines below the crosshair
			_banner = HudKit.Rect(_center, "Banner");
			HudKit.Stretch(_banner, new Vector2(0.3f, 0.36f), new Vector2(0.7f, 0.42f));
			_bannerTitle = HudKit.Text(_banner, "Title", 26.0f, TextAlignmentOptions.Center, s.Text);
			HudKit.Stretch(_bannerTitle.rectTransform, new Vector2(0.0f, 0.5f), Vector2.one);
			_bannerDamage = HudKit.Text(_banner, "Damage", 18.0f, TextAlignmentOptions.Center, s.Dim, true);
			HudKit.Stretch(_bannerDamage.rectTransform, Vector2.zero, new Vector2(1.0f, 0.5f));
			_banner.gameObject.SetActive(false);

			// Progress bar (reference: x 0.36-0.61 at y 0.71, from the top)
			Vector4 bar = s.ProgressBar;
			_progress = HudKit.Rect(_center, "Progress");
			HudKit.Stretch(_progress, new Vector2(bar.x, 1.0f - bar.w), new Vector2(bar.z, 1.0f - bar.y));
			HudKit.Stretch(HudKit.Image(_progress, "Back", s.Panel).rectTransform, Vector2.zero, Vector2.one);
			_progressFill = HudKit.Fill(_progress, "Fill", s.Text);
			HudKit.Stretch(_progressFill.rectTransform, Vector2.zero, Vector2.one);
			_progressLabel = HudKit.Text(_progress, "Label", 20.0f, TextAlignmentOptions.Bottom, s.Text, true);
			_progressLabel.rectTransform.anchorMin = new Vector2(0.0f, 1.0f);
			_progressLabel.rectTransform.anchorMax = new Vector2(1.0f, 1.0f);
			_progressLabel.rectTransform.pivot     = new Vector2(0.5f, 0.0f);
			_progressLabel.rectTransform.sizeDelta = new Vector2(0.0f, 26.0f);
			_progressLabel.rectTransform.anchoredPosition = new Vector2(0.0f, 4.0f);
			_progress.gameObject.SetActive(false);

			// Bleed-out bar while downed, under the progress bar
			_bleed = HudKit.Rect(_center, "BleedOut");
			HudKit.Stretch(_bleed, new Vector2(bar.x, 1.0f - bar.w - 0.05f), new Vector2(bar.z, 1.0f - bar.y - 0.05f));
			HudKit.Stretch(HudKit.Image(_bleed, "Back", s.Panel).rectTransform, Vector2.zero, Vector2.one);
			_bleedFill = HudKit.Fill(_bleed, "Fill", s.BleedOut);
			HudKit.Stretch(_bleedFill.rectTransform, Vector2.zero, Vector2.one);
			TextMeshProUGUI bleedLabel = HudKit.Text(_bleed, "Label", 16.0f, TextAlignmentOptions.Bottom, s.BleedOut, true);
			bleedLabel.rectTransform.anchorMin = new Vector2(0.0f, 1.0f);
			bleedLabel.rectTransform.anchorMax = new Vector2(1.0f, 1.0f);
			bleedLabel.rectTransform.pivot     = new Vector2(0.5f, 0.0f);
			bleedLabel.rectTransform.sizeDelta = new Vector2(0.0f, 22.0f);
			bleedLabel.rectTransform.anchoredPosition = new Vector2(0.0f, 2.0f);
			bleedLabel.text = "BLEEDING OUT";
			_bleed.gameObject.SetActive(false);

			// Dash cooldown: a thin ring under the crosshair
			_dashRing = HudKit.Fill(_center, "DashCooldown", s.Dim, HudKit.Ring, true);
			HudKit.Fixed(_dashRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(18.0f, 18.0f), new Vector2(0.5f, 0.5f));
			_dashRing.rectTransform.anchoredPosition = new Vector2(0.0f, -48.0f);
			_dashRing.gameObject.SetActive(false);

			// Pickup prompt: a box over the item on a thin stem
			_prompt = HudKit.Rect(_world, "Prompt");
			HudKit.Fixed(_prompt, Vector2.zero, new Vector2(320.0f, 54.0f), new Vector2(0.5f, 0.0f));
			_promptStem = HudKit.Image(_prompt, "Stem", s.Text);
			_promptStem.rectTransform.anchorMin = new Vector2(0.5f, 0.0f);
			_promptStem.rectTransform.anchorMax = new Vector2(0.5f, 0.0f);
			_promptStem.rectTransform.pivot     = new Vector2(0.5f, 1.0f);
			_promptStem.rectTransform.sizeDelta = new Vector2(1.5f, 40.0f);
			RectTransform promptBox = HudKit.Rect(_prompt, "Box");
			HudKit.Stretch(promptBox, Vector2.zero, Vector2.one);
			HudKit.Panel(promptBox, "Back", s.Panel);
			_promptMain = HudKit.Text(promptBox, "Main", 20.0f, TextAlignmentOptions.Center, s.Text, true);
			HudKit.Stretch(_promptMain.rectTransform, new Vector2(0.0f, 0.45f), Vector2.one);
			_promptSecond = HudKit.Text(promptBox, "Second", 16.0f, TextAlignmentOptions.Center, s.Dim);
			HudKit.Stretch(_promptSecond.rectTransform, Vector2.zero, new Vector2(1.0f, 0.45f));

			// Phase 11a: the item's icon floats over the prompt (reference: "a floating icon of it above the model on a thin stem")
			_promptIcon = HudKit.Image(_prompt, "Icon", Color.white);
			_promptIcon.preserveAspect = true;
			_promptIcon.rectTransform.anchorMin = new Vector2(0.5f, 1.0f);
			_promptIcon.rectTransform.anchorMax = new Vector2(0.5f, 1.0f);
			_promptIcon.rectTransform.pivot     = new Vector2(0.5f, 0.0f);
			_promptIcon.rectTransform.sizeDelta = new Vector2(96.0f, 80.0f);
			_promptIcon.rectTransform.anchoredPosition = new Vector2(0.0f, 4.0f);
			_prompt.gameObject.SetActive(false);
		}

		private void BuildWorldMarkers()
		{
			_teammateMarker = WorldMarker("Teammate", _settings.Team, out _teammateMarkerText);
			_pingMarker = WorldMarker("Ping", _settings.Ping, out _pingMarkerText);
			_teammatePingMarker = WorldMarker("TeammatePing", _settings.Ping, out _teammatePingMarkerText);
		}

		private RectTransform WorldMarker(string name, Color color, out TextMeshProUGUI text)
		{
			RectTransform marker = HudKit.Fixed(HudKit.Rect(_world, name), Vector2.zero, new Vector2(14.0f, 14.0f), new Vector2(0.5f, 0.5f));
			Image diamond = HudKit.Image(marker, "Diamond", color);
			HudKit.Stretch(diamond.rectTransform, Vector2.zero, Vector2.one);
			diamond.rectTransform.localRotation = Quaternion.Euler(0.0f, 0.0f, 45.0f);

			text = HudKit.Text(marker, "Text", 16.0f, TextAlignmentOptions.Bottom, color, true);
			text.rectTransform.anchorMin = new Vector2(0.5f, 1.0f);
			text.rectTransform.anchorMax = new Vector2(0.5f, 1.0f);
			text.rectTransform.pivot     = new Vector2(0.5f, 0.0f);
			text.rectTransform.sizeDelta = new Vector2(240.0f, 40.0f);
			text.rectTransform.anchoredPosition = new Vector2(0.0f, 4.0f);

			marker.gameObject.SetActive(false);
			return marker;
		}

		private void OnHitPerformed(HitData hit)
		{
			if (hit.Action != EHitAction.Damage || hit.Amount <= 0.0f)
				return;

			var target = hit.Target as Health;

			_hitMarkerTime     = Time.unscaledTime;
			_hitMarkerFatal    = hit.IsFatal;
			_hitMarkerArmored  = hit.IsArmorHit;   // Phase 11c: sent with the hit (was a guess from the target's armor)
			_hitMarkerCritical = hit.IsCritical;

			if (hit.ArmorBroke == true)
			{
				_armorBrokeTime = Time.unscaledTime;
			}

			if (target == null)
				return;

			// Phase 11b: the summary counts your own damage to others (not while watching a teammate)
			if (_agent != null && target != _agent.Health && _agent.Object != null && _agent.Object.InputAuthority == _context.LocalPlayerRef)
			{
				AddDamageDealt(hit.Amount);
			}
			_damageDealt.TryGetValue(target, out float dealt);
			_damageDealt[target] = dealt + hit.Amount;
			_lastHitTime[target] = Time.unscaledTime;

			if (hit.IsFatal == false)
				return;

			// A fatal hit downs or kills: the banner shows the damage dealt to that player since the last banner
			bool   killed = target.IsAlive == false || target.IsDowned == false;
			string name   = GetName(target.Agent);

			_bannerTitle.text  = killed == true ? $"KILLED <b><color=#{Hex(_settings.Enemy)}>{name}</color></b>" : $"DOWNED <b>{name}</b>";
			_bannerDamage.text = $"{Mathf.RoundToInt(_damageDealt[target])} DAMAGE DEALT";
			_bannerTime        = Time.unscaledTime;

			Readout["banner.last"] = $"{(killed ? "KILLED" : "DOWNED")} {name} {Mathf.RoundToInt(_damageDealt[target])}";

			if (killed == true)
			{
				_killConfirmTime = Time.unscaledTime;
			}

			_damageDealt.Remove(target);
			_lastHitTime.Remove(target);
		}

		/// <summary>Phase 11c: a hit on the observed player: blood builds up, the downing hit flashes red, broken armor flashes blue-white.</summary>
		private void OnHitTakenHud(HitData hit)
		{
			if (hit.Action != EHitAction.Damage || hit.Amount <= 0.0f || _agent == null)
				return;

			FeelSettings feel = FeelSettings.Current;
			_bloodAlpha = Mathf.Min(feel.BloodMax, _bloodAlpha + hit.Amount * feel.BloodPerDamage);

			if (hit.IsFatal == true && _agent.Health.IsDowned == true)
			{
				_flashTime   = Time.unscaledTime;
				_flashLength = Mathf.Max(0.05f, feel.DownedFlashTime);
				_flashColor  = new Color(0.75f, 0.0f, 0.0f, feel.DownedFlash);
				_bloodAlpha  = feel.BloodMax;
			}
			else if (hit.ArmorBroke == true)
			{
				_flashTime   = Time.unscaledTime;
				_flashLength = Mathf.Max(0.05f, feel.ArmorBreakFlashTime);
				_flashColor  = feel.ArmorBreakFlash;
			}

			Readout["hit.taken"] = $"{hit.Amount:F0}{(hit.IsCritical ? " head" : "")}{(hit.IsArmorHit ? " armor" : "")}{(hit.ArmorBroke ? " broke" : "")}{(hit.IsFatal ? " fatal" : "")}";
		}

		private void UpdateFeedbackOverlays(float now)
		{
			FeelSettings feel = FeelSettings.Current;

			_bloodAlpha = Mathf.MoveTowards(_bloodAlpha, 0.0f, Time.unscaledDeltaTime * feel.BloodFade);
			Color blood = _blood.color;
			blood.a = _bloodAlpha;
			_blood.color = blood;
			HudKit.Show(_blood, _bloodAlpha > 0.001f);

			float flash = 1.0f - (now - _flashTime) / _flashLength;
			HudKit.Show(_flash, flash > 0.0f);
			if (flash > 0.0f)
			{
				Color color = _flashColor;
				color.a *= flash * flash;
				_flash.color = color;
			}

			bool broke = now - _armorBrokeTime <= feel.ArmorBrokeTime;
			HudKit.Show(_armorBroke, broke);
			if (broke == true)
			{
				_armorBroke.alpha = Mathf.Clamp01((feel.ArmorBrokeTime - (now - _armorBrokeTime)) * 3.0f);
			}

			float kill     = now - _killConfirmTime;
			bool  showKill = kill <= feel.KillConfirmTime;
			HudKit.Show(_killConfirm, showKill);
			if (showKill == true)
			{
				float pop = 1.0f + Mathf.Max(0.0f, 0.15f - kill) / 0.15f * 0.6f;
				_killConfirm.rectTransform.localScale = Vector3.one * pop;
				Color color = _settings.Enemy;
				color.a = Mathf.Clamp01((feel.KillConfirmTime - kill) * 4.0f);
				_killConfirm.color = color;
			}

			Readout["feedback"] = $"blood {_bloodAlpha:F2}{(flash > 0.0f ? " flash" : "")}{(broke ? " armor-broken" : "")}{(showKill ? " kill" : "")}";
		}

		private void UpdateCenter()
		{
			HudSettings s      = _settings;
			Health      health = _agent.Health;
			float       now    = Time.unscaledTime;

			// Forget damage on players not hit for a while
			foreach (var pair in _lastHitTime)
			{
				if (now - pair.Value > 15.0f || pair.Key == null)
				{
					_expired.Add(pair.Key);
				}
			}
			for (int i = 0; i < _expired.Count; ++i)
			{
				_damageDealt.Remove(_expired[i]);
				_lastHitTime.Remove(_expired[i]);
			}
			_expired.Clear();

			// Red at the screen edges as health drops
			float healthShare = health.IsAlive == true && health.IsDowned == false ? health.CurrentHealth / Mathf.Max(1.0f, health.MaxHealth) : 0.0f;
			float edge        = Mathf.Clamp01((s.LowHealthEdge - healthShare) / Mathf.Max(0.01f, s.LowHealthEdge));
			Color edgeColor   = _lowHealth.color;
			edgeColor.a       = edge * 0.6f;
			_lowHealth.color  = edgeColor;

			// Crosshair: only with a gun out, hip-fire, hands free and the inventory closed
			var  gun           = _agent.Weapons.CurrentWeapon as BulletWeapon;
			bool aimed         = _agent.Character.CharacterController.Data.Aim == true;
			bool inventoryOpen = _context.Input != null && _context.Input.IsOverlayOpen == true && _agent.HasInputAuthority == true;
			bool showCrosshair = gun != null && gun.GunData != null && aimed == false && health.IsHandsBusy == false && inventoryOpen == false && _agent.Weapons.IsSwitchingWeapon() == false;

			HudKit.Show(_crosshair, showCrosshair);
			if (showCrosshair == true)
			{
				float gap = s.CrosshairGap + gun.TotalDispersion * s.CrosshairPerDegree + s.CrosshairLine * 0.5f;
				_crosshairLines[0].rectTransform.anchoredPosition = new Vector2(0.0f,  gap);
				_crosshairLines[1].rectTransform.anchoredPosition = new Vector2(0.0f, -gap);
				_crosshairLines[2].rectTransform.anchoredPosition = new Vector2( gap, 0.0f);
				_crosshairLines[3].rectTransform.anchoredPosition = new Vector2(-gap, 0.0f);
			}
			Readout["crosshair"] = showCrosshair == true ? "shown" : "hidden";

			// Hit marker (phase 11c: gold and bigger on a headshot, blue on armor, red on a down or kill; pops on each hit)
			FeelSettings feel = FeelSettings.Current;
			bool showMarker = now - _hitMarkerTime <= s.HitMarkerTime;
			HudKit.Show(_hitMarker, showMarker);
			if (showMarker == true)
			{
				Color color = _hitMarkerFatal == true ? s.Enemy : (_hitMarkerCritical == true ? feel.HeadshotColor : (_hitMarkerArmored == true ? feel.ArmorHitColor : Color.white));
				for (int i = 0; i < 4; ++i)
				{
					_hitMarkerTicks[i].color = color;
				}
				HudKit.Show(_hitMarkerArmor, _hitMarkerArmored);

				float age   = now - _hitMarkerTime;
				float scale = (_hitMarkerCritical == true ? feel.HeadshotMarkerScale : 1.0f) * Mathf.Lerp(feel.MarkerPop, 1.0f, Mathf.Clamp01(age / 0.08f));
				_hitMarker.localScale = Vector3.one * scale;
			}
			Readout["hitmarker.last"] = _hitMarkerTime > 0.0f ? $"{(_hitMarkerFatal ? "red" : (_hitMarkerCritical ? "head" : "white"))}{(_hitMarkerArmored ? " armor" : "")} {now - _hitMarkerTime:F2}s ago" : "none";

			UpdateFeedbackOverlays(now);

			// Banner
			HudKit.Show(_banner, now - _bannerTime <= s.BannerTime);

			// Progress bar: healing, reviving, or being revived
			UpdateProgress(health);

			// Bleed-out bar
			HudKit.Show(_bleed, health.IsDowned);
			if (health.IsDowned == true)
			{
				_bleedFill.fillAmount = Mathf.Clamp01(health.BleedOutLeft / Mathf.Max(0.1f, HealthSettings.Current.GetBleedOutTime(health.DownCount)));
			}

			// Dash cooldown
			var   runner   = _agent.Runner;
			int   readyTick = _agent.Character.CharacterController.Data.DashReadyTick;
			float dashLeft  = runner != null ? Mathf.Max(0.0f, (readyTick - runner.Tick) * runner.DeltaTime) : 0.0f;
			float cooldown  = Mathf.Max(0.1f, MovementSettings.Current.DashCooldown);
			HudKit.Show(_dashRing, dashLeft > 0.0f && health.IsDowned == false);
			_dashRing.fillAmount = Mathf.Clamp01(dashLeft / cooldown);
			Readout["dash"] = dashLeft.ToString("F1");

			UpdatePrompt();
		}

		private void UpdateProgress(Health health)
		{
			string label    = null;
			float  progress = 0.0f;

			if (health.IsHealing == true)
			{
				label    = $"USING {HealthSettings.Current.GetHealItem(health.HealingItem).Name.ToUpperInvariant()}";
				progress = health.HealProgress;
			}
			else if (health.IsReviving == true)
			{
				label    = "REVIVING";
				progress = health.ReviveProgress;
			}
			else if (health.IsDowned == true && health.IsBeingRevived == true)
			{
				label = "BEING REVIVED";
				for (int i = 0; i < _agents.Count; ++i)
				{
					Agent other = _agents[i];
					if (other != null && other.Object != null && other.Object.IsValid == true && other.Health.IsReviving == true && other.Health.ReviveTarget == health)
					{
						progress = other.Health.ReviveProgress;
						break;
					}
				}
			}

			HudKit.Show(_progress, label != null);
			if (label != null)
			{
				HudKit.Set(_progressLabel, label);
				_progressFill.fillAmount = progress;
			}

			Readout["progress"] = label != null ? $"{label} {progress:F2}" : "none";
		}

		private void UpdatePrompt()
		{
			IInteraction target = _agent.Health.IsDowned == false ? _agent.Interactions.InteractionTarget : null;
			bool visible = target != null && target.Equals(null) == false && target.IsActive == true && string.IsNullOrEmpty(target.Name) == false;

			if (visible == true && WorldToCanvas(target.HUDPosition, out Vector2 position) == true)
			{
				_prompt.anchoredPosition = position + new Vector2(0.0f, 40.0f);
				HudKit.Set(_promptMain, target.Name);
				HudKit.Set(_promptSecond, target.Description ?? string.Empty);
				HudKit.Show(_prompt, true);

				LootItem item = target as LootItem;
				Sprite   icon = item != null ? IconSettings.Get(item.Stack) : null;
				HudKit.Show(_promptIcon, icon != null);
				if (icon != null)
				{
					HudKit.SetIcon(_promptIcon, icon, Color.white);
					_promptIcon.color = item.Stack.Kind == EItemKind.Gear ? EquipmentSettings.Current.GetGearColor(item.Stack.GearItem) : Color.white;
					_promptIcon.rectTransform.sizeDelta = item.Stack.Kind == EItemKind.Gun ? new Vector2(180.0f, 90.0f) : new Vector2(96.0f, 80.0f);
				}

				Readout["prompt"] = target.Name;
				return;
			}

			HudKit.Show(_prompt, false);
			Readout["prompt"] = "none";
		}

		private void UpdateWorldMarkers()
		{
			Agent   teammate = GetTeammate();
			Vector3 view     = GetViewPosition();

			// Teammate: name and distance over their head, through walls; a different look when downed
			Vector2 teammatePosition = default;
			bool showTeammate = teammate != null && WorldToCanvas(teammate.transform.position + Vector3.up * 2.1f, out teammatePosition) == true;
			HudKit.Show(_teammateMarker, showTeammate);
			if (showTeammate == true)
			{
				_teammateMarker.anchoredPosition = teammatePosition;
				float distance = Vector3.Distance(view, teammate.transform.position);
				string label   = teammate.Health.IsDowned == true ? $"<color=#{Hex(_settings.BleedOut)}>+ {GetName(teammate)}</color>" : GetName(teammate);
				HudKit.Set(_teammateMarkerText, $"{label}\n<size=13>{distance:F0} m</size>");
			}
			Readout["marker.teammate"] = showTeammate == true ? "shown" : "hidden";

			PlacePing(_pingMarker, _pingMarkerText, _agent, view);
			PlacePing(_teammatePingMarker, _teammatePingMarkerText, teammate, view);

			Readout["ping.own"]      = _agent.HasPing == true ? _agent.PingPoint.ToString("F1") : "none";
			Readout["ping.teammate"] = teammate != null && teammate.HasPing == true ? teammate.PingPoint.ToString("F1") : "none";
		}

		private void PlacePing(RectTransform marker, TextMeshProUGUI text, Agent owner, Vector3 view)
		{
			Vector2 position = default;
			bool visible = owner != null && owner.HasPing == true && WorldToCanvas(owner.PingPoint, out position) == true;
			HudKit.Show(marker, visible);

			if (visible == true)
			{
				marker.anchoredPosition = position;
				HudKit.Set(text, $"{Vector3.Distance(view, owner.PingPoint):F0} m");
			}
		}
	}
}
