namespace TPSBR
{
	using UnityEngine;
	using Fusion.Addons.KCC;

	public enum EMoveMode
	{
		Walk    = 0,
		Sprint  = 1,
		Crouch  = 2,
		Slide   = 3,
		Dash    = 4,
		Mantle  = 5,
		SkipAir = 6, // Airborne inside a skip chain: momentum is kept, C queues the air slide
		Downed  = 7, // Phase 4: crawling at crouch speed until revived or dead
		Grapple   = 8,  // Phase 5b: pulled along the grapple's rope to KCCData.GrappleTarget
		Wingsuit  = 9,  // Phase 7: out of the helicopter, gliding or diving at KCCData.DropVelocity
		WingsOff  = 10, // Phase 10 (Logan, 9 Oct, no parachute): the wings popped off MatchSettings.WingsOffHeight above the ground, falling
		LandRoll  = 11, // Phase 10: on the ground after the drop, rolling (MatchSettings.LandRollTime), then on your feet
	}

	public enum EStance
	{
		Stand  = 0,
		Crouch = 1,
		Slide  = 2,
		Downed = 3,
	}

	/// <summary>
	/// Brownout movement rules (phase 2): sprint, dash, crouch, slide and skipping, jump, mantle, stance and eye height.
	/// The state lives in the KCC's networked data (KCC.User.cs), so the server owns it and the input authority predicts it.
	/// Runs every fixed tick from Agent.ProcessFixedInput, before the KCC moves. Speeds are applied by BREnvironmentProcessor.
	/// All numbers come from MovementSettings.
	/// </summary>
	[DefaultExecutionOrder(-4)]
	public sealed class AgentMovement : MonoBehaviour
	{
		// CONSTANTS

		private const float SkinWidth = 0.02f;

		private static readonly float[] WallProbeHeights = { 0.3f, 0.9f, 1.5f };
		private const float WindowSideSearch = 0.6f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>
		/// Test hook: holds this player (PlayerRef index) in DebugStance, so hitboxes can be checked from the other peer. -1 = off.
		/// Static, so in Multipeer the server and the client apply it alike.
		/// </summary>
		public static int     DebugStancePlayer = -1;
		public static EStance DebugStance;
		/// <summary>
		/// Test hook: the server logs why each slide ends or leaves the ground.
		/// </summary>
		public static bool    DebugLogSlideEnds;
#endif

		// PRIVATE MEMBERS

		private Agent _agent;
		private KCC   _kcc;

		private readonly Collider[] _overlapBuffer = new Collider[1];

		// PUBLIC METHODS

		public void OnSpawned()
		{
			MovementSettings settings = MovementSettings.Current;

			if (_kcc.IsProxy == false)
			{
				KCCData data = _kcc.FixedData;
				data.MoveMode            = EMoveMode.Walk;
				data.Stance              = EStance.Stand;
				data.ModeStartTick       = 0;
				data.ChainStep           = 0;
				data.AirSlideQueued      = false;
				data.LastSprintPressTick = 0;
				data.DashReadyTick       = 0;
				data.GunReadyTick        = 0;
				data.EyeHeight           = settings.StandEyeHeight;

				_kcc.SetShape(EKCCShape.Capsule, settings.CapsuleRadius, settings.StandHeight);
			}
		}

