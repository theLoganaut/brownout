namespace TPSBR.UI
{
	using TMPro;
	using UnityEngine;
	using UnityEngine.UI;

	/// <summary>
	/// Phase 11b: Brownout's loading screen over the sample's (LoadingScene): the map picture with the POI names, a rotating tip and the
	/// connection status, in the HUD's style. Shown while the loading scene is faded in; hidden while its Esc dialog is open. Built in code
	/// on its own canvas (no edits to the LoadingScene scene). Map picture and POI names from BrownoutMapSettings, tips from HudSettings.
	/// </summary>
	public sealed class BrownoutLoading : MonoBehaviour
	{
		// PRIVATE MEMBERS

		private CanvasGroup     _group;
		private TextMeshProUGUI _tip;
		private TextMeshProUGUI _status;
		private RectTransform   _spinner;
		private int             _tipIndex = -1;
		private float           _nextTipTime;
		private float           _target;

		private System.Func<bool> _isShown;
		private System.Func<bool> _isDialogOpen;

		// PUBLIC METHODS

		public static BrownoutLoading Create(Transform parent, System.Func<bool> isShown, System.Func<bool> isDialogOpen)
		{
			var go = new GameObject("BrownoutLoading", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
			go.transform.SetParent(parent, false);

			// If the parent is under a canvas this one is nested: fill it and keep its own sorting
			var rect = (RectTransform)go.transform;
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;

			var canvas = go.GetComponent<Canvas>();
			canvas.renderMode      = RenderMode.ScreenSpaceOverlay;
			canvas.overrideSorting = true;
			canvas.sortingOrder    = 500;

			var scaler = go.GetComponent<CanvasScaler>();
			scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
			scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			scaler.matchWidthOrHeight  = 1.0f;

			var loading = go.AddComponent<BrownoutLoading>();
			loading._group        = go.GetComponent<CanvasGroup>();
			loading._group.alpha  = 0.0f;
			loading._group.blocksRaycasts = false;
			loading._isShown      = isShown;
			loading._isDialogOpen = isDialogOpen;
			loading.Build();
			return loading;
		}

		// MONOBEHAVIOUR

		private void Update()
		{
			bool shown = _isShown != null && _isShown() == true && (_isDialogOpen == null || _isDialogOpen() == false);
			_target = shown == true ? 1.0f : 0.0f;
			_group.alpha = Mathf.MoveTowards(_group.alpha, _target, Time.unscaledDeltaTime * 4.0f);

			if (_group.alpha <= 0.0f)
				return;

			HudSettings settings = HudSettings.Current;
			string[] tips = settings.LoadingTips;
			if (tips != null && tips.Length > 0 && Time.unscaledTime >= _nextTipTime)
			{
				_tipIndex    = _tipIndex < 0 ? Random.Range(0, tips.Length) : (_tipIndex + 1) % tips.Length;
				_nextTipTime = Time.unscaledTime + Mathf.Max(1.0f, settings.LoadingTipTime);
				HudKit.Set(_tip, tips[_tipIndex]);
			}

			Networking networking = Global.Networking;
			string status      = networking != null ? networking.Status : string.Empty;
			string description = networking != null ? networking.StatusDescription : string.Empty;
			HudKit.Set(_status, string.IsNullOrEmpty(description) == false ? $"{status.ToUpperInvariant()}\n<size=70%><color=#FFFFFF80>{description}</color></size>" : status.ToUpperInvariant());

			_spinner.localRotation = Quaternion.Euler(0.0f, 0.0f, -Time.unscaledTime * 240.0f);
		}

		// PRIVATE METHODS

		private void Build()
		{
			HudSettings s = HudSettings.Current;
			BrownoutMapSettings map = Global.Settings != null ? Global.Settings.BrownoutMap : null;

			RectTransform root = HudKit.Rect(transform, "Root");
			HudKit.Stretch(root, Vector2.zero, Vector2.one);
			HudKit.Stretch(HudKit.Image(root, "Black", new Color(0.02f, 0.02f, 0.025f, 1.0f)).rectTransform, Vector2.zero, Vector2.one);

			// The map on the right, with a frame and the POI names
			RectTransform mapBox = HudKit.Rect(root, "Map");
			mapBox.anchorMin = mapBox.anchorMax = new Vector2(1.0f, 0.5f);
			mapBox.pivot     = new Vector2(1.0f, 0.5f);
			mapBox.sizeDelta = new Vector2(820.0f, 820.0f);
			mapBox.anchoredPosition = new Vector2(-110.0f, 20.0f);

			if (map != null && map.Picture != null)
			{
				RawImage picture = HudKit.Rect(mapBox, "Picture").gameObject.AddComponent<RawImage>();
				picture.texture       = map.Picture;
				picture.color         = new Color(0.8f, 0.8f, 0.8f, 1.0f);
				picture.raycastTarget = false;
				HudKit.Stretch(picture.rectTransform, Vector2.zero, Vector2.one);

				for (int i = 0; map.PoiLabels != null && i < map.PoiLabels.Length; ++i)
				{
					BrownoutMapSettings.PoiLabel poi = map.PoiLabels[i];

					Image dot = HudKit.Image(mapBox, $"Dot{i}", s.Team, HudKit.Circle);
					HudKit.Fixed(dot.rectTransform, new Vector2(poi.X, poi.Y), new Vector2(10.0f, 10.0f), new Vector2(0.5f, 0.5f));

					RectTransform tag = HudKit.Rect(mapBox, $"Poi{i}");
					HudKit.Fixed(tag, new Vector2(poi.X, poi.Y), new Vector2(220.0f, 28.0f), new Vector2(0.5f, 0.0f)).anchoredPosition = new Vector2(0.0f, 10.0f);
					TextMeshProUGUI label = HudKit.Label(tag, "Name", 16.0f, TextAlignmentOptions.Center, s.Text);
					HudKit.Stretch(label.rectTransform, Vector2.zero, Vector2.one);
					label.text = poi.Name;
				}
			}
			else
			{
				HudKit.Panel(mapBox, "Empty", s.WidgetBack);
			}

			Image frame = HudKit.Image(mapBox, "Frame", Color.white, HudKit.MapFrame);
			frame.type = Image.Type.Sliced;
			HudKit.Stretch(frame.rectTransform, Vector2.zero, Vector2.one);
			frame.rectTransform.offsetMin = new Vector2(-5.0f, -5.0f);
			frame.rectTransform.offsetMax = new Vector2(5.0f, 5.0f);

			// Title and tip on the left
			TextMeshProUGUI title = HudKit.Label(root, "Title", 84.0f, TextAlignmentOptions.TopLeft, s.Text);
			Place(title.rectTransform, new Vector2(110.0f, -120.0f), new Vector2(800.0f, 110.0f));
			title.characterSpacing = 18.0f;
			title.text = "BROWNOUT";

			TextMeshProUGUI tipLabel = HudKit.Label(root, "TipLabel", 18.0f, TextAlignmentOptions.TopLeft, s.Team);
			Place(tipLabel.rectTransform, new Vector2(112.0f, -720.0f), new Vector2(700.0f, 26.0f));
			tipLabel.text = "TIP";

			_tip = HudKit.Text(root, "Tip", 26.0f, TextAlignmentOptions.TopLeft, s.Text);
			Place(_tip.rectTransform, new Vector2(112.0f, -754.0f), new Vector2(720.0f, 120.0f));
			_tip.textWrappingMode = TextWrappingModes.Normal;

			// Status and a spinner bottom-left
			_spinner = HudKit.Image(root, "Spinner", s.Team, HudKit.Ring).rectTransform;
			_spinner.anchorMin = _spinner.anchorMax = new Vector2(0.0f, 0.0f);
			_spinner.pivot     = new Vector2(0.5f, 0.5f);
			_spinner.sizeDelta = new Vector2(34.0f, 34.0f);
			_spinner.anchoredPosition = new Vector2(129.0f, 112.0f);
			Image spinnerImage = _spinner.GetComponent<Image>();
			spinnerImage.type       = Image.Type.Filled;
			spinnerImage.fillMethod = Image.FillMethod.Radial360;
			spinnerImage.fillAmount = 0.7f;

			_status = HudKit.Text(root, "Status", 22.0f, TextAlignmentOptions.Left, s.Text, true);
			_status.rectTransform.anchorMin = _status.rectTransform.anchorMax = new Vector2(0.0f, 0.0f);
			_status.rectTransform.pivot     = new Vector2(0.0f, 0.5f);
			_status.rectTransform.sizeDelta = new Vector2(700.0f, 60.0f);
			_status.rectTransform.anchoredPosition = new Vector2(164.0f, 112.0f);
		}

		private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
		{
			rect.anchorMin = new Vector2(0.0f, 1.0f);
			rect.anchorMax = new Vector2(0.0f, 1.0f);
			rect.pivot     = new Vector2(0.0f, 1.0f);
			rect.sizeDelta = size;
			rect.anchoredPosition = topLeft;
		}
	}
}
