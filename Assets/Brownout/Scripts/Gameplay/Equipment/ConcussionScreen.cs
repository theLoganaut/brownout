namespace TPSBR
{
	using System.Collections;
	using UnityEngine;

	/// <summary>
	/// The concussed screen (phase 5b), a placeholder until phase 10: double vision as a ghost of the last frame drawn over this one,
	/// drifting off-center and fading with the concussion. Because each ghost includes the one before it, moving or turning leaves streaks.
	/// Only the local player's screen (the observed agent of the peer with input). Added to every agent at runtime.
	/// </summary>
	public sealed class ConcussionScreen : MonoBehaviour
	{
		// CONSTANTS

		private const float GHOST_ALPHA  = 0.5f;
		private const float GHOST_OFFSET = 40.0f; // Pixels at 1080p, at full strength

		// PRIVATE MEMBERS

		private Agent         _agent;
		private RenderTexture _frame;
		private bool          _hasFrame;
		private bool          _capturing;
		private float         _value;

		private static readonly WaitForEndOfFrame _endOfFrame = new WaitForEndOfFrame();

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_agent = GetComponent<Agent>();
		}

		private void OnDisable()
		{
			_capturing = false;
			_hasFrame  = false;
			_value     = 0.0f;
		}

		private void OnDestroy()
		{
			if (_frame != null)
			{
				_frame.Release();
				Destroy(_frame);
			}
		}

		private void Update()
		{
			bool isLocal = _agent != null && _agent.Object != null && _agent.Object.IsValid == true && _agent.IsObserved == true && _agent.Context != null && _agent.Context.HasInput == true;

			_value = isLocal == true && _agent.Health.IsAlive == true ? _agent.Senses.GetConcussionValue(true) : 0.0f;

			if (_value <= 0.001f)
			{
				_hasFrame = false;
				return;
			}

			if (_frame == null || _frame.width != Screen.width || _frame.height != Screen.height)
			{
				if (_frame != null)
				{
					_frame.Release();
					Destroy(_frame);
				}

				_frame    = new RenderTexture(Screen.width, Screen.height, 0) { name = "ConcussionFrame" };
				_hasFrame = false;
			}

			if (_capturing == false)
			{
				_capturing = true;
				StartCoroutine(CaptureLoop());
			}
		}

		private void OnGUI()
		{
			if (Event.current.type != EventType.Repaint || _value <= 0.001f || _hasFrame == false)
				return;

			float scale = Screen.height / 1080.0f;
			float time  = Time.time;
			var   drift = new Vector2(Mathf.Sin(time * 2.1f), Mathf.Cos(time * 1.7f) * 0.5f) * GHOST_OFFSET * scale * _value;

			Color previous = GUI.color;
			GUI.color = new Color(1.0f, 0.95f, 0.9f, GHOST_ALPHA * _value);

			Rect rect = new Rect(drift.x, drift.y, Screen.width, Screen.height);

			// The captured frame is upside down where UVs start at the top (D3D, Metal, Vulkan)
			Rect uv = SystemInfo.graphicsUVStartsAtTop == true ? new Rect(0.0f, 1.0f, 1.0f, -1.0f) : new Rect(0.0f, 0.0f, 1.0f, 1.0f);
			GUI.DrawTextureWithTexCoords(rect, _frame, uv);

			GUI.color = previous;
		}

		// PRIVATE METHODS

		private IEnumerator CaptureLoop()
		{
			while (_value > 0.001f && _frame != null)
			{
				yield return _endOfFrame;

				if (_frame == null)
					break;

				ScreenCapture.CaptureScreenshotIntoRenderTexture(_frame);
				_hasFrame = true;
			}

			_capturing = false;
		}
	}
}
