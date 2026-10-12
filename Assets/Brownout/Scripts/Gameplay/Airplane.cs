using UnityEngine;
using Fusion;
using Fusion.Addons.KCC;

namespace TPSBR
{
	public sealed class Airplane : ContextBehaviour
	{
		// PUBLIC MEMBERS

		[Networked, HideInInspector]
		public Vector3     NextTargetPosition   { get; set; }

		public bool        DropEnabled          { get { return _state.IsBitSet(0); } set { _state = _state.SetBitNoRef(0, value); } }
		public bool        DropFinished         { get { return _state.IsBitSet(1); } set { _state = _state.SetBitNoRef(1, value); } }
		public bool        IsFlyingAway         { get { return _state.IsBitSet(2); } set { _state = _state.SetBitNoRef(2, value); } }
		public bool        IsFinished           { get { return _state.IsBitSet(3); } set { _state = _state.SetBitNoRef(3, value); } }

		public Transform   AgentPosition        => _agentPosition;
		public float       OutZoneDistance      => _outZoneDistance;

		/// <summary>Phase 7: flying a straight line from PathStart to PathEnd at PathSpeed (the Brownout helicopter), not the sample's steering.</summary>
		public bool        IsStraight           { get { return _state.IsBitSet(4); } set { _state = _state.SetBitNoRef(4, value); } }
		[Networked, HideInInspector]
		public Vector3     PathStart            { get; private set; }
		[Networked, HideInInspector]
		public Vector3     PathEnd              { get; private set; }
		[Networked, HideInInspector]
		public float       PathSpeed            { get; private set; }
		[Networked, HideInInspector]
		private int        _pathStartTick       { get; set; }

		/// <summary>Phase 7: share of the straight path flown, 0..1.</summary>
		public float PathProgress
		{
			get
			{
				float length = Vector3.Distance(PathStart, PathEnd);
				return length > 0.01f ? Mathf.Clamp01(Vector3.Distance(PathStart, transform.position) / length) : 1.0f;
			}
		}

		// PRIVATE MEMBERS

		[SerializeField]
		private GameObject _dropEnabledObject;
		[SerializeField]
		private GameObject _dropDisabledObject;
		[SerializeField]
		private Transform _agentPosition;
		[SerializeField]
		private float _maxSpeed = 8f;
		[SerializeField]
		private float _maxAcceleration = 8f;
		[SerializeField]
		private float _tiltSpeed = 8f;
		[SerializeField]
		private float _maxTilt = 60;
		[SerializeField]
		private float _minDistanceToTargetChange = 60f;
		[SerializeField]
		private float _outZoneDistance = 20f;
		[SerializeField]
		private float _inZoneDistance = 10f;

		[Header("Sounds")]
		[SerializeField]
		private AudioEffect _audioEffect;
		[SerializeField]
		private AudioSetup _playerJumpedSound;

		[Networked]
		private byte _state { get; set; }
		[Networked]
		private Vector3 _velocity { get; set; }
		[Networked]
		private float _tilt { get; set; }

		// PUBLIC METHODS

		public void ActivateDropWindow()
		{
			if (HasStateAuthority == false)
				return;

			DropEnabled = true;
			NextTargetPosition = Vector3.zero;
		}

		public void DeactivateDropWindow()
		{
			if (HasStateAuthority == false)
				return;

			DropEnabled = false;
			DropFinished = true;
			NextTargetPosition = Vector3.zero;
		}

		public void OnPlayerJumped(PlayerRef playerRef)
		{
			_audioEffect.Play(_playerJumpedSound);
		}

		/// <summary>Phase 7 (server, right after spawning): fly straight from start to end at speed, then IsFinished.</summary>
		public void SetStraightPath(Vector3 start, Vector3 end, float speed)
		{
			if (HasStateAuthority == false)
				return;

			IsStraight     = true;
			PathStart      = start;
			PathEnd        = end;
			PathSpeed      = speed;
			_pathStartTick = Runner.Tick;
			_velocity      = (end - start).normalized * speed;

			transform.SetPositionAndRotation(start, Quaternion.LookRotation((end - start).OnlyXZ(), Vector3.up));
		}

		/// <summary>Phase 7: the helicopter's velocity (what a jumper leaves with).</summary>
		public Vector3 Velocity => _velocity;

		// NetworkBehaviour INTERFACE

