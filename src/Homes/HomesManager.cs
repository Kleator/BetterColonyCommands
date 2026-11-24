using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pipliz;
using UnityEngine;

namespace ColonyCommands
{
	public class HomePoint
	{
		public string Name = "home";
		public float X;
		public float Y;
		public float Z;

		public HomePoint Clone() => new HomePoint { Name = Name, X = X, Y = Y, Z = Z };
		public bool SameAs(HomePoint o) => o != null && Name == o.Name && X == o.X && Y == o.Y && Z == o.Z;
	}

	// Home points (/sethome, /home, /homes, /delhome) - a Better Colony Commands feature, moved here
	// from Pytonia Automation. Better Colony Commands always owns them: its own data (bcc-homes.json),
	// its own limits per role (bcc-settings.json -> Homes) and its own commands, which answer before
	// Pytonia Automation's (routed by the CommandInterceptor) if an older Pytonia Automation with a
	// homes module is still installed.
	// Migration: at every start, homes found in pytonia-automation.json that Better Colony Commands
	// doesn't know yet are imported (three-way merge against the last import, so homes deleted in BCC
	// stay deleted). Nothing is written back to Pytonia Automation.
	public static class HomesManager
	{
		public const string FILE_NAME = "bcc-homes.json";

		class Store
		{
			public Dictionary<string, List<HomePoint>> Homes = new Dictionary<string, List<HomePoint>>();
			// state of Pytonia Automation's homes at the last import
			public Dictionary<string, List<HomePoint>> PytoniaSnapshot = new Dictionary<string, List<HomePoint>>();
			// Pytonia Automation's home permissions were translated once
			public bool PermissionsMigrated;
		}

		// Pytonia Automation home permission -> Better Colony Commands permissions
		static readonly (string from, string[] to)[] PermissionMap = {
			("pytonia.automation.home", new[] { "bcc.cmd.home", "bcc.cmd.homes" }),   // Pytonia's /homes used /home's permission
			("pytonia.automation.sethome", new[] { "bcc.cmd.sethome" }),
			("pytonia.automation.delhome", new[] { "bcc.cmd.delhome" })
		};

		static Store store = new Store();
		static bool loadFailed;

		static BccSettings.HomesSettings Cfg => BccSettingsManager.Settings.Homes;
		static string FilePath => Path.Combine("gamedata", "savegames", ServerManager.WorldName, FILE_NAME);

		public static bool IsActive => Cfg.Enabled && !loadFailed;

		public static void AfterWorldLoad()
		{
			Load();
			ImportRoleLimitsOnce();
			ImportFromPytonia();
			MigratePermissionsOnce();
		}

		// game rule: a permission entry grants every permission that starts with it ("" = all)
		static bool Grants(string entry, string permission)
		{
			return entry != null && entry.Length > 0 && permission.StartsWith(entry, StringComparison.Ordinal);
		}

