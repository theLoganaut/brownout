using System;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Phase 11c (feel): the numbers for the screen look on our map, hit feedback, camera feel, gun effects and match moments. Lives in
	/// Resources/Settings/FeelSettings.asset (made and linked by Brownout → Setup → Build Feel, which also makes the particle and sun
	/// shaft materials and adds ambient occlusion and sun shafts to the URP renderers). Everything is render only.
	/// </summary>
	[CreateAssetMenu(fileName = "FeelSettings", menuName = "TPSBR/Feel Settings")]
	public sealed class FeelSettings : ScriptableObject
	{
		// SCREEN LOOK

		[Header("Screen look: color grade (our map only)")]
		public bool    ScreenLook        = true;
		[Tooltip("Exposure in stops after tonemapping (ACES).")]
		public float   PostExposure      = 0.2f;
		public float   Contrast          = 5.0f;
		public float   Saturation        = 0.0f;
		public Color   ColorFilter       = new Color(1.0f, 0.99f, 0.97f, 1.0f);
		[Tooltip("White balance: positive = warmer.")]
		public float   Temperature       = 4.0f;
		public float   Tint              = 0.0f;
		[Tooltip("Shadows pulled slightly cool, highlights warm (lift/gain, w = brightness).")]
		public Vector4 Lift              = new Vector4(0.98f, 1.0f, 1.03f, 0.04f);
		public Vector4 Gain              = new Vector4(1.02f, 1.0f, 0.98f, 0.0f);
		public float   BloomThreshold    = 1.0f;
		public float   BloomIntensity    = 0.3f;
		public float   BloomScatter      = 0.65f;
		public float   VignetteIntensity = 0.2f;

		[Header("Screen look: daylight and sky")]
		public Color   SunColor          = new Color(1.0f, 0.93f, 0.8f, 1.0f);
		public float   SunIntensity      = 1.5f;
		[Tooltip("Sun height and heading in degrees (x = above the horizon, y = heading). Zero keeps the scene's sun.")]
		public Vector2 SunAngles         = new Vector2(42.0f, 135.0f);
		public Color   AmbientSky        = new Color(0.78f, 0.84f, 0.95f, 1.0f);
		public Color   AmbientEquator    = new Color(0.72f, 0.72f, 0.68f, 1.0f);
		public Color   AmbientGround     = new Color(0.36f, 0.33f, 0.28f, 1.0f);
		[Tooltip("Procedural skybox: thicker = bluer sky, redder horizon.")]
		public float   SkyAtmosphere     = 1.1f;
		public Color   SkyTint           = new Color(0.52f, 0.6f, 0.72f, 1.0f);
		public Color   SkyGround         = new Color(0.42f, 0.4f, 0.37f, 1.0f);
		public float   SkyExposure       = 1.25f;
		public float   SkySunSize        = 0.035f;

		[Header("Screen look: horizon haze (never over the map: it starts past where the map ends from the helicopter)")]
		public bool    Haze              = true;
		public Color   HazeColor         = new Color(0.7f, 0.76f, 0.82f, 1.0f);
		public float   HazeStart         = 900.0f;
		public float   HazeEnd           = 4000.0f;

		[Header("Screen look: sun shafts (Brownout/Sun Shafts, a URP renderer feature)")]
		public float   SunShafts         = 0.35f;
		[Tooltip("How far each ray reaches toward the sun, as a share of the way.")]
		public float   SunShaftLength    = 0.6f;
		public Color   SunShaftColor     = new Color(1.0f, 0.9f, 0.72f, 1.0f);

		[Header("Screen look: ambient occlusion (URP SSAO on the renderers, set by Build Feel)")]
		public float   AoIntensity       = 1.0f;
		public float   AoRadius          = 0.25f;

		[Header("Screen look: health")]
		[Tooltip("Below this share of health the screen loses color, down to LowHealthSaturation at 0.")]
		public float   LowHealthStart      = 0.4f;
		public float   LowHealthSaturation = -55.0f;
		[Tooltip("Downed: the screen goes this grey and dark.")]
		public float   DownedSaturation    = -80.0f;
		public float   DownedExposure      = -0.6f;

		// HIT FEEDBACK

		[Header("Hit feedback (the shooter)")]
		[Tooltip("Headshots: the marker grows by this much and turns this color.")]
		public float   HeadshotMarkerScale = 1.35f;
		public Color   HeadshotColor       = new Color(1.0f, 0.82f, 0.25f, 1.0f);
		[Tooltip("Hits on armor: the ticks turn this color.")]
		public Color   ArmorHitColor       = new Color(0.55f, 0.8f, 1.0f, 1.0f);
		[Tooltip("The ARMOR BROKEN cue under the crosshair.")]
		public float   ArmorBrokeTime      = 1.2f;
		[Tooltip("Kill confirm: a skull over the crosshair.")]
		public float   KillConfirmTime     = 1.2f;
		public float   KillConfirmSize     = 46.0f;
		[Tooltip("The marker pops to this scale on each hit and settles.")]
		public float   MarkerPop           = 1.25f;

		[Header("Hit feedback (the one hit)")]
		[Tooltip("Blood at the screen edges: alpha per point of damage, the most it builds up to, and how fast it fades (alpha per second).")]
		public float   BloodPerDamage      = 0.012f;
		public float   BloodMax            = 0.75f;
		public float   BloodFade           = 0.6f;
		[Tooltip("The hit that downs you: a red flash and a hard camera punch.")]
		public float   DownedFlash         = 0.85f;
		public float   DownedFlashTime     = 0.9f;
		[Tooltip("Your armor breaking: a white-blue flash at the edges.")]
		public Color   ArmorBreakFlash     = new Color(0.7f, 0.85f, 1.0f, 0.55f);
		public float   ArmorBreakFlashTime = 0.5f;

		// CAMERA FEEL

		[Header("Camera feel (first person, render only: shots still go where you aim)")]
		[Tooltip("Landing: the view dips this far (m) per m/s of fall speed, at most LandDipMax, and springs back.")]
		public float   LandDipPerSpeed   = 0.012f;
		public float   LandDipMax        = 0.14f;
		public float   LandDipMinSpeed   = 3.0f;
		[Tooltip("Spring (stiffness, damping) for the landing dip and the punches.")]
		public float   SpringStiffness   = 170.0f;
		public float   SpringDamping     = 16.0f;
		[Tooltip("Sprint bob: up and down (m), side (m), roll (deg), steps per second at sprint speed.")]
		public float   SprintBobHeight   = 0.028f;
		public float   SprintBobSide     = 0.018f;
		public float   SprintBobRoll     = 0.45f;
		public float   SprintBobRate     = 2.7f;
		[Tooltip("Walk bob as a share of the sprint bob (aimed: almost none).")]
		public float   WalkBobShare      = 0.35f;
		[Tooltip("Roll into a slide (deg) and while strafing (deg at full speed).")]
		public float   SlideTilt         = 4.0f;
		public float   StrafeTilt        = 1.2f;
		public float   TiltSpeed         = 8.0f;
		[Tooltip("Firing: the view kicks up (deg) and rolls a little, then springs back. By gun class: AR, SMG, Tactical, Sniper. Since phase 11e Brownout guns use recoil_feel in brownout_guns.json (punch_pitch_deg / punch_roll_deg); this is only for other weapons.")]
		public float[] FireKickPitch     = { 0.35f, 0.25f, 0.55f, 1.3f };
		public float[] FireKickRoll      = { 0.25f, 0.2f, 0.3f, 0.6f };
		[Tooltip("Aimed in, the kick is this share.")]
		public float   FireKickAimed     = 0.5f;
		[Tooltip("Hit: the view punches away from where the shot came from (deg per point of damage, at most HitPunchMax).")]
		public float   HitPunchPerDamage = 0.06f;
		public float   HitPunchMax       = 3.0f;
		public float   DownedPunch       = 7.0f;

		// GUN EFFECTS

		[Header("Gun effects: by gun class (AR, SMG, Tactical, Sniper)")]
		public float[] MuzzleFlashScale  = { 1.0f, 0.8f, 1.15f, 1.6f };
		[Tooltip("Smoke puffs per shot (fractions build up, so 0.5 = every other shot).")]
		public float[] MuzzleSmoke       = { 0.6f, 0.45f, 0.9f, 3.0f };
		[Tooltip("Every Nth bullet draws a tracer (1 = every bullet).")]
		public int[]   TracerEvery       = { 3, 3, 2, 1 };
		[Tooltip("Shell length (m) and how far behind the muzzle the ejection port is (m).")]
		public float[] ShellLength       = { 0.045f, 0.03f, 0.055f, 0.075f };
		public float[] EjectBack         = { 0.42f, 0.28f, 0.48f, 0.62f };
		[Tooltip("Shells fly right and up at this speed (m/s), spinning.")]
		public float   ShellSpeed        = 2.6f;
		public float   ShellLife         = 1.6f;
		[Tooltip("A suppressor cuts the smoke to this share.")]
		public float   SuppressedSmoke   = 0.35f;
		[Tooltip("Also spawn the sample's bullet impact (KriptoFX concrete, dark smoke) on every hit, as before 11c.")]
		public bool    SampleImpacts     = false;

		[Header("Gun effects: impacts by surface (Concrete, Dirt, Wood, Metal, Water, Body)")]
		public Surface[] Surfaces =
		{
			new Surface { Name = "Concrete", Dust = new Color(0.68f, 0.66f, 0.62f, 0.8f), DustCount = 6,  DustSize = 0.35f, Chips = 6,  ChipColor = new Color(0.55f, 0.54f, 0.5f), Sparks = 2  },
			new Surface { Name = "Dirt",     Dust = new Color(0.45f, 0.36f, 0.25f, 0.85f), DustCount = 8, DustSize = 0.45f, Chips = 7,  ChipColor = new Color(0.3f, 0.23f, 0.15f), Sparks = 0 },
			new Surface { Name = "Wood",     Dust = new Color(0.62f, 0.5f, 0.35f, 0.7f), DustCount = 4,  DustSize = 0.3f,  Chips = 8,  ChipColor = new Color(0.6f, 0.45f, 0.28f), Sparks = 0 },
			new Surface { Name = "Metal",    Dust = new Color(0.5f, 0.5f, 0.52f, 0.5f),  DustCount = 3,  DustSize = 0.25f, Chips = 0,  ChipColor = Color.grey,                  Sparks = 14 },
			new Surface { Name = "Water",    Dust = new Color(0.85f, 0.9f, 0.95f, 0.8f), DustCount = 10, DustSize = 0.4f,  Chips = 0,  ChipColor = Color.white,                 Sparks = 0  },
			new Surface { Name = "Body",     Dust = new Color(0.45f, 0.03f, 0.03f, 0.9f), DustCount = 6, DustSize = 0.28f, Chips = 0,  ChipColor = Color.red,                   Sparks = 0  },
		};

		[Header("Gun effects: materials and meshes (made by Build Feel)")]
		public Material SmokeMaterial;
		public Material SparkMaterial;
		public Material ChipMaterial;
		public Material ShellMaterial;
		public Mesh     ShellMesh;
		public Mesh     ChipMesh;
		public Material SunShaftMaterial;

		// MATCH MOMENTS

		[Header("Match moments")]
		[Tooltip("ELIMINATE ALL OTHER PLAYERS: shown this long once you're in the helicopter.")]
		public float   IntroBannerTime   = 5.0f;
		[Tooltip("Fleet shot: the first seconds in the helicopter the camera watches it from the side, then swings in behind.")]
		public float   FleetShotTime     = 4.0f;
		[Tooltip("Fleet shot camera, from the helicopter in its own space (right, up, forward).")]
		public Vector3 FleetShotOffset   = new Vector3(-55.0f, 8.0f, 30.0f);
		public float   FleetShotBlend    = 1.5f;
		[Tooltip("Zone announcements (ZONE CLOSING, SHRINKING NOW) and YOUR SQUAD IS OUT in the middle of the screen.")]
		public float   AnnounceTime      = 3.5f;

		// PUBLIC MEMBERS

		[Serializable]
		public sealed class Surface
		{
			public string Name;
			public Color  Dust;
			public int    DustCount;
			public float  DustSize;
			public int    Chips;
			public Color  ChipColor;
			public int    Sparks;
		}

		private static FeelSettings _current;
		private static FeelSettings _defaults;

		/// <summary>The asset in Resources/Settings, or the defaults above until Build Feel has made it.</summary>
		public static FeelSettings Current
		{
			get
			{
				if (_current == null)
				{
					_current = Resources.Load<FeelSettings>("Settings/FeelSettings");
				}
				if (_current == null)
				{
					if (_defaults == null)
					{
						_defaults = CreateInstance<FeelSettings>();
					}
					return _defaults;
				}

				return _current;
			}
		}

		// PUBLIC METHODS

		public static float ByClass(float[] values, EGunClass gunClass, float fallback = 1.0f)
		{
			int index = (int)gunClass;
			return values != null && index >= 0 && index < values.Length ? values[index] : fallback;
		}

		public static int ByClass(int[] values, EGunClass gunClass, int fallback = 1)
		{
			int index = (int)gunClass;
			return values != null && index >= 0 && index < values.Length ? values[index] : fallback;
		}

		public Surface GetSurface(ESurface surface)
		{
			int index = (int)surface;
			return Surfaces != null && index >= 0 && index < Surfaces.Length ? Surfaces[index] : null;
		}
	}

	/// <summary>Phase 11c: what a bullet hit, for its impact (index into FeelSettings.Surfaces). From the collider's tag, else concrete.</summary>
	public enum ESurface
	{
		Concrete = 0,
		Dirt     = 1,
		Wood     = 2,
		Metal    = 3,
		Water    = 4,
		Body     = 5,
	}
}
