using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Pipliz;

namespace ColonyCommands
{
	// Knows every permission in the game that we can find out about: Better Colony Commands' own,
	// permissions registered by other mods through BccApi / command registration, auto-created
	// command permissions, and everything actually written into permissiongroups.json /
	// permissionusers.json.
	//
	// "Open by default" permissions (auto-created for commands that had none) allow everybody as
	// long as that exact permission string is not assigned to any group or user. As soon as an admin
	// assigns it somewhere, it becomes a normal, strict permission.
	public static class BccPermissions
	{
		public const string PERMISSION_OVERVIEW = AntiGrief.MOD_PREFIX + "permissions";
		public const string PERMISSION_CONFIG = AntiGrief.MOD_PREFIX + "config";

		public class PermissionInfo
		{
			public string Permission;
			public string Mod;
			public string Description;
			public bool OpenByDefault;
		}

		public class AssignmentInfo
		{
			public List<string> GroupsExact = new List<string>();
			public List<string> GroupsViaParent = new List<string>();
			public int UsersExact;
			public int UsersViaParent;
			public bool IsUsed => GroupsExact.Count + GroupsViaParent.Count + UsersExact + UsersViaParent > 0;
		}

		static readonly Dictionary<string, PermissionInfo> known = new Dictionary<string, PermissionInfo>(StringComparer.Ordinal);

		// raw permission lists as written in the files: group name -> permissions
		static Dictionary<string, List<string>> groupPermissions = new Dictionary<string, List<string>>();
		static List<List<string>> userPermissions = new List<List<string>>();
		static HashSet<string> exactlyAssigned = new HashSet<string>(StringComparer.Ordinal);
		static DateTime lastRefresh = DateTime.MinValue;
		static HashSet<string> baseGamePermissions;
		const double REFRESH_SECONDS = 15.0;

		public static void Register(string permission, string mod, string description, bool openByDefault = false)
		{
			if (string.IsNullOrEmpty(permission)) {
				return;
			}
			if (known.TryGetValue(permission, out PermissionInfo existing)) {
				if (string.IsNullOrEmpty(existing.Description) && !string.IsNullOrEmpty(description)) {
					existing.Description = description;
				}
				if ((string.IsNullOrEmpty(existing.Mod) || existing.Mod == CommandRegistry.UNKNOWN_MOD) && !string.IsNullOrEmpty(mod)) {
					existing.Mod = mod;
				}
				existing.OpenByDefault |= openByDefault;
				return;
			}
			known[permission] = new PermissionInfo {
				Permission = permission,
				Mod = string.IsNullOrEmpty(mod) ? CommandRegistry.UNKNOWN_MOD : mod,
				Description = description ?? "",
				OpenByDefault = openByDefault
			};
		}

		public static bool TryGetInfo(string permission, out PermissionInfo info)
		{
			return known.TryGetValue(permission, out info);
		}

		// Re-reads which permissions are assigned where. Cheap enough, but cached for a few seconds
		// because it is consulted on every command typed.
		public static void Refresh(bool force = false)
		{
			if (force) {
				RoleIndex.Invalidate();
			}
			if (!force && (DateTime.UtcNow - lastRefresh).TotalSeconds < REFRESH_SECONDS) {
				return;
			}
			lastRefresh = DateTime.UtcNow;
			Dictionary<string, List<string>> groups = new Dictionary<string, List<string>>();
			List<List<string>> users = new List<List<string>>();
			HashSet<string> exact = new HashSet<string>(StringComparer.Ordinal);
			try {
				if (!PermissionsManager.EnsureLoadedSuccesfully()) {
					return;
				}
				if (PermissionsManager.Groups != null) {
					foreach (KeyValuePair<string, PermissionsManager.PermissionsGroup> kvp in PermissionsManager.Groups) {
						List<string> perms = ReadPermissions(kvp.Value);
						groups[kvp.Key] = perms;
						foreach (string p in perms) {
							exact.Add(p);
						}
					}
				}
				if (PermissionsManager.Users != null) {
					foreach (PermissionsManager.PermissionsGroup user in PermissionsManager.Users.Values) {
						List<string> perms = ReadPermissions(user);
						users.Add(perms);
						foreach (string p in perms) {
							exact.Add(p);
						}
					}
				}
				groupPermissions = groups;
				userPermissions = users;
				exactlyAssigned = exact;
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not read permission assignments: {e.Message}");
			}
		}

		// PermissionsGroup keeps its list private, but it is [JsonProperty("permissions")]
		static List<string> ReadPermissions(PermissionsManager.PermissionsGroup group)
		{
			List<string> result = new List<string>();
			if (group == null) {
				return result;
			}
			JObject serialized = JObject.FromObject(group);
			if (serialized["permissions"] is JArray arr) {
				foreach (JToken t in arr) {
					if (t.Type == JTokenType.String) {
						result.Add(t.Value<string>());
					}
				}
			}
			return result;
		}

		// Assigned = the permission itself, or a parent of it in dotted form (e.g. "colonysurvival.cmd.debug"
		// restricts every auto permission below it at once). The all-permission "" is ignored here,
		// otherwise every open command would be restricted as soon as a god role exists.
		public static bool IsAssignedExactly(string permission)
		{
			Refresh();
			if (exactlyAssigned.Contains(permission)) {
				return true;
			}
			foreach (string p in exactlyAssigned) {
				if (p.Length > 0 && permission.StartsWith(p + ".", StringComparison.Ordinal)) {
					return true;
				}
			}
			return false;
		}

