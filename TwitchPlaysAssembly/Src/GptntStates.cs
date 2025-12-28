using System;
using UnityEngine;
using System.Collections.Generic;
using Assets.Scripts.Missions;
using System.Linq;
using TwitchPlaysAssembly;
using System.Collections;
using KModkit;
using log4net;

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
			}
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
		OnFirstLightsOn?.Invoke();
		gameState = GameState.LightsOn;
		bombState = GetInitialBombState();
	}

	private BombState GetInitialBombState()
	{
		return new BombState
		{
			isLightOn = gameState == GameState.LightsOn,
			isSolved = false,
			isDetonated = false,
			widgets = WidgetStates(bomb.WidgetManager.GetWidgets()),
			modules = ModuleStates(bomb.BombComponents),
			strikes = new List<string>(),
			seed = bomb.Seed,
			maxStrikes = bomb.NumStrikesToLose,
			bombSide = gptntActions.GetBombSide(),
			timerModule = new TimerModuleState(bomb.GetTimer())
		};
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
			GetModuleStateFromSelectable(selectable).inFocus = true;
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
				moduleState.OnStrike += () =>
				{
					bombState.strikes.Add(moduleState.name);
					if (bombState.strikes.Count == 2)
					{
						RemoveBlinking();
					}
				};
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
			default: return null;
		}
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