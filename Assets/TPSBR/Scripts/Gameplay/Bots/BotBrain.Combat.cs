using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.AI;

namespace TPSBR
{
	/// <summary>
	/// A bot's senses and fighting (phase 9): sight in a cone with line-of-sight rays (walls, terrain, closed doors and, since phase 9,
	/// bushes block it), hearing unsuppressed shots, choosing a target, aiming with bullet drop and lead plus the skill's aim error,
	/// firing in bursts or paced shots, strafing, cover when hit, healing.
	/// </summary>
	public sealed partial class BotBrain
	{
		// PRIVATE MEMBERS

		private static readonly List<Agent>   _agentBuffer = new List<Agent>(64);
		private static readonly RaycastHit[]  _bushHits    = new RaycastHit[8];

		private Agent   _target;
		private bool    _targetVisible;
		private Vector3 _targetLastSeen;
		private float   _targetLastSeenTime;
		private float   _targetFirstSeen;
		private float   _trackStart;

		private Agent   _lastAttacker;
		private float   _lastHitTime = -100.0f;
		private bool    _wantCover;
		private float   _coverCooldownUntil;

		private Vector3 _heardPoint;
		private float   _heardTime = -100.0f;

		// Firing
		private float   _burstUntil;
		private float   _nextBurstTime;
		private float   _nextShotTime;
		private float   _nextPunchTime;
		private float   _nextSwapTime;
		private float   _nextReloadTime;
		private float   _aimStart;
		private bool    _wasAiming;
		private float   _lastAimOffset = 180.0f;

		// Strafing
		private float   _strafeSign;
		private float   _strafeUntil;

		// Cover
		private Vector3 _coverSpot;
		private Vector3 _coverThreat;
		private bool    _coverArrived;
		private float   _coverStart;
		private float   _coverWaitUntil;

		// PUBLIC METHODS

		/// <summary>Server, from Health: it was hit (after the damage is applied).</summary>
		public void OnHit(HitData hit)
		{
			Agent attacker = hit.Instigator is Health health ? health.Agent : null;
			float now      = Now;

			if (_agent.Health.IsAlive == false)
			{
				BotDebug.Log(_agent.Runner, $"DEATH {_agent.name} by {(attacker != null ? attacker.name : "?")}");
			}
			else if (_agent.Health.IsDowned == true)
			{
				BotDebug.Log(_agent.Runner, $"DOWNED {_agent.name} by {(attacker != null ? attacker.name : "?")}");
			}

			if (attacker == null || attacker == _agent || GameplayMode.AreTeammates(attacker, _agent) == true)
				return;

			BotDebug.Log(_agent.Runner, $"HIT {_agent.name} took {hit.Amount:F0} from {attacker.name} at {Vector3.Distance(attacker.transform.position, Position):F0} m, health {_agent.Health.CurrentHealth:F0}, armor {_agent.Health.ArmorLeft:F0}");

			_lastAttacker = attacker;
			_lastHitTime  = now;
			_wantCover    = true;

			if (_target == null || _targetVisible == false || _target.Health.IsAlive == false)
			{
				SetTarget(attacker, now);
				_targetLastSeen     = attacker.transform.position;
				_targetLastSeenTime = now;
			}

			BotBrain partner = FindPartner();
			if (partner != null)
			{
				partner.OnPartnerHit(attacker, now);
			}
		}

		/// <summary>Server, from BulletWeapon: an unsuppressed shot. Bots in earshot turn toward it.</summary>
		public static void OnShotFired(Agent shooter, Vector3 position)
		{
			if (shooter == null || shooter.Runner == null || shooter.Runner.IsServer == false || shooter.Runner.IsResimulation == true)
				return;

			float range = BotSettings.Current.HearingRange;

			for (int i = 0; i < _all.Count; ++i)
			{
				BotBrain bot = _all[i];
				if (bot._agent == shooter || bot._agent.Runner != shooter.Runner || bot._agent.Health.IsUp == false)
					continue;
				if (GameplayMode.AreTeammates(bot._agent, shooter) == true || bot.IsIgnored(shooter) == true)
					continue;
				if ((bot.Position - position).sqrMagnitude > range * range)
					continue;

				bot._heardPoint = position;
				bot._heardTime  = bot.Now;
			}
		}

