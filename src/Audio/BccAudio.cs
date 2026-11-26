using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NetworkUI;
using Pipliz;
using Shared;
using Shared.Networking;
using UnityEngine;
using BlockTypes;

using Math = System.Math;

namespace ColonyCommands
{
	// Audio sources placed in the world by admins (lobby music at the spawn, bells, ambience ...).
	//
	// Tracks: every Ogg Vorbis file in gamedata/savegames/<world>/bcc-audio/. They are registered with
	// the game's audio system at server start - before the game builds its audio table - in every
	// range step (Positional) and volume step, so a source's volume and range can change at any time
	// without a restart (the game itself can't change the volume of a playing clip). Every client
	// downloads the files once when joining (cached afterwards); new files need a server restart.
	//
	// Sources (bcc-audio.json): position + settings, played by the AudioEngine only for players in
	// range - nothing is sent to anyone else.
	[ModLoader.ModManager]
	public static class BccAudio
	{
		public const string FILE_NAME = "bcc-audio.json";
		public const string FOLDER = "bcc-audio";
		public const string PERMISSION = AntiGrief.MOD_PREFIX + "audio";
		const string CLIP_PREFIX = "bcc.audio.";
		// the game plays every audio packet at 70 % (AudioManager.PlayLoop on the client)
		const float CLIENT_VOLUME = 0.7f;
		const int MAX_RANGE = 4096;
		// at most this many source ranges are added to the configured steps (each adds a set of clips per track)
		const int MAX_EXTRA_RANGES = 20;

		public class TrackRt
		{
			public string Id;
			public string File;
			public double Length;
			public long Size;
			public int RangeCount;
			public int LevelCount;
			public AudioManager.AudioClipIndex[,] Positional;
			public AudioManager.AudioClipIndex[] Flat;
			public bool Resolved;
			public string FileType;
			public string Group;
		}

		static AudioFileData data = new AudioFileData();
		static readonly Dictionary<string, TrackRt> tracks = new Dictionary<string, TrackRt>(StringComparer.OrdinalIgnoreCase);
		static readonly AudioEngine engine = new AudioEngine();
		static readonly Stopwatch clock = Stopwatch.StartNew();
		static readonly List<AudioListenerInfo> listeners = new List<AudioListenerInfo>();
		static readonly HashSet<Players.Player> markerViewers = new HashSet<Players.Player>();
		static readonly Dictionary<Players.Player, Dictionary<Pipliz.Vector3Int, MarkerShown>> markersShown = new Dictionary<Players.Player, Dictionary<Pipliz.Vector3Int, MarkerShown>>();
		static bool registered;
		static bool loadFailed;
		static double lastTick;
		static double lastMarkers;
		static readonly HashSet<string> registeredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		static readonly HashSet<string> invalidFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		struct MarkerShown
		{
			public string Text;
			public UnityEngine.Vector3 Dir;
			public double SentAt;
		}

		static BccSettings.AudioSettings Cfg => BccSettingsManager.Settings.Audio;
		public static double Now => clock.Elapsed.TotalSeconds;
		public static bool IsActive => registered;
		public static List<AudioSourceDef> Sources => data.Sources;
		public static AudioEngineConfig EngineConfig => engine.Config;
		static string WorldDir => Path.Combine("gamedata", "savegames", ServerManager.WorldName);
		public static string FolderPath => Path.Combine(WorldDir, FOLDER);
		static string FilePath => Path.Combine(WorldDir, FILE_NAME);

		// ------------------------------------------------------------------ registration (before the game's audio table)

		[ModLoader.ModCallback(ModLoader.EModCallbackType.AfterSelectedWorld, "bcc.audio.registertracks", -200f)]
		[ModLoader.ModCallbackProvidesFor("pipliz.server.loadaudiofiles")]
		public static void RegisterTracks()
		{
			registered = false;
			tracks.Clear();
			registeredFiles.Clear();
			invalidFiles.Clear();
			try {
				BccSettingsManager.Load();
				Load();
				if (!Cfg.Enabled) {
					Log.Write("Better Colony Commands: audio sources are switched off (bcc-settings.json -> Audio.Enabled)");
					return;
				}
				ApplyEngineConfig();
				EnsureFolder();
				ScanTracks();
				List<AudioManager.AudioPatch> patches = new List<AudioManager.AudioPatch>();
				foreach (KeyValuePair<string, AudioTrackSettings> kvp in data.Tracks) {
					AudioTrackSettings t = kvp.Value;
					if (t.Missing) {
						continue;
					}
					patches.AddRange(BuildPatches(kvp.Key, t));
				}
				if (patches.Count > 0) {
					AudioManager.QueueAudioPatch(patches);
				}
				registered = true;
				Save();
				Log.Write($"Better Colony Commands: {tracks.Count} audio track(s) registered ({patches.Count} clip variants, {TotalBytes / 1024} KB), {data.Sources.Count} audio source(s)");
			} catch (Exception e) {
				Log.WriteException("Better Colony Commands: registering the audio tracks failed", e);
			}
		}

