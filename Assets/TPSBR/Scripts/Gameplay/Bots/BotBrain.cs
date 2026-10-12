using System.Collections.Generic;
using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;

namespace TPSBR
{
	public enum EBotState : byte
	{
		Idle   = 0,
		Loot   = 1,
		Roam   = 2,
		Follow = 3,
		Fight  = 4,
		Cover  = 5,
		Heal   = 6,
		Downed = 7,
	}

	/// <summary>
	/// A bot's brain (phase 9), on the server only. It decides a few times a second (BotSettings.ThinkRate, spread across bots) and
	/// steers, aims and presses keys every tick, by writing the agent's GameplayInput (AgentInput.BeforeTick), so a bot moves, fires,
	/// reloads, heals and punches through the same rules as a player. One state at a time: Loot (empty-handed at first, like a player),
	/// Roam (next area, the zone), Follow (a duo's second bot), Fight, Cover (when hit), Heal, Downed. Senses, combat and looting are in
	/// the partial files next to this one; every number is in BotSettings.
	/// </summary>
	public sealed partial class BotBrain
	{
		// PUBLIC MEMBERS

		public static IReadOnlyList<BotBrain> All => _all;

		/// <summary>Time spent in every bot's Tick, in Stopwatch ticks (the Bot Check measures the brains' share of the host's tick).</summary>
		public static long DebugTickTime;
		public static int  DebugTickCount;

		public Agent         Agent      => _agent;
		public EBotState     State      => _state;
		public float         SkillValue => _skillValue;
		public BotSettings.Skill Skill  => _skill;
		public Agent         Target     => _target;
		public bool          TargetVisible => _targetVisible;
		public BotNavigation Navigation => _nav;
		/// <summary>What it's doing, for the debug view and Log Bot Status.</summary>
		public string        Goal       => _goal;
		public BotBrain      Partner    => FindPartner();
		public bool          IsLeader   => IsDuoLeader();
		/// <summary>Degrees between where it looks and where it means to shoot (the last tick it aimed).</summary>
		public float         AimOffset  => _lastAimOffset;
		/// <summary>Its pose is updated every few ticks (far from every human and not fighting).</summary>
		public bool          IsThinned  => _thinned;
		/// <summary>The human teammate it follows (a lone player's bot), or null.</summary>
		public Agent         HumanTeammate => _human;

		// PRIVATE MEMBERS

		private static readonly List<BotBrain> _all = new List<BotBrain>(32);

		private readonly Agent         _agent;
		private readonly BotNavigation _nav = new BotNavigation();

		private EBotState         _state;
		private float             _stateTime;
		private float             _skillValue;
		private BotSettings.Skill _skill;
		private string            _goal = string.Empty;
		private float             _nextThink;
		private float             _noiseSeed;

		// This tick's output
		private Vector3 _move;
		private bool    _hasLook;
		private Vector3 _lookPoint;
		private bool    _preciseLook;
		private bool    _sprint;
		private bool    _crouch;
		private bool    _aim;
		private bool    _attack;
		private bool    _reload;
		private bool    _heal;
		private bool    _jump;
		private bool    _melee;
		private byte    _weapon;

		// Sprint can't be pressed again right after letting go (a double tap is a dash)
		private bool    _sprintHeld;
		private float   _sprintReleasedTime = -10.0f;

		// Stuck recovery
		private float   _unstickUntil;
		private Vector3 _unstickMove;

		// Aim tracking
		private Vector2 _lastAim;
		private Vector2 _aimRate;
		private bool    _hasLastAim;

		// Test hook: walk to this point and nothing else (except fighting, unless players are ignored)
		private bool    _hasForcedGoal;
		private Vector3 _forcedGoal;

		// Roam goal
		private int     _roamArea = -1;
		private Vector3 _roamPoint;
		private bool    _hasRoamPoint;
		private float   _roamPickTime;
		private float   _stayUntil = -1.0f;
		private Vector3 _strollPoint;
		private float   _strollTime;
		private readonly UnityEngine.AI.NavMeshPath _strollPath = new UnityEngine.AI.NavMeshPath();

		// A lone player's bot teammate (phase 9b): the human it follows once they've landed, and where they were last shot from
		private Agent   _human;
		private Vector3 _leaderThreatPoint;
		private float   _leaderThreatTime = -100.0f;

		// Distant bots' pose is updated every few ticks (phase 9b, Logan's question 2)
		private bool    _thinned;

		// PUBLIC METHODS

		public BotBrain(Agent agent)
		{
			_agent = agent;
		}

