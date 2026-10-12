namespace TPSBR
{
	using System.Collections.Generic;
	using System.IO;
	using System.Text;
	using UnityEditor;
	using UnityEditor.SceneManagement;
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.SceneManagement;

	using UnityScene = UnityEngine.SceneManagement.Scene;

	/// <summary>
	/// Phase 11a: Brownout â†’ Setup â†’ Bake Icons. Renders every item from the same model the ground shows (LootVisuals) into a transparent
	/// PNG in Assets/Brownout/UI/Icons/ and links them in Resources/Settings/IconSettings.asset: guns from the side (plus a white silhouette),
	/// attachments, healing, armor, backpack, ammo, grenades and the grappling gun from above at an angle, the loot case. Gear items have
	/// no models; their symbols are drawn here. Rendered in a preview scene, so the open scene isn't touched. Safe to run again.
	/// </summary>
	public static class BrownoutIconBaker
	{
		// CONSTANTS

		public const string Folder    = "Assets/Brownout/UI/Icons/";
		public const string AssetPath = "Assets/TPSBR/Resources/Settings/IconSettings.asset";

		private const int ItemSize  = 256;
		private const int GunWidth  = 512;
		private const int GunHeight = 256;
		private const int Padding   = 10;

		// MENU

		[MenuItem("Brownout/Setup/Bake Icons", priority = 16)]
		private static void BakeMenu() => Debug.Log(BakeAll());

		// PUBLIC METHODS

		public static string BakeAll()
		{
			// Outside Play mode Global.Settings isn't loaded, and the item catalog reads the guns and attachments through it
			var settingsProperty = typeof(Global).GetProperty(nameof(Global.Settings));
			bool borrowed = Global.Settings == null;
			if (borrowed == true)
			{
				GlobalSettings[] global = Resources.LoadAll<GlobalSettings>("");
				settingsProperty.SetValue(null, global.Length > 0 ? global[0] : null);
			}

			try
			{
				return Bake();
			}
			finally
			{
				if (borrowed == true)
				{
					settingsProperty.SetValue(null, null);
				}
			}
		}

		// PRIVATE METHODS

		private static string Bake()
		{
			var log = new StringBuilder("Bake Icons:\n");
			Directory.CreateDirectory(Folder);

			GunplaySettings gunplay = GunplaySettings.Current;
			var written = new List<(string path, System.Action<Sprite> link)>(96);

			IconSettings settings = AssetDatabase.LoadAssetAtPath<IconSettings>(AssetPath);
			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<IconSettings>();
				AssetDatabase.CreateAsset(settings, AssetPath);
				log.AppendLine($"Made {AssetPath}.");
			}

			int guns = ItemCatalog.GunCount;
			int attachments = gunplay.Attachments != null ? gunplay.Attachments.Length : 0;
			settings.Guns           = new Sprite[guns];
			settings.GunSilhouettes = new Sprite[guns];
			settings.Attachments    = new Sprite[attachments];
			settings.Heals          = new Sprite[3];
			settings.Armor          = new Sprite[3];
			settings.Ammo           = new Sprite[6];
			settings.Equipment      = new Sprite[5];
			settings.Gear           = new Sprite[6];

			UnityScene scene = EditorSceneManager.NewPreviewScene();
			try
			{
				Camera camera = MakeStage(scene);

				for (int i = 0; i < guns; ++i)
				{
					GunData gun = ItemCatalog.GetGunData(i);
					string  name = gun != null ? gun.GunName.Replace(" ", "") : $"Gun{i}";
					int     index = i;

					Texture2D icon = Render(scene, camera, LootStack.Gun(i, 0, 0), true);
					if (icon == null)
						continue;

					written.Add((Save(Silhouette(icon), $"Gun_{name}_Silhouette"), s => settings.GunSilhouettes[index] = s));
					written.Add((Save(icon, $"Gun_{name}"), s => settings.Guns[index] = s));
				}

				for (int i = 0; i < attachments; ++i)
				{
					AttachmentData data = gunplay.Attachments[i];
					int index = i;
					Texture2D icon = Render(scene, camera, LootStack.Attachment(i + 1), false);
					if (icon != null)
					{
						written.Add((Save(icon, $"Attachment_{(data != null ? data.AttachmentName.Replace(" ", "") : (i + 1).ToString())}"), s => settings.Attachments[index] = s));
					}
				}

				for (int i = 0; i < 3; ++i)
				{
					int index = i;
					written.Add((Save(Render(scene, camera, LootStack.Heal((EHealItem)i, 1), false), $"Heal_{(EHealItem)i}"), s => settings.Heals[index] = s));
					written.Add((Save(Render(scene, camera, LootStack.ArmorVest(i + 1, 1.0f), false), $"Armor_T{i + 1}"), s => settings.Armor[index] = s));
				}

				written.Add((Save(Render(scene, camera, LootStack.Backpack(), false), "Backpack"), s => settings.Backpack = s));

				for (int i = 0; i < 6; ++i)
				{
					int index = i;
					written.Add((Save(Render(scene, camera, LootStack.Ammo((ECaliber)i, 1), false), $"Ammo_{(ECaliber)i}"), s => settings.Ammo[index] = s));
				}

				for (int i = 0; i < 5; ++i)
				{
					int index = i;
					written.Add((Save(Render(scene, camera, LootStack.EquipmentItem((EEquipment)i), false), $"Equipment_{(EEquipment)i}"), s => settings.Equipment[index] = s));
				}

				ArtSettings art = ArtSettings.Current;
				if (art != null && art.EquipmentCaseModel != null)
				{
					written.Add((Save(RenderModel(scene, camera, art.EquipmentCaseModel), "Case"), s => settings.Case = s));
				}
			}
			finally
			{
				EditorSceneManager.ClosePreviewScene(scene);
			}

			for (int i = 0; i < 6; ++i)
			{
				int index = i;
				written.Add((Save(DrawGear((EGear)i), $"Gear_{(EGear)i}"), s => settings.Gear[index] = s));
			}

			// Import every PNG as a sprite, then link them
			AssetDatabase.Refresh();
			int linked = 0;
			foreach ((string path, System.Action<Sprite> link) in written)
			{
				if (string.IsNullOrEmpty(path) == true)
					continue;

				var importer = (TextureImporter)AssetImporter.GetAtPath(path);
				if (importer != null)
				{
					importer.textureType         = TextureImporterType.Sprite;
					importer.spriteImportMode    = SpriteImportMode.Single;
					importer.alphaIsTransparency = true;
					importer.mipmapEnabled       = true;
					importer.textureCompression  = TextureImporterCompression.Uncompressed;
					importer.SaveAndReimport();
				}

				Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
				if (sprite != null)
				{
					link(sprite);
					++linked;
				}
				else
				{
					log.AppendLine($"  couldn't load {path} as a sprite");
				}
			}

			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssets();

			log.AppendLine($"{linked} icons in {Folder} ({guns} guns + silhouettes, {attachments} attachments, 3 heals, 3 armor, backpack, 6 ammo, 5 equipment, case, 6 gear symbols), linked in {AssetPath}.");
			return log.ToString();
		}

		/// <summary>A camera and two lights in the preview scene.</summary>
		private static Camera MakeStage(UnityScene scene)
		{
			var cameraObject = new GameObject("IconCamera");
			SceneManager.MoveGameObjectToScene(cameraObject, scene);
			Camera camera = cameraObject.AddComponent<Camera>();
			camera.scene           = scene;
			camera.orthographic    = true;
			camera.clearFlags      = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			camera.nearClipPlane   = 0.01f;
			camera.farClipPlane    = 20.0f;
			camera.allowHDR        = false;
			camera.allowMSAA       = false;
			camera.enabled         = false;

			var key = new GameObject("Key");
			SceneManager.MoveGameObjectToScene(key, scene);
			Light keyLight = key.AddComponent<Light>();
			keyLight.type      = LightType.Directional;
			keyLight.intensity = 1.6f;
			keyLight.color     = new Color(1.0f, 0.97f, 0.92f);
			key.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);

			var fill = new GameObject("Fill");
			SceneManager.MoveGameObjectToScene(fill, scene);
			Light fillLight = fill.AddComponent<Light>();
			fillLight.type      = LightType.Directional;
			fillLight.intensity = 0.7f;
			fillLight.color     = new Color(0.85f, 0.9f, 1.0f);
			fill.transform.rotation = Quaternion.Euler(30.0f, 150.0f, 0.0f);

			return camera;
		}

		/// <summary>The item as the ground shows it. Guns are seen from straight above (they lie on their side), muzzle to the right.</summary>
		private static Texture2D Render(UnityScene scene, Camera camera, LootStack stack, bool gun)
		{
			var root = new GameObject("Item");
			SceneManager.MoveGameObjectToScene(root, scene);
			try
			{
				LootVisuals.Build(stack, root.transform, null, out Bounds _, false);
				return Capture(camera, root, gun);
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
		}

		private static Texture2D RenderModel(UnityScene scene, Camera camera, GameObject model)
		{
			var root = new GameObject("Item");
			SceneManager.MoveGameObjectToScene(root, scene);
			try
			{
				Object.Instantiate(model, root.transform, false);
				return Capture(camera, root, false);
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
		}

		private static Texture2D Capture(Camera camera, GameObject root, bool gun)
		{
			Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
			if (renderers.Length == 0)
				return null;

			int width  = gun == true ? GunWidth : ItemSize;
			int height = gun == true ? GunHeight : ItemSize;

			// Guns: from straight above with the long axis across the picture; everything else from above at an angle
			Bounds all = renderers[0].bounds;
			foreach (Renderer renderer in renderers)
			{
				all.Encapsulate(renderer.bounds);
			}

			Vector3    across = all.size.z > all.size.x ? Vector3.forward : Vector3.right;
			Quaternion view   = gun == true ? Quaternion.LookRotation(Vector3.down, Vector3.Cross(across, Vector3.down)) : Quaternion.Euler(50.0f, -35.0f, 0.0f);
			camera.transform.rotation = view;

			// Fit the camera to the renderers' corners in view space
			Matrix4x4 toView = Matrix4x4.Rotate(Quaternion.Inverse(view));
			Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
			foreach (Renderer renderer in renderers)
			{
				Bounds b = renderer.bounds;
				for (int i = 0; i < 8; ++i)
				{
					Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
					Vector3 p = toView.MultiplyPoint3x4(corner);
					min = Vector3.Min(min, p);
					max = Vector3.Max(max, p);
				}
			}

			Vector3 centerView = (min + max) * 0.5f;
			Vector3 sizeView   = max - min;
			float   aspect     = width / (float)height;
			float   fill       = 1.0f - 2.0f * Padding / (float)height;

			camera.aspect           = aspect;
			camera.orthographicSize = Mathf.Max(sizeView.y * 0.5f, sizeView.x * 0.5f / aspect) / fill;
			camera.transform.position = view * new Vector3(centerView.x, centerView.y, min.z - 5.0f);

			var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
			RenderTexture previous = RenderTexture.active;
			try
			{
				camera.targetTexture = target;
				camera.Render();

				RenderTexture.active = target;
				var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
				texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				texture.Apply();

				// Guns point their muzzle right, mag down
				if (gun == true && PartSide(root, camera, "_Muzzle").x < 0.0f)
				{
					Mirror(texture, true);
				}
				if (gun == true && PartSide(root, camera, "_Mag").y > 0.0f)
				{
					Mirror(texture, false);
				}

				return texture;
			}
			finally
			{
				camera.targetTexture = null;
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(target);
			}
		}

		/// <summary>Where the gun's named part (by suffix) sits from the picture's center, in the camera's x/y; zero when it has none.</summary>
		private static Vector2 PartSide(GameObject root, Camera camera, string suffix)
		{
			Bounds all = default;
			bool   any = false;
			foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
			{
				if (any == false) { all = renderer.bounds; any = true; } else { all.Encapsulate(renderer.bounds); }
			}

			foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
			{
				if (child.name.EndsWith(suffix) == true)
				{
					Vector3 part   = child.GetComponent<Renderer>() != null ? child.GetComponent<Renderer>().bounds.center : child.position;
					Vector3 offset = camera.transform.InverseTransformPoint(part) - camera.transform.InverseTransformPoint(all.center);
					return new Vector2(offset.x, offset.y);
				}
			}

			return Vector2.zero;
		}

		private static void Mirror(Texture2D texture, bool horizontal)
		{
			Color32[] pixels = texture.GetPixels32();
			var       copy   = (Color32[])pixels.Clone();
			int w = texture.width, h = texture.height;
			for (int y = 0; y < h; ++y)
			{
				for (int x = 0; x < w; ++x)
				{
					pixels[y * w + x] = horizontal == true ? copy[y * w + (w - 1 - x)] : copy[(h - 1 - y) * w + x];
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply();
		}

		/// <summary>White where the icon is opaque.</summary>
		private static Texture2D Silhouette(Texture2D icon)
		{
			Color32[] pixels = icon.GetPixels32();
			for (int i = 0; i < pixels.Length; ++i)
			{
				pixels[i] = new Color32(255, 255, 255, pixels[i].a);
			}

			var texture = new Texture2D(icon.width, icon.height, TextureFormat.RGBA32, false);
			texture.SetPixels32(pixels);
			texture.Apply();
			return texture;
		}

		private static string Save(Texture2D texture, string name)
		{
			if (texture == null)
				return null;

			string path = Folder + name + ".png";
			File.WriteAllBytes(path, texture.EncodeToPNG());
			Object.DestroyImmediate(texture);
			return path;
		}

		// GEAR SYMBOLS (no models): simple white shapes from distance functions, antialiased

		private static Texture2D DrawGear(EGear gear)
		{
			const int size = ItemSize;
			var pixels = new Color32[size * size];

			for (int y = 0; y < size; ++y)
			{
				for (int x = 0; x < size; ++x)
				{
					// -1..1, y up
					Vector2 p = new Vector2((x + 0.5f) / size * 2.0f - 1.0f, (y + 0.5f) / size * 2.0f - 1.0f);
					float   d = GearShape(gear, p);
					float   a = Mathf.Clamp01(0.5f - d * size * 0.5f);
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255.0f));
				}
			}

			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
			texture.SetPixels32(pixels);
			texture.Apply();
			return texture;
		}

		/// <summary>Signed distance (negative inside) of each gear item's symbol.</summary>
		private static float GearShape(EGear gear, Vector2 p)
		{
			switch (gear)
			{
				case EGear.DoubleJump: // two chevrons pointing up
					return Mathf.Min(Chevron(p - new Vector2(0.0f, 0.25f), Vector2.up), Chevron(p + new Vector2(0.0f, 0.25f), Vector2.up));
				case EGear.DoubleTime: // two chevrons pointing right
					return Mathf.Min(Chevron(p - new Vector2(0.25f, 0.0f), Vector2.right), Chevron(p + new Vector2(0.25f, 0.0f), Vector2.right));
				case EGear.Ninja: // a footprint: sole and four toes
				{
					float sole = Ellipse(p - new Vector2(0.0f, -0.2f), new Vector2(0.28f, 0.45f));
					float toes = 10.0f;
					for (int i = 0; i < 4; ++i)
					{
						Vector2 toe = new Vector2(-0.24f + i * 0.16f, 0.42f + (i == 1 || i == 2 ? 0.08f : 0.0f));
						toes = Mathf.Min(toes, (p - toe).magnitude - 0.075f);
					}
					return Mathf.Min(sole, toes);
				}
				case EGear.SixthSense: // an eye
				{
					float lids  = Mathf.Max(Circle(p - new Vector2(0.0f, -0.55f), 0.9f), Circle(p - new Vector2(0.0f, 0.55f), 0.9f));
					float inner = Mathf.Max(Circle(p - new Vector2(0.0f, -0.62f), 0.8f), Circle(p - new Vector2(0.0f, 0.62f), 0.8f));
					float ring  = Mathf.Max(lids, -inner);
					return Mathf.Min(ring, Circle(p, 0.2f));
				}
				case EGear.Camouflage: // a grid of diamonds
				{
					Vector2 q = new Vector2(Mathf.Repeat(p.x + 0.25f, 0.5f) - 0.25f, Mathf.Repeat(p.y + 0.25f, 0.5f) - 0.25f);
					float diamond = (Mathf.Abs(q.x) + Mathf.Abs(q.y)) * 0.7071f - 0.12f;
					return Mathf.Max(diamond, Circle(p, 0.8f));
				}
				default: // O2 Max: three rising bubbles
					return Mathf.Min(Mathf.Min(Ring(p - new Vector2(-0.25f, -0.3f), 0.3f, 0.07f), Ring(p - new Vector2(0.3f, 0.1f), 0.22f, 0.06f)), Ring(p - new Vector2(-0.1f, 0.55f), 0.14f, 0.05f));
			}
		}

		private static float Circle(Vector2 p, float r) => p.magnitude - r;
		private static float Ring(Vector2 p, float r, float w) => Mathf.Abs(p.magnitude - r) - w;
		private static float Ellipse(Vector2 p, Vector2 r) => (new Vector2(p.x / r.x, p.y / r.y).magnitude - 1.0f) * Mathf.Min(r.x, r.y);

		private static float Chevron(Vector2 p, Vector2 direction)
		{
			// Rotate so the chevron points along +Y, then two thick lines from the tip
			Vector2 side = new Vector2(direction.y, -direction.x);
			Vector2 q    = new Vector2(Vector2.Dot(p, side), Vector2.Dot(p, direction));
			q.x = Mathf.Abs(q.x);
			return Segment(q, new Vector2(0.0f, 0.2f), new Vector2(0.45f, -0.2f)) - 0.09f;
		}

		private static float Segment(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 pa = p - a, ba = b - a;
			float   h  = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
			return (pa - ba * h).magnitude;
		}
	}
}
