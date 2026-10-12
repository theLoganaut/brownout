using UnityEngine;
using Fusion;
using TMPro;

namespace TPSBR.UI
{
	public class UIGameplayView : UIView
	{
		// PRIVATE MEMBERS

		[SerializeField]
		private UIPlayer _player;
		[SerializeField]
		private UICrosshair _crosshair;
		[SerializeField]
		private UIHealth _health;
		[SerializeField]
		private UIWeapons _weapons;
		[SerializeField]
		private UIGameplayInteractions _interactions;
		[SerializeField]
		private UIAgentEffects _effects;
		[SerializeField]
		private UIGameplayEvents _events;
		[SerializeField]
		private UIShrinkingArea _shrinkingArea;
		[SerializeField]
		private UIKillFeed _killFeed;
		[SerializeField]
		private UIBehaviour _spectatingGroup;
		[SerializeField]
		private TextMeshProUGUI _spectatingText;
		[SerializeField]
		private UIHitDamageIndicator _hitDamage;
		[SerializeField]
		private UIJetpack _jetpack;
		[SerializeField]
		private UIButton _menuButton;

		[Header("Gameplay Modes")]
		[SerializeField]
		private UIBattleRoyale _battleRoyale;

		[Header("Events Setup")]
		[SerializeField]
		private Color _enemyKilledColor = Color.red;
		[SerializeField]
		private Color _playerDeathColor = Color.yellow;
		[SerializeField]
		private AudioSetup _enemyKilledSound;
		[SerializeField]
		private AudioSetup _playerDeathSound;
		[SerializeField]
		private Color _interactionFailedColor = Color.white;
		[SerializeField]
		private AudioSetup _interactionFailedSound;

		private Agent              _localAgent;
		private NetworkBehaviourId _localAgentId;

		// Phase 6a: the Brownout HUD, built in code from hud_rects.json, replaces the sample's health, weapons, crosshair visuals,
		// interaction prompts, kill feed, floating damage numbers, player panel and minimap
		private BrownoutHud        _hud;
		private Canvas             _canvas;

		// UIView INTERFACE

		protected override void OnInitialize()
		{
			base.OnInitialize();

			_canvas = GetComponentInParent<Canvas>() != null ? GetComponentInParent<Canvas>().rootCanvas : null;
			_hud    = BrownoutHud.Create(Context, _canvas);
			UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_hud.gameObject, gameObject.scene);

			HideSampleWidgets();
			ClearLocalAgent();

			Context.GameplayMode.OnAgentDeath       += OnAgentDeath;
			Context.GameplayMode.OnPlayerEliminated += OnPlayerEliminated;
			Context.GameplayMode.OnPlayerJoinedGame += OnPlayerJoined;
			Context.GameplayMode.OnPlayerLeftGame   += OnPlayerLeft;

			if (Context.Announcer != null)
			{
				Context.Announcer.Announce += OnAnnounce;
			}

			// Phase 7: the Brownout HUD shows the warm-up, helicopter, zone and end (BrownoutHud.Match)
			_battleRoyale.SetActive(false);

			if ((Application.isMobilePlatform == false || Application.isEditor == true) && Context.Settings.SimulateMobileInput == false)
			{
				_menuButton.SetActive(false);
			}

			if (_menuButton != null)
			{
				_menuButton.onClick.AddListener(OnMenuButton);
			}
		}

		protected override void OnDeinitialize()
		{
			base.OnDeinitialize();

			Context.GameplayMode.OnAgentDeath       -= OnAgentDeath;
			Context.GameplayMode.OnPlayerEliminated -= OnPlayerEliminated;
			Context.GameplayMode.OnPlayerJoinedGame -= OnPlayerJoined;
			Context.GameplayMode.OnPlayerLeftGame   -= OnPlayerLeft;

			if (Context.Announcer != null)
			{
				Context.Announcer.Announce -= OnAnnounce;
			}

			if (_menuButton != null)
			{
				_menuButton.onClick.RemoveListener(OnMenuButton);
			}

			if (_hud != null)
			{
				Destroy(_hud.gameObject);
				_hud = null;
			}
		}

		protected override void OnTick()
		{
			base.OnTick();

			if (Context.Runner == null || Context.Runner.IsRunning == false)
				return;
			if (Context.GameplayMode == null || Context.Runner.Exists(Context.GameplayMode.Object) == false)
				return;

			if (_localAgent != Context.ObservedAgent || (Context.ObservedAgent != null && _localAgentId != Context.ObservedAgent.Id))
			{
				if (Context.ObservedAgent == null)
				{
					ClearLocalAgent();
				}
				else
				{
					var player = Context.NetworkGame.GetPlayer(Context.ObservedPlayerRef);
					if (player == null)
					{
						ClearLocalAgent();
					}
					else
					{
						SetLocalAgent(SceneUI.Context.ObservedAgent, player, Context.LocalPlayerRef == Context.ObservedPlayerRef);
					}
				}
			}

			// Phase 7: the zone timer is the Brownout HUD's box 2; the sample's zone widget stays off
			_shrinkingArea.SetActive(false);

			if (_hud != null)
			{
				_hud.Tick(_canvas == null || _canvas.enabled == true);
			}

			if (_localAgent == null)
				return;

			_effects.UpdateEffects(_localAgent);
		}

