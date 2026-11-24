using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Pipliz;

namespace ColonyCommands
{
	// Trust on a claim (the banner area of a colony): a trusted player may build there and place banners
	// close to it, like an owner as far as Anti-Grief is concerned (stockpile and jobs stay with "Manage
	// Owners"). Stored as own permission of the trusted player:
	//   mods.scarabol.antigrief.banner.colony.0000000005   - this claim (written by the trust windows)
	//   mods.scarabol.antigrief.banner.<owner steamid>     - every claim of that owner (/antigrief banner)
	// Angry Guards (optional): its friend list belongs to an owner and protects the player from the guards
	// of all that owner's colonies - read and changed through its public PlayerTracker by reflection.
	public static class TrustManager
	{
		public enum TrustKind { Claim, AllOfOwner }

		public class TrustEntry
		{
			public Players.Player Player;
			public TrustKind Kind;
			public Players.Player Owner;   // AllOfOwner: whose claims
		}

		// ------------------------------------------------------------------ permissions

		// zero padded: the game matches permissions by prefix, "colony.5" would also cover colony 50, 512, ...
		public static string ClaimPermission(Colony c) => AntiGrief.PERMISSION_BANNER_PREFIX + "colony." + c.ColonyID.Value.ToString("0000000000");

		// what older versions checked (ColonyID ignores the number format) - only accepted as exact match
		static string LegacyClaimPermission(Colony c) => AntiGrief.PERMISSION_BANNER_PREFIX + "colony." + c.ColonyID.Value;

		public static string OwnerPermission(Players.Player owner) => AntiGrief.PERMISSION_BANNER_PREFIX + owner.ID.SteamID.m_SteamID;

		public static bool IsOwner(Players.Player p, Colony c)
		{
			if (p == null || c?.ColonyGroup == null) {
				return false;
			}
			for (int i = 0; i < c.ColonyGroup.Owners.Count; i++) {
				if (c.ColonyGroup.Owners.GetAt(i) == p) {
					return true;
				}
			}
			return false;
		}

		// used by Anti-Grief for every block change in a banner area
		public static bool IsTrusted(Players.Player p, Colony c)
		{
			if (p == null || c?.ColonyGroup == null) {
				return false;
			}
			if (PermissionsManager.HasPermission(p, ClaimPermission(c)) || PermissionsManager.HasPermissionExact(p, LegacyClaimPermission(c))) {
				return true;
			}
			for (int i = 0; i < c.ColonyGroup.Owners.Count; i++) {
				Players.Player owner = c.ColonyGroup.Owners.GetAt(i);
				if (owner != null && owner.ID.HasSteamID && PermissionsManager.HasPermission(p, OwnerPermission(owner))) {
					return true;
				}
			}
			return false;
		}

		public static bool CanManage(Players.Player viewer, Colony c)
		{
			return viewer != null && c != null && (IsOwner(viewer, c) || PermissionsManager.HasPermission(viewer, AntiGrief.PERMISSION_SUPER));
		}

		// ------------------------------------------------------------------ colonies

		public static Colony GetColony(string id)
		{
			if (!int.TryParse(id, out int value)) {
				return null;
			}
			return ServerManager.ColonyTracker.ColoniesByID.TryGetValue(new ColonyID(value), out Colony c) ? c : null;
		}

