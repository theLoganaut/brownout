#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Diagnostics;
using Fusion;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Test tool (phase 4 performance check): records frame times and how long the runner spends simulating its ticks each frame.
	/// Attached to the host's runner, so the simulation time is the server's tick time. Editor and development builds only.
	/// </summary>
	public sealed class PerformanceProbe : SimulationBehaviour, IBeforeAllTicks, IAfterAllTicks
	{
		// PUBLIC MEMBERS

		public bool IsRecording { get; private set; }

		public readonly List<float> FrameTimes = new List<float>(8192);
		public readonly List<float> TickTimes  = new List<float>(8192);

		// PRIVATE MEMBERS

		private readonly Stopwatch _stopwatch = new Stopwatch();

		// PUBLIC METHODS

		public static PerformanceProbe Attach(NetworkRunner runner)
		{
			var probe = runner.GetComponent<PerformanceProbe>();
			if (probe == null)
			{
				probe = runner.gameObject.AddComponent<PerformanceProbe>();
				runner.AddGlobal(probe);
			}

			return probe;
		}

		public void Begin()
		{
			FrameTimes.Clear();
			TickTimes.Clear();
			IsRecording = true;
		}

		public void End()
		{
			IsRecording = false;
		}

		// IBeforeAllTicks / IAfterAllTicks INTERFACE

		void IBeforeAllTicks.BeforeAllTicks(bool resimulation, int tickCount)
		{
			if (resimulation == false)
			{
				_stopwatch.Restart();
			}
		}

		void IAfterAllTicks.AfterAllTicks(bool resimulation, int tickCount)
		{
			if (resimulation == true || IsRecording == false || tickCount <= 0)
				return;

			float milliseconds = (float)_stopwatch.Elapsed.TotalMilliseconds;
			for (int i = 0; i < tickCount; ++i)
			{
				TickTimes.Add(milliseconds / tickCount);
			}
		}

		// MonoBehaviour INTERFACE

		private void Update()
		{
			if (IsRecording == true)
			{
				FrameTimes.Add(Time.unscaledDeltaTime);
			}
		}
	}
}
#endif