		public void OnSpawned()
		{
			if (_all.Contains(this) == false)
			{
				_all.Add(this);
			}

			BotSettings settings = BotSettings.Current;

			_skillValue = Random.Range(Mathf.Min(settings.SkillMin, settings.SkillMax), Mathf.Max(settings.SkillMin, settings.SkillMax));
			_skill      = settings.GetSkill(_skillValue);
			_noiseSeed  = Random.Range(0.0f, 1000.0f);
			_nextThink  = Now + Random.Range(0.0f, 1.0f / Mathf.Max(1.0f, settings.ThinkRate));

			_state         = EBotState.Idle;
			_stateTime     = Now;
			_hasRoamPoint  = false;
			_hasForcedGoal = false;
			_roamArea      = -1;
			_stayUntil     = -1.0f;
			_human         = null;
			_dropActive    = false;
			_dropFollow    = null;

			// It starts by staying a while in the area it spawned in, looting as it walks around it (phase 9b); a bot spawned at the
			// Osprey's door does that where it lands (phase 11f)
			Vector3 spawnedAt = _agent.transform.position;   // The KCC may not have its position yet
			if (spawnedAt.y < MatchSettings.Current.HelicopterHeight * 0.5f)
			{
				SetHome(spawnedAt);
			}
			_leaderThreatTime = -100.0f;
			_thinned       = false;   // A spawned agent's animation starts on every tick
			_nav.Clear();
			ResetCombat();
			ResetLoot();

			BotDebug.Log(_agent.Runner, $"SPAWN {_agent.name} team {_agent.Team} skill {_skillValue:F2} (reaction {_skill.Reaction:F2} s, aim {_skill.AimErrorStart:F1}° → {_skill.AimErrorSettled:F1}°) at {_agent.transform.position:F0}");
		}

		/// <summary>The POI or cabin area holding the point becomes the one it stays in a while (BotSettings.AreaStay), looting around it.</summary>
		private void SetHome(Vector3 at)
		{
			BrownoutMap map  = BrownoutMap.Get(_agent.gameObject.scene);
			int         home = map != null ? FindArea(map, at) : -1;
			if (home < 0)
				return;

			Vector2 stay = BotSettings.Current.AreaStay;
			_roamArea     = home;
			_roamPoint    = at;
			_hasRoamPoint = true;
			_roamPickTime = Now;
			_stayUntil    = Now + Random.Range(Mathf.Min(stay.x, stay.y), Mathf.Max(stay.x, stay.y));
			_strollPoint  = at;
			_strollTime   = 0.0f;
		}

		/// <summary>Test hook (server): go to a point and stay there (no looting or roaming); Clear with null.</summary>
		public void DebugGoTo(Vector3? point)
		{
			_hasForcedGoal = point.HasValue;
			_forcedGoal    = point ?? default;
			_nav.Clear();

			if (point.HasValue == true)
			{
				_state = EBotState.Idle;
				SetState(EBotState.Roam, Now, $"test: going to {_forcedGoal:F0}");
			}
		}

		public bool HasForcedGoal => _hasForcedGoal;

		/// <summary>Test hook (server): a fixed skill (0 = Medium, 1 = Hard) instead of the rolled one.</summary>
		public void DebugSetSkill(float skill)
		{
			_skillValue = Mathf.Clamp01(skill);
			_skill      = BotSettings.Current.GetSkill(_skillValue);
		}

		public void OnDespawned()
		{
			_all.Remove(this);
			ReleaseClaims();
		}

		/// <summary>Server, before the agent's tick: this tick's input.</summary>
		public GameplayInput Tick()
		{
			long started = System.Diagnostics.Stopwatch.GetTimestamp();
			GameplayInput result = TickBrain();
			DebugTickTime += System.Diagnostics.Stopwatch.GetTimestamp() - started;
			++DebugTickCount;
			return result;
		}

