using System.IO;
using System;
using UnityEngine;
using System.Collections;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Assets.Scripts.Missions;
using System.Reflection;
using BombGame;
using System.Drawing;
using Assets.Scripts.Components.VennWire;
using System.Linq;
using TMPro;
using Events;
using Assets.Scripts.Rules;

public class GptntStates : MonoBehaviour 
{
	public string logFilePath;
	public long lastPosition = 0;
	private string line;
	public BombState bombState;
	float StartTime;
	TwitchBomb twitchBomb;
	public bool readyToGive = false;
	KMBombInfo bombInfo;

	public void Start()
	{
		bombInfo = GetComponent<KMBombInfo>();

		bombInfo.OnBombExploded += () =>
		{
			bombState.isDetonated = true;
			bombState.CurrentStrikes = twitchBomb.Bomb.NumStrikes;
			bombState.TimeRemaining = twitchBomb.Bomb.GetTimer().TimeRemaining;
			if (bombState.TimeRemaining < 0)
			{
				bombState.TimeRemaining = 0;
			}
			else
			{
				bombState.CurrentStrikes++;
			}

		};

		bombInfo.OnBombSolved += () =>
		{
			bombState.isSolved = true;
			bombState.CurrentStrikes = twitchBomb.Bomb.NumStrikes;
			bombState.TimeRemaining = twitchBomb.Bomb.GetTimer().TimeRemaining;
		};
	}

	public void ResetClock()
	{
		StartTime = Time.time;
	}

