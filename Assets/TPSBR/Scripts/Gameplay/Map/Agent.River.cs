using Fusion.Addons.KCC;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// The river is a barrier (phase 8, Map & POIs): an invisible wall on the RiverWall layer along both banks that the player's capsule
	/// collides with and bullets, grenades and the grapple ray don't (their masks leave the layer out). Players in the wingsuit or under
	/// the chute pass through it, and anyone who still ends up standing in the channel is put on the nearer bank by the server.
	/// </summary>
	public sealed partial class Agent
	{
		// PRIVATE MEMBERS

		private static int _riverWallLayer = int.MinValue;

		private BrownoutMap _brownoutMap;
		private bool        _brownoutMapSearched;

		// PUBLIC MEMBERS

		public static int RiverWallLayer
		{
			get
			{
				if (_riverWallLayer == int.MinValue)
				{
					_riverWallLayer = LayerMask.NameToLayer("RiverWall");
				}

				return _riverWallLayer;
			}
		}

		// PRIVATE METHODS

		/// <summary>Every simulated tick, on every peer: the KCC collides with the river wall, except during the drop.</summary>
		private void UpdateRiverWall()
		{
			int layer = RiverWallLayer;
			if (layer < 0)
				return;

			KCC kcc = _character.CharacterController;
			int bit = 1 << layer;

			if ((kcc.Settings.CollisionLayerMask & bit) == 0)
			{
				kcc.SetCollisionLayerMask(kcc.Settings.CollisionLayerMask | bit);
			}

			if (kcc.ResolveCollision == null)
			{
				kcc.ResolveCollision = ResolveDropCollision;
			}
		}

		/// <summary>
		/// During the wingsuit and chute the capsule passes through the river wall and other players (two players jumping a moment apart
		/// used to land on each other's heads 250 m up, phase 8).
		/// </summary>
		private static bool ResolveDropCollision(KCC kcc, Collider collider)
		{
			int layer = collider.gameObject.layer;
			if (layer != RiverWallLayer && layer != ObjectLayer.AgentKCC)
				return true;

			return AgentMovement.IsDropMode(kcc.Data.MoveMode) == false;
		}

		/// <summary>Server: anyone standing in the river channel (a drop that landed in it) goes up the nearer bank.</summary>
		private void UpdateRiverBank()
		{
			if (_health.IsAlive == false)
				return;

			if (_brownoutMapSearched == false)
			{
				_brownoutMap         = BrownoutMap.Get(gameObject.scene);
				_brownoutMapSearched = true;
			}

			if (_brownoutMap == null)
				return;

			KCC kcc = _character.CharacterController;
			if (kcc.FixedData.IsGrounded == false || AgentMovement.IsDropMode(kcc.FixedData.MoveMode) == true)
				return;

			if (_brownoutMap.TryGetBankPoint(transform.position, BrownoutMapSettings.Current.BankPutDistance, out Vector3 bank) == false)
				return;

			// Only when standing on the ground itself (a bridge deck over the water is fine)
			PhysicsScene physics = Runner.GetPhysicsScene();
			if (physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit under, 1.5f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true && under.collider is TerrainCollider == false)
				return;

			if (physics.Raycast(bank + Vector3.up * 30.0f, Vector3.down, out RaycastHit hit, 60.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
			{
				bank = hit.point;
			}

			bank += Vector3.up * 0.05f;

			kcc.FixedData.BasePosition    = bank;
			kcc.FixedData.DesiredPosition = bank;
			kcc.FixedData.TargetPosition  = bank;
			kcc.SetPosition(bank, true, false, false);

			MatchDebug.Log(Runner, $"RIVER {name} was standing in the river at {transform.position.ToString("F0")}, put on the bank at {bank.ToString("F0")}");
		}
	}
}