		// Once per world that ran Pytonia Automation: whoever had a Pytonia home permission (directly or
		// through a parent like "pytonia.automation") gets the matching Better Colony Commands permission.
		// Once assigned, those bcc.cmd.* permissions are strict - exactly like Pytonia's were. If nobody
		// had a Pytonia home permission, only all-permission roles could use the command before, so the
		// BCC permission is given to those roles to keep it restricted.
		static void MigratePermissionsOnce()
		{
			if (loadFailed || store.PermissionsMigrated || !File.Exists(PytoniaBridge.DiskFilePath)) {
				return;
			}
			try {
				// Pytonia Automation without a home module (1.0.7+, fresh installs): nothing to take over -
				// the fallback below must not restrict BCC's home commands to admins there
				bool pytoniaHadHomes = PytoniaBridge.ReadDisk()?["Homes"] is JObject;
				RoleManager.ReloadFromDisk();
				Dictionary<string, bool> granted = PermissionMap.ToDictionary(m => m.from, m => false);
				List<string> report = new List<string>();
				bool rolesChanged = false;

				foreach (RoleManager.Role role in RoleManager.All) {
					foreach ((string from, string[] to) in PermissionMap) {
						if (!role.Permissions.Any(p => Grants(p, from))) {
							continue;
						}
						granted[from] = true;
						foreach (string t in to.Where(t => !role.Permissions.Contains(t))) {
							role.Permissions.Add(t);
							rolesChanged = true;
							report.Add($"role {role.Name} +{t}");
						}
					}
				}

				// players with own permissions (permissionusers.json)
				string usersPath = Path.Combine("gamedata", "savegames", ServerManager.WorldName, "permissionusers.json");
				bool usersChanged = false;
				if (File.Exists(usersPath)) {
					JObject users = JObject.Parse(File.ReadAllText(usersPath));
					foreach (JProperty u in users.Properties()) {
						if (!(u.Value["permissions"] is JArray perms)) {
							continue;
						}
						List<string> list = perms.Where(t => t.Type == JTokenType.String).Select(t => t.Value<string>()).ToList();
						foreach ((string from, string[] to) in PermissionMap) {
							if (!list.Any(p => Grants(p, from))) {
								continue;
							}
							granted[from] = true;
							foreach (string t in to.Where(t => !list.Contains(t))) {
								perms.Add(t);
								list.Add(t);
								usersChanged = true;
								report.Add($"player {u.Name} +{t}");
							}
						}
					}
					if (usersChanged) {
						File.Copy(usersPath, usersPath + ".bak", true);
						File.WriteAllText(usersPath, users.ToString(Formatting.Indented));
					}
				}

				// nobody had it, but Pytonia Automation had homes: keep the command restricted to the all-permission roles
				foreach ((string from, string[] to) in PermissionMap.Where(m => pytoniaHadHomes && !granted[m.from])) {
					List<RoleManager.Role> admins = RoleManager.All.Where(r => r.Permissions.Contains("")).ToList();
					foreach (RoleManager.Role admin in admins) {
						foreach (string t in to.Where(t => !admin.Permissions.Contains(t))) {
							admin.Permissions.Add(t);
							rolesChanged = true;
							report.Add($"role {admin.Name} +{t} (nobody had {from})");
						}
					}
				}

				if (rolesChanged) {
					RoleManager.SaveRolesAndReload();
				} else if (usersChanged) {
					PermissionsManager.Reload();
					PermissionsManager.EnsureLoadedSuccesfully();
					BccPermissions.Refresh(true);
				}
				store.PermissionsMigrated = true;
				Save();
				Log.Write(report.Count == 0
					? "Better Colony Commands: Pytonia Automation home permissions checked - nothing to migrate"
					: $"Better Colony Commands: migrated Pytonia Automation home permissions: {string.Join(", ", report)}");
			} catch (Exception e) {
				Log.WriteException("Better Colony Commands: home permission migration failed (will be retried on the next start)", e);
			}
		}

		static void Load()
		{
			loadFailed = false;
			store = new Store();
			if (!File.Exists(FilePath)) {
				return;
			}
			try {
				store = JsonConvert.DeserializeObject<Store>(File.ReadAllText(FilePath)) ?? new Store();
				if (store.Homes == null) store.Homes = new Dictionary<string, List<HomePoint>>();
				if (store.PytoniaSnapshot == null) store.PytoniaSnapshot = new Dictionary<string, List<HomePoint>>();
				PlayerKeyMigration.MigrateLegacyKeys(store.Homes, FILE_NAME);
			} catch (Exception e) {
				loadFailed = true;
				store = new Store();
				Log.WriteError($"Better Colony Commands: could not read {FILE_NAME} - file left untouched, homes disabled this session: {e.Message}");
			}
		}

		static void Save()
		{
			if (loadFailed) {
				return;
			}
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				if (File.Exists(FilePath)) {
					File.Copy(FilePath, FilePath + ".bak", true);
				}
				File.WriteAllText(FilePath, JsonConvert.SerializeObject(store, Formatting.Indented));
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: could not save {FILE_NAME}: {e.Message}");
			}
		}

