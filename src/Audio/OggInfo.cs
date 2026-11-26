using System;
using System.IO;
using System.Text;

namespace ColonyCommands
{
	// Reads length, channels and sample rate of an Ogg Vorbis file (the only format Colony Survival
	// plays): sample rate from the Vorbis identification header on the first page, number of samples
	// from the granule position of the stream's last page.
	public static class OggInfo
	{
		public class Result
		{
			public bool Ok;
			public string Problem = "";
			public int Channels;
			public int SampleRate;
			public long Samples;
			public long SizeBytes;
			public double LengthSeconds => SampleRate > 0 ? (double)Samples / SampleRate : 0.0;
		}

		public static Result Read(string path)
		{
			Result r = new Result();
			try {
				using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
					r.SizeBytes = fs.Length;
					if (fs.Length < 64) {
						r.Problem = "file too small";
						return r;
					}
					byte[] head = new byte[(int)Math.Min(fs.Length, 8192)];
					ReadFully(fs, head, head.Length);
					if (head[0] != 'O' || head[1] != 'g' || head[2] != 'g' || head[3] != 'S') {
						r.Problem = "not an Ogg file (convert it to .ogg / Vorbis)";
						return r;
					}
					uint serial = BitConverter.ToUInt32(head, 14);
					int segments = head[26];
					int payload = 27 + segments;
					if (payload + 30 > head.Length) {
						r.Problem = "broken Ogg header";
						return r;
					}
					if (Match(head, payload, "OpusHead")) {
						r.Problem = "Opus audio is not supported by the game - convert it to Ogg Vorbis";
						return r;
					}
					if (head[payload] != 1 || !Match(head, payload + 1, "vorbis")) {
						r.Problem = "not Ogg Vorbis audio (convert it to Ogg Vorbis)";
						return r;
					}
					r.Channels = head[payload + 11];
					r.SampleRate = (int)BitConverter.ToUInt32(head, payload + 12);
					if (r.SampleRate <= 0 || r.Channels <= 0) {
						r.Problem = "invalid Vorbis header";
						return r;
					}
					r.Samples = LastGranule(fs, serial);
					if (r.Samples <= 0) {
						r.Problem = "could not determine the length";
						return r;
					}
					r.Ok = true;
				}
			} catch (Exception e) {
				r.Problem = e.Message;
			}
			return r;
		}

		static bool Match(byte[] data, int offset, string text)
		{
			if (offset + text.Length > data.Length) {
				return false;
			}
			for (int i = 0; i < text.Length; i++) {
				if (data[offset + i] != text[i]) {
					return false;
				}
			}
			return true;
		}

		static void ReadFully(Stream s, byte[] buffer, int count)
		{
			int read = 0;
			while (read < count) {
				int n = s.Read(buffer, read, count - read);
				if (n <= 0) {
					throw new EndOfStreamException();
				}
				read += n;
			}
		}

		// granule position of the last page of the stream that has one (searching from the end)
		static long LastGranule(FileStream fs, uint serial)
		{
			const int CHUNK = 65536;
			long end = fs.Length;
			byte[] buffer = new byte[CHUNK + 27];
			while (end > 0) {
				long start = Math.Max(0, end - CHUNK);
				int len = (int)Math.Min(end - start + 27, fs.Length - start);
				fs.Position = start;
				ReadFully(fs, buffer, len);
				for (int i = len - 27; i >= 0; i--) {
					if (buffer[i] == 'O' && buffer[i + 1] == 'g' && buffer[i + 2] == 'g' && buffer[i + 3] == 'S' && buffer[i + 4] == 0) {
						long granule = BitConverter.ToInt64(buffer, i + 6);
						uint pageSerial = BitConverter.ToUInt32(buffer, i + 14);
						if (pageSerial == serial && granule > 0) {
							return granule;
						}
					}
				}
				if (start == 0) {
					break;
				}
				end = start;
			}
			return -1;
		}
	}
}
