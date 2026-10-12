using UnityEngine;
using Fusion;
using System.Collections.Generic;
using Fusion.Addons.KCC;

namespace TPSBR
{
	public enum EMatchPhase : byte
	{
		Warmup = 0, // On the ground, respawning, until the host starts
		Flight = 1, // The helicopter is crossing the map; jump any time over it
		Match  = 2, // The helicopter has left the map; the zone clock runs
		Ended  = 3, // One team left: victory, then "Match ends in"
	}

	/// <summary>
	/// Brownout's match flow (phase 7, Match Flow & Zone page, Phase 7 spec). Players wait on the ground in a warm-up (respawning) until the
	/// host starts the match; then everyone is reset into one helicopter flying a random straight line across the map, ground loot is rolled
	/// again and the test dummy duos are placed. Jump over the map; the wingsuit and chute are movement modes (AgentMovement). The zone clock
	/// starts when the helicopter leaves the map. The last team with anyone alive wins; teams are placed in the order they go out.
	/// Numbers in MatchSettings.
	/// </summary>
	public sealed class BattleRoyaleGameplayMode : GameplayMode
	{
		// PUBLIC MEMBERS

		[Networked, HideInInspector]
		public EMatchPhase Phase          { get; private set; }
		/// <summary>The winning team once the match has ended (0 = nobody).</summary>
		[Networked, HideInInspector]
		public byte        WinningTeam    { get; private set; }
		/// <summary>Teams still in the match (anyone alive or aboard), for the HUD.</summary>
		[Networked, HideInInspector]
		public byte        TeamsAlive     { get; private set; }
		/// <summary>Players and dummies still alive or aboard, for the HUD.</summary>
		[Networked, HideInInspector]
		public byte        PlayersAlive   { get; private set; }

		// Kept for the sample's UIBattleRoyale widget (switched off by the Brownout HUD)
		public bool     HasStarted         => Phase != EMatchPhase.Warmup;
		public bool     AirplaneActive     => Phase == EMatchPhase.Flight;
		public float    WaitingCooldown    => 0.0f;
		public float    DropCooldown       => 0.0f;
		public Airplane Airplane           => _airplane;
		/// <summary>Phase 11f (server): the Osprey's path the bots' jumps were planned on.</summary>
		public DropPath BotDropPath        => _dropPath;

		/// <summary>Seconds left on "Match ends in" (0 when not ending).</summary>
		public float EndTimeLeft => Phase == EMatchPhase.Ended && _endTick > 0 ? Mathf.Max(0.0f, (_endTick - Runner.Tick) * Runner.DeltaTime) : 0.0f;

		/// <summary>Players in the helicopter right now (all peers).</summary>
		public int Aboard => _aboardCount;

		/// <summary>Phase 11b: seconds left on the warm-up countdown, or -1 when it isn't running.</summary>
		public float CountdownLeft => Phase == EMatchPhase.Warmup && _countdownEndTick > 0 ? Mathf.Max(0.0f, (_countdownEndTick - Runner.Tick) * Runner.DeltaTime) : -1.0f;

		/// <summary>
		/// Phase 11b: set on the host by the main menu's PLAY (a matchmade match): the warm-up counts down MatchSettings.WarmupCountdown
		/// once a real player has loaded in, then starts. Matches started by pressing Play in a scene keep waiting for Enter.
		/// </summary>
		public static bool UseWarmupCountdown;

#if UNITY_EDITOR
		/// <summary>Editor test switch (Brownout → Test → Match → Start Match On Play, set by StandaloneManager): the host starts the match
		/// by itself once this many players have spawned in the warm-up (0 = off, wait for Enter).</summary>
		public static int DebugAutoStartPlayers;
		/// <summary>True while the warm-up is waiting to start by itself, for the HUD.</summary>
		public static bool IsAutoStartPending => DebugAutoStartPlayers > 0;
#endif

		// PRIVATE MEMBERS

		// The sample's other numbers on this prefab (height, drop time, waiting time) are replaced by MatchSettings
		[SerializeField]
		private Airplane _airplanePrefab;
		[SerializeField]
		private AirplaneAgent _airplaneAgentPrefab;

