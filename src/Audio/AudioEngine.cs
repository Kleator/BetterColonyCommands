using System;
using System.Collections.Generic;
using System.Linq;

namespace ColonyCommands
{
	// What the client is told to play: one prepared clip collection = track + spatial mode + range step
	// + volume step. The game can't change the volume or range of a clip that is already playing, so
	// every track is registered at server start in all range and volume steps; a volume change (fade,
	// distance in Area mode, channel blending) switches to another step at the same track position.
	public struct VoiceSpec : IEquatable<VoiceSpec>
	{
		public string Track;
		public bool Flat;
		public int RangeIndex;
		public int Level;

		public bool Equals(VoiceSpec o) => Flat == o.Flat && RangeIndex == o.RangeIndex && Level == o.Level && string.Equals(Track, o.Track, StringComparison.OrdinalIgnoreCase);
		public override bool Equals(object obj) => obj is VoiceSpec o && Equals(o);
		public override int GetHashCode() => ((Track ?? "").ToLowerInvariant().GetHashCode() * 31 + (Flat ? 1 : 0)) * 397 + RangeIndex * 31 + Level;
		public override string ToString() => $"{Track}/{(Flat ? "flat" : "r" + RangeIndex)}/v{Level}";
	}

	// Implemented by the game glue (packets) and by tests (recording).
	public interface IAudioOutput
	{
		uint NewId();
		// starts a voice, or - with the id of a voice the client already plays - only extends it
		void Play(object listener, uint id, VoiceSpec spec, AVec3 position, int offsetMs, int lengthMs);
		// stops a voice at once (used when switching to another volume step)
		void Cut(object listener, uint id, VoiceSpec spec, AVec3 position);
		// stops a voice with the client's short fade-out
		void FadeStop(object listener, uint id, AVec3 position);
	}

	public class AudioEngineConfig
	{
		public float[] RangeSteps = { 8, 12, 16, 24, 32, 48, 64, 96, 128, 192 };
		public float LevelStepDb = 2.5f;
		public int LevelCount = 17;
		public double KeepAliveSeconds = 8.0;
		// minimum time between two volume steps of one voice (fades, Area distance changes)
		public double SwitchIntervalSeconds = 0.25;
		// leaving the range is only noticed this many blocks beyond it (no flapping at the edge)
		public float LeaveMargin = 6f;
		// channels: with equal priority another source takes over only when it is this much nearer
		public float ChannelSwitchMargin = 4f;
		// finite voices are told to stop this much before the track's real end (the client loops
		// clips, so stopping late would play the first milliseconds again)
		public double EndEpsilon = 0.04;

		public float LevelGain(int level) => (float)Math.Pow(10.0, -level * LevelStepDb / 20.0);

		// -1 = too quiet to be played at all
		public int LevelFor(float gain)
		{
			if (gain >= 1f) {
				return 0;
			}
			if (gain <= 0f || float.IsNaN(gain)) {
				return -1;
			}
			double db = -20.0 * Math.Log10(gain);
			int level = (int)Math.Round(db / LevelStepDb);
			if (level >= LevelCount) {
				// below the quietest step by more than half a step = silent
				return db <= (LevelCount - 0.5) * LevelStepDb ? LevelCount - 1 : -1;
			}
			return level;
		}

		// hysteresis: only leave a volume step when the wanted volume is clearly outside it (3/4 of a step
		// from its centre), so a player standing at the border between two steps doesn't flip between them
		public bool LeavesLevel(float gain, int level)
		{
			if (gain <= 0f || float.IsNaN(gain)) {
				return true;
			}
			double db = -20.0 * Math.Log10(gain);
			return Math.Abs(db - level * LevelStepDb) >= 0.75 * LevelStepDb;
		}

		public int RangeIndexFor(float range)
		{
			int best = 0;
			double bestDiff = double.MaxValue;
			for (int i = 0; i < RangeSteps.Length; i++) {
				double diff = Math.Abs(Math.Log(Math.Max(0.5, RangeSteps[i])) - Math.Log(Math.Max(0.5, range)));
				if (diff < bestDiff) {
					bestDiff = diff;
					best = i;
				}
			}
			return best;
		}
	}

	public struct AudioListenerInfo
	{
		public object Key;
		public AVec3 Position;

