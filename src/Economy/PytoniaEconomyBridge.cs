using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Pipliz;

namespace ColonyCommands
{
	// Optional link to the economy of "Pytonia Automation" (pytonia-automation.json, Economy.Balances,
	// keyed by Player.ID.ToStringReadable()). Everything goes through reflection, so Better Colony
	// Commands neither needs nor breaks without that mod.
	//
	// IMPORTANT: Pytonia's EconomyModule saves its whole config on every balance change. Calling it
	// before Pytonia has loaded its config would overwrite the real file with empty defaults, so
	// IsReady only becomes true once its in-memory data demonstrably matches the file on disk.
	public static class PytoniaEconomyBridge
	{
		const string ECONOMY_TYPE = "PytoniaAutomation.Modules.Economy.EconomyModule";
		const string CONFIG_TYPE = "PytoniaAutomation.Core.ConfigManager";
		public const string MOD_NAME = "Pytonia Automation";

		static Type economyType;
		static Type configManagerType;
		static MethodInfo getBalance, addBalance, setBalance, tryRemoveBalance, format, save;
		static bool ready;

		public static bool IsInstalled => economyType != null;
		public static bool IsReady => ready;

		public static string DiskFilePath => Path.Combine("gamedata", "savegames", ServerManager.WorldName, "pytonia-automation.json");

		public static void Detect(List<ModLoader.ModDescription> mods)
		{
			economyType = null;
			configManagerType = null;
			foreach (ModLoader.ModDescription mod in mods) {
				if (mod == null || !mod.HasAssembly) {
					continue;
				}
				try {
					Type t = mod.LoadedAssembly.GetType(ECONOMY_TYPE, false);
					if (t != null) {
						economyType = t;
						configManagerType = mod.LoadedAssembly.GetType(CONFIG_TYPE, false);
						break;
					}
				} catch {
				}
			}
			if (economyType == null) {
				return;
			}
			Type player = typeof(Players.Player);
			getBalance = economyType.GetMethod("GetBalance", new[] { player });
			addBalance = economyType.GetMethod("AddBalance", new[] { player, typeof(long) });
			setBalance = economyType.GetMethod("SetBalance", new[] { player, typeof(long) });
			tryRemoveBalance = economyType.GetMethod("TryRemoveBalance", new[] { player, typeof(long) });
			format = economyType.GetMethod("Format", new[] { typeof(long) });
			save = configManagerType?.GetMethod("Save", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
			if (getBalance == null || addBalance == null || setBalance == null || tryRemoveBalance == null) {
				Log.WriteWarning("Better Colony Commands: Pytonia Automation found, but its EconomyModule has an unexpected shape - economy link disabled.");
				economyType = null;
				return;
			}
			Log.Write("Better Colony Commands: Pytonia Automation economy found - balances will be linked.");
		}

		// in-memory Economy.Balances of Pytonia Automation (live object, do not keep references)
		static IDictionary GetLiveBalances()
		{
			if (configManagerType == null) {
				return null;
			}
			try {
				object config = configManagerType.GetProperty("Config", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
				object economy = config?.GetType().GetField("Economy")?.GetValue(config);
				return economy?.GetType().GetField("Balances")?.GetValue(economy) as IDictionary;
			} catch {
				return null;
			}
		}

		public static Dictionary<string, long> ReadLiveBalances()
		{
			IDictionary live = GetLiveBalances();
			if (live == null) {
				return null;
			}
			Dictionary<string, long> result = new Dictionary<string, long>();
			foreach (DictionaryEntry e in live) {
				if (e.Key is string key && e.Value is long value) {
					result[key] = value;
				}
			}
			return result;
		}

		// Economy.Balances as stored in pytonia-automation.json (works while the mod is uninstalled)
		public static Dictionary<string, long> ReadDiskBalances()
		{
			string path = DiskFilePath;
			if (!File.Exists(path)) {
				return null;
			}
			try {
				JObject root = JObject.Parse(File.ReadAllText(path));
				Dictionary<string, long> result = new Dictionary<string, long>();
				if (root["Economy"]?["Balances"] is JObject balances) {
					foreach (JProperty p in balances.Properties()) {
						if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float) {
							result[p.Name] = p.Value.Value<long>();
						}
					}
				}
				return result;
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not read Pytonia Automation balances from disk: {e.Message}");
				return null;
			}
		}

		public static string ReadDiskCurrency()
		{
			try {
				string path = DiskFilePath;
				if (File.Exists(path)) {
					return JObject.Parse(File.ReadAllText(path))["Economy"]?["CurrencySymbol"]?.Value<string>();
				}
			} catch {
			}
			return null;
		}

		// Ready when Pytonia's in-memory balances are loaded (same keys as the file, or the file has none)
		public static bool CheckReady()
		{
			if (!IsInstalled) {
				ready = false;
				return false;
			}
			if (ready) {
				return true;
			}
			Dictionary<string, long> live = ReadLiveBalances();
			Dictionary<string, long> disk = ReadDiskBalances();
			if (live == null) {
				return false;
			}
			if (disk == null || disk.Count == 0 || live.Count > 0) {
				int matching = 0;
				if (disk != null) {
					foreach (string k in disk.Keys) {
						if (live.ContainsKey(k)) matching++;
					}
				}
				if (disk == null || disk.Count == 0 || matching >= disk.Count / 2) {
					ready = true;
				}
			}
			return ready;
		}

		public static long GetBalance(Players.Player player)
		{
			return (long)getBalance.Invoke(null, new object[] { player });
		}

		public static void AddBalance(Players.Player player, long amount)
		{
			addBalance.Invoke(null, new object[] { player, amount });
		}

		public static void SetBalance(Players.Player player, long amount)
		{
			setBalance.Invoke(null, new object[] { player, amount });
		}

		public static bool TryRemoveBalance(Players.Player player, long amount)
		{
			return (bool)tryRemoveBalance.Invoke(null, new object[] { player, amount });
		}

		public static string Format(long amount)
		{
			try {
				if (format != null) {
					return (string)format.Invoke(null, new object[] { amount });
				}
			} catch {
			}
			return null;
		}

		// for offline players / bulk merges: write directly into Pytonia's dictionary and save once
		public static bool SetBalancesByKey(Dictionary<string, long> values)
		{
			IDictionary live = GetLiveBalances();
			if (live == null || !ready) {
				return false;
			}
			foreach (KeyValuePair<string, long> kvp in values) {
				live[kvp.Key] = kvp.Value;
			}
			try {
				save?.Invoke(null, null);
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not save Pytonia Automation config after merge: {e.Message}");
			}
			return true;
		}
	}
}
