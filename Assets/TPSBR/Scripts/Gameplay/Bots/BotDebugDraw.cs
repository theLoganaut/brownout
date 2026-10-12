using UnityEngine;

namespace TPSBR
{
	/// <summary>
	/// Bots (phase 9), editor and development builds: draws a bot's path, its state and goal, and a line to its target in the Scene view
	/// while Brownout → Test → Bots → Show Bot Debug is on. Added to a bot's agent on the server.
	/// </summary>
	public sealed class BotDebugDraw : MonoBehaviour
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		private Agent _agent;

		private void Awake()
		{
			_agent = GetComponent<Agent>();
		}

		private void OnDrawGizmos()
		{
			if (BotDebug.ShowDebug == false || _agent == null || _agent.Bot == null)
				return;

			BotBrain      bot = _agent.Bot;
			BotNavigation nav = bot.Navigation;
			Vector3       up  = Vector3.up * 0.2f;

			Gizmos.color = new Color(0.2f, 0.9f, 1.0f);
			if (nav.HasPath == true)
			{
				Vector3 previous = transform.position + up;
				for (int i = nav.CornerIndex; i < nav.CornerCount; ++i)
				{
					Vector3 corner = nav.GetCorner(i) + up;
					Gizmos.DrawLine(previous, corner);
					previous = corner;
				}
			}
			else if (nav.HasDestination == true)
			{
				Gizmos.DrawLine(transform.position + up, nav.Destination + up);
			}

			if (bot.Target != null)
			{
				Gizmos.color = bot.TargetVisible == true ? Color.red : new Color(1.0f, 0.6f, 0.2f);
				Gizmos.DrawLine(transform.position + Vector3.up * 1.6f, bot.Target.transform.position + Vector3.up * 1.3f);
			}

#if UNITY_EDITOR
			UnityEditor.Handles.Label(transform.position + Vector3.up * 2.3f, $"{_agent.name} [{bot.State}]\n{bot.Goal}\nskill {bot.SkillValue:F2}");
#endif
		}
#endif
	}
}
