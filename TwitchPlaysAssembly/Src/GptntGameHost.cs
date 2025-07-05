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
	KMGameCommands gameCommands;
	KMGameInfo gameInfo;
	private bool isStarted = false;
	GameObject spawn;
	KMMission mission;
	GptntActions gptntActions;
	GptntStates gptntStates;
	BombState lastKnownBombState;
	public volatile string gameState;

	Thread workerThread;
	Worker workerObject;
	Queue<Action> actions;
	string timestamp;
	string destinationLogPath;

	public int timeStepSize = 250;

	TwitchBomb bomb;
	public string sourceLogPath = @"logs/ktane.log";
	private string lastRead = "";
	long lastPosition = 0;

	// Observation variables 
	private GptntBuffer gptntBuffer;
	private RenderTexture rawScreenRenderTexture;
	private Texture2D tex;
	private Rect rect;

	private Segmentation segmentation;

	private const int MaxFrames = 16;
	private const float FrameRateMS = 0.25f;

	int screenWidth = 512;
	int screenHeight = 384;

	void Awake()
	{
		using (FileStream fs = new FileStream(sourceLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
		{
			lastPosition = fs.Length;
		}
		actions = new Queue<Action>();
		bombInfo = GetComponent<KMBombInfo>();
		gameCommands = GetComponent<KMGameCommands>();
		spawn = new GameObject();
		mission = ScriptableObject.CreateInstance<KMMission>();
		// Create the thread object. This does not start the thread.
		workerObject = new Worker(this);
		workerThread = new Thread(workerObject.DoWork);
		// Start the worker thread.
		workerThread.Start(this);
		gptntBuffer = GetComponent<GptntBuffer>();
		gptntActions = GetComponent<GptntActions>();
		gptntStates = GetComponent<GptntStates>();


		segmentation = GetComponent<Segmentation>();
	}

	private void Start()
	{
		bombInfo.OnBombExploded += OnGameEnd;
		bombInfo.OnBombSolved += OnGameEnd;
		timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
		destinationLogPath = $"{timestamp}log.txt";
		gptntStates.logFilePath = destinationLogPath;
		StartCoroutine(CopyLogPeriodically());
		gameInfo = FindObjectOfType<KMGameInfo>();
		gameInfo.OnStateChange += (KMGameInfo.State state) =>
		{
			gameState = state.ToString();
			if (gameState.Equals("Setup"))
			{
				Reset();
			}
		};
		gameInfo.OnLightsChange += (bool on) =>
		{
			bomb = FindObjectOfType<TwitchBomb>();
			gptntActions.bomb = bomb;
			gptntActions.InitRotation();
			gameState = gameState.EqualsAny("Gameplay", "Lights On", "Lights Off") ? (on ? "Lights On" : "Lights Off") : gameState;

			if (gameState.Equals("Lights On") && !isStarted)
			{
				// when the first light turns on
				gptntStates.readyToGive = true;
				isStarted = true;
				gptntBuffer.StartBuffer(FrameRateMS);
				lastKnownBombState = gptntStates.GetInitialBombState();
				StartCoroutine(HoldBomb());
			}
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
		isStarted = false;
	}

	private void Reset()
	{
		actions.Clear();
		gptntStates.readyToGive = false;
		isStarted = false;
		InputInterceptor.EnableInput();
		gptntBuffer.ClearBuffer();
	}

	void Update()
	{
		if (actions.Count > 0)
		{
			Action action = actions.Dequeue();
			action();
		}
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

	void OnDestroy()
	{
		workerThread.Abort();
		workerObject.Stop();
	}

	public void SimpleListenerExample(HttpListener listener)
	{
		while (true)
		{
			try
			{
				HttpListenerContext context = listener.GetContext();
				HandleRequest(context);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error processing request: " + ex.Message);
			}
		}
	}
	#region Handling Requests
	private void HandleRequest(HttpListenerContext context)
	{
		HttpListenerRequest request = context.Request;
		HttpListenerResponse response = context.Response;

		string responseString;
		string path = request.Url.AbsolutePath.ToLowerInvariant();
		if (!path.Equals("/health")) GptntDebug.Log("[HTTP Request] " + path);
		// Route handling with switch
		switch (path)
		{
			case "/startmission":
				responseString = HandleStartMission(request, response); // Main thread
				break;
			case "/screenshot":
				responseString = HandleScreenshot(response); // Main thread
				break;
			case "/settimescale":
				responseString = HandleSetTimescale(request);
				break;
			case "/setstepunit":
				responseString = HandleSetStepUnit(request);
				break;
			case "/timestep":
				responseString = HandleTimeStep(request);
				break;
			case "/action":
				responseString = HandleAction(request, response); // Main thread
				break;
			case "/observation":
				responseString = HandleObservation(response); // Main thread
				break;
			case "/fullobservation":
				responseString = HandleFullObservation(response); // Main thread
				break;
			case "/buffer":
				responseString = HandleObservationBuffer(response);
				break;
			case "/health":
				responseString = HandleHealth();
				break;
			case "/reset":
				responseString = HandleReset();
				break;
			case "/state":
				responseString = HandleGetState(response);
				break;
			case "/random":
				responseString = HandleRandomSolve(request);
				break;
			default:
				responseString = "Unknown route.";
				break;
		}

		// Send response
		SendResponse(request, response, responseString);
	}

	private string HandleRandomSolve(HttpListenerRequest request)
	{
		string responseString = "";
		int numModulesToSolve = int.Parse(request.QueryString.Get("value"));
		// Get the modules

		var waitHandle = new ManualResetEvent(false);
		MainThreadQueue.Enqueue(() =>
		{
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
			waitHandle.Set();
		});

		waitHandle.WaitOne();
		return responseString;
	}

	private string HandleReset()
	{
		if (!gameState.Equals("Setup"))
			SceneManager.Instance.ReturnToSetupState();
		return "Nuh uh";
	}

	private string HandleHealth()
	{
		return gameState;
	}

	private string HandleObservationBuffer(HttpListenerResponse response)
	{

		string segmentation = HandleSegmentation(response);
		ObservationPayload observation = gptntBuffer.GetBufferJSON();
		observation.segmentation = segmentation;

		return JsonConvert.SerializeObject(observation);
	}

	private string HandleObservation(HttpListenerResponse response)
	{
		string screenshot = HandleScreenshot(response);
		string segmentation = HandleSegmentation(response);

		if (response.StatusCode != (int) HttpStatusCode.OK)
		{
			return screenshot + " " + segmentation;
		}

		string json = "{"
		+ "\"screenshot\":\"" + EscapeJsonString(screenshot) + "\","
		+ "\"segmentation\":\"" + EscapeJsonString(segmentation) + "\""
		+ "}";

		response.ContentType = "application/json";
		return json;
	}

	private string HandleFullObservation(HttpListenerResponse response)
	{
		string screenshot = HandleScreenshot(response);
		string segmentation = HandleSegmentation(response);
		string state = HandleGetState(response);

		if (response.StatusCode != (int) HttpStatusCode.OK)
		{
			return screenshot + " " + segmentation;
		}

		string json = "{"
		+ "\"screenshot\":\"" + EscapeJsonString(screenshot) + "\","
		+ "\"segmentation\":\"" + EscapeJsonString(segmentation) + "\""
		+ "\"state\":\"" + state + "\""
		+ "}";

		response.ContentType = "application/json";
		return json;
	}

	// Helper function to parse string to json
	private string EscapeJsonString(string str)
	{
		if (str == null) return "";
		return str
			.Replace("\\", "\\\\")
			.Replace("\"", "\\\"")
			.Replace("\n", "\\n")
			.Replace("\r", "\\r")
			.Replace("\t", "\\t");
	}

	private string HandleAction(HttpListenerRequest request, HttpListenerResponse response)
	{
		if (!gameState.EqualsAny("Lights On", "Lights Off") || !isStarted)
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Cannot send action to the game in " + gameState + " state";
		}

		string responseString = null;

		responseString = HandleActionOnMainThread(request, response);

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
		var waitHandle = new ManualResetEvent(false);
		MainThreadQueue.Enqueue(() =>
		{
			GptntDebug.Log("[Action] " + actionType);

			try
			{
				lastKnownBombState = gptntStates.UpdateBombState();
			}
			catch
			{
				// Ignored - state may be invalid at current time
			}

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
			waitHandle.Set();
		}
	);
		waitHandle.WaitOne();
		return responseString;
	}

	private string HandleClick(HttpListenerRequest request, HttpListenerResponse response)
	{
		string responseString = "";
		responseString += HandleClickStart(request, response);
		responseString += HandleClickEnd();

		return responseString;
	}

	private string HandleHold(HttpListenerRequest request, HttpListenerResponse response)
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

	private string HandleClickEnd()
	{
		return gptntActions.Release();
	}

	private string HandleZoomOut()
	{
		return gptntActions.ZoomOut();
	}

	private string HandleStartMission(HttpListenerRequest request, HttpListenerResponse response)
	{
		if (!gameState.Equals(KMGameInfo.State.Setup.ToString()))
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Cannot start a game from " + gameState + " state";
		}
		string seed = request.QueryString.Get("seed");
		int timeLimit = int.Parse(request.QueryString.Get("timeLimit"));
		int numStrikes = int.Parse(request.QueryString.Get("numStrikes"));
		int needyTime = int.Parse(request.QueryString.Get("needyTime"));
		bool isFront = bool.Parse(request.QueryString.Get("isFront"));
		int optWidgets = int.Parse(request.QueryString.Get("optWidgets"));
		string componentsString = request.QueryString.Get("components");
		List<String> components = componentsString.Split(',').ToList();
		Time.timeScale = float.Parse(request.QueryString.Get("timeScale"));
		timeStepSize = int.Parse(request.QueryString.Get("timeStepSize"));
		return StartMission(seed, timeLimit, numStrikes, needyTime, isFront, optWidgets, components);
	}

	protected string StartMission(string seed, int timeLimit, int numStrikes, int needyTime, bool isFront, int optWidgets, List<String> components)
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
			String compString = components[i];
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

		actions.Enqueue(delegate () { gameCommands.StartMission(mission, seed); });

		return seed;
	}

	private string HandleGetState(HttpListenerResponse response)
	{
		if (!gptntStates.readyToGive)
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Cannot get bomb state in " + gameState + " state";
		}


		string responseString = null;
		var waitHandle = new ManualResetEvent(false);
		MainThreadQueue.Enqueue(() =>
		{
			BombState bombState = null;
			try
			{
				bombState = gptntStates.UpdateBombState();
			}
			catch (MemoryModuleException ex)
			{
				StartCoroutine(GetMemoryState());
				GptntDebug.Log("[DEBUG] caught exception: " + ex.ToString());
			}
			finally
			{
				lastKnownBombState = (bombState != null) ? bombState : lastKnownBombState;
				responseString = JsonConvert.SerializeObject(lastKnownBombState, Formatting.Indented);
				waitHandle.Set();
			}
		});

		waitHandle.WaitOne();
		if (responseString == null)
		{
			response.StatusCode = (int) HttpStatusCode.InternalServerError;
			responseString = "Could not serialize bomb state";
		}

		response.ContentType = "application/json";
		return responseString;

	}

	private IEnumerator GetMemoryState()
	{
		// Keep trying to get the state
		yield return new WaitUntil(() => gptntStates.badModule.IsInputValid);
		GptntDebug.Log("[Memory] Updated the bomb state.");
		lastKnownBombState = gptntStates.UpdateBombState();
	}

	private string HandleRotate(HttpListenerRequest request)
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
			GptntDebug.Log("Handle the fucking rotate to the left");
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

	private string HandleScreenshot(HttpListenerResponse response)
	{
		byte[] imageBytes;
		imageBytes = GetScreenshot();

		if (imageBytes == null || imageBytes.Length == 0)
		{
			response.StatusCode = (int) HttpStatusCode.InternalServerError;
			return "Empty screenshot";
		}

		return Convert.ToBase64String(imageBytes);
	}

	private string HandleSegmentation(HttpListenerResponse response)
	{

		if (!gameState.Equals("Lights On"))
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

	private string HandleSetTimescale(HttpListenerRequest request)
	{
		string value = request.QueryString.Get("value");
		MainThreadQueue.Enqueue(() => GptntDebug.Log("[TimeScale] value = " + value));
		Time.timeScale = float.Parse(value);
		return "Set timeScale to " + value;
	}

	private string HandleSetStepUnit(HttpListenerRequest request)
	{
		string value = request.QueryString.Get("value");
		timeStepSize = int.Parse(value);
		return "Set timeStepSize to " + value;
	}

	private string HandleTimeStep(HttpListenerRequest request)
	{
		// Start the coroutine to handle the time step
		MainThreadQueue.Enqueue(() => StartCoroutine(TimeStepCoroutine()));
		return "Paused after " + timeStepSize + " in-game milliseconds";
	}

	private IEnumerator TimeStepCoroutine()
	{
		Time.timeScale = 1; // Unpause
		yield return new WaitForSeconds(timeStepSize / 1000f);
		if (isStarted)
			Time.timeScale = 0; // Pause
	}

	#endregion

	private void SendResponse(HttpListenerRequest request, HttpListenerResponse response, string responseString)
	{
		byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);

		// Set CORS headers here
		response.AddHeader("Access-Control-Allow-Origin", "*"); // or restrict to your domain
		response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
		response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

		// Special case for preflight OPTIONS requests — immediately return 200 OK
		if (response.StatusCode == (int) HttpStatusCode.OK && request.HttpMethod == "OPTIONS")
		{
			response.ContentLength64 = 0;
			response.OutputStream.Close();
			return;
		}

		// Get a response stream and write the response to it.
		response.ContentLength64 = buffer.Length;
		System.IO.Stream output = response.OutputStream;
		output.Write(buffer, 0, buffer.Length);
		// You must close the output stream.
		output.Close();
	}

	protected byte[] GetScreenshot()
	{
		return gptntBuffer.GetLastFrame();
	}

	public class Worker
	{
		ExampleWebService service;
		HttpListener listener;

		public Worker(ExampleWebService s)
		{
			service = s;
		}

		// This method will be called when the thread is started. 
		public void DoWork()
		{
			string port = Environment.GetEnvironmentVariable("port");
			if (port == "" || port is null)
			{
				port = "8085";
			}
			// Create a listener.
			listener = new HttpListener();
			// Add the prefixes.
			foreach (string s in new string[] { $"http://localhost:{port}/" })
			{
				listener.Prefixes.Add(s);
			}
			listener.Start();

			service.SimpleListenerExample(listener);
		}

		public void Stop()
		{
			listener.Stop();
		}
	}
	void CopyLogContents()
	{
		try
		{
			if (File.Exists(sourceLogPath))
			{
				using (FileStream fs = new FileStream(sourceLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				{
					fs.Seek(lastPosition, SeekOrigin.Begin); // jump to where we left off

					using (StreamReader reader = new StreamReader(fs))
					{
						string newContent = reader.ReadToEnd();

						if (!string.IsNullOrEmpty(newContent))
						{
							File.AppendAllText(destinationLogPath, newContent);
							lastPosition = fs.Position; // update our position
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"Error while copying log: {ex.Message}");
		}
	}

	IEnumerator CopyLogPeriodically()
	{
		while (true)
		{
			CopyLogContents();
			yield return new WaitForSeconds(0.5f); // wait 1 second
		}
	}
}