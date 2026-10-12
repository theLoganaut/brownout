using System.Collections.Generic;
using Fusion;
using Fusion.Addons.KCC;

namespace TPSBR
{
	using UnityEngine;

	/// <summary>
	/// Healing on Q and reviving on F (phase 4), both holds. Runs every tick on the server and the predicting input authority,
	/// from Agent.ProcessFixedInput: ProcessInput before the movement update, ProcessAfterMovement after it.
	/// A downed player's inputs are stripped here to crawling only.
	/// </summary>
	public sealed partial class Health
	{
		// PUBLIC MEMBERS

		public bool      IsHealing        => _healStartTick > 0;
		public bool      IsReviving       => _reviveStartTick > 0;
		/// <summary>Downed, healing, reviving, or throwing or grappling (phase 5b): the gun can't be used.</summary>
		public bool      IsHandsBusy      => _downed == true || IsHealing == true || IsReviving == true || (_agent != null && _agent.EquipmentActions != null && _agent.EquipmentActions.IsBusy == true);
		/// <summary>The healing item in the Health slot (what Q uses); a bandage when the slot is empty.</summary>
		public EHealItem EquippedHealItem => _agent != null && _agent.HealthSlot.Kind == EItemKind.Heal ? (EHealItem)_agent.HealthSlot.Index : EHealItem.Bandage;
		public EHealItem HealingItem      => (EHealItem)_healingItem;
		public Health    ReviveTarget     => _reviveTarget != null ? _reviveTarget.GetBehaviour<Health>() : null;

		/// <summary>0..1 through the current heal.</summary>
		public float HealProgress
		{
			get
			{
				if (IsHealing == false)
					return 0.0f;

				float useTime = HealthSettings.Current.GetHealItem(HealingItem).UseTime;
				return Mathf.Clamp01((Runner.Tick - _healStartTick) * Runner.DeltaTime / Mathf.Max(0.01f, useTime));
			}
		}

		/// <summary>0..1 through the current revive (on the reviver).</summary>
		public float ReviveProgress => IsReviving == true ? Mathf.Clamp01((Runner.Tick - _reviveStartTick) * Runner.DeltaTime / Mathf.Max(0.01f, HealthSettings.Current.ReviveTime)) : 0.0f;

		// PRIVATE MEMBERS

		[Networked]
		private int _healStartTick { get; set; }
		[Networked]
		private byte _healingItem { get; set; }
		[Networked]
		private NetworkObject _reviveTarget { get; set; }
		[Networked]
		private int _reviveStartTick { get; set; }

		private static readonly List<Agent> _agentBuffer = new List<Agent>(64);

		// PUBLIC METHODS

		/// <summary>Healing items of this kind carried (the Health slot and the inventory, phase 5a).</summary>
		public int GetHealItemCount(EHealItem item)
		{
			return _agent != null && _agent.Inventory != null ? _agent.Inventory.CountHeal(item) : 0;
		}

		/// <summary>
		/// Before the movement update: strips a downed player's inputs to crawling, then runs the revive and the heal start and cancels.
		/// </summary>
		public void ProcessInput(ref GameplayInput input, AgentInput agentInput)
		{
			if (IsAlive == false || _agent == null)
				return;

			if (_downed == true)
			{
				if (IsHealing == true || IsReviving == true)
				{
					ResetActions();
				}

				// Crawl only. Held still while a teammate revives you
				StripActions(ref input, IsBeingRevived == false);
				input.Interact = false;
				return;
			}

			HealthSettings settings = HealthSettings.Current;

			UpdateRevive(ref input, settings);
			UpdateHealInput(ref input, settings);
		}

