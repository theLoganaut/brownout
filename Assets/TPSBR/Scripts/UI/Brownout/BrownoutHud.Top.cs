namespace TPSBR.UI
{
	using System.Collections.Generic;
	using TMPro;
	using UnityEngine;
	using UnityEngine.UI;

	/// <summary>Top of the HUD (phase 6a): minimap (box 1), zone timer stub (2), compass (3), counters (4), kill feed (5).</summary>
	public sealed partial class BrownoutHud
	{
		// PRIVATE MEMBERS

		private struct FeedRow
		{
			public string Text;
			public float  Time;
			/// <summary>Kill rows (phase 11a): killer, the gun's silhouette (or a cause in text), victim.</summary>
			public string Killer;
			public Sprite Icon;
			public string Cause;
			public string Victim;
		}

		private sealed class KillRowView
		{
			public RectTransform   Root;
			public TextMeshProUGUI Killer;
			public Image           Icon;
			public TextMeshProUGUI Cause;
			public TextMeshProUGUI Victim;
		}

		private RectTransform   _minimapBox;
		private RectTransform   _minimapViewport;
		private RectTransform   _minimapRotator;
		private RawImage        _minimapImage;
		private Image           _minimapPlayer;
		private Image           _minimapTeammate;
		private Image           _minimapPing;
		private Image           _minimapTeammatePing;
		private readonly Image[] _minimapSixthSense = new Image[AgentGear.MaxBlinkSpots];

		private RectTransform   _zoneTimer;

		private RectTransform   _compassStrip;
		private TextMeshProUGUI _compassHeading;
		private readonly TextMeshProUGUI[] _compassTicks = new TextMeshProUGUI[24];
		private Image           _compassTeammate;
		private Image           _compassPing;
		private Image           _compassTeammatePing;

		private TextMeshProUGUI _counters;
		private TextMeshProUGUI _aliveText;
		private TextMeshProUGUI _killsText;
		private float           _nextCounterTime;
		private string          _countersText = string.Empty;

		private RectTransform   _killFeed;
		private KillRowView[]   _killFeedRows;
		private readonly List<FeedRow> _feed = new List<FeedRow>(8);

		// Phase 11a: the yellow system feed on the left (reference: "a yellow system/chat feed on the left"), newest at the bottom
		private const int       SYSTEM_ROWS = 5;
		private RectTransform   _systemFeed;
		private readonly TextMeshProUGUI[] _systemRows = new TextMeshProUGUI[SYSTEM_ROWS];
		private readonly List<FeedRow> _system = new List<FeedRow>(SYSTEM_ROWS);

		// PRIVATE METHODS

		private void BuildTop()
		{
			HudSettings s = _settings;

			// 1 Minimap: the generated level's top-down render, player-up, in a clipped square
			RectTransform minimap = HudKit.Box(_root, "Minimap", s.GetBox(HudSettings.Minimap));
			HudKit.Panel(minimap, "Back", s.Panel);
			_minimapBox = minimap;

			_minimapViewport = HudKit.Rect(minimap, "Viewport");
			HudKit.Stretch(_minimapViewport, Vector2.zero, Vector2.one);
			// A stencil mask: RectMask2D doesn't clip the turning map
			Image maskImage = _minimapViewport.gameObject.AddComponent<Image>();
			maskImage.sprite = HudKit.White;
			maskImage.raycastTarget = false;
			_minimapViewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

			_minimapRotator = HudKit.Fixed(HudKit.Rect(_minimapViewport, "Rotator"), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0.5f, 0.5f));

			_minimapImage = HudKit.Rect(_minimapRotator, "Map").gameObject.AddComponent<RawImage>();
			_minimapImage.raycastTarget = false;
			_minimapImage.color = new Color(1.0f, 1.0f, 1.0f, 0.85f);
			HudKit.Fixed(_minimapImage.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0.5f, 0.5f));

			_minimapTeammate     = MinimapDot("Teammate", s.Team, 10.0f);
			_minimapPing         = MinimapDot("Ping", s.Ping, 10.0f);
			_minimapTeammatePing = MinimapDot("TeammatePing", s.Ping, 10.0f);
			for (int i = 0; i < _minimapSixthSense.Length; ++i)
			{
				_minimapSixthSense[i] = MinimapDot($"SixthSense{i}", s.Enemy, 12.0f);
			}

			// The player: a notched triangle with a dark outline fixed at the center, pointing up (the map turns under it)
			_minimapPlayer = HudKit.Image(_minimapViewport, "Player", s.Team, HudKit.Arrow);
			HudKit.Fixed(_minimapPlayer.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(22.0f, 22.0f), new Vector2(0.5f, 0.5f));

			// Phase 11a (reference): a thick dark frame with a thin light line inside, over the map's edge
			Image frame = HudKit.Image(minimap, "Frame", Color.white, HudKit.MapFrame);
			frame.type = Image.Type.Sliced;
			HudKit.Stretch(frame.rectTransform, Vector2.zero, Vector2.one);
			frame.rectTransform.offsetMin = new Vector2(-4.0f, -4.0f);
			frame.rectTransform.offsetMax = new Vector2(4.0f, 4.0f);

			// 2 Zone timer (wired in phase 7, BrownoutHud.Match)
			_zoneTimer = HudKit.Box(_root, "ZoneTimer", s.GetBox(HudSettings.ZoneTimer));
			BuildZoneTimer();
			_zoneTimer.gameObject.SetActive(false);

			// 3 Compass: ticks every 15°, letters at the cardinals, the heading in a box above the center, pins
			RectTransform compass = HudKit.Box(_root, "Compass", s.GetBox(HudSettings.Compass));

			_compassStrip = HudKit.Rect(compass, "Strip");
			HudKit.Stretch(_compassStrip, new Vector2(0.0f, 0.0f), new Vector2(1.0f, 0.55f));
			HudKit.Stretch(HudKit.Image(_compassStrip, "Back", s.Panel).rectTransform, Vector2.zero, Vector2.one);

			for (int i = 0; i < _compassTicks.Length; ++i)
			{
				int    degrees  = i * 15;
				bool   cardinal = degrees % 90 == 0;
				string label    = degrees == 0 ? "N" : degrees == 90 ? "E" : degrees == 180 ? "S" : degrees == 270 ? "W" : degrees.ToString();

				TextMeshProUGUI tick = HudKit.Text(_compassStrip, $"Tick{degrees}", cardinal == true ? 26.0f : 16.0f, TextAlignmentOptions.Center, cardinal == true ? s.Text : s.Dim, cardinal);
				HudKit.Fixed(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(60.0f, 30.0f), new Vector2(0.5f, 0.5f));
				tick.text = label;
				_compassTicks[i] = tick;
			}

			Image centerMark = HudKit.Image(_compassStrip, "CenterMark", s.Text);
			HudKit.Fixed(centerMark.rectTransform, new Vector2(0.5f, 1.0f), new Vector2(2.0f, 8.0f), new Vector2(0.5f, 1.0f));

			RectTransform headingBox = HudKit.Rect(compass, "HeadingBox");
			HudKit.Fixed(headingBox, new Vector2(0.5f, 1.0f), new Vector2(64.0f, 26.0f), new Vector2(0.5f, 1.0f));
			HudKit.Stretch(HudKit.Image(headingBox, "Back", s.Panel).rectTransform, Vector2.zero, Vector2.one);
			_compassHeading = HudKit.Text(headingBox, "Heading", 20.0f, TextAlignmentOptions.Center, s.Text, true);
			HudKit.Stretch(_compassHeading.rectTransform, Vector2.zero, Vector2.one);

			_compassTeammate     = CompassPin("Teammate", s.Team);
			_compassPing         = CompassPin("Ping", s.Ping);
			_compassTeammatePing = CompassPin("TeammatePing", s.Ping);

			// 4 Counters: players alive, kills, spectators
			// Phase 11a (reference): two dark chips, a person and the players alive, a skull and your kills; spectators to their left
			RectTransform counters = HudKit.Box(_root, "Counters", s.GetBox(HudSettings.Counters));
			_aliveText = CounterChip(counters, "Alive", HudKit.Person, new Vector2(0.0f, 0.0f), new Vector2(0.53f, 1.0f));
			_killsText = CounterChip(counters, "Kills", HudKit.Skull,  new Vector2(0.57f, 0.0f), new Vector2(1.0f, 1.0f));
			_counters = HudKit.Label(counters, "Watching", 15.0f, TextAlignmentOptions.Right, s.Dim);
			_counters.rectTransform.anchorMin = new Vector2(0.0f, 0.0f);
			_counters.rectTransform.anchorMax = new Vector2(0.0f, 1.0f);
			_counters.rectTransform.pivot     = new Vector2(1.0f, 0.5f);
			_counters.rectTransform.sizeDelta = new Vector2(200.0f, 0.0f);
			_counters.rectTransform.anchoredPosition = new Vector2(-8.0f, 0.0f);

			// 5 Kill feed: the box is one row; rows stack downward, newest on top
			_killFeed = HudKit.Box(_root, "KillFeed", s.GetBox(HudSettings.KillFeed));
			int rows = Mathf.Max(1, s.KillFeedRows);
			_killFeedRows = new KillRowView[rows];

			for (int i = 0; i < rows; ++i)
			{
				var row = new KillRowView();
				row.Root = HudKit.Rect(_killFeed, $"Row{i}");
				row.Root.anchorMin = new Vector2(1.0f, 1.0f);
				row.Root.anchorMax = new Vector2(1.0f, 1.0f);
				row.Root.pivot     = new Vector2(1.0f, 1.0f);
				row.Root.sizeDelta = new Vector2(0.0f, 28.0f);
				row.Root.anchoredPosition = new Vector2(0.0f, -i * 30.0f);

				row.Killer = FeedPart(row.Root, "Killer", TextAlignmentOptions.Right);
				row.Victim = FeedPart(row.Root, "Victim", TextAlignmentOptions.Right);
				row.Cause  = FeedPart(row.Root, "Cause",  TextAlignmentOptions.Center);
				row.Icon   = HudKit.Image(row.Root, "Gun", s.Text);
				row.Icon.preserveAspect = true;
				row.Icon.rectTransform.anchorMin = row.Icon.rectTransform.anchorMax = new Vector2(1.0f, 0.5f);
				row.Icon.rectTransform.pivot     = new Vector2(1.0f, 0.5f);
				row.Icon.rectTransform.sizeDelta = new Vector2(64.0f, 28.0f);

				row.Root.gameObject.SetActive(false);
				_killFeedRows[i] = row;
			}

			// System feed: left, above the squad list
			HudSettings.Box squadBox = s.GetBox(HudSettings.Squad);
			_systemFeed = HudKit.Rect(_root, "SystemFeed");
			_systemFeed.anchorMin = new Vector2(squadBox.X0, 1.0f - squadBox.Y0 + 0.012f);
			_systemFeed.anchorMax = new Vector2(squadBox.X0 + 0.3f, 1.0f - squadBox.Y0 + 0.012f);
			_systemFeed.pivot     = new Vector2(0.0f, 0.0f);
			_systemFeed.sizeDelta = new Vector2(0.0f, SYSTEM_ROWS * 24.0f);

			for (int i = 0; i < SYSTEM_ROWS; ++i)
			{
				TextMeshProUGUI row = HudKit.Text(_systemFeed, $"Row{i}", 17.0f, TextAlignmentOptions.BottomLeft, s.Team);
				row.rectTransform.anchorMin = new Vector2(0.0f, 0.0f);
				row.rectTransform.anchorMax = new Vector2(1.0f, 0.0f);
				row.rectTransform.pivot     = new Vector2(0.0f, 0.0f);
				row.rectTransform.sizeDelta = new Vector2(0.0f, 24.0f);
				row.rectTransform.anchoredPosition = new Vector2(0.0f, i * 24.0f);
				row.gameObject.SetActive(false);
				_systemRows[i] = row;
			}
		}

		private TextMeshProUGUI CounterChip(RectTransform parent, string name, Sprite icon, Vector2 min, Vector2 max)
		{
			RectTransform chip = HudKit.Stretch(HudKit.Rect(parent, name), min, max);
			HudKit.Panel(chip, "Back", _settings.Panel);

			Image image = HudKit.Image(chip, "Icon", _settings.Text, icon);
			HudKit.Fixed(image.rectTransform, new Vector2(0.0f, 0.5f), new Vector2(20.0f, 20.0f), new Vector2(0.0f, 0.5f)).anchoredPosition = new Vector2(7.0f, 0.0f);

			TextMeshProUGUI text = HudKit.Text(chip, "Number", 21.0f, TextAlignmentOptions.Center, _settings.Text, true);
			HudKit.Stretch(text.rectTransform, Vector2.zero, Vector2.one);
			text.rectTransform.offsetMin = new Vector2(28.0f, 0.0f);
			return text;
		}

		private TextMeshProUGUI FeedPart(RectTransform row, string name, TextAlignmentOptions alignment)
		{
			TextMeshProUGUI text = HudKit.Text(row, name, 18.0f, alignment, _settings.Text, true);
			text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(1.0f, 0.5f);
			text.rectTransform.pivot     = new Vector2(1.0f, 0.5f);
			text.rectTransform.sizeDelta = new Vector2(0.0f, 28.0f);
			return text;
		}

		private Image MinimapDot(string name, Color color, float size)
		{
			Image dot = HudKit.Image(_minimapRotator, name, color, HudKit.Circle);
			HudKit.Fixed(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(size, size), new Vector2(0.5f, 0.5f));
			dot.gameObject.SetActive(false);
			return dot;
		}

		private Image CompassPin(string name, Color color)
		{
			Image pin = HudKit.Image(_compassStrip, name, color, HudKit.Circle);
			HudKit.Fixed(pin.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(10.0f, 10.0f), new Vector2(0.5f, 0.0f));
			pin.gameObject.SetActive(false);
			return pin;
		}

		private void UpdateTop()
		{
			Agent   teammate = GetTeammate();
			float   heading  = GetHeading();
			Vector3 center   = _agent.transform.position;

			// Phase 11a: the inventory covers the top left (its AMMO grid sits there)
			bool inventoryOpen = _context.Input != null && _context.Input.IsInventoryOpen == true && _agent.HasInputAuthority == true;
			HudKit.Show(_minimapBox, inventoryOpen == false);

			UpdateMinimap(teammate, heading, center);
			UpdateCompass(teammate, heading);
			UpdateCounters();
			UpdateKillFeed();
		}

		private void UpdateMinimap(Agent teammate, float heading, Vector3 center)
		{
			SceneMap map = _context.Map;
			float viewport = _minimapViewport.rect.width;

			if (map == null || map.MapTexture == null || viewport <= 0.0f)
			{
				HudKit.Show(_minimapImage, false);
				return;
			}

			HudKit.Show(_minimapImage, true);
			_minimapImage.texture = map.MapTexture;

			float   pixelsPerMeter = viewport / Mathf.Max(1.0f, _settings.MinimapMeters);
			float   mapMeters      = Mathf.Max(map.WorldDimensions.x, map.WorldDimensions.y);
			Vector3 mapCenter      = map.transform.position;

			_minimapRotator.localRotation = Quaternion.Euler(0.0f, 0.0f, heading);
			_minimapImage.rectTransform.sizeDelta        = new Vector2(mapMeters, mapMeters) * pixelsPerMeter;
			_minimapImage.rectTransform.anchoredPosition = new Vector2(mapCenter.x - center.x, mapCenter.z - center.z) * pixelsPerMeter;

			// Zone circles, route to the zone, flight path (phase 7)
			UpdateMinimapMatch(center, pixelsPerMeter);

			bool hasAgent = _agent != null;

			PlaceMinimapDot(_minimapTeammate, teammate != null, teammate != null ? teammate.transform.position : default, center, pixelsPerMeter);
			PlaceMinimapDot(_minimapPing, hasAgent && _agent.HasPing, hasAgent ? _agent.PingPoint : default, center, pixelsPerMeter);
			PlaceMinimapDot(_minimapTeammatePing, teammate != null && teammate.HasPing == true, teammate != null ? teammate.PingPoint : default, center, pixelsPerMeter);
			PlaceMinimapDot(_minimapWaypoint, hasAgent && _agent.HasWaypoint, hasAgent ? _agent.WaypointPoint : default, center, pixelsPerMeter);
			PlaceMinimapDot(_minimapTeammateWaypoint, teammate != null && teammate.HasWaypoint == true, teammate != null ? teammate.WaypointPoint : default, center, pixelsPerMeter);

			// Sixth Sense (phase 5b): the last blink's spots, each fading over SixthSenseFade
			float age   = hasAgent ? Time.time - _agent.SixthSenseBlinkTime : 1000.0f;
			float fade  = Mathf.Max(0.01f, EquipmentSettings.Current.SixthSenseFade);
			float alpha = age < fade ? 1.0f - age / fade : 0.0f;
			int   count = alpha > 0.0f ? _agent.SixthSenseSpotCount : 0;

			for (int i = 0; i < _minimapSixthSense.Length; ++i)
			{
				Image dot = _minimapSixthSense[i];
				PlaceMinimapDot(dot, i < count, i < count ? _agent.SixthSenseSpots[i] : default, center, pixelsPerMeter);
				Color color = _settings.Enemy;
				color.a   = alpha;
				dot.color = color;
			}

			Readout["minimap.heading"] = heading.ToString("F0");
			Readout["minimap.blinks"]  = count.ToString();
		}

		private static void PlaceMinimapDot(Image dot, bool visible, Vector3 position, Vector3 center, float pixelsPerMeter)
		{
			HudKit.Show(dot, visible);
			if (visible == true)
			{
				dot.rectTransform.anchoredPosition = new Vector2(position.x - center.x, position.z - center.z) * pixelsPerMeter;
			}
		}

		private void UpdateCompass(Agent teammate, float heading)
		{
			float width = _compassStrip.rect.width;
			float span  = Mathf.Max(10.0f, _settings.CompassDegrees);

			for (int i = 0; i < _compassTicks.Length; ++i)
			{
				float delta   = Mathf.DeltaAngle(heading, i * 15.0f);
				bool  visible = Mathf.Abs(delta) <= span * 0.5f;

				HudKit.Show(_compassTicks[i], visible);
				if (visible == true)
				{
					_compassTicks[i].rectTransform.anchoredPosition = new Vector2(delta / span * width, 0.0f);
				}
			}

			int rounded = Mathf.RoundToInt(heading) % 360;
			HudKit.Set(_compassHeading, rounded.ToString());
			Readout["compass.heading"] = rounded.ToString();

			Vector3 view = GetViewPosition();
			bool hasAgent = _agent != null;
			PlaceCompassPin(_compassTeammate, teammate != null, teammate != null ? teammate.transform.position : default, view, heading, width, span);
			PlaceCompassPin(_compassPing, hasAgent && _agent.HasPing, hasAgent ? _agent.PingPoint : default, view, heading, width, span);
			PlaceCompassPin(_compassTeammatePing, teammate != null && teammate.HasPing == true, teammate != null ? teammate.PingPoint : default, view, heading, width, span);
			PlaceCompassPin(_compassWaypoint, hasAgent && _agent.HasWaypoint, hasAgent ? _agent.WaypointPoint : default, view, heading, width, span);
			PlaceCompassPin(_compassTeammateWaypoint, teammate != null && teammate.HasWaypoint == true, teammate != null ? teammate.WaypointPoint : default, view, heading, width, span);
		}

		private static void PlaceCompassPin(Image pin, bool visible, Vector3 target, Vector3 view, float heading, float width, float span)
		{
			if (visible == true)
			{
				Vector3 offset  = target - view;
				float   bearing = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
				float   delta   = Mathf.Clamp(Mathf.DeltaAngle(heading, bearing), -span * 0.5f, span * 0.5f);
				pin.rectTransform.anchoredPosition = new Vector2(delta / span * width, 2.0f);
			}

			HudKit.Show(pin, visible);
		}

		private void UpdateCounters()
		{
			if (Time.unscaledTime >= _nextCounterTime)
			{
				_nextCounterTime = Time.unscaledTime + 0.25f;

				int alive = 0;
				for (int i = 0; i < _agents.Count; ++i)
				{
					if (_agents[i] != null && _agents[i].Object != null && _agents[i].Object.IsValid == true && _agents[i].Health.IsAlive == true)
					{
						++alive;
					}
				}

				// Battle Royale (phase 7): everyone still in, the helicopter's riders too, counted by the server
				if (_context.GameplayMode is BattleRoyaleGameplayMode battleRoyale && battleRoyale.Phase != EMatchPhase.Warmup)
				{
					alive = battleRoyale.PlayersAlive;
				}

				var    observed   = _agent != null ? _agent.Object.InputAuthority : _context.LocalPlayerRef;
				Player player     = observed.IsRealPlayer == true ? _context.NetworkGame.GetPlayer(observed) : null;
				int    kills      = player != null ? player.Statistics.Kills : 0;
				int    spectators = 0;

				foreach (Player other in _context.NetworkGame.ActivePlayers)
				{
					if (other != null && other.Object != null && other.Object.InputAuthority != observed && other.Watching == observed)
					{
						++spectators;
					}
				}

				_countersText = spectators > 0 ? $"{spectators} WATCHING" : string.Empty;
				HudKit.Set(_aliveText, alive.ToString());
				HudKit.Set(_killsText, kills.ToString());

				Readout["counters.alive"]      = alive.ToString();
				Readout["counters.kills"]      = kills.ToString();
				Readout["counters.spectators"] = spectators.ToString();
			}

			HudKit.Set(_counters, _countersText);
		}

		private void AddKillFeed(KillData data)
		{
			int localTeam = _agent != null ? GameplayMode.GetTeam(_agent) : 0;
			var local     = _context.LocalPlayerRef;

			string killer = data.KillerBot > 0 ? GetBotName(data.KillerBot) : (data.KillerRef.IsRealPlayer == true ? GetName(data.KillerRef) : string.Empty);
			string victim = data.VictimBot > 0 ? GetBotName(data.VictimBot) : GetName(data.VictimRef);
			bool   self   = data.KillerBot > 0 ? data.KillerBot == data.VictimBot : (data.VictimBot == 0 && data.KillerRef == data.VictimRef);

			bool killerOurs = (localTeam > 0 && data.KillerTeam == localTeam) || (data.KillerRef == local && data.KillerRef.IsRealPlayer == true);
			bool victimOurs = (localTeam > 0 && data.VictimTeam == localTeam) || (data.VictimRef == local && data.VictimRef.IsRealPlayer == true);

			GunData gun   = data.GunIndex > 0 ? ItemCatalog.GetGunData(data.GunIndex - 1) : null;
			string  cause = gun != null ? $"[{gun.GunName}]" : "[X]";

			string victimText = $"<color=#{Hex(victimOurs == true ? _settings.Team : _settings.Enemy)}>{victim}</color>";
			string killerText = killer.Length > 0 && self == false ? $"<color=#{Hex(killerOurs == true ? _settings.Team : _settings.Enemy)}>{killer}</color>" : string.Empty;
			string text       = killerText.Length > 0 ? $"{killerText}  {cause}  {victimText}" : $"{cause}  {victimText}";

			// Phase 11a: the gun's silhouette between the names; an X for deaths without a gun (zone, fall, grenade)
			Sprite icon = gun != null ? IconSettings.GetGunSilhouette(data.GunIndex - 1) : null;

			_feed.Insert(0, new FeedRow { Text = text, Time = Time.unscaledTime, Killer = killerText, Victim = victimText, Icon = icon, Cause = icon != null ? string.Empty : (gun != null ? gun.GunName : "X") });
			while (_feed.Count > _killFeedRows.Length)
			{
				_feed.RemoveAt(_feed.Count - 1);
			}
		}

		/// <summary>Phase 11a: a line in the yellow system feed on the left (joined, left, teammate down, zone, match).</summary>
		private void AddSystemRow(string text)
		{
			_system.Insert(0, new FeedRow { Text = text, Time = Time.unscaledTime });
			while (_system.Count > SYSTEM_ROWS)
			{
				_system.RemoveAt(_system.Count - 1);
			}

			Readout["system.last"] = text;
		}

		private void UpdateKillFeed()
		{
			for (int i = _feed.Count - 1; i >= 0; --i)
			{
				if (Time.unscaledTime - _feed[i].Time > _settings.KillFeedTime)
				{
					_feed.RemoveAt(i);
				}
			}

			for (int i = 0; i < _killFeedRows.Length; ++i)
			{
				KillRowView row     = _killFeedRows[i];
				bool        visible = i < _feed.Count;
				HudKit.Show(row.Root, visible);
				if (visible == false)
					continue;

				// Right to left: victim, gun, killer
				FeedRow feed = _feed[i];
				HudKit.Set(row.Victim, feed.Victim);
				HudKit.Set(row.Killer, feed.Killer);
				HudKit.Set(row.Cause,  feed.Cause);

				float x = PlaceFeedPart(row.Victim.rectTransform, row.Victim.preferredWidth, 0.0f) - 10.0f;

				bool hasIcon = feed.Icon != null;
				HudKit.Show(row.Icon, hasIcon);
				HudKit.Show(row.Cause, hasIcon == false);
				if (hasIcon == true)
				{
					if (row.Icon.sprite != feed.Icon)
					{
						row.Icon.sprite = feed.Icon;
					}
					x = PlaceFeedPart(row.Icon.rectTransform, row.Icon.rectTransform.sizeDelta.x, x);
				}
				else
				{
					x = PlaceFeedPart(row.Cause.rectTransform, row.Cause.preferredWidth, x);
				}

				PlaceFeedPart(row.Killer.rectTransform, row.Killer.preferredWidth, x - 10.0f);
			}

			// System feed: newest at the bottom, each line fading out in its last second
			float systemTime = _settings.KillFeedTime + 2.0f;
			for (int i = _system.Count - 1; i >= 0; --i)
			{
				if (Time.unscaledTime - _system[i].Time > systemTime)
				{
					_system.RemoveAt(i);
				}
			}

			for (int i = 0; i < _systemRows.Length; ++i)
			{
				bool visible = i < _system.Count;
				HudKit.Show(_systemRows[i], visible);
				if (visible == true)
				{
					HudKit.Set(_systemRows[i], _system[i].Text);
					_systemRows[i].alpha = Mathf.Clamp01(systemTime - (Time.unscaledTime - _system[i].Time));
				}
			}

			Readout["killfeed.rows"] = _feed.Count.ToString();
			Readout["killfeed.top"]  = _feed.Count > 0 ? _feed[0].Text : string.Empty;
			Readout["system.rows"]   = _system.Count.ToString();
		}

		/// <summary>Puts a feed part with its right edge at x (from the row's right) and returns its left edge.</summary>
		private static float PlaceFeedPart(RectTransform part, float width, float x)
		{
			part.sizeDelta        = new Vector2(width, part.sizeDelta.y);
			part.anchoredPosition = new Vector2(x, 0.0f);
			return x - width;
		}
	}
}
