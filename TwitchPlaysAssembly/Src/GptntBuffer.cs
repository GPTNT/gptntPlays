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
			textureBuffer.Add(ConvertRenderTextureToTexture2D(screenshotRT));
			yield return wait;
		}
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
		Texture2D tex = new Texture2D(rt.width, rt.height);
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
	private int index = 0;
	private int count = 0;

	public TextureRingBuffer(int size)
	{
		buffer = new Texture2D[size];
	}

	public void Add(Texture2D tex)
	{
		if (buffer[index] != null)
		{
			UnityEngine.Object.Destroy(buffer[index]); // Free memory
		}

		buffer[index] = tex;
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

	public Texture2D GetLastFrame()
	{
		int lastIndex = (index - 1 + buffer.Length) % buffer.Length;
		return buffer[lastIndex];
	}

	public void Clear()
	{
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