namespace TPSBR
{
	using System;
	using System.Collections.Generic;
	using UnityEngine;
	using Fusion;

	[Serializable]
	public sealed class WeaponSlot
	{
		public Transform  Active;
		public Transform  Inactive;
		[NonSerialized]
		public Quaternion BaseRotation;
	}

	public sealed class Weapons : NetworkBehaviour, IBeforeTick
	{
		// CONSTANTS

		public const int FIRST_PRIMARY_SLOT = 1;
		public const int LAST_PRIMARY_SLOT  = 2;

		// PUBLIC MEMBERS

		public Weapon     PendingWeapon             { get; private set; }
		public Weapon     CurrentWeapon             { get; private set; }
		public Transform  CurrentWeaponHandle       { get; private set; }
		public Quaternion CurrentWeaponBaseRotation { get; private set; }

		public LayerMask  HitMask            => _hitMask;
		public int        CurrentWeaponSlot  => _currentWeaponSlot;
		public int        PendingWeaponSlot  => _pendingWeaponSlot;
		public int        PreviousWeaponSlot => _previousWeaponSlot;

		/// <summary>Raising the gun after a timed swap: no firing or aiming yet.</summary>
		public bool       IsDrawing          => Runner.Tick < _switchReadyTick;
		public bool       IsPunching         => _punchStartTick > 0 && Runner.Tick < _punchStartTick + SecondsToTicks(GunplaySettings.Current.PunchDuration);
		/// <summary>0..1 through the current punch (render).</summary>
		public float      PunchProgress      => IsPunching == true ? Mathf.Clamp01((Runner.LocalRenderTime - _punchStartTick * Runner.DeltaTime) / GunplaySettings.Current.PunchDuration) : 0.0f;
		/// <summary>Seconds of the current timed swap: holster of the old gun + draw of the new one (0 when none).</summary>
		public float      SwitchDuration     => (_switchReadyTick - _switchStartTick) * Runner.DeltaTime;
		public float      SwitchElapsed      => Mathf.Max(0.0f, Runner.LocalRenderTime - _switchStartTick * Runner.DeltaTime);
		public float      SwitchArmTime      => (_switchArmTick - _switchStartTick) * Runner.DeltaTime;
		/// <summary>Aiming with a magnified optic: scope sway and hold breath apply.</summary>
		public bool       IsScopedAim        => CurrentWeapon is BulletWeapon bulletWeapon && bulletWeapon.HasMagnifiedOptic == true && _character.CharacterController.Data.Aim == true;
		public float      BreathRemaining    => Mathf.Max(0.0f, HoldBreathTime - _breathUsed);
		/// <summary>GunplaySettings.HoldBreathTime, or O2 Max's longer breath while it's on (phase 5b).</summary>
		public float      HoldBreathTime     => AgentGear.IsActive(_agent, EGear.O2Max) == true ? EquipmentSettings.Current.O2MaxBreathTime : GunplaySettings.Current.HoldBreathTime;
		public bool       IsHoldingBreath    => _steadiness > 0.5f;

		// PRIVATE MEMBERS

		[SerializeField]
		private WeaponSlot[] _slots;
		[SerializeField]
		private Weapon[]     _initialWeapons;
		[SerializeField]
		private Vector3      _dropWeaponImpulse = new Vector3(5, 5f, 10f);
		[SerializeField]
		private LayerMask    _hitMask;
		[SerializeField, Tooltip("Rough first-person muzzle position relative to the eye (look space). If level geometry sits between the eye and this point, the gun is pressed into a wall and the shot hits that wall.")]
		private Vector3      _muzzleBlockOffset = new Vector3(0.14f, -0.12f, 0.62f);

		[Header("Audio")]
		[SerializeField]
		private Transform    _fireAudioEffectsRoot;

		[Networked, Capacity(8)]
		private NetworkArray<NetworkObject> _weapons { get; }
		[Networked]
		private byte _currentWeaponSlot { get; set; }
		[Networked]
		private byte _pendingWeaponSlot { get; set; }
		[Networked]
		private byte _previousWeaponSlot { get; set; }

		// Brownout (Phase 3a): ammo by caliber, timed swaps, the punch and hold breath
		[Networked, Capacity(GunData.CaliberCount)]
		private NetworkArray<int> _ammo { get; }
		[Networked]
		private int   _switchStartTick { get; set; }
		[Networked]
		private int   _switchArmTick { get; set; }
		[Networked]
		private int   _switchReadyTick { get; set; }
		[Networked]
		private int   _punchStartTick { get; set; }
		[Networked]
		private float _breathUsed { get; set; }
		[Networked]
		private NetworkBool _breathExhausted { get; set; }
		[Networked]
		private float _steadiness { get; set; }

		private Health        _health;
		private Agent         _agent;
		private Character     _character;
		private Interactions  _interactions;
		private AudioEffect[] _fireAudioEffects;
		private Weapon[]      _localWeapons = new Weapon[8];

		private readonly List<LagCompensatedHit> _punchHits = new List<LagCompensatedHit>(8);

		// PUBLIC METHODS

