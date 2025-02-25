using UnityEngine;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System;
using System.Linq;
using System.Collections;

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

    public int timeStepSize = 250; // in-game milliseconds

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

    void Update()
    {
        if(actions.Count > 0)
        {
            Action action = actions.Dequeue();
            action();
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

    private string HandleCauseStrike(HttpListenerRequest request)
    {
        string reason = request.QueryString["reason"];
        return CauseStrike(reason);
    }

    private string HandleScreenshot(HttpListenerResponse response)
    {
        byte[] imageBytes = null;
        StartCoroutine(GetScreenshot((img) => imageBytes = img));
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
        solvableModules = GetListAsHTML(bombInfo.GetSolvableModuleNames());
        solvedModules = GetListAsHTML(bombInfo.GetSolvedModuleNames());
        
        string responseString = string.Format(
            "<HTML><BODY>"
            + "<span>Time: {0}</span><br>"
            + "<span>Strikes: {1}</span><br>"
            + "<span>Modules: {2}</span><br>"
            + "<span>Solvable Modules: {3}</span><br>"
            + "<span>Solved Modules: {4}</span><br>"
            + "<span>State: {5}</span><br>"
            + "</BODY></HTML>", time, strikes, modules, solvableModules, solvedModules, bombState);

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

    protected IEnumerator GetScreenshot(System.Action<byte[]> callback)
    {   
        //yield return new WaitForSecondsRealtime(1); // to test the time out 
        yield return new WaitForEndOfFrame();
        //byte[] img = ScreenCapture.CaptureScreenshotAsTexture().EncodeToPNG();
        //callback(img);
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
}