using System;
using Assets.Scripts.Components.VennWire;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.Linq;
using log4net;

namespace TwitchPlaysAssembly
{
	public class BaseModuleState
	{ 
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
		}

		public virtual void UpdateAttributes() { }

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

	public class SolvableModuleState : BaseModuleState
	{
		public event Action OnStrike;
		public event Action OnPass;

		public bool isSolved { get; set; }
		public bool inFocus { get; set; }

		public SolvableModuleState(BombComponent comp) : base(comp)
		{
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

		public override void UpdateAttributes()
		{
			base.UpdateAttributes();
			FieldInfo fieldInfo = typeof(BombComponent).GetField("isFocused", BindingFlags.NonPublic | BindingFlags.Instance);
			inFocus = (bool) fieldInfo.GetValue(component);
		}
	}

	public interface IEmergingModule
	{
		bool isEmerged { get; }
	}

	// --- Button Module ---
	public class ButtonModuleState : SolvableModuleState
	{
		public string buttonColor { get; set; }
		public string buttonWord { get; set; }
		public bool isHeld { get; set; }
		public string stripColor { get; set; }

		public ButtonModuleState(BombComponent comp) : base(comp)
		{
			component = comp;
			SetAttributes();
		}

		public override void UpdateAttributes()
		{
			base.UpdateAttributes();
			SetAttributes();
		}

		private void SetAttributes()
		{
			ButtonComponent button = (ButtonComponent) component;

			buttonColor = button.ButtonColor.ToString().ToLower();
			buttonWord = button.ButtonInstruction.ToString();
			isHeld = button.IsHolding;
			stripColor = isHeld ? button.IndicatorColor.ToString().ToLower() : null;

			name = "BigButton";
		}
	}

	// --- Keypad Module ---
	public class KeyPadButtonState
	{
		public string symbol { get; set; }
		public string color { get; set; }
	}

	public class KeypadModuleState : SolvableModuleState
	{
		public KeyPadButtonState topLeft { get; set; }
		public KeyPadButtonState topRight { get; set; }
		public KeyPadButtonState bottomLeft { get; set; }
		public KeyPadButtonState bottomRight { get; set; }

		public KeypadModuleState(BombComponent comp) : base(comp)
		{
			component = comp;
			SetAttributes();
		}

		public override void UpdateAttributes()
		{
			base.UpdateAttributes();
			SetAttributes();
		}

		private void SetAttributes()
		{
			KeypadComponent keypad = (KeypadComponent) component;
			KeyPadButtonState[] buttonStates = new KeyPadButtonState[4];

			for (int i = 0; i < 4; i++)
			{
				KeypadButton button = keypad.buttons[i];
				string symbol = button.GetValue();
				if (symbol == "©") symbol = "copyright";
				else if (symbol == "★") symbol = "star";
				else if (symbol == "☆") symbol = "hollow-star";
				else if (symbol == "ټ") symbol = "pashto-teh";
				else if (symbol == "Җ") symbol = "zh";
				else if (symbol == "Ω") symbol = "omega";
				else if (symbol == "Ѭ") symbol = "ligature-iotated-e";
				else if (symbol == "Ѽ") symbol = "ot";
				else if (symbol == "ϗ") symbol = "kai";
				else if (symbol == "ϫ") symbol = "egyptian-kai";
				else if (symbol == "Ϭ") symbol = "lunate-sampi";
				else if (symbol == "Ϟ") symbol = "qoppa";
				else if (symbol == "Ѧ") symbol = "little-yus";
				else if (symbol == "ӕ") symbol = "ae";
				else if (symbol == "Ԇ") symbol = "ha-with-descender";
				else if (symbol == "Ӭ") symbol = "e-with-diaeresis";
				else if (symbol == "\u0488") symbol = "thousand-sign";
				else if (symbol == "Ҋ") symbol = "short-i";
				else if (symbol == "ѯ") symbol = "ksi";
				else if (symbol == "¿") symbol = "inverted-question";
				else if (symbol == "¶") symbol = "pilcrow";
				else if (symbol == "Ͼ") symbol = "lunate-epsilon";
				else if (symbol == "Ͽ") symbol = "reversed-lunate-epsilon";
				else if (symbol == "Ψ") symbol = "psi";
				else if (symbol == "Ѫ") symbol = "big-yus";
				else if (symbol == "Ҩ") symbol = "qa";
				else if (symbol == "҂") symbol = "titlo";
				else if (symbol == "Ϙ") symbol = "archaic-koppa";
				else if (symbol == "ζ") symbol = "zeta";
				else if (symbol == "ƛ") symbol = "lambda-bar";
				else if (symbol == "ѣ") symbol = "yat";
				buttonStates[i] = new KeyPadButtonState
				{
					symbol = symbol,
					color = button.LED_Correct.activeInHierarchy ? "green" :
							button.LED_Wrong.activeInHierarchy ? "red" : null
				};
			}

			topLeft = buttonStates[0];
			topRight = buttonStates[1];
			bottomLeft = buttonStates[2];
			bottomRight = buttonStates[3];

			name = "Keypad";
		}
	}

