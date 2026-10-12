namespace TPSBR
{
	using System.Collections.Generic;
	using System.Text;
	using Fusion;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 10: Brownout → Setup → Build Agent Body. Makes Logan's soldier the body of the two agent prefabs (Soldier and Marine,
	/// variants of AgentBase; players, dummies and bots all use them):
	/// - the soldier model goes under VisualsRoot as "SoldierBody", with its Humanoid avatar, and drives the animation states
	/// - every hitbox moves to the matching soldier bone (on a child object, so it keeps the sample bone's orientation), sized to the soldier
	/// - the weapon, back, belt and grenade handles move to the matching soldier bones
	/// - every reference into the sample's body (bones, the Animator) is pointed at the soldier
	/// - every animation state plays the Humanoid copies of the sample's clips (Bake Humanoid Clips) with Humanoid masks
	/// - the sample's body is switched off (kept in the prefab)
	/// Both skeletons are put in the same Humanoid pose first, so positions and rotations carry across. The first build moves the hitboxes
	/// and handles and deletes the sample's hitboxes (HitboxRoot counts every hitbox under it, switched off or not). Running it again keeps
	/// the soldier body with its hitboxes and handles and refreshes the rest (avatar, gear meshes, references, clips, the hitbox list).
	/// </summary>
	public static class BrownoutAgentBody
	{
		// CONSTANTS

		public const string BodyName   = "SoldierBody";
		public const string ModelPath  = "Assets/Brownout/Art/Characters/soldier.fbx";

		private const string HITBOX_PREFIX = "Hitbox_";
		private const string AGENT_BASE    = "Assets/TPSBR/Prefabs/Agents/AgentBase.prefab";

		private static readonly string[] AgentPrefabs =
		{
			"Assets/TPSBR/Prefabs/Agents/Soldier.prefab",
			"Assets/TPSBR/Prefabs/Agents/Marine.prefab",
		};

		// Built-in gear meshes in soldier.fbx, hidden until BodyGear shows them (phase 10 step 4)
		public static readonly string[] GearMeshes = BodyGear.GearMeshes;

		// For limb hitboxes: the next bone down the limb, to measure the segment on both skeletons
		private static readonly Dictionary<HumanBodyBones, HumanBodyBones> SegmentEnd = new Dictionary<HumanBodyBones, HumanBodyBones>
		{
			{ HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm  }, { HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftHand  },
			{ HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm }, { HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
			{ HumanBodyBones.LeftUpperLeg,  HumanBodyBones.LeftLowerLeg  }, { HumanBodyBones.LeftLowerLeg,  HumanBodyBones.LeftFoot  },
			{ HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg }, { HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot },
		};

		// MENU

		[MenuItem("Brownout/Setup/Build Agent Body", priority = 11)]
		private static void BuildMenu() => Debug.Log(BuildAll());

		// PUBLIC METHODS

		public static string BuildAll()
		{
			var log = new StringBuilder();

			var sampleAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(BrownoutHumanoidBaker.AvatarPath);
			var model        = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
			if (sampleAvatar == null)
				return "Build Agent Body: run Brownout → Setup → Bake Humanoid Clips first.";
			if (model == null)
				return "Build Agent Body: the soldier model is missing at " + ModelPath;

			int swapped = SwapClips(AGENT_BASE, log);
			log.AppendLine($"AgentBase: {swapped} clip and mask references now Humanoid.");

			foreach (string path in AgentPrefabs)
			{
				BuildPrefab(path, model, sampleAvatar, log);
			}

			AssetDatabase.SaveAssets();
			return "Build Agent Body:\n" + log;
		}

		/// <summary>
		/// Brownout → Setup → Fix Gun Grip: only re-places the rifle and pistol handles on the soldier's right hand (both agent prefabs).
		/// </summary>
		[MenuItem("Brownout/Setup/Fix Gun Grip", priority = 14)]
		private static void FixGunGripMenu() => Debug.Log(FixGunGrip());

		public static string FixGunGrip()
		{
			var log = new StringBuilder("Fix Gun Grip:\n");

			foreach (string path in AgentPrefabs)
			{
				GameObject root = PrefabUtility.LoadPrefabContents(path);
				try
				{
					Transform body = root.transform.Find("VisualsRoot/" + BodyName);
					if (body == null)
					{
						log.AppendLine($"{path}: no soldier body, skipped.");
						continue;
					}

					log.Append(path + ": ");
					PlaceGunHandles(body, log);
					PrefabUtility.SaveAsPrefabAsset(root, path);
				}
				finally
				{
					PrefabUtility.UnloadPrefabContents(root);
				}
			}

			AssetDatabase.SaveAssets();
			return log.ToString();
		}

		// PRIVATE METHODS

		/// <summary>
		/// The gun in the hand the way Logan holds it: his first-person arms (soldier_arms.fbx, the soldier's skeleton) carry the gun on
		/// their Weapon bone, so in the armed Idle take the Weapon bone's pose relative to Hand.R is the grip. The weapon handles on the
		/// soldier's Hand.R take that pose (the gun's origin on the grip, barrel forward as the arms hold it). The first build's handles
		/// came from the sample's hand and ended up about 1.1 m off the hand (the gun floated above the head, 9 Oct).
		/// </summary>
		private static void PlaceGunHandles(Transform body, StringBuilder log)
		{
			if (GetArmsGrip(out Vector3 gripPosition, out Quaternion gripRotation, out string problem) == false)
			{
				log.AppendLine($"  gun grip: {problem}");
				return;
			}

			Transform hand = FindDeep(body, "Hand.R");
			int placed = 0;

			foreach (string handleName in new[] { "WeaponHandleRifle", "WeaponHandlePistol" })
			{
				Transform handle = hand != null ? hand.Find(handleName) : null;
				if (handle == null)
					continue;

				handle.SetLocalPositionAndRotation(gripPosition, gripRotation);
				++placed;
			}

			log.AppendLine($"  gun grip: {placed} weapon handles on Hand.R at {gripPosition.ToString("F3")} (from soldier_arms' Weapon bone in Idle).");
		}

		private static bool GetArmsGrip(out Vector3 position, out Quaternion rotation, out string problem)
		{
			position = default;
			rotation = Quaternion.identity;
			problem  = null;

			const string armsPath = BrownoutModelImport.CharactersFolder + "soldier_arms.fbx";

			var arms = AssetDatabase.LoadAssetAtPath<GameObject>(armsPath);
			AnimationClip idle = null;
			foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(armsPath))
			{
				if (asset is AnimationClip clip && clip.name.StartsWith("__preview") == false && clip.name.EndsWith("|Idle") == true)
				{
					idle = clip;
				}
			}

			if (arms == null || idle == null)
			{
				problem = $"no {armsPath} or no Idle take in it";
				return false;
			}

			GameObject instance = Object.Instantiate(arms);
			instance.hideFlags = HideFlags.HideAndDontSave;

			try
			{
				Transform root   = instance.transform;
				Transform weapon = FindDeep(root, "Weapon");
				Transform hand   = FindDeep(root, "Hand.R");
				if (weapon == null || hand == null)
				{
					problem = "soldier_arms has no Weapon or Hand.R bone";
					return false;
				}

				// As ViewmodelArms: the rest pose holds the gun with the barrel along the model's -Z (the model faces -Z)
				Quaternion weaponToGun = Quaternion.Inverse(Quaternion.Inverse(root.rotation) * weapon.rotation) * Quaternion.Euler(0.0f, 180.0f, 0.0f);

				idle.SampleAnimation(instance, 0.0f);

				position = hand.InverseTransformPoint(weapon.position);
				rotation = Quaternion.Inverse(hand.rotation) * (weapon.rotation * weaponToGun);
				return true;
			}
			finally
			{
				Object.DestroyImmediate(instance);
			}
		}

		private static void BuildPrefab(string path, GameObject model, Avatar sampleAvatar, StringBuilder log)
		{
			GameObject root = PrefabUtility.LoadPrefabContents(path);

			try
			{
				Transform visuals = root.transform.Find("VisualsRoot");
				Animator  sample  = FindSampleAnimator(visuals);

				if (visuals == null || sample == null)
				{
					log.AppendLine($"{path}: no sample body under VisualsRoot, skipped.");
					return;
				}

				// A rebuild keeps the soldier body, its hitboxes and handles, and refreshes everything else
				Transform existing = visuals.Find(BodyName);
				bool      isNew    = existing == null;

				sample.gameObject.SetActive(true);

				GameObject body = existing != null ? existing.gameObject : (GameObject)PrefabUtility.InstantiatePrefab(model, visuals);
				body.name = BodyName;
				body.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
				body.transform.localScale = Vector3.one;
				SetLayer(body.transform, sample.gameObject.layer);

				var animator = body.GetComponent<Animator>();
				if (animator == null)
				{
					animator = body.AddComponent<Animator>();
				}

				animator.avatar                    = GetModelAvatar();
				animator.runtimeAnimatorController = null;
				animator.applyRootMotion           = false;
				animator.cullingMode               = sample.cullingMode;
				animator.updateMode                = sample.updateMode;

				foreach (string gearName in GearMeshes)
				{
					// The drop suit hangs off the chest bone, the rest off the model root
					Transform gear = FindDeep(body.transform, gearName);
					if (gear != null)
					{
						gear.gameObject.SetActive(false);
					}
				}

				CopyRendererSettings(sample.transform, body.transform);

				// Same Humanoid pose on both skeletons
				var sampleBones  = MapBones(sample.transform, GetSampleMapping());
				var soldierBones = MapBones(body.transform, GetModelMapping());

				var restPose = RecordPose(sample.transform, body.transform);

				PoseNeutral(sampleAvatar, sample.transform);
				PoseNeutral(animator.avatar, body.transform);

				float heightRatio = GetHeightRatio(sampleBones, soldierBones);
				var   remap       = new Dictionary<Object, Object>();

				remap[sample]           = animator;
				remap[sample.transform] = body.transform;
				remap[sample.gameObject] = body;

				Transform sampleRig  = sample.transform.Find("root");
				Transform soldierRig = body.transform.Find("SoldierRig");
				if (sampleRig != null && soldierRig != null)
				{
					remap[sampleRig] = soldierRig;
				}

				foreach (var pair in sampleBones)
				{
					if (soldierBones.TryGetValue(pair.Key, out Transform soldierBone) == true)
					{
						remap[pair.Value] = soldierBone;
					}
				}

				int hitboxes = isNew == true ? MoveHitboxes(sample, sampleBones, soldierBones, heightRatio, remap, log) : body.GetComponentsInChildren<BodyPart>(true).Length;
				int handles  = isNew == true ? MoveHandles(sample, model, sampleBones, soldierBones, heightRatio, log) : body.GetComponentsInChildren<BodyHandleOrigin>(true).Length;

				// Back to the rest pose, so the bones carry no overrides (the hitboxes and handles keep their places on them)
				ApplyPose(restPose);

				PlaceGunHandles(body.transform, log);

				int references = RemapReferences(root, sample.transform, remap, log);
				AssignBodyReferences(root, animator, soldierBones);

				// The sample's hitboxes go (HitboxRoot counts every hitbox under it, switched off or not), then the root lists the soldier's
				foreach (BodyPart hitbox in sample.GetComponentsInChildren<BodyPart>(true))
				{
					Object.DestroyImmediate(hitbox);
				}

				var hitboxRoot = root.GetComponent<HitboxRoot>();
				if (hitboxRoot != null)
				{
					hitboxRoot.InitHitboxes();
					if (hitboxRoot.Hitboxes.Length != hitboxes)
					{
						log.AppendLine($"  the hitbox root lists {hitboxRoot.Hitboxes.Length} hitboxes, expected {hitboxes}");
					}
				}

				SwapClips(root, log);

				sample.gameObject.SetActive(false);

				PrefabUtility.SaveAsPrefabAsset(root, path);
				log.AppendLine($"{path}: soldier body in, {hitboxes} hitboxes and {handles} handles moved, {references} references repointed (height ×{heightRatio:F3}).");
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		private static Animator FindSampleAnimator(Transform visuals)
		{
			if (visuals == null)
				return null;

			foreach (Animator animator in visuals.GetComponentsInChildren<Animator>(true))
			{
				if (animator.name.StartsWith("Sci_Fi_Character") == true)
					return animator;
			}

			return null;
		}

		private static Avatar GetModelAvatar()
		{
			foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
			{
				if (asset is Avatar avatar)
					return avatar;
			}

			return null;
		}

		// Humanoid bone name → skeleton bone name
		private static Dictionary<string, string> GetModelMapping()
		{
			var mapping  = new Dictionary<string, string>();
			var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
			foreach (HumanBone bone in importer.humanDescription.human)
			{
				mapping[bone.humanName] = bone.boneName;
			}
			return mapping;
		}

		private static Dictionary<string, string> GetSampleMapping()
		{
			var mapping = new Dictionary<string, string>();
			var avatar  = AssetDatabase.LoadAssetAtPath<Avatar>(BrownoutHumanoidBaker.AvatarPath);
			foreach (HumanBone bone in avatar.humanDescription.human)
			{
				mapping[bone.humanName] = bone.boneName;
			}
			return mapping;
		}

		private static Dictionary<HumanBodyBones, Transform> MapBones(Transform root, Dictionary<string, string> mapping)
		{
			var result = new Dictionary<HumanBodyBones, Transform>();
			string[] humanNames = HumanTrait.BoneName;

			for (int i = 0; i < (int)HumanBodyBones.LastBone; ++i)
			{
				if (mapping.TryGetValue(humanNames[i], out string boneName) == false)
					continue;

				Transform bone = FindDeep(root, boneName);
				if (bone != null)
				{
					result[(HumanBodyBones)i] = bone;
				}
			}

			return result;
		}

		private static void PoseNeutral(Avatar avatar, Transform root)
		{
			using (var handler = new HumanPoseHandler(avatar, root))
			{
				var pose = new HumanPose();
				handler.GetHumanPose(ref pose);

				for (int i = 0; i < pose.muscles.Length; ++i)
				{
					pose.muscles[i] = 0.0f;
				}

				pose.bodyRotation = Quaternion.identity;
				handler.SetHumanPose(ref pose);
			}
		}

		private static float GetHeightRatio(Dictionary<HumanBodyBones, Transform> sample, Dictionary<HumanBodyBones, Transform> soldier)
		{
			float sampleHeight  = Vector3.Distance(sample[HumanBodyBones.Head].position,  sample[HumanBodyBones.LeftFoot].position);
			float soldierHeight = Vector3.Distance(soldier[HumanBodyBones.Head].position, soldier[HumanBodyBones.LeftFoot].position);
			return sampleHeight > 0.01f ? soldierHeight / sampleHeight : 1.0f;
		}

		private static int MoveHitboxes(Animator sample, Dictionary<HumanBodyBones, Transform> sampleBones, Dictionary<HumanBodyBones, Transform> soldierBones, float heightRatio, Dictionary<Object, Object> remap, StringBuilder log)
		{
			var moved = new List<Hitbox>();

			foreach (BodyPart source in sample.GetComponentsInChildren<BodyPart>(true))
			{
				HumanBodyBones humanBone = FindHumanBone(sampleBones, source.transform);
				if (humanBone == HumanBodyBones.LastBone || soldierBones.TryGetValue(humanBone, out Transform target) == false)
				{
					log.AppendLine($"  hitbox on {source.name}: no matching soldier bone, left out");
					continue;
				}

				float alongRatio = heightRatio;
				if (SegmentEnd.TryGetValue(humanBone, out HumanBodyBones end) == true && sampleBones.ContainsKey(end) == true && soldierBones.ContainsKey(end) == true)
				{
					float sampleLength  = Vector3.Distance(sampleBones[humanBone].position,  sampleBones[end].position);
					float soldierLength = Vector3.Distance(soldierBones[humanBone].position, soldierBones[end].position);
					alongRatio = sampleLength > 0.01f ? soldierLength / sampleLength : heightRatio;
				}

				// Bone direction in the sample bone's own axes: the biggest offset component
				Vector3 offset   = source.Offset;
				int     alongAxis = Mathf.Abs(offset.x) >= Mathf.Abs(offset.y) ? (Mathf.Abs(offset.x) >= Mathf.Abs(offset.z) ? 0 : 2) : (Mathf.Abs(offset.y) >= Mathf.Abs(offset.z) ? 1 : 2);
				bool    isLimb    = SegmentEnd.ContainsKey(humanBone) == true && offset.sqrMagnitude > 0.0001f;

				Vector3 sampleCenter  = source.transform.TransformPoint(offset);
				Vector3 soldierCenter = target.position + (sampleCenter - source.transform.position) * (isLimb == true ? alongRatio : heightRatio);

				var holder = new GameObject(HITBOX_PREFIX + humanBone);
				holder.layer = source.gameObject.layer;
				holder.transform.SetParent(target, false);
				holder.transform.SetPositionAndRotation(soldierCenter, source.transform.rotation);

				var copy = holder.AddComponent<BodyPart>();
				EditorUtility.CopySerialized(source, copy);

				var serialized = new SerializedObject(copy);
				serialized.FindProperty("Offset").vector3Value = Vector3.zero;

				Vector3 extents = source.BoxExtents * heightRatio;
				if (isLimb == true)
				{
					extents[alongAxis] = source.BoxExtents[alongAxis] * alongRatio;
				}

				serialized.FindProperty("BoxExtents").vector3Value = extents;
				serialized.FindProperty("SphereRadius").floatValue = source.SphereRadius * heightRatio;
				serialized.ApplyModifiedPropertiesWithoutUndo();

				remap[source] = copy;
				moved.Add(copy);
			}

			return moved.Count;
		}

		/// <summary>
		/// Objects the prefab added to the sample's skeleton (weapon, back, belt and grenade handles) move to the matching soldier bone.
		/// A finger's handle goes on the hand. Moving the objects themselves keeps every reference to them.
		/// </summary>
		private static int MoveHandles(Animator sample, GameObject model, Dictionary<HumanBodyBones, Transform> sampleBones, Dictionary<HumanBodyBones, Transform> soldierBones, float heightRatio, StringBuilder log)
		{
			var modelNames = new HashSet<string>();
			var sampleModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3rdParty/Maksim Bugrimov/Sci_Fi_Character_08/Mesh/Sci_Fi_Character_08_Generic.FBX");
			foreach (Transform bone in sampleModel.GetComponentsInChildren<Transform>(true))
			{
				modelNames.Add(bone.name);
			}

			var handles = new List<Transform>();
			foreach (Transform t in sample.GetComponentsInChildren<Transform>(true))
			{
				if (t != sample.transform && modelNames.Contains(t.name) == false && t.name.StartsWith(HITBOX_PREFIX) == false && t.parent != null && modelNames.Contains(t.parent.name) == true)
				{
					handles.Add(t);
				}
			}

			int moved = 0;
			foreach (Transform handle in handles)
			{
				HumanBodyBones humanBone = FindHumanBone(sampleBones, handle.parent);
				if (humanBone >= HumanBodyBones.LeftThumbProximal && humanBone <= HumanBodyBones.RightLittleDistal)
				{
					humanBone = humanBone < HumanBodyBones.RightThumbProximal ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
				}

				if (humanBone == HumanBodyBones.LastBone || soldierBones.TryGetValue(humanBone, out Transform target) == false || sampleBones.TryGetValue(humanBone, out Transform sampleBone) == false)
				{
					log.AppendLine($"  handle {handle.name}: no matching soldier bone, left on the sample");
					continue;
				}

				Vector3    position = target.position + (handle.position - sampleBone.position) * heightRatio;
				Quaternion rotation = handle.rotation;

				handle.SetParent(target, true);
				handle.SetPositionAndRotation(position, rotation);

				// Remember where it came from, so a rebuild can put it back first
				handle.gameObject.AddComponent<BodyHandleOrigin>().SourceBone = sampleBone.name;
				++moved;
			}

			return moved;
		}

		/// <summary>
		/// Every serialized reference outside the sample's body that points into it is repointed at the soldier.
		/// </summary>
		private static int RemapReferences(GameObject root, Transform sampleRoot, Dictionary<Object, Object> remap, StringBuilder log)
		{
			int count = 0;

			foreach (Component component in root.GetComponentsInChildren<Component>(true))
			{
				if (component == null || component is Transform)
					continue;
				if (component.transform.IsChildOf(sampleRoot) == true)
					continue;

				var serialized = new SerializedObject(component);
				SerializedProperty property = serialized.GetIterator();
				bool changed = false;

				while (property.Next(true) == true)
				{
					if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null)
						continue;

					Object value = property.objectReferenceValue;
					Transform valueTransform = value is Component c ? c.transform : value is GameObject g ? g.transform : null;
					if (valueTransform == null || valueTransform.IsChildOf(sampleRoot) == false)
						continue;

					if (remap.TryGetValue(value, out Object replacement) == true)
					{
						property.objectReferenceValue = replacement;
						changed = true;
						++count;
					}
					else
					{
						log.AppendLine($"  {component.GetType().Name}.{property.propertyPath} on {component.name} still points at the sample's {value.name}");
					}
				}

				if (changed == true)
				{
					serialized.ApplyModifiedPropertiesWithoutUndo();
				}
			}

			return count;
		}

		/// <summary>
		/// The body references the agent's scripts hold, set from the soldier's Humanoid bones every build.
		/// </summary>
		private static void AssignBodyReferences(GameObject root, Animator animator, Dictionary<HumanBodyBones, Transform> bones)
		{
			var animation = root.GetComponent<CharacterAnimationController>();
			if (animation != null)
			{
				var serialized = new SerializedObject(animation);
				serialized.FindProperty("_animator").objectReferenceValue     = animator;
				serialized.FindProperty("_leftHand").objectReferenceValue     = bones[HumanBodyBones.LeftHand];
				serialized.FindProperty("_leftLowerArm").objectReferenceValue = bones[HumanBodyBones.LeftLowerArm];
				serialized.FindProperty("_leftUpperArm").objectReferenceValue = bones[HumanBodyBones.LeftUpperArm];
				serialized.ApplyModifiedPropertiesWithoutUndo();
			}

			var character = root.GetComponent<Character>();
			if (character != null)
			{
				var serialized = new SerializedObject(character);
				serialized.FindProperty("_thirdPersonView.RootBone").objectReferenceValue      = animator.transform.Find("SoldierRig");
				serialized.FindProperty("_thirdPersonView.HeadTransform").objectReferenceValue = bones[HumanBodyBones.Head];
				serialized.FindProperty("_thirdPersonView.LeftFoot").objectReferenceValue      = bones[HumanBodyBones.LeftFoot];
				serialized.FindProperty("_thirdPersonView.RightFoot").objectReferenceValue     = bones[HumanBodyBones.RightFoot];
				serialized.ApplyModifiedPropertiesWithoutUndo();
			}
		}

		private static int SwapClips(string path, StringBuilder log)
		{
			GameObject root = PrefabUtility.LoadPrefabContents(path);
			try
			{
				int count = SwapClips(root, log);
				PrefabUtility.SaveAsPrefabAsset(root, path);
				return count;
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		/// <summary>
		/// Every clip and mask in the animation states → its Humanoid copy.
		/// </summary>
		private static int SwapClips(GameObject root, StringBuilder log)
		{
			Transform layers = root.transform.Find("AnimationLayers");
			if (layers == null)
				return 0;

			var masks = new Dictionary<string, AvatarMask>
			{
				{ "UpperBodyMask_Marine",     AssetDatabase.LoadAssetAtPath<AvatarMask>(BrownoutHumanoidBaker.UpperMask) },
				{ "LowerBodyMask_Marine",     AssetDatabase.LoadAssetAtPath<AvatarMask>(BrownoutHumanoidBaker.LowerMask) },
				{ "UpperBodyMaskLook_Marine", AssetDatabase.LoadAssetAtPath<AvatarMask>(BrownoutHumanoidBaker.LookMask)  },
			};

			int count = 0;

			foreach (MonoBehaviour behaviour in layers.GetComponentsInChildren<MonoBehaviour>(true))
			{
				var serialized = new SerializedObject(behaviour);
				SerializedProperty property = serialized.GetIterator();
				bool changed = false;

				while (property.Next(true) == true)
				{
					if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null)
						continue;

					if (property.objectReferenceValue is AnimationClip clip && clip.isHumanMotion == false)
					{
						AnimationClip baked = BrownoutHumanoidBaker.GetBaked(clip);
						if (baked != null)
						{
							property.objectReferenceValue = baked;
							changed = true;
							++count;
						}
						else
						{
							log.AppendLine($"  {behaviour.name}: no baked copy of {clip.name}");
						}
					}
					else if (property.objectReferenceValue is AvatarMask mask && masks.TryGetValue(mask.name, out AvatarMask humanMask) == true && humanMask != null)
					{
						property.objectReferenceValue = humanMask;
						changed = true;
						++count;
					}
				}

				if (changed == true)
				{
					serialized.ApplyModifiedPropertiesWithoutUndo();
				}
			}

			return count;
		}

		private static HumanBodyBones FindHumanBone(Dictionary<HumanBodyBones, Transform> bones, Transform transform)
		{
			for (Transform t = transform; t != null; t = t.parent)
			{
				foreach (var pair in bones)
				{
					if (pair.Value == t)
						return pair.Key;
				}
			}

			return HumanBodyBones.LastBone;
		}

		private static Dictionary<Transform, (Vector3, Quaternion, Vector3)> RecordPose(params Transform[] roots)
		{
			var pose = new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
			foreach (Transform root in roots)
			{
				foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
				{
					pose[t] = (t.localPosition, t.localRotation, t.localScale);
				}
			}
			return pose;
		}

		private static void ApplyPose(Dictionary<Transform, (Vector3, Quaternion, Vector3)> pose)
		{
			foreach (var pair in pose)
			{
				if (pair.Key == null)
					continue;

				pair.Key.SetLocalPositionAndRotation(pair.Value.Item1, pair.Value.Item2);
				pair.Key.localScale = pair.Value.Item3;
			}
		}

		private static void CopyRendererSettings(Transform sample, Transform body)
		{
			SkinnedMeshRenderer reference = sample.GetComponentInChildren<SkinnedMeshRenderer>(true);
			if (reference == null)
				return;

			foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				renderer.shadowCastingMode    = reference.shadowCastingMode;
				renderer.receiveShadows       = reference.receiveShadows;
				renderer.updateWhenOffscreen  = reference.updateWhenOffscreen;
				renderer.skinnedMotionVectors = reference.skinnedMotionVectors;
				renderer.quality              = reference.quality;
			}
		}

		private static void SetLayer(Transform root, int layer)
		{
			foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
			{
				t.gameObject.layer = layer;
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
	}
}