		private GameplayInput TickBrain()
		{
			GameplayInput input = default;

			if (_agent.Object == null || _agent.Object.IsValid == false || _agent.Health.IsAlive == false)
				return input;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (BotDebug.Freeze == true)
				return input;
#endif
			// Phase 11f: bots drop from the Osprey to their spot (BotBrain.Drop)
			if (_agent.Movement.IsDropping == true)
				return TickDrop();

			if (_dropActive == true)
			{
				OnDropLanded();
			}

			float now = Now;

			if (now >= _nextThink)
			{
				_nextThink = now + 1.0f / Mathf.Max(1.0f, BotSettings.Current.ThinkRate);
				Think(now);
			}

			ClearOutput();

			switch (_state)
			{
				case EBotState.Fight:  UpdateFight(now);  break;
				case EBotState.Cover:  UpdateCover(now);  break;
				case EBotState.Heal:   UpdateHeal(now);   break;
				case EBotState.Loot:   UpdateLoot(now);   break;
				case EBotState.Roam:   UpdateRoam(now);   break;
				case EBotState.Follow: UpdateFollow(now); break;
				case EBotState.Downed: UpdateDowned(now); break;
			}

			// A shot heard nearby turns its head (sight does the rest)
			if (_state != EBotState.Fight && _state != EBotState.Cover && _state != EBotState.Downed && now - _heardTime < 2.0f)
			{
				LookAt(_heardPoint + Vector3.up * 1.3f, false);
			}

			UpdateStuck(now);

			return BuildInput(now);
		}

		// THINKING

		private void Think(float now)
		{
			bool humanNear = FindHumans();
			UpdateAnimationRate(now, humanNear);

			if (_agent.Health.IsDowned == true)
			{
				SetState(EBotState.Downed, now, "crawling away");
				return;
			}

			UpdateSenses(now);

			// A partner in a fight pulls it in: it goes to where the enemy was seen and looks for it
			if (_target == null)
			{
				BotBrain fighting = FindPartner();
				if (fighting != null && fighting._target != null && IsEnemy(fighting._target) == true && now - fighting._targetLastSeenTime < 2.0f)
				{
					SetTarget(fighting._target, now);
					_targetLastSeen     = fighting._targetLastSeen;
					_targetLastSeenTime = now;
					_targetVisible      = false;
				}
			}

			// Fighting comes first: an enemy in sight, remembered, or the one who just hit it
			if (HasFightTarget(now) == true)
			{
				if (_state == EBotState.Cover && IsCoverDone(now) == false)
					return;

				if (WantsCover(now) == true && TryStartCover(now) == true)
					return;

				if (_state != EBotState.Fight)
				{
					SetState(EBotState.Fight, now, $"fighting {_target.name}");
				}
				return;
			}

			if (_state == EBotState.Cover && IsCoverDone(now) == false)
				return;

			if (_hasForcedGoal == true)
			{
				if (_state != EBotState.Roam)
				{
					SetState(EBotState.Roam, now, $"test: going to {_forcedGoal:F0}");
				}
				return;
			}

			// Out of a fight: heal when hurt
			if (WantsHeal() == true)
			{
				if (_state != EBotState.Heal)
				{
					SetState(EBotState.Heal, now, "healing");
				}
				return;
			}

			if (_state == EBotState.Heal && _agent.Health.IsHealing == true)
				return;

			// Out of the zone: go now (loot can wait)
			if (IsOutsideZone(true) == true)
			{
				if (_state != EBotState.Roam)
				{
					SetState(EBotState.Roam, now, "into the zone");
				}
				return;
			}

			// The duo's second bot (or a lone player's bot, once its player has landed) doesn't fall behind: it catches up before it loots
			// (Unarmed, it takes a gun from close by first: a lone player's bot may have a long way to go)
			Agent leader = GetLeader();
			if (leader != null && FlatDistance(Position, PositionOf(leader)) > BotSettings.Current.FollowDistance.y * 1.5f && (HasUsableGun() == true || ThinkLoot(now) == false))
			{
				if (_state != EBotState.Follow)
				{
					SetState(EBotState.Follow, now, $"catching up with {leader.name}");
				}
				return;
			}

			// Loot nearby that it wants
			if (ThinkLoot(now) == true)
			{
				if (_state != EBotState.Loot)
				{
					SetState(EBotState.Loot, now, "looting");
				}
				return;
			}

			// The duo's second bot follows the first; a lone player's bot follows its player
			if (leader != null)
			{
				if (_state != EBotState.Follow)
				{
					SetState(EBotState.Follow, now, $"following {leader.name}");
				}
				return;
			}

			if (_state != EBotState.Roam)
			{
				SetState(EBotState.Roam, now, "roaming");
			}
		}

		private void SetState(EBotState state, float now, string goal)
		{
			if (_state == state)
				return;

			EBotState previous = _state;

			if (previous == EBotState.Cover)
			{
				OnLeaveCover(now);
			}

			_state     = state;
			_stateTime = now;
			_goal      = goal;
			_nav.Clear();

			BotDebug.Log(_agent.Runner, $"STATE {_agent.name} {previous} → {state}: {goal}");
		}

		// ROAMING AND FOLLOWING

