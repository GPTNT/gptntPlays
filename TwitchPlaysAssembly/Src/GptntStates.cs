using System;
using UnityEngine;
using System.Collections.Generic;
using Assets.Scripts.Missions;
using System.Reflection;
using BombGame;
using Assets.Scripts.Components.VennWire;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine.UI;

public class GptntStates : MonoBehaviour 
{
	public long lastPosition = 0;
	public BombState bombState;
	public GptntGameHost host;
	public GptntActions gptntActions;
	TwitchBomb twitchBomb;
	KMBombInfo bombInfo;
	KMGameInfo gameInfo;
	public MemoryComponent badModule; // TODO: Change this
	public volatile GameState gameState;
	private bool isStarted;

	public event Action OnGameStart;
	public event Action OnFirstLightsOn;
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

		twitchBomb = host.bomb;

		gptntActions.OnZoomOut += OnZoomOut;

		bombInfo.OnBombExploded += () =>
		{
			bombState.isDetonated = true;
			bombState.timerModule.secondsRemaining = twitchBomb.Bomb.GetTimer().TimeRemaining;
			if (bombState.timerModule.secondsRemaining < 0)
			{
				bombState.timerModule.secondsRemaining = 0;
			}
			OnGameEnd?.Invoke();
		};

		bombInfo.OnBombSolved += () =>
		{
			bombState.isSolved = true;
			bombState.timerModule.secondsRemaining = twitchBomb.Bomb.GetTimer().TimeRemaining;
			OnGameEnd?.Invoke();
		};

