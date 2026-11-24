using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Pipliz;
using Steamworks;

namespace ColonyCommands
{
	// gamedata/savegames/<world>/bcc-moderation-log.json - everything that happened to a player
	// that isn't already recorded elsewhere: kicks, mutes, unmutes, jail releases, unbans, roleplay
	// bans, blocked colony names, killed colonists, admin changes from the player window and notes.
	// Bans (banned-players-log.json) and jail sentences (jail log) keep their own files; the player
	// history merges all sources.
	public static class ModerationLog
	{
		public const string FILE_NAME = "bcc-moderation-log.json";
		const int MAX_EVENTS = 5000;

		public class ModerationEvent
		{
			public DateTime Time;
			public ulong SteamId;
			public string PlayerKey = "";
			public string PlayerName = "";
			public string Type = "";
			public string By = "";
			public string Details = "";
			// true = counts as an incident (rule violation, punishment); false = admin change or info
			public bool Offense = true;
			// warnings: shown to the player as popup until confirmed with OK
			public bool PendingPopup;
		}

		public class HistoryEntry
		{
			public DateTime Time;
			public string Type;
			public string By;
			public string Details;
			public bool Offense;
		}

		static List<ModerationEvent> events = new List<ModerationEvent>();
		static bool loadFailed;
		static string FilePath => Path.Combine("gamedata", "savegames", ServerManager.WorldName, FILE_NAME);

		public static void Load()
		{
			loadFailed = false;
			events = new List<ModerationEvent>();
			if (!File.Exists(FilePath)) {
				return;
			}
			try {
				events = JsonConvert.DeserializeObject<List<ModerationEvent>>(File.ReadAllText(FilePath)) ?? new List<ModerationEvent>();
			} catch (Exception e) {
				loadFailed = true;
				Log.WriteError($"Better Colony Commands: could not read {FILE_NAME} - left untouched, not saving this session: {e.Message}");
			}
		}

		static void Save()
		{
			if (loadFailed) {
				return;
			}
			try {
				while (events.Count > MAX_EVENTS) {
					events.RemoveAt(0);
				}
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				File.WriteAllText(FilePath, JsonConvert.SerializeObject(events, Formatting.Indented));
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: could not save {FILE_NAME}: {e.Message}");
			}
		}

		public static void Add(Players.Player target, string type, Players.Player by, string details, bool offense = true, bool pendingPopup = false)
		{
			if (target == null) {
				return;
			}
			try {
				events.Add(new ModerationEvent {
					PendingPopup = pendingPopup,
					Time = DateTime.Now,
					SteamId = target.ID.HasSteamID ? target.ID.SteamID.m_SteamID : 0,
					PlayerKey = target.ID.ToStringReadable(),
					PlayerName = target.Name ?? "",
					Type = type,
					By = by == null ? "Server" : by.Name,
					Details = details ?? "",
					Offense = offense
				});
				Save();
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not record moderation event: {e.Message}");
			}
		}

		public static List<ModerationEvent> PendingWarnings(Players.Player p)
		{
			return events.Where(e => e.PendingPopup && Matches(e, p)).ToList();
		}

		public static int AcknowledgeWarnings(Players.Player p)
		{
			int n = 0;
			foreach (ModerationEvent e in events.Where(e => e.PendingPopup && Matches(e, p))) {
				e.PendingPopup = false;
				n++;
			}
			if (n > 0) {
				Save();
			}
			return n;
		}

		static bool Matches(ModerationEvent e, Players.Player p)
		{
			return e.PlayerKey == p.ID.ToStringReadable() || (p.ID.HasSteamID && e.SteamId != 0 && e.SteamId == p.ID.SteamID.m_SteamID && string.IsNullOrEmpty(e.PlayerKey));
		}

		static string NameOfSteamId(ulong id)
		{
			if (id == 0) {
				return "Server";
			}
			if (Players.PlayerDatabaseBySteamID.TryGetValue(new CSteamID(id), out Players.Player p)) {
				return p.Name;
			}
			return id.ToString();
		}

