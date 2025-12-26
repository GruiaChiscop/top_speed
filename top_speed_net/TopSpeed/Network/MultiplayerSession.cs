using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TopSpeed.Network
{
    internal sealed class MultiplayerSession : IDisposable
    {
        private readonly UdpClient _client;
        private readonly IPEndPoint _serverEndPoint;
        private readonly CancellationTokenSource _cts;
        private readonly Task _keepAliveTask;

        public MultiplayerSession(UdpClient client, IPEndPoint serverEndPoint, byte playerNumber, string? motd, string? playerName)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _serverEndPoint = serverEndPoint ?? throw new ArgumentNullException(nameof(serverEndPoint));
            PlayerNumber = playerNumber;
            Motd = motd ?? string.Empty;
            PlayerName = playerName ?? string.Empty;
            _cts = new CancellationTokenSource();
            _keepAliveTask = Task.Run(KeepAliveLoop);
        }

        public IPAddress Address => _serverEndPoint.Address;
        public int Port => _serverEndPoint.Port;
        public byte PlayerNumber { get; }
        public string Motd { get; }
        public string PlayerName { get; }

        private async Task KeepAliveLoop()
        {
            var payload = new[] { ClientProtocol.Version, (byte)ClientCommand.KeepAlive };
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await _client.SendAsync(payload, payload.Length, _serverEndPoint);
                }
                catch
                {
                    // Keepalive failures shouldn't crash the session.
                }

                try
                {
                    await Task.Delay(1000, _cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _client.Close();
            _client.Dispose();
            _cts.Dispose();
        }
    }
}
