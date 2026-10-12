namespace TPSBR
{
	using UnityEngine;
	using Fusion;
	using Fusion.Addons.InterestManagement;
	using Fusion.Addons.KCC;

	// Replicated to all players - 200 players means 200x200 relationships every send tick.
	// Keep this struct as small as possible, pack values instead of adding fields.
	public struct PlayerStatistics : INetworkStruct
	{
		public PlayerRef PlayerRef;
		public TickTimer RespawnTimer;
		public TickTimer KillsInRowCooldown;

		public bool      IsValid           { get { return PlayerRef.IsRealPlayer; } }
		public bool      IsAlive           { get { return _flags.IsBitSet(0); } set { _flags.SetBit(0, value); } }
		public bool      IsEliminated      { get { return _flags.IsBitSet(1); } set { _flags.SetBit(1, value); } }

		public byte      Kills             { get { return _packedA.GetByte( 0); } set { _packedA.SetByte( 0, value); } }
		public byte      Deaths            { get { return _packedA.GetByte( 8); } set { _packedA.SetByte( 8, value); } }
		public byte      Position          { get { return _packedA.GetByte(16); } set { _packedA.SetByte(16, value); } }
		public byte      ExtraLives        { get { return _packedA.GetByte(24); } set { _packedA.SetByte(24, value); } }

		public short     Score             { get { return _packedB.GetShort(0); } set { _packedB.SetShort(0, value); } }
		public byte      KillsInRow        { get { return _packedB.GetByte(16); } set { _packedB.SetByte(16, value); } }
		public byte      KillsWithoutDeath { get { return _packedB.GetByte(24); } set { _packedB.SetByte(24, value); } }

		private byte     _flags;
		private uint     _packedA;
		private uint     _packedB;
	}

	public sealed class Player : PlayerInterestManager, IPlayer, IContextBehaviour
	{
		// PUBLIC MEMBERS

		public string           UserID         { get; private set; }
		public string           Nickname       { get; private set; }
		/// <summary>Phase 11b: the Steam party this player came with (the leader's Steam id; empty when solo), known on the server for matchmade teams.</summary>
		public string           PartyID        { get; private set; } = string.Empty;
		public SceneContext     Context        { get; set; }
		public Agent            ActiveAgent    => _activeAgent;
		public bool             IsInitialized  => _initCounter <= 0;
		public bool             IsSynchronized => SyncToken != default;

		[Networked]
		public NetworkPrefabRef AgentPrefab    { get; set; }
		[Networked]
		public PlayerStatistics Statistics     { get; private set; }
		/// <summary>Who this player is watching (themselves when not spectating), so the HUD can count spectators on every peer (phase 6a).</summary>
		[Networked]
		public PlayerRef        Watching       { get; private set; }

		// PRIVATE MEMBERS

		[Networked]
		private byte SyncToken { get; set; }
		[Networked]
		private NetworkObject SyncAgent { get; set; }
		[Networked]
		private NetworkString<_16> NetworkedNickname { get; set; }

		private byte      _syncToken;
		private Agent     _activeAgent;
		private PlayerRef _observePlayer;
		private Vector3   _lastViewPosition;
		private Vector3   _lastViewDirection;
		private Agent     _platformAgent;
		private bool      _playerDataSent;
		private int       _initCounter;

		// PUBLIC METHODS

		public void SetActiveAgent(Agent agent)
		{
			SyncAgent = agent.Object;

			UpdateLocalState(agent);
		}

		public void DespawnAgent()
		{
			if (Runner.IsServer == false)
				return;

			if (SyncAgent != null)
			{
				Runner.Despawn(SyncAgent);
				SyncAgent = null;
				_activeAgent = null;
				_platformAgent = null;
			}
		}

		public void UpdateStatistics(PlayerStatistics statistics)
		{
			Statistics = statistics;
		}

		public void SetObservedPlayer(PlayerRef playerRef)
		{
			if (playerRef.IsRealPlayer == false)
			{
				playerRef = Object.InputAuthority;
			}

			if (playerRef == _observePlayer)
				return;

			RPC_SetObservedPlayer(playerRef);
		}

		public void Refresh()
		{
			PlayerStatistics statistics = Statistics;
			statistics.PlayerRef = Object.InputAuthority;
			Statistics = statistics;
		}

		public void OnReconnect(Player newPlayer)
		{
			UserID            = newPlayer.UserID;
			Nickname          = newPlayer.Nickname;
			PartyID           = newPlayer.PartyID;
			NetworkedNickname = newPlayer.NetworkedNickname;
			AgentPrefab       = newPlayer.AgentPrefab;
		}

		// PlayerInterestManager INTERFACE

		protected override void OnSpawned()
		{
			NetworkObject syncAgent = SyncAgent;
			Agent activeAgent = Runner.Exists(syncAgent) == true ? syncAgent.GetBehaviour<Agent>() : null;

			_syncToken      = default;
			_activeAgent    = activeAgent;
			_observePlayer  = Object.InputAuthority;
			_playerDataSent = false;
			_initCounter    = 10;

			if (HasInputAuthority == true)
			{
				Context.LocalPlayerRef = Object.InputAuthority;
			}

			UpdateLocalState(activeAgent);

			Runner.SetIsSimulated(Object, true);
		}

		protected override void OnDespawned(NetworkRunner runner, bool hasState)
		{
			DespawnAgent();
		}

		protected override void OnFixedUpdateNetwork()
		{
			NetworkObject syncAgent = SyncAgent;
			Agent activeAgent = Runner.Exists(syncAgent) == true ? syncAgent.GetBehaviour<Agent>() : null;

			UpdateLocalState(activeAgent);

			if (_syncToken != default && Runner.IsForward == true)
			{
				_initCounter = Mathf.Max(0, _initCounter - 1);
			}

			if (IsProxy == true)
				return;

			var observedAgent = activeAgent;

			// Phase 6b: only that player's own agent counts. A pooled agent object can be reused by a test dummy after the player's agent
			// leaves this peer's interest (found in 5a), so check whose input it is before watching it
			var observedPlayer = Context.NetworkGame.GetPlayer(_observePlayer);
			if (observedPlayer != null && IsAgentOf(observedPlayer.ActiveAgent, _observePlayer) == true)
			{
				observedAgent = observedPlayer.ActiveAgent;
			}

			var observedPlayerRef = observedAgent != null ? observedAgent.Object.InputAuthority : Object.InputAuthority;

			if (HasStateAuthority == true)
			{
				ObservedPlayer = observedPlayerRef;
				Watching       = observedPlayerRef;
			}

			if (HasInputAuthority == true)
			{
				Context.ObservedAgent     = observedAgent;
				Context.ObservedPlayerRef = observedPlayerRef;
				Context.LocalPlayerRef    = Object.InputAuthority;

				if (_playerDataSent == false && Runner.IsForward == true && Context.PlayerData != null)
				{
					RPC_SendPlayerData(Context.PeerUserID, Context.PlayerData.Nickname, Context.PlayerData.AgentPrefab, BrownoutSteam.Instance != null ? BrownoutSteam.Instance.PartyID : string.Empty);
					_playerDataSent = true;
				}
			}
		}

		public override bool CanUpdatePlayerInterest(bool isUpdateRequested)
		{
			if (isUpdateRequested == true)
				return true;

			int interlacing = 50;
			int currentTick = Runner.Tick;

			if ((currentTick - LastUpdateTick) >= interlacing)
			{
				if ((currentTick % interlacing) == (Player.AsIndex % interlacing))
					return true;
			}

			if (TryGetObservedPlayerView(out PlayerInterestView observedPlayerView) == true)
			{
				if (Vector3.SqrMagnitude(_lastViewPosition - observedPlayerView.CameraPosition) > 4.0f) // 2 meter distance change
					return true;
				if (Vector3.Dot(_lastViewDirection, observedPlayerView.CameraDirection) < 0.99f) // Roughly 8 degrees change
					return true;
			}

			return false;
		}

		protected override void AfterPlayerInterestUpdate(bool success, PlayerInterestView playerView)
		{
			if (success == true)
			{
				_lastViewPosition  = playerView.CameraPosition;
				_lastViewDirection = playerView.CameraDirection;
			}
		}

		// PRIVATE METHODS

		private static bool IsAgentOf(Agent agent, PlayerRef playerRef)
		{
			return agent != null && agent.Object != null && agent.Object.IsValid == true && agent.Object.InputAuthority == playerRef;
		}

		private void UpdateLocalState(Agent activeAgent)
		{
			// Phase 6b: a reused pooled object isn't this player's agent any more
			if (activeAgent != null && IsAgentOf(activeAgent, Object.InputAuthority) == false)
			{
				activeAgent = null;
			}

			if (_activeAgent != activeAgent)
			{
				_activeAgent   = activeAgent;
				_observePlayer = Object.InputAuthority;

				if (_activeAgent != null)
				{
					InterestView = _activeAgent.InterestView;
				}
			}

			if (_syncToken != SyncToken)
			{
				_syncToken = SyncToken;

				Nickname = NetworkedNickname.Value;
			}

			if (ReferenceEquals(_platformAgent, _activeAgent) == false && _activeAgent != null)
			{
				if (_activeAgent.Character.CharacterController.IsSpawned == true)
				{
					if (PlatformProcessor.MAX_PLATFORMS != 1)
					{
						#pragma warning disable 0162 // Unreachable code detected
						Debug.LogWarning($"BR200 was built with max 1 synchronized platform (performance optimization, set {nameof(PlatformProcessor)}.{nameof(PlatformProcessor.MAX_PLATFORMS)}). Either revert the value to 1 or delete this log if you need to synchronize more platforms.");
						#pragma warning restore 0162 // Unreachable code detected
					}

					PlatformProcessor platformProcessor = GetComponent<PlatformProcessor>();
					_activeAgent.Character.CharacterController.AddLocalProcessor(platformProcessor);
					_platformAgent = _activeAgent;
				}
			}
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
		private void RPC_SendPlayerData(string userID, string nickname, NetworkPrefabRef agentPrefab, string partyID)
		{
			PartyID = partyID ?? string.Empty;

			#if UNITY_EDITOR
			nickname += $" {Object.InputAuthority}";
			#endif

			++SyncToken;
			if (SyncToken == default)
			{
				SyncToken = 1;
			}

			_syncToken = SyncToken;

			UserID = userID;
			Nickname = nickname;
			NetworkedNickname = nickname;
			AgentPrefab = agentPrefab;
		}

		[Rpc(RpcSources.StateAuthority | RpcSources.InputAuthority, RpcTargets.StateAuthority | RpcTargets.InputAuthority, Channel = RpcChannel.Reliable)]
		private void RPC_SetObservedPlayer(PlayerRef player)
		{
			_observePlayer = player;
		}
	}
}
