using System.Collections.Generic;
using System.Text.RegularExpressions;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

	public class ListPlayerChatCommand : IChatCommand
	{

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/list")) {
				return false;
			}
			Match m = Regex.Match(chattext, @"/list (?<playername>['].+[']|.+)");
			if (!m.Success) {
				BccChat.Send(causedBy, "Syntax: /list {playername}");
				return true;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "listplayer")) {
				return true;
			}

			string targetName = m.Groups["playername"].Value;
			string error;
			Players.Player targetPlayer;
			if (!PlayerHelper.TryGetPlayer(targetName, out targetPlayer, out error, true)) {
				BccChat.Send(causedBy, $"Could not find '{targetName}'; {error}");
				return true;
			}

			string lastSeen = ActivityTracker.GetLastSeen(targetPlayer.ID.ToStringReadable());
			string colonies = "";
			int count = 0;
			for (int i = 0; i < targetPlayer.ColonyGroups.Count; i++) {
				if (!colonies.Equals("")) {
					colonies += ", ";
				}
				colonies += TextHelper.SafeName(targetPlayer.ColonyGroups[i].Name);
				count++;
			}

			BccChat.Send(causedBy, $"Player {ChatColors.GetDisplayName(targetPlayer)} last seen {lastSeen}");
			BccChat.Send(causedBy, $"{ChatColors.GetDisplayName(targetPlayer)} owns {count} colonies: {colonies}");
			return true;
		}
	}
}

