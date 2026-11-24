using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Pipliz;

namespace ColonyCommands
{
	// General, optional access to Pytonia Automation's config (pytonia-automation.json) via reflection:
	// roles (time based role thresholds, home limits, colony limits), time based role records and
	// home points. Never touches it before its data is loaded (see PytoniaEconomyBridge.CheckReady),
	// because Pytonia saves its whole config on every change.
	public static class PytoniaBridge
	{
		public const string MOD_NAME = "Pytonia Automation";
		static Type configManagerType;
		static Type homePointType;
		static MethodInfo save;
		static MethodInfo getHomeLimit;

		public static bool IsInstalled => configManagerType != null;
		public static bool IsReady => IsInstalled && PytoniaEconomyBridge.CheckReady();

		public static string DiskFilePath => Path.Combine("gamedata", "savegames", ServerManager.WorldName, "pytonia-automation.json");

		public static void Detect(List<ModLoader.ModDescription> mods)
		{
			configManagerType = null;
			homePointType = null;
			getHomeLimit = null;
			foreach (ModLoader.ModDescription mod in mods) {
				if (mod == null || !mod.HasAssembly) {
					continue;
				}
				try {
					Type t = mod.LoadedAssembly.GetType("PytoniaAutomation.Core.ConfigManager", false);
					if (t == null) {
						continue;
					}
					configManagerType = t;
					save = t.GetMethod("Save", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
					homePointType = mod.LoadedAssembly.GetType("PytoniaAutomation.Modules.Homes.HomePoint", false);
					Type homes = mod.LoadedAssembly.GetType("PytoniaAutomation.Modules.Homes.HomesModule", false);
					getHomeLimit = homes?.GetMethod("GetHomeLimit", new[] { typeof(Players.Player) });
					break;
				} catch {
				}
			}
		}

		public static object Config()
		{
			try {
				return configManagerType?.GetProperty("Config", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
			} catch {
				return null;
			}
		}

		public static object Section(string name)
		{
			object cfg = Config();
			return cfg?.GetType().GetField(name)?.GetValue(cfg);
		}

		public static void Save()
		{
			if (!IsReady) {
				return;
			}
			try {
				save?.Invoke(null, null);
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not save Pytonia Automation's config: {e.Message}");
			}
		}

		// Roles: role name -> RoleSettings (live object)
		public static IDictionary Roles()
		{
			return Section("Roles") as IDictionary;
		}

		public static object GetField(object obj, string field)
		{
			return obj?.GetType().GetField(field)?.GetValue(obj);
		}

		public static bool SetField(object obj, string field, object value)
		{
			FieldInfo f = obj?.GetType().GetField(field);
			if (f == null) {
				return false;
			}
			f.SetValue(obj, Convert.ChangeType(value, f.FieldType));
			return true;
		}

		public static Type FieldType(object obj, string field)
		{
			return obj?.GetType().GetField(field)?.FieldType;
		}

		// effective colony/outpost limit (override or role limit), -1 = unlimited
		public static int GetColonyLimit(Players.Player player)
		{
			try {
				Type t = configManagerType?.Assembly.GetType("PytoniaAutomation.Modules.Colonies.ColoniesModule", false);
				MethodInfo m = t?.GetMethod("GetColonyLimit", new[] { typeof(Players.Player) });
				return m != null ? (int)m.Invoke(null, new object[] { player }) : -1;
			} catch {
				return -1;
			}
		}

		public static int GetHomeLimit(Players.Player player)
		{
			try {
				return (int)getHomeLimit.Invoke(null, new object[] { player });
			} catch {
				return 0;
			}
		}

		// Homes.PlayerHomes: player key -> List<HomePoint> (live)
		public static IDictionary LiveHomes()
		{
			return GetField(Section("Homes"), "PlayerHomes") as IDictionary;
		}

		public static object NewHomePoint(string name, float x, float y, float z)
		{
			if (homePointType == null) {
				return null;
			}
			object hp = Activator.CreateInstance(homePointType);
			SetField(hp, "Name", name);
			SetField(hp, "X", x);
			SetField(hp, "Y", y);
			SetField(hp, "Z", z);
			return hp;
		}

		public static IList NewHomeList()
		{
			if (homePointType == null) {
				return null;
			}
			return (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(homePointType));
		}

		public static JObject ReadDisk()
		{
			try {
				if (File.Exists(DiskFilePath)) {
					return JObject.Parse(File.ReadAllText(DiskFilePath));
				}
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not read pytonia-automation.json: {e.Message}");
			}
			return null;
		}

		public static void WriteDisk(JObject root)
		{
			try {
				if (File.Exists(DiskFilePath)) {
					File.Copy(DiskFilePath, DiskFilePath + ".bak", true);
				}
				File.WriteAllText(DiskFilePath, root.ToString(Newtonsoft.Json.Formatting.Indented));
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not write pytonia-automation.json: {e.Message}");
			}
		}

		// Renames/removes a role in Pytonia's data (live if loaded, otherwise in the file)
		public static void RenameRole(string oldName, string newName)
		{
			if (IsReady) {
				IDictionary roles = Roles();
				if (roles != null && roles.Contains(oldName)) {
					object settings = roles[oldName];
					roles.Remove(oldName);
					if (newName != null && !roles.Contains(newName)) {
						roles[newName] = settings;
					}
				}
				object tbr = Section("TimeBasedRoles");
				if (GetField(tbr, "GrantedRoleKeys") is IDictionary granted) {
					foreach (object list in granted.Values) {
						if (list is List<string> l) {
							int i = l.IndexOf(oldName);
							if (i >= 0) {
								if (newName != null) l[i] = newName; else l.RemoveAt(i);
							}
						}
					}
				}
				if (GetField(tbr, "GrantedRoleLog") is IList log) {
					foreach (object entry in log) {
						if (GetField(entry, "RoleName") as string == oldName && newName != null) {
							SetField(entry, "RoleName", newName);
						}
					}
				}
				Save();
				return;
			}
			JObject root = ReadDisk();
			if (root == null) {
				return;
			}
			if (root["Roles"] is JObject r && r[oldName] != null) {
				JToken settings = r[oldName];
				r.Remove(oldName);
				if (newName != null && r[newName] == null) {
					r[newName] = settings;
				}
			}
			if (root["TimeBasedRoles"]?["GrantedRoleKeys"] is JObject g) {
				foreach (JProperty p in g.Properties()) {
					if (p.Value is JArray arr) {
						for (int i = arr.Count - 1; i >= 0; i--) {
							if (arr[i].ToString() == oldName) {
								if (newName != null) arr[i] = newName; else arr.RemoveAt(i);
							}
						}
					}
				}
			}
			if (root["TimeBasedRoles"]?["GrantedRoleLog"] is JArray logArr && newName != null) {
				foreach (JObject e in logArr.OfType<JObject>()) {
					if (e["RoleName"]?.ToString() == oldName) {
						e["RoleName"] = newName;
					}
				}
			}
			WriteDisk(root);
		}
	}
}
