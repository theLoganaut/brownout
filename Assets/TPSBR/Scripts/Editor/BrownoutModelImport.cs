namespace TPSBR
{
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Import rules for Logan's models (Assets/Brownout/Art/).
	/// Guns and attachments (Weapons/, Attachments/): the FBX files come from Blender without axis conversion, so Unity puts a
	/// -90° X rotation on each file's root and the barrel points down as soon as anything resets that rotation. This bakes the
	/// root rotation into the meshes and empties (barrel along +Z, up +Y, empties keep zero rotation) and leaves the root at
	/// identity. The .fbx files are not changed.
	/// Rigid props (Armor/, Items/, Drop/, Vehicles/, Arms/, Nature/Models/): the same settings as the guns. Files exported with
	/// axis conversion have no root rotation, so the bake below leaves them untouched.
	/// Characters (Characters/): skinned, with animations as takes. Set up once on first import (Humanoid rig, animations on,
	/// Unity's axis conversion baked, which is safe for skinned meshes), then left alone so the settings can be tuned by hand.
	/// A file ending in "_arms" (first-person arms) gets a Generic rig instead (Set Up First Person Arms does the rest).
	/// </summary>
	public sealed class BrownoutModelImport : AssetPostprocessor
	{
		// CONSTANTS

		public const string ArtFolder         = "Assets/Brownout/Art/";
		public const string WeaponsFolder     = ArtFolder + "Weapons/";
		public const string AttachmentsFolder = ArtFolder + "Attachments/";
		public const string CharactersFolder  = ArtFolder + "Characters/";

		private static readonly string[] RigidFolders =
		{
			ArtFolder + "Weapons/",
			ArtFolder + "Attachments/",
			ArtFolder + "Armor/",
			ArtFolder + "Items/",
			ArtFolder + "Drop/",
			ArtFolder + "Vehicles/",
			ArtFolder + "Arms/",
			ArtFolder + "Nature/Models/",
		};

		private const int VERSION = 1;

		// AssetPostprocessor INTERFACE

		public override uint GetVersion()
		{
			return VERSION;
		}

		private void OnPreprocessModel()
		{
			if (IsCharacter() == true)
			{
				PreprocessCharacter();
				return;
			}

			if (IsRigid() == false)
				return;

			var importer = (ModelImporter)assetImporter;

			importer.globalScale          = 1.0f;
			importer.useFileScale         = true;
			importer.bakeAxisConversion   = false;
			importer.importCameras        = false;
			importer.importLights         = false;
			importer.importAnimation      = false;
			importer.animationType        = ModelImporterAnimationType.None;
			importer.isReadable           = false;
			importer.materialImportMode   = ModelImporterMaterialImportMode.ImportStandard;
		}

		private void OnPostprocessModel(GameObject root)
		{
			if (IsRigid() == false)
				return;

			Quaternion rotation = root.transform.localRotation;
			if (Quaternion.Angle(rotation, Quaternion.identity) < 0.01f)
				return;

			Quaternion inverse = Quaternion.Inverse(rotation);

			// Change of basis for every node below the root: world = R * (p + q * v) stays the same with
			// p' = R p, q' = R q R⁻¹, v' = R v (and the same again for nested children)
			foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
			{
				if (child == root.transform)
					continue;

				child.localPosition = rotation * child.localPosition;

				Quaternion childRotation = child.localRotation;
				child.localRotation = rotation * childRotation * inverse;

				var meshFilter = child.GetComponent<MeshFilter>();
				if (meshFilter != null && meshFilter.sharedMesh != null)
				{
					RotateMesh(meshFilter.sharedMesh, rotation);
				}
			}

			root.transform.localRotation = Quaternion.identity;
		}

		// PRIVATE METHODS

		private bool IsRigid()
		{
			foreach (string folder in RigidFolders)
			{
				if (assetPath.StartsWith(folder) == true)
					return true;
			}

			return false;
		}

		private bool IsCharacter()
		{
			return assetPath.StartsWith(CharactersFolder) == true;
		}

		private void PreprocessCharacter()
		{
			var importer = (ModelImporter)assetImporter;

			// Only on the first import, so later changes in the Inspector (clip loops, avatar fixes) stick
			if (importer.importSettingsMissing == false)
				return;

			importer.globalScale          = 1.0f;
			importer.useFileScale         = true;
			importer.bakeAxisConversion   = true;
			importer.importCameras        = false;
			importer.importLights         = false;
			importer.importAnimation      = true;
			importer.animationType        = ModelImporterAnimationType.Human;
			importer.avatarSetup          = ModelImporterAvatarSetup.CreateFromThisModel;
			importer.isReadable           = false;
			importer.materialImportMode   = ModelImporterMaterialImportMode.ImportStandard;

			// First-person arms (soldier_arms.fbx): the soldier's skeleton with only the arms skinned and the same takes, played as Generic clips (ViewmodelArms)
			if (System.IO.Path.GetFileNameWithoutExtension(assetPath).EndsWith("_arms") == true)
			{
				importer.animationType   = ModelImporterAnimationType.Generic;
				importer.avatarSetup     = ModelImporterAvatarSetup.NoAvatar;
			}
		}

		private static void RotateMesh(Mesh mesh, Quaternion rotation)
		{
			Vector3[] vertices = mesh.vertices;
			for (int i = 0; i < vertices.Length; ++i)
			{
				vertices[i] = rotation * vertices[i];
			}

			Vector3[] normals = mesh.normals;
			for (int i = 0; i < normals.Length; ++i)
			{
				normals[i] = rotation * normals[i];
			}

			Vector4[] tangents = mesh.tangents;
			for (int i = 0; i < tangents.Length; ++i)
			{
				Vector3 tangent = rotation * new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
				tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w);
			}

			mesh.vertices = vertices;
			mesh.normals  = normals;
			mesh.tangents = tangents;
			mesh.RecalculateBounds();
		}
	}
}
