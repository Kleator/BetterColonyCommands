using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NetworkUI;
using NetworkUI.Items;
using Pipliz;

namespace ColonyCommands
{
	// Trust windows for players (no moderator rights needed - everyone manages their own colonies):
	//  trust        (Key = colony id) "Manage Permission": players trusted on this claim (Remove, Angry
	//               Guards toggle, Player), add players (online first, search for offline ones)
	//  trustplayer  (Key = player key) one player and all claims of the viewer: trust per claim, on all
	//               claims, Angry Guards toggle for all of the viewer's guards
	// Opened by the "Manage Permission" button next to "Manage Owners" in the Colony tab, /trust and /help.
	public static class UiTrustPages
	{
		const int MAX_CANDIDATES = 50;
		const string RED = "#FF4D4D";

		static string Hostile(string text = "hostile") => $"<color={RED}>{text}</color>";
		static string Friendly(string text = "friendly") => Style.Success(text);
		static int W => UiManager.ContentWidth;

		// ------------------------------------------------------------------ Colony tab button

		public static void AddColonyTabButton(Players.Player player, Table left)
		{
			Colony c = player?.ActiveColony;
			if (c == null || left?.Rows == null || !TrustManager.CanManage(player, c)) {
				return;
			}
			ButtonCallback mine = new ButtonCallback(UiManager.BTN + "trust|" + c.ColonyID.Value,
				new LabelData("Manage Permission", ELabelAlignment.MiddleCenter, 16, LabelData.ELocalizationType.None), 150, 30,
				ButtonCallback.EOnClickActions.None, null, 0f, 0f, true);
			for (int i = 0; i < left.Rows.Count; i++) {
				if (left.Rows[i] is ButtonCallback owners && owners.UniqueIdentifier == "colonymanagement.startmanage") {
					left.Rows[i] = new HorizontalRow(new List<(IItem, int)> { (owners, 154), (mine, 154) }, 30);
					return;
				}
			}
			left.Rows.Add(mine);
		}

		// ------------------------------------------------------------------ Manage Permission (one claim)

		public static void BuildTrust(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "Manage Permission");
			Colony c = TrustManager.GetColony(state.Key);
			if (c == null) {
				menu.Items.Add(UiManager.Text(Style.Error("This colony doesn't exist (any more).")));
				return;
			}
			if (!TrustManager.CanManage(player, c)) {
				menu.Items.Add(UiManager.Text(Style.Error("Only the owners of this colony can choose who may build there.")));
				return;
			}
			UiManager.PermissionNotice(menu, player);
			UiManager.Messages(menu, state);
			menu.Items.Add(UiManager.Text("<b>" + TrustManager.Describe(c) + "</b>", 30, 18));
			List<string> ownerNames = new List<string>();
			for (int i = 0; i < c.ColonyGroup.Owners.Count; i++) {
				ownerNames.Add(ChatColors.GetDisplayName(c.ColonyGroup.Owners.GetAt(i)));
			}
			menu.Items.Add(UiManager.Text(Style.Muted("Owners: ") + string.Join(", ", ownerNames), 24, 14));
			menu.Items.Add(UiManager.Text(Style.Muted("Trusted players may build and place banners in this colony's banner area. Stockpile and jobs: \"Manage Owners\" in the Colony tab."), -1, 13));
			bool guards = TrustManager.GuardsAvailable;
			if (guards) {
				menu.Items.Add(UiManager.Text(Style.Muted("Angry Guards: \"Guards: ") + Friendly() + Style.Muted("\" = the guards of this colony don't attack the player, \"") + Hostile() + Style.Muted("\" = they do (friend list of the owners, valid for all their colonies)."), -1, 13));
			}

