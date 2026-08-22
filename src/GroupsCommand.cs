using System.Collections.Generic;
using System.Text.RegularExpressions;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{
	public class GroupsChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/groups")) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "groups")) {
				return true;
			}

			var m = Regex.Match(chattext, @"/groups (?<targetplayername>['].+[']|.+)");
			if (!m.Success) {
				Chat.Send(causedBy, "Syntax: /groups {playername}");
				return true;
			}

			string targetPlayerName = m.Groups["targetplayername"].Value;
			string error;
			Players.Player targetPlayer;
			if (!PlayerHelper.TryGetPlayer(targetPlayerName, out targetPlayer, out error, true)) {
				Chat.Send(causedBy, $"Could not find player '{targetPlayerName}'; {error}");
				return true;
			}

			List<string> groups = PermissionsHelper.GetGroupNames(targetPlayer);
			if (groups.Count == 0) {
				Chat.Send(causedBy, $"{ChatColors.GetDisplayName(targetPlayer)} has no explicitly assigned groups (default group)");
			} else {
				Chat.Send(causedBy, $"{ChatColors.GetDisplayName(targetPlayer)} groups: {string.Join(", ", groups)}");
			}
			return true;
		}
	}
}
