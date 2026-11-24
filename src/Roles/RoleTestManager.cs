using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pipliz;
using Steamworks;

namespace ColonyCommands
{
	// Role test (Roles & permissions -> "Test"): the player keeps only the tested role for
	// DURATION_SECONDS, then gets their own roles back.
	//
	// Crash safety: the roles before the test are written to the world's bcc-roletests.json BEFORE
	// anything changes, and the entry is removed only after the roles were restored. The roles come
	// back when the time is up, when the player disconnects, when the server stops and - after a
	// crash - at the next start of the world (the player gets a chat notice when joining).
	// Roles given to the player by someone else during the test (an admin with the game's
	// commands, Pytonia Automation's time based roles) are kept. BCC's own role tools (role pages,
	// player moderation, /promote, jail, war) end the test first, so they change the real roles.
	public static class RoleTestManager
	{
		public const int DURATION_SECONDS = 120;
		const int WARN_SECONDS = 30;
		const string PREFIX = "<b>[Role test]</b> ";

		public class TestRecord
		{
			public string Name = "";
			public string Role = "";
			public bool HadEntry;                            // own entry in permissionusers.json before the test
			public List<string> Roles = new List<string>();  // that entry's roles ("includes") before the test
			public DateTime StartedUtc;
			public DateTime EndsUtc;
			public bool Warned;
		}

		static Dictionary<string, TestRecord> tests = new Dictionary<string, TestRecord>();   // key: SteamID
		static readonly HashSet<string> restoredWhileAway = new HashSet<string>();
		static bool loadFailed;
		static DateTime nextTick = DateTime.MinValue;

		static string FilePath => Path.Combine("gamedata", "savegames", ServerManager.WorldName, "bcc-roletests.json");

		// ------------------------------------------------------------------ state

		public static bool IsTesting(Players.Player player)
		{
			return player != null && player.ID.HasSteamID && tests.ContainsKey(Key(player.ID.SteamID));
		}

		public static TestRecord Get(Players.Player player)
		{
			if (player == null || !player.ID.HasSteamID) {
				return null;
			}
			tests.TryGetValue(Key(player.ID.SteamID), out TestRecord r);
			return r;
		}

		public static int SecondsLeft(TestRecord r)
		{
			return r == null ? 0 : System.Math.Max(0, (int)System.Math.Ceiling((r.EndsUtc - DateTime.UtcNow).TotalSeconds));
		}

		static string Key(CSteamID id) => id.m_SteamID.ToString();

		// ------------------------------------------------------------------ start / end

		// null = started, otherwise the reason it can't start
		public static string Start(Players.Player player, string role)
		{
			if (player == null || !player.ID.HasSteamID) {
				return UiPlayerPages.NoSteamText;
			}
			if (loadFailed) {
				return "Role tests are blocked: bcc-roletests.json could not be read (see the server log).";
			}
			RoleManager.Role r = RoleManager.Get(role);
			if (r == null || !PermissionsManager.EnsureLoadedSuccesfully() || !PermissionsManager.Groups.ContainsKey(r.Name)) {
				return $"There is no role '{role}'.";
			}
			role = r.Name;
			string key = Key(player.ID.SteamID);
			bool switching = tests.TryGetValue(key, out TestRecord rec);
			if (!switching) {
				bool hadEntry = PermissionsManager.Users.TryGetValue(player.ID.SteamID, out PermissionsManager.PermissionsGroup current);
				rec = new TestRecord {
					Name = player.Name,
					HadEntry = hadEntry,
					Roles = hadEntry ? ReadIncludes(current) : new List<string>()
				};
			}
			string oldRole = rec.Role;
			rec.Role = role;
			rec.StartedUtc = DateTime.UtcNow;
			rec.EndsUtc = rec.StartedUtc.AddSeconds(DURATION_SECONDS);
			rec.Warned = false;
			// the way back is on disk before anything changes
			tests[key] = rec;
			try {
				Save();
			} catch (Exception e) {
				if (switching) {
					rec.Role = oldRole;
					rec.EndsUtc = DateTime.UtcNow.AddSeconds(5); // ends the running test soon
				} else {
					tests.Remove(key);
				}
				Log.WriteException("Better Colony Commands: role test not started - bcc-roletests.json could not be written", e);
				return "The role test could not be saved, so it was not started (see the server log).";
			}
			PermissionsManager.Users.TryGetValue(player.ID.SteamID, out PermissionsManager.PermissionsGroup entry);
			PermissionsManager.Users[player.ID.SteamID] = WithIncludes(entry, new List<string> { role });
			PermissionsManager.SaveUsers();
			BccPermissions.Refresh(true);
			Log.Write($"Better Colony Commands: {player.Name} tests the role {role} for {DURATION_SECONDS}s (own roles: {Describe(rec)})");

			string back = Describe(rec);
			string text = PREFIX + (switching ? "You now test " : $"Your roles were removed for {DURATION_SECONDS / 60} minutes - you now only have the role ")
				+ ChatColors.FormatRole(role) + " to try it out. "
				+ $"Your roles ({back}) come back automatically in {DURATION_SECONDS / 60} minutes, also if you disconnect or the server stops. "
				+ $"End it now: {Style.Cmd("/roletest end")}";
			BccChat.Send(player, Style.Success(text));
			string note = OwnPermissionNote(player);
			if (note != null) {
				BccChat.Send(player, Style.Muted(PREFIX + note));
			}
			return null;
		}

