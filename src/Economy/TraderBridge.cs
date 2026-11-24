using System;
using System.Collections.Generic;
using System.Linq;
using Pipliz;

namespace ColonyCommands
{
	// "THE TRADER - Advanced Trading Station" (Steam Workshop 3782886936, MyFirstMod.dll) keeps its
	// currency as the stockpile item "thetrader.tradercoin" of each colony group - there is no
	// per-player balance and no save file of its own for coins (only station upgrade levels in
	// gamedata/mods/thetrader/tradeupgrades.txt). Stockpiles are saved by a world specific item index
	// (world.sqlite3 table item_mapping), so once the mod is removed its coin type is unknown and the
	// coins can no longer be used.
	//
	// To never lose that value Better Colony Commands snapshots every colony group's coin stock while
	// THE TRADER is installed. When the coin type is missing at startup, the last snapshot is
	// converted once into money for the colony group's owners. If THE TRADER is installed again and
	// the game kept the old coins, exactly the converted amount is taken back out of the stockpile, so
	// nothing is counted twice.
	public static class TraderBridge
	{
		public const string COIN_TYPE = "thetrader.tradercoin";
		public const string MOD_NAME = "THE TRADER";

		static ushort coinIndex;
		static bool coinTypeKnown;
		static bool modLoaded;
		static double nextSnapshot;

		// Tested (BCC 1.1.0 self test): when THE TRADER is removed, the game still knows the item type
		// "thetrader.tradercoin" as a placeholder from the world's item_mapping table, and the coins
		// stay in the stockpiles - but nothing can use them any more. So "installed" means the mod's
		// assembly is loaded, not just that the item type exists.
		public static bool IsInstalled => modLoaded && coinTypeKnown;
		public static ushort CoinIndex => coinIndex;

		public static void Detect(List<ModLoader.ModDescription> mods)
		{
			modLoaded = false;
			foreach (ModLoader.ModDescription m in mods) {
				if (m == null) {
					continue;
				}
				try {
					if (string.Equals(m.name, MOD_NAME, StringComparison.OrdinalIgnoreCase) && m.HasAssembly) {
						modLoaded = true;
					} else if (m.HasAssembly && m.LoadedAssembly.GetType("TheTrader.TraderMain", false) != null) {
						modLoaded = true;
					}
				} catch {
				}
			}
		}

		public static void AfterItemTypesDefined()
		{
			coinTypeKnown = ItemTypes.TryGetType(COIN_TYPE, out ItemTypes.ItemType type) && type != null && type.ItemIndex != 0;
			coinIndex = coinTypeKnown ? type.ItemIndex : (ushort)0;
			if (IsInstalled) {
				Log.Write("Better Colony Commands: THE TRADER found - Trader Coins are tracked by the economy module.");
			} else if (coinTypeKnown) {
				Log.Write("Better Colony Commands: THE TRADER is not loaded, but its Trader Coins still exist as placeholder items in this world - they will be converted into money.");
			}
		}

		static IEnumerable<ColonyGroup> AllGroups()
		{
			List<ColonyGroup> groups = new List<ColonyGroup>();
			if (ServerManager.ColonyTracker == null) {
				return groups;
			}
			var e = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
			while (e.MoveNext()) {
				if (e.Current != null) {
					groups.Add(e.Current);
				}
			}
			return groups;
		}

		static List<string> OwnerKeys(ColonyGroup group)
		{
			List<string> keys = new List<string>();
			for (int i = 0; i < group.Owners.Count; i++) {
				Players.Player owner = group.Owners.GetAt(i);
				if (owner != null) {
					keys.Add(owner.ID.ToStringReadable());
				}
			}
			return keys;
		}

		public static string GroupName(ColonyGroup group)
		{
			try {
				return TextHelper.SafeName(group.Colonies.Count > 0 ? group.Colonies.GetAt(0).Name : $"group {group.ColonyGroupID.Value}");
			} catch {
				return $"group {group.ColonyGroupID.Value}";
			}
		}

