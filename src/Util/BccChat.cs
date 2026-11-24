using System.Collections.Generic;
using Chatting;

namespace ColonyCommands
{
	// Every chat line Better Colony Commands sends to a single player goes through here instead of
	// Chat.Send directly. While a capture is active for that player (a command started from the UI,
	// or a "window" command typed in chat), the lines are collected instead of sent, so they can be
	// shown in an output window afterwards. Lines to anybody else are always sent normally.
	public static class BccChat
	{
		class Capture
		{
			public List<string> Lines = new List<string>();
			public int Depth;
		}

		static readonly Dictionary<Players.Player, Capture> captures = new Dictionary<Players.Player, Capture>();

		public static void Send(Players.Player player, string text)
		{
			if (player == null) {
				return;
			}
			if (captures.TryGetValue(player, out Capture capture)) {
				if (text != null) {
					foreach (string line in text.Split('\n')) {
						capture.Lines.Add(line);
					}
				}
				return;
			}
			Chat.Send(player, text);
		}

		public static void Send(Players.Player player, string text, EChatSendOptions options)
		{
			if (player != null && captures.ContainsKey(player)) {
				Send(player, text);
				return;
			}
			Chat.Send(player, text, options);
		}

		public static bool IsCapturing(Players.Player player)
		{
			return player != null && captures.ContainsKey(player);
		}

		public static void BeginCapture(Players.Player player)
		{
			if (!captures.TryGetValue(player, out Capture capture)) {
				capture = new Capture();
				captures[player] = capture;
			}
			capture.Depth++;
		}

		// returns the captured lines once the outermost capture ends, otherwise null
		public static List<string> EndCapture(Players.Player player)
		{
			if (!captures.TryGetValue(player, out Capture capture)) {
				return new List<string>();
			}
			capture.Depth--;
			if (capture.Depth > 0) {
				return null;
			}
			captures.Remove(player);
			return capture.Lines;
		}
	}
}