			// --- trusted players
			List<TrustManager.TrustEntry> trusted = TrustManager.TrustedOn(c);
			menu.Items.Add(new EmptySpace(4));
			UiManager.SectionHeader(menu, $"Trusted on this claim ({trusted.Count})");
			List<string> keys = trusted.Select(e => e.Player.ID.ToStringReadable()).ToList();
			state.Lists["untrust"] = keys;
			state.Lists["guard"] = keys;
			state.Lists["pview"] = keys;
			bool staff = PermissionsManager.HasPermission(player, AntiGrief.PERMISSION_SUPER);
			for (int i = 0; i < trusted.Count; i++) {
				TrustManager.TrustEntry e = trusted[i];
				string kind = e.Kind == TrustManager.TrustKind.Claim ? "this claim" : $"all claims of {ChatColors.GetDisplayName(e.Owner)} (/antigrief banner)";
				string label = ChatColors.GetDisplayName(e.Player) + "  " + Style.Muted(kind) + (UiPlayerPages.IsOnline(e.Player) ? "  " + Style.Success("online") : "");
				List<(IItem, int)> cells = new List<(IItem, int)>();
				int buttons = (guards ? 166 : 0) + 106 + 106;
				cells.Add((UiManager.Text(label, 28, 14), W - buttons));
				if (guards) {
					bool friendly = TrustManager.IsFriendlyFor(c, e.Player);
					cells.Add((UiManager.Button("act", "guard:" + i, "Guards: " + (friendly ? Friendly() : Hostile()), 160, true, 28), 166));
				}
				cells.Add((UiManager.Button("act", "pview:" + i, "Player", 100, true, 28), 106));
				bool canRemove = e.Kind == TrustManager.TrustKind.Claim || e.Owner == player || staff;
				cells.Add((UiManager.Button("act", "untrust:" + i, "Remove", 100, canRemove, 28), 106));
				menu.Items.Add(new BackgroundColor(UiManager.Row(30, cells.ToArray()), W, 34, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			if (trusted.Count == 0) {
				menu.Items.Add(UiManager.Text(Style.Muted("Nobody is trusted on this claim yet - choose a player below."), 24, 13));
			}

			// --- add players: online first, search for everyone else
			menu.Items.Add(new EmptySpace(6));
			UiManager.SectionHeader(menu, "Add player");
			UiManager.FilterRow(menu, state.Filter, "Player");
			string f = (state.Filter ?? "").Trim().ToLowerInvariant();
			HashSet<Players.Player> skip = new HashSet<Players.Player>(trusted.Select(e => e.Player)) { player };
			List<Players.Player> candidates = Players.PlayerDatabase.Values
				.Where(p => p != null && p.ID.HasSteamID && !Players.IsServerID(p.ID) && !skip.Contains(p) && !TrustManager.IsOwner(p, c))
				.Where(p => f.Length == 0
					? UiPlayerPages.IsOnline(p)
					: TextHelper.StripRichText(p.Name ?? "").ToLowerInvariant().Contains(f) || p.ID.SteamID.m_SteamID.ToString().Contains(f))
				.GroupBy(p => p.ID.SteamID.m_SteamID)
				.Select(g => g.OrderByDescending(UiPlayerPages.LastSeen).First())
				.OrderByDescending(UiPlayerPages.LastSeen)
				.ThenBy(p => TextHelper.StripRichText(p.Name ?? ""), StringComparer.OrdinalIgnoreCase)
				.ToList();
			if (f.Length == 0) {
				menu.Items.Add(UiManager.Text(Style.Muted(candidates.Count == 0
					? "No other player is online right now - type a name and press Search to find offline players."
					: "Players online right now - or type a name and press Search to find offline players."), -1, 13));
			}
			List<Players.Player> shown = candidates.Take(MAX_CANDIDATES).ToList();
			List<string> ckeys = shown.Select(p => p.ID.ToStringReadable()).ToList();
			state.Lists["trust"] = ckeys;
			state.Lists["cview"] = ckeys;
			for (int i = 0; i < shown.Count; i++) {
				Players.Player p = shown[i];
				string seen = UiPlayerPages.IsOnline(p) ? Style.Success("online") : Style.Muted("last seen " + ActivityTracker.GetLastSeen(p.ID.ToStringReadable()));
				menu.Items.Add(new BackgroundColor(UiManager.Row(30,
					(UiManager.Text(ChatColors.GetDisplayName(p) + "  " + seen, 28, 14), W - 212),
					(UiManager.Button("act", "trust:" + i, "Trust", 100, p.ID.HasSteamID, 28), 106),
					(UiManager.Button("act", "cview:" + i, "Player", 100, true, 28), 106)), W, 34, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			if (candidates.Count > shown.Count) {
				menu.Items.Add(UiManager.Text(Style.Muted($"... and {candidates.Count - shown.Count} more - type more of the name."), -1, 12));
			}
			if (f.Length > 0 && candidates.Count == 0) {
				menu.Items.Add(UiManager.Text(Style.Muted("No player found."), 24, 13));
			}
		}

		// ------------------------------------------------------------------ one player and all own claims

		public static void BuildPlayer(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			Players.Player target = UiPlayerPages.FindPlayer(state.Key);
			UiManager.Header(player, menu, target != null ? "Player: " + TextHelper.SafeName(target.Name) : "Player");
			if (target == null) {
				menu.Items.Add(UiManager.Text(Style.Error("Player not found.")));
				return;
			}
			UiManager.Messages(menu, state);
			string seen = UiPlayerPages.IsOnline(target) ? Style.Success("online") : Style.Muted("last seen " + ActivityTracker.GetLastSeen(target.ID.ToStringReadable()));
			menu.Items.Add(UiManager.Text(ChatColors.GetDisplayName(target) + "   " + seen, 30, 17));
			if (target == player) {
				menu.Items.Add(UiManager.Text(Style.Muted("This is you."), 24, 14));
				return;
			}
			if (!target.ID.HasSteamID) {
				menu.Items.Add(UiManager.Text(Style.Error(UiPlayerPages.NoSteamText), -1, 13));
				return;
			}

			if (TrustManager.GuardsAvailable) {
				bool friendly = TrustManager.IsFriendly(player, target);
				menu.Items.Add(new EmptySpace(4));
				UiManager.SectionHeader(menu, "Angry Guards");
				menu.Items.Add(UiManager.Row(34,
					(UiManager.Text(friendly ? Style.Success("Your guards treat this player as a friend.") : "Your guards attack this player like anyone else.", 30, 14), W - 226),
					(UiManager.Button("act", "guardall", "Make " + (friendly ? Hostile() : Friendly()), 216), 226)));
				menu.Items.Add(UiManager.Text(Style.Muted("Applies to the guards of all your colonies."), -1, 12));
			}

			List<Colony> mine = TrustManager.OwnColonies(player);
			menu.Items.Add(new EmptySpace(4));
			UiManager.SectionHeader(menu, $"Your claims ({mine.Count})");
			if (mine.Count == 0) {
				menu.Items.Add(UiManager.Text(Style.Muted("You don't own a colony yet."), 24, 14));
				return;
			}
			List<string> own = PermissionsHelper.GetOwnPermissions(target);
			List<string> states = mine.Select(c => StateOf(target, c, own)).ToList();
			int trustedCount = states.Count(s => s != null);
			menu.Items.Add(UiManager.Row(34,
				(UiManager.Text(Style.Muted($"{TextHelper.SafeName(target.Name)} may build on {trustedCount} of your {mine.Count} claims."), 30, 14), W - 452),
				(UiManager.Button("act", "trustall", "Trust on all my claims", 216, trustedCount < mine.Count), 226),
				(UiManager.Button("act", "untrustall", "Remove from all", 216, states.Any(s => s == "claim" || s == "owner")), 226)));
			List<string> ids = mine.Select(c => c.ColonyID.Value.ToString()).ToList();
			state.Lists["ctoggle"] = ids;
			state.Lists["copen"] = ids;
			for (int i = 0; i < mine.Count; i++) {
				string s = states[i];
				string label = TrustManager.Describe(mine[i]) + "  " + (s == null ? Style.Muted("not trusted")
					: s == "claim" ? Style.Success("trusted")
					: s == "owner" ? Style.Success("trusted") + Style.Muted(" (all your claims, /antigrief banner)")
					: Style.Success("trusted") + Style.Muted(" (through a role)"));
				menu.Items.Add(new BackgroundColor(UiManager.Row(30,
					(UiManager.Text(label, 28, 14), W - 212),
					(UiManager.Button("act", "ctoggle:" + i, s == null ? "Trust" : "Remove", 100, s != "role", 28), 106),
					(UiManager.Button("act", "copen:" + i, "Open", 100, true, 28), 106)), W, 34, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			menu.Items.Add(UiManager.Text(Style.Muted("\"Open\" shows everyone who may build on that claim."), -1, 12));
		}

		// claim = this claim, owner = all claims of the viewer (/antigrief banner), role = through a role, null = not
		static string StateOf(Players.Player target, Colony c, List<string> targetOwn)
		{
			if (!TrustManager.IsTrusted(target, c)) {
				return null;
			}
			if (targetOwn.Contains(TrustManager.ClaimPermission(c))) {
				return "claim";
			}
			for (int i = 0; i < c.ColonyGroup.Owners.Count; i++) {
				Players.Player o = c.ColonyGroup.Owners.GetAt(i);
				if (o != null && o.ID.HasSteamID && targetOwn.Contains(TrustManager.OwnerPermission(o))) {
					return "owner";
				}
			}
			return TrustManager.Find(target, c) != null ? "claim" : "role";
		}

		// ------------------------------------------------------------------ actions

		public static void Action(Players.Player player, UiManager.PageState state, string action, string item, JObject storage)
		{
			string error = null, ok = null;
			if (state.Kind == "trust") {
				Colony c = TrustManager.GetColony(state.Key);
				if (c == null || !TrustManager.CanManage(player, c)) {
					error = "Only the owners of this colony can choose who may build there.";
				} else {
					Players.Player target = item != null ? UiPlayerPages.FindPlayer(item) : null;
					switch (action) {
						case "pview":
						case "cview":
							UiManager.Open(player, new UiManager.PageState { Kind = "trustplayer", Key = item });
							return;
						case "trust":
							error = target == null ? "Player not found." : TrustManager.Trust(player, target, c);
							ok = error == null ? $"{target.Name} may build on this claim now." : null;
							break;
						case "untrust":
							error = target == null ? "Player not found." : TrustManager.Untrust(player, target, c);
							ok = error == null ? $"{target.Name} may no longer build on this claim." : null;
							break;
						case "guard":
							if (target == null) {
								error = "Player not found.";
								break;
							}
							bool friendly = !TrustManager.IsFriendlyFor(c, target);
							TrustManager.SetFriendlyFor(c, player, target, friendly);
							ok = friendly ? $"The guards don't attack {target.Name} any more." : $"The guards treat {target.Name} like anyone else again.";
							break;
					}
				}
			} else if (state.Kind == "trustplayer") {
				Players.Player target = UiPlayerPages.FindPlayer(state.Key);
				if (target == null || target == player) {
					error = "Player not found.";
				} else {
					List<Colony> mine = TrustManager.OwnColonies(player);
					switch (action) {
						case "copen":
							UiManager.Open(player, new UiManager.PageState { Kind = "trust", Key = item });
							return;
						case "ctoggle": {
							Colony c = TrustManager.GetColony(item);
							if (c == null || !TrustManager.CanManage(player, c)) {
								error = "Only the owners of this colony can choose who may build there.";
							} else if (TrustManager.IsTrusted(target, c)) {
								error = TrustManager.Untrust(player, target, c);
								ok = error == null ? $"{target.Name} may no longer build at {TextHelper.StripRichText(c.Name ?? "")}." : null;
							} else {
								error = TrustManager.Trust(player, target, c);
								ok = error == null ? $"{target.Name} may build at {TextHelper.StripRichText(c.Name ?? "")} now." : null;
							}
							break;
						}
						case "trustall": {
							int n = 0;
							foreach (Colony c in mine.Where(c => !TrustManager.IsTrusted(target, c))) {
								if (TrustManager.Trust(player, target, c, false) == null) {
									n++;
								}
							}
							if (n > 0 && UiPlayerPages.IsOnline(target)) {
								BccChat.Send(target, Style.Success($"{ChatColors.GetDisplayName(player)} trusted you on {n} claim(s): you may build in their banner areas now."));
							}
							ok = $"{target.Name} may build on all your claims now ({n} added).";
							break;
						}
						case "untrustall": {
							int n = 0;
							foreach (Colony c in mine.Where(c => TrustManager.Find(target, c) != null)) {
								if (TrustManager.Untrust(player, target, c, false) == null) {
									n++;
								}
							}
							if (n > 0 && UiPlayerPages.IsOnline(target)) {
								BccChat.Send(target, Style.Muted($"{ChatColors.GetDisplayName(player)} removed your permission to build on their claims."));
							}
							ok = $"{target.Name} may no longer build on your claims.";
							break;
						}
						case "guardall": {
							bool friendly = !TrustManager.IsFriendly(player, target);
							TrustManager.SetFriendly(player, target, friendly);
							ok = friendly ? $"Your guards don't attack {target.Name} any more." : $"Your guards treat {target.Name} like anyone else again.";
							break;
						}
					}
				}
			}
			if (error != null) {
				state.Messages.Add(Style.Error(error));
			} else if (ok != null) {
				state.Messages.Add(Style.Success(ok));
			}
			UiManager.Render(player);
		}
	}
}
