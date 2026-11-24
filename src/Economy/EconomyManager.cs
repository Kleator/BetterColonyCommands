using System;
using System.Collections.Generic;
using System.Linq;
using Pipliz;

namespace ColonyCommands
{
	// Economy module (based on the Pytonia Automation economy). Two backends:
	//  - "BetterColonyCommands": balances live in bcc-economy.json
	//  - "PytoniaAutomation": Pytonia Automation is installed and holds the balances; BCC forwards every
	//    operation to it and mirrors all balances into bcc-economy.json, so they survive if it is removed.
	// At startup the ledgers are reconciled with a three-way merge against the last snapshot, so changes
	// made in either mod while the other one was missing are both kept.
	public static class EconomyManager
	{
		public const string PERMISSION_ADMIN = AntiGrief.MOD_PREFIX + "economy.admin";
		public const string BACKEND_BCC = "BetterColonyCommands";
		public const string BACKEND_PYTONIA = "PytoniaAutomation";

		static bool startupDone;
		static double nextMirror;
		static double nextPassiveIncome;
		static int readyRetries;

		static BccSettings.EconomySettings Cfg => BccSettingsManager.Settings.Economy;

		public static bool IsEnabled => Cfg.Enabled && !EconomyLedgerStore.LoadFailed;

		public static bool PytoniaActive => Cfg.PytoniaSync && PytoniaEconomyBridge.IsInstalled && PytoniaEconomyBridge.IsReady;

		// true when Pytonia Automation answers the money commands itself
		public static bool PytoniaHandlesCommands => PytoniaEconomyBridge.IsInstalled;

		public static string BackendName => PytoniaActive ? BACKEND_PYTONIA : BACKEND_BCC;

		public static string CurrencyName {
			get {
				if (PytoniaEconomyBridge.IsInstalled && Cfg.PytoniaSync) {
					string sym = PytoniaEconomyBridge.ReadDiskCurrency();
					if (!string.IsNullOrEmpty(sym)) {
						return sym;
					}
				}
				return Cfg.CurrencyName;
			}
		}

		public static string Format(long amount)
		{
			return $"{amount:N0} {CurrencyName}";
		}

		public static string Key(Players.Player player) => player.ID.ToStringReadable();

		// ------------------------------------------------------------------ lifecycle

		public static void AfterWorldLoad()
		{
			EconomyLedgerStore.Load();
			startupDone = false;
			readyRetries = 0;
			// all mods must have loaded their own data first (Pytonia saves its whole config on every
			// balance change - touching it too early would overwrite it with empty defaults)
			ThreadManager.InvokeOnMainThread(StartupReconcile, 5.0);
		}

		static void StartupReconcile()
		{
			try {
				if (PytoniaEconomyBridge.IsInstalled && Cfg.PytoniaSync && !PytoniaEconomyBridge.CheckReady()) {
					if (++readyRetries <= 12) {
						ThreadManager.InvokeOnMainThread(StartupReconcile, 5.0);
						return;
					}
					Log.WriteError("Better Colony Commands: Pytonia Automation is installed but its economy data never finished loading - not touching it. BCC keeps its own mirrored balances this session.");
				}
				MergePytonia();
				TraderBridge.ReconcileAtStartup();
				EconomyLedgerStore.Ledger.LastBackend = BackendName;
				EconomyLedgerStore.Save();
				Log.Write($"Better Colony Commands: economy ready (enabled={IsEnabled}, backend={BackendName}, players={EconomyLedgerStore.Ledger.Balances.Count})");
			} catch (Exception e) {
				Log.WriteException("Better Colony Commands: economy startup reconcile failed", e);
			}
			startupDone = true;
		}

		// three-way merge: merged = mine + (theirs - base), where base is the last value both agreed on
		public static Dictionary<string, long> ThreeWayMerge(Dictionary<string, long> mine, Dictionary<string, long> theirs, Dictionary<string, long> snapshot)
		{
			Dictionary<string, long> merged = new Dictionary<string, long>();
			HashSet<string> keys = new HashSet<string>(mine.Keys);
			keys.UnionWith(theirs.Keys);
			keys.UnionWith(snapshot.Keys);
			foreach (string k in keys) {
				bool hasMine = mine.TryGetValue(k, out long m);
				bool hasTheirs = theirs.TryGetValue(k, out long t);
				bool hasBase = snapshot.TryGetValue(k, out long b);
				long result;
				if (hasBase) {
					if (!hasMine) m = b;
					if (!hasTheirs) t = b;
					result = m + (t - b);
				} else if (hasMine && hasTheirs) {
					// never synced before: both sides started independently (e.g. both granted a
					// starting balance) - take the larger one instead of adding them up
					result = System.Math.Max(m, t);
				} else {
					result = hasMine ? m : t;
				}
				merged[k] = System.Math.Max(0, result);
			}
			return merged;
		}

