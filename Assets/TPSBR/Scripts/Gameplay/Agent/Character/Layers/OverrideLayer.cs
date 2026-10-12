namespace TPSBR
{
	using UnityEngine;
	using Fusion.Addons.KCC;

	/// <summary>
	/// Phase 10: full-body clips that replace everything else (the last layer): downed (going down, lying, crawling, getting up),
	/// reviving a teammate, the drop (wingsuit glide and dive, the fall after the wings pop off, the landing roll).
	/// </summary>
	public sealed class OverrideLayer : ClipLayer
	{
		// PRIVATE MEMBERS

		[SerializeField, Tooltip("Crawl clip above this speed (m/s), lying still below.")]
		private float _crawlSpeed = 0.3f;
		[SerializeField, Tooltip("Wingsuit: the dive clip when falling faster than this share of the speed.")]
		private float _diveVerticalShare = 0.7f;
		[SerializeField]
		private float _fadeDuration = 0.2f;

		private KCC             _kcc;
		private Agent           _agent;
		private SyncedClipState _downedEnter;
		private SyncedClipState _downedIdle;
		private SyncedClipState _downedCrawl;
		private SyncedClipState _downedGetUp;
		private SyncedClipState _revive;
		private SyncedClipState _skydive;
		private SyncedClipState _dive;
		private SyncedClipState _wingsRelease;
		private SyncedClipState _wingsOffFall;
		private SyncedClipState _landRoll;

		// Transitions seen by this peer (from networked state, so every peer sees the same ones)
		private bool _wasDowned;
		private int  _getUpTick;

		// PUBLIC MEMBERS

		public bool IsOverriding => HasActiveClip();

		// AnimationLayer INTERFACE

		protected override void OnInitialize()
		{
			_kcc          = Controller.GetComponentNoAlloc<KCC>();
			_agent        = Controller.GetComponentNoAlloc<Agent>();
			_downedEnter  = Find("DownedEnter");
			_downedIdle   = Find("DownedIdle");
			_downedCrawl  = Find("DownedCrawl");
			_downedGetUp  = Find("DownedGetUp");
			_revive       = Find("Revive");
			_skydive      = Find("Skydive");
			_dive         = Find("Dive");
			_wingsRelease = Find("ChuteRelease"); // State names kept from before phase 10's no-parachute change (Drop_Release, Drop_Fall)
			_wingsOffFall = Find("ChuteHang");
			_landRoll     = Find("LandRoll");
		}

		protected override void OnSpawned()
		{
			_wasDowned = false;
			_getUpTick = 0;
		}

		protected override void OnFixedUpdate()
		{
			KCCData data   = _kcc.FixedData;
			Health  health = _agent.Health;
			int     tick   = Controller.Runner.Tick;

			if (health.IsAlive == false)
			{
				_wasDowned = false;
				Show(null, _fadeDuration);
				return;
			}

			// Transitions
			if (_wasDowned == true && health.IsDowned == false)
			{
				_getUpTick = tick;
			}
			_wasDowned = health.IsDowned;

			// Downed
			if (health.IsDowned == true)
			{
				float downedTime = GetSeconds(health.DownedTick);
				if (downedTime < _downedEnter.Length)
				{
					Show(_downedEnter, 0.1f);
					_downedEnter.SetSyncedTime(downedTime / _downedEnter.Length);
				}
				else
				{
					Show(data.RealSpeed > _crawlSpeed ? _downedCrawl : _downedIdle, _fadeDuration);
				}
				return;
			}

			if (_getUpTick > 0 && GetSeconds(_getUpTick) < _downedGetUp.Length)
			{
				Show(_downedGetUp, 0.1f);
				_downedGetUp.SetSyncedTime(GetSeconds(_getUpTick) / _downedGetUp.Length);
				return;
			}

			// Reviving a teammate
			if (health.IsReviving == true)
			{
				Show(_revive, _fadeDuration);
				_revive.SetSyncedTime(health.ReviveProgress);
				return;
			}

			// The drop
			if (data.MoveMode == EMoveMode.Wingsuit)
			{
				Vector3 velocity = data.DropVelocity;
				bool    diving   = velocity.sqrMagnitude > 1.0f && -velocity.y > velocity.magnitude * _diveVerticalShare;
				Show(diving == true ? _dive : _skydive, 0.3f);
				return;
			}

			if (data.MoveMode == EMoveMode.WingsOff)
			{
				// The wings popped off: Drop_Release, then Drop_Fall if the ground is still further
				Show(GetSeconds(data.ModeStartTick) < _wingsRelease.Length ? _wingsRelease : _wingsOffFall, 0.15f);
				return;
			}

			if (data.MoveMode == EMoveMode.LandRoll)
			{
				// Held to the roll's game timer, so it ends with the roll
				Show(_landRoll, 0.1f);
				_landRoll.SetSyncedTime(Mathf.Clamp01(GetSeconds(data.ModeStartTick) / Mathf.Max(0.1f, MatchSettings.Current.LandRollTime)));
				return;
			}

			Show(null, _fadeDuration);
		}
	}
}