		public AudioListenerInfo(object key, AVec3 position)
		{
			Key = key;
			Position = position;
		}
	}

	// Decides per player and source what plays: range, triggers, time condition, fades, loop
	// crossfades and channels. Everything it sends goes to IAudioOutput, only to players in range.
	public class AudioEngine
	{
		public AudioEngineConfig Config = new AudioEngineConfig();
		public IAudioOutput Output;
		// seconds, 0 or less = unknown
		public Func<string, double> TrackLength = t => 0.0;
		public Func<string, bool> TrackPlayable = t => true;

		enum Mode { None, Global, Personal, InRange }

		class Voice
		{
			public uint Id;
			public long Cycle;
			public VoiceSpec Spec;
			public AVec3 Pos;
			public double LastSent;
			public double LastSwitch;
			public double End = double.PositiveInfinity;
		}

		class ListenerState
		{
			public bool InRange;
			public bool InTrigger;
			public double EnteredAt;
			public bool Done;
			public double LastStart = double.NegativeInfinity;
			public double LastFire = double.NegativeInfinity;
			public bool Act;
			public double ActStart;
			public double ActUntil;
			public bool Playing;
			public Mode Mode;
			public double Origin;
			public double Start;
			public double Until;
			public bool Stopping;
			public double StopStart;
			public float Duck = 1f;
			public bool Suppressed;
			public bool JustStarted;
			public float Distance;
			public bool Seen;
			// timeline origin of a playback that ended naturally (never restart that same one)
			public double EndedOrigin = double.NaN;
			public readonly List<Voice> Voices = new List<Voice>();
		}

		class SourceState
		{
			public AudioSourceDef Def;
			public double SharedOrigin;
			public bool GlobalActive;
			public double GlobalStart;
			public double GlobalUntil;
			public double LastFire = double.NegativeInfinity;
			public double NextInterval = double.NaN;
			// source whose timeline this one follows (SyncWith), resolved every tick
			public SourceState Root;
			public readonly Dictionary<object, ListenerState> Listeners = new Dictionary<object, ListenerState>();
		}

		readonly List<SourceState> sources = new List<SourceState>();
		double lastTick = double.NaN;
		float lastHour = float.NaN;
		bool isDay = true;
		double now;

		// ------------------------------------------------------------------ source management

		public void SetSources(IEnumerable<AudioSourceDef> defs, double time)
		{
			List<AudioSourceDef> list = defs.ToList();
			for (int i = sources.Count - 1; i >= 0; i--) {
				if (!list.Contains(sources[i].Def)) {
					StopAll(sources[i]);
					sources.RemoveAt(i);
				}
			}
			foreach (AudioSourceDef def in list) {
				if (Find(def) == null) {
					sources.Add(new SourceState { Def = def, SharedOrigin = time });
				}
			}
		}

		SourceState Find(AudioSourceDef def)
		{
			for (int i = 0; i < sources.Count; i++) {
				if (sources[i].Def == def) {
					return sources[i];
				}
			}
			return null;
		}

		// stops everything of this source and forgets its runtime state (after a bigger change)
		public void Reset(AudioSourceDef def)
		{
			SourceState s = Find(def);
			if (s == null) {
				return;
			}
			StopAll(s);
			s.GlobalActive = false;
			s.NextInterval = double.NaN;
			s.Listeners.Clear();
		}

		void StopAll(SourceState s)
		{
			foreach (KeyValuePair<object, ListenerState> kvp in s.Listeners) {
				StopNow(s, kvp.Key, kvp.Value);
			}
		}

		// a disconnected player: just forget them (their client stopped everything itself)
		public void RemoveListener(object key)
		{
			foreach (SourceState s in sources) {
				s.Listeners.Remove(key);
			}
		}

		public void StopEverything()
		{
			foreach (SourceState s in sources) {
				StopAll(s);
				s.Listeners.Clear();
				s.GlobalActive = false;
			}
		}

		// ------------------------------------------------------------------ triggers

