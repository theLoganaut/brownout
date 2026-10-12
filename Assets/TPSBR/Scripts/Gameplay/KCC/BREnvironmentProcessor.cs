namespace TPSBR
{
	using UnityEngine;
	using Fusion.Addons.KCC;

	// WARNING!
	// This processor is a special variant of EnvironmentProcessor and has optimizations which would normally be considered as KCC anti-pattern,
	// but these optimizations are required to shrink down performance overhead to minimum.
	// 1. All stages (ISetDynamicVelocity, ISetKinematicDirection, ISetKinematicTangent, ISetKinematicSpeed, ISetKinematicVelocity) were collapsed in one stage (IPrepareData).
	// 2. Step-Up and Ground-Snap processors are executed directly from this processor from IAfterMoveStep stage.
	public sealed class BREnvironmentProcessor : KCCProcessor, IPrepareData, IAfterMoveStep
	{
		// CONSTANTS

		public static readonly int DefaultPriority = EnvironmentProcessor.DefaultPriority;

		// PUBLIC MEMBERS

		[Header("General")]
		[Tooltip("Maximum allowed speed the KCC can move with player input.")]
		public float KinematicSpeed = 8.0f;
		[Tooltip("Custom jump multiplier.")]
		public float JumpMultiplier = 1.0f;
		[Tooltip("Custom gravity. Physics.gravity is used if default.")]
		public Vector3 Gravity = default;
		[Tooltip("Relative environment priority. Default environment processor priority is 1000.")]
		public int   RelativePriority = 0;

		[Header("Ground")]
		[Tooltip("Maximum angle of walkable ground.")]
		public float MaxGroundAngle = 60.0f;
		[Tooltip("Dynamic velocity is decelerated by actual dynamic speed multiplied by this. The faster KCC moves, the more deceleration is applied.")]
		public float DynamicGroundFriction = 20.0f;
		[Tooltip("Kinematic velocity is accelerated by calculated kinematic speed multiplied by this.")]
		public float KinematicGroundAcceleration = 50.0f;
		[Tooltip("Kinematic velocity is decelerated by actual kinematic speed multiplied by this. The faster KCC moves, the more deceleration is applied.")]
		public float KinematicGroundFriction = 35.0f;

		[Header("Air")]
		[Tooltip("Dynamic velocity is decelerated by actual dynamic speed multiplied by this. The faster KCC moves, the more deceleration is applied.")]
		public float DynamicAirFriction = 2.0f;
		[Tooltip("Kinematic velocity is accelerated by calculated kinematic speed multiplied by this.")]
		public float KinematicAirAcceleration = 5.0f;
		[Tooltip("Kinematic velocity is decelerated by actual kinematic speed multiplied by this. The faster KCC moves, the more deceleration is applied.")]
		public float KinematicAirFriction = 2.0f;

		// PRIVATE MEMBERS

		[Header("BR200")]
		[SerializeField]
		private float _upGravityMultiplier = 1.0f;
		[SerializeField]
		private float _downGravityMultiplier = 1.0f;

		[SerializeField]
		private StepUpProcessor _stepUpProcessor;
		[SerializeField]
		private GroundSnapProcessor _groundSnapProcessor;

		// KCCProcessor INTERFACE

		public override float GetPriority(KCC kcc) => DefaultPriority + RelativePriority;

		// IPrepareData INTERFACE

		public void Execute(PrepareData stage, KCC kcc, KCCData data)
		{
			data.MaxGroundAngle = MaxGroundAngle;
			data.Gravity        = Gravity != default ? Gravity : Physics.gravity;

			if (kcc.FixedData.MoveMode == EMoveMode.Mantle)
			{
				ExecuteMantle(kcc, data);
				return;
			}

			if (kcc.FixedData.MoveMode == EMoveMode.Grapple)
			{
				ExecuteGrapple(kcc, data);
				return;
			}

			if (AgentMovement.IsDropMode(kcc.FixedData.MoveMode) == true)
			{
				ExecuteDrop(kcc, data);
				return;
			}

			data.Gravity *= kcc.FixedData.RealVelocity.y > 0.0f ? _upGravityMultiplier : _downGravityMultiplier;

			SetDynamicVelocity(kcc, data);

			data.KinematicDirection = data.InputDirection.OnlyXZ();

			SetKinematicVelocity(kcc, data);
		}

		private void SetDynamicVelocity(KCC kcc, KCCData data)
		{
			// This code path can be executed in both fixed/render updates.
			// Next fixed/render value is based on values from last fixed update to not introduce error accumulation due to render delta time integration.

			// ERROR ACCUMULATION EXAMPLE
			// ========================================
			// Input:     x(0)     = 1.0f;
			// Operation: x(n + 1) = x(n) * (1.0f + data.DeltaTime);
			// Delta time for fixed  update (60Hz):  0.016667f
			// Delta time for render update (240Hz): 0.004167f

			// RENDER UPDATE PREDICTION (240Hz, 4 frames)
			// ========================================
			// Result (frame 1):    x(r1) = x(0)  + x(0)  * 0.004167f = 1.0f      * (1.0f + 0.004167f) = 1.004167f
			// Result (frame 2):    x(r2) = x(r1) + x(r1) * 0.004167f = 1.004167f * (1.0f + 0.004167f) = 1.008351f
			// Result (frame 3):    x(r3) = x(r2) + x(r2) * 0.004167f = 1.008351f * (1.0f + 0.004167f) = 1.012552f
			// Result (frame 4):    x(r4) = x(r3) + x(r3) * 0.004167f = 1.012552f * (1.0f + 0.004167f) = 1.016771f

			// FIXED UPDATE PREDICTION (60Hz, 1 tick)
			// ========================================
			// Result (tick 1):     x(f1) = x(0)  + x(0)  * 0.016667f = 1.0f * (1.0f + 0.016667f) = 1.016667f

			// THE PROBLEM
			// ========================================
			// Ideally we want mutiple render updates (total delta time equal to single fixed update) to match the fixed update: x(f1) == x(r4).
			// Example above has a render update error: e(r) = x(f1) - x(r4) = 1.016667f - 1.016771f = -0.000104f
			// This is only a demonstration and the error can result to a perceivable jitter, depends on error amplification.

			// THE SOLUTION
			// ========================================
			// The trick is to make all intermediate calculations with FIXED update delta time (0.016667f).
			// And use RENDER update delta time (0.004167f) only to calculate final position delta.
			// This way 4 render position deltas will match 1 fixed position delta.

			// This is the reason why fixedData is sometimes used instead of data (except values calculated in recently executed processors chain, these are safe to use).

			// We explicitly need data from fixed update.
			KCCData fixedData       = kcc.FixedData;
			float   fixedDeltaTime  = fixedData.DeltaTime;
			Vector3 dynamicVelocity = fixedData.DynamicVelocity;

			if (fixedData.IsGrounded == false || (fixedData.IsSteppingUp == false && (fixedData.IsSnappingToGround == true || fixedData.GroundDistance > 0.001f)))
			{
				// Applying gravity only while in the air (not grounded) and not stepping up, ground snapping can be active.
				dynamicVelocity += data.Gravity * fixedDeltaTime;
			}

			if (data.JumpImpulse.IsZero() == false && JumpMultiplier > 0.0f)
			{
				Vector3 jumpDirection = data.JumpImpulse.normalized;

				// Elimination of dynamic velocity in direction of jump, otherwise the jump trajectory would be distorted.
				dynamicVelocity -= Vector3.Scale(dynamicVelocity, jumpDirection);

				// Applying jump impulse. Brownout: the speed comes from the jump height in MovementSettings, not the impulse size.
				dynamicVelocity += jumpDirection * GetJumpSpeed(kcc);

				// Increase jump counter.
				// For fixed updates this counter will be 1 at max.
				// For render updates this counter can reach higher values, indicating how many times the JumpImpulse was applied on top of fixed data.
				++data.JumpFrames;
			}

			// Apply external forces.
			dynamicVelocity += data.ExternalVelocity;
			dynamicVelocity += data.ExternalAcceleration * fixedDeltaTime;
			dynamicVelocity += data.ExternalImpulse;
			dynamicVelocity += data.ExternalForce * fixedDeltaTime;

			if (dynamicVelocity.IsZero() == false)
			{
				if (dynamicVelocity.IsAlmostZero(0.001f) == true)
				{
					// Clamping values near zero.
					dynamicVelocity = default;
				}
				else
				{
					// Applying ground (XYZ) and air (XZ) friction.
					if (fixedData.IsGrounded == true)
					{
						Vector3 frictionAxis = Vector3.one;
						if (fixedData.GroundDistance > 0.001f || fixedData.IsSnappingToGround == true)
						{
							frictionAxis.y = default;
						}

						dynamicVelocity += KCCPhysicsUtility.GetFriction(dynamicVelocity, dynamicVelocity, frictionAxis, fixedData.GroundNormal, fixedData.KinematicSpeed, true, 0.0f, 0.0f, DynamicGroundFriction, fixedDeltaTime);
					}
					else
					{
						dynamicVelocity += KCCPhysicsUtility.GetFriction(dynamicVelocity, dynamicVelocity, new Vector3(1.0f, 0.0f, 1.0f), fixedData.KinematicSpeed, true, 0.0f, 0.0f, DynamicAirFriction, fixedDeltaTime);
					}
				}
			}

			data.DynamicVelocity = dynamicVelocity;

			if (kcc.IsInFixedUpdate == true)
			{
				// Consume one-time effects only in fixed update.
				// For render prediction we need them to be applied on top of fixed data in all frames.
				data.JumpImpulse      = default;
				data.ExternalVelocity = default;
				data.ExternalImpulse  = default;
			}

			// Forces applied over-time are reset always. These are set every tick/frame.
			data.ExternalAcceleration = default;
			data.ExternalForce        = default;
		}

		private void SetKinematicVelocity(KCC kcc, KCCData data)
		{
			// SetKinematicTangent

			KCCData fixedData         = kcc.FixedData;
			float   fixedDeltaTime    = fixedData.DeltaTime;
			Vector3 kinematicVelocity = fixedData.KinematicVelocity;

			if (fixedData.IsGrounded == true)
			{
				// The character is grounded.

				if (data.KinematicDirection.IsAlmostZero(0.0001f) == false && KCCPhysicsUtility.ProjectOnGround(fixedData.GroundNormal, data.KinematicDirection, out Vector3 projectedMoveDirection) == true)
				{
					// Use projected kinematic direction on ground when possible.
					data.KinematicTangent = projectedMoveDirection.normalized;
				}
				else
				{
					// Otherwise use ground tangent => steepest descent.
					data.KinematicTangent = fixedData.GroundTangent;
				}
			}
			else
			{
				// The character is floating in the air.

				if (data.KinematicDirection.IsAlmostZero(0.0001f) == false)
				{
					// Use kinematic direction directly.
					data.KinematicTangent = data.KinematicDirection.normalized;
				}
				else
				{
					// No direction set, use character forward.
					data.KinematicTangent = data.TransformDirection;
				}
			}

			// SetKinematicSpeed

			// Brownout: speeds come from the movement mode and MovementSettings, not the animation blend tree
			if (SetModeVelocity(kcc, data) == true)
				return;

			data.KinematicSpeed = GetModeSpeed(fixedData, kinematicVelocity.OnlyXZ().magnitude, fixedDeltaTime, GetAimSpeedMultiplier(kcc), GetGearSpeedMultiplier(kcc) * GetConcussionSlow(kcc));

			// SetKinematicVelocity

			// This code path can be executed in both fixed/render updates.
			// Next fixed/render value is based on values from last fixed update to not introduce error accumulation due to render delta time integration.
			// Details are described above in SetDynamicVelocity().

			if (fixedData.IsGrounded == true)
			{
				// The character is grounded.

				if (kinematicVelocity.IsAlmostZero() == false && KCCPhysicsUtility.ProjectOnGround(fixedData.GroundNormal, kinematicVelocity, out Vector3 projectedKinematicVelocity) == true)
				{
					// Project current velocity on ground.
					kinematicVelocity = projectedKinematicVelocity.normalized * kinematicVelocity.magnitude;
				}

				if (data.KinematicDirection.IsAlmostZero() == true)
				{
					// No kinematic direction
					// Just apply friction (XYZ) and exit early.
					data.KinematicVelocity = kinematicVelocity + KCCPhysicsUtility.GetFriction(kinematicVelocity, kinematicVelocity, Vector3.one, fixedData.GroundNormal, data.KinematicSpeed, true, 0.0f, 0.0f, KinematicGroundFriction, fixedDeltaTime);
					return;
				}
			}
			else
			{
				// The character is floating in the air.

				if (data.KinematicDirection.IsAlmostZero() == true)
				{
					// No kinematic direction
					// Just apply friction (XZ) and exit early.
					data.KinematicVelocity = kinematicVelocity + KCCPhysicsUtility.GetFriction(kinematicVelocity, kinematicVelocity, new Vector3(1.0f, 0.0f, 1.0f), data.KinematicSpeed, true, 0.0f, 0.0f, KinematicAirFriction, fixedDeltaTime);
					return;
				}
			}

			// Following section calculates ground/air acceleration and friction relatively to move direction and kinematic tangent.
			// And combines them together with base kinematic velocity.

			Vector3 moveDirection = kinematicVelocity;
			if (moveDirection.IsZero() == true)
			{
				moveDirection = data.KinematicTangent;
			}

			Vector3 acceleration;
			Vector3 friction;

			if (fixedData.IsGrounded == true)
			{
				acceleration = KCCPhysicsUtility.GetAcceleration(kinematicVelocity, data.KinematicTangent, Vector3.one, data.KinematicSpeed, false, data.KinematicDirection.magnitude, 0.0f, KinematicGroundAcceleration, 0.0f, fixedDeltaTime);
				friction     = KCCPhysicsUtility.GetFriction(kinematicVelocity, moveDirection, Vector3.one, fixedData.GroundNormal, data.KinematicSpeed, false, 0.0f, 0.0f, KinematicGroundFriction, fixedDeltaTime);
			}
			else
			{
				acceleration = KCCPhysicsUtility.GetAcceleration(kinematicVelocity, data.KinematicTangent, Vector3.one, data.KinematicSpeed, false, data.KinematicDirection.magnitude, 0.0f, KinematicAirAcceleration, 0.0f, fixedDeltaTime);
				friction     = KCCPhysicsUtility.GetFriction(kinematicVelocity, moveDirection, new Vector3(1.0f, 0.0f, 1.0f), data.KinematicSpeed, false, 0.0f, 0.0f, KinematicAirFriction, fixedDeltaTime);
			}

			kinematicVelocity = KCCPhysicsUtility.CombineAccelerationAndFriction(kinematicVelocity, acceleration, friction);

			// Clamp velocity.
			if (kinematicVelocity.sqrMagnitude > data.KinematicSpeed * data.KinematicSpeed)
			{
				kinematicVelocity = kinematicVelocity / Vector3.Magnitude(kinematicVelocity) * data.KinematicSpeed;
			}

			// Reset Y axis to get stable jump height results even if moving downwards.
			if (data.JumpFrames > 0 && kinematicVelocity.y < 0.0f)
			{
				kinematicVelocity.y = 0.0f;
			}

			data.KinematicVelocity = kinematicVelocity;
		}

		/// <summary>
		/// Slide, dash and skip-air set the velocity directly (exact speeds from the data); mantle stands still here and moves in ExecuteMantle.
		/// </summary>
		private bool SetModeVelocity(KCC kcc, KCCData data)
		{
			MovementSettings settings  = MovementSettings.Current;
			KCCData          fixedData = kcc.FixedData;

			switch (fixedData.MoveMode)
			{
				case EMoveMode.Slide:
				case EMoveMode.Dash:
				{
					// Double Time leaves slides and dashes alone; a concussion slows them too
					float   speed     = (fixedData.MoveMode == EMoveMode.Slide ? AgentMovement.GetSlideSpeed(fixedData, settings) : settings.DashSpeed) * GetConcussionSlow(kcc);
					Vector3 direction = fixedData.MoveDirectionLock;

					if (fixedData.IsGrounded == false)
					{
						// Left the ground this tick: keep the horizontal momentum
						data.KinematicVelocity = fixedData.KinematicVelocity.OnlyXZ();
						data.KinematicSpeed    = data.KinematicVelocity.magnitude;
						return true;
					}

					if (KCCPhysicsUtility.ProjectOnGround(fixedData.GroundNormal, direction, out Vector3 groundDirection) == true)
					{
						// Same horizontal speed on slopes, so the readout matches the table
						Vector3 flat = groundDirection.OnlyXZ();
						direction = flat.sqrMagnitude > 0.0001f ? groundDirection / flat.magnitude : direction;
					}

					data.KinematicSpeed    = speed;
					data.KinematicVelocity = direction * speed;
					return true;
				}
				case EMoveMode.SkipAir:
				{
					// Jumping keeps whatever momentum you have
					data.KinematicVelocity = fixedData.KinematicVelocity.OnlyXZ();
					data.KinematicSpeed    = data.KinematicVelocity.magnitude;
					return true;
				}
				case EMoveMode.LandRoll:
				{
					// Phase 10: the landing roll carries you on along the landing direction, slowing to a stop by the end
					MatchSettings match   = MatchSettings.Current;
					float         elapsed = (fixedData.Tick - fixedData.ModeStartTick) * fixedData.DeltaTime;
					float         speed   = fixedData.DropVelocity.magnitude * Mathf.Clamp01(1.0f - elapsed / Mathf.Max(0.1f, match.LandRollTime));
					Vector3       direction = fixedData.MoveDirectionLock;

					if (fixedData.IsGrounded == true && KCCPhysicsUtility.ProjectOnGround(fixedData.GroundNormal, direction, out Vector3 groundDirection) == true)
					{
						direction = groundDirection.normalized;
					}

					data.KinematicSpeed    = speed;
					data.KinematicVelocity = direction * speed;
					return true;
				}
			}

			return false;
		}

		/// <summary>Stock (phase 3c): faster while aiming, from the gun in hand.</summary>
		private static float GetAimSpeedMultiplier(KCC kcc)
		{
			if (kcc.FixedData.Aim == false || kcc.TryGetComponent(out Weapons weapons) == false)
				return 1.0f;

			return weapons.CurrentWeapon is BulletWeapon bulletWeapon && bulletWeapon.GunData != null ? bulletWeapon.Stats.AimMoveSpeed : 1.0f;
		}

		/// <summary>Double Time (phase 5b): walk, sprint, crouch, aim and crawl faster while it's on.</summary>
		private static float GetGearSpeedMultiplier(KCC kcc)
		{
			if (kcc.TryGetComponent(out Agent agent) == false || agent.Gear == null || agent.Object == null)
				return 1.0f;

			return agent.Gear.IsActive(EGear.DoubleTime) == true ? EquipmentSettings.Current.DoubleTimeMultiplier : 1.0f;
		}

		/// <summary>Concussion (phase 5b): slowed for a while.</summary>
		private static float GetConcussionSlow(KCC kcc)
		{
			if (kcc.TryGetComponent(out AgentSenses senses) == false || senses.Object == null)
				return 1.0f;

			return senses.GetConcussionSlow();
		}

		private static float GetModeSpeed(KCCData fixedData, float currentSpeed, float deltaTime, float aimSpeedMultiplier, float modeMultiplier)
		{
			MovementSettings settings = MovementSettings.Current;

			float speed;
			switch (fixedData.MoveMode)
			{
				case EMoveMode.Sprint: speed = settings.SprintSpeed; break;
				case EMoveMode.Crouch: speed = settings.CrouchSpeed; break;
				case EMoveMode.Downed: speed = settings.CrouchSpeed; break; // Crawl at crouch speed (Logan, 4 Oct)
				default:               speed = settings.WalkSpeed;   break;
			}

			if (fixedData.Stance != EStance.Stand)
			{
				speed = Mathf.Min(speed, settings.CrouchSpeed);
			}

			if (fixedData.Aim == true)
			{
				speed = Mathf.Min(speed, settings.AimSpeed * aimSpeedMultiplier);
			}

			speed *= modeMultiplier;

			// Coming out of a slide chain or a dash faster than the new cap: bleed the extra off instead of snapping
			if (currentSpeed > speed)
			{
				speed = Mathf.Max(speed, currentSpeed - settings.OverspeedDeceleration * deltaTime);
			}

			return speed;
		}

		private float GetJumpSpeed(KCC kcc)
		{
			// Peak of the fixed-step arc: v^2 / 2g + v * dt / 2 = h (gravity starts on the tick after the jump)
			float gravity   = (Gravity != default ? Gravity : Physics.gravity).magnitude * _upGravityMultiplier;
			float deltaTime = kcc.FixedData.DeltaTime > 0.0f ? kcc.FixedData.DeltaTime : kcc.Runner.DeltaTime;
			float height    = MovementSettings.Current.JumpHeight;

			return gravity * (-deltaTime * 0.5f + Mathf.Sqrt(deltaTime * deltaTime * 0.25f + 2.0f * height / gravity));
		}

		private static void ExecuteMantle(KCC kcc, KCCData data)
		{
			KCCData fixedData = kcc.FixedData;

			// Fixed: time at this tick. Render: extended by the render alpha, so the climb is smooth between ticks
			float   alpha   = kcc.IsInFixedUpdate == true ? 0.0f : data.Alpha;
			float   elapsed = (data.Tick - fixedData.ModeStartTick + alpha) * kcc.Runner.DeltaTime;
			Vector3 target  = AgentMovement.GetMantlePosition(fixedData, elapsed);

			data.Gravity            = Vector3.zero;
			data.DynamicVelocity    = Vector3.zero;
			data.KinematicVelocity  = Vector3.zero;
			data.KinematicDirection = Vector3.zero;
			data.KinematicSpeed     = 0.0f;
			data.ExternalDelta      = target - data.BasePosition;

			if (kcc.IsInFixedUpdate == true)
			{
				data.JumpImpulse      = default;
				data.ExternalVelocity = default;
				data.ExternalImpulse  = default;
			}

			data.ExternalAcceleration = default;
			data.ExternalForce        = default;
		}

		/// <summary>
		/// Phase 5b: pulled straight at the grapple spot at GrappleSpeed (never past it), no gravity. Walls still stop the capsule, which
		/// AgentMovement treats as the end of the pull.
		/// </summary>
		private static void ExecuteGrapple(KCC kcc, KCCData data)
		{
			KCCData fixedData = kcc.FixedData;

			Vector3 toTarget = fixedData.GrappleTarget - data.BasePosition;
			float   distance = toTarget.magnitude;
			float   maxSpeed = fixedData.DeltaTime > 0.0f ? distance / fixedData.DeltaTime : fixedData.GrappleSpeed;

			data.Gravity            = Vector3.zero;
			data.KinematicVelocity  = Vector3.zero;
			data.KinematicDirection = Vector3.zero;
			data.KinematicSpeed     = 0.0f;
			data.DynamicVelocity    = distance > 0.001f ? toTarget / distance * Mathf.Min(fixedData.GrappleSpeed, maxSpeed) : Vector3.zero;

			if (kcc.IsInFixedUpdate == true)
			{
				data.JumpImpulse      = default;
				data.ExternalVelocity = default;
				data.ExternalImpulse  = default;
			}

			data.ExternalAcceleration = default;
			data.ExternalForce        = default;
		}

		/// <summary>
		/// Phase 7: the wingsuit and the fall after the wings pop off move at KCCData.DropVelocity (set by AgentMovement each tick; it adds the
		/// fall's gravity itself), no KCC gravity.
		/// </summary>
		private static void ExecuteDrop(KCC kcc, KCCData data)
		{
			KCCData fixedData = kcc.FixedData;

			data.Gravity            = Vector3.zero;
			data.KinematicVelocity  = Vector3.zero;
			data.KinematicDirection = Vector3.zero;
			data.KinematicSpeed     = 0.0f;
			data.DynamicVelocity    = fixedData.DropVelocity;

			if (kcc.IsInFixedUpdate == true)
			{
				data.JumpImpulse      = default;
				data.ExternalVelocity = default;
				data.ExternalImpulse  = default;
			}

			data.ExternalAcceleration = default;
			data.ExternalForce        = default;
		}

		// IAfterMoveStep INTERFACE

		public void Execute(AfterMoveStep stage, KCC kcc, KCCData data)
		{
			// This code path can be executed multiple times in single update if CCD is active (Continuous Collision Detection).

			KCCData fixedData = kcc.FixedData;

			if (data.IsGrounded == true)
			{
				if (fixedData.WasGrounded == true && data.IsSnappingToGround == false && data.DynamicVelocity.y < 0.0f && data.DynamicVelocity.OnlyXZ().IsAlmostZero() == true)
				{
					// Reset dynamic velocity Y axis while grounded (to not accumulate gravity indefinitely and clamp to precise zero).
					data.DynamicVelocity.y = 0.0f;
				}

				if (fixedData.WasGrounded == false)
				{
					if (data.KinematicVelocity.OnlyXZ().IsAlmostZero() == true)
					{
						// Reset Y axis after getting grounded and there is no horizontal movement.
						data.KinematicVelocity.y = 0.0f;
					}
					else
					{
						// Otherwise try projecting kinematic velocity onto ground.
						if (KCCPhysicsUtility.ProjectOnGround(data.GroundNormal, data.KinematicVelocity, out Vector3 projectedKinematicVelocity) == true)
						{
							data.KinematicVelocity = projectedKinematicVelocity.normalized * data.KinematicVelocity.magnitude;
						}
					}
				}
			}
			else
			{
				if (fixedData.WasGrounded == false && data.DynamicVelocity.y > 0.0f && data.DeltaTime > 0.0f)
				{
					Vector3 currentVelocity;

					if (kcc.IsInFixedUpdate == true)
					{
						currentVelocity = (data.TargetPosition - data.BasePosition) / data.DeltaTime;
					}
					else
					{
						currentVelocity = (data.TargetPosition - fixedData.TargetPosition) / (fixedData.DeltaTime * data.Alpha);
					}

					if (currentVelocity.y.IsAlmostZero() == true)
					{
						// Clamping dynamic up velocity if there is no real position change => hitting a roof.
						data.DynamicVelocity.y = 0.0f;
					}
				}
			}

			if (kcc.FixedData.MoveMode == EMoveMode.Mantle || kcc.FixedData.MoveMode == EMoveMode.Grapple || AgentMovement.IsDropMode(kcc.FixedData.MoveMode) == true)
				return;

			_stepUpProcessor.Execute(stage, kcc, data);
			_groundSnapProcessor.Execute(stage, kcc, data);
		}
	}
}
