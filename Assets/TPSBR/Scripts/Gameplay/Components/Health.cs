using System;
using Fusion;

namespace TPSBR
{
	using UnityEngine;

	public struct BodyHitData : INetworkStruct
	{
		public PlayerRef  Instigator;

		public EHitAction Action           { get { return (EHitAction)_actionAndDamage.GetByte(0); }                       set { _actionAndDamage.SetByte(0, (byte)value); } }
		public float      Damage           { get { return _actionAndDamage.GetUShort(16) * DAMAGE_PRECISION; }             set { _actionAndDamage.SetUShort(16, (ushort)Mathf.Clamp(Mathf.RoundToInt(value / DAMAGE_PRECISION), 0, ushort.MaxValue)); } }
		public Vector3    RelativePosition { get { return NetworkCompression.UnpackOffset(_relativePosition, BODY_RANGE); } set { _relativePosition = NetworkCompression.PackOffset(value, BODY_RANGE); } }
		public Vector3    Direction        { get { return NetworkCompression.UnpackDirection(_direction); }                set { _direction = NetworkCompression.PackDirection(value); } }
		/// <summary>Phase 11c: what the shooter's hit marker shows (EBodyHitFlags), sent with the hit.</summary>
		public byte       Flags            { get { return _actionAndDamage.GetByte(8); }                                   set { _actionAndDamage.SetByte(8, value); } }

		private const float DAMAGE_PRECISION = 0.25f;
		private const float BODY_RANGE       = 4.0f;

		private uint _actionAndDamage;
		private uint _relativePosition;
		private uint _direction;
	}

	/// <summary>Phase 11c: hit details for the shooter's marker (BodyHitData.Flags).</summary>
	[System.Flags]
	public enum EBodyHitFlags : byte
	{
		None       = 0,
		Critical   = 1,
		ArmorHit   = 2,
		ArmorBroke = 4,
	}

	/// <summary>
	/// Brownout health (phase 4): 150 with no regen, healing to 200 with a trauma kit, armor tiers that wear out and stop bullets only,
	/// and the downed state (separate downed health, bleed-out, revive). Healing and reviving are in Health.Actions.cs.
	/// The server owns all of it; numbers come from HealthSettings.
	/// </summary>
	public sealed partial class Health : ContextBehaviour, IHitTarget, IHitInstigator
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>
		/// Test tool (Brownout → Test → God Mode): players take no damage. Checked where damage is applied, so set it on the server.
		/// </summary>
		public static bool DebugGodMode;
#endif

		// PUBLIC MEMBERS

		/// <summary>Alive, including downed.</summary>
		public bool  IsAlive   => CurrentHealth > 0f || _downed == true;
		/// <summary>Alive and not downed.</summary>
		public bool  IsUp      => CurrentHealth > 0f && _downed == false;
		public bool  IsDowned  => _downed == true;
		public float MaxHealth => _maxHealth;
		public float MaxShield => _maxShield;

		[Networked, HideInInspector]
		public float CurrentHealth { get; private set; }
		[Networked, HideInInspector]
		public float CurrentShield { get; private set; }

		/// <summary>0 = no armor, else tier 1..3.</summary>
		[Networked, HideInInspector]
		public byte  ArmorTier     { get; private set; }
		/// <summary>Incoming damage the armor takes before it breaks.</summary>
		[Networked, HideInInspector]
		public float ArmorLeft     { get; private set; }
		public float ArmorDurability => HealthSettings.Current.GetArmorTier(ArmorTier).Durability;

		[Networked, HideInInspector]
		public float DownedHealth  { get; private set; }
		/// <summary>Seconds left before a downed player bleeds out (paused while being revived).</summary>
		[Networked, HideInInspector]
		public float BleedOutLeft  { get; private set; }
		/// <summary>Downs in this life (picks the bleed-out time).</summary>
		[Networked, HideInInspector]
		public byte  DownCount     { get; private set; }
		[Networked, HideInInspector]
		public int   DownedTick    { get; private set; }

		public bool  IsBeingRevived => _downed == true && _beingRevivedTick > 0 && _beingRevivedTick >= Runner.Tick - 1;
		public Agent Agent          => _agent;

		public event Action<HitData> HitTaken;
		public event Action<HitData> HitPerformed;

		// PRIVATE MEMBERS

