using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using NetworkUI;
using NetworkUI.Items;
using Pipliz;

namespace ColonyCommands
{
	// Role management windows (permission mods.scarabol.commands.config, checked on build and on every click):
	//  roles       - tree of all roles (higher roles above the roles they inherit from), create a role
	//  role        - one role: rename, chat formatting, inheritance (both directions), own permissions,
	//                Pytonia Automation values / home limit, default role, delete
	//  permpicker  - every known permission, add one to the role
	//  roledelete  - confirmation
	public static class UiRolePages
	{
		static int W => UiManager.ContentWidth;

		static bool Denied(Players.Player player, NetworkMenu menu)
		{
			TestBanner(player, menu);
			if (BccPermissions.CanEditConfig(player)) {
				return false;
			}
			menu.Items.Add(UiManager.Text(Style.Error("You don't have permission to manage roles.")));
			if (RoleTestManager.IsTesting(player)) {
				menu.Items.Add(UiManager.Text(Style.Muted("The role you are testing can't manage roles - your own roles come back when the test ends."), -1, 13));
			}
			return true;
		}

		// shown on every role page while the viewer tests a role (also when the tested role may not manage roles)
		static void TestBanner(Players.Player player, NetworkMenu menu)
		{
			RoleTestManager.TestRecord rec = RoleTestManager.Get(player);
			if (rec == null) {
				return;
			}
			menu.Items.Add(new BackgroundColor(UiManager.Row(34,
				(UiManager.Text($"<b>Role test:</b> you only have {ChatColors.FormatRole(rec.Role)} - your roles come back in {RoleTestManager.SecondsLeft(rec)} s.", 30, 14), W - 200),
				(UiManager.Button("act", "endtest", "End test now", 186), 196)), W, 38, 0f, 0f, 2f, 2f, new UnityEngine.Color32(70, 160, 90, 70)));
			menu.Items.Add(new EmptySpace(4));
		}

		public static void BuildRoles(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "Roles & permissions");
			if (Denied(player, menu)) {
				return;
			}
			UiManager.PermissionNotice(menu, player);
			UiManager.Messages(menu, state);
			menu.Items.Add(UiManager.Text("A role inherits every permission of the roles below it. Higher roles stand above the roles they include.", -1, 14));
			menu.Items.Add(UiManager.Text(Style.Muted("Saved to the world's permissiongroups.json; the game's permissions are reloaded right away."), -1, 13));
			menu.Items.Add(new Line());
			UiManager.SectionHeader(menu, "New role");
			menu.LocalStorage.SetAs("new_name", "");
			menu.LocalStorage.SetAs("new_parent", "");
			menu.LocalStorage.SetAs("new_child", "");
			int third = (W - 140) / 3;
			menu.Items.Add(UiManager.Row(22,
				(UiManager.Text(Style.Muted("name"), 20, 13), third),
				(UiManager.Text(Style.Muted("place under role (optional)"), 20, 13), third),
				(UiManager.Text(Style.Muted("inherits from role (optional)"), 20, 13), third)));
			menu.Items.Add(UiManager.Row(32,
				(new InputField("new_name", third - 6, 30), third),
				(new InputField("new_parent", third - 6, 30), third),
				(new InputField("new_child", third - 6, 30), third),
				(UiManager.Button("act", "create", "Create", 126), 132)));
			menu.Items.Add(new Line());