		// ------------------------------------------------------------------ migration from Pytonia Automation

		static Dictionary<string, List<HomePoint>> ReadPytoniaFile()
		{
			JObject root = PytoniaBridge.ReadDisk();
			if (root?["Homes"]?["PlayerHomes"] is JObject ph) {
				try {
					return ph.ToObject<Dictionary<string, List<HomePoint>>>();
				} catch {
				}
			}
			return null;
		}

		static Dictionary<string, HomePoint> Flatten(Dictionary<string, List<HomePoint>> d)
		{
			Dictionary<string, HomePoint> f = new Dictionary<string, HomePoint>();
			if (d == null) {
				return f;
			}
			foreach (KeyValuePair<string, List<HomePoint>> kvp in d) {
				foreach (HomePoint h in kvp.Value ?? new List<HomePoint>()) {
					f[kvp.Key + "\u0001" + (h.Name ?? "home").ToLowerInvariant()] = h;
				}
			}
			return f;
		}

		// per (player, home): changed on one side -> that side, deleted on one side -> deleted
		public static Dictionary<string, List<HomePoint>> Merge(Dictionary<string, List<HomePoint>> mine, Dictionary<string, List<HomePoint>> theirs, Dictionary<string, List<HomePoint>> snapshot)
		{
			Dictionary<string, HomePoint> m = Flatten(mine), t = Flatten(theirs), s = Flatten(snapshot);
			HashSet<string> keys = new HashSet<string>(m.Keys);
			keys.UnionWith(t.Keys);
			keys.UnionWith(s.Keys);
			Dictionary<string, List<HomePoint>> result = new Dictionary<string, List<HomePoint>>();
			foreach (string k in keys) {
				m.TryGetValue(k, out HomePoint hm);
				t.TryGetValue(k, out HomePoint ht);
				s.TryGetValue(k, out HomePoint hs);
				HomePoint chosen;
				if (hs != null) {
					if (hm == null && ht == null) chosen = null;
					else if (hm == null) chosen = ht.SameAs(hs) ? null : ht;     // deleted in BCC, unchanged in Pytonia
					else if (ht == null) chosen = hm.SameAs(hs) ? null : hm;     // deleted in Pytonia, unchanged in BCC
					else chosen = !hm.SameAs(hs) ? hm : ht;
				} else {
					chosen = hm ?? ht;
				}
				if (chosen == null) {
					continue;
				}
				string player = k.Substring(0, k.IndexOf('\u0001'));
				if (!result.TryGetValue(player, out List<HomePoint> list)) {
					list = new List<HomePoint>();
					result[player] = list;
				}
				list.Add(chosen.Clone());
			}
			return result;
		}

