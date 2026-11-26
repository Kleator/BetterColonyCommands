using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ColonyCommands
{
	// Data of gamedata/savegames/<world>/bcc-audio.json: the audio tracks found in the world's
	// bcc-audio folder and the audio sources placed in the world. Pure data - no game types, so the
	// audio engine can be tested outside the game.

	// "Positional": real 3D sound - louder the closer you are, fades out smoothly up to the range
	// (done by the client, no steps). "Area": same volume everywhere inside the inner radius, fades
	// out between inner radius and range (done by the server in volume steps), not directional.
	[JsonConverter(typeof(StringEnumConverter))]
	public enum AudioSpatial { Positional, Area }

	[JsonConverter(typeof(StringEnumConverter))]
	public enum AudioTrigger
	{
		InRange,    // plays while a player is within range
		Enter,      // starts when a player comes within the trigger radius
		Click,      // starts when a player right-clicks the block at (or directly below) the source
		Join,       // starts when a player joins the server within the trigger radius
		Interval,   // starts every N minutes
		Daybreak,   // starts at sunrise
		Nightfall,  // starts at sunset
		TimeOfDay,  // starts at a given in-game hour
		Manual      // only by /audio play, the window or another mod (BccApi)
	}

	[JsonConverter(typeof(StringEnumConverter))]
	public enum AudioAudience { EveryoneInRange, TriggeringPlayer }

	[JsonConverter(typeof(StringEnumConverter))]
	public enum AudioTimeCondition { Always, Day, Night }

	public class AudioSourceDef
	{
		public string Name = "";
		public string Track = "";
		public float X, Y, Z;
		public bool Enabled = true;

		public AudioSpatial Spatial = AudioSpatial.Positional;
		// audible distance in blocks (Positional: rounded to the prepared range steps)
		public float Range = 32f;
		// Area only: full volume up to this distance, fading out until Range
		public float InnerRadius = 16f;
		// 1..100 % of full volume
		public int Volume = 100;

		public bool Loop = true;
		// seconds the end of the track overlaps the start of the next round (0 = seamless loop)
		public float CrossfadeSeconds = 0f;
		public float FadeInSeconds = 2f;
		public float FadeOutSeconds = 2f;
		// looping InRange sources: everyone hears the same moment of the track (like a radio);
		// off = the track starts from the beginning for every player who comes into range
		public bool Synced = true;

		public AudioTrigger Trigger = AudioTrigger.InRange;
		// Enter / Join: distance that triggers (0 = Range)
		public float TriggerRadius = 0f;
		public float IntervalMinutes = 10f;
		// TimeOfDay: in-game hour 0..24
		public float Hour = 12f;
		public AudioAudience Audience = AudioAudience.EveryoneInRange;
		// 0 = once through the track (no loop) / until stopped (loop)
		public float DurationSeconds = 0f;
		public float CooldownSeconds = 0f;
		public AudioTimeCondition Time = AudioTimeCondition.Always;

		// sources in the same channel never play at the same time for a player: only the one with the
		// highest priority (then the nearest) is heard, the others fade out
		public string Channel = "";
		public int Priority = 0;

		// name of another source whose timeline this one follows: same position in the track, same loop
		// rounds (its loop crossfade), a triggered start joins at that position. Empty = own timeline.
		// Trigger, track, volume, range, fades, ... stay this source's own.
		public string SyncWith = "";

		public AudioSourceDef Clone()
		{
			return (AudioSourceDef)MemberwiseClone();
		}

		public AVec3 Position => new AVec3(X, Y, Z);

		// settings whose change needs the playback to start over (the rest is picked up live)
		public static bool NeedsRestart(AudioSourceDef a, AudioSourceDef b)
		{
			return a.Loop != b.Loop || Math.Abs(a.CrossfadeSeconds - b.CrossfadeSeconds) > 0.001f || a.Synced != b.Synced
				|| a.Trigger != b.Trigger || a.Audience != b.Audience || Math.Abs(a.DurationSeconds - b.DurationSeconds) > 0.001f
				|| a.Spatial != b.Spatial || !string.Equals(a.Track, b.Track, StringComparison.OrdinalIgnoreCase)
				|| !string.Equals(a.SyncWith ?? "", b.SyncWith ?? "", StringComparison.OrdinalIgnoreCase);
		}

		public void Normalize()
		{
			Name = (Name ?? "").Trim();
			Track = (Track ?? "").Trim();
			Channel = (Channel ?? "").Trim();
			SyncWith = (SyncWith ?? "").Trim();
			if (SyncWith.Equals(Name, StringComparison.OrdinalIgnoreCase)) SyncWith = "";
			Range = Clamp(Range, 1f, 4096f);
			InnerRadius = Clamp(InnerRadius, 0f, Range);
			Volume = Math.Max(1, Math.Min(100, Volume));
			CrossfadeSeconds = Clamp(CrossfadeSeconds, 0f, 600f);
			FadeInSeconds = Clamp(FadeInSeconds, 0f, 600f);
			FadeOutSeconds = Clamp(FadeOutSeconds, 0f, 600f);
			TriggerRadius = Clamp(TriggerRadius, 0f, 4096f);
			IntervalMinutes = Clamp(IntervalMinutes, 0.1f, 100000f);
			Hour = ((Hour % 24f) + 24f) % 24f;
			DurationSeconds = Clamp(DurationSeconds, 0f, 1000000f);
			CooldownSeconds = Clamp(CooldownSeconds, 0f, 1000000f);
			if (float.IsNaN(X) || float.IsInfinity(X)) X = 0f;
			if (float.IsNaN(Y) || float.IsInfinity(Y)) Y = 0f;
			if (float.IsNaN(Z) || float.IsInfinity(Z)) Z = 0f;
		}

		static float Clamp(float v, float min, float max)
		{
			if (float.IsNaN(v)) return min;
			return v < min ? min : (v > max ? max : v);
		}

		public float EffectiveTriggerRadius => TriggerRadius > 0f ? TriggerRadius : Range;
	}

	public class AudioTrackSettings
	{
		// file name inside the bcc-audio folder
		public string File = "";
		// mixer group = which of the player's volume sliders applies: Ambient, Effects or Master
		public string Group = "Ambient";
		// Auto (tracks over 5 minutes streamed, shorter ones compressed in memory), Streamed, Compressed, Raw
		public string LoadType = "Auto";
		// measured at server start (information only)
		public double LengthSeconds;
		public long SizeBytes;
		public int Channels;
		public int SampleRate;
		// file not found or not a valid Ogg Vorbis file at the last start
		public bool Missing;
		public string Problem = "";
	}

	public class AudioFileData
	{
		public int Version = 1;
		public Dictionary<string, AudioTrackSettings> Tracks = new Dictionary<string, AudioTrackSettings>(StringComparer.OrdinalIgnoreCase);
		public List<AudioSourceDef> Sources = new List<AudioSourceDef>();
	}

	public struct AVec3 : IEquatable<AVec3>
	{
		public float X, Y, Z;

		public AVec3(float x, float y, float z)
		{
			X = x; Y = y; Z = z;
		}

		public float DistanceTo(AVec3 o)
		{
			float dx = X - o.X, dy = Y - o.Y, dz = Z - o.Z;
			return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
		}

		public bool Equals(AVec3 o) => X == o.X && Y == o.Y && Z == o.Z;
		public override bool Equals(object obj) => obj is AVec3 o && Equals(o);
		public override int GetHashCode() => (X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode();
		public override string ToString() => $"{X:0.#} {Y:0.#} {Z:0.#}";
	}
}
