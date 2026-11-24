using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NetworkUI;
using NetworkUI.Items;
using Pipliz;
using UnityEngine;

namespace ColonyCommands
{
	// One server popup window ("bcc.ui") per player with a navigation history, so every page has a
	// working "Back" button to the previously shown page. Buttons are identified as
	// "bcc.ui|<action>|<argument>"; row buttons only carry an index into the rows the page itself
	// rendered (kept server side), so a client can never inject a command or a target.
	// Every page re-checks permissions when it is built AND when one of its buttons is used.
	public static class UiManager
	{
		public const string MENU_ID = "bcc.ui";
		public const string BTN = "bcc.ui|";
		const int MAX_HISTORY = 25;

		public class PageState
		{
			public string Kind;
			public int Page = 1;
			public string Filter = "";
			public string Key = "";
			public string Title = "";
			public List<string> Lines = new List<string>();
			public List<string> RowKeys = new List<string>();
			public List<string> Messages = new List<string>();
			// extra row lists of a page (e.g. role page: permissions, sub roles, parents), addressed by "act|<list>:<index>"
			public Dictionary<string, List<string>> Lists = new Dictionary<string, List<string>>();
			public string Sort = "";
			// where the first page of the window was opened from ("colonytab" = the game's Colony tab)
			public string Origin = "";
		}

		// the game's own button that closes the popup and opens the inventory on the Colony tab
		public const string GAME_OPEN_COLONY_TAB = "bannerplacementpopup.opencolonytab";

		static readonly Dictionary<Players.Player, List<PageState>> history = new Dictionary<Players.Player, List<PageState>>();

		static BccSettings.UiSettings Cfg => BccSettingsManager.Settings.Ui;
		public static int Width => Cfg.WindowWidth;
		// leaves room on the right for the popup's scrollbar, so buttons and input fields never touch it
		public static int ContentWidth => Cfg.WindowWidth - 64;

		// set while a command is executed from a window: native windows it opens are pushed onto the
		// current history instead of starting a new one, so "Back" returns to where the player came from
		public static bool PushMode;

		// lists scroll; they are only split into pages beyond this many lines (bcc-settings.json Ui.MaxLinesPerPage)
		public static int LinesPerPage => Cfg.MaxLinesPerPage;

		// ------------------------------------------------------------------ navigation

		public static void Open(Players.Player player, PageState state, bool newHistory = false)
		{
			if (player == null || state == null) {
				return;
			}
			if (PushMode) {
				newHistory = false;
			}
			if (!history.TryGetValue(player, out List<PageState> stack) || newHistory) {
				stack = new List<PageState>();
				history[player] = stack;
			}
			stack.Add(state);
			while (stack.Count > MAX_HISTORY) {
				stack.RemoveAt(0);
			}
			Render(player);
		}

		public static PageState Current(Players.Player player)
		{
			if (history.TryGetValue(player, out List<PageState> stack) && stack.Count > 0) {
				return stack[stack.Count - 1];
			}
			return null;
		}

		static bool CanGoBack(Players.Player player)
		{
			return history.TryGetValue(player, out List<PageState> stack) && stack.Count > 1;
		}

		public static void Back(Players.Player player)
		{
			if (history.TryGetValue(player, out List<PageState> stack) && stack.Count > 1) {
				stack.RemoveAt(stack.Count - 1);
				Render(player);
			} else {
				Close(player);
			}
		}

		public static void Close(Players.Player player)
		{
			history.Remove(player);
			NetworkMenuManager.CloseServerPopup(player);
		}

		public static void OnPlayerDisconnected(Players.Player player)
		{
			history.Remove(player);
		}

