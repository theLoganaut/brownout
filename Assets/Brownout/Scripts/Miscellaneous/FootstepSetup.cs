using System;
using UnityEngine;

namespace TPSBR
{
	[CreateAssetMenu(menuName = "TPSBR/Footstep Setup")]
	public class FootstepSetup : ScriptableObject
	{
		// PRIVATE MEMBERS

		[SerializeField]
		private AudioSetup _fallbackWalkSound;
		[SerializeField]
		private AudioSetup _fallbackRunSound;
		[SerializeField]
		private FootstepData[] _footsteps;

		[NonSerialized]
		private bool _initialized;
		[NonSerialized]
		private int _untaggedHash;

		// PUBLIC METHODS

		public AudioSetup GetSound(int tagHash, bool isRunning)
		{
			AudioSetup sample = GetSampleSound(tagHash, isRunning, out string surface);

			// Phase 10: Logan's clips for this surface (SoundSettings), else the sample's
			SoundSettings settings = SoundSettings.Current;
			return settings != null ? settings.GetFootstepSound(surface, isRunning, sample) : sample;
		}

		/// <summary>Phase 10 (Sound Check): the surfaces the sample has footstep sounds for.</summary>
		public string[] GetSurfaces()
		{
			var surfaces = new string[_footsteps != null ? _footsteps.Length : 0];
			for (int i = 0; i < surfaces.Length; ++i)
			{
				surfaces[i] = _footsteps[i].Tag;
			}
			return surfaces;
		}

		private AudioSetup GetSampleSound(int tagHash, bool isRunning, out string surface)
		{
			surface = SoundSettings.DefaultSurface;

			if (tagHash == 0 || tagHash == _untaggedHash)
				return isRunning == true ? _fallbackRunSound : _fallbackWalkSound;

			if (_initialized == false)
			{
				_untaggedHash = "Untagged".GetHashCode();

				for (int i = 0; i < _footsteps.Length; i++)
				{
					_footsteps[i].TagHash = _footsteps[i].Tag.GetHashCode();
				}

				_initialized = true;
			}

			for (int i = 0; i < _footsteps.Length; i++)
			{
				if (_footsteps[i].TagHash == tagHash)
				{
					surface = _footsteps[i].Tag;
					return isRunning == true ? _footsteps[i].SoundRun : _footsteps[i].SoundWalk;
				}
			}

			return isRunning == true ? _fallbackRunSound : _fallbackWalkSound;
		}

		// HELPERS

		[Serializable]
		private class FootstepData
		{
			public string Tag;
			public AudioSetup SoundWalk;
			public AudioSetup SoundRun;

			[NonSerialized]
			public int TagHash;
		}
	}
}
