using System.Collections.Generic;
using System.Text.RegularExpressions;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{
	public class TpaChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/tpa")) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "tpa.self")) {
				return true;
			}

			var m = Regex.Match(chattext, @"/tpa (?<targetplayername>['].+[']|.+)");
			if (!m.Success) {
				Chat.Send(causedBy, "Syntax: /tpa {targetplayername}");
				return true;
			}

			string targetPlayerName = m.Groups["targetplayername"].Value;
			string error;
			Players.Player targetPlayer;
			if (!PlayerHelper.TryGetPlayer(targetPlayerName, out targetPlayer, out error)) {
				Chat.Send(causedBy, $"Could not find target player '{targetPlayerName}'; {error}");
				return true;
			}

			if (!TpaManager.TryCreateRequest(causedBy, targetPlayer, out error)) {
				Chat.Send(causedBy, $"<color=red>{error}</color>");
			}
			return true;
		}
	}
}
