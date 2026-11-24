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
				BccChat.Send(causedBy, "Syntax: /groups {playername}");
				return true;
			}

			string targetPlayerName = m.Groups["targetplayername"].Value;
			string error;
			Players.Player targetPlayer;
			if (!PlayerHelper.TryGetPlayer(targetPlayerName, out targetPlayer, out error, true)) {
				BccChat.Send(causedBy, $"Could not find player '{targetPlayerName}'; {error}");
				return true;
			}

			string notice = NetworkInfo.PermissionNotice(causedBy);
			if (notice != null) {
				BccChat.Send(causedBy, $"<color=#FFB347>{notice}</color>");
			}
			List<RoleIndex.Holding> held = RoleIndex.Of(targetPlayer);
			List<RoleIndex.Holding> assigned = held.FindAll(h => h.Kind != RoleIndex.HoldKind.Inherited);
			List<RoleIndex.Holding> inherited = held.FindAll(h => h.Kind == RoleIndex.HoldKind.Inherited);
			if (assigned.Count == 0) {
				BccChat.Send(causedBy, $"{ChatColors.GetDisplayName(targetPlayer)} has no role");
			} else {
				BccChat.Send(causedBy, $"{ChatColors.GetDisplayName(targetPlayer)} roles: {string.Join(", ", assigned.ConvertAll(h => ChatColors.FormatRole(h.Role) + (h.Kind == RoleIndex.HoldKind.Default ? Style.Muted(" (" + RoleIndex.Describe(h) + ")") : "")))}");
			}
			if (inherited.Count > 0) {
				BccChat.Send(causedBy, Style.Muted("through these also: ") + string.Join(", ", inherited.ConvertAll(h => ChatColors.FormatRole(h.Role))));
			}
			return true;
		}
	}
}
