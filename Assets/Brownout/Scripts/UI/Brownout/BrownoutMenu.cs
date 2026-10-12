namespace TPSBR.UI
{
	using System.Collections.Generic;
	using Fusion;
	using Fusion.Photon.Realtime;
	using TMPro;
	using UnityEngine;
	using UnityEngine.UI;

	/// <summary>
	/// Phase 11b: Brownout's main menu, built in code on its own canvas one sorting step under the sample's menu canvas (like the HUD), so
	/// the sample's popups (settings with our Controls page, the quit dialog) still draw on top. The sample's main menu view stays open
	/// underneath as the menu's state but is drawn invisible (its agent preview, the soldier, stays as the backdrop); its session list,
	/// map picker, agent selection, nickname and credits are no longer reachable.
	/// PLAY (solo or the party leader): joins the Photon lobby, picks the open Brownout match still in its warm-up with the most players
	/// and room for the party, or hosts a new one (with the 30 s warm-up countdown); the session is posted to the Steam party so the duo
	/// joins the same one. A party member's PLAY waits for the leader.
	/// </summary>
	public sealed class BrownoutMenu : MonoBehaviour
	{
		// PRIVATE MEMBERS

		private enum EState
		{
			Idle,
			FindingLobby,
			Starting,
		}

		private const float LOBBY_TIMEOUT    = 20.0f;
		private const float SESSION_LIST_WAIT = 3.0f;

		private SceneContext    _context;
		private SceneUI         _sceneUI;
		private HudSettings     _settings;
		private Canvas          _canvas;

		private RectTransform   _root;
		private Button          _playButton;
		private TextMeshProUGUI _playText;
		private Button          _settingsButton;
		private Button          _quitButton;
		private TextMeshProUGUI _status;
		private TextMeshProUGUI _partyYou;
		private TextMeshProUGUI _partyOther;
		private Button          _inviteButton;
		private TextMeshProUGUI _inviteText;
		private TextMeshProUGUI _partyNote;

		private EState          _state;
		private float           _stateTime;
		private float           _lobbyJoinedTime = -1.0f;
		private bool            _hasSessionList;
		private readonly List<SessionInfo> _sessions = new List<SessionInfo>(16);
		private BrownoutSteam   _steam;
		private string          _lastError = string.Empty;

		/// <summary>Test hook: the menu's state and status line.</summary>
		public string Readout => $"{_state} | {(_status != null ? _status.text : string.Empty)}";
		public static BrownoutMenu Current { get; private set; }

		// PUBLIC METHODS

		public static BrownoutMenu Create(SceneContext context, SceneUI sceneUI)
		{
			Canvas sampleCanvas = sceneUI.Canvas;

			var go = new GameObject("BrownoutMenu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			go.layer = sampleCanvas != null ? sampleCanvas.gameObject.layer : go.layer;
			go.transform.SetParent(sceneUI.transform, false);

			// The sample's MenuUI object is itself a canvas, so this one is nested in it: fill it, and sort just under the sample's
			// views (they override their sorting at 1 and up) so its popups still draw on top
			var rect = (RectTransform)go.transform;
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;

			var canvas = go.GetComponent<Canvas>();
			canvas.overrideSorting = true;
			if (sampleCanvas != null)
			{
				canvas.sortingLayerID = sampleCanvas.sortingLayerID;
				canvas.sortingOrder   = sampleCanvas.sortingOrder - 1;
			}

			// The sample's BR200 logo on the menu background
			foreach (GameObject sceneRoot in sceneUI.gameObject.scene.GetRootGameObjects())
			{
				Transform logo = sceneRoot.name == "MenuUIBackground" ? sceneRoot.transform.Find("BR200") : null;
				if (logo != null)
				{
					logo.gameObject.SetActive(false);
				}
			}

			var scaler = go.GetComponent<CanvasScaler>();
			scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
			scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			scaler.matchWidthOrHeight  = 1.0f;

			var menu = go.AddComponent<BrownoutMenu>();
			menu._context  = context;
			menu._sceneUI  = sceneUI;
			menu._settings = HudSettings.Current;
			menu._canvas   = canvas;
			menu._steam    = BrownoutSteam.Ensure();
			menu.Build();

			Current = menu;
			return menu;
		}

		/// <summary>Test hook: PLAY as if clicked.</summary>
		public void TestPlay() => OnPlay();

		// MONOBEHAVIOUR

		private void OnEnable()
		{
			if (_steam != null)
			{
				_steam.SessionPosted += OnSessionPosted;
			}
		}

		private void Start()
		{
			// A matchmade match is hosted from here only; a scene started in the editor waits for Enter
			BattleRoyaleGameplayMode.UseWarmupCountdown = false;

			_steam.SessionPosted -= OnSessionPosted;
			_steam.SessionPosted += OnSessionPosted;

			if (string.IsNullOrEmpty(_steam.PendingSession) == false)
			{
				string session = _steam.PendingSession;
				_steam.PendingSession = null;
				OnSessionPosted(session);
			}
		}

		private void OnDisable()
		{
			if (_steam != null)
			{
				_steam.SessionPosted -= OnSessionPosted;
			}

			StopListening();
		}

		private void OnDestroy()
		{
			if (Current == this)
			{
				Current = null;
			}
		}

		private void Update()
		{
			// The sample's main menu view stays open underneath, drawn invisible
			UIMainMenuView mainMenu = _sceneUI.Get<UIMainMenuView>();
			bool menuOnTop = mainMenu != null && mainMenu.IsOpen == true && _sceneUI.IsTopView(mainMenu);
			if (mainMenu != null && mainMenu.IsOpen == true)
			{
				mainMenu.CanvasGroup.alpha          = 0.0f;
				mainMenu.CanvasGroup.blocksRaycasts = false;
			}

			HudKit.Show(_root, mainMenu == null || mainMenu.IsOpen == true);

			bool member = _steam.InParty == true && _steam.IsLeader == false;
			bool busy   = _state != EState.Idle;

			_playButton.interactable     = menuOnTop == true && member == false && _state != EState.Starting;
			_settingsButton.interactable = menuOnTop == true && busy == false;
			_quitButton.interactable     = menuOnTop == true && busy == false;
			_inviteButton.interactable   = menuOnTop == true && _steam.IsAvailable == true && _steam.IsLeader == true && _steam.InParty == false;

			HudKit.Set(_playText, member == true ? "WAITING FOR THE LEADER" : (_state == EState.FindingLobby ? "CANCEL" : "PLAY"));

			UpdateMatchmaking();
			UpdateParty();
		}

		// PRIVATE METHODS

		private void Build()
		{
			HudSettings s = _settings;

			_root = HudKit.Rect(transform, "Root");
			HudKit.Stretch(_root, Vector2.zero, Vector2.one);

			// A dark band down the left for the text, fading into the soldier backdrop
			Image band = HudKit.Image(_root, "Band", new Color(0.0f, 0.0f, 0.0f, 0.72f));
			HudKit.Stretch(band.rectTransform, Vector2.zero, new Vector2(0.36f, 1.0f));
			Image fade = HudKit.Image(_root, "Fade", new Color(0.0f, 0.0f, 0.0f, 0.72f), MakeFade());
			HudKit.Stretch(fade.rectTransform, new Vector2(0.36f, 0.0f), new Vector2(0.56f, 1.0f));

			// Title
			TextMeshProUGUI title = HudKit.Label(_root, "Title", 104.0f, TextAlignmentOptions.TopLeft, s.Text);
			Place(title.rectTransform, new Vector2(110.0f, -120.0f), new Vector2(900.0f, 130.0f));
			title.characterSpacing = 18.0f;
			title.text = "BROWNOUT";

			TextMeshProUGUI subtitle = HudKit.Label(_root, "Subtitle", 22.0f, TextAlignmentOptions.TopLeft, s.Team);
			Place(subtitle.rectTransform, new Vector2(116.0f, -255.0f), new Vector2(700.0f, 30.0f));
			subtitle.text = "BATTLE ROYALE  ·  DUOS";

			// Buttons
			_playButton     = MakeButton(_root, "Play", new Vector2(110.0f, -380.0f), new Vector2(420.0f, 74.0f), 34.0f, true, out _playText);
			_settingsButton = MakeButton(_root, "Settings", new Vector2(110.0f, -476.0f), new Vector2(420.0f, 56.0f), 24.0f, false, out TextMeshProUGUI settingsText);
			_quitButton     = MakeButton(_root, "Quit", new Vector2(110.0f, -544.0f), new Vector2(420.0f, 56.0f), 24.0f, false, out TextMeshProUGUI quitText);
			_playText.text  = "PLAY";
			settingsText.text = "SETTINGS";
			quitText.text     = "QUIT";

			_playButton.onClick.AddListener(OnPlay);
			_settingsButton.onClick.AddListener(OnSettings);
			_quitButton.onClick.AddListener(OnQuit);

			_status = HudKit.Text(_root, "Status", 20.0f, TextAlignmentOptions.TopLeft, s.Text);
			Place(_status.rectTransform, new Vector2(112.0f, -618.0f), new Vector2(560.0f, 60.0f));
			_status.textWrappingMode = TextWrappingModes.Normal;

			// Party: you, your duo (or INVITE), a note
			RectTransform party = HudKit.Rect(_root, "Party");
			party.anchorMin = party.anchorMax = new Vector2(0.0f, 0.0f);
			party.pivot     = new Vector2(0.0f, 0.0f);
			party.sizeDelta = new Vector2(420.0f, 190.0f);
			party.anchoredPosition = new Vector2(110.0f, 90.0f);

			Image chip = HudKit.Image(party, "Chip", s.Chip);
			Place(chip.rectTransform, new Vector2(0.0f, 0.0f), new Vector2(110.0f, 24.0f));
			TextMeshProUGUI chipText = HudKit.Label(chip.rectTransform, "Text", 16.0f, TextAlignmentOptions.Center, new Color(0.08f, 0.08f, 0.08f, 1.0f));
			HudKit.Stretch(chipText.rectTransform, Vector2.zero, Vector2.one);
			chipText.text = "PARTY";

			_partyYou   = PartyRow(party, -34.0f);
			_partyOther = PartyRow(party, -92.0f);

			_inviteButton = MakeButton((RectTransform)_partyOther.transform.parent, "Invite", Vector2.zero, Vector2.zero, 20.0f, false, out _inviteText);
			HudKit.Stretch((RectTransform)_inviteButton.transform, Vector2.zero, Vector2.one);
			_inviteText.text = "+  INVITE A FRIEND";
			_inviteButton.onClick.AddListener(OnInvite);

			_partyNote = HudKit.Text(party, "Note", 16.0f, TextAlignmentOptions.TopLeft, s.Dim);
			Place(_partyNote.rectTransform, new Vector2(0.0f, -152.0f), new Vector2(560.0f, 40.0f));
			_partyNote.textWrappingMode = TextWrappingModes.Normal;

			TextMeshProUGUI version = HudKit.Text(_root, "Version", 15.0f, TextAlignmentOptions.BottomRight, s.Dim);
			version.rectTransform.anchorMin = version.rectTransform.anchorMax = new Vector2(1.0f, 0.0f);
			version.rectTransform.pivot     = new Vector2(1.0f, 0.0f);
			version.rectTransform.sizeDelta = new Vector2(400.0f, 24.0f);
			version.rectTransform.anchoredPosition = new Vector2(-30.0f, 20.0f);
			version.text = $"BROWNOUT  {Application.version}";
		}

		private TextMeshProUGUI PartyRow(RectTransform parent, float y)
		{
			RectTransform row = HudKit.Rect(parent, "Row");
			Place(row, new Vector2(0.0f, y), new Vector2(420.0f, 50.0f));
			HudKit.Panel(row, "Back", _settings.WidgetBack);

			TextMeshProUGUI text = HudKit.Text(row, "Name", 22.0f, TextAlignmentOptions.Left, _settings.Text, true);
			HudKit.Stretch(text.rectTransform, Vector2.zero, Vector2.one);
			text.rectTransform.offsetMin = new Vector2(16.0f, 0.0f);
			text.rectTransform.offsetMax = new Vector2(-16.0f, 0.0f);
			return text;
		}

		private Button MakeButton(RectTransform parent, string name, Vector2 topLeft, Vector2 size, float fontSize, bool primary, out TextMeshProUGUI label)
		{
			RectTransform rect = HudKit.Rect(parent, name);
			Place(rect, topLeft, size);

			Image back = HudKit.Image(rect, "Back", Color.white);
			HudKit.Stretch(back.rectTransform, Vector2.zero, Vector2.one);
			back.raycastTarget = true;

			var button = rect.gameObject.AddComponent<Button>();
			button.targetGraphic = back;
			ColorBlock colors = button.colors;
			colors.normalColor      = primary == true ? _settings.Team : new Color(0.0f, 0.0f, 0.0f, 0.0f);
			colors.highlightedColor = primary == true ? Color.Lerp(_settings.Team, Color.white, 0.35f) : new Color(1.0f, 1.0f, 1.0f, 0.12f);
			colors.pressedColor     = primary == true ? Color.Lerp(_settings.Team, Color.black, 0.2f) : new Color(1.0f, 1.0f, 1.0f, 0.2f);
			colors.selectedColor    = colors.normalColor;
			colors.disabledColor    = primary == true ? new Color(_settings.Team.r, _settings.Team.g, _settings.Team.b, 0.35f) : new Color(0.0f, 0.0f, 0.0f, 0.0f);
			colors.fadeDuration     = 0.08f;
			button.colors = colors;

			label = HudKit.Label(rect, "Text", fontSize, TextAlignmentOptions.Left, primary == true ? new Color(0.08f, 0.08f, 0.08f, 1.0f) : _settings.Text);
			HudKit.Stretch(label.rectTransform, Vector2.zero, Vector2.one);
			label.rectTransform.offsetMin = new Vector2(primary == true ? 26.0f : 16.0f, 0.0f);
			return button;
		}

		private void UpdateParty()
		{
			string you = _context.PlayerData != null ? _context.PlayerData.Nickname : string.Empty;
			HudKit.Set(_partyYou, $"<color=#{ColorUtility.ToHtmlStringRGB(_settings.Team)}>1</color>   {you}{(_steam.InParty == true && _steam.IsLeader == true ? "   <size=70%><color=#FFFFFF80>LEADER</color></size>" : string.Empty)}");

			bool hasOther = _steam.Others.Count > 0;
			HudKit.Show(_inviteButton, hasOther == false);
			HudKit.Set(_partyOther, hasOther == true ? $"<color=#{ColorUtility.ToHtmlStringRGB(_settings.Team)}>2</color>   {_steam.Others[0]}{(_steam.IsLeader == false ? "   <size=70%><color=#FFFFFF80>LEADER</color></size>" : string.Empty)}" : string.Empty);
			HudKit.Set(_inviteText, _steam.IsAvailable == true ? (_steam.IsLeader == true ? "+  INVITE A FRIEND" : string.Empty) : "STEAM ISN'T RUNNING");

			string note = _steam.Status;
			if (note.Length == 0)
			{
				note = hasOther == true
					? (_steam.IsLeader == true ? "Your duo comes with you when you press PLAY." : "The leader starts the match; you'll follow.")
					: "Playing alone: a bot fills your duo.";
			}
			HudKit.Set(_partyNote, note);
		}

		// Matchmaking

		private void OnPlay()
		{
			if (_state == EState.FindingLobby)
			{
				SetState(EState.Idle, "Cancelled.");
				StopListening();
				return;
			}

			if (_state != EState.Idle || (_steam.InParty == true && _steam.IsLeader == false))
				return;

			if (PhotonAppSettings.Global.AppSettings.AppIdFusion.HasValue() == false)
			{
				SetState(EState.Idle, "The Fusion App Id isn't set in the Photon App Settings.");
				return;
			}

			_sessions.Clear();
			_hasSessionList  = false;
			_lobbyJoinedTime = -1.0f;

			_context.Matchmaking.SessionListUpdated -= OnSessionListUpdated;
			_context.Matchmaking.SessionListUpdated += OnSessionListUpdated;

			SetState(EState.FindingLobby, "Finding a match…");
			_ = _context.Matchmaking.JoinLobby(false);
		}

		private void UpdateMatchmaking()
		{
			if (_state != EState.FindingLobby)
				return;

			Matchmaking matchmaking = _context.Matchmaking;

			if (matchmaking.IsConnectedToLobby == true && _lobbyJoinedTime < 0.0f)
			{
				_lobbyJoinedTime = Time.unscaledTime;
			}

			if (matchmaking.IsConnectedToLobby == false && matchmaking.IsJoiningToLobby == false && Time.unscaledTime - _stateTime > 2.0f && _lobbyJoinedTime < 0.0f)
			{
				// The join finished without connecting (region down, no internet)
				SetState(EState.Idle, "Couldn't reach the matchmaking server. Check your connection and try again.");
				StopListening();
				return;
			}

			if (Time.unscaledTime - _stateTime > LOBBY_TIMEOUT)
			{
				SetState(EState.Idle, "Matchmaking timed out. Try again.");
				StopListening();
				return;
			}

			// The session list usually arrives right after joining the lobby; an empty lobby sends none, so wait a moment
			if (_lobbyJoinedTime >= 0.0f && (_hasSessionList == true || Time.unscaledTime - _lobbyJoinedTime > SESSION_LIST_WAIT))
			{
				PickSession();
			}
		}

		private void PickSession()
		{
			MapSetup map = GetMenuMap();
			if (map == null)
			{
				SetState(EState.Idle, $"The map {MatchSettings.Current.MenuMapID} isn't in MapSettings.");
				StopListening();
				return;
			}

			int partySize = _steam.PartySize;

			SessionInfo best = null;
			for (int i = 0; i < _sessions.Count; ++i)
			{
				SessionInfo session = _sessions[i];
				if (session.IsValid == false || session.IsOpen == false || session.IsVisible == false)
					continue;
				if (session.GetGameplayType() != EGameplayType.BattleRoyale)
					continue;
				MapSetup sessionMap = session.GetMapSetup();
				if (sessionMap == null || sessionMap.ID != map.ID)
					continue;
				if (session.PlayerCount + partySize > session.MaxPlayers)
					continue;

				if (best == null || session.PlayerCount > best.PlayerCount)
				{
					best = session;
				}
			}

			StopListening();

			if (best != null)
			{
				BattleRoyaleGameplayMode.UseWarmupCountdown = false;
				SetState(EState.Starting, $"Joining {best.GetDisplayName()} ({best.PlayerCount}/{best.MaxPlayers})…");
				_steam.PostSession(best.Name);
				_context.Matchmaking.JoinSession(best);
				Debug.Log($"BrownoutMenu: joining session {best.Name} ({best.PlayerCount}/{best.MaxPlayers}).");
				return;
			}

			string sessionName = System.Guid.NewGuid().ToString("N");
			var request = new SessionRequest
			{
				DisplayName  = $"{_context.PlayerData.Nickname}'s match",
				SessionName  = sessionName,
				GameMode     = GameMode.Host,
				GameplayType = EGameplayType.BattleRoyale,
				MaxPlayers   = Mathf.Max(2, MatchSettings.Current.SessionMaxPlayers),
				ScenePath    = map.ScenePath,
			};

			BattleRoyaleGameplayMode.UseWarmupCountdown = true;
			SetState(EState.Starting, "No open match: hosting a new one…");
			_steam.PostSession(sessionName);
			_context.Matchmaking.CreateSession(request);
			Debug.Log($"BrownoutMenu: hosting session {sessionName}.");
		}

		/// <summary>A party member: the leader found or made a session; follow.</summary>
		private void OnSessionPosted(string sessionName)
		{
			if (_steam.IsLeader == true || _state == EState.Starting)
				return;

			MapSetup map = GetMenuMap();
			if (map == null)
				return;

			StopListening();
			BattleRoyaleGameplayMode.UseWarmupCountdown = false;
			SetState(EState.Starting, "Joining your leader's match…");

			// Give the leader's host a few seconds to come up before joining it
			StartCoroutine(JoinAfter(sessionName, map.ScenePath, 6.0f));
		}

		private System.Collections.IEnumerator JoinAfter(string sessionName, string scenePath, float delay)
		{
			yield return new WaitForSecondsRealtime(delay);
			_context.Matchmaking.JoinSession(sessionName, EGameplayType.BattleRoyale, scenePath);
		}

		private void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessions)
		{
			_sessions.Clear();
			_sessions.AddRange(sessions);
			_hasSessionList = true;
		}

		private void StopListening()
		{
			if (_context != null && _context.Matchmaking != null)
			{
				_context.Matchmaking.SessionListUpdated -= OnSessionListUpdated;
			}
		}

		private void SetState(EState state, string status)
		{
			_state     = state;
			_stateTime = Time.unscaledTime;
			HudKit.Set(_status, status);
		}

		private static MapSetup GetMenuMap()
		{
			MapSetup[] maps = Global.Settings.Map.Maps;
			for (int i = 0; i < maps.Length; ++i)
			{
				if (maps[i] != null && maps[i].ID == MatchSettings.Current.MenuMapID)
					return maps[i];
			}
			return null;
		}

		// Buttons

		private void OnSettings()
		{
			_sceneUI.Open<UISettingsView>();
		}

		private void OnQuit()
		{
			var dialog = _sceneUI.Open<UIYesNoDialogView>();

			dialog.Title.text       = "EXIT GAME";
			dialog.Description.text = "Are you sure you want to exit the game?";
			dialog.YesButtonText.text = "EXIT";
			dialog.NoButtonText.text  = "CANCEL";

			dialog.HasClosed += (result) =>
			{
				if (result == true)
				{
					_sceneUI.Scene.Quit();
				}
			};
		}

		private void OnInvite()
		{
			_steam.Invite();
		}

		private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
		{
			rect.anchorMin = new Vector2(0.0f, 1.0f);
			rect.anchorMax = new Vector2(0.0f, 1.0f);
			rect.pivot     = new Vector2(0.0f, 1.0f);
			rect.sizeDelta = size;
			rect.anchoredPosition = topLeft;
		}

		/// <summary>A left-to-right fade (opaque on the left), for the band's edge.</summary>
		private static Sprite MakeFade()
		{
			const int width = 64;
			var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
			for (int x = 0; x < width; ++x)
			{
				texture.SetPixel(x, 0, new Color(1.0f, 1.0f, 1.0f, Mathf.SmoothStep(1.0f, 0.0f, x / (float)(width - 1))));
			}
			texture.Apply(false, true);
			return Sprite.Create(texture, new Rect(0, 0, width, 1), new Vector2(0.5f, 0.5f), 100.0f);
		}
	}
}
