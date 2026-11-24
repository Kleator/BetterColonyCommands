using System.Collections.Generic;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{
	public class TpAcceptChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/tpaccept")) {
				return false;
			}

			string error;
			if (!TpaManager.TryAccept(causedBy, out error)) {
				BccChat.Send(causedBy, $"<color=red>{error}</color>");
			}
			return true;
		}
	}
}
