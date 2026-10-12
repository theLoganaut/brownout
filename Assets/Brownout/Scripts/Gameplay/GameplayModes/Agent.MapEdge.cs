using Fusion;
using UnityEngine;

namespace TPSBR
{
	/// <summary>The map's square (phase 7): the scene map's center and size, the generated level's until phase 8's map.</summary>
	public static class MapBounds
	{
		public static bool TryGet(SceneContext context, out Vector3 center, out Vector2 halfSize)
		{
			SceneMap map = context != null ? context.Map : null;
			if (map == null || map.WorldDimensions.x <= 0 || map.WorldDimensions.y <= 0)
			{
				center   = default;
				halfSize = default;
				return false;
			}

			center   = map.transform.position;
			halfSize = new Vector2(map.WorldDimensions.x, map.WorldDimensions.y) * 0.5f;
			return true;
		}

		public static bool IsInside(SceneContext context, Vector3 position)
		{
			if (TryGet(context, out Vector3 center, out Vector2 halfSize) == false)
				return true;

			return Mathf.Abs(position.x - center.x) <= halfSize.x && Mathf.Abs(position.z - center.z) <= halfSize.y;
		}
	}

	/// <summary>
	/// Map edge (phase 7, Match Flow & Zone): leaving the map's square in Battle Royale starts a countdown (MatchSettings.EdgeCountdown);
	/// once it runs out you take EdgeDamage a second until you're back. Server only; EdgeOutTick is networked for the HUD's countdown.
	/// </summary>
	public sealed partial class Agent
	{
		// PUBLIC MEMBERS

		/// <summary>The tick this agent left the map, 0 while inside.</summary>
		[Networked, HideInInspector]
		public int EdgeOutTick { get; private set; }

		/// <summary>Seconds left on the countdown (0 once it has run out); -1 while inside the map.</summary>
		public float EdgeTimeLeft
		{
			get
			{
				if (EdgeOutTick <= 0 || Runner == null)
					return -1.0f;

				float elapsed = (Runner.Tick - EdgeOutTick) * Runner.DeltaTime;
				return Mathf.Max(0.0f, MatchSettings.Current.EdgeCountdown - elapsed);
			}
		}

		// PRIVATE MEMBERS

		private int _edgeNextDamageTick;

		// PRIVATE METHODS

		private void UpdateMapEdge()
		{
			if (Context.GameplayMode is BattleRoyaleGameplayMode == false || _health.IsAlive == false)
			{
				EdgeOutTick = 0;
				return;
			}

			if (MapBounds.IsInside(Context, transform.position) == true)
			{
				if (EdgeOutTick > 0)
				{
					MatchDebug.Log(Runner, $"EDGE {name} back on the map");
				}

				EdgeOutTick = 0;
				return;
			}

			MatchSettings settings = MatchSettings.Current;
			int           tick     = Runner.Tick;

			if (EdgeOutTick <= 0)
			{
				EdgeOutTick         = tick;
				_edgeNextDamageTick = tick + Mathf.CeilToInt(settings.EdgeCountdown / Runner.DeltaTime);
				MatchDebug.Log(Runner, $"EDGE {name} left the map at {transform.position.ToString("F0")}, {settings.EdgeCountdown:F0} s to get back");
				return;
			}

			if (tick < _edgeNextDamageTick)
				return;

			_edgeNextDamageTick = tick + Mathf.Max(1, Mathf.RoundToInt(1.0f / Runner.DeltaTime));

			var hitData = new HitData
			{
				Action    = EHitAction.Damage,
				HitType   = EHitType.ShrinkingArea,
				Amount    = settings.EdgeDamage,
				Position  = transform.position,
				Normal    = Vector3.up,
				Target    = _health,
			};

			HitUtility.ProcessHit(ref hitData);
			MatchDebug.Log(Runner, $"EDGE {name} took {settings.EdgeDamage:F0} outside the map, health {_health.CurrentHealth:F0}");
		}
	}
}
