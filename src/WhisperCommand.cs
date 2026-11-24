using System.Text.RegularExpressions;
using System.Collections.Generic;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

	public class WhisperChatCommand : IChatCommand
	{

		public bool TryDoCommand (Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || (!splits[0].Equals("/w") && !splits[0].Equals("/whisper"))) {
				return false;
			}
			var m = Regex.Match(chattext, @"/((w)|(whisper)) (?<targetplayername>['].+[']|[^ ]+) (?<message>.+)");
			if (!m.Success) {
				BccChat.Send(causedBy, "Syntax: /w [player] [message] or /whisper [player] [message]");
				return true;
			}
			var targetPlayerName = m.Groups["targetplayername"].Value;
			Players.Player targetPlayer;
			string error;
			if (!PlayerHelper.TryGetPlayer(targetPlayerName, out targetPlayer, out error)) {
				BccChat.Send(causedBy, $"Could not find target player '{targetPlayerName}'; {error}");
				return true;
			}
			var message = m.Groups["message"].Value;
			BccChat.Send(targetPlayer, $"<color=#00ffff>From [{ChatColors.GetDisplayName(causedBy)}]: {message}</color>");
			BccChat.Send(causedBy, $"<color=#00ffff>To [{ChatColors.GetDisplayName(targetPlayer)}]: {message}</color>");
			return true;
		}
	}
}
