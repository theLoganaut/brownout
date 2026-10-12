namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.Rendering;

	/// <summary>
	/// Full body awareness for the agent the local camera looks through.
	/// The real body (hitboxes, third-person gun) switches to shadows only, so you keep your full shadow.
	/// A render-only copy of the body follows the real skeleton every frame with the head and the third-person arms
	/// folded away, so looking down shows your torso, legs and feet under the first-person arms and gun.
	/// Nobody else sees the copy, and it has no colliders or hitboxes.
	/// </summary>
	[DefaultExecutionOrder(10001)]
	public sealed class FirstPersonBody : MonoBehaviour
	{
		// PRIVATE MEMBERS

		[SerializeField, Tooltip("Bones folded to zero size on the copy (children fold with them). The upper chest folds because the third-person aim pose bends it forward into the camera when looking down; that also removes the neck, head, shoulders and third-person arms.")]
		private string[] _foldedBones = new string[] { "spine_03" };
		[SerializeField, Tooltip("The same on a Humanoid body (Logan's soldier, phase 10).")]
		private HumanBodyBones[] _foldedHumanBones = new HumanBodyBones[] { HumanBodyBones.UpperChest };
		[SerializeField, Tooltip("How far the copy slides back when looking straight down, so the camera never ends up inside the chest.")]
		private float    _lookDownBackOffset = 0.18f;
		[SerializeField, Tooltip("Extra slide back when fully crouched or sliding (eye lowered), so the bent knees stay out of the camera.")]
		private float    _lowStanceBackOffset = 0.35f;

		private Agent        _agent;
		private bool         _isFirstPerson;

		private readonly Dictionary<Renderer, ShadowCastingMode> _hiddenRenderers = new Dictionary<Renderer, ShadowCastingMode>(32);
		private readonly List<Renderer>                          _rendererBuffer  = new List<Renderer>(64);
		private Weapon                                           _lastWeapon;
		private int                                              _lastChildCount;

		private Transform    _sourceModel;
		private Transform    _cloneModel;
		private Transform[]  _sourceBones;
		private Transform[]  _cloneBones;
		private bool[]       _foldedMask;
		private GameObject[] _sourceRendererObjects;
		private GameObject[] _cloneRendererObjects;
		private bool         _cloneFailed;

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_agent = GetComponent<Agent>();
		}

		private void OnDisable()
		{
			SetFirstPerson(false);
		}

		private void OnDestroy()
		{
			if (_cloneModel != null)
			{
				Destroy(_cloneModel.gameObject);
			}
		}

		private void LateUpdate()
		{
			bool isFirstPerson = _agent != null && _agent.Object != null && _agent.Object.IsValid == true && _agent.IsObserved == true && _agent.Health.IsAlive == true && _agent.Character.IsThirdPersonView == false;

			SetFirstPerson(isFirstPerson);

			if (isFirstPerson == false)
				return;

			RefreshHiddenRenderers(false);
			UpdateClone();
		}

		// PRIVATE METHODS

		private void SetFirstPerson(bool isFirstPerson)
		{
			if (_isFirstPerson == isFirstPerson)
				return;

			_isFirstPerson = isFirstPerson;

			if (isFirstPerson == true)
			{
				RefreshHiddenRenderers(true);

				if (_cloneModel == null && _cloneFailed == false)
				{
					BuildClone();
				}
			}
			else
			{
				RestoreHiddenRenderers();
			}

			if (_cloneModel != null)
			{
				_cloneModel.gameObject.SetActive(isFirstPerson);
			}
		}

		private void RefreshHiddenRenderers(bool force)
		{
			// Weapons get parented under the agent when picked up or switched, so recheck when that changes
			Weapon currentWeapon = _agent.Weapons.CurrentWeapon;
			int    childCount    = CountChildren(transform);

			if (force == false && currentWeapon == _lastWeapon && childCount == _lastChildCount)
				return;

			_lastWeapon     = currentWeapon;
			_lastChildCount = childCount;

			// A dropped gun leaves the agent: it shows normally again (it stayed shadows-only before 3c)
			_rendererBuffer.Clear();
			foreach (var pair in _hiddenRenderers)
			{
				if (pair.Key == null || pair.Key.transform.IsChildOf(transform) == false)
				{
					_rendererBuffer.Add(pair.Key);
				}
			}

			for (int i = 0; i < _rendererBuffer.Count; ++i)
			{
				Renderer renderer = _rendererBuffer[i];
				if (renderer != null)
				{
					renderer.shadowCastingMode = _hiddenRenderers[renderer];
				}

				_hiddenRenderers.Remove(renderer);
			}

			_rendererBuffer.Clear();
			GetComponentsInChildren(true, _rendererBuffer);

			for (int i = 0; i < _rendererBuffer.Count; ++i)
			{
				Renderer renderer = _rendererBuffer[i];

				if (renderer is MeshRenderer == false && renderer is SkinnedMeshRenderer == false)
					continue;
				if (_cloneModel != null && renderer.transform.IsChildOf(_cloneModel) == true)
					continue;
				if (_hiddenRenderers.ContainsKey(renderer) == true)
					continue;

				_hiddenRenderers.Add(renderer, renderer.shadowCastingMode);
				renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
			}

			_rendererBuffer.Clear();
		}

		private void RestoreHiddenRenderers()
		{
			foreach (var pair in _hiddenRenderers)
			{
				if (pair.Key != null)
				{
					pair.Key.shadowCastingMode = pair.Value;
				}
			}

			_hiddenRenderers.Clear();
			_lastWeapon     = null;
			_lastChildCount = -1;
		}

		private void BuildClone()
		{
			Transform rootBone = _agent.Character.ThirdPersonView.RootBone;
			if (rootBone == null || rootBone.parent == null)
			{
				_cloneFailed = true;
				return;
			}

			Transform headBone = _agent.Character.ThirdPersonView.HeadTransform;

			_sourceModel = rootBone.parent;

			// Body meshes only: skip small meshes driven by the head (head, hat, face details)
			var sourceRenderers = new List<SkinnedMeshRenderer>(8);
			foreach (var skinnedRenderer in _sourceModel.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				bool isHeadMesh = headBone != null && skinnedRenderer.bones.Length < 10 && System.Array.IndexOf(skinnedRenderer.bones, headBone) >= 0;
				if (isHeadMesh == false)
				{
					sourceRenderers.Add(skinnedRenderer);
				}
			}

			if (sourceRenderers.Count == 0)
			{
				_cloneFailed = true;
				return;
			}

			// Mirror every bone the body meshes use, plus their parents up to the model root
			var boneSet = new HashSet<Transform>();
			foreach (var skinnedRenderer in sourceRenderers)
			{
				foreach (var bone in skinnedRenderer.bones)
				{
					for (Transform t = bone; t != null && t != _sourceModel; t = t.parent)
					{
						boneSet.Add(t);
					}
				}
			}

			_cloneModel = new GameObject($"{_sourceModel.name}_FirstPersonBody").transform;
			_cloneModel.SetParent(_sourceModel.parent, false);

			var sourceToClone = new Dictionary<Transform, Transform>(boneSet.Count + 1);
			sourceToClone.Add(_sourceModel, _cloneModel);

			var sourceBones = new List<Transform>(boneSet.Count);
			var cloneBones  = new List<Transform>(boneSet.Count);

			CloneHierarchy(_sourceModel, _cloneModel, boneSet, sourceToClone, sourceBones, cloneBones);

			_sourceBones = sourceBones.ToArray();
			_cloneBones  = cloneBones.ToArray();
			_foldedMask  = new bool[_cloneBones.Length];

			var foldedHumanBones = new HashSet<Transform>();
			var animator         = _sourceModel.GetComponent<Animator>();
			if (animator != null && animator.avatar != null && animator.isHuman == true)
			{
				foreach (HumanBodyBones humanBone in _foldedHumanBones)
				{
					Transform bone = animator.GetBoneTransform(humanBone);
					if (bone != null)
					{
						foldedHumanBones.Add(bone);
					}
				}
			}

			for (int i = 0; i < _cloneBones.Length; ++i)
			{
				_foldedMask[i] = System.Array.IndexOf(_foldedBones, _cloneBones[i].name) >= 0 || foldedHumanBones.Contains(_sourceBones[i]) == true;
			}

			_sourceRendererObjects = new GameObject[sourceRenderers.Count];
			_cloneRendererObjects  = new GameObject[sourceRenderers.Count];

			for (int rendererIndex = 0; rendererIndex < sourceRenderers.Count; ++rendererIndex)
			{
				var sourceRenderer = sourceRenderers[rendererIndex];
				var cloneObject = new GameObject(sourceRenderer.name);

				// The soldier's armor, backpack and drop suit are switched on and off (BodyGear); the copy follows
				_sourceRendererObjects[rendererIndex] = sourceRenderer.gameObject;
				_cloneRendererObjects[rendererIndex]  = cloneObject;
				cloneObject.layer = sourceRenderer.gameObject.layer;
				cloneObject.transform.SetParent(_cloneModel, false);

				var bones = sourceRenderer.bones;
				var mappedBones = new Transform[bones.Length];
				for (int i = 0; i < bones.Length; ++i)
				{
					mappedBones[i] = bones[i] != null && sourceToClone.TryGetValue(bones[i], out Transform cloneBone) == true ? cloneBone : null;
				}

				var cloneRenderer = cloneObject.AddComponent<SkinnedMeshRenderer>();
				cloneRenderer.sharedMesh          = sourceRenderer.sharedMesh;
				cloneRenderer.sharedMaterials     = sourceRenderer.sharedMaterials;
				cloneRenderer.bones               = mappedBones;
				cloneRenderer.rootBone            = sourceRenderer.rootBone != null && sourceToClone.TryGetValue(sourceRenderer.rootBone, out Transform cloneRoot) == true ? cloneRoot : null;
				cloneRenderer.localBounds         = sourceRenderer.localBounds;
				cloneRenderer.updateWhenOffscreen = true;
				cloneRenderer.shadowCastingMode   = ShadowCastingMode.Off; // The real body casts the shadow
				cloneRenderer.receiveShadows      = true;
			}

			_cloneModel.gameObject.SetActive(_isFirstPerson);
		}

		private static void CloneHierarchy(Transform source, Transform clone, HashSet<Transform> boneSet, Dictionary<Transform, Transform> sourceToClone, List<Transform> sourceBones, List<Transform> cloneBones)
		{
			for (int i = 0, count = source.childCount; i < count; ++i)
			{
				Transform sourceChild = source.GetChild(i);
				if (boneSet.Contains(sourceChild) == false)
					continue;

				var cloneChild = new GameObject(sourceChild.name).transform;
				cloneChild.SetParent(clone, false);
				cloneChild.SetLocalPositionAndRotation(sourceChild.localPosition, sourceChild.localRotation);
				cloneChild.localScale = sourceChild.localScale;

				sourceToClone.Add(sourceChild, cloneChild);
				sourceBones.Add(sourceChild);
				cloneBones.Add(cloneChild);

				CloneHierarchy(sourceChild, cloneChild, boneSet, sourceToClone, sourceBones, cloneBones);
			}
		}

		private void UpdateClone()
		{
			if (_cloneModel == null)
				return;

			// Slide the copy back as the view pitches down (positive pitch = looking down), and further as the eye lowers (crouch, slide)
			var     kcc        = _agent.Character.CharacterController;
			var     movement   = MovementSettings.Current;
			float   lookDown   = Mathf.Clamp01(kcc.Data.LookPitch / 90.0f);
			float   lowStance  = Mathf.Clamp01((movement.StandEyeHeight - AgentMovement.GetEyeHeight(kcc, kcc.Data)) / Mathf.Max(0.01f, movement.StandEyeHeight - movement.SlideEyeHeight));
			Vector3 backOffset = _sourceModel.parent.InverseTransformDirection(-transform.forward) * (_lookDownBackOffset * lookDown + _lowStanceBackOffset * lowStance);

			_cloneModel.SetLocalPositionAndRotation(_sourceModel.localPosition + backOffset, _sourceModel.localRotation);
			_cloneModel.localScale = _sourceModel.localScale;

			for (int i = 0; i < _sourceBones.Length; ++i)
			{
				Transform sourceBone = _sourceBones[i];
				Transform cloneBone  = _cloneBones[i];

				sourceBone.GetLocalPositionAndRotation(out Vector3 localPosition, out Quaternion localRotation);
				cloneBone.SetLocalPositionAndRotation(localPosition, localRotation);
				cloneBone.localScale = _foldedMask[i] == true ? Vector3.one * 0.001f : sourceBone.localScale;
			}

			for (int i = 0; i < _sourceRendererObjects.Length; ++i)
			{
				bool isActive = _sourceRendererObjects[i] != null && _sourceRendererObjects[i].activeInHierarchy == true;
				if (_cloneRendererObjects[i].activeSelf != isActive)
				{
					_cloneRendererObjects[i].SetActive(isActive);
				}
			}
		}

		private static int CountChildren(Transform root)
		{
			int count = root.childCount;
			for (int i = 0, childCount = root.childCount; i < childCount; ++i)
			{
				count += CountChildren(root.GetChild(i));
			}

			return count;
		}
	}
}
