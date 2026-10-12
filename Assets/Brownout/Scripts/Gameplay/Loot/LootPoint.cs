namespace TPSBR
{
	using UnityEngine;

	/// <summary>
	/// A hand-placed loot point (phase 8 places these per POI). It rolls one item from the loot table at match start, on the server.
	/// Generated levels use the sample's item box spots instead (NetworkGame).
	/// </summary>
	public sealed class LootPoint : MonoBehaviour
	{
		[Tooltip("Top-tier areas (Power Plant underground, Factory): better odds of tier 3 armor and trauma kits.")]
		public bool TopTier;

		private void OnDrawGizmos()
		{
			Gizmos.color = TopTier == true ? new Color(0.8f, 0.3f, 1.0f) : new Color(1.0f, 0.85f, 0.2f);
			Gizmos.DrawWireCube(transform.position + Vector3.up * 0.15f, new Vector3(0.5f, 0.3f, 0.5f));
		}
	}
}