	public void GetInitialBombState()
	{
		ResetClock();
		bombState = new BombState();
		bombState.isSolved = false;
		bombState.isDetonated = false;
		bombState.Widgets = new List<BaseWidgetState> { };
		bombState.Modules = new List<BaseModuleState> { };
		twitchBomb = FindObjectOfType<TwitchBomb>();
		Bomb bomb = twitchBomb.Bomb;

		bombState.Seed = bomb.Seed;
		bombState.Timestamp = Time.time - StartTime;
		try
		{
			bombState.TimeRemaining = bomb.GetTimer().TimeRemaining;
		}
		catch (Exception ex)
		{
			GptntDebug.Log("Error setting TimeRemaining: " + ex);
		}

		try
		{
			bombState.CurrentStrikes = bomb.NumStrikes;
		}
		catch (Exception ex)
		{
			GptntDebug.Log("Error setting CurrentStrikes: " + ex);
		}

		try
		{
			bombState.MaxStrikes = bomb.NumStrikesToLose;
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
				widgetFace = "Bottom";
			}
			else if (widget.transform.parent.name.Equals("TopFaces"))
			{
				widgetFace = "Top";
			}
			else if (widget.transform.parent.name.Equals("RightFaces"))
			{
				widgetFace = "Right";
			}
			else
			{
				widgetFace = "Left";
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
						serialState.SerialNumber = serialString;
						serialState.Position = widgetFace;
						bombState.Widgets.Add(serialState);
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
							batteryState.BatteryType = "AA";
							batteryState.BatteriesCount = 2;
						}
						else if (batteryType.Equals(BatteryWidget.BatteryTypeEnum.DCell))
						{
							batteryState.BatteryType = "D";
							batteryState.BatteriesCount = 1;
						}
						batteryState.Position = widgetFace;
						bombState.Widgets.Add(batteryState);
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

						portWidgetState.PortTypes = ports;
						portWidgetState.Position = widgetFace;
						bombState.Widgets.Add(portWidgetState);
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
						indicatorWidgetState.Label = indicatorWidget.Label;
						indicatorWidgetState.LightActivated = indicatorWidget.On;
						indicatorWidgetState.Position = widgetFace;
						bombState.Widgets.Add(indicatorWidgetState);
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
			bool isSolved = comp.IsSolved;
			FieldInfo fieldInfo3 = typeof(BombComponent).GetField("isFocused", BindingFlags.NonPublic | BindingFlags.Instance);
			bool isFocused = (bool) fieldInfo3.GetValue(comp);



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


				simonState.BeepSequence = beepSequence;
				simonState.solveProgress = solveProgress;
				simonState.IsSolved = isSolved;
				simonState.InFocus = isFocused;
				bombState.Modules.Add(simonState);


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
				wireSetState.Wires = new WireSetWireState[6];
				// Just assign the spaces that contain wires
				int indicesIndex = 0;
				foreach (SnippableWire wire in wireset.wires)
				{
					wireSetState.Wires[indices[indicesIndex]] = new WireSetWireState();
					wireSetState.Wires[indices[indicesIndex]].Colour = wire.GetColor().ToString();
					wireSetState.Wires[indices[indicesIndex]].IsCut = wire.Snipped;
					wireSetState.Wires[indices[indicesIndex]].Position = indices[indicesIndex];
					indicesIndex++;
				}

				wireSetState.IsSolved = isSolved;
				wireSetState.InFocus = isFocused;
				bombState.Modules.Add(wireSetState);
			}
			else if (comp.ComponentType == ComponentTypeEnum.BigButton)
			{
				ButtonComponent button = (ButtonComponent) comp;
				string buttonColor = button.ButtonColor.ToString();
				string buttonMessage = button.ButtonInstruction.ToString();
				string stripColor = button.IndicatorColor.ToString();

				ButtonModuleState buttonState =	new ButtonModuleState();
				buttonState.ButtonColor = buttonColor;
				buttonState.ButtonWord = buttonMessage;
				buttonState.IsHeld = button.IsHolding;
				if (buttonState.IsHeld)
				{
					buttonState.StripColour = stripColor;
				}
				else
				{
					buttonState.StripColour = null;
				}
				buttonState.IsSolved = isSolved;
				buttonState.InFocus = isFocused;
				bombState.Modules.Add(buttonState);

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
					KeypadButtons[i].Symbol = button.GetValue();
					if (button.LED_Correct.active)
					{
						KeypadButtons[i].Colour = "Green";


					}
					else if (button.LED_Wrong.active)
					{
						KeypadButtons[i].Colour = "Red";

					}
					else
					{
						KeypadButtons[i].Colour = null;
					}
				}
				keypadState.IsSolved = isSolved;
				keypadState.InFocus = isFocused;
				keypadState.topLeft = KeypadButtons[0];
				keypadState.topRight = KeypadButtons[1];
				keypadState.bottomLeft = KeypadButtons[2];
				keypadState.bottomRight = KeypadButtons[3];

				GptntDebug.Log($"TOP LEFT: {keypadState.topLeft}");
				GptntDebug.Log($"TOP RIGHT: {keypadState.topRight}");
				GptntDebug.Log($"BOTTOM LEFT: {keypadState.bottomLeft}");
				GptntDebug.Log($"BOTTOM RIGHT: {keypadState.bottomRight}");


				bombState.Modules.Add(keypadState);


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
				whoFirstState.Stage = stage;
				whoFirstState.ButtonWords = buttonValues;
				whoFirstState.DisplayWord = displayWord;
				whoFirstState.IsSolved = isSolved;
				bombState.Modules.Add(whoFirstState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.Memory)
			{
				MemoryComponent memory = (MemoryComponent) comp;
				MemoryModuleState memoryState = new MemoryModuleState();
				memoryState.Stage = memory.CurrentStage;
				memoryState.DisplayNumber = int.Parse(memory.DisplayText.text);
				int[] buttonValues = new int[4];
				foreach (KeypadButton button in memory.Buttons)
				{
					buttonValues[button.ButtonIndex] = int.Parse(button.Text.text);

				}
				memoryState.ButtonNumbers = buttonValues;
				memoryState.IsSolved = isSolved;
				memoryState.InFocus = isFocused;
				bombState.Modules.Add(memoryState);
			}
			
