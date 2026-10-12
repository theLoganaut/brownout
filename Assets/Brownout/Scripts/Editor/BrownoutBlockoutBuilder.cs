namespace TPSBR
{
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using Fusion;
	using Newtonsoft.Json.Linq;
	using UnityEditor;
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.SceneManagement;

	/// <summary>
	/// Map v0 buildings (phase 8b): every POI and cabin area's blockout from References/map_blockout.json, built into the Brownout scene's
	/// "Buildings" section by Brownout → Setup → Build Brownout Map → Buildings (and Build All). Simple shapes only: buildings with walls,
	/// floors, stairs, roofs, doors (Door, networked through MapState) and window cutouts; blocks, cooling towers, dishes, rails, trains
	/// with walk-through cars, catwalks, vehicle shapes; the Power Plant's underground reached through power doors (terrain holes over
	/// its shafts); and the power placeholders (PowerProp: lights, camera, turret and hum spots). Sizes of doors and windows come from
	/// map_layout.json; numbers for the look and the door feel from BrownoutMapSettings.
	/// </summary>
	public static class BrownoutBlockoutBuilder
	{
		// CONSTANTS

		public const string BlockoutPath = "References/map_blockout.json";
		public const string StateName    = "BrownoutMapState";
		public const string MeshFolder   = "Assets/Brownout/Art/Map/Blockout";

		private static readonly string[] Walls = { "S", "N", "E", "W" };

		// FOOTPRINTS

		/// <summary>A rotated rectangle on the ground (world X / Z) that nature, spawns and the loot scatter keep clear of.</summary>
		public struct Footprint
		{
			public string  Name;
			public Vector2 Center;
			public Vector2 Half;
			public float   Yaw;

			public Footprint(string name, Vector2 center, Vector2 half, float yaw)
			{
				Name   = name;
				Center = center;
				Half   = half;
				Yaw    = yaw;
			}

			public bool Contains(Vector2 point, float margin)
			{
				Vector2 local = Rotate(point - Center, -Yaw);
				return Mathf.Abs(local.x) <= Half.x + margin && Mathf.Abs(local.y) <= Half.y + margin;
			}

			public Vector2 ToWorld(Vector2 local) => Center + Rotate(local, Yaw);
		}

		/// <summary>The footprints of everything in the blockout file, without building it (for parts rebuilt on their own).</summary>
		public static List<Footprint> LoadFootprints(BrownoutMapBuilder.Context c)
		{
			var footprints = new List<Footprint>();
			if (File.Exists(Path.Combine(BrownoutMapData.ProjectFolder, BlockoutPath)) == false)
				return footprints;

			var data = new Data(c);
			foreach (Placed placed in data.Pieces)
			{
				AddFootprints(data, placed, footprints);
			}

			return footprints;
		}

		// BUILD

		public static void Build(BrownoutMapBuilder.Context c)
		{
			if (File.Exists(Path.Combine(BrownoutMapData.ProjectFolder, BlockoutPath)) == false)
			{
				c.Log.Add($"Buildings: {BlockoutPath} not found, skipped.");
				return;
			}

			BrownoutMapBuilder.EnsureFolder(MeshFolder);

			var data    = new Data(c);
			var state   = new State(c, data);
			Transform section = c.Section("Buildings");

			var areaGroups = new Dictionary<string, Transform>();
			foreach (Placed placed in data.Pieces)
			{
				if (areaGroups.TryGetValue(placed.AreaId, out Transform group) == false)
				{
					group = new GameObject(placed.AreaName).transform;
					group.SetParent(section, false);
					areaGroups.Add(placed.AreaId, group);
				}

				state.Area = placed.Area;
				BuildWorldPiece(state, placed, group);
			}

			int drawn = CombineMeshes(state, areaGroups);
			CutTerrainHoles(state);
			c.Map.EditorSetBuildings(state.Doors.ToArray(), state.Props.ToArray());
			EditorUtility.SetDirty(c.Map);
			string networked = EnsureMapState(c);
			c.ResetFootprints(state.Footprints);

			CheckPlacement(state);

			int powerDoors = state.Doors.Count(t => t.IsPower == true);
			c.Log.Add($"Buildings: {state.Buildings} buildings in {areaGroups.Count} areas, {state.Doors.Count} doors ({powerDoors} power doors), {state.Windows} windows, " +
			          $"{state.Stairs} stair flights, {state.Props.Count} power props ({state.Props.Count(t => t.Kind == EPowerProp.Light)} lights), {state.Holes.Count} terrain holes over underground shafts; drawn as {drawn} merged meshes (boxes keep their colliders). {networked}");

			if (state.Doors.Count > MapState.MaxDoors)
			{
				c.Log.Add($"  WARNING: {state.Doors.Count} doors, but MapState holds {MapState.MaxDoors}; raise MapState.MaxDoors.");
			}

			if (state.Warnings.Count > 0)
			{
				c.Log.Add("Buildings to look at:\n" + string.Join("\n", state.Warnings.Select(t => "  " + t)));
			}
		}

		// DATA

		private sealed class Defaults
		{
			public float Wall        = 0.25f;
			public float Floor       = 0.3f;
			public float FloorHeight = 3.2f;
			public float StairWidth  = 1.2f;
			public float StairAngle  = 33.0f;
			public float FloorRaise  = 0.15f;
			public float DoorWidth   = 0.75f;
			public float DoorHeight  = 2.29f;
			public float WindowWidth = 0.75f;
			public float WindowHeight = 0.92f;
			public float Sill        = 1.0f;
		}

		private sealed class Placed
		{
			public JObject Data;
			public Vector2 Position;  // World X / Z
			public float   Yaw;
			public int     Area;
			public string  AreaId;
			public string  AreaName;
		}

		private sealed class Data
		{
			public readonly BrownoutMapBuilder.Context Context;
			public readonly Defaults     Defaults = new Defaults();
			public readonly List<Placed> Pieces   = new List<Placed>();

			private readonly JObject _templates;

			public Data(BrownoutMapBuilder.Context c)
			{
				Context = c;

				JObject root = JObject.Parse(File.ReadAllText(Path.Combine(BrownoutMapData.ProjectFolder, BlockoutPath)));
				JObject layout = JObject.Parse(File.ReadAllText(Path.Combine(BrownoutMapData.ProjectFolder, BrownoutMapData.LayoutPath)));

				if (root["defaults"] is JObject defaults)
				{
					Defaults.Wall        = F(defaults, "wall",         Defaults.Wall);
					Defaults.Floor       = F(defaults, "floor",        Defaults.Floor);
					Defaults.FloorHeight = F(defaults, "floor_height", Defaults.FloorHeight);
					Defaults.StairWidth  = F(defaults, "stair_width",  Defaults.StairWidth);
					Defaults.StairAngle  = F(defaults, "stair_angle",  Defaults.StairAngle);
					Defaults.FloorRaise  = F(defaults, "floor_raise",  Defaults.FloorRaise);
				}

				if (layout["doors"] is JObject doors)
				{
					Defaults.DoorWidth  = F(doors, "width",  Defaults.DoorWidth);
					Defaults.DoorHeight = F(doors, "height", Defaults.DoorHeight);
				}

				if (layout["windows"] is JObject windows)
				{
					Defaults.WindowWidth  = F(windows, "width",  Defaults.WindowWidth);
					Defaults.WindowHeight = F(windows, "height", Defaults.WindowHeight);
					Defaults.Sill         = F(windows, "sill",   Defaults.Sill);
				}

				_templates = root["templates"] as JObject ?? new JObject();

				List<BrownoutMapData.Box> areas = c.Layout.AllAreas().ToList();
				if (root["areas"] is JObject areaPieces)
				{
					foreach (JProperty area in areaPieces.Properties())
					{
						int index = areas.FindIndex(t => t.Id == area.Name);
						if (index < 0)
						{
							c.Log.Add($"  map_blockout.json: no area '{area.Name}' in map_layout.json, skipped.");
							continue;
						}

						foreach (JToken piece in area.Value)
						{
							Expand((JObject)piece, areas[index].Center, 0.0f, index, areas[index].Id, areas[index].Name, Pieces);
						}
					}
				}
			}

			/// <summary>Places a piece in its parent's frame; a template reference becomes the template's pieces (a one-piece template takes the reference's fields).</summary>
			public void Expand(JObject piece, Vector2 parent, float parentYaw, int area, string areaId, string areaName, List<Placed> output)
			{
				Vector2 at  = V2(piece["at"], Vector2.zero);
				float   yaw = parentYaw + F(piece, "rot", 0.0f);
				Vector2 position = parent + Rotate(at, parentYaw);

				string template = S(piece, "template", null);
				if (template != null && _templates[template] is JArray pieces)
				{
					foreach (JToken templatePiece in pieces)
					{
						JObject merged = pieces.Count == 1 ? Merge((JObject)templatePiece, piece) : (JObject)templatePiece;
						Expand(merged, position, yaw, area, areaId, areaName, output);
					}

					return;
				}

				if (template != null && piece["type"] == null)
				{
					Context.Log.Add($"  map_blockout.json: no template '{template}', skipped.");
					return;
				}

				output.Add(new Placed { Data = piece, Position = position, Yaw = yaw, Area = area, AreaId = areaId, AreaName = areaName });
			}

			/// <summary>Expands a template reference inside a building (local frame).</summary>
			public IEnumerable<(JObject Piece, Vector2 At, float Yaw)> ExpandLocal(JObject piece, Vector2 parent, float parentYaw)
			{
				var list = new List<Placed>();
				Expand(piece, parent, parentYaw, -1, string.Empty, string.Empty, list);
				return list.Select(t => (t.Data, t.Position, t.Yaw));
			}

			private static JObject Merge(JObject template, JObject reference)
			{
				var merged = (JObject)template.DeepClone();
				foreach (JProperty property in reference.Properties())
				{
					if (property.Name == "template" || property.Name == "at" || property.Name == "rot")
						continue;

					if ((property.Name == "inside" || property.Name == "doors" || property.Name == "windows") && merged[property.Name] is JArray existing && property.Value is JArray added)
					{
						foreach (JToken item in added) existing.Add(item.DeepClone());
						continue;
					}

					merged[property.Name] = property.Value.DeepClone();
				}

				return merged;
			}
		}

		private sealed class State
		{
			public readonly BrownoutMapBuilder.Context Context;
			public readonly BrownoutMapSettings        S;
			public readonly Data                       Data;
			public readonly Defaults                   D;
			public readonly List<Door>      Doors      = new List<Door>();
			public readonly List<PowerProp> Props      = new List<PowerProp>();
			public readonly List<Footprint> Footprints = new List<Footprint>();
			public readonly List<Footprint> Holes      = new List<Footprint>();
			public readonly List<string>    Warnings   = new List<string>();
			public readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

			public int Area;
			public int Buildings;
			public int Windows;
			public int Stairs;

			public State(BrownoutMapBuilder.Context c, Data data)
			{
				Context = c;
				S       = c.S;
				Data    = data;
				D       = data.Defaults;
			}

			public Material Mat(string colorName)
			{
				if (string.IsNullOrEmpty(colorName) == true)
				{
					colorName = "Cover";
				}

				if (Materials.TryGetValue(colorName, out Material material) == true)
					return material;

				var field = typeof(BrownoutMapSettings).GetField(colorName);
				Color color = field != null && field.FieldType == typeof(Color) ? (Color)field.GetValue(S) : S.Cover;

				if (colorName == "LampOn")
				{
					material = BrownoutMapBuilder.ColorMaterial("Blockout" + colorName, color, 0.2f);
					material.EnableKeyword("_EMISSION");
					material.SetColor("_EmissionColor", color * 2.0f);
					material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
					EditorUtility.SetDirty(material);
				}
				else
				{
					material = BrownoutMapBuilder.ColorMaterial("Blockout" + colorName, color, colorName == "Metal" || colorName == "Machine" ? 0.35f : 0.1f);
				}

				Materials.Add(colorName, material);
				return material;
			}
		}

		// PIECES

		private static void BuildWorldPiece(State s, Placed placed, Transform group)
		{
			JObject j    = placed.Data;
			string  type = S(j, "type", "block");

			if (type == "underground")
			{
				BuildUnderground(s, placed, group);
				return;
			}

			var footprints = new List<Footprint>();
			AddFootprints(s.Data, placed, footprints);
			s.Footprints.AddRange(footprints);

			// The piece's base: a building's floor sits over the highest ground under it; other pieces on the ground at their middle, buried to the lowest
			float minimum = float.MaxValue;
			float maximum = float.MinValue;
			foreach (Footprint footprint in footprints)
			{
				GroundRange(s.Context, footprint, out float low, out float high);
				minimum = Mathf.Min(minimum, low);
				maximum = Mathf.Max(maximum, high);
			}

			float ground = s.Context.GroundHeight(placed.Position);
			if (minimum == float.MaxValue)
			{
				minimum = ground;
				maximum = ground;
			}

			float baseY = type == "building" ? maximum + s.D.FloorRaise : ground + F(j, "y", 0.0f);
			float sink  = Mathf.Max(0.0f, baseY - minimum) + 0.3f;

			var root = new GameObject(S(j, "name", Title(type))).transform;
			root.SetParent(group, false);
			root.SetPositionAndRotation(new Vector3(placed.Position.x, baseY, placed.Position.y), Quaternion.Euler(0.0f, placed.Yaw, 0.0f));

			BuildPiece(s, j, root, sink, true);
		}

		/// <summary>Builds one piece in its own frame (y 0 = its base). Sink: how far below the base to bury it on uneven ground.</summary>
		private static void BuildPiece(State s, JObject j, Transform root, float sink, bool onGround)
		{
			switch (S(j, "type", "block"))
			{
				case "building": BuildBuilding(s, j, root, sink, onGround); break;
				case "block":    BuildBlock(s, j, root, sink);    break;
				case "cylinder": BuildCylinder(s, j, root, sink); break;
				case "tower":    BuildTower(s, j, root, sink);    break;
				case "dish":     BuildDish(s, j, root, sink);     break;
				case "rails":    BuildRails(s, j, root);          break;
				case "train":    BuildTrain(s, j, root);          break;
				case "catwalk":  BuildCatwalk(s, j, root);        break;
				case "vehicle":  BuildVehicle(s, j, root);        break;
				case "light":    BuildLight(s, root, Vector3.zero, onGround == true); break;
				case "camera":   BuildCamera(s, root);            break;
				case "turret":   BuildTurret(s, root, sink);      break;
				case "hum":      AddProp(s, root.gameObject, EPowerProp.Hum, null, null); break;
				default:
					s.Warnings.Add($"Unknown piece type '{S(j, "type", "")}' ({root.name}).");
					break;
			}
		}

		/// <summary>Pieces inside a building or the underground, on a floor of it (y = floor * floorHeight in its frame).</summary>
		private static void BuildInside(State s, JObject j, Transform root, float floorHeight)
		{
			if (j["inside"] is JArray inside == false)
				return;

			foreach (JToken token in inside)
			{
				foreach ((JObject piece, Vector2 at, float yaw) in s.Data.ExpandLocal((JObject)token, Vector2.zero, 0.0f))
				{
					int floor = I(piece, "floor", 0);
					var child = new GameObject(S(piece, "name", Title(S(piece, "type", "block")))).transform;
					child.SetParent(root, false);
					child.localPosition = new Vector3(at.x, floor * floorHeight + F(piece, "y", 0.0f), at.y);
					child.localRotation = Quaternion.Euler(0.0f, yaw, 0.0f);
					BuildPiece(s, piece, child, 0.0f, false);
				}
			}
		}

		// BUILDING

		private struct Opening
		{
			public string Wall;
			public float  At;
			public int    Floor;
			public float  Width;
			public float  Height;
			public float  Sill;
			public bool   IsDoor;
			public bool   IsOpen;
			public bool   IsPower;
		}

		private struct StairPlan
		{
			public bool  Has;
			public bool  AlongZ;   // Lanes run along local Z (against the E or W wall) or X (S or N wall)
			public float LaneA;
			public float LaneB;
			public float Start;
			public float End;
			public float Width;
		}

		private static void BuildBuilding(State s, JObject j, Transform root, float sink, bool onGround)
		{
			Defaults d = s.D;
			Vector2 size   = V2(j["size"], new Vector2(8.0f, 6.0f));
			float   w      = size.x;
			float   depth  = size.y;
			int     floors = Mathf.Max(1, I(j, "floors", 1));
			float   fh     = F(j, "floor_height", d.FloorHeight);
			float   t      = d.Wall;
			float   ft     = d.Floor;
			string  roof   = S(j, "roof", "flat");
			bool    roofAccess = B(j, "roof_access") && roof != "gable";
			float   parapet    = F(j, "parapet", 0.0f);

			Material wallMat  = s.Mat(S(j, "color", "Wall"));
			Material floorMat = s.Mat("Floor");
			Material roofMat  = s.Mat("Roof");

			++s.Buildings;

			// Ground floor: a plinth down into the ground
			Box(root, "Ground floor", new Vector3(0.0f, -sink * 0.5f, 0.0f), new Vector3(w, sink, depth), floorMat);

			// Stairs: switchback flights in two lanes against one wall, a hole in each floor over the lane that arrives there
			int flights = floors - 1 + (roofAccess == true ? 1 : 0);
			StairPlan stairs = PlanStairs(s, j, root.name, w, depth, fh, flights);
			var holes = new List<Rect>();
			for (int f = 0; f < flights && stairs.Has == true; ++f)
			{
				bool  even  = f % 2 == 0;
				float lane  = even == true ? stairs.LaneA : stairs.LaneB;
				float from  = even == true ? stairs.Start : stairs.End;
				float to    = even == true ? stairs.End   : stairs.Start;
				float yFrom = f * fh;
				float yTo   = (f + 1) * fh + (f + 1 == floors ? ft : 0.0f);

				Vector3 a = stairs.AlongZ == true ? new Vector3(lane, yFrom, from) : new Vector3(from, yFrom, lane);
				Vector3 b = stairs.AlongZ == true ? new Vector3(lane, yTo,   to)   : new Vector3(to,   yTo,   lane);
				SlopedBox(root, $"Stairs {f + 1}", a, b, stairs.Width - 0.1f, 0.3f, floorMat);
				++s.Stairs;

				float half = stairs.Width * 0.5f;
				float lo   = Mathf.Min(stairs.Start, stairs.End);
				float hi   = Mathf.Max(stairs.Start, stairs.End);
				holes.Add(stairs.AlongZ == true ? Rect.MinMaxRect(lane - half, lo, lane + half, hi) : Rect.MinMaxRect(lo, lane - half, hi, lane + half));
			}

			Rect outer = Rect.MinMaxRect(-w * 0.5f, -depth * 0.5f, w * 0.5f, depth * 0.5f);

			// Upper floors (a hole over the flight that arrives at each)
			for (int k = 1; k < floors; ++k)
			{
				var floorHoles = k - 1 < holes.Count ? new List<Rect> { holes[k - 1] } : new List<Rect>();
				Slab(root, $"Floor {k}", k * fh, ft, outer, floorHoles, floorMat);
			}

			// Roof
			float top = floors * fh;
			if (roof == "gable")
			{
				float overhang = 0.35f;
				float rise     = (depth * 0.5f + overhang) * Mathf.Tan(25.0f * Mathf.Deg2Rad);
				Slab(root, "Ceiling", top, 0.12f, outer, new List<Rect>(), roofMat);
				var prism = MeshObject(root, "Roof", PrismMesh(w + overhang * 2.0f, depth + overhang * 2.0f, rise), roofMat, true);
				prism.transform.localPosition = new Vector3(0.0f, top, 0.0f);
			}
			else if (roof != "none")
			{
				var roofHoles = roofAccess == true && flights - 1 < holes.Count ? new List<Rect> { holes[flights - 1] } : new List<Rect>();
				Slab(root, "Roof", top + ft, ft, outer, roofHoles, roofMat);

				if (parapet > 0.0f)
				{
					float y = top + ft + parapet * 0.5f;
					Box(root, "Parapet S", new Vector3(0.0f, y, -depth * 0.5f + t * 0.5f), new Vector3(w, parapet, t), wallMat);
					Box(root, "Parapet N", new Vector3(0.0f, y,  depth * 0.5f - t * 0.5f), new Vector3(w, parapet, t), wallMat);
					Box(root, "Parapet W", new Vector3(-w * 0.5f + t * 0.5f, y, 0.0f), new Vector3(t, parapet, depth - 2.0f * t), wallMat);
					Box(root, "Parapet E", new Vector3( w * 0.5f - t * 0.5f, y, 0.0f), new Vector3(t, parapet, depth - 2.0f * t), wallMat);
				}
			}

			// Walls with their doors and windows
			List<Opening> openings = ReadOpenings(s, j, w, depth, floors);
			for (int k = 0; k < floors; ++k)
			{
				foreach (string wall in Walls)
				{
					BuildWall(s, root, wall, w, depth, k * fh, fh, openings.Where(o => o.Floor == k && o.Wall == wall).ToList(), wallMat);
				}
			}

			foreach (Opening opening in openings)
			{
				if (opening.IsDoor == true && opening.IsOpen == false)
				{
					MakeDoor(s, root, opening, w, depth, fh);
				}

				if (opening.IsDoor == true && opening.Floor == 0 && onGround == true)
				{
					MakeDoorStep(s, root, opening, w, depth);
				}

				if (opening.IsDoor == false)
				{
					++s.Windows;
				}
			}

			// Ceiling lights (power props), along the building's long side on each floor
			int lights = I(j, "lights", 0);
			for (int k = 0; k < floors && lights > 0; ++k)
			{
				float ceiling = (k + 1) * fh - (k + 1 < floors ? ft : 0.0f) - 0.08f;
				for (int i = 0; i < lights; ++i)
				{
					float u = (i + 0.5f) / lights - 0.5f;
					Vector3 at = w >= depth ? new Vector3(u * (w - 2.0f), ceiling, depth * 0.15f) : new Vector3(-w * 0.15f, ceiling, u * (depth - 2.0f));
					BuildLight(s, root, at, false);
				}
			}

			BuildInside(s, j, root, fh);
		}

		private static StairPlan PlanStairs(State s, JObject j, string name, float w, float depth, float fh, int flights)
		{
			var plan = new StairPlan();
			if (flights <= 0)
				return plan;

			JObject stairs = j["stairs"] as JObject ?? new JObject();
			string  wall   = S(stairs, "wall", "E");
			float   at     = F(stairs, "at", 0.0f);
			float   sw     = s.D.StairWidth;
			float   t      = s.D.Wall;
			float   run    = fh / Mathf.Tan(s.D.StairAngle * Mathf.Deg2Rad);

			plan.Has    = true;
			plan.Width  = sw;
			plan.AlongZ = wall == "E" || wall == "W";
			plan.Start  = at - run * 0.5f;
			plan.End    = at + run * 0.5f;

			float across = plan.AlongZ == true ? w : depth;
			float along  = plan.AlongZ == true ? depth : w;
			float sign   = wall == "E" || wall == "N" ? 1.0f : -1.0f;
			plan.LaneA = sign * (across * 0.5f - t - sw * 0.5f);
			plan.LaneB = plan.LaneA - sign * sw;

			if (Mathf.Abs(at) + run * 0.5f + sw > along * 0.5f - t + 0.01f || sw * 2.0f > across - 2.0f * t)
			{
				s.Warnings.Add($"{name}: the stairs (run {run:F1} m plus a {sw:F1} m landing at each end, two {sw:F1} m lanes) don't fit along the {wall} wall.");
			}

			return plan;
		}

		private static List<Opening> ReadOpenings(State s, JObject j, float w, float depth, int floors)
		{
			Defaults d = s.D;
			var openings = new List<Opening>();

			if (j["doors"] is JArray doors)
			{
				foreach (JObject door in doors)
				{
					openings.Add(new Opening
					{
						Wall    = S(door, "wall", "S"),
						At      = F(door, "at", 0.0f),
						Floor   = I(door, "floor", 0),
						Width   = F(door, "width", d.DoorWidth),
						Height  = F(door, "height", d.DoorHeight),
						IsDoor  = true,
						IsOpen  = B(door, "open"),
						IsPower = B(door, "power"),
					});
				}
			}

			if (j["windows"] is JArray windows)
			{
				foreach (JObject window in windows)
				{
					openings.Add(new Opening
					{
						Wall   = S(window, "wall", "S"),
						At     = F(window, "at", 0.0f),
						Floor  = I(window, "floor", 0),
						Width  = F(window, "width", d.WindowWidth),
						Height = F(window, "height", d.WindowHeight),
						Sill   = F(window, "sill", d.Sill),
					});
				}
			}

			// Windows every so many meters along each wall, on every floor, kept clear of the other openings
			float every = F(j, "window_every", 0.0f);
			if (every > 0.5f)
			{
				for (int k = 0; k < floors; ++k)
				{
					foreach (string wall in Walls)
					{
						float length = wall == "S" || wall == "N" ? w : depth - 2.0f * d.Wall;
						int   count  = Mathf.FloorToInt((length - 1.0f) / every);
						for (int i = 0; i < count; ++i)
						{
							float at = (i + 0.5f) * (length - 1.0f) / count - (length - 1.0f) * 0.5f;
							bool clear = openings.Where(o => o.Wall == wall && o.Floor == k).All(o => Mathf.Abs(o.At - at) > (o.Width + d.WindowWidth) * 0.5f + 0.5f);
							if (clear == true)
							{
								openings.Add(new Opening { Wall = wall, At = at, Floor = k, Width = d.WindowWidth, Height = d.WindowHeight, Sill = d.Sill });
							}
						}
					}
				}
			}

			return openings;
		}

		/// <summary>One wall of one floor, cut around its doors and windows. Walls S and N run the full width; E and W fit between them.</summary>
		private static void BuildWall(State s, Transform root, string wall, float w, float depth, float yBase, float height, List<Opening> openings, Material material)
		{
			float t      = s.D.Wall;
			bool  alongX = wall == "S" || wall == "N";
			float length = alongX == true ? w : depth - 2.0f * t;
			float offset = alongX == true ? (wall == "N" ? 1.0f : -1.0f) * (depth * 0.5f - t * 0.5f) : (wall == "E" ? 1.0f : -1.0f) * (w * 0.5f - t * 0.5f);

			void Piece(string name, float a0, float a1, float y0, float y1)
			{
				if (a1 - a0 < 0.01f || y1 - y0 < 0.01f)
					return;

				float   middle = (a0 + a1) * 0.5f;
				Vector3 center = alongX == true ? new Vector3(middle, yBase + (y0 + y1) * 0.5f, offset) : new Vector3(offset, yBase + (y0 + y1) * 0.5f, middle);
				Vector3 size   = alongX == true ? new Vector3(a1 - a0, y1 - y0, t) : new Vector3(t, y1 - y0, a1 - a0);
				Box(root, $"Wall {wall} {name}", center, size, material);
			}

			float cursor = -length * 0.5f;
			foreach (Opening opening in openings.OrderBy(o => o.At))
			{
				float a0 = Mathf.Max(cursor, opening.At - opening.Width * 0.5f);
				float a1 = Mathf.Min(length * 0.5f, opening.At + opening.Width * 0.5f);
				if (a1 <= a0)
				{
					s.Warnings.Add($"{root.name}: an opening on wall {wall} at {opening.At:F1} m overlaps another or the wall's end.");
					continue;
				}

				Piece("", cursor, a0, 0.0f, height);
				Piece("below", a0, a1, 0.0f, opening.Sill);
				Piece("above", a0, a1, Mathf.Min(height, opening.Sill + opening.Height), height);
				cursor = a1;
			}

			Piece("", cursor, length * 0.5f, 0.0f, height);
		}

		/// <summary>A door leaf on a hinge at the opening's left jamb, centered in the wall; the Door component networks its swing.</summary>
		private static void MakeDoor(State s, Transform root, Opening opening, float w, float depth, float fh)
		{
			float   t      = s.D.Wall;
			bool    alongX = opening.Wall == "S" || opening.Wall == "N";
			float   offset = alongX == true ? (opening.Wall == "N" ? 1.0f : -1.0f) * (depth * 0.5f - t * 0.5f) : (opening.Wall == "E" ? 1.0f : -1.0f) * (w * 0.5f - t * 0.5f);
			float   hinge  = opening.At - opening.Width * 0.5f;
			Vector3 at     = alongX == true ? new Vector3(hinge, opening.Floor * fh, offset) : new Vector3(offset, opening.Floor * fh, hinge);

			AddDoor(s, root, $"Door {opening.Wall}{(opening.IsPower ? " (power)" : string.Empty)}", at, Quaternion.Euler(0.0f, alongX == true ? 0.0f : -90.0f, 0.0f), opening.Width, opening.Height, opening.IsPower);
		}

		private static void AddDoor(State s, Transform parent, string name, Vector3 localHinge, Quaternion localRotation, float width, float height, bool isPower)
		{
			var pivot = new GameObject(name);
			pivot.transform.SetParent(parent, false);
			pivot.transform.localPosition = localHinge;
			pivot.transform.localRotation = localRotation;

			var body = pivot.AddComponent<Rigidbody>();
			body.isKinematic = true;
			body.useGravity  = false;

			GameObject leaf = Box(pivot.transform, "Leaf", new Vector3(width * 0.5f, height * 0.5f, 0.0f), new Vector3(width - 0.04f, height - 0.03f, 0.06f), s.Mat(isPower == true ? "PowerDoor" : "DoorLeaf"));
			leaf.isStatic = false;

			Door door = pivot.AddComponent<Door>();
			door.EditorSet(s.Doors.Count, s.Area, isPower, width, height);
			s.Doors.Add(door);
		}

		/// <summary>A ramp outside a ground-floor door when the floor is above the ground there.</summary>
		private static void MakeDoorStep(State s, Transform root, Opening opening, float w, float depth)
		{
			bool    alongX  = opening.Wall == "S" || opening.Wall == "N";
			float   sign    = opening.Wall == "N" || opening.Wall == "E" ? 1.0f : -1.0f;
			Vector3 outward = alongX == true ? new Vector3(0.0f, 0.0f, sign) : new Vector3(sign, 0.0f, 0.0f);
			Vector3 face    = alongX == true ? new Vector3(opening.At, 0.0f, sign * depth * 0.5f) : new Vector3(sign * w * 0.5f, 0.0f, opening.At);

			Vector3 outside = root.TransformPoint(face + outward * 1.2f);
			float   drop    = root.position.y - s.Context.GroundHeight(new Vector2(outside.x, outside.z));
			if (drop < s.S.DoorStepMinimum)
				return;

			float length = Mathf.Max(1.0f, drop / Mathf.Tan(s.S.DoorRampAngle * Mathf.Deg2Rad));
			SlopedBox(root, $"Door ramp {opening.Wall}", face + Vector3.up * 0.001f, face + outward * length + Vector3.down * (drop + 0.15f), opening.Width + 0.8f, 0.4f, s.Mat("Floor"));
		}

		// UNDERGROUND

		/// <summary>
		/// A room under the ground (floor 'depth' under the surface), reached by ramps down shafts from entrance huts on the surface, each
		/// behind a power door. Terrain holes open the ground over the shafts; the huts' floors cover the rest.
		/// </summary>
		private static void BuildUnderground(State s, Placed placed, Transform group)
		{
			BrownoutMapBuilder.Context c = s.Context;
			JObject j      = placed.Data;
			Vector2 size   = V2(j["size"], new Vector2(40.0f, 30.0f));
			float   depth  = F(j, "depth", 8.0f);
			float   height = F(j, "height", 4.5f);
			float   t      = s.D.Wall;
			float   ft     = s.D.Floor;

			float surface = c.GroundHeight(placed.Position);
			float floorY  = surface - depth;

			var root = new GameObject(S(j, "name", "Underground")).transform;
			root.SetParent(group, false);
			root.SetPositionAndRotation(new Vector3(placed.Position.x, floorY, placed.Position.y), Quaternion.Euler(0.0f, placed.Yaw, 0.0f));

			Material wall  = s.Mat(S(j, "color", "Wall"));
			Material floor = s.Mat("Floor");

			float w = size.x;
			float d = size.y;
			Box(root.transform, "Floor", new Vector3(0.0f, -ft * 0.5f, 0.0f), new Vector3(w + 2.0f * t, ft, d + 2.0f * t), floor);
			Box(root.transform, "Wall S", new Vector3(0.0f, height * 0.5f, -d * 0.5f - t * 0.5f), new Vector3(w + 2.0f * t, height, t), wall);
			Box(root.transform, "Wall N", new Vector3(0.0f, height * 0.5f,  d * 0.5f + t * 0.5f), new Vector3(w + 2.0f * t, height, t), wall);
			Box(root.transform, "Wall W", new Vector3(-w * 0.5f - t * 0.5f, height * 0.5f, 0.0f), new Vector3(t, height, d), wall);
			Box(root.transform, "Wall E", new Vector3( w * 0.5f + t * 0.5f, height * 0.5f, 0.0f), new Vector3(t, height, d), wall);

			float ceilingTop = floorY + height + ft;
			var   ceilingHoles = new List<Rect>();

			if (j["entrances"] is JArray entrances)
			{
				int number = 0;
				foreach (JObject entrance in entrances)
				{
					++number;
					Vector2 at  = V2(entrance["at"], Vector2.zero);
					float   rot = F(entrance, "rot", 0.0f);
					Rect    shaft = BuildEntrance(s, root, placed, at, rot, floorY, ceilingTop, number, out bool inside);
					ceilingHoles.Add(shaft);
					if (inside == false)
					{
						s.Warnings.Add($"{root.name}: entrance {number}'s ramp ends outside the room ({w:F0} × {d:F0} m).");
					}
				}
			}

			Slab(root.transform, "Ceiling", height + ft, ft, Rect.MinMaxRect(-w * 0.5f - t, -d * 0.5f - t, w * 0.5f + t, d * 0.5f + t), ceilingHoles, wall);

			int lights = I(j, "lights", 0);
			int columns = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(lights * w / Mathf.Max(1.0f, d))));
			for (int i = 0; i < lights; ++i)
			{
				int   rows = Mathf.CeilToInt(lights / (float)columns);
				float u    = ((i % columns) + 0.5f) / columns - 0.5f;
				float v    = ((i / columns) + 0.5f) / rows - 0.5f;
				BuildLight(s, root.transform, new Vector3(u * w * 0.8f, height - 0.08f, v * d * 0.8f), false);
			}

			BuildInside(s, j, root.transform, height);
		}

		/// <summary>An entrance hut over a shaft with a ramp down into the room; returns the shaft's rectangle in the room's frame.</summary>
		private static Rect BuildEntrance(State s, Transform room, Placed placed, Vector2 at, float rot, float floorY, float ceilingTop, int number, out bool rampInside)
		{
			BrownoutMapBuilder.Context c = s.Context;
			float t  = s.D.Wall;
			float ft = s.D.Floor;
			const float shaftWidth = 2.0f;
			const float landing    = 1.2f;
			const float side       = 1.7f;
			const float rampAngle  = 30.0f;

			// Hut frame: origin at the inside face of the door wall, +Z down the shaft
			float   yaw    = placed.Yaw + rot;
			Vector2 origin = placed.Position + Rotate(at, placed.Yaw);

			// The hut's floor over the ground there; the ramp's length from the drop
			Footprint estimate = new Footprint("hut", origin + Rotate(new Vector2(0.0f, 8.0f), yaw), new Vector2(shaftWidth * 0.5f + side + t, 9.0f), yaw);
			GroundRange(c, estimate, out float low, out float high);
			float hutFloor = high + s.D.FloorRaise;
			float drop     = hutFloor - floorY;
			float run      = drop / Mathf.Tan(rampAngle * Mathf.Deg2Rad);
			float shaftEnd = landing + run + 0.6f;
			float hutEnd   = shaftEnd + side;
			float halfX    = shaftWidth * 0.5f + side;

			var hut = new GameObject($"Entrance {number}").transform;
			hut.SetParent(room.parent, false);
			hut.SetPositionAndRotation(new Vector3(origin.x, hutFloor, origin.y), Quaternion.Euler(0.0f, yaw, 0.0f));

			Material wall  = s.Mat("Wall");
			Material floor = s.Mat("Floor");
			Material roof  = s.Mat("Roof");

			Rect shaft = Rect.MinMaxRect(-shaftWidth * 0.5f, landing, shaftWidth * 0.5f, shaftEnd);
			Rect outer = Rect.MinMaxRect(-halfX - t, -t, halfX + t, hutEnd + t);

			// Floor and plinth around the shaft, walls with the power door, a roof
			float sink = hutFloor - (low - 0.6f);
			Slab(hut, "Floor", 0.0f, sink, outer, new List<Rect> { shaft }, floor);

			const float hutHeight = 3.0f;
			float centerZ = (outer.yMin + outer.yMax) * 0.5f;
			float doorSide = (halfX + t + s.D.DoorWidth * 0.5f) * 0.5f;   // Middle of the wall on each side of the door
			Box(hut, "Wall S", new Vector3(-doorSide, hutHeight * 0.5f, -t * 0.5f), new Vector3(halfX + t - s.D.DoorWidth * 0.5f, hutHeight, t), wall);
			Box(hut, "Wall S", new Vector3( doorSide, hutHeight * 0.5f, -t * 0.5f), new Vector3(halfX + t - s.D.DoorWidth * 0.5f, hutHeight, t), wall);
			Box(hut, "Wall S above door", new Vector3(0.0f, (s.D.DoorHeight + hutHeight) * 0.5f, -t * 0.5f), new Vector3(s.D.DoorWidth, hutHeight - s.D.DoorHeight, t), wall);
			Box(hut, "Wall N", new Vector3(0.0f, hutHeight * 0.5f, hutEnd + t * 0.5f), new Vector3(2.0f * (halfX + t), hutHeight, t), wall);
			Box(hut, "Wall W", new Vector3(-halfX - t * 0.5f, hutHeight * 0.5f, hutEnd * 0.5f), new Vector3(t, hutHeight, hutEnd), wall);
			Box(hut, "Wall E", new Vector3( halfX + t * 0.5f, hutHeight * 0.5f, hutEnd * 0.5f), new Vector3(t, hutHeight, hutEnd), wall);
			Box(hut, "Roof", new Vector3(0.0f, hutHeight + ft * 0.5f, centerZ), new Vector3(outer.width, ft, outer.height), roof);
			AddDoor(s, hut, "Power door", new Vector3(-s.D.DoorWidth * 0.5f, 0.0f, -t * 0.5f), Quaternion.identity, s.D.DoorWidth, s.D.DoorHeight, true);

			// The shaft: walls from the room's ceiling up to the hut's floor, the ramp down
			float bottom = ceilingTop - hutFloor;
			float wallHeight = -bottom;
			Box(hut, "Shaft wall W", new Vector3(-shaftWidth * 0.5f - t * 0.5f, bottom * 0.5f, (landing + shaftEnd) * 0.5f), new Vector3(t, wallHeight, shaftEnd - landing), wall);
			Box(hut, "Shaft wall E", new Vector3( shaftWidth * 0.5f + t * 0.5f, bottom * 0.5f, (landing + shaftEnd) * 0.5f), new Vector3(t, wallHeight, shaftEnd - landing), wall);
			Box(hut, "Shaft wall S", new Vector3(0.0f, bottom * 0.5f - 0.2f, landing - t * 0.5f), new Vector3(shaftWidth + 2.0f * t, wallHeight - 0.4f, t), wall);
			Box(hut, "Shaft wall N", new Vector3(0.0f, bottom * 0.5f, shaftEnd + t * 0.5f), new Vector3(shaftWidth + 2.0f * t, wallHeight, t), wall);
			SlopedBox(hut, "Ramp", new Vector3(0.0f, 0.0f, landing), new Vector3(0.0f, -drop, landing + run), shaftWidth - 0.05f, 0.4f, floor);
			BuildLight(s, hut, new Vector3(0.0f, hutHeight - 0.08f, landing * 0.5f), false);

			s.Footprints.Add(new Footprint($"{room.name} entrance {number}", (Vector2)FlatOf(hut.TransformPoint(new Vector3(0.0f, 0.0f, centerZ))), new Vector2(outer.width * 0.5f, outer.height * 0.5f), yaw));
			s.Holes.Add(new Footprint($"{room.name} shaft {number}", (Vector2)FlatOf(hut.TransformPoint(new Vector3(0.0f, 0.0f, (shaft.yMin + shaft.yMax) * 0.5f))), new Vector2(shaft.width * 0.5f, shaft.height * 0.5f), yaw));

			// The shaft in the room's frame (entrances turn in steps of 90°, so it stays a rectangle there)
			Vector3 a = room.InverseTransformPoint(hut.TransformPoint(new Vector3(shaft.xMin, 0.0f, shaft.yMin)));
			Vector3 b = room.InverseTransformPoint(hut.TransformPoint(new Vector3(shaft.xMax, 0.0f, shaft.yMax)));
			Vector3 end = room.InverseTransformPoint(hut.TransformPoint(new Vector3(0.0f, 0.0f, landing + run)));
			Rect inRoom = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - 0.05f, Mathf.Min(a.z, b.z) - 0.05f, Mathf.Max(a.x, b.x) + 0.05f, Mathf.Max(a.z, b.z) + 0.05f);

			Vector2 roomSize = V2(placed.Data["size"], Vector2.zero);
			rampInside = Mathf.Abs(end.x) < roomSize.x * 0.5f - 0.5f && Mathf.Abs(end.z) < roomSize.y * 0.5f - 0.5f;
			return inRoom;
		}

		/// <summary>
		/// Draw calls: each area's static boxes are merged into one mesh per material (saved under MeshFolder/Combined), and the boxes
		/// keep only their colliders. Doors (they swing) and the lamp lenses and screens (switched with power) stay separate.
		/// </summary>
		private static int CombineMeshes(State s, Dictionary<string, Transform> areaGroups)
		{
			string folder = $"{MeshFolder}/Combined";
			if (AssetDatabase.IsValidFolder(folder) == true)
			{
				AssetDatabase.DeleteAsset(folder);
			}
			BrownoutMapBuilder.EnsureFolder(folder);

			var lampMaterials = new HashSet<Material> { s.Mat("LampOn"), s.Mat("LampOff") };
			int meshes = 0;

			foreach (KeyValuePair<string, Transform> area in areaGroups)
			{
				Transform group = area.Value;
				var byMaterial = new Dictionary<Material, List<MeshRenderer>>();
				foreach (MeshRenderer renderer in group.GetComponentsInChildren<MeshRenderer>(true))
				{
					if (renderer.gameObject.activeInHierarchy == false || renderer.GetComponentInParent<Door>() != null)
						continue;

					Material material = renderer.sharedMaterial;
					if (material == null || lampMaterials.Contains(material) == true || renderer.GetComponent<MeshFilter>() == null)
						continue;

					if (byMaterial.TryGetValue(material, out List<MeshRenderer> list) == false)
					{
						list = new List<MeshRenderer>();
						byMaterial.Add(material, list);
					}
					list.Add(renderer);
				}

				foreach (KeyValuePair<Material, List<MeshRenderer>> entry in byMaterial)
				{
					var parts = entry.Value.Select(r => new CombineInstance
					{
						mesh      = r.GetComponent<MeshFilter>().sharedMesh,
						transform = group.worldToLocalMatrix * r.transform.localToWorldMatrix,
					}).ToArray();

					var mesh = new Mesh { name = $"{area.Key}_{entry.Key.name}", indexFormat = IndexFormat.UInt32 };
					mesh.CombineMeshes(parts, true, true);
					mesh.RecalculateBounds();
					AssetDatabase.CreateAsset(mesh, $"{folder}/{mesh.name}.asset");

					var combined = new GameObject($"Drawn ({entry.Key.name})");
					combined.transform.SetParent(group, false);
					combined.isStatic = true;
					combined.AddComponent<MeshFilter>().sharedMesh = mesh;
					combined.AddComponent<MeshRenderer>().sharedMaterial = entry.Key;
					++meshes;

					// Phase 11c: the collider left behind is tagged by its material, for bullet impacts and footsteps
					string surfaceTag = SurfaceTag(entry.Key.name);

					foreach (MeshRenderer renderer in entry.Value)
					{
						if (surfaceTag != null)
						{
							renderer.gameObject.tag = surfaceTag;
						}

						MeshFilter filter = renderer.GetComponent<MeshFilter>();
						Object.DestroyImmediate(renderer);
						Object.DestroyImmediate(filter);
					}
				}
			}

			return meshes;
		}

		/// <summary>Phase 11c: Wood and cabin walls are wood; metal, machines and vehicles are metal; the rest stays untagged (concrete).</summary>
		private static string SurfaceTag(string materialName)
		{
			if (materialName.Contains("Wood") == true || materialName.Contains("CabinWall") == true)
				return "Wood";
			if (materialName.Contains("Metal") == true || materialName.Contains("Machine") == true || materialName.Contains("Vehicle") == true)
				return "Metal";
			return null;
		}

		private static void CutTerrainHoles(State s)
		{
			Terrain terrain = s.Context.Terrain;
			if (terrain == null)
				return;

			TerrainData data = terrain.terrainData;
			int     res      = data.holesResolution;
			float   cell     = data.size.x / res;
			Vector3 origin   = terrain.transform.position;
			var     solid    = new bool[res, res];

			for (int z = 0; z < res; ++z)
				for (int x = 0; x < res; ++x)
					solid[z, x] = true;

			int cut = 0;
			foreach (Footprint hole in s.Holes)
			{
				float reach = hole.Half.magnitude + cell;
				int x0 = Mathf.Max(0,       Mathf.FloorToInt((hole.Center.x - reach - origin.x) / cell));
				int x1 = Mathf.Min(res - 1, Mathf.CeilToInt ((hole.Center.x + reach - origin.x) / cell));
				int z0 = Mathf.Max(0,       Mathf.FloorToInt((hole.Center.y - reach - origin.z) / cell));
				int z1 = Mathf.Min(res - 1, Mathf.CeilToInt ((hole.Center.y + reach - origin.z) / cell));

				for (int z = z0; z <= z1; ++z)
				{
					for (int x = x0; x <= x1; ++x)
					{
						Vector2 center = new Vector2(origin.x + (x + 0.5f) * cell, origin.z + (z + 0.5f) * cell);
						if (hole.Contains(center, cell * 0.5f + 0.15f) == true)
						{
							solid[z, x] = false;
							++cut;
						}
					}
				}
			}

			data.SetHoles(0, 0, solid);
			EditorUtility.SetDirty(data);
		}

		// OTHER PIECES

		private static void BuildBlock(State s, JObject j, Transform root, float sink)
		{
			Vector3 size = V3(j["size"], new Vector3(2.0f, 2.0f, 2.0f));   // [width, depth, height]
			float   h    = size.z;
			Material material = s.Mat(S(j, "color", "Cover"));
			Box(root, "Block", new Vector3(0.0f, (h - sink) * 0.5f, 0.0f), new Vector3(size.x, h + sink, size.y), material);

			if (B(j, "ramps") == true)
			{
				float length = h / Mathf.Tan(25.0f * Mathf.Deg2Rad);
				for (int end = -1; end <= 1; end += 2)
				{
					Vector3 top = new Vector3(end * size.x * 0.5f, h, 0.0f);
					SlopedBox(root, "Ramp", top, top + new Vector3(end * length, -h - 0.1f, 0.0f), Mathf.Min(size.y, 4.0f), 0.4f, material);
				}
			}
		}

		private static void BuildCylinder(State s, JObject j, Transform root, float sink)
		{
			float r = F(j, "radius", 2.0f);
			float h = F(j, "height", 3.0f);
			Cylinder(root, "Cylinder", new Vector3(0.0f, -sink, 0.0f), r, h + sink, s.Mat(S(j, "color", "Machine")));
		}

		/// <summary>A cooling tower: a wide base, a narrower waist and a flared top (three solid drums; not enterable).</summary>
		private static void BuildTower(State s, JObject j, Transform root, float sink)
		{
			float r     = F(j, "radius", 14.0f);
			float waist = F(j, "waist", r * 0.7f);
			float top   = F(j, "top", r * 0.8f);
			float h     = F(j, "height", 30.0f);
			Material concrete = s.Mat(S(j, "color", "Wall"));

			Cylinder(root, "Base",  new Vector3(0.0f, -sink, 0.0f), r, h * 0.3f + sink, concrete);
			Cylinder(root, "Waist", new Vector3(0.0f, h * 0.3f, 0.0f), (r + waist) * 0.5f, h * 0.2f, concrete);
			Cylinder(root, "Neck",  new Vector3(0.0f, h * 0.5f, 0.0f), waist, h * 0.3f, concrete);
			Cylinder(root, "Top",   new Vector3(0.0f, h * 0.8f, 0.0f), top, h * 0.2f, concrete);
		}

		/// <summary>A satellite dish: a pedestal and a tilted disc facing a compass direction.</summary>
		private static void BuildDish(State s, JObject j, Transform root, float sink)
		{
			float r      = F(j, "radius", 8.0f);
			float h      = F(j, "height", 6.0f);
			float tilt   = F(j, "tilt", 40.0f);
			float facing = F(j, "facing", 180.0f);
			Material metal = s.Mat("Metal");

			Box(root, "Pedestal", new Vector3(0.0f, (h - sink) * 0.5f, 0.0f), new Vector3(2.4f, h + sink, 2.4f), s.Mat("Wall"));

			var mount = new GameObject("Mount").transform;
			mount.SetParent(root, false);
			mount.localPosition = new Vector3(0.0f, h + r * Mathf.Sin(tilt * Mathf.Deg2Rad) * 0.5f, 0.0f);
			mount.rotation = Quaternion.Euler(0.0f, facing, 0.0f) * Quaternion.Euler(tilt - 90.0f, 0.0f, 0.0f);
			Cylinder(mount, "Dish", new Vector3(0.0f, -0.15f, 0.0f), r, 0.3f, metal);
			Box(mount, "Feed arm", new Vector3(0.0f, r * 0.35f, 0.0f), new Vector3(0.25f, r * 0.7f, 0.25f), metal);
		}

		/// <summary>Railway tracks along local X: two rails on a low bed for each track.</summary>
		private static void BuildRails(State s, JObject j, Transform root)
		{
			float length  = F(j, "length", 100.0f);
			int   tracks  = Mathf.Max(1, I(j, "tracks", 1));
			float spacing = F(j, "spacing", 5.0f);
			Material rail = s.Mat("Rail");
			Material bed  = s.Mat("Dirt");

			for (int i = 0; i < tracks; ++i)
			{
				float z = (i - (tracks - 1) * 0.5f) * spacing;
				Box(root, $"Bed {i + 1}", new Vector3(0.0f, -0.1f, z), new Vector3(length, 0.3f, 3.2f), bed);
				Box(root, $"Rail {i + 1}", new Vector3(0.0f, 0.12f, z - 0.72f), new Vector3(length, 0.14f, 0.1f), rail);
				Box(root, $"Rail {i + 1}", new Vector3(0.0f, 0.12f, z + 0.72f), new Vector3(length, 0.14f, 0.1f), rail);
			}
		}

		/// <summary>A parked train along local X: closed cars are solid blocks, open cars have a doorway on both sides to walk through.</summary>
		private static void BuildTrain(State s, JObject j, Transform root)
		{
			JArray cars = j["cars"] as JArray ?? new JArray("closed");
			const float carLength = 14.0f;
			const float gap       = 1.2f;
			const float width     = 3.0f;
			const float floorY    = 1.2f;
			const float roofY     = 3.9f;
			const float doorWidth = 1.6f;
			const float doorTop   = 3.5f;
			const float side      = 0.15f;

			Material body  = s.Mat("Vehicle");
			Material under = s.Mat("Rail");
			Material floor = s.Mat("Floor");

			float total = cars.Count * carLength + (cars.Count - 1) * gap;
			for (int i = 0; i < cars.Count; ++i)
			{
				float x    = -total * 0.5f + carLength * 0.5f + i * (carLength + gap);
				bool  open = (string)cars[i] == "open";
				var   car  = new GameObject($"Car {i + 1} ({(open ? "open" : "closed")})").transform;
				car.SetParent(root, false);
				car.localPosition = new Vector3(x, 0.0f, 0.0f);

				Box(car, "Underframe", new Vector3(0.0f, 0.65f, 0.0f), new Vector3(carLength - 2.0f, 0.9f, 2.4f), under);
				if (open == false)
				{
					Box(car, "Body", new Vector3(0.0f, (floorY + roofY) * 0.5f, 0.0f), new Vector3(carLength, roofY - floorY + 0.2f, width), body);
					continue;
				}

				Box(car, "Floor", new Vector3(0.0f, floorY - 0.1f, 0.0f), new Vector3(carLength, 0.2f, width), floor);
				Box(car, "Roof",  new Vector3(0.0f, roofY + 0.1f, 0.0f), new Vector3(carLength, 0.2f, width), body);
				for (int end = -1; end <= 1; end += 2)
				{
					Box(car, "End wall", new Vector3(end * (carLength * 0.5f - side * 0.5f), (floorY + roofY) * 0.5f, 0.0f), new Vector3(side, roofY - floorY, width), body);
				}

				for (int sideSign = -1; sideSign <= 1; sideSign += 2)
				{
					float z    = sideSign * (width * 0.5f - side * 0.5f);
					float wall = (carLength - doorWidth) * 0.5f;
					Box(car, "Side", new Vector3(-(doorWidth + wall) * 0.5f, (floorY + roofY) * 0.5f, z), new Vector3(wall, roofY - floorY, side), body);
					Box(car, "Side", new Vector3( (doorWidth + wall) * 0.5f, (floorY + roofY) * 0.5f, z), new Vector3(wall, roofY - floorY, side), body);
					Box(car, "Over door", new Vector3(0.0f, (doorTop + roofY) * 0.5f, z), new Vector3(doorWidth, roofY - doorTop, side), body);

					// Two steps up to the doorway
					float outside = sideSign * (width * 0.5f);
					Box(car, "Step", new Vector3(0.0f, 0.2f, outside + sideSign * 0.6f), new Vector3(doorWidth, 0.4f, 0.4f), floor);
					Box(car, "Step", new Vector3(0.0f, 0.4f, outside + sideSign * 0.2f), new Vector3(doorWidth, 0.8f, 0.4f), floor);
				}
			}
		}

		/// <summary>A raised walkway along local X at 'height', with a rail on its -Z side, posts, and a ramp down from its +X end.</summary>
		private static void BuildCatwalk(State s, JObject j, Transform root)
		{
			Vector2 size   = V2(j["size"], new Vector2(20.0f, 2.0f));
			float   height = F(j, "height", 4.0f);
			Material metal = s.Mat("Metal");

			Box(root, "Walkway", new Vector3(0.0f, height - 0.1f, 0.0f), new Vector3(size.x, 0.2f, size.y), metal);
			Box(root, "Rail", new Vector3(0.0f, height + 0.5f, -size.y * 0.5f + 0.04f), new Vector3(size.x, 1.0f, 0.08f), metal);

			int posts = Mathf.Max(2, Mathf.CeilToInt(size.x / 6.0f) + 1);
			for (int i = 0; i < posts; ++i)
			{
				float x = Mathf.Lerp(-size.x * 0.5f + 0.2f, size.x * 0.5f - 0.2f, i / (posts - 1.0f));
				Box(root, "Post", new Vector3(x, (height - 0.2f) * 0.5f, -size.y * 0.5f + 0.15f), new Vector3(0.2f, height - 0.2f, 0.2f), metal);
			}

			float run = height / Mathf.Tan(s.D.StairAngle * Mathf.Deg2Rad);
			SlopedBox(root, "Stairs", new Vector3(size.x * 0.5f, height, 0.0f), new Vector3(size.x * 0.5f + run, 0.0f, 0.0f), size.y, 0.3f, metal);
			++s.Stairs;
		}

		/// <summary>War machines in the Warehouses (blocks): a walker mech, a tank, a truck. Long axis along local Z.</summary>
		private static void BuildVehicle(State s, JObject j, Transform root)
		{
			Material body  = s.Mat("Vehicle");
			Material metal = s.Mat("Machine");

			switch (S(j, "kind", "truck"))
			{
				case "mech":
					Box(root, "Leg", new Vector3(-0.9f, 1.3f, 0.0f), new Vector3(0.8f, 2.6f, 1.0f), metal);
					Box(root, "Leg", new Vector3( 0.9f, 1.3f, 0.0f), new Vector3(0.8f, 2.6f, 1.0f), metal);
					Box(root, "Body", new Vector3(0.0f, 3.5f, 0.0f), new Vector3(3.2f, 1.8f, 2.4f), body);
					Box(root, "Arm", new Vector3(-2.0f, 3.4f, 0.6f), new Vector3(0.7f, 0.7f, 2.4f), metal);
					Box(root, "Arm", new Vector3( 2.0f, 3.4f, 0.6f), new Vector3(0.7f, 0.7f, 2.4f), metal);
					Box(root, "Cockpit", new Vector3(0.0f, 4.7f, 0.4f), new Vector3(1.4f, 0.8f, 1.2f), metal);
					break;

				case "tank":
					Box(root, "Hull", new Vector3(0.0f, 0.9f, 0.0f), new Vector3(3.4f, 1.8f, 7.0f), body);
					Box(root, "Turret", new Vector3(0.0f, 2.25f, -0.4f), new Vector3(2.4f, 0.9f, 3.0f), body);
					Box(root, "Barrel", new Vector3(0.0f, 2.3f, 2.8f), new Vector3(0.3f, 0.3f, 3.6f), metal);
					break;

				default:
					Box(root, "Cab", new Vector3(0.0f, 1.3f, 2.6f), new Vector3(2.4f, 2.6f, 2.2f), body);
					Box(root, "Bed", new Vector3(0.0f, 1.3f, -1.4f), new Vector3(2.4f, 1.4f, 5.6f), body);
					Box(root, "Chassis", new Vector3(0.0f, 0.4f, 0.0f), new Vector3(2.0f, 0.8f, 7.8f), metal);
					break;
			}
		}

		// POWER PROPS

		/// <summary>A light fixture (a ceiling lamp inside, or a lamp on a pole outside): dark now, lit when its POI has power (phase 11).</summary>
		private static void BuildLight(State s, Transform parent, Vector3 localPosition, bool onPole)
		{
			var fixture = new GameObject("Light");
			fixture.transform.SetParent(parent, false);
			fixture.transform.localPosition = localPosition;

			float headY = 0.0f;
			if (onPole == true)
			{
				Box(fixture.transform, "Pole", new Vector3(0.0f, 2.25f, 0.0f), new Vector3(0.2f, 4.5f, 0.2f), s.Mat("Metal"));
				headY = 4.5f;
			}

			GameObject housing = Box(fixture.transform, "Housing", new Vector3(0.0f, headY + 0.03f, 0.0f), new Vector3(0.7f, 0.1f, 0.7f), s.Mat("Metal"), false);
			GameObject off     = Box(fixture.transform, "Lamp (off)", new Vector3(0.0f, headY - 0.04f, 0.0f), new Vector3(0.55f, 0.05f, 0.55f), s.Mat("LampOff"), false);
			GameObject on      = Box(fixture.transform, "Lamp (on)",  new Vector3(0.0f, headY - 0.04f, 0.0f), new Vector3(0.55f, 0.05f, 0.55f), s.Mat("LampOn"), false);
			housing.isStatic = true;

			var lightObject = new GameObject("Point light");
			lightObject.transform.SetParent(on.transform, false);
			lightObject.transform.localPosition = Vector3.down * 4.0f;   // 0.2 m under the lamp (the lamp is scaled 0.05 high)
			var light = lightObject.AddComponent<Light>();
			light.type      = LightType.Point;
			light.range     = s.S.LightRange;
			light.intensity = s.S.LightIntensity;
			light.color     = s.S.LampOn;
			light.shadows   = LightShadows.None;
			on.SetActive(false);

			AddProp(s, fixture, EPowerProp.Light, new[] { on }, new[] { off });
		}

		/// <summary>The Radio Array's camera monitors (Open Questions 1: where the cameras are and how they're viewed comes in phase 11).</summary>
		private static void BuildCamera(State s, Transform root)
		{
			Box(root, "Desk", new Vector3(0.0f, 0.4f, 0.0f), new Vector3(2.2f, 0.8f, 0.9f), s.Mat("Machine"));
			GameObject off = Box(root, "Screens (off)", new Vector3(0.0f, 1.25f, 0.2f), new Vector3(2.0f, 0.9f, 0.08f), s.Mat("LampOff"), false);
			GameObject on  = Box(root, "Screens (on)",  new Vector3(0.0f, 1.25f, 0.2f), new Vector3(2.0f, 0.9f, 0.08f), s.Mat("LampOn"), false);
			on.SetActive(false);
			AddProp(s, root.gameObject, EPowerProp.Camera, new[] { on }, new[] { off });
		}

		/// <summary>A Factory turret spot (Open Questions 2: what it does comes in phase 11): an inert block on a base.</summary>
		private static void BuildTurret(State s, Transform root, float sink)
		{
			Material metal = s.Mat("Machine");
			Cylinder(root, "Base", new Vector3(0.0f, -sink, 0.0f), 0.7f, 1.0f + sink, metal);
			Box(root, "Head", new Vector3(0.0f, 1.45f, 0.0f), new Vector3(1.2f, 0.9f, 1.4f), metal);
			Box(root, "Barrel", new Vector3(0.0f, 1.5f, 1.3f), new Vector3(0.2f, 0.2f, 1.4f), s.Mat("Metal"));
			GameObject off = Box(root, "Eye (off)", new Vector3(0.0f, 1.6f, 0.72f), new Vector3(0.3f, 0.15f, 0.05f), s.Mat("LampOff"), false);
			AddProp(s, root.gameObject, EPowerProp.Turret, null, new[] { off });
		}

		private static void AddProp(State s, GameObject target, EPowerProp kind, GameObject[] on, GameObject[] off)
		{
			PowerProp prop = target.AddComponent<PowerProp>();
			prop.EditorSet(kind, s.Area, on, off);
			s.Props.Add(prop);
		}

		// FOOTPRINTS OF PIECES

		private static void AddFootprints(Data data, Placed placed, List<Footprint> footprints)
		{
			JObject j    = placed.Data;
			string  type = S(j, "type", "block");
			string  name = S(j, "name", Title(type));

			void Add(Vector2 half) => footprints.Add(new Footprint(name, placed.Position, half, placed.Yaw));

			switch (type)
			{
				case "building": Add(V2(j["size"], new Vector2(8.0f, 6.0f)) * 0.5f); break;
				case "block":
				{
					Vector3 size = V3(j["size"], new Vector3(2.0f, 2.0f, 2.0f));
					float   ramp = B(j, "ramps") == true ? size.z / Mathf.Tan(25.0f * Mathf.Deg2Rad) : 0.0f;
					Add(new Vector2(size.x * 0.5f + ramp, size.y * 0.5f));
					break;
				}
				case "cylinder": Add(Vector2.one * F(j, "radius", 2.0f)); break;
				case "tower":    Add(Vector2.one * F(j, "radius", 14.0f)); break;
				case "dish":     Add(Vector2.one * (F(j, "radius", 8.0f) * 0.8f)); break;
				case "rails":    Add(new Vector2(F(j, "length", 100.0f) * 0.5f, (Mathf.Max(1, I(j, "tracks", 1)) - 1) * F(j, "spacing", 5.0f) * 0.5f + 1.8f)); break;
				case "train":
				{
					int cars = (j["cars"] as JArray)?.Count ?? 1;
					Add(new Vector2((cars * 14.0f + (cars - 1) * 1.2f) * 0.5f, 2.6f));
					break;
				}
				case "catwalk":  Add(V2(j["size"], new Vector2(20.0f, 2.0f)) * 0.5f + new Vector2(F(j, "height", 4.0f), 0.0f)); break;
				case "vehicle":  Add(new Vector2(2.5f, 4.0f)); break;
				case "turret":   Add(Vector2.one * 0.8f); break;
				case "light":    Add(Vector2.one * 0.4f); break;
				case "underground":
				{
					// The entrance huts (their length depends on the ground; the estimate is long enough for nature and loot)
					if (j["entrances"] is JArray entrances)
					{
						float depth = F(j, "depth", 8.0f);
						float length = 1.2f + depth / Mathf.Tan(30.0f * Mathf.Deg2Rad) + 4.0f;
						foreach (JObject entrance in entrances)
						{
							float   yaw = placed.Yaw + F(entrance, "rot", 0.0f);
							Vector2 at  = placed.Position + Rotate(V2(entrance["at"], Vector2.zero), placed.Yaw);
							footprints.Add(new Footprint($"{name} entrance", at + Rotate(new Vector2(0.0f, length * 0.5f), yaw), new Vector2(3.0f, length * 0.5f + 0.3f), yaw));
						}
					}
					break;
				}
			}
		}

		// CHECKS

		/// <summary>Warns about buildings on roads, trails or the river, and about buildings outside their area's box.</summary>
		private static void CheckPlacement(State s)
		{
			BrownoutMapBuilder.Context c = s.Context;
			foreach (Footprint footprint in s.Footprints)
			{
				bool road = false, river = false;
				for (float u = -1.0f; u <= 1.001f && road == false && river == false; u += 0.25f)
				{
					for (float v = -1.0f; v <= 1.001f; v += 0.25f)
					{
						Vector2 p = footprint.ToWorld(new Vector2(u * footprint.Half.x, v * footprint.Half.y));
						if (c.RoadDistance(p, out _) < 0.5f) { road = true; break; }
						if (c.RiverDistance(p) < c.S.ValleyHalfWidth) { river = true; break; }
					}
				}

				Vector2 file = BrownoutMapData.ToFile(footprint.Center);
				if (road == true)  s.Warnings.Add($"{footprint.Name} (E{file.x:F0} N{file.y:F0}) is on a road or trail.");
				if (river == true) s.Warnings.Add($"{footprint.Name} (E{file.x:F0} N{file.y:F0}) is in the river valley.");
			}
		}

		// NETWORKED STATE

		/// <summary>The scene object that networks the doors and power (a NetworkObject with MapState, seen by every player).</summary>
		private static string EnsureMapState(BrownoutMapBuilder.Context c)
		{
			GameObject root = c.Scene.GetRootGameObjects().FirstOrDefault(t => t.name == StateName);
			string result = string.Empty;
			if (root == null)
			{
				root = new GameObject(StateName);
				SceneManager.MoveGameObjectToScene(root, c.Scene);
				result = $"Made the {StateName} scene object (NetworkObject + MapState).";
			}

			NetworkObject networkObject = root.GetComponent<NetworkObject>() ?? root.AddComponent<NetworkObject>();
			networkObject.ObjectInterest = NetworkObjectInterestModes.Global;
			if (root.GetComponent<MapState>() == null)
			{
				root.AddComponent<MapState>();
			}

			EditorUtility.SetDirty(root);
			return result;
		}

		// GEOMETRY

		public static Vector2 Rotate(Vector2 v, float yaw)
		{
			Vector3 r = Quaternion.Euler(0.0f, yaw, 0.0f) * new Vector3(v.x, 0.0f, v.y);
			return new Vector2(r.x, r.z);
		}

		private static Vector2 FlatOf(Vector3 v) => new Vector2(v.x, v.z);

		private static void GroundRange(BrownoutMapBuilder.Context c, Footprint footprint, out float low, out float high)
		{
			low  = float.MaxValue;
			high = float.MinValue;

			int nx = Mathf.Max(2, Mathf.CeilToInt(footprint.Half.x * 2.0f) + 1);
			int nz = Mathf.Max(2, Mathf.CeilToInt(footprint.Half.y * 2.0f) + 1);
			for (int i = 0; i < nx; ++i)
			{
				for (int k = 0; k < nz; ++k)
				{
					Vector2 local = new Vector2(Mathf.Lerp(-footprint.Half.x, footprint.Half.x, i / (nx - 1.0f)), Mathf.Lerp(-footprint.Half.y, footprint.Half.y, k / (nz - 1.0f)));
					float   y     = c.GroundHeight(footprint.ToWorld(local));
					low  = Mathf.Min(low, y);
					high = Mathf.Max(high, y);
				}
			}
		}

		private static GameObject Box(Transform parent, string name, Vector3 localCenter, Vector3 size, Material material, bool collider = true)
		{
			var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
			block.name = name;
			block.transform.SetParent(parent, false);
			block.transform.localPosition = localCenter;
			block.transform.localRotation = Quaternion.identity;
			block.transform.localScale    = size;
			block.layer    = 0;
			block.isStatic = true;
			block.GetComponent<MeshRenderer>().sharedMaterial = material;
			if (collider == false)
			{
				Object.DestroyImmediate(block.GetComponent<Collider>());
				block.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
			}

			return block;
		}

		/// <summary>A slab whose top runs from a to b (a stair flight, a ramp), width across, thickness under the top.</summary>
		private static void SlopedBox(Transform parent, string name, Vector3 a, Vector3 b, float width, float thickness, Material material)
		{
			Vector3    along    = b - a;
			Quaternion rotation = Quaternion.LookRotation(along, Vector3.up);
			Vector3    center   = (a + b) * 0.5f + rotation * new Vector3(0.0f, -thickness * 0.5f, 0.0f);

			GameObject block = Box(parent, name, center, new Vector3(width, thickness, along.magnitude), material);
			block.transform.localRotation = rotation;
		}

		/// <summary>A horizontal slab (top at yTop, thickness down) over a rectangle, with rectangular holes cut out (split into boxes).</summary>
		private static void Slab(Transform parent, string name, float yTop, float thickness, Rect outer, List<Rect> holes, Material material)
		{
			var xs = new List<float> { outer.xMin, outer.xMax };
			foreach (Rect hole in holes)
			{
				xs.Add(Mathf.Clamp(hole.xMin, outer.xMin, outer.xMax));
				xs.Add(Mathf.Clamp(hole.xMax, outer.xMin, outer.xMax));
			}
			xs = xs.Distinct().OrderBy(t => t).ToList();

			int piece = 0;
			for (int i = 0; i < xs.Count - 1; ++i)
			{
				float xa = xs[i];
				float xb = xs[i + 1];
				if (xb - xa < 0.01f)
					continue;

				var covered = holes.Where(h => h.xMin < xb - 0.001f && h.xMax > xa + 0.001f).Select(h => (Mathf.Max(h.yMin, outer.yMin), Mathf.Min(h.yMax, outer.yMax))).OrderBy(t => t.Item1).ToList();
				float cursor = outer.yMin;
				foreach ((float z0, float z1) in covered)
				{
					if (z0 > cursor + 0.01f)
					{
						Box(parent, $"{name} {++piece}", new Vector3((xa + xb) * 0.5f, yTop - thickness * 0.5f, (cursor + z0) * 0.5f), new Vector3(xb - xa, thickness, z0 - cursor), material);
					}
					cursor = Mathf.Max(cursor, z1);
				}

				if (outer.yMax > cursor + 0.01f)
				{
					Box(parent, $"{name} {++piece}", new Vector3((xa + xb) * 0.5f, yTop - thickness * 0.5f, (cursor + outer.yMax) * 0.5f), new Vector3(xb - xa, thickness, outer.yMax - cursor), material);
				}
			}
		}

		/// <summary>A solid upright cylinder from its bottom center (Unity's cylinder with a convex mesh collider).</summary>
		private static void Cylinder(Transform parent, string name, Vector3 bottom, float radius, float height, Material material)
		{
			var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			cylinder.name = name;
			cylinder.transform.SetParent(parent, false);
			cylinder.transform.localPosition = bottom + Vector3.up * (height * 0.5f);
			cylinder.transform.localRotation = Quaternion.identity;
			cylinder.transform.localScale    = new Vector3(radius * 2.0f, height * 0.5f, radius * 2.0f);
			cylinder.layer    = 0;
			cylinder.isStatic = true;
			cylinder.GetComponent<MeshRenderer>().sharedMaterial = material;

			Object.DestroyImmediate(cylinder.GetComponent<Collider>());
			var collider = cylinder.AddComponent<MeshCollider>();
			collider.sharedMesh = cylinder.GetComponent<MeshFilter>().sharedMesh;
			collider.convex     = true;
		}

		private static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material material, bool collide)
		{
			var gameObject = new GameObject(name);
			gameObject.transform.SetParent(parent, false);
			gameObject.isStatic = true;
			gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
			gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
			if (collide == true)
			{
				var collider = gameObject.AddComponent<MeshCollider>();
				collider.sharedMesh = mesh;
				collider.convex     = true;
			}

			return gameObject;
		}

		/// <summary>A gable roof: a triangular prism along local X (length), base width across Z, ridge height up. Saved as an asset per size.</summary>
		private static Mesh PrismMesh(float length, float width, float height)
		{
			string path = $"{MeshFolder}/Gable_{length:F2}x{width:F2}x{height:F2}.asset".Replace(',', '.');
			Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
			if (mesh != null)
				return mesh;

			float hx = length * 0.5f;
			float hz = width * 0.5f;
			Vector3 a = new Vector3(-hx, 0.0f, -hz), b = new Vector3(hx, 0.0f, -hz), c = new Vector3(hx, 0.0f, hz), d = new Vector3(-hx, 0.0f, hz);
			Vector3 e = new Vector3(-hx, height, 0.0f), f = new Vector3(hx, height, 0.0f);

			var vertices  = new List<Vector3>();
			var triangles = new List<int>();
			void Face(params Vector3[] points)
			{
				int start = vertices.Count;
				vertices.AddRange(points);
				for (int i = 1; i < points.Length - 1; ++i)
				{
					triangles.Add(start);
					triangles.Add(start + i);
					triangles.Add(start + i + 1);
				}
			}

			Face(a, e, f, b);   // South slope
			Face(c, f, e, d);   // North slope
			Face(d, e, a);      // West gable
			Face(b, f, c);      // East gable
			Face(a, b, c, d);   // Bottom

			mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			AssetDatabase.CreateAsset(mesh, path);
			return mesh;
		}

		// JSON

		private static float  F(JObject j, string key, float fallback)  => j[key] != null && j[key].Type != JTokenType.Null ? (float)j[key] : fallback;
		private static int    I(JObject j, string key, int fallback)    => j[key] != null && j[key].Type != JTokenType.Null ? (int)j[key] : fallback;
		private static bool   B(JObject j, string key)                  => j[key] != null && j[key].Type == JTokenType.Boolean && (bool)j[key] == true;
		private static string S(JObject j, string key, string fallback) => j[key] != null && j[key].Type == JTokenType.String ? (string)j[key] : fallback;

		private static Vector2 V2(JToken token, Vector2 fallback)
		{
			return token is JArray array && array.Count >= 2 ? new Vector2((float)array[0], (float)array[1]) : fallback;
		}

		private static Vector3 V3(JToken token, Vector3 fallback)
		{
			return token is JArray array && array.Count >= 3 ? new Vector3((float)array[0], (float)array[1], (float)array[2]) : fallback;
		}

		private static string Title(string type) => string.IsNullOrEmpty(type) == true ? "Piece" : char.ToUpperInvariant(type[0]) + type.Substring(1);
	}
}
