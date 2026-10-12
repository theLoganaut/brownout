namespace Fusion.Addons.AnimationController
{
	using System;
	using UnityEngine;
	using UnityEngine.Animations;
	using UnityEngine.Playables;

	[Serializable]
	public sealed class ClipNode
	{
		// PUBLIC MEMBERS

		public AnimationClip         Clip;
		[NonSerialized]
		public AnimationClipPlayable PlayableClip;
		public float                 Speed = 1.0f;
		public bool                  IsLooping;

		public float                 Length => Clip.length;

		// PUBLIC METHODS

		public void CreatePlayable(PlayableGraph graph)
		{
			PlayableClip = AnimationClipPlayable.Create(graph, Clip);
			// Brownout (phase 10): the Humanoid clips baked from the sample's have no IK goal curves, and foot IK would pull the feet to the body's center
			PlayableClip.SetApplyFootIK(false);
		}

		public void DestroyPlayable()
		{
			if (PlayableClip.IsValid() == true)
			{
				PlayableClip.Destroy();
			}

			PlayableClip = default;
		}
	}
}
