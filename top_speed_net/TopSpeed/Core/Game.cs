using System;
using SharpDX.DirectInput;
using TopSpeed.Audio;
using TopSpeed.Common;
using TopSpeed.Data;
using TopSpeed.Input;
using TopSpeed.Menu;
using TopSpeed.Network;
using TopSpeed.Protocol;
using TopSpeed.Race;
using TopSpeed.Speech;
using TopSpeed.Windowing;

namespace TopSpeed.Core
{
    internal sealed class Game : IDisposable, IMenuActions
    {
        private enum AppState
        {
            Logo,
            Menu,
            TimeTrial,
            SingleRace,
            MultiplayerRace,
            Paused
        }

        private readonly GameWindow _window;
        private readonly AudioManager _audio;
        private readonly SpeechService _speech;
        private readonly InputManager _input;
        private readonly MenuManager _menu;
        private readonly RaceSettings _settings;
        private readonly RaceInput _raceInput;
        private readonly RaceSetup _setup;
        private readonly SettingsManager _settingsManager;
        private readonly RaceSelection _selection;
        private readonly MenuRegistry _menuRegistry;
        private readonly MultiplayerCoordinator _multiplayerCoordinator;
        private MultiplayerSession? _session;
        private readonly InputMappingHandler _inputMapping;
        private LogoScreen? _logo;
        private AppState _state;
        private AppState _pausedState;
        private bool _pendingRaceStart;
        private RaceMode _pendingMode;
        private bool _pauseKeyReleased = true;
        private LevelTimeTrial? _timeTrial;
        private LevelSingleRace? _singleRace;
        private LevelMultiplayer? _multiplayerRace;
        private bool _textInputActive;
        private Action<string>? _textInputHandler;
        private Action? _textInputCancelled;
        private TrackData? _pendingMultiplayerTrack;
        private string _pendingMultiplayerTrackName = string.Empty;
        private int _pendingMultiplayerLaps;
        private bool _pendingMultiplayerStart;

        public event Action? ExitRequested;

        public Game(GameWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _settingsManager = new SettingsManager();
            _settings = _settingsManager.Load();
            _audio = new AudioManager(_settings.ThreeDSound);
            _speech = new SpeechService();
            _input = new InputManager(_window.Handle);
            _raceInput = new RaceInput(_settings);
            _setup = new RaceSetup();
            _menu = new MenuManager(_audio, _speech);
            _selection = new RaceSelection(_setup, _settings);
            _menuRegistry = new MenuRegistry(_menu, _settings, _setup, _raceInput, _selection, this);
            _inputMapping = new InputMappingHandler(_input, _raceInput, _settings, _speech, SaveSettings);
            _multiplayerCoordinator = new MultiplayerCoordinator(
                _menu,
                _speech,
                _settings,
                new MultiplayerConnector(),
                BeginTextInput,
                SaveSettings,
                EnterMenuState,
                SetSession,
                ClearSession,
                ResetPendingMultiplayerState);
            _menuRegistry.RegisterAll();
        }

        public void Initialize()
        {
            _logo = new LogoScreen(_audio);
            _logo.Start();
            _state = AppState.Logo;
        }

        public void Update(float deltaSeconds)
        {
            _input.Update();
            if (_input.TryGetJoystickState(out var joystick))
                _raceInput.Run(_input.Current, joystick);
            else
                _raceInput.Run(_input.Current);

            switch (_state)
            {
                case AppState.Logo:
                    if (_logo == null || _logo.Update(_input, deltaSeconds))
                    {
                        _logo?.Dispose();
                        _logo = null;
                        _menu.ShowRoot("main");
                        _speech.Speak("Main menu", interrupt: true);
                        _state = AppState.Menu;
                    }
                    break;
                case AppState.Menu:
                    if (UpdateModalOperations())
                        break;

                    if (_session != null)
                    {
                        ProcessMultiplayerPackets();
                        if (_state != AppState.Menu)
                            break;
                    }

                    if (_inputMapping.IsActive)
                    {
                        _inputMapping.Update();
                        break;
                    }

                    var action = _menu.Update(_input);
                    HandleMenuAction(action);
                    break;
                case AppState.TimeTrial:
                    RunTimeTrial(deltaSeconds);
                    break;
                case AppState.SingleRace:
                    RunSingleRace(deltaSeconds);
                    break;
                case AppState.MultiplayerRace:
                    RunMultiplayerRace(deltaSeconds);
                    break;
                case AppState.Paused:
                    UpdatePaused();
                    break;
            }

            if (_pendingRaceStart)
            {
                _pendingRaceStart = false;
                StartRace(_pendingMode);
            }

            _audio.Update();
        }