		gameInfo.OnStateChange += (KMGameInfo.State state) =>
		{
			switch (state)
			{
				case KMGameInfo.State.Gameplay:
					gameState = GameState.Gameplay;
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
				OnFirstLightsOn?.Invoke();
				//gptntBuffer.StartBuffer(FrameRateMS); // TODO: Move back to GameHost
				//lastKnownBombState = gptntStates.GetInitialBombState();
				//StartCoroutine(HoldBomb());
			}
		};
	}

	public BombState GetInitialBombState()
	{
		Bomb bomb = twitchBomb.Bomb;
		bombState = new BombState
		{
			isLightOn = gameState == GameState.LightsOn,
			isSolved = false,
			isDetonated = false,
			widgets = new List<BaseWidgetState>(),
			modules = new List<BaseModuleState>(),
			strikes = new List<string>(),
			seed = bomb.Seed,
			maxStrikes = bomb.NumStrikesToLose,
		};

		bombState.widgets = WidgetStates(bomb.WidgetManager.GetWidgets());

		foreach (var comp in bomb.BombComponents)
		{
			if (comp.ComponentType == ComponentTypeEnum.Timer)
			{
				var timerState = new TimerModuleState(comp)
				{
					secondsRemaining = bomb.GetTimer().TimeRemaining
				};
				bombState.timerModule = timerState;
				continue;
			}

			var moduleState = CreateModuleState(comp);

			if (moduleState != null)
			{
				moduleState.OnStrike += () => bombState.strikes.Add(moduleState.name);
				bombState.modules.Add(moduleState);
			}
			else
			{
				GptntDebug.Log("Unknown bomb component: " + comp.name);
			}
		}
		return bombState;
	}

	public BombState UpdateBombState()
	{
		if (bombState == null)
			return GetInitialBombState();

		Bomb bomb = twitchBomb.Bomb;
		if (!bomb)
			return bombState;
		string gameStateString = gameState.ToString();
		bombState.isLightOn = gameState == GameState.LightsOn;

		bombState.bombSide = BombSide(gptntActions.bombRotationX, gptntActions.bombRotationZ);

		foreach (BombComponent comp in bomb.BombComponents)
		{
			if (comp.ComponentType == ComponentTypeEnum.Timer)
			{
				TimerModuleState timerState = bombState.timerModule;
				timerState.secondsRemaining = bomb.GetTimer().TimeRemaining;
				timerState.name = "Timer";
				bombState.timerModule = timerState;
			}

			if (comp.ComponentType == ComponentTypeEnum.Simon)
			{
				SimonComponent simon = (SimonComponent) comp;
				FieldInfo fieldInfo1 = typeof(SimonComponent).GetField("currentSequence", BindingFlags.NonPublic | BindingFlags.Instance);
				int[] sequence = (int[]) fieldInfo1.GetValue(simon);
				FieldInfo fieldInfo2 = typeof(SimonComponent).GetField("lastIndex", BindingFlags.NonPublic | BindingFlags.Instance);
				int solveProgress = (int) fieldInfo2.GetValue(simon);

				SimonSaysModuleState simonState = (SimonSaysModuleState) bombState.modules.FirstOrDefault(module => module.component == comp);
				List<string> beepSequence = new List<string>();
				foreach (int beep in sequence)
				{
					if (beep == 0)
					{
						beepSequence.Add("red");
					}
					else if (beep == 1)
					{
						beepSequence.Add("blue");
					}
					else if (beep == 2)
					{
						beepSequence.Add("green");
					}
					else if (beep == 3)
					{
						beepSequence.Add("yellow");
					}
				}


				simonState.beepSequence = beepSequence;
				simonState.solveProgress = solveProgress;
			}

			else if (comp.ComponentType == ComponentTypeEnum.Wires)
			{
				WireSetComponent wireset = (WireSetComponent) comp;
				List<int> indices = new List<int>();
				Selectable component = wireset.GetComponent<Selectable>();
				for (int i = 0; i < component.Children.Length; i++)
				{
					if (component.Children[i] != null)
					{
						indices.Add(int.Parse(component.Children[i].name[component.Children[i].name.Length - 1].ToString()) - 1);
					}
				}
				WireColor[] colors = new WireColor[6];
				bool[] is_snipped = new bool[6];
				WireSetModuleState wireSetState = (WireSetModuleState) bombState.modules.FirstOrDefault(module => module.component == comp);
				wireSetState.wires = new WireSetWireState[6];
				// Just assign the spaces that contain wires
				int indicesIndex = 0;
				foreach (SnippableWire wire in wireset.wires)
				{
					wireSetState.wires[indices[indicesIndex]] = new WireSetWireState();
					wireSetState.wires[indices[indicesIndex]].color = wire.GetColor().ToString().ToLower();
					wireSetState.wires[indices[indicesIndex]].isCut = wire.Snipped;
					wireSetState.wires[indices[indicesIndex]].position = indices[indicesIndex];
					indicesIndex++;
				}

			}

			else if (comp.ComponentType == ComponentTypeEnum.BigButton)
			{
				ButtonComponent button = (ButtonComponent) comp;
				string buttonColor = button.ButtonColor.ToString().ToLower();
				string buttonMessage = button.ButtonInstruction.ToString();
				string stripColor = button.IndicatorColor.ToString().ToLower();

				ButtonModuleState buttonState = (ButtonModuleState) bombState.modules.FirstOrDefault(module => module.component == comp);
				buttonState.buttonColor = buttonColor;
				buttonState.buttonWord = buttonMessage;
				buttonState.isHeld = button.IsHolding;
				if (buttonState.isHeld)
				{
					buttonState.stripColor = stripColor;
				}
				else
				{
					buttonState.stripColor = null;
				}
			
			}

			else if (comp.ComponentType == ComponentTypeEnum.Keypad)
			{
				KeypadComponent keypad = (KeypadComponent) comp;

				KeypadModuleState keypadState = (KeypadModuleState) bombState.modules.FirstOrDefault(module => module.component == comp);
				KeyPadButtonState[] KeypadButtons = new KeyPadButtonState[4];

				for (int i = 0; i < 4; i++)
				{
					KeypadButton button = keypad.buttons[i];
					string symbol = button.GetValue();
					KeypadButtons[i] = new KeyPadButtonState();

					if (symbol == "©") KeypadButtons[i].symbol = "copyright";
					else if (symbol == "★") KeypadButtons[i].symbol = "star";
					else if (symbol == "☆") KeypadButtons[i].symbol = "hollow-star";
					else if (symbol == "ټ") KeypadButtons[i].symbol = "pashto-teh";
					else if (symbol == "Җ") KeypadButtons[i].symbol = "zh";
					else if (symbol == "Ω") KeypadButtons[i].symbol = "omega";
					else if (symbol == "Ѭ") KeypadButtons[i].symbol = "ligature-iotated-e";
					else if (symbol == "Ѽ") KeypadButtons[i].symbol = "ot";
					else if (symbol == "ϗ") KeypadButtons[i].symbol = "kai";
					else if (symbol == "ϫ") KeypadButtons[i].symbol = "egyptian-kai";
					else if (symbol == "Ϭ") KeypadButtons[i].symbol = "lunate-sampi";
					else if (symbol == "Ϟ") KeypadButtons[i].symbol = "qoppa";
					else if (symbol == "Ѧ") KeypadButtons[i].symbol = "little-yus";
					else if (symbol == "ӕ") KeypadButtons[i].symbol = "ae";
					else if (symbol == "Ԇ") KeypadButtons[i].symbol = "ha-with-descender";
					else if (symbol == "Ӭ") KeypadButtons[i].symbol = "e-with-diaeresis";
					else if (symbol == "\u0488") KeypadButtons[i].symbol = "thousand-sign";
					else if (symbol == "Ҋ") KeypadButtons[i].symbol = "short-i";
					else if (symbol == "ѯ") KeypadButtons[i].symbol = "ksi";
					else if (symbol == "¿") KeypadButtons[i].symbol = "inverted-question";
					else if (symbol == "¶") KeypadButtons[i].symbol = "pilcrow";
					else if (symbol == "Ͼ") KeypadButtons[i].symbol = "lunate-epsilon";
					else if (symbol == "Ͽ") KeypadButtons[i].symbol = "reversed-lunate-epsilon";
					else if (symbol == "Ψ") KeypadButtons[i].symbol = "psi";
					else if (symbol == "Ѫ") KeypadButtons[i].symbol = "big-yus";
					else if (symbol == "Ҩ") KeypadButtons[i].symbol = "qa";
					else if (symbol == "҂") KeypadButtons[i].symbol = "titlo";
					else if (symbol == "Ϙ") KeypadButtons[i].symbol = "archaic-koppa";
					else if (symbol == "ζ") KeypadButtons[i].symbol = "zeta";
					else if (symbol == "ƛ") KeypadButtons[i].symbol = "lambda-bar";
					else if (symbol == "ѣ") KeypadButtons[i].symbol = "yat";
					if (button.LED_Correct.active)
					{
						KeypadButtons[i].color = "green";


					}
					else if (button.LED_Wrong.active)
					{
						KeypadButtons[i].color = "red";

					}
					else
					{
						KeypadButtons[i].color = null;
					}
				}
				
				keypadState.topLeft = KeypadButtons[0];
				keypadState.topRight = KeypadButtons[1];
				keypadState.bottomLeft = KeypadButtons[2];
				keypadState.bottomRight = KeypadButtons[3];
			}

			else if (comp.ComponentType == ComponentTypeEnum.WhosOnFirst)
			{
				WhosOnFirstComponent whoFirst = (WhosOnFirstComponent) comp;
				if (whoFirst.ButtonsEmerged)
				{
					int stage = whoFirst.CurrentStage + 1;
					string[] buttonValues = new string[6];
					foreach (KeypadButton button in whoFirst.Buttons)
					{
						buttonValues[button.ButtonIndex] = button.Text.text;

					}
					string displayWord = whoFirst.DisplayText.text;

					WhosOnFirstModuleState whoFirstState = (WhosOnFirstModuleState) bombState.modules.FirstOrDefault(module => module.component == comp);
					whoFirstState.stage = stage;
					whoFirstState.buttonWords = buttonValues;
					whoFirstState.displayWord = displayWord;
					
				}

			}

			else if (comp.ComponentType == ComponentTypeEnum.Memory)
			{
				MemoryComponent memory = (MemoryComponent) comp;
				badModule = memory;
				bool isInputValid = memory.IsInputValid;
				if (!isInputValid && !comp.IsSolved) 
				{
				  throw new MemoryModuleException("Memory buttons not yet emerged");
				}
        
				MemoryModuleState memoryState = (MemoryModuleState) bombState.modules.FirstOrDefault(module => module.component == comp);
				memoryState.stage = memory.CurrentStage + 1;
				memoryState.displayNumber = memory.DisplayText.text;
				string[] buttonValues = new string[4];
				foreach (KeypadButton button in memory.Buttons)
				{
					buttonValues[button.ButtonIndex] = button.Text.text;
				}
				memoryState.buttonNumbers = buttonValues;
				
			}

			else if (comp.ComponentType == ComponentTypeEnum.Morse)
			{
				MorseCodeComponent morse = (MorseCodeComponent) comp;
				int currentFrequency = morse.CurrentFrequency;
				FieldInfo fieldInfo = typeof(MorseCodeComponent).GetField("chosenWord", BindingFlags.NonPublic | BindingFlags.Instance);
				string word = (string) fieldInfo.GetValue(morse);

				MorseCodeModuleState morseState = (MorseCodeModuleState) bombState.modules.FirstOrDefault(module => module.component == comp);

				morseState.currentFrequency = currentFrequency;
				morseState.sequence = word;
				morseState.correctFrequency = morse.ChosenFrequency;
				
			}

			else if (comp.ComponentType == ComponentTypeEnum.Venn)
			{
				VennWireComponent venn = (VennWireComponent) comp;
				bool[] has_star = new bool[6];
				bool[] is_led_on = new bool[6];
				bool[] is_snipped = new bool[6];
				string[] color = new string[6];

				List<int> indices = new List<int>();
				Selectable component = venn.GetComponent<Selectable>();
				for (int i = 0; i < component.Children.Length; i++)
				{
					if (component.Children[i] != null)
					{
						indices.Add(int.Parse(component.Children[i].name[component.Children[i].name.Length - 1].ToString()) - 1);
					}
				}

				ComplicatedWiresModuleState compState = (ComplicatedWiresModuleState) bombState.modules.FirstOrDefault(module => module.component == comp);
				compState.wires = new ComplicatedWireState[6];

				int indicesIndex = 0;
				foreach (VennSnippableWire wire in venn.ActiveWires)
				{
					compState.wires[indices[indicesIndex]] = new ComplicatedWireState();
					compState.wires[indices[indicesIndex]].hasStar = wire.HasSymbol;
					compState.wires[indices[indicesIndex]].isLedOn = wire.IsLEDOn;
					compState.wires[indices[indicesIndex]].isCut = wire.Snipped;
					compState.wires[indices[indicesIndex]].color = wire.Color.ToString().ToLower();
					compState.wires[indices[indicesIndex]].position = indices[indicesIndex];

					indicesIndex++;
				}

			}

			else if (comp.ComponentType == ComponentTypeEnum.WireSequence)
			{
				WireSequenceComponent wireSeq = (WireSequenceComponent) comp;
				FieldInfo fieldInfo = typeof(WireSequenceComponent).GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance);

				WireSequenceModuleState wireSeqState = (WireSequenceModuleState) bombState.modules.FirstOrDefault(module => module.component == comp);
				wireSeqState.panel = (int) fieldInfo.GetValue(wireSeq) + 1;
				wireSeqState.wires = new WireSequenceWireState[12];

				FieldInfo fieldInfo2 = typeof(WireSequenceComponent).GetField("wireSequence", BindingFlags.NonPublic | BindingFlags.Instance);
				List<WireSequenceComponent.WireConfiguration> configs = (List<WireSequenceComponent.WireConfiguration>) fieldInfo2.GetValue(wireSeq);
				for (int i = 0; i < 12; i++)
				{
					WireSequenceComponent.WireConfiguration config = configs[i];
					if (!config.NoWire)
					{
						wireSeqState.wires[i] = new WireSequenceWireState();
						wireSeqState.wires[i].startPositionNumber = i;
						if (config.To == 0)
						{
							wireSeqState.wires[i].endPositionLetter = "A";

						}
						else if (config.To == 1)
						{
							wireSeqState.wires[i].endPositionLetter = "B";

						}
						else
						{
							wireSeqState.wires[i].endPositionLetter = "C";

						}
						wireSeqState.wires[i].color = config.Wire.GetColor().ToString().ToLower();
						wireSeqState.wires[i].isCut = config.Wire.Snipped;
					}
				}

			}

			else if (comp.ComponentType == ComponentTypeEnum.Maze)
			{
				InvisibleWallsComponent invis = (InvisibleWallsComponent) comp;
				MazeModuleState mazeState = (MazeModuleState) bombState.modules.FirstOrDefault(module => module.component == comp);
				mazeState.numColumns = 6;
				mazeState.numRows = 6;


				int StartX = invis.CurrentCell.X;
				int startY = invis.CurrentCell.Y;
				int goalX = invis.GoalCell.X;
				int goalY = invis.GoalCell.Y;
				int circle1X = 0;
				int circle1Y = 0;
				int circle2X = 0;
				int circle2Y = 0;

				foreach (List<InvisibleMazeCell> cellRow in invis.Cells)
				{
					foreach (InvisibleMazeCell cell in cellRow)
					{
						FieldInfo fieldInfo = typeof(InvisibleMazeCell).GetField("cell", BindingFlags.NonPublic | BindingFlags.Instance);
						MazeCell cellData = (MazeCell) fieldInfo.GetValue(cell);
						if (cell.Identifier1 != null)
						{
							circle1X = cellData.X;
							circle1Y = cellData.Y;
						}
						else if (cell.Identifier2 != null)
						{
							circle2X = cellData.X;
							circle2Y = cellData.Y;
						}
					}
				}

				mazeState.squarePosition = new MazeCoordinate();
				mazeState.squarePosition.column = StartX;
				mazeState.squarePosition.row = startY;
				mazeState.trianglePosition = new MazeCoordinate();
				mazeState.trianglePosition.column = goalX;
				mazeState.trianglePosition.row = goalY;
				mazeState.circlePositions = new MazeCoordinate[2];
				mazeState.circlePositions[0] = new MazeCoordinate();
				mazeState.circlePositions[0].column = circle1X;
				mazeState.circlePositions[0].row = circle1Y;
				mazeState.circlePositions[1] = new MazeCoordinate();
				mazeState.circlePositions[1].column = circle2X;
				mazeState.circlePositions[1].row = circle2Y;

			}

			else if (comp.ComponentType == ComponentTypeEnum.Password)
			{
				PasswordComponent pass = (PasswordComponent) comp;
				FieldInfo fieldInfo = typeof(PasswordComponent).GetField("m_CurrentLayout", BindingFlags.NonPublic | BindingFlags.Instance);
				PasswordLayout layout = (PasswordLayout) fieldInfo.GetValue(pass);

				PasswordModuleState passState = (PasswordModuleState) bombState.modules.FirstOrDefault(module => module.component == comp);

				passState.currentWord = layout.GetCurrentWord();
				passState.goalWord = pass.CorrectWord;

			}
		}
		return bombState;
	}

	#region Helper functions

	private string BombSide(float xRotation, float zRotation)
	{
		// Normalize the angles between 0 and 360
		xRotation = NormalizeAngle(xRotation);
		zRotation = NormalizeAngle(zRotation);

		// Check for top/bottom tilt (looking up/down)
		if (xRotation > 45f && xRotation < 135f)
			return "bottom";  // Looking downward
		if (xRotation > 225f && xRotation < 315f)
			return "top";     // Looking upward

		if (zRotation >= 315f || zRotation < 45f)
			return "front";
		if (zRotation >= 45f && zRotation < 135f)
			return "right";
		if (zRotation >= 135f && zRotation < 225f)
			return "back";
		if (zRotation >= 225f && zRotation < 315f)
			return "left";

		return "unknown";
	}

	private float NormalizeAngle(float angle)
	{
		angle %= 360f;
		if (angle < 0f)
			angle += 360f;
		return angle;
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

public class MemoryModuleException : Exception
{
	public MemoryModuleException(string message) : base(message) { }
}

public class BaseModuleState
{
	public event Action OnStrike;
	public event Action OnPass;

	public bool isSolved { get; set; }
	public bool inFocus { get; set; }
	public bool onFront { get; set; }
	public int index { get; set; }
	public string name { get; set; }
	[JsonIgnore]
	public BombComponent component { get; set; }

	public BaseModuleState(BombComponent comp)
	{
		AnchorInfo anchor = GetClosestAnchorInfo(comp);
		onFront = anchor.onFront;
		index = anchor.index;

		FieldInfo fieldInfo = typeof(BombComponent).GetField("isFocused", BindingFlags.NonPublic | BindingFlags.Instance);
		inFocus = (bool) fieldInfo.GetValue(comp);

		comp.OnStrike += (_) =>
		{
			OnStrike?.Invoke();
			return false;
		};
		comp.OnPass += (_) =>
		{
			OnPass?.Invoke();
			isSolved = true;
			return false;
		};
	}

	private AnchorInfo GetClosestAnchorInfo(BombComponent comp)
	{
		Bomb bomb = comp.Bomb;
		float minDistance = float.MaxValue;
		int closestIndex = -1;
		bool onFront = false;

		var frontAnchors = bomb.Faces[0].Anchors;
		for (int i = 0; i < frontAnchors.Count; i++)
		{
			float distance = Vector3.Distance(comp.transform.position, frontAnchors[i].position);
			if (distance < minDistance)
			{
				minDistance = distance;
				closestIndex = i;
				onFront = true;
			}
		}

		var backAnchors = bomb.Faces[1].Anchors;
		for (int i = 0; i < backAnchors.Count; i++)
		{
			float distance = Vector3.Distance(comp.transform.position, backAnchors[i].position);
			if (distance < minDistance)
			{
				minDistance = distance;
				closestIndex = i;
				onFront = false;
			}
		}

		return new AnchorInfo { index = closestIndex, onFront = onFront };
	}

}

// --- Button Module ---
public class ButtonModuleState : BaseModuleState
{
	public string buttonColor { get; set; }
	public string buttonWord { get; set; }
	public bool isHeld { get; set; }
	public string stripColor { get; set; }

	public ButtonModuleState(BombComponent comp) : base(comp)
	{
		ButtonComponent button = (ButtonComponent) comp;
		string buttonColor = button.ButtonColor.ToString().ToLower();
		string buttonWord = button.ButtonInstruction.ToString();
		string stripColor = button.IndicatorColor.ToString().ToLower();

		name = "BigButton";
		this.buttonColor = buttonColor;
		this.buttonWord = buttonWord;
		isHeld = button.IsHolding;
		component = comp;
		this.stripColor = isHeld ? stripColor : null;
	}
}

// --- Keypad Module ---
public class KeyPadButtonState
{
	public string symbol { get; set; }
	public string color { get; set; }
}

public class KeypadModuleState : BaseModuleState
{
	public KeyPadButtonState topLeft { get; set; }
	public KeyPadButtonState topRight { get; set; }
	public KeyPadButtonState bottomLeft { get; set; }
	public KeyPadButtonState bottomRight { get; set; }

	public KeypadModuleState(BombComponent comp) : base (comp)
	{
		KeypadComponent keypad = (KeypadComponent) comp;
		KeyPadButtonState[] KeypadButtons = new KeyPadButtonState[4];

		for (int i = 0; i < 4; i++)
		{
			KeypadButton button = keypad.buttons[i];
			KeypadButtons[i] = new KeyPadButtonState();
			KeypadButtons[i].symbol = button.GetValue();
			if (button.LED_Correct.activeInHierarchy)
			{
				KeypadButtons[i].color = "green";
			}
			else if (button.LED_Wrong.activeInHierarchy)
			{
				KeypadButtons[i].color = "red";
			}
			else
			{
				KeypadButtons[i].color = null;
			}
		}

		topLeft = KeypadButtons[0];
		topRight = KeypadButtons[1];
		bottomLeft = KeypadButtons[2];
		bottomRight = KeypadButtons[3];
		name = "Keypad";
		component = comp;
	}

}

// --- Simon Says ---
public class SimonSaysModuleState : BaseModuleState
{
	public List<string> beepSequence { get; set; }
	public int solveProgress { get; set; }

	public SimonSaysModuleState(BombComponent comp) : base (comp)
	{
		SimonComponent simon = (SimonComponent) comp;

		FieldInfo sequenceField = typeof(SimonComponent).GetField("currentSequence", BindingFlags.NonPublic | BindingFlags.Instance);
		FieldInfo progressField = typeof(SimonComponent).GetField("lastIndex", BindingFlags.NonPublic | BindingFlags.Instance);

		int[] sequence = (int[]) sequenceField.GetValue(simon);
		int solveProgress = (int) progressField.GetValue(simon);

		List<string> beepSequence = new List<string>();
		foreach (int beep in sequence)
		{
			switch (beep)
			{
				case 0:
					beepSequence.Add("red");
					break;
				case 1:
					beepSequence.Add("blue");
					break;
				case 2:
					beepSequence.Add("green");
					break;
				case 3:
					beepSequence.Add("yellow");
					break;
			}
		}

		this.beepSequence = beepSequence;
		this.solveProgress = solveProgress;
		name = "Simon";
		component = comp;

		comp.OnStrike += (_) => {
			simon.PlaySequenceDelay = 1f;
			simon.StopAllCoroutines();
			simon.StartCoroutine("PlaySequence", simon.PlaySequenceDelay);
			return false;
		};
	}
}

// --- Wire base + variants ---
public class BaseWire
{
	public bool isCut { get; set; }
	public string color { get; set; }
}

public class WireSetWireState : BaseWire
{
	public int position { get; set; }
}

public class ComplicatedWireState : BaseWire
{
	public int position { get; set; }
	public bool isLedOn { get; set; }
	public bool hasStar { get; set; }
}

public class WireSequenceWireState : BaseWire
{
	public int startPositionNumber { get; set; }
	public string endPositionLetter { get; set; }
}

// --- Wire Modules ---
public class WireSetModuleState : BaseModuleState
{
	public WireSetWireState[] wires { get; set; }

	public WireSetModuleState(BombComponent comp) : base(comp)
	{
		WireSetComponent wireset = (WireSetComponent) comp;
		List<int> indices = new List<int>();
		Selectable selectable = wireset.GetComponent<Selectable>();
		for (int i = 0; i < selectable.Children.Length; i++)
		{
			if (selectable.Children[i] != null)
			{
				indices.Add(int.Parse(selectable.Children[i].name[selectable.Children[i].name.Length - 1].ToString()) - 1);
			}
		}

		wires = new WireSetWireState[6];
		// Just assign the spaces that contain wires
		int indicesIndex = 0;
		foreach (SnippableWire wire in wireset.wires)
		{
			wires[indices[indicesIndex]] = new WireSetWireState
			{
				color = wire.GetColor().ToString().ToLower(),
				isCut = wire.Snipped,
				position = indices[indicesIndex]
			};
			indicesIndex++;
		}
		name = "Wires";
		component = comp;
	}
}

public class ComplicatedWiresModuleState : BaseModuleState
{
	public ComplicatedWireState[] wires { get; set; }
	public ComplicatedWiresModuleState(BombComponent comp) : base(comp)
	{
		VennWireComponent venn = (VennWireComponent) comp;

		List<int> indices = new List<int>();
		wires = new ComplicatedWireState[6];

		Selectable selectable = venn.GetComponent<Selectable>();
		for (int i = 0; i < selectable.Children.Length; i++)
		{
			if (selectable.Children[i] != null)
			{
				indices.Add(int.Parse(selectable.Children[i].name[selectable.Children[i].name.Length - 1].ToString()) - 1);
			}
		}

		int indicesIndex = 0;
		foreach (VennSnippableWire wire in venn.ActiveWires)
		{
			wires[indices[indicesIndex]] = new ComplicatedWireState
			{
				hasStar = wire.HasSymbol,
				isLedOn = wire.IsLEDOn,
				isCut = wire.Snipped,
				color = wire.Color.ToString().ToLower(),
				position = indices[indicesIndex]
			};
			indicesIndex++;
		}
		name = "Venn";
		component = comp;
	}
}

public class WireSequenceModuleState : BaseModuleState
{
	public int panel { get; set; }
	public WireSequenceWireState[] wires { get; set; }

	public WireSequenceModuleState(BombComponent comp) : base(comp)
	{
		var wireSeq = (WireSequenceComponent) comp;

		panel = (int) typeof(WireSequenceComponent)
			.GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance)
			.GetValue(wireSeq) + 1;

		var configs = (List<WireSequenceComponent.WireConfiguration>) typeof(WireSequenceComponent)
			.GetField("wireSequence", BindingFlags.NonPublic | BindingFlags.Instance)
			.GetValue(wireSeq);

		wires = new WireSequenceWireState[12];

		for (int i = 0; i < configs.Count && i < 12; i++)
		{
			var config = configs[i];
			if (config.NoWire) continue;

			wires[i] = new WireSequenceWireState
			{
				startPositionNumber = i,
				endPositionLetter = ((char) ('A' + config.To)).ToString(),
				color = config.Wire.GetColor().ToString().ToLower(),
				isCut = config.Wire.Snipped
			};
		}

		name = "WireSequence";
		component = comp;
	}
}

