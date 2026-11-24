using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pipliz;

namespace ColonyCommands
{
	// Protects the permission groups (roles) against game updates.
	//
	// The game reads the world's own permissiongroups.json if there is one, otherwise
	// gamedata/settings/permissiongroups.json - and depending on the server setup a game update puts the
	// game's default file back there. Better Colony Commands keeps a copy of the last state in the world
	// folder (bcc-permissiongroups-backup.json, game updates never touch savegames), together with the game
	// version it belongs to. At every start:
	//  - same content as the backup        -> nothing to do (a new game version is noted)
	//  - other content, game version changed -> reset by the update: the backup is written as the world's
	//    own permissiongroups.json (wins over the settings file from now on), the game's permissions are
	//    reloaded, the reset file is kept in bcc-permission-backups, admins are told when they join
	//  - other content, same game version  -> changed on purpose: becomes the new backup (old one archived)
	// The backup is also renewed after every change in the role editor and at autosave if the file changed.
	public static class PermissionGroupsGuard
	{
		public class Backup
		{
			public string GameVersion = "";
			public DateTime SavedUtc;
			public string Source = "";      // file the content was taken from
			public string Hash = "";
			public string Content = "";     // the file exactly as it was (comments included)
			public int Roles;
			public DateTime RestoredUtc;    // last automatic restore
			public string RestoredFrom = "";
		}

		static Backup backup;
		static string restoreNotice;
		static readonly HashSet<ulong> noticeShown = new HashSet<ulong>();

		static string WorldDir => Path.Combine("gamedata", "savegames", ServerManager.WorldName);
		static string WorldGroupsPath => Path.Combine(WorldDir, "permissiongroups.json");
		static string SettingsGroupsPath => Path.Combine("gamedata", "settings", "permissiongroups.json");
		static string BackupPath => Path.Combine(WorldDir, "bcc-permissiongroups-backup.json");
		static string HistoryDir => Path.Combine(WorldDir, "bcc-permission-backups");
		static string ActivePath => File.Exists(WorldGroupsPath) ? WorldGroupsPath : SettingsGroupsPath;

		static BccSettings.PermissionBackupSettings Cfg => BccSettingsManager.Settings.Permissions ?? (BccSettingsManager.Settings.Permissions = new BccSettings.PermissionBackupSettings());

		public static string CurrentGameVersion()
		{
			try {
				return VersionTrackerServer.CurrentVersion.ToString();
			} catch {
				return "unknown";
			}
		}

		// ------------------------------------------------------------------ start

		public static void AfterWorldLoad()
		{
			restoreNotice = null;
			noticeShown.Clear();
			try {
				Check(CurrentGameVersion());
			} catch (Exception e) {
				Log.WriteException("Better Colony Commands: permission groups backup check failed", e);
			}
		}

		// gameVersion is a parameter so the self test can simulate an update
		public static string Check(string gameVersion)
		{
			backup = LoadBackup();
			string active = ActivePath;
			if (!File.Exists(active)) {
				return "no permission groups file";
			}
			string content = File.ReadAllText(active);
			string hash = Hash(content);
			if (backup == null) {
				SaveBackup(content, active, gameVersion, "first backup");
				return "first backup";
			}
			if (hash == backup.Hash) {
				if (backup.GameVersion != gameVersion) {
					Log.Write($"Better Colony Commands: game version {backup.GameVersion} -> {gameVersion}, permission groups unchanged");
					backup.GameVersion = gameVersion;
					WriteBackup(backup);
				}
				return "unchanged";
			}
			if (backup.GameVersion != gameVersion && backup.GameVersion.Length > 0) {
				if (!Cfg.RestoreGroupsAfterGameUpdate) {
					Log.WriteWarning($"Better Colony Commands: {active} changed with the game update {backup.GameVersion} -> {gameVersion}; automatic restore is off (bcc-settings.json -> Permissions) - the backup stays in {BackupPath}");
					return "changed with update, restore off";
				}
				return Restore(content, active, gameVersion);
			}
			// same game version and the file looks like the game's default while the backup had more roles:
			// some hosts put the game files back on every start - that's a reset too, not a change on purpose
			if (LooksLikeGameDefault(content, backup.Content)) {
				if (!Cfg.RestoreGroupsAfterGameUpdate) {
					Log.WriteWarning($"Better Colony Commands: {active} was reset to the game's default; automatic restore is off - the backup stays in {BackupPath}");
					return "reset to default, restore off";
				}
				return Restore(content, active, gameVersion);
			}
			// same game version: someone changed the roles on purpose
			SaveBackup(content, active, gameVersion, "changed outside the role editor");
			return "adopted";
		}

