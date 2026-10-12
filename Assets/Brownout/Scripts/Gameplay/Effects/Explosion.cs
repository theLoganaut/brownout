using Fusion;

namespace TPSBR
{
	using UnityEngine;

	public class Explosion : ContextBehaviour
	{
		// PRIVATE MEMBERS

		[SerializeField]
		private LayerMask  _hitMask;
		[SerializeField]
		private EHitType   _hitType;

		[SerializeField]
		private float      _innerRadius;
		[SerializeField]
		private float      _outerRadius;

		[SerializeField]
		private float      _innerHitValue;
		[SerializeField]
		private float      _outerHitValue;

		[SerializeField]
		private bool       _useBodyPartMultipliers;

		[SerializeField]
		private float      _despawnDelay;

		[SerializeField]
		private Transform  _effectRoot;


		private TickTimer  _despawnTimer;

		// NetworkBehaviour INTERFACE

		public override void Spawned()
		{
			base.Spawned();

			ShowEffect();
			Explode();
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false)
				return;
			if (_despawnTimer.Expired(Runner) == false)
				return;

			Runner.Despawn(Object);
		}

		// PRIVATE METHODS

		private void Explode()
		{
			if (HasStateAuthority == false)
				return;

			// Brownout frag (phase 5b): numbers from EquipmentSettings, and it hurts the thrower too
			if (_hitType == EHitType.Grenade)
			{
				ExplodeFrag();
				return;
			}

			var hits = ListPool.Get<LagCompensatedHit>(16);
			var hitRoots = ListPool.Get<int>(16);

			var position = transform.position + Vector3.up * 0.5f; // Check explosion slightly above

			int count = Runner.LagCompensation.OverlapSphere(position, _outerRadius, Object.InputAuthority, hits, _hitMask);
			bool damageFalloff = _innerRadius < _outerRadius && _innerHitValue != _outerHitValue;

			var player = Context.NetworkGame.GetPlayer(Object.InputAuthority);
			var owner = player != null ? player.ActiveAgent : null;

			for (int i = 0; i < count; i++)
			{
				var hit = hits[i];

				if (hit.Hitbox == null)
					continue;

				var hitTarget = hit.Hitbox.Root.GetComponent<IHitTarget>();

				if (hitTarget == null)
					continue;

				int hitRootID = hit.Hitbox.Root.GetInstanceID();
				if (hitRoots.Contains(hitRootID) == true)
					continue; // Same object was hit multiple times

				// TODO: Replace this when detailed hit info will be fixed
				var direction = hit.GameObject.transform.position - position;
				float distance = direction.magnitude;
				direction /= distance; // Normalize

				if (Runner.GetPhysicsScene().Raycast(position, direction, distance, ObjectLayerMask.Default) == true)
					continue;

				hitRoots.Add(hitRootID);

				float damage = _innerHitValue;

				if (damageFalloff == true && distance > _innerRadius)
				{
					damage = MathUtility.Map(_innerRadius, _outerRadius, _innerHitValue, _outerHitValue, distance);
				}

				// TODO: Remove this when detailed hit info will be fixed
				hit.Point = hit.GameObject.transform.position;
				hit.Normal = -direction;

				if (owner != null)
				{
					HitUtility.ProcessHit(owner.Object, direction, hit, damage, _hitType, out HitData hitData);
				}
				else
				{
					HitUtility.ProcessHit(Object.InputAuthority, direction, hit, damage, _hitType, out HitData hitData);
				}
			}

			ListPool.Return(hitRoots);
			ListPool.Return(hits);

			_despawnTimer = TickTimer.CreateFromSeconds(Runner, _despawnDelay);
		}

