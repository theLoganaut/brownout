namespace TPSBR
{
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 3b: compares each gun's reload clip with its reload time in the data. Clips are named `<Name>_Reload`
	/// (Blender Pipeline Brief, e.g. `Icarus_Reload`, `AlibiDMR_Reload`) anywhere under Assets/Brownout/Art/.
	/// Only warns: the game never takes timing from a clip (ammo-in is a server timer from brownout_guns.json).
	/// </summary>
	public sealed class BrownoutReloadClipCheck : AssetPostprocessor
	{
		// CONSTANTS

		public const string ArtFolder = "Assets/Brownout/Art";

		private const string RELOAD_SUFFIX = "_Reload";

		// MENU

		[MenuItem("Brownout/Data/Check Reload Clips", priority = 2)]
		private static void CheckReloadClipsMenu()
		{
			Debug.Log(CheckReloadClips());
		}

		// PUBLIC METHODS

		/// <summary>
		/// One line per gun: clip found or missing, clip length, data length, difference.
		/// </summary>
		public static string CheckReloadClips()
		{
			Dictionary<string, AnimationClip> clips = FindReloadClips(AssetDatabase.FindAssets("t:Model t:AnimationClip", new[] { ArtFolder }).Select(AssetDatabase.GUIDToAssetPath).Distinct());
			List<GunData>                     guns  = LoadGunData();

			var builder = new StringBuilder();
			int found   = 0;
			int off     = 0;

			foreach (GunData gun in guns)
			{
				string key = GetClipKey(gun);
				if (clips.TryGetValue(key, out AnimationClip clip) == false)
				{
					builder.AppendLine($"{gun.GunName}: no {key}{RELOAD_SUFFIX} clip (data {gun.ReloadTime:F2} s)");
					continue;
				}

				++found;
				bool matches = IsLengthOk(clip, gun, out float difference, out float tolerance);
				off += matches == true ? 0 : 1;

				builder.AppendLine($"{gun.GunName}: {(matches ? "OK" : "OFF")} clip {clip.length:F3} s, data {gun.ReloadTime:F2} s, difference {difference * 1000.0f:+0;-0} ms (allowed ±{tolerance * 1000.0f:F0} ms) [{AssetDatabase.GetAssetPath(clip)}]");
			}

			builder.Insert(0, $"Reload clips: {found} of {guns.Count} guns have one, {off} differ from the data by more than one frame.\n");
			return builder.ToString();
		}

		// AssetPostprocessor INTERFACE

		private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			IEnumerable<string> paths = importedAssets.Concat(movedAssets).Where(t => t.StartsWith(ArtFolder + "/") == true && (t.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) == true || t.EndsWith(".anim") == true));
			if (paths.Any() == false)
				return;

			Dictionary<string, AnimationClip> clips = FindReloadClips(paths);
			if (clips.Count == 0)
				return;

			foreach (GunData gun in LoadGunData())
			{
				if (clips.TryGetValue(GetClipKey(gun), out AnimationClip clip) == false)
					continue;

				if (IsLengthOk(clip, gun, out float difference, out float tolerance) == false)
				{
					Debug.LogWarning($"Reload clip {clip.name} is {clip.length:F3} s but {gun.GunName}'s reload_s is {gun.ReloadTime:F2} s ({difference * 1000.0f:+0;-0} ms, more than one frame). The game uses the data's time; fix the clip or the data. ({AssetDatabase.GetAssetPath(clip)})", clip);
				}
			}
		}

		// PRIVATE METHODS

		private static bool IsLengthOk(AnimationClip clip, GunData gun, out float difference, out float tolerance)
		{
			difference = clip.length - gun.ReloadTime;
			tolerance  = 1.0f / Mathf.Max(1.0f, clip.frameRate) + 0.0001f;
			return Mathf.Abs(difference) <= tolerance;
		}

		/// <summary>Gun name without spaces, the way the art files are named (Alibi DMR → AlibiDMR).</summary>
		private static string GetClipKey(GunData gun)
		{
			return gun.GunName.Replace(" ", string.Empty);
		}

		/// <summary>
		/// Reload clips by gun key. FBX takes can come in as "Armature|Icarus_Reload", so the name only has to end with `<Name>_Reload`.
		/// </summary>
		private static Dictionary<string, AnimationClip> FindReloadClips(IEnumerable<string> assetPaths)
		{
			var clips = new Dictionary<string, AnimationClip>();

			foreach (string path in assetPaths)
			{
				foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
				{
					var clip = asset as AnimationClip;
					if (clip == null || clip.name.StartsWith("__preview__") == true || clip.name.EndsWith(RELOAD_SUFFIX) == false)
						continue;

					string name = clip.name.Substring(0, clip.name.Length - RELOAD_SUFFIX.Length);
					int    bar  = Mathf.Max(name.LastIndexOf('|'), name.LastIndexOf('/'));
					clips[bar >= 0 ? name.Substring(bar + 1) : name] = clip;
				}
			}

			return clips;
		}

		private static List<GunData> LoadGunData()
		{
			return AssetDatabase.FindAssets("t:GunData", new[] { BrownoutGunImporter.DataFolder })
				.Select(guid => AssetDatabase.LoadAssetAtPath<GunData>(AssetDatabase.GUIDToAssetPath(guid)))
				.Where(t => t != null)
				.OrderBy(t => t.name)
				.ToList();
		}
	}
}
