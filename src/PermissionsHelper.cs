using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ColonyCommands
{
	// PermissionsManager.PermissionsGroup keeps its assigned group names ("ParentGroups") in a
	// private field with no public getter, and PermissionsManager.Users[steamId] is always an
	// independent copy - never the same object reference as anything in PermissionsManager.Groups.
	// So there is no public API to ask "which group(s) is this player actually in". ParentGroups is
	// marked [JsonProperty("includes")] though - the same mechanism PermissionsManager.SaveUsers()
	// itself relies on to persist it - so round-tripping the object through JSON reads it out reliably
	// without reflection.
	public static class PermissionsHelper
	{
		// every player whose own permission entry includes this group (players without an entry
		// implicitly have the default group and are not listed)
		public static List<Players.Player> GetPlayersWithGroup(string group)
		{
			List<Players.Player> result = new List<Players.Player>();
			if (!PermissionsManager.EnsureLoadedSuccesfully() || PermissionsManager.Users == null) {
				return result;
			}
			foreach (KeyValuePair<Steamworks.CSteamID, PermissionsManager.PermissionsGroup> kvp in PermissionsManager.Users) {
				JObject serialized = JObject.FromObject(kvp.Value);
				List<string> includes = serialized["includes"]?.ToObject<List<string>>();
				if (includes == null || !includes.Exists(g => string.Equals(g, group, System.StringComparison.OrdinalIgnoreCase))) {
					continue;
				}
				if (Players.PlayerDatabaseBySteamID.TryGetValue(kvp.Key, out Players.Player p)) {
					result.Add(p);
				}
			}
			result.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
			return result;
		}

		// number of permission entries (steam accounts) that include the group, from the game's live data
		public static int CountUsersWithGroup(string group)
		{
			if (!PermissionsManager.EnsureLoadedSuccesfully() || PermissionsManager.Users == null) {
				return 0;
			}
			int n = 0;
			foreach (PermissionsManager.PermissionsGroup g in PermissionsManager.Users.Values) {
				List<string> includes = JObject.FromObject(g)["includes"]?.ToObject<List<string>>();
				if (includes != null && includes.Exists(x => string.Equals(x, group, System.StringComparison.OrdinalIgnoreCase))) {
					n++;
				}
			}
			return n;
		}

		public static bool HasOwnEntry(Players.Player player)
		{
			return player.ID.HasSteamID && PermissionsManager.EnsureLoadedSuccesfully() && PermissionsManager.Users.ContainsKey(player.ID.SteamID);
		}

		// the player's own permissions (not inherited from groups)
		public static List<string> GetOwnPermissions(Players.Player player)
		{
			if (!HasOwnEntry(player) || !PermissionsManager.Users.TryGetValue(player.ID.SteamID, out PermissionsManager.PermissionsGroup group)) {
				return new List<string>();
			}
			JObject serialized = JObject.FromObject(group);
			return serialized["permissions"]?.ToObject<List<string>>() ?? new List<string>();
		}

		public static List<string> GetGroupNames(Players.Player player)
		{
			if (!player.ID.HasSteamID || !PermissionsManager.EnsureLoadedSuccesfully()) {
				return new List<string>();
			}
			if (!PermissionsManager.Users.TryGetValue(player.ID.SteamID, out PermissionsManager.PermissionsGroup group)) {
				// no explicit entry: player is on the implicit default group, which has no public name to restore to
				return new List<string>();
			}

			JObject serialized = JObject.FromObject(group);
			List<string> includes = serialized["includes"]?.ToObject<List<string>>();
			// a role can be in the entry twice (the game's add doesn't check) - count it once
			return System.Linq.Enumerable.ToList(System.Linq.Enumerable.Distinct(includes ?? new List<string>(), System.StringComparer.OrdinalIgnoreCase));
		}
	}
}
