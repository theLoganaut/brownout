using System.Collections.Generic;
using Fusion;

namespace TPSBR
{
	/// <summary>
	/// Server-side log of match events (phase 7): START, HELI, JUMP, CHUTE, LAND, ZONE, EDGE, TEAM OUT, PLACED, VICTORY, each with its
	/// tick and time, for the Match Check and Brownout → Test → Match. Editor and development builds only.
	/// </summary>
	public static class MatchDebug
	{
		// PUBLIC MEMBERS

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		public static readonly List<string> EventLog = new List<string>(1024);
		/// <summary>Also print each line to the console.</summary>
		public static bool LogToConsole = true;
#endif

		// PUBLIC METHODS

		/// <summary>Logs on the server (never during a resimulation).</summary>
		public static void Log(NetworkRunner runner, string line)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (runner == null || runner.IsServer == false || runner.IsResimulation == true)
				return;

			int    tick  = runner.Tick;
			string entry = $"{line} | tick {tick} time {tick * runner.DeltaTime:F2} s";

			if (EventLog.Count >= 4000)
			{
				EventLog.RemoveRange(0, 1000);
			}

			EventLog.Add(entry);

			if (LogToConsole == true)
			{
				UnityEngine.Debug.Log($"[Match] {entry}");
			}
#endif
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		public static void Clear()
		{
			EventLog.Clear();
		}
#endif
	}
}
