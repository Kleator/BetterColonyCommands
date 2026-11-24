using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chatting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pipliz;
using Shared;

namespace ColonyCommands
{
	// Collects what /modlist shows from every loaded mod's modInfo.json entry (see
	// Dev\_Wissensbasis\05_Modinfo-Standard.md):
	//   name, version, description                      - standard game keys
	//   author, homepage, workshopid                    - optional
	//   longdescription  (string or array of strings)   - optional, shown on the detail page
	//   previewimage     (path relative to mod folder)  - optional, else preview.png/.jpg, icon.png, thumbnail.png, logo.png
	//   patchnotes       (BCC style [{version, notes[]}]) - optional, newest entry shown
	//   bcc_hidden: true                                - hide the mod from /modlist
	public static class ModInfoCollector
	{
		public class ModEntry
		{
			public string Name = "";
			public string Version = "";
			public string Description = "";
			public string Author = "";
			public string Homepage = "";
			public string WorkshopId = "";
			public List<string> LongDescription = new List<string>();
			public List<string> Compatible = new List<string>();
			public string LatestNotesVersion = "";
			public List<string> LatestNotes = new List<string>();
			public string Folder = "";
			public string ImagePath = "";
			public FileTable.FileID ImageFile;
			public bool Hidden;
			public bool IsBaseGame;
			public DateTime LastUpdated = DateTime.MinValue;
			public List<ChangelogEntry> Changelog = new List<ChangelogEntry>();
		}

		public class ChangelogEntry
		{
			public string Version = "";
			public string Date = "";
			public List<string> Notes = new List<string>();
		}

		// parts of the base game the mod loader reports like mods
		static readonly HashSet<string> BaseGameNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"Colony Survival", "Colony Survival Localization", "pipliz.game"
		};

		static readonly string[] ImageCandidates = {
			"preview.png", "preview.jpg", "preview.jpeg", "icon.png", "thumbnail.png", "logo.png", "previewimage.png"
		};

		static List<ModEntry> mods = new List<ModEntry>();

		public static void Collect(List<ModLoader.ModDescription> loaded)
		{
			List<ModEntry> result = new List<ModEntry>();
			Dictionary<string, string> folderByName = ScanModFolders();
			foreach (ModLoader.ModDescription d in loaded) {
				if (d == null || string.IsNullOrEmpty(d.name)) {
					continue;
				}
				try {
					result.Add(FromDescription(d, folderByName));
				} catch (Exception e) {
					Log.WriteWarning($"Better Colony Commands: could not read mod info of '{d.name}': {e.Message}");
				}
			}
			mods = result.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
		}

		// modInfo.json files of installed mods: gamedata/mods/*/ and the Steam Workshop folder
		static Dictionary<string, string> ScanModFolders()
		{
			Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			List<string> roots = new List<string> {
				Path.Combine("gamedata", "mods"),
				Path.Combine("..", "..", "workshop", "content", "366090")
			};
			foreach (string root in roots) {
				try {
					if (!Directory.Exists(root)) {
						continue;
					}
					foreach (string dir in Directory.GetDirectories(root)) {
						string info = Path.Combine(dir, "modInfo.json");
						if (!File.Exists(info)) {
							continue;
						}
						try {
							JToken token = JToken.Parse(File.ReadAllText(info));
							IEnumerable<JToken> entries = token is JArray arr ? arr : (IEnumerable<JToken>)new[] { token };
							foreach (JToken e in entries) {
								string name = e["name"]?.Value<string>();
								if (!string.IsNullOrEmpty(name) && !result.ContainsKey(name)) {
									result[name] = Path.GetFullPath(dir);
								}
							}
						} catch {
						}
					}
				} catch {
				}
			}
			return result;
		}

