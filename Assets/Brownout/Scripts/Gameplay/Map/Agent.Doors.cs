using Fusion.Addons.KCC;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Doors (phase 8b, Map & POIs): walking into a closed door makes the server swing it open away from you, the way you're moving.
	/// An open door isn't pushed again; F closes one (Interactions). Power doors stay shut while their POI has no power.
	/// </summary>
	public sealed partial class Agent
	{
		// PRIVATE MEMBERS

		private static readonly float[] DoorProbeHeights = { 1.0f, 0.4f };

		private MapState _mapState;
		private bool     _mapStateSearched;

		// PUBLIC METHODS

		/// <summary>The map's networked state (doors and power) in this agent's scene, or null on maps without one.</summary>
		public MapState GetMapState()
		{
			if (_mapStateSearched == false || (_mapState == null && Runner != null && Runner.Tick % 32 == 0))
			{
				_mapState         = MapState.Get(gameObject.scene);
				_mapStateSearched = true;
			}

			return _mapState;
		}

		// PRIVATE METHODS

		/// <summary>Server, every tick: a door right in front, the way you're pushing, opens away from you.</summary>
		private void UpdateDoors()
		{
			if (_health.IsAlive == false)
				return;

			MapState state = GetMapState();
			if (state == null)
				return;

			KCC     kcc  = _character.CharacterController;
			KCCData data = kcc.FixedData;
			if (AgentMovement.IsDropMode(data.MoveMode) == true || data.MoveMode == EMoveMode.Mantle)
				return;

			Vector3 push = data.InputDirection.OnlyXZ();
			if (push.sqrMagnitude < 0.04f)
				return;

			push.Normalize();

			PhysicsScene physics = Runner.GetPhysicsScene();
			float        reach   = kcc.Settings.Radius + BrownoutMapSettings.Current.DoorPushReach;
			Vector3      feet    = data.TargetPosition;

			for (int i = 0; i < DoorProbeHeights.Length; ++i)
			{
				if (physics.Raycast(feet + Vector3.up * DoorProbeHeights[i], push, out RaycastHit hit, reach, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == false)
					continue;

				Door door = hit.collider.GetComponentInParent<Door>();
				if (door != null)
				{
					state.OpenDoor(door, feet, name);
				}

				return;
			}
		}
	}
}
