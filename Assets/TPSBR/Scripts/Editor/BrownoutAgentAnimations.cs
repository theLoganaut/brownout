namespace TPSBR
{
	using System.Collections.Generic;
	using System.Text;
	using Fusion.Addons.AnimationController;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Phase 10 step 2: Brownout → Setup → Build Agent Animations. Puts Logan's soldier takes into the agent's animation states
	/// (AgentBase, so Soldier and Marine follow):
	/// - soldier.fbx clip settings: loops, and root motion left out of the clips the game moves itself (mantle, vault, dash, ...)
	/// - locomotion: idle, a walk ring at the walk speed and a sprint ring at the sprint speed for the unarmed set and both gun sets,
	///   and a fourth set for crouching. Directions Logan hasn't made (backwards, crouch strafes) keep the baked sample clips
	/// - jump, fall and land with a gun: Logan's Jump, Fall and Jump_Land (a fourth set mirrors the crouch set)
	/// - three new layers of SyncedClipStates: Moves (slide, mantle, vault, dash) above FullBody, Hands (heal, punch, throw) and
	///   Override (downed, revive, wingsuit, chute, landing roll) at the end
	/// Run Bake Humanoid Clips and Build Agent Body first. Safe to run again.
	/// </summary>
	public static class BrownoutAgentAnimations
	{
		// CONSTANTS

		private const string AGENT_BASE = "Assets/TPSBR/Prefabs/Agents/AgentBase.prefab";
		private const string MODEL      = BrownoutAgentBody.ModelPath;
		private const float  TakeRotationOffset = 180.0f;

		// Playback speed of the clips in the locomotion sets, on top of LOGAN_TAKE_SPEED. Logan, 9 Oct (after trying 0.5 × 1.5, then 0.7 and
		// 0.8): every animation plays at 1x, his takes and the sample's clips alike. Tune by feel.
		private const float LOGAN_WALK_PLAYBACK   = 1.0f;
		private const float LOGAN_SPRINT_PLAYBACK = 1.0f;
		private const float LOGAN_CROUCH_PLAYBACK = 1.0f;
		private const float SAMPLE_JOG_PLAYBACK   = 1.0f;
		// The sample's unarmed strafes (no gun; Logan has no unarmed strafe takes), walking and sprinting
		private const float SAMPLE_STRAFE_PLAYBACK = 1.0f;

		// Every one of Logan's takes plays at this speed on top of the above (9 Oct: 0.5, then back to 1 with everything else). Takes that
		// follow a game timer (mantle, vault, dash, heal, revive, punch, landing roll) still end with their action.
		public const float LOGAN_TAKE_SPEED = 1.0f;

		private static readonly string[] LoopingTakes =
		{
			"Idle", "Walk", "Walk_ForwardLeft", "Walk_ForwardRight", "Walk_Left", "Walk_Right",
			"Sprint", "Sprint_ForwardLeft", "Sprint_ForwardRight", "Sprint_Left", "Sprint_Right",
			"Crouch_Idle", "Crouch_Walk", "Slide", "Downed_Idle", "Downed_Crawl",
			"Drop_Skydive", "Drop_Dive", "Drop_Fall", "Fall", "Jump",
			"Unarmed_Idle", "Unarmed_Walk", "Unarmed_Sprint",
		};

		// The game moves the capsule through these, so the clip's own travel is left out (not baked into the pose)
		private static readonly string[] NoTravelTakes      = { "Mantle", "Vault", "Dash", "Slide_Enter", "Drop_Roll", "Drop_Release", "Downed_Enter", "Downed_GetUp" };
		private static readonly string[] NoVerticalTakes    = { "Mantle", "Vault" };

		// MENU

		[MenuItem("Brownout/Setup/Build Agent Animations", priority = 12)]
		private static void BuildMenu() => Debug.Log(BuildAll());

		[MenuItem("Brownout/Setup/Set Up First Person Arms", priority = 13)]
		private static void ArmsMenu() => Debug.Log(SetUpFirstPersonArms());

		// PUBLIC METHODS

		public static string BuildAll()
		{
			var log = new StringBuilder("Build Agent Animations:\n");

			ConfigureTakes(log);

			var takes = LoadTakes();
			if (takes.Count == 0)
				return log.Append("No takes found in " + MODEL).ToString();

			GameObject root = PrefabUtility.LoadPrefabContents(AGENT_BASE);
			try
			{
				Transform layers = root.transform.Find("AnimationLayers");

				RestoreSampleSpeeds(layers, log);
				BuildLocomotion(layers, takes, log);
				BuildAirborne(layers, takes, log);

				var upperMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(BrownoutHumanoidBaker.UpperMask);

				Transform fullBody = layers.Find("FullBody");
				BuildLayer<MovesLayer>(layers, "Moves", fullBody.GetSiblingIndex() + 1, null, takes, log,
					("Slide", "Slide", true), ("Mantle", "Mantle", false), ("Vault", "Vault", false), ("Dash", "Dash", false));

				BuildLayer<HandsLayer>(layers, "Hands", -1, upperMask, takes, log,
					("HealBandage", "Heal_Bandage", false), ("HealSyringe", "Heal_Syringe", false), ("Melee", "Melee", false), ("Jab", "Unarmed_Jab_R", false), ("Throw", "Grenade_Throw", false));

				BuildLayer<OverrideLayer>(layers, "Override", -1, null, takes, log,
					("DownedEnter", "Downed_Enter", false), ("DownedIdle", "Downed_Idle", true), ("DownedCrawl", "Downed_Crawl", true), ("DownedGetUp", "Downed_GetUp", false),
					("Revive", "Revive", false), ("Skydive", "Drop_Skydive", true), ("Dive", "Drop_Dive", true), ("ChuteRelease", "Drop_Release", false),
					("ChuteHang", "Drop_Fall", true), ("LandRoll", "Drop_Roll", false));

				// Hands, then Override, are the last two layers
				layers.Find("Hands").SetAsLastSibling();
				layers.Find("Override").SetAsLastSibling();

				PrefabUtility.SaveAsPrefabAsset(root, AGENT_BASE);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}

			log.AppendLine(SetUpFirstPersonArms());
			return log.ToString();
		}

		/// <summary>
		/// soldier_arms.fbx (Logan's first-person arms, the soldier's skeleton with the same takes): Generic with its takes (loops as
		/// on the body), the soldier's materials, and both linked in GunplaySettings for ViewmodelArms.
		/// </summary>
		public static string SetUpFirstPersonArms()
		{
			const string arms = BrownoutModelImport.CharactersFolder + "soldier_arms.fbx";

			var importer = AssetImporter.GetAtPath(arms) as ModelImporter;
			if (importer == null)
				return $"No {arms}.";

			importer.importAnimation = true;
			importer.animationType   = ModelImporterAnimationType.Generic;
			importer.avatarSetup     = ModelImporterAvatarSetup.NoAvatar;

			foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(MODEL))
			{
				if (asset is Material material)
				{
					importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
				}
			}

			ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
			foreach (ModelImporterClipAnimation clip in clips)
			{
				clip.loopTime = System.Array.IndexOf(LoopingTakes, GetTakeName(clip.name)) >= 0;
				clip.loopPose = false;
			}

			importer.clipAnimations = clips;
			importer.SaveAndReimport();

			var takes = new List<AnimationClip>();
			foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(arms))
			{
				if (asset is AnimationClip clip && clip.name.StartsWith("__preview") == false)
				{
					takes.Add(clip);
				}
			}

			var settings = AssetDatabase.LoadAssetAtPath<GunplaySettings>("Assets/TPSBR/Resources/Settings/GunplaySettings.asset");
			if (settings == null)
				return "No GunplaySettings asset.";

			settings.FirstPersonArms      = AssetDatabase.LoadAssetAtPath<GameObject>(arms);
			settings.FirstPersonArmsClips = takes.ToArray();
			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssets();

			return $"First person arms: {arms}, {takes.Count} takes linked in GunplaySettings.";
		}

		// PRIVATE METHODS

		private static void ConfigureTakes(StringBuilder log)
		{
			var importer = (ModelImporter)AssetImporter.GetAtPath(MODEL);
			ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;

			bool changed = importer.clipAnimations.Length == 0;

			foreach (ModelImporterClipAnimation clip in clips)
			{
				string take       = GetTakeName(clip.name);
				bool   loop       = System.Array.IndexOf(LoopingTakes, take) >= 0;
				bool   noTravel   = System.Array.IndexOf(NoTravelTakes, take) >= 0;
				bool   noVertical = System.Array.IndexOf(NoVerticalTakes, take) >= 0;

				// "Bake into pose" (lock) keeps the clip's movement in the pose; off hands it to the root, which the agent ignores
				bool bakeY  = noVertical == false;
				bool bakeXZ = noTravel == false;

				// The soldier faces -Z in the file, so its takes are turned around to face the agent's +Z
				if (clip.loopTime != loop || clip.loopPose == true || clip.lockRootRotation == false || clip.keepOriginalOrientation == false || clip.rotationOffset != TakeRotationOffset ||
					clip.lockRootHeightY != bakeY || clip.keepOriginalPositionY != bakeY || clip.lockRootPositionXZ != bakeXZ || clip.keepOriginalPositionXZ != bakeXZ)
				{
					clip.loopTime                = loop;
					clip.loopPose                = false;
					clip.lockRootRotation        = true;
					clip.keepOriginalOrientation = true;
					clip.rotationOffset          = TakeRotationOffset;
					clip.lockRootHeightY         = bakeY;
					clip.keepOriginalPositionY   = bakeY;
					clip.lockRootPositionXZ      = bakeXZ;
					clip.keepOriginalPositionXZ  = bakeXZ;
					changed = true;
				}
			}

			if (changed == true)
			{
				importer.clipAnimations = clips;
				importer.SaveAndReimport();
				log.AppendLine($"soldier.fbx: {clips.Length} takes set up (loops, root motion) and reimported.");
			}
			else
			{
				log.AppendLine("soldier.fbx: takes already set up.");
			}
		}

		private static string GetTakeName(string clipName)
		{
			int bar = clipName.LastIndexOf('|');
			return bar >= 0 ? clipName.Substring(bar + 1) : clipName;
		}

		private static Dictionary<string, AnimationClip> LoadTakes()
		{
			var takes = new Dictionary<string, AnimationClip>();
			foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(MODEL))
			{
				if (asset is AnimationClip clip && clip.name.StartsWith("__preview") == false)
				{
					takes[GetTakeName(clip.name)] = clip;
				}
			}
			return takes;
		}

		private static AnimationClip Baked(string sampleName)
		{
			return AssetDatabase.LoadAssetAtPath<AnimationClip>(BrownoutHumanoidBaker.BakedFolder + "/" + sampleName + ".anim");
		}

		/// <summary>
		/// The sample's own clip speeds, which its upper-body code times the gun draw and holster by (UpperBodyLayer steps through equip
		/// and unequip by the clip's progress). Logan's "everything at 1x" (9 Oct) is for his takes; these are the sample's clips and keep
		/// the sample's speeds (an earlier pass set them to 1 and the draw got stuck with the arm up).
		/// </summary>
		private static void RestoreSampleSpeeds(Transform layers, StringBuilder log)
		{
			int restored = 0;

			foreach (MonoBehaviour behaviour in layers.GetComponentsInChildren<MonoBehaviour>(true))
			{
				var serialized = new SerializedObject(behaviour);

				if (behaviour is DeadState)
				{
					restored += SetNodeSpeeds(serialized.FindProperty("_nodes"), 0, 1.5f);
				}
				else if (behaviour is EquipState || behaviour is UnequipState)
				{
					restored += SetNodeSpeeds(serialized.FindProperty("_nodes"), 1, 2.0f);
				}
				else if (behaviour.transform.parent != null && behaviour.transform.parent.GetComponent<GrenadeState>() != null && behaviour.GetType().Name == "ClipState")
				{
					SerializedProperty speed = serialized.FindProperty("_node.Speed");
					float sampleSpeed = behaviour.name == "Throw" ? 1.5f : (behaviour.name == "Equip" ? 2.0f : 1.0f);
					if (speed != null && Mathf.Approximately(speed.floatValue, sampleSpeed) == false)
					{
						speed.floatValue = sampleSpeed;
						++restored;
					}
				}

				serialized.ApplyModifiedPropertiesWithoutUndo();
			}

			log.AppendLine($"Sample clip speeds: {restored} restored (equip/unequip 2, death 1.5, grenade throw 1.5 / equip 2).");
		}

		private static int SetNodeSpeeds(SerializedProperty nodes, int from, float speed)
		{
			int count = 0;
			for (int i = from; nodes != null && i < nodes.arraySize; ++i)
			{
				SerializedProperty property = nodes.GetArrayElementAtIndex(i).FindPropertyRelative("Speed");
				if (property != null && Mathf.Approximately(property.floatValue, speed) == false)
				{
					property.floatValue = speed;
					++count;
				}
			}
			return count;
		}

		/// <summary>
		/// The Move state's sets. Node order 0 idle, 1 F, 2 B, 3 L, 4 R, 5 FL, 6 FR, 7 BL, 8 BR as in the sample, then the sprint ring.
		/// </summary>
		private static void BuildLocomotion(Transform layers, Dictionary<string, AnimationClip> takes, StringBuilder log)
		{
			var moveState = layers.GetComponentInChildren<MoveState>(true);
			var serialized = new SerializedObject(moveState);
			SerializedProperty sets = serialized.FindProperty("_sets");

			MovementSettings settings = AssetDatabase.LoadAssetAtPath<MovementSettings>("Assets/TPSBR/Resources/Settings/MovementSettings.asset");
			float walk   = settings.WalkSpeed;
			float sprint = settings.SprintSpeed;
			float crouch = settings.CrouchSpeed;
			float d      = 0.70710678f;

			var unarmed = new List<Node>
			{
				new Node(takes["Unarmed_Idle"],                   Vector2.zero,                        1.0f),
				new Node(takes["Unarmed_Walk"],                   new Vector2(0.0f, walk),             LOGAN_WALK_PLAYBACK),
				new Node(Baked("MOB1_Jog_B_Loop_IPC"),            new Vector2(0.0f, -walk),            SAMPLE_JOG_PLAYBACK),
				new Node(Baked("MOB1_Jog_L_Loop_IPC"),            new Vector2(-walk, 0.0f),            SAMPLE_STRAFE_PLAYBACK),
				new Node(Baked("MOB1_Jog_R_Loop_IPC"),            new Vector2(walk, 0.0f),             SAMPLE_STRAFE_PLAYBACK),
				new Node(Baked("MOB1_Jog_FL_Loop_IPC"),           new Vector2(-walk, walk) * d,        SAMPLE_STRAFE_PLAYBACK),
				new Node(Baked("MOB1_Jog_FR_Loop_IPC"),           new Vector2(walk, walk) * d,         SAMPLE_STRAFE_PLAYBACK),
				new Node(Baked("MOB1_Jog_BL_BkPd_Loop_IPC"),      new Vector2(-walk, -walk) * d,       SAMPLE_JOG_PLAYBACK),
				new Node(Baked("MOB1_Jog_BR_BkPd_Loop_IPC"),      new Vector2(walk, -walk) * d,        SAMPLE_JOG_PLAYBACK),
				new Node(takes["Unarmed_Sprint"],                 new Vector2(0.0f, sprint),           LOGAN_SPRINT_PLAYBACK),
				// The same strafes at sprint speed, so sprinting sideways doesn't speed them up (MoveState matches the clip to the speed)
				new Node(Baked("MOB1_Jog_L_Loop_IPC"),            new Vector2(-sprint, 0.0f),          SAMPLE_STRAFE_PLAYBACK),
				new Node(Baked("MOB1_Jog_R_Loop_IPC"),            new Vector2(sprint, 0.0f),           SAMPLE_STRAFE_PLAYBACK),
				new Node(Baked("MOB1_Jog_FL_Loop_IPC"),           new Vector2(-sprint, sprint) * d,    SAMPLE_STRAFE_PLAYBACK),
				new Node(Baked("MOB1_Jog_FR_Loop_IPC"),           new Vector2(sprint, sprint) * d,     SAMPLE_STRAFE_PLAYBACK),
			};

			var armed = new List<Node>
			{
				new Node(takes["Idle"],                           Vector2.zero,                        1.0f),
				new Node(takes["Walk"],                           new Vector2(0.0f, walk),             LOGAN_WALK_PLAYBACK),
				new Node(Baked("W2_Jog_Aim_B_Loop_IPC"),          new Vector2(0.0f, -walk),            SAMPLE_JOG_PLAYBACK),
				new Node(takes["Walk_Left"],                      new Vector2(-walk, 0.0f),            LOGAN_WALK_PLAYBACK),
				new Node(takes["Walk_Right"],                     new Vector2(walk, 0.0f),             LOGAN_WALK_PLAYBACK),
				new Node(takes["Walk_ForwardLeft"],               new Vector2(-walk, walk) * d,        LOGAN_WALK_PLAYBACK),
				new Node(takes["Walk_ForwardRight"],              new Vector2(walk, walk) * d,         LOGAN_WALK_PLAYBACK),
				new Node(Baked("W2_Jog_Aim_BL_BkPd_Loop_IPC"),    new Vector2(-walk, -walk) * d,       SAMPLE_JOG_PLAYBACK),
				new Node(Baked("W2_Jog_Aim_BR_BkPd_Loop_IPC"),    new Vector2(walk, -walk) * d,        SAMPLE_JOG_PLAYBACK),
				new Node(takes["Sprint"],                         new Vector2(0.0f, sprint),           LOGAN_SPRINT_PLAYBACK),
				new Node(takes["Sprint_Left"],                    new Vector2(-sprint, 0.0f),          LOGAN_SPRINT_PLAYBACK),
				new Node(takes["Sprint_Right"],                   new Vector2(sprint, 0.0f),           LOGAN_SPRINT_PLAYBACK),
				new Node(takes["Sprint_ForwardLeft"],             new Vector2(-sprint, sprint) * d,    LOGAN_SPRINT_PLAYBACK),
				new Node(takes["Sprint_ForwardRight"],            new Vector2(sprint, sprint) * d,     LOGAN_SPRINT_PLAYBACK),
			};

			// Crouch: Logan's crouch walk forward and on the forward diagonals; strafes are the walk strafes and backwards the sample's,
			// lowered into the crouch capsule by StancePose
			var crouched = new List<Node>
			{
				new Node(takes["Crouch_Idle"],                    Vector2.zero,                        1.0f),
				new Node(takes["Crouch_Walk"],                    new Vector2(0.0f, crouch),           LOGAN_CROUCH_PLAYBACK),
				new Node(Baked("W2_Jog_Aim_B_Loop_IPC"),          new Vector2(0.0f, -crouch),          SAMPLE_JOG_PLAYBACK),
				new Node(takes["Walk_Left"],                      new Vector2(-crouch, 0.0f),          LOGAN_WALK_PLAYBACK),
				new Node(takes["Walk_Right"],                     new Vector2(crouch, 0.0f),           LOGAN_WALK_PLAYBACK),
				new Node(takes["Crouch_Walk"],                    new Vector2(-crouch, crouch) * d,    LOGAN_CROUCH_PLAYBACK),
				new Node(takes["Crouch_Walk"],                    new Vector2(crouch, crouch) * d,     LOGAN_CROUCH_PLAYBACK),
				new Node(Baked("W2_Jog_Aim_BL_BkPd_Loop_IPC"),    new Vector2(-crouch, -crouch) * d,   SAMPLE_JOG_PLAYBACK),
				new Node(Baked("W2_Jog_Aim_BR_BkPd_Loop_IPC"),    new Vector2(crouch, -crouch) * d,    SAMPLE_JOG_PLAYBACK),
			};

			sets.arraySize = 4;
			WriteSet(sets.GetArrayElementAtIndex(0), unarmed);
			WriteSet(sets.GetArrayElementAtIndex(1), armed);
			WriteSet(sets.GetArrayElementAtIndex(2), armed);
			WriteSet(sets.GetArrayElementAtIndex(3), crouched);
			serialized.ApplyModifiedPropertiesWithoutUndo();

			log.AppendLine($"Move: unarmed {unarmed.Count}, armed {armed.Count} ×2 and crouch {crouched.Count} clips (walk {walk} m/s, sprint {sprint}, crouch {crouch}).");
		}

		/// <summary>
		/// Jump, Fall and Land mirror the Move state's sets: with a gun (sets 1 and 2) they play Logan's clips, unarmed keeps the
		/// sample's, and a fourth set (crouched) copies set 2.
		/// </summary>
		private static void BuildAirborne(Transform layers, Dictionary<string, AnimationClip> takes, StringBuilder log)
		{
			(string State, string Take)[] states = { ("Jump", "Jump"), ("Fall", "Fall"), ("Land", "Jump_Land") };

			foreach (var entry in states)
			{
				Transform state = layers.Find("FullBody/" + entry.State);
				var serialized = new SerializedObject(state.GetComponent<Fusion.Addons.AnimationController.AnimationState>());
				SerializedProperty sets = serialized.FindProperty("_sets");

				if (sets.arraySize < 3)
					continue;

				for (int setIndex = 1; setIndex <= 2; ++setIndex)
				{
					SerializedProperty nodes = sets.GetArrayElementAtIndex(setIndex).FindPropertyRelative("_nodes");
					for (int i = 0; i < nodes.arraySize; ++i)
					{
						nodes.GetArrayElementAtIndex(i).FindPropertyRelative("Clip").objectReferenceValue = takes[entry.Take];
					nodes.GetArrayElementAtIndex(i).FindPropertyRelative("Speed").floatValue          = LOGAN_TAKE_SPEED;
					}
				}

				if (sets.arraySize < 4)
				{
					sets.InsertArrayElementAtIndex(2); // Duplicates set 2 into index 3
				}

				serialized.ApplyModifiedPropertiesWithoutUndo();
				log.AppendLine($"{entry.State}: {entry.Take} with a gun, {sets.arraySize} sets.");
			}
		}

		private static void BuildLayer<T>(Transform layers, string name, int siblingIndex, AvatarMask mask, Dictionary<string, AnimationClip> takes, StringBuilder log, params (string State, string Take, bool Loop)[] clips) where T : AnimationLayer
		{
			Transform layerTransform = layers.Find(name);
			if (layerTransform == null)
			{
				layerTransform = new GameObject(name).transform;
				layerTransform.SetParent(layers, false);
			}

			if (siblingIndex >= 0)
			{
				layerTransform.SetSiblingIndex(siblingIndex);
			}

			T layer = layerTransform.GetComponent<T>();
			if (layer == null)
			{
				layer = layerTransform.gameObject.AddComponent<T>();
			}

			var serializedLayer = new SerializedObject(layer);
			serializedLayer.FindProperty("_avatarMask").objectReferenceValue = mask;
			serializedLayer.FindProperty("_isAdditive").boolValue            = false;
			serializedLayer.FindProperty("_initialWeight").floatValue        = 1.0f;
			serializedLayer.ApplyModifiedPropertiesWithoutUndo();

			foreach (var clip in clips)
			{
				if (takes.TryGetValue(clip.Take, out AnimationClip animation) == false)
				{
					log.AppendLine($"  {name}/{clip.State}: no take {clip.Take} in soldier.fbx");
					continue;
				}

				Transform stateTransform = layerTransform.Find(clip.State);
				if (stateTransform == null)
				{
					stateTransform = new GameObject(clip.State).transform;
					stateTransform.SetParent(layerTransform, false);
				}

				var state = stateTransform.GetComponent<SyncedClipState>();
				if (state == null)
				{
					state = stateTransform.gameObject.AddComponent<SyncedClipState>();
				}

				var serializedState = new SerializedObject(state);
				serializedState.FindProperty("_node.Clip").objectReferenceValue = animation;
				serializedState.FindProperty("_node.Speed").floatValue          = LOGAN_TAKE_SPEED;
				serializedState.FindProperty("_node.IsLooping").boolValue       = clip.Loop;
				serializedState.ApplyModifiedPropertiesWithoutUndo();
			}

			log.AppendLine($"{name}: {clips.Length} clips{(mask != null ? ", " + mask.name : ", full body")}.");
		}

		private static void WriteSet(SerializedProperty set, List<Node> nodes)
		{
			set.FindPropertyRelative("_speed").floatValue = 1.0f;

			SerializedProperty array = set.FindPropertyRelative("_nodes");
			array.arraySize = nodes.Count;

			for (int i = 0; i < nodes.Count; ++i)
			{
				SerializedProperty node = array.GetArrayElementAtIndex(i);
				node.FindPropertyRelative("Clip").objectReferenceValue = nodes[i].Clip;
				node.FindPropertyRelative("Position").vector2Value     = nodes[i].Position;
				node.FindPropertyRelative("Speed").floatValue          = nodes[i].Speed * (AssetDatabase.GetAssetPath(nodes[i].Clip) == MODEL ? LOGAN_TAKE_SPEED : 1.0f);
			}
		}

		// HELPERS

		private readonly struct Node
		{
			public readonly AnimationClip Clip;
			public readonly Vector2       Position;
			public readonly float         Speed;

			public Node(AnimationClip clip, Vector2 position, float speed)
			{
				Clip     = clip;
				Position = position;
				Speed    = speed;
			}
		}
	}
}
