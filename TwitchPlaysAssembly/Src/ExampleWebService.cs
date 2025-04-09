using UnityEngine;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System;
using System.Linq;
using System.Collections;
using System.Net.Configuration;
using Assets.Scripts.Platform.PS4.IO;
using Assets.Scripts.Missions;
using System.ComponentModel;
using Assets.Scripts.Input;
using System.IO;
// using Org.BouncyCastle.Asn1.X509;

public class ExampleWebService : MonoBehaviour
{
    KMBombInfo bombInfo;
    KMGameCommands gameCommands;
    string modules;
    string solvableModules;
    string solvedModules;
    string bombState;
    GameObject spawn;
    KMMission mission;

    Thread workerThread;
    Worker workerObject;
    Queue<Action> actions;

    public int timeStepSize = 250;
	float bombRotationX = 0f;
	float bombRotationZ = 0f;
	public bool bombStarted = false;


	TwitchBomb bomb;
	bool StartingFace;
	bool onFrontFace = true;
	bool onBackFace = false;
	bool onLeftSide = false;
	bool onRightSide = false;
	bool onTopFromFront = false;
	bool onTopFromBack = false;
	bool onTopFromLeftSide = false;
	bool onTopFromRightSide = false;
	bool onBottomFromBack = false;
	bool onBottomFromFront = false;
	bool onBottomFromLeftSide = false;
	bool onBottomFromRightSide = false;
	bool inMiddle = true;
	private string destinationLogPath;
    public string sourceLogPath = @".\logs\ktane.log";
	private string lastRead = "";

	// in-game milliseconds

	//public int TimeLimitSecs = 300;
	//public int NumStrikesBeforeExplosion = 3;
	//public int TimeBeforeNeedyActivationSecs = 150;
	//public bool FrontFaceOnly = true;
	//public int OptionalWidgetCount = 3;

	// [SerializeField] private List<KMComponentPool.ComponentTypeEnum> Modules = new List<KMComponentPool.ComponentTypeEnum>();

	//private void OnValidate()
	//{
	//    if (Modules.Count > 11)
	//    {
	//        Modules = Modules.GetRange(0, 11);
	//    }

	//    if (Modules.Count < 1)
	//    {
	//        Modules = Modules.GetRange(0, 1);
	//    }
	//}

	void Awake()
    {
        actions = new Queue<Action>();
        bombInfo = GetComponent<KMBombInfo>();
        bombInfo.OnBombExploded += OnBombExplodes;
        bombInfo.OnBombSolved += OnBombDefused;
        gameCommands = GetComponent<KMGameCommands>();
        bombState = "NA";
        spawn = new GameObject();
        mission = ScriptableObject.CreateInstance<KMMission>();
        // Create the thread object. This does not start the thread.
        workerObject = new Worker(this);
        workerThread = new Thread(workerObject.DoWork);
        // Start the worker thread.
        workerThread.Start(this);
	}


	void Start()
	{
		string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		destinationLogPath = $@".\{timestamp}mirrored_log.txt";
	InvokeRepeating(nameof(CopyLogContents), 1f, 0.25f);
	}

	void Update()
	{
		if (bombStarted)
		{
			bombStarted = false;
			bombRotationZ = 0;
			bombRotationX = 0;
			StartingFace = KTInputManager.Instance.SelectableManager.GetActiveFace() == FaceEnum.Front;
			onFrontFace = true;
			onBackFace = false;
			onLeftSide = false;
			onRightSide = false;
			onTopFromFront = false;
			onTopFromBack = false;
			onTopFromLeftSide = false;
			onTopFromRightSide = false;
			onBottomFromFront = false;
			onBottomFromBack = false;
			onBottomFromLeftSide = false;
			onBottomFromRightSide = false;
			inMiddle = true;
		}

		if (actions.Count > 0)
		{
			Action action = actions.Dequeue();
			action();
		}

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
			Rotation180();
		}

		// Disabling mouse input ensures correct functioning of other commands- should be used before any other
		if (Input.GetKeyDown(KeyCode.Z))
		{

			throw new Exception($"Initial rotation: {bombRotationZ}");

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
		if (Input.GetKeyDown(KeyCode.G))
		{
			Rotation90("up");
		}
		// rotate 90 degrees down
		if (Input.GetKeyDown(KeyCode.H))
		{
			Rotation90("down");
		}
		// rotate 90 degrees right
		if (Input.GetKeyDown(KeyCode.J))
		{
			Rotation90("left");
		}
		// rotate 90 degrees left
		if (Input.GetKeyDown(KeyCode.K))
		{
			Rotation90("right");
		}

		if (Input.GetKeyDown(KeyCode.T))
		{
			StartCoroutine(GetScreenshotViaScreenCapture((bytes) => {
				System.IO.File.WriteAllBytes("screenshot.png", bytes);
			}));
		}

	}

