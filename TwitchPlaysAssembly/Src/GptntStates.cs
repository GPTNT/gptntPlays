using System.IO;
using System;
using UnityEngine;
using System.Collections.Generic;
using Assets.Scripts.Missions;
using System.Reflection;
using BombGame;
using Assets.Scripts.Components.VennWire;


public class GptntStates : MonoBehaviour 
{
	public string logFilePath;
	public long lastPosition = 0;
	private string line;
	public BombState bombState;
	public ExampleWebService webService;
	TwitchBomb twitchBomb;
	public bool readyToGive = false;
	KMBombInfo bombInfo;


	public void Start()
	{
		bombInfo = GetComponent<KMBombInfo>();
		webService = GetComponent<ExampleWebService>();


		bombInfo.OnBombExploded += () =>
		{
			bombState.isDetonated = true;
			bombState.currentStrikes = twitchBomb.Bomb.NumStrikes;
			bombState.timerModule.secondsRemaining = twitchBomb.Bomb.GetTimer().TimeRemaining;
			if (bombState.timerModule.secondsRemaining < 0)
			{
				bombState.timerModule.secondsRemaining = 0;
			}
			else
			{
				bombState.currentStrikes++;
			}

		};

		bombInfo.OnBombSolved += () =>
		{
			bombState.isSolved = true;
			bombState.currentStrikes = twitchBomb.Bomb.NumStrikes;
			bombState.timerModule.secondsRemaining = twitchBomb.Bomb.GetTimer().TimeRemaining;
		};
	}