		[SerializeField]
		private float _maxHealth;
		[SerializeField]
		private float _maxShield;
		[SerializeField]
		private float _startShield;
		[SerializeField]
		private Transform _hitIndicatorPivot;

		[Networked]
		private int _hitCount { get; set; }
		[Networked, Capacity(4)]
		private NetworkArray<BodyHitData> _hitData { get; }

		[Networked]
		private NetworkBool _downed { get; set; }
		[Networked]
		private PlayerRef _downedBy { get; set; }
		[Networked]
		private byte _downedHitType { get; set; }
		[Networked]
		private int _beingRevivedTick { get; set; }

		private int _visibleHitCount;
		private Agent _agent;

		// PUBLIC METHODS

		public void OnSpawned(Agent agent)
		{
			_visibleHitCount = _hitCount;

			if (HasStateAuthority == true && _agent != null)
			{
				ArmorTier         = 0;
				ArmorLeft         = 0.0f;
				_downed           = false;
				DownedHealth      = 0.0f;
				BleedOutLeft      = 0.0f;
				DownCount         = 0;
				DownedTick        = 0;
				_downedBy         = PlayerRef.None;
				_beingRevivedTick = 0;

				InitActions();
			}
		}

		public void OnDespawned()
		{
			HitTaken = null;
			HitPerformed = null;
		}

		/// <summary>Every tick (server): the bleed-out.</summary>
		public void OnFixedUpdate()
		{
			if (HasStateAuthority == false)
				return;

			if (_downed == true && IsBeingRevived == false)
			{
				// The slack absorbs float rounding over a thousand ticks, which otherwise adds one
				BleedOutLeft = Mathf.Max(0.0f, BleedOutLeft - Runner.DeltaTime);
				if (BleedOutLeft <= 0.001f)
				{
					HealthDebug.Log(Runner, $"BLEED OUT {name} after {(Runner.Tick - DownedTick) * Runner.DeltaTime:F2} s downed (down {DownCount})");
					KillDowned("bleed-out");
				}
			}
		}

		/// <summary>
		/// Server: a downed player dies (bleed-out or team out). The kill goes to whoever downed them.
		/// </summary>
		public void KillDowned(string reason)
		{
			if (HasStateAuthority == false || _downed == false)
				return;

			_downed       = false;
			DownedHealth  = 0.0f;
			BleedOutLeft  = 0.0f;
			CurrentHealth = 0.0f;

			ResetActions();

			var hitData = new HitData
			{
				Action        = EHitAction.Damage,
				Amount        = 0.0f,
				IsFatal       = true,
				Position      = transform.position,
				Direction     = Vector3.down,
				Normal        = Vector3.up,
				InstigatorRef = _downedBy,
				Target        = this,
				HitType       = (EHitType)_downedHitType,
			};

			HealthDebug.Log(Runner, $"DEATH {name} ({reason}), kill to {_downedBy}");

			Context.GameplayMode.AgentDeath(_agent, hitData);
		}

		/// <summary>Server: back up from downed at the revived health.</summary>
		public void Revive()
		{
			if (HasStateAuthority == false || _downed == false)
				return;

			HealthSettings settings = HealthSettings.Current;

			_downed           = false;
			DownedHealth      = 0.0f;
			BleedOutLeft      = 0.0f;
			_beingRevivedTick = 0;
			CurrentHealth     = Mathf.Min(settings.RevivedHealth, settings.HealthCap);
		}

		/// <summary>Server, from a reviver's tick: pauses the bleed-out.</summary>
		public void MarkBeingRevived()
		{
			if (HasStateAuthority == true && _downed == true)
			{
				_beingRevivedTick = Runner.Tick;
			}
		}

		public override void CopyBackingFieldsToState(bool firstTime)
		{
			base.CopyBackingFieldsToState(firstTime);

			InvokeWeavedCode();

			CurrentHealth = _maxHealth;
			CurrentShield = _startShield;
		}

