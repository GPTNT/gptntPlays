using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using UnityEngine;
using log4net;
using TwitchPlaysAssembly;
using System.Security.AccessControl;

public class TraceContext
{
	public string TraceId { get; set; }
	public string SpanId { get; set; }
}


public class GptntHttpHandler : MonoBehaviour
{
	Thread workerThread;
	Worker workerObject;
	RequestHandlers requestHandlers;

	private Dictionary<string, Func<HttpListenerRequest, HttpListenerResponse, string>> routeHandlers;

	// Add trace context holder
	private string currentTraceId;
	private string currentSpanId;

	private static ILog log = LogManager.GetLogger("HttpHandler");

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
			["/detonate"] = requestHandlers.HandleDetonateBomb,
			["/solve"] = requestHandlers.HandleSolveBomb,
		};
	}

	private void HandleRequest(HttpListenerContext context)
	{
		var path = context.Request.Url.AbsolutePath.ToLowerInvariant();

		// Extract trace context from incoming request
		string traceParent = context.Request.Headers["traceparent"];

		// Parse trace context
		string traceId, parentSpanId;
		TraceContext tc = ParseTraceParent(traceParent);
		traceId = tc.TraceId;
		parentSpanId = tc.SpanId;

		// Create span for this HTTP request
		var span = new OpenTelemetrySpan(
			$"HTTP {context.Request.HttpMethod} {path}",
			traceId,
			parentSpanId
		);
		if(!path.Equals("/health")) // Remove logs for /health not to clog logs
			log.Debug(GptntDebug.FormatMessage("Received request to: " + path, span.GetTraceId(), span.GetSpanId()));

		// Add HTTP attributes
		span.SetAttribute("http.method", context.Request.HttpMethod);
		span.SetAttribute("http.route", path);
		span.SetAttribute("http.url", context.Request.Url.ToString());
		span.SetAttribute("http.user_agent", context.Request.UserAgent ?? "unknown");

		// Store for child spans (if handlers need to create nested spans)
		currentTraceId = span.GetTraceId();
		currentSpanId = span.GetSpanId();

		// Set trace context in log4net for logs during this request
		GlobalContext.Properties["trace_id"] = currentTraceId;
		GlobalContext.Properties["span_id"] = currentSpanId;

		string responseString;
		bool success = true;

		try
		{
			// Call the route handler
			if (routeHandlers.TryGetValue(path, out var handler))
			{
				responseString = handler(context.Request, context.Response);
				span.SetAttribute("http.status_code", 200);
			}
			else
			{
				responseString = "Unknown route.";
				span.SetAttribute("http.status_code", 404);
				success = false;
			}
		}
		catch (Exception ex)
		{
			log.Error(GptntDebug.FormatMessage($"Error when handling {path}:", span.GetTraceId(), span.GetSpanId()), ex);
			responseString = $"Error: {ex.Message}";
			span.SetAttribute("http.status_code", 500);
			span.SetAttribute("error", true);
			span.SetAttribute("error.type", ex.GetType().Name);
			span.SetAttribute("error.message", ex.Message);
			span.SetAttribute("error.stack", ex.StackTrace);
			success = false;
		}
		finally
		{
			// Clean up trace context
			GlobalContext.Properties.Remove("trace_id");
			GlobalContext.Properties.Remove("span_id");

			// End span
			span.End(success);
		}

		SendResponse(context.Request, context.Response, responseString);
	}

	// Helper to parse W3C traceparent header
	private TraceContext ParseTraceParent(string traceParent)
	{
		if (string.IsNullOrEmpty(traceParent))
			return new TraceContext();

		// Format: version-trace_id-parent_id-trace_flags
		// Example: 00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01
		var parts = traceParent.Split('-');
		if (parts.Length >= 3)
		{
			return new TraceContext { TraceId = parts[1], SpanId = parts[2] };
		}

		return new TraceContext();
	}

	// Expose current trace context for handlers that want to create child spans
	public TraceContext GetCurrentTraceContext()
	{
		return new TraceContext { TraceId = currentTraceId, SpanId = currentSpanId };
	}

	private void SendResponse(HttpListenerRequest request, HttpListenerResponse response, string responseString)
	{
		byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);

		response.AddHeader("Access-Control-Allow-Origin", "*");
		response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
		response.AddHeader("Access-Control-Allow-Headers", "Content-Type, traceparent, tracestate"); // Add trace headers
		
		if (response.StatusCode == (int) HttpStatusCode.OK && request.HttpMethod == "OPTIONS")
		{
			response.ContentLength64 = 0;
			response.OutputStream.Close();
			return;
		}

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

		public void DoWork()
		{
			string port = Environment.GetEnvironmentVariable("port");
			if (port == "" || port is null)
			{
				port = "8085";
			}

			listener = new HttpListener();
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
				log.Error(GptntDebug.FormatMessage("Error processing request: "), ex);
			}
		}
	}

	void OnDestroy()
	{
		workerThread.Abort();
		workerObject.Stop();
	}
}
