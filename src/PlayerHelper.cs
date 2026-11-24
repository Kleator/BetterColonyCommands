using System;
using System.Collections.Generic;
using System.IO;
using Pipliz;
using System.Text.RegularExpressions;
using Steamworks;

namespace ColonyCommands
{

	public static class PlayerHelper
	{

		public static bool TryGetPlayer(string identifier, out Players.Player targetPlayer, out string error, bool includeOffline = false)
		{
			targetPlayer = null;
			if (identifier.StartsWith("'")) {
				if (identifier.EndsWith("'")) {
					identifier = identifier.Substring(1, identifier.Length - 2);
				} else {
					error = "missing ' after playername";
					return false;
				}
			}
			if (identifier.Length < 1) {
				error = "no playername given";
				return false;
			}

			// try to find by steamID first
			ulong steamid;
			if (ulong.TryParse(identifier, out steamid)) {
				Steamworks.CSteamID csteamid = new Steamworks.CSteamID(steamid);
				if (csteamid.IsValid()) {
					error = "";
					if (Players.TryGetPlayer(csteamid, out targetPlayer)) {
						return true;
					} else {
						targetPlayer = null;
					}
				}
			}

			// try to find by hash code
			var m = Regex.Match(identifier, @"#(?<hash>\d{8})");
			int givenHash;
			if (m.Success && int.TryParse(m.Groups["hash"].Value, out givenHash)) {
				foreach (Players.Player player in Players.PlayerDatabase.Values) {
					if (!includeOffline && player.ConnectionState != Players.EConnectionState.Connected) {
						continue;
					}
					if (player.ID.SteamID.GetHashCode() == givenHash) {
						if (targetPlayer == null) {
							targetPlayer = player;
						} else {
							targetPlayer = null;
							error = "duplicate hash code, please use full SteamID";
							return false;
						}
					}
				}

				if (targetPlayer != null) {
					error = "";
					return true;
				}
			}

			// try to find by string closest match
			Players.Player closestMatch = null;
			int closestDist = int.MaxValue;
			foreach (var player in Players.PlayerDatabase.Values) {
				if (!includeOffline && player.ConnectionState != Players.EConnectionState.Connected) {
					continue;
				}
				if (player.Name != null) {
					if (string.Equals(player.Name, identifier, StringComparison.InvariantCultureIgnoreCase)) {
						if (targetPlayer == null) {
							targetPlayer = player;
						} else {
							targetPlayer = null;
							error = "duplicate player name, pls use SteamID";
							return false;
						}
					} else {
						int levDist = LevenshteinDistance.Compute(player.Name.ToLower(), identifier.ToLower());
						if (levDist < closestDist) {
							closestDist = levDist;
							closestMatch = player;
						} else if (levDist == closestDist) {
							closestMatch = null;
						}
					}
				}
			}

			if (targetPlayer != null) {
				error = "";
				return true;
			}

			// try again ignoring whitespace - handles extra/missing spaces, and lets splitscreen
			// profile names ("Kleator 1") be targeted by typing them as one word ("Kleator1"),
			// since the chat command parser itself splits unquoted arguments on whitespace and
			// would otherwise truncate the name at the first space
			string identifierNoSpaces = RemoveWhitespace(identifier);
			Players.Player spacelessMatch = null;
			foreach (var player in Players.PlayerDatabase.Values) {
				if (!includeOffline && player.ConnectionState != Players.EConnectionState.Connected) {
					continue;
				}
				if (player.Name != null && string.Equals(RemoveWhitespace(player.Name), identifierNoSpaces, StringComparison.InvariantCultureIgnoreCase)) {
					if (spacelessMatch == null) {
						spacelessMatch = player;
					} else {
						error = "duplicate player name, pls use SteamID";
						return false;
					}
				}
			}
			if (spacelessMatch != null) {
				error = "";
				targetPlayer = spacelessMatch;
				return true;
			}

			if (closestMatch != null && (closestDist < closestMatch.Name.Length * 0.2)) {
				error = "";
				targetPlayer = closestMatch;
				Log.Write($"Name '{identifier}' did not match, picked closest match '{targetPlayer.Name}' instead");
				return true;
			}
			error = "player not found";
			return false;
		}


