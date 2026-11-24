using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chatting;
using Newtonsoft.Json.Linq;
using NetworkUI;
using NetworkUI.Items;
using Pipliz;

namespace ColonyCommands
{
	// Announcements (announcements.json) fully by buttons: welcome message, interval, every rotating
	// message with on/off, edit (multi-line, with preview), move up/down, send now, delete, and new
	// messages. Permission mods.scarabol.commands.config (checked on build and on every click).
	public static class UiAnnouncementPages
	{
		static int W => UiManager.ContentWidth;

		// admin config permission, or all announcement permissions (mods.scarabol.commands.announcements)
		public static bool CanEdit(Players.Player player)
		{
			return player != null && (BccPermissions.CanEditConfig(player) || PermissionsManager.HasPermission(player, AntiGrief.MOD_PREFIX + "announcements"));
		}

		static bool Denied(Players.Player player, NetworkMenu menu)
		{
			if (CanEdit(player)) {
				return false;
			}
			menu.Items.Add(UiManager.Text(Style.Error("You don't have permission to change the announcements.")));
			return true;
		}

		static List<AnnouncementMessage> Messages {
			get {
				if (Announcements.config.Messages == null) {
					Announcements.config.Messages = new List<AnnouncementMessage>();
				}
				return Announcements.config.Messages;
			}
		}

		public static void Build(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "Announcements");
			if (Denied(player, menu)) {
				return;
			}
			UiManager.Messages(menu, state);
			AnnouncementConfig cfg = Announcements.config;

			UiManager.SectionHeader(menu, "Welcome message");
			menu.Items.Add(UiManager.Text(Style.Muted("Sent to every player when they join. Empty = none. Rich text allowed."), -1, 12));
			menu.LocalStorage.SetAs("welcome", cfg.welcomeMessage ?? "");
			menu.Items.Add(UiManager.Row(32,
				(new InputField("welcome", W - 200, 30), W - 196),
				(UiManager.Button("act", "savewelcome", "Save", 186), 196)));

			UiManager.SectionHeader(menu, "Interval");
			menu.Items.Add(UiManager.Text(Style.Muted($"Minutes between two announcements (the enabled messages are sent in turn). Minimum {Announcements.MIN_INTERVAL} seconds."), -1, 12));
			menu.LocalStorage.SetAs("interval", (cfg.IntervalSeconds / 60.0).ToString("0.##", CultureInfo.InvariantCulture));
			menu.Items.Add(UiManager.Row(32,
				(new InputField("interval", 160, 30), 166),
				(UiManager.Text(Style.Muted("minutes"), 30, 13), W - 166 - 196),
				(UiManager.Button("act", "saveinterval", "Save", 186), 196)));

