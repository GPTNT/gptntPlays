using System;
using System.IO;
using UnityEngine;
using log4net;

/// <summary>
/// Lock-guarded mono 16-bit PCM ring buffer. Written from the FMOD audio thread
/// (via <see cref="AudioListenerTap"/>) and read from the HTTP worker thread.
/// The absolute sample cursor is monotonic and never resets, so stale client
/// cursors are always safe to clamp.
/// </summary>
public class AudioRingBuffer
{
	private readonly short[] buffer;
	private long writeCursor; // absolute samples written since startup; never resets
	private int validCount;   // samples currently readable (reset by Clear)
	private readonly object sync = new object();

	public readonly int SampleRate;

	public AudioRingBuffer(int sampleRate, int capacitySeconds)
	{
		SampleRate = sampleRate;
		int capacity = Math.Max(1, sampleRate * Math.Max(1, capacitySeconds));
		buffer = new short[capacity];
	}

	// Audio thread. Must not call Unity APIs or allocate.
	public void Write(float[] interleaved, int channels)
	{
		if (channels <= 0) channels = 1;
		int frames = interleaved.Length / channels;

		lock (sync)
		{
			for (int f = 0; f < frames; f++)
			{
				float sum = 0f;
				int baseIndex = f * channels;
				for (int c = 0; c < channels; c++)
					sum += interleaved[baseIndex + c];
				float mono = sum / channels;

				if (mono > 1f) mono = 1f;
				else if (mono < -1f) mono = -1f;

				int pos = (int) (writeCursor % buffer.Length);
				buffer[pos] = (short) (mono * 32767f);
				writeCursor++;
			}

			if (validCount < buffer.Length)
				validCount = (int) Math.Min((long) buffer.Length, validCount + frames);
		}
	}

	// HTTP thread. Returns everything written after <paramref name="cursor"/>.
	// If the cursor is older than the retained window it is clamped and the gap
	// is reported via <paramref name="dropped"/>.
	public short[] ReadSince(long cursor, out long newCursor, out long dropped)
	{
		lock (sync)
		{
			newCursor = writeCursor;
			long oldest = writeCursor - validCount;
			dropped = 0;

			if (cursor < oldest)
			{
				dropped = oldest - cursor;
				cursor = oldest;
			}
			else if (cursor > writeCursor)
			{
				cursor = writeCursor;
			}

			int count = (int) (writeCursor - cursor);
			return CopyRange(cursor, count);
		}
	}

	// HTTP thread. Returns the most recent <paramref name="sampleCount"/> samples
	// (clamped to what is retained).
	public short[] ReadLast(int sampleCount, out long newCursor)
	{
		lock (sync)
		{
			newCursor = writeCursor;
			if (sampleCount < 0) sampleCount = 0;
			int count = Math.Min(sampleCount, validCount);
			long start = writeCursor - count;
			return CopyRange(start, count);
		}
	}

	// Caller must hold sync. start is an absolute cursor within the retained window.
	private short[] CopyRange(long start, int count)
	{
		short[] result = new short[count];
		for (int i = 0; i < count; i++)
		{
			int pos = (int) ((start + i) % buffer.Length);
			result[i] = buffer[pos];
		}
		return result;
	}

	public long GetCursor()
	{
		lock (sync) { return writeCursor; }
	}

	// Empties the retained window without resetting the monotonic cursor.
	public void Clear()
	{
		lock (sync) { validCount = 0; }
	}
}

/// <summary>
/// Attached at runtime to the GameObject that carries the active AudioListener.
/// Receives the final mixed output on the audio thread and forwards it to the ring.
/// </summary>
public class AudioListenerTap : MonoBehaviour
{
	public AudioRingBuffer Target;

	private void OnAudioFilterRead(float[] data, int channels)
	{
		AudioRingBuffer target = Target;
		if (target == null || !GptntAudioBuffer.CaptureEnabled)
			return;
		target.Write(data, channels);
	}
}

/// <summary>
/// Manager component. Owns the ring buffer, keeps the tap attached across scene
/// transitions (the AudioListener is scene-owned and dies on scene changes),
/// and serializes ring slices to WAV.
/// </summary>
public class GptntAudioBuffer : MonoBehaviour
{
	// Read from the audio thread; gates out audio emitted while the game is paused.
	public static volatile bool CaptureEnabled = true;

	private AudioRingBuffer ring;
	private AudioListenerTap tap;

	private static ILog log = LogManager.GetLogger("AudioBuffer");

	public AudioRingBuffer Ring { get { return ring; } }

	public void Init(int bufferSeconds)
	{
		int sampleRate = AudioSettings.outputSampleRate;
		ring = new AudioRingBuffer(sampleRate, bufferSeconds);

		OpenTelemetrySpan span = new OpenTelemetrySpan("audio.init");
		span.SetAttribute("sampleRate", sampleRate);
		span.SetAttribute("bufferSeconds", bufferSeconds);
		log.Debug(GptntDebug.FormatMessage("Initialized the audio buffer", span.GetTraceId(), span.GetSpanId()));
		span.End(true);
	}

	private void Update()
	{
		if (ring == null || tap != null)
			return;

		AudioListener listener = FindObjectOfType<AudioListener>();
		if (listener == null)
			return;

		tap = listener.gameObject.AddComponent<AudioListenerTap>();
		tap.Target = ring;
		log.Debug(GptntDebug.FormatMessage("Attached audio tap to " + listener.gameObject.name));
	}

	public void Clear()
	{
		if (ring != null)
			ring.Clear();
	}

	// Standard 44-byte RIFF/PCM header + little-endian mono 16-bit samples.
	public static byte[] ToWav(short[] samples, int sampleRate)
	{
		int dataBytes = samples.Length * 2;
		using (MemoryStream stream = new MemoryStream(44 + dataBytes))
		using (BinaryWriter writer = new BinaryWriter(stream))
		{
			const short channels = 1;
			const short bitsPerSample = 16;
			int byteRate = sampleRate * channels * (bitsPerSample / 8);
			short blockAlign = (short) (channels * (bitsPerSample / 8));

			writer.Write(new char[] { 'R', 'I', 'F', 'F' });
			writer.Write(36 + dataBytes);
			writer.Write(new char[] { 'W', 'A', 'V', 'E' });

			writer.Write(new char[] { 'f', 'm', 't', ' ' });
			writer.Write(16);            // PCM fmt chunk size
			writer.Write((short) 1);     // audio format = PCM
			writer.Write(channels);
			writer.Write(sampleRate);
			writer.Write(byteRate);
			writer.Write(blockAlign);
			writer.Write(bitsPerSample);

			writer.Write(new char[] { 'd', 'a', 't', 'a' });
			writer.Write(dataBytes);
			for (int i = 0; i < samples.Length; i++)
				writer.Write(samples[i]);

			writer.Flush();
			return stream.ToArray();
		}
	}
}