		public void DisarmCurrentWeapon()
		{
			if (_currentWeaponSlot == 0)
				return;

			if (CurrentWeapon != null)
			{
				CurrentWeapon.DisarmWeapon();
			}

			if (_currentWeaponSlot > 0)
			{
				_previousWeaponSlot = _currentWeaponSlot;
			}

			_currentWeaponSlot = 0;

			RefreshCurrentWeapon();

			if (CurrentWeapon != null)
			{
				CurrentWeapon.ArmWeapon();
			}
		}

		public void SetPendingWeapon(int slot)
		{
			if (_pendingWeaponSlot == slot)
				return;

			_pendingWeaponSlot = (byte)slot;
			PendingWeapon = GetWeapon(_pendingWeaponSlot);
		}

		public void ArmPendingWeapon()
		{
			if (_currentWeaponSlot == _pendingWeaponSlot)
				return;

			if (CurrentWeapon != null)
			{
				CurrentWeapon.DisarmWeapon();
			}

			if (_currentWeaponSlot > 0)
			{
				_previousWeaponSlot = _currentWeaponSlot;
			}

			_currentWeaponSlot = _pendingWeaponSlot;

			RefreshCurrentWeapon();

			if (CurrentWeapon != null)
			{
				CurrentWeapon.ArmWeapon();
			}
		}

		public void DropCurrentWeapon()
		{
			DropWeapon(_currentWeaponSlot);
		}

		public void Pickup(DynamicPickup dynamicPickup, Weapon pickupWeapon)
		{
			if (HasStateAuthority == false)
				return;

			if (IsPrimaryGun(pickupWeapon) == true)
			{
				// Brownout: guns don't merge their ammo (it lives in the caliber pools); the pickup goes into a primary slot
				dynamicPickup.UnassignObject();
				PickupWeapon(pickupWeapon);
				return;
			}

			var ownedWeapon = GetWeapon(pickupWeapon.WeaponSlot);
			if (ownedWeapon != null && ownedWeapon.WeaponID == pickupWeapon.WeaponID)
			{
				// We already have this weapon, try add at least the ammo
				var firearmWeapon = pickupWeapon as FirearmWeapon;
				bool consumed = firearmWeapon != null && ownedWeapon.AddAmmo(firearmWeapon.TotalAmmo);

				if (consumed == true)
				{
					dynamicPickup.UnassignObject();
					Runner.Despawn(pickupWeapon.Object);
				}
			}
			else
			{
				dynamicPickup.UnassignObject();
				PickupWeapon(pickupWeapon);
			}
		}

		public void Pickup(WeaponPickup weaponPickup)
		{
			if (HasStateAuthority == false)
				return;

			if (weaponPickup.Consumed == true || weaponPickup.IsDisabled == true)
				return;

			if (IsPrimaryGun(weaponPickup.WeaponPrefab) == true)
			{
				weaponPickup.TryConsume(gameObject, out string primaryPickupResult);
				PickupWeapon(Runner.Spawn(weaponPickup.WeaponPrefab, inputAuthority: Object.InputAuthority));
				return;
			}

			var ownedWeapon = GetWeapon(weaponPickup.WeaponPrefab.WeaponSlot);
			if (ownedWeapon != null && ownedWeapon.WeaponID == weaponPickup.WeaponPrefab.WeaponID)
			{
				// We already have this weapon, try add at least the ammo
				var firearmWeapon = weaponPickup.WeaponPrefab as FirearmWeapon;
				bool consumed = firearmWeapon != null && ownedWeapon.AddAmmo(firearmWeapon.InitialAmmo);

				if (consumed == true)
				{
					weaponPickup.TryConsume(gameObject, out string weaponPickupResult);
				}
			}
			else
			{
				weaponPickup.TryConsume(gameObject, out string weaponPickupResult2);

				var weapon = Runner.Spawn(weaponPickup.WeaponPrefab, inputAuthority: Object.InputAuthority);
				PickupWeapon(weapon);
			}
		}

		/// <summary>Phase 5a: an empty gun slot (1 or 2), or -1.</summary>
		public int GetEmptyPrimarySlot()
		{
			for (int slot = FIRST_PRIMARY_SLOT; slot <= LAST_PRIMARY_SLOT; ++slot)
			{
				if (GetWeapon(slot) == null)
					return slot;
			}

			return -1;
		}

		/// <summary>Phase 5a: the gun slot a picked-up gun replaces when both are full: the gun in hand (or the one being switched to).</summary>
		public int GetSwapSlot()
		{
			if (_pendingWeaponSlot >= FIRST_PRIMARY_SLOT && _pendingWeaponSlot <= LAST_PRIMARY_SLOT)
				return _pendingWeaponSlot;
			if (_currentWeaponSlot >= FIRST_PRIMARY_SLOT && _currentWeaponSlot <= LAST_PRIMARY_SLOT)
				return _currentWeaponSlot;

			return FIRST_PRIMARY_SLOT;
		}

