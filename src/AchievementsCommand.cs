using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Chatting;
using Pipliz;

namespace ColonyCommands
{
	// /achievements                      - are Steam achievements on for you, your colonies and this world?
	// /achievements enable {player|all}  - switches them back on (mods.scarabol.commands.achievements.enable)
	//
	// How the game handles it: there is no world switch. Every player and every colony group has an
	// "achievements disabled" state; cheats (/disableachievements, /loot, /teleport, /setflight, /debug ...)
	// switch it off for the player and all their colony groups, and a player joining a colony group that has
	// it off loses it too (and the other way round). The client awards Steam achievements and statistics only
	// while the server tells it "achievements on" (SetCheatState). Re-enabling therefore resets the player
	// (achievements, used-cheats flag), all their colony groups and - so it doesn't spread back -
	// the other owners of those groups, then sends the new state to the clients right away.
	public class AchievementsChatCommand : IChatCommand
	{
		public const string PERMISSION_ENABLE = AntiGrief.MOD_PREFIX + "achievements.enable";

		static readonly FieldInfo achievementsField = typeof(Players.Player).GetField("areAchievementsDisabled", BindingFlags.NonPublic | BindingFlags.Instance);
		static readonly FieldInfo cheatsField = typeof(Players.Player).GetField("hasEnabledCheats", BindingFlags.NonPublic | BindingFlags.Instance);

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/achievements", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			if (causedBy == null) {
				return true;
			}
			if (splits.Count >= 2 && splits[1].Equals("enable", StringComparison.OrdinalIgnoreCase)) {
				Enable(causedBy, splits.Count >= 3 ? string.Join(" ", splits.GetRange(2, splits.Count - 2)).Trim() : "");
				return true;
			}
			Status(causedBy);
			return true;
		}

		// ------------------------------------------------------------------ status

		static void Status(Players.Player p)
		{
			BccChat.Send(p, Style.Header("Steam achievements"));
			BccChat.Send(p, "You: " + (p.AreAchievementsDisabled
				? Style.Error("off") + Style.Muted(p.HasEnabledCheats ? " (cheats were used)" : " (disabled, e.g. through a colony)")
				: Style.Success("on")));
			for (int i = 0; i < p.ColonyGroups.Count; i++) {
				ColonyGroup g = p.ColonyGroups.GetAt(i);
				BccChat.Send(p, $"Colony {GroupName(g)}: " + (g.AreAchievementsDisabled ? Style.Error("off") : Style.Success("on")));
			}

			List<ColonyGroup> groups = AllGroups();
			List<ColonyGroup> groupsOff = groups.Where(g => g.AreAchievementsDisabled).ToList();
			List<Players.Player> players = AllPlayers();
			List<Players.Player> playersOff = players.Where(x => x.AreAchievementsDisabled || x.HasEnabledCheats).ToList();
			// how many colonies and players the server has is for admins only
			bool admin = PermissionsManager.HasPermission(p, PERMISSION_ENABLE);
			BccChat.Send(p, $"This world: {(groupsOff.Count == 0 && playersOff.Count == 0 ? Style.Success("on for everyone") : Style.Error("partly off"))}"
				+ (admin ? Style.Muted($" - colonies off: {groupsOff.Count} of {groups.Count}, players off: {playersOff.Count} of {players.Count}") : ""));
			if (admin && (groupsOff.Count > 0 || playersOff.Count > 0)) {
				if (playersOff.Count > 0) {
					BccChat.Send(p, Style.Muted("Players: ") + string.Join(", ", playersOff.Take(25).Select(ChatColors.GetDisplayName)) + (playersOff.Count > 25 ? Style.Muted($" ... +{playersOff.Count - 25}") : ""));
				}
				if (groupsOff.Count > 0) {
					BccChat.Send(p, Style.Muted("Colonies: ") + string.Join(", ", groupsOff.Take(25).Select(GroupName)) + (groupsOff.Count > 25 ? Style.Muted($" ... +{groupsOff.Count - 25}") : ""));
				}
				BccChat.Send(p, Style.Muted("Switch them back on: ") + Style.Cmd("/achievements enable {player}") + Style.Muted(" or ") + Style.Cmd("/achievements enable all"));
			} else if (p.AreAchievementsDisabled) {
				BccChat.Send(p, Style.Muted("Cheats (/disableachievements, /loot, /teleport, /setflight, /debug ...) or joining a colony that used them switch achievements off. An admin can switch them back on."));
			}
		}

		// ------------------------------------------------------------------ enable

		static void Enable(Players.Player causedBy, string target)
		{
			if (!PermissionsManager.HasPermission(causedBy, PERMISSION_ENABLE)) {
				BccChat.Send(causedBy, Style.Error($"You don't have permission to switch achievements back on ({PERMISSION_ENABLE})."));
				return;
			}
			if (achievementsField == null || cheatsField == null) {
				BccChat.Send(causedBy, Style.Error("This game version stores achievements differently - switching them back on is not possible."));
				return;
			}
			List<Players.Player> start;
			if (target.Equals("all", StringComparison.OrdinalIgnoreCase)) {
				start = AllPlayers();
			} else if (target.Length == 0) {
				start = new List<Players.Player> { causedBy };
			} else if (PlayerHelper.TryGetPlayer(target, out Players.Player tp, out string err, true)) {
				start = new List<Players.Player> { tp };
			} else {
				BccChat.Send(causedBy, Style.Error($"Player '{target}' not found: {err}"));
				return;
			}

			// the player(s), their colony groups and the other owners of those groups
			HashSet<Players.Player> players = new HashSet<Players.Player>();
			HashSet<ColonyGroup> groups = new HashSet<ColonyGroup>();
			Queue<Players.Player> todo = new Queue<Players.Player>(start);
			while (todo.Count > 0) {
				Players.Player p = todo.Dequeue();
				if (p == null || !players.Add(p)) {
					continue;
				}
				for (int i = 0; i < p.ColonyGroups.Count; i++) {
					ColonyGroup g = p.ColonyGroups.GetAt(i);
					if (g == null || !groups.Add(g)) {
						continue;
					}
					for (int k = 0; k < g.Owners.Count; k++) {
						todo.Enqueue(g.Owners.GetAt(k));
					}
				}
			}
			if (target.Equals("all", StringComparison.OrdinalIgnoreCase)) {
				foreach (ColonyGroup g in AllGroups()) {
					groups.Add(g);
				}
			}

			int playersChanged = 0, groupsChanged = 0, stillFlying = 0;
			foreach (ColonyGroup g in groups) {
				if (g.AreAchievementsDisabled) {
					g.AreAchievementsDisabled = false;
					groupsChanged++;
				}
			}
			foreach (Players.Player p in players) {
				if (!p.AreAchievementsDisabled && !p.HasEnabledCheats) {
					continue;
				}
				// flight mode is left alone: other mods (e.g. WorldEdit) give it without touching achievements
				if (p.HasFlightMode) {
					stillFlying++;
				}
				achievementsField.SetValue(p, false);
				cheatsField.SetValue(p, false);
				p.IsDirty = true;
				p.SendCheatState();
				playersChanged++;
				if (p.ConnectionState == Players.EConnectionState.Connected && p != causedBy) {
					BccChat.Send(p, Style.Success($"{ChatColors.GetDisplayName(causedBy)} switched Steam achievements back on for you on this world."));
				}
			}
			Log.Write($"Better Colony Commands: {causedBy.Name} switched achievements back on ({(target.Length == 0 ? "self" : target)}): {playersChanged} player(s) [{string.Join(", ", players.Where(x => x != null).Select(x => x.Name).Take(30))}], {groupsChanged} colony group(s), {stillFlying} of them still have flight mode");
			if (playersChanged == 0 && groupsChanged == 0) {
				BccChat.Send(causedBy, Style.Success("Achievements were already on there - nothing to change."));
				return;
			}
			BccChat.Send(causedBy, Style.Success($"Steam achievements are on again for {playersChanged} player(s) and {groupsChanged} colony group(s)")
				+ Style.Muted((stillFlying > 0 ? $"; {stillFlying} of them still have flight mode (/setflight to change)" : "") + ". Using cheats again switches them off."));
			if (players.Count > start.Count && !target.Equals("all", StringComparison.OrdinalIgnoreCase)) {
				BccChat.Send(causedBy, Style.Muted("Also the co-owners of their colonies (otherwise the game would switch them off again): ") + string.Join(", ", players.Except(start).Select(ChatColors.GetDisplayName)));
			}
		}

		// ------------------------------------------------------------------ helpers

		static List<ColonyGroup> AllGroups()
		{
			List<ColonyGroup> result = new List<ColonyGroup>();
			var e = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
			while (e.MoveNext()) {
				if (e.Current != null) {
					result.Add(e.Current);
				}
			}
			return result;
		}

		// every known player once (several profiles of one account count separately in the game, too)
		static List<Players.Player> AllPlayers()
		{
			return Players.PlayerDatabase.Values.Where(p => p != null && !Players.IsServerID(p.ID)).ToList();
		}

		static string GroupName(ColonyGroup g)
		{
			try {
				return TextHelper.SafeName(g.Name ?? "(no name)");
			} catch {
				return "(no name)";
			}
		}
	}
}
