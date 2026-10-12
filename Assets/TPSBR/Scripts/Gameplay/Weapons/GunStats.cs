using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// A gun's numbers with its attachments applied (Phase 3c): GunData values times each attachment's multipliers.
	/// Every peer computes the same result from the gun's synced attachment IDs, so tuning stays a data edit.
	/// </summary>
	public struct GunStats
	{
		public int   Magazine;
		public float ReloadTime;
		public float AmmoInTime;
		public float AdsTime;
		public float AdsZoom;
		public bool  Magnified;
		public float EyeDistance;
		public float BulletSpeed;
		public float FalloffRange;
		public float Recoil;
		public float HipSpread;
		public float AimMoveSpeed;
		public float AimIdleMotion;
		public float GunshotRange;
		public bool  HidesFlash;
		public bool  Suppressed;

		/// <summary>attachments: one per slot (null = none), indexed by EAttachmentSlot.</summary>
		public static GunStats Compute(GunData data, GunplaySettings settings, AttachmentData[] attachments)
		{
			var stats = new GunStats
			{
				Magazine      = data.Magazine,
				ReloadTime    = data.ReloadTime,
				AmmoInTime    = data.AmmoInTime,
				AdsTime       = data.AdsTime,
				AdsZoom       = data.AdsZoom > 1.0f ? data.AdsZoom : settings.GetIronSightZoom(data.Class),
				EyeDistance   = settings.IronSightEyeDistance,
				BulletSpeed   = data.BulletSpeed,
				FalloffRange  = 1.0f,
				Recoil        = 1.0f,
				HipSpread     = 1.0f,
				AimMoveSpeed  = 1.0f,
				AimIdleMotion = 1.0f,
				GunshotRange  = 1.0f,
			};

			float magazine = 1.0f;

			// Parts built into the model: a fixed scope, and stocks or foregrips that always count as fitted
			if (data.BuiltInScopeZoom > 1.0f)
			{
				stats.AdsZoom     = data.BuiltInScopeZoom;
				stats.Magnified   = data.BuiltInScopeMagnified;
				stats.EyeDistance = data.BuiltInScopeEyeDistance;
			}

			if (data.BuiltInAttachments != null)
			{
				for (int i = 0; i < data.BuiltInAttachments.Length; ++i)
				{
					Apply(ref stats, ref magazine, data.BuiltInAttachments[i]);
				}
			}

			if (attachments != null)
			{
				for (int i = 0; i < attachments.Length; ++i)
				{
					Apply(ref stats, ref magazine, attachments[i]);
				}
			}

			// Rounded up: 35 becomes 39, 5 becomes 6 (the small epsilon keeps 30 x 1.0 at 30)
			stats.Magazine = Mathf.CeilToInt(data.Magazine * magazine - 0.0001f);

			return stats;
		}

		private static void Apply(ref GunStats stats, ref float magazine, AttachmentData attachment)
		{
			if (attachment == null)
				return;

			if (attachment.Slot == EAttachmentSlot.Optic)
			{
				if (attachment.Zoom > 1.0f)
				{
					stats.AdsZoom = attachment.Zoom;
				}

				stats.Magnified   = attachment.Magnified;
				stats.EyeDistance = attachment.EyeDistance;
			}

			magazine            *= attachment.Magazine;
			stats.ReloadTime    *= attachment.ReloadTime;
			stats.AmmoInTime    *= attachment.ReloadTime;
			stats.AdsTime       *= attachment.AdsTime;
			stats.BulletSpeed   *= attachment.BulletSpeed;
			stats.FalloffRange  *= attachment.FalloffRange;
			stats.Recoil        *= attachment.Recoil;
			stats.HipSpread     *= attachment.HipSpread;
			stats.AimMoveSpeed  *= attachment.AimMoveSpeed;
			stats.AimIdleMotion *= attachment.AimIdleMotion;
			stats.GunshotRange  *= attachment.GunshotRange;
			stats.HidesFlash    |= attachment.HidesFlash;
			stats.Suppressed    |= attachment.Suppressed;
		}
	}
}