		private void UpdateRoam(float now)
		{
			Vector3 position = Position;

			if (_hasForcedGoal == true)
			{
				if (FlatDistance(position, _forcedGoal) > 1.0f)
				{
					MoveTo(_forcedGoal, now, true);
				}
				return;
			}

			// The zone first
			if (TryGetZoneGoal(out Vector3 zoneGoal, out bool urgent) == true)
			{
				if (_hasRoamPoint == false || IsInsideNextZone(_roamPoint) == false)
				{
					_roamPoint    = zoneGoal;
					_hasRoamPoint = true;
					_roamArea     = -1;
					BotDebug.Log(_agent.Runner, $"ZONE {_agent.name} heading into the zone at {_roamPoint:F0}{(urgent ? " (outside it)" : string.Empty)}");
				}

				_goal = urgent == true ? "into the zone" : "toward the next circle";
				_stayUntil = -1.0f;
			}
			else
			{
				// At an area it stays a while, walking around it, before heading for the next
				if (_hasRoamPoint == true && _stayUntil < 0.0f && _roamArea >= 0 && FlatDistance(position, _roamPoint) < 6.0f)
				{
					Vector2 stay = BotSettings.Current.AreaStay;
					_stayUntil = now + Random.Range(Mathf.Min(stay.x, stay.y), Mathf.Max(stay.x, stay.y));
					_strollTime  = 0.0f;
					_strollPoint = _roamPoint;
				}

				if (_stayUntil >= 0.0f && now < _stayUntil)
				{
					if (now > _strollTime || FlatDistance(position, _strollPoint) < 2.0f)
					{
						// Anywhere in the area's box (where its loot is), else around the spot it came to
						BrownoutMap map = BrownoutMap.Get(_agent.gameObject.scene);
						Vector3 around = _roamPoint;
						Vector2 offset = Random.insideUnitCircle * 15.0f;
						if (map != null && _roamArea >= 0 && _roamArea < map.Areas.Count)
						{
							BrownoutMap.Area area = map.Areas[_roamArea];
							around = new Vector3(area.Center.x, position.y, area.Center.y);
							offset = new Vector2(Random.Range(-0.4f, 0.4f) * area.Size.x, Random.Range(-0.4f, 0.4f) * area.Size.y);
						}

						if (BotNavigation.Sample(around + new Vector3(offset.x, 0.0f, offset.y), 8.0f, out Vector3 stroll) == true && BotNavigation.CanReach(position, stroll, _strollPath, 1.0f) == true)
						{
							_strollPoint = stroll;
						}
						_strollTime = now + Random.Range(6.0f, 12.0f);
					}

					_goal = $"looking around ({_stayUntil - now:F0} s)";
					MoveTo(_strollPoint, now, false);
					return;
				}

				if (_hasRoamPoint == false || _stayUntil >= 0.0f || (_roamArea < 0 && _nav.DistanceToDestination(position) < 4.0f) || now - _roamPickTime > 90.0f)
				{
					_stayUntil = -1.0f;
					PickRoamPoint(now);
				}
			}

			if (_hasRoamPoint == false)
				return;

			MoveTo(_roamPoint, now, true);
		}

		private void UpdateFollow(float now)
		{
			Agent leaderAgent = GetLeader();
			if (leaderAgent == null)
				return;

			Vector3 position = Position;
			Vector3 leader   = PositionOf(leaderAgent);
			float   distance = FlatDistance(position, leader);
			Vector2 follow   = BotSettings.Current.FollowDistance;

			// A leader in a fight pulls its partner into it (a bot leader's target; a human leader's last attacker)
			BotBrain partner = leaderAgent.Bot;
			bool     leaderFighting = partner != null ? partner._target != null && partner._target.Health.IsAlive == true : now - _leaderThreatTime < 5.0f;
			if (leaderFighting == true)
			{
				_goal = $"helping {leaderAgent.name}";
				if (distance > follow.x)
				{
					MoveTo(leader, now, distance > BotSettings.Current.SprintDistance);
				}
				LookAt((partner != null ? partner._targetLastSeen : _leaderThreatPoint) + Vector3.up * 1.3f, false);
				return;
			}

			_goal = $"following {leaderAgent.name}";

			if (distance > follow.y || (distance > follow.x && _nav.HasDestination == true))
			{
				// Aim for a spot a few meters short of the leader
				Vector3 toLeader = leader - position;
				toLeader.y = 0.0f;
				Vector3 spot = leader - toLeader.normalized * (follow.x * 0.8f);
				MoveTo(spot, now, distance > BotSettings.Current.SprintDistance);

				if (distance <= follow.x)
				{
					_nav.Clear();
				}
			}
		}

