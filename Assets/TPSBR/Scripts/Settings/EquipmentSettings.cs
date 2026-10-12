namespace TPSBR
{
	using UnityEngine;

	/// <summary>Equipment, used with E (phase 5b). The value is LootStack.Index for EItemKind.Equipment.</summary>
	public enum EEquipment : byte
	{
		Frag       = 0,
		Smoke      = 1,
		Flash      = 2,
		Concussion = 3,
		Grapple    = 4,
	}

	/// <summary>Gear items, toggled with Left Ctrl (phase 5b). The value is LootStack.Index for EItemKind.Gear.</summary>
	public enum EGear : byte
	{
		DoubleJump = 0,
		DoubleTime = 1,
		Ninja      = 2,
		SixthSense = 3,
		Camouflage = 4,
		O2Max      = 5,
	}

	/// <summary>
	/// Equipment and gear numbers (phase 5b), from the Items, Gear & Inventory page, gameplay_timings.json (equipment) and the
	/// Phase 5b spec (Logan's answers and Claude's defaults). Lives in Resources/Settings/EquipmentSettings.asset, linked from
	/// GlobalSettings.Equipment (Brownout → Setup → Build Equipment Prefabs).
	/// </summary>
	[CreateAssetMenu(fileName = "EquipmentSettings", menuName = "TPSBR/Equipment Settings")]
	public sealed class EquipmentSettings : ScriptableObject
	{
		// CONSTANTS

		public const int EquipmentCount = 5;
		public const int GearCount      = 6;

		[Header("Throwing (gameplay_timings.json, equipment)")]
		[Tooltip("From pressing E to the grenade leaving the hand (smoke_press_to_release_s). A cooked frag leaves when E is let go, but never sooner.")]
		public float PressToRelease  = 0.65f;
		[Tooltip("From pressing E to the gun being ready again (smoke_press_to_gun_ready_s). Measured from the release when E is held longer.")]
		public float PressToGunReady = 1.4f;
		[Tooltip("Throw speed (m/s) along the look direction, plus your own movement (the sample's 35).")]
		public float ThrowSpeed      = 35.0f;
		[Tooltip("The grenade leaves this far in front of the eye (m), or at the eye if a wall is closer.")]
		public float ThrowForward    = 0.4f;

		[Header("Fuses (s)")]
		[Tooltip("Frag: from the pin coming out (pressing E). Holding E cooks it (Logan, 5 Oct); held this long, it goes off in your hand.")]
		public float FragFuse       = 3.0f;
		[Tooltip("Smoke: from the release (smoke_release_to_pop_s).")]
		public float SmokeFuse      = 1.8f;
		[Tooltip("Flash: from the release (Claude's default).")]
		public float FlashFuse      = 1.5f;
		[Tooltip("Concussion: from the release (Claude's default).")]
		public float ConcussionFuse = 1.5f;

		[Header("Shared falloff (frag, flash, concussion)")]
		[Tooltip("Full effect within this distance (m).")]
		public float FullEffectRadius = 6.0f;
		[Tooltip("Down to EdgeEffect at this distance (m); nothing beyond it.")]
		public float MaxRadius        = 10.0f;
		[Tooltip("Share of the full effect at MaxRadius.")]
		public float EdgeEffect       = 0.25f;

		[Header("Frag")]
		public float FragDamage       = 120.0f;
		[Tooltip("Armor cuts frag damage by its body percentage and wears by the full incoming damage, like bullets (Logan, 5 Oct).")]
		public bool  ArmorCutsFrags   = true;

		[Header("Smoke")]
		[Tooltip("Cloud diameter (m), Claude's default.")]
		public float SmokeDiameter    = 6.0f;
		[Tooltip("Time to grow to full size after it pops (smoke_full_cloud_s).")]
		public float SmokeGrowTime    = 2.2f;
		[Tooltip("How long the cloud lasts at full size (s), then it fades over SmokeFadeTime.")]
		public float SmokeDuration    = 15.0f;
		public float SmokeFadeTime    = 2.0f;

		[Header("Flash")]
		[Tooltip("Whiteout length at full effect (s, the sample's 4). Shorter with distance and when facing away.")]
		public float FlashMaxDuration = 4.0f;
		[Tooltip("Facing away from the flash multiplies its strength and length down to this.")]
		public float FlashFacingAwayMultiplier = 0.3f;

		[Header("Concussion")]
		[Tooltip("Dizzy length at full effect (s), shorter with distance.")]
		public float ConcussionMaxDuration = 4.0f;
		[Tooltip("Look and movement speed at full effect for the first ConcussionSlowTime (Logan, 5 Oct: 50% for 2 s), easing back by the end.")]
		public float ConcussionSlow        = 0.5f;
		public float ConcussionSlowTime    = 2.0f;

		[Header("Grapple (gameplay_timings.json, equipment)")]
		[Tooltip("From pressing E to the hook firing (grapple_deploy_to_fire_s).")]
		public float GrappleDeployTime  = 0.5f;
		[Tooltip("The hook's flight to the wall (s).")]
		public float GrappleHookTime    = 0.15f;
		[Tooltip("The pull (grapple_travel_s): you cover the rope in this time, faster for a longer rope.")]
		public float GrapplePullTime    = 1.3f;
		[Tooltip("From the end of the pull to the gun being ready (grapple_total_to_gun_ready_s 2.5 minus the rest).")]
		public float GrappleRecoverTime = 0.55f;
		[Tooltip("A miss: the gun comes back this long after the hook fires.")]
		public float GrappleMissRecover = 0.35f;
		[Tooltip("Hooks on the world (ground, walls, roofs) up to this far (m), Claude's default.")]
		public float GrappleRange       = 40.0f;
		[Tooltip("Pulls in one grapple (Logan, 5 Oct).")]
		public int   GrapplePulls       = 3;
		[Tooltip("A hook on a wall pulls the feet to this far below the hook spot (m), so the end lift carries you over a ledge you hooked near its top.")]
		public float GrappleWallDrop    = 0.5f;
		[Tooltip("The pull lets go this close to the hook spot (m). Close enough at a wall for the mantle to reach the ledge.")]
		public float GrappleStopDistance = 0.3f;
		[Tooltip("Lift at the end of the pull (m/s up), so you land on a ledge or roof.")]
		public float GrappleEndLift     = 5.0f;
		[Tooltip("Forward speed kept at the end of the pull (m/s).")]
		public float GrappleEndForward  = 3.0f;

		[Header("Gear items")]
		[Tooltip("Seconds of use in each gear item; switching off keeps what's left, and at 0 it breaks.")]
		public float GearSeconds          = 10.0f;
		[Tooltip("Double Time: walk, sprint, crouch, aiming and crawling speeds (not sliding, skipping or dashing).")]
		public float DoubleTimeMultiplier = 1.4f;
		[Tooltip("Ninja: footsteps and sprinting are heard this share as far.")]
		public float NinjaRangeMultiplier = 0.5f;
		[Tooltip("O2 Max: hold breath this long instead of GunplaySettings.HoldBreathTime (s).")]
		public float O2MaxBreathTime      = 8.0f;
		[Tooltip("O2 Max: recoil multiplier while it's on.")]
		public float O2MaxRecoil          = 0.75f;
		[Tooltip("Sixth Sense: a marked enemy's spot blinks this often (s).")]
		public float SixthSenseInterval   = 2.0f;
		[Tooltip("Sixth Sense: each blink fades over this long (s).")]
		public float SixthSenseFade       = 1.0f;
		[Tooltip("Camouflage: how visible the body stays while still (0 = gone, 1 = normal), tuned later.")]
		public float CamouflageVisibility = 0.15f;
		[Tooltip("Camouflage: still means slower than this (m/s)...")]
		public float CamouflageStillSpeed = 0.2f;
		[Tooltip("...for this long (s).")]
		public float CamouflageStillTime  = 0.3f;

		[Header("Sound ranges (Open Questions: starting values, tuned later)")]
		[Tooltip("Footsteps are heard this far (m).")]
		public float FootstepRange = 10.0f;
		[Tooltip("Sprinting footsteps are heard this far (m).")]
		public float SprintRange   = 20.0f;

		[Header("Test dummies")]
		[Tooltip("Dummies spawn with one of these in the Equipment slot, so their stash has it.")]
		public EEquipment DummyEquipment = EEquipment.Frag;
		[Tooltip("...and this gear item.")]
		public EGear      DummyGear      = EGear.DoubleTime;

		[Header("Loot colors (blocks until phase 10)")]
		public Color[] EquipmentColors =
		{
			new Color(0.30f, 0.38f, 0.22f), // Frag: olive
			new Color(0.75f, 0.75f, 0.75f), // Smoke: light grey
			new Color(0.95f, 0.95f, 0.55f), // Flash: pale yellow
			new Color(0.30f, 0.65f, 0.85f), // Concussion: light blue
			new Color(0.85f, 0.45f, 0.10f), // Grapple: orange
		};
		public Color[] GearColors =
		{
			new Color(0.20f, 0.85f, 0.85f), // Double Jump: cyan
			new Color(0.95f, 0.85f, 0.10f), // Double Time: yellow
			new Color(0.15f, 0.15f, 0.18f), // Ninja: black
			new Color(0.85f, 0.20f, 0.55f), // Sixth Sense: magenta
			new Color(0.35f, 0.55f, 0.30f), // Camouflage: green
			new Color(0.30f, 0.50f, 0.95f), // O2 Max: blue
		};

		[Header("Prefabs (Brownout → Setup → Build Equipment Prefabs)")]
		[Tooltip("The thrown grenade for each equipment item (frag, smoke, flash, concussion); the grapple has none.")]
		public KinematicProjectile[] GrenadePrefabs = new KinematicProjectile[4];

		// PUBLIC MEMBERS

		private static EquipmentSettings _fallback;

		/// <summary>The asset linked from GlobalSettings. Falls back to the defaults above (with a warning) if it isn't linked yet.</summary>
		public static EquipmentSettings Current
		{
			get
			{
				EquipmentSettings settings = Global.Settings != null ? Global.Settings.Equipment : null;
				if (settings != null)
					return settings;

				if (_fallback == null)
				{
					_fallback = CreateInstance<EquipmentSettings>();
					Debug.LogWarning("EquipmentSettings isn't linked in GlobalSettings; using the code defaults. Run Brownout → Setup → Build Equipment Prefabs.");
				}

				return _fallback;
			}
		}

		// PUBLIC METHODS

		/// <summary>The shared falloff: 1 within FullEffectRadius, down to EdgeEffect at MaxRadius, 0 beyond.</summary>
		public float GetFalloff(float distance)
		{
			// 1 cm of slack at the edge, so a target standing exactly at MaxRadius gets EdgeEffect
			if (distance <= FullEffectRadius)
				return 1.0f;
			if (distance > MaxRadius + 0.01f)
				return 0.0f;

			return Mathf.Lerp(1.0f, EdgeEffect, Mathf.Clamp01((distance - FullEffectRadius) / Mathf.Max(0.001f, MaxRadius - FullEffectRadius)));
		}

		/// <summary>Fuse after the release (the frag's runs from the press; see FragFuse).</summary>
		public float GetFuse(EEquipment equipment)
		{
			switch (equipment)
			{
				case EEquipment.Frag:       return FragFuse;
				case EEquipment.Smoke:      return SmokeFuse;
				case EEquipment.Flash:      return FlashFuse;
				case EEquipment.Concussion: return ConcussionFuse;
				default:                    return 0.0f;
			}
		}

		public static bool IsGrenade(EEquipment equipment) => equipment != EEquipment.Grapple;

		public KinematicProjectile GetGrenadePrefab(EEquipment equipment)
		{
			int index = (int)equipment;
			return GrenadePrefabs != null && index >= 0 && index < GrenadePrefabs.Length ? GrenadePrefabs[index] : null;
		}

		public Color GetEquipmentColor(EEquipment equipment)
		{
			int index = (int)equipment;
			return EquipmentColors != null && index < EquipmentColors.Length ? EquipmentColors[index] : Color.gray;
		}

		public Color GetGearColor(EGear gear)
		{
			int index = (int)gear;
			return GearColors != null && index < GearColors.Length ? GearColors[index] : Color.gray;
		}

		public static string GetName(EEquipment equipment)
		{
			switch (equipment)
			{
				case EEquipment.Frag:       return "Frag Grenade";
				case EEquipment.Smoke:      return "Smoke Grenade";
				case EEquipment.Flash:      return "Flash Grenade";
				case EEquipment.Concussion: return "Concussion Grenade";
				case EEquipment.Grapple:    return "Grapple";
				default:                    return equipment.ToString();
			}
		}

		public static string GetName(EGear gear)
		{
			switch (gear)
			{
				case EGear.DoubleJump: return "Double Jump";
				case EGear.DoubleTime: return "Double Time";
				case EGear.Ninja:      return "Ninja";
				case EGear.SixthSense: return "Sixth Sense";
				case EGear.Camouflage: return "Camouflage";
				case EGear.O2Max:      return "O2 Max";
				default:               return gear.ToString();
			}
		}

		public string GetDescription(EEquipment equipment)
		{
			switch (equipment)
			{
				case EEquipment.Frag:       return $"{FragDamage:F0} damage within {FullEffectRadius:F0} m, {FragDamage * EdgeEffect:F0} at {MaxRadius:F0} m. Hold E to cook it ({FragFuse:0.#} s fuse).";
				case EEquipment.Smoke:      return $"A {SmokeDiameter:F0} m cloud for {SmokeDuration:F0} s, popping {SmokeFuse:0.#} s after the throw.";
				case EEquipment.Flash:      return $"Whites out anyone looking at it, up to {FlashMaxDuration:0.#} s within {FullEffectRadius:F0} m, less up to {MaxRadius:F0} m.";
				case EEquipment.Concussion: return $"Dizzy for up to {ConcussionMaxDuration:0.#} s and slowed to {ConcussionSlow * 100.0f:F0}% for the first {ConcussionSlowTime:0.#} s.";
				case EEquipment.Grapple:    return $"Fires a hook up to {GrappleRange:F0} m and pulls you to it in {GrapplePullTime:0.#} s. Holds {GrapplePulls} pulls.";
				default:                    return string.Empty;
			}
		}

		public string GetDescription(EGear gear)
		{
			switch (gear)
			{
				case EGear.DoubleJump: return "A second jump in the air.";
				case EGear.DoubleTime: return $"Walk, sprint, crouch and aim {DoubleTimeMultiplier:0.##}x faster (not sliding, skipping or dashing).";
				case EGear.Ninja:      return $"Your footsteps are heard {NinjaRangeMultiplier * 100.0f:F0}% as far.";
				case EGear.SixthSense: return $"Enemies firing unsuppressed shots blink on your minimap every {SixthSenseInterval:0.#} s.";
				case EGear.Camouflage: return "Standing still makes you see-through.";
				case EGear.O2Max:      return $"Hold your breath for {O2MaxBreathTime:0.#} s; recoil {O2MaxRecoil * 100.0f:F0}% of normal.";
				default:               return string.Empty;
			}
		}
	}
}
