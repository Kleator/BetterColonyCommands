using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Chatting;
using Chatting.Commands;

using Math = System.Math;

namespace ColonyCommands
{
	// /audio - audio sources (admin, permission mods.scarabol.commands.audio). Without arguments it
	// opens the audio window; everything can also be done in chat.
	public class AudioChatCommand : IChatCommand
	{
		public const string USAGE = "/audio [list|tracks|add|remove|move|tp|play|stop|on|off|set|info|show|near|edit] ...";

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/audio", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, BccAudio.PERMISSION)) {
				return true;
			}
			string sub = splits.Count > 1 ? splits[1].ToLowerInvariant() : "";
			string rest = splits.Count > 2 ? string.Join(" ", splits.Skip(2)).Trim() : "";
			if (sub.Length == 0 && UiPreferences.ServerAllowsWindows && !BccChat.IsCapturing(causedBy)) {
				UiManager.OpenNamed(causedBy, "audio", true);
				return true;
			}
			if (sub != "help" && sub.Length > 0) {
				string reason = BccAudio.Unavailable();
				if (reason != null) {
					BccChat.Send(causedBy, Style.Error(reason));
					return true;
				}
			}
			switch (sub) {
				case "":
				case "list":
					List(causedBy);
					break;
				case "tracks":
					Tracks(causedBy);
					break;
				case "add": {
					string track = splits.Count > 2 ? splits[2] : "";
					string name = splits.Count > 3 ? string.Join(" ", splits.Skip(3)) : "";
					if (track.Length == 0) {
						BccChat.Send(causedBy, Style.Error("Usage: /audio add <track> [name] - tracks: " + string.Join(", ", BccAudio.PlayableTracks)));
						break;
					}
					string error = BccAudio.Add(causedBy, track, name, out AudioSourceDef def);
					BccChat.Send(causedBy, error == null
						? Style.Success($"Audio source '{Style.Value(def.Name)}' placed at your position ({BccAudio.Summary(def)}). Change it with /audio edit {def.Name} or /audio set {def.Name} <setting> <value>.")
						: Style.Error(error));
					break;
				}
				case "remove":
				case "delete":
					WithSource(causedBy, rest, def => {
						BccAudio.Remove(def, causedBy);
						BccChat.Send(causedBy, Style.Success($"Audio source '{def.Name}' removed."));
					});
					break;
				case "move":
					WithSource(causedBy, rest, def => {
						AudioSourceDef before = def.Clone();
						UnityEngine.Vector3 p = causedBy.PositionStanding;
						def.X = (float)Math.Round(p.x, 2);
						def.Y = (float)Math.Round(p.y, 2);
						def.Z = (float)Math.Round(p.z, 2);
						BccAudio.Changed(def, before, causedBy);
						BccChat.Send(causedBy, Style.Success($"Audio source '{def.Name}' moved to your position ({def.Position})."));
					});
					break;
				case "tp":
				case "goto":
					WithSource(causedBy, rest, def => {
						Teleport.TeleportTo(causedBy, new UnityEngine.Vector3(def.X, def.Y, def.Z));
						BccChat.Send(causedBy, Style.Success($"Teleported to audio source '{def.Name}'."));
					});
					break;
				case "play":
					WithSource(causedBy, rest, def => {
						string error = BccAudio.Play(def, causedBy);
						BccChat.Send(causedBy, error == null
							? Style.Success($"'{def.Name}' started" + (def.Audience == AudioAudience.TriggeringPlayer ? " for you." : " for everyone in range."))
							: Style.Error($"'{def.Name}' can't start: {error}."));
					});
					break;
				case "stop":
					WithSource(causedBy, rest, def => {
						if (!BccAudio.IsTriggeredActive(def) && def.Trigger == AudioTrigger.InRange) {
							BccChat.Send(causedBy, Style.Muted($"'{def.Name}' plays whenever someone is in range - switch it off instead: /audio off {def.Name}"));
							return;
						}
						BccAudio.Stop(def);
						BccChat.Send(causedBy, Style.Success($"'{def.Name}' stopped" + (def.FadeOutSeconds > 0 ? FormattableString.Invariant($" (fading out over {def.FadeOutSeconds:0.#} s).") : ".")));
					});
					break;
				case "on":
				case "off":
					WithSource(causedBy, rest, def => {
						AudioSourceDef before = def.Clone();
						def.Enabled = sub == "on";
						BccAudio.Changed(def, before, causedBy);
						BccChat.Send(causedBy, Style.Success($"Audio source '{def.Name}' switched {sub}."));
					});
					break;
				case "set":
					Set(causedBy, splits);
					break;
				case "info":
					WithSource(causedBy, rest, def => Info(causedBy, def));
					break;
				case "show": {
					bool on = BccAudio.ToggleMarkers(causedBy);
					BccChat.Send(causedBy, on
						? Style.Success("Audio source markers on - you see every source (and its range) near you. /audio show again to hide them.")
						: Style.Success("Audio source markers off."));
					break;
				}
				case "near":
					Near(causedBy);
					break;
				case "edit":
					WithSource(causedBy, rest, def => {
						if (UiPreferences.ServerAllowsWindows && !BccChat.IsCapturing(causedBy)) {
							UiManager.Open(causedBy, new UiManager.PageState { Kind = "audiosrc", Key = def.Name }, true);
						} else {
							Info(causedBy, def);
						}
					});
					break;
				default:
					Help(causedBy);
					break;
			}
			return true;
		}

		static void WithSource(Players.Player player, string name, Action<AudioSourceDef> action)
		{
			AudioSourceDef def = BccAudio.Find(name);
			if (def == null) {
				string names = string.Join(", ", BccAudio.Sources.Select(s => s.Name));
				BccChat.Send(player, Style.Error(name.Length == 0 ? "Please name the audio source." : $"No audio source named '{name}'.") + (names.Length > 0 ? Style.Muted(" Sources: " + names) : ""));
				return;
			}
			action(def);
		}

		static void Help(Players.Player p)
		{
			BccChat.Send(p, Style.Header("Audio sources"));
			BccChat.Send(p, $"{Style.Cmd("/audio")} {Style.Muted("window (or list in chat)")} · {Style.Cmd("/audio tracks")} · {Style.Cmd("/audio show")} {Style.Muted("markers on/off")} · {Style.Cmd("/audio near")}");
			BccChat.Send(p, $"{Style.Cmd("/audio add <track> [name]")} {Style.Muted("at your position")} · {Style.Cmd("/audio remove|move|tp|play|stop|on|off|info|edit <name>")}");
			BccChat.Send(p, $"{Style.Cmd("/audio set <name> <setting> <value>")} {Style.Muted("settings: " + string.Join(", ", Settable().Select(f => f.Name)))}");
			BccChat.Send(p, Style.Muted($"Tracks are the Ogg Vorbis files in the world folder {BccAudio.FOLDER} (new files need a restart)."));
		}

		static void List(Players.Player p)
		{
			string reason = BccAudio.Unavailable();
			if (reason != null) {
				BccChat.Send(p, Style.Error(reason));
				return;
			}
			BccChat.Send(p, Style.Header($"Audio sources ({BccAudio.Sources.Count})"));
			foreach (AudioSourceDef def in BccAudio.Sources) {
				BccChat.Send(p, $"{Style.Value(def.Name)} {Style.Muted(BccAudio.Summary(def))} - {BccAudio.StateText(def)} {Style.Muted($"@ {(int)def.X} {(int)def.Y} {(int)def.Z}")}");
			}
			if (BccAudio.Sources.Count == 0) {
				BccChat.Send(p, Style.Muted("None yet - /audio add <track> [name] places one at your position."));
			}
			BccChat.Send(p, Style.Muted($"Tracks: {string.Join(", ", BccAudio.PlayableTracks)}"));
		}

		static void Tracks(Players.Player p)
		{
			BccChat.Send(p, Style.Header("Audio tracks") + Style.Muted($" (folder {BccAudio.FOLDER} in the world folder, {BccAudio.TotalBytes / 1024} KB for every joining player)"));
			foreach (KeyValuePair<string, AudioTrackSettings> kvp in BccAudio.AllTracks) {
				AudioTrackSettings t = kvp.Value;
				string state = BccAudio.IsPlayable(kvp.Key) ? Style.Success("ready") : Style.Error(t.Problem.Length > 0 ? t.Problem : "not loaded (restart)");
				BccChat.Send(p, $"{Style.Value(kvp.Key)} {Style.Muted($"{t.File}, {FormatLength(t.LengthSeconds)}, {t.SizeBytes / 1024} KB, {t.Group}")} - {state}");
			}
			foreach (string f in BccAudio.PendingFiles()) {
				BccChat.Send(p, $"{Style.Value(f)} - {Style.Muted("new file, loaded after the next server restart")}");
			}
			foreach (string f in BccAudio.UnconvertedFiles()) {
				BccChat.Send(p, $"{Style.Value(f)} - <color=#FFB347>not used: {BccAudio.ConvertHint(f)}</color>");
			}
		}

		public static string FormatLength(double seconds)
		{
			if (seconds <= 0) {
				return "?";
			}
			TimeSpan t = TimeSpan.FromSeconds(seconds);
			return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 100}";
		}

		static void Near(Players.Player p)
		{
			AVec3 pos = BccAudio.ToA(p.PositionCamera);
			List<AudioSourceDef> near = BccAudio.Sources.OrderBy(s => pos.DistanceTo(s.Position)).Take(10).ToList();
			BccChat.Send(p, Style.Header("Nearest audio sources"));
			foreach (AudioSourceDef def in near) {
				float d = pos.DistanceTo(def.Position);
				string audible = d <= BccAudio.EffectiveRange(def) ? Style.Success("in range") : Style.Muted("out of range");
				BccChat.Send(p, $"{Style.Value(def.Name)} {d:0} blocks - {audible} {Style.Muted(BccAudio.Summary(def))}");
			}
			if (near.Count == 0) {
				BccChat.Send(p, Style.Muted("No audio sources yet."));
			}
		}

		static void Info(Players.Player p, AudioSourceDef def)
		{
			BccChat.Send(p, Style.Header($"Audio source {def.Name}") + " " + Style.Muted(BccAudio.StateText(def)));
			List<string> parts = new List<string>();
			foreach (FieldInfo f in Settable()) {
				parts.Add($"{f.Name}={Style.Value(Format(f.GetValue(def)))}");
			}
			BccChat.Send(p, string.Join(", ", parts));
			BccChat.Send(p, Style.Muted(FormattableString.Invariant($"Effective: range {BccAudio.EffectiveRange(def):0.#}, volume {BccAudio.EffectiveVolume(def)} %")));
		}

		static string Format(object value)
		{
			if (value is float f) return f.ToString("0.##", CultureInfo.InvariantCulture);
			return value?.ToString() ?? "";
		}

		// public fields that /audio set may change
		public static List<FieldInfo> Settable()
		{
			return typeof(AudioSourceDef).GetFields(BindingFlags.Public | BindingFlags.Instance).ToList();
		}

		static void Set(Players.Player p, List<string> splits)
		{
			if (splits.Count < 5) {
				BccChat.Send(p, Style.Error("Usage: /audio set <name> <setting> <value> - settings: " + string.Join(", ", Settable().Select(f => f.Name))));
				return;
			}
			AudioSourceDef def = BccAudio.Find(splits[2]);
			if (def == null) {
				BccChat.Send(p, Style.Error($"No audio source named '{splits[2]}'."));
				return;
			}
			string key = splits[3];
			string value = string.Join(" ", splits.Skip(4)).Trim();
			AudioSourceDef before = def.Clone();
			string error = SetField(def, key, value);
			if (error != null) {
				BccChat.Send(p, Style.Error(error));
				return;
			}
			BccAudio.Changed(def, before, p);
			FieldInfo field = Settable().First(f => f.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
			BccChat.Send(p, Style.Success($"{def.Name}: {field.Name} = {Format(field.GetValue(def))}"));
			string note = BccAudio.RangeNote(def);
			if (note != null && (field.Name == nameof(AudioSourceDef.Range) || field.Name == nameof(AudioSourceDef.Spatial))) {
				BccChat.Send(p, Style.Muted(note));
			}
		}

		// shared with the window; returns an error or null
		public static string SetField(AudioSourceDef def, string key, string value)
		{
			FieldInfo field = Settable().FirstOrDefault(f => f.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
			if (field == null) {
				return $"Unknown setting '{key}'. Settings: {string.Join(", ", Settable().Select(f => f.Name))}";
			}
			value = (value ?? "").Trim();
			try {
				if (field.Name == nameof(AudioSourceDef.Name)) {
					string name = TextHelper.StripRichText(value).Trim().Replace(' ', '-');
					if (name.Length == 0) {
						return "The name can't be empty.";
					}
					AudioSourceDef other = BccAudio.Find(name);
					if (other != null && other != def) {
						return $"There is already a source named '{name}'.";
					}
					string oldName = def.Name;
					def.Name = name.Length > 40 ? name.Substring(0, 40) : name;
					// sources synced to this one follow the new name
					foreach (AudioSourceDef s in BccAudio.Sources) {
						if (s != def && string.Equals(s.SyncWith, oldName, StringComparison.OrdinalIgnoreCase)) {
							s.SyncWith = def.Name;
						}
					}
					return null;
				}
				if (field.Name == nameof(AudioSourceDef.SyncWith)) {
					string target = value.Trim();
					if (target.Length == 0 || target == "-" || target.Equals("none", StringComparison.OrdinalIgnoreCase) || target.Equals("off", StringComparison.OrdinalIgnoreCase)) {
						def.SyncWith = "";
						return null;
					}
					AudioSourceDef master = BccAudio.Find(target);
					if (master == null) {
						return $"No audio source named '{target}'.";
					}
					if (master == def) {
						return "A source can't be synced with itself.";
					}
					string before = def.SyncWith;
					def.SyncWith = master.Name;
					AudioEngine.ResolveSyncRoot(def, BccAudio.Find, out string problem);
					if (problem != null) {
						def.SyncWith = before;
						return $"Can't sync with '{master.Name}': {problem}.";
					}
					return null;
				}
				if (field.Name == nameof(AudioSourceDef.Track)) {
					string id = BccAudio.ResolveTrackId(value);
					if (id == null) {
						return $"Unknown track '{value}'. Tracks: {string.Join(", ", BccAudio.PlayableTracks)}";
					}
					def.Track = id;
					return null;
				}
				if (field.FieldType == typeof(string)) {
					field.SetValue(def, value);
				} else if (field.FieldType == typeof(bool)) {
					string v = value.ToLowerInvariant();
					if (v == "true" || v == "on" || v == "yes" || v == "1") field.SetValue(def, true);
					else if (v == "false" || v == "off" || v == "no" || v == "0") field.SetValue(def, false);
					else return $"{field.Name} needs on/off.";
				} else if (field.FieldType == typeof(int)) {
					if (!int.TryParse(value.TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return $"{field.Name} needs a whole number.";
					field.SetValue(def, i);
				} else if (field.FieldType == typeof(float)) {
					if (!float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) || float.IsNaN(f) || float.IsInfinity(f)) return $"{field.Name} needs a number.";
					field.SetValue(def, f);
				} else if (field.FieldType.IsEnum) {
					string match = Enum.GetNames(field.FieldType).FirstOrDefault(n => n.Equals(value, StringComparison.OrdinalIgnoreCase)
						|| (field.FieldType == typeof(AudioSpatial) && value.Equals("3d", StringComparison.OrdinalIgnoreCase) && n == nameof(AudioSpatial.Positional)));
					if (match == null) return $"{field.Name} must be one of: {string.Join(", ", Enum.GetNames(field.FieldType))}";
					field.SetValue(def, Enum.Parse(field.FieldType, match));
				} else {
					return $"{field.Name} can't be set here.";
				}
			} catch (Exception e) {
				return e.Message;
			}
			return null;
		}
	}
}
