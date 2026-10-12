using System;
using UnityEngine;

namespace TPSBR
{
	public enum EHealItem
	{
		Bandage   = 0,
		MedKit    = 1,
		TraumaKit = 2,
	}

	/// <summary>
	/// Health, armor, healing, downed and revive numbers (Items, Gear & Inventory page; Phase 4 spec). Tuned here, never in code.
	/// Linked from GlobalSettings.Health. Crawl speed is the crouch speed (Logan, 4 Oct), so it lives in MovementSettings.
	/// </summary>
	[CreateAssetMenu(fileName = "HealthSettings", menuName = "TPSBR/Health Settings")]
	public sealed class HealthSettings : ScriptableObject
	{
		// CONSTANTS

		public const int HealItemCount = 3;

		[Serializable]
		public struct HealItem
		{
			public EHealItem Item;
			public string    Name;
			[Tooltip("Health added when the use time ends.")]
			public float     Amount;
			[Tooltip("Healing stops at this much health (150 for bandages and med kits, 200 for the trauma kit).")]
			public float     Cap;
			[Tooltip("Seconds Q has to be held.")]
			public float     UseTime;
			public int       Stack;
		}

		[Serializable]
		public struct ArmorTier
		{
			[Tooltip("Body damage cut, 0..1.")]
			public float BodyCut;
			[Tooltip("Head damage cut, 0..1. 0 = the head isn't covered (and head hits don't wear the armor).")]
			public float HeadCut;
			[Tooltip("Incoming damage to the armored area before it breaks (one health bar = 150).")]
			public float Durability;
		}

		[Header("Health")]
		public float       MaxHealth = 150.0f;
		[Tooltip("Only the trauma kit heals above MaxHealth, up to this.")]
		public float       HealthCap = 200.0f;

		[Header("Healing (Q, a hold)")]
		public HealItem[]  HealItems =
		{
			new HealItem { Item = EHealItem.Bandage,   Name = "Bandage",    Amount = 25.0f,  Cap = 150.0f, UseTime = 1.0f, Stack = 10 },
			new HealItem { Item = EHealItem.MedKit,    Name = "Med kit",    Amount = 75.0f,  Cap = 150.0f, UseTime = 2.0f, Stack = 5  },
			new HealItem { Item = EHealItem.TraumaKit, Name = "Trauma kit", Amount = 200.0f, Cap = 200.0f, UseTime = 4.0f, Stack = 1  },
		};
		[Tooltip("Carried at spawn: bandages, med kits, trauma kits (until loot and the inventory, phase 5a).")]
		public int[]       SpawnHealItems = { 5, 2, 1 };

		[Header("Armor (stops bullets only, Logan 4 Oct)")]
		[Tooltip("Tiers 1, 2, 3.")]
		public ArmorTier[] ArmorTiers =
		{
			new ArmorTier { BodyCut = 0.30f, HeadCut = 0.00f, Durability = 300.0f },
			new ArmorTier { BodyCut = 0.40f, HeadCut = 0.00f, Durability = 450.0f },
			new ArmorTier { BodyCut = 0.50f, HeadCut = 0.30f, Durability = 600.0f },
		};

		[Header("Downed")]
		[Tooltip("Bleed-out time for the 1st, 2nd, ... down in a life; the last entry repeats.")]
		public float[]     BleedOutTimes = { 30.0f, 20.0f, 10.0f };
		[Tooltip("A downed player is finished by this much damage.")]
		public float       DownedHealth = 50.0f;
		[Tooltip("The camera switches to over the shoulder this long after going down (downed_to_third_person_s).")]
		public float       ThirdPersonDelay = 0.45f;

		[Header("Revive (F, a hold)")]
		public float       ReviveTime = 3.1f;
		public float       RevivedHealth = 50.0f;
		[Tooltip("How far the reviver can be from the downed teammate (the sample's interaction reach).")]
		public float       ReviveReach = 2.0f;
		[Tooltip("The reviver has to look within this many degrees of the downed teammate.")]
		public float       ReviveLookAngle = 45.0f;

		[Header("Teams")]
		[Tooltip("Duos for v1. Players are paired in join order.")]
		public int         TeamSize = 2;

		// PUBLIC MEMBERS

		private static HealthSettings _fallback;

		/// <summary>
		/// The asset linked from GlobalSettings. Falls back to the defaults above (with a warning) if it isn't linked yet.
		/// </summary>
		public static HealthSettings Current
		{
			get
			{
				HealthSettings settings = Global.Settings != null ? Global.Settings.Health : null;
				if (settings != null)
					return settings;

				if (_fallback == null)
				{
					_fallback = CreateInstance<HealthSettings>();
					Debug.LogWarning("HealthSettings isn't linked in GlobalSettings; using the code defaults. Run Brownout → Setup → Create Health Settings.");
				}

				return _fallback;
			}
		}

		// PUBLIC METHODS

		public HealItem GetHealItem(EHealItem item)
		{
			int index = (int)item;
			return HealItems != null && index >= 0 && index < HealItems.Length ? HealItems[index] : default;
		}

		/// <summary>Tier 1..3; default (no cut) for 0 or out of range.</summary>
		public ArmorTier GetArmorTier(int tier)
		{
			return ArmorTiers != null && tier >= 1 && tier <= ArmorTiers.Length ? ArmorTiers[tier - 1] : default;
		}

		/// <summary>Bleed-out time for the given down in this life (1 = first).</summary>
		public float GetBleedOutTime(int downCount)
		{
			if (BleedOutTimes == null || BleedOutTimes.Length == 0)
				return 30.0f;

			return BleedOutTimes[Mathf.Clamp(downCount - 1, 0, BleedOutTimes.Length - 1)];
		}
	}
}
