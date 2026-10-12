using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;


namespace TPSBR
{
	/// <summary>
	/// Our 1 km map (phase 8, Map & POIs page). Written by Brownout → Setup → Build Brownout Map into the Brownout scene: the POI and
	/// cabin area boxes (full-map labels, test tools), the river's center line with its water heights (the river rule: anyone standing in
	/// the channel is put on the nearer bank) and the full-map grid. The map's square itself is the scene map's (SceneMap), which the map
	/// edge, helicopter and zone already read. Since 8b also every door and power prop, for MapState.
	/// </summary>
	public sealed class BrownoutMap : MonoBehaviour
	{
		[Serializable]
		public struct Area
		{
			public string  Id;
			public string  Name;
			[Tooltip("World X / Z of the box's center.")]
			public Vector2 Center;
			public Vector2 Size;
			public bool    IsCabins;
		}

		// PUBLIC MEMBERS

		public IReadOnlyList<Area> Areas         => _areas;
		public int                 GridCells     => _gridCells;
		public float               RiverHalfWidth => _riverHalfWidth;
		public IReadOnlyList<Vector2> RiverLine  => _riverLine;
		public IReadOnlyList<float>   RiverWater => _riverWater;
		public IReadOnlyList<Door>      Doors      => _doors;
		public IReadOnlyList<PowerProp> PowerProps => _powerProps;

		// PRIVATE MEMBERS

		[SerializeField]
		private Area[]    _areas = new Area[0];
		[SerializeField]
		private int       _gridCells = 10;
		[SerializeField, Tooltip("World X / Z points along the river's center line, a few meters apart.")]
		private Vector2[] _riverLine = new Vector2[0];
		[SerializeField, Tooltip("Water surface height at each point of the line.")]
		private float[]   _riverWater = new float[0];
		[SerializeField]
		private float     _riverHalfWidth = 2.0f;
		[SerializeField, Tooltip("Every door on the map (phase 8b), by index into MapState's door array.")]
		private Door[]      _doors = new Door[0];
		[SerializeField, Tooltip("Lights, camera and turret spots and the hum spot (phase 8b), switched by MapState with each POI's power.")]
		private PowerProp[] _powerProps = new PowerProp[0];
		[SerializeField, Tooltip("The bots' NavMesh (phase 9), baked by Build Brownout Map → NavMesh.")]
		private NavMeshData _navMesh;

		private static readonly List<BrownoutMap> _maps = new List<BrownoutMap>(4);

		// The NavMesh is one per process, while Multipeer loads the map once per peer: added by the first copy, removed with the last
		private static NavMeshDataInstance _navMeshInstance;
		private static int                 _navMeshUsers;

		// PUBLIC METHODS

		/// <summary>The map in a scene (each Multipeer peer has its own copy), or null.</summary>
		public static BrownoutMap Get(UnityEngine.SceneManagement.Scene scene)
		{
			for (int i = 0; i < _maps.Count; ++i)
			{
				if (_maps[i] != null && _maps[i].gameObject.scene == scene)
					return _maps[i];
			}

			return null;
		}

		/// <summary>Any loaded map (the HUD's grid and labels; every peer's copy is the same).</summary>
		public static BrownoutMap Any => _maps.Count > 0 ? _maps[0] : null;

		public bool TryGetArea(string id, out Area area)
		{
			for (int i = 0; i < _areas.Length; ++i)
			{
				if (string.Equals(_areas[i].Id, id, StringComparison.OrdinalIgnoreCase) == true || string.Equals(_areas[i].Name, id, StringComparison.OrdinalIgnoreCase) == true)
				{
					area = _areas[i];
					return true;
				}
			}

			area = default;
			return false;
		}

