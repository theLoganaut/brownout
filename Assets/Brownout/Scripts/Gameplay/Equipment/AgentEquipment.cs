namespace TPSBR
{
	using Fusion;
	using Fusion.Addons.KCC;
	using UnityEngine;

	/// <summary>
	/// Using the Equipment slot on E (phase 5b): throwing a grenade without a weapon switch, and the grapple. Runs every tick on the server
	/// and the predicting input authority, from Agent.ProcessFixedInput before the movement update. The state is networked on the agent
	/// (Agent.Equipment.cs); only the server spawns grenades and takes items out of the slot.
	///
	/// Grenade: the gun lowers, the grenade leaves the hand PressToRelease after the press (a frag only when E is let go, so holding E cooks
	/// it; held for its whole fuse it goes off in the hand), and the gun is ready PressToGunReady - PressToRelease after the release.
	/// Grapple: fires GrappleDeployTime after the press; on a hit the hook lands GrappleHookTime later and AgentMovement pulls the player to
	/// it (EMoveMode.Grapple); the gun is ready GrappleRecoverTime after the pull ends. A miss uses nothing.
	/// While either is going, the hands are busy (Health.IsHandsBusy): no firing, aiming, reloading, punching, swapping or healing.
	/// </summary>
	public sealed class AgentEquipment
	{
		// PRIVATE MEMBERS

		private readonly Agent _agent;

		private NetworkRunner Runner => _agent.Runner;

		// CONSTRUCTORS

		public AgentEquipment(Agent agent)
		{
			_agent = agent;
		}

		// PUBLIC MEMBERS

		public bool       IsBusy      => _agent.EquipKindValue != 0;
		public EEquipment CurrentKind => (EEquipment)Mathf.Max(0, _agent.EquipKindValue - 1);
		public bool       IsThrowing  => IsBusy == true && CurrentKind != EEquipment.Grapple;
		public bool       IsGrappling => IsBusy == true && CurrentKind == EEquipment.Grapple;
		public bool       HasReleased => _agent.EquipReleaseTick > 0;
		/// <summary>Grapple: the hook is out (flying, or pulling).</summary>
		public bool       IsHookOut   => IsGrappling == true && _agent.GrappleHookTick > 0;

		/// <summary>Seconds since E was pressed (render time on the local player, for the viewmodel).</summary>
		public float GetElapsed(bool render)
		{
			if (IsBusy == false)
				return 0.0f;

			float now = render == true ? Runner.LocalRenderTime : Runner.Tick * Runner.DeltaTime;
			return Mathf.Max(0.0f, now - _agent.EquipStartTick * Runner.DeltaTime);
		}

		/// <summary>Seconds since the release (0 before it).</summary>
		public float GetReleaseElapsed(bool render)
		{
			if (IsBusy == false || _agent.EquipReleaseTick <= 0)
				return 0.0f;

			float now = render == true ? Runner.LocalRenderTime : Runner.Tick * Runner.DeltaTime;
			return Mathf.Max(0.0f, now - _agent.EquipReleaseTick * Runner.DeltaTime);
		}

		// PUBLIC METHODS

		/// <summary>Before the movement update: starts, advances and ends the equipment action, and strips the inputs it blocks.</summary>
		public void ProcessInput(ref GameplayInput input, AgentInput agentInput)
		{
			Health health = _agent.Health;

			if (health.IsAlive == false || health.IsDowned == true)
			{
				if (IsBusy == true)
				{
					Interrupt(health.IsAlive == false ? "dead" : "downed");
				}

				return;
			}

			if (IsBusy == false)
			{
				if (agentInput.WasActivated(EGameplayInputAction.Equipment, input) == true)
				{
					TryStart(input, out string refusal);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					if (refusal != null && _agent.EquipmentSlot.IsEmpty == false)
					{
						EquipmentDebug.Log(Runner, $"REFUSED {_agent.name} E: {refusal}");
					}
#endif
				}

				if (IsBusy == false)
					return;
			}

			if (IsGrappling == true)
			{
				UpdateGrapple();
			}
			else
			{
				UpdateThrow(input);
			}

			if (IsBusy == true)
			{
				// Hands busy: the gun stays quiet until the action is over
				input.Attack = false;
				input.Aim    = false;
				input.Reload = false;
				input.Melee  = false;
				input.Heal   = false;
				input.Weapon = 0;
			}
		}

		/// <summary>Server, at spawn: nothing in hand.</summary>
		public void Reset()
		{
			_agent.EquipKindValue       = 0;
			_agent.EquipStartTick       = 0;
			_agent.EquipReleaseTick     = 0;
			_agent.EquipReadyTick       = 0;
			_agent.GrappleHookTick      = 0;
			_agent.GrapplePullStartTick = 0;
		}

		// PRIVATE METHODS

		private bool TryStart(GameplayInput input, out string refusal)
		{
			refusal = null;

			LootStack slot = _agent.EquipmentSlot;
			if (slot.Kind != EItemKind.Equipment || slot.IsEmpty == true)
			{
				refusal = "nothing equipped";
				return false;
			}

			if (slot.Equipment == EEquipment.Grapple && slot.Charges <= 0)
			{
				refusal = "grapple empty";
				return false;
			}

			Health  health  = _agent.Health;
			Weapons weapons = _agent.Weapons;

			if (health.IsHealing == true)                    { refusal = "healing";  return false; }
			if (health.IsReviving == true)                   { refusal = "reviving"; return false; }
			if (weapons.IsSwitchingWeapon() == true)         { refusal = "swapping"; return false; }
			if (weapons.IsDrawing == true)                   { refusal = "gun raising"; return false; }
			if (weapons.IsPunching == true)                  { refusal = "punching"; return false; }
			if (weapons.CurrentWeapon is FirearmWeapon firearm && firearm.IsReloading == true) { refusal = "reloading"; return false; }

			EMoveMode mode = _agent.Character.CharacterController.FixedData.MoveMode;
			if (mode == EMoveMode.Mantle || mode == EMoveMode.Grapple || mode == EMoveMode.Downed)
			{
				refusal = mode.ToString();
				return false;
			}

			int tick = Runner.Tick;

			_agent.EquipKindValue       = (byte)(slot.Index + 1);
			_agent.EquipStartTick       = tick;
			_agent.EquipReleaseTick     = 0;
			_agent.EquipReadyTick       = 0;
			_agent.GrappleHookTick      = 0;
			_agent.GrapplePullStartTick = 0;

			weapons.CancelGunActions();

			EquipmentDebug.Log(Runner, $"{(slot.Equipment == EEquipment.Grapple ? "GRAPPLE" : "THROW")} {_agent.name} {EquipmentSettings.GetName(slot.Equipment)} pressed{(slot.Equipment == EEquipment.Frag ? " (pin out, fuse running)" : "")}");
			return true;
		}

		private void UpdateThrow(GameplayInput input)
		{
			EquipmentSettings settings = EquipmentSettings.Current;
			EEquipment        kind     = CurrentKind;
			int               tick     = Runner.Tick;

			if (HasReleased == false)
			{
				bool minReached = tick >= _agent.EquipStartTick + SecondsToTicks(settings.PressToRelease);
				bool cooking    = kind == EEquipment.Frag && input.Equipment == true;

				if (kind == EEquipment.Frag && cooking == true && tick >= _agent.EquipStartTick + SecondsToTicks(settings.FragFuse))
				{
					Release(tick, true);
				}
				else if (minReached == true && cooking == false)
				{
					Release(tick, false);
				}

				return;
			}

			if (tick >= _agent.EquipReadyTick)
			{
				Finish();
			}
		}

		/// <summary>The grenade leaves the hand (inHand: a cooked frag held too long goes off where you stand).</summary>
		private void Release(int tick, bool inHand)
		{
			EquipmentSettings settings = EquipmentSettings.Current;
			EEquipment        kind     = CurrentKind;

			_agent.EquipReleaseTick = tick;
			_agent.EquipReadyTick   = tick + SecondsToTicks(Mathf.Max(0.0f, settings.PressToGunReady - settings.PressToRelease));

			if (_agent.HasStateAuthority == false || Runner.IsForward == false)
				return;

			float fuse;
			if (kind == EEquipment.Frag)
			{
				float cooked = (tick - _agent.EquipStartTick) * Runner.DeltaTime;
				fuse = inHand == true ? 0.0f : Mathf.Max(0.0f, settings.FragFuse - cooked);
			}
			else
			{
				fuse = settings.GetFuse(kind);
			}

			if (ThrowGrenade(kind, inHand == false, fuse) == false)
				return;

			_agent.Inventory.UseEquipment();
		}

		/// <summary>Server: spawns the grenade at the eye (or the feet when dropped) with its fuse.</summary>
		private bool ThrowGrenade(EEquipment kind, bool thrown, float fuse)
		{
			EquipmentSettings   settings = EquipmentSettings.Current;
			KinematicProjectile prefab   = settings.GetGrenadePrefab(kind);
			if (prefab == null)
			{
				Debug.LogError($"EquipmentSettings has no grenade prefab for {kind}. Run Brownout → Setup → Build Equipment Prefabs.");
				return false;
			}

			TransformData eye       = _agent.Character.GetCameraTransform(false);
			Vector3       direction = eye.Rotation * Vector3.forward;
			Vector3       position  = eye.Position;
			Vector3       velocity  = Vector3.zero;

			if (thrown == true)
			{
				// In front of the eye, unless a wall is closer
				if (Runner.GetPhysicsScene().Raycast(eye.Position, direction, out RaycastHit hit, settings.ThrowForward + 0.1f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == false)
				{
					position = eye.Position + direction * settings.ThrowForward;
				}

				Vector3 ownerVelocity = _agent.Character.CharacterController.FixedData.RealVelocity;
				if (ownerVelocity.y < 0.0f)
				{
					ownerVelocity.y = 0.0f;
				}

				velocity = direction * settings.ThrowSpeed + ownerVelocity;
			}
			else
			{
				position = _agent.transform.position + Vector3.up * 0.3f;
			}

			KinematicProjectile projectile = Runner.Spawn(prefab, position, Quaternion.LookRotation(direction), _agent.Object.InputAuthority);
			if (projectile == null)
				return false;

			projectile.Fire(_agent.Object, position, velocity, _agent.Weapons.HitMask, EHitType.Grenade);
			projectile.SetDespawnCooldown(Mathf.Max(Runner.DeltaTime, fuse));

			float cooked = (Runner.Tick - _agent.EquipStartTick) * Runner.DeltaTime;
			EquipmentDebug.Log(Runner, $"THROW {_agent.name} {EquipmentSettings.GetName(kind)} {(thrown == true ? $"released {cooked:F3} s after the press at {velocity.magnitude:F1} m/s" : "dropped at the feet")}, goes off in {fuse:F2} s");
			return true;
		}

		private void UpdateGrapple()
		{
			EquipmentSettings settings = EquipmentSettings.Current;
			int               tick     = Runner.Tick;
			KCCData           data     = _agent.Character.CharacterController.FixedData;

			if (HasReleased == false)
			{
				if (tick >= _agent.EquipStartTick + SecondsToTicks(settings.GrappleDeployTime))
				{
					FireHook(tick, settings);
				}

				return;
			}

			if (_agent.GrappleHookTick > 0 && _agent.GrapplePullStartTick == 0)
			{
				if (tick >= _agent.GrappleHookTick)
				{
					StartPull(tick, settings);
				}

				return;
			}

			if (_agent.GrapplePullStartTick > 0 && _agent.EquipReadyTick == 0 && tick > _agent.GrapplePullStartTick && data.MoveMode != EMoveMode.Grapple)
			{
				_agent.EquipReadyTick = tick + SecondsToTicks(settings.GrappleRecoverTime);
				EquipmentDebug.Log(Runner, $"GRAPPLE {_agent.name} pull ended after {(tick - _agent.GrapplePullStartTick) * Runner.DeltaTime:F3} s at {data.TargetPosition.ToString("F2")}, {Vector3.Distance(data.TargetPosition, data.GrappleTarget):F2} m from the spot");
			}

			if (_agent.EquipReadyTick > 0 && tick >= _agent.EquipReadyTick)
			{
				Finish();
			}
		}

		private void FireHook(int tick, EquipmentSettings settings)
		{
			_agent.EquipReleaseTick = tick;

			TransformData eye       = _agent.Character.GetCameraTransform(false);
			Vector3       direction = eye.Rotation * Vector3.forward;

			if (Runner.GetPhysicsScene().Raycast(eye.Position, direction, out RaycastHit hit, settings.GrappleRange, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == false)
			{
				_agent.GrappleHookTick = 0;
				_agent.EquipReadyTick  = tick + SecondsToTicks(settings.GrappleMissRecover);
				EquipmentDebug.Log(Runner, $"GRAPPLE {_agent.name} missed (nothing within {settings.GrappleRange:F0} m), nothing used");
				return;
			}

			_agent.GrapplePoint    = hit.point;
			_agent.GrappleHookTick = tick + Mathf.Max(1, SecondsToTicks(settings.GrappleHookTime));

			// Where the feet go: a floor or roof right onto it, a wall just below the spot and out from it, so the end lift clears the ledge
			Vector3 target = hit.normal.y >= 0.5f ? hit.point + Vector3.up * 0.05f : hit.point + hit.normal * (MovementSettings.Current.CapsuleRadius + 0.05f) + Vector3.down * settings.GrappleWallDrop;
			_agent.Character.CharacterController.FixedData.GrappleTarget = target;

			EquipmentDebug.Log(Runner, $"GRAPPLE {_agent.name} hooked {hit.distance:F1} m away at {hit.point.ToString("F2")} ({(hit.normal.y >= 0.5f ? "floor or roof" : "wall")})");
		}

		private void StartPull(int tick, EquipmentSettings settings)
		{
			_agent.GrapplePullStartTick = tick;

			KCCData data   = _agent.Character.CharacterController.FixedData;
			Vector3 target = data.GrappleTarget;

			if (_agent.Movement.StartGrapple(target) == false)
			{
				// Already there: nothing to pull
				_agent.EquipReadyTick = tick + SecondsToTicks(settings.GrappleRecoverTime);
			}

			if (_agent.HasStateAuthority == true && Runner.IsForward == true)
			{
				_agent.Inventory.UseEquipment();
				EquipmentDebug.Log(Runner, $"GRAPPLE {_agent.name} pull start, {Vector3.Distance(data.TargetPosition, target):F1} m to go at {data.GrappleSpeed:F1} m/s");
			}
		}

		/// <summary>Downed or dead mid-action: a frag with its pin out falls at your feet; anything else is put away unused.</summary>
		private void Interrupt(string reason)
		{
			if (IsThrowing == true && HasReleased == false && CurrentKind == EEquipment.Frag && _agent.HasStateAuthority == true && Runner.IsForward == true)
			{
				float cooked = (Runner.Tick - _agent.EquipStartTick) * Runner.DeltaTime;
				if (ThrowGrenade(EEquipment.Frag, false, Mathf.Max(0.0f, EquipmentSettings.Current.FragFuse - cooked)) == true)
				{
					_agent.Inventory.UseEquipment();
				}
			}

			if (IsGrappling == true && _agent.Character.CharacterController.FixedData.MoveMode == EMoveMode.Grapple)
			{
				_agent.Movement.EndGrapple(false);
			}

			EquipmentDebug.Log(Runner, $"{(IsGrappling ? "GRAPPLE" : "THROW")} {_agent.name} interrupted ({reason})");
			Reset();
		}

		private void Finish()
		{
			EquipmentDebug.Log(Runner, $"{(IsGrappling ? "GRAPPLE" : "THROW")} {_agent.name} gun ready {(Runner.Tick - _agent.EquipStartTick) * Runner.DeltaTime:F3} s after the press");
			Reset();
		}

		private int SecondsToTicks(float seconds)
		{
			return Mathf.CeilToInt(seconds / Runner.DeltaTime - 0.0001f);
		}
	}
}
