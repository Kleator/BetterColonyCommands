using NetworkUI;
using UnityEngine;

namespace ColonyCommands
{
	// Balance as overlay counter (same base-game OverlayCounterManager the Pytonia Automation HUD uses).
	// Only active while Better Colony Commands is the economy backend - with Pytonia Automation
	// installed, its own HUD shows the (shared) balance instead.
	public static class EconomyHud
	{
		static OverlayCounterManager.CounterID? counterID;

		static bool Active => counterID != null && EconomyManager.IsEnabled && !EconomyManager.PytoniaHandlesCommands
			&& BccSettingsManager.Settings.Economy.ShowHud;

		public static void Initialize()
		{
			if (counterID != null || !BccSettingsManager.Settings.Economy.ShowHud || EconomyManager.PytoniaHandlesCommands) {
				return;
			}
			try {
				string labelText = $"<color=#FFD700>{EconomyManager.CurrencyName}</color>";
				LabelData label = new LabelData(labelText, Color.white, ELabelAlignment.Default, 16, LabelData.ELocalizationType.None);
				counterID = OverlayCounterManager.RegisterCounter(label, 110);
			} catch (System.Exception e) {
				Pipliz.Log.WriteWarning($"Better Colony Commands: could not register economy HUD counter: {e.Message}");
			}
		}

		public static void UpdatePlayer(Players.Player player)
		{
			if (!Active || player == null) {
				return;
			}
			try {
				long balance = EconomyManager.GetBalance(player);
				OverlayCounterManager.SetCounterValue(counterID.Value, player, (int)System.Math.Min(balance, int.MaxValue));
			} catch {
			}
		}

		public static void OnBalanceChanged(Players.Player player, long delta)
		{
			UpdatePlayer(player);
		}
	}
}
