using UnityEngine;
using Fusion;

namespace TPSBR
{
	public sealed class ShrinkingArea : ContextBehaviour
	{
		// PUBLIC MEMBERS

		public bool      IsActive       { get { return _flags.IsBitSet(0); } private set { _flags = _flags.SetBitNoRef(0, value); } }
		public bool      IsShrinking    { get { return _flags.IsBitSet(1); } private set { _flags = _flags.SetBitNoRef(1, value); } }
		public bool      IsAnnounced    { get { return _flags.IsBitSet(2); } private set { _flags = _flags.SetBitNoRef(2, value); } }
		public bool      IsPaused       { get { return _flags.IsBitSet(3); } private set { _flags = _flags.SetBitNoRef(3, value); } }
		/// <summary>Phase 7: running Brownout's schedule from MatchSettings instead of the sample's.</summary>
		public bool      IsScheduled    { get { return _flags.IsBitSet(4); } private set { _flags = _flags.SetBitNoRef(4, value); } }

		/// <summary>Phase 7: the MatchSettings.ZoneSteps entry running now (-1 before the start, ZoneSteps.Length after the final close).</summary>
		[Networked, HideInInspector]
		public int       ScheduleStep   { get; private set; }
		/// <summary>Phase 7: the tick the current step ends (0 when nothing more is coming).</summary>
		[Networked, HideInInspector]
		public int       StepEndTick    { get; private set; }
		/// <summary>Phase 7: damage per second outside the circle right now.</summary>
		[Networked, HideInInspector]
		public float     StepDamage     { get; private set; }
		/// <summary>Phase 7: the tick the schedule started (the helicopter left the map).</summary>
		[Networked, HideInInspector]
		public int       ScheduleStartTick { get; private set; }

		/// <summary>Phase 7: seconds until the current step ends (0 when nothing more is coming).</summary>
		public float     StepTimeLeft   => StepEndTick > 0 && Runner != null ? Mathf.Max(0.0f, (StepEndTick - Runner.Tick) * Runner.DeltaTime) : 0.0f;
		/// <summary>Phase 7: the current step's phase (Wait while nothing runs).</summary>
		public EZonePhase StepPhase
		{
			get
			{
				MatchSettings.ZoneStep[] steps = MatchSettings.Current.ZoneSteps;
				return steps != null && ScheduleStep >= 0 && ScheduleStep < steps.Length ? steps[ScheduleStep].Phase : EZonePhase.Wait;
			}
		}

		[Networked, HideInInspector]
		public float     ShrinkDelay    { get; private set; }

		public TickTimer NextShrinking  => _nextShrinking;
		public bool      IsFinished     => Radius <= _endRadius;

		[Networked, HideInInspector]
		public  Vector3  Center        { get; set; }
		[Networked, HideInInspector]
		public  float    Radius        { get; set; }
		[Networked, HideInInspector]
		public  Vector3  ShrinkCenter  { get; set; }
		[Networked, HideInInspector]
		public  float    ShrinkRadius  { get; set; }

		public System.Action<Vector3, float> ShrinkingAnnounced;

		// PRIVATE MEMBERS

		[Header("Size")]
		[SerializeField]
		private float     _mapRadius = 200f;
		[SerializeField]
		private float     _startRadius = 100f;
		[SerializeField]
		private float     _endRadius = 40f;
		[SerializeField]
		private Transform _shrinkArea;
		[SerializeField]
		private Transform _shrinkAreaTarget;

		[Header("Timing")]
		[SerializeField]
		private float _shrinkStartDelay = 30f;
		[SerializeField]
		private float _minShrinkDelay = 35f;
		[SerializeField]
		private float _maxShrinkDelay = 90f;
		[SerializeField]
		private int   _minShrinkDelayPlayers = 2;
		[SerializeField]
		private int   _maxShrinkDelayPlayers = 60;
		[SerializeField]
		private float _shrinkDuration = 20f;
		[SerializeField]
		private float _shrinkAnnounceDuration = 30f;
		[SerializeField]
		private int   _shrinkSteps = 5;

		[Header("Damage")]
		[SerializeField]
		private float _damagePerTick = 5f;
		[SerializeField]
		private float _damageTickTime = 1f;