		[Networked]
		private Airplane _airplane { get; set; }
		[Networked]
		private int _endTick { get; set; }
		[Networked]
		private byte _aboardCount { get; set; }
		[Networked]
		private int _countdownEndTick { get; set; }
		/// <summary>Place by team number (0 = still in, or not in the match).</summary>
		[Networked, Capacity(256)]
		private NetworkArray<byte> _teamPlace { get; }
		/// <summary>Team by player index, so every peer knows a player's team after their agent is gone.</summary>
		[Networked, Capacity(256)]
		private NetworkArray<byte> _playerTeam { get; }
		/// <summary>Phase 9b: the bot numbers on each team (two per team, 0 = none), so every peer can name a bot team (winners).</summary>
		[Networked, Capacity(512)]
		private NetworkArray<byte> _teamBots { get; }

		private readonly Dictionary<PlayerRef, AirplaneAgent> _airplaneAgents = new Dictionary<PlayerRef, AirplaneAgent>(byte.MaxValue);
		private readonly HashSet<int>                         _teamsInMatch   = new HashSet<int>();
		private readonly Dictionary<int, float>               _lastTeamHealth = new Dictionary<int, float>(32);
		private readonly Dictionary<int, float>               _teamHealth     = new Dictionary<int, float>(32);
		private readonly List<Agent>                          _agentBuffer    = new List<Agent>(64);
		private readonly List<PlayerRef>                      _jumpBuffer     = new List<PlayerRef>(16);

		private bool  _wasOverMap;
		private DropPath _dropPath;   // Phase 11f: the Osprey's path, for the bots' jumps (server)
		private int   _nextForcedJumpTick;
		private int   _nextSpectateCheckTick;

		private BattleRoyaleComparer _playerComparer = new BattleRoyaleComparer();

		// PUBLIC METHODS

		/// <summary>The host starts the match (Enter on the host, or a test tool). Only during the warm-up.</summary>
		public void StartMatch()
		{
			if (HasStateAuthority == false || Phase != EMatchPhase.Warmup || State != EState.Active)
				return;

			BeginMatch();
		}

		/// <summary>Kept for the sample's UIBattleRoyale widget.</summary>
		public void StartImmediately() => StartMatch();
		public void TryAddWaitTime(float time) { }

		/// <summary>A player's team on any peer (0 = unknown).</summary>
		public int GetTeamOfPlayer(PlayerRef playerRef)
		{
			int index = playerRef.AsIndex;
			return playerRef.IsRealPlayer == true && index >= 0 && index < _playerTeam.Length ? _playerTeam[index] : 0;
		}

		/// <summary>A team's place once it's out (1 = won), 0 while still in.</summary>
		public int GetTeamPlace(int team)
		{
			return team > 0 && team < _teamPlace.Length ? _teamPlace[team] : 0;
		}

		/// <summary>A team's bot numbers (0 = none), on any peer.</summary>
		public byte GetTeamBot(int team, int index)
		{
			int slot = team * 2 + index;
			return team > 0 && index >= 0 && index < 2 && slot < _teamBots.Length ? _teamBots[slot] : (byte)0;
		}

		/// <summary>Server: jump out of the helicopter (Space over the map), or pushed out (forced). False when not allowed.</summary>
		public bool RequestAirplaneJump(PlayerRef playerRef, Vector3 direction)
		{
			return Jump(playerRef, direction, false);
		}

		// GameplayMode INTERFACE

		protected override bool DummiesRespawn => Phase == EMatchPhase.Warmup;

		protected override void OnActivate()
		{
			// The zone waits for the helicopter to leave the map
			if (_shrinkingArea != null)
			{
				_shrinkingArea.Deactivate();
			}

			// The sample's zone announced its own first circle on activation and dropped the spawn points outside it; on our 1 km map
			// that was all of them (phase 8). The warm-up spawns anywhere
			ResetSpawnPoints();

			Phase       = EMatchPhase.Warmup;
			WinningTeam = 0;

			MatchDebug.Log(Runner, "WARM-UP waiting for the host to start");
		}

		public override void FixedUpdateNetwork()
		{
			base.FixedUpdateNetwork();

			if (HasStateAuthority == false)
				return;

			if (State != EState.Active)
				return;

			switch (Phase)
			{
				case EMatchPhase.Flight:
				case EMatchPhase.Match:
					UpdateHelicopter();
					UpdateTeams();
					UpdateSpectators();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					MatchPerformanceLog.Tick(Runner, Phase);
#endif
					break;
				case EMatchPhase.Ended:
					UpdateSpectators();
					if (_endTick > 0 && Runner.Tick >= _endTick)
					{
						_endTick = 0;
						FinishGameplay();
					}
					break;
				default:
					UpdatePlayersAlive();
					UpdateCountdown();
#if UNITY_EDITOR
					UpdateAutoStart();
#endif
					break;
			}
		}

