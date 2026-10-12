using System;
using UnityEngine;

namespace TPSBR
{
	public enum EZonePhase
	{
		Wait  = 0,
		Close = 1,
	}

	/// <summary>
	/// Match flow numbers (phase 7): warm-up, helicopter, wingsuit and landing, zone schedule and damage, map edge, end of match.
	/// From the Match Flow & Zone page and the Phase 7 spec; tuned here, never in code. Linked from GlobalSettings.Match
	/// (Brownout → Setup → Build Match Settings).
	/// </summary>
	[CreateAssetMenu(fileName = "MatchSettings", menuName = "TPSBR/Match Settings")]
	public sealed class MatchSettings : ScriptableObject
	{
		[Serializable]
		public struct ZoneStep
		{
			public EZonePhase Phase;
			[Tooltip("Seconds.")]
			public float      Duration;
			[Tooltip("Damage per second outside the circle during this step.")]
			public float      Damage;
		}

		[Header("Warm-up (before the host starts)")]
		[Tooltip("Respawn time while waiting for the host.")]
		public float       WarmupRespawnTime = 3.0f;
		[Tooltip("Phase 11b: in a match started from the main menu, the warm-up counts down this long once a real player has loaded in, then the match starts (Logan, 10 Oct: later it waits until the match is full first).")]
		public float       WarmupCountdown = 30.0f;
		[Tooltip("Phase 11b: real players a menu match takes (bots fill the rest).")]
		public int         SessionMaxPlayers = 24;
		[Tooltip("Phase 11b: the map the main menu's PLAY plays (its ID in MapSettings).")]
		public string      MenuMapID = "Map.Brownout";

		[Header("Helicopter (Match Flow & Zone)")]
		[Tooltip("Height above sea level (y = 0).")]
		public float       HelicopterHeight = 250.0f;
		public float       HelicopterSpeed  = 40.0f;
		[Tooltip("The straight path passes through a random point this share of the map's width from its center.")]
		public float       PathCenterShare  = 0.25f;
		[Tooltip("Seconds from boarding until the helicopter reaches the map edge and you may jump (loading in and a look around).")]
		public float       RunInTime        = 7.0f;
		[Tooltip("The helicopter leaves this far past the far edge.")]
		public float       PathOutside      = 120.0f;
		[Tooltip("Helicopter camera: this far behind the helicopter, orbiting it with the mouse.")]
		public float       HelicopterCameraDistance = 40.0f;
		[Tooltip("Helicopter camera: the point it orbits is this far above the helicopter.")]
		public float       HelicopterCameraHeight   = 6.0f;
		[Tooltip("Past the far edge, anyone still aboard is pushed out one per this many seconds.")]
		public float       ForcedJumpInterval = 0.5f;

		[Header("Wingsuit")]
		[Tooltip("Looking level: forward speed and sink (m/s). Logan, 11 Oct (11f): a straight glide from the helicopter lands in about 18 s, about 250 m out; diving first and leveling out reaches about 350 m.")]
		public float       GlideSpeed       = 12.0f;
		public float       GlideSink        = 13.0f;
		[Tooltip("Looking straight down at DivePitch: forward speed and sink (m/s).")]
		public float       DiveSpeed        = 10.0f;
		public float       DiveSink         = 45.0f;
		[Tooltip("Look pitch (degrees down) that gives the full dive.")]
		public float       DivePitch        = 70.0f;
		[Tooltip("W speeds the forward speed up by this share, S slows it down by SlowShare.")]
		public float       FastShare        = 0.2f;
		public float       SlowShare        = 0.4f;
		[Tooltip("A / D sideways speed (m/s).")]
		public float       BankSpeed        = 6.0f;
		[Tooltip("How fast the velocity reaches its target (m/s per second).")]
		public float       DropAcceleration = 15.0f;
		[Tooltip("Pulling out of a dive: the fall slows toward the glide's sink at this rate (m/s per second), so leveling out costs some height.")]
		public float       PullOutRate      = 12.0f;
		[Tooltip("Share of the downward speed shed while pulling out that becomes carry: extra forward speed on top of the glide.")]
		public float       CarryShare       = 0.6f;
		[Tooltip("Most carry you can hold (m/s).")]
		public float       CarryMax         = 20.0f;
		[Tooltip("Carry bleeds off this fast while gliding (m/s per second).")]
		public float       CarryDecay       = 1.0f;
		[Tooltip("Carry bleeds off this fast while diving (m/s per second), so repeated short dives don't stack.")]
		public float       CarryDiveDecay   = 10.0f;
		[Tooltip("With CarryLiftAt of carry the sink is this share lower (less with less carry).")]
		public float       CarryLift        = 0.3f;
		public float       CarryLiftAt      = 20.0f;
		[Tooltip("Jumping out adds this much speed straight down (m/s).")]
		public float       JumpOutSpeed     = 5.0f;
		[Tooltip("The body's angle follows the look (level = flat, straight down = head down) with this lag in seconds (time to cover about 63% of a change), like air resistance.")]
		public float       BodyPitchLag     = 0.5f;