// --- Maze ---
public class MazeCoordinate
{
	public int row { get; set; }
	public int column { get; set; }
}

public class MazeModuleState : BaseModuleState
{
	public int numRows { get; set; } = 6;
	public int numColumns { get; set; } = 6;

	public MazeCoordinate trianglePosition { get; set; }
	public MazeCoordinate squarePosition { get; set; }
	public MazeCoordinate[] circlePositions { get; set; }

	public MazeModuleState(BombComponent comp) : base(comp)
	{
		var invis = (InvisibleWallsComponent) comp;

		squarePosition = new MazeCoordinate
		{
			column = invis.CurrentCell.X,
			row = invis.CurrentCell.Y
		};

		trianglePosition = new MazeCoordinate
		{
			column = invis.GoalCell.X,
			row = invis.GoalCell.Y
		};

		var fieldInfo = typeof(InvisibleMazeCell).GetField("cell", BindingFlags.NonPublic | BindingFlags.Instance);
		var circles = new List<MazeCoordinate>();

		foreach (var row in invis.Cells)
		{
			foreach (var cell in row)
			{
				var cellData = (MazeCell) fieldInfo.GetValue(cell);
				if (cell.Identifier1 != null || cell.Identifier2 != null)
				{
					circles.Add(new MazeCoordinate
					{
						column = cellData.X,
						row = cellData.Y
					});
				}
			}
		}

		// Ensure exactly two circle positions
		circlePositions = circles.Take(2).ToArray();

		name = "Maze";
		component = comp;
	}
}


