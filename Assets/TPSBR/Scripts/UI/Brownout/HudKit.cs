namespace TPSBR.UI
{
	using TMPro;
	using UnityEngine;
	using UnityEngine.UI;

	/// <summary>
	/// Small helpers that build the phase 6a HUD in code: rects anchored to hud_rects.json boxes, flat images and TextMesh Pro texts, and
	/// the few shape sprites (white, circle, ring, edge vignette, frame, brackets, glow) made at runtime. Phase 11a: texts use the HudSettings
	/// fonts, Panel gives the shared panel look, and item icons come from IconSettings (SetIcon).
	/// </summary>
	public static class HudKit
	{
		// PRIVATE MEMBERS

		private static Sprite _white;
		private static Sprite _circle;
		private static Sprite _ring;
		private static Sprite _zoneRing;
		private static Sprite _vignette;

		// PUBLIC MEMBERS

		public static Sprite White    => _white    != null ? _white    : (_white    = MakeSprite(4, (x, y) => 1.0f));
		public static Sprite Circle   => _circle   != null ? _circle   : (_circle   = MakeSprite(64, (x, y) => Edge(1.0f - Radius(x, y), 64)));
		public static Sprite Ring     => _ring     != null ? _ring     : (_ring     = MakeSprite(64, (x, y) => Edge(1.0f - Radius(x, y), 64) * Edge(Radius(x, y) - 0.78f, 64)));
		/// <summary>The zone circle on the minimap and full map: a thin line (4.4% of the radius, Logan 6 Oct: 80% thinner than Ring), mipmapped so it stays smooth when small.</summary>
		public static Sprite ZoneRing => _zoneRing != null ? _zoneRing : (_zoneRing = MakeSprite(512, (x, y) => Edge(1.0f - Radius(x, y), 512) * Edge(Radius(x, y) - 0.956f, 512), true));
		public static Sprite Vignette => _vignette != null ? _vignette : (_vignette = MakeSprite(128, (x, y) => Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01((Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) - 0.6f) / 0.4f))));
		/// <summary>Phase 11a: a 1 px outline, nine-sliced (panel and slot edges).</summary>
		public static Sprite Frame    => _frame != null ? _frame : (_frame = MakeFrame());
		/// <summary>Phase 11a: four corner brackets, nine-sliced (the hovered slot, as in the reference).</summary>
		public static Sprite Brackets => _brackets != null ? _brackets : (_brackets = MakeBrackets());
		/// <summary>Phase 11a: a soft round glow (ground item hover, markers).</summary>
		public static Sprite Glow     => _glow != null ? _glow : (_glow = MakeSprite(64, (x, y) => Mathf.Pow(Mathf.Clamp01(1.0f - Radius(x, y)), 2.0f)));

		/// <summary>Phase 11a: the player arrow (minimap and full map), a notched triangle pointing up, white with a dark outline (tint it).</summary>
		public static Sprite Arrow    => _arrow != null ? _arrow : (_arrow = MakeArrow(64));
		/// <summary>Phase 11a: the minimap's thick dark border with a thin light line inside it, nine-sliced.</summary>
		public static Sprite MapFrame => _mapFrame != null ? _mapFrame : (_mapFrame = MakeMapFrame());
		/// <summary>Phase 11a: counter icons, a person (players alive) and a skull (kills).</summary>
		public static Sprite Person   => _person != null ? _person : (_person = MakeSprite(48, PersonAlpha));
		public static Sprite Skull    => _skull  != null ? _skull  : (_skull  = MakeSprite(48, SkullAlpha));
		/// <summary>Phase 11c: blotchy blood at the screen edges (stretched over the screen).</summary>
		public static Sprite Blood    => _blood  != null ? _blood  : (_blood  = MakeSprite(256, BloodAlpha));

		private static Sprite _frame;
		private static Sprite _brackets;
		private static Sprite _glow;
		private static Sprite _arrow;
		private static Sprite _mapFrame;
		private static Sprite _person;
		private static Sprite _skull;
		private static Sprite _blood;

		// PUBLIC METHODS

		public static RectTransform Rect(Transform parent, string name)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.layer = parent.gameObject.layer;
			var rect = (RectTransform)go.transform;
			rect.SetParent(parent, false);
			return rect;
		}

		/// <summary>Stretches the rect over a hud_rects.json box (normalized, (0, 0) at the top left).</summary>
		public static RectTransform Box(Transform parent, string name, HudSettings.Box box)
		{
			RectTransform rect = Rect(parent, name);
			rect.anchorMin = new Vector2(box.X0, 1.0f - box.Y1);
			rect.anchorMax = new Vector2(box.X1, 1.0f - box.Y0);
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			return rect;
		}

		/// <summary>Anchored inside its parent by normalized min/max ((0, 0) bottom left), with no offsets.</summary>
		public static RectTransform Stretch(RectTransform rect, Vector2 min, Vector2 max)
		{
			rect.anchorMin = min;
			rect.anchorMax = max;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			return rect;
		}

		/// <summary>A fixed-size rect centered on a point of its parent.</summary>
		public static RectTransform Fixed(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 pivot)
		{
			rect.anchorMin = anchor;
			rect.anchorMax = anchor;
			rect.pivot     = pivot;
			rect.sizeDelta = size;
			rect.anchoredPosition = Vector2.zero;
			return rect;
		}

		public static Image Image(Transform parent, string name, Color color, Sprite sprite = null)
		{
			RectTransform rect = Rect(parent, name);
			var image = rect.gameObject.AddComponent<Image>();
			image.sprite        = sprite != null ? sprite : White;
			image.color         = color;
			image.raycastTarget = false;
			return image;
		}

		/// <summary>A bar that fills left to right (or radially for a ring).</summary>
		public static Image Fill(Transform parent, string name, Color color, Sprite sprite = null, bool radial = false)
		{
			Image image = Image(parent, name, color, sprite);
			image.type       = UnityEngine.UI.Image.Type.Filled;
			image.fillMethod = radial == true ? UnityEngine.UI.Image.FillMethod.Radial360 : UnityEngine.UI.Image.FillMethod.Horizontal;
			image.fillOrigin = radial == true ? (int)UnityEngine.UI.Image.Origin360.Top : (int)UnityEngine.UI.Image.OriginHorizontal.Left;
			image.fillClockwise = false;
			image.fillAmount = 1.0f;
			return image;
		}

		public static TextMeshProUGUI Text(Transform parent, string name, float size, TextAlignmentOptions alignment, Color color, bool bold = false)
		{
			RectTransform rect = Rect(parent, name);
			var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
			HudSettings settings = HudSettings.Current;
			TMP_FontAsset font = bold == true && settings.FontBold != null ? settings.FontBold : settings.Font;

			if (font != null)
			{
				text.font = font;
				size *= settings.FontScale;
			}

			text.fontSize         = size;
			text.alignment        = alignment;
			text.color            = color;
			text.raycastTarget    = false;
			text.textWrappingMode = TextWrappingModes.NoWrap;
			text.overflowMode     = TextOverflowModes.Overflow;
			// Phase 11a: the SemiBold face instead of faux bold
			text.fontStyle        = bold == true && settings.FontBold == null ? FontStyles.Bold : FontStyles.Normal;
			text.text             = string.Empty;
			return text;
		}

		/// <summary>A label in spaced capitals (the reference's AMMO / INVENTORY / KILLED style).</summary>
		public static TextMeshProUGUI Label(Transform parent, string name, float size, TextAlignmentOptions alignment, Color color)
		{
			TextMeshProUGUI text = Text(parent, name, size, alignment, color, true);
			text.characterSpacing = HudSettings.Current.LabelSpacing;
			text.fontStyle       |= FontStyles.UpperCase;
			return text;
		}

		/// <summary>A panel: the dark translucent fill with a thin light edge, stretched over its parent. Returns the fill.</summary>
		public static Image Panel(RectTransform parent, string name, Color fill)
		{
			Image back = Image(parent, name, fill);
			Stretch(back.rectTransform, Vector2.zero, Vector2.one);

			Image edge = Image(back.rectTransform, "Edge", HudSettings.Current.PanelEdge, Frame);
			edge.type = UnityEngine.UI.Image.Type.Sliced;
			Stretch(edge.rectTransform, Vector2.zero, Vector2.one);
			return back;
		}

		/// <summary>Shrinks the text to fit its rect (down to minSize), then cuts it with an ellipsis.</summary>
		public static TextMeshProUGUI Fit(TextMeshProUGUI text, float minSize)
		{
			text.enableAutoSizing = true;
			text.fontSizeMin      = minSize;
			text.fontSizeMax      = text.fontSize;
			text.overflowMode     = TextOverflowModes.Ellipsis;
			return text;
		}

		/// <summary>Sets text only when it changed (TextMesh Pro rebuilds the mesh on every assignment).</summary>
		public static void Set(TextMeshProUGUI text, string value)
		{
			if (text.text != value)
			{
				text.text = value;
			}
		}

		public static void Show(Component component, bool visible)
		{
			if (component != null && component.gameObject.activeSelf != visible)
			{
				component.gameObject.SetActive(visible);
			}
		}

		// PRIVATE METHODS

		private static float Radius(float x, float y) => Mathf.Sqrt(x * x + y * y);

		private static float BloodAlpha(float x, float y)
		{
			// Thick at the edges and corners, ragged toward the middle, with darker blotches
			float edge   = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) * 0.65f + Radius(x, y) * 0.35f;
			float ragged = (Mathf.PerlinNoise(x * 3.1f + 11.0f, y * 3.1f + 5.0f) - 0.5f) * 0.45f + (Mathf.PerlinNoise(x * 9.0f + 3.0f, y * 9.0f + 7.0f) - 0.5f) * 0.15f;
			float body   = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01((edge + ragged - 0.62f) / 0.3f));
			float blotch = Mathf.Lerp(0.65f, 1.0f, Mathf.PerlinNoise(x * 6.0f + 21.0f, y * 6.0f + 2.0f));
			return body * blotch;
		}

		/// <summary>An anti-aliased edge: 0 below zero, 1 a pixel above it.</summary>
		private static float Edge(float value, int size) => Mathf.Clamp01(value * size * 0.5f);

		private static Sprite MakeFrame()
		{
			const int size = 8;
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
			var pixels  = new Color32[size * size];
			for (int y = 0; y < size; ++y)
				for (int x = 0; x < size; ++x)
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)(x == 0 || y == 0 || x == size - 1 || y == size - 1 ? 255 : 0));
			texture.SetPixels32(pixels);
			texture.Apply(false, true);
			return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100.0f, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
		}

		private static Sprite MakeBrackets()
		{
			const int size = 32, arm = 10, width = 2;
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
			var pixels  = new Color32[size * size];
			for (int y = 0; y < size; ++y)
			{
				for (int x = 0; x < size; ++x)
				{
					int  dx = Mathf.Min(x, size - 1 - x), dy = Mathf.Min(y, size - 1 - y);
					bool on = (dx < width && dy < arm) || (dy < width && dx < arm);
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)(on ? 255 : 0));
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(false, true);
			return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100.0f, 0, SpriteMeshType.FullRect, new Vector4(arm, arm, arm, arm));
		}

		private static readonly Vector2[] _arrowPoints = { new Vector2(0.0f, 0.92f), new Vector2(0.74f, -0.84f), new Vector2(0.0f, -0.42f), new Vector2(-0.74f, -0.84f) };

		/// <summary>The notched triangle, white inside a dark outline, 4 × 4 samples per pixel.</summary>
		private static Sprite MakeArrow(int size)
		{
			const int   samples = 4;
			const float outline = 0.16f;

			var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
			var pixels  = new Color32[size * size];

			for (int y = 0; y < size; ++y)
			{
				for (int x = 0; x < size; ++x)
				{
					float alpha = 0.0f, white = 0.0f;
					for (int sy = 0; sy < samples; ++sy)
					{
						for (int sx = 0; sx < samples; ++sx)
						{
							var p = new Vector2((x + (sx + 0.5f) / samples) / size * 2.0f - 1.0f, (y + (sy + 0.5f) / samples) / size * 2.0f - 1.0f);
							if (InsidePolygon(p, _arrowPoints) == false)
								continue;

							alpha += 1.0f;
							white += DistanceToPolygon(p, _arrowPoints) > outline ? 1.0f : 0.0f;
						}
					}

					float shade = alpha > 0.0f ? white / alpha : 0.0f;
					byte  value = (byte)Mathf.RoundToInt(Mathf.Lerp(0.08f, 1.0f, shade) * 255.0f);
					pixels[y * size + x] = new Color32(value, value, value, (byte)Mathf.RoundToInt(alpha / (samples * samples) * 255.0f));
				}
			}

			texture.SetPixels32(pixels);
			texture.Apply(true, true);
			return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100.0f);
		}

		private static bool InsidePolygon(Vector2 p, Vector2[] points)
		{
			bool inside = false;
			for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
			{
				if ((points[i].y > p.y) != (points[j].y > p.y) && p.x < (points[j].x - points[i].x) * (p.y - points[i].y) / (points[j].y - points[i].y) + points[i].x)
				{
					inside = !inside;
				}
			}
			return inside;
		}

		private static float DistanceToPolygon(Vector2 p, Vector2[] points)
		{
			float best = float.MaxValue;
			for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
			{
				Vector2 a = points[j], b = points[i];
				float   t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / Mathf.Max(1e-6f, (b - a).sqrMagnitude));
				best = Mathf.Min(best, Vector2.Distance(p, a + (b - a) * t));
			}
			return best;
		}

		/// <summary>5 px of near-black, then a 1 px light line, then clear (16 px, sliced at 6).</summary>
		private static Sprite MakeMapFrame()
		{
			const int size = 16, dark = 5, border = 6;
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
			var pixels  = new Color32[size * size];
			for (int y = 0; y < size; ++y)
			{
				for (int x = 0; x < size; ++x)
				{
					int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
					pixels[y * size + x] = edge < dark ? new Color32(14, 13, 12, 235) : (edge == dark ? new Color32(255, 255, 255, 70) : new Color32(0, 0, 0, 0));
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(false, true);
			return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100.0f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
		}

		private static float PersonAlpha(float x, float y)
		{
			const float aa = 24.0f;
			float head = (0.3f - new Vector2(x, y - 0.42f).magnitude) * aa;
			float body = (1.0f - Mathf.Sqrt((x / 0.68f) * (x / 0.68f) + ((y + 0.78f) / 0.62f) * ((y + 0.78f) / 0.62f))) * aa * 0.6f;
			body = Mathf.Min(body, (y + 0.86f) * aa);
			return Mathf.Max(head, body);
		}

		private static float SkullAlpha(float x, float y)
		{
			const float aa = 24.0f;
			float cranium = (0.62f - new Vector2(x, y - 0.14f).magnitude) * aa;
			float jaw     = Mathf.Min(Mathf.Min(0.36f - Mathf.Abs(x), y + 0.78f), 0.2f - y) * aa;
			float eyes    = (Mathf.Min(new Vector2(x - 0.25f, y - 0.06f).magnitude, new Vector2(x + 0.25f, y - 0.06f).magnitude) - 0.17f) * aa;
			float teeth   = y < -0.44f ? (Mathf.Abs(Mathf.Repeat(x + 0.06f, 0.24f) - 0.12f) - 0.03f) * aa * 2.0f : 1.0f;
			return Mathf.Min(Mathf.Min(Mathf.Max(cranium, jaw), eyes), teeth);
		}

		/// <summary>An item icon in an image (keeping its shape), or the old colored block when the item has no icon yet.</summary>
		public static void SetIcon(Image image, Sprite icon, Color fallback)
		{
			if (icon != null)
			{
				if (image.sprite != icon)
				{
					image.sprite = icon;
				}
				image.preserveAspect = true;
				image.color          = Color.white;
			}
			else
			{
				if (image.sprite != White)
				{
					image.sprite = White;
				}
				image.preserveAspect = false;
				image.color          = fallback;
			}
		}

		/// <summary>A white sprite whose alpha comes from the function of (x, y) in -1..1.</summary>
		private static Sprite MakeSprite(int size, System.Func<float, float, float> alpha, bool mipmaps = false)
		{
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipmaps) { wrapMode = TextureWrapMode.Clamp, filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear };
			var pixels  = new Color32[size * size];

			for (int y = 0; y < size; ++y)
			{
				for (int x = 0; x < size; ++x)
				{
					float u = (x + 0.5f) / size * 2.0f - 1.0f;
					float v = (y + 0.5f) / size * 2.0f - 1.0f;
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(u, v)) * 255.0f));
				}
			}

			texture.SetPixels32(pixels);
			texture.Apply(mipmaps, true);
			return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100.0f);
		}
	}
}