		public static void Render(Players.Player player)
		{
			PageState state = Current(player);
			if (state == null) {
				return;
			}
			NetworkMenu menu = NewMenu();
			try {
				switch (state.Kind) {
					case "help": UiPages.BuildHelp(player, state, menu); break;
					case "cmd": UiPages.BuildCommand(player, state, menu); break;
					case "output": UiPages.BuildOutput(player, state, menu); break;
					case "mods": UiPages.BuildModList(player, state, menu); break;
					case "mod": UiPages.BuildMod(player, state, menu); break;
					case "perms": UiPages.BuildPermissions(player, state, menu); break;
					case "config": UiConfigPages.BuildSections(player, state, menu); break;
					case "configsection": UiConfigPages.BuildSection(player, state, menu); break;
					case "settings": UiPages.BuildSettings(player, state, menu); break;
					case "roles": UiRolePages.BuildRoles(player, state, menu); break;
					case "role": UiRolePages.BuildRole(player, state, menu); break;
					case "roledelete": UiRolePages.BuildDeleteConfirm(player, state, menu); break;
					case "permpicker": UiRolePages.BuildPermissionPicker(player, state, menu); break;
					case "homes": UiPages.BuildHomes(player, state, menu); break;
					case "announce": UiAnnouncementPages.Build(player, state, menu); break;
					case "players": UiPlayerPages.BuildList(player, state, menu); break;
					case "player": UiPlayerPages.BuildPlayer(player, state, menu); break;
					case "playerban": UiPlayerPages.BuildBanConfirm(player, state, menu); break;
					case "colonydelete": UiPlayerPages.BuildColonyDeleteConfirm(player, state, menu); break;
					case "announceedit": UiAnnouncementPages.BuildEdit(player, state, menu); break;
					case "trust": UiTrustPages.BuildTrust(player, state, menu); break;
					case "trustplayer": UiTrustPages.BuildPlayer(player, state, menu); break;
					default: UiPages.BuildOutput(player, state, menu); break;
				}
			} catch (Exception e) {
				Log.WriteException("Better Colony Commands: UI page build failed", e);
				menu = NewMenu();
				Header(player, menu, "Error");
				menu.Items.Add(Text(Style.Error("This page could not be shown. Details are in the server log.")));
			}
			state.Messages.Clear();
			NetworkMenuManager.SendServerPopup(player, menu);
		}

		static NetworkMenu NewMenu()
		{
			NetworkMenu menu = new NetworkMenu {
				Identifier = MENU_ID,
				Width = Cfg.WindowWidth,
				Height = Cfg.WindowHeight,
				IsInteractive = true,
				ForceClosePopups = true
			};
			// the popup's title bar (client default "Server Popup")
			menu.LocalStorage.SetAs("header", WindowTitle);
			return menu;
		}

		static string windowTitle;
		static string WindowTitle {
			get {
				if (windowTitle == null) {
					windowTitle = "<b>" + Gradient("Better Colony Commands", new Color32(255, 209, 102, 255), new Color32(255, 107, 107, 255)) + "</b>";
				}
				return windowTitle;
			}
		}

		// per character color gradient (TextMeshPro has no gradient tag without a preset asset)
		public static string Gradient(string text, Color32 from, Color32 to)
		{
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			int n = System.Math.Max(1, text.Length - 1);
			for (int i = 0; i < text.Length; i++) {
				float t = (float)i / n;
				byte r = (byte)(from.r + (to.r - from.r) * t);
				byte g = (byte)(from.g + (to.g - from.g) * t);
				byte b = (byte)(from.b + (to.b - from.b) * t);
				if (text[i] == ' ') {
					sb.Append(' ');
				} else {
					sb.Append($"<color=#{r:X2}{g:X2}{b:X2}>{text[i]}</color>");
				}
			}
			return sb.ToString();
		}

		// output window for a finished command, or for other mods via BccApi
		public static void ShowOutput(Players.Player player, string title, List<string> lines, bool newHistory)
		{
			PageState state = new PageState { Kind = "output", Title = title ?? "Output", Lines = lines ?? new List<string>() };
			Open(player, state, newHistory);
		}

		// ------------------------------------------------------------------ button handling