// --- Memory ---
public class MemoryModuleState : BaseModuleState
{
	public string displayNumber { get; set; }
	public string[] buttonNumbers { get; set; }
	public int stage { get; set; }

	public MemoryModuleState(BombComponent comp) : base(comp)
	{
		MemoryComponent memory = (MemoryComponent) comp;
		FieldInfo fieldInfo = typeof(MemoryComponent).GetField("buttonsEmerged", BindingFlags.NonPublic | BindingFlags.Instance);
		bool buttonsEmerged = (bool) fieldInfo.GetValue(memory);
		if (!buttonsEmerged)
		{
			GptntDebug.Log("[Exception] Buttons not yet emrged");
			throw new MemoryModuleException("Memory buttons not yet emerged");
		}
		stage = memory.CurrentStage + 1;
		displayNumber = memory.DisplayText.text;
		string[] buttonValues = new string[4];
		foreach (KeypadButton button in memory.Buttons)
		{
			buttonValues[button.ButtonIndex] = button.Text.text;
		}
		buttonNumbers = buttonValues;
		name = "Memory";
		component = comp;
	}
}

// --- Morse Code ---
public class MorseCodeModuleState : BaseModuleState
{
	public string sequence { get; set; }
	public float currentFrequency { get; set; }
	public float correctFrequency { get; set; }

