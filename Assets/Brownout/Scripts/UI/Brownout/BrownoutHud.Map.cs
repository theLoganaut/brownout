namespace TPSBR.UI
{
	using TMPro;
	using UnityEngine;
	using UnityEngine.InputSystem;
	using UnityEngine.UI;

	/// <summary>
	/// The full map on M (phase 6b, HUD & UI page and the reference): the map with a lettered grid (A.. across, 1.. down; 10 × 10 cells of
	/// 100 m on our map, phase 8, else HudSettings.MapGrid), the POI names, you as an arrow, your teammate, pings, waypoints and the zone
	/// circles. Left click places your waypoint (it shows on your compass, minimap and in the world, and on your teammate's); a left click
	/// on it removes it. The wheel zooms around the mouse. The game keeps running; you can still move. The image is our map's baked picture
	/// (phase 8), or the generated level's render.
	/// </summary>
	public sealed partial class BrownoutHud
	{
		// PRIVATE MEMBERS

		private RectTransform   _map;
		private RectTransform   _mapFrame;
		private RectTransform   _mapViewport;
		private RectTransform   _mapContent;
		private RawImage        _mapImage;
		private TextMeshProUGUI[] _mapLetters;
		private TextMeshProUGUI[] _mapNumbers;
		private RectTransform   _mapPlayer;
		private Image           _mapTeammate;
		private Image           _mapPing;
		private Image           _mapTeammatePing;
		private RectTransform   _mapWaypoint;
		private RectTransform   _mapTeammateWaypoint;
		private Image           _mapZone;
		private Image           _mapZoneNext;
		private TextMeshProUGUI _mapInfo;
		private System.Collections.Generic.List<TextMeshProUGUI> _mapLabels;

		private bool            _mapOpen;
		private bool            _hasPendingWaypoint; // Placed in the helicopter (no agent yet), sent once you're out
		private Vector3         _pendingWaypoint;
		private float           _mapZoom = 1.0f;
		private Vector2         _mapPan  = new Vector2(0.5f, 0.5f); // The normalized map point at the viewport's center

		private Image           _minimapWaypoint;
		private Image           _minimapTeammateWaypoint;
		private Image           _compassWaypoint;
		private Image           _compassTeammateWaypoint;
		private RectTransform   _waypointMarker;
		private TextMeshProUGUI _waypointMarkerText;
		private RectTransform   _teammateWaypointMarker;
		private TextMeshProUGUI _teammateWaypointMarkerText;

		// PUBLIC METHODS

		public bool IsMapOpen => _mapOpen;

		/// <summary>Opens or closes the full map (M, Esc, or a test).</summary>
		public void SetMapOpen(bool open)
		{
			if (_mapOpen == open)
				return;

			_mapOpen = open;

			if (open == true)
			{
				_mapZoom = 1.0f;
				_mapPan  = new Vector2(0.5f, 0.5f);
			}

			if (_context.Input != null)
			{
				if (open == true)
				{
					_context.Input.RequestCursorVisibility(false, ECursorStateSource.Inventory);
				}

				_context.Input.RequestCursorVisibility(open, ECursorStateSource.Map);
			}
		}

		/// <summary>Test hook: a waypoint at a world point, as if clicked on the map (or removed).</summary>
		public void TestWaypoint(Vector3 position, bool place)
		{
			if (_agent != null)
			{
				_agent.RequestWaypoint(new Vector2(position.x, position.z), place);
			}
		}

		// PRIVATE METHODS

		private void BuildMap()
		{
			HudSettings s = _settings;
			float size = s.ReferenceHeight * Mathf.Clamp(s.MapScreenShare, 0.3f, 1.0f);
			const float axis = 36.0f;

			_map = HudKit.Rect(transform, "FullMap");
			HudKit.Stretch(_map, Vector2.zero, Vector2.one);
			HudKit.Stretch(HudKit.Image(_map, "Dim", new Color(0.0f, 0.0f, 0.0f, 0.35f)).rectTransform, Vector2.zero, Vector2.one);

			_mapFrame = HudKit.Fixed(HudKit.Rect(_map, "Frame"), new Vector2(0.5f, 0.5f), new Vector2(size, size), new Vector2(0.5f, 0.5f));
			HudKit.Stretch(HudKit.Image(_mapFrame, "Back", new Color(0.08f, 0.09f, 0.1f, 0.95f)).rectTransform, Vector2.zero, Vector2.one);

			_mapViewport = HudKit.Rect(_mapFrame, "Viewport");
			_mapViewport.anchorMin = Vector2.zero;
			_mapViewport.anchorMax = Vector2.one;
			_mapViewport.offsetMin = new Vector2(axis, 6.0f);
			_mapViewport.offsetMax = new Vector2(-6.0f, -axis);
			_mapViewport.gameObject.AddComponent<RectMask2D>();

			_mapContent = HudKit.Rect(_mapViewport, "Content");
			_mapContent.anchorMin = _mapContent.anchorMax = _mapContent.pivot = new Vector2(0.5f, 0.5f);

			_mapImage = HudKit.Rect(_mapContent, "Image").gameObject.AddComponent<RawImage>();
			_mapImage.raycastTarget = false;
			HudKit.Stretch(_mapImage.rectTransform, Vector2.zero, Vector2.one);

			int grid = MapGridCells;
			for (int i = 1; i < grid; ++i)
			{
				float t = i / (float)grid;
				Image vertical = HudKit.Image(_mapContent, $"GridX{i}", new Color(0.0f, 0.0f, 0.0f, 0.45f));
				vertical.rectTransform.anchorMin = new Vector2(t, 0.0f);
				vertical.rectTransform.anchorMax = new Vector2(t, 1.0f);
				vertical.rectTransform.sizeDelta = new Vector2(2.0f, 0.0f);
				Image horizontal = HudKit.Image(_mapContent, $"GridY{i}", new Color(0.0f, 0.0f, 0.0f, 0.45f));
				horizontal.rectTransform.anchorMin = new Vector2(0.0f, t);
				horizontal.rectTransform.anchorMax = new Vector2(1.0f, t);
				horizontal.rectTransform.sizeDelta = new Vector2(0.0f, 2.0f);
			}

			_mapLetters = new TextMeshProUGUI[grid];
			_mapNumbers = new TextMeshProUGUI[grid];
			for (int i = 0; i < grid; ++i)
			{
				_mapLetters[i] = HudKit.Text(_mapFrame, $"Letter{i}", 22.0f, TextAlignmentOptions.Center, s.Text, true);
				_mapLetters[i].rectTransform.anchorMin = _mapLetters[i].rectTransform.anchorMax = new Vector2(0.0f, 1.0f);
				_mapLetters[i].rectTransform.sizeDelta = new Vector2(40.0f, axis);
				_mapLetters[i].text = ((char)('A' + i)).ToString();

				_mapNumbers[i] = HudKit.Text(_mapFrame, $"Number{i}", 22.0f, TextAlignmentOptions.Center, s.Text, true);
				_mapNumbers[i].rectTransform.anchorMin = _mapNumbers[i].rectTransform.anchorMax = new Vector2(0.0f, 1.0f);
				_mapNumbers[i].rectTransform.sizeDelta = new Vector2(axis, 40.0f);
				_mapNumbers[i].text = (i + 1).ToString();
			}

			_mapZone     = MapMarker("Zone", s.ZoneCurrent, HudKit.ZoneRing, 10.0f);
			_mapZoneNext = MapMarker("ZoneNext", s.ZoneNext, HudKit.ZoneRing, 10.0f);
			_mapTeammate     = MapMarker("Teammate", s.Team, HudKit.Circle, 14.0f);
			_mapPing         = MapMarker("Ping", s.Ping, HudKit.Circle, 12.0f);
			_mapTeammatePing = MapMarker("TeammatePing", s.Ping, HudKit.Circle, 12.0f);
			_mapWaypoint         = WaypointPin(_mapContent, "Waypoint", s.Waypoint, 22.0f);
			_mapTeammateWaypoint = WaypointPin(_mapContent, "TeammateWaypoint", Color.Lerp(s.Waypoint, s.Team, 0.5f), 18.0f);

			// You: an arrow pointing where you look
			_mapPlayer = HudKit.Fixed(HudKit.Rect(_mapContent, "Player"), Vector2.zero, new Vector2(26.0f, 26.0f), new Vector2(0.5f, 0.5f));
			HudKit.Stretch(HudKit.Image(_mapPlayer, "Arrow", s.Team, HudKit.Arrow).rectTransform, Vector2.zero, Vector2.one);

			// Controls panel, bottom right like the reference
			RectTransform controls = HudKit.Rect(_map, "Controls");
			controls.anchorMin = controls.anchorMax = controls.pivot = new Vector2(1.0f, 0.0f);
			controls.sizeDelta = new Vector2(300.0f, 150.0f);
			controls.anchoredPosition = new Vector2(-40.0f, 60.0f);
			HudKit.Stretch(HudKit.Image(controls, "Back", new Color(0.05f, 0.06f, 0.06f, 0.85f)).rectTransform, Vector2.zero, Vector2.one);
			TextMeshProUGUI controlsText = HudKit.Text(controls, "Text", 18.0f, TextAlignmentOptions.TopLeft, s.Text);
			Pad(controlsText.rectTransform, new Vector2(16.0f, -12.0f), 130.0f);
			string key = Hex(s.Team);
			controlsText.text = $"<size=14><color=#{Hex(s.Dim)}>CONTROLS</color></size>\n<color=#{key}>[LMB]</color>  Place waypoint\n<color=#{key}>[LMB]</color>  on it: remove it\n<color=#{key}>[Wheel]</color>  Zoom\n<color=#{key}>[M] / [Esc]</color>  Close";

			_mapInfo = HudKit.Text(_map, "Info", 18.0f, TextAlignmentOptions.Top, s.Text, true);
			_mapInfo.rectTransform.anchorMin = _mapInfo.rectTransform.anchorMax = _mapInfo.rectTransform.pivot = new Vector2(0.5f, 0.0f);
			_mapInfo.rectTransform.sizeDelta = new Vector2(size, 30.0f);
			_mapInfo.rectTransform.anchoredPosition = new Vector2(0.0f, (s.ReferenceHeight - size) * 0.5f - 34.0f);

			_map.gameObject.SetActive(false);

			// Waypoints on the minimap, compass and in the world (6a's elements)
			_minimapWaypoint         = WaypointDot(_minimapRotator, "Waypoint", s.Waypoint);
			_minimapTeammateWaypoint = WaypointDot(_minimapRotator, "TeammateWaypoint", Color.Lerp(s.Waypoint, s.Team, 0.5f));
			_compassWaypoint         = CompassPin("Waypoint", s.Waypoint);
			_compassTeammateWaypoint = CompassPin("TeammateWaypoint", Color.Lerp(s.Waypoint, s.Team, 0.5f));
			_waypointMarker         = WorldMarker("Waypoint", s.Waypoint, out _waypointMarkerText);
			_teammateWaypointMarker = WorldMarker("TeammateWaypoint", Color.Lerp(s.Waypoint, s.Team, 0.5f), out _teammateWaypointMarkerText);
		}

		private Image MapMarker(string name, Color color, Sprite sprite, float size)
		{
			Image marker = HudKit.Image(_mapContent, name, color, sprite);
			HudKit.Fixed(marker.rectTransform, Vector2.zero, new Vector2(size, size), new Vector2(0.5f, 0.5f));
			marker.gameObject.SetActive(false);
			return marker;
		}

		/// <summary>A waypoint flag: a diamond on a short stem, its foot on the spot.</summary>
		private static RectTransform WaypointPin(RectTransform parent, string name, Color color, float size)
		{
			RectTransform pin = HudKit.Fixed(HudKit.Rect(parent, name), Vector2.zero, new Vector2(size, size * 1.6f), new Vector2(0.5f, 0.0f));
			Image stem = HudKit.Image(pin, "Stem", color);
			HudKit.Stretch(stem.rectTransform, new Vector2(0.45f, 0.0f), new Vector2(0.55f, 0.5f));
			Image head = HudKit.Image(pin, "Head", color);
			HudKit.Fixed(head.rectTransform, new Vector2(0.5f, 0.7f), new Vector2(size * 0.6f, size * 0.6f), new Vector2(0.5f, 0.5f)).localRotation = Quaternion.Euler(0.0f, 0.0f, 45.0f);
			pin.gameObject.SetActive(false);
			return pin;
		}

		private Image WaypointDot(RectTransform parent, string name, Color color)
		{
			Image dot = HudKit.Image(parent, name, color);
			HudKit.Fixed(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(9.0f, 9.0f), new Vector2(0.5f, 0.5f)).localRotation = Quaternion.Euler(0.0f, 0.0f, 45.0f);
			dot.gameObject.SetActive(false);
			return dot;
		}

		/// <summary>The waypoint placed in the helicopter goes to the server once you have an agent.</summary>
		private void SendPendingWaypoint()
		{
			if (_hasPendingWaypoint == false || _agent.HasInputAuthority == false)
				return;

			_hasPendingWaypoint = false;
			_agent.RequestWaypoint(new Vector2(_pendingWaypoint.x, _pendingWaypoint.z), true);
		}

		/// <summary>M opens and closes the map for the local player (also in the helicopter); Esc and the inventory close it (cursor state).</summary>
		private void UpdateMapInput(bool riding)
		{
			bool local = riding == true ? _context.HasInput == true : _agent.HasInputAuthority == true && _context.HasInput == true && _agent.Health.IsAlive == true;

			if (local == false)
			{
				SetMapOpen(false);
				return;
			}

			if (_mapOpen == true && _context.Input.IsMapOpen == false)
			{
				SetMapOpen(false);
			}

			if (_context.Input.IsCursorVisibleOutsideInventory == true && _mapOpen == false)
				return;

			if (KeyBindings.WasPressedThisFrame(EBinding.Map) == true)
			{
				SetMapOpen(_mapOpen == false);
			}
		}

		/// <summary>The full map. Riding: you are the helicopter, no teammate or pings, and a click places a waypoint that's sent once you're out.</summary>
		private void UpdateMap(Agent teammate, bool riding)
		{
			HudKit.Show(_map, _mapOpen);
			Readout["map.open"] = _mapOpen == true ? "open" : "closed";

			if (riding == false)
			{
				UpdateWaypointElements(teammate);
			}

			if (_mapOpen == false)
				return;

			SceneMap sceneMap = _context.Map;
			if (sceneMap == null || sceneMap.MapTexture == null)
				return;

			_mapImage.texture = sceneMap.MapTexture;

			Vector2 viewport = _mapViewport.rect.size;
			float   side     = Mathf.Min(viewport.x, viewport.y);
			Mouse   mouse    = Mouse.current;
			Camera  camera   = GetUICamera();
			Vector2 screen   = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
			bool    inside   = RectTransformUtility.RectangleContainsScreenPoint(_mapViewport, screen, camera) == true;

			// Wheel: zoom around the point under the mouse
			if (mouse != null && inside == true)
			{
				float wheel = mouse.scroll.ReadValue().y;
				if (Mathf.Abs(wheel) > 0.01f)
				{
					Vector2 before = ScreenToMap(screen, camera, side);
					_mapZoom = Mathf.Clamp(_mapZoom * (wheel > 0.0f ? 1.25f : 0.8f), 1.0f, Mathf.Max(1.0f, _settings.MapMaxZoom));
					PlaceMapContent(side);
					Vector2 after = ScreenToMap(screen, camera, side);
					_mapPan += before - after;
				}
			}

			PlaceMapContent(side);

			bool    hasWaypoint = riding == true ? _hasPendingWaypoint : _agent.HasWaypoint;
			Vector3 waypoint    = riding == true ? _pendingWaypoint : _agent.WaypointPoint;

			// Left click: place a waypoint, or remove yours when clicking on it
			if (mouse != null && inside == true && mouse.leftButton.wasPressedThisFrame == true)
			{
				Vector2 clicked = ScreenToMap(screen, camera, side);
				bool    onOwn   = hasWaypoint == true && Vector2.Distance(MapToLocal(WorldToMap(waypoint), side), MapToLocal(clicked, side)) <= _settings.WaypointRemovePixels;
				Vector3 world   = MapToWorld(clicked);

				if (riding == true)
				{
					_hasPendingWaypoint = onOwn == false;
					_pendingWaypoint    = world;
				}
				else
				{
					_agent.RequestWaypoint(new Vector2(world.x, world.z), onOwn == false);
				}
			}

			// Grid labels follow the zoom and pan
			int grid = _mapLetters.Length;
			Vector2 axisOrigin = _mapViewport.offsetMin;
			for (int i = 0; i < grid; ++i)
			{
				float center = (i + 0.5f) / grid;
				float x      = MapToLocal(new Vector2(center, 0.0f), side).x + viewport.x * 0.5f;
				float y      = MapToLocal(new Vector2(0.0f, 1.0f - center), side).y - viewport.y * 0.5f;

				HudKit.Show(_mapLetters[i], x >= 0.0f && x <= viewport.x);
				_mapLetters[i].rectTransform.anchoredPosition = new Vector2(axisOrigin.x + x, -18.0f);
				HudKit.Show(_mapNumbers[i], y <= 0.0f && y >= -viewport.y);
				_mapNumbers[i].rectTransform.anchoredPosition = new Vector2(18.0f, -36.0f + y);
			}

			// You, your teammate, pings, waypoints, the zone
			Vector3 own = riding == true ? _context.WaitingAgentTransform.position : _agent.transform.position;
			_mapPlayer.anchorMin = _mapPlayer.anchorMax = WorldToMap(own);
			_mapPlayer.anchoredPosition = Vector2.zero;
			_mapPlayer.localRotation    = Quaternion.Euler(0.0f, 0.0f, -GetHeading());

			PlaceMapMarker(_mapTeammate.rectTransform, teammate != null, teammate != null ? teammate.transform.position : default, side);
			PlaceMapMarker(_mapPing.rectTransform, riding == false && _agent.HasPing == true, riding == false ? _agent.PingPoint : default, side);
			PlaceMapMarker(_mapTeammatePing.rectTransform, teammate != null && teammate.HasPing == true, teammate != null ? teammate.PingPoint : default, side);
			PlaceMapMarker(_mapWaypoint, hasWaypoint, waypoint, side);
			PlaceMapMarker(_mapTeammateWaypoint, teammate != null && teammate.HasWaypoint == true, teammate != null ? teammate.WaypointPoint : default, side);

			ShrinkingArea zone = _context.GameplayMode != null ? _context.GameplayMode.ShrinkingArea : null;
			bool zoneShown = zone != null && zone.IsActive == true;
			PlaceZone(_mapZone, zoneShown, zoneShown == true ? zone.Center : default, zoneShown == true ? zone.Radius : 0.0f, side, sceneMap);
			PlaceZone(_mapZoneNext, zoneShown == true && zone.IsAnnounced == true, zoneShown == true ? zone.ShrinkCenter : default, zoneShown == true ? zone.ShrinkRadius : 0.0f, side, sceneMap);

			PlaceMapLabels();

			string square = GetGridSquare(own);
			HudKit.Set(_mapInfo, $"{square}    ·    {(hasWaypoint == true ? $"waypoint {GetGridSquare(waypoint)}, {Vector2.Distance(new Vector2(own.x, own.z), new Vector2(waypoint.x, waypoint.z)):F0} m" : "no waypoint")}");

			Readout["map.zoom"] = _mapZoom.ToString("F2");
			Readout["map.square"] = square;
		}

		/// <summary>Grid cells per side: our map's 100 m cells (phase 8, 10 × 10), else HudSettings.MapGrid.</summary>
		private int MapGridCells
		{
			get
			{
				BrownoutMap map = BrownoutMap.Any;
				return Mathf.Max(1, map != null && map.GridCells > 0 ? map.GridCells : _settings.MapGrid);
			}
		}

		/// <summary>The POI names on our map (phase 8), made once the map is there.</summary>
		private void PlaceMapLabels()
		{
			BrownoutMap map = BrownoutMap.Any;
			if (map == null)
				return;

			if (_mapLabels == null)
			{
				_mapLabels = new System.Collections.Generic.List<TextMeshProUGUI>();
				foreach (BrownoutMap.Area area in map.Areas)
				{
					if (area.IsCabins == true)
						continue;

					TextMeshProUGUI label = HudKit.Text(_mapContent, $"Label {area.Name}", 17.0f, TextAlignmentOptions.Center, _settings.Text, true);
					label.rectTransform.sizeDelta = new Vector2(220.0f, 26.0f);
					label.text = area.Name.ToUpperInvariant();
					label.fontStyle = FontStyles.Bold;
					label.rectTransform.anchorMin = label.rectTransform.anchorMax = WorldToMap(new Vector3(area.Center.x, 0.0f, area.Center.y));
					label.rectTransform.anchoredPosition = Vector2.zero;
					label.transform.SetSiblingIndex(_mapImage.transform.GetSiblingIndex() + 1); // Under the markers and you
					_mapLabels.Add(label);
				}
			}

			Readout["map.labels"] = _mapLabels.Count.ToString();
		}

		/// <summary>The waypoint on the compass, minimap and in the world (shown with the map closed too).</summary>
		private void UpdateWaypointElements(Agent teammate)
		{
			bool own   = _agent.HasWaypoint == true;
			bool their = teammate != null && teammate.HasWaypoint == true;

			Readout["waypoint.own"]      = own   == true ? _agent.WaypointPoint.ToString("F0") : "none";
			Readout["waypoint.teammate"] = their == true ? teammate.WaypointPoint.ToString("F0") : "none";

			Vector3 view = GetViewPosition();
			PlaceWorldWaypoint(_waypointMarker, _waypointMarkerText, own, own == true ? _agent.WaypointPoint : default, view);
			PlaceWorldWaypoint(_teammateWaypointMarker, _teammateWaypointMarkerText, their, their == true ? teammate.WaypointPoint : default, view);
		}

		private void PlaceWorldWaypoint(RectTransform marker, TextMeshProUGUI text, bool visible, Vector3 point, Vector3 view)
		{
			Vector2 position = default;
			visible &= WorldToCanvas(point + Vector3.up * 1.0f, out position);
			HudKit.Show(marker, visible);
			if (visible == true)
			{
				marker.anchoredPosition = position;
				HudKit.Set(text, $"{Vector3.Distance(view, point):F0} m");
			}
		}

		private void PlaceMapContent(float side)
		{
			float size = side * _mapZoom;
			float half = 0.5f / _mapZoom;

			_mapPan.x = Mathf.Clamp(_mapPan.x, half, 1.0f - half);
			_mapPan.y = Mathf.Clamp(_mapPan.y, half, 1.0f - half);

			_mapContent.sizeDelta        = new Vector2(size, size);
			_mapContent.anchoredPosition = (new Vector2(0.5f, 0.5f) - _mapPan) * size;
		}

		private void PlaceMapMarker(RectTransform marker, bool visible, Vector3 world, float side)
		{
			HudKit.Show(marker, visible);
			if (visible == true)
			{
				Vector2 map = WorldToMap(world);
				marker.anchorMin = marker.anchorMax = map;
				marker.anchoredPosition = Vector2.zero;
			}
		}

		private void PlaceZone(Image ring, bool visible, Vector3 center, float radius, float side, SceneMap sceneMap)
		{
			HudKit.Show(ring, visible);
			if (visible == false)
				return;

			float meters = Mathf.Max(1.0f, Mathf.Max(sceneMap.WorldDimensions.x, sceneMap.WorldDimensions.y));
			float pixels = radius * 2.0f / meters * side * _mapZoom;
			ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = WorldToMap(center);
			ring.rectTransform.anchoredPosition = Vector2.zero;
			ring.rectTransform.sizeDelta = new Vector2(pixels, pixels);
		}

		/// <summary>World (x, z) to the normalized map (0..1, (0, 0) at the south-west corner), as the minimap places the render.</summary>
		private Vector2 WorldToMap(Vector3 world)
		{
			SceneMap sceneMap = _context.Map;
			if (sceneMap == null)
				return new Vector2(0.5f, 0.5f);

			float   meters = Mathf.Max(1.0f, Mathf.Max(sceneMap.WorldDimensions.x, sceneMap.WorldDimensions.y));
			Vector3 center = sceneMap.transform.position;
			return new Vector2((world.x - center.x) / meters + 0.5f, (world.z - center.z) / meters + 0.5f);
		}

		private Vector3 MapToWorld(Vector2 map)
		{
			SceneMap sceneMap = _context.Map;
			float    meters   = Mathf.Max(1.0f, Mathf.Max(sceneMap.WorldDimensions.x, sceneMap.WorldDimensions.y));
			Vector3  center   = sceneMap.transform.position;
			return new Vector3(center.x + (map.x - 0.5f) * meters, 0.0f, center.z + (map.y - 0.5f) * meters);
		}

		/// <summary>A normalized map point to the viewport's local position (origin at its center).</summary>
		private Vector2 MapToLocal(Vector2 map, float side)
		{
			return (map - _mapPan) * side * _mapZoom;
		}

		private Vector2 ScreenToMap(Vector2 screen, Camera camera, float side)
		{
			RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapViewport, screen, camera, out Vector2 local);
			local -= _mapViewport.rect.center;
			return _mapPan + local / Mathf.Max(1.0f, side * _mapZoom);
		}

		/// <summary>The grid square of a world point: a letter across (A at the west) and a number down (1 at the north).</summary>
		public string GetGridSquare(Vector3 world)
		{
			int     grid = MapGridCells;
			Vector2 map  = WorldToMap(world);
			int     x    = Mathf.Clamp(Mathf.FloorToInt(map.x * grid), 0, grid - 1);
			int     y    = Mathf.Clamp(Mathf.FloorToInt((1.0f - map.y) * grid), 0, grid - 1);
			return $"{(char)('A' + x)}{y + 1}";
		}
	}
}
