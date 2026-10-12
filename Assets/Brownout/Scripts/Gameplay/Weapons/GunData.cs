using UnityEngine;

namespace TPSBR
{
	public enum EGunClass
	{
		AR,
		SMG,
		Tactical,
		Sniper,
	}

	public enum EFireMode
	{
		Auto,
		Semi,
		Burst,
		Bolt,
	}

	/// <summary>
	/// The six ammo pools (Weapons & Gunplay page). The value is the pool's index.
	/// </summary>
	public enum ECaliber
	{
		Cal9mm  = 0,
		Cal45   = 1,
		Cal556  = 2,
		Cal762  = 3,
		Cal338  = 4,
		Cal50   = 5,
	}

	public enum ESwapWeight
	{
		Light,
		Medium,
		Heavy,
		Sniper,
		HeavySniper,
	}

	/// <summary>
	/// One gun's numbers, written by Brownout → Data → Import Guns from References/brownout_guns.json.
	/// Re-running the import overwrites these assets, so tune the JSON file, not the assets.
	/// </summary>
	[CreateAssetMenu(fileName = "GunData", menuName = "TPSBR/Gun Data")]
	public sealed class GunData : ScriptableObject
	{
		public const int CaliberCount = 6;

		[Header("Identity")]
		public string      GunName;
		public string      BasedOn;
		public EGunClass   Class;

		[Header("Firing")]
		public EFireMode   FireMode;
		[Tooltip("Shots per burst (burst guns only).")]
		public int         BurstCount = 1;
		public int         Magazine = 30;
		[Tooltip("Body damage before armor.")]
		public float       Damage = 30.0f;
		public float       HeadMultiplier = 1.1f;
		[Tooltip("Seconds between shots (inside a burst for burst guns, the bolt cycle for bolt guns).")]
		public float       FireInterval = 0.1f;
		[Tooltip("Seconds from one burst's first shot to the next burst's (burst guns only).")]
		public float       BurstCycle;
		[Tooltip("The table's semi-auto interval is how fast the footage player fired, not a hard limit.")]
		public bool        FireIntervalIsLowerBound;

		[Header("Reload")]
		[Tooltip("Full reload: the gun can fire again after this many seconds.")]
		public float       ReloadTime = 2.5f;
		[Tooltip("The mag counts as loaded at this many seconds (server timer). Swapping or punching before it loses the reload.")]
		public float       AmmoInTime = 1.4f;
		public bool        AmmoInEstimated;

		[Header("Aiming")]
		public float       AdsTime = 0.27f;
		[Tooltip("Measured iron-sight zoom; 0 = the class's iron-sight zoom from GunplaySettings.")]
		public float       AdsZoom;

		[Header("Ammo and bullets")]
		public ECaliber    Caliber;
		[Tooltip("Muzzle speed in m/s.")]
		public float       BulletSpeed = 600.0f;

		[Header("Swap")]
		public ESwapWeight SwapWeight;
		[Tooltip("Seconds to put this gun away.")]
		public float       HolsterTime = 0.25f;
		[Tooltip("Seconds to raise this gun.")]
		public float       DrawTime = 0.35f;

		[Header("Recoil (measured, degrees)")]
		[Tooltip("Where the view sat at each of the first shots of a spray, as an offset from the aim point: x = side (+ right), y = up (recoil_measured.ads_pattern_deg_first12).")]
		public Vector2[]   AdsRecoilSteps;
		public float       AdsUpMean;
		[Tooltip("Peak up offset during a spray (ads_up_max_deg).")]
		public float       AdsUpMax;
		public float       AdsSideMean;
		public float       AdsSideRange;
		[Tooltip("Seconds the view took to settle after the trigger was let go (ads_settle_after_release_s).")]
		public float       AdsSettleTime;
		[Tooltip("0 = not measured (the ADS pattern is used from the hip too).")]
		public float       HipClimbPerShot;
		public float       HipSideRange;

		[Header("Recoil feel (phase 11e, recoil_feel by class; render only)")]
		[Tooltip("Per shot the gun and arms slam back toward the camera this far (m), then recover.")]
		public float       KickBack = 0.03f;
		[Tooltip("The gun's own small pitch up per shot (deg).")]
		public float       KickPitch = 1.0f;
		[Tooltip("View punch per shot (deg); it springs back and doesn't move where you aim.")]
		public float       PunchPitch = 0.35f;
		public float       PunchRoll = 0.25f;
		[Tooltip("How fast the gun's kickback recovers.")]
		public float       KickRecovery = 14.0f;