		/// <summary>Phase 6a: the sample widgets the Brownout HUD replaces stay in the scene, switched off.</summary>
		private void HideSampleWidgets()
		{
			_health.SetActive(false);
			_weapons.SetActive(false);
			_interactions.SetActive(false);
			_hitDamage.SetActive(false); // Floating damage numbers removed (Logan, 6 Oct)
			_killFeed.SetActive(false);
			_player.SetActive(false);
			_jetpack.SetActive(false);

			// The crosshair stays active for its hit sounds, drawn invisible
			var crosshairGroup = _crosshair.GetComponent<CanvasGroup>();
			if (crosshairGroup == null)
			{
				crosshairGroup = _crosshair.gameObject.AddComponent<CanvasGroup>();
			}
			crosshairGroup.alpha = 0.0f;

			var root = _canvas != null ? _canvas.transform : transform;
			foreach (var minimap in root.GetComponentsInChildren<UIMinimap>(true))
			{
				minimap.gameObject.SetActive(false);
			}

			// The sample's PLACE / mode and time / SCORE bar sits where the compass goes
			foreach (var panel in GetComponentsInChildren<UIGameplayPanel>(true))
			{
				panel.gameObject.SetActive(false);
			}
		}

		// PRIVATE MEMBERS

		private void OnHitPerformed(HitData hitData)
		{
			_crosshair.HitPerformed(hitData);
			_hitDamage.HitPerformed(hitData);
		}

		// Phase 6a: kills, joins and announcements go to the Brownout kill feed; the DOWNED / KILLED banners come from the observed
		// player's own hits (BrownoutHud), so the sample's ENEMY KILLED / YOU WERE KILLED events are gone

		private void OnAgentDeath(KillData killData)
		{
			if (_hud != null)
			{
				_hud.OnAgentDeath(killData);
			}

			if (killData.KillerRef == Context.ObservedPlayerRef && killData.VictimRef != Context.ObservedPlayerRef)
			{
				SceneUI.PlaySound(_enemyKilledSound);
			}
			else if (killData.VictimRef == Context.ObservedPlayerRef)
			{
				SceneUI.PlaySound(_playerDeathSound);
			}
		}

		private void OnPlayerEliminated(PlayerRef playerRef)
		{
		}

		private void OnPlayerJoined(PlayerRef playerRef)
		{
			var player = Context.NetworkGame.GetPlayer(playerRef);
			if (player == null || _hud == null)
				return;

			_hud.ShowSystem($"<color=#FFD940>{player.Nickname} joined</color>");
		}

		private void OnPlayerLeft(string nickname)
		{
			if (_hud != null)
			{
				_hud.ShowSystem($"<color=#FFD940>{nickname} left</color>");
			}
		}

		private void OnAnnounce(AnnouncementData announcement)
		{
			if (announcement.FeedMessage.HasValue() == false || _hud == null)
				return;

			_hud.ShowSystem(announcement.FeedMessage);
		}

		private void OnMenuButton()
		{
			Context.Input.TrigggerBackAction();
		}

		private void SetLocalAgent(Agent agent, Player player, bool isLocalPlayer)
		{
			if (_localAgent != null)
			{
				_localAgent.Health.HitPerformed -= OnHitPerformed;
				_localAgent.Health.HitTaken -= OnHitTaken;

				_localAgent.Interactions.InteractionFailed -= OnInteractionFailed;
			}

			_localAgent   = agent;
			_localAgentId = agent.Id;

			_crosshair.SetActive(true);
			_effects.SetActive(true);
			_spectatingGroup.SetActive(false); // Phase 6b: the Brownout HUD shows "SPECTATING <name>" under the compass

			if (_hud != null)
			{
				_hud.SetAgent(agent);
			}

			agent.Health.HitPerformed += OnHitPerformed;
			agent.Health.HitTaken += OnHitTaken;
			agent.Interactions.InteractionFailed += OnInteractionFailed;
		}

		private void ClearLocalAgent()
		{
			_crosshair.SetActive(false);
			_effects.SetActive(false);
			_spectatingGroup.SetActive(false);

			if (_hud != null)
			{
				_hud.SetAgent(null);
			}

			if (_localAgent != null)
			{
				_localAgent.Health.HitPerformed -= OnHitPerformed;
				_localAgent.Health.HitTaken -= OnHitTaken;
				_localAgent.Interactions.InteractionFailed -= OnInteractionFailed;

				_localAgent   = null;
				_localAgentId = default;
			}
		}

		private void OnHitTaken(HitData hitData)
		{
			_effects.OnHitTaken(hitData);
		}

		private void OnInteractionFailed(string reason)
		{
			_events.ShowEvent(new GameplayEventData
			{
				Name        = string.Empty,
				Description = reason,
				Color       = _interactionFailedColor,
				Sound       = _interactionFailedSound,
			}, false, true);
		}

	}
}
