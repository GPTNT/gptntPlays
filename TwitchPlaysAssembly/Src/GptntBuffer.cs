using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using log4net;

public class GptntBuffer : MonoBehaviour
{
	// Cameras 
	private Camera mainCam = null;
	private Camera screenshotCam = null;

	// Camera game objects
	private GameObject screenshotObject = null;
	private RenderTexture screenshotRT;

	// Ring Buffer
	private TextureRingBuffer textureBuffer;
	private bool isRecording = false;
	private bool feedbackEnabled;
	private GptntFeedback feedbackCapture;
	private Camera feedbackRgbCamera;
	private long feedbackSequence;
	private long feedbackEpoch;

	public void SetFeedbackEnabled(bool enabled)
	{
		feedbackEnabled = enabled;
		if (feedbackCapture != null) feedbackCapture.ResetTarget();
	}

	private void OnDestroy()
	{
		if (feedbackCapture != null) feedbackCapture.Dispose();
		if (feedbackRgbCamera) UnityEngine.Object.Destroy(feedbackRgbCamera.gameObject);
	}

	// Coroutine
	private Coroutine bufferCoroutine;

	private static ILog log = LogManager.GetLogger("Buffer");

	public void Init(int width, int height, int bufferLength)
	{
		screenshotRT = new RenderTexture(width, height, 24);
		textureBuffer = new TextureRingBuffer(bufferLength);

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
		textureBuffer.Clear();
		feedbackEpoch++;
		if (feedbackCapture != null) feedbackCapture.ResetTarget();
		OpenTelemetrySpan span = new OpenTelemetrySpan("buffer.clear");
		log.Debug(GptntDebug.FormatMessage("Cleared the buffer"));
		span.End(true);
	}

	public ObservationPayload GetBufferJSON()
	{
		List<string> frameStrings = new List<string>();
		foreach (Texture2D frame in textureBuffer.GetLastFrames())
		{
			byte[] frameBytes = frame.EncodeToPNG();
			frameStrings.Add(Convert.ToBase64String(frameBytes));
		}

		return new ObservationPayload { frames = frameStrings };
	}

	public RawObservationPayload GetRawBufferData()
	{
		List<byte[]> rawFrames = new List<byte[]>();
		foreach (Texture2D frame in textureBuffer.GetLastFrames())
		{
			byte[] frameBytes = frame.GetRawTextureData();
			// crop out the padding added by unity for the gpu
			int validBytes = frame.width * frame.height * 3;
			byte[] trimmed = new byte[validBytes];
			Array.Copy(frameBytes, trimmed, validBytes);
			rawFrames.Add(trimmed);
		}
		return new RawObservationPayload { rawFrames = rawFrames, frameHeight = screenshotRT.height, frameWidth = screenshotRT.width };
	}

	public byte[] GetLastFrame()
	{
		return textureBuffer.GetLastFrame().EncodeToPNG();
	}

	private IEnumerator BufferCoroutine(float frequency)
	{
		if (isRecording) yield break;
		isRecording = true;
		var wait = new WaitForSeconds(frequency);
		while (isRecording)
		{
			yield return new WaitForEndOfFrame();
			FeedbackFrame feedback = null;
			Texture2D texture = feedbackEnabled
				? CaptureFeedbackFrame(out feedback)
				: ConvertRenderTextureToTexture2D(screenshotRT);
			textureBuffer.Add(texture, feedback);
			yield return wait;
		}
	}

	public FeedbackObservationSnapshot CaptureFeedbackObservation()
	{
		if (!feedbackEnabled)
			throw new InvalidOperationException("Start the mission with feedbackVersion=1 before reading feedback");
		FeedbackFrame currentFeedback;
		Texture2D current = CaptureFeedbackFrame(out currentFeedback);
		try
		{
			int length = screenshotRT.width * screenshotRT.height * 3;
			byte[] currentImage = new byte[length];
			Array.Copy(current.GetRawTextureData(), currentImage, length);
			FeedbackObservationSnapshot snapshot = new FeedbackObservationSnapshot
			{
				Frames = GetRawBufferData(), FrameFeedback = textureBuffer.GetFeedbackFrames(), Epoch = feedbackEpoch,
				CurrentImage = currentImage, CurrentImageFeedback = currentFeedback,
			};
			// The fresh capture is the sixteenth observation image.
			int remove = Math.Max(0, snapshot.Frames.rawFrames.Count - 15);
			snapshot.Frames.rawFrames.RemoveRange(0, remove);
			snapshot.FrameFeedback.RemoveRange(0, remove);
			return snapshot;
		}
		finally { UnityEngine.Object.Destroy(current); }
	}

