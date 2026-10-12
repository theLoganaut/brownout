namespace TPSBR.Editor
{
	using UnityEditor;
	using UnityEngine;
	using TPSBR.UI;

	/// <summary>
	/// Phase 11c test tools: Brownout → Test → Feel. Switch the camera feel and the screen look off and on to compare, show a match
	/// moment, and log what the feel is doing right now.
	/// </summary>
	public static class BrownoutFeelTools
	{
		private const string MENU = "Brownout/Test/Feel/";

		[MenuItem(MENU + "Toggle Camera Feel")]
		public static void ToggleCameraFeel()
		{
			CameraFeel.DebugOff = CameraFeel.DebugOff == false;
			Debug.Log($"[Feel] Camera feel {(CameraFeel.DebugOff ? "OFF" : "ON")}");
		}

		[MenuItem(MENU + "Toggle Screen Look")]
		public static void ToggleScreenLook()
		{
			int count = 0;
			foreach (var volume in Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
			{
				if (volume.gameObject.name != "ScreenLook")
					continue;

				volume.enabled = volume.enabled == false;
				++count;
				Debug.Log($"[Feel] Screen look grade {(volume.enabled ? "ON" : "OFF")}");
			}
			if (count == 0)
			{
				Debug.Log("[Feel] No screen look: it's made in Play on the Brownout map.");
			}
		}

		[MenuItem(MENU + "Show Intro Banner")]
		public static void ShowIntro() => Announce("ELIMINATE ALL OTHER PLAYERS", "12 SQUADS");

		[MenuItem(MENU + "Show Zone Closing")]
		public static void ShowZone() => Announce("THE ZONE IS CLOSING", "MOVE INTO THE NEW ZONE");

		[MenuItem(MENU + "Show Squad Out")]
		public static void ShowSquadOut() => Announce("YOUR SQUAD IS OUT", string.Empty);

		[MenuItem(MENU + "Log Feel Status")]
		public static void LogStatus() => Debug.Log(GetStatus());

		public static string GetStatus()
		{
			var    hud   = BrownoutHud.Instance;
			Agent  agent = hud != null ? hud.Agent : null;
			string feel  = agent != null ? $"camera offset {agent.Character.CameraFeel.LastPosition.ToString("F3")} rotation {agent.Character.CameraFeel.LastRotation.ToString("F2")}" : "no agent";
			string hits  = hud != null ? $"marker {Get(hud, "hitmarker.last")}, feedback {Get(hud, "feedback")}, taken {Get(hud, "hit.taken")}, moment {Get(hud, "moment")}" : "no HUD";
			return $"[Feel] {feel}; screen {ScreenLook.Readout}; {hits}; camera feel {(CameraFeel.DebugOff ? "off" : "on")}";
		}

		private static string Get(BrownoutHud hud, string key) => hud.Readout.TryGetValue(key, out string value) == true ? value : "-";

		/// <summary>On every peer's HUD (in the helicopter nobody has an agent, so BrownoutHud.Instance can be the hidden peer's).</summary>
		private static void Announce(string title, string subtitle)
		{
			var huds = Object.FindObjectsByType<BrownoutHud>(FindObjectsSortMode.None);
			if (huds.Length == 0)
			{
				Debug.Log("[Feel] No HUD: enter Play first.");
				return;
			}

			foreach (BrownoutHud hud in huds)
			{
				hud.Announce(title, subtitle, FeelSettings.Current.AnnounceTime);
			}
		}
	}
}
