namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.SceneManagement;
	using UnityEditor;
	using UnityEditor.SceneManagement;

	using UnityScene = UnityEngine.SceneManagement.Scene;

	/// <summary>
	/// Opens the first scene from build settings when the editor would otherwise
	/// start on an empty untitled scene - typically after a fresh clone of the project.
	/// </summary>
	[InitializeOnLoad]
	public static class StartupSceneLoader
	{
		// CONSTRUCTORS

		static StartupSceneLoader()
		{
			if (Application.isBatchMode == true)
				return;

			EditorApplication.update -= TryOpenStartupScene;
			EditorApplication.update += TryOpenStartupScene;
		}

		// PRIVATE METHODS

		private static void TryOpenStartupScene()
		{
			if (EditorApplication.isCompiling == true || EditorApplication.isUpdating == true)
				return;

			EditorApplication.update -= TryOpenStartupScene;

			if (EditorApplication.isPlayingOrWillChangePlaymode == true)
				return;

			OpenStartupScene();
		}

		private static void OpenStartupScene()
		{
			UnityScene activeScene = SceneManager.GetActiveScene();

			// Only an unsaved empty scene is replaced, never a scene the user has open or modified.
			if (string.IsNullOrEmpty(activeScene.path) == false || activeScene.isDirty == true)
				return;

			foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
			{
				if (buildScene.enabled == false || string.IsNullOrEmpty(buildScene.path) == true)
					continue;

				EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
				return;
			}
		}
	}
}
