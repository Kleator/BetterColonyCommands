using System;
using System.Collections.Generic;
using System.Linq;
using Chatting;
using Newtonsoft.Json.Linq;
using NetworkUI;
using NetworkUI.Items;
using Pipliz;

namespace ColonyCommands
{
	public static class UiPages
	{
		static int W => UiManager.ContentWidth;
		static int LinesPerPage => UiManager.LinesPerPage;

		// ------------------------------------------------------------------ /help window

		public static void BuildHelp(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "Commands");
			// navigation to the other windows - admin entries only for those allowed to use them
			int bw = System.Math.Min(140, (W - 8) / 6);
			List<(IItem, int)> nav = new List<(IItem, int)> {
				(UiManager.Button("open", "settings", "My settings", bw - 4), bw),
				(UiManager.Button("open", "mods", "Mod list", bw - 4), bw)
			};
			if (HomesManager.IsActive) {
				nav.Add((UiManager.Button("open", "homes", "My homes", bw - 4), bw));
			}
			if (BccPermissions.CanSeePermissionOverview(player)) {
				nav.Add((UiManager.Button("open", "perms", "Permissions", bw - 4), bw));
			}
			if (UiPlayerPages.CanOpen(player)) {
				nav.Add((UiManager.Button("open", "players", "Players", bw - 4), bw));
			}
			if (BccPermissions.CanEditConfig(player)) {
				nav.Add((UiManager.Button("open", "config", "Admin config", bw - 4), bw));
			}
			menu.Items.Add(new HorizontalRow(nav, 34));
			UiManager.FilterRow(menu, state.Filter, "Filter:");
			UiManager.Messages(menu, state);

			// one scrollable page; split into pages only beyond Ui.MaxLinesPerPage entries
			List<BccCommandInfo> visible = CommandRegistry.GetVisible(player, state.Filter);
			List<BccCommandInfo> page = TextHelper.Page(visible, state.Page, LinesPerPage, out int p, out int pages);
			state.Page = p;
			state.RowKeys = page.Select(c => c.Command).ToList();
			menu.Items.Add(UiManager.Text(Style.Muted($"{visible.Count} command(s) available to you"), 22, 14));

