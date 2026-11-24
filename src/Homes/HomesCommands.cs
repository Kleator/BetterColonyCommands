using System;
using System.Collections.Generic;
using System.Linq;
using Chatting;

namespace ColonyCommands
{
	// /sethome [name], /home [name], /homes, /delhome [name] - Better Colony Commands' home points.
	// Routed by the CommandInterceptor before any other mod, so an older Pytonia Automation that
	// still has its own home commands never answers them.
	public class HomesChatCommand : IChatCommand
	{
		public static readonly HashSet<string> Commands = new HashSet<string> { "/sethome", "/home", "/homes", "/delhome" };

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0) {
				return false;
			}
			string cmd = splits[0].ToLowerInvariant();
			if (cmd != "/sethome" && cmd != "/home" && cmd != "/homes" && cmd != "/delhome") {
				return false;
			}
			if (!HomesManager.IsActive) {
				BccChat.Send(causedBy, Style.Error("Home points are disabled on this server."));
				return true;
			}
			string name = splits.Count >= 2 ? string.Join(" ", splits.GetRange(1, splits.Count - 1)).Trim() : "home";
			switch (cmd) {
				case "/sethome": {
					string error = HomesManager.SetHome(causedBy, name);
					BccChat.Send(causedBy, error == null ? Style.Success($"Home '{Style.Value(name)}' set to your current position.") : Style.Error(error));
					break;
				}
				case "/home": {
					HomePoint home = HomesManager.Find(causedBy, name);
					if (home == null) {
						BccChat.Send(causedBy, Style.Error($"No home named '{name}'. Your homes: {Describe(causedBy)}"));
						break;
					}
					HomesManager.TeleportHome(causedBy, home);
					BccChat.Send(causedBy, Style.Success($"Teleported to home '{Style.Value(home.Name)}'."));
					break;
				}
				case "/delhome":
					BccChat.Send(causedBy, HomesManager.DeleteHome(causedBy, name)
						? Style.Success($"Home '{Style.Value(name)}' removed.")
						: Style.Error($"No home named '{name}'. Your homes: {Describe(causedBy)}"));
					break;
				default:
					if (!BccChat.IsCapturing(causedBy) && UiPreferences.UsesWindow(causedBy, "/homes")) {
						UiManager.OpenNamed(causedBy, "homes", true);
						break;
					}
					int limit = HomesManager.GetLimit(causedBy);
					List<HomePoint> homes = HomesManager.GetHomes(causedBy);
					BccChat.Send(causedBy, Style.Header($"Your homes ({homes.Count}/{(limit < 0 ? "unlimited" : limit.ToString())})"));
					foreach (HomePoint h in homes) {
						BccChat.Send(causedBy, $"{Style.Value(h.Name)} {Style.Muted($"{(int)h.X} {(int)h.Y} {(int)h.Z}")}");
					}
					if (homes.Count == 0) {
						BccChat.Send(causedBy, Style.Muted("No homes yet - set one with /sethome [name]."));
					}
					break;
			}
			return true;
		}

		public static string Describe(Players.Player player)
		{
			List<HomePoint> homes = HomesManager.GetHomes(player);
			return homes.Count == 0 ? "none yet" : string.Join(", ", homes.Select(h => h.Name));
		}
	}
}
