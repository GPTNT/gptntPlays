using System;
using UnityEngine;
using System.Collections.Generic;
using Assets.Scripts.Missions;
using System.Linq;
using TwitchPlaysAssembly;
using System.Collections;
using KModkit;
using log4net;
using Events;
using System.Reflection;
using Newtonsoft.Json;

public class GptntStates : MonoBehaviour 
{
	public long lastPosition = 0;
	public BombState bombState;
	public GptntGameHost host;
	public GptntActions gptntActions;
	private GptntHttpHandler httpHandler;
	public Bomb bomb;
	KMBombInfo bombInfo;
	KMGameInfo gameInfo;
	public volatile GameState gameState;
	public bool isStarted;

	public event Action OnFirstLightsOn;
	public event Action OnReset;
	public event Action OnGameEnd;

	private NeedyControls pendingControls;
	private string pendingSession;
	private volatile string controlsAcknowledgement = "{\"protocolVersion\":1,\"sessionId\":null,\"applied\":null}";

	public string ControlsAcknowledgement { get { return controlsAcknowledgement; } }

	public void SetNeedyControls(NeedyControls controls, string session)
	{
		pendingControls = controls;
		pendingSession = session;
		controlsAcknowledgement = "{\"protocolVersion\":1,\"sessionId\":null,\"applied\":null}";
	}

	private static ILog log = LogManager.GetLogger("GptntStates");

	public enum GameState
	{
		Gameplay,
		Setup,
		LightsOn,
		LightsOff,
		Transitioning,
		PostGame,
	}

	public void Start()
	{
		bombInfo = GetComponent<KMBombInfo>();
		host = GetComponent<GptntGameHost>();
		gptntActions = GetComponent<GptntActions>();
		gameInfo = FindObjectOfType<KMGameInfo>();
		httpHandler = GetComponent<GptntHttpHandler>();

		gptntActions.OnZoomOut += OnZoomOut;
		BombComponentEvents.OnComponentStrike += RecordComponentStrike;

		bombInfo.OnBombExploded += () =>
		{
			isStarted = false;
			bombState.isDetonated = true;
			bombState.timerModule.secondsRemaining = bomb.GetTimer().TimeRemaining;
			if (bombState.timerModule.secondsRemaining < 0)
			{
				bombState.timerModule.secondsRemaining = 0;
			}
			OnGameEnd?.Invoke();
		};

		bombInfo.OnBombSolved += () =>
		{
			isStarted = false;
			bombState.isSolved = true;
			bombState.timerModule.secondsRemaining = bomb.GetTimer().TimeRemaining;
			OnGameEnd?.Invoke();
		};

		gameInfo.OnStateChange += (KMGameInfo.State state) =>
		{
			log.Debug(GptntDebug.FormatMessage($"State changed: {gameState} -> {state}"));
			switch (state)
			{
				case KMGameInfo.State.Gameplay:
					gameState = GameState.Gameplay;
					break;
				case KMGameInfo.State.Setup:
					gameState = GameState.Setup;
					isStarted = false;
					bombState = null;
					SetNeedyControls(null, null);
					OnReset?.Invoke();
					break;
				case KMGameInfo.State.PostGame:
					gameState = GameState.PostGame;
					break;
				case KMGameInfo.State.Transitioning:
					gameState = GameState.Transitioning;
					break;
				default:
					break;
			}
		};
		gameInfo.OnLightsChange += (bool on) =>
		{
			log.Debug(GptntDebug.FormatMessage("Lights on changes to " + on.ToString()));
			TwitchBomb twitchBomb = FindObjectOfType<TwitchBomb>();
			try
			{
				bomb = twitchBomb.Bomb;
			}
			catch (Exception ex)
			{
				log.Warn(GptntDebug.FormatMessage("Twitch bomb is null"), ex);
				return;
			}
			if (!bomb) return;
			if (!isStarted && pendingControls != null && pendingControls.HasOverrides)
				ApplyNeedyControls();
			gptntActions.bomb = twitchBomb;
			gptntActions.InitRotation();
			GameState newGameState = gameState.EqualsAny(GameState.Gameplay, GameState.LightsOn, GameState.LightsOff) ? (on ? GameState.LightsOn : GameState.LightsOff) : gameState;

			if (newGameState == GameState.LightsOn && !isStarted)
			{
				// when the first light turns on
				isStarted = true;
				StartCoroutine(FirstLightOn());
				return;
			}
			gameState = newGameState;
		};
	}