			string lastMod = null;
			for (int i = 0; i < page.Count; i++) {
				BccCommandInfo c = page[i];
				if (c.Mod != lastMod) {
					lastMod = c.Mod;
					UiManager.SectionHeader(menu, c.Mod);
				}
				string desc = string.IsNullOrEmpty(c.Description) ? Style.Muted("(no description)") : TextHelper.Truncate(c.Description, 80);
				menu.Items.Add(new BackgroundColor(UiManager.Row(34,
					(UiManager.Text(Style.UiCmd(c.Command), 30, 16), 150),
					(UiManager.Text(desc, 30, 14), W - 150 - 110 - 12),
					(UiManager.Button("row", i.ToString(), "Details", 100), 104)), W, 38, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			if (page.Count == 0) {
				menu.Items.Add(UiManager.Text(Style.Muted("No commands match.")));
			}
			UiManager.Pager(menu, p, pages);
		}

		// ------------------------------------------------------------------ command detail + execute

		public static void BuildCommand(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			if (!CommandRegistry.TryGet(state.Key, out BccCommandInfo c)) {
				UiManager.Header(player, menu, state.Key);
				menu.Items.Add(UiManager.Text(Style.Error("Unknown command.")));
				return;
			}
			bool allowed = CommandRegistry.CanUse(player, c);
			UiManager.Header(player, menu, c.Command);
			UiManager.Messages(menu, state);
			menu.Items.Add(UiManager.Text($"{Style.Muted("Mod:")} {Style.SubHeader(c.Mod)}{(string.IsNullOrEmpty(c.Category) ? "" : Style.Muted("  ·  " + c.Category))}", 24, 15));
			menu.Items.Add(UiManager.Text(string.IsNullOrEmpty(c.Description) ? Style.Muted("No description available.") : c.Description, -1, 16));
			menu.Items.Add(UiManager.Text($"{Style.Muted("Usage:")} {Style.CmdRef(TextHelper.EscapeTags(c.Usage))}", 24, 15));
			if (c.Aliases.Count > 0) {
				menu.Items.Add(UiManager.Text($"{Style.Muted("Aliases:")} {string.Join(", ", c.Aliases)}", 24, 15));
			}
			if (BccPermissions.CanSeePermissionOverview(player)) {
				string permText = c.Permission + (c.OpenByDefault ? (BccPermissions.IsAssignedExactly(c.Permission) ? "  (assigned - restricted)" : "  (open to everyone until assigned)") : "");
				menu.Items.Add(UiManager.Text($"{Style.Muted("Permission:")} {permText}", 24, 14));
			}
			menu.Items.Add(new Line());

			if (!allowed) {
				// no input fields and no execute button at all without permission
				menu.Items.Add(UiManager.Text(Style.Error("You don't have permission to use this command."), 28, 16));
				return;
			}
			if (!c.UiExecutable) {
				menu.Items.Add(UiManager.Text(Style.Muted("This command can only be used in chat."), 28, 15));
				return;
			}
			List<BccCommandParam> parameters = c.Parameters.Count > 0 ? c.Parameters : new List<BccCommandParam> { new BccCommandParam("arguments", "Everything after the command (optional)", true) };
			for (int i = 0; i < parameters.Count; i++) {
				BccCommandParam prm = parameters[i];
				string key = "p" + i;
				if (!menu.LocalStorage.ContainsKey(key)) {
					menu.LocalStorage.SetAs(key, "");
				}
				string label = $"{prm.Name}{(prm.Optional ? Style.Muted(" (optional)") : "")}";
				menu.Items.Add(UiManager.Row(32,
					(UiManager.Text(label, 30, 15), 200),
					(new InputField(key, W - 200 - 8, 30), W - 200)));
				if (!string.IsNullOrEmpty(prm.Description)) {
					menu.Items.Add(UiManager.Text(Style.Muted(prm.Description), 20, 13));
				}
			}
			menu.Items.Add(new EmptySpace(6));
			menu.Items.Add(UiManager.Row(36,
				(new EmptySpace(30), W - 200),
				(UiManager.Button("exec", null, "Execute", 190, true, 34), 196)));
		}

		public static void ExecuteFromUi(Players.Player player, UiManager.PageState state, JObject storage)
		{
			if (!CommandRegistry.TryGet(state.Key, out BccCommandInfo c)) {
				return;
			}
			// server side permission re-check - the button is never shown without permission,
			// but a modified client could still send the click
			if (!CommandRegistry.CanUse(player, c) || !c.UiExecutable) {
				state.Messages.Add(Style.Error("You don't have permission to use this command."));
				UiManager.Render(player);
				return;
			}
			int count = System.Math.Max(1, c.Parameters.Count);
			List<string> args = new List<string>();
			for (int i = 0; i < count; i++) {
				string v = (storage.Value<string>("p" + i) ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
				if (v.Length > 0) {
					// keep multi-word values together for commands that parse quoted names
					if (v.Contains(" ") && c.Parameters.Count > 1 && i < c.Parameters.Count - 1 && !v.StartsWith("'") && !v.StartsWith("\"")) {
						v = "'" + v + "'";
					}
					args.Add(v);
				} else if (i < c.Parameters.Count && !c.Parameters[i].Optional) {
					state.Messages.Add(Style.Error($"Please fill in '{c.Parameters[i].Name}'."));
					UiManager.Render(player);
					return;
				}
			}
			string line = c.Command + (args.Count > 0 ? " " + string.Join(" ", args) : "");
			if (line.Length > 500) {
				line = line.Substring(0, 500);
			}
			// commands that have their own window (/help, /bcsettings, /modlist, /permissions,
			// /bcconfig, /homes) open it - on top of the current page, so Back still works
			// every command started from a window is logged like a typed one, with its result
			Log.Write($"[BCC UI] {player.Name} ({player.ID.ToStringReadable()}) executed: {line}");
			// /announcements with arguments is a chat command (add/remove/...), only without arguments a window
			bool native = UiPreferences.NativeUiCommands.Contains(c.Command) && !(c.Command == "/announcements" && args.Count > 0)
				&& !(c.Command == "/trust" && args.Count > 0 && TrustChatCommand.ChatSubcommands.Contains(args[0]));
			if (native && UiPreferences.ServerAllowsWindows) {
				UiManager.PushMode = true;
				try {
					bool ok = CommandManager.TryDoCommand(player, line);
					Log.Write($"[BCC UI] result for {player.Name}: {(ok ? "window opened" : "not handled")}");
				} finally {
					UiManager.PushMode = false;
				}
				return;
			}
			List<string> output = RunCaptured(player, line, out bool handled);
			LogResult(player, handled, output);
			if (!handled) {
				output.Add(Style.Error($"{line} could not be executed - the command failed (details in the server log) or does not exist."));
			} else if (output.Count == 0) {
				output.Add(Style.Muted(c.Mod == CommandRegistry.BCC_MOD
					? "The command was executed (no output)."
					: "The command was executed. The game and other mods answer in the chat - their output can't be shown here."));
			}
			UiManager.ShowOutput(player, line, output, false);
		}

		static void LogResult(Players.Player player, bool handled, List<string> output)
		{
			if (!handled) {
				Log.Write($"[BCC UI] result for {player.Name}: NOT HANDLED (command failed or unknown)");
				return;
			}
			if (output.Count == 0) {
				Log.Write($"[BCC UI] result for {player.Name}: handled (answer went to the chat - game/other mod command)");
				return;
			}
			foreach (string l in output) {
				Log.Write($"[BCC UI] result for {player.Name}: {TextHelper.StripRichText(l)}");
			}
		}

		public static List<string> RunCaptured(Players.Player player, string line, out bool handled)
		{
			BccChat.BeginCapture(player);
			handled = false;
			try {
				handled = CommandManager.TryDoCommand(player, line);
			} finally {
				List<string> captured = BccChat.EndCapture(player);
				if (captured != null) {
					lastCaptured = captured;
				}
			}
			List<string> result = lastCaptured ?? new List<string>();
			lastCaptured = null;
			return result;
		}

		static List<string> lastCaptured;

		// ------------------------------------------------------------------ output window

		public static void BuildOutput(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, TextHelper.Truncate(TextHelper.EscapeTags(state.Title), 60));
			int perPage = LinesPerPage;
			List<string> lines = TextHelper.Page(state.Lines, state.Page, perPage, out int p, out int pages);
			state.Page = p;
			foreach (string l in lines) {
				menu.Items.Add(UiManager.Text(TextHelper.SafeName(l), -1, 16));
			}
			UiManager.Pager(menu, p, pages);
		}

		// ------------------------------------------------------------------ mod list

		public static void BuildModList(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "Installed mods");
			if (!BccSettingsManager.Settings.ModList.Enabled) {
				menu.Items.Add(UiManager.Text(Style.Error("The mod list is disabled on this server.")));
				return;
			}
			UiManager.FilterRow(menu, state.Filter, "Filter:");
			bool az = state.Sort == "az";
			menu.Items.Add(UiManager.Row(32,
				(UiManager.Text(Style.Muted("Sort:"), 30, 14), 60),
				(UiManager.Button("sort", "new", (az ? "" : "> ") + "Recently updated", 196, az), 200),
				(UiManager.Button("sort", "az", (az ? "> " : "") + "A - Z", 116, !az), 120)));
			// one scrollable page; split into pages only beyond Ui.MaxLinesPerPage entries
			List<ModInfoCollector.ModEntry> mods = ModInfoCollector.GetVisible(state.Filter, az);
			List<ModInfoCollector.ModEntry> page = TextHelper.Page(mods, state.Page, LinesPerPage, out int p, out int pages);
			state.Page = p;
			state.RowKeys = page.Select(m => m.Name).ToList();
			menu.Items.Add(UiManager.Text(Style.Muted($"{mods.Count} mod(s)"), 22, 14));
			bool images = BccSettingsManager.Settings.ModList.ShowImages;
			for (int i = 0; i < page.Count; i++) {
				ModInfoCollector.ModEntry m = page[i];
				List<(IItem, int)> row = new List<(IItem, int)>();
				if (images) {
					if (m.ImageFile.IsValid) {
						row.Add((new FileIcon(m.ImageFile, 56), 62));
					} else {
						row.Add((UiManager.Text(Style.Muted("no image"), 56, 11, ELabelAlignment.MiddleCenter), 62));
					}
				}
				int textWidth = W - (images ? 62 : 0) - 110 - 12;
				string title = $"<b>{TextHelper.SafeName(m.Name)}</b> {Style.Muted("v" + m.Version + (m.LastUpdated > DateTime.MinValue ? "  ·  updated " + m.LastUpdated.ToString("yyyy-MM-dd") : ""))}";
				string desc = string.IsNullOrEmpty(m.Description) ? Style.Muted("(no description)") : TextHelper.Truncate(TextHelper.SafeName(m.Description), 90);
				row.Add((UiManager.Text(title + "\n" + desc, 56, 15), textWidth));
				row.Add((UiManager.Button("row", i.ToString(), "Details", 100), 104));
				menu.Items.Add(new BackgroundColor(new HorizontalRow(row, 58), W, 62, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			UiManager.Pager(menu, p, pages);
		}

		public static void BuildMod(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			ModInfoCollector.ModEntry m = ModInfoCollector.Find(state.Key);
			UiManager.Header(player, menu, m != null ? TextHelper.SafeName(m.Name) : state.Key);
			if (m == null) {
				menu.Items.Add(UiManager.Text(Style.Error("Mod not found.")));
				return;
			}
			List<string> facts = new List<string> {
				$"{Style.Muted("Version:")} {m.Version}"
			};
			if (!string.IsNullOrEmpty(m.Author)) facts.Add($"{Style.Muted("Author:")} {TextHelper.SafeName(m.Author)}");
			if (m.Compatible.Count > 0) facts.Add($"{Style.Muted("Game versions:")} {string.Join(", ", m.Compatible)}");
			if (!string.IsNullOrEmpty(m.Homepage)) facts.Add($"{Style.Muted("Website:")} {TextHelper.EscapeTags(m.Homepage)}");
			if (!string.IsNullOrEmpty(m.WorkshopId)) facts.Add($"{Style.Muted("Steam Workshop:")} {m.WorkshopId}");
			if (m.LastUpdated > DateTime.MinValue) facts.Add($"{Style.Muted("Updated on this server:")} {m.LastUpdated:yyyy-MM-dd HH:mm}");
			int commands = CommandRegistry.All().Count(c => string.Equals(c.Mod, m.Name, StringComparison.OrdinalIgnoreCase));
			if (commands > 0) facts.Add($"{Style.Muted("Commands:")} {commands} (see /help)");

			if (BccSettingsManager.Settings.ModList.ShowImages && m.ImageFile.IsValid) {
				menu.Items.Add(UiManager.Row(150,
					(new FileIcon(m.ImageFile, 144), 152),
					(UiManager.Text(string.Join("\n", facts), 144, 15), W - 152)));
			} else {
				foreach (string f in facts) {
					menu.Items.Add(UiManager.Text(f, 22, 15));
				}
			}
			menu.Items.Add(new Line());
			menu.Items.Add(UiManager.Text(string.IsNullOrEmpty(m.Description) ? Style.Muted("No description.") : TextHelper.SafeName(m.Description), -1, 16));
			foreach (string l in m.LongDescription) {
				menu.Items.Add(UiManager.Text(TextHelper.SafeName(l), -1, 15));
			}
			if (m.Changelog.Count > 0) {
				menu.Items.Add(new Line());
				menu.Items.Add(UiManager.Text(Style.Header("Version history"), 28, 16));
				// newest version first
				foreach (ModInfoCollector.ChangelogEntry entry in m.Changelog) {
					menu.Items.Add(UiManager.Text(Style.SubHeader("v" + entry.Version) + (string.IsNullOrEmpty(entry.Date) ? "" : Style.Muted("  " + entry.Date)), 24, 15));
					foreach (string n in entry.Notes) {
						menu.Items.Add(UiManager.Text("• " + TextHelper.SafeName(n), -1, 13));
					}
				}
			}
		}

		// ------------------------------------------------------------------ permission overview

		public static void BuildPermissions(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "Permissions");
			if (!BccPermissions.CanSeePermissionOverview(player)) {
				menu.Items.Add(UiManager.Text(Style.Error("You don't have permission to see this.")));
				return;
			}
			BccPermissions.Refresh(true);
			UiManager.PermissionNotice(menu, player);
			UiManager.FilterRow(menu, state.Filter, "Filter:");
			List<string> allGroups = BccPermissions.GetAllPermissionGroups();
			if (allGroups.Count > 0) {
				menu.Items.Add(UiManager.Text(Style.Muted("Groups with all permissions: ") + string.Join(", ", allGroups.Select(ChatColors.FormatRole)), 22, 14));
			}
			List<string> lines = PermissionsChatCommand.BuildLines(state.Filter, out int unused, out int total);
			menu.Items.Add(UiManager.Text(Style.Muted($"{total} permission(s), {unused} unused"), 22, 14));
			int size = System.Math.Max(10, LinesPerPage / 2); // two lines per permission
			List<string> page = TextHelper.Page(lines, state.Page, size, out int p, out int pages);
			state.Page = p;
			for (int i = 0; i < page.Count; i++) {
				menu.Items.Add(new BackgroundColor(UiManager.Text(page[i], -1, 14), W, 48, 0f, 0f, 4f, 2f, UiManager.RowColor(i)));
			}
			UiManager.Pager(menu, p, pages);
		}

		// ------------------------------------------------------------------ homes

		public static void BuildHomes(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "My homes");
			if (!HomesManager.IsActive) {
				menu.Items.Add(UiManager.Text(Style.Error("Home points are disabled on this server.")));
				return;
			}
			UiManager.Messages(menu, state);
			List<HomePoint> homes = HomesManager.GetHomes(player);
			int limit = HomesManager.GetLimit(player);
			menu.Items.Add(UiManager.Text($"{homes.Count} of {(limit < 0 ? "unlimited" : limit.ToString())} home points used.", 24, 15));
			state.Lists["homes"] = homes.Select(h => h.Name).ToList();
			for (int i = 0; i < homes.Count; i++) {
				HomePoint h = homes[i];
				menu.Items.Add(new BackgroundColor(UiManager.Row(34,
					(UiManager.Text($"<b>{TextHelper.EscapeTags(h.Name)}</b> {Style.Muted($"{(int)h.X} {(int)h.Y} {(int)h.Z}")}", 30, 15), W - 240),
					(UiManager.Button("act", "tp:" + i, "Teleport", 112), 116),
					(UiManager.Button("act", "del:" + i, "Delete", 112), 116)), W, 38, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			if (homes.Count == 0) {
				menu.Items.Add(UiManager.Text(Style.Muted("No homes yet."), 24, 14));
			}
			menu.Items.Add(new Line());
			menu.LocalStorage.SetAs("homename", "home");
			bool canAdd = limit < 0 || homes.Count < limit;
			menu.Items.Add(UiManager.Row(34,
				(UiManager.Text("Set a home here:", 30, 15), 150),
				(new InputField("homename", W - 150 - 200, 30), W - 150 - 196),
				(UiManager.Button("act", "set", "Set home", 186, true), 196)));
			if (!canAdd) {
				menu.Items.Add(UiManager.Text(Style.Muted("Limit reached - only existing names can be moved."), 20, 13));
			}
		}

		public static void HomesAction(Players.Player player, UiManager.PageState state, string action, string item, JObject storage)
		{
			if (!HomesManager.IsActive) {
				return;
			}
			switch (action) {
				case "tp": {
					HomePoint h = HomesManager.Find(player, item);
					if (h != null) {
						UiManager.Close(player);
						HomesManager.TeleportHome(player, h);
						BccChat.Send(player, Style.Success($"Teleported to home '{h.Name}'."));
						return;
					}
					break;
				}
				case "del":
					state.Messages.Add(HomesManager.DeleteHome(player, item) ? Style.Success($"Home '{item}' removed.") : Style.Error("Home not found."));
					break;
				case "set": {
					string name = (storage.Value<string>("homename") ?? "home").Trim();
					string error = HomesManager.SetHome(player, name);
					state.Messages.Add(error == null ? Style.Success($"Home '{(name.Length == 0 ? "home" : name)}' set to your position.") : Style.Error(error));
					break;
				}
			}
			UiManager.Render(player);
		}

		// ------------------------------------------------------------------ player settings

		public static void BuildSettings(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "My settings");
			UiManager.Messages(menu, state);
			UiPreferences.PlayerUiSettings mine = UiPreferences.Get(player);
			menu.Items.Add(UiManager.Text("Choose which commands show their output in a window. Unticked commands answer in the chat.", -1, 15));
			menu.Items.Add(UiManager.Text(Style.Muted("Same as '/bcui exclude <command>' / '/bcui include <command>'. Server settings always win."), -1, 13));
			menu.LocalStorage.SetAs("s_all", !mine.WindowsDisabled);
			menu.Items.Add(new Toggle(new LabelData("<b>Use windows at all</b>", ELabelAlignment.MiddleLeft, 16, LabelData.ELocalizationType.None), "s_all"));
			menu.Items.Add(new Line());
			List<string> cmds = UiPreferences.WindowCapableCommands();
			List<string> page = TextHelper.Page(cmds, state.Page, LinesPerPage, out int p, out int pages);
			state.Page = p;
			state.RowKeys = page;
			for (int i = 0; i < page.Count; i++) {
				string c = page[i];
				if (UiPreferences.IsServerExcluded(c)) {
					menu.Items.Add(UiManager.Text($"{Style.Cmd(c)} {Style.Muted("- disabled for windows by the server")}", 26, 15));
					continue;
				}
				string key = "s_" + i;
				menu.LocalStorage.SetAs(key, !UiPreferences.IsPlayerExcluded(player, c));
				menu.Items.Add(new Toggle(new LabelData(Style.Cmd(c), ELabelAlignment.MiddleLeft, 16, LabelData.ELocalizationType.None), key));
			}
			UiManager.Pager(menu, p, pages);
			menu.Items.Add(new EmptySpace(6));
			menu.Items.Add(UiManager.Row(36,
				(new EmptySpace(30), W - 200),
				(UiManager.Button("setsave", null, "Save", 190, true, 34), 196)));
		}

		public static void SaveSettings(Players.Player player, UiManager.PageState state, JObject storage)
		{
			if (storage.TryGetValue("s_all", out JToken all) && all.Type == JTokenType.Boolean) {
				UiPreferences.SetWindowsDisabled(player, !all.Value<bool>());
			}
			for (int i = 0; i < state.RowKeys.Count; i++) {
				string c = state.RowKeys[i];
				if (UiPreferences.IsServerExcluded(c)) {
					continue;
				}
				if (storage.TryGetValue("s_" + i, out JToken t) && t.Type == JTokenType.Boolean) {
					UiPreferences.SetExcluded(player, c, !t.Value<bool>());
				}
			}
			state.Messages.Add(Style.Success("Settings saved."));
			UiManager.Render(player);
		}
	}
}
