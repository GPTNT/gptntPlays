using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using log4net;

public class GptntBuffer : MonoBehaviour
{
	// Cameras
	private Camera mainCam;
	private Camera screenshotCam;

	// Camera game object and reusable GPU/CPU readback resources
	private GameObject screenshotObject;
	private RenderTexture screenshotRT;
	private Texture2D readbackTexture;

	// Ring buffer. A frame owns its pixel array for the rest of its lifetime: once
	// captured, readers may safely treat those bytes as read-only while the ring
	// replaces the slot with a different frame.
	private TimedFrameRingBuffer frameBuffer;
	private GptntAudioBuffer audioBuffer;
	private bool isRecording;

	// Coroutine responsible for periodically adding rendered frames to the ring.
	private Coroutine bufferCoroutine;

	// Sequence numbers identify individual frames for incremental reads. The epoch
	// identifies the mission/reset generation. It prevents a cursor retained by a
	// client from being mistaken for a valid cursor in a later mission.
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
		// Stop first so the capture coroutine cannot add a frame while the retained
		// window is being discarded. Old mission pixels must never leak into the
		// first observation of the next mission.
		StopBuffer();
		frameBuffer.Clear();

		// Frame sequences stay monotonic for the lifetime of the process, while the
		// epoch makes mission boundaries explicit to clients.
		epoch++;
		OpenTelemetrySpan span = new OpenTelemetrySpan("buffer.clear");
		span.SetAttribute("epoch", epoch);
		log.Debug(GptntDebug.FormatMessage("Cleared the buffer"));
		span.End(true);
	}

	// Must run on Unity's main thread, after the frame has rendered. This explicit
	// final frame is the endpoint's linearization point: video, segmentation, and
	// the end of the audio interval are all tied to it.
	public TimedFrame CaptureAnchorFrame()
	{
		return CaptureFrame();
	}

	// Must run on Unity's main thread. Pixel arrays are never mutated after capture;
	// the returned list therefore forms a stable, read-only snapshot that the HTTP
	// worker can serialize after the main thread resumes.
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

		// The absolute audio-sample cursor is our shared AV clock. Unlike wall time,
		// it directly identifies which audio belongs before and after this frame.
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
		// Convert the screenshot RenderTexture into raw RGB24 bytes. Reusing the
		// Texture2D avoids allocating another Unity object for every buffered frame.
		RenderTexture previous = RenderTexture.active;
		RenderTexture.active = screenshotRT;
		readbackTexture.ReadPixels(new Rect(0, 0, screenshotRT.width, screenshotRT.height), 0, 0);
		readbackTexture.Apply();
		RenderTexture.active = previous;

		byte[] source = readbackTexture.GetRawTextureData();

		// Crop any row/mipmap padding Unity may expose. Every wire frame has exactly
		// width * height * 3 bytes, which keeps the binary protocol deterministic.
		int validBytes = screenshotRT.width * screenshotRT.height * 3;
		byte[] pixels = new byte[validBytes];
		Array.Copy(source, pixels, validBytes);
		return pixels;
	}

	private byte[] EncodeToPng(byte[] pixels)
	{
		// Legacy JSON endpoints still require PNG. New observation traffic keeps the
		// raw RGB bytes and avoids this extra encoding work.
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

		// Duplicate the main camera's transform and projection so buffered frames use
		// exactly the viewpoint visible to the player.
		screenshotObject = new GameObject { name = "ScreenshotCamera" };
		screenshotObject.transform.SetParent(mainCam.transform);
		screenshotObject.transform.localPosition = Vector3.zero;
		screenshotObject.transform.localRotation = Quaternion.identity;
		screenshotObject.transform.localScale = Vector3.one;

		// Add the camera and render into our private texture instead of the display.
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
	// These fields describe one indivisible frame. GameTimeSeconds follows
	// Time.timeScale; RealtimeSeconds continues while paused and exposes latency.
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
	// readonly prevents replacing the fixed-capacity backing store after
	// construction. Individual slots are intentionally overwritten as the window
	// advances, keeping memory bounded.
	private readonly TimedFrame[] buffer;
	private int index;
	private int count;

	public TimedFrameRingBuffer(int size)
	{
		buffer = new TimedFrame[Math.Max(1, size)];
	}

	public void Add(TimedFrame frame)
	{
		// Replacing a full slot releases the ring's reference to the oldest frame.
		// Any in-flight snapshot still owns its reference until serialization ends.
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
		return frames; // The final element is the most recently captured frame.
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
			// Include the anchor itself, not only newer frames. This carries the last
			// image seen by the model into the next clip so action effects have context.
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
		// Releasing every reference allows old frame arrays to be collected and
		// guarantees that GetAll cannot return pixels from the previous mission.
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
