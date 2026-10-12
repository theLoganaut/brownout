using UnityEngine;
using UnityEngine.Profiling;
using Fusion;
using Fusion.Addons.KCC;

namespace TPSBR
{
	[DefaultExecutionOrder(-5)]
	public sealed partial class Agent : ContextBehaviour, ISortedUpdate
	{
		// PUBLIC METHODS

		public bool IsObserved => Context != null && Context.ObservedAgent == this;

		public AgentInput        AgentInput   => _agentInput;
		public Interactions      Interactions => _interactions;
		public Character         Character    => _character;
		public Weapons           Weapons      => _weapons;
		public Health            Health       => _health;
		public AgentSenses       Senses       => _senses;
		public Jetpack           Jetpack      => _jetpack;
		public AgentVFX          Effects      => _agentVFX;
		public AgentInterestView InterestView => _interestView;
		public AgentMovement     Movement     => _movement;

		/// <summary>Duo team (phase 4), set by the server at spawn. 0 = no team. Players are paired in join order; test dummies have their own.</summary>
		[Networked, HideInInspector]
		public byte              Team         { get; set; }

		// PRIVATE MEMBERS

		[SerializeField]
		private float _topCameraAngleLimit;
		[SerializeField]
		private float _bottomCameraAngleLimit;
		[SerializeField]
		private GameObject _visualRoot;

		private AgentInput          _agentInput;
		private Interactions        _interactions;
		private AgentFootsteps      _footsteps;
		private Character           _character;
		private Weapons             _weapons;
		private Jetpack             _jetpack;
		private AgentSenses         _senses;
		private Health              _health;
		private AgentVFX            _agentVFX;
		private AgentInterestView   _interestView;
		private AgentMovement       _movement;
		private SortedUpdateInvoker _sortedUpdateInvoker;
		private Quaternion          _cachedLookRotation;
		private Quaternion          _cachedPitchRotation;

		// NetworkBehaviour INTERFACE

		public override void Spawned()
		{
			name = Object.InputAuthority.IsRealPlayer == true ? Object.InputAuthority.ToString() : (BotNumber > 0 ? $"Bot {BotName}" : $"Dummy {Object.Id.Raw}");

			_sortedUpdateInvoker = Runner.GetSingleton<SortedUpdateInvoker>();

			_visualRoot.SetActive(true);

			_character.OnSpawned(this);
			_movement.OnSpawned();
			_jetpack.OnSpawned(this);
			_health.OnSpawned(this);
			_agentVFX.OnSpawned(this);

			// Loot and inventory (phase 5a): players start empty-handed, dummies with a kit
			if (HasStateAuthority == true)
			{
				_inventory.InitForSpawn();
				_equipmentActions.Reset();
				IsCamouflaged = false;
				EdgeOutTick   = 0;
			}

			// Bots (phase 9): the server's brain
			SpawnedBot();

			if (ApplicationSettings.IsStrippedBatch == true)
			{
				gameObject.SetActive(false);

				if (ApplicationSettings.GenerateInput == true)
				{
					NetworkEvents networkEvents = Runner.GetComponent<NetworkEvents>();
					networkEvents.OnInput.RemoveListener(GenerateRandomInput);
					networkEvents.OnInput.AddListener(GenerateRandomInput);
				}
			}

			return;

			void GenerateRandomInput(NetworkRunner runner, NetworkInput networkInput)
			{
				// Used for batch testing

				GameplayInput gameplayInput = new GameplayInput();
				gameplayInput.MoveDirection     = new Vector2(UnityEngine.Random.value * 2.0f - 1.0f, UnityEngine.Random.value > 0.25f ? 1.0f : -1.0f).normalized;
				gameplayInput.LookRotationDelta = new Vector2(UnityEngine.Random.value * 2.0f - 1.0f, UnityEngine.Random.value * 2.0f - 1.0f);
				gameplayInput.Jump              = UnityEngine.Random.value > 0.99f;
				gameplayInput.Attack            = UnityEngine.Random.value > 0.99f;
				gameplayInput.Reload            = UnityEngine.Random.value > 0.99f;
				gameplayInput.Interact          = UnityEngine.Random.value > 0.99f;
				gameplayInput.Weapon            = (byte)(UnityEngine.Random.value > 0.99f ? (UnityEngine.Random.value > 0.25f ? 2 : 1) : 0);

				networkInput.Set(gameplayInput);
			}
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (_weapons  != null) { _weapons.OnDespawned();  }
			if (_jetpack  != null) { _jetpack.OnDespawned();  }
			if (_health   != null) { _health.OnDespawned();   }
			if (_agentVFX != null) { _agentVFX.OnDespawned(); }

			DespawnedBot();
		}

