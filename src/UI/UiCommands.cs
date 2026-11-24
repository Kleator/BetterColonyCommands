using System;
using System.Collections.Generic;
using System.Linq;
using Chatting;

namespace ColonyCommands
{
	// /bcui {exclude|include|list|reset|on|off} [command]
	public class BcUiChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/bcui", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			string action = splits.Count > 1 ? splits[1].ToLowerInvariant() : "list";
			string command = splits.Count > 2 ? CommandRegistry.NormalizeCommand(splits[2]) : "";
			switch (action) {
				case "exclude":
				case "include":
					if (command.Length < 2) {
						BccChat.Send(causedBy, Style.Error($"Syntax: {Style.CmdRef("/bcui " + action + " <command>")}, e.g. /bcui {action} /top"));
						return true;
					}
					if (!UiPreferences.IsWindowCommand(command)) {
						BccChat.Send(causedBy, Style.Error($"{command} never uses a window. Window commands: {string.Join(", ", UiPreferences.WindowCapableCommands())}"));
						return true;
					}
					UiPreferences.SetExcluded(causedBy, command, action == "exclude");
					string effect = action == "exclude" ? "answers in the chat from now on" : "uses a window again";
					BccChat.Send(causedBy, Style.Success($"{command} {effect}."));
					if (action == "include" && UiPreferences.IsServerExcluded(command)) {
						BccChat.Send(causedBy, Style.Muted("Note: the server disabled windows for this command, so it stays in the chat."));
					}
					return true;
				case "off":
					UiPreferences.SetWindowsDisabled(causedBy, true);
					BccChat.Send(causedBy, Style.Success("Windows switched off - all commands answer in the chat. '/bcui on' switches them back on."));
					return true;
				case "on":
					UiPreferences.SetWindowsDisabled(causedBy, false);
					BccChat.Send(causedBy, Style.Success("Windows switched on."));
					return true;
				case "reset":
					UiPreferences.Reset(causedBy);
					BccChat.Send(causedBy, Style.Success("Your window settings were reset to the server defaults."));
					return true;
				default:
					UiPreferences.PlayerUiSettings s = UiPreferences.Get(causedBy);
					BccChat.Send(causedBy, Style.Header("Window settings"));
					if (!UiPreferences.ServerAllowsWindows) {
						BccChat.Send(causedBy, Style.Muted("Windows are disabled on this server."));
						return true;
					}
					BccChat.Send(causedBy, $"Windows: {(s.WindowsDisabled ? Style.Error("off") : Style.Success("on"))}");
					foreach (string c in UiPreferences.WindowCapableCommands()) {
						string state = UiPreferences.IsServerExcluded(c) ? Style.Muted("chat (server)")
							: UiPreferences.IsPlayerExcluded(causedBy, c) ? Style.Muted("chat") : Style.Success("window");
						BccChat.Send(causedBy, $"{Style.Cmd(c)} - {state}");
					}
					BccChat.Send(causedBy, Style.Muted("/bcui exclude|include <command>, /bcui on|off, /bcui reset, or /bcsettings"));
					return true;
			}
		}
	}

	// /bcsettings - personal settings window
	public class BcSettingsChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/bcsettings", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			if (!UiPreferences.ServerAllowsWindows || UiPreferences.IsServerExcluded("/bcsettings") || BccChat.IsCapturing(causedBy)) {
				return new BcUiChatCommand().TryDoCommand(causedBy, "/bcui list", new List<string> { "/bcui", "list" });
			}
			UiManager.OpenNamed(causedBy, "settings", true);
			return true;
		}
	}

	// /players [search] - player moderation window
	public class PlayersChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/players", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, UiPlayerPages.PERMISSION)) {
				return true;
			}
			if (!UiPreferences.ServerAllowsWindows || BccChat.IsCapturing(causedBy)) {
				BccChat.Send(causedBy, Style.Muted("Player moderation needs windows to be enabled (bcc-settings.json -> Ui.Enabled)."));
				return true;
			}
			string filter = splits.Count > 1 ? chattext.Substring(chattext.IndexOf(' ') + 1).Trim() : "";
			UiManager.Open(causedBy, new UiManager.PageState { Kind = "players", Filter = filter }, !UiManager.PushMode);
			return true;
		}
	}

	// /bcconfig - admin configuration window
	public class BcConfigChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/bcconfig", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, BccPermissions.PERMISSION_CONFIG)) {
				return true;
			}
			if (!UiPreferences.ServerAllowsWindows || BccChat.IsCapturing(causedBy)) {
				BccChat.Send(causedBy, Style.Muted("The configuration window needs windows to be enabled (bcc-settings.json -> Ui.Enabled). Edit the json files and use /bcreload instead."));
				return true;
			}
			UiManager.OpenNamed(causedBy, "config", true);
			return true;
		}
	}

	// Registered first in the game's command list. For every typed command it
	//  1. enforces auto-created "open by default" permissions once an admin assigned them, and
	//  2. for Better Colony Commands' info commands, captures the chat output and shows it in a
	//     window (if the server and the player allow windows for that command).
	public class CommandInterceptor : IChatCommand
	{
		static readonly HashSet<Players.Player> running = new HashSet<Players.Player>();
		static readonly HomesChatCommand homes = new HomesChatCommand();

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (causedBy == null || splits.Count == 0 || running.Contains(causedBy) || !splits[0].StartsWith("/")) {
				return false;
			}
			// sub commands first ("/debug clearstockpile"), then the command itself
			BccCommandInfo info = null;
			if (splits.Count > 1 && CommandRegistry.TryGetExact(splits[0] + " " + splits[1], out BccCommandInfo sub)) {
				info = sub;
			} else if (!CommandRegistry.TryGet(splits[0], out info)) {
				return false;
			}
			if (info.OpenByDefault && !CommandRegistry.CanUse(causedBy, info)) {
				BccChat.Send(causedBy, Style.Error($"You don't have permission to use {info.Command} ({info.Permission})."));
				return true;
			}
			// home points belong to Better Colony Commands: answer them here, before any other mod
			if (HomesChatCommand.Commands.Contains(info.Command) && HomesManager.IsActive) {
				return homes.TryDoCommand(causedBy, chattext, splits);
			}
			if (info.Mod != CommandRegistry.BCC_MOD || UiPreferences.NativeUiCommands.Contains(info.Command) || BccChat.IsCapturing(causedBy)
				|| !UiPreferences.IsWindowCommand(info.Command) || !UiPreferences.UsesWindow(causedBy, info.Command)) {
				return false;
			}
			running.Add(causedBy);
			bool handled = false;
			BccChat.BeginCapture(causedBy);
			List<string> lines = null;
			try {
				List<IChatCommand> all = CommandManager.RegisteredCommands;
				for (int i = 0; i < all.Count && !handled; i++) {
					if (all[i] == this) {
						continue;
					}
					try {
						handled = all[i].TryDoCommand(causedBy, chattext, splits);
					} catch (Exception e) {
						Pipliz.Log.WriteException($"Exception in trydocommand type: {all[i].GetType()}", e);
					}
				}
			} finally {
				lines = BccChat.EndCapture(causedBy) ?? new List<string>();
				running.Remove(causedBy);
			}
			if (!handled) {
				foreach (string l in lines) {
					BccChat.Send(causedBy, l);
				}
				return false;
			}
			if (lines.Count > 0) {
				UiManager.ShowOutput(causedBy, chattext, lines, true);
			}
			return true;
		}
	}
}