	private IEnumerator FirstLightOn()
	{
		yield return new WaitForSeconds(1.5f); // wait for a bit for the bomb to fully initiate.
		VanillaRuleModifier.ApplyDeferredRules();
		OnFirstLightsOn?.Invoke();
		gameState = GameState.LightsOn;
		bombState = GetInitialBombState();
	}

	private BombState GetInitialBombState()
	{
		BombState currentBombState = new BombState
		{
			isLightOn = gameState == GameState.LightsOn,
			isSolved = false,
			isDetonated = false,
			widgets = WidgetStates(bomb.WidgetManager.GetWidgets()),
			modules = ModuleStates(bomb.BombComponents),
			needyStatuses = bomb.BombComponents.OfType<NeedyComponent>().Select(c => new NeedyStatus(c)).ToList(),
			strikes = new List<string>(),
			seed = bomb.Seed,
			maxStrikes = bomb.NumStrikesToLose,
			bombSide = gptntActions.GetBombSide(),
			timerModule = new TimerModuleState(bomb.GetTimer())
		};

		currentBombState.isEmerging = currentBombState.modules
			.OfType<IEmergingModule>()
			.Any(m => !m.isEmerged);
		return currentBombState;

	}

	public BombState UpdateBombState()
	{
		var traceContext = httpHandler.GetCurrentTraceContext();

		OpenTelemetrySpan span = new OpenTelemetrySpan("state.update", traceContext.TraceId, traceContext.SpanId);

		log.Debug(GptntDebug.FormatMessage("Updating bomb state", span.GetTraceId(), span.GetSpanId()));

		if (bombState == null)
			return GetInitialBombState();

		if (!bomb)
			return bombState;

		bombState.isLightOn = gameState == GameState.LightsOn;
		bombState.bombSide = gptntActions.GetBombSide();
		bombState.timerModule.secondsRemaining = bomb.GetTimer().TimeRemaining;

		foreach (BaseModuleState module in bombState.modules)
		{
			try
			{
				log.Debug(GptntDebug.FormatMessage($"Updating attributes for module {module.name}", span.GetTraceId(), span.GetSpanId()));
				module.UpdateAttributes();
			}
			catch (Exception ex)
			{
				log.Error(GptntDebug.FormatMessage($"Module update failed for: {module.name} because of ", span.GetTraceId(), span.GetSpanId()), ex);
			}
		}
		foreach (NeedyStatus status in bombState.needyStatuses)
			status.UpdateAttributes();
		bombState.isEmerging = bombState.modules
			.OfType<IEmergingModule>()
			.Any(m => !m.isEmerged);

		span.End(true);
		return bombState;
	}

	#region Helper functions

	private IEnumerator UpdateStateWhenAvailable(BaseModuleState module)
	{
		if (module is MemoryModuleState)
		{
			MemoryComponent memoryComponent = (MemoryComponent) module.component;
			yield return new WaitUntil(() => memoryComponent.IsInputValid);
			module.UpdateAttributes();
		}
		else if (module is WhosOnFirstModuleState)
		{
			WhosOnFirstComponent whosOnFirstComponent = (WhosOnFirstComponent) module.component;
			yield return new WaitUntil(() => whosOnFirstComponent.ButtonsEmerged);
			module.UpdateAttributes();
		}
		else
		{
			throw new Exception("Module invalid for dealyed update");
		}
		log.Debug(GptntDebug.FormatMessage($"Updated {module.name} state."));
	}

	private void OnZoomOut()
	{
		foreach (SolvableModuleState moduleState in bombState.modules)
		{
			moduleState.inFocus = false;
		}
	}

