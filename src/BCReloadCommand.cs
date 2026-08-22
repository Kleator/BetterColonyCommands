using System.Collections.Generic;
using System.IO;
using Pipliz;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{
	// Reloads this mod's config files from disk without a server restart. Deliberately only
	// touches genuine admin-tunable config, not runtime/history data (playeractivity.json,
	// banned-players-log.json, jail-log.json, roleplay-banned.json, war state) and never
	// re-runs the startup-only calls that would start a second copy of a self-rescheduling
	// background loop (announcement broadcasts, jail release checks, colony name scans, war
	// status checks) - see JailManager.ReloadConfig / ColonyNameGuard.ReloadConfig.
	public class BCReloadChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/bcreload")) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "bcreload")) {
				return true;
			}

			AntiGrief.Load();
			ChatColors.LoadChatColors();
			TravelManager.Load();
			ColonyNameGuard.ReloadConfig();
			JailManager.ReloadConfig();
			if (File.Exists(Path.Combine("gamedata", "savegames", ServerManager.WorldName, "announcements.json"))) {
				Announcements.Load();
			}

			Chat.Send(causedBy, "<color=yellow>BetterColonyCommands: config reloaded (antigrief-config.json, chatcolors.json, travelpaths.json, blacklistedwords.json, jail-config.json, announcements.json)</color>");
			Log.Write($"{causedBy.Name} reloaded BetterColonyCommands configuration via /bcreload");
			return true;
		}
	}
}
