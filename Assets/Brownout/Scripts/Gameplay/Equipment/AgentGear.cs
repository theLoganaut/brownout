namespace TPSBR
{
	using System.Collections.Generic;
	using Fusion;
	using UnityEngine;

	/// <summary>
	/// Gear items on Left Ctrl (phase 5b, Items, Gear & Inventory page). Left Ctrl switches the item in the Gear slot on or off (predicted by
	/// the input authority, owned by the server). While it's on, the server counts its seconds down in the slot; switching off keeps what's
	/// left, and at 0 it breaks and the slot empties. The effects read IsActive:
	/// Double Jump (AgentMovement), Double Time (BREnvironmentProcessor), Ninja (AgentFootsteps), O2 Max (Weapons, BulletWeapon),
	/// Camouflage (IsCamouflaged, set here; GearVisuals draws it) and Sixth Sense (marks and blinks, here; UIMinimap draws them).
	/// </summary>
	public sealed class AgentGear
	{
		// CONSTANTS

		public const int MaxBlinkSpots = 4;

		// PRIVATE MEMBERS

		private readonly Agent       _agent;
		private readonly List<Agent> _marked = new List<Agent>(8);

		private float _stillTime;
		private int   _nextBlinkTick;

		private static readonly List<Agent> _agentBuffer = new List<Agent>(64);

		private NetworkRunner Runner => _agent.Runner;

		// CONSTRUCTORS

		public AgentGear(Agent agent)
		{
			_agent = agent;
		}

		// PUBLIC MEMBERS

		public bool IsOn => _agent.GearOn == true && _agent.GearSlot.Kind == EItemKind.Gear;

		/// <summary>Seconds left in the equipped gear item (0 when there's none).</summary>
		public float SecondsLeft => _agent.GearSlot.Kind == EItemKind.Gear ? _agent.GearSlot.SecondsLeft : 0.0f;

		/// <summary>Enemies Sixth Sense has marked (server).</summary>
		public int MarkedCount => _marked.Count;

		public bool IsActive(EGear gear)
		{
			LootStack slot = _agent.GearSlot;
			return _agent.GearOn == true && slot.Kind == EItemKind.Gear && slot.Index == (byte)gear && slot.SecondsLeft > 0.0f;
		}

		/// <summary>Any agent (null-safe).</summary>
		public static bool IsActive(Agent agent, EGear gear)
		{
			return agent != null && agent.Gear != null && agent.Object != null && agent.Object.IsValid == true && agent.Gear.IsActive(gear) == true;
		}

		/// <summary>O2 Max: recoil multiplier for this agent's gun.</summary>
		public static float GetRecoilMultiplier(Agent agent)
		{
			return IsActive(agent, EGear.O2Max) == true ? EquipmentSettings.Current.O2MaxRecoil : 1.0f;
		}

		// PUBLIC METHODS

		/// <summary>Every tick (server and the predicting input authority): Left Ctrl switches the gear item on or off.</summary>
		public void ProcessInput(GameplayInput input, AgentInput agentInput)
		{
			if (agentInput.WasActivated(EGameplayInputAction.Gear, input) == false)
				return;

			if (_agent.GearOn == true)
			{
				SwitchOff("Left Ctrl");
				return;
			}

			LootStack slot = _agent.GearSlot;
			if (slot.Kind != EItemKind.Gear || slot.SecondsLeft <= 0.0f || _agent.Health.IsAlive == false || _agent.Health.IsDowned == true)
				return;

			_agent.GearOn = true;
			EquipmentDebug.Log(Runner, $"GEAR ON {_agent.name} {EquipmentSettings.GetName(slot.GearItem)}, {slot.SecondsLeft:F2} s left");
		}

		/// <summary>Server, every tick: the timer, Camouflage's stillness and Sixth Sense's blinks.</summary>
		public void OnFixedUpdate()
		{
			if (_agent.GearOn == true && (_agent.Health.IsAlive == false || _agent.Health.IsDowned == true || _agent.GearSlot.Kind != EItemKind.Gear))
			{
				SwitchOff(_agent.GearSlot.Kind != EItemKind.Gear ? "no gear item" : "down");
			}

			if (_agent.GearOn == true)
			{
				LootStack slot = _agent.GearSlot;
				slot.SecondsLeft = Mathf.Max(0.0f, slot.SecondsLeft - Runner.DeltaTime);

				if (slot.SecondsLeft <= 0.0001f)
				{
					EquipmentDebug.Log(Runner, $"GEAR BROKE {_agent.name} {EquipmentSettings.GetName(slot.GearItem)}: its {EquipmentSettings.Current.GearSeconds:0} s are spent");
					_agent.GearSlot = default;
					SwitchOff(null);
				}
				else
				{
					_agent.GearSlot = slot;
				}
			}

			UpdateCamouflage();
			UpdateSixthSense();
		}

		/// <summary>Switches the gear item off, keeping its seconds (reason null: no log line).</summary>
		public void SwitchOff(string reason)
		{
			if (_agent.GearOn == true && reason != null)
			{
				EquipmentDebug.Log(Runner, $"GEAR OFF {_agent.name} {EquipmentSettings.GetName(_agent.GearSlot.GearItem)} ({reason}), {SecondsLeft:F2} s left");
			}

			_agent.GearOn = false;
			_marked.Clear();
			_nextBlinkTick = 0;
		}

		/// <summary>
		/// Server: a gun fired an unsuppressed shot. Every enemy of the shooter with Sixth Sense on marks them; their spot then blinks on
		/// that player's minimap every SixthSenseInterval until the gear item is off or spent.
		/// </summary>
		public static void OnShotFired(NetworkObject shooterObject)
		{
			Agent shooter = shooterObject != null ? shooterObject.GetComponent<Agent>() : null;
			if (shooter == null || shooter.Runner == null || shooter.Runner.IsServer == false)
				return;

			shooter.Runner.GetAllBehaviours(_agentBuffer);

			for (int i = 0; i < _agentBuffer.Count; ++i)
			{
				Agent observer = _agentBuffer[i];
				if (observer == shooter || observer.Gear == null || observer.Gear.IsActive(EGear.SixthSense) == false)
					continue;
				if (GameplayMode.AreTeammates(observer, shooter) == true)
					continue;

				observer.Gear.Mark(shooter);
			}

			_agentBuffer.Clear();
		}

		// PRIVATE METHODS

		private void Mark(Agent shooter)
		{
			if (_marked.Contains(shooter) == true)
				return;

			_marked.Add(shooter);

			if (_nextBlinkTick == 0)
			{
				_nextBlinkTick = Runner.Tick;
			}

			EquipmentDebug.Log(Runner, $"MARK {_agent.name} Sixth Sense marked {shooter.name} at {shooter.transform.position.ToString("F1")}");
		}

		private void UpdateSixthSense()
		{
			if (IsActive(EGear.SixthSense) == false)
			{
				if (_marked.Count > 0)
				{
					_marked.Clear();
					_nextBlinkTick = 0;
				}

				return;
			}

			if (_marked.Count == 0 || _nextBlinkTick == 0 || Runner.Tick < _nextBlinkTick)
				return;

			_nextBlinkTick = Runner.Tick + Mathf.Max(1, Mathf.RoundToInt(EquipmentSettings.Current.SixthSenseInterval / Runner.DeltaTime));

			Vector3[] spots = new Vector3[MaxBlinkSpots];
			int       count = 0;

			for (int i = _marked.Count - 1; i >= 0; --i)
			{
				Agent marked = _marked[i];
				if (marked == null || marked.Object == null || marked.Object.IsValid == false || marked.Health.IsAlive == false)
				{
					_marked.RemoveAt(i);
					continue;
				}

				if (count < MaxBlinkSpots)
				{
					spots[count++] = marked.transform.position;
				}
			}

			if (count == 0)
				return;

			_agent.SendSixthSenseBlink(spots[0], spots[1], spots[2], spots[3], count);
			EquipmentDebug.Log(Runner, $"MARK {_agent.name} Sixth Sense blink: {count} spot(s), first at {spots[0].ToString("F1")}");
		}

		private void UpdateCamouflage()
		{
			EquipmentSettings settings = EquipmentSettings.Current;

			if (IsActive(EGear.Camouflage) == false)
			{
				_stillTime = 0.0f;
				_agent.IsCamouflaged = false;
				return;
			}

			float speed = _agent.Character.CharacterController.FixedData.RealSpeed;
			_stillTime = speed < settings.CamouflageStillSpeed ? _stillTime + Runner.DeltaTime : 0.0f;

			bool camouflaged = _stillTime >= settings.CamouflageStillTime;
			if (camouflaged != _agent.IsCamouflaged)
			{
				_agent.IsCamouflaged = camouflaged;
				EquipmentDebug.Log(Runner, $"GEAR {_agent.name} Camouflage {(camouflaged ? "see-through (standing still)" : "visible (moving)")}");
			}
		}
	}
}
