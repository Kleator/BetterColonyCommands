using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chatting;
using Newtonsoft.Json.Linq;
using NetworkUI;
using NetworkUI.Items;
using Pipliz;
using UnityEngine;

namespace ColonyCommands
{
	// Player moderation (permission mods.scarabol.commands.playermoderation to open; every action
	// additionally needs its own permission, buttons without it are disabled):
	//  players      - every player of the server, last online first, search + filters, ban/jail/mute
	//                 status and incident count at a glance
	//  player       - one player: status, moderation actions (kick, mute, jail, ban), online time,
	//                 money, roles, own permissions, colonies & banners (+ Pytonia colony limit),
	//                 homes and the complete moderation history with notes
	//  playerban    - ban confirmation (banning also deletes colonies the player owns alone)
	//  colonydelete - colony delete confirmation
	public static class UiPlayerPages
	{
		public const string PERMISSION = AntiGrief.MOD_PREFIX + "playermoderation";
		const string P = AntiGrief.MOD_PREFIX;

		static int W => UiManager.ContentWidth;

		// Moderators only: players need mods.scarabol.commands.playermoderation (groups with the
		// all-permission "" have it). Note: in singleplayer and LAN ("LocalHost") the game connects
		// players without SteamID and gives them every permission - that's the game's own rule.
		public static bool CanOpen(Players.Player player) => player != null && PermissionsManager.HasPermission(player, PERMISSION);

		public const string NoSteamText = "This player has no SteamID (singleplayer or LAN game): the game can't store roles or own permissions for them - and gives them every permission anyway. Roles work on a server or when the world is hosted for friends.";
		static bool Has(Players.Player player, string permission) => PermissionsManager.HasPermission(player, permission);

		public static Players.Player FindPlayer(string key)
		{
			if (string.IsNullOrEmpty(key)) {
				return null;
			}
			foreach (Players.Player p in Players.PlayerDatabase.Values) {
				if (p != null && p.ID.ToStringReadable() == key) {
					return p;
				}
			}
			return null;
		}

		public static bool IsOnline(Players.Player p) => p.ConnectionState == Players.EConnectionState.Connected;

		public static DateTime LastSeen(Players.Player p)
		{
			if (IsOnline(p)) {
				return DateTime.MaxValue;
			}
			return DateTime.TryParse(ActivityTracker.GetLastSeen(p.ID.ToStringReadable()), out DateTime t) ? t : DateTime.MinValue;
		}

		static string Ago(DateTime t)
		{
			if (t == DateTime.MaxValue) return Style.Success("online");
			if (t == DateTime.MinValue) return Style.Muted("never seen");
			TimeSpan d = DateTime.Now - t;
			string text = d.TotalDays >= 1 ? $"{(int)d.TotalDays}d ago" : d.TotalHours >= 1 ? $"{(int)d.TotalHours}h ago" : $"{System.Math.Max(0, (int)d.TotalMinutes)}m ago";
			return Style.Muted(text);
		}

		static List<string> Badges(Players.Player p, int incidents)
		{
			List<string> b = new List<string>();
			if (ModerationLog.IsBanned(p)) b.Add("<b><color=#FF6B6B>BANNED</color></b>");
			if (JailManager.IsPlayerJailed(p)) b.Add("<b><color=#FFB347>JAILED</color></b>");
			if (ModerationLog.IsMuted(p)) b.Add("<color=#FFB347>muted</color>");
			if (RoleplayManager.IsRoleplayBanned(p)) b.Add("<color=#FFB347>rp-banned</color>");
			if (incidents > 0) b.Add($"<color=#FFD166>{incidents} incident(s)</color>");
			return b;
		}

		// ------------------------------------------------------------------ overview

		public static void BuildList(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "Player moderation");
			if (!CanOpen(player)) {
				menu.Items.Add(UiManager.Text(Style.Error("You don't have permission to moderate players.")));
				return;
			}
			UiManager.PermissionNotice(menu, player);
			UiManager.Messages(menu, state);
			UiManager.FilterRow(menu, state.Filter, "Search:");
			string mode = string.IsNullOrEmpty(state.Sort) ? "all" : state.Sort;
			int bw = 140;
			menu.Items.Add(UiManager.Row(32,
				(UiManager.Text(Style.Muted("Show:"), 30, 14), 60),
				(UiManager.Button("sort", "all", (mode == "all" ? "> " : "") + "All", bw - 4, mode != "all"), bw),
				(UiManager.Button("sort", "online", (mode == "online" ? "> " : "") + "Online", bw - 4, mode != "online"), bw),
				(UiManager.Button("sort", "flagged", (mode == "flagged" ? "> " : "") + "With incidents", bw - 4, mode != "flagged"), bw),
				(UiManager.Button("sort", "banned", (mode == "banned" ? "> " : "") + "Banned", bw - 4, mode != "banned"), bw)));