		protected override float GetRespawnTime(PlayerStatistics playerStatistics)
		{
			// Warm-up: back in a few seconds. The match: no respawns
			return Phase == EMatchPhase.Warmup ? MatchSettings.Current.WarmupRespawnTime : -1.0f;
		}

		protected override void SortPlayers(List<PlayerStatistics> allStatistics)
		{
			allStatistics.Sort(_playerComparer);
		}

		protected override void CheckWinCondition()
		{
			// Checked every tick by team (UpdateTeams)
		}

		protected override void TrySpawnAgent(Player player)
		{
			PlayerRef playerRef = player.Object.InputAuthority;
			RecordTeam(playerRef);

			switch (Phase)
			{
				case EMatchPhase.Warmup:
				{
					base.TrySpawnAgent(player);
					break;
				}
				case EMatchPhase.Flight:
				{
					// Joined while the helicopter is still over the map: aboard
					if (_airplane != null && _airplane.IsFinished == false)
					{
						Board(player);
						break;
					}

					Eliminate(player);
					break;
				}
				default:
				{
					// Too late: out, watching
					Eliminate(player);
					break;
				}
			}
		}

		// PRIVATE METHODS

#if UNITY_EDITOR
		private int _autoStartTick;
		private int _warmupStartTick;

		// Start Match On Play: wait for every peer's player to spawn (so teams are made with both humans), then a short grace;
		// after 30 s start anyway (a late client still boards while the helicopter is over the map)
		private void UpdateAutoStart()
		{
			if (DebugAutoStartPlayers <= 0)
				return;

			if (_warmupStartTick == 0)
			{
				_warmupStartTick = Runner.Tick;
			}

			int spawned = 0;
			foreach (Player player in Context.NetworkGame.ActivePlayers)
			{
				if (player != null && player.ActiveAgent != null)
				{
					++spawned;
				}
			}

			bool timedOut = (Runner.Tick - _warmupStartTick) * Runner.DeltaTime > 30.0f;
			if (spawned < DebugAutoStartPlayers && timedOut == false)
			{
				_autoStartTick = 0;
				return;
			}

			if (_autoStartTick == 0)
			{
				_autoStartTick = Runner.Tick + Mathf.CeilToInt(1.0f / Runner.DeltaTime);
			}

			if (Runner.Tick >= _autoStartTick)
			{
				MatchDebug.Log(Runner, $"AUTO START (Start Match On Play): {spawned}/{DebugAutoStartPlayers} players in{(timedOut == true ? ", timed out" : "")}");
				StartMatch();
			}
		}
#endif

		/// <summary>Phase 11b: the warm-up countdown of a matchmade match; starts once any real player has an agent, then starts the match.</summary>
		private void UpdateCountdown()
		{
			if (UseWarmupCountdown == false)
				return;

			if (_countdownEndTick == 0)
			{
				foreach (Player player in Context.NetworkGame.ActivePlayers)
				{
					if (player != null && player.ActiveAgent != null)
					{
						_countdownEndTick = Runner.Tick + Mathf.CeilToInt(Mathf.Max(1.0f, MatchSettings.Current.WarmupCountdown) / Runner.DeltaTime);
						MatchDebug.Log(Runner, $"COUNTDOWN {MatchSettings.Current.WarmupCountdown:0} s (a player has loaded in)");
						break;
					}
				}
				return;
			}

			if (Runner.Tick >= _countdownEndTick)
			{
				StartMatch();
			}
		}

