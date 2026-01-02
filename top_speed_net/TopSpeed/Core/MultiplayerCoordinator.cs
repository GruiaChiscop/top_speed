using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TopSpeed.Menu;
using TopSpeed.Network;
using TopSpeed.Protocol;
using TopSpeed.Input;
using TopSpeed.Speech;

namespace TopSpeed.Core
{
    internal sealed class MultiplayerCoordinator
    {
        private readonly MenuManager _menu;
        private readonly SpeechService _speech;
        private readonly RaceSettings _settings;
        private readonly MultiplayerConnector _connector;
        private readonly Action<string, string?, Action<string>, Action?> _beginTextInput;
        private readonly Action _saveSettings;
        private readonly Action _enterMenuState;
        private readonly Action<MultiplayerSession> _setSession;
        private readonly Action _clearSession;
        private readonly Action _resetPendingState;

        private Task<IReadOnlyList<ServerInfo>>? _discoveryTask;
        private CancellationTokenSource? _discoveryCts;
        private Task<ConnectResult>? _connectTask;
        private CancellationTokenSource? _connectCts;
        private string _pendingServerAddress = string.Empty;
        private int _pendingServerPort;
        private string _pendingCallSign = string.Empty;

        public MultiplayerCoordinator(
            MenuManager menu,
            SpeechService speech,
            RaceSettings settings,
            MultiplayerConnector connector,
            Action<string, string?, Action<string>, Action?> beginTextInput,
            Action saveSettings,
            Action enterMenuState,
            Action<MultiplayerSession> setSession,
            Action clearSession,
            Action resetPendingState)
        {
            _menu = menu ?? throw new ArgumentNullException(nameof(menu));
            _speech = speech ?? throw new ArgumentNullException(nameof(speech));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _connector = connector ?? throw new ArgumentNullException(nameof(connector));
            _beginTextInput = beginTextInput ?? throw new ArgumentNullException(nameof(beginTextInput));
            _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
            _enterMenuState = enterMenuState ?? throw new ArgumentNullException(nameof(enterMenuState));
            _setSession = setSession ?? throw new ArgumentNullException(nameof(setSession));
            _clearSession = clearSession ?? throw new ArgumentNullException(nameof(clearSession));
            _resetPendingState = resetPendingState ?? throw new ArgumentNullException(nameof(resetPendingState));
        }

        public bool UpdatePendingOperations()
        {
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

        public void StartServerDiscovery()
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

        public void BeginManualServerEntry()
        {
            _beginTextInput("Enter the server IP address or domain.", _settings.LastServerAddress, HandleServerAddressInput, null);
        }

        public void BeginServerPortEntry()
        {
            var current = _settings.ServerPort > 0 ? _settings.ServerPort.ToString() : string.Empty;
            _beginTextInput("Enter a custom server port, or leave empty for default.", current, HandleServerPortInput, null);
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
            items.Add(new MenuItem("Go back", MenuAction.Back));
            _menu.UpdateItems("multiplayer_servers", items);
        }

        private void SelectDiscoveredServer(ServerInfo server)
        {
            _pendingServerAddress = server.Address.ToString();
            _pendingServerPort = server.Port;
            BeginCallSignInput();
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
            _saveSettings();
            _pendingServerAddress = host;
            _pendingServerPort = overridePort ?? ResolveServerPort();
            BeginCallSignInput();
        }

        private void BeginCallSignInput()
        {
            _beginTextInput("Enter your call sign.", null, HandleCallSignInput, null);
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

        private void AttemptConnect(string host, int port, string callSign)
        {
            _speech.Speak("Attempting to connect, please wait...", interrupt: true);
            _clearSession();
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = new CancellationTokenSource();
            _connectTask = _connector.ConnectAsync(host, port, callSign, TimeSpan.FromSeconds(3), _connectCts.Token);
        }

        private void HandleConnectResult(ConnectResult result)
        {
            if (result.Success && result.Session != null)
            {
                var session = result.Session;
                _setSession(session);
                _resetPendingState();
                session.SendPlayerState(PlayerState.NotReady);

                var welcome = "You are now in the lobby.";
                if (!string.IsNullOrWhiteSpace(result.Motd))
                    welcome += $" Message of the day: {result.Motd}.";
                _speech.Speak(welcome, interrupt: true);
                _menu.ShowRoot("multiplayer_lobby");
                _enterMenuState();
                return;
            }

            _speech.Speak($"Failed to connect: {result.Message}", interrupt: true);
            _enterMenuState();
        }

        private void HandleServerPortInput(string text)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                _settings.ServerPort = 0;
                _saveSettings();
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
            _saveSettings();
            _speech.Speak($"Server port set to {port}.", interrupt: true);
        }

        private int ResolveServerPort()
        {
            return _settings.ServerPort > 0 ? _settings.ServerPort : ClientProtocol.DefaultServerPort;
        }
    }
}