	public void GetInitialBombState()
	{
		bombState = new BombState();
		string gameState = webService.gameState;
		bombState.isLightOn = gameState.Equals("Lights On");
		bombState.isSolved = false;
		bombState.isDetonated = false;
		bombState.widgets = new List<BaseWidgetState>();
		bombState.modules = new List<BaseModuleState>();
		bombState.strikes = new List<string>();
		twitchBomb = FindObjectOfType<TwitchBomb>();
		Bomb bomb = twitchBomb.Bomb;

		bombState.seed = bomb.Seed;
		try
		{
			bombState.timerModule.secondsRemaining = bomb.GetTimer().TimeRemaining;
		}
		catch (Exception ex)
		{
			GptntDebug.Log("Error setting TimeRemaining: " + ex);
		}

		try
		{
			bombState.currentStrikes = bomb.NumStrikes;
		}
		catch (Exception ex)
		{
			GptntDebug.Log("Error setting CurrentStrikes: " + ex);
		}

		try
		{
			bombState.maxStrikes = bomb.NumStrikesToLose;
		}
		catch (Exception ex)
		{
			GptntDebug.Log("Error setting MaxStrikes: " + ex);
		}


		foreach (Widget widget in bomb.WidgetManager.GetWidgets())
		{
			string widgetFace;
			if (widget.transform.parent.name.Equals("BottomFaces"))
			{
				widgetFace = "bottom";
			}
			else if (widget.transform.parent.name.Equals("TopFaces"))
			{
				widgetFace = "top";
			}
			else if (widget.transform.parent.name.Equals("RightFaces"))
			{
				widgetFace = "right";
			}
			else
			{
				widgetFace = "left";
			}
			try
			{
				if (widget.GetType() == typeof(SerialNumber))
				{
					try
					{
						SerialNumber serialNumber = (SerialNumber) widget;
						FieldInfo fieldInfo = typeof(SerialNumber).GetField("serialString", BindingFlags.NonPublic | BindingFlags.Instance);
						string serialString = (string) fieldInfo.GetValue(serialNumber);
						SerialNumberWidgetState serialState = new SerialNumberWidgetState();
						serialState.serialNumber = serialString;
						serialState.position = widgetFace;
						serialState.name = "SerialNumber";
						bombState.widgets.Add(serialState);
					}
					catch (Exception ex)
					{
						GptntDebug.Log("Error parsing SerialNumber widget: " + ex);
					}
				}

				else if (widget.GetType() == typeof(BatteryWidget))
				{
					try
					{
						BatteryWidget batteryWidget = (BatteryWidget) widget;
						BatteryWidgetState batteryState = new BatteryWidgetState();
						FieldInfo fieldInfo = typeof(BatteryWidget).GetField("batteryType", BindingFlags.NonPublic | BindingFlags.Instance);
						BatteryWidget.BatteryTypeEnum batteryType = (BatteryWidget.BatteryTypeEnum) fieldInfo.GetValue(batteryWidget);

						if (batteryType.Equals(BatteryWidget.BatteryTypeEnum.DoubleA))
						{
							batteryState.batteryType = "AA";
							batteryState.batteriesCount = 2;
						}
						else if (batteryType.Equals(BatteryWidget.BatteryTypeEnum.DCell))
						{
							batteryState.batteryType = "D";
							batteryState.batteriesCount = 1;
						}
						batteryState.position = widgetFace;
						batteryState.name = "Battery";
						bombState.widgets.Add(batteryState);
					}
					catch (Exception ex)
					{
						GptntDebug.Log("Error parsing BatteryWidget: " + ex);
					}
				}

				else if (widget.GetType() == typeof(PortWidget))
				{
					try
					{
						PortWidget portWidget = (PortWidget) widget;
						PortWidgetState portWidgetState = new PortWidgetState();
						FieldInfo fieldInfo = typeof(PortWidget).GetField("presentPorts", BindingFlags.NonPublic | BindingFlags.Instance);
						PortWidget.PortType portType = (PortWidget.PortType) fieldInfo.GetValue(portWidget);

						List<string> ports = new List<string>();

						if ((portType & PortWidget.PortType.DVI) != 0)
						{
							ports.Add("DVI-D");
						}
						if ((portType & PortWidget.PortType.Parallel) != 0)
						{
							ports.Add("Parallel");
						}
						if ((portType & PortWidget.PortType.PS2) != 0)
						{
							ports.Add("PS/2");
						}
						if ((portType & PortWidget.PortType.RJ45) != 0)
						{
							ports.Add("RJ-45");
						}
						if ((portType & PortWidget.PortType.Serial) != 0)
						{
							ports.Add("Serial");
						}
						if ((portType & PortWidget.PortType.StereoRCA) != 0)
						{
							ports.Add("Stereo RCA");
						}

						portWidgetState.portType = ports;
						portWidgetState.position = widgetFace;
						portWidgetState.name = "Port";
						bombState.widgets.Add(portWidgetState);
					}
					catch (Exception ex)
					{
						GptntDebug.Log("Error parsing PortWidget: " + ex);
					}
				}

				else if (widget.GetType() == typeof(IndicatorWidget))
				{
					try
					{
						IndicatorWidget indicatorWidget = (IndicatorWidget) widget;
						IndicatorWidgetState indicatorWidgetState = new IndicatorWidgetState();
						indicatorWidgetState.label = indicatorWidget.Label;
						indicatorWidgetState.lightActivated = indicatorWidget.On;
						indicatorWidgetState.position = widgetFace;
						indicatorWidgetState.name = "Indicator";
						bombState.widgets.Add(indicatorWidgetState);
					}
					catch (Exception ex)
					{
						GptntDebug.Log("Error parsing IndicatorWidget: " + ex);
					}
				}
			}
			catch (Exception ex)
			{
				GptntDebug.Log("Error processing widget of type " + widget.GetType() + ": " + ex);
			}
		}


		foreach (BombComponent comp in bomb.BombComponents)
		{
			Transform closest = null;
			float minDistance = float.MaxValue;
			bool onFront = false;
			int closestIndex = -1;

			var frontAnchors = bomb.Faces[0].Anchors;
			for (int i = 0; i < frontAnchors.Count; i++)
			{
				var anchor = frontAnchors[i];
				float distance = Vector3.Distance(comp.transform.position, anchor.position);
				if (distance < minDistance)
				{
					minDistance = distance;
					onFront = true;
					closest = anchor;
					closestIndex = i;
				}
			}

			var backAnchors = bomb.Faces[1].Anchors;
			for (int i = 0; i < backAnchors.Count; i++)
			{
				var anchor = backAnchors[i];
				float distance = Vector3.Distance(comp.transform.position, anchor.position);
				if (distance < minDistance)
				{
					minDistance = distance;
					onFront = false;
					closest = anchor;
					closestIndex = i;
				}
			}



			bool isSolved = comp.IsSolved;
			FieldInfo fieldInfo3 = typeof(BombComponent).GetField("isFocused", BindingFlags.NonPublic | BindingFlags.Instance);
			bool isFocused = (bool) fieldInfo3.GetValue(comp);

			if (comp.ComponentType == ComponentTypeEnum.Timer)
			{
				TimerComponent time = (TimerComponent) comp;
				TimerModuleState timerState = new TimerModuleState();
				timerState.secondsRemaining = bomb.GetTimer().TimeRemaining;
				timerState.onFront = onFront;
				timerState.index = closestIndex;
				timerState.name = "Timer";
				bombState.timerModule =  timerState;
			}



			if (comp.ComponentType == ComponentTypeEnum.Simon)
			{
				SimonComponent simon = (SimonComponent) comp;
				FieldInfo fieldInfo1 = typeof(SimonComponent).GetField("currentSequence", BindingFlags.NonPublic | BindingFlags.Instance);
				int[] sequence = (int[]) fieldInfo1.GetValue(simon);
				FieldInfo fieldInfo2 = typeof(SimonComponent).GetField("solveProgress", BindingFlags.NonPublic | BindingFlags.Instance);
				int solveProgress = (int) fieldInfo2.GetValue(simon);

				SimonSaysModuleState simonState = new SimonSaysModuleState();
				List<string> beepSequence = new List<string>();
				foreach (int beep in sequence)
				{
					if (beep == 0)
					{
						beepSequence.Add("Red");
					}
					else if (beep == 1)
					{
						beepSequence.Add("Blue");
					}
					else if (beep == 2)
					{
						beepSequence.Add("Green");
					}
					else if (beep == 3)
					{
						beepSequence.Add("Yellow");
					}
				}


				simonState.beepSequence = beepSequence;
				simonState.solveProgress = solveProgress;
				simonState.isSolved = isSolved;
				simonState.inFocus = isFocused;
				simonState.onFront = onFront;
				simonState.index = closestIndex;
				simonState.name = "Simon";
				bombState.modules.Add(simonState);
				comp.OnStrike += (_) => { bombState.strikes.Add(simonState.name); return false; };

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
						indices.Add(int.Parse(component.Children[i].name[component.Children[i].name.Length-1].ToString())-1);
					}
				}
				WireColor[] colors = new WireColor[6];
				bool[] is_snipped = new bool[6];

				WireSetModuleState wireSetState = new WireSetModuleState();
				wireSetState.wires = new WireSetWireState[6];
				// Just assign the spaces that contain wires
				int indicesIndex = 0;
				foreach (SnippableWire wire in wireset.wires)
				{
					wireSetState.wires[indices[indicesIndex]] = new WireSetWireState();
					wireSetState.wires[indices[indicesIndex]].color = wire.GetColor().ToString();
					wireSetState.wires[indices[indicesIndex]].isCut = wire.Snipped;
					wireSetState.wires[indices[indicesIndex]].position = indices[indicesIndex];
					indicesIndex++;
				}

				wireSetState.isSolved = isSolved;
				wireSetState.inFocus = isFocused;
				wireSetState.onFront = onFront;
				wireSetState.index = closestIndex;
				wireSetState.name = "Wires";
				bombState.modules.Add(wireSetState);
				comp.OnStrike += (_) => { bombState.strikes.Add(wireSetState.name); return false; };
			}
			else if (comp.ComponentType == ComponentTypeEnum.BigButton)
			{
				ButtonComponent button = (ButtonComponent) comp;
				string buttonColor = button.ButtonColor.ToString();
				string buttonMessage = button.ButtonInstruction.ToString();
				string stripColor = button.IndicatorColor.ToString();

				ButtonModuleState buttonState =	new ButtonModuleState();
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
				buttonState.isSolved = isSolved;
				buttonState.inFocus = isFocused;
				buttonState.onFront = onFront;
				buttonState.index = closestIndex;
				buttonState.name = "BigButton";
				bombState.modules.Add(buttonState);
				comp.OnStrike += (_) => { bombState.strikes.Add(buttonState.name); return false; };

			}
			else if (comp.ComponentType == ComponentTypeEnum.Keypad)
			{
				KeypadComponent keypad = (KeypadComponent) comp;

				KeypadModuleState keypadState = new KeypadModuleState();
				KeyPadButtonState[] KeypadButtons = new KeyPadButtonState[4];

				for (int i = 0; i < 4; i++)
				{
					KeypadButton button = keypad.buttons[i];
					KeypadButtons[i] = new KeyPadButtonState();
					KeypadButtons[i].symbol = button.GetValue();
					if (button.LED_Correct.active)
					{
						KeypadButtons[i].color = "Green";


					}
					else if (button.LED_Wrong.active)
					{
						KeypadButtons[i].color = "Red";

					}
					else
					{
						KeypadButtons[i].color = null;
					}
				}
				keypadState.isSolved = isSolved;
				keypadState.inFocus = isFocused;
				keypadState.onFront = onFront;
				keypadState.index = closestIndex;
				keypadState.topLeft = KeypadButtons[0];
				keypadState.topRight = KeypadButtons[1];
				keypadState.bottomLeft = KeypadButtons[2];
				keypadState.bottomRight = KeypadButtons[3];
				keypadState.name = "KeyPad";
				bombState.modules.Add(keypadState);
				comp.OnStrike += (_) => { bombState.strikes.Add(keypadState.name); return false; };


			}
			else if (comp.ComponentType == ComponentTypeEnum.WhosOnFirst)
			{
				WhosOnFirstComponent whoFirst = (WhosOnFirstComponent) comp;
				int stage = whoFirst.CurrentStage;
				string[] buttonValues = new string[6];
				foreach (KeypadButton button in whoFirst.Buttons)
				{
					buttonValues[button.ButtonIndex] = button.Text.text;

				}
				string displayWord = whoFirst.DisplayText.text;

				WhosOnFirstModuleState whoFirstState = new WhosOnFirstModuleState();
				whoFirstState.stage = stage;
				whoFirstState.buttonWords = buttonValues;
				whoFirstState.displayWord = displayWord;
				whoFirstState.isSolved = isSolved;
				whoFirstState.inFocus = isFocused;
				whoFirstState.onFront = onFront;
				whoFirstState.index = closestIndex;
				whoFirstState.name = "WhosOnFirst";
				bombState.modules.Add(whoFirstState);
				comp.OnStrike += (_) => { bombState.strikes.Add(whoFirstState.name); return false; };
			}