		/// <summary>
		/// Server (phase 5a): takes the gun out of a slot as a loot stack (its mag and attachments) and despawns the gun object.
		/// If it was in hand, the hands switch to the other gun or the punch. Empty stack if there's no Brownout gun there.
		/// </summary>
		public LootStack TakeGun(int weaponSlot)
		{
			if (HasStateAuthority == false)
				return default;

			var gun = GetWeapon(weaponSlot) as BulletWeapon;
			if (gun == null || gun.GunData == null)
				return default;

			gun.CancelReload();

			var stack = LootStack.Gun(ItemCatalog.GetGunIndex(gun.GunData), gun.MagazineAmmo, gun.AttachmentKey);

			gun.Deinitialize(Object);

			if (weaponSlot == _currentWeaponSlot || weaponSlot == _pendingWeaponSlot)
			{
				byte bestWeaponSlot = _previousWeaponSlot;
				if (bestWeaponSlot == 0 || bestWeaponSlot == weaponSlot)
				{
					bestWeaponSlot = FindBestWeaponSlot(weaponSlot);
				}

				_switchStartTick = _switchArmTick = _switchReadyTick = 0;

				SetPendingWeapon(bestWeaponSlot);
				ArmPendingWeapon();

				_previousWeaponSlot = bestWeaponSlot;
			}

			RemoveWeapon(weaponSlot);
			Runner.Despawn(gun.Object);

			return stack;
		}

		/// <summary>
		/// Server (phase 5a): spawns the gun of a loot stack (its mag and attachments) into a gun slot and puts it in hand, raised for
		/// LootSettings.GunRaiseTime before it can fire or aim. The slot must be empty (TakeGun first).
		/// </summary>
		public bool AddGunFromLoot(LootStack stack, int weaponSlot, bool raise)
		{
			if (HasStateAuthority == false || stack.Kind != EItemKind.Gun || GetWeapon(weaponSlot) != null)
				return false;

			BulletWeapon prefab = ItemCatalog.GetGunPrefab(stack.Index);
			if (prefab == null)
				return false;

			BulletWeapon gun = Runner.Spawn(prefab, inputAuthority: Object.InputAuthority);
			gun.SetLootState(stack.Mag, stack.Attachments);

			AddWeapon(gun, weaponSlot);

			CancelGunActions();
			SetPendingWeapon(weaponSlot);
			ArmPendingWeapon();

			if (raise == true)
			{
				StartRaise(LootSettings.Current.GunRaiseTime);
			}

			return true;
		}