		// Starts a source: for everyone in range, or (audience "triggering player") only for that
		// listener. force = manual start (ignores cooldown and time condition). Returns null or a reason.
		public string Fire(AudioSourceDef def, object listener, double time, bool force)
		{
			SourceState s = Find(def);
			if (s == null) {
				return "unknown source";
			}
			if (!def.Enabled) {
				return "the source is switched off";
			}
			if (!TrackPlayable(def.Track)) {
				return $"track '{def.Track}' is not loaded";
			}
			if (!force && !TimeOk(def)) {
				return def.Time == AudioTimeCondition.Day ? "it only plays during the day" : "it only plays at night";
			}
			double length = TrackLength(def.Track);
			double until = def.DurationSeconds > 0 ? time + def.DurationSeconds : (def.Loop ? double.PositiveInfinity : time + (length > 0 ? length : 3600.0));
			if (listener != null && def.Audience == AudioAudience.TriggeringPlayer) {
				if (!s.Listeners.TryGetValue(listener, out ListenerState l)) {
					l = new ListenerState();
					s.Listeners[listener] = l;
				}
				if (!force && time - l.LastFire < def.CooldownSeconds) {
					return "cooldown";
				}
				l.LastFire = time;
				l.Act = true;
				l.ActStart = time;
				l.ActUntil = until;
				return null;
			}
			if (!force && time - s.LastFire < def.CooldownSeconds) {
				return "cooldown";
			}
			s.LastFire = time;
			s.GlobalActive = true;
			s.GlobalStart = time;
			s.GlobalUntil = until;
			return null;
		}

		// ends all triggered playback of a source (with its fade-out); InRange playback is not affected
		public void StopTriggered(AudioSourceDef def)
		{
			SourceState s = Find(def);
			if (s == null) {
				return;
			}
			s.GlobalActive = false;
			foreach (ListenerState l in s.Listeners.Values) {
				l.Act = false;
			}
		}

		public bool IsTriggeredActive(AudioSourceDef def)
		{
			SourceState s = Find(def);
			return s != null && (s.GlobalActive || s.Listeners.Values.Any(l => l.Act));
		}

		// how many listeners currently hear something of this source
		public int AudibleCount(AudioSourceDef def)
		{
			SourceState s = Find(def);
			return s == null ? 0 : s.Listeners.Values.Count(l => l.Playing && l.Voices.Count > 0);
		}

		public bool IsAudibleFor(AudioSourceDef def, object listener)
		{
			SourceState s = Find(def);
			return s != null && s.Listeners.TryGetValue(listener, out ListenerState l) && l.Playing && l.Voices.Count > 0;
		}

		public int TotalVoices => sources.Sum(s => s.Listeners.Values.Sum(l => l.Voices.Count));

		bool TimeOk(AudioSourceDef def)
		{
			return def.Time == AudioTimeCondition.Always || (def.Time == AudioTimeCondition.Day) == isDay;
		}

		static bool Crossed(float prev, float cur, float threshold)
		{
			if (float.IsNaN(prev)) {
				return false;
			}
			if (cur >= prev) {
				return prev < threshold && threshold <= cur;
			}
			// wrapped past midnight
			return threshold > prev || threshold <= cur;
		}

		// ------------------------------------------------------------------ tick

		public void Tick(double time, IList<AudioListenerInfo> listeners, bool day, float hour, float sunrise, float sunset)
		{
			now = time;
			isDay = day;
			double dt = double.IsNaN(lastTick) ? 0.0 : Math.Max(0.0, time - lastTick);
			lastTick = time;
			float prevHour = lastHour;
			lastHour = hour;
			ResolveSync();

			foreach (SourceState s in sources) {
				TickTriggers(s, prevHour, hour, sunrise, sunset);
				TickDesires(s, listeners);
			}
			ResolveChannels(listeners);
			foreach (SourceState s in sources) {
				foreach (KeyValuePair<object, ListenerState> kvp in s.Listeners) {
					ListenerState l = kvp.Value;
					if (!l.Playing) {
						continue;
					}
					UpdateDuck(s.Def, l, dt);
					UpdateVoices(s, kvp.Key, l, Gain(s.Def, l));
				}
			}
		}

		// ------------------------------------------------------------------ sync (SyncWith)

		readonly Dictionary<string, SourceState> byName = new Dictionary<string, SourceState>(StringComparer.OrdinalIgnoreCase);

