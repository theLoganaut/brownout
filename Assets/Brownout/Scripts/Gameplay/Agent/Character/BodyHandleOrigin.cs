namespace TPSBR
{
	using UnityEngine;

	/// <summary>
	/// Phase 10: marks a handle (weapon, back, belt, grenade) that Build Agent Body moved from the sample's skeleton to the soldier's,
	/// with the sample bone it came from, so a rebuild can put it back first. Holds no runtime behaviour.
	/// </summary>
	[AddComponentMenu("")]
	public sealed class BodyHandleOrigin : MonoBehaviour
	{
		public string SourceBone;
	}
}
