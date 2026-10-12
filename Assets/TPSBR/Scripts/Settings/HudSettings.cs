namespace TPSBR
{
	using System;
	using UnityEngine;

	/// <summary>
	/// HUD numbers (phase 6a), from the HUD & UI page, hud_rects.json and the HUD & gameplay feel reference.
	/// Lives in Resources/Settings/HudSettings.asset, linked from GlobalSettings.Hud (Brownout → Setup → Build HUD, which also reads
	/// References/hud_rects.json into Boxes, so moving an element is a data change).
	/// </summary>
	[CreateAssetMenu(fileName = "HudSettings", menuName = "TPSBR/HUD Settings")]
	public sealed class HudSettings : ScriptableObject
	{
		/// <summary>One element's box, normalized to the screen with (0, 0) at the top left, as in hud_rects.json.</summary>
		[Serializable]
		public struct Box
		{
			public int    Id;
			public string Element;
			public float  X0;
			public float  Y0;
			public float  X1;
			public float  Y1;
		}

		// The hud_rects.json ids
		public const int Minimap   = 1;
		public const int ZoneTimer = 2;
		public const int Compass   = 3;
		public const int Counters  = 4;
		public const int KillFeed  = 5;
		public const int Squad     = 6;
		public const int Health    = 7;
		public const int HealItem  = 8;
		public const int Armor     = 9;
		public const int QuickSlot = 10;
		public const int Equipment = 11;
		public const int Ammo      = 12;
		public const int AmmoType  = 13;
		public const int Gun       = 14;

		[Header("Layout (read from References/hud_rects.json by Brownout → Setup → Build HUD)")]
		public Box[] Boxes = new Box[0];
		[Tooltip("The progress bar (revive, heal), normalized: x 0.36-0.61 at y 0.71 (reference).")]
		public Vector4 ProgressBar   = new Vector4(0.36f, 0.705f, 0.61f, 0.715f);
		[Tooltip("The reference was measured at this height; text and marker sizes scale from it.")]
		public float   ReferenceHeight = 1080.0f;

		[Header("Style (phase 11a: one look for the HUD, inventory, map and menus; fonts made by Build HUD from Assets/Brownout/UI/Fonts)")]
		[Tooltip("Barlow Condensed (SIL Open Font License): body text and numbers.")]
		public TMPro.TMP_FontAsset Font;
		[Tooltip("Barlow Condensed SemiBold: labels, names, big numbers (used for every bold text instead of faux bold).")]
		public TMPro.TMP_FontAsset FontBold;
		[Tooltip("Spaced capitals on labels (TextMesh Pro character spacing, em/100).")]
		public float   LabelSpacing    = 12.0f;
		[Tooltip("Thin light edge around panels and slots.")]
		public Color   PanelEdge       = new Color(1.0f, 1.0f, 1.0f, 0.14f);
		[Tooltip("Text sizes were set for the default font; Barlow Condensed is narrower and reads smaller, so every size is scaled by this.")]
		public float   FontScale       = 1.12f;

		[Header("Colors")]
		public Color Text          = new Color(0.95f, 0.95f, 0.95f, 1.0f);
		public Color Dim           = new Color(0.95f, 0.95f, 0.95f, 0.35f);
		public Color Panel         = new Color(0.0f, 0.0f, 0.0f, 0.35f);
		[Tooltip("Phase 11a: the darker boxes of the bottom HUD strips (health, items, ammo, gun), like the reference.")]
		public Color WidgetBack    = new Color(0.03f, 0.03f, 0.035f, 0.78f);
		public Color Team          = new Color(1.0f, 0.85f, 0.25f, 1.0f);
		public Color Enemy         = new Color(0.95f, 0.25f, 0.2f, 1.0f);
		public Color HealthColor   =new Color(0.95f, 0.95f, 0.95f, 1.0f);
		public Color ArmorTail     = new Color(1.0f, 0.8f, 0.2f, 1.0f);
		public Color BleedOut      = new Color(0.9f, 0.15f, 0.1f, 1.0f);
		public Color Ping          = new Color(0.3f, 0.65f, 1.0f, 1.0f);
		public Color Gear          = new Color(0.4f, 0.9f, 0.5f, 1.0f);

		[Header("Center")]
		[Tooltip("Hit marker size at 1080p (reference: about 45 px).")]
		public float HitMarkerSize     = 45.0f;
		[Tooltip("The hit marker clears this long after the last hit (reference: 0.1-0.2 s).")]
		public float HitMarkerTime     = 0.15f;
		[Tooltip("DOWNED / KILLED banner time (reference: 1.65 s).")]
		public float BannerTime        = 1.65f;
		[Tooltip("Crosshair gap at zero spread, px at 1080p.")]
		public float CrosshairGap      = 8.0f;
		[Tooltip("Extra crosshair gap per degree of spread, px at 1080p.")]
		public float CrosshairPerDegree = 6.0f;
		public float CrosshairLine     = 10.0f;
		[Tooltip("Red at the screen edges starts below this share of the normal maximum health.")]
		public float LowHealthEdge     = 0.5f;

		[Header("Minimap and compass")]
		[Tooltip("Meters across the minimap.")]
		public float MinimapMeters     = 150.0f;
		[Tooltip("Degrees of heading the compass strip shows edge to edge.")]
		public float CompassDegrees    = 180.0f;

		[Header("Kill feed")]
		public float KillFeedTime      = 6.0f;
		public int   KillFeedRows      = 5;

		[Header("Ping")]
		[Tooltip("How far a ping reaches (m).")]
		public float PingRange         = 500.0f;
		[Tooltip("How long a ping lasts (s).")]
		public float PingTime          = 20.0f;
		[Tooltip("Middle mouse within this many degrees of your own ping removes it.")]
		public float PingRemoveAngle   = 3.0f;

		[Header("Inventory and stash (phase 6b)")]
		[Tooltip("Stash items per row (reference: 8).")]
		public int   StashColumns      = 8;
		[Tooltip("Stash rows shown before it scrolls.")]
		public int   StashRows         = 3;
		public Color SlotBack          = new Color(0.025f, 0.028f, 0.032f, 0.86f);
		public Color SlotDim           = new Color(0.02f, 0.02f, 0.02f, 0.3f);
		public Color Hover             = new Color(1.0f, 0.85f, 0.25f, 1.0f);
		[Tooltip("Phase 11a (Logan, 10 Oct: darker): how dark the screen goes behind the open inventory (0-1).")]
		public float InventoryShade    = 0.65f;
		[Tooltip("The light frame round the big equipped slots (Health, Armor, Gear, Storage, Equipment) and the selected weapon tab.")]
		public Color SlotFrame         = new Color(0.85f, 0.85f, 0.82f, 0.55f);
		[Tooltip("The AMMO / STASH / BACKPACK chips and the selected weapon tab.")]
		public Color Chip              = new Color(0.78f, 0.78f, 0.75f, 0.9f);

		[Header("Full map (phase 6b)")]
		[Tooltip("Grid squares per side, lettered A.. across and numbered 1.. down (reference: 8).")]
		public int   MapGrid           = 8;
		[Tooltip("The map's height as a share of the screen's.")]
		public float MapScreenShare    = 0.86f;
		[Tooltip("Most zoom the scroll wheel gives.")]
		public float MapMaxZoom        = 4.0f;
		[Tooltip("A click this close (px at 1080p) to your waypoint removes it.")]
		public float WaypointRemovePixels = 18.0f;
		public Color Waypoint          = new Color(0.25f, 0.95f, 0.55f, 1.0f);
		public Color ZoneCurrent       = new Color(1.0f, 1.0f, 1.0f, 0.9f);
		public Color ZoneNext          = new Color(0.3f, 0.65f, 1.0f, 0.9f);

		[Header("Loading screen (phase 11b)")]
		[Tooltip("One shows at a time, changing every LoadingTipTime seconds.")]
		public string[] LoadingTips = new string[]
		{
			"Armor only stops bullets. Tier 3 also covers your head.",
			"Slide out of a sprint and skip to chain your speed. Steep slopes break the chain.",
			"Hold breath on a scope to steady it.",
			"A downed teammate bleeds out: revive them with F.",
			"Drag items in the inventory: onto the gun to attach, out of the panel to drop.",
			"Jump out early to land first, or ride to the end of the path for a quiet drop.",
			"Gear runs for 10 seconds. Toggle it with Left Ctrl.",
			"The zone hurts more each step. Watch the timer under the minimap."
		};
		public float   LoadingTipTime = 6.0f;

		[Header("Victory (phase 6b; shown by a test tool until phase 7)")]
		public float VictoryTime       = 8.0f;

		// PUBLIC MEMBERS

		private static HudSettings _fallback;

		/// <summary>The asset linked from GlobalSettings. Falls back to the defaults above (with a warning) if it isn't linked yet.</summary>
		public static HudSettings Current
		{
			get
			{
				HudSettings settings = Global.Settings != null ? Global.Settings.Hud : null;
				if (settings != null)
					return settings;

				if (_fallback == null)
				{
					_fallback = CreateInstance<HudSettings>();
					Debug.LogWarning("HudSettings isn't linked in GlobalSettings; using the code defaults. Run Brownout → Setup → Build HUD.");
				}

				return _fallback;
			}
		}

		// PUBLIC METHODS

		/// <summary>The element's box, or a zero box when it's missing.</summary>
		public Box GetBox(int id)
		{
			for (int i = 0; Boxes != null && i < Boxes.Length; ++i)
			{
				if (Boxes[i].Id == id)
					return Boxes[i];
			}

			return default;
		}
	}
}
