using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// A bot's looting (phase 9): it starts empty-handed like a player and goes for the nearest item it wants within LootRadius: a gun
	/// while a gun slot is free, ammo for its guns, better armor, healing items. Attachments, equipment, gear and backpacks are skipped
	/// for now. Duo partners claim different items. At the item it uses the inventory's own rules and reach: F (Inventory.Equip) for guns
	/// and armor, G (Inventory.Store) for ammo and healing.
	/// </summary>
	public sealed partial class BotBrain
	{
		// PRIVATE MEMBERS

		private static readonly List<LootItem>                  _items       = new List<LootItem>(1024);
		private static readonly Dictionary<NetworkId, BotBrain> _claims      = new Dictionary<NetworkId, BotBrain>(64);
		private static NetworkRunner _itemsRunner;
		private static float         _itemsTime = -100.0f;

		private readonly Dictionary<NetworkId, float> _skipUntil = new Dictionary<NetworkId, float>(64);
		private readonly UnityEngine.AI.NavMeshPath   _reachPath = new UnityEngine.AI.NavMeshPath();

		private LootItem  _lootTarget;
		private NetworkId _lootTargetId;
		private float     _lootStart;

		// PRIVATE METHODS

		/// <summary>True while there's an item worth going for (picks one when it has none).</summary>
		private bool ThinkLoot(float now)
		{
			if (_lootTarget != null)
			{
				if (IsValidItem(_lootTarget) == true && _lootTarget.Object.Id == _lootTargetId && Wants(_lootTarget.Stack) == true && now - _lootStart < BotSettings.Current.LootGiveUp)
					return true;

				AbandonLootTarget(now - _lootStart >= BotSettings.Current.LootGiveUp ? "gave up" : "no longer wanted");
			}

			LootItem item = FindItem(now);
			if (item == null)
				return false;

			// Items on top of racks, vehicles and machines need a jump or mantle, which bots don't do: skip what can't be walked to
			if (BotNavigation.CanReach(Position, item.transform.position, _reachPath, 1.0f) == false)
			{
				_skipUntil[item.Object.Id] = now + 120.0f;
				return false;
			}

			_lootTarget   = item;
			_lootTargetId = item.Object.Id;
			_lootStart    = now;
			_claims[_lootTargetId] = this;
			_nav.Clear();
			return true;
		}

		private void UpdateLoot(float now)
		{
			if (IsValidItem(_lootTarget) == false || _lootTarget.Object.Id != _lootTargetId)
			{
				AbandonLootTarget("gone");
				return;
			}

			Vector3 position = Position;
			Vector3 itemPosition = _lootTarget.transform.position;
			float   flat   = FlatDistance(position, itemPosition);
			float   height = itemPosition.y - position.y;
			float   reach  = Mathf.Max(1.0f, LootSettings.Current.PickupReach - 0.8f);

			_goal = $"getting {ItemCatalog.GetLabel(_lootTarget.Stack)} {flat:F0} m away";

			if (flat < reach && height > -1.8f && height < 1.6f)
			{
				TakeItem(now);
				return;
			}

			if (flat < 5.0f)
			{
				LookAt(itemPosition, false);
			}

			// The last few meters straight at it (the path's end can snap to the mesh beside the item)
			if (flat < 3.0f)
			{
				Vector3 toItem = itemPosition - position;
				toItem.y = 0.0f;
				_move = toItem.normalized;
				return;
			}

			MoveTo(itemPosition, now, flat > BotSettings.Current.SprintDistance);
		}

		private void TakeItem(float now)
		{
			LootItem  item  = _lootTarget;
			LootStack stack = item.Stack;
			bool      taken;

			switch (stack.Kind)
			{
				case EItemKind.Gun:
				case EItemKind.Armor:
					taken = _agent.Inventory.Equip(item);
					break;
				default:
					taken = _agent.Inventory.Store(item);
					break;
			}

			BotDebug.Log(_agent.Runner, $"LOOT {_agent.name} {(taken ? "took" : "couldn't take")} {stack} after {now - _lootStart:F1} s");

			// Done with this one either way (what's left of a stack can be picked again later)
			_skipUntil[_lootTargetId] = now + (taken == true ? 3.0f : 30.0f);
			ReleaseClaims();
			_lootTarget = null;
			_nav.Clear();
		}

		private LootItem FindItem(float now)
		{
			RefreshItems(now);

			BotSettings settings = BotSettings.Current;
			Vector3     position = Position;
			float       radius   = settings.LootRadius;
			LootItem    best     = null;
			float       bestScore = float.MaxValue;

			BotBrain leader = FindPartner();
			Vector3? leaderPosition = leader != null && IsDuoLeader() == false && leader._agent.Health.IsAlive == true ? leader.Position : (Vector3?)null;

			for (int i = 0; i < _items.Count; ++i)
			{
				LootItem item = _items[i];
				if (IsValidItem(item) == false)
					continue;

				Vector3 offset = item.transform.position - position;
				if (Mathf.Abs(offset.x) > radius || Mathf.Abs(offset.z) > radius)
					continue;

				float flat = new Vector2(offset.x, offset.z).magnitude;
				if (flat > radius)
					continue;

				NetworkId id = item.Object.Id;
				if (_skipUntil.TryGetValue(id, out float until) == true && now < until)
					continue;
				if (_claims.TryGetValue(id, out BotBrain owner) == true && owner != this && owner._lootTarget == item)
					continue;
				if (Wants(item.Stack) == false)
					continue;

				// A duo's second bot only loots near its leader
				if (leaderPosition.HasValue == true && FlatDistance(item.transform.position, leaderPosition.Value) > settings.FollowDistance.y * 1.5f)
					continue;

				// Up or down a floor counts extra
				float score = flat + Mathf.Abs(offset.y) * 3.0f;
				if (score < bestScore)
				{
					bestScore = score;
					best      = item;
				}
			}

			return best;
		}

		/// <summary>What it still needs.</summary>
		private bool Wants(LootStack stack)
		{
			if (stack.IsEmpty == true)
				return false;

			Weapons weapons = _agent.Weapons;

			switch (stack.Kind)
			{
				case EItemKind.Gun:
					return weapons.GetEmptyPrimarySlot() >= 0;

				case EItemKind.Ammo:
				{
					ECaliber caliber = (ECaliber)stack.Index;
					for (int slot = Weapons.FIRST_PRIMARY_SLOT; slot <= Weapons.LAST_PRIMARY_SLOT; ++slot)
					{
						if (weapons.GetWeapon(slot) is BulletWeapon gun && gun.GunData != null && gun.GunData.Caliber == caliber)
							return weapons.GetAmmo(caliber) < GunplaySettings.Current.GetCarryCap(caliber) * BotSettings.Current.WantAmmoBelow;
					}
					return false;
				}

				case EItemKind.Armor:
				{
					Health health = _agent.Health;
					return stack.Index > health.ArmorTier || (stack.Index == health.ArmorTier && stack.Armor > health.ArmorLeft + 50.0f);
				}

				case EItemKind.Heal:
				{
					int heals = 0;
					for (int i = 0; i < HealthSettings.HealItemCount; ++i)
					{
						heals += _agent.Inventory.CountHeal((EHealItem)i);
					}
					return heals < BotSettings.Current.WantHeals;
				}

				default:
					return false;
			}
		}

		private void AbandonLootTarget(string reason)
		{
			if (_lootTarget == null)
				return;

			BotDebug.Log(_agent.Runner, $"LOOT {_agent.name} left {(IsValidItem(_lootTarget) ? _lootTarget.Stack.ToString() : "an item")}: {reason}");

			_skipUntil[_lootTargetId] = Now + 30.0f;
			ReleaseClaims();
			_lootTarget = null;
			_nav.Clear();
		}

		private void ReleaseClaims()
		{
			if (_lootTarget != null && _claims.TryGetValue(_lootTargetId, out BotBrain owner) == true && owner == this)
			{
				_claims.Remove(_lootTargetId);
			}
		}

		private void ResetLoot()
		{
			ReleaseClaims();
			_lootTarget = null;
			_skipUntil.Clear();
		}

		private void RefreshItems(float now)
		{
			if (_itemsRunner == _agent.Runner && now - _itemsTime < 1.0f && now >= _itemsTime)
				return;

			_itemsRunner = _agent.Runner;
			_itemsTime   = now;
			_items.Clear();
			_agent.Runner.GetAllBehaviours(_items);
		}

		private static bool IsValidItem(LootItem item)
		{
			return item != null && item.Object != null && item.Object.IsValid == true && item.Stack.IsEmpty == false;
		}
	}
}
