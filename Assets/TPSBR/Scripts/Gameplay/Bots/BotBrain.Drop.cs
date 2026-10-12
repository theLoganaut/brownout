using Fusion.Addons.KCC;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Phase 11f (Logan, 11 Oct): a bot's drop from the Osprey. At the jump it plans how long to dive (a dive builds speed that leveling
	/// out turns into carry, AgentMovement.UpdateDrop) so it reaches its spot, then glides at it holding W, slowing down or diving when it
	/// would still glide past it. A lone player's bot follows its player down instead. Landing hands over to the normal brain, which stays
	/// a while in the area it came down in.
	/// </summary>
	public sealed partial class BotBrain
	{
		// PUBLIC MEMBERS

		/// <summary>Flying to a spot from the Osprey (until it lands).</summary>
		public bool    HasDropTarget => _dropActive;
		public Vector3 DropTarget    => _dropTarget;
		/// <summary>Seconds it planned to dive after the jump.</summary>
		public float   DropDiveTime  => _dropDiveTime;

		// PRIVATE MEMBERS

		private const float DROP_DIVE_PITCH = 80.0f;
		private const float DROP_PLAN_STEP  = 0.1f;
		private const float DROP_PLAN_MAX   = 4.0f;

		private bool    _dropActive;
		private bool    _dropPlanned;
		private Vector3 _dropTarget;
		private Agent   _dropFollow;
		private float   _dropStart;
		private float   _dropDiveTime;

		// PUBLIC METHODS

		/// <summary>Server, right after the bot jumps: the spot it flies to, or (follow set) the agent it follows down.</summary>
		public void SetDrop(Vector3 target, Agent follow)
		{
			_dropActive   = true;
			_dropPlanned  = false;
			_dropTarget   = target;
			_dropFollow   = follow;
			_dropStart    = Now;
			_dropDiveTime = 0.0f;
		}

		// PRIVATE METHODS

		private GameplayInput TickDrop()
		{
			GameplayInput input = default;

			if (_dropActive == false)
				return input;   // A drop without a spot (a test): it just glides ahead

			KCCData data = _agent.Character.CharacterController.FixedData;
			if (data.MoveMode != EMoveMode.Wingsuit)
				return input;   // Wings off: falling the last meters

			MatchSettings m        = MatchSettings.Current;
			BotSettings   settings = BotSettings.Current;
			float         now      = Now;
			Vector3       position = Position;
			Vector3       target   = GetDropGoal();
			float         distance = FlatDistance(position, target);
			float         height   = position.y - GroundHeight(target) - m.WingsOffHeight;

			if (_dropPlanned == false)
			{
				_dropPlanned  = true;
				_dropStart    = now;
				_dropDiveTime = _dropFollow != null ? 0.0f : PlanDive(height, distance, m, settings);

				BotDebug.Log(_agent.Runner, $"DROP {_agent.name} {(_dropFollow != null ? $"following {_dropFollow.name}" : $"to {target:F0}")}, {distance:F0} m away, {height:F0} m up, diving {_dropDiveTime:F1} s first");
			}

			bool  dive    = now - _dropStart < _dropDiveTime;
			float forward = 1.0f;

			// Once leveled out: coming down on the spot rather than past it
			if (dive == false && now - _dropStart > _dropDiveTime + 1.0f)
			{
				float reach = EstimateDropReach(height, -data.DropVelocity.y, data.DropCarry, m);
				if (reach > distance + settings.DropOvershoot)
				{
					if (distance < height * 0.6f)
					{
						dive = true;
					}
					else
					{
						forward = -1.0f;
					}
				}
			}

			Vector3 to      = target - position;
			Vector2 current = data.GetLookRotation(true, true);
			float   yaw     = new Vector2(to.x, to.z).sqrMagnitude > 1.0f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : current.y;
			float   pitch   = dive == true ? DROP_DIVE_PITCH : 0.0f;

			Vector2 delta   = new Vector2(Mathf.DeltaAngle(current.x, pitch), Mathf.DeltaAngle(current.y, yaw));
			float   maxStep = settings.DropTurnSpeed * _agent.Runner.DeltaTime;
			if (delta.magnitude > maxStep)
			{
				delta = delta.normalized * maxStep;
			}

			input.LookRotationDelta = delta;
			input.MoveDirection     = new Vector2(0.0f, forward);
			return input;
		}

		/// <summary>The first tick on the ground after a drop: the area it came down in becomes its home for a while.</summary>
		private void OnDropLanded()
		{
			_dropActive = false;
			_dropFollow = null;

			SetHome(Position);
			_nav.Clear();

			BotDebug.Log(_agent.Runner, $"LANDED {_agent.name} at {Position:F0}, {FlatDistance(Position, _dropTarget):F0} m from its spot");
		}

		private Vector3 GetDropGoal()
		{
			Agent follow = _dropFollow;
			if (follow != null && follow.Object != null && follow.Object.IsValid == true && follow.Health.IsAlive == true)
				return PositionOf(follow);

			return _dropTarget;
		}

		/// <summary>
		/// How long to dive first: the shortest dive (in steps of a few tenths) whose straight flight reaches the spot, else the dive that
		/// gets furthest. Flies the same rule as AgentMovement.UpdateDrop holding W, with the look turning at DropTurnSpeed.
		/// </summary>
		private static float PlanDive(float height, float distance, MatchSettings m, BotSettings settings)
		{
			float bestReach = PredictDropReach(height, 0.0f, m, settings);
			float bestTime  = 0.0f;

			if (bestReach >= distance)
				return 0.0f;

			for (float time = 0.5f; time <= DROP_PLAN_MAX; time += 0.5f)
			{
				float reach = PredictDropReach(height, time, m, settings);
				if (reach >= distance)
					return time;

				if (reach > bestReach)
				{
					bestReach = reach;
					bestTime  = time;
				}
			}

			return bestTime;
		}

		private static float PredictDropReach(float height, float diveTime, MatchSettings m, BotSettings settings)
		{
			float dt      = DROP_PLAN_STEP;
			float along   = 0.0f;
			float fall    = m.JumpOutSpeed;
			float carry   = 0.0f;
			float pitch   = 0.0f;
			float reach   = 0.0f;
			float time    = 0.0f;

			for (int i = 0; i < 2000 && height > 0.0f; ++i, time += dt)
			{
				bool dive = time < diveTime;

				float step = settings.DropTurnSpeed * dt;
				pitch = Mathf.MoveTowards(pitch, dive == true ? DROP_DIVE_PITCH : 0.0f, step);

				float share  = Mathf.Clamp01(pitch / Mathf.Max(1.0f, m.DivePitch));
				float speed  = Mathf.Lerp(m.GlideSpeed, m.DiveSpeed, share) * (1.0f + m.FastShare);
				float sink   = Mathf.Lerp(m.GlideSink, m.DiveSink, share) * (1.0f - m.CarryLift * Mathf.Clamp01(carry / Mathf.Max(0.01f, m.CarryLiftAt)));
				bool  diving = share >= 0.5f;

				if (fall > sink)
				{
					float pulled = Mathf.Max(sink, fall - m.PullOutRate * dt);
					if (diving == false)
					{
						carry += (fall - pulled) * m.CarryShare;
					}
					fall = pulled;
				}
				else
				{
					fall = Mathf.MoveTowards(fall, sink, m.DropAcceleration * dt);
				}

				along  = Mathf.MoveTowards(along, speed + carry, m.DropAcceleration * dt);
				carry  = Mathf.Clamp(carry - (diving == true ? m.CarryDiveDecay : m.CarryDecay) * dt, 0.0f, m.CarryMax);
				reach  += along * dt;
				height -= fall * dt;
			}

			return reach;
		}

		/// <summary>Roughly how far it would still glide holding W from here (pulling out now if falling fast), for the approach.</summary>
		private static float EstimateDropReach(float height, float fall, float carry, MatchSettings m)
		{
			float glide = m.GlideSpeed * (1.0f + m.FastShare);
			float sink0 = m.GlideSink;

			carry = Mathf.Min(m.CarryMax, carry + m.CarryShare * Mathf.Max(0.0f, fall - sink0));

			float pullTime = Mathf.Max(0.0f, (fall - sink0) / Mathf.Max(0.01f, m.PullOutRate));
			float reach    = glide * pullTime;
			height        -= fall > sink0 ? (fall + sink0) * 0.5f * pullTime : 0.0f;

			const float dt = 0.5f;
			for (int i = 0; i < 200 && height > 0.0f; ++i)
			{
				float sink = sink0 * (1.0f - m.CarryLift * Mathf.Clamp01(carry / Mathf.Max(0.01f, m.CarryLiftAt)));
				reach  += (glide + carry) * dt;
				height -= sink * dt;
				carry   = Mathf.Max(0.0f, carry - m.CarryDecay * dt);
			}

			return reach;
		}
	}
}
