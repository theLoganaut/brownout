namespace TPSBR
{
	using System.Collections.Generic;
	using Fusion.Addons.AnimationController;

	using AnimationState = Fusion.Addons.AnimationController.AnimationState;

	/// <summary>
	/// Phase 10: base for the layers that play one of Logan's soldier clips at a time (Moves, Hands, Override). Each tick the layer picks
	/// the clip that matches the game state and fades it in; every other clip in the layer fades out. No clip means the layer is off.
	/// </summary>
	public abstract class ClipLayer : AnimationLayer
	{
		// PRIVATE MEMBERS

		private readonly List<SyncedClipState> _clips = new List<SyncedClipState>(8);

		// PROTECTED METHODS

		protected SyncedClipState Find(string stateName)
		{
			var state = FindState<SyncedClipState>(stateName, false);
			if (state != null && _clips.Contains(state) == false)
			{
				_clips.Add(state);
			}
			return state;
		}

		/// <summary>
		/// Fades this clip in (from its start if it wasn't playing) and the others out. Null fades everything out.
		/// </summary>
		protected void Show(SyncedClipState clip, float fadeDuration)
		{
			for (int i = 0, count = _clips.Count; i < count; ++i)
			{
				SyncedClipState other = _clips[i];
				if (other != clip && other.IsActive() == true)
				{
					other.Deactivate(fadeDuration);
				}
			}

			if (clip == null)
				return;

			if (clip.IsActive() == false || clip.IsFadingOut() == true)
			{
				clip.Restart();
				clip.Activate(fadeDuration);
			}
		}

		protected bool IsShowing(SyncedClipState clip)
		{
			return clip != null && clip.IsActive() == true && clip.IsFadingOut() == false;
		}

		protected float GetSeconds(int fromTick)
		{
			return (Controller.Runner.Tick - fromTick) * Controller.Runner.DeltaTime;
		}

		protected bool HasActiveClip()
		{
			for (int i = 0, count = _clips.Count; i < count; ++i)
			{
				if (_clips[i].IsActive() == true && _clips[i].IsFadingOut() == false)
					return true;
			}

			return false;
		}
	}
}