		/// <summary>
		/// Updates the movement mode from this tick's input. Clears inputs the mode doesn't allow (aim while sprinting, dashing or mantling)
		/// and scales the look delta while dashing. Starts jumps itself.
		/// </summary>
		public void ProcessFixedInput(ref GameplayInput input)
		{
			MovementSettings s    = MovementSettings.Current;
			KCCData          data = _kcc.FixedData;
			AgentInput       agentInput = _agent.AgentInput;

			int   tick      = _agent.Runner.Tick;
			float deltaTime = _agent.Runner.DeltaTime;

			bool grounded       = data.IsGrounded;
			bool hasMove        = input.MoveDirection.sqrMagnitude > 0.01f;
			bool movingBackward = input.MoveDirection.y < -0.01f;
			bool jumpPressed    = agentInput.WasActivated(EGameplayInputAction.Jump,   input);
			bool crouchPressed  = agentInput.WasActivated(EGameplayInputAction.Crouch, input);
			bool sprintPressed  = agentInput.WasActivated(EGameplayInputAction.Sprint, input);

			Vector3 horizontalVelocity = data.KinematicVelocity.OnlyXZ();
			float   horizontalSpeed    = horizontalVelocity.magnitude;

			EMoveMode previousMode = data.MoveMode;
			EMoveMode mode         = previousMode;

			// Downed (phase 4): crawl only. Health has already stripped every other input
			if (_agent.Health.IsDowned == true)
			{
				if (previousMode != EMoveMode.Downed)
				{
					EndChain(data);
					SetMode(data, EMoveMode.Downed, tick);
				}

				data.GunReadyTick = int.MaxValue;
				input.Aim         = false;

				UpdateStance(data, deltaTime, s);
				return;
			}

			// The drop (phase 7, ending changed in phase 10): wingsuit, the wings pop off, the landing roll. Agent has already stripped every other input
			if (previousMode == EMoveMode.Wingsuit || previousMode == EMoveMode.WingsOff)
			{
				UpdateDrop(ref input, data, tick, deltaTime, grounded);
				UpdateStance(data, deltaTime, s);
				return;
			}

			if (previousMode == EMoveMode.LandRoll)
			{
				input.Aim  = false;
				input.Jump = false;

				if ((tick - data.ModeStartTick) * deltaTime < MatchSettings.Current.LandRollTime)
				{
					UpdateStance(data, deltaTime, s);
					return;
				}

				// Up and moving, the gun ready (you land empty-handed)
				mode = GetGroundMode(input, grounded, movingBackward, hasMove, EMoveMode.Walk);
				SetMode(data, mode, tick);
				data.DropVelocity = Vector3.zero;
				data.DropCarry    = 0.0f;
				data.GunReadyTick = tick;
				previousMode      = mode;
			}

			if (previousMode == EMoveMode.Downed)
			{
				// Revived: back on your feet, the gun comes up like after a sprint
				mode = EMoveMode.Walk;
				SetMode(data, mode, tick);
				data.GunReadyTick = tick + SecondsToTicks(s.SprintOutTime, deltaTime);
				previousMode = mode;
			}

			// Double Jump (phase 5b): the air jump comes back on landing
			if (grounded == true && data.DoubleJumpUsed == true && mode != EMoveMode.Grapple)
			{
				data.DoubleJumpUsed = false;
			}

			// Mantle: Space pressed on the ground, or held in the air, while facing a ledge in reach
			if (mode != EMoveMode.Mantle && mode != EMoveMode.Grapple && input.Jump == true && (jumpPressed == true || grounded == false) && TryStartMantle(data, tick, s) == true)
			{
				mode = EMoveMode.Mantle;
			}

			switch (mode)
			{
				case EMoveMode.Grapple:
				{
					mode = UpdateGrapple(data, tick, deltaTime, jumpPressed);
					break;
				}
				case EMoveMode.Mantle:
				{
					if (previousMode == EMoveMode.Mantle && (tick - data.ModeStartTick - 1) * deltaTime >= data.MantleDuration)
					{
						mode = input.Crouch == true ? EMoveMode.Crouch : EMoveMode.Walk;
						SetMode(data, mode, tick);
						data.GunReadyTick = tick + SecondsToTicks(s.SprintOutTime, deltaTime);
					}
					break;
				}
				case EMoveMode.Dash:
				{
					if ((tick - data.ModeStartTick) * deltaTime >= s.DashDuration)
					{
						mode = GetGroundMode(input, grounded, movingBackward, hasMove, EMoveMode.Walk);
						SetMode(data, mode, tick);
						data.DashReadyTick = tick + SecondsToTicks(s.DashCooldown, deltaTime);
						data.GunReadyTick  = tick + SecondsToTicks(s.SprintOutTime, deltaTime);
					}
					break;
				}
				case EMoveMode.Slide:
				{
					mode = UpdateSlide(ref input, data, tick, deltaTime, grounded, hasMove, jumpPressed, s);
					break;
				}
				case EMoveMode.SkipAir:
				{
					if (crouchPressed == true)
					{
						data.AirSlideQueued = true;
					}

					if (jumpPressed == true && grounded == false)
					{
						TryDoubleJump(data);
					}

					if (grounded == true && tick > data.ModeStartTick + 1)
					{
						if (data.AirSlideQueued == true && hasMove == true)
						{
							// Land in a slide: ramp to the next step's peak (SlidePeakMultipliers), capped at the last one
							StartSlide(data, tick, data.ChainStep + 1, horizontalSpeed, s.GetSlidePeakSpeed(data.ChainStep + 1), horizontalVelocity, s);
							mode = EMoveMode.Slide;
						}
						else
						{
							LogSlideEnd(data, tick, "landed without an air slide");
							EndChain(data);
							mode = GetGroundMode(input, grounded, movingBackward, hasMove, EMoveMode.Walk);
							SetMode(data, mode, tick);
						}
					}
					break;
				}
				default:
				{
					// Walk, Sprint, Crouch
					bool canDash = grounded == true && previousMode != EMoveMode.Crouch && tick >= data.DashReadyTick;

					if (sprintPressed == true)
					{
						bool isDoubleTap = data.LastSprintPressTick > 0 && (tick - data.LastSprintPressTick) * deltaTime <= s.DashDoubleTapWindow;
						if (isDoubleTap == true && canDash == true)
						{
							Vector3 dashDirection = hasMove == true ? data.TransformRotation * input.MoveDirection.X0Y() : data.TransformDirection;
							data.MoveDirectionLock   = dashDirection.OnlyXZ().normalized;
							data.LastSprintPressTick = 0;
							mode = EMoveMode.Dash;
							SetMode(data, mode, tick);
							break;
						}

						data.LastSprintPressTick = tick;
					}

					if (crouchPressed == true && previousMode == EMoveMode.Sprint && grounded == true && horizontalSpeed >= s.SprintSpeed * s.SlideEntrySpeedFraction)
					{
						// First slide of a chain: entered at sprint speed, ramps to the first peak
						StartSlide(data, tick, 1, s.SprintSpeed, s.GetSlidePeakSpeed(1), horizontalVelocity, s);
						mode = EMoveMode.Slide;
						break;
					}

					mode = GetGroundMode(input, grounded, movingBackward, hasMove, previousMode);

					if (jumpPressed == true && grounded == true && mode != EMoveMode.Crouch && _agent.Character.AnimationController.CanJump() == true)
					{
						_kcc.Jump(Vector3.up);
					}
					else if (jumpPressed == true && grounded == false)
					{
						TryDoubleJump(data);
					}

					if (mode != previousMode)
					{
						SetMode(data, mode, tick);
					}
					break;
				}
			}

			if (previousMode == EMoveMode.Sprint && mode != EMoveMode.Sprint)
			{
				// Gun comes up from the sprint tuck
				data.GunReadyTick = Mathf.Max(data.GunReadyTick, tick + SecondsToTicks(s.SprintOutTime, deltaTime));
			}

			if (mode == EMoveMode.Sprint || mode == EMoveMode.Dash || mode == EMoveMode.Mantle || mode == EMoveMode.Grapple)
			{
				input.Aim = false;
			}

			if (mode == EMoveMode.Dash)
			{
				input.LookRotationDelta *= s.DashTurnRateMultiplier;
			}

			UpdateStance(data, deltaTime, s);
		}

