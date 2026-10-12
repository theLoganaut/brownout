namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEditor;

	public static class ProjectUtility
	{
		[MenuItem("Tools/Reserialize All Prefabs")]
		public static void ReserializeAllPrefabs()
		{
			int count = 0;

			foreach (string prefabPath in GetAllAssets(".prefab"))
			{
				GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
				if (PrefabUtility.IsPartOfImmutablePrefab(prefabAsset) == false)
				{
					Debug.Log($"Reserializing {prefabPath}", prefabAsset);
					PrefabUtility.SavePrefabAsset(prefabAsset);
					++count;
				}
			}

			Debug.LogWarning($"Reserialized {count} prefabs");
		}

		[MenuItem("Tools/Reserialize All ScriptableObjects")]
		public static void ReserializeAllScriptableObjects()
		{
			List<string> assetPaths = new List<string>();

			foreach (string assetPath in GetAllAssets(".asset"))
			{
				if (assetPath.StartsWith("Assets/") == false)
					continue;

				ScriptableObject scriptableObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
				if (scriptableObject == null)
					continue;

				Debug.Log($"Reserializing {assetPath}", scriptableObject);
				assetPaths.Add(assetPath);
			}

			if (assetPaths.Count > 0)
			{
				AssetDatabase.ForceReserializeAssets(assetPaths);
			}

			Debug.LogWarning($"Reserialized {assetPaths.Count} scriptable objects");
		}

		[MenuItem("Tools/Reserialize All Scenes")]
		public static void ReserializeAllScenes()
		{
			List<string> assetPaths = new List<string>();

			foreach (string assetPath in GetAllAssets(".unity"))
			{
				if (assetPath.StartsWith("Assets/") == false)
					continue;

				Debug.Log($"Reserializing {assetPath}");
				assetPaths.Add(assetPath);
			}

			if (assetPaths.Count > 0)
			{
				AssetDatabase.ForceReserializeAssets(assetPaths);
			}

			Debug.LogWarning($"Reserialized {assetPaths.Count} scenes");
		}

		// PRIVATE METHODS

		private static string[] GetAllAssets(string suffix)
		{
			List<string> assetPaths = new List<string>();
			foreach (string assetPath in AssetDatabase.GetAllAssetPaths())
			{
				if (assetPath.EndsWith(suffix) == true)
				{
					assetPaths.Add(assetPath);
				}
			}

			return assetPaths.ToArray();
		}
	}
}
