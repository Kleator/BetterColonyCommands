using System;
using System.Collections.Generic;
using Notifications;
using Pipliz;
using Shared.Notifications;

namespace ColonyCommands
{
	// When a colony's colonists or job blocks (not the player) try to change protected blocks,
	// the attempt is blocked silently and the colony's owners get ONE entry in the game's own
	// notification list per session (instead of chat spam on every single block). Marking it read
	// or deleting it is the acknowledgement; the next notice comes at the earliest after all
	// owners of that colony reconnected.
	public static class ProtectionNotices
	{
		static readonly HashSet<int> notifiedGroups = new HashSet<int>();
		static readonly HashSet<int> chatFallbackGroups = new HashSet<int>();

		public static void Report(Colony colony, Pipliz.Vector3Int position, string what)
		{
			ColonyGroup group = colony?.ColonyGroup;
			if (group == null) {
				return;
			}
			int id = group.ColonyGroupID.Value;
			if (!notifiedGroups.Add(id)) {
				return; // already told this session
			}
			string colonyName = TextHelper.SafeName(colony.Name);
			string preview = $"Colonists of {colonyName} tried to change protected blocks";
			string message = $"Colonists or job blocks of your colony '{colonyName}' tried to change blocks at {position.x} {position.y} {position.z} ({what}). "
				+ "Those changes are blocked - move the job/area out of the protected zone or ask the owner/staff for building rights (/antigrief grant). "
				+ "You get this notice only once per session; mark it read or delete it.";
			try {
				TextNotification notice = TextNotification.CreateNotification(ENotificationOwnerKind.ColonyGroup, id,
					"Protected area", "Protected area", preview, message, "alert_generic", true, true, "bcc.protection");
				group.NotificationManager.Notifications.Enqueue(notice.ID);
				group.NotificationManager.OnEditedNotification(notice);
			} catch (Exception e) {
				// fallback: a single chat line per session
				Log.WriteWarning($"Better Colony Commands: could not create a protection notification ({e.Message}), using chat");
				if (chatFallbackGroups.Add(id)) {
					for (int i = 0; i < group.Owners.Count; i++) {
						BccChat.Send(group.Owners.GetAt(i), $"<color=#FFD166>{message}</color>");
					}
				}
			}
		}

		public static void OnPlayerDisconnected(Players.Player player)
		{
			if (player == null) {
				return;
			}
			// a colony may notify again once none of its owners is online any more
			for (int i = 0; i < player.ColonyGroups.Count; i++) {
				ColonyGroup g = player.ColonyGroups[i];
				bool anyOnline = false;
				for (int j = 0; j < g.Owners.Count; j++) {
					Players.Player o = g.Owners.GetAt(j);
					if (o != player && o.ConnectionState == Players.EConnectionState.Connected) {
						anyOnline = true;
					}
				}
				if (!anyOnline) {
					notifiedGroups.Remove(g.ColonyGroupID.Value);
					chatFallbackGroups.Remove(g.ColonyGroupID.Value);
				}
			}
		}
	}
}