		[Header("Landing (Logan, 9 Oct: no parachute)")]
		[Tooltip("The wings pop off by themselves this high above the ground; you fall the rest of the way.")]
		public float       WingsOffHeight   = 6.0f;
		[Tooltip("The landing roll (Drop_Roll's length at 1x), then you get up and move.")]
		public float       LandRollTime     = 0.93f;
		[Tooltip("Share of the roll after which the camera moves from third to first person.")]
		public float       RollFirstPersonShare = 0.5f;
		[Tooltip("The roll carries you forward at most this fast (m/s), slowing to a stop by its end.")]
		public float       LandRollSpeed    = 5.0f;

		[Header("Zone (radius halves each close)")]
		[Tooltip("The first circle, on the map center.")]
		public float       ZoneStartRadius  = 500.0f;
		[Tooltip("Each close multiplies the radius by this; the final close goes to 0.")]
		public float       ZoneShrink       = 0.5f;
		public ZoneStep[]  ZoneSteps        = DefaultZoneSteps();

		[Header("Map edge")]
		public float       EdgeCountdown    = 10.0f;
		[Tooltip("Damage per second once the countdown runs out.")]
		public float       EdgeDamage       = 20.0f;

		[Header("Bots (phase 9)")]
		[Tooltip("Bot duos at match start (Tech & Networking: 11, so 2 humans + 22 bots). On our map they ride the Osprey and drop to POIs and cabin areas near its path (phase 11f).")]
		public int         BotDuos          = 11;
		[Tooltip("A team with only one human at match start gets a bot teammate.")]
		public bool        LoneBotTeammate  = true;

		[Header("Test match")]
		[Tooltip("Dummy duos placed on the ground at the start (phase 7: 3; 0 since phase 9, still there for testing).")]
		public int         DummyDuos        = 0;
		[Tooltip("Dummy duos are at least this far apart.")]
		public float       DummySpacing     = 100.0f;

		[Header("End")]
		[Tooltip("\"Match ends in\" countdown after the victory.")]
		public float       EndCountdown     = 10.0f;

		// PUBLIC MEMBERS

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>Test switch (Fast Match): the zone clock runs this many times faster. Static, so every Multipeer peer agrees.</summary>
		public static float DebugTimeScale = 1.0f;
		/// <summary>Test overrides (-1 = the asset's number): the Match Check runs with its 3 dummy duos and no bots.</summary>
		public static int   DebugBotDuos   = -1;
		public static int   DebugDummyDuos = -1;
#endif

		/// <summary>Bot duos at match start (the asset's number, unless a test overrides it).</summary>
		public int BotDuoCount
		{
			get
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				if (DebugBotDuos >= 0)
					return DebugBotDuos;
#endif
				return Mathf.Max(0, BotDuos);
			}
		}

		/// <summary>Dummy duos at match start (the asset's number, unless a test overrides it).</summary>
		public int DummyDuoCount
		{
			get
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				if (DebugDummyDuos >= 0)
					return DebugDummyDuos;
#endif
				return Mathf.Max(0, DummyDuos);
			}
		}

		public static float TimeScale
		{
			get
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				return Mathf.Max(0.01f, DebugTimeScale);
#else
				return 1.0f;
#endif
			}
		}

		private static MatchSettings _fallback;

		/// <summary>The asset linked from GlobalSettings. Falls back to the defaults above (with a warning) if it isn't linked yet.</summary>
		public static MatchSettings Current
		{
			get
			{
				MatchSettings settings = Global.Settings != null ? Global.Settings.Match : null;
				if (settings != null)
					return settings;

				if (_fallback == null)
				{
					_fallback = CreateInstance<MatchSettings>();
					Debug.LogWarning("MatchSettings isn't linked in GlobalSettings; using the code defaults. Run Brownout → Setup → Build Match Settings.");
				}

				return _fallback;
			}
		}

		// PUBLIC METHODS

		/// <summary>Number of closes in the schedule (the last one ends at radius 0).</summary>
		public int CloseCount
		{
			get
			{
				int count = 0;
				for (int i = 0; ZoneSteps != null && i < ZoneSteps.Length; ++i)
				{
					if (ZoneSteps[i].Phase == EZonePhase.Close)
					{
						++count;
					}
				}
				return count;
			}
		}

		/// <summary>The Match Flow & Zone table: Wait 5:00, Close 1 2:00, Wait 2:00, Close 2 1:00, Wait 1:00, Close 3 1:00, Wait 1:00, Final 5:00; damage 10 / 30 / 75 from 5:00.</summary>
		public static ZoneStep[] DefaultZoneSteps()
		{
			return new ZoneStep[]
			{
				new ZoneStep { Phase = EZonePhase.Wait,  Duration = 300.0f, Damage = 0.0f  },
				new ZoneStep { Phase = EZonePhase.Close, Duration = 120.0f, Damage = 10.0f },
				new ZoneStep { Phase = EZonePhase.Wait,  Duration = 120.0f, Damage = 10.0f },
				new ZoneStep { Phase = EZonePhase.Close, Duration = 60.0f,  Damage = 30.0f },
				new ZoneStep { Phase = EZonePhase.Wait,  Duration = 60.0f,  Damage = 30.0f },
				new ZoneStep { Phase = EZonePhase.Close, Duration = 60.0f,  Damage = 75.0f },
				new ZoneStep { Phase = EZonePhase.Wait,  Duration = 60.0f,  Damage = 75.0f },
				new ZoneStep { Phase = EZonePhase.Close, Duration = 300.0f, Damage = 75.0f },
			};
		}
	}
}
