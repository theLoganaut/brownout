using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// The Brownout map's networked state (phase 8b), one scene object for the whole map: every door's open state and swing direction in
	/// one array, and each POI's power. The server changes it (walking into a closed door, F on an open one, the power test tool); every
	/// peer animates the doors and switches the power props from it. Doors and props are listed on BrownoutMap by the map builder.
	/// </summary>
	public sealed class MapState : NetworkBehaviour
	{
		// CONSTANTS

		public const int MaxDoors = 512;
		public const int MaxAreas = 16;

		private const byte OpenBit     = 1;
		private const byte NegativeBit = 2;

		// PRIVATE MEMBERS

		[Networked, Capacity(MaxDoors)]
		private NetworkArray<byte>        _doors { get; }
		[Networked, Capacity(MaxAreas)]
		private NetworkArray<NetworkBool> _power { get; }

		private BrownoutMap _map;
		private readonly bool[] _appliedPower = new bool[MaxAreas];
		private bool            _powerApplied;

		private static readonly List<MapState> _states = new List<MapState>(4);

		// PUBLIC METHODS

		/// <summary>The map state in a scene (each Multipeer peer has its own), or null.</summary>
		public static MapState Get(UnityEngine.SceneManagement.Scene scene)
		{
			for (int i = 0; i < _states.Count; ++i)
			{
				if (_states[i] != null && _states[i].gameObject.scene == scene)
					return _states[i];
			}

			return null;
		}

		public bool IsDoorOpen(int index)
		{
			return index >= 0 && index < MaxDoors && (_doors[index] & OpenBit) != 0;
		}

		public bool IsDoorLocked(Door door)
		{
			return door.IsPower == true && IsPowered(door.Area) == false;
		}

		public bool IsPowered(int area)
		{
			return area >= 0 && area < MaxAreas && _power[area] == true;
		}

		/// <summary>Server: a player walked into a closed door; it swings open away from them. False when it's open, locked or not ours.</summary>
		public bool OpenDoor(Door door, Vector3 from, string who)
		{
			if (HasStateAuthority == false || door == null || door.Index >= MaxDoors)
				return false;
			if (IsDoorOpen(door.Index) == true || IsDoorLocked(door) == true)
				return false;

			int sign = door.GetSwingAwayFrom(from);
			_doors.Set(door.Index, (byte)(OpenBit | (sign < 0 ? NegativeBit : 0)));
			MatchDebug.Log(Runner, $"DOOR {who} opened door {door.Index} ({door.name}), swinging {(sign > 0 ? "+" : "-")}");
			return true;
		}

		/// <summary>Server: F on an open door closes it.</summary>
		public bool CloseDoor(Door door, string who)
		{
			if (HasStateAuthority == false || door == null || door.Index >= MaxDoors)
				return false;
			if (IsDoorOpen(door.Index) == false || IsDoorLocked(door) == true)
				return false;

			_doors.Set(door.Index, 0);
			MatchDebug.Log(Runner, $"DOOR {who} closed door {door.Index} ({door.name})");
			return true;
		}

		/// <summary>Server: switches a POI's power. With power its power doors open (and then work like any door); without it they shut and lock.</summary>
		public void SetPower(int area, bool powered)
		{
			if (HasStateAuthority == false || area < 0 || area >= MaxAreas || _map == null)
				return;

			_power.Set(area, powered);

			foreach (Door door in _map.Doors)
			{
				if (door != null && door.IsPower == true && door.Area == area && door.Index < MaxDoors)
				{
					_doors.Set(door.Index, powered == true ? OpenBit : (byte)0);
				}
			}

			MatchDebug.Log(Runner, $"POWER area {area} {(powered ? "on" : "off")}");
		}

		/// <summary>Server: every door shut (the match start).</summary>
		public void CloseAllDoors()
		{
			if (HasStateAuthority == false)
				return;

			for (int i = 0; i < MaxDoors; ++i)
			{
				if (_doors[i] != 0)
				{
					_doors.Set(i, 0);
				}
			}
		}

		// NetworkBehaviour INTERFACE

		public override void Spawned()
		{
			if (_states.Contains(this) == false)
			{
				_states.Add(this);
			}

			_map = BrownoutMap.Get(gameObject.scene);
			if (_map != null)
			{
				foreach (Door door in _map.Doors)
				{
					if (door != null)
					{
						door.SetState(this);
					}
				}
			}

			_powerApplied = false;
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			_states.Remove(this);
		}

		public override void Render()
		{
			if (_map == null)
				return;

			BrownoutMapSettings settings = BrownoutMapSettings.Current;
			float speed = settings.DoorOpenAngle / Mathf.Max(0.01f, settings.DoorSwingTime);
			float delta = Time.deltaTime;

			IReadOnlyList<Door> doors = _map.Doors;
			for (int i = 0; i < doors.Count; ++i)
			{
				Door door = doors[i];
				if (door == null || door.Index >= MaxDoors)
					continue;

				byte  state  = _doors[door.Index];
				float target = (state & OpenBit) == 0 ? 0.0f : ((state & NegativeBit) != 0 ? -settings.DoorOpenAngle : settings.DoorOpenAngle);
				door.UpdateSwing(target, speed, delta);
			}

			// Lights and other props follow their POI's power
			for (int area = 0; area < MaxAreas; ++area)
			{
				bool powered = _power[area];
				if (_powerApplied == true && _appliedPower[area] == powered)
					continue;

				_appliedPower[area] = powered;
				foreach (PowerProp prop in _map.PowerProps)
				{
					if (prop != null && prop.Area == area)
					{
						prop.SetPowered(powered);
					}
				}
			}

			_powerApplied = true;
		}
	}
}
