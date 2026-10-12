using System.Collections.Generic;
using Fusion;

namespace TPSBR
{
	/// <summary>
	/// Server-side log of loot events (phase 5a): SPAWN, PICKUP, EQUIP, SWAP, STORE, DROP, ATTACH, STASH, REFUSED, each with its tick
	/// and time, for the Loot Check and Brownout → Test → Loot. Editor and development builds only.
	/// </summary>
	public static class LootDebug
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

			int    tick  = runner.Tick;
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
