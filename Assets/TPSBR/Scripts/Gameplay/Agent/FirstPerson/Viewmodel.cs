namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.Rendering;
	using Fusion.Addons.KCC;

	/// <summary>
	/// First-person arms and gun for the agent the local camera looks through, drawn on the FPV layer by the scene camera's
	/// stacked viewmodel camera, so it never clips into walls.
	/// Guns with a model from Logan (GunData.Model) show it; the rest are block placeholders by class. Arms are Logan's
	/// (GunplaySettings.FirstPersonArms, playing his takes from the player's state in ViewmodelArms, the gun in their hand),
	/// or blocks when none is set.
	/// All motion is procedural until the Blender clips arrive (phase 10): sway, bob, kick, the bolt cycle, reload dip,
	/// swaps timed from the data and the punch.
	/// Phase 3c: attachments mount on the model (GunModelParts); the sight, muzzle and left hand follow them.
	/// </summary>
	[DefaultExecutionOrder(10000)]
	public sealed class Viewmodel : MonoBehaviour
	{
		// PUBLIC MEMBERS

		public bool      IsVisible => _isVisible;
		public Transform Muzzle    => _currentGun != null ? _currentGun.Muzzle : null;
		/// <summary>The Laser Sight's beam origin on the gun in hand, or null.</summary>
		public Transform Laser     => _currentGun != null && _currentGun.Parts != null ? _currentGun.Parts.Laser : null;
		/// <summary>The attachments and points of the gun in hand (model guns only).</summary>
		public GunModelParts CurrentParts => _currentGun != null ? _currentGun.Parts : null;
		/// <summary>The gun pivot (camera-relative pose of the gun in hand).</summary>
		public Transform GunPivot  => _gunPivot;
		/// <summary>0..1: how far the gun is lowered (swaps, reloads, a new gun being raised).</summary>
		public float     Lowered   => _lowered;
		/// <summary>Where the left arm reaches on the gun in hand (world).</summary>
		public Vector3   LeftHandPosition => _currentGun != null ? _gunPivot.TransformPoint(_currentGun.LeftHand) : default;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>Test hook (Gun Bench check): no sway, bob or kick, so the aimed pose is exact.</summary>
		public static bool DebugFreezeMotion;
#endif

		// PRIVATE MEMBERS

		[Header("Poses (viewmodel camera space, meters)")]
		[SerializeField, Tooltip("Where the gun's grip sits at the hip.")]
		private Vector3 _hipPosition = new Vector3(0.15f, -0.19f, 0.38f);
		[SerializeField, Tooltip("Distance in front of the eye where a block gun's rear sight sits when fully aimed (models put their own ADS eye point on the eye).")]
		private float   _adsSightDistance = 0.34f;
		[SerializeField]
		private Vector3 _rightShoulder = new Vector3(0.22f, -0.40f, -0.08f);
		[SerializeField]
		private Vector3 _leftShoulder = new Vector3(-0.22f, -0.40f, -0.02f);
		[SerializeField]
		private float   _armThickness = 0.07f;
		[SerializeField, Tooltip("Logan's arms: where the eye is relative to the model's head bone (right, up, forward).")]
		private Vector3 _armsEyeOffset = new Vector3(0.0f, 0.08f, 0.10f);
		[SerializeField, Tooltip("Logan's arms: every take plays at this speed, whatever your speed (Logan, 9 Oct: 0.8; the body plays at 1). Takes on a game timer still end with their action.")]
		private float   _armsTakeSpeed = 0.8f;
		[SerializeField, Tooltip("Logan's arms: the left hand holds the gun this far (m) below each gun's left-hand point, so the fingers stay clear of the sights (Logan, 9 Oct).")]
		private float   _leftHandLower = 0.025f;
		[SerializeField, Tooltip("A mantle up to this height plays the Vault take (as MovesLayer).")]
		private float   _vaultMaxHeight = 1.3f;

		[Header("Motion")]
		[SerializeField]
		private float   _swayAmount = 0.0012f;
		[SerializeField]
		private float   _swayMax = 0.03f;
		[SerializeField]
		private float   _swaySmoothing = 10.0f;
		[SerializeField]
		private float   _bobAmount = 0.008f;
		[SerializeField]
		private float   _bobFrequency = 9.0f;
		[SerializeField, Tooltip("Kick for weapons without GunData; Brownout guns use their recoil_feel numbers (phase 11e).")]
		private float   _kickBack = 0.03f;
		[SerializeField]
		private float   _kickPitch = 3.0f;
		[SerializeField]
		private float   _kickRecovery = 14.0f;
		[SerializeField]
		private float   _lowerSpeed = 6.0f;

		[Header("Punch")]
		[SerializeField]
		private Vector3 _fistRest = new Vector3(-0.12f, -0.16f, 0.30f);
		[SerializeField]
		private Vector3 _fistReach = new Vector3(-0.03f, -0.06f, 0.62f);

		private Agent      _agent;
		private Transform  _root;
		private Transform  _gunPivot;
		private Transform  _rightArm;
		private Transform  _leftArm;
		private Transform  _leftFist;
		private GunRig     _rifle;
		private GunRig     _sniper;
		private GunRig     _pistol;
		private GunRig     _currentGun;
		private Weapon     _currentWeapon;
		private int        _lastShotCount;
		private bool       _isVisible;
		private Material   _modelFallbackMaterial;
		private Transform  _equipmentItem;
		// Phase 10: Logan's grenade and grappling gun models, each centered in a holder that takes the block's place
		private readonly System.Collections.Generic.Dictionary<EEquipment, Transform> _equipmentModels = new System.Collections.Generic.Dictionary<EEquipment, Transform>();
		private Material   _equipmentMaterial;
		private Material   _armMaterial;
		private Color      _armColor;
		private ViewmodelArms _arms;
		private ScopeLens  _lens;
		private float      _leftGrip;
		private bool       _wasGrounded = true;
		private float      _landTime = -10.0f;
		private bool       _armsFalling;

		private readonly Dictionary<GunData, GunRig> _modelRigs = new Dictionary<GunData, GunRig>();

		// Phase 11e: kickback by gun class (recoil_feel in brownout_guns.json)
		private GunData    KickData     => _currentWeapon is BulletWeapon gun ? gun.GunData : null;
		private float      KickBack     => KickData != null ? KickData.KickBack : _kickBack;
		private float      KickPitch    => KickData != null ? KickData.KickPitch : _kickPitch;
		private float      KickRecovery => KickData != null ? KickData.KickRecovery : _kickRecovery;

		private Quaternion _lastCameraRotation;
		private Vector2    _sway;
		private float      _bobTime;
		private float      _kick;
		private float      _lowered;
		private float      _sprint;
		private float      _highReady;
		private float      _boltTime = -1.0f;

		// PUBLIC METHODS

		/// <summary>
		/// The viewmodel muzzle as a point the world camera draws at the same spot on screen
		/// (the two cameras share a position but not a FOV). Used as the start of first-person tracers.
		/// </summary>
		public bool TryGetWorldMuzzlePosition(out Vector3 position)
		{
			return TryGetWorldPosition(_currentGun != null ? _currentGun.Muzzle : null, out position);
		}

		/// <summary>Any viewmodel point (e.g. the laser) as a world point drawn at the same spot on screen.</summary>
		public bool TryGetWorldPosition(Transform viewmodelPoint, out Vector3 position)
		{
			position = default;

			if (_isVisible == false || _currentGun == null || _agent == null || viewmodelPoint == null)
				return false;

			SceneCamera sceneCamera = _agent.Context != null ? _agent.Context.Camera : null;
			if (sceneCamera == null || sceneCamera.ViewmodelCamera == null)
				return false;

			Vector3 viewport = sceneCamera.ViewmodelCamera.WorldToViewportPoint(viewmodelPoint.position);
			position = sceneCamera.Camera.ViewportToWorldPoint(viewport);
			return true;
		}

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_agent = GetComponent<Agent>();
		}

		private void OnDisable()
		{
			SetVisible(false);
		}

		private void OnDestroy()
		{
			if (_arms != null)
			{
				_arms.Dispose();
			}

			_lens?.Dispose();

			if (_root != null)
			{
				Destroy(_root.gameObject);
			}
		}

		private void LateUpdate()
		{
			SceneCamera sceneCamera = _agent != null && _agent.Object != null && _agent.Object.IsValid == true ? _agent.Context.Camera : null;

			bool shouldBeVisible = sceneCamera != null && sceneCamera.ViewmodelCamera != null && _agent.IsObserved == true && _agent.Health.IsAlive == true && _agent.Health.IsDowned == false && _agent.Character.IsThirdPersonView == false;
			if (shouldBeVisible == false)
			{
				SetVisible(false);
				_lens?.Update(null, null, null, 0.0f, 0.0f);
				return;
			}

			if (_root == null)
			{
				Build();
			}

			if (_root.parent != sceneCamera.ViewmodelCamera.transform)
			{
				_root.SetParent(sceneCamera.ViewmodelCamera.transform, false);
				_lastCameraRotation = sceneCamera.Camera.transform.rotation;
			}

			SetVisible(true);
			UpdateGun();
			UpdateMotion(sceneCamera);
			UpdateEquipmentHand();

			if (_lens != null)
			{
				bool shown = sceneCamera.Camera.isActiveAndEnabled == true && _agent.Context.IsVisible == true;
				_lens.Update(shown == true ? sceneCamera : null, _currentWeapon as BulletWeapon, CurrentParts, _agent.Character.AimProgress, _agent.Character.GetHipVerticalFOV());
			}
		}

		// PRIVATE METHODS

		private void SetVisible(bool visible)
		{
			if (_isVisible == visible)
				return;

			_isVisible = visible;

			if (_root != null)
			{
				_root.gameObject.SetActive(visible);
			}
		}

		private void UpdateGun()
		{
			Weapons weapons       = _agent.Weapons;
			Weapon  currentWeapon = weapons.CurrentWeapon;

			if (currentWeapon != _currentWeapon)
			{
				_currentWeapon = currentWeapon;
				_lowered       = 1.0f; // Raise the new gun from below
				_kick          = 0.0f;
				_boltTime      = -1.0f;
				_lastShotCount = currentWeapon is FirearmWeapon firearm ? firearm.ShotCount : 0;
			}

			GunRig gun = null;
			if (currentWeapon is BulletWeapon bulletWeapon && bulletWeapon.GunData != null)
			{
				gun = GetModelRig(bulletWeapon.GunData);
				if (gun == null)
				{
					gun = bulletWeapon.GunData.Class == EGunClass.Sniper ? _sniper : _rifle;
				}
			}
			else if (currentWeapon != null)
			{
				switch (currentWeapon.HitType)
				{
					case EHitType.Rifle:  gun = _rifle;  break;
					case EHitType.Sniper: gun = _sniper; break;
					case EHitType.Pistol: gun = _pistol; break;
				}
			}

			if (gun != _currentGun)
			{
				if (_currentGun != null)
				{
					_currentGun.Root.gameObject.SetActive(false);
				}

				if (gun != null)
				{
					gun.Root.gameObject.SetActive(true);
				}

				_currentGun = gun;
			}

			if (_currentGun != null && _currentGun.Parts != null)
			{
				_currentGun.Refresh(currentWeapon as BulletWeapon);
			}

			bool hasGun = _currentGun != null;
			SetArmsActive(hasGun, hasGun);
		}

		private void UpdateMotion(SceneCamera sceneCamera)
		{
			if (_currentGun == null && _arms == null)
			{
				_leftFist.gameObject.SetActive(false);
				return;
			}

			float deltaTime = Time.deltaTime;
			float aim       = Mathf.SmoothStep(0.0f, 1.0f, _agent.Character.AimProgress);

			// Sway: the gun lags behind camera turns
			Quaternion cameraRotation = sceneCamera.Camera.transform.rotation;
			Vector3    turn           = (Quaternion.Inverse(_lastCameraRotation) * cameraRotation).eulerAngles;
			_lastCameraRotation = cameraRotation;

			// Turning right moves the gun left on screen; looking down (positive pitch) moves it up
			Vector2 turnDelta   = new Vector2(-Mathf.DeltaAngle(0.0f, turn.y), Mathf.DeltaAngle(0.0f, turn.x));
			Vector2 targetSway  = Vector2.ClampMagnitude(turnDelta * _swayAmount / Mathf.Max(deltaTime * 60.0f, 0.01f), _swayMax) * Mathf.Lerp(1.0f, 0.2f, aim);
			_sway = Vector2.Lerp(_sway, targetSway, 1.0f - Mathf.Exp(-_swaySmoothing * deltaTime));

			// Holo: calmer while aimed (cosmetic, Logan's 3c answer)
			BulletWeapon aimedGun  = _currentWeapon as BulletWeapon;
			float        aimedCalm = Mathf.Lerp(1.0f, aimedGun != null && aimedGun.GunData != null ? aimedGun.Stats.AimIdleMotion : 1.0f, aim);

			// Bob with movement speed
			float speed = _agent.Character.CharacterController.Data.RealSpeed;
			_bobTime += deltaTime * _bobFrequency * Mathf.Clamp01(speed / 4.0f);
			float   bobStrength = Mathf.Clamp01(speed / 4.0f) * Mathf.Lerp(1.0f, 0.15f, aim) * _bobAmount;
			Vector3 bob         = new Vector3(Mathf.Cos(_bobTime * 0.5f) * bobStrength, Mathf.Abs(Mathf.Sin(_bobTime * 0.5f)) * -bobStrength, 0.0f) * aimedCalm;
			Vector2 sway        = _sway * aimedCalm;

			// Logan's takes move the arms themselves (walk, sprint, dash, mantle, heal)
			if (_arms != null)
			{
				bob = Vector3.zero;
			}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (DebugFreezeMotion == true)
			{
				bob   = Vector3.zero;
				sway  = Vector2.zero;
				_kick = 0.0f;
			}
#endif

			// Kick per shot; bolt guns also work the bolt between shots
			BulletWeapon bulletWeapon = _currentWeapon as BulletWeapon;
			if (_currentWeapon is FirearmWeapon firearm)
			{
				int shotCount = firearm.ShotCount;
				if (shotCount != _lastShotCount)
				{
					_kick = Mathf.Min(_kick + Mathf.Max(1, shotCount - _lastShotCount), 2.0f);
					_lastShotCount = shotCount;

					if (bulletWeapon != null && bulletWeapon.GunData.FireMode == EFireMode.Bolt)
					{
						_boltTime = 0.0f;
					}
				}
			}

			_kick = Mathf.MoveTowards(_kick, 0.0f, deltaTime * KickRecovery * Mathf.Max(_kick, 0.2f));

			float bolt = 0.0f;
			if (_boltTime >= 0.0f && bulletWeapon != null)
			{
				_boltTime += deltaTime;

				// Kick settles, then the bolt is worked for the rest of the cycle
				float cycle    = Mathf.Max(0.1f, bulletWeapon.GunData.FireInterval);
				float progress = Mathf.Clamp01((_boltTime - 0.12f) / Mathf.Max(0.05f, cycle - 0.17f));
				bolt = Mathf.Sin(progress * Mathf.PI);

				if (_boltTime >= cycle)
				{
					_boltTime = -1.0f;
				}
			}

			Weapons   weapons    = _agent.Weapons;
			EMoveMode moveMode   = _agent.Character.CharacterController.Data.MoveMode;
			bool      isMantling = moveMode == EMoveMode.Mantle;

			// Lowered: timed swaps follow the data (down over the holster time, up over the draw time); reloads and mantles ease
			bool isReloading = _currentWeapon is FirearmWeapon reloadingFirearm && reloadingFirearm.IsReloading == true;
			float swapLowered;
			if (TryGetSwapLowered(weapons, out swapLowered) == true)
			{
				_lowered = swapLowered;
			}
			else
			{
				bool isSwitching = weapons.PendingWeaponSlot != weapons.CurrentWeaponSlot;
				bool isBusy      = _agent.Health.IsHealing == true || _agent.Health.IsReviving == true; // Phase 4: the gun is down while healing or reviving
				isBusy |= _agent.EquipmentActions != null && _agent.EquipmentActions.IsThrowing == true;          // Phase 5b: and while throwing (the grapple keeps it up)
				if (_arms != null)
				{
					// Those have takes; only reloads and swaps still dip the gun
					isBusy     = false;
					isMantling = false;
				}
				_lowered = Mathf.MoveTowards(_lowered, isReloading == true || isSwitching == true || isMantling == true || isBusy == true ? 1.0f : 0.0f, deltaTime * _lowerSpeed);
			}

			float lowered = Mathf.SmoothStep(0.0f, 1.0f, _lowered);

			// Punch: the gun dips right while the left fist jabs forward
			float punch     = weapons.PunchProgress;
			float punchDip  = punch > 0.0f ? Mathf.Sin(Mathf.Clamp01(punch * 1.4f) * Mathf.PI) : 0.0f;
			float hitPoint  = GunplaySettings.Current.PunchHitTime / Mathf.Max(0.01f, GunplaySettings.Current.PunchDuration);
			float punchJab  = punch <= 0.0f ? 0.0f : punch < hitPoint ? Mathf.SmoothStep(0.0f, 1.0f, punch / hitPoint) : 1.0f - Mathf.SmoothStep(0.0f, 1.0f, (punch - hitPoint) / (1.0f - hitPoint));

			// Sprint tucks the gun low and across; dash holds it at high ready
			_sprint    = Mathf.MoveTowards(_sprint,    moveMode == EMoveMode.Sprint ? 1.0f : 0.0f, deltaTime * _lowerSpeed * 1.5f);
			_highReady = Mathf.MoveTowards(_highReady, moveMode == EMoveMode.Dash   ? 1.0f : 0.0f, deltaTime * _lowerSpeed * 1.5f);
			float sprint    = Mathf.SmoothStep(0.0f, 1.0f, _sprint);
			float highReady = Mathf.SmoothStep(0.0f, 1.0f, _highReady);

			if (_arms != null)
			{
				UpdateAnimatedArms(aim, sway, lowered, bolt);
				return;
			}

			// Hip to aimed: the sight lands on the screen center
			Vector3 adsPosition = new Vector3(0.0f, 0.0f, _currentGun.AdsDistance) - _currentGun.SightLocalPosition;
			Vector3 position    = Vector3.Lerp(_hipPosition, adsPosition, aim);

			position += new Vector3(sway.x, sway.y, 0.0f) + bob;
			position += new Vector3(0.0f, 0.0f, -KickBack * _kick * Mathf.Lerp(1.0f, 0.5f, aim));
			position += new Vector3(0.02f, -0.16f, -0.04f) * lowered;
			position += new Vector3(-0.06f, -0.08f, -0.06f) * sprint;
			position += new Vector3(-0.04f, 0.10f, -0.10f) * highReady;
			position += new Vector3(0.05f, -0.10f, -0.05f) * punchDip;
			position += new Vector3(0.0f, -0.015f, -0.02f) * bolt;

			Quaternion rotation = Quaternion.Euler(
				-KickPitch * _kick * Mathf.Lerp(1.0f, 0.4f, aim) + 35.0f * lowered + 20.0f * sprint - 50.0f * highReady + 25.0f * punchDip + 4.0f * bolt,
				-35.0f * sprint + 15.0f * punchDip,
				-25.0f * lowered + 10.0f * sprint - 20.0f * punchDip - 25.0f * bolt);

			_gunPivot.localPosition = position;
			_gunPivot.localRotation = rotation;

			PlaceArm(_rightArm, _rightShoulder, _gunPivot.TransformPoint(_currentGun.RightHand));

			_leftFist.gameObject.SetActive(punch > 0.0f);
			if (punch > 0.0f)
			{
				Vector3 fist = Vector3.Lerp(_fistRest, _fistReach, punchJab);
				_leftFist.localPosition = fist;
				PlaceLeft(_root.TransformPoint(fist), false);
			}
			else
			{
				PlaceLeft(_gunPivot.TransformPoint(_currentGun.LeftHand), true);
			}
		}

		/// <summary>
		/// Phase 10: the equipment in hand is Logan's model (grenade or grappling gun) where the block would be; no model keeps the block.
		/// </summary>
		private void UpdateEquipmentHand()
		{
			UpdateEquipmentBlock();

			bool       shown = _equipmentItem.gameObject.activeSelf;
			EEquipment kind  = _agent.EquipmentActions != null ? _agent.EquipmentActions.CurrentKind : EEquipment.Frag;

			foreach (var pair in _equipmentModels)
			{
				bool show = shown == true && pair.Key == kind;
				if (show == true)
				{
					pair.Value.SetPositionAndRotation(_equipmentItem.position, _equipmentItem.rotation);
				}

				if (pair.Value.gameObject.activeSelf != show)
				{
					pair.Value.gameObject.SetActive(show);
				}
			}

			if (shown == true && _equipmentModels.ContainsKey(kind) == true)
			{
				_equipmentItem.gameObject.SetActive(false);
			}
		}

		/// <summary>
		/// Phase 5b: the equipment in the left hand. A grenade comes up from below over the windup, is held while a frag cooks, and goes
		/// forward at the release; the grapple is held out front from the press until the pull ends. Also tints the arms while Camouflage
		/// makes you see-through to others.
		/// </summary>
		private void UpdateEquipmentBlock()
		{
			if (_armMaterial != null)
			{
				Color tint = _agent.IsCamouflaged == true ? Color.Lerp(_armColor, new Color(0.45f, 0.75f, 0.85f), 0.6f) : _armColor;
				_armMaterial.SetColor("_BaseColor", tint);
			}

			if (_arms != null)
			{
				_arms.SetTint(_agent.IsCamouflaged == true ? 0.6f : 0.0f, new Color(0.45f, 0.75f, 0.85f));
			}

			AgentEquipment equipment = _agent.EquipmentActions;
			if (equipment == null || equipment.IsBusy == false)
			{
				_equipmentItem.gameObject.SetActive(false);
				return;
			}

			EquipmentSettings settings = EquipmentSettings.Current;
			EEquipment        kind     = equipment.CurrentKind;
			float             elapsed  = equipment.GetElapsed(true);
			Vector3           hand;

			if (kind == EEquipment.Grapple)
			{
				if (_agent.EquipReadyTick > 0 && equipment.IsHookOut == false && equipment.HasReleased == true)
				{
					_equipmentItem.gameObject.SetActive(false); // A miss: put away
					return;
				}

				float raise = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(elapsed / Mathf.Max(0.05f, settings.GrappleDeployTime)));
				hand = Vector3.Lerp(new Vector3(-0.22f, -0.40f, 0.20f), new Vector3(-0.20f, -0.12f, 0.42f), raise);

				_equipmentItem.localScale    = new Vector3(0.06f, 0.07f, 0.24f);
				_equipmentItem.localRotation = Quaternion.identity;
			}
			else
			{
				float windup  = Mathf.Max(0.05f, settings.PressToRelease);
				float release = equipment.GetReleaseElapsed(true);

				if (equipment.HasReleased == true && release > 0.12f)
				{
					_equipmentItem.gameObject.SetActive(false);
					if (_currentGun == null)
					{
						SetArmsActive(false, false);
					}
					return;
				}

				Vector3 low  = new Vector3(-0.16f, -0.45f, 0.15f);
				Vector3 hold = new Vector3(-0.20f, -0.06f, 0.22f);
				Vector3 out_ = new Vector3(-0.06f, 0.04f, 0.70f);

				if (equipment.HasReleased == true)
				{
					hand = Vector3.Lerp(hold, out_, Mathf.Clamp01(release / 0.12f));
				}
				else
				{
					hand = Vector3.Lerp(low, hold, Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(elapsed / (windup * 0.7f))));

					// Cooking: a little tremble
					if (elapsed > windup)
					{
						hand += new Vector3(Mathf.Sin(elapsed * 41.0f), Mathf.Sin(elapsed * 37.0f), 0.0f) * 0.003f;
					}
				}

				_equipmentItem.localScale    = new Vector3(0.06f, 0.09f, 0.06f);
				_equipmentItem.localRotation = Quaternion.Euler(-20.0f, 0.0f, 15.0f);
			}

			_equipmentMaterial.SetColor("_BaseColor", settings.GetEquipmentColor(kind));
			_equipmentItem.gameObject.SetActive(true);
			_equipmentItem.localPosition = hand;

			// Logan's arms: the grenade is in the left hand of the throw take; the grapple is held out as before
			if (_arms != null && kind != EEquipment.Grapple)
			{
				_equipmentItem.position = _arms.LeftPalm;
				return;
			}

			SetArmsActive(_currentGun != null, true);
			PlaceLeft(_root.TransformPoint(hand), false);
		}

		private void SetArmsActive(bool right, bool left)
		{
			// Logan's arms always show in first person (Unarmed_Idle without a gun)
			if (_arms != null)
			{
				right = false;
				left  = false;
			}

			_rightArm.gameObject.SetActive(right);
			_leftArm.gameObject.SetActive(left);
		}

		/// <summary>The left hand at a point: block arm, or Logan's arm reaching from its take.</summary>
		private void PlaceLeft(Vector3 handWorld, bool onGun)
		{
			if (_arms == null)
			{
				PlaceArm(_leftArm, _leftShoulder, handWorld);
				return;
			}

			_arms.ReachLeft(handWorld, 1.0f);
		}

		/// <summary>
		/// Phase 10, Logan's arms: plays the take for what the player is doing, then puts the gun in the take's hand (the Weapon bone)
		/// and moves the whole arms for aiming (the sight onto the screen center), kick, sway and the reload / swap dip. In the takes
		/// that hold the gun with both hands, the left hand reaches to this gun's own left-hand point.
		/// </summary>
		private void UpdateAnimatedArms(float aim, Vector2 sway, float lowered, float bolt)
		{
			_arms.SpeedScale = _armsTakeSpeed;

			bool holdsGun = SelectArmsTake(out bool leftOnGun);

			_arms.Evaluate(Time.deltaTime);

			holdsGun &= _currentGun != null;
			if (_gunPivot.gameObject.activeSelf != holdsGun)
			{
				_gunPivot.gameObject.SetActive(holdsGun);
			}

			_leftGrip = Mathf.MoveTowards(_leftGrip, holdsGun == true && leftOnGun == true ? 1.0f : 0.0f, Time.deltaTime * 8.0f);

			if (holdsGun == false)
				return;

			_arms.GetGunPose(out Vector3 takePosition, out Quaternion takeRotation);

			Vector3    adsPosition = new Vector3(0.0f, 0.0f, _currentGun.AdsDistance) - _currentGun.SightLocalPosition;
			Vector3    position    = Vector3.Lerp(takePosition, adsPosition, aim);
			Quaternion rotation    = Quaternion.Slerp(takeRotation, Quaternion.identity, aim);

			position += new Vector3(sway.x, sway.y, 0.0f);
			position += rotation * new Vector3(0.0f, 0.0f, -KickBack * _kick * Mathf.Lerp(1.0f, 0.5f, aim));
			position += new Vector3(0.02f, -0.16f, -0.04f) * lowered;
			position += new Vector3(0.0f, -0.015f, -0.02f) * bolt;
			rotation  = rotation * Quaternion.Euler(-KickPitch * _kick * Mathf.Lerp(1.0f, 0.4f, aim) + 35.0f * lowered + 4.0f * bolt, 0.0f, -25.0f * lowered - 25.0f * bolt);

			_arms.MoveRoot(takePosition, takeRotation, position, rotation);

			_gunPivot.localPosition = position;
			_gunPivot.localRotation = rotation;

			_arms.ReachLeft(_gunPivot.TransformPoint(_currentGun.LeftHand + Vector3.down * _leftHandLower), Mathf.SmoothStep(0.0f, 1.0f, _leftGrip));
		}

		/// <summary>True when there's nothing to stand on within height below the feet.</summary>
		private bool IsGroundFarBelow(Vector3 feet, float height)
		{
			return _agent.Runner.GetPhysicsScene().Raycast(feet + Vector3.up * 0.1f, Vector3.down, height + 0.1f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == false;
		}

		/// <summary>
		/// The take for the player's state, as the body's layers pick theirs (Hands, Moves, Override, locomotion). True when the take
		/// holds the gun in the right hand; leftOnGun when the left hand is on it too.
		/// </summary>
		private bool SelectArmsTake(out bool leftOnGun)
		{
			leftOnGun = false;

			Health         health    = _agent.Health;
			Weapons        weapons   = _agent.Weapons;
			AgentEquipment equipment = _agent.EquipmentActions;
			KCCData        data      = _agent.Character.CharacterController.Data;
			bool           armed     = _currentGun != null;
			float          tickTime  = _agent.Runner.DeltaTime;

			bool grounded = data.IsGrounded == true;
			if (grounded == true && _wasGrounded == false && armed == true)
			{
				_landTime = Time.time;
			}
			_wasGrounded = grounded;

			if (grounded == true)
			{
				_armsFalling = false;
			}

			if (health.IsHealing == true)
			{
				_arms.PlaySynced(health.HealingItem == EHealItem.Bandage ? "Heal_Bandage" : "Heal_Syringe", health.HealProgress, 0.15f);
				return false;
			}

			if (health.IsReviving == true)
			{
				_arms.PlaySynced("Revive", health.ReviveProgress, 0.15f);
				return true;
			}

			if (weapons.IsPunching == true)
			{
				_arms.PlaySynced(armed == true ? "Melee" : "Unarmed_Jab_R", weapons.PunchProgress, 0.06f);
				return armed;
			}

			if (equipment != null && equipment.IsThrowing == true)
			{
				// The windup takes the first part of the take, the release the rest
				const float releaseAt = 0.45f;
				float windup  = Mathf.Max(0.05f, EquipmentSettings.Current.PressToRelease);
				float release = equipment.GetReleaseElapsed(true);
				float time    = equipment.HasReleased == true
					? releaseAt + (1.0f - releaseAt) * Mathf.Clamp01(release / Mathf.Max(0.05f, _arms.GetLength("Grenade_Throw") * (1.0f - releaseAt)))
					: releaseAt * Mathf.Clamp01(equipment.GetElapsed(true) / windup);

				_arms.PlaySynced("Grenade_Throw", time, 0.1f);
				return armed;
			}

			switch (data.MoveMode)
			{
				case EMoveMode.Mantle:
				{
					string take = data.MantleEnd.y - data.MantleStart.y <= _vaultMaxHeight ? "Vault" : "Mantle";
					_arms.PlaySynced(take, (_agent.Runner.Tick - data.ModeStartTick) * tickTime / Mathf.Max(0.05f, data.MantleDuration), 0.08f);
					return armed;
				}
				case EMoveMode.Dash:
				{
					_arms.PlaySynced("Dash", (_agent.Runner.Tick - data.ModeStartTick) * tickTime / Mathf.Max(0.05f, MovementSettings.Current.DashDuration), 0.1f);
					return armed;
				}
				case EMoveMode.LandRoll:
				{
					// The second half of the landing roll is seen in first person (phase 10)
					_arms.PlaySynced("Drop_Roll", (_agent.Runner.Tick - data.ModeStartTick) * tickTime / Mathf.Max(0.1f, MatchSettings.Current.LandRollTime), 0.1f);
					return false;
				}
			}

			float speed = data.RealSpeed;

			if (armed == false)
			{
				if (data.MoveMode == EMoveMode.Sprint)
				{
					_arms.Play("Unarmed_Sprint");
				}
				else if (speed > 0.5f)
				{
					_arms.Play("Unarmed_Walk");
				}
				else
				{
					_arms.Play("Unarmed_Idle");
				}

				return false;
			}

			leftOnGun = true;

			if (data.Stance == EStance.Slide)
			{
				_arms.Play("Slide");
				return true;
			}

			if (grounded == false)
			{
				// A normal jump keeps the Jump take (both hands on the gun); the Fall take only once coming down with the ground more than
				// FallAnimationHeight below (Logan, 9 Oct), and then until landing
				if (_armsFalling == false && data.RealVelocity.y < 0.0f && IsGroundFarBelow(data.TargetPosition, MovementSettings.Current.FallAnimationHeight) == true)
				{
					_armsFalling = true;
				}

				_arms.Play(_armsFalling == true ? "Fall" : "Jump", 1.0f, 0.2f);
				leftOnGun = _armsFalling == false;
				return true;
			}

			if (Time.time - _landTime < _arms.GetLength("Jump_Land"))
			{
				_arms.Play("Jump_Land", 1.0f, 0.08f);
				return true;
			}

			if (data.Stance == EStance.Crouch)
			{
				if (speed > 0.5f)
				{
					_arms.Play("Crouch_Walk");
				}
				else
				{
					_arms.Play("Crouch_Idle");
				}

				return true;
			}

			if (data.MoveMode == EMoveMode.Sprint)
			{
				_arms.Play("Sprint");
			}
			else if (speed > 0.5f)
			{
				_arms.Play("Walk");
			}
			else
			{
				_arms.Play("Idle");
			}

			return true;
		}

		/// <summary>
		/// During a timed gun swap: 0 → 1 while the old gun goes down (its holster time), 1 → 0 while the new one comes up (its draw time).
		/// </summary>
		private bool TryGetSwapLowered(Weapons weapons, out float lowered)
		{
			lowered = 0.0f;

			float duration = weapons.SwitchDuration;
			float elapsed  = weapons.SwitchElapsed;
			if (duration <= 0.0f || elapsed >= duration)
				return false;

			float armTime = weapons.SwitchArmTime;
			if (elapsed < armTime)
			{
				lowered = armTime > 0.0f ? elapsed / armTime : 1.0f;
			}
			else
			{
				float drawTime = duration - armTime;
				lowered = drawTime > 0.0f ? 1.0f - (elapsed - armTime) / drawTime : 0.0f;
			}

			lowered = Mathf.Clamp01(lowered);
			return true;
		}

		private void PlaceArm(Transform arm, Vector3 shoulderLocal, Vector3 handWorld)
		{
			Vector3 handLocal = _root.InverseTransformPoint(handWorld);
			Vector3 direction = handLocal - shoulderLocal;
			float   length    = direction.magnitude;

			if (length < 0.001f)
				return;

			arm.localPosition = shoulderLocal + direction * 0.5f;
			arm.localRotation = Quaternion.LookRotation(direction / length, Vector3.up);
			arm.localScale    = new Vector3(_armThickness, _armThickness, length);
		}

		/// <summary>
		/// One instance per gun model, built the first time that gun is in hand. Points come from the model's empties
		/// (`<Name>_ADS_Eye`, `<Name>_Muzzle`, `<Name>_LeftHand`); the grip is the model's origin.
		/// </summary>
		private GunRig GetModelRig(GunData gunData)
		{
			if (gunData.Model == null)
				return null;

			if (_modelRigs.TryGetValue(gunData, out GunRig rig) == true)
				return rig;

			rig = new GunRig(gunData.GunName, _gunPivot);

			GameObject model = Instantiate(gunData.Model, rig.Root, false);
			model.transform.localPosition = Vector3.zero;
			model.transform.localRotation = Quaternion.identity;
			model.transform.localScale    = Vector3.one;

			PrepareViewmodelObject(model);

			var parts = new GunModelParts(model.transform, PrepareViewmodelObject);

			Vector3 sight = parts.GunAdsEye   != null ? rig.Root.InverseTransformPoint(parts.GunAdsEye.position)   : new Vector3(0.0f, 0.10f, -0.06f);
			Vector3 tip   = parts.GunMuzzle   != null ? rig.Root.InverseTransformPoint(parts.GunMuzzle.position)   : new Vector3(0.0f, 0.05f, 0.60f);
			Vector3 hand  = parts.GunLeftHand != null ? rig.Root.InverseTransformPoint(parts.GunLeftHand.position) : new Vector3(0.0f, -0.01f, 0.30f);

			rig.SetPoints(sight, tip, Vector3.zero, hand, 0.0f);
			rig.SetParts(parts);
			rig.Root.gameObject.SetActive(false);

			if (parts.GunAdsEye == null || parts.GunMuzzle == null)
			{
				Debug.LogWarning($"Viewmodel: {gunData.Model.name} is missing its _ADS_Eye or _Muzzle empty; using rough defaults.", gunData.Model);
			}

			_modelRigs[gunData] = rig;
			return rig;
		}

		/// <summary>FPV layer, no shadows, and a URP material where the import has none (gun and attachment models).</summary>
		private void PrepareViewmodelObject(GameObject model)
		{
			foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
			{
				node.gameObject.layer = ObjectLayer.FPV;
			}

			foreach (Renderer modelRenderer in model.GetComponentsInChildren<Renderer>(true))
			{
				modelRenderer.shadowCastingMode = ShadowCastingMode.Off;
				modelRenderer.receiveShadows    = false;

				// Imported materials without a URP shader show pink; give them a plain lit one
				Material[] materials = modelRenderer.sharedMaterials;
				for (int i = 0; i < materials.Length; ++i)
				{
					if (materials[i] == null || materials[i].shader == null || materials[i].shader.name.StartsWith("Universal Render Pipeline") == false)
					{
						materials[i] = GetModelFallbackMaterial(materials[i]);
					}
				}

				modelRenderer.sharedMaterials = materials;
			}
		}

		private Material GetModelFallbackMaterial(Material source)
		{
			Color color = source != null && source.HasProperty("_Color") == true ? source.color : new Color(0.25f, 0.25f, 0.27f);
			var material = CreateMaterial(color);
			if (source != null && source.HasProperty("_MainTex") == true && source.mainTexture != null)
			{
				material.SetTexture("_BaseMap", source.mainTexture);
			}

			return material;
		}

		private void Build()
		{
			_root = new GameObject($"{name}_Viewmodel").transform;
			_root.gameObject.layer = ObjectLayer.FPV;

			_gunPivot = CreateNode("GunPivot", _root);

			// Phase 11a: magnified optics zoom inside their lens
			ArtSettings lensArt = ArtSettings.Current;
			if (lensArt != null && lensArt.ScopeLens != null)
			{
				_lens = new ScopeLens(_root, ObjectLayer.FPV, lensArt.ScopeLens);
			}

			Material gunMaterial  = CreateMaterial(new Color(0.18f, 0.18f, 0.20f));
			Material partMaterial = CreateMaterial(new Color(0.32f, 0.30f, 0.26f));
			Material armMaterial  = CreateMaterial(new Color(0.28f, 0.33f, 0.24f));

			_rightArm = CreateBlock("RightArm", _root, Vector3.zero, Vector3.one, armMaterial);
			_leftArm  = CreateBlock("LeftArm",  _root, Vector3.zero, Vector3.one, armMaterial);
			_leftFist = CreateBlock("LeftFist", _root, _fistRest, new Vector3(0.08f, 0.09f, 0.10f), armMaterial);
			_leftFist.gameObject.SetActive(false);

			// Phase 5b: the grenade or grapple in the left hand, recolored per item
			_armMaterial       = armMaterial;
			_armColor          = armMaterial.GetColor("_BaseColor");
			_equipmentMaterial = CreateMaterial(Color.gray);
			_equipmentItem     = CreateBlock("EquipmentItem", _root, Vector3.zero, Vector3.one, _equipmentMaterial);
			_equipmentItem.gameObject.SetActive(false);

			ArtSettings art = ArtSettings.Current;
			for (EEquipment kind = EEquipment.Frag; kind <= EEquipment.Grapple; ++kind)
			{
				GameObject model = art != null ? art.GetEquipmentModel(kind) : null;
				MeshFilter filter = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
				if (filter == null)
					continue;

				Transform  holder   = CreateNode($"Equipment_{kind}", _root);
				GameObject instance = Instantiate(model, holder, false);
				instance.transform.localPosition = kind == EEquipment.Grapple ? new Vector3(0.0f, 0.0f, -filter.sharedMesh.bounds.center.z) : -filter.sharedMesh.bounds.center;
				PrepareViewmodelObject(instance);

				holder.gameObject.SetActive(false);
				_equipmentModels[kind] = holder;
			}

			// Phase 10: Logan's arms replace the blocks
			GameObject armsPrefab = GunplaySettings.Current.FirstPersonArms;
			if (armsPrefab != null)
			{
				_arms = new ViewmodelArms(armsPrefab, GunplaySettings.Current.FirstPersonArmsClips, _root, _armsEyeOffset);
				_arms.SpeedScale = _armsTakeSpeed;
				_rightArm.gameObject.SetActive(false);
				_leftArm.gameObject.SetActive(false);
			}

			// Gun origin = right hand on the grip; +Z points down the barrel
			_rifle = new GunRig("Rifle", _gunPivot);
			_rifle.AddBlock(new Vector3(0.0f,  0.035f,  0.10f), new Vector3(0.05f,  0.08f,  0.40f), gunMaterial);  // Receiver
			_rifle.AddBlock(new Vector3(0.0f,  0.045f,  0.44f), new Vector3(0.025f, 0.025f, 0.28f), gunMaterial);  // Barrel
			_rifle.AddBlock(new Vector3(0.0f,  0.020f,  0.34f), new Vector3(0.045f, 0.05f,  0.16f), partMaterial); // Handguard
			_rifle.AddBlock(new Vector3(0.0f, -0.050f,  0.10f), new Vector3(0.035f, 0.12f,  0.06f), partMaterial); // Magazine
			_rifle.AddBlock(new Vector3(0.0f, -0.040f, -0.01f), new Vector3(0.03f,  0.09f,  0.04f), gunMaterial);  // Grip
			_rifle.AddBlock(new Vector3(0.0f,  0.010f, -0.22f), new Vector3(0.04f,  0.08f,  0.22f), partMaterial); // Stock
			_rifle.AddBlock(new Vector3(0.0f,  0.090f, -0.06f), new Vector3(0.03f,  0.03f,  0.02f), gunMaterial);  // Rear sight
			_rifle.AddBlock(new Vector3(0.0f,  0.085f,  0.52f), new Vector3(0.006f, 0.04f,  0.01f), gunMaterial);  // Front post
			_rifle.SetPoints(sight: new Vector3(0.0f, 0.105f, -0.06f), muzzle: new Vector3(0.0f, 0.045f, 0.58f), rightHand: new Vector3(0.0f, -0.04f, -0.01f), leftHand: new Vector3(0.0f, -0.01f, 0.33f), adsDistance: _adsSightDistance);

			_sniper = new GunRig("Sniper", _gunPivot);
			_sniper.AddBlock(new Vector3(0.0f,  0.035f,  0.10f), new Vector3(0.055f, 0.085f, 0.44f), gunMaterial);  // Receiver
			_sniper.AddBlock(new Vector3(0.0f,  0.045f,  0.60f), new Vector3(0.03f,  0.03f,  0.56f), gunMaterial);  // Barrel
			_sniper.AddBlock(new Vector3(0.0f, -0.050f,  0.12f), new Vector3(0.04f,  0.09f,  0.07f), partMaterial); // Magazine
			_sniper.AddBlock(new Vector3(0.0f, -0.040f, -0.01f), new Vector3(0.03f,  0.09f,  0.04f), gunMaterial);  // Grip
			_sniper.AddBlock(new Vector3(0.0f,  0.000f, -0.26f), new Vector3(0.045f, 0.10f,  0.28f), partMaterial); // Stock
			_sniper.AddBlock(new Vector3(0.0f,  0.090f, -0.06f), new Vector3(0.03f,  0.03f,  0.02f), gunMaterial);  // Rear sight
			_sniper.AddBlock(new Vector3(0.0f,  0.085f,  0.84f), new Vector3(0.006f, 0.04f,  0.01f), gunMaterial);  // Front post
			_sniper.SetPoints(sight: new Vector3(0.0f, 0.105f, -0.06f), muzzle: new Vector3(0.0f, 0.045f, 0.88f), rightHand: new Vector3(0.0f, -0.04f, -0.01f), leftHand: new Vector3(0.0f, -0.005f, 0.36f), adsDistance: _adsSightDistance);

			_pistol = new GunRig("Pistol", _gunPivot);
			_pistol.AddBlock(new Vector3(0.0f,  0.040f,  0.06f), new Vector3(0.032f, 0.035f, 0.19f), gunMaterial);  // Slide
			_pistol.AddBlock(new Vector3(0.0f,  0.000f,  0.00f), new Vector3(0.03f,  0.11f,  0.045f), partMaterial); // Grip
			_pistol.AddBlock(new Vector3(0.0f,  0.064f, -0.02f), new Vector3(0.02f,  0.012f, 0.01f), gunMaterial);  // Rear sight
			_pistol.AddBlock(new Vector3(0.0f,  0.064f,  0.14f), new Vector3(0.005f, 0.012f, 0.01f), gunMaterial);  // Front post
			_pistol.SetPoints(sight: new Vector3(0.0f, 0.072f, -0.02f), muzzle: new Vector3(0.0f, 0.040f, 0.16f), rightHand: new Vector3(0.0f, -0.02f, 0.0f), leftHand: new Vector3(-0.025f, -0.03f, 0.01f), adsDistance: _adsSightDistance);

			_rifle.Root.gameObject.SetActive(false);
			_sniper.Root.gameObject.SetActive(false);
			_pistol.Root.gameObject.SetActive(false);
			_currentGun = null;

			_root.gameObject.SetActive(false);
			_isVisible = false;
		}

		private static Transform CreateNode(string nodeName, Transform parent)
		{
			var node = new GameObject(nodeName).transform;
			node.gameObject.layer = ObjectLayer.FPV;
			node.SetParent(parent, false);
			return node;
		}

		private static Transform CreateBlock(string blockName, Transform parent, Vector3 position, Vector3 size, Material material)
		{
			var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
			block.name  = blockName;
			block.layer = ObjectLayer.FPV;

			// Visual only: no collider, no shadows
			var collider = block.GetComponent<Collider>();
			collider.enabled = false;
			Destroy(collider);

			var renderer = block.GetComponent<MeshRenderer>();
			renderer.sharedMaterial    = material;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows    = false;

			block.transform.SetParent(parent, false);
			block.transform.localPosition = position;
			block.transform.localScale    = size;

			return block.transform;
		}

		private static Material CreateMaterial(Color color)
		{
			var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
			material.SetColor("_BaseColor", color);
			material.SetFloat("_Smoothness", 0.3f);
			return material;
		}

		// HELPERS

		private sealed class GunRig
		{
			public readonly Transform Root;

			public Transform Muzzle             { get; private set; }
			/// <summary>Logan's model with its attachments (null for block guns).</summary>
			public GunModelParts Parts          { get; private set; }
			public Vector3   SightLocalPosition { get; private set; }
			public Vector3   RightHand          { get; private set; }
			public Vector3   LeftHand           { get; private set; }
			/// <summary>How far in front of the eye the sight point sits when aimed (0 = the point is the eye itself).</summary>
			public float     AdsDistance        { get; private set; }

			public GunRig(string rigName, Transform parent)
			{
				Root = CreateNode(rigName, parent);
			}

			public void AddBlock(Vector3 position, Vector3 size, Material material)
			{
				CreateBlock("Block", Root, position, size, material);
			}

			public void SetParts(GunModelParts parts)
			{
				Parts = parts;
			}

			/// <summary>
			/// Model guns: shows the gun's attachments and takes the sight, muzzle and left hand from them (the optic's eye point,
			/// the muzzle attachment's tip, the foregrip's hand spot). The eye sits EyeDistance behind the sight (data file).
			/// </summary>
			public void Refresh(BulletWeapon weapon)
			{
				if (Parts == null)
					return;

				long key = weapon != null && weapon.GunData != null ? weapon.AttachmentKey : 0;
				Parts.Apply(key);

				if (Parts.AdsEye   != null) { SightLocalPosition = Root.InverseTransformPoint(Parts.AdsEye.position); }
				if (Parts.LeftHand != null) { LeftHand           = Root.InverseTransformPoint(Parts.LeftHand.position); }
				if (Parts.Muzzle   != null) { Muzzle             = Parts.Muzzle; }

				AdsDistance = weapon != null && weapon.GunData != null ? weapon.Stats.EyeDistance : 0.0f;
			}

			public void SetPoints(Vector3 sight, Vector3 muzzle, Vector3 rightHand, Vector3 leftHand, float adsDistance)
			{
				// Named like the Attachments page mount points
				Transform sightNode = CreateNode("S_Sight", Root);
				sightNode.localPosition = sight;

				Muzzle = CreateNode("S_Muzzle", Root);
				Muzzle.localPosition = muzzle;

				SightLocalPosition = sight;
				RightHand          = rightHand;
				LeftHand           = leftHand;
				AdsDistance        = adsDistance;
			}
		}
	}
}
