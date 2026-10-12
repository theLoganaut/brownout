namespace TPSBR
{
	using System;
	using System.Collections.Generic;
	using UnityEngine;
#if !DISABLESTEAMWORKS
	using Steamworks;
#endif

	/// <summary>
	/// Phase 11b (Logan, 10 Oct): duos are invited through Steam (Steamworks.NET, App ID 480 Spacewar for development: steam_appid.txt at the
	/// project root for the editor and next to the game's exe in builds). Steam gives the player name and the party: a friends-only Steam lobby
	/// of 2 that INVITE opens Steam's invite dialog for; accepting the invite (or Join Game from the friends list, or a launch with
	/// +connect_lobby) joins it. The lobby owner is the party leader; when the leader's PLAY finds or hosts a Fusion session it posts the
	/// session's name in the lobby data, and the other member's menu joins the same session (SessionPosted). Matchmade teams come from the
	/// party (PartyID, sent with the player data). Without Steam running everything still works, solo.
	/// Created on first use (the main menu) and kept across scenes.
	/// </summary>
	public sealed class BrownoutSteam : MonoBehaviour
	{
		// CONSTANTS

		private const string SESSION_KEY = "session";

		// PUBLIC MEMBERS

		public static BrownoutSteam Instance { get; private set; }

		/// <summary>Steam is running and initialized.</summary>
		public bool   IsAvailable  { get; private set; }
		public string PersonaName  { get; private set; } = string.Empty;
		public string Status       { get; private set; } = string.Empty;

		/// <summary>In a Steam lobby with at least one other member.</summary>
		public bool   InParty      => _lobby != 0 && _memberCount > 1;
		/// <summary>Solo, or the party's leader (the lobby owner).</summary>
		public bool   IsLeader     => _lobby == 0 || _isOwner == true;
		public int    PartySize    => _lobby != 0 ? Mathf.Max(1, _memberCount) : 1;
		/// <summary>The party's id for matchmade teams (the leader's Steam id), empty when solo.</summary>
		public string PartyID      => InParty == true ? _ownerId : string.Empty;

		/// <summary>The other members' names (not you).</summary>
		public readonly List<string> Others = new List<string>(1);

		/// <summary>A party member (not the leader) is told which session to join.</summary>
		public event Action<string> SessionPosted;

		/// <summary>Set when the leader posts a session before this member's menu was there to hear it.</summary>
		public string PendingSession { get; set; }

		// PRIVATE MEMBERS

		private ulong  _lobby;
		private bool   _isOwner;
		private string _ownerId = string.Empty;
		private string _lastSession = string.Empty;
		private bool   _inviteWhenCreated;
		private int    _memberCount;

#if !DISABLESTEAMWORKS
		private Callback<GameLobbyJoinRequested_t> _joinRequested;
		private Callback<LobbyEnter_t>             _lobbyEntered;
		private Callback<LobbyChatUpdate_t>        _lobbyChanged;
		private Callback<LobbyDataUpdate_t>        _lobbyData;
		private CallResult<LobbyCreated_t>         _lobbyCreated;
#endif

		// PUBLIC METHODS

		/// <summary>The instance, created (and Steam started) on first use.</summary>
		public static BrownoutSteam Ensure()
		{
			if (Instance == null)
			{
				var go = new GameObject("BrownoutSteam");
				DontDestroyOnLoad(go);
				Instance = go.AddComponent<BrownoutSteam>();
			}

			return Instance;
		}

		/// <summary>INVITE: opens Steam's invite dialog for the party lobby (made first if there isn't one).</summary>
		public void Invite()
		{
#if !DISABLESTEAMWORKS
			if (IsAvailable == false)
				return;

			if (_lobby == 0)
			{
				_inviteWhenCreated = true;
				_lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 2));
				Status = "Making the party";
				return;
			}

			SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(_lobby));