		private void PickRoamPoint(float now)
		{
			_roamPickTime = now;

			BrownoutMap map = BrownoutMap.Get(_agent.gameObject.scene);
			Vector3     position = Position;

			if (map != null && map.Areas.Count > 0)
			{
				// The next area: one of the three nearest it isn't in, inside the zone when there is one
				var candidates = new List<(int Index, float Distance)>(map.Areas.Count);
				for (int i = 0; i < map.Areas.Count; ++i)
				{
					BrownoutMap.Area area = map.Areas[i];
					Vector3 center = new Vector3(area.Center.x, position.y, area.Center.y);
					float   distance = FlatDistance(position, center);

					if (i == _roamArea || distance < Mathf.Max(area.Size.x, area.Size.y) * 0.5f)
						continue;
					if (IsInsideNextZone(center) == false)
						continue;

					candidates.Add((i, distance));
				}

				candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

				if (candidates.Count > 0)
				{
					int pick = candidates[Random.Range(0, Mathf.Min(3, candidates.Count))].Index;
					BrownoutMap.Area area = map.Areas[pick];
					Vector2 offset = new Vector2(Random.Range(-0.3f, 0.3f) * area.Size.x, Random.Range(-0.3f, 0.3f) * area.Size.y);
					Vector3 point  = new Vector3(area.Center.x + offset.x, position.y, area.Center.y + offset.y);

					if (BotNavigation.Sample(point, 40.0f, out Vector3 onMesh) == true || BotNavigation.Sample(new Vector3(point.x, GroundHeight(point), point.z), 20.0f, out onMesh) == true)
					{
						point = onMesh;
					}

					_roamArea     = pick;
					_roamPoint    = point;
					_hasRoamPoint = true;
					_goal         = $"going to {area.Name}";
					return;
				}
			}

			// No map areas (a generated level): a random point around
			Vector2 random = Random.insideUnitCircle * 60.0f;
			Vector3 target = position + new Vector3(random.x, 0.0f, random.y);
			BotNavigation.Sample(target, 20.0f, out target);
			_roamPoint    = target;
			_hasRoamPoint = true;
			_goal         = "wandering";
		}

		// DOWNED

		private void UpdateDowned(float now)
		{
			// Crawl away from whoever downed it, toward cover if it knows any
			Vector3 position = Position;
			Vector3 away = _lastAttacker != null ? position - _lastAttacker.transform.position : -Forward;
			away.y = 0.0f;

			if (away.sqrMagnitude < 0.01f)
				return;

			_move = away.normalized;
			LookAt(position + away.normalized * 5.0f + Vector3.up * 0.5f, false);
		}

		// OUTPUT

		private void ClearOutput()
		{
			_move        = Vector3.zero;
			_hasLook     = false;
			_preciseLook = false;
			_sprint      = false;
			_crouch      = false;
			_aim         = false;
			_attack      = false;
			_reload      = false;
			_heal        = false;
			_jump        = false;
			_melee       = false;
			_weapon      = 0;
		}

		/// <summary>Walks the path to a point; looks where it's going unless something else sets the look this tick.</summary>
		private void MoveTo(Vector3 destination, float now, bool sprint)
		{
			Vector3 position = Position;
			_nav.SetDestination(position, destination, now);

			_move = _nav.Steer(position);

			if (_move.sqrMagnitude > 0.01f)
			{
				_sprint = sprint == true && _nav.DistanceToDestination(position) > BotSettings.Current.SprintDistance * 0.5f;

				if (_hasLook == false)
				{
					LookAt(Eye + _move.normalized * 10.0f, false);
				}
			}
		}

		private void LookAt(Vector3 point, bool precise)
		{
			_hasLook     = true;
			_lookPoint   = point;
			_preciseLook = precise;
		}

		private void UpdateStuck(float now)
		{
			bool wantsToMove = _move.sqrMagnitude > 0.04f && _state != EBotState.Downed;

			if (now < _unstickUntil)
			{
				_move = _unstickMove;
				return;
			}

			if (_nav.UpdateStuck(Position, now, wantsToMove) == false)
				return;

			int step = _nav.StuckCount;
			BotDebug.Log(_agent.Runner, $"STUCK {_agent.name} at {Position:F1} in {_state} (try {step})");

			switch (step % 3)
			{
				case 1:
					// Jump (also mantles onto a low ledge)
					_jump = true;
					break;
				case 2:
					// Step sideways for a moment
					Vector3 side = Vector3.Cross(Vector3.up, _move.normalized) * (Random.value < 0.5f ? 1.0f : -1.0f);
					_unstickMove  = (side + -_move.normalized * 0.3f).normalized;
					_unstickUntil = now + 0.7f;
					break;
				default:
					// New path, and a new goal if it keeps failing
					_nav.Repath(Position, now);
					if (step >= 6)
					{
						_hasRoamPoint = false;
						AbandonLootTarget("stuck");
						_nav.Clear();
					}
					break;
			}
		}

