using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using log4net;

public class GptntBuffer : MonoBehaviour
{
	private Camera mainCam;
	private Camera screenshotCam;
	private GameObject screenshotObject;
	private RenderTexture screenshotRT;
	private Texture2D readbackTexture;

	private TimedFrameRingBuffer frameBuffer;
	private GptntAudioBuffer audioBuffer;
	private bool isRecording;
	private Coroutine bufferCoroutine;
	private long nextSequence;
	private long epoch;

	private static readonly ILog log = LogManager.GetLogger("Buffer");

	public long Epoch { get { return epoch; } }

	public void Init(int width, int height, int bufferLength, GptntAudioBuffer sourceAudioBuffer)
	{
		screenshotRT = new RenderTexture(width, height, 24);
		readbackTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
		frameBuffer = new TimedFrameRingBuffer(bufferLength);
		audioBuffer = sourceAudioBuffer;

		OpenTelemetrySpan span = new OpenTelemetrySpan("buffer.init");
		span.SetAttribute("width", width);
		span.SetAttribute("height", height);
		span.SetAttribute("bufferLength", bufferLength);
		log.Debug(GptntDebug.FormatMessage("Initialized the Buffer", span.GetTraceId(), span.GetSpanId()));
		span.End(true);
	}

	public void StartBuffer(float frequency)
	{
		DuplicateCamera();
		bufferCoroutine = StartCoroutine(BufferCoroutine(frequency));

		OpenTelemetrySpan span = new OpenTelemetrySpan("buffer.start");
		span.SetAttribute("frequency", frequency);
		log.Debug(GptntDebug.FormatMessage("Started the Buffer", span.GetTraceId(), span.GetSpanId()));
		span.End(true);
	}

	public void StopBuffer()
	{
		isRecording = false;
		if (bufferCoroutine == null)
			return;
		StopCoroutine(bufferCoroutine);
		bufferCoroutine = null;
		OpenTelemetrySpan span = new OpenTelemetrySpan("buffer.stop");
		log.Debug(GptntDebug.FormatMessage("Stopped the Buffer", span.GetTraceId(), span.GetSpanId()));
		span.End(true);
	}

	public void ClearBuffer()
	{
		StopBuffer();
		frameBuffer.Clear();
		epoch++;
		OpenTelemetrySpan span = new OpenTelemetrySpan("buffer.clear");
		span.SetAttribute("epoch", epoch);
		log.Debug(GptntDebug.FormatMessage("Cleared the buffer"));
		span.End(true);
	}

	// Must run on Unity's main thread, after the frame has rendered.
	public TimedFrame CaptureAnchorFrame()
	{
		return CaptureFrame();
	}

	// Must run on Unity's main thread. The returned arrays are immutable copies,
	// so the HTTP worker can safely serialize them after the main thread resumes.
	public TimedRawObservationPayload GetTimedRawBufferData(long? anchorSequence, long? requestedEpoch)
	{
		TimedFrameWindow window = frameBuffer.GetWindow(anchorSequence, requestedEpoch, epoch);
		List<byte[]> rawFrames = new List<byte[]>(window.Frames.Count);
		List<FrameTimingPayload> timing = new List<FrameTimingPayload>(window.Frames.Count);

		foreach (TimedFrame frame in window.Frames)
		{
			rawFrames.Add(frame.Pixels);
			timing.Add(new FrameTimingPayload
			{
				sequence = frame.Sequence,
				epoch = frame.Epoch,
				audioCursor = frame.AudioCursor,
				gameTimeSeconds = frame.GameTimeSeconds,
				realtimeSeconds = frame.RealtimeSeconds,
			});
		}

		return new TimedRawObservationPayload
		{
			rawFrames = rawFrames,
			frameTiming = timing,
			frameHeight = screenshotRT.height,
			frameWidth = screenshotRT.width,
			epoch = epoch,
			anchorRequested = anchorSequence.HasValue,
			anchorIncluded = window.AnchorIncluded,
			coverageGap = window.CoverageGap,
			oldestAvailableSequence = window.OldestAvailableSequence,
			endFrameSequence = window.EndFrameSequence,
		};
	}

