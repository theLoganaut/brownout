using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Map v0 numbers (phase 8): how Brownout → Setup → Build Brownout Map turns References/map_layout.json and map_loot.json into the
	/// Brownout scene (terrain, heights, river and its wall, bridges, roads, nature, loot spots, the map picture), plus the few the game
	/// reads at runtime (the river rule, the full-map grid). Tuned here, never in code. Linked from GlobalSettings.BrownoutMap.
	/// The layout itself (where things are) lives in the References files, not here.
	/// </summary>
	[CreateAssetMenu(fileName = "BrownoutMapSettings", menuName = "TPSBR/Brownout Map Settings")]
	public sealed class BrownoutMapSettings : ScriptableObject
	{
		[Header("Build")]
		[Tooltip("Seed for everything random in the build (noise, trees, loot point scatter), so a rebuild gives the same map.")]
		public int   Seed = 8;

		[Header("Terrain")]
		[Tooltip("Terrain square (m): the 1000 m map plus open wilderness on every side.")]
		public float TerrainSize          = 1600.0f;
		[Tooltip("Heightmap samples per side (2^n + 1).")]
		public int   HeightmapResolution  = 1025;
		[Tooltip("Paint (splat) pixels per side.")]
		public int   AlphamapResolution   = 1024;
		[Tooltip("Grass cells per side.")]
		public int   DetailResolution     = 1024;
		[Tooltip("Lowest point the terrain can hold (m, below sea level, for the river bed).")]
		public float TerrainBottom        = -10.0f;
		[Tooltip("Height range from the bottom (m).")]
		public float TerrainHeight        = 60.0f;

		[Header("Heights (Logan's height sketch: the bands in map_layout.json heights.zones, 0-35 m)")]
		[Tooltip("Cell size (m) of the grid the height zones are worked out on.")]
		public float HeightCell           = 4.0f;
		[Tooltip("A zone with no higher zone inside reaches its band's top this far in from its edge (m); a valley its bottom.")]
		public float BandRamp             = 100.0f;
		[Tooltip("Softens the slope's bends at zone lines (m).")]
		public float HeightBlur           = 14.0f;
		[Tooltip("Rolling hills: up and down by this share of the local height (0.4 = ±40%, so ±12 m at 30 m), and never less than HillMinimum (m).")]
		public float HillShare            = 0.4f;
		public float HillMinimum          = 6.0f;
		[Tooltip("Hills calm down toward the river: flat at the valley's edge, full size this far from the river's line (m).")]
		public float HillRiverClear       = 20.0f;
		[Tooltip("Bank mounds along both sides of the river (cover at the chokepoint): height (m), feature size (m), and how far from the river's line they reach (m).")]
		public float BankMoundHeight      = 8.0f;
		public float BankMoundScale       = 30.0f;
		public float BankMoundReach       = 80.0f;
		[Tooltip("Rolling hills' feature size (m), and how often the noise reaches the full share (higher = more often).")]
		public float HillScale            = 55.0f;
		public float HillContrast         = 1.8f;
		[Tooltip("Small bumps (m), and their feature size (m).")]
		public float BumpHeight           = 0.8f;
		public float BumpScale            = 35.0f;
		[Tooltip("POI and cabin pads: how much of the ground's tilt is kept (0 = flat, 1 = untouched), and the blend distance outside the box (m).")]
		public float PadTiltKept          = 0.1f;
		public float PadFalloff           = 40.0f;
		[Tooltip("Road heights are averaged along the road over this distance (m), so roads run smooth.")]
		public float RoadSmoothing        = 40.0f;
		[Tooltip("Ground blended to the road height this far past the road's edge (m).")]
		public float RoadShoulder         = 10.0f;
		public float TrailShoulder        = 5.0f;

		[Header("River (a barrier: no wading, no swimming)")]
		[Tooltip("River bed below the bank (m).")]
		public float RiverDepth           = 3.0f;
		[Tooltip("Water surface above the bed (m).")]
		public float WaterDepth           = 0.6f;
		[Tooltip("Valley half width from the river's center line (m): the banks slope down to the water over this distance.")]
		public float ValleyHalfWidth      = 11.0f;
		[Tooltip("Bank heights are averaged along the river over this distance (m).")]
		public float RiverSmoothing       = 80.0f;
		[Tooltip("The river's line continues this far past the map at both ends (m), so the wall reaches past the map edge.")]
		public float RiverExtension       = 320.0f;
		[Tooltip("Invisible wall height above the water (m) and thickness (m). Players collide with it; bullets, grenades and the grapple ray don't.")]
		public float WallHeight           = 40.0f;
		public float WallThickness        = 0.5f;
		[Tooltip("Runtime: anyone standing in the channel (wingsuit or chute landing) is put this far up the nearer bank (m).")]
		public float BankPutDistance      = 2.0f;

		[Header("Bridges")]
		[Tooltip("Deck width (m): the 6 m road plus a walkway each side.")]
		public float BridgeWidth          = 8.0f;
		[Tooltip("Extra deck length past the valley at each end (m): the abutments.")]
		public float BridgeOverhang       = 4.0f;
		public float BridgeThickness      = 0.6f;
		public float BridgeRailHeight     = 1.0f;
		[Tooltip("Deck underside at least this far above the water (m), and the road's ramp up to the deck at each end (m).")]
		public float BridgeClearance      = 2.5f;
		public float BridgeRampLength     = 30.0f;

		[Header("Nature (placeholders until Blender models replace the prefabs)")]
		[Tooltip("Trees: one try per cell of this size (m), kept with this chance in open wilderness.")]
		public float TreeCell             = 9.0f;
		public float TreeChance           = 0.42f;
		public float BushCell             = 8.0f;
		public float BushChance           = 0.22f;
		[Tooltip("Bushes are solid (Logan, phase 9: nobody hides inside one): a standing capsule on the player-only layer 15 (bullets and grenades pass), this radius at scale 1 (m); its top is this height plus the radius above the highest ground under the bush.")]
		public float BushColliderRadius   = 0.95f;
		public float BushColliderHeight   = 0.6f;
		public float SmallRockCell        = 16.0f;
		public float SmallRockChance      = 0.25f;
		[Tooltip("Medium and large rocks (solid objects), over the whole terrain.")]
		public int   MediumRocks          = 450;
		public int   LargeRocks           = 110;
		[Tooltip("Tree and bush density inside POIs and cabin areas (share of the wilderness density).")]
		public float PoiDensity           = 0.12f;
		public float CabinDensity         = 0.35f;
		[Tooltip("Kept clear of trees past a road's or trail's edge (m).")]
		public float RoadClearance        = 3.0f;
		public float TrailClearance       = 1.5f;
		[Tooltip("Grass clumps per grass cell (0..16) in open ground, and how far grass is drawn (m).")]
		public int   GrassDensity         = 3;
		public float GrassDistance        = 80.0f;
		[Tooltip("Runtime (NatureInstancer): how far trees are drawn (m), where they switch to their simple model (m), and how far bushes and small rocks are drawn (m).")]
		public float TreeDrawDistance        = 500.0f;
		public float TreeLodDistance         = 110.0f;
		public float SmallNatureDrawDistance = 130.0f;

		[Header("NavMesh for the bots (phase 9; Build Brownout Map → NavMesh)")]
		[Tooltip("Baked over the playable square plus this margin (m).")]
		public float NavMargin            = 30.0f;
		[Tooltip("The walker the mesh is baked for: radius (a little under the player's 0.30 m so 0.75 m doors stay open), height, step (m) and steepest slope (degrees).")]
		public float NavAgentRadius       = 0.25f;
		public float NavAgentHeight       = 2.00f;
		public float NavStepHeight        = 0.45f;
		public float NavMaxSlope          = 45.0f;
		[Tooltip("Voxel size (m) and tile size (voxels). Smaller voxels keep doorways open but take longer to bake.")]
		public float NavVoxelSize         = 0.125f;
		public int   NavTileSize          = 256;

		[Header("Doors (phase 8b; sizes in map_layout.json doors and windows)")]
		[Tooltip("How far a door swings open (degrees) and how long the swing takes (s).")]
		public float DoorOpenAngle        = 90.0f;
		public float DoorSwingTime        = 0.3f;
		[Tooltip("A closed door this far past the capsule's edge, the way you're pushing, opens (m).")]
		public float DoorPushReach        = 0.35f;
		[Tooltip("F closes an open door within this distance of the eye (m).")]
		public float DoorCloseReach       = 2.5f;

		[Header("Buildings (phase 8b; layouts in References/map_blockout.json)")]
		[Tooltip("Steps or a ramp go in front of a ground-floor door when the floor is more than this above the ground there (m).")]
		public float DoorStepMinimum      = 0.2f;
		[Tooltip("Steepest ramp in front of a door (degrees).")]
		public float DoorRampAngle        = 25.0f;
		[Tooltip("Trees, bushes, rocks and grass are kept this far from a building's walls (m).")]
		public float BuildingClearance    = 2.0f;
		[Tooltip("Placeholder lights when a POI has power (phase 11 switches them): range (m) and intensity.")]
		public float LightRange           = 14.0f;
		public float LightIntensity       = 2.5f;

		[Header("View (runtime)")]
		[Tooltip("Distance fog on our map. Off (Logan, phase 11): the sample's Gameplay prefab turns on linear fog ending at 350 m, which hides the map from the helicopter; the Brownout map overrides it with these.")]
		public bool  Fog                  = false;
		public float FogStart             = 600.0f;
		public float FogEnd               = 2000.0f;

		[Header("Loot spots")]
		[Tooltip("Loot points in a spot are at least this far apart (m), and this far from tree trunks.")]
		public float LootSpacing          = 1.5f;
		public float LootTreeClearance    = 1.2f;
		[Tooltip("No loot on ground steeper than this (degrees).")]
		public float LootMaxSlope         = 30.0f;

		[Header("Map picture and full map")]
		[Tooltip("Pixels per side of the baked top-down picture of the 1000 m map.")]
		public int   MapImageResolution   = 2048;
		[Tooltip("Full-map grid: this many 100 m cells per side (A..J across, 1..10 down from the north).")]
		public int   GridCells            = 10;

		[Header("Placeholder palette")]
		public Color Grass      = new Color(0.36f, 0.50f, 0.25f);
		public Color GrassDry   = new Color(0.52f, 0.55f, 0.30f);
		public Color Dirt       = new Color(0.47f, 0.38f, 0.27f);
		public Color Rock       = new Color(0.45f, 0.44f, 0.42f);
		public Color Road       = new Color(0.33f, 0.31f, 0.29f);
		public Color Trail      = new Color(0.58f, 0.48f, 0.34f);
		public Color Water      = new Color(0.16f, 0.36f, 0.48f);
		public Color Trunk      = new Color(0.33f, 0.24f, 0.16f);
		public Color Pine       = new Color(0.16f, 0.32f, 0.18f);
		public Color Leaves     = new Color(0.30f, 0.46f, 0.20f);
		public Color DeadWood   = new Color(0.40f, 0.36f, 0.31f);
		public Color Bush       = new Color(0.26f, 0.42f, 0.19f);
		public Color Stone      = new Color(0.50f, 0.50f, 0.48f);
		public Color Bridge     = new Color(0.42f, 0.40f, 0.38f);
		public Color Steel      = new Color(0.30f, 0.33f, 0.37f);
		public Color Wood       = new Color(0.42f, 0.30f, 0.19f);
		[Tooltip("Blockouts (phase 8b): walls, floors, roofs, doors, power doors, cover blocks, machines, vehicles, metal, rails.")]
		public Color Wall       = new Color(0.62f, 0.60f, 0.56f);
		public Color Floor      = new Color(0.45f, 0.44f, 0.42f);
		public Color Roof       = new Color(0.36f, 0.34f, 0.33f);
		public Color DoorLeaf   = new Color(0.46f, 0.33f, 0.22f);
		public Color PowerDoor  = new Color(0.78f, 0.62f, 0.12f);
		public Color Cover      = new Color(0.52f, 0.50f, 0.47f);
		public Color Machine    = new Color(0.34f, 0.40f, 0.44f);
		public Color Vehicle    = new Color(0.33f, 0.37f, 0.27f);
		public Color Metal      = new Color(0.55f, 0.57f, 0.60f);
		public Color Rail       = new Color(0.30f, 0.28f, 0.26f);
		public Color CabinWall  = new Color(0.55f, 0.42f, 0.30f);
		public Color LampOff    = new Color(0.25f, 0.25f, 0.25f);
		public Color LampOn     = new Color(1.00f, 0.92f, 0.70f);

		[Header("Loading screen (phase 11b)")]
		[Tooltip("The map picture the loading screen shows (Build Map Picture makes it).")]
		public Texture2D Picture;
		[Tooltip("POI names on the loading screen's map: x / y = east / north from the map's south-west corner, 0-1.")]
		public PoiLabel[] PoiLabels = new PoiLabel[0];

		[System.Serializable]
		public struct PoiLabel
		{
			public string Name;
			public float  X;
			public float  Y;
		}

		// PUBLIC MEMBERS

		public static BrownoutMapSettings Current
		{
			get
			{
				BrownoutMapSettings settings = Global.Settings != null ? Global.Settings.BrownoutMap : null;
				if (settings != null)
					return settings;

				if (_fallback == null)
				{
					_fallback = CreateInstance<BrownoutMapSettings>();
				}

				return _fallback;
			}
		}

		private static BrownoutMapSettings _fallback;
	}
}
