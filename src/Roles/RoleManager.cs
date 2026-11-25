using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pipliz;

namespace ColonyCommands
{
	// Roles = the game's permission groups (permissiongroups.json). A role "includes" other roles
	// and thereby inherits all their permissions; shown as a tree with the including (higher) role
	// above the included (lower) ones. Edits are written to the world's permissiongroups.json (the
	// game's default file is copied there first) and the game's permissions are reloaded.
	// Renaming updates every place a role name is used: other roles' includes, the default role,
	// permissionusers.json, permission strings that end in the role name (betterchat.<role>,
	// permissions.setgroup.<role>), chatcolors.json, jail-config.json, Pytonia Automation and the
	// home limits.
	public static class RoleManager
	{
		public class Role
		{
			public string Name;
			public List<string> Includes = new List<string>();
			public List<string> Permissions = new List<string>();
		}

		public const string BETTERCHAT_PREFIX = AntiGrief.MOD_PREFIX + "betterchat.";
		static readonly string[] RoleSuffixPermissionPrefixes = { BETTERCHAT_PREFIX, "permissions.setgroup.", "permissions.addgroup.", "permissions.removegroup." };
		static readonly Regex ValidName = new Regex(@"^[A-Za-z0-9_\-]{1,32}$");

		static JObject root;
		static List<Role> roles = new List<Role>();
		static string defaultRole = "";
		static DateTime loadedAt = DateTime.MinValue;

		static string WorldDir => Path.Combine("gamedata", "savegames", ServerManager.WorldName);
		static string WorldGroupsPath => Path.Combine(WorldDir, "permissiongroups.json");
		static string DefaultGroupsPath => Path.Combine("gamedata", "settings", "permissiongroups.json");
		static string UsersPath => Path.Combine(WorldDir, "permissionusers.json");

		public static string DefaultRole {
			get {
				EnsureLoaded();
				return defaultRole;
			}
		}

		public static List<Role> All {
			get {
				EnsureLoaded();
				return roles;
			}
		}

		public static Role Get(string name)
		{
			EnsureLoaded();
			return roles.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
		}

		static void EnsureLoaded()
		{
			// re-read now and then, the file may have been edited by hand or /reloadpermission
			if (root == null || (DateTime.UtcNow - loadedAt).TotalSeconds > 20) {
				Load();
			}
		}

		public static void Load()
		{
			loadedAt = DateTime.UtcNow;
			string path = File.Exists(WorldGroupsPath) ? WorldGroupsPath : DefaultGroupsPath;
			try {
				root = JObject.Parse(File.ReadAllText(path));
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: could not read {path}: {e.Message}");
				root = new JObject { ["defaults"] = new JObject { ["normal"] = "peasant" }, ["groups"] = new JObject() };
			}
			defaultRole = root["defaults"]?["normal"]?.ToString() ?? "";
			roles = new List<Role>();
			if (root["groups"] is JObject groups) {
				foreach (JProperty g in groups.Properties()) {
					roles.Add(new Role {
						Name = g.Name,
						Includes = Strings(g.Value["includes"]),
						Permissions = Strings(g.Value["permissions"])
					});
				}
			}
		}

		static List<string> Strings(JToken token)
		{
			List<string> list = new List<string>();
			if (token is JArray arr) {
				foreach (JToken t in arr) {
					if (t.Type == JTokenType.String) {
						list.Add(t.Value<string>());
					}
				}
			}
			return list;
		}

		// writes the world's permissiongroups.json and reloads the game's permissions
		static void SaveAndReload()
		{
			JObject groups = new JObject();
			foreach (Role r in roles) {
				JObject g = new JObject();
				if (r.Includes.Count > 0) g["includes"] = new JArray(r.Includes);
				g["permissions"] = new JArray(r.Permissions);
				groups[r.Name] = g;
			}
			if (root == null) {
				root = new JObject();
			}
			if (!(root["defaults"] is JObject)) {
				root["defaults"] = new JObject();
			}
			root["defaults"]["normal"] = defaultRole;
			root["groups"] = groups;
			try {
				Directory.CreateDirectory(WorldDir);
				if (File.Exists(WorldGroupsPath)) {
					File.Copy(WorldGroupsPath, WorldGroupsPath + ".bak", true);
				}
				File.WriteAllText(WorldGroupsPath, root.ToString(Formatting.Indented));
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: could not write {WorldGroupsPath}: {e.Message}");
				return;
			}
			loadedAt = DateTime.UtcNow;
			ReloadGamePermissions();
			ChatColors.InvalidateRank();
			PermissionGroupsGuard.AfterRoleEditorSave();
		}

