namespace TPSBR
{
	using UnityEngine;
	using Fusion.Addons.KCC;
	using Fusion.Addons.AnimationController;

	public sealed class MoveState : MultiBlendTreeState
	{
		// PUBLIC MEMBERS

		public Vector3 FixedDirection        => _fixedDirection;
		public float   FixedMagnitude        => _fixedMagnitude;
		public Vector3 InterpolatedDirection => _interpolatedDirection;
		public float   InterpolatedMagnitude => _interpolatedMagnitude;

		// PRIVATE MEMBERS

		private const int CROUCH_SET = 3;

		[SerializeField]
		private float   _minAnimationSpeed       = 0.25f;
		[SerializeField]
		private float   _maxAnimationSpeed       = 2.0f;
		[SerializeField]
		private float   _directionSmoothingSpeed = 16.0f;
		[SerializeField]
		private float   _magnitudeSmoothingSpeed = 16.0f;

		private KCC     _kcc;
		private Weapons _weapons;
		private Vector3 _fixedDirection;
		private float   _fixedMagnitude;
		private Vector3 _interpolatedDirection;
		private float   _interpolatedMagnitude;

		// PUBLIC METHODS

		public float GetBaseSpeed(Vector2 localNormalizedDirection, float multiplier)
		{
			if (multiplier == default)
			{
				multiplier = GetMultiplier();
			}

			return GetMaxBaseSpeed(localNormalizedDirection) * multiplier;
		}

		// MultiBlendTreeState INTERFACE

		public override Vector2 GetBlendPosition(bool interpolated)
		{
			Vector3 direction = interpolated == true ? _interpolatedDirection : _fixedDirection;
			float   magnitude = interpolated == true ? _interpolatedMagnitude : _fixedMagnitude;

			return _kcc.transform.InverseTransformDirection(direction).XZ0().normalized * magnitude;
		}

		public override float GetSpeedMultiplier()
		{
			KCCData kccData = _kcc.Object.IsInSimulation == true ? _kcc.FixedData : _kcc.Data;

			// Aimed in and moving: the walk and strafe clips play faster (MovementSettings, animation only)
			float aimMultiplier = kccData.Aim == true && kccData.RealSpeed > 0.2f ? Mathf.Max(0.01f, MovementSettings.Current.AimMoveAnimationSpeed) : 1.0f;

			float maxBaseSpeed = GetMaxBaseSpeed((Quaternion.Inverse(kccData.TransformRotation) * kccData.KinematicDirection).XZ0().normalized);
			if (maxBaseSpeed > 0.0f && kccData.RealSpeed > maxBaseSpeed)
					return Mathf.Clamp(kccData.RealSpeed / maxBaseSpeed, _minAnimationSpeed, _maxAnimationSpeed) * aimMultiplier;

			return aimMultiplier;
		}

		public override int GetSetID()
		{
			// Phase 10: Logan's crouch clips are the fourth set, for any weapon
			if (Sets.Length > CROUCH_SET && _kcc.FixedData.Stance == EStance.Crouch)
				return CROUCH_SET;

			int currentWeaponSlot = _weapons.CurrentWeaponSlot;
			if (currentWeaponSlot > 2)
			{
				currentWeaponSlot = 1; // For grenades we use pistol set
			}

			if (currentWeaponSlot < 0)
				return 0;

			return currentWeaponSlot;
		}

		// AnimationState INTERFACE

		protected override void OnInitialize()
		{
			base.OnInitialize();

			_kcc     = Controller.GetComponentNoAlloc<KCC>();
			_weapons = Controller.GetComponentNoAlloc<Weapons>();
		}

		protected override void OnSpawned()
		{
			base.OnSpawned();

			_fixedDirection        = Vector3.forward;
			_fixedMagnitude        = 0.0f;
			_interpolatedDirection = Vector3.forward;
			_interpolatedMagnitude = 0.0f;
		}

		protected override void OnFixedUpdate()
		{
			SetFixedProperties();

			base.OnFixedUpdate();
		}

		protected override void OnInterpolate()
		{
			SetInterpolatedProperties();

			base.OnInterpolate();
		}

		// PRIVATE METHODS

		private void SetFixedProperties()
		{
			KCCData kccFixedData    = _kcc.FixedData;
			Vector3 targetDirection = _fixedDirection;
			float   targetMagnitude;

			if (Controller.HasInputAuthority == true || Controller.HasStateAuthority == true)
			{
				if (kccFixedData.InputDirection.OnlyXZ().IsAlmostZero(0.025f) == false)
				{
					targetDirection = kccFixedData.InputDirection.OnlyXZ().normalized;
				}
				else if (kccFixedData.KinematicDirection.OnlyXZ().IsAlmostZero(0.025f) == false)
				{
					targetDirection = kccFixedData.KinematicDirection.OnlyXZ().normalized;
				}
				else if (kccFixedData.DesiredVelocity.OnlyXZ().IsAlmostZero(0.025f) == false)
				{
					targetDirection = kccFixedData.DesiredVelocity.OnlyXZ().normalized;
				}

				float realVelocityMagnitude      = kccFixedData.RealVelocity.OnlyXZ().magnitude;
				float desiredVelocityMagnitude   = kccFixedData.DesiredVelocity.OnlyXZ().magnitude;
				float kinematicVelocityMagnitude = kccFixedData.KinematicVelocity.OnlyXZ().magnitude;

				targetMagnitude = Mathf.Min(realVelocityMagnitude, Mathf.Max(kinematicVelocityMagnitude, desiredVelocityMagnitude));
			}
			else
			{
				if (kccFixedData.RealVelocity.OnlyXZ().IsAlmostZero(0.025f) == false)
				{
					targetDirection = kccFixedData.RealVelocity.OnlyXZ().normalized;
				}

				targetMagnitude = kccFixedData.RealSpeed;
			}

			_fixedDirection = targetDirection;
			_fixedMagnitude = targetMagnitude;
		}

