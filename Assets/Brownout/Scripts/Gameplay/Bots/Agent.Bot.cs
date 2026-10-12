using Fusion;
using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Bots (phase 9): a bot is a dummy (an agent with no player behind it) with a brain. BotNumber is set by the server before the agent
	/// spawns (Runner.Spawn's onBeforeSpawned) and is networked, so every peer knows the agent is a bot and its name. The brain itself only
	/// exists on the server; it writes the agent's input every tick (AgentInput.BeforeTick), so a bot moves, aims, fires, reloads and
	/// heals through the same code as a player.
	/// </summary>
	public sealed partial class Agent
	{
		// PUBLIC MEMBERS

		/// <summary>0 = not a bot; otherwise the bot's number (its name comes from BotSettings.Names).</summary>
		[Networked, HideInInspector]
		public byte     BotNumber { get; set; }

		public bool     IsBot     => Object != null && Object.IsValid == true && BotNumber > 0;
		public string   BotName   => BotSettings.Current.GetName(BotNumber);

		/// <summary>Server: the pose is updated every this many ticks (1 = every tick; set by the brain for distant bots, read by Character).</summary>
		public int      AnimationEveryTicks { get; set; } = 1;

		/// <summary>The brain, on the server only (null elsewhere and for players and dummies).</summary>
		public BotBrain Bot       => _bot;

		// PRIVATE MEMBERS

		private BotBrain _bot;

		// PRIVATE METHODS

		/// <summary>From Spawned: the server gives a bot its brain (agents are pooled, so the same object can come back as a player).</summary>
		private void SpawnedBot()
		{
			AnimationEveryTicks = 1;   // Agents are pooled
			if (HasStateAuthority == true && BotNumber > 0)
			{
				if (_bot == null)
				{
					_bot = new BotBrain(this);
				}

				_bot.OnSpawned();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
				if (GetComponent<BotDebugDraw>() == null) { gameObject.AddComponent<BotDebugDraw>(); }
#endif
			}
			else if (_bot != null)
			{
				_bot.OnDespawned();
				_bot = null;
			}
		}

		private void DespawnedBot()
		{
			if (_bot != null)
			{
				_bot.OnDespawned();
				_bot = null;
			}
		}
	}
}
