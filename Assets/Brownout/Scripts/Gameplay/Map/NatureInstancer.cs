using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TPSBR
{
	/// <summary>
	/// Draws the map's terrain trees, bushes and small rocks with GPU instancing (phase 8a). Unity's terrain draws LOD-group trees one by
	/// one with per-tree CPU work every frame, which took the 2-peer editor test from 56 to 26 fps; here they're grouped in square chunks,
	/// culled by the camera and by distance, and each chunk picks LOD0 or LOD1 by its distance. The terrain keeps the trees for their trunk
	/// colliders (its own tree drawing distance is 0). Distances in BrownoutMapSettings. Only cameras in this scene draw them, so each
	/// Multipeer peer draws its own copy into its own view.
	/// </summary>
	[ExecuteAlways]
	[RequireComponent(typeof(Terrain))]
	public sealed class NatureInstancer : MonoBehaviour
	{
		// PUBLIC MEMBERS

		/// <summary>Editor (the map picture): draw every instance at its far LOD regardless of distance.</summary>
		public static bool DrawEverything;

		public int InstanceCount => _instanceCount;

		// PRIVATE MEMBERS

		private const float ChunkSize = 64.0f;
		private const int   BatchSize = 1023;

		private sealed class Lod
		{
			public Mesh       Mesh;
			public Material[] Materials;
			public bool       CastShadows;
		}

		private sealed class Prototype
		{
			public Lod[]  Lods;
			public float  DrawDistance;
			public float  LodDistance;
		}

		private sealed class Chunk
		{
			public Bounds              Bounds;
			public List<Matrix4x4>[]   Instances; // Per prototype
		}

		private Terrain           _terrain;
		private Prototype[]       _prototypes;
		private List<Chunk>       _chunks = new List<Chunk>();
		private List<Matrix4x4>[,] _batches; // [prototype, lod]
		private Matrix4x4[]       _buffer = new Matrix4x4[BatchSize];
		private Plane[]           _planes = new Plane[6];
		private int               _instanceCount;
		private int               _builtFor = -1;

		// PUBLIC METHODS

		/// <summary>Reads the terrain's tree instances again (after a map build).</summary>
		public void Rebuild()
		{
			_builtFor = -1;
			Build();
		}

		// MONOBEHAVIOUR

		private void OnEnable()
		{
			_terrain = GetComponent<Terrain>();
			RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
		}

		private void OnDisable()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
		}

		// PRIVATE METHODS

		private void Build()
		{
			TerrainData data = _terrain != null ? _terrain.terrainData : null;
			if (data == null)
				return;

			int signature = data.treeInstanceCount * 31 + data.treePrototypes.Length;
			if (_builtFor == signature && _prototypes != null)
				return;

			_builtFor = signature;
			_chunks.Clear();
			_instanceCount = 0;

			BrownoutMapSettings settings = BrownoutMapSettings.Current;
			TreePrototype[] prototypes = data.treePrototypes;
			_prototypes = new Prototype[prototypes.Length];

			for (int i = 0; i < prototypes.Length; ++i)
			{
				_prototypes[i] = MakePrototype(prototypes[i].prefab, settings);
			}

			_batches = new List<Matrix4x4>[prototypes.Length, 2];
			for (int i = 0; i < prototypes.Length; ++i)
			{
				_batches[i, 0] = new List<Matrix4x4>(1024);
				_batches[i, 1] = new List<Matrix4x4>(1024);
			}

			Vector3 size   = data.size;
			Vector3 origin = _terrain.transform.position;
			int     cells  = Mathf.CeilToInt(size.x / ChunkSize);
			var     grid   = new Chunk[cells, cells];

			foreach (TreeInstance instance in data.treeInstances)
			{
				if (instance.prototypeIndex < 0 || instance.prototypeIndex >= _prototypes.Length || _prototypes[instance.prototypeIndex] == null)
					continue;

				Vector3 position = origin + Vector3.Scale(instance.position, size);
				position.y = origin.y + data.GetInterpolatedHeight(instance.position.x, instance.position.z);

				int cx = Mathf.Clamp((int)(instance.position.x * cells), 0, cells - 1);
				int cz = Mathf.Clamp((int)(instance.position.z * cells), 0, cells - 1);

				Chunk chunk = grid[cz, cx];
				if (chunk == null)
				{
					chunk = new Chunk
					{
						Bounds    = new Bounds(position, Vector3.one),
						Instances = new List<Matrix4x4>[_prototypes.Length],
					};
					grid[cz, cx] = chunk;
					_chunks.Add(chunk);
				}

				List<Matrix4x4> list = chunk.Instances[instance.prototypeIndex];
				if (list == null)
				{
					list = new List<Matrix4x4>(64);
					chunk.Instances[instance.prototypeIndex] = list;
				}

				var scale = new Vector3(instance.widthScale, instance.heightScale, instance.widthScale);
				list.Add(Matrix4x4.TRS(position, Quaternion.Euler(0.0f, instance.rotation * Mathf.Rad2Deg, 0.0f), scale));

				chunk.Bounds.Encapsulate(position);
				chunk.Bounds.Encapsulate(position + Vector3.up * 10.0f * instance.heightScale);
				++_instanceCount;
			}

			// Room for crowns and rocks reaching past their centers
			foreach (Chunk chunk in _chunks)
			{
				chunk.Bounds.Expand(new Vector3(8.0f, 2.0f, 8.0f));
			}
		}

		private static Prototype MakePrototype(GameObject prefab, BrownoutMapSettings settings)
		{
			if (prefab == null)
				return null;

			var lods = new List<Lod>(2);
			LODGroup group = prefab.GetComponent<LODGroup>();

			if (group != null)
			{
				foreach (LOD lod in group.GetLODs())
				{
					Renderer renderer = lod.renderers != null && lod.renderers.Length > 0 ? lod.renderers[0] : null;
					MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
					if (filter != null && filter.sharedMesh != null)
					{
						lods.Add(new Lod { Mesh = filter.sharedMesh, Materials = renderer.sharedMaterials, CastShadows = renderer.shadowCastingMode != ShadowCastingMode.Off });
					}
				}
			}
			else
			{
				MeshFilter filter = prefab.GetComponentInChildren<MeshFilter>();
				Renderer renderer = filter != null ? filter.GetComponent<Renderer>() : null;
				if (filter != null && filter.sharedMesh != null)
				{
					lods.Add(new Lod { Mesh = filter.sharedMesh, Materials = renderer.sharedMaterials, CastShadows = renderer.shadowCastingMode != ShadowCastingMode.Off });
				}
			}

			if (lods.Count == 0)
				return null;

			// Small things (bushes, small rocks) aren't drawn as far as trees
			float height = lods[0].Mesh.bounds.size.y;
			float draw   = height < 2.0f ? settings.SmallNatureDrawDistance : settings.TreeDrawDistance;

			return new Prototype
			{
				Lods         = lods.ToArray(),
				DrawDistance = draw,
				LodDistance  = Mathf.Min(settings.TreeLodDistance, draw),
			};
		}

		private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
		{
			if (_terrain == null || isActiveAndEnabled == false)
				return;

			// Only this scene's cameras (each Multipeer peer has its own), the editor's views, and only cameras that draw the default layer
			bool editorView = camera.cameraType == CameraType.SceneView || camera.cameraType == CameraType.Preview;
			if (editorView == false && camera.gameObject.scene != gameObject.scene)
				return;
			if ((camera.cullingMask & (1 << gameObject.layer)) == 0)
				return;

			Build();
			if (_prototypes == null)
				return;

			Vector3 eye = camera.transform.position;
			GeometryUtility.CalculateFrustumPlanes(camera, _planes);

			for (int p = 0; p < _prototypes.Length; ++p)
			{
				_batches[p, 0].Clear();
				_batches[p, 1].Clear();
			}

			foreach (Chunk chunk in _chunks)
			{
				if (GeometryUtility.TestPlanesAABB(_planes, chunk.Bounds) == false)
					continue;

				float distance = Mathf.Sqrt(chunk.Bounds.SqrDistance(eye));

				for (int p = 0; p < _prototypes.Length; ++p)
				{
					List<Matrix4x4> instances = chunk.Instances[p];
					Prototype prototype = _prototypes[p];
					if (instances == null || prototype == null)
						continue;

					if (DrawEverything == false && distance > prototype.DrawDistance)
						continue;

					int lod = DrawEverything == true || distance > prototype.LodDistance ? Mathf.Min(1, prototype.Lods.Length - 1) : 0;
					_batches[p, lod].AddRange(instances);
				}
			}

			for (int p = 0; p < _prototypes.Length; ++p)
			{
				Prototype prototype = _prototypes[p];
				if (prototype == null)
					continue;

				for (int lod = 0; lod < 2 && lod < prototype.Lods.Length; ++lod)
				{
					Draw(camera, prototype.Lods[lod], _batches[p, lod]);
				}
			}
		}

		private void Draw(Camera camera, Lod lod, List<Matrix4x4> instances)
		{
			if (instances.Count == 0)
				return;

			for (int start = 0; start < instances.Count; start += BatchSize)
			{
				int count = Mathf.Min(BatchSize, instances.Count - start);
				instances.CopyTo(start, _buffer, 0, count);

				for (int submesh = 0; submesh < lod.Mesh.subMeshCount && submesh < lod.Materials.Length; ++submesh)
				{
					var parameters = new RenderParams(lod.Materials[submesh])
					{
						camera            = camera,
						layer             = gameObject.layer,
						shadowCastingMode = lod.CastShadows == true ? ShadowCastingMode.On : ShadowCastingMode.Off,
						receiveShadows    = true,
						worldBounds       = new Bounds(camera.transform.position, Vector3.one * 4000.0f),
					};

					Graphics.RenderMeshInstanced(parameters, lod.Mesh, submesh, _buffer, count);
				}
			}
		}
	}
}
