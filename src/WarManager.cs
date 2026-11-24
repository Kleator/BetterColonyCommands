using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using Pipliz;
using Newtonsoft.Json.Linq;
using Chatting;
using Shared.Networking;

namespace ColonyCommands {

	public static class WarManager
	{
		private struct warEntry {
			public long started;
			public long duration;

			public warEntry(long s, long d)
			{
				started = s;
				duration = d;
			}
		}

		private static Dictionary<Players.Player, warEntry> warDict = new Dictionary<Players.Player, warEntry>();
		private static string warPermissionGroup = "peasant";
		// roles of players in war mode, written to disk before they change (restored after the war or at the next start)
		public class StoredRoles
		{
			public string Name = "";
			public bool HadEntry;                            // own entry in permissionusers.json before the war
			public List<string> Groups = new List<string>(); // its roles ("includes")
		}
		private static Dictionary<Players.Player, StoredRoles> storedPermissions = new Dictionary<Players.Player, StoredRoles>();

		static string SaveFile {
			get {
				return Path.Combine(Path.Combine("gamedata", "savegames"), Path.Combine(ServerManager.WorldName, "war-permissions-to-restore.json"));
			}
		}


		// get player list
		public static List<string> PlayerList {
			get {
				List<string> result = new List<string>();
				foreach (Players.Player p in warDict.Keys) {
					string entry = p.Name + "[" + GetRemainingTimestring(warDict[p]) + "]";
					result.Add(entry);
				}
				return result;
			}
		}


		private static string GetRemainingTimestring(warEntry entry)
		{
			long minutes = (entry.started + entry.duration - DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000) / 60;
			return $"{minutes / 60}:{minutes % 60}";
		}


		// check if war enabled Player
		public static bool IsWarEnabled(Players.Player player)
		{
			return warDict.ContainsKey(player);
		}


		// check if war enabled Colony
		public static bool IsWarEnabled(Colony colony)
		{
			if (colony.ColonyGroup.Owners.Count == 0) {
				return false;
			}
			bool result = false;
			for (int i = 0; i < colony.ColonyGroup.Owners.Count; ++i) {
				Players.Player owner = colony.ColonyGroup.Owners.GetAt(i);
				if (IsWarEnabled(owner)) {
					result = true;
				}
			}

			return result;
		}


		// enable war for a player. Also used to reset the timestamp to now
		public static void EnableWar(Players.Player player, int duration)
		{
			// war is only allowed within roleplay setting
			if (!RoleplayManager.IsRoleplaying(player)) {
				return;
			}
			if (duration < AntiGrief.config.WarDuration) {
				duration = AntiGrief.config.WarDuration;
			}
			warEntry entry = new warEntry(DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000, duration);
			warDict[player] = entry;

			// enable AngryGuards active mode for all colonies
			if (AntiGrief.AngryGuardsWarMode != null) {
				for (int i = 0; i < player.ColonyGroups.Count; ++i) {
					ColonyGroup colonyGroup = player.ColonyGroups.GetAt(i);
					for (int j = 0; j < colonyGroup.Colonies.Count; ++j) {
						Colony colony = colonyGroup.Colonies.GetAt(j);
						AntiGrief.AngryGuardsWarMode.Invoke(null, new object[]{colony, true});
					}
				}
			}

			RemovePlayerPermissions(player);	// ensure fair wars
		}


		// disable war for a player
		public static void DisableWar(Players.Player player)
		{
			warDict.Remove(player);

			if (player.ConnectionState == Players.EConnectionState.Connected) {
				BccChat.Send(player, "<color=yellow>Your WAR status expired. Do no longer attack others!</color>");
			}
			Chat.SendToConnectedBut(player, $"<color=yellow>WAR status of {ChatColors.GetDisplayName(player)} expired.</color>");

			// disable AngryGuards active mode for all colonies
			if (AntiGrief.AngryGuardsWarMode != null) {
				for (int i = 0; i < player.ColonyGroups.Count; ++i) {
					ColonyGroup colonyGroup = player.ColonyGroups.GetAt(i);
					for (int j = 0; j < colonyGroup.Colonies.Count; ++j) {
						AntiGrief.AngryGuardsWarMode.Invoke(null, new object[]{colonyGroup.Colonies.GetAt(j), false});
					}
				}
			}

			RestorePlayerPermissions(player);
			storedPermissions.Remove(player);
			SavePermissions();
		}


		// remove player permissions and set to default group (fair wars)
		public static void RemovePlayerPermissions(Players.Player target)
		{
			RoleTestManager.EndIfActive(target, "a war started");
			if (!target.ID.HasSteamID || !PermissionsManager.EnsureLoadedSuccesfully()) {
				return; // without SteamID the game stores no roles (and gives every permission anyway)
			}
			string group = PermissionsManager.Groups.ContainsKey(warPermissionGroup) ? warPermissionGroup : RoleManager.DefaultRole;
			if (string.IsNullOrEmpty(group) || !PermissionsManager.Groups.ContainsKey(group)) {
				Log.WriteWarning($"Better Colony Commands: no war permission group '{warPermissionGroup}' - {target.Name} keeps the roles");
				return;
			}
			// the way back is on disk before anything changes; a second war call keeps the first snapshot
			if (!storedPermissions.ContainsKey(target)) {
				storedPermissions[target] = new StoredRoles {
					Name = target.Name,
					HadEntry = PermissionsHelper.HasOwnEntry(target),
					Groups = PermissionsHelper.GetGroupNames(target)
				};
				SavePermissions();
			}
			PermissionsManager.SetGroupOfUser((Players.Player)null, target, group);
			BccPermissions.Refresh(true);
			if (target.ConnectionState == Players.EConnectionState.Connected) {
				BccChat.Send(target, $"<color=yellow>Permissions set to {group}</color>");
			}

			// remove flight state
			if (target.HasFlightMode) {
				target.SetFlightMode(false);
			}
		}