	public MorseCodeModuleState(BombComponent comp) : base(comp)
	{
		MorseCodeComponent morse = (MorseCodeComponent) comp;
		FieldInfo fieldInfo = typeof(MorseCodeComponent).GetField("chosenWord", BindingFlags.NonPublic | BindingFlags.Instance);
		string word = (string) fieldInfo.GetValue(morse);

		currentFrequency = morse.CurrentFrequency;
		sequence = word;
		correctFrequency = morse.ChosenFrequency;
		name = "Morse";
		component = comp;
	}
}

// --- Password ---
public class PasswordModuleState : BaseModuleState
{
	public string currentWord { get; set; }
	public string goalWord { get; set; }

	public PasswordModuleState(BombComponent comp) : base(comp)
	{
		PasswordComponent pass = (PasswordComponent) comp;
		FieldInfo fieldInfo = typeof(PasswordComponent).GetField("m_CurrentLayout", BindingFlags.NonPublic | BindingFlags.Instance);
		PasswordLayout layout = (PasswordLayout) fieldInfo.GetValue(pass);

		currentWord = layout.GetCurrentWord();
		goalWord = pass.CorrectWord;

		name = "Password";
		component = comp;
	}
}

// --- Who’s On First ---
public class WhosOnFirstModuleState : BaseModuleState
{
	public string displayWord { get; set; }
	public string[] buttonWords { get; set; }
	public int stage { get; set; }