		/// <summary>Server (phase 5a, the inventory): wears an armor vest of this tier with this much damage left; tier 0 takes it off.</summary>
		public void SetArmor(int tier, float left)
		{
			if (HasStateAuthority == false)
				return;

			ArmorTier = (byte)Mathf.Clamp(tier, 0, 3);
			ArmorLeft = ArmorTier > 0 ? Mathf.Clamp(left, 0.0f, HealthSettings.Current.GetArmorTier(ArmorTier).Durability) : 0.0f;
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>Test tool (server): armor tier 0..3, at full durability.</summary>
		public void DebugSetArmor(int tier)
		{
			SetArmor(tier, tier > 0 ? HealthSettings.Current.GetArmorTier(Mathf.Clamp(tier, 1, 3)).Durability : 0.0f);
		}

		/// <summary>Test tool (server): sets health on a player who is up.</summary>
		public void DebugSetHealth(float health)
		{
			if (HasStateAuthority == true && IsUp == true)
			{
				SetHealth(Mathf.Max(1.0f, health));
			}
		}
#endif

		// NetworkBehaviour INTERFACE

		public override void Render()
		{
			if (Runner.Mode != SimulationModes.Server)
			{
				UpdateVisibleHits();
			}
		}

		// MONOBEHAVIOUR

		private void Awake()
		{
			_agent = GetComponent<Agent>();

			if (_agent != null)
			{
				// Brownout players: health from HealthSettings, no regen and no shield (phase 3a), not the agent prefab
				_maxHealth   = HealthSettings.Current.MaxHealth;
				_maxShield   = 0.0f;
				_startShield = 0.0f;
			}
		}

		// IHitTarget INTERFACE

		Transform IHitTarget.HitPivot => _hitIndicatorPivot != null ? _hitIndicatorPivot : transform;

		void IHitTarget.ProcessHit(ref HitData hitData)
		{
			if (IsAlive == false)
			{
				hitData.Amount = 0;
				return;
			}

			if (hitData.Action == EHitAction.Damage && IsFriendlyFire(hitData) == true)
			{
				HealthDebug.Log(Runner, HasStateAuthority, $"FRIENDLY FIRE blocked: {hitData.InstigatorRef} -> {name}, {hitData.Amount:F1}");
				hitData.Amount = 0;
				return;
			}

			bool wasDowned = _downed;

			ApplyHit(ref hitData);

			// Bots (phase 9): the server's brain learns who hit it
			if (hitData.Amount > 0.0f && _agent != null && _agent.Bot != null)
			{
				_agent.Bot.OnHit(hitData);
			}
			else if (hitData.Amount > 0.0f && _agent != null && _agent.Object.InputAuthority.IsRealPlayer == true)
			{
				// A lone player's bot teammate (phase 9b) comes to help
				BotBrain.OnHumanHit(_agent, hitData);
			}

			if (IsAlive == false)
			{
				hitData.IsFatal = true;
				Context.GameplayMode.AgentDeath(_agent, hitData);
			}
			else if (_downed == true && wasDowned == false)
			{
				// The hit that downs counts as the fatal one (red hit marker, TTK is time to down)
				hitData.IsFatal = true;
				Context.GameplayMode.AgentDowned(_agent, hitData);
			}
		}

		// IHitInstigator INTERFACE

		void IHitInstigator.HitPerformed(HitData hitData)
		{
			if (hitData.Amount > 0 && hitData.Target != (IHitTarget)this && Runner.IsResimulation == false)
			{
				HitPerformed?.Invoke(hitData);
			}
		}

		// PRIVATE METHODS

		private void ApplyHit(ref HitData hit)
		{
			if (IsAlive == false)
				return;

			if (hit.Action == EHitAction.Damage)
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				if (DebugGodMode == true && _agent != null && _agent.Object.InputAuthority.IsRealPlayer == true)
				{
					hit.Amount = 0.0f;
				}
#endif
				hit.Amount = ApplyDamage(ref hit);
			}
			else if (hit.Action == EHitAction.Heal)
			{
				// The sample's health pickups heal up to the normal maximum, like bandages
				hit.Amount = _downed == true ? 0.0f : AddHealth(Mathf.Max(0.0f, Mathf.Min(hit.Amount, _maxHealth - CurrentHealth)));
			}
			else if (hit.Action == EHitAction.Shield)
			{
				hit.Amount = AddShield(hit.Amount);
			}

			if (hit.Amount <= 0)
				return;

			// Hit taken effects (blood) is shown immediately for local player, for other
			// effects (hit number, crosshair hit effect) we are waiting for server confirmation
			if (hit.InstigatorRef == Context.LocalPlayerRef && Runner.IsForward == true)
			{
				HitTaken?.Invoke(hit);
			}

			if (HasStateAuthority == false)
				return;

			_hitCount++;

			var bodyHitData = new BodyHitData
			{
				Action           = hit.Action,
				Damage           = hit.Amount,
				Direction        = hit.Direction,
				RelativePosition = hit.Position != Vector3.zero ? hit.Position - transform.position : Vector3.zero,
				Instigator       = hit.InstigatorRef,
				Flags            = (byte)((hit.IsCritical ? EBodyHitFlags.Critical : 0) | (hit.IsArmorHit ? EBodyHitFlags.ArmorHit : 0) | (hit.ArmorBroke ? EBodyHitFlags.ArmorBroke : 0)),
			};

			int hitIndex = _hitCount % _hitData.Length;
			_hitData.Set(hitIndex, bodyHitData);
		}

