namespace TPSBR
{
	using UnityEngine;
	using Fusion.Addons.KCC;

	/// <summary>
	/// Phase 11c (camera feel): a landing dip, sprint and walk bob, a roll into slides and strafes, a kick on each shot and a punch when
	/// hit, all springing back. Added to the first-person camera after Character places it, so it's render only: shots and hits still go
	/// from the eye where you aim (Character.GetCameraTransform). The viewmodel rides on the camera, so it moves with it. FeelSettings.
	/// </summary>
	public sealed class CameraFeel
	{
		// PUBLIC MEMBERS

		/// <summary>Test hook: no camera feel at all (checks that need the exact eye).</summary>
		public static bool DebugOff;

		/// <summary>The last offsets applied, for the HUD Readout and checks.</summary>
		public Vector3 LastPosition { get; private set; }
		public Vector3 LastRotation { get; private set; }

		// PRIVATE MEMBERS

		private float   _dip;
		private float   _dipVelocity;
		private Vector3 _punch;          // pitch, yaw, roll (deg)
		private Vector3 _punchVelocity;
		private float   _bobPhase;
		private float   _bobWeight;
		private float   _tilt;

		private bool    _wasGrounded = true;
		private float   _fallSpeed;
		private Weapon  _weapon;
		private int     _shotCount;

		// PUBLIC METHODS

		public void Reset()
		{
			_dip           = 0.0f;
			_dipVelocity   = 0.0f;
			_punch         = Vector3.zero;
			_punchVelocity = Vector3.zero;
			_bobWeight     = 0.0f;
			_tilt          = 0.0f;
			_wasGrounded   = true;
			_fallSpeed     = 0.0f;
			_weapon        = null;
			LastPosition   = Vector3.zero;
			LastRotation   = Vector3.zero;
		}

		/// <summary>A hit on you: the view punches the way the shot travels; the hit that downs you punches hard.</summary>
		public void AddHit(HitData hit, Quaternion view, bool downed)
		{
			if (hit.Action != EHitAction.Damage || hit.Amount <= 0.0f)
				return;

			FeelSettings settings = FeelSettings.Current;
			float amount = downed == true ? settings.DownedPunch : Mathf.Min(settings.HitPunchMax, hit.Amount * settings.HitPunchPerDamage);

			Vector3 local = hit.Direction.sqrMagnitude > 0.0001f ? Quaternion.Inverse(view) * hit.Direction.normalized : Vector3.forward;
			float   side  = Mathf.Abs(local.x) > 0.05f ? Mathf.Sign(local.x) : (Random.value < 0.5f ? -1.0f : 1.0f);

			_punch += new Vector3(-amount * 0.6f - local.y * amount * 0.4f, local.x * amount * 0.6f, -side * amount * 0.8f);
		}