		/// <summary>
		/// Phase 5b frag: FragDamage within FullEffectRadius, down to EdgeEffect at MaxRadius, nothing beyond or behind a wall. Distance is
		/// to the nearest point of the target's body (feet to head). The thrower is hurt too; teammates are safe (Health's no friendly fire);
		/// armor cuts it as a body hit (Health.ApplyArmor). No body-part multipliers.
		/// </summary>
		private void ExplodeFrag()
		{
			EquipmentSettings settings = EquipmentSettings.Current;

			var hits    = ListPool.Get<LagCompensatedHit>(32);
			var targets = ListPool.Get<Health>(16);

			Vector3 position = transform.position + Vector3.up * 0.2f; // The height of the body line's bottom, so distance on flat ground is the ground distance

			Runner.LagCompensation.OverlapSphere(position, settings.MaxRadius + 1.5f, Object.InputAuthority, hits, _hitMask);

			var    player     = Context.NetworkGame.GetPlayer(Object.InputAuthority);
			Agent  owner      = player != null ? player.ActiveAgent : null;
			string ownerName  = owner != null ? owner.name : Object.InputAuthority.ToString();

			for (int i = 0; i < hits.Count; i++)
			{
				LagCompensatedHit hit = hits[i];
				if (hit.Hitbox == null || hit.Hitbox.Root == null)
					continue;

				Health target = hit.Hitbox.Root.GetComponent<Health>();
				if (target == null || targets.Contains(target) == true)
					continue;

				targets.Add(target);

				Vector3 closest  = GetClosestBodyPoint(target.transform.position, position);
				Vector3 offset   = closest - position;
				float   distance = offset.magnitude;
				float   falloff  = settings.GetFalloff(distance);

				if (falloff <= 0.0f)
					continue;

				// Behind a wall: check to the chest and to the head, either clear line counts
				if (IsBlocked(position, target.transform.position + Vector3.up * 1.0f) == true && IsBlocked(position, target.transform.position + Vector3.up * 1.6f) == true)
				{
					EquipmentDebug.Log(Runner, $"DETONATE frag ({ownerName}) {target.name} at {distance:F2} m behind a wall: 0");
					continue;
				}

				Vector3 direction = distance > 0.001f ? offset / distance : Vector3.up;
				float   damage    = settings.FragDamage * falloff;
				float   before    = target.CurrentHealth;

				var hitData = new HitData
				{
					Action        = EHitAction.Damage,
					Amount        = damage,
					Position      = closest,
					Normal        = -direction,
					Direction     = direction,
					Target        = target,
					InstigatorRef = Object.InputAuthority,
					Instigator    = owner != null ? owner.Health : null,
					HitType       = EHitType.Grenade,
				};

				HitUtility.ProcessHit(ref hitData);

				EquipmentDebug.Log(Runner, $"DETONATE frag ({ownerName}) {target.name} at {distance:F2} m: {damage:F1} damage, health {before:F1} -> {target.CurrentHealth:F1}");
			}

			EquipmentDebug.Log(Runner, $"DETONATE frag ({ownerName}) at {transform.position.ToString("F2")}, {targets.Count} in reach");

			ListPool.Return(targets);
			ListPool.Return(hits);

			_despawnTimer = TickTimer.CreateFromSeconds(Runner, _despawnDelay);
		}

		/// <summary>The nearest point on a standing body (a line from 0.2 m to 1.6 m above the feet) to a position.</summary>
		public static Vector3 GetClosestBodyPoint(Vector3 feet, Vector3 position)
		{
			Vector3 bottom = feet + Vector3.up * 0.2f;
			Vector3 top    = feet + Vector3.up * 1.6f;
			Vector3 axis   = top - bottom;
			float   t      = Mathf.Clamp01(Vector3.Dot(position - bottom, axis) / axis.sqrMagnitude);
			return bottom + axis * t;
		}

		private bool IsBlocked(Vector3 from, Vector3 to)
		{
			Vector3 path = to - from;
			float   distance = path.magnitude;
			return distance > 0.001f && Runner.GetPhysicsScene().Raycast(from, path / distance, distance, ObjectLayerMask.Default) == true;
		}

		private void ShowEffect()
		{
			if (Runner.Mode == SimulationModes.Server)
				return;

			if (_effectRoot != null)
			{
				_effectRoot.SetActive(true);
				_effectRoot.localScale = Vector3.one * _outerRadius * 2f;
			}
		}
	}
}
