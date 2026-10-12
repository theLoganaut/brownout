namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.Animations;
	using UnityEngine.Playables;
	using UnityEngine.Rendering;

	/// <summary>
	/// Phase 10: Logan's first-person arms (GunplaySettings.FirstPersonArms, soldier_arms.fbx). The file is the soldier's skeleton
	/// with only the arms skinned and the same takes as the body; Viewmodel picks a take from the player's state and this plays it
	/// (a small playable graph, evaluated by hand every frame). The model sits in camera space so its eye (just in front of the head
	/// bone) stays on the camera whatever the take does. In the takes that hold the gun, the Weapon bone is the grip: Viewmodel puts
	/// the gun there, moves the whole arms for aiming, kick and sway (MoveRoot), and reaches the left hand to each gun's own left-hand
	/// point (two-bone IK on top of the take).
	/// </summary>
	public sealed class ViewmodelArms
	{
		// PUBLIC MEMBERS

		public readonly Transform Root;

		/// <summary>The take playing most (without the "SoldierRig|" prefix).</summary>
		public string CurrentTake => _target >= 0 ? _takes[_target].Name : null;

		/// <summary>Every take's playback speed on top of the speed asked for (Logan, 9 Oct: half speed).</summary>
		public float SpeedScale = 1.0f;

		/// <summary>The left hand's palm center (world), for the grenade.</summary>
		public Vector3 LeftPalm => _left.Palm;

		// PRIVATE MEMBERS

		private static readonly Quaternion Facing = Quaternion.Euler(0.0f, 180.0f, 0.0f); // The model faces -Z, the camera +Z

		private readonly Vector3               _eyeOffset;
		private readonly Transform             _head;
		private readonly Transform             _weapon;
		private readonly Quaternion            _weaponToGun;
		private readonly Arm                   _left;
		private readonly SkinnedMeshRenderer[] _renderers;
		private readonly Color[][]             _baseColors;
		private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
		private float                          _tint = -1.0f;

		private PlayableGraph                  _graph;
		private AnimationMixerPlayable          _mixer;
		private readonly List<Take>            _takes      = new List<Take>(48);
		private readonly Dictionary<string, int> _takeIndex = new Dictionary<string, int>(48);
		private int                            _target = -1;
		private float                          _fadeDuration = 0.15f;

		// CONSTRUCTORS

		/// <param name="eyeOffset">The eye relative to the head bone (right, up, forward as the model faces).</param>
		public ViewmodelArms(GameObject prefab, AnimationClip[] clips, Transform parent, Vector3 eyeOffset)
		{
			GameObject model = Object.Instantiate(prefab, parent, false);
			model.name = "Arms";
			Root = model.transform;
			Root.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
			Root.localScale = Vector3.one;

			_eyeOffset = new Vector3(-eyeOffset.x, eyeOffset.y, -eyeOffset.z);

			// Viewmodel layer, no shadows
			foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
			{
				node.gameObject.layer = ObjectLayer.FPV;
			}

			_renderers  = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			_baseColors = new Color[_renderers.Length][];
			for (int i = 0; i < _renderers.Length; ++i)
			{
				SkinnedMeshRenderer renderer = _renderers[i];
				renderer.shadowCastingMode   = ShadowCastingMode.Off;
				renderer.receiveShadows      = false;
				renderer.updateWhenOffscreen = true;

				Material[] materials = renderer.sharedMaterials;
				_baseColors[i] = new Color[materials.Length];
				for (int m = 0; m < materials.Length; ++m)
				{
					_baseColors[i][m] = materials[m] != null && materials[m].HasProperty("_BaseColor") == true ? materials[m].GetColor("_BaseColor") : Color.white;
				}
			}

			_head   = FindDeep(Root, "Head");
			_weapon = FindDeep(Root, "Weapon");

			// The rest pose holds the gun with the barrel along the model's -Z: the gun's frame relative to the Weapon bone
			_weaponToGun = _weapon != null ? Quaternion.Inverse(Quaternion.Inverse(Root.rotation) * _weapon.rotation) * Facing : Quaternion.identity;

			_left = new Arm(Root, "L");

			// Takes
			var animator = model.GetComponent<Animator>();
			if (animator == null)
			{
				animator = model.AddComponent<Animator>();
			}
			animator.applyRootMotion = false;
			animator.cullingMode     = AnimatorCullingMode.AlwaysAnimate;

			_graph = PlayableGraph.Create($"{parent.name}_Arms");
			_graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

			int count = clips != null ? clips.Length : 0;
			_mixer = AnimationMixerPlayable.Create(_graph, count);

			for (int i = 0; i < count; ++i)
			{
				AnimationClip clip = clips[i];
				var playable = AnimationClipPlayable.Create(_graph, clip);

				_graph.Connect(playable, 0, _mixer, i);
				_mixer.SetInputWeight(i, 0.0f);

				string name = clip.name.Substring(clip.name.LastIndexOf('|') + 1);
				_takeIndex[name] = i;
				_takes.Add(new Take { Name = name, Clip = clip, Playable = playable, Looping = clip.isLooping });
			}

			var output = AnimationPlayableOutput.Create(_graph, "Arms", animator);
			output.SetSourcePlayable(_mixer);
			_graph.Play();
		}

		// PUBLIC METHODS

		public bool HasTake(string take) => _takeIndex.ContainsKey(take);

		/// <summary>Seconds the take lasts at SpeedScale.</summary>
		public float GetLength(string take) => _takeIndex.TryGetValue(take, out int index) == true ? _takes[index].Clip.length / Mathf.Max(0.01f, SpeedScale) : 0.0f;

		/// <summary>Fades to a take that plays on its own (loops loop, others play once from the start).</summary>
		public void Play(string take, float speed = 1.0f, float fade = 0.15f)
		{
			if (SetTarget(take, fade, out Take target) == false)
				return;

			target.Synced = false;
			target.Playable.SetSpeed(speed * SpeedScale);
		}

		/// <summary>Fades to a take held at this point (0–1), following a game timer.</summary>
		public void PlaySynced(string take, float normalizedTime, float fade = 0.1f)
		{
			if (SetTarget(take, fade, out Take target) == false)
				return;

			target.Synced = true;
			target.Playable.SetSpeed(0.0);
			target.Playable.SetTime(Mathf.Clamp01(normalizedTime) * target.Clip.length);
		}

		/// <summary>Plays the takes forward and puts the eye back on the camera.</summary>
		public void Evaluate(float deltaTime)
		{
			// Fade toward the target take; the weights are kept summing to 1 so a fade never blends toward the rest pose
			float sum = 0.0f;
			for (int i = 0; i < _takes.Count; ++i)
			{
				Take take = _takes[i];
				take.Weight = Mathf.MoveTowards(take.Weight, i == _target ? 1.0f : 0.0f, deltaTime / Mathf.Max(0.01f, _fadeDuration));
				sum += take.Weight;
			}

			for (int i = 0; i < _takes.Count; ++i)
			{
				_mixer.SetInputWeight(i, sum > 0.0f ? _takes[i].Weight / sum : 0.0f);
			}

			_graph.Evaluate(deltaTime);

			Root.localRotation = Facing;
			Root.localPosition = Vector3.zero;

			if (_head != null)
			{
				Vector3 eye = Root.InverseTransformPoint(_head.position) + _eyeOffset;
				Root.localPosition = -(Facing * eye);
			}
		}

		/// <summary>Where the take holds the gun, in the parent's space (grip, +Z down the barrel).</summary>
		public void GetGunPose(out Vector3 position, out Quaternion rotation)
		{
			Transform parent = Root.parent;
			position = parent.InverseTransformPoint(_weapon != null ? _weapon.position : Root.position);
			rotation = Quaternion.Inverse(parent.rotation) * (_weapon != null ? _weapon.rotation * _weaponToGun : Root.rotation);
		}

		/// <summary>Moves the whole arms so the gun goes from the take's pose to the given one (parent space): aiming, kick, sway.</summary>
		public void MoveRoot(Vector3 fromPosition, Quaternion fromRotation, Vector3 toPosition, Quaternion toRotation)
		{
			Quaternion delta = toRotation * Quaternion.Inverse(fromRotation);

			Root.localPosition = toPosition + delta * (Root.localPosition - fromPosition);
			Root.localRotation = delta * Root.localRotation;
		}

		/// <summary>The left hand's palm on a point of the gun, keeping the take's hand turn; weight 0 leaves the take alone.</summary>
		public void ReachLeft(Vector3 palmWorld, float weight)
		{
			if (weight <= 0.0f)
				return;

			_left.Reach(Vector3.Lerp(_left.Palm, palmWorld, weight));
		}

		/// <summary>Camouflage: tints the arms toward see-through blue, 0..1.</summary>
		public void SetTint(float amount, Color tintColor)
		{
			if (Mathf.Approximately(amount, _tint) == true)
				return;

			_tint = amount;

			for (int i = 0; i < _renderers.Length; ++i)
			{
				for (int m = 0; m < _baseColors[i].Length; ++m)
				{
					_renderers[i].GetPropertyBlock(_block, m);
					_block.SetColor("_BaseColor", Color.Lerp(_baseColors[i][m], tintColor, amount));
					_renderers[i].SetPropertyBlock(_block, m);
				}
			}
		}

		public void Dispose()
		{
			if (_graph.IsValid() == true)
			{
				_graph.Destroy();
			}
		}

		// PRIVATE METHODS

		private bool SetTarget(string take, float fade, out Take target)
		{
			target = null;

			if (_takeIndex.TryGetValue(take, out int index) == false)
				return false;

			target        = _takes[index];
			_fadeDuration = fade;

			if (_target != index)
			{
				// One-shot takes start over when they come back; loops keep their phase
				if (target.Looping == false || target.Weight <= 0.0f)
				{
					target.Playable.SetTime(0.0);
				}

				_target = index;
			}

			return true;
		}

		private static Transform FindDeep(Transform root, string name)
		{
			if (root.name == name)
				return root;

			for (int i = 0; i < root.childCount; ++i)
			{
				Transform found = FindDeep(root.GetChild(i), name);
				if (found != null)
					return found;
			}

			return null;
		}

		// HELPERS

		private sealed class Take
		{
			public string                Name;
			public AnimationClip         Clip;
			public AnimationClipPlayable Playable;
			public bool                  Looping;
			public bool                  Synced;
			public float                 Weight;
		}

		/// <summary>Two-bone IK from the animated pose: the hand moves to the target, the elbow stays on the side the take bends it.</summary>
		private sealed class Arm
		{
			private readonly Transform _upper;
			private readonly Transform _lower;
			private readonly Transform _hand;
			private readonly Transform _middle;
			private readonly float     _upperLength;
			private readonly float     _lowerLength;

			public Vector3 Palm => _hand == null ? Vector3.zero : (_middle != null ? (_hand.position + _middle.position) * 0.5f : _hand.position);

			public Arm(Transform root, string side)
			{
				_upper  = FindDeep(root, $"UpperArm.{side}");
				_lower  = FindDeep(root, $"LowerArm.{side}");
				_hand   = FindDeep(root, $"Hand.{side}");
				_middle = _hand != null ? FindDeep(_hand, $"MiddleProximal.{side}") : null;

				if (_upper == null || _lower == null || _hand == null)
				{
					Debug.LogWarning($"ViewmodelArms: {root.name} is missing UpperArm.{side} / LowerArm.{side} / Hand.{side}.");
					return;
				}

				_upperLength = Vector3.Distance(_upper.position, _lower.position);
				_lowerLength = Vector3.Distance(_lower.position, _hand.position);
			}

			/// <summary>Moves the palm to the target, the hand keeping its turn.</summary>
			public void Reach(Vector3 palmTarget)
			{
				if (_hand == null)
					return;

				Quaternion handRotation = _hand.rotation;
				Vector3    target       = palmTarget - (Palm - _hand.position);

				Vector3 a = _upper.position;
				Vector3 b = _lower.position;
				Vector3 c = _hand.position;

				float reach = Mathf.Clamp(Vector3.Distance(a, target), 0.01f, (_upperLength + _lowerLength) * 0.999f);

				float ac_ab_0 = Angle(c - a, b - a);
				float ba_bc_0 = Angle(a - b, c - b);
				float ac_at_0 = Angle(c - a, target - a);

				float ac_ab_1 = Mathf.Acos(Mathf.Clamp((_lowerLength * _lowerLength - _upperLength * _upperLength - reach * reach) / (-2.0f * _upperLength * reach), -1.0f, 1.0f));
				float ba_bc_1 = Mathf.Acos(Mathf.Clamp((reach * reach - _upperLength * _upperLength - _lowerLength * _lowerLength) / (-2.0f * _upperLength * _lowerLength), -1.0f, 1.0f));

				Vector3 bendAxis  = Vector3.Cross(c - a, b - a);
				Vector3 reachAxis = Vector3.Cross(c - a, target - a);
				if (bendAxis.sqrMagnitude < 1e-8f)
				{
					bendAxis = _upper.right;
				}
				bendAxis.Normalize();

				Quaternion upperRotation = _upper.rotation;
				Quaternion lowerRotation = _lower.rotation;

				Quaternion r0 = Quaternion.AngleAxis((ac_ab_1 - ac_ab_0) * Mathf.Rad2Deg, Quaternion.Inverse(upperRotation) * bendAxis);
				Quaternion r1 = Quaternion.AngleAxis((ba_bc_1 - ba_bc_0) * Mathf.Rad2Deg, Quaternion.Inverse(lowerRotation) * bendAxis);
				Quaternion r2 = reachAxis.sqrMagnitude > 1e-8f ? Quaternion.AngleAxis(ac_at_0 * Mathf.Rad2Deg, Quaternion.Inverse(upperRotation) * reachAxis.normalized) : Quaternion.identity;

				_upper.localRotation = _upper.localRotation * (r0 * r2);
				_lower.localRotation = _lower.localRotation * r1;
				_hand.rotation       = handRotation;
			}

			private static float Angle(Vector3 from, Vector3 to)
			{
				float lengths = from.magnitude * to.magnitude;
				return lengths > 1e-6f ? Mathf.Acos(Mathf.Clamp(Vector3.Dot(from, to) / lengths, -1.0f, 1.0f)) : 0.0f;
			}
		}
	}
}