	public WhosOnFirstModuleState (BombComponent comp) : base(comp)
	{
		WhosOnFirstComponent whoFirst = (WhosOnFirstComponent) comp;
		int stage = whoFirst.CurrentStage + 1;
		string[] buttonValues = new string[6];
		foreach (KeypadButton button in whoFirst.Buttons)
		{
			buttonValues[button.ButtonIndex] = button.Text.text;
		}

		this.stage = stage;
		buttonWords = buttonValues;
		displayWord = whoFirst.DisplayText.text;
		name = "WhosOnFirst";
		component = comp;
	}
}

public class TimerModuleState : BaseModuleState
{
	public float secondsRemaining { get; set; }

	public TimerModuleState (BombComponent comp) : base(comp)
	{
		name = "Timer";
	}
}

// --- Needy Modules ---
//public class DischargeModuleState : BaseModuleState
//{
//	public bool isBeingNeedy { get; set; }
//	public int secondsUntilDischarge { get; set; }
//}

//public class KnobModuleState : BaseModuleState
//{
//	public bool isBeingNeedy { get; set; }
//	public string knobPosition { get; set; }
//	public Dictionary<int, bool> ledPosition { get; set; }
//}

//public class GasModuleState : BaseModuleState
//{
//	public bool isBeingNeedy { get; set; }
//	public string message { get; set; }
//	public int timer { get; set; }
//}

