using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Pipliz;

namespace ColonyCommands
{
	// gamedata/savegames/<world>/bcc-economy.json
	public class EconomyLedger
	{
		// player key (Player.ID.ToStringReadable()) -> balance. Always kept up to date, also while
		// Pytonia Automation is the active backend (mirror), so nothing is lost if it gets removed.
		public Dictionary<string, long> Balances = new Dictionary<string, long>();

		// player key -> last known player name (for /baltop of offline players)
		public Dictionary<string, string> Names = new Dictionary<string, string>();

		// last Pytonia Automation balances this ledger has accounted for (three-way merge base)
		public Dictionary<string, long> PytoniaSnapshot = new Dictionary<string, long>();

		// THE TRADER: colony group id -> last seen Trader Coin stock
		public Dictionary<string, TraderGroupSnapshot> TraderSnapshots = new Dictionary<string, TraderGroupSnapshot>();

		// THE TRADER: colony group id -> coins already converted into money after the mod was removed
		public Dictionary<string, TraderConversion> TraderConverted = new Dictionary<string, TraderConversion>();

		// players that should be told about a conversion when they join next time
		public Dictionary<string, List<string>> PendingNotices = new Dictionary<string, List<string>>();

		public string LastBackend = "";
		public DateTime LastSaveUtc = DateTime.MinValue;

		public List<EconomyTransaction> RecentTransactions = new List<EconomyTransaction>();

		public class TraderGroupSnapshot
		{
			public long Coins;
			public List<string> OwnerKeys = new List<string>();
			public string Name = "";
			public DateTime UpdatedUtc;
		}

		public class TraderConversion
		{
			public long Coins;
			public long Money;
			public List<string> OwnerKeys = new List<string>();
			public DateTime ConvertedUtc;
		}

		public class EconomyTransaction
		{
			public DateTime Utc;
			public string PlayerKey;
			public long Delta;
			public string Reason;
		}
	}

	public static class EconomyLedgerStore
	{
		public const string FILE_NAME = "bcc-economy.json";
		const int MAX_TRANSACTIONS = 300;

		public static EconomyLedger Ledger = new EconomyLedger();
		static bool loadFailed;
		static bool dirty;

		static string FilePath => Path.Combine("gamedata", "savegames", ServerManager.WorldName, FILE_NAME);

		public static void Load()
		{
			loadFailed = false;
			string path = FilePath;
			if (!File.Exists(path)) {
				Ledger = new EconomyLedger();
				return;
			}
			try {
				EconomyLedger loaded = JsonConvert.DeserializeObject<EconomyLedger>(File.ReadAllText(path), new JsonSerializerSettings {
					ObjectCreationHandling = ObjectCreationHandling.Replace
				});
				if (loaded == null) {
					throw new InvalidDataException("empty file");
				}
				Ledger = loaded;
				if (Ledger.Balances == null) Ledger.Balances = new Dictionary<string, long>();
				if (Ledger.Names == null) Ledger.Names = new Dictionary<string, string>();
				if (Ledger.PytoniaSnapshot == null) Ledger.PytoniaSnapshot = new Dictionary<string, long>();
				if (Ledger.TraderSnapshots == null) Ledger.TraderSnapshots = new Dictionary<string, EconomyLedger.TraderGroupSnapshot>();
				if (Ledger.TraderConverted == null) Ledger.TraderConverted = new Dictionary<string, EconomyLedger.TraderConversion>();
				if (Ledger.PendingNotices == null) Ledger.PendingNotices = new Dictionary<string, List<string>>();
				if (Ledger.RecentTransactions == null) Ledger.RecentTransactions = new List<EconomyLedger.EconomyTransaction>();
				PlayerKeyMigration.MigrateLegacyKeys(Ledger.Balances, "bcc-economy Balances");
				PlayerKeyMigration.MigrateLegacyKeys(Ledger.PytoniaSnapshot, "bcc-economy PytoniaSnapshot");
			} catch (Exception e) {
				// keep the file untouched and refuse to save over it during this session
				loadFailed = true;
				Ledger = new EconomyLedger();
				Log.WriteError($"Better Colony Commands: could not read {FILE_NAME} - file left untouched and economy saving disabled for this session: {e.Message}");
			}
		}

		public static bool LoadFailed => loadFailed;

		public static void MarkDirty()
		{
			dirty = true;
		}

		public static void SaveIfDirty()
		{
			if (dirty) {
				Save();
			}
		}

		public static void Save()
		{
			if (loadFailed) {
				return;
			}
			dirty = false;
			try {
				string path = FilePath;
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				if (File.Exists(path)) {
					File.Copy(path, path + ".bak", true);
				}
				while (Ledger.RecentTransactions.Count > MAX_TRANSACTIONS) {
					Ledger.RecentTransactions.RemoveAt(0);
				}
				Ledger.LastSaveUtc = DateTime.UtcNow;
				File.WriteAllText(path, JsonConvert.SerializeObject(Ledger, Formatting.Indented));
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: could not save {FILE_NAME}: {e.Message}");
			}
		}
	}
}
