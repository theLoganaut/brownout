using System;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Bots (phase 9, Tech & Networking: bots, first pass). Every number the bots' brains use: senses, skill, fighting, cover, looting and
	/// movement. Logan (7 Oct): each bot gets its own skill between Medium and Hard. Linked from GlobalSettings.Bots;
	/// Brownout → Setup → Build Bot Settings makes it.
	/// </summary>
	[CreateAssetMenu(fileName = "BotSettings", menuName = "TPSBR/Brownout/Bot Settings")]
	public sealed class BotSettings : ScriptableObject
	{
		[Serializable]
		public struct Skill
		{
			[Tooltip("Seconds from first seeing an enemy to the first shot.")]
			public float Reaction;
			[Tooltip("Aim error (degrees) when it starts tracking a target.")]
			public float AimErrorStart;
			[Tooltip("Aim error (degrees) once it has tracked the target for SettleTime.")]
			public float AimErrorSettled;
			[Tooltip("Seconds of tracking to go from the start error to the settled error.")]
			public float SettleTime;
			[Tooltip("Fastest turn while aiming, degrees per second.")]
			public float TurnSpeed;
			[Tooltip("Share of the gun's recoil it pulls down (1 = all of it).")]
			public float RecoilControl;
			[Tooltip("Share of a moving target's sideways motion it follows (1 = no lag behind a strafing target).")]
			public float Tracking;

			public static Skill Lerp(Skill a, Skill b, float t)
			{
				return new Skill
				{
					Reaction        = Mathf.Lerp(a.Reaction,        b.Reaction,        t),
					AimErrorStart   = Mathf.Lerp(a.AimErrorStart,   b.AimErrorStart,   t),
					AimErrorSettled = Mathf.Lerp(a.AimErrorSettled, b.AimErrorSettled, t),
					SettleTime      = Mathf.Lerp(a.SettleTime,      b.SettleTime,      t),
					TurnSpeed       = Mathf.Lerp(a.TurnSpeed,       b.TurnSpeed,       t),
					RecoilControl   = Mathf.Lerp(a.RecoilControl,   b.RecoilControl,   t),
					Tracking        = Mathf.Lerp(a.Tracking,        b.Tracking,        t),
				};
			}
		}

		// PUBLIC MEMBERS

		public static BotSettings Current
		{
			get
			{
				GlobalSettings global = Global.Settings;
				if (global != null && global.Bots != null)
					return global.Bots;

				if (_fallback == null)
				{
					_fallback = Resources.Load<BotSettings>("Settings/BotSettings");
					if (_fallback == null)
					{
						_fallback = CreateInstance<BotSettings>();
					}
				}

				return _fallback;
			}
		}

		[Header("Skill (Logan, 7 Oct: a mix from medium to hard)")]
		public Skill Medium = new Skill { Reaction = 0.5f, AimErrorStart = 1.5f, AimErrorSettled = 0.6f, SettleTime = 1.5f, TurnSpeed = 360.0f, RecoilControl = 0.6f,  Tracking = 0.6f  };
		public Skill Hard   = new Skill { Reaction = 0.3f, AimErrorStart = 0.8f, AimErrorSettled = 0.3f, SettleTime = 1.0f, TurnSpeed = 540.0f, RecoilControl = 0.85f, Tracking = 0.85f };
		[Tooltip("Each bot's skill is rolled between these (0 = Medium, 1 = Hard).")]
		public float SkillMin = 0.0f;
		public float SkillMax = 1.0f;

		[Header("Senses")]
		[Tooltip("Enemies are seen up to this far (m).")]
		public float SightRange      = 150.0f;
		[Tooltip("Full angle of the view cone (degrees).")]
		public float SightCone       = 120.0f;
		[Tooltip("Enemies this close are noticed in any direction (m).")]
		public float CloseAwareness  = 6.0f;
		[Tooltip("Unsuppressed shots are heard this far away (m).")]
		public float HearingRange    = 100.0f;
		[Tooltip("Camouflage hides a still player from bots beyond this distance (m).")]
		public float CamouflageRange = 10.0f;
		[Tooltip("An enemy out of sight is remembered this long (s).")]
		public float MemoryTime      = 6.0f;
		[Tooltip("Decisions per second (sight checks, targets, goals). Steering and aim run every tick.")]
		public float ThinkRate       = 5.0f;

		[Header("Fighting")]
		[Tooltip("Aims down sights beyond this distance (m).")]
		public float   AdsDistance     = 25.0f;
		[Tooltip("Automatic bursts (shots): under 15 m, under 50 m, beyond.")]
		public Vector2 BurstClose      = new Vector2(8, 14);
		public Vector2 BurstMid        = new Vector2(4, 7);
		public Vector2 BurstFar        = new Vector2(2, 4);
		[Tooltip("Pause between bursts (s).")]
		public Vector2 BurstPause      = new Vector2(0.2f, 0.45f);
		[Tooltip("Extra wait between single shots (semi, burst, bolt) by distance: under 15 m, under 50 m, beyond (s).")]
		public Vector3 ShotPacing      = new Vector3(0.05f, 0.15f, 0.35f);
		[Tooltip("Fires only when the aim is within this angle of where it means to shoot (degrees), or the target's width when larger.")]
		public float   FireAngle       = 1.0f;
		[Tooltip("Seconds between strafe direction changes.")]
		public Vector2 StrafeTime      = new Vector2(0.6f, 1.4f);
		[Tooltip("Backs off when an enemy is closer than this (m).")]
		public float   BackOffDistance = 6.0f;
		[Tooltip("Reloads between fights when the mag is below this share.")]
		public float   ReloadBelow     = 0.4f;
		[Tooltip("Punches when unarmed and an enemy is this close (m).")]
		public float   PunchDistance   = 2.2f;

		[Header("Cover")]
		[Tooltip("Looks for cover this far around (m).")]
		public float   CoverRadius     = 15.0f;
		[Tooltip("Spots tried per cover search.")]
		public int     CoverCandidates = 16;
		[Tooltip("Seconds it stays in cover before peeking back out.")]
		public Vector2 CoverWait       = new Vector2(1.5f, 3.0f);
		[Tooltip("Seconds after leaving cover before a hit sends it to cover again (unless badly hurt).")]
		public float   CoverCooldown   = 4.0f;
		[Tooltip("Gives up on reaching cover after this long (s).")]
		public float   CoverTimeout    = 6.0f;
		[Tooltip("Heals when below this health and no enemy is in sight.")]
		public float   HealBelow       = 110.0f;
		[Tooltip("Goes to cover on any hit below this health, even during the cooldown.")]
		public float   BadlyHurt       = 60.0f;

		[Header("Looting")]
		[Tooltip("Looks for loot this far around (m).")]
		public float   LootRadius      = 45.0f;
		[Tooltip("Wants healing items until it has this many.")]
		public int     WantHeals       = 3;
		[Tooltip("Wants ammo until a gun's caliber pool is below this share of its cap.")]
		public float   WantAmmoBelow   = 0.7f;
		[Tooltip("Gives up on an item it can't reach after this long (s).")]
		public float   LootGiveUp      = 15.0f;

		[Header("Movement")]
		[Tooltip("A duo's follower stays between these distances from its leader (m).")]
		public Vector2 FollowDistance  = new Vector2(5.0f, 14.0f);
		[Tooltip("Turn speed while walking around (degrees per second).")]
		public float   WalkTurnSpeed   = 300.0f;
		[Tooltip("Next path corner counts as reached within this (m).")]
		public float   CornerReach     = 0.7f;
		[Tooltip("Paths are recomputed at least this often while moving (s).")]
		public float   RepathTime      = 4.0f;
		[Tooltip("Stuck when it moved less than StuckDistance in StuckTime.")]
		public float   StuckTime       = 1.5f;
		public float   StuckDistance   = 0.6f;
		[Tooltip("Sprints toward goals farther than this (m).")]
		public float   SprintDistance  = 12.0f;
		[Tooltip("Seconds a duo stays at each area it reaches (looking around it) before heading for the next, unless the zone moves it on.")]
		public Vector2 AreaStay        = new Vector2(45.0f, 90.0f);

		[Header("Match start (phase 9b)")]
		[Tooltip("No POI or cabin area gets more bot duos than this.")]
		public int     MaxDuosPerArea  = 2;
		[Tooltip("Bot duos in one area start at least this far apart (m), each at its own loot spot.")]
		public float   DuoSpacing      = 25.0f;

		[Header("Drop (phase 11f: bots ride the Osprey and drop to their spot)")]
		[Tooltip("Duos only pick spots this far from the Osprey's path over the map (m); the best dive-and-glide reaches about 350 m.")]
		public float   DropReach       = 300.0f;
		[Tooltip("A duo jumps this far before the Osprey passes closest to its spot (m): the Osprey's speed carries a jumper on along the path.")]
		public float   DropJumpLead    = 50.0f;
		[Tooltip("The duo's second bot jumps this long after the first (s).")]
		public float   DropPairGap     = 0.6f;
		[Tooltip("How fast a dropping bot turns its look (degrees per second).")]
		public float   DropTurnSpeed   = 120.0f;
		[Tooltip("Gliding: when it would still glide this much past its spot (m), it slows down or dives to come down on it.")]
		public float   DropOvershoot   = 10.0f;

		[Header("Performance (Logan, 7 Oct: thin distant bots)")]
		[Tooltip("A bot farther than this from every human (and not fighting) has its pose updated less often on the server (m).")]
		public float   ThinDistance    = 120.0f;
		[Tooltip("Such a bot's pose is updated every this many ticks (1 = every tick).")]
		public int     ThinEveryTicks  = 4;
		[Tooltip("A dead bot's body is removed after this many seconds once the match is on (its loot bag stays).")]
		public float   BodyTime        = 8.0f;

		[Header("Look (phase 10: bots wear a different suit color)")]
		[Tooltip("Bots' color for the soldier's SLD_Armor plates (players: blue-grey).")]
		public Color   SuitColor       = new Color(0.36f, 0.32f, 0.20f);
		[Tooltip("Bots' color for the soldier's SLD_Accent parts (players: orange).")]
		public Color   AccentColor     = new Color(0.70f, 0.08f, 0.06f);

		[Header("Names (placeholders)")]
		public string[] Names =
		{
			"Alder", "Badger", "Cinder", "Dusk", "Ember", "Flint", "Gale", "Hollow", "Ivy", "Juniper", "Kestrel", "Lark",
			"Moss", "Nettle", "Onyx", "Pike", "Quill", "Rook", "Slate", "Thistle", "Umber", "Vale", "Wren", "Yarrow",
		};

		// PRIVATE MEMBERS

		private static BotSettings _fallback;

		// PUBLIC METHODS

		public Skill GetSkill(float skill) => Skill.Lerp(Medium, Hard, Mathf.Clamp01(skill));

		public string GetName(int botNumber)
		{
			if (Names == null || Names.Length == 0 || botNumber <= 0)
				return $"Bot {botNumber}";

			int index = (botNumber - 1) % Names.Length;
			int round = (botNumber - 1) / Names.Length;
			return round == 0 ? Names[index] : $"{Names[index]} {round + 1}";
		}
	}
}