        private void HandleMenuAction(MenuAction action)
        {
            switch (action)
            {
                case MenuAction.Exit:
                    ExitRequested?.Invoke();
                    break;
                case MenuAction.QuickStart:
                    PrepareQuickStart();
                    QueueRaceStart(RaceMode.QuickStart);
                    break;
                default:
                    break;
            }
        }

        private bool UpdateModalOperations()
        {
            if (_textInputActive)
            {
                UpdateTextInput();
                return true;
            }
            return _multiplayerCoordinator.UpdatePendingOperations();
        }

        private void UpdateTextInput()
        {
            if (!_window.TryConsumeTextInput(out var result))
                return;

            _textInputActive = false;
            if (result.Cancelled)
            {
                _textInputCancelled?.Invoke();
            }
            else
            {
                _textInputHandler?.Invoke(result.Text ?? string.Empty);
            }

            if (!_textInputActive)
                _input.Resume();
        }

        private void BeginTextInput(string prompt, string? initialValue, Action<string> onSubmit, Action? onCancel = null)
        {
            _textInputHandler = onSubmit;
            _textInputCancelled = onCancel;
            _textInputActive = true;
            _input.Suspend();
            _window.ShowTextInput(initialValue);
            _speech.Speak(prompt, interrupt: true);
        }

        private void EnterMenuState()
        {
            _state = AppState.Menu;
        }

        private void SetSession(MultiplayerSession session)
        {
            _session = session;
        }

        private void ClearSession()
        {
            _session?.Dispose();
            _session = null;
        }

        private void ResetPendingMultiplayerState()
        {
            _pendingMultiplayerTrack = null;
            _pendingMultiplayerTrackName = string.Empty;
            _pendingMultiplayerLaps = 0;
            _pendingMultiplayerStart = false;
        }

        private void DisconnectFromServer()
        {
            _multiplayerRace?.FinalizeLevelMultiplayer();
            _multiplayerRace?.Dispose();
            _multiplayerRace = null;

            ResetPendingMultiplayerState();
            ClearSession();
            _state = AppState.Menu;
            _menu.ShowRoot("main");
            _menu.FadeInMenuMusic();
            _speech.Speak("Main menu", interrupt: true);
        }

        private void RestoreDefaults()
        {
            _settings.RestoreDefaults();
            _raceInput.SetDevice(_settings.DeviceMode);
            SaveSettings();
            _speech.Speak("Defaults restored.", interrupt: true);
        }

        private void SetDevice(InputDeviceMode mode)
        {
            _settings.DeviceMode = mode;
            _raceInput.SetDevice(mode);
            SaveSettings();
        }

        private void ToggleCurveAnnouncements()
        {
            _settings.CurveAnnouncement = _settings.CurveAnnouncement == CurveAnnouncementMode.FixedDistance
                ? CurveAnnouncementMode.SpeedDependent
                : CurveAnnouncementMode.FixedDistance;
            SaveSettings();
        }

        private void ToggleSetting(Action update)
        {
            update();
            SaveSettings();
        }

        private void UpdateSetting(Action update)
        {
            update();
            SaveSettings();
        }

        private void PrepareQuickStart()
        {
            _setup.Mode = RaceMode.QuickStart;
            _setup.ClearSelection();
            _selection.SelectRandomTrackAny(_settings.RandomCustomTracks);
            _selection.SelectRandomVehicle();
            _setup.Transmission = TransmissionMode.Automatic;
        }

        private void QueueRaceStart(RaceMode mode)
        {
            _pendingRaceStart = true;
            _pendingMode = mode;
        }

        private void RunTimeTrial(float elapsed)
        {
            if (_timeTrial == null)
            {
                EndRace();
                return;
            }

            _timeTrial.Run(elapsed);
            if (_timeTrial.WantsPause)
                EnterPause(AppState.TimeTrial);
            if (_timeTrial.WantsExit || _input.WasPressed(SharpDX.DirectInput.Key.Escape))
                EndRace();
        }

        private void RunSingleRace(float elapsed)
        {
            if (_singleRace == null)
            {
                EndRace();
                return;
            }

            _singleRace.Run(elapsed);
            if (_singleRace.WantsPause)
                EnterPause(AppState.SingleRace);
            if (_singleRace.WantsExit || _input.WasPressed(SharpDX.DirectInput.Key.Escape))
                EndRace();
        }

        private void RunMultiplayerRace(float elapsed)
        {
            if (_multiplayerRace == null)
            {
                EndMultiplayerRace();
                return;
            }

            ProcessMultiplayerPackets();
            if (_multiplayerRace == null)
                return;
            _multiplayerRace.Run(elapsed);
            if (_multiplayerRace.WantsExit || _input.WasPressed(SharpDX.DirectInput.Key.Escape))
                EndMultiplayerRace();
        }