		static void ApplyEngineConfig()
		{
			AudioEngineConfig c = engine.Config;
			List<int> steps = (Cfg.RangeSteps ?? new List<int>()).Where(r => r >= 2 && r <= MAX_RANGE).ToList();
			if (steps.Count == 0) {
				steps = new BccSettings.AudioSettings().RangeSteps;
			}
			// the exact range of every positional source (so 500 really is 500, not the nearest step)
			List<int> exact = data.Sources.Where(s => s.Spatial == AudioSpatial.Positional)
				.Select(s => (int)Math.Round(s.Range)).Where(r => r >= 2 && r <= MAX_RANGE && !steps.Contains(r)).Distinct().Take(MAX_EXTRA_RANGES).ToList();
			c.RangeSteps = steps.Concat(exact).Distinct().OrderBy(r => r).Select(r => (float)r).ToArray();
			c.LevelStepDb = Math.Max(0.5f, Math.Min(6f, Cfg.VolumeStepDb));
			float quietest = Math.Max(10f, Math.Min(60f, Math.Abs(Cfg.QuietestDb)));
			c.LevelCount = Math.Max(2, (int)Math.Floor(quietest / c.LevelStepDb) + 1);
			ApplyLiveConfig();
		}

		// settings that may change while the server runs
		public static void ApplyLiveConfig()
		{
			engine.Config.SwitchIntervalSeconds = Math.Max(0.05, Math.Min(5.0, Cfg.FadeStepSeconds));
		}

		static void EnsureFolder()
		{
			Directory.CreateDirectory(FolderPath);
			string readme = Path.Combine(FolderPath, "README.txt");
			if (!File.Exists(readme)) {
				File.WriteAllText(readme,
					"Better Colony Commands - audio tracks\r\n\r\n" +
					"Put Ogg Vorbis files (*.ogg) here and restart the server. Every file becomes a track that\r\n" +
					"admins can place in the world: /audio add <track> [name], or the window /audio.\r\n\r\n" +
					"* Only Ogg Vorbis works (the game's format). Convert mp3/wav e.g. with Audacity (Export as OGG).\r\n" +
					"* Every player downloads all files once when joining - keep them small (mono, 96-128 kbit/s).\r\n" +
					"* Tracks meant to loop should end where they begin (or use the crossfade setting).\r\n" +
					"* Track settings (volume slider group, loading) are in bcc-audio.json and the /audio window.\r\n");
			}
		}

		static string MakeTrackId(string fileName, HashSet<string> taken)
		{
			string stem = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
			StringBuilder sb = new StringBuilder();
			foreach (char ch in stem) {
				sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '-');
			}
			string id = sb.ToString().Trim('-');
			if (id.Length == 0) {
				id = "track";
			}
			string unique = id;
			for (int i = 2; taken.Contains(unique); i++) {
				unique = id + "-" + i;
			}
			return unique;
		}

		static void ScanTracks()
		{
			Dictionary<string, string> byFile = data.Tracks.Where(kvp => !string.IsNullOrEmpty(kvp.Value.File))
				.GroupBy(kvp => kvp.Value.File, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);
			HashSet<string> taken = new HashSet<string>(data.Tracks.Keys, StringComparer.OrdinalIgnoreCase);
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string path in Directory.GetFiles(FolderPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) {
				string file = Path.GetFileName(path);
				if (!file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) {
					continue;
				}
				if (!byFile.TryGetValue(file, out string id)) {
					id = MakeTrackId(file, taken);
					taken.Add(id);
					data.Tracks[id] = new AudioTrackSettings { File = file };
					Log.Write($"Better Colony Commands: new audio track '{id}' ({file})");
				}
				seen.Add(id);
				AudioTrackSettings t = data.Tracks[id];
				OggInfo.Result info = OggInfo.Read(path);
				t.SizeBytes = info.SizeBytes;
				if (!info.Ok) {
					t.Missing = true;
					t.Problem = info.Problem;
					invalidFiles.Add(file);
					Log.WriteWarning($"Better Colony Commands: audio file {file} can't be used: {info.Problem}");
					continue;
				}
				t.Missing = false;
				t.Problem = "";
				t.LengthSeconds = Math.Round(info.LengthSeconds, 3);
				t.Channels = info.Channels;
				t.SampleRate = info.SampleRate;
				registeredFiles.Add(file);
			}
			foreach (string file in UnconvertedFiles()) {
				Log.WriteWarning($"Better Colony Commands: {FOLDER}/{file} is not used - {ConvertHint(file)}");
			}
			foreach (KeyValuePair<string, AudioTrackSettings> kvp in data.Tracks) {
				if (!seen.Contains(kvp.Key)) {
					kvp.Value.Missing = true;
					kvp.Value.Problem = $"file {kvp.Value.File} not found in {FOLDER}";
				}
			}
		}

		public static Packets.AudioMapping.EGroupType ParseGroup(string group)
		{
			switch ((group ?? "").Trim().ToLowerInvariant()) {
				case "effects": return Packets.AudioMapping.EGroupType.Effects;
				case "master": return Packets.AudioMapping.EGroupType.Master;
				default: return Packets.AudioMapping.EGroupType.AmbientLoop;
			}
		}

		public static readonly string[] Groups = { "Ambient", "Effects", "Master" };
		public static readonly string[] LoadTypes = { "Auto", "Streamed", "Compressed", "Raw" };

		static ECachedFileType FileTypeFor(AudioTrackSettings t)
		{
			switch ((t.LoadType ?? "").Trim().ToLowerInvariant()) {
				case "streamed": return ECachedFileType.OggStreamed;
				case "compressed": return ECachedFileType.OggCompressed;
				case "raw": return ECachedFileType.OggRaw;
				// kept compressed in memory: several voices of one clip at once (fades, crossfades) work best that way
				default: return t.LengthSeconds > 300 ? ECachedFileType.OggStreamed : ECachedFileType.OggCompressed;
			}
		}

		static string ClipName(string id, bool flat, int rangeIndex, int level)
		{
			return flat ? $"{CLIP_PREFIX}{id}.flat.v{level}" : $"{CLIP_PREFIX}{id}.r{rangeIndex}.v{level}";
		}

