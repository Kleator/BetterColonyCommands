using System.Collections.Generic;
using System.Text.RegularExpressions;
using Pipliz;
using Chatting;
using Chatting.Commands;
using BlockEntities.Implementations;

namespace ColonyCommands
{
	// lets admins rename any colony (found via the banner nearest to them), bypassing the
	// game's own owner-only check in ClientCommands.SetColonyName
	public class SetColonyNameChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/setcolonyname")) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "setcolonyname")) {
				return true;
			}

			var m = Regex.Match(chattext, @"/setcolonyname (?<name>.+)");
			if (!m.Success) {
				Chat.Send(causedBy, "Syntax: /setcolonyname {new name}");
				return true;
			}
			string newName = m.Groups["name"].Value.Trim();
			if (newName.Length == 0) {
				Chat.Send(causedBy, "Syntax: /setcolonyname {new name}");
				return true;
			}
			if (newName.Length > ColonyNameGuard.MaxNameLength) {
				newName = newName.Substring(0, ColonyNameGuard.MaxNameLength);
			}

			BannerTracker.Banner closestBanner = null;
			int shortestDistance = int.MaxValue;
			Pipliz.Collections.Hashmap<ColonyGroupID, ColonyGroup>.ValueEnumerator colonyGroupEnum = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
			while (colonyGroupEnum.MoveNext()) {
				ColonyGroup checkColonyGroup = colonyGroupEnum.Current;
				for (int i = 0; i < checkColonyGroup.Colonies.Count; ++i) {
					Colony checkColony = checkColonyGroup.Colonies.GetAt(i);
					for (int j = 0; j < checkColony.Banners.Count; ++j) {
						BannerTracker.Banner checkBanner = checkColony.Banners.GetAt(j);
						int distX = (int)(causedBy.PositionStanding.x - checkBanner.Position.x);
						int distZ = (int)(causedBy.PositionStanding.z - checkBanner.Position.z);
						int distance = (int)System.Math.Sqrt(System.Math.Pow(distX, 2) + System.Math.Pow(distZ, 2));
						if (distance < shortestDistance) {
							shortestDistance = distance;
							closestBanner = checkBanner;
						}
					}
				}
			}

			if (closestBanner == null || closestBanner.Colony == null) {
				Chat.Send(causedBy, "No colony banner found nearby");
				return true;
			}

			Colony colony = closestBanner.Colony;
			string oldName = colony.Name;
			colony.Name = newName;
			ColonyGroup colonyGroup = colony.ColonyGroup;
			for (int i = 0; i < colonyGroup.Owners.Count; i++) {
				colony.SendNameUpdate(colonyGroup.Owners.GetAt(i));
			}
			ColonyNameGuard.RegisterKnownGoodName(colony.ColonyID, newName);

			Chat.Send(causedBy, $"Renamed colony '{oldName}' to '{newName}'");
			Log.Write($"{causedBy.Name} renamed colony '{oldName}' to '{newName}' ({shortestDistance} blocks away)");
			return true;
		}
	}
}
