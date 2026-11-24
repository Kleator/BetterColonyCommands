using Pipliz;
using Chatting;
using Chatting.Commands;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ColonyCommands
{
	public class HelpCommand: IChatCommand
	{

		private static Dictionary<string, string> commandPermissionList = new Dictionary<string, string>() {
			{"/announcements",	"mods.scarabol.commands.announcements.list"},
			{"/antigrief",		""},
			// {"areashow", "DISABLED"},
			{"/ban",			"mods.scarabol.commands.ban"},
			{"/banlog",			"mods.scarabol.commands.ban"},
			{"/bannername",		""},
			{"/bcreload",		"mods.scarabol.commands.bcreload"},
			{"/beds",			"mods.scarabol.commands.beds"},
			{"/colonycap",		""},
			{"/colortest",		"mods.scarabol.commands.mute"},   // hide it for normal users, permission randomly choosen
			{"/customarea",		""},
			{"/drain",			"mods.scarabol.commands.drain"},
			{"/god",			"mods.scarabol.commands.god"},
			{"/graceperiod",	"mods.scarabol.commands.antigrief.setgraceperiod"},
			{"/groups",			"mods.scarabol.commands.groups"},
			{"/inactive",		"mods.scarabol.commands.inactive"},
			{"/itemid",			""},
			{"/jail",			"mods.scarabol.commands.jail"},
			{"/jailleave",		""},
			{"/leavejail",		""},
			{"/jailrec",		"mods.scarabol.commands.jail"},
			{"/jailrelease",	"mods.scarabol.commands.jail"},
			{"/jailtime",		""},
			{"/jailvisit",		""},
			{"/visitjail",		""},
			{"/kick",			"mods.scarabol.commands.kick"},
			{"/killnpcs",		"mods.scarabol.commands.killnpcs.self"},
			{"/killplayer",		"mods.scarabol.commands.killplayer.self"},
			{"/lastseen",		""},
			{"/list",			"mods.scarabol.commands.listplayer"},
			{"/mtop",			""},
			{"/noflight",		"mods.scarabol.commands.noflight"},
			{"/online",			""},
			{"/promote",		"permissions.setgroup"},
			{"/purgebanner",	"mods.scarabol.commands.purgebanner"},
			{"/rpban",			"mods.scarabol.commands.roleplayban"},
			{"/rpunban",		"mods.scarabol.commands.roleplayban"},
			{"/rp",				""},
			{"/serverpop",		""},
			{"/setcolonyname",	"mods.scarabol.commands.setcolonyname"},
			{"/setjail",		"mods.scarabol.commands.setjailposition"},
			{"/mute",			"mods.scarabol.commands.mute"},
			{"/unmute",			"mods.scarabol.commands.mute"},
			{"/spawnnpc",		"mods.scarabol.commands.npcandbeds"},
			{"/stuck",			""},
			{"/top",			""},
			{"/total",			""},
			{"/tpa",			"mods.scarabol.commands.tpa.self"},
			{"/tpaccept",		""},
			{"/tpadeny",		""},
			{"/trade",			"mods.scarabol.commands.trade"},
			{"/trash",			""}, {"travel",			"mods.scarabol.commands.travelpaths"},
			{"/war",			""},
			{"/warpbanner",		"mods.scarabol.commands.warp.banner"},
			{"/warp",			"mods.scarabol.commands.warp.self"},
			{"/warpplace",		"mods.scarabol.commands.warp.place"},
			{"/warpspawn",		"mods.scarabol.commands.warp.spawn.self"},
			{"/spawn",			""},
			{"/w",				""},
			{"/whisper",		""}
		};


		// Other mods (Pytonia Automation, Starter-Kit, WorldEdit, Minimap, ...) add their commands to
		// commandPermissionList above via reflection - that field (name, type, private static) must stay
		// exactly as it is. Its entries are merged into the CommandRegistry, which is what /help shows.
		public static List<KeyValuePair<string, string>> GetLegacyEntries()
		{
			return new List<KeyValuePair<string, string>>(commandPermissionList);
		}

		const int CHAT_PAGE_SIZE = 12;

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/help", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}

			string arg = splits.Count > 1 ? chattext.Substring(chattext.IndexOf(' ') + 1).Trim() : "";
			if (arg == "?" || arg.Equals("help", StringComparison.OrdinalIgnoreCase)) {
				BccChat.Send(causedBy, $"{Style.Cmd("/help")} opens the command window. {Style.CmdRef("/help chat")} lists commands in chat, {Style.CmdRef("/help <filter>")} filters, {Style.CmdRef("/help <page>")} pages, {Style.CmdRef("/help <command>")} shows details.");
				return true;
			}
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

			// "/help /warp" or "/help warp" with an exact command -> details
			BccCommandInfo exact = null;
			if (filter.Length > 0 && CommandRegistry.TryGet(filter, out BccCommandInfo found) && CommandRegistry.CanUse(causedBy, found)) {
				exact = found;
			}

			if (!chatOnly && !BccChat.IsCapturing(causedBy) && UiPreferences.UsesWindow(causedBy, "/help")) {
				UiManager.Open(causedBy, new UiManager.PageState { Kind = "help", Filter = exact != null ? "" : filter, Page = page }, true);
				if (exact != null) {
					UiManager.Open(causedBy, new UiManager.PageState { Kind = "cmd", Key = exact.Command });
				}
				return true;
			}

			if (exact != null) {
				BccChat.Send(causedBy, $"{Style.Cmd(exact.Command)} {Style.SubHeader("[" + exact.Mod + "]")}");
				BccChat.Send(causedBy, string.IsNullOrEmpty(exact.Description) ? Style.Muted("No description.") : exact.Description);
				BccChat.Send(causedBy, $"{Style.Muted("Usage:")} {Style.CmdRef(exact.Usage)}");
				if (exact.Aliases.Count > 0) {
					BccChat.Send(causedBy, $"{Style.Muted("Aliases:")} {string.Join(", ", exact.Aliases)}");
				}
				return true;
			}

			List<BccCommandInfo> visible = CommandRegistry.GetVisible(causedBy, filter);
			if (visible.Count == 0) {
				BccChat.Send(causedBy, Style.Error(filter.Length > 0 ? $"No commands match '{filter}'." : "No commands available to you."));
				return true;
			}
			List<BccCommandInfo> entries = TextHelper.Page(visible, page, CHAT_PAGE_SIZE, out int p, out int pages);
			BccChat.Send(causedBy, Style.Header($"Commands ({visible.Count}) - page {p}/{pages}"));
			string lastMod = null;
			foreach (BccCommandInfo c in entries) {
				if (c.Mod != lastMod) {
					lastMod = c.Mod;
					BccChat.Send(causedBy, Style.SubHeader(c.Mod + ":"));
				}
				string desc = string.IsNullOrEmpty(c.Description) ? "" : " - " + c.Description;
				BccChat.Send(causedBy, $"{Style.Cmd(c.Command)}{desc}");
			}
			if (pages > 1) {
				BccChat.Send(causedBy, Style.Muted($"/help {(chatOnly ? "chat " : "")}{System.Math.Min(p + 1, pages)} for the next page, /help <command> for details"));
			}
			return true;
		}
	}
}