	public void UpdateZoomIn(Selectable selectable)
	{
		if (selectable != null)
		{
			SolvableModuleState moduleState = GetModuleStateFromSelectable(selectable);
			if (moduleState != null)
				moduleState.inFocus = true;
		}
	}

	private SolvableModuleState GetModuleStateFromSelectable(Selectable selectable)
	{
		BombComponent selectableComponent = selectable.GetComponent<BombComponent>();
		if (!selectableComponent)
		{
			log.Debug(GptntDebug.FormatMessage("No Component Found"));
			return null;
		}
		return bombState.modules.FirstOrDefault(module => module.component == selectableComponent);
	}

	private List<BaseWidgetState> WidgetStates(List<Widget> widgets)
	{
		List<BaseWidgetState> widgetStates = new List<BaseWidgetState>();
		foreach (Widget widget in widgets)
		{
			try
			{
				switch (widget)
				{
					case SerialNumber serialNumber:
						widgetStates.Add(SerialNumberWidgetState.FromWidget(serialNumber));
						break;

					case BatteryWidget batteryWidget:
						widgetStates.Add(BatteryWidgetState.FromWidget(batteryWidget));
						break;

					case PortWidget portWidget:
						widgetStates.Add(PortWidgetState.FromWidget(portWidget));
						break;

					case IndicatorWidget indicatorWidget:
						widgetStates.Add(IndicatorWidgetState.FromWidget(indicatorWidget));
						break;
				}
			}
			catch (Exception ex)
			{
				log.Error(GptntDebug.FormatMessage($"Error processing {widget.GetType().Name}"), ex);
			}
		}

		return widgetStates;
	}

	private List<SolvableModuleState> ModuleStates(List<BombComponent> components)
	{
		List<SolvableModuleState> moduleStates = new List<SolvableModuleState>();
		foreach (var comp in components)
		{
			if (comp.ComponentType == ComponentTypeEnum.Timer)
				continue;

			SolvableModuleState moduleState = CreateModuleState(comp);
			if (moduleState != null)
			{
				moduleStates.Add(moduleState);
			}
			else
			{
				log.Debug(GptntDebug.FormatMessage("Unknown bomb component: " + comp.name));
			}
		}
		return moduleStates;
	}

	private SolvableModuleState CreateModuleState(BombComponent comp)
	{
		switch (comp.ComponentType)
		{
			case ComponentTypeEnum.Simon: return new SimonSaysModuleState(comp);
			case ComponentTypeEnum.Wires: return new WireSetModuleState(comp);
			case ComponentTypeEnum.BigButton: return new ButtonModuleState(comp);
			case ComponentTypeEnum.Keypad: return new KeypadModuleState(comp);
			case ComponentTypeEnum.WhosOnFirst: return new WhosOnFirstModuleState(comp);
			case ComponentTypeEnum.Memory: return new MemoryModuleState(comp);
			case ComponentTypeEnum.Morse: return new MorseCodeModuleState(comp);
			case ComponentTypeEnum.Venn: return new ComplicatedWiresModuleState(comp);
			case ComponentTypeEnum.WireSequence: return new WireSequenceModuleState(comp);
			case ComponentTypeEnum.Maze: return new MazeModuleState(comp);
			case ComponentTypeEnum.Password: return new PasswordModuleState(comp);
			case ComponentTypeEnum.Mod: return new ModdedModuleState(comp);
			default: return null;
		}
	}

	private void OnDestroy()
	{
		BombComponentEvents.OnComponentStrike -= RecordComponentStrike;
	}

	private void RecordComponentStrike(BombComponent component, bool finalStrike)
	{
		if (bombState == null || component == null || component.Bomb != bomb) return;
		var state = bombState.modules.FirstOrDefault(module => module.component == component);
		bombState.strikes.Add(state != null ? state.name : component.ComponentType.ToString());
		if (bombState.strikes.Count == 2) RemoveBlinking();
	}

