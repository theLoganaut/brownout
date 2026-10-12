namespace TPSBR
{
	using UnityEngine;

	/// <summary>
	/// Phase 10: finds a body bone. On a Humanoid body (Logan's soldier) through the avatar's bone map, otherwise by the sample's bone name.
	/// The root bone is CharacterView.RootBone; its parent holds the body's Animator.
	/// </summary>
	public static class CharacterBones
	{
		public static Transform Get(Transform rootBone, HumanBodyBones bone, string sampleName)
		{
			if (rootBone == null)
				return null;

			Animator animator = rootBone.parent != null ? rootBone.parent.GetComponent<Animator>() : null;
			if (animator != null && animator.avatar != null && animator.isHuman == true)
			{
				Transform humanBone = animator.GetBoneTransform(bone);
				if (humanBone != null)
					return humanBone;
			}

			return FindDeep(rootBone, sampleName);
		}

		public static Transform FindDeep(Transform parent, string boneName)
		{
			if (parent.name == boneName)
				return parent;

			for (int i = 0, count = parent.childCount; i < count; ++i)
			{
				Transform found = FindDeep(parent.GetChild(i), boneName);
				if (found != null)
					return found;
			}

			return null;
		}
	}
}