		/// <summary>
		/// Phase 5a: the gun in hand is raised (or an attachment fitted) for this long: no firing, aiming or reloading until it's done.
		/// Uses the timed swap's draw, so the viewmodel and third-person body show it the same way.
		/// </summary>
		public void StartRaise(float seconds)
		{
			if (seconds <= 0.0f)
				return;

			int now = Runner.Tick;
			_switchStartTick = now;
			_switchArmTick   = now;
			_switchReadyTick = Mathf.Max(_switchReadyTick, now + SecondsToTicks(seconds));

			_character.AnimationController.PlayEquip();
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>
		/// Test tool (Brownout → Test → Give Weapon): spawns the weapon on the server and equips it like a pickup.
		/// </summary>
		public bool DebugGiveWeapon(Weapon weaponPrefab)
		{
			if (HasStateAuthority == false || weaponPrefab == null)
				return false;

			var weapon = Runner.Spawn(weaponPrefab, inputAuthority: Object.InputAuthority);
			PickupWeapon(weapon);

			// Players spawn with no ammo since phase 5a; the test tools fill the gun's pool so older checks run as before
			if (weapon is BulletWeapon bulletWeapon && bulletWeapon.GunData != null)
			{
				ECaliber caliber = bulletWeapon.GunData.Caliber;
				_ammo.Set((int)caliber, Mathf.Max(_ammo[(int)caliber], GunplaySettings.Current.GetCarryCap(caliber)));
			}

			return true;
		}

		/// <summary>
		/// Test tool: sets one caliber pool (server).
		/// </summary>
		public void DebugSetAmmo(ECaliber caliber, int amount)
		{
			if (HasStateAuthority == true)
			{
				_ammo.Set((int)caliber, Mathf.Max(0, amount));
			}
		}
#endif

		public override void Spawned()
		{
			if (HasStateAuthority == false)
			{
				RefreshWeapons();
				return;
			}

			_currentWeaponSlot  = 0;
			_pendingWeaponSlot  = 0;
			_previousWeaponSlot = 0;

			byte bestWeaponSlot = 0;

			GunplaySettings settings = GunplaySettings.Current;

			// Phase 5a: players spawn with only the punch and loot the rest; test dummies keep the loadout so their stash has guns
			// Phase 9: bots start empty-handed like players (their bot number is set before they spawn)
			Agent owner      = GetComponent<Agent>();
			bool emptyHanded = (Object.InputAuthority.IsRealPlayer == true || (owner != null && owner.BotNumber > 0)) && LootSettings.Current.PlayersSpawnEmptyHanded == true;
			bool hasLoadout  = emptyHanded == false && settings.SpawnLoadout != null && settings.SpawnLoadout.Length > 0;

			// Spawn initial weapons. With a Brownout loadout (or empty-handed) the sample's guns (its pistol) stay out; unarmed and grenades stay
			for (byte i = 0; i < _initialWeapons.Length; i++)
			{
				var weaponPrefab = _initialWeapons[i];
				if (weaponPrefab == null)
					continue;
				if ((hasLoadout == true || emptyHanded == true) && weaponPrefab.WeaponSlot >= FIRST_PRIMARY_SLOT && weaponPrefab.WeaponSlot <= LAST_PRIMARY_SLOT)
					continue;
				// Phase 5b: the sample's grenade slots go; grenades are thrown from the Equipment slot on E (AgentEquipment)
				if (weaponPrefab is ThrowableWeapon)
					continue;

				var weapon = Runner.Spawn(weaponPrefab, inputAuthority: Object.InputAuthority);
				AddWeapon(weapon, weapon.WeaponSlot);

				if (weapon.WeaponSlot > bestWeaponSlot && weapon.WeaponSlot < 3)
				{
					bestWeaponSlot = (byte)weapon.WeaponSlot;
				}
			}

			if (hasLoadout == true)
			{
				int slot = FIRST_PRIMARY_SLOT;
				for (int i = 0; i < settings.SpawnLoadout.Length && slot <= LAST_PRIMARY_SLOT; i++)
				{
					if (settings.SpawnLoadout[i] == null)
						continue;

					var weapon = Runner.Spawn(settings.SpawnLoadout[i], inputAuthority: Object.InputAuthority);
					AddWeapon(weapon, slot);

					if (bestWeaponSlot < FIRST_PRIMARY_SLOT)
					{
						bestWeaponSlot = (byte)slot;
					}

					++slot;
				}
			}

			if (settings.SpawnWithFullAmmo == true && emptyHanded == false)
			{
				for (int i = 0; i < GunData.CaliberCount; ++i)
				{
					_ammo.Set(i, settings.GetCarryCap((ECaliber)i));
				}
			}

			_switchStartTick = _switchArmTick = _switchReadyTick = 0;
			_punchStartTick  = 0;
			_breathUsed      = 0.0f;
			_breathExhausted = false;
			_steadiness      = 0.0f;

			_previousWeaponSlot = bestWeaponSlot;

			SetPendingWeapon(bestWeaponSlot);
			ArmPendingWeapon();
			RefreshWeapons();
		}

		public void OnDespawned()
		{
			// Cleanup weapons
			for (int i = 0; i < _weapons.Length; i++)
			{
				Weapon weapon = GetWeapon(i);
				if (weapon != null)
				{
					weapon.Deinitialize(Object);
					Runner.Despawn(weapon.Object);
					_weapons.Set(i, null);
					_localWeapons[i] = null;
				}
			}

			for (int i = 0; i < _localWeapons.Length; i++)
			{
				Weapon weapon = _localWeapons[i];
				if (weapon != null)
				{
					weapon.Deinitialize(Object);
					_localWeapons[i] = null;
				}
			}

			_currentWeaponSlot  = 0;
			_pendingWeaponSlot  = 0;
			_previousWeaponSlot = 0;

			PendingWeapon             = default;
			CurrentWeapon             = default;
			CurrentWeaponHandle       = default;
			CurrentWeaponBaseRotation = default;
		}

		public void OnFixedUpdate()
		{
			if (HasStateAuthority == false)
				return;

			if (_health.IsAlive == false)
			{
				DropAllWeapons();
				return;
			}

			// Autoswitch to valid weapon if current is invalid
			if (CurrentWeapon != null && CurrentWeapon.ValidOnlyWithAmmo == true && CurrentWeapon.HasAmmo() == false)
			{
				byte bestWeaponSlot = _previousWeaponSlot;
				if (bestWeaponSlot == 0 || bestWeaponSlot == _currentWeaponSlot)
				{
					bestWeaponSlot = FindBestWeaponSlot(_currentWeaponSlot);
				}

				DisarmCurrentWeapon();
				SetPendingWeapon(bestWeaponSlot);

				_previousWeaponSlot = bestWeaponSlot;
			}
		}

		public override void Render()
		{
			RefreshWeapons();
		}

		public bool IsSwitchingWeapon()
		{
			return _pendingWeaponSlot != _currentWeaponSlot;
		}

		public bool CanFireWeapon(bool keyDown)
		{
			return _health.IsHandsBusy == false && IsSwitchingWeapon() == false && IsDrawing == false && IsPunching == false && CurrentWeapon != null && CurrentWeapon.CanFire(keyDown) == true;
		}

		public bool CanReloadWeapon(bool autoReload)
		{
			return _health.IsHandsBusy == false && IsSwitchingWeapon() == false && IsDrawing == false && IsPunching == false && CurrentWeapon != null && CurrentWeapon.CanReload(autoReload) == true;
		}

		public bool CanAim()
		{
			return _health.IsHandsBusy == false && IsSwitchingWeapon() == false && IsDrawing == false && IsPunching == false && CurrentWeapon != null && CurrentWeapon.CanAim() == true;
		}

		public Vector2 GetRecoil()
		{
			var firearmWeapon = CurrentWeapon as FirearmWeapon;
			var recoil = firearmWeapon != null ? firearmWeapon.Recoil : Vector2.zero;
			return new Vector2(-recoil.y, recoil.x); // Convert to axis angles
		}

		public void SetRecoil(Vector2 axisRecoil)
		{
			var firearmWeapon = CurrentWeapon as FirearmWeapon;

			if (firearmWeapon == null)
				return;

			firearmWeapon.Recoil = new Vector2(axisRecoil.y, -axisRecoil.x);
		}

		public bool SwitchWeapon(int weaponSlot)
		{
			if (weaponSlot == _pendingWeaponSlot)
				return false;

			var weapon = GetWeapon(weaponSlot);
			if (weapon == null || (weapon.ValidOnlyWithAmmo == true && weapon.HasAmmo() == false))
				return false;

			SetPendingWeapon(weaponSlot);
			return true;
		}

		/// <summary>
		/// Gun to gun swaps (Brownout guns) are timed from the data: put the current gun away, then raise the next one.
		/// Everything else (grenades, unarmed) keeps the sample's animation-driven switch.
		/// </summary>
		public bool IsTimedSwitch(int weaponSlot)
		{
			Weapon target = GetWeapon(weaponSlot);
			return target is BulletWeapon && (CurrentWeapon == null || CurrentWeapon is BulletWeapon) && PendingWeapon is ThrowableWeapon == false;
		}

		public bool StartTimedSwitch(int weaponSlot)
		{
			if (weaponSlot == _pendingWeaponSlot)
				return false;

			Weapon target = GetWeapon(weaponSlot);
			if (target is BulletWeapon == false)
				return false;

			CancelGunActions();

			int now = Runner.Tick;

			SetPendingWeapon(weaponSlot);

			if (weaponSlot == _currentWeaponSlot)
			{
				// Changed your mind while putting it away: bring the same gun back up
				_switchStartTick = now;
				_switchArmTick   = now;
				_switchReadyTick = now + SecondsToTicks(GetDrawTime(target));
				return true;
			}

			float holsterTime = CurrentWeapon is BulletWeapon current ? current.GunData.HolsterTime : 0.0f;

			_switchStartTick = now;
			_switchArmTick   = now + SecondsToTicks(holsterTime);
			_switchReadyTick = _switchArmTick + SecondsToTicks(GetDrawTime(target));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (HasStateAuthority == true && Runner.IsForward == true)
			{
				GunplayDebug.LogEvent($"SWAP {(CurrentWeapon is BulletWeapon from ? from.GunData.GunName : "none")} -> {((BulletWeapon)target).GunData.GunName} start tick {now}, new gun in hand tick {_switchArmTick}, can fire tick {_switchReadyTick} ({(_switchReadyTick - now) * Runner.DeltaTime * 1000.0f:F0} ms)");
			}
#endif
			return true;
		}

		/// <summary>
		/// Called every tick (server and the predicting client): swaps the gun in hand when the old one is put away.
		/// Returns true on the tick the new gun comes up.
		/// </summary>
		public bool UpdateTimedSwitch()
		{
			if (_pendingWeaponSlot == _currentWeaponSlot || _switchArmTick <= 0 || Runner.Tick < _switchArmTick)
				return false;
			if (GetWeapon(_pendingWeaponSlot) is BulletWeapon == false)
				return false;

			ArmPendingWeapon();
			return true;
		}

		/// <summary>0..1 through raising the gun after a timed swap.</summary>
		public float GetDrawProgress()
		{
			if (_switchReadyTick <= _switchArmTick)
				return 1.0f;

			return Mathf.Clamp01((Runner.Tick - _switchArmTick) / (float)(_switchReadyTick - _switchArmTick));
		}

		/// <summary>
		/// Quick punch on key 3 (Phase 3a, item 8): the gun dips, one punch, then the gun comes straight back.
		/// </summary>
		public bool StartPunch()
		{
			if (IsPunching == true || IsSwitchingWeapon() == true || IsDrawing == true || _health.IsHandsBusy == true)
				return false;
			if (CurrentWeapon is ThrowableWeapon)
				return false;

			CancelGunActions();
			_punchStartTick = Runner.Tick;
			return true;
		}

		/// <summary>
		/// The punch lands PunchHitTime into it: lag-compensated ray from the eye, PunchReach long, flat PunchDamage (server).
		/// </summary>
		public void UpdatePunch()
		{
			if (_punchStartTick <= 0 || HasStateAuthority == false || Runner.IsForward == false)
				return;

			GunplaySettings settings = GunplaySettings.Current;
			if (Runner.Tick != _punchStartTick + SecondsToTicks(settings.PunchHitTime))
				return;

			TransformData eye       = _character.GetFireTransform(false);
			Vector3       direction = eye.Rotation * Vector3.forward;

			if (ProjectileUtility.ProjectileCast(Runner, Object.InputAuthority, gameObject.GetInstanceID(), eye.Position, direction, settings.PunchReach, _hitMask, _punchHits) == false)
				return;

			LagCompensatedHit hit = _punchHits[0];
			if (hit.Hitbox == null || hit.Hitbox.Root == null)
				return;

			IHitTarget target = hit.Hitbox.Root.GetComponent<IHitTarget>();
			if (target == null)
				return;

			Health victimHealth = target as Health;
			float  healthBefore = victimHealth != null ? victimHealth.CurrentHealth : 0.0f;

			var hitData = new HitData
			{
				Action        = EHitAction.Damage,
				Amount        = settings.PunchDamage,
				Position      = hit.Point,
				Normal        = hit.Normal,
				Direction     = direction,
				Target        = target,
				InstigatorRef = Object.InputAuthority,
				Instigator    = _health,
				HitType       = EHitType.None,
			};

			HitUtility.ProcessHit(ref hitData);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			GunplayDebug.LogPunch(Runner.Tick, hit.Hitbox.Root.name, hitData.Amount);
			if (victimHealth != null)
			{
				GunplayDebug.TrackDamage(victimHealth.GetInstanceID(), hit.Hitbox.Root.name, healthBefore, victimHealth.MaxHealth, (_punchStartTick + SecondsToTicks(settings.PunchHitTime)) * Runner.DeltaTime, hitData.IsFatal, "Punch");
			}
#endif
		}

		/// <summary>
		/// Hold breath while scoped (Shift): steady for up to HoldBreathTime, refilling over the time it was held. Every tick.
		/// </summary>
		public void UpdateBreath(bool holdInput)
		{
			float           holdTime  = HoldBreathTime;
			float           deltaTime = Runner.DeltaTime;
			bool            isScoped  = IsScopedAim;

			bool holding = holdInput == true && isScoped == true && _breathExhausted == false && _breathUsed < holdTime;
			if (holding == true)
			{
				_breathUsed += deltaTime;
				if (_breathUsed >= holdTime)
				{
					_breathUsed      = holdTime;
					_breathExhausted = true;
				}
			}
			else
			{
				_breathUsed = Mathf.Max(0.0f, _breathUsed - deltaTime);
				if (_breathUsed <= 0.0f)
				{
					_breathExhausted = false;
				}
			}

			_steadiness = Mathf.MoveTowards(_steadiness, holding == true ? 1.0f : 0.0f, deltaTime / 0.2f);
		}

		/// <summary>
		/// Scope sway as a look offset in degrees (x = pitch, y = yaw): a slow figure-eight while scoped, steady while holding breath.
		/// The same function runs on the server and the clients, so shots follow the sway the shooter sees.
		/// </summary>
		public Vector2 GetSwayOffset(float time)
		{
			if (IsScopedAim == false)
				return Vector2.zero;

			GunplaySettings settings = GunplaySettings.Current;

			float phase  = time * settings.SwayFrequency * 2.0f * Mathf.PI;
			float weight = 1.0f - _steadiness;

			return new Vector2(Mathf.Sin(phase * 2.0f) * 0.5f, Mathf.Sin(phase)) * (settings.SwayAmplitude * weight);
		}

		public bool HasWeapon(int slot, bool checkAmmo = false)
		{
			if (slot < 0 || slot >= _weapons.Length)
				return false;

			var weapon = GetWeapon(slot);
			return weapon != null && (checkAmmo == false || (weapon.Object != null && weapon.HasAmmo() == true));
		}

		public Weapon GetWeapon(int slot)
		{
			NetworkObject weaponObject = _weapons[slot];
			if (ReferenceEquals(weaponObject, null) == true)
				return default;

			return weaponObject.GetBehaviour<Weapon>();
		}

		public int GetNextWeaponSlot(int fromSlot, int minSlot = 0, bool checkAmmo = true)
		{
			int weaponCount = _weapons.Length;

			for (int i = 0; i < weaponCount; i++)
			{
				int slot = (i + fromSlot + 1) % weaponCount;

				if (slot < minSlot)
					continue;

				var weapon = GetWeapon(slot);
				if (weapon == null)
					continue;

				if (checkAmmo == true && weapon.HasAmmo() == false)
					continue;

				return slot;
			}

			return 0;
		}

		public bool Fire()
		{
			if (CurrentWeapon == null)
				return false;

			Vector3       targetPoint   = _interactions.GetTargetPoint(false, true);
			TransformData fireTransform = _character.GetFireTransform(true);

			if (IsMuzzleBlocked(fireTransform, out Vector3 blockPoint) == true)
			{
				targetPoint = blockPoint;
			}

			CurrentWeapon.Fire(fireTransform.Position, targetPoint, _hitMask);

			return true;
		}

		public bool IsMuzzleBlocked(TransformData fireTransform, out Vector3 blockPoint)
		{
			blockPoint = default;

			Vector3 muzzlePosition = fireTransform.Position + fireTransform.Rotation * _muzzleBlockOffset;
			Vector3 direction      = muzzlePosition - fireTransform.Position;
			float   distance       = direction.magnitude;

			if (distance <= 0.0f)
				return false;

			if (Runner.GetPhysicsScene().Raycast(fireTransform.Position, direction / distance, out RaycastHit hit, distance, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == false)
				return false;

			blockPoint = hit.point;
			return true;
		}

		public bool Reload()
		{
			if (CurrentWeapon == null)
				return false;

			CurrentWeapon.Reload();
			return true;
		}

		public bool AddAmmo(int weaponSlot, int amount, out string result)
		{
			if (weaponSlot < 0 || weaponSlot >= _weapons.Length)
			{
				result = string.Empty;
				return false;
			}

			var weapon = GetWeapon(weaponSlot);
			if (weapon == null)
			{
				result = "No weapon with this type of ammo";
				return false;
			}

			bool ammoAdded = weapon.AddAmmo(amount);
			result = ammoAdded == true ? string.Empty : "Cannot add more ammo";

			return ammoAdded;
		}

		/// <summary>Rounds in one caliber pool.</summary>
		public int GetAmmo(ECaliber caliber)
		{
			return _ammo[(int)caliber];
		}

		/// <summary>Takes up to amount from the pool (reload). Returns what was taken.</summary>
		public int TakeAmmo(ECaliber caliber, int amount)
		{
			int available = _ammo[(int)caliber];
			int taken     = Mathf.Clamp(amount, 0, available);

			_ammo.Set((int)caliber, available - taken);
			return taken;
		}

		/// <summary>Adds up to the pool's cap. Returns what was added.</summary>
		public int AddAmmo(ECaliber caliber, int amount)
		{
			int current = _ammo[(int)caliber];
			int cap     = GunplaySettings.Current.GetCarryCap(caliber);
			int added   = Mathf.Clamp(amount, 0, Mathf.Max(0, cap - current));

			_ammo.Set((int)caliber, current + added);
			return added;
		}

		// IBeforeTick INTERFACE

		void IBeforeTick.BeforeTick()
		{
			RefreshWeapons();
		}

		// MONOBEHAVIOUR

		private void Awake()
		{
			_health = GetComponent<Health>();
			_agent = GetComponent<Agent>();
			_character = GetComponent<Character>();
			_interactions = GetComponent<Interactions>();
			_fireAudioEffects = _fireAudioEffectsRoot.GetComponentsInChildren<AudioEffect>();

			foreach (WeaponSlot slot in _slots)
			{
				if (slot.Active != null)
				{
					slot.BaseRotation = slot.Active.localRotation;
				}
			}
		}

		// PRIVATE METHODS

		private static bool IsPrimaryGun(Weapon weapon)
		{
			return weapon is BulletWeapon;
		}

		private static float GetDrawTime(Weapon weapon)
		{
			return weapon is BulletWeapon bulletWeapon ? bulletWeapon.GunData.DrawTime : 0.0f;
		}

		private int SecondsToTicks(float seconds)
		{
			return Mathf.CeilToInt(seconds / Runner.DeltaTime - 0.0001f);
		}

		/// <summary>
		/// A swap or punch cancels the reload (no ammo before ammo-in; after it the mag stays full) and any burst.
		/// </summary>
		/// <summary>Cancels a reload or a burst in progress (swaps, the punch, healing and reviving).</summary>
		public void CancelGunActions()
		{
			if (CurrentWeapon is BulletWeapon bulletWeapon)
			{
				bulletWeapon.CancelReload();
				bulletWeapon.CancelBurst();
			}
		}

		/// <summary>
		/// The third-person hand and holster positions follow the gun's own slot type (pistol or rifle), not the slot it's carried in,
		/// since Brownout's two primary slots take any gun.
		/// </summary>
		private void RefreshCurrentWeapon()
		{
			CurrentWeapon = GetWeapon(_currentWeaponSlot);

			int handleSlot = CurrentWeapon != null ? CurrentWeapon.WeaponSlot : _currentWeaponSlot;
			CurrentWeaponHandle       = _slots[handleSlot].Active;
			CurrentWeaponBaseRotation = _slots[handleSlot].BaseRotation;
		}

		private void RefreshWeapons()
		{
			PendingWeapon = GetWeapon(_pendingWeaponSlot);

			Vector2 lastRecoil = Vector2.zero;

			for (int i = 0; i < _weapons.Length; i++)
			{
				var weapon = GetWeapon(i);
				if (weapon == null)
					continue;

				if (weapon.IsInitialized == false)
				{
					weapon.Initialize(Object, _slots[weapon.WeaponSlot].Active, _slots[weapon.WeaponSlot].Inactive);
					weapon.AssignFireAudioEffects(_fireAudioEffectsRoot, _fireAudioEffects);
					_localWeapons[i] = weapon;
				}

				if (weapon.IsArmed == true)
				{
					if (i != _currentWeaponSlot)
					{
						weapon.DisarmWeapon();
					}

					if (weapon is FirearmWeapon firearmWeapon)
					{
						lastRecoil = firearmWeapon.Recoil;
					}
				}
			}

			Weapon currentWeapon = GetWeapon(_currentWeaponSlot);
			if (CurrentWeapon != currentWeapon)
			{
				if (currentWeapon == null && CurrentWeapon != null)
				{
					CurrentWeapon.Deinitialize(Object);
					_localWeapons[_currentWeaponSlot] = default;
				}

				RefreshCurrentWeapon();

				if (CurrentWeapon != null)
				{
					CurrentWeapon.ArmWeapon();

					if (CurrentWeapon is FirearmWeapon firearmWeapon)
					{
						// Recoil transfers to new weapon
						// (might be better to have recoil as an agent property instead of a weapon property)
						firearmWeapon.Recoil = lastRecoil;
					}
				}
			}
		}

		private void DropAllWeapons()
		{
			for (int i = 1; i < _weapons.Length; i++)
			{
				DropWeapon(i);
			}
		}

		private void DropWeapon(int weaponSlot)
		{
			var weapon = GetWeapon(weaponSlot);
			if (weapon == null)
				return;

			// Phase 5a: a Brownout gun becomes a light item on the ground with its mag and attachments (no despawn timer)
			if (weapon is BulletWeapon)
			{
				LootStack stack = TakeGun(weaponSlot);
				if (stack.IsEmpty == false)
				{
					LootSpawner.DropNear(GetComponent<Agent>(), stack);
				}

				return;
			}

			if (weapon.PickupPrefab == null)
			{
				Debug.LogWarning($"Cannot drop weapon {gameObject.name}, pickup prefab not assigned.");
				return;
			}

			weapon.Deinitialize(Object);

			if (weaponSlot == _currentWeaponSlot)
			{
				byte bestWeaponSlot = _previousWeaponSlot;
				if (bestWeaponSlot == 0 || bestWeaponSlot == _currentWeaponSlot)
				{
					bestWeaponSlot = FindBestWeaponSlot(_currentWeaponSlot);
				}

				SetPendingWeapon(bestWeaponSlot);
				ArmPendingWeapon();

				_previousWeaponSlot = bestWeaponSlot;
			}

			var weaponTransform = weapon.transform;

			var pickup = Runner.Spawn(weapon.PickupPrefab, weaponTransform.position, weaponTransform.rotation,
				PlayerRef.None, BeforePickupSpawned);

			RemoveWeapon(weaponSlot);

			var pickupRigidbody = pickup.GetComponent<Rigidbody>();
			if (pickupRigidbody != null)
			{
				var forcePosition = weaponTransform.TransformPoint(new Vector3(-0.005f, 0.005f, 0.015f) * weaponSlot);
				pickupRigidbody.AddForceAtPosition(weaponTransform.rotation * _dropWeaponImpulse, forcePosition, ForceMode.Impulse);
			}

			void BeforePickupSpawned(NetworkRunner runner, NetworkObject obj)
			{
				var dynamicPickup = obj.GetComponent<DynamicPickup>();
				dynamicPickup.AssignObject(_weapons[weaponSlot].Id);
			}
		}

		private void PickupWeapon(Weapon weapon)
		{
			if (weapon == null)
				return;

			int slot          = IsPrimaryGun(weapon) == true ? ChoosePrimarySlot() : weapon.WeaponSlot;
			int slotInHand    = _currentWeaponSlot; // Dropping the gun in hand switches away from it, so remember it first

			DropWeapon(slot);
			AddWeapon(weapon, slot);

			if (slot >= slotInHand && slot < 5)
			{
				CancelGunActions();
				SetPendingWeapon(slot);
				ArmPendingWeapon();
			}
		}

		/// <summary>
		/// A new gun fills an empty primary slot; with both full it replaces the gun in hand (Phase 3a, item 5).
		/// </summary>
		private int ChoosePrimarySlot()
		{
			for (int slot = FIRST_PRIMARY_SLOT; slot <= LAST_PRIMARY_SLOT; ++slot)
			{
				if (GetWeapon(slot) == null)
					return slot;
			}

			if (_currentWeaponSlot >= FIRST_PRIMARY_SLOT && _currentWeaponSlot <= LAST_PRIMARY_SLOT)
				return _currentWeaponSlot;

			return FIRST_PRIMARY_SLOT;
		}

		private void AddWeapon(Weapon weapon, int slot)
		{
			if (weapon == null)
				return;

			RemoveWeapon(slot);

			weapon.Object.AssignInputAuthority(Object.InputAuthority);
			weapon.Initialize(Object, _slots[weapon.WeaponSlot].Active, _slots[weapon.WeaponSlot].Inactive);
			weapon.AssignFireAudioEffects(_fireAudioEffectsRoot, _fireAudioEffects);

			var aoiProxy = weapon.GetComponent<NetworkAreaOfInterestProxy>();
			aoiProxy.SetPositionSource(transform);

			Runner.SetPlayerAlwaysInterested(Object.InputAuthority, weapon.Object, true);

			_weapons.Set(slot, weapon.Object);
			_localWeapons[slot] = weapon;
		}

		private void RemoveWeapon(int slot)
		{
			var weapon = GetWeapon(slot);
			if (weapon == null)
				return;

			weapon.Deinitialize(Object);
			weapon.Object.RemoveInputAuthority();

			var aoiProxy = weapon.GetComponent<NetworkAreaOfInterestProxy>();
			aoiProxy.ResetPositionSource();

			Runner.SetPlayerAlwaysInterested(Object.InputAuthority, weapon.Object, false);

			_weapons.Set(slot, null);
			_localWeapons[slot] = null;
		}

		private byte FindBestWeaponSlot(int ignoreSlot)
		{
			byte bestWeaponSlot = 0;

			for (int i = 0; i < _weapons.Length; i++)
			{
				if (i == ignoreSlot)
					continue;

				if (GetWeapon(i) != null && i > bestWeaponSlot && i < 3)
				{
					bestWeaponSlot = (byte)i;
				}
			}

			return bestWeaponSlot;
		}
	}
}
