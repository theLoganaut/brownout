namespace TPSBR.UI
{
	using System.Collections.Generic;
	using TMPro;
	using UnityEngine;
	using UnityEngine.InputSystem;
	using UnityEngine.InputSystem.Controls;
	using UnityEngine.UI;

	/// <summary>
	/// The Controls page of the settings menu (phase 6b, HUD & UI and Controls pages): hold or toggle for crouch, sprint and aim, and a
	/// key for every action on the Controls page (click one, press the new key or mouse button; Esc cancels), with a reset. Built in code
	/// into the sample's settings popup by UISettingsView, behind a CONTROLS button next to Reset, so no prefab is edited. Changes apply
	/// at once and are saved per player straight away (KeyBindings, RuntimeSettings). The sensitivity sliders stay on the main page.
	/// </summary>
	public sealed class ControlsPanel : MonoBehaviour
	{
		// PRIVATE MEMBERS

		private sealed class KeyRow
		{
			public EBinding        Action;
			public TextMeshProUGUI Key;
			public Image           Back;
		}

		private RuntimeSettings     _settings;
		private TMP_FontAsset       _font;
		private RectTransform       _panel;
		private TextMeshProUGUI     _crouchMode;
		private TextMeshProUGUI     _sprintMode;
		private TextMeshProUGUI     _aimMode;
		private readonly List<KeyRow> _rows = new List<KeyRow>(24);

		private KeyRow              _listening;
		private int                 _listenFrame;

		private static readonly Color ButtonColor    = new Color(0.16f, 0.17f, 0.19f, 1.0f);
		private static readonly Color ListeningColor = new Color(0.55f, 0.45f, 0.10f, 1.0f);
		private static readonly Color TextColor      = new Color(0.95f, 0.95f, 0.95f, 1.0f);
		private static readonly Color DimColor       = new Color(0.95f, 0.95f, 0.95f, 0.55f);

		// PUBLIC METHODS

		/// <summary>Adds the CONTROLS button (a copy of the Reset button) and the hidden panel over the popup.</summary>
		public static ControlsPanel Attach(RectTransform popup, UIButton resetButton, RuntimeSettings settings)
		{
			var panel = popup.gameObject.AddComponent<ControlsPanel>();
			panel._settings = settings;

			TextMeshProUGUI anyText = popup.GetComponentInChildren<TextMeshProUGUI>(true);
			panel._font = anyText != null ? anyText.font : null;

			// The CONTROLS button, left of Reset
			UIButton button = Instantiate(resetButton, resetButton.transform.parent);
			button.name = "ControlsButton";
			button.onClick.RemoveAllListeners();
			((RectTransform)button.transform).anchoredPosition = ((RectTransform)resetButton.transform).anchoredPosition + new Vector2(-250.0f, 0.0f);
			TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
			if (label != null)
			{
				// The sample's buttons may localize or set their text; ours just says CONTROLS
				label.text = "CONTROLS";
			}
			button.onClick.AddListener(() => panel.SetOpen(true));

			panel.Build(popup);
			return panel;
		}

		public void SetOpen(bool open)
		{
			_listening = null;
			_panel.gameObject.SetActive(open);
			if (open == true)
			{
				Refresh();
			}
		}

		// MONOBEHAVIOUR

		private void OnDisable()
		{
			if (_panel != null)
			{
				SetOpen(false);
			}
		}

		private void Update()
		{
			if (_listening == null || Time.frameCount <= _listenFrame)
				return;

			Keyboard keyboard = Keyboard.current;
			if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame == true)
			{
				_listening = null;
				Refresh();
				return;
			}

			ButtonControl pressed = KeyBindings.GetPressedControl();
			if (pressed == null)
				return;

			KeyBindings.SetPath(_listening.Action, KeyBindings.GetPathFor(pressed));
			_listening = null;
			Refresh();
		}

		// PRIVATE METHODS

		private void Build(RectTransform popup)
		{
			_panel = HudKit.Rect(popup, "ControlsPanel");
			HudKit.Stretch(_panel, Vector2.zero, Vector2.one);
			Image back = HudKit.Image(_panel, "Back", new Color(0.07f, 0.08f, 0.09f, 0.98f));
			back.raycastTarget = true; // Blocks clicks to the page under it
			HudKit.Stretch(back.rectTransform, Vector2.zero, Vector2.one);

			Label(_panel, "CONTROLS", new Vector2(40.0f, -20.0f), new Vector2(300.0f, 30.0f), 26.0f, TextColor, true);
			Button(_panel, "BACK", new Vector2(-40.0f - 150.0f, -18.0f), new Vector2(150.0f, 34.0f), () => SetOpen(false), true);

			// Hold or toggle
			Label(_panel, "HOLD OR TOGGLE", new Vector2(40.0f, -66.0f), new Vector2(300.0f, 22.0f), 15.0f, DimColor, true);
			_crouchMode = ModeButton("Crouch", 0, () => { _settings.ToggleCrouch = _settings.ToggleCrouch == false; Refresh(); });
			_sprintMode = ModeButton("Sprint", 1, () => { _settings.ToggleSprint = _settings.ToggleSprint == false; Refresh(); });
			_aimMode    = ModeButton("Aim",    2, () => { _settings.ToggleAim    = _settings.ToggleAim    == false; Refresh(); });

			// Keys, two columns
			Label(_panel, "KEYS  (click one, then press the new key or mouse button; Esc cancels)", new Vector2(40.0f, -138.0f), new Vector2(700.0f, 22.0f), 15.0f, DimColor, true);

			ControlsSettings controls = ControlsSettings.Current;
			int count    = KeyBindings.Count;
			int perColumn = (count + 1) / 2;
			for (int i = 0; i < count; ++i)
			{
				EBinding action = (EBinding)i;
				float    x      = 40.0f + (i / perColumn) * 370.0f;
				float    y      = -166.0f - (i % perColumn) * 34.0f;

				Label(_panel, controls.GetLabel(action), new Vector2(x, y - 4.0f), new Vector2(200.0f, 26.0f), 16.0f, TextColor, false);

				var row = new KeyRow { Action = action };
				row.Back = Button(_panel, "-", new Vector2(x + 200.0f, y), new Vector2(140.0f, 28.0f), () => StartListening(row), false, out row.Key);
				_rows.Add(row);
			}

			// Reset, and what the sliders mean
			Button(_panel, "RESET CONTROLS", new Vector2(-40.0f - 220.0f, -560.0f), new Vector2(220.0f, 40.0f), ResetControls, true);
			TextMeshProUGUI note = Label(_panel, "Sensitivity and ADS sensitivity (main page) multiply the default: about 30 cm of mouse per turn at 1000 DPI, and aiming moves at the same speed on screen at any zoom.",
				new Vector2(40.0f, -556.0f), new Vector2(460.0f, 50.0f), 13.0f, DimColor, false);
			note.textWrappingMode = TextWrappingModes.Normal;

			_panel.gameObject.SetActive(false);
		}

		private TextMeshProUGUI ModeButton(string name, int index, UnityEngine.Events.UnityAction onClick)
		{
			float x = 40.0f + index * 240.0f;
			Label(_panel, name, new Vector2(x, -94.0f), new Vector2(90.0f, 30.0f), 18.0f, TextColor, false);
			Button(_panel, "HOLD", new Vector2(x + 90.0f, -92.0f), new Vector2(120.0f, 30.0f), onClick, true, out TextMeshProUGUI text);
			return text;
		}

		private void StartListening(KeyRow row)
		{
			_listening   = row;
			_listenFrame = Time.frameCount;
			Refresh();
		}

		private void ResetControls()
		{
			KeyBindings.ResetAll();
			_settings.ToggleCrouch = false;
			_settings.ToggleSprint = false;
			_settings.ToggleAim    = false;
			_listening = null;
			Refresh();
		}

		private void Refresh()
		{
			_crouchMode.text = _settings.ToggleCrouch == true ? "TOGGLE" : "HOLD";
			_sprintMode.text = _settings.ToggleSprint == true ? "TOGGLE" : "HOLD";
			_aimMode.text    = _settings.ToggleAim    == true ? "TOGGLE" : "HOLD";

			for (int i = 0; i < _rows.Count; ++i)
			{
				KeyRow row       = _rows[i];
				bool   listening = row == _listening;
				row.Key.text     = listening == true ? "press a key..." : KeyBindings.GetKeyName(row.Action);
				row.Back.color   = listening == true ? ListeningColor : ButtonColor;
			}
		}

		private TextMeshProUGUI Label(RectTransform parent, string text, Vector2 topLeft, Vector2 size, float fontSize, Color color, bool bold)
		{
			TextMeshProUGUI label = HudKit.Text(parent, text, fontSize, TextAlignmentOptions.MidlineLeft, color, bold);
			if (_font != null)
			{
				label.font = _font;
			}
			Place(label.rectTransform, topLeft, size, topLeft.x < 0.0f);
			label.text = text;
			return label;
		}

		private Image Button(RectTransform parent, string text, Vector2 topLeft, Vector2 size, UnityEngine.Events.UnityAction onClick, bool bold)
		{
			return Button(parent, text, topLeft, size, onClick, bold, out _);
		}

		private Image Button(RectTransform parent, string text, Vector2 topLeft, Vector2 size, UnityEngine.Events.UnityAction onClick, bool bold, out TextMeshProUGUI label)
		{
			Image back = HudKit.Image(parent, $"{text}Button", ButtonColor);
			back.raycastTarget = true;
			Place(back.rectTransform, topLeft, size, topLeft.x < 0.0f);

			var button = back.gameObject.AddComponent<Button>();
			button.targetGraphic = back;
			ColorBlock colors = button.colors;
			colors.highlightedColor = new Color(1.3f, 1.3f, 1.2f, 1.0f);
			colors.pressedColor     = new Color(0.8f, 0.8f, 0.8f, 1.0f);
			button.colors = colors;
			button.onClick.AddListener(onClick);

			label = HudKit.Text(back.rectTransform, "Text", 15.0f, TextAlignmentOptions.Center, TextColor, bold);
			if (_font != null)
			{
				label.font = _font;
			}
			HudKit.Stretch(label.rectTransform, Vector2.zero, Vector2.one);
			label.text = text;
			return back;
		}

		/// <summary>Top-left placement in the popup; a negative x counts from the right edge.</summary>
		private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size, bool fromRight)
		{
			rect.anchorMin = rect.anchorMax = new Vector2(fromRight == true ? 1.0f : 0.0f, 1.0f);
			rect.pivot     = new Vector2(0.0f, 1.0f);
			rect.sizeDelta = size;
			rect.anchoredPosition = topLeft;
		}
	}
}