	// --- Simon Says ---
	public class SimonSaysModuleState : SolvableModuleState
	{
		public List<string> beepSequence { get; set; }
		public int solveProgress { get; set; }

		public SimonSaysModuleState(BombComponent comp) : base(comp)
		{
			component = comp;
			SetAttributes();

			SimonComponent simon = (SimonComponent) comp;
			comp.OnStrike += (_) =>
			{
				simon.PlaySequenceDelay = 1f;
				simon.StopAllCoroutines();
				simon.StartCoroutine("PlaySequence", simon.PlaySequenceDelay);
				return false;
			};
		}

		public override void UpdateAttributes()
		{
			base.UpdateAttributes();
			SetAttributes();
		}

		private void SetAttributes()
		{
			SimonComponent simon = (SimonComponent) component;

			FieldInfo sequenceField = typeof(SimonComponent).GetField("currentSequence", BindingFlags.NonPublic | BindingFlags.Instance);
			FieldInfo progressField = typeof(SimonComponent).GetField("lastIndex", BindingFlags.NonPublic | BindingFlags.Instance);

			int[] sequence = (int[]) sequenceField.GetValue(simon);
			int lastIndex = (int) progressField.GetValue(simon);

			List<string> sequenceNames = new List<string>();
			foreach (int beep in sequence)
			{
				switch (beep)
				{
					case 0: sequenceNames.Add("red"); break;
					case 1: sequenceNames.Add("blue"); break;
					case 2: sequenceNames.Add("green"); break;
					case 3: sequenceNames.Add("yellow"); break;
				}
			}

			beepSequence = sequenceNames;
			solveProgress = lastIndex;
			name = "Simon";
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
	public class WireSetModuleState : SolvableModuleState
	{
		public WireSetWireState[] wires { get; set; }

		public WireSetModuleState(BombComponent comp) : base(comp)
		{
			component = comp;
			SetAttributes();
		}

		public override void UpdateAttributes()
		{
			base.UpdateAttributes();
			SetAttributes();
		}

		private void SetAttributes()
		{
			WireSetComponent wireset = (WireSetComponent) component;

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
		}
	}

	public class ComplicatedWiresModuleState : SolvableModuleState
	{
		public ComplicatedWireState[] wires { get; set; }

		public ComplicatedWiresModuleState(BombComponent comp) : base(comp)
		{
			component = comp;
			SetAttributes();
		}

		public override void UpdateAttributes()
		{
			base.UpdateAttributes();
			SetAttributes();
		}

		private void SetAttributes()
		{
			VennWireComponent venn = (VennWireComponent) component;
			Selectable selectable = venn.GetComponent<Selectable>();

			List<int> indices = new List<int>();
			wires = new ComplicatedWireState[6];

			for (int i = 0; i < selectable.Children.Length; i++)
			{
				if (selectable.Children[i] != null)
				{
					string name = selectable.Children[i].name;
					int pos = int.Parse(name[name.Length - 1].ToString()) - 1;
					indices.Add(pos);
				}
			}

			int indicesIndex = 0;
			foreach (VennSnippableWire wire in venn.ActiveWires)
			{
				int position = indices[indicesIndex];
				wires[position] = new ComplicatedWireState
				{
					hasStar = wire.HasSymbol,
					isLedOn = wire.IsLEDOn,
					isCut = wire.Snipped,
					color = wire.Color.ToString().ToLower(),
					position = position
				};
				indicesIndex++;
			}

			name = "Venn";
		}
	}

	public class WireSequenceModuleState : SolvableModuleState, IEmergingModule
	{
		public int panel { get; set; }
		public WireSequenceWireState[] wires { get; set; }
		public bool isEmerged { get; set; }

		public WireSequenceModuleState(BombComponent comp) : base(comp)
		{
			component = comp;
			SetAttributes();
		}

		public override void UpdateAttributes()
		{
			base.UpdateAttributes();
			SetAttributes();
		}

		private void SetAttributes()
		{
			WireSequenceComponent wireSeq = (WireSequenceComponent) component;

			FieldInfo panelField = typeof(WireSequenceComponent).GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance);
			panel = (int) panelField.GetValue(wireSeq) + 1;

			FieldInfo sequenceField = typeof(WireSequenceComponent).GetField("wireSequence", BindingFlags.NonPublic | BindingFlags.Instance);
			var configs = (List<WireSequenceComponent.WireConfiguration>) sequenceField.GetValue(wireSeq);

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

			isEmerged = !wireSeq.IsChangingPage;
			name = "WireSequence";
		}
	}


	// --- Maze ---
	public class MazeCoordinate
	{
		public int row { get; set; }
		public int column { get; set; }
	}

	public class MazeModuleState : SolvableModuleState
	{
		public int numRows { get; set; } = 6;
		public int numColumns { get; set; } = 6;

		public MazeCoordinate trianglePosition { get; set; }
		public MazeCoordinate squarePosition { get; set; }
		public MazeCoordinate[] circlePositions { get; set; }

