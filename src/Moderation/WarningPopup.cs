using System;
using System.Collections.Generic;
using System.Linq;
using NetworkUI;
using NetworkUI.Items;
using Pipliz;
using UnityEngine;

namespace ColonyCommands
{
	// A warning added in the player window is shown to the player as a popup. If the player is
	// offline, it is shown on the next join. It stays pending until the player confirms it with
	// "OK" (closing with X shows it again next time); the confirmation is written to the history.
	public static class WarningPopup
	{
		public const string MENU_ID = "bcc.warning";
		const string OK_BUTTON = "bcc.warning|ok";

		public static void ShowPending(Players.Player player)
		{
			if (player == null || player.ConnectionState != Players.EConnectionState.Connected) {
				return;
			}
			List<ModerationLog.ModerationEvent> pending = ModerationLog.PendingWarnings(player);
			if (pending.Count == 0) {
				return;
			}
			int width = 620;
			NetworkMenu menu = new NetworkMenu {
				Identifier = MENU_ID,
				Width = width,
				Height = System.Math.Min(560, 220 + pending.Count * 70),
				IsInteractive = true,
				ForceClosePopups = true
			};
			menu.LocalStorage.SetAs("header", "<b><color=#FF6B6B>Official warning</color></b>");
			int w = width - 60;
			menu.Items.Add(new Label(new LabelData($"<b><color=#FF6B6B><size=120%>{(pending.Count == 1 ? "You received a warning" : $"You received {pending.Count} warnings")}</size></color></b>", ELabelAlignment.MiddleCenter, 18, LabelData.ELocalizationType.None), 36));
			foreach (ModerationLog.ModerationEvent e in pending) {
				menu.Items.Add(new BackgroundColor(new Label(new LabelData(
					$"{TextHelper.SafeName(e.Details)}\n<color=#B0B0B0>{e.Time:yyyy-MM-dd HH:mm} - by {e.By}</color>",
					ELabelAlignment.MiddleLeft, 16, LabelData.ELocalizationType.None), -1), w, 50, 0f, 0f, 8f, 6f, new Color32(0, 0, 0, 40)));
			}
			menu.Items.Add(new Label(new LabelData("<color=#FFD166>Please follow the server rules. Repeated violations can lead to a mute, jail or ban.</color>", ELabelAlignment.MiddleCenter, 14, LabelData.ELocalizationType.None), -1));
			menu.Items.Add(new HorizontalRow(new List<(IItem, int)> {
				(new EmptySpace(30), (w - 200) / 2),
				(new ButtonCallback(OK_BUTTON, new LabelData("OK, understood", ELabelAlignment.MiddleCenter, 16, LabelData.ELocalizationType.None), 196, 36, ButtonCallback.EOnClickActions.ClosePopup), 200)
			}, 38));
			NetworkMenuManager.SendServerPopup(player, menu);
		}

		public static void OnPlayerConnectedLate(Players.Player player)
		{
			if (player == null || ModerationLog.PendingWarnings(player).Count == 0) {
				return;
			}
			// after the other join popups (test server notice, protection notice)
			ThreadManager.InvokeOnMainThread(() => ShowPending(player), 16.0);
		}

		public static void OnPlayerPushedNetworkUIButton(ButtonPressCallbackData data)
		{
			if (data.Player == null || data.ButtonIdentifier != OK_BUTTON) {
				return;
			}
			int count = ModerationLog.AcknowledgeWarnings(data.Player);
			if (count > 0) {
				ModerationLog.Add(data.Player, "Warning acknowledged", data.Player, $"{count} warning(s) confirmed with OK", false);
				Log.Write($"Better Colony Commands: {data.Player.Name} acknowledged {count} warning(s)");
			}
		}
	}
}
