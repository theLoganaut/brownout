namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.InputSystem;

	/// <summary>
	/// Gun Bench keys (Phase 3c spec): , / . previous and next gun; F1–F5 cycle the optic, muzzle, underbarrel, magazine and
	/// stock; F8 cycles the grip (F7 is the Animation Arena's camera); F6 strips everything. Placed in the Gun Bench scene by Brownout → Setup → Build Gun Bench. In Multipeer every
	/// peer loads the scene, so only one copy handles each frame.
	/// </summary>
	public sealed class GunBenchKeys : MonoBehaviour
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		private static int _handledFrame = -1;

		private void Update()
		{
			if (_handledFrame == Time.frameCount)
				return;

			_handledFrame = Time.frameCount;

			Keyboard keyboard = Keyboard.current;
			if (keyboard == null)
				return;

			string result = null;

			if      (keyboard.commaKey.wasPressedThisFrame  == true) { result = GunBench.StepGun(-1); }
			else if (keyboard.periodKey.wasPressedThisFrame == true) { result = GunBench.StepGun(1); }
			else if (keyboard.f1Key.wasPressedThisFrame     == true) { result = GunBench.CycleSlot(EAttachmentSlot.Optic); }
			else if (keyboard.f2Key.wasPressedThisFrame     == true) { result = GunBench.CycleSlot(EAttachmentSlot.Muzzle); }
			else if (keyboard.f3Key.wasPressedThisFrame     == true) { result = GunBench.CycleSlot(EAttachmentSlot.Underbarrel); }
			else if (keyboard.f4Key.wasPressedThisFrame     == true) { result = GunBench.CycleSlot(EAttachmentSlot.Magazine); }
			else if (keyboard.f5Key.wasPressedThisFrame     == true) { result = GunBench.CycleSlot(EAttachmentSlot.Stock); }
			else if (keyboard.f8Key.wasPressedThisFrame     == true) { result = GunBench.CycleSlot(EAttachmentSlot.Grip); }
			else if (keyboard.f6Key.wasPressedThisFrame     == true) { result = GunBench.Strip(); }

			if (result != null)
			{
				Debug.Log($"Gun Bench: {result}");
			}
		}
#endif
	}
}
