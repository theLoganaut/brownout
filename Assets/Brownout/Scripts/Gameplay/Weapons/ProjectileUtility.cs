using Fusion;
using System.Collections.Generic;
using UnityEngine;

namespace TPSBR
{
	public static class ProjectileUtility
	{
		public static bool ProjectileCast(NetworkRunner runner, PlayerRef owner, int ownerObjectInstanceID, Vector3 firePosition, Vector3 direction, float raycastDistance, LayerMask hitMask, List<LagCompensatedHit> validHits)
		{
			return ProjectileCast(runner, owner, ownerObjectInstanceID, firePosition, direction, 0f, raycastDistance, hitMask, validHits);
		}

		public static bool ProjectileCast(NetworkRunner runner, PlayerRef owner, int ownerObjectInstanceID, Vector3 firePosition, Vector3 direction, float distanceToTarget, float raycastDistance, LayerMask hitMask, List<LagCompensatedHit> validHits)
		{
			List<LagCompensatedHit> hits = ListPool.Get<LagCompensatedHit>(16);

			int hitCount = runner.LagCompensation.RaycastAll(firePosition, direction, raycastDistance, owner, hits, hitMask, true, HitOptions.IncludePhysX | HitOptions.SubtickAccuracy);
			if (hitCount <= 0)
			{
				ListPool.Return(hits);
				validHits.Clear();
				return false;
			}

			ProcessHits(hits, hitCount, owner, ownerObjectInstanceID, validHits);

			ListPool.Return(hits);

			return validHits.Count > 0;
		}

		public static bool ProjectileCast(NetworkRunner runner, PlayerRef owner, int ownerObjectInstanceID, int fromTick, int toTick, float alpha, Vector3 firePosition, Vector3 direction, float raycastDistance, LayerMask hitMask, List<LagCompensatedHit> validHits)
		{
			return ProjectileCast(runner, owner, ownerObjectInstanceID, fromTick, toTick, alpha, firePosition, direction, 0f, raycastDistance, hitMask, validHits);
		}

		public static bool ProjectileCast(NetworkRunner runner, PlayerRef owner, int ownerObjectInstanceID, int fromTick, int toTick, float alpha, Vector3 firePosition, Vector3 direction, float distanceToTarget, float raycastDistance, LayerMask hitMask, List<LagCompensatedHit> validHits)
		{
			List<LagCompensatedHit> hits = ListPool.Get<LagCompensatedHit>(16);

			int hitCount = runner.LagCompensation.RaycastAll(firePosition, direction, raycastDistance, fromTick, toTick, alpha, hits, hitMask, true, HitOptions.IncludePhysX | HitOptions.SubtickAccuracy);
			if (hitCount <= 0)
			{
				ListPool.Return(hits);
				validHits.Clear();
				return false;
			}

			for (int i = hitCount - 1; i >= 0; --i)
			{
				LagCompensatedHit hit = hits[i];
				if (hit.Hitbox != null &&  hit.Hitbox.Root != null &&  hit.Hitbox.Root.Object != null &&  hit.Hitbox.Root.Object.InputAuthority == owner)
				{
					hits.RemoveAt(i);
					--hitCount;
				}
			}

			if (hitCount <= 0)
			{
				ListPool.Return(hits);
				validHits.Clear();
				return false;
			}

			ProcessHits(hits, hitCount, owner, ownerObjectInstanceID, validHits);

			ListPool.Return(hits);

			return validHits.Count > 0;
		}

		private static void ProcessHits(List<LagCompensatedHit> hits, int hitCount, PlayerRef owner, int ownerObjectInstanceID, List<LagCompensatedHit> validHits)
		{
			validHits.Clear();

			var hitRoots = ListPool.Get<int>(16);

			// Brownout: shots start at the eye, which sits inside the shooter's own head hitbox, and nothing is skipped
			// between the eye and the target (the sample's third-person shoot-through-environment allowance is gone).

			Sort(hits, hitCount);

			for (int i = 0; i < hits.Count; i++)
			{
				var hit = hits[i];

				if (IsOwnerHit(hit, owner, ownerObjectInstanceID) == true)
					continue; // Owner was hit

				int hitRootID = hit.Hitbox != null ? hit.Hitbox.Root.gameObject.GetInstanceID() : 0;

				if (hitRootID != 0)
				{
					if (hitRootID == ownerObjectInstanceID)
						continue; // Owner was hit

					if (hitRoots.Contains(hitRootID) == true)
						continue; // Same object was hit multiple times

					hitRoots.Add(hitRootID);
				}

				validHits.Add(hits[i]);
			}

			ListPool.Return(hitRoots);
		}

		private static bool IsOwnerHit(LagCompensatedHit hit, PlayerRef owner, int ownerObjectInstanceID)
		{
			NetworkObject hitObject = null;

			if (hit.Hitbox != null && hit.Hitbox.Root != null)
			{
				if (hit.Hitbox.Root.gameObject.GetInstanceID() == ownerObjectInstanceID)
					return true;

				hitObject = hit.Hitbox.Root.Object;
			}
			else if (hit.GameObject != null)
			{
				hitObject = hit.GameObject.GetComponentInParent<NetworkObject>();
			}

			if (hitObject == null)
				return false;

			if (hitObject.gameObject.GetInstanceID() == ownerObjectInstanceID)
				return true;

			return owner.IsRealPlayer == true && hitObject.InputAuthority == owner && hitObject.GetComponent<Agent>() != null;
		}

		private static void Sort(List<LagCompensatedHit> hits, int maxHits)
		{
			while (true)
			{
				bool swap = false;

				for (int i = 0; i < maxHits; ++i)
				{
					for (int j = i + 1; j < maxHits; ++j)
					{
						if (hits[j].Distance < hits[i].Distance)
						{
							LagCompensatedHit hit = hits[i];
							hits[i] = hits[j];
							hits[j] = hit;

							swap = true;
						}
					}
				}

				if (swap == false)
					return;
			}
		}
	}
}