		static List<AudioManager.AudioPatch> BuildPatches(string id, AudioTrackSettings t)
		{
			AudioEngineConfig c = engine.Config;
			// relative to the server folder (the game resolves it with Path.GetFullPath)
			string path = string.Join("/", "gamedata", "savegames", ServerManager.WorldName, FOLDER, t.File);
			Packets.AudioMapping.EGroupType group = ParseGroup(t.Group);
			ECachedFileType type = FileTypeFor(t);
			List<AudioManager.AudioPatch> list = new List<AudioManager.AudioPatch>();
			for (int level = 0; level < c.LevelCount; level++) {
				float volume = c.LevelGain(level) / CLIENT_VOLUME;
				for (int r = 0; r < c.RangeSteps.Length; r++) {
					list.Add(new AudioManager.AudioPatch(ClipName(id, false, r, level), new List<Packets.AudioMapping.FileEntry> {
						Entry(path, group, type, true, volume)
					}, 0, null, c.RangeSteps[r]));
				}
				list.Add(new AudioManager.AudioPatch(ClipName(id, true, 0, level), new List<Packets.AudioMapping.FileEntry> {
					Entry(path, group, type, false, volume)
				}, 0, null, c.RangeSteps[c.RangeSteps.Length - 1]));
			}
			tracks[id] = new TrackRt {
				Id = id,
				File = t.File,
				Length = t.LengthSeconds,
				Size = t.SizeBytes,
				RangeCount = c.RangeSteps.Length,
				LevelCount = c.LevelCount,
				FileType = type.ToString(),
				Group = group.ToString()
			};
			return list;
		}

		static Packets.AudioMapping.FileEntry Entry(string path, Packets.AudioMapping.EGroupType group, ECachedFileType type, bool threeD, float volume)
		{
			return new Packets.AudioMapping.FileEntry {
				audioGroup = group,
				isThreeD = threeD,
				volume = volume,
				fileType = type,
				path = path,
				pitchMinimum = 1f,
				pitchMaximum = 1f
			};
		}

		// ------------------------------------------------------------------ after the world loaded

		public static void AfterWorldLoad()
		{
			if (!registered) {
				return;
			}
			int resolved = 0;
			foreach (TrackRt t in tracks.Values) {
				t.Positional = new AudioManager.AudioClipIndex[t.RangeCount, t.LevelCount];
				t.Flat = new AudioManager.AudioClipIndex[t.LevelCount];
				bool ok = true;
				for (int level = 0; level < t.LevelCount && ok; level++) {
					for (int r = 0; r < t.RangeCount && ok; r++) {
						ok = AudioManager.TryGetIndex(ClipName(t.Id, false, r, level), out t.Positional[r, level]);
					}
					ok = ok && AudioManager.TryGetIndex(ClipName(t.Id, true, 0, level), out t.Flat[level]);
				}
				t.Resolved = ok;
				if (ok) {
					resolved++;
				} else {
					Log.WriteError($"Better Colony Commands: audio track '{t.Id}' was not taken over by the game's audio system");
				}
			}
			engine.Output = new GameOutput();
			engine.TrackLength = id => tracks.TryGetValue(id ?? "", out TrackRt t) ? t.Length : 0.0;
			engine.TrackPlayable = id => tracks.TryGetValue(id ?? "", out TrackRt t) && t.Resolved;
			engine.SetSources(data.Sources, Now);
			if (tracks.Count > 0) {
				Log.Write($"Better Colony Commands: {resolved}/{tracks.Count} audio track(s) ready, audio table {AudioTableBytes() / 1024} KB");
			}
		}