		public static void OnPlayerPushedNetworkUIButton(ButtonPressCallbackData data)
		{
			if (data.Player != null && data.ButtonIdentifier == GAME_OPEN_COLONY_TAB) {
				// the game closes our window and opens the Colony tab - forget the window's pages
				history.Remove(data.Player);
				return;
			}
			if (data.Player == null || data.ButtonIdentifier == null || !data.ButtonIdentifier.StartsWith(BTN, StringComparison.Ordinal)) {
				return;
			}
			Players.Player player = data.Player;
			string[] parts = data.ButtonIdentifier.Split('|');
			string action = parts.Length > 1 ? parts[1] : "";
			string arg = parts.Length > 2 ? parts[2] : "";
			PageState state = Current(player);
			JObject storage = data.Storage ?? new JObject();
			try {
				switch (action) {
					case "back":
						Back(player);
						return;
					case "close":
						Close(player);
						return;
					case "prev":
						if (state != null) { state.Page = System.Math.Max(1, state.Page - 1); Render(player); }
						return;
					case "next":
						if (state != null) { state.Page++; Render(player); }
						return;
					case "open":
						OpenNamed(player, arg);
						return;
					case "trust":
						// "Manage Permission" next to "Manage Owners" in the Colony tab (arg = colony id) - its
						// Back button returns to the Colony tab
						Open(player, new PageState { Kind = "trust", Key = arg, Origin = "colonytab" }, true);
						return;
					case "filter":
						if (state != null) {
							state.Filter = (storage.Value<string>("filter") ?? "").Trim();
							state.Page = 1;
							Render(player);
						}
						return;
					case "row":
						if (state != null && int.TryParse(arg, out int idx) && idx >= 0 && idx < state.RowKeys.Count) {
							OpenRow(player, state, state.RowKeys[idx]);
						}
						return;
					case "exec":
						if (state != null && state.Kind == "cmd") {
							UiPages.ExecuteFromUi(player, state, storage);
						}
						return;
					case "cfgsave":
						if (state != null && state.Kind == "configsection") {
							UiConfigPages.Save(player, state, storage);
						}
						return;
					case "setsave":
						if (state != null && state.Kind == "settings") {
							UiPages.SaveSettings(player, state, storage);
						}
						return;
					case "sort":
						if (state != null) {
							state.Sort = arg;
							Render(player);
						}
						return;
					case "act":
						// page specific actions (roles, homes): arg = "<action>" or "<list>:<index>"
						if (state != null) {
							PageAction(player, state, arg, storage);
						}
						return;
				}
			} catch (Exception e) {
				Log.WriteException("Better Colony Commands: UI button failed", e);
			}
		}

		// row button action -> name of the row list the page stored
		static readonly Dictionary<string, string> ListAliases = new Dictionary<string, string> {
			{ "edit", "roles" }, { "test", "roles" }, { "rmchild", "children" }, { "rmparent", "parents" }, { "rmperm", "perms" },
			{ "pick", "pick" }, { "tp", "homes" }, { "del", "homes" },
			{ "toggle", "msgs" }, { "editmsg", "msgs" }, { "up", "msgs" }, { "down", "msgs" }, { "sendnow", "msgs" }, { "delmsg", "msgs" }
		};

		static void PageAction(Players.Player player, PageState state, string arg, JObject storage)
		{
			string listName = null;
			int index = -1;
			int colon = arg.IndexOf(':');
			if (colon > 0 && int.TryParse(arg.Substring(colon + 1), out int idx)) {
				listName = arg.Substring(0, colon);
				index = idx;
				arg = listName;
			}
			string item = null;
			if (listName != null) {
				// the list is stored under the action name or under the name of the rows it belongs to
				List<string> list = null;
				if (!state.Lists.TryGetValue(listName, out list) && ListAliases.TryGetValue(listName, out string alias)) {
					state.Lists.TryGetValue(alias, out list);
				}
				if (list == null || index < 0 || index >= list.Count) {
					Log.WriteWarning($"Better Colony Commands: UI action '{listName}:{index}' on page '{state.Kind}' has no matching row");
					return;
				}
				item = list[index];
			}
			switch (state.Kind) {
				case "roles":
				case "role":
				case "roledelete":
				case "permpicker":
					UiRolePages.Action(player, state, arg, item, storage);
					break;
				case "homes":
					UiPages.HomesAction(player, state, arg, item, storage);
					break;
				case "trust":
				case "trustplayer":
					UiTrustPages.Action(player, state, arg, item, storage);
					break;
				case "announce":
				case "announceedit":
					UiAnnouncementPages.Action(player, state, arg, item, storage);
					break;
				case "players":
				case "player":
				case "playerban":
				case "colonydelete":
					UiPlayerPages.Action(player, state, arg, item, storage);
					break;
			}
		}