		static ModEntry FromDescription(ModLoader.ModDescription d, Dictionary<string, string> folderByName)
		{
			ModEntry m = new ModEntry { Name = d.name, Version = d.version.ToString() };
			JObject json = d.customJSON;
			if (!string.IsNullOrEmpty(d.assemblyPath)) {
				m.Folder = Path.GetDirectoryName(Path.GetFullPath(d.assemblyPath));
			} else if (folderByName.TryGetValue(d.name, out string folder)) {
				m.Folder = folder;
			}
			if (!string.IsNullOrEmpty(m.Folder)) {
				System.Text.RegularExpressions.Match wm = System.Text.RegularExpressions.Regex.Match(m.Folder, @"366090[\\/](\d+)");
				if (wm.Success) {
					m.WorkshopId = wm.Groups[1].Value;
				}
			}
			// ModDescription.customJSON does not contain the standard keys (description, ...), so the
			// mod's own modInfo.json entry is read from its folder and merged over it
			JObject fileJson = ReadOwnEntry(m.Folder, d.name);
			if (fileJson != null) {
				if (json == null) {
					json = fileJson;
				} else {
					json = (JObject)json.DeepClone();
					json.Merge(fileJson, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
				}
			}
			m.IsBaseGame = BaseGameNames.Contains(d.name);
			if (json != null) {
				m.Description = Str(json, "description");
				m.Author = Str(json, "author");
				m.Homepage = Str(json, "homepage") ?? Str(json, "url");
				m.WorkshopId = Str(json, "workshopid") ?? m.WorkshopId;
				m.Hidden = json["bcc_hidden"]?.Type == JTokenType.Boolean && json["bcc_hidden"].Value<bool>();
				JToken longDesc = json["longdescription"];
				if (longDesc is JArray la) {
					m.LongDescription = la.Select(t => t.ToString()).ToList();
				} else if (longDesc != null && longDesc.Type == JTokenType.String) {
					m.LongDescription = longDesc.Value<string>().Split('\n').ToList();
				}
				if (json["compatibleversions"] is JArray cv) {
					m.Compatible = cv.Select(t => t.ToString()).ToList();
				}
				if (json["patchnotes"] is JArray notes) {
					foreach (JObject entry in notes.OfType<JObject>()) {
						ChangelogEntry ce = new ChangelogEntry { Version = entry["version"]?.ToString() ?? "", Date = entry["date"]?.ToString() ?? "" };
						if (entry["notes"] is JArray n) {
							ce.Notes = n.Select(t => t.ToString()).ToList();
						}
						m.Changelog.Add(ce);
					}
					if (m.Changelog.Count > 0) {
						m.LatestNotesVersion = m.Changelog[0].Version;
						m.LatestNotes = m.Changelog[0].Notes;
					}
				}
				string img = Str(json, "previewimage") ?? Str(json, "preview") ?? Str(json, "icon");
				if (!string.IsNullOrEmpty(img) && !string.IsNullOrEmpty(m.Folder)) {
					string full = Path.GetFullPath(Path.Combine(m.Folder, img));
					// stay inside the mod folder
					if (full.StartsWith(m.Folder, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) {
						m.ImagePath = full;
					}
				}
			}
			if (string.IsNullOrEmpty(m.ImagePath) && !string.IsNullOrEmpty(m.Folder) && Directory.Exists(m.Folder) && !m.IsBaseGame) {
				foreach (string c in ImageCandidates) {
					string full = Path.Combine(m.Folder, c);
					if (File.Exists(full)) {
						m.ImagePath = full;
						break;
					}
				}
			}
			if (string.IsNullOrEmpty(m.ImagePath) && !string.IsNullOrEmpty(m.Folder) && Directory.Exists(m.Folder) && !m.IsBaseGame) {
				m.ImagePath = GuessImage(m.Folder);
			}
			if (!string.IsNullOrEmpty(m.Folder) && Directory.Exists(m.Folder) && !m.IsBaseGame) {
				// "last updated on this server" = newest file in the mod folder (top level)
				try {
					foreach (string f in Directory.GetFiles(m.Folder)) {
						DateTime t = File.GetLastWriteTime(f);
						if (t > m.LastUpdated) {
							m.LastUpdated = t;
						}
					}
				} catch {
				}
				if (m.Changelog.Count == 0) {
					m.Changelog = ParseChangelogFile(Path.Combine(m.Folder, "CHANGELOG.md"));
				}
			}
			return m;
		}

		// "## [1.0.6] - 2026-08-22" / "## 1.1.0" headings, "### Fixed" sub headings and "- " bullets
		// (continuation lines are joined). Markdown emphasis and code marks are removed.
		public static List<ChangelogEntry> ParseChangelogFile(string path)
		{
			List<ChangelogEntry> result = new List<ChangelogEntry>();
			if (!File.Exists(path)) {
				return result;
			}
			try {
				ChangelogEntry current = null;
				string section = "";
				System.Text.StringBuilder bullet = null;
				void Flush()
				{
					if (current != null && bullet != null && bullet.Length > 0) {
						current.Notes.Add((section.Length > 0 ? section + ": " : "") + bullet.ToString().Trim());
					}
					bullet = null;
				}
				foreach (string raw in File.ReadAllLines(path)) {
					string line = raw.TrimEnd();
					System.Text.RegularExpressions.Match h = System.Text.RegularExpressions.Regex.Match(line, @"^##\s+\[?v?(?<v>[0-9][0-9A-Za-z.\-]*)\]?\s*(?:[-–]\s*(?<d>.+))?$");
					if (h.Success) {
						Flush();
						current = new ChangelogEntry { Version = h.Groups["v"].Value, Date = h.Groups["d"].Success ? h.Groups["d"].Value.Trim() : "" };
						result.Add(current);
						section = "";
						continue;
					}
					if (current == null) {
						continue;
					}
					if (line.StartsWith("### ")) {
						Flush();
						section = line.Substring(4).Trim();
						continue;
					}
					if (line.StartsWith("- ") || line.StartsWith("* ")) {
						Flush();
						bullet = new System.Text.StringBuilder(CleanMarkdown(line.Substring(2)));
						continue;
					}
					if (bullet != null && line.StartsWith(" ") && line.Trim().Length > 0) {
						bullet.Append(' ').Append(CleanMarkdown(line.Trim()));
						continue;
					}
					if (line.Trim().Length == 0) {
						Flush();
					}
				}
				Flush();
			} catch (Exception e) {
				Log.WriteWarning($"Better Colony Commands: could not read {path}: {e.Message}");
			}
			return result;
		}

		static string CleanMarkdown(string text)
		{
			return text.Replace("**", "").Replace("`", "").Replace("__", "");
		}

		// any png/jpg in the mod's root folder that looks like a preview, square ones ("600x600") first
		static string GuessImage(string folder)
		{
			try {
				string[] keywords = { "thumbnail", "preview", "logo", "icon", "banner" };
				List<string> files = Directory.GetFiles(folder)
					.Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
					.Where(f => keywords.Any(k => Path.GetFileName(f).ToLowerInvariant().Contains(k)))
					.ToList();
				return files
					.OrderBy(f => Path.GetFileName(f).ToLowerInvariant().Contains("600x600") || Path.GetFileName(f).ToLowerInvariant().Contains("thumbnail") ? 0 : 1)
					.ThenBy(f => Path.GetFileName(f).ToLowerInvariant().Contains("banner") ? 1 : 0)
					.ThenBy(f => new FileInfo(f).Length)
					.FirstOrDefault() ?? "";
			} catch {
				return "";
			}
		}

		static JObject ReadOwnEntry(string folder, string name)
		{
			if (string.IsNullOrEmpty(folder)) {
				return null;
			}
			string path = Path.Combine(folder, "modInfo.json");
			if (!File.Exists(path)) {
				return null;
			}
			try {
				JToken token = JToken.Parse(File.ReadAllText(path));
				IEnumerable<JToken> entries = token is JArray arr ? arr : (IEnumerable<JToken>)new[] { token };
				return entries.OfType<JObject>().FirstOrDefault(e => string.Equals(e["name"]?.ToString(), name, StringComparison.OrdinalIgnoreCase));
			} catch {
				return null;
			}
		}

		static string Str(JObject json, string key)
		{
			JToken t = json[key];
			if (t == null || t.Type == JTokenType.Null) {
				return null;
			}
			string s = t.Type == JTokenType.String ? t.Value<string>() : t.ToString();
			return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
		}

		// registers the preview images with the game's file table so clients download them once
		public static void RegisterImages()
		{
			BccSettings.ModListSettings cfg = BccSettingsManager.Settings.ModList;
			if (!cfg.ShowImages || ServerManager.FileTable == null) {
				return;
			}
			int registered = 0;
			foreach (ModEntry m in mods) {
				if (m.ImageFile.IsValid || string.IsNullOrEmpty(m.ImagePath) || m.Hidden || IsHiddenByConfig(m)) {
					continue;
				}
				try {
					long size = new FileInfo(m.ImagePath).Length;
					if (size > cfg.MaxImageKilobytes * 1024L) {
						Log.Write($"Better Colony Commands: preview image of '{m.Name}' skipped ({size / 1024} KB > MaxImageKilobytes {cfg.MaxImageKilobytes})");
						continue;
					}
					m.ImageFile = ServerManager.FileTable.StartLoading(m.ImagePath, ECachedFileType.Image, true);
					if (m.ImageFile.IsValid) {
						registered++;
					}
				} catch (Exception e) {
					Log.WriteWarning($"Better Colony Commands: could not register preview image of '{m.Name}': {e.Message}");
				}
			}
			Log.Write($"Better Colony Commands: {mods.Count} mods listed, {registered} preview image(s) registered");
		}

		static bool IsHiddenByConfig(ModEntry m)
		{
			return BccSettingsManager.Settings.ModList.HiddenMods.Any(h => string.Equals(h, m.Name, StringComparison.OrdinalIgnoreCase));
		}

		public static List<ModEntry> GetVisible(string filter = null, bool alphabetical = false)
		{
			List<ModEntry> list = GetVisibleUnsorted(filter);
			if (alphabetical) {
				return list.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
			}
			// default: most recently updated mod first
			return list.OrderByDescending(m => m.LastUpdated).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
		}

		static List<ModEntry> GetVisibleUnsorted(string filter)
		{
			bool showBase = BccSettingsManager.Settings.ModList.ShowBaseGame;
			IEnumerable<ModEntry> q = mods.Where(m => !m.Hidden && !IsHiddenByConfig(m) && (showBase || !m.IsBaseGame));
			if (!string.IsNullOrEmpty(filter)) {
				string f = filter.ToLowerInvariant();
				q = q.Where(m => m.Name.ToLowerInvariant().Contains(f) || (m.Description ?? "").ToLowerInvariant().Contains(f) || (m.Author ?? "").ToLowerInvariant().Contains(f));
			}
			return q.ToList();
		}

		public static ModEntry Find(string name)
		{
			List<ModEntry> visible = GetVisible();
			ModEntry exact = visible.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
			if (exact != null) {
				return exact;
			}
			string norm = TextHelper.NormalizeName(name);
			return visible.FirstOrDefault(m => TextHelper.NormalizeName(m.Name) == norm)
				?? visible.FirstOrDefault(m => norm.Length > 2 && TextHelper.NormalizeName(m.Name).Contains(norm));
		}
	}

	// /modlist [page|modname]
	public class ModListChatCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/modlist", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			if (!BccSettingsManager.Settings.ModList.Enabled) {
				BccChat.Send(causedBy, Style.Error("/modlist is disabled on this server."));
				return true;
			}
			string arg = splits.Count > 1 ? chattext.Substring(chattext.IndexOf(' ') + 1).Trim() : "";
			bool az = false;
			if (arg.Equals("az", StringComparison.OrdinalIgnoreCase) || arg.EndsWith(" az", StringComparison.OrdinalIgnoreCase)) {
				az = true;
				arg = arg.Length > 2 ? arg.Substring(0, arg.Length - 2).Trim() : "";
			}
			bool chatOnly = arg.Equals("chat", StringComparison.OrdinalIgnoreCase);
			int page = 1;
			bool isPage = arg.Length == 0 || chatOnly || int.TryParse(arg, out page);
			if (page < 1) page = 1;

