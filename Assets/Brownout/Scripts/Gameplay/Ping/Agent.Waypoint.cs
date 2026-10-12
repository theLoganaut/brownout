namespace TPSBR
{
	using Fusion;
	using UnityEngine;

	/// <summary>
	/// Map waypoint (phase 6b): a left click on the full map places it, a left click on your own waypoint removes it. One per player; it
	/// stays until removed or you die. The server puts it on the ground under the clicked spot; the HUD shows it on your compass, minimap,
	/// full map and in the world, and on your teammate's (BrownoutHud), like the ping.
	/// </summary>
	public sealed partial class Agent
	{
		// PUBLIC MEMBERS

		[Networked, HideInInspector]
		public Vector3     WaypointPoint { get; set; }
		[Networked, HideInInspector]
		public NetworkBool HasWaypoint   { get; set; }

		// PUBLIC METHODS

		/// <summary>From the full map (the local player): places the waypoint at a map point (x, z), or removes it.</summary>
		public void RequestWaypoint(Vector2 point, bool place)
		{
			if (Object == null || Object.IsValid == false)
				return;

			if (HasStateAuthority == true)
			{
				SetWaypoint(point, place);
			}
			else if (HasInputAuthority == true)
			{
				RPC_SetWaypoint(point, place);
			}
		}

		// PRIVATE METHODS

		/// <summary>Server, every tick: no waypoint for the dead.</summary>
		private void ProcessWaypoint()
		{
			if (HasStateAuthority == true && HasWaypoint == true && _health.IsAlive == false)
			{
				HasWaypoint = false;
			}
		}

		private void SetWaypoint(Vector2 point, bool place)
		{
			if (place == false || _health.IsAlive == false)
			{
				HasWaypoint = false;
				return;
			}

			// On the ground under the click (or at the player's height over nothing)
			Vector3 top    = new Vector3(point.x, 2000.0f, point.y);
			Vector3 ground = new Vector3(point.x, transform.position.y, point.y);
			if (Runner.GetPhysicsScene().Raycast(top, Vector3.down, out RaycastHit hit, 4000.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
			{
				ground = hit.point;
			}

			WaypointPoint = ground;
			HasWaypoint   = true;
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
		private void RPC_SetWaypoint(Vector2 point, NetworkBool place)
		{
			SetWaypoint(point, place);
		}
	}
}
