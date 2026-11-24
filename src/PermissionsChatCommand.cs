using System;
using System.Collections.Generic;
using System.Linq;
using Chatting;

namespace ColonyCommands
{
	// /permissions [filter|page] - every permission that exists in the game (registered by any mod or
	// written into the permission files), its mod, description, who has it and whether it is unused.
	// Only for players with mods.scarabol.commands.permissions (groups with all permissions have it).
	public class PermissionsChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/permissions", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			if (!BccPermissions.CanSeePermissionOverview(causedBy)) {
				PermissionsManager.CheckAndWarnPermission(causedBy, BccPermissions.PERMISSION_OVERVIEW);
				return true;
			}
			string arg = splits.Count > 1 ? chattext.Substring(chattext.IndexOf(' ') + 1).Trim() : "";
			bool chatOnly = false;
			if (arg.StartsWith("chat", StringComparison.OrdinalIgnoreCase)) {
				chatOnly = true;
				arg = arg.Substring(4).Trim();
			}
			int page = 1;
			string filter = arg;
			if (int.TryParse(arg, out int n)) {
				page = n;
				filter = "";
			}
			if (!chatOnly && !BccChat.IsCapturing(causedBy) && UiPreferences.UsesWindow(causedBy, "/permissions")) {
				UiManager.Open(causedBy, new UiManager.PageState { Kind = "perms", Filter = filter, Page = page }, true);
				return true;
			}
			BccPermissions.Refresh(true);
			List<string> lines = BuildLines(filter, out int unused, out int total);
			List<string> entries = TextHelper.Page(lines, page, 6, out int p, out int pages);
			BccChat.Send(causedBy, Style.Header($"Permissions ({total}, {unused} unused) - page {p}/{pages}"));
			string notice = NetworkInfo.PermissionNotice(causedBy);
			if (notice != null) {
				BccChat.Send(causedBy, $"<color=#FFB347>{notice}</color>");
			}
			foreach (string l in entries) {
				BccChat.Send(causedBy, l);
			}
			if (pages > 1) {
				BccChat.Send(causedBy, Style.Muted("/permissions <page>, /permissions <filter>, /permissions unused|used|open"));
			}
			return true;
		}

		public static List<string> BuildLines(string filter, out int unused, out int total)
		{
			List<string> lines = new List<string>();
			unused = 0;
			total = 0;
			string f = (filter ?? "").ToLowerInvariant().Trim();
			// keywords: "unused" (nobody has it), "used", "open" (auto-created, open to everyone)
			string state = f == "unused" || f == "used" || f == "open" ? f : null;
			if (state != null) {
				f = "";
			}
			foreach (BccPermissions.PermissionInfo info in BccPermissions.GetAll()) {
				if (state != null) {
					BccPermissions.AssignmentInfo s0 = BccPermissions.GetAssignment(info.Permission);
					if ((state == "unused" && s0.IsUsed) || (state == "used" && !s0.IsUsed) || (state == "open" && !(info.OpenByDefault && !s0.IsUsed))) {
						continue;
					}
				}
				if (f.Length > 0 && !info.Permission.ToLowerInvariant().Contains(f) && !info.Mod.ToLowerInvariant().Contains(f)
					&& !(info.Description ?? "").ToLowerInvariant().Contains(f)) {
					continue;
				}
				total++;
				BccPermissions.AssignmentInfo a = BccPermissions.GetAssignment(info.Permission);
				List<string> who = new List<string>();
				if (a.GroupsExact.Count > 0) who.Add(string.Join(", ", a.GroupsExact.Select(ChatColors.FormatRole)));
				if (a.GroupsViaParent.Count > 0) who.Add(Style.Muted("via parent: ") + string.Join(", ", a.GroupsViaParent.Select(ChatColors.FormatRole)));
				if (a.UsersExact + a.UsersViaParent > 0) who.Add(Style.Muted($"{a.UsersExact + a.UsersViaParent} player(s)"));
				string status;
				if (!a.IsUsed) {
					unused++;
					status = info.OpenByDefault ? Style.Success("open to everyone (not assigned)") : Style.Error("unused");
				} else {
					status = string.Join(" | ", who);
				}
				string desc = string.IsNullOrEmpty(info.Description) ? "" : Style.Muted(" - " + TextHelper.Truncate(info.Description, 90));
				lines.Add($"{Style.Cmd(info.Permission.Length == 0 ? "(all)" : info.Permission)} {Style.SubHeader("[" + info.Mod + "]")}{desc}\n    {status}");
			}
			return lines;
		}
	}
}
