using Chatting;
using Chatting.Commands;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ColonyCommands
{

  public class JailReleaseCommand : IChatCommand
  {

    public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
    {
	  if (splits.Count == 0 || !splits[0].Equals("/jailrelease")) {
		return false;
		}
      if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "jail")) {
        return true;
      }

      var m = Regex.Match(chattext, @"/jailrelease (?<player>.+)");
      if (!m.Success) {
        BccChat.Send(causedBy, "Syntax error, use /jailrelease <player>");
        return true;
      }

      Players.Player target;
      string targetName = m.Groups["player"].Value;
      string error;
      if (!PlayerHelper.TryGetPlayer(targetName, out target, out error, true)) {
        BccChat.Send(causedBy, $"Could not find player {targetName}: {error}");
        return true;
      }

      if (!JailManager.IsPlayerJailed(target)) {
        BccChat.Send(causedBy, $"{ChatColors.GetDisplayName(target)} is currently not in jail");
        return true;
      }

      JailManager.releasePlayer(target, causedBy);
      BccChat.Send(causedBy, $"Released {ChatColors.GetDisplayName(target)} from jail");
      BccChat.Send(target, $"<color=yellow>{ChatColors.GetDisplayName(causedBy)} released you from jail</color>");

      return true;
    }
  }
}