	public ObservationPayload GetBufferJSON()
	{
		List<string> frameStrings = new List<string>();
		foreach (TimedFrame frame in frameBuffer.GetAll())
			frameStrings.Add(Convert.ToBase64String(EncodeToPng(frame.Pixels)));
		return new ObservationPayload { frames = frameStrings };
	}

	public RawObservationPayload GetRawBufferData()
	{
		List<byte[]> rawFrames = new List<byte[]>();
		foreach (TimedFrame frame in frameBuffer.GetAll())
			rawFrames.Add(frame.Pixels);
		return new RawObservationPayload
		{
			rawFrames = rawFrames,
			frameHeight = screenshotRT.height,
			frameWidth = screenshotRT.width,
		};
	}

	public byte[] GetLastFrame()
	{
		TimedFrame frame = frameBuffer.GetLastFrame();
		return frame == null ? null : EncodeToPng(frame.Pixels);
	}

	private IEnumerator BufferCoroutine(float frequency)
	{
		if (isRecording)
			yield break;
		isRecording = true;
		WaitForSeconds wait = new WaitForSeconds(frequency);
		while (isRecording)
		{
			yield return new WaitForEndOfFrame();
			CaptureFrame();
			yield return wait;
		}
	}

	private TimedFrame CaptureFrame()
	{
		byte[] pixels = ReadRenderTexture();
		AudioRingBuffer ring = audioBuffer != null ? audioBuffer.Ring : null;
		long audioCursor = ring != null ? ring.GetCursor() : 0;
		TimedFrame frame = new TimedFrame
		{
			Sequence = nextSequence++,
			Epoch = epoch,
			AudioCursor = audioCursor,
			GameTimeSeconds = Time.time,
			RealtimeSeconds = Time.realtimeSinceStartup,
			Pixels = pixels,
		};
		frameBuffer.Add(frame);
		return frame;
	}

	private byte[] ReadRenderTexture()
	{
		RenderTexture previous = RenderTexture.active;
		RenderTexture.active = screenshotRT;
		readbackTexture.ReadPixels(new Rect(0, 0, screenshotRT.width, screenshotRT.height), 0, 0);
		readbackTexture.Apply();
		RenderTexture.active = previous;

		byte[] source = readbackTexture.GetRawTextureData();
		int validBytes = screenshotRT.width * screenshotRT.height * 3;
		byte[] pixels = new byte[validBytes];
		Array.Copy(source, pixels, validBytes);
		return pixels;
	}

	private byte[] EncodeToPng(byte[] pixels)
	{
		Texture2D texture = new Texture2D(screenshotRT.width, screenshotRT.height, TextureFormat.RGB24, false);
		try
		{
			texture.LoadRawTextureData(pixels);
			texture.Apply();
			return texture.EncodeToPNG();
		}
		finally
		{
			Destroy(texture);
		}
	}

	private void DuplicateCamera()
	{
		if (screenshotObject)
			return;

		if (!mainCam)
		{
			mainCam = Camera.main;
			if (!mainCam)
			{
				OpenTelemetrySpan span = new OpenTelemetrySpan("buffer.duplicate_camera_fail");
				span.SetAttribute("error", "Main camera not found");
				span.End(false);
				log.Error(GptntDebug.FormatMessage("Failed to duplicate camera: Main camera not found", span.GetTraceId(), span.GetSpanId()));
				return;
			}
		}

		screenshotObject = new GameObject { name = "ScreenshotCamera" };
		screenshotObject.transform.SetParent(mainCam.transform);
		screenshotObject.transform.localPosition = Vector3.zero;
		screenshotObject.transform.localRotation = Quaternion.identity;
		screenshotObject.transform.localScale = Vector3.one;

		screenshotCam = screenshotObject.AddComponent<Camera>();
		screenshotCam.cullingMask = mainCam.cullingMask;
		screenshotCam.aspect = mainCam.aspect;
		screenshotCam.nearClipPlane = mainCam.nearClipPlane;
		screenshotCam.farClipPlane = mainCam.farClipPlane;
		screenshotCam.fieldOfView = mainCam.fieldOfView;
		screenshotCam.rect = mainCam.rect;
		screenshotCam.depth = mainCam.depth + 1;
		screenshotCam.targetTexture = screenshotRT;
	}
}

