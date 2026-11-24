using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Pipliz;

namespace ColonyCommands
{
	public class BccCommandParam
	{
		public string Name;
		public string Description;
		public bool Optional;

		public BccCommandParam() { }
		public BccCommandParam(string name, string description, bool optional = false)
		{
			Name = name;
			Description = description;
			Optional = optional;
		}
	}

	public class BccCommandInfo
	{
		public string Command;                       // "/warp"
		public List<string> Aliases = new List<string>();
		public string Mod = CommandRegistry.BCC_MOD;
		public string Category = "";
		public string Description = "";
		public string Usage = "";                    // "/warp {player} [other player]"
		public string Permission = "";               // permission needed to see/use the command
		public bool OpenByDefault;                   // auto-created permission: everyone until assigned somewhere
		public List<BccCommandParam> Parameters = new List<BccCommandParam>();
		public List<string> ExtraPermissions = new List<string>(); // documented sub permissions (e.g. ".self", admin variants)
		public string Source = "builtin";            // builtin | api | discovered | legacy | scanned
		public bool UiExecutable = true;             // can be filled in and run from the help window

		public bool Matches(string text)
		{
			if (string.Equals(text, Command, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
			foreach (string a in Aliases) {
				if (string.Equals(text, a, StringComparison.OrdinalIgnoreCase)) {
					return true;
				}
			}
			return false;
		}
	}

	// Central list of every chat command known to Better Colony Commands - its own and the ones of
	// other mods. Sources (later ones never overwrite descriptions given by earlier ones):
	//  1. built-in BCC commands (BuiltinCommands)
	//  2. BccApi.RegisterCommand(...) called by other mods (directly or via reflection)
	//  3. auto discovery: any static class named "*CommandRegistry" in another loaded mod that has a
	//     static "All"/"Commands" collection whose items have CommandText/Command, Description and
	//     Permission members (the shape used by all Pytonia mods)
	//  4. the legacy HelpCommand.commandPermissionList dictionary other mods write into via reflection
	//  5. mods without any of that (e.g. MapMod): the "/command" texts their chat command handler
	//     (IChatCommand.TryDoCommand) compares with, read from its compiled code
	// A command without permission gets an auto-created "open by default" permission.
	public static class CommandRegistry
	{
		public const string BCC_MOD = "Better Colony Commands";
		public const string UNKNOWN_MOD = "Other mods";

		static readonly List<BccCommandInfo> commands = new List<BccCommandInfo>();
		static readonly Dictionary<string, BccCommandInfo> byCommand = new Dictionary<string, BccCommandInfo>(StringComparer.OrdinalIgnoreCase);
		static List<ModLoader.ModDescription> loadedMods = new List<ModLoader.ModDescription>();
		static DateTime lastDiscovery = DateTime.MinValue;
		static bool builtinsRegistered;

		public static void SetLoadedMods(List<ModLoader.ModDescription> mods)
		{
			loadedMods = mods ?? new List<ModLoader.ModDescription>();
		}

		public static void EnsureBuiltins()
		{
			if (builtinsRegistered) {
				return;
			}
			builtinsRegistered = true;
			BuiltinCommands.RegisterAll();
			VanillaCommands.RegisterAll();
		}

		public static string NormalizeCommand(string command)
		{
			if (string.IsNullOrEmpty(command)) {
				return "";
			}
			command = command.Trim();
			int space = command.IndexOf(' ');
			if (space > 0) {
				command = command.Substring(0, space);
			}
			if (!command.StartsWith("/")) {
				command = "/" + command;
			}
			return command.ToLowerInvariant();
		}

		// Registry key: keeps sub commands ("/worldedit fill [block]" -> "/worldedit fill") so mods that
		// register several sub commands of one chat command keep one entry each; stops at the first
		// argument placeholder (<x>, [x], {x}, (x), a|b).
		public static string NormalizeKey(string command)
		{
			if (string.IsNullOrEmpty(command)) {
				return "";
			}
			string s = command.Trim();
			if (!s.StartsWith("/")) {
				s = "/" + s;
			}
			List<string> keep = new List<string>();
			foreach (string part in s.Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries)) {
				if (keep.Count > 0 && ("<[{(".IndexOf(part[0]) >= 0 || part.Contains("|") || keep.Count >= 3)) {
					break;
				}
				keep.Add(part);
			}
			return string.Join(" ", keep).ToLowerInvariant();
		}

		public static string AutoPermissionFor(string mod, string command)
		{
			string name = NormalizeKey(command).TrimStart('/').Replace(' ', '.');
			if (mod == BCC_MOD) {
				return "bcc.cmd." + name;
			}
			return ModSlug(mod) + ".cmd." + name;
		}

		public static string ModSlug(string mod)
		{
			if (string.IsNullOrEmpty(mod)) {
				return "mod";
			}
			string slug = new string(mod.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
			return slug.Length == 0 ? "mod" : slug;
		}

		// Adds or merges a command. Returns the stored entry.
		public static BccCommandInfo Register(BccCommandInfo info)
		{
			if (info == null || string.IsNullOrEmpty(info.Command)) {
				return null;
			}
			info.Command = NormalizeKey(info.Command);
			if (string.IsNullOrEmpty(info.Mod)) {
				info.Mod = UNKNOWN_MOD;
			}
			if (string.IsNullOrEmpty(info.Usage)) {
				info.Usage = info.Command;
			}
			if (byCommand.TryGetValue(info.Command, out BccCommandInfo existing)) {
				// Better Colony Commands' own commands (e.g. the home points moved here from Pytonia
				// Automation) keep their description, mod and permission - another mod registering the
				// same command doesn't take it over
				if (existing.Source == "builtin" && info.Source != "builtin") {
					return existing;
				}
				// merge: fill gaps only, a richer source never loses information
				if (string.IsNullOrEmpty(existing.Description) && !string.IsNullOrEmpty(info.Description)) existing.Description = info.Description;
				if ((existing.Usage == existing.Command) && !string.IsNullOrEmpty(info.Usage) && info.Usage != info.Command) existing.Usage = info.Usage;
				if ((existing.Mod == UNKNOWN_MOD) && info.Mod != UNKNOWN_MOD) existing.Mod = info.Mod;
				if (existing.Parameters.Count == 0 && info.Parameters != null && info.Parameters.Count > 0) existing.Parameters = info.Parameters;
				if (existing.OpenByDefault && !string.IsNullOrEmpty(info.Permission) && !info.OpenByDefault) {
					existing.Permission = info.Permission;
					existing.OpenByDefault = false;
				}
				if (string.IsNullOrEmpty(existing.Category) && !string.IsNullOrEmpty(info.Category)) existing.Category = info.Category;
				RegisterPermissions(existing);
				return existing;
			}
			if (string.IsNullOrEmpty(info.Permission)) {
				info.Permission = AutoPermissionFor(info.Mod, info.Command);
				info.OpenByDefault = true;
			}
			if (info.Aliases == null) info.Aliases = new List<string>();
			if (info.Parameters == null) info.Parameters = new List<BccCommandParam>();
			if (info.ExtraPermissions == null) info.ExtraPermissions = new List<string>();
			info.Aliases = info.Aliases.Select(NormalizeKey).Where(a => a.Length > 1).ToList();
			commands.Add(info);
			byCommand[info.Command] = info;
			foreach (string alias in info.Aliases) {
				if (!byCommand.ContainsKey(alias)) {
					byCommand[alias] = info;
				}
			}
			RegisterPermissions(info);
			return info;
		}

		static void RegisterPermissions(BccCommandInfo info)
		{
			string desc = info.OpenByDefault
				? $"Use {info.Command} (auto-created: everyone may use it until this permission is assigned to a group or player)"
				: $"Use {info.Command}";
			BccPermissions.Register(info.Permission, info.Mod, desc, info.OpenByDefault);
			foreach (string extra in info.ExtraPermissions) {
				BccPermissions.Register(extra, info.Mod, $"Extended rights for {info.Command}");
			}
		}

		// exact registry key only (no fallback to the first word)
		public static bool TryGetExact(string key, out BccCommandInfo info)
		{
			EnsureBuiltins();
			return byCommand.TryGetValue(NormalizeKey(key), out info);
		}

		public static bool TryGet(string command, out BccCommandInfo info)
		{
			EnsureBuiltins();
			return byCommand.TryGetValue(NormalizeKey(command), out info) || byCommand.TryGetValue(NormalizeCommand(command), out info);
		}

		public static List<BccCommandInfo> All()
		{
			EnsureBuiltins();
			RefreshForeign();
			return new List<BccCommandInfo>(commands);
		}

		// commands the player is allowed to use, BCC first (sorted), then other mods (grouped, sorted)
		public static List<BccCommandInfo> GetVisible(Players.Player player, string filter = null)
		{
			List<BccCommandInfo> result = new List<BccCommandInfo>();
			foreach (BccCommandInfo info in All()) {
				if (!CanUse(player, info)) {
					continue;
				}
				if (!string.IsNullOrEmpty(filter)) {
					string f = filter.ToLowerInvariant();
					if (!info.Command.Contains(f) && !info.Mod.ToLowerInvariant().Contains(f)
						&& !(info.Description ?? "").ToLowerInvariant().Contains(f) && !info.Aliases.Any(a => a.Contains(f))) {
						continue;
					}
				}
				result.Add(info);
			}
			return result
				.OrderBy(c => c.Mod == BCC_MOD ? 0 : 1)
				.ThenBy(c => c.Mod, StringComparer.OrdinalIgnoreCase)
				.ThenBy(c => c.Command, StringComparer.Ordinal)
				.ToList();
		}

		public static bool CanUse(Players.Player player, BccCommandInfo info)
		{
			if (info == null) {
				return false;
			}
			if (player == null) {
				return true;
			}
			if (string.IsNullOrEmpty(info.Permission)) {
				return true;
			}
			if (info.OpenByDefault) {
				return BccPermissions.HasOpenPermission(player, info.Permission);
			}
			return PermissionsManager.HasPermission(player, info.Permission);
		}

		// ---------------------------------------------------------------- foreign commands

		static void RefreshForeign(bool force = false)
		{
			if (!force && (DateTime.UtcNow - lastDiscovery).TotalSeconds < 30) {
				return;
			}
			lastDiscovery = DateTime.UtcNow;
			try {
				DiscoverRegistries();
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: command discovery failed (non-fatal): {e.Message}");
			}
			try {
				ImportLegacyHelpList();
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: legacy /help list import failed (non-fatal): {e.Message}");
			}
			try {
				DiscoverChatCommandHandlers();
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: chat command scan failed (non-fatal): {e.Message}");
			}
		}

		// ---------------------------------------------------------------- mods without a registry

		static readonly HashSet<Assembly> scannedAssemblies = new HashSet<Assembly>();
		static readonly List<(string mod, string command)> scannedCommands = new List<(string, string)>();
		static readonly Regex CommandLiteral = new Regex(@"^/[a-z][a-z0-9_\-]{0,30}$", RegexOptions.IgnoreCase);
		static Dictionary<short, OpCode> opCodes;

		// Every chat command handler of another mod is looked at once: the "/xyz" texts it compares
		// with are its commands. They are only added if no richer source knows them; the description
		// is the sentence of the mod's own description that names the command, if there is one.
		static void DiscoverChatCommandHandlers()
		{
			Assembly own = typeof(CommandRegistry).Assembly;
			Assembly game = typeof(PermissionsManager).Assembly;
			foreach (ModLoader.ModDescription mod in loadedMods) {
				if (mod == null || !mod.HasAssembly || mod.LoadedAssembly == null || mod.LoadedAssembly == own || mod.LoadedAssembly == game
					|| !scannedAssemblies.Add(mod.LoadedAssembly)) {
					continue;
				}
				// a mod that lists its commands itself (registry or BccApi) decides what is shown
				if (commands.Any(c => string.Equals(c.Mod, mod.name, StringComparison.OrdinalIgnoreCase) && (c.Source == "discovered" || c.Source == "api"))) {
					continue;
				}
				Type[] types;
				try {
					types = mod.LoadedAssemblyTypes;
				} catch (ReflectionTypeLoadException e) {
					types = e.Types.Where(t => t != null).ToArray();
				} catch {
					continue;
				}
				HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (Type t in types) {
					try {
						if (t == null || t.IsAbstract || t.IsInterface || !typeof(Chatting.IChatCommand).IsAssignableFrom(t)) {
							continue;
						}
						foreach (string s in StringLiterals(HandlerOf(t))) {
							if (CommandLiteral.IsMatch(s)) {
								found.Add(s.ToLowerInvariant());
							}
						}
					} catch {
					}
				}
				foreach (string cmd in found.OrderBy(c => c, StringComparer.Ordinal)) {
					scannedCommands.Add((mod.name, cmd));
				}
				if (found.Count > 0) {
					Log.Write($"Better Colony Commands: {mod.name} has no command list for other mods - found its commands {string.Join(", ", found.OrderBy(c => c, StringComparer.Ordinal))}");
				}
			}
			foreach ((string mod, string cmd) in scannedCommands) {
				if (byCommand.ContainsKey(cmd)) {
					continue;
				}
				Register(new BccCommandInfo {
					Command = cmd,
					Mod = mod,
					Description = DescriptionFromModInfo(mod, cmd),
					Usage = cmd,
					Source = "scanned"
				});
			}
		}

		static MethodInfo HandlerOf(Type t)
		{
			try {
				InterfaceMapping map = t.GetInterfaceMap(typeof(Chatting.IChatCommand));
				for (int i = 0; i < map.InterfaceMethods.Length; i++) {
					if (map.InterfaceMethods[i].Name == "TryDoCommand") {
						return map.TargetMethods[i];
					}
				}
			} catch {
			}
			return t.GetMethod("TryDoCommand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		}

		// the sentence of the mod's description that mentions the command ("Type /map to open ...")
		static string DescriptionFromModInfo(string mod, string cmd)
		{
			ModInfoCollector.ModEntry entry = ModInfoCollector.Find(mod);
			string text = entry?.Description;
			if (string.IsNullOrEmpty(text)) {
				return "";
			}
			foreach (string sentence in Regex.Split(text, @"(?<=[.!?])\s+")) {
				if (Regex.IsMatch(sentence, @"(^|[\s'""(])" + Regex.Escape(cmd) + @"($|[\s'"".,!?)])", RegexOptions.IgnoreCase)) {
					return sentence.Trim();
				}
			}
			return "";
		}

		// string literals (ldstr) in a method's compiled code
		static List<string> StringLiterals(MethodInfo m)
		{
			List<string> result = new List<string>();
			byte[] il = null;
			try {
				il = m?.GetMethodBody()?.GetILAsByteArray();
			} catch {
			}
			if (il == null) {
				return result;
			}
			if (opCodes == null) {
				opCodes = new Dictionary<short, OpCode>();
				foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
					if (f.GetValue(null) is OpCode op) {
						opCodes[op.Value] = op;
					}
				}
			}
			int pos = 0;
			while (pos < il.Length) {
				int code = il[pos++];
				if (code == 0xFE && pos < il.Length) {
					code = 0xFE00 | il[pos++];
				}
				if (!opCodes.TryGetValue(unchecked((short)code), out OpCode op)) {
					break; // unknown instruction: stop instead of reading garbage
				}
				int size;
				switch (op.OperandType) {
					case OperandType.InlineNone: size = 0; break;
					case OperandType.ShortInlineBrTarget:
					case OperandType.ShortInlineI:
					case OperandType.ShortInlineVar: size = 1; break;
					case OperandType.InlineVar: size = 2; break;
					case OperandType.InlineI8:
					case OperandType.InlineR: size = 8; break;
					case OperandType.InlineSwitch: size = pos + 4 <= il.Length ? 4 + 4 * BitConverter.ToInt32(il, pos) : il.Length; break;
					default: size = 4; break;
				}
				if (op.Value == OpCodes.Ldstr.Value && pos + 4 <= il.Length) {
					try {
						result.Add(m.Module.ResolveString(BitConverter.ToInt32(il, pos)));
					} catch {
					}
				}
				pos += size;
			}
			return result;
		}

		public static void ForceRefresh()
		{
			EnsureBuiltins();
			RefreshForeign(true);
		}

		static void DiscoverRegistries()
		{
			Assembly own = typeof(CommandRegistry).Assembly;
			List<(string mod, List<object> items)> found = new List<(string, List<object>)>();
			foreach (ModLoader.ModDescription mod in loadedMods) {
				if (mod == null || !mod.HasAssembly || mod.LoadedAssembly == own) {
					continue;
				}
				Type[] types;
				try {
					types = mod.LoadedAssemblyTypes;
				} catch (ReflectionTypeLoadException e) {
					types = e.Types.Where(t => t != null).ToArray();
				} catch {
					continue;
				}
				foreach (Type t in types) {
					if (t == null || !t.Name.EndsWith("CommandRegistry", StringComparison.Ordinal)) {
						continue;
					}
					IEnumerable items = GetStaticCollection(t, "All") ?? GetStaticCollection(t, "Commands");
					if (items != null) {
						found.Add((mod.name, items.Cast<object>().Where(o => o != null).ToList()));
					}
				}
			}
			// Some registries collect the commands of several mods (Pytonia Automation's also lists
			// WorldEdit's and Starter-Kit's). Smaller, mod specific registries go first, so every
			// command is attributed to the mod that actually owns it.
			foreach ((string mod, List<object> items) in found.OrderBy(f => f.items.Count)) {
				foreach (object item in items) {
					string cmd = ReadMember(item, "CommandText") ?? ReadMember(item, "Command") ?? ReadMember(item, "Name");
					if (string.IsNullOrEmpty(cmd)) {
						continue;
					}
					Register(new BccCommandInfo {
						Command = cmd,
						Mod = mod,
						Description = ReadMember(item, "Description") ?? "",
						Permission = ReadMember(item, "Permission") ?? "",
						Usage = ReadMember(item, "Usage") ?? cmd,
						Source = "discovered"
					});
				}
			}
		}

		static IEnumerable GetStaticCollection(Type t, string name)
		{
			const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
			try {
				PropertyInfo prop = t.GetProperty(name, flags);
				if (prop != null && prop.GetIndexParameters().Length == 0) {
					return prop.GetValue(null) as IEnumerable;
				}
				FieldInfo field = t.GetField(name, flags);
				if (field != null) {
					return field.GetValue(null) as IEnumerable;
				}
			} catch {
			}
			return null;
		}

		static string ReadMember(object item, string name)
		{
			Type t = item.GetType();
			const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
			FieldInfo f = t.GetField(name, flags);
			if (f != null) {
				return f.GetValue(item) as string;
			}
			PropertyInfo p = t.GetProperty(name, flags);
			if (p != null && p.GetIndexParameters().Length == 0) {
				return p.GetValue(item) as string;
			}
			return null;
		}

		static void ImportLegacyHelpList()
		{
			foreach (KeyValuePair<string, string> kvp in HelpCommand.GetLegacyEntries()) {
				string cmd = NormalizeKey(kvp.Key);
				if (byCommand.ContainsKey(cmd)) {
					continue;
				}
				Register(new BccCommandInfo {
					Command = cmd,
					Mod = UNKNOWN_MOD,
					Permission = kvp.Value ?? "",
					Source = "legacy"
				});
			}
		}
	}
}
