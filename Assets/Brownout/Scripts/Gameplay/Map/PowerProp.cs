using UnityEngine;

namespace TPSBR
{
	public enum EPowerProp
	{
		Light  = 0,
		Camera = 1,
		Turret = 2,
		Hum    = 3,
	}

	/// <summary>
	/// A placeholder that phase 11's power switches (Brownout Mechanic): a light fixture, a Radio Array camera spot, a Factory turret
	/// spot or the Data Center's hum spot, tagged with its POI. Phase 8b only places them; every POI starts unpowered. Power doors are
	/// Doors with IsPower. MapState switches what's shown here when a POI's power changes (lights only for now; turrets and cameras
	/// stay inert blocks).
	/// </summary>
	public sealed class PowerProp : MonoBehaviour
	{
		// PUBLIC MEMBERS

		public EPowerProp Kind => _kind;
		public int        Area => _area;

		// PRIVATE MEMBERS

		[SerializeField]
		private EPowerProp   _kind;
		[SerializeField, Tooltip("Index of the POI or cabin area in BrownoutMap.Areas.")]
		private int          _area = -1;
		[SerializeField, Tooltip("Shown only with power (the lit lens and the light).")]
		private GameObject[] _onObjects = new GameObject[0];
		[SerializeField, Tooltip("Shown only without power (the dark lens).")]
		private GameObject[] _offObjects = new GameObject[0];

		// PUBLIC METHODS

		public void SetPowered(bool powered)
		{
			for (int i = 0; i < _onObjects.Length; ++i)
			{
				if (_onObjects[i] != null && _onObjects[i].activeSelf != powered)
				{
					_onObjects[i].SetActive(powered);
				}
			}

			for (int i = 0; i < _offObjects.Length; ++i)
			{
				if (_offObjects[i] != null && _offObjects[i].activeSelf == powered)
				{
					_offObjects[i].SetActive(powered == false);
				}
			}
		}

#if UNITY_EDITOR
		/// <summary>The map builder sets the prop up.</summary>
		public void EditorSet(EPowerProp kind, int area, GameObject[] onObjects, GameObject[] offObjects)
		{
			_kind       = kind;
			_area       = area;
			_onObjects  = onObjects  ?? new GameObject[0];
			_offObjects = offObjects ?? new GameObject[0];
		}
#endif
	}
}