		// Follows SyncWith links to the source whose timeline is used. null = not synced, or the link is
		// broken (missing source, loop) - problem says why.
		public static AudioSourceDef ResolveSyncRoot(AudioSourceDef def, Func<string, AudioSourceDef> find, out string problem)
		{
			problem = null;
			if (def == null || string.IsNullOrEmpty(def.SyncWith)) {
				return null;
			}
			List<string> chain = new List<string> { def.Name };
			AudioSourceDef cur = def;
			for (int depth = 0; depth < 16; depth++) {
				if (string.IsNullOrEmpty(cur.SyncWith)) {
					return cur;
				}
				AudioSourceDef next = find(cur.SyncWith);
				if (next == null) {
					problem = $"'{cur.SyncWith}' doesn't exist";
					return null;
				}
				bool seen = chain.Any(n => string.Equals(n, next.Name, StringComparison.OrdinalIgnoreCase));
				chain.Add(next.Name);
				if (seen) {
					problem = "sync loop " + string.Join(" -> ", chain);
					return null;
				}
				cur = next;
			}
			problem = "sync chain too long";
			return null;
		}

		void ResolveSync()
		{
			byName.Clear();
			foreach (SourceState s in sources) {
				if (!string.IsNullOrEmpty(s.Def.Name) && !byName.ContainsKey(s.Def.Name)) {
					byName[s.Def.Name] = s;
				}
			}
			foreach (SourceState s in sources) {
				AudioSourceDef root = ResolveSyncRoot(s.Def, n => byName.TryGetValue(n, out SourceState st) ? st.Def : null, out _);
				s.Root = root != null && byName.TryGetValue(root.Name, out SourceState rs) && rs != s ? rs : null;
			}
		}

		// the root's timeline: its start for everyone while it is triggered, otherwise its shared ("radio") clock
		static double Clock(SourceState root) => root.GlobalActive ? root.GlobalStart : root.SharedOrigin;

		// synced sources use the loop crossfade of the source they follow (it decides where the loop rounds are)
		static float Crossfade(SourceState s) => s.Root != null && s.Root.Def.Loop ? s.Root.Def.CrossfadeSeconds : s.Def.CrossfadeSeconds;

		// start of the loop round that is playing at time 'at' on a timeline starting at 'clock'
		static double RoundStart(double clock, double length, float crossfade, double at)
		{
			if (length <= 0.2 || at <= clock) {
				return clock;
			}
			double period = crossfade > 0 ? length - Math.Min(crossfade, length / 2.0) : length;
			return clock + Math.Floor((at - clock) / period) * period;
		}

		// timeline origin of a synced source for one listener
		double SyncedOrigin(SourceState s, ListenerState l, Mode mode, double length)
		{
			double clock = Clock(s.Root);
			if (s.Def.Loop) {
				return clock;
			}
			// once through: from the current position to the end of the current round, kept while playing
			if (l.Playing && l.Mode == mode) {
				return l.Origin;
			}
			double at = mode == Mode.Global ? s.GlobalStart : (mode == Mode.Personal ? l.ActStart : now);
			return RoundStart(clock, length, Crossfade(s), at);
		}

		public AudioSourceDef SyncRootOf(AudioSourceDef def) => Find(def)?.Root?.Def;

		void TickTriggers(SourceState s, float prevHour, float hour, float sunrise, float sunset)
		{
			AudioSourceDef def = s.Def;
			bool usable = def.Enabled && TrackPlayable(def.Track);
			if (s.GlobalActive && (!usable || !TimeOk(def) || now >= s.GlobalUntil)) {
				s.GlobalActive = false;
			}
			if (!usable) {
				return;
			}
			switch (def.Trigger) {
				case AudioTrigger.Interval:
					if (double.IsNaN(s.NextInterval)) {
						s.NextInterval = now + def.IntervalMinutes * 60.0;
					} else if (now >= s.NextInterval) {
						s.NextInterval = now + def.IntervalMinutes * 60.0;
						Fire(def, null, now, false);
					}
					break;
				case AudioTrigger.Daybreak:
					if (Crossed(prevHour, hour, sunrise)) Fire(def, null, now, false);
					break;
				case AudioTrigger.Nightfall:
					if (Crossed(prevHour, hour, sunset)) Fire(def, null, now, false);
					break;
				case AudioTrigger.TimeOfDay:
					if (Crossed(prevHour, hour, def.Hour)) Fire(def, null, now, false);
					break;
			}
		}