			if (!chatOnly && !BccChat.IsCapturing(causedBy) && UiPreferences.UsesWindow(causedBy, "/modlist")) {
				if (isPage) {
					UiManager.Open(causedBy, new UiManager.PageState { Kind = "mods", Sort = az ? "az" : "" }, true);
				} else {
					ModInfoCollector.ModEntry found = ModInfoCollector.Find(arg);
					UiManager.Open(causedBy, new UiManager.PageState { Kind = "mods" }, true);
					if (found != null) {
						UiManager.Open(causedBy, new UiManager.PageState { Kind = "mod", Key = found.Name });
					}
				}
				return true;
			}

			if (!isPage) {
				ModInfoCollector.ModEntry m = ModInfoCollector.Find(arg);
				if (m == null) {
					BccChat.Send(causedBy, Style.Error($"No mod named '{arg}'."));
					return true;
				}
				BccChat.Send(causedBy, Style.Header($"{TextHelper.SafeName(m.Name)} v{m.Version}"));
				if (!string.IsNullOrEmpty(m.Author)) BccChat.Send(causedBy, $"{Style.Muted("Author:")} {TextHelper.SafeName(m.Author)}");
				BccChat.Send(causedBy, string.IsNullOrEmpty(m.Description) ? Style.Muted("No description.") : TextHelper.SafeName(m.Description));
				foreach (string l in m.LongDescription.Take(8)) {
					BccChat.Send(causedBy, TextHelper.SafeName(l));
				}
				return true;
			}
			List<ModInfoCollector.ModEntry> all = ModInfoCollector.GetVisible(null, az);
			List<ModInfoCollector.ModEntry> entries = TextHelper.Page(all, page, 10, out int p, out int pages);
			BccChat.Send(causedBy, Style.Header($"Installed mods ({all.Count}) - page {p}/{pages}"));
			foreach (ModInfoCollector.ModEntry m in entries) {
				string desc = string.IsNullOrEmpty(m.Description) ? "" : " - " + TextHelper.Truncate(TextHelper.SafeName(m.Description), 70);
				BccChat.Send(causedBy, $"<b>{TextHelper.SafeName(m.Name)}</b> {Style.Muted("v" + m.Version)}{desc}");
			}
			if (pages > 1) {
				BccChat.Send(causedBy, Style.Muted($"/modlist chat <page> for more, /modlist <name> for details, add 'az' to sort A-Z"));
			}
			return true;
		}
	}
}
