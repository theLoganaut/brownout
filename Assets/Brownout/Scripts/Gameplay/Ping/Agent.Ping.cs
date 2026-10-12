namespace TPSBR
{
	using Fusion;
	using UnityEngine;

	/// <summary>
	/// Ping (phase 6a): middle mouse puts a marker where you look, up to HudSettings.PingRange away. One per player, a new one replaces
	/// the old, it lasts HudSettings.PingTime, and middle mouse on your own ping removes it. Set by the server from the input; the HUD shows
	/// it in the world, on the compass and on the minimap for you and your teammate only (BrownoutHud).
	/// </summary>
	public sealed partial class Agent
	{
		// PUBLIC MEMBERS

		/// <summary>Where the ping is.</summary>
		[Networked, HideInInspector]
		public Vector3 PingPoint { get; set; }
		/// <summary>Tick the ping was placed, 0 = none.</summary>
		[Networked, HideInInspector]
		public int     PingTick  { get; set; }

		/// <summary>The ping is placed and hasn't run out.</summary>
		public bool HasPing => PingTick > 0 && Runner != null && (Runner.Tick - PingTick) * Runner.DeltaTime < HudSettings.Current.PingTime;

		// PRIVATE METHODS

		/// <summary>Server, every tick: middle mouse places, moves or removes the ping.</summary>
		private void ProcessPing(GameplayInput input)
		{
			if (HasStateAuthority == false)
				return;

			if (PingTick > 0 && HasPing == false)
			{
				PingTick = 0;
			}

			if (_health.IsAlive == false || _agentInput.WasActivated(EGameplayInputAction.Ping, input) == false)
				return;

			HudSettings   settings  = HudSettings.Current;
			TransformData eye       = _character.GetCameraTransform(false);
			Vector3       direction = eye.Rotation * Vector3.forward;

			// Pointing at your own ping removes it
			if (HasPing == true && Vector3.Angle(direction, PingPoint - eye.Position) < settings.PingRemoveAngle)
			{
				PingTick = 0;
				return;
			}

			if (Runner.GetPhysicsScene().Raycast(eye.Position, direction, out RaycastHit hit, settings.PingRange, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == false)
				return;

			PingPoint = hit.point;
			PingTick  = Runner.Tick;
		}
	}
}