	private Texture2D CaptureFeedbackFrame(out FeedbackFrame feedback)
	{
		DuplicateCamera();
		if (!mainCam || !screenshotCam)
			throw new InvalidOperationException("RGB camera unavailable for feedback capture");
		// RGB and geometry render on one main-thread call without advancing the scene.
		if (!feedbackRgbCamera)
		{
			GameObject host = new GameObject("FeedbackRgbCamera");
			feedbackRgbCamera = host.AddComponent<Camera>();
			feedbackRgbCamera.enabled = false;
		}
		feedbackRgbCamera.CopyFrom(mainCam);
		feedbackRgbCamera.enabled = false;
		feedbackRgbCamera.transform.position = mainCam.transform.position;
		feedbackRgbCamera.transform.rotation = mainCam.transform.rotation;
		feedbackRgbCamera.targetTexture = screenshotRT;
		feedbackRgbCamera.useOcclusionCulling = false;
		feedbackRgbCamera.Render();
		Texture2D texture = ConvertRenderTextureToTexture2D(screenshotRT);
		if (feedbackCapture == null) feedbackCapture = new GptntFeedback();
		Segmentation segmentation = GetComponent<Segmentation>();
		feedback = feedbackCapture.Capture(feedbackRgbCamera, segmentation ? segmentation.shader : null,
			++feedbackSequence, feedbackEpoch, screenshotRT.width, screenshotRT.height);
		return texture;
	}

	// Helper functions
	private void DuplicateCamera()
	{
		if (screenshotObject) return;

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

		// duplicate game object
		screenshotObject = new GameObject();
		screenshotObject.name = "ScreenshotCamera";
		screenshotObject.transform.SetParent(mainCam.transform);
		screenshotObject.transform.localPosition = Vector3.zero;
		screenshotObject.transform.localRotation = Quaternion.identity;
		screenshotObject.transform.localScale = Vector3.one;

		// Adding camera
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

	// Convert a RenderTexture to a Texture2D
	private Texture2D ConvertRenderTextureToTexture2D(RenderTexture rt)
	{
		RenderTexture.active = rt;
		Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, mipmap: true);
		Rect rect = new Rect(0, 0, rt.width, rt.height);
		tex.ReadPixels(rect, 0, 0);
		tex.Apply();
		RenderTexture.active = null;
		return tex;
	}
}

public class TextureRingBuffer
{
	private readonly Texture2D[] buffer;
	private readonly FeedbackFrame[] feedback;
	private int index = 0;
	private int count = 0;

	public TextureRingBuffer(int size)
	{
		buffer = new Texture2D[size];
		feedback = new FeedbackFrame[size];
	}

	public void Add(Texture2D tex, FeedbackFrame frameFeedback = null)
	{
		if (buffer[index] != null)
		{
			UnityEngine.Object.Destroy(buffer[index]); // Free memory
		}

		buffer[index] = tex;
		feedback[index] = frameFeedback;
		index = (index + 1) % buffer.Length;
		count = Math.Min(count + 1, buffer.Length);
	}

	public List<Texture2D> GetLastFrames()
	{
		List<Texture2D> frames = new List<Texture2D>(count);
		int start = (index - count + buffer.Length) % buffer.Length;

		for (int i = 0; i < count; i++)
		{
			int pos = (start + i) % buffer.Length;
			frames.Add(buffer[pos]);
		}

		return frames; // the last element in the list is the most recent
	}

	public List<FeedbackFrame> GetFeedbackFrames()
	{
		List<FeedbackFrame> frames = new List<FeedbackFrame>(count);
		int start = (index - count + buffer.Length) % buffer.Length;
		for (int i = 0; i < count; i++)
			frames.Add(feedback[(start + i) % buffer.Length]);
		return frames;
	}

	public Texture2D GetLastFrame()
	{
		int lastIndex = (index - 1 + buffer.Length) % buffer.Length;
		return buffer[lastIndex];
	}

	public void Clear()
	{
		Array.Clear(feedback, 0, feedback.Length);
		foreach (Texture2D item in buffer)
		{
			if (item != null)
				UnityEngine.Object.Destroy(item);
			index = 0;
			count = 0;
		}
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