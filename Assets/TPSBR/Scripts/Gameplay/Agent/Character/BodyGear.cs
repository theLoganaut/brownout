namespace TPSBR
{
	using Fusion;
	using UnityEngine;
	using UnityEngine.SceneManagement;

	/// <summary>
	/// Phase 10 step 4: the soldier's built-in gear meshes (soldier.fbx children, switched off by Build Agent Body) shown from networked state
	/// on every peer: Armor_T1/T2/T3 from the armor tier (+ the helmet at tier 3), the backpack when carried, the drop suit in the drop.
	/// The drop suit's wings pop off when the wings come off near the ground (Logan, 9 Oct: no parachute): each wing flies off as a loose
	/// copy that tumbles to the ground and disappears. Bots wear BotSettings' colors on the armor plates and accents.
	/// Step 6: the grenade being thrown and the grappling gun show in the left hand (Logan's models from ArtSettings), on every peer.
	/// Added to every agent at runtime, so the agent prefabs stay untouched. Gear is switched by GameObject (Multipeer visibility).
	/// </summary>
	public sealed class BodyGear : MonoBehaviour
	{
		// CONSTANTS

		public static readonly string[] GearMeshes = { "Armor_T1", "Armor_T2", "Armor_T3", "Armor_T3_Helmet", "Backpack", "DropSuit" };

		private const string WING_LEFT      = "DropSuit_Wing_L";
		private const string WING_RIGHT     = "DropSuit_Wing_R";
		private const float  WING_OPEN_ANGLE = 80.0f; // Degrees out from hanging down the sides
		private const float  WING_OPEN_TIME  = 0.4f;
		private const string BODY_MESH      = "Soldier";
		private const string ARMOR_MATERIAL  = "SLD_Armor";
		private const string ACCENT_MATERIAL = "SLD_Accent";

		// PUBLIC MEMBERS

		/// <summary>Goes up whenever a gear mesh is switched or the colors change (GearVisuals refades Camouflage on a change).</summary>
		public int Version { get; private set; }

		// PRIVATE MEMBERS

		private Agent      _agent;
		private bool       _isReady;
		private GameObject _armorT1;
		private GameObject _armorT2;
		private GameObject _armorT3;
		private GameObject _helmet;
		private GameObject _backpack;
		private GameObject _dropSuit;
		private GameObject _wingLeft;
		private GameObject _wingRight;
		private SkinnedMeshRenderer _bodyMesh;
		private Material[] _playerMaterials;
		private bool       _wearsBotColors;
		private bool       _wingsWereOn;
		private float      _wingsOnTime;
		private Quaternion _wingLeftRest;
		private Quaternion _wingRightRest;
		private Transform  _leftHand;
		private Transform  _leftFingers;
		private readonly System.Collections.Generic.Dictionary<EEquipment, Transform> _held = new System.Collections.Generic.Dictionary<EEquipment, Transform>();

		private static Material _botArmor;
		private static Material _botAccent;

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_agent = GetComponent<Agent>();
		}

		private void LateUpdate()
		{
			if (_agent == null || _agent.Object == null || _agent.Object.IsValid == false)
				return;

			if (_isReady == false)
			{
				FindParts();
			}

			Health    health = _agent.Health;
			EMoveMode mode   = _agent.Character.CharacterController.Data.MoveMode;
			int       tier   = health.ArmorTier;
			bool      alive  = health.IsAlive;

			bool suitOn  = alive == true && (mode == EMoveMode.Wingsuit || mode == EMoveMode.WingsOff);
			bool wingsOn = alive == true && mode == EMoveMode.Wingsuit;

			Show(_armorT1,  tier == 1);
			Show(_armorT2,  tier == 2);
			Show(_armorT3,  tier == 3);
			Show(_helmet,   tier == 3);
			Show(_backpack, _agent.HasBackpack == true);
			Show(_dropSuit, suitOn);

			if (_wingsWereOn == true && wingsOn == false && mode == EMoveMode.WingsOff)
			{
				PopWing(_wingLeft,  -1.0f);
				PopWing(_wingRight,  1.0f);
			}

			if (wingsOn == true && _wingsWereOn == false)
			{
				_wingsOnTime = Time.time;
			}

			Show(_wingLeft,  wingsOn);
			Show(_wingRight, wingsOn);
			_wingsWereOn = wingsOn;

			if (wingsOn == true)
			{
				// They hang down the sides in the model; spread them out to glide, over a moment after the jump
				float open = WING_OPEN_ANGLE * Mathf.SmoothStep(0.0f, 1.0f, (Time.time - _wingsOnTime) / WING_OPEN_TIME);
				OpenWing(_wingLeft,  _wingLeftRest,  open);
				OpenWing(_wingRight, _wingRightRest, open);
			}

			if (_wearsBotColors != _agent.IsBot)
			{
				SetBotColors(_agent.IsBot);
			}

			UpdateHeldItem(alive);
		}

		/// <summary>
		/// The grenade from the press until it leaves the hand, the grappling gun from the press until the pull ends, in the left hand.
		/// </summary>
		private void UpdateHeldItem(bool alive)
		{
			AgentEquipment equipment = _agent.EquipmentActions;
			bool           busy      = alive == true && equipment != null && equipment.IsBusy == true;
			EEquipment     kind      = busy == true ? equipment.CurrentKind : EEquipment.Frag;
			bool           holding   = busy == true && (kind == EEquipment.Grapple || equipment.HasReleased == false);

			foreach (var pair in _held)
			{
				bool show = holding == true && pair.Key == kind;
				if (pair.Value.gameObject.activeSelf != show)
				{
					pair.Value.gameObject.SetActive(show);
				}

				if (show == false || _leftHand == null)
					continue;

				// In the palm: between the wrist and the knuckles. The grappling gun points where you look
				Vector3 palm = _leftFingers != null ? Vector3.Lerp(_leftHand.position, _leftFingers.position, 0.7f) : _leftHand.position;
				Quaternion rotation = kind == EEquipment.Grapple ? _agent.Character.CharacterController.Data.LookRotation : _leftHand.rotation;
				pair.Value.SetPositionAndRotation(palm, rotation);
			}
		}

		// PRIVATE METHODS

		private void FindParts()
		{
			_isReady   = true;
			_armorT1   = FindPart(GearMeshes[0]);
			_armorT2   = FindPart(GearMeshes[1]);
			_armorT3   = FindPart(GearMeshes[2]);
			_helmet    = FindPart(GearMeshes[3]);
			_backpack  = FindPart(GearMeshes[4]);
			_dropSuit  = FindPart(GearMeshes[5]);
			_wingLeft  = FindPart(WING_LEFT);
			_wingRight = FindPart(WING_RIGHT);
			_wingLeftRest  = _wingLeft  != null ? _wingLeft.transform.localRotation  : Quaternion.identity;
			_wingRightRest = _wingRight != null ? _wingRight.transform.localRotation : Quaternion.identity;

			// Step 6: held equipment, made once (FirstPersonBody hides it with the body in first person)
			Animator animator = FindBodyAnimator();
			_leftHand    = animator != null && animator.isHuman == true ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
			_leftFingers = animator != null && animator.isHuman == true ? animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal) : null;
			MakeHeldItems();

			// The Soldier prefab's root has the same name as the body mesh, so look for the renderer
			foreach (SkinnedMeshRenderer mesh in GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (mesh.name == BODY_MESH)
				{
					_bodyMesh = mesh;
					break;
				}
			}
		}

		private Animator FindBodyAnimator()
		{
			foreach (Animator animator in GetComponentsInChildren<Animator>(false))
			{
				if (animator.isHuman == true)
					return animator;
			}
			return null;
		}

		private void MakeHeldItems()
		{
			ArtSettings art = ArtSettings.Current;
			if (art == null || _leftHand == null)
				return;

			for (EEquipment kind = EEquipment.Frag; kind <= EEquipment.Grapple; ++kind)
			{
				GameObject model  = art.GetEquipmentModel(kind);
				MeshFilter filter = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
				if (filter == null)
					continue;

				var holder = new GameObject($"Held_{kind}").transform;
				holder.SetParent(_leftHand, false);
				holder.gameObject.layer = _leftHand.gameObject.layer;

				GameObject instance = Instantiate(model, holder, false);
				instance.transform.localPosition = kind == EEquipment.Grapple ? new Vector3(0.0f, 0.0f, -filter.sharedMesh.bounds.center.z) : -filter.sharedMesh.bounds.center;
				foreach (Transform node in instance.GetComponentsInChildren<Transform>(true))
				{
					node.gameObject.layer = holder.gameObject.layer;
				}
				foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
				{
					Destroy(collider);
				}

				holder.gameObject.SetActive(false);
				_held[kind] = holder;
			}

			NetworkRunner runner = _agent.Runner;
			if (_held.Count > 0 && runner != null && runner.Config.PeerMode != NetworkProjectConfig.PeerModes.Single)
			{
				foreach (var pair in _held)
				{
					runner.AddVisibilityNodes(pair.Value.gameObject);
				}
			}
		}

		private GameObject FindPart(string partName)
		{
			foreach (Transform child in GetComponentsInChildren<Transform>(true))
			{
				if (child.name == partName)
					return child.gameObject;
			}

			return null;
		}

		private void Show(GameObject part, bool show)
		{
			if (part == null || part.activeSelf == show)
				return;

			part.SetActive(show);
			++Version;
		}

		private void SetBotColors(bool isBot)
		{
			_wearsBotColors = isBot;

			if (_bodyMesh == null)
				return;

			if (_playerMaterials == null)
			{
				_playerMaterials = _bodyMesh.sharedMaterials;
			}

			Material[] materials = (Material[])_playerMaterials.Clone();

			if (isBot == true)
			{
				BotSettings settings = BotSettings.Current;

				for (int i = 0; i < materials.Length; ++i)
				{
					if (materials[i] == null)
						continue;

					if (materials[i].name == ARMOR_MATERIAL)
					{
						materials[i] = GetBotMaterial(ref _botArmor, materials[i], settings.SuitColor);
					}
					else if (materials[i].name == ACCENT_MATERIAL)
					{
						materials[i] = GetBotMaterial(ref _botAccent, materials[i], settings.AccentColor);
					}
				}
			}

			_bodyMesh.sharedMaterials = materials;
			++Version;
		}

		/// <summary>One shared copy for every bot, recolored when the setting changes.</summary>
		private static Material GetBotMaterial(ref Material copy, Material source, Color color)
		{
			if (copy == null)
			{
				copy = new Material(source) { name = source.name + " (Bot)" };
			}

			copy.SetColor("_BaseColor", color);
			return copy;
		}

		/// <summary>
		/// Swings a wing out from its rest pose by angle, around its top (the pivot): in the drop suit's space, from the way it hangs (pivot
		/// to the mesh's middle) toward its own side (the sign of its x). The suit is on the upper chest, so the wings move with the body.
		/// </summary>
		private static void OpenWing(GameObject wing, Quaternion rest, float angle)
		{
			if (wing == null)
				return;

			Transform  transform = wing.transform;
			MeshFilter filter    = wing.GetComponent<MeshFilter>();
			Vector3    hang      = rest * (filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.center : Vector3.down);
			Vector3    outward   = new Vector3(transform.localPosition.x >= 0.0f ? 1.0f : -1.0f, 0.0f, 0.0f);

			if (hang.sqrMagnitude < 0.000001f)
				return;

			Quaternion swing = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(hang, outward), angle);
			transform.localRotation = swing * rest;
		}

		/// <summary>A loose copy of the wing flies off sideways and up from where it was, tumbling; the real one is switched off.</summary>
		private void PopWing(GameObject wing, float side)
		{
			if (wing == null || wing.activeInHierarchy == false)
				return;

			var filter   = wing.GetComponent<MeshFilter>();
			var renderer = wing.GetComponent<MeshRenderer>();
			if (filter == null || renderer == null)
				return;

			var copy = new GameObject(wing.name + " (Popped)");
			copy.layer = wing.layer;
			SceneManager.MoveGameObjectToScene(copy, gameObject.scene);
			copy.transform.SetPositionAndRotation(wing.transform.position, wing.transform.rotation);
			copy.transform.localScale = wing.transform.lossyScale;

			copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
			var copyRenderer = copy.AddComponent<MeshRenderer>();
			copyRenderer.sharedMaterials   = renderer.sharedMaterials;
			copyRenderer.shadowCastingMode = renderer.shadowCastingMode;

			NetworkRunner runner = _agent.Runner;
			if (runner != null && runner.Config.PeerMode != NetworkProjectConfig.PeerModes.Single)
			{
				runner.AddVisibilityNodes(copy);
			}

			Vector3 velocity = _agent.Character.CharacterController.Data.RealVelocity;
			Vector3 right    = transform.right;

			var flying = copy.AddComponent<PoppedWing>();
			flying.Launch(runner != null ? runner.GetPhysicsScene() : default, velocity * 0.6f + right * (side * 3.5f) + Vector3.up * 4.0f, Random.onUnitSphere * Random.Range(240.0f, 540.0f));
		}

		// HELPERS

		/// <summary>A wing that came off: falls with gravity and air drag, tumbling, until it lies on the ground; gone after a few seconds.</summary>
		private sealed class PoppedWing : MonoBehaviour
		{
			private const float LIFETIME = 6.0f;
			private const float DRAG     = 0.6f;

			private PhysicsScene _physics;
			private Vector3      _velocity;
			private Vector3      _spin;
			private float        _age;
			private bool         _landed;

			public void Launch(PhysicsScene physics, Vector3 velocity, Vector3 spin)
			{
				_physics  = physics;
				_velocity = velocity;
				_spin     = spin;
			}

			private void Update()
			{
				float deltaTime = Time.deltaTime;

				_age += deltaTime;
				if (_age >= LIFETIME)
				{
					Destroy(gameObject);
					return;
				}

				if (_landed == true)
					return;

				_velocity += Physics.gravity * deltaTime;
				_velocity *= Mathf.Clamp01(1.0f - DRAG * deltaTime);

				Vector3 from = transform.position;
				Vector3 step = _velocity * deltaTime;

				if (_physics.IsValid() == true && step.sqrMagnitude > 0.000001f &&
				    _physics.Raycast(from, step.normalized, out RaycastHit hit, step.magnitude + 0.05f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
				{
					transform.position = hit.point + hit.normal * 0.03f;
					_landed = true;
					return;
				}

				transform.position = from + step;
				transform.rotation = Quaternion.Euler(_spin * deltaTime) * transform.rotation;
			}
		}
	}
}