			else if (comp.ComponentType == ComponentTypeEnum.Memory)
			{
				MemoryComponent memory = (MemoryComponent) comp;
				MemoryModuleState memoryState = new MemoryModuleState();
				memoryState.stage = memory.CurrentStage;
				memoryState.displayNumber = int.Parse(memory.DisplayText.text);
				int[] buttonValues = new int[4];
				foreach (KeypadButton button in memory.Buttons)
				{
					buttonValues[button.ButtonIndex] = int.Parse(button.Text.text);

				}
				memoryState.buttonNumbers = buttonValues;
				memoryState.isSolved = isSolved;
				memoryState.inFocus = isFocused;
				memoryState.onFront = onFront;
				memoryState.index = closestIndex;
				memoryState.name = "Memory";
				bombState.modules.Add(memoryState);
				comp.OnStrike += (_) => { bombState.strikes.Add(memoryState.name); return false; };
			}
			
			else if (comp.ComponentType == ComponentTypeEnum.Morse)
			{
				MorseCodeComponent morse = (MorseCodeComponent) comp;
				int currentFrequency = morse.CurrentFrequency;
				FieldInfo fieldInfo = typeof(MorseCodeComponent).GetField("chosenWord", BindingFlags.NonPublic | BindingFlags.Instance);
				string word = (string)fieldInfo.GetValue(morse);

				MorseCodeModuleState morseState = new MorseCodeModuleState();
				
				morseState.currentFrequency = currentFrequency;
				morseState.sequence = word;
				morseState.correctFrequency = morse.ChosenFrequency;
				morseState.isSolved = isSolved;
				morseState.inFocus = isFocused;
				morseState.onFront = onFront;
				morseState.index = closestIndex;
				morseState.name = "Morse";
				bombState.modules.Add(morseState);
				comp.OnStrike += (_) => { bombState.strikes.Add(morseState.name); return false; };
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

				ComplicatedWiresModuleState compState = new ComplicatedWiresModuleState();
				compState.wires = new ComplicatedWireState[6];

				int indicesIndex = 0;
				foreach (VennSnippableWire wire in venn.ActiveWires)
				{
					compState.wires[indices[indicesIndex]] = new ComplicatedWireState();
					compState.wires[indices[indicesIndex]].hasStar = wire.HasSymbol;
					compState.wires[indices[indicesIndex]].isLedOn = wire.IsLEDOn;
					compState.wires[indices[indicesIndex]].isCut = wire.Snipped;
					compState.wires[indices[indicesIndex]].color = wire.Color.ToString();
					compState.wires[indices[indicesIndex]].position = indices[indicesIndex];

					indicesIndex++;
				}

				compState.isSolved = isSolved;
				compState.inFocus = isFocused;
				compState.onFront = onFront;
				compState.index = closestIndex;
				compState.name = "Venn";
				bombState.modules.Add(compState);
				comp.OnStrike += (_) => { bombState.strikes.Add(compState.name); return false; };
			}

