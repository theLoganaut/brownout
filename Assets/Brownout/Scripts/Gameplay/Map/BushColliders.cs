using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Solid bushes (phase 9, Logan: nobody hides inside a bush). On the map's terrain: when the scene loads it gives every bush (the
	/// terrain trees of the Bush prototype) a standing capsule collider on the player-only layer 15 (RiverWall), so the player's capsule and the
	/// bots' sight checks are stopped by it while bullets, grenades and the grapple pass through the leaves. Made at load time rather
	/// than saved in the scene (8,000+ objects would triple its size); the map builder makes them for a moment to bake the NavMesh.
	/// </summary>
	[RequireComponent(typeof(Terrain))]
	public sealed class BushColliders : MonoBehaviour
	{
		// CONSTANTS

		public const string RootName = "BushColliders";

		private const int   FootprintSamples = 8;
		/// <summary>The ground is sampled this far outside the radius: where a player's capsule stands when it touches the bush.</summary>
		private const float FootprintReach   = 0.5f;

		// PRIVATE MEMBERS

		[SerializeField, Tooltip("The bush prototypes' indices in the terrain's tree prototypes (phase 10: Bush, Bush_02, Bush_03).")]
		private int[] _bushPrototypes = { 3, 5, 6 };

		private Transform _root;

		// PUBLIC MEMBERS

		public Transform Root => _root;
		public int       Count => _root != null ? _root.childCount : 0;

		// PUBLIC METHODS

		/// <summary>Makes the colliders (again). Returns how many.</summary>
		public int Build()
		{
			Clear();

			Terrain terrain = GetComponent<Terrain>();
			TerrainData data = terrain != null ? terrain.terrainData : null;
			if (data == null)
				return 0;

			BrownoutMapSettings settings = BrownoutMapSettings.Current;
			int layer = Agent.RiverWallLayer;
			if (layer < 0)
				return 0;
			Vector3 origin = terrain.transform.position;

			_root = new GameObject(RootName).transform;
			_root.SetParent(transform, false);
			_root.gameObject.layer = layer;

			// One object per 64 m chunk, a capsule per bush, so there are a few hundred objects instead of thousands
			var chunks = new System.Collections.Generic.Dictionary<Vector2Int, GameObject>(512);
			int count = 0;

			foreach (TreeInstance instance in data.treeInstances)
			{
				if (System.Array.IndexOf(_bushPrototypes, instance.prototypeIndex) < 0)
					continue;

				Vector3 position = origin + Vector3.Scale(instance.position, data.size);
				float   radius   = settings.BushColliderRadius * Mathf.Max(instance.widthScale, instance.heightScale);

				// A standing capsule from the lowest ground under the bush to the height a flat-ground sphere would reach above the highest
				// ground. A sphere set by the ground at its middle is a low dome from the uphill side of a steep slope, and players walked over it
				float low  = float.MaxValue;
				float high = float.MinValue;
				for (int i = 0; i <= FootprintSamples; ++i)
				{
					Vector3 sample = position;
					if (i > 0)
					{
						float angle = (i - 1) * Mathf.PI * 2.0f / FootprintSamples;
						sample += new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * (radius + FootprintReach);
					}

					float ground = terrain.SampleHeight(sample) + origin.y;
					low  = Mathf.Min(low, ground);
					high = Mathf.Max(high, ground);
				}

				float bottom = low - 0.1f;
				float top    = high + settings.BushColliderHeight * instance.heightScale + radius;
				position.y = (bottom + top) * 0.5f;

				var key = new Vector2Int(Mathf.FloorToInt(position.x / 64.0f), Mathf.FloorToInt(position.z / 64.0f));
				if (chunks.TryGetValue(key, out GameObject chunk) == false)
				{
					chunk = new GameObject($"Chunk {key.x} {key.y}");
					chunk.layer = layer;
					chunk.transform.SetParent(_root, false);
					chunk.transform.position = new Vector3(key.x * 64.0f, 0.0f, key.y * 64.0f);
					chunks.Add(key, chunk);
				}

				var capsule = chunk.AddComponent<CapsuleCollider>();
				capsule.direction = 1;
				capsule.center    = position - chunk.transform.position;
				capsule.radius    = radius;
				capsule.height    = Mathf.Max(top - bottom, radius * 2.0f);
				++count;
			}

			Physics.SyncTransforms();
			return count;
		}

		public void Clear()
		{
			Transform existing = transform.Find(RootName);
			while (existing != null)
			{
				DestroyImmediate(existing.gameObject);
				existing = transform.Find(RootName);
			}

			_root = null;
		}

		// MONOBEHAVIOUR

		private void Awake()
		{
			if (Application.isPlaying == true)
			{
				Build();
			}
		}
	}
}