		public override void Spawned()
		{
			// Phase 10: Logan's Osprey in place of the sample's plane, on every peer
			HelicopterVisuals visuals = GetComponent<HelicopterVisuals>();
			if (visuals == null)
			{
				visuals = gameObject.AddComponent<HelicopterVisuals>();
			}
			visuals.Build(Runner);

			if (HasStateAuthority == false)
				return;

			_velocity = transform.rotation * (Vector3.forward * _maxSpeed);
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false)
				return;

			if (IsFinished == true)
				return;

			if (IsStraight == true)
			{
				float   length    = Vector3.Distance(PathStart, PathEnd);
				float   travelled = (Runner.Tick - _pathStartTick) * Runner.DeltaTime * PathSpeed;
				Vector3 direction = length > 0.01f ? (PathEnd - PathStart) / length : Vector3.forward;

				transform.position = PathStart + direction * Mathf.Min(travelled, length);

				if (travelled >= length)
				{
					IsFinished = true;
				}
				return;
			}

			if (NeedsNewTarget() == true)
			{
				if (IsFlyingAway == true)
				{
					// Plane is near fly away target, let's despawn
					IsFinished = true;
					return;
				}
				else if (DropEnabled == true)
				{
					NextTargetPosition = FindNewTargetInZone();
				}
				else if (DropFinished == false)
				{
					// Waiting for drop start
					NextTargetPosition = FindNewTargetOutsideZone();
				}
				else
				{
					NextTargetPosition = FindFlyAwayTarget();
					IsFlyingAway = true;
				}
			}

			UpdatePosition();
		}

		public override void Render()
		{
			// Phase 10: the sample's drop markers belong to its plane; with the Osprey they stay off
			if (GetComponent<HelicopterVisuals>() != null && ArtSettings.Current != null && ArtSettings.Current.HelicopterModel != null)
				return;

			_dropEnabledObject.SetActive(DropEnabled);
			_dropDisabledObject.SetActive(DropEnabled == false);
		}

		// PRIVATE METHODS

		private bool NeedsNewTarget()
		{
			if (NextTargetPosition == Vector3.zero)
				return true;

			return Vector3.SqrMagnitude(transform.position - NextTargetPosition) < _minDistanceToTargetChange * _minDistanceToTargetChange;
		}

		private Vector3 FindNewTargetInZone()
		{
			var area = Context.GameplayMode.ShrinkingArea;

			var dirToPlane = (transform.position - area.Center).OnlyXZ().normalized;
			var nextDirection = -dirToPlane + _velocity.OnlyXZ().normalized * 0.3f;

			if (nextDirection == Vector3.zero)
			{
				nextDirection = -dirToPlane;
				nextDirection.x += 5;
			}

			var areaPoint = area.Center + nextDirection.normalized * (area.Radius - _inZoneDistance);
			areaPoint.y = transform.position.y;

			return areaPoint;
		}

		private Vector3 FindNewTargetOutsideZone()
		{
			var area = Context.GameplayMode.ShrinkingArea;
			var dirOutOfArea = (transform.position + transform.forward * 60f - area.Center).OnlyXZ().normalized;

			var areaPoint = area.Center + dirOutOfArea * (area.Radius + _outZoneDistance);
			areaPoint.y = transform.position.y;

			return areaPoint;
		}

		private Vector3 FindFlyAwayTarget()
		{
			var area = Context.GameplayMode.ShrinkingArea;
			var dirOutOfArea = (transform.position + transform.forward * 60f - area.Center).OnlyXZ().normalized;

			var areaPoint = area.Center + dirOutOfArea * (area.Radius + 300f);
			areaPoint.y = transform.position.y + 50f;

			return areaPoint;
		}

		private void UpdatePosition()
		{
			float deltaTime = Runner.DeltaTime;

			transform.position = transform.position + _velocity * deltaTime;
			var directionToTarget = NextTargetPosition - transform.position;

			var lastLeft = -transform.right;
			lastLeft.y = 0f;
			float directionDot = Vector3.Dot(directionToTarget.normalized, lastLeft.normalized);
			float targetTilt = MathUtility.Map(-1f, 1f, -_maxTilt, _maxTilt, directionDot);

			_tilt = Mathf.Lerp(_tilt, targetTilt, _tiltSpeed * deltaTime);

			var newRotation = Quaternion.LookRotation(_velocity, Vector3.up).eulerAngles;
			newRotation.z = _tilt;
			transform.rotation = Quaternion.Euler(newRotation);

			_velocity += directionToTarget.normalized * _maxAcceleration * deltaTime;

			float speed = _velocity.magnitude;
			if (speed > _maxSpeed)
			{
				_velocity = (_velocity / speed) * _maxSpeed;
			}
		}
	}
}