		void TickDesires(SourceState s, IList<AudioListenerInfo> listeners)
		{
			AudioSourceDef def = s.Def;
			bool usable = def.Enabled && TrackPlayable(def.Track);
			bool timeOk = TimeOk(def);
			double length = TrackLength(def.Track);
			AVec3 pos = def.Position;

			foreach (ListenerState l in s.Listeners.Values) {
				l.Seen = false;
			}
			for (int i = 0; i < listeners.Count; i++) {
				AudioListenerInfo info = listeners[i];
				float dist = info.Position.DistanceTo(pos);
				bool known = s.Listeners.TryGetValue(info.Key, out ListenerState l);
				// only keep state for players near the source (or with a running personal trigger)
				if (!known) {
					bool near = dist <= def.Range || (def.Trigger == AudioTrigger.Enter && dist <= def.EffectiveTriggerRadius + Config.LeaveMargin);
					if (!near) {
						continue;
					}
					l = new ListenerState();
					s.Listeners[info.Key] = l;
				}
				l.Seen = true;
				l.Distance = dist;

				bool wasIn = l.InRange;
				l.InRange = wasIn ? dist <= def.Range + Config.LeaveMargin : dist <= def.Range;
				if (l.InRange && !wasIn) {
					l.EnteredAt = now;
				}
				if (!l.InRange && wasIn) {
					l.Done = false;
					if (l.Act && def.Loop && def.DurationSeconds <= 0) {
						l.Act = false;
					}
				}
				float tr = def.EffectiveTriggerRadius;
				bool wasTrig = l.InTrigger;
				l.InTrigger = wasTrig ? dist <= tr + 1f : dist <= tr;
				if (def.Trigger == AudioTrigger.Enter && usable && timeOk && l.InTrigger && !wasTrig) {
					Fire(def, info.Key, now, false);
				}
				if (l.Act && now >= l.ActUntil) {
					l.Act = false;
				}

				// natural end of a track that doesn't loop: the client stops it by itself
				if (l.Playing && !l.Stopping && !def.Loop && length > 0 && now >= l.Origin + length - Config.EndEpsilon) {
					l.Playing = false;
					l.Voices.Clear();
					l.EndedOrigin = l.Origin;
					// played through: an InRange source waits until the player left and came back
					l.Done = true;
					if (l.Mode == Mode.Personal) l.Act = false;
					l.Mode = Mode.None;
				}

				Mode mode = Mode.None;
				double origin = 0, start = 0, until = double.PositiveInfinity;
				if (usable && timeOk && l.InRange) {
					if (s.GlobalActive) {
						mode = Mode.Global;
						origin = s.GlobalStart;
						start = Math.Max(s.GlobalStart, l.EnteredAt);
						until = s.GlobalUntil;
					} else if (l.Act) {
						mode = Mode.Personal;
						origin = start = l.ActStart;
						until = l.ActUntil;
					} else if (def.Trigger == AudioTrigger.InRange) {
						if (def.Loop) {
							mode = Mode.InRange;
							origin = def.Synced ? s.SharedOrigin : l.EnteredAt;
							start = l.EnteredAt;
						} else if (l.Playing && l.Mode == Mode.InRange) {
							mode = Mode.InRange;
							origin = start = l.Origin;
						} else if (!l.Done && now - l.LastStart >= def.CooldownSeconds) {
							mode = Mode.InRange;
							origin = start = now;
						}
					}
				}
				if (mode != Mode.None && s.Root != null) {
					origin = SyncedOrigin(s, l, mode, length);
				}
				if (mode != Mode.None && !l.Playing && Math.Abs(origin - l.EndedOrigin) < 0.001) {
					mode = Mode.None; // this playback already ended
				}
				Apply(s, info.Key, l, mode, origin, start, until, usable);

				// far away and nothing pending: forget the player for this source
				if (!l.Playing && !l.Act && !l.InRange && !l.InTrigger
					&& dist > def.Range + 2f * Config.LeaveMargin && dist > def.EffectiveTriggerRadius + 2f * Config.LeaveMargin
					&& now - Math.Max(l.LastFire, l.LastStart) >= def.CooldownSeconds) {
					s.Listeners.Remove(info.Key);
				}
			}

			// listeners that are gone (disconnected)
			List<object> drop = null;
			foreach (KeyValuePair<object, ListenerState> kvp in s.Listeners) {
				ListenerState l = kvp.Value;
				if (l.Seen) {
					continue;
				}
				if (l.Playing) {
					StopNow(s, kvp.Key, l);
				}
				if (!l.Act || now >= l.ActUntil) {
					(drop ?? (drop = new List<object>())).Add(kvp.Key);
				}
			}
			if (drop != null) {
				foreach (object k in drop) {
					s.Listeners.Remove(k);
				}
			}
		}

