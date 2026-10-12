namespace TPSBR
{
	using System;
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.InputSystem;
	using UnityEngine.InputSystem.Controls;

	/// <summary>
	/// The player's key map (phase 6b, Controls page): each EBinding is one keyboard key or mouse button, defaulting to ControlsSettings and
	/// saved per player when rebound in the settings menu (PersistentStorage, "Controls.<action>"). Gameplay input (AgentInput), the Tab
	/// inventory and the M map read keys only through here.
	/// </summary>
	public static class KeyBindings
	{
		// CONSTANTS

		private const string PREFIX = "Controls.";

		// PUBLIC MEMBERS

		public static int Count => _count;

		/// <summary>Raised after a key is rebound or the keys are reset.</summary>
		public static event Action Changed;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>Test hook: these count as held (and pressed on the frame they're added), so checks can press keys without a focused Game view.</summary>
		public static readonly HashSet<EBinding> DebugHeld = new HashSet<EBinding>();
		private static readonly HashSet<EBinding> _debugHeldLastFrame = new HashSet<EBinding>();
		private static int _debugFrame = -1;
#endif

		// PRIVATE MEMBERS

		private static readonly int             _count    = Enum.GetValues(typeof(EBinding)).Length;
		private static readonly string[]        _paths    = new string[_count];
		private static readonly ButtonControl[] _controls = new ButtonControl[_count];
		private static bool                     _loaded;

		// PUBLIC METHODS

		public static string GetPath(EBinding action)
		{
			Load();
			return _paths[(int)action];
		}

		/// <summary>The key's name for the settings menu and the HUD's key hints ("F", "Left Shift", "Mouse Middle").</summary>
		public static string GetKeyName(EBinding action)
		{
			ButtonControl control = GetControl(action);
			return control != null ? GetDisplayName(control) : "-";
		}

		public static string GetDisplayName(InputControl control)
		{
			if (control == null)
				return "-";

			if (control.device is Mouse)
			{
				switch (control.name)
				{
					case "leftButton":    return "Mouse Left";
					case "rightButton":   return "Mouse Right";
					case "middleButton":  return "Mouse Middle";
					case "forwardButton": return "Mouse 5";
					case "backButton":    return "Mouse 4";
				}
			}

			return string.IsNullOrEmpty(control.displayName) == false ? control.displayName : control.name;
		}

		public static bool IsHeld(EBinding action)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (DebugHeld.Contains(action) == true)
				return true;
#endif
			ButtonControl control = GetControl(action);
			return control != null && control.isPressed == true;
		}

		public static bool WasPressedThisFrame(EBinding action)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			UpdateDebugFrame();
			if (DebugHeld.Contains(action) == true && _debugHeldLastFrame.Contains(action) == false)
				return true;
#endif
			ButtonControl control = GetControl(action);
			return control != null && control.wasPressedThisFrame == true;
		}

		/// <summary>Rebinds and saves. Another action on the same key is swapped onto this action's old key, so no key does two things.</summary>
		public static void SetPath(EBinding action, string path)
		{
			Load();

			int    index   = (int)action;
			string oldPath = _paths[index];

			for (int i = 0; i < _count; ++i)
			{
				if (i != index && string.Equals(_paths[i], path, StringComparison.OrdinalIgnoreCase) == true)
				{
					Store(i, oldPath);
				}
			}

			Store(index, path);
			PersistentStorage.Save();

			Changed?.Invoke();
		}

		/// <summary>Back to the Controls page's keys.</summary>
		public static void ResetAll()
		{
			ControlsSettings settings = ControlsSettings.Current;
			for (int i = 0; i < _count; ++i)
			{
				Store(i, settings.GetDefaultPath((EBinding)i));
			}

			PersistentStorage.Save();
			_loaded = true;

			Changed?.Invoke();
		}

		/// <summary>Every action's path at once, as saved (tests put the player's keys back with this).</summary>
		public static string[] GetAllPaths()
		{
			Load();
			return (string[])_paths.Clone();
		}

		/// <summary>Sets and saves every action's path at once, with no swapping (tests restore the player's keys with this).</summary>
		public static void SetAllPaths(string[] paths)
		{
			Load();
			for (int i = 0; i < _count && paths != null && i < paths.Length; ++i)
			{
				Store(i, paths[i]);
			}

			PersistentStorage.Save();
			Changed?.Invoke();
		}

		/// <summary>Re-reads the saved keys (after a profile change or a test).</summary>
		public static void Reload()
		{
			_loaded = false;
			Load();
			Changed?.Invoke();
		}

		/// <summary>The key or mouse button pressed this frame, for the settings menu's "press a key" (null when none).</summary>
		public static ButtonControl GetPressedControl()
		{
			Keyboard keyboard = Keyboard.current;
			if (keyboard != null)
			{
				var keys = keyboard.allKeys;
				for (int i = 0; i < keys.Count; ++i)
				{
					KeyControl key = keys[i];
					if (key != null && key.wasPressedThisFrame == true && key.keyCode != Key.Escape)
						return key;
				}
			}

			Mouse mouse = Mouse.current;
			if (mouse != null)
			{
				if (mouse.leftButton.wasPressedThisFrame    == true) return mouse.leftButton;
				if (mouse.rightButton.wasPressedThisFrame   == true) return mouse.rightButton;
				if (mouse.middleButton.wasPressedThisFrame  == true) return mouse.middleButton;
				if (mouse.forwardButton.wasPressedThisFrame == true) return mouse.forwardButton;
				if (mouse.backButton.wasPressedThisFrame    == true) return mouse.backButton;
			}

			return null;
		}

		/// <summary>The control path to save for a pressed control ("&lt;Keyboard&gt;/f", "&lt;Mouse&gt;/middleButton").</summary>
		public static string GetPathFor(InputControl control)
		{
			if (control == null)
				return string.Empty;

			string layout = control.device is Mouse ? "Mouse" : (control.device is Keyboard ? "Keyboard" : control.device.layout);
			return $"<{layout}>/{control.name}";
		}

		// PRIVATE METHODS

		private static void Load()
		{
			if (_loaded == true)
				return;

			_loaded = true;

			ControlsSettings settings = ControlsSettings.Current;
			for (int i = 0; i < _count; ++i)
			{
				string defaultPath = settings.GetDefaultPath((EBinding)i);
				_paths[i]    = PersistentStorage.GetString(PREFIX + (EBinding)i, defaultPath);
				_controls[i] = null;
			}
		}

		private static void Store(int index, string path)
		{
			_paths[index]    = path;
			_controls[index] = null;

			string defaultPath = ControlsSettings.Current.GetDefaultPath((EBinding)index);
			if (string.Equals(path, defaultPath, StringComparison.OrdinalIgnoreCase) == true)
			{
				PersistentStorage.Delete(PREFIX + (EBinding)index);
			}
			else
			{
				PersistentStorage.SetString(PREFIX + (EBinding)index, path, false);
			}
		}

		private static ButtonControl GetControl(EBinding action)
		{
			Load();

			int           index   = (int)action;
			ButtonControl control = _controls[index];

			// Found again when its device is gone (unplugged, or a new mouse or keyboard)
			if (control == null || control.device == null || control.device.added == false)
			{
				string path = _paths[index];
				control = string.IsNullOrEmpty(path) == false ? InputSystem.FindControl(path) as ButtonControl : null;
				_controls[index] = control;
			}

			return control;
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		private static readonly HashSet<EBinding> _debugHeldThisFrame = new HashSet<EBinding>();

		/// <summary>Keeps last frame's debug keys, so a newly added one reads as pressed for one frame.</summary>
		private static void UpdateDebugFrame()
		{
			if (_debugFrame == Time.frameCount)
				return;

			_debugFrame = Time.frameCount;
			_debugHeldLastFrame.Clear();
			foreach (EBinding held in _debugHeldThisFrame)
			{
				_debugHeldLastFrame.Add(held);
			}

			_debugHeldThisFrame.Clear();
			foreach (EBinding held in DebugHeld)
			{
				_debugHeldThisFrame.Add(held);
			}
		}
#endif
	}
}
