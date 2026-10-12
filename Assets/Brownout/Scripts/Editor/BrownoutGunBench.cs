namespace TPSBR
{
	using System.Collections;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text;
	using Fusion;
	using Fusion.Addons.KCC;
	using UnityEditor;
	using UnityEditor.SceneManagement;
	using UnityEngine;
	using UnityEngine.Rendering;

	/// <summary>
	/// Phase 3c Gun Bench (Attachments page, "Gun Bench (how we prove it)"):
	/// Brownout → Setup → Build Gun Bench makes the test scene; Brownout → Test → Gun Bench Check runs every gun × attachment pair
	/// (view, muzzle, swap, network, mount, hand, stats) and then measures the effects on Icarus; Brownout → Test → Gun Bench
	/// Contact Sheets writes one image per gun to Logs/GunBench/ for the by-eye mount and hand checks.
	/// Run the check and sheets in Play mode with 2 peers; switch to the client first (Numpad 2) so the network check is real.
	/// </summary>
	public static class BrownoutGunBench
	{
		// CONSTANTS

		public const string BenchScene   = "Assets/Scenes/GunBench.unity";
		public const string BenchMapID   = "Map.GunBench";
		public const string SheetFolder  = "Logs/GunBench";

		/// <summary>Where the shooter stands (facing +Z, the target 25 m out) and where the second player watches from.</summary>
		public static readonly Vector3 BenchSpot = new Vector3(0.0f, 0.0f, 0.0f);
		public static readonly Vector3 WatchSpot = new Vector3(3.0f, 0.0f, 4.0f);
		public const float TargetDistance = 25.0f;

		private const string SETUP_MENU = "Brownout/Setup/";
		private const string TEST_MENU  = "Brownout/Test/";

		private const float  MaxSightErrorPx = 2.0f;
		private const float  PositionTolerance = 0.001f;
		private const int    SwapCycles = 10;
		private const int    RecoilShots = 6;

		// MENU

		[MenuItem(SETUP_MENU + "Build Gun Bench", priority = 3)]
		private static void BuildGunBenchMenu()
		{
			if (File.Exists(BenchScene) == true && EditorUtility.DisplayDialog("Gun Bench", "GunBench.unity exists. Rebuild it from the Template scene?", "Rebuild", "Cancel") == false)
				return;

			Debug.Log(BuildGunBench());
		}

		[MenuItem(TEST_MENU + "Gun Bench Check", priority = 36)]
		private static void GunBenchCheckMenu() => Debug.Log(RunGunBenchCheck(null, true));

		[MenuItem(TEST_MENU + "Gun Bench Check", true)]
		private static bool ValidateGunBenchCheck() => Application.isPlaying;

		[MenuItem(TEST_MENU + "Gun Bench Contact Sheets", priority = 37)]
		private static void ContactSheetsMenu() => Debug.Log(MakeContactSheets(null));

		[MenuItem(TEST_MENU + "Gun Bench Contact Sheets", true)]
		private static bool ValidateContactSheets() => Application.isPlaying;

		[MenuItem(TEST_MENU + "Attachments/Strip All (F6)", priority = 60)]
		private static void StripMenu() => Debug.Log(GunBench.Strip());

		[MenuItem(TEST_MENU + "Attachments/Next Optic (F1)", priority = 61)]       private static void NextOptic()       => Debug.Log(GunBench.CycleSlot(EAttachmentSlot.Optic));
		[MenuItem(TEST_MENU + "Attachments/Next Muzzle (F2)", priority = 62)]      private static void NextMuzzle()      => Debug.Log(GunBench.CycleSlot(EAttachmentSlot.Muzzle));
		[MenuItem(TEST_MENU + "Attachments/Next Underbarrel (F3)", priority = 63)] private static void NextUnderbarrel() => Debug.Log(GunBench.CycleSlot(EAttachmentSlot.Underbarrel));
		[MenuItem(TEST_MENU + "Attachments/Next Magazine (F4)", priority = 64)]    private static void NextMagazine()    => Debug.Log(GunBench.CycleSlot(EAttachmentSlot.Magazine));
		[MenuItem(TEST_MENU + "Attachments/Next Stock (F5)", priority = 65)]       private static void NextStock()       => Debug.Log(GunBench.CycleSlot(EAttachmentSlot.Stock));
		[MenuItem(TEST_MENU + "Attachments/Next Grip (F8)", priority = 66)]        private static void NextGrip()        => Debug.Log(GunBench.CycleSlot(EAttachmentSlot.Grip));

		[MenuItem(TEST_MENU + "Attachments/Strip All (F6)", true)]
		private static bool ValidateAttachmentMenu() => Application.isPlaying;

		// SCENE

		/// <summary>
		/// Copies the Template scene to GunBench.unity: a floor, a 25 m target wall, a side wall for the laser, the Gun Bench keys,
		/// spawn points at the bench, 2 peers. Registers it like the Movement Gym.
		/// </summary>
		public static string BuildGunBench()
		{
			if (EditorApplication.isPlaying == true)
				return "Stop Play mode first.";

			EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

			if (AssetDatabase.CopyAsset(BrownoutMovementSetup.TemplateScene, BenchScene) == false)
				return $"Couldn't copy {BrownoutMovementSetup.TemplateScene} to {BenchScene}.";

			var scene     = EditorSceneManager.OpenScene(BenchScene, OpenSceneMode.Single);
			var root      = new GameObject("GunBench").transform;
			var materials = new BrownoutMovementSetup.Materials();

			BrownoutMovementSetup.Solid(root, "Bench Floor", new Vector3(0.0f, -0.05f, 10.0f), new Vector3(16.0f, 0.1f, 40.0f), materials.Strip);
			BrownoutMovementSetup.Visual(root, "Firing Line", new Vector3(0.0f, 0.01f, 0.6f), new Vector3(4.0f, 0.012f, 0.1f), materials.MarkerMajor);
			BrownoutMovementSetup.Solid(root, "Target 25 m", new Vector3(0.0f, 2.0f, TargetDistance + 0.15f), new Vector3(6.0f, 4.0f, 0.3f), materials.Grid);
			BrownoutMovementSetup.Visual(root, "Eye Line 25 m", new Vector3(0.0f, 1.70f, TargetDistance - 0.005f), new Vector3(6.0f, 0.01f, 0.01f), materials.Fail);
			BrownoutMovementSetup.Visual(root, "Center 25 m", new Vector3(0.0f, 2.0f, TargetDistance - 0.005f), new Vector3(0.01f, 4.0f, 0.01f), materials.Fail);
			BrownoutMovementSetup.Solid(root, "Side Wall", new Vector3(-6.0f, 1.5f, 10.0f), new Vector3(0.3f, 3.0f, 20.0f), materials.Frame);

			var keys = new GameObject("GunBenchKeys");
			keys.transform.SetParent(root, false);
			keys.AddComponent<GunBenchKeys>();

			// Spawn points: the bench and the watch spot (facing the bench)
			var spawnPoints = Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
			for (int i = 0; i < spawnPoints.Length; ++i)
			{
				bool watch = i % 2 == 1;
				spawnPoints[i].transform.position = (watch == true ? WatchSpot : BenchSpot) + new Vector3(0.0f, 0.0f, -(i / 2) * 1.5f);
				spawnPoints[i].transform.rotation = watch == true ? Quaternion.Euler(0.0f, -90.0f, 0.0f) : Quaternion.identity;
			}

			BrownoutMovementSetup.SetExtraPeers(1);

			EditorSceneManager.MarkSceneDirty(scene);
			EditorSceneManager.SaveScene(scene);

			string registered = BrownoutMovementSetup.RegisterScene(BenchScene, BenchMapID, "Gun Bench", "Phase 3c test scene: every gun with every attachment (keys , . F1-F6, F8 grip).");
			return $"Built {BenchScene}. {registered}";
		}

		// CHECK

		private static readonly List<string> _log = new List<string>(512);
		private static readonly Stack<IEnumerator> _routine = new Stack<IEnumerator>();
		private static string _waitFailed;
		private static string _routineName;

		/// <summary>
		/// Runs the Gun Bench check for every gun (or a comma-separated list) and, with measureEffects, the measured effects on Icarus.
		/// About 3–5 minutes for everything. Read the result with GetGunBenchLog().
		/// </summary>
		public static string RunGunBenchCheck(string onlyGuns, bool measureEffects)
		{
			if (Application.isPlaying == false)
				return "Enter Play mode (Gun Bench, 2 peers) first.";
			if (_routine.Count > 0)
				return $"{_routineName} is already running.";

			List<BulletWeapon> guns = GetGuns(onlyGuns);
			if (guns.Count == 0)
				return "No guns to check (run Brownout → Data → Import Guns).";

			_log.Clear();
			StartRoutine("Gun Bench Check", CheckRoutine(guns, measureEffects));
			return $"Gun Bench Check started: {guns.Count} guns{(measureEffects == true ? " + measured effects" : "")}. Read GetGunBenchLog().";
		}

		public static string GetGunBenchLog()
		{
			return (_routine.Count > 0 ? $"({_routineName} running)\n" : "") + string.Join("\n", _log);
		}

		private static IEnumerator CheckRoutine(List<BulletWeapon> guns, bool measureEffects)
		{
			if (FindPlayers(out Agent observed, out Agent server, out _) == false)
			{
				Log("FAIL: needs 2 players in Play mode (Gun Bench, ExtraPeers = 1).");
				yield break;
			}

			Log($"Gun Bench Check: observed {observed.name} on {(observed.Runner.IsServer == true ? "the host" : "the client")} (switch to the client, Numpad 2, for a real network check).");

			PlaceAtBench(observed);
			yield return WaitSeconds(1.5f); // The lagged client settles after a teleport

			int pairs  = 0;
			int passed = 0;
			var failedPairs = new List<string>();

			try
			{
				Viewmodel.DebugFreezeMotion = true;
				AgentInput.DebugHoldAim     = true;

				foreach (BulletWeapon prefab in guns)
				{
					GunData data = prefab.GunData;

					server.Weapons.DebugGiveWeapon(prefab);
					yield return WaitFor(() => IsGunReady(observed, data, true), 10.0f, $"{data.GunName} in hand and aimed");
					if (_waitFailed != null)
					{
						Log($"FAIL {data.GunName}: {_waitFailed}.");
						failedPairs.Add($"{data.GunName} (not ready)");
						continue;
					}

					var serverGun = (BulletWeapon)server.Weapons.CurrentWeapon;
					Strip(serverGun);
					yield return WaitFor(() => AllCopiesHaveKey(serverGun, 0), 5.0f, "stripped gun synced");
					yield return null;
					yield return null;

					GunStats baseStats = serverGun.Stats;
					var      baseSigs  = new Dictionary<GunModelParts, string>();
					foreach (GunModelParts parts in GetAllParts(serverGun, observed))
					{
						baseSigs[parts] = ActiveSignature(parts.Root);
					}

					Log($"{data.GunName}: irons view {SightError(observed, baseStats.AdsZoom):F2} px (max {MaxSightErrorPx} px).");

					for (int slotIndex = 0; slotIndex < AttachmentData.SlotCount; ++slotIndex)
					{
						var slot = (EAttachmentSlot)slotIndex;

						foreach (AttachmentData attachment in GunBench.GetOptions(data, slot))
						{
							if (attachment == null)
								continue;

							++pairs;
							var problems = new List<string>();

							if (serverGun.SetAttachment(slot, attachment, out string setResult) == false)
							{
								problems.Add($"refused: {setResult}");
							}

							long key = serverGun.AttachmentKey;
							yield return WaitFor(() => AllCopiesHaveKey(serverGun, key) && GetViewmodelParts(observed) != null && GetViewmodelParts(observed).GetAttachment(slot) == attachment, 5.0f, "attachment synced to every copy");
							if (_waitFailed != null)
							{
								problems.Add(_waitFailed);
							}

							yield return null;
							yield return null;

							string checks = CheckPair(observed, serverGun, slot, attachment, baseStats, problems);

							// Swap: 10 on/off cycles on every copy's parts, and on the server's stats
							CheckSwaps(observed, serverGun, slot, attachment, baseStats, problems);

							// Off again: every copy back to the gun's own parts and stats
							serverGun.SetAttachment(slot, null, out _);
							yield return WaitFor(() => AllCopiesHaveKey(serverGun, 0), 5.0f, "removal synced");
							yield return null;
							yield return null;

							foreach (GunModelParts parts in GetAllParts(serverGun, observed))
							{
								if (baseSigs.TryGetValue(parts, out string baseSig) == true && ActiveSignature(parts.Root) != baseSig)
								{
									problems.Add($"leftover parts on {parts.Root.name} after removal");
								}
							}

							foreach (BulletWeapon copy in FindCopies(serverGun))
							{
								if (StatsEqual(copy.Stats, baseStats, out string difference) == false)
								{
									problems.Add($"{RunnerName(copy)} stats not back to base ({difference})");
								}
							}

							if (problems.Count == 0)
							{
								++passed;
								Log($"  PASS {data.GunName} + {attachment.AttachmentName}: {checks}");
							}
							else
							{
								failedPairs.Add($"{data.GunName} + {attachment.AttachmentName}");
								Log($"  FAIL {data.GunName} + {attachment.AttachmentName}: {string.Join("; ", problems)} ({checks})");
							}
						}
					}
				}

				Log($"Pairs: {passed}/{pairs} pass.{(failedPairs.Count > 0 ? $" Failed: {string.Join(", ", failedPairs)}" : "")}");
			}
			finally
			{
				Viewmodel.DebugFreezeMotion = false;
				AgentInput.DebugHoldAim     = false;
			}

			if (measureEffects == true)
			{
				yield return EffectsRoutine();
			}

			Log("Gun Bench Check done.");
		}

		/// <summary>Mount, view, muzzle, hand, network and stats for one gun with one attachment on. Returns a short summary.</summary>
		private static string CheckPair(Agent observed, BulletWeapon serverGun, EAttachmentSlot slot, AttachmentData attachment, GunStats baseStats, List<string> problems)
		{
			GunModelParts viewParts = GetViewmodelParts(observed);
			var           summary   = new StringBuilder();

			// Mount: on every copy, the model sits on its mount at zero offset and the default part it replaces is hidden
			foreach (GunModelParts parts in GetAllParts(serverGun, observed))
			{
				CheckMount(parts, slot, attachment, problems);
			}

			// View: the active sight's line on the screen center
			float sightError = SightError(observed, serverGun.Stats.AdsZoom);
			summary.Append($"view {sightError:F2} px");
			if (sightError > MaxSightErrorPx)
			{
				problems.Add($"view {sightError:F2} px off center");
			}

			if (slot == EAttachmentSlot.Optic && viewParts != null && viewParts.AdsEye == viewParts.GunAdsEye)
			{
				problems.Add("aiming through the irons, not the optic");
			}

			// Muzzle: first-person flash and tracer start, third-person fire point
			Viewmodel viewmodel = observed.GetComponent<Viewmodel>();
			if (viewParts != null)
			{
				Transform expected = viewParts.Muzzle;
				if (slot == EAttachmentSlot.Muzzle && (expected == viewParts.GunMuzzle || expected.IsChildOf(viewParts.GetInstance(attachment).transform) == false))
				{
					problems.Add("first-person muzzle isn't the attachment's tip");
				}

				if (viewmodel.Muzzle != expected)
				{
					problems.Add("viewmodel flash/tracer point isn't the active muzzle");
				}
			}

			float worstFire = 0.0f;
			foreach (BulletWeapon copy in FindCopies(serverGun))
			{
				var visuals = copy.GetComponent<WeaponAttachmentVisuals>();
				if (visuals == null || visuals.Parts == null)
				{
					problems.Add($"{RunnerName(copy)}: no attachment visuals");
					continue;
				}

				worstFire = Mathf.Max(worstFire, Vector3.Distance(copy.FireTransform.position, visuals.Parts.Muzzle.position));
			}

			summary.Append($", fire point {worstFire * 1000.0f:F2} mm");
			if (worstFire > PositionTolerance)
			{
				problems.Add($"third-person fire point {worstFire * 1000.0f:F1} mm from the muzzle tip");
			}

			// Hand: the first-person arm reaches the active hand spot; third-person targets follow the grip
			if (viewParts != null && viewParts.LeftHand != null)
			{
				float handError = Vector3.Distance(viewmodel.LeftHandPosition, viewParts.LeftHand.position);
				if (handError > PositionTolerance)
				{
					problems.Add($"first-person left hand {handError * 1000.0f:F1} mm off its spot");
				}

				if (slot == EAttachmentSlot.Underbarrel && attachment.Model != null && attachment.AttachmentName == "Foregrip" && viewParts.LeftHand == viewParts.GunLeftHand)
				{
					problems.Add("first-person left hand not on the foregrip");
				}
			}

			// Network: every copy shows the same parts, puts the hand target and fire point in the same place, and has the same stats
			BulletWeapon reference = null;
			foreach (BulletWeapon copy in FindCopies(serverGun))
			{
				var visuals = copy.GetComponent<WeaponAttachmentVisuals>();
				if (visuals == null || visuals.Parts == null)
					continue;

				if (visuals.Parts.GetAttachment(slot) != attachment)
				{
					problems.Add($"{RunnerName(copy)} shows {(visuals.Parts.GetAttachment(slot) != null ? visuals.Parts.GetAttachment(slot).AttachmentName : "nothing")}");
				}

				if (StatsEqual(copy.Stats, serverGun.Stats, out string difference) == false)
				{
					problems.Add($"{RunnerName(copy)} stats differ from the server ({difference})");
				}

				if (reference == null)
				{
					reference = copy;
					continue;
				}

				float handDifference = Vector3.Distance(copy.LeftHandTarget.localPosition, reference.LeftHandTarget.localPosition);
				float fireDifference = Vector3.Distance(copy.FireTransform.localPosition, reference.FireTransform.localPosition);
				if (handDifference > PositionTolerance || fireDifference > PositionTolerance)
				{
					problems.Add($"{RunnerName(copy)} vs {RunnerName(reference)}: hand target {handDifference * 1000.0f:F1} mm, fire point {fireDifference * 1000.0f:F1} mm apart");
				}
			}

			// The foregrip's third-person hand target moves by the grip's offset from the gun's spot
			var serverVisuals = serverGun.GetComponent<WeaponAttachmentVisuals>();
			if (serverVisuals != null && serverVisuals.Parts != null && serverVisuals.Parts.LeftHand != null && serverVisuals.Parts.GunLeftHand != null)
			{
				Transform parent   = serverGun.LeftHandTarget.parent;
				Vector3   offset   = parent.InverseTransformPoint(serverVisuals.Parts.LeftHand.position) - parent.InverseTransformPoint(serverVisuals.Parts.GunLeftHand.position);
				Vector3   expected = serverVisuals.DefaultLeftHandLocal + offset;
				float     error    = Vector3.Distance(serverGun.LeftHandTarget.localPosition, expected);
				if (error > PositionTolerance)
				{
					problems.Add($"third-person hand target {error * 1000.0f:F1} mm from the grip");
				}

				if (offset.sqrMagnitude > 0.000001f)
				{
					summary.Append($", hand moved {offset.magnitude * 100.0f:F1} cm");
				}
			}

			// Stats: base values times this attachment's numbers from the data file
			GunStats expectedStats = GetExpectedStats(serverGun.GunData, baseStats, attachment);
			if (StatsEqual(serverGun.Stats, expectedStats, out string statsDifference) == false)
			{
				problems.Add($"stats wrong ({statsDifference})");
			}

			summary.Append($", {DescribeEffect(baseStats, serverGun.Stats)}");
			return summary.ToString();
		}

		private static void CheckMount(GunModelParts parts, EAttachmentSlot slot, AttachmentData attachment, List<string> problems)
		{
			string where = parts.Root.gameObject.layer == ObjectLayer.FPV ? "first person" : RunnerName(parts.Root.GetComponentInParent<NetworkObject>());

			if (parts.GetAttachment(slot) != attachment)
			{
				problems.Add($"{where}: slot shows {(parts.GetAttachment(slot) != null ? parts.GetAttachment(slot).AttachmentName : "nothing")}");
				return;
			}

			if (attachment.Model != null)
			{
				GameObject instance = parts.GetInstance(attachment);
				Transform  mount    = parts.GetMount(slot);

				if (instance == null || instance.activeInHierarchy == false)
				{
					problems.Add($"{where}: model not showing");
				}
				else if (instance.transform.parent != mount || instance.transform.localPosition.sqrMagnitude > 0.0000001f || Quaternion.Angle(instance.transform.localRotation, Quaternion.identity) > 0.01f)
				{
					problems.Add($"{where}: model not at zero offset on its mount");
				}
			}

			switch (slot)
			{
				case EAttachmentSlot.Optic:
					if (attachment.Model != null && parts.IronSights != null && parts.IronSights.activeInHierarchy == true)
					{
						problems.Add($"{where}: iron sights still showing");
					}
					break;
				case EAttachmentSlot.Magazine:
					bool extended = attachment.UsesGunMagExt == true;
					if (parts.MagExt != null && parts.MagExt.activeSelf != extended || parts.Mag != null && parts.Mag.activeSelf == extended)
					{
						problems.Add($"{where}: wrong mag showing");
					}
					break;
				case EAttachmentSlot.Stock:
					if (attachment.Model != null && parts.Stock != null && parts.Stock.activeInHierarchy == true)
					{
						problems.Add($"{where}: default stock still showing");
					}
					break;
				case EAttachmentSlot.Grip:
					if (attachment.Model != null && parts.Grip != null && parts.Grip.activeInHierarchy == true)
					{
						problems.Add($"{where}: default grip still showing");
					}
					break;
			}
		}

		/// <summary>
		/// 10 on/off cycles on every copy's parts (directly, no network): nothing may pile up and the shown parts must return to the
		/// gun's own. Then 10 on/off cycles on the server: the stats must return to base each time. Ends with the attachment on.
		/// </summary>
		private static void CheckSwaps(Agent observed, BulletWeapon serverGun, EAttachmentSlot slot, AttachmentData attachment, GunStats baseStats, List<string> problems)
		{
			var on  = new AttachmentData[AttachmentData.SlotCount];
			var off = new AttachmentData[AttachmentData.SlotCount];
			on[(int)slot] = attachment;

			foreach (GunModelParts parts in GetAllParts(serverGun, observed))
			{
				long key = serverGun.AttachmentKey;

				parts.Apply(off);
				string baseSig       = ActiveSignature(parts.Root);
				int    baseCount     = parts.Root.GetComponentsInChildren<Transform>(true).Length;
				int    baseInstances = parts.InstanceCount;

				for (int i = 0; i < SwapCycles; ++i)
				{
					parts.Apply(on);
					parts.Apply(off);
				}

				string sig       = ActiveSignature(parts.Root);
				int    count     = parts.Root.GetComponentsInChildren<Transform>(true).Length;
				int    instances = parts.InstanceCount;

				parts.Apply(key);

				if (sig != baseSig || count != baseCount || instances != baseInstances)
				{
					problems.Add($"swap on {parts.Root.name}: {count - baseCount} extra objects, {instances - baseInstances} extra models, parts {(sig == baseSig ? "same" : "different")}");
				}
			}

			for (int i = 0; i < SwapCycles; ++i)
			{
				serverGun.SetAttachment(slot, null, out _);
				if (StatsEqual(serverGun.Stats, baseStats, out string difference) == false)
				{
					problems.Add($"swap {i + 1}: stats not back to base ({difference})");
					break;
				}

				serverGun.SetAttachment(slot, attachment, out _);
			}
		}

		// MEASURED EFFECTS (Icarus)

		private static IEnumerator EffectsRoutine()
		{
			if (FindPlayers(out Agent observed, out Agent server, out _) == false)
				yield break;

			BulletWeapon prefab = GetGuns("Icarus").FirstOrDefault();
			if (prefab == null)
				yield break;

			Log("Measured effects (Icarus, the server's numbers):");

			GunplaySettings settings = GunplaySettings.Current;
			bool noRecoil   = GunplayDebug.NoRecoil;
			bool zeroSpread = GunplayDebug.ZeroSpread;

			try
			{
				GunplayDebug.NoRecoil   = false;
				GunplayDebug.ZeroSpread = false;
				AgentInput.DebugHoldAim = false;

				server.Weapons.DebugGiveWeapon(prefab);
				yield return WaitFor(() => IsGunReady(observed, prefab.GunData, false), 10.0f, "Icarus in hand");
				if (_waitFailed != null)
				{
					Log($"  FAIL: {_waitFailed}.");
					yield break;
				}

				var gun = (BulletWeapon)server.Weapons.CurrentWeapon;
				Strip(gun);

				// Extended Mag + reload: 35 → 39; Fast Mag: reload 2.63 / ammo-in 1.18 s → x0.9
				yield return MeasureReload(gun, null);
				yield return MeasureReload(gun, settings.FindAttachment("Extended Mag"));
				yield return MeasureReload(gun, settings.FindAttachment("Fast Mag"));
				Strip(gun);

				// Long Barrel: bullet speed x1.1; falloff ranges x1.1
				yield return MeasureBulletSpeed(observed, gun, null);
				yield return MeasureBulletSpeed(observed, gun, settings.FindAttachment("Long Barrel"));
				Strip(gun);
				float falloffBase = settings.GetFalloff(EGunClass.AR, 55.0f, gun.Stats.FalloffRange);
				gun.SetAttachment(EAttachmentSlot.Muzzle, settings.FindAttachment("Long Barrel"), out _);
				float falloffLong = settings.GetFalloff(EGunClass.AR, 55.0f, gun.Stats.FalloffRange);
				Log($"  {Mark(Mathf.Approximately(falloffLong, 1.0f) && falloffBase < 1.0f)} Long Barrel falloff at 55 m: {falloffLong:F3} (without {falloffBase:F3}); AR full damage to 50 m, 55 m with it.");
				Strip(gun);

				// Suppressor: no flash, gunshot range x0.65
				yield return MeasureSuppressor(observed, gun);
				Strip(gun);

				// Foregrip: recoil x0.9 (same shots, with and without)
				yield return MeasureRecoil(observed, gun);
				Strip(gun);

				// Laser: hip spread x0.9 (the lagged server must see the aim released first)
				yield return WaitFor(() => server.Character.CharacterController.FixedData.Aim == false, 3.0f, "server not aiming");
				yield return WaitSeconds(0.5f);
				float spreadBase = gun.TotalDispersion;
				gun.SetAttachment(EAttachmentSlot.Underbarrel, settings.FindAttachment("Laser Sight"), out _);
				float spreadLaser = gun.TotalDispersion;
				Log($"  {Mark(Mathf.Abs(spreadLaser / spreadBase - 0.9f) < 0.005f)} Laser hip spread: {spreadLaser:F3}° vs {spreadBase:F3}° (x{spreadLaser / spreadBase:F3}, data x0.9).");
				Strip(gun);

				// Stock: aiming walk speed 2.5 → 2.75 m/s
				yield return MeasureAimSpeed(observed, server, gun, null);
				yield return MeasureAimSpeed(observed, server, gun, settings.FindAttachment("Stock"));
				Strip(gun);

				// Reflex: ADS time x0.9; 2x / 4x zoom; magnified optics sway and hold breath, others don't
				yield return MeasureAds(observed, server, gun, null);
				yield return MeasureAds(observed, server, gun, settings.FindAttachment("Reflex"));
				yield return MeasureAds(observed, server, gun, settings.FindAttachment("Holo"));
				yield return MeasureAds(observed, server, gun, settings.FindAttachment("2x"));
				yield return MeasureAds(observed, server, gun, settings.FindAttachment("4x"));
				Strip(gun);
			}
			finally
			{
				GunplayDebug.NoRecoil         = noRecoil;
				GunplayDebug.ZeroSpread       = zeroSpread;
				AgentInput.DebugHoldAim       = false;
				AgentInput.DebugHoldAttack    = false;
				AgentInput.DebugMoveDirection = Vector2.zero;
			}
		}

		private static IEnumerator MeasureReload(BulletWeapon gun, AttachmentData magazine)
		{
			Strip(gun);
			if (magazine != null)
			{
				gun.SetAttachment(EAttachmentSlot.Magazine, magazine, out _);
			}

			yield return WaitFor(() => AllCopiesHaveKey(gun, gun.AttachmentKey), 5.0f, "synced");

			GunplayDebug.Clear();
			gun.DebugSetMagazine(5);
			gun.Reload();

			yield return WaitFor(() => gun.IsReloading == false, 6.0f, "reload done");

			float ammoIn = ParseReloadMs("ammo-in");
			float done   = ParseReloadMs("done");
			GunStats stats = gun.Stats;
			float tick     = gun.Runner.DeltaTime * 1000.0f;
			bool  pass     = Mathf.Abs(ammoIn - stats.AmmoInTime * 1000.0f) <= tick && Mathf.Abs(done - stats.ReloadTime * 1000.0f) <= tick && gun.MagazineAmmo == stats.Magazine;

			Log($"  {Mark(pass)} Reload{(magazine != null ? $" with {magazine.AttachmentName}" : "")}: ammo-in {ammoIn:F0} ms (data {stats.AmmoInTime * 1000.0f:F0}), done {done:F0} ms (data {stats.ReloadTime * 1000.0f:F0}), mag {gun.MagazineAmmo}/{stats.Magazine}.");
		}

		private static IEnumerator MeasureBulletSpeed(Agent observed, BulletWeapon gun, AttachmentData muzzle)
		{
			Strip(gun);
			gun.SetAttachment(EAttachmentSlot.Muzzle, muzzle, out _);
			yield return WaitFor(() => AllCopiesHaveKey(gun, gun.AttachmentKey), 5.0f, "synced");

			GunplayDebug.Clear();
			yield return FireShots(observed, gun, 1);

			float speed = ParseLastFire("speed ", " m/s");
			bool  pass  = Mathf.Abs(speed - gun.Stats.BulletSpeed) < 0.5f && Mathf.Abs(speed - gun.GunData.BulletSpeed * (muzzle != null ? muzzle.BulletSpeed : 1.0f)) < 0.5f;
			Log($"  {Mark(pass)} Bullet speed{(muzzle != null ? $" with {muzzle.AttachmentName}" : "")}: {speed:F1} m/s (data {gun.GunData.BulletSpeed:F0} x {(muzzle != null ? muzzle.BulletSpeed : 1.0f):F2}).");
		}

		private static IEnumerator MeasureSuppressor(Agent observed, BulletWeapon gun)
		{
			AttachmentData suppressor = GunplaySettings.Current.FindAttachment("Suppressor");
			Viewmodel      viewmodel  = observed.GetComponent<Viewmodel>();

			// Without: a flash appears on the muzzle in first person
			Strip(gun);
			yield return WaitFor(() => AllCopiesHaveKey(gun, 0), 5.0f, "synced");
			yield return FireShots(observed, gun, 1);
			int   flashesOff = CountActiveChildren(viewmodel.Muzzle);
			float rangeOff   = GetFireAudioRange(observed);

			gun.SetAttachment(EAttachmentSlot.Muzzle, suppressor, out _);
			yield return WaitFor(() => AllCopiesHaveKey(gun, gun.AttachmentKey), 5.0f, "synced");
			yield return WaitSeconds(1.2f); // Old flashes go back to the cache
			yield return FireShots(observed, gun, 1);
			int   flashesOn = CountActiveChildren(viewmodel.Muzzle);
			float rangeOn   = GetFireAudioRange(observed);

			bool pass = flashesOff > 0 && flashesOn == 0 && Mathf.Abs(rangeOn / rangeOff - suppressor.GunshotRange) < 0.01f && gun.IsSuppressed == true;
			Log($"  {Mark(pass)} Suppressor: flash objects on the muzzle {flashesOn} (without {flashesOff}); gunshot range {rangeOn:F1} m vs {rangeOff:F1} m (x{rangeOn / Mathf.Max(0.01f, rangeOff):F2}, data x{suppressor.GunshotRange:F2}); suppressed flag {gun.IsSuppressed}.");
		}

		private static IEnumerator MeasureRecoil(Agent observed, BulletWeapon gun)
		{
			AttachmentData foregrip = GunplaySettings.Current.FindAttachment("Foregrip");
			var results     = new float[2];
			var shots       = new int[2];
			var clientShots = new int[2];

			for (int pass = 0; pass < 2; ++pass)
			{
				Strip(gun);
				if (pass == 1)
				{
					gun.SetAttachment(EAttachmentSlot.Underbarrel, foregrip, out _);
				}

				yield return WaitFor(() => AllCopiesHaveKey(gun, gun.AttachmentKey), 5.0f, "synced");

				AgentInput.DebugHoldAim = true;
				yield return WaitFor(() => observed.Character.AimProgress >= 0.999f, 3.0f, "aimed");
				yield return WaitSeconds(0.6f); // Recoil settles back to zero

				int   before = gun.ShotCount;
				float peak   = 0.0f;

				// Six shots (the first measured kicks are tiny); the server's kick peaks after the last one and then settles back,
				// so keep the largest
				var observedGun   = (FirearmWeapon)observed.Weapons.CurrentWeapon;
				int observedStart = observedGun.ShotCount;
				AgentInput.DebugHoldAttack = true;
				double end = EditorApplication.timeSinceStartup + 3.0;
				while (EditorApplication.timeSinceStartup < end)
				{
					if (observedGun.ShotCount >= observedStart + RecoilShots)
					{
						AgentInput.DebugHoldAttack = false;
					}

					peak = Mathf.Max(peak, gun.Recoil.magnitude);
					yield return null;
				}

				AgentInput.DebugHoldAttack = false;

				results[pass]       = peak;
				shots[pass]         = gun.ShotCount - before;
				clientShots[pass]   = observedGun.ShotCount - observedStart;
				AgentInput.DebugHoldAim = false;
			}

			// Phase 11e: the view springs toward each shot's measured offset, so its peak lands a little under the largest offset
			// of the string; the Foregrip scales the whole thing, so the two passes are compared with each other
			float expectedBase = GetExpectedRecoilPeak(gun.GunData, shots[0]);
			float expectedGrip = GetExpectedRecoilPeak(gun.GunData, shots[1]) * foregrip.Recoil;
			float ratioBase    = results[0] / Mathf.Max(0.0001f, expectedBase);
			float ratioGrip    = results[1] / Mathf.Max(0.0001f, results[0]);
			bool  ok           = shots[0] > 0 && shots[1] > 0 && Mathf.Abs(ratioBase - 1.0f) < 0.15f && Mathf.Abs(ratioGrip - foregrip.Recoil) < 0.03f;
			Log($"  {Mark(ok)} Foregrip recoil: {results[1]:F3}° after {shots[1]} shots (data {expectedGrip:F3}°, x{ratioGrip:F3} of the run without) vs without {results[0]:F3}° after {shots[0]} shots (data {expectedBase:F3}°, x{ratioBase:F2}); data x{foregrip.Recoil:F2}.");
		}

		private static IEnumerator MeasureAimSpeed(Agent observed, Agent server, BulletWeapon gun, AttachmentData stock)
		{
			Strip(gun);
			gun.SetAttachment(EAttachmentSlot.Stock, stock, out _);
			yield return WaitFor(() => AllCopiesHaveKey(gun, gun.AttachmentKey), 5.0f, "synced");

			MoveBothCopies(observed, BenchSpot + new Vector3(-3.0f, 0.0f, 1.0f), 0.0f);
			yield return WaitSeconds(1.5f);

			AgentInput.DebugHoldAim       = true;
			AgentInput.DebugMoveDirection = new Vector2(0.0f, 1.0f);
			yield return WaitSeconds(1.2f);

			float sum = 0.0f;
			int   count = 0;
			double end = EditorApplication.timeSinceStartup + 0.5;
			while (EditorApplication.timeSinceStartup < end)
			{
				sum += server.Character.CharacterController.FixedData.RealSpeed;
				++count;
				yield return null;
			}

			AgentInput.DebugMoveDirection = Vector2.zero;
			AgentInput.DebugHoldAim       = false;

			float speed    = sum / Mathf.Max(1, count);
			float expected = MovementSettings.Current.AimSpeed * (stock != null ? stock.AimMoveSpeed : 1.0f);
			Log($"  {Mark(Mathf.Abs(speed - expected) < 0.05f)} Aiming walk speed{(stock != null ? $" with {stock.AttachmentName}" : "")}: {speed:F2} m/s (data {expected:F2}).");

			MoveBothCopies(observed, BenchSpot, 0.0f);
			yield return WaitSeconds(1.5f);
		}

		private static IEnumerator MeasureAds(Agent observed, Agent server, BulletWeapon gun, AttachmentData optic)
		{
			Strip(gun);
			gun.SetAttachment(EAttachmentSlot.Optic, optic, out _);
			yield return WaitFor(() => AllCopiesHaveKey(gun, gun.AttachmentKey), 5.0f, "synced");

			AgentInput.DebugHoldAim = false;
			yield return WaitFor(() => observed.Character.AimProgress <= 0.0f, 3.0f, "lowered");
			yield return WaitSeconds(0.3f);

			// Timed in game time, which is what AimProgress moves by (an editor hitch would distort a wall-clock time)
			AgentInput.DebugHoldAim = true;
			float start = -1.0f;
			float last  = Time.time;
			yield return WaitFor(() =>
			{
				if (start < 0.0f && observed.Character.AimProgress > 0.0f)
				{
					start = last; // The frame before progress started
				}

				last = Time.time;
				return observed.Character.AimProgress >= 1.0f;
			}, 3.0f, "aimed");
			float adsTime = Time.time - Mathf.Max(0.0f, start);

			yield return WaitFor(() => server.Character.CharacterController.FixedData.Aim == true, 3.0f, "server aiming");
			yield return WaitSeconds(0.2f);

			GunStats stats    = gun.Stats;
			Camera   camera   = observed.Context.Camera.Camera;
			float    hipFov   = observed.Character.GetHipVerticalFOV();
			// Phase 11a: with a scope lens the camera only zooms by the view zoom (the lens shows AdsZoom)
			float    zoomed   = 2.0f * Mathf.Atan(Mathf.Tan(hipFov * 0.5f * Mathf.Deg2Rad) / gun.ViewZoom) * Mathf.Rad2Deg;
			bool     scoped   = observed.Weapons.IsScopedAim;
			bool     serverScoped = gun.HasMagnifiedOptic == true && server.Character.CharacterController.FixedData.Aim == true;

			AgentInput.DebugHoldAim = false;

			float frame = Time.smoothDeltaTime; // One frame either side
			bool  pass  = Mathf.Abs(adsTime - stats.AdsTime) <= frame * 1.5f && Mathf.Abs(camera.fieldOfView - zoomed) < 0.05f && scoped == stats.Magnified && serverScoped == stats.Magnified;
			Log($"  {Mark(pass)} {(optic != null ? optic.AttachmentName : "Irons")}: ADS {adsTime * 1000.0f:F0} ms (data {stats.AdsTime * 1000.0f:F0}, frame {frame * 1000.0f:F0} ms), zoom x{stats.AdsZoom:F2} (FOV {camera.fieldOfView:F2}°, expected {zoomed:F2}°), scope sway and hold breath {(scoped ? "on" : "off")} (server {(serverScoped ? "on" : "off")}).");
		}

		/// <summary>The largest aimed view offset over a string of this many shots, from the gun's measured pattern (no attachments).</summary>
		private static float GetExpectedRecoilPeak(GunData data, int shots)
		{
			if (data.HasMeasuredRecoil == false)
				return GunplaySettings.Current.UnmeasuredKickUp;

			float peak = 0.0f;
			for (int i = 0; i < shots; ++i)
			{
				peak = Mathf.Max(peak, data.GetAdsOffset(i, i, GunplaySettings.Current.RecoilWanderUp).magnitude);
			}

			return peak;
		}

		private static IEnumerator FireShots(Agent observed, BulletWeapon serverGun, int shots)
		{
			var observedGun = observed.Weapons.CurrentWeapon as FirearmWeapon;
			int start       = observedGun != null ? observedGun.ShotCount : 0;

			AgentInput.DebugHoldAttack = true;
			yield return WaitFor(() => observedGun != null && observedGun.ShotCount >= start + shots, 3.0f, "shot fired");
			AgentInput.DebugHoldAttack = false;

			// The server gets the same input a little later
			yield return WaitSeconds(0.5f);
		}

		// CONTACT SHEETS

		private static readonly List<string> _sheetLog = new List<string>(64);

		/// <summary>
		/// One image per gun in Logs/GunBench/: a row per attachment (and none, and a full kit), columns first person at the hip,
		/// first person aimed, third person from the side (as the other peer sees it). Foregrip guns also get a hand sheet: idle,
		/// firing, aimed, sprinting, after a reload. About 20 s per gun. Read the result with GetGunBenchLog().
		/// </summary>
		public static string MakeContactSheets(string onlyGuns)
		{
			if (Application.isPlaying == false)
				return "Enter Play mode (Gun Bench, 2 peers) first.";
			if (_routine.Count > 0)
				return $"{_routineName} is already running.";

			List<BulletWeapon> guns = GetGuns(onlyGuns);
			_log.Clear();
			StartRoutine("Contact Sheets", SheetsRoutine(guns));
			return $"Contact sheets started for {guns.Count} guns, written to {SheetFolder}/. Read GetGunBenchLog().";
		}

		private const int TileWidth  = 480;
		private const int TileHeight = 270;

		private static IEnumerator SheetsRoutine(List<BulletWeapon> guns)
		{
			if (FindPlayers(out Agent observed, out Agent server, out Agent otherCopy) == false)
			{
				Log("FAIL: needs 2 players in Play mode (Gun Bench, ExtraPeers = 1).");
				yield break;
			}

			Directory.CreateDirectory(SheetFolder);

			try
			{
				Viewmodel.DebugFreezeMotion = true;

				PlaceAtBench(observed);
				yield return WaitSeconds(1.5f);

				foreach (BulletWeapon prefab in guns)
				{
					GunData data = prefab.GunData;

					server.Weapons.DebugGiveWeapon(prefab);
					yield return WaitFor(() => IsGunReady(observed, data, false), 10.0f, $"{data.GunName} in hand");
					if (_waitFailed != null)
					{
						Log($"FAIL {data.GunName}: {_waitFailed}.");
						continue;
					}

					var serverGun = (BulletWeapon)server.Weapons.CurrentWeapon;

					// Rows: none, each attachment alone, then a full kit
					var rows = new List<AttachmentData[]> { new AttachmentData[AttachmentData.SlotCount] };
					var labels = new List<string> { "none" };
					var kit = new AttachmentData[AttachmentData.SlotCount];

					for (int slot = 0; slot < AttachmentData.SlotCount; ++slot)
					{
						List<AttachmentData> options = GunBench.GetOptions(data, (EAttachmentSlot)slot);
						for (int i = 1; i < options.Count; ++i)
						{
							var row = new AttachmentData[AttachmentData.SlotCount];
							row[slot] = options[i];
							rows.Add(row);
							labels.Add(options[i].AttachmentName);
						}

						// Kit: the last optic (the biggest), the first muzzle, underbarrel, mag and stock in the data file's order
						if (options.Count > 1)
						{
							kit[slot] = slot == (int)EAttachmentSlot.Optic ? options[options.Count - 1] : options[1];
						}
					}

					rows.Add(kit);
					labels.Add("kit: " + string.Join(", ", kit.Where(t => t != null).Select(t => t.AttachmentName)));

					var tiles = new List<Texture2D>();
					for (int r = 0; r < rows.Count; ++r)
					{
						yield return ApplyRow(serverGun, rows[r]);
						yield return CaptureRow(observed, tiles);
					}

					string file = $"{SheetFolder}/{data.GunName.Replace(" ", string.Empty)}.png";
					SaveSheet(tiles, 3, file);
					File.WriteAllText(Path.ChangeExtension(file, ".txt"), $"{data.GunName} contact sheet. Columns: first person at the hip, first person aimed, third person from the side (as the other peer sees it).\nRows:\n" + string.Join("\n", labels.Select((t, i) => $"{i + 1}. {t}")));
					Log($"{data.GunName}: {file} ({rows.Count} rows).");

					// Hand sheet for guns that take the Foregrip
					AttachmentData foregrip = GunplaySettings.Current.FindAttachment("Foregrip");
					if (data.Allows(foregrip) == true)
					{
						var handRow = new AttachmentData[AttachmentData.SlotCount];
						handRow[(int)EAttachmentSlot.Underbarrel] = foregrip;
						yield return ApplyRow(serverGun, handRow);

						var handTiles = new List<Texture2D>();
						string[] states = { "idle", "firing", "aimed", "sprinting", "after reload" };
						foreach (string state in states)
						{
							yield return CaptureHandState(observed, serverGun, state, handTiles);
						}

						string handFile = $"{SheetFolder}/{data.GunName.Replace(" ", string.Empty)}_Hand.png";
						SaveSheet(handTiles, 2, handFile);
						File.WriteAllText(Path.ChangeExtension(handFile, ".txt"), $"{data.GunName} with the Foregrip. Columns: first person, third person (as the other peer sees it).\nRows:\n" + string.Join("\n", states.Select((t, i) => $"{i + 1}. {t}")));
						Log($"{data.GunName}: {handFile}.");
					}

					Strip(serverGun);
				}
			}
			finally
			{
				Viewmodel.DebugFreezeMotion   = false;
				AgentInput.DebugHoldAim       = false;
				AgentInput.DebugHoldAttack    = false;
				AgentInput.DebugHoldSprint    = false;
				AgentInput.DebugMoveDirection = Vector2.zero;
				SetVisiblePeer(observed, true);
			}

			Log($"Contact sheets done: {Path.GetFullPath(SheetFolder)}");
		}

		private static IEnumerator ApplyRow(BulletWeapon serverGun, AttachmentData[] row)
		{
			for (int slot = 0; slot < AttachmentData.SlotCount; ++slot)
			{
				serverGun.SetAttachment((EAttachmentSlot)slot, row[slot], out _);
			}

			long key = serverGun.AttachmentKey;
			yield return WaitFor(() => AllCopiesHaveKey(serverGun, key), 5.0f, "synced");
			yield return null;
			yield return null;
		}

		private static IEnumerator CaptureRow(Agent observed, List<Texture2D> tiles)
		{
			AgentInput.DebugHoldAim = false;
			yield return WaitFor(() => observed.Character.AimProgress <= 0.0f, 3.0f, "lowered");
			yield return WaitSeconds(0.15f);
			tiles.Add(RenderFirstPerson(observed));

			AgentInput.DebugHoldAim = true;
			yield return WaitFor(() => observed.Character.AimProgress >= 1.0f, 3.0f, "aimed");
			yield return WaitSeconds(0.15f);
			tiles.Add(RenderFirstPerson(observed));
			AgentInput.DebugHoldAim = false;

			yield return CaptureThirdPerson(observed, tiles);
		}

		private static IEnumerator CaptureHandState(Agent observed, BulletWeapon serverGun, string state, List<Texture2D> tiles)
		{
			AgentInput.DebugHoldAim       = state == "aimed";
			AgentInput.DebugHoldSprint    = state == "sprinting";
			AgentInput.DebugMoveDirection = state == "sprinting" ? new Vector2(0.0f, 1.0f) : Vector2.zero;
			AgentInput.DebugHoldAttack    = state == "firing";

			if (state == "after reload")
			{
				serverGun.DebugSetMagazine(1);
				serverGun.Reload();
				yield return WaitFor(() => serverGun.IsReloading == true, 2.0f, "reloading");
				yield return WaitFor(() => serverGun.IsReloading == false, 6.0f, "reloaded");
			}

			yield return WaitSeconds(state == "sprinting" ? 0.8f : 0.4f);
			tiles.Add(RenderFirstPerson(observed));
			yield return CaptureThirdPerson(observed, tiles);

			AgentInput.DebugHoldAim       = false;
			AgentInput.DebugHoldSprint    = false;
			AgentInput.DebugHoldAttack    = false;
			AgentInput.DebugMoveDirection = Vector2.zero;

			if (state == "sprinting")
			{
				MoveBothCopies(observed, BenchSpot, 0.0f);
				yield return WaitSeconds(1.0f);
			}
		}

		/// <summary>Switches to the other peer's view for one frame and renders the observed player from the side.</summary>
		private static IEnumerator CaptureThirdPerson(Agent observed, List<Texture2D> tiles)
		{
			SetVisiblePeer(observed, false);
			yield return null;
			yield return null;
			yield return null;

			Agent otherCopy = FindCopyOn(observed, false);
			if (otherCopy != null && otherCopy.Weapons.CurrentWeapon is BulletWeapon otherGun)
			{
				var visuals = otherGun.GetComponent<WeaponAttachmentVisuals>();
				Transform model  = visuals != null && visuals.Parts != null ? visuals.Parts.Root : otherGun.transform;
				Vector3   center = model.position + model.forward * 0.15f;
				Vector3   side   = -otherCopy.transform.right;
				tiles.Add(RenderFrom(center + side * 1.1f + Vector3.up * 0.15f, center, 45.0f));
			}
			else
			{
				tiles.Add(Blank());
			}

			SetVisiblePeer(observed, true);
			yield return null;
			yield return null;
		}

		// RENDERING

		private static Texture2D RenderFirstPerson(Agent observed)
		{
			Camera camera = observed.Context.Camera.Camera;
			return RenderCamera(camera);
		}

		private static Texture2D RenderFrom(Vector3 position, Vector3 target, float fieldOfView)
		{
			var cameraObject = new GameObject("GunBenchSheetCamera");
			try
			{
				var camera = cameraObject.AddComponent<Camera>();
				camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
				camera.fieldOfView   = fieldOfView;
				camera.nearClipPlane = 0.02f;
				camera.cullingMask   = ~(1 << ObjectLayer.FPV);
				camera.enabled       = false;
				return RenderCamera(camera);
			}
			finally
			{
				Object.DestroyImmediate(cameraObject);
			}
		}

		/// <summary>Renders a camera with its overlay stack (the viewmodel camera) into a tile.</summary>
		private static Texture2D RenderCamera(Camera camera)
		{
			RenderTexture target = RenderTexture.GetTemporary(TileWidth, TileHeight, 24, RenderTextureFormat.ARGB32);
			try
			{
				var request = new RenderPipeline.StandardRequest { destination = target };
				if (RenderPipeline.SupportsRenderRequest(camera, request) == true)
				{
					RenderPipeline.SubmitRenderRequest(camera, request);
				}
				else
				{
					RenderTexture previous = camera.targetTexture;
					camera.targetTexture = target;
					camera.Render();
					camera.targetTexture = previous;
				}

				RenderTexture active = RenderTexture.active;
				RenderTexture.active = target;
				var tile = new Texture2D(TileWidth, TileHeight, TextureFormat.RGB24, false);
				tile.ReadPixels(new Rect(0, 0, TileWidth, TileHeight), 0, 0);
				tile.Apply();
				RenderTexture.active = active;
				return tile;
			}
			finally
			{
				RenderTexture.ReleaseTemporary(target);
			}
		}

		private static Texture2D Blank()
		{
			var tile = new Texture2D(TileWidth, TileHeight, TextureFormat.RGB24, false);
			var pixels = new Color32[TileWidth * TileHeight];
			for (int i = 0; i < pixels.Length; ++i)
			{
				pixels[i] = new Color32(60, 0, 0, 255);
			}

			tile.SetPixels32(pixels);
			tile.Apply();
			return tile;
		}

		/// <summary>Tiles left to right, top to bottom, with a 4 px gap.</summary>
		private static void SaveSheet(List<Texture2D> tiles, int columns, string path)
		{
			const int gap = 4;
			int rows   = Mathf.CeilToInt(tiles.Count / (float)columns);
			int width  = columns * TileWidth + (columns + 1) * gap;
			int height = rows * TileHeight + (rows + 1) * gap;

			var sheet  = new Texture2D(width, height, TextureFormat.RGB24, false);
			var pixels = new Color32[width * height];
			for (int i = 0; i < pixels.Length; ++i)
			{
				pixels[i] = new Color32(20, 20, 20, 255);
			}

			sheet.SetPixels32(pixels);

			for (int i = 0; i < tiles.Count; ++i)
			{
				int column = i % columns;
				int row    = i / columns;
				int x      = gap + column * (TileWidth + gap);
				int y      = height - (row + 1) * (TileHeight + gap); // Texture rows start at the bottom
				sheet.SetPixels(x, y, TileWidth, TileHeight, tiles[i].GetPixels());
				Object.DestroyImmediate(tiles[i]);
			}

			sheet.Apply();
			File.WriteAllBytes(path, sheet.EncodeToPNG());
			Object.DestroyImmediate(sheet);
		}

		// HELPERS: players, copies and peers

		/// <summary>observed = the player you look through (peer with input); server = the server's copy; other = the other player on the server.</summary>
		private static bool FindPlayers(out Agent observed, out Agent server, out Agent other)
		{
			observed = null;
			server   = null;
			other    = null;

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
				if (agent.Runner == null || agent.Runner.IsServer == false || agent.Object == null || agent.Object.IsValid == false)
					continue;

				if (agent.Object.InputAuthority == observed.Object.InputAuthority)
				{
					server = agent;
				}
				else
				{
					other = agent;
				}
			}

			return server != null && other != null;
		}

		/// <summary>The observed player's copy on its own peer (sameRunner) or on the other peer.</summary>
		private static Agent FindCopyOn(Agent observed, bool sameRunner)
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object == null || agent.Object.IsValid == false || agent.Object.InputAuthority != observed.Object.InputAuthority)
					continue;

				if ((agent.Runner == observed.Runner) == sameRunner)
					return agent;
			}

			return null;
		}

		private static List<BulletWeapon> FindCopies(BulletWeapon serverGun)
		{
			var copies = new List<BulletWeapon>(2);
			if (serverGun == null || serverGun.Object == null || serverGun.Object.IsValid == false)
				return copies;

			NetworkId id = serverGun.Object.Id;
			foreach (BulletWeapon weapon in Object.FindObjectsByType<BulletWeapon>(FindObjectsSortMode.None))
			{
				if (weapon.Object != null && weapon.Object.IsValid == true && weapon.Object.Id == id)
				{
					copies.Add(weapon);
				}
			}

			return copies;
		}

		private static bool AllCopiesHaveKey(BulletWeapon serverGun, long key)
		{
			List<BulletWeapon> copies = FindCopies(serverGun);
			if (copies.Count < 2)
				return false;

			foreach (BulletWeapon copy in copies)
			{
				var visuals = copy.GetComponent<WeaponAttachmentVisuals>();
				if (copy.AttachmentKey != key || visuals == null || visuals.Parts == null)
					return false;
			}

			return true;
		}

		/// <summary>The viewmodel's parts and every peer's third-person parts for this gun.</summary>
		private static List<GunModelParts> GetAllParts(BulletWeapon serverGun, Agent observed)
		{
			var parts = new List<GunModelParts>(3);

			GunModelParts viewParts = GetViewmodelParts(observed);
			if (viewParts != null)
			{
				parts.Add(viewParts);
			}

			foreach (BulletWeapon copy in FindCopies(serverGun))
			{
				var visuals = copy.GetComponent<WeaponAttachmentVisuals>();
				if (visuals != null && visuals.Parts != null)
				{
					parts.Add(visuals.Parts);
				}
			}

			return parts;
		}

		private static GunModelParts GetViewmodelParts(Agent observed)
		{
			Viewmodel viewmodel = observed.GetComponent<Viewmodel>();
			return viewmodel != null && viewmodel.IsVisible == true ? viewmodel.CurrentParts : null;
		}

		private static bool IsGunReady(Agent observed, GunData data, bool aimed)
		{
			if (observed.Weapons.CurrentWeapon is BulletWeapon gun == false || gun.GunData != data || observed.Weapons.IsDrawing == true)
				return false;

			GunModelParts parts = GetViewmodelParts(observed);
			if (parts == null || parts.Root.name.StartsWith($"WPN_{data.GunName.Replace(" ", string.Empty)}") == false)
				return false;

			// A new gun is raised from below first
			if (observed.GetComponent<Viewmodel>().Lowered > 0.0f)
				return false;

			return aimed == false || observed.Character.AimProgress >= 1.0f;
		}

		/// <summary>Shows the observed peer (true) or the other peer (false); input stays with the observed peer.</summary>
		private static void SetVisiblePeer(Agent observed, bool observedVisible)
		{
			var contexts = new HashSet<SceneContext>();
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Context != null)
				{
					contexts.Add(agent.Context);
				}
			}

			foreach (SceneContext context in contexts)
			{
				bool isObservedPeer = context == observed.Context;
				context.IsVisible = isObservedPeer == observedVisible;
			}
		}

		/// <summary>The observed player at the bench facing the target, the other player at the watch spot facing the bench.</summary>
		private static void PlaceAtBench(Agent observed)
		{
			MoveBothCopies(observed, BenchSpot, 0.0f);

			if (FindPlayers(out _, out _, out Agent other) == true)
			{
				MoveBothCopies(other, WatchSpot, -90.0f);
			}
		}

		/// <summary>Teleports every peer's copy of this player and sets its look (yaw in degrees, level).</summary>
		private static void MoveBothCopies(Agent player, Vector3 position, float yaw)
		{
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object == null || agent.Object.IsValid == false || agent.Object.InputAuthority != player.Object.InputAuthority)
					continue;

				KCC kcc = agent.Character.CharacterController;
				kcc.FixedData.BasePosition     = position;
				kcc.FixedData.DesiredPosition  = position;
				kcc.FixedData.TargetPosition   = position;
				kcc.RenderData.BasePosition    = position;
				kcc.RenderData.DesiredPosition = position;
				kcc.RenderData.TargetPosition  = position;
				kcc.SetPosition(position, true, false, false);
				agent.transform.position = position;

				kcc.FixedData.LookYaw    = yaw;
				kcc.RenderData.LookYaw   = yaw;
				kcc.FixedData.LookPitch  = 0.0f;
				kcc.RenderData.LookPitch = 0.0f;
			}
		}

		private static void Strip(BulletWeapon gun)
		{
			for (int slot = 0; slot < AttachmentData.SlotCount; ++slot)
			{
				gun.SetAttachment((EAttachmentSlot)slot, null, out _);
			}
		}

		private static List<BulletWeapon> GetGuns(string onlyGuns)
		{
			GunplaySettings settings = GunplaySettings.Current;
			var names = string.IsNullOrEmpty(onlyGuns) == false ? new HashSet<string>(onlyGuns.Split(',').Select(t => t.Trim())) : null;

			return (settings.TestGuns ?? new Weapon[0]).OfType<BulletWeapon>().Where(t => t.GunData != null && (names == null || names.Contains(t.GunData.GunName))).ToList();
		}

		// HELPERS: measurements

		/// <summary>
		/// How far the active sight's line lands from the screen center, in pixels at 1920 x 1080, at FOV 90 and 110 (the worse of
		/// the two, and of points 0.1 m and 1 m down the sight line). Uses the zoomed world FOV, where shots go.
		/// </summary>
		private static float SightError(Agent observed, float zoom)
		{
			GunModelParts parts  = GetViewmodelParts(observed);
			Camera        camera = observed.Context.Camera != null ? observed.Context.Camera.ViewmodelCamera : null;
			if (parts == null || parts.AdsEye == null || camera == null)
				return float.PositiveInfinity;

			float referenceAspect = new SerializedObject(observed.Character).FindProperty("_fovReferenceAspect").floatValue;
			float worst = 0.0f;

			foreach (float fovSetting in new[] { 90.0f, 110.0f })
			{
				float hipVertical = 2.0f * Mathf.Atan(Mathf.Tan(fovSetting * 0.5f * Mathf.Deg2Rad) / referenceAspect);
				float adsVertical = 2.0f * Mathf.Atan(Mathf.Tan(hipVertical * 0.5f) / Mathf.Max(1.0f, zoom));
				float tanHalfV    = Mathf.Tan(adsVertical * 0.5f);
				float tanHalfH    = tanHalfV * 16.0f / 9.0f;

				foreach (float distance in new[] { 0.1f, 1.0f })
				{
					Vector3 point = camera.transform.InverseTransformPoint(parts.AdsEye.position + parts.Root.forward * distance);
					if (point.z <= 0.0f)
						return float.PositiveInfinity;

					float x = point.x / point.z / tanHalfH * 960.0f;
					float y = point.y / point.z / tanHalfV * 540.0f;
					worst = Mathf.Max(worst, Mathf.Sqrt(x * x + y * y));
				}
			}

			return worst;
		}

		/// <summary>The gun's base stats with this one attachment's numbers from the data file applied, field by field.</summary>
		private static GunStats GetExpectedStats(GunData data, GunStats baseStats, AttachmentData attachment)
		{
			GunStats expected = baseStats;

			expected.Magazine      = Mathf.CeilToInt(data.Magazine * attachment.Magazine - 0.0001f);
			expected.ReloadTime    = baseStats.ReloadTime * attachment.ReloadTime;
			expected.AmmoInTime    = baseStats.AmmoInTime * attachment.ReloadTime;
			expected.AdsTime       = baseStats.AdsTime * attachment.AdsTime;
			expected.BulletSpeed   = baseStats.BulletSpeed * attachment.BulletSpeed;
			expected.FalloffRange  = baseStats.FalloffRange * attachment.FalloffRange;
			expected.Recoil        = baseStats.Recoil * attachment.Recoil;
			expected.HipSpread     = baseStats.HipSpread * attachment.HipSpread;
			expected.AimMoveSpeed  = baseStats.AimMoveSpeed * attachment.AimMoveSpeed;
			expected.AimIdleMotion = baseStats.AimIdleMotion * attachment.AimIdleMotion;
			expected.GunshotRange  = baseStats.GunshotRange * attachment.GunshotRange;
			expected.HidesFlash    = attachment.HidesFlash;
			expected.Suppressed    = attachment.Suppressed;

			if (attachment.Slot == EAttachmentSlot.Optic)
			{
				expected.AdsZoom     = attachment.Zoom > 1.0f ? attachment.Zoom : baseStats.AdsZoom;
				expected.Magnified   = attachment.Magnified;
				expected.EyeDistance = attachment.EyeDistance;
			}

			return expected;
		}

		private static bool StatsEqual(GunStats a, GunStats b, out string difference)
		{
			var parts = new List<string>();
			void Compare(string name, float x, float y) { if (Mathf.Abs(x - y) > 0.0001f) parts.Add($"{name} {x:0.####} vs {y:0.####}"); }

			if (a.Magazine != b.Magazine) parts.Add($"mag {a.Magazine} vs {b.Magazine}");
			Compare("reload", a.ReloadTime, b.ReloadTime);
			Compare("ammo-in", a.AmmoInTime, b.AmmoInTime);
			Compare("ADS", a.AdsTime, b.AdsTime);
			Compare("zoom", a.AdsZoom, b.AdsZoom);
			Compare("eye", a.EyeDistance, b.EyeDistance);
			Compare("speed", a.BulletSpeed, b.BulletSpeed);
			Compare("falloff", a.FalloffRange, b.FalloffRange);
			Compare("recoil", a.Recoil, b.Recoil);
			Compare("hip spread", a.HipSpread, b.HipSpread);
			Compare("aim speed", a.AimMoveSpeed, b.AimMoveSpeed);
			Compare("aim motion", a.AimIdleMotion, b.AimIdleMotion);
			Compare("gunshot range", a.GunshotRange, b.GunshotRange);
			if (a.Magnified  != b.Magnified)  parts.Add("magnified");
			if (a.HidesFlash != b.HidesFlash) parts.Add("flash");
			if (a.Suppressed != b.Suppressed) parts.Add("suppressed");

			difference = string.Join(", ", parts);
			return parts.Count == 0;
		}

		private static string DescribeEffect(GunStats before, GunStats after)
		{
			var parts = new List<string>();
			void Change(string name, float x, float y, string format) { if (Mathf.Abs(x - y) > 0.0001f) parts.Add($"{name} {x.ToString(format)}→{y.ToString(format)}"); }

			if (before.Magazine != after.Magazine) parts.Add($"mag {before.Magazine}→{after.Magazine}");
			Change("reload", before.ReloadTime, after.ReloadTime, "0.###");
			Change("ammo-in", before.AmmoInTime, after.AmmoInTime, "0.###");
			Change("ADS", before.AdsTime, after.AdsTime, "0.###");
			Change("zoom", before.AdsZoom, after.AdsZoom, "0.##");
			Change("speed", before.BulletSpeed, after.BulletSpeed, "0");
			Change("falloff range", before.FalloffRange, after.FalloffRange, "0.##");
			Change("recoil", before.Recoil, after.Recoil, "0.##");
			Change("hip spread", before.HipSpread, after.HipSpread, "0.##");
			Change("aim speed", before.AimMoveSpeed, after.AimMoveSpeed, "0.##");
			Change("aim motion", before.AimIdleMotion, after.AimIdleMotion, "0.##");
			Change("gunshot range", before.GunshotRange, after.GunshotRange, "0.##");
			if (after.Magnified  == true && before.Magnified  == false) parts.Add("magnified");
			if (after.HidesFlash == true && before.HidesFlash == false) parts.Add("no flash");
			if (after.Suppressed == true && before.Suppressed == false) parts.Add("suppressed");

			return parts.Count > 0 ? string.Join(", ", parts) : "no stat change";
		}

		private static float ParseReloadMs(string marker)
		{
			for (int i = GunplayDebug.ShotLog.Count - 1; i >= 0; --i)
			{
				string line = GunplayDebug.ShotLog[i];
				if (line.StartsWith("RELOAD ") == false || line.Contains($"): {marker}") == false)
					continue;

				int start = line.IndexOf(" (") + 2;
				int end   = line.IndexOf(" ms)", start);
				return float.Parse(line.Substring(start, end - start), System.Globalization.CultureInfo.InvariantCulture);
			}

			return -1.0f;
		}

		private static float ParseLastFire(string startMarker, string endMarker)
		{
			for (int i = GunplayDebug.ShotLog.Count - 1; i >= 0; --i)
			{
				string line = GunplayDebug.ShotLog[i];
				if (line.StartsWith("FIRE ") == false)
					continue;

				int start = line.IndexOf(startMarker) + startMarker.Length;
				int end   = line.IndexOf(endMarker, start);
				return float.Parse(line.Substring(start, end - start), System.Globalization.CultureInfo.InvariantCulture);
			}

			return -1.0f;
		}

		/// <summary>The observed player's gunshot audio range (the agent's shared fire sources, as the gun last set them).</summary>
		private static float GetFireAudioRange(Agent observed)
		{
			var field   = typeof(BulletWeapon).GetField("_fireAudio", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			var effects = field != null ? field.GetValue(observed.Weapons.CurrentWeapon) as AudioEffect[] : null;
			return effects != null && effects.Length > 0 && effects[0] != null ? effects[0].AudioSource.maxDistance : -1.0f;
		}

		private static int CountActiveChildren(Transform parent)
		{
			int count = 0;
			if (parent == null)
				return 0;

			for (int i = 0; i < parent.childCount; ++i)
			{
				if (parent.GetChild(i).gameObject.activeInHierarchy == true)
				{
					++count;
				}
			}

			return count;
		}

		/// <summary>Names of the shown renderers under a model (what the player sees).</summary>
		private static string ActiveSignature(Transform root)
		{
			return string.Join("|", root.GetComponentsInChildren<Renderer>(false).Select(t => t.name).OrderBy(t => t));
		}

		private static string RunnerName(Component component)
		{
			NetworkObject networkObject = component != null ? component.GetComponentInParent<NetworkObject>() : null;
			return networkObject != null && networkObject.Runner != null ? (networkObject.Runner.IsServer == true ? "host" : "client") : "?";
		}

		private static string Mark(bool pass) => pass == true ? "PASS" : "FAIL";

		// HELPERS: routine

		private static void Log(string line)
		{
			_log.Add(line);
		}

		private static void StartRoutine(string routineName, IEnumerator routine)
		{
			_routine.Clear();
			_routine.Push(routine);
			_routineName = routineName;

			EditorApplication.update -= UpdateRoutine;
			EditorApplication.update += UpdateRoutine;
		}

		private static void UpdateRoutine()
		{
			if (Application.isPlaying == false)
			{
				Log($"{_routineName} stopped: Play mode ended.");
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

					break; // yield return null: next editor update
				}
			}
			catch (System.Exception exception)
			{
				Log($"{_routineName} failed: {exception}");
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
			// Run the finally blocks of anything still on the stack
			while (_routine.Count > 0)
			{
				(_routine.Pop() as System.IDisposable)?.Dispose();
			}

			EditorApplication.update -= UpdateRoutine;
			Viewmodel.DebugFreezeMotion   = false;
			AgentInput.DebugHoldAim       = false;
			AgentInput.DebugHoldAttack    = false;
			AgentInput.DebugHoldSprint    = false;
			AgentInput.DebugMoveDirection = Vector2.zero;
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
