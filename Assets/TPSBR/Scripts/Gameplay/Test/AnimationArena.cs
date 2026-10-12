namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.InputSystem;
	using Fusion.Addons.KCC;

	public enum EArenaPad
	{
		Gun,
		NoGun,
		Heal,
		Grenade,
		Down,
		GetUp,
		Drop,
		Gear, // Phase 10 step 4: each step on it shows the next look (none, armor T1, T2, T3, T3 + backpack)
		Items,   // Phase 11a HUD Gallery: every item on the ground in rows ahead (outline, hover icon, prompts)
		FullKit, // Phase 11a: two guns, ammo, armor, heals, backpack, a grenade and a gear item (the inventory and HUD)
		Scope,   // Phase 11a: each step gives the next scoped gun (Icarus 2x, Icarus 4x, Swivel, Twofer, Omicron 4x); targets at 25/100/200 m
		Recoil,  // Phase 11e: each step gives the next gun with full ammo and turns you to face the 25 m board
	}

	/// <summary>
	/// Phase 10 Animation Arena (editor and development builds): pads that set up a move when you step on them, so the animations
	/// can be tried by hand. Placed by Brownout → Setup → Build Animation Arena. The pads act on the server copy of whoever steps on
	/// them (either player). F7 cycles the camera: behind you, in front of you, first person (Character.DebugView).
	/// In Multipeer every peer loads the scene, so one copy handles each frame.
	/// </summary>
	public sealed class AnimationArena : MonoBehaviour
	{
		// PUBLIC MEMBERS

		[System.Serializable]
		public struct Pad
		{
			public EArenaPad Action;
			public Vector3   Center;
			public Vector2   Size;
		}

		public Pad[] Pads;

		[Tooltip("The Drop pad puts you this high above the pad, in the wingsuit.")]
		public float DropHeight = 400.0f;
		[Tooltip("The Drop pad's start speed forward (m/s), like leaving the helicopter.")]
		public float DropForwardSpeed = 40.0f;
		[Tooltip("The Heal pad takes you down to this health, so healing has something to do.")]
		public float HealToHealth = 40.0f;
		[Tooltip("The gun the Gun pad gives (GunplaySettings test guns, by gun name).")]
		public string GunName = "Icarus";
		[Tooltip("The Items pad lays every item out this far ahead (m), this far apart (m).")]
		public float ItemsDistance = 4.0f;
		public float ItemsSpacing  = 1.6f;
		[Tooltip("The Recoil pad turns you to face this point (the 25 m board's center).")]
		public Vector3 RecoilTarget = new Vector3(-6.0f, 2.4f, -25.0f);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		// PRIVATE MEMBERS

		private static int _handledFrame = -1;
		private int        _scopeStep;
		private int        _recoilStep;

		private readonly HashSet<long> _onPad     = new HashSet<long>();
		private readonly HashSet<long> _stillOnPad = new HashSet<long>();

		// MonoBehaviour INTERFACE

		private void Update()
		{
			if (_handledFrame == Time.frameCount)
				return;

			_handledFrame = Time.frameCount;

			Keyboard keyboard = Keyboard.current;
			if (keyboard != null && keyboard.f7Key.wasPressedThisFrame == true)
			{
				Character.DebugView = (Character.EDebugView)(((int)Character.DebugView + 1) % 3);
				Debug.Log($"Animation Arena: camera {Character.DebugView}.");
			}

			if (Pads == null)
				return;

			_stillOnPad.Clear();

			foreach (Agent agent in FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object == null || agent.Object.IsValid == false || agent.HasStateAuthority == false)
					continue;

				Vector3 position = agent.transform.position;

				for (int i = 0; i < Pads.Length; ++i)
				{
					Pad pad = Pads[i];
					if (Mathf.Abs(position.x - pad.Center.x) > pad.Size.x * 0.5f || Mathf.Abs(position.z - pad.Center.z) > pad.Size.y * 0.5f || Mathf.Abs(position.y - pad.Center.y) > 1.0f)
						continue;

					long key = ((long)agent.Object.Id.Raw << 8) | (long)i;
					_stillOnPad.Add(key);

					// Only on stepping onto the pad
					if (_onPad.Contains(key) == false)
					{
						Debug.Log($"Animation Arena: {agent.name} {pad.Action}: {Use(agent, pad)}");
					}
				}
			}

			_onPad.Clear();
			_onPad.UnionWith(_stillOnPad);
		}

		// PRIVATE METHODS

		private string Use(Agent agent, Pad pad)
		{
			Health health = agent.Health;

			switch (pad.Action)
			{
				case EArenaPad.Gun:
				{
					foreach (Weapon weapon in GunplaySettings.Current.TestGuns)
					{
						if (weapon is BulletWeapon gun && gun.GunData != null && gun.GunData.GunName == GunName)
							return agent.Weapons.DebugGiveWeapon(gun) == true ? $"gave {GunName}." : "couldn't give the gun.";
					}
					return $"no test gun named {GunName}.";
				}
				case EArenaPad.NoGun:
				{
					for (int slot = 0; slot < 8; ++slot)
					{
						agent.Weapons.TakeGun(slot);
					}
					return "guns taken away.";
				}
				case EArenaPad.Heal:
				{
					if (health.IsUp == false)
						return "not up.";

					health.DebugRefillHealItems();
					if (health.CurrentHealth > HealToHealth)
					{
						Hit(agent, health.CurrentHealth - HealToHealth);
					}
					return $"health {health.CurrentHealth:F0}, healing items refilled (Q heals, H changes the item).";
				}
				case EArenaPad.Grenade:
				{
					agent.Inventory.DebugGive(LootStack.EquipmentItem(EEquipment.Frag), true);
					return "frag in the Equipment slot (E throws).";
				}
				case EArenaPad.Down:
				{
					if (health.IsUp == false)
						return "not up.";

					Hit(agent, health.CurrentHealth + 1.0f);
					return health.IsDowned == true ? "downed (crawl onto Get Up, or have the other player revive with F)." : "died (the other player has to be up and on your team).";
				}
				case EArenaPad.GetUp:
				{
					if (health.IsDowned == false)
						return "not downed.";

					health.Revive();
					return "revived.";
				}
				case EArenaPad.Drop:
				{
					KCC        kcc     = agent.Character.CharacterController;
					Quaternion facing  = Quaternion.Euler(0.0f, kcc.FixedData.LookYaw, 0.0f);
					Vector3    target  = agent.transform.position + Vector3.up * DropHeight;

					MoveTo(kcc, agent, target);
					agent.Movement.StartDrop(facing * Vector3.forward * DropForwardSpeed + Vector3.down * MatchSettings.Current.JumpOutSpeed);
					return $"dropping from {DropHeight:F0} m (wingsuit, the wings pop off 6 m up, then the landing roll).";
				}
				case EArenaPad.Gear:
				{
					// none → T1 → T2 → T3 → T3 + backpack → none
					int  tier     = health.ArmorTier;
					bool backpack = agent.HasBackpack;

					if (backpack == true)          { tier = 0; backpack = false; }
					else if (tier == 3)            { backpack = true; }
					else                           { tier += 1; }

					health.DebugSetArmor(tier);
					agent.HasBackpack = backpack;
					return $"armor tier {tier}{(backpack ? ", backpack" : "")} (F7 for the front view).";
				}
				case EArenaPad.Items:
				{
					var items = new List<LootStack>();
					ItemCatalog.GetAllItems(items);
					LootSpawner.ClearGround(agent.Runner);
					int count = LootSpawner.LayOut(agent, items, ItemsDistance, ItemsSpacing, false);
					return $"{count} items laid out ahead (look at one for its icon and prompt; G / F pick up).";
				}
				case EArenaPad.FullKit:
				{
					GunplaySettings gunplay = GunplaySettings.Current;
					GiveKit(agent, "Icarus", "2x", "Alibi DMR");
					agent.Inventory.DebugGive(LootStack.EquipmentItem(EEquipment.Frag), true);
					agent.Inventory.DebugGive(LootStack.Gear(EGear.SixthSense), true);
					agent.Inventory.DebugGive(LootStack.Attachment(gunplay.GetAttachmentID(gunplay.FindAttachment("Suppressor"))), false);
					agent.Inventory.DebugGive(LootStack.Attachment(gunplay.GetAttachmentID(gunplay.FindAttachment("Reflex"))), false);
					return "full kit (Tab: drag items between slots, onto the gun or out to drop).";
				}
				case EArenaPad.Scope:
				{
					string[,] scopes = { { "Icarus", "2x" }, { "Icarus", "4x" }, { "Swivel", null }, { "Twofer", null }, { "Omicron", "4x" } };
					int step = _scopeStep % scopes.GetLength(0);
					_scopeStep += 1;
					GiveKit(agent, scopes[step, 0], scopes[step, 1], null);
					return $"{scopes[step, 0]}{(scopes[step, 1] != null ? " with " + scopes[step, 1] : " (built-in scope)")}: aim at the boards (25 / 100 / 200 m). Step on again for the next.";
				}
				case EArenaPad.Recoil:
				{
					Weapon[] guns = GunplaySettings.Current.TestGuns;
					if (guns == null || guns.Length == 0)
						return "no test guns (run Brownout → Data → Import Guns).";

					int          index = _recoilStep % guns.Length;
					BulletWeapon gun   = guns[index] as BulletWeapon;
					_recoilStep += 1;
					if (gun == null || gun.GunData == null)
						return "not a Brownout gun.";

					GiveKit(agent, gun.GunData.GunName, null, null);
					Face(agent, RecoilTarget);
					return $"{gun.GunData.GunName} ({gun.GunData.Class}, {gun.GunData.FireMode}): aim at the 25 m board and fire a full mag. Step off and on for the next ({index + 1} of {guns.Length}).";
				}
			}

			return "unknown pad.";
		}

		/// <summary>A gun (with an optic) in slot 1, another in slot 2, full ammo, tier 3 armor, the healing kit and a backpack.</summary>
		private static void GiveKit(Agent agent, string gunName, string optic, string secondGun)
		{
			Weapons         weapons = agent.Weapons;
			GunplaySettings gunplay = GunplaySettings.Current;

			for (int slot = Weapons.FIRST_PRIMARY_SLOT; slot <= Weapons.LAST_PRIMARY_SLOT; ++slot)
			{
				weapons.TakeGun(slot);
			}

			int  first = ItemCatalog.GetGunIndex(gunName);
			long key   = optic != null ? (long)gunplay.GetAttachmentID(gunplay.FindAttachment(optic)) << (8 * (int)EAttachmentSlot.Optic) : 0;
			int  other = secondGun != null ? ItemCatalog.GetGunIndex(secondGun) : -1;

			if (other >= 0) { weapons.AddGunFromLoot(LootStack.Gun(other, ItemCatalog.GetGunData(other).Magazine, 0), Weapons.LAST_PRIMARY_SLOT, false); }
			if (first >= 0) { weapons.AddGunFromLoot(LootStack.Gun(first, ItemCatalog.GetGunData(first).Magazine, key), Weapons.FIRST_PRIMARY_SLOT, false); }

			for (int i = 0; i < GunData.CaliberCount; ++i)
			{
				weapons.AddAmmo((ECaliber)i, gunplay.GetCarryCap((ECaliber)i));
			}

			agent.Health.SetArmor(3, HealthSettings.Current.GetArmorTier(3).Durability);
			agent.HasBackpack = true;
			agent.Inventory.GiveHealKit();
		}

		/// <summary>Turns every copy of this player (one per peer in Multipeer) to look at a point.</summary>
		private static void Face(Agent agent, Vector3 point)
		{
			foreach (Agent copy in FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (copy.Object == null || copy.Object.IsValid == false || copy.Object.InputAuthority != agent.Object.InputAuthority)
					continue;

				KCC     kcc       = copy.Character.CharacterController;
				Vector3 direction = point - copy.Character.GetCameraTransform(false).Position;
				float   yaw       = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
				float   pitch     = -Mathf.Asin(direction.normalized.y) * Mathf.Rad2Deg;

				kcc.FixedData.LookYaw    = yaw;
				kcc.RenderData.LookYaw   = yaw;
				kcc.FixedData.LookPitch  = pitch;
				kcc.RenderData.LookPitch = pitch;
			}
		}

		/// <summary>Damage from no one (not a bullet, so armor doesn't take it).</summary>
		private static void Hit(Agent agent, float amount)
		{
			var hitData = new HitData
			{
				Action        = EHitAction.Damage,
				Amount        = amount,
				IsBullet      = false,
				Position      = agent.transform.position + Vector3.up,
				Normal        = Vector3.up,
				Direction     = Vector3.forward,
				Target        = agent.Health,
				InstigatorRef = Fusion.PlayerRef.None,
				HitType       = EHitType.None,
			};

			HitUtility.ProcessHit(ref hitData);
		}

		private static void MoveTo(KCC kcc, Agent agent, Vector3 position)
		{
			kcc.FixedData.BasePosition     = position;
			kcc.FixedData.DesiredPosition  = position;
			kcc.FixedData.TargetPosition   = position;
			kcc.RenderData.BasePosition    = position;
			kcc.RenderData.DesiredPosition = position;
			kcc.RenderData.TargetPosition  = position;

			kcc.SetPosition(position, true, false, false);
			agent.transform.position = position;
		}
#endif
	}
}
