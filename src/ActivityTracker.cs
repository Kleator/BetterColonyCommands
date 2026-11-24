using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Pipliz;
using Newtonsoft.Json;

namespace ColonyCommands
{
	public static class ActivityTracker
	{
		const string CONFIG_FILE = "playeractivity.json";
		static string ConfigFilePath {
			get {
				return Path.Combine(Path.Combine("gamedata", "savegames"), Path.Combine(ServerManager.WorldName, CONFIG_FILE));
			}
		}
		static Dictionary<string, StatsDataEntry> PlayerStats = new Dictionary<string, StatsDataEntry>();

		// Generous upper bound for a single tracked gap (autosave-to-autosave or connect-to-disconnect).
		// Real gaps are always far shorter than this (autosave intervals are minutes, not hours, and a
		// human play session rarely exceeds a day). A larger computed gap means lastSeen was stale -
		// e.g. from a much earlier session - not real playtime, so it's discarded instead of corrupting
		// secondsPlayed. This is what previously let disconnected/reconnecting players rack up
		// implausible totals (hundreds/thousands of hours): a player is added to Players.ConnectedPlayers
		// (and so picked up by OnAutoSaveWorld, or by OnPlayerDisconnected if the connection drops before
		// finishing) at OnPlayerConnectedEarly time, but lastSeen only used to get reset at
		// OnPlayerConnectedLate - which can fire noticeably later (client still loading) or, if the
		// connection never completes, not at all. Any autosave or disconnect landing in that gap computed
		// "now minus their last session's lastSeen", which could be days or weeks old.
		const double MAX_TRACKED_GAP_SECONDS = 24.0 * 60.0 * 60.0;


		public static void OnPlayerConnectedEarly(Players.Player player)
		{
			ResetLastSeen(player);
		}


		public static void OnPlayerConnectedLate(Players.Player player)
		{
			ResetLastSeen(player);
		}


		static void ResetLastSeen(Players.Player player)
		{
			var stats = GetOrCreateStats(player.ID.ToStringReadable());
			stats.lastSeen = DateTime.Now.ToString();
		}


		public static void OnPlayerDisconnected(Players.Player player)
		{
			var stats = GetOrCreateStats(player.ID.ToStringReadable());
			AddElapsedSeconds(stats, DateTime.Now);
		}


		public static void OnAutoSaveWorld()
		{
			var now = DateTime.Now;
			for (var c = 0; c < Players.ConnectedPlayers.Count; c++) {
				Players.Player player = Players.ConnectedPlayers[c];
				var stats = GetOrCreateStats(player.ID.ToStringReadable());
				AddElapsedSeconds(stats, now);
			}
			Save();
		}


		static void AddElapsedSeconds(StatsDataEntry stats, DateTime now)
		{
			DateTime lastSeen;
			if (DateTime.TryParse(stats.lastSeen, out lastSeen)) {
				double elapsedSeconds = now.Subtract(lastSeen).TotalSeconds;
				if (elapsedSeconds > 0 && elapsedSeconds <= MAX_TRACKED_GAP_SECONDS) {
					stats.secondsPlayed += (long)elapsedSeconds;
				} else if (elapsedSeconds > MAX_TRACKED_GAP_SECONDS) {
					Log.WriteWarning($"ActivityTracker: discarded a {elapsedSeconds / 3600.0:F1}h gap (stale lastSeen) instead of adding it to playtime");
				}
			}
			stats.lastSeen = now.ToString();
		}


		public static void OnQuit()
		{
			Save();
		}


		// total online time including the not yet booked time of the running session
		public static long GetLiveSecondsPlayed(Players.Player player)
		{
			StatsDataEntry stats;
			if (!PlayerStats.TryGetValue(player.ID.ToStringReadable(), out stats)) {
				return 0;
			}
			long total = stats.secondsPlayed;
			if (player.ConnectionState == Players.EConnectionState.Connected && DateTime.TryParse(stats.lastSeen, out DateTime lastSeen)) {
				double running = DateTime.Now.Subtract(lastSeen).TotalSeconds;
				if (running > 0 && running <= MAX_TRACKED_GAP_SECONDS) {
					total += (long)running;
				}
			}
			return total;
		}


		public static void Load()
		{
			if (File.Exists(ConfigFilePath)) {
				try {
					Log.Write("Loading player activity from {0}", CONFIG_FILE);
					JsonSerializer js = new JsonSerializer();
					JsonTextReader jtr = new JsonTextReader(new StreamReader(ConfigFilePath));
					PlayerStats = js.Deserialize<Dictionary<string, StatsDataEntry>>(jtr);
					jtr.Close();
					if (PlayerStats == null) {
						PlayerStats = new Dictionary<string, StatsDataEntry>();
					}
					if (PlayerKeyMigration.MigrateLegacyKeys(PlayerStats, CONFIG_FILE) > 0) {
						Save();
					}
				} catch (Exception e) {
					Log.Write("Error parsing {0}: {1}", CONFIG_FILE, e.Message);
				}
			}
		}


		public static void Save()
		{
			if (PlayerStats.Count == 0) return;

			Log.Write("Saving player activity to {0}", CONFIG_FILE);
			try {
				JsonSerializer json = new JsonSerializer();
				JsonTextWriter jsonWriter = new JsonTextWriter(new StreamWriter(ConfigFilePath));
				json.Serialize(jsonWriter, PlayerStats);
				jsonWriter.Close();
			} catch (Exception e) {
				Log.Write("Could not save {0}: {1}", CONFIG_FILE, e.Message);
			}
		}


		public static StatsDataEntry GetOrCreateStats(string playerId)
		{
			StatsDataEntry stats;
			if (!PlayerStats.TryGetValue(playerId, out stats)) {
				stats = new StatsDataEntry();
				PlayerStats.Add(playerId, stats);
			}
			return stats;
		}


		public static string GetLastSeen(string playerId)
		{
			StatsDataEntry stats;
			if (!PlayerStats.TryGetValue(playerId, out stats)) {
				return "never";
			}
			return stats.lastSeen;
		}


		public static Dictionary<Players.Player, int> GetInactivePlayers(int days)
		{
			Dictionary<Players.Player, int> result = new Dictionary<Players.Player, int>();
			foreach (Players.Player player in Players.PlayerDatabase.Values) {
				if (Players.IsServerID(player.ID)) {
					continue;
				}
				StatsDataEntry stats = GetOrCreateStats(player.ID.ToStringReadable());
				DateTime lastSeen = DateTime.Now;
				try {
					lastSeen = DateTime.Parse(stats.lastSeen);
				} catch (Exception exception) {
					Log.WriteError($"Unable to parse lastSeen '{stats.lastSeen}': {exception.Message}");
				}
				double inactiveDays = DateTime.Now.Subtract(lastSeen).TotalDays;
				if (inactiveDays >= days) {
					result.Add(player, (int)inactiveDays);
				}
			}
			return result;
		}


		public class StatsDataEntry
		{
			public string lastSeen = "";
			public long secondsPlayed;

			public StatsDataEntry()
				: this(DateTime.Now.ToString(), 0)
			{
			}

			public StatsDataEntry(String lastSeen, long secondsPlayed)
			{
				this.lastSeen = lastSeen;
				this.secondsPlayed = secondsPlayed;
			}

		}
	}
}
