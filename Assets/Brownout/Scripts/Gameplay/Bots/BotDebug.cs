using System.Collections.Generic;
using Fusion;

namespace TPSBR
{
	/// <summary>
	/// Server-side log of bot events (phase 9): SPAWN, STATE, SEE, FIRE, HIT, COVER, HEAL, LOOT, ZONE, STUCK, DOWNED, DEATH, each with its tick
	/// and time, for the Bot Check and Brownout → Test → Bots. Editor and development builds only.
	/// </summary>
	public static class BotDebug
	{
		// PUBLIC MEMBERS

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		public static readonly List<string> EventLog = new List<string>(4096);
		/// <summary>Also print each line to the console (off by default: 22 bots are chatty).</summary>
		public static bool LogToConsole;

		/// <summary>Test hooks (static, so every peer agrees): bots don't target players; bots stand still and do nothing.</summary>
		public static bool IgnorePlayers;
		public static bool Freeze;
		/// <summary>Bots aim and fire but don't move (the Bot Check's hit rates measure aim alone).</summary>
		public static bool HoldStill;
		/// <summary>Draw each bot's path, state and target in the Scene view.</summary>
		public static bool ShowDebug;
		/// <summary>Every bot's pose is updated every tick, however far it is (the Bot Check measures with and without the thinning).</summary>
		public static bool NoThinning;
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

			if (EventLog.Count >= 8000)
			{
				EventLog.RemoveRange(0, 2000);
			}

			EventLog.Add(entry);

			if (LogToConsole == true)
			{
				UnityEngine.Debug.Log($"[Bot] {entry}");
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