		private GameplayInput BuildInput(float now)
		{
			GameplayInput input = default;
			KCC     kcc  = _agent.Character.CharacterController;
			KCCData data = kcc.FixedData;

			// Look: turn toward the look point, at the skill's speed when aiming, the walking speed otherwise
			if (_hasLook == true)
			{
				input.LookRotationDelta = GetLookDelta(data, _lookPoint, _preciseLook);
			}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (BotDebug.HoldStill == true)
			{
				_move   = Vector3.zero;
				_crouch = false;
				_jump   = false;
			}
#endif
			// Move: the world direction in the body's frame
			if (_move.sqrMagnitude > 0.0001f)
			{
				Vector3 local = Quaternion.Inverse(data.TransformRotation) * _move;
				Vector2 move  = new Vector2(local.x, local.z);
				input.MoveDirection = move.sqrMagnitude > 1.0f ? move.normalized : move;
			}

			// Sprint only forward, never a quick re-press (that's a dash), never while aiming or healing
			bool sprint = _sprint == true && _aim == false && _heal == false && input.MoveDirection.y > 0.7f;
			if (sprint == true && _sprintHeld == false && now - _sprintReleasedTime < 0.6f)
			{
				sprint = false;
			}

			if (_sprintHeld == true && sprint == false)
			{
				_sprintReleasedTime = now;
			}

			_sprintHeld = sprint;

			input.Sprint = sprint;
			input.Crouch = _crouch == true && sprint == false;
			input.Aim    = _aim;
			input.Attack = _attack;
			input.Reload = _reload;
			input.Heal   = _heal;
			input.Jump   = _jump;
			input.Melee  = _melee;
			input.Weapon = _weapon;

			return input;
		}

		/// <summary>Pitch and yaw to turn this tick toward a point.</summary>
		private Vector2 GetLookDelta(KCCData data, Vector3 point, bool precise)
		{
			Vector3 eye = Eye;
			Vector3 to  = point - eye;
			float   flat = new Vector2(to.x, to.z).magnitude;

			if (flat < 0.01f && Mathf.Abs(to.y) < 0.01f)
				return Vector2.zero;

			float yaw   = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
			float pitch = -Mathf.Atan2(to.y, Mathf.Max(0.01f, flat)) * Mathf.Rad2Deg;
			float dt    = _agent.Runner.DeltaTime;

			// How fast the point it aims at moves across its view (degrees per second), smoothed over a few ticks
			Vector2 aimRate = Vector2.zero;
			if (precise == true)
			{
				if (_hasLastAim == true)
				{
					Vector2 rate = new Vector2(Mathf.DeltaAngle(_lastAim.x, pitch), Mathf.DeltaAngle(_lastAim.y, yaw)) / dt;
					_aimRate = Vector2.Lerp(_aimRate, rate, 0.5f);
				}

				_lastAim    = new Vector2(pitch, yaw);
				_hasLastAim = true;
				aimRate     = _aimRate;
			}
			else
			{
				_hasLastAim = false;
				_aimRate    = Vector2.zero;
			}

			if (precise == true)
			{
				Vector2 error = GetAimError();
				pitch += error.x;
				yaw   += error.y;

				// Pulls down only part of the recoil, by skill
				Vector2 recoil = data.Recoil;
				pitch += recoil.x * (1.0f - _skill.RecoilControl);
				yaw   += recoil.y * (1.0f - _skill.RecoilControl);
			}

			Vector2 current = data.GetLookRotation(true, true);
			Vector2 delta   = new Vector2(Mathf.DeltaAngle(current.x, pitch), Mathf.DeltaAngle(current.y, yaw));

			float maxStep  = (precise == true ? _skill.TurnSpeed : BotSettings.Current.WalkTurnSpeed) * dt;
			// Closes the gap smoothly and follows the target's motion by the skill's share (a strafing target is tracked, not chased)
			Vector2 step   = precise == true ? delta * (1.0f - Mathf.Exp(-dt * 18.0f)) + aimRate * (_skill.Tracking * dt) : delta;

			if (delta.magnitude < 0.05f)
			{
				step = delta;
			}

			if (step.magnitude > maxStep)
			{
				step = step.normalized * maxStep;
			}

			_lastAimOffset = (delta - step).magnitude;
			return step;
		}

