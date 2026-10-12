namespace TPSBR
{
	using System.IO;
	using UnityEditor.Build;
	using UnityEditor.Build.Reporting;
	using UnityEngine;

	/// <summary>
	/// Phase 11b: after a Windows build, copies steam_appid.txt (App ID 480 Spacewar while in development) from the project root next to
	/// the game's exe, so Steam starts without the game being launched from the Steam client. Remove the file from builds once Brownout
	/// has its own App ID and ships through Steam.
	/// </summary>
	public sealed class BrownoutBuildSteps : IPostprocessBuildWithReport
	{
		public int callbackOrder => 0;

		public void OnPostprocessBuild(BuildReport report)
		{
			if (report.summary.platform != UnityEditor.BuildTarget.StandaloneWindows64 && report.summary.platform != UnityEditor.BuildTarget.StandaloneWindows)
				return;

			string source = Path.Combine(Directory.GetCurrentDirectory(), "steam_appid.txt");
			if (File.Exists(source) == false)
			{
				Debug.LogWarning("BrownoutBuildSteps: no steam_appid.txt at the project root; the build won't start Steam unless launched from Steam.");
				return;
			}

			string folder = Path.GetDirectoryName(report.summary.outputPath);
			File.Copy(source, Path.Combine(folder, "steam_appid.txt"), true);
			Debug.Log($"BrownoutBuildSteps: copied steam_appid.txt to {folder}.");
		}
	}
}
