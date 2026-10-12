using UnityEngine;
using UnityEngine.AI;

namespace TPSBR
{
	/// <summary>
	/// A bot's path on the map's NavMesh (phase 9). Paths come from NavMesh.CalculatePath (no NavMeshAgent: the KCC moves the bot, the
	/// brain only steers its input toward the next corner). Off the mesh (no NavMesh baked, or standing somewhere it doesn't cover) it
	/// steers straight at the destination. Watches its own progress and reports when it's stuck.
	/// </summary>
	public sealed class BotNavigation
	{
		// PUBLIC MEMBERS

		public bool    HasDestination => _hasDestination;
		public Vector3 Destination    => _destination;
		public bool    HasPath        => _cornerCount > 0;
		public int     CornerCount    => _cornerCount;
		public Vector3 GetCorner(int i) => _corners[i];
		public int     CornerIndex    => _cornerIndex;
		/// <summary>How many times in a row it got stuck on the current destination.</summary>
		public int     StuckCount     => _stuckCount;

		// PRIVATE MEMBERS

		private readonly NavMeshPath _path    = new NavMeshPath();
		private readonly Vector3[]   _corners = new Vector3[256];

		private int     _cornerCount;
		private int     _cornerIndex;
		private bool    _hasDestination;
		private Vector3 _destination;
		private Vector3 _pathDestination;
		private float   _pathTime;
		private bool    _pathFailed;
		private bool    _partial;

		private Vector3 _progressPosition;
		private float   _progressTime;
		private int     _stuckCount;

		// PUBLIC METHODS

		/// <summary>A point on the mesh near this one (within range), or false.</summary>
		public static bool Sample(Vector3 point, float range, out Vector3 onMesh)
		{
			if (NavMesh.SamplePosition(point, out NavMeshHit hit, range, NavMesh.AllAreas) == true)
			{
				onMesh = hit.position;
				return true;
			}

			onMesh = point;
			return false;
		}

		/// <summary>
		/// True when a full path exists between the two points (both snapped to the mesh). Long paths stop partway at the pathfinder's search
		/// limit, so it carries on from where each one stops, a few times.
		/// </summary>
		public static bool CanReach(Vector3 from, Vector3 to, NavMeshPath path, float targetRange = 3.0f)
		{
			if (Sample(from, 3.0f, out Vector3 a) == false || Sample(to, targetRange, out Vector3 b) == false)
				return false;

			for (int leg = 0; leg < 8; ++leg)
			{
				if (NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) == false || path.status == NavMeshPathStatus.PathInvalid)
					return false;
				if (path.status == NavMeshPathStatus.PathComplete)
					return true;

				Vector3[] corners = path.corners;
				Vector3   end     = corners.Length > 0 ? corners[corners.Length - 1] : a;
				if ((end - a).sqrMagnitude < 1.0f)
					return false;

				a = end;
			}

			return false;
		}

		/// <summary>Heads for a point. A new path is made when the point moves more than a couple of meters, or when forced.</summary>
		public void SetDestination(Vector3 position, Vector3 destination, float now, bool force = false)
		{
			// Measured from where the current path goes, not from last tick's point: a target that keeps moving (a partner, a fight) drifts
			// a little each tick, and the path must follow it, or the bot reaches the old end and stands there until the timed re-plan
			bool moved = _hasDestination == false || (destination - _pathDestination).sqrMagnitude > 4.0f;

			if (moved == true)
			{
				_stuckCount = 0;
			}

			_hasDestination = true;
			_destination    = destination;

			if (moved == true || force == true || now - _pathTime > BotSettings.Current.RepathTime)
			{
				Repath(position, now);
			}
		}

		public void Clear()
		{
			_hasDestination = false;
			_cornerCount    = 0;
			_cornerIndex    = 0;
			_stuckCount     = 0;
		}

		/// <summary>Horizontal distance to the destination.</summary>
		public float DistanceToDestination(Vector3 position)
		{
			if (_hasDestination == false)
				return 0.0f;

			Vector3 offset = _destination - position;
			offset.y = 0.0f;
			return offset.magnitude;
		}

		/// <summary>The world direction (flat, length 0..1) to move this tick, following the path's corners.</summary>
		public Vector3 Steer(Vector3 position)
		{
			if (_hasDestination == false)
				return Vector3.zero;

			Vector3 target = _destination;

			if (_cornerCount > 0)
			{
				float reach = BotSettings.Current.CornerReach;

				while (_cornerIndex < _cornerCount - 1)
				{
					Vector3 toCorner = _corners[_cornerIndex] - position;
					toCorner.y = 0.0f;
					if (toCorner.sqrMagnitude > reach * reach)
						break;

					++_cornerIndex;
				}

				target = _corners[_cornerIndex];

				// Long paths stop partway (the pathfinder's search limit): carry on from the end of this one
				if (_partial == true && _cornerIndex == _cornerCount - 1)
				{
					Vector3 toEnd = target - position;
					toEnd.y = 0.0f;
					if (toEnd.sqrMagnitude < reach * reach * 4.0f)
					{
						Repath(position, _pathTime);
						if (_cornerCount > 0)
						{
							target = _corners[_cornerIndex];
						}
					}
				}
			}

			Vector3 offset = target - position;
			offset.y = 0.0f;

			float distance = offset.magnitude;
			if (distance < 0.05f)
				return Vector3.zero;

			// Slow down only at the very end
			return offset / distance * Mathf.Clamp01(distance / 0.5f);
		}

		/// <summary>
		/// Called every tick while it wants to move: true once when it hasn't made StuckDistance of progress in StuckTime.
		/// </summary>
		public bool UpdateStuck(Vector3 position, float now, bool wantsToMove)
		{
			BotSettings settings = BotSettings.Current;

			if (wantsToMove == false || (position - _progressPosition).sqrMagnitude > settings.StuckDistance * settings.StuckDistance)
			{
				_progressPosition = position;
				_progressTime     = now;
				return false;
			}

			if (now - _progressTime < settings.StuckTime)
				return false;

			_progressPosition = position;
			_progressTime     = now;
			++_stuckCount;
			return true;
		}

		public void Repath(Vector3 position, float now)
		{
			_pathTime        = now;
			_pathDestination = _destination;
			_cornerCount = 0;
			_cornerIndex = 0;
			_pathFailed  = false;

			if (Sample(position, 3.0f, out Vector3 from) == false || Sample(_destination, 6.0f, out Vector3 to) == false)
			{
				_pathFailed = true;
				return;
			}

			if (NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _path) == false || _path.status == NavMeshPathStatus.PathInvalid)
			{
				_pathFailed = true;
				return;
			}

			_cornerCount = _path.GetCornersNonAlloc(_corners);
			_partial     = _path.status == NavMeshPathStatus.PathPartial && _cornerCount > 0 && (_corners[_cornerCount - 1] - to).sqrMagnitude > 4.0f;
			// The first corner is where it stands
			_cornerIndex = _cornerCount > 1 ? 1 : 0;
		}

		/// <summary>The last path request found no way there (no mesh, or the point isn't reachable).</summary>
		public bool PathFailed => _pathFailed;
	}
}
