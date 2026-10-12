using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Every movement number from the hub's Movement page (and the sprint-out time from gameplay_timings.json).
	/// Tuning happens here, never in code. Linked from GlobalSettings.Movement.
	/// </summary>
	[CreateAssetMenu(fileName = "MovementSettings", menuName = "TPSBR/Movement Settings")]
	public sealed class MovementSettings : ScriptableObject
	{
		[Header("Speeds")]
		[Tooltip("W, base walk speed in m/s. Strafe and backpedal use it too.")]
		public float WalkSpeed = 6.0f;
		[Tooltip("Crouch speed as a multiple of W.")]
		public float CrouchMultiplier = 0.75f;
		[Tooltip("Move speed while aimed in, m/s.")]
		public float AimSpeed = 2.5f;
		[Tooltip("Animation only: the walk and strafe clips play this many times faster while aimed in and moving (Logan, 9 Oct: trying 2).")]
		public float AimMoveAnimationSpeed = 2.0f;
		[Tooltip("Animation only: in the air, the body and the first-person arms switch to the Fall take once coming down with nothing to stand on this far below (m); jumps and short drops don't.")]
		public float FallAnimationHeight = 6.0f;
		[Tooltip("Sprint speed as a multiple of W.")]
		public float SprintMultiplier = 1.5f;
		[Tooltip("Speed above the current cap bleeds off at this rate (m/s per second), e.g. after a slide chain ends.")]
		public float OverspeedDeceleration = 8.0f;

		[Header("Sprint")]
		[Tooltip("After a sprint ends, the gun can fire this many seconds later (sprint_end_to_gun_up_s).")]
		public float SprintOutTime = 0.1f;
		[Tooltip("Slide starts only at this fraction of full sprint speed or more.")]
		public float SlideEntrySpeedFraction = 0.95f;

		[Header("Dash")]
		[Tooltip("Dash speed as a multiple of sprint speed.")]
		public float DashSprintMultiplier = 1.2f;
		public float DashDuration = 1.0f;
		[Tooltip("Counted from when the dash ends.")]
		public float DashCooldown = 4.0f;
		[Tooltip("Two sprint presses within this time make a dash.")]
		public float DashDoubleTapWindow = 0.3f;
		[Tooltip("Mouse turn rate multiplier while dashing.")]
		public float DashTurnRateMultiplier = 0.5f;

		[Header("Slide and skipping")]
		[Tooltip("Seconds each slide ramps toward its peak.")]
		public float SlideRampTime = 0.75f;
		[Tooltip("Decay time of the first slide, seconds.")]
		public float SlideFirstDecayTime = 0.75f;
		[Tooltip("Each skip's decay lasts this much longer than the last.")]
		public float SlideDecayGrowth = 1.2f;
		[Tooltip("Peak of each slide in a chain as a multiple of sprint speed: first slide, skip 1, skip 2, ... The last entry is the cap and repeats.")]
		public float[] SlidePeakMultipliers = { 1.2f, 1.4f, 1.6f };
		[Tooltip("Moving up a slope steeper than this (degrees) ends the chain.")]
		public float ChainBreakSlope = 10.0f;
		[Tooltip("The uphill has to last this many seconds before it ends the chain, so the corner at the top of a hill doesn't.")]
		public float ChainBreakTime = 0.1f;
		[Tooltip("Sliding downhill gains speed at this times the sine of the slope (m/s per second; 9.81 is real gravity, not the sample's 16), up to the cap. 0 turns it off.")]
		public float SlideDownhillAcceleration = 9.81f;
		[Tooltip("A/D bend a slide by up to this many degrees per second.")]
		public float SlideSteerRate = 30.0f;

		[Header("Jump and mantle")]
		[Tooltip("Jump height in meters (Logan, 4 Oct: 1.2; 9 Oct: doubled to 2.4, may tweak).")]
		public float JumpHeight = 1.2f;
		[Tooltip("Highest ledge above the feet you can mantle onto. In the air this counts from where your feet are, so a jump adds its height.")]
		public float MantleMaxHeight = 2.0f;
		[Tooltip("Lower ledges are left to the step-up and the jump.")]
		public float MantleMinHeight = 0.5f;
		[Tooltip("Slack on the height check, so a ledge exactly at the limit passes.")]
		public float MantleTolerance = 0.05f;
		[Tooltip("How far in front of the capsule a wall can be and still be mantled.")]
		public float MantleReach = 0.45f;
		public float MantleBaseTime = 0.3f;
		[Tooltip("Added mantle time per meter climbed.")]
		public float MantleTimePerMeter = 0.25f;
		public float MantleMaxTime = 0.8f;

		[Header("Player metrics")]
		[Tooltip("Phase 10 (Logan, 8 Oct): the player is 2.00 m, Logan's soldier. Each stance's capsule top is the top of the soldier's head in that stance's clip, and the eye is the soldier's eye bone.")]
		public float CapsuleRadius = 0.30f;
		public float StandHeight = 2.00f;
		public float StandEyeHeight = 1.80f;
		public float CrouchHeight = 1.57f;
		public float CrouchEyeHeight = 1.36f;
		public float SlideHeight = 1.43f;
		public float SlideEyeHeight = 1.20f;
		[Tooltip("Downed (phase 4): the capsule while crawling.")]
		public float DownedHeight = 0.87f;
		public float DownedEyeHeight = 0.53f;
		[Tooltip("Seconds for the eye to move between stance heights.")]
		public float EyeHeightChangeTime = 0.15f;

		[Header("Fall damage (sample's curve)")]
		[Tooltip("Landing speed (m/s) where damage starts.")]
		public float FallDamageMinVelocity = 20.0f;
		[Tooltip("Landing speed (m/s) for maximum damage.")]
		public float FallDamageMaxVelocity = 90.0f;
		public float FallDamageMax = 150.0f;
		[Tooltip("Smaller hits are ignored.")]
		public float FallDamageMinimum = 5.0f;

		// PUBLIC METHODS

		private static MovementSettings _fallback;

		/// <summary>
		/// The asset linked from GlobalSettings. Falls back to the defaults above (with a warning) if it isn't linked yet.
		/// </summary>
		public static MovementSettings Current
		{
			get
			{
				MovementSettings settings = Global.Settings != null ? Global.Settings.Movement : null;
				if (settings != null)
					return settings;

				if (_fallback == null)
				{
					_fallback = CreateInstance<MovementSettings>();
					Debug.LogWarning("MovementSettings isn't linked in GlobalSettings; using the code defaults. Run Brownout → Setup → Create Movement Settings.");
				}

				return _fallback;
			}
		}

		public float CrouchSpeed  => WalkSpeed * CrouchMultiplier;
		public float SprintSpeed  => WalkSpeed * SprintMultiplier;
		public float DashSpeed    => SprintSpeed * DashSprintMultiplier;
		public float SkipCapSpeed => SprintSpeed * GetSlidePeakMultiplier(int.MaxValue);

		/// <summary>
		/// Peak speed of a slide at this chain step (1 = first slide, 2 = skip 1, ...).
		/// </summary>
		public float GetSlidePeakSpeed(int chainStep)
		{
			return SprintSpeed * GetSlidePeakMultiplier(chainStep);
		}

		private float GetSlidePeakMultiplier(int chainStep)
		{
			if (SlidePeakMultipliers == null || SlidePeakMultipliers.Length == 0)
				return 1.0f;

			return SlidePeakMultipliers[Mathf.Clamp(chainStep - 1, 0, SlidePeakMultipliers.Length - 1)];
		}

		public float GetHeight(EStance stance)
		{
			switch (stance)
			{
				case EStance.Crouch: return CrouchHeight;
				case EStance.Slide:  return SlideHeight;
				case EStance.Downed: return DownedHeight;
				default:             return StandHeight;
			}
		}

		public float GetEyeHeight(EStance stance)
		{
			switch (stance)
			{
				case EStance.Crouch: return CrouchEyeHeight;
				case EStance.Slide:  return SlideEyeHeight;
				case EStance.Downed: return DownedEyeHeight;
				default:             return StandEyeHeight;
			}
		}

		public float GetSlideDecayTime(int chainStep)
		{
			return SlideFirstDecayTime * Mathf.Pow(SlideDecayGrowth, Mathf.Max(0, chainStep - 1));
		}

		public float GetMantleTime(float climbHeight)
		{
			return Mathf.Min(MantleBaseTime + MantleTimePerMeter * Mathf.Max(0.0f, climbHeight), MantleMaxTime);
		}
	}
}