		void Apply(SourceState s, object key, ListenerState l, Mode mode, double origin, double start, double until, bool usable)
		{
			AudioSourceDef def = s.Def;
			if (mode != Mode.None) {
				if (!l.Playing || l.Mode != mode || Math.Abs(l.Origin - origin) > 0.001) {
					if (l.Playing) {
						CutAll(key, l);
					}
					l.Playing = true;
					l.JustStarted = true;
					l.Mode = mode;
					l.Origin = origin;
					l.Start = start;
					l.Until = until;
					l.Stopping = false;
					if (mode == Mode.InRange) {
						l.LastStart = now;
					}
				} else {
					l.Until = until;
					if (l.Stopping) {
						// came back while fading out: continue from the current volume upwards
						float g = RampOut(def, l);
						l.Stopping = false;
						l.Start = def.FadeInSeconds > 0 ? now - g * def.FadeInSeconds : now;
					}
				}
				return;
			}
			if (!l.Playing) {
				return;
			}
			if (!l.InRange || !usable) {
				StopNow(s, key, l);
			} else if (!l.Stopping) {
				if (def.FadeOutSeconds > 0) {
					l.Stopping = true;
					l.StopStart = now;
				} else {
					StopNow(s, key, l);
				}
			} else if (RampOut(def, l) <= 0f) {
				StopNow(s, key, l);
			}
		}

		// source heard per player and channel in the last tick
		Dictionary<(object, string), SourceState> lastBest = new Dictionary<(object, string), SourceState>();

		// per player and channel only the best source (highest priority, then nearest) is heard
		void ResolveChannels(IList<AudioListenerInfo> listeners)
		{
			Dictionary<(object, string), SourceState> best = null;
			foreach (SourceState s in sources) {
				if (string.IsNullOrEmpty(s.Def.Channel)) {
					foreach (ListenerState l in s.Listeners.Values) l.Suppressed = false;
					continue;
				}
				foreach (KeyValuePair<object, ListenerState> kvp in s.Listeners) {
					ListenerState l = kvp.Value;
					if (!l.Playing || l.Stopping) {
						continue;
					}
					if (best == null) best = new Dictionary<(object, string), SourceState>();
					var k = (kvp.Key, s.Def.Channel.ToLowerInvariant());
					if (!best.TryGetValue(k, out SourceState cur) || Better(s, cur, kvp.Key)) {
						best[k] = s;
					}
				}
			}
			// hysteresis: with equal priority the source heard so far stays until another one is nearer by
			// ChannelSwitchMargin blocks, so standing between two sources doesn't switch back and forth
			if (best != null) {
				foreach (var k in best.Keys.ToList()) {
					SourceState b = best[k];
					if (lastBest.TryGetValue(k, out SourceState prev) && prev != b && prev.Def.Priority == b.Def.Priority
						&& string.Equals(prev.Def.Channel, k.Item2, StringComparison.OrdinalIgnoreCase)
						&& prev.Listeners.TryGetValue(k.Item1, out ListenerState pl) && pl.Playing && !pl.Stopping
						&& pl.Distance <= b.Listeners[k.Item1].Distance + Config.ChannelSwitchMargin) {
						best[k] = prev;
					}
				}
			}
			lastBest = best ?? new Dictionary<(object, string), SourceState>();
			foreach (SourceState s in sources) {
				if (string.IsNullOrEmpty(s.Def.Channel)) {
					continue;
				}
				foreach (KeyValuePair<object, ListenerState> kvp in s.Listeners) {
					ListenerState l = kvp.Value;
					l.Suppressed = best != null && best.TryGetValue((kvp.Key, s.Def.Channel.ToLowerInvariant()), out SourceState b) && b != s;
				}
			}
			foreach (SourceState s in sources) {
				foreach (ListenerState l in s.Listeners.Values) {
					if (l.JustStarted) {
						l.Duck = l.Suppressed ? 0f : 1f;
						l.JustStarted = false;
					}
				}
			}
		}

