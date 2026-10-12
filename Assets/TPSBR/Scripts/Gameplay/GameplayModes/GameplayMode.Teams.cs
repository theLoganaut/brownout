using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Duos (phase 4): teams in join order, downed instead of dead while a teammate is up, team out, spectating the teammate,
	/// and the server-spawned test dummies (no AI, no player behind them) that fill teams for testing.
	/// </summary>
	public abstract partial class GameplayMode
	{
		// CONSTANTS

		private const byte FIRST_DUMMY_TEAM = 100;

		// PUBLIC MEMBERS

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>
		/// Test hook: everyone counts as their own team (no downed state, friendly fire on), so older checks like the Roster Check
		/// can shoot the other player as before. Static, so every peer in Multipeer agrees.
		/// </summary>
		public static bool DebugNoTeams;

		/// <summary>Test hook (phase 9b): every player is alone on a team (bots still team up), to test the lone player's bot teammate with 2 peers.</summary>
		public static bool DebugSoloTeams;
#endif

		public int DummyCount => _dummies.Count;

		/// <summary>Dead dummies come back where they stood (Deathmatch, warm-up); Battle Royale turns this off once the match starts.</summary>
		protected virtual bool DummiesRespawn => true;

		// PRIVATE MEMBERS

		private struct Dummy
		{
			public Agent      Agent;
			public Vector3    Position;
			public Quaternion Rotation;
			public byte       Team;
			public byte       BotNumber;   // Phase 9: > 0 for a bot
			public TickTimer  RespawnTimer;
		}

		private readonly Dictionary<PlayerRef, byte> _playerTeams = new Dictionary<PlayerRef, byte>(32);
		private readonly List<Dummy>                 _dummies     = new List<Dummy>(32);
		private readonly List<Agent>                 _teamBuffer  = new List<Agent>(64);
		private int  _teamSlots;
		private byte _nextDummyTeam = FIRST_DUMMY_TEAM;

		// PUBLIC METHODS

		/// <summary>The agent's team, or 0 (no team) when teams are off.</summary>
		public static int GetTeam(Agent agent)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (DebugNoTeams == true)
				return 0;
#endif
			return agent != null && agent.Object != null && agent.Object.IsValid == true ? agent.Team : 0;
		}

		public static bool AreTeammates(Agent a, Agent b)
		{
			if (a == null || b == null || a == b)
				return false;

			int team = GetTeam(a);
			return team > 0 && team == GetTeam(b);
		}

		/// <summary>Server: the player's team, given out in join order (duos: the first two players are team 1, the next two team 2...).</summary>
		public byte GetPlayerTeam(PlayerRef playerRef)
		{
			if (_playerTeams.TryGetValue(playerRef, out byte team) == true)
				return team;

			int size = Mathf.Max(1, HealthSettings.Current.TeamSize);

			// Phase 11b: a matchmade match (from the main menu) teams players by Steam party, not join order; a player who came alone
			// gets a team of their own (and a bot teammate)
			if (BattleRoyaleGameplayMode.UseWarmupCountdown == true)
			{
				Player player = Context.NetworkGame.GetPlayer(playerRef);
				string party  = player != null ? player.PartyID : string.Empty;

				if (string.IsNullOrEmpty(party) == false)
				{
					foreach (var pair in _playerTeams)
					{
						Player other = Context.NetworkGame.GetPlayer(pair.Key);
						if (other != null && other.PartyID == party)
						{
							_playerTeams[playerRef] = pair.Value;
							return pair.Value;
						}
					}
				}

				team = (byte)Mathf.Min(1 + _teamSlots / size, FIRST_DUMMY_TEAM - 1);
				_teamSlots += size;

				_playerTeams[playerRef] = team;
				return team;
			}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (DebugSoloTeams == true)
			{
				size = 1;
			}
#endif
			team = (byte)Mathf.Min(1 + _teamSlots / size, FIRST_DUMMY_TEAM - 1);
			++_teamSlots;

			_playerTeams[playerRef] = team;
			return team;
		}

		/// <summary>Server: teams given out again in join order (after DebugSoloTeams changes); agents on the ground take their new team.</summary>
		public void ReassignPlayerTeams()
		{
			if (Runner == null || Runner.IsServer == false)
				return;

			_playerTeams.Clear();
			_teamSlots = 0;

			var players = new List<Player>();
			foreach (Player player in Context.NetworkGame.ActivePlayers)
			{
				if (player != null && player.Object != null)
				{
					players.Add(player);
				}
			}
			players.Sort((a, b) => a.Object.InputAuthority.AsIndex.CompareTo(b.Object.InputAuthority.AsIndex));

			foreach (Player player in players)
			{
				PlayerRef playerRef = player.Object.InputAuthority;
				byte      team      = GetPlayerTeam(playerRef);

				if (player.ActiveAgent != null && player.ActiveAgent.Object != null && player.ActiveAgent.Object.IsValid == true)
				{
					player.ActiveAgent.Team = team;
				}

				OnPlayerTeamChanged(playerRef);
			}
		}

		/// <summary>A player's team changed (ReassignPlayerTeams).</summary>
		protected virtual void OnPlayerTeamChanged(PlayerRef playerRef) { }

		/// <summary>A teammate of this agent who is alive and not downed, or null.</summary>
		public Agent GetUpTeammate(Agent agent)
		{
			if (agent == null || GetTeam(agent) <= 0 || Runner == null)
				return null;

			Agent found = null;

			Runner.GetAllBehaviours(_teamBuffer);
			for (int i = 0; i < _teamBuffer.Count; ++i)
			{
				Agent other = _teamBuffer[i];
				if (AreTeammates(agent, other) == true && other.Health.IsUp == true)
				{
					found = other;
					break;
				}
			}

			_teamBuffer.Clear();
			return found;
		}

		/// <summary>At 0 health a player goes down instead of dying while a teammate is up.</summary>
		public bool CanBeDowned(Agent victim)
		{
			if (State != EState.Active || HealthSettings.Current.TeamSize < 2)
				return false;

			return GetUpTeammate(victim) != null;
		}

		/// <summary>Server, from Health: a player went down. If nobody on the team is up any more, the team is out.</summary>
		public void AgentDowned(Agent victim, HitData hitData)
		{
			if (Runner.IsServer == false || State != EState.Active)
				return;

			CheckTeamOut(victim);
		}

		/// <summary>Server: spawns a test dummy (the normal agent prefab, no player and no AI) on the given team.</summary>
		public Agent SpawnDummy(Vector3 position, Quaternion rotation, byte team)
		{
			return SpawnDummy(position, rotation, team, 0);
		}

		/// <summary>Server: a dummy, or a bot when botNumber > 0 (phase 9: the same agent with a brain, set before it spawns).</summary>
		public Agent SpawnDummy(Vector3 position, Quaternion rotation, byte team, byte botNumber)
		{
			if (Runner.IsServer == false)
				return null;

			NetworkPrefabRef prefab = GetDummyPrefab();
			if (prefab.IsValid == false)
				return null;

			NetworkObject dummyObject = Runner.Spawn(prefab, position, rotation, PlayerRef.None, (runner, spawned) =>
			{
				Agent spawnedAgent = spawned.GetComponent<Agent>();
				spawnedAgent.BotNumber = botNumber;
				spawnedAgent.Team      = team;
			});
			Agent         dummy       = dummyObject.GetComponent<Agent>();

			dummy.Team = team;

			_dummies.Add(new Dummy { Agent = dummy, Position = position, Rotation = rotation, Team = team, BotNumber = botNumber });
			return dummy;
		}

		/// <summary>A new team number for a dummy duo.</summary>
		public byte NewDummyTeam()
		{
			byte team = _nextDummyTeam;
			_nextDummyTeam = (byte)(_nextDummyTeam >= 250 ? FIRST_DUMMY_TEAM : _nextDummyTeam + 1);
			return team;
		}

		/// <summary>Server: despawns every test dummy.</summary>
		public int RemoveDummies()
		{
			int count = 0;

			for (int i = 0; i < _dummies.Count; ++i)
			{
				Agent dummy = _dummies[i].Agent;
				if (dummy != null && dummy.Object != null && dummy.Object.IsValid == true)
				{
					Runner.Despawn(dummy.Object);
					++count;
				}
			}

			_dummies.Clear();
			return count;
		}

		public IEnumerable<Agent> GetDummies()
		{
			for (int i = 0; i < _dummies.Count; ++i)
			{
				if (_dummies[i].Agent != null)
					yield return _dummies[i].Agent;
			}
		}

		// PRIVATE METHODS

		private void OnAgentDeathTeams(Agent victim, Player victimPlayer)
		{
			// Watch the teammate who's still up (their first-person view) until you respawn
			Agent teammate = GetUpTeammate(victim);
			if (victimPlayer != null && teammate != null && teammate.Object.InputAuthority.IsRealPlayer == true)
			{
				victimPlayer.SetObservedPlayer(teammate.Object.InputAuthority);
			}

			CheckTeamOut(victim);
		}

		/// <summary>When nobody on the agent's team is up, every downed member dies at once (the kill goes to whoever downed them).</summary>
		private void CheckTeamOut(Agent agent)
		{
			int team = GetTeam(agent);
			if (team <= 0)
				return;

			var downed = new List<Health>(2);

			Runner.GetAllBehaviours(_teamBuffer);
			for (int i = 0; i < _teamBuffer.Count; ++i)
			{
				Agent member = _teamBuffer[i];
				if (GetTeam(member) != team)
					continue;

				if (member.Health.IsUp == true)
				{
					_teamBuffer.Clear();
					return;
				}

				if (member.Health.IsDowned == true)
				{
					downed.Add(member.Health);
				}
			}

			_teamBuffer.Clear();

			HealthDebug.Log(Runner, $"TEAM OUT team {team}: nobody up, {downed.Count} downed die");

			for (int i = 0; i < downed.Count; ++i)
			{
				downed[i].KillDowned("team out");
			}
		}

		private void UpdateDummies()
		{
			for (int i = _dummies.Count - 1; i >= 0; --i)
			{
				Dummy dummy = _dummies[i];

				if (dummy.Agent == null || dummy.Agent.Object == null || dummy.Agent.Object.IsValid == false)
				{
					_dummies.RemoveAt(i);
					continue;
				}

				if (dummy.Agent.Health.IsAlive == true)
					continue;

				// Battle Royale (phase 7): dead dummies stay dead once the match is on. A dead bot's body goes after a few seconds (phase 9b:
				// a body still costs its animation every tick); its loot bag stays
				if (DummiesRespawn == false)
				{
					if (dummy.BotNumber > 0)
					{
						if (dummy.RespawnTimer.IsRunning == false)
						{
							dummy.RespawnTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(1.0f, BotSettings.Current.BodyTime));
							_dummies[i] = dummy;
						}
						else if (dummy.RespawnTimer.Expired(Runner) == true)
						{
							Runner.Despawn(dummy.Agent.Object);
							_dummies.RemoveAt(i);
						}
					}
					continue;
				}

				if (dummy.RespawnTimer.IsRunning == false)
				{
					dummy.RespawnTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(1.0f, RespawnTime));
					_dummies[i] = dummy;
					continue;
				}

				if (dummy.RespawnTimer.Expired(Runner) == false)
					continue;

				// Back where it stood, same team
				Runner.Despawn(dummy.Agent.Object);
				_dummies.RemoveAt(i);
				SpawnDummy(dummy.Position, dummy.Rotation, dummy.Team, dummy.BotNumber);
			}
		}

		private NetworkPrefabRef GetDummyPrefab()
		{
			foreach (Player player in Context.NetworkGame.ActivePlayers)
			{
				if (player != null && player.AgentPrefab.IsValid == true)
					return player.AgentPrefab;
			}

			AgentSettings agentSettings = Global.Settings != null ? Global.Settings.Agent : null;
			return agentSettings != null && agentSettings.Agents.Length > 0 ? agentSettings.Agents[0].AgentPrefab : default;
		}
	}
}