		[Networked]
		private byte      _flags         { get; set; }
		[Networked]
		private TickTimer _nextShrinking { get; set; }

		private int         _currentStage;
		private AreaStage[] _calculatedStages;

		private int       _shrinkStartTick;
		private int       _shrinkEndTick;

		private Material  _shrinkAreaMaterial;
		private int       _materialRadiusID;

		private TickTimer _damageTimer;

		// PUBLIC METHODS

		public void Activate()
		{
			if (IsActive == true)
				return;
			if (HasStateAuthority == false)
				return;

			IsActive = true;

			if (_calculatedStages == null)
			{
				var endCenter = new Vector2(transform.position.x, transform.position.z) + Random.insideUnitCircle * (_mapRadius - _endRadius);
				CalculateStages(endCenter);
			}

			var startStage = _calculatedStages[0];

			Center = new Vector3(startStage.Center.x, transform.position.y, startStage.Center.y);
			Radius = startStage.Radius;

			_damageTimer = TickTimer.CreateFromSeconds(Runner, _damageTickTime);
			SetNextShrinkingTimer(_shrinkStartDelay);
		}

		public void Deactivate()
		{
			if (HasStateAuthority == false)
				return;

			IsActive     = false;
			IsScheduled  = false;
			IsShrinking  = false;
			IsAnnounced  = false;
			ScheduleStep = -1;
			StepEndTick  = 0;
			StepDamage   = 0.0f;
		}

		/// <summary>
		/// Phase 7 (server): starts Brownout's zone (Match Flow & Zone): ZoneStartRadius on the map center, the radius times ZoneShrink at
		/// each close and 0 at the final one, every new circle at a random spot fully inside the last (and its center on the map), steps and
		/// damage from MatchSettings.ZoneSteps. The first circle is shown at once, each later one when the previous close ends.
		/// </summary>
		public void StartSchedule(Vector3 mapCenter, Vector2 mapHalfSize)
		{
			if (HasStateAuthority == false)
				return;

			MatchSettings settings = MatchSettings.Current;
			int           closes   = Mathf.Max(1, settings.CloseCount);

			_calculatedStages = new AreaStage[closes + 1];

			Vector2 center = new Vector2(mapCenter.x, mapCenter.z);
			float   radius = Mathf.Max(1.0f, settings.ZoneStartRadius);
			_calculatedStages[0] = new AreaStage(center, radius);

			for (int i = 1; i <= closes; ++i)
			{
				float nextRadius = i == closes ? 0.0f : radius * Mathf.Clamp01(settings.ZoneShrink);
				Vector2 next = center;

				for (int attempt = 0; attempt < 50; ++attempt)
				{
					Vector2 candidate = center + Random.insideUnitCircle * Mathf.Max(0.0f, radius - nextRadius);
					next = candidate;

					if (Mathf.Abs(candidate.x - mapCenter.x) <= mapHalfSize.x && Mathf.Abs(candidate.y - mapCenter.z) <= mapHalfSize.y)
						break;

					next = center;
				}

				center = next;
				radius = nextRadius;
				_calculatedStages[i] = new AreaStage(center, radius);
			}

			_currentStage = 0;

			Center            = new Vector3(_calculatedStages[0].Center.x, transform.position.y, _calculatedStages[0].Center.y);
			Radius            = _calculatedStages[0].Radius;
			IsActive          = true;
			IsPaused          = false;
			IsScheduled       = true;
			IsShrinking       = false;
			IsAnnounced       = false;
			ScheduleStartTick = Runner.Tick;
			_damageTimer      = TickTimer.CreateFromSeconds(Runner, _damageTickTime);

			MatchDebug.Log(Runner, $"ZONE start: {Radius:F0} m on {Center.ToString("F0")}, {closes} closes, clock x{MatchSettings.TimeScale:F1}");

			BeginStep(0);
		}

		/// <summary>Phase 7 (server): freezes the zone where it is (the match is over). No more damage.</summary>
		public void StopSchedule()
		{
			if (HasStateAuthority == false)
				return;

			IsPaused    = true;
			StepEndTick = 0;
			StepDamage  = 0.0f;
		}