		[Header("Attachments (phase 3c)")]
		public string[]    OpticsAllowed;
		public string[]    AttachmentsAllowed;

		[Header("Built-in parts (phase 10 models)")]
		[Tooltip("Zoom of a scope that is part of the model (Swivel, Twofer); 0 = none. Such a gun has no optic slot and no iron sights.")]
		public float            BuiltInScopeZoom;
		[Tooltip("The built-in scope is magnified: scope sway and hold breath apply.")]
		public bool             BuiltInScopeMagnified;
		[Tooltip("How far behind the gun's _ADS_Eye the eye sits when aimed through the built-in scope (m) (built_in_scope.eye_distance_m; phase 11a).")]
		public float            BuiltInScopeEyeDistance;
		[Tooltip("Parts built into the model that always give that attachment's effects (built_in_attachments in the data file).")]
		public AttachmentData[] BuiltInAttachments;

		[Header("Model")]
		[Tooltip("Logan's model (Assets/Brownout/Art/Weapons/<Name>/), or none for a block placeholder.")]
		public GameObject  Model;

		// PUBLIC METHODS

		public static string GetCaliberName(ECaliber caliber)
		{
			switch (caliber)
			{
				case ECaliber.Cal9mm: return "9mm";
				case ECaliber.Cal45:  return ".45";
				case ECaliber.Cal556: return "5.56";
				case ECaliber.Cal762: return "7.62";
				case ECaliber.Cal338: return ".338";
				case ECaliber.Cal50:  return ".50";
				default:                  return caliber.ToString();
			}
		}

		public static bool TryParseCaliber(string name, out ECaliber caliber)
		{
			for (int i = 0; i < CaliberCount; ++i)
			{
				if (GetCaliberName((ECaliber)i) == name)
				{
					caliber = (ECaliber)i;
					return true;
				}
			}

			caliber = default;
			return false;
		}

		/// <summary>
		/// Phase 11e: where the view should sit (side, up; degrees from the aim point) once shot shotIndex (0 = first of a spray)
		/// has fired, i.e. the measured offset at the next shot. After the measured shots it wanders around the gun's mean
		/// within its side range (and wanderUpShare of the gap from the mean to the peak up). seed varies the wander per shot.
		/// Guns with no measured pattern return zero (see HasMeasuredRecoil).
		/// </summary>
		public Vector2 GetAdsOffset(int shotIndex, int seed, float wanderUpShare)
		{
			Vector2[] steps = AdsRecoilSteps;
			if (steps == null || steps.Length == 0)
				return Vector2.zero;

			int next = shotIndex + 1;
			if (next < steps.Length)
				return steps[next];

			return new Vector2(AdsSideMean + 0.5f * AdsSideRange * Hash(seed, 1), AdsUpMean + wanderUpShare * Mathf.Max(0.0f, AdsUpMax - AdsUpMean) * Hash(seed, 2));
		}

		public bool HasMeasuredRecoil => AdsRecoilSteps != null && AdsRecoilSteps.Length > 0;
		public bool HasHipRecoil      => HipClimbPerShot > 0.0f || HipSideRange > 0.0f;

		/// <summary>A repeatable number in -1..1 for this seed (the same on every peer, so prediction matches).</summary>
		public static float Hash(int seed, int salt)
		{
			unchecked
			{
				uint h = (uint)seed * 747796405u + (uint)salt * 2891336453u;
				h ^= h >> 16;
				h *= 0x7feb352du;
				h ^= h >> 15;
				h *= 0x846ca68bu;
				h ^= h >> 16;
				return (h & 0xFFFFFF) / (float)0x7FFFFF - 1.0f;
			}
		}

		/// <summary>The gun takes this attachment (optics_allowed / attachments_allowed in the data file).</summary>
		public bool Allows(AttachmentData attachment)
		{
			if (attachment == null)
				return true;

			string[] allowed = attachment.Slot == EAttachmentSlot.Optic ? OpticsAllowed : AttachmentsAllowed;
			return allowed != null && System.Array.IndexOf(allowed, attachment.AttachmentName) >= 0;
		}

	}
}
