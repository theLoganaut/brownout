namespace TPSBR
{
	using UnityEngine;
	using Fusion.Addons.KCC;

	/// <summary>
	/// Phase 10: full-body clips for the movement abilities (Logan's soldier takes): slide, mantle or vault, dash. Sits right above
	/// FullBody, so the reload, shoot and look layers still act on top (you can aim and fire while sliding).
	/// Mantle, vault and dash follow the movement's own timers, so the clip ends when the move does.
	/// </summary>
	public sealed class MovesLayer : ClipLayer
	{
		// PRIVATE MEMBERS

		[SerializeField, Tooltip("A mantle up to this height plays the vault clip, higher ones the climb.")]
		private float _vaultMaxHeight = 1.3f;
		[SerializeField]
		private float _fadeDuration = 0.12f;

		private KCC             _kcc;
		private SyncedClipState _slide;
		private SyncedClipState _mantle;
		private SyncedClipState _vault;
		private SyncedClipState _dash;

		// AnimationLayer INTERFACE

		protected override void OnInitialize()
		{
			_kcc    = Controller.GetComponentNoAlloc<KCC>();
			_slide  = Find("Slide");
			_mantle = Find("Mantle");
			_vault  = Find("Vault");
			_dash   = Find("Dash");
		}

		protected override void OnFixedUpdate()
		{
			KCCData data = _kcc.FixedData;

			switch (data.MoveMode)
			{
				case EMoveMode.Slide:
				case EMoveMode.SkipAir:
				{
					// In the air with the slide queued (C during a skip), the capsule is already slide height: the slide clip comes in then
					Show(data.Stance == EStance.Slide ? _slide : null, _fadeDuration);
					return;
				}
				case EMoveMode.Mantle:
				{
					float           height = data.MantleEnd.y - data.MantleStart.y;
					SyncedClipState clip   = height <= _vaultMaxHeight ? _vault : _mantle;

					Show(clip, 0.08f);
					clip.SetSyncedTime(GetSeconds(data.ModeStartTick) / Mathf.Max(0.05f, data.MantleDuration));
					return;
				}
				case EMoveMode.Dash:
				{
					Show(_dash, _fadeDuration);
					_dash.SetSyncedTime(GetSeconds(data.ModeStartTick) / Mathf.Max(0.05f, MovementSettings.Current.DashDuration));
					return;
				}
			}

			Show(null, _fadeDuration);
		}
	}
}