		public MazeModuleState(BombComponent comp) : base(comp)
		{
			component = comp;
			SetAttributes();
		}

		public override void UpdateAttributes()
		{
			base.UpdateAttributes();
			SetAttributes();
		}

		private void SetAttributes()
		{
			var invis = (InvisibleWallsComponent) component;

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
		}
	}

	// --- Memory ---
	public class MemoryModuleState : SolvableModuleState, IEmergingModule
	{
		public string displayNumber { get; set; }
		public string[] buttonNumbers { get; set; }
		public int stage { get; set; }
		public bool isEmerged { get; set; }

		public MemoryModuleState(BombComponent comp) : base(comp)
		{
			component = comp;
			SetAttributes();
		}

		public override void UpdateAttributes()
		{
			base.UpdateAttributes();
			SetAttributes();
		}

		private void SetAttributes()
		{
			MemoryComponent memory = (MemoryComponent) component;

			FieldInfo buttonsEmergedField = typeof(MemoryComponent).GetField("buttonsEmerged", BindingFlags.NonPublic | BindingFlags.Instance);
			isEmerged = (bool) buttonsEmergedField.GetValue(memory);

			stage = memory.CurrentStage + 1;
			displayNumber = memory.DisplayText.text;
			isEmerged &= !displayNumber.Equals(""); // Buttons must be emerged AND display must be populated

			name = "Memory";

			if (!isEmerged)
			{
				displayNumber = null;
				buttonNumbers = null;
				return;
			}

			string[] buttonValues = new string[4];
			foreach (KeypadButton button in memory.Buttons)
			{
				buttonValues[button.ButtonIndex] = button.Text.text;
			}
			buttonNumbers = buttonValues;
		}
	}

	// --- Morse Code ---
	public class MorseCodeModuleState : SolvableModuleState
	{
		public string sequence { get; set; }
		public float currentFrequency { get; set; }
		public float correctFrequency { get; set; }

		public MorseCodeModuleState(BombComponent comp) : base(comp)
		{
			component = comp;
			SetAttributes();
		}

		public override void UpdateAttributes()
		{
			base.UpdateAttributes();
			SetAttributes();
		}

		private void SetAttributes()
		{
			MorseCodeComponent morse = (MorseCodeComponent) component;
			FieldInfo fieldInfo = typeof(MorseCodeComponent).GetField("chosenWord", BindingFlags.NonPublic | BindingFlags.Instance);
			string word = (string) fieldInfo.GetValue(morse);

			sequence = word;
			currentFrequency = morse.CurrentFrequency;
			correctFrequency = morse.ChosenFrequency;
			name = "Morse";
		}
	}

	// --- Password ---
	public class PasswordModuleState : SolvableModuleState
	{
		public string currentWord { get; set; }
		public string goalWord { get; set; }

		public PasswordModuleState(BombComponent comp) : base(comp)
		{
			component = comp;
			SetAttributes();
		}

		public override void UpdateAttributes()
		{
			base.UpdateAttributes();
			SetAttributes();
		}

		private void SetAttributes()
		{
			PasswordComponent pass = (PasswordComponent) component;
			FieldInfo fieldInfo = typeof(PasswordComponent).GetField("m_CurrentLayout", BindingFlags.NonPublic | BindingFlags.Instance);
			PasswordLayout layout = (PasswordLayout) fieldInfo.GetValue(pass);

			currentWord = layout.GetCurrentWord();
			goalWord = pass.CorrectWord;

			name = "Password";
		}
	}

	// --- Who’s On First ---
	public class WhosOnFirstModuleState : SolvableModuleState, IEmergingModule
	{
		public string displayWord { get; set; }
		public string[] buttonWords { get; set; }
		public int stage { get; set; }
		public bool isEmerged { get; set; }

		public WhosOnFirstModuleState(BombComponent comp) : base(comp)
		{
			component = comp;
			SetAttributes();
		}

		public override void UpdateAttributes()
		{
			base.UpdateAttributes();
			SetAttributes();
		}

		private void SetAttributes()
		{
			WhosOnFirstComponent whoFirst = (WhosOnFirstComponent) component;

			stage = whoFirst.CurrentStage + 1;
			isEmerged = whoFirst.ButtonsEmerged;

			name = "WhosOnFirst";

			if (!isEmerged)
			{
				buttonWords = null;
				displayWord = null;
				return;
			}
			buttonWords = new string[6];
			foreach (KeypadButton button in whoFirst.Buttons)
			{
				buttonWords[button.ButtonIndex] = button.Text.text;
			}

			displayWord = whoFirst.DisplayText.text;
		}
	}


	public class TimerModuleState : BaseModuleState
	{
		public float secondsRemaining { get; set; }

		public TimerModuleState(BombComponent comp) : base(comp)
		{
			name = "Timer";
			var timer = (TimerComponent) comp;
			secondsRemaining = timer.TimeRemaining;
		}
	}

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
		public List<SolvableModuleState> modules { get; set; }
		public List<string> strikes { get; set; }
		[JsonIgnore] public bool isEmerging { get; set; }
	}
}