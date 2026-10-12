using UnityEngine;
using Fusion;

namespace TPSBR
{
	public class Flashbang : ContextBehaviour
	{
		// PRIVATE MEMBERS

		// Phase 5b: the radii, strengths and lengths come from EquipmentSettings now; the sample's fields stay so the prefab is untouched
#pragma warning disable 0414

		[SerializeField]
		private float      _innerRadius = 10f;
		[SerializeField]
		private float      _outerRadius = 25f;

		[SerializeField]
		private float      _innerFlashValue = 1f;
		[SerializeField]
		private float      _outerFlashValue = 0.1f;
		[SerializeField]
		private float      _maxFlashDuration = 4f;
		[SerializeField]
		private float      _minFlashDuration = 0.5f;
		[SerializeField]
		private float      _flashFalloffDelayMultiplier = 0.8f;
		[SerializeField]
		private float      _maxFlashDot = 0.7f;
		[SerializeField]
		private float      _minFlashDot = -0.5f;
		[SerializeField]
		private float      _minDotMultiplier = 0.3f;
		[SerializeField]
		private bool       _affectsOwner = true;

#pragma warning restore 0414
		[SerializeField]
		private float      _despawnDelay = 3f;

		[SerializeField]
		private GameObject _effectRoot;

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

		// MONOBEHAVIOUR

		protected void OnEnable()
		{
			_effectRoot.SetActive(false);
		}

		// PRIVATE METHODS

		/// <summary>
		/// Brownout (phase 5b): every agent (players and dummies) with a clear line from the flash to its eyes, within EquipmentSettings'
		/// shared falloff (full within 6 m, 25% at 10 m, nothing beyond). Facing away scales it down (the sample's rule, down to
		/// FlashFacingAwayMultiplier). It flashes the thrower and teammates too, since it does no damage.
		/// </summary>
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

				Vector3 eyes             = agent.transform.position + Vector3.up * MovementSettings.Current.StandEyeHeight;
				Vector3 directionToAgent = eyes - position;
				float   distance         = Vector3.Distance(Explosion.GetClosestBodyPoint(agent.transform.position, position), position);
				float   falloff          = settings.GetFalloff(distance);

				if (falloff <= 0.0f)
					continue;

				float eyeDistance = directionToAgent.magnitude;
				directionToAgent /= Mathf.Max(0.001f, eyeDistance);

				if (Context.Runner.GetPhysicsScene().Raycast(position, directionToAgent, eyeDistance, ObjectLayerMask.Default) == true)
				{
					EquipmentDebug.Log(Runner, $"DETONATE flash {agent.name} at {distance:F2} m behind a wall: 0");
					continue;
				}

				float dot    = Vector3.Dot(agent.Character.CharacterController.Data.LookDirection, -directionToAgent);
				float facing = MathUtility.Map(_minFlashDot, _maxFlashDot, settings.FlashFacingAwayMultiplier, 1f, dot);

				float flash    = Mathf.Clamp01(falloff * facing);
				float duration = settings.FlashMaxDuration * falloff * facing;

				agent.Senses.SetEyesFlash(flash, duration, duration * _flashFalloffDelayMultiplier);
				++affected;

				EquipmentDebug.Log(Runner, $"DETONATE flash {agent.name} at {distance:F2} m, facing {facing:F2}: whiteout {flash:F2} for {duration:F2} s");
			}

			EquipmentDebug.Log(Runner, $"DETONATE flash ({Object.InputAuthority}) at {transform.position.ToString("F2")}, {affected} flashed");

			ListPool.Return(agents);

			_despawnTimer = TickTimer.CreateFromSeconds(Runner, _despawnDelay);
		}

		private void ShowEffect()
		{
			if (Runner.Mode == SimulationModes.Server)
				return;

			_effectRoot.SetActive(true);
		}
	}
}
