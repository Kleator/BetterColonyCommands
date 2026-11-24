using System.Collections.Generic;
using System.Text.RegularExpressions;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

  public class KickChatCommand : IChatCommand
  {

    public bool TryDoCommand (Players.Player causedBy, string chattext, List<string> splits)
    {
	  if (splits.Count == 0 || !splits[0].Equals ("/kick")) {
		return false;
		}
      if (!PermissionsManager.CheckAndWarnPermission (causedBy, AntiGrief.MOD_PREFIX + "kick")) {
        return true;
      }
      var m = Regex.Match (chattext, @"/kick (?<targetplayername>['].+[']|.+)");
      if (!m.Success) {
        BccChat.Send(causedBy, "Command didn't match, use /kick [targetplayername]");
        return true;
      }
      var targetPlayerName = m.Groups ["targetplayername"].Value;
      Players.Player targetPlayer;
      string error;
      if (!PlayerHelper.TryGetPlayer (targetPlayerName, out targetPlayer, out error)) {
        BccChat.Send(causedBy, $"Could not find target player '{targetPlayerName}'; {error}");
        return true;
      }
      BccChat.Send(targetPlayer, "<color=red>You were kicked from the server</color>");
      Chat.SendToConnected ($"{ChatColors.GetDisplayName(targetPlayer)} is kicked by {ChatColors.GetDisplayName(causedBy)}");
      ModerationLog.Add(targetPlayer, "Kick", causedBy, "kicked with /kick");
      Players.Disconnect (targetPlayer);
      return true;
    }
  }
}
