using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Pipliz;

namespace ColonyCommands
{
	// Which roles every player really has - read from the game's live permission data (the same data
	// the game checks permissions against), so it is never out of date:
	//  Direct    - the role is in the player's own entry ("includes" in permissionusers.json)
	//  Default   - the default role: either the player has no own entry yet, or the game created the
	//              entry from a COPY of the default role (its permissions, e.g. betterchat.<default>,
	//              were copied as own permissions - the role name itself is not stored). This is how
	//              most players hold the default role once they got any other role.
	//  Inherited - through another role the player has (a role has every role it includes)
	// The chat role tag (betterchat.<role> permission) sees all three, so lists and buttons now match it.
	// Built once at server start and then on demand (cached for a few seconds, cleared on every change).
	public static class RoleIndex
	{
		public enum HoldKind { Direct, Default, Inherited }

		public class Holding
		{
			public string Role;
			public HoldKind Kind;
			public string Via = "";        // Inherited: the role it comes through
			public bool Implicit;          // Default without own entry
		}

		public class Holder
		{
			public Players.Player Player;
			public Holding Holding;
		}

		const double CACHE_SECONDS = 5;
		static Dictionary<ulong, (Players.Player player, List<Holding> roles)> cache;
		static DateTime builtAt = DateTime.MinValue;

		public static void Invalidate()
		{
			cache = null;
		}

		public static void AfterWorldLoad()
		{
			try {
				Build();
				int entries = cache.Count(c => PermissionsHelper.HasOwnEntry(c.Value.player));
				string def = RoleManager.DefaultRole;
				int defCount = cache.Values.Count(c => c.roles.Any(h => h.Kind == HoldKind.Default));
				Log.Write($"Better Colony Commands: roles of {cache.Count} players read ({entries} with own role entry, {defCount} hold the default role {def})");
			} catch (Exception e) {
				Log.WriteException("Better Colony Commands: could not read the players' roles", e);
			}
		}

		// ------------------------------------------------------------------ queries

		// the roles of one player, always fresh
		public static List<Holding> Of(Players.Player player)
		{
			if (player == null || !player.ID.HasSteamID || !PermissionsManager.EnsureLoadedSuccesfully()) {
				return new List<Holding>();
			}
			PermissionsManager.Users.TryGetValue(player.ID.SteamID, out PermissionsManager.PermissionsGroup entry);
			return Compute(entry, DefaultInfo());
		}

		public static Holding Find(Players.Player player, string role)
		{
			return Of(player).FirstOrDefault(h => string.Equals(h.Role, role, StringComparison.OrdinalIgnoreCase));
		}

		// every player that has the role (direct and default first, then inherited)
		public static List<Holder> HoldersOf(string role)
		{
			List<Holder> result = new List<Holder>();
			foreach ((Players.Player player, List<Holding> roles) in All().Values) {
				Holding h = roles.FirstOrDefault(x => string.Equals(x.Role, role, StringComparison.OrdinalIgnoreCase));
				if (h != null) {
					result.Add(new Holder { Player = player, Holding = h });
				}
			}
			return result;
		}

		// players the role is assigned to (direct or as default role), without inherited ones
		public static int AssignedCount(string role)
		{
			return All().Values.Count(v => v.roles.Any(h => h.Kind != HoldKind.Inherited && string.Equals(h.Role, role, StringComparison.OrdinalIgnoreCase)));
		}

		public static int InheritedCount(string role)
		{
			return All().Values.Count(v => v.roles.Any(h => h.Kind == HoldKind.Inherited && string.Equals(h.Role, role, StringComparison.OrdinalIgnoreCase)));
		}

		// role names the player holds directly or as default role (what /groups and home limits use)
		public static List<string> AssignedRoles(Players.Player player)
		{
			return Of(player).Where(h => h.Kind != HoldKind.Inherited).Select(h => h.Role).ToList();
		}

		public static string Describe(Holding h)
		{
			if (h == null) {
				return "";
			}
			switch (h.Kind) {
				case HoldKind.Default: return h.Implicit ? "default role (no own entry)" : "default role";
				case HoldKind.Inherited: return "through " + ChatColors.FormatRole(h.Via);
				default: return "assigned";
			}
		}

