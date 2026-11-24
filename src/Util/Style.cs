namespace ColonyCommands
{
	// One palette for chat and UI, matching the look of /pytoniahelp (Pytonia mods' ChatFormat).
	public static class Style
	{
		public const string CMD_COLOR = "#6495ED";
		public const string CMDREF_COLOR = "#ADD8E6";
		public const string HEADER_COLOR = "#FFD166";
		public const string PLAYER_COLOR = "#FFF7A0";
		public const string SUCCESS_COLOR = "#7CFC9E";
		public const string ERROR_COLOR = "#FF6B6B";
		public const string MUTED_COLOR = "#B0B0B0";
		public const string MOD_COLOR = "#E2C6FF";

		public static string Cmd(string text) => $"<color={CMD_COLOR}>{text}</color>";
		// lighter command color for the windows (their brown background is much lighter than the chat's)
		public const string UICMD_COLOR = "#A9CBFF";
		public static string UiCmd(string text) => $"<color={UICMD_COLOR}>{text}</color>";
		public static string CmdRef(string text) => $"<color={CMDREF_COLOR}>{text}</color>";
		public static string Header(string text) => $"<b><color={HEADER_COLOR}><size=115%>{text}</size></color></b>";
		public static string SubHeader(string text) => $"<b><color={MOD_COLOR}>{text}</color></b>";
		public static string Value(string text) => $"<b>{text}</b>";
		public static string Player(string text) => $"<color={PLAYER_COLOR}>{text}</color>";
		public static string Success(string text) => $"<color={SUCCESS_COLOR}>{text}</color>";
		public static string Error(string text) => $"<color={ERROR_COLOR}>{text}</color>";
		public static string Muted(string text) => $"<color={MUTED_COLOR}>{text}</color>";
	}
}
