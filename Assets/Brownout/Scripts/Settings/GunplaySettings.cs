using System;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Shared gunplay numbers (Weapons & Gunplay page, brownout_guns.json, Phase 3a spec). Per-gun numbers live in GunData.
	/// Brownout → Data → Import Guns refreshes the values that come from the JSON file; the rest are tuned here, never in code.
	/// Linked from GlobalSettings.Gunplay.
	/// </summary>
	[CreateAssetMenu(fileName = "GunplaySettings", menuName = "TPSBR/Gunplay Settings")]
	public sealed class GunplaySettings : ScriptableObject
	{
		[Serializable]
		public struct ClassFalloff
		{
			public EGunClass Class;
			[Tooltip("Full damage out to this distance (m). 0 = no falloff.")]
			public float     FullDamageDistance;
			[Tooltip("Minimum damage from this distance (m); linear in between.")]
			public float     MinDamageDistance;
			[Range(0.0f, 1.0f)]
			public float     MinDamageFraction;
		}

		[Serializable]
		public struct ClassZoom
		{
			public EGunClass Class;
			public float     IronSightZoom;
		}

		[Serializable]
		public struct CaliberCap
		{
			public ECaliber Caliber;
			public int      Cap;
		}

		// Health moved to HealthSettings in phase 4 (150, no regen, no shield since 3a)

		[Header("Bullets")]
		[Tooltip("m/s² (ballistics.gravity_mps2).")]
		public float          Gravity = 9.81f;
		[Tooltip("A bullet is removed after this many seconds in flight.")]
		public float          BulletLifetime = 2.0f;
		public ClassFalloff[] Falloff =
		{
			new ClassFalloff { Class = EGunClass.SMG,      FullDamageDistance = 15.0f, MinDamageDistance = 40.0f,  MinDamageFraction = 0.70f },
			new ClassFalloff { Class = EGunClass.AR,       FullDamageDistance = 50.0f, MinDamageDistance = 100.0f, MinDamageFraction = 0.75f },
			new ClassFalloff { Class = EGunClass.Tactical, FullDamageDistance = 70.0f, MinDamageDistance = 130.0f, MinDamageFraction = 0.80f },
			new ClassFalloff { Class = EGunClass.Sniper,   FullDamageDistance = 0.0f,  MinDamageDistance = 0.0f,   MinDamageFraction = 1.00f },
		};

		[Header("Aiming")]
		public ClassZoom[]    IronSightZoom =
		{
			new ClassZoom { Class = EGunClass.SMG,      IronSightZoom = 1.26f },
			new ClassZoom { Class = EGunClass.AR,       IronSightZoom = 1.43f },
			new ClassZoom { Class = EGunClass.Tactical, IronSightZoom = 1.43f },
			new ClassZoom { Class = EGunClass.Sniper,   IronSightZoom = 1.43f },
		};
		[Tooltip("Look speed while aimed scales by this / the current zoom (the ADS sensitivity setting is tuned for iron sights at this zoom).")]
		public float          ReferenceAdsZoom = 1.43f;
		[Tooltip("How far behind a gun's _ADS_Eye point the eye sits when aimed with iron sights (m) (iron_sights.eye_distance_m).")]
		public float          IronSightEyeDistance = 0.0f;

		[Header("Attachments (phase 3c)")]
		[Tooltip("Written by Import Guns from the file's attachments section. An attachment's ID is its index + 1 (0 = none), so keep the order.")]
		public AttachmentData[] Attachments;

		[Header("Ammo")]
		public CaliberCap[]   CarryCaps =
		{
			new CaliberCap { Caliber = ECaliber.Cal9mm, Cap = 200 },
			new CaliberCap { Caliber = ECaliber.Cal45,  Cap = 200 },
			new CaliberCap { Caliber = ECaliber.Cal556, Cap = 150 },
			new CaliberCap { Caliber = ECaliber.Cal762, Cap = 150 },
			new CaliberCap { Caliber = ECaliber.Cal338, Cap = 50  },
			new CaliberCap { Caliber = ECaliber.Cal50,  Cap = 50  },
		};

		[Header("Trigger")]
		[Tooltip("A semi or bolt press this close before the gun is ready fires as soon as it is.")]
		public float          PressBufferTime = 0.15f;

		[Header("Recoil (phase 11e: the view springs toward each shot's measured offset)")]
		[Tooltip("Up kick (degrees) for guns with no measured pattern (snipers); it comes back down before the next shot.")]
		public float          UnmeasuredKickUp = 2.0f;
		[Tooltip("Guns with no measured pattern hold their kick this long (s) before it springs back down.")]
		public float          UnmeasuredKickHold = 0.05f;
		[Tooltip("Hip fire: the side offset after each shot = random within ± this × hip_side_range_deg.")]
		public float          HipSideFraction = 0.15f;
		[Tooltip("Hip fire: the up offset climbs hip_climb_deg_per_shot per shot up to this (deg).")]
		public float          HipClimbMax = 1.5f;
		[Tooltip("How fast the view moves toward a shot's offset (1/s; 35 gets 95% of the way in about 0.09 s).")]
		public float          RecoilSpring = 35.0f;
		[Tooltip("After the measured shots the up offset wanders this share of the gap between the gun's mean and peak, either way.")]
		public float          RecoilWanderUp = 0.5f;
		[Tooltip("On release the view settles back over the gun's ads_settle_after_release_s, but never faster than this (s).")]
		public float          RecoilSettleMin = 0.1f;

		[Header("Melee punch (key 3)")]
		public float          PunchDamage = 50.0f;
		public float          PunchReach = 1.5f;
		public float          PunchDuration = 0.5f;
		public float          PunchHitTime = 0.15f;

		[Header("Scope sway and hold breath (magnified optics only)")]
		[Tooltip("Size of the figure-eight, degrees.")]
		public float          SwayAmplitude = 0.4f;
		[Tooltip("Figure-eight speed, cycles per second.")]
		public float          SwayFrequency = 0.5f;
		[Tooltip("Seconds of steady aim per full breath; it refills over the time it was held.")]
		public float          HoldBreathTime = 4.0f;

		[Header("Scope lens (phase 11a: magnified optics zoom only inside the lens, Logan 10 Oct)")]
		[Tooltip("On: a magnified optic's zoom is drawn inside its lens by a second camera, and the main view only zooms by the gun's iron-sight zoom. Off: the whole view zooms (phase 3c).")]
		public bool           ScopeLens = true;
		[Tooltip("The lens disc sits this far in front of the optic's _ADS_Eye point (m): the rear of the 2x and 4x tubes.")]
		public float          ScopeLensOffset = 0.042f;
		[Tooltip("Lens disc radius (m).")]
		public float          ScopeLensRadius = 0.019f;
		[Tooltip("The lens picture's size in pixels (square).")]
		public int            ScopeLensResolution = 512;
		[Tooltip("Dark ring inside the lens edge, as a share of the radius.")]
		public float          ScopeLensVignette = 0.18f;
		[Tooltip("Reticle color and line width (share of the lens radius).")]
		public Color          ScopeReticleColor = new Color(0.05f, 0.05f, 0.05f, 0.95f);
		public float          ScopeReticleWidth = 0.012f;
		[Tooltip("The lens camera runs once the sights are this far up (0..1).")]
		public float          ScopeLensFromAim = 0.35f;

		[Header("Loadout")]
		[Tooltip("Guns every player spawns with (Deathmatch, Phase 3a decision: Icarus + Omicron).")]
		public Weapon[]       SpawnLoadout;
		[Tooltip("Every ammo pool starts at its cap.")]
		public bool           SpawnWithFullAmmo = true;
		[Tooltip("Guns offered by Brownout → Test → Give Weapon.")]
		public Weapon[]       TestGuns;

		[Header("First person (phase 10)")]
		[Tooltip("Logan's first-person arms (soldier_arms.fbx: the soldier's skeleton, arms only). Block arms when empty.")]
		public GameObject     FirstPersonArms;
		[Tooltip("The arms file's takes (Brownout → Setup → Set Up First Person Arms fills this), played by ViewmodelArms from the player's state.")]
		public AnimationClip[] FirstPersonArmsClips;

		// PUBLIC METHODS

		private static GunplaySettings _fallback;

		/// <summary>
		/// The asset linked from GlobalSettings. Falls back to the defaults above (with a warning) if it isn't linked yet.
		/// </summary>
		public static GunplaySettings Current
		{
			get
			{
				GunplaySettings settings = Global.Settings != null ? Global.Settings.Gunplay : null;
				if (settings != null)
					return settings;

				if (_fallback == null)
				{
					_fallback = CreateInstance<GunplaySettings>();
					Debug.LogWarning("GunplaySettings isn't linked in GlobalSettings; using the code defaults. Run Brownout → Data → Import Guns.");
				}

				return _fallback;
			}
		}

		public float GetIronSightZoom(EGunClass gunClass)
		{
			for (int i = 0; i < IronSightZoom.Length; ++i)
			{
				if (IronSightZoom[i].Class == gunClass)
					return IronSightZoom[i].IronSightZoom;
			}

			return 1.0f;
		}

		public int GetCarryCap(ECaliber caliber)
		{
			for (int i = 0; i < CarryCaps.Length; ++i)
			{
				if (CarryCaps[i].Caliber == caliber)
					return CarryCaps[i].Cap;
			}

			return 0;
		}

		/// <summary>Attachment by ID (index + 1); null for 0 or an unknown ID.</summary>
		public AttachmentData GetAttachment(int id)
		{
			return id > 0 && Attachments != null && id <= Attachments.Length ? Attachments[id - 1] : null;
		}

		public byte GetAttachmentID(AttachmentData attachment)
		{
			if (attachment == null || Attachments == null)
				return 0;

			int index = System.Array.IndexOf(Attachments, attachment);
			return index >= 0 ? (byte)(index + 1) : (byte)0;
		}

		public AttachmentData FindAttachment(string attachmentName)
		{
			if (Attachments == null)
				return null;

			for (int i = 0; i < Attachments.Length; ++i)
			{
				if (Attachments[i] != null && Attachments[i].AttachmentName == attachmentName)
					return Attachments[i];
			}

			return null;
		}

		/// <summary>
		/// Damage multiplier after a bullet has travelled this far (straight line between the two class distances).
		/// rangeMultiplier stretches both distances (Long Barrel).
		/// </summary>
		public float GetFalloff(EGunClass gunClass, float distance, float rangeMultiplier = 1.0f)
		{
			distance /= Mathf.Max(0.01f, rangeMultiplier);

			for (int i = 0; i < Falloff.Length; ++i)
			{
				ClassFalloff falloff = Falloff[i];
				if (falloff.Class != gunClass)
					continue;

				if (falloff.FullDamageDistance <= 0.0f || distance <= falloff.FullDamageDistance)
					return 1.0f;
				if (distance >= falloff.MinDamageDistance)
					return falloff.MinDamageFraction;

				float progress = (distance - falloff.FullDamageDistance) / Mathf.Max(0.01f, falloff.MinDamageDistance - falloff.FullDamageDistance);
				return Mathf.Lerp(1.0f, falloff.MinDamageFraction, progress);
			}

			return 1.0f;
		}
	}
}
