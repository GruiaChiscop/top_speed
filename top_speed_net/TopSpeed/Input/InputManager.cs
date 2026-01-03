using System;
using SharpDX;
using SharpDX.DirectInput;

namespace TopSpeed.Input
{
    internal sealed class InputManager : IDisposable
    {
        private const int JoystickRescanIntervalMs = 1000;
        private const int MenuBackThreshold = 50;
        private readonly DirectInput _directInput;
        private readonly Keyboard _keyboard;
        private readonly GamepadDevice _gamepad;
        private JoystickDevice? _joystick;
        private readonly InputState _current;
        private readonly InputState _previous;
        private readonly IntPtr _windowHandle;
        private int _lastJoystickScan;
        private bool _suspended;
        private bool _menuBackLatched;
        private bool _ignoreMenuBackUntilRelease;

        public InputState Current => _current;

        public InputManager(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
            _directInput = new DirectInput();
            _keyboard = new Keyboard(_directInput);
            _keyboard.Properties.BufferSize = 128;
            _keyboard.SetCooperativeLevel(windowHandle, CooperativeLevel.Foreground | CooperativeLevel.NonExclusive);
            _gamepad = new GamepadDevice();
            if (!_gamepad.IsAvailable)
                TryRescanJoystick(force: true);
            _current = new InputState();
            _previous = new InputState();
            TryAcquire();
        }

        public void Update()
        {
            _previous.CopyFrom(_current);
            _current.Clear();

            if (_suspended)
                return;

            if (!TryAcquire())
                return;

            var state = _keyboard.GetCurrentState();
            foreach (var key in state.PressedKeys)
            {
                _current.Set(key, true);
            }

            _gamepad.Update();
            if (!_gamepad.IsAvailable)
            {
                if (_joystick == null || !_joystick.IsAvailable)
                    TryRescanJoystick();
                _joystick?.Update();
            }
        }

        public bool IsDown(Key key) => _current.IsDown(key);

        public bool WasPressed(Key key) => _current.IsDown(key) && !_previous.IsDown(key);

        public bool IsAnyInputHeld()
        {
            if (_suspended)
                return false;

            UpdateMenuBackLatchImmediate();

            if (IsAnyKeyboardKeyHeld())
                return true;

            return IsAnyJoystickButtonHeld();
        }

        public bool IsAnyMenuInputHeld()
        {
            if (_suspended)
                return false;

            if (_ignoreMenuBackUntilRelease)
            {
                if (IsAnyKeyboardKeyHeld(ignoreModifiers: true, ignoreEscape: true))
                    return true;
                if (IsAnyJoystickButtonHeld(ignoreBack: true))
                    return true;
                if (!IsMenuBackHeldRaw())
                    _ignoreMenuBackUntilRelease = false;
                return false;
            }

            if (IsAnyKeyboardKeyHeld(ignoreModifiers: true))
                return true;

            return IsAnyJoystickButtonHeld();
        }

        public bool IsMenuBackHeld()
        {
            if (_suspended)
                return false;

            if (_ignoreMenuBackUntilRelease)
            {
                if (!IsMenuBackHeldRaw())
                    _ignoreMenuBackUntilRelease = false;
                return false;
            }

            return IsMenuBackHeldRaw();
        }

        public void LatchMenuBack()
        {
            _menuBackLatched = true;
        }

        public void IgnoreMenuBackUntilRelease()
        {
            _ignoreMenuBackUntilRelease = true;
        }

        public bool ShouldIgnoreMenuBack()
        {
            if (!_menuBackLatched)
                return false;
            if (IsMenuBackHeld())
                return true;
            _menuBackLatched = false;
            return false;
        }

        public bool TryGetJoystickState(out JoystickStateSnapshot state)        
        {
            var device = VibrationDevice;
            if (device != null && device.IsAvailable)
            {
                state = device.State;
                return true;
            }
            state = default;
            return false;
        }

        public void ResetState()
        {
            _current.Clear();
            _previous.Clear();
        }

        public IVibrationDevice? VibrationDevice => _gamepad.IsAvailable        
            ? _gamepad
            : (_joystick != null && _joystick.IsAvailable ? _joystick : null);  

        public void Suspend()
        {
            _suspended = true;
            try
            {
                _keyboard.Unacquire();
            }
            catch (SharpDXException)
            {
                // Ignore unacquire failures.
            }

            if (_joystick?.Device != null)
            {
                try
                {
                    _joystick.Device.Unacquire();
                }
                catch (SharpDXException)
                {
                    // Ignore unacquire failures.
                }
            }
        }

