using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NetworkUI;
using NetworkUI.Items;
using Pipliz;

namespace ColonyCommands
{
	// Admin configuration window. Every config object is edited generically through reflection:
	// bool -> toggle; numbers, strings -> text field; List<string>/List<int> -> comma separated;
	// Dictionary<string,double> -> "key=value, key=value". Complex entries (protection areas, jail
	// records, ...) are shown read-only with the command that manages them.
	// Permission mods.scarabol.commands.config is checked when building AND when saving.
	public static class UiConfigPages
	{
		// a field takes about two lines (input + explanation)
		static int FieldsPerPage => System.Math.Max(10, UiManager.LinesPerPage / 2);

		public class ConfigSection
		{
			public string Key;
			public string Title;
			public string File;
			public Func<object> Target;
			public Action Save;
			public Dictionary<string, string> ReadOnlyHints = new Dictionary<string, string>();
			public Func<List<FieldRef>> CustomFields;
			// returns a reason if the section can't be edited right now
			public Func<string> Unavailable;
			// opens another window instead of the field editor (e.g. role management)
			public string OpensPage;
			// row shown only if this returns true (null = always)
			public Func<Players.Player, bool> Visible;
			// descriptions for fields without a [Description] attribute
			public Dictionary<string, string> Help = new Dictionary<string, string>();
			// status line shown above the fields
			public Func<string> Info;
		}

		// explanations for antigrief-config.json (ModConfig has no attributes)
		static readonly Dictionary<string, string> AntiGriefHelp = new Dictionary<string, string> {
			{ "SpawnProtectionRange", "Blocks around the spawn point (x/z) nobody without permission may change. 0 = no spawn protection." },
			{ "BannerProtectionRange", "Blocks around every banner only its colony's owners may change. 0 = no banner protection." },
			{ "ShowJoinPopup", "Ticked = new players also get the protection notice as a small popup (not only as chat line)." },
			{ "ShowJoinChatMessage", "Ticked = every joining player gets the chat line 'Anti-Grief protection enabled'. Unticked = no chat line for anyone (the popup above is separate)." },
			{ "ProtectionIncludesHeightCheck", "Ticked = the protection is a cube (also limited in height), unticked = a column from bedrock to sky." },
			{ "ColonistLimit", "Max. colonists per player. 0 = no limit (unlimited)." },
			{ "ColonistLimitCheckSeconds", "How often (seconds) the colonist limit is checked." },
			{ "ColonistLimitMaxKillPerIteration", "Max. colonists removed per check when a player is above the limit." },
			{ "ColonistTierLimits", "Higher colonist limits for permission colonistcapacity.tier1, tier2, ... (comma separated). 0 in a slot = not used." },
			{ "ColonyColonistLimitTierCheckSeconds", "How often (seconds) the per-colony difficulty limits are checked (currently unused)." },
			{ "ColonyColonistLimitTierWarnTimes", "Warnings before colonists are removed (per-colony difficulty limits, currently unused)." },
			{ "OnlineBackupIntervalHours", "Creates a zip backup of the world every N hours while the server runs. 0 = off." },
			{ "NpcKillsJailThreshold", "Killed foreign colonists after which a player is jailed. 0 = off." },
			{ "NpcKillsKickThreshold", "Killed foreign colonists after which a player is kicked. 0 = off." },
			{ "NpcKillsBanThreshold", "Killed foreign colonists after which a player is banned. 0 = off." },
			{ "EnableWarpCommand", "Ticked = /warp is available (still needs the warp permissions)." },
			{ "WarDuration", "Default duration of /war start in seconds (7200 = 2 hours)." },
			{ "StartupGracePeriod", "Seconds after server start during which joining players are asked to wait. 0 = off." }
		};

		static readonly Dictionary<string, string> JailHelp = new Dictionary<string, string> {
			{ "rangex", "Half size of the jail area in x (set with /setjail)." },
			{ "rangey", "Half size of the jail area in y (set with /setjail)." },
			{ "rangez", "Half size of the jail area in z (set with /setjail)." },
			{ "boxRange", "Size of the jail box built around the jail position." },
			{ "defaultJailTime", "Jail time in minutes when /jail is used without a time." },
			{ "graceEscapeAttempts", "Escape attempts that are tolerated before the jail time is extended. 0 = none." },
			{ "prisonerGroup", "Role prisoners get while in jail (empty = none)." },
			{ "restoreGroupsOnRelease", "Ticked = prisoners get their previous roles back on release." },
			{ "defaultGroup", "Role given on release if no previous roles are restored." },
			{ "rolesGrantedOnJail", "Additional roles granted when jailed (comma separated)." },
			{ "rolesGrantedOnRelease", "Additional roles granted on release (comma separated)." }
		};