		/// <summary>Horizontal distance to the river's center line, the closest line point, its water height and the direction across.</summary>
		public float GetRiverDistance(Vector3 position, out Vector2 closest, out float water, out Vector2 across)
		{
			closest = default;
			water   = 0.0f;
			across  = Vector2.right;

			if (_riverLine.Length < 2)
				return float.MaxValue;

			Vector2 point = new Vector2(position.x, position.z);
			float   best  = float.MaxValue;

			for (int i = 0; i < _riverLine.Length - 1; ++i)
			{
				Vector2 a = _riverLine[i];
				Vector2 b = _riverLine[i + 1];
				Vector2 ab = b - a;
				float   length = ab.sqrMagnitude;
				float   t  = length > 0.0001f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / length) : 0.0f;
				Vector2 c  = a + ab * t;
				float   d  = (point - c).sqrMagnitude;

				if (d < best)
				{
					best    = d;
					closest = c;
					water   = Mathf.Lerp(_riverWater[i], _riverWater[i + 1], t);

					Vector2 side = (point - c).sqrMagnitude > 0.0001f ? (point - c).normalized : new Vector2(-ab.y, ab.x).normalized;
					across = side;
				}
			}

			return Mathf.Sqrt(best);
		}

		/// <summary>
		/// True when a position is in the river channel (between the bank walls, down at the water, so not on a bridge deck); bank is the
		/// point on the nearer bank to put them (height from the ground under it).
		/// </summary>
		public bool TryGetBankPoint(Vector3 position, float putDistance, out Vector3 bank)
		{
			bank = default;

			float distance = GetRiverDistance(position, out Vector2 closest, out float water, out Vector2 across);
			// In the channel you stand on the bed (under the water) or its steep side (just above it); a bridge deck is well above
			if (distance > _riverHalfWidth + 0.6f || position.y > water + 1.0f)
				return false;

			Vector2 target = closest + across * (_riverHalfWidth + putDistance);
			bank = new Vector3(target.x, water + 2.0f, target.y);
			return true;
		}

		// MONOBEHAVIOUR

		private void OnEnable()
		{
			if (_maps.Contains(this) == false)
			{
				_maps.Add(this);
			}

			if (_navMesh != null && Application.isPlaying == true)
			{
				if (_navMeshUsers == 0)
				{
					_navMeshInstance = NavMesh.AddNavMeshData(_navMesh);
				}

				++_navMeshUsers;
			}
		}

		private void OnDisable()
		{
			_maps.Remove(this);

			if (_navMesh != null && Application.isPlaying == true && _navMeshUsers > 0)
			{
				--_navMeshUsers;

				if (_navMeshUsers == 0)
				{
					_navMeshInstance.Remove();
				}
			}
		}

		private void OnDrawGizmosSelected()
		{
			Gizmos.color = new Color(1.0f, 0.8f, 0.2f);
			foreach (Area area in _areas)
			{
				Gizmos.DrawWireCube(new Vector3(area.Center.x, 60.0f, area.Center.y), new Vector3(area.Size.x, 120.0f, area.Size.y));
			}

			Gizmos.color = new Color(0.2f, 0.6f, 1.0f);
			for (int i = 0; i < _riverLine.Length - 1; ++i)
			{
				Gizmos.DrawLine(new Vector3(_riverLine[i].x, _riverWater[i], _riverLine[i].y), new Vector3(_riverLine[i + 1].x, _riverWater[i + 1], _riverLine[i + 1].y));
			}
		}

#if UNITY_EDITOR
		/// <summary>The map builder writes the data.</summary>
		public void EditorSet(Area[] areas, int gridCells, Vector2[] riverLine, float[] riverWater, float riverHalfWidth)
		{
			_areas          = areas;
			_gridCells      = gridCells;
			_riverLine      = riverLine;
			_riverWater     = riverWater;
			_riverHalfWidth = riverHalfWidth;
		}

		/// <summary>The map builder links the baked NavMesh.</summary>
		public void EditorSetNavMesh(NavMeshData navMesh)
		{
			_navMesh = navMesh;
		}

		public NavMeshData NavMeshAsset => _navMesh;

		/// <summary>The map builder lists the buildings' doors and power props.</summary>
		public void EditorSetBuildings(Door[] doors, PowerProp[] powerProps)
		{
			_doors      = doors;
			_powerProps = powerProps;
		}
#endif
	}
}
