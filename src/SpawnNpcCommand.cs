using Chatting;
using Chatting.Commands;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NPC;
using BlockEntities.Implementations;

namespace ColonyCommands
{

	public class SpawnNpcCommand : IChatCommand
	{

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/spawnnpc")) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "npcandbeds")) {
				return true;
			}

			Match m = Regex.Match(chattext, @"/spawnnpc (?<amount>\d+) ?(?<player>['].+[']|.+)?");
			if (!m.Success) {
				BccChat.Send(causedBy, "Syntax: /spawnnpc {number} [targetplayer]");
				return true;
			}
			int amount = 0;
			if (!int.TryParse(m.Groups["amount"].Value, out amount) || amount <= 0) {
				BccChat.Send(causedBy, "Number should be > 0");
				return true;
			}

			Players.Player target = causedBy;
			string error;
			if (!m.Groups["player"].Value.Equals("")) {
				if (!PlayerHelper.TryGetPlayer(m.Groups["player"].Value, out target, out error)) {
					BccChat.Send(causedBy, "Could not find target: {error}");
				}
			}

			Colony colony = target.ActiveColony;
			if (colony == null) {
				BccChat.Send(target, "You need to be at an active colony to spawn NPCs");
				if (causedBy != target) {
					BccChat.Send(causedBy, " {target} has no active colony");
				}
				return true;
			}
			BannerTracker.Banner banner = colony.GetClosestBanner(causedBy.PositionVoxelStanding);
			if (banner == null) {
				BccChat.Send(causedBy, "No banners found for the active colony");
				return true;
			}

			for (int i = 0; i < amount; i++) {
				NPCBase npc = new NPCBase(colony, banner.Position);
				NPCTracker.Add(npc);
				colony.RegisterNPC(npc);
				ModLoader.Callbacks.OnNPCRecruited.Invoke(npc);
			}

			BccChat.Send(causedBy, $"Spawned {amount} colonists");
			return true;
		}
	}
}
