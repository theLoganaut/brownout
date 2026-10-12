using System.Collections.Generic;
using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Bots (phase 9): server-spawned duos of agents with a brain (BotBrain). They're dummies with a bot number, so they share the dummies'
	/// teams, respawn in the warm-up and are removed with them. 9a spawns them from the test tools; 9b places them at match start
	/// (PlaceBotDuos: on the ground at POIs and cabin areas, the bigger POIs first, at most BotSettings.MaxDuosPerArea per area, each duo
	/// at its own loot spot) and gives a lone player a bot teammate (SpawnBotTeammate).
	/// </summary>
	public abstract partial class GameplayMode
	{
		// PUBLIC MEMBERS

		/// <summary>Where the last match start put each bot team (for the Bot Check and Log Bot Status).</summary>
		public struct BotPlacement
		{
			public byte    Team;
			public string  Area;
			public Vector3 Position;
			public bool    Teammate;   // A lone player's bot
		}

		public IReadOnlyList<BotPlacement> LastBotPlacements => _botPlacements;

		/// <summary>Phase 11f: the Osprey's straight path, for planning where bots jump (distances in m along it from Start).</summary>
		public struct DropPath
		{
			public Vector3 Start;
			public Vector3 Direction;   // Flat, normalized
			public float   OverMapFrom; // The part of the path over the map
			public float   OverMapTo;

			public float Along(Vector3 point) => Vector3.Dot((point - Start).OnlyXZ(), Direction);

			/// <summary>Flat distance from a point to the part of the path over the map.</summary>
			public float DistanceTo(Vector3 point)
			{
				float   along   = Mathf.Clamp(Along(point), OverMapFrom, OverMapTo);
				Vector3 closest = Start + Direction * along;
				return (point - closest).OnlyXZ().magnitude;
			}
		}

		/// <summary>Phase 11f: a bot team still aboard the Osprey (until all its bots have jumped).</summary>
		public sealed class BotDrop
		{
			public byte      Team;
			public string    Area;
			public Vector3   Target;
			public float     JumpAlong;     // The duo jumps once the Osprey is this far along its path
			public byte[]    Numbers;       // Bot numbers, one per bot to jump
			public bool      Teammate;      // A lone player's bot: jumps when its player does and follows them down
			public PlayerRef Human;
			public bool      HumanJumped;
			public int       Jumped;
			public int       NextJumpTick;

			public int Aboard => Numbers.Length - Jumped;
		}

		public IReadOnlyList<BotDrop> BotDrops => _botDrops;

		/// <summary>Bots still aboard the Osprey.</summary>
		public int BotsAboard
		{
			get
			{
				int count = 0;
				for (int i = 0; i < _botDrops.Count; ++i)
				{
					count += _botDrops[i].Aboard;
				}
				return count;
			}
		}

		// PRIVATE MEMBERS

		private byte _nextBotNumber = 1;
		private readonly List<BotPlacement> _botPlacements = new List<BotPlacement>(32);
		private readonly List<BotDrop>      _botDrops      = new List<BotDrop>(16);

		// PUBLIC METHODS

		public int BotCount
		{
			get
			{
				int count = 0;
				foreach (Agent agent in GetDummies())
				{
					if (agent.BotNumber > 0 && agent.Object != null && agent.Object.IsValid == true)
					{
						++count;
					}
				}
				return count;
			}
		}

		/// <summary>Server: a bot on the given team at a point (snapped to the NavMesh when there is one).</summary>
		public Agent SpawnBot(Vector3 position, Quaternion rotation, byte team)
		{
			if (Runner.IsServer == false)
				return null;

			if (BotNavigation.Sample(position, 6.0f, out Vector3 onMesh) == true)
			{
				position = onMesh;
			}

			return SpawnDummy(position + Vector3.up * 0.05f, rotation, team, TakeBotNumber());
		}

		/// <summary>Server: two bots on a new team, side by side at a point.</summary>
		public void SpawnBotDuo(Vector3 position, Quaternion rotation, List<Agent> spawned = null)
		{
			SpawnBotDuo(NewDummyTeam(), position, rotation, spawned);
		}

		public IEnumerable<Agent> GetBots()
		{
			foreach (Agent agent in GetDummies())
			{
				if (agent.BotNumber > 0 && agent.Object != null && agent.Object.IsValid == true)
					yield return agent;
			}
		}

		/// <summary>
		/// Server, at match start: bot duos spread over the map's POIs and cabin areas. Every area gets one duo (POIs before cabins, bigger
		/// first) before any gets a second, and none gets more than BotSettings.MaxDuosPerArea; each duo's spot is its own loot spot in the
		/// area. Without a drop path they stand there on the ground. With one (phase 11f), only areas within BotSettings.DropReach of the
		/// Osprey's path are used (more duos than room there: the extras go to those areas again) and each duo is planned to ride the
		/// Osprey and jump for its spot (BotDrops, UpdateBotDrops). Without map areas (a generated level), on the ground at loot points
		/// spread across the level. Returns the new teams.
		/// </summary>
		public List<byte> PlaceBotDuos(int duos, int keepFree = 0, DropPath? path = null)
		{
			var teams = new List<byte>(duos);
			_botPlacements.Clear();
			_botDrops.Clear();
			_nextBotNumber = 1;

			if (Runner.IsServer == false || duos <= 0)
				return teams;

			BotSettings settings = BotSettings.Current;
			BrownoutMap map      = BrownoutMap.Get(Runner.SimulationUnityScene);
			var         used     = new List<Vector3>(duos + 2);

			if (map != null && map.Areas.Count > 0)
			{
				var order = new List<int>(map.Areas.Count);
				for (int i = 0; i < map.Areas.Count; ++i)
				{
					if (path.HasValue == true && path.Value.DistanceTo(new Vector3(map.Areas[i].Center.x, 0.0f, map.Areas[i].Center.y)) > settings.DropReach)
						continue;

					order.Add(i);
				}

				if (order.Count == 0)
				{
					// Nothing in reach (shouldn't happen on our map): every area, the jumpers get as close as they can
					for (int i = 0; i < map.Areas.Count; ++i)
					{
						order.Add(i);
					}
				}

				order.Sort((a, b) =>
				{
					BrownoutMap.Area x = map.Areas[a];
					BrownoutMap.Area y = map.Areas[b];
					if (x.IsCabins != y.IsCabins)
						return x.IsCabins == true ? 1 : -1;
					return (y.Size.x * y.Size.y).CompareTo(x.Size.x * x.Size.y);
				});

				// Areas kept free of duos for lone players' bot teammates (random cabin areas first), so a teammate doesn't start 1 against 2
				for (int i = 0; i < keepFree && order.Count > 1; ++i)
				{
					int cabins = 0;
					for (int k = 0; k < order.Count; ++k)
					{
						cabins += map.Areas[order[k]].IsCabins == true ? 1 : 0;
					}

					int pick = cabins > 0 ? order.Count - 1 - Random.Range(0, cabins) : Random.Range(0, order.Count);
					order.RemoveAt(pick);
				}

				int perArea = Mathf.Max(1, settings.MaxDuosPerArea);
				int rounds  = path.HasValue == true ? perArea + 3 : perArea;
				for (int round = 0; round < rounds && teams.Count < duos; ++round)
				{
					for (int k = 0; k < order.Count && teams.Count < duos; ++k)
					{
						// Each round goes POIs first, so the bigger POIs get the second duos
						BrownoutMap.Area area = map.Areas[order[k]];
						if (FindAreaSpot(area, used, settings.DuoSpacing, out Vector3 spot) == false)
							continue;

						// A big area can stick out of reach: another spot in it, else the nearest of those tried
						for (int retry = 0; retry < 3 && path.HasValue == true && path.Value.DistanceTo(spot) > settings.DropReach; ++retry)
						{
							if (FindAreaSpot(area, used, settings.DuoSpacing, out Vector3 other) == true && path.Value.DistanceTo(other) < path.Value.DistanceTo(spot))
							{
								spot = other;
							}
						}

						byte team = NewDummyTeam();
						if (path.HasValue == true)
						{
							PlanBotDrop(team, area.Name, spot, path.Value, 2, PlayerRef.None);
						}
						else
						{
							SpawnBotDuo(team, spot, Quaternion.Euler(0.0f, Random.Range(0.0f, 360.0f), 0.0f), null);
						}
						used.Add(spot);
						teams.Add(team);
						_botPlacements.Add(new BotPlacement { Team = team, Area = area.Name, Position = spot });
					}
				}
			}
			else
			{
				// A generated level: loot points spread out
				IReadOnlyList<LootSpawner.Point> points = LootSpawner.GetPoints(Runner);
				for (int i = 0; i < points.Count * 2 && teams.Count < duos && points.Count > 0; ++i)
				{
					Vector3 spot = points[Random.Range(0, points.Count)].Position;
					if (IsFarFrom(used, spot, 60.0f) == false || BotNavigation.Sample(spot, 4.0f, out spot) == false)
						continue;

					byte team = NewDummyTeam();
					SpawnBotDuo(team, spot, Quaternion.Euler(0.0f, Random.Range(0.0f, 360.0f), 0.0f), null);
					used.Add(spot);
					teams.Add(team);
					_botPlacements.Add(new BotPlacement { Team = team, Area = "level", Position = spot });
				}
			}

			return teams;
		}

		/// <summary>
		/// Server, at match start: a bot on a lone player's team. It spawns at a POI or cabin area with the fewest bots, like the others,
		/// doesn't drop with the player, and walks to them once they've landed.
		/// </summary>
		public Agent SpawnBotTeammate(byte team)
		{
			if (Runner.IsServer == false)
				return null;

			BotSettings settings = BotSettings.Current;
			BrownoutMap map      = BrownoutMap.Get(Runner.SimulationUnityScene);
			var         used     = new List<Vector3>(_botPlacements.Count);
			foreach (BotPlacement placement in _botPlacements)
			{
				used.Add(placement.Position);
			}

			Vector3 spot = default;
			string  name = string.Empty;
			bool    found = false;

			if (map != null && map.Areas.Count > 0)
			{
				// The area with the fewest bots, picked at random among those
				int fewest = int.MaxValue;
				var candidates = new List<int>();
				for (int i = 0; i < map.Areas.Count; ++i)
				{
					int count = 0;
					foreach (BotPlacement placement in _botPlacements)
					{
						if (placement.Area == map.Areas[i].Name)
						{
							++count;
						}
					}

					if (count < fewest)
					{
						fewest = count;
						candidates.Clear();
					}

					if (count == fewest)
					{
						candidates.Add(i);
					}
				}

				for (int attempt = 0; attempt < candidates.Count && found == false; ++attempt)
				{
					BrownoutMap.Area area = map.Areas[candidates[(attempt + Random.Range(0, candidates.Count)) % candidates.Count]];
					found = FindAreaSpot(area, used, settings.DuoSpacing, out spot, true);
					name  = area.Name;
				}
			}

			if (found == false)
			{
				Transform spawnPoint = GetRandomSpawnPoint(50.0f);
				spot  = spawnPoint != null ? spawnPoint.position : Vector3.zero;
				name  = "spawn point";
			}

			Agent bot = SpawnBot(spot, Quaternion.Euler(0.0f, Random.Range(0.0f, 360.0f), 0.0f), team);
			_botPlacements.Add(new BotPlacement { Team = team, Area = name, Position = spot, Teammate = true });
			return bot;
		}

		/// <summary>
		/// Phase 11f, server, at match start: a lone player's bot teammate rides the Osprey with them, jumps when they do and follows them
		/// down (BotBrain.SetDrop). Its spot (for the logs) is under the path's middle.
		/// </summary>
		public void PlanBotTeammateDrop(byte team, PlayerRef human, DropPath path)
		{
			if (Runner.IsServer == false)
				return;

			Vector3 middle = path.Start + path.Direction * ((path.OverMapFrom + path.OverMapTo) * 0.5f);
			BotDrop drop   = PlanBotDrop(team, "with its player", middle, path, 1, human);
			drop.Teammate  = true;

			_botPlacements.Add(new BotPlacement { Team = team, Area = drop.Area, Position = middle, Teammate = true });
		}

		/// <summary>Phase 11f, server: a player jumped; their bot teammate goes after them.</summary>
		public void OnHumanJumped(PlayerRef human)
		{
			for (int i = 0; i < _botDrops.Count; ++i)
			{
				if (_botDrops[i].Teammate == true && _botDrops[i].Human == human)
				{
					_botDrops[i].HumanJumped = true;
				}
			}
		}

		/// <summary>
		/// Phase 11f, server, every tick of the flight: bots whose jump point the Osprey has passed (over the map) jump, a duo's second
		/// BotSettings.DropPairGap after the first; a lone player's bot right after its player. pushOut (past the far edge or the Osprey
		/// gone): everyone left aboard jumps now. Returns how many jumped.
		/// </summary>
		public int UpdateBotDrops(Airplane airplane, DropPath path, bool overMap, bool pushOut)
		{
			if (Runner.IsServer == false || airplane == null || _botDrops.Count == 0)
				return 0;

			float along  = path.Along(airplane.transform.position);
			int   jumped = 0;

			for (int i = 0; i < _botDrops.Count; ++i)
			{
				BotDrop drop = _botDrops[i];

				while (drop.Aboard > 0)
				{
					bool due = drop.Teammate == true ? drop.HumanJumped == true : overMap == true && along >= drop.JumpAlong;
					if (due == false && pushOut == false)
						break;
					if (drop.Jumped > 0 && Runner.Tick < drop.NextJumpTick && pushOut == false)
						break;

					JumpBot(airplane, drop);
					++jumped;

					if (pushOut == false)
						break;
				}
			}

			return jumped;
		}

		// PRIVATE METHODS

		private byte TakeBotNumber()
		{
			byte number = _nextBotNumber;
			_nextBotNumber = (byte)(_nextBotNumber >= 250 ? 1 : _nextBotNumber + 1);
			return number;
		}

		/// <summary>Phase 11f: a team that rides the Osprey and jumps where the path passes closest to its spot (less BotSettings.DropJumpLead).</summary>
		private BotDrop PlanBotDrop(byte team, string area, Vector3 spot, DropPath path, int bots, PlayerRef human)
		{
			float jumpAlong = Mathf.Clamp(path.Along(spot) - BotSettings.Current.DropJumpLead, path.OverMapFrom + 5.0f, Mathf.Max(path.OverMapFrom + 5.0f, path.OverMapTo - 5.0f));

			var drop = new BotDrop
			{
				Team      = team,
				Area      = area,
				Target    = spot,
				JumpAlong = jumpAlong,
				Numbers   = new byte[bots],
				Human     = human,
			};

			for (int i = 0; i < bots; ++i)
			{
				drop.Numbers[i] = TakeBotNumber();
			}

			_botDrops.Add(drop);
			return drop;
		}

		/// <summary>Phase 11f: the drop's next bot spawns at the Osprey's door and jumps (like a player's jump), then flies for its spot.</summary>
		private void JumpBot(Airplane airplane, BotDrop drop)
		{
			int        index    = drop.Jumped;
			Vector3    door     = airplane.AgentPosition.position;
			Vector3    toSpot   = (drop.Target - door).OnlyXZ();
			Quaternion rotation = Quaternion.LookRotation(toSpot.sqrMagnitude > 1.0f ? toSpot.normalized : airplane.transform.forward.OnlyXZ().normalized);

			drop.Jumped       = index + 1;
			drop.NextJumpTick = Runner.Tick + Mathf.Max(1, Mathf.CeilToInt(BotSettings.Current.DropPairGap / Runner.DeltaTime));

			Agent bot = SpawnDummy(door, rotation, drop.Team, drop.Numbers[index]);
			if (bot == null)
				return;

			bot.Movement.StartDrop(airplane.Velocity + Vector3.down * MatchSettings.Current.JumpOutSpeed);

			// The second bot of a duo aims a few meters to the side of the first, so they don't land on each other
			Vector3 side   = Vector3.Cross(Vector3.up, toSpot.sqrMagnitude > 1.0f ? toSpot.normalized : Vector3.forward) * (index == 0 ? 0.0f : 4.0f);
			Agent   follow = null;
			if (drop.Teammate == true)
			{
				Player human = Context.NetworkGame.GetPlayer(drop.Human);
				follow = human != null ? human.ActiveAgent : null;
			}

			if (bot.Bot != null)
			{
				bot.Bot.SetDrop(drop.Target + side, follow);
			}

			MatchDebug.Log(Runner, $"JUMP {bot.BotName} (bot, team {drop.Team}) at {door.ToString("F0")} for {(drop.Teammate ? "its player" : $"{drop.Area} {drop.Target.ToString("F0")}")}, {(drop.Target - door).OnlyXZ().magnitude:F0} m away");
		}

		private void SpawnBotDuo(byte team, Vector3 position, Quaternion rotation, List<Agent> spawned)
		{
			Vector3 side = rotation * Vector3.right * 1.5f;

			Agent a = SpawnBot(position - side, rotation, team);
			Agent b = SpawnBot(position + side, rotation, team);

			if (spawned != null)
			{
				if (a != null) { spawned.Add(a); }
				if (b != null) { spawned.Add(b); }
			}
		}

		/// <summary>
		/// A loot spot in the area (on the NavMesh and connected to the rest of the map, so never in the sealed Power Plant underground), at
		/// least spacing from the spots already used, or the one farthest from them; else open ground near the area's center.
		/// </summary>
		private bool FindAreaSpot(BrownoutMap.Area area, List<Vector3> used, float spacing, out Vector3 spot, bool farthest = false)
		{
			IReadOnlyList<LootSpawner.Point> points = LootSpawner.GetPoints(Runner);
			var inside = new List<Vector3>();

			for (int i = 0; i < points.Count; ++i)
			{
				Vector3 p = points[i].Position;
				if (Mathf.Abs(p.x - area.Center.x) <= area.Size.x * 0.5f + 5.0f && Mathf.Abs(p.z - area.Center.y) <= area.Size.y * 0.5f + 5.0f)
				{
					inside.Add(p);
				}
			}

			// Shuffled, so each match puts them somewhere new
			for (int i = inside.Count - 1; i > 0; --i)
			{
				int     j    = Random.Range(0, i + 1);
				Vector3 swap = inside[i];
				inside[i] = inside[j];
				inside[j] = swap;
			}

			if (farthest == true)
			{
				float   best      = -1.0f;
				Vector3 bestSpot  = default;
				for (int i = 0; i < inside.Count; ++i)
				{
					float nearest = NearestDistance(used, inside[i]);
					if (nearest > best && BotNavigation.Sample(inside[i], 3.0f, out Vector3 onMesh) == true && IsConnected(onMesh) == true)
					{
						best     = nearest;
						bestSpot = onMesh;
					}
				}

				if (best >= 0.0f)
				{
					spot = bestSpot;
					return true;
				}
			}

			for (float space = spacing; space >= 4.0f; space *= 0.5f)
			{
				for (int i = 0; i < inside.Count; ++i)
				{
					if (IsFarFrom(used, inside[i], space) == true && BotNavigation.Sample(inside[i], 3.0f, out spot) == true && IsConnected(spot) == true)
						return true;
				}
			}

			for (int i = 0; i < 30; ++i)
			{
				Vector2 offset = Random.insideUnitCircle * Mathf.Min(area.Size.x, area.Size.y) * 0.4f;
				Vector3 p      = new Vector3(area.Center.x + offset.x, 0.0f, area.Center.y + offset.y);

				if (Runner.GetPhysicsScene().Raycast(new Vector3(p.x, 400.0f, p.z), Vector3.down, out RaycastHit hit, 600.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
				{
					p.y = hit.point.y;
				}

				if (IsFarFrom(used, p, spacing * 0.5f) == true && BotNavigation.Sample(p, 6.0f, out spot) == true && IsConnected(spot) == true)
					return true;
			}

			spot = default;
			return false;
		}

		/// <summary>The NavMesh has a whole path from this point to the map's center (the rest of the map).</summary>
		private bool IsConnected(Vector3 point)
		{
			if (_connectionPath == null)
			{
				_connectionPath = new UnityEngine.AI.NavMeshPath();
			}

			if (_connectionTarget.HasValue == false)
			{
				Vector3 center = Vector3.zero;
				if (Runner.GetPhysicsScene().Raycast(new Vector3(0.0f, 400.0f, 0.0f), Vector3.down, out RaycastHit hit, 600.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
				{
					center = hit.point;
				}

				if (BotNavigation.Sample(center, 40.0f, out Vector3 onMesh) == false)
					return true;   // No NavMesh to judge by

				_connectionTarget = onMesh;
			}

			return BotNavigation.CanReach(point, _connectionTarget.Value, _connectionPath, 3.0f);
		}

		private static float NearestDistance(List<Vector3> used, Vector3 point)
		{
			float nearest = float.MaxValue;
			for (int i = 0; i < used.Count; ++i)
			{
				nearest = Mathf.Min(nearest, Vector3.Distance(used[i], point));
			}
			return nearest;
		}

		private UnityEngine.AI.NavMeshPath _connectionPath;
		private Vector3?                   _connectionTarget;

		private static bool IsFarFrom(List<Vector3> used, Vector3 point, float distance)
		{
			for (int i = 0; i < used.Count; ++i)
			{
				if ((used[i] - point).sqrMagnitude < distance * distance)
					return false;
			}

			return true;
		}
	}
}
