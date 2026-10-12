#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.IO;
using System.Linq;
using Fusion;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Phase 9b (Logan's question 2: measure a development build): on the host, while a Battle Royale match is on, every 15 s writes the
	/// frame rate, the host's simulation time per tick, ticks per frame and how many bots are alive and thinned to
	/// Logs/MatchPerformance.txt (next to the project in the editor, next to the game's folder in a build). Editor and development builds.
	/// </summary>
	public static class MatchPerformanceLog
	{
		// CONSTANTS

		private const float INTERVAL = 15.0f;

		// PRIVATE MEMBERS

		private static PerformanceProbe _probe;
		private static float            _windowStart = -1.0f;
		private static string           _path;

		// PUBLIC METHODS

		/// <summary>Server, every tick of the match.</summary>
		public static void Tick(NetworkRunner runner, EMatchPhase phase)
		{
			if (runner == null || runner.IsServer == false || runner.IsResimulation == true)
				return;

			if (_probe == null || _probe.Runner != runner)
			{
				_probe = PerformanceProbe.Attach(runner);
				_probe.Begin();
				_windowStart = Time.realtimeSinceStartup;
				Write($"--- {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} {(Application.isEditor ? "editor" : "development build")}, {SystemInfo.processorType}, {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
				return;
			}

			if (Time.realtimeSinceStartup - _windowStart < INTERVAL)
				return;

			_probe.End();

			float frame  = _probe.FrameTimes.Count > 0 ? _probe.FrameTimes.Average() : 0.0f;
			float tick   = _probe.TickTimes.Count > 0 ? _probe.TickTimes.Average() : 0.0f;
			float ticks  = _probe.FrameTimes.Count > 0 ? _probe.TickTimes.Count / (float)_probe.FrameTimes.Count : 0.0f;
			int   bots   = 0;
			int   thin   = 0;
			for (int i = 0; i < BotBrain.All.Count; ++i)
			{
				BotBrain bot = BotBrain.All[i];
				if (bot.Agent != null && bot.Agent.Runner == runner && bot.Agent.Object != null && bot.Agent.Object.IsValid == true && bot.Agent.Health.IsAlive == true)
				{
					++bots;
					thin += bot.IsThinned == true ? 1 : 0;
				}
			}

			Write($"{runner.Tick * runner.DeltaTime,7:F0} s  {phase,-6}  {(frame > 0.0f ? 1.0f / frame : 0.0f),5:F1} fps  host simulation {tick,5:F2} ms per tick  {ticks:F1} ticks per frame  {bots} bots alive, {thin} thinned");

			_probe.Begin();
			_windowStart = Time.realtimeSinceStartup;
		}

		// PRIVATE METHODS

		private static void Write(string line)
		{
			try
			{
				if (_path == null)
				{
					string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
					Directory.CreateDirectory(folder);
					_path = Path.Combine(folder, "MatchPerformance.txt");
				}

				File.AppendAllText(_path, line + "\n");
			}
			catch (System.Exception exception)
			{
				Debug.LogWarning($"MatchPerformanceLog: {exception.Message}");
			}
		}
	}
}
#endif
