using System;
using System.Collections.Generic;
using Chatting;

namespace ColonyCommands
{
	// /roletest            - status of your role test
	// /roletest end        - ends your role test now (always allowed, also without any permission)
	// /roletest {role}     - tests a role for 2 minutes (role managers: mods.scarabol.commands.config)
	public class RoleTestChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/roletest", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			if (causedBy == null) {
				return true;
			}
			string arg = splits.Count >= 2 ? string.Join(" ", splits.GetRange(1, splits.Count - 1)).Trim() : "";
			RoleTestManager.TestRecord rec = RoleTestManager.Get(causedBy);
			if (arg.Length == 0) {
				if (rec != null) {
					BccChat.Send(causedBy, $"You are testing {ChatColors.FormatRole(rec.Role)} - {RoleTestManager.SecondsLeft(rec)} seconds left. End it now: {Style.Cmd("/roletest end")}");
				} else {
					BccChat.Send(causedBy, Style.Muted("No role test running. Role managers start one in /roles (Test button) or with /roletest {role}."));
				}
				return true;
			}
			if (arg.Equals("end", StringComparison.OrdinalIgnoreCase) || arg.Equals("stop", StringComparison.OrdinalIgnoreCase)) {
				if (!RoleTestManager.End(causedBy, "ended by you")) {
					BccChat.Send(causedBy, Style.Muted("No role test running."));
				}
				return true;
			}
			if (!BccPermissions.CanEditConfig(causedBy)) {
				BccChat.Send(causedBy, Style.Error($"You don't have permission to test roles ({BccPermissions.PERMISSION_CONFIG})."));
				return true;
			}
			string error = RoleTestManager.Start(causedBy, arg);
			if (error != null) {
				BccChat.Send(causedBy, Style.Error(error));
			}
			return true;
		}
	}
}
