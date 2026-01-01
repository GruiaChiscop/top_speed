using System.Collections.Generic;
using SharpDX.DirectInput;

namespace TopSpeed.Input
{
    internal readonly struct InputActionDefinition
    {
        public InputActionDefinition(InputAction action, string label)
        {
            Action = action;
            Label = label;
        }

        public InputAction Action { get; }
        public string Label { get; }
    }

    internal sealed class KeyMapManager
    {
        private readonly RaceInput _input;
        private readonly List<InputActionDefinition> _actions;
        private readonly Dictionary<InputAction, string> _labels;

        public KeyMapManager(RaceInput input)
        {
            _input = input;
            _actions = new List<InputActionDefinition>
            {
                new InputActionDefinition(InputAction.SteerLeft, "Steer left"),
                new InputActionDefinition(InputAction.SteerRight, "Steer right"),
                new InputActionDefinition(InputAction.Throttle, "Throttle"),
                new InputActionDefinition(InputAction.Brake, "Brake"),
                new InputActionDefinition(InputAction.GearUp, "Shift gear up"),
                new InputActionDefinition(InputAction.GearDown, "Shift gear down"),
                new InputActionDefinition(InputAction.Horn, "Use horn"),
                new InputActionDefinition(InputAction.RequestInfo, "Request position information"),
                new InputActionDefinition(InputAction.CurrentGear, "Current gear"),
                new InputActionDefinition(InputAction.CurrentLapNr, "Current lap number"),
                new InputActionDefinition(InputAction.CurrentRacePerc, "Current race percentage"),
                new InputActionDefinition(InputAction.CurrentLapPerc, "Current lap percentage"),
                new InputActionDefinition(InputAction.CurrentRaceTime, "Current race time"),
                new InputActionDefinition(InputAction.StartEngine, "Start the engine"),
                new InputActionDefinition(InputAction.ReportDistance, "Report distance"),
                new InputActionDefinition(InputAction.ReportSpeed, "Report speed"),
                new InputActionDefinition(InputAction.TrackName, "Report track name"),
                new InputActionDefinition(InputAction.Pause, "Pause")
            };
            _labels = new Dictionary<InputAction, string>();
            foreach (var action in _actions)
                _labels[action.Action] = action.Label;
        }

        public IReadOnlyList<InputActionDefinition> Actions => _actions;

        public string GetLabel(InputAction action)
        {
            return _labels.TryGetValue(action, out var label) ? label : "Action";
        }

        public Key GetKey(InputAction action)
        {
            return _input.GetKeyMapping(action);
        }

        public JoystickAxisOrButton GetAxis(InputAction action)
        {
            return _input.GetAxisMapping(action);
        }

        public void ApplyKeyMapping(InputAction action, Key key)
        {
            _input.ApplyKeyMapping(action, key);
        }

        public void ApplyAxisMapping(InputAction action, JoystickAxisOrButton axis)
        {
            _input.ApplyAxisMapping(action, axis);
        }

        public bool IsKeyInUse(Key key, InputAction ignore)
        {
            foreach (var action in _actions)
            {
                if (action.Action == ignore)
                    continue;
                if (GetKey(action.Action) == key)
                    return true;
            }
            return false;
        }

        public bool IsAxisInUse(JoystickAxisOrButton axis, InputAction ignore)
        {
            foreach (var action in _actions)
            {
                if (action.Action == ignore)
                    continue;
                if (GetAxis(action.Action) == axis)
                    return true;
            }
            return false;
        }

        public static bool IsReservedKey(Key key)
        {
            if (key >= Key.F1 && key <= Key.F8)
                return true;
            if (key == Key.F11)
                return true;
            if (key >= Key.D1 && key <= Key.D8)
                return true;
            return key == Key.LeftAlt;
        }

        public static string FormatKey(Key key)
        {
            if ((int)key <= 0)
                return "none";
            return key.ToString();
        }

        public static string FormatAxis(JoystickAxisOrButton axis)
        {
            return axis switch
            {
                JoystickAxisOrButton.AxisNone => "none",
                JoystickAxisOrButton.AxisXNeg => "X-",
                JoystickAxisOrButton.AxisXPos => "X+",
                JoystickAxisOrButton.AxisYNeg => "Y-",
                JoystickAxisOrButton.AxisYPos => "Y+",
                JoystickAxisOrButton.AxisZNeg => "Z-",
                JoystickAxisOrButton.AxisZPos => "Z+",
                JoystickAxisOrButton.AxisRxNeg => "Rx-",
                JoystickAxisOrButton.AxisRxPos => "Rx+",
                JoystickAxisOrButton.AxisRyNeg => "Ry-",
                JoystickAxisOrButton.AxisRyPos => "Ry+",
                JoystickAxisOrButton.AxisRzNeg => "Rz-",
                JoystickAxisOrButton.AxisRzPos => "Rz+",
                JoystickAxisOrButton.AxisSlider1Neg => "Slider1-",
                JoystickAxisOrButton.AxisSlider1Pos => "Slider1+",
                JoystickAxisOrButton.AxisSlider2Neg => "Slider2-",
                JoystickAxisOrButton.AxisSlider2Pos => "Slider2+",
                JoystickAxisOrButton.Button1 => "Button 1",
                JoystickAxisOrButton.Button2 => "Button 2",
                JoystickAxisOrButton.Button3 => "Button 3",
                JoystickAxisOrButton.Button4 => "Button 4",
                JoystickAxisOrButton.Button5 => "Button 5",
                JoystickAxisOrButton.Button6 => "Button 6",
                JoystickAxisOrButton.Button7 => "Button 7",
                JoystickAxisOrButton.Button8 => "Button 8",
                JoystickAxisOrButton.Button9 => "Button 9",
                JoystickAxisOrButton.Button10 => "Button 10",
                JoystickAxisOrButton.Button11 => "Button 11",
                JoystickAxisOrButton.Button12 => "Button 12",
                JoystickAxisOrButton.Button13 => "Button 13",
                JoystickAxisOrButton.Button14 => "Button 14",
                JoystickAxisOrButton.Button15 => "Button 15",
                JoystickAxisOrButton.Button16 => "Button 16",
                JoystickAxisOrButton.Pov1 => "POV 1 up",
                JoystickAxisOrButton.Pov2 => "POV 1 right",
                JoystickAxisOrButton.Pov3 => "POV 1 down",
                JoystickAxisOrButton.Pov4 => "POV 1 left",
                JoystickAxisOrButton.Pov5 => "POV 2 up",
                JoystickAxisOrButton.Pov6 => "POV 2 right",
                JoystickAxisOrButton.Pov7 => "POV 2 down",
                JoystickAxisOrButton.Pov8 => "POV 2 left",
                _ => axis.ToString()
            };
        }

        public string GetMappingInstruction(bool keyboard, InputAction action)
        {
            var label = GetLabel(action).ToLowerInvariant();
            return keyboard
                ? $"Press the new key for {label}."
                : $"Move or press the joystick control for {label}.";
        }
    }
}
