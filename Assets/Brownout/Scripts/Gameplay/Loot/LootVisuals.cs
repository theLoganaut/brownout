namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using Fusion;

	/// <summary>
	/// Builds the model of an item lying on the ground (phase 5a), locally on every peer. Guns use Logan's model with their attachments
	/// (GunModelParts), lying on their side; attachments with a model use it. Phase 10: healing, armor, ammo, the backpack, grenades,
	/// the grappling gun and gear items (the equipment case, small) use Logan's models from ArtSettings; an item without one (and the
	/// mags without their own model) keeps its colored block. Runtime renderers are registered for Multipeer visibility.
	/// </summary>
	public static class LootVisuals
	{
		// PRIVATE MEMBERS

		private static Mesh _cubeMesh;
		private static readonly int _centerId = Shader.PropertyToID("_CenterOS");
		private static readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();

		// PUBLIC METHODS

		/// <summary>Builds the model under parent (sitting on the parent's origin) and returns its bounds in the parent's space.</summary>
		public static GameObject Build(LootStack stack, Transform parent, NetworkRunner runner, out Bounds bounds, bool outline = true)
		{
			GameObject visual = new GameObject("Visual");
			visual.transform.SetParent(parent, false);

			switch (stack.Kind)
			{
				case EItemKind.Gun:        BuildGun(stack, visual.transform, runner); break;
				case EItemKind.Attachment: BuildAttachment(stack, visual.transform);  break;
				case EItemKind.Heal:       BuildHeal(stack, visual.transform);        break;
				case EItemKind.Armor:      BuildArmor(stack, visual.transform);       break;
				case EItemKind.Ammo:       BuildAmmo(stack, visual.transform);        break;
				case EItemKind.Backpack:   BuildBackpack(visual.transform); break;
				case EItemKind.Equipment:  BuildEquipment(stack, visual.transform); break;
				case EItemKind.Gear:       BuildGear(stack, visual.transform);      break;
			}

			// Nothing on an item may block shots or interaction casts: only the item's own interaction box is a collider
			foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
			{
				// The icon baker (phase 11a) builds these outside Play mode
				if (Application.isPlaying == true)
				{
					Object.Destroy(collider);
				}
				else
				{
					Object.DestroyImmediate(collider);
				}
			}

			// Sit it on the ground, centered
			bounds = GetBounds(visual.transform, parent);
			visual.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y - 0.01f, bounds.center.z);
			bounds.center -= new Vector3(bounds.center.x, bounds.min.y - 0.01f, bounds.center.z);

			if (outline == true)
			{
				AddOutline(visual);
			}

			RegisterVisibility(runner, visual);
			return visual;
		}

		/// <summary>Phase 11a: a copy of every mesh drawn with the outline material (ArtSettings.ItemOutline), so loot reads on any floor.</summary>
		private static void AddOutline(GameObject visual)
		{
			ArtSettings art      = ArtSettings.Current;
			Material    material = art != null ? art.ItemOutline : null;
			if (material == null)
				return;

			MeshFilter[] filters = visual.GetComponentsInChildren<MeshFilter>(false);
			for (int i = 0; i < filters.Length; ++i)
			{
				MeshFilter   filter   = filters[i];
				MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
				if (filter.sharedMesh == null || renderer == null || renderer.enabled == false)
					continue;

				var copy = new GameObject("Outline");
				copy.layer = filter.gameObject.layer;
				copy.transform.SetParent(filter.transform, false);
				copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

				var outline = copy.AddComponent<MeshRenderer>();
				var materials = new Material[Mathf.Max(1, filter.sharedMesh.subMeshCount)];
				for (int m = 0; m < materials.Length; ++m)
				{
					materials[m] = material;
				}
				outline.sharedMaterials      = materials;
				// The outline grows from the mesh's center on screen
				var block = new MaterialPropertyBlock();
				block.SetVector(_centerId, filter.sharedMesh.bounds.center);
				outline.SetPropertyBlock(block);
				outline.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
				outline.receiveShadows       = false;
				outline.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
				outline.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
			}
		}

		// PRIVATE METHODS

		private static void BuildGun(LootStack stack, Transform root, NetworkRunner runner)
		{
			GunData gun = ItemCatalog.GetGunData(stack.Index);
			if (gun == null || gun.Model == null)
			{
				AddBlock(root, new Vector3(0.08f, 0.18f, 0.80f), new Color(0.25f, 0.25f, 0.25f));
				return;
			}

			GameObject model = Object.Instantiate(gun.Model, root, false);
			model.name = gun.Model.name;
			model.transform.localPosition = Vector3.zero;
			model.transform.localRotation = Quaternion.identity;

			var parts = new GunModelParts(model.transform, null);
			parts.Apply(stack.Attachments);

			// On its side: turn it about its long axis
			Bounds  local = GetBounds(model.transform, root);
			Vector3 axis  = local.size.x > local.size.z ? Vector3.right : Vector3.forward;
			model.transform.localRotation = Quaternion.AngleAxis(90.0f, axis);
		}

		private static void BuildAttachment(LootStack stack, Transform root)
		{
			AttachmentData attachment = ItemCatalog.GetAttachment(stack);
			if (attachment != null && attachment.Model != null)
			{
				GameObject model = Object.Instantiate(attachment.Model, root, false);
				model.name = attachment.Model.name;
				model.transform.localPosition = Vector3.zero;
				model.transform.localRotation = Quaternion.identity;
				return;
			}

			// Extended Mag and Fast Mag have no model of their own: a mag-shaped block
			AddBlock(root, new Vector3(0.05f, 0.16f, 0.09f), new Color(0.20f, 0.20f, 0.22f));
			AddBlock(root, new Vector3(0.055f, 0.03f, 0.095f), attachment != null && attachment.UsesGunMagExt == true ? new Color(0.85f, 0.65f, 0.15f) : new Color(0.20f, 0.65f, 0.85f), new Vector3(0.0f, 0.065f, 0.0f));
		}

		private static void BuildHeal(LootStack stack, Transform root)
		{
			if (AddModel(root, ArtSettings.Get(ArtSettings.Current?.HealModels, stack.Index)) == true)
				return;

			switch ((EHealItem)stack.Index)
			{
				case EHealItem.Bandage:
					AddBlock(root, new Vector3(0.14f, 0.07f, 0.10f), new Color(0.92f, 0.88f, 0.78f));
					break;
				case EHealItem.MedKit:
					AddBlock(root, new Vector3(0.30f, 0.12f, 0.22f), new Color(0.92f, 0.92f, 0.92f));
					AddBlock(root, new Vector3(0.12f, 0.125f, 0.04f), new Color(0.85f, 0.12f, 0.12f));
					AddBlock(root, new Vector3(0.04f, 0.125f, 0.12f), new Color(0.85f, 0.12f, 0.12f));
					break;
				default:
					AddBlock(root, new Vector3(0.42f, 0.20f, 0.30f), new Color(0.80f, 0.10f, 0.10f));
					AddBlock(root, new Vector3(0.16f, 0.205f, 0.05f), Color.white);
					AddBlock(root, new Vector3(0.05f, 0.205f, 0.16f), Color.white);
					break;
			}
		}

		private static void BuildArmor(LootStack stack, Transform root)
		{
			// Lying on its back on the ground
			if (AddModel(root, ArtSettings.Get(ArtSettings.Current?.ArmorModels, stack.Index - 1), 1.0f, Quaternion.Euler(-90.0f, 0.0f, 0.0f)) == true)
				return;

			Color color;
			switch (stack.Index)
			{
				case 1:  color = new Color(0.50f, 0.55f, 0.42f); break;
				case 2:  color = new Color(0.25f, 0.40f, 0.75f); break;
				default: color = new Color(0.45f, 0.20f, 0.60f); break;
			}

			AddBlock(root, new Vector3(0.48f, 0.08f, 0.58f), color);
			AddBlock(root, new Vector3(0.30f, 0.09f, 0.12f), color * 0.7f, new Vector3(0.0f, 0.0f, 0.18f));
		}

		private static void BuildBackpack(Transform root)
		{
			if (AddModel(root, ArtSettings.Current?.BackpackModel) == true)
				return;

			AddBlock(root, new Vector3(0.40f, 0.25f, 0.50f), new Color(0.45f, 0.32f, 0.18f));
		}

		private static void BuildAmmo(LootStack stack, Transform root)
		{
			if (AddModel(root, ArtSettings.Get(ArtSettings.Current?.AmmoModels, stack.Index)) == true)
				return;

			Color color = LootSettings.Current.GetAmmoBoxColor((ECaliber)stack.Index);
			AddBlock(root, new Vector3(0.26f, 0.14f, 0.15f), color);
			AddBlock(root, new Vector3(0.27f, 0.03f, 0.155f), color * 0.6f, new Vector3(0.0f, 0.05f, 0.0f));
		}

		/// <summary>Phase 5b: a grenade is a small can with a darker cap; the grapple a long block with a hook end.</summary>
		private static void BuildEquipment(LootStack stack, Transform root)
		{
			// The grappling gun lies on its side like a gun
			ArtSettings art = ArtSettings.Current;
			if (AddModel(root, art != null ? art.GetEquipmentModel(stack.Equipment) : null, 1.0f, stack.Equipment == EEquipment.Grapple ? Quaternion.Euler(0.0f, 0.0f, 90.0f) : Quaternion.identity) == true)
				return;

			Color color = EquipmentSettings.Current.GetEquipmentColor(stack.Equipment);

			if (stack.Equipment == EEquipment.Grapple)
			{
				AddBlock(root, new Vector3(0.07f, 0.09f, 0.32f), color);
				AddBlock(root, new Vector3(0.12f, 0.04f, 0.04f), new Color(0.20f, 0.20f, 0.22f), new Vector3(0.0f, 0.0f, 0.17f));
				AddBlock(root, new Vector3(0.04f, 0.10f, 0.05f), new Color(0.20f, 0.20f, 0.22f), new Vector3(0.0f, -0.06f, -0.05f));
				return;
			}

			AddBlock(root, new Vector3(0.07f, 0.11f, 0.07f), color);
			AddBlock(root, new Vector3(0.05f, 0.03f, 0.05f), color * 0.55f, new Vector3(0.0f, 0.065f, 0.0f));
		}

		/// <summary>Phase 5b: a gear item is a flat colored box with a white stripe.</summary>
		private static void BuildGear(LootStack stack, Transform root)
		{
			ArtSettings art = ArtSettings.Current;
			if (art != null && AddModel(root, art.EquipmentCaseModel, art.GearCaseScale) == true)
				return;

			Color color = EquipmentSettings.Current.GetGearColor(stack.GearItem);
			AddBlock(root, new Vector3(0.20f, 0.06f, 0.14f), color);
			AddBlock(root, new Vector3(0.205f, 0.065f, 0.03f), Color.white);
		}

		/// <summary>Phase 10: Logan's model under root (scaled, turned); false when there's no model, so the caller draws its block.</summary>
		private static bool AddModel(Transform root, GameObject model, float scale = 1.0f, Quaternion rotation = default)
		{
			if (model == null)
				return false;

			GameObject instance = Object.Instantiate(model, root, false);
			instance.name = model.name;
			instance.transform.localPosition = Vector3.zero;
			instance.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
			instance.transform.localScale    = Vector3.one * scale;
			return true;
		}

		private static void AddBlock(Transform root, Vector3 size, Color color, Vector3 center = default)
		{
			if (_cubeMesh == null)
			{
				_cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
			}

			var block = new GameObject("Block");
			block.transform.SetParent(root, false);
			block.transform.localPosition = center;
			block.transform.localScale    = size;

			block.AddComponent<MeshFilter>().sharedMesh = _cubeMesh;
			var renderer = block.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = GetMaterial(color);
		}

		private static Material GetMaterial(Color color)
		{
			color.a = 1.0f;

			if (_materials.TryGetValue(color, out Material material) == true && material != null)
				return material;

			material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
			material.SetColor("_BaseColor", color);
			material.SetFloat("_Smoothness", 0.2f);
			_materials[color] = material;
			return material;
		}

		/// <summary>Bounds of every renderer under root, in space's local space.</summary>
		private static Bounds GetBounds(Transform root, Transform space)
		{
			bool   hasBounds = false;
			Bounds bounds    = new Bounds(Vector3.zero, Vector3.zero);

			foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
			{
				if (renderer.gameObject.activeInHierarchy == false && renderer.transform != root)
					continue;

				Bounds    local  = renderer.localBounds;
				Matrix4x4 matrix = space.worldToLocalMatrix * renderer.transform.localToWorldMatrix;

				for (int i = 0; i < 8; ++i)
				{
					Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
					Vector3 point  = matrix.MultiplyPoint3x4(corner);

					if (hasBounds == false)
					{
						bounds    = new Bounds(point, Vector3.zero);
						hasBounds = true;
					}
					else
					{
						bounds.Encapsulate(point);
					}
				}
			}

			return bounds;
		}

		/// <summary>Multipeer: renderers made at runtime must be registered, or every peer's copy would draw.</summary>
		private static void RegisterVisibility(NetworkRunner runner, GameObject instance)
		{
			if (runner != null && runner.Config.PeerMode != NetworkProjectConfig.PeerModes.Single)
			{
				runner.AddVisibilityNodes(instance);
			}
		}
	}
}