			else if (comp.ComponentType == ComponentTypeEnum.WireSequence)
			{
				WireSequenceComponent wireSeq = (WireSequenceComponent) comp;
				FieldInfo fieldInfo = typeof(WireSequenceComponent).GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance);

				WireSequenceModuleState wireSeqState = new WireSequenceModuleState();
				wireSeqState.panel = (int) fieldInfo.GetValue(wireSeq);
				wireSeqState.wires = new WireSequenceWireState[12];

				FieldInfo fieldInfo2 = typeof(WireSequenceComponent).GetField("wireSequence", BindingFlags.NonPublic | BindingFlags.Instance);
				List<WireSequenceComponent.WireConfiguration> configs = (List<WireSequenceComponent.WireConfiguration>) fieldInfo2.GetValue(wireSeq);
				for (int i = 0; i< 12; i++)
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
						wireSeqState.wires[i].color = config.Wire.GetColor().ToString();
						wireSeqState.wires[i].isCut = config.Wire.Snipped;
					}
				}

				wireSeqState.isSolved = isSolved;
				wireSeqState.inFocus = isFocused;
				wireSeqState.onFront = onFront;
				wireSeqState.index = closestIndex;
				wireSeqState.name = "WireSequence";
				bombState.modules.Add(wireSeqState);
				comp.OnStrike += (_) => { bombState.strikes.Add(wireSeqState.name); return false; };
			}

			else if (comp.ComponentType == ComponentTypeEnum.Maze)
			{
				InvisibleWallsComponent invis = (InvisibleWallsComponent) comp;
				MazeModuleState mazeState = new MazeModuleState();
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
					foreach(InvisibleMazeCell cell in cellRow)
					{
						FieldInfo fieldInfo = typeof(InvisibleMazeCell).GetField("cell", BindingFlags.NonPublic | BindingFlags.Instance);
						MazeCell cellData = (MazeCell) fieldInfo.GetValue(cell);
						if (cell.Identifier1 != null)
						{
							circle1X = cellData.X;
							circle1Y = cellData.Y;
						}
						else if(cell.Identifier2 != null)
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

				mazeState.isSolved = isSolved;
				mazeState.inFocus = isFocused;
				mazeState.onFront = onFront;
				mazeState.index = closestIndex;
				mazeState.name = "Maze";
				bombState.modules.Add(mazeState);
				comp.OnStrike += (_) => { bombState.strikes.Add(mazeState.name); return false; };
			}

			else if (comp.ComponentType == ComponentTypeEnum.Password)
			{
				PasswordComponent pass = (PasswordComponent) comp;
				FieldInfo fieldInfo = typeof(PasswordComponent).GetField("m_CurrentLayout", BindingFlags.NonPublic | BindingFlags.Instance);
				PasswordLayout layout = (PasswordLayout) fieldInfo.GetValue(pass);

				PasswordModuleState passState = new PasswordModuleState();


				passState.currentWord = layout.GetCurrentWord();
				passState.goalWord = pass.CorrectWord;

				passState.isSolved = isSolved;
				passState.inFocus = isFocused;
				passState.onFront = onFront;
				passState.index = closestIndex;
				passState.name = "Password";
				bombState.modules.Add(passState);
				comp.OnStrike += (_) => { bombState.strikes.Add(passState.name); return false; };
			}
		}
		readyToGive = true;
	}


	public BombState UpdateBombState()
	{
		Bomb bomb = twitchBomb.Bomb;
		if (!bomb)
			return bombState;
		bombState.modules = new List<BaseModuleState>();
		string gameState = webService.gameState;
		bombState.isLightOn = gameState.Equals("Lights On");

		try
		{
			bombState.currentStrikes = bomb.NumStrikes;
		}
		catch (Exception ex)
		{
			GptntDebug.Log("Error setting CurrentStrikes: " + ex);
		}
		

		foreach (BombComponent comp in bomb.BombComponents)
		{
			Transform closest = null;
			float minDistance = float.MaxValue;
			bool onFront = false;
			int closestIndex = -1;

			var frontAnchors = bomb.Faces[0].Anchors;
			for (int i = 0; i < frontAnchors.Count; i++)
			{
				var anchor = frontAnchors[i];
				float distance = Vector3.Distance(comp.transform.position, anchor.position);
				if (distance < minDistance)
				{
					minDistance = distance;
					onFront = true;
					closest = anchor;
					closestIndex = i;
				}
			}

			var backAnchors = bomb.Faces[1].Anchors;
			for (int i = 0; i < backAnchors.Count; i++)
			{
				var anchor = backAnchors[i];
				float distance = Vector3.Distance(comp.transform.position, anchor.position);
				if (distance < minDistance)
				{
					minDistance = distance;
					onFront = false;
					closest = anchor;
					closestIndex = i;
				}
			}



			bool isSolved = comp.IsSolved;
			FieldInfo fieldInfo3 = typeof(BombComponent).GetField("isFocused", BindingFlags.NonPublic | BindingFlags.Instance);
			bool isFocused = (bool) fieldInfo3.GetValue(comp);

			if (comp.ComponentType == ComponentTypeEnum.Timer)
			{
				TimerComponent time = (TimerComponent) comp;
				TimerModuleState timerState = new TimerModuleState();
				timerState.secondsRemaining = bomb.GetTimer().TimeRemaining;
				timerState.onFront = onFront;
				timerState.index = closestIndex;
				timerState.name = "Timer";
				bombState.timerModule = timerState;
			}

			if (comp.ComponentType == ComponentTypeEnum.Simon)
			{
				SimonComponent simon = (SimonComponent) comp;
				FieldInfo fieldInfo1 = typeof(SimonComponent).GetField("currentSequence", BindingFlags.NonPublic | BindingFlags.Instance);
				int[] sequence = (int[]) fieldInfo1.GetValue(simon);
				FieldInfo fieldInfo2 = typeof(SimonComponent).GetField("solveProgress", BindingFlags.NonPublic | BindingFlags.Instance);
				int solveProgress = (int) fieldInfo2.GetValue(simon);

				SimonSaysModuleState simonState = new SimonSaysModuleState();
				List<string> beepSequence = new List<string>();
				foreach (int beep in sequence)
				{
					if (beep == 0)
					{
						beepSequence.Add("Red");
					}
					else if (beep == 1)
					{
						beepSequence.Add("Blue");
					}
					else if (beep == 2)
					{
						beepSequence.Add("Green");
					}
					else if (beep == 3)
					{
						beepSequence.Add("Yellow");
					}
				}


				simonState.beepSequence = beepSequence;
				simonState.solveProgress = solveProgress;
				simonState.isSolved = isSolved;
				simonState.inFocus = isFocused;
				simonState.onFront = onFront;
				simonState.index = closestIndex;
				simonState.name = "Simon";
				bombState.modules.Add(simonState);
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

				WireSetModuleState wireSetState = new WireSetModuleState();
				wireSetState.wires = new WireSetWireState[6];
				// Just assign the spaces that contain wires
				int indicesIndex = 0;
				foreach (SnippableWire wire in wireset.wires)
				{
					wireSetState.wires[indices[indicesIndex]] = new WireSetWireState();
					wireSetState.wires[indices[indicesIndex]].color = wire.GetColor().ToString();
					wireSetState.wires[indices[indicesIndex]].isCut = wire.Snipped;
					wireSetState.wires[indices[indicesIndex]].position = indices[indicesIndex];
					indicesIndex++;
				}

				wireSetState.isSolved = isSolved;
				wireSetState.inFocus = isFocused;
				wireSetState.onFront = onFront;
				wireSetState.index = closestIndex;
				wireSetState.name = "Wires";
				bombState.modules.Add(wireSetState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.BigButton)
			{
				ButtonComponent button = (ButtonComponent) comp;
				string buttonColor = button.ButtonColor.ToString();
				string buttonMessage = button.ButtonInstruction.ToString();
				string stripColor = button.IndicatorColor.ToString();

				ButtonModuleState buttonState = new ButtonModuleState();
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
				buttonState.isSolved = isSolved;
				buttonState.inFocus = isFocused;
				buttonState.onFront = onFront;
				buttonState.index = closestIndex;
				buttonState.name = "BigButton";
				bombState.modules.Add(buttonState);

			}

			else if (comp.ComponentType == ComponentTypeEnum.Keypad)
			{
				KeypadComponent keypad = (KeypadComponent) comp;

				KeypadModuleState keypadState = new KeypadModuleState();
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
						KeypadButtons[i].color = "Green";


					}
					else if (button.LED_Wrong.active)
					{
						KeypadButtons[i].color = "Red";

					}
					else
					{
						KeypadButtons[i].color = null;
					}
				}
				keypadState.isSolved = isSolved;
				keypadState.inFocus = isFocused;
				keypadState.onFront = onFront;
				keypadState.index = closestIndex;
				keypadState.topLeft = KeypadButtons[0];
				keypadState.topRight = KeypadButtons[1];
				keypadState.bottomLeft = KeypadButtons[2];
				keypadState.bottomRight = KeypadButtons[3];
				keypadState.name = "Keypad";

				bombState.modules.Add(keypadState);


			}

			else if (comp.ComponentType == ComponentTypeEnum.WhosOnFirst)
			{
				WhosOnFirstComponent whoFirst = (WhosOnFirstComponent) comp;
				int stage = whoFirst.CurrentStage;
				string[] buttonValues = new string[6];
				foreach (KeypadButton button in whoFirst.Buttons)
				{
					buttonValues[button.ButtonIndex] = button.Text.text;

				}
				string displayWord = whoFirst.DisplayText.text;

				WhosOnFirstModuleState whoFirstState = new WhosOnFirstModuleState();
				whoFirstState.stage = stage;
				whoFirstState.buttonWords = buttonValues;
				whoFirstState.displayWord = displayWord;
				whoFirstState.isSolved = isSolved;
				whoFirstState.inFocus = isFocused;
				whoFirstState.onFront = onFront;
				whoFirstState.index = closestIndex;
				whoFirstState.name = "WhosOnFirst";
				bombState.modules.Add(whoFirstState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.Memory)
			{
				MemoryComponent memory = (MemoryComponent) comp;
				MemoryModuleState memoryState = new MemoryModuleState();
				memoryState.stage = memory.CurrentStage;
				memoryState.displayNumber = int.Parse(memory.DisplayText.text);
				int[] buttonValues = new int[4];
				foreach (KeypadButton button in memory.Buttons)
				{
					buttonValues[button.ButtonIndex] = int.Parse(button.Text.text);

				}
				memoryState.buttonNumbers = buttonValues;
				memoryState.isSolved = isSolved;
				memoryState.inFocus = isFocused;
				memoryState.onFront = onFront;
				memoryState.index = closestIndex;
				memoryState.name = "Memory";
				bombState.modules.Add(memoryState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.Morse)
			{
				MorseCodeComponent morse = (MorseCodeComponent) comp;
				int currentFrequency = morse.CurrentFrequency;
				FieldInfo fieldInfo = typeof(MorseCodeComponent).GetField("chosenWord", BindingFlags.NonPublic | BindingFlags.Instance);
				string word = (string) fieldInfo.GetValue(morse);

				MorseCodeModuleState morseState = new MorseCodeModuleState();

				morseState.currentFrequency = currentFrequency;
				morseState.sequence = word;
				morseState.correctFrequency = morse.ChosenFrequency;
				morseState.isSolved = isSolved;
				morseState.inFocus = isFocused;
				morseState.onFront = onFront;
				morseState.index = closestIndex;
				morseState.name = "Morse";
				bombState.modules.Add(morseState);
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

				ComplicatedWiresModuleState compState = new ComplicatedWiresModuleState();
				compState.wires = new ComplicatedWireState[6];

				int indicesIndex = 0;
				foreach (VennSnippableWire wire in venn.ActiveWires)
				{
					compState.wires[indices[indicesIndex]] = new ComplicatedWireState();
					compState.wires[indices[indicesIndex]].hasStar = wire.HasSymbol;
					compState.wires[indices[indicesIndex]].isLedOn = wire.IsLEDOn;
					compState.wires[indices[indicesIndex]].isCut = wire.Snipped;
					compState.wires[indices[indicesIndex]].color = wire.Color.ToString();
					compState.wires[indices[indicesIndex]].position = indices[indicesIndex];

					indicesIndex++;
				}

				compState.isSolved = isSolved;
				compState.inFocus = isFocused;
				compState.onFront = onFront;
				compState.index = closestIndex;
				compState.name = "Venn";
				bombState.modules.Add(compState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.WireSequence)
			{
				WireSequenceComponent wireSeq = (WireSequenceComponent) comp;
				FieldInfo fieldInfo = typeof(WireSequenceComponent).GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance);

				WireSequenceModuleState wireSeqState = new WireSequenceModuleState();
				wireSeqState.panel = (int) fieldInfo.GetValue(wireSeq);
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
						wireSeqState.wires[i].color = config.Wire.GetColor().ToString();
						wireSeqState.wires[i].isCut = config.Wire.Snipped;
					}
				}

				wireSeqState.isSolved = isSolved;
				wireSeqState.inFocus = isFocused;
				wireSeqState.onFront = onFront;
				wireSeqState.index = closestIndex;
				wireSeqState.name = "WireSequence";
				bombState.modules.Add(wireSeqState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.Maze)
			{
				InvisibleWallsComponent invis = (InvisibleWallsComponent) comp;
				MazeModuleState mazeState = new MazeModuleState();
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

				mazeState.isSolved = isSolved;
				mazeState.inFocus = isFocused;
				mazeState.onFront = onFront;
				mazeState.index = closestIndex;
				mazeState.name = "Maze";
				bombState.modules.Add(mazeState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.Password)
			{
				PasswordComponent pass = (PasswordComponent) comp;
				FieldInfo fieldInfo = typeof(PasswordComponent).GetField("m_CurrentLayout", BindingFlags.NonPublic | BindingFlags.Instance);
				PasswordLayout layout = (PasswordLayout) fieldInfo.GetValue(pass);

				PasswordModuleState passState = new PasswordModuleState();


				passState.currentWord = layout.GetCurrentWord();
				passState.goalWord = pass.CorrectWord;

				passState.isSolved = isSolved;
				passState.inFocus = isFocused;
				passState.onFront = onFront;
				passState.index = closestIndex;
				passState.name = "Password";
				bombState.modules.Add(passState);
			}
		}
		readyToGive = true;
		return bombState;
	}
}