    void OnDestroy()
    {
        workerThread.Abort();
        workerObject.Stop();
	}

    // This example requires the System and System.Net namespaces.
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

    private void HandleRequest(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        string responseString;
        string path = request.Url.AbsolutePath.ToLowerInvariant(); // Normalise the path for comparison

        // Route handling with switch
        switch (path)
        {
            case "/bombinfo":
                responseString = GetBombInfo();
                break;
            case "/startmission":
                responseString = HandleStartMission(request);
                break;
            case "/causestrike":
                responseString = HandleCauseStrike(request);
                break;
            case "/screenshot":
                responseString = HandleScreenshot(response);
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
			case "/rotation":
				responseString = HandleRotation(request);
				break;
            case "/selectables":
                responseString = HandleBombChildren();
                break;
            default:
                responseString = "Unknown route.";
                break;
        }

        // Send response
        SendResponse(response, responseString);
    }

    private string HandleBombChildren()
    {
        KMBomb bomb = GameObject.FindObjectOfType<KMBomb>();
        return GetHierarchyString(bomb.gameObject);
    }

    string GetHierarchyString(GameObject obj, int level = 0)
    {
        // Start the string with the current GameObject's name and indent based on level
        string hierarchy = new string('-', level * 2) + obj.name + "\n";

        // Loop through all children
        foreach (Transform child in obj.transform)
        {
            // Append the child's hierarchy to the string (recursive call)
            hierarchy += GetHierarchyString(child.gameObject, level + 1);
        }

        return hierarchy; // Return the entire hierarchy as a string
    }

