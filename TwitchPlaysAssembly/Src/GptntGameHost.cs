using UnityEngine;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System;
using System.Linq;
using System.Collections;
using Assets.Scripts.Missions;
using System.IO;
using Newtonsoft.Json;
using System.Reflection;

public class GptntGameHost : MonoBehaviour
{
	KMBombInfo bombInfo;
	GptntActions gptntActions;
	GptntStates gptntStates;
	BombState lastKnownBombState; // TODO: Move to states

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
		gptntActions = GetComponent<GptntActions>();
		gptntStates = GetComponent<GptntStates>();
		bombInfo = GetComponent<KMBombInfo>();
		segmentation = GetComponent<Segmentation>();
	}

	private void Start()
	{
		bombInfo.OnBombExploded += OnGameEnd;
		bombInfo.OnBombSolved += OnGameEnd;

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
		isStarted = false;
	}

	private void Reset()
	{
		gptntStates.readyToGive = false;
		isStarted = false;
		gptntBuffer.ClearBuffer();
	}

	private void ResetSimon()
	{
		GptntDebug.Log("Resetting Simon");
		SimonComponent simon = FindObjectOfType<SimonComponent>();
		FieldInfo seq = typeof(SimonComponent).GetField("currentSequence", BindingFlags.NonPublic | BindingFlags.Instance);
		FieldInfo progress = typeof(SimonComponent).GetField("solveProgress", BindingFlags.NonPublic | BindingFlags.Instance);
		int[] newSequence = { 1, 1, 1, 1, 1 };
		seq.SetValue(simon, newSequence);
		progress.SetValue(simon, 4);
		simon.StopAllCoroutines();
		simon.PlaySequenceDelay = 1f;
		simon.StartCoroutine("PlaySequence", simon.PlaySequenceDelay);
	}

	private List<Selectable> GetActiveSelectables()
	{
		List<Selectable> activeSelectables = new List<Selectable>();
		SelectableManager selectableManager = KTInputManager.Instance.SelectableManager;
		string parentName = selectableManager.GetCurrentParent().gameObject.name;
		if (parentName.Equals("BasicRectangleBomb(Clone)")) // Level 1
		{
			// Face has no selectables;
		}
		else if (parentName.Equals("FrontFace") || parentName.Equals("RearFace")) // Level 2 
		{
			if (!(gptntActions.bombRotationX == 0f && gptntActions.bombRotationZ.EqualsAny(0f, 180f)))
				return activeSelectables;

			foreach (BombComponent component in bomb.Bomb.BombComponents)
			{
				if (!component.ComponentType.EqualsAny(ComponentTypeEnum.Empty, ComponentTypeEnum.Timer))
				{
					Vector3 componentUp = component.transform.up;
					Vector3 bombUp = bomb.Bomb.transform.up;
					float angleBetween = Vector3.Angle(componentUp, bombUp);
					bool isFront = angleBetween < 90.0f;
					if (isFront == parentName.Equals("FrontFace"))
					{
						activeSelectables.Add(component.GetComponent<Selectable>());
					}
				}
			}
		}
		else // level 3 
		{
			// assume that it is a module and get its selectables
			Selectable parent = selectableManager.GetCurrentParent();
			Selectable[] children = parent.gameObject.GetComponentsInChildren<Selectable>();
			if (children.Length <= 1) return activeSelectables;

			Selectable[] childrenWithoutHead = new Selectable[children.Length - 1];
			Array.Copy(children, 1, childrenWithoutHead, 0, children.Length - 1);
			foreach (Selectable selectable in childrenWithoutHead)
			{
				activeSelectables.Add(selectable);
			}
		}
		return activeSelectables;
	}
}