		static string RemoveWhitespace(string s)
		{
			System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
			foreach (char c in s) {
				if (!char.IsWhiteSpace(c)) {
					sb.Append(c);
				}
			}
			return sb.ToString();
		}

		// try find colony by name match
		public static bool TryGetColony(string name, out Colony colony, out string error)
		{
			error = "";
			colony = null;
			if (name.StartsWith("'") || name.StartsWith("\"")) {
				name = name.Substring(1, name.Length - 1);
			}
			if (name.EndsWith("'") || name.EndsWith("\"")) {
				name = name.Substring(0, name.Length - 1);
			}

			// Colony/banner names may contain rich text and decorative symbols, e.g.
			// "<b><size=25><color=#EB34C9>✮Highrise-</color><color=#FF4FB9>City✮3</color></size></b>".
			// Matching order: exact raw text, visible text (tags removed), normalized (only letters and
			// digits, so "Highrise-City3" / "highrise city 3" match), then closest normalized match.
			List<Colony> all = new List<Colony>();
			Pipliz.Collections.Hashmap<ColonyID, Colony>.ValueEnumerator colonyEnum = ServerManager.ColonyTracker.ColoniesByID.GetValueEnumerator();
			while (colonyEnum.MoveNext()) {
				if (colonyEnum.Current != null && !string.IsNullOrEmpty(colonyEnum.Current.Name)) {
					all.Add(colonyEnum.Current);
				}
			}

			foreach (Colony checkColony in all) {
				if (string.Equals(name, checkColony.Name, StringComparison.InvariantCultureIgnoreCase)) {
					colony = checkColony;
					return true;
				}
			}

			string plainWanted = TextHelper.PlainLower(name);
			foreach (Colony checkColony in all) {
				if (plainWanted.Length > 0 && plainWanted == TextHelper.PlainLower(checkColony.Name)) {
					colony = checkColony;
					return true;
				}
			}

			string normWanted = TextHelper.NormalizeName(name);
			if (normWanted.Length > 0) {
				Colony normMatch = null;
				int normMatches = 0;
				foreach (Colony checkColony in all) {
					if (normWanted == TextHelper.NormalizeName(checkColony.Name)) {
						normMatch = checkColony;
						normMatches++;
					}
				}
				if (normMatches == 1) {
					colony = normMatch;
					return true;
				}
				if (normMatches > 1) {
					error = "several colonies match that name, please be more specific";
					return false;
				}
			}

			// try to find by string closest match (on the normalized names)
			Colony closestMatch = null;
			int closestDist = int.MaxValue;
			foreach (Colony checkColony in all) {
				string norm = TextHelper.NormalizeName(checkColony.Name);
				if (norm.Length == 0) {
					continue;
				}
				int levDist = LevenshteinDistance.Compute(normWanted, norm);
				if (levDist < closestDist) {
					closestDist = levDist;
					closestMatch = checkColony;
				} else if (levDist == closestDist) {
					closestMatch = null;
				}
			}
			if (closestMatch != null && closestDist <= TextHelper.NormalizeName(closestMatch.Name).Length * 0.3) {
				colony = closestMatch;
				return true;
			}

			error = "colony not found";
			return false;
		}
	}

	// src: https://www.dotnetperls.com/levenshtein
	static class LevenshteinDistance
	{
		public static int Compute(string s, string t)
		{
			var n = s.Length;
			var m = t.Length;
			int[,] d = new int[n + 1, m + 1];
			if (n == 0) {
				return m;
			}
			if (m == 0) {
				return n;
			}
			for (var i = 0; i <= n; d[i, 0] = i++) {
			}
			for (var j = 0; j <= m; d[0, j] = j++) {
			}
			for (var i = 1; i <= n; i++) {
				for (var j = 1; j <= m; j++) {
					var cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
					d[i, j] = System.Math.Min(
							System.Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
							d[i - 1, j - 1] + cost);
				}
			}
			return d[n, m];
		}
	}
}

