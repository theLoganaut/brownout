namespace TPSBR
{
	using UnityEngine;
	using Fusion;

	/// <summary>
	/// Phase 3c: the attachments on the third-person and dropped gun, built locally on every peer from the gun's synced IDs.
	/// The fire point (flash, third-person tracers) moves to the muzzle attachment's tip, and the left-hand target follows
	/// a foregrip's hand spot, so other players see the hand on the grip (CharacterAnimationController.SnapWeapon()).
	/// Also draws the Laser Sight's beam: seen by everyone, while hip-firing, hidden while aiming.
	/// Added to every Brownout gun prefab by Brownout → Data → Import Guns.
	/// </summary>
	[DefaultExecutionOrder(-100)]
	public sealed class WeaponAttachmentVisuals : MonoBehaviour
	{
		// PUBLIC MEMBERS

		public GunModelParts Parts => _parts;
		public LineRenderer  LaserBeam => _laserBeam;
		/// <summary>The fire point and left-hand target as the importer left them (no attachments).</summary>
		public Vector3       DefaultFireLocal     => _defaultFireLocal;
		public Vector3       DefaultLeftHandLocal => _defaultLeftHandLocal;

		// PRIVATE MEMBERS

		[SerializeField, Tooltip("Logan's model instance under Visuals (set by the gun importer).")]
		private Transform _model;
		[SerializeField]
		private float     _laserRange = 30.0f;
		[SerializeField]
		private float     _laserWidth = 0.004f;
		[SerializeField]
		private Color     _laserColor = new Color(1.0f, 0.12f, 0.08f, 1.0f);

		private BulletWeapon  _weapon;
		private GunModelParts _parts;
		private Vector3       _defaultFireLocal;
		private Vector3       _defaultLeftHandLocal;
		private LineRenderer  _laserBeam;
		private Viewmodel     _ownerViewmodel;

		private static Material _laserMaterial;

		// PUBLIC METHODS

		/// <summary>Applies the synced attachments now (normally done every LateUpdate).</summary>
		public void Refresh()
		{
			if (_weapon == null || _parts == null)
				return;

			if (_parts.Apply(_weapon.AttachmentKey) == true)
			{
				PlaceFireAndHand();
			}
		}

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_weapon = GetComponent<BulletWeapon>();

			if (_model == null || _weapon == null)
				return;

			Transform fireTransform = _weapon.FireTransform;
			if (fireTransform != null)
			{
				_defaultFireLocal = fireTransform.localPosition;
			}

			if (_weapon.LeftHandTarget != null)
			{
				_defaultLeftHandLocal = _weapon.LeftHandTarget.localPosition;
			}

			_parts = new GunModelParts(_model, OnInstanceCreated);
		}

		private void LateUpdate()
		{
			if (_parts == null || _weapon.Object == null || _weapon.Object.IsValid == false)
				return;

			Refresh();
			UpdateLaser();
		}

		private void OnDisable()
		{
			SetLaserVisible(false);
		}

		// PRIVATE METHODS

		private void PlaceFireAndHand()
		{
			Transform fireTransform = _weapon.FireTransform;
			if (fireTransform != null)
			{
				fireTransform.localPosition = _defaultFireLocal;

				// The importer put the fire point on the gun's own tip; a muzzle attachment moves it to its tip
				if (_parts.Muzzle != null && _parts.Muzzle != _parts.GunMuzzle)
				{
					fireTransform.position = _parts.Muzzle.position;
				}
			}

			Transform leftHandTarget = _weapon.LeftHandTarget;
			if (leftHandTarget != null)
			{
				leftHandTarget.localPosition = _defaultLeftHandLocal;

				// The target is the hand bone beside the handguard (see the importer), so it moves by the grip's offset from the gun's spot
				if (_parts.LeftHand != null && _parts.GunLeftHand != null && _parts.LeftHand != _parts.GunLeftHand)
				{
					Transform parent = leftHandTarget.parent;
					Vector3   offset = parent.InverseTransformPoint(_parts.LeftHand.position) - parent.InverseTransformPoint(_parts.GunLeftHand.position);
					leftHandTarget.localPosition = _defaultLeftHandLocal + offset;
				}
			}
		}

		private void OnInstanceCreated(GameObject instance)
		{
			// Same layer as the gun model, so the gun's cameras and culling treat it the same
			int layer = _model.gameObject.layer;
			foreach (Transform node in instance.GetComponentsInChildren<Transform>(true))
			{
				node.gameObject.layer = layer;
			}

			RegisterVisibility(instance);
		}

		/// <summary>
		/// Multipeer: renderers made at runtime must be registered, or every peer's copy would draw. Attachment models are only
		/// made in LateUpdate after the gun is spawned, so the runner is known.
		/// </summary>
		private void RegisterVisibility(GameObject instance)
		{
			NetworkRunner runner = _weapon.Runner;
			if (runner != null && runner.Config.PeerMode != NetworkProjectConfig.PeerModes.Single)
			{
				runner.AddVisibilityNodes(instance);
			}
		}

		private void UpdateLaser()
		{
			Transform laser = _parts.Laser;
			Character owner = _weapon.Character;

			bool show = laser != null && laser.gameObject.activeInHierarchy == true && _weapon.IsArmed == true && owner != null && owner.Agent.Health.IsAlive == true && owner.CharacterController.Data.Aim == false;
			if (show == false)
			{
				SetLaserVisible(false);
				return;
			}

			if (_laserBeam == null)
			{
				CreateLaserBeam();
			}

			Vector3 start     = laser.position;
			Vector3 direction = laser.forward;

			// In first person the beam leaves the viewmodel's laser (as drawn on screen) toward where the player looks
			if (_ownerViewmodel == null || _ownerViewmodel.gameObject != owner.gameObject)
			{
				_ownerViewmodel = owner.GetComponent<Viewmodel>();
			}

			if (_ownerViewmodel != null && _ownerViewmodel.IsVisible == true && _ownerViewmodel.TryGetWorldPosition(_ownerViewmodel.Laser, out Vector3 viewStart) == true)
			{
				Transform eye = owner.Agent.Context.Camera.transform;
				Vector3   aim = eye.position + eye.forward * _laserRange;

				if (_weapon.Runner.GetPhysicsScene().Raycast(eye.position, eye.forward, out RaycastHit eyeHit, _laserRange, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
				{
					aim = eyeHit.point;
				}

				start     = viewStart;
				direction = (aim - start).normalized;
			}

			Vector3 end = start + direction * _laserRange;
			if (_weapon.Runner.GetPhysicsScene().Raycast(start, direction, out RaycastHit hit, _laserRange, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
			{
				end = hit.point;
			}

			SetLaserVisible(true);
			_laserBeam.SetPosition(0, start);
			_laserBeam.SetPosition(1, end);
		}

		private void CreateLaserBeam()
		{
			var beamObject = new GameObject("LaserBeam");
			beamObject.transform.SetParent(transform, false);
			beamObject.layer = gameObject.layer;

			_laserBeam = beamObject.AddComponent<LineRenderer>();
			_laserBeam.useWorldSpace     = true;
			_laserBeam.positionCount     = 2;
			_laserBeam.startWidth        = _laserWidth;
			_laserBeam.endWidth          = _laserWidth;
			_laserBeam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			_laserBeam.receiveShadows    = false;
			_laserBeam.sharedMaterial    = GetLaserMaterial(_laserColor);
			beamObject.SetActive(false);

			RegisterVisibility(beamObject);
		}

		// The GameObject is switched, not the renderer: Multipeer visibility owns Renderer.enabled
		private void SetLaserVisible(bool visible)
		{
			if (_laserBeam != null && _laserBeam.gameObject.activeSelf != visible)
			{
				_laserBeam.gameObject.SetActive(visible);
			}
		}

		private static Material GetLaserMaterial(Color color)
		{
			if (_laserMaterial == null)
			{
				_laserMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
				_laserMaterial.SetColor("_BaseColor", color);
			}

			return _laserMaterial;
		}
	}
}
