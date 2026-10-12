namespace TPSBR
{
	using UnityEngine;
	using Fusion.Addons.KCC;

	/// <summary>
	/// Test tool (editor and development builds): on-screen movement readout for the player the camera looks through.
	/// Speed is horizontal, from the fixed simulation. Jump apex is the highest the feet got above the takeoff point on the last jump.
	/// Toggle with Brownout → Test → Movement Readout.
	/// </summary>
	public sealed class MovementReadout : MonoBehaviour
	{
		// PUBLIC MEMBERS

		/// <summary>Off by default since phase 6a (the real HUD is in); F9 or the menu shows it.</summary>
		public static bool Show = false;

		public float Speed        { get; private set; }
		public float PeakSpeed    { get; private set; }
		public float LastJumpApex { get; private set; }

		// PRIVATE MEMBERS

		private Agent    _agent;
		private KCC      _kcc;
		private bool     _wasGrounded = true;
		private float    _takeoffY;
		private float    _apexY;
		private float    _peakResetTime;
		private GUIStyle _style;

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_agent = GetComponent<Agent>();
			_kcc   = GetComponent<KCC>();
		}

		private void LateUpdate()
		{
			if (_agent == null || _agent.Object == null || _agent.Object.IsValid == false)
				return;

			var keyboard = UnityEngine.InputSystem.Keyboard.current;
			if (_agent.IsObserved == true && _agent.HasInputAuthority == true && keyboard != null && keyboard.f9Key.wasPressedThisFrame == true)
			{
				Show = !Show;
			}

			KCCData data = _kcc.IsProxy == true ? _kcc.RenderData : _kcc.FixedData;

			Speed = data.RealVelocity.OnlyXZ().magnitude;

			// Peak over the last 2 s, so short spikes are readable
			if (Speed >= PeakSpeed || Time.time > _peakResetTime)
			{
				PeakSpeed      = Speed;
				_peakResetTime = Time.time + 2.0f;
			}

			// Takeoff is the last grounded height, not the first airborne one (that frame is already on its way up)
			float y = data.TargetPosition.y;
			if (data.IsGrounded == false)
			{
				if (_wasGrounded == true)
				{
					_apexY = y;
				}

				_apexY = Mathf.Max(_apexY, y);
			}
			else
			{
				if (_wasGrounded == false)
				{
					LastJumpApex = _apexY - _takeoffY;
				}

				_takeoffY = y;
			}

			_wasGrounded = data.IsGrounded;
		}

		private void OnGUI()
		{
			if (Show == false || _agent == null || _agent.Object == null || _agent.Object.IsValid == false || _agent.IsObserved == false)
				return;

			if (_style == null)
			{
				_style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = false };
				_style.normal.textColor = Color.white;
			}

			KCCData data      = _kcc.FixedData;
			int     tick      = _agent.Runner.Tick;
			float   deltaTime = _agent.Runner.DeltaTime;
			float   dashIn    = Mathf.Max(0.0f, (data.DashReadyTick - tick) * deltaTime);

			// Health (phase 4) until the HUD in phase 6
			Health health = _agent.Health;
			string downed = health.IsDowned == true ? $"DOWNED {health.DownedHealth:F0}/{HealthSettings.Current.DownedHealth:F0}, bleed-out {health.BleedOutLeft:F1} s{(health.IsBeingRevived ? " (being revived)" : "")}" : "-";
			string action = health.IsHealing == true ? $"healing {HealthSettings.Current.GetHealItem(health.HealingItem).Name} {health.HealProgress * 100.0f:F0}%" : health.IsReviving == true ? $"reviving {health.ReviveProgress * 100.0f:F0}%" : "-";

			// Equipment and gear (phase 5b)
			AgentEquipment equipment = _agent.EquipmentActions;
			if (equipment != null && equipment.IsBusy == true)
			{
				action = $"{(equipment.IsGrappling ? "grappling" : "throwing")} {EquipmentSettings.GetName(equipment.CurrentKind)} {equipment.GetElapsed(false):F2} s{(equipment.HasReleased ? " (released)" : "")}";
			}

			LootStack equipped = _agent.EquipmentSlot;
			LootStack gear     = _agent.GearSlot;
			string    gearText = gear.IsEmpty == true ? "none" : $"{ItemCatalog.GetName(gear)} {(_agent.GearOn ? "ON" : "off")}, {gear.SecondsLeft:F1} s left{(_agent.IsCamouflaged ? ", see-through" : "")}";
			float     dizzy    = _agent.Senses.GetConcussionValue(false);

			string text =
				$"Health     {health.CurrentHealth:F0}  team {_agent.Team}\n" +
				$"Armor      {(health.ArmorTier > 0 ? $"tier {health.ArmorTier}, {health.ArmorLeft:F0}/{health.ArmorDurability:F0} left" : "none")}\n" +
				$"Heals      {health.GetHealItemCount(EHealItem.Bandage)} bandage, {health.GetHealItemCount(EHealItem.MedKit)} med, {health.GetHealItemCount(EHealItem.TraumaKit)} trauma; Q uses {(_agent.HealthSlot.IsEmpty == true ? "nothing" : HealthSettings.Current.GetHealItem(health.EquippedHealItem).Name)}\n" +
				$"Looking at {GetInteractionName()}\n" +
				$"Equipment  {(equipped.IsEmpty == true ? "none" : ItemCatalog.GetLabel(equipped))}  gear {gearText}{(dizzy > 0.0f ? $"  DIZZY {dizzy:F2}" : "")}\n" +
				$"Action     {action}\n" +
				$"Downed     {downed}\n" +
				$"Speed      {Speed:F2} m/s  (peak {PeakSpeed:F2})\n" +
				$"Mode       {data.MoveMode}  chain {data.ChainStep}{(AgentMovement.IsDropMode(data.MoveMode) ? $"  fall {-data.DropVelocity.y:F1}  forward {data.DropVelocity.OnlyXZ().magnitude:F1}  carry {data.DropCarry:F1} m/s" : "")}\n" +
				$"Stance     {data.Stance}  capsule {_kcc.Settings.Height:F2} m\n" +
				$"Eye        {AgentMovement.GetEyeHeight(_kcc, data):F2} m\n" +
				$"Grounded   {data.IsGrounded}\n" +
				$"Dash ready {(dashIn > 0.0f ? $"in {dashIn:F1} s" : "yes")}\n" +
				$"Last jump  {LastJumpApex:F2} m";

			GUI.Box(new Rect(10.0f, Screen.height * 0.5f - 120.0f, 520.0f, 276.0f), text, _style);
		}

		private string GetInteractionName()
		{
			IInteraction target = _agent.Interactions.InteractionTarget;
			return target != null && target.Equals(null) == false ? target.Name : "-";
		}
	}
}