		// size of the audio table every client gets when joining (the game's private package)
		public static int AudioTableBytes()
		{
			try {
				var f = typeof(AudioManager).GetField("AudioFilesPackage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
				return (f?.GetValue(null) as byte[])?.Length ?? -1;
			} catch {
				return -1;
			}
		}

		public static bool TryClip(VoiceSpec spec, out AudioManager.AudioClipIndex clip)
		{
			clip = default;
			if (spec.Track == null || !tracks.TryGetValue(spec.Track, out TrackRt t) || !t.Resolved || spec.Level < 0 || spec.Level >= t.LevelCount) {
				return false;
			}
			if (spec.Flat) {
				clip = t.Flat[spec.Level];
			} else {
				if (spec.RangeIndex < 0 || spec.RangeIndex >= t.RangeCount) {
					return false;
				}
				clip = t.Positional[spec.RangeIndex, spec.Level];
			}
			return clip.IsValid;
		}

		// Every loop packet is sent TWICE. The client starts a voice it doesn't know yet in its "one shot"
		// state, in which it ignores the play length and loops the clip forever; only a second packet for
		// the same id ("refresh") switches it to the state that ends it at the given time. So:
		//  - Play: start + refresh = the voice really ends after lengthMs (no forgotten endless voices)
		//  - Cut: length -1 s twice = a known voice stops on the client's next frame (no fade); an id the
		//    client no longer knows is started and stopped again in the same frame instead of looping forever
		class GameOutput : IAudioOutput
		{
			public uint NewId() => AudioManager.AudioClipPlayingID.GenerateNew().ID;

			public void Play(object listener, uint id, VoiceSpec spec, AVec3 p, int offsetMs, int lengthMs)
			{
				if (listener is Players.Player player && TryClip(spec, out AudioManager.AudioClipIndex clip)) {
					UnityEngine.Vector3 pos = new UnityEngine.Vector3(p.X, p.Y, p.Z);
					AudioManager.AudioClipPlayingID playing = new AudioManager.AudioClipPlayingID(id);
					AudioManager.SendPlayLoopPacket(player, pos, clip, playing, offsetMs, lengthMs);
					AudioManager.SendPlayLoopPacket(player, pos, clip, playing, offsetMs, lengthMs);
				}
			}

			public void Cut(object listener, uint id, VoiceSpec spec, AVec3 p)
			{
				if (listener is Players.Player player && TryClip(spec, out AudioManager.AudioClipIndex clip)) {
					UnityEngine.Vector3 pos = new UnityEngine.Vector3(p.X, p.Y, p.Z);
					AudioManager.AudioClipPlayingID playing = new AudioManager.AudioClipPlayingID(id);
					AudioManager.SendPlayLoopPacket(player, pos, clip, playing, 0, -1000);
					AudioManager.SendPlayLoopPacket(player, pos, clip, playing, 0, -1000);
				}
			}

			public void FadeStop(object listener, uint id, AVec3 p)
			{
				if (!(listener is Players.Player player) || !player.IsConnectionReady) {
					return;
				}
				using (ByteBuilder b = ByteBuilder.Get()) {
					b.Write(ClientMessageType.AudioStop);
					b.WriteVariable(id);
					NetworkWrapper.Send(b, player);
				}
			}
		}

		// ------------------------------------------------------------------ data file

		static void Load()
		{
			loadFailed = false;
			string path = FilePath;
			if (!File.Exists(path)) {
				data = new AudioFileData();
				return;
			}
			try {
				AudioFileData loaded = JsonConvert.DeserializeObject<AudioFileData>(File.ReadAllText(path), new JsonSerializerSettings {
					ObjectCreationHandling = ObjectCreationHandling.Replace
				});
				if (loaded == null) {
					throw new InvalidDataException("empty file");
				}
				if (loaded.Tracks == null) loaded.Tracks = new Dictionary<string, AudioTrackSettings>(StringComparer.OrdinalIgnoreCase);
				else loaded.Tracks = new Dictionary<string, AudioTrackSettings>(loaded.Tracks, StringComparer.OrdinalIgnoreCase);
				if (loaded.Sources == null) loaded.Sources = new List<AudioSourceDef>();
				loaded.Sources.RemoveAll(s => s == null);
				foreach (AudioSourceDef s in loaded.Sources) {
					s.Normalize();
				}
				data = loaded;
			} catch (Exception e) {
				// never overwrite a file we could not read
				loadFailed = true;
				data = new AudioFileData();
				Log.WriteError($"Better Colony Commands: could not read {FILE_NAME}, file left untouched (audio sources inactive until it is fixed): {e.Message}");
			}
		}

		public static void Save()
		{
			if (loadFailed) {
				return;
			}
			try {
				string path = FilePath;
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				if (File.Exists(path)) {
					File.Copy(path, path + ".bak", true);
				}
				File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
			} catch (Exception e) {
				Log.WriteError($"Better Colony Commands: could not save {FILE_NAME}: {e.Message}");
			}
		}

		// /bcreload: sources and track settings from disk (new files still need a restart)
		public static void Reload()
		{
			if (!registered) {
				return;
			}
			engine.StopEverything();
			Dictionary<string, AudioTrackSettings> measured = data.Tracks;
			Load();
			foreach (KeyValuePair<string, AudioTrackSettings> kvp in measured) {
				if (data.Tracks.TryGetValue(kvp.Key, out AudioTrackSettings t)) {
					t.LengthSeconds = kvp.Value.LengthSeconds;
					t.Missing = kvp.Value.Missing;
					t.Problem = kvp.Value.Problem;
				} else {
					data.Tracks[kvp.Key] = kvp.Value;
				}
			}
			ApplyLiveConfig();
			engine.SetSources(data.Sources, Now);
		}

		// ------------------------------------------------------------------ runtime

		public static void OnUpdate()
		{
			if (!registered || engine.Output == null) {
				return;
			}
			double now = Now;
			if (now - lastTick < Math.Max(50, Cfg.UpdateMilliseconds) / 1000.0) {
				return;
			}
			lastTick = now;
			try {
				ApplyLiveConfig();
				listeners.Clear();
				IList<Players.Player> connected = Players.ConnectedPlayers;
				for (int i = 0; i < connected.Count; i++) {
					Players.Player p = connected[i];
					if (p != null && p.IsConnectionReady) {
						listeners.Add(new AudioListenerInfo(p, ToA(p.PositionCamera)));
					}
				}
				engine.Tick(now, listeners, TimeCycle.IsDay, TimeCycle.TimeOfDayHours, TimeCycle.SunRise, TimeCycle.SunSet);
				if (now - lastMarkers >= 1.0) {
					lastMarkers = now;
					TickMarkers(now);
				}
			} catch (Exception e) {
				Log.WriteException("Better Colony Commands: audio update failed", e);
			}
		}

		public static AVec3 ToA(UnityEngine.Vector3 v) => new AVec3(v.x, v.y, v.z);

		public static void OnPlayerConnectedLate(Players.Player player)
		{
			if (!registered || player == null) {
				return;
			}
			AVec3 pos = ToA(player.PositionCamera);
			foreach (AudioSourceDef def in data.Sources) {
				if (def.Trigger == AudioTrigger.Join && def.Enabled && pos.DistanceTo(def.Position) <= def.EffectiveTriggerRadius) {
					engine.Fire(def, player, Now, false);
				}
			}
		}

		public static void OnPlayerDisconnected(Players.Player player)
		{
			engine.RemoveListener(player);
			markerViewers.Remove(player);
			markersShown.Remove(player);
		}

		public static void OnPlayerClicked(Players.Player player, PlayerClickedData click)
		{
			if (!registered || player == null || click == null || click.ClickType != PlayerClickedData.EClickType.Right
				|| click.HitType != PlayerClickedData.EHitType.Block || click.IsHoldingButton) {
				return;
			}
			Pipliz.Vector3Int hit = click.GetVoxelHit().BlockHit;
			foreach (AudioSourceDef def in data.Sources) {
				if (def.Trigger != AudioTrigger.Click || !def.Enabled) {
					continue;
				}
				Pipliz.Vector3Int block = BlockOf(def);
				if (hit == block || hit == new Pipliz.Vector3Int(block.x, block.y - 1, block.z)) {
					engine.Fire(def, player, Now, false);
				}
			}
		}

		public static Pipliz.Vector3Int BlockOf(AudioSourceDef def)
		{
			return new Pipliz.Vector3Int(Mathf.FloorToInt(def.X), Mathf.FloorToInt(def.Y), Mathf.FloorToInt(def.Z));
		}

		// ------------------------------------------------------------------ API for commands, windows and BccApi

		public static AudioSourceDef Find(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) {
				return null;
			}
			name = name.Trim();
			return data.Sources.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
		}