		// note when the test can't show the role alone
		public static string OwnPermissionNote(Players.Player player)
		{
			if (NetworkInfo.IsListenHost(player) || Networking.RCONHandler.CheckAuthenticatedNoKick(player.ID)) {
				return "You are the host / logged in as server admin - the game keeps giving you every permission, so the test shows no difference for you.";
			}
			List<string> own = PermissionsHelper.GetOwnPermissions(player);
			if (own.Contains("")) {
				return "You have every permission as an own permission (not from a role) - it stays active, so the test shows no difference for you.";
			}
			if (own.Count > 0) {
				return $"Your {own.Count} own permission(s) (not from a role) stay active during the test.";
			}
			return null;
		}

		public static bool End(Players.Player player, string reason)
		{
			return player != null && player.ID.HasSteamID && Restore(Key(player.ID.SteamID), reason);
		}

		// a role that is tested or saved in a test is renamed or deleted: end those tests first
		public static void EndTestsInvolving(string role, string reason)
		{
			foreach (KeyValuePair<string, TestRecord> kvp in tests.ToList()) {
				if (string.Equals(kvp.Value.Role, role, StringComparison.OrdinalIgnoreCase)
					|| kvp.Value.Roles.Contains(role, StringComparer.OrdinalIgnoreCase)) {
					Restore(kvp.Key, reason);
				}
			}
		}

		// for BCC's role tools: work on the real roles, not on the tested one
		public static void EndIfActive(Players.Player player, string reason)
		{
			if (IsTesting(player)) {
				End(player, reason);
			}
		}

