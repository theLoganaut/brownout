namespace TPSBR.UI
{
	using TMPro;
	using UnityEngine;
	using UnityEngine.UI;

	/// <summary>
	/// Match moments (phase 11c): ELIMINATE ALL OTHER PLAYERS once you're in the helicopter, the zone's announcements (the next zone
	/// marked, the zone closing) and YOUR SQUAD IS OUT, as a large line across the upper middle of the screen with thin rules either side.
	/// The placement banner and the helicopter's fleet shot (AirplaneAgent) go with them. Times in FeelSettings.
	/// </summary>
	public sealed partial class BrownoutHud
	{
		// PRIVATE MEMBERS

		private RectTransform   _moment;
		private TextMeshProUGUI _momentTitle;
		private TextMeshProUGUI _momentSub;
		private CanvasGroup     _momentGroup;
		private float           _momentTime = -100.0f;
		private float           _momentLength;

		private bool            _introShown;
		private bool            _zoneWasClosing;
		private bool            _zoneWasAnnounced;
		private int             _squadOutPlace;

		// PUBLIC METHODS

		/// <summary>A large announcement in the upper middle (replaces the one showing).</summary>
		public void Announce(string title, string subtitle, float time)
		{
			HudKit.Set(_momentTitle, title);
			HudKit.Set(_momentSub, subtitle ?? string.Empty);
			_momentTime   = Time.unscaledTime;
			_momentLength = Mathf.Max(0.5f, time);
			Readout["moment.last"] = title;
		}

		// PRIVATE METHODS

		private void BuildMoments()
		{
			HudSettings s = _settings;

			_moment = HudKit.Rect(transform, "Moment");
			_moment.anchorMin = new Vector2(0.0f, 0.75f);
			_moment.anchorMax = new Vector2(1.0f, 0.75f);
			_moment.sizeDelta = new Vector2(0.0f, 110.0f);
			_momentGroup = _moment.gameObject.AddComponent<CanvasGroup>();
			_momentGroup.blocksRaycasts = false;
			_momentGroup.interactable   = false;

			Image band = HudKit.Image(_moment, "Band", new Color(0.0f, 0.0f, 0.0f, 0.45f), HudKit.Glow);
			HudKit.Stretch(band.rectTransform, new Vector2(0.2f, -0.2f), new Vector2(0.8f, 1.2f));

			_momentTitle = HudKit.Label(_moment, "Title", 44.0f, TextAlignmentOptions.Center, s.Text);
			HudKit.Stretch(_momentTitle.rectTransform, new Vector2(0.0f, 0.4f), Vector2.one);
			_momentTitle.characterSpacing = 10.0f;

			_momentSub = HudKit.Label(_moment, "Subtitle", 18.0f, TextAlignmentOptions.Center, s.Team);
			HudKit.Stretch(_momentSub.rectTransform, Vector2.zero, new Vector2(1.0f, 0.38f));

			Image left  = HudKit.Image(_moment, "RuleLeft", s.Team);
			Image right = HudKit.Image(_moment, "RuleRight", s.Team);
			HudKit.Stretch(left.rectTransform,  new Vector2(0.28f, 0.4f), new Vector2(0.36f, 0.4f)).sizeDelta = new Vector2(0.0f, 2.0f);
			HudKit.Stretch(right.rectTransform, new Vector2(0.64f, 0.4f), new Vector2(0.72f, 0.4f)).sizeDelta = new Vector2(0.0f, 2.0f);

			_moment.gameObject.SetActive(false);
		}

		private void UpdateMoments(BattleRoyaleGameplayMode battleRoyale, EMatchPhase phase, bool riding)
		{
			FeelSettings feel = FeelSettings.Current;
			int localTeam = battleRoyale.GetTeamOfPlayer(_context.LocalPlayerRef);
			int place     = localTeam > 0 ? battleRoyale.GetTeamPlace(localTeam) : 0;

			if (phase == EMatchPhase.Warmup)
			{
				_introShown       = false;
				_zoneWasClosing   = false;
				_zoneWasAnnounced = false;
				_squadOutPlace    = 0;
			}

			// The helicopter: the match's goal
			if (riding == true && phase == EMatchPhase.Flight && _introShown == false)
			{
				_introShown = true;
				int teams = battleRoyale.TeamsAlive;
				Announce("ELIMINATE ALL OTHER PLAYERS", teams > 0 ? $"{teams} SQUADS" : string.Empty, feel.IntroBannerTime);
			}

			// The zone
			ShrinkingArea zone = _context.GameplayMode.ShrinkingArea;
			if (phase == EMatchPhase.Match && zone != null && zone.IsScheduled == true && zone.IsActive == true)
			{
				bool closing   = zone.IsShrinking == true;
				bool announced = zone.IsAnnounced == true && closing == false;

				if (announced == true && _zoneWasAnnounced == false)
				{
					Announce("NEW ZONE MARKED", $"CLOSING IN {FormatTime(zone.StepTimeLeft)}", feel.AnnounceTime);
				}
				if (closing == true && _zoneWasClosing == false)
				{
					Announce("THE ZONE IS CLOSING", "MOVE INTO THE NEW ZONE", feel.AnnounceTime);
				}

				_zoneWasClosing   = closing;
				_zoneWasAnnounced = announced;
			}

			// Your squad out (not the winners): before the placement banner takes over
			if (place > 1 && _squadOutPlace != place)
			{
				_squadOutPlace = place;
				Announce("YOUR SQUAD IS OUT", string.Empty, feel.AnnounceTime);
			}

			// Draw
			float age  = Time.unscaledTime - _momentTime;
			bool  show = age >= 0.0f && age <= _momentLength && (_inventoryShown == false || riding == true);
			HudKit.Show(_moment, show);
			if (show == true)
			{
				float fadeIn  = Mathf.Clamp01(age / 0.25f);
				float fadeOut = Mathf.Clamp01((_momentLength - age) / 0.5f);
				_momentGroup.alpha = Mathf.Min(fadeIn, fadeOut);
				_momentTitle.characterSpacing = Mathf.Lerp(24.0f, 10.0f, Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(age / 0.6f)));
			}

			Readout["moment"] = show == true ? _momentTitle.text : "none";
		}
	}
}