		// SENSES

		private void UpdateSenses(float now)
		{
			BotSettings settings = BotSettings.Current;
			Vector3     eye      = Eye;
			Vector3     forward  = Forward;
			forward.y = 0.0f;
			forward.Normalize();

			float halfCone = settings.SightCone * 0.5f;
			Agent best     = null;
			float bestScore = float.MaxValue;

			_agentBuffer.Clear();
			_agent.Runner.GetAllBehaviours(_agentBuffer);

			for (int i = 0; i < _agentBuffer.Count; ++i)
			{
				Agent other = _agentBuffer[i];
				if (other == _agent || IsEnemy(other) == false)
					continue;

				Vector3 offset   = other.transform.position - eye;
				float   distance = offset.magnitude;
				if (distance > settings.SightRange)
					continue;

				// The cone (the current target is tracked all around once seen)
				bool tracking = other == _target && now - _targetLastSeenTime < 1.0f;
				if (distance > settings.CloseAwareness && tracking == false)
				{
					Vector3 flat = new Vector3(offset.x, 0.0f, offset.z);
					if (Vector3.Angle(forward, flat) > halfCone)
						continue;
				}

				// Camouflage hides a still player beyond a few meters (phase 5b gear)
				if (other.IsCamouflaged == true && distance > settings.CamouflageRange)
					continue;

				if (CanSee(eye, other) == false)
					continue;

				float score = distance;
				if (other.Health.IsDowned == true)  { score += 40.0f; }
				if (other == _target)               { score -= 10.0f; }
				if (other == _lastAttacker && now - _lastHitTime < 3.0f) { score -= 20.0f; }

				if (score < bestScore)
				{
					bestScore = score;
					best      = other;
				}
			}

			_agentBuffer.Clear();

			if (best != null)
			{
				if (best != _target)
				{
					SetTarget(best, now);
					BotDebug.Log(_agent.Runner, $"SEE {_agent.name} sees {best.name} at {Vector3.Distance(best.transform.position, Position):F0} m");
				}
				else if (_targetVisible == false && now - _targetLastSeenTime > 1.0f)
				{
					// Back in sight after a while: react and settle again
					_targetFirstSeen = now;
					_trackStart      = now;
				}

				_targetVisible      = true;
				_targetLastSeen     = best.transform.position;
				_targetLastSeenTime = now;
				return;
			}

			_targetVisible = false;

			if (_target != null && (_target.Object == null || _target.Object.IsValid == false || _target.Health.IsAlive == false || IsEnemy(_target) == false || now - _targetLastSeenTime > settings.MemoryTime))
			{
				_target = null;
			}
		}

		private void SetTarget(Agent target, float now)
		{
			_target          = target;
			_targetFirstSeen = now;
			_trackStart      = now;
		}

		private bool IsEnemy(Agent other)
		{
			if (other == null || other.Object == null || other.Object.IsValid == false || other.Health.IsAlive == false)
				return false;
			if (GameplayMode.AreTeammates(_agent, other) == true)
				return false;
			if (other.Movement.IsDropping == true)
				return false;

			return IsIgnored(other) == false;
		}

		private bool IsIgnored(Agent other)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (BotDebug.IgnorePlayers == true && other.Object != null && other.Object.InputAuthority.IsRealPlayer == true)
				return true;
#endif
			return false;
		}

		/// <summary>Line of sight from an eye to another agent's head or chest.</summary>
		private bool CanSee(Vector3 eye, Agent other)
		{
			Vector3 feet  = other.transform.position;
			float   height = EyeHeightOf(other);

			return IsClear(eye, feet + Vector3.up * height) == true || IsClear(eye, feet + Vector3.up * (height * 0.7f)) == true;
		}

