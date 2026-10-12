using Fusion.Addons.KCC;

namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.InputSystem;

	public sealed partial class AgentInput
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		// Test hooks (editor and development builds only): held down for the agent with local input,
		// so automated checks through the Unity MCP can fire and aim without a focused Game view.
		public static bool DebugHoldAttack;
		public static bool DebugHoldAim;
		public static bool DebugHoldSprint;
		public static bool DebugHoldCrouch;
		public static bool DebugHoldJump;
		public static bool DebugHoldMelee;
		public static bool DebugHoldReload;
		public static bool DebugHoldHeal;
		public static bool DebugHoldInteract;
		public static bool DebugHoldPickUp;
		public static bool DebugHoldEquipment;
		public static bool DebugHoldGear;
		public static bool DebugHoldPing;
		/// <summary>Set to 1 or 2 to switch to that primary slot once (cleared when read).</summary>
		public static int  DebugSwitchSlot;
		public static Vector2 DebugMoveDirection;
#endif

		// Hold or toggle (phase 6b): the latched state of each toggled action, and last frame's raw keys
		private bool _crouchLatch;
		private bool _sprintLatch;
		private bool _sprintGap;
		private bool _aimLatch;
		private bool _lastCrouchKey;
		private bool _lastSprintKey;
		private bool _lastAimKey;

		partial void ProcessStandaloneInput(bool isInputPoll)
		{
			// Always use KeyControl.isPressed, Input.GetMouseButton() and Input.GetKey().
			// Never use KeyControl.wasPressedThisFrame, Input.GetMouseButtonDown() or Input.GetKeyDown() otherwise the action might be lost.
			// Phase 6b: every key comes from KeyBindings (rebindable in the settings menu).

			Vector2 moveDirection;

			Mouse    mouse      = Mouse.current;
			Keyboard keyboard   = Keyboard.current;

			moveDirection = Vector2.zero;

			if (KeyBindings.IsHeld(EBinding.MoveForward) == true) { moveDirection += Vector2.up;    }
			if (KeyBindings.IsHeld(EBinding.MoveBack)    == true) { moveDirection += Vector2.down;  }
			if (KeyBindings.IsHeld(EBinding.MoveLeft)    == true) { moveDirection += Vector2.left;  }
			if (KeyBindings.IsHeld(EBinding.MoveRight)   == true) { moveDirection += Vector2.right; }

			if (moveDirection.IsZero() == false)
			{
				moveDirection.Normalize();
			}

			bool jump = KeyBindings.IsHeld(EBinding.Jump);

			_renderInput.MoveDirection     = moveDirection;
			_renderInput.LookRotationDelta = GetMouseLook(mouse);
			_renderInput.Jump              = jump;
			_renderInput.Attack            = KeyBindings.IsHeld(EBinding.Fire);
			_renderInput.Reload            = KeyBindings.IsHeld(EBinding.Reload);
			_renderInput.Interact          = KeyBindings.IsHeld(EBinding.Interact);
			_renderInput.PickUp            = KeyBindings.IsHeld(EBinding.PickUp);
			_renderInput.Heal              = KeyBindings.IsHeld(EBinding.Heal);
			_renderInput.Equipment         = KeyBindings.IsHeld(EBinding.Equipment);
			_renderInput.Gear              = KeyBindings.IsHeld(EBinding.Gear);
			_renderInput.Inventory         = KeyBindings.IsHeld(EBinding.Inventory);
			_renderInput.Map               = KeyBindings.IsHeld(EBinding.Map);
			_renderInput.Ping              = KeyBindings.IsHeld(EBinding.Ping);
			_renderInput.Melee             = KeyBindings.IsHeld(EBinding.Melee);
			_renderInput.Weapon            = GetWeaponInput();

			ApplyHoldOrToggle(moveDirection, jump);

			// Ctrl+Shift+1..6 switches the Multipeer peer (Networking.UpdatePeerSwitch), so the number keys don't also swap guns or punch
			if (keyboard.leftCtrlKey.isPressed == true && keyboard.leftShiftKey.isPressed == true)
			{
				_renderInput.Weapon = 0;
				_renderInput.Melee  = false;
#if UNITY_EDITOR
				// Editor only (phase 5b): pressing Ctrl with Shift held is the peer switch, not the gear toggle
				if (_previousRenderInput.Gear == false)
				{
					_renderInput.Gear = false;
				}
#endif
			}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			_renderInput.Attack |= DebugHoldAttack;
			_renderInput.Aim    |= DebugHoldAim;
			_renderInput.Sprint |= DebugHoldSprint;
			_renderInput.Crouch |= DebugHoldCrouch;
			_renderInput.Jump   |= DebugHoldJump;
			_renderInput.Melee  |= DebugHoldMelee;
			_renderInput.Reload |= DebugHoldReload;
			_renderInput.Heal     |= DebugHoldHeal;
			_renderInput.Interact |= DebugHoldInteract;
			_renderInput.PickUp   |= DebugHoldPickUp;
			_renderInput.Equipment |= DebugHoldEquipment;
			_renderInput.Gear      |= DebugHoldGear;
			_renderInput.Ping      |= DebugHoldPing;
			if (DebugSwitchSlot > 0)
			{
				_renderInput.Weapon = (byte)(DebugSwitchSlot + 1);
				DebugSwitchSlot = 0;
			}
			if (DebugMoveDirection.IsZero() == false)
			{
				_renderInput.MoveDirection = DebugMoveDirection.normalized;
			}
#endif
		}

		/// <summary>
		/// Mouse look in degrees (phase 6b, HUD & UI page): ControlsSettings sets the turn per mouse count (about 30 cm per full turn at
		/// 1000 DPI) and the sensitivity slider multiplies it. Aimed, the turn is scaled by the zoom (so the view moves at the same speed on
		/// screen) and the ADS slider multiplies that.
		/// </summary>
		private Vector2 GetMouseLook(Mouse mouse)
		{
			ControlsSettings controls   = ControlsSettings.Current;
			Vector2          mouseDelta = mouse.delta.ReadValue() * controls.HipDegreesPerCount;

			Vector2 look = InputUtility.GetSmoothLookRotationDelta(_smoothLookRotationDelta, new Vector2(-mouseDelta.y, mouseDelta.x), Global.RuntimeSettings.Sensitivity, _lookResponsivity);

			if (_agent.Character.CharacterController.FixedData.Aim == true)
			{
				look *= Global.RuntimeSettings.AimSensitivity * GetZoomScale(controls);
			}

			return look;
		}

		/// <summary>Degrees of turn per mouse count right now (hip or aimed), for the settings check (phase 6b).</summary>
		public float GetLookDegreesPerCount()
		{
			ControlsSettings controls = ControlsSettings.Current;
			float degrees = controls.HipDegreesPerCount * Global.RuntimeSettings.Sensitivity;

			if (_agent.Character.CharacterController.FixedData.Aim == true)
			{
				degrees *= Global.RuntimeSettings.AimSensitivity * GetZoomScale(controls);
			}

			return degrees;
		}

		/// <summary>tan(view / 2) / tan(hip view / 2): 1 at the hip, 1 / zoom fully aimed, in between while the sights come up.</summary>
		private float GetZoomScale(ControlsSettings controls)
		{
			if (controls.ScaleAdsByZoom == false || Context.Camera == null || Context.Camera.Camera == null)
				return 1.0f;

			// Phase 11a: the zoom you aim through (a scope's lens zoom, not the camera's, which only zooms by the iron-sight zoom)
			return Mathf.Clamp(_agent.Character.LookZoomScale, 0.05f, 1.0f);
		}

		/// <summary>
		/// Crouch, sprint and aim: holds by default, each with a toggle option in the settings menu (HUD & UI page). The network input stays
		/// a hold, so the server's rules don't change. Toggled sprint runs while you move forward; tapping Sprint again gives a fresh press,
		/// so a double tap still dashes. Aimed, Sprint is the hold-breath key as before.
		/// </summary>
		private void ApplyHoldOrToggle(Vector2 moveDirection, bool jump)
		{
			RuntimeSettings settings = Global.RuntimeSettings;

			bool crouchKey = KeyBindings.IsHeld(EBinding.Crouch);
			bool sprintKey = KeyBindings.IsHeld(EBinding.Sprint);
			bool aimKey    = KeyBindings.IsHeld(EBinding.Aim);

			bool crouchPressed = crouchKey == true && _lastCrouchKey == false;
			bool sprintPressed = sprintKey == true && _lastSprintKey == false;
			bool aimPressed    = aimKey    == true && _lastAimKey    == false;

			_lastCrouchKey = crouchKey;
			_lastSprintKey = sprintKey;
			_lastAimKey    = aimKey;

			if (_agent.Health.IsAlive == false || _agent.Health.IsDowned == true || Context.Input.IsOverlayOpen == true)
			{
				_crouchLatch = false;
				_sprintLatch = false;
				_sprintGap   = false;
				_aimLatch    = false;
			}

			// Aim
			bool aim = aimKey;
			if (settings.ToggleAim == true)
			{
				if (aimPressed    == true) { _aimLatch = _aimLatch == false; }
				if (sprintPressed == true) { _aimLatch = false; }
				aim = _aimLatch;
			}

			// Crouch
			bool crouch = crouchKey;
			if (settings.ToggleCrouch == true)
			{
				if (crouchPressed == true) { _crouchLatch = _crouchLatch == false; }
				if ((jump == true && _previousRenderInput.Jump == false) || sprintPressed == true) { _crouchLatch = false; }
				crouch = _crouchLatch;
			}

			// Sprint
			bool sprint = sprintKey;
			if (settings.ToggleSprint == true && aim == false)
			{
				if (sprintPressed == true)
				{
					if (_sprintLatch == true) { _sprintGap = true; } else { _sprintLatch = true; }
				}
				if (sprintKey == false)           { _sprintGap = false; }
				if (moveDirection.y <= 0.1f)      { _sprintLatch = false; }
				if (crouchPressed == true)        { _sprintLatch = false; }

				sprint = _sprintLatch == true && _sprintGap == false;
			}
			else
			{
				_sprintLatch = false;
				_sprintGap   = false;
			}

			_renderInput.Aim    = aim;
			_renderInput.Crouch = crouch;
			_renderInput.Sprint = sprint;
		}

		/// <summary>The toggled states, for the settings check (phase 6b).</summary>
		public string GetToggleState() => $"crouch {_crouchLatch}, sprint {_sprintLatch}, aim {_aimLatch}";
	}
}