		public void EarlyFixedUpdateNetwork()
		{
			Profiler.BeginSample($"{nameof(Agent)}(Early)");

			UpdateRiverWall();
			ProcessFixedInput();

			_weapons.OnFixedUpdate();
			_jetpack.OnFixedUpdate();
			_character.OnFixedUpdate();

			Profiler.EndSample();
		}

		public override void FixedUpdateNetwork()
		{
			Profiler.BeginSample($"{nameof(Agent)}(Regular)");

			// Performance optimization, unnecessary euler call
			Quaternion currentLookRotation = _character.CharacterController.FixedData.LookRotation;
			if (_cachedLookRotation.ComponentEquals(currentLookRotation) == false)
			{
				_cachedLookRotation  = currentLookRotation;
				_cachedPitchRotation = Quaternion.Euler(_character.CharacterController.FixedData.LookPitch, 0.0f, 0.0f);
			}

			_character.GetCameraHandle().transform.localRotation = _cachedPitchRotation;

			CheckFallDamage();

			if (_health.IsAlive == true)
			{
				float sortOrder = _agentInput.FixedInput.LocalAlpha;
				if (sortOrder <= 0.0f)
				{
					// Default LocalAlpha value results in update callback being executed last.
					sortOrder = 1.0f;
				}

				// Schedule update to process render-accurate shooting.
				_sortedUpdateInvoker.ScheduleSortedUpdate(this, sortOrder);

				if (Runner.IsServer == true)
				{
					_interestView.SetPlayerInfo(_character.CharacterController.Transform, _character.GetCameraHandle());
				}
			}

			_health.OnFixedUpdate();

			if (HasStateAuthority == true)
			{
				_inventory.OnFixedUpdate();
				_gear.OnFixedUpdate();
				UpdateMapEdge();
				UpdateRiverBank();
				UpdateDoors();
			}

			Profiler.EndSample();
		}

		public void EarlyRender()
		{
			if (HasInputAuthority == true)
			{
				ProcessRenderInput();
			}

			_character.OnRender();
		}

		public override void Render()
		{
			if (HasInputAuthority == true || IsObserved == true)
			{
				// Performance optimization, unnecessary euler call
				Quaternion currentLookRotation = _character.CharacterController.RenderData.LookRotation;
				if (_cachedLookRotation.ComponentEquals(currentLookRotation) == false)
				{
					_cachedLookRotation  = currentLookRotation;
					_cachedPitchRotation = Quaternion.Euler(_character.CharacterController.RenderData.LookPitch, 0.0f, 0.0f);
				}

				_character.GetCameraHandle().transform.localRotation = _cachedPitchRotation;
			}

			_character.OnAgentRender();
			_footsteps.OnAgentRender();
		}

		// ISortedUpdate INTERFACE

		void ISortedUpdate.SortedUpdate()
		{
			// This method execution is sorted by LocalAlpha property passed in input and preserves realtime order of input actions.

			bool attackWasActivated   = _agentInput.WasActivated(EGameplayInputAction.Attack) || (_agentInput.FixedInput.Attack == true && _movement.IsFirstGunReadyTick() == true);
			bool reloadWasActivated   = _agentInput.WasActivated(EGameplayInputAction.Reload);
			bool interactWasActivated = _agentInput.WasActivated(EGameplayInputAction.Interact);
			bool pickUpWasActivated   = _agentInput.WasActivated(EGameplayInputAction.PickUp);

			TryFire(attackWasActivated, _agentInput.FixedInput.Attack);
			TryReload(reloadWasActivated == false);

			// F revives a downed teammate first (Health.Actions); while reviving or downed the interactions (F equip, G pick up) stay off
			if (_health.IsReviving == false && _health.IsDowned == false)
			{
				_interactions.TryInteract(interactWasActivated, pickUpWasActivated);
			}
		}

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_agentInput   = GetComponent<AgentInput>();
			_interactions = GetComponent<Interactions>();
			_footsteps    = GetComponent<AgentFootsteps>();
			_character    = GetComponent<Character>();
			_weapons      = GetComponent<Weapons>();
			_health       = GetComponent<Health>();
			_agentVFX     = GetComponent<AgentVFX>();
			_senses       = GetComponent<AgentSenses>();
			_jetpack      = GetComponent<Jetpack>();
			_interestView = GetComponent<AgentInterestView>();
			_inventory    = new Inventory(this);
			_equipmentActions = new AgentEquipment(this);
			_gear             = new AgentGear(this);