public class BaseModuleState
{
	public bool isSolved { get; set; }
	public bool inFocus { get; set; }
	public bool onFront { get; set; }
	public int index { get; set; }
	public string name { get; set; }
}

// --- Button Module ---
public class ButtonModuleState : BaseModuleState
{
	public string buttonColor { get; set; }
	public string buttonWord { get; set; }
	public bool isHeld { get; set; }
	public string stripColor { get; set; }
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

}

// --- Simon Says ---
public class SimonSaysModuleState : BaseModuleState
{
	public List<string> beepSequence { get; set; }
	public int solveProgress { get; set; }
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
}

public class ComplicatedWiresModuleState : BaseModuleState
{
	public ComplicatedWireState[] wires { get; set; }
}

public class WireSequenceModuleState : BaseModuleState
{
	public int panel { get; set; }
	public WireSequenceWireState[] wires { get; set; }
}

// --- Maze ---
public class MazeCoordinate
{
	public int row { get; set; }
	public int column { get; set; }
}

public class MazeModuleState : BaseModuleState
{
	public int numRows { get; set; }
	public int numColumns { get; set; }

	public MazeCoordinate trianglePosition { get; set; }
	public MazeCoordinate squarePosition { get; set; }
	public MazeCoordinate[] circlePositions { get; set; }
}

