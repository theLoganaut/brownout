using UnityEngine;
using System;
using Fusion;

namespace TPSBR
{
	[Serializable]
	[CreateAssetMenu(fileName = "GlobalSettings", menuName = "TPSBR/Global Settings")]
	public class GlobalSettings : ScriptableObject
	{
		public NetworkRunner        RunnerPrefab;
		public string               LoadingScene = "LoadingScene";
		public string               MenuScene = "Menu";
		public bool                 SimulateMobileInput;

		public AgentSettings        Agent;
		public MovementSettings     Movement;
		public GunplaySettings      Gunplay;
		public HealthSettings       Health;
		public LootSettings         Loot;
		public EquipmentSettings    Equipment;
		public HudSettings          Hud;
		public ControlsSettings     Controls;
		public MatchSettings        Match;
		public BotSettings          Bots;
		public BrownoutMapSettings  BrownoutMap;
		public MapSettings          Map;
		public NetworkSettings      Network;
		public OptionsData          DefaultOptions;
	}
}