		/// <summary>Every frame for the observed agent in first person, after the camera is placed.</summary>
		public void Apply(Agent agent, Transform camera, float aim, float deltaTime)
		{
			FeelSettings settings = FeelSettings.Current;
			KCC          kcc      = agent.Character.CharacterController;
			KCCData      data     = kcc.Data;
			EMoveMode    mode     = data.MoveMode;
			bool         grounded = data.IsGrounded == true;
			Vector3      velocity = data.RealVelocity;

			deltaTime = Mathf.Min(deltaTime, 0.05f);

			// Landing: a dip that grows with the fall speed (not the drop's landing, which has its roll)
			bool landingMode = mode == EMoveMode.LandRoll || AgentMovement.IsDropMode(mode) == true || mode == EMoveMode.Mantle || mode == EMoveMode.Grapple;
			if (grounded == false)
			{
				_fallSpeed = Mathf.Max(0.0f, -velocity.y);
			}
			else if (_wasGrounded == false && landingMode == false && _fallSpeed >= settings.LandDipMinSpeed)
			{
				float dip = Mathf.Min(settings.LandDipMax, _fallSpeed * settings.LandDipPerSpeed);
				_dipVelocity -= dip * Mathf.Sqrt(settings.SpringStiffness) * 2.0f;
				_fallSpeed = 0.0f;
			}
			_wasGrounded = grounded;

			// A kick on each shot (shotgun-free roster: one per bullet fired)
			Weapon weapon = agent.Weapons.CurrentWeapon;
			if (weapon is FirearmWeapon firearm)
			{
				if (weapon != _weapon)
				{
					_weapon    = weapon;
					_shotCount = firearm.ShotCount;
				}
				else if (firearm.ShotCount != _shotCount)
				{
					int       shots    = Mathf.Clamp(firearm.ShotCount - _shotCount, 1, 3);
					// Phase 11e: the punch by gun class comes from recoil_feel (GunData); FeelSettings' table is for other weapons
					GunData   gunData  = weapon is BulletWeapon bullet ? bullet.GunData : null;
					float     share    = Mathf.Lerp(1.0f, settings.FireKickAimed, aim);
					float     pitch    = (gunData != null ? gunData.PunchPitch : FeelSettings.ByClass(settings.FireKickPitch, EGunClass.AR, 0.3f)) * share;
					float     roll     = (gunData != null ? gunData.PunchRoll  : FeelSettings.ByClass(settings.FireKickRoll,  EGunClass.AR, 0.2f)) * share;

					_punch    += new Vector3(-pitch, 0.0f, (Random.value < 0.5f ? -roll : roll)) * shots;
					_shotCount = firearm.ShotCount;
				}
			}
			else
			{
				_weapon = weapon;
			}

			// Springs back
			Spring(ref _dip, ref _dipVelocity, settings, deltaTime);
			Spring(ref _punch.x, ref _punchVelocity.x, settings, deltaTime);
			Spring(ref _punch.y, ref _punchVelocity.y, settings, deltaTime);
			Spring(ref _punch.z, ref _punchVelocity.z, settings, deltaTime);
			_punch = Vector3.ClampMagnitude(_punch, 12.0f);

			// Bob: sprinting full, walking a share, almost none aimed
			Vector3 flat     = new Vector3(velocity.x, 0.0f, velocity.z);
			float   speed    = flat.magnitude;
			float   sprint   = Mathf.Max(0.1f, MovementSettings.Current.SprintSpeed);
			bool    bobbing  = grounded == true && speed > 0.5f && (mode == EMoveMode.Walk || mode == EMoveMode.Sprint || mode == EMoveMode.Crouch);
			float   target   = bobbing == false ? 0.0f : (mode == EMoveMode.Sprint ? 1.0f : settings.WalkBobShare * Mathf.Clamp01(speed / sprint * 1.6f));
			target *= Mathf.Lerp(1.0f, 0.1f, aim);
			_bobWeight = Mathf.MoveTowards(_bobWeight, target, deltaTime * 4.0f);
			_bobPhase  = Mathf.Repeat(_bobPhase + deltaTime * Mathf.PI * settings.SprintBobRate * Mathf.Clamp(speed / sprint, 0.5f, 1.3f), Mathf.PI * 2.0f);

			float   wave = Mathf.Sin(_bobPhase);
			Vector3 bob  = new Vector3(wave * settings.SprintBobSide, (Mathf.Cos(_bobPhase * 2.0f) - 1.0f) * 0.5f * settings.SprintBobHeight, 0.0f) * _bobWeight;
			float   bobRoll = wave * settings.SprintBobRoll * _bobWeight;

			// Roll: into a slide, and with the sideways speed
			Quaternion yaw    = Quaternion.Euler(0.0f, data.LookYaw, 0.0f);
			float      sideways = (Quaternion.Inverse(yaw) * flat).x;
			float      tilt   = mode == EMoveMode.Slide ? settings.SlideTilt : 0.0f;
			tilt -= Mathf.Clamp(sideways / sprint, -1.0f, 1.0f) * settings.StrafeTilt * (1.0f - aim * 0.7f);
			_tilt = Mathf.Lerp(_tilt, tilt, 1.0f - Mathf.Exp(-settings.TiltSpeed * deltaTime));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (DebugOff == true || Viewmodel.DebugFreezeMotion == true)
			{
				LastPosition = Vector3.zero;
				LastRotation = Vector3.zero;
				return;
			}
#endif

			Vector3 position = new Vector3(bob.x, bob.y + _dip, 0.0f);
			Vector3 rotation = new Vector3(_punch.x, _punch.y, _punch.z + bobRoll + _tilt);

			Quaternion view = camera.rotation;
			camera.SetPositionAndRotation(camera.position + view * position, view * Quaternion.Euler(rotation));

			LastPosition = position;
			LastRotation = rotation;
		}

		// PRIVATE METHODS

		private static void Spring(ref float value, ref float velocity, FeelSettings settings, float deltaTime)
		{
			velocity += (-settings.SpringStiffness * value - settings.SpringDamping * velocity) * deltaTime;
			value    += velocity * deltaTime;
		}
	}
}
