using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// The six attachment slots (Attachments page; Grip added 9 Oct). The value is the slot's index in a gun's attachment IDs.
	/// </summary>
	public enum EAttachmentSlot
	{
		Optic       = 0,
		Muzzle      = 1,
		Underbarrel = 2,
		Magazine    = 3,
		Stock       = 4,
		Grip        = 5,
	}

	/// <summary>
	/// One attachment's numbers, written by Brownout → Data → Import Guns from the attachments section of
	/// References/brownout_guns.json. Effects are multipliers on the gun's own values (1 = no change).
	/// Re-running the import overwrites these assets, so tune the JSON file, not the assets.
	/// </summary>
	[CreateAssetMenu(fileName = "AttachmentData", menuName = "TPSBR/Attachment Data")]
	public sealed class AttachmentData : ScriptableObject
	{
		public const int SlotCount = 6;

		[Header("Identity")]
		public string          AttachmentName;
		public EAttachmentSlot Slot;

		[Header("Model")]
		[Tooltip("Logan's model (Assets/Brownout/Art/Attachments/), parented to the gun's mount at zero offset. None for Fast Mag.")]
		public GameObject      Model;
		[Tooltip("Shows the gun's own <Name>_MagExt instead of a separate model (Extended Mag).")]
		public bool            UsesGunMagExt;
		[Tooltip("Finger pose for the left hand while it holds this attachment (used from phase 10).")]
		public string          LeftHandPose;

		[Header("Optic")]
		[Tooltip("Zoom as a multiplier on the player's FOV; 0 = the gun's iron-sight zoom.")]
		public float           Zoom;
		[Tooltip("Magnified: scope sway and hold breath apply.")]
		public bool            Magnified;
		[Tooltip("How far behind the optic's _ADS_Eye point the eye sits when aimed (m).")]
		public float           EyeDistance;

		[Header("Effects (multipliers)")]
		public float           AdsTime = 1.0f;
		[Tooltip("The viewmodel's sway and bob while aimed (cosmetic).")]
		public float           AimIdleMotion = 1.0f;
		public float           BulletSpeed = 1.0f;
		[Tooltip("Both falloff distances.")]
		public float           FalloffRange = 1.0f;
		[Tooltip("ADS and hip kick.")]
		public float           Recoil = 1.0f;
		public float           HipSpread = 1.0f;
		[Tooltip("Mag size, rounded up.")]
		public float           Magazine = 1.0f;
		[Tooltip("Reload and ammo-in times.")]
		public float           ReloadTime = 1.0f;
		public float           AimMoveSpeed = 1.0f;
		[Tooltip("How far the gunshot is heard.")]
		public float           GunshotRange = 1.0f;
		public bool            HidesFlash;
		[Tooltip("Hidden from Sixth Sense (phase 5b) and the minimap later.")]
		public bool            Suppressed;
	}
}