		public bool CanFire()
		{
			KCCData   data = _kcc.FixedData;
			EMoveMode mode = data.MoveMode;

			if (mode == EMoveMode.Sprint || mode == EMoveMode.Dash || mode == EMoveMode.Mantle || mode == EMoveMode.Downed || mode == EMoveMode.Grapple || mode == EMoveMode.LandRoll || IsDropMode(mode) == true)
				return false;

			return _agent.Runner.Tick >= data.GunReadyTick;
		}

		/// <summary>Phase 7: in the air from the helicopter, wingsuit or wings off.</summary>
		public static bool IsDropMode(EMoveMode mode)
		{
			return mode == EMoveMode.Wingsuit || mode == EMoveMode.WingsOff;
		}

		/// <summary>True while falling from the helicopter (wingsuit or wings off).</summary>
		public bool IsDropping => IsDropMode(_kcc.FixedData.MoveMode);

		/// <summary>Phase 10: the landing roll after the drop.</summary>
		public bool IsLandRolling => _kcc.FixedData.MoveMode == EMoveMode.LandRoll;

		/// <summary>Seconds into the landing roll at the given simulation time, or -1 when not rolling.</summary>
		public float GetLandRollTime(float time)
		{
			KCCData data = _kcc.FixedData;
			if (data.MoveMode != EMoveMode.LandRoll)
				return -1.0f;

			return Mathf.Max(0.0f, time - data.ModeStartTick * _agent.Runner.DeltaTime);
		}

		/// <summary>
		/// Phase 7 (server, right after the agent spawns at the helicopter): starts the wingsuit with the helicopter's velocity. The mode
		/// is in the KCC's networked data, so the client's first snapshot of its new agent already has it.
		/// </summary>
		public void StartDrop(Vector3 velocity)
		{
			KCCData data = _kcc.FixedData;

			EndChain(data);

			data.DropVelocity      = velocity;
			data.DropPitch         = 0.0f;
			data.DropCarry         = 0.0f;
			data.KinematicVelocity = Vector3.zero;
			data.DynamicVelocity   = Vector3.zero;
			data.GunReadyTick      = int.MaxValue;

			SetMode(data, EMoveMode.Wingsuit, _agent.Runner.Tick);
		}

		/// <summary>
		/// Phase 5b (AgentEquipment): starts the grapple's pull to target (where the feet go), covering the distance in GrapplePullTime.
		/// False when already there.
		/// </summary>
		public bool StartGrapple(Vector3 target)
		{
			EquipmentSettings settings = EquipmentSettings.Current;
			KCCData           data     = _kcc.FixedData;

			Vector3 offset   = target - data.TargetPosition;
			float   distance = offset.magnitude;
			if (distance <= settings.GrappleStopDistance)
				return false;

			EndChain(data);

			data.GrappleTarget     = target;
			data.GrappleSpeed      = distance / Mathf.Max(0.1f, settings.GrapplePullTime);
			data.MoveDirectionLock = offset.OnlyXZ().sqrMagnitude > 0.0001f ? offset.OnlyXZ().normalized : data.TransformDirection.OnlyXZ().normalized;
			data.GunReadyTick      = int.MaxValue;

			SetMode(data, EMoveMode.Grapple, _agent.Runner.Tick);
			return true;
		}

		/// <summary>Phase 5b: lets go of the rope. With lift, a push up and forward so you land on the ledge or roof.</summary>
		public void EndGrapple(bool lift)
		{
			EquipmentSettings settings = EquipmentSettings.Current;
			KCCData           data     = _kcc.FixedData;

			if (data.MoveMode != EMoveMode.Grapple)
				return;

			int tick = _agent.Runner.Tick;

			SetMode(data, EMoveMode.Walk, tick);
			data.GunReadyTick      = tick;
			data.KinematicVelocity = data.MoveDirectionLock * settings.GrappleEndForward;
			data.DynamicVelocity   = lift == true ? Vector3.up * settings.GrappleEndLift : Vector3.zero;
		}

		/// <summary>
		/// True on the first tick the gun is up after a sprint, dash or mantle. A trigger held through the sprint-out then fires like a fresh press.
		/// </summary>
		public bool IsFirstGunReadyTick()
		{
			return _agent.Runner.Tick == _kcc.FixedData.GunReadyTick;
		}

		public bool CanAim()
		{
			EMoveMode mode = _kcc.FixedData.MoveMode;
			return mode != EMoveMode.Sprint && mode != EMoveMode.Dash && mode != EMoveMode.Mantle && mode != EMoveMode.Downed && mode != EMoveMode.Grapple && mode != EMoveMode.LandRoll && IsDropMode(mode) == false;
		}

		public float GetTurnRateMultiplier()
		{
			return _kcc.FixedData.MoveMode == EMoveMode.Dash ? MovementSettings.Current.DashTurnRateMultiplier : 1.0f;
		}