		static bool Restore(string key, string reason)
		{
			if (!tests.TryGetValue(key, out TestRecord rec) || !ulong.TryParse(key, out ulong steam)) {
				return false;
			}
			if (!PermissionsManager.EnsureLoadedSuccesfully()) {
				return false; // try again on the next tick / start
			}
			CSteamID id = new CSteamID(steam);
			PermissionsManager.Users.TryGetValue(id, out PermissionsManager.PermissionsGroup current);
			List<string> now = current != null ? ReadIncludes(current) : new List<string>();
			// roles someone else gave the player during the test stay
			List<string> added = now.Where(g => !string.Equals(g, rec.Role, StringComparison.OrdinalIgnoreCase)
				&& !rec.Roles.Contains(g, StringComparer.OrdinalIgnoreCase)).ToList();
			if (rec.HadEntry) {
				PermissionsManager.Users[id] = WithIncludes(current, rec.Roles.Concat(added).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
			} else if (added.Count == 0 && (current == null || ReadOwnPermissions(current).Count == 0)) {
				PermissionsManager.Users.Remove(id); // back to the implicit default role, exactly as before
			} else {
				List<string> roles = new List<string> { RoleManager.DefaultRole };
				roles.AddRange(added.Where(g => !string.Equals(g, RoleManager.DefaultRole, StringComparison.OrdinalIgnoreCase)));
				PermissionsManager.Users[id] = WithIncludes(current, roles);
			}
			PermissionsManager.SaveUsers();
			BccPermissions.Refresh(true);
			RoleIndex.Invalidate();
			tests.Remove(key);
			try {
				Save();
			} catch (Exception e) {
				// the roles are back; the stale entry would only restore the same roles again
				Log.WriteException("Better Colony Commands: bcc-roletests.json could not be updated after a role test", e);
			}
			string back = Describe(rec) + (added.Count > 0 ? ", " + string.Join(", ", added) : "");
			Log.Write($"Better Colony Commands: role test of {rec.Name} ({key}) ended ({reason}) - roles restored: {back}");

			Players.Player player = FindConnected(id);
			if (player != null) {
				BccChat.Send(player, Style.Success(PREFIX + $"Test of {ChatColors.FormatRole(rec.Role)} ended ({reason}) - your roles are back: {FormatRoles(rec, added)}"));
			} else {
				restoredWhileAway.Add(key);
			}
			return true;
		}

		static string Describe(TestRecord rec)
		{
			List<string> roles = rec.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			return rec.HadEntry ? (roles.Count == 0 ? "none" : string.Join(", ", roles)) : RoleManager.DefaultRole + " (default)";
		}

		static string FormatRoles(TestRecord rec, List<string> added)
		{
			List<string> roles = rec.HadEntry ? new List<string>(rec.Roles) : new List<string> { RoleManager.DefaultRole };
			roles.AddRange(added);
			roles = roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			return roles.Count == 0 ? "none" : string.Join(", ", roles.Select(ChatColors.FormatRole));
		}

		static Players.Player FindConnected(CSteamID id)
		{
			foreach (Players.Player p in Players.PlayerDatabase.Values) {
				if (p != null && p.ID.HasSteamID && p.ID.SteamID == id && p.ConnectionState == Players.EConnectionState.Connected) {
					return p;
				}
			}
			return null;
		}

		// ------------------------------------------------------------------ game callbacks

		// a crash or kill during a test: give the roles back before anyone joins
		public static void AfterWorldLoad()
		{
			Load();
			foreach (string key in tests.Keys.ToList()) {
				Restore(key, "the server was stopped during the test");
			}
		}

		public static void OnUpdate()
		{
			if (tests.Count == 0 || DateTime.UtcNow < nextTick) {
				return;
			}
			nextTick = DateTime.UtcNow.AddSeconds(1);
			foreach (KeyValuePair<string, TestRecord> kvp in tests.ToList()) {
				TestRecord rec = kvp.Value;
				int left = SecondsLeft(rec);
				if (left <= 0) {
					Restore(kvp.Key, "time is up");
					continue;
				}
				if (!rec.Warned && left <= WARN_SECONDS) {
					rec.Warned = true;
					if (ulong.TryParse(kvp.Key, out ulong steam)) {
						Players.Player p = FindConnected(new CSteamID(steam));
						if (p != null) {
							BccChat.Send(p, Style.Muted(PREFIX + $"{left} seconds left, then your roles come back."));
						}
					}
				}
			}
		}

		public static void OnPlayerDisconnected(Players.Player player)
		{
			End(player, "you left the server");
		}

		public static void OnQuit()
		{
			foreach (string key in tests.Keys.ToList()) {
				Restore(key, "the server stopped");
			}
		}

		public static void OnPlayerConnectedLate(Players.Player player)
		{
			if (player == null || !player.ID.HasSteamID) {
				return;
			}
			string key = Key(player.ID.SteamID);
			if (tests.ContainsKey(key)) {
				Restore(key, "you joined again");
			} else if (restoredWhileAway.Remove(key)) {
				BccChat.Send(player, Style.Success(PREFIX + "Your last role test ended while you were away - your own roles are back."));
			}
		}

		// self test: let the running test run out now
		public static void ExpireForTest(Players.Player player)
		{
			TestRecord rec = Get(player);
			if (rec != null) {
				rec.EndsUtc = DateTime.UtcNow.AddSeconds(-1);
				nextTick = DateTime.MinValue;
				OnUpdate();
			}
		}

		// self test: the player's permission entry as stored by the game
		public static string DescribeEntry(Players.Player player)
		{
			if (!player.ID.HasSteamID || !PermissionsManager.EnsureLoadedSuccesfully()) {
				return "no steam id / permissions not loaded";
			}
			if (!PermissionsManager.Users.TryGetValue(player.ID.SteamID, out PermissionsManager.PermissionsGroup g)) {
				return "no own entry (implicit default role)";
			}
			return $"includes=[{string.Join(",", ReadIncludes(g))}] permissions=[{string.Join(",", ReadOwnPermissions(g))}]";
		}

		public static string FileText()
		{
			return File.Exists(FilePath) ? File.ReadAllText(FilePath).Replace("\r", "").Replace("\n", " ") : "(no file)";
		}

		// ------------------------------------------------------------------ permission entries

		static List<string> ReadIncludes(PermissionsManager.PermissionsGroup g)
		{
			JToken t = JObject.FromObject(g)["includes"];
			List<string> list = t == null || t.Type != JTokenType.Array ? new List<string>() : t.ToObject<List<string>>();
			return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		static List<string> ReadOwnPermissions(PermissionsManager.PermissionsGroup g)
		{
			JToken t = JObject.FromObject(g)["permissions"];
			return t == null || t.Type != JTokenType.Array ? new List<string>() : t.ToObject<List<string>>();
		}

		// copy of the entry (own permissions kept) with other roles; "permissions" must never be
		// null - the game's reader fails on that and would lose every player's permissions
		internal static PermissionsManager.PermissionsGroup WithIncludes(PermissionsManager.PermissionsGroup current, List<string> includes)
		{
			JObject j = current != null ? JObject.FromObject(current) : new JObject();
			j["includes"] = new JArray(includes.Cast<object>().ToArray());
			if (j["permissions"] == null || j["permissions"].Type != JTokenType.Array) {
				j["permissions"] = new JArray();
			}
			return j.ToObject<PermissionsManager.PermissionsGroup>();
		}

		// ------------------------------------------------------------------ file

		static void Load()
		{
			tests = new Dictionary<string, TestRecord>();
			loadFailed = false;
			string path = FilePath;
			List<string> candidates = new List<string> { path, path + ".tmp" };
			if (File.Exists(path)) {
				candidates.Add(path + ".bak");
			}
			bool any = false;
			foreach (string candidate in candidates) {
				if (!File.Exists(candidate)) {
					continue;
				}
				any = true;
				try {
					tests = JsonConvert.DeserializeObject<Dictionary<string, TestRecord>>(File.ReadAllText(candidate)) ?? new Dictionary<string, TestRecord>();
					if (candidate != path) {
						Log.WriteWarning($"Better Colony Commands: {path} was not readable - role tests taken from {Path.GetFileName(candidate)}");
					}
					if (tests.Count > 0) {
						Log.Write($"Better Colony Commands: {tests.Count} unfinished role test(s) found - restoring the roles");
					}
					return;
				} catch (Exception e) {
					Log.WriteException($"Better Colony Commands: could not read {candidate}", e);
				}
			}
			// files there, none readable: never overwrite them
			loadFailed = any;
			if (loadFailed) {
				Log.WriteError($"Better Colony Commands: {path} is damaged - role tests are blocked until it is fixed or removed");
			}
		}

		static void Save()
		{
			string path = FilePath;
			string tmp = path + ".tmp";
			File.WriteAllText(tmp, JsonConvert.SerializeObject(tests, Formatting.Indented));
			if (File.Exists(path)) {
				File.Copy(path, path + ".bak", true);
			}
			File.Copy(tmp, path, true);
			File.Delete(tmp);
		}
	}
}
