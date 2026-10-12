namespace TPSBR
{
	using System.Collections.Generic;
	using System.IO;
	using System.Text;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 10: Brownout → Setup → Bake Humanoid Clips. The sample's body is a Generic rig, so its clips only play on that skeleton.
	/// This builds a Humanoid avatar for the sample's skeleton (it is in a T-pose, as the avatar builder wants), samples every clip the
	/// agent's animation states use frame by frame, reads each pose back as Humanoid muscles and writes it as a Humanoid clip.
	/// Logan's soldier (a Humanoid) then plays every sample animation. Also makes Humanoid versions of the three layer masks.
	/// Output in Assets/Brownout/Animations/ (Baked/ for the clips, same names as the sample's). Safe to run again: it overwrites.
	/// </summary>
	public static class BrownoutHumanoidBaker
	{
		// CONSTANTS

		public const string OutputFolder = "Assets/Brownout/Animations";
		public const string BakedFolder  = OutputFolder + "/Baked";
		public const string AvatarPath   = OutputFolder + "/SampleHumanoidAvatar.asset";
		public const string UpperMask    = OutputFolder + "/UpperBodyMask_Humanoid.mask";
		public const string LowerMask    = OutputFolder + "/LowerBodyMask_Humanoid.mask";
		public const string LookMask     = OutputFolder + "/UpperBodyMaskLook_Humanoid.mask";

		private const string SAMPLE_MODEL = "Assets/3rdParty/Maksim Bugrimov/Sci_Fi_Character_08/Mesh/Sci_Fi_Character_08_Generic.FBX";
		private const string AGENT_PREFAB = "Assets/TPSBR/Prefabs/Agents/AgentBase.prefab";

		// Sample bone → Unity Humanoid bone. Fingers: thumb_01 is the thumb's proximal bone, as Unity counts them.
		private static readonly string[,] BoneMap =
		{
			{ "Hips", "pelvis" }, { "Spine", "spine_01" }, { "Chest", "spine_02" }, { "UpperChest", "spine_03" }, { "Neck", "neck_01" }, { "Head", "head" },
			{ "LeftShoulder", "clavicle_l" }, { "LeftUpperArm", "upperarm_l" }, { "LeftLowerArm", "lowerarm_l" }, { "LeftHand", "hand_l" },
			{ "RightShoulder", "clavicle_r" }, { "RightUpperArm", "upperarm_r" }, { "RightLowerArm", "lowerarm_r" }, { "RightHand", "hand_r" },
			{ "LeftUpperLeg", "thigh_l" }, { "LeftLowerLeg", "calf_l" }, { "LeftFoot", "foot_l" }, { "LeftToes", "ball_l" },
			{ "RightUpperLeg", "thigh_r" }, { "RightLowerLeg", "calf_r" }, { "RightFoot", "foot_r" }, { "RightToes", "ball_r" },
		};

		private static readonly string[,] FingerMap =
		{
			{ "Thumb", "thumb" }, { "Index", "index" }, { "Middle", "middle" }, { "Ring", "ring" }, { "Little", "pinky" },
		};

		// MENU

		[MenuItem("Brownout/Setup/Bake Humanoid Clips", priority = 10)]
		private static void BakeMenu() => Debug.Log(BakeAll());

		// PUBLIC METHODS

		public static string BakeAll()
		{
			var log = new StringBuilder();

			EnsureFolder(OutputFolder);
			EnsureFolder(BakedFolder);

			var model = AssetDatabase.LoadAssetAtPath<GameObject>(SAMPLE_MODEL);
			if (model == null)
				return "Bake Humanoid Clips: the sample model is missing at " + SAMPLE_MODEL;

			var instance = Object.Instantiate(model);
			instance.hideFlags = HideFlags.HideAndDontSave;

			try
			{
				Avatar avatar = BuildAvatar(instance, log);
				if (avatar == null)
					return log.ToString();

				var restPose = RecordRest(instance.transform);
				var handler  = new HumanPoseHandler(avatar, instance.transform);

				List<AnimationClip> clips = GetAgentClips();
				int baked = 0;

				foreach (AnimationClip clip in clips)
				{
					ApplyRest(restPose);
					BakeClip(clip, instance, handler, log);
					++baked;
				}

				handler.Dispose();

				MakeMask(UpperMask, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK);
				MakeMask(LowerMask, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg, AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK);
				MakeMask(LookMask, AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK);

				AssetDatabase.SaveAssets();

				log.Insert(0, $"Bake Humanoid Clips: {baked} clips baked to {BakedFolder}, avatar {AvatarPath}, 3 masks.\n");
			}
			finally
			{
				Object.DestroyImmediate(instance);
			}

			return log.ToString();
		}

		/// <summary>
		/// The baked copy of a sample clip, or null.
		/// </summary>
		public static AnimationClip GetBaked(AnimationClip source)
		{
			if (source == null)
				return null;

			return AssetDatabase.LoadAssetAtPath<AnimationClip>(GetBakedPath(source));
		}

		public static string GetBakedPath(AnimationClip source)
		{
			return BakedFolder + "/" + source.name + ".anim";
		}

		/// <summary>
		/// Every clip the agent's animation states reference (the AnimationLayers subtree of AgentBase).
		/// </summary>
		public static List<AnimationClip> GetAgentClips()
		{
			var result = new List<AnimationClip>();
			var agent  = AssetDatabase.LoadAssetAtPath<GameObject>(AGENT_PREFAB);
			Transform layers = agent != null ? agent.transform.Find("AnimationLayers") : null;
			if (layers == null)
				return result;

			foreach (MonoBehaviour behaviour in layers.GetComponentsInChildren<MonoBehaviour>(true))
			{
				var serialized = new SerializedObject(behaviour);
				SerializedProperty property = serialized.GetIterator();

				while (property.Next(true) == true)
				{
					if (property.propertyType != SerializedPropertyType.ObjectReference)
						continue;

					var clip = property.objectReferenceValue as AnimationClip;
					if (clip != null && clip.isHumanMotion == false && result.Contains(clip) == false && AssetDatabase.GetAssetPath(clip).StartsWith(BakedFolder) == false)
					{
						result.Add(clip);
					}
				}
			}

			return result;
		}

		// PRIVATE METHODS

		private static Avatar BuildAvatar(GameObject instance, StringBuilder log)
		{
			var human = new List<HumanBone>();

			for (int i = 0; i < BoneMap.GetLength(0); ++i)
			{
				AddBone(human, BoneMap[i, 0], BoneMap[i, 1], instance.transform, log);
			}

			string[] sides     = { "Left", "Right" };
			string[] suffixes  = { "_l", "_r" };
			string[] segments  = { "Proximal", "Intermediate", "Distal" };

			for (int side = 0; side < 2; ++side)
			{
				for (int finger = 0; finger < FingerMap.GetLength(0); ++finger)
				{
					for (int segment = 0; segment < 3; ++segment)
					{
						string humanName = $"{sides[side]} {FingerMap[finger, 0]} {segments[segment]}";
						string boneName  = $"{FingerMap[finger, 1]}_0{segment + 1}{suffixes[side]}";
						AddBone(human, humanName, boneName, instance.transform, log);
					}
				}
			}

			var skeleton = new List<SkeletonBone>();
			foreach (Transform bone in instance.GetComponentsInChildren<Transform>(true))
			{
				skeleton.Add(new SkeletonBone
				{
					name     = bone.name,
					position = bone.localPosition,
					rotation = bone.localRotation,
					scale    = bone.localScale,
				});
			}

			var description = new HumanDescription
			{
				human             = human.ToArray(),
				skeleton          = skeleton.ToArray(),
				upperArmTwist     = 0.5f,
				lowerArmTwist     = 0.5f,
				upperLegTwist     = 0.5f,
				lowerLegTwist     = 0.5f,
				armStretch        = 0.05f,
				legStretch        = 0.05f,
				feetSpacing       = 0.0f,
				hasTranslationDoF = false,
			};

			Avatar avatar = AvatarBuilder.BuildHumanAvatar(instance, description);
			if (avatar == null || avatar.isValid == false || avatar.isHuman == false)
			{
				log.AppendLine("Bake Humanoid Clips: the avatar for the sample's skeleton is not valid.");
				return null;
			}

			avatar.name = "SampleHumanoidAvatar";

			var existing = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
			if (existing != null)
			{
				AssetDatabase.DeleteAsset(AvatarPath);
			}

			AssetDatabase.CreateAsset(avatar, AvatarPath);
			return avatar;
		}

		private static void AddBone(List<HumanBone> human, string humanName, string boneName, Transform root, StringBuilder log)
		{
			if (FindDeep(root, boneName) == null)
			{
				log.AppendLine($"  missing sample bone {boneName} for {humanName}");
				return;
			}

			var bone = new HumanBone { humanName = humanName, boneName = boneName };
			bone.limit.useDefaultValues = true;
			human.Add(bone);
		}

		private static void BakeClip(AnimationClip source, GameObject instance, HumanPoseHandler handler, StringBuilder log)
		{
			float frameRate = source.frameRate > 0.0f ? source.frameRate : 30.0f;
			int   frames    = Mathf.Max(1, Mathf.RoundToInt(source.length * frameRate)) + 1;

			string[] muscleNames = HumanTrait.MuscleName;
			int      muscleCount = HumanTrait.MuscleCount;

			var muscles = new AnimationCurve[muscleCount];
			for (int i = 0; i < muscleCount; ++i)
			{
				muscles[i] = new AnimationCurve();
			}

			var rootT = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
			var rootQ = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };

			var pose = new HumanPose();

			for (int frame = 0; frame < frames; ++frame)
			{
				float time = Mathf.Min(frame / frameRate, source.length);

				source.SampleAnimation(instance, time);
				handler.GetHumanPose(ref pose);

				for (int i = 0; i < muscleCount; ++i)
				{
					muscles[i].AddKey(new Keyframe(time, pose.muscles[i]));
				}

				rootT[0].AddKey(time, pose.bodyPosition.x);
				rootT[1].AddKey(time, pose.bodyPosition.y);
				rootT[2].AddKey(time, pose.bodyPosition.z);
				rootQ[0].AddKey(time, pose.bodyRotation.x);
				rootQ[1].AddKey(time, pose.bodyRotation.y);
				rootQ[2].AddKey(time, pose.bodyRotation.z);
				rootQ[3].AddKey(time, pose.bodyRotation.w);
			}

			string path  = GetBakedPath(source);
			var    baked = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
			bool   isNew = baked == null;

			if (isNew == true)
			{
				baked = new AnimationClip();
			}
			else
			{
				baked.ClearCurves();
			}

			baked.name      = source.name;
			baked.frameRate = frameRate;

			for (int i = 0; i < muscleCount; ++i)
			{
				baked.SetCurve("", typeof(Animator), muscleNames[i], muscles[i]);
			}

			string[] axes = { "x", "y", "z", "w" };
			for (int i = 0; i < 3; ++i)
			{
				baked.SetCurve("", typeof(Animator), "RootT." + axes[i], rootT[i]);
			}
			for (int i = 0; i < 4; ++i)
			{
				baked.SetCurve("", typeof(Animator), "RootQ." + axes[i], rootQ[i]);
			}

			// Non-transform curves (if any) come across as they are
			foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
			{
				if (binding.type == typeof(Transform))
					continue;

				AnimationUtility.SetEditorCurve(baked, binding, AnimationUtility.GetEditorCurve(source, binding));
			}

			// Same loop and additive reference as the sample's clip. Everything is baked into the pose, as on the Generic rig
			// (the agent never uses root motion).
			AnimationClipSettings sourceSettings = AnimationUtility.GetAnimationClipSettings(source);
			AnimationClipSettings settings       = AnimationUtility.GetAnimationClipSettings(baked);

			settings.loopTime                    = sourceSettings.loopTime;
			settings.loopBlend                   = sourceSettings.loopBlend;
			settings.cycleOffset                 = sourceSettings.cycleOffset;
			settings.mirror                      = false;
			settings.loopBlendOrientation        = true;
			settings.loopBlendPositionY          = true;
			settings.loopBlendPositionXZ         = true;
			settings.keepOriginalOrientation     = true;
			settings.keepOriginalPositionY       = true;
			settings.keepOriginalPositionXZ      = true;
			settings.heightFromFeet              = false;
			settings.hasAdditiveReferencePose    = sourceSettings.hasAdditiveReferencePose;
			settings.additiveReferencePoseTime   = sourceSettings.additiveReferencePoseTime;
			settings.additiveReferencePoseClip   = sourceSettings.additiveReferencePoseClip != null ? GetBaked(sourceSettings.additiveReferencePoseClip) : null;
			settings.startTime                   = 0.0f;
			settings.stopTime                    = source.length;

			AnimationUtility.SetAnimationClipSettings(baked, settings);
			AnimationUtility.SetAnimationEvents(baked, AnimationUtility.GetAnimationEvents(source));

			if (isNew == true)
			{
				AssetDatabase.CreateAsset(baked, path);
			}
			else
			{
				EditorUtility.SetDirty(baked);
			}

			if (sourceSettings.additiveReferencePoseClip != null)
			{
				log.AppendLine($"  {source.name}: additive reference clip {sourceSettings.additiveReferencePoseClip.name}");
			}
		}

		private static void MakeMask(string path, params AvatarMaskBodyPart[] activeParts)
		{
			var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
			if (mask == null)
			{
				mask = new AvatarMask();
				AssetDatabase.CreateAsset(mask, path);
			}

			for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; ++part)
			{
				mask.SetHumanoidBodyPartActive(part, System.Array.IndexOf(activeParts, part) >= 0);
			}

			mask.transformCount = 0;
			EditorUtility.SetDirty(mask);
		}

		private static Dictionary<Transform, (Vector3, Quaternion, Vector3)> RecordRest(Transform root)
		{
			var rest = new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
			foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
			{
				rest[bone] = (bone.localPosition, bone.localRotation, bone.localScale);
			}
			return rest;
		}

		private static void ApplyRest(Dictionary<Transform, (Vector3, Quaternion, Vector3)> rest)
		{
			foreach (var pair in rest)
			{
				pair.Key.localPosition = pair.Value.Item1;
				pair.Key.localRotation = pair.Value.Item2;
				pair.Key.localScale    = pair.Value.Item3;
			}
		}

		private static Transform FindDeep(Transform parent, string name)
		{
			if (parent.name == name)
				return parent;

			foreach (Transform child in parent)
			{
				Transform found = FindDeep(child, name);
				if (found != null)
					return found;
			}

			return null;
		}

		private static void EnsureFolder(string path)
		{
			if (AssetDatabase.IsValidFolder(path) == true)
				return;

			string parent = Path.GetDirectoryName(path).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
		}
	}
}
