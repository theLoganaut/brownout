namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;

	/// <summary>
	/// Phase 3c Gun Bench helpers (editor and development builds): change the guns and attachments of the player you're
	/// looking through, on the server. Used by the Gun Bench keys below and by BrownoutTestTools / BrownoutGunBench from the MCP.
	/// They need the server's copy of the player, so they work on the host (and in Multipeer, from either peer).
	/// </summary>
	public static class GunBench
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>The player of the peer with input (the one you're looking through), as the server has it.</summary>
		public static Agent GetServerAgentOfObserved()
		{
			Agent observed = null;
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object != null && agent.Object.IsValid == true && agent.IsObserved == true && agent.Context.HasInput == true)
				{
					observed = agent;
					break;
				}
			}

			if (observed == null)
				return null;

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Runner != null && agent.Runner.IsServer == true && agent.Object != null && agent.Object.IsValid == true && agent.Object.InputAuthority == observed.Object.InputAuthority)
					return agent;
			}

			return null;
		}

		public static BulletWeapon GetServerGun(out Agent agent)
		{
			agent = GetServerAgentOfObserved();
			return agent != null ? agent.Weapons.CurrentWeapon as BulletWeapon : null;
		}

		/// <summary>Gives the next (+1) or previous (-1) Brownout gun in GunplaySettings.TestGuns order.</summary>
		public static string StepGun(int direction)
		{
			GunplaySettings settings = GunplaySettings.Current;
			if (settings.TestGuns == null || settings.TestGuns.Length == 0)
				return "No test guns (run Brownout → Data → Import Guns).";

			BulletWeapon current = GetServerGun(out Agent agent);
			if (agent == null)
				return "No observed player on a running host.";

			int index = -1;
			for (int i = 0; i < settings.TestGuns.Length; ++i)
			{
				if (current != null && settings.TestGuns[i] is BulletWeapon gun && gun.GunData == current.GunData)
				{
					index = i;
					break;
				}
			}

			int next = (index + direction + settings.TestGuns.Length) % settings.TestGuns.Length;
			if (index < 0 && direction < 0)
			{
				next = settings.TestGuns.Length - 1;
			}

			Weapon prefab = settings.TestGuns[next];
			return agent.Weapons.DebugGiveWeapon(prefab) == true ? $"Gave {prefab.DisplayName}." : "Couldn't give the gun.";
		}

		/// <summary>Moves one slot to the next attachment the gun takes (then none, then around).</summary>
		public static string CycleSlot(EAttachmentSlot slot)
		{
			BulletWeapon gun = GetServerGun(out Agent agent);
			if (gun == null || gun.GunData == null)
				return "No Brownout gun in hand.";

			List<AttachmentData> options = GetOptions(gun.GunData, slot);
			if (options.Count == 1)
				return $"{gun.GunData.GunName} takes nothing in the {slot} slot.";

			int index = Mathf.Max(0, options.IndexOf(gun.GetAttachment(slot)));
			AttachmentData next = options[(index + 1) % options.Count];

			gun.SetAttachment(slot, next, out string result);
			return result;
		}

		public static string Strip()
		{
			BulletWeapon gun = GetServerGun(out Agent agent);
			if (gun == null || gun.GunData == null)
				return "No Brownout gun in hand.";

			for (int i = 0; i < AttachmentData.SlotCount; ++i)
			{
				gun.SetAttachment((EAttachmentSlot)i, null, out _);
			}

			return $"{gun.GunData.GunName}: all attachments off.";
		}

		/// <summary>Puts a named attachment on the gun in hand (its own slot).</summary>
		public static string SetAttachment(string attachmentName)
		{
			BulletWeapon gun = GetServerGun(out Agent agent);
			if (gun == null || gun.GunData == null)
				return "No Brownout gun in hand.";

			AttachmentData attachment = GunplaySettings.Current.FindAttachment(attachmentName);
			if (attachment == null)
				return $"No attachment named {attachmentName}.";

			gun.SetAttachment(attachment.Slot, attachment, out string result);
			return result;
		}

		/// <summary>None first, then what the gun allows for that slot, in the data file's order.</summary>
		public static List<AttachmentData> GetOptions(GunData gunData, EAttachmentSlot slot)
		{
			var options = new List<AttachmentData> { null };

			AttachmentData[] attachments = GunplaySettings.Current.Attachments;
			if (attachments != null)
			{
				foreach (AttachmentData attachment in attachments)
				{
					if (attachment != null && attachment.Slot == slot && gunData.Allows(attachment) == true)
					{
						options.Add(attachment);
					}
				}
			}

			return options;
		}
#endif
	}
}
