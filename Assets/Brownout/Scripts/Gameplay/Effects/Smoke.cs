using Fusion;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Smoke cloud. Brownout (phase 5b): size and timing from EquipmentSettings: it grows to SmokeDiameter over SmokeGrowTime after it pops,
	/// lasts SmokeDuration, then stops emitting and fades over SmokeFadeTime. It only blocks sight (bots reading it come in phase 9).
	/// </summary>
	public sealed class Smoke : NetworkBehaviour
	{
		// PRIVATE MEMBERS

		[Networked]
		private NetworkBool _finished { get; set; }
		[Networked]
		private int         _startTick { get; set; }

		// The sample's values, kept so the prefab is untouched; EquipmentSettings is used instead
#pragma warning disable 0414
		[SerializeField]
		private float      _radius;
		[SerializeField]
		private float      _duration;
		[SerializeField]
		private float      _despawnDelay;
#pragma warning restore 0414
		[SerializeField]
		private ParticleSystem _effect;

		private TickTimer _timer;

		// NetworkBehaviour INTERFACE

		public override void CopyBackingFieldsToState(bool firstTime)
		{
			InvokeWeavedCode();

			_effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		}

		public override void Spawned()
		{
			if (HasStateAuthority == true)
			{
				EquipmentSettings settings = EquipmentSettings.Current;

				_timer     = TickTimer.CreateFromSeconds(Runner, settings.SmokeGrowTime + settings.SmokeDuration);
				_finished  = false;
				_startTick = Runner.Tick;

				EquipmentDebug.Log(Runner, $"DETONATE smoke ({Object.InputAuthority}) popped at {transform.position.ToString("F2")}: {settings.SmokeDiameter:F1} m across, full in {settings.SmokeGrowTime:F1} s, lasts {settings.SmokeDuration:F0} s");
			}
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false)
				return;

			if (_timer.Expired(Runner) == true)
			{
				if (_finished == false)
				{
					_finished = true;
					_timer = TickTimer.CreateFromSeconds(Runner, EquipmentSettings.Current.SmokeFadeTime + 1.0f);
					EquipmentDebug.Log(Runner, $"DETONATE smoke ({Object.InputAuthority}) fading after {(Runner.Tick - _startTick) * Runner.DeltaTime:F2} s");
				}
				else
				{
					Runner.Despawn(Object);
				}
			}
		}

		public override void Render()
		{
			EquipmentSettings settings = EquipmentSettings.Current;

			float now     = Object.IsProxy == true ? Runner.RemoteRenderTime : Runner.LocalRenderTime;
			float elapsed = Mathf.Max(0.0f, now - _startTick * Runner.DeltaTime);
			float grow    = settings.SmokeGrowTime > 0.0f ? Mathf.Clamp01(elapsed / settings.SmokeGrowTime) : 1.0f;

			_effect.transform.localScale = Vector3.one * settings.SmokeDiameter * Mathf.Lerp(0.3f, 1.0f, Mathf.SmoothStep(0.0f, 1.0f, grow));

			if (_finished == false && _effect.isPlaying == false)
			{
				_effect.Play(true);
			}
			else if (_finished == true && _effect.isPlaying == true)
			{
				_effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
			}
		}
	}
}