public class TimedFrame
{
	public long Sequence;
	public long Epoch;
	public long AudioCursor;
	public float GameTimeSeconds;
	public float RealtimeSeconds;
	public byte[] Pixels;
}

public class TimedFrameWindow
{
	public List<TimedFrame> Frames;
	public bool AnchorIncluded;
	public bool CoverageGap;
	public long OldestAvailableSequence;
	public long EndFrameSequence;
}

public class TimedFrameRingBuffer
{
	private readonly TimedFrame[] buffer;
	private int index;
	private int count;

	public TimedFrameRingBuffer(int size)
	{
		buffer = new TimedFrame[Math.Max(1, size)];
	}

	public void Add(TimedFrame frame)
	{
		buffer[index] = frame;
		index = (index + 1) % buffer.Length;
		count = Math.Min(count + 1, buffer.Length);
	}

	public List<TimedFrame> GetAll()
	{
		List<TimedFrame> frames = new List<TimedFrame>(count);
		int start = (index - count + buffer.Length) % buffer.Length;
		for (int offset = 0; offset < count; offset++)
			frames.Add(buffer[(start + offset) % buffer.Length]);
		return frames;
	}

	public TimedFrameWindow GetWindow(long? anchorSequence, long? requestedEpoch, long currentEpoch)
	{
		List<TimedFrame> all = GetAll();
		List<TimedFrame> selected = new List<TimedFrame>();
		bool epochMismatch = requestedEpoch.HasValue && requestedEpoch.Value != currentEpoch;
		bool anchorIncluded = false;
		bool coverageGap = epochMismatch;

		if (!anchorSequence.HasValue || epochMismatch)
		{
			selected.AddRange(all);
		}
		else
		{
			foreach (TimedFrame frame in all)
			{
				if (frame.Sequence < anchorSequence.Value)
					continue;
				if (frame.Sequence == anchorSequence.Value)
					anchorIncluded = true;
				selected.Add(frame);
			}
			coverageGap = !anchorIncluded;
		}

		long oldest = all.Count == 0 ? -1 : all[0].Sequence;
		long newest = all.Count == 0 ? -1 : all[all.Count - 1].Sequence;
		return new TimedFrameWindow
		{
			Frames = selected,
			AnchorIncluded = anchorIncluded,
			CoverageGap = coverageGap,
			OldestAvailableSequence = oldest,
			EndFrameSequence = newest,
		};
	}

	public TimedFrame GetLastFrame()
	{
		if (count == 0)
			return null;
		return buffer[(index - 1 + buffer.Length) % buffer.Length];
	}

	public void Clear()
	{
		Array.Clear(buffer, 0, buffer.Length);
		index = 0;
		count = 0;
	}
}

public class ObservationPayload
{
	public List<string> frames { get; set; }
	public string segmentation { get; set; }
}

public class RawObservationPayload
{
	public List<byte[]> rawFrames { get; set; }
	public int frameWidth;
	public int frameHeight;
}

public class FrameTimingPayload
{
	public long sequence;
	public long epoch;
	public long audioCursor;
	public float gameTimeSeconds;
	public float realtimeSeconds;
}

public class TimedRawObservationPayload : RawObservationPayload
{
	public List<FrameTimingPayload> frameTiming;
	public long epoch;
	public bool anchorRequested;
	public bool anchorIncluded;
	public bool coverageGap;
	public long oldestAvailableSequence;
	public long endFrameSequence;
}
