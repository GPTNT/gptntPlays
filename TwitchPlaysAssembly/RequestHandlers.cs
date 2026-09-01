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
using System.Globalization;
using TwitchPlaysAssembly;
using System.IO;

public class RequestHandlers : MonoBehaviour
{
	private float timeStepSize;
	KMMission mission;
	GameObject spawn;
	KMGameCommands gameCommands;

	GptntStates gptntStates;
	GptntActions gptntActions;
	GptntBuffer gptntBuffer;
	GptntAudioBuffer gptntAudioBuffer;
	Segmentation segmentation;
	GptntHttpHandler httpHandler;
	MagicSolver magic;
	LotterySolver lottery;

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
		lottery = GetComponent<LotterySolver>();

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
		if (Input.GetKeyDown(KeyCode.C))
		{
			gptntActions.Click(0.4f, 0.6f);
		}
		if (Input.GetKeyDown(KeyCode.X))
		{
			gptntActions.ZoomOut();
		}
	}

	public string HandleGetModules(HttpListenerRequest request, HttpListenerResponse response)
	{
		KMGameInfo gameInfo = FindObjectOfType<KMGameInfo>();
		var modules = gameInfo.GetAvailableModuleInfo()
			.Where(m => !m.IsNeedy)
			.Select(m =>
			{
				string id = m.IsMod ? m.ModuleId : m.ModuleType.ToString();
				string displayName = ComponentSolverFactory.GetModuleInfo(id, false).moduleDisplayName;
				return new { id, displayName, isMod = m.IsMod };
			})
			.OrderBy(m => m.displayName)
			.ToList();

		response.ContentType = "application/json";
		return JsonConvert.SerializeObject(modules, Formatting.Indented);
	}

	public string HandleDebug(HttpListenerRequest request, HttpListenerResponse response)
	{
		log.Debug($"Current face is {KTInputManager.Instance.SelectableManager.GetActiveFace()}");
		return $"Current face is {KTInputManager.Instance.SelectableManager.GetActiveFace()}";
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
		GptntDebug.ResetLogFormat();
		log.Debug(GptntDebug.FormatMessage("reset successful", span.GetTraceId(), span.GetSpanId()));
		span.End(true);
		return "Nuh uh";
	}

	public string HandleHealth(HttpListenerRequest request, HttpListenerResponse response)
	{
		return gptntStates.gameState.ToString();
	}

	public string HandleOldObservationBuffer(HttpListenerRequest request, HttpListenerResponse response)
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

	public void HandleObservationBuffer(HttpListenerRequest request, HttpListenerResponse response)
	{
		response.ContentType = "application/octet-stream";
		response.StatusCode = 200;
		byte[] rawSegmentation = GetRawSegmentation(response);
		log.Debug("Got the raw segmentation, status code: " + response.StatusCode);
		if (response.StatusCode == (int) HttpStatusCode.RequestTimeout)
			return;
		bool segmentationIncluded = rawSegmentation != null;

		RawObservationPayload rawObservation = gptntBuffer.GetRawBufferData();
		int frameCount = rawObservation.rawFrames.Count;
		log.Debug("All frames = " + frameCount);

		int totalSize = sizeof(bool) + sizeof(int) * 3; // for the segmentation bool, image count, image height, and image width
		foreach (byte[] frame in rawObservation.rawFrames)
			totalSize += frame.Length;
		if (rawSegmentation != null)
			totalSize += rawSegmentation.Length;

		response.ContentLength64 = totalSize;
		log.Debug("Content size: " + totalSize);

		BinaryWriter writer = new BinaryWriter(response.OutputStream);
		writer.Write(segmentationIncluded);
		writer.Write(frameCount);
		writer.Write(rawObservation.frameHeight); // Prolly safe to assume observation frames and segm is same size
		writer.Write(rawObservation.frameWidth);
		foreach (byte[] frameData in rawObservation.rawFrames)
		{
			writer.Write(frameData, 0, frameData.Length);
		}
		if (rawSegmentation != null)
		{
			writer.Write(rawSegmentation, 0, rawSegmentation.Length);
			log.Debug("Wrote the segmentation");
		}
		writer.Flush();
		log.Debug("Finished");
	}

	public void HandleAudio(HttpListenerRequest request, HttpListenerResponse response)
	{
		var traceContext = httpHandler.GetCurrentTraceContext();
		OpenTelemetrySpan span = new OpenTelemetrySpan("audio.read", traceContext.TraceId, traceContext.SpanId);

		if (gptntAudioBuffer == null)
			gptntAudioBuffer = GetComponent<GptntAudioBuffer>();

		AudioRingBuffer ring = gptntAudioBuffer != null ? gptntAudioBuffer.Ring : null;
		if (ring == null)
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			WriteText(response, "Audio buffer not initialized yet");
			span.End(false);
			return;
		}

		short[] samples;
		long newCursor;
		long dropped = 0;
		try
		{
			string since = request.QueryString.Get("since");
			if (since != null)
			{
				samples = ring.ReadSince(long.Parse(since, CultureInfo.InvariantCulture), out newCursor, out dropped);
			}
			else
			{
				string secondsRaw = request.QueryString.Get("seconds");
				float seconds = string.IsNullOrEmpty(secondsRaw) ? 5f : float.Parse(secondsRaw, CultureInfo.InvariantCulture);
				samples = ring.ReadLast((int) (seconds * ring.SampleRate), out newCursor);
			}
		}
		catch (Exception ex)
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			log.Error(GptntDebug.FormatMessage("Could not parse audio request", span.GetTraceId(), span.GetSpanId()), ex);
			WriteText(response, "Could not parse request");
			span.End(false);
			return;
		}

		byte[] wav = GptntAudioBuffer.ToWav(samples, ring.SampleRate);

		response.ContentType = "audio/wav";
		response.Headers["X-Audio-Cursor"] = newCursor.ToString(CultureInfo.InvariantCulture);
		response.Headers["X-Sample-Rate"] = ring.SampleRate.ToString(CultureInfo.InvariantCulture);
		response.Headers["X-Channels"] = "1";
		response.Headers["X-Audio-Dropped-Samples"] = dropped.ToString(CultureInfo.InvariantCulture);
		response.ContentLength64 = wav.Length;
		response.OutputStream.Write(wav, 0, wav.Length);

		span.SetAttribute("audio.samples", samples.Length);
		span.SetAttribute("audio.dropped", dropped);
		span.End(true);
	}

	public void HandleAtomicObservation(HttpListenerRequest request, HttpListenerResponse response)
	{
		var traceContext = httpHandler.GetCurrentTraceContext();
		OpenTelemetrySpan span = new OpenTelemetrySpan("observation.atomic", traceContext.TraceId, traceContext.SpanId);

		if (!StateEqualsAny(GptntStates.GameState.LightsOn))
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			WriteText(response, "Cannot capture an observation in " + gptntStates.gameState + " state");
			span.End(false);
			return;
		}

		AtomicObservationRequest observationRequest;
		try
		{
			observationRequest = new AtomicObservationRequest
			{
				AnchorFrameSequence = ParseOptionalLong(request.QueryString.Get("anchorFrameSequence")),
				Epoch = ParseOptionalLong(request.QueryString.Get("epoch")),
				AudioCursor = ParseOptionalLong(request.QueryString.Get("audioCursor")),
			};
		}
		catch (FormatException ex)
		{
			response.StatusCode = (int) HttpStatusCode.BadRequest;
			WriteText(response, ex.Message);
			span.End(false);
			return;
		}

		AtomicObservationSnapshot snapshot = null;
		Exception captureError = null;
		ManualResetEvent waitHandle = new ManualResetEvent(false);

		// Unity rendering APIs are main-thread-only. The HTTP worker waits only for
		// the immutable snapshot; the larger binary response is written afterward on
		// the worker so the Unity main loop is not occupied by network writes.
		MainThreadQueue.Enqueue(() => StartCoroutine(CaptureAtomicObservation(
			observationRequest,
			result =>
			{
				snapshot = result;
				waitHandle.Set();
			},
			error =>
			{
				captureError = error;
				waitHandle.Set();
			})));

		if (!waitHandle.WaitOne(5000))
		{
			response.StatusCode = (int) HttpStatusCode.RequestTimeout;
			WriteText(response, "Timed out while capturing the observation");
			span.End(false);
			return;
		}
		if (captureError != null)
		{
			response.StatusCode = (int) HttpStatusCode.InternalServerError;
			log.Error(GptntDebug.FormatMessage("Could not capture atomic observation", span.GetTraceId(), span.GetSpanId()), captureError);
			WriteText(response, "Could not capture the observation");
			span.End(false);
			return;
		}

		AtomicObservationWriter.Write(response, snapshot);
		span.SetAttribute("observation.frames", snapshot.Frames.rawFrames.Count);
		span.SetAttribute("observation.audio_samples", snapshot.AudioSamples.Length);
		span.SetAttribute("observation.coverage_gap", snapshot.Frames.coverageGap || snapshot.AudioDroppedSamples > 0);
		span.End(true);
	}

	private IEnumerator CaptureAtomicObservation(
		AtomicObservationRequest request,
		Action<AtomicObservationSnapshot> onSuccess,
		Action<Exception> onError)
	{
		// Waiting for the rendered frame gives the observation a clear boundary. No
		// game update can occur while the following synchronous captures run.
		yield return new WaitForEndOfFrame();

		try
		{
			// Capture the current RGB image without adding it to the periodic video ring.
			// This image is the background for the SoM mask returned below.
			TimedFrame currentImage = gptntBuffer.CaptureCurrentImage();
			Selectable[] selectables = GetActiveSelectables().ToArray();
			GameObject[] objects = new GameObject[selectables.Length];
			for (int index = 0; index < selectables.Length; index++)
				objects[index] = selectables[index].gameObject;

			// Capture the mask immediately, on the same main-thread turn, so it labels
			// the exact scene represented by currentImage.
			byte[] rawSegmentation = segmentation.CaptureRawNow(objects);
			TimedRawObservationPayload frames = gptntBuffer.GetTimedRawBufferData(
				request.AnchorFrameSequence,
				request.Epoch);
			// Video and audio share the newest regularly scheduled frame as their end
			// boundary. Keeping that boundary independent of request timing preserves
			// the configured capture cadence and the ring's time horizon.
			if (frames.frameTiming.Count == 0)
				throw new InvalidOperationException("Video buffer does not contain a frame yet");
			FrameTimingPayload videoEndFrame = frames.frameTiming[frames.frameTiming.Count - 1];

			if (gptntAudioBuffer == null)
				gptntAudioBuffer = GetComponent<GptntAudioBuffer>();
			AudioRingBuffer ring = gptntAudioBuffer != null ? gptntAudioBuffer.Ring : null;
			if (ring == null)
				throw new InvalidOperationException("Audio buffer not initialized yet");

			long requestedAudioStart;
			if (request.AudioCursor.HasValue)
				requestedAudioStart = request.AudioCursor.Value;
			else if (frames.frameTiming.Count > 0)
				// On the first request, align audio with the first returned video frame.
				requestedAudioStart = frames.frameTiming[0].audioCursor;
			else
				requestedAudioStart = ring.GetOldestCursor();

			long audioStart;
			long audioEnd;
			long audioDropped;
			// End audio at the newest periodic frame. The current SoM image is newer and
			// remains separate, so requesting an image cannot change the video's timing.
			short[] audio = ring.ReadBetween(
				requestedAudioStart,
				videoEndFrame.audioCursor,
				out audioStart,
				out audioEnd,
				out audioDropped);

			onSuccess(new AtomicObservationSnapshot
			{
				Frames = frames,
				CurrentImage = currentImage.Pixels,
				CurrentImageTiming = FrameTimingPayload.FromFrame(currentImage),
				Segmentation = rawSegmentation,
				AudioSamples = audio,
				AudioSampleRate = ring.SampleRate,
				RequestedAudioCursor = request.AudioCursor,
				AudioStartCursor = audioStart,
				AudioEndCursor = audioEnd,
				AudioDroppedSamples = audioDropped,
				RequestedEpoch = request.Epoch,
				RequestedAnchorFrameSequence = request.AnchorFrameSequence,
			});
		}
		catch (Exception ex)
		{
			onError(ex);
		}
	}

	private static long? ParseOptionalLong(string raw)
	{
		if (string.IsNullOrEmpty(raw))
			return null;
		long value;
		if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
			throw new FormatException("Expected an integer cursor, got: " + raw);
		return value;
	}

	private static void WriteText(HttpListenerResponse response, string text)
	{
		byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
		response.ContentLength64 = bytes.Length;
		response.OutputStream.Write(bytes, 0, bytes.Length);
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
			case "lottery":
				responseString = HandleLottery();
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

	public string HandleLottery()
	{
		return lottery.Scratch(GetActiveSelectables());
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

	// Single choke point for timeScale changes so audio stays paused in lockstep
	// with the game. AudioListener.pause is main-thread-only; MainThreadQueue runs
	// inline when already on the main thread, so coroutine callers stay synchronous.
	private void SetTimeScale(float value)
	{
		MainThreadQueue.Enqueue(() =>
		{
			Time.timeScale = value;
			AudioListener.pause = value == 0f;
			GptntAudioBuffer.CaptureEnabled = value != 0f;
		});
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
		SetTimeScale(float.Parse(request.QueryString.Get("timeScale")));
		timeStepSize = int.Parse(request.QueryString.Get("timeStepSize"));
		string sessionId = request.QueryString.Get("sessionId");

		// If the ruleSeed parameter is not provided, default to 1, otherwise parse it.
		string ruleSeedRaw = request.QueryString.Get("ruleSeed");
		int ruleSeed = string.IsNullOrEmpty(ruleSeedRaw) ? 1 : int.Parse(ruleSeedRaw);
		
		if (sessionId != null)
		{
			GptntDebug.AddSessionId(sessionId);
			log.Debug("Starting game with sessionId=" + sessionId);
		}
		return StartMission(seed, ruleSeed, timeLimit, numStrikes, needyTime, isFront, optWidgets, components);
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

	private byte[] GetRawSegmentation(HttpListenerResponse response)
	{
		Selectable[] selectables = GetActiveSelectables().ToArray();
		GameObject[] objects = new GameObject[selectables.Length];
		for (int i = 0; i < selectables.Length; i++)
		{
			objects[i] = selectables[i].gameObject;
		}

		byte[] imageBytes = null;
		var waitHandle = new ManualResetEvent(false);

		MainThreadQueue.Enqueue(() =>
		{
			StartCoroutine(segmentation.RawCapture(objects, (bytes) =>
			{
				imageBytes = bytes;
				waitHandle.Set();
			}));
		});

		if (!waitHandle.WaitOne(500))
		{
			response.StatusCode = (int) HttpStatusCode.RequestTimeout;
			return null;
		}
		return imageBytes;
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
				SetTimeScale(float.Parse(value));
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
		SetTimeScale(0);
	}

	public string HandleSetStepUnit(HttpListenerRequest request, HttpListenerResponse response)
	{
		string value = request.QueryString.Get("value");
		timeStepSize = int.Parse(value);
		return "Set timeStepSize to " + value;
	}

	public string HandleTimeStep(HttpListenerRequest request, HttpListenerResponse response)
	{
		var waitHandle = new ManualResetEvent(false);

		MainThreadQueue.Enqueue(() =>
		{
			StartCoroutine(WaitForStep(() => waitHandle.Set()));
		});

		if (!waitHandle.WaitOne(10000)) // 10 second timeout
		{
			response.StatusCode = (int) HttpStatusCode.RequestTimeout;
			log.Error("Time out while waiting " + timeStepSize + " seconds");
			return "Time out while waiting " + timeStepSize + " seconds";
		}

		waitHandle.Reset();

		MainThreadQueue.Enqueue(() =>
		{
			StartCoroutine(WaitForNotTransitioning(() => waitHandle.Set()));
		});

		if (!waitHandle.WaitOne(10000)) // 10 second timeout
		{
			response.StatusCode = (int) HttpStatusCode.RequestTimeout;
			log.Error("Time out while waiting for game to exit transitioning");
			return "Time out while waiting for game to exit transitioning";
		}

		waitHandle.Reset();

		MainThreadQueue.Enqueue(() =>
		{
			StartCoroutine(WaitForEmerging(() => waitHandle.Set()));
		});

		if (!waitHandle.WaitOne(10000)) // 10 second timeout
		{
			response.StatusCode = (int) HttpStatusCode.RequestTimeout;
			log.Error("Time out while waiting for module elements to emerge");
			return "Time out while waiting for module elements to emerge";
		}
		return "Paused after " + timeStepSize + " in-game milliseconds (and modules emerged)";
	}

	private IEnumerator WaitForStep(Action onComplete)
	{
		SetTimeScale(1); // Unpause
		yield return new WaitForSeconds(timeStepSize / 1000f);
		onComplete?.Invoke();
	}

	private IEnumerator WaitForNotTransitioning(Action onComplete)
	{
		yield return new WaitUntil(() => !StateEqualsAny(GptntStates.GameState.Transitioning));
		onComplete?.Invoke();
	}

	private IEnumerator WaitForEmerging(Action onComplete)
	{
		BombState currentBombState = gptntStates.UpdateBombState();
		while (currentBombState.isEmerging)
		{
			yield return new WaitForSeconds(0.1f);
			currentBombState = gptntStates.UpdateBombState();
			log.Debug("Waiting for emerging modules");
		}
		SetTimeScale(0);
		onComplete?.Invoke();
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

	private string StartMission(string seed, int ruleSeed, int timeLimit, int numStrikes, int needyTime, bool isFront, int optWidgets, List<string> components)
	{
		if (string.IsNullOrEmpty(seed))
		{
			return "Please enter valid seed. e.g. seed=123";
		}

		if (ruleSeed != 1 && !VanillaRuleModifier.Installed()) 
		{
			return "Rule Seed Modifier mod not installed and cannot instantiate when the rule seed is not 1. Please install the Rule Seed Modifier mod or set ruleSeed=1.";
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
			try
			{
				KMComponentPool.ComponentTypeEnum CompType = (KMComponentPool.ComponentTypeEnum) Enum.Parse(typeof(KMComponentPool.ComponentTypeEnum), compString);
				pool.ComponentTypes = new List<KMComponentPool.ComponentTypeEnum> { CompType };
			}
			catch (ArgumentException)
			{
				// Not a vanilla component type — treat as a modded module ID
				pool.ModTypes = new List<string> { compString };
			}
			pools.Add(pool);
		}
		setting.ComponentPools = pools;
		mission.GeneratorSetting = setting;

		// Using the VanillaRuleModifier requires a separate mod to be installed. If it is installed,
		// then the VanillaRuleModifierProperties GameObject will exist at runtime and then this should
		// be true. Otherwise, it'll just be false and we then ignore the ruleSeed Parameter
		if (VanillaRuleModifier.Installed())
		{
			VanillaRuleModifier.SetRuleSeed(ruleSeed, true);
		}

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

