using System.Collections.Generic;
using Pipliz;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{
	// shared pending-request state backing /tpa, /tpaccept and /tpadeny
	public static class TpaManager
	{
		const double REQUEST_TIMEOUT_SECONDS = 60.0;
		const double ACCEPT_COUNTDOWN_SECONDS = 3.0;

		class PendingRequest
		{
			public Players.Player Requester;
			public int Version;
		}

		// keyed by the target player (the one who must /tpaccept or /tpadeny)
		static Dictionary<Players.Player, PendingRequest> pendingRequests = new Dictionary<Players.Player, PendingRequest>();
		static Dictionary<Players.Player, int> requestVersions = new Dictionary<Players.Player, int>();


		public static bool TryCreateRequest(Players.Player requester, Players.Player target, out string error)
		{
			error = "";
			if (requester == target) {
				error = "You can't send a teleport request to yourself";
				return false;
			}
			if (pendingRequests.ContainsKey(target)) {
				error = $"{target.Name} already has a pending teleport request";
				return false;
			}

			int version = NextVersion(target);
			pendingRequests[target] = new PendingRequest { Requester = requester, Version = version };

			BccChat.Send(requester, $"Teleport request sent to {ChatColors.GetDisplayName(target)}. It will expire in {(int)REQUEST_TIMEOUT_SECONDS} seconds.");
			BccChat.Send(target, $"{ChatColors.GetDisplayName(requester)} wants to teleport to you. Type /tpaccept or /tpadeny (expires in {(int)REQUEST_TIMEOUT_SECONDS}s).");

			ThreadManager.InvokeOnMainThread(delegate() {
				ExpireIfStillPending(target, version);
			}, REQUEST_TIMEOUT_SECONDS);

			return true;
		}


		public static bool TryAccept(Players.Player target, out string error)
		{
			error = "";
			if (!pendingRequests.TryGetValue(target, out PendingRequest request)) {
				error = "You have no pending teleport request";
				return false;
			}
			pendingRequests.Remove(target);

			Players.Player requester = request.Requester;
			// countdown so the requester isn't instantly yanked without warning
			BccChat.Send(requester, $"{ChatColors.GetDisplayName(target)} accepted your teleport request. Teleporting in {(int)ACCEPT_COUNTDOWN_SECONDS} seconds, stay put!");
			BccChat.Send(target, $"Accepted. Teleporting {ChatColors.GetDisplayName(requester)} to you in {(int)ACCEPT_COUNTDOWN_SECONDS} seconds.");

			ThreadManager.InvokeOnMainThread(delegate() {
				CompleteTeleport(requester, target);
			}, ACCEPT_COUNTDOWN_SECONDS);

			return true;
		}


		public static bool TryDeny(Players.Player target, out string error)
		{
			error = "";
			if (!pendingRequests.TryGetValue(target, out PendingRequest request)) {
				error = "You have no pending teleport request";
				return false;
			}
			pendingRequests.Remove(target);

			BccChat.Send(request.Requester, $"{ChatColors.GetDisplayName(target)} denied your teleport request");
			BccChat.Send(target, "Teleport request denied");
			return true;
		}


		static void CompleteTeleport(Players.Player requester, Players.Player target)
		{
			if (requester.ConnectionState != Players.EConnectionState.Connected || target.ConnectionState != Players.EConnectionState.Connected) {
				return;
			}
			Teleport.TeleportTo(requester, target.PositionStanding);
		}


		static void ExpireIfStillPending(Players.Player target, int version)
		{
			if (pendingRequests.TryGetValue(target, out PendingRequest request) && request.Version == version) {
				pendingRequests.Remove(target);
				BccChat.Send(request.Requester, $"Your teleport request to {ChatColors.GetDisplayName(target)} timed out");
				BccChat.Send(target, "The pending teleport request timed out");
			}
		}


		static int NextVersion(Players.Player target)
		{
			int version = requestVersions.TryGetValue(target, out int v) ? v + 1 : 0;
			requestVersions[target] = version;
			return version;
		}
	}
}
