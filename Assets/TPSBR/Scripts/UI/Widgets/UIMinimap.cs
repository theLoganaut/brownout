namespace TPSBR.UI
{
	using UnityEngine;
	using UnityEngine.UI;

	public class UIMinimap : UIWidget
	{
		// CONSTANTS

		private static readonly int     ID_CIRCLE_RADIUS = Shader.PropertyToID("_CircleRadius");
		private static readonly int     ID_CIRCLE_CENTER = Shader.PropertyToID("_CircleCenter");

		private static readonly int     ID_CUTOUT_RADIUS = Shader.PropertyToID("_CutoutRadius");
		private static readonly int     ID_CUTOUT_CENTER = Shader.PropertyToID("_CutoutCenter");

		private static readonly Vector3 MAP_CENTER_SHIFT = new Vector3(0.5f, 0f, 0.5f);

		// PRIVATE MEMBERS

		[SerializeField]
		private RawImage      _mapImage;
		[SerializeField]
		private Image         _currentShrinkArea;
		[SerializeField]
		private Image         _nextShrinkArea;

		[SerializeField]
		private RectTransform _localPlayer;
		[SerializeField]
		private RectTransform _airplane;

		private Material      _currentAreaMaterial;
		private Material      _nextAreaMaterial;

		private readonly Image[] _sixthSenseDots = new Image[AgentGear.MaxBlinkSpots];

		// UIWidget INTERFACE

		protected override void OnInitialize()
		{
			base.OnInitialize();

			_currentAreaMaterial = Instantiate(_currentShrinkArea.material);
			_currentShrinkArea.material = _currentAreaMaterial;

			_nextAreaMaterial = Instantiate(_nextShrinkArea.material);
			_nextShrinkArea.material = _nextAreaMaterial;
		}

		protected override void OnDeinitialize()
		{
			if (_currentAreaMaterial != null)
			{
				Destroy(_currentAreaMaterial);
			}

			if (_nextAreaMaterial != null)
			{
				Destroy(_nextAreaMaterial);
			}
		}

		protected override void OnTick()
		{
			if (Context.Runner.Exists(Context.GameplayMode.Object) == false)
				return;

			var map        = Context.Map;
			var shrinkArea = Context.GameplayMode.ShrinkingArea;

			if (map.MapTexture == null)
				return;

			_mapImage.texture = map.MapTexture;
			var mapSize       = Mathf.Max(map.WorldDimensions.x, map.WorldDimensions.y);
			var mapCenter     = map.transform.position;

			if (shrinkArea != null)
			{
				_currentShrinkArea.SetActive(true);

				var currentCenter = (shrinkArea.Center - mapCenter) / mapSize + MAP_CENTER_SHIFT;

				if (shrinkArea.IsAnnounced == true)
				{
					_nextShrinkArea.SetActive(true);

					var nextCenter = (shrinkArea.ShrinkCenter - mapCenter) / mapSize + MAP_CENTER_SHIFT;

					_nextAreaMaterial.SetFloat(ID_CIRCLE_RADIUS, shrinkArea.ShrinkRadius / mapSize);
					_nextAreaMaterial.SetVector(ID_CIRCLE_CENTER, new Vector4(nextCenter.x, nextCenter.z, 0f, 0f));

					_nextAreaMaterial.SetFloat(ID_CUTOUT_RADIUS, shrinkArea.Radius / mapSize);
					_nextAreaMaterial.SetVector(ID_CUTOUT_CENTER, new Vector4(currentCenter.x, currentCenter.z, 0f, 0f));

				}
				else
				{
					_nextShrinkArea.SetActive(false);
				}

				_currentAreaMaterial.SetFloat(ID_CIRCLE_RADIUS, shrinkArea.Radius / mapSize);
				_currentAreaMaterial.SetVector(ID_CIRCLE_CENTER, new Vector4(currentCenter.x, currentCenter.z, 0f, 0f));
			}
			else
			{
				_currentShrinkArea.SetActive(false);
				_nextShrinkArea.SetActive(false);
			}

			var playerTransform = Context.ObservedAgent != null ? Context.ObservedAgent.transform : Context.WaitingAgentTransform;
			if (playerTransform != null)
			{
				_localPlayer.SetActive(true);
				UpdateMinimapObject(_localPlayer, playerTransform);
			}
			else
			{
				_localPlayer.SetActive(false);
			}

			UpdateSixthSense();

			if (Context.GameplayMode is BattleRoyaleGameplayMode battleRoyale && battleRoyale.Airplane != null)
			{
				_airplane.SetActive(true);
				UpdateMinimapObject(_airplane, battleRoyale.Airplane.transform);
			}
			else
			{
				_airplane.SetActive(false);
			}
		}

		// PRIVATE METHODS

		/// <summary>
		/// Sixth Sense (phase 5b): the spots the server sent the observed player blink as red dots, each fading over SixthSenseFade.
		/// The dots are made at runtime; phase 6 replaces the minimap.
		/// </summary>
		private void UpdateSixthSense()
		{
			Agent agent = Context.ObservedAgent;
			float age   = agent != null ? Time.time - agent.SixthSenseBlinkTime : float.MaxValue;
			float fade  = Mathf.Max(0.01f, EquipmentSettings.Current.SixthSenseFade);
			float alpha = age < fade ? 1.0f - age / fade : 0.0f;
			int   count = alpha > 0.0f ? agent.SixthSenseSpotCount : 0;

			for (int i = 0; i < AgentGear.MaxBlinkSpots; ++i)
			{
				if (i >= count)
				{
					if (_sixthSenseDots[i] != null)
					{
						_sixthSenseDots[i].gameObject.SetActive(false);
					}

					continue;
				}

				if (_sixthSenseDots[i] == null)
				{
					var dot = new GameObject($"SixthSense{i}", typeof(RectTransform), typeof(Image));
					dot.transform.SetParent(_localPlayer.parent, false);
					_sixthSenseDots[i] = dot.GetComponent<Image>();
					_sixthSenseDots[i].raycastTarget = false;
					_sixthSenseDots[i].rectTransform.sizeDelta = new Vector2(14.0f, 14.0f);
				}

				Image image = _sixthSenseDots[i];
				image.gameObject.SetActive(true);
				image.color = new Color(1.0f, 0.15f, 0.1f, alpha);

				var map     = Context.Map;
				int mapSize = Mathf.Max(map.WorldDimensions.x, map.WorldDimensions.y);
				var spot    = (agent.SixthSenseSpots[i] - map.transform.position) / mapSize;
				image.rectTransform.localPosition = new Vector2(spot.x * RectTransform.sizeDelta.x, spot.z * RectTransform.sizeDelta.y);
			}
		}

		private void UpdateMinimapObject(RectTransform minimapObject, Transform objectTransform)
		{
			var map = Context.Map;
			int mapSize = Mathf.Max(map.WorldDimensions.x, map.WorldDimensions.y);

			var objectPosition = (objectTransform.position - map.transform.position) / mapSize;

			minimapObject.localPosition = new Vector2(objectPosition.x * RectTransform.sizeDelta.x, objectPosition.z * RectTransform.sizeDelta.y);
			minimapObject.rotation  = Quaternion.Euler(0f, 0f, -objectTransform.rotation.eulerAngles.y);
		}
	}
}
