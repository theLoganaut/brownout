using TPSBR.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace TPSBR
{
	public class LoadingScene : MonoBehaviour
	{
		// PUBLIC MEMBERS

		public bool IsFading => _activeFader != null && _activeFader.IsFinished == false;

		// PRIVATE MEMBERS

		[SerializeField]
		private UIFader _fadeInObject;
		[SerializeField]
		private UIFader _fadeOutObject;
		[SerializeField]
		private TextMeshProUGUI _status;
		[SerializeField]
		private TextMeshProUGUI _statusDescription;
		[SerializeField]
		private UIYesNoDialogView _dialog;

		private UIFader _activeFader;

		// PUBLIC METHODS

		public void FadeIn()
		{
			_fadeInObject.SetActive(true);
			_fadeOutObject.SetActive(false);

			_activeFader = _fadeInObject;
		}

		public void FadeOut()
		{
			_dialog.Close_Internal();

			_fadeInObject.SetActive(false);
			_fadeOutObject.SetActive(true);

			_activeFader = _fadeOutObject;
		}

		// MONOBEHAVIOUR

		protected void Awake()
		{
			_fadeInObject.SetActive(false);
			_fadeOutObject.SetActive(false);

			_dialog.Initialize(null, null);

			// Phase 11b: Brownout's loading screen (map, tips, status) over the sample's
			UI.BrownoutLoading.Create(transform, () => _activeFader == _fadeInObject, () => _dialog != null && _dialog.IsOpen == true);

			ReplaceSampleLogos();
		}

		/// <summary>The sample's BR200 logo (one in each fader) becomes the BROWNOUT title in the HUD's font, like the main menu's.</summary>
		private void ReplaceSampleLogos()
		{
			foreach (UnityEngine.UI.Image image in GetComponentsInChildren<UnityEngine.UI.Image>(true))
			{
				if (image.sprite == null || image.sprite.name != "LogoBR200")
					continue;

				image.enabled = false;

				TextMeshProUGUI title = HudKit.Label(image.transform, "BrownoutTitle", 104.0f, TextAlignmentOptions.Center, HudSettings.Current.Text);
				HudKit.Stretch(title.rectTransform, Vector2.zero, Vector2.one);
				title.textWrappingMode   = TextWrappingModes.NoWrap;
				title.overflowMode       = TextOverflowModes.Overflow;
				title.characterSpacing   = 18.0f;
				title.text               = "BROWNOUT";
			}
		}

		protected void Update()
		{
			_status.text = Global.Networking.Status;
			_statusDescription.text = Global.Networking.StatusDescription;

			if (Keyboard.current.escapeKey.wasPressedThisFrame == true)
			{
				_dialog.Open_Internal();

				Cursor.lockState = CursorLockMode.None;
				Cursor.visible   = true;

				_dialog.HasClosed += (result) =>
				{
					if (result == true)
					{
						Global.Networking.StopGame();
					}
				};
			}
		}

		protected void OnDestroy()
		{
			if (_dialog != null)
			{
				_dialog.Deinitialize();
			}
		}
	}
}
