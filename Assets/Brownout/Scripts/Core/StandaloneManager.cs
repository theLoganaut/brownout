using Fusion;
using Fusion.Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TPSBR
{
	using System;

	[Serializable]
	public sealed class StandaloneConfiguration
	{
		public EGameplayType GameplayType;
		public GameMode      GameMode;
		public string        ServerName;
		public int           MaxPlayers;
		public int           ExtraPeers;
		public string        Region;
		public string        SessionName;
		public string        CustomLobby;
		public string        IPAddress;
		public ushort        Port;
	}

	public class StandaloneManager : MonoBehaviour
	{
		// PUBLIC MEMBERS

		public static StandaloneConfiguration ExternalConfiguration;

		/// <summary>Phase 7: editor preference (Brownout → Test → Match → Play Battle Royale) that starts the scene in Battle Royale.</summary>
		public const string PlayBattleRoyaleKey = "Brownout.PlayBattleRoyale";
		/// <summary>Editor preference (Brownout → Test → Match → Start Match On Play): the warm-up starts the match by itself once every peer is in.</summary>
		public const string StartOnPlayKey      = "Brownout.StartMatchOnPlay";

		// PRIVATE MEMBERS

		[SerializeField]
		private StandaloneConfiguration _defaultConfiguration;

		// MONOBEHAVIOUR

		protected void Awake()
		{
			if (Global.Networking.HasSession == true)
			{
				Destroy(gameObject);
			}
		}

		protected void Start()
		{
			StandaloneConfiguration configuration = ExternalConfiguration ?? _defaultConfiguration;

			var playerData = Global.PlayerService.PlayerData;
			var scenePath = SceneManager.GetActiveScene().path;

			scenePath = scenePath.Substring("Assets/".Length, scenePath.Length - "Assets/".Length - ".unity".Length);

			PhotonAppSettings.Global.AppSettings.FixedRegion = configuration.Region;

			EGameplayType gameplayType = configuration.GameplayType;
#if UNITY_EDITOR
			// Phase 7: test a Battle Royale match in any scene without changing its saved mode
			if (ExternalConfiguration == null && UnityEditor.EditorPrefs.GetBool(PlayBattleRoyaleKey, false) == true)
			{
				gameplayType = EGameplayType.BattleRoyale;
			}

			// Start Match On Play: skip waiting for Enter once every peer is in
			BattleRoyaleGameplayMode.DebugAutoStartPlayers = ExternalConfiguration == null && UnityEditor.EditorPrefs.GetBool(StartOnPlayKey, false) == true ? 1 + configuration.ExtraPeers : 0;
#endif

			var request = new SessionRequest
			{
				UserID       = playerData.UserID.HasValue() ? playerData.UserID : new Guid().ToString(),
				GameMode     = configuration.GameMode,
				SessionName  = configuration.SessionName.HasValue() ? configuration.SessionName : Guid.NewGuid().ToString(),
				DisplayName  = configuration.ServerName,
				ScenePath    = scenePath,
				GameplayType = gameplayType,
				ExtraPeers   = configuration.ExtraPeers,
				MaxPlayers   = configuration.MaxPlayers,
				CustomLobby  = configuration.CustomLobby.HasValue() ? configuration.CustomLobby : "FusionBR." + Application.version,
				IPAddress    = configuration.IPAddress,
				Port         = configuration.Port,
			};

			Global.Networking.StartGame(request);
		}
	}
}
