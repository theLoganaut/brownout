using Fusion;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Phase 10 step 6: Logan's Osprey (ArtSettings.HelicopterModel) in place of the sample's AC-130, on every peer. The sample's plane
	/// renderers are switched off by GameObject (Multipeer), the Osprey goes where the plane was with its nose along the flight path, and
	/// the rotors (ArtSettings.RotorNames) spin. Added by Airplane when it spawns, so the airplane prefabs stay untouched.
	/// </summary>
	public sealed class HelicopterVisuals : MonoBehaviour
	{
		// PRIVATE MEMBERS

		private GameObject  _model;
		private Transform[] _rotors;
		private float       _turnsPerSecond;

		// PUBLIC METHODS

		public void Build(NetworkRunner runner)
		{
			ArtSettings art = ArtSettings.Current;
			if (_model != null || art == null || art.HelicopterModel == null)
				return;

			// The sample's plane: every renderer under the airplane's Root (lights and the drop markers too)
			Transform root = transform.Find("Root");
			Transform parent = root != null ? root : transform;

			foreach (MeshRenderer renderer in parent.GetComponentsInChildren<MeshRenderer>(true))
			{
				renderer.gameObject.SetActive(false);
			}

			_model = Instantiate(art.HelicopterModel, parent, false);
			_model.name = art.HelicopterModel.name;
			_model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

			foreach (Collider collider in _model.GetComponentsInChildren<Collider>(true))
			{
				Destroy(collider);
			}

			_rotors = new Transform[art.RotorNames != null ? art.RotorNames.Length : 0];
			for (int i = 0; i < _rotors.Length; ++i)
			{
				_rotors[i] = FindDeep(_model.transform, art.RotorNames[i]);
			}

			_turnsPerSecond = art.RotorTurnsPerSecond;

			if (runner != null && runner.Config.PeerMode != NetworkProjectConfig.PeerModes.Single)
			{
				runner.AddVisibilityNodes(_model);
			}
		}

		// MonoBehaviour INTERFACE

		private void LateUpdate()
		{
			if (_rotors == null)
				return;

			float degrees = _turnsPerSecond * 360.0f * Time.deltaTime;
			for (int i = 0; i < _rotors.Length; ++i)
			{
				if (_rotors[i] != null)
				{
					// The two rotors turn opposite ways, as on a tiltrotor
					_rotors[i].Rotate(Vector3.up, i % 2 == 0 ? degrees : -degrees, Space.Self);
				}
			}
		}

		// PRIVATE METHODS

		private static Transform FindDeep(Transform parent, string childName)
		{
			if (parent.name == childName)
				return parent;

			foreach (Transform child in parent)
			{
				Transform found = FindDeep(child, childName);
				if (found != null)
					return found;
			}

			return null;
		}
	}
}