			Dictionary<string, int> counts = ModerationLog.IncidentCounts();
			string f = (state.Filter ?? "").Trim().ToLowerInvariant();
			List<(Players.Player p, DateTime seen, int incidents)> rows = new List<(Players.Player, DateTime, int)>();
			foreach (Players.Player p in Players.PlayerDatabase.Values) {
				if (p == null || Players.IsServerID(p.ID)) {
					continue;
				}
				if (f.Length > 0 && !(p.Name ?? "").ToLowerInvariant().Contains(f) && !p.ID.ToStringReadable().Contains(f)) {
					continue;
				}
				int inc = ModerationLog.IncidentCount(counts, p);
				if (mode == "online" && !IsOnline(p)) continue;
				if (mode == "flagged" && inc == 0) continue;
				if (mode == "banned" && !ModerationLog.IsBanned(p)) continue;
				rows.Add((p, LastSeen(p), inc));
			}
			// last online first
			rows = rows.OrderByDescending(r => r.seen).ThenBy(r => r.p.Name, StringComparer.OrdinalIgnoreCase).ToList();
			var page = TextHelper.Page(rows, state.Page, UiManager.LinesPerPage, out int pg, out int pages);
			state.Page = pg;
			state.Lists["pedit"] = page.Select(r => r.p.ID.ToStringReadable()).ToList();
			menu.Items.Add(UiManager.Text(Style.Muted($"{rows.Count} player(s)"), 22, 13));
			for (int i = 0; i < page.Count; i++) {
				var r = page[i];
				string playtime = TextHelper.FormatDuration(ActivityTracker.GetLiveSecondsPlayed(r.p));
				string shownName = string.IsNullOrEmpty(r.p.Name) ? Style.Muted("(no name) " + r.p.ID.ToStringReadable()) : ChatColors.GetDisplayName(r.p);
				string info = $"{shownName}  {Ago(r.seen)}  {Style.Muted(playtime + " played")}";
				List<string> badges = Badges(r.p, r.incidents);
				menu.Items.Add(new BackgroundColor(UiManager.Row(32,
					(UiManager.Text(info, 30, 14), W - 120 - 330),
					(UiManager.Text(string.Join("  ", badges), 30, 13), 330),
					(UiManager.Button("act", "pedit:" + i, "Edit", 104), 110)), W, 36, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			UiManager.Pager(menu, pg, pages);
		}

		// ------------------------------------------------------------------ one player

		static void Section(NetworkMenu menu, string title)
		{
			menu.Items.Add(new EmptySpace(4));
			UiManager.SectionHeader(menu, title);
		}

		static void Hint(NetworkMenu menu, string text) => menu.Items.Add(UiManager.Text(Style.Muted(text), -1, 12));

		static List<Colony> ColoniesOf(Players.Player p)
		{
			List<Colony> list = new List<Colony>();
			for (int i = 0; i < p.ColonyGroups.Count; i++) {
				ColonyGroup g = p.ColonyGroups[i];
				for (int j = 0; j < g.Colonies.Count; j++) {
					list.Add(g.Colonies.GetAt(j));
				}
			}
			return list;
		}

		static Colony FindColony(string id)
		{
			if (!int.TryParse(id, out int value)) {
				return null;
			}
			var e = ServerManager.ColonyTracker.ColoniesByID.GetValueEnumerator();
			while (e.MoveNext()) {
				if (e.Current != null && e.Current.ColonyID.Value == value) {
					return e.Current;
				}
			}
			return null;
		}

		public static void BuildPlayer(Players.Player admin, UiManager.PageState state, NetworkMenu menu)
		{
			Players.Player p = FindPlayer(state.Key);
			UiManager.Header(admin, menu, p != null ? "Player: " + TextHelper.SafeName(p.Name) : "Player");
			if (!CanOpen(admin)) {
				menu.Items.Add(UiManager.Text(Style.Error("You don't have permission to moderate players.")));
				return;
			}
			if (p == null) {
				menu.Items.Add(UiManager.Text(Style.Error("Player not found.")));
				return;
			}
			UiManager.PermissionNotice(menu, admin);
			UiManager.Messages(menu, state);
			string key = p.ID.ToStringReadable();
			int incidents = ModerationLog.IncidentCount(ModerationLog.IncidentCounts(), p);

			// --- status
			List<string> badges = Badges(p, incidents);
			menu.Items.Add(UiManager.Text($"{ChatColors.GetDisplayName(p)}   {(IsOnline(p) ? Style.Success("online") : Style.Muted("last seen " + ActivityTracker.GetLastSeen(key)))}   {string.Join("  ", badges)}", 28, 16));
			Vector3 pos = p.PositionStanding;
			menu.Items.Add(UiManager.Text(Style.Muted($"SteamID {(p.ID.HasSteamID ? p.ID.SteamID.m_SteamID.ToString() : "-")}  ·  key {key}  ·  position {(int)pos.x} {(int)pos.y} {(int)pos.z}  ·  {ColoniesOf(p).Count} colonies"), -1, 13));
			BanLogRecord ban = ModerationLog.ActiveBan(p);
			if (ban != null) {
				DateTime until = new DateTime((ban.timestamp + (long)ban.duration * 86400) * TimeSpan.TicksPerSecond);
				menu.Items.Add(UiManager.Text(Style.Error($"Banned until {until:yyyy-MM-dd HH:mm}: {ban.reason}"), -1, 14));
			} else if (ModerationLog.IsBanned(p)) {
				menu.Items.Add(UiManager.Text(Style.Error("On the server blacklist (banned without end date)."), -1, 14));
			}
			if (JailManager.IsPlayerJailed(p)) {
				menu.Items.Add(UiManager.Text($"<color=#FFB347>In jail, {TextHelper.FormatDuration(JailManager.getRemainingTime(p))} left: {JailManager.jailedPersons[p].jailReason}</color>", -1, 14));
			}

			// --- actions
			Section(menu, "Moderation");
			bool online = IsOnline(p);
			bool muted = ModerationLog.IsMuted(p);
			bool jailed = JailManager.IsPlayerJailed(p);
			bool banned = ModerationLog.IsBanned(p);
			menu.LocalStorage.SetAs("m_reason", "");
			menu.LocalStorage.SetAs("m_minutes", JailManager.jailConfig.defaultJailTime > 0 ? JailManager.jailConfig.defaultJailTime.ToString() : "10");
			menu.LocalStorage.SetAs("m_days", "7");
			menu.Items.Add(UiManager.Row(32,
				(UiManager.Button("act", "ptp", "Teleport to player", 196, Has(admin, P + "warp.self")), 200),
				(UiManager.Button("act", "pkick", "Kick", 120, online && Has(admin, P + "kick")), 124),
				(UiManager.Button("act", muted ? "punmute" : "pmute", muted ? "Unmute" : "Mute", 120, Has(admin, P + "mute")), 124),
				(UiManager.Button("act", jailed ? "prelease" : "pjail", jailed ? "Release" : "Jail", 120, Has(admin, P + "jail") && (jailed || JailManager.validJail)), 124),
				(UiManager.Button("act", banned ? "punban" : "pban", banned ? "Unban" : "Ban...", 120, Has(admin, P + "ban")), 124)));
			menu.Items.Add(UiManager.Row(32,
				(UiManager.Text("Reason:", 30, 14), 90),
				(new InputField("m_reason", W - 90 - 340, 30), W - 90 - 336),
				(UiManager.Text("Minutes:", 30, 14), 80),
				(new InputField("m_minutes", 70, 30), 76),
				(UiManager.Text("Days:", 30, 14), 56),
				(new InputField("m_days", 70, 30), 76)));
			Hint(menu, "Minutes are used for Mute and Jail, Days for Ban; the reason is used for Kick, Jail and Ban." + (JailManager.validJail ? "" : " No jail is set (/setjail), so Jail is unavailable."));

			// --- online time
			Section(menu, "Online time");
			long seconds = ActivityTracker.GetLiveSecondsPlayed(p);
			menu.LocalStorage.SetAs("p_hours", (seconds / 3600.0).ToString("0.##", CultureInfo.InvariantCulture));
			bool canConfig = BccPermissions.CanEditConfig(admin);
			menu.Items.Add(UiManager.Row(32,
				(UiManager.Text($"{TextHelper.FormatDuration(seconds)} played - hours:", 30, 14), 300),
				(new InputField("p_hours", 120, 30), 126),
				(new EmptySpace(30), W - 300 - 126 - 196),
				(UiManager.Button("act", "psavehours", "Save", 186, canConfig), 196)));
			Hint(menu, "Used for /top time and the time based roles of Pytonia Automation.");

			// --- money
			if (EconomyManager.IsEnabled) {
				Section(menu, "Money");
				bool canMoney = Has(admin, EconomyManager.PERMISSION_ADMIN);
				menu.LocalStorage.SetAs("p_money", "");
				menu.Items.Add(UiManager.Row(32,
					(UiManager.Text($"Balance: <b>{EconomyManager.Format(EconomyManager.GetBalance(p))}</b> - amount:", 30, 14), 340),
					(new InputField("p_money", 140, 30), 146),
					(UiManager.Button("act", "pmoneyset", "Set", 110, canMoney), 114),
					(UiManager.Button("act", "pmoneyadd", "Add", 110, canMoney), 114),
					(UiManager.Button("act", "pmoneyrem", "Remove", 110, canMoney), 114)));
			}

			// --- roles
			Section(menu, "Roles");
			bool storable = p.ID.HasSteamID;
			if (!storable) {
				menu.Items.Add(UiManager.Text(Style.Error(NoSteamText), -1, 13));
			}
			bool implicitDefault = !PermissionsHelper.HasOwnEntry(p);
			List<RoleIndex.Holding> held = RoleIndex.Of(p);
			List<string> roles = RoleManager.Tree().Select(t => t.role).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			state.Lists["prole"] = roles;
			for (int i = 0; i < roles.Count; i++) {
				string r = roles[i];
				RoleIndex.Holding h = held.FirstOrDefault(x => string.Equals(x.Role, r, StringComparison.OrdinalIgnoreCase));
				string label = ChatColors.FormatRole(r) + (h != null ? "  " + Style.Success("has this role")
					+ (h.Kind != RoleIndex.HoldKind.Direct ? "  " + Style.Muted("(" + RoleIndex.Describe(h) + ")") : "") : "");
				// a role the player has through a higher role can only be changed there: no button
				(NetworkUI.IItem, int) action = h != null && h.Kind == RoleIndex.HoldKind.Inherited
					? ((NetworkUI.IItem)UiManager.Text("", 28, 14), 110)
					: ((NetworkUI.IItem)UiManager.Button("act", "prole:" + i, h != null ? "Remove" : "Add", 104, canConfig && storable, 28), 110);
				menu.Items.Add(new BackgroundColor(UiManager.Row(30,
					(UiManager.Text(label, 28, 14), W - 120),
					action), W, 34, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			if (implicitDefault) {
				Hint(menu, "No own role entry yet - the player has the default role. Adding a role creates the entry.");
			}

			// --- own permissions
			Section(menu, "Own permissions");
			List<string> perms = PermissionsHelper.GetOwnPermissions(p);
			state.Lists["pperm"] = perms;
			for (int i = 0; i < perms.Count; i++) {
				string label = perms[i].Length == 0 ? Style.UiCmd("(all permissions)") : Style.UiCmd(TextHelper.EscapeTags(perms[i]));
				menu.Items.Add(new BackgroundColor(UiManager.Row(30,
					(UiManager.Text(label, 28, 14), W - 120),
					(UiManager.Button("act", "pperm:" + i, "Remove", 104, canConfig && storable, 28), 110)), W, 34, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			if (perms.Count == 0) {
				Hint(menu, "No own permissions (only those of the roles).");
			}
			menu.LocalStorage.SetAs("p_perm", "");
			menu.Items.Add(UiManager.Row(32,
				(new InputField("p_perm", W - 330, 30), W - 326),
				(UiManager.Button("act", "paddperm", "Add", 120, canConfig && storable), 126),
				(UiManager.Button("act", "ppickperm", "Choose...", 190, canConfig && storable), 200)));

			// --- colonies
			Section(menu, "Colonies & banners");
			List<Colony> colonies = ColoniesOf(p);
			List<string> ids = colonies.Select(c => c.ColonyID.Value.ToString()).ToList();
			state.Lists["ctp"] = ids;
			state.Lists["cren"] = ids;
			state.Lists["cleave"] = ids;
			state.Lists["cdel"] = ids;
			bool canRename = Has(admin, P + "setcolonyname");
			bool canPurge = Has(admin, P + "purgebanner");
			for (int i = 0; i < colonies.Count; i++) {
				Colony c = colonies[i];
				string bannerPos = c.Banners.Count > 0 ? $"{c.Banners[0].Position.x} {c.Banners[0].Position.y} {c.Banners[0].Position.z}" : "no banner";
				int owners = c.ColonyGroup?.Owners.Count ?? 0;
				string colonyName = string.IsNullOrWhiteSpace(TextHelper.StripRichText(c.Name ?? "")) ? Style.Muted("(no name)") : TextHelper.SafeName(c.Name);
				string leftover = c.Banners.Count == 0 ? Style.Muted("  - leftover without banner") : "";
				string info = $"<b>{colonyName}</b>{leftover}\n{Style.Muted($"#{c.ColonyID.Value} · {c.Banners.Count} banner(s) at {bannerPos} · {c.FollowerCount} colonists · {owners} owner(s)")}";
				string nameKey = "cname_" + i;
				menu.LocalStorage.SetAs(nameKey, TextHelper.StripRichText(c.Name ?? ""));
				menu.Items.Add(new BackgroundColor(UiManager.Row(44,
					(UiManager.Text(info, 42, 14), W - 4 * 104 - 230),
					(new InputField(nameKey, 220, 30), 226),
					(UiManager.Button("act", "cren:" + i, "Rename", 98, canRename), 104),
					(UiManager.Button("act", "ctp:" + i, "Teleport", 98, Has(admin, P + "warp.banner") && c.Banners.Count > 0), 104),
					(UiManager.Button("act", "cleave:" + i, "Remove owner", 98, canPurge && owners > 1), 104),
					(UiManager.Button("act", "cdel:" + i, "Delete...", 98, canPurge && c.Banners.Count > 0), 104)), W, 48, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			if (colonies.Count == 0) {
				Hint(menu, "No colonies.");
			} else {
				Hint(menu, "Rename uses the text field (formatting allowed). \"Remove owner\" takes the player out of a colony with several owners.");
			}
			if (PytoniaBridge.IsReady && PytoniaBridge.Section("Colonies") != null) {
				IDictionary overrides = PytoniaBridge.GetField(PytoniaBridge.Section("Colonies"), "LimitOverrides") as IDictionary;
				string current = overrides != null && overrides.Contains(key) ? overrides[key].ToString() : "";
				int effective = PytoniaBridge.GetColonyLimit(p);
				bool canLimit = canConfig || Has(admin, "pytonia.automation.setcolonylimit");
				menu.LocalStorage.SetAs("p_climit", current);
				menu.Items.Add(UiManager.Row(32,
					(UiManager.Text($"Colony limit (Pytonia Automation): {(effective < 0 ? "unlimited" : effective.ToString())} - override:", 30, 14), 420),
					(new InputField("p_climit", 110, 30), 116),
					(new EmptySpace(30), W - 420 - 116 - 228),
					(UiManager.Button("act", "pclimit", "Save", 110, canLimit), 114),
					(UiManager.Button("act", "pclimitclear", "Role limit", 110, canLimit && current.Length > 0), 114)));
				Hint(menu, "Override: -1 = unlimited, 0 or more = max. colonies + outposts; \"Role limit\" removes the override.");
			}

			// --- homes
			if (HomesManager.IsActive) {
				List<HomePoint> homes = HomesManager.GetHomes(p);
				Section(menu, $"Homes ({homes.Count})");
				state.Lists["hdel"] = homes.Select(h => h.Name).ToList();
				state.Lists["htp"] = state.Lists["hdel"];
				for (int i = 0; i < homes.Count; i++) {
					HomePoint h = homes[i];
					menu.Items.Add(new BackgroundColor(UiManager.Row(30,
						(UiManager.Text($"<b>{TextHelper.EscapeTags(h.Name)}</b> {Style.Muted($"{(int)h.X} {(int)h.Y} {(int)h.Z}")}", 28, 14), W - 230),
						(UiManager.Button("act", "htp:" + i, "Teleport", 104, Has(admin, P + "warp.place"), 28), 110),
						(UiManager.Button("act", "hdel:" + i, "Delete", 104, canConfig, 28), 110)), W, 34, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
				}
				if (homes.Count == 0) {
					Hint(menu, "No homes.");
				}
			}

			// --- history
			List<ModerationLog.HistoryEntry> history = ModerationLog.History(p);
			Section(menu, $"Moderation history ({incidents} incident(s), {history.Count} entries)");
			foreach (ModerationLog.HistoryEntry h in history) {
				string type = h.Offense ? $"<b><color=#FFB347>{h.Type}</color></b>" : $"<b>{h.Type}</b>";
				menu.Items.Add(UiManager.Text($"{Style.Muted(h.Time.ToString("yyyy-MM-dd HH:mm"))}  {type}  {TextHelper.SafeName(h.Details)} {Style.Muted("by " + h.By)}", -1, 13));
			}
			if (history.Count == 0) {
				Hint(menu, "Nothing recorded - no bans, jail sentences, kicks, mutes or other incidents.");
			}
			menu.LocalStorage.SetAs("p_note", "");
			menu.Items.Add(UiManager.Row(32,
				(new InputField("p_note", W - 330, 30), W - 326),
				(UiManager.Button("act", "pnote", "Add note", 150, true), 156),
				(UiManager.Button("act", "pwarn", "Add warning", 160, true), 170)));
			Hint(menu, "A note is information only. A warning counts as an incident and is shown to the player as a popup (on the next join if offline) until they confirm it.");
		}

		// ------------------------------------------------------------------ confirmations

		public static void BuildBanConfirm(Players.Player admin, UiManager.PageState state, NetworkMenu menu)
		{
			Players.Player p = FindPlayer(state.Key);
			UiManager.Header(admin, menu, "Ban player");
			if (p == null || !Has(admin, P + "ban")) {
				menu.Items.Add(UiManager.Text(Style.Error(p == null ? "Player not found." : "You don't have permission to ban players.")));
				return;
			}
			int days = state.Lists.TryGetValue("bandays", out List<string> d) && d.Count > 0 && int.TryParse(d[0], out int v) ? v : 7;
			string reason = state.Lists.TryGetValue("banreason", out List<string> r) && r.Count > 0 ? r[0] : "";
			int alone = 0;
			for (int i = 0; i < p.ColonyGroups.Count; i++) {
				if (p.ColonyGroups[i].Owners.Count == 1) alone += p.ColonyGroups[i].Colonies.Count;
			}
			menu.Items.Add(UiManager.Text($"Really ban {ChatColors.GetDisplayName(p)} for <b>{days} day(s)</b>?", 30, 17));
			menu.Items.Add(UiManager.Text($"Reason: {TextHelper.EscapeTags(reason)}", -1, 14));
			menu.Items.Add(UiManager.Text(Style.Error($"Banning also DELETES the {alone} colonies/outposts the player owns alone, and removes them from shared colonies (same as /ban)."), -1, 14));
			menu.Items.Add(new EmptySpace(10));
			menu.Items.Add(UiManager.Row(36,
				(UiManager.Button("back", null, "Cancel", 186), 196),
				(new EmptySpace(30), W - 400),
				(UiManager.Button("act", "pbanconfirm", "Yes, ban", 186), 196)));
		}

		public static void BuildColonyDeleteConfirm(Players.Player admin, UiManager.PageState state, NetworkMenu menu)
		{
			Colony c = FindColony(state.Key);
			UiManager.Header(admin, menu, "Delete colony");
			if (c == null || !Has(admin, P + "purgebanner")) {
				menu.Items.Add(UiManager.Text(Style.Error(c == null ? "Colony not found." : "You don't have permission to delete colonies.")));
				return;
			}
			menu.Items.Add(UiManager.Text($"Really delete <b>{TextHelper.SafeName(c.Name)}</b> (#{c.ColonyID.Value}) with all {c.Banners.Count} banner(s) and {c.FollowerCount} colonists?", -1, 16));
			menu.Items.Add(UiManager.Text(Style.Error("This can't be undone (same as /purgebanner colony)."), -1, 14));
			menu.Items.Add(new EmptySpace(10));
			menu.Items.Add(UiManager.Row(36,
				(UiManager.Button("back", null, "Cancel", 186), 196),
				(new EmptySpace(30), W - 400),
				(UiManager.Button("act", "cdelconfirm", "Yes, delete", 186), 196)));
		}

		// ------------------------------------------------------------------ actions

		public static void Action(Players.Player admin, UiManager.PageState state, string action, string item, JObject storage)
		{
			if (!CanOpen(admin)) {
				state.Messages.Add(Style.Error("You don't have permission to moderate players."));
				UiManager.Render(admin);
				return;
			}
			if (action == "pedit") {
				UiManager.Open(admin, new UiManager.PageState { Kind = "player", Key = item });
				return;
			}
			string ok = null, error = null;
			if (state.Kind == "colonydelete") {
				Colony c = FindColony(state.Key);
				if (action == "cdelconfirm" && c != null && Has(admin, P + "purgebanner")) {
					Players.Player owner = c.ColonyGroup != null && c.ColonyGroup.Owners.Count > 0 ? c.ColonyGroup.Owners.GetAt(0) : null;
					string name = TextHelper.StripRichText(c.Name);
					new PurgeBannerCommand().PurgeColony(admin, c);
					if (owner != null) ModerationLog.Add(owner, "Colony deleted", admin, $"'{name}' (#{state.Key})", false);
					Log.Write($"[BCC UI] {admin.Name} deleted colony '{name}' (#{state.Key})");
					BackWithMessage(admin, Style.Success($"Colony '{name}' deleted."));
				}
				return;
			}
			Players.Player p = FindPlayer(state.Key);
			if (p == null) {
				return;
			}
			string reason = (storage.Value<string>("m_reason") ?? "").Trim();
			int.TryParse((storage.Value<string>("m_minutes") ?? "").Trim(), out int minutes);
			int.TryParse((storage.Value<string>("m_days") ?? "").Trim(), out int days);
			if ((action == "prole" || action == "pperm" || action == "paddperm" || action == "ppickperm") && !p.ID.HasSteamID) {
				state.Messages.Add(Style.Error(NoSteamText));
				UiManager.Render(admin);
				return;
			}
			switch (action) {
				case "ptp":
					if (!Has(admin, P + "warp.self")) break;
					UiManager.Close(admin);
					Helper.TeleportPlayer(admin, p.PositionStanding);
					Log.Write($"[BCC UI] {admin.Name} teleported to {p.Name}");
					return;
				case "pkick":
					if (!Has(admin, P + "kick") || !IsOnline(p)) break;
					BccChat.Send(p, "<color=red>You were kicked from the server</color>" + (reason.Length > 0 ? $" <color=red>Reason: {reason}</color>" : ""));
					ModerationLog.Add(p, "Kick", admin, reason.Length > 0 ? reason : "kicked from the player window");
					Log.Write($"[BCC UI] {admin.Name} kicked {p.Name}: {reason}");
					Players.Disconnect(p);
					ok = $"{p.Name} was kicked.";
					break;
				case "pmute":
					if (!Has(admin, P + "mute")) break;
					if (minutes < 1) { error = "Please enter the minutes."; break; }
					MuteList.MutedMinutes[p.ID.ID] = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond + minutes * 60L * 1000L;
					BccChat.Send(p, $"You're muted for {minutes} Minute(s)");
					ModerationLog.Add(p, "Mute", admin, $"{minutes} minute(s)" + (reason.Length > 0 ? ": " + reason : ""));
					Log.Write($"[BCC UI] {admin.Name} muted {p.Name} for {minutes} minute(s)");
					ok = $"{p.Name} muted for {minutes} minute(s).";
					break;
				case "punmute":
					if (!Has(admin, P + "mute")) break;
					MuteList.MutedMinutes.Remove(p.ID.ID);
					ModerationLog.Add(p, "Unmute", admin, "", false);
					Log.Write($"[BCC UI] {admin.Name} unmuted {p.Name}");
					ok = $"{p.Name} unmuted.";
					break;
				case "pjail":
					if (!Has(admin, P + "jail")) break;
					if (!JailManager.validJail) { error = "No jail is set - use /setjail first."; break; }
					if (minutes < 1) { error = "Please enter the minutes."; break; }
					if (reason.Length == 0) { error = "Please enter a reason."; break; }
					JailManager.jailPlayer(p, admin, reason, minutes);
					Log.Write($"[BCC UI] {admin.Name} jailed {p.Name} for {minutes} minute(s): {reason}");
					ok = $"{p.Name} jailed for {minutes} minute(s).";
					break;
				case "prelease":
					if (!Has(admin, P + "jail") || !JailManager.IsPlayerJailed(p)) break;
					JailManager.releasePlayer(p, admin);
					ok = $"{p.Name} released.";
					break;
				case "pban":
					if (!Has(admin, P + "ban")) break;
					if (days < 1) { error = "Please enter the days (1 or more)."; break; }
					if (reason.Length == 0) { error = "Please enter a reason."; break; }
					UiManager.PageState confirm = new UiManager.PageState { Kind = "playerban", Key = state.Key };
					confirm.Lists["bandays"] = new List<string> { days.ToString() };
					confirm.Lists["banreason"] = new List<string> { reason };
					UiManager.Open(admin, confirm);
					return;
				case "pbanconfirm": {
					if (!Has(admin, P + "ban")) break;
					int d = state.Lists.TryGetValue("bandays", out List<string> dl) && dl.Count > 0 && int.TryParse(dl[0], out int dv) ? dv : 7;
					string r = state.Lists.TryGetValue("banreason", out List<string> rl) && rl.Count > 0 ? rl[0] : "";
					BannedPlayerManager.BanPlayer(p, admin, (uint)d, r);
					Chat.SendToConnected($"{ChatColors.GetDisplayName(p)} was banned for {d} day(s): {r}");
					Log.Write($"[BCC UI] {admin.Name} banned {p.Name} for {d} day(s): {r}");
					BackWithMessage(admin, Style.Success($"{p.Name} banned for {d} day(s)."));
					return;
				}
				case "punban": {
					if (!Has(admin, P + "ban")) break;
					BanLogRecord active = ModerationLog.ActiveBan(p);
					if (active != null) {
						BannedPlayerManager.UnbanPlayer(active, admin);
					} else if (p.ID.HasSteamID) {
						BlackAndWhitelisting.RemoveBlackList(p.ID.SteamID.m_SteamID);
						ModerationLog.Add(p, "Unban", admin, "removed from the blacklist", false);
					}
					BlackAndWhitelisting.Reload();
					Log.Write($"[BCC UI] {admin.Name} unbanned {p.Name}");
					ok = $"{p.Name} unbanned.";
					break;
				}
				case "psavehours": {
					if (!BccPermissions.CanEditConfig(admin)) break;
					if (!double.TryParse((storage.Value<string>("p_hours") ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double hours) || hours < 0) {
						error = "Please enter the hours as a number.";
						break;
					}
					ActivityTracker.StatsDataEntry stats = ActivityTracker.GetOrCreateStats(p.ID.ToStringReadable());
					long before = ActivityTracker.GetLiveSecondsPlayed(p);
					stats.secondsPlayed = (long)(hours * 3600);
					if (IsOnline(p)) stats.lastSeen = DateTime.Now.ToString();
					ActivityTracker.Save();
					ModerationLog.Add(p, "Online time changed", admin, $"{before / 3600.0:0.##}h -> {hours:0.##}h", false);
					Log.Write($"[BCC UI] {admin.Name} set the online time of {p.Name} to {hours:0.##}h");
					ok = "Online time saved.";
					break;
				}
				case "pmoneyset":
				case "pmoneyadd":
				case "pmoneyrem": {
					if (!Has(admin, EconomyManager.PERMISSION_ADMIN)) break;
					if (!long.TryParse((storage.Value<string>("p_money") ?? "").Trim(), out long amount) || amount < 0) {
						error = "Please enter a whole, positive amount.";
						break;
					}
					string why = $"player window by {admin.Name}";
					bool done = action == "pmoneyset" ? EconomyManager.TrySet(p, amount, why)
						: action == "pmoneyadd" ? EconomyManager.TryAdd(p, amount, why) : EconomyManager.TryRemove(p, amount, why);
					if (!done) {
						error = "Not possible (not enough money or economy disabled).";
						break;
					}
					string verb = action == "pmoneyset" ? "set to" : action == "pmoneyadd" ? "+" : "-";
					ModerationLog.Add(p, "Money changed", admin, $"{verb} {EconomyManager.Format(amount)}, now {EconomyManager.Format(EconomyManager.GetBalance(p))}", false);
					Log.Write($"[BCC UI] {admin.Name} changed the money of {p.Name}: {verb} {amount}");
					ok = $"New balance: {EconomyManager.Format(EconomyManager.GetBalance(p))}";
					break;
				}
				case "prole": {
					if (!BccPermissions.CanEditConfig(admin)) break;
					RoleTestManager.EndIfActive(p, "an admin changed your roles");
					RoleIndex.Holding held = RoleIndex.Find(p, item);
					if (held != null && held.Kind == RoleIndex.HoldKind.Inherited) {
						error = $"{p.Name} has {item} through {held.Via} - change {held.Via} instead.";
					} else if (held != null) {
						error = RoleIndex.RemoveRole(admin, p, item);
						if (error == null) {
							ModerationLog.Add(p, "Role removed", admin, item, false);
							ok = $"Role {item} removed.";
						}
					} else {
						PermissionsManager.AddGroupToUser(admin, p, item);
						ModerationLog.Add(p, "Role added", admin, item, false);
						ok = $"Role {item} added.";
					}
					BccPermissions.Refresh(true);
					Log.Write($"[BCC UI] {admin.Name}: {ok ?? error} ({p.Name})");
					break;
				}
				case "pperm":
					if (!BccPermissions.CanEditConfig(admin)) break;
					PermissionsManager.RemovePermissionOfUser(admin, p, item);
					BccPermissions.Refresh(true);
					ModerationLog.Add(p, "Permission removed", admin, item, false);
					ok = $"Permission {item} removed.";
					break;
				case "paddperm": {
					if (!BccPermissions.CanEditConfig(admin)) break;
					string perm = (storage.Value<string>("p_perm") ?? "").Trim();
					if (perm.Length == 0 || perm.Contains(" ")) { error = "Please enter a permission (no spaces)."; break; }
					PermissionsManager.AddPermissionToUser(admin, p, perm);
					BccPermissions.Refresh(true);
					ModerationLog.Add(p, "Permission added", admin, perm, false);
					ok = $"Permission {perm} added.";
					break;
				}
				case "ppickperm":
					if (!BccPermissions.CanEditConfig(admin)) break;
					UiManager.Open(admin, new UiManager.PageState { Kind = "permpicker", Key = "player:" + state.Key });
					return;
				case "cren": {
					Colony c = FindColony(item);
					if (c == null || !Has(admin, P + "setcolonyname")) break;
					int idx = state.Lists["cren"].IndexOf(item);
					string newName = (storage.Value<string>("cname_" + idx) ?? "").Trim();
					if (newName.Length == 0) { error = "Please enter a name."; break; }
					string old = c.Name;
					c.Name = newName;
					for (int i = 0; i < c.ColonyGroup.Owners.Count; i++) {
						c.SendNameUpdate(c.ColonyGroup.Owners.GetAt(i));
					}
					ColonyNameGuard.RegisterKnownGoodName(c.ColonyID, newName);
					ModerationLog.Add(p, "Colony renamed", admin, $"'{TextHelper.StripRichText(old)}' -> '{TextHelper.StripRichText(newName)}'", false);
					Log.Write($"[BCC UI] {admin.Name} renamed colony #{item} '{old}' to '{newName}'");
					ok = "Colony renamed.";
					break;
				}
				case "ctp": {
					Colony c = FindColony(item);
					if (c == null || c.Banners.Count == 0 || !Has(admin, P + "warp.banner")) break;
					UiManager.Close(admin);
					Helper.TeleportPlayer(admin, c.Banners[0].Position.Vector);
					return;
				}
				case "cleave": {
					Colony c = FindColony(item);
					if (c == null || !Has(admin, P + "purgebanner") || c.ColonyGroup.Owners.Count < 2) break;
					c.ColonyGroup.RemoveOwner(p);
					ModerationLog.Add(p, "Removed from colony", admin, $"'{TextHelper.StripRichText(c.Name)}' (#{item})", false);
					Log.Write($"[BCC UI] {admin.Name} removed {p.Name} from colony #{item}");
					ok = "Player removed from the colony.";
					break;
				}
				case "cdel":
					if (!Has(admin, P + "purgebanner")) break;
					UiManager.Open(admin, new UiManager.PageState { Kind = "colonydelete", Key = item });
					return;
				case "pclimit":
				case "pclimitclear": {
					if (!(BccPermissions.CanEditConfig(admin) || Has(admin, "pytonia.automation.setcolonylimit")) || !PytoniaBridge.IsReady) break;
					IDictionary overrides = PytoniaBridge.GetField(PytoniaBridge.Section("Colonies"), "LimitOverrides") as IDictionary;
					if (overrides == null) break;
					string key = p.ID.ToStringReadable();
					if (action == "pclimitclear") {
						overrides.Remove(key);
						ok = "Colony limit override removed - the role limit applies.";
					} else if (int.TryParse((storage.Value<string>("p_climit") ?? "").Trim(), out int limit) && limit >= -1) {
						overrides[key] = limit;
						ok = $"Colony limit set to {(limit < 0 ? "unlimited" : limit.ToString())}.";
					} else {
						error = "The limit must be -1 (unlimited) or a whole number of 0 or more.";
						break;
					}
					PytoniaBridge.Save();
					ModerationLog.Add(p, "Colony limit changed", admin, ok, false);
					Log.Write($"[BCC UI] {admin.Name}: {ok} ({p.Name})");
					break;
				}
				case "htp": {
					HomePoint h = HomesManager.GetHomes(p).FirstOrDefault(x => x.Name == item);
					if (h == null || !Has(admin, P + "warp.place")) break;
					UiManager.Close(admin);
					Helper.TeleportPlayer(admin, new Vector3(h.X, h.Y, h.Z));
					return;
				}
				case "hdel":
					if (!BccPermissions.CanEditConfig(admin)) break;
					if (HomesManager.DeleteHome(p, item)) {
						ModerationLog.Add(p, "Home deleted", admin, item, false);
						ok = $"Home {item} deleted.";
					}
					break;
				case "pnote":
				case "pwarn": {
					string text = (storage.Value<string>("p_note") ?? "").Trim();
					if (text.Length == 0) { error = "Please enter a text."; break; }
					ModerationLog.Add(p, action == "pwarn" ? "Warning" : "Note", admin, text, action == "pwarn", action == "pwarn");
					if (action == "pwarn") {
						WarningPopup.ShowPending(p);
					}
					Log.Write($"[BCC UI] {admin.Name} added a {(action == "pwarn" ? "warning" : "note")} to {p.Name}: {text}");
					ok = action == "pwarn" ? "Warning recorded." : "Note saved.";
					break;
				}
			}
			if (error != null) state.Messages.Add(Style.Error(error));
			else if (ok != null) state.Messages.Add(Style.Success(ok));
			UiManager.Render(admin);
		}

		static void BackWithMessage(Players.Player admin, string message)
		{
			UiManager.Back(admin);
			UiManager.PageState cur = UiManager.Current(admin);
			if (cur != null) {
				cur.Messages.Add(message);
				UiManager.Render(admin);
			}
		}
	}
}
