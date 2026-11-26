using System;
using System.Collections.Generic;

namespace ColonyCommands
{
	// Public, stable integration API for other mods. Every method only uses primitive types and
	// Players.Player, so it can be called either with a direct reference to ColonyCommands.dll or
	// - recommended, keeps Better Colony Commands an optional dependency - via reflection:
	//
	//   Type api = AppDomain.CurrentDomain.GetAssemblies()
	//       .Select(a => a.GetType("ColonyCommands.BccApi")).FirstOrDefault(t => t != null);
	//   api?.GetMethod("RegisterCommand", new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string) })
	//      ?.Invoke(null, new object[] { "My Mod", "/mycmd", "Does something.", "mymod.mycmd", "/mycmd {player}" });
	//
	// See Dev\_Wissensbasis\06_BCC-API.md for the full guide.
	public static class BccApi
	{
		// Bumped when methods are added. 1 = first version (BCC 1.1.0), 2 = audio sources (BCC 1.2.0).
		public const int ApiVersion = 2;

		public static int GetApiVersion() => ApiVersion;

		// ------------------------------------------------------------------ commands & permissions

		// permission null/"" => an "open by default" permission "<modslug>.cmd.<command>" is created:
		// everybody may use the command until an admin assigns exactly that permission somewhere.
		// Returns the permission that is effectively used for the command.
		public static string RegisterCommand(string modName, string command, string description, string permission)
		{
			return RegisterCommand(modName, command, description, permission, null);
		}

		public static string RegisterCommand(string modName, string command, string description, string permission, string usage)
		{
			return RegisterCommandEx(modName, command, description, permission, usage, null, null, null);
		}

		// parameterNames / parameterDescriptions: one entry per argument, used for the input fields in
		// the command window. A name ending in '?' marks the argument as optional ("player?").
		public static string RegisterCommandEx(string modName, string command, string description, string permission, string usage,
			string[] parameterNames, string[] parameterDescriptions, string[] aliases)
		{
			if (string.IsNullOrEmpty(command)) {
				return null;
			}
			List<BccCommandParam> parameters = new List<BccCommandParam>();
			if (parameterNames != null) {
				for (int i = 0; i < parameterNames.Length; i++) {
					string name = parameterNames[i] ?? "";
					bool optional = name.EndsWith("?");
					if (optional) {
						name = name.Substring(0, name.Length - 1);
					}
					string desc = parameterDescriptions != null && i < parameterDescriptions.Length ? parameterDescriptions[i] : "";
					parameters.Add(new BccCommandParam(name, desc, optional));
				}
			}
			BccCommandInfo info = CommandRegistry.Register(new BccCommandInfo {
				Command = command,
				Mod = string.IsNullOrEmpty(modName) ? CommandRegistry.UNKNOWN_MOD : modName,
				Description = description ?? "",
				Permission = permission ?? "",
				Usage = usage ?? "",
				Parameters = parameters,
				Aliases = aliases != null ? new List<string>(aliases) : new List<string>(),
				Source = "api"
			});
			return info?.Permission;
		}

		// Documents a permission (shown in /permissions with mod and description).
		public static void RegisterPermission(string modName, string permission, string description)
		{
			BccPermissions.Register(permission, modName, description);
		}

		// Effective check for a command registered with BCC (respects "open by default").
		public static bool CanUseCommand(Players.Player player, string command)
		{
			return CommandRegistry.TryGet(command, out BccCommandInfo info) && CommandRegistry.CanUse(player, info);
		}

		// Check with "open by default" semantics for any permission string.
		public static bool HasOpenPermission(Players.Player player, string permission)
		{
			return BccPermissions.HasOpenPermission(player, permission);
		}

		// ------------------------------------------------------------------ economy

		public static bool IsEconomyEnabled() => EconomyManager.IsEnabled;

		// "BetterColonyCommands" or "PytoniaAutomation" - whichever currently holds the balances
		public static string GetEconomyBackend() => EconomyManager.BackendName;

		public static string GetCurrencyName() => EconomyManager.CurrencyName;

		public static string FormatMoney(long amount) => EconomyManager.Format(amount);

		public static long GetBalance(Players.Player player) => EconomyManager.GetBalance(player);

		// amount may be negative; returns false if the economy is disabled or the balance would go below 0
		public static bool AddBalance(Players.Player player, long amount, string reason) => EconomyManager.TryAdd(player, amount, reason);

		public static bool TryRemoveBalance(Players.Player player, long amount, string reason) => EconomyManager.TryRemove(player, amount, reason);

		public static bool SetBalance(Players.Player player, long amount, string reason) => EconomyManager.TrySet(player, amount, reason);

		public static bool TryTransfer(Players.Player from, Players.Player to, long amount, string reason) => EconomyManager.TryTransfer(from, to, amount, reason);

		// ------------------------------------------------------------------ UI

		// Shows lines (rich text allowed) in a Better Colony Commands output window with paging and a back button.
		public static void ShowOutputWindow(Players.Player player, string title, string[] lines)
		{
			UiManager.ShowOutput(player, title, lines != null ? new List<string>(lines) : new List<string>(), true);
		}

		// True if the player and the server allow window output for this command.
		public static bool UsesWindowFor(Players.Player player, string command) => UiPreferences.UsesWindow(player, command);

		// ------------------------------------------------------------------ audio sources (API 2)

		// Names of all audio sources placed with /audio.
		public static string[] GetAudioSourceNames() => BccAudio.Sources.ConvertAll(s => s.Name).ToArray();

		// Starts an audio source like /audio play (ignores cooldown and time condition). player = the
		// triggering player (only matters for sources whose audience is "only the triggering player"),
		// may be null. Returns null on success, otherwise the reason.
		public static string PlayAudioSource(string name, Players.Player player)
		{
			AudioSourceDef def = BccAudio.Find(name);
			return def == null ? "unknown audio source" : BccAudio.Play(def, player);
		}

		// Stops what a trigger / PlayAudioSource started (with the source's fade-out).
		public static bool StopAudioSource(string name)
		{
			AudioSourceDef def = BccAudio.Find(name);
			if (def == null) {
				return false;
			}
			BccAudio.Stop(def);
			return true;
		}

		// Switches a source on or off.
		public static bool SetAudioSourceEnabled(string name, bool enabled)
		{
			AudioSourceDef def = BccAudio.Find(name);
			if (def == null) {
				return false;
			}
			AudioSourceDef before = def.Clone();
			def.Enabled = enabled;
			BccAudio.Changed(def, before, null);
			return true;
		}

		// ------------------------------------------------------------------ text helpers

		public static string StripRichText(string text) => TextHelper.StripRichText(text);

		public static string SafeName(string text) => TextHelper.SafeName(text);

		public static string FormatRole(string roleName) => ChatColors.FormatRole(roleName);
	}
}