		private void BeginMatch()
		{
			MatchSettings settings = MatchSettings.Current;

			// Phase 11b: no one joins a match that has left the warm-up (matchmaking looks for open Brownout sessions)
			_countdownEndTick = 0;
			if (Runner.SessionInfo != null && Runner.SessionInfo.IsValid == true)
			{
				Runner.SessionInfo.IsOpen = false;
			}

			MatchDebug.Log(Runner, $"START by the host: {Context.NetworkGame.ActivePlayers.Count} players, {settings.BotDuoCount} bot duos, {settings.DummyDuoCount} dummy duos, zone clock x{MatchSettings.TimeScale:F1}");

			// 1. Everyone off the ground and reset
			ClearRespawns();
			RemoveDummies();

			foreach (Player player in Context.NetworkGame.ActivePlayers)
			{
				if (player == null)
					continue;

				player.DespawnAgent();

				var statistics = player.Statistics;
				statistics.IsAlive      = false;
				statistics.IsEliminated = false;
				statistics.Kills        = 0;
				statistics.Deaths       = 0;
				statistics.Score        = 0;
				statistics.Position     = 0;
				statistics.RespawnTimer = default;
				player.UpdateStatistics(statistics);
				player.SetObservedPlayer(player.Object.InputAuthority);
			}

			for (int i = 0; i < _teamPlace.Length; ++i)
			{
				_teamPlace.Set(i, 0);
			}

			for (int i = 0; i < _teamBots.Length; ++i)
			{
				_teamBots.Set(i, 0);
			}

			// 2. The ground loot rolls again; warm-up drops and bags go
			Runner.GetAllBehaviours(_bagBuffer);
			for (int i = 0; i < _bagBuffer.Count; ++i)
			{
				Runner.Despawn(_bagBuffer[i].Object);
			}
			_bagBuffer.Clear();
			LootSpawner.Reroll(Runner);

			// Every door shut again (phase 8b; power doors follow their POI's power)
			MapState.Get(Runner.SimulationUnityScene)?.CloseAllDoors();

			// 3. The helicopter on a random straight line
			SpawnHelicopter(settings);

			// 4. Everyone aboard
			_teamsInMatch.Clear();
			foreach (Player player in Context.NetworkGame.ActivePlayers)
			{
				if (player == null)
					continue;

				RecordTeam(player.Object.InputAuthority);
				Board(player);
				_teamsInMatch.Add(GetPlayerTeam(player.Object.InputAuthority));
			}

			// 5. Bot duos on the ground at POIs and cabin areas (phase 9b), a bot teammate for every lone player, test dummies
			PlaceBots(settings);
			PlaceDummies(settings);

			_lastTeamHealth.Clear();
			_wasOverMap = false;
			WinningTeam = 0;
			Phase       = EMatchPhase.Flight;

			UpdatePlayersAlive();
		}

		private void SpawnHelicopter(MatchSettings settings)
		{
			if (MapBounds.TryGet(Context, out Vector3 center, out Vector2 halfSize) == false)
			{
				center   = _shrinkingArea != null ? _shrinkingArea.transform.position : Vector3.zero;
				halfSize = new Vector2(500.0f, 500.0f);
			}

			// A random direction through a random point near the center; from outside one edge to outside the other
			float   angle     = Random.Range(0.0f, Mathf.PI * 2.0f);
			Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
			Vector2 through   = new Vector2(center.x, center.z) + Random.insideUnitCircle * (Mathf.Min(halfSize.x, halfSize.y) * 2.0f * settings.PathCenterShare);

			// Where the line crosses the square (slab test)
			float tMin = float.MinValue;
			float tMax = float.MaxValue;
			ClipSlab(through.x, direction.x, center.x - halfSize.x, center.x + halfSize.x, ref tMin, ref tMax);
			ClipSlab(through.y, direction.y, center.z - halfSize.y, center.z + halfSize.y, ref tMin, ref tMax);

			// RunInTime before the map edge (Logan, 9 Oct: 7 s to load in and look around before the drop opens)
			Vector2 start = through + direction * (tMin - settings.HelicopterSpeed * Mathf.Max(0.0f, settings.RunInTime));
			Vector2 end   = through + direction * (tMax + settings.PathOutside);

			Vector3 start3 = new Vector3(start.x, settings.HelicopterHeight, start.y);
			Vector3 end3   = new Vector3(end.x,   settings.HelicopterHeight, end.y);

			float overMapFrom = settings.HelicopterSpeed * Mathf.Max(0.0f, settings.RunInTime);
			_dropPath = new DropPath
			{
				Start       = start3,
				Direction   = new Vector3(direction.x, 0.0f, direction.y),
				OverMapFrom = overMapFrom,
				OverMapTo   = overMapFrom + Mathf.Max(0.0f, tMax - tMin),
			};

			_airplane = Runner.Spawn(_airplanePrefab, start3, Quaternion.LookRotation((end3 - start3).OnlyXZ()));
			_airplane.SetStraightPath(start3, end3, settings.HelicopterSpeed);
			_airplane.DropEnabled = false;

			MatchDebug.Log(Runner, $"HELI path {start3.ToString("F0")} -> {end3.ToString("F0")}, {Vector3.Distance(start3, end3):F0} m at {settings.HelicopterSpeed:F0} m/s, {tMax - tMin:F0} m over the map");
		}