	private void ApplyNeedyControls()
	{
		var controls = pendingControls;
		if (controls == null || !controls.HasOverrides) return;
		var original = new Dictionary<NeedyComponent, float[]>();
		FieldInfo probabilityField = null;
		try
		{
			var needy = bomb.BombComponents.OfType<NeedyComponent>().ToList();
			if (needy.Count == 0) throw new InvalidOperationException("No needy components were instantiated");
			probabilityField = typeof(NeedyComponent).GetField("activationChanceAfterFirstChange", BindingFlags.NonPublic | BindingFlags.Instance);
			if (controls.FirstEventProbability.HasValue && (probabilityField == null || probabilityField.FieldType != typeof(float)))
				throw new InvalidOperationException("Needy activation probability field unavailable");
			foreach (var component in needy)
			{
				if (component.State != NeedyComponent.NeedyStateEnum.InitialSetup && component.State != NeedyComponent.NeedyStateEnum.AwaitingActivation)
					throw new InvalidOperationException("Needy activation began before controls were applied");
			}
			foreach (var component in needy)
				original.Add(component, new[] { controls.FirstEventProbability.HasValue ? (float) probabilityField.GetValue(component) : 0f, component.ResetDelayMin, component.ResetDelayMax });
			var applied = new List<object>();
			foreach (var component in needy)
			{
				// Random.value is nonnegative, but the game compares with <=. A negative
				// sentinel therefore implements a true zero-probability override.
				float effectiveProbability = controls.FirstEventProbability == 0f ? -1f : controls.FirstEventProbability.GetValueOrDefault();
				if (controls.FirstEventProbability.HasValue) probabilityField.SetValue(component, effectiveProbability);
				if (controls.CooldownMin.HasValue) { component.ResetDelayMin = controls.CooldownMin.Value; component.ResetDelayMax = controls.CooldownMax.Value; }
				float? probability = null;
				if (controls.FirstEventProbability.HasValue)
				{
					float actual = (float) probabilityField.GetValue(component);
					if (actual != effectiveProbability) throw new InvalidOperationException("Needy probability failed readback");
					probability = actual == -1f ? 0f : actual;
				}
				float? min = controls.CooldownMin.HasValue ? (float?) component.ResetDelayMin : null;
				float? max = controls.CooldownMax.HasValue ? (float?) component.ResetDelayMax : null;
				if (probability != controls.FirstEventProbability || min != controls.CooldownMin || max != controls.CooldownMax)
					throw new InvalidOperationException("Needy controls failed readback");
				var location = new NeedyStatus(component);
				applied.Add(new { name = location.name, index = location.index, onFront = location.onFront, firstEventProbability = probability, cooldownMin = min, cooldownMax = max });
			}
			controlsAcknowledgement = JsonConvert.SerializeObject(new { protocolVersion = 1, sessionId = pendingSession, applied });
		}
		catch (Exception ex)
		{
			foreach (var previous in original)
			{
				try
				{
					if (controls.FirstEventProbability.HasValue && probabilityField != null) probabilityField.SetValue(previous.Key, previous.Value[0]);
					previous.Key.ResetDelayMin = previous.Value[1];
					previous.Key.ResetDelayMax = previous.Value[2];
				}
				catch (Exception rollbackError) { log.Error(GptntDebug.FormatMessage("Could not restore needy controls"), rollbackError); }
			}
			log.Error(GptntDebug.FormatMessage("Could not apply needy controls"), ex);
			controlsAcknowledgement = JsonConvert.SerializeObject(new { protocolVersion = 1, sessionId = pendingSession, applied = (object) null, error = ex.Message });
		}
		pendingControls = null;
	}

	private void RemoveBlinking()
	{
		StrikeIndicator indicator = FindObjectOfType<StrikeIndicator>();
		indicator.StopAllCoroutines();
		var method = typeof(StrikeIndicator).GetMethod("SetAllIndicatorsOn",
		System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		method.Invoke(indicator, new object[] { indicator.RedColour });
	}

	#endregion  
}

public class AnchorInfo
{
	public int index;
	public bool onFront;
}
