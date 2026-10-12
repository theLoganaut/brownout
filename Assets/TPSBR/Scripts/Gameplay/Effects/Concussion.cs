using UnityEngine;
using Fusion;

namespace TPSBR
{
	/// <summary>
	/// The concussion grenade's blast (phase 5b), on ConcussionExplosion.prefab (Brownout → Setup → Build Equipment Prefabs, a copy of the
	/// flash's). Every agent with a clear line to it, within EquipmentSettings' shared falloff, gets dizzy for up to ConcussionMaxDuration
	/// (shorter with distance) and slowed (AgentSenses.GetConcussionSlow). The screen effect is ConcussionScreen. It also hits the thrower
	/// and teammates, since it does no damage.
	/// </summary>
	public sealed class Concussion : ContextBehaviour
	{
		// PRIVATE MEMBERS

		[SerializeField]
		private float      _despawnDelay = 1.0f;
		[SerializeField]
		private GameObject _effectRoot;

		private TickTimer  _despawnTimer;

		// PUBLIC METHODS

		/// <summary>Editor script: the effect root copied from the flash.</summary>
		public void SetEffectRoot(GameObject effectRoot)
		{
			_effectRoot = effectRoot;
		}

		// NetworkBehaviour INTERFACE

		public override void Spawned()
		{
			base.Spawned();

			if (Runner.Mode != SimulationModes.Server && _effectRoot != null)
			{
				_effectRoot.SetActive(true);
			}

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

		// MONOBEHAVIOUR

		private void OnEnable()
		{
			if (_effectRoot != null)
			{
				_effectRoot.SetActive(false);
			}
		}

		// PRIVATE METHODS

		private void Explode()
		{
			if (HasStateAuthority == false)
				return;

			EquipmentSettings settings = EquipmentSettings.Current;

			var position = transform.position + Vector3.up * 0.3f;
			var agents   = ListPool.Get<Agent>(64);
			int affected = 0;

			Runner.GetAllBehaviours(agents);

			for (int i = 0; i < agents.Count; i++)
			{
				Agent agent = agents[i];
				if (agent == null || agent.Object == null || agent.Object.IsValid == false || agent.Health.IsAlive == false)
					continue;

				float distance = Vector3.Distance(Explosion.GetClosestBodyPoint(agent.transform.position, position), position);
				float falloff  = settings.GetFalloff(distance);
				if (falloff <= 0.0f)
					continue;

				Vector3 chest = agent.transform.position + Vector3.up * 1.0f;
				Vector3 head  = agent.transform.position + Vector3.up * 1.6f;
				if (IsBlocked(position, chest) == true && IsBlocked(position, head) == true)
				{
					EquipmentDebug.Log(Runner, $"DETONATE concussion {agent.name} at {distance:F2} m behind a wall: 0");
					continue;
				}

				float duration = settings.ConcussionMaxDuration * falloff;
				agent.Senses.SetConcussion(falloff, duration);
				++affected;

				EquipmentDebug.Log(Runner, $"DETONATE concussion {agent.name} at {distance:F2} m: dizzy {falloff:F2} for {duration:F2} s, slowed to {Mathf.Lerp(1.0f, settings.ConcussionSlow, falloff) * 100.0f:F0}%");
			}

			EquipmentDebug.Log(Runner, $"DETONATE concussion ({Object.InputAuthority}) at {transform.position.ToString("F2")}, {affected} concussed");

			ListPool.Return(agents);

			_despawnTimer = TickTimer.CreateFromSeconds(Runner, _despawnDelay);
		}

		private bool IsBlocked(Vector3 from, Vector3 to)
		{
			Vector3 path     = to - from;
			float   distance = path.magnitude;
			return distance > 0.001f && Runner.GetPhysicsScene().Raycast(from, path / distance, distance, ObjectLayerMask.Default) == true;
		}
	}
}
