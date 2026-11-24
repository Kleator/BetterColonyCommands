using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chatting;
using Newtonsoft.Json.Linq;
using Pipliz;

namespace ColonyCommands
{
	// Role online-time requirements. Source: Pytonia Automation's time based roles
	// (pytonia-automation.json -> Roles.<role>.RequiredHoursOnline, roles with
	// ExcludeFromTimeBasedAssignment or 0 hours are not time based), read from disk so it also works
	// without that mod loaded. Fallback: bcc-settings.json RoleTime.RequiredHours.
	public static class RoleTimeData
	{
		public class RoleRequirement
		{
			public string Role;
			public double Hours;
		}

		static string PytoniaPath => Path.Combine("gamedata", "savegames", ServerManager.WorldName, "pytonia-automation.json");

		// Time based roles are a Pytonia Automation feature: live data while it is loaded, otherwise
		// nothing (also not from an old pytonia-automation.json - the roles wouldn't be granted anyway).
		public static bool Available => PytoniaBridge.IsInstalled;

		public static List<RoleRequirement> GetRequirements(out string source, out Dictionary<string, List<string>> grantedByPlayer)
		{
			grantedByPlayer = new Dictionary<string, List<string>>();
			List<RoleRequirement> result = new List<RoleRequirement>();
			source = PytoniaBridge.MOD_NAME;
			if (!Available) {
				return result;
			}
			try {
				JObject root = PytoniaBridge.IsReady
					? JObject.FromObject(PytoniaBridge.Config())
					: PytoniaBridge.ReadDisk();
				if (root == null) {
					return result;
				}
				bool enabled = root["TimeBasedRoles"]?["Enabled"]?.Value<bool>() ?? true;
				if (root["Roles"] is JObject roles && enabled) {
					foreach (JProperty p in roles.Properties()) {
						bool excluded = p.Value["ExcludeFromTimeBasedAssignment"]?.Value<bool>() ?? true;
						double hours = p.Value["RequiredHoursOnline"]?.Value<double>() ?? 0.0;
						if (!excluded && hours > 0.0) {
							result.Add(new RoleRequirement { Role = p.Name, Hours = hours });
						}
					}
				}
				if (root["TimeBasedRoles"]?["GrantedRoleKeys"] is JObject granted) {
					foreach (JProperty p in granted.Properties()) {
						if (p.Value is JArray arr) {
							grantedByPlayer[p.Name] = arr.Select(t => t.Value<string>()).Where(x => x != null).ToList();
						}
					}
				}
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not read Pytonia Automation's time based roles: {e.Message}");
			}
			return result.OrderBy(r => r.Hours).ToList();
		}

		public static HashSet<string> GetOwnedRoles(Players.Player player, Dictionary<string, List<string>> grantedByPlayer)
		{
			HashSet<string> owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (RoleIndex.Holding h in RoleIndex.Of(player)) {
				owned.Add(h.Role);
			}
			if (grantedByPlayer.TryGetValue(player.ID.ToStringReadable(), out List<string> granted)) {
				foreach (string g in granted) {
					owned.Add(g);
				}
			}
			return owned;
		}
	}

	public class RoleTimeChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/roletime", StringComparison.OrdinalIgnoreCase) || !RoleTimeData.Available) {
				return false;
			}
			long seconds = ActivityTracker.GetLiveSecondsPlayed(causedBy);
			double hoursPlayed = seconds / 3600.0;
			List<RoleTimeData.RoleRequirement> reqs = RoleTimeData.GetRequirements(out string source, out Dictionary<string, List<string>> granted);
			HashSet<string> owned = RoleTimeData.GetOwnedRoles(causedBy, granted);

			BccChat.Send(causedBy, Style.Header("Role time"));
			BccChat.Send(causedBy, $"Your online time: {Style.Value(TextHelper.FormatDuration(seconds))} ({hoursPlayed:0.0}h)");
			if (reqs.Count == 0) {
				BccChat.Send(causedBy, Style.Muted("No time based roles are configured on this server."));
				return true;
			}
			// still ahead = not owned yet (by group or grant record); the next one first, the last one at the end
			List<RoleTimeData.RoleRequirement> ahead = reqs.Where(r => !owned.Contains(r.Role)).OrderBy(r => r.Hours).ToList();
			if (ahead.Count == 0) {
				BccChat.Send(causedBy, Style.Success("You already reached every time based role!"));
				return true;
			}
			// next = the lowest role whose time is not reached yet; roles already reached but not
			// granted yet (the granting mod checks every few minutes) are only marked in the list
			RoleTimeData.RoleRequirement next = ahead.Where(r => r.Hours * 3600.0 > seconds).OrderBy(r => r.Hours).FirstOrDefault();
			if (next != null) {
				BccChat.Send(causedBy, $"Next role: {ChatColors.FormatRole(next.Role)} in {Style.Value(TextHelper.FormatDuration(next.Hours * 3600.0 - seconds))}");
			} else {
				BccChat.Send(causedBy, Style.Success("You reached the time for every role - the remaining ones are granted shortly."));
			}
			BccChat.Send(causedBy, Style.SubHeader("Roles still ahead of you:"));
			foreach (RoleTimeData.RoleRequirement r in ahead) {
				double left = r.Hours * 3600.0 - seconds;
				string leftText = left > 0 ? $"{TextHelper.FormatDuration(left)} left" : "reached - granted shortly";
				BccChat.Send(causedBy, $"{ChatColors.FormatRole(r.Role)} - {FormatHours(r.Hours)} {Style.Muted("(" + leftText + ")")}");
			}
			return true;
		}

		static string FormatHours(double hours)
		{
			return hours == System.Math.Floor(hours) ? $"{hours:0}h" : $"{hours:0.##}h";
		}
	}

	public class RolesChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/roles", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			if (!PermissionsManager.EnsureLoadedSuccesfully() || PermissionsManager.Groups == null) {
				BccChat.Send(causedBy, Style.Error("Permission groups are not loaded."));
				return true;
			}
			List<RoleTimeData.RoleRequirement> reqs = RoleTimeData.GetRequirements(out string source, out Dictionary<string, List<string>> granted);
			Dictionary<string, double> hoursByRole = reqs.GroupBy(r => r.Role, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Hours, StringComparer.OrdinalIgnoreCase);
			HashSet<string> mine = new HashSet<string>(RoleIndex.Of(causedBy).Select(h => h.Role), StringComparer.OrdinalIgnoreCase);

			// tree order: higher roles first, each followed by the roles it inherits from
			List<string> ordered = RoleManager.Tree().Select(t => t.role).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			ordered.AddRange(PermissionsManager.Groups.Keys.Where(k => !ordered.Contains(k, StringComparer.OrdinalIgnoreCase)).OrderBy(k => k));

			BccChat.Send(causedBy, Style.Header($"Roles ({ordered.Count})"));
			string notice = NetworkInfo.PermissionNotice(causedBy);
			if (notice != null) {
				BccChat.Send(causedBy, $"<color=#FFB347>{notice}</color>");
			}
			foreach (string role in ordered) {
				string line = ChatColors.FormatRole(role);
				if (hoursByRole.TryGetValue(role, out double h)) {
					line += Style.Muted($" - {h:0.##}h online");
				}
				if (mine.Contains(role)) {
					line += " " + Style.Success("(yours)");
				}
				BccChat.Send(causedBy, line);
			}
			return true;
		}
	}
}
