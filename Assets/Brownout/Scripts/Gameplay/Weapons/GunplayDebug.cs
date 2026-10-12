using System.Collections.Generic;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Phase 3a test tools (editor and development builds): toggles and the server's shot and TTK logs.
	/// Read through Brownout → Test or BrownoutTestTools from the Unity MCP. Static, so in Multipeer every peer shares them.
	/// </summary>
	public static class GunplayDebug
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>No hip or ADS spread (drop checks).</summary>
		public static bool ZeroSpread;
		/// <summary>No camera kick (TTK checks at range).</summary>
		public static bool NoRecoil;
		/// <summary>Treat iron sights as a magnified optic, so scope sway and hold breath can be tested before optics exist (3c).</summary>
		public static bool ScopedIrons;

		public static readonly List<string> ShotLog = new List<string>(1024);
		public static readonly List<string> TtkLog  = new List<string>(64);

		private static readonly Dictionary<int, float> _firstHitTimes = new Dictionary<int, float>();

		public static void Clear()
		{
			ShotLog.Clear();
			TtkLog.Clear();
			_firstHitTimes.Clear();
		}

		public static void LogFire(string gun, int tick, float fireTime, Vector3 origin, Vector3 direction, int magazineAfter, float speed)
		{
			Add(ShotLog, $"FIRE {gun} tick {tick} time {fireTime * 1000.0f:F1} ms origin {origin.ToString("F3")} dir {direction.ToString("F4")} mag {magazineAfter} speed {speed:F1} m/s");
		}

		public static void LogImpact(string gun, int tick, float fireTime, Vector3 origin, Vector3 point, string target, string part, float damage)
		{
			float distance = Vector3.Distance(origin, point);
			Add(ShotLog, $"HIT {gun} tick {tick} fired {fireTime * 1000.0f:F1} ms point {point.ToString("F3")} dist {distance:F2} m target {target} part {part} damage {damage:F2}");
		}

		public static void LogEvent(string text)
		{
			Add(ShotLog, text);
		}

		public static void LogPunch(int tick, string target, float damage)
		{
			Add(ShotLog, $"PUNCH tick {tick} target {target} damage {damage:F2}");
		}

		/// <summary>
		/// TTK = fire time of the killing shot minus fire time of the first shot that hit a full-health target.
		/// </summary>
		public static void TrackDamage(int victimID, string victimName, float healthBefore, float maxHealth, float fireTime, bool isFatal, string gun)
		{
			if (healthBefore >= maxHealth - 0.01f || _firstHitTimes.ContainsKey(victimID) == false)
			{
				_firstHitTimes[victimID] = fireTime;
			}

			if (isFatal == true)
			{
				float ttk = fireTime - _firstHitTimes[victimID];
				Add(TtkLog, $"TTK {gun} -> {victimName}: {ttk * 1000.0f:F1} ms");
				_firstHitTimes.Remove(victimID);
			}
		}

		private static void Add(List<string> log, string line)
		{
			if (log.Count >= 4000)
			{
				log.RemoveRange(0, 1000);
			}

			log.Add(line);
		}
#endif
	}
}
