namespace TPSBR
{
	using Fusion;
	using UnityEngine;

	/// <summary>
	/// The grapple's rope (phase 5b), drawn on every peer from the agent's networked grapple state: it shoots out from the hand to the
	/// hook spot over the hook's flight and stays until the pull ends. Starts at the left side of the chest (in first person, just below
	/// and left of the eye). Added to every agent at runtime; the line is registered for Multipeer visibility.
	/// </summary>
	public sealed class GrappleRope : MonoBehaviour
	{
		// PRIVATE MEMBERS

		private Agent        _agent;
		private LineRenderer _line;
		private bool         _registered;

		private static Material _material;

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_agent = GetComponent<Agent>();
		}

		private void OnDisable()
		{
			if (_line != null)
			{
				_line.gameObject.SetActive(false);
			}
		}

		private void LateUpdate()
		{
			bool show = TryGetRope(out Vector3 start, out Vector3 end);

			if (show == false)
			{
				if (_line != null && _line.gameObject.activeSelf == true)
				{
					_line.gameObject.SetActive(false);
				}

				return;
			}

			if (_line == null)
			{
				Build();
			}

			if (_registered == false && _agent.Runner != null)
			{
				if (_agent.Runner.Config.PeerMode != NetworkProjectConfig.PeerModes.Single)
				{
					_agent.Runner.AddVisibilityNodes(_line.gameObject);
				}

				_registered = true;
			}

			_line.gameObject.SetActive(true);
			_line.SetPosition(0, start);
			_line.SetPosition(1, end);
		}

		// PRIVATE METHODS

		private bool TryGetRope(out Vector3 start, out Vector3 end)
		{
			start = default;
			end   = default;

			if (_agent == null || _agent.Object == null || _agent.Object.IsValid == false || _agent.EquipmentActions == null)
				return false;

			AgentEquipment equipment = _agent.EquipmentActions;
			if (equipment.IsHookOut == false || _agent.EquipReadyTick > 0)
				return false;

			var   runner = _agent.Runner;
			float now    = (_agent.Object.IsProxy == true ? runner.RemoteRenderTime : runner.LocalRenderTime) / runner.DeltaTime;

			Character character = _agent.Character;
			bool      isLocal   = _agent.IsObserved == true && _agent.Health.IsAlive == true && _agent.Context != null && _agent.Context.Camera != null;

			if (isLocal == true)
			{
				Transform view = _agent.Context.Camera.Camera.transform;
				start = view.position + view.forward * 0.45f - view.right * 0.20f - view.up * 0.18f;
			}
			else
			{
				Quaternion yaw = Quaternion.Euler(0.0f, character.CharacterController.RenderData.LookYaw, 0.0f);
				start = _agent.transform.position + Vector3.up * 1.35f + yaw * new Vector3(-0.25f, 0.0f, 0.3f);
			}

			float release = _agent.EquipReleaseTick;
			float hook    = _agent.GrappleHookTick;
			float flight  = hook > release ? Mathf.Clamp01((now - release) / (hook - release)) : 1.0f;

			end = Vector3.Lerp(start, _agent.GrapplePoint, flight);
			return now >= release;
		}

		private void Build()
		{
			if (_material == null)
			{
				_material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
				_material.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.12f));
			}

			var lineObject = new GameObject("GrappleRope");
			lineObject.transform.SetParent(transform, false);

			_line = lineObject.AddComponent<LineRenderer>();
			_line.useWorldSpace     = true;
			_line.positionCount     = 2;
			_line.startWidth        = 0.025f;
			_line.endWidth          = 0.02f;
			_line.sharedMaterial    = _material;
			_line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			_line.receiveShadows    = false;
		}
	}
}