			// Brownout first person: render-only components, created at runtime so the agent prefabs stay untouched
			if (GetComponent<FirstPersonBody>() == null) { gameObject.AddComponent<FirstPersonBody>(); }
			if (GetComponent<Viewmodel>()       == null) { gameObject.AddComponent<Viewmodel>();       }

			// Brownout movement (phase 2): rules component, state in the KCC's networked data
			_movement = GetComponent<AgentMovement>();
			if (_movement == null) { _movement = gameObject.AddComponent<AgentMovement>(); }
			if (GetComponent<StancePose>() == null) { gameObject.AddComponent<StancePose>(); }

			// Loot and inventory (phase 5a): the Tab screen, drawn only for the local player
			if (GetComponent<InventoryScreen>() == null) { gameObject.AddComponent<InventoryScreen>(); }

			// Equipment and gear (phase 5b): the grapple rope (everyone), Camouflage (other players), the concussion screen (local player)
			if (GetComponent<GrappleRope>()      == null) { gameObject.AddComponent<GrappleRope>();      }
			if (GetComponent<GearVisuals>()      == null) { gameObject.AddComponent<GearVisuals>();      }
			if (GetComponent<ConcussionScreen>() == null) { gameObject.AddComponent<ConcussionScreen>(); }

			// Phase 10: the soldier's built-in armor, helmet, backpack and drop suit (the wings pop off near the ground), bots' colors
			if (GetComponent<BodyGear>() == null) { gameObject.AddComponent<BodyGear>(); }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (GetComponent<MovementReadout>() == null) { gameObject.AddComponent<MovementReadout>(); }
#endif
		}

		// PRIVATE METHODS

		private void ProcessFixedInput()
		{
			KCC     kcc          = _character.CharacterController;
			KCCData kccFixedData = kcc.FixedData;

			GameplayInput input = default;

			if (_health.IsAlive == true)
			{
				input = _agentInput.FixedInput;
			}

			// The drop (phase 7): only steering, looking and pinging until you land and the landing roll ends (phase 10)
			if (_movement.IsDropping == true || _movement.IsLandRolling == true)
			{
				bool ping = input.Ping;
				input.Actions = default;
				input.Ping    = ping;
				input.Weapon  = 0;
			}

			// Downed players only crawl; healing (Q) and reviving (F) start and cancel here (phase 4)
			_health.ProcessInput(ref input, _agentInput);

			// Equipment on E and gear on Left Ctrl (phase 5b). A throw or grapple keeps the hands busy; the grapple's pull starts here
			_equipmentActions.ProcessInput(ref input, _agentInput);
			_gear.ProcessInput(input, _agentInput);

			// Ping on middle mouse (phase 6a), placed by the server
			ProcessPing(input);
			ProcessWaypoint();

			// Concussed: look slowed (movement is slowed in BREnvironmentProcessor)
			input.LookRotationDelta *= _senses.GetConcussionSlow();

			// Sprint, dash, crouch, slide, skip, jump and mantle. Clears aim when the mode doesn't allow it and halves turning while dashing
			_movement.ProcessFixedInput(ref input);

			_health.ProcessAfterMovement();

			// Gunplay (phase 3a): gun-to-gun swaps run on timers from the data, then the punch and hold breath
			if (_weapons.UpdateTimedSwitch() == true)
			{
				_character.AnimationController.PlayEquip();
			}

			if (_weapons.IsDrawing == true)
			{
				_character.AnimationController.SetEquipProgress(_weapons.GetDrawProgress());
			}

			if (_health.IsAlive == true && _agentInput.WasActivated(EGameplayInputAction.Melee, input) == true && _weapons.StartPunch() == true)
			{
				_character.AnimationController.CancelReload();
			}

			_weapons.UpdatePunch();

			if (_weapons.CurrentWeapon is BulletWeapon reloadingGun && reloadingGun.IsReloading == true)
			{
				_character.AnimationController.SetReloadProgress(reloadingGun.ReloadElapsed / reloadingGun.ReloadTime);
			}

			if (input.Aim == true)
			{
				input.Aim &= CanAim(kccFixedData);
			}

			// Shift while aimed through a magnified optic holds your breath (you can't sprint while aiming)
			_weapons.UpdateBreath(input.Sprint);

			// A bot's brain turns by exact angles, so the aimed look scaling (made for mouse sensitivity) doesn't apply to it
			if (input.Aim == true && _bot == null)
			{
				input.LookRotationDelta *= GetAimLookMultiplier();
			}

			kcc.SetAim(input.Aim);

			SetLookRotation(kccFixedData, input.LookRotationDelta, _weapons.GetRecoil(), out Vector2 newRecoil);
			_weapons.SetRecoil(newRecoil);

			kcc.SetInputDirection(input.MoveDirection.IsZero() == true ? Vector3.zero : kcc.FixedData.TransformRotation * input.MoveDirection.X0Y());

			if (input.Weapon > 0 && _character.AnimationController.CanSwitchWeapons(true) == true)
			{
				int weaponSlot = input.Weapon - 1;

				if (_weapons.IsTimedSwitch(weaponSlot) == true)
				{
					if (_weapons.StartTimedSwitch(weaponSlot) == true)
					{
						_character.AnimationController.CancelReload();
					}
				}
				else if (_weapons.SwitchWeapon(weaponSlot) == true)
				{
					_character.AnimationController.SwitchWeapons();
				}
			}
			else if (input.Weapon <= 0 && _weapons.PendingWeaponSlot != _weapons.CurrentWeaponSlot && _weapons.IsTimedSwitch(_weapons.PendingWeaponSlot) == false && _character.AnimationController.CanSwitchWeapons(false) == true)
			{
				_character.AnimationController.SwitchWeapons();
			}

			_agentInput.SetFixedInput(input, false);
		}

