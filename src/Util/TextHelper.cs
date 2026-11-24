using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ColonyCommands
{
	// Rich text helpers. The client renders chat and NetworkUI labels through TextMeshPro, so
	// <b>, <i>, <u>, <color=...>, <size=...> etc. all work. Player-chosen names (colonies, banners,
	// player names) may contain such tags; these helpers keep them readable and matchable.
	public static class TextHelper
	{
		// only real TextMeshPro tags - placeholders like "<player>" in help texts must survive
		static readonly Regex AnyTag = new Regex(@"<\/?(b|i|u|s|color|size|sup|sub|mark|font|material|alpha|align|cspace|indent|line-height|line-indent|link|lowercase|uppercase|smallcaps|allcaps|margin|mspace|noparse|nobr|page|pos|rotate|space|sprite|strikethrough|style|voffset|width|gradient|br)(=[^<>]*)?>|<#[0-9a-fA-F]{3,8}>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		static readonly Regex SizeTag = new Regex(@"<\/?size(=[^<>]*)?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		static readonly Regex LayoutTags = new Regex(@"<\/?(size|voffset|line-height|cspace|mspace|indent|margin|pos|space|width|rotate|scale)(=[^<>]*)?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		// "<b><size=25><color=#EB34C9>✮Highrise-</color></size></b>" -> "✮Highrise-"
		public static string StripRichText(string text)
		{
			if (string.IsNullOrEmpty(text)) {
				return text ?? "";
			}
			return AnyTag.Replace(text, "");
		}

		// Removes only <size=..> / </size> so a player-chosen name can never be rendered huge,
		// while colors/bold etc. stay intact.
		public static string StripSize(string text)
		{
			if (string.IsNullOrEmpty(text)) {
				return text ?? "";
			}
			return SizeTag.Replace(text, "");
		}

		// Display-safe version of any player-defined name (colony, banner, player): keeps colors and
		// styles but drops size and other layout-breaking tags, and never returns null.
		public static string SafeName(string text)
		{
			if (string.IsNullOrEmpty(text)) {
				return text ?? "";
			}
			return LayoutTags.Replace(text, "");
		}

		// Canonical form for name matching: rich text removed, only letters and digits kept, lower case.
		// "<b>✮Highrise-City✮3</b>" and "Highrise-City3" and "highrise city 3" all become "highrisecity3".
		public static string NormalizeName(string text)
		{
			string plain = StripRichText(text);
			StringBuilder sb = new StringBuilder(plain.Length);
			foreach (char c in plain) {
				if (char.IsLetterOrDigit(c)) {
					sb.Append(char.ToLowerInvariant(c));
				}
			}
			return sb.ToString();
		}

		// Visible text with symbols kept but tags removed and whitespace collapsed, lower case.
		public static string PlainLower(string text)
		{
			return Regex.Replace(StripRichText(text), @"\s+", " ").Trim().ToLowerInvariant();
		}

		// Escapes text so TextMeshPro shows it literally (used for raw config values in the UI).
		public static string EscapeTags(string text)
		{
			if (string.IsNullOrEmpty(text)) {
				return text ?? "";
			}
			return text.Replace("<", "<noparse><</noparse>");
		}

		public static string Truncate(string text, int maxVisibleChars)
		{
			if (string.IsNullOrEmpty(text)) {
				return text ?? "";
			}
			string plain = StripRichText(text);
			if (plain.Length <= maxVisibleChars) {
				return text;
			}
			return plain.Substring(0, System.Math.Max(0, maxVisibleChars - 3)) + "...";
		}

		public static string FormatDuration(double totalSeconds)
		{
			if (totalSeconds < 0) {
				totalSeconds = 0;
			}
			long secs = (long)totalSeconds;
			long days = secs / 86400;
			long hours = (secs % 86400) / 3600;
			long minutes = (secs % 3600) / 60;
			if (days > 0) {
				return $"{days}d {hours}h {minutes}m";
			}
			if (hours > 0) {
				return $"{hours}h {minutes}m";
			}
			return $"{minutes}m";
		}

		// splits a list into pages; page is 1-based and clamped into range
		public static List<T> Page<T>(IList<T> items, int page, int pageSize, out int clampedPage, out int pageCount)
		{
			pageCount = System.Math.Max(1, (items.Count + pageSize - 1) / pageSize);
			clampedPage = System.Math.Min(System.Math.Max(1, page), pageCount);
			List<T> result = new List<T>();
			int start = (clampedPage - 1) * pageSize;
			for (int i = start; i < items.Count && i < start + pageSize; i++) {
				result.Add(items[i]);
			}
			return result;
		}
	}
}
