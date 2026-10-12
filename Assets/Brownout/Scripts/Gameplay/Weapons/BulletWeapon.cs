using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace TPSBR
{
	public enum EBulletState : byte
	{
		None           = 0,
		Flying         = 1,
		HitEnvironment = 2,
		HitTarget      = 3,
		Expired        = 4,
	}

	/// <summary>
	/// One bullet in flight. Position at flight time t: Origin + Velocity * t + 0.5 * gravity * t².
	/// </summary>
	public struct BulletData : INetworkStruct
	{
		public int     ShotIndex;     // The gun's shot count when it was fired, so visuals can tell bullets apart
		public int     FireTick;
		public float   FireOffset;    // How long before the end of FireTick the shot really happened (exact timing inside the tick)
		public Vector3 Origin;
		public Vector3 Velocity;
		public byte    State;         // EBulletState
		public float   ImpactTime;    // Flight time when it stopped
		public Vector3 ImpactPoint;
		public int     ImpactSurface; // Phase 11c: ESurface (was the collider's tag hash)

		public Vector3 ImpactNormal { get { return NetworkCompression.UnpackDirection(_impactNormal); } set { _impactNormal = NetworkCompression.PackDirection(value); } }

		private uint   _impactNormal;
	}

	/// <summary>
	/// Brownout guns (Phase 3a): real bullets with travel time and gravity drop, every number from the gun's GunData.
	/// The server moves each bullet along its arc every tick and casts that segment with lag compensation from the shooter's view;
	/// the first thing it hits stops it. Fire timing keeps the fraction of a tick, so average fire rates match the table exactly.
	/// Ammo comes from the owner's caliber pools (Weapons); ammo-in is a server timer from the data, never an animation event.
	/// </summary>
	public sealed class BulletWeapon : FirearmWeapon
	{
		// CONSTANTS

		private const int   MAX_BULLETS          = 36; // Splitter: a full 34-round mag fits in the 2 s bullet life
		private const float TRACER_CONVERGE_DIST = 20.0f; // The tracer leaves the visible muzzle and joins the real path (from the eye) over this distance

		// PUBLIC MEMBERS

		public GunData GunData           => _gunData;
		public bool    IsBurstActive     => IsFiring;
		/// <summary>A semi or bolt press made just before the gun was ready; it fires as soon as it is, even if the trigger is already let go.</summary>
		public bool    HasBufferedPress  => _bufferedPressTick > 0;
		public bool    IsAmmoIn          => _state.IsBitSet(2);
		public float   ReloadElapsed     => IsReloading == true ? (Runner.Tick - _reloadStartTick) * Runner.DeltaTime : 0.0f;
		/// <summary>A 2x or 4x is on (sway and hold breath), or the 3a test toggle that treats iron sights as scoped.</summary>
		public bool    HasMagnifiedOptic => Stats.Magnified == true || IsScopedDebug();
		/// <summary>Suppressor on: hidden from Sixth Sense (5b) and the minimap later.</summary>
		public bool    IsSuppressed      => Stats.Suppressed;
		/// <summary>The gun's numbers with its attachments applied.</summary>
		public GunStats Stats            => GetStats();

		public override int   MaxMagazineAmmo => _gunData != null ? Stats.Magazine : base.MaxMagazineAmmo;
		public override int   WeaponAmmo      => _gunData != null && OwnerWeapons != null ? OwnerWeapons.GetAmmo(_gunData.Caliber) : 0;
		public override float ReloadTime      => _gunData != null ? Stats.ReloadTime : base.ReloadTime;
		public override float Cooldown        => IsReloading == true ? Mathf.Max(0.0f, ReloadTime - ReloadElapsed) : 0.0f;
		public override float AdsTime         => _gunData != null ? Stats.AdsTime : base.AdsTime;
		public override float AdsZoom         => _gunData != null ? Stats.AdsZoom : base.AdsZoom;
		/// <summary>Phase 11a: a magnified optic draws its zoom inside the lens (ScopeLens), so the view around it only zooms by the gun's iron-sight zoom.</summary>
		public bool    UsesScopeLens          => _gunData != null && Stats.Magnified == true && GunplaySettings.Current.ScopeLens == true;
		/// <summary>The main camera's zoom while aimed: the iron-sight zoom with a scope lens, else AdsZoom.</summary>
		public float   ViewZoom               => UsesScopeLens == true ? GunplaySettings.Current.GetIronSightZoom(_gunData.Class) : AdsZoom;

		// PRIVATE MEMBERS

		[Header("Brownout")]
		[SerializeField]
		private GunData         _gunData;
		[SerializeField, Tooltip("Tracer visual (its trail is reused; it follows the bullet's real arc).")]
		private DummyProjectile _projectile;
		[SerializeField]
		private ImpactSetup     _impactSetup;

		[Networked, Capacity(MAX_BULLETS)]
		private NetworkArray<BulletData> _bullets { get; }

		// Next shot allowed at (tick + fraction) * DeltaTime
		[Networked]
		private int     _readyTick { get; set; }
		[Networked]
		private float   _readyFraction { get; set; }
		[Networked]
		private int     _burstStartTick { get; set; }
		[Networked]
		private float   _burstStartFraction { get; set; }
		[Networked]
		private byte    _burstShotsLeft { get; set; }
		[Networked]
		private int     _bufferedPressTick { get; set; }
		[Networked, OnChangedRender(nameof(OnReloadStarted))]
		private int     _reloadStartTick { get; set; }
		[Networked]
		private int     _lastShotTick { get; set; }
		[Networked]
		private int     _recoilShotIndex { get; set; }
		// Phase 11e: the gun's own share of the view offset (Recoil also holds what the player pulled against it) and where it's heading
		[Networked]
		private Vector2 _recoilOffset { get; set; }
		[Networked]
		private Vector2 _recoilTarget { get; set; }
		[Networked]
		private NetworkBool _recoilKickOnly { get; set; }
		// Phase 3c: one attachment ID per slot (GunplaySettings.Attachments index + 1, 0 = none), set only by the server
		[Networked, Capacity(AttachmentData.SlotCount)]
		private NetworkArray<byte> _attachments { get; }

		private bool      _keyDown;
		private int       _keyDownTick;
		private LayerMask _hitMask;
		private Weapons   _ownerWeapons;
		private NetworkObject _ownerWeaponsSource;

		private readonly List<LagCompensatedHit> _validHits = new List<LagCompensatedHit>(16);
		private readonly List<Tracer>            _tracers   = new List<Tracer>(32);

		private GunStats         _stats;
		private long             _statsKey = -1;
		private AttachmentData[] _statsAttachments = new AttachmentData[AttachmentData.SlotCount];

		private AudioEffect[]    _fireAudio;
		private float            _smokeDebt; // Phase 11c: fractional smoke puffs carried to the next shot
		// The agent's fire audio is shared by all its guns, so each source's own range is remembered once, before any scaling
		private static readonly Dictionary<AudioSource, float> _gunshotBaseRange = new Dictionary<AudioSource, float>(64);

		private Weapons OwnerWeapons
		{
			get
			{
				if (Owner == null)
					return null;

				if (_ownerWeaponsSource != Owner)
				{
					_ownerWeaponsSource = Owner;
					_ownerWeapons       = Owner.GetComponent<Weapons>();
				}

				return _ownerWeapons;
			}
		}

		private bool AmmoInDone { get { return _state.IsBitSet(2); } set { _state = _state.SetBitNoRef(2, value); } }

		// PUBLIC METHODS

		public void CancelReload()
		{
			if (IsReloading == false)
				return;

			// After ammo-in the mag is already full and stays that way; before it, the reload is simply lost
			LogReload(AmmoInDone == true ? $"cancelled after ammo-in, mag {_magazineAmmo}" : $"cancelled before ammo-in, mag {_magazineAmmo}");

			IsReloading = false;
			AmmoInDone  = false;
		}

		public void CancelBurst()
		{
			_burstShotsLeft    = 0;
			_bufferedPressTick = 0;
			IsFiring           = false;
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>Test tool (Gun Bench check): sets the rounds in the mag (server).</summary>
		public void DebugSetMagazine(int rounds)
		{
			if (HasStateAuthority == true)
			{
				_magazineAmmo = Mathf.Clamp(rounds, 0, MaxMagazineAmmo);
			}
		}
#endif

		/// <summary>The attachment ID in a slot (0 = none, or before the gun is spawned).</summary>
		public int GetAttachmentID(EAttachmentSlot slot)
		{
			return Object != null && Object.IsValid == true ? _attachments[(int)slot] : 0;
		}

		public AttachmentData GetAttachment(EAttachmentSlot slot)
		{
			return GunplaySettings.Current.GetAttachment(GetAttachmentID(slot));
		}

		/// <summary>All six IDs in one number, to notice a change.</summary>
		public long AttachmentKey
		{
			get
			{
				if (Object == null || Object.IsValid == false)
					return 0;

				long key = 0;
				for (int i = 0; i < AttachmentData.SlotCount; ++i)
				{
					key |= (long)_attachments[i] << (8 * i);
				}

				return key;
			}
		}

		/// <summary>
		/// Server, right after spawning a gun from a loot stack (phase 5a): its attachments (any the gun doesn't take are left off)
		/// and the rounds in its mag.
		/// </summary>
		public void SetLootState(int magazine, long attachmentKey)
		{
			if (HasStateAuthority == false || _gunData == null)
				return;

			GunplaySettings settings = GunplaySettings.Current;

			for (int i = 0; i < AttachmentData.SlotCount; ++i)
			{
				byte           id         = (byte)((attachmentKey >> (8 * i)) & 0xFF);
				AttachmentData attachment = settings.GetAttachment(id);

				if (attachment == null || attachment.Slot != (EAttachmentSlot)i || _gunData.Allows(attachment) == false)
				{
					id = 0;
				}

				_attachments.Set(i, id);
			}

			_magazineAmmo = Mathf.Clamp(magazine, 0, MaxMagazineAmmo);
		}

		/// <summary>
		/// Server: puts an attachment in its slot (null clears the slot). Refused if the gun doesn't take it.
		/// Cancels a reload in progress; a smaller mag sends the extra rounds back to the caliber pool (up to its cap).
		/// </summary>
		public bool SetAttachment(EAttachmentSlot slot, AttachmentData attachment, out string result)
		{
			if (HasStateAuthority == false || _gunData == null)
			{
				result = "Attachments are set by the server.";
				return false;
			}

			if (attachment != null && attachment.Slot != slot)
			{
				result = $"{attachment.AttachmentName} goes in the {attachment.Slot} slot, not {slot}.";
				return false;
			}

			if (_gunData.Allows(attachment) == false)
			{
				result = $"{_gunData.GunName} doesn't take {attachment.AttachmentName}.";
				return false;
			}

			byte id = GunplaySettings.Current.GetAttachmentID(attachment);
			if (attachment != null && id == 0)
			{
				result = $"{attachment.AttachmentName} isn't in GunplaySettings.Attachments (run Brownout → Data → Import Guns).";
				return false;
			}

			if (_attachments[(int)slot] == id)
			{
				result = $"{_gunData.GunName} {slot}: unchanged.";
				return true;
			}

			if (IsReloading == true)
			{
				CancelReload();
			}

			_attachments.Set((int)slot, id);

			int maxMagazine = MaxMagazineAmmo;
			if (_magazineAmmo > maxMagazine)
			{
				int extra = _magazineAmmo - maxMagazine;
				_magazineAmmo = maxMagazine;

				if (OwnerWeapons != null)
				{
					OwnerWeapons.AddAmmo(_gunData.Caliber, extra);
				}
			}

			result = $"{_gunData.GunName} {slot}: {(attachment != null ? attachment.AttachmentName : "none")}.";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (Runner.IsForward == true)
			{
				GunplayDebug.LogEvent($"ATTACH {_gunData.GunName} tick {Runner.Tick}: {slot} {(attachment != null ? attachment.AttachmentName : "none")}, mag {_magazineAmmo}/{maxMagazine}");
			}
#endif
			return true;
		}

		// NetworkBehaviour INTERFACE

		public override void Spawned()
		{
			base.Spawned();

			if (_gunData == null)
			{
				Debug.LogError($"{name}: no GunData assigned.", this);
				return;
			}

			if (HasStateAuthority == true)
			{
				_magazineAmmo = _gunData.Magazine;
			}
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			for (int i = _tracers.Count - 1; i >= 0; --i)
			{
				ReturnTracer(i);
			}

			base.Despawned(runner, hasState);
		}

		public override void FixedUpdateNetwork()
		{
			if (IsProxy == true || _gunData == null)
				return;

			int intervalTicks = SecondsToTicks(_gunData.FireInterval);
			bool isIdle       = Runner.Tick > _lastShotTick + intervalTicks + 1;

			// Burst guns keep their spray going across the pause between bursts (the footage measured it that way)
			bool isRecoilIdle = Runner.Tick > _lastShotTick + SecondsToTicks(Mathf.Max(_gunData.FireInterval, _gunData.BurstCycle)) + 1;
			UpdateRecoil(isRecoilIdle);

			if (isIdle == true)
			{
				DecayDispersion();
			}

			UpdateReload();

			if (HasStateAuthority == true && Runner.IsForward == true)
			{
				UpdateBullets();
			}
		}

		public override void Render()
		{
			base.Render(); // Spawns visuals for new shots through FireVisualProjectile

			UpdateTracers(Time.deltaTime);
		}

		// Weapon INTERFACE

		public override bool IsBusy()
		{
			return IsFiring == true || IsReloading == true;
		}

		public override bool CanFire(bool keyDown)
		{
			_keyDown     = keyDown;
			_keyDownTick = Runner.Tick;

			if (_gunData == null)
				return false;
			if (IsReloading == true || _magazineAmmo <= 0)
				return false;

			return TryGetShotTime(keyDown, true, out _);
		}

		public override void Fire(Vector3 firePosition, Vector3 targetPosition, LayerMask hitMask)
		{
			if (_gunData == null || IsReloading == true || _magazineAmmo <= 0)
				return;

			bool keyDown = _keyDown == true && _keyDownTick == Runner.Tick;
			if (TryGetShotTime(keyDown, false, out double shotTime) == false)
				return;

			_hitMask = hitMask;

			double deltaTime = Runner.DeltaTime;
			double tickEnd   = Runner.Tick * deltaTime;
			float  fireOffset = Mathf.Clamp((float)(tickEnd - shotTime), 0.0f, (float)deltaTime);

			// When the next shot is allowed
			double nextShot = shotTime + _gunData.FireInterval;
			if (_gunData.FireMode == EFireMode.Burst)
			{
				if (IsFiring == false)
				{
					SetTime(shotTime, out int burstTick, out float burstFraction);
					_burstStartTick     = burstTick;
					_burstStartFraction = burstFraction;
					_burstShotsLeft     = (byte)Mathf.Max(0, _gunData.BurstCount - 1);
				}
				else
				{
					_burstShotsLeft--;
				}

				IsFiring = _burstShotsLeft > 0;

				if (IsFiring == false)
				{
					double burstStart = GetTime(_burstStartTick, _burstStartFraction);
					nextShot = System.Math.Max(nextShot, burstStart + _gunData.BurstCycle);
				}
			}

			SetTime(nextShot, out int readyTick, out float readyFraction);
			_readyTick         = readyTick;
			_readyFraction     = readyFraction;
			_bufferedPressTick = 0;

			_magazineAmmo--;
			if (_magazineAmmo <= 0)
			{
				CancelBurst();
			}

			// Spread
			Vector3 direction = targetPosition - firePosition;
			direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : FireTransform.forward;

			float spread = GetTotalDispersion();
			if (spread > 0.0f)
			{
				Random.InitState((_projectilesCount + 10) * Runner.Tick);
				Vector3 randomDispersion = Random.insideUnitSphere * spread;
				direction = Quaternion.Euler(randomDispersion.x, randomDispersion.y, randomDispersion.z) * direction;
			}

			GrowDispersion();

			var bullet = new BulletData
			{
				ShotIndex  = _projectilesCount,
				FireTick   = Runner.Tick,
				FireOffset = fireOffset,
				Origin     = firePosition,
				Velocity   = direction * Stats.BulletSpeed,
				State      = (byte)EBulletState.Flying,
			};

			if (HasStateAuthority == true && Runner.IsForward == true)
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				GunplayDebug.LogFire(_gunData.GunName, Runner.Tick, (float)shotTime, firePosition, direction, _magazineAmmo, bullet.Velocity.magnitude);
#endif
				// First segment: up to the end of this tick, checked against what the shooter saw at the moment of the shot
				SimulateBullet(ref bullet, 0.0f, fireOffset, true);

				// Sixth Sense (phase 5b): an unsuppressed shot marks the shooter for enemies who have it on
				if (Stats.Suppressed == false)
				{
					AgentGear.OnShotFired(Owner);

					// Bots (phase 9) hear unsuppressed shots
					BotBrain.OnShotFired(Owner != null ? Owner.GetComponent<Agent>() : null, firePosition);
				}
			}

			_bullets.Set(_projectilesCount % MAX_BULLETS, bullet);
			_projectilesCount++;
			_lastShotTick = Runner.Tick;

			Kick();
		}

		public override bool CanReload(bool autoReload)
		{
			if (_gunData == null)
				return false;
			if (IsReloading == true || IsFiring == true)
				return false;
			if (_magazineAmmo >= MaxMagazineAmmo)
				return false;
			if (WeaponAmmo <= 0)
				return false;

			return autoReload == false || _magazineAmmo <= 0;
		}

		public override void Reload()
		{
			if (CanReload(false) == false)
				return;

			IsReloading      = true;
			AmmoInDone       = false;
			_reloadStartTick = Runner.Tick;

			LogReload($"start, mag {_magazineAmmo}");

			CancelBurst();
			ResetDispersion();
		}

		public override bool HasAmmo()
		{
			return _magazineAmmo > 0 || WeaponAmmo > 0;
		}

		public override bool AddAmmo(int ammo)
		{
			return _gunData != null && OwnerWeapons != null && OwnerWeapons.AddAmmo(_gunData.Caliber, ammo) > 0;
		}

		// FirearmWeapon INTERFACE

		protected override int ProjectileDataCapacity => MAX_BULLETS;

		protected override float GetTotalDispersion()
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (GunplayDebug.ZeroSpread == true)
				return 0.0f;
#endif
			// Laser Sight: tighter from the hip only
			bool isAimed = Character != null && Character.CharacterController.FixedData.Aim == true;
			return base.GetTotalDispersion() * (isAimed == true ? 1.0f : Stats.HipSpread);
		}

		protected override bool ShowFireFlash => Stats.HidesFlash == false;
		protected override float FireFlashScale => _gunData != null ? FeelSettings.ByClass(FeelSettings.Current.MuzzleFlashScale, _gunData.Class) : 1.0f;

		// Phase 10: Logan's clips for this gun (SoundSettings), else the sample's
		protected override AudioSetup FireSound   => GetSound(Stats.Suppressed == true ? SoundSettings.EGunSound.FireSuppressed : SoundSettings.EGunSound.Fire, base.FireSound);
		protected override AudioSetup ReloadSound => GetSound(SoundSettings.EGunSound.Reload, base.ReloadSound);

		private AudioSetup GetSound(SoundSettings.EGunSound sound, AudioSetup sampleSetup)
		{
			SoundSettings settings = SoundSettings.Current;
			return settings != null && GunData != null ? settings.GetGunSound(GunData.GunName, sound, sampleSetup) : sampleSetup;
		}

		public override void AssignFireAudioEffects(Transform root, AudioEffect[] audioEffects)
		{
			base.AssignFireAudioEffects(root, audioEffects);
			_fireAudio = audioEffects;
		}

		protected override void FireVisualProjectile(int projectileIndex, bool playFireEffects)
		{
			BulletData bullet = _bullets[projectileIndex % MAX_BULLETS];
			if (bullet.ShotIndex == projectileIndex && bullet.State != (byte)EBulletState.None && _projectile != null)
			{
				// Phase 11c: every bullet is followed for its impact, every Nth draws a tracer
				int every = _gunData != null ? Mathf.Max(1, FeelSettings.ByClass(FeelSettings.Current.TracerEvery, _gunData.Class)) : 1;
				SpawnTracer(projectileIndex % MAX_BULLETS, bullet, projectileIndex % every == 0);
			}

			if (playFireEffects == true)
			{
				ApplyGunshotRange();
				PlayShotEffects();
			}

			base.FireVisualProjectile(projectileIndex, playFireEffects);
		}

		// PRIVATE METHODS

		/// <summary>Phase 11c: smoke off the muzzle and a shell out of the ejection port, sized by gun class.</summary>
		private void PlayShotEffects()
		{
			if (_gunData == null)
				return;

			GunEffects effects = GunEffects.Get(Runner);
			Transform  muzzle  = GetVisualMuzzle(out bool firstPerson);
			if (effects == null || muzzle == null)
				return;

			FeelSettings settings = FeelSettings.Current;
			EGunClass    gunClass = _gunData.Class;

			Vector3 forward = muzzle.forward;
			Vector3 right   = muzzle.right;
			Vector3 up      = muzzle.up;
			if (firstPerson == true && Context.Camera != null)
			{
				Transform view = Context.Camera.Camera.transform;
				forward = view.forward;
				right   = view.right;
				up      = view.up;
			}

			_smokeDebt += FeelSettings.ByClass(settings.MuzzleSmoke, gunClass, 0.5f) * (IsSuppressed == true ? settings.SuppressedSmoke : 1.0f);
			int puffs = Mathf.FloorToInt(_smokeDebt);
			if (puffs > 0)
			{
				_smokeDebt -= puffs;
				effects.MuzzleSmoke(muzzle.position, forward, puffs, firstPerson);
			}

			Vector3 port = muzzle.position - forward * FeelSettings.ByClass(settings.EjectBack, gunClass, 0.4f) + up * 0.03f + right * 0.02f;
			effects.Shell(port, right, up, FeelSettings.ByClass(settings.ShellLength, gunClass, 0.045f), firstPerson);
		}

		private void UpdateReload()
		{
			if (IsReloading == false)
				return;

			int elapsedTicks = Runner.Tick - _reloadStartTick;

			GunStats stats = Stats;

			if (AmmoInDone == false && elapsedTicks >= SecondsToTicks(stats.AmmoInTime))
			{
				int needed = stats.Magazine - _magazineAmmo;
				int taken  = OwnerWeapons != null ? OwnerWeapons.TakeAmmo(_gunData.Caliber, needed) : 0;

				_magazineAmmo += taken;
				AmmoInDone     = true;

				LogReload($"ammo-in +{taken}, mag {_magazineAmmo}");
			}

			if (elapsedTicks >= SecondsToTicks(stats.ReloadTime))
			{
				IsReloading = false;
				AmmoInDone  = false;

				LogReload("done, can fire");
			}
		}

		/// <summary>
		/// Earliest moment the next shot can happen inside this tick, or false if not this tick.
		/// Semi and bolt guns need a press; a press up to PressBufferTime early fires as soon as the gun is ready.
		/// </summary>
		private bool TryGetShotTime(bool keyDown, bool recordPress, out double shotTime)
		{
			shotTime = 0.0;

			double deltaTime = Runner.DeltaTime;
			double tickEnd   = Runner.Tick * deltaTime;
			double tickStart = tickEnd - deltaTime;
			double ready     = GetTime(_readyTick, _readyFraction);

			if (IsFiring == true)
			{
				// A started burst always finishes
				shotTime = System.Math.Max(ready, tickStart);
				return shotTime <= tickEnd + 0.000001;
			}

			float  localAlpha = Character != null ? Character.Agent.AgentInput.FixedInput.LocalAlpha : 0.0f;
			double pressTime  = localAlpha > 0.0f && localAlpha <= 1.0f ? tickStart + localAlpha * deltaTime : tickEnd;
			double earliest;

			switch (_gunData.FireMode)
			{
				case EFireMode.Semi:
				case EFireMode.Bolt:
				{
					double buffer = GunplaySettings.Current.PressBufferTime;

					if (keyDown == true)
					{
						if (pressTime < ready - buffer)
							return false; // Too early: the press is ignored

						if (ready > tickEnd)
						{
							if (recordPress == true)
							{
								_bufferedPressTick = Runner.Tick;
							}

							return false;
						}

						earliest = pressTime;
					}
					else if (_bufferedPressTick > 0 && _bufferedPressTick * deltaTime >= ready - buffer - deltaTime)
					{
						earliest = tickStart;
					}
					else
					{
						return false;
					}

					break;
				}
				default:
				{
					// Auto, and burst guns repeating while held
					earliest = keyDown == true ? pressTime : tickStart;
					break;
				}
			}

			shotTime = System.Math.Max(ready, earliest);
			return shotTime <= tickEnd + 0.000001;
		}

		private void Kick()
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (GunplayDebug.NoRecoil == true)
				return;
#endif
			GunplaySettings settings = GunplaySettings.Current;

			// Phase 11e: the measured numbers are where the view sat during a spray, so each shot sets where the view heads
			// (UpdateRecoil springs it there) instead of adding a new kick on top of the last one
			bool    isAimed  = Character != null && Character.CharacterController.FixedData.Aim == true;
			bool    kickOnly = false;
			Vector2 target;

			if (isAimed == true || _gunData.HasHipRecoil == false)
			{
				if (_gunData.HasMeasuredRecoil == true)
				{
					target = _gunData.GetAdsOffset(_recoilShotIndex, _projectilesCount, settings.RecoilWanderUp);
				}
				else
				{
					// Snipers: one kick up per shot that comes back down before the next
					target   = new Vector2(0.0f, settings.UnmeasuredKickUp);
					kickOnly = true;
				}
			}
			else
			{
				target = new Vector2(GunData.Hash(_projectilesCount, 3) * settings.HipSideFraction * _gunData.HipSideRange, Mathf.Min((_recoilShotIndex + 1) * _gunData.HipClimbPerShot, settings.HipClimbMax));
			}

			target *= Stats.Recoil * AgentGear.GetRecoilMultiplier(Owner != null ? Owner.GetComponent<Agent>() : null); // O2 Max (phase 5b)

			_recoilShotIndex++;
			_recoilTarget   = target;
			_recoilKickOnly = kickOnly;

			// A kick lands at once; a measured offset is sprung toward
			if (kickOnly == true)
			{
				Recoil       += target - _recoilOffset;
				_recoilOffset = target;
			}
		}

		/// <summary>
		/// Phase 11e: while firing, the gun's offset springs toward the last shot's target and the view moves by the change
		/// (so pulling against it still counts); once the trigger is let go both settle back to the aim point.
		/// </summary>
		private void UpdateRecoil(bool isIdle)
		{
			GunplaySettings settings  = GunplaySettings.Current;
			float           deltaTime = Runner.DeltaTime;

			if (isIdle == true)
			{
				_recoilTarget = Vector2.zero;

				if (Recoil != Vector2.zero || _recoilOffset != Vector2.zero)
				{
					float settle = Mathf.Max(_gunData.AdsSettleTime, settings.RecoilSettleMin);
					float keep   = Mathf.Exp(-3.0f * deltaTime / settle); // About 95% back within the settle time

					Recoil        = Recoil.sqrMagnitude < 0.0001f ? Vector2.zero : Recoil * keep;
					_recoilOffset = _recoilOffset.sqrMagnitude < 0.0001f ? Vector2.zero : _recoilOffset * keep;
				}

				if (_recoilOffset.sqrMagnitude < 0.0025f)
				{
					_recoilShotIndex = 0;
				}

				return;
			}

			Vector2 target = _recoilTarget;
			if (_recoilKickOnly == true && (Runner.Tick - _lastShotTick) * deltaTime >= settings.UnmeasuredKickHold)
			{
				target = Vector2.zero;
			}

			Vector2 offset = Vector2.Lerp(_recoilOffset, target, 1.0f - Mathf.Exp(-settings.RecoilSpring * deltaTime));
			Recoil       += offset - _recoilOffset;
			_recoilOffset = offset;
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>Test hook (Recoil Check): the gun's own view offset (side, up; deg) and the shot count of the current spray.</summary>
		public Vector2 DebugRecoilOffset => _recoilOffset;
		public int     DebugRecoilShot   => _recoilShotIndex;
#endif

		private void UpdateBullets()
		{
			float deltaTime = Runner.DeltaTime;
			float lifetime  = GunplaySettings.Current.BulletLifetime;

			for (int i = 0; i < MAX_BULLETS; ++i)
			{
				BulletData bullet = _bullets[i];
				if (bullet.State != (byte)EBulletState.Flying || bullet.FireTick >= Runner.Tick)
					continue;

				float from = (Runner.Tick - 1 - bullet.FireTick) * deltaTime + bullet.FireOffset;
				float to   = Mathf.Min(from + deltaTime, lifetime);

				SimulateBullet(ref bullet, from, to, false);

				if (bullet.State == (byte)EBulletState.Flying && to >= lifetime)
				{
					bullet.State      = (byte)EBulletState.Expired;
					bullet.ImpactTime = lifetime;
				}

				_bullets.Set(i, bullet);
			}
		}

		private void SimulateBullet(ref BulletData bullet, float fromTime, float toTime, bool useShotView)
		{
			Vector3 start   = GetBulletPosition(bullet, fromTime);
			Vector3 end     = GetBulletPosition(bullet, toTime);
			Vector3 segment = end - start;
			float   length  = segment.magnitude;

			if (length < 0.0001f)
				return;

			Vector3 direction     = segment / length;
			int     ownerObjectID = Owner != null ? Owner.gameObject.GetInstanceID() : 0;

			GameplayInput input = Character != null ? Character.Agent.AgentInput.FixedInput : default;
			if (useShotView == true && input.InterpolationFromTick != default && input.InterpolationToTick != default && input.InterpolationAlpha != default)
			{
				ProjectileUtility.ProjectileCast(Runner, Object.InputAuthority, ownerObjectID, input.InterpolationFromTick, input.InterpolationToTick, input.InterpolationAlpha, start, direction, length, _hitMask, _validHits);
			}
			else
			{
				ProjectileUtility.ProjectileCast(Runner, Object.InputAuthority, ownerObjectID, start, direction, length, _hitMask, _validHits);
			}

			if (_validHits.Count == 0)
				return;

			// No penetration: the first thing hit stops the bullet
			LagCompensatedHit hit = _validHits[0];

			bullet.ImpactTime    = Mathf.Lerp(fromTime, toTime, Mathf.Clamp01(hit.Distance / length));
			bullet.ImpactPoint   = hit.Point;
			bullet.ImpactNormal  = (hit.Normal - direction) * 0.5f;
			bullet.ImpactSurface = (int)(hit.Hitbox != null ? ESurface.Body : GunEffects.GetSurface(hit.GameObject));
			bullet.State         = (byte)(hit.Hitbox != null ? EBulletState.HitTarget : EBulletState.HitEnvironment);

			float  distance = Vector3.Distance(bullet.Origin, hit.Point);
			float  fireTime = bullet.FireTick * Runner.DeltaTime - bullet.FireOffset;
			string part     = hit.Hitbox != null ? hit.Hitbox.name : (hit.GameObject != null ? hit.GameObject.name : "?");
			float  damage   = 0.0f;
			string target   = "environment";

			if (hit.Hitbox != null)
			{
				damage = ApplyDamage(hit, direction, distance, fireTime, out target);
			}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			GunplayDebug.LogImpact(_gunData.GunName, Runner.Tick, fireTime, bullet.Origin, hit.Point, target, part, damage);
#endif
		}

		private float ApplyDamage(LagCompensatedHit hit, Vector3 direction, float distance, float fireTime, out string targetName)
		{
			targetName = "?";

			IHitTarget target = hit.Hitbox.Root != null ? hit.Hitbox.Root.GetComponent<IHitTarget>() : null;
			if (target == null)
				return 0.0f;

			// Head multiplier from the gun's data; every other body part takes full damage (Phase 3a spec, item 4)
			bool  isHead     = hit.Hitbox is BodyPart bodyPart && bodyPart.IsCritical == true;
			float multiplier = isHead == true ? _gunData.HeadMultiplier : 1.0f;
			float damage     = _gunData.Damage * GunplaySettings.Current.GetFalloff(_gunData.Class, distance, Stats.FalloffRange) * multiplier;

			Health health       = target as Health;
			float  healthBefore = health != null ? health.CurrentHealth : 0.0f;

			var hitData = new HitData
			{
				Action        = EHitAction.Damage,
				Amount        = damage,
				IsCritical    = isHead,
				IsBullet      = true,
				Position      = hit.Point,
				Normal        = hit.Normal,
				Direction     = direction,
				Target        = target,
				InstigatorRef = Object.InputAuthority,
				Instigator    = Owner != null ? Owner.GetComponent<IHitInstigator>() : null,
				HitType       = HitType,
			};

			HitUtility.ProcessHit(ref hitData);

			targetName = hit.Hitbox.Root.name;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (health != null)
			{
				GunplayDebug.TrackDamage(health.GetInstanceID(), targetName, healthBefore, health.MaxHealth, fireTime, hitData.IsFatal, _gunData.GunName);
			}
#endif
			return hitData.Amount;
		}

		private Vector3 GetBulletPosition(BulletData bullet, float time)
		{
			float gravity = GunplaySettings.Current.Gravity;
			return bullet.Origin + bullet.Velocity * time + new Vector3(0.0f, -0.5f * gravity * time * time, 0.0f);
		}

		private Vector3 GetBulletVelocity(BulletData bullet, float time)
		{
			return bullet.Velocity + new Vector3(0.0f, -GunplaySettings.Current.Gravity * time, 0.0f);
		}

		private int SecondsToTicks(float seconds)
		{
			return Mathf.CeilToInt(seconds / Runner.DeltaTime - 0.0001f);
		}

		private double GetTime(int tick, float fraction)
		{
			return (tick + (double)fraction) * Runner.DeltaTime;
		}

		private void SetTime(double time, out int tick, out float fraction)
		{
			double ticks = time / Runner.DeltaTime;
			tick     = (int)System.Math.Floor(ticks);
			fraction = (float)(ticks - tick);
		}

		[System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
		private void LogReload(string text)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (HasStateAuthority == true && Runner.IsForward == true)
			{
				GunplayDebug.LogEvent($"RELOAD {_gunData.GunName} tick {Runner.Tick} ({(Runner.Tick - _reloadStartTick) * Runner.DeltaTime * 1000.0f:F0} ms): {text}");
			}
#endif
		}

		private GunStats GetStats()
		{
			if (_gunData == null)
				return default;

			long key = AttachmentKey;
			if (key != _statsKey)
			{
				GunplaySettings settings = GunplaySettings.Current;
				for (int i = 0; i < AttachmentData.SlotCount; ++i)
				{
					_statsAttachments[i] = settings.GetAttachment((int)((key >> (8 * i)) & 0xFF));
				}

				_stats    = GunStats.Compute(_gunData, settings, _statsAttachments);
				_statsKey = key;
			}

			return _stats;
		}

		/// <summary>Suppressor: the agent's gunshot sources reach GunshotRange x their own range for this gun's shots.</summary>
		private void ApplyGunshotRange()
		{
			if (_fireAudio == null)
				return;

			if (_gunshotBaseRange.Count > 256)
			{
				var stale = new List<AudioSource>();
				foreach (AudioSource source in _gunshotBaseRange.Keys)
				{
					if (source == null)
					{
						stale.Add(source);
					}
				}

				foreach (AudioSource source in stale)
				{
					_gunshotBaseRange.Remove(source);
				}
			}

			float range = Stats.GunshotRange;
			for (int i = 0; i < _fireAudio.Length; ++i)
			{
				AudioSource source = _fireAudio[i] != null ? _fireAudio[i].AudioSource : null;
				if (source == null)
					continue;

				if (_gunshotBaseRange.TryGetValue(source, out float baseRange) == false)
				{
					baseRange = source.maxDistance;
					_gunshotBaseRange.Add(source, baseRange);
				}

				source.maxDistance = baseRange * range;
			}
		}

		private bool IsScopedDebug()
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			return GunplayDebug.ScopedIrons;
#else
			return false;
#endif
		}

		private void OnReloadStarted()
		{
			if (IsReloading == true)
			{
				PlayLocalSound(ReloadSound);
			}
		}

		// TRACERS (render only)

		private void SpawnTracer(int slot, BulletData bullet, bool visible)
		{
			Vector3 visualStart = GetVisualFirePosition();

			// Phase 11c: a bullet without a tracer is still followed, for its impact
			DummyProjectile instance = null;
			if (visible == true)
			{
				instance = Context.ObjectCache.Get(_projectile);
				Runner.MoveToRunnerScene(instance);
				instance.Context = Context;

				// Its own Update would fly it in a straight line; we drive it along the arc instead
				instance.Fire(visualStart, Quaternion.LookRotation(bullet.Velocity), visualStart);
				instance.enabled = false;
			}

			_tracers.Add(new Tracer
			{
				Instance     = instance,
				Slot         = slot,
				ShotIndex    = bullet.ShotIndex,
				Time         = 0.0f,
				VisualOffset = visualStart - bullet.Origin,
				LastPosition = visualStart,
			});
		}

		private void UpdateTracers(float deltaTime)
		{
			float lifetime = GunplaySettings.Current.BulletLifetime;

			for (int i = _tracers.Count - 1; i >= 0; --i)
			{
				Tracer     tracer = _tracers[i];
				BulletData bullet = _bullets[tracer.Slot];

				if (bullet.ShotIndex != tracer.ShotIndex || bullet.State == (byte)EBulletState.None)
				{
					ReturnTracer(i);
					continue;
				}

				tracer.Time += deltaTime;

				bool  hasImpact = bullet.State == (byte)EBulletState.HitEnvironment || bullet.State == (byte)EBulletState.HitTarget;
				float endTime   = hasImpact == true ? bullet.ImpactTime : lifetime;
				float time      = Mathf.Min(tracer.Time, endTime);

				float   speed       = Mathf.Max(1.0f, bullet.Velocity.magnitude);
				float   converge    = Mathf.Clamp01(time * speed / TRACER_CONVERGE_DIST);
				Vector3 position    = GetBulletPosition(bullet, time) + tracer.VisualOffset * (1.0f - converge);
				bool    finished    = tracer.Time >= endTime;

				// Until the server reports where it stopped, walls stop the tracer locally (visual only)
				if (hasImpact == false && finished == false)
				{
					Vector3 move   = position - tracer.LastPosition;
					float   length = move.magnitude;
					if (length > 0.0001f && Runner.GetPhysicsScene().Raycast(tracer.LastPosition, move / length, out RaycastHit wallHit, length, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
					{
						position = wallHit.point;
						finished = true;
						SpawnImpact(wallHit.point, wallHit.normal, GunEffects.GetSurface(wallHit.collider.gameObject));
						tracer.ImpactShown = true;
					}
				}

				if (tracer.Instance != null)
				{
					tracer.Instance.transform.SetPositionAndRotation(position, Quaternion.LookRotation(GetBulletVelocity(bullet, time)));
				}
				tracer.LastPosition = position;

				if (finished == true)
				{
					if (bullet.State == (byte)EBulletState.HitEnvironment && tracer.ImpactShown == false)
					{
						SpawnImpact(bullet.ImpactPoint, bullet.ImpactNormal, (ESurface)bullet.ImpactSurface);
					}
					else if (bullet.State == (byte)EBulletState.HitTarget)
					{
						// Phase 11c: a puff of blood where a player was hit
						GunEffects.Get(Runner)?.Impact(bullet.ImpactPoint, bullet.ImpactNormal, ESurface.Body);
					}

					ReturnTracer(i);
					continue;
				}

				_tracers[i] = tracer;
			}
		}

		private void SpawnImpact(Vector3 position, Vector3 normal, ESurface surface)
		{
			if (normal == Vector3.zero)
				return;

			// Phase 11c: dust, chips, sparks or a splash by surface (the sample's impact, with its dark smoke, only if switched back on)
			GunEffects.Get(Runner)?.Impact(position, normal, surface);

			if (_impactSetup == null || FeelSettings.Current.SampleImpacts == false)
				return;

			var impactPrefab = _impactSetup.GetImpact(0);
			if (impactPrefab == null)
				return;

			var impact = Context.ObjectCache.Get(impactPrefab);
			Context.ObjectCache.ReturnDeferred(impact, 5.0f);
			Runner.MoveToRunnerSceneExtended(impact);

			impact.transform.SetPositionAndRotation(position, Quaternion.LookRotation(normal));
		}

		private void ReturnTracer(int index)
		{
			Tracer tracer = _tracers[index];
			_tracers.RemoveAt(index);

			if (tracer.Instance != null)
			{
				tracer.Instance.enabled = true;
				if (Context != null)
				{
					Context.ObjectCache.Return(tracer.Instance);
				}
			}
		}

		// HELPERS

		private struct Tracer
		{
			public DummyProjectile Instance;
			public int             Slot;
			public int             ShotIndex;
			public float           Time;
			public Vector3         VisualOffset;
			public Vector3         LastPosition;
			public bool            ImpactShown;
		}
	}
}
