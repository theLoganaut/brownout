namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.Rendering.Universal;

	/// <summary>
	/// Phase 11a (Logan, 10 Oct): a magnified optic zooms only inside its lens. While aimed, a second camera at the eye renders the world
	/// at the optic's zoom into a render texture, shown on a disc at the scope's eyepiece on the viewmodel (Brownout/Scope Lens: a dark
	/// edge ring and the reticle). The main view only zooms by the gun's iron-sight zoom (Character), so the scope body and the world around
	/// the glass stay at normal size and there's no black around the reticle. The lens camera's field of view comes from how big the disc
	/// looks on screen, so what's in the glass is exactly AdsZoom times the hip view. Only the local player's viewmodel makes one.
	/// </summary>
	public sealed class ScopeLens
	{
		// PRIVATE MEMBERS

		private readonly Transform _disc;
		private readonly Material  _material;
		private Camera             _camera;
		private RenderTexture      _texture;
		private bool               _active;

		private static Mesh        _quad;
		private static readonly int _lensTexId      = Shader.PropertyToID("_LensTex");
		private static readonly int _vignetteId     = Shader.PropertyToID("_Vignette");
		private static readonly int _reticleColorId = Shader.PropertyToID("_ReticleColor");
		private static readonly int _reticleWidthId = Shader.PropertyToID("_ReticleWidth");

		// CONSTRUCTORS

		/// <param name="parent">The viewmodel root (the disc is moved onto the optic each frame).</param>
		public ScopeLens(Transform parent, int layer, Material template)
		{
			var disc = new GameObject("ScopeLens");
			disc.layer = layer;
			disc.transform.SetParent(parent, false);
			disc.AddComponent<MeshFilter>().sharedMesh = GetQuad();

			_material = new Material(template) { name = "ScopeLens (instance)" };
			var renderer = disc.AddComponent<MeshRenderer>();
			renderer.sharedMaterial    = _material;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows    = false;

			_disc = disc.transform;
			disc.SetActive(false);
		}

		// PUBLIC METHODS

		/// <summary>
		/// Every frame after the viewmodel has posed the gun. Off unless a lens optic is aimed past ScopeLensFromAim.
		/// </summary>
		public void Update(SceneCamera sceneCamera, BulletWeapon weapon, GunModelParts parts, float aimProgress, float hipVerticalFOV)
		{
			GunplaySettings settings = GunplaySettings.Current;
			bool on = sceneCamera != null && sceneCamera.ViewmodelCamera != null && weapon != null && weapon.UsesScopeLens == true && parts != null && parts.AdsEye != null && aimProgress >= settings.ScopeLensFromAim;

			SetActive(on);
			if (on == false)
				return;

			Camera main       = sceneCamera.Camera;
			Camera viewCamera = sceneCamera.ViewmodelCamera;

			// The disc: at the eyepiece, facing the eye
			Transform eye    = parts.AdsEye;
			float     radius = settings.ScopeLensRadius;
			_disc.position   = eye.position + eye.forward * settings.ScopeLensOffset;
			_disc.rotation   = eye.rotation;
			_disc.localScale = new Vector3(radius * 2.0f, radius * 2.0f, 1.0f);

			EnsureCamera(main, settings.ScopeLensResolution);

			// How big the disc looks: its center and an edge point on screen, as a share of the screen's half height
			Vector3 center = viewCamera.WorldToScreenPoint(_disc.position);
			Vector3 edge   = viewCamera.WorldToScreenPoint(_disc.position + _disc.up * radius);
			float   share  = Mathf.Max(0.001f, Vector2.Distance(center, edge) / (Screen.height * 0.5f));

			// What's in the glass is AdsZoom times the hip view: tan(lens half fov) = share * tan(hip half fov) / zoom
			float zoom     = Mathf.Max(1.0f, weapon.AdsZoom);
			float tanLens  = share * Mathf.Tan(hipVerticalFOV * 0.5f * Mathf.Deg2Rad) / zoom;
			_camera.fieldOfView = 2.0f * Mathf.Atan(tanLens) * Mathf.Rad2Deg;

			// Looking from the eye through the disc's center
			Ray ray = main.ScreenPointToRay(new Vector3(center.x, center.y, 0.0f));
			_camera.transform.SetPositionAndRotation(main.transform.position, Quaternion.LookRotation(ray.direction, main.transform.up));
			_camera.cullingMask   = main.cullingMask & ~(1 << _disc.gameObject.layer);
			_camera.farClipPlane  = main.farClipPlane;
			_camera.nearClipPlane = Mathf.Max(0.05f, main.nearClipPlane);
			_camera.depth         = main.depth - 1.0f;

			_material.SetFloat(_vignetteId, settings.ScopeLensVignette);
			_material.SetColor(_reticleColorId, settings.ScopeReticleColor);
			_material.SetFloat(_reticleWidthId, settings.ScopeReticleWidth);
		}

		public void Dispose()
		{
			if (_camera != null)
			{
				Object.Destroy(_camera.gameObject);
			}

			if (_texture != null)
			{
				_texture.Release();
				Object.Destroy(_texture);
			}

			if (_material != null)
			{
				Object.Destroy(_material);
			}
		}

		// PRIVATE METHODS

		private void SetActive(bool active)
		{
			if (_active == active)
				return;

			_active = active;
			_disc.gameObject.SetActive(active);

			if (_camera != null)
			{
				_camera.enabled = active;
			}
		}

		private void EnsureCamera(Camera main, int resolution)
		{
			resolution = Mathf.Clamp(resolution, 128, 2048);

			if (_texture == null || _texture.width != resolution)
			{
				if (_texture != null)
				{
					_texture.Release();
					Object.Destroy(_texture);
				}

				_texture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32) { name = "ScopeLens", antiAliasing = 1 };
				_material.SetTexture(_lensTexId, _texture);

				if (_camera != null)
				{
					_camera.targetTexture = _texture;
				}
			}

			if (_camera != null)
				return;

			// A plain camera in the viewmodel's scene, not in the main camera's stack
			var cameraObject = new GameObject("ScopeLensCamera");
			UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, _disc.gameObject.scene);
			_camera = cameraObject.AddComponent<Camera>();
			_camera.clearFlags      = main.clearFlags;
			_camera.backgroundColor = main.backgroundColor;
			_camera.allowHDR        = false;
			_camera.allowMSAA       = false;
			_camera.targetTexture   = _texture;
			_camera.enabled         = _active;

			UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
			data.renderType          = CameraRenderType.Base;
			// The same color grading as the main view, or the glass looks paler than the world around it
			data.renderPostProcessing = true;
			data.renderShadows       = true;
			data.requiresDepthTexture = false;
		}

		/// <summary>A 1 × 1 quad facing -Z (toward the eye behind it), UVs 0..1.</summary>
		private static Mesh GetQuad()
		{
			if (_quad != null)
				return _quad;

			_quad = new Mesh { name = "ScopeLensQuad" };
			_quad.vertices  = new[] { new Vector3(-0.5f, -0.5f, 0.0f), new Vector3(0.5f, -0.5f, 0.0f), new Vector3(-0.5f, 0.5f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f) };
			_quad.uv        = new[] { new Vector2(0.0f, 0.0f), new Vector2(1.0f, 0.0f), new Vector2(0.0f, 1.0f), new Vector2(1.0f, 1.0f) };
			_quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
			_quad.RecalculateNormals();
			_quad.RecalculateBounds();
			return _quad;
		}
	}
}
