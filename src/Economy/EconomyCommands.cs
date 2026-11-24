using System;
using System.Collections.Generic;
using Chatting;

namespace ColonyCommands
{
	// /money, /pay, /baltop, /exchange, /addmoney, /removemoney, /setmoney
	// While Pytonia Automation is installed it answers /money, /addmoney, /setmoney and /removemoney
	// itself - our versions step aside (return false) so the player never gets two answers.
	public class EconomyChatCommand : IChatCommand
	{
		static readonly HashSet<string> pytoniaOwned = new HashSet<string> { "/money", "/addmoney", "/setmoney", "/removemoney" };

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0) {
				return false;
			}
			string cmd = splits[0].ToLowerInvariant();
			switch (cmd) {
				case "/money":
				case "/pay":
				case "/baltop":
				case "/exchange":
				case "/addmoney":
				case "/removemoney":
				case "/setmoney":
					break;
				default:
					return false;
			}
			if (pytoniaOwned.Contains(cmd) && EconomyManager.PytoniaHandlesCommands) {
				return false;
			}
			if (!EconomyManager.IsEnabled) {
				BccChat.Send(causedBy, Style.Error("The economy module is disabled on this server."));
				return true;
			}
			switch (cmd) {
				case "/money": Money(causedBy, chattext, splits); break;
				case "/pay": Pay(causedBy, splits); break;
				case "/baltop": BalTop(causedBy, splits); break;
				case "/exchange": Exchange(causedBy, splits); break;
				default: Admin(causedBy, cmd, splits); break;
			}
			return true;
		}

		static void Money(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count >= 2) {
				string name = chattext.Substring(chattext.IndexOf(' ') + 1).Trim();
				if (!PlayerHelper.TryGetPlayer(name, out Players.Player target, out string error, true)) {
					BccChat.Send(causedBy, Style.Error($"Could not find player '{name}': {error}"));
					return;
				}
				BccChat.Send(causedBy, $"{Style.Player(ChatColors.GetDisplayName(target))} has {Style.Value(EconomyManager.Format(EconomyManager.GetBalance(target)))}");
				return;
			}
			BccChat.Send(causedBy, $"{Style.Header("Your balance")}: {Style.Value(EconomyManager.Format(EconomyManager.GetBalance(causedBy)))}");
			if (TraderBridge.IsInstalled && causedBy.ActiveColonyGroup != null) {
				BccChat.Send(causedBy, $"Trader Coins in your colony stockpile: {Style.Value(TraderBridge.GetCoins(causedBy.ActiveColonyGroup).ToString("N0"))}");
			}
		}

		static void Pay(Players.Player causedBy, List<string> splits)
		{
			if (!BccSettingsManager.Settings.Economy.AllowPay) {
				BccChat.Send(causedBy, Style.Error("/pay is disabled on this server."));
				return;
			}
			if (splits.Count < 3 || !long.TryParse(splits[splits.Count - 1], out long amount) || amount <= 0) {
				BccChat.Send(causedBy, Style.Error($"Syntax: {Style.CmdRef("/pay {player} {amount}")} - amount must be a positive whole number"));
				return;
			}
			string name = string.Join(" ", splits.GetRange(1, splits.Count - 2));
			if (!PlayerHelper.TryGetPlayer(name, out Players.Player target, out string error, true)) {
				BccChat.Send(causedBy, Style.Error($"Could not find player '{name}': {error}"));
				return;
			}
			if (target == causedBy) {
				BccChat.Send(causedBy, Style.Error("You can't pay yourself."));
				return;
			}
			if (!EconomyManager.TryTransfer(causedBy, target, amount, $"/pay {causedBy.Name} -> {target.Name}")) {
				BccChat.Send(causedBy, Style.Error($"Not enough money. Your balance: {EconomyManager.Format(EconomyManager.GetBalance(causedBy))}"));
				return;
			}
			BccChat.Send(causedBy, Style.Success($"Sent {EconomyManager.Format(amount)} to {target.Name}. New balance: {EconomyManager.Format(EconomyManager.GetBalance(causedBy))}"));
			BccChat.Send(target, Style.Success($"{causedBy.Name} sent you {EconomyManager.Format(amount)}."));
		}

		static void BalTop(Players.Player causedBy, List<string> splits)
		{
			int page = 1;
			if (splits.Count >= 2) {
				int.TryParse(splits[1], out page);
			}
			List<KeyValuePair<string, long>> top = EconomyManager.GetTop();
			List<KeyValuePair<string, long>> entries = TextHelper.Page(top, page, 10, out int p, out int pages);
			BccChat.Send(causedBy, Style.Header($"Richest players (page {p}/{pages})"));
			int rank = (p - 1) * 10;
			foreach (KeyValuePair<string, long> kvp in entries) {
				rank++;
				Players.Player who = EconomyManager.PlayerOf(kvp.Key);
				string shown = who != null ? ChatColors.GetDisplayName(who) : Style.Player(TextHelper.SafeName(EconomyManager.NameOf(kvp.Key)));
				BccChat.Send(causedBy, $"{rank}. {shown} - {Style.Value(EconomyManager.Format(kvp.Value))}");
			}
		}

		static void Exchange(Players.Player causedBy, List<string> splits)
		{
			BccSettings.EconomySettings cfg = BccSettingsManager.Settings.Economy;
			if (!TraderBridge.IsInstalled) {
				BccChat.Send(causedBy, Style.Error("THE TRADER is not installed - there are no Trader Coins to exchange."));
				return;
			}
			if (!cfg.TraderExchangeEnabled || cfg.TraderCoinValue <= 0) {
				BccChat.Send(causedBy, Style.Error("Exchanging Trader Coins is disabled on this server."));
				return;
			}
			if (splits.Count < 3 || !long.TryParse(splits[2], out long amount) || amount <= 0 || amount > int.MaxValue) {
				BccChat.Send(causedBy, Style.Error($"Syntax: {Style.CmdRef("/exchange {tomoney|tocoins} {amount}")} (1 Trader Coin = {EconomyManager.Format(cfg.TraderCoinValue)})"));
				return;
			}
			ColonyGroup group = causedBy.ActiveColonyGroup;
			if (group == null) {
				BccChat.Send(causedBy, Style.Error("You need an active colony - the coins are taken from/put into its stockpile."));
				return;
			}
			string direction = splits[1].ToLowerInvariant();
			if (direction == "tomoney" || direction == "money") {
				if (!TraderBridge.TryRemoveCoins(group, (int)amount)) {
					BccChat.Send(causedBy, Style.Error($"Your stockpile only has {TraderBridge.GetCoins(group):N0} Trader Coins."));
					return;
				}
				EconomyManager.TryAdd(causedBy, amount * cfg.TraderCoinValue, $"/exchange {amount} Trader Coins");
				BccChat.Send(causedBy, Style.Success($"Exchanged {amount:N0} Trader Coins into {EconomyManager.Format(amount * cfg.TraderCoinValue)}."));
			} else if (direction == "tocoins" || direction == "coins") {
				long cost = amount * cfg.TraderCoinValue;
				if (!EconomyManager.TryRemove(causedBy, cost, $"/exchange to {amount} Trader Coins")) {
					BccChat.Send(causedBy, Style.Error($"You need {EconomyManager.Format(cost)}."));
					return;
				}
				TraderBridge.AddCoins(group, (int)amount);
				BccChat.Send(causedBy, Style.Success($"Exchanged {EconomyManager.Format(cost)} into {amount:N0} Trader Coins (added to your colony stockpile)."));
			} else {
				BccChat.Send(causedBy, Style.Error($"Syntax: {Style.CmdRef("/exchange {tomoney|tocoins} {amount}")}"));
				return;
			}
			TraderBridge.Snapshot();
		}

		static void Admin(Players.Player causedBy, string cmd, List<string> splits)
		{
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, EconomyManager.PERMISSION_ADMIN)) {
				return;
			}
			if (splits.Count < 3 || !long.TryParse(splits[splits.Count - 1], out long amount) || amount < 0 || (amount == 0 && cmd != "/setmoney")) {
				BccChat.Send(causedBy, Style.Error($"Syntax: {Style.CmdRef(cmd + " {player} {amount}")}"));
				return;
			}
			string name = string.Join(" ", splits.GetRange(1, splits.Count - 2));
			if (!PlayerHelper.TryGetPlayer(name, out Players.Player target, out string error, true)) {
				BccChat.Send(causedBy, Style.Error($"Could not find player '{name}': {error}"));
				return;
			}
			bool ok;
			string reason = $"{cmd} by {causedBy.Name}";
			if (cmd == "/addmoney") {
				ok = EconomyManager.TryAdd(target, amount, reason);
			} else if (cmd == "/removemoney") {
				ok = EconomyManager.TryRemove(target, amount, reason);
			} else {
				ok = EconomyManager.TrySet(target, amount, reason);
			}
			if (!ok) {
				BccChat.Send(causedBy, Style.Error($"Not possible - {target.Name} has {EconomyManager.Format(EconomyManager.GetBalance(target))}."));
				return;
			}
			BccChat.Send(causedBy, Style.Success($"{target.Name}'s new balance: {EconomyManager.Format(EconomyManager.GetBalance(target))}"));
		}
	}
}
