using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using UnityEngine;
using System.Collections;
using Assets.Scripts.Missions;
using log4net;

public class RequestHandlers : MonoBehaviour
{
	private float timeStepSize;
	KMMission mission;
	GameObject spawn;
	KMGameCommands gameCommands;

	GptntStates gptntStates;
	GptntActions gptntActions;
	GptntBuffer gptntBuffer;
	Segmentation segmentation;
	GptntHttpHandler httpHandler;
	MagicSolver magic;

	bool canGetState;

	private static ILog log = LogManager.GetLogger("RequestHandler");

	private void Awake()
	{
		gameCommands = GetComponent<KMGameCommands>();
		gptntStates = GetComponent<GptntStates>();
		gptntActions = GetComponent<GptntActions>();
		gptntBuffer = GetComponent<GptntBuffer>();
		segmentation = GetComponent<Segmentation>();
		httpHandler = GetComponent<GptntHttpHandler>();
		magic = GetComponent<MagicSolver>();

		spawn = new GameObject();
		mission = ScriptableObject.CreateInstance<KMMission>();

		gptntStates.OnFirstLightsOn += () => canGetState = true;
		gptntStates.OnReset += () => canGetState = false;
	}

	#region Debug Helpers

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.W))
		{
			StartCoroutine(gptntActions.Rotate90("up"));
		}
		if (Input.GetKeyDown(KeyCode.S))
		{
			StartCoroutine(gptntActions.Rotate90("down"));
		}
		if (Input.GetKeyDown(KeyCode.A))
		{
			StartCoroutine(gptntActions.Rotate90("left"));
		}
		if (Input.GetKeyDown(KeyCode.D))
		{
			StartCoroutine(gptntActions.Rotate90("right"));
		}
		if (Input.GetKeyDown(KeyCode.F))
		{
			StartCoroutine(gptntActions.Rotate180());
		}
		if (Input.GetKeyDown(KeyCode.O))
		{
			Selectable[] selectables = GetActiveSelectables().ToArray();
			GameObject[] objects = new GameObject[selectables.Length];
			for (int i = 0; i < selectables.Length; i++)
			{
				objects[i] = selectables[i].gameObject;
			}
			StartCoroutine(segmentation.Capture(objects, (bytes) => {
				System.IO.File.WriteAllBytes("segmentation.png", bytes);
			}));
		}
	}

	#endregion

	public string HandleRandomSolve(HttpListenerRequest request, HttpListenerResponse response)
	{
		var traceContext = httpHandler.GetCurrentTraceContext();
		OpenTelemetrySpan span = new OpenTelemetrySpan("random.solve", traceContext.TraceId, traceContext.SpanId);

		int numModulesToSolve = int.Parse(request.QueryString.Get("value"));
		log.Debug(GptntDebug.FormatMessage("Handling random solve for " + numModulesToSolve + " modules", span.GetTraceId(), span.GetSpanId()));
		return RunOnMainThread(() =>
		{
			string responseString = "";
			List<TwitchModule> modules = FindObjectsOfType<TwitchModule>().Where(x => !x.Solved).ToList();
			System.Random rnd = new System.Random();
			// Choose random number of module between 1 and max-1
			List<TwitchModule> modulesToSolve = modules.OrderBy(x => rnd.Next()).Take(numModulesToSolve).ToList();
			foreach (var module in modulesToSolve)
			{
				module.Solver.SolveSilently();
				log.Debug(GptntDebug.FormatMessage("Randomly solved module: " + module.name, span.GetTraceId(), span.GetSpanId()));
				responseString += " " + module.name;
			}
			span.End(true);
			return responseString;
		});
	}

	public string HandleReset(HttpListenerRequest request, HttpListenerResponse response)
	{
		var traceContext = httpHandler.GetCurrentTraceContext();
		OpenTelemetrySpan span = new OpenTelemetrySpan("game.reset", traceContext.TraceId, traceContext.SpanId);

		if (gptntStates.gameState != GptntStates.GameState.Setup)
			SceneManager.Instance.ReturnToSetupState();

		log.Debug(GptntDebug.FormatMessage("reset successful", span.GetTraceId(), span.GetSpanId()));
		span.End(true);
		return "Nuh uh";
	}

	public string HandleHealth(HttpListenerRequest request, HttpListenerResponse response)
	{
		return gptntStates.gameState.ToString();
	}

	public string HandleObservationBuffer(HttpListenerRequest request, HttpListenerResponse response)
	{
		var traceContext = httpHandler.GetCurrentTraceContext();
		OpenTelemetrySpan span = new OpenTelemetrySpan("observation.buffer", traceContext.TraceId, traceContext.SpanId);

		string segmentation = HandleSegmentation(response);
		if (response.StatusCode != 200)
		{
			log.Debug(GptntDebug.FormatMessage("Segmentation took too long", span.GetTraceId(), span.GetSpanId()));
			return "";
		}
		else
			log.Debug(GptntDebug.FormatMessage("segmentation returned properly", span.GetTraceId(), span.GetSpanId()));
		ObservationPayload observation = gptntBuffer.GetBufferJSON();
		observation.segmentation = segmentation;

		span.End(true);
		return JsonConvert.SerializeObject(observation);
	}

	#region Handle Actions

	public string HandleAction(HttpListenerRequest request, HttpListenerResponse response)
	{
		var traceContext = httpHandler.GetCurrentTraceContext();
		OpenTelemetrySpan span = new OpenTelemetrySpan("game.action", traceContext.TraceId, traceContext.SpanId);

		GptntStates.GameState gameState = gptntStates.gameState;
		if (!gameState.EqualsAny(GptntStates.GameState.LightsOn, GptntStates.GameState.LightsOff) || !gptntStates.isStarted)
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			log.Debug(GptntDebug.FormatMessage("Cannot send action to the game in " + gameState.ToString() + " state", span.GetTraceId(), span.GetSpanId()));
			span.End(false);
			return "Cannot send action to the game in " + gameState.ToString() + " state";
		}

		string responseString = RunOnMainThread(() => HandleActionOnMainThread(request, response));

		if (response.StatusCode != (int) HttpStatusCode.OK)
		{
			return responseString;
		}
		span.End(true);
		return responseString;
	}

	private string HandleActionOnMainThread(HttpListenerRequest request, HttpListenerResponse response)
	{
		var traceContext = httpHandler.GetCurrentTraceContext();
		OpenTelemetrySpan span = new OpenTelemetrySpan("game.action.mainthread", traceContext.TraceId, traceContext.SpanId);

		string actionType = request.QueryString.Get("action");
		string responseString = null;
		span.SetAttribute("action.type", actionType);
		log.Debug(GptntDebug.FormatMessage("Handling action: " + actionType, span.GetTraceId(), span.GetSpanId()));

		gptntStates.UpdateBombState();
		switch (actionType)
		{
			case "click":
				responseString = HandleClick(request, response);
				break;
			case "hold":
				responseString = HandleHold(request, response);
				break;
			case "release":
				responseString = HandleClickEnd();
				break;
			case "out":
				responseString = HandleZoomOut();
				break;
			case "magic":
				responseString = HandleMagic();
				break;
			default:
				responseString = HandleRotate(request);
				break;
		}
		span.End(true);
		return responseString;
	}

	public string HandleClick(HttpListenerRequest request, HttpListenerResponse response)
	{
		string responseString = "";
		responseString += HandleClickStart(request, response);
		responseString += HandleClickEnd();

		return responseString;
	}

	public string HandleHold(HttpListenerRequest request, HttpListenerResponse response)
	{
		return HandleClickStart(request, response);
	}

	private string HandleClickStart(HttpListenerRequest request, HttpListenerResponse response)
	{
		float x, y;
		var traceContext = httpHandler.GetCurrentTraceContext();
		OpenTelemetrySpan span = new OpenTelemetrySpan("game.action.clickstart", traceContext.TraceId, traceContext.SpanId);

		try
		{
			x = (float) Convert.ToDouble(request.QueryString.Get("x_pos"));
			y = 1 - (float) Convert.ToDouble(request.QueryString.Get("y_pos"));
			if (x < 0 || x > 1 || y < 0 || y > 1)
			{
				throw new Exception("Coordinates must be between 0-1");
			}
		}
		catch (Exception ex)
		{
			log.Error(GptntDebug.FormatMessage("Uh oh click start failed", span.GetTraceId(), span.GetSpanId()), ex);
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			span.End(false);
			return "Could not parse x and y coordinates: " + ex;
		}
		span.SetAttribute("click.x", x);
		span.SetAttribute("click.y", y);
		log.Debug(GptntDebug.FormatMessage($"Clicked at x: {x}, y: {y}", span.GetTraceId(), span.GetSpanId()));
		span.End(true);
		return gptntActions.Click(x, y);
	}

	public string HandleClickEnd()
	{
		return gptntActions.Release();
	}

	public string HandleZoomOut()
	{
		return gptntActions.ZoomOut();
	}

	public string HandleMagic()
	{
		return magic.ApplyMagic();
	}

	#endregion

	private string RunOnMainThread(Func<string> func)
	{
		string result = null;
		var handle = new ManualResetEvent(false);
		MainThreadQueue.Enqueue(() => {
			result = func();
			handle.Set();
		});
		bool completed = handle.WaitOne(50000);

		if (!completed)
		{
			log.Error(GptntDebug.FormatMessage("Timed out waiting for main thread"));
			return null;
		}

		return result;
	}

	public string HandleStartMission(HttpListenerRequest request, HttpListenerResponse response)
	{
		if (!StateEqualsAny(GptntStates.GameState.Setup))
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Cannot start a game from " + gptntStates.gameState + " state";
		}
		string seed = request.QueryString.Get("seed");
		int timeLimit = int.Parse(request.QueryString.Get("timeLimit"));
		int numStrikes = int.Parse(request.QueryString.Get("numStrikes"));
		int needyTime = int.Parse(request.QueryString.Get("needyTime"));
		bool isFront = bool.Parse(request.QueryString.Get("isFront"));
		int optWidgets = int.Parse(request.QueryString.Get("optWidgets"));
		string componentsString = request.QueryString.Get("components");
		List<string> components = componentsString.Split(',').ToList();
		Time.timeScale = float.Parse(request.QueryString.Get("timeScale"));
		timeStepSize = int.Parse(request.QueryString.Get("timeStepSize"));
		return StartMission(seed, timeLimit, numStrikes, needyTime, isFront, optWidgets, components);
	}

	public string HandleGetState(HttpListenerRequest request, HttpListenerResponse response)
	{
		var traceContext = httpHandler.GetCurrentTraceContext();
		OpenTelemetrySpan span = new OpenTelemetrySpan("game.getstate", traceContext.TraceId, traceContext.SpanId);
		if (!canGetState)
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Cannot get bomb state in " + gptntStates.gameState + " state";
		}
		log.Debug(GptntDebug.FormatMessage("Throwing get state into main thread", span.GetTraceId(), span.GetSpanId()));
		string responseString = RunOnMainThread(() =>
		{
			string stateJson =  JsonConvert.SerializeObject(gptntStates.UpdateBombState(), Formatting.Indented);
			span.SetAttribute("bomb.state", stateJson);
			log.Debug(GptntDebug.FormatMessage("Retrieved bomb state", span.GetTraceId(), span.GetSpanId()));
			return stateJson;
		});

		if (responseString == null)
		{
			response.StatusCode = (int) HttpStatusCode.InternalServerError;
			log.Error(GptntDebug.FormatMessage("Could not serialize bomb state", span.GetTraceId(), span.GetSpanId()));
			span.End(false);
			responseString = "Could not serialize bomb state";
		}

		response.ContentType = "application/json";
		span.End(true);
		return responseString;
	}

	public string HandleDetonateBomb(HttpListenerRequest request, HttpListenerResponse response)
	{
		if(!StateEqualsAny(GptntStates.GameState.LightsOn))
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Cannot detonate bomb in " + gptntStates.gameState + " state";
		}

		TwitchBomb bomb = FindObjectOfType<TwitchBomb>();
		bomb.Bomb.Detonate();
		return "Detonated bomb successfully";
	}

	public string HandleSolveBomb(HttpListenerRequest request, HttpListenerResponse response)
	{
		if (!StateEqualsAny(GptntStates.GameState.LightsOn))
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Cannot solve bomb in " + gptntStates.gameState + " state";
		}

		foreach (var module in FindObjectsOfType<TwitchModule>())
		{
			if (module.Solved)
				continue;
			module.Solver.SolveSilently();
		}
		return "Solved bomb successfully";
	}

	public string HandleRotate(HttpListenerRequest request)
	{
		string direction = request.QueryString.Get("action");
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
		else
		{
			return "invalid direction. Valid directions: left, right, up, down, flip";
		}
	}

	private string HandleSegmentation(HttpListenerResponse response)
	{
		if (!StateEqualsAny(GptntStates.GameState.LightsOn))
		{
			response.ContentType = "image/png";
			return "";
		}

		byte[] imageBytes = null;
		var waitHandle = new ManualResetEvent(false);

		Selectable[] selectables = GetActiveSelectables().ToArray();
		GameObject[] objects = new GameObject[selectables.Length];
		for (int i = 0; i < selectables.Length; i++)
		{
			objects[i] = selectables[i].gameObject;
		}
		MainThreadQueue.Enqueue(() =>
		{
			StartCoroutine(segmentation.Capture(objects, (bytes) =>
			{
				imageBytes = bytes;
				waitHandle.Set();
			}));
		});

		if (!waitHandle.WaitOne(500))
		{
			response.StatusCode = (int) HttpStatusCode.RequestTimeout;
			return "Failed to get segmentation mask";
		}
		response.ContentType = "image/png";
		return (imageBytes != null) ? Convert.ToBase64String(imageBytes) : "";
	}

	public string HandleSetTimescale(HttpListenerRequest request, HttpListenerResponse response)
	{
		try
		{
			string value = request.QueryString.Get("value");
			MainThreadQueue.Enqueue(() => log.Debug(GptntDebug.FormatMessage("Setting time scale to: " + value)));
			if (float.Parse(value) == 0 && StateEqualsAny(GptntStates.GameState.Transitioning))
				MainThreadQueue.Enqueue(() =>
				{
					log.Warn(GptntDebug.FormatMessage("Game paused during transitioning, waiting for end of transitioning to pause"));
					StartCoroutine(PauseAfterTransition());
				});
			else
				Time.timeScale = float.Parse(value);
			return "Set timeScale to " + value;
		}
		catch(Exception ex)
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			log.Error(GptntDebug.FormatMessage("Could not parse time scale request"), ex);
			return "Could not parse request";
		}

	}

	private IEnumerator PauseAfterTransition()
	{
		yield return new WaitUntil(() => !StateEqualsAny(GptntStates.GameState.Transitioning));
		Time.timeScale = 0;
	}

	public string HandleSetStepUnit(HttpListenerRequest request, HttpListenerResponse response)
	{
		string value = request.QueryString.Get("value");
		timeStepSize = int.Parse(value);
		return "Set timeStepSize to " + value;
	}

	public string HandleTimeStep(HttpListenerRequest request, HttpListenerResponse response)
	{
		// Start the coroutine to handle the time step
		MainThreadQueue.Enqueue(() => StartCoroutine(TimeStepCoroutine()));
		return "Paused after " + timeStepSize + " in-game milliseconds";
	}

	private IEnumerator TimeStepCoroutine()
	{
		Time.timeScale = 1; // Unpause
		yield return new WaitForSeconds(timeStepSize / 1000f);
		yield return new WaitUntil(() => !StateEqualsAny(GptntStates.GameState.Transitioning));
		Time.timeScale = 0; // Pause
	}

	private bool StateEqualsAny(params GptntStates.GameState[] states)
	{
		foreach (var state in states)
		{
			if (state == gptntStates.gameState)
				return true;
		}
		return false;
	}

	private string StartMission(string seed, int timeLimit, int numStrikes, int needyTime, bool isFront, int optWidgets, List<string> components)
	{
		if (string.IsNullOrEmpty(seed))
		{
			return "Please enter valid seed. e.g. seed=123";
		}

		if (timeLimit < 0)
		{
			return "Please enter a valid time limit. e.g. timeLimit=90";
		}

		if (numStrikes < 1)
		{
			return "Please enter a valid number of strikes. e.g. numStrikes=3";
		}

		if (needyTime < 0 || needyTime > timeLimit)
		{
			return "Please enter valid time delay for needy module activation. e.g. needyTime=30";
		}

		if (optWidgets < 0)
		{
			return "Please enter a valid number of optional widgets. e.g. optWidgets=3";
		}

		if (components.Count < 1 || components.Count > 11)
		{
			return "Please enter a valid list of components to be present on the bomb. e.g. components=Wires,BigButton";
		}

		//Update bomb characteristics with inputted values, then create mission instance with the bomb
		KMGeneratorSetting setting = new KMGeneratorSetting();
		setting.TimeLimit = timeLimit;
		setting.NumStrikes = numStrikes;
		setting.TimeBeforeNeedyActivation = needyTime;
		setting.FrontFaceOnly = isFront;
		setting.OptionalWidgetCount = optWidgets;
		List<KMComponentPool> pools = new List<KMComponentPool>();

		for (int i = 0; i < components.Count; i++)
		{
			KMComponentPool pool = new KMComponentPool();
			pool.Count = 1;
			string compString = components[i];
			KMComponentPool.ComponentTypeEnum CompType;
			try
			{
				CompType = (KMComponentPool.ComponentTypeEnum) Enum.Parse(typeof(KMComponentPool.ComponentTypeEnum), compString);
			}
			catch (Exception e)
			{
				return "Invalid component found! Please try again.";
			}
			pool.ComponentTypes = new List<KMComponentPool.ComponentTypeEnum> { CompType };
			pools.Add(pool);


		}
		setting.ComponentPools = pools;
		mission.GeneratorSetting = setting;

		KMBomb bomb = gameCommands.CreateBomb(null, setting, spawn, seed);
		MainThreadQueue.Enqueue(delegate () { gameCommands.StartMission(mission, seed); });

		return seed;
	}

	private List<Selectable> GetActiveSelectables()
	{
		List<Selectable> activeSelectables = new List<Selectable>();
		SelectableManager selectableManager = KTInputManager.Instance.SelectableManager;
		string parentName = selectableManager.GetCurrentParent().gameObject.name;
		TwitchBomb bomb = FindObjectOfType<TwitchBomb>();

		if (parentName.Equals("BasicRectangleBomb(Clone)")) // Bomb still on table
		{
			// Face has no selectables;
		}
		else if (parentName.Equals("FrontFace") || parentName.Equals("RearFace")) // Bomb held, return module selectables
		{
			if (!(gptntActions.currentFace == GptntActions.ZFace.Side && gptntActions.activeFace.EqualsAny(GptntActions.SideFace.Front, GptntActions.SideFace.Back)))
				return activeSelectables; // Return empty if we're not on the front or back side of the bomb

			foreach (BombComponent component in bomb.Bomb.BombComponents)
			{
				if (!component.ComponentType.EqualsAny(ComponentTypeEnum.Empty, ComponentTypeEnum.Timer))
				{
					Vector3 componentUp = component.transform.up;
					Vector3 bombUp = bomb.Bomb.transform.up;
					float angleBetween = Vector3.Angle(componentUp, bombUp);
					bool isFront = angleBetween < 90.0f;
					log.Debug($"Componenet {component.name} isFront={isFront}. Bomb on front={gptntActions.activeFace.Equals(GptntActions.SideFace.Front)}");	
					if (isFront == gptntActions.activeFace.Equals(GptntActions.SideFace.Front))
					{
						activeSelectables.Add(component.GetComponent<Selectable>());
					}
				}
			}
		}
		else // Module zoomed in return internal selectables
		{
			// Assume that it is a module and get its selectables
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