		/// <summary>
		/// After the movement update: a heal ends in any movement but standing, walking or crouching on the ground, and lands when its time is up.
		/// </summary>
		public void ProcessAfterMovement()
		{
			if (IsHealing == false || _agent == null)
				return;

			KCCData data = _agent.Character.CharacterController.FixedData;
			if ((data.MoveMode != EMoveMode.Walk && data.MoveMode != EMoveMode.Crouch) || data.IsGrounded == false)
			{
				CancelHeal(data.IsGrounded == false ? "in the air" : data.MoveMode.ToString());
				return;
			}

			HealthSettings.HealItem item = HealthSettings.Current.GetHealItem(HealingItem);
			if (Runner.Tick < _healStartTick + SecondsToTicks(item.UseTime))
				return;

			float before = CurrentHealth;
			SetHealth(Mathf.Max(CurrentHealth, Mathf.Min(item.Cap, CurrentHealth + item.Amount)));

			// One from the Health slot; the next healing stack moves in when it runs out (phase 5a)
			_agent.Inventory.UseHealthSlotItem();

			HealthDebug.Log(Runner, $"HEAL {name} {item.Name} done after {(Runner.Tick - _healStartTick) * Runner.DeltaTime:F3} s: health {before:F1} -> {CurrentHealth:F1}, {GetHealItemCount(HealingItem)} left");

			_healStartTick = 0;
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>Test tool (server): HealthSettings' healing kit again (first kind in the Health slot, the rest in the inventory).</summary>
		public void DebugRefillHealItems()
		{
			if (HasStateAuthority == true)
			{
				_agent.Inventory.GiveHealKit();
			}
		}

		/// <summary>Test tool (server): a full stack of this item in the Health slot, so Q uses it.</summary>
		public void DebugEquipHealItem(EHealItem item)
		{
			if (HasStateAuthority == true)
			{
				_agent.Inventory.DebugSetHealthSlot(item);
			}
		}
#endif

		// PRIVATE METHODS

		private void InitActions()
		{
			// The healing items themselves are in the inventory (phase 5a), set up by Inventory.InitForSpawn
			ResetActions();
		}

		private void ResetActions()
		{
			_healStartTick   = 0;
			_reviveStartTick = 0;
			_reviveTarget    = null;
		}

		private void UpdateRevive(ref GameplayInput input, HealthSettings settings)
		{
			int tick = Runner.Tick;

			if (IsReviving == true)
			{
				Health target = ReviveTarget;
				string cancel = null;

				if (input.Interact == false)
				{
					cancel = "let go";
				}
				else if (target == null || target.IsDowned == false)
				{
					cancel = "teammate not downed";
				}
				else if (IsInReviveReach(target, settings.ReviveReach + 0.5f) == false)
				{
					cancel = "out of reach";
				}

				if (cancel != null)
				{
					HealthDebug.Log(Runner, $"REVIVE {name} cancelled ({cancel}) after {(tick - _reviveStartTick) * Runner.DeltaTime:F2} s");
					_reviveStartTick = 0;
					_reviveTarget    = null;
					return;
				}

				// The reviver stands still with the gun down; looking around is fine
				StripActions(ref input, false);
				target.MarkBeingRevived();

				if (tick >= _reviveStartTick + SecondsToTicks(settings.ReviveTime))
				{
					HealthDebug.Log(Runner, $"REVIVE {name} -> {target.name} done after {(tick - _reviveStartTick) * Runner.DeltaTime:F3} s, bleed-out left {target.BleedOutLeft:F2} s");
					target.Revive();

					_reviveStartTick = 0;
					_reviveTarget    = null;
				}

				return;
			}

			if (input.Interact == false || _agent.EquipmentActions.IsBusy == true || FindReviveTarget(settings, out Health found) == false)
				return;

			if (IsHealing == true)
			{
				CancelHeal("revive");
			}

			_reviveTarget    = found.Object;
			_reviveStartTick = tick;
			_agent.Weapons.CancelGunActions();

			StripActions(ref input, false);
			found.MarkBeingRevived();

			HealthDebug.Log(Runner, $"REVIVE {name} -> {found.name} start, bleed-out left {found.BleedOutLeft:F2} s");
		}

		private void UpdateHealInput(ref GameplayInput input, HealthSettings settings)
		{
			bool hasMove = input.MoveDirection.sqrMagnitude > 0.01f;

			if (IsHealing == true)
			{
				string cancel = null;

				if (input.Heal == false)                    { cancel = "let go"; }
				else if (input.Attack == true)              { cancel = "fire";   }
				else if (input.Aim == true)                 { cancel = "aim";    }
				else if (input.Reload == true)              { cancel = "reload"; }
				else if (input.Weapon > 0)                  { cancel = "swap";   }
				else if (input.Melee == true)               { cancel = "punch";  }
				else if (input.Jump == true)                { cancel = "jump";   }
				else if (input.Sprint == true && hasMove)   { cancel = "sprint"; }
				else if (IsReviving == true)                { cancel = "revive"; }

				if (cancel != null)
				{
					CancelHeal(cancel);
				}

				return;
			}

			if (input.Heal == false || IsReviving == true)
				return;
			if (input.Attack == true || input.Aim == true || input.Reload == true || input.Weapon > 0 || input.Melee == true || input.Jump == true || (input.Sprint == true && hasMove == true))
				return;

			Weapons weapons = _agent.Weapons;
			if (weapons.IsSwitchingWeapon() == true || weapons.IsDrawing == true || weapons.IsPunching == true || _agent.EquipmentActions.IsBusy == true)
				return;

			KCCData data = _agent.Character.CharacterController.FixedData;
			if ((data.MoveMode != EMoveMode.Walk && data.MoveMode != EMoveMode.Crouch) || data.IsGrounded == false)
				return;

			if (TryGetUsableItem(settings, out EHealItem item) == false)
				return;

			_healingItem   = (byte)item;
			_healStartTick = Runner.Tick;
			weapons.CancelGunActions();

			HealthDebug.Log(Runner, $"HEAL {name} {settings.GetHealItem(item).Name} start at health {CurrentHealth:F1}");
		}

		/// <summary>The item in the Health slot (phase 5a: Q uses only that). It has to be able to heal.</summary>
		private bool TryGetUsableItem(HealthSettings settings, out EHealItem item)
		{
			LootStack slot = _agent.HealthSlot;
			item = EquippedHealItem;

			return slot.Kind == EItemKind.Heal && slot.Count > 0 && CurrentHealth < settings.GetHealItem(item).Cap - 0.01f;
		}

		private void CancelHeal(string reason)
		{
			HealthDebug.Log(Runner, $"HEAL {name} {HealthSettings.Current.GetHealItem(HealingItem).Name} cancelled ({reason}) after {(Runner.Tick - _healStartTick) * Runner.DeltaTime:F2} s, nothing used");
			_healStartTick = 0;
		}

		/// <summary>A downed teammate in reach, closest to where you're looking.</summary>
		private bool FindReviveTarget(HealthSettings settings, out Health target)
		{
			target = null;

			TransformData eye       = _agent.Character.GetCameraTransform(false);
			Vector3       look      = eye.Rotation * Vector3.forward;
			float         bestAngle = settings.ReviveLookAngle;

			Runner.GetAllBehaviours(_agentBuffer);

			for (int i = 0; i < _agentBuffer.Count; ++i)
			{
				Agent other = _agentBuffer[i];
				if (other == _agent || other.Object == null || other.Object.IsValid == false)
					continue;
				if (GameplayMode.AreTeammates(_agent, other) == false)
					continue;

				Health health = other.Health;
				if (health.IsDowned == false || IsInReviveReach(health, settings.ReviveReach) == false)
					continue;

				float angle = Vector3.Angle(look, other.transform.position + Vector3.up * 0.3f - eye.Position);
				if (angle <= bestAngle)
				{
					bestAngle = angle;
					target    = health;
				}
			}

			_agentBuffer.Clear();
			return target != null;
		}

		private bool IsInReviveReach(Health target, float reach)
		{
			Vector3 offset = target.transform.position - transform.position;
			return Mathf.Abs(offset.y) < 1.5f && offset.OnlyXZ().sqrMagnitude <= reach * reach;
		}

		private static void StripActions(ref GameplayInput input, bool keepMove)
		{
			input.Jump   = false;
			input.Sprint = false;
			input.Crouch = false;
			input.Aim    = false;
			input.Attack = false;
			input.Reload = false;
			input.Melee  = false;
			input.Heal   = false;
			input.Weapon = 0;

			if (keepMove == false)
			{
				input.MoveDirection = default;
			}
		}

		private int SecondsToTicks(float seconds)
		{
			return Mathf.CeilToInt(seconds / Runner.DeltaTime - 0.0001f);
		}
	}
}