		// Permission check with "open by default" semantics.
		public static bool HasOpenPermission(Players.Player player, string permission)
		{
			if (player == null || string.IsNullOrEmpty(permission)) {
				return true;
			}
			if (!IsAssignedExactly(permission)) {
				return true;
			}
			return PermissionsManager.HasPermission(player, permission);
		}

		public static bool CanSeePermissionOverview(Players.Player player)
		{
			return player != null && PermissionsManager.HasPermission(player, PERMISSION_OVERVIEW);
		}

		public static bool CanEditConfig(Players.Player player)
		{
			return player != null && PermissionsManager.HasPermission(player, PERMISSION_CONFIG);
		}

		public static AssignmentInfo GetAssignment(string permission)
		{
			Refresh();
			AssignmentInfo info = new AssignmentInfo();
			foreach (KeyValuePair<string, List<string>> kvp in groupPermissions) {
				if (kvp.Value.Contains(permission)) {
					info.GroupsExact.Add(kvp.Key);
				} else if (kvp.Value.Any(q => q.Length > 0 && permission.StartsWith(q, StringComparison.Ordinal))) {
					info.GroupsViaParent.Add(kvp.Key);
				}
			}
			foreach (List<string> perms in userPermissions) {
				if (perms.Contains(permission)) {
					info.UsersExact++;
				} else if (perms.Any(q => q.Length > 0 && permission.StartsWith(q, StringComparison.Ordinal))) {
					info.UsersViaParent++;
				}
			}
			return info;
		}

		// groups whose permission list contains the root ("") permission, i.e. "has all permissions"
		public static List<string> GetAllPermissionGroups()
		{
			Refresh();
			return groupPermissions.Where(kvp => kvp.Value.Contains("") || kvp.Value.Contains("*")).Select(kvp => kvp.Key).ToList();
		}

		// every permission we know of, including ones only found in the permission files
		public static List<PermissionInfo> GetAll()
		{
			Refresh();
			Dictionary<string, PermissionInfo> all = new Dictionary<string, PermissionInfo>(known, StringComparer.Ordinal);
			foreach (string p in exactlyAssigned) {
				if (p.Length == 0 || all.ContainsKey(p)) {
					continue;
				}
				all[p] = new PermissionInfo {
					Permission = p,
					Mod = GuessMod(p),
					Description = "(found in permission files)",
					OpenByDefault = false
				};
			}
			return all.Values.OrderBy(p => p.Mod == CommandRegistry.BCC_MOD ? 0 : 1).ThenBy(p => p.Mod).ThenBy(p => p.Permission).ToList();
		}

		// permissions listed in the game's own default permissiongroups.json belong to the base game
		static HashSet<string> BaseGamePermissions()
		{
			if (baseGamePermissions != null) {
				return baseGamePermissions;
			}
			baseGamePermissions = new HashSet<string>(StringComparer.Ordinal);
			try {
				string path = System.IO.Path.Combine("gamedata", "settings", "permissiongroups.json");
				if (System.IO.File.Exists(path)) {
					// the file contains /* comments */, which Newtonsoft's JObject.Parse accepts
					JObject root = JObject.Parse(System.IO.File.ReadAllText(path));
					if (root["groups"] is JObject groups) {
						foreach (JProperty g in groups.Properties()) {
							if (g.Value["permissions"] is JArray arr) {
								foreach (JToken t in arr) {
									if (t.Type == JTokenType.String && t.Value<string>().Length > 0) {
										baseGamePermissions.Add(t.Value<string>());
									}
								}
							}
						}
					}
				}
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not read the default permissiongroups.json: {e.Message}");
			}
			return baseGamePermissions;
		}

		static string GuessMod(string permission)
		{
			if (BaseGamePermissions().Contains(permission)) {
				return "Colony Survival";
			}
			// longest known permission that is a prefix of this one, or that this one is a prefix of
			string bestMod = null;
			int bestLen = 0;
			foreach (PermissionInfo info in known.Values) {
				string common = CommonDottedPrefix(info.Permission, permission);
				if (common.Length > bestLen && common.Contains(".")) {
					bestLen = common.Length;
					bestMod = info.Mod;
				}
			}
			if (bestMod != null) {
				return bestMod;
			}
			if (permission.StartsWith("mods.scarabol") || permission.StartsWith("bcc.")) {
				return CommandRegistry.BCC_MOD;
			}
			if (permission.StartsWith("betterchat") || permission.StartsWith("colonistcapacity")) {
				return CommandRegistry.BCC_MOD;
			}
			if (!permission.Contains(".") || permission.StartsWith("permissions") || permission.StartsWith("cheats")) {
				return "Colony Survival";
			}
			return CommandRegistry.UNKNOWN_MOD;
		}

		static string CommonDottedPrefix(string a, string b)
		{
			string[] pa = a.Split('.');
			string[] pb = b.Split('.');
			List<string> common = new List<string>();
			for (int i = 0; i < pa.Length && i < pb.Length; i++) {
				if (pa[i] != pb[i]) {
					break;
				}
				common.Add(pa[i]);
			}
			// require at least two shared segments to attribute
			return common.Count >= 2 ? string.Join(".", common) : "";
		}
	}
}