		static void MergePytonia()
		{
			if (!Cfg.PytoniaSync) {
				return;
			}
			EconomyLedger ledger = EconomyLedgerStore.Ledger;
			bool active = PytoniaActive;
			Dictionary<string, long> theirs = active ? PytoniaEconomyBridge.ReadLiveBalances() : PytoniaEconomyBridge.ReadDiskBalances();
			if (theirs == null) {
				return; // Pytonia Automation never ran on this world
			}
			Dictionary<string, long> merged = ThreeWayMerge(ledger.Balances, theirs, ledger.PytoniaSnapshot);
			int changedMine = merged.Count(kvp => !ledger.Balances.TryGetValue(kvp.Key, out long v) || v != kvp.Value);
			ledger.Balances = merged;
			if (active) {
				Dictionary<string, long> push = merged.Where(kvp => !theirs.TryGetValue(kvp.Key, out long v) || v != kvp.Value).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
				if (push.Count > 0) {
					PytoniaEconomyBridge.SetBalancesByKey(push);
				}
				ledger.PytoniaSnapshot = new Dictionary<string, long>(merged);
				Log.Write($"Better Colony Commands: economy linked with Pytonia Automation ({changedMine} BCC balance(s) updated, {push.Count} pushed to Pytonia Automation).");
			} else {
				ledger.PytoniaSnapshot = new Dictionary<string, long>(theirs);
				if (changedMine > 0) {
					Log.Write($"Better Colony Commands: imported {changedMine} balance change(s) from pytonia-automation.json (Pytonia Automation not active).");
				}
			}
			EconomyLedgerStore.MarkDirty();
		}

		// keeps the BCC ledger a full copy of Pytonia's balances while it is the active backend
		static void MirrorPytonia()
		{
			if (!PytoniaActive) {
				return;
			}
			Dictionary<string, long> live = PytoniaEconomyBridge.ReadLiveBalances();
			if (live == null) {
				return;
			}
			EconomyLedger ledger = EconomyLedgerStore.Ledger;
			bool changed = false;
			foreach (KeyValuePair<string, long> kvp in live) {
				if (!ledger.Balances.TryGetValue(kvp.Key, out long v) || v != kvp.Value) {
					ledger.Balances[kvp.Key] = kvp.Value;
					changed = true;
				}
				ledger.PytoniaSnapshot[kvp.Key] = kvp.Value;
			}
			if (changed) {
				EconomyLedgerStore.MarkDirty();
			}
		}

		public static void OnUpdate()
		{
			if (!startupDone) {
				return;
			}
			double now = Pipliz.Time.SecondsSinceStartDoubleThisFrame;
			if (now >= nextMirror) {
				nextMirror = now + 30.0;
				MirrorPytonia();
				EconomyLedgerStore.SaveIfDirty();
			}
			TraderBridge.OnUpdate();
			if (IsEnabled && !PytoniaHandlesCommands && Cfg.PassiveIncomeIntervalMinutes > 0 && Cfg.PassiveIncomeAmount != 0) {
				if (nextPassiveIncome <= 0) {
					nextPassiveIncome = now + Cfg.PassiveIncomeIntervalMinutes * 60.0;
				} else if (now >= nextPassiveIncome) {
					nextPassiveIncome = now + Cfg.PassiveIncomeIntervalMinutes * 60.0;
					foreach (Players.Player p in Players.ConnectedPlayers) {
						TryAdd(p, Cfg.PassiveIncomeAmount, "passive income");
					}
				}
			}
		}

		public static void OnAutoSaveOrQuit()
		{
			if (!startupDone) {
				return;
			}
			MirrorPytonia();
			TraderBridge.Snapshot();
			EconomyLedgerStore.Save();
		}

		public static void OnPlayerConnectedLate(Players.Player player)
		{
			if (player == null) {
				return;
			}
			EconomyLedger ledger = EconomyLedgerStore.Ledger;
			string key = Key(player);
			ledger.Names[key] = player.Name;
			if (IsEnabled && !PytoniaHandlesCommands && !ledger.Balances.ContainsKey(key)) {
				ledger.Balances[key] = 0;
				if (Cfg.StartingBalance > 0) {
					TryAdd(player, Cfg.StartingBalance, "starting balance");
				}
			}
			if (ledger.PendingNotices.TryGetValue(key, out List<string> notices)) {
				ledger.PendingNotices.Remove(key);
				ThreadManager.InvokeOnMainThread(delegate {
					foreach (string n in notices) {
						BccChat.Send(player, $"<color=#FFD166>{n}</color>");
					}
				}, 12.0);
			}
			EconomyLedgerStore.MarkDirty();
			EconomyHud.UpdatePlayer(player);
		}

		// ------------------------------------------------------------------ operations

		public static long GetBalance(Players.Player player)
		{
			if (player == null) {
				return 0;
			}
			if (PytoniaActive) {
				try {
					return PytoniaEconomyBridge.GetBalance(player);
				} catch (Exception e) {
					Log.WriteWarning($"Better Colony Commands: Pytonia balance read failed: {e.Message}");
				}
			}
			return GetBalanceByKey(Key(player));
		}

