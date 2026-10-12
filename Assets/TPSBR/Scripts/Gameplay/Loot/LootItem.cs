namespace TPSBR
{
	using UnityEngine;
	using Fusion;
	using Plugins.Outline;

	/// <summary>
	/// One item lying on the ground (phase 5a): a light networked object holding a LootStack, never a full gun. A gun's Weapon object
	/// only exists while someone holds it; dropping it turns it back into one of these with its mag and attachments.
	/// Static once spawned (position sent once, like the sample's item boxes) and kept for the whole match.
	/// F equips or swaps it, G picks it up into the inventory (Interactions → Inventory).
	/// </summary>
	public sealed class LootItem : ContextTRSPBehaviour, IInteraction
	{
		// PUBLIC MEMBERS

		[Networked, OnChangedRender(nameof(OnStackChanged))]
		public LootStack Stack { get; private set; }

		// PRIVATE MEMBERS

		private GameObject  _visual;
		private BoxCollider _collider;
		private LootStack   _shownStack;
		private bool        _isShown;
		private float       _hudHeight = 0.3f;
		private OutlineBehaviour _outline;

		// The sample's dropped-weapon outline: thin and white, full within 5 m, gone by 10 m
		private const float FULL_OUTLINE_DISTANCE = 5.0f;
		private const float NO_OUTLINE_DISTANCE   = 10.0f;

		// PUBLIC METHODS

		/// <summary>Server, from Runner.Spawn's onBeforeSpawned: what the new item holds.</summary>
		public void Init(LootStack stack)
		{
			Stack = stack;
		}

		/// <summary>Server: what this item holds now. An empty stack despawns it.</summary>
		public void SetStack(LootStack stack)
		{
			if (HasStateAuthority == false)
				return;

			if (stack.IsEmpty == true)
			{
				Runner.Despawn(Object);
				return;
			}

			Stack = stack;
			RefreshVisual();
		}

		// NetworkBehaviour INTERFACE

		public override void Spawned()
		{
			if (HasStateAuthority == true)
			{
				State.Position = transform.position;
				State.Rotation = transform.rotation;
			}
			else
			{
				transform.SetPositionAndRotation(State.Position, State.Rotation);
			}

			_isShown = false;
			RefreshVisual();
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (_visual != null)
			{
				Destroy(_visual);
				_visual = null;
			}

			_outline = null;
			_isShown = false;
		}

		// IInteraction INTERFACE

		string  IInteraction.Name        => GetPrompt(out _);
		string  IInteraction.Description => GetSecondPrompt();
		Vector3 IInteraction.HUDPosition => transform.position + Vector3.up * _hudHeight;
		bool    IInteraction.IsActive    => Object != null && Object.IsValid == true && Stack.IsEmpty == false;

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			var interact = new GameObject("Interact");
			interact.layer = ObjectLayer.Interaction;
			interact.transform.SetParent(transform, false);

			_collider = interact.AddComponent<BoxCollider>();
			_collider.size = new Vector3(0.4f, 0.3f, 0.4f);
			_collider.center = new Vector3(0.0f, 0.15f, 0.0f);
		}

		private void Update()
		{
			// Checked a few times a second per item; there can be hundreds
			if (_visual == null || ((Time.frameCount + GetInstanceID()) & 7) != 0)
				return;

			Agent agent = Context != null && Context.IsVisible == true ? Context.ObservedAgent : null;
			if (agent == null || Runner == null || Runner.IsPlayer == false)
			{
				SetOutline(false, 0.0f);
				return;
			}

			float distance = Vector3.Distance(agent.transform.position, transform.position);
			if (distance > NO_OUTLINE_DISTANCE)
			{
				SetOutline(false, 0.0f);
				return;
			}

			SetOutline(true, 1.0f - Mathf.Clamp01((distance - FULL_OUTLINE_DISTANCE) / (NO_OUTLINE_DISTANCE - FULL_OUTLINE_DISTANCE)));
		}

		// PRIVATE METHODS

		private void SetOutline(bool visible, float alpha)
		{
			if (visible == false)
			{
				if (_outline != null && _outline.enabled == true)
				{
					_outline.enabled = false;
				}

				return;
			}

			if (_outline == null)
			{
				_outline = _visual.AddComponent<OutlineBehaviour>();
				_outline.Settings.Width      = 8.0f;
				_outline.Settings.Intensity  = 4.0f;
				_outline.Settings.Pass       = UnityEngine.Rendering.Universal.RenderPassEvent.AfterRenderingTransparents;
				_outline.Settings.UpdateMode = EOutlineUpdateMode.Children;
			}

			_outline.Settings.Color = new Color(1.0f, 1.0f, 1.0f, alpha);

			if (_outline.enabled == false)
			{
				_outline.enabled = true;
			}
		}

		private void OnStackChanged()
		{
			RefreshVisual();
		}

		private void RefreshVisual()
		{
			LootStack stack = Stack;

			if (_isShown == true && stack.KindValue == _shownStack.KindValue && stack.Index == _shownStack.Index && stack.Attachments == _shownStack.Attachments)
			{
				_shownStack = stack;
				return;
			}

			if (_visual != null)
			{
				Destroy(_visual);
				_visual = null;
			}

			_outline    = null;
			_shownStack = stack;
			_isShown    = true;

			if (stack.IsEmpty == true)
				return;

			_visual = LootVisuals.Build(stack, transform, Runner, out Bounds bounds);

			// A forgiving interaction box: at least 0.35 m across, a little taller than the item
			Vector3 size = Vector3.Max(bounds.size + new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0.35f, 0.25f, 0.35f));
			_collider.size   = size;
			_collider.center = new Vector3(bounds.center.x, size.y * 0.5f, bounds.center.z);
			_hudHeight       = size.y + 0.15f;
		}

		private string GetSecondPrompt()
		{
			GetPrompt(out string second);
			return second;
		}

		/// <summary>
		/// The prompt for the player looking at it: name = the F line (or the G line when F doesn't apply), description = the G line
		/// for items that offer both.
		/// </summary>
		private string GetPrompt(out string second)
		{
			second = null;

			LootStack stack = Stack;
			string    label = ItemCatalog.GetLabel(stack);

			switch (stack.Kind)
			{
				case EItemKind.Gun:
					return $"[F] Equip | {label}";
				case EItemKind.Ammo:
					return $"[F] Pick up | {label}";
				case EItemKind.Attachment:
					Agent agent = Context != null ? Context.ObservedAgent : null;
					second = "[G] Pick up";
					if (agent != null && agent.Weapons.CurrentWeapon is BulletWeapon gun && gun.GunData != null && gun.GunData.Allows(ItemCatalog.GetAttachment(stack)) == true)
						return $"[F] Equip | {label}";
					second = null;
					return $"[G] Pick up | {label}";
				default:
					second = "[G] Pick up";
					return $"[F] Equip | {label}";
			}
		}
	}
}