		static bool Better(SourceState a, SourceState b, object key)
		{
			if (a.Def.Priority != b.Def.Priority) {
				return a.Def.Priority > b.Def.Priority;
			}
			float da = a.Listeners[key].Distance, db = b.Listeners[key].Distance;
			if (Math.Abs(da - db) > 0.01f) {
				return da < db;
			}
			return string.CompareOrdinal(a.Def.Name, b.Def.Name) < 0;
		}

		void UpdateDuck(AudioSourceDef def, ListenerState l, double dt)
		{
			float target = l.Suppressed ? 0f : 1f;
			if (l.Duck > target) {
				l.Duck = def.FadeOutSeconds > 0 ? Math.Max(target, l.Duck - (float)(dt / def.FadeOutSeconds)) : target;
			} else if (l.Duck < target) {
				l.Duck = def.FadeInSeconds > 0 ? Math.Min(target, l.Duck + (float)(dt / def.FadeInSeconds)) : target;
			}
		}

		float RampOut(AudioSourceDef def, ListenerState l)
		{
			if (!l.Stopping) {
				return 1f;
			}
			if (def.FadeOutSeconds <= 0) {
				return 0f;
			}
			return Clamp01(1f - (float)((now - l.StopStart) / def.FadeOutSeconds));
		}

		float Gain(AudioSourceDef def, ListenerState l)
		{
			float g = def.Volume / 100f;
			if (def.FadeInSeconds > 0) {
				g *= Clamp01((float)((now - l.Start) / def.FadeInSeconds));
			}
			g *= RampOut(def, l);
			g *= l.Duck;
			if (def.Spatial == AudioSpatial.Area) {
				g *= AreaGain(def, l.Distance);
			}
			return g;
		}

		public static float AreaGain(AudioSourceDef def, float dist)
		{
			float inner = Math.Min(def.InnerRadius, def.Range);
			if (dist <= inner) {
				return 1f;
			}
			if (dist >= def.Range) {
				return 0f;
			}
			float t = (dist - inner) / Math.Max(0.001f, def.Range - inner);
			float smooth = t * t * (3f - 2f * t);
			return 1f - smooth;
		}

		static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

		// ------------------------------------------------------------------ voices

		struct Cycle
		{
			public long K;
			public double Start;
			public double End;
			public bool Native;
			public float Xf;
		}

		readonly List<Cycle> cycles = new List<Cycle>(2);

