using System;
using System.Collections.Generic;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Phase 10 step 7: Logan's sound clips by slot. Brownout → Data → Import Sounds fills it from the clips in Assets/Brownout/Audio/ by
	/// their names (SFX_&lt;Gun&gt;_Fire, SFX_&lt;Gun&gt;_FireSuppressed, SFX_&lt;Gun&gt;_Reload, SFX_Footstep_&lt;Surface&gt;[_Run], each with an optional
	/// _01, _02 ... for variations). A slot without clips plays the sample's sound. Volume, pitch spread and range stay the sample's
	/// (the suppressor's shorter range is GunStats.GunshotRange, from brownout_guns.json). Lives in Resources/Settings/SoundSettings.asset.
	/// </summary>
	[CreateAssetMenu(fileName = "SoundSettings", menuName = "TPSBR/Sound Settings")]
	public sealed class SoundSettings : ScriptableObject
	{
		// HELPERS

		public enum EGunSound
		{
			Fire           = 0,
			FireSuppressed = 1,
			Reload         = 2,
		}

		[Serializable]
		public sealed class GunSounds
		{
			[Tooltip("GunData.GunName.")]
			public string      Gun;
			public AudioClip[] Fire;
			[Tooltip("With a Suppressor on. Empty: the normal fire clips (or the sample's).")]
			public AudioClip[] FireSuppressed;
			public AudioClip[] Reload;

			public AudioClip[] Get(EGunSound sound)
			{
				switch (sound)
				{
					case EGunSound.FireSuppressed: return FireSuppressed;
					case EGunSound.Reload:         return Reload;
					default:                       return Fire;
				}
			}
		}

		[Serializable]
		public sealed class FootstepSounds
		{
			[Tooltip("The ground's tag (the sample's footstep surfaces), or Default for untagged ground.")]
			public string      Surface;
			public AudioClip[] Walk;
			[Tooltip("Empty: the walk clips.")]
			public AudioClip[] Run;
		}

		// PUBLIC MEMBERS

		public const string DefaultSurface = "Default";

		public GunSounds[]      Guns      = new GunSounds[0];
		public FootstepSounds[] Footsteps = new FootstepSounds[0];

		private static SoundSettings _current;

		/// <summary>The asset in Resources/Settings (null until Import Sounds has made it; everything then plays the sample's sounds).</summary>
		public static SoundSettings Current
		{
			get
			{
				if (_current == null)
				{
					_current = Resources.Load<SoundSettings>("Settings/SoundSettings");
				}

				return _current;
			}
		}

		// PRIVATE MEMBERS

		[NonSerialized]
		private Dictionary<string, AudioSetup> _setups;

		// PUBLIC METHODS

		/// <summary>The gun's sound: Logan's clips in the sample's setup (volume, pitch), or the sample's setup when the slot is empty.</summary>
		public AudioSetup GetGunSound(string gun, EGunSound sound, AudioSetup sampleSetup)
		{
			GunSounds sounds = FindGun(gun);
			AudioClip[] clips = sounds != null ? sounds.Get(sound) : null;

			if (IsEmpty(clips) == true && sound == EGunSound.FireSuppressed && sounds != null)
			{
				clips = sounds.Fire;
			}

			return IsEmpty(clips) == true ? sampleSetup : GetSetup($"gun|{gun}|{sound}", clips, sampleSetup);
		}

		public AudioSetup GetFootstepSound(string surface, bool isRunning, AudioSetup sampleSetup)
		{
			FootstepSounds sounds = FindFootsteps(surface);
			AudioClip[] clips = sounds == null ? null : (isRunning == true && IsEmpty(sounds.Run) == false ? sounds.Run : sounds.Walk);

			return IsEmpty(clips) == true ? sampleSetup : GetSetup($"step|{surface}|{isRunning}", clips, sampleSetup);
		}

		public GunSounds FindGun(string gun)
		{
			for (int i = 0; Guns != null && i < Guns.Length; ++i)
			{
				if (Guns[i] != null && string.Equals(Guns[i].Gun, gun, StringComparison.OrdinalIgnoreCase) == true)
					return Guns[i];
			}
			return null;
		}

		public FootstepSounds FindFootsteps(string surface)
		{
			for (int i = 0; Footsteps != null && i < Footsteps.Length; ++i)
			{
				if (Footsteps[i] != null && string.Equals(Footsteps[i].Surface, surface, StringComparison.OrdinalIgnoreCase) == true)
					return Footsteps[i];
			}
			return null;
		}

		public static bool IsEmpty(AudioClip[] clips)
		{
			if (clips == null)
				return true;

			for (int i = 0; i < clips.Length; ++i)
			{
				if (clips[i] != null)
					return false;
			}

			return true;
		}

		// PRIVATE METHODS

		private AudioSetup GetSetup(string key, AudioClip[] clips, AudioSetup sampleSetup)
		{
			_setups ??= new Dictionary<string, AudioSetup>();

			if (_setups.TryGetValue(key, out AudioSetup setup) == false)
			{
				setup = new AudioSetup();
				if (sampleSetup != null)
				{
					setup.CopyFrom(sampleSetup);
				}

				setup.Clips = clips;
				_setups.Add(key, setup);
			}

			return setup;
		}

		private void OnValidate()
		{
			_setups = null;
		}
	}
}
