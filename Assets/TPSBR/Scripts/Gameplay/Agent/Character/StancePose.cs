namespace TPSBR
{
	using UnityEngine;
	using Fusion.Addons.KCC;

	/// <summary>
	/// Placeholder third-person crouch and slide poses (phase 2) and the downed crawl pose (phase 4), until the Blender clips arrive.
	/// Applied right after every animation evaluation (server ticks and client renders alike): the hips drop, the upper body
	/// leans (forward for crouch, back for slide) until the head fits under the stance's capsule, and two-bone IK keeps the feet planted.
	/// The bone hitboxes follow, so lag-compensated hits match the lower capsule.
	/// Phase 10: on Logan's soldier the clips own the crouch, slide, downed and drop poses; this only steps in when a pose (for example
	/// a standing strafe clip while crouched) pokes out of the stance's capsule.
	/// </summary>
	public sealed class StancePose : MonoBehaviour
	{
		// PRIVATE MEMBERS

		[SerializeField, Tooltip("The top of the head hitbox ends this far below the top of the stance's capsule.")]
		private float _headClearance = 0.02f;
		[SerializeField, Tooltip("The hips never go lower than this above the feet while crouched.")]
		private float _crouchMinHipHeight = 0.42f;
		[SerializeField, Tooltip("The hips never go lower than this above the feet while sliding.")]
		private float _slideMinHipHeight = 0.24f;
		[SerializeField]
		private float _maxLeanAngle = 85.0f;
		[SerializeField, Tooltip("Slide: how far forward the feet reach, in meters.")]
		private float _slideFootReach = 0.55f;
		[SerializeField, Tooltip("Downed: the whole body tips forward this far around the hips, so it lies along the ground.")]
		private float _downedPitch = 80.0f;
		[SerializeField, Tooltip("Downed: the hips sit this high above the feet.")]
		private float _downedHipHeight = 0.2f;
		[SerializeField, Tooltip("Wingsuit (phase 7): the body tips forward this far around the hips, flat in the air.")]
		private float _wingsuitPitch = 80.0f;
		[SerializeField, Tooltip("Wings off: seconds for the body to come back from the glide angle to the release clip's pose.")]
		private float _wingsOffUpright = 0.3f;

		private Agent     _agent;
		private KCC       _kcc;
		private Transform _pelvis;
		private Transform _spine;
		private Transform _head;
		private BodyPart  _headHitbox;
		private Transform _leftClavicle;
		private Transform _rightClavicle;
		private Leg       _leftLeg;
		private Leg       _rightLeg;
		private bool      _isValid;
		private bool      _hasClipPoses;

		// PUBLIC METHODS

		/// <summary>
		/// Called by CharacterAnimationController right after the animation graph is evaluated.
		/// </summary>
		public void Apply()
		{
			if (_isValid == false || _agent == null || _agent.Object == null || _agent.Object.IsValid == false)
				return;
			if (_agent.Health.IsAlive == false)
				return;

			MovementSettings settings = MovementSettings.Current;
			KCCData          data     = _kcc.Data;

			float eyeHeight = AgentMovement.GetEyeHeight(_kcc, data);

			// Phase 7: the wingsuit lies flat, head first (placeholder; Logan's soldier has its own drop and downed clips, phase 10)
			if (data.MoveMode == EMoveMode.Wingsuit && _hasClipPoses == false)
			{
				Vector3 pelvisPosition = _pelvis.position;
				_pelvis.rotation = Quaternion.AngleAxis(_wingsuitPitch, transform.right) * _pelvis.rotation;
				_pelvis.position = pelvisPosition;
				return;
			}

			// Logan's drop clips: the whole body turns around the hips to KCCData.DropPitch (level = flat and head first, 90 = head down);
			// after the wings pop off it eases back to the release clip's own pose
			if (_hasClipPoses == true && (data.MoveMode == EMoveMode.Wingsuit || data.MoveMode == EMoveMode.WingsOff))
			{
				float dropWeight = 1.0f;
				if (data.MoveMode == EMoveMode.WingsOff)
				{
					dropWeight = 1.0f - Mathf.Clamp01((_agent.Runner.Tick - data.ModeStartTick) * _agent.Runner.DeltaTime / _wingsOffUpright);
				}

				if (dropWeight > 0.001f)
				{
					ApplyDropPitch(data.DropPitch, dropWeight);
				}
				return;
			}

			if (data.Stance == EStance.Downed)
			{
				if (_hasClipPoses == true)
					return;

				ApplyDowned(eyeHeight, settings);
				return;
			}

			// Logan's clips: only a crouch or slide capsule needs the pose to fit. Standing (e.g. jumping out of a slide while the eye is still
			// coming up) leaves the clip alone; squeezing a standing jump into the slide height pulled the torso and legs together (9 Oct)
			if (_hasClipPoses == true && data.Stance == EStance.Stand)
				return;

			bool  isSlide   = data.Stance == EStance.Slide || (data.Stance == EStance.Stand && eyeHeight < settings.CrouchEyeHeight);

			float targetEye = isSlide == true ? settings.SlideEyeHeight : settings.CrouchEyeHeight;
			float weight    = Mathf.Clamp01((settings.StandEyeHeight - eyeHeight) / Mathf.Max(0.01f, settings.StandEyeHeight - targetEye));
			if (weight <= 0.001f)
				return;

			Transform root    = transform;
			Vector3   up      = root.up;
			Vector3   forward = root.forward;
			Vector3   right   = root.right;
			float     rootY   = Vector3.Dot(root.position, up);

			// Feet before anything moves: crouch keeps them where they are, slide pushes them forward
			Vector3 leftFoot  = _leftLeg.Foot.position;
			Vector3 rightFoot = _rightLeg.Foot.position;
			Quaternion leftFootRotation  = _leftLeg.Foot.rotation;
			Quaternion rightFootRotation = _rightLeg.Foot.rotation;

			// Logan's slide clip already has the legs out front
			if (isSlide == true && _hasClipPoses == false)
			{
				leftFoot  += forward * (_slideFootReach * weight);
				rightFoot += forward * (_slideFootReach * 0.7f * weight);
			}

			// 1. Drop the hips
			float pelvisHeight = Vector3.Dot(_pelvis.position, up) - rootY;
			float minHip       = isSlide == true ? _slideMinHipHeight : _crouchMinHipHeight;
			// The head hitbox (center + radius) has to fit under the stance's capsule
			float targetHead   = Mathf.Lerp(Vector3.Dot(GetHeadPoint(), up) - rootY, settings.GetHeight(isSlide == true ? EStance.Slide : EStance.Crouch) - GetHeadRadius() - _headClearance, weight);
			float headHeight   = Vector3.Dot(GetHeadPoint(), up) - rootY;
			float neededDrop   = Mathf.Max(0.0f, headHeight - targetHead);
			float hipDrop      = Mathf.Min(neededDrop, Mathf.Max(0.0f, pelvisHeight - minHip));

			_pelvis.position -= up * hipDrop;

			// 2. Lean the upper body until the head is low enough. Crouch leans forward, slide leans back.
			// The animation's aim pose already tips the spine, so measure and correct a few times
			float totalLean = 0.0f;
			for (int i = 0; i < 3; ++i)
			{
				Vector3 headPoint = GetHeadPoint();
				headHeight = Vector3.Dot(headPoint, up) - rootY;
				if (headHeight <= targetHead + 0.01f)
					break;

				Vector3 spineBase    = _spine.position;
				float   spineBaseY   = Vector3.Dot(spineBase, up) - rootY;
				float   headDistance = Vector3.Distance(spineBase, headPoint);
				if (headDistance < 0.05f)
					break;

				// Angle of the head from upright, positive on the side we lean toward (forward for crouch, back for slide)
				float leanSide     = isSlide == true ? -1.0f : 1.0f;
				float currentAngle = Mathf.Acos(Mathf.Clamp((headHeight - spineBaseY) / headDistance, -1.0f, 1.0f));
				if (Vector3.Dot(headPoint - spineBase, forward) * leanSide < 0.0f)
				{
					currentAngle = -currentAngle;
				}

				float targetAngle = Mathf.Acos(Mathf.Clamp((targetHead - spineBaseY) / headDistance, -1.0f, 1.0f));
				float lean        = Mathf.Clamp((targetAngle - currentAngle) * Mathf.Rad2Deg, 0.0f, _maxLeanAngle - totalLean);

				if (lean <= 0.1f)
					break;

				_spine.rotation = Quaternion.AngleAxis(isSlide == true ? -lean : lean, right) * _spine.rotation;
				totalLean += lean;
			}

			// 3. Shoulders turn back by the same amount, so the arms and gun stay level and on target
			if (totalLean > 0.0f)
			{
				Quaternion counter = Quaternion.AngleAxis(isSlide == true ? totalLean : -totalLean, right);
				if (_leftClavicle  != null) { _leftClavicle.rotation  = counter * _leftClavicle.rotation;  }
				if (_rightClavicle != null) { _rightClavicle.rotation = counter * _rightClavicle.rotation; }
			}

			// 4. Legs reach the feet again (with Logan's clips only if the pose had to change)
			if (_hasClipPoses == true && hipDrop <= 0.0f && totalLean <= 0.0f)
				return;

			_leftLeg.Solve(leftFoot, forward);
			_rightLeg.Solve(rightFoot, forward);

			_leftLeg.Foot.rotation  = leftFootRotation;
			_rightLeg.Foot.rotation = rightFootRotation;
		}

		/// <summary>
		/// The drop: turns the body around the hips (about the body's left-right axis) until the hips-to-head line points along the look
		/// tipped down by pitch: forward and level at 0, straight down at 90. Measured from the clip's pose, so any glide or dive clip works.
		/// </summary>
		private void ApplyDropPitch(float pitch, float weight)
		{
			Transform root    = transform;
			Vector3   right   = root.right;
			Vector3   target  = Quaternion.AngleAxis(pitch, right) * root.forward;
			Vector3   current = Vector3.ProjectOnPlane(_head.position - _pelvis.position, right);
			if (current.sqrMagnitude < 0.0001f)
				return;

			float angle = Vector3.SignedAngle(current, target, right) * weight;

			Vector3 pelvisPosition = _pelvis.position;
			_pelvis.rotation = Quaternion.AngleAxis(angle, right) * _pelvis.rotation;
			_pelvis.position = pelvisPosition;
		}

		/// <summary>
		/// Downed crawl (placeholder): the body tips forward around the hips until it lies along the ground, head ahead and legs trailing,
		/// and the hips drop close to the ground, so every hitbox is low.
		/// </summary>
		private void ApplyDowned(float eyeHeight, MovementSettings settings)
		{
			float weight = Mathf.Clamp01((settings.StandEyeHeight - eyeHeight) / Mathf.Max(0.01f, settings.StandEyeHeight - settings.DownedEyeHeight));
			if (weight <= 0.001f)
				return;

			Transform root  = transform;
			Vector3   up    = root.up;
			float     rootY = Vector3.Dot(root.position, up);

			Vector3 pelvisPosition = _pelvis.position;
			float   pelvisHeight   = Vector3.Dot(pelvisPosition, up) - rootY;

			_pelvis.rotation = Quaternion.AngleAxis(_downedPitch * weight, root.right) * _pelvis.rotation;
			_pelvis.position = pelvisPosition - up * ((pelvisHeight - _downedHipHeight) * weight);
		}

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_agent = GetComponent<Agent>();
			_kcc   = GetComponent<KCC>();

			Transform rootBone = _agent.Character != null && _agent.Character.ThirdPersonView != null ? _agent.Character.ThirdPersonView.RootBone : null;
			Transform search   = rootBone != null ? rootBone : transform;

			// Humanoid body (phase 10): bones from the avatar; the sample's body by name
			_pelvis = CharacterBones.Get(search, HumanBodyBones.Hips, "pelvis");
			_spine  = CharacterBones.Get(search, HumanBodyBones.Spine, "spine_01");
			_head   = CharacterBones.Get(search, HumanBodyBones.Head, "head");

			// The soldier's head hitbox sits on a child of the head bone
			_headHitbox    = _head != null ? _head.GetComponentInChildren<BodyPart>(true) : null;
			_leftClavicle  = CharacterBones.Get(search, HumanBodyBones.LeftShoulder, "clavicle_l");
			_rightClavicle = CharacterBones.Get(search, HumanBodyBones.RightShoulder, "clavicle_r");

			_leftLeg  = new Leg(CharacterBones.Get(search, HumanBodyBones.LeftUpperLeg, "thigh_l"), CharacterBones.Get(search, HumanBodyBones.LeftLowerLeg, "calf_l"), CharacterBones.Get(search, HumanBodyBones.LeftFoot, "foot_l"));
			_rightLeg = new Leg(CharacterBones.Get(search, HumanBodyBones.RightUpperLeg, "thigh_r"), CharacterBones.Get(search, HumanBodyBones.RightLowerLeg, "calf_r"), CharacterBones.Get(search, HumanBodyBones.RightFoot, "foot_r"));

			Animator animator = rootBone != null && rootBone.parent != null ? rootBone.parent.GetComponent<Animator>() : null;
			_hasClipPoses = animator != null && animator.avatar != null && animator.avatar.isHuman == true;

			_isValid = _pelvis != null && _spine != null && _head != null && _leftLeg.IsValid == true && _rightLeg.IsValid == true;

			if (_isValid == false)
			{
				Debug.LogWarning($"{nameof(StancePose)}: skeleton bones not found on {name}; crouch and slide poses are off.", this);
			}
		}

		// PRIVATE METHODS

		private Vector3 GetHeadPoint()
		{
			return _headHitbox != null ? _headHitbox.transform.TransformPoint(_headHitbox.Offset) : _head.position;
		}

		private float GetHeadRadius()
		{
			return _headHitbox != null && _headHitbox.SphereRadius > 0.0f ? _headHitbox.SphereRadius : 0.12f;
		}

		// HELPERS

		private readonly struct Leg
		{
			public readonly Transform Thigh;
			public readonly Transform Calf;
			public readonly Transform Foot;

			public bool IsValid => Thigh != null && Calf != null && Foot != null;

			public Leg(Transform thigh, Transform calf, Transform foot)
			{
				Thigh = thigh;
				Calf  = calf;
				Foot  = foot;
			}

			/// <summary>
			/// Two-bone IK: thigh and calf turn so the foot reaches the target, knee bending toward the hint.
			/// </summary>
			public void Solve(Vector3 target, Vector3 kneeHint)
			{
				Vector3 a = Thigh.position;
				Vector3 b = Calf.position;
				Vector3 c = Foot.position;

				float lengthAB = Vector3.Distance(a, b);
				float lengthBC = Vector3.Distance(b, c);
				float lengthAT = Mathf.Clamp(Vector3.Distance(a, target), 0.01f, lengthAB + lengthBC - 0.001f);

				Vector3 ac = c - a;
				if (ac.sqrMagnitude < 0.000001f || lengthAB < 0.001f || lengthBC < 0.001f)
					return;

				float currentHip  = Mathf.Acos(Mathf.Clamp(Vector3.Dot(ac.normalized, (b - a).normalized), -1.0f, 1.0f));
				float currentKnee = Mathf.Acos(Mathf.Clamp(Vector3.Dot((a - b).normalized, (c - b).normalized), -1.0f, 1.0f));
				float targetHip   = Mathf.Acos(Mathf.Clamp((lengthBC * lengthBC - lengthAB * lengthAB - lengthAT * lengthAT) / (-2.0f * lengthAB * lengthAT), -1.0f, 1.0f));
				float targetKnee  = Mathf.Acos(Mathf.Clamp((lengthAT * lengthAT - lengthAB * lengthAB - lengthBC * lengthBC) / (-2.0f * lengthAB * lengthBC), -1.0f, 1.0f));

				Vector3 axis = Vector3.Cross(ac, kneeHint);
				if (axis.sqrMagnitude < 0.000001f)
					return;

				axis.Normalize();

				Thigh.rotation = Quaternion.AngleAxis((targetHip  - currentHip)  * Mathf.Rad2Deg, axis) * Thigh.rotation;
				Calf.rotation  = Quaternion.AngleAxis((targetKnee - currentKnee) * Mathf.Rad2Deg, axis) * Calf.rotation;

				// Swing the whole leg so the foot lands on the target
				Vector3 newAC = Foot.position - a;
				Vector3 toTarget = target - a;
				if (newAC.sqrMagnitude > 0.000001f && toTarget.sqrMagnitude > 0.000001f)
				{
					Thigh.rotation = Quaternion.FromToRotation(newAC, toTarget) * Thigh.rotation;
				}
			}
		}
	}
}
