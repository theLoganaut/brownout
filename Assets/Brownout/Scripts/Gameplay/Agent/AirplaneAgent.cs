using UnityEngine;
using UnityEngine.InputSystem;
using Fusion;
using Fusion.Addons.KCC;

namespace TPSBR
{
	using TPSBR.UI;

	public sealed class AirplaneAgent : ContextBehaviour
	{
		// PUBLIC MEMBERS

		[Networked, HideInInspector]
		public Airplane Airplane { get; set; }

		// PRIVATE MEMBERS

		[SerializeField]
		private Transform _cameraTransform;

		[Networked]
		private uint _packedLookRotation { get; set; }
		[Networked]
		private NetworkButtons _lastKnownActions { get; set; }

		private Quaternion _lookRotation
		{
			get { NetworkCompression.UnpackPitchYaw(_packedLookRotation, out float pitch, out float yaw); return Quaternion.Euler(pitch, yaw, 0.0f); }
			set { Vector2 euler = KCCUtility.GetClampedEulerLookRotation(value); _packedLookRotation = NetworkCompression.PackPitchYaw(euler.x, euler.y); }
		}

		private bool              _jump;
		private bool              _isParented;
		private Vector2           _lookRotationDelta;
		private SmoothVector2     _smoothLookRotationDelta = new SmoothVector2(256);
		private UIMobileInputView _mobileInputView;
		private float             _firstRenderTime = -1.0f; // Phase 11c: the fleet shot starts with the first frame in the helicopter

		// NetworkBehaviour INTERFACE

		public override void Spawned()
		{
			_smoothLookRotationDelta.ClearValues();

			NetworkEvents networkEvents = Runner.GetComponent<NetworkEvents>();
			networkEvents.OnInput.RemoveListener(OnInput);
			networkEvents.OnInput.AddListener(OnInput);

			// Brownout: the look is relative to the helicopter's heading (GetViewRotation), so start looking straight ahead
			_lookRotation = Quaternion.identity;

			if (HasInputAuthority == true)
			{
				Context.WaitingAgentTransform = transform;
			}

			_isParented      = false;
			_firstRenderTime = -1.0f;
		}

		public override void FixedUpdateNetwork()
		{
			if (_isParented == false && Airplane != null)
			{
				transform.SetParent(Airplane.AgentPosition, false);
				_isParented = true;
			}

			if (IsProxy == true)
				return;

			if (Runner.TryGetInputForPlayer(Object.InputAuthority, out GameplayInput input) == true)
			{
				_lookRotation = UpdateRotation(_lookRotation, input.LookRotationDelta);

				if (input.Actions.IsSet(EGameplayInputAction.Jump) == true && _lastKnownActions.IsSet(EGameplayInputAction.Jump) == false)
				{
					if ((Context.GameplayMode as BattleRoyaleGameplayMode).RequestAirplaneJump(Object.InputAuthority, GetViewRotation() * Vector3.forward) == true)
						return;
				}

				_lastKnownActions = input.Actions;
			}

			if (HasStateAuthority == true && Runner.IsPlayerValid(Object.InputAuthority) == true)
			{
				Vector3 lookDirection = GetViewRotation() * Vector3.forward;

				Runner.ClearPlayerAreaOfInterest(Object.InputAuthority);
				Runner.AddPlayerAreaOfInterest(Object.InputAuthority, transform.position + lookDirection *  25.0f,  50.0f);
				Runner.AddPlayerAreaOfInterest(Object.InputAuthority, transform.position + lookDirection * 100.0f,  75.0f);
				Runner.AddPlayerAreaOfInterest(Object.InputAuthority, transform.position + lookDirection * 175.0f, 100.0f);
			}
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (runner != null)
			{
				NetworkEvents networkEvents = runner.GetComponent<NetworkEvents>();
				networkEvents.OnInput.RemoveListener(OnInput);
			}

			if (HasInputAuthority == true && Context.WaitingAgentTransform == transform)
			{
				Context.WaitingAgentTransform = null;
			}
		}