		// ------------------------------------------------------------------ changes

		// takes a role away, whichever way the player has it; null = done, otherwise why not
		public static string RemoveRole(Players.Player changer, Players.Player target, string role)
		{
			if (!target.ID.HasSteamID || !PermissionsManager.EnsureLoadedSuccesfully()) {
				return UiPlayerPages.NoSteamText;
			}
			Holding h = Find(target, role);
			if (h == null) {
				return $"{target.Name} doesn't have the role {role}.";
			}
			if (h.Kind == HoldKind.Inherited) {
				return $"{target.Name} has {role} through {h.Via} - remove {h.Via} instead.";
			}
			PermissionsManager.Users.TryGetValue(target.ID.SteamID, out PermissionsManager.PermissionsGroup entry);
			JObject j = entry != null ? JObject.FromObject(entry) : new JObject();
			List<string> includes = ReadList(j, "includes");
			List<string> own = ReadList(j, "permissions");
			// every copy of the role name (the game's own remove only takes the first one)
			includes.RemoveAll(g => string.Equals(g, role, StringComparison.OrdinalIgnoreCase));
			(string defName, List<string> defPerms) = DefaultInfo();
			List<string> stripped = new List<string>();
			if (string.Equals(role, defName, StringComparison.OrdinalIgnoreCase) && IsDefaultCopy(own, (defName, defPerms))) {
				// the game's copy of the default role: its permissions go away too, or the player keeps it
				HashSet<string> copied = new HashSet<string>(defPerms.Where(p => p.Length > 0), StringComparer.Ordinal);
				stripped = own.Where(p => copied.Contains(p) || string.Equals(p, MarkerOf(defName), StringComparison.OrdinalIgnoreCase)).ToList();
				own.RemoveAll(p => stripped.Contains(p));
			}
			j["includes"] = new JArray(includes.Cast<object>().ToArray());
			j["permissions"] = new JArray(own.Cast<object>().ToArray());   // never null - see RoleTestManager
			PermissionsManager.Users[target.ID.SteamID] = j.ToObject<PermissionsManager.PermissionsGroup>();
			PermissionsManager.SaveUsers();
			BccPermissions.Refresh(true);
			Invalidate();
			Log.Write($"Better Colony Commands: {changer?.Name ?? "server"} removed the role {role} ({TextHelper.StripRichText(Describe(h))}) from {target.Name}"
				+ (stripped.Count > 0 ? $" - with the permissions the game had copied from it: {string.Join(", ", stripped)}" : ""));
			return null;
		}

		// The game adds a role to a player without checking whether it is already there, and its remove
		// takes away only the first copy - so a role could be in a player's entry twice. Removes the
		// extra copies (keeps the spelling of the existing role). All players at start, a player on join.
		public static int CleanDuplicates(Players.Player only = null)
		{
			if (!PermissionsManager.EnsureLoadedSuccesfully()) {
				return 0;
			}
			int changed = 0;
			List<string> report = new List<string>();
			foreach (Steamworks.CSteamID id in PermissionsManager.Users.Keys.ToList()) {
				if (only != null && (!only.ID.HasSteamID || only.ID.SteamID != id)) {
					continue;
				}
				JObject j = JObject.FromObject(PermissionsManager.Users[id]);
				List<string> includes = ReadList(j, "includes");
				List<string> clean = includes
					.GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
					.Select(grp => grp.FirstOrDefault(g => PermissionsManager.Groups.ContainsKey(g)) ?? grp.First())
					.ToList();
				if (clean.Count == includes.Count) {
					continue;
				}
				j["includes"] = new JArray(clean.Cast<object>().ToArray());
				if (j["permissions"] == null || j["permissions"].Type != JTokenType.Array) {
					j["permissions"] = new JArray();
				}
				PermissionsManager.Users[id] = j.ToObject<PermissionsManager.PermissionsGroup>();
				changed++;
				report.Add($"{id.m_SteamID}: {string.Join(",", includes)} -> {string.Join(",", clean)}");
			}
			if (changed > 0) {
				PermissionsManager.SaveUsers();
				BccPermissions.Refresh(true);
				Invalidate();
				Log.Write($"Better Colony Commands: removed duplicate roles of {changed} player(s): {string.Join("; ", report.Take(20))}{(report.Count > 20 ? " ..." : "")}");
			}
			return changed;
		}

