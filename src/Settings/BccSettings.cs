using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Newtonsoft.Json;
using Pipliz;

namespace ColonyCommands
{
	// gamedata/savegames/<world>/bcc-settings.json - every setting introduced with 1.1.0.
	// [Description] texts are shown in the admin configuration window.
	public class BccSettings
	{
		public UiSettings Ui = new UiSettings();
		public RtpSettings Rtp = new RtpSettings();
		public EconomySettings Economy = new EconomySettings();
		public ModListSettings ModList = new ModListSettings();
		public HomesSettings Homes = new HomesSettings();
		public PermissionBackupSettings Permissions = new PermissionBackupSettings();

		public class PermissionBackupSettings
		{
			[Description("Keeps a backup of the permission groups (roles). If a game update resets permissiongroups.json, the last state is put back automatically as the world's own permissiongroups.json and the permissions are reloaded.")]
			public bool RestoreGroupsAfterGameUpdate = true;

			[Description("How many earlier versions of permissiongroups.json are kept in the world folder (bcc-permission-backups).")]
			public int KeepBackups = 10;
		}

		public class UiSettings
		{
			[Description("Master switch for all Better Colony Commands windows. Off = everything is chat only.")]
			public bool Enabled = true;

			[Description("Commands that never use a window for anyone. Overrides every player setting.")]
			public List<string> ExcludedCommands = new List<string>();

			[Description("Typing these commands in chat shows their output in a window instead of the chat (unless excluded).")]
			public List<string> WindowCommands = new List<string>() {
				"/help", "/modlist", "/roles", "/roletime", "/permissions", "/top", "/mtop", "/groups",
				"/banlog", "/jailrec", "/inactive", "/list", "/baltop", "/bcsettings", "/bcconfig"
			};

			[Description("Windows scroll; only beyond this many lines/entries a list is split into pages (< Prev / Next >).")]
			public int MaxLinesPerPage = 500;

			[Description("Window width in pixels.")]
			public int WindowWidth = 1000;

			[Description("Window height in pixels.")]
			public int WindowHeight = 620;
		}

		public class RtpSettings
		{
			[Description("Enables /rtp (random teleport around your position).")]
			public bool Enabled = true;

			[Description("Minimum distance in blocks from your current position.")]
			public int MinDistance = 100;

			[Description("Maximum distance in blocks from your current position.")]
			public int MaxDistance = 1000;

			[Description("Cooldown between two /rtp uses in seconds (permission mods.scarabol.commands.rtp.nocooldown bypasses it).")]
			public int CooldownSeconds = 300;

			[Description("How many random spots are tried before giving up.")]
			public int MaxAttempts = 25;

			[Description("Never land inside the banner protection range of a colony that is not yours.")]
			public bool AvoidForeignColonies = true;

			[Description("Never land inside the spawn protection area or custom protection areas.")]
			public bool AvoidProtectedAreas = true;
		}

		public class EconomySettings
		{
			[Description("Enables the Better Colony Commands economy module (balances, /money, /pay, API for other mods).")]
			public bool Enabled = true;

			[Description("Currency name shown after every amount.")]
			public string CurrencyName = "Coins";

			[Description("Money granted once to every new player. 0 = off.")]
			public long StartingBalance = 1000;

			[Description("Every N minutes every online player gets the passive income. 0 = off.")]
			public int PassiveIncomeIntervalMinutes = 10;

			[Description("Passive income amount per interval.")]
			public long PassiveIncomeAmount = 50;

			[Description("Show the balance as overlay counter (only while BCC is the active economy).")]
			public bool ShowHud = true;

			[Description("Allow players to send money to each other with /pay.")]
			public bool AllowPay = true;

			[Description("Keep balances in sync with Pytonia Automation (if installed) and import its data if it gets uninstalled.")]
			public bool PytoniaSync = true;

			[Description("Track 'THE TRADER' Trader Coins so they are converted into money if THE TRADER gets uninstalled.")]
			public bool TraderIntegration = true;

			[Description("Allow /exchange between Trader Coins and money while THE TRADER is installed.")]
			public bool TraderExchangeEnabled = true;

			[Description("Money value of one Trader Coin (used by /exchange and for the conversion after uninstalling THE TRADER).")]
			public long TraderCoinValue = 1;

			[Description("How often (seconds) Trader Coin stocks are snapshotted.")]
			public int TraderSnapshotIntervalSeconds = 120;
		}

		public class ModListSettings
		{
			[Description("Enables /modlist.")]
			public bool Enabled = true;

			[Description("Show mod preview images in the mod list window (images are sent to clients once).")]
			public bool ShowImages = true;

