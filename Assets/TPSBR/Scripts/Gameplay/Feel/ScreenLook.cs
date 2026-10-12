namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.Rendering.Universal;

	/// <summary>
	/// Phase 11c (screen look): on our map, a global post-processing volume built in code from FeelSettings (ACES, a warm grade, slight
	/// bloom, vignette), the daylight (sun color, height and heading, trilight ambient), a tuned copy of the procedural sky, haze only past
	/// the map, and the sun shafts' sun position for the Brownout/Sun Shafts renderer pass. The screen also loses color as the observed
	/// player's health drops, and goes grey and dark while downed. Made by Gameplay when the Brownout map loads; render only.
	/// </summary>
	public sealed class ScreenLook : MonoBehaviour
	{
		// PUBLIC MEMBERS

		public static readonly int SunShaftsID      = Shader.PropertyToID("_BrownoutSunShafts");
		public static readonly int SunScreenID      = Shader.PropertyToID("_BrownoutSunScreen");
		public static readonly int SunShaftColorID  = Shader.PropertyToID("_BrownoutSunShaftColor");

		/// <summary>The look on screen right now, for checks.</summary>
		public static string Readout { get; private set; } = "off";

		// PRIVATE MEMBERS

		private SceneContext      _context;
		private Volume            _volume;
		private VolumeProfile     _profile;
		private ColorAdjustments  _color;
		private Light             _sun;
		private Material          _sky;
		private float             _saturation;
		private float             _exposure;

		// PUBLIC METHODS

		public static ScreenLook Create(SceneContext context, UnityEngine.SceneManagement.Scene scene)
		{
			FeelSettings settings = FeelSettings.Current;
			if (settings.ScreenLook == false)
			{
				Shader.SetGlobalFloat(SunShaftsID, 0.0f);
				return null;
			}

			var go = new GameObject("ScreenLook");
			UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

			var look = go.AddComponent<ScreenLook>();
			look._context = context;
			look.Build(scene);
			return look;
		}

		// MONOBEHAVIOUR

		private void LateUpdate()
		{
			FeelSettings settings = FeelSettings.Current;

			// Multipeer: volumes and shader globals are shared by every peer in the editor, so only the visible peer's counts
			bool visible = _context == null || _context.IsVisible == true;
			_volume.weight = visible == true ? 1.0f : 0.0f;
			if (visible == false)
				return;

			// Health: color drains away as it drops, grey and dark while downed
			float saturation = settings.Saturation;
			float exposure   = settings.PostExposure;
			Agent agent      = _context != null ? _context.ObservedAgent : null;
			if (agent != null && agent.Object != null && agent.Object.IsValid == true && agent.Health != null)
			{
				Health health = agent.Health;
				if (health.IsDowned == true)
				{
					saturation = settings.DownedSaturation;
					exposure  += settings.DownedExposure;
				}
				else if (health.IsAlive == true)
				{
					float share = health.CurrentHealth / Mathf.Max(1.0f, health.MaxHealth);
					float low   = Mathf.Clamp01((settings.LowHealthStart - share) / Mathf.Max(0.01f, settings.LowHealthStart));
					saturation  = Mathf.Lerp(settings.Saturation, settings.LowHealthSaturation, low);
				}
			}

			_saturation = Mathf.MoveTowards(_saturation, saturation, Time.deltaTime * 120.0f);
			_exposure   = Mathf.MoveTowards(_exposure, exposure, Time.deltaTime * 2.0f);
			_color.saturation.value   = _saturation;
			_color.postExposure.value = _exposure;

			// Sun shafts: where the sun is on screen, faded out as it leaves the view
			Camera camera = _context != null && _context.Camera != null ? _context.Camera.Camera : null;
			float  shafts = 0.0f;
			if (camera != null && _sun != null && camera.isActiveAndEnabled == true)
			{
				Vector3 toSun  = -_sun.transform.forward;
				Vector3 screen = camera.WorldToViewportPoint(camera.transform.position + toSun * 1000.0f);
				float   facing = Vector3.Dot(camera.transform.forward, toSun);
				float   edge   = Mathf.Clamp01(1.5f - Mathf.Max(Mathf.Abs(screen.x - 0.5f), Mathf.Abs(screen.y - 0.5f)) * 2.0f);
				shafts = settings.SunShafts * Mathf.Clamp01(facing * 3.0f) * edge;
				Shader.SetGlobalVector(SunScreenID, new Vector4(screen.x, screen.y, settings.SunShaftLength, 0.0f));
			}
			Shader.SetGlobalFloat(SunShaftsID, shafts);
			Shader.SetGlobalColor(SunShaftColorID, settings.SunShaftColor);

			Readout = $"saturation {_saturation:F0}, exposure {_exposure:F2}, shafts {shafts:F2}";
		}

		private void OnDestroy()
		{
			Shader.SetGlobalFloat(SunShaftsID, 0.0f);
			Readout = "off";

			if (_profile != null)
			{
				Destroy(_profile);
			}
			if (_sky != null)
			{
				Destroy(_sky);
			}
		}

		// PRIVATE METHODS

		private void Build(UnityEngine.SceneManagement.Scene scene)
		{
			FeelSettings settings = FeelSettings.Current;

			// The grade: a global volume over everything in this peer's scene
			_profile = ScriptableObject.CreateInstance<VolumeProfile>();
			_profile.name = "BrownoutScreenLook";

			var tonemapping = _profile.Add<Tonemapping>(true);
			tonemapping.mode.value = TonemappingMode.ACES;

			_color = _profile.Add<ColorAdjustments>(true);
			_color.postExposure.value = settings.PostExposure;
			_color.contrast.value     = settings.Contrast;
			_color.saturation.value   = settings.Saturation;
			_color.colorFilter.value  = settings.ColorFilter;
			_saturation = settings.Saturation;
			_exposure   = settings.PostExposure;

			var whiteBalance = _profile.Add<WhiteBalance>(true);
			whiteBalance.temperature.value = settings.Temperature;
			whiteBalance.tint.value        = settings.Tint;

			var liftGammaGain = _profile.Add<LiftGammaGain>(true);
			liftGammaGain.lift.value = settings.Lift;
			liftGammaGain.gain.value = settings.Gain;

			var bloom = _profile.Add<Bloom>(true);
			bloom.threshold.value = settings.BloomThreshold;
			bloom.intensity.value = settings.BloomIntensity;
			bloom.scatter.value   = settings.BloomScatter;

			var vignette = _profile.Add<Vignette>(true);
			vignette.intensity.value  = settings.VignetteIntensity;
			vignette.smoothness.value = 0.4f;

			_volume = gameObject.AddComponent<Volume>();
			_volume.isGlobal      = true;
			_volume.priority      = 10.0f;
			_volume.sharedProfile = _profile;

			// Daylight: the scene's sun, warmer and higher
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Light light in root.GetComponentsInChildren<Light>(true))
				{
					if (light.type == LightType.Directional)
					{
						_sun = light;
						break;
					}
				}
				if (_sun != null)
					break;
			}

			if (_sun != null)
			{
				_sun.color     = settings.SunColor;
				_sun.intensity = settings.SunIntensity;
				if (settings.SunAngles != Vector2.zero)
				{
					_sun.transform.rotation = Quaternion.Euler(settings.SunAngles.x, settings.SunAngles.y, 0.0f);
				}
				RenderSettings.sun = _sun;
			}

			RenderSettings.ambientMode         = AmbientMode.Trilight;
			RenderSettings.ambientSkyColor     = settings.AmbientSky;
			RenderSettings.ambientEquatorColor = settings.AmbientEquator;
			RenderSettings.ambientGroundColor  = settings.AmbientGround;

			// Sky: a copy of the procedural skybox, so the sample's material stays as it is
			if (RenderSettings.skybox != null && RenderSettings.skybox.shader != null && RenderSettings.skybox.shader.name == "Skybox/Procedural")
			{
				_sky = new Material(RenderSettings.skybox) { name = "BrownoutSky" };
				_sky.SetFloat("_AtmosphereThickness", settings.SkyAtmosphere);
				_sky.SetColor("_SkyTint", settings.SkyTint);
				_sky.SetColor("_GroundColor", settings.SkyGround);
				_sky.SetFloat("_Exposure", settings.SkyExposure);
				_sky.SetFloat("_SunSize", settings.SkySunSize);
				RenderSettings.skybox = _sky;
			}

			// Haze only past the map's far corners from the helicopter
			if (settings.Haze == true)
			{
				RenderSettings.fog              = true;
				RenderSettings.fogMode          = FogMode.Linear;
				RenderSettings.fogColor         = settings.HazeColor;
				RenderSettings.fogStartDistance = settings.HazeStart;
				RenderSettings.fogEndDistance   = settings.HazeEnd;
			}

			// The far haze needs the camera to see that far
			if (_context != null && _context.Camera != null && _context.Camera.Camera.farClipPlane < settings.HazeEnd)
			{
				_context.Camera.Camera.farClipPlane = Mathf.Max(_context.Camera.Camera.farClipPlane, 2500.0f);
			}

			DynamicGI.UpdateEnvironment();
		}
	}
}
