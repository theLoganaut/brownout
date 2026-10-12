namespace TPSBR
{
	using System.Collections;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 11e Recoil Check: on the observed player (switch to the client first), every gun fires a string aimed (up to MaxShots,
	/// snipers SniperShots) and the view offset (the gun's Recoil: side, up in degrees) is logged at each shot. Measured guns pass
	/// when the spray's mean and peak up offset land within Tolerance of ads_up_mean_deg / ads_up_max_deg (× the gun's recoil
	/// multiplier); unmeasured guns (snipers) when each shot kicks UnmeasuredKickUp and is back down before the next. The settle
	/// after release is logged too. About 2 minutes in any scene with 2 peers (the Animation Arena turns you to its 25 m board).
	/// Read GetRecoilCheckLog() or Logs/RecoilCheck/RecoilCheck.txt.
	/// </summary>
	public static class BrownoutRecoilCheck
	{
		private const string MENU      = "Brownout/Test/Gunplay/";
		private const string LogFolder = "Logs/RecoilCheck";
		private const int    MaxShots    = 20;
		private const int    SniperShots = 3;
		private const float  Tolerance   = 0.25f;
		private const float  SmallOffset = 0.12f; // Below this (deg) a mean or peak is compared absolutely, not as a share

		private static readonly List<string>        _log     = new List<string>(256);
		private static readonly Stack<IEnumerator>  _routine = new Stack<IEnumerator>();
		private static string _waitFailed;

		[MenuItem(MENU + "Recoil Check", priority = 60)]
		private static void RecoilCheckMenu() => Debug.Log(RunRecoilCheck(null));

		// PUBLIC METHODS

		/// <summary>Every gun, or a comma-separated list ("Icarus,Omicron").</summary>
		public static string RunRecoilCheck(string onlyGuns)
		{
			if (Application.isPlaying == false)
				return "Enter Play mode (2 peers) first.";
			if (_routine.Count > 0)
				return "The Recoil Check is already running.";

			HashSet<string> only = string.IsNullOrEmpty(onlyGuns) == false ? new HashSet<string>(onlyGuns.Split(',').Select(t => t.Trim())) : null;
			List<BulletWeapon> guns = (GunplaySettings.Current.TestGuns ?? new Weapon[0]).OfType<BulletWeapon>().Where(t => t.GunData != null && (only == null || only.Contains(t.GunData.GunName))).ToList();
			if (guns.Count == 0)
				return "No guns to check (run Brownout → Data → Import Guns).";

			_log.Clear();
			_routine.Clear();
			_routine.Push(CheckRoutine(guns));
			EditorApplication.update -= UpdateRoutine;
			EditorApplication.update += UpdateRoutine;
			return $"Recoil Check started: {guns.Count} guns. Read GetRecoilCheckLog().";
		}

		public static string GetRecoilCheckLog()
		{
			return (_routine.Count > 0 ? "(Recoil Check running)\n" : "") + string.Join("\n", _log);
		}

		// PRIVATE METHODS

		private static IEnumerator CheckRoutine(List<BulletWeapon> guns)
		{
			if (FindPlayers(out Agent observed, out Agent server) == false)
			{
				Log("FAIL: needs 2 players in Play mode (ExtraPeers = 1).");
				yield break;
			}

			GunplaySettings settings = GunplaySettings.Current;
			Log($"Recoil Check: observed {observed.name} on {(observed.Runner.IsServer == true ? "the host" : "the client")}; spring {settings.RecoilSpring}/s, wander {settings.RecoilWanderUp}, settle min {settings.RecoilSettleMin} s, unmeasured kick {settings.UnmeasuredKickUp}°.");

			int passed = 0;
			var failed = new List<string>();

			try
			{
				AgentInput.DebugHoldAim = false;

				foreach (BulletWeapon prefab in guns)
				{
					GunData data = prefab.GunData;

					AgentInput.DebugHoldAim    = false;
					AgentInput.DebugHoldAttack = false;
					server.Weapons.DebugGiveWeapon(prefab);
					FaceArenaBoard(observed);

					yield return WaitFor(() => observed.Weapons.CurrentWeapon is BulletWeapon held && held.GunData == data && observed.Weapons.IsDrawing == false, 10.0f, $"{data.GunName} in hand");
					if (_waitFailed != null)
					{
						Log($"FAIL {data.GunName}: {_waitFailed}.");
						failed.Add(data.GunName);
						continue;
					}

					AgentInput.DebugHoldAim = true;
					yield return WaitFor(() => observed.Character.AimProgress >= 0.999f, 3.0f, "aimed");
					yield return WaitSeconds(0.5f);

					var gun = (BulletWeapon)observed.Weapons.CurrentWeapon;
					yield return data.HasMeasuredRecoil == true ? FireMeasured(gun, data, settings) : FireUnmeasured(gun, data, settings);
					bool   pass = _lastPass;
					string line = _lastLine;

					AgentInput.DebugHoldAim = false;

					Log($"  {(pass ? "PASS" : "FAIL")} {line}");
					if (pass == true) { ++passed; } else { failed.Add(data.GunName); }
				}
			}
			finally
			{
				AgentInput.DebugHoldAim    = false;
				AgentInput.DebugHoldAttack = false;
			}

			Log($"Recoil Check done: {passed}/{guns.Count} passed{(failed.Count > 0 ? ". Failed: " + string.Join(", ", failed) : "")}.");
			SaveLog();
		}

		private static bool   _lastPass;
		private static string _lastLine;

		/// <summary>A string aimed; the up offset at each shot against the measured mean and peak.</summary>
		private static IEnumerator FireMeasured(BulletWeapon gun, GunData data, GunplaySettings settings)
		{
			float scale  = gun.Stats.Recoil;
			int   shots  = Mathf.Min(MaxShots, gun.MagazineAmmo);
			var   atShot = new List<Vector2>(shots);

			yield return Fire(gun, data, shots, atShot, null);

			// Settle: from the last shot until the view is back within 0.05°
			double settleStart = EditorApplication.timeSinceStartup;
			while (gun.Recoil.magnitude > 0.05f && EditorApplication.timeSinceStartup - settleStart < 2.0)
			{
				yield return null;
			}
			float settle         = (float)(EditorApplication.timeSinceStartup - settleStart);
			float settleExpected = Mathf.Max(data.AdsSettleTime, settings.RecoilSettleMin) + Mathf.Max(data.FireInterval, data.BurstCycle) + 0.15f; // It starts once no shot came for a fire interval (burst guns: a burst cycle)

			float mean = atShot.Count > 0 ? atShot.Average(t => t.y) : 0.0f;
			float peak = atShot.Count > 0 ? atShot.Max(t => t.y) : 0.0f;
			float side = atShot.Count > 0 ? atShot.Average(t => t.x) : 0.0f;

			// The peak the game aims for: the measured pattern's highest offset, or the wander's top after it (the data's
			// ads_up_max_deg can come later in a mag than the measured first 12 shots)
			float patternPeak = data.AdsRecoilSteps.Max(t => t.y);
			float wanderPeak  = data.AdsUpMean + settings.RecoilWanderUp * Mathf.Max(0.0f, data.AdsUpMax - data.AdsUpMean);
			float wantMean    = data.AdsUpMean * scale;
			float wantPeak    = Mathf.Max(patternPeak, shots > data.AdsRecoilSteps.Length ? wanderPeak : 0.0f) * scale;

			bool meanOk   = Near(mean, wantMean);
			bool peakOk   = Near(peak, wantPeak);
			bool settleOk = settle <= settleExpected;

			_lastPass = atShot.Count >= Mathf.Min(shots, 3) && meanOk == true && peakOk == true && settleOk == true;
			_lastLine = $"{data.GunName}: {atShot.Count} shots, up mean {mean:F2}° (data {wantMean:F2}){(meanOk ? "" : " OFF")}, peak {peak:F2}° (expected {wantPeak:F2}, data max {data.AdsUpMax * scale:F2}){(peakOk ? "" : " OFF")}, side mean {side:F2}° (data {data.AdsSideMean * scale:F2}), settled in {settle:F2} s (allowed {settleExpected:F2}){(settleOk ? "" : " SLOW")}; per shot up: {string.Join(" ", atShot.Select(t => t.y.ToString("F2")))}";
		}

		/// <summary>Snipers: each shot kicks UnmeasuredKickUp and is back down before the next.</summary>
		private static IEnumerator FireUnmeasured(BulletWeapon gun, GunData data, GunplaySettings settings)
		{
			float scale  = gun.Stats.Recoil;
			int   shots  = Mathf.Min(SniperShots, gun.MagazineAmmo);
			var   atShot = new List<Vector2>(shots);
			var   peaks  = new List<float>(shots);

			yield return Fire(gun, data, shots, atShot, peaks);

			float want   = settings.UnmeasuredKickUp * scale;
			bool  kickOk = peaks.Count > 0 && peaks.All(t => Near(t, want));
			// The view at each shot after the first: back down from the last kick
			bool  downOk = atShot.Skip(1).All(t => Mathf.Abs(t.y) < 0.15f * want + 0.05f);

			_lastPass = atShot.Count >= Mathf.Min(shots, 2) && kickOk == true && downOk == true;
			_lastLine = $"{data.GunName} (no measured pattern): {atShot.Count} shots, kick peaks {string.Join(" ", peaks.Select(t => t.ToString("F2")))}° (want {want:F2}){(kickOk ? "" : " OFF")}, view at each shot {string.Join(" ", atShot.Select(t => t.y.ToString("F2")))}°{(downOk ? "" : " (not back down)")}";
		}

		/// <summary>
		/// Fires this many shots (held for auto and burst guns, pressed again and again for semi and bolt ones) and records the
		/// view offset at each shot (the frame its shot count goes up) and, with peaks, the largest up offset after each shot.
		/// </summary>
		private static IEnumerator Fire(BulletWeapon gun, GunData data, int shots, List<Vector2> atShot, List<float> peaks)
		{
			bool   pulse   = data.FireMode == EFireMode.Semi || data.FireMode == EFireMode.Bolt;
			int    start   = gun.ShotCount;
			int    last    = start;
			double end     = EditorApplication.timeSinceStartup + shots * Mathf.Max(data.FireInterval, data.BurstCycle / Mathf.Max(1, data.BurstCount)) + 3.0;
			float  peak    = 0.0f;
			Vector2 before = gun.Recoil;

			while (EditorApplication.timeSinceStartup < end)
			{
				int count = gun.ShotCount;

				// A prediction correction under lag can step the client's count back; each shot is recorded once
				if (count > last)
				{
					if (peaks != null && last != start)
					{
						peaks.Add(peak);
					}

					// The view at the moment of the shot is what the footage measured: the last frame before its own move
					atShot.Add(before);
					peak = gun.Recoil.y;
					last = count;
				}
				else
				{
					peak = Mathf.Max(peak, gun.Recoil.y);
				}

				if (count - start >= shots)
				{
					AgentInput.DebugHoldAttack = false;

					if (peaks == null)
						break;

					// Snipers: watch the last kick come back down
					if (peaks.Count >= shots - 1 && gun.Recoil.y < 0.05f && EditorApplication.timeSinceStartup > end - 2.0)
						break;
				}
				else
				{
					AgentInput.DebugHoldAttack = pulse == false || ((int)(EditorApplication.timeSinceStartup * 1000.0) / 60) % 2 == 0;
				}

				before = gun.Recoil;
				yield return null;
			}

			AgentInput.DebugHoldAttack = false;

			if (peaks != null && peaks.Count < atShot.Count)
			{
				peaks.Add(peak);
			}
		}

		private static bool Near(float value, float want)
		{
			if (Mathf.Abs(want) < SmallOffset)
				return Mathf.Abs(value - want) <= SmallOffset * Tolerance + 0.05f;

			return Mathf.Abs(value - want) <= Mathf.Abs(want) * Tolerance;
		}

		/// <summary>In the Animation Arena, every copy of the observed player looks at the 25 m board.</summary>
		private static void FaceArenaBoard(Agent observed)
		{
			AnimationArena arena = Object.FindAnyObjectByType<AnimationArena>();
			if (arena == null)
				return;

			foreach (Agent copy in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (copy.Object == null || copy.Object.IsValid == false || copy.Object.InputAuthority != observed.Object.InputAuthority)
					continue;

				var     kcc       = copy.Character.CharacterController;
				Vector3 direction = arena.RecoilTarget - copy.Character.GetCameraTransform(false).Position;

				kcc.FixedData.LookYaw    = kcc.RenderData.LookYaw   = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
				kcc.FixedData.LookPitch  = kcc.RenderData.LookPitch = -Mathf.Asin(direction.normalized.y) * Mathf.Rad2Deg;
			}
		}

		private static bool FindPlayers(out Agent observed, out Agent server)
		{
			observed = null;
			server   = null;

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object != null && agent.Object.IsValid == true && agent.IsObserved == true && agent.Context.HasInput == true)
				{
					observed = agent;
				}
			}

			if (observed == null)
				return false;

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Runner != null && agent.Runner.IsServer == true && agent.Object != null && agent.Object.IsValid == true && agent.Object.InputAuthority == observed.Object.InputAuthority)
				{
					server = agent;
				}
			}

			return server != null;
		}

		private static void SaveLog()
		{
			string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), LogFolder);
			Directory.CreateDirectory(folder);
			File.WriteAllText(Path.Combine(folder, "RecoilCheck.txt"), string.Join("\n", _log));
			Debug.Log(string.Join("\n", _log));
		}

		private static void Log(string line)
		{
			_log.Add(line);
		}

		// ROUTINE

		private static void UpdateRoutine()
		{
			if (Application.isPlaying == false)
			{
				Log("Recoil Check stopped: Play mode ended.");
				StopRoutine();
				return;
			}

			try
			{
				while (_routine.Count > 0)
				{
					IEnumerator top = _routine.Peek();
					if (top.MoveNext() == false)
					{
						_routine.Pop();
						continue;
					}

					if (top.Current is IEnumerator nested)
					{
						_routine.Push(nested);
						continue;
					}

					break;
				}
			}
			catch (System.Exception exception)
			{
				Log($"Recoil Check failed: {exception}");
				Debug.LogException(exception);
				StopRoutine();
				return;
			}

			if (_routine.Count == 0)
			{
				StopRoutine();
			}
		}

		private static void StopRoutine()
		{
			while (_routine.Count > 0)
			{
				(_routine.Pop() as System.IDisposable)?.Dispose();
			}

			EditorApplication.update -= UpdateRoutine;
			AgentInput.DebugHoldAim    = false;
			AgentInput.DebugHoldAttack = false;
		}

		private static IEnumerator WaitFor(System.Func<bool> condition, float timeout, string what)
		{
			_waitFailed = null;
			double end = EditorApplication.timeSinceStartup + timeout;

			while (condition() == false)
			{
				if (EditorApplication.timeSinceStartup > end)
				{
					_waitFailed = $"timed out waiting for {what}";
					yield break;
				}

				yield return null;
			}
		}

		private static IEnumerator WaitSeconds(float seconds)
		{
			double end = EditorApplication.timeSinceStartup + seconds;
			while (EditorApplication.timeSinceStartup < end)
			{
				yield return null;
			}
		}
	}
}
