namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.Rendering;

	/// <summary>
	/// Camouflage's look (phase 5b): while the server says this agent is camouflaged (standing still with the gear item on), other players
	/// see its body and gun see-through (EquipmentSettings.CamouflageVisibility). The player's own peer draws nothing different here (the
	/// viewmodel tints the arms). Each renderer gets a transparent copy of its materials while it's on, and its own back when it ends.
	/// Added to every agent at runtime.
	/// </summary>
	public sealed class GearVisuals : MonoBehaviour
	{
		// PRIVATE MEMBERS

		private struct RendererState
		{
			public Renderer          Renderer;
			public Material[]        Materials;
			public ShadowCastingMode Shadows;
		}

		private Agent  _agent;
		private bool   _isFaded;
		private Weapon _fadedWeapon;
		private float  _fadedAlpha;
		private int    _fadedGearVersion;
		private BodyGear _bodyGear;

		private readonly List<RendererState> _faded = new List<RendererState>(32);

		private static readonly Dictionary<(Material, int), Material> _transparentCopies = new Dictionary<(Material, int), Material>();

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_agent = GetComponent<Agent>();
		}

		private void Start()
		{
			_bodyGear = GetComponent<BodyGear>();
		}

		private void OnDisable()
		{
			Restore();
		}

		private void LateUpdate()
		{
			bool valid = _agent != null && _agent.Object != null && _agent.Object.IsValid == true;
			bool fade  = valid == true && _agent.IsCamouflaged == true && _agent.HasInputAuthority == false && _agent.Health.IsAlive == true;

			if (fade == false)
			{
				Restore();
				return;
			}

			float alpha = Mathf.Clamp01(EquipmentSettings.Current.CamouflageVisibility);

			// A different gun in hand, gear switched on the body (BodyGear) or a new setting since it faded: fade again so the new renderers are included
			int gearVersion = _bodyGear != null ? _bodyGear.Version : 0;
			if (_isFaded == true && (_fadedWeapon != _agent.Weapons.CurrentWeapon || _fadedGearVersion != gearVersion || Mathf.Abs(_fadedAlpha - alpha) > 0.001f))
			{
				Restore();
			}

			if (_isFaded == false)
			{
				Fade(alpha);
			}
		}

		// PRIVATE METHODS

		private void Fade(float alpha)
		{
			_isFaded          = true;
			_fadedWeapon      = _agent.Weapons.CurrentWeapon;
			_fadedAlpha       = alpha;
			_fadedGearVersion = _bodyGear != null ? _bodyGear.Version : 0;

			int alphaKey = Mathf.RoundToInt(alpha * 1000.0f);

			foreach (Renderer renderer in GetComponentsInChildren<Renderer>(false))
			{
				// Disabled renderers count too: Multipeer switches off the renderers of the peers not on screen
				if (renderer is LineRenderer || renderer is ParticleSystemRenderer)
					continue;
				if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
					continue;

				Material[] materials = renderer.sharedMaterials;
				var        copies    = new Material[materials.Length];

				for (int i = 0; i < materials.Length; ++i)
				{
					copies[i] = GetTransparentCopy(materials[i], alpha, alphaKey);
				}

				_faded.Add(new RendererState { Renderer = renderer, Materials = materials, Shadows = renderer.shadowCastingMode });

				renderer.sharedMaterials   = copies;
				renderer.shadowCastingMode = ShadowCastingMode.Off;
			}
		}

		private void Restore()
		{
			if (_isFaded == false)
				return;

			for (int i = 0; i < _faded.Count; ++i)
			{
				RendererState state = _faded[i];
				if (state.Renderer == null)
					continue;

				state.Renderer.sharedMaterials   = state.Materials;
				state.Renderer.shadowCastingMode = state.Shadows;
			}

			_faded.Clear();
			_isFaded     = false;
			_fadedWeapon = null;
		}

		/// <summary>A see-through URP Lit copy of a material (its texture and color, at this alpha), made once and shared.</summary>
		private static Material GetTransparentCopy(Material source, float alpha, int alphaKey)
		{
			if (source == null)
				return null;

			if (_transparentCopies.TryGetValue((source, alphaKey), out Material copy) == true && copy != null)
				return copy;

			copy = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"{source.name} (Camouflage)" };

			Texture texture = source.HasProperty("_BaseMap") == true ? source.GetTexture("_BaseMap") : (source.HasProperty("_MainTex") == true ? source.mainTexture : null);
			Color   color   = source.HasProperty("_BaseColor") == true ? source.GetColor("_BaseColor") : (source.HasProperty("_Color") == true ? source.color : Color.white);

			color.a = alpha;

			copy.SetTexture("_BaseMap", texture);
			copy.SetColor("_BaseColor", color);
			copy.SetFloat("_Surface", 1.0f);
			copy.SetFloat("_Blend", 0.0f);
			copy.SetFloat("_ZWrite", 0.0f);
			copy.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
			copy.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
			copy.SetOverrideTag("RenderType", "Transparent");
			copy.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			copy.renderQueue = (int)RenderQueue.Transparent;

			_transparentCopies[(source, alphaKey)] = copy;
			return copy;
		}
	}
}