			else if (comp.ComponentType == ComponentTypeEnum.Morse)
			{
				MorseCodeComponent morse = (MorseCodeComponent) comp;
				int currentFrequency = morse.CurrentFrequency;
				FieldInfo fieldInfo = typeof(MorseCodeComponent).GetField("chosenWord", BindingFlags.NonPublic | BindingFlags.Instance);
				string word = (string)fieldInfo.GetValue(morse);

				MorseCodeModuleState morseState = new MorseCodeModuleState();
				
				morseState.Frequency = currentFrequency;
				morseState.Sequence = word;
				morseState.IsSolved = isSolved;
				morseState.InFocus = isFocused;
				bombState.Modules.Add(morseState);
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
				compState.Wires = new ComplicatedWireState[6];

				int indicesIndex = 0;
				foreach (VennSnippableWire wire in venn.ActiveWires)
				{
					compState.Wires[indices[indicesIndex]] = new ComplicatedWireState();
					compState.Wires[indices[indicesIndex]].HasStar = wire.HasSymbol;
					compState.Wires[indices[indicesIndex]].IsLedOn = wire.IsLEDOn;
					compState.Wires[indices[indicesIndex]].IsCut = wire.Snipped;
					compState.Wires[indices[indicesIndex]].Colour = wire.Color.ToString();
					compState.Wires[indices[indicesIndex]].Position = indices[indicesIndex];

					indicesIndex++;
				}

				compState.IsSolved = isSolved;
				compState.InFocus = isFocused;
				bombState.Modules.Add(compState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.WireSequence)
			{
				WireSequenceComponent wireSeq = (WireSequenceComponent) comp;
				FieldInfo fieldInfo = typeof(WireSequenceComponent).GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance);

				WireSequenceModuleState wireSeqState = new WireSequenceModuleState();
				wireSeqState.Panel = (int) fieldInfo.GetValue(wireSeq);
				wireSeqState.Wires = new WireSequenceWireState[12];

				FieldInfo fieldInfo2 = typeof(WireSequenceComponent).GetField("wireSequence", BindingFlags.NonPublic | BindingFlags.Instance);
				List<WireSequenceComponent.WireConfiguration> configs = (List<WireSequenceComponent.WireConfiguration>) fieldInfo2.GetValue(wireSeq);
				for (int i = 0; i< 12; i++)
				{
					WireSequenceComponent.WireConfiguration config = configs[i];
					if (!config.NoWire)
					{
						wireSeqState.Wires[i] = new WireSequenceWireState();
						wireSeqState.Wires[i].StartPositionNumber = i;
						if (config.To == 0)
						{
							wireSeqState.Wires[i].EndPositionLetter = "A";

						}
						else if (config.To == 1)
						{
							wireSeqState.Wires[i].EndPositionLetter = "B";

						}
						else
						{
							wireSeqState.Wires[i].EndPositionLetter = "C";

						}
						wireSeqState.Wires[i].Colour = config.Wire.GetColor().ToString();
						wireSeqState.Wires[i].IsCut = config.Wire.Snipped;
					}
				}

				wireSeqState.IsSolved = isSolved;
				wireSeqState.InFocus = isFocused;
				bombState.Modules.Add(wireSeqState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.Maze)
			{
				InvisibleWallsComponent invis = (InvisibleWallsComponent) comp;
				MazeModuleState mazeState = new MazeModuleState();
				mazeState.NumColumns = 6;
				mazeState.NumRows = 6;


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

				mazeState.SquarePosition = new MazeCoordinate();
				mazeState.SquarePosition.Column = StartX;
				mazeState.SquarePosition.Row = startY;
				mazeState.TrianglePosition = new MazeCoordinate();
				mazeState.TrianglePosition.Column = goalX;
				mazeState.TrianglePosition.Row = goalY;
				mazeState.CirclePositions = new MazeCoordinate[2];
				mazeState.CirclePositions[0] = new MazeCoordinate();
				mazeState.CirclePositions[0].Column = circle1X;
				mazeState.CirclePositions[0].Row = circle1Y;
				mazeState.CirclePositions[1] = new MazeCoordinate();
				mazeState.CirclePositions[1].Column = circle2X;
				mazeState.CirclePositions[1].Row = circle2Y;

				mazeState.IsSolved = isSolved;
				mazeState.InFocus = isFocused;
				bombState.Modules.Add(mazeState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.Password)
			{
				PasswordComponent pass = (PasswordComponent) comp;
				FieldInfo fieldInfo = typeof(PasswordComponent).GetField("m_CurrentLayout", BindingFlags.NonPublic | BindingFlags.Instance);
				PasswordLayout layout = (PasswordLayout) fieldInfo.GetValue(pass);

				PasswordModuleState passState = new PasswordModuleState();


				passState.currentWord = layout.GetCurrentWord();
				passState.goalWord = pass.CorrectWord;

				passState.IsSolved = isSolved;
				passState.InFocus = isFocused;
				bombState.Modules.Add(passState);
			}
		}
		readyToGive = true;
		// TODO: readyToGive needs to be reset when the bomb is finished, ask Kareem about how to know if a module is infocus
	}


