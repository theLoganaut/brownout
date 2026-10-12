namespace TPSBR
{
	using System.Collections.Generic;
	using System.Globalization;
	using System.IO;
	using System.Text;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;
	using UnityEngine;

	/// <summary>
	/// The map's data files (phase 8): References/map_layout.json (where everything is; the Map & POIs page's code block) and
	/// References/map_loot.json (loot spots per POI, edited with the Loot Spot window). Coordinates in the files are [E, N] from the
	/// map's southwest corner; in Unity world X = E − 500, Z = N − 500.
	/// </summary>
	public static class BrownoutMapData
	{
		public const string LayoutPath = "References/map_layout.json";
		public const string LootPath   = "References/map_loot.json";

		public static Vector2 ToWorld(float e, float n) => new Vector2(e - 500.0f, n - 500.0f);
		public static Vector2 ToWorld(JToken point)     => ToWorld((float)point[0], (float)point[1]);
		public static Vector2 ToFile(Vector2 world)     => new Vector2(world.x + 500.0f, world.y + 500.0f);

		// LAYOUT

		public sealed class Box
		{
			public string  Id;
			public string  Name;
			public Vector2 Min;     // World X / Z
			public Vector2 Max;
			public bool    IsCabins;
			public int     Cabins;

			public Vector2 Center => (Min + Max) * 0.5f;
			public Vector2 Size   => Max - Min;
		}

		/// <summary>An outline from Logan's height sketch and its band [low, high] (m); later zones are drawn over earlier ones.</summary>
		public sealed class HeightZone
		{
			public string        Name;
			public Vector2       Band;
			public List<Vector2> Outline = new List<Vector2>();   // World X / Z
		}

		public sealed class Layout
		{
			public float               Size;
			public List<Box>           Pois       = new List<Box>();
			public List<Box>           CabinAreas = new List<Box>();
			public List<Vector2>       River      = new List<Vector2>();
			public float               RiverWidth;
			public List<Vector2>       Bridges    = new List<Vector2>();
			public List<string>        BridgeStyles = new List<string>();
			public List<List<Vector2>> Roads      = new List<List<Vector2>>();
			public float               RoadWidth;
			public List<List<Vector2>> Trails     = new List<List<Vector2>>();
			public float               TrailWidth;
			public Vector2             DefaultBand;   // [low, high] m where no height zone is
			public List<HeightZone>    HeightZones = new List<HeightZone>();

			public IEnumerable<Box> AllAreas()
			{
				foreach (Box box in Pois)       yield return box;
				foreach (Box box in CabinAreas) yield return box;
			}
		}

		private static readonly Dictionary<string, string> CabinNames = new Dictionary<string, string>
		{
			{ "northwest", "Cabins, northwest corner" },
			{ "ridge",     "Cabins, south of Radio Array" },
			{ "center",    "Cabins, center" },
			{ "south",     "Cabins, south edge" },
		};

		public static Layout LoadLayout()
		{
			JObject root   = JObject.Parse(File.ReadAllText(Path.Combine(ProjectFolder, LayoutPath)));
			var     layout = new Layout();

			layout.Size = (float)root["size"][0];

			foreach (JProperty poi in ((JObject)root["pois"]).Properties())
			{
				layout.Pois.Add(new Box
				{
					Id   = poi.Name,
					Name = TitleCase(poi.Name),
					Min  = ToWorld(poi.Value["min"]),
					Max  = ToWorld(poi.Value["max"]),
				});
			}

			foreach (JProperty area in ((JObject)root["cabin_areas"]).Properties())
			{
				layout.CabinAreas.Add(new Box
				{
					Id       = "cabins_" + area.Name,
					Name     = CabinNames.TryGetValue(area.Name, out string cabinName) == true ? cabinName : "Cabins, " + area.Name,
					Min      = ToWorld(area.Value["min"]),
					Max      = ToWorld(area.Value["max"]),
					IsCabins = true,
					Cabins   = (int)area.Value["cabins"],
				});
			}

			layout.RiverWidth = (float)root["river"]["width"];
			foreach (JToken point in root["river"]["points"])
			{
				layout.River.Add(ToWorld(point));
			}

			foreach (JToken bridge in root["bridges"])
			{
				// [E, N], or {"at": [E, N], "style": "concrete" | "truss" | "arch" | "wood" | "girder"}
				bool simple = bridge.Type == JTokenType.Array;
				layout.Bridges.Add(ToWorld(simple == true ? bridge : bridge["at"]));
				layout.BridgeStyles.Add(simple == true ? "concrete" : ((string)bridge["style"] ?? "concrete"));
			}

			layout.RoadWidth = (float)root["roads"]["width"];
			foreach (JToken line in root["roads"]["lines"])
			{
				var points = new List<Vector2>();
				foreach (JToken point in line) points.Add(ToWorld(point));
				layout.Roads.Add(points);
			}

			layout.TrailWidth = (float)root["trails"]["width"];
			foreach (JToken line in root["trails"]["lines"])
			{
				var points = new List<Vector2>();
				foreach (JToken point in line) points.Add(ToWorld(point));
				layout.Trails.Add(points);
			}

			JToken heights = root["heights"];
			layout.DefaultBand = new Vector2((float)heights["default_band"][0], (float)heights["default_band"][1]);
			foreach (JToken zone in heights["zones"])
			{
				var heightZone = new HeightZone
				{
					Name = (string)zone["name"],
					Band = new Vector2((float)zone["band"][0], (float)zone["band"][1]),
				};
				foreach (JToken point in zone["outline"]) heightZone.Outline.Add(ToWorld(point));
				layout.HeightZones.Add(heightZone);
			}

			return layout;
		}

		// LOOT SPOTS

		public sealed class LootFile
		{
			public string         about;
			public List<LootArea> areas = new List<LootArea>();
		}

		public sealed class LootArea
		{
			public string         id;
			public string         name;
			public string         density;
			public List<LootSpot> spots = new List<LootSpot>();

			public int Total
			{
				get
				{
					int total = 0;
					foreach (LootSpot spot in spots) total += spot.count;
					return total;
				}
			}
		}

		public sealed class LootSpot
		{
			public string  name;
			public float[] at = new float[2];
			public float   radius;
			public int     count;
			public bool    top_tier;
			[Tooltip("Phase 8b: which floors the points go on: 0 the ground or a building's ground floor, 1, 2... the floors above (the roof counts), -1 underground. Missing = [0].")]
			public int[]   floors;
			[Tooltip("Phase 8b: only points with a roof or ceiling above them (inside buildings and train cars).")]
			public bool    indoors;

			[JsonIgnore]
			public Vector2 World
			{
				get => ToWorld(at[0], at[1]);
				set
				{
					Vector2 file = ToFile(value);
					at = new[] { Mathf.Round(file.x), Mathf.Round(file.y) };
				}
			}
		}

		public static LootFile LoadLoot()
		{
			return JsonConvert.DeserializeObject<LootFile>(File.ReadAllText(Path.Combine(ProjectFolder, LootPath)));
		}

		/// <summary>Writes the loot file with one spot per line, so it stays easy to read and diff.</summary>
		public static void SaveLoot(LootFile file)
		{
			var text = new StringBuilder();
			text.Append("{\n");
			text.Append($"  \"about\": {JsonConvert.ToString(file.about ?? string.Empty)},\n");
			text.Append("  \"areas\": [\n");

			for (int a = 0; a < file.areas.Count; ++a)
			{
				LootArea area = file.areas[a];
				text.Append("    {\n");
				text.Append($"      \"id\": {JsonConvert.ToString(area.id)},\n");
				text.Append($"      \"name\": {JsonConvert.ToString(area.name)},\n");
				text.Append($"      \"density\": {JsonConvert.ToString(area.density ?? string.Empty)},\n");
				text.Append("      \"spots\": [\n");

				for (int s = 0; s < area.spots.Count; ++s)
				{
					LootSpot spot = area.spots[s];
					text.Append("        { ");
					text.Append($"\"name\": {JsonConvert.ToString(spot.name)}, ");
					text.Append($"\"at\": [{Number(spot.at[0])}, {Number(spot.at[1])}], ");
					text.Append($"\"radius\": {Number(spot.radius)}, ");
					text.Append($"\"count\": {spot.count}, ");
					text.Append($"\"top_tier\": {(spot.top_tier ? "true" : "false")}");
					if (spot.floors != null && spot.floors.Length > 0 && (spot.floors.Length != 1 || spot.floors[0] != 0))
					{
						text.Append($", \"floors\": [{string.Join(", ", spot.floors)}]");
					}
					if (spot.indoors == true)
					{
						text.Append(", \"indoors\": true");
					}
					text.Append(" }");
					text.Append(s < area.spots.Count - 1 ? ",\n" : "\n");
				}

				text.Append("      ]\n");
				text.Append(a < file.areas.Count - 1 ? "    },\n" : "    }\n");
			}

			text.Append("  ]\n}\n");
			File.WriteAllText(Path.Combine(ProjectFolder, LootPath), text.ToString());
		}

		// HELPERS

		public static string ProjectFolder => Directory.GetParent(Application.dataPath).FullName;

		private static string Number(float value)
		{
			return Mathf.Approximately(value, Mathf.Round(value)) == true ? Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);
		}

		private static string TitleCase(string id)
		{
			string[] words = id.Split('_');
			for (int i = 0; i < words.Length; ++i)
			{
				if (words[i].Length > 0)
				{
					words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
				}
			}

			return string.Join(" ", words);
		}
	}
}
