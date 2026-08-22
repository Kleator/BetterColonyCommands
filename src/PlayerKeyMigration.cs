using System.Collections.Generic;
using Pipliz;
using Steamworks;

namespace ColonyCommands
{
	// Game version 0.18.0.0 added a "profile" field to player IDs (for splitscreen support),
	// which changed what Player.ID.ToStringReadable() returns for every player - it went from
	// "<id> (<steamid>)" to "<id> (<steamid>, profile <n>)". playeractivity.json is keyed by
	// that exact string, so an existing player's entry silently became unreachable the first
	// time they reconnected under the new format - indistinguishable from their lastSeen/
	// secondsPlayed having reset.
	public static class PlayerKeyMigration
	{
		// Migrates every legacy-format key in dict to whatever key that same player's
		// Player.ID.ToStringReadable() actually returns right now - resolved via the SteamID
		// embedded in the key against the persistent player database, not by guessing the new
		// format, so this keeps working even if a future update changes the format again.
		// Players this server has never seen (no persistent record to resolve a SteamID against)
		// are left untouched rather than lost - they simply migrate the next time this runs after
		// they've connected at least once.
		public static int MigrateLegacyKeys<T>(Dictionary<string, T> dict, string label)
		{
			List<string> legacyKeys = null;
			foreach (string key in dict.Keys) {
				if (IsLegacyKey(key)) {
					(legacyKeys ?? (legacyKeys = new List<string>())).Add(key);
				}
			}

			if (legacyKeys == null) {
				return 0;
			}

			int migrated = 0;
			foreach (string legacyKey in legacyKeys) {
				if (!TryExtractSteamID(legacyKey, out ulong steamId) || !Players.TryGetPlayer(new CSteamID(steamId), out Players.Player player)) {
					continue;
				}

				string currentKey = player.ID.ToStringReadable();
				if (currentKey == legacyKey) {
					continue;
				}

				T value = dict[legacyKey];
				dict.Remove(legacyKey);

				// A colliding current-format entry (created the moment this player first
				// reconnected under the new format, before this migration existed) is always the
				// fresher, less complete one - a just-started stats entry, not real accumulated
				// data - so keeping the legacy entry here is the correct direction.
				if (dict.ContainsKey(currentKey)) {
					Log.WriteWarning($"{label}: both '{legacyKey}' and '{currentKey}' existed - kept the legacy entry's value.");
				}

				dict[currentKey] = value;
				migrated++;
			}

			if (migrated > 0) {
				Log.Write($"{label}: migrated {migrated} legacy player key(s) to the current Player.ID format (game version 0.18.0.0+ added a profile suffix).");
			}
			return migrated;
		}

		// Legacy keys ("<id> (<steamid>)") have no comma inside the parentheses; current keys
		// ("<id> (<steamid>, profile <n>)") do.
		static bool IsLegacyKey(string key)
		{
			int openParen = key.IndexOf('(');
			int closeParen = key.LastIndexOf(')');
			if (openParen < 0 || closeParen < openParen) {
				return false;
			}

			return key.IndexOf(',', openParen, closeParen - openParen) < 0;
		}

		static bool TryExtractSteamID(string key, out ulong steamId)
		{
			steamId = 0;
			int openParen = key.IndexOf('(');
			int closeParen = key.IndexOf(')', openParen + 1);
			if (openParen < 0 || closeParen < 0) {
				return false;
			}

			string inner = key.Substring(openParen + 1, closeParen - openParen - 1).Trim();
			return ulong.TryParse(inner, out steamId);
		}
	}
}