#endif
		}

		public void LeaveParty()
		{
#if !DISABLESTEAMWORKS
			if (_lobby != 0 && IsAvailable == true)
			{
				SteamMatchmaking.LeaveLobby(new CSteamID(_lobby));
			}
#endif
			_lobby       = 0;
			_isOwner     = false;
			_ownerId     = string.Empty;
			_lastSession = string.Empty;
			_memberCount  = 0;
			Others.Clear();
			Status       = string.Empty;
		}

		/// <summary>The leader tells the party which Fusion session to join.</summary>
		public void PostSession(string sessionName)
		{
#if !DISABLESTEAMWORKS
			if (IsAvailable == true && _lobby != 0 && _isOwner == true)
			{
				_lastSession = sessionName;
				SteamMatchmaking.SetLobbyData(new CSteamID(_lobby), SESSION_KEY, sessionName);
			}
#endif
		}

		// MONOBEHAVIOUR

		private void Awake()
		{
#if !DISABLESTEAMWORKS
			try
			{
				if (Packsize.Test() == false || DllCheck.Test() == false)
				{
					Status = "Steam's library doesn't match";
					Debug.LogWarning("BrownoutSteam: Steamworks.NET's packsize or dll check failed; playing without Steam.");
					return;
				}

				IsAvailable = SteamAPI.Init();
			}
			catch (Exception exception)
			{
				Debug.LogWarning($"BrownoutSteam: Steam didn't start ({exception.Message}); playing without Steam.");
				IsAvailable = false;
			}

			if (IsAvailable == false)
			{
				Status = "Steam isn't running: solo only";
				Debug.Log("BrownoutSteam: Steam isn't running (or steam_appid.txt is missing); playing solo.");
				return;
			}

			PersonaName = SteamFriends.GetPersonaName();

			_joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
			_lobbyEntered  = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
			_lobbyChanged  = Callback<LobbyChatUpdate_t>.Create(OnLobbyChanged);
			_lobbyData     = Callback<LobbyDataUpdate_t>.Create(OnLobbyData);
			_lobbyCreated  = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);

			Debug.Log($"BrownoutSteam: signed in as {PersonaName}.");

			// Launched from an invite while the game wasn't running: +connect_lobby <id>
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; ++i)
			{
				if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong lobby) == true)
				{
					SteamMatchmaking.JoinLobby(new CSteamID(lobby));
				}
			}
#else
			Status = "Steam is switched off in this build";
#endif
		}

		private void Update()
		{
#if !DISABLESTEAMWORKS
			if (IsAvailable == true)
			{
				SteamAPI.RunCallbacks();
			}
#endif
		}

		private void OnDestroy()
		{
#if !DISABLESTEAMWORKS
			if (IsAvailable == true)
			{
				LeaveParty();
				SteamAPI.Shutdown();
			}
#endif
			if (Instance == this)
			{
				Instance = null;
			}
		}

		// PRIVATE METHODS

#if !DISABLESTEAMWORKS
		private void OnLobbyCreated(LobbyCreated_t result, bool failure)
		{
			if (failure == true || result.m_eResult != EResult.k_EResultOK)
			{
				Status = "Couldn't make the party";
				_inviteWhenCreated = false;
				return;
			}

			_lobby = result.m_ulSteamIDLobby;
			RefreshMembers();

			if (_inviteWhenCreated == true)
			{
				_inviteWhenCreated = false;
				SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(_lobby));
			}
		}

		private void OnJoinRequested(GameLobbyJoinRequested_t request)
		{
			// Accepted an invite (or Join Game): leave our own party first
			if (_lobby != 0 && _lobby != request.m_steamIDLobby.m_SteamID)
			{
				LeaveParty();
			}

			Status = "Joining the party";
			SteamMatchmaking.JoinLobby(request.m_steamIDLobby);
		}

		private void OnLobbyEntered(LobbyEnter_t entered)
		{
			if (entered.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
			{
				Status = "Couldn't join the party";
				return;
			}

			_lobby = entered.m_ulSteamIDLobby;
			Status = string.Empty;
			RefreshMembers();

			// A session the leader already posted counts as new for this member only if it's posted again; a member joining a party
			// whose leader is already in a match waits for the next one
			_lastSession = SteamMatchmaking.GetLobbyData(new CSteamID(_lobby), SESSION_KEY);
		}

		private void OnLobbyChanged(LobbyChatUpdate_t change)
		{
			if (change.m_ulSteamIDLobby == _lobby)
			{
				RefreshMembers();
			}
		}

		private void OnLobbyData(LobbyDataUpdate_t update)
		{
			if (update.m_ulSteamIDLobby != _lobby || _lobby == 0)
				return;

			RefreshMembers();

			if (_isOwner == true)
				return;

			string session = SteamMatchmaking.GetLobbyData(new CSteamID(_lobby), SESSION_KEY);
			if (string.IsNullOrEmpty(session) == false && session != _lastSession)
			{
				_lastSession = session;
				Debug.Log($"BrownoutSteam: the leader posted session {session}.");

				if (SessionPosted != null)
				{
					SessionPosted.Invoke(session);
				}
				else
				{
					PendingSession = session;
				}
			}
		}

		private void RefreshMembers()
		{
			var lobby = new CSteamID(_lobby);
			CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);
			CSteamID self  = SteamUser.GetSteamID();

			_isOwner    = owner == self;
			_ownerId    = owner.m_SteamID.ToString();
			_memberCount = SteamMatchmaking.GetNumLobbyMembers(lobby);

			Others.Clear();
			for (int i = 0; i < _memberCount; ++i)
			{
				CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
				if (member != self)
				{
					Others.Add(SteamFriends.GetFriendPersonaName(member));
				}
			}
		}
#endif
	}
}