		private void ProcessRenderInput()
		{
			KCC     kcc           = _character.CharacterController;
			KCCData kccFixedData  = kcc.FixedData;

			GameplayInput input = default;

			if (_health.IsAlive == true)
			{
				input = _agentInput.RenderInput;

				var accumulatedInput = _agentInput.AccumulatedInput;

				input.LookRotationDelta = accumulatedInput.LookRotationDelta;
				input.Aim               = accumulatedInput.Aim;
			}

			if (input.Aim == true)
			{
				input.Aim &= CanAim(kccFixedData) && _movement.CanAim() == true;
			}

			// Reviving, or being revived: held still (the fixed tick strips it too)
			if (_health.IsReviving == true || _health.IsBeingRevived == true)
			{
				input.MoveDirection = default;
			}

			if (input.Aim == true)
			{
				input.LookRotationDelta *= GetAimLookMultiplier();
			}

			input.LookRotationDelta *= _movement.GetTurnRateMultiplier();
			input.LookRotationDelta *= _senses.GetConcussionSlow();

			SetLookRotation(kccFixedData, input.LookRotationDelta, _weapons.GetRecoil(), out Vector2 newRecoil);

			kcc.SetInputDirection(input.MoveDirection.IsZero() == true ? Vector3.zero : kcc.RenderData.TransformRotation * input.MoveDirection.X0Y());

			kcc.SetAim(input.Aim);

			if (_agentInput.WasActivated(EGameplayInputAction.Jump, input) == true && _movement.ShouldPredictJump() == true)
			{
				kcc.Jump(Vector3.up);
			}
		}

		private void TryFire(bool attack, bool hold)
		{
			var currentWeapon = _weapons.CurrentWeapon;
			if (currentWeapon is ThrowableWeapon && currentWeapon.WeaponSlot == _weapons.PendingWeaponSlot)
			{
				// Fire is handled form the grenade animation state itself
				_character.AnimationController.ProcessThrow(attack, hold);
				return;
			}

			// A started burst finishes, and a press made just before the gun was ready still fires, even if the trigger is let go
			bool keepFiring = currentWeapon is BulletWeapon bulletWeapon && (bulletWeapon.IsBurstActive == true || bulletWeapon.HasBufferedPress == true);
			if (hold == false && keepFiring == false)
				return;
			if (_movement.CanFire() == false)
				return;
			if (_weapons.CanFireWeapon(attack) == false)
				return;

			if (_character.AnimationController.StartFire() == true)
			{
				if (_weapons.Fire() == true)
				{
					if (Runner.IsServer == true)
					{
						PlayerRef inputAuthority = Object.InputAuthority;
						if (inputAuthority.IsRealPlayer == true)
						{
							_interestView.UpdateShootInterestTargets();
						}
					}
				}
			}
		}

		private void TryReload(bool autoReload)
		{
			if (_weapons.CanReloadWeapon(autoReload) == false)
				return;

			if (_character.AnimationController.StartReload() == true)
			{
				_weapons.Reload();
			}
		}

