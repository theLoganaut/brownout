namespace TPSBR.UI
{
	using System.Collections.Generic;
	using Fusion;
	using UnityEngine;
	using UnityEngine.UI;

	/// <summary>
	/// The Blackout-style HUD (phase 6a, HUD & UI page). Built in code when the gameplay view starts, every element anchored to its box in
	/// hud_rects.json (HudSettings.Boxes), on its own overlay canvas just under the sample's. UIGameplayView feeds it the observed agent and
	/// the kill events, and switches off the sample widgets it replaces. Split in three files: this one (canvas, agent, events), .Top
	/// (minimap, zone timer stub, compass, counters, kill feed) and .Bottom (squad list, health, healing, armor, gear, equipment, ammo, gun,
	/// and the center: crosshair, hit marker, banners, progress bars, prompts, dash cooldown, low health, teammate and ping markers).
	/// Phase 6b adds the screens on the same canvas: .Inventory (Tab inventory and stash), .Map (M full map and waypoints) and .Screens
	/// (spectating, victory).
	/// </summary>
	public sealed partial class BrownoutHud : MonoBehaviour
	{
		// PUBLIC MEMBERS

		/// <summary>The HUD on screen (in Multipeer, the visible peer's).</summary>
		public static BrownoutHud Instance
		{
			get
			{
				for (int i = 0; i < _all.Count; ++i)
				{
					if (_all[i] != null && _all[i]._canvas.enabled == true && _all[i]._agent != null)
						return _all[i];
				}

				return _all.Count > 0 ? _all[_all.Count - 1] : null;
			}
		}

		private static readonly List<BrownoutHud> _all = new List<BrownoutHud>(2);

		/// <summary>What the HUD shows right now, for the HUD Check (phase 6a test tools).</summary>
		public readonly Dictionary<string, string> Readout = new Dictionary<string, string>(64);

		public Agent Agent => _agent;

		/// <summary>Layout sheet (test tool): outlines every hud_rects.json box over the HUD, with its id and name.</summary>
		public static bool ShowLayoutBoxes;

		// PRIVATE MEMBERS

		private SceneContext  _context;
		private HudSettings   _settings;
		private Canvas        _canvas;
		private RectTransform _root;
		private RectTransform _bottom;
		private RectTransform _center;
		private RectTransform _world;

		private Agent         _agent;
		private Health        _subscribedHealth;
		private float         _scale = 1.0f;

		private static readonly List<Agent> _agents = new List<Agent>(64);

		// PUBLIC METHODS

		/// <summary>
		/// Builds the HUD on its own canvas, drawn the same way as the sample's canvas (same render mode and camera) one sorting step under
		/// it, so the sample's menus and effects stay on top.
		/// </summary>
		public static BrownoutHud Create(SceneContext context, Canvas sampleCanvas)
		{
			var go = new GameObject("BrownoutHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
			go.layer = sampleCanvas != null ? sampleCanvas.gameObject.layer : go.layer;

			var canvas = go.GetComponent<Canvas>();
			if (sampleCanvas != null)
			{
				canvas.renderMode       = sampleCanvas.renderMode;
				canvas.worldCamera      = sampleCanvas.worldCamera;
				canvas.planeDistance    = sampleCanvas.planeDistance + 1.0f;
				canvas.sortingLayerID   = sampleCanvas.sortingLayerID;
				canvas.sortingOrder     = sampleCanvas.sortingOrder - 1;
			}
			else
			{
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			}

			var scaler = go.GetComponent<CanvasScaler>();
			scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920.0f, HudSettings.Current.ReferenceHeight);
			scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			scaler.matchWidthOrHeight  = 1.0f;

			var hud = go.AddComponent<BrownoutHud>();
			hud._context  = context;
			hud._settings = HudSettings.Current;
			hud._canvas   = canvas;
			hud.Build();

			_all.Add(hud);
			return hud;
		}

		public void SetAgent(Agent agent)
		{
			if (_subscribedHealth != null)
			{
				_subscribedHealth.HitPerformed -= OnHitPerformed;
				_subscribedHealth.HitTaken     -= OnHitTakenHud;
				_subscribedHealth = null;
			}

			_agent = agent;

			if (agent != null && agent.Health != null)
			{
				_subscribedHealth = agent.Health;
				_subscribedHealth.HitPerformed += OnHitPerformed;
				_subscribedHealth.HitTaken     += OnHitTakenHud;
			}

			Readout.Clear();
			_damageDealt.Clear();
			_hitMarkerTime = -100.0f;
			_bannerTime    = -100.0f;
			_bloodAlpha    = 0.0f;
			_flashTime     = -100.0f;
		}

		public void OnAgentDeath(KillData killData)
		{
			AddKillFeed(killData);
		}

		/// <summary>A line in the yellow system feed on the left (players joining and leaving, announcements; phase 11a, was the kill feed).</summary>
		public void ShowSystem(string message)
		{
			AddSystemRow(message);
		}

		/// <summary>Every frame from UIGameplayView; shown only while the sample's canvas is.</summary>
		public void Tick(bool visible)
		{
			if (_canvas.enabled != visible)
			{
				_canvas.enabled = visible;
			}

			_scale = _canvas.scaleFactor > 0.0f ? _canvas.scaleFactor : 1.0f;

			HudKit.Show(_layoutBoxes, ShowLayoutBoxes);

			bool hasAgent = _agent != null && _agent.Object != null && _agent.Object.IsValid == true && _context.Runner != null && _context.Runner.IsRunning == true;
			bool riding   = hasAgent == false && _context.Runner != null && _context.Runner.IsRunning == true && _context.WaitingAgentTransform != null;
			// Phase 11a (reference): the open inventory takes the whole screen, the rest of the HUD hides under it
			InventoryScreen inventoryScreen = hasAgent == true ? GetInventoryScreen() : null;
			bool inventoryOpen = inventoryScreen != null && inventoryScreen.IsOpen == true && _context.HasInput == true;
			_inventoryShown = inventoryOpen;
			HudKit.Show(_root, (hasAgent || riding) && inventoryOpen == false);

			UpdateScreens(hasAgent);
			UpdateMatch(hasAgent, riding);

			if (hasAgent == false)
			{
				HudKit.Show(_inventory, false);

				// In the helicopter (phase 7): minimap with the flight path, compass, counters and the feed; M opens the full map
				if (riding == true)
				{
					_context.Runner.GetAllBehaviours(_agents);
					UpdateRiding();
					UpdateMapInput(true);
					UpdateMap(null, true);
					_agents.Clear();
				}
				else
				{
					SetMapOpen(false);
					HudKit.Show(_map, false);
					_hasPendingWaypoint = false;
				}
				return;
			}

			_context.Runner.GetAllBehaviours(_agents);

			SendPendingWaypoint();
			UpdateMapInput(false);

			UpdateTop();
			UpdateBottom();
			UpdateCenter();
			UpdateWorldMarkers();
			UpdateMap(GetTeammate(), false);
			UpdateInventory();

			_agents.Clear();
		}

		// MONOBEHAVIOUR

		private void OnDestroy()
		{
			SetMapOpen(false);
			SetAgent(null);

			_all.Remove(this);
		}

		// PRIVATE METHODS

		private void Build()
		{
			_root = HudKit.Rect(transform, "Root");
			HudKit.Stretch(_root, Vector2.zero, Vector2.one);

			// Teammate and ping markers sit under everything else
			_world = HudKit.Rect(_root, "World");
			HudKit.Stretch(_world, Vector2.zero, Vector2.zero);
			_world.pivot = Vector2.zero;

			_center = HudKit.Rect(_root, "Center");
			HudKit.Stretch(_center, Vector2.zero, Vector2.one);

			_bottom = HudKit.Rect(_root, "Bottom");
			HudKit.Stretch(_bottom, Vector2.zero, Vector2.one);

			BuildTop();
			BuildBottom();
			BuildCenter();
			BuildWorldMarkers();
			BuildScreens();
			BuildMap();
			BuildMatch();
			BuildMoments();
			BuildInventory();
			BuildLayoutBoxes();
		}

		private RectTransform _layoutBoxes;
		private bool          _inventoryShown;

		private void BuildLayoutBoxes()
		{
			_layoutBoxes = HudKit.Rect(transform, "LayoutBoxes");
			HudKit.Stretch(_layoutBoxes, Vector2.zero, Vector2.one);

			for (int i = 0; _settings.Boxes != null && i < _settings.Boxes.Length; ++i)
			{
				HudSettings.Box box  = _settings.Boxes[i];
				RectTransform  rect = HudKit.Box(_layoutBoxes, $"Box{box.Id}", box);
				Color          color = new Color(1.0f, 0.2f, 1.0f, 0.9f);

				HudKit.Stretch(HudKit.Image(rect, "Top", color).rectTransform, new Vector2(0.0f, 1.0f), Vector2.one).sizeDelta = new Vector2(0.0f, 2.0f);
				HudKit.Stretch(HudKit.Image(rect, "Bottom", color).rectTransform, Vector2.zero, new Vector2(1.0f, 0.0f)).sizeDelta = new Vector2(0.0f, 2.0f);
				HudKit.Stretch(HudKit.Image(rect, "Left", color).rectTransform, Vector2.zero, new Vector2(0.0f, 1.0f)).sizeDelta = new Vector2(2.0f, 0.0f);
				HudKit.Stretch(HudKit.Image(rect, "Right", color).rectTransform, new Vector2(1.0f, 0.0f), Vector2.one).sizeDelta = new Vector2(2.0f, 0.0f);

				var label = HudKit.Text(rect, "Label", 12.0f, TMPro.TextAlignmentOptions.TopLeft, color, true);
				HudKit.Stretch(label.rectTransform, Vector2.zero, Vector2.one);
				label.text = $"{box.Id} {box.Element}";
			}

			_layoutBoxes.gameObject.SetActive(false);
		}

		/// <summary>Display name: the player's nickname, a bot's name, or "Dummy" for a test dummy.</summary>
		private string GetName(Agent agent)
		{
			if (agent == null || agent.Object == null)
				return string.Empty;

			// Bots (phase 9) have names of their own, with a bot mark
			if (agent.IsBot == true)
				return GetBotName(agent.BotNumber);

			PlayerRef playerRef = agent.Object.InputAuthority;
			Player    player    = playerRef.IsRealPlayer == true && _context.NetworkGame != null ? _context.NetworkGame.GetPlayer(playerRef) : null;
			return player != null && string.IsNullOrEmpty(player.Nickname) == false ? player.Nickname : (playerRef.IsRealPlayer == true ? $"Player {playerRef.PlayerId}" : "Dummy");
		}

		/// <summary>A bot's name and its bot mark (phase 9b).</summary>
		private string GetBotName(byte botNumber)
		{
			return $"{BotSettings.Current.GetName(botNumber)} <size=70%><color=#{Hex(_settings.Dim)}>BOT</color></size>";
		}

		private string GetName(PlayerRef playerRef)
		{
			if (playerRef.IsRealPlayer == false)
				return "Dummy";

			Player player = _context.NetworkGame != null ? _context.NetworkGame.GetPlayer(playerRef) : null;
			return player != null && string.IsNullOrEmpty(player.Nickname) == false ? player.Nickname : $"Player {playerRef.PlayerId}";
		}

		/// <summary>The observed agent's teammate (alive or downed), or null.</summary>
		private Agent GetTeammate()
		{
			for (int i = 0; i < _agents.Count; ++i)
			{
				Agent other = _agents[i];
				if (other != null && other.Object != null && other.Object.IsValid == true && other.Health.IsAlive == true && GameplayMode.AreTeammates(_agent, other) == true)
					return other;
			}

			return null;
		}

		/// <summary>Canvas position (pixels at the reference height, from the bottom left) of a world point; false when behind the camera.</summary>
		private bool WorldToCanvas(Vector3 position, out Vector2 canvasPosition)
		{
			canvasPosition = default;

			Camera camera = _context.Camera != null ? _context.Camera.Camera : null;
			if (camera == null)
				return false;

			Vector3 screen = camera.WorldToScreenPoint(position);
			if (screen.z <= 0.0f)
				return false;

			canvasPosition = new Vector2(screen.x, screen.y) / _scale;
			return true;
		}

		/// <summary>Heading of the view in degrees (0 = north, +Z; 90 = east, +X).</summary>
		private float GetHeading()
		{
			Camera camera = _context.Camera != null ? _context.Camera.Camera : null;
			Vector3 forward = camera != null ? camera.transform.forward : _agent != null ? _agent.transform.forward : Vector3.forward;
			return Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360.0f);
		}

		private Vector3 GetViewPosition()
		{
			Camera camera = _context.Camera != null ? _context.Camera.Camera : null;
			return camera != null ? camera.transform.position : _agent.transform.position;
		}

		private static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);
	}
}