		/// <summary>Phase 7, test tool (server): ends the current step now.</summary>
		public void SkipStep()
		{
			if (HasStateAuthority == false || IsScheduled == false)
				return;

			if (IsShrinking == true)
			{
				_shrinkEndTick = Runner.Tick;
				UpdateScheduledShrinking();
				return;
			}

			BeginStep(ScheduleStep + 1);
		}

		public void Pause(bool pause)
		{
			if (IsPaused == pause)
				return;

			IsPaused = pause;

			if (pause == false)
			{
				_damageTimer = TickTimer.CreateFromSeconds(Runner, _damageTickTime);
				SetNextShrinkingTimer();
			}
		}

		public void OverrideParameters(Vector3 center, float mapRadius, float startRadius, float endRadius)
		{
			transform.position = center;

			_mapRadius = mapRadius;
			_startRadius = startRadius;
			_endRadius = endRadius;
		}

		public bool SetEndCenter(Vector2 endCenter, bool force = false)
		{
			if (HasStateAuthority == false)
				return false;

			var position = new Vector2(transform.position.x, transform.position.z);

			if (Vector2.Distance(endCenter, position) > _mapRadius - _endRadius)
			{
				if (force == false)
					return false;

				var direction = endCenter - position;
				endCenter = position + direction.normalized * Random.value * (_mapRadius - _endRadius);
			}

			CalculateStages(endCenter);
			return true;
		}

		// NetworkBehaviour INTERFACE

		public override void FixedUpdateNetwork()
		{
			if (IsActive == false || IsPaused == true)
				return;

			if (HasStateAuthority == false)
				return;

			if (IsScheduled == true)
			{
				FixedUpdateSchedule();
				return;
			}

			if (IsPaused == false)
			{
				if (_nextShrinking.IsRunning == true)
				{
					var remainingTime = _nextShrinking.RemainingTime(Runner);

					if (remainingTime <= 0f)
					{
						Shrink();
					}
					else if (remainingTime <= _shrinkAnnounceDuration)
					{
						Announce();
					}
				}

				if (IsShrinking == true)
				{
					UpdateShrinking();
				}
			}

			if (_damageTimer.IsRunning == true && _damageTimer.Expired(Runner) == true)
			{
				UpdateDamage();
			}
		}

		public override void Render()
		{
			if (IsActive == true)
			{
				_shrinkArea.SetActive(true);

				float radius = Mathf.Max(0.01f, Radius);
				_shrinkArea.position   = Center;
				_shrinkArea.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
				_shrinkAreaMaterial.SetFloat(_materialRadiusID, radius);

				// Phase 11 (Logan, 10 Oct): the next circle isn't drawn in the world (too hard to see through); the minimap and full map show it
				_shrinkAreaTarget.SetActive(false);
			}
			else
			{
				_shrinkAreaTarget.SetActive(false);
				_shrinkArea.SetActive(false);
			}
		}

		// MONOBEHAVIOUR

		private void Start()
		{
			_shrinkAreaMaterial = _shrinkArea.GetComponentInChildren<MeshRenderer>(true).material;
			_materialRadiusID = Shader.PropertyToID("Radius");
		}

		private void OnDrawGizmosSelected()
		{
			var tmpColor = Gizmos.color;

			Gizmos.color = Color.green;
			Gizmos.DrawWireSphere(transform.position, _startRadius);

			Gizmos.color = Color.red;
			Gizmos.DrawWireSphere(transform.position, _endRadius);

			Gizmos.color = tmpColor;
		}

		// PRIVATE METHODS

		private void FixedUpdateSchedule()
		{
			if (IsPaused == true)
				return;

			if (IsShrinking == true)
			{
				UpdateScheduledShrinking();
			}
			else if (StepEndTick > 0 && Runner.Tick >= StepEndTick)
			{
				BeginStep(ScheduleStep + 1);
			}

			if (_damageTimer.IsRunning == true && _damageTimer.Expired(Runner) == true)
			{
				_damageTimer = TickTimer.CreateFromSeconds(Runner, _damageTickTime);

				if (StepDamage > 0.0f)
				{
					DamageOutside(StepDamage);
				}
			}
		}