		static readonly HashSet<string> GameDefaultRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "god", "king", "peasant" };

		static bool LooksLikeGameDefault(string content, string backupContent)
		{
			try {
				List<string> now = (JObject.Parse(content)["groups"] as JObject)?.Properties().Select(p => p.Name).ToList() ?? new List<string>();
				List<string> before = (JObject.Parse(backupContent)["groups"] as JObject)?.Properties().Select(p => p.Name).ToList() ?? new List<string>();
				return now.Count > 0 && now.All(GameDefaultRoles.Contains) && before.Any(r => !now.Contains(r, StringComparer.OrdinalIgnoreCase));
			} catch {
				return false;
			}
		}

		static string Restore(string resetContent, string resetPath, string gameVersion)
		{
			if (!IsValidGroups(backup.Content, out string why)) {
				Log.WriteError($"Better Colony Commands: permission groups changed with the game update, but the backup can't be used ({why}) - nothing restored");
				return "backup invalid";
			}
			// keep what the update put there
			Archive(resetContent, backup.GameVersion == gameVersion ? $"reset-to-default-{gameVersion}" : $"reset-by-update-{backup.GameVersion}-to-{gameVersion}");
			Directory.CreateDirectory(WorldDir);
			if (File.Exists(WorldGroupsPath)) {
				File.Copy(WorldGroupsPath, WorldGroupsPath + ".bak", true);
			}
			File.WriteAllText(WorldGroupsPath, backup.Content);
			string from = backup.GameVersion;
			backup.GameVersion = gameVersion;
			backup.RestoredUtc = DateTime.UtcNow;
			backup.RestoredFrom = from;
			backup.Source = WorldGroupsPath;
			WriteBackup(backup);

			PermissionsManager.Reload();
			PermissionsManager.EnsureLoadedSuccesfully();
			RoleManager.ReloadFromDisk();
			BccPermissions.Refresh(true);
			RoleIndex.Invalidate();

			string resetName = resetPath == WorldGroupsPath ? "the world's permissiongroups.json" : "gamedata/settings/permissiongroups.json";
			restoreNotice = (from == gameVersion ? $"{resetName} was reset to the game's default (game {gameVersion})." : $"Game update {from} -> {gameVersion} reset {resetName}.") + $" Better Colony Commands put back the last state ({backup.Roles} roles, saved {backup.SavedUtc.ToLocalTime():dd.MM.yyyy HH:mm}) as the world's own permissiongroups.json and reloaded the permissions. The reset file is kept in bcc-permission-backups.";
			Log.WriteWarning("Better Colony Commands: " + restoreNotice);
			return "restored";
		}

		// ------------------------------------------------------------------ keeping the backup current

		// after every change made by the role editor
		public static void AfterRoleEditorSave()
		{
			Refresh("role editor");
		}

		// at autosave: picks up changes made in the file (and reloaded with /reloadpermission)
		public static void OnAutoSave()
		{
			Refresh("autosave");
		}

		static void Refresh(string reason)
		{
			try {
				string active = ActivePath;
				if (!File.Exists(active)) {
					return;
				}
				string content = File.ReadAllText(active);
				if (backup != null && Hash(content) == backup.Hash) {
					return;
				}
				SaveBackup(content, active, CurrentGameVersion(), reason);
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: permission groups backup not renewed: {e.Message}");
			}
		}

		static void SaveBackup(string content, string source, string gameVersion, string reason)
		{
			if (!IsValidGroups(content, out string why)) {
				// never replace a good backup with a broken file
				Log.WriteWarning($"Better Colony Commands: {source} is not a valid permission groups file ({why}) - backup kept");
				return;
			}
			if (backup != null && backup.Content.Length > 0) {
				Archive(backup.Content, $"before-{reason.Replace(' ', '-')}");
			}
			Backup b = new Backup {
				GameVersion = gameVersion,
				SavedUtc = DateTime.UtcNow,
				Source = source,
				Hash = Hash(content),
				Content = content,
				Roles = CountRoles(content),
				RestoredUtc = backup?.RestoredUtc ?? default(DateTime),
				RestoredFrom = backup?.RestoredFrom ?? ""
			};
			WriteBackup(b);
			backup = b;
			Log.Write($"Better Colony Commands: permission groups backed up ({reason}, {b.Roles} roles, game {gameVersion})");
		}

