using Networking;
using Shared.Options;

namespace ColonyCommands
{
	// How the game treats permissions depends on how the world is running:
	//  - Singleplayer (2) and LAN/LocalHost (3): players connect without SteamID. The game gives them
	//    every permission and can't store roles or own permissions for them.
	//  - Hosted for friends ("SteamListen", 5): everybody has a SteamID; the host is connected through
	//    a local, RCON-authenticated connection and therefore always has every permission. Roles and
	//    permissions work for the guests and are stored with the world.
	//  - Dedicated Steam server (1, 4): roles and permissions work for everybody.
	public static class NetworkInfo
	{
		public static EServerNetworkType Type => NetworkWrapper.NetworkType;

		public static bool IsWithoutSteam => Type == EServerNetworkType.Singleplayer || Type == EServerNetworkType.LocalHost;

		public static bool IsHostedForFriends => Type == EServerNetworkType.SteamListen;

		// the host of a game hosted for friends (always all permissions)
		public static bool IsListenHost(Players.Player player)
		{
			return IsHostedForFriends && player != null && player.ID.HasSteamID && RCONHandler.CheckAuthenticatedNoKick(player.ID);
		}

		// notice for pages and commands about roles/permissions; null on a normal server
		public static string PermissionNotice(Players.Player viewer)
		{
			if (Type == EServerNetworkType.Singleplayer) {
				return "Singleplayer: the game connects players without SteamID - it gives them every permission and can't store roles or own permissions. Roles and permissions only work on a server (a dedicated server, or the world hosted for friends).";
			}
			if (Type == EServerNetworkType.LocalHost) {
				return "LAN game: the game connects players without SteamID - every player has every permission and roles or own permissions can't be stored. Roles and permissions only work on a Steam server (dedicated, or hosted for friends).";
			}
			if (IsListenHost(viewer)) {
				return "Hosted game: as the host you always have every permission (the game treats the host as server admin). Roles and permissions apply to your guests and are stored with the world.";
			}
			return null;
		}
	}
}