		public static TrackRt GetTrack(string id) => id != null && tracks.TryGetValue(id, out TrackRt t) ? t : null;

		public static AudioTrackSettings GetTrackSettings(string id) => id != null && data.Tracks.TryGetValue(id, out AudioTrackSettings t) ? t : null;

		public static IEnumerable<KeyValuePair<string, AudioTrackSettings>> AllTracks => data.Tracks.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);

		public static List<string> PlayableTracks => tracks.Values.Where(t => t.Resolved).Select(t => t.Id).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

		public static bool IsPlayable(string track) => engine.TrackPlayable(track);

		public static long TotalBytes => tracks.Values.Sum(t => t.Size);

		public static string Unavailable()
		{
			if (!Cfg.Enabled) {
				return "Audio sources are switched off (bcc-settings.json -> Audio.Enabled). Switching them on needs a server restart.";
			}
			if (!registered) {
				return loadFailed ? $"{FILE_NAME} could not be read - fix or remove it and restart the server." : "Audio sources were switched on after the server started - restart the server to use them.";
			}
			if (loadFailed) {
				return $"{FILE_NAME} could not be read - fix or remove it and restart the server.";
			}
			return null;
		}

		// audio files in formats the game client can't play (it only decodes Ogg Vorbis)
		static readonly string[] OtherAudioExtensions = { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".opus", ".wma", ".aif", ".aiff", ".mp4", ".webm", ".mid", ".midi" };

		// such files in the bcc-audio folder (read live, so a fresh upload shows up without a restart)
		public static List<string> UnconvertedFiles()
		{
			try {
				if (!Directory.Exists(FolderPath)) {
					return new List<string>();
				}
				return Directory.GetFiles(FolderPath).Select(Path.GetFileName)
					.Where(f => OtherAudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
					.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
			} catch {
				return new List<string>();
			}
		}

		// what to do with such a file (plain text, no rich text)
		public static string ConvertHint(string file)
		{
			string ext = Path.GetExtension(file).TrimStart('.').ToUpperInvariant();
			string stem = Path.GetFileNameWithoutExtension(file);
			if (File.Exists(Path.Combine(FolderPath, stem + ".ogg"))) {
				return $"{ext} file - already converted ({stem}.ogg is there), this file can be deleted.";
			}
			if (ext == "MID" || ext == "MIDI") {
				return "MIDI contains no audio - export the song as audio (WAV) from your music program, then convert that to Ogg Vorbis (.ogg).";
			}
			return $"{ext} can't be played by the game - convert it to Ogg Vorbis (.ogg): Audacity -> Export -> Ogg Vorbis (quality 5), "
				+ $"or ffmpeg -i \"{file}\" -c:a libvorbis -q:a 5 \"{stem}.ogg\" - then restart the server. Best source: WAV (lossless).";
		}

		// .ogg files that appeared after the start (need a restart)
		public static List<string> PendingFiles()
		{
			try {
				if (!registered || !Directory.Exists(FolderPath)) {
					return new List<string>();
				}
				return Directory.GetFiles(FolderPath, "*.ogg").Select(Path.GetFileName)
					.Where(f => !registeredFiles.Contains(f) && !invalidFiles.Contains(f)).ToList();
			} catch {
				return new List<string>();
			}
		}

		public static string UniqueName(string wanted)
		{
			string baseName = TextHelper.StripRichText(wanted ?? "").Trim().Replace(' ', '-');
			if (baseName.Length == 0) {
				baseName = "source";
			}
			if (baseName.Length > 40) {
				baseName = baseName.Substring(0, 40);
			}
			string name = baseName;
			for (int i = 2; Find(name) != null; i++) {
				name = baseName + "-" + i;
			}
			return name;
		}

		public static string Add(Players.Player admin, string track, string name, out AudioSourceDef def)
		{
			def = null;
			string reason = Unavailable();
			if (reason != null) {
				return reason;
			}
			string id = ResolveTrackId(track);
			if (id == null) {
				return $"Unknown track '{track}'. Tracks: {string.Join(", ", PlayableTracks)}";
			}
			if (!string.IsNullOrWhiteSpace(name) && Find(name) != null) {
				return $"There is already a source named '{name}'.";
			}
			UnityEngine.Vector3 pos = admin.PositionStanding;
			def = new AudioSourceDef {
				Name = UniqueName(string.IsNullOrWhiteSpace(name) ? id : name),
				Track = id,
				X = (float)Math.Round(pos.x, 2),
				Y = (float)Math.Round(pos.y, 2),
				Z = (float)Math.Round(pos.z, 2)
			};
			// short tracks are usually effects (bell, jingle) and long ones music
			TrackRt t = GetTrack(id);
			if (t != null && t.Length > 0 && t.Length < 15) {
				def.Loop = false;
				def.FadeInSeconds = 0f;
				def.FadeOutSeconds = 0.5f;
				def.Range = 24f;
			}
			def.Normalize();
			data.Sources.Add(def);
			engine.SetSources(data.Sources, Now);
			Save();
			Log.Write($"[BCC audio] {admin.Name} added audio source '{def.Name}' ({id}) at {def.Position}");
			return null;
		}

		public static string ResolveTrackId(string track)
		{
			if (string.IsNullOrWhiteSpace(track)) {
				return null;
			}
			track = track.Trim();
			if (tracks.ContainsKey(track)) {
				return tracks[track].Id;
			}
			TrackRt byFile = tracks.Values.FirstOrDefault(t => string.Equals(t.File, track, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(Path.GetFileNameWithoutExtension(t.File), track, StringComparison.OrdinalIgnoreCase));
			return byFile?.Id;
		}

		public static void Remove(AudioSourceDef def, Players.Player by)
		{
			engine.Reset(def);
			data.Sources.Remove(def);
			// sources synced to it get their own timeline again
			foreach (AudioSourceDef s in data.Sources) {
				if (string.Equals(s.SyncWith, def.Name, StringComparison.OrdinalIgnoreCase)) {
					s.SyncWith = "";
				}
			}
			engine.SetSources(data.Sources, Now);
			ClearMarkersEverywhere();
			Save();
			Log.Write($"[BCC audio] {by?.Name ?? "server"} removed audio source '{def.Name}'");
		}

		// the source whose timeline def follows (null = own timeline) and what could keep it from staying in step
		public static AudioSourceDef SyncInfo(AudioSourceDef def, out List<string> warnings)
		{
			warnings = new List<string>();
			if (string.IsNullOrEmpty(def.SyncWith)) {
				return null;
			}
			AudioSourceDef root = AudioEngine.ResolveSyncRoot(def, Find, out string problem);
			if (root == null) {
				warnings.Add($"Not synced: {problem ?? "unknown source"} - it plays on its own timeline.");
				return null;
			}
			if (root.Trigger == AudioTrigger.InRange && root.Loop && !root.Synced) {
				warnings.Add($"'{root.Name}' has Synced off: it starts from the beginning for every player, so this source can't stay at the same point. Switch Synced on at '{root.Name}'.");
			}
			double a = GetTrack(def.Track)?.Length ?? 0, b = GetTrack(root.Track)?.Length ?? 0;
			if (a > 0 && b > 0 && Math.Abs(a - b) > 0.05) {
				warnings.Add(FormattableString.Invariant($"The tracks differ in length ({a:0.##} s / {b:0.##} s): they drift apart with every loop round. Use the same track or a version of the same length."));
			}
			if (string.Equals(def.Track, root.Track, StringComparison.OrdinalIgnoreCase)
				&& (string.IsNullOrEmpty(def.Channel) || !string.Equals(def.Channel, root.Channel, StringComparison.OrdinalIgnoreCase))
				&& def.Position.DistanceTo(root.Position) < def.Range + root.Range) {
				warnings.Add($"Same track as '{root.Name}' and the areas overlap: put both into the same channel - then the hand-over is seamless instead of hearing it twice.");
			}
			return root;
		}

		// call after changing a source; before = copy taken before the change
		public static void Changed(AudioSourceDef def, AudioSourceDef before, Players.Player by)
		{
			def.Normalize();
			if (before != null && AudioSourceDef.NeedsRestart(before, def)) {
				engine.Reset(def);
			}
			if (before != null && (before.X != def.X || before.Y != def.Y || before.Z != def.Z || before.Range != def.Range || before.Spatial != def.Spatial || before.InnerRadius != def.InnerRadius)) {
				ClearMarkersEverywhere();
			}
			Save();
			if (by != null) {
				Log.Write($"[BCC audio] {by.Name} changed audio source '{def.Name}'");
			}
		}

		public static void SaveTrackSettings()
		{
			Save();
		}

		public static string Play(AudioSourceDef def, Players.Player by)
		{
			string reason = Unavailable();
			if (reason != null) {
				return reason;
			}
			return engine.Fire(def, by, Now, true);
		}

		public static void Stop(AudioSourceDef def)
		{
			engine.StopTriggered(def);
		}

		public static bool IsTriggeredActive(AudioSourceDef def) => engine.IsTriggeredActive(def);

		public static int AudibleCount(AudioSourceDef def) => engine.AudibleCount(def);

		public static int TotalVoices => engine.TotalVoices;

		public static string StateText(AudioSourceDef def)
		{
			if (!def.Enabled) {
				return "off";
			}
			if (!IsPlayable(def.Track)) {
				return "track missing";
			}
			int n = AudibleCount(def);
			if (n > 0) {
				return $"playing for {n}";
			}
			if (IsTriggeredActive(def)) {
				return "triggered (nobody in range)";
			}
			return def.Trigger == AudioTrigger.InRange ? "waiting for players" : "waiting for trigger";
		}

		public static string Summary(AudioSourceDef def)
		{
			string space = def.Spatial == AudioSpatial.Area ? FormattableString.Invariant($"area {def.InnerRadius:0.#}/{def.Range:0.#}") : FormattableString.Invariant($"3D {EffectiveRange(def):0.#}");
			string loop = def.Loop ? (def.CrossfadeSeconds > 0 ? FormattableString.Invariant($"loop xf {def.CrossfadeSeconds:0.#}s") : "loop") : "once";
			string sync = string.IsNullOrEmpty(def.SyncWith) ? "" : $" · sync {def.SyncWith}";
			return $"{def.Track} · {space} · {def.Volume}% · {loop} · {TriggerText(def)}{sync}";
		}

		public static float EffectiveRange(AudioSourceDef def)
		{
			if (def.Spatial == AudioSpatial.Area) {
				return def.Range;
			}
			AudioEngineConfig c = engine.Config;
			return c.RangeSteps.Length > 0 ? c.RangeSteps[c.RangeIndexFor(def.Range)] : def.Range;
		}

		// null when a positional source's range is prepared exactly, otherwise what happens until the restart
		public static string RangeNote(AudioSourceDef def)
		{
			if (def.Spatial != AudioSpatial.Positional) {
				return null;
			}
			float eff = EffectiveRange(def);
			if (Math.Abs(eff - def.Range) < 0.5f) {
				return null;
			}
			return FormattableString.Invariant($"Range {def.Range:0} is not prepared yet - until the next server restart it uses the nearest prepared step ({eff:0}), after the restart exactly {def.Range:0}.");
		}

		public static int EffectiveVolume(AudioSourceDef def)
		{
			int level = engine.Config.LevelFor(def.Volume / 100f);
			return level < 0 ? 0 : (int)Math.Round(engine.Config.LevelGain(level) * 100f);
		}

		public static string TriggerText(AudioSourceDef def)
		{
			switch (def.Trigger) {
				case AudioTrigger.InRange: return "in range";
				case AudioTrigger.Enter: return FormattableString.Invariant($"enter {def.EffectiveTriggerRadius:0.#}");
				case AudioTrigger.Click: return "click";
				case AudioTrigger.Join: return "join";
				case AudioTrigger.Interval: return FormattableString.Invariant($"every {def.IntervalMinutes:0.##} min");
				case AudioTrigger.Daybreak: return "daybreak";
				case AudioTrigger.Nightfall: return "nightfall";
				case AudioTrigger.TimeOfDay: return FormattableString.Invariant($"at {def.Hour:0.##} h");
				default: return "manual";
			}
		}

		// ------------------------------------------------------------------ self test

		class SimOutput : IAudioOutput
		{
			public double Now;
			public readonly List<string> Lines = new List<string>();
			public int Plays, Cuts, Fades, Unresolved;
			uint next = 1;

			public uint NewId() => next++;

			public void Play(object l, uint id, VoiceSpec s, AVec3 p, int off, int len)
			{
				Plays++;
				bool ok = TryClip(s, out AudioManager.AudioClipIndex clip);
				if (!ok) Unresolved++;
				Lines.Add($"{Now,6:0.0} play  #{id} {s} clip={(ok ? clip.Index.ToString() : "MISSING")} offset={off} ms length={len} ms");
			}

			public void Cut(object l, uint id, VoiceSpec s, AVec3 p)
			{
				Cuts++;
				Lines.Add($"{Now,6:0.0} cut   #{id}");
			}

			public void FadeStop(object l, uint id, AVec3 p)
			{
				Fades++;
				Lines.Add($"{Now,6:0.0} stop  #{id}");
			}
		}

		// runs a separate engine with one imaginary listener at pos (nothing is sent to anyone)
		public static List<string> Simulate(AVec3 pos, double seconds, string fire)
		{
			List<string> result = new List<string>();
			SimOutput rec = new SimOutput();
			AudioEngine sim = new AudioEngine {
				Config = engine.Config,
				Output = rec,
				TrackLength = engine.TrackLength,
				TrackPlayable = engine.TrackPlayable
			};
			sim.SetSources(data.Sources, 0);
			object key = "listener";
			List<AudioListenerInfo> ls = new List<AudioListenerInfo> { new AudioListenerInfo(key, pos) };
			AudioSourceDef fireDef = fire != null ? Find(fire) : null;
			bool fired = false;
			for (double t = 0; t < seconds; t += 0.2) {
				rec.Now = t;
				if (fireDef != null && !fired && t >= 1.0) {
					fired = true;
					rec.Lines.Add($"{t,6:0.0} fire {fireDef.Name}: {sim.Fire(fireDef, key, t, true) ?? "ok"}");
				}
				sim.Tick(t, ls, TimeCycle.IsDay, TimeCycle.TimeOfDayHours, TimeCycle.SunRise, TimeCycle.SunSet);
			}
			int shown = Math.Min(rec.Lines.Count, 80);
			result.AddRange(rec.Lines.Take(shown));
			if (rec.Lines.Count > shown) {
				result.Add($"... {rec.Lines.Count - shown} more");
			}
			result.Add($"summary: {rec.Plays} play, {rec.Cuts} cut, {rec.Fades} stop packets, {rec.Unresolved} without registered clip, audible sources at the end: {string.Join(", ", data.Sources.Where(d => sim.IsAudibleFor(d, key)).Select(d => d.Name))}");
			sim.StopEverything();
			return result;
		}

		// ------------------------------------------------------------------ markers (/audio show)

		public static bool MarkersOn(Players.Player player) => player != null && markerViewers.Contains(player);

		public static bool ToggleMarkers(Players.Player player)
		{
			if (markerViewers.Remove(player)) {
				ClearMarkers(player);
				return false;
			}
			markerViewers.Add(player);
			lastMarkers = 0;
			return true;
		}

		static void ClearMarkers(Players.Player player)
		{
			if (markersShown.TryGetValue(player, out Dictionary<Pipliz.Vector3Int, MarkerShown> shown)) {
				if (player.IsConnectionReady) {
					foreach (Pipliz.Vector3Int p in shown.Keys) {
						SignText.SendClearText(player, p);
					}
				}
				markersShown.Remove(player);
			}
		}

		static void ClearMarkersEverywhere()
		{
			foreach (Players.Player p in markerViewers.ToList()) {
				ClearMarkers(p);
			}
			lastMarkers = 0;
		}

		static void TickMarkers(double now)
		{
			if (markerViewers.Count == 0) {
				return;
			}
			foreach (Players.Player viewer in markerViewers.ToList()) {
				if (viewer == null || !viewer.IsConnectionReady) {
					continue;
				}
				if (!PermissionsManager.HasPermission(viewer, PERMISSION)) {
					markerViewers.Remove(viewer);
					ClearMarkers(viewer);
					continue;
				}
				AVec3 eye = ToA(viewer.PositionCamera);
				Dictionary<Pipliz.Vector3Int, string> want = new Dictionary<Pipliz.Vector3Int, string>();
				HashSet<Pipliz.Vector3Int> centers = new HashSet<Pipliz.Vector3Int>();
				foreach (AudioSourceDef def in data.Sources) {
					float dist = eye.DistanceTo(def.Position);
					if (dist > Cfg.MarkerDistance + def.Range) {
						continue;
					}
					Pipliz.Vector3Int center = BlockOf(def);
					if (dist <= Cfg.MarkerDistance) {
						string color = !def.Enabled ? "#B0B0B0" : (AudibleCount(def) > 0 ? "#7CFC9E" : "#FFD166");
						want[center] = $"<color={color}><b>♪ {def.Name}</b></color>\n<size=70%>{Summary(def)}\n{StateText(def)}</size>";
						centers.Add(center);
					}
					if (Cfg.MarkerRings) {
						AddRing(want, def, EffectiveRange(def), eye);
						if (def.Spatial == AudioSpatial.Area && def.InnerRadius > 1f && def.InnerRadius < def.Range - 1f) {
							AddRing(want, def, def.InnerRadius, eye);
						}
					}
				}
				if (!markersShown.TryGetValue(viewer, out Dictionary<Pipliz.Vector3Int, MarkerShown> shown)) {
					shown = new Dictionary<Pipliz.Vector3Int, MarkerShown>();
					markersShown[viewer] = shown;
				}
				foreach (KeyValuePair<Pipliz.Vector3Int, string> kvp in want) {
					UnityEngine.Vector3 toViewer = viewer.PositionCamera - (kvp.Key.Vector + new UnityEngine.Vector3(0.5f, 1.5f, 0.5f));
					toViewer.y = 0f;
					UnityEngine.Vector3 dir = toViewer.sqrMagnitude > 0.01f ? toViewer.normalized : UnityEngine.Vector3.forward;
					if (shown.TryGetValue(kvp.Key, out MarkerShown s) && s.Text == kvp.Value && UnityEngine.Vector3.Angle(s.Dir, dir) < 20f && now - s.SentAt < 10.0) {
						continue;
					}
					bool center = centers.Contains(kvp.Key);
					SignText.Config config = new SignText.Config(new JObject {
						{ "offsetx", 0.5f }, { "offsety", center ? 1.6f : 0.6f }, { "offsetz", 0.5f },
						{ "dirx", dir.x }, { "diry", 0f }, { "dirz", dir.z },
						{ "height", center ? 3f : 1.2f }, { "width", center ? 10f : 3f }
					});
					SignText.SendText(viewer, kvp.Key, kvp.Value, config);
					shown[kvp.Key] = new MarkerShown { Text = kvp.Value, Dir = dir, SentAt = now };
				}
				foreach (Pipliz.Vector3Int p in shown.Keys.Where(k => !want.ContainsKey(k)).ToList()) {
					SignText.SendClearText(viewer, p);
					shown.Remove(p);
				}
			}
		}

		// small marks on the circle of the audible range (only the part near the viewer)
		static void AddRing(Dictionary<Pipliz.Vector3Int, string> want, AudioSourceDef def, float radius, AVec3 eye)
		{
			if (radius < 2f) {
				return;
			}
			int count = Math.Max(8, Math.Min(64, (int)Math.Round(2 * Math.PI * radius / 6f)));
			for (int i = 0; i < count; i++) {
				double a = 2 * Math.PI * i / count;
				float x = def.X + (float)(radius * Math.Cos(a));
				float z = def.Z + (float)(radius * Math.Sin(a));
				Pipliz.Vector3Int p = new Pipliz.Vector3Int(Mathf.FloorToInt(x), Mathf.FloorToInt(def.Y), Mathf.FloorToInt(z));
				if (new AVec3(p.x, p.y, p.z).DistanceTo(eye) > Cfg.MarkerDistance) {
					continue;
				}
				// lift out of solid ground (a few blocks at most)
				for (int up = 0; up < 4; up++) {
					if (!World.TryGetTypeAt(p, out ushort type) || type == BuiltinBlocks.Indices.air) {
						break;
					}
					p = new Pipliz.Vector3Int(p.x, p.y + 1, p.z);
				}
				if (!want.ContainsKey(p)) {
					want[p] = $"<color=#FFD166>♪</color><size=60%> {def.Name}</size>";
				}
			}
		}
	}
}