public class BaseWidgetState
{
	public string position { get; set; }
	public string name { get; set; }

	public static string CoercePosition(string parentName)
	{
		return parentName.Replace("Faces", "").ToLower();
	}
}

public class BatteryWidgetState : BaseWidgetState
{
	public string batteryType { get; set; }
	public int batteriesCount { get; set; }

	public static BatteryWidgetState FromWidget(BatteryWidget widget)
	{
		FieldInfo fieldInfo = typeof(BatteryWidget).GetField("batteryType", BindingFlags.NonPublic | BindingFlags.Instance);
		var batteryType = (BatteryWidget.BatteryTypeEnum) fieldInfo.GetValue(widget);

		return new BatteryWidgetState
		{
			batteryType = batteryType == BatteryWidget.BatteryTypeEnum.DoubleA ? "AA" : "D",
			batteriesCount = batteryType == BatteryWidget.BatteryTypeEnum.DoubleA ? 2 : 1,
			position = CoercePosition(widget.transform.parent.name),
			name = "Battery"
		};
	}
}

public class IndicatorWidgetState : BaseWidgetState
{
	public string label { get; set; }
	public bool lightActivated { get; set; }

	public static IndicatorWidgetState FromWidget(IndicatorWidget widget)
	{
		return new IndicatorWidgetState
		{
			label = widget.Label,
			lightActivated = widget.On,
			position = CoercePosition(widget.transform.parent.name),
			name = "Indicator"
		};
	}
}

