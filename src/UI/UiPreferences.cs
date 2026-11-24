using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Pipliz;

namespace ColonyCommands
{
	// Which commands show their output in a window. Order of precedence:
	//  1. server: Ui.Enabled = false or command in Ui.ExcludedCommands -> never a window (always wins)
	//  2. player: windows switched off, or command excluded via /bcui or the settings window -> chat
	//  3. otherwise: window for the commands in Ui.WindowCommands (and the native UI commands)
	// Stored in gamedata/savegames/<world>/bcc-playersettings.json.
	public static class UiPreferences
	{
		public const string FILE_NAME = "bcc-playersettings.json";

		// commands that build their own window instead of having their chat output captured
		public static readonly HashSet<string> NativeUiCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"/help", "/modlist", "/permissions", "/bcconfig", "/bcsettings", "/homes", "/players", "/announcements", "/trust"
		};

		public class PlayerUiSettings
		{
			public bool WindowsDisabled;
			public List<string> ExcludedCommands = new List<string>();
		}

		static Dictionary<string, PlayerUiSettings> players = new Dictionary<string, PlayerUiSettings>();
		static string FilePath => Path.Combine("gamedata", "savegames", ServerManager.WorldName, FILE_NAME);

		public static void Load()
		{
			players = new Dictionary<string, PlayerUiSettings>();
			if (!File.Exists(FilePath)) {
				return;
			}
			try {
				players = JsonConvert.DeserializeObject<Dictionary<string, PlayerUiSettings>>(File.ReadAllText(FilePath)) ?? new Dictionary<string, PlayerUiSettings>();
				PlayerKeyMigration.MigrateLegacyKeys(players, FILE_NAME);
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: could not read {FILE_NAME}: {e.Message}");
			}
		}

		public static void Save()
		{
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				File.WriteAllText(FilePath, JsonConvert.SerializeObject(players, Formatting.Indented));
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: could not save {FILE_NAME}: {e.Message}");
			}
		}

		public static PlayerUiSettings Get(Players.Player player)
		{
			string key = player.ID.ToStringReadable();
			if (!players.TryGetValue(key, out PlayerUiSettings s)) {
				s = new PlayerUiSettings();
				players[key] = s;
			}
			if (s.ExcludedCommands == null) {
				s.ExcludedCommands = new List<string>();
			}
			return s;
		}

		static BccSettings.UiSettings Server => BccSettingsManager.Settings.Ui;

		public static bool ServerAllowsWindows => Server.Enabled;

		public static bool IsServerExcluded(string command)
		{
			string c = CommandRegistry.NormalizeCommand(command);
			return Server.ExcludedCommands.Any(e => CommandRegistry.NormalizeCommand(e) == c);
		}

		public static bool IsPlayerExcluded(Players.Player player, string command)
		{
			string c = CommandRegistry.NormalizeCommand(command);
			return Get(player).ExcludedCommands.Any(e => CommandRegistry.NormalizeCommand(e) == c);
		}

		// commands whose output may be shown in a window at all
		public static List<string> WindowCapableCommands()
		{
			HashSet<string> set = new HashSet<string>(NativeUiCommands, StringComparer.OrdinalIgnoreCase);
			foreach (string c in Server.WindowCommands) {
				set.Add(CommandRegistry.NormalizeCommand(c));
			}
			return set.Select(CommandRegistry.NormalizeCommand).Distinct().OrderBy(c => c).ToList();
		}

		public static bool IsWindowCommand(string command)
		{
			string c = CommandRegistry.NormalizeCommand(command);
			return NativeUiCommands.Contains(c) || Server.WindowCommands.Any(w => CommandRegistry.NormalizeCommand(w) == c);
		}

		// may this player get a window for this command?
		public static bool UsesWindow(Players.Player player, string command)
		{
			if (player == null || !Server.Enabled || IsServerExcluded(command)) {
				return false;
			}
			PlayerUiSettings s = Get(player);
			if (s.WindowsDisabled) {
				return false;
			}
			return !IsPlayerExcluded(player, command);
		}

		// the UI itself (help window etc.) - only the server switch and the player's global switch matter
		public static bool UiAvailable(Players.Player player)
		{
			return player != null && Server.Enabled && !Get(player).WindowsDisabled;
		}

		public static void SetExcluded(Players.Player player, string command, bool excluded)
		{
			string c = CommandRegistry.NormalizeCommand(command);
			PlayerUiSettings s = Get(player);
			s.ExcludedCommands.RemoveAll(e => CommandRegistry.NormalizeCommand(e) == c);
			if (excluded) {
				s.ExcludedCommands.Add(c);
			}
			Save();
		}

		public static void SetWindowsDisabled(Players.Player player, bool disabled)
		{
			Get(player).WindowsDisabled = disabled;
			Save();
		}

		public static void Reset(Players.Player player)
		{
			players.Remove(player.ID.ToStringReadable());
			Save();
		}
	}
}