		void UpdateVoices(SourceState s, object key, ListenerState l, float gain)
		{
			AudioSourceDef def = s.Def;
			double length = TrackLength(def.Track);
			AVec3 pos = def.Position;
			cycles.Clear();
			if (!def.Loop) {
				if (length > 0) {
					cycles.Add(new Cycle { K = 0, Start = l.Origin, End = l.Origin + length, Xf = 1f });
				} else {
					cycles.Add(new Cycle { K = 0, Start = l.Origin, End = double.PositiveInfinity, Native = true, Xf = 1f });
				}
			} else if (Crossfade(s) <= 0 || length <= 0.2) {
				cycles.Add(new Cycle { K = 0, Start = l.Origin, End = double.PositiveInfinity, Native = true, Xf = 1f });
			} else {
				double x = Math.Min(Crossfade(s), length / 2.0);
				double period = length - x;
				long kNow = (long)Math.Floor((now - l.Origin) / period);
				for (long k = Math.Max(0, kNow - 1); k <= kNow; k++) {
					double st = l.Origin + k * period;
					double en = st + length;
					if (now < st || now >= en - Config.EndEpsilon) {
						continue;
					}
					// equal power crossfade: constant loudness while both rounds overlap
					float xf = 1f;
					if (k > 0 && now < st + x) {
						xf *= (float)Math.Sin(Clamp01((float)((now - st) / x)) * Math.PI / 2.0);
					}
					if (now > en - x) {
						xf *= (float)Math.Cos(Clamp01((float)((now - (en - x)) / x)) * Math.PI / 2.0);
					}
					cycles.Add(new Cycle { K = k, Start = st, End = en, Xf = xf });
				}
			}

			VoiceSpec baseSpec = new VoiceSpec {
				Track = def.Track,
				Flat = def.Spatial == AudioSpatial.Area,
				RangeIndex = def.Spatial == AudioSpatial.Area ? 0 : Config.RangeIndexFor(def.Range)
			};

			// voices of rounds that are over
			for (int i = l.Voices.Count - 1; i >= 0; i--) {
				Voice v = l.Voices[i];
				bool needed = false;
				foreach (Cycle c in cycles) {
					if (c.K == v.Cycle) {
						needed = true;
						break;
					}
				}
				if (!needed) {
					if (now < v.End - Config.EndEpsilon) {
						Output.Cut(key, v.Id, v.Spec, v.Pos);
					}
					l.Voices.RemoveAt(i);
				}
			}

			foreach (Cycle c in cycles) {
				float voiceGain = gain * c.Xf;
				int level = Config.LevelFor(voiceGain);
				Voice v = null;
				foreach (Voice candidate in l.Voices) {
					if (candidate.Cycle == c.K) {
						v = candidate;
						break;
					}
				}
				if (level < 0) {
					if (v != null) {
						Output.Cut(key, v.Id, v.Spec, v.Pos);
						l.Voices.Remove(v);
					}
					continue;
				}
				VoiceSpec spec = baseSpec;
				spec.Level = level;
				Timing(c, length, out int offsetMs, out int lengthMs);
				if (lengthMs <= 0) {
					continue;
				}
				if (v == null) {
					v = new Voice { Id = Output.NewId(), Cycle = c.K, Spec = spec, Pos = pos, LastSent = now, LastSwitch = now, End = c.End };
					l.Voices.Add(v);
					Output.Play(key, v.Id, spec, pos, offsetMs, lengthMs);
					continue;
				}
				bool moved = !v.Pos.Equals(pos);
				bool otherClip = v.Spec.Flat != spec.Flat || v.Spec.RangeIndex != spec.RangeIndex || !string.Equals(v.Spec.Track, spec.Track, StringComparison.OrdinalIgnoreCase);
				bool otherLevel = v.Spec.Level != spec.Level && now - v.LastSwitch >= Config.SwitchIntervalSeconds && Config.LeavesLevel(voiceGain, v.Spec.Level);
				if (moved || otherClip || otherLevel) {
					// same position in the track, other step: start the new one first, then cut the old one
					uint oldId = v.Id;
					VoiceSpec oldSpec = v.Spec;
					AVec3 oldPos = v.Pos;
					v.Id = Output.NewId();
					v.Spec = spec;
					v.Pos = pos;
					v.LastSent = now;
					v.LastSwitch = now;
					Output.Play(key, v.Id, spec, pos, offsetMs, lengthMs);
					Output.Cut(key, oldId, oldSpec, oldPos);
				} else if (now - v.LastSent >= Config.KeepAliveSeconds / 2.0) {
					// keeps the voice alive; also starts it if the client missed the first packet
					v.LastSent = now;
					Output.Play(key, v.Id, v.Spec, v.Pos, offsetMs, lengthMs);
				}
			}
		}

		void Timing(Cycle c, double length, out int offsetMs, out int lengthMs)
		{
			double offset;
			double len;
			if (c.Native) {
				offset = length > 0 ? Mod(now - c.Start, length) : 0.0;
				len = Config.KeepAliveSeconds;
			} else {
				offset = Math.Max(0.0, now - c.Start);
				len = c.End - Config.EndEpsilon - now;
			}
			offsetMs = (int)Math.Round(offset * 1000.0);
			lengthMs = (int)Math.Floor(Math.Min(len, 1e6) * 1000.0);
		}

		static double Mod(double a, double b)
		{
			double r = a % b;
			return r < 0 ? r + b : r;
		}

		void CutAll(object key, ListenerState l)
		{
			foreach (Voice v in l.Voices) {
				Output.Cut(key, v.Id, v.Spec, v.Pos);
			}
			l.Voices.Clear();
		}

		void StopNow(SourceState s, object key, ListenerState l)
		{
			foreach (Voice v in l.Voices) {
				Output.FadeStop(key, v.Id, v.Pos);
			}
			l.Voices.Clear();
			l.Playing = false;
			l.Stopping = false;
			l.Mode = Mode.None;
		}
	}
}
