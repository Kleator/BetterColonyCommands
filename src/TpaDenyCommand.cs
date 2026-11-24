using System.Collections.Generic;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{
	public class TpaDenyChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/tpadeny")) {
				return false;
			}

			string error;
			if (!TpaManager.TryDeny(causedBy, out error)) {
				BccChat.Send(causedBy, $"<color=red>{error}</color>");
			}
			return true;
		}
	}
}
