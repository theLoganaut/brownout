namespace TPSBR.UI
{
	using Fusion;
	using TMPro;
	using UnityEngine;
	using UnityEngine.UI;

	/// <summary>
	/// Spectating and victory (phase 6b). Spectating: "SPECTATING &lt;name&gt;" under the compass while the HUD shows the watched player's
	/// values (the sample's own SPECTATING label is switched off). Victory: the `#1 | Squad Placed | VICTORY` banner above center with the
	/// HUD cleared except the feed, as in the reference; shown by a test tool now (Brownout → Test → HUD → Show Victory Banner), wired to
	/// the match's end in phase 7.
	/// </summary>
	public sealed partial class BrownoutHud
	{
		// PRIVATE MEMBERS

		private RectTransform   _spectating;
		private TextMeshProUGUI _spectatingText;

		private RectTransform   _victory;
		private TextMeshProUGUI _victoryText;
		private float           _victoryUntil = -1.0f;

		// Phase 11b: the match summary once your team is out (or the match is over): place, kills, damage dealt, time alive, BACK TO MENU
		private RectTransform   _summary;
		private TextMeshProUGUI _summaryTitle;
		private TextMeshProUGUI _summaryStats;
		private Button          _summaryBack;
		private Button          _summaryWatch;
		private bool            _summaryDismissed;
		private bool            _summaryShown;
		private float           _totalDamageDealt;
		private float           _aliveFrom = -1.0f;
		private float           _aliveUntil = -1.0f;
		private bool            _stopOnDisconnectSet;

		// PUBLIC METHODS

		public bool IsVictoryShown => Time.unscaledTime < _victoryUntil;

		/// <summary>Shows the victory banner for HudSettings.VictoryTime (place 1 = VICTORY). Place 0 hides it.</summary>
		public void ShowVictory(int place)
		{
			if (place <= 0)
			{
				_victoryUntil = -1.0f;
				return;
			}

			string result = place == 1 ? "VICTORY" : "ELIMINATED";
			_victoryText.text = $"<color=#{Hex(_settings.Team)}>#{place}</color>   |   Squad Placed   |   <color=#{Hex(_settings.Team)}>{result}</color>";
			_victoryUntil     = Time.unscaledTime + Mathf.Max(1.0f, _settings.VictoryTime);
		}

		// PRIVATE METHODS

		private void BuildScreens()
		{
			HudSettings s = _settings;

			// Spectating: a strip under the compass
			_spectating = HudKit.Rect(transform, "Spectating");
			_spectating.anchorMin = _spectating.anchorMax = _spectating.pivot = new Vector2(0.5f, 1.0f);
			_spectating.sizeDelta = new Vector2(520.0f, 54.0f);
			_spectating.anchoredPosition = new Vector2(0.0f, -132.0f);
			HudKit.Stretch(HudKit.Image(_spectating, "Back", s.Panel).rectTransform, Vector2.zero, Vector2.one);
			_spectatingText = HudKit.Text(_spectating, "Text", 22.0f, TextAlignmentOptions.Center, s.Text, true);
			HudKit.Stretch(_spectatingText.rectTransform, Vector2.zero, Vector2.one);
			_spectating.gameObject.SetActive(false);

			// Victory: a wide dark band above center with an emblem block under it
			_victory = HudKit.Rect(transform, "Victory");
			_victory.anchorMin = new Vector2(0.0f, 0.62f);
			_victory.anchorMax = new Vector2(1.0f, 0.62f);
			_victory.sizeDelta = new Vector2(0.0f, 96.0f);
			Image band = HudKit.Image(_victory, "Band", new Color(0.0f, 0.0f, 0.0f, 0.65f));
			HudKit.Stretch(band.rectTransform, new Vector2(0.18f, 0.0f), new Vector2(0.82f, 1.0f));
			_victoryText = HudKit.Text(_victory, "Text", 46.0f, TextAlignmentOptions.Center, s.Text, true);
			HudKit.Stretch(_victoryText.rectTransform, Vector2.zero, Vector2.one);
			_victoryText.characterSpacing = 6.0f;
			Image emblem = HudKit.Image(_victory, "Emblem", s.Team);
			HudKit.Fixed(emblem.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(70.0f, 70.0f), new Vector2(0.5f, 1.0f)).anchoredPosition = new Vector2(0.0f, -40.0f);
			emblem.rectTransform.localRotation = Quaternion.Euler(0.0f, 0.0f, 45.0f);
			_victory.gameObject.SetActive(false);

			BuildSummary();
		}

		private void BuildSummary()
		{
			HudSettings s = _settings;

			_summary = HudKit.Rect(transform, "Summary");
			HudKit.Stretch(_summary, Vector2.zero, Vector2.one);
			HudKit.Stretch(HudKit.Image(_summary, "Shade", new Color(0.0f, 0.0f, 0.0f, 0.55f)).rectTransform, Vector2.zero, Vector2.one);

			RectTransform panel = HudKit.Rect(_summary, "Panel");
			HudKit.Fixed(panel, new Vector2(0.5f, 0.5f), new Vector2(560.0f, 420.0f), new Vector2(0.5f, 0.5f));
			HudKit.Panel(panel, "Back", new Color(0.03f, 0.035f, 0.04f, 0.94f));

			_summaryTitle = HudKit.Label(panel, "Title", 40.0f, TextAlignmentOptions.Center, s.Text);
			HudKit.Stretch(_summaryTitle.rectTransform, new Vector2(0.0f, 0.78f), new Vector2(1.0f, 0.96f));

			_summaryStats = HudKit.Text(panel, "Stats", 24.0f, TextAlignmentOptions.Center, s.Text);
			HudKit.Stretch(_summaryStats.rectTransform, new Vector2(0.08f, 0.36f), new Vector2(0.92f, 0.76f));
			_summaryStats.lineSpacing = 18.0f;

			_summaryBack  = SummaryButton(panel, "BackToMenu", "BACK TO MENU", new Vector2(0.08f, 0.07f), new Vector2(0.54f, 0.24f), true);
			_summaryWatch = SummaryButton(panel, "Watch", "KEEP WATCHING", new Vector2(0.57f, 0.07f), new Vector2(0.92f, 0.24f), false);
			_summaryBack.onClick.AddListener(OnSummaryBack);
			_summaryWatch.onClick.AddListener(() => _summaryDismissed = true);

			// The HUD canvas has no raycaster of its own (it never needed clicks before)
			if (gameObject.GetComponent<GraphicRaycaster>() == null)
			{
				gameObject.AddComponent<GraphicRaycaster>();
			}

			_summary.gameObject.SetActive(false);
		}

		private Button SummaryButton(RectTransform parent, string name, string text, Vector2 min, Vector2 max, bool primary)
		{
			RectTransform rect = HudKit.Stretch(HudKit.Rect(parent, name), min, max);
			Image back = HudKit.Image(rect, "Back", Color.white);
			HudKit.Stretch(back.rectTransform, Vector2.zero, Vector2.one);
			back.raycastTarget = true;

			var button = rect.gameObject.AddComponent<Button>();
			button.targetGraphic = back;
			ColorBlock colors = button.colors;
			colors.normalColor      = primary == true ? _settings.Team : new Color(1.0f, 1.0f, 1.0f, 0.08f);
			colors.highlightedColor = primary == true ? Color.Lerp(_settings.Team, Color.white, 0.35f) : new Color(1.0f, 1.0f, 1.0f, 0.18f);
			colors.pressedColor     = primary == true ? Color.Lerp(_settings.Team, Color.black, 0.2f) : new Color(1.0f, 1.0f, 1.0f, 0.25f);
			colors.selectedColor    = colors.normalColor;
			button.colors = colors;

			TextMeshProUGUI label = HudKit.Label(rect, "Text", 18.0f, TextAlignmentOptions.Center, primary == true ? new Color(0.08f, 0.08f, 0.08f, 1.0f) : _settings.Text);
			HudKit.Stretch(label.rectTransform, Vector2.zero, Vector2.one);
			label.text = text;
			return button;
		}

		private void OnSummaryBack()
		{
			_context.Input.RequestCursorVisibility(false, ECursorStateSource.Summary);
			Global.Networking.StopGame();
		}

		/// <summary>Phase 11b: counts the local player's damage for the summary (from OnHitPerformed).</summary>
		private void AddDamageDealt(float amount)
		{
			if (amount > 0.0f)
			{
				_totalDamageDealt += amount;
			}
		}

		private void UpdateSummary(BattleRoyaleGameplayMode battleRoyale, EMatchPhase phase, int place)
		{
			Player local = _context.NetworkGame != null ? _context.NetworkGame.GetPlayer(_context.LocalPlayerRef) : null;

			if (phase == EMatchPhase.Warmup)
			{
				_summaryDismissed = false;
				_totalDamageDealt = 0.0f;
				_aliveFrom        = -1.0f;
				_aliveUntil       = -1.0f;
			}
			else
			{
				if (_aliveFrom < 0.0f)
				{
					_aliveFrom = Time.unscaledTime;
				}
				if (_aliveUntil < 0.0f && (place > 0 || (local != null && local.Statistics.IsAlive == false && phase == EMatchPhase.Match)))
				{
					_aliveUntil = Time.unscaledTime;
				}
			}

			bool finished = _context.GameplayMode.State == GameplayMode.EState.Finished;
			if (finished == true && _stopOnDisconnectSet == false)
			{
				_stopOnDisconnectSet = true;
				Global.Networking.StopGameOnDisconnect();
			}

			bool show = (place > 0 && IsVictoryShown == false && _summaryDismissed == false) || finished == true;
			HudKit.Show(_summary, show);
			HudKit.Show(_summaryWatch, finished == false);
			if (show != _summaryShown)
			{
				_summaryShown = show;
				_context.Input.RequestCursorVisibility(show, ECursorStateSource.Summary);
			}

			Readout["summary"] = show == true ? "shown" : "hidden";
			if (show == false)
				return;

			int   kills = local != null ? local.Statistics.Kills : 0;
			float alive = _aliveFrom >= 0.0f ? (_aliveUntil >= 0.0f ? _aliveUntil : Time.unscaledTime) - _aliveFrom : 0.0f;
			int   teams = Mathf.Max(place, battleRoyale.TeamsAlive + place);

			HudKit.Set(_summaryTitle, place == 1 ? $"<color=#{Hex(_settings.Team)}>VICTORY</color>" : (place > 0 ? $"<color=#{Hex(_settings.Team)}>#{place}</color>  SQUAD PLACED" : "MATCH OVER"));
			HudKit.Set(_summaryStats,
				$"<color=#{Hex(_settings.Dim)}>KILLS</color>   {kills}\n" +
				$"<color=#{Hex(_settings.Dim)}>DAMAGE DEALT</color>   {Mathf.RoundToInt(_totalDamageDealt)}\n" +
				$"<color=#{Hex(_settings.Dim)}>TIME ALIVE</color>   {FormatTime(alive)}");

			Readout["summary.stats"] = $"place {place}, kills {kills}, damage {Mathf.RoundToInt(_totalDamageDealt)}, alive {alive:F0} s (of {teams} teams)";
		}

		private void UpdateScreens(bool hasAgent)
		{
			// Spectating: the HUD follows someone other than you
			PlayerRef observed  = hasAgent == true ? _agent.Object.InputAuthority : PlayerRef.None;
			bool      spectating = hasAgent == true && _context.LocalPlayerRef.IsRealPlayer == true && observed != _context.LocalPlayerRef;
			HudKit.Show(_spectating, spectating && IsVictoryShown == false);
			if (spectating == true)
			{
				Player local      = _context.NetworkGame != null ? _context.NetworkGame.GetPlayer(_context.LocalPlayerRef) : null;
				bool   eliminated = local != null && local.Statistics.IsEliminated == true;
				HudKit.Set(_spectatingText, $"SPECTATING  <color=#{Hex(_settings.Team)}>{GetName(_agent)}</color>{(eliminated == true ? "\n<size=14>[Z] / [X] switch player</size>" : string.Empty)}");
			}
			Readout["spectating"] = spectating == true ? GetName(_agent) : "none";

			// Victory: everything but the feed clears
			bool victory = IsVictoryShown;
			HudKit.Show(_victory, victory);
			Readout["victory"] = victory == true ? _victoryText.text : "none";

			if (hasAgent == true)
			{
				// The inventory covers the compass, counters and kill feed (stash title, weapon panel), as in the reference
				InventoryScreen screen    = GetInventoryScreen();
				bool            inventory = screen != null && screen.IsOpen == true && _context.HasInput == true;

				HudKit.Show(_center, victory == false);
				HudKit.Show(_world, victory == false);
				HudKit.Show(_minimapViewport.parent, victory == false);
				HudKit.Show(_compassStrip.parent, victory == false && inventory == false);
				HudKit.Show(_counters.rectTransform.parent, victory == false && inventory == false);
				HudKit.Show(_killFeed, inventory == false);
			}
		}
	}
}