	private void Rotation90(string direction)
	{
		bomb = GameObject.FindObjectOfType<TwitchBomb>();

		if (direction.Equals("right"))
		{	
			InputInterceptor.DisableInput();
			if (bombRotationZ == 180)
			{
				bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, 0));

			}
			else
			{
				bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, -bombRotationZ));

			}
			StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));
			if (onFrontFace)
			{
				bombRotationZ = 90;
			}
			else if (onBackFace)
			{
				bombRotationZ = -90;
			}
			else if (onLeftSide)
			{
				bombRotationZ = 0;
			}
			else if (onRightSide)
			{
				bombRotationZ = 180;
			}
			bomb.RotateByLocalQuaternion(Quaternion.Euler(bombRotationX, 0, bombRotationZ));
			alignFace90R();
		}
		if (direction.Equals("left"))
		{
			InputInterceptor.DisableInput();
			if (bombRotationZ == 180)
			{
				bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, 0));

			}
			else
			{
				bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, -bombRotationZ));

			}
			StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));
			if (onFrontFace)
			{
				bombRotationZ = -90;
			}
			else if (onBackFace)
			{
				bombRotationZ = 90;
			}
			else if (onLeftSide)
			{
				bombRotationZ = 180;
			}
			else if (onRightSide)
			{
				bombRotationZ = 0;
			}
			bomb.RotateByLocalQuaternion(Quaternion.Euler(bombRotationX, 0, bombRotationZ));
			alignFace90L();
		}

		if (direction.Equals("up"))
		{
			if (bombRotationX < 90)
			{
				InputInterceptor.DisableInput();
				if (bombRotationZ == 180)
				{
					bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, 0));

				}
				else
				{
					bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, -bombRotationZ));

				}
				StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));


				bombRotationX += 90;
				bomb.RotateByLocalQuaternion(Quaternion.Euler(bombRotationX, 0, bombRotationZ));

			}
		}

		if (direction.Equals("down"))
		{
			if (bombRotationX > -90)
			{
				InputInterceptor.DisableInput();
				if (bombRotationZ == 180)
				{
					bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, 0));

				}
				else
				{
					bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, -bombRotationZ));

				}
				StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));


				bombRotationX -= 90;
				bomb.RotateByLocalQuaternion(Quaternion.Euler(bombRotationX, 0, bombRotationZ));
			}
		}

		if (bombRotationX == 0)
		{
			if (bombRotationZ % 360 == 0)
			{
				StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));
				InputInterceptor.EnableInput();
			}
			else if (bombRotationZ % 180 == 0)
			{
				StartCoroutine(bomb.MyForceHeldRotation(!StartingFace, 0f));
				InputInterceptor.EnableInput();
			}
		}
		//throw new Exception($"X rotation: {bombRotationX}\n Z rotation: {bombRotationZ}");
		throw new Exception($"X: {bombRotationX}\n Z:{bombRotationZ}");
	}

		private void Rotation180()
	{
		bomb = GameObject.FindObjectOfType<TwitchBomb>();

		InputInterceptor.DisableInput();
		if (bombRotationZ == 180)
		{
			bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, 0));

		}
		else
		{
			bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, -bombRotationZ));

		}
		StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));

		if (onFrontFace)
		{
			bombRotationZ = 180;
		}
		else if (onBackFace)
		{
			bombRotationZ = 0;
		}
		else if (onLeftSide)
		{
			bombRotationZ = 90;
		}
		else if (onRightSide)
		{
			bombRotationZ = -90;
		}
		bomb.RotateByLocalQuaternion(Quaternion.Euler(bombRotationX, 0, bombRotationZ));

		if (bombRotationX == 0)
		{
			if (bombRotationZ % 360 == 0)
			{
				StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));
				InputInterceptor.EnableInput();
			}
			else if (bombRotationZ % 180 == 0)
			{
				StartCoroutine(bomb.MyForceHeldRotation(!StartingFace, 0f));
				InputInterceptor.EnableInput();
			}
		}
		alignFace180();
	}

	private void alignFace180()
	{
		if (onFrontFace)
		{
			onFrontFace = false;
			onBackFace = true;
		}
		else if (onBackFace)
		{
			onBackFace = false;
			onFrontFace = true;
		}
		else if (onLeftSide)
		{
			onLeftSide = false;
			onRightSide = true;
		}
		else if (onRightSide)
		{
			onRightSide = false;
			onLeftSide = true;
		}
	}


	private void alignFace90L()
	{
		if (onFrontFace)
		{
			onFrontFace = false;
			onLeftSide = true;
		}
		else if (onBackFace)
		{
			onBackFace = false;
			onRightSide = true;
		}
		else if (onLeftSide)
		{
			onLeftSide = false;
			onBackFace = true;
		}
		else if (onRightSide)
		{
			onRightSide = false;
			onFrontFace = true;
		}
	}

	private void alignFace90R()
	{
		if (onFrontFace)
		{
			onFrontFace = false;
			onRightSide = true;
		}
		else if (onBackFace)
		{
			onBackFace = false;
			onLeftSide = true;
		}
		else if (onLeftSide)
		{
			onLeftSide = false;
			onFrontFace = true;
		}
		else if (onRightSide)
		{
			onRightSide = false;
			onBackFace = true;
		}
	}

	private void alignFace90U()
	{
		if (inMiddle && onFrontFace)
		{
			inMiddle = false;
			onTopFromFront = true;
		}

		else if (inMiddle && onBackFace)
		{
			inMiddle = false;
			onTopFromBack = true;
		}

		//else if (inMiddle && onLeftSideFromFront)
		//{
		//	inMiddle=false;
		//	onTopFromLeftSide = true;
		//}

		//else if (inMiddle && onRightSide)
		//{
		//	inMiddle=false;
		//	onTopFromRightSide = true;
		//}

		else if (onBottomFromBack)
		{
			onBottomFromBack = false;
			inMiddle = true;
		}
		else if (onBottomFromFront)
		{
			onBottomFromFront = false;
			inMiddle = true;
		}
		else if (onBottomFromLeftSide)
		{
			onBottomFromLeftSide = false;
			inMiddle = true;
		}

		else if (onTopFromRightSide)
		{
			onBottomFromRightSide = false;
			inMiddle = true;
		}
	}

	private void alignFace90D()
	{
		if (inMiddle && onFrontFace)
		{
			inMiddle = false;
			onBottomFromFront = true;
		}
		else if (inMiddle && onBackFace)
		{
			inMiddle = false;
			onBottomFromBack = true;
		}

		//else if (inMiddle && onLeftSide)
		//{
		//	inMiddle = false;
		//	onBottomFromLeftSide = true;
		//}

		//else if (inMiddle && onRightSide)
		//{
		//	inMiddle = false;
		//	onBottomFromRightSide = true;
		//}

		else if (onTopFromFront)
		{
			onTopFromFront = false;
			inMiddle = true;
		}

		else if (onTopFromLeftSide)
		{
			onTopFromLeftSide = false;
			inMiddle = true;
		}

		else if (onTopFromRightSide)
		{
			onTopFromRightSide = false;
			inMiddle = true;
		}

		else if (onTopFromBack)
		{
			onTopFromBack = false;
			inMiddle = true;
		}
	}





	private string HandleStartMission(HttpListenerRequest request)
    {
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

	private string HandleRotation(HttpListenerRequest request)
	{
		string direction = request.QueryString.Get("action");
		if (direction.Equals("flip"))
		{
			Rotation180();
			return "flipped bomb 180 degrees";
		}
		else if (direction.Equals("left"))
		{
			Rotation90(direction);
			return "flipped bomb left by 90 degrees";
		} 
		else if (direction.Equals("right"))
		{
			Rotation90(direction);
			return "flipped bomb right by 90 degrees";
		} 
		else if (direction.Equals("up"))
		{
			Rotation90(direction);
			return "flipped bomb up by 90 degrees";
		}
		else if (direction.Equals("down"))
		{
			Rotation90(direction);
			return "flipped bomb down by 90 degrees";
		}
		else
		{
			return "invalid direction. Valid directions: left, right, up, down, flip";
		}
	}

    private string HandleCauseStrike(HttpListenerRequest request)
    {
        string reason = request.QueryString["reason"];
        return CauseStrike(reason);
    }

    private string HandleScreenshot(HttpListenerResponse response)
    {
        byte[] imageBytes = null;
        StartCoroutine(GetScreenshotViaScreenCapture((img) => imageBytes = img));
		var stopwatch = new System.Diagnostics.Stopwatch();
		stopwatch.Start();
		while (imageBytes == null)
        {
			if (stopwatch.ElapsedMilliseconds >= 500)
            {
				return "Failed to take screenshot";
            }
            // Wait for the screenshot to be taken
        }
		// base encode as a string
		response.ContentType = "image/png";
        return Convert.ToBase64String(imageBytes);
    }

    private string HandleSetTimescale(HttpListenerRequest request)
    {
        string value = request.QueryString.Get("value");
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
        StartCoroutine(TimeStepCoroutine());
        return "Paused after " + timeStepSize + " in-game milliseconds";
    }

    private IEnumerator TimeStepCoroutine()
    {
        Time.timeScale = 1; // Unpause
        yield return new WaitForSeconds(timeStepSize / 1000f);
        Time.timeScale = 0; // Pause
    }

    private void SendResponse(HttpListenerResponse response, string responseString)
    {
        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
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

    protected string GetBombInfo()
    {
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

	protected IEnumerator GetScreenshotViaScreenCapture(System.Action<byte[]> callback)
	{
		yield return new WaitForEndOfFrame();
		byte[] img = ScreenCapture.CaptureScreenshotAsTexture().EncodeToPNG();
		callback(img);
	}
	
	protected IEnumerator GetScreenshotViaRenderTexture(System.Action<byte[]> callback)
    {
		RenderTexture renderTexture = new RenderTexture(Screen.width, Screen.height, 24);
		RenderTexture oldTexture = Camera.main.targetTexture;
		Camera.main.targetTexture = renderTexture;
		yield return new WaitForEndOfFrame();
		byte[] img = RenderTextureToPNGBytes(renderTexture); 
		callback(img);
		//GptntConsole.WriteLine( "Remove line 988 if null: " + oldTexture); TODO: Test this out
		//Camera.main.targetTexture = null;
		Camera.main.targetTexture = oldTexture;
	}
	private Texture2D ConvertRenderTextureToTexture2D(RenderTexture rt)
	{
		Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
		RenderTexture.active = rt;
		tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
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
            // Create a listener.
            listener = new HttpListener();
            // Add the prefixes.
            foreach (string s in new string[] { "http://localhost:8085/" })
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
				using (StreamReader reader = new StreamReader(fs))
				{
					string currentContents = reader.ReadToEnd();
					if (currentContents != lastRead)
					{
						File.WriteAllText(destinationLogPath, currentContents);
						lastRead = currentContents;
					}
				}
			}
		}
		catch (IOException ex)
		{
			Debug.LogWarning("Failed to read log: " + ex.Message);
		}
	}
}