        private void ProcessMultiplayerPackets()
        {
            if (_session == null)
                return;

            while (_session.TryDequeuePacket(out var packet))
            {
                switch (packet.Command)
                {
                    case Command.PlayerJoined:
                        if (ClientPacketSerializer.TryReadPlayerJoined(packet.Payload, out var joined))
                        {
                            if (joined.PlayerNumber != _session.PlayerNumber)
                            {
                                var name = string.IsNullOrWhiteSpace(joined.Name)
                                    ? $"Player {joined.PlayerNumber + 1}"
                                    : joined.Name;
                                _speech.Speak($"{name} joined.", interrupt: true);
                            }
                        }
                        break;
                    case Command.LoadCustomTrack:
                        if (ClientPacketSerializer.TryReadLoadCustomTrack(packet.Payload, out var track))
                        {
                            var name = string.IsNullOrWhiteSpace(track.TrackName) ? "custom" : track.TrackName;
                            var userDefined = string.Equals(name, "custom", StringComparison.OrdinalIgnoreCase);
                            _pendingMultiplayerTrack = new TrackData(userDefined, track.TrackWeather, track.TrackAmbience, track.Definitions);
                            _pendingMultiplayerTrackName = name;
                            _pendingMultiplayerLaps = track.NrOfLaps;
                            if (_pendingMultiplayerStart)
                                StartMultiplayerRace();
                        }
                        break;
                    case Command.StartRace:
                        StartMultiplayerRace();
                        break;
                    case Command.PlayerData:
                        if (_multiplayerRace != null && ClientPacketSerializer.TryReadPlayerData(packet.Payload, out var playerData))
                            _multiplayerRace.ApplyRemoteData(playerData);
                        break;
                    case Command.PlayerBumped:
                        if (_multiplayerRace != null && ClientPacketSerializer.TryReadPlayerBumped(packet.Payload, out var bump))
                            _multiplayerRace.ApplyBump(bump);
                        break;
                    case Command.PlayerDisconnected:
                        if (_multiplayerRace != null && ClientPacketSerializer.TryReadPlayer(packet.Payload, out var disconnected))
                            _multiplayerRace.RemoveRemotePlayer(disconnected.PlayerNumber);
                        break;
                    case Command.StopRace:
                    case Command.RaceAborted:
                        if (_state == AppState.MultiplayerRace)
                            EndMultiplayerRace();
                        break;
                }
            }
        }

        private void StartMultiplayerRace()
        {
            if (_session == null)
                return;
            if (_multiplayerRace != null)
                return;
            if (_pendingMultiplayerTrack == null)
            {
                _pendingMultiplayerStart = true;
                return;
            }

            _pendingMultiplayerStart = false;
            FadeOutMenuMusic();
            var trackName = string.IsNullOrWhiteSpace(_pendingMultiplayerTrackName) ? "custom" : _pendingMultiplayerTrackName;
            var laps = _pendingMultiplayerLaps > 0 ? _pendingMultiplayerLaps : _settings.NrOfLaps;
            var vehicleIndex = 0;
            var automatic = true;

            _multiplayerRace?.FinalizeLevelMultiplayer();
            _multiplayerRace?.Dispose();
            _multiplayerRace = new LevelMultiplayer(
                _audio,
                _speech,
                _settings,
                _raceInput,
                _pendingMultiplayerTrack!,
                trackName,
                automatic,
                laps,
                vehicleIndex,
                null,
                _input.VibrationDevice,
                _session,
                _session.PlayerId,
                _session.PlayerNumber);
            _multiplayerRace.Initialize();
            _state = AppState.MultiplayerRace;
        }

        private void EndMultiplayerRace()
        {
            _multiplayerRace?.FinalizeLevelMultiplayer();
            _multiplayerRace?.Dispose();
            _multiplayerRace = null;

            if (_session != null)
            {
                _session.SendPlayerState(PlayerState.NotReady);
                _state = AppState.Menu;
                _menu.ShowRoot("multiplayer_lobby");
            }
            else
            {
                _state = AppState.Menu;
                _menu.ShowRoot("main");
                _menu.FadeInMenuMusic();
                _speech.Speak("Main menu", interrupt: true);
            }
        }

        private void UpdatePaused()
        {
            if (!_raceInput.GetPause() && !_pauseKeyReleased)
            {
                _pauseKeyReleased = true;
                return;
            }

            if (_raceInput.GetPause() && _pauseKeyReleased)
            {
                _pauseKeyReleased = false;
                switch (_pausedState)
                {
                    case AppState.TimeTrial:
                        _timeTrial?.Unpause();
                        _timeTrial?.StopStopwatchDiff();
                        _state = AppState.TimeTrial;
                        break;
                    case AppState.SingleRace:
                        _singleRace?.Unpause();
                        _singleRace?.StopStopwatchDiff();
                        _state = AppState.SingleRace;
                        break;
                }
            }
        }

