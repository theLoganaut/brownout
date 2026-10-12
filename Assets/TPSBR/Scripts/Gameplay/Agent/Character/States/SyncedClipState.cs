namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.Playables;
	using Fusion.Addons.AnimationController;

	/// <summary>
	/// Phase 10: a single clip that either plays on its own (like ClipState) or follows a game timer, so it ends when the action does
	/// (mantle, dash, heal, revive, punch). The owning layer calls SetSyncedTime every tick it wants the clip held to a timer.
	/// </summary>
	public sealed class SyncedClipState : ClipState
	{
		// PUBLIC MEMBERS

		/// <summary>Seconds the clip takes to play at its speed (Logan's takes play at half speed).</summary>
		public float Length => Node.Length / Mathf.Max(0.01f, Node.Speed);

		// PRIVATE MEMBERS

		private bool  _isSynced;
		private float _syncedTime;

		// PUBLIC METHODS

		/// <summary>
		/// Holds the clip at this normalized time (0–1) from the next tick on.
		/// </summary>
		public void SetSyncedTime(float normalizedTime)
		{
			_isSynced   = true;
			_syncedTime = Mathf.Clamp01(normalizedTime);
		}

		/// <summary>
		/// Starts the clip from its beginning, playing on its own.
		/// </summary>
		public void Restart()
		{
			_isSynced = false;
			SetAnimationTime(0.0f);
		}

		// AnimationState INTERFACE

		protected override void OnFixedUpdate()
		{
			if (_isSynced == false)
			{
				base.OnFixedUpdate();
				return;
			}

			SetAnimationTime(_syncedTime);
			Node.PlayableClip.SetTime(_syncedTime * Node.Length);
		}
	}
}
