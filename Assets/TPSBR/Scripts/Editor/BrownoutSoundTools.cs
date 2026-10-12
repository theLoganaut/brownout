namespace TPSBR
{
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Text.RegularExpressions;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 10 step 7, the sound pipeline (no clips yet; Logan's come later):
	/// - Brownout → Data → Import Sounds: every clip in Assets/Brownout/Audio/ (any subfolder) named by the rule goes to its slot in
	///   Resources/Settings/SoundSettings.asset; the slots are rebuilt from the files each time.
	///     SFX_&lt;Gun&gt;_Fire[_NN]            gun shot (gun name without spaces, e.g. SFX_AlibiDMR_Fire_01)
	///     SFX_&lt;Gun&gt;_FireSuppressed[_NN]  gun shot with a Suppressor (falls back to _Fire)
	///     SFX_&lt;Gun&gt;_Reload[_NN]          reload
	///     SFX_Footstep_&lt;Surface&gt;[_NN]     footstep walking on a surface (Default = untagged ground)
	///     SFX_Footstep_&lt;Surface&gt;_Run[_NN] footstep running (falls back to walking)
	/// - Brownout → Test → Sound Check: lists every slot and whether it plays Logan's clip or the sample's (Logs/SoundCheck/SoundCheck.txt).
	/// </summary>
	public static class BrownoutSoundTools
	{
		// CONSTANTS

		public const string AudioFolder = "Assets/Brownout/Audio";
		public const string AssetPath   = "Assets/TPSBR/Resources/Settings/SoundSettings.asset";

		private static readonly Regex GunPattern      = new Regex(@"^SFX_(?<gun>[A-Za-z0-9]+)_(?<kind>FireSuppressed|Fire|Reload)(_\d+)?$", RegexOptions.IgnoreCase);
		private static readonly Regex FootstepPattern = new Regex(@"^SFX_Footstep_(?<surface>[A-Za-z0-9]+?)(?<run>_Run)?(_\d+)?$", RegexOptions.IgnoreCase);

		// MENU

		[MenuItem("Brownout/Data/Import Sounds", priority = 20)]
		private static void ImportMenu() => Debug.Log(ImportSounds());

		[MenuItem("Brownout/Test/Sound Check", priority = 60)]
		private static void CheckMenu() => Debug.Log(RunSoundCheck());

		// PUBLIC METHODS

		public static string ImportSounds()
		{
			var log = new StringBuilder("Import Sounds:\n");

			if (AssetDatabase.IsValidFolder(AudioFolder) == false)
			{
				BrownoutMapBuilder.EnsureFolder(AudioFolder);
				log.AppendLine($"Made {AudioFolder}/ (drop clips here, named by the rule in BrownoutSoundTools).");
			}

			SoundSettings settings = AssetDatabase.LoadAssetAtPath<SoundSettings>(AssetPath);
			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<SoundSettings>();
				AssetDatabase.CreateAsset(settings, AssetPath);
				log.AppendLine($"Made {AssetPath}.");
			}

			Dictionary<string, string> gunNames = GetGunNames();

			var guns      = new Dictionary<string, (List<AudioClip> Fire, List<AudioClip> Suppressed, List<AudioClip> Reload)>();
			var footsteps = new Dictionary<string, (List<AudioClip> Walk, List<AudioClip> Run)>(System.StringComparer.OrdinalIgnoreCase);
			var unmatched = new List<string>();

			foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder }).OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
			{
				string    path = AssetDatabase.GUIDToAssetPath(guid);
				string    name = Path.GetFileNameWithoutExtension(path);
				AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

				Match step = FootstepPattern.Match(name);
				if (step.Success == true)
				{
					string surface = step.Groups["surface"].Value;
					if (footsteps.TryGetValue(surface, out var lists) == false)
					{
						lists = (new List<AudioClip>(), new List<AudioClip>());
						footsteps.Add(surface, lists);
					}

					(step.Groups["run"].Success == true ? lists.Run : lists.Walk).Add(clip);
					continue;
				}

				Match gun = GunPattern.Match(name);
				if (gun.Success == true && gunNames.TryGetValue(gun.Groups["gun"].Value.ToLowerInvariant(), out string gunName) == true)
				{
					if (guns.TryGetValue(gunName, out var lists) == false)
					{
						lists = (new List<AudioClip>(), new List<AudioClip>(), new List<AudioClip>());
						guns.Add(gunName, lists);
					}

					string kind = gun.Groups["kind"].Value.ToLowerInvariant();
					(kind == "fire" ? lists.Fire : kind == "firesuppressed" ? lists.Suppressed : lists.Reload).Add(clip);
					continue;
				}

				unmatched.Add(name);
			}

			settings.Guns = guns.Select(pair => new SoundSettings.GunSounds { Gun = pair.Key, Fire = pair.Value.Fire.ToArray(), FireSuppressed = pair.Value.Suppressed.ToArray(), Reload = pair.Value.Reload.ToArray() }).ToArray();
			settings.Footsteps = footsteps.Select(pair => new SoundSettings.FootstepSounds { Surface = pair.Key, Walk = pair.Value.Walk.ToArray(), Run = pair.Value.Run.ToArray() }).ToArray();

			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssets();

			int clips = guns.Values.Sum(g => g.Fire.Count + g.Suppressed.Count + g.Reload.Count) + footsteps.Values.Sum(f => f.Walk.Count + f.Run.Count);
			log.AppendLine($"{clips} clips: {guns.Count} guns, {footsteps.Count} footstep surfaces.");
			if (unmatched.Count > 0)
			{
				log.AppendLine($"Not matched (check the name rule or the gun name): {string.Join(", ", unmatched)}");
			}

			return log.ToString();
		}

		/// <summary>Every sound slot and whether it plays Logan's clip or the sample's. Editor only, no Play mode.</summary>
		public static string RunSoundCheck()
		{
			SoundSettings settings = AssetDatabase.LoadAssetAtPath<SoundSettings>(AssetPath);
			var lines = new List<string> { $"Sound Check {System.DateTime.Now:yyyy-MM-dd HH:mm}", settings == null ? $"No {AssetPath} yet (run Brownout → Data → Import Sounds): every slot plays the sample's sound." : $"Settings: {AssetPath}" };

			int logan = 0, sample = 0;

			string Slot(AudioClip[] clips, string fallback)
			{
				if (SoundSettings.IsEmpty(clips) == false)
				{
					++logan;
					return $"Logan's ({string.Join(", ", clips.Where(c => c != null).Select(c => c.name))})";
				}

				++sample;
				return fallback;
			}

			lines.Add("");
			lines.Add("Guns (fire / suppressed fire / reload):");
			foreach (string gun in GetGunNames().Values.Distinct().OrderBy(n => n))
			{
				SoundSettings.GunSounds sounds = settings != null ? settings.FindGun(gun) : null;
				string fire       = Slot(sounds?.Fire, "sample");
				string suppressed = Slot(sounds?.FireSuppressed, SoundSettings.IsEmpty(sounds?.Fire) == false ? "falls back to Logan's fire" : "sample");
				string reload     = Slot(sounds?.Reload, "sample");
				lines.Add($"  {gun,-14} fire: {fire} | suppressed: {suppressed} | reload: {reload}");
			}

			lines.Add("");
			lines.Add("Footsteps (walk / run):");
			var surfaces = new List<string> { SoundSettings.DefaultSurface };
			foreach (string guid in AssetDatabase.FindAssets("t:FootstepSetup"))
			{
				var setup = AssetDatabase.LoadAssetAtPath<FootstepSetup>(AssetDatabase.GUIDToAssetPath(guid));
				if (setup != null)
				{
					surfaces.AddRange(setup.GetSurfaces().Where(s => string.IsNullOrEmpty(s) == false));
				}
			}

			foreach (string surface in surfaces.Distinct(System.StringComparer.OrdinalIgnoreCase))
			{
				SoundSettings.FootstepSounds sounds = settings != null ? settings.FindFootsteps(surface) : null;
				string walk = Slot(sounds?.Walk, "sample");
				string run  = Slot(sounds?.Run, SoundSettings.IsEmpty(sounds?.Walk) == false ? "falls back to Logan's walk" : "sample");
				lines.Add($"  {surface,-14} walk: {walk} | run: {run}");
			}

			lines.Add("");
			lines.Add("Ranges: gunshots use the sample's audio range × the gun's GunshotRange (Suppressor 0.65, brownout_guns.json); footsteps the sample's range (Ninja shortens it).");
			lines.Add($"Total: {logan} slots play Logan's clips, {sample} the sample's.");

			Directory.CreateDirectory("Logs/SoundCheck");
			File.WriteAllLines("Logs/SoundCheck/SoundCheck.txt", lines);
			return string.Join("\n", lines);
		}

		// PRIVATE METHODS

		/// <summary>The guns' names without spaces, lowercased → the GunData name.</summary>
		private static Dictionary<string, string> GetGunNames()
		{
			var names = new Dictionary<string, string>();
			foreach (string guid in AssetDatabase.FindAssets("t:GunData", new[] { "Assets/TPSBR/Data/Guns" }))
			{
				var gun = AssetDatabase.LoadAssetAtPath<GunData>(AssetDatabase.GUIDToAssetPath(guid));
				if (gun != null && string.IsNullOrEmpty(gun.GunName) == false)
				{
					names[gun.GunName.Replace(" ", string.Empty).ToLowerInvariant()] = gun.GunName;
				}
			}
			return names;
		}
	}
}
