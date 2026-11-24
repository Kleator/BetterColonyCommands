using System.Text.RegularExpressions;
using System.Collections.Generic;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

	public class WarpChatCommand : IChatCommand
	{

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals ("/warp")) {
				return false;
			}
			if (!PermissionsManager.HasPermission(causedBy, AntiGrief.MOD_PREFIX + "warp.self")) {
				BccChat.Send(causedBy, "<color=red>You don't have permission to warp</color>");
				return true;
			}
			var m = Regex.Match(chattext, @"/warp (?<targetplayername>['].+[']|[^ ]+)( (?<teleportplayername>['].+[']|[^ ]+))?");
			if (!m.Success) {
				BccChat.Send(causedBy, "Syntax: /warp [targetplayername] or /warp [targetplayername] [teleportplayername]");
				return true;
			}

			string targetPlayerName = m.Groups["targetplayername"].Value;
			string error;
			Players.Player targetPlayer;
			if (!PlayerHelper.TryGetPlayer(targetPlayerName, out targetPlayer, out error)) {
				BccChat.Send(causedBy, $"Could not find target player '{targetPlayerName}'; {error}");
				return true;
			}

			Players.Player teleportPlayer = causedBy;
			string teleportPlayerName = m.Groups["teleportplayername"].Value;
			if (teleportPlayerName != null && teleportPlayerName.Length > 0) {
				if (!PermissionsManager.HasPermission(causedBy, AntiGrief.MOD_PREFIX + "warp.player")) {
					BccChat.Send(causedBy, "<color=red>You don't have permission to warp other players</color>");
					return true;
				}

				if (!PlayerHelper.TryGetPlayer(teleportPlayerName, out teleportPlayer, out error)) {
					BccChat.Send(causedBy, $"Could not find teleport player '{teleportPlayerName}'; {error}");
					return true;
				}
			}

			Teleport.TeleportTo(teleportPlayer, targetPlayer.PositionStanding);
			return true;
		}
	}
}
