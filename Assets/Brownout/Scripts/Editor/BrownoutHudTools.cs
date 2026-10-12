namespace TPSBR
{
	using System.IO;
	using System.Linq;
	using System.Text;
	using TPSBR.UI;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 6a test tools (Brownout → Test → HUD): log what the HUD shows (BrownoutHud.Readout), save a screenshot to Logs/HudCheck/,
	/// add a fake kill feed row. The HUD Check itself is in BrownoutHudCheck.cs.
	/// </summary>
	public static class BrownoutHudTools
	{
		// CONSTANTS

		public const string LogFolder = "Logs/HudCheck";

		private const string MENU = "Brownout/Test/HUD/";

		// MENU

		[MenuItem(MENU + "Log HUD Readout", priority = 100)]   private static void LogMenu()        => Debug.Log(GetReadout());
		[MenuItem(MENU + "Save Screenshot", priority = 101)]   private static void ScreenshotMenu() => Debug.Log(SaveScreenshot("manual"));
		[MenuItem(MENU + "Fake Kill Feed Row", priority = 102)] private static void FakeFeedMenu()  => Debug.Log(FakeKillFeed());

		[MenuItem(MENU + "Show Layout Boxes", priority = 110)]
		private static void LayoutMenu() => BrownoutHud.ShowLayoutBoxes = !BrownoutHud.ShowLayoutBoxes;
		[MenuItem(MENU + "Show Layout Boxes", true)]
		private static bool ValidateLayoutMenu()
		{
			UnityEditor.Menu.SetChecked(MENU + "Show Layout Boxes", BrownoutHud.ShowLayoutBoxes);
			return true;
		}

		/// <summary>Layout sheet: a screenshot with every hud_rects.json box outlined over the HUD, saved as Logs/HudCheck/layout.png.</summary>
		public static string SaveLayoutSheet()
		{
			BrownoutHud.ShowLayoutBoxes = true;
			string result = SaveScreenshot("layout");
			EditorApplication.delayCall += () => EditorApplication.delayCall += () => BrownoutHud.ShowLayoutBoxes = false;
			return result;
		}

		[MenuItem(MENU + "Log HUD Readout", true)]
		[MenuItem(MENU + "Save Screenshot", true)]
		[MenuItem(MENU + "Fake Kill Feed Row", true)]
		private static bool ValidatePlaying() => Application.isPlaying;

		// PUBLIC METHODS

		/// <summary>Every value the HUD shows right now, one per line, sorted.</summary>
		public static string GetReadout()
		{
			BrownoutHud hud = BrownoutHud.Instance;
			if (hud == null)
				return "No Brownout HUD (not in a match?).";

			var text = new StringBuilder();
			text.AppendLine($"HUD for {(hud.Agent != null ? hud.Agent.name : "nobody")}:");

			foreach (var pair in hud.Readout.OrderBy(p => p.Key))
			{
				text.AppendLine($"  {pair.Key} = {pair.Value}");
			}

			return text.ToString();
		}

		/// <summary>Saves the Game view (with the HUD) to Logs/HudCheck/&lt;name&gt;.png at the end of the frame.</summary>
		public static string SaveScreenshot(string name)
		{
			string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, LogFolder);
			Directory.CreateDirectory(folder);

			string path = Path.Combine(folder, $"{name}.png");
			ScreenCapture.CaptureScreenshot(path);
			return $"Screenshot will be saved to {path}.";
		}

		public static string FakeKillFeed()
		{
			BrownoutHud hud = BrownoutHud.Instance;
			if (hud == null)
				return "No Brownout HUD.";

			hud.OnAgentDeath(new KillData
			{
				KillerRef = hud.Agent != null ? hud.Agent.Object.InputAuthority : default,
				VictimRef = default,
				HitType   = EHitType.Rifle,
				KillerTeam = (byte)GameplayMode.GetTeam(hud.Agent),
				VictimTeam = 200,
				GunIndex   = 1,
			});

			return "Added a fake kill feed row.";
		}
	}
}