		static Dictionary<string, List<HomePoint>> DeepCopy(Dictionary<string, List<HomePoint>> d)
		{
			return d.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(h => h.Clone()).ToList());
		}

		static void ImportFromPytonia()
		{
			if (loadFailed) {
				return;
			}
			try {
				Dictionary<string, List<HomePoint>> theirs = ReadPytoniaFile();
				if (theirs == null) {
					return; // Pytonia Automation never stored homes on this world
				}
				int before = Flatten(store.Homes).Count;
				store.Homes = Merge(store.Homes, theirs, store.PytoniaSnapshot);
				store.PytoniaSnapshot = DeepCopy(theirs);
				Save();
				int imported = Flatten(store.Homes).Count - before;
				if (imported != 0) {
					Log.Write($"Better Colony Commands: imported {imported} home point(s) from pytonia-automation.json");
				}
			} catch (Exception e) {
				Log.WriteException("Better Colony Commands: home import from Pytonia Automation failed", e);
			}
		}

		// first run: take the per role home limits from Pytonia Automation's roles
		static void ImportRoleLimitsOnce()
		{
			if (Cfg.RoleLimits.Count > 0) {
				return;
			}
			JObject root = PytoniaBridge.ReadDisk();
			if (root?["Roles"] is JObject roles) {
				foreach (JProperty p in roles.Properties()) {
					JToken homes = p.Value["Homes"];
					if (homes != null && homes.Type == JTokenType.Integer) {
						Cfg.RoleLimits[p.Name] = homes.Value<int>();
					}
				}
				if (Cfg.RoleLimits.Count > 0) {
					BccSettingsManager.Save();
					Log.Write($"Better Colony Commands: took the home limits of {Cfg.RoleLimits.Count} role(s) from pytonia-automation.json");
				}
			}
		}

		// ------------------------------------------------------------------ operations

		static string Key(Players.Player p) => p.ID.ToStringReadable();

		public static List<HomePoint> GetHomes(Players.Player player)
		{
			return store.Homes.TryGetValue(Key(player), out List<HomePoint> homes) ? homes.Select(h => h.Clone()).ToList() : new List<HomePoint>();
		}

		static void SetHomes(Players.Player player, List<HomePoint> homes)
		{
			string key = Key(player);
			if (homes.Count == 0) {
				store.Homes.Remove(key);
			} else {
				store.Homes[key] = homes.Select(h => h.Clone()).ToList();
			}
			Save();
		}

		// limit from the player's roles (bcc-settings.json -> Homes.RoleLimits); -1 = unlimited
		public static int GetLimit(Players.Player player)
		{
			// assigned roles incl. the default role (also when the game only copied it into the player's entry)
			List<string> groups = RoleIndex.AssignedRoles(player);
			if (groups.Count == 0) {
				string def = RoleManager.DefaultRole;
				if (!string.IsNullOrEmpty(def)) {
					groups.Add(def);
				}
			}
			int limit = -2;
			foreach (string g in groups) {
				if (Cfg.RoleLimits.TryGetValue(g, out int l)) {
					if (l < 0) {
						return -1;
					}
					limit = System.Math.Max(limit, l);
				}
			}
			return limit == -2 ? Cfg.DefaultLimit : limit;
		}

		public static HomePoint Find(Players.Player player, string name)
		{
			return GetHomes(player).FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
		}

		// returns null on success, otherwise the error text
		public static string SetHome(Players.Player player, string name)
		{
			name = string.IsNullOrWhiteSpace(name) ? "home" : name.Trim();
			if (name.Length > 32) {
				name = name.Substring(0, 32);
			}
			List<HomePoint> homes = GetHomes(player);
			HomePoint home = homes.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
			if (home == null) {
				int limit = GetLimit(player);
				if (limit >= 0 && homes.Count >= limit) {
					return limit == 0
						? "Your role can't set home points."
						: $"You've reached your home limit ({limit}). Delete one with /delhome or reuse an existing name to move it.";
				}
				home = new HomePoint { Name = name };
				homes.Add(home);
			}
			Vector3 pos = player.PositionStanding;
			home.X = pos.x;
			home.Y = pos.y;
			home.Z = pos.z;
			SetHomes(player, homes);
			return null;
		}

		public static bool DeleteHome(Players.Player player, string name)
		{
			List<HomePoint> homes = GetHomes(player);
			int removed = homes.RemoveAll(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
			if (removed > 0) {
				SetHomes(player, homes);
			}
			return removed > 0;
		}

		public static void TeleportHome(Players.Player player, HomePoint home)
		{
			Helper.TeleportPlayer(player, new Vector3(home.X, home.Y, home.Z));
		}

		public static void RenameRole(string oldName, string newName)
		{
			if (Cfg.RoleLimits.TryGetValue(oldName, out int limit)) {
				Cfg.RoleLimits.Remove(oldName);
				if (newName != null) {
					Cfg.RoleLimits[newName] = limit;
				}
				BccSettingsManager.Save();
			}
		}
	}
}
