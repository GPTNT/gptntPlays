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

	private const int MaxFrames = 16;
	private const float FrameRateMS = 0.25f;
	private const int DefaultAudioBufferSeconds = 30;

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
			gptntBuffer.StartBuffer(FrameRateMS);
			StartCoroutine(HoldBomb());
		};

		string widthEnv = Environment.GetEnvironmentVariable("GAME_WIDTH");
		string heightEnv = Environment.GetEnvironmentVariable("GAME_HEIGHT");

		if (int.TryParse(widthEnv, out int parsedWidth)) screenWidth = parsedWidth;
		if (int.TryParse(heightEnv, out int parsedHeight)) screenHeight = parsedHeight;

		Screen.SetResolution(screenWidth, screenHeight, false);
		gptntBuffer.Init(screenWidth, screenHeight, MaxFrames);
		segmentation.Init(screenWidth, screenHeight);

		string audioSecondsEnv = Environment.GetEnvironmentVariable("AUDIO_BUFFER_SECONDS");
		int audioBufferSeconds = DefaultAudioBufferSeconds;
		if (int.TryParse(audioSecondsEnv, out int parsedAudioSeconds) && parsedAudioSeconds > 0)
			audioBufferSeconds = parsedAudioSeconds;
		gptntAudioBuffer.Init(audioBufferSeconds);
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