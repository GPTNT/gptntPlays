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

public class ExampleWebService : MonoBehaviour
{
	KMBombInfo bombInfo;
	KMGameCommands gameCommands;
	KMGameInfo gameInfo;
	private bool isStarted = false;
	string modules;
	string solvableModules;
	string solvedModules;
	string bombState;
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

	public string otherSeed;

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

	private const int MaxFrames = 12;
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
		bombState = "NA";
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
		gameInfo.OnLightsChange += (bool on) => {
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
	/*
	Keys and what they do:
	Space: Throws an exception with all the bomb components
	P: ???
	Left Click: prints mouse position
	F: Rotate 180
	backslash: Enable inputs
	X: Hold bomb
	C: Let go Bomb
	V: Finds a twitch module, assumes it is a wire and then cuts the first wire
	M: interact with the first module it finds
	B: ???
	I: rotate up 
	K: rotate down
	J: rotate left
	K: rotate right
	T: Take a screeenshot
	U: Testing -Kareem
	Y: Testing -Kareem 
	*/

	void Update()
	{
		if (actions.Count > 0)
		{
			Action action = actions.Dequeue();
			action();
		}

		 #region debug with clicking a key 

		// A list of all bomb components in order, including empties and the timer
		if (Input.GetKeyDown(KeyCode.Space))
		{
			TwitchGame game = FindObjectOfType<TwitchGame>();
			bomb = GameObject.FindObjectOfType<TwitchBomb>();
			List<TwitchModule> modules = new List<TwitchModule>();
			int modulesIndex = 0;
			foreach (var component in bomb.Bomb.BombComponents)
			{
				if (component.ComponentType.EqualsAny(ComponentTypeEnum.Empty, ComponentTypeEnum.Timer))
				{
					TwitchModule emptyModule = new TwitchModule();
					emptyModule.BombComponent = component;
					modules.Add(emptyModule);
				}
				else
				{
					modules.Add(game.Modules[modulesIndex]);
					modulesIndex++;
				}
			}
			string mystr = "";
			foreach (var comp in bomb.Bomb.Faces)
			{
				mystr += comp.name;


				mystr += "\n";
			}
			throw new Exception(mystr);
		}

		if (Input.GetKeyDown(KeyCode.P))
		{
			TwitchGame game = FindObjectOfType<TwitchGame>();
			bomb = GameObject.FindObjectOfType<TwitchBomb>();
			List<TwitchModule> modules = new List<TwitchModule>();
			int modulesIndex = 0;
			foreach (var component in bomb.Bomb.BombComponents)
			{
				if (component.ComponentType.EqualsAny(ComponentTypeEnum.Empty, ComponentTypeEnum.Timer))
				{
					TwitchModule emptyModule = new TwitchModule();
					emptyModule.BombComponent = component;
					modules.Add(emptyModule);
				}
				else
				{
					modules.Add(game.Modules[modulesIndex]);
					modulesIndex++;
				}

			}
			BombGenerator gen = FindObjectOfType<BombGenerator>();
			string mystr = "";
			{

			}
			mystr += "END";
			throw new Exception(mystr);
		}

		if (Input.GetMouseButtonDown(1))
		{
			throw new Exception(Input.mousePosition.ToString());
		}

		// Flip 180 degrees
		if (Input.GetKeyDown(KeyCode.F))
		{
			StartCoroutine(gptntActions.Rotate180());
		}

		if (Input.GetKeyDown(KeyCode.Backslash))
		{
			InputInterceptor.EnableInput();
		}


		// Can potentially use FloatingHoldable.HoldStateEnum.Held, i.e. if user sends click and bomb is not held, then assume action is to hold bomb

		// Pick up the bomb when it is on the table
		if (Input.GetKeyDown(KeyCode.X))
		{
			bomb = GameObject.FindObjectOfType<TwitchBomb>();
			StartCoroutine(bomb.HoldBomb());
		}
		// Drop bomb onto table
		if (Input.GetKeyDown(KeyCode.C))
		{
			bomb = GameObject.FindObjectOfType<TwitchBomb>();
			StartCoroutine(bomb.LetGoBomb());
		}

		// Finds a twitch module, assumes it is a wire and then cuts the first wire
		if (Input.GetKeyDown(KeyCode.V))
		{
			TwitchModule module = GameObject.FindObjectOfType<TwitchModule>();
			WireSetComponentSolver solver = new WireSetComponentSolver(module);
			StartCoroutine(solver.RespondToCommandInternal("cut 1"));
		}

		if (Input.GetKeyDown(KeyCode.M))
		{
			TwitchModule module = GameObject.FindObjectOfType<TwitchModule>();
			module.Selectable.HandleInteract();
		}

		// Finds a twitch module and focuses onto it
		if (Input.GetKeyDown(KeyCode.B))
		{
			bomb = GameObject.FindObjectOfType<TwitchBomb>();
			TwitchModule module = GameObject.FindObjectOfType<TwitchModule>();
			StartCoroutine(bomb.Focus(module.Selectable, module.FocusDistance, module.FrontFace, true));
		}



		// rotate 90 degrees up
		if (Input.GetKeyDown(KeyCode.I))
		{
			StartCoroutine(gptntActions.Rotate90("up"));
		}
		// rotate 90 degrees down
		if (Input.GetKeyDown(KeyCode.K))
		{
			StartCoroutine(gptntActions.Rotate90("down"));
		}
		// rotate 90 degrees left
		if (Input.GetKeyDown(KeyCode.J))
		{
			StartCoroutine(gptntActions.Rotate90("left"));
		}
		// rotate 90 degrees right
		if (Input.GetKeyDown(KeyCode.L))
		{
			StartCoroutine(gptntActions.Rotate90("right"));
		}

		if (Input.GetKeyDown(KeyCode.T))
		{
			Vector2 mouseInput = new Vector2(
				Input.mousePosition.x / Screen.width,
				Input.mousePosition.y / Screen.height
				);
			gptntActions.Click(mouseInput.x, mouseInput.y);
			gptntActions.Release();
		}
		if (Input.GetKeyDown(KeyCode.U))
		{
			LogBombPosition();
		}

		if (Input.GetKeyDown(KeyCode.O))
		{
			string path = Path.Combine(Application.persistentDataPath, "screenshot.png");
			StartCoroutine(GetScreenshotViaRenderTexture((bytes) => {
				File.WriteAllBytes(path, bytes);
			}));
			SegmentSelectables(GetActiveSelectables().ToArray());
		}
		#endregion
	}

	private void LogBombPosition()
	{
		bool hasParent = bomb.Bomb.transform.parent != null;
		Transform bombTransform = bomb.Bomb.transform;

		while (hasParent)
		{
			GptntDebug.Log("Has parent");
			bombTransform = bombTransform.parent;
			hasParent = bombTransform.parent != null;
		}

		GptntDebug.Log(bombTransform.name);
		float x = bombTransform.position.x;
		float y = bombTransform.position.y;
		float z = bombTransform.position.z;
		GptntDebug.Log($"x: {x}, y: {y}, z: {z}");
		GptntDebug.Log("Newer");
		x = 0.1017131f;
		y = 1.090481f;
		z = -0.4161007f;
		bombTransform.position = new Vector3(x, y, z);
	}

	private void LogClick()
	{
		Vector2 mouseInput = new Vector2(
				Input.mousePosition.x / Screen.width,
				Input.mousePosition.y / Screen.height
				);
		GptntDebug.Log("x = " + mouseInput.x + "y = " + (1 - mouseInput.y));
	}

	private void SegmentSelectables(Selectable[] activeSelectables)
	{
		string path = Path.Combine(Application.persistentDataPath, "segmentation.png");
		GameObject[] objects = new GameObject[activeSelectables.Length];
		for (int i = 0; i < activeSelectables.Length; i++)
		{
			objects[i] = activeSelectables[i].gameObject;
		}

		StartCoroutine(segmentation.Capture(objects, (bytes) => {
			File.WriteAllBytes(path, bytes);
			}));
	}

	private void PrintActiveSelectables()
	{
		GptntDebug.Log("Parent: " + KTInputManager.Instance.SelectableManager.GetCurrentParent().name);
		GptntDebug.Log("Selectables: ");
		foreach(Selectable selectable in GetActiveSelectables())
		{
			string selectableName = selectable.name;
			GptntDebug.Log(selectableName);

		}
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
					bool isFront =  angleBetween < 90.0f;
					if(isFront == parentName.Equals("FrontFace")){
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
		if(!path.Equals("/health")) GptntDebug.Log("[HTTP Request] " + path);
		// Route handling with switch
		switch (path)
		{
			case "/bombinfo":
				responseString = GetBombInfo(response);
				break;
			case "/startmission":
				responseString = HandleStartMission(request, response); // Main thread
				break;
			case "/causestrike":
				responseString = HandleCauseStrike(request, response);
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
			default:
				responseString = "Unknown route.";
				break;
		}

		// Send response
		SendResponse(request, response, responseString);
	}

	private string HandleReset()
	{
		if(!gameState.Equals("Setup"))
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

		if(response.StatusCode != (int) HttpStatusCode.OK)
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
		+ "\"state\":\"" + state +"\""
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
		if(!gameState.EqualsAny("Lights On", "Lights Off") || !isStarted)
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Cannot send action to the game in " + gameState + " state";
		}

		string responseString = null;

		responseString = HandleActionOnMainThread(request, response);

		if(response.StatusCode != (int) HttpStatusCode.OK)
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
		otherSeed = seed;
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
			catch(MemoryModuleException ex)
			{
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

	private string HandleCauseStrike(HttpListenerRequest request, HttpListenerResponse response)
	{
		if (gameState.EqualsAny("Lights On", "Lights Off"))
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Cannot strike a bomb when in " + gameState;
		}
		string reason = request.QueryString["reason"];
		return CauseStrike(reason);
	}

	private string HandleScreenshot(HttpListenerResponse response)
	{
		byte[] imageBytes = null;
		var waitHandle = new ManualResetEvent(false);

		// Run this on the Unity main thread
		MainThreadQueue.Enqueue(() =>
		{
			// StartCoroutine must be called on the main thread
			StartCoroutine(GetScreenshot((img) =>
			{
				imageBytes = img;
				waitHandle.Set();
			}));
		});

		// Wait up to 500ms for the screenshot to be captured
		if (!waitHandle.WaitOne(500))
		{
			response.StatusCode = (int) HttpStatusCode.RequestTimeout;
			return "Failed to take screenshot";
		}

		if(imageBytes == null || imageBytes.Length == 0)
		{
			response.StatusCode = (int) HttpStatusCode.InternalServerError;
			return "Empty screenshot";
		}

		response.ContentType = "image/png";
		return Convert.ToBase64String(imageBytes);
	}
	
	private string HandleSegmentation(HttpListenerResponse response)
	{

		if(!gameState.Equals("Lights On"))
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
	#endregion

	private IEnumerator TimeStepCoroutine()
	{
		Time.timeScale = 1; // Unpause
		yield return new WaitForSeconds(timeStepSize / 1000f);
		if(isStarted)
			Time.timeScale = 0; // Pause
	}

	private void SendResponse(HttpListenerRequest request ,HttpListenerResponse response, string responseString)
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

	protected string StartMission(string seed, int timeLimit, int numStrikes, int needyTime, bool isFront, int optWidgets, List<String> components)
	{
		if (string.IsNullOrEmpty(seed)) {
			return "Please enter valid seed. e.g. seed=123";
		}

		if (timeLimit < 0) {
			return "Please enter a valid time limit. e.g. timeLimit=90";
		}

		if (numStrikes < 1)
		{
			return "Please enter a valid number of strikes. e.g. numStrikes=3";
		}

		if (needyTime < 0 || needyTime > timeLimit){
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
				CompType = (KMComponentPool.ComponentTypeEnum)Enum.Parse(typeof(KMComponentPool.ComponentTypeEnum), compString);
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

	protected string CauseStrike(string reason)
	{
		actions.Enqueue(delegate () { gameCommands.CauseStrike(reason); });

		return reason;
	}

	protected string GetBombInfo(HttpListenerResponse response)
	{
		if(!gameState.EqualsAny("Lights On", "Lights Off"))
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			return "Cannot get bomb info before the game starts";
		}
		if(bombInfo.IsBombPresent())
		{
			if(bombState == "NA")
			{
				bombState = "Active";
			}
		}
		else if(bombState == "Active")
		{
			bombState = "NA";
		}

		string time = bombInfo.GetFormattedTime();
		int strikes = bombInfo.GetStrikes();
		modules = GetListAsHTML(bombInfo.GetModuleNames());
		TwitchModule mod = GameObject.FindObjectOfType<TwitchModule>();
		ComponentSolver Solver = ComponentSolverFactory.CreateSolver(mod);
		var ModInfo = Solver.ModInfo;
		string id = ModInfo.moduleID;
		solvableModules = GetListAsHTML(bombInfo.GetSolvableModuleNames());
		solvedModules = GetListAsHTML(bombInfo.GetSolvedModuleNames());
		
		string responseString = string.Format(
			"<HTML><BODY>"
			+ "<span>Time: {0}</span><br>"
			+ "<span>Strikes: {1}</span><br>"
			+ "<span>Modules: {2}</span><br>"
			+ "<span>IDs: {6}</span><br>"
			+ "<span>Solvable Modules: {3}</span><br>"
			+ "<span>Solved Modules: {4}</span><br>"
			+ "<span>State: {5}</span><br>"
			+ "</BODY></HTML>", time, strikes, modules, solvableModules, solvedModules, bombState, id);

		return responseString;
	}

	protected void OnBombExplodes()
	{
		bombState = "Exploded";
	}

	protected void OnBombDefused()
	{
		bombState = "Defused";
	}

	protected string GetListAsHTML(List<string> list)
	{
		string listString = "";

		foreach(string s in list)
		{
			listString += s + ", ";
		}

		return listString;
	}

	protected IEnumerator GetScreenshot(Action<byte[]> callback)
	{
		return GetScreenshotViaRenderTexture(callback);
	}

	protected IEnumerator GetScreenshotViaRenderTexture(Action<byte[]> callback)
	{
		Camera.main.targetTexture = rawScreenRenderTexture;
		yield return new WaitForEndOfFrame();
		byte[] img = RenderTextureToPNGBytes(rawScreenRenderTexture);
		Camera.main.targetTexture = null;
		callback(img);
	}

	private Texture2D ConvertRenderTextureToTexture2D(RenderTexture rt)
	{
		RenderTexture.active = rt;
		tex.ReadPixels(rect, 0, 0);
		tex.Apply();
		RenderTexture.active = null;
		return tex;
	}

	private byte[] RenderTextureToPNGBytes(RenderTexture rt)
	{
		byte[] bytes = ConvertRenderTextureToTexture2D(rt).EncodeToPNG();
		return bytes;
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