		private static void ClipSlab(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
		{
			if (Mathf.Abs(direction) < 0.0001f)
				return;

			float t0 = (min - origin) / direction;
			float t1 = (max - origin) / direction;
			if (t0 > t1)
			{
				float swap = t0;
				t0 = t1;
				t1 = swap;
			}

			tMin = Mathf.Max(tMin, t0);
			tMax = Mathf.Min(tMax, t1);
		}

		private void Board(Player player)
		{
			PlayerRef playerRef = player.Object.InputAuthority;
			if (_airplaneAgents.ContainsKey(playerRef) == true || _airplane == null)
				return;

			var agent = Runner.Spawn(_airplaneAgentPrefab, inputAuthority: playerRef);
			Runner.SetPlayerAlwaysInterested(playerRef, agent.Object, true);
			Runner.SetPlayerAlwaysInterested(playerRef, _airplane.Object, true);

			agent.Airplane = _airplane;
			_airplaneAgents.Add(playerRef, agent);
			_aboardCount = (byte)_airplaneAgents.Count;

			var statistics = player.Statistics;
			statistics.IsAlive      = true;
			statistics.IsEliminated = false;
			player.UpdateStatistics(statistics);
		}

		private void Eliminate(Player player)
		{
			var statistics = player.Statistics;
			statistics.IsEliminated = true;
			statistics.IsAlive      = false;
			player.UpdateStatistics(statistics);

			SetSpectatorTargetToBestPlayer(player);
		}

		private bool Jump(PlayerRef playerRef, Vector3 direction, bool forced)
		{
			if (HasStateAuthority == false || _airplane == null)
				return false;
			if (Phase != EMatchPhase.Flight && Phase != EMatchPhase.Match)
				return false;
			if (forced == false && _airplane.DropEnabled == false)
				return false;
			if (_airplaneAgents.TryGetValue(playerRef, out var airplaneAgent) == false)
				return false;

			Runner.Despawn(airplaneAgent.Object);
			_airplaneAgents.Remove(playerRef);
			_aboardCount = (byte)_airplaneAgents.Count;

			MatchSettings settings = MatchSettings.Current;
			Vector3       forward  = direction.OnlyXZ().sqrMagnitude > 0.001f ? direction.OnlyXZ().normalized : _airplane.transform.forward.OnlyXZ().normalized;

			var agent = SpawnAgent(playerRef, _airplane.AgentPosition.position, Quaternion.LookRotation(forward));
			agent.Movement.StartDrop(_airplane.Velocity + Vector3.down * settings.JumpOutSpeed);

			RPC_PlayerJumped(playerRef);
			OnHumanJumped(playerRef);   // Phase 11f: their bot teammate follows

			MatchDebug.Log(Runner, $"JUMP {agent.name}{(forced ? " (pushed out)" : string.Empty)} at {agent.transform.position.ToString("F0")}, {_airplane.PathProgress * 100.0f:F0}% along the path");
			return true;
		}

		private void UpdateHelicopter()
		{
			if (_airplane == null)
				return;

			bool overMap = _airplane.IsFinished == false && MapBounds.IsInside(Context, _airplane.transform.position) == true;
			_airplane.DropEnabled = overMap;

			if (overMap == true)
			{
				_wasOverMap = true;
			}

			bool pastMap = (_wasOverMap == true && overMap == false) || _airplane.IsFinished == true;

			// Left the map: the zone clock starts
			if (pastMap == true && Phase == EMatchPhase.Flight)
			{
				Phase = EMatchPhase.Match;

				if (_shrinkingArea != null && MapBounds.TryGet(Context, out Vector3 center, out Vector2 halfSize) == true)
				{
					_shrinkingArea.StartSchedule(center, halfSize);
				}

				MatchDebug.Log(Runner, $"HELI left the map, {_airplaneAgents.Count} still aboard; the zone clock starts");
			}

			// Past the far edge: anyone still aboard is pushed out, one at a time (all at once when the helicopter is gone)
			if (pastMap == true && _airplaneAgents.Count > 0 && (Runner.Tick >= _nextForcedJumpTick || _airplane.IsFinished == true))
			{
				_jumpBuffer.Clear();
				_jumpBuffer.AddRange(_airplaneAgents.Keys);

				int count = _airplane.IsFinished == true ? _jumpBuffer.Count : 1;
				for (int i = 0; i < count; ++i)
				{
					Jump(_jumpBuffer[i], _airplane.transform.forward, true);
				}

				_nextForcedJumpTick = Runner.Tick + Mathf.Max(1, Mathf.CeilToInt(MatchSettings.Current.ForcedJumpInterval / Runner.DeltaTime));
			}

			// Phase 11f: bots jump at their points over the map; past the far edge (or with the Osprey gone) whoever's left goes
			UpdateBotDrops(_airplane, _dropPath, overMap, pastMap);

			if (_airplane.IsFinished == true && _airplaneAgents.Count == 0 && BotsAboard == 0)
			{
				Runner.Despawn(_airplane.Object);
				_airplane = null;
				MatchDebug.Log(Runner, "HELI gone");
			}
		}

		/// <summary>Which teams still have anyone alive (up or downed) or aboard; places the ones that just went out; one left wins.</summary>
		private void UpdateTeams()
		{
			_teamHealth.Clear();

			Runner.GetAllBehaviours(_agentBuffer);
			for (int i = 0; i < _agentBuffer.Count; ++i)
			{
				Agent agent = _agentBuffer[i];
				if (agent == null || agent.Object == null || agent.Object.IsValid == false || agent.Health.IsAlive == false)
					continue;

				int team = GetTeam(agent);
				if (team <= 0)
					continue;

				_teamHealth.TryGetValue(team, out float health);
				_teamHealth[team] = health + agent.Health.CurrentHealth + 1.0f;
				_teamsInMatch.Add(team);
			}
			_agentBuffer.Clear();

			foreach (PlayerRef playerRef in _airplaneAgents.Keys)
			{
				int team = GetPlayerTeam(playerRef);
				_teamHealth.TryGetValue(team, out float health);
				_teamHealth[team] = health + HealthSettings.Current.MaxHealth;
				_teamsInMatch.Add(team);
			}

			// Phase 11f: bot teams still aboard the Osprey are in
			foreach (BotDrop drop in BotDrops)
			{
				if (drop.Aboard <= 0)
					continue;

				_teamHealth.TryGetValue(drop.Team, out float health);
				_teamHealth[drop.Team] = health + HealthSettings.Current.MaxHealth * drop.Aboard;
				_teamsInMatch.Add(drop.Team);
			}

			int alive = _teamHealth.Count;
			TeamsAlive = (byte)Mathf.Min(255, alive);
			UpdatePlayersAlive();

			// Teams that just went out are placed: one more than the teams still in
			foreach (int team in _teamsInMatch)
			{
				if (_teamHealth.ContainsKey(team) == true || GetTeamPlace(team) > 0 || team >= _teamPlace.Length)
					continue;

				if (alive == 0)
					continue; // The last ones out together: decided below

				_teamPlace.Set(team, (byte)Mathf.Min(255, alive + 1));
				MatchDebug.Log(Runner, $"PLACED team {team} #{alive + 1} ({alive} teams left)");
			}

			if (_teamsInMatch.Count >= 2 && alive == 1)
			{
				foreach (int team in _teamHealth.Keys)
				{
					EndMatch(team, "last team standing");
				}
			}
			else if (alive == 0)
			{
				// Everyone left went out on the same tick: the one with the most health a tick before wins, unless that's a tie too
				int   best       = 0;
				float bestHealth = -1.0f;
				bool  tie        = false;

				foreach (var pair in _lastTeamHealth)
				{
					if (GetTeamPlace(pair.Key) > 0)
						continue;

					if (pair.Value > bestHealth + 0.01f)
					{
						best       = pair.Key;
						bestHealth = pair.Value;
						tie        = false;
					}
					else if (Mathf.Abs(pair.Value - bestHealth) <= 0.01f)
					{
						tie = true;
					}
				}

				foreach (var pair in _lastTeamHealth)
				{
					if (GetTeamPlace(pair.Key) == 0 && pair.Key != best && pair.Key < _teamPlace.Length)
					{
						_teamPlace.Set(pair.Key, 2);
					}
				}

				EndMatch(tie == true ? 0 : best, tie == true ? "the last teams went out together, a tie" : "the last teams went out together; most health a tick before");
			}

			_lastTeamHealth.Clear();
			foreach (var pair in _teamHealth)
			{
				_lastTeamHealth[pair.Key] = pair.Value;
			}
		}

		private void EndMatch(int winner, string reason)
		{
			if (Phase == EMatchPhase.Ended)
				return;

			if (winner > 0 && winner < _teamPlace.Length)
			{
				_teamPlace.Set(winner, 1);
			}

			WinningTeam = (byte)Mathf.Clamp(winner, 0, 255);
			Phase       = EMatchPhase.Ended;
			_endTick    = Runner.Tick + Mathf.CeilToInt(MatchSettings.Current.EndCountdown / Runner.DeltaTime);

			if (_shrinkingArea != null)
			{
				_shrinkingArea.StopSchedule();
			}

			MatchDebug.Log(Runner, winner > 0 ? $"VICTORY team {winner} ({reason})" : $"VICTORY nobody ({reason})");
		}

		private void UpdatePlayersAlive()
		{
			int count = _airplaneAgents.Count + (Phase == EMatchPhase.Warmup ? 0 : BotsAboard);

			Runner.GetAllBehaviours(_agentBuffer);
			for (int i = 0; i < _agentBuffer.Count; ++i)
			{
				Agent agent = _agentBuffer[i];
				if (agent != null && agent.Object != null && agent.Object.IsValid == true && agent.Health.IsAlive == true)
				{
					++count;
				}
			}
			_agentBuffer.Clear();

			PlayersAlive = (byte)Mathf.Min(255, count);
		}

		/// <summary>Players who are out watch someone still in (their teammate first, set on death).</summary>
		private void UpdateSpectators()
		{
			if (Runner.Tick < _nextSpectateCheckTick)
				return;

			_nextSpectateCheckTick = Runner.Tick + Mathf.CeilToInt(1.0f / Runner.DeltaTime);

			foreach (Player player in Context.NetworkGame.ActivePlayers)
			{
				if (player == null || player.Statistics.IsEliminated == false)
					continue;

				Player watched = Context.NetworkGame.GetPlayer(player.Watching);
				bool   valid   = watched != null && watched != player && watched.ActiveAgent != null && watched.ActiveAgent.Health.IsAlive == true;

				if (valid == false)
				{
					SetSpectatorTargetToBestPlayer(player);
				}
			}
		}

		private void RecordTeam(PlayerRef playerRef)
		{
			int index = playerRef.AsIndex;
			if (index >= 0 && index < _playerTeam.Length)
			{
				_playerTeam.Set(index, GetPlayerTeam(playerRef));
			}
		}

		protected override void OnPlayerTeamChanged(PlayerRef playerRef)
		{
			RecordTeam(playerRef);
		}

		/// <summary>
		/// Phase 9b: MatchSettings.BotDuos bot duos on the ground (they don't drop), and a bot teammate for each team with only one human.
		/// Bots start empty-handed and loot, like players.
		/// </summary>
		private void PlaceBots(MatchSettings settings)
		{
			// Teams with only one human get a bot teammate, in an area kept free of bot duos
			var humans = new Dictionary<int, int>(8);
			foreach (Player player in Context.NetworkGame.ActivePlayers)
			{
				if (player == null)
					continue;

				int team = GetPlayerTeam(player.Object.InputAuthority);
				humans.TryGetValue(team, out int count);
				humans[team] = count + 1;
			}

			bool giveTeammates = settings.LoneBotTeammate == true && HealthSettings.Current.TeamSize >= 2;
			int  lone          = 0;
			foreach (var pair in humans)
			{
				lone += pair.Value == 1 ? 1 : 0;
			}

			// Phase 11f: on our map the bots ride the Osprey and drop to their spots; elsewhere they stand on the ground as in phase 9b
			BrownoutMap map  = BrownoutMap.Get(Runner.SimulationUnityScene);
			bool        drop = _airplane != null && map != null && map.Areas.Count > 0;

			List<byte> teams = drop == true ? PlaceBotDuos(settings.BotDuoCount, 0, _dropPath) : PlaceBotDuos(settings.BotDuoCount, giveTeammates == true ? lone : 0);
			foreach (byte team in teams)
			{
				_teamsInMatch.Add(team);
			}

			int teammates = 0;
			if (giveTeammates == true)
			{
				foreach (var pair in humans)
				{
					if (pair.Value != 1)
						continue;

					if (drop == true)
					{
						foreach (Player player in Context.NetworkGame.ActivePlayers)
						{
							if (player != null && GetPlayerTeam(player.Object.InputAuthority) == pair.Key)
							{
								PlanBotTeammateDrop((byte)pair.Key, player.Object.InputAuthority, _dropPath);
								++teammates;
								MatchDebug.Log(Runner, $"TEAMMATE a bot rides with lone team {pair.Key} and jumps after its player");
								break;
							}
						}
						continue;
					}

					Agent bot = SpawnBotTeammate((byte)pair.Key);
					if (bot != null)
					{
						++teammates;
						MatchDebug.Log(Runner, $"TEAMMATE bot {bot.BotName} joins lone team {pair.Key} at {bot.transform.position.ToString("F0")}");
					}
				}
			}

			// Each team's bots by number, for the winner banner on every peer (bots on the ground, and those still to jump)
			foreach (Agent bot in GetBots())
			{
				SetTeamBot(bot.Team, bot.BotNumber);
			}

			foreach (BotDrop botDrop in BotDrops)
			{
				foreach (byte number in botDrop.Numbers)
				{
					SetTeamBot(botDrop.Team, number);
				}

				if (botDrop.Teammate == false)
				{
					MatchDebug.Log(Runner, $"BOTS team {botDrop.Team} jump {botDrop.JumpAlong - _dropPath.OverMapFrom:F0} m into the flight for {botDrop.Area} {botDrop.Target.ToString("F0")}, {_dropPath.DistanceTo(botDrop.Target):F0} m from the path");
				}
			}

			if (drop == false)
			{
				foreach (BotPlacement placement in LastBotPlacements)
				{
					if (placement.Teammate == false)
					{
						MatchDebug.Log(Runner, $"BOTS team {placement.Team} at {placement.Area} {placement.Position.ToString("F0")}");
					}
				}
			}

			MatchDebug.Log(Runner, $"BOTS {BotCount + BotsAboard} bots{(drop ? " (aboard the Osprey)" : string.Empty)}: {teams.Count} duos and {teammates} lone players' teammates");
		}

		private void SetTeamBot(int team, byte number)
		{
			if (team <= 0 || team * 2 + 1 >= _teamBots.Length || number == 0)
				return;

			int slot = _teamBots[team * 2] == 0 ? team * 2 : team * 2 + 1;
			_teamBots.Set(slot, number);
		}

		/// <summary>The test dummy duos (MatchSettings.DummyDuos) at loot spots spread across the map, two per spot, standing still.</summary>
		private void PlaceDummies(MatchSettings settings)
		{
			int duos = settings.DummyDuoCount;
			if (duos == 0)
				return;

			var candidates = new List<Vector3>();
			IReadOnlyList<LootSpawner.Point> points = LootSpawner.GetPoints(Runner);
			for (int i = 0; i < points.Count; ++i)
			{
				if (MapBounds.IsInside(Context, points[i].Position) == true)
				{
					candidates.Add(points[i].Position);
				}
			}

			if (candidates.Count == 0)
			{
				foreach (SpawnPoint spawnPoint in SpawnPoints)
				{
					candidates.Add(spawnPoint.transform.position);
				}
			}

			// Shuffle, then keep the ones far enough from those already used
			for (int i = candidates.Count - 1; i > 0; --i)
			{
				int j = Random.Range(0, i + 1);
				Vector3 swap = candidates[i];
				candidates[i] = candidates[j];
				candidates[j] = swap;
			}

			var chosen = new List<Vector3>(duos);
			for (float spacing = settings.DummySpacing; chosen.Count < duos && spacing >= 1.0f; spacing *= 0.5f)
			{
				for (int i = 0; i < candidates.Count && chosen.Count < duos; ++i)
				{
					bool farEnough = true;
					for (int k = 0; k < chosen.Count; ++k)
					{
						if (Vector3.Distance(chosen[k], candidates[i]) < spacing)
						{
							farEnough = false;
							break;
						}
					}

					if (farEnough == true)
					{
						chosen.Add(candidates[i]);
					}
				}
			}

			for (int i = 0; i < chosen.Count; ++i)
			{
				byte       team     = NewDummyTeam();
				Quaternion rotation = Quaternion.Euler(0.0f, Random.Range(0.0f, 360.0f), 0.0f);
				Vector3    side     = rotation * Vector3.right * 1.5f;

				SpawnDummy(chosen[i] - side, rotation, team);
				SpawnDummy(chosen[i] + side, rotation, team);
				_teamsInMatch.Add(team);

				MatchDebug.Log(Runner, $"DUMMIES team {team} at {chosen[i].ToString("F0")}");
			}
		}

		private readonly List<LootBag> _bagBuffer = new List<LootBag>(16);

		// RPCs

		[Rpc(RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Reliable)]
		private void RPC_PlayerJumped(PlayerRef playerRef)
		{
			if (_airplane != null)
			{
				_airplane.OnPlayerJumped(playerRef);
			}
		}

		// HELPERS

		private class BattleRoyaleComparer : IComparer<PlayerStatistics>
		{
			public int Compare(PlayerStatistics x, PlayerStatistics y)
			{
				var result = x.IsEliminated.CompareTo(y.IsEliminated);
				if (result != 0)
					return result;

				return x.Score.CompareTo(y.Score);
			}
		}
	}
}
