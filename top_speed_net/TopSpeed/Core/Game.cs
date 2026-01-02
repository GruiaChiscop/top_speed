using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    internal sealed class Game : IDisposable
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

        private enum InputMappingMode
        {
            Keyboard,
            Joystick
        }


        private readonly struct TrackInfo
        {
            public TrackInfo(string key, string display)
            {
                Key = key;
                Display = display;
            }

            public string Key { get; }
            public string Display { get; }
        }

        private static readonly TrackInfo[] RaceTracks =
        {
            new TrackInfo("america", "America"),
            new TrackInfo("austria", "Austria"),
            new TrackInfo("belgium", "Belgium"),
            new TrackInfo("brazil", "Brazil"),
            new TrackInfo("china", "China"),
            new TrackInfo("england", "England"),
            new TrackInfo("finland", "Finland"),
            new TrackInfo("france", "France"),
            new TrackInfo("germany", "Germany"),
            new TrackInfo("ireland", "Ireland"),
            new TrackInfo("italy", "Italy"),
            new TrackInfo("netherlands", "Netherlands"),
            new TrackInfo("portugal", "Portugal"),
            new TrackInfo("russia", "Russia"),
            new TrackInfo("spain", "Spain"),
            new TrackInfo("sweden", "Sweden"),
            new TrackInfo("switserland", "Switserland")
        };

        private static readonly TrackInfo[] AdventureTracks =
        {
            new TrackInfo("advHills", "Rally hills"),
            new TrackInfo("advCoast", "French coast"),
            new TrackInfo("advCountry", "English country"),
            new TrackInfo("advAirport", "Ride airport"),
            new TrackInfo("advDesert", "Rally desert"),
            new TrackInfo("advRush", "Rush hour"),
            new TrackInfo("advEscape", "Polar escape")
        };

        private readonly GameWindow _window;
        private readonly AudioManager _audio;
        private readonly SpeechService _speech;
        private readonly InputManager _input;
        private readonly MenuManager _menu;
        private readonly RaceSettings _settings;
        private readonly RaceInput _raceInput;
        private readonly RaceSetup _setup;
        private readonly SettingsManager _settingsManager;
        private readonly MultiplayerConnector _connector = new MultiplayerConnector();
        private MultiplayerSession? _session;
        private bool _mappingActive;
        private InputMappingMode _mappingMode;
        private InputAction _mappingAction;
        private bool _mappingNeedsInstruction;
        private JoystickStateSnapshot _mappingPrevJoystick;
        private bool _mappingHasPrevJoystick;
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
        private Task<IReadOnlyList<ServerInfo>>? _discoveryTask;
        private CancellationTokenSource? _discoveryCts;
        private Task<ConnectResult>? _connectTask;
        private CancellationTokenSource? _connectCts;
        private ServerInfo? _pendingServer;
        private string _pendingServerAddress = string.Empty;
        private int _pendingServerPort;
        private string _pendingCallSign = string.Empty;
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
            RegisterMenus();
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

                    if (_mappingActive)
                    {
                        UpdateMapping();
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

        private void RegisterMenus()
        {
            var mainMenu = _menu.CreateMenu("main", new[]
            {
                new MenuItem("Quick start", MenuAction.QuickStart),
                new MenuItem("Time trial", MenuAction.None, nextMenuId: "time_trial_type", onActivate: () => PrepareMode(RaceMode.TimeTrial)),
                new MenuItem("Single race", MenuAction.None, nextMenuId: "single_race_type", onActivate: () => PrepareMode(RaceMode.SingleRace)),
                new MenuItem("MultiPlayer game", MenuAction.None, nextMenuId: "multiplayer"),
                new MenuItem("Options", MenuAction.None, nextMenuId: "options_main"),
                new MenuItem("Exit Game", MenuAction.Exit)
            }, "Main menu");
            mainMenu.MusicFile = "theme1.ogg";
            mainMenu.MusicVolume = _settings.MusicVolume;
            mainMenu.MusicVolumeChanged = SaveMusicVolume;
            _menu.Register(mainMenu);

            _menu.Register(BuildMultiplayerMenu());
            _menu.Register(BuildMultiplayerServersMenu());
            _menu.Register(BuildMultiplayerLobbyMenu());

            _menu.Register(BuildTrackTypeMenu("time_trial_type", RaceMode.TimeTrial));
            _menu.Register(BuildTrackTypeMenu("single_race_type", RaceMode.SingleRace));

            _menu.Register(BuildTrackMenu("time_trial_tracks_race", RaceMode.TimeTrial, TrackCategory.RaceTrack));
            _menu.Register(BuildTrackMenu("time_trial_tracks_adventure", RaceMode.TimeTrial, TrackCategory.StreetAdventure));
            _menu.Register(BuildTrackMenu("single_race_tracks_race", RaceMode.SingleRace, TrackCategory.RaceTrack));
            _menu.Register(BuildTrackMenu("single_race_tracks_adventure", RaceMode.SingleRace, TrackCategory.StreetAdventure));

            _menu.Register(BuildVehicleMenu("time_trial_vehicles", RaceMode.TimeTrial));
            _menu.Register(BuildVehicleMenu("single_race_vehicles", RaceMode.SingleRace));

            _menu.Register(BuildTransmissionMenu("time_trial_transmission", RaceMode.TimeTrial));
            _menu.Register(BuildTransmissionMenu("single_race_transmission", RaceMode.SingleRace));

            _menu.Register(BuildOptionsMenu());
            _menu.Register(BuildOptionsGameSettingsMenu());
            _menu.Register(BuildOptionsControlsMenu());
            _menu.Register(BuildOptionsControlsDeviceMenu());
            _menu.Register(BuildOptionsControlsKeyboardMenu());
            _menu.Register(BuildOptionsControlsJoystickMenu());
            _menu.Register(BuildOptionsRaceSettingsMenu());
            _menu.Register(BuildOptionsAutomaticInfoMenu());
            _menu.Register(BuildOptionsCopilotMenu());
            _menu.Register(BuildOptionsLapsMenu());
            _menu.Register(BuildOptionsComputersMenu());
            _menu.Register(BuildOptionsDifficultyMenu());
            _menu.Register(BuildOptionsRestoreMenu());
            _menu.Register(BuildOptionsServerSettingsMenu());
        }

        private MenuScreen BuildTrackTypeMenu(string id, RaceMode mode)
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Race track", MenuAction.None, nextMenuId: TrackMenuId(mode, TrackCategory.RaceTrack), onActivate: () => _setup.TrackCategory = TrackCategory.RaceTrack),
                new MenuItem("Street adventure", MenuAction.None, nextMenuId: TrackMenuId(mode, TrackCategory.StreetAdventure), onActivate: () => _setup.TrackCategory = TrackCategory.StreetAdventure),
                new MenuItem("Random", MenuAction.None, onActivate: () => PushRandomTrackType(mode)),
                BackItem()
            };
            var title = "Choose track type";
            return _menu.CreateMenu(id, items, title);
        }

        private MenuScreen BuildMultiplayerMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Join a game on the local network", MenuAction.None, onActivate: StartServerDiscovery, suppressPostActivateAnnouncement: true),
                new MenuItem("Enter the IP address or domain manually", MenuAction.None, onActivate: BeginManualServerEntry, suppressPostActivateAnnouncement: true),
                BackItem()
            };
            return _menu.CreateMenu("multiplayer", items, "Multiplayer");
        }

        private MenuScreen BuildMultiplayerServersMenu()
        {
            var items = new List<MenuItem>
            {
                BackItem()
            };
            return _menu.CreateMenu("multiplayer_servers", items, "Available servers");
        }

        private MenuScreen BuildMultiplayerLobbyMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Create a new game", MenuAction.None, onActivate: SpeakNotImplemented),
                new MenuItem("Join an existing game", MenuAction.None, onActivate: SpeakNotImplemented),
                new MenuItem("Who is online", MenuAction.None, onActivate: SpeakNotImplemented),
                new MenuItem("Options", MenuAction.None, nextMenuId: "options_main"),
                new MenuItem("Disconnect", MenuAction.None, onActivate: DisconnectFromServer)
            };
            return _menu.CreateMenu("multiplayer_lobby", items, string.Empty);
        }

        private MenuScreen BuildTrackMenu(string id, RaceMode mode, TrackCategory category)
        {
            var items = new List<MenuItem>();
            var trackList = category == TrackCategory.RaceTrack ? RaceTracks : AdventureTracks;
            var nextMenuId = VehicleMenuId(mode);

            foreach (var track in trackList)
            {
                var key = track.Key;
                items.Add(new MenuItem(track.Display, MenuAction.None, nextMenuId: nextMenuId, onActivate: () => SelectTrack(category, key)));
            }

            items.Add(new MenuItem("Random", MenuAction.None, nextMenuId: nextMenuId, onActivate: () => SelectRandomTrack(category)));
            items.Add(BackItem());
            var title = "Choose track";
            return _menu.CreateMenu(id, items, title);
        }

        private MenuScreen BuildVehicleMenu(string id, RaceMode mode)
        {
            var items = new List<MenuItem>();
            var nextMenuId = TransmissionMenuId(mode);

            for (var i = 0; i < VehicleCatalog.VehicleCount; i++)
            {
                var index = i;
                var name = VehicleCatalog.Vehicles[i].Name;
                items.Add(new MenuItem(name, MenuAction.None, nextMenuId: nextMenuId, onActivate: () => SelectVehicle(index)));
            }

            foreach (var file in GetCustomVehicleFiles())
            {
                var filePath = file;
                var fileName = Path.GetFileNameWithoutExtension(filePath) ?? "Custom vehicle";
                items.Add(new MenuItem(fileName, MenuAction.None, nextMenuId: nextMenuId, onActivate: () => SelectCustomVehicle(filePath)));
            }

            items.Add(new MenuItem("Random", MenuAction.None, nextMenuId: nextMenuId, onActivate: SelectRandomVehicle));
            items.Add(BackItem());
            var title = "Choose vehicle";
            return _menu.CreateMenu(id, items, title);
        }

        private MenuScreen BuildTransmissionMenu(string id, RaceMode mode)
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Automatic", MenuAction.None, onActivate: () => CompleteTransmission(mode, TransmissionMode.Automatic)),
                new MenuItem("Manual", MenuAction.None, onActivate: () => CompleteTransmission(mode, TransmissionMode.Manual)),
                new MenuItem("Random", MenuAction.None, onActivate: () => CompleteTransmission(mode, Algorithm.RandomInt(2) == 0 ? TransmissionMode.Automatic : TransmissionMode.Manual)),
                BackItem()
            };
            var title = "Choose transmission";
            return _menu.CreateMenu(id, items, title);
        }

        private MenuScreen BuildOptionsMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Game settings", MenuAction.None, nextMenuId: "options_game"),
                new MenuItem("Controls", MenuAction.None, nextMenuId: "options_controls"),
                new MenuItem("Race settings", MenuAction.None, nextMenuId: "options_race"),
                new MenuItem("Server settings", MenuAction.None, nextMenuId: "options_server"),
                new MenuItem("Restore default settings", MenuAction.None, nextMenuId: "options_restore"),
                BackItem()
            };
            return _menu.CreateMenu("options_main", items, "Options");
        }

        private MenuScreen BuildOptionsGameSettingsMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem(() => $"Include custom tracks in randomization: {FormatOnOff(_settings.RandomCustomTracks)}", MenuAction.None, onActivate: () => ToggleSetting(() => _settings.RandomCustomTracks = !_settings.RandomCustomTracks)),
                new MenuItem(() => $"Include custom vehicles in randomization: {FormatOnOff(_settings.RandomCustomVehicles)}", MenuAction.None, onActivate: () => ToggleSetting(() => _settings.RandomCustomVehicles = !_settings.RandomCustomVehicles)),
                new MenuItem(() => $"Enable Three-D sound: {FormatOnOff(_settings.ThreeDSound)}", MenuAction.None, onActivate: () => ToggleSetting(() => _settings.ThreeDSound = !_settings.ThreeDSound)),
                new MenuItem(() => $"Units: {UnitsLabel(_settings.Units)}", MenuAction.None, onActivate: () => ToggleSetting(() => _settings.Units = _settings.Units == UnitSystem.Metric ? UnitSystem.Imperial : UnitSystem.Metric)),
                BackItem()
            };
            return _menu.CreateMenu("options_game", items, "Game settings");    
        }

        private MenuScreen BuildOptionsServerSettingsMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem(() => $"Custom server port: {FormatServerPort(_settings.ServerPort)}", MenuAction.None, onActivate: BeginServerPortEntry),
                BackItem()
            };
            return _menu.CreateMenu("options_server", items, "Server settings");
        }

        private bool UpdateModalOperations()
        {
            if (_textInputActive)
            {
                UpdateTextInput();
                return true;
            }

            if (_connectTask != null)
            {
                if (!_connectTask.IsCompleted)
                    return true;
                var result = _connectTask.IsFaulted || _connectTask.IsCanceled
                    ? ConnectResult.CreateFail("Connection attempt failed.")
                    : _connectTask.GetAwaiter().GetResult();
                _connectTask = null;
                _connectCts?.Dispose();
                _connectCts = null;
                HandleConnectResult(result);
                return false;
            }

            if (_discoveryTask != null)
            {
                if (!_discoveryTask.IsCompleted)
                    return true;
                IReadOnlyList<ServerInfo> servers;
                if (_discoveryTask.IsFaulted || _discoveryTask.IsCanceled)
                    servers = Array.Empty<ServerInfo>();
                else
                    servers = _discoveryTask.GetAwaiter().GetResult();
                _discoveryTask = null;
                _discoveryCts?.Dispose();
                _discoveryCts = null;
                HandleDiscoveryResult(servers);
                return false;
            }

            return false;
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

        private void StartServerDiscovery()
        {
            if (_discoveryTask != null && !_discoveryTask.IsCompleted)
                return;

            _speech.Speak("Please wait. Scanning for servers on the local network.", interrupt: true);
            _discoveryCts?.Cancel();
            _discoveryCts?.Dispose();
            _discoveryCts = new CancellationTokenSource();
            _discoveryTask = Task.Run(async () =>
            {
                using var client = new DiscoveryClient();
                return await client.ScanAsync(ClientProtocol.DefaultDiscoveryPort, TimeSpan.FromSeconds(2), _discoveryCts.Token);
            }, _discoveryCts.Token);
        }

        private void HandleDiscoveryResult(IReadOnlyList<ServerInfo> servers)
        {
            if (servers == null || servers.Count == 0)
            {
                _speech.Speak("No servers were found on the local network. You can enter an address manually.", interrupt: true);
                return;
            }

            UpdateServerListMenu(servers);
            _menu.Push("multiplayer_servers");
        }

        private void UpdateServerListMenu(IReadOnlyList<ServerInfo> servers)
        {
            var items = new List<MenuItem>();
            foreach (var server in servers)
            {
                var info = server;
                var label = $"{info.Address}:{info.Port}";
                items.Add(new MenuItem(label, MenuAction.None, onActivate: () => SelectDiscoveredServer(info), suppressPostActivateAnnouncement: true));
            }
            items.Add(BackItem());
            _menu.UpdateItems("multiplayer_servers", items);
        }

        private void SelectDiscoveredServer(ServerInfo server)
        {
            _pendingServerAddress = server.Address.ToString();
            _pendingServerPort = server.Port;
            _pendingServer = server;
            BeginCallSignInput();
        }

        private void BeginManualServerEntry()
        {
            BeginTextInput("Enter the server IP address or domain.", _settings.LastServerAddress, HandleServerAddressInput);
        }

        private void SpeakNotImplemented()
        {
            _speech.Speak("Not implemented yet.", interrupt: true);
        }

        private void HandleServerAddressInput(string text)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                _speech.Speak("Please enter a server address.", interrupt: true);
                BeginManualServerEntry();
                return;
            }

            var host = trimmed;
            int? overridePort = null;
            var lastColon = trimmed.LastIndexOf(':');
            if (lastColon > 0 && lastColon < trimmed.Length - 1)
            {
                var portPart = trimmed.Substring(lastColon + 1);
                if (int.TryParse(portPart, out var parsedPort))
                {
                    host = trimmed.Substring(0, lastColon);
                    overridePort = parsedPort;
                }
            }

            _settings.LastServerAddress = host;
            SaveSettings();
            _pendingServerAddress = host;
            _pendingServerPort = overridePort ?? ResolveServerPort();
            BeginCallSignInput();
        }

        private void BeginCallSignInput()
        {
            BeginTextInput("Enter your call sign.", null, HandleCallSignInput);
        }

        private void HandleCallSignInput(string text)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                _speech.Speak("Call sign cannot be empty.", interrupt: true);
                BeginCallSignInput();
                return;
            }

            _pendingCallSign = trimmed;
            AttemptConnect(_pendingServerAddress, _pendingServerPort, _pendingCallSign);
        }

        private void DisconnectFromServer()
        {
            _multiplayerRace?.FinalizeLevelMultiplayer();
            _multiplayerRace?.Dispose();
            _multiplayerRace = null;

            _pendingMultiplayerTrack = null;
            _pendingMultiplayerTrackName = string.Empty;
            _pendingMultiplayerLaps = 0;
            _pendingMultiplayerStart = false;

            _session?.Dispose();
            _session = null;

            _state = AppState.Menu;
            _menu.ShowRoot("main");
            _menu.FadeInMenuMusic();
            _speech.Speak("Main menu", interrupt: true);
        }

        private void AttemptConnect(string host, int port, string callSign)
        {
            _speech.Speak("Attempting to connect, please wait...", interrupt: true);
            _session?.Dispose();
            _session = null;
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = new CancellationTokenSource();
            _connectTask = _connector.ConnectAsync(host, port, callSign, TimeSpan.FromSeconds(3), _connectCts.Token);
        }

        private void HandleConnectResult(ConnectResult result)
        {
            if (result.Success)
            {
                _session = result.Session;
                _pendingMultiplayerTrack = null;
                _pendingMultiplayerTrackName = string.Empty;
                _pendingMultiplayerLaps = 0;
                _pendingMultiplayerStart = false;
                _session?.SendPlayerState(PlayerState.NotReady);

                var welcome = "You are now in the lobby.";
                if (!string.IsNullOrWhiteSpace(result.Motd))
                    welcome += $" Message of the day: {result.Motd}.";
                _speech.Speak(welcome, interrupt: true);
                _menu.ShowRoot("multiplayer_lobby");
                _state = AppState.Menu;
                return;
            }

            _speech.Speak($"Failed to connect: {result.Message}", interrupt: true);
            _state = AppState.Menu;
                _menu.ShowRoot("main");
                _speech.Speak("Main menu", interrupt: true);
        }

        private void BeginServerPortEntry()
        {
            var current = _settings.ServerPort > 0 ? _settings.ServerPort.ToString() : string.Empty;
            BeginTextInput("Enter a custom server port, or leave empty for default.", current, HandleServerPortInput);
        }

        private void HandleServerPortInput(string text)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                _settings.ServerPort = 0;
                SaveSettings();
                _speech.Speak("Server port cleared. The default port will be used.", interrupt: true);
                return;
            }

            if (!int.TryParse(trimmed, out var port) || port < 1 || port > 65535)
            {
                _speech.Speak("Invalid port. Enter a number between 1 and 65535.", interrupt: true);
                BeginServerPortEntry();
                return;
            }

            _settings.ServerPort = port;
            SaveSettings();
            _speech.Speak($"Server port set to {port}.", interrupt: true);
        }

        private int ResolveServerPort()
        {
            return _settings.ServerPort > 0 ? _settings.ServerPort : ClientProtocol.DefaultServerPort;
        }

        private MenuScreen BuildOptionsControlsMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem(() => $"Select device: {DeviceLabel(_settings.DeviceMode)}", MenuAction.None, nextMenuId: "options_controls_device"),
                new MenuItem(() => $"Force feedback: {FormatOnOff(_settings.ForceFeedback)}", MenuAction.None, onActivate: () => ToggleSetting(() => _settings.ForceFeedback = !_settings.ForceFeedback)),
                new MenuItem("Map keyboard keys", MenuAction.None, nextMenuId: "options_controls_keyboard"),
                new MenuItem("Map joystick keys", MenuAction.None, nextMenuId: "options_controls_joystick"),
                BackItem()
            };
            return _menu.CreateMenu("options_controls", items, "Controls");
        }

        private MenuScreen BuildOptionsControlsDeviceMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Keyboard", MenuAction.Back, onActivate: () => SetDevice(InputDeviceMode.Keyboard)),
                new MenuItem("Joystick", MenuAction.Back, onActivate: () => SetDevice(InputDeviceMode.Joystick)),
                new MenuItem("Both", MenuAction.Back, onActivate: () => SetDevice(InputDeviceMode.Both)),
                BackItem()
            };
            return _menu.CreateMenu("options_controls_device", items, "Choose control device");
        }

        private MenuScreen BuildOptionsControlsKeyboardMenu()
        {
            var items = BuildMappingItems(InputMappingMode.Keyboard);
            return _menu.CreateMenu("options_controls_keyboard", items, "Map keyboard keys");
        }

        private MenuScreen BuildOptionsControlsJoystickMenu()
        {
            var items = BuildMappingItems(InputMappingMode.Joystick);
            return _menu.CreateMenu("options_controls_joystick", items, "Map joystick keys");
        }

        private List<MenuItem> BuildMappingItems(InputMappingMode mode)
        {
            var items = new List<MenuItem>();
            foreach (var action in _raceInput.KeyMap.Actions)
            {
                var definition = action;
                items.Add(new MenuItem(
                    () => $"{definition.Label}: {FormatMappingValue(definition.Action, mode)}",
                    MenuAction.None,
                    onActivate: () => BeginMapping(mode, definition.Action)));
            }
            items.Add(BackItem());
            return items;
        }

        private MenuScreen BuildOptionsRaceSettingsMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem(() => $"Copilot: {CopilotLabel(_settings.Copilot)}", MenuAction.None, nextMenuId: "options_race_copilot"),
                new MenuItem(() => $"Curve announcements: {CurveLabel(_settings.CurveAnnouncement)}", MenuAction.None, onActivate: ToggleCurveAnnouncements),
                new MenuItem(() => $"Automatic race information: {AutomaticInfoLabel(_settings.AutomaticInfo)}", MenuAction.None, nextMenuId: "options_race_info"),
                new MenuItem(() => $"Number of laps: {_settings.NrOfLaps}", MenuAction.None, nextMenuId: "options_race_laps"),
                new MenuItem(() => $"Number of computer players: {_settings.NrOfComputers}", MenuAction.None, nextMenuId: "options_race_computers"),
                new MenuItem(() => $"Single race difficulty: {DifficultyLabel(_settings.Difficulty)}", MenuAction.None, nextMenuId: "options_race_difficulty"),
                BackItem()
            };
            return _menu.CreateMenu("options_race", items, "Race settings");    
        }

        private MenuScreen BuildOptionsAutomaticInfoMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Off", MenuAction.Back, onActivate: () => UpdateSetting(() => _settings.AutomaticInfo = AutomaticInfoMode.Off)),
                new MenuItem("Laps only", MenuAction.Back, onActivate: () => UpdateSetting(() => _settings.AutomaticInfo = AutomaticInfoMode.LapsOnly)),
                new MenuItem("On", MenuAction.Back, onActivate: () => UpdateSetting(() => _settings.AutomaticInfo = AutomaticInfoMode.On)),
                BackItem()
            };
            return _menu.CreateMenu("options_race_info", items, "Automatic information");
        }

        private MenuScreen BuildOptionsCopilotMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Off", MenuAction.Back, onActivate: () => UpdateSetting(() => _settings.Copilot = CopilotMode.Off)),
                new MenuItem("Curves only", MenuAction.Back, onActivate: () => UpdateSetting(() => _settings.Copilot = CopilotMode.CurvesOnly)),
                new MenuItem("All", MenuAction.Back, onActivate: () => UpdateSetting(() => _settings.Copilot = CopilotMode.All)),
                BackItem()
            };
            return _menu.CreateMenu("options_race_copilot", items, "Copilot settings");
        }

        private MenuScreen BuildOptionsLapsMenu()
        {
            var items = new List<MenuItem>();
            for (var laps = 2; laps <= 20; laps++)
            {
                var value = laps;
                items.Add(new MenuItem(laps.ToString(), MenuAction.Back, onActivate: () => UpdateSetting(() => _settings.NrOfLaps = value)));
            }
            items.Add(BackItem());
            return _menu.CreateMenu("options_race_laps", items, "Choose lap count");
        }

        private MenuScreen BuildOptionsComputersMenu()
        {
            var items = new List<MenuItem>();
            for (var count = 1; count <= 7; count++)
            {
                var value = count;
                items.Add(new MenuItem(count.ToString(), MenuAction.Back, onActivate: () => UpdateSetting(() => _settings.NrOfComputers = value)));
            }
            items.Add(BackItem());
            return _menu.CreateMenu("options_race_computers", items, "Choose number of computer players");
        }

        private MenuScreen BuildOptionsDifficultyMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Easy", MenuAction.Back, onActivate: () => UpdateSetting(() => _settings.Difficulty = RaceDifficulty.Easy)),
                new MenuItem("Normal", MenuAction.Back, onActivate: () => UpdateSetting(() => _settings.Difficulty = RaceDifficulty.Normal)),
                new MenuItem("Hard", MenuAction.Back, onActivate: () => UpdateSetting(() => _settings.Difficulty = RaceDifficulty.Hard)),
                BackItem()
            };
            return _menu.CreateMenu("options_race_difficulty", items, "Choose difficulty");
        }

        private MenuScreen BuildOptionsRestoreMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Yes", MenuAction.Back, onActivate: RestoreDefaults),
                new MenuItem("No", MenuAction.Back),
                BackItem()
            };
            return _menu.CreateMenu("options_restore", items, "Restore defaults");
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

        private void UpdateMapping()
        {
            if (_mappingNeedsInstruction)
            {
                _mappingNeedsInstruction = false;
                var instruction = _raceInput.KeyMap.GetMappingInstruction(_mappingMode == InputMappingMode.Keyboard, _mappingAction);
                _speech.Speak(instruction, interrupt: true);
            }

            if (_input.WasPressed(Key.Escape))
            {
                _mappingActive = false;
                _speech.Speak("Mapping cancelled.", interrupt: true);
                return;
            }

            if (_mappingMode == InputMappingMode.Keyboard)
                TryCaptureKeyboardMapping();
            else
                TryCaptureJoystickMapping();
        }

        private void TryCaptureKeyboardMapping()
        {
            for (var i = 1; i < 256; i++)
            {
                var key = (Key)i;
                if (!_input.WasPressed(key))
                    continue;
                if (KeyMapManager.IsReservedKey(key))
                {
                    _speech.Speak("That key is reserved.", interrupt: true);
                    return;
                }
                if (_raceInput.KeyMap.IsKeyInUse(key, _mappingAction))
                {
                    _speech.Speak("That key is already in use.", interrupt: true);
                    return;
                }

                _raceInput.KeyMap.ApplyKeyMapping(_mappingAction, key);
                SaveSettings();
                _mappingActive = false;
                var label = _raceInput.KeyMap.GetLabel(_mappingAction);
                _speech.Speak($"{label} set to {KeyMapManager.FormatKey(key)}.", interrupt: true);
                return;
            }
        }

        private void TryCaptureJoystickMapping()
        {
            if (!_input.TryGetJoystickState(out var state))
            {
                _mappingActive = false;
                _speech.Speak("No joystick detected.", interrupt: true);
                return;
            }

            if (!_mappingHasPrevJoystick)
            {
                _mappingPrevJoystick = state;
                _mappingHasPrevJoystick = true;
                return;
            }

            var axis = FindTriggeredAxis(state, _mappingPrevJoystick);
            _mappingPrevJoystick = state;
            if (axis == JoystickAxisOrButton.AxisNone)
                return;
            if (_raceInput.KeyMap.IsAxisInUse(axis, _mappingAction))
            {
                _speech.Speak("That control is already in use.", interrupt: true);
                return;
            }

            _raceInput.KeyMap.ApplyAxisMapping(_mappingAction, axis);
            SaveSettings();
            _mappingActive = false;
            var label = _raceInput.KeyMap.GetLabel(_mappingAction);
            _speech.Speak($"{label} set to {KeyMapManager.FormatAxis(axis)}.", interrupt: true);
        }

        private JoystickAxisOrButton FindTriggeredAxis(JoystickStateSnapshot current, JoystickStateSnapshot previous)
        {
            for (var i = (int)JoystickAxisOrButton.AxisXNeg; i <= (int)JoystickAxisOrButton.Pov8; i++)
            {
                var axis = (JoystickAxisOrButton)i;
                if (IsAxisActive(axis, current) && !IsAxisActive(axis, previous))
                    return axis;
            }
            return JoystickAxisOrButton.AxisNone;
        }

        private bool IsAxisActive(JoystickAxisOrButton axis, JoystickStateSnapshot state)
        {
            var center = _settings.JoystickCenter;
            const int threshold = 50;
            switch (axis)
            {
                case JoystickAxisOrButton.AxisXNeg:
                    return state.X < center.X - threshold;
                case JoystickAxisOrButton.AxisXPos:
                    return state.X > center.X + threshold;
                case JoystickAxisOrButton.AxisYNeg:
                    return state.Y < center.Y - threshold;
                case JoystickAxisOrButton.AxisYPos:
                    return state.Y > center.Y + threshold;
                case JoystickAxisOrButton.AxisZNeg:
                    return state.Z < center.Z - threshold;
                case JoystickAxisOrButton.AxisZPos:
                    return state.Z > center.Z + threshold;
                case JoystickAxisOrButton.AxisRxNeg:
                    return state.Rx < center.Rx - threshold;
                case JoystickAxisOrButton.AxisRxPos:
                    return state.Rx > center.Rx + threshold;
                case JoystickAxisOrButton.AxisRyNeg:
                    return state.Ry < center.Ry - threshold;
                case JoystickAxisOrButton.AxisRyPos:
                    return state.Ry > center.Ry + threshold;
                case JoystickAxisOrButton.AxisRzNeg:
                    return state.Rz < center.Rz - threshold;
                case JoystickAxisOrButton.AxisRzPos:
                    return state.Rz > center.Rz + threshold;
                case JoystickAxisOrButton.AxisSlider1Neg:
                    return state.Slider1 < center.Slider1 - threshold;
                case JoystickAxisOrButton.AxisSlider1Pos:
                    return state.Slider1 > center.Slider1 + threshold;
                case JoystickAxisOrButton.AxisSlider2Neg:
                    return state.Slider2 < center.Slider2 - threshold;
                case JoystickAxisOrButton.AxisSlider2Pos:
                    return state.Slider2 > center.Slider2 + threshold;
                case JoystickAxisOrButton.Button1:
                    return state.B1;
                case JoystickAxisOrButton.Button2:
                    return state.B2;
                case JoystickAxisOrButton.Button3:
                    return state.B3;
                case JoystickAxisOrButton.Button4:
                    return state.B4;
                case JoystickAxisOrButton.Button5:
                    return state.B5;
                case JoystickAxisOrButton.Button6:
                    return state.B6;
                case JoystickAxisOrButton.Button7:
                    return state.B7;
                case JoystickAxisOrButton.Button8:
                    return state.B8;
                case JoystickAxisOrButton.Button9:
                    return state.B9;
                case JoystickAxisOrButton.Button10:
                    return state.B10;
                case JoystickAxisOrButton.Button11:
                    return state.B11;
                case JoystickAxisOrButton.Button12:
                    return state.B12;
                case JoystickAxisOrButton.Button13:
                    return state.B13;
                case JoystickAxisOrButton.Button14:
                    return state.B14;
                case JoystickAxisOrButton.Button15:
                    return state.B15;
                case JoystickAxisOrButton.Button16:
                    return state.B16;
                case JoystickAxisOrButton.Pov1:
                    return state.Pov1;
                case JoystickAxisOrButton.Pov2:
                    return state.Pov2;
                case JoystickAxisOrButton.Pov3:
                    return state.Pov3;
                case JoystickAxisOrButton.Pov4:
                    return state.Pov4;
                case JoystickAxisOrButton.Pov5:
                    return state.Pov5;
                case JoystickAxisOrButton.Pov6:
                    return state.Pov6;
                case JoystickAxisOrButton.Pov7:
                    return state.Pov7;
                case JoystickAxisOrButton.Pov8:
                    return state.Pov8;
                default:
                    return false;
            }
        }

        private string FormatMappingValue(InputAction action, InputMappingMode mode)
        {
            return mode == InputMappingMode.Keyboard
                ? KeyMapManager.FormatKey(_raceInput.KeyMap.GetKey(action))
                : KeyMapManager.FormatAxis(_raceInput.KeyMap.GetAxis(action));
        }

        private void BeginMapping(InputMappingMode mode, InputAction action)
        {
            if (mode == InputMappingMode.Joystick)
            {
                if (_input.VibrationDevice == null || !_input.VibrationDevice.IsAvailable)
                {
                    _speech.Speak("No joystick detected.", interrupt: true);
                    return;
                }
            }

            _mappingActive = true;
            _mappingMode = mode;
            _mappingAction = action;
            _mappingHasPrevJoystick = false;
            _mappingNeedsInstruction = true;
        }

        private void PrepareQuickStart()
        {
            PrepareMode(RaceMode.QuickStart);
            SelectRandomTrackAny(_settings.RandomCustomTracks);
            SelectRandomVehicle();
            _setup.Transmission = TransmissionMode.Automatic;
        }

        private void PrepareMode(RaceMode mode)
        {
            _setup.Mode = mode;
            _setup.ClearSelection();
        }

        private void SelectTrack(TrackCategory category, string trackKey)
        {
            _setup.TrackCategory = category;
            _setup.TrackNameOrFile = trackKey;
        }

        private void SelectRandomTrack(TrackCategory category)
        {
            SelectRandomTrack(category, _settings.RandomCustomTracks);
        }

        private void SelectRandomTrack(TrackCategory category, bool includeCustom)
        {
            _setup.TrackCategory = category;
            _setup.TrackNameOrFile = GetRandomTrack(category, includeCustom);
        }

        private void SelectRandomTrackAny(bool includeCustom)
        {
            var candidates = new List<(string Key, TrackCategory Category)>();
            candidates.AddRange(RaceTracks.Select(track => (track.Key, TrackCategory.RaceTrack)));
            candidates.AddRange(AdventureTracks.Select(track => (track.Key, TrackCategory.StreetAdventure)));
            if (includeCustom)
                candidates.AddRange(GetCustomTrackFiles().Select(file => (file, TrackCategory.RaceTrack)));

            if (candidates.Count == 0)
            {
                _setup.TrackCategory = TrackCategory.RaceTrack;
                _setup.TrackNameOrFile = RaceTracks[0].Key;
                return;
            }

            var pick = candidates[Algorithm.RandomInt(candidates.Count)];
            _setup.TrackCategory = pick.Category;
            _setup.TrackNameOrFile = pick.Key;
        }

        private void SelectVehicle(int index)
        {
            _setup.VehicleIndex = index;
            _setup.VehicleFile = null;
        }

        private void SelectCustomVehicle(string file)
        {
            _setup.VehicleIndex = null;
            _setup.VehicleFile = file;
        }

        private void SelectRandomVehicle()
        {
            var customFiles = _settings.RandomCustomVehicles ? GetCustomVehicleFiles().ToList() : new List<string>();
            var total = VehicleCatalog.VehicleCount + customFiles.Count;
            if (total <= 0)
            {
                SelectVehicle(0);
                return;
            }

            var roll = Algorithm.RandomInt(total);
            if (roll < VehicleCatalog.VehicleCount)
            {
                SelectVehicle(roll);
                return;
            }

            var customIndex = roll - VehicleCatalog.VehicleCount;
            if (customIndex >= 0 && customIndex < customFiles.Count)
                SelectCustomVehicle(customFiles[customIndex]);
            else
                SelectVehicle(0);
        }

        private void CompleteTransmission(RaceMode mode, TransmissionMode transmission)
        {
            _setup.Transmission = transmission;
            QueueRaceStart(mode);
        }

        private void QueueRaceStart(RaceMode mode)
        {
            _pendingRaceStart = true;
            _pendingMode = mode;
        }

        private void PushRandomTrackType(RaceMode mode)
        {
            var category = Algorithm.RandomInt(2) == 0 ? TrackCategory.RaceTrack : TrackCategory.StreetAdventure;
            _setup.TrackCategory = category;
            _menu.Push(TrackMenuId(mode, category));
        }

        private string GetRandomTrack(TrackCategory category, bool includeCustom)
        {
            var candidates = new List<string>();
            var source = category == TrackCategory.RaceTrack ? RaceTracks : AdventureTracks;
            candidates.AddRange(source.Select(t => t.Key));

            if (includeCustom)
                candidates.AddRange(GetCustomTrackFiles());

            if (candidates.Count == 0)
                return RaceTracks[0].Key;

            var index = Algorithm.RandomInt(candidates.Count);
            return candidates[index];
        }

        private IEnumerable<string> GetCustomTrackFiles()
        {
            var root = Path.Combine(AssetPaths.Root, "Tracks");
            if (!Directory.Exists(root))
                return Array.Empty<string>();
            return Directory.EnumerateFiles(root, "*.trk", SearchOption.TopDirectoryOnly);
        }

        private IEnumerable<string> GetCustomVehicleFiles()
        {
            var root = Path.Combine(AssetPaths.Root, "Vehicles");
            if (!Directory.Exists(root))
                return Array.Empty<string>();
            return Directory.EnumerateFiles(root, "*.vhc", SearchOption.TopDirectoryOnly);
        }

        private static string TrackMenuId(RaceMode mode, TrackCategory category)
        {
            var prefix = mode == RaceMode.TimeTrial ? "time_trial" : "single_race";
            return category == TrackCategory.RaceTrack ? $"{prefix}_tracks_race" : $"{prefix}_tracks_adventure";
        }

        private static string VehicleMenuId(RaceMode mode)
        {
            return mode == RaceMode.TimeTrial ? "time_trial_vehicles" : "single_race_vehicles";
        }

        private static string TransmissionMenuId(RaceMode mode)
        {
            return mode == RaceMode.TimeTrial ? "time_trial_transmission" : "single_race_transmission";
        }

        private static MenuItem BackItem()
        {
            return new MenuItem("Go back", MenuAction.Back);
        }

        private static string FormatOnOff(bool value) => value ? "on" : "off";

        private static string FormatServerPort(int port)
        {
            return port > 0 ? port.ToString() : $"default ({ClientProtocol.DefaultServerPort})";
        }

        private static string DeviceLabel(InputDeviceMode mode)
        {
            return mode switch
            {
                InputDeviceMode.Keyboard => "keyboard",
                InputDeviceMode.Joystick => "joystick",
                InputDeviceMode.Both => "both",
                _ => "keyboard"
            };
        }

        private static string CopilotLabel(CopilotMode mode)
        {
            return mode switch
            {
                CopilotMode.Off => "off",
                CopilotMode.CurvesOnly => "curves only",
                CopilotMode.All => "all",
                _ => "off"
            };
        }

        private static string CurveLabel(CurveAnnouncementMode mode)
        {
            return mode switch
            {
                CurveAnnouncementMode.FixedDistance => "fixed distance",        
                CurveAnnouncementMode.SpeedDependent => "speed dependent",      
                _ => "fixed distance"
            };
        }

        private static string AutomaticInfoLabel(AutomaticInfoMode mode)
        {
            return mode switch
            {
                AutomaticInfoMode.Off => "off",
                AutomaticInfoMode.LapsOnly => "laps only",
                AutomaticInfoMode.On => "on",
                _ => "on"
            };
        }

        private static string DifficultyLabel(RaceDifficulty difficulty)        
        {
            return difficulty switch
            {
                RaceDifficulty.Easy => "easy",
                RaceDifficulty.Normal => "normal",
                RaceDifficulty.Hard => "hard",
                _ => "easy"
            };
        }

        private static string UnitsLabel(UnitSystem units)
        {
            return units switch
            {
                UnitSystem.Metric => "metric",
                UnitSystem.Imperial => "imperial",
                _ => "metric"
            };
        }

        private static string ModeLabel(RaceMode mode)
        {
            return mode switch
            {
                RaceMode.QuickStart => "Quick start",
                RaceMode.TimeTrial => "Time trial",
                RaceMode.SingleRace => "Single race",
                _ => "Race"
            };
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
                ? RaceTracks[0].Key
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
    }
}
