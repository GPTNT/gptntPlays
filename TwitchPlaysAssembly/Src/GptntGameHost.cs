using UnityEngine;
using System.Collections.Generic;
using System;
using System.Collections;
using Assets.Scripts.Missions;

public class GptntGameHost : MonoBehaviour
{
	GptntStates gptntStates;

	private int timeStepSize = 250;

	public TwitchBomb bomb;

	// Observation variables 
	private GptntBuffer gptntBuffer;
	private Segmentation segmentation;

	private const int MaxFrames = 16;
	private const float FrameRateMS = 0.25f;

	int screenWidth = 512;
	int screenHeight = 384;

	void Awake()
	{
		gptntBuffer = GetComponent<GptntBuffer>();
		gptntStates = GetComponent<GptntStates>();
		segmentation = GetComponent<Segmentation>();
	}

	private void Start()
	{
		gptntStates.OnGameEnd += OnGameEnd;
		gptntStates.OnReset += gptntBuffer.ClearBuffer;

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
		gptntBuffer.Init(screenHeight, screenHeight, MaxFrames);
		segmentation.Init(screenWidth, screenHeight);
	}

	private IEnumerator HoldBomb()
	{
		yield return new WaitUntil(() => Time.timeScale > 0);
		bomb.Bomb.GetComponent<Selectable>().Trigger();
	}

	private void OnGameEnd()
	{
		gptntBuffer.StopBuffer();
	}

}