public class PortWidgetState : BaseWidgetState
{
	public List<string> portType { get; set; }

	public static PortWidgetState FromWidget(PortWidget widget)
	{
		FieldInfo fieldInfo = typeof(PortWidget).GetField("presentPorts", BindingFlags.NonPublic | BindingFlags.Instance);
		var portType = (PortWidget.PortType) fieldInfo.GetValue(widget);

		var ports = new List<string>();
		if ((portType & PortWidget.PortType.DVI) != 0) ports.Add("DVI-D");
		if ((portType & PortWidget.PortType.Parallel) != 0) ports.Add("Parallel");
		if ((portType & PortWidget.PortType.PS2) != 0) ports.Add("PS/2");
		if ((portType & PortWidget.PortType.RJ45) != 0) ports.Add("RJ-45");
		if ((portType & PortWidget.PortType.Serial) != 0) ports.Add("Serial");
		if ((portType & PortWidget.PortType.StereoRCA) != 0) ports.Add("Stereo RCA");

		return new PortWidgetState
		{
			portType = ports,
			position = CoercePosition(widget.transform.parent.name),
			name = "Port"
		};
	}
}

public class SerialNumberWidgetState : BaseWidgetState
{
	public string serialNumber { get; set; }

	public static SerialNumberWidgetState FromWidget(SerialNumber widget)
	{
		FieldInfo fieldInfo = typeof(SerialNumber).GetField("serialString", BindingFlags.NonPublic | BindingFlags.Instance);
		string serialString = (string) fieldInfo.GetValue(widget);

		return new SerialNumberWidgetState
		{
			serialNumber = serialString,
			position = CoercePosition(widget.transform.parent.name),
			name = "SerialNumber"
		};
	}
}

public class BombState
{
	public int seed { get; set; }
	public int maxStrikes { get; set; } = 3;
	public bool isDetonated { get; set; }
	public bool isSolved { get; set; }
	public bool isLightOn { get; set; }
	public string bombSide { get; set; }
	public TimerModuleState timerModule { get; set; }
	public List<BaseWidgetState> widgets { get; set; }
	public List<BaseModuleState> modules { get; set; }
	public List<string> strikes { get; set; }
}