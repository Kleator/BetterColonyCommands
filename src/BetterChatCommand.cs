using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Pipliz;
using Chatting;
using Chatting.Commands;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/*
 * Inspired by Crone's BetterChat
 */
namespace ColonyCommands
{
	public class BetterChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chatmsg, List<string> splits)
		{
			// never match actual commands
			if (chatmsg.StartsWith("/")) {
				return false;
			}

			MuteList.Update();
			if (MuteList.MutedMinutes.ContainsKey(causedBy.ID.ID)) {
				BccChat.Send(causedBy, "<color=yellow>[muted]</color>");
				return true;
			}

			string Name = TextHelper.SafeName(causedBy.Name);
			string Text = chatmsg;

			List<string> matchedGroups = ChatColors.GetMatchedGroupKeys(causedBy);
			string topKey = matchedGroups.Count > 0 ? matchedGroups[0] : null;
			string bottomKey = matchedGroups.Count > 0 ? matchedGroups[matchedGroups.Count - 1] : null;
			ChatColors.ApplyNameAndTextColor(topKey, ref Name, ref Text);

			string rp = RoleplayManager.IsRoleplaying(causedBy) ? "<color=yellow>[RP]</color>" : "";

			// same shape as the old hardcoded Prefix (role tag + RP marker) for foreign mod hook compatibility
			string legacyPrefix = ChatColors.GetRoleLabel(topKey) + rp;

			// allow foreign mods to alter chat further
			foreach (MethodInfo func in AntiGrief.ChatColorForeignModMethods) {
				bool val = (bool)func.Invoke(null, new object[]{causedBy, Name, legacyPrefix, Text});
				if (val == true) { // stop chain here, other mod handles message
					return false;
				}
			}

			string rendered = ChatColors.RenderFormat(matchedGroups, topKey, bottomKey, Name, rp, Text);
			Chat.SendToConnected(rendered);
			return true;
		}
	}


	public static class ChatColors
	{
		public static Dictionary<string, ChatColorSpecification> Colors = new Dictionary<string, ChatColorSpecification>();
		public static string Format = DefaultFormat;

		// preserves the exact look of the old hardcoded "{Name}{Prefix}> {Text}" assembly by default
		const string DefaultFormat = "{name}{rolename:top}{rp}: {message}";

		const string CONFIG_FILE = "chatcolors.json";
		private static string ConfigFilePath {
			get {
				return Path.Combine(Path.Combine("gamedata", "savegames"), Path.Combine(ServerManager.WorldName, CONFIG_FILE));
			}
		}

		// {name}, {message}, {rp} take no argument.
		// {rolename:top}, {rolename:bottom}, {rolename:<key>} resolve to that group's bracketed, colored tag (or "" if unmatched).
		// {role:<key>;string:"literal text"} resolves to the literal text only if the player currently matches <key>, else "".
		static readonly Regex PlaceholderPattern = new Regex(@"\{(?<kw>\w+)(?::\s*(?<arg>[^;{}]*?)\s*)?(?:;\s*string\s*:\s*""(?<str>[^""]*)"")?\}");


		// groups that causedBy currently has the "betterchat.<key>" permission for, highest first:
		//  - order of the role tree from the top (a role, then the roles it includes, strand by strand -
		//    as in the role window), keys that aren't roles after them in chatcolors.json order
		//  - a role that another of the player's matched roles includes (a base role like the default role
		//    every time role inherits) goes behind all others: it is the top only if nothing is above it
		public static List<string> GetMatchedGroupKeys(Players.Player causedBy)
		{
			List<string> matched = new List<string>();
			foreach (string key in Colors.Keys) {
				if (PermissionsManager.HasPermission(causedBy, AntiGrief.MOD_PREFIX + "betterchat." + key)) {
					matched.Add(key);
				}
			}
			if (matched.Count < 2) {
				return matched;
			}
			EnsureRank();
			HashSet<string> included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string key in matched) {
				if (rankBelow.TryGetValue(key, out HashSet<string> below)) {
					included.UnionWith(below);
				}
			}
			List<string> declared = new List<string>(Colors.Keys);
			int Position(string key)
			{
				int i = rankOrder.FindIndex(r => string.Equals(r, key, StringComparison.OrdinalIgnoreCase));
				return i >= 0 ? i : rankOrder.Count + declared.IndexOf(key);
			}
			return matched.OrderBy(k => included.Contains(k) ? 1 : 0).ThenBy(Position).ToList();
		}

		// role tree order and "roles below each role", rebuilt at most every few seconds (chat and player lists ask often)
		static List<string> rankOrder = new List<string>();
		static Dictionary<string, HashSet<string>> rankBelow = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		static DateTime rankBuilt = DateTime.MinValue;

		static void EnsureRank()
		{
			if ((DateTime.UtcNow - rankBuilt).TotalSeconds < 5) {
				return;
			}
			rankBuilt = DateTime.UtcNow;
			try {
				List<string> order = RoleManager.Tree().Select(t => t.role).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				Dictionary<string, HashSet<string>> below = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
				foreach (string role in order) {
					below[role] = new HashSet<string>(RoleManager.Descendants(role), StringComparer.OrdinalIgnoreCase);
				}
				rankOrder = order;
				rankBelow = below;
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: role order for chat tags not available: {e.Message}");
			}
		}

		// role tags change right away after a role edit
		public static void InvalidateRank()
		{
			rankBuilt = DateTime.MinValue;
		}


		// colors Name/Text using the highest matched group (see GetMatchedGroupKeys)
		public static void ApplyNameAndTextColor(string topKey, ref string name, ref string text)
		{
			if (topKey == null || !Colors.TryGetValue(topKey, out ChatColorSpecification spec)) {
				return;
			}
			if (!string.IsNullOrEmpty(spec.Name)) {
				name = $"<color={spec.Name}>{name}</color>";
			}
			if (!string.IsNullOrEmpty(spec.Text)) {
				text = $"<color={spec.Text}>{text}</color>";
			}
		}


		// colored display tag for one configured group, e.g. "<color=red>[God]</color>"; "" if the
		// group has no prefix configured. No brackets are added here - spec.Prefix is used exactly
		// as configured, so the admin has full control over its look (including using none, or
		// their own custom markup like "<b>[Owner]</b>") without ending up with doubled brackets.
		public static string GetRoleLabel(string key)
		{
			if (key == null || !Colors.TryGetValue(key, out ChatColorSpecification spec) || string.IsNullOrEmpty(spec.Prefix)) {
				return "";
			}
			if (!string.IsNullOrEmpty(spec.PrefixColor)) {
				return $"<color={spec.PrefixColor}>{spec.Prefix}</color>";
			}
			return spec.Prefix;
		}


		// A role (permission group) name shown with the formatting configured for it in chatcolors.json:
		// its Prefix (with PrefixColor) if it has one - exactly how the role appears in chat - otherwise
		// the role name in the group's Name color, otherwise the plain name. Used wherever a role is
		// mentioned (/roles, /groups, /roletime, /permissions, ...).
		public static string FormatRole(string role)
		{
			if (string.IsNullOrEmpty(role)) {
				return role ?? "";
			}
			string key = FindKey(role);
			if (key == null) {
				return role;
			}
			ChatColorSpecification spec = Colors[key];
			if (!string.IsNullOrEmpty(spec.Prefix)) {
				string label = TextHelper.SafeName(GetRoleLabel(key));
				string visible = TextHelper.NormalizeName(label);
				// show the technical name too if the prefix text doesn't contain it
				if (!visible.Contains(TextHelper.NormalizeName(role))) {
					return $"{label} <color={Style.MUTED_COLOR}>({role})</color>";
				}
				return label;
			}
			if (!string.IsNullOrEmpty(spec.Name)) {
				return $"<color={spec.Name}>{role}</color>";
			}
			if (!string.IsNullOrEmpty(spec.PrefixColor)) {
				return $"<color={spec.PrefixColor}>{role}</color>";
			}
			return role;
		}

		static string FindKey(string role)
		{
			if (Colors.ContainsKey(role)) {
				return role;
			}
			foreach (string k in Colors.Keys) {
				if (string.Equals(k, role, StringComparison.OrdinalIgnoreCase)) {
					return k;
				}
			}
			return null;
		}

		// writes the current Format and groups back (used by the admin configuration window)
		public static void SaveChatColors()
		{
			try {
				JObject groups = new JObject();
				foreach (KeyValuePair<string, ChatColorSpecification> kvp in Colors) {
					JObject g = new JObject();
					if (!string.IsNullOrEmpty(kvp.Value.Prefix)) g["Prefix"] = kvp.Value.Prefix;
					if (!string.IsNullOrEmpty(kvp.Value.PrefixColor)) g["PrefixColor"] = kvp.Value.PrefixColor;
					if (!string.IsNullOrEmpty(kvp.Value.Name)) g["Name"] = kvp.Value.Name;
					if (!string.IsNullOrEmpty(kvp.Value.Text)) g["Text"] = kvp.Value.Text;
					if (!string.IsNullOrEmpty(kvp.Value.RpMarker)) g["RpMarker"] = kvp.Value.RpMarker;
					groups[kvp.Key] = g;
				}
				JObject root = new JObject { ["Format"] = Format, ["Groups"] = groups };
				if (File.Exists(ConfigFilePath)) {
					File.Copy(ConfigFilePath, ConfigFilePath + ".bak", true);
				}
				File.WriteAllText(ConfigFilePath, root.ToString(Formatting.Indented));
			} catch (Exception e) {
				Log.WriteError($"Could not save {CONFIG_FILE}: {e.Message}");
			}
		}

		// colored name + top-role tag, for reuse anywhere else in the mod that prints a player's name
		public static string GetDisplayName(Players.Player player)
		{
			List<string> matched = GetMatchedGroupKeys(player);
			string topKey = matched.Count > 0 ? matched[0] : null;
			string name = TextHelper.SafeName(player.Name);
			string unusedText = "";
			ApplyNameAndTextColor(topKey, ref name, ref unusedText);
			string label = GetRoleLabel(topKey);
			return label.Length > 0 ? name + " " + label : name;
		}


		public static string RenderFormat(List<string> matchedGroups, string topKey, string bottomKey, string name, string rp, string message)
		{
			return PlaceholderPattern.Replace(Format, delegate(Match m) {
				string kw = m.Groups["kw"].Value.ToLowerInvariant();
				string arg = m.Groups["arg"].Success ? m.Groups["arg"].Value.Trim() : null;
				switch (kw) {
					case "name":
						return name;
					case "message":
						return message;
					case "rp":
						return rp;
					case "rolename":
						if (arg == "top") return GetRoleLabel(topKey);
						if (arg == "bottom") return GetRoleLabel(bottomKey);
						return (arg != null && matchedGroups.Contains(arg)) ? GetRoleLabel(arg) : "";
					case "role": {
						string literalText = m.Groups["str"].Success ? m.Groups["str"].Value : "";
						return (arg != null && matchedGroups.Contains(arg)) ? literalText : "";
					}
					default:
						return m.Value;
				}
			});
		}


		// Load from config file (JSON). A top-level "Format" key (any casing) is used as the
		// template string; an optional "Groups" wrapper (any casing) holds the per-group map, but
		// isn't required - if absent, every other top-level key (i.e. everything except "Format")
		// is treated as a group. This also transparently covers the legacy flat shape (no Format,
		// no Groups - root IS the groups map) and someone hand-adding "Format" to an existing flat
		// file without wrapping the rest into "Groups".
		public static void LoadChatColors()
		{
			if (!File.Exists(ConfigFilePath)) {
				Log.Write($"Chatcolors file not found {CONFIG_FILE}");
				return;
			}

			try {
				JObject root = JObject.Parse(File.ReadAllText(ConfigFilePath));

				JProperty formatProperty = null;
				JProperty groupsProperty = null;
				foreach (JProperty prop in root.Properties()) {
					if (formatProperty == null && string.Equals(prop.Name, "Format", StringComparison.OrdinalIgnoreCase)) {
						formatProperty = prop;
					} else if (groupsProperty == null && string.Equals(prop.Name, "Groups", StringComparison.OrdinalIgnoreCase)) {
						groupsProperty = prop;
					}
				}

				Format = (formatProperty != null && formatProperty.Value.Type == JTokenType.String)
					? formatProperty.Value.Value<string>()
					: DefaultFormat;

				JObject groupsObject;
				if (groupsProperty != null && groupsProperty.Value is JObject explicitGroups) {
					groupsObject = explicitGroups;
				} else {
					groupsObject = new JObject();
					foreach (JProperty prop in root.Properties()) {
						if (prop == formatProperty || prop == groupsProperty) {
							continue;
						}
						groupsObject[prop.Name] = prop.Value;
					}
				}
				Colors = groupsObject.ToObject<Dictionary<string, ChatColorSpecification>>() ?? new Dictionary<string, ChatColorSpecification>();
			} catch (Exception exception) {
				Log.WriteError($"Exception while loading chatcolors: {exception.Message}");
			}
		}
	}


	public struct ChatColorSpecification
	{
		public string Prefix;
		public string PrefixColor;
		public string Name;
		public string Text;
		public string RpMarker;

		public ChatColorSpecification(string p, string pc, string n, string t, string r)
		{
			Prefix = p;
			PrefixColor = pc;
			Name = n;
			Text = t;
			RpMarker = r;
		}
	}
}