		/// <summary>Nothing solid between two points: walls, terrain, closed doors (Default layer), and bushes (their colliders are on layer 15).</summary>
		public bool IsClear(Vector3 from, Vector3 to)
		{
			Vector3 offset   = to - from;
			float   distance = offset.magnitude;
			if (distance < 0.01f)
				return true;

			Vector3      direction = offset / distance;
			PhysicsScene physics   = _agent.Runner.GetPhysicsScene();

			if (physics.Raycast(from, direction, out _, distance - 0.2f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
				return false;

			return IsBushBetween(physics, from, direction, distance) == false;
		}

		/// <summary>Bushes are solid since phase 9 (Logan): capsule colliders on the player-only layer 15 that also holds the river wall's boxes.</summary>
		private static bool IsBushBetween(PhysicsScene physics, Vector3 from, Vector3 direction, float distance)
		{
			int layer = Agent.RiverWallLayer;
			if (layer < 0)
				return false;

			int count = physics.Raycast(from, direction, _bushHits, distance - 0.2f, 1 << layer, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < count; ++i)
			{
				if (_bushHits[i].collider is CapsuleCollider)
					return true;
			}

			return false;
		}

		// FIGHTING

		private bool HasFightTarget(float now)
		{
			if (_target == null || IsEnemy(_target) == false)
				return false;
			if (_targetVisible == false && now - _targetLastSeenTime > BotSettings.Current.MemoryTime)
				return false;

			// Unarmed, it only takes on someone close enough to punch soon
			if (HasUsableGun() == false)
				return FlatDistance(Position, _target.transform.position) < 12.0f;

			return true;
		}

		private void UpdateFight(float now)
		{
			if (_target == null || IsEnemy(_target) == false)
				return;

			BotSettings  settings = BotSettings.Current;
			Vector3      position = Position;
			Vector3      targetPosition = _target.transform.position;
			float        distance = Vector3.Distance(position, targetPosition);
			BulletWeapon gun      = SelectGun(now);

			if (_targetVisible == false)
			{
				// Go and look where it was last seen
				_goal = $"searching for {_target.name}";
				MoveTo(_targetLastSeen, now, false);
				LookAt(_targetLastSeen + Vector3.up * 1.3f, false);
				TopUpMagazine(gun, now);

				if (FlatDistance(position, _targetLastSeen) < 2.0f)
				{
					_target = null;
				}
				return;
			}

			_goal = $"fighting {_target.name} at {distance:F0} m";

			Vector3 aimPoint = GetAimPoint(_target, gun, distance);
			LookAt(aimPoint, true);

			if (gun == null)
			{
				// Fists: close in and punch
				MoveTo(targetPosition, now, distance > 6.0f);
				_hasLook = true;
				if (distance < settings.PunchDistance && now >= _nextPunchTime)
				{
					_melee         = true;
					_nextPunchTime = now + 0.7f;
				}
				return;
			}

			// Aim down sights at range (snipers almost always)
			_aim = distance > settings.AdsDistance || (gun.GunData.Class == EGunClass.Sniper && distance > 12.0f);
			if (_aim == true && _wasAiming == false)
			{
				_aimStart = now;
			}
			_wasAiming = _aim;

			UpdateStrafe(now, position, targetPosition, distance, gun);

			// Fire once it has reacted, the sights are up and the aim is where it means to shoot
			bool  reacted  = now - _targetFirstSeen >= _skill.Reaction;
			bool  adsReady = _aim == false || now - _aimStart >= gun.AdsTime;
			float allowed  = Mathf.Max(settings.FireAngle, Mathf.Atan2(0.35f, Mathf.Max(1.0f, distance)) * Mathf.Rad2Deg);

			if (reacted == true && adsReady == true && _lastAimOffset <= allowed && gun.IsReloading == false && gun.MagazineAmmo > 0)
			{
				PullTrigger(gun, distance, now);
			}
		}

		private void UpdateStrafe(float now, Vector3 position, Vector3 targetPosition, float distance, BulletWeapon gun)
		{
			BotSettings settings = BotSettings.Current;

			Vector3 toTarget = targetPosition - position;
			toTarget.y = 0.0f;
			toTarget   = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : Forward;
			Vector3 side = Vector3.Cross(Vector3.up, toTarget);

			if (now >= _strafeUntil)
			{
				// Stands still now and then at long range, where moving hurts its own aim
				float roll = Random.value;
				_strafeSign  = distance > 60.0f && roll < 0.35f ? 0.0f : (roll < 0.5f ? -1.0f : 1.0f);
				_strafeUntil = now + Random.Range(settings.StrafeTime.x, settings.StrafeTime.y);

				if (_strafeSign != 0.0f && NavMesh.Raycast(position, position + side * _strafeSign * 2.5f, out _, NavMesh.AllAreas) == true)
				{
					_strafeSign = -_strafeSign;
				}
			}

			Vector3 move = side * _strafeSign;
			float preferred = GetPreferredRange(gun);

			if (distance < settings.BackOffDistance)
			{
				move -= toTarget * 0.7f;
			}
			else if (distance > preferred && _aim == false)
			{
				move += toTarget * 0.5f;
			}

			_move = move.sqrMagnitude > 1.0f ? move.normalized : move;
		}

		private static float GetPreferredRange(BulletWeapon gun)
		{
			if (gun == null || gun.GunData == null)
				return 5.0f;

			switch (gun.GunData.Class)
			{
				case EGunClass.SMG:      return 15.0f;
				case EGunClass.AR:       return 35.0f;
				case EGunClass.Tactical: return 50.0f;
				case EGunClass.Sniper:   return 90.0f;
				default:                 return 30.0f;
			}
		}

		private void PullTrigger(BulletWeapon gun, float distance, float now)
		{
			BotSettings settings = BotSettings.Current;
			GunData     data     = gun.GunData;

			if (data.FireMode == EFireMode.Auto)
			{
				if (now < _burstUntil)
				{
					_attack = true;
					return;
				}

				if (now < _nextBurstTime)
					return;

				Vector2 range = distance < 15.0f ? settings.BurstClose : (distance < 50.0f ? settings.BurstMid : settings.BurstFar);
				int     shots = Mathf.RoundToInt(Random.Range(range.x, range.y));

				_burstUntil    = now + Mathf.Max(1, shots) * data.FireInterval;
				_nextBurstTime = _burstUntil + Random.Range(settings.BurstPause.x, settings.BurstPause.y);
				_attack        = true;

				BotDebug.Log(_agent.Runner, $"FIRE {_agent.name} {data.GunName} burst of {shots} at {_target.name}, {distance:F0} m");
				return;
			}

			if (now < _nextShotTime)
				return;

			float interval = data.FireInterval;
			if (data.FireMode == EFireMode.Burst)
			{
				interval = Mathf.Max(data.BurstCycle, data.FireInterval * Mathf.Max(1, data.BurstCount));
			}

			float pacing = distance < 15.0f ? settings.ShotPacing.x : (distance < 50.0f ? settings.ShotPacing.y : settings.ShotPacing.z);

			_attack       = true;
			_nextShotTime = now + interval + pacing + Random.Range(0.0f, 0.05f);

			BotDebug.Log(_agent.Runner, $"FIRE {_agent.name} {data.GunName} {data.FireMode} at {_target.name}, {distance:F0} m");
		}

		/// <summary>Where it means to shoot: the chest (low when downed), led by the target's velocity and lifted for bullet drop.</summary>
		private Vector3 GetAimPoint(Agent target, BulletWeapon gun, float distance)
		{
			Vector3 point = target.transform.position + Vector3.up * (target.Health.IsDowned == true ? 0.3f : EyeHeightOf(target) * 0.78f);

			if (gun == null || gun.GunData == null)
				return point;

			float speed = Mathf.Max(50.0f, gun.Stats.BulletSpeed);
			float time  = distance / speed;

			Vector3 velocity = target.Character.CharacterController.FixedData.RealVelocity;
			point += new Vector3(velocity.x, 0.0f, velocity.z) * time;

			time = Vector3.Distance(Eye, point) / speed;
			point.y += 0.5f * GunplaySettings.Current.Gravity * time * time;

			return point;
		}

		/// <summary>The skill's aim error (pitch, yaw degrees): starts large on a new target and settles while it keeps tracking; drifts smoothly.</summary>
		private Vector2 GetAimError()
		{
			float now     = Now;
			float settle  = Mathf.Clamp01((now - _trackStart) / Mathf.Max(0.05f, _skill.SettleTime));
			float size    = Mathf.Lerp(_skill.AimErrorStart, _skill.AimErrorSettled, settle);

			float angle   = Mathf.PerlinNoise(_noiseSeed, now * 0.6f) * Mathf.PI * 4.0f;
			float amount  = Mathf.Lerp(0.4f, 1.0f, Mathf.PerlinNoise(_noiseSeed + 37.0f, now * 1.1f));

			return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * (size * amount);
		}

		/// <summary>The gun in hand when it has ammo; otherwise asks to swap to the other one. Null = fists.</summary>
		private BulletWeapon SelectGun(float now)
		{
			Weapons      weapons = _agent.Weapons;
			BulletWeapon current = weapons.CurrentWeapon as BulletWeapon;

			if (current != null && current.HasAmmo() == true)
				return current;

			if (now >= _nextSwapTime)
			{
				for (int slot = Weapons.FIRST_PRIMARY_SLOT; slot <= Weapons.LAST_PRIMARY_SLOT; ++slot)
				{
					if (slot != weapons.CurrentWeaponSlot && weapons.GetWeapon(slot) is BulletWeapon other && other.HasAmmo() == true)
					{
						_weapon       = (byte)(slot + 1);
						_nextSwapTime = now + 1.0f;
						break;
					}
				}
			}

			return current != null && current.HasAmmo() == true ? current : null;
		}

		private bool HasUsableGun()
		{
			Weapons weapons = _agent.Weapons;
			for (int slot = Weapons.FIRST_PRIMARY_SLOT; slot <= Weapons.LAST_PRIMARY_SLOT; ++slot)
			{
				if (weapons.GetWeapon(slot) is BulletWeapon gun && gun.HasAmmo() == true)
					return true;
			}

			return false;
		}

		/// <summary>Reloads a half-empty mag while nobody is in sight.</summary>
		private void TopUpMagazine(BulletWeapon gun, float now)
		{
			if (gun == null || gun.IsReloading == true || now < _nextReloadTime)
				return;
			if (gun.MagazineAmmo >= gun.MaxMagazineAmmo * BotSettings.Current.ReloadBelow || gun.WeaponAmmo <= 0)
				return;

			_reload         = true;
			_nextReloadTime = now + 1.0f;
		}

		private void ResetCombat()
		{
			_target             = null;
			_targetVisible      = false;
			_targetLastSeenTime = -100.0f;
			_lastAttacker       = null;
			_lastHitTime        = -100.0f;
			_wantCover          = false;
			_coverCooldownUntil = 0.0f;
			_heardTime          = -100.0f;
			_burstUntil         = 0.0f;
			_nextBurstTime      = 0.0f;
			_nextShotTime       = 0.0f;
			_wasAiming          = false;
			_lastAimOffset      = 180.0f;
			_coverArrived       = false;
		}

		private void OnPartnerHit(Agent attacker, float now)
		{
			if (_target == null || _targetVisible == false)
			{
				SetTarget(attacker, now);
				_targetLastSeen     = attacker.transform.position;
				_targetLastSeenTime = now;
			}
		}

		// COVER

		private bool WantsCover(float now)
		{
			if (_wantCover == false || _state == EBotState.Cover)
				return false;

			_wantCover = false;

			if (now - _lastHitTime > 1.0f)
				return false;

			return now >= _coverCooldownUntil || _agent.Health.CurrentHealth < BotSettings.Current.BadlyHurt;
		}

		private bool TryStartCover(float now)
		{
			Agent   threat      = _lastAttacker != null && _lastAttacker.Health.IsAlive == true ? _lastAttacker : _target;
			Vector3 threatPoint = threat != null ? threat.transform.position + Vector3.up * EyeHeightOf(threat) : _targetLastSeen + Vector3.up * 1.6f;

			if (FindCover(threatPoint, out Vector3 spot) == false)
			{
				BotDebug.Log(_agent.Runner, $"COVER {_agent.name} found no cover within {BotSettings.Current.CoverRadius:F0} m, keeps fighting");
				_coverCooldownUntil = now + 1.5f;
				return false;
			}

			_coverSpot    = spot;
			_coverThreat  = threatPoint;
			_coverArrived = false;
			_coverStart   = now;

			SetState(EBotState.Cover, now, $"taking cover {FlatDistance(Position, spot):F0} m away");
			BotDebug.Log(_agent.Runner, $"COVER {_agent.name} going to {spot:F1}, {FlatDistance(Position, spot):F1} m away, out of sight of {(threat != null ? threat.name : "the shooter")}");
			return true;
		}

		/// <summary>A spot on the mesh within CoverRadius where the threat can't see a crouched chest. Nearer is better; toward the threat is worse.</summary>
		private bool FindCover(Vector3 threatPoint, out Vector3 spot)
		{
			BotSettings settings = BotSettings.Current;
			Vector3     position = Position;
			float       bestScore = float.MaxValue;
			float       threatDistance = FlatDistance(position, threatPoint);

			spot = default;

			// Near first; a second, wider ring when nothing near hides it
			for (int i = 0; i < settings.CoverCandidates * 2; ++i)
			{
				if (i == settings.CoverCandidates && bestScore < float.MaxValue)
					break;

				float   far      = i < settings.CoverCandidates ? settings.CoverRadius : settings.CoverRadius * 2.0f;
				Vector2 random   = Random.insideUnitCircle.normalized * Random.Range(2.5f, far);
				Vector3 candidate = position + new Vector3(random.x, 0.0f, random.y);

				if (BotNavigation.Sample(candidate, 2.0f, out Vector3 onMesh) == false)
					continue;
				if (Mathf.Abs(onMesh.y - position.y) > 4.0f)
					continue;

				// Hidden when crouched (chest at about 0.65 m)
				if (IsClear(threatPoint, onMesh + Vector3.up * 0.65f) == true)
					continue;

				float score = FlatDistance(position, onMesh);

				// Better still hidden standing
				if (IsClear(threatPoint, onMesh + Vector3.up * 1.6f) == true)
				{
					score += 6.0f;
				}

				// Don't run at the shooter
				if (FlatDistance(onMesh, threatPoint) < threatDistance - 3.0f)
				{
					score += 10.0f;
				}

				if (score < bestScore)
				{
					bestScore = score;
					spot      = onMesh;
				}
			}

			return bestScore < float.MaxValue;
		}

		private void UpdateCover(float now)
		{
			BotSettings settings = BotSettings.Current;
			BulletWeapon gun     = _agent.Weapons.CurrentWeapon as BulletWeapon;

			if (_coverArrived == false)
			{
				MoveTo(_coverSpot, now, true);

				if (FlatDistance(Position, _coverSpot) < 0.9f)
				{
					_coverArrived   = true;
					_coverWaitUntil = now + Random.Range(settings.CoverWait.x, settings.CoverWait.y);
					BotDebug.Log(_agent.Runner, $"COVER {_agent.name} in cover after {now - _coverStart:F1} s, {(IsClear(_coverThreat, Position + Vector3.up * 0.65f) ? "still seen" : "out of sight")}");
				}
				return;
			}

			_crouch = true;
			LookAt(_coverThreat, false);

			// Heal and reload while hidden
			if (_agent.Health.CurrentHealth < settings.HealBelow && HasHealItem() == true)
			{
				_heal   = true;
				_crouch = false;
				_coverWaitUntil = Mathf.Max(_coverWaitUntil, now + 0.2f);
				return;
			}

			TopUpMagazine(gun, now);
		}

		private bool IsCoverDone(float now)
		{
			if (_state != EBotState.Cover)
				return true;

			// Someone right on top of it: fight
			if (_targetVisible == true && _target != null && FlatDistance(Position, _target.transform.position) < 10.0f)
				return true;

			float timeout = BotSettings.Current.CoverTimeout;
			if (_coverArrived == false)
				return now - _coverStart > timeout;

			return now >= _coverWaitUntil && _agent.Health.IsHealing == false;
		}

		private void OnLeaveCover(float now)
		{
			_coverCooldownUntil = now + BotSettings.Current.CoverCooldown;
			_coverArrived       = false;
		}

		// HEALING

		private bool WantsHeal()
		{
			if (HasHealItem() == false)
				return false;

			float health = _agent.Health.CurrentHealth;
			return _state == EBotState.Heal ? health < 140.0f : health < BotSettings.Current.HealBelow;
		}

		private bool HasHealItem()
		{
			LootStack slot = _agent.HealthSlot;
			if (slot.Kind == EItemKind.Heal && slot.IsEmpty == false)
				return true;

			for (int i = 0; i < HealthSettings.HealItemCount; ++i)
			{
				if (_agent.Inventory.CountHeal((EHealItem)i) > 0)
					return true;
			}

			return false;
		}

		private void UpdateHeal(float now)
		{
			_heal = true;
			if (_heardTime > now - 2.0f)
			{
				LookAt(_heardPoint + Vector3.up * 1.3f, false);
			}
		}
	}
}