		public class FieldRef
		{
			public string Name;
			public string Description;
			public Type Type;
			public Func<object> Get;
			public Action<object> Set;
			public string ReadOnlyHint;
		}

		public static List<ConfigSection> Sections()
		{
			List<ConfigSection> list = new List<ConfigSection> {
				new ConfigSection { Key = "roles", Title = "Roles, permissions & role formatting", File = "permissiongroups.json, chatcolors.json", OpensPage = "roles" },
				new ConfigSection { Key = "announcements", Title = "Announcements", File = "announcements.json", OpensPage = "announce", Visible = UiAnnouncementPages.CanEdit },
				new ConfigSection { Key = "players", Title = "Player moderation", File = "all players, roles, money, colonies, history", OpensPage = "players", Visible = UiPlayerPages.CanOpen },
				new ConfigSection { Key = "antigrief", Title = "Anti-Grief & server", File = "antigrief-config.json", Target = () => AntiGrief.config, Save = AntiGrief.Save, Help = AntiGriefHelp,
					ReadOnlyHints = new Dictionary<string, string> {
						{ "CustomAreas", "managed with /antigrief area and /customarea" },
						{ "ColonistPerColonyTierLimits", "edit antigrief-config.json" },
						{ "ColonyColonistLimitTiers", "runtime data" }
					} },
				new ConfigSection { Key = "ui", Title = "Windows (UI)", File = "bcc-settings.json", Target = () => BccSettingsManager.Settings.Ui, Save = BccSettingsManager.AfterChange },
				new ConfigSection { Key = "rtp", Title = "Random teleport (/rtp)", File = "bcc-settings.json", Target = () => BccSettingsManager.Settings.Rtp, Save = BccSettingsManager.AfterChange },
				new ConfigSection { Key = "economy", Title = "Economy", File = "bcc-settings.json", Target = () => BccSettingsManager.Settings.Economy, Save = BccSettingsManager.AfterChange },
				new ConfigSection { Key = "modlist", Title = "Mod list (/modlist)", File = "bcc-settings.json", Target = () => BccSettingsManager.Settings.ModList, Save = BccSettingsManager.AfterChange },
				new ConfigSection { Key = "permbackup", Title = "Permission groups backup (game updates)", File = "bcc-settings.json", Target = () => BccSettingsManager.Settings.Permissions, Save = BccSettingsManager.AfterChange, Info = PermissionGroupsGuard.Status },
				new ConfigSection { Key = "jail", Title = "Jail", File = "jail-config.json", Target = () => JailManager.jailConfig, Save = JailManager.Save, Help = JailHelp,
					Unavailable = () => JailManager.validJail ? null : "No jail is set yet - set one with /setjail first (the jail config is only saved once a jail exists).",
					ReadOnlyHints = new Dictionary<string, string> {
						{ "jailedRecords", "managed with /jail and /jailrelease" },
						{ "jailx", "set with /setjail" }, { "jaily", "set with /setjail" }, { "jailz", "set with /setjail" },
						{ "jailvx", "set with /setjail visitor" }, { "jailvy", "set with /setjail visitor" }, { "jailvz", "set with /setjail visitor" }
					} },
				new ConfigSection { Key = "chatformat", Title = "Chat line format", File = "chatcolors.json", Target = () => ChatColors.Colors, Save = ChatColors.SaveChatColors, CustomFields = ChatFormatFields }
			};
			{
				list.Insert(6, new ConfigSection { Key = "homes", Title = "Home points (/home)", File = "bcc-settings.json", Target = () => BccSettingsManager.Settings.Homes, Save = BccSettingsManager.AfterChange });
			}
			// time based roles belong to Pytonia Automation - only shown when it is installed
			if (PytoniaBridge.IsInstalled) {
				list.Add(new ConfigSection { Key = "pytoniaroles", Title = "Time based roles (Pytonia Automation)", File = "pytonia-automation.json", Target = () => PytoniaBridge.Config(), Save = PytoniaBridge.Save,
					CustomFields = PytoniaRoleFields,
					Unavailable = () => PytoniaBridge.IsReady ? null : "Pytonia Automation's data is not loaded (yet)." });
			}
			return list;
		}