		// restore player permissions at end of war (exactly the roles before; own permissions are kept)
		public static void RestorePlayerPermissions(Players.Player target)
		{
			if (!storedPermissions.TryGetValue(target, out StoredRoles stored) || !PermissionsManager.EnsureLoadedSuccesfully()) {
				return;
			}
			PermissionsManager.Users.TryGetValue(target.ID.SteamID, out PermissionsManager.PermissionsGroup current);
			if (stored.HadEntry) {
				PermissionsManager.Users[target.ID.SteamID] = RoleTestManager.WithIncludes(current, stored.Groups);
			} else {
				PermissionsManager.Users.Remove(target.ID.SteamID); // back to the implicit default role
			}
			PermissionsManager.SaveUsers();
			BccPermissions.Refresh(true);
			string roles = stored.HadEntry ? string.Join(", ", stored.Groups) : RoleManager.DefaultRole + " (default)";
			if (target.ConnectionState == Players.EConnectionState.Connected) {
				BccChat.Send(target, $"<color=yellow>Your roles are back: {roles}</color>");
			}
			Log.Write($"Better Colony Commands: restored the roles of {target.Name} after the war: {roles}");
		}


		// end all wars
		public static void EndAllWars()
		{
			List<Players.Player> toDisable = new List<Players.Player>();
			foreach (KeyValuePair<Players.Player, warEntry> kvp in warDict) {
				Players.Player target = kvp.Key;
				toDisable.Add(target);
			}
			foreach (Players.Player target in toDisable) {
				DisableWar(target);
			}
			warDict.Clear();
		}


		// SAVE - stored roles as failsafe, so they are restored even after a crash
		public static void SavePermissions()
		{
			try {
				if (storedPermissions.Count == 0) {
					if (File.Exists(SaveFile)) {
						File.Delete(SaveFile);
					}
					return;
				}
				Dictionary<string, StoredRoles> data = new Dictionary<string, StoredRoles>();
				foreach (KeyValuePair<Players.Player, StoredRoles> kvp in storedPermissions) {
					if (kvp.Key.ID.HasSteamID) {
						data[kvp.Key.ID.SteamID.m_SteamID.ToString()] = kvp.Value;
					}
				}
				string tmp = SaveFile + ".tmp";
				File.WriteAllText(tmp, Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented));
				File.Copy(tmp, SaveFile, true);
				File.Delete(tmp);
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: could not save {SaveFile}: {e.Message}");
			}
		}


		// LOAD permissions file
		public static void LoadPermissionsFile()
		{
			try {
				Dictionary<string, StoredRoles> data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, StoredRoles>>(File.ReadAllText(SaveFile));
				foreach (KeyValuePair<string, StoredRoles> kvp in data ?? new Dictionary<string, StoredRoles>()) {
					if (ulong.TryParse(kvp.Key, out ulong steam) && Players.PlayerDatabaseBySteamID.TryGetValue(new Steamworks.CSteamID(steam), out Players.Player target)) {
						storedPermissions[target] = kvp.Value;
					} else {
						Log.WriteWarning($"Better Colony Commands: war roles of {kvp.Value?.Name} ({kvp.Key}) can't be restored - player not found");
					}
				}
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: error reading {SaveFile} - restore the roles manually: {e.Message}");
			}
		}


		// LOAD - ensure all permissions are restored at server startup
		public static void Load()
		{
			if (File.Exists(SaveFile)) {
				LoadPermissionsFile();
				foreach (Players.Player target in new List<Players.Player>(storedPermissions.Keys)) {
					RestorePlayerPermissions(target);
				}
				storedPermissions.Clear();
				File.Delete(SaveFile);
				Log.Write($"Better Colony Commands: roles of players in war mode restored at start ({SaveFile} deleted)");
			}
			CheckWarStatus();
		}


		// check war status and disable after timeout
		public static void CheckWarStatus()
		{
			long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000;
			List<Players.Player> toDisable = new List<Players.Player>();

			foreach (KeyValuePair<Players.Player, warEntry> kvp in warDict) {
				Players.Player target = kvp.Key;
				warEntry entry = kvp.Value;
				if (entry.started + entry.duration <= now) {
					toDisable.Add(target);
				}
			}

			foreach (Players.Player target in toDisable) {
				DisableWar(target);
			}

			ThreadManager.InvokeOnMainThread(delegate() {
				CheckWarStatus();
			}, 65.350);

			return;
		}


	}	// end of class
} // namespace