		private void BeginStep(int index)
		{
			MatchSettings.ZoneStep[] steps = MatchSettings.Current.ZoneSteps;
			int count = steps != null ? steps.Length : 0;

			if (index >= count)
			{
				// After the final close: nothing left, the damage stays
				ScheduleStep = count;
				StepEndTick  = 0;
				MatchDebug.Log(Runner, $"ZONE schedule over, {StepDamage:F0} per second everywhere");
				return;
			}

			MatchSettings.ZoneStep step = steps[index];

			ScheduleStep = index;
			StepDamage   = step.Damage;
			StepEndTick  = Runner.Tick + Mathf.Max(1, Mathf.CeilToInt(step.Duration / MatchSettings.TimeScale / Runner.DeltaTime));

			if (IsAnnounced == false && _currentStage + 1 < _calculatedStages.Length)
			{
				// The next circle shows as soon as the previous close ends (and at the start)
				AreaStage next = _calculatedStages[_currentStage + 1];
				ShrinkRadius = next.Radius;
				ShrinkCenter = new Vector3(next.Center.x, transform.position.y, next.Center.y);
				IsAnnounced  = true;
			}

			if (step.Phase == EZonePhase.Close && IsAnnounced == true)
			{
				_currentStage   += 1;
				_shrinkStartTick = Runner.Tick;
				_shrinkEndTick   = StepEndTick;
				IsShrinking      = true;
			}

			MatchDebug.Log(Runner, $"ZONE step {index + 1}/{count} {step.Phase} {step.Duration:F0} s, damage {step.Damage:F0}/s, circle {Radius:F0} m on {Center.ToString("F0")}{(IsAnnounced ? $", next {ShrinkRadius:F0} m on {ShrinkCenter.ToString("F0")}" : string.Empty)}");
		}

		private void UpdateScheduledShrinking()
		{
			AreaStage previous = _calculatedStages[_currentStage - 1];
			int       tick     = Runner.Tick;

			if (tick >= _shrinkEndTick)
			{
				Radius      = ShrinkRadius;
				Center      = ShrinkCenter;
				IsShrinking = false;
				IsAnnounced = false;

				BeginStep(ScheduleStep + 1);
				return;
			}

			float progress = (tick - _shrinkStartTick) / (float)Mathf.Max(1, _shrinkEndTick - _shrinkStartTick);

			Radius = Mathf.Lerp(previous.Radius, ShrinkRadius, progress);
			Center = Vector3.Lerp(new Vector3(previous.Center.x, transform.position.y, previous.Center.y), ShrinkCenter, progress);
		}

		/// <summary>Phase 7: every agent outside the circle, players, downed players and dummies alike. Armor doesn't stop it.</summary>
		private void DamageOutside(float damage)
		{
			Runner.GetAllBehaviours(_agentBuffer);

			float radiusSqr = Radius * Radius;

			for (int i = 0; i < _agentBuffer.Count; ++i)
			{
				Agent agent = _agentBuffer[i];
				if (agent == null || agent.Object == null || agent.Object.IsValid == false || agent.Health.IsAlive == false)
					continue;

				Vector3 offset = agent.transform.position - Center;
				offset.y = 0.0f;

				if (offset.sqrMagnitude < radiusSqr)
					continue;

				var hitData = new HitData()
				{
					Action    = EHitAction.Damage,
					HitType   = EHitType.ShrinkingArea,
					Amount    = damage,
					Position  = agent.transform.position,
					Target    = agent.Health,
					Normal    = Vector3.up,
				};

				HitUtility.ProcessHit(ref hitData);
			}

			_agentBuffer.Clear();
		}

		private readonly System.Collections.Generic.List<Agent> _agentBuffer = new System.Collections.Generic.List<Agent>(64);

		private void Announce()
		{
			if (IsActive == false || IsAnnounced == true)
				return;
			if (HasStateAuthority == false)
				return;
			if (IsFinished == true)
				return;

			IsAnnounced = true;

			_currentStage += 1;
			var stage = _calculatedStages[_currentStage];

			ShrinkRadius = stage.Radius;
			ShrinkCenter = new Vector3(stage.Center.x, transform.position.y, stage.Center.y);

			ShrinkingAnnounced?.Invoke(ShrinkCenter, ShrinkRadius);
		}