		// HELPERS

		private float   Now      => _agent.Runner.Tick * _agent.Runner.DeltaTime;
		private Vector3 Position => _agent.Character.CharacterController.FixedData.TargetPosition;
		private Vector3 Eye      => Position + Vector3.up * EyeHeightOf(_agent);
		private Vector3 Forward
		{
			get
			{
				Vector2 look = _agent.Character.CharacterController.FixedData.GetLookRotation(true, true);
				return Quaternion.Euler(look.x, look.y, 0.0f) * Vector3.forward;
			}
		}

		private static float EyeHeightOf(Agent agent)
		{
			float eye = agent.Character.CharacterController.FixedData.EyeHeight;
			return eye > 0.1f ? eye : MovementSettings.Current.StandEyeHeight;
		}

		private static float FlatDistance(Vector3 a, Vector3 b)
		{
			float x = a.x - b.x;
			float z = a.z - b.z;
			return Mathf.Sqrt(x * x + z * z);
		}

		private float GroundHeight(Vector3 point)
		{
			if (_agent.Runner.GetPhysicsScene().Raycast(new Vector3(point.x, 300.0f, point.z), Vector3.down, out RaycastHit hit, 600.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
				return hit.point.y;

			return point.y;
		}

		private BotBrain FindPartner()
		{
			int team = _agent.Team;
			if (team == 0)
				return null;

			for (int i = 0; i < _all.Count; ++i)
			{
				BotBrain other = _all[i];
				if (other != this && other._agent != null && other._agent.Runner == _agent.Runner && other._agent.Team == team && other._agent.Health.IsAlive == true)
					return other;
			}

			return null;
		}

		/// <summary>Who it follows: its human teammate once they've landed (a lone player's bot), else the duo's first bot; null when it leads.</summary>
		private Agent GetLeader()
		{
			Agent human = _human;
			if (human != null && human.Object != null && human.Object.IsValid == true && human.Health.IsAlive == true && human.Movement.IsDropping == false)
				return human;

			BotBrain partner = FindPartner();
			return partner != null && IsDuoLeader() == false ? partner._agent : null;
		}

		/// <summary>The POI or cabin area whose box holds the point, or -1.</summary>
		private static int FindArea(BrownoutMap map, Vector3 point)
		{
			for (int i = 0; i < map.Areas.Count; ++i)
			{
				BrownoutMap.Area area = map.Areas[i];
				if (Mathf.Abs(point.x - area.Center.x) <= area.Size.x * 0.5f + 5.0f && Mathf.Abs(point.z - area.Center.y) <= area.Size.y * 0.5f + 5.0f)
					return i;
			}

			return -1;
		}

		private static Vector3 PositionOf(Agent agent) => agent.Character.CharacterController.FixedData.TargetPosition;

		/// <summary>
		/// Looks over the human players' agents: remembers a human teammate (a lone player's bot) and says whether any human is within
		/// BotSettings.ThinDistance.
		/// </summary>
		private bool FindHumans()
		{
			float range    = BotSettings.Current.ThinDistance;
			bool  near     = false;
			Agent teammate = null;
			Vector3 position = Position;

			_agentBuffer.Clear();
			_agent.Runner.GetAllBehaviours(_agentBuffer);

			for (int i = 0; i < _agentBuffer.Count; ++i)
			{
				Agent other = _agentBuffer[i];
				if (other == _agent || other.Object == null || other.Object.IsValid == false || other.Object.InputAuthority.IsRealPlayer == false || other.Health.IsAlive == false)
					continue;

				if (other.Team == _agent.Team && GameplayMode.AreTeammates(_agent, other) == true)
				{
					teammate = other;
				}

				if ((other.transform.position - position).sqrMagnitude < range * range)
				{
					near = true;
				}
			}

			_agentBuffer.Clear();

			if (teammate != _human)
			{
				_human = teammate;
				if (teammate != null)
				{
					BotDebug.Log(_agent.Runner, $"TEAMMATE {_agent.name} is with {teammate.name}{(teammate.Movement.IsDropping ? " (still in the air)" : string.Empty)}");
				}
			}

			return near;
		}

		/// <summary>Far from every human and not fighting: its pose is updated every BotSettings.ThinEveryTicks ticks (server and host view).</summary>
		private void UpdateAnimationRate(float now, bool humanNear)
		{
			BotSettings settings = BotSettings.Current;

			bool fighting = _state == EBotState.Fight || _state == EBotState.Cover || _state == EBotState.Downed || _target != null || now - _lastHitTime < 5.0f || now - _heardTime < 3.0f;
			bool thin     = settings.ThinEveryTicks > 1 && humanNear == false && fighting == false && _agent.Health.IsAlive == true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (BotDebug.NoThinning == true)
			{
				thin = false;
			}
#endif
			if (thin == _thinned)
				return;

			_thinned = thin;

			var animation = _agent.Character.AnimationController;
			if (thin == true)
			{
				// The server's tick rate goes through Character (the sample sets it there every tick); the host's own view here
				_agent.AnimationEveryTicks = settings.ThinEveryTicks;
				animation.SetInterlacedEvaluation(Fusion.Addons.AnimationController.EEvaluationTarget.RenderUpdate, settings.ThinEveryTicks, _agent.BotNumber % settings.ThinEveryTicks);
			}
			else
			{
				_agent.AnimationEveryTicks = 1;
				animation.SetEvaluationMode(Fusion.Addons.AnimationController.EEvaluationTarget.RenderUpdate, Fusion.Addons.AnimationController.EEvaluationMode.Full);
			}
		}

		/// <summary>Server, from Health: a human was hit; their bot teammates turn on the attacker and come to help.</summary>
		public static void OnHumanHit(Agent victim, HitData hit)
		{
			Agent attacker = hit.Instigator is Health health ? health.Agent : null;
			if (victim == null || attacker == null || attacker == victim || GameplayMode.AreTeammates(attacker, victim) == true)
				return;

			for (int i = 0; i < _all.Count; ++i)
			{
				BotBrain bot = _all[i];
				if (bot._agent.Runner != victim.Runner || GameplayMode.AreTeammates(bot._agent, victim) == false || bot._agent.Health.IsUp == false)
					continue;

				float now = bot.Now;
				bot._leaderThreatPoint = attacker.transform.position;
				bot._leaderThreatTime  = now;
				bot.OnPartnerHit(attacker, now);
			}
		}

		/// <summary>The duo's first bot leads; a bot alone, or with a human teammate, leads itself.</summary>
		private bool IsDuoLeader()
		{
			BotBrain partner = FindPartner();
			return partner == null || partner._agent.Health.IsUp == false || _agent.BotNumber < partner._agent.BotNumber;
		}

		// ZONE

		private ShrinkingArea Zone
		{
			get
			{
				GameplayMode mode = _agent.Context != null ? _agent.Context.GameplayMode : null;
				ShrinkingArea zone = mode != null ? mode.ShrinkingArea : null;
				return zone != null && zone.IsScheduled == true ? zone : null;
			}
		}

		private bool IsOutsideZone(bool current)
		{
			ShrinkingArea zone = Zone;
			if (zone == null)
				return false;

			Vector3 center = current == true ? zone.Center : zone.ShrinkCenter;
			float   radius = current == true ? zone.Radius : zone.ShrinkRadius;
			return radius > 0.0f && FlatDistance(Position, center) > radius * 0.95f;
		}

		private bool IsInsideNextZone(Vector3 point)
		{
			ShrinkingArea zone = Zone;
			if (zone == null)
				return true;

			float radius = zone.ShrinkRadius > 0.0f ? zone.ShrinkRadius : zone.Radius;
			Vector3 center = zone.ShrinkRadius > 0.0f ? zone.ShrinkCenter : zone.Center;
			return FlatDistance(point, center) < radius * 0.9f;
		}

		/// <summary>A point well inside the next circle when it's outside it now.</summary>
		private bool TryGetZoneGoal(out Vector3 goal, out bool urgent)
		{
			goal   = default;
			urgent = IsOutsideZone(true);

			ShrinkingArea zone = Zone;
			if (zone == null || (urgent == false && IsOutsideZone(false) == false))
				return false;

			Vector3 center = zone.ShrinkRadius > 0.0f ? zone.ShrinkCenter : zone.Center;
			float   radius = zone.ShrinkRadius > 0.0f ? zone.ShrinkRadius : zone.Radius;

			if (_hasRoamPoint == true && FlatDistance(_roamPoint, center) < radius * 0.6f)
			{
				goal = _roamPoint;
				return true;
			}

			Vector2 offset = Random.insideUnitCircle * radius * 0.5f;
			goal = new Vector3(center.x + offset.x, Position.y, center.z + offset.y);
			goal.y = GroundHeight(goal);
			BotNavigation.Sample(goal, 20.0f, out goal);
			return true;
		}
	}
}