		// ------------------------------------------------------------------ admins

		public static void OnPlayerConnectedLate(Players.Player player)
		{
			if (restoreNotice == null || player == null || !player.ID.HasSteamID || !BccPermissions.CanEditConfig(player)
				|| !noticeShown.Add(player.ID.SteamID.m_SteamID)) {
				return;
			}
			BccChat.Send(player, Style.Error("[Better Colony Commands] ") + restoreNotice);
		}

		public static string Status()
		{
			Backup b = backup ?? LoadBackup();
			if (b == null) {
				return "No backup yet - it is made at the next start.";
			}
			string s = $"Backup: {b.Roles} roles, saved {b.SavedUtc.ToLocalTime():dd.MM.yyyy HH:mm}, game {b.GameVersion} (now {CurrentGameVersion()}), from {b.Source}.";
			if (b.RestoredUtc != default(DateTime)) {
				s += $" Last automatic restore: {b.RestoredUtc.ToLocalTime():dd.MM.yyyy HH:mm} ({(b.RestoredFrom == b.GameVersion ? "the file was reset to the game's default" : "after the update from " + b.RestoredFrom)}).";
			}
			if (restoreNotice != null) {
				s += " " + Style.Error("This start: ") + restoreNotice;
			}
			return s;
		}

		// ------------------------------------------------------------------ files

		static Backup LoadBackup()
		{
			foreach (string path in new[] { BackupPath, BackupPath + ".bak" }) {
				if (!File.Exists(path)) {
					continue;
				}
				try {
					Backup b = JsonConvert.DeserializeObject<Backup>(File.ReadAllText(path));
					if (b != null && b.Content.Length > 0) {
						return b;
					}
				} catch (Exception e) {
					Log.WriteWarning($"Better Colony Commands: could not read {path}: {e.Message}");
				}
			}
			return null;
		}

		static void WriteBackup(Backup b)
		{
			Directory.CreateDirectory(WorldDir);
			string tmp = BackupPath + ".tmp";
			File.WriteAllText(tmp, JsonConvert.SerializeObject(b, Formatting.Indented));
			if (File.Exists(BackupPath)) {
				File.Copy(BackupPath, BackupPath + ".bak", true);
			}
			File.Copy(tmp, BackupPath, true);
			File.Delete(tmp);
		}

		static void Archive(string content, string label)
		{
			try {
				Directory.CreateDirectory(HistoryDir);
				File.WriteAllText(Path.Combine(HistoryDir, $"permissiongroups-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{label}.json"), content);
				List<FileInfo> files = new DirectoryInfo(HistoryDir).GetFiles("permissiongroups-*.json").OrderByDescending(f => f.Name).ToList();
				foreach (FileInfo old in files.Skip(System.Math.Max(1, Cfg.KeepBackups))) {
					old.Delete();
				}
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not archive permission groups: {e.Message}");
			}
		}

		static bool IsValidGroups(string content, out string why)
		{
			why = null;
			try {
				JObject root = JObject.Parse(content);   // comments are allowed, like in the game's own file
				if (!(root["groups"] is JObject groups) || !groups.Properties().Any()) {
					why = "no groups";
					return false;
				}
				string def = root["defaults"]?["normal"]?.ToString();
				if (string.IsNullOrEmpty(def) || groups[def] == null) {
					why = "default group missing";
					return false;
				}
				return true;
			} catch (Exception e) {
				why = e.Message;
				return false;
			}
		}

		static int CountRoles(string content)
		{
			try {
				return (JObject.Parse(content)["groups"] as JObject)?.Properties().Count() ?? 0;
			} catch {
				return 0;
			}
		}

		static string Hash(string content)
		{
			using (SHA256 sha = SHA256.Create()) {
				return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n")))).Replace("-", "");
			}
		}
	}
}
