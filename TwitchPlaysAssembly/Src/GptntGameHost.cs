using UnityEngine;
using System;
using System.Collections;

public class GptntGameHost : MonoBehaviour
{
	GptntStates gptntStates;

	// Observation variables
	private GptntBuffer gptntBuffer;
	private Segmentation segmentation;
	private GptntAudioBuffer gptntAudioBuffer;

	private const int DefaultVideoBufferSeconds = 10;
	private const float DefaultVideoCaptureFps = 4f;
	private const int DefaultAudioBufferSeconds = 30;
	private float frameIntervalSeconds = 1f / DefaultVideoCaptureFps;

	int screenWidth = 512;
	int screenHeight = 384;

	void Awake()
	{
		gptntBuffer = GetComponent<GptntBuffer>();
		gptntStates = GetComponent<GptntStates>();
		segmentation = GetComponent<Segmentation>();
		gptntAudioBuffer = gameObject.AddComponent<GptntAudioBuffer>();
	}

	private void Start()
	{
		gptntStates.OnGameEnd += OnGameEnd;
		gptntStates.OnReset += gptntBuffer.ClearBuffer;
		gptntStates.OnReset += gptntAudioBuffer.Clear;

		gptntStates.OnFirstLightsOn += () =>
		{
			gptntBuffer.StartBuffer(frameIntervalSeconds);
			StartCoroutine(HoldBomb());
		};

		string widthEnv = Environment.GetEnvironmentVariable("GAME_WIDTH");
		string heightEnv = Environment.GetEnvironmentVariable("GAME_HEIGHT");

		if (int.TryParse(widthEnv, out int parsedWidth)) screenWidth = parsedWidth;
		if (int.TryParse(heightEnv, out int parsedHeight)) screenHeight = parsedHeight;

		Screen.SetResolution(screenWidth, screenHeight, false);
		string audioSecondsEnv = Environment.GetEnvironmentVariable("AUDIO_BUFFER_SECONDS");
		int audioBufferSeconds = DefaultAudioBufferSeconds;
		if (int.TryParse(audioSecondsEnv, out int parsedAudioSeconds) && parsedAudioSeconds > 0)
			audioBufferSeconds = parsedAudioSeconds;
		gptntAudioBuffer.Init(audioBufferSeconds);

		string videoSecondsEnv = Environment.GetEnvironmentVariable("VIDEO_BUFFER_SECONDS");
		int videoBufferSeconds = DefaultVideoBufferSeconds;
		if (int.TryParse(videoSecondsEnv, out int parsedVideoSeconds) && parsedVideoSeconds > 0)
			videoBufferSeconds = parsedVideoSeconds;

		string videoFpsEnv = Environment.GetEnvironmentVariable("VIDEO_CAPTURE_FPS");
		float videoCaptureFps = DefaultVideoCaptureFps;
		if (float.TryParse(videoFpsEnv, out float parsedVideoFps) && parsedVideoFps > 0f)
			videoCaptureFps = parsedVideoFps;
		frameIntervalSeconds = 1f / videoCaptureFps;

		int videoBufferFrames = Math.Max(1, (int) Math.Ceiling(videoBufferSeconds * videoCaptureFps));
		gptntBuffer.Init(screenWidth, screenHeight, videoBufferFrames, gptntAudioBuffer);
		segmentation.Init(screenWidth, screenHeight);
	}

	private IEnumerator HoldBomb()
	{
		yield return new WaitUntil(() => Time.timeScale > 0);
		gptntStates.bomb.GetComponent<Selectable>().Trigger();
	}

	private void OnGameEnd()
	{
		gptntBuffer.StopBuffer();
	}

}
