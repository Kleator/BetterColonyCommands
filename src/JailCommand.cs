using Chatting;
using Chatting.Commands;
using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace ColonyCommands
{

  public class JailCommand : IChatCommand
  {

    public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
    {
	  if (splits.Count == 0 || !splits[0].Equals("/jail")) {
		return false;
		}
      if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "jail")) {
        return true;
      }

      // tried first: a numeric jailtime unambiguously marks where the (possibly multi-word,
      // unquoted) player name ends, e.g. "/jail Aldric Stormvale 30 being toxic"
      var m = Regex.Match(chattext, @"/jail (?<player>['].+[']|.+?) (?<jailtime>[0-9]+) (?<reason>.+)$");
      if (!m.Success) {
        // no jailtime given - nothing left to disambiguate a multi-word name from the reason,
        // so an unquoted multi-word name still requires quotes here
        m = Regex.Match(chattext, @"/jail (?<player>['].+[']|[^ ]+) ?(?<reason>.+)?$");
      }
      if (!m.Success) {
        BccChat.Send(causedBy, "Syntax error, use /jail <player> [time] <reason>");
        return true;
      }

      Players.Player target;
      string targetName = m.Groups["player"].Value;
      string error;
      if (!PlayerHelper.TryGetPlayer(targetName, out target, out error, true)) {
        BccChat.Send(causedBy, $"Could not find player {targetName}: {error}");
        return true;
      }

      int jailtime = 0;
      var timeval = m.Groups["jailtime"].Value;
      if (timeval.Equals("")) {
        jailtime = JailManager.jailConfig.defaultJailTime;
      } else {
        if (!int.TryParse(timeval, out jailtime)) {
          BccChat.Send(causedBy, $"Could not identify time value {timeval}");
        }
      }
      string reason = m.Groups["reason"].Value;

      if (JailManager.IsPlayerJailed(target)) {
        BccChat.Send(causedBy, $"{ChatColors.GetDisplayName(target)} is already in jail.");
        return true;
      }

      JailManager.jailPlayer(target, causedBy, reason, jailtime);
      BccChat.Send(causedBy, $"Threw {ChatColors.GetDisplayName(target)} into the jail for {jailtime} minutes");

      return true;
    }
  }
}

