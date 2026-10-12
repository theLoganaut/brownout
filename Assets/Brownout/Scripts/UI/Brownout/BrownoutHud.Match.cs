namespace TPSBR.UI
{
	using System.Collections.Generic;
	using TMPro;
	using UnityEngine;
	using UnityEngine.UI;

	/// <summary>
	/// Match flow on the HUD (phase 7, Phase 7 spec section 9): the zone timer (box 2: mm:ss to the next change, a blue distance bar
	/// outside the zone, a copy near the top at 1:00 left), the zone circles, the route to the zone and the flight path on the minimap and
	/// the full map, the helicopter HUD (progress bar, players aboard, the jump prompt), the altimeter during the drop, the map-edge
	/// countdown, the warm-up banner (the host's START MATCH key) and the end (placement, victory, "Match ends in").
	/// </summary>
	public sealed partial class BrownoutHud
	{
		// CONSTANTS

		private const int MinimapDashes = 40;
		private const int MapDashes     = 48;

		// PRIVATE MEMBERS

		private Image           _zoneTimerBack;
		private Image           _zoneTimerIcon;
		private TextMeshProUGUI _zoneTimerText;
		private Image           _zoneDistanceFill;
		private TextMeshProUGUI _zonePopup;
		private RectTransform   _zonePopupBox;
		private int             _zoneFeedStep = -2;
		private bool            _zoneFeedMinute;

		private Image           _minimapZone;
		private Image           _minimapZoneNext;
		private readonly List<Image> _minimapRoute      = new List<Image>(MinimapDashes);
		private readonly List<Image> _minimapPathBehind = new List<Image>(1);
		private readonly List<Image> _minimapPathAhead  = new List<Image>(MinimapDashes);
		private Image           _minimapHeli;

		private readonly List<Image> _mapPathBehind = new List<Image>(1);
		private readonly List<Image> _mapPathAhead  = new List<Image>(MapDashes);
		private Image           _mapHeli;

		private RectTransform   _flight;
		private Image           _flightFill;
		private TextMeshProUGUI _aboardText;
		private TextMeshProUGUI _jumpPrompt;

		private RectTransform   _altimeter;
		private RectTransform   _altimeterMarker;
		private TextMeshProUGUI _altimeterSpeed;

		private TextMeshProUGUI _edgeText;
		private TextMeshProUGUI _warmupText;
		private TextMeshProUGUI _endText;

		private int             _shownPlace;

		// PRIVATE METHODS

		private void BuildZoneTimer()
		{
			HudSettings s = _settings;

			_zoneTimerBack = HudKit.Panel(_zoneTimer, "Back", s.Panel);

			// Outside the zone: a blue bar filling toward the zone, with the distance on it
			_zoneDistanceFill = HudKit.Fill(_zoneTimer, "Distance", new Color(s.Ping.r, s.Ping.g, s.Ping.b, 0.75f));
			HudKit.Stretch(_zoneDistanceFill.rectTransform, Vector2.zero, Vector2.one);
			_zoneDistanceFill.gameObject.SetActive(false);

			_zoneTimerIcon = HudKit.Image(_zoneTimer, "Icon", s.Text, HudKit.Ring);
			HudKit.Fixed(_zoneTimerIcon.rectTransform, new Vector2(0.0f, 0.5f), new Vector2(22.0f, 22.0f), new Vector2(0.0f, 0.5f)).anchoredPosition = new Vector2(10.0f, 0.0f);

			_zoneTimerText = HudKit.Text(_zoneTimer, "Time", 24.0f, TextAlignmentOptions.Center, s.Text, true);
			HudKit.Stretch(_zoneTimerText.rectTransform, new Vector2(0.25f, 0.0f), Vector2.one);
			_zoneTimerText.text = "0:00";
		}

		private void BuildMatch()
		{
			HudSettings s = _settings;

			// Zone circles, the route and the flight path on the minimap (under the dots)
			_minimapZone     = HudKit.Image(_minimapRotator, "Zone", s.ZoneCurrent, HudKit.ZoneRing);
			_minimapZoneNext = HudKit.Image(_minimapRotator, "ZoneNext", s.ZoneNext, HudKit.ZoneRing);
			HudKit.Fixed(_minimapZone.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0.5f, 0.5f));
			HudKit.Fixed(_minimapZoneNext.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0.5f, 0.5f));
			_minimapZone.transform.SetSiblingIndex(1);
			_minimapZoneNext.transform.SetSiblingIndex(1);
			_minimapZone.gameObject.SetActive(false);
			_minimapZoneNext.gameObject.SetActive(false);

			MakeSegments(_minimapRotator, "Route", s.ZoneNext, MinimapDashes, _minimapRoute);
			MakeSegments(_minimapRotator, "PathBehind", s.Text, 1, _minimapPathBehind);
			MakeSegments(_minimapRotator, "PathAhead", new Color(1.0f, 0.35f, 0.3f, 1.0f), MinimapDashes, _minimapPathAhead);
			_minimapHeli = HudKit.Image(_minimapRotator, "Heli", s.Text, HudKit.Circle);
			HudKit.Fixed(_minimapHeli.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(12.0f, 12.0f), new Vector2(0.5f, 0.5f));
			_minimapHeli.gameObject.SetActive(false);

			// The flight path on the full map
			MakeSegments(_mapContent, "PathBehind", s.Text, 1, _mapPathBehind);
			MakeSegments(_mapContent, "PathAhead", new Color(1.0f, 0.35f, 0.3f, 1.0f), MapDashes, _mapPathAhead);
			_mapHeli = HudKit.Image(_mapContent, "Heli", s.Text, HudKit.Circle);
			HudKit.Fixed(_mapHeli.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(14.0f, 14.0f), new Vector2(0.5f, 0.5f));
			_mapHeli.gameObject.SetActive(false);

			// Zone popup at 1:00 left, near the top (phase 11a: a panel under the compass for 10 s)
			_zonePopupBox = HudKit.Rect(transform, "ZonePopup");
			Anchor(_zonePopupBox, new Vector2(0.5f, 0.84f), new Vector2(460.0f, 44.0f));
			HudKit.Panel(_zonePopupBox, "Back", new Color(0.0f, 0.0f, 0.0f, 0.55f));
			_zonePopup = HudKit.Label(_zonePopupBox, "Text", 24.0f, TextAlignmentOptions.Center, s.Text);
			HudKit.Stretch(_zonePopup.rectTransform, Vector2.zero, Vector2.one);
			_zonePopupBox.gameObject.SetActive(false);

			// Helicopter: flight progress under the minimap (where the zone timer goes later), players aboard, the jump prompt
			_flight = HudKit.Box(_root, "Flight", s.GetBox(HudSettings.ZoneTimer));
			HudKit.Stretch(HudKit.Image(_flight, "Back", s.Panel).rectTransform, Vector2.zero, Vector2.one);
			_flightFill = HudKit.Fill(_flight, "Fill", s.Team);
			HudKit.Stretch(_flightFill.rectTransform, new Vector2(0.0f, 0.35f), new Vector2(1.0f, 0.65f));
			_flight.gameObject.SetActive(false);

			_aboardText = HudKit.Text(transform, "Aboard", 20.0f, TextAlignmentOptions.Right, s.Text, true);
			Anchor(_aboardText.rectTransform, new Vector2(0.95f, 0.86f), new Vector2(300.0f, 30.0f), new Vector2(1.0f, 0.5f));
			_aboardText.gameObject.SetActive(false);

			_jumpPrompt = HudKit.Text(transform, "JumpPrompt", 26.0f, TextAlignmentOptions.Center, s.Text, true);
			Anchor(_jumpPrompt.rectTransform, new Vector2(0.5f, 0.22f), new Vector2(700.0f, 40.0f));
			_jumpPrompt.gameObject.SetActive(false);

			// Altimeter: a bar on the right edge, green ground marker at the bottom, the speed box sliding down it
			_altimeter = HudKit.Rect(transform, "Altimeter");
			_altimeter.anchorMin = new Vector2(0.93f, 0.25f);
			_altimeter.anchorMax = new Vector2(0.93f, 0.75f);
			_altimeter.sizeDelta = new Vector2(6.0f, 0.0f);
			HudKit.Stretch(HudKit.Image(_altimeter, "Bar", s.Dim).rectTransform, Vector2.zero, Vector2.one);
			Image ground = HudKit.Image(_altimeter, "Ground", new Color(0.3f, 0.9f, 0.4f, 1.0f));
			HudKit.Fixed(ground.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(30.0f, 6.0f), new Vector2(0.5f, 0.5f));
			_altimeterMarker = HudKit.Fixed(HudKit.Rect(_altimeter, "Marker"), new Vector2(0.5f, 1.0f), new Vector2(70.0f, 30.0f), new Vector2(1.0f, 0.5f));
			_altimeterMarker.anchoredPosition = new Vector2(-10.0f, 0.0f);
			HudKit.Stretch(HudKit.Image(_altimeterMarker, "Back", s.Panel).rectTransform, Vector2.zero, Vector2.one);
			Image tick = HudKit.Image(_altimeterMarker, "Tick", s.Text);
			HudKit.Fixed(tick.rectTransform, new Vector2(1.0f, 0.5f), new Vector2(16.0f, 3.0f), new Vector2(0.0f, 0.5f));
			_altimeterSpeed = HudKit.Text(_altimeterMarker, "Speed", 20.0f, TextAlignmentOptions.Center, s.Text, true);
			HudKit.Stretch(_altimeterSpeed.rectTransform, Vector2.zero, Vector2.one);
			TextMeshProUGUI chevrons = HudKit.Text(_altimeterMarker, "Chevrons", 16.0f, TextAlignmentOptions.Center, s.Text, true);
			HudKit.Fixed(chevrons.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(70.0f, 20.0f), new Vector2(0.5f, 1.0f));
			chevrons.text = "v v";
			_altimeter.gameObject.SetActive(false);

			// Map edge countdown, warm-up banner, end
			_edgeText = HudKit.Text(transform, "MapEdge", 30.0f, TextAlignmentOptions.Center, s.Enemy, true);
			Anchor(_edgeText.rectTransform, new Vector2(0.5f, 0.64f), new Vector2(700.0f, 90.0f));
			_edgeText.gameObject.SetActive(false);

			_warmupText = HudKit.Text(transform, "Warmup", 22.0f, TextAlignmentOptions.Center, s.Text, true);
			Anchor(_warmupText.rectTransform, new Vector2(0.5f, 0.83f), new Vector2(900.0f, 36.0f));
			_warmupText.gameObject.SetActive(false);

			_endText = HudKit.Text(transform, "MatchEnd", 22.0f, TextAlignmentOptions.Right, s.Text, true);
			Anchor(_endText.rectTransform, new Vector2(0.96f, 0.10f), new Vector2(700.0f, 64.0f), new Vector2(1.0f, 0.5f));
			_endText.gameObject.SetActive(false);
		}

		private void UpdateMatch(bool hasAgent, bool riding)
		{
			var battleRoyale = _context.GameplayMode as BattleRoyaleGameplayMode;
			var runner       = _context.Runner;

			if (battleRoyale == null || runner == null || runner.IsRunning == false || battleRoyale.Object == null || battleRoyale.Object.IsValid == false)
			{
				HideMatch();
				Readout["match.phase"] = "none";
				return;
			}

			EMatchPhase phase = battleRoyale.Phase;
			Readout["match.phase"] = phase.ToString();

			UpdateWarmup(battleRoyale, phase);
			UpdateZoneTimer(hasAgent);
			UpdateHelicopterHud(battleRoyale, riding);
			UpdateAltimeter(hasAgent);
			UpdateMapEdge(hasAgent);
			UpdateEnd(battleRoyale, phase);
			UpdateMapFlightPath(battleRoyale);
			UpdateMoments(battleRoyale, phase, riding);
		}

		private void HideMatch()
		{
			HudKit.Show(_zoneTimer, false);
			HudKit.Show(_zonePopupBox, false);
			HudKit.Show(_flight, false);
			HudKit.Show(_aboardText, false);
			HudKit.Show(_jumpPrompt, false);
			HudKit.Show(_altimeter, false);
			HudKit.Show(_edgeText, false);
			HudKit.Show(_warmupText, false);
			HudKit.Show(_endText, false);
			HudKit.Show(_moment, false);
		}

		private void UpdateWarmup(BattleRoyaleGameplayMode battleRoyale, EMatchPhase phase)
		{
			bool warmup = phase == EMatchPhase.Warmup;
			HudKit.Show(_warmupText, warmup == true && _inventoryShown == false);
			Readout["warmup"] = warmup == true ? "on" : "off";

			if (warmup == false)
				return;

			int  players = _context.NetworkGame != null ? _context.NetworkGame.ActivePlayers.Count : 0;
			bool host    = _context.Runner.IsServer == true;
			string key   = $"<color=#{Hex(_settings.Team)}>[{KeyBindings.GetKeyName(EBinding.StartMatch)}]</color>";

			HudKit.Set(_warmupText, host == true
				? $"WARM-UP   ·   {players} {(players == 1 ? "PLAYER" : "PLAYERS")}   ·   {key} START MATCH"
				: $"WARM-UP   ·   {players} {(players == 1 ? "PLAYER" : "PLAYERS")}   ·   waiting for the host to start");
#if UNITY_EDITOR
			if (BattleRoyaleGameplayMode.IsAutoStartPending == true)
			{
				HudKit.Set(_warmupText, $"WARM-UP   ·   {players} {(players == 1 ? "PLAYER" : "PLAYERS")}   ·   STARTING WHEN EVERYONE IS IN");
			}
#endif

			// Phase 11b: a matchmade match counts down once a real player is in (Enter still skips it in editor and dev builds)
			float countdown = battleRoyale.CountdownLeft;
			bool  canSkip   = countdown < 0.0f || Application.isEditor == true || Debug.isDebugBuild == true;
			if (countdown >= 0.0f)
			{
				int seconds = Mathf.CeilToInt(countdown);
				HudKit.Set(_warmupText, $"WARM-UP   ·   {players} {(players == 1 ? "PLAYER" : "PLAYERS")}   ·   MATCH STARTS IN <color=#{Hex(_settings.Team)}>{seconds / 60}:{seconds % 60:00}</color>");
			}
			Readout["warmup.countdown"] = countdown >= 0.0f ? countdown.ToString("0.0") : "off";

			// The host starts the match (only the peer with input, and not in a menu)
			if (host == true && canSkip == true && _context.HasInput == true && _context.Input != null && _context.Input.IsCursorVisible == false && KeyBindings.WasPressedThisFrame(EBinding.StartMatch) == true)
			{
				battleRoyale.StartMatch();
			}
		}

		private void UpdateZoneTimer(bool hasAgent)
		{
			ShrinkingArea zone = _context.GameplayMode.ShrinkingArea;
			bool shown = hasAgent == true && zone != null && zone.IsScheduled == true && zone.IsActive == true && IsVictoryShown == false;

			HudKit.Show(_zoneTimer, shown);
			if (shown == false)
			{
				HudKit.Show(_zonePopupBox, false);
				Readout["zone.timer"]   = "hidden";
				Readout["zone.outside"] = "no";
				return;
			}

			float   timeLeft = zone.StepTimeLeft;
			bool    closing  = zone.IsShrinking == true;
			string  time     = zone.StepEndTick > 0 ? FormatTime(timeLeft) : "--:--";

			Vector3 position = _agent.transform.position;
			Vector3 offset   = position - zone.Center;
			offset.y = 0.0f;
			float   outside  = offset.magnitude - zone.Radius;

			bool isOutside = outside > 0.0f;
			HudKit.Show(_zoneDistanceFill, isOutside);
			_zoneTimerIcon.color = isOutside == true ? _settings.Text : (closing == true ? _settings.Enemy : _settings.Text);

			if (isOutside == true)
			{
				// Fills up as you get closer (full at the edge)
				_zoneDistanceFill.fillAmount = Mathf.Clamp01(1.0f - outside / 250.0f);
				HudKit.Set(_zoneTimerText, $"{outside:F0} m  ·  {time}");
			}
			else
			{
				HudKit.Set(_zoneTimerText, closing == true ? $"<color=#{Hex(_settings.Enemy)}>{time}</color>" : time);
			}

			// At 1:00 left a copy pops up near the top for 10 s
			bool popup = zone.StepEndTick > 0 && timeLeft <= 60.0f && timeLeft > 50.0f && closing == false;
			HudKit.Show(_zonePopupBox, popup);
			if (popup == true)
			{
				HudKit.Set(_zonePopup, $"Zone closing in  <color=#{Hex(_settings.Enemy)}>{time}</color>");
			}

			// Phase 11a: the zone's moments in the system feed (once per step)
			if (zone.ScheduleStep != _zoneFeedStep)
			{
				_zoneFeedStep     = zone.ScheduleStep;
				_zoneFeedMinute   = false;
				if (closing == true)
				{
					AddSystemRow("The zone is closing");
				}
			}
			if (popup == true && _zoneFeedMinute == false)
			{
				_zoneFeedMinute = true;
				AddSystemRow("Zone closing in 1:00");
			}

			Readout["zone.timer"]   = $"{zone.StepPhase} {time}";
			Readout["zone.outside"] = isOutside == true ? $"{outside:F0} m" : "no";
			Readout["zone.radius"]  = zone.Radius.ToString("F0");
			Readout["zone.damage"]  = zone.StepDamage.ToString("F0");
		}

		private void UpdateHelicopterHud(BattleRoyaleGameplayMode battleRoyale, bool riding)
		{
			Airplane heli = battleRoyale.Airplane;
			bool     show = riding == true && heli != null && heli.Object != null && heli.Object.IsValid == true;

			HudKit.Show(_flight, show);
			HudKit.Show(_aboardText, show);
			HudKit.Show(_jumpPrompt, show);

			if (show == false)
			{
				Readout["heli.prompt"] = "hidden";
				return;
			}

			_flightFill.rectTransform.anchorMax = new Vector2(heli.PathProgress, 0.65f);
			HudKit.Set(_aboardText, $"{battleRoyale.Aboard} ABOARD");

			string key = $"<color=#{Hex(_settings.Team)}>[{KeyBindings.GetKeyName(EBinding.Jump)}]</color>";
			HudKit.Set(_jumpPrompt, heli.DropEnabled == true ? $"{key}  JUMP" : "<alpha=#88>Over the map soon: wait to jump");

			Readout["heli.prompt"]   = heli.DropEnabled == true ? "jump" : "wait";
			Readout["heli.progress"] = heli.PathProgress.ToString("F2");
		}

		private void UpdateAltimeter(bool hasAgent)
		{
			bool show = hasAgent == true && _agent.Movement.IsDropping == true;
			HudKit.Show(_altimeter, show);

			if (show == false)
			{
				Readout["altimeter"] = "hidden";
				return;
			}

			var     kcc      = _agent.Character.CharacterController;
			Vector3 feet     = _agent.transform.position;
			float   top      = Mathf.Max(1.0f, MatchSettings.Current.HelicopterHeight);
			float   height   = feet.y;

			if (_context.Runner.GetPhysicsScene().Raycast(feet + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, top + 50.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
			{
				height = hit.distance - 0.1f;
			}

			float share = Mathf.Clamp01(height / top);
			_altimeterMarker.anchorMin = _altimeterMarker.anchorMax = new Vector2(0.5f, share);
			_altimeterMarker.anchoredPosition = new Vector2(-10.0f, 0.0f);

			float speed = kcc.RenderData.RealVelocity.magnitude;
			HudKit.Set(_altimeterSpeed, speed.ToString("F0"));

			Readout["altimeter"] = $"{height:F0} m, {speed:F0} m/s, {kcc.FixedData.MoveMode}";
		}

		private void UpdateMapEdge(bool hasAgent)
		{
			float timeLeft = hasAgent == true && _agent.Health.IsAlive == true ? _agent.EdgeTimeLeft : -1.0f;
			bool  show     = timeLeft >= 0.0f && IsVictoryShown == false;

			HudKit.Show(_edgeText, show);
			Readout["edge"] = show == true ? timeLeft.ToString("F1") : "inside";

			if (show == false)
				return;

			HudKit.Set(_edgeText, timeLeft > 0.0f
				? $"RETURN TO THE MAP\n<size=44>{Mathf.CeilToInt(timeLeft)}</size>"
				: $"RETURN TO THE MAP\n<size=20>taking {MatchSettings.Current.EdgeDamage:F0} damage a second</size>");
		}

		private void UpdateEnd(BattleRoyaleGameplayMode battleRoyale, EMatchPhase phase)
		{
			int localTeam = battleRoyale.GetTeamOfPlayer(_context.LocalPlayerRef);
			int place     = localTeam > 0 ? battleRoyale.GetTeamPlace(localTeam) : 0;

			if (phase == EMatchPhase.Warmup)
			{
				_shownPlace = 0;
			}

			UpdateSummary(battleRoyale, phase, place);

			// Your team's placement when it goes out; the winners keep the VICTORY banner until the match ends
			if (place > 0 && (place != _shownPlace || (place == 1 && IsVictoryShown == false)))
			{
				_shownPlace = place;
				ShowVictory(place);
			}

			Readout["match.place"] = place.ToString();

			bool ended = phase == EMatchPhase.Ended;
			HudKit.Show(_endText, ended);
			if (ended == false)
			{
				Readout["end"] = "none";
				return;
			}

			string winners = GetWinnerNames(battleRoyale);
			HudKit.Set(_endText, $"{winners}\n<size=18><color=#{Hex(_settings.Dim)}>MATCH ENDS IN</color></size>  {FormatTime(battleRoyale.EndTimeLeft)}");
			Readout["end"] = $"winner team {battleRoyale.WinningTeam}, {battleRoyale.EndTimeLeft:F1} s";
		}

		private string GetWinnerNames(BattleRoyaleGameplayMode battleRoyale)
		{
			int team = battleRoyale.WinningTeam;
			if (team <= 0)
				return "NO WINNER";

			string names = string.Empty;
			if (_context.NetworkGame != null)
			{
				foreach (Player player in _context.NetworkGame.ActivePlayers)
				{
					if (player == null || player.Object == null || battleRoyale.GetTeamOfPlayer(player.Object.InputAuthority) != team)
						continue;

					names += names.Length > 0 ? $" & {GetName(player.Object.InputAuthority)}" : GetName(player.Object.InputAuthority);
				}
			}

			// Bots on the team (phase 9b): a bot duo, or a lone player's bot teammate
			for (int i = 0; i < 2; ++i)
			{
				byte bot = battleRoyale.GetTeamBot(team, i);
				if (bot > 0)
				{
					names += names.Length > 0 ? $" & {GetBotName(bot)}" : GetBotName(bot);
				}
			}

			return names.Length > 0 ? $"WINNERS  <color=#{Hex(_settings.Team)}>{names}</color>" : $"WINNERS  <color=#{Hex(_settings.Enemy)}>DUMMY SQUAD {team}</color>";
		}

		/// <summary>In the helicopter: the minimap with the flight path, the compass, counters and the feed; nothing else.</summary>
		private void UpdateRiding()
		{
			HudKit.Show(_center, false);
			HudKit.Show(_world, false);
			HudKit.Show(_bottom, false);
			HudKit.Show(_minimapViewport.parent, true);
			HudKit.Show(_compassStrip.parent, true);
			HudKit.Show(_counters.rectTransform.parent, true);
			HudKit.Show(_killFeed, true);

			float   heading = GetHeading();
			Vector3 center  = _context.WaitingAgentTransform.position;

			UpdateMinimap(null, heading, center);
			UpdateCompass(null, heading);
			UpdateCounters();
			UpdateKillFeed();
		}

		/// <summary>From UpdateMinimap: the zone circles, the dashed route to the zone when outside it, and the flight path while it matters.</summary>
		private void UpdateMinimapMatch(Vector3 center, float pixelsPerMeter)
		{
			float clipRadius = _minimapViewport.rect.width * 0.75f;

			ShrinkingArea zone = _context.GameplayMode != null ? _context.GameplayMode.ShrinkingArea : null;
			bool zoneShown = zone != null && zone.IsActive == true && zone.IsScheduled == true;

			PlaceMinimapRing(_minimapZone, zoneShown, zoneShown ? zone.Center : default, zoneShown ? zone.Radius : 0.0f, center, pixelsPerMeter);
			PlaceMinimapRing(_minimapZoneNext, zoneShown && zone.IsAnnounced, zoneShown ? zone.ShrinkCenter : default, zoneShown ? zone.ShrinkRadius : 0.0f, center, pixelsPerMeter);

			// Outside the zone: a dashed line to its nearest edge
			bool route = false;
			if (zoneShown == true && _agent != null)
			{
				Vector3 offset = center - zone.Center;
				offset.y = 0.0f;
				float distance = offset.magnitude;

				if (distance > zone.Radius && distance > 0.01f)
				{
					Vector3 edge = zone.Center + offset / distance * zone.Radius;
					DrawLine(_minimapRoute, Vector2.zero, ToMinimap(edge, center, pixelsPerMeter), 8.0f, 6.0f, 3.0f, clipRadius);
					route = true;
				}
			}

			if (route == false)
			{
				HideSegments(_minimapRoute);
			}

			Readout["minimap.route"] = route == true ? "shown" : "hidden";

			// The flight path: solid behind the helicopter, dashed ahead, until you're out of it
			var  battleRoyale = _context.GameplayMode as BattleRoyaleGameplayMode;
			Airplane heli     = battleRoyale != null ? battleRoyale.Airplane : null;
			bool showPath     = heli != null && heli.Object != null && heli.Object.IsValid == true && heli.IsStraight == true && (_agent == null || _agent.Movement.IsDropping == true);

			if (showPath == true)
			{
				Vector2 start = ToMinimap(heli.PathStart, center, pixelsPerMeter);
				Vector2 now   = ToMinimap(heli.transform.position, center, pixelsPerMeter);
				Vector2 end   = ToMinimap(heli.PathEnd, center, pixelsPerMeter);

				DrawLine(_minimapPathBehind, start, now, 0.0f, 0.0f, 3.0f, clipRadius);
				DrawLine(_minimapPathAhead, now, end, 10.0f, 7.0f, 3.0f, clipRadius);
				PlaceMinimapDot(_minimapHeli, true, heli.transform.position, center, pixelsPerMeter);
			}
			else
			{
				HideSegments(_minimapPathBehind);
				HideSegments(_minimapPathAhead);
				HudKit.Show(_minimapHeli, false);
			}

			Readout["minimap.path"] = showPath == true ? "shown" : "hidden";
		}

		/// <summary>The flight path on the full map (open in the helicopter or mid-glide).</summary>
		private void UpdateMapFlightPath(BattleRoyaleGameplayMode battleRoyale)
		{
			Airplane heli = battleRoyale.Airplane;
			bool     show = _mapOpen == true && heli != null && heli.Object != null && heli.Object.IsValid == true && heli.IsStraight == true &&
			                (_agent == null || _agent.Object == null || _agent.Object.IsValid == false || _agent.Movement.IsDropping == true);

			if (show == false)
			{
				HideSegments(_mapPathBehind);
				HideSegments(_mapPathAhead);
				HudKit.Show(_mapHeli, false);
				return;
			}

			Vector2 size  = _mapContent.sizeDelta;
			Vector2 start = (WorldToMap(heli.PathStart) - new Vector2(0.5f, 0.5f)) * size;
			Vector2 now   = (WorldToMap(heli.transform.position) - new Vector2(0.5f, 0.5f)) * size;
			Vector2 end   = (WorldToMap(heli.PathEnd) - new Vector2(0.5f, 0.5f)) * size;

			DrawLine(_mapPathBehind, start, now, 0.0f, 0.0f, 3.0f, 0.0f);
			DrawLine(_mapPathAhead, now, end, Mathf.Max(8.0f, size.x / 60.0f), Mathf.Max(6.0f, size.x / 90.0f), 3.0f, 0.0f);

			HudKit.Show(_mapHeli, true);
			_mapHeli.rectTransform.anchoredPosition = now;
		}

		private void PlaceMinimapRing(Image ring, bool visible, Vector3 position, float radius, Vector3 center, float pixelsPerMeter)
		{
			HudKit.Show(ring, visible && radius > 0.5f);
			if (visible == false || radius <= 0.5f)
				return;

			ring.rectTransform.anchoredPosition = ToMinimap(position, center, pixelsPerMeter);
			ring.rectTransform.sizeDelta        = Vector2.one * radius * 2.0f * pixelsPerMeter;
		}

		private static Vector2 ToMinimap(Vector3 position, Vector3 center, float pixelsPerMeter)
		{
			return new Vector2(position.x - center.x, position.z - center.z) * pixelsPerMeter;
		}

		private static void MakeSegments(RectTransform parent, string name, Color color, int count, List<Image> pool)
		{
			for (int i = 0; i < count; ++i)
			{
				Image segment = HudKit.Image(parent, $"{name}{i}", color);
				RectTransform rect = segment.rectTransform;
				rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
				rect.pivot     = new Vector2(0.0f, 0.5f);
				segment.gameObject.SetActive(false);
				pool.Add(segment);
			}
		}

		private static void HideSegments(List<Image> pool)
		{
			for (int i = 0; i < pool.Count; ++i)
			{
				HudKit.Show(pool[i], false);
			}
		}

		/// <summary>
		/// A line from a to b in the parent's local space: solid with one segment (dash 0), else dashes. With clipRadius &gt; 0 only the part
		/// within that distance of the origin is drawn (the minimap's visible circle), so long lines don't run out of dashes off-screen.
		/// </summary>
		private static void DrawLine(List<Image> pool, Vector2 a, Vector2 b, float dash, float gap, float thickness, float clipRadius)
		{
			if (clipRadius > 0.0f && ClipToCircle(ref a, ref b, clipRadius) == false)
			{
				HideSegments(pool);
				return;
			}

			Vector2 delta  = b - a;
			float   length = delta.magnitude;
			int     used   = 0;

			if (length > 0.5f)
			{
				Vector2    direction = delta / length;
				Quaternion rotation  = Quaternion.Euler(0.0f, 0.0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);

				if (dash <= 0.0f)
				{
					SetSegment(pool[0], a, length, thickness, rotation);
					used = 1;
				}
				else
				{
					float step = Mathf.Max(dash + gap, length / pool.Count);
					for (float t = 0.0f; t < length && used < pool.Count; t += step)
					{
						SetSegment(pool[used], a + direction * t, Mathf.Min(dash, length - t), thickness, rotation);
						++used;
					}
				}
			}

			for (int i = used; i < pool.Count; ++i)
			{
				HudKit.Show(pool[i], false);
			}
		}

		private static void SetSegment(Image segment, Vector2 start, float length, float thickness, Quaternion rotation)
		{
			HudKit.Show(segment, true);
			RectTransform rect = segment.rectTransform;
			rect.anchoredPosition = start;
			rect.sizeDelta        = new Vector2(length, thickness);
			rect.localRotation    = rotation;
		}

		/// <summary>Cuts the segment to the circle of the given radius around the origin; false when it misses it.</summary>
		private static bool ClipToCircle(ref Vector2 a, ref Vector2 b, float radius)
		{
			Vector2 d  = b - a;
			float   qa = Vector2.Dot(d, d);
			if (qa < 0.0001f)
				return a.sqrMagnitude <= radius * radius;

			float qb   = 2.0f * Vector2.Dot(a, d);
			float qc   = Vector2.Dot(a, a) - radius * radius;
			float disc = qb * qb - 4.0f * qa * qc;
			if (disc < 0.0f)
				return false;

			float root = Mathf.Sqrt(disc);
			float t0   = Mathf.Max(0.0f, (-qb - root) / (2.0f * qa));
			float t1   = Mathf.Min(1.0f, (-qb + root) / (2.0f * qa));
			if (t0 >= t1)
				return false;

			Vector2 start = a + d * t0;
			b = a + d * t1;
			a = start;
			return true;
		}

		private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size)
		{
			Anchor(rect, anchor, size, new Vector2(0.5f, 0.5f));
		}

		private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 pivot)
		{
			rect.anchorMin = rect.anchorMax = anchor;
			rect.pivot     = pivot;
			rect.sizeDelta = size;
			rect.anchoredPosition = Vector2.zero;
		}

		private static string FormatTime(float seconds)
		{
			int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
			return $"{total / 60}:{total % 60:00}";
		}
	}
}