		private void Shrink()
		{
			if (IsActive == false || IsShrinking == true)
				return;
			if (HasStateAuthority == false)
				return;
			if (IsFinished == true)
				return;

			_shrinkStartTick = Runner.Tick;
			_shrinkEndTick   = _shrinkStartTick + Mathf.CeilToInt(_shrinkDuration / Runner.DeltaTime);

			IsShrinking    = true;
			_nextShrinking = default;
		}

		private void UpdateShrinking()
		{
			var currentTick = Runner.Tick;

			if (currentTick >= _shrinkEndTick)
			{
				IsAnnounced = false;
				IsShrinking = false;
				Radius     = ShrinkRadius;
				Center     = ShrinkCenter;

				if (_currentStage < _shrinkSteps)
				{
					SetNextShrinkingTimer();
				}
				return;
			}

			float progress = (currentTick - _shrinkStartTick) / (float)(_shrinkEndTick - _shrinkStartTick);

			var previousStage = _calculatedStages[_currentStage - 1];

			Radius = Mathf.Lerp(previousStage.Radius, ShrinkRadius,  progress);
			Center = Vector3.Lerp(new Vector3(previousStage.Center.x, transform.position.y, previousStage.Center.y), ShrinkCenter, progress);
		}

		private void UpdateDamage()
		{
			var radiusSqr = Radius * Radius;

			foreach (var player in Context.NetworkGame.ActivePlayers)
			{
				if (player == null)
					continue;

				var agent = player.ActiveAgent;
				if (agent == null)
					continue;

				var agentPosition = agent.transform.position;
				var direction    = agentPosition - Center;
				direction.y      = 0f;

				if (radiusSqr > direction.sqrMagnitude)
					continue;

				var hitData = new HitData()
				{
					Action    = EHitAction.Damage,
					HitType   = EHitType.ShrinkingArea,
					Amount    = _damagePerTick,
					Position  = agentPosition,
					Target    = agent.Health,
					Normal    = Vector3.up,
				};

				HitUtility.ProcessHit(ref hitData);
			}

			_damageTimer = TickTimer.CreateFromSeconds(Runner, _damageTickTime);
		}

		private void SetNextShrinkingTimer(float additionalTime = 0f)
		{
			int playerCount = Context.NetworkGame.ActivePlayerCount;
			ShrinkDelay = MathUtility.Map(_minShrinkDelayPlayers, _maxShrinkDelayPlayers, _minShrinkDelay, _maxShrinkDelay, playerCount);

			ShrinkDelay += additionalTime;

			_nextShrinking = TickTimer.CreateFromSeconds(Runner, ShrinkDelay);
		}

		private void CalculateStages(Vector2 endCenter)
		{
			_calculatedStages = new AreaStage[_shrinkSteps + 1];

			Vector2 startCenter = new Vector2(transform.position.x, transform.position.z);

			Vector2 center = endCenter;
			float radius = _endRadius;

			// Last stage is fixed
			_calculatedStages[_shrinkSteps] = new AreaStage(center, radius);

			for (int i = _shrinkSteps - 1; i >= 0; i--)
			{
				float nextRadius = Mathf.Lerp(_startRadius, _endRadius, i / (float)_shrinkSteps);

				Vector2 nextCenter = Vector2.zero;
				bool success = false;

				float maxSqrDistanceFromCenter = _mapRadius - nextRadius;
				maxSqrDistanceFromCenter *= maxSqrDistanceFromCenter;

				// Just try it few times instead of complex calculation, lol
				for (int j = 0; j < 50; j++)
				{
					nextCenter = center + Random.insideUnitCircle * (nextRadius - radius);

					if (Vector2.SqrMagnitude(nextCenter - startCenter) < maxSqrDistanceFromCenter)
					{
						success = true;
						break;
					}
				}

				if (success == false)
				{
					var direction = nextCenter - startCenter;
					nextCenter = startCenter + direction.normalized * (_mapRadius - nextRadius);
				}

				center = nextCenter;
				radius = nextRadius;
				_calculatedStages[i] = new AreaStage(center, radius);
			}
		}

		// HELPERS

		private struct AreaStage
		{
			public Vector2 Center;
			public float   Radius;

			public AreaStage(Vector2 center, float radius)
			{
				Center = center;
				Radius = radius;
			}
		}
	}
}
