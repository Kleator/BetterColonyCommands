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
			return includes ?? new List<string>();
		}
	}
}
