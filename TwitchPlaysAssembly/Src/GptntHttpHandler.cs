using System;
using System.Net;
using System.Threading;
using UnityEngine;

public class GptntHandler : MonoBehaviour
{
	Thread workerThread;
	Worker workerObject;

	private void Awake()
	{
		// Create the thread object. This does not start the thread.
		workerObject = new Worker(this);
		workerThread = new Thread(workerObject.DoWork);
		// Start the worker thread.
		workerThread.Start(this);
	}

	public class Worker
	{
		GptntHandler host;
		HttpListener listener;

		public Worker(GptntHandler h)
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

