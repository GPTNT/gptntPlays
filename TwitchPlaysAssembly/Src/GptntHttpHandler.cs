using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using UnityEngine;

public class GptntHttpHandler : MonoBehaviour
{
	Thread workerThread;
	Worker workerObject;

	RequestHandlers requestHandlers;

	private Dictionary<string, Func<HttpListenerRequest, HttpListenerResponse, string>> routeHandlers;

	private void Awake()
	{
		requestHandlers = GetComponent<RequestHandlers>();
		StartHttpWorker();
		SetupRoutes();
	}

	private void StartHttpWorker()
	{
		workerObject = new Worker(this);
		workerThread = new Thread(workerObject.DoWork);
		workerThread.Start();
	}

	private void SetupRoutes()
	{
		routeHandlers = new Dictionary<string, Func<HttpListenerRequest, HttpListenerResponse, string>>()
		{
			["/startmission"] = requestHandlers.HandleStartMission,
			["/settimescale"] = requestHandlers.HandleSetTimescale,
			["/setstepunit"] = requestHandlers.HandleSetStepUnit,
			["/timestep"] = requestHandlers.HandleTimeStep,
			["/action"] = requestHandlers.HandleAction,
			["/buffer"] = requestHandlers.HandleObservationBuffer,
			["/health"] = requestHandlers.HandleHealth,
			["/reset"] = requestHandlers.HandleReset,
			["/state"] = requestHandlers.HandleGetState, 
			["/random"] = requestHandlers.HandleRandomSolve,
		};
	}

	private void HandleRequest(HttpListenerContext context)
	{
		var path = context.Request.Url.AbsolutePath.ToLowerInvariant();
		//if (!path.Equals("/health")) GptntDebug.Log("[HTTP Request] " + path);
		GptntDebug.Log("[HTTP Request] " + path);

		string responseString = routeHandlers.TryGetValue(path, out var handler)
			? handler(context.Request, context.Response)
			: "Unknown route.";

		SendResponse(context.Request, context.Response, responseString);
	}

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
		output.Close();
	}

	public class Worker
	{
		GptntHttpHandler host;
		HttpListener listener;

		public Worker(GptntHttpHandler h)
		{
			host = h;
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

			host.Listener(listener);
		}

		public void Stop()
		{
			listener.Stop();
		}
	}

	public void Listener(HttpListener listener)
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

	void OnDestroy()
	{
		workerThread.Abort();
		workerObject.Stop();
	}
}
