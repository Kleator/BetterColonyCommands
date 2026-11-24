using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Pipliz;
using Chatting;

namespace ColonyCommands
{
	// Colony renames apply directly server-side (ClientCommands.SetColonyName) with no moddable
	// hook to intercept - so blacklist enforcement is a periodic detect-and-revert scan rather
	// than a hard pre-block, both for renames and for newly placed banners.
	public static class ColonyNameGuard
	{
		public const int MaxNameLength = 50;
		const double SCAN_INTERVAL_SECONDS = 10.0;

		const string CONFIG_FILE = "blacklistedwords.json";
		static string ConfigFilePath {
			get {
				return Path.Combine(Path.Combine("gamedata", "savegames"), Path.Combine(ServerManager.WorldName, CONFIG_FILE));
			}
		}

		// each entry is a regex pattern tested against the normalized (lowercased, diacritic- and
		// punctuation-stripped) colony name - a plain word is already a valid regex matching that
		// literal substring, so existing plain-word blacklists keep working unchanged
		static List<Regex> blacklistPatterns = new List<Regex>();
		static Dictionary<ColonyID, string> knownGoodNames = new Dictionary<ColonyID, string>();


		public static void Load()
		{
			LoadBlacklist();
			ScanLoop();
		}


		// re-reads blacklistedwords.json without restarting the scan loop - safe to call any
		// time (e.g. /bcreload)
		public static void ReloadConfig()
		{
			LoadBlacklist();
		}


		static void LoadBlacklist()
		{
			blacklistPatterns = new List<Regex>();
			if (!File.Exists(ConfigFilePath)) {
				Log.Write($"{CONFIG_FILE} not found, colony name blacklist disabled");
				return;
			}
			try {
				List<string> patterns = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(ConfigFilePath)) ?? new List<string>();
				foreach (string pattern in patterns) {
					if (string.IsNullOrWhiteSpace(pattern)) {
						continue;
					}
					try {
						blacklistPatterns.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
					} catch (ArgumentException exception) {
						Log.WriteError($"{CONFIG_FILE}: skipping invalid regex '{pattern}': {exception.Message}");
					}
				}
			} catch (Exception exception) {
				Log.WriteError($"Exception while loading {CONFIG_FILE}: {exception.Message}");
			}
		}


		static void ScanLoop()
		{
			ScanColonies();
			ThreadManager.InvokeOnMainThread(delegate() {
				ScanLoop();
			}, SCAN_INTERVAL_SECONDS);
		}


		static void ScanColonies()
		{
			if (blacklistPatterns.Count == 0) {
				return;
			}
			Pipliz.Collections.Hashmap<ColonyGroupID, ColonyGroup>.ValueEnumerator colonyGroupEnum = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
			while (colonyGroupEnum.MoveNext()) {
				ColonyGroup checkColonyGroup = colonyGroupEnum.Current;
				for (int i = 0; i < checkColonyGroup.Colonies.Count; ++i) {
					CheckColony(checkColonyGroup.Colonies.GetAt(i));
				}
			}
		}


		static void CheckColony(Colony colony)
		{
			if (colony == null || string.IsNullOrEmpty(colony.Name)) {
				return;
			}
			string currentName = colony.Name;
			bool blacklisted = TryGetBlacklistMatch(currentName, out string matchedPattern);

			if (blacklisted) {
				bool hasKnownGood = knownGoodNames.TryGetValue(colony.ColonyID, out string lastGood);
				if (hasKnownGood && lastGood != currentName) {
					// an actual rename (by a player) to something blacklisted - revert to their prior good name
					RevertToPrevious(colony, currentName, lastGood, matchedPattern);
				} else {
					// either a brand new colony, or the name has been sitting blacklisted unchanged
					// (e.g. this rule was just added and reloaded via /bcreload, and there's no
					// different prior name to fall back to) - use the default name instead
					RevertToDefault(colony, currentName, matchedPattern);
				}
				return;
			}

			knownGoodNames[colony.ColonyID] = currentName;
		}


		static void RevertToDefault(Colony colony, string rejectedName, string matchedPattern)
		{
			string ownerName = colony.ColonyGroup.Owners.Count > 0 ? colony.ColonyGroup.Owners.GetAt(0).Name : "Unknown";
			ApplyName(colony, $"{ownerName}'s Colony", rejectedName, matchedPattern);
		}


		static void RevertToPrevious(Colony colony, string rejectedName, string previousName, string matchedPattern)
		{
			ApplyName(colony, previousName, rejectedName, matchedPattern);
		}


		static void ApplyName(Colony colony, string newName, string rejectedName, string matchedPattern)
		{
			// the replacement itself can still be blacklisted - e.g. the default "{OwnerName}'s
			// Colony" fallback embeds the owner's own player name, which might itself contain a
			// blacklisted word. Registering that as known-good would silently and permanently
			// leave a blacklisted name in place (the next scan only re-checks on a *change*), so
			// fall back to a name built purely from the colony ID, which can never match a
			// word-based rule.
			if (TryGetBlacklistMatch(newName, out string fallbackMatch)) {
				Log.WriteWarning($"ColonyNameGuard: replacement name '{newName}' for colony {colony.ColonyID} also matched {CONFIG_FILE} rule '{fallbackMatch}' (likely from the owner's own player name) - using a generic name instead");
				newName = $"Colony {colony.ColonyID}";
			}

			colony.Name = newName;
			ColonyGroup colonyGroup = colony.ColonyGroup;
			for (int i = 0; i < colonyGroup.Owners.Count; i++) {
				Players.Player owner = colonyGroup.Owners.GetAt(i);
				colony.SendNameUpdate(owner);
				if (i == 0) {
					BccChat.Send(owner, $"<color=red>That colony name isn't allowed, reset to '{newName}'. Trying to get around this with special characters risks getting the colony purged.</color>");
				}
			}
			knownGoodNames[colony.ColonyID] = newName;
			if (colonyGroup.Owners.Count > 0) {
				ModerationLog.Add(colonyGroup.Owners.GetAt(0), "Colony name blocked", null, $"'{TextHelper.StripRichText(rejectedName)}' matched blacklist rule '{matchedPattern}', reset to '{newName}'");
			}
			Log.WriteWarning($"ColonyNameGuard: colony name '{rejectedName}' matched {CONFIG_FILE} rule '{matchedPattern}', reset to '{newName}'");
		}


		// lets other commands (e.g. admin /setcolonyname) register a deliberate rename as known-good
		// so the next scan doesn't treat it as a rogue rename
		public static void RegisterKnownGoodName(ColonyID colonyID, string name)
		{
			knownGoodNames[colonyID] = name;
		}


		static bool TryGetBlacklistMatch(string name, out string matchedPattern)
		{
			matchedPattern = null;
			string normalizedName = Normalize(name);
			if (normalizedName.Length == 0) {
				return false;
			}
			foreach (Regex pattern in blacklistPatterns) {
				if (pattern.IsMatch(normalizedName)) {
					matchedPattern = pattern.ToString();
					return true;
				}
			}
			return false;
		}


		// lowercase, strips diacritics and non-alphanumerics before matching - catches basic
		// evasion (spacing, punctuation, accents) without a full profanity filter
		static string Normalize(string input)
		{
			if (string.IsNullOrEmpty(input)) {
				return "";
			}
			string decomposed = input.Normalize(NormalizationForm.FormD);
			StringBuilder sb = new StringBuilder();
			foreach (char c in decomposed) {
				if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) {
					continue;
				}
				if (char.IsLetterOrDigit(c)) {
					sb.Append(char.ToLowerInvariant(c));
				}
			}
			return sb.ToString();
		}
	}
}