        private void EnterPause(AppState state)
        {
            _pausedState = state;
            _pauseKeyReleased = false;
            switch (_pausedState)
            {
                case AppState.TimeTrial:
                    _timeTrial?.StartStopwatchDiff();
                    _timeTrial?.Pause();
                    _state = AppState.Paused;
                    break;
                case AppState.SingleRace:
                    _singleRace?.StartStopwatchDiff();
                    _singleRace?.Pause();
                    _state = AppState.Paused;
                    break;
            }
        }

        private void StartRace(RaceMode mode)
        {
            FadeOutMenuMusic();
            var track = string.IsNullOrWhiteSpace(_setup.TrackNameOrFile)
                ? TrackList.RaceTracks[0].Key
                : _setup.TrackNameOrFile!;
            var vehicleIndex = _setup.VehicleIndex ?? 0;
            var vehicleFile = _setup.VehicleFile;
            var automatic = _setup.Transmission == TransmissionMode.Automatic;

            switch (mode)
            {
                case RaceMode.TimeTrial:
                    _timeTrial?.FinalizeLevelTimeTrial();
                    _timeTrial?.Dispose();
                    _timeTrial = new LevelTimeTrial(_audio, _speech, _settings, _raceInput, track, automatic, _settings.NrOfLaps, vehicleIndex, vehicleFile, _input.VibrationDevice);
                    _timeTrial.Initialize();
                    _state = AppState.TimeTrial;
                    _speech.Speak("Time trial.", interrupt: true);
                    break;
                case RaceMode.QuickStart:
                case RaceMode.SingleRace:
                    _singleRace?.FinalizeLevelSingleRace();
                    _singleRace?.Dispose();
                    _singleRace = new LevelSingleRace(_audio, _speech, _settings, _raceInput, track, automatic, _settings.NrOfLaps, vehicleIndex, vehicleFile, _input.VibrationDevice);
                    _singleRace.Initialize(Algorithm.RandomInt(_settings.NrOfComputers + 1));
                    _state = AppState.SingleRace;
                    _speech.Speak(mode == RaceMode.QuickStart ? "Quick start." : "Single race.", interrupt: true);
                    break;
            }
        }

        private void EndRace()
        {
            _timeTrial?.FinalizeLevelTimeTrial();
            _timeTrial?.Dispose();
            _timeTrial = null;

            _singleRace?.FinalizeLevelSingleRace();
            _singleRace?.Dispose();
            _singleRace = null;

            _state = AppState.Menu;
            _menu.ShowRoot("main");
            _menu.FadeInMenuMusic();
            _speech.Speak("Main menu", interrupt: true);
        }

        public void Dispose()
        {
            _logo?.Dispose();
            _menu.Dispose();
            _input.Dispose();
            _session?.Dispose();
            _speech.Dispose();
            _audio.Dispose();
        }

        public void FadeOutMenuMusic()
        {
            _menu.FadeOutMenuMusic();
        }

        private void SaveSettings()
        {
            _settingsManager.Save(_settings);
        }

        private void SaveMusicVolume(float volume)
        {
            _settings.MusicVolume = volume;
            SaveSettings();
        }

        void IMenuActions.SaveMusicVolume(float volume) => SaveMusicVolume(volume);
        void IMenuActions.QueueRaceStart(RaceMode mode) => QueueRaceStart(mode);
        void IMenuActions.StartServerDiscovery() => _multiplayerCoordinator.StartServerDiscovery();
        void IMenuActions.BeginManualServerEntry() => _multiplayerCoordinator.BeginManualServerEntry();
        void IMenuActions.DisconnectFromServer() => DisconnectFromServer();
        void IMenuActions.SpeakNotImplemented() => _speech.Speak("Not implemented yet.", interrupt: true);
        void IMenuActions.BeginServerPortEntry() => _multiplayerCoordinator.BeginServerPortEntry();
        void IMenuActions.RestoreDefaults() => RestoreDefaults();
        void IMenuActions.SetDevice(InputDeviceMode mode) => SetDevice(mode);
        void IMenuActions.ToggleCurveAnnouncements() => ToggleCurveAnnouncements();
        void IMenuActions.ToggleSetting(Action update) => ToggleSetting(update);
        void IMenuActions.UpdateSetting(Action update) => UpdateSetting(update);
        void IMenuActions.BeginMapping(InputMappingMode mode, InputAction action) => _inputMapping.BeginMapping(mode, action);
        string IMenuActions.FormatMappingValue(InputAction action, InputMappingMode mode) => _inputMapping.FormatMappingValue(action, mode);
    }
}