		/// <summary>
		/// Render-predicted jump for the local player: only when the next fixed tick will jump too.
		/// </summary>
		public bool ShouldPredictJump()
		{
			KCCData   data = _kcc.FixedData;
			EMoveMode mode = data.MoveMode;

			if (data.IsGrounded == false)
				return false;
			if (mode != EMoveMode.Walk && mode != EMoveMode.Sprint && (mode != EMoveMode.Slide || GetSlideSpeed(data, MovementSettings.Current) <= MovementSettings.Current.WalkSpeed))
				return false;
			if (_agent.Character.AnimationController.CanJump() == false)
				return false;

			return ProbeMantle(data, MovementSettings.Current, out _, out _, out _, out _) == false;
		}

		/// <summary>
		/// Eye height above the feet. The fixed value moves at a fixed rate each tick; the local render view extends it to render time so it doesn't step.
		/// </summary>
		public static float GetEyeHeight(KCC kcc, KCCData data)
		{
			MovementSettings s = MovementSettings.Current;

			float eyeHeight = data.EyeHeight > 0.0f ? data.EyeHeight : s.StandEyeHeight;

			if (kcc.IsInFixedUpdate == false && kcc.IsProxy == false)
			{
				float rate = GetEyeHeightRate(s);
				eyeHeight = Mathf.MoveTowards(eyeHeight, GetTargetEyeHeight(data, eyeHeight, s), rate * data.Alpha * data.DeltaTime);
			}

			return eyeHeight;
		}

		public static float GetSlideSpeed(KCCData data, MovementSettings s)
		{
			return Mathf.Min(GetSlideCurveSpeed(data, s) + data.SlideBonus, Mathf.Max(s.SkipCapSpeed, data.SlidePeakSpeed));
		}

		private static float GetSlideCurveSpeed(KCCData data, MovementSettings s)
		{
			float rampTime  = s.SlideRampTime;
			float decayTime = s.GetSlideDecayTime(data.ChainStep);
			float time      = data.SlideTime;

			if (time < rampTime)
				return Mathf.Lerp(data.SlideStartSpeed, data.SlidePeakSpeed, time / rampTime);

			return Mathf.Lerp(data.SlidePeakSpeed, s.CrouchSpeed, (time - rampTime) / decayTime);
		}

		/// <summary>
		/// Where the mantle puts the feet at a given time since it started: straight up to the ledge, then across onto it.
		/// </summary>
		public static Vector3 GetMantlePosition(KCCData data, float elapsed)
		{
			Vector3 start   = data.MantleStart;
			Vector3 end     = data.MantleEnd;
			Vector3 top     = new Vector3(start.x, end.y, start.z);
			float   rise    = Mathf.Max(0.0f, end.y - start.y);
			float   across  = Vector3.Distance(top, end);
			float   total   = rise + across;

			if (total <= 0.0001f || data.MantleDuration <= 0.0f)
				return end;

			float distance = total * Mathf.Clamp01(elapsed / data.MantleDuration);
			if (distance <= rise)
				return start + Vector3.up * distance;

			return Vector3.Lerp(top, end, across > 0.0001f ? (distance - rise) / across : 1.0f);
		}

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_agent = GetComponent<Agent>();
			_kcc   = GetComponent<KCC>();
		}

		// PRIVATE METHODS

		/// <summary>
		/// Phase 5b: on the rope. Ends near the spot or when the pull time runs out (with the lift), on Space, or when something stops it.
		/// </summary>
		private EMoveMode UpdateGrapple(KCCData data, int tick, float deltaTime, bool jumpPressed)
		{
			EquipmentSettings settings = EquipmentSettings.Current;

			float elapsed  = (tick - data.ModeStartTick) * deltaTime;
			float distance = Vector3.Distance(data.TargetPosition, data.GrappleTarget);

			bool arrived = distance <= settings.GrappleStopDistance;
			bool timeout = elapsed >= settings.GrapplePullTime * 1.25f + 0.1f;
			bool stuck   = elapsed > 0.25f && data.RealVelocity.sqrMagnitude < 1.0f;

			if (arrived == false && timeout == false && stuck == false && jumpPressed == false)
				return EMoveMode.Grapple;

			// At a ledge: climb onto it with the mantle (the hook was near its top). Otherwise every way off the rope gets the lift
			if (jumpPressed == false && TryStartMantle(data, tick, MovementSettings.Current) == true)
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				EquipmentDebug.Log(_agent.Runner, $"GRAPPLE {_agent.name} let go ({(arrived ? "arrived" : timeout ? "pull time over" : "stopped by something")}) {distance:F2} m from the spot, climbing the ledge");
#endif
				return EMoveMode.Mantle;
			}

