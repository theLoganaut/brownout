namespace Fusion.Addons.KCC
{
	using UnityEngine;
	using System.Collections.Generic;
	using TPSBR;

	public partial class KCC
	{
		public MoveState MoveState { get; set; }

		public void SetAim(bool aim)
		{
			if (IsProxy == true)
				return;

			Data.Aim = aim;
		}

		public void SetRecoil(Vector2 recoil)
		{
			if (IsProxy == true)
				return;

			KCCData data = _renderData;
			data.Recoil = recoil;

			if (IsInFixedUpdate == true)
			{
				data = _fixedData;
				data.Recoil = recoil;
			}
		}

		partial void InitializeUserNetworkProperties(KCCNetworkContext networkContext, List<IKCCNetworkProperty> networkProperties)
		{
			networkProperties.Add(new KCCNetworkVector2<KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.Recoil     = value, (context) => context.Data.Recoil,     null));
			networkProperties.Add(new KCCNetworkBool   <KCCNetworkContext>(networkContext,       (context, value) => context.Data.IsGrounded = value, (context) => context.Data.IsGrounded, null));
			networkProperties.Add(new KCCNetworkBool   <KCCNetworkContext>(networkContext,       (context, value) => context.Data.Aim        = value, (context) => context.Data.Aim,        null));

			// Brownout movement state (phase 2). Owned by the server, predicted by the input authority, read by proxies for stance and pose.
			// Discrete values never blend between snapshots: proxies take the newer one.
			networkProperties.Add(new KCCNetworkInt    <KCCNetworkContext>(networkContext,       (context, value) => context.Data.MoveMode            = (EMoveMode)value, (context) => (int)context.Data.MoveMode,           PickInt));
			networkProperties.Add(new KCCNetworkInt    <KCCNetworkContext>(networkContext,       (context, value) => context.Data.Stance              = (EStance)value,   (context) => (int)context.Data.Stance,             PickInt));
			networkProperties.Add(new KCCNetworkInt    <KCCNetworkContext>(networkContext,       (context, value) => context.Data.ModeStartTick       = value,            (context) => context.Data.ModeStartTick,           PickInt));
			networkProperties.Add(new KCCNetworkInt    <KCCNetworkContext>(networkContext,       (context, value) => context.Data.ChainStep           = value,            (context) => context.Data.ChainStep,               PickInt));
			networkProperties.Add(new KCCNetworkBool   <KCCNetworkContext>(networkContext,       (context, value) => context.Data.AirSlideQueued      = value,            (context) => context.Data.AirSlideQueued,          null));
			networkProperties.Add(new KCCNetworkFloat  <KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.SlideTime           = value,            (context) => context.Data.SlideTime,               null));
			networkProperties.Add(new KCCNetworkFloat  <KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.SlideStartSpeed     = value,            (context) => context.Data.SlideStartSpeed,         null));
			networkProperties.Add(new KCCNetworkFloat  <KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.SlidePeakSpeed      = value,            (context) => context.Data.SlidePeakSpeed,          null));
			networkProperties.Add(new KCCNetworkFloat  <KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.SlideMaxSpeed       = value,            (context) => context.Data.SlideMaxSpeed,           null));
			networkProperties.Add(new KCCNetworkFloat  <KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.SlideBonus          = value,            (context) => context.Data.SlideBonus,              null));
			networkProperties.Add(new KCCNetworkFloat  <KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.SlideUphillTime     = value,            (context) => context.Data.SlideUphillTime,         null));
			networkProperties.Add(new KCCNetworkVector3<KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.MoveDirectionLock   = value,            (context) => context.Data.MoveDirectionLock,       null));
			networkProperties.Add(new KCCNetworkInt    <KCCNetworkContext>(networkContext,       (context, value) => context.Data.LastSprintPressTick = value,            (context) => context.Data.LastSprintPressTick,     PickInt));
			networkProperties.Add(new KCCNetworkInt    <KCCNetworkContext>(networkContext,       (context, value) => context.Data.DashReadyTick       = value,            (context) => context.Data.DashReadyTick,           PickInt));
			networkProperties.Add(new KCCNetworkInt    <KCCNetworkContext>(networkContext,       (context, value) => context.Data.GunReadyTick        = value,            (context) => context.Data.GunReadyTick,            PickInt));
			networkProperties.Add(new KCCNetworkVector3<KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.MantleStart         = value,            (context) => context.Data.MantleStart,             null));
			networkProperties.Add(new KCCNetworkVector3<KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.MantleEnd           = value,            (context) => context.Data.MantleEnd,               null));
			networkProperties.Add(new KCCNetworkFloat  <KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.MantleDuration      = value,            (context) => context.Data.MantleDuration,          null));
			networkProperties.Add(new KCCNetworkFloat  <KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.EyeHeight           = value,            (context) => context.Data.EyeHeight,               null));

			// Phase 5b: the grapple pull and Double Jump
			networkProperties.Add(new KCCNetworkVector3<KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.GrappleTarget       = value,            (context) => context.Data.GrappleTarget,           null));
			networkProperties.Add(new KCCNetworkFloat  <KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.GrappleSpeed        = value,            (context) => context.Data.GrappleSpeed,            null));
			networkProperties.Add(new KCCNetworkBool   <KCCNetworkContext>(networkContext,       (context, value) => context.Data.DoubleJumpUsed      = value,            (context) => context.Data.DoubleJumpUsed,          null));

			// Phase 7: the wingsuit and parachute velocity
			networkProperties.Add(new KCCNetworkVector3<KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.DropVelocity        = value,            (context) => context.Data.DropVelocity,            null));
			networkProperties.Add(new KCCNetworkFloat  <KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.DropPitch           = value,            (context) => context.Data.DropPitch,               null));
			networkProperties.Add(new KCCNetworkFloat  <KCCNetworkContext>(networkContext, 0.0f, (context, value) => context.Data.DropCarry           = value,            (context) => context.Data.DropCarry,               null));
		}

		private static int PickInt(KCCNetworkContext context, float alpha, int from, int to) => alpha < 0.5f ? from : to;
	}

	public partial class KCCData
	{
		public bool    Aim;
		public Vector2 Recoil;

		// Brownout movement state, see AgentMovement
		public EMoveMode MoveMode;
		public EStance   Stance;
		public int       ModeStartTick;
		public int       ChainStep;            // 0 = no skip chain, 1 = first slide, 2 = skip 1, ...
		public bool      AirSlideQueued;
		public float     SlideTime;            // Seconds into the current slide (paused while the decay pauses downhill)
		public float     SlideStartSpeed;
		public float     SlidePeakSpeed;
		public float     SlideMaxSpeed;        // Fastest speed reached in the current slide
		public float     SlideBonus;           // Speed gained sliding downhill, on top of the ramp/decay curve
		public float     SlideUphillTime;      // Seconds the slide has been going up a slope steeper than ChainBreakSlope
		public Vector3   MoveDirectionLock;    // Slide or dash direction
		public int       LastSprintPressTick;
		public int       DashReadyTick;
		public int       GunReadyTick;         // The gun can fire from this tick (sprint out, dash, mantle)
		public Vector3   MantleStart;
		public Vector3   MantleEnd;
		public float     MantleDuration;
		public float     EyeHeight;
		public Vector3   GrappleTarget;        // Phase 5b: where the grapple pulls the feet
		public float     GrappleSpeed;         // Phase 5b: pull speed (the rope's length over the pull time)
		public bool      DoubleJumpUsed;       // Phase 5b: Double Jump's air jump is spent until you land
		public Vector3   DropVelocity;         // Phase 7: wingsuit / parachute velocity
		public float     DropPitch;            // Wingsuit: the body's pitch (0 level, 90 head straight down), easing toward the look
		public float     DropCarry;            // Phase 11f: extra forward speed gained pulling out of a dive, bleeding off while gliding

		partial void CopyUserDataFromOther(KCCData other)
		{
			Aim    = other.Aim;
			Recoil = other.Recoil;

			MoveMode            = other.MoveMode;
			Stance              = other.Stance;
			ModeStartTick       = other.ModeStartTick;
			ChainStep           = other.ChainStep;
			AirSlideQueued      = other.AirSlideQueued;
			SlideTime           = other.SlideTime;
			SlideStartSpeed     = other.SlideStartSpeed;
			SlidePeakSpeed      = other.SlidePeakSpeed;
			SlideMaxSpeed       = other.SlideMaxSpeed;
			SlideBonus          = other.SlideBonus;
			SlideUphillTime     = other.SlideUphillTime;
			MoveDirectionLock   = other.MoveDirectionLock;
			LastSprintPressTick = other.LastSprintPressTick;
			DashReadyTick       = other.DashReadyTick;
			GunReadyTick        = other.GunReadyTick;
			MantleStart         = other.MantleStart;
			MantleEnd           = other.MantleEnd;
			MantleDuration      = other.MantleDuration;
			EyeHeight           = other.EyeHeight;
			GrappleTarget       = other.GrappleTarget;
			GrappleSpeed        = other.GrappleSpeed;
			DoubleJumpUsed      = other.DoubleJumpUsed;
			DropVelocity        = other.DropVelocity;
			DropPitch           = other.DropPitch;
			DropCarry           = other.DropCarry;
		}

		partial void ClearUserData()
		{
			MoveMode            = default;
			Stance              = default;
			ModeStartTick       = default;
			ChainStep           = default;
			AirSlideQueued      = default;
			SlideTime           = default;
			SlideStartSpeed     = default;
			SlidePeakSpeed      = default;
			SlideMaxSpeed       = default;
			SlideBonus          = default;
			SlideUphillTime     = default;
			MoveDirectionLock   = default;
			LastSprintPressTick = default;
			DashReadyTick       = default;
			GunReadyTick        = default;
			MantleStart         = default;
			MantleEnd           = default;
			MantleDuration      = default;
			EyeHeight           = default;
			GrappleTarget       = default;
			GrappleSpeed        = default;
			DoubleJumpUsed      = default;
			DropVelocity        = default;
			DropPitch           = default;
			DropCarry           = default;
		}
	}
}