	public void UpdateBombState()
	{
		TwitchBomb twitchBomb = FindObjectOfType<TwitchBomb>();
		Bomb bomb = twitchBomb.Bomb;
		bombState.Modules = new List<BaseModuleState> { };
		bombState.Timestamp = Time.time - StartTime;
		try
		{
			bombState.TimeRemaining = bomb.GetTimer().TimeRemaining;
		}
		catch (Exception ex)
		{
			GptntDebug.Log("Error setting TimeRemaining: " + ex);
		}

		try
		{
			bombState.CurrentStrikes = bomb.NumStrikes;
		}
		catch (Exception ex)
		{
			GptntDebug.Log("Error setting CurrentStrikes: " + ex);
		}


		foreach (BombComponent comp in bomb.BombComponents)
		{
			GptntDebug.Log($"GO: {comp.transform.name}, PARENT GO: {comp.transform.parent.name} GRANDPARENT GO: {comp.transform.parent.name}");
			bool isSolved = comp.IsSolved;
			FieldInfo fieldInfo3 = typeof(BombComponent).GetField("isFocused", BindingFlags.NonPublic | BindingFlags.Instance);
			bool isFocused = (bool) fieldInfo3.GetValue(comp);



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


				simonState.BeepSequence = beepSequence;
				simonState.solveProgress = solveProgress;
				simonState.IsSolved = isSolved;
				simonState.InFocus = isFocused;
				bombState.Modules.Add(simonState);


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
				wireSetState.Wires = new WireSetWireState[6];
				// Just assign the spaces that contain wires
				int indicesIndex = 0;
				foreach (SnippableWire wire in wireset.wires)
				{
					wireSetState.Wires[indices[indicesIndex]] = new WireSetWireState();
					wireSetState.Wires[indices[indicesIndex]].Colour = wire.GetColor().ToString();
					wireSetState.Wires[indices[indicesIndex]].IsCut = wire.Snipped;
					wireSetState.Wires[indices[indicesIndex]].Position = indices[indicesIndex];
					indicesIndex++;
				}

				wireSetState.IsSolved = isSolved;
				wireSetState.InFocus = isFocused;
				bombState.Modules.Add(wireSetState);
			}
			else if (comp.ComponentType == ComponentTypeEnum.BigButton)
			{
				ButtonComponent button = (ButtonComponent) comp;
				string buttonColor = button.ButtonColor.ToString();
				string buttonMessage = button.ButtonInstruction.ToString();
				string stripColor = button.IndicatorColor.ToString();

				ButtonModuleState buttonState = new ButtonModuleState();
				buttonState.ButtonColor = buttonColor;
				buttonState.ButtonWord = buttonMessage;
				buttonState.IsHeld = button.IsHolding;
				if (buttonState.IsHeld)
				{
					buttonState.StripColour = stripColor;
				}
				else
				{
					buttonState.StripColour = null;
				}
				buttonState.IsSolved = isSolved;
				buttonState.InFocus = isFocused;
				bombState.Modules.Add(buttonState);

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
					KeypadButtons[i].Symbol = button.GetValue();
					if (button.LED_Correct.active)
					{
						KeypadButtons[i].Colour = "Green";


					}
					else if (button.LED_Wrong.active)
					{
						KeypadButtons[i].Colour = "Red";

					}
					else
					{
						KeypadButtons[i].Colour = null;
					}
				}
				keypadState.IsSolved = isSolved;
				keypadState.InFocus = isFocused;
				keypadState.topLeft = KeypadButtons[0];
				keypadState.topRight = KeypadButtons[1];
				keypadState.bottomLeft = KeypadButtons[2];
				keypadState.bottomRight = KeypadButtons[3];

				GptntDebug.Log($"TOP LEFT: {keypadState.topLeft}");
				GptntDebug.Log($"TOP RIGHT: {keypadState.topRight}");
				GptntDebug.Log($"BOTTOM LEFT: {keypadState.bottomLeft}");
				GptntDebug.Log($"BOTTOM RIGHT: {keypadState.bottomRight}");


				bombState.Modules.Add(keypadState);


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
				whoFirstState.Stage = stage;
				whoFirstState.ButtonWords = buttonValues;
				whoFirstState.DisplayWord = displayWord;
				whoFirstState.IsSolved = isSolved;
				bombState.Modules.Add(whoFirstState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.Memory)
			{
				MemoryComponent memory = (MemoryComponent) comp;
				MemoryModuleState memoryState = new MemoryModuleState();
				memoryState.Stage = memory.CurrentStage;
				memoryState.DisplayNumber = int.Parse(memory.DisplayText.text);
				int[] buttonValues = new int[4];
				foreach (KeypadButton button in memory.Buttons)
				{
					buttonValues[button.ButtonIndex] = int.Parse(button.Text.text);

				}
				memoryState.ButtonNumbers = buttonValues;
				memoryState.IsSolved = isSolved;
				memoryState.InFocus = isFocused;
				bombState.Modules.Add(memoryState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.Morse)
			{
				MorseCodeComponent morse = (MorseCodeComponent) comp;
				int currentFrequency = morse.CurrentFrequency;
				FieldInfo fieldInfo = typeof(MorseCodeComponent).GetField("chosenWord", BindingFlags.NonPublic | BindingFlags.Instance);
				string word = (string) fieldInfo.GetValue(morse);

				MorseCodeModuleState morseState = new MorseCodeModuleState();

				morseState.Frequency = currentFrequency;
				morseState.Sequence = word;
				morseState.IsSolved = isSolved;
				morseState.InFocus = isFocused;
				bombState.Modules.Add(morseState);
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
				compState.Wires = new ComplicatedWireState[6];

				int indicesIndex = 0;
				foreach (VennSnippableWire wire in venn.ActiveWires)
				{
					compState.Wires[indices[indicesIndex]] = new ComplicatedWireState();
					compState.Wires[indices[indicesIndex]].HasStar = wire.HasSymbol;
					compState.Wires[indices[indicesIndex]].IsLedOn = wire.IsLEDOn;
					compState.Wires[indices[indicesIndex]].IsCut = wire.Snipped;
					compState.Wires[indices[indicesIndex]].Colour = wire.Color.ToString();
					compState.Wires[indices[indicesIndex]].Position = indices[indicesIndex];

					indicesIndex++;
				}

				compState.IsSolved = isSolved;
				compState.InFocus = isFocused;
				bombState.Modules.Add(compState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.WireSequence)
			{
				WireSequenceComponent wireSeq = (WireSequenceComponent) comp;
				FieldInfo fieldInfo = typeof(WireSequenceComponent).GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance);

				WireSequenceModuleState wireSeqState = new WireSequenceModuleState();
				wireSeqState.Panel = (int) fieldInfo.GetValue(wireSeq);
				wireSeqState.Wires = new WireSequenceWireState[12];

				FieldInfo fieldInfo2 = typeof(WireSequenceComponent).GetField("wireSequence", BindingFlags.NonPublic | BindingFlags.Instance);
				List<WireSequenceComponent.WireConfiguration> configs = (List<WireSequenceComponent.WireConfiguration>) fieldInfo2.GetValue(wireSeq);
				for (int i = 0; i < 12; i++)
				{
					WireSequenceComponent.WireConfiguration config = configs[i];
					if (!config.NoWire)
					{
						wireSeqState.Wires[i] = new WireSequenceWireState();
						wireSeqState.Wires[i].StartPositionNumber = i;
						if (config.To == 0)
						{
							wireSeqState.Wires[i].EndPositionLetter = "A";

						}
						else if (config.To == 1)
						{
							wireSeqState.Wires[i].EndPositionLetter = "B";

						}
						else
						{
							wireSeqState.Wires[i].EndPositionLetter = "C";

						}
						wireSeqState.Wires[i].Colour = config.Wire.GetColor().ToString();
						wireSeqState.Wires[i].IsCut = config.Wire.Snipped;
					}
				}

				wireSeqState.IsSolved = isSolved;
				wireSeqState.InFocus = isFocused;
				bombState.Modules.Add(wireSeqState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.Maze)
			{
				InvisibleWallsComponent invis = (InvisibleWallsComponent) comp;
				MazeModuleState mazeState = new MazeModuleState();
				mazeState.NumColumns = 6;
				mazeState.NumRows = 6;


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

				mazeState.SquarePosition = new MazeCoordinate();
				mazeState.SquarePosition.Column = StartX;
				mazeState.SquarePosition.Row = startY;
				mazeState.TrianglePosition = new MazeCoordinate();
				mazeState.TrianglePosition.Column = goalX;
				mazeState.TrianglePosition.Row = goalY;
				mazeState.CirclePositions = new MazeCoordinate[2];
				mazeState.CirclePositions[0] = new MazeCoordinate();
				mazeState.CirclePositions[0].Column = circle1X;
				mazeState.CirclePositions[0].Row = circle1Y;
				mazeState.CirclePositions[1] = new MazeCoordinate();
				mazeState.CirclePositions[1].Column = circle2X;
				mazeState.CirclePositions[1].Row = circle2Y;

				mazeState.IsSolved = isSolved;
				mazeState.InFocus = isFocused;
				bombState.Modules.Add(mazeState);
			}

			else if (comp.ComponentType == ComponentTypeEnum.Password)
			{
				PasswordComponent pass = (PasswordComponent) comp;
				FieldInfo fieldInfo = typeof(PasswordComponent).GetField("m_CurrentLayout", BindingFlags.NonPublic | BindingFlags.Instance);
				PasswordLayout layout = (PasswordLayout) fieldInfo.GetValue(pass);

				PasswordModuleState passState = new PasswordModuleState();


				passState.currentWord = layout.GetCurrentWord();
				passState.goalWord = pass.CorrectWord;

				passState.IsSolved = isSolved;
				passState.InFocus = isFocused;
				bombState.Modules.Add(passState);
			}
		}
		readyToGive = true;
		// TODO: readyToGive needs to be reset when the bomb is finished, ask Kareem about how to know if a module is infocus
	}
}

public class BaseModuleState
{
	public bool IsSolved { get; set; }
	public bool InFocus { get; set; }
}

// --- Button Module ---
public class ButtonModuleState : BaseModuleState
{
	public string ButtonColor { get; set; }
	public string ButtonWord { get; set; }
	public bool IsHeld { get; set; }
	public string StripColour { get; set; }
}

// --- Keypad Module ---
public class KeyPadButtonState
{
	public string Symbol { get; set; }
	public string Colour { get; set; }
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
	public List<string> BeepSequence { get; set; }
	public int solveProgress { get; set; }
}

// --- Wire base + variants ---
public class BaseWire
{
	public bool IsCut { get; set; }
	public string Colour { get; set; }
}

public class WireSetWireState : BaseWire
{
	public int Position { get; set; }
}

public class ComplicatedWireState : BaseWire
{
	public int Position { get; set; }
	public bool IsLedOn { get; set; }
	public bool HasStar { get; set; }
}

public class WireSequenceWireState : BaseWire
{
	public int StartPositionNumber { get; set; }
	public string EndPositionLetter { get; set; }
}

// --- Wire Modules ---
public class WireSetModuleState : BaseModuleState
{
	public WireSetWireState[] Wires { get; set; }
}

public class ComplicatedWiresModuleState : BaseModuleState
{
	public ComplicatedWireState[] Wires { get; set; }
}

public class WireSequenceModuleState : BaseModuleState
{
	public int Panel { get; set; }
	public WireSequenceWireState[] Wires { get; set; }
}

// --- Maze ---
public class MazeCoordinate
{
	public int Row { get; set; }
	public int Column { get; set; }
}

public class MazeModuleState : BaseModuleState
{
	public int NumRows { get; set; }
	public int NumColumns { get; set; }