			List<AnnouncementMessage> msgs = Messages;
			UiManager.SectionHeader(menu, $"Messages ({msgs.Count(m => m.Enabled)} of {msgs.Count} enabled)");
			state.Lists["msgs"] = Enumerable.Range(0, msgs.Count).Select(i => i.ToString()).ToList();
			int textWidth = W - 6 * 76 - 8;
			for (int i = 0; i < msgs.Count; i++) {
				AnnouncementMessage m = msgs[i];
				string label = (m.Enabled ? "" : Style.Muted("(off) ")) + TextHelper.Truncate(m.Text ?? "", 70);
				menu.Items.Add(new BackgroundColor(UiManager.Row(34,
					(UiManager.Text($"{Style.Muted((i + 1) + ".")} {label}", 30, 14), textWidth),
					(UiManager.Button("act", "toggle:" + i, m.Enabled ? "Off" : "On", 72), 76),
					(UiManager.Button("act", "editmsg:" + i, "Edit", 72), 76),
					(UiManager.Button("act", "up:" + i, "Up", 72, i > 0), 76),
					(UiManager.Button("act", "down:" + i, "Down", 72, i < msgs.Count - 1), 76),
					(UiManager.Button("act", "sendnow:" + i, "Send", 72), 76),
					(UiManager.Button("act", "delmsg:" + i, "Delete", 72), 76)), W, 38, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			if (msgs.Count == 0) {
				menu.Items.Add(UiManager.Text(Style.Muted("No announcements yet."), 22, 13));
			}
			menu.Items.Add(UiManager.Text(Style.Muted("New message:"), 22, 13));
			menu.LocalStorage.SetAs("newmsg", "");
			menu.Items.Add(UiManager.Row(32,
				(new InputField("newmsg", W - 200, 30), W - 196),
				(UiManager.Button("act", "addmsg", "Add", 186), 196)));
		}

		public static void BuildEdit(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "Edit announcement");
			if (Denied(player, menu)) {
				return;
			}
			if (!int.TryParse(state.Key, out int idx) || idx < 0 || idx >= Messages.Count) {
				menu.Items.Add(UiManager.Text(Style.Error("This announcement does not exist (any more).")));
				return;
			}
			UiManager.Messages(menu, state);
			AnnouncementMessage m = Messages[idx];
			menu.Items.Add(UiManager.Text(Style.Muted("Preview:"), 20, 13));
			menu.Items.Add(new BackgroundColor(UiManager.Text(m.Text ?? "", -1, 16), W, 40, 0f, 0f, 6f, 4f, new UnityEngine.Color32(0, 0, 0, 40)));
			menu.Items.Add(UiManager.Text(Style.Muted("Text (rich text such as <b>, <color=#FFD166> is allowed):"), 20, 13));
			menu.LocalStorage.SetAs("amsg", m.Text ?? "");
			menu.Items.Add(new InputField("amsg", W, 90) { Multiline = true });
			menu.LocalStorage.SetAs("aenabled", m.Enabled);
			menu.Items.Add(new Toggle(new LabelData("<b>Enabled</b>", ELabelAlignment.MiddleLeft, 15, LabelData.ELocalizationType.None), "aenabled"));
			menu.Items.Add(new EmptySpace(6));
			menu.Items.Add(UiManager.Row(36,
				(UiManager.Button("back", null, "Cancel", 186), 196),
				(new EmptySpace(30), W - 400),
				(UiManager.Button("act", "savemsg", "Save", 186), 196)));
		}

		public static void Action(Players.Player player, UiManager.PageState state, string action, string item, JObject storage)
		{
			if (!CanEdit(player)) {
				state.Messages.Add(Style.Error("You don't have permission to change the announcements."));
				UiManager.Render(player);
				return;
			}
			List<AnnouncementMessage> msgs = Messages;
			int idx = -1;
			if (item != null && (!int.TryParse(item, out idx) || idx < 0 || idx >= msgs.Count)) {
				UiManager.Render(player);
				return;
			}
			string ok = null, error = null;
			switch (action) {
				case "savewelcome":
					Announcements.config.welcomeMessage = (storage.Value<string>("welcome") ?? "").Trim();
					ok = "Welcome message saved.";
					break;
				case "saveinterval":
					if (double.TryParse((storage.Value<string>("interval") ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes) && minutes > 0) {
						Announcements.SetIntervalSeconds((int)System.Math.Round(minutes * 60));
						ok = $"Interval set to {Announcements.config.IntervalSeconds / 60.0:0.##} minutes.";
					} else {
						error = "The interval must be a positive number of minutes.";
					}
					break;
				case "addmsg": {
					string text = (storage.Value<string>("newmsg") ?? "").Trim();
					if (text.Length == 0) {
						error = "Please enter a text.";
					} else {
						msgs.Add(new AnnouncementMessage { Text = text, Enabled = true });
						ok = "Announcement added.";
					}
					break;
				}
				case "toggle":
					msgs[idx].Enabled = !msgs[idx].Enabled;
					ok = $"Announcement {idx + 1} {(msgs[idx].Enabled ? "enabled" : "disabled")}.";
					break;
				case "up":
					if (idx > 0) {
						AnnouncementMessage m = msgs[idx];
						msgs.RemoveAt(idx);
						msgs.Insert(idx - 1, m);
					}
					break;
				case "down":
					if (idx < msgs.Count - 1) {
						AnnouncementMessage m = msgs[idx];
						msgs.RemoveAt(idx);
						msgs.Insert(idx + 1, m);
					}
					break;
				case "sendnow":
					Chat.SendToConnected(msgs[idx].Text ?? "");
					ok = "Sent to everyone online.";
					Log.Write($"[BCC UI] {player.Name} sent announcement {idx + 1} now");
					break;
				case "delmsg":
					msgs.RemoveAt(idx);
					ok = "Announcement deleted.";
					break;
				case "editmsg":
					UiManager.Open(player, new UiManager.PageState { Kind = "announceedit", Key = idx.ToString() });
					return;
				case "savemsg": {
					if (!int.TryParse(state.Key, out int e) || e < 0 || e >= msgs.Count) {
						break;
					}
					msgs[e].Text = (storage.Value<string>("amsg") ?? "").Trim();
					if (storage.TryGetValue("aenabled", out JToken en) && en.Type == JTokenType.Boolean) {
						msgs[e].Enabled = en.Value<bool>();
					}
					Announcements.Save();
					Log.Write($"[BCC UI] {player.Name} changed announcement {e + 1}");
					UiManager.Back(player);
					UiManager.PageState list = UiManager.Current(player);
					if (list != null) {
						list.Messages.Add(Style.Success($"Announcement {e + 1} saved."));
						UiManager.Render(player);
					}
					return;
				}
			}
			if (Announcements.config.Messages.Count > 0) {
				Announcements.CurrentIndex %= Announcements.config.Messages.Count;
			} else {
				Announcements.CurrentIndex = 0;
			}
			if (error == null) {
				Announcements.Save();
				Log.Write($"[BCC UI] {player.Name} changed the announcements ({action})");
			}
			if (error != null) state.Messages.Add(Style.Error(error));
			else if (ok != null) state.Messages.Add(Style.Success(ok));
			UiManager.Render(player);
		}
	}
}
