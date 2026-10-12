namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;

	/// <summary>
	/// Phase 3c: the named points and default parts of one instance of Logan's gun model, and the attachment models mounted on it.
	/// Used by the first-person viewmodel and the third-person / dropped gun alike, so both show the same parts.
	///
	/// Guns and attachments only meet at named mount points (Attachments page): each attachment model is parented to its mount
	/// at zero offset. Placeholder names (Blender Pipeline Brief): `<Name>_OpticMount`, `_Muzzle` (barrel tip, also the muzzle
	/// mount), `_UnderbarrelMount`, `_StockMount`, `_GripMount`; default parts `_IronSights`, `_Mag` / `_MagExt`, `_Stock`, `_Grip`; points
	/// `_ADS_Eye`, `_LeftHand`, `_Laser`. Attachment models are kept once made, so swapping never piles up objects.
	/// </summary>
	public sealed class GunModelParts
	{
		// PUBLIC MEMBERS

		public readonly Transform Root;

		public Transform GunAdsEye   { get; private set; }
		public Transform GunMuzzle   { get; private set; }
		public Transform GunLeftHand { get; private set; }

		/// <summary>The optic's eye point, else the gun's.</summary>
		public Transform AdsEye      { get; private set; }
		/// <summary>The muzzle attachment's tip, else the gun's.</summary>
		public Transform Muzzle      { get; private set; }
		/// <summary>The foregrip's hand spot, else the gun's.</summary>
		public Transform LeftHand    { get; private set; }
		/// <summary>The Laser Sight's beam origin, or null.</summary>
		public Transform Laser       { get; private set; }

		public AttachmentData GetAttachment(EAttachmentSlot slot) => _active[(int)slot];
		public Transform      GetMount(EAttachmentSlot slot)      => _mounts[(int)slot];
		/// <summary>The model made for this attachment, or null if it hasn't been shown yet.</summary>
		public GameObject     GetInstance(AttachmentData attachment) => attachment != null && _instances.TryGetValue(attachment, out Instance instance) == true ? instance.Root : null;
		public GameObject     IronSights => _ironSights;
		public GameObject     Mag        => _mag;
		public GameObject     MagExt     => _magExt;
		public GameObject     Stock      => _stock;
		public GameObject     Grip       => _grip;
		/// <summary>Attachment models made so far (they're reused, so this stops growing).</summary>
		public int            InstanceCount => _instances.Count;

		// PRIVATE MEMBERS

		private readonly Transform[] _mounts = new Transform[AttachmentData.SlotCount];
		private readonly GameObject  _ironSights;
		private readonly GameObject  _mag;
		private readonly GameObject  _magExt;
		private readonly GameObject  _stock;
		private readonly GameObject  _grip;

		private readonly AttachmentData[] _active = new AttachmentData[AttachmentData.SlotCount];
		private readonly Dictionary<AttachmentData, Instance> _instances = new Dictionary<AttachmentData, Instance>();
		private readonly System.Action<GameObject> _onInstanceCreated;

		private long _appliedKey = -1;

		// CONSTRUCTORS

		/// <param name="onInstanceCreated">Called once per new attachment model (layer, shadows, per-peer visibility).</param>
		public GunModelParts(Transform modelRoot, System.Action<GameObject> onInstanceCreated)
		{
			Root               = modelRoot;
			_onInstanceCreated = onInstanceCreated;

			foreach (Transform node in modelRoot.GetComponentsInChildren<Transform>(true))
			{
				string nodeName = node.name;

				if      (nodeName.EndsWith("_ADS_Eye")          == true) { GunAdsEye   = node; }
				else if (nodeName.EndsWith("_Muzzle")           == true) { GunMuzzle   = node; }
				else if (nodeName.EndsWith("_LeftHand")         == true) { GunLeftHand = node; }
				else if (nodeName.EndsWith("_OpticMount")       == true) { _mounts[(int)EAttachmentSlot.Optic]       = node; }
				else if (nodeName.EndsWith("_UnderbarrelMount") == true) { _mounts[(int)EAttachmentSlot.Underbarrel] = node; }
				else if (nodeName.EndsWith("_StockMount")       == true) { _mounts[(int)EAttachmentSlot.Stock]       = node; }
				else if (nodeName.EndsWith("_GripMount")        == true) { _mounts[(int)EAttachmentSlot.Grip]        = node; }
				else if (nodeName.EndsWith("_IronSights")       == true) { _ironSights = node.gameObject; }
				else if (nodeName.EndsWith("_MagExt")           == true) { _magExt     = node.gameObject; }
				else if (nodeName.EndsWith("_Mag")              == true) { _mag        = node.gameObject; }
				else if (nodeName.EndsWith("_Stock")            == true) { _stock      = node.gameObject; }
				else if (nodeName.EndsWith("_Grip")             == true) { _grip       = node.gameObject; }
			}

			_mounts[(int)EAttachmentSlot.Muzzle] = GunMuzzle;

			Apply(null);
		}

		// PUBLIC METHODS

		/// <summary>Shows the attachments of a gun's synced IDs (BulletWeapon.AttachmentKey). Returns true when something changed.</summary>
		public bool Apply(long attachmentKey)
		{
			if (attachmentKey == _appliedKey)
				return false;

			GunplaySettings settings    = GunplaySettings.Current;
			var             attachments = new AttachmentData[AttachmentData.SlotCount];

			for (int i = 0; i < AttachmentData.SlotCount; ++i)
			{
				attachments[i] = settings.GetAttachment((int)((attachmentKey >> (8 * i)) & 0xFF));
			}

			Apply(attachments);
			_appliedKey = attachmentKey;
			return true;
		}

		/// <summary>Shows these attachments (one per slot, null = none) and hides the default parts they replace.</summary>
		public void Apply(AttachmentData[] attachments)
		{
			_appliedKey = -1;

			for (int i = 0; i < AttachmentData.SlotCount; ++i)
			{
				AttachmentData attachment = attachments != null && i < attachments.Length ? attachments[i] : null;

				// A slot the gun has no mount for can't show a model (the server refuses those anyway)
				if (attachment != null && attachment.Model != null && _mounts[i] == null)
				{
					Debug.LogWarning($"{Root.name}: no mount for the {(EAttachmentSlot)i} slot; {attachment.AttachmentName} not shown.", Root);
					attachment = null;
				}

				_active[i] = attachment;
			}

			foreach (var pair in _instances)
			{
				bool isActive = _active[(int)pair.Key.Slot] == pair.Key;
				if (pair.Value.Root.activeSelf != isActive)
				{
					pair.Value.Root.SetActive(isActive);
				}
			}

			AdsEye   = GunAdsEye;
			Muzzle   = GunMuzzle;
			LeftHand = GunLeftHand;
			Laser    = null;

			for (int i = 0; i < AttachmentData.SlotCount; ++i)
			{
				AttachmentData attachment = _active[i];
				if (attachment == null || attachment.Model == null)
					continue;

				Instance instance = GetInstance(attachment, _mounts[i]);
				if (instance.Root.activeSelf == false)
				{
					instance.Root.SetActive(true);
				}

				if (instance.AdsEye   != null) { AdsEye   = instance.AdsEye;   }
				if (instance.Muzzle   != null) { Muzzle   = instance.Muzzle;   }
				if (instance.LeftHand != null) { LeftHand = instance.LeftHand; }
				if (instance.Laser    != null) { Laser    = instance.Laser;    }
			}

			// Default parts hide when their slot is taken
			AttachmentData optic    = _active[(int)EAttachmentSlot.Optic];
			AttachmentData magazine = _active[(int)EAttachmentSlot.Magazine];
			AttachmentData stock    = _active[(int)EAttachmentSlot.Stock];
			AttachmentData grip     = _active[(int)EAttachmentSlot.Grip];
			bool           magExt   = magazine != null && magazine.UsesGunMagExt == true && _magExt != null;

			SetActive(_ironSights, optic == null || optic.Model == null);
			SetActive(_mag,        magExt == false);
			SetActive(_magExt,     magExt == true);
			SetActive(_stock,      stock == null || stock.Model == null);
			SetActive(_grip,       grip == null || grip.Model == null);
		}

		// PRIVATE METHODS

		private Instance GetInstance(AttachmentData attachment, Transform mount)
		{
			if (_instances.TryGetValue(attachment, out Instance instance) == true && instance.Root != null)
				return instance;

			GameObject model = Object.Instantiate(attachment.Model, mount, false);
			model.name = attachment.Model.name;
			model.transform.localPosition = Vector3.zero;
			model.transform.localRotation = Quaternion.identity;
			model.transform.localScale    = Vector3.one;

			instance = new Instance { Root = model };

			foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
			{
				string nodeName = node.name;

				if      (nodeName.EndsWith("_ADS_Eye")  == true) { instance.AdsEye   = node; }
				else if (nodeName.EndsWith("_Muzzle")   == true) { instance.Muzzle   = node; }
				else if (nodeName.EndsWith("_LeftHand") == true) { instance.LeftHand = node; }
				else if (nodeName.EndsWith("_Laser")    == true) { instance.Laser    = node; }
			}

			_onInstanceCreated?.Invoke(model);

			_instances[attachment] = instance;
			return instance;
		}

		private static void SetActive(GameObject gameObject, bool active)
		{
			if (gameObject != null && gameObject.activeSelf != active)
			{
				gameObject.SetActive(active);
			}
		}

		// HELPERS

		private sealed class Instance
		{
			public GameObject Root;
			public Transform  AdsEye;
			public Transform  Muzzle;
			public Transform  LeftHand;
			public Transform  Laser;
		}
	}
}
