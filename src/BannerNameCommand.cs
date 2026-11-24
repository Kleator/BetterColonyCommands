using System.Collections.Generic;
using Pipliz;
using Chatting;
using Chatting.Commands;
using BlockEntities.Implementations;

namespace ColonyCommands
{
	public class BannerNameChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/bannername")) {
				return false;
			}
			BannerTracker.Banner closestBanner = null;
			int shortestDistance = int.MaxValue;
			Pipliz.Collections.Hashmap<ColonyGroupID, ColonyGroup>.ValueEnumerator colonyGroupEnum = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
			while (colonyGroupEnum.MoveNext()) {
				ColonyGroup checkColonyGroup = colonyGroupEnum.Current;
				if (checkColonyGroup.Owners.IndexOf(causedBy) >= 0) {
					continue;
				}

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

			if (closestBanner != null) {
				string owners = "";
				string name = "";
				if (closestBanner.Colony == null || closestBanner.Colony.Name == null) {
					name = "(invalid)";
					owners = "(invalid)";
				} else {
					name = TextHelper.SafeName(closestBanner.Colony.Name);
				}

				if (closestBanner.Colony != null && closestBanner.Colony.ColonyGroup.Owners.Count > 0) {
					for (int i = 0; i < closestBanner.Colony.ColonyGroup.Owners.Count; ++i) {
						Players.Player owner = closestBanner.Colony.ColonyGroup.Owners.GetAt(i);
						if (!owners.Equals("")) {
							owners += ", ";
						}
						owners += ChatColors.GetDisplayName(owner) + Style.Muted(" (" + LastSeenText(owner) + ")");
					}
				}
				BccChat.Send(causedBy, $"Closest banner is at {closestBanner.Position.x},{closestBanner.Position.z}. {shortestDistance} blocks away. It belongs to colony {name} owned by {owners}");
			}
			return true;
		}

		static string LastSeenText(Players.Player owner)
		{
			if (owner.ConnectionState == Players.EConnectionState.Connected) {
				return "online now";
			}
			string lastSeen = ActivityTracker.GetLastSeen(owner.ID.ToStringReadable());
			if (System.DateTime.TryParse(lastSeen, out System.DateTime t)) {
				double days = (System.DateTime.Now - t).TotalDays;
				string ago = days >= 1 ? $"{(int)days} day(s) ago" : $"{(int)((System.DateTime.Now - t).TotalHours)} hour(s) ago";
				return $"last seen {t:yyyy-MM-dd HH:mm}, {ago}";
			}
			return "last seen: " + lastSeen;
		}
	}
}