		public static long GetBalanceByKey(string key)
		{
			return EconomyLedgerStore.Ledger.Balances.TryGetValue(key, out long v) ? v : 0;
		}

		static void Record(string key, long delta, string reason)
		{
			EconomyLedgerStore.Ledger.RecentTransactions.Add(new EconomyLedger.EconomyTransaction {
				Utc = DateTime.UtcNow, PlayerKey = key, Delta = delta, Reason = reason ?? ""
			});
			EconomyLedgerStore.MarkDirty();
		}

		public static bool TryAdd(Players.Player player, long amount, string reason)
		{
			if (!IsEnabled || player == null) {
				return false;
			}
			if (amount < 0) {
				return TryRemove(player, -amount, reason);
			}
			string key = Key(player);
			if (PytoniaActive) {
				PytoniaEconomyBridge.AddBalance(player, amount);
				long now = PytoniaEconomyBridge.GetBalance(player);
				EconomyLedgerStore.Ledger.Balances[key] = now;
				EconomyLedgerStore.Ledger.PytoniaSnapshot[key] = now;
			} else {
				EconomyLedgerStore.Ledger.Balances[key] = GetBalanceByKey(key) + amount;
			}
			Record(key, amount, reason);
			EconomyHud.OnBalanceChanged(player, amount);
			return true;
		}

		public static bool TryRemove(Players.Player player, long amount, string reason)
		{
			if (!IsEnabled || player == null || amount < 0) {
				return false;
			}
			string key = Key(player);
			if (PytoniaActive) {
				if (!PytoniaEconomyBridge.TryRemoveBalance(player, amount)) {
					return false;
				}
				long now = PytoniaEconomyBridge.GetBalance(player);
				EconomyLedgerStore.Ledger.Balances[key] = now;
				EconomyLedgerStore.Ledger.PytoniaSnapshot[key] = now;
			} else {
				long balance = GetBalanceByKey(key);
				if (balance < amount) {
					return false;
				}
				EconomyLedgerStore.Ledger.Balances[key] = balance - amount;
			}
			Record(key, -amount, reason);
			EconomyHud.OnBalanceChanged(player, -amount);
			return true;
		}

		public static bool TrySet(Players.Player player, long amount, string reason)
		{
			if (!IsEnabled || player == null || amount < 0) {
				return false;
			}
			string key = Key(player);
			long before = GetBalance(player);
			if (PytoniaActive) {
				PytoniaEconomyBridge.SetBalance(player, amount);
				EconomyLedgerStore.Ledger.PytoniaSnapshot[key] = amount;
			}
			EconomyLedgerStore.Ledger.Balances[key] = amount;
			Record(key, amount - before, reason);
			EconomyHud.OnBalanceChanged(player, amount - before);
			return true;
		}

		public static bool TryTransfer(Players.Player from, Players.Player to, long amount, string reason)
		{
			if (!IsEnabled || from == null || to == null || amount <= 0 || from == to) {
				return false;
			}
			if (!TryRemove(from, amount, reason)) {
				return false;
			}
			if (!TryAdd(to, amount, reason)) {
				TryAdd(from, amount, "refund: " + reason);
				return false;
			}
			return true;
		}

		// credit by player key, works for offline players (used for Trader Coin conversion)
		public static void CreditKey(string key, long amount, string reason)
		{
			Players.Player online = Players.ConnectedPlayers.FirstOrDefault(p => p != null && Key(p) == key);
			if (online != null && TryAdd(online, amount, reason)) {
				return;
			}
			EconomyLedger ledger = EconomyLedgerStore.Ledger;
			long now = GetBalanceByKey(key) + amount;
			ledger.Balances[key] = System.Math.Max(0, now);
			if (PytoniaActive) {
				PytoniaEconomyBridge.SetBalancesByKey(new Dictionary<string, long> { { key, ledger.Balances[key] } });
				ledger.PytoniaSnapshot[key] = ledger.Balances[key];
			}
			Record(key, amount, reason);
		}

		public static List<KeyValuePair<string, long>> GetTop()
		{
			MirrorPytonia();
			return EconomyLedgerStore.Ledger.Balances.OrderByDescending(kvp => kvp.Value).ToList();
		}

		public static Players.Player PlayerOf(string key)
		{
			foreach (Players.Player p in Players.PlayerDatabase.Values) {
				if (p != null && Key(p) == key) {
					return p;
				}
			}
			return null;
		}

		public static string NameOf(string key)
		{
			if (EconomyLedgerStore.Ledger.Names.TryGetValue(key, out string name) && !string.IsNullOrEmpty(name)) {
				return name;
			}
			foreach (Players.Player p in Players.PlayerDatabase.Values) {
				if (p != null && Key(p) == key) {
					return p.Name;
				}
			}
			return key;
		}
	}
}