		static void ReloadGamePermissions()
		{
			try {
				PermissionsManager.Reload();
				PermissionsManager.EnsureLoadedSuccesfully();
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: permission reload failed: {e.Message}");
			}
			BccPermissions.Refresh(true);
		}

		// for bulk changes made directly on Role objects (e.g. permission migrations)
		public static void SaveRolesAndReload()
		{
			EnsureLoaded();
			SaveAndReload();
		}

		public static void ReloadFromDisk()
		{
			Load();
		}

		// ------------------------------------------------------------------ tree

		public static List<string> Parents(string role)
		{
			return All.Where(r => r.Includes.Any(i => string.Equals(i, role, StringComparison.OrdinalIgnoreCase))).Select(r => r.Name).ToList();
		}

		// roles not included by any other role = top of the tree
		public static List<string> Roots()
		{
			List<string> roots = All.Where(r => Parents(r.Name).Count == 0).Select(r => r.Name).ToList();
			return roots.Count > 0 ? roots : All.Select(r => r.Name).ToList();
		}

		// depth first: (role, depth, alreadyShownElsewhere)
		public static List<(string role, int depth, bool repeat)> Tree()
		{
			List<(string, int, bool)> result = new List<(string, int, bool)>();
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			void Walk(string name, int depth, HashSet<string> path)
			{
				bool repeat = seen.Contains(name);
				result.Add((name, depth, repeat));
				seen.Add(name);
				if (repeat || path.Contains(name)) {
					return;
				}
				Role r = Get(name);
				if (r == null) {
					return;
				}
				path.Add(name);
				foreach (string child in r.Includes) {
					if (Get(child) != null) {
						Walk(Get(child).Name, depth + 1, path);
					}
				}
				path.Remove(name);
			}
			foreach (string root in Roots()) {
				Walk(root, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
			}
			foreach (Role r in All.Where(r => !seen.Contains(r.Name))) {
				Walk(r.Name, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
			}
			return result;
		}

		// all roles below this one (inherited from)
		public static List<string> Descendants(string role)
		{
			List<string> result = new List<string>();
			Stack<string> todo = new Stack<string>();
			todo.Push(role);
			while (todo.Count > 0) {
				Role r = Get(todo.Pop());
				if (r == null) continue;
				foreach (string i in r.Includes) {
					if (!result.Contains(i, StringComparer.OrdinalIgnoreCase) && !string.Equals(i, role, StringComparison.OrdinalIgnoreCase)) {
						result.Add(i);
						todo.Push(i);
					}
				}
			}
			return result;
		}

		static bool WouldCreateCycle(string parent, string child)
		{
			return string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) || Descendants(child).Contains(parent, StringComparer.OrdinalIgnoreCase);
		}

		// ------------------------------------------------------------------ edits (return null = ok, else error)

		public static string Create(string name, string parent, string child)
		{
			EnsureLoaded();
			name = (name ?? "").Trim();
			if (!ValidName.IsMatch(name)) return "Role names may only contain letters, digits, _ and - (max. 32).";
			if (Get(name) != null) return $"A role '{name}' already exists.";
			Role parentRole = string.IsNullOrWhiteSpace(parent) ? null : Get(parent.Trim());
			Role childRole = string.IsNullOrWhiteSpace(child) ? null : Get(child.Trim());
			if (!string.IsNullOrWhiteSpace(parent) && parentRole == null) return $"There is no role '{parent}'.";
			if (!string.IsNullOrWhiteSpace(child) && childRole == null) return $"There is no role '{child}'.";
			Role role = new Role { Name = name };
			if (childRole != null) role.Includes.Add(childRole.Name);
			roles.Add(role);
			if (parentRole != null) parentRole.Includes.Add(name);
			SaveAndReload();
			Log.Write($"Better Colony Commands: role '{name}' created (under '{parentRole?.Name}', inherits '{childRole?.Name}')");
			return null;
		}

		public static string Delete(string name)
		{
			Role role = Get(name);
			if (role == null) return "Role not found.";
			if (string.Equals(role.Name, defaultRole, StringComparison.OrdinalIgnoreCase)) return "The default role of new players can't be deleted.";
			RoleTestManager.EndTestsInvolving(role.Name, "the role was deleted");
			roles.Remove(role);
			foreach (Role r in roles) {
				r.Includes.RemoveAll(i => string.Equals(i, role.Name, StringComparison.OrdinalIgnoreCase));
			}
			UpdateUsers(role.Name, null);
			SaveAndReload();
			RenameInOtherFiles(role.Name, null);
			Log.Write($"Better Colony Commands: role '{role.Name}' deleted");
			return null;
		}

		public static string Rename(string oldName, string newName)
		{
			Role role = Get(oldName);
			newName = (newName ?? "").Trim();
			if (role == null) return "Role not found.";
			if (!ValidName.IsMatch(newName)) return "Role names may only contain letters, digits, _ and - (max. 32).";
			if (string.Equals(role.Name, newName, StringComparison.Ordinal)) return null;
			if (Get(newName) != null && !string.Equals(newName, role.Name, StringComparison.OrdinalIgnoreCase)) return $"A role '{newName}' already exists.";
			// role tests remember role names - give those players their roles back before the name changes
			RoleTestManager.EndTestsInvolving(role.Name, "the role was renamed");
			string old = role.Name;
			role.Name = newName;
			foreach (Role r in roles) {
				for (int i = 0; i < r.Includes.Count; i++) {
					if (string.Equals(r.Includes[i], old, StringComparison.OrdinalIgnoreCase)) r.Includes[i] = newName;
				}
				RenamePermissionSuffix(r.Permissions, old, newName);
			}
			if (string.Equals(defaultRole, old, StringComparison.OrdinalIgnoreCase)) defaultRole = newName;
			UpdateUsers(old, newName);
			SaveAndReload();
			RenameInOtherFiles(old, newName);
			Log.Write($"Better Colony Commands: role '{old}' renamed to '{newName}'");
			return null;
		}

		static void RenamePermissionSuffix(List<string> perms, string oldName, string newName)
		{
			for (int i = perms.Count - 1; i >= 0; i--) {
				foreach (string prefix in RoleSuffixPermissionPrefixes) {
					if (string.Equals(perms[i], prefix + oldName, StringComparison.OrdinalIgnoreCase)) {
						if (newName != null) perms[i] = prefix + newName; else perms.RemoveAt(i);
						break;
					}
				}
			}
		}

		// permissionusers.json: role names in "includes", role-suffixed permissions
		static void UpdateUsers(string oldName, string newName)
		{
			if (!File.Exists(UsersPath)) {
				return;
			}
			try {
				JObject users = JObject.Parse(File.ReadAllText(UsersPath));
				bool changed = false;
				foreach (JProperty u in users.Properties()) {
					if (u.Value["includes"] is JArray inc) {
						for (int i = inc.Count - 1; i >= 0; i--) {
							if (string.Equals(inc[i].ToString(), oldName, StringComparison.OrdinalIgnoreCase)) {
								if (newName != null) inc[i] = newName; else inc.RemoveAt(i);
								changed = true;
							}
						}
					}
					if (u.Value["permissions"] is JArray perms) {
						List<string> list = Strings(perms);
						List<string> before = new List<string>(list);
						RenamePermissionSuffix(list, oldName, newName);
						if (!list.SequenceEqual(before)) {
							u.Value["permissions"] = new JArray(list);
							changed = true;
						}
					}
				}
				if (changed) {
					File.Copy(UsersPath, UsersPath + ".bak", true);
					File.WriteAllText(UsersPath, users.ToString(Formatting.Indented));
				}
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: could not update permissionusers.json: {e.Message}");
			}
		}

		static void RenameInOtherFiles(string oldName, string newName)
		{
			// chatcolors.json (keeps the order = chat priority)
			if (ChatColors.Colors.ContainsKey(oldName)) {
				Dictionary<string, ChatColorSpecification> renamed = new Dictionary<string, ChatColorSpecification>();
				foreach (KeyValuePair<string, ChatColorSpecification> kvp in ChatColors.Colors) {
					if (kvp.Key == oldName) {
						if (newName != null) renamed[newName] = kvp.Value;
					} else {
						renamed[kvp.Key] = kvp.Value;
					}
				}
				ChatColors.Colors = renamed;
				ChatColors.SaveChatColors();
			}
			// jail-config.json
			try {
				string jailPath = Path.Combine(WorldDir, "jail-config.json");
				if (File.Exists(jailPath)) {
					JObject jail = JObject.Parse(File.ReadAllText(jailPath));
					bool changed = false;
					foreach (string key in new[] { "prisonerGroup", "defaultGroup" }) {
						if (string.Equals(jail[key]?.ToString(), oldName, StringComparison.OrdinalIgnoreCase)) {
							jail[key] = newName ?? "";
							changed = true;
						}
					}
					foreach (string key in new[] { "rolesGrantedOnJail", "rolesGrantedOnRelease" }) {
						if (jail[key] is JArray arr) {
							for (int i = arr.Count - 1; i >= 0; i--) {
								if (string.Equals(arr[i].ToString(), oldName, StringComparison.OrdinalIgnoreCase)) {
									if (newName != null) arr[i] = newName; else arr.RemoveAt(i);
									changed = true;
								}
							}
						}
					}
					if (changed) {
						File.WriteAllText(jailPath, jail.ToString(Formatting.None));
						JailManager.ReloadConfig();
					}
				}
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not update jail-config.json: {e.Message}");
			}
			PytoniaBridge.RenameRole(oldName, newName);
			HomesManager.RenameRole(oldName, newName);
		}

		public static string AddInclude(string role, string child)
		{
			Role r = Get(role), c = Get(child?.Trim());
			if (r == null || c == null) return $"There is no role '{child}'.";
			if (r.Includes.Contains(c.Name, StringComparer.OrdinalIgnoreCase)) return null;
			if (WouldCreateCycle(r.Name, c.Name)) return $"'{c.Name}' already inherits from '{r.Name}' - that would be a loop.";
			r.Includes.Add(c.Name);
			SaveAndReload();
			return null;
		}

		public static void RemoveInclude(string role, string child)
		{
			Role r = Get(role);
			if (r != null && r.Includes.RemoveAll(i => string.Equals(i, child, StringComparison.OrdinalIgnoreCase)) > 0) {
				SaveAndReload();
			}
		}

		public static string AddPermission(string role, string permission)
		{
			Role r = Get(role);
			permission = (permission ?? "").Trim();
			if (r == null) return "Role not found.";
			if (permission.Contains(" ")) return "Permissions contain no spaces.";
			if (r.Permissions.Contains(permission)) return null;
			r.Permissions.Add(permission);
			SaveAndReload();
			return null;
		}

		public static void RemovePermission(string role, string permission)
		{
			Role r = Get(role);
			if (r != null && r.Permissions.Remove(permission)) {
				SaveAndReload();
			}
		}

		public static void SetDefault(string role)
		{
			Role r = Get(role);
			if (r != null) {
				defaultRole = r.Name;
				SaveAndReload();
			}
		}

		// chatcolors.json entry for this role; also makes sure the role carries the
		// betterchat.<role> permission that applies it, and orders chatcolors by the tree (higher first)
		public static void SetFormatting(string role, string prefix, string prefixColor, string nameColor, string textColor)
		{
			Role r = Get(role);
			if (r == null) {
				return;
			}
			bool any = !string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(prefixColor) || !string.IsNullOrEmpty(nameColor) || !string.IsNullOrEmpty(textColor);
			ChatColorSpecification spec = ChatColors.Colors.TryGetValue(r.Name, out ChatColorSpecification existing) ? existing : new ChatColorSpecification();
			spec.Prefix = Empty(prefix);
			spec.PrefixColor = Empty(prefixColor);
			spec.Name = Empty(nameColor);
			spec.Text = Empty(textColor);
			Dictionary<string, ChatColorSpecification> colors = new Dictionary<string, ChatColorSpecification>(ChatColors.Colors);
			if (any) colors[r.Name] = spec; else colors.Remove(r.Name);
			// chat priority = tree order (a player's highest role wins)
			List<string> order = Tree().Select(t => t.role).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			Dictionary<string, ChatColorSpecification> sorted = new Dictionary<string, ChatColorSpecification>();
			foreach (string key in colors.Keys.OrderBy(k => { int i = order.FindIndex(o => string.Equals(o, k, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; })) {
				sorted[key] = colors[key];
			}
			ChatColors.Colors = sorted;
			ChatColors.SaveChatColors();
			if (any && !r.Permissions.Contains(BETTERCHAT_PREFIX + r.Name)) {
				r.Permissions.Add(BETTERCHAT_PREFIX + r.Name);
				SaveAndReload();
			}
		}

		static string Empty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

		// players the role is assigned to (directly or as default role, also when the game only copied it)
		public static int PlayerCount(string role)
		{
			return RoleIndex.AssignedCount(role);
		}

		static int PlayerCountFromFile(string role)
		{
			int count = 0;
			try {
				if (File.Exists(UsersPath)) {
					JObject users = JObject.Parse(File.ReadAllText(UsersPath));
					foreach (JProperty u in users.Properties()) {
						if (u.Value["includes"] is JArray inc && inc.Any(t => string.Equals(t.ToString(), role, StringComparison.OrdinalIgnoreCase))) {
							count++;
						}
					}
				}
			} catch {
			}
			return count;
		}
	}
}
