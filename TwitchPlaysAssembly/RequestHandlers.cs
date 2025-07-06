using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using UnityEngine;
using System.Collections;
using Assets.Scripts.Missions;

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

	bool canGetState;

	private void Awake()
	{
		gameCommands = GetComponent<KMGameCommands>();
		gptntStates = GetComponent<GptntStates>();
		gptntActions = GetComponent<GptntActions>();
		gptntBuffer = GetComponent<GptntBuffer>();
		segmentation = GetComponent<Segmentation>();

		spawn = new GameObject();
		mission = ScriptableObject.CreateInstance<KMMission>();

		gptntStates.OnFirstLightsOn += () => canGetState = true;
		gptntStates.OnReset += () => canGetState = false;
	}

	public string HandleRandomSolve(HttpListenerRequest request, HttpListenerResponse response)
	{
		int numModulesToSolve = int.Parse(request.QueryString.Get("value"));
		return RunOnMainThread(() =>
		{
			string responseString = "";
			GptntDebug.Log("[RandomSolve] value = " + numModulesToSolve);
			List<TwitchModule> modules = FindObjectsOfType<TwitchModule>().Where(x => !x.Solved).ToList();
			System.Random rnd = new System.Random();
			// Choose random number of module between 1 and max-1
			List<TwitchModule> modulesToSolve = modules.OrderBy(x => rnd.Next()).Take(numModulesToSolve).ToList();
			foreach (var module in modulesToSolve)
			{
				module.Solver.SolveSilently();
				responseString += " " + module.name;
			}
			return responseString;
		});
	}

	public string HandleReset(HttpListenerRequest request, HttpListenerResponse response)
	{
		if (gptntStates.gameState != GptntStates.GameState.Setup)
			SceneManager.Instance.ReturnToSetupState();
		return "Nuh uh";
	}

	public string HandleHealth(HttpListenerRequest request, HttpListenerResponse response)
	{
		return gptntStates.gameState.ToString();
	}

	public string HandleObservationBuffer(HttpListenerRequest request, HttpListenerResponse response)
	{
		string segmentation = HandleSegmentation(response);
		ObservationPayload observation = gptntBuffer.GetBufferJSON();
		observation.segmentation = segmentation;

		return JsonConvert.SerializeObject(observation);
	}

	#region Handle Actions

	public string HandleAction(HttpListenerRequest request, HttpListenerResponse response)
	{
		GptntStates.GameState gameState = gptntStates.gameState;
		if (!gameState.EqualsAny(GptntStates.GameState.LightsOn, GptntStates.GameState.LightsOff) || !gptntStates.isStarted)
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Cannot send action to the game in " + gameState.ToString() + " state";
		}

		string responseString = RunOnMainThread(() => HandleActionOnMainThread(request, response));

		if (response.StatusCode != (int) HttpStatusCode.OK)
		{
			return responseString;
		}
		return responseString;
	}

	private string HandleActionOnMainThread(HttpListenerRequest request, HttpListenerResponse response)
	{
		string actionType = request.QueryString.Get("action");
		string responseString = null;
		GptntDebug.Log("[Action] " + actionType);

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
			default:
				responseString = HandleRotate(request);
				break;
		}
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
			GptntDebug.Log(ex.ToString());
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Could not parse x and y coordinates: " + ex;
		}
		GptntDebug.Log($"[Location] x: {x}, y: {y}");
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

	#endregion

	private string RunOnMainThread(Func<string> func)
	{
		string result = null;
		var handle = new ManualResetEvent(false);
		MainThreadQueue.Enqueue(() => {
			result = func();
			handle.Set();
		});
		handle.WaitOne();
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
		if (!canGetState)
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Cannot get bomb state in " + gptntStates.gameState + " state";
		}

		string responseString = RunOnMainThread(() =>
		{
			return JsonConvert.SerializeObject(gptntStates.UpdateBombState(), Formatting.Indented);
		});

		if (responseString == null)
		{
			response.StatusCode = (int) HttpStatusCode.InternalServerError;
			responseString = "Could not serialize bomb state";
		}

		response.ContentType = "application/json";
		return responseString;
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
			return "Failed to get segmentation mask";
		}
		response.ContentType = "image/png";
		return (imageBytes != null) ? Convert.ToBase64String(imageBytes) : "";
	}

	public string HandleSetTimescale(HttpListenerRequest request, HttpListenerResponse response)
	{
		string value = request.QueryString.Get("value");
		MainThreadQueue.Enqueue(() => GptntDebug.Log("[TimeScale] value = " + value));
		Time.timeScale = float.Parse(value);
		return "Set timeScale to " + value;
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