		private float ApplyDamage(ref HitData hit)
		{
			float damage = hit.Amount;
			if (damage <= 0f)
				return 0f;

			// Downed: the separate downed health takes it all; armor doesn't help
			if (_downed == true)
			{
				float downedBefore = DownedHealth;
				DownedHealth = Mathf.Max(0.0f, DownedHealth - damage);

				HealthDebug.Log(Runner, HasStateAuthority, $"HIT {name} downed {damage:F1}, downed health {downedBefore:F1} -> {DownedHealth:F1}");

				if (DownedHealth <= 0.0f)
				{
					_downed = false;
					ResetActions();
				}

				return downedBefore - DownedHealth;
			}

			float healthBefore = CurrentHealth;
			float taken        = ApplyArmor(damage, ref hit);

			var shieldChange = AddShield(-taken);
			AddHealth(-(taken + shieldChange));

			HealthDebug.Log(Runner, HasStateAuthority, $"HIT {name} {(hit.IsBullet ? (hit.IsCritical ? "bullet head" : "bullet body") : hit.HitType.ToString())} {damage:F1} -> {taken:F1}, health {healthBefore:F1} -> {CurrentHealth:F1}{(ArmorTier > 0 || taken < damage ? $", armor T{ArmorTier} {ArmorLeft:F1} left" : "")}");

			if (CurrentHealth <= 0.0f && _agent != null && Context.GameplayMode != null && Context.GameplayMode.CanBeDowned(_agent) == true)
			{
				GoDown(hit);
			}

			return healthBefore - CurrentHealth;
		}

		/// <summary>
		/// Armor stops bullets (Logan, 4 Oct) and frags (5 Oct, EquipmentSettings.ArmorCutsFrags; a frag counts as a body hit). A hit on the
		/// armored area (the body; the head only on tier 3) is cut by the tier's percentage and wears the armor by the full incoming damage.
		/// The hit that breaks it is still cut.
		/// </summary>
		private float ApplyArmor(float damage, ref HitData hit)
		{
			bool isFrag = hit.HitType == EHitType.Grenade && EquipmentSettings.Current.ArmorCutsFrags == true;
			if ((hit.IsBullet == false && isFrag == false) || ArmorTier == 0)
				return damage;

			HealthSettings.ArmorTier tier = HealthSettings.Current.GetArmorTier(ArmorTier);
			float cut = hit.IsCritical == true ? tier.HeadCut : tier.BodyCut;
			if (cut <= 0.0f)
				return damage;

			ArmorLeft -= damage;
			hit.IsArmorHit = true;

			if (ArmorLeft <= 0.001f)
			{
				hit.ArmorBroke = true;
				HealthDebug.Log(Runner, HasStateAuthority, $"ARMOR BROKE {name} tier {ArmorTier}");
				ArmorTier = 0;
				ArmorLeft = 0.0f;
			}

			return damage * (1.0f - cut);
		}

		private void GoDown(HitData hit)
		{
			HealthSettings settings = HealthSettings.Current;

			_downed          = true;
			DownCount        = (byte)Mathf.Min(DownCount + 1, 255);
			DownedHealth     = settings.DownedHealth;
			BleedOutLeft     = settings.GetBleedOutTime(DownCount);
			DownedTick       = Runner.Tick;
			_downedBy        = hit.InstigatorRef;
			_downedHitType   = (byte)hit.HitType;
			_beingRevivedTick = 0;
			CurrentHealth    = 0.0f;

			ResetActions();

			HealthDebug.Log(Runner, HasStateAuthority, $"DOWNED {name} by {hit.InstigatorRef} (down {DownCount}, bleed-out {BleedOutLeft:F0} s)");
		}

