namespace TPSBR
{
	using System;
	using UnityEngine;

	/// <summary>Every rebindable action on the Controls page (phase 6b).</summary>
	public enum EBinding
	{
		MoveForward,
		MoveBack,
		MoveLeft,
		MoveRight,
		Sprint,
		Crouch,
		Jump,
		Fire,
		Aim,
		Reload,
		Primary,
		Secondary,
		Melee,
		Interact,
		PickUp,
		Heal,
		Equipment,
		Gear,
		Inventory,
		Map,
		Ping,
		StartMatch, // Phase 7: the host starts the match from the warm-up
	}

	/// <summary>
	/// Mouse and key numbers (phase 6b), from the HUD & UI and Controls pages. Lives in Resources/Settings/ControlsSettings.asset, linked
	/// from GlobalSettings.Controls (Brownout → Setup → Build HUD makes and links it). The players' own choices (sensitivity multipliers,
	/// hold or toggle, rebound keys) are saved per player on top of these defaults.
	/// </summary>
	[CreateAssetMenu(fileName = "ControlsSettings", menuName = "TPSBR/Controls Settings")]
	public sealed class ControlsSettings : ScriptableObject
	{
		[Serializable]
		public struct Binding
		{
			public EBinding Action;
			public string   Label;
			[Tooltip("Input System control path, e.g. <Keyboard>/w or <Mouse>/leftButton.")]
			public string   Path;
		}

		[Header("Mouse (defaults for a 1000 DPI mouse; the sliders multiply these)")]
		[Tooltip("Mouse travel for a full turn at the hip with the sensitivity slider at 1 (HUD & UI page: about 30 cm, a guess to tune).")]
		public float HipCentimetersPerTurn = 30.0f;
		[Tooltip("The mouse DPI the default is set for.")]
		public float ReferenceDpi          = 1000.0f;
		[Tooltip("ADS is scaled by zoom so aiming moves at the same speed on screen; the ADS slider multiplies that.")]
		public bool  ScaleAdsByZoom        = true;

		[Header("Keys (Controls page); every one can be rebound in the settings menu")]
		public Binding[] Bindings = DefaultBindings();

		// PUBLIC MEMBERS

		private static ControlsSettings _fallback;

		/// <summary>The asset linked from GlobalSettings. Falls back to the defaults above (with a warning) if it isn't linked yet.</summary>
		public static ControlsSettings Current
		{
			get
			{
				ControlsSettings settings = Global.Settings != null ? Global.Settings.Controls : null;
				if (settings != null)
					return settings;

				if (_fallback == null)
				{
					_fallback = CreateInstance<ControlsSettings>();
					Debug.LogWarning("ControlsSettings isn't linked in GlobalSettings; using the code defaults. Run Brownout → Setup → Build HUD.");
				}

				return _fallback;
			}
		}

		/// <summary>Degrees of turn per mouse count at the hip with the slider at 1.</summary>
		public float HipDegreesPerCount
		{
			get
			{
				float countsPerTurn = Mathf.Max(0.1f, HipCentimetersPerTurn) / 2.54f * Mathf.Max(1.0f, ReferenceDpi);
				return 360.0f / countsPerTurn;
			}
		}

		// PUBLIC METHODS

		public string GetDefaultPath(EBinding action)
		{
			for (int i = 0; Bindings != null && i < Bindings.Length; ++i)
			{
				if (Bindings[i].Action == action)
					return Bindings[i].Path;
			}

			Binding[] defaults = DefaultBindings();
			for (int i = 0; i < defaults.Length; ++i)
			{
				if (defaults[i].Action == action)
					return defaults[i].Path;
			}

			return string.Empty;
		}

		public string GetLabel(EBinding action)
		{
			for (int i = 0; Bindings != null && i < Bindings.Length; ++i)
			{
				if (Bindings[i].Action == action && string.IsNullOrEmpty(Bindings[i].Label) == false)
					return Bindings[i].Label;
			}

			// Actions added after the asset was made (StartMatch) use the code's label
			Binding[] defaults = DefaultBindings();
			for (int i = 0; i < defaults.Length; ++i)
			{
				if (defaults[i].Action == action)
					return defaults[i].Label;
			}

			return action.ToString();
		}

		/// <summary>The Controls page's keys.</summary>
		public static Binding[] DefaultBindings()
		{
			return new Binding[]
			{
				new Binding { Action = EBinding.MoveForward, Label = "Move forward",          Path = "<Keyboard>/w" },
				new Binding { Action = EBinding.MoveBack,    Label = "Move back",             Path = "<Keyboard>/s" },
				new Binding { Action = EBinding.MoveLeft,    Label = "Move left",             Path = "<Keyboard>/a" },
				new Binding { Action = EBinding.MoveRight,   Label = "Move right",            Path = "<Keyboard>/d" },
				new Binding { Action = EBinding.Sprint,      Label = "Sprint / hold breath",  Path = "<Keyboard>/leftShift" },
				new Binding { Action = EBinding.Crouch,      Label = "Crouch / slide",        Path = "<Keyboard>/c" },
				new Binding { Action = EBinding.Jump,        Label = "Jump / mantle",         Path = "<Keyboard>/space" },
				new Binding { Action = EBinding.Fire,        Label = "Shoot",                 Path = "<Mouse>/leftButton" },
				new Binding { Action = EBinding.Aim,         Label = "Aim down sights",       Path = "<Mouse>/rightButton" },
				new Binding { Action = EBinding.Reload,      Label = "Reload",                Path = "<Keyboard>/r" },
				new Binding { Action = EBinding.Primary,     Label = "Primary gun",           Path = "<Keyboard>/1" },
				new Binding { Action = EBinding.Secondary,   Label = "Secondary gun",         Path = "<Keyboard>/2" },
				new Binding { Action = EBinding.Melee,       Label = "Melee punch",           Path = "<Keyboard>/3" },
				new Binding { Action = EBinding.Interact,    Label = "Interact (equip, revive)", Path = "<Keyboard>/f" },
				new Binding { Action = EBinding.PickUp,      Label = "Pick up",               Path = "<Keyboard>/g" },
				new Binding { Action = EBinding.Heal,        Label = "Heal",                  Path = "<Keyboard>/q" },
				new Binding { Action = EBinding.Equipment,   Label = "Equipment",             Path = "<Keyboard>/e" },
				new Binding { Action = EBinding.Gear,        Label = "Gear on / off",         Path = "<Keyboard>/leftCtrl" },
				new Binding { Action = EBinding.Inventory,   Label = "Inventory",             Path = "<Keyboard>/tab" },
				new Binding { Action = EBinding.Map,         Label = "Full map",              Path = "<Keyboard>/m" },
				new Binding { Action = EBinding.Ping,        Label = "Ping",                  Path = "<Mouse>/middleButton" },
				new Binding { Action = EBinding.StartMatch,  Label = "Start match (host)",    Path = "<Keyboard>/enter" },
			};
		}
	}
}
