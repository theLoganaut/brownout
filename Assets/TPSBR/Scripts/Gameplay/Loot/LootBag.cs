namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using Fusion;

	/// <summary>
	/// The stash a player or dummy leaves when they die (phase 5a): everything they carried, in one bag at the body. F opens it in a
	/// STASH panel beside the inventory (Inventory.OpenStash). Kept for the match; it goes when it's empty.
	/// </summary>
	public sealed class LootBag : ContextTRSPBehaviour, IInteraction
	{
		// CONSTANTS

		public const int Capacity = 32;

		// PUBLIC MEMBERS

		public string OwnerName => _ownerName.ToString();

		public int ItemCount
		{
			get
			{
				int count = 0;
				for (int i = 0; i < _items.Length; ++i)
				{
					if (_items[i].IsEmpty == false)
					{
						++count;
					}
				}

				return count;
			}
		}

		// PRIVATE MEMBERS

		[Networked, Capacity(Capacity)]
		private NetworkArray<LootStack> _items { get; }
		[Networked]
		private NetworkString<_32> _ownerName { get; set; }

		private GameObject _visual;

		private static Material _material;

		// PUBLIC METHODS

		/// <summary>Server, from Runner.Spawn's onBeforeSpawned.</summary>
		public void Init(string ownerName, List<LootStack> items)
		{
			_ownerName = ownerName != null && ownerName.Length > 32 ? ownerName.Substring(0, 32) : ownerName;

			int index = 0;
			for (int i = 0; i < items.Count && index < _items.Length; ++i)
			{
				if (items[i].IsEmpty == false)
				{
					_items.Set(index++, items[i]);
				}
			}
		}

		public LootStack GetItem(int index)
		{
			return index >= 0 && index < _items.Length ? _items[index] : default;
		}

		/// <summary>Server: replaces one item (an empty stack clears it). The bag goes when nothing is left.</summary>
		public void SetItem(int index, LootStack stack)
		{
			if (HasStateAuthority == false || index < 0 || index >= _items.Length)
				return;

			_items.Set(index, stack.IsEmpty == true ? default : stack);

			if (ItemCount == 0)
			{
				Runner.Despawn(Object);
			}
		}

		/// <summary>Server: puts an item in the first free place. Returns false when the bag is full.</summary>
		public bool AddItem(LootStack stack)
		{
			if (HasStateAuthority == false || stack.IsEmpty == true)
				return false;

			for (int i = 0; i < _items.Length; ++i)
			{
				if (_items[i].IsEmpty == true)
				{
					_items.Set(i, stack);
					return true;
				}
			}

			return false;
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

			if (_visual == null)
			{
				BuildVisual();
			}
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (_visual != null)
			{
				Destroy(_visual);
				_visual = null;
			}
		}

		// IInteraction INTERFACE

		string  IInteraction.Name        => "[F] Open | Stash";
		string  IInteraction.Description => OwnerName;
		Vector3 IInteraction.HUDPosition => transform.position + Vector3.up * 0.6f;
		bool    IInteraction.IsActive    => Object != null && Object.IsValid == true;

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			var interact = new GameObject("Interact");
			interact.layer = ObjectLayer.Interaction;
			interact.transform.SetParent(transform, false);

			var collider = interact.AddComponent<BoxCollider>();
			collider.size   = new Vector3(0.7f, 0.5f, 0.6f);
			collider.center = new Vector3(0.0f, 0.25f, 0.0f);
		}

		// PRIVATE METHODS

		private void BuildVisual()
		{
			_visual = new GameObject("Visual");
			_visual.transform.SetParent(transform, false);

			// Phase 10 (Logan, 9 Oct): the equipment case, scaled up, for now
			ArtSettings art = ArtSettings.Current;
			if (art != null && art.EquipmentCaseModel != null)
			{
				GameObject model = Instantiate(art.EquipmentCaseModel, _visual.transform, false);
				model.name = art.EquipmentCaseModel.name;
				model.transform.localScale = Vector3.one * art.LootBagScale;

				foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
				{
					Destroy(collider);
				}

				// The F box covers the case
				var box = transform.Find("Interact")?.GetComponent<BoxCollider>();
				if (box != null)
				{
					box.size   = new Vector3(0.82f, 0.38f, 0.58f) * art.LootBagScale;
					box.center = new Vector3(0.0f, 0.19f * art.LootBagScale, 0.0f);
				}

				if (Runner != null && Runner.Config.PeerMode != NetworkProjectConfig.PeerModes.Single)
				{
					Runner.AddVisibilityNodes(_visual);
				}
				return;
			}

			// No model: the placeholder, a dark sack with a tied top

			var bag = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			Destroy(bag.GetComponent<Collider>());
			bag.transform.SetParent(_visual.transform, false);
			bag.transform.localPosition = new Vector3(0.0f, 0.2f, 0.0f);
			bag.transform.localScale    = new Vector3(0.6f, 0.4f, 0.5f);

			var top = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			Destroy(top.GetComponent<Collider>());
			top.transform.SetParent(_visual.transform, false);
			top.transform.localPosition = new Vector3(0.0f, 0.43f, 0.0f);
			top.transform.localScale    = new Vector3(0.12f, 0.06f, 0.12f);

			if (_material == null)
			{
				_material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
				_material.SetColor("_BaseColor", new Color(0.22f, 0.24f, 0.16f));
				_material.SetFloat("_Smoothness", 0.1f);
			}

			bag.GetComponent<Renderer>().sharedMaterial = _material;
			top.GetComponent<Renderer>().sharedMaterial = _material;

			if (Runner != null && Runner.Config.PeerMode != NetworkProjectConfig.PeerModes.Single)
			{
				Runner.AddVisibilityNodes(_visual);
			}
		}
	}
}