		private void SetInterpolatedProperties()
		{
			KCCData kccFixedData    = _kcc.FixedData;
			KCCData kccRenderData   = _kcc.RenderData;
			Vector3 targetDirection = _interpolatedDirection;
			float   targetMagnitude;

			if (Controller.HasInputAuthority == true || Controller.HasStateAuthority == true)
			{
				if (kccRenderData.InputDirection.OnlyXZ().IsAlmostZero(0.025f) == false)
				{
					targetDirection = kccRenderData.InputDirection.OnlyXZ().normalized;
				}
				else if (kccRenderData.KinematicDirection.OnlyXZ().IsAlmostZero(0.025f) == false)
				{
					targetDirection = kccRenderData.KinematicDirection.OnlyXZ().normalized;
				}
				else if (kccFixedData.DesiredVelocity.OnlyXZ().IsAlmostZero(0.025f) == false)
				{
					targetDirection = kccFixedData.DesiredVelocity.OnlyXZ().normalized;
				}

				float directionDot = Vector3.Dot(targetDirection, _interpolatedDirection);
				if (directionDot < -0.5f)
				{
					const float angleTolerance = 25.0f;

					float angle = Vector3.SignedAngle(_kcc.transform.forward, _interpolatedDirection, Vector3.up);
					if (angle.AlmostEquals(90.0f, angleTolerance) == true)
					{
						targetDirection = Quaternion.Euler(0.0f, -90.0f, 0.0f) * _interpolatedDirection;
					}
					else if (angle.AlmostEquals(-90.0f, angleTolerance) == true)
					{
						targetDirection = Quaternion.Euler(0.0f, 90.0f, 0.0f) * _interpolatedDirection;
					}
					else if (angle.AlmostEquals(0.0f, angleTolerance) == true)
					{
						targetDirection = Quaternion.Euler(0.0f, -90.0f, 0.0f) * _interpolatedDirection;
					}
					else if (Mathf.Abs(angle).AlmostEquals(180.0f, angleTolerance) == true)
					{
						targetDirection = Quaternion.Euler(0.0f, 90.0f, 0.0f) * _interpolatedDirection;
					}
				}

				float realVelocityMagnitude      = kccFixedData.RealVelocity.OnlyXZ().magnitude;
				float desiredVelocityMagnitude   = kccFixedData.DesiredVelocity.OnlyXZ().magnitude;
				float kinematicVelocityMagnitude = kccFixedData.KinematicVelocity.OnlyXZ().magnitude;

				targetMagnitude = Mathf.Min(realVelocityMagnitude, Mathf.Max(kinematicVelocityMagnitude, desiredVelocityMagnitude));
			}
			else
			{
				if (kccRenderData.RealVelocity.OnlyXZ().IsAlmostZero(0.025f) == false)
				{
					targetDirection = kccRenderData.RealVelocity.OnlyXZ().normalized;
					targetMagnitude = kccRenderData.RealSpeed;
				}
				else
				{
					targetMagnitude = 0.0f;
				}
			}

			_interpolatedDirection = Vector3.Slerp(_interpolatedDirection, targetDirection, _directionSmoothingSpeed * Time.deltaTime).normalized;
			_interpolatedMagnitude = Mathf.Lerp(_interpolatedMagnitude, targetMagnitude, _magnitudeSmoothingSpeed * Time.deltaTime);
		}

		/// <summary>
		/// The speed the set's clips are made for in this direction: the fastest node within 45° of it, projected onto it. Phase 10 sets
		/// have a walk ring and a sprint ring (Logan's takes), so this is the sprint speed forward and the walk speed backward.
		/// </summary>
		private float GetMaxBaseSpeed(Vector2 localNormalizedDirection)
		{
			if (localNormalizedDirection == Vector2.zero)
				return 0.0f;

			int setID = GetSetID();
			if (setID < 0)
				return 0.0f;

			BlendTreeNode[] nodes = Sets[setID].Nodes;
			float maxSpeed = 0.0f;

			for (int i = 0; i < nodes.Length; ++i)
			{
				Vector2 position  = nodes[i].Position;
				float   magnitude = position.magnitude;
				if (magnitude < 0.01f)
					continue;

				float cosine = Vector2.Dot(position / magnitude, localNormalizedDirection);
				if (cosine < 0.7071f)
					continue;

				maxSpeed = Mathf.Max(maxSpeed, magnitude * cosine);
			}

			return maxSpeed;
		}

		private float GetMultiplier()
		{
			switch (_weapons.CurrentWeaponSlot)
			{
				case 0: { return 1.0f;  }
				case 1: { return 0.95f; }
				case 2: { return 0.9f;  }
			}

			return 0.95f;
		}
	}
}
