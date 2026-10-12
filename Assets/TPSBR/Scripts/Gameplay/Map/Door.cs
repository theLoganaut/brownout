using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// A door on the Brownout map (phase 8b, Map & POIs: 0.75 × 2.29 m). This object is the hinge: its child leaf is solid for players,
	/// bullets and grenades. The state lives in MapState (one networked array for every door): walking into a closed door makes the server
	/// swing it open away from you, F closes an open one you're looking at. A power door stays closed and locked while its POI has no
	/// power (Brownout Mechanic). The swing animates on every peer from the networked state.
	/// </summary>
	public sealed class Door : MonoBehaviour, IInteraction
	{
		// PUBLIC MEMBERS

		public int  Index   => _index;
		public int  Area    => _area;
		public bool IsPower => _isPower;

		/// <summary>The current hinge angle (degrees, 0 = closed).</summary>
		public float Angle  => _angle;

		/// <summary>The closed leaf's facing (world): which side a player is on decides the swing.</summary>
		public Vector3 Normal => (transform.parent != null ? transform.parent.rotation * _closedRotation : _closedRotation) * Vector3.forward;

		// PRIVATE MEMBERS

		[SerializeField]
		private int       _index;
		[SerializeField, Tooltip("Index of the POI or cabin area in BrownoutMap.Areas (power doors follow its power).")]
		private int       _area = -1;
		[SerializeField]
		private bool      _isPower;
		[SerializeField]
		private float     _width = 0.75f;
		[SerializeField]
		private float     _height = 2.29f;

		private Quaternion _closedRotation;
		private float      _angle;
		private MapState   _state;

		// PUBLIC METHODS

		/// <summary>Every frame from MapState.Render: turns toward the networked angle at the swing speed.</summary>
		public void UpdateSwing(float targetAngle, float speed, float deltaTime)
		{
			if (Mathf.Approximately(_angle, targetAngle) == true)
				return;

			_angle = Mathf.MoveTowards(_angle, targetAngle, speed * deltaTime);
			transform.localRotation = _closedRotation * Quaternion.Euler(0.0f, _angle, 0.0f);
		}

		/// <summary>Which way to swing so the leaf moves away from a player at this position: +1 or -1.</summary>
		public int GetSwingAwayFrom(Vector3 position)
		{
			Vector3 toPlayer = position - transform.position;
			return Vector3.Dot(toPlayer, Normal) >= 0.0f ? 1 : -1;
		}

		public void SetState(MapState state)
		{
			_state = state;
		}

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_closedRotation = transform.localRotation;
		}

#if UNITY_EDITOR
		/// <summary>The map builder sets the door up.</summary>
		public void EditorSet(int index, int area, bool isPower, float width, float height)
		{
			_index   = index;
			_area    = area;
			_isPower = isPower;
			_width   = width;
			_height  = height;
		}
#endif

		// IInteraction INTERFACE

		string  IInteraction.Name        => "[F] Close door";
		string  IInteraction.Description => string.Empty;
		Vector3 IInteraction.HUDPosition => transform.position + transform.right * (_width * 0.5f) + Vector3.up * (_height * 0.6f);
		bool    IInteraction.IsActive    => _state != null && _state.IsDoorOpen(_index) == true && _state.IsDoorLocked(this) == false;
	}
}
