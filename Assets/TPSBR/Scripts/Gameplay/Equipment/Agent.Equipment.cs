namespace TPSBR
{
	using Fusion;
	using UnityEngine;

	/// <summary>
	/// Equipment (E) and gear (Left Ctrl) state (phase 5b), on the agent so the agent prefabs stay untouched. The equipment action and the
	/// gear switch are predicted by the input authority and owned by the server (AgentEquipment, AgentGear); the slots themselves are in
	/// Agent.Inventory.cs and change only on the server.
	/// </summary>
	public sealed partial class Agent
	{
		// PUBLIC MEMBERS

		public AgentEquipment EquipmentActions => _equipmentActions;
		public AgentGear      Gear             => _gear;

		/// <summary>The equipment being used: EEquipment + 1, 0 = none.</summary>
		[Networked, HideInInspector]
		public byte        EquipKindValue        { get; set; }
		/// <summary>Tick E was pressed (a frag's pin comes out here).</summary>
		[Networked, HideInInspector]
		public int         EquipStartTick        { get; set; }
		/// <summary>Tick the grenade left the hand or the grapple fired, 0 = not yet.</summary>
		[Networked, HideInInspector]
		public int         EquipReleaseTick      { get; set; }
		/// <summary>Tick the gun is ready again, 0 = not known yet.</summary>
		[Networked, HideInInspector]
		public int         EquipReadyTick        { get; set; }
		/// <summary>Grapple: tick the hook reaches the wall, 0 = no hook (not fired yet, or a miss).</summary>
		[Networked, HideInInspector]
		public int         GrappleHookTick       { get; set; }
		/// <summary>Grapple: where the hook hit.</summary>
		[Networked, HideInInspector]
		public Vector3     GrapplePoint          { get; set; }
		/// <summary>Grapple: tick the pull started, 0 = not yet.</summary>
		[Networked, HideInInspector]
		public int         GrapplePullStartTick  { get; set; }

		/// <summary>The gear item in the Gear slot is switched on.</summary>
		[Networked, HideInInspector]
		public NetworkBool GearOn                { get; set; }
		/// <summary>Camouflage is on and the player is standing still: other players see through them (set by the server).</summary>
		[Networked, HideInInspector]
		public NetworkBool IsCamouflaged         { get; set; }

		/// <summary>Sixth Sense (local, from the server's RPC): the last blink's spots and when it arrived (Time.time).</summary>
		public readonly Vector3[] SixthSenseSpots = new Vector3[AgentGear.MaxBlinkSpots];
		public int                SixthSenseSpotCount { get; private set; }
		public float              SixthSenseBlinkTime { get; private set; } = -100.0f;

		// PRIVATE MEMBERS

		private AgentEquipment _equipmentActions;
		private AgentGear      _gear;

		// PUBLIC METHODS

		/// <summary>Server: Sixth Sense blink, sent only to this player.</summary>
		public void SendSixthSenseBlink(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int count)
		{
			RPC_SixthSenseBlink(a, b, c, d, (byte)Mathf.Clamp(count, 0, AgentGear.MaxBlinkSpots));
		}

		// RPCs

		[Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority, Channel = RpcChannel.Unreliable)]
		private void RPC_SixthSenseBlink(Vector3 a, Vector3 b, Vector3 c, Vector3 d, byte count)
		{
			SixthSenseSpots[0] = a;
			SixthSenseSpots[1] = b;
			SixthSenseSpots[2] = c;
			SixthSenseSpots[3] = d;

			SixthSenseSpotCount = count;
			SixthSenseBlinkTime = Time.time;
		}
	}
}