		public static void OpenNamed(Players.Player player, string name, bool newHistory = false)
		{
			switch (name) {
				case "roles":
					if (BccPermissions.CanEditConfig(player)) Open(player, new PageState { Kind = "roles" }, newHistory);
					break;
				case "homes":
					if (HomesManager.IsActive) Open(player, new PageState { Kind = "homes" }, newHistory);
					break;
				case "announce":
					if (UiAnnouncementPages.CanEdit(player)) Open(player, new PageState { Kind = "announce" }, newHistory);
					break;
				case "players":
					if (UiPlayerPages.CanOpen(player)) Open(player, new PageState { Kind = "players" }, newHistory);
					break;
				case "help": Open(player, new PageState { Kind = "help" }, newHistory); break;
				case "mods": Open(player, new PageState { Kind = "mods" }, newHistory); break;
				case "perms":
					if (BccPermissions.CanSeePermissionOverview(player)) Open(player, new PageState { Kind = "perms" }, newHistory);
					break;
				case "config":
					if (BccPermissions.CanEditConfig(player)) Open(player, new PageState { Kind = "config" }, newHistory);
					break;
				case "settings": Open(player, new PageState { Kind = "settings" }, newHistory); break;
			}
		}

		static void OpenRow(Players.Player player, PageState state, string key)
		{
			switch (state.Kind) {
				case "help":
					Open(player, new PageState { Kind = "cmd", Key = key });
					break;
				case "mods":
					Open(player, new PageState { Kind = "mod", Key = key });
					break;
				case "config":
					UiConfigPages.OpenSection(player, key);
					break;
			}
		}

		// ------------------------------------------------------------------ widgets

		public static Label Text(string text, int height = 24, int fontSize = 16, ELabelAlignment align = ELabelAlignment.MiddleLeft)
		{
			return new Label(new LabelData(text ?? "", align, fontSize, LabelData.ELocalizationType.None), height);
		}

		public static ButtonCallback Button(string action, string arg, string text, int width, bool enabled = true, int height = 30)
		{
			string id = BTN + action + (arg != null ? "|" + arg : "");
			return new ButtonCallback(id, new LabelData(text, ELabelAlignment.MiddleCenter, 15, LabelData.ELocalizationType.None), width, height,
				ButtonCallback.EOnClickActions.None, null, 0f, 0f, enabled);
		}

		public static HorizontalRow Row(int height, params (IItem, int)[] items)
		{
			return new HorizontalRow(new List<(IItem, int)>(items), height);
		}

		// small Better Colony Commands logo (ui-logo.png in the mod folder), shown centered under the
		// window title on every page; the title bar itself can't show images (TextMeshPro text only)
		public static FileTable.FileID LogoFile;
		const int LOGO_SIZE = 32;

		public static void RegisterAssets()
		{
			try {
				string path = System.IO.Path.Combine(AntiGrief.MOD_DIRECTORY ?? "", "ui-logo.png");
				if (ServerManager.FileTable != null && System.IO.File.Exists(path)) {
					LogoFile = ServerManager.FileTable.StartLoading(System.IO.Path.GetFullPath(path), Shared.ECachedFileType.Image, true);
				}
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not register ui-logo.png: {e.Message}");
			}
		}

		public static void Header(Players.Player player, NetworkMenu menu, string title)
		{
			PageState first = history.TryGetValue(player, out List<PageState> pages) && pages.Count > 0 ? pages[0] : null;
			Header(player, menu, title, first != null && first.Origin == "colonytab" ? GAME_OPEN_COLONY_TAB : null);
		}