		/// <summary>
		/// Look speed while aimed. Brownout guns scale with the zoom (the ADS sensitivity setting is tuned for iron sights);
		/// the sample's other weapons keep its 0.3x for snipers.
		/// </summary>
		private float GetAimLookMultiplier()
		{
			Weapon currentWeapon = _weapons.CurrentWeapon;
			if (currentWeapon is BulletWeapon)
				return GunplaySettings.Current.ReferenceAdsZoom / Mathf.Max(1.0f, currentWeapon.AdsZoom);

			return currentWeapon != null && currentWeapon.HitType == EHitType.Sniper ? 0.3f : 1.0f;
		}

		private bool CanAim(KCCData kccData)
		{
			// Brownout: aiming works in the air too (skipping), only the spread grows there
			return _weapons.CanAim();
		}

		private void SetLookRotation(KCCData kccData, Vector2 lookRotationDelta, Vector2 recoil, out Vector2 newRecoil)
		{
			if (lookRotationDelta.IsZero() == true && recoil.IsZero() == true && _character.CharacterController.Data.Recoil == Vector2.zero)
			{
				newRecoil = recoil;
				return;
			}

			Vector2 baseLookRotation = kccData.GetLookRotation(true, true) - kccData.Recoil;
			Vector2 recoilReduction  = Vector2.zero;

			if (recoil.x > 0f && lookRotationDelta.x < 0)
			{
				recoilReduction.x = Mathf.Clamp(lookRotationDelta.x, -recoil.x, 0f);
			}

			if (recoil.x < 0f && lookRotationDelta.x > 0f)
			{
				recoilReduction.x = Mathf.Clamp(lookRotationDelta.x, 0, -recoil.x);
			}

			if (recoil.y > 0f && lookRotationDelta.y < 0)
			{
				recoilReduction.y = Mathf.Clamp(lookRotationDelta.y, -recoil.y, 0f);
			}

			if (recoil.y < 0f && lookRotationDelta.y > 0f)
			{
				recoilReduction.y = Mathf.Clamp(lookRotationDelta.y, 0, -recoil.y);
			}

			lookRotationDelta -= recoilReduction;
			recoil            += recoilReduction;

			lookRotationDelta.x = Mathf.Clamp(baseLookRotation.x + lookRotationDelta.x, -_topCameraAngleLimit, _bottomCameraAngleLimit) - baseLookRotation.x;

			_character.CharacterController.SetLookRotation(baseLookRotation + recoil + lookRotationDelta);
			_character.CharacterController.SetRecoil(recoil);

			_character.AnimationController.Turn(lookRotationDelta.y);

			newRecoil = recoil;
		}

		private void CheckFallDamage()
		{
			if (IsProxy == true)
				return;

			if (_health.IsAlive == false)
				return;

			var kccData = _character.CharacterController.Data;

			if (kccData.IsGrounded == false || kccData.WasGrounded == true)
				return;

			// Landing from the drop (phase 7) never hurts
			if (AgentMovement.IsDropMode(kccData.MoveMode) == true)
				return;

			float fallVelocity = -kccData.DesiredVelocity.y;
			for (int i = 1; i < 3; ++i)
			{
				var historyData = _character.CharacterController.GetHistoryData(kccData.Tick - i);
				if (historyData != null)
				{
					fallVelocity = Mathf.Max(fallVelocity, -historyData.DesiredVelocity.y);
				}
			}

			if (fallVelocity < 0f)
				return;

			MovementSettings movementSettings = MovementSettings.Current;

			float damage = MathUtility.Map(movementSettings.FallDamageMinVelocity, movementSettings.FallDamageMaxVelocity, 0f, movementSettings.FallDamageMax, fallVelocity);

			if (damage <= movementSettings.FallDamageMinimum)
				return;

			var hitData = new HitData
			{
				Action           = EHitAction.Damage,
				Amount           = damage,
				Position         = transform.position,
				Normal           = Vector3.up,
				Direction        = -Vector3.up,
				InstigatorRef    = Object.InputAuthority,
				Instigator       = _health,
				Target           = _health,
				HitType          = EHitType.Suicide,
			};

			(_health as IHitTarget).ProcessHit(ref hitData);
		}

		private void OnCullingUpdated(bool isCulled)
		{
			bool isActive = isCulled == false;

			// Show/hide the game object based on AoI (Area of Interest)

			_visualRoot.SetActive(isActive);

			if (_character.CharacterController.Collider != null)
			{
				_character.CharacterController.Collider.enabled = isActive;
			}
		}
	}
}