// --- Memory ---
public class MemoryModuleState : BaseModuleState
{
	public int displayNumber { get; set; }
	public int[] buttonNumbers { get; set; }
	public int stage { get; set; }
}

// --- Morse Code ---
public class MorseCodeModuleState : BaseModuleState
{
	public string sequence { get; set; }
	public float currentFrequency { get; set; }
	public float correctFrequency { get; set; }
}

// --- Password ---
public class PasswordModuleState : BaseModuleState
{
	public string currentWord { get; set; }
	public string goalWord { get; set; }

}

// --- Who’s On First ---
public class WhosOnFirstModuleState : BaseModuleState
{
	public string displayWord { get; set; }
	public string[] buttonWords { get; set; }
	public int stage { get; set; }
}

public class TimerModuleState
{
	public float secondsRemaining { get; set; }
	public bool onFront { get; set; }
	public int index { get; set; }
	public string name { get; set; }


}

// --- Needy Modules ---
public class DischargeModuleState : BaseModuleState
{
	public bool isBeingNeedy { get; set; }
	public int secondsUntilDischarge { get; set; }
}

public class KnobModuleState : BaseModuleState
{
	public bool isBeingNeedy { get; set; }
	public string knobPosition { get; set; }
	public Dictionary<int, bool> ledPosition { get; set; }
}

public class GasModuleState : BaseModuleState
{
	public bool isBeingNeedy { get; set; }
	public string message { get; set; }
	public int timer { get; set; }
}

public class BaseWidgetState
{
	public string position { get; set; }
	public string name { get; set; }
}

public class BatteryWidgetState : BaseWidgetState
{
	public int batteriesCount { get; set; }
	public string batteryType { get; set; }
}

public class IndicatorWidgetState : BaseWidgetState
{
	public bool lightActivated { get; set; }
	public string label { get; set; }
}

public class PortWidgetState : BaseWidgetState
{
	public List<string> portType { get; set; }
}

public class SerialNumberWidgetState : BaseWidgetState
{
	public string serialNumber { get; set; }
}

public class BombState
{
	public int seed { get; set; }
	public int maxStrikes { get; set; } = 3;
	public int currentStrikes { get; set; } = 0;
	public bool isDetonated { get; set; }
	public bool isSolved { get; set; }
	public bool isLightOn { get; set; }
	public TimerModuleState timerModule { get; set; }
	public List<BaseWidgetState> widgets { get; set; }
	public List<BaseModuleState> modules { get; set; }
	public List<string> strikes { get; set; }
}