			EndGrapple(true);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			EquipmentDebug.Log(_agent.Runner, $"GRAPPLE {_agent.name} let go ({(arrived ? "arrived" : timeout ? "pull time over" : stuck ? "stopped by something" : "Space")}) {distance:F2} m from the spot");
#endif
			return data.MoveMode;
		}

		/// <summary>
		/// Phase 7: the wingsuit follows your look (level glides, looking down dives; W faster, S slower, A/D bank) until the ground is
		/// WingsOffHeight below, then the wings pop off and you fall the rest of the way (phase 10, Logan 9 Oct: no parachute). Touching
		/// the ground starts the landing roll, carrying some of the forward speed, empty-handed.
		/// </summary>
		private void UpdateDrop(ref GameplayInput input, KCCData data, int tick, float deltaTime, bool grounded)
		{
			MatchSettings m = MatchSettings.Current;

			input.Aim  = false;
			input.Jump = false;

			if (grounded == true && tick > data.ModeStartTick + 1)
			{
				Vector3 along     = data.DropVelocity.OnlyXZ();
				float   rollSpeed = Mathf.Min(along.magnitude, m.LandRollSpeed);

				data.MoveDirectionLock = along.sqrMagnitude > 0.01f ? along.normalized : data.TransformDirection.OnlyXZ().normalized;
				data.DropVelocity      = data.MoveDirectionLock * rollSpeed; // The roll's start speed (BREnvironmentProcessor slows it to 0)
				data.KinematicVelocity = Vector3.zero;
				data.DynamicVelocity   = Vector3.zero;
				data.GunReadyTick      = int.MaxValue;

				SetMode(data, EMoveMode.LandRoll, tick);

				if (_agent.Runner.IsServer == true)
				{
					MatchDebug.Log(_agent.Runner, $"LAND {_agent.name} at {data.TargetPosition.ToString("F0")}, rolling at {rollSpeed:F1} m/s");
				}
				return;
			}

			if (data.MoveMode == EMoveMode.Wingsuit && GroundDistance(data.TargetPosition, m.WingsOffHeight) <= m.WingsOffHeight)
			{
				SetMode(data, EMoveMode.WingsOff, tick);

				if (_agent.Runner.IsServer == true)
				{
					MatchDebug.Log(_agent.Runner, $"WINGS OFF {_agent.name} {GroundDistance(data.TargetPosition, m.WingsOffHeight * 2.0f):F1} m above the ground at {data.TargetPosition.ToString("F0")}");
				}
			}

			if (data.MoveMode == EMoveMode.WingsOff)
			{
				// Falling: the forward speed carries on, gravity takes over
				data.DropVelocity += Physics.gravity * deltaTime;
				data.GunReadyTick  = int.MaxValue;
				return;
			}

			Quaternion yaw     = Quaternion.Euler(0.0f, data.LookYaw, 0.0f);
			Vector3    forward = yaw * Vector3.forward;
			Vector3    right   = yaw * Vector3.right;
			Vector2    move    = input.MoveDirection;

			float dive  = Mathf.Clamp01(data.LookPitch / Mathf.Max(1.0f, m.DivePitch));
			float speed = Mathf.Lerp(m.GlideSpeed, m.DiveSpeed, dive);
			float sink  = Mathf.Lerp(m.GlideSink, m.DiveSink, dive);

			if (move.y > 0.1f)  { speed *= 1.0f + m.FastShare; }
			if (move.y < -0.1f) { speed *= 1.0f - m.SlowShare; }

			// Phase 11f (Logan, 11 Oct): diving builds speed; pulling out turns part of the falling speed into carry, extra forward speed
			// that bleeds off slowly while gliding (fast while diving) and lowers the sink a little while you have it
			float carry  = data.DropCarry;
			bool  diving = dive >= 0.5f;

			sink *= 1.0f - m.CarryLift * Mathf.Clamp01(carry / Mathf.Max(0.01f, m.CarryLiftAt));

			float fall       = data.DropVelocity.y;
			float targetFall = -sink;
			if (fall < targetFall)
			{
				// Falling faster than this pitch's sink: pull out at PullOutRate, the speed shed becomes carry unless still diving
				float pulled = Mathf.Min(targetFall, fall + m.PullOutRate * deltaTime);
				if (diving == false)
				{
					carry += (pulled - fall) * m.CarryShare;
				}
				fall = pulled;
			}
			else
			{
				fall = Mathf.MoveTowards(fall, targetFall, m.DropAcceleration * deltaTime);
			}

			Vector3 flat       = data.DropVelocity.OnlyXZ();
			Vector3 flatTarget = forward * (speed + carry) + right * (move.x * m.BankSpeed);
			flat = Vector3.MoveTowards(flat, flatTarget, m.DropAcceleration * deltaTime);

			carry = Mathf.Clamp(carry - (diving == true ? m.CarryDiveDecay : m.CarryDecay) * deltaTime, 0.0f, m.CarryMax);

			data.DropCarry    = carry;
			data.DropVelocity = new Vector3(flat.x, fall, flat.z);
			data.GunReadyTick = int.MaxValue;

			// The body follows the look's pitch (level = flat, head first; straight down = head down), lagging like air resistance
			float pitchTarget = Mathf.Clamp(data.LookPitch, 0.0f, 90.0f);
			data.DropPitch += (pitchTarget - data.DropPitch) * (1.0f - Mathf.Exp(-deltaTime / Mathf.Max(0.01f, m.BodyPitchLag)));
		}

		/// <summary>Distance from the feet down to the ground, or more than maxDistance when there's nothing that close.</summary>
		private float GroundDistance(Vector3 feet, float maxDistance)
		{
			if (_agent.Runner.GetPhysicsScene().Raycast(feet + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, maxDistance + 0.1f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
				return Mathf.Max(0.0f, hit.distance - 0.1f);

			return maxDistance + 1.0f;
		}

		/// <summary>Phase 5b: Double Jump's second jump in the air, at full jump height, once until you land.</summary>
		private void TryDoubleJump(KCCData data)
		{
			if (data.DoubleJumpUsed == true || AgentGear.IsActive(_agent, EGear.DoubleJump) == false)
				return;

			data.DoubleJumpUsed = true;
			_kcc.Jump(Vector3.up);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			EquipmentDebug.Log(_agent.Runner, $"GEAR {_agent.name} Double Jump at {data.TargetPosition.y:F2} m");
#endif
		}

		private EMoveMode GetGroundMode(GameplayInput input, bool grounded, bool movingBackward, bool hasMove, EMoveMode previousMode)
		{
			if (input.Crouch == true)
				return EMoveMode.Crouch;

			// Sprint: Shift held, moving but not backward, not aiming or firing. Starts on the ground; carries through a jump
			bool canSprint = input.Sprint == true && hasMove == true && movingBackward == false && input.Aim == false && input.Attack == false;
			if (canSprint == true && (grounded == true || previousMode == EMoveMode.Sprint))
				return EMoveMode.Sprint;

			return EMoveMode.Walk;
		}

		private EMoveMode UpdateSlide(ref GameplayInput input, KCCData data, int tick, float deltaTime, bool grounded, bool hasMove, bool jumpPressed, MovementSettings s)
		{
			if (grounded == false)
			{
				// Slid off an edge: the chain carries on in the air
				LogSlideEnd(data, tick, "left the ground");
				SetMode(data, EMoveMode.SkipAir, tick);
				data.AirSlideQueued = false;
				return EMoveMode.SkipAir;
			}

			float speed = GetSlideSpeed(data, s);

			if (jumpPressed == true)
			{
				if (speed > s.WalkSpeed && _agent.Character.AnimationController.CanJump() == true)
				{
					// Skip: the jump keeps the slide's momentum
					data.SlideMaxSpeed  = Mathf.Max(data.SlideMaxSpeed, speed);
					data.AirSlideQueued = false;
					SetMode(data, EMoveMode.SkipAir, tick);
					_kcc.Jump(Vector3.up);
					return EMoveMode.SkipAir;
				}

				return EndSlide(data, input, tick, "jump below walk speed");
			}

			if (hasMove == false)
				return EndSlide(data, input, tick, "no move input");

			// Slope along the slide direction: steep uphill breaks the chain, downhill pauses the decay and gains speed
			float slope = 0.0f;
			if (KCCPhysicsUtility.ProjectOnGround(data.GroundNormal, data.MoveDirectionLock, out Vector3 groundDirection) == true)
			{
				slope = Mathf.Asin(Mathf.Clamp(groundDirection.normalized.y, -1.0f, 1.0f)) * Mathf.Rad2Deg;
			}

			// The uphill has to last: at the top of a hill the capsule rides over the corner and the ground normal tips back for a moment
			data.SlideUphillTime = slope > s.ChainBreakSlope ? data.SlideUphillTime + deltaTime : 0.0f;
			if (data.SlideUphillTime >= s.ChainBreakTime)
				return EndSlide(data, input, tick, $"uphill, ground {slope:F1} deg");

			bool isDecaying = data.SlideTime >= s.SlideRampTime;
			if (isDecaying == false || slope > -2.0f)
			{
				data.SlideTime += deltaTime;
			}

			if (data.SlideTime >= s.SlideRampTime + s.GetSlideDecayTime(data.ChainStep))
				return EndSlide(data, input, tick, "decay finished");

			if (slope < -2.0f && s.SlideDownhillAcceleration > 0.0f)
			{
				// Gravity along the slope, up to the cap
				float gain = s.SlideDownhillAcceleration * Mathf.Sin(-slope * Mathf.Deg2Rad) * deltaTime;
				float room = Mathf.Max(s.SkipCapSpeed, data.SlidePeakSpeed) - GetSlideCurveSpeed(data, s);
				data.SlideBonus = Mathf.Clamp(data.SlideBonus + gain, 0.0f, Mathf.Max(0.0f, room));
			}

			data.SlideMaxSpeed = Mathf.Max(data.SlideMaxSpeed, GetSlideSpeed(data, s));

			// A/D bend the slide a little; mouse look stays free
			Vector3 inputDirection = (data.TransformRotation * input.MoveDirection.X0Y()).OnlyXZ();
			if (inputDirection.sqrMagnitude > 0.01f && Vector3.Dot(inputDirection.normalized, data.MoveDirectionLock) > -0.1f)
			{
				data.MoveDirectionLock = Vector3.RotateTowards(data.MoveDirectionLock, inputDirection.normalized, s.SlideSteerRate * Mathf.Deg2Rad * deltaTime, 0.0f).OnlyXZ().normalized;
			}

			return EMoveMode.Slide;
		}

		private EMoveMode EndSlide(KCCData data, GameplayInput input, int tick, string reason)
		{
			LogSlideEnd(data, tick, reason);
			EndChain(data);

			EMoveMode mode = input.Crouch == true ? EMoveMode.Crouch : EMoveMode.Walk;
			SetMode(data, mode, tick);
			return mode;
		}

		private void StartSlide(KCCData data, int tick, int chainStep, float startSpeed, float peakSpeed, Vector3 velocity, MovementSettings s)
		{
			Vector3 direction = velocity.OnlyXZ();
			if (direction.sqrMagnitude < 0.01f)
			{
				direction = data.TransformDirection.OnlyXZ();
			}

			data.ChainStep         = chainStep;
			data.SlideTime         = 0.0f;
			data.SlideStartSpeed   = Mathf.Min(startSpeed, s.SkipCapSpeed);
			data.SlidePeakSpeed    = Mathf.Max(peakSpeed, data.SlideStartSpeed);
			data.SlideMaxSpeed     = data.SlideStartSpeed;
			data.SlideBonus        = 0.0f;
			data.SlideUphillTime   = 0.0f;
			data.AirSlideQueued    = false;
			data.MoveDirectionLock = direction.normalized;

			SetMode(data, EMoveMode.Slide, tick);
		}

		private void LogSlideEnd(KCCData data, int tick, string reason)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (DebugLogSlideEnds == true && _agent.Runner.IsServer == true)
			{
				Debug.Log($"Slide end ({_agent.Object.InputAuthority}) tick {tick}: {reason}. Chain {data.ChainStep}, speed {GetSlideSpeed(data, MovementSettings.Current):F2}, at {data.TargetPosition.ToString("F2")}, ground normal {data.GroundNormal.ToString("F2")}, real velocity {data.RealVelocity.ToString("F2")}");
			}
#endif
		}

		private static void EndChain(KCCData data)
		{
			data.ChainStep      = 0;
			data.AirSlideQueued = false;
			data.SlideTime      = 0.0f;
			data.SlideBonus     = 0.0f;
		}

		private static void SetMode(KCCData data, EMoveMode mode, int tick)
		{
			data.MoveMode      = mode;
			data.ModeStartTick = tick;
		}

		private bool TryStartMantle(KCCData data, int tick, MovementSettings s)
		{
			if (ProbeMantle(data, s, out Vector3 end, out EStance stance, out float climb, out Vector3 shift) == false)
				return false;

			data.MantleStart    = data.TargetPosition + shift;
			data.MantleEnd      = end;
			data.MantleDuration = s.GetMantleTime(climb);
			data.Stance         = stance;
			data.GunReadyTick   = int.MaxValue;
			EndChain(data);
			SetMode(data, EMoveMode.Mantle, tick);

			ApplyCapsuleHeight(s.GetHeight(stance));
			return true;
		}

		/// <summary>
		/// Looks for a ledge in front: a wall within reach, a walkable top no higher than the mantle height above the feet,
		/// and room for the capsule. If only the slide-height capsule fits (a window), the end point is on the far side, and the climb
		/// starts lined up with the opening's middle (shift, sideways along the wall; phase 8b: a 0.75 m window leaves 7 cm each side).
		/// </summary>
		private bool ProbeMantle(KCCData data, MovementSettings s, out Vector3 end, out EStance stance, out float climb, out Vector3 shift)
		{
			end    = default;
			stance = EStance.Stand;
			climb  = 0.0f;
			shift  = default;

			LayerMask    mask    = ObjectLayerMask.Environment;
			PhysicsScene physics = _agent.Runner.GetPhysicsScene();
			Vector3      feet    = data.TargetPosition;
			Vector3   forward = data.TransformDirection.OnlyXZ().normalized;
			float     radius  = s.CapsuleRadius;

			// 1. A wall in front
			RaycastHit wallHit = default;
			bool       hasWall = false;
			for (int i = 0; i < WallProbeHeights.Length && hasWall == false; ++i)
			{
				if (physics.Raycast(feet + Vector3.up * WallProbeHeights[i], forward, out RaycastHit hit, radius + s.MantleReach, mask, QueryTriggerInteraction.Ignore) == true)
				{
					Vector3 wallNormal = hit.normal.OnlyXZ();
					if (Mathf.Abs(hit.normal.y) < 0.3f && wallNormal.sqrMagnitude > 0.01f && Vector3.Dot(forward, -wallNormal.normalized) > 0.5f)
					{
						wallHit = hit;
						hasWall = true;
					}
				}
			}

			if (hasWall == false)
				return false;

			Vector3 into = -wallHit.normal.OnlyXZ().normalized;

			// 2. The top. The ray starts at the highest reachable point, so anything taller is skipped (a ray starting inside a wall doesn't hit it)
			float   maxTop    = feet.y + s.MantleMaxHeight + s.MantleTolerance;
			float   minTop    = feet.y + s.MantleMinHeight;
			Vector3 topProbe  = new Vector3(wallHit.point.x, maxTop, wallHit.point.z) + into * 0.1f;

			if (physics.Raycast(topProbe, Vector3.down, out RaycastHit topHit, maxTop - minTop, mask, QueryTriggerInteraction.Ignore) == false)
				return false;
			if (topHit.normal.y < Mathf.Cos(60.0f * Mathf.Deg2Rad))
				return false;

			float   topY   = topHit.point.y + SkinWidth;
			Vector3 riseTo = new Vector3(feet.x, topY, feet.z);
			Vector3 onTop  = new Vector3(wallHit.point.x, topY, wallHit.point.z) + into * (radius + 0.1f);

			climb = topY - feet.y;

			// 3. Room: stand or crouch on the top, otherwise slide-height through to the far side (window)
			EStance[] stances = { EStance.Stand, EStance.Crouch };
			for (int i = 0; i < stances.Length; ++i)
			{
				float height = s.GetHeight(stances[i]);
				if (FitsCapsule(riseTo, radius, height) == true && FitsCapsule(onTop, radius, height) == true)
				{
					end    = onTop;
					stance = stances[i];
					return true;
				}
			}

			float slideHeight = s.SlideHeight;

			// A window: find its sides from inside the opening and line up with its middle
			Vector3 along   = Vector3.Cross(Vector3.up, into).normalized;
			Vector3 opening = new Vector3(wallHit.point.x, topY + slideHeight * 0.5f, wallHit.point.z) + into * 0.05f;
			if (physics.Raycast(opening, along,  out RaycastHit sideA, WindowSideSearch, mask, QueryTriggerInteraction.Ignore) == true &&
			    physics.Raycast(opening, -along, out RaycastHit sideB, WindowSideSearch, mask, QueryTriggerInteraction.Ignore) == true)
			{
				shift   = along * ((sideA.distance - sideB.distance) * 0.5f);
				riseTo += shift;
			}

			if (FitsCapsule(riseTo, radius, slideHeight) == false)
				return false;

			Vector3 lastFit = default;
			bool    hasFit  = false;

			for (float distance = radius + 0.1f; distance <= 1.6f; distance += 0.1f)
			{
				Vector3 point = new Vector3(wallHit.point.x, topY, wallHit.point.z) + shift + into * distance;
				if (FitsCapsule(point, radius, slideHeight) == false)
					break;

				lastFit = point;
				hasFit  = true;

				// Past the sill: nothing right under the capsule
				if (physics.SphereCast(point + Vector3.up * (radius + 0.05f), radius * 0.9f, Vector3.down, out _, 0.35f, mask, QueryTriggerInteraction.Ignore) == false)
				{
					end    = point;
					stance = EStance.Slide;
					return true;
				}
			}

			if (hasFit == true)
			{
				end    = lastFit;
				stance = EStance.Slide;
				return true;
			}

			return false;
		}

		private void UpdateStance(KCCData data, float deltaTime, MovementSettings s)
		{
			EStance desired;
			switch (data.MoveMode)
			{
				case EMoveMode.Crouch:  desired = EStance.Crouch; break;
				case EMoveMode.Slide:   desired = EStance.Slide;  break;
				case EMoveMode.SkipAir: desired = data.AirSlideQueued == true ? EStance.Slide : EStance.Stand; break;
				case EMoveMode.Mantle:  desired = data.Stance; break; // Chosen when the mantle started
				case EMoveMode.Downed:  desired = EStance.Downed; break;
				case EMoveMode.LandRoll: desired = EStance.Crouch; break; // Low through the roll, up again as you get up
				default:               desired = EStance.Stand;  break;
			}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (DebugStancePlayer >= 0 && _agent.Object.InputAuthority.AsIndex == DebugStancePlayer)
			{
				desired = DebugStance;
			}
#endif

			// Growing taller needs room above; stay low under a ceiling
			EStance stance = desired;
			if (s.GetHeight(desired) > _kcc.Settings.Height + 0.001f && data.MoveMode != EMoveMode.Mantle)
			{
				stance = data.Stance;

				for (EStance candidate = EStance.Stand; candidate <= EStance.Downed; ++candidate)
				{
					if (candidate < desired)
						continue;

					if (s.GetHeight(candidate) <= _kcc.Settings.Height + 0.001f || FitsCapsule(data.TargetPosition, s.CapsuleRadius, s.GetHeight(candidate)) == true)
					{
						stance = candidate;
						break;
					}
				}
			}

			if (stance != desired && (data.MoveMode == EMoveMode.Walk || data.MoveMode == EMoveMode.Sprint))
			{
				// Kept low by a ceiling: move at crouch speed
				SetMode(data, EMoveMode.Crouch, _agent.Runner.Tick);
			}

			data.Stance = stance;
			ApplyCapsuleHeight(s.GetHeight(stance));

			if (data.EyeHeight <= 0.0f)
			{
				data.EyeHeight = s.StandEyeHeight;
			}

			data.EyeHeight = Mathf.MoveTowards(data.EyeHeight, GetTargetEyeHeight(data, data.EyeHeight, s), GetEyeHeightRate(s) * deltaTime);
		}

		/// <summary>
		/// The stance's eye height, except in the air (Logan, 9 Oct): crouching or queuing a slide in the air doesn't lower the eye until you
		/// land, so the camera dropping doesn't make the fall feel faster. The capsule still tucks. Mantles, grapples and the drop are left alone.
		/// </summary>
		private static float GetTargetEyeHeight(KCCData data, float currentEyeHeight, MovementSettings s)
		{
			float target = s.GetEyeHeight(data.Stance);

			bool holdInAir = data.IsGrounded == false && (data.MoveMode == EMoveMode.Walk || data.MoveMode == EMoveMode.Sprint || data.MoveMode == EMoveMode.Crouch || data.MoveMode == EMoveMode.SkipAir);
			if (holdInAir == true)
			{
				target = Mathf.Max(target, currentEyeHeight);
			}

			return target;
		}

		private void ApplyCapsuleHeight(float height)
		{
			MovementSettings s = MovementSettings.Current;

			if (Mathf.Abs(_kcc.Settings.Radius - s.CapsuleRadius) > 0.0001f)
			{
				_kcc.SetRadius(s.CapsuleRadius);
			}

			if (Mathf.Abs(_kcc.Settings.Height - height) > 0.0001f)
			{
				_kcc.SetHeight(height);
			}
		}

		private bool FitsCapsule(Vector3 feet, float radius, float height)
		{
			// Each Multipeer peer has its own physics scene
			float   checkRadius = radius - SkinWidth;
			Vector3 bottom      = feet + Vector3.up * (radius + SkinWidth);
			Vector3 top         = feet + Vector3.up * Mathf.Max(radius + SkinWidth, height - radius);

			return _agent.Runner.GetPhysicsScene().OverlapCapsule(bottom, top, checkRadius, _overlapBuffer, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == 0;
		}

		private static float GetEyeHeightRate(MovementSettings s)
		{
			return (s.StandEyeHeight - s.SlideEyeHeight) / Mathf.Max(0.01f, s.EyeHeightChangeTime);
		}

		private static int SecondsToTicks(float seconds, float deltaTime)
		{
			return Mathf.CeilToInt(seconds / deltaTime - 0.001f);
		}
	}
}
