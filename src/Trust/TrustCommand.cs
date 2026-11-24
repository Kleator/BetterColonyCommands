using System;
using System.Collections.Generic;
using Chatting;

namespace ColonyCommands
{
	// /trust                  - "Manage Permission" window of your active colony
	// /trust {player}         - that player and all your claims (window)
	// /trust add|remove {player}, /trust list - the same in chat, for your active colony
	public class TrustChatCommand : IChatCommand
	{
		public static readonly HashSet<string> ChatSubcommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "add", "remove", "list" };

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/trust", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			if (causedBy == null) {
				return true;
			}
			string sub = splits.Count > 1 ? splits[1] : "";
			string rest = splits.Count > 2 ? string.Join(" ", splits.GetRange(2, splits.Count - 2)).Trim() : "";
			Colony colony = causedBy.ActiveColony;

			if (ChatSubcommands.Contains(sub)) {
				if (colony == null) {
					BccChat.Send(causedBy, Style.Error("You have no active colony - found one or choose it in the Colony tab first."));
					return true;
				}
				if (!TrustManager.CanManage(causedBy, colony)) {
					BccChat.Send(causedBy, Style.Error("Only the owners of this colony can choose who may build there."));
					return true;
				}
				if (sub.Equals("list", StringComparison.OrdinalIgnoreCase)) {
					List<TrustManager.TrustEntry> trusted = TrustManager.TrustedOn(colony);
					BccChat.Send(causedBy, Style.Header($"Trusted on {TextHelper.SafeName(colony.Name ?? "")} ({trusted.Count})"));
					foreach (TrustManager.TrustEntry e in trusted) {
						BccChat.Send(causedBy, ChatColors.GetDisplayName(e.Player) + (e.Kind == TrustManager.TrustKind.AllOfOwner ? Style.Muted($" (all claims of {e.Owner.Name})") : ""));
					}
					if (trusted.Count == 0) {
						BccChat.Send(causedBy, Style.Muted("Nobody yet - /trust add {player}"));
					}
					return true;
				}
				if (rest.Length == 0) {
					BccChat.Send(causedBy, Style.Error($"Usage: /trust {sub.ToLowerInvariant()} {{player}}"));
					return true;
				}
				if (!PlayerHelper.TryGetPlayer(rest, out Players.Player target, out string perr, true)) {
					BccChat.Send(causedBy, Style.Error($"Player '{rest}' not found: {perr}"));
					return true;
				}
				string error = sub.Equals("add", StringComparison.OrdinalIgnoreCase)
					? TrustManager.Trust(causedBy, target, colony)
					: TrustManager.Untrust(causedBy, target, colony);
				BccChat.Send(causedBy, error != null ? Style.Error(error)
					: Style.Success(sub.Equals("add", StringComparison.OrdinalIgnoreCase)
						? $"{target.Name} may build at {TextHelper.SafeName(colony.Name ?? "")} now."
						: $"{target.Name} may no longer build at {TextHelper.SafeName(colony.Name ?? "")}."));
				return true;
			}

			if (!UiPreferences.ServerAllowsWindows) {
				BccChat.Send(causedBy, Style.Error("Windows are disabled on this server - use /trust add|remove {player} or /trust list."));
				return true;
			}
			if (sub.Length > 0) {
				string name = string.Join(" ", splits.GetRange(1, splits.Count - 1)).Trim();
				if (!PlayerHelper.TryGetPlayer(name, out Players.Player target, out string perr, true)) {
					BccChat.Send(causedBy, Style.Error($"Player '{name}' not found: {perr}"));
					return true;
				}
				UiManager.Open(causedBy, new UiManager.PageState { Kind = "trustplayer", Key = target.ID.ToStringReadable() }, true);
				return true;
			}
			if (colony == null) {
				BccChat.Send(causedBy, Style.Error("You have no active colony - found one or choose it in the Colony tab first. /trust {player} shows all your claims."));
				return true;
			}
			if (!TrustManager.CanManage(causedBy, colony)) {
				BccChat.Send(causedBy, Style.Error("Only the owners of this colony can choose who may build there."));
				return true;
			}
			UiManager.Open(causedBy, new UiManager.PageState { Kind = "trust", Key = colony.ColonyID.Value.ToString() }, true);
			return true;
		}
	}
}
