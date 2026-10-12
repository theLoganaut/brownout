namespace TPSBR
{
	using System.Linq;
	using UnityEditor;
	using UnityEditor.SceneManagement;
	using UnityEngine;

	/// <summary>
	/// Loot Spots (phase 8, Logan 6 Oct): each POI's loot is several named spots, each a circle holding a number of loot points. Pick a POI
	/// and its spots show in the Scene view; drag a spot's center to move it and the dot on its edge to resize it, or type the numbers here.
	/// Add, rename or delete spots, change counts and the top-tier tick. Save writes References/map_loot.json and rebuilds only the loot
	/// points (Brownout → Setup → Build Brownout Map → Loot Spots). Use it outside Play mode with the Brownout scene open.
	/// </summary>
	public sealed class BrownoutLootSpotWindow : EditorWindow
	{
		// PRIVATE MEMBERS

		private BrownoutMapData.LootFile _file;
		private int     _areaIndex;
		private int     _selectedSpot = -1;
		private bool    _dirty;
		private Vector2 _scroll;
		private string  _status = string.Empty;

		// MENU

		[MenuItem("Brownout/Map/Loot Spots", priority = 1)]
		private static void Open()
		{
			GetWindow<BrownoutLootSpotWindow>("Loot Spots").Show();
		}

		// EDITORWINDOW

		private void OnEnable()
		{
			Load();
			SceneView.duringSceneGui -= OnSceneGUI;
			SceneView.duringSceneGui += OnSceneGUI;
		}

		private void OnDisable()
		{
			SceneView.duringSceneGui -= OnSceneGUI;
		}

		private void OnGUI()
		{
			if (_file == null || _file.areas.Count == 0)
			{
				EditorGUILayout.HelpBox($"Couldn't read {BrownoutMapData.LootPath}.", MessageType.Error);
				if (GUILayout.Button("Reload")) Load();
				return;
			}

			if (EditorSceneManager.GetActiveScene().path != BrownoutMapBuilder.ScenePath)
			{
				EditorGUILayout.HelpBox("Open the Brownout scene to see and drag the spots (Brownout → Setup → Build Brownout Map makes it).", MessageType.Info);
			}

			if (Application.isPlaying == true)
			{
				EditorGUILayout.HelpBox("Stop Play mode to save.", MessageType.Warning);
			}

			string[] names = _file.areas.Select(a => $"{a.name}  ({a.Total})").ToArray();
			int area = EditorGUILayout.Popup("POI", _areaIndex, names);
			if (area != _areaIndex)
			{
				_areaIndex    = area;
				_selectedSpot = -1;
				FrameArea();
			}

			BrownoutMapData.LootArea current = _file.areas[_areaIndex];
			EditorGUILayout.LabelField("Loot density", current.density);
			EditorGUILayout.LabelField("Loot points", $"{current.Total} in {current.spots.Count} spots");

			EditorGUILayout.Space();
			_scroll = EditorGUILayout.BeginScrollView(_scroll);

			for (int i = 0; i < current.spots.Count; ++i)
			{
				BrownoutMapData.LootSpot spot = current.spots[i];
				bool selected = i == _selectedSpot;

				EditorGUILayout.BeginVertical(selected == true ? EditorStyles.helpBox : GUIStyle.none);
				EditorGUI.BeginChangeCheck();

				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button(selected ? "●" : "○", GUILayout.Width(24)))
				{
					_selectedSpot = selected ? -1 : i;
					SceneView.RepaintAll();
				}
				spot.name = EditorGUILayout.TextField(spot.name);
				if (GUILayout.Button("Delete", GUILayout.Width(60)) && EditorUtility.DisplayDialog("Loot Spots", $"Delete '{spot.name}'?", "Delete", "Cancel"))
				{
					current.spots.RemoveAt(i);
					_dirty = true;
					_selectedSpot = -1;
					GUIUtility.ExitGUI();
				}
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.BeginHorizontal();
				float e = EditorGUILayout.FloatField("E", spot.at[0]);
				float n = EditorGUILayout.FloatField("N", spot.at[1]);
				EditorGUILayout.EndHorizontal();
				spot.at     = new[] { Mathf.Round(e), Mathf.Round(n) };
				spot.radius = Mathf.Max(1.0f, Mathf.Round(EditorGUILayout.FloatField("Radius (m)", spot.radius)));
				spot.count  = Mathf.Max(0, EditorGUILayout.IntField("Loot points", spot.count));
				spot.top_tier = EditorGUILayout.Toggle("Top tier", spot.top_tier);

				if (EditorGUI.EndChangeCheck() == true)
				{
					_dirty = true;
					SceneView.RepaintAll();
				}

				EditorGUILayout.EndVertical();
				EditorGUILayout.Space(4);
			}

			EditorGUILayout.EndScrollView();

			if (GUILayout.Button("Add spot"))
			{
				Vector2 at = current.spots.Count > 0 ? new Vector2(current.spots[0].at[0], current.spots[0].at[1]) + new Vector2(20.0f, 0.0f) : new Vector2(500.0f, 500.0f);
				current.spots.Add(new BrownoutMapData.LootSpot { name = $"Spot {current.spots.Count + 1}", at = new[] { at.x, at.y }, radius = 20.0f, count = 5 });
				_selectedSpot = current.spots.Count - 1;
				_dirty = true;
				SceneView.RepaintAll();
			}

			EditorGUILayout.Space();
			EditorGUILayout.BeginHorizontal();

			GUI.enabled = Application.isPlaying == false;
			if (GUILayout.Button(_dirty ? "Save and rebuild loot points *" : "Save and rebuild loot points", GUILayout.Height(28)))
			{
				Save();
			}
			GUI.enabled = true;

			if (GUILayout.Button("Reload file", GUILayout.Height(28), GUILayout.Width(100)))
			{
				if (_dirty == false || EditorUtility.DisplayDialog("Loot Spots", "Throw away unsaved changes?", "Reload", "Cancel"))
				{
					Load();
				}
			}

			if (GUILayout.Button("Frame", GUILayout.Height(28), GUILayout.Width(60)))
			{
				FrameArea();
			}

			EditorGUILayout.EndHorizontal();

			if (string.IsNullOrEmpty(_status) == false)
			{
				EditorGUILayout.HelpBox(_status, MessageType.None);
			}
		}

		// SCENE VIEW

		private void OnSceneGUI(SceneView view)
		{
			if (_file == null || _areaIndex >= _file.areas.Count || EditorSceneManager.GetActiveScene().path != BrownoutMapBuilder.ScenePath)
				return;

			Terrain terrain = Object.FindFirstObjectByType<Terrain>();
			BrownoutMapData.LootArea area = _file.areas[_areaIndex];

			for (int i = 0; i < area.spots.Count; ++i)
			{
				BrownoutMapData.LootSpot spot = area.spots[i];
				Vector2 world  = spot.World;
				float   height = terrain != null ? terrain.SampleHeight(new Vector3(world.x, 0.0f, world.y)) + terrain.transform.position.y : 0.0f;
				Vector3 center = new Vector3(world.x, height + 0.3f, world.y);
				bool    selected = i == _selectedSpot;

				Handles.color = spot.top_tier == true ? new Color(0.8f, 0.3f, 1.0f) : new Color(1.0f, 0.85f, 0.2f);
				Handles.DrawWireDisc(center, Vector3.up, spot.radius, selected ? 4.0f : 2.0f);
				Handles.Label(center + Vector3.up * 3.0f, $"{spot.name}\n{spot.count} points{(spot.top_tier ? ", top tier" : "")}", EditorStyles.whiteBoldLabel);

				float size = HandleUtility.GetHandleSize(center) * 0.12f;

				EditorGUI.BeginChangeCheck();
				Vector3 moved = Handles.Slider2D(center, Vector3.up, Vector3.right, Vector3.forward, size, Handles.CircleHandleCap, 0.0f);
				if (EditorGUI.EndChangeCheck() == true)
				{
					spot.World    = new Vector2(moved.x, moved.z);
					_selectedSpot = i;
					_dirty        = true;
					Repaint();
				}

				Vector3 edge = center + Vector3.right * spot.radius;
				EditorGUI.BeginChangeCheck();
				Vector3 resized = Handles.Slider(edge, Vector3.right, size * 0.8f, Handles.DotHandleCap, 0.0f);
				if (EditorGUI.EndChangeCheck() == true)
				{
					spot.radius   = Mathf.Max(1.0f, Mathf.Round(Mathf.Abs(resized.x - center.x)));
					_selectedSpot = i;
					_dirty        = true;
					Repaint();
				}
			}
		}

		// PRIVATE METHODS

		private void Load()
		{
			try
			{
				_file   = BrownoutMapData.LoadLoot();
				_dirty  = false;
				_status = $"Read {BrownoutMapData.LootPath}: {_file.areas.Sum(a => a.Total)} loot points in {_file.areas.Count} areas.";
			}
			catch (System.Exception exception)
			{
				_file   = null;
				_status = exception.Message;
			}

			_areaIndex    = Mathf.Clamp(_areaIndex, 0, _file != null ? Mathf.Max(0, _file.areas.Count - 1) : 0);
			_selectedSpot = -1;
			SceneView.RepaintAll();
		}

		private void Save()
		{
			BrownoutMapData.SaveLoot(_file);
			_dirty = false;

			if (EditorSceneManager.GetActiveScene().path == BrownoutMapBuilder.ScenePath)
			{
				string result = BrownoutMapBuilder.Run(BrownoutMapBuilder.BuildLoot);
				_status = $"Saved {BrownoutMapData.LootPath}.\n{result.Split('\n').FirstOrDefault(l => l.StartsWith("Loot:")) ?? result}";
				Debug.Log(result);
			}
			else
			{
				_status = $"Saved {BrownoutMapData.LootPath}. Open the Brownout scene and run Build Brownout Map → Loot Spots to place the points.";
			}
		}

		private void FrameArea()
		{
			if (_file == null || _areaIndex >= _file.areas.Count || SceneView.lastActiveSceneView == null)
				return;

			BrownoutMapData.LootArea area = _file.areas[_areaIndex];
			if (area.spots.Count == 0)
				return;

			var bounds = new Bounds(new Vector3(area.spots[0].World.x, 50.0f, area.spots[0].World.y), Vector3.zero);
			foreach (BrownoutMapData.LootSpot spot in area.spots)
			{
				bounds.Encapsulate(new Bounds(new Vector3(spot.World.x, 50.0f, spot.World.y), new Vector3(spot.radius * 2.0f, 20.0f, spot.radius * 2.0f)));
			}

			SceneView view = SceneView.lastActiveSceneView;
			view.LookAt(bounds.center, Quaternion.Euler(60.0f, 0.0f, 0.0f), bounds.extents.magnitude * 1.4f);
		}
	}
}