		public override void Render()
		{
			if (Context.ObservedPlayerRef != Object.InputAuthority)
				return;

			if (_isParented == false && Airplane != null)
			{
				transform.SetParent(Airplane.AgentPosition, false);
				_isParented = true;
			}

			Quaternion look = UpdateRotation(_lookRotation, _lookRotationDelta);

			if (Airplane == null)
			{
				Context.Camera.transform.position = _cameraTransform.position;
				Context.Camera.transform.rotation = _cameraTransform.rotation;
				return;
			}

			// Brownout: far out behind the helicopter, orbiting it with the mouse (directly behind until you look around)
			MatchSettings settings = MatchSettings.Current;
			Quaternion    view     = GetViewRotation(look);
			Vector3       pivot    = Airplane.transform.position + Vector3.up * settings.HelicopterCameraHeight;

			Vector3    position = pivot - view * Vector3.forward * settings.HelicopterCameraDistance;
			Quaternion rotation = view;

			// Phase 11c: the fleet shot. The first seconds the camera watches the helicopter fly past from the side, then swings in behind
			FeelSettings feel = FeelSettings.Current;
			if (_firstRenderTime < 0.0f)
			{
				_firstRenderTime = Time.time;
			}
			float shot = Time.time - _firstRenderTime;
			if (shot < feel.FleetShotTime + feel.FleetShotBlend && feel.FleetShotTime > 0.0f)
			{
				Transform  heli       = Airplane.transform;
				Vector3    drift      = heli.forward * -shot * 6.0f;
				Vector3    shotPos    = heli.position + Quaternion.Euler(0.0f, heli.eulerAngles.y, 0.0f) * feel.FleetShotOffset + drift;
				Quaternion shotRot    = Quaternion.LookRotation(heli.position + heli.forward * 10.0f - shotPos, Vector3.up);
				float      blend      = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01((shot - feel.FleetShotTime) / Mathf.Max(0.01f, feel.FleetShotBlend)));

				position = Vector3.Lerp(shotPos, position, blend);
				rotation = Quaternion.Slerp(shotRot, rotation, blend);
			}

			Context.Camera.transform.position = position;
			Context.Camera.transform.rotation = rotation;
		}

		/// <summary>The look as a world rotation around the helicopter: yaw and pitch from the helicopter's heading, so no look = straight ahead.</summary>
		private Quaternion GetViewRotation() => GetViewRotation(_lookRotation);

		private Quaternion GetViewRotation(Quaternion look)
		{
			float   heading = Airplane != null ? Airplane.transform.eulerAngles.y : 0.0f;
			Vector3 euler   = KCCUtility.GetClampedEulerLookRotation(look);
			return Quaternion.Euler(euler.x, heading + euler.y, 0.0f);
		}

		// MONOBEHAVIOUR

		private void Update()
		{
			if (HasInputAuthority == false || Context.HasInput == false)
				return;

			if ((Context.Input.IsCursorVisible == true && Context.Settings.SimulateMobileInput == false) || Context.GameplayMode.State != GameplayMode.EState.Active)
				return;

			Vector2 mouseDelta = default;

			if ((Application.isMobilePlatform == false || Application.isEditor == true) && Context.Settings.SimulateMobileInput == false)
			{
				mouseDelta = Mouse.current.delta.ReadValue() * 0.075f;
				// Phase 7: the Jump binding, rebindable. Sent for as long as it's held (phase 8): a press that lived in one tick's input was
				// lost now and then under lag and packet loss. The server jumps on the first tick it sees it, so holding never jumps twice
				_jump |= KeyBindings.WasPressedThisFrame(EBinding.Jump) || KeyBindings.IsHeld(EBinding.Jump);
			}
			else
			{
				if (_mobileInputView == null)
				{
					if (Context.UI != null)
					{
						_mobileInputView = Context.UI.Get<UIMobileInputView>();
						_mobileInputView.Look = default;
					}
				}
				else
				{
					const float mobileSensitivityMultiplier = 32.0f;
					mouseDelta = _mobileInputView.Look * mobileSensitivityMultiplier;
					_mobileInputView.Look = default;
					_jump |= _mobileInputView.Jump;
				}
			}

			_lookRotationDelta += InputUtility.GetSmoothLookRotationDelta(_smoothLookRotationDelta, new Vector2(-mouseDelta.y, mouseDelta.x), Global.RuntimeSettings.Sensitivity, 0.025f);
		}

		// PRIVATE METHODS

		private void OnInput(NetworkRunner runner, Fusion.NetworkInput networkInput)
		{
			if (HasInputAuthority == false || Context.HasInput == false)
				return;

			GameplayInput gameplayInput = new GameplayInput();

			gameplayInput.Jump = _jump;
			gameplayInput.LookRotationDelta = _lookRotationDelta;

			networkInput.Set(gameplayInput);

			_jump = default;
			_lookRotationDelta = default;
		}

		private Quaternion UpdateRotation(Quaternion rotation, Vector2 rotationDelta)
		{
			if (rotationDelta == Vector2.zero)
			{
				transform.localRotation = rotation;
				return rotation;
			}

			Vector3 rotationEuler = KCCUtility.GetClampedEulerLookRotation(rotation);

			float lookRotationDeltaX = Mathf.Clamp(rotationEuler.x + rotationDelta.x, -85f, 85f) - rotationEuler.x;
			float lookRotationDeltaY = rotationDelta.y;

			var newRotation = Quaternion.Euler(rotationEuler + new Vector3(lookRotationDeltaX, lookRotationDeltaY, 0f));

			transform.localRotation = newRotation;
			return newRotation;
		}
	}
}
