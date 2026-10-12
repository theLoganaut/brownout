using Fusion;
using UnityEngine;
using DG.Tweening;

namespace TPSBR
{
	public sealed class AgentSenses : NetworkBehaviour
	{
		[Networked, HideInInspector]
		public float  EyesFlashValue { get; set; }

		/// <summary>Concussion (phase 5b): ticks it started and ends, and its strength (the falloff at your distance).</summary>
		[Networked, HideInInspector]
		public int    ConcussStartTick { get; set; }
		[Networked, HideInInspector]
		public int    ConcussEndTick   { get; set; }
		[Networked, HideInInspector]
		public float  ConcussStrength  { get; set; }

		public bool   IsConcussed => ConcussEndTick > 0 && Runner != null && Runner.Tick < ConcussEndTick;

		[SerializeField]
		private Ease  _eyesFlashFalloff;

		private float _eyesFlashStartValue;
		private int   _eyesFlashStartTick;
		private int   _eyesFlashEndTick;

		public void SetEyesFlash(float value, float duration, float falloffDelay)
		{
			if (HasStateAuthority == false)
				return;

			if (falloffDelay > duration - 0.1f)
			{
				falloffDelay = duration - 0.1f;
			}

			_eyesFlashStartTick = Runner.Tick + (int)(falloffDelay / Runner.DeltaTime);
			_eyesFlashEndTick   = _eyesFlashStartTick + (int)(duration / Runner.DeltaTime);

			_eyesFlashStartValue = value;
			EyesFlashValue       = value;
		}

		/// <summary>Server: dizzy for duration seconds at this strength (0..1). A stronger or longer one replaces a weaker one.</summary>
		public void SetConcussion(float strength, float duration)
		{
			if (HasStateAuthority == false || strength <= 0.0f || duration <= 0.0f)
				return;

			int endTick = Runner.Tick + Mathf.CeilToInt(duration / Runner.DeltaTime);
			if (IsConcussed == true && endTick <= ConcussEndTick && strength <= ConcussStrength)
				return;

			ConcussStartTick = Runner.Tick;
			ConcussEndTick   = endTick;
			ConcussStrength  = Mathf.Clamp01(strength);
		}

		/// <summary>0..1: how dizzy, fading out over the concussion (render time when render is true).</summary>
		public float GetConcussionValue(bool render)
		{
			if (Runner == null || ConcussEndTick <= ConcussStartTick)
				return 0.0f;

			float now      = render == true ? (Object.IsProxy == true ? Runner.RemoteRenderTime : Runner.LocalRenderTime) / Runner.DeltaTime : Runner.Tick;
			float progress = (now - ConcussStartTick) / (ConcussEndTick - ConcussStartTick);
			if (progress < 0.0f || progress >= 1.0f)
				return 0.0f;

			return ConcussStrength * (1.0f - progress * progress);
		}

		/// <summary>
		/// Look and movement speed multiplier while concussed (Logan, 5 Oct): ConcussionSlow at full strength for ConcussionSlowTime,
		/// easing back to normal by the end. 1 when not concussed.
		/// </summary>
		public float GetConcussionSlow()
		{
			if (Runner == null || Object == null || IsConcussed == false)
				return 1.0f;

			EquipmentSettings settings = EquipmentSettings.Current;

			float elapsed  = (Runner.Tick - ConcussStartTick) * Runner.DeltaTime;
			float total    = (ConcussEndTick - ConcussStartTick) * Runner.DeltaTime;
			float slowTime = Mathf.Min(settings.ConcussionSlowTime, total);
			float slow     = Mathf.Lerp(1.0f, settings.ConcussionSlow, ConcussStrength);

			if (elapsed <= slowTime)
				return slow;

			return Mathf.Lerp(slow, 1.0f, (elapsed - slowTime) / Mathf.Max(0.01f, total - slowTime));
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false)
				return;

			var currentTick = Runner.Tick;
			if (currentTick >= _eyesFlashEndTick)
			{
				EyesFlashValue = 0f;
				return;
			}

			var progress   = (currentTick - _eyesFlashStartTick) / (float)(_eyesFlashEndTick - _eyesFlashStartTick);
			EyesFlashValue = Mathf.Lerp(_eyesFlashStartValue, 0f, DOVirtual.EasedValue(0f, 1f, progress, _eyesFlashFalloff));
		}
	}
}