		/// <summary>No friendly fire: a teammate's damage does nothing. Your own (fall damage) still counts.</summary>
		private bool IsFriendlyFire(HitData hit)
		{
			if (_agent == null)
				return false;

			Agent instigator = null;

			if (hit.Instigator is Health instigatorHealth)
			{
				instigator = instigatorHealth._agent;
			}
			else if (hit.InstigatorRef.IsRealPlayer == true && Context.NetworkGame != null)
			{
				Player player = Context.NetworkGame.GetPlayer(hit.InstigatorRef);
				instigator = player != null ? player.ActiveAgent : null;
			}

			return instigator != null && instigator != _agent && GameplayMode.AreTeammates(instigator, _agent) == true;
		}

		private float AddHealth(float health)
		{
			float previousHealth = CurrentHealth;
			SetHealth(CurrentHealth + health);
			return CurrentHealth - previousHealth;
		}

		private float AddShield(float shield)
		{
			float previousShield = CurrentShield;
			SetShield(CurrentShield + shield);
			return CurrentShield - previousShield;
		}

		private void SetHealth(float health)
		{
			// Above the maximum only from a trauma kit, up to the cap
			float cap = _agent != null ? Mathf.Max(_maxHealth, HealthSettings.Current.HealthCap) : _maxHealth;
			CurrentHealth = Mathf.Clamp(health, 0, cap);
		}

		private void SetShield(float shield)
		{
			CurrentShield = Mathf.Clamp(shield, 0, _maxShield);
		}

		private void UpdateVisibleHits()
		{
			if (_visibleHitCount == _hitCount)
				return;

			int dataCount = _hitData.Length;
			int oldestHitData = _hitCount - dataCount + 1;

			for (int i = Mathf.Max(_visibleHitCount + 1, oldestHitData); i <= _hitCount; i++)
			{
				int shotIndex = i % dataCount;
				var bodyHitData = _hitData.Get(shotIndex);
				var flags       = (EBodyHitFlags)bodyHitData.Flags;

				var hitData = new HitData
				{
					IsCritical    = (flags & EBodyHitFlags.Critical) != 0,
					IsArmorHit    = (flags & EBodyHitFlags.ArmorHit) != 0,
					ArmorBroke    = (flags & EBodyHitFlags.ArmorBroke) != 0,
					Action        = bodyHitData.Action,
					Amount        = bodyHitData.Damage,
					Position      = transform.position + bodyHitData.RelativePosition,
					Direction     = bodyHitData.Direction,
					Normal        = -bodyHitData.Direction,
					Target        = this,
					InstigatorRef = bodyHitData.Instigator,
					IsFatal       = i == _hitCount && (IsAlive == false || (_downed == true && DownedHealth >= HealthSettings.Current.DownedHealth - 0.01f)),
				};

				OnHitTaken(hitData);
			}

			_visibleHitCount = _hitCount;
		}

		private void OnHitTaken(HitData hit)
		{
			// For local player, HitTaken was already called when applying hit
			if (hit.InstigatorRef != Context.LocalPlayerRef)
			{
				HitTaken?.Invoke(hit);
			}

			// We use _hitData buffer to inform instigator about successful hit as this needs
			// to be synchronized over network as well (e.g. when spectating other players)
			if (hit.InstigatorRef.IsRealPlayer == true && hit.InstigatorRef == Context.ObservedPlayerRef)
			{
				var instigator = hit.Instigator;

				if (instigator == null)
				{
					var player = Context.NetworkGame.GetPlayer(hit.InstigatorRef);
					instigator = player != null ? player.ActiveAgent.Health as IHitInstigator : null;
				}

				if (instigator != null)
				{
					instigator.HitPerformed(hit);
				}
			}
		}

		// DEBUG

		[ContextMenu("Add Health")]
		private void Debug_AddHealth()
		{
			CurrentHealth += 10;
		}

		[ContextMenu("Remove Health")]
		private void Debug_RemoveHealth()
		{
			CurrentHealth -= 10;
		}
	}
}
