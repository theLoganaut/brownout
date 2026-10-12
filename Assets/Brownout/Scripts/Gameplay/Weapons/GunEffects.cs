namespace TPSBR
{
	using System.Collections.Generic;
	using Fusion;
	using UnityEngine;

	/// <summary>
	/// Phase 11c (gun feel): muzzle smoke, ejected shells and bullet impacts by surface (dust, chips, sparks, splashes, blood). One set of
	/// world-space particle systems per peer, built in code and fed with Emit, so a burst costs no objects. Render only; the numbers and
	/// materials are in FeelSettings. Surfaces come from the collider's tag (Wood, Metal, Dirt, Mud, Water; the terrain is dirt).
	/// </summary>
	public sealed class GunEffects : MonoBehaviour
	{
		// PRIVATE MEMBERS

		private static readonly Dictionary<NetworkRunner, GunEffects> _all = new Dictionary<NetworkRunner, GunEffects>(2);

		private static int _woodTag;
		private static int _metalTag;
		private static int _dirtTag;
		private static int _mudTag;
		private static int _waterTag;

		private NetworkRunner  _runner;
		private ParticleSystem _dust;
		private ParticleSystem _chips;
		private ParticleSystem _sparks;
		private ParticleSystem _smoke;
		private ParticleSystem _shells;
		private ParticleSystem _viewShells;

		// PUBLIC METHODS

		/// <summary>The surface a bullet stops on (sent with the bullet as an int).</summary>
		public static ESurface GetSurface(GameObject hit)
		{
			if (hit == null)
				return ESurface.Concrete;

			if (_woodTag == 0)
			{
				_woodTag  = "Wood".GetHashCode();
				_metalTag = "Metal".GetHashCode();
				_dirtTag  = "Dirt".GetHashCode();
				_mudTag   = "Mud".GetHashCode();
				_waterTag = "Water".GetHashCode();
			}

			int tag = hit.tag.GetHashCode();
			if (tag == _woodTag)  return ESurface.Wood;
			if (tag == _metalTag) return ESurface.Metal;
			if (tag == _dirtTag || tag == _mudTag) return ESurface.Dirt;
			if (tag == _waterTag) return ESurface.Water;

			if (hit.GetComponent<TerrainCollider>() != null)
				return ESurface.Dirt;

			return ESurface.Concrete;
		}

		public static GunEffects Get(NetworkRunner runner)
		{
			if (runner == null || runner.IsRunning == false)
				return null;

			if (_all.TryGetValue(runner, out GunEffects effects) == true && effects != null)
				return effects;

			var go = new GameObject("GunEffects");
			effects = go.AddComponent<GunEffects>();
			effects._runner = runner;
			effects.Build();

			// Renderers exist now, so Multipeer shows them only with their own peer
			runner.MoveToRunnerSceneExtended(go);

			_all[runner] = effects;
			return effects;
		}

		/// <summary>A bullet stopping on a surface: dust along the normal, chips, sparks or a splash.</summary>
		public void Impact(Vector3 position, Vector3 normal, ESurface surface)
		{
			FeelSettings         settings = FeelSettings.Current;
			FeelSettings.Surface look     = settings.GetSurface(surface);
			if (look == null)
				return;

			normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
			Vector3 origin = position + normal * 0.03f;

			var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };

			bool splash = surface == ESurface.Water;
			for (int i = 0; i < look.DustCount; ++i)
			{
				Vector3 direction = Vector3.Slerp(normal, Random.onUnitSphere, splash == true ? 0.25f : 0.45f).normalized;
				if (splash == true)
				{
					direction = (Vector3.up * 2.0f + Random.insideUnitSphere * 0.5f).normalized;
				}
				emit.position      = origin;
				emit.velocity      = direction * Random.Range(0.6f, splash == true ? 5.0f : 2.2f);
				emit.startSize     = look.DustSize * Random.Range(0.6f, 1.3f);
				emit.startLifetime = Random.Range(0.5f, 1.1f);
				emit.startColor    = look.Dust;
				emit.rotation      = Random.Range(0.0f, 360.0f);
				_dust.Emit(emit, 1);
			}

			for (int i = 0; i < look.Chips; ++i)
			{
				emit.position         = origin;
				emit.velocity         = (Vector3.Slerp(normal, Random.onUnitSphere, 0.6f).normalized) * Random.Range(1.5f, 4.5f);
				emit.startSize        = Random.Range(0.012f, 0.03f);
				emit.startLifetime    = Random.Range(0.6f, 1.2f);
				emit.startColor       = look.ChipColor;
				emit.rotation3D       = Random.insideUnitSphere * 180.0f;
				emit.angularVelocity3D = Random.insideUnitSphere * 720.0f;
				_chips.Emit(emit, 1);
			}
			emit.ResetAngularVelocity();
			emit.ResetRotation();

			for (int i = 0; i < look.Sparks; ++i)
			{
				emit.position      = origin;
				emit.velocity      = Vector3.Slerp(Vector3.Reflect(-normal, normal), Random.onUnitSphere, 0.55f).normalized * Random.Range(3.0f, 9.0f);
				emit.startSize     = Random.Range(0.01f, 0.025f);
				emit.startLifetime = Random.Range(0.15f, 0.4f);
				emit.startColor    = new Color(1.0f, Random.Range(0.6f, 0.85f), 0.35f, 1.0f);
				_sparks.Emit(emit, 1);
			}
		}

		/// <summary>Smoke drifting off the muzzle after a shot (count may be fractional: built up by the caller).</summary>
		public void MuzzleSmoke(Vector3 position, Vector3 forward, int count, bool firstPerson)
		{
			var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
			for (int i = 0; i < count; ++i)
			{
				emit.position      = position + forward * Random.Range(0.0f, 0.15f);
				emit.velocity      = forward * Random.Range(0.4f, 1.4f) + Vector3.up * Random.Range(0.15f, 0.45f) + Random.insideUnitSphere * 0.15f;
				emit.startSize     = Random.Range(0.12f, 0.3f) * (firstPerson == true ? 0.8f : 1.0f);
				emit.startLifetime = Random.Range(0.8f, 1.6f);
				emit.startColor    = new Color(0.85f, 0.85f, 0.85f, Random.Range(0.12f, 0.22f));
				emit.rotation      = Random.Range(0.0f, 360.0f);
				_smoke.Emit(emit, 1);
			}
		}

		/// <summary>A spent case thrown out of the ejection port to the right. In first person it's drawn by the viewmodel camera.</summary>
		public void Shell(Vector3 port, Vector3 right, Vector3 up, float length, bool firstPerson)
		{
			FeelSettings settings = FeelSettings.Current;
			var emit = new ParticleSystem.EmitParams
			{
				applyShapeToPosition = false,
				position             = port,
				velocity             = (right * 1.0f + up * 0.7f + Random.insideUnitSphere * 0.15f) * settings.ShellSpeed * Random.Range(0.85f, 1.15f),
				startSize3D          = new Vector3(length * 0.22f, length * 0.5f, length * 0.22f), // The built-in cylinder is 1 wide and 2 tall
				startLifetime        = firstPerson == true ? 0.5f : settings.ShellLife,
				startColor           = Color.white,
				rotation3D           = new Vector3(0.0f, 0.0f, 90.0f) + Random.insideUnitSphere * 20.0f,
				angularVelocity3D    = new Vector3(Random.Range(-900.0f, 900.0f), Random.Range(-300.0f, 300.0f), Random.Range(600.0f, 1400.0f)),
			};

			(firstPerson == true ? _viewShells : _shells).Emit(emit, 1);
		}

		// MONOBEHAVIOUR

		private void OnDestroy()
		{
			if (_runner != null && _all.TryGetValue(_runner, out GunEffects effects) == true && effects == this)
			{
				_all.Remove(_runner);
			}
		}

		// PRIVATE METHODS

		private void Build()
		{
			FeelSettings settings = FeelSettings.Current;

			_dust   = MakeSystem("Dust",   settings.SmokeMaterial, null, 1.2f, 256, false);
			var dustSize = _dust.sizeOverLifetime;
			dustSize.enabled = true;
			dustSize.size    = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.EaseInOut(0.0f, 0.5f, 1.0f, 1.6f));
			Fade(_dust, 0.15f);
			Drag(_dust, 3.0f);

			_chips  = MakeSystem("Chips",  settings.ChipMaterial, settings.ChipMesh, 6.0f, 256, false);
			Bounce(_chips, 0.3f);

			_sparks = MakeSystem("Sparks", settings.SparkMaterial, null, 2.0f, 256, false);
			var sparkRenderer = _sparks.GetComponent<ParticleSystemRenderer>();
			sparkRenderer.renderMode     = ParticleSystemRenderMode.Stretch;
			sparkRenderer.velocityScale  = 0.03f;
			sparkRenderer.lengthScale    = 1.0f;
			Fade(_sparks, 0.0f);

			_smoke  = MakeSystem("Smoke",  settings.SmokeMaterial, null, -0.05f, 256, false);
			var smokeSize = _smoke.sizeOverLifetime;
			smokeSize.enabled = true;
			smokeSize.size    = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.Linear(0.0f, 0.6f, 1.0f, 2.4f));
			Fade(_smoke, 0.1f);
			Drag(_smoke, 2.0f);

			_shells = MakeSystem("Shells", settings.ShellMaterial, settings.ShellMesh, 9.81f, 128, true);
			Bounce(_shells, 0.35f);

			_viewShells = MakeSystem("ViewShells", settings.ShellMaterial, settings.ShellMesh, 9.81f, 32, true);
			_viewShells.gameObject.layer = ObjectLayer.FPV;
		}

		private ParticleSystem MakeSystem(string name, Material material, Mesh mesh, float gravity, int maxParticles, bool threeD)
		{
			var go = new GameObject(name);
			go.transform.SetParent(transform, false);

			var system = go.AddComponent<ParticleSystem>();
			system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

			var main = system.main;
			main.loop             = false;
			main.playOnAwake      = false;
			main.duration         = 1.0f;
			main.simulationSpace  = ParticleSystemSimulationSpace.World;
			main.maxParticles     = maxParticles;
			main.gravityModifier  = gravity / 9.81f;
			main.startSize3D      = threeD;
			main.startRotation3D  = threeD == true || mesh != null;
			main.scalingMode      = ParticleSystemScalingMode.Shape;

			var emission = system.emission;
			emission.enabled = false;

			var shape = system.shape;
			shape.enabled = false;

			var renderer = go.GetComponent<ParticleSystemRenderer>();
			renderer.sharedMaterial    = material;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows    = false;
			renderer.sortingFudge      = 0.0f;
			if (mesh != null)
			{
				renderer.renderMode = ParticleSystemRenderMode.Mesh;
				renderer.mesh       = mesh;
				renderer.alignment  = ParticleSystemRenderSpace.World;
			}
			else
			{
				renderer.renderMode = ParticleSystemRenderMode.Billboard;
			}

			system.Play();
			return system;
		}

		private static void Fade(ParticleSystem system, float fadeIn)
		{
			var color = system.colorOverLifetime;
			color.enabled = true;

			var gradient = new Gradient();
			gradient.SetKeys(
				new[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
				fadeIn > 0.0f
					? new[] { new GradientAlphaKey(0.0f, 0.0f), new GradientAlphaKey(1.0f, fadeIn), new GradientAlphaKey(0.0f, 1.0f) }
					: new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) });
			color.color = gradient;
		}

		private static void Drag(ParticleSystem system, float drag)
		{
			var limit = system.limitVelocityOverLifetime;
			limit.enabled = true;
			limit.limit   = 100.0f;
			limit.drag    = drag;
			limit.multiplyDragByParticleSize = false;
		}

		private static void Bounce(ParticleSystem system, float bounce)
		{
			var collision = system.collision;
			collision.enabled        = true;
			collision.type           = ParticleSystemCollisionType.World;
			collision.mode           = ParticleSystemCollisionMode.Collision3D;
			collision.quality        = ParticleSystemCollisionQuality.Low;
			collision.collidesWith   = ObjectLayerMask.Environment;
			collision.bounce         = bounce;
			collision.dampen         = 0.4f;
			collision.radiusScale    = 0.5f;
			collision.enableDynamicColliders = false;
		}
	}
}
