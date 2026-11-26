using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chatting;
using NetworkUI;
using Pipliz;

namespace ColonyCommands
{
	// Developer self test without a game client. Only runs if the server admin puts a file
	//   gamedata/savegames/<world>/bcc-selftest.txt
	// next to the world. 20 seconds after the world loaded, every line of it is executed as a chat
	// command by the server itself (full permissions), its output is captured, internal checks are
	// run, and everything is written to bcc-selftest-result.txt. The request file is then renamed
	// to bcc-selftest.done.txt so it runs only once.
	public static class SelfTest
	{
		static string Dir => Path.Combine("gamedata", "savegames", ServerManager.WorldName);

		public static void AfterWorldLoad()
		{
			string request = Path.Combine(Dir, "bcc-selftest.txt");
			if (!File.Exists(request)) {
				return;
			}
			ThreadManager.InvokeOnMainThread(() => Run(request), 20.0);
		}

		static void Run(string request)
		{
			List<string> report = new List<string>();
			int failures = 0;
			void Check(string name, bool ok, string detail = "")
			{
				if (!ok) failures++;
				report.Add($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " - " + detail : "")}");
			}
			try {
				report.Add($"Better Colony Commands self test {DateTime.Now:yyyy-MM-dd HH:mm:ss}, world {ServerManager.WorldName}");
				report.Add("");
				report.Add("== Internal checks");

				// text helpers (warpbanner / size stripping)
				string banner = "<b><size=25><color=#EB34C9>✮Highrise-</color><color=#FF4FB9>City✮3</color></size></b>";
				Check("NormalizeName matches formatted banner", TextHelper.NormalizeName(banner) == TextHelper.NormalizeName("Highrise-City3"), TextHelper.NormalizeName(banner));
				Check("StripSize removes size tags", !TextHelper.StripSize(banner).Contains("size") && TextHelper.StripSize(banner).Contains("color"), TextHelper.StripSize(banner));
				Check("StripRichText visible text", TextHelper.StripRichText(banner) == "✮Highrise-City✮3", TextHelper.StripRichText(banner));

				// three-way economy merge
				var mine = new Dictionary<string, long> { { "a", 150 }, { "b", 100 }, { "c", 70 } };
				var theirs = new Dictionary<string, long> { { "a", 100 }, { "b", 130 }, { "d", 40 } };
				var snap = new Dictionary<string, long> { { "a", 100 }, { "b", 100 } };
				var merged = EconomyManager.ThreeWayMerge(mine, theirs, snap);
				Check("merge keeps BCC-only change", merged["a"] == 150, merged["a"].ToString());
				Check("merge keeps Pytonia-only change", merged["b"] == 130, merged["b"].ToString());
				Check("merge keeps BCC-only player", merged["c"] == 70);
				Check("merge keeps Pytonia-only player", merged["d"] == 40);
				var both = EconomyManager.ThreeWayMerge(new Dictionary<string, long> { { "x", 1000 } }, new Dictionary<string, long> { { "x", 1200 } }, new Dictionary<string, long>());
				Check("first sync takes larger value (no double starting balance)", both["x"] == 1200, both["x"].ToString());

				// paging
				List<int> nums = Enumerable.Range(1, 23).ToList();
				List<int> p3 = TextHelper.Page(nums, 3, 10, out int cp, out int pc);
				Check("paging last page", pc == 3 && cp == 3 && p3.Count == 3 && p3[0] == 21);
				TextHelper.Page(nums, 99, 10, out cp, out pc);
				Check("paging clamps page", cp == 3);

				// registry
				List<BccCommandInfo> all = CommandRegistry.All();
				var byMod = all.GroupBy(c => c.Mod).Select(g => $"{g.Key}: {g.Count()}").ToList();
				Check("commands registered", all.Count > 60, string.Join(", ", byMod));
				Check("every BCC command has a description", all.Where(c => c.Mod == CommandRegistry.BCC_MOD).All(c => !string.IsNullOrEmpty(c.Description)));
				Check("command without permission gets open auto permission", CommandRegistry.TryGet("/rtp", out BccCommandInfo rtp) && rtp.OpenByDefault && rtp.Permission == "bcc.cmd.rtp", rtp?.Permission);
				Check("legacy /help list still exists", HelpCommand.GetLegacyEntries().Count > 50);
				foreach (BccCommandInfo c in all.Where(c => c.Mod != CommandRegistry.BCC_MOD)) {
					report.Add($"   foreign: {c.Command} [{c.Mod}] perm={c.Permission}{(c.OpenByDefault ? " (open)" : "")} - {c.Description}");
				}
				Check("interceptor is first command", CommandManager.RegisteredCommands.Count > 0 && CommandManager.RegisteredCommands[0] is CommandInterceptor);

				// permissions
				List<BccPermissions.PermissionInfo> perms = BccPermissions.GetAll();
				Check("permission overview has entries", perms.Count > 40, $"{perms.Count} permissions, all-permission groups: {string.Join(", ", BccPermissions.GetAllPermissionGroups())}");

				// roles
				foreach (string g in ChatColors.Colors.Keys) {
					report.Add($"   role format {g}: {ChatColors.FormatRole(g)}");
				}

				// mods
				List<ModInfoCollector.ModEntry> mods = ModInfoCollector.GetVisible();
				Check("mod list collected", mods.Count > 0, $"{mods.Count} mods");
				foreach (ModInfoCollector.ModEntry m in mods) {
					report.Add($"   mod: {m.Name} v{m.Version} image={(m.ImageFile.IsValid ? Path.GetFileName(m.ImagePath) : "-")} workshop={m.WorkshopId} desc={TextHelper.Truncate(m.Description, 60)}");
				}

				// economy
				report.Add($"   economy enabled={EconomyManager.IsEnabled} backend={EconomyManager.BackendName} pytoniaInstalled={PytoniaEconomyBridge.IsInstalled} pytoniaReady={PytoniaEconomyBridge.IsReady} trader={TraderBridge.IsInstalled} currency={EconomyManager.CurrencyName}");
				EconomyLedger ledger = EconomyLedgerStore.Ledger;
				report.Add($"   ledger: {ledger.Balances.Count} balances, {ledger.PytoniaSnapshot.Count} pytonia snapshot, {ledger.TraderSnapshots.Count} trader snapshots ({ledger.TraderSnapshots.Values.Sum(s => s.Coins)} coins), {ledger.TraderConverted.Count} converted groups, {ledger.PendingNotices.Count} pending notices");

				// UI pages: build every page for the server player (not sent anywhere)
				Players.Player server = Players.GetPlayer(Players.GetServerID());
				foreach (string kind in new[] { "help", "mods", "perms", "config", "settings" }) {
					Check($"UI page '{kind}' builds", TryBuild(server, new UiManager.PageState { Kind = kind }, out string info), info);
				}
				Check("UI page 'cmd' builds", TryBuild(server, new UiManager.PageState { Kind = "cmd", Key = "/warp" }, out string ci), ci);
				if (mods.Count > 0) {
					Check("UI page 'mod' builds", TryBuild(server, new UiManager.PageState { Kind = "mod", Key = mods[0].Name }, out string mi), mi);
				}
				foreach (UiConfigPages.ConfigSection s in UiConfigPages.Sections()) {
					Check($"UI config section '{s.Key}' builds", TryBuild(server, new UiManager.PageState { Kind = "configsection", Key = s.Key }, out string si), si);
				}
				// audio sources
				report.Add($"   audio: active={BccAudio.IsActive} tracks={BccAudio.PlayableTracks.Count}/{BccAudio.AllTracks.Count()} sources={BccAudio.Sources.Count} files={BccAudio.TotalBytes / 1024} KB audio table={BccAudio.AudioTableBytes() / 1024} KB ({BccAudio.Unavailable() ?? "ok"})");
				if (BccAudio.IsActive) {
					AudioEngineConfig ac = BccAudio.EngineConfig;
					foreach (string id in BccAudio.PlayableTracks) {
						bool all3d = true, allFlat = true;
						for (int lv = 0; lv < ac.LevelCount; lv++) {
							for (int ri = 0; ri < ac.RangeSteps.Length; ri++) {
								all3d &= BccAudio.TryClip(new VoiceSpec { Track = id, RangeIndex = ri, Level = lv }, out _);
							}
							allFlat &= BccAudio.TryClip(new VoiceSpec { Track = id, Flat = true, Level = lv }, out _);
						}
						Check($"audio track '{id}' registered in all {ac.RangeSteps.Length}x{ac.LevelCount} positional + {ac.LevelCount} area steps", all3d && allFlat);
					}
					Check("UI page 'audio' builds", TryBuild(server, new UiManager.PageState { Kind = "audio" }, out string ai), ai);
					if (BccAudio.Sources.Count > 0) {
						Check("UI page 'audiosrc' builds", TryBuild(server, new UiManager.PageState { Kind = "audiosrc", Key = BccAudio.Sources[0].Name }, out string asi), asi);
					}
					if (BccAudio.PlayableTracks.Count > 0) {
						Check("UI page 'audiotrack' builds", TryBuild(server, new UiManager.PageState { Kind = "audiotrack", Key = BccAudio.PlayableTracks[0] }, out string ati), ati);
					}
				}
				Check("UI page 'output' builds", TryBuild(server, new UiManager.PageState { Kind = "output", Title = "t", Lines = Enumerable.Range(1, 40).Select(i => "line " + i).ToList(), Page = 2 }, out string oi), oi);

				// commands from the request file
				report.Add("");
				report.Add("== Commands");
				foreach (string raw in File.ReadAllLines(request)) {
					string line = raw.Trim();
					if (line.Length == 0 || line.StartsWith("#")) {
						continue;
					}
					if (line.StartsWith("!")) {
						report.Add($"> {line}");
						foreach (string o in Directive(line)) {
							report.Add("    " + o);
						}
						if (line.StartsWith("!quit")) {
							quitRequested = true;
						}
						continue;
					}
					List<string> output = UiPages.RunCaptured(server, line, out bool handled);
					report.Add($"> {line}   [{(handled ? "handled" : "NOT HANDLED")}, {output.Count} line(s)]");
					foreach (string o in output) {
						report.Add("    " + o);
					}
				}
			} catch (Exception e) {
				failures++;
				report.Add("EXCEPTION: " + e);
			}
			report.Add("");
			report.Add($"== {failures} failure(s)");
			try {
				File.WriteAllLines(Path.Combine(Dir, "bcc-selftest-result.txt"), report);
				string done = Path.Combine(Dir, "bcc-selftest.done.txt");
				if (File.Exists(done)) {
					File.Delete(done);
				}
				File.Move(request, done);
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: self test could not write its result: {e.Message}");
			}
			Log.Write($"Better Colony Commands: self test finished with {failures} failure(s), see bcc-selftest-result.txt");
			if (crashRequested) {
				crashRequested = false;
				Log.Write("Better Colony Commands: self test kills the server process (simulated crash)");
				System.Diagnostics.Process.GetCurrentProcess().Kill();
			}
			if (quitRequested) {
				quitRequested = false;
				// regular shutdown: saves the world and runs every mod's OnQuit
				ThreadManager.InvokeOnMainThread(() => UnityEngine.Application.Quit(), 2.0);
			}
		}