		public static void Snapshot()
		{
			if (!IsInstalled || !BccSettingsManager.Settings.Economy.TraderIntegration) {
				return;
			}
			EconomyLedger ledger = EconomyLedgerStore.Ledger;
			foreach (ColonyGroup group in AllGroups()) {
				string id = group.ColonyGroupID.Value.ToString();
				long coins = group.Stockpile.AmountContained(coinIndex);
				if (coins <= 0 && !ledger.TraderSnapshots.ContainsKey(id)) {
					continue;
				}
				if (!ledger.TraderSnapshots.TryGetValue(id, out EconomyLedger.TraderGroupSnapshot snap)) {
					snap = new EconomyLedger.TraderGroupSnapshot();
					ledger.TraderSnapshots[id] = snap;
				}
				snap.Coins = coins;
				snap.OwnerKeys = OwnerKeys(group);
				snap.Name = GroupName(group);
				snap.UpdatedUtc = DateTime.UtcNow;
			}
			EconomyLedgerStore.MarkDirty();
		}

		public static void OnUpdate()
		{
			if (!IsInstalled) {
				return;
			}
			double now = Pipliz.Time.SecondsSinceStartDouble;
			if (now < nextSnapshot) {
				return;
			}
			nextSnapshot = now + BccSettingsManager.Settings.Economy.TraderSnapshotIntervalSeconds;
			Snapshot();
		}

		// called once after the world (and every other mod) has loaded
		public static void ReconcileAtStartup()
		{
			EconomySettingsCheck();
			EconomyLedger ledger = EconomyLedgerStore.Ledger;
			if (!BccSettingsManager.Settings.Economy.TraderIntegration) {
				return;
			}
			if (!modLoaded) {
				ConvertSnapshotsToMoney(ledger);
			} else if (coinTypeKnown) {
				TakeBackAlreadyConvertedCoins(ledger);
				Snapshot();
			}
			EconomyLedgerStore.Save();
		}

		static void EconomySettingsCheck()
		{
			if (!EconomyManager.IsEnabled && !modLoaded && EconomyLedgerStore.Ledger.TraderSnapshots.Values.Any(s => s.Coins > 0)) {
				Log.WriteWarning("Better Colony Commands: THE TRADER is gone and coin snapshots exist, but the economy module is disabled - conversion postponed until the economy is enabled.");
			}
		}

