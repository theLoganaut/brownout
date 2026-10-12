namespace TPSBR
{
	using UnityEngine;

	/// <summary>
	/// Phase 10: arm clips over everything below (upper-body mask): healing (bandage, or the syringe for the med and trauma kits),
	/// the punch (Melee with a gun in hand, the right jab without) and throwing equipment. Healing and the punch follow their timers.
	/// </summary>
	public sealed class HandsLayer : ClipLayer
	{
		// PRIVATE MEMBERS

		[SerializeField]
		private float _fadeDuration = 0.15f;

		private Agent           _agent;
		private SyncedClipState _healBandage;
		private SyncedClipState _healSyringe;
		private SyncedClipState _melee;
		private SyncedClipState _jab;
		private SyncedClipState _throw;

		// PUBLIC MEMBERS

		public bool IsBusy => HasActiveClip();

		// AnimationLayer INTERFACE

		protected override void OnInitialize()
		{
			_agent       = Controller.GetComponentNoAlloc<Agent>();
			_healBandage = Find("HealBandage");
			_healSyringe = Find("HealSyringe");
			_melee       = Find("Melee");
			_jab         = Find("Jab");
			_throw       = Find("Throw");
		}

		protected override void OnFixedUpdate()
		{
			Health  health  = _agent.Health;
			Weapons weapons = _agent.Weapons;

			if (health.IsAlive == false || health.IsDowned == true)
			{
				Show(null, _fadeDuration);
				return;
			}

			if (health.IsHealing == true)
			{
				SyncedClipState clip = health.HealingItem == EHealItem.Bandage ? _healBandage : _healSyringe;
				Show(clip, _fadeDuration);
				clip.SetSyncedTime(health.HealProgress);
				return;
			}

			if (weapons.IsPunching == true)
			{
				SyncedClipState clip = weapons.CurrentWeapon != null ? _melee : _jab;
				Show(clip, 0.08f);
				clip.SetSyncedTime(weapons.PunchProgress);
				return;
			}

			AgentEquipment equipment = _agent.EquipmentActions;
			if (equipment != null && equipment.IsThrowing == true)
			{
				Show(_throw, 0.1f);
				return;
			}

			Show(null, _fadeDuration);
		}
	}
}
