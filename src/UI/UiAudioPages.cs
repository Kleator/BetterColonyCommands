using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chatting.Commands;
using Newtonsoft.Json.Linq;
using NetworkUI;
using NetworkUI.Items;
using Pipliz;

using Math = System.Math;

namespace ColonyCommands
{
	// Audio sources fully by buttons: list (on/off, edit, play, stop, go to, delete), add at your
	// position, every setting of a source, track settings, markers on/off. Permission
	// mods.scarabol.commands.audio or the admin config permission (checked on build and every click).
	public static class UiAudioPages
	{
		static int W => UiManager.ContentWidth;

		public static bool CanEdit(Players.Player player)
		{
			return player != null && (BccPermissions.CanEditConfig(player) || PermissionsManager.HasPermission(player, BccAudio.PERMISSION));
		}

		static bool Denied(Players.Player player, NetworkMenu menu)
		{
			if (CanEdit(player)) {
				return false;
			}
			menu.Items.Add(UiManager.Text(Style.Error("You don't have permission to manage audio sources.")));
			return true;
		}

		static readonly string[] SpatialLabels = { "Positional (3D: fades with distance, directional)", "Area (same volume inside, fades at the edge)" };
		static readonly string[] TriggerLabels = {
			"In range (plays while a player is near)",
			"Enter (a player comes within the trigger radius)",
			"Click (right-click the block at / below the source)",
			"Join (a player joins within the trigger radius)",
			"Interval (every N minutes)",
			"Daybreak (sunrise)",
			"Nightfall (sunset)",
			"Time of day (at an in-game hour)",
			"Manual (/audio play, window, other mods)"
		};
		static readonly string[] AudienceLabels = { "Everyone in range", "Only the triggering player" };
		static readonly string[] TimeLabels = { "Always", "Only during the day", "Only at night" };

		// ------------------------------------------------------------------ list

		public static void BuildList(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "Audio sources");
			if (Denied(player, menu)) {
				return;
			}
			UiManager.Messages(menu, state);
			string reason = BccAudio.Unavailable();
			if (reason != null) {
				menu.Items.Add(UiManager.Text(Style.Error(reason), -1, 15));
				return;
			}
			List<AudioSourceDef> sources = BccAudio.Sources;
			long kb = BccAudio.TotalBytes / 1024;
			bool markers = BccAudio.MarkersOn(player);
			menu.Items.Add(UiManager.Row(34,
				(UiManager.Text(Style.Muted($"{BccAudio.PlayableTracks.Count} track(s), {sources.Count} source(s), {BccAudio.TotalVoices} voice(s) playing now · audio files {kb} KB (every player downloads them once)"), 32, 13), W - 2 * 196),
				(UiManager.Button("act", "markers", markers ? "Markers: on" : "Markers: off", 186), 196),
				(UiManager.Button("act", "settings", "Audio settings", 186), 196)));
			if (kb > BccSettingsManager.Settings.Audio.WarnTotalMegabytes * 1024L) {
				menu.Items.Add(UiManager.Text(Style.Error(FormattableString.Invariant($"The audio files are {kb / 1024.0:0.#} MB - every joining player downloads them once. Smaller files (mono, 96 kbit/s) join faster.")), -1, 13));
			}
			List<string> pending = BccAudio.PendingFiles();
			if (pending.Count > 0) {
				menu.Items.Add(UiManager.Text($"<color=#FFB347>New file(s) {string.Join(", ", pending)} - loaded after the next server restart.</color>", -1, 13));
			}
			List<string> unconverted = BccAudio.UnconvertedFiles();
			if (unconverted.Count > 0) {
				menu.Items.Add(UiManager.Text($"<color=#FFB347>{string.Join(", ", unconverted)}: not Ogg Vorbis - the game only plays .ogg files. How to convert them: see Tracks below.</color>", -1, 13));
			}