			List<(string role, int depth, bool repeat)> tree = RoleManager.Tree();
			state.Lists["roles"] = tree.Select(t => t.role).ToList();
			string def = RoleManager.DefaultRole;
			for (int i = 0; i < tree.Count; i++) {
				(string role, int depth, bool repeat) = tree[i];
				RoleManager.Role r = RoleManager.Get(role);
				string indent = new string(' ', depth * 6) + (depth > 0 ? "└ " : "");
				string info = r == null ? "" : Style.Muted($"  {r.Permissions.Count} permission(s), {RoleManager.PlayerCount(role)} player(s)"
					+ (string.Equals(role, def, StringComparison.OrdinalIgnoreCase) ? ", default for new players" : "")
					+ (repeat ? ", see above" : ""));
				menu.Items.Add(new BackgroundColor(UiManager.Row(32,
					(UiManager.Text(indent + ChatColors.FormatRole(role) + info, 30, 15), W - 230),
					(UiManager.Button("act", "test:" + i, "Test", 104, r != null && player.ID.HasSteamID), 110),
					(UiManager.Button("act", "edit:" + i, "Edit", 104), 110)), W, 36, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			if (tree.Count > 0) {
				menu.Items.Add(UiManager.Text(Style.Muted($"Test = you get only that role for {RoleTestManager.DURATION_SECONDS / 60} minutes, then your own roles come back automatically (also after a disconnect or a server stop)."), -1, 12));
			}
		}

		public static void BuildRole(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			RoleManager.Role role = RoleManager.Get(state.Key);
			UiManager.Header(player, menu, role != null ? "Role: " + role.Name : "Role");
			if (Denied(player, menu)) {
				return;
			}
			if (role == null) {
				menu.Items.Add(UiManager.Text(Style.Error("This role does not exist (any more).")));
				return;
			}
			UiManager.PermissionNotice(menu, player);
			UiManager.Messages(menu, state);
			menu.Items.Add(UiManager.Text($"Shown in chat as: {ChatColors.FormatRole(role.Name)}   {Style.Muted($"{RoleManager.PlayerCount(role.Name)} player(s) have it, {RoleIndex.InheritedCount(role.Name)} more through a higher role")}", 26, 15));

			// --- test
			Section(menu, "Test this role");
			RoleTestManager.TestRecord testing = RoleTestManager.Get(player);
			bool testsThis = testing != null && string.Equals(testing.Role, role.Name, StringComparison.OrdinalIgnoreCase);
			menu.Items.Add(UiManager.Row(34,
				(UiManager.Text(testsThis ? Style.Success($"You are testing this role ({RoleTestManager.SecondsLeft(testing)} s left).") : "Try out what players of this role can do.", 30, 14), W - 260),
				(UiManager.Button("act", "testthis", $"Test for {RoleTestManager.DURATION_SECONDS / 60} minutes", 246, player.ID.HasSteamID && !testsThis), 256)));
			menu.Items.Add(UiManager.Text(Style.Muted(player.ID.HasSteamID
				? $"Your roles are saved and you get only this role; after {RoleTestManager.DURATION_SECONDS / 60} minutes they come back automatically - also after a disconnect or a server stop. End it early with /roletest end."
				: UiPlayerPages.NoSteamText), -1, 12));

			// --- name
			Section(menu, "Name");
			menu.LocalStorage.SetAs("rename", role.Name);
			menu.Items.Add(UiManager.Row(32,
				(new InputField("rename", W - 200, 30), W - 196),
				(UiManager.Button("act", "rename", "Rename", 186), 196)));
			menu.Items.Add(UiManager.Text(Style.Muted("Renaming updates other roles, players, chat colors, jail settings, Pytonia Automation and home limits."), -1, 12));
			bool isDefault = string.Equals(role.Name, RoleManager.DefaultRole, StringComparison.OrdinalIgnoreCase);
			if (!isDefault) {
				menu.Items.Add(UiManager.Row(32,
					(UiManager.Text(Style.Muted("New players currently get: ") + ChatColors.FormatRole(RoleManager.DefaultRole), 30, 13), W - 260),
					(UiManager.Button("act", "default", "Make default role", 246), 256)));
			}

			// --- formatting
			Section(menu, "Chat formatting");
			ChatColors.Colors.TryGetValue(role.Name, out ChatColorSpecification spec);
			Field(menu, "f_prefix", "Role tag (prefix)", spec.Prefix, "e.g. <b>[Mod]</b> - rich text allowed, empty = no tag");
			Field(menu, "f_prefixcolor", "Tag color", spec.PrefixColor, "e.g. #6E69FF or red");
			Field(menu, "f_name", "Player name color", spec.Name, "e.g. #FA6464");
			Field(menu, "f_text", "Message color", spec.Text, "e.g. white");
			menu.Items.Add(UiManager.Row(34,
				(UiManager.Text(Style.Muted("Players of several roles get the formatting of their highest role."), 30, 12), W - 200),
				(UiManager.Button("act", "savefmt", "Save formatting", 186), 196)));

			// --- inheritance
			Section(menu, "Inherits from (roles below)");
			state.Lists["children"] = new List<string>(role.Includes);
			ItemList(menu, state.Lists["children"].Select(ChatColors.FormatRole).ToList(), "rmchild", "Remove", "inherits from no other role");
			List<string> inherited = RoleManager.Descendants(role.Name).Where(d => !role.Includes.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
			if (inherited.Count > 0) {
				menu.Items.Add(UiManager.Text(Style.Muted("Indirectly also: ") + string.Join(", ", inherited.Select(ChatColors.FormatRole)), -1, 13));
			}
			AddRow(menu, "add_child", "addchild", "Add role below");

			Section(menu, "Is part of (roles above)");
			state.Lists["parents"] = RoleManager.Parents(role.Name);
			ItemList(menu, state.Lists["parents"].Select(ChatColors.FormatRole).ToList(), "rmparent", "Remove", "no role includes this one");
			AddRow(menu, "add_parent", "addparent", "Place under role");

			// --- permissions
			Section(menu, $"Own permissions ({role.Permissions.Count})");
			state.Lists["perms"] = new List<string>(role.Permissions);
			List<string> permLabels = role.Permissions.Select(p => {
				string label = p.Length == 0 ? Style.Cmd("(all permissions)") : Style.Cmd(TextHelper.EscapeTags(p));
				if (BccPermissions.TryGetInfo(p, out BccPermissions.PermissionInfo info)) {
					label += Style.Muted($"  [{info.Mod}] {TextHelper.Truncate(info.Description, 50)}");
				}
				return label;
			}).ToList();
			ItemList(menu, permLabels, "rmperm", "Remove", "no own permissions");
			menu.LocalStorage.SetAs("add_perm", "");
			menu.Items.Add(UiManager.Row(32,
				(new InputField("add_perm", W - 330, 30), W - 326),
				(UiManager.Button("act", "addperm", "Add", 120), 126),
				(UiManager.Button("act", "browse", "Choose...", 190), 200)));

			// --- Pytonia Automation values / home limit
			if (PytoniaBridge.IsReady && PytoniaBridge.Roles() is IDictionary pyRoles && pyRoles.Contains(role.Name)) {
				object rs = pyRoles[role.Name];
				Section(menu, "Pytonia Automation (time based roles, colony limit)");
				foreach (string f in new[] { "RequiredHoursOnline", "ExcludeFromTimeBasedAssignment", "MaxColonies" }) {
					Type t = PytoniaBridge.FieldType(rs, f);
					if (t == null) continue;
					object v = PytoniaBridge.GetField(rs, f);
					string help = PytoniaHelp(f);
					if (t == typeof(bool)) {
						menu.LocalStorage.SetAs("py_" + f, (bool)v);
						menu.Items.Add(new Toggle(new LabelData($"<b>{f}</b>", ELabelAlignment.MiddleLeft, 15, LabelData.ELocalizationType.None), "py_" + f));
						menu.Items.Add(UiManager.Text(Style.Muted(help), -1, 12));
					} else {
						Field(menu, "py_" + f, f, Convert.ToString(v, CultureInfo.InvariantCulture), help);
					}
				}
				menu.Items.Add(UiManager.Row(34, (new EmptySpace(30), W - 200), (UiManager.Button("act", "savepy", "Save values", 186), 196)));
			}
			{
				Section(menu, "Home points");
				int limit = BccSettingsManager.Settings.Homes.RoleLimits.TryGetValue(role.Name, out int l) ? l : int.MinValue;
				Field(menu, "homes_limit", "Home limit", limit == int.MinValue ? "" : limit.ToString(), "-1 = unlimited, 0 = none, empty = default limit (" + BccSettingsManager.Settings.Homes.DefaultLimit + ")");
				menu.Items.Add(UiManager.Row(34, (new EmptySpace(30), W - 200), (UiManager.Button("act", "savehomes", "Save limit", 186), 196)));
			}

			// --- players
			// every player that has the role - assigned, as default role (also the game's copies of it) and
			// through a higher role - from the game's live data; online and recently seen first
			List<RoleIndex.Holder> holders = RoleIndex.HoldersOf(role.Name)
				.OrderBy(h => h.Holding.Kind == RoleIndex.HoldKind.Inherited ? 1 : 0)
				.ThenByDescending(h => UiPlayerPages.LastSeen(h.Player))
				.ThenBy(h => h.Player.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();
			int assigned = holders.Count(h => h.Holding.Kind != RoleIndex.HoldKind.Inherited);
			Section(menu, $"Players with this role ({assigned}{(holders.Count > assigned ? $" + {holders.Count - assigned} through a higher role" : "")})");
			if (isDefault) {
				menu.Items.Add(UiManager.Text(Style.Muted("Default role: players without an own role entry have it, and so do players whose entry the game created as a copy of it."), -1, 12));
			}
			List<RoleIndex.Holder> shown = holders.Take(UiManager.LinesPerPage).ToList();
			state.Lists["rmplayer"] = shown.Select(h => h.Player.ID.ToStringReadable()).ToList();
			for (int i = 0; i < shown.Count; i++) {
				RoleIndex.Holder h = shown[i];
				string label = ChatColors.GetDisplayName(h.Player) + "  " + Style.Muted(RoleIndex.Describe(h.Holding))
					+ (UiPlayerPages.IsOnline(h.Player) ? "  " + Style.Success("online") : "");
				(IItem, int) action = h.Holding.Kind == RoleIndex.HoldKind.Inherited
					? ((IItem)UiManager.Text("", 28, 14), 110)
					: ((IItem)UiManager.Button("act", "rmplayer:" + i, "Remove", 104, true, 28), 110);
				menu.Items.Add(new BackgroundColor(UiManager.Row(30, (UiManager.Text(label, 28, 14), W - 120), action),
					W, 34, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			if (holders.Count > shown.Count) {
				menu.Items.Add(UiManager.Text(Style.Muted($"... and {holders.Count - shown.Count} more - search a player in Player moderation."), -1, 12));
			}
			if (holders.Count == 0) {
				menu.Items.Add(UiManager.Text(Style.Muted("no player has this role"), 22, 13));
			}
			menu.LocalStorage.SetAs("add_player", "");
			menu.Items.Add(UiManager.Row(32,
				(new InputField("add_player", W - 230, 30), W - 226),
				(UiManager.Button("act", "addplayer", "Add player", 216), 226)));
			menu.Items.Add(UiManager.Text(Style.Muted("Player name or SteamID (also offline players)."), -1, 12));

			// --- delete
			menu.Items.Add(new Line());
			menu.Items.Add(UiManager.Row(34,
				(UiManager.Text(isDefault ? Style.Muted("The default role can't be deleted.") : "", 30, 13), W - 200),
				(UiManager.Button("act", "delete", "Delete role", 186, !isDefault), 196)));
		}

		static string PytoniaHelp(string field)
		{
			switch (field) {
				case "RequiredHoursOnline": return "Online hours after which Pytonia Automation grants this role (0 = not time based).";
				case "ExcludeFromTimeBasedAssignment": return "Ticked = never granted automatically by online time.";
				case "MaxColonies": return "Max. colonies + outposts a player of this role may found (-1 = unlimited).";
				default: return "";
			}
		}

		static void Section(NetworkMenu menu, string title)
		{
			menu.Items.Add(new EmptySpace(4));
			UiManager.SectionHeader(menu, title);
		}

		static void Field(NetworkMenu menu, string key, string label, string value, string help)
		{
			menu.LocalStorage.SetAs(key, value ?? "");
			menu.Items.Add(UiManager.Row(32,
				(UiManager.Text($"<b>{label}</b>", 30, 14), 220),
				(new InputField(key, W - 228, 30), W - 220)));
			if (!string.IsNullOrEmpty(help)) {
				menu.Items.Add(UiManager.Text(Style.Muted(help), -1, 12));
			}
		}

		static void ItemList(NetworkMenu menu, List<string> labels, string action, string buttonText, string empty)
		{
			for (int i = 0; i < labels.Count; i++) {
				menu.Items.Add(new BackgroundColor(UiManager.Row(30,
					(UiManager.Text(labels[i], 28, 14), W - 120),
					(UiManager.Button("act", action + ":" + i, buttonText, 104, true, 28), 110)), W, 34, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			if (labels.Count == 0) {
				menu.Items.Add(UiManager.Text(Style.Muted(empty), 22, 13));
			}
		}

		static void AddRow(NetworkMenu menu, string key, string action, string buttonText)
		{
			menu.LocalStorage.SetAs(key, "");
			menu.Items.Add(UiManager.Row(32,
				(new InputField(key, W - 230, 30), W - 226),
				(UiManager.Button("act", action, buttonText, 216), 226)));
		}

		public static void BuildDeleteConfirm(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "Delete role");
			if (Denied(player, menu)) {
				return;
			}
			int players = RoleManager.PlayerCount(state.Key);
			menu.Items.Add(UiManager.Text($"Really delete the role {ChatColors.FormatRole(state.Key)}?", 30, 17));
			menu.Items.Add(UiManager.Text(Style.Muted($"{players} player(s) lose it; roles that include it no longer inherit its permissions; its chat formatting is removed. A backup of permissiongroups.json is kept (.bak)."), -1, 13));
			menu.Items.Add(new EmptySpace(10));
			menu.Items.Add(UiManager.Row(36,
				(UiManager.Button("back", null, "Cancel", 186), 196),
				(new EmptySpace(30), W - 400),
				(UiManager.Button("act", "confirmdelete", "Yes, delete", 186), 196)));
		}

		public static void BuildPermissionPicker(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			bool forPlayer = state.Key.StartsWith("player:");
			Players.Player targetPlayer = forPlayer ? UiPlayerPages.FindPlayer(state.Key.Substring(7)) : null;
			UiManager.Header(player, menu, "Add permission to " + (forPlayer ? (targetPlayer?.Name ?? "?") : state.Key));
			if (Denied(player, menu)) {
				return;
			}
			RoleManager.Role role = forPlayer ? null : RoleManager.Get(state.Key);
			if (forPlayer ? targetPlayer == null : role == null) {
				menu.Items.Add(UiManager.Text(Style.Error(forPlayer ? "Player not found." : "Role not found.")));
				return;
			}
			List<string> existing = forPlayer ? PermissionsHelper.GetOwnPermissions(targetPlayer) : role.Permissions;
			UiManager.Messages(menu, state);
			UiManager.FilterRow(menu, state.Filter, "Filter:");
			string f = (state.Filter ?? "").ToLowerInvariant();
			List<BccPermissions.PermissionInfo> all = BccPermissions.GetAll()
				.Where(p => p.Permission.Length > 0 && !existing.Contains(p.Permission))
				.Where(p => f.Length == 0 || p.Permission.ToLowerInvariant().Contains(f) || p.Mod.ToLowerInvariant().Contains(f) || (p.Description ?? "").ToLowerInvariant().Contains(f))
				.ToList();
			state.Lists["pick"] = all.Select(p => p.Permission).ToList();
			menu.Items.Add(UiManager.Text(Style.Muted($"{all.Count} permission(s)"), 22, 13));
			for (int i = 0; i < all.Count; i++) {
				BccPermissions.PermissionInfo p = all[i];
				menu.Items.Add(new BackgroundColor(UiManager.Row(40,
					(UiManager.Text($"{Style.Cmd(p.Permission)} {Style.SubHeader("[" + p.Mod + "]")}\n{Style.Muted(TextHelper.Truncate(p.Description, 90))}", 38, 13), W - 120),
					(UiManager.Button("act", "pick:" + i, "Add", 104), 110)), W, 44, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
		}

		// ------------------------------------------------------------------ actions

		public static void Action(Players.Player player, UiManager.PageState state, string action, string item, JObject storage)
		{
			// a running test can always be ended - the tested role may not manage roles
			if (action == "endtest") {
				if (!RoleTestManager.End(player, "ended by you")) {
					state.Messages.Add(Style.Muted("No role test running."));
				}
				UiManager.Render(player);
				return;
			}
			if (!BccPermissions.CanEditConfig(player)) {
				state.Messages.Add(Style.Error("You don't have permission to manage roles."));
				UiManager.Render(player);
				return;
			}
			string error = null;
			string ok = null;
			string role = state.Key;
			switch (action) {
				case "create": {
					string name = storage.Value<string>("new_name");
					error = RoleManager.Create(name, storage.Value<string>("new_parent"), storage.Value<string>("new_child"));
					if (error == null) {
						UiManager.Open(player, new UiManager.PageState { Kind = "role", Key = name.Trim() });
						return;
					}
					break;
				}
				case "edit":
					UiManager.Open(player, new UiManager.PageState { Kind = "role", Key = item });
					return;
				case "test":
				case "testthis": {
					string tested = action == "test" ? item : role;
					error = RoleTestManager.Start(player, tested);
					ok = error == null ? $"You now test {tested} - your roles come back automatically." : null;
					Log.Write($"[BCC UI] {player.Name}: role test {tested} -> {error ?? "started"}");
					break;
				}
				case "rename": {
					string newName = (storage.Value<string>("rename") ?? "").Trim();
					error = RoleManager.Rename(role, newName);
					if (error == null) {
						state.Key = newName;
						ok = $"Renamed to {newName}.";
					}
					break;
				}
				case "default":
					RoleManager.SetDefault(role);
					ok = "New players now get this role.";
					break;
				case "savefmt":
					RoleManager.SetFormatting(role, storage.Value<string>("f_prefix"), storage.Value<string>("f_prefixcolor"), storage.Value<string>("f_name"), storage.Value<string>("f_text"));
					ok = "Formatting saved.";
					break;
				case "rmchild":
					RoleManager.RemoveInclude(role, item);
					ok = $"No longer inherits from {item}.";
					break;
				case "addchild":
					error = RoleManager.AddInclude(role, storage.Value<string>("add_child"));
					ok = error == null ? "Saved." : null;
					break;
				case "rmparent":
					RoleManager.RemoveInclude(item, role);
					ok = $"Removed from {item}.";
					break;
				case "addparent": {
					string parent = (storage.Value<string>("add_parent") ?? "").Trim();
					error = RoleManager.Get(parent) == null ? $"There is no role '{parent}'." : RoleManager.AddInclude(parent, role);
					ok = error == null ? "Saved." : null;
					break;
				}
				case "rmperm":
					RoleManager.RemovePermission(role, item);
					ok = $"Removed {item}.";
					break;
				case "addperm":
					error = RoleManager.AddPermission(role, storage.Value<string>("add_perm"));
					ok = error == null ? "Permission added." : null;
					break;
				case "browse":
					UiManager.Open(player, new UiManager.PageState { Kind = "permpicker", Key = role });
					return;
				case "pick":
					if (role.StartsWith("player:")) {
						Players.Player target = UiPlayerPages.FindPlayer(role.Substring(7));
						if (target == null) { error = "Player not found."; break; }
						PermissionsManager.AddPermissionToUser(player, target, item);
						BccPermissions.Refresh(true);
						ModerationLog.Add(target, "Permission added", player, item, false);
						ok = $"Added {item} to {target.Name}.";
						break;
					}
					error = RoleManager.AddPermission(role, item);
					ok = error == null ? $"Added {item}." : null;
					break;
				case "savepy":
					error = SavePytonia(role, storage);
					ok = error == null ? "Saved to Pytonia Automation." : null;
					break;
				case "addplayer": {
					string name = (storage.Value<string>("add_player") ?? "").Trim();
					if (!PlayerHelper.TryGetPlayer(name, out Players.Player target, out string perr, true)) {
						error = $"Player '{name}' not found: {perr}";
						break;
					}
					if (!target.ID.HasSteamID) {
						error = UiPlayerPages.NoSteamText;
						break;
					}
					RoleIndex.Holding already = RoleIndex.Find(target, role);
					if (already != null) {
						error = $"{target.Name} already has the role {role} ({TextHelper.StripRichText(RoleIndex.Describe(already))}).";
						break;
					}
					RoleTestManager.EndIfActive(target, "an admin changed your roles");
					PermissionsManager.AddGroupToUser(player, target, role);
					BccPermissions.Refresh(true);
					ModerationLog.Add(target, "Role added", player, role, false);
					ok = $"{target.Name} now has the role {role}.";
					break;
				}
				case "rmplayer": {
					Players.Player target = UiPlayerPages.FindPlayer(item);
					if (target == null) {
						error = "Player not found.";
						break;
					}
					RoleTestManager.EndIfActive(target, "an admin changed your roles");
					error = RoleIndex.RemoveRole(player, target, role);
					if (error != null) {
						break;
					}
					ModerationLog.Add(target, "Role removed", player, role, false);
					ok = $"{target.Name} no longer has the role {role}.";
					break;
				}
				case "savehomes": {
					string v = (storage.Value<string>("homes_limit") ?? "").Trim();
					Dictionary<string, int> limits = BccSettingsManager.Settings.Homes.RoleLimits;
					if (v.Length == 0) {
						limits.Remove(role);
					} else if (int.TryParse(v, out int limit)) {
						limits[role] = limit;
					} else {
						error = "The limit must be a whole number.";
					}
					if (error == null) {
						BccSettingsManager.Save();
						ok = "Home limit saved.";
					}
					break;
				}
				case "delete":
					UiManager.Open(player, new UiManager.PageState { Kind = "roledelete", Key = role });
					return;
				case "confirmdelete":
					error = RoleManager.Delete(role);
					if (error == null) {
						// back past the deleted role's page to the role list
						UiManager.Back(player);
						UiManager.PageState cur = UiManager.Current(player);
						if (cur != null && cur.Kind == "role") {
							UiManager.Back(player);
						}
						cur = UiManager.Current(player);
						if (cur != null) {
							cur.Messages.Add(Style.Success($"Role {role} deleted."));
							UiManager.Render(player);
						}
						return;
					}
					break;
			}
			if (error != null) {
				state.Messages.Add(Style.Error(error));
			} else if (ok != null) {
				state.Messages.Add(Style.Success(ok));
			}
			UiManager.Render(player);
		}

		static string SavePytonia(string role, JObject storage)
		{
			if (!PytoniaBridge.IsReady || !(PytoniaBridge.Roles() is IDictionary roles) || !roles.Contains(role)) {
				return "Pytonia Automation is not loaded.";
			}
			object rs = roles[role];
			foreach (string f in new[] { "RequiredHoursOnline", "ExcludeFromTimeBasedAssignment", "MaxColonies" }) {
				Type t = PytoniaBridge.FieldType(rs, f);
				if (t == null || !storage.TryGetValue("py_" + f, out JToken token)) continue;
				try {
					if (t == typeof(bool)) {
						if (token.Type == JTokenType.Boolean) PytoniaBridge.SetField(rs, f, token.Value<bool>());
					} else {
						string text = token.ToString().Trim().Replace(',', '.');
						PytoniaBridge.SetField(rs, f, Convert.ChangeType(text, t, CultureInfo.InvariantCulture));
					}
				} catch {
					return $"{f}: '{token}' is not a valid value.";
				}
			}
			PytoniaBridge.Save();
			return null;
		}
	}
}