		// backId: button id used for "< Back" when there is no earlier page of our own (e.g. back to the game's Colony tab)
		public static void Header(Players.Player player, NetworkMenu menu, string title, string backId)
		{
			int w = ContentWidth;
			if (LogoFile.IsValid) {
				int side = (w - LOGO_SIZE) / 2;
				menu.Items.Add(new HorizontalRow(new List<(IItem, int)> {
					(new EmptySpace(LOGO_SIZE), side),
					(new FileIcon(LogoFile, LOGO_SIZE), LOGO_SIZE),
					(new EmptySpace(LOGO_SIZE), side)
				}, LOGO_SIZE + 2));
			}
			List<(IItem, int)> items = new List<(IItem, int)>();
			if (CanGoBack(player)) {
				items.Add((Button("back", null, "< Back", 90), 94));
			} else if (backId != null) {
				items.Add((new ButtonCallback(backId, new LabelData("< Back", ELabelAlignment.MiddleCenter, 15, LabelData.ELocalizationType.None), 90, 30,
					ButtonCallback.EOnClickActions.None, null, 0f, 0f, true), 94));
			} else {
				items.Add((new EmptySpace(30), 94));
			}
			items.Add((Text(Style.Header(title), 34, 18), w - 94 - 94));
			items.Add((Button("close", null, "Close", 90), 94));
			menu.Items.Add(new HorizontalRow(items, 36));
			menu.Items.Add(new Line());
		}

		// section / mod heading: dark bar with a gold accent strip and gold bold text (contrast ~10:1
		// against the window background, clearly different from the blue command rows)
		public static void SectionHeader(NetworkMenu menu, string text)
		{
			int w = ContentWidth;
			HorizontalRow inner = new HorizontalRow(new List<(IItem, int)> {
				(new BackgroundColor(new EmptySpace(22), 5, 24, 0f, 0f, 0f, 0f, new Color32(255, 209, 102, 255)), 9),
				(Text($"<b><color=#FFD166>{text}</color></b>", 24, 16), w - 20)
			}, 24, 0f, 0f, 4f);
			menu.Items.Add(new BackgroundColor(inner, w, 28, 0f, 0f, 0f, 2f, new Color32(30, 22, 18, 210)));
		}

		// orange notice bar about how permissions work in singleplayer / LAN / hosted games
		public static void PermissionNotice(NetworkMenu menu, Players.Player viewer)
		{
			string notice = NetworkInfo.PermissionNotice(viewer);
			if (notice == null) {
				return;
			}
			int w = ContentWidth;
			menu.Items.Add(new BackgroundColor(Text($"<b><color=#FFB347>Note:</color></b> <color=#FFE2B8>{notice}</color>", -1, 14), w, 44, 0f, 0f, 8f, 5f, new Color32(120, 60, 0, 120)));
		}

		public static void Messages(NetworkMenu menu, PageState state)
		{
			foreach (string m in state.Messages) {
				menu.Items.Add(Text(m, 22, 15));
			}
		}

		// "< Prev   Page x / y   Next >" - only when there is more than one page
		public static void Pager(NetworkMenu menu, int page, int pageCount)
		{
			if (pageCount <= 1) {
				return;
			}
			int w = ContentWidth;
			menu.Items.Add(new Line());
			menu.Items.Add(Row(34,
				(Button("prev", null, "< Prev", 110, page > 1), 114),
				(Text($"Page {page} / {pageCount}", 30, 15, ELabelAlignment.MiddleCenter), w - 228),
				(Button("next", null, "Next >", 110, page < pageCount), 114)));
		}

		public static void FilterRow(NetworkMenu menu, string current, string placeholderLabel)
		{
			int w = ContentWidth;
			menu.LocalStorage.SetAs("filter", current ?? "");
			menu.Items.Add(Row(32,
				(Text(placeholderLabel, 30, 15), 110),
				(new InputField("filter", w - 110 - 110, 30), w - 220),
				(Button("filter", null, "Search", 104), 110)));
		}

		// BackgroundColor turns a fully transparent color (alpha 0) into opaque white - so both
		// alternating colors need a small, non-zero alpha
		public static Color32 RowColor(int i)
		{
			// slightly darker than the window: light text stays readable (contrast checked against a screenshot)
			return i % 2 == 0 ? new Color32(0, 0, 0, 12) : new Color32(0, 0, 0, 34);
		}
	}
}