		static ConfigSection FindSection(string key) => Sections().FirstOrDefault(s => s.Key == key);

		public static void OpenSection(Players.Player player, string key)
		{
			ConfigSection section = FindSection(key);
			if (section == null || !BccPermissions.CanEditConfig(player) || (section.Visible != null && !section.Visible(player))) {
				return;
			}
			if (section.OpensPage != null) {
				UiManager.OpenNamed(player, section.OpensPage);
			} else {
				UiManager.Open(player, new UiManager.PageState { Kind = "configsection", Key = key });
			}
		}

		public static void BuildSections(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "Admin configuration");
			if (!BccPermissions.CanEditConfig(player)) {
				menu.Items.Add(UiManager.Text(Style.Error("You don't have permission to change the configuration.")));
				return;
			}
			UiManager.PermissionNotice(menu, player);
			menu.Items.Add(UiManager.Text("Choose a section. Changes are saved to the file right away and apply immediately.", -1, 15));
			menu.Items.Add(UiManager.Text(Style.Muted("Travel paths and the name blacklist are managed with /travel and blacklistedwords.json + /bcreload."), -1, 13));
			List<ConfigSection> sections = Sections().Where(s => s.Visible == null || s.Visible(player)).ToList();
			state.RowKeys = sections.Select(s => s.Key).ToList();
			int w = UiManager.ContentWidth;
			for (int i = 0; i < sections.Count; i++) {
				ConfigSection s = sections[i];
				menu.Items.Add(new BackgroundColor(UiManager.Row(34,
					(UiManager.Text($"<b>{s.Title}</b> {Style.Muted(s.File)}", 30, 16), w - 130),
					(UiManager.Button("row", i.ToString(), s.OpensPage != null ? "Open" : "Edit", 110), 116)), w, 38, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
		}

		static List<FieldRef> FieldsOf(ConfigSection section)
		{
			if (section.CustomFields != null) {
				return section.CustomFields();
			}
			List<FieldRef> result = new List<FieldRef>();
			object target = section.Target();
			if (target == null) {
				return result;
			}
			foreach (FieldInfo f in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)) {
				if (f.IsInitOnly || f.IsLiteral) {
					continue;
				}
				string desc = f.GetCustomAttribute<DescriptionAttribute>()?.Description ?? (section.Help.TryGetValue(f.Name, out string helpText) ? helpText : "");
				string hint = section.ReadOnlyHints.TryGetValue(f.Name, out string h) ? h : (IsSupported(f.FieldType) ? null : "complex value - edit the file");
				FieldInfo field = f;
				result.Add(new FieldRef { Name = f.Name, Description = desc, Type = f.FieldType, Get = () => field.GetValue(target), Set = v => field.SetValue(target, v), ReadOnlyHint = hint });
			}
			return result;
		}

		static bool IsSupported(Type t)
		{
			return t == typeof(bool) || t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double) || t == typeof(string)
				|| t == typeof(List<string>) || t == typeof(List<int>) || t == typeof(Dictionary<string, double>) || t == typeof(Dictionary<string, int>);
		}

		static string ToText(object value)
		{
			switch (value) {
				case null: return "";
				case float f: return f.ToString(CultureInfo.InvariantCulture);
				case double d: return d.ToString(CultureInfo.InvariantCulture);
				case List<string> ls: return string.Join(", ", ls);
				case List<int> li: return string.Join(", ", li);
				case Dictionary<string, double> dict: return string.Join(", ", dict.Select(kvp => $"{kvp.Key}={kvp.Value.ToString(CultureInfo.InvariantCulture)}"));
				case Dictionary<string, int> idict: return string.Join(", ", idict.Select(kvp => $"{kvp.Key}={kvp.Value}"));
				default: return Convert.ToString(value, CultureInfo.InvariantCulture);
			}
		}

