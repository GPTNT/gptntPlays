using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

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


	public void Init(int width, int height, int bufferLength)
	{
		screenshotRT = new RenderTexture(width, height, 24);
		textureBuffer = new TextureRingBuffer(bufferLength);
		GptntDebug.Log("[DEBUG] Initialized the Buffer");
		DuplicateCamera();
	}

	public IEnumerator StartBuffer(float frequency)
	{
		if (isRecording) yield break;
		isRecording = true;
		var wait = new WaitForSeconds(frequency);
		while (isRecording)
		{
			GptntDebug.Log("[DEBUG] Adding frame");
			yield return new WaitForEndOfFrame();
			textureBuffer.Add(ConvertRenderTextureToTexture2D(screenshotRT));
			yield return wait;
		}
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

	public void StopBuffer()
	{
		isRecording = false;
		textureBuffer.Reset();
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
				GptntDebug.Log("[OH SHIT...] Main camera not found");
				return;
			}
		}

		// duplicate game object
		screenshotObject = new GameObject();
		screenshotObject.name = "ScreenshotCam";
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

	private Texture2D ConvertRenderTextureToTexture2D(RenderTexture rt)
	{
		Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
		RenderTexture.active = rt;
		tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
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

	public void Reset()
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