		static void ConvertSnapshotsToMoney(EconomyLedger ledger)
		{
			if (!EconomyManager.IsEnabled) {
				return;
			}
			long value = BccSettingsManager.Settings.Economy.TraderCoinValue;
			Dictionary<string, ColonyGroup> groups = AllGroups().ToDictionary(g => g.ColonyGroupID.Value.ToString());

			// with placeholder coins still in the stockpiles, those are the exact current amounts;
			// otherwise the last snapshot taken while THE TRADER was running is used
			HashSet<string> ids = new HashSet<string>(ledger.TraderSnapshots.Where(kvp => kvp.Value.Coins > 0).Select(kvp => kvp.Key));
			if (coinTypeKnown) {
				foreach (KeyValuePair<string, ColonyGroup> g in groups) {
					if (g.Value.Stockpile.AmountContained(coinIndex) > 0) {
						ids.Add(g.Key);
					}
				}
			}
			foreach (string id in ids) {
				ledger.TraderSnapshots.TryGetValue(id, out EconomyLedger.TraderGroupSnapshot snap);
				groups.TryGetValue(id, out ColonyGroup group);
				long coins = snap?.Coins ?? 0;
				bool fromStockpile = false;
				if (coinTypeKnown && group != null) {
					coins = group.Stockpile.AmountContained(coinIndex);
					fromStockpile = true;
				}
				if (coins <= 0) {
					if (snap != null) snap.Coins = 0;
					continue;
				}
				List<string> owners = group != null ? OwnerKeys(group) : (snap?.OwnerKeys ?? new List<string>());
				string name = group != null ? GroupName(group) : (snap?.Name ?? id);
				if (owners.Count == 0) {
					Log.WriteWarning($"Better Colony Commands: {coins} Trader Coins of colony group {id} have no known owner - kept for manual handling.");
					continue;
				}
				long money = coins * value;
				long share = money / owners.Count;
				long remainder = money - share * owners.Count;
				for (int i = 0; i < owners.Count; i++) {
					long amount = share + (i == 0 ? remainder : 0);
					if (amount != 0) {
						EconomyManager.CreditKey(owners[i], amount, $"THE TRADER removed: {coins} Trader Coins of '{name}' converted");
						AddNotice(ledger, owners[i], $"THE TRADER was removed from the server. Your colony '{name}' had {coins:N0} Trader Coins - you received {EconomyManager.Format(amount)} instead.");
					}
				}
				if (fromStockpile) {
					// take the unusable placeholder coins out, so they can never be counted twice
					group.Stockpile.TryRemove(coinIndex, (int)System.Math.Min(coins, int.MaxValue));
				}
				if (!ledger.TraderConverted.TryGetValue(id, out EconomyLedger.TraderConversion conv)) {
					conv = new EconomyLedger.TraderConversion();
					ledger.TraderConverted[id] = conv;
				}
				// coins still waiting in the save (no placeholder) are what a reinstall would bring back
				if (!fromStockpile) {
					conv.Coins += coins;
				}
				conv.Money += money;
				conv.OwnerKeys = owners;
				conv.ConvertedUtc = DateTime.UtcNow;
				Log.Write($"Better Colony Commands: converted {coins} Trader Coins of colony group {id} ('{name}') into {money} money for {owners.Count} owner(s){(fromStockpile ? ", placeholder coins removed from the stockpile" : "")}.");
				if (snap != null) {
					snap.Coins = 0;
					snap.UpdatedUtc = DateTime.UtcNow;
				}
			}
		}

		static void TakeBackAlreadyConvertedCoins(EconomyLedger ledger)
		{
			if (ledger.TraderConverted.Count == 0) {
				return;
			}
			Dictionary<string, ColonyGroup> groups = AllGroups().ToDictionary(g => g.ColonyGroupID.Value.ToString());
			foreach (KeyValuePair<string, EconomyLedger.TraderConversion> kvp in ledger.TraderConverted.ToList()) {
				if (groups.TryGetValue(kvp.Key, out ColonyGroup group)) {
					int inStock = group.Stockpile.AmountContained(coinIndex);
					int takeBack = (int)System.Math.Min(inStock, kvp.Value.Coins);
					if (takeBack > 0) {
						group.Stockpile.TryRemove(coinIndex, takeBack);
						Log.Write($"Better Colony Commands: THE TRADER is back - removed {takeBack} old Trader Coins from colony group {kvp.Key}, they were already converted into money.");
					}
				}
				ledger.TraderConverted.Remove(kvp.Key);
			}
		}

		static void AddNotice(EconomyLedger ledger, string playerKey, string text)
		{
			if (!ledger.PendingNotices.TryGetValue(playerKey, out List<string> list)) {
				list = new List<string>();
				ledger.PendingNotices[playerKey] = list;
			}
			list.Add(text);
		}

		public static int GetCoins(ColonyGroup group)
		{
			if (!coinTypeKnown || group == null) {
				return 0;
			}
			return group.Stockpile.AmountContained(coinIndex);
		}

		public static bool TryRemoveCoins(ColonyGroup group, int amount)
		{
			if (!coinTypeKnown || group == null || amount <= 0) {
				return false;
			}
			return group.Stockpile.TryRemove(coinIndex, amount);
		}

		public static void AddCoins(ColonyGroup group, int amount)
		{
			if (coinTypeKnown && group != null && amount > 0) {
				group.Stockpile.Add(coinIndex, amount);
			}
		}
	}
}