		static bool quitRequested;
		static bool crashRequested;

		// test helpers: !addtradercoins <n>, !tradercoins, !autosave, !ledger, !quit
		static List<string> Directive(string line)
		{
			List<string> result = new List<string>();
			string[] parts = line.Split(' ');
			ColonyGroup group = FirstOwnedGroup();
			switch (parts[0]) {
				case "!addtradercoins":
					if (group == null || !TraderBridge.IsInstalled) {
						result.Add("no colony group with owner or THE TRADER not installed");
						break;
					}
					int n = parts.Length > 1 && int.TryParse(parts[1], out int v) ? v : 100;
					TraderBridge.AddCoins(group, n);
					TraderBridge.Snapshot();
					EconomyLedgerStore.Save();
					result.Add($"added {n} coins to group {group.ColonyGroupID.Value} ({TraderBridge.GroupName(group)}), now {TraderBridge.GetCoins(group)}, owners: {string.Join(", ", OwnerNames(group))}");
					break;
				case "!tradercoins":
					if (group != null) {
						result.Add($"group {group.ColonyGroupID.Value}: {(TraderBridge.IsInstalled ? TraderBridge.GetCoins(group).ToString() : "trader not installed")} coins in stockpile");
						EconomyLedger l = EconomyLedgerStore.Ledger;
						string id = group.ColonyGroupID.Value.ToString();
						result.Add($"snapshot: {(l.TraderSnapshots.TryGetValue(id, out var s) ? s.Coins.ToString() : "-")}, converted: {(l.TraderConverted.TryGetValue(id, out var c) ? c.Coins + " coins -> " + c.Money : "-")}");
						foreach (string key in OwnerKeys(group)) {
							result.Add($"owner {key}: balance {EconomyManager.GetBalanceByKey(key)}, notices {(l.PendingNotices.TryGetValue(key, out var ns) ? ns.Count : 0)}");
						}
					}
					break;
				case "!findcolony": {
					string name = line.Substring(parts[0].Length).Trim();
					if (PlayerHelper.TryGetColony(name, out Colony col, out string err)) {
						result.Add($"'{name}' -> colony #{col.ColonyID.Value} '{col.Name}' (shown as '{TextHelper.SafeName(col.Name)}')");
					} else {
						result.Add($"'{name}' -> not found: {err}");
					}
					break;
				}
				case "!as": {
					// !as <player name> | <command>  - runs a command as that (possibly offline) player
					string rest = line.Substring(3).Trim();
					int bar = rest.IndexOf('|');
					if (bar < 0) {
						result.Add("syntax: !as <player> | <command>");
						break;
					}
					string who = rest.Substring(0, bar).Trim();
					string cmd = rest.Substring(bar + 1).Trim();
					if (!PlayerHelper.TryGetPlayer(who, out Players.Player p, out string perr, true)) {
						result.Add($"player '{who}' not found: {perr}");
						break;
					}
					List<string> output = UiPages.RunCaptured(p, cmd, out bool handled);
					result.Add($"as {p.Name} ({p.ID.ToStringReadable()}): {cmd} [{(handled ? "handled" : "NOT HANDLED")}, {output.Count} line(s)]");
					result.AddRange(output);
					break;
				}
				case "!ui": {
					// !ui <player> | <kind> [key]  - builds a window for that player and lists its elements
					string rest = line.Substring(3).Trim();
					int bar = rest.IndexOf('|');
					string who = bar >= 0 ? rest.Substring(0, bar).Trim() : "";
					string[] spec = (bar >= 0 ? rest.Substring(bar + 1) : rest).Trim().Split(new[] { ' ' }, 2);
					Players.Player p = Players.GetPlayer(Players.GetServerID());
					if (who.Length > 0 && !PlayerHelper.TryGetPlayer(who, out p, out string perr, true)) {
						result.Add($"player '{who}' not found: {perr}");
						break;
					}
					NetworkMenu menu = new NetworkMenu();
					UiManager.PageState state = new UiManager.PageState { Kind = spec[0], Key = spec.Length > 1 ? spec[1] : "" };
					try {
						switch (state.Kind) {
							case "help": UiPages.BuildHelp(p, state, menu); break;
							case "cmd": UiPages.BuildCommand(p, state, menu); break;
							case "mods": UiPages.BuildModList(p, state, menu); break;
							case "mod": UiPages.BuildMod(p, state, menu); break;
							case "perms": UiPages.BuildPermissions(p, state, menu); break;
							case "config": UiConfigPages.BuildSections(p, state, menu); break;
							case "configsection": UiConfigPages.BuildSection(p, state, menu); break;
							case "settings": UiPages.BuildSettings(p, state, menu); break;
							case "roles": UiRolePages.BuildRoles(p, state, menu); break;
							case "role": UiRolePages.BuildRole(p, state, menu); break;
							case "permpicker": UiRolePages.BuildPermissionPicker(p, state, menu); break;
							case "homes": UiPages.BuildHomes(p, state, menu); break;
							case "announce": UiAnnouncementPages.Build(p, state, menu); break;
							case "announceedit": UiAnnouncementPages.BuildEdit(p, state, menu); break;
							case "players": UiPlayerPages.BuildList(p, state, menu); break;
							case "player": UiPlayerPages.BuildPlayer(p, state, menu); break;
							case "playerban": UiPlayerPages.BuildBanConfirm(p, state, menu); break;
							case "trust": UiTrustPages.BuildTrust(p, state, menu); break;
							case "trustplayer": UiTrustPages.BuildPlayer(p, state, menu); break;
							case "audio": UiAudioPages.BuildList(p, state, menu); break;
							case "audiosrc": UiAudioPages.BuildSource(p, state, menu); break;
							case "audiotrack": UiAudioPages.BuildTrack(p, state, menu); break;
							case "audiodelete": UiAudioPages.BuildDeleteConfirm(p, state, menu); break;
						}
					} catch (Exception e) {
						result.Add("EXCEPTION " + e.Message);
					}
					result.Add($"window '{state.Kind}' for {p.Name}: {menu.Items.Count} items");
					foreach (IItem item in menu.Items) {
						Dump(item, result, "  ");
					}
					break;
				}
				case "!role": {
					// !role create <name> [parent] [child] | rename <old> <new> | delete <name> | addperm <role> <perm>
					//       | addchild <role> <child> | fmt <role> <prefix> <prefixcolor> | tree
					string err = null;
					switch (parts.Length > 1 ? parts[1] : "") {
						case "create": err = RoleManager.Create(parts[2], parts.Length > 3 ? parts[3] : null, parts.Length > 4 ? parts[4] : null); break;
						case "rename": err = RoleManager.Rename(parts[2], parts[3]); break;
						case "delete": err = RoleManager.Delete(parts[2]); break;
						case "addperm": err = RoleManager.AddPermission(parts[2], parts[3]); break;
						case "addchild": err = RoleManager.AddInclude(parts[2], parts[3]); break;
						case "fmt": RoleManager.SetFormatting(parts[2], parts[3], parts.Length > 4 ? parts[4] : null, null, null); break;
						case "tree":
							foreach (var t in RoleManager.Tree()) {
								RoleManager.Role r = RoleManager.Get(t.role);
								result.Add($"{new string(' ', t.depth * 2)}{t.role} includes=[{string.Join(",", r?.Includes ?? new List<string>())}] perms=[{string.Join(",", r?.Permissions ?? new List<string>())}]{(t.repeat ? " (repeat)" : "")}");
							}
							result.Add($"default={RoleManager.DefaultRole}, chatcolors order: {string.Join(", ", ChatColors.Colors.Keys)}");
							break;
						default: err = "unknown role op"; break;
					}
					result.Add(err == null ? "ok" : "error: " + err);
					break;
				}
				case "!click": {
					// !click <player> | <page kind> [key] | <button id> [| {storage json}]
					//   opens the page in the player's window history and presses the button exactly like a client
					string[] seg = line.Substring(6).Split('|');
					if (seg.Length < 4) {
						result.Add("syntax: !click <player> | <kind> [key] | bcc.ui|<action>|<arg> [| {json}]");
						break;
					}
					string who = seg[0].Trim();
					string[] spec = seg[1].Trim().Split(new[] { ' ' }, 2);
					// the button id itself contains '|': everything after the 2nd separator up to an optional json part
					string rest = string.Join("|", seg.Skip(2));
					string storageJson = null;
					int brace = rest.IndexOf('{');
					if (brace >= 0) {
						storageJson = rest.Substring(brace).Trim();
						rest = rest.Substring(0, brace).TrimEnd(' ', '|');
					}
					string buttonId = rest.Trim();
					Players.Player cp = Players.GetPlayer(Players.GetServerID());
					if (who.Length > 0 && !PlayerHelper.TryGetPlayer(who, out cp, out string cerr, true)) {
						result.Add($"player '{who}' not found: {cerr}");
						break;
					}
					try {
						UiManager.Open(cp, new UiManager.PageState { Kind = spec[0], Key = spec.Length > 1 ? spec[1] : "" }, true);
						ButtonPressCallbackData press = new ButtonPressCallbackData {
							Player = cp,
							ButtonIdentifier = buttonId,
							Storage = storageJson != null ? Newtonsoft.Json.Linq.JObject.Parse(storageJson) : new Newtonsoft.Json.Linq.JObject()
						};
						UiManager.OnPlayerPushedNetworkUIButton(press);
						WarningPopup.OnPlayerPushedNetworkUIButton(press);
						UiManager.PageState cur = UiManager.Current(cp);
						result.Add(cur == null
							? "window closed"
							: $"now on page '{cur.Kind}' key='{cur.Key}' messages: {string.Join(" / ", cur.Messages.Select(TextHelper.StripRichText))}");
					} catch (Exception e) {
						result.Add("EXCEPTION " + e.GetType().Name + ": " + e.Message);
					}
					break;
				}
				case "!autosave":
					ModLoader.Callbacks.OnAutoSaveWorld.Invoke();
					result.Add("autosave callbacks invoked");
					break;
				case "!ledger":
					foreach (var kvp in EconomyManager.GetTop().Take(8)) {
						result.Add($"{kvp.Key} = {kvp.Value} (pytonia snapshot {(EconomyLedgerStore.Ledger.PytoniaSnapshot.TryGetValue(kvp.Key, out long ps) ? ps.ToString() : "-")})");
					}
					break;
				case "!quit":
					result.Add("server will quit after writing this report");
					break;
				case "!permguard":
					// !permguard status | check <game version>
					if (parts.Length > 2 && parts[1] == "check") {
						result.Add("check: " + PermissionGroupsGuard.Check(parts[2]));
					}
					result.Add(TextHelper.StripRichText(PermissionGroupsGuard.Status()));
					break;
				case "!colonytab": {
					// !colonytab <player> | <colony id> - builds the game's Colony tab for that player (active colony set) and lists the left column
					string[] seg = line.Substring(10).Split('|');
					if (seg.Length < 2 || !PlayerHelper.TryGetPlayer(seg[0].Trim(), out Players.Player tp, out string terr, true)) {
						result.Add("syntax: !colonytab <player> | <colony id>");
						break;
					}
					Colony col = TrustManager.GetColony(seg[1].Trim());
					if (col == null) {
						result.Add("colony not found");
						break;
					}
					tp.ActiveColony = col;
					NetworkMenu tab = new NetworkMenu();
					NetworkUI.Items.Table left = new NetworkUI.Items.Table(400, -1) { Rows = new List<IItem>() };
					NetworkUI.Items.Table right = new NetworkUI.Items.Table(400, -1) { Rows = new List<IItem>() };
					NetworkMenuManager.OnConstructInventoryManageColonyUI.Invoke(tp, tab, (left, right));
					foreach (IItem it in left.Rows) {
						Dump(it, result, "  ");
					}
					break;
				}
				case "!trustcheck": {
					// !trustcheck <player> | <colony id> - would Anti-Grief let the player build in that claim?
					string[] seg = line.Substring(11).Split('|');
					if (seg.Length < 2 || !PlayerHelper.TryGetPlayer(seg[0].Trim(), out Players.Player tp, out string terr, true)) {
						result.Add("syntax: !trustcheck <player> | <colony id>");
						break;
					}
					Colony col = TrustManager.GetColony(seg[1].Trim());
					result.Add(col == null ? "colony not found" : $"{tp.Name} on {TextHelper.StripRichText(col.Name ?? "")}: owner={TrustManager.IsOwner(tp, col)} trusted={TrustManager.IsTrusted(tp, col)} guardsFriendly={(TrustManager.GuardsAvailable ? TrustManager.IsFriendlyFor(col, tp).ToString() : "n/a")} own perms=[{string.Join(",", PermissionsHelper.GetOwnPermissions(tp).FindAll(x => x.Contains("antigrief")))}]");
					break;
				}
				case "!buildcheck": {
					// !buildcheck <player> | <colony id> - places planks 3 blocks next to the colony's banner through
					// Anti-Grief's real check (nothing is changed in the world) and reports allowed/denied
					string[] seg = line.Substring(11).Split('|');
					if (seg.Length < 2 || !PlayerHelper.TryGetPlayer(seg[0].Trim(), out Players.Player bp, out string berr, true)) {
						result.Add("syntax: !buildcheck <player> | <colony id>");
						break;
					}
					Colony bcol = TrustManager.GetColony(seg[1].Trim());
					if (bcol == null || bcol.Banners.Count == 0) {
						result.Add("colony/banner not found");
						break;
					}
					Pipliz.Vector3Int bpos = bcol.Banners.GetAt(0).Position;
					ModLoader.OnTryChangeBlockData data = new ModLoader.OnTryChangeBlockData {
						RequestOrigin = new BlockChangeRequestOrigin(bp),
						Position = new Pipliz.Vector3Int(bpos.x + 3, bpos.y, bpos.z + 3),
						TypeOld = BlockTypes.BuiltinBlocks.Types.air,
						TypeNew = BlockTypes.BuiltinBlocks.Types.planks,
						InventoryItemResults = new List<InventoryItem>()
					};
					AntiGrief.OnTryChangeBlock(data);
					result.Add($"{bp.Name} builds at {TextHelper.StripRichText(bcol.Name ?? "")}: {(data.CallbackState == ModLoader.OnTryChangeBlockData.ECallbackState.Cancelled ? "DENIED" : "allowed")}");
					break;
				}
				case "!uifilter": {
					// !uifilter <player> | <kind> | <key> | <search text> - like !ui, with the page's search field filled in
					string[] seg = line.Substring(9).Split('|');
					if (seg.Length < 4 || !PlayerHelper.TryGetPlayer(seg[0].Trim(), out Players.Player fp, out string ferr, true)) {
						result.Add("syntax: !uifilter <player> | <kind> | <key> | <text>");
						break;
					}
					NetworkMenu fmenu = new NetworkMenu();
					UiManager.PageState fstate = new UiManager.PageState { Kind = seg[1].Trim(), Key = seg[2].Trim(), Filter = seg[3].Trim() };
					if (fstate.Kind == "trust") {
						UiTrustPages.BuildTrust(fp, fstate, fmenu);
					}
					result.Add($"window '{fstate.Kind}' for {fp.Name} (search '{fstate.Filter}'): {fmenu.Items.Count} items");
					foreach (IItem item in fmenu.Items) {
						Dump(item, result, "  ");
					}
					break;
				}
				case "!tag": {
					// !tag <player> - chat role tags in priority order and the name as shown everywhere
					if (parts.Length < 2 || !PlayerHelper.TryGetPlayer(line.Substring(4).Trim(), out Players.Player tg, out string tgerr, true)) {
						result.Add("player not found");
						break;
					}
					List<string> keys = ChatColors.GetMatchedGroupKeys(tg);
					result.Add($"{tg.Name}: top={(keys.Count > 0 ? keys[0] : "-")} shown as '{TextHelper.StripRichText(ChatColors.GetDisplayName(tg))}' order=[{string.Join(",", keys)}]");
					break;
				}
				case "!cheat": {
					// !cheat <player> - what a cheat command does: achievements off, cheats used, flight on
					if (parts.Length < 2 || !PlayerHelper.TryGetPlayer(line.Substring(6).Trim(), out Players.Player cp2, out string cerr2, true)) {
						result.Add("player not found");
						break;
					}
					cp2.SetAchievementsDisabled();
					cp2.SetCheatsEnabled();
					cp2.SetFlightMode(true);
					result.Add($"{cp2.Name}: achievementsDisabled={cp2.AreAchievementsDisabled} cheats={cp2.HasEnabledCheats} flight={cp2.HasFlightMode} groups off=[{string.Join(",", Enumerable.Range(0, cp2.ColonyGroups.Count).Select(i => cp2.ColonyGroups.GetAt(i).AreAchievementsDisabled.ToString()))}]");
					break;
				}
				case "!perm": {
					// !perm <player> <permission> - gives the player an own permission (like an admin would)
					if (parts.Length < 3 || !PlayerHelper.TryGetPlayer(parts[1], out Players.Player pp, out string perr2, true)) {
						result.Add("syntax: !perm <player> <permission>");
						break;
					}
					PermissionsManager.AddPermissionToUser(null, pp, parts[2]);
					result.Add($"{pp.Name}: {RoleTestManager.DescribeEntry(pp)}");
					break;
				}
				case "!war": {
					// !war <player> start|end - fair-war role switch and restore
					if (parts.Length < 3 || !PlayerHelper.TryGetPlayer(parts[1], out Players.Player wp, out string werr, true)) {
						result.Add("syntax: !war <player> start|end");
						break;
					}
					if (parts[2] == "start") {
						WarManager.RemovePlayerPermissions(wp);
					} else {
						WarManager.DisableWar(wp);
					}
					string wf = System.IO.Path.Combine(Dir, "war-permissions-to-restore.json");
					result.Add($"{wp.Name}: {RoleTestManager.DescribeEntry(wp)}; file=" + (System.IO.File.Exists(wf) ? string.Join(" ", System.IO.File.ReadAllLines(wf)) : "(none)"));
					break;
				}
				case "!crash":
					// kills the process after writing the report: no world save, no OnQuit
					crashRequested = true;
					result.Add("server process will be killed after writing this report");
					break;
				case "!rt": {
					// !rt start <player> <role> | expire <player> | disconnect <player> | state <player> | addgroup <player> <role> | file
					string op = parts.Length > 1 ? parts[1] : "";
					if (op == "file") {
						result.Add(RoleTestManager.FileText());
						break;
					}
					if (parts.Length < 3 || !PlayerHelper.TryGetPlayer(parts[2], out Players.Player rp, out string rerr, true)) {
						result.Add("player not found");
						break;
					}
					switch (op) {
						case "start": result.Add(RoleTestManager.Start(rp, parts[3]) ?? "started"); break;
						case "expire": RoleTestManager.ExpireForTest(rp); result.Add("expired"); break;
						case "disconnect": RoleTestManager.OnPlayerDisconnected(rp); result.Add("disconnect callback invoked"); break;
						case "addgroup": PermissionsManager.AddGroupToUser(null, rp, parts[3]); result.Add("added by 'another mod'"); break;
						case "state": break;
						default: result.Add("unknown op"); break;
					}
					RoleTestManager.TestRecord rec = RoleTestManager.Get(rp);
					result.Add($"{rp.Name}: {RoleTestManager.DescribeEntry(rp)}; test={(rec == null ? "none" : rec.Role + " (" + RoleTestManager.SecondsLeft(rec) + " s left, saved roles " + string.Join(",", rec.Roles) + ", hadEntry=" + rec.HadEntry + ")")}");
					break;
				}
				case "!audio": {
					// !audio sim <x> <y> <z> <seconds> [source to fire after 1 s]  - simulated listener, every packet listed
					// !audio fire <source> | stop <source> | status
					string op = parts.Length > 1 ? parts[1] : "status";
					if (op == "sim" && parts.Length >= 6
						&& float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sx)
						&& float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sy)
						&& float.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sz)
						&& double.TryParse(parts[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double secs)) {
						result.AddRange(BccAudio.Simulate(new AVec3(sx, sy, sz), secs, parts.Length > 6 ? parts[6] : null));
					} else if ((op == "fire" || op == "stop") && parts.Length > 2) {
						AudioSourceDef def = BccAudio.Find(parts[2]);
						if (def == null) {
							result.Add("unknown source");
						} else if (op == "fire") {
							result.Add(BccAudio.Play(def, null) ?? "started");
						} else {
							BccAudio.Stop(def);
							result.Add("stopped");
						}
					} else {
						foreach (AudioSourceDef def in BccAudio.Sources) {
							result.Add($"{def.Name}: {BccAudio.Summary(def)} - {BccAudio.StateText(def)} @ {def.Position}");
						}
						result.Add($"{BccAudio.TotalVoices} voices, audio table {BccAudio.AudioTableBytes()} bytes");
					}
					break;
				}
				default:
					result.Add("unknown directive");
					break;
			}
			return result;
		}

		static void Dump(IItem item, List<string> output, string indent)
		{
			switch (item) {
				case NetworkUI.Items.Label l:
					output.Add(indent + "label: " + TextHelper.Truncate(TextHelper.StripRichText(l.labelData.text ?? ""), 110).Replace("\n", " / "));
					break;
				case NetworkUI.Items.ButtonCallback b:
					output.Add(indent + $"BUTTON '{TextHelper.StripRichText(b.Text.text ?? "")}' id={b.UniqueIdentifier} {(b.Enabled ? "clickable" : "DISABLED")}");
					break;
				case NetworkUI.Items.InputField f:
					output.Add(indent + $"input {f.UniqueIdentifier}");
					break;
				case NetworkUI.Items.Toggle t:
					output.Add(indent + $"toggle {t.StorageIndexer}: {TextHelper.StripRichText(t.Label.text ?? "")}");
					break;
				case NetworkUI.Items.DropDownNoLabel dd:
					output.Add(indent + $"dropdown {dd.StorageIndexer}: {string.Join(" | ", dd.Options)}");
					break;
				case NetworkUI.Items.FileIcon fi:
					output.Add(indent + $"image file#{fi.FileID.value}");
					break;
				case NetworkUI.Items.HorizontalRow r:
					foreach ((IItem child, int _) in r.Items) {
						Dump(child, output, indent);
					}
					break;
				case NetworkUI.Items.BackgroundColor bg:
					Dump(bg.ChildItem, output, indent);
					break;
				case NetworkUI.Items.Line _:
					output.Add(indent + "----");
					break;
			}
		}

		static ColonyGroup FirstOwnedGroup()
		{
			if (ServerManager.ColonyTracker == null) {
				return null;
			}
			ColonyGroup best = null;
			var e = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
			while (e.MoveNext()) {
				ColonyGroup g = e.Current;
				if (g != null && g.Owners.Count > 0 && (best == null || g.ColonyGroupID.Value < best.ColonyGroupID.Value)) {
					best = g;
				}
			}
			return best;
		}

		static List<string> OwnerKeys(ColonyGroup g)
		{
			List<string> keys = new List<string>();
			for (int i = 0; i < g.Owners.Count; i++) {
				keys.Add(g.Owners.GetAt(i).ID.ToStringReadable());
			}
			return keys;
		}

		static List<string> OwnerNames(ColonyGroup g)
		{
			List<string> names = new List<string>();
			for (int i = 0; i < g.Owners.Count; i++) {
				names.Add(g.Owners.GetAt(i).Name);
			}
			return names;
		}

		static bool TryBuild(Players.Player player, UiManager.PageState state, out string info)
		{
			try {
				NetworkMenu menu = new NetworkMenu();
				switch (state.Kind) {
					case "help": UiPages.BuildHelp(player, state, menu); break;
					case "cmd": UiPages.BuildCommand(player, state, menu); break;
					case "output": UiPages.BuildOutput(player, state, menu); break;
					case "mods": UiPages.BuildModList(player, state, menu); break;
					case "mod": UiPages.BuildMod(player, state, menu); break;
					case "perms": UiPages.BuildPermissions(player, state, menu); break;
					case "config": UiConfigPages.BuildSections(player, state, menu); break;
					case "configsection": UiConfigPages.BuildSection(player, state, menu); break;
					case "settings": UiPages.BuildSettings(player, state, menu); break;
					case "audio": UiAudioPages.BuildList(player, state, menu); break;
					case "audiosrc": UiAudioPages.BuildSource(player, state, menu); break;
					case "audiotrack": UiAudioPages.BuildTrack(player, state, menu); break;
				}
				info = $"{menu.Items.Count} items, page {state.Page}, {state.RowKeys.Count} rows";
				return menu.Items.Count > 0;
			} catch (Exception e) {
				info = e.GetType().Name + ": " + e.Message;
				return false;
			}
		}
	}
}