			[Description("Preview images larger than this (KB) are skipped to keep joining fast.")]
			public int MaxImageKilobytes = 1024;

			[Description("Also list the base game parts the mod loader reports as mods (Colony Survival, pipliz.game, ...).")]
			public bool ShowBaseGame = false;

			[Description("Mod names hidden from /modlist.")]
			public List<string> HiddenMods = new List<string>();
		}

		public class HomesSettings
		{
			[Description("Enables home points (/sethome, /home, /homes, /delhome). They are part of Better Colony Commands; homes of an older Pytonia Automation are imported once.")]
			public bool Enabled = true;

			[Description("Homes a player may set if none of their roles has a limit below. -1 = unlimited, 0 = none.")]
			public int DefaultLimit = 1;

			[Description("Home limit per role (role=limit, -1 = unlimited). The highest limit of a player's roles counts. Also editable on each role's page.")]
			public Dictionary<string, int> RoleLimits = new Dictionary<string, int>();
		}
	}

	public static class BccSettingsManager
	{
		public const string FILE_NAME = "bcc-settings.json";
		public static BccSettings Settings = new BccSettings();

		static string FilePath => Path.Combine("gamedata", "savegames", ServerManager.WorldName, FILE_NAME);

		public static void Load()
		{
			string path = FilePath;
			if (!File.Exists(path)) {
				Settings = new BccSettings();
				Save();
				Log.Write($"Better Colony Commands: created {FILE_NAME} with defaults");
				return;
			}
			try {
				BccSettings loaded = JsonConvert.DeserializeObject<BccSettings>(File.ReadAllText(path), new JsonSerializerSettings {
					ObjectCreationHandling = ObjectCreationHandling.Replace
				});
				if (loaded == null) {
					throw new InvalidDataException("empty file");
				}
				Settings = loaded;
				Normalize();
				Save(); // writes newly added keys back with their defaults
			} catch (Exception e) {
				// never overwrite a file we could not read - keep defaults in memory only
				Log.WriteError($"Better Colony Commands: could not read {FILE_NAME}, file left untouched, using defaults: {e.Message}");
				Settings = new BccSettings();
			}
		}

		static void Normalize()
		{
			BccSettings s = Settings;
			if (s.Ui == null) s.Ui = new BccSettings.UiSettings();
			if (s.Rtp == null) s.Rtp = new BccSettings.RtpSettings();
			if (s.Economy == null) s.Economy = new BccSettings.EconomySettings();
			if (s.ModList == null) s.ModList = new BccSettings.ModListSettings();
			if (s.Homes == null) s.Homes = new BccSettings.HomesSettings();
			if (s.Ui.ExcludedCommands == null) s.Ui.ExcludedCommands = new List<string>();
			if (s.Ui.WindowCommands == null) s.Ui.WindowCommands = new List<string>();
			if (s.ModList.HiddenMods == null) s.ModList.HiddenMods = new List<string>();
			if (s.Homes.RoleLimits == null) s.Homes.RoleLimits = new Dictionary<string, int>();
			s.Ui.MaxLinesPerPage = System.Math.Max(50, System.Math.Min(2000, s.Ui.MaxLinesPerPage));
			s.Ui.WindowWidth = System.Math.Max(400, System.Math.Min(1600, s.Ui.WindowWidth));
			s.Ui.WindowHeight = System.Math.Max(300, System.Math.Min(1000, s.Ui.WindowHeight));
			if (s.Rtp.MinDistance < 0) s.Rtp.MinDistance = 0;
			if (s.Rtp.MaxDistance < s.Rtp.MinDistance + 1) s.Rtp.MaxDistance = s.Rtp.MinDistance + 1;
			if (s.Rtp.MaxAttempts < 1) s.Rtp.MaxAttempts = 1;
			if (s.Rtp.CooldownSeconds < 0) s.Rtp.CooldownSeconds = 0;
			if (string.IsNullOrEmpty(s.Economy.CurrencyName)) s.Economy.CurrencyName = "Coins";
			if (s.Economy.TraderCoinValue < 0) s.Economy.TraderCoinValue = 0;
			if (s.Economy.TraderSnapshotIntervalSeconds < 10) s.Economy.TraderSnapshotIntervalSeconds = 10;
		}

		public static void Save()
		{
			try {
				string path = FilePath;
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				if (File.Exists(path)) {
					File.Copy(path, path + ".bak", true);
				}
				File.WriteAllText(path, JsonConvert.SerializeObject(Settings, Formatting.Indented));
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: could not save {FILE_NAME}: {e.Message}");
			}
		}

		public static void AfterChange()
		{
			Normalize();
			Save();
		}
	}
}
