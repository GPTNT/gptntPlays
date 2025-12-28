using System;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TwitchPlaysAssembly;
using log4net;
using System.Collections;
using Org.BouncyCastle.Asn1.X509;

public class MagicSolver : MonoBehaviour
{
	private static System.Random rng = new System.Random();
	private static ILog log = LogManager.GetLogger("MagicSolver");

	private List<TwitchModule> solveSequence;
	GptntStates gptntStates;
	GptntActions gptntActions;

	private IEnumerator solverEnumerator;
	private bool startedSolving;

	private void Awake()
	{
		gptntActions = GetComponent<GptntActions>();
		gptntStates = GetComponent<GptntStates>();
		gptntStates.OnFirstLightsOn += ChooseModules;
	}

	private void ChooseModules()
	{
		log.Debug("Choosing Modules");
		List<TwitchModule> twitchModules = new List<TwitchModule>(FindObjectsOfType<TwitchModule>());
		if (twitchModules.Count == 1)
		{
			solveSequence = twitchModules;
			return;
		}

		solveSequence = twitchModules.OrderBy(_ => rng.Next()).ToList();
		var names = solveSequence.Select(module => module.BombComponent.GetModuleDisplayName());
		string allNames = "";
		foreach (var name in names)
		{
			allNames += name + ", ";
		}
		log.Debug($"Module magic solve sequence: {allNames}");
	}

	public string ApplyMagic()
	{

		if (gptntActions.isZoomedIn) // check if zoomed in
		{
			log.Debug("We are zoomed in");
			SolvableModuleState zoomedIn = gptntStates.bombState.modules.First(module => module.inFocus);
			if (zoomedIn.isSolved) // if is solved -> zoom out
			{
				startedSolving = false;
				solveSequence.RemoveAt(0);
				return gptntActions.ZoomOut();
			}
			SolveStep(solveSequence[0]);
			return "Solved one step";
		}
		// if zoomed out
		log.Debug("We are zoomed out");
		TwitchModule nextModule = solveSequence[0];
		bool moduleOnFront = StateFromTwitchModule(nextModule).onFront;
		log.Debug($"Module on front? {nextModule.BombComponent.GetModuleDisplayName()} {moduleOnFront}, current side is Front? {gptntStates.bombState.bombSide.Equals("front")}");
		if (moduleOnFront == gptntStates.bombState.bombSide.Equals("front")) // next module face == current face -> zoom in
		{
			log.Debug("Same side");
			gptntActions.ZoomIn(nextModule.Selectable);
			return $"Zoomed into {nextModule.BombComponent.GetModuleDisplayName()}";
		}
		// else -> flip
		log.Debug("Flip");
		StartCoroutine(gptntActions.Rotate180());
		return "magically flipped to other side";
	}

	public bool SolveStep(TwitchModule twitchModule)
	{
		if (!startedSolving)
		{
			startedSolving = true;
			// Init solver
			log.Debug("About to run the ienum");
			MethodInfo method = twitchModule.Solver.GetType().GetMethod(
				"ForcedSolveIEnumerator",
				BindingFlags.Instance | BindingFlags.NonPublic
			);
			solverEnumerator = (IEnumerator) method.Invoke(twitchModule.Solver, null);
			log.Debug($"name of enumerator {solverEnumerator}");
			solverEnumerator.MoveNext(); // for the first yield return null
		}

		if (solverEnumerator.MoveNext())
		{
			var current = solverEnumerator.Current;

			// Handle nested coroutines/enumerators
			if (current is IEnumerator nestedEnumerator)
			{
				StartCoroutine(nestedEnumerator);
			}

			return true; // Step executed successfully
		}
		else
		{
			// Coroutine finished
			solverEnumerator = null;
			startedSolving = false;
			return false; // No more steps
		}
	}

	private SolvableModuleState StateFromTwitchModule(TwitchModule twitchModule)
	{
		BombComponent component = twitchModule.BombComponent;
		return gptntStates.bombState.modules.First(module => module.component == component);
	}
}