		public static List<Colony> OwnColonies(Players.Player p)
		{
			List<Colony> all = new List<Colony>();
			var e = ServerManager.ColonyTracker.ColoniesByID.GetValueEnumerator();
			while (e.MoveNext()) {
				all.Add(e.Current);
			}
			return all
				.Where(c => IsOwner(p, c))
				.OrderBy(c => TextHelper.StripRichText(c.Name ?? ""), StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		public static string Describe(Colony c)
		{
			string name = TextHelper.SafeName(string.IsNullOrEmpty(c.Name) ? "(no name)" : c.Name);
			string where = c.Banners.Count > 0
				? $"banner at {(int)c.Banners.GetAt(0).Position.x} {(int)c.Banners.GetAt(0).Position.y} {(int)c.Banners.GetAt(0).Position.z}"
				: "no banner";
			return $"{name}  {Style.Muted(where)}";
		}

		// ------------------------------------------------------------------ who is trusted

		public static List<TrustEntry> TrustedOn(Colony c)
		{
			List<TrustEntry> result = new List<TrustEntry>();
			if (c == null || !PermissionsManager.EnsureLoadedSuccesfully()) {
				return result;
			}
			string claim = ClaimPermission(c), legacy = LegacyClaimPermission(c);
			Dictionary<string, Players.Player> owners = new Dictionary<string, Players.Player>();
			for (int i = 0; i < c.ColonyGroup.Owners.Count; i++) {
				Players.Player o = c.ColonyGroup.Owners.GetAt(i);
				if (o != null && o.ID.HasSteamID) {
					owners[OwnerPermission(o)] = o;
				}
			}
			foreach (KeyValuePair<Steamworks.CSteamID, PermissionsManager.PermissionsGroup> kvp in PermissionsManager.Users) {
				List<string> own = OwnPermissions(kvp.Value);
				if (own.Count == 0 || !Players.PlayerDatabaseBySteamID.TryGetValue(kvp.Key, out Players.Player p) || IsOwner(p, c)) {
					continue;
				}
				if (own.Contains(claim) || own.Contains(legacy)) {
					result.Add(new TrustEntry { Player = p, Kind = TrustKind.Claim });
					continue;
				}
				string viaOwner = own.FirstOrDefault(x => owners.ContainsKey(x));
				if (viaOwner != null) {
					result.Add(new TrustEntry { Player = p, Kind = TrustKind.AllOfOwner, Owner = owners[viaOwner] });
				}
			}
			return result
				.OrderBy(e => UiPlayerPages.IsOnline(e.Player) ? 0 : 1)
				.ThenBy(e => TextHelper.StripRichText(e.Player.Name ?? ""), StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		public static TrustEntry Find(Players.Player p, Colony c)
		{
			return TrustedOn(c).FirstOrDefault(e => e.Player == p);
		}

		// ------------------------------------------------------------------ changes

		// null = done, otherwise why not
		public static string Trust(Players.Player by, Players.Player target, Colony c, bool notify = true)
		{
			if (!CanManage(by, c)) {
				return "Only the owners of this colony can change its permissions.";
			}
			if (target == null || !target.ID.HasSteamID) {
				return UiPlayerPages.NoSteamText;
			}
			if (IsOwner(target, c)) {
				return $"{target.Name} is an owner of this colony.";
			}
			if (IsTrusted(target, c)) {
				return $"{target.Name} is already trusted here.";
			}
			AddOwnPermission(target, ClaimPermission(c));
			Log.Write($"Better Colony Commands: {by.Name} trusted {target.Name} on colony {TextHelper.StripRichText(c.Name ?? "")} ({c.ColonyID.Value})");
			if (notify && UiPlayerPages.IsOnline(target)) {
				BccChat.Send(target, Style.Success($"{ChatColors.GetDisplayName(by)} trusted you on {TextHelper.SafeName(c.Name ?? "")}: you may build in its banner area now."));
			}
			return null;
		}

		public static string Untrust(Players.Player by, Players.Player target, Colony c, bool notify = true)
		{
			if (!CanManage(by, c)) {
				return "Only the owners of this colony can change its permissions.";
			}
			TrustEntry e = Find(target, c);
			if (e == null) {
				return $"{target?.Name} is not trusted here (or only through a role).";
			}
			if (e.Kind == TrustKind.AllOfOwner) {
				// "/antigrief banner": all claims of one owner - only that owner (or staff) may take it back
				if (e.Owner != by && !PermissionsManager.HasPermission(by, AntiGrief.PERMISSION_SUPER)) {
					return $"{target.Name} is trusted on all claims of {e.Owner.Name} - only {e.Owner.Name} can take that back.";
				}
				RemoveOwnPermission(target, OwnerPermission(e.Owner));
			} else {
				RemoveOwnPermission(target, ClaimPermission(c));
				RemoveOwnPermission(target, LegacyClaimPermission(c));
			}
			Log.Write($"Better Colony Commands: {by.Name} removed the trust of {target.Name} on colony {TextHelper.StripRichText(c.Name ?? "")} ({c.ColonyID.Value}){(e.Kind == TrustKind.AllOfOwner ? " (was: all claims of " + e.Owner.Name + ")" : "")}");
			if (notify && UiPlayerPages.IsOnline(target)) {
				BccChat.Send(target, Style.Muted($"{ChatColors.GetDisplayName(by)} removed your permission to build at {TextHelper.SafeName(c.Name ?? "")}."));
			}
			return null;
		}

		// own permission without the game's chat messages ("You gained permission [...]")
		static void AddOwnPermission(Players.Player target, string permission)
		{
			if (!PermissionsManager.EnsureLoadedSuccesfully()) {
				return;
			}
			if (!PermissionsManager.Users.TryGetValue(target.ID.SteamID, out PermissionsManager.PermissionsGroup g)) {
				g = PermissionsManager.DefaultNormal.Copy();   // exactly what the game does for a player without entry
			}
			if (!OwnPermissions(g).Contains(permission)) {
				g.AddPermission(permission);
			}
			PermissionsManager.Users[target.ID.SteamID] = g;
			PermissionsManager.SaveUsers();
			BccPermissions.Refresh(true);
		}

		static void RemoveOwnPermission(Players.Player target, string permission)
		{
			if (!PermissionsManager.EnsureLoadedSuccesfully() || !PermissionsManager.Users.TryGetValue(target.ID.SteamID, out PermissionsManager.PermissionsGroup g)) {
				return;
			}
			bool changed = false;
			for (int i = 0; i < 20 && OwnPermissions(g).Contains(permission); i++) {
				changed |= g.RemovePermission(permission);
			}
			if (changed) {
				PermissionsManager.SaveUsers();
				BccPermissions.Refresh(true);
			}
		}

		static List<string> OwnPermissions(PermissionsManager.PermissionsGroup g)
		{
			JToken t = JObject.FromObject(g)["permissions"];
			return t == null || t.Type != JTokenType.Array ? new List<string>() : t.Where(x => x.Type == JTokenType.String).Select(x => x.Value<string>()).ToList();
		}

		// ------------------------------------------------------------------ Angry Guards (optional)

		static bool guardsResolved;
		static MethodInfo agAdd, agRemove, agIsFriendly;

		public static bool GuardsAvailable {
			get {
				ResolveGuards();
				return agAdd != null && agRemove != null && agIsFriendly != null;
			}
		}

		static void ResolveGuards()
		{
			if (guardsResolved) {
				return;
			}
			guardsResolved = true;
			try {
				Type tracker = AppDomain.CurrentDomain.GetAssemblies()
					.Where(a => a.GetName().Name == "AngryGuards")
					.Select(a => a.GetType("AngryGuards.PlayerTracker", false))
					.FirstOrDefault(t => t != null);
				if (tracker == null) {
					return;
				}
				Type[] two = { typeof(Players.Player), typeof(Players.Player) };
				agAdd = tracker.GetMethod("AddFriendly", BindingFlags.Public | BindingFlags.Static, null, two, null);
				agRemove = tracker.GetMethod("RemoveFriendly", BindingFlags.Public | BindingFlags.Static, null, two, null);
				agIsFriendly = tracker.GetMethod("IsFriendly", BindingFlags.Public | BindingFlags.Static, null, two, null);
				Log.Write($"Better Colony Commands: Angry Guards friend list {(GuardsAvailable ? "found - trust windows can change it" : "not usable")}");
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: Angry Guards not usable: {e.Message}");
			}
		}

		public static bool IsFriendly(Players.Player owner, Players.Player target)
		{
			try {
				return GuardsAvailable && owner != null && target != null && (bool)agIsFriendly.Invoke(null, new object[] { owner, target });
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: Angry Guards IsFriendly failed: {e.InnerException?.Message ?? e.Message}");
				return false;
			}
		}

		// friendly for the guards of this colony = friend of any of its owners
		public static bool IsFriendlyFor(Colony c, Players.Player target)
		{
			for (int i = 0; i < c.ColonyGroup.Owners.Count; i++) {
				if (IsFriendly(c.ColonyGroup.Owners.GetAt(i), target)) {
					return true;
				}
			}
			return false;
		}

		public static void SetFriendly(Players.Player owner, Players.Player target, bool friendly)
		{
			if (!GuardsAvailable || owner == null || target == null || owner == target) {
				return;
			}
			try {
				if (friendly && !IsFriendly(owner, target)) {
					agAdd.Invoke(null, new object[] { owner, target });
				} else if (!friendly && IsFriendly(owner, target)) {
					agRemove.Invoke(null, new object[] { owner, target });
				}
				Log.Write($"Better Colony Commands: Angry Guards - {target.Name} is now {(friendly ? "friendly" : "not friendly")} for the guards of {owner.Name}");
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: Angry Guards friend list change failed: {e.InnerException?.Message ?? e.Message}");
			}
		}

		// guards of one colony: on = friend of the acting owner, off = no owner has the player as friend
		public static void SetFriendlyFor(Colony c, Players.Player actingOwner, Players.Player target, bool friendly)
		{
			if (friendly) {
				SetFriendly(IsOwner(actingOwner, c) ? actingOwner : c.ColonyGroup.Owners.GetAt(0), target, true);
				return;
			}
			for (int i = 0; i < c.ColonyGroup.Owners.Count; i++) {
				SetFriendly(c.ColonyGroup.Owners.GetAt(i), target, false);
			}
		}
	}
}