		// ------------------------------------------------------------------ building

		static Dictionary<ulong, (Players.Player player, List<Holding> roles)> All()
		{
			if (cache == null || (DateTime.UtcNow - builtAt).TotalSeconds > CACHE_SECONDS) {
				Build();
			}
			return cache;
		}

		static void Build()
		{
			Dictionary<ulong, (Players.Player, List<Holding>)> result = new Dictionary<ulong, (Players.Player, List<Holding>)>();
			builtAt = DateTime.UtcNow;
			if (!PermissionsManager.EnsureLoadedSuccesfully()) {
				cache = result;
				return;
			}
			(string, List<string>) def = DefaultInfo();
			foreach (Players.Player p in Players.PlayerDatabase.Values) {
				if (p == null || !p.ID.HasSteamID || Players.IsServerID(p.ID)) {
					continue;
				}
				ulong id = p.ID.SteamID.m_SteamID;
				// several profiles of one Steam account share its roles: keep the one seen last
				if (result.TryGetValue(id, out (Players.Player player, List<Holding> roles) existing)
					&& UiPlayerPages.LastSeen(existing.player) >= UiPlayerPages.LastSeen(p)) {
					continue;
				}
				PermissionsManager.Users.TryGetValue(p.ID.SteamID, out PermissionsManager.PermissionsGroup entry);
				result[id] = (p, Compute(entry, def));
			}
			cache = result;
		}

		static List<Holding> Compute(PermissionsManager.PermissionsGroup entry, (string name, List<string> perms) def)
		{
			List<Holding> result = new List<Holding>();
			bool Has(string r) => result.Any(h => string.Equals(h.Role, r, StringComparison.OrdinalIgnoreCase));

			if (entry == null) {
				if (RoleManager.Get(def.name) != null) {
					result.Add(new Holding { Role = RoleManager.Get(def.name).Name, Kind = HoldKind.Default, Implicit = true });
				}
			} else {
				JObject j = JObject.FromObject(entry);
				List<string> own = ReadList(j, "permissions");
				foreach (string g in ReadList(j, "includes")) {
					RoleManager.Role r = RoleManager.Get(g);
					if (r != null && !Has(r.Name)) {
						result.Add(new Holding { Role = r.Name, Kind = HoldKind.Direct });
					}
				}
				RoleManager.Role d = RoleManager.Get(def.name);
				if (d != null && !Has(d.Name) && IsDefaultCopy(own, def)) {
					result.Add(new Holding { Role = d.Name, Kind = HoldKind.Default });
				}
			}
			// inherited through the roles above
			foreach (Holding a in result.ToList()) {
				foreach (string sub in RoleManager.Descendants(a.Role)) {
					RoleManager.Role r = RoleManager.Get(sub);
					if (r != null && !Has(r.Name)) {
						result.Add(new Holding { Role = r.Name, Kind = HoldKind.Inherited, Via = a.Role });
					}
				}
			}
			return result;
		}

		// own permissions that came from the game copying the default role
		static bool IsDefaultCopy(List<string> own, (string name, List<string> perms) def)
		{
			if (own.Count == 0) {
				return false;
			}
			string marker = MarkerOf(def.name);
			if (own.Any(p => string.Equals(p, marker, StringComparison.OrdinalIgnoreCase))) {
				return true;
			}
			List<string> perms = def.perms.Where(p => p.Length > 0).ToList();
			return perms.Count > 0 && perms.All(p => own.Contains(p));
		}

		static string MarkerOf(string role) => AntiGrief.MOD_PREFIX + "betterchat." + role;

		static (string name, List<string> perms) DefaultInfo()
		{
			string name = RoleManager.DefaultRole ?? "";
			RoleManager.Role r = RoleManager.Get(name);
			return (r?.Name ?? name, r != null ? new List<string>(r.Permissions) : new List<string>());
		}

		static List<string> ReadList(JObject j, string key)
		{
			JToken t = j[key];
			return t == null || t.Type != JTokenType.Array ? new List<string>() : t.Where(x => x.Type == JTokenType.String).Select(x => x.Value<string>()).ToList();
		}
	}
}
