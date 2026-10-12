using UnityEngine;

namespace TPSBR
{
	public class RuntimeSettings
	{
		// CONSTANTS

		public const string KEY_MUSIC_VOLUME     = "MusicVolume";
		public const string KEY_EFFECTS_VOLUME   = "EffectsVolume";
		public const string KEY_WINDOWED         = "Windowed";
		public const string KEY_RESOLUTION       = "Resolution";
		public const string KEY_GRAPHICS_QUALITY = "GraphicsQuality";
		public const string KEY_LIMIT_FPS        = "LimitFPS";
		public const string KEY_TARGET_FPS       = "TargetFPS";
		public const string KEY_REGION           = "Region";
		public const string KEY_SENSITIVITY      = "Sensitivity";
		public const string KEY_AIM_SENSITIVITY  = "AimSensitivity";
		public const string KEY_VSYNC            = "VSync";
		public const string KEY_FIELD_OF_VIEW    = "FieldOfView";
		// Brownout (phase 6b, HUD & UI page): crouch, sprint and aim are holds by default, each with a toggle option
		public const string KEY_TOGGLE_CROUCH    = "ToggleCrouch";
		public const string KEY_TOGGLE_SPRINT    = "ToggleSprint";
		public const string KEY_TOGGLE_AIM       = "ToggleAim";

		// Brownout: horizontal FOV in degrees at 16:9 (CoD convention). Range and default decided by Logan on 3 Oct.
		public const float  FIELD_OF_VIEW_MIN     = 90.0f;
		public const float  FIELD_OF_VIEW_MAX     = 110.0f;
		public const float  FIELD_OF_VIEW_DEFAULT = 90.0f;

		// PUBLIC MEMBERS

		public Options Options => _options;

		public float  MusicVolume    { get { return _options.GetFloat(KEY_MUSIC_VOLUME); }     set { _options.Set(KEY_MUSIC_VOLUME, value, false); } }
		public float  EffectsVolume  { get { return _options.GetFloat(KEY_EFFECTS_VOLUME); }   set { _options.Set(KEY_EFFECTS_VOLUME, value, false); } }

		public bool   Windowed        { get { return _options.GetBool(KEY_WINDOWED); }         set { _options.Set(KEY_WINDOWED, value, false); } }
		public int    Resolution      { get { return _options.GetInt(KEY_RESOLUTION); }        set { _options.Set(KEY_RESOLUTION, value, false); } }
		public int    GraphicsQuality { get { return _options.GetInt(KEY_GRAPHICS_QUALITY); }  set { _options.Set(KEY_GRAPHICS_QUALITY, value, false); } }
		public bool   VSync           { get { return _options.GetBool(KEY_VSYNC); }            set { _options.Set(KEY_VSYNC, value, false); } }
		public bool   LimitFPS        { get { return _options.GetBool(KEY_LIMIT_FPS); }        set { _options.Set(KEY_LIMIT_FPS, value, false); } }
		public int    TargetFPS       { get { return _options.GetInt(KEY_TARGET_FPS); }        set { _options.Set(KEY_TARGET_FPS, value, false); } }
		public float  Sensitivity     { get { return _options.GetFloat(KEY_SENSITIVITY); }     set { _options.Set(KEY_SENSITIVITY, value, false); } }
		public float  AimSensitivity  { get { return _options.GetFloat(KEY_AIM_SENSITIVITY); } set { _options.Set(KEY_AIM_SENSITIVITY, value, false); } }
		public float  FieldOfView     { get { return _options.GetFloat(KEY_FIELD_OF_VIEW); }   set { _options.Set(KEY_FIELD_OF_VIEW, value, false); } }
		public bool   ToggleCrouch    { get { return _options.GetBool(KEY_TOGGLE_CROUCH); }    set { _options.Set(KEY_TOGGLE_CROUCH, value, true); } }
		public bool   ToggleSprint    { get { return _options.GetBool(KEY_TOGGLE_SPRINT); }    set { _options.Set(KEY_TOGGLE_SPRINT, value, true); } }
		public bool   ToggleAim       { get { return _options.GetBool(KEY_TOGGLE_AIM); }       set { _options.Set(KEY_TOGGLE_AIM, value, true); } }

		public string Region          { get { return _options.GetString(KEY_REGION); }         set { _options.Set(KEY_REGION, value, true); } }


		// PRIVATE MEMBERS

		private Options _options = new Options();

		// PUBLIC METHODS

		public void Initialize(GlobalSettings settings)
		{
			AddRuntimeDefaults(settings.DefaultOptions);

			_options.Initialize(settings.DefaultOptions, true, "Options.V3.");

			Windowed = Screen.fullScreen == false;
			GraphicsQuality = QualitySettings.GetQualityLevel();
			Resolution = GetCurrentResolutionIndex();

			QualitySettings.vSyncCount = VSync == true ? 1 : 0;
			Application.targetFrameRate = LimitFPS == true ? TargetFPS : -1;

			_options.SaveChanges();
		}

		// PRIVATE MEMBERS

		private static void AddRuntimeDefaults(OptionsData optionsData)
		{
			optionsData.Initialize();

			var fieldOfView = new OptionsValue(KEY_FIELD_OF_VIEW, FIELD_OF_VIEW_DEFAULT);
			fieldOfView.FloatValue.MinValue = FIELD_OF_VIEW_MIN;
			fieldOfView.FloatValue.MaxValue = FIELD_OF_VIEW_MAX;
			optionsData.AddRuntimeValue(fieldOfView);

			optionsData.AddRuntimeValue(new OptionsValue(KEY_TOGGLE_CROUCH, false));
			optionsData.AddRuntimeValue(new OptionsValue(KEY_TOGGLE_SPRINT, false));
			optionsData.AddRuntimeValue(new OptionsValue(KEY_TOGGLE_AIM, false));
		}

		private int GetCurrentResolutionIndex()
		{
			var resolutions = Screen.resolutions;
			if (resolutions == null || resolutions.Length == 0)
				return -1;

			int currentWidth = Mathf.RoundToInt(Screen.width);
			int currentHeight = Mathf.RoundToInt(Screen.height);
			int defaultRefreshRate = Mathf.RoundToInt((float)resolutions[^1].refreshRateRatio.value);

			for (int i = 0; i < resolutions.Length; i++)
			{
				var resolution = resolutions[i];

				if (resolution.width == currentWidth && resolution.height == currentHeight && Mathf.RoundToInt((float)resolution.refreshRateRatio.value) == defaultRefreshRate)
					return i;
			}

			return -1;
		}
	}
}