			UiManager.SectionHeader(menu, $"Sources ({sources.Count})");
			state.Lists["srcs"] = sources.Select(s => s.Name).ToList();
			int textWidth = W - 6 * 76 - 8;
			for (int i = 0; i < sources.Count; i++) {
				AudioSourceDef s = sources[i];
				string state2 = BccAudio.StateText(s);
				string stateColored = !s.Enabled ? Style.Muted(state2) : (state2.StartsWith("playing") ? Style.Success(state2) : (state2 == "track missing" ? Style.Error(state2) : state2));
				string label = $"<b>{s.Name}</b> {stateColored}\n<size=85%>{Style.Muted(BccAudio.Summary(s) + $" · {(int)s.X} {(int)s.Y} {(int)s.Z}")}</size>";
				menu.Items.Add(new BackgroundColor(UiManager.Row(44,
					(UiManager.Text(label, 42, 14), textWidth),
					(UiManager.Button("act", "srctoggle:" + i, s.Enabled ? "Off" : "On", 72), 76),
					(UiManager.Button("act", "srcedit:" + i, "Edit", 72), 76),
					(UiManager.Button("act", "srcplay:" + i, "Play", 72, s.Enabled), 76),
					(UiManager.Button("act", "srcstop:" + i, "Stop", 72, BccAudio.IsTriggeredActive(s)), 76),
					(UiManager.Button("act", "srcgoto:" + i, "Go to", 72), 76),
					(UiManager.Button("act", "srcdel:" + i, "Delete", 72), 76)), W, 48, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			if (sources.Count == 0) {
				menu.Items.Add(UiManager.Text(Style.Muted("No audio sources yet - add one below at the position you are standing."), 22, 13));
			}

			List<string> playable = BccAudio.PlayableTracks;
			UiManager.SectionHeader(menu, "Add a source at your position");
			if (playable.Count == 0) {
				menu.Items.Add(UiManager.Text(Style.Muted($"No tracks yet: put Ogg Vorbis files (*.ogg) into gamedata/savegames/{ServerManager.WorldName}/{BccAudio.FOLDER}/ and restart the server."), -1, 13));
			} else {
				menu.LocalStorage.SetAs("newtrack", 0);
				menu.LocalStorage.SetAs("newname", "");
				menu.Items.Add(UiManager.Row(34,
					(UiManager.Text("<b>Track</b>", 30, 14), 70),
					(new DropDownNoLabel("newtrack", playable, 30, 260), 270),
					(UiManager.Text("<b>Name</b>", 30, 14), 64),
					(new InputField("newname", W - 70 - 270 - 64 - 200, 30), W - 70 - 270 - 64 - 196),
					(UiManager.Button("act", "add", "Add here", 186), 196)));
				menu.Items.Add(UiManager.Text(Style.Muted("Empty name = track name. Short tracks (< 15 s) start as a one-shot effect, long ones as looping music - change everything afterwards with Edit."), -1, 12));
			}

			List<KeyValuePair<string, AudioTrackSettings>> all = BccAudio.AllTracks.ToList();
			UiManager.SectionHeader(menu, $"Tracks ({all.Count})");
			menu.Items.Add(UiManager.Text(Style.Muted($"Folder: gamedata/savegames/{ServerManager.WorldName}/{BccAudio.FOLDER}/ - Ogg Vorbis files only, new files need a server restart."), -1, 12));
			state.Lists["tracks"] = all.Select(kvp => kvp.Key).ToList();
			for (int i = 0; i < all.Count; i++) {
				string id = all[i].Key;
				AudioTrackSettings t = all[i].Value;
				int used = sources.Count(s => string.Equals(s.Track, id, StringComparison.OrdinalIgnoreCase));
				bool ok = BccAudio.IsPlayable(id);
				string info = ok
					? Style.Muted($"{t.File} · {AudioChatCommand.FormatLength(t.LengthSeconds)} · {t.SizeBytes / 1024} KB · {(t.Channels == 1 ? "mono" : t.Channels + " ch")} {(t.SampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)} kHz · {t.Group} · used by {used}")
					: Style.Error(string.IsNullOrEmpty(t.Problem) ? "not loaded (restart the server)" : t.Problem);
				menu.Items.Add(new BackgroundColor(UiManager.Row(40,
					(UiManager.Text($"<b>{id}</b>\n<size=85%>{info}</size>", 38, 14), W - 2 * 106 - 4),
					(UiManager.Button("act", "trkedit:" + i, "Settings", 100), 106),
					(UiManager.Button("act", "trkadd:" + i, "Add here", 100, ok), 106)), W, 44, 0f, 0f, 2f, 2f, UiManager.RowColor(i)));
			}
			for (int i = 0; i < unconverted.Count; i++) {
				string f = unconverted[i];
				menu.Items.Add(new BackgroundColor(UiManager.Text($"<b>{f}</b> <color=#FFB347>not used</color>\n<size=85%><color=#FFE2B8>{BccAudio.ConvertHint(f)}</color></size>", -1, 14),
					W, 66, 0f, 0f, 6f, 2f, new UnityEngine.Color32(120, 60, 0, 90)));
			}
			if (all.Count == 0 && unconverted.Count == 0) {
				menu.Items.Add(UiManager.Text(Style.Muted("No audio files found."), 22, 13));
			}
		}

		// ------------------------------------------------------------------ source

		static void Field(NetworkMenu menu, string key, string label, string value, string help, int inputWidth = 0)
		{
			menu.LocalStorage.SetAs(key, value ?? "");
			int iw = inputWidth > 0 ? inputWidth : W - 228;
			menu.Items.Add(UiManager.Row(32,
				(UiManager.Text($"<b>{label}</b>", 30, 14), 220),
				(new InputField(key, iw, 30), iw + 8)));
			if (!string.IsNullOrEmpty(help)) {
				menu.Items.Add(UiManager.Text(Style.Muted(help), -1, 12));
			}
		}

		static void Choice(NetworkMenu menu, string key, string label, string[] options, int selected, string help)
		{
			menu.LocalStorage.SetAs(key, selected);
			menu.Items.Add(UiManager.Row(34,
				(UiManager.Text($"<b>{label}</b>", 30, 14), 220),
				(new DropDownNoLabel(key, options.ToList(), 30, W - 228), W - 220)));
			if (!string.IsNullOrEmpty(help)) {
				menu.Items.Add(UiManager.Text(Style.Muted(help), -1, 12));
			}
		}

		static void Check(NetworkMenu menu, string key, string label, bool value, string help)
		{
			menu.LocalStorage.SetAs(key, value);
			menu.Items.Add(new Toggle(new LabelData($"<b>{label}</b>", ELabelAlignment.MiddleLeft, 15, LabelData.ELocalizationType.None), key));
			if (!string.IsNullOrEmpty(help)) {
				menu.Items.Add(UiManager.Text(Style.Muted(help), -1, 12));
			}
		}

		static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

		static List<string> TrackOptions(AudioSourceDef def)
		{
			List<string> list = BccAudio.PlayableTracks;
			if (!string.IsNullOrEmpty(def.Track) && !list.Any(t => string.Equals(t, def.Track, StringComparison.OrdinalIgnoreCase))) {
				list.Insert(0, def.Track);
			}
			if (list.Count == 0) {
				list.Add(def.Track ?? "");
			}
			return list;
		}

		public static void BuildSource(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			AudioSourceDef def = BccAudio.Find(state.Key);
			UiManager.Header(player, menu, def != null ? $"Audio source: {def.Name}" : "Audio source");
			if (Denied(player, menu)) {
				return;
			}
			if (def == null) {
				menu.Items.Add(UiManager.Text(Style.Error("This audio source does not exist (any more).")));
				return;
			}
			UiManager.Messages(menu, state);
			float dist = BccAudio.ToA(player.PositionCamera).DistanceTo(def.Position);
			menu.Items.Add(UiManager.Text($"{BccAudio.StateText(def)} {Style.Muted($"· {BccAudio.Summary(def)} · you are {dist:0} blocks away")}", -1, 14));
			menu.Items.Add(UiManager.Row(36,
				(UiManager.Button("act", "playnow", "Play now", 150, def.Enabled), 156),
				(UiManager.Button("act", "stopnow", "Stop", 150, BccAudio.IsTriggeredActive(def)), 156),
				(UiManager.Button("act", "movehere", "Move here", 150), 156),
				(UiManager.Button("act", "goto", "Go to", 150), 156),
				(new EmptySpace(30), W - 5 * 156),
				(UiManager.Button("act", "delete", "Delete", 150), 156)));
			menu.Items.Add(UiManager.Text(Style.Muted("Play now / Move here / Go to also save the fields below first."), -1, 12));

			UiManager.SectionHeader(menu, "General");
			Field(menu, "aname", "Name", def.Name, null);
			List<string> trackOptions = TrackOptions(def);
			int ti = Math.Max(0, trackOptions.FindIndex(t => string.Equals(t, def.Track, StringComparison.OrdinalIgnoreCase)));
			Choice(menu, "atrack", "Track", trackOptions.ToArray(), ti, BccAudio.IsPlayable(def.Track) ? null : "This track is not loaded - pick another one or restart the server after adding the file.");
			Check(menu, "aenabled", "Enabled", def.Enabled, null);
			menu.LocalStorage.SetAs("ax", F(def.X));
			menu.LocalStorage.SetAs("ay", F(def.Y));
			menu.LocalStorage.SetAs("az", F(def.Z));
			int cw = (W - 228) / 3;
			menu.Items.Add(UiManager.Row(32,
				(UiManager.Text("<b>Position x / y / z</b>", 30, 14), 220),
				(new InputField("ax", cw - 8, 30), cw),
				(new InputField("ay", cw - 8, 30), cw),
				(new InputField("az", cw - 8, 30), cw)));

			UiManager.SectionHeader(menu, "Sound");
			Field(menu, "avolume", "Volume (%)", def.Volume.ToString(), $"1-100 % of full volume (now {BccAudio.EffectiveVolume(def)} % after rounding to the volume steps). The players' own volume sliders apply on top (track setting: {BccAudio.GetTrackSettings(def.Track)?.Group ?? "Ambient"}).", 160);
			Choice(menu, "aspatial", "Spatial", SpatialLabels, (int)def.Spatial, null);
			Field(menu, "arange", "Range (blocks)", F(def.Range), def.Spatial == AudioSpatial.Positional
				? (BccAudio.RangeNote(def) ?? "Audible distance (up to 4096). Prepared exactly.")
				: "Outer edge of the area: silent from here on.", 160);
			Field(menu, "ainner", "Inner radius (blocks)", F(def.InnerRadius), "Area only: full volume up to this distance, then it fades out until the range.", 160);
			AudioSourceDef syncRoot = BccAudio.SyncInfo(def, out List<string> syncWarnings);
			Check(menu, "aloop", "Loop", def.Loop, "Repeat the track. Off = it plays once per trigger (InRange: once per visit).");
			Field(menu, "axfade", "Loop crossfade (s)", F(def.CrossfadeSeconds), syncRoot != null && syncRoot.Loop
				? FormattableString.Invariant($"Not used while synced: the loop rounds of '{syncRoot.Name}' apply (crossfade {syncRoot.CrossfadeSeconds:0.##} s).")
				: "Seconds the end of the track overlaps (fades into) the start of the next round. 0 = seamless loop - best for tracks made to loop.", 160);
			Field(menu, "afadein", "Fade in (s)", F(def.FadeInSeconds), "Fades in when it starts for a player (and when it comes back after being blended out by a channel).", 160);
			Field(menu, "afadeout", "Fade out (s)", F(def.FadeOutSeconds), "Fades out when it is stopped, when its duration is over or when the time condition ends.", 160);
			Check(menu, "asynced", "Synced", def.Synced, syncRoot != null
				? $"Not used while synced: the position in the track comes from '{syncRoot.Name}'."
				: "Looping 'In range' sources: everyone hears the same moment of the track (like a radio). Off = it starts from the beginning for every player who comes into range.");

			UiManager.SectionHeader(menu, "Trigger");
			Choice(menu, "atrigger", "Trigger", TriggerLabels, (int)def.Trigger, null);
			Field(menu, "atradius", "Trigger radius (blocks)", F(def.TriggerRadius), "Enter / Join: distance that triggers it (0 = the range).", 160);
			Field(menu, "ainterval", "Interval (minutes)", F(def.IntervalMinutes), "Interval trigger: real minutes between two starts.", 160);
			Field(menu, "ahour", "Hour (0-24)", F(def.Hour), "Time of day trigger: in-game hour (e.g. 12 = noon, 18.5 = 18:30).", 160);
			Choice(menu, "aaudience", "Who hears it", AudienceLabels, (int)def.Audience, "Enter, Click, Join: everyone in range, or only the player who triggered it.");
			Field(menu, "aduration", "Duration (s)", F(def.DurationSeconds), "How long a triggered start plays before it fades out. 0 = once through the track (no loop) or until stopped / the time condition ends (loop).", 160);
			Field(menu, "acooldown", "Cooldown (s)", F(def.CooldownSeconds), "Minimum time between two triggered starts (per player for 'only the triggering player').", 160);
			Choice(menu, "atime", "Plays", TimeLabels, (int)def.Time, null);

			UiManager.SectionHeader(menu, "Blending with other sources");
			Field(menu, "achannel", "Channel", def.Channel, "Sources with the same channel name never play at the same time for a player: only the one with the highest priority (then the nearest) is heard, the others fade out - e.g. 'music' for all background music. Empty = mixes freely with everything.");
			Field(menu, "apriority", "Priority", def.Priority.ToString(), "Higher wins within the channel. Equal priority: the nearest wins (it changes only when another one is clearly nearer).", 160);
			List<string> syncOptions = new List<string> { "(own timeline)" };
			syncOptions.AddRange(BccAudio.Sources.Where(s => s != def).Select(s => s.Name));
			state.Lists["syncopts"] = syncOptions;
			int si = Math.Max(0, syncOptions.FindIndex(n => string.Equals(n, def.SyncWith, StringComparison.OrdinalIgnoreCase)));
			Choice(menu, "async", "Sync with", syncOptions.ToArray(), si,
				"Follow another source's timeline: always the same point in the music and the same loop rounds, a triggered start joins there instead of starting from the beginning. "
				+ "Trigger, track, volume, range, fades and everything else stay this source's own. For a seamless hand-over put both into the same channel; "
				+ "use the same track or a version of the same length (e.g. a muffled indoor version).");
			foreach (string w in syncWarnings) {
				menu.Items.Add(UiManager.Text($"<color=#FFB347>{w}</color>", -1, 13));
			}
			if (syncRoot != null && syncWarnings.Count == 0) {
				string via = string.Equals(syncRoot.Name, def.SyncWith, StringComparison.OrdinalIgnoreCase) ? "." : $" (via '{def.SyncWith}').";
				menu.Items.Add(UiManager.Text(Style.Success($"Synced with '{syncRoot.Name}'{via}"), -1, 13));
			}
			List<string> followers = BccAudio.Sources.Where(s => s != def && string.Equals(s.SyncWith, def.Name, StringComparison.OrdinalIgnoreCase)).Select(s => s.Name).ToList();
			if (followers.Count > 0) {
				menu.Items.Add(UiManager.Text(Style.Muted($"Synced to this source: {string.Join(", ", followers)}."), -1, 13));
			}

			menu.Items.Add(new EmptySpace(6));
			menu.Items.Add(UiManager.Row(36,
				(UiManager.Button("back", null, "Cancel", 186), 196),
				(new EmptySpace(30), W - 400),
				(UiManager.Button("act", "save", "Save", 186), 196)));
		}

		// applies the window's fields to the source; returns errors (fields that could not be applied)
		static List<string> ApplyFields(AudioSourceDef def, JObject storage, UiManager.PageState state)
		{
			List<string> errors = new List<string>();
			void Set(string key, string storageKey)
			{
				JToken tok = storage[storageKey];
				if (tok == null) {
					return;
				}
				string error = AudioChatCommand.SetField(def, key, tok.Type == JTokenType.Boolean ? (tok.Value<bool>() ? "on" : "off") : tok.ToString());
				if (error != null) {
					errors.Add(error);
				}
			}
			int Index(string storageKey, int count, int fallback)
			{
				JToken tok = storage[storageKey];
				if (tok == null) {
					return fallback;
				}
				int i = tok.Type == JTokenType.Integer ? tok.Value<int>() : (int.TryParse(tok.ToString(), out int v) ? v : fallback);
				return i >= 0 && i < count ? i : fallback;
			}
			Set(nameof(AudioSourceDef.Name), "aname");
			List<string> trackOptions = TrackOptions(def);
			int ti = Index("atrack", trackOptions.Count, -1);
			if (ti >= 0 && BccAudio.ResolveTrackId(trackOptions[ti]) != null) {
				def.Track = BccAudio.ResolveTrackId(trackOptions[ti]);
			}
			Set(nameof(AudioSourceDef.Enabled), "aenabled");
			Set(nameof(AudioSourceDef.X), "ax");
			Set(nameof(AudioSourceDef.Y), "ay");
			Set(nameof(AudioSourceDef.Z), "az");
			Set(nameof(AudioSourceDef.Volume), "avolume");
			def.Spatial = (AudioSpatial)Index("aspatial", 2, (int)def.Spatial);
			Set(nameof(AudioSourceDef.Range), "arange");
			Set(nameof(AudioSourceDef.InnerRadius), "ainner");
			Set(nameof(AudioSourceDef.Loop), "aloop");
			Set(nameof(AudioSourceDef.CrossfadeSeconds), "axfade");
			Set(nameof(AudioSourceDef.FadeInSeconds), "afadein");
			Set(nameof(AudioSourceDef.FadeOutSeconds), "afadeout");
			Set(nameof(AudioSourceDef.Synced), "asynced");
			def.Trigger = (AudioTrigger)Index("atrigger", TriggerLabels.Length, (int)def.Trigger);
			Set(nameof(AudioSourceDef.TriggerRadius), "atradius");
			Set(nameof(AudioSourceDef.IntervalMinutes), "ainterval");
			Set(nameof(AudioSourceDef.Hour), "ahour");
			def.Audience = (AudioAudience)Index("aaudience", 2, (int)def.Audience);
			Set(nameof(AudioSourceDef.DurationSeconds), "aduration");
			Set(nameof(AudioSourceDef.CooldownSeconds), "acooldown");
			def.Time = (AudioTimeCondition)Index("atime", 3, (int)def.Time);
			Set(nameof(AudioSourceDef.Channel), "achannel");
			Set(nameof(AudioSourceDef.Priority), "apriority");
			// "Sync with": the options the page showed (index 0 = own timeline)
			if (state.Lists.TryGetValue("syncopts", out List<string> syncOptions)) {
				int si = Index("async", syncOptions.Count, -1);
				if (si == 0) {
					def.SyncWith = "";
				} else if (si > 0) {
					string error = AudioChatCommand.SetField(def, nameof(AudioSourceDef.SyncWith), syncOptions[si]);
					if (error != null) {
						errors.Add(error);
					}
				}
			}
			return errors;
		}

		// ------------------------------------------------------------------ track

		public static void BuildTrack(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, $"Audio track: {state.Key}");
			if (Denied(player, menu)) {
				return;
			}
			AudioTrackSettings t = BccAudio.GetTrackSettings(state.Key);
			if (t == null) {
				menu.Items.Add(UiManager.Text(Style.Error("This track does not exist (any more).")));
				return;
			}
			UiManager.Messages(menu, state);
			BccAudio.TrackRt rt = BccAudio.GetTrack(state.Key);
			menu.Items.Add(UiManager.Text($"<b>{t.File}</b> {Style.Muted($"· {AudioChatCommand.FormatLength(t.LengthSeconds)} · {t.SizeBytes / 1024} KB · {t.Channels} channel(s) · {t.SampleRate} Hz")}", -1, 14));
			menu.Items.Add(UiManager.Text(BccAudio.IsPlayable(state.Key)
				? Style.Success($"Loaded ({rt?.FileType}, {rt?.Group}).")
				: Style.Error(string.IsNullOrEmpty(t.Problem) ? "Not loaded - restart the server." : t.Problem), -1, 13));
			int used = BccAudio.Sources.Count(s => string.Equals(s.Track, state.Key, StringComparison.OrdinalIgnoreCase));
			menu.Items.Add(UiManager.Text(Style.Muted($"Used by {used} source(s)."), -1, 13));
			UiManager.SectionHeader(menu, "Settings (take effect after a server restart)");
			int gi = Math.Max(0, Array.FindIndex(BccAudio.Groups, g => g.Equals(t.Group ?? "", StringComparison.OrdinalIgnoreCase)));
			Choice(menu, "tgroup", "Volume slider", BccAudio.Groups, gi, "Which of the players' own volume sliders applies (besides the master volume): Ambient (music, ambience), Effects (bells, jingles) or Master only.");
			int li = Math.Max(0, Array.FindIndex(BccAudio.LoadTypes, g => g.Equals(t.LoadType ?? "", StringComparison.OrdinalIgnoreCase)));
			Choice(menu, "tload", "Loading", BccAudio.LoadTypes, li, "Auto = tracks longer than 5 minutes are streamed from disk, shorter ones kept compressed in memory (about the file size). Raw = decoded in memory (much more RAM on the clients).");
			menu.Items.Add(new EmptySpace(6));
			menu.Items.Add(UiManager.Row(36,
				(UiManager.Button("back", null, "Cancel", 186), 196),
				(new EmptySpace(30), W - 400),
				(UiManager.Button("act", "savetrack", "Save", 186), 196)));
		}

		public static void BuildDeleteConfirm(Players.Player player, UiManager.PageState state, NetworkMenu menu)
		{
			UiManager.Header(player, menu, "Delete audio source");
			if (Denied(player, menu)) {
				return;
			}
			AudioSourceDef def = BccAudio.Find(state.Key);
			if (def == null) {
				menu.Items.Add(UiManager.Text(Style.Error("This audio source does not exist (any more).")));
				return;
			}
			menu.Items.Add(UiManager.Text($"Delete the audio source <b>{def.Name}</b> ({BccAudio.Summary(def)})? The track file stays.", -1, 15));
			menu.Items.Add(new EmptySpace(10));
			menu.Items.Add(UiManager.Row(36,
				(UiManager.Button("back", null, "Cancel", 186), 196),
				(new EmptySpace(30), W - 400),
				(UiManager.Button("act", "confirmdel", "Delete", 186), 196)));
		}

		// ------------------------------------------------------------------ actions

		public static void Action(Players.Player player, UiManager.PageState state, string action, string item, JObject storage)
		{
			if (!CanEdit(player)) {
				state.Messages.Add(Style.Error("You don't have permission to manage audio sources."));
				UiManager.Render(player);
				return;
			}
			string unavailable = BccAudio.Unavailable();
			if (unavailable != null && action != "settings") {
				state.Messages.Add(Style.Error(unavailable));
				UiManager.Render(player);
				return;
			}
			AudioSourceDef def = null;
			switch (action) {
				case "srctoggle": case "srcedit": case "srcplay": case "srcstop": case "srcgoto": case "srcdel":
					def = BccAudio.Find(item);
					if (def == null) {
						state.Messages.Add(Style.Error("This audio source does not exist (any more)."));
						UiManager.Render(player);
						return;
					}
					break;
				case "save": case "playnow": case "stopnow": case "movehere": case "goto": case "delete": case "confirmdel":
					def = BccAudio.Find(state.Key);
					if (def == null) {
						UiManager.Back(player);
						return;
					}
					break;
			}
			string ok = null, error = null;
			switch (action) {
				case "markers":
					ok = BccAudio.ToggleMarkers(player) ? "Markers on - you see every audio source and its range near you." : "Markers off.";
					break;
				case "settings":
					UiConfigPages.OpenSection(player, "audio");
					return;
				case "add": {
					List<string> playable = BccAudio.PlayableTracks;
					JToken tok = storage["newtrack"];
					int idx = tok == null ? 0 : (tok.Type == JTokenType.Integer ? tok.Value<int>() : (int.TryParse(tok.ToString(), out int v) ? v : 0));
					if (idx < 0 || idx >= playable.Count) {
						error = "Please choose a track.";
						break;
					}
					error = BccAudio.Add(player, playable[idx], storage.Value<string>("newname") ?? "", out AudioSourceDef created);
					if (error == null) {
						UiManager.Open(player, new UiManager.PageState { Kind = "audiosrc", Key = created.Name, Messages = new List<string> { Style.Success($"'{created.Name}' placed at your position - adjust it below and press Save.") } });
						return;
					}
					break;
				}
				case "trkadd":
				case "trkedit": {
					if (BccAudio.GetTrackSettings(item) == null) {
						error = "This track does not exist (any more).";
						break;
					}
					if (action == "trkedit") {
						UiManager.Open(player, new UiManager.PageState { Kind = "audiotrack", Key = item });
						return;
					}
					error = BccAudio.Add(player, item, "", out AudioSourceDef created);
					if (error == null) {
						UiManager.Open(player, new UiManager.PageState { Kind = "audiosrc", Key = created.Name, Messages = new List<string> { Style.Success($"'{created.Name}' placed at your position - adjust it below and press Save.") } });
						return;
					}
					break;
				}
				case "savetrack": {
					AudioTrackSettings t = BccAudio.GetTrackSettings(state.Key);
					if (t == null) {
						UiManager.Back(player);
						return;
					}
					int gi = storage["tgroup"]?.Type == JTokenType.Integer ? storage["tgroup"].Value<int>() : -1;
					int li = storage["tload"]?.Type == JTokenType.Integer ? storage["tload"].Value<int>() : -1;
					if (gi >= 0 && gi < BccAudio.Groups.Length) t.Group = BccAudio.Groups[gi];
					if (li >= 0 && li < BccAudio.LoadTypes.Length) t.LoadType = BccAudio.LoadTypes[li];
					BccAudio.SaveTrackSettings();
					Log.Write($"[BCC UI] {player.Name} changed audio track '{state.Key}' ({t.Group}, {t.LoadType})");
					BackWith(player, Style.Success($"Track '{state.Key}' saved - takes effect after the next server restart."));
					return;
				}
				case "srctoggle": {
					AudioSourceDef before = def.Clone();
					def.Enabled = !def.Enabled;
					BccAudio.Changed(def, before, player);
					ok = $"'{def.Name}' switched {(def.Enabled ? "on" : "off")}.";
					break;
				}
				case "srcedit":
					UiManager.Open(player, new UiManager.PageState { Kind = "audiosrc", Key = def.Name });
					return;
				case "srcplay":
				case "playnow": {
					if (action == "playnow" && !SaveFields(player, state, def, storage)) {
						return;
					}
					string why = BccAudio.Play(def, player);
					if (why == null) ok = $"'{def.Name}' started" + (def.Audience == AudioAudience.TriggeringPlayer ? " for you." : " for everyone in range.");
					else error = $"'{def.Name}' can't start: {why}.";
					break;
				}
				case "srcstop":
				case "stopnow":
					BccAudio.Stop(def);
					ok = $"'{def.Name}' stopped.";
					break;
				case "srcgoto":
				case "goto":
					if (action == "goto" && !SaveFields(player, state, def, storage)) {
						return;
					}
					UiManager.Close(player);
					Teleport.TeleportTo(player, new UnityEngine.Vector3(def.X, def.Y, def.Z));
					return;
				case "movehere": {
					if (!SaveFields(player, state, def, storage)) {
						return;
					}
					AudioSourceDef before = def.Clone();
					UnityEngine.Vector3 p = player.PositionStanding;
					def.X = (float)Math.Round(p.x, 2);
					def.Y = (float)Math.Round(p.y, 2);
					def.Z = (float)Math.Round(p.z, 2);
					BccAudio.Changed(def, before, player);
					ok = $"'{def.Name}' moved to your position.";
					break;
				}
				case "srcdel":
				case "delete":
					UiManager.Open(player, new UiManager.PageState { Kind = "audiodelete", Key = def.Name });
					return;
				case "confirmdel": {
					string name = def.Name;
					BccAudio.Remove(def, player);
					// back to the list (skipping the deleted source's page)
					UiManager.Back(player);
					UiManager.PageState cur = UiManager.Current(player);
					if (cur != null && cur.Kind == "audiosrc") {
						UiManager.Back(player);
						cur = UiManager.Current(player);
					}
					if (cur != null) {
						cur.Messages.Add(Style.Success($"Audio source '{name}' deleted."));
						UiManager.Render(player);
					}
					return;
				}
				case "save": {
					if (!SaveFields(player, state, def, storage)) {
						return;
					}
					string note = BccAudio.RangeNote(def);
					BackWith(player, Style.Success($"Audio source '{def.Name}' saved.") + (note != null ? " " + Style.Muted(note) : ""));
					return;
				}
			}
			if (error != null) state.Messages.Add(Style.Error(error));
			else if (ok != null) state.Messages.Add(Style.Success(ok));
			UiManager.Render(player);
		}

		// applies and saves the fields of the source page; on errors re-renders the page with them
		static bool SaveFields(Players.Player player, UiManager.PageState state, AudioSourceDef def, JObject storage)
		{
			AudioSourceDef before = def.Clone();
			List<string> errors = ApplyFields(def, storage, state);
			BccAudio.Changed(def, before, player);
			// the page is addressed by name
			state.Key = def.Name;
			if (errors.Count > 0) {
				foreach (string e in errors) {
					state.Messages.Add(Style.Error(e));
				}
				state.Messages.Add(Style.Muted("The other fields were saved."));
				UiManager.Render(player);
				return false;
			}
			return true;
		}

		static void BackWith(Players.Player player, string message)
		{
			UiManager.Back(player);
			UiManager.PageState cur = UiManager.Current(player);
			if (cur != null) {
				cur.Messages.Add(message);
				UiManager.Render(player);
			}
		}
	}
}
