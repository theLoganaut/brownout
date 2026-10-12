using System.Collections.Generic;
using Fusion;

namespace TPSBR
{
	/// <summary>
	/// Server-side log of health events (phase 4): HIT, ARMOR BROKE, HEAL, DOWNED, REVIVE, BLEED OUT, DEATH, TEAM OUT, FRIENDLY FIRE,
	/// each with its tick and time, for the Health Check and Brownout → Test → Health. Editor and development builds only.
	/// </summary>
	public static class HealthDebug
	{
		// PUBLIC MEMBERS

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		public static readonly List<string> EventLog = new List<string>(1024);
		/// <summary>Also print each line to the console.</summary>
		public static bool LogToConsole;
#endif

		// PUBLIC METHODS

		/// <summary>Logs on the server (never during a resimulation).</summary>
		public static void Log(NetworkRunner runner, string line)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (runner == null || runner.IsServer == false || runner.IsResimulation == true)
				return;

			Add(runner, line);
#endif
		}

		/// <summary>Logs when the caller has state authority (the server).</summary>
		public static void Log(NetworkRunner runner, bool hasStateAuthority, string line)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (hasStateAuthority == false || runner == null || runner.IsResimulation == true)
				return;

			Add(runner, line);
#endif
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		public static void Clear()
		{
			EventLog.Clear();
		}

		private static void Add(NetworkRunner runner, string line)
		{
			int tick = runner.Tick;
			string entry = $"{line} | tick {tick} time {tick * runner.DeltaTime * 1000.0f:F0} ms";

			if (EventLog.Count >= 4000)
			{
				EventLog.RemoveRange(0, 1000);
			}

			EventLog.Add(entry);

			if (LogToConsole == true)
			{
				UnityEngine.Debug.Log(entry);
			}
		}
#endif
	}
}