		static long NowSeconds => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000;

		static DateTime FromSeconds(long s)
		{
			return new DateTime(s * TimeSpan.TicksPerSecond);
		}

		// ------------------------------------------------------------------ status

		public static BanLogRecord ActiveBan(Players.Player p)
		{
			if (!p.ID.HasSteamID) {
				return null;
			}
			ulong id = p.ID.SteamID.m_SteamID;
			long now = NowSeconds;
			return BannedPlayerManager.banLogs.LastOrDefault(r => r.target == id && !r.unbanned && r.timestamp + (long)r.duration * 86400 > now);
		}

		public static bool IsBanned(Players.Player p)
		{
			if (!p.ID.HasSteamID) {
				return false;
			}
			return ActiveBan(p) != null || BlackAndWhitelisting.Blacklisted.Contains(p.ID.SteamID.m_SteamID);
		}

		public static bool IsMuted(Players.Player p)
		{
			MuteList.Update();
			return MuteList.MutedMinutes.ContainsKey(p.ID.ID);
		}

		// incidents per steam id / player key, for the player list
		public static Dictionary<string, int> IncidentCounts()
		{
			Dictionary<string, int> counts = new Dictionary<string, int>();
			void Inc(string key)
			{
				counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
			}
			foreach (BanLogRecord r in BannedPlayerManager.banLogs) {
				Inc("s" + r.target);
			}
			foreach (KeyValuePair<ulong, List<JailManager.JailLogRecord>> kvp in JailManager.jailLog) {
				for (int i = 0; i < kvp.Value.Count; i++) {
					Inc("s" + kvp.Key);
				}
			}
			foreach (ModerationEvent e in events) {
				if (e.Offense) {
					Inc(string.IsNullOrEmpty(e.PlayerKey) ? "s" + e.SteamId : "k" + e.PlayerKey);
				}
			}
			return counts;
		}

		public static int IncidentCount(Dictionary<string, int> counts, Players.Player p)
		{
			int n = 0;
			if (p.ID.HasSteamID && counts.TryGetValue("s" + p.ID.SteamID.m_SteamID, out int a)) {
				n += a;
			}
			if (counts.TryGetValue("k" + p.ID.ToStringReadable(), out int b)) {
				n += b;
			}
			return n;
		}

		// ------------------------------------------------------------------ history

		public static List<HistoryEntry> History(Players.Player p)
		{
			List<HistoryEntry> list = new List<HistoryEntry>();
			ulong id = p.ID.HasSteamID ? p.ID.SteamID.m_SteamID : 0;
			long now = NowSeconds;
			foreach (BanLogRecord r in BannedPlayerManager.banLogs.Where(r => r.target == id && id != 0)) {
				bool active = !r.unbanned && r.timestamp + (long)r.duration * 86400 > now;
				list.Add(new HistoryEntry {
					Time = FromSeconds(r.timestamp), Type = "Ban", By = NameOfSteamId(r.causedBy == r.target ? 0 : r.causedBy), Offense = true,
					Details = $"{r.duration} day(s): {r.reason}" + (active ? " (active)" : r.unbanned ? " (lifted)" : " (expired)")
				});
			}
			if (id != 0 && JailManager.jailLog.TryGetValue(id, out List<JailManager.JailLogRecord> jail)) {
				foreach (JailManager.JailLogRecord r in jail) {
					list.Add(new HistoryEntry {
						Time = FromSeconds(r.timestamp), Type = "Jail", By = NameOfSteamId(r.jailedBy), Offense = true,
						Details = $"{TextHelper.FormatDuration(r.duration)}: {r.reason}"
					});
				}
			}
			foreach (ModerationEvent e in events.Where(e => Matches(e, p))) {
				list.Add(new HistoryEntry { Time = e.Time, Type = e.Type, By = e.By, Details = e.Details, Offense = e.Offense });
			}
			return list.OrderByDescending(h => h.Time).ToList();
		}
	}
}