		static bool TryParse(Type t, string text, out object value, out string error)
		{
			value = null;
			error = null;
			text = (text ?? "").Trim();
			try {
				if (t == typeof(string)) { value = text; return true; }
				if (t == typeof(int)) { value = int.Parse(text, CultureInfo.InvariantCulture); return true; }
				if (t == typeof(long)) { value = long.Parse(text, CultureInfo.InvariantCulture); return true; }
				if (t == typeof(float)) { value = float.Parse(text.Replace(',', '.'), CultureInfo.InvariantCulture); return true; }
				if (t == typeof(double)) { value = double.Parse(text.Replace(',', '.'), CultureInfo.InvariantCulture); return true; }
				if (t == typeof(List<string>)) {
					value = text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
					return true;
				}
				if (t == typeof(List<int>)) {
					value = text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList();
					return true;
				}
				if (t == typeof(Dictionary<string, int>)) {
					Dictionary<string, int> d = new Dictionary<string, int>();
					foreach (string part in text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)) {
						string[] kv = part.Split('=');
						if (kv.Length != 2) {
							error = $"'{part}' is not key=value";
							return false;
						}
						d[kv[0].Trim()] = int.Parse(kv[1].Trim(), CultureInfo.InvariantCulture);
					}
					value = d;
					return true;
				}
				if (t == typeof(Dictionary<string, double>)) {
					Dictionary<string, double> d = new Dictionary<string, double>();
					foreach (string part in text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)) {
						string[] kv = part.Split('=');
						if (kv.Length != 2) {
							error = $"'{part}' is not key=value";
							return false;
						}
						d[kv[0].Trim()] = double.Parse(kv[1].Trim().Replace(',', '.'), CultureInfo.InvariantCulture);
					}
					value = d;
					return true;
				}
			} catch (Exception) {
				error = $"'{text}' is not a valid {t.Name}";
				return false;
			}
			error = "unsupported type";
			return false;
		}

		public static void BuildSection(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			ConfigSection section = FindSection(state.Key);
			UiManager.Header(player, menu, section != null ? section.Title : "Configuration");
			if (!BccPermissions.CanEditConfig(player)) {
				menu.Items.Add(UiManager.Text(Style.Error("You don't have permission to change the configuration.")));
				return;
			}
			if (section != null && section.OpensPage != null) {
				menu.Items.Add(UiManager.Text(Style.Muted("This section has its own window."), 24, 14));
				return;
			}
			if (section == null || section.Target == null || section.Target() == null) {
				menu.Items.Add(UiManager.Text(Style.Error("This configuration is not loaded.")));
				return;
			}
			UiManager.Messages(menu, state);
			string unavailable = section.Unavailable?.Invoke();
			if (unavailable != null) {
				menu.Items.Add(UiManager.Text(Style.Error(unavailable), -1, 15));
				return;
			}
			menu.Items.Add(UiManager.Text(Style.Muted("File: " + section.File + " - lists are comma separated."), 22, 13));
			string info = section.Info?.Invoke();
			if (!string.IsNullOrEmpty(info)) {
				menu.Items.Add(UiManager.Text(info, -1, 14));
			}
			List<FieldRef> fields = FieldsOf(section);
			List<FieldRef> page = TextHelper.Page(fields, state.Page, FieldsPerPage, out int p, out int pages);
			state.Page = p;
			state.RowKeys = page.Select(f => f.Name).ToList();
			int w = UiManager.ContentWidth;
			for (int i = 0; i < page.Count; i++) {
				FieldRef f = page[i];
				object value = f.Get();
				string key = "f" + i;
				string label = $"<b>{f.Name}</b>";
				if (f.ReadOnlyHint != null) {
					menu.Items.Add(UiManager.Text($"{label}: {TextHelper.Truncate(TextHelper.EscapeTags(ToText(value)), 60)} {Style.Muted("(" + f.ReadOnlyHint + ")")}", -1, 14));
				} else if (f.Type == typeof(bool)) {
					menu.LocalStorage.SetAs(key, (bool)value);
					menu.Items.Add(new Toggle(new LabelData(label, ELabelAlignment.MiddleLeft, 15, LabelData.ELocalizationType.None), key));
				} else {
					menu.LocalStorage.SetAs(key, ToText(value));
					menu.Items.Add(UiManager.Row(32,
						(UiManager.Text(label, 30, 15), 260),
						(new InputField(key, w - 268, 30), w - 260)));
				}
				if (!string.IsNullOrEmpty(f.Description)) {
					menu.Items.Add(UiManager.Text(Style.Muted(f.Description), -1, 13));
				}
			}
			UiManager.Pager(menu, p, pages);
			menu.Items.Add(new EmptySpace(6));
			menu.Items.Add(UiManager.Row(36,
				(UiManager.Text(Style.Muted("Saves the fields of this page."), 30, 13), w - 200),
				(UiManager.Button("cfgsave", null, "Save", 190, true, 34), 196)));
		}

		// chatcolors.json Format only - role colors are edited on each role's page (roles window)
		static List<FieldRef> ChatFormatFields()
		{
			return new List<FieldRef> {
				new FieldRef { Name = "Format", Description = "Chat line template (default {name}{rolename:top}{rp}: {message}). Placeholders: {name}, {message}, {rp}, {rolename:top}, {rolename:bottom}, {rolename:<role>}, {role:<role>;string:\"text\"}", Type = typeof(string),
					Get = () => ChatColors.Format, Set = v => ChatColors.Format = (string)v }
			};
		}

		// Pytonia Automation: TimeBasedRoles switches plus every role's time threshold
		static List<FieldRef> PytoniaRoleFields()
		{
			List<FieldRef> fields = new List<FieldRef>();
			object tbr = PytoniaBridge.Section("TimeBasedRoles");
			foreach (string f in new[] { "Enabled", "CheckIntervalSeconds" }) {
				Type t = PytoniaBridge.FieldType(tbr, f);
				if (t == null) continue;
				string field = f;
				fields.Add(new FieldRef { Name = field, Type = t,
					Description = field == "Enabled" ? "Ticked = roles are granted automatically by online time." : "How often (seconds) online times are checked.",
					Get = () => PytoniaBridge.GetField(PytoniaBridge.Section("TimeBasedRoles"), field),
					Set = v => PytoniaBridge.SetField(PytoniaBridge.Section("TimeBasedRoles"), field, v) });
			}
			if (PytoniaBridge.Roles() is System.Collections.IDictionary roles) {
				foreach (object key in roles.Keys.Cast<object>().ToList()) {
					string role = key.ToString();
					fields.Add(new FieldRef { Name = role + ".RequiredHoursOnline", Type = typeof(double),
						Description = $"{ChatColors.FormatRole(role)}: online hours until this role is granted (0 = not time based)",
						Get = () => PytoniaBridge.GetField(PytoniaBridge.Roles()[role], "RequiredHoursOnline"),
						Set = v => PytoniaBridge.SetField(PytoniaBridge.Roles()[role], "RequiredHoursOnline", v) });
					fields.Add(new FieldRef { Name = role + ".ExcludeFromTimeBasedAssignment", Type = typeof(bool),
						Description = $"{ChatColors.FormatRole(role)}: ticked = never granted by online time",
						Get = () => PytoniaBridge.GetField(PytoniaBridge.Roles()[role], "ExcludeFromTimeBasedAssignment"),
						Set = v => PytoniaBridge.SetField(PytoniaBridge.Roles()[role], "ExcludeFromTimeBasedAssignment", v) });
				}
			}
			return fields;
		}

		public static void Save(Players.Player player, UiManager.PageState state, JObject storage)
		{
			if (!BccPermissions.CanEditConfig(player)) {
				state.Messages.Add(Style.Error("You don't have permission to change the configuration."));
				UiManager.Render(player);
				return;
			}
			ConfigSection section = FindSection(state.Key);
			if (section == null || section.OpensPage != null || section.Target == null || section.Target() == null || section.Unavailable?.Invoke() != null) {
				return;
			}
			Dictionary<string, FieldRef> fields = FieldsOf(section).ToDictionary(f => f.Name);
			int changed = 0;
			for (int i = 0; i < state.RowKeys.Count; i++) {
				if (!fields.TryGetValue(state.RowKeys[i], out FieldRef f) || f.ReadOnlyHint != null) {
					continue;
				}
				if (!storage.TryGetValue("f" + i, out JToken token)) {
					continue;
				}
				object newValue;
				if (f.Type == typeof(bool)) {
					if (token.Type != JTokenType.Boolean) {
						continue;
					}
					newValue = token.Value<bool>();
				} else if (!TryParse(f.Type, token.Type == JTokenType.String ? token.Value<string>() : token.ToString(), out newValue, out string error)) {
					state.Messages.Add(Style.Error($"{f.Name}: {error} - not changed"));
					continue;
				}
				object old = f.Get();
				if (ToText(old) != ToText(newValue)) {
					f.Set(newValue);
					changed++;
					Log.Write($"Better Colony Commands: {player.Name} changed {section.File} {f.Name}: '{ToText(old)}' -> '{ToText(newValue)}'");
				}
			}
			if (changed > 0) {
				section.Save();
				state.Messages.Add(Style.Success($"{changed} setting(s) saved to {section.File}."));
			} else if (state.Messages.Count == 0) {
				state.Messages.Add(Style.Muted("Nothing changed."));
			}
			UiManager.Render(player);
		}
	}
}
