using System;
using UnityEngine;
using System.Collections.Generic;
using Assets.Scripts.Missions;
using System.Linq;
using TwitchPlaysAssembly;
using System.Collections;

public class GptntStates : MonoBehaviour 
{
	public long lastPosition = 0;
	public BombState bombState;
	public GptntGameHost host;
	public GptntActions gptntActions;
	Bomb bomb;
	KMBombInfo bombInfo;
	KMGameInfo gameInfo;
	public volatile GameState gameState;
	public bool isStarted;

	public event Action OnFirstLightsOn;
	public event Action OnGameStart;
	public event Action OnGameEnd;

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

		bomb = host.bomb.Bomb;

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
			switch (state)
			{
				case KMGameInfo.State.Gameplay:
					gameState = GameState.Gameplay;
					OnGameStart?.Invoke();
					break;
				case KMGameInfo.State.Setup:
					gameState = GameState.Setup;
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
			TwitchBomb bomb = FindObjectOfType<TwitchBomb>();
			gptntActions.bomb = bomb;
			gptntActions.InitRotation();
			gameState = gameState.EqualsAny(GameState.Gameplay, GameState.LightsOn, GameState.LightsOff) ? (on ? GameState.LightsOn : GameState.LightsOff) : gameState;

			if (gameState == GameState.LightsOn && !isStarted)
			{
				// when the first light turns on
				isStarted = true;
				bombState = GetInitialBombState();
				OnFirstLightsOn?.Invoke();
			}
		};
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
				module.UpdateAttributes();
			}
			catch (MemoryModuleException)
			{
				StartCoroutine(GetMemoryState((MemoryModuleState) module));
			}
		}

		return bombState;
	}

	#region Helper functions

	private IEnumerator GetMemoryState(MemoryModuleState memory)
	{
		MemoryComponent memoryComponent = (MemoryComponent) memory.component;
		yield return new WaitUntil(() => memoryComponent.IsInputValid);
		GptntDebug.Log("[Memory] Updated memory state.");
		memory.UpdateAttributes(); 
	}

	private void OnZoomOut()
	{
		foreach (BaseModuleState moduleState in bombState.modules)
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

	private BaseModuleState GetModuleStateFromSelectable(Selectable selectable)
	{
		BombComponent selectableComponent = selectable.GetComponent<BombComponent>();
		if (!selectableComponent)
		{
			GptntDebug.Log("No Component Found");
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
				GptntDebug.Log($"Error processing {widget.GetType().Name} widget: {ex}");
			}
		}

		return widgetStates;
	}

	private List<BaseModuleState> ModuleStates(List<BombComponent> components)
	{
		List<BaseModuleState> moduleStates = new List<BaseModuleState>();
		foreach (var comp in components)
		{
			if (comp.ComponentType == ComponentTypeEnum.Timer)
				continue;

			var moduleState = CreateModuleState(comp);

			if (moduleState != null)
			{
				moduleState.OnStrike += () => bombState.strikes.Add(moduleState.name);
				moduleStates.Add(moduleState);
			}
			else
			{
				GptntDebug.Log("Unknown bomb component: " + comp.name);
			}
		}
		return moduleStates;
	}

	private BaseModuleState CreateModuleState(BombComponent comp)
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

	#endregion
}

public class AnchorInfo
{
	public int index;
	public bool onFront;
}