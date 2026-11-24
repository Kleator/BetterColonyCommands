using System.Text.RegularExpressions;
using System.Collections.Generic;
using Pipliz;
using Chatting;
using Chatting.Commands;
using BlockEntities.Implementations;

namespace ColonyCommands
{

	// /warpbanner           - to the closest banner of any of your own colonies (all colonies and
	//                          outposts of all your colony groups, like the game's own teleport)
	// /warpbanner <name>    - to a colony by its displayed name (formatting/symbols ignored); you
	//                          land at the banner of that colony closest to you
	public class WarpBannerChatCommand : IChatCommand
	{

		public bool TryDoCommand (Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/warpbanner")) {
				return false;
			}

			Colony targetColony = null;
			BannerTracker.Banner targetBanner = null;
			Match m = Regex.Match(chattext, @"/warpbanner\s+(?<target>\S.*)");
			if (m.Success) {
				string error;
				if (!PlayerHelper.TryGetColony(m.Groups["target"].Value.Trim(), out targetColony, out error)) {
					BccChat.Send(causedBy, $"Could not find target: {error}");
					return true;
				}
				targetColony.Banners.GetClosestDistance(causedBy.PositionVoxelStanding, out targetBanner);
				if (targetBanner == null) {
					BccChat.Send(causedBy, $"The colony {TextHelper.SafeName(targetColony.Name)} has no banner to warp to.");
					return true;
				}
			} else {
				int minDistance = int.MaxValue;
				for (int i = 0; i < causedBy.ColonyGroups.Count; i++) {
					ColonyGroup group = causedBy.ColonyGroups[i];
					for (int j = 0; j < group.Colonies.Count; j++) {
						Colony colony = group.Colonies.GetAt(j);
						BannerTracker.Banner found;
						int distance = colony.Banners.GetClosestDistance(causedBy.PositionVoxelStanding, out found);
						if (found != null && distance < minDistance) {
							minDistance = distance;
							targetColony = colony;
							targetBanner = found;
						}
					}
				}
				if (targetBanner == null) {
					BccChat.Send(causedBy, "Could not find any banner of your colonies to warp to");
					return true;
				}
			}

			string permission = AntiGrief.MOD_PREFIX + "warp.banner";
			if (targetColony.ColonyGroup != null && targetColony.ColonyGroup.Owners.ContainsByReference(causedBy)) {
				permission += ".self";
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, permission)) {
				return true;
			}
			Helper.TeleportPlayer(causedBy, targetBanner.Position.Vector);
			BccChat.Send(causedBy, $"Warped to {TextHelper.SafeName(targetColony.Name)}");
			return true;
		}
	}
}
