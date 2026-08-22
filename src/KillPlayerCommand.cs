using System.Collections.Generic;
using System.Text.RegularExpressions;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

	public class KillPlayerChatCommand : IChatCommand
	{

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/killplayer")) {
				return false;
			}
			var m = Regex.Match(chattext, @"/killplayer( (?<targetplayername>['].+[']|.+))?");
			if (!m.Success) {
				Chat.Send(causedBy, "Syntax: /killplayer [targetplayername]");
				return true;
			}

			Players.Player targetPlayer = causedBy;
			string targetPlayerName = m.Groups["targetplayername"].Value;
			string error;
			if (targetPlayerName.Length > 0) {
				if (!PlayerHelper.TryGetPlayer(targetPlayerName, out targetPlayer, out error, true)) {
					Chat.Send(causedBy, $"Could not find '{targetPlayerName}'; {error}");
					return true;
				}
			}

			string permission = AntiGrief.MOD_PREFIX + "killplayer";
			if (causedBy == targetPlayer) {
				permission += ".self";
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, permission)) {
				return true;
			}

			Players.OnDeath(targetPlayer);
			targetPlayer.SendHealthPacket();
			if (targetPlayer == causedBy) {
				Chat.SendToConnected($"Player {ChatColors.GetDisplayName(causedBy)} committed suicide");
			} else {
				Chat.SendToConnected($"Player {ChatColors.GetDisplayName(targetPlayer)} was killed by {ChatColors.GetDisplayName(causedBy)}");
			}
			return true;
		}
	}
}

