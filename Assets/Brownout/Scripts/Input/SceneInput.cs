using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TPSBR
{
	[Flags]
	public enum ECursorStateSource
	{
		None,
		UI,
		Menu,
		Agent,
		/// <summary>The Tab inventory (phase 5a): the cursor shows, but the player can still move.</summary>
		Inventory = 8,
		/// <summary>The M full map (phase 6b): like the inventory, the cursor shows and the player can still move.</summary>
		Map       = 16,
		/// <summary>Phase 11b: the end-of-match summary (BACK TO MENU needs the cursor).</summary>
		Summary   = 32,
	}

	public class SceneInput : SceneService
	{
		// PUBLIC MEMBERS

		public bool IsCursorVisible => _cursorVisibilitySources != ECursorStateSource.None;
		/// <summary>The Tab inventory is open (phase 5a).</summary>
		public bool IsInventoryOpen => (_cursorVisibilitySources & ECursorStateSource.Inventory) != 0;
		/// <summary>The full map is open (phase 6b).</summary>
		public bool IsMapOpen => (_cursorVisibilitySources & ECursorStateSource.Map) != 0;
		/// <summary>The inventory or the full map is open: the mouse drives a cursor, movement keeps going (phase 6b).</summary>
		public bool IsOverlayOpen => (_cursorVisibilitySources & OVERLAYS) != 0;
		/// <summary>The cursor shows for something other than the inventory or the map (menus, dialogs): no gameplay input at all.</summary>
		public bool IsCursorVisibleOutsideInventory => (_cursorVisibilitySources & ~OVERLAYS) != ECursorStateSource.None;

		private const ECursorStateSource OVERLAYS = ECursorStateSource.Inventory | ECursorStateSource.Map;

		// PRIVATE MEMBERS

		private List<IBackHandler> _backHandlers = new List<IBackHandler>();
		private ECursorStateSource _cursorVisibilitySources;

		private bool _hasInput;

		// PUBLIC METHODS

		public void RequestCursorVisibility(bool isVisible, ECursorStateSource source, bool force = true)
		{
			if (source == ECursorStateSource.None)
				return;

			var previousSources = _cursorVisibilitySources;

			if (isVisible == true)
			{
				_cursorVisibilitySources = _cursorVisibilitySources | source;
			}
			else
			{
				_cursorVisibilitySources = _cursorVisibilitySources & ~source;
			}

			if (_cursorVisibilitySources != previousSources || force == true)
			{
				RefreshCursor();
			}
		}

		public void ClearCursorLock()
		{
			_cursorVisibilitySources = ECursorStateSource.None;
			RefreshCursor();
		}

		public void TrigggerBackAction()
		{
			BackAction();
		}

		// SceneService INTERFACE

		protected override void OnTick()
		{
			base.OnTick();

			if (Context.Runner != null)
			{
				if (ApplicationSettings.IsStrippedBatch == true && ApplicationSettings.GenerateInput == true)
				{
					Context.Runner.ProvideInput = true;
				}
				else
				{
					Context.Runner.ProvideInput = Context.HasInput;
				}
			}

			if (Context.HasInput == true || Scene is Menu)
			{
				if (Keyboard.current.escapeKey.wasPressedThisFrame == true)
				{
					BackAction();
				}
			}

			if (Context.HasInput == true)
			{
				bool toggleCursor = Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame;
#if !UNITY_EDITOR
				toggleCursor &= Keyboard.current.leftCtrlKey.isPressed;
#endif
				if (toggleCursor == true)
				{
					RequestCursorVisibility(IsCursorVisible == false, ECursorStateSource.Agent);
				}
			}

			if (_hasInput != Context.HasInput)
			{
				// Refresh cursor when input changed (e.g. when switching between peers)
				RefreshCursor();

				_hasInput = Context.HasInput;
			}
		}

		// PRIVATE METHODS

		private void BackAction()
		{
			// Phase 6b: Esc closes the inventory or the full map first (they follow the cursor state and close themselves)
			if (IsOverlayOpen == true)
			{
				RequestCursorVisibility(false, OVERLAYS);
				return;
			}

			if (_backHandlers.Count == 0)
			{
				Context.UI.GetAll(_backHandlers);
				_backHandlers.Add(Context.UI);

				_backHandlers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
			}

			for (int i = 0, count = _backHandlers.Count; i < count; ++i)
			{
				IBackHandler handler = _backHandlers[i];
				if (handler.IsActive == true && handler.OnBackAction() == true)
					break;
			}
		}

		private void RefreshCursor()
		{
			if (IsActive == false)
				return;

			if (Context != null && Context.HasInput == false)
				return;

			if (IsCursorVisible == true)
			{
				Cursor.lockState = CursorLockMode.None;
				Cursor.visible   = true;
			}
			else
			{
				Cursor.lockState = CursorLockMode.Locked;
				Cursor.visible = false;
			}
		}
	}
}