        public void Resume()
        {
            _suspended = false;
            TryAcquire();

            if (_joystick?.Device != null)
            {
                try
                {
                    _joystick.Device.Acquire();
                }
                catch (SharpDXException)
                {
                    // Ignore acquire failures.
                }
            }
        }

        private bool IsAnyKeyboardKeyHeld(bool ignoreModifiers = false, bool ignoreEscape = false)
        {
            try
            {
                _keyboard.Acquire();
                var state = _keyboard.GetCurrentState();
                if (!ignoreModifiers)
                {
                    if (!ignoreEscape)
                        return state.PressedKeys.Count > 0;
                    foreach (var key in state.PressedKeys)
                    {
                        if (key != Key.Escape)
                            return true;
                    }
                    return false;
                }

                foreach (var key in state.PressedKeys)
                {
                    if (ignoreEscape && key == Key.Escape)
                        continue;
                    if (key == Key.LeftControl || key == Key.RightControl ||
                        key == Key.LeftShift || key == Key.RightShift ||
                        key == Key.LeftAlt || key == Key.RightAlt)
                        continue;
                    return true;
                }

                return false;
            }
            catch (SharpDXException)
            {
                return false;
            }
        }

        private bool IsAnyJoystickButtonHeld(bool ignoreBack = false)
        {
            if (_gamepad.IsAvailable)
            {
                _gamepad.Update();
                return ignoreBack
                    ? HasAnyButtonDownExceptBack(_gamepad.State)
                    : _gamepad.State.HasAnyButtonDown();
            }

            if (_joystick == null || !_joystick.IsAvailable)
                TryRescanJoystick();

            if (_joystick == null || !_joystick.IsAvailable)
                return false;

            if (!_joystick.Update())
                return false;

            return ignoreBack
                ? HasAnyButtonDownExceptBack(_joystick.State)
                : _joystick.State.HasAnyButtonDown();
        }

        private bool IsMenuBackHeldRaw()
        {
            if (IsDown(Key.Escape))
                return true;

            if (TryGetJoystickState(out var state))
                return state.X < -MenuBackThreshold || state.Pov4;

            return false;
        }

        private static bool HasAnyButtonDownExceptBack(JoystickStateSnapshot state)
        {
            return state.B1 || state.B2 || state.B3 || state.B4 || state.B5 || state.B6 || state.B7 || state.B8 ||
                   state.B9 || state.B10 || state.B11 || state.B12 || state.B13 || state.B14 || state.B15 || state.B16 ||
                   state.Pov1 || state.Pov2 || state.Pov3 || state.Pov5 || state.Pov6 || state.Pov7 || state.Pov8;
        }

        private void UpdateMenuBackLatchImmediate()
        {
            if (!_menuBackLatched)
                return;
            if (!IsMenuBackHeldImmediate())
                _menuBackLatched = false;
        }

        private bool IsMenuBackHeldImmediate()
        {
            try
            {
                _keyboard.Acquire();
                var state = _keyboard.GetCurrentState();
                foreach (var key in state.PressedKeys)
                {
                    if (key == Key.Escape)
                        return true;
                }
            }
            catch (SharpDXException)
            {
            }

            if (_gamepad.IsAvailable)
            {
                _gamepad.Update();
                var state = _gamepad.State;
                return state.X < -MenuBackThreshold || state.Pov4;
            }

            if (_joystick == null || !_joystick.IsAvailable)
                TryRescanJoystick();

            if (_joystick == null || !_joystick.IsAvailable)
                return false;

            return _joystick.Update() && (_joystick.State.X < -MenuBackThreshold || _joystick.State.Pov4);
        }

        private bool TryAcquire()
        {
            try
            {
                _keyboard.Acquire();
                return true;
            }
            catch (SharpDXException)
            {
                return false;
            }
        }

        private void TryRescanJoystick(bool force = false)
        {
            var now = Environment.TickCount;
            if (!force && unchecked((uint)(now - _lastJoystickScan)) < (uint)JoystickRescanIntervalMs)
                return;
            _lastJoystickScan = now;

            _joystick?.Dispose();
            _joystick = new JoystickDevice(_directInput, _windowHandle);
            if (_joystick.IsAvailable)
                return;

            _joystick.Dispose();
            _joystick = null;
        }

        public void Dispose()
        {
            _keyboard.Unacquire();
            _keyboard.Dispose();
            _gamepad.Dispose();
            _joystick?.Dispose();
            _directInput.Dispose();
        }
    }
}
