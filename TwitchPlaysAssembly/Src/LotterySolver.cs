using System;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TwitchPlaysAssembly;
using log4net;
using System.Collections;

public class LotterySolver : MonoBehaviour
{
	List<string> rotationActions = new List<string> { "left", "right", "up", "down", "flip" };
	string click = "click";
	string hold = "hold";
	string release = "release";
	string zoomOut = "out";
	System.Random random;
	GptntActions gptntActions;
	GptntStates gptntStates;
	private ILog log = LogManager.GetLogger("Lottery");

	private void Start()
	{
		gptntStates = GetComponent<GptntStates>();
		gptntStates.OnReset += () => random = new System.Random();
		random = new System.Random();
		gptntActions = GetComponent<GptntActions>();
	}

	private void GptntStates_OnReset() => Start();

	public string Scratch(List<Selectable> activeSelectables)
	{
		// I need all possible actions
		// And a refernce to the previous action
		InputInterceptor.DisableInput();
		List<string> allActions = AllPossibleActions(activeSelectables);
		string debug = "[";
		foreach (var action in allActions)
		{
			debug += action;
			debug += ", ";
		}
		log.Debug("All possible: " + debug + "]");
		string randomAction = allActions[random.Next(allActions.Count)];
		log.Debug(randomAction);

		if (randomAction.Contains(click) || randomAction.Contains(hold))
		{
			string[] split = randomAction.Split('_');
			randomAction = split[0];
			int index = Int32.Parse(split[1]);
			activeSelectables[index].HandleInteract();
			if (randomAction.Equals(click))
				Click(activeSelectables[index]);
			else
				Hold(activeSelectables[index]);
		}

		else if (rotationActions.Contains(randomAction))
		{
			log.Debug(Rotate(randomAction));
		}

		else if (randomAction.Equals(release))
		{
			gptntActions.Release();
		}

		else if (randomAction.Equals(zoomOut))
		{
			gptntActions.ZoomOut();
		}

		return "Did: " + randomAction;
	}

	private string Rotate(string direction)
	{
		if (direction.Equals("flip"))
		{
			StartCoroutine(gptntActions.Rotate180());
			return "flipped bomb 180 degrees";
		}
		else if (direction.Equals("left"))
		{
			StartCoroutine(gptntActions.Rotate90(direction));
			return "flipped bomb left by 90 degrees";
		}
		else if (direction.Equals("right"))
		{
			StartCoroutine(gptntActions.Rotate90(direction));
			return "flipped bomb right by 90 degrees";
		}
		else if (direction.Equals("up"))
		{
			StartCoroutine(gptntActions.Rotate90(direction));
			return "flipped bomb up by 90 degrees";
		}
		else if (direction.Equals("down"))
		{
			StartCoroutine(gptntActions.Rotate90(direction));
			return "flipped bomb down by 90 degrees";
		}
		return "nerd";
	}

	private List<string> AllPossibleActions(List<Selectable> activeSelectables)
	{
		// Should always have all actions, including clicks, holds, outs, releases, rotations
		List<string> actions = new List<string>();
		actions.AddRange(Clickables(activeSelectables.Count, click));
		actions.AddRange(Clickables(activeSelectables.Count, hold));
		actions.Add(zoomOut);
		actions.Add(release);
		actions.AddRange(rotationActions);
		return actions;
	}

	private List<string> Clickables(int selectableCount, string action)
	{
		List<string> clickables = new List<string>();
		for (int i = 0; i < selectableCount; i++)
		{
			clickables.Add(action + "_" + i.ToString());
		}
		return clickables;
	}

	private void Click(Selectable selectable)
	{
		gptntActions.ClickSelectable(selectable);
		gptntActions.Release();
	}

	private void Hold(Selectable selectable)
	{
		gptntActions.ClickSelectable(selectable);
	}
}