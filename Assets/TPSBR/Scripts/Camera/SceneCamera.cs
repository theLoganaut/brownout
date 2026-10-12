using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TPSBR
{
	public class SceneCamera : SceneService
	{
		// PUBLIC MEMBERS

		public Camera      Camera          => _camera;
		public Camera      ViewmodelCamera => _viewmodelCamera;
		public ShakeEffect ShakeEffect     => _shakeEffect;
		public bool        EnableCamera    { get; set; } = true;

		// PRIVATE MEMBERS

		[SerializeField]
		private Camera _camera;
		[SerializeField]
		private AudioListener _audioListener;
		[SerializeField]
		private ShakeEffect _shakeEffect;

		[Header("Viewmodel")]
		[SerializeField, Tooltip("Vertical FOV of the camera that draws the first-person arms and gun. Fixed, so the gun doesn't stretch with the FOV setting.")]
		private float _viewmodelFOV = 55.0f;
		[SerializeField]
		private float _viewmodelNearClip = 0.01f;
		[SerializeField]
		private float _viewmodelFarClip = 10.0f;

		private int    _cameraCullingMask;
		private int    _viewmodelCullingMask;
		private Camera _viewmodelCamera;

		// SceneService INTERFACE

		protected override void OnInitialize()
		{
			base.OnInitialize();

			// The first-person viewmodel (FPV layer) is drawn only by the stacked viewmodel camera.
			_viewmodelCullingMask = 1 << ObjectLayer.FPV;
			_cameraCullingMask    = _camera.cullingMask & ~_viewmodelCullingMask;

			CreateViewmodelCamera();
		}

		protected override void OnTick()
		{
			if (Scene is Gameplay)
			{
				_audioListener.enabled = Context.HasInput;
				_camera.enabled = Context.HasInput;

				// We are just switching culling mask as disabling would mean more complex camera setup to not stop UI rendering
				_camera.cullingMask = EnableCamera == true ? _cameraCullingMask : 0;
			}

			if (_viewmodelCamera != null)
			{
				_viewmodelCamera.cullingMask = EnableCamera == true ? _viewmodelCullingMask : 0;
				_viewmodelCamera.fieldOfView = _viewmodelFOV;
			}
		}

		// PRIVATE METHODS

		private void CreateViewmodelCamera()
		{
			var baseCameraData = _camera.GetUniversalAdditionalCameraData();
			if (baseCameraData == null)
				return;

			var viewmodelObject = new GameObject("ViewmodelCamera");
			viewmodelObject.transform.SetParent(_camera.transform, false);

			_viewmodelCamera = viewmodelObject.AddComponent<Camera>();
			_viewmodelCamera.cullingMask   = _viewmodelCullingMask;
			_viewmodelCamera.fieldOfView   = _viewmodelFOV;
			_viewmodelCamera.nearClipPlane = _viewmodelNearClip;
			_viewmodelCamera.farClipPlane  = _viewmodelFarClip;

			var viewmodelCameraData = _viewmodelCamera.GetUniversalAdditionalCameraData();
			viewmodelCameraData.renderType          = CameraRenderType.Overlay;
			viewmodelCameraData.renderShadows       = false;
			viewmodelCameraData.renderPostProcessing = baseCameraData.renderPostProcessing;

			baseCameraData.cameraStack.Add(_viewmodelCamera);
		}
	}
}
