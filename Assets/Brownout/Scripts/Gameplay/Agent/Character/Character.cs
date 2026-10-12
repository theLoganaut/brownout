namespace TPSBR
{
	using System;
	using UnityEngine;
	using UnityEngine.Profiling;
	using Fusion.Addons.AnimationController;
	using Fusion.Addons.KCC;

	[Serializable]
	public sealed class CharacterView
	{
		public Transform RootBone;
		public Transform HeadTransform;

		public Transform CameraHandle;
		public Transform CameraTransformHead;
		public Transform DefaultCameraTransform;
		public Transform AimCameraTransform;
		public Transform JetpackCameraTransform;

		public Transform FireTransformRoot;
		public Transform FireTransform;

		public Transform LeftFoot;
		public Transform RightFoot;
	}

	[Serializable]
	public struct TransformData
	{
		public Vector3    Position;
		public Vector3    LocalPosition;
		public Quaternion Rotation;

		public TransformData(Vector3 position, Vector3 localPosition, Quaternion rotation)
		{
			Position      = position;
			LocalPosition = localPosition;
			Rotation      = rotation;
		}
	}

	public class Character : MonoBehaviour
	{
		// PUBLIC MEMBERS

		public Agent                        Agent               => _agent;
		public bool                         HasInputAuthority   => _agent.HasInputAuthority;
		public KCC                          CharacterController => _characterController;
		public CharacterAnimationController AnimationController => _animationController;
		public CharacterView                ThirdPersonView     => _thirdPersonView;

		public float DispersionMultiplier { get; private set; }

		/// <summary>
		/// 0 = hip, 1 = fully aimed. Advances over the current weapon's ADS time. Render only, valid for the observed agent.
		/// </summary>
		public float AimProgress { get; private set; }
		/// <summary>Phase 11a: tan(view / 2) / tan(hip / 2) of the zoom you aim through (a scope's lens, not the view around it); scales aimed look speed.</summary>
		public float LookZoomScale { get; private set; } = 1.0f;

		/// <summary>
		/// Downed (phase 4): the camera is over the shoulder instead of at the eye. Render only, valid for the observed agent.
		/// </summary>
		public bool IsThirdPersonView { get; private set; }

		public enum EDebugView
		{
			FirstPerson,
			Behind,
			Front,
		}

		/// <summary>
		/// Animation Arena (phase 10, F7): watch your own body from behind or from the front. Editor and development builds.
		/// </summary>
		public static EDebugView DebugView;

		/// <summary>Phase 11c: landing dip, bob, tilt, fire kick and hit punch on the first-person camera.</summary>
		public CameraFeel CameraFeel => _cameraFeel;

		// PRIVATE MEMBERS

		[SerializeField]
		private CharacterView _thirdPersonView;

		[Header("First Person")]
		[SerializeField, Tooltip("Neck pivot relative to the character root (yaw only). The eye rotates around it with the look pitch, like a real head. The height comes from MovementSettings: standing eye height minus the eye offset's height.")]
		private Vector3 _neckOffset = new Vector3(0.0f, 1.55f, -0.02f);
		[SerializeField, Tooltip("Eye relative to the neck pivot, in look space. Looking level, the eye is at MovementSettings' standing eye height.")]
		private Vector3 _eyeOffset = new Vector3(0.0f, 0.15f, 0.12f);
		[SerializeField, Tooltip("The horizontal FOV setting is defined at this aspect ratio, like CoD's FOV slider.")]
		private float _fovReferenceAspect = 16.0f / 9.0f;

		[Header("Downed Camera")]
		[SerializeField, Tooltip("Over-the-shoulder pivot above the feet while downed.")]
		private float   _downedCameraPivotHeight = 0.8f;
		[SerializeField, Tooltip("Camera offset from the pivot in look space (right, up, back).")]
		private Vector3 _downedCameraOffset = new Vector3(0.45f, 0.35f, -2.2f);
		[SerializeField]
		private float   _downedCameraBlendTime = 0.3f;

		[Header("Drop Camera (phase 7)")]
		[SerializeField, Tooltip("Wingsuit: behind and above you, pivot above the feet and offset in look space (right, up, back).")]
		private float   _wingsuitCameraPivotHeight = 1.0f;
		[SerializeField]
		private Vector3 _wingsuitCameraOffset = new Vector3(0.0f, 1.2f, -5.5f);

		[Header("Debug Camera (Animation Arena)")]
		[SerializeField, Tooltip("F7 behind view: pivot above the feet and offset in look space (right, up, back). The front view uses the same distance, turned around.")]
		private float   _debugCameraPivotHeight = 1.3f;
		[SerializeField]
		private Vector3 _debugCameraOffset = new Vector3(0.6f, 0.3f, -3.2f);

		[Header("Dispersion")]
		[SerializeField]
		private float _aimDispersionMultiplier = 0.5f;
		[SerializeField]
		private float _airDispersionMultiplier = 4f;
		[SerializeField]
		private float _runDispersionMultiplier = 5f;

		private KCC                          _characterController;
		private CharacterAnimationController _animationController;
		private Agent                        _agent;
		private SceneCamera                  _camera;

		private TransformSampler             _cameraTransformSampler = new TransformSampler();
		private float                        _thirdPersonBlend;
		private readonly CameraFeel          _cameraFeel = new CameraFeel();

		// PUBLIC METHODS

		public Transform GetCameraHandle()
		{
			return _thirdPersonView.CameraHandle;
		}

		/// <summary>
		/// First-person eye. Computed from the character position and look rotation only, so the server and clients agree on it.
		/// </summary>
		public TransformData GetCameraTransform(bool resolveRenderHistory)
		{
			TransformData transformData = GetEyeTransform();

			if (resolveRenderHistory == true && CharacterController.IsProxy == false && _cameraTransformSampler.ResolveRenderPositionAndRotation(_characterController, _agent.AgentInput.FixedInput.LocalAlpha, out Vector3 cameraPosition, out Quaternion cameraRotation) == true)
			{
				transformData.Position      = cameraPosition;
				transformData.LocalPosition = transform.InverseTransformPoint(cameraPosition);
				transformData.Rotation      = cameraRotation;
			}

			return transformData;
		}

		/// <summary>
		/// Shots are checked from the eye, like CoD. Tracers and muzzle flashes are drawn from the gun separately.
		/// </summary>
		public TransformData GetFireTransform(bool resolveRenderHistory)
		{
			return GetCameraTransform(resolveRenderHistory);
		}

		/// <summary>
		/// Converts the horizontal FOV setting (degrees at the reference aspect) to Unity's vertical FOV.
		/// </summary>
		public float GetHipVerticalFOV()
		{
			float horizontalFOV = Global.RuntimeSettings.FieldOfView;
			return 2.0f * Mathf.Atan(Mathf.Tan(horizontalFOV * 0.5f * Mathf.Deg2Rad) / _fovReferenceAspect) * Mathf.Rad2Deg;
		}

		public void OnSpawned(Agent agent)
		{
			_agent  = agent;
			_camera = agent.Context.Camera;

			_characterController.SetManualUpdate(true);
			_animationController.SetManualUpdate(true);

			AimProgress = 0.0f;

			_cameraTransformSampler.Clear();

			_cameraFeel.Reset();
			agent.Health.HitTaken -= OnHitTaken;
			agent.Health.HitTaken += OnHitTaken;
		}

		public void OnFixedUpdate()
		{
			Profiler.BeginSample(nameof(Character));

			if (_agent.Runner.IsClient == false)
			{
				int playerCount = _agent.Context.NetworkGame.ActivePlayerCount;
				if (_agent.AnimationEveryTicks > 1)
				{
					// Phase 9b: a bot far from every human and not fighting (BotBrain) has its pose updated every few ticks
					_animationController.SetInterlacedEvaluation(EEvaluationTarget.FixedUpdate, _agent.AnimationEveryTicks, _agent.BotNumber);
				}
				else if (playerCount <  50)
				{
					_animationController.SetInterlacedEvaluation(EEvaluationTarget.FixedUpdate, 1, _agent.Object.InputAuthority.AsIndex);
				}
				else if (playerCount < 100)
				{
					_animationController.SetInterlacedEvaluation(EEvaluationTarget.FixedUpdate, 2, _agent.Object.InputAuthority.AsIndex);
				}
				else if (playerCount < 150)
				{
					_animationController.SetInterlacedEvaluation(EEvaluationTarget.FixedUpdate, 4, _agent.Object.InputAuthority.AsIndex);
				}
				else
				{
					_animationController.SetInterlacedEvaluation(EEvaluationTarget.FixedUpdate, 6, _agent.Object.InputAuthority.AsIndex);
				}
			}

			if (_agent.Health.IsAlive == false)
			{
				_animationController.SetDead(true);
			}

			_characterController.ManualFixedUpdate();
			_animationController.ManualFixedUpdate();

			TransformData cameraTransformData = GetCameraTransform(false);
			_cameraTransformSampler.Sample(_characterController, cameraTransformData.Position, cameraTransformData.Rotation);

			DispersionMultiplier = GetDispersionMultiplier();

			Profiler.EndSample();
		}

		public void OnRender()
		{
			_characterController.ManualRenderUpdate();
			_animationController.ManualRenderUpdate();

			TransformData cameraTransformData = GetCameraTransform(false);
			_cameraTransformSampler.Sample(_characterController, cameraTransformData.Position, cameraTransformData.Rotation);
		}

		public void OnAgentRender()
		{
			if (_agent.IsObserved == false)
			{
				_thirdPersonBlend = 0.0f;
				IsThirdPersonView = false;
				return;
			}

			Weapon currentWeapon = _agent.Weapons.CurrentWeapon;

			float adsTime   = currentWeapon != null && currentWeapon.AdsTime > 0.0f ? currentWeapon.AdsTime : 0.25f;
			float adsZoom   = currentWeapon != null && currentWeapon.AdsZoom > 1.0f ? currentWeapon.AdsZoom : 1.0f;
			float targetAim = _characterController.Data.Aim == true ? 1.0f : 0.0f;

			AimProgress = Mathf.MoveTowards(AimProgress, targetAim, Time.deltaTime / adsTime);

			// Phase 11a: a magnified optic zooms inside its lens (ScopeLens); the view around it only by the gun's iron-sight zoom
			float viewZoom = currentWeapon is BulletWeapon scoped && scoped.UsesScopeLens == true ? Mathf.Max(1.0f, scoped.ViewZoom) : adsZoom;

			// Zoom is magnification: tan(ads / 2) = tan(hip / 2) / zoom
			float hipFOV = GetHipVerticalFOV();
			float adsFOV = 2.0f * Mathf.Atan(Mathf.Tan(hipFOV * 0.5f * Mathf.Deg2Rad) / viewZoom) * Mathf.Rad2Deg;
			float aim    = Mathf.SmoothStep(0.0f, 1.0f, AimProgress);

			_camera.Camera.fieldOfView = Mathf.Lerp(hipFOV, adsFOV, aim);

			// Look speed follows the zoom you aim through (the lens's, with a scope): tan(view / 2) / tan(hip / 2)
			float lookFOV = 2.0f * Mathf.Atan(Mathf.Tan(hipFOV * 0.5f * Mathf.Deg2Rad) / adsZoom) * Mathf.Rad2Deg;
			LookZoomScale = Mathf.Tan(Mathf.Lerp(hipFOV, lookFOV, aim) * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(hipFOV * 0.5f * Mathf.Deg2Rad);

			TransformData cameraTransform = GetCameraTransform(false);
			Quaternion    cameraRotation  = cameraTransform.Rotation;
			Vector3       cameraPosition  = GetDownedCameraPosition(cameraTransform.Position, ref cameraRotation);
			_camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);

			// Phase 11c: render-only camera feel in first person
			if (IsThirdPersonView == false && _agent.Health.IsAlive == true)
			{
				_cameraFeel.Apply(_agent, _camera.transform, aim, Time.deltaTime);
			}
			else
			{
				_cameraFeel.Reset();
			}

			if (_agent.HasInputAuthority == true)
			{
				_animationController.RefreshSnapping();
			}
		}

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_characterController = GetComponent<KCC>();
			_animationController = GetComponent<CharacterAnimationController>();
		}

		// PRIVATE METHODS

		private void OnHitTaken(HitData hit)
		{
			if (_agent == null || _agent.IsObserved == false)
				return;

			Health health = _agent.Health;
			_cameraFeel.AddHit(hit, _characterController.Data.LookRotation, hit.IsFatal == true && health.IsDowned == true);
		}

		private TransformData GetEyeTransform()
		{
			KCCData    kccData      = _characterController.Data;
			Quaternion lookRotation = kccData.LookRotation;

			// Scope sway (magnified optics, phase 3a): moves the eye's aim, so the camera and the shots follow it alike
			Vector2 sway = GetScopeSway();
			if (sway != Vector2.zero)
			{
				lookRotation = lookRotation * Quaternion.Euler(sway.x, sway.y, 0.0f);
			}

			// Crouch and slide lower the neck pivot so the eye lands at the stance's eye height (Movement page metrics)
			MovementSettings settings = MovementSettings.Current;
			float   eyeDrop      = settings.StandEyeHeight - AgentMovement.GetEyeHeight(_characterController, kccData);
			Vector3 neckOffset   = new Vector3(_neckOffset.x, settings.StandEyeHeight - _eyeOffset.y, _neckOffset.z);
			Vector3 neckPosition = transform.position + kccData.TransformRotation * (neckOffset + Vector3.down * eyeDrop);
			Vector3 eyePosition  = neckPosition + lookRotation * _eyeOffset;

			return new TransformData(eyePosition, transform.InverseTransformPoint(eyePosition), lookRotation);
		}

		/// <summary>
		/// Downed: the camera pulls out over the shoulder ThirdPersonDelay after going down, as in the Blackout footage, and comes back on revive.
		/// </summary>
		private Vector3 GetDownedCameraPosition(Vector3 eyePosition, ref Quaternion lookRotation)
		{
			Health    health   = _agent.Health;
			EMoveMode moveMode = _characterController.Data.MoveMode;
			MatchSettings match = MatchSettings.Current;

			// The drop: third person behind you all the way down and through the first part of the landing roll (phase 10, Logan 9 Oct)
			float rollTime = _agent.Movement.GetLandRollTime(_characterController.IsProxy == true ? _agent.Runner.RemoteRenderTime : _agent.Runner.LocalRenderTime);
			bool  rolling  = rollTime >= 0.0f && rollTime < match.LandRollTime * match.RollFirstPersonShare;
			bool  dropping = (AgentMovement.IsDropMode(moveMode) == true || rolling == true) && health.IsAlive == true;
			bool  isThirdPerson = dropping == true || (health.IsDowned == true && (_agent.Runner.Tick - health.DownedTick) * _agent.Runner.DeltaTime >= HealthSettings.Current.ThirdPersonDelay);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (DebugView != EDebugView.FirstPerson && health.IsAlive == true)
				return GetDebugCameraPosition(ref lookRotation);
#endif

			if (dropping == true)
			{
				_thirdPersonBlend = 1.0f;
			}

			// Halfway through the roll the camera moves into the eye (the downed camera's blend time)
			_thirdPersonBlend = Mathf.MoveTowards(_thirdPersonBlend, isThirdPerson == true ? 1.0f : 0.0f, Time.deltaTime / Mathf.Max(0.01f, _downedCameraBlendTime));
			IsThirdPersonView = _thirdPersonBlend > 0.0f;

			if (IsThirdPersonView == false)
				return eyePosition;

			bool    dropCamera  = dropping == true || (health.IsDowned == false && rollTime >= 0.0f);
			float   pivotHeight = dropCamera == true ? _wingsuitCameraPivotHeight : _downedCameraPivotHeight;
			Vector3 offset      = dropCamera == true ? _wingsuitCameraOffset      : _downedCameraOffset;

			Vector3 pivot     = transform.position + Vector3.up * pivotHeight;
			Vector3 desired   = pivot + lookRotation * offset;
			Vector3 direction = desired - pivot;
			float   distance  = direction.magnitude;

			// Pulled in by walls and the ground
			if (distance > 0.001f && _agent.Runner.GetPhysicsScene().SphereCast(pivot, 0.15f, direction / distance, out RaycastHit hit, distance, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
			{
				desired = pivot + direction / distance * Mathf.Max(0.2f, hit.distance);
			}

			return Vector3.Lerp(eyePosition, desired, Mathf.SmoothStep(0.0f, 1.0f, _thirdPersonBlend));
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>
		/// Animation Arena: behind you over the shoulder, or in front of you looking back (level, so the mouse still turns the body).
		/// </summary>
		private Vector3 GetDebugCameraPosition(ref Quaternion lookRotation)
		{
			_thirdPersonBlend = 1.0f;
			IsThirdPersonView = true;

			Vector3 offset = _debugCameraOffset;
			if (DebugView == EDebugView.Front)
			{
				lookRotation = Quaternion.Euler(10.0f, lookRotation.eulerAngles.y + 180.0f, 0.0f);
				offset.x     = 0.0f;
			}

			Vector3 pivot     = transform.position + Vector3.up * _debugCameraPivotHeight;
			Vector3 desired   = pivot + lookRotation * offset;
			Vector3 direction = desired - pivot;
			float   distance  = direction.magnitude;

			if (distance > 0.001f && _agent.Runner.GetPhysicsScene().SphereCast(pivot, 0.15f, direction / distance, out RaycastHit hit, distance, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
			{
				desired = pivot + direction / distance * Mathf.Max(0.2f, hit.distance);
			}

			return desired;
		}
#endif

		private Vector2 GetScopeSway()
		{
			if (_agent == null || _agent.Object == null || _agent.Object.IsValid == false || _agent.Weapons == null)
				return Vector2.zero;

			Fusion.NetworkRunner runner = _agent.Runner;
			float time = _characterController.IsInFixedUpdate == true ? runner.Tick * runner.DeltaTime : runner.LocalRenderTime;

			return _agent.Weapons.GetSwayOffset(time);
		}

		private float GetDispersionMultiplier()
		{
			float multiplier = 1f;

			bool isGrounded = _characterController.FixedData.IsGrounded == true;

			if (isGrounded == true && _characterController.FixedData.RealSpeed > 0.5f)
			{
				multiplier *= _runDispersionMultiplier;
			}

			if (isGrounded == false)
			{
				multiplier *= _airDispersionMultiplier;
			}

			if (_characterController.FixedData.Aim == true)
			{
				multiplier *= _aimDispersionMultiplier;
			}

			return multiplier;
		}
	}
}