	public MazeCoordinate TrianglePosition { get; set; }
	public MazeCoordinate SquarePosition { get; set; }
	public MazeCoordinate[] CirclePositions { get; set; }
}

// --- Memory ---
public class MemoryModuleState : BaseModuleState
{
	public int DisplayNumber { get; set; }
	public int[] ButtonNumbers { get; set; }
	public int Stage { get; set; }
}

// --- Morse Code ---
public class MorseCodeModuleState : BaseModuleState
{
	public string Sequence { get; set; }
	public float Frequency { get; set; }
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
	public string DisplayWord { get; set; }
	public string[] ButtonWords { get; set; }
	public int Stage { get; set; }
}

// --- Needy Modules ---
public class DischargeModuleState : BaseModuleState
{
	public bool IsBeingNeedy { get; set; }
	public int SecondsUntilDischarge { get; set; }
}

public class KnobModuleState : BaseModuleState
{
	public bool IsBeingNeedy { get; set; }
	public string KnobPosition { get; set; }
	public Dictionary<int, bool> LedPosition { get; set; }
}

public class GasModuleState : BaseModuleState
{
	public bool IsBeingNeedy { get; set; }
	public string Message { get; set; }
	public int Timer { get; set; }
}

public class BaseWidgetState
{
	public string Position { get; set; }
}

public class BatteryWidgetState : BaseWidgetState
{
	public int BatteriesCount { get; set; }
	public string BatteryType { get; set; }
}

public class IndicatorWidgetState : BaseWidgetState
{
	public bool LightActivated { get; set; }
	public string Label { get; set; }
}

public class PortWidgetState : BaseWidgetState
{
	public List<string> PortTypes { get; set; }
}

public class SerialNumberWidgetState : BaseWidgetState
{
	public string SerialNumber { get; set; }
}

public class BombState
{
	public int Seed { get; set; }
	public float TimeRemaining { get; set; } = 300;
	public float Timestamp { get; set; }
	public int MaxStrikes { get; set; } = 3;
	public int CurrentStrikes { get; set; } = 0;
	public bool isDetonated { get; set; }
	public bool isSolved { get; set; }

	public List<BaseWidgetState> Widgets { get; set; }
	public List<BaseModuleState> Modules { get; set; }
}