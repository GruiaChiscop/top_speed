using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TopSpeed.Server.Logging;

namespace TopSpeed.Server.Network
{
    internal sealed class UdpServerTransport : IDisposable
    {
        public const int MaxDatagramSize = 65507;
        private readonly Logger _logger;
        private UdpClient? _client;
        private CancellationTokenSource? _cts;
        private Task? _receiveTask;

        public event Action<IPEndPoint, byte[]>? PacketReceived;

        public UdpServerTransport(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Start(int port)
        {
            if (_client != null)
                return;
            var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.ReceiveBufferSize = 1024 * 1024;
            client.Client.SendBufferSize = 1024 * 1024;
            client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            _client = client;
            _cts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token));
            _logger.Info($"UDP transport listening on {port}.");
        }

        public void Stop()
        {
            if (_client == null)
                return;
            _cts?.Cancel();
            _client.Close();
            _client.Dispose();
            _client = null;
            _logger.Info("UDP transport stopped.");
        }

        public void Send(IPEndPoint endPoint, byte[] payload)
        {
            if (_client == null)
                return;
            if (payload.Length > MaxDatagramSize)
            {
                _logger.Warning($"UDP send dropped: payload too large ({payload.Length} bytes).");
                return;
            }
            try
            {
                _client.Send(payload, payload.Length, endPoint);
            }
            catch (Exception ex)
            {
                _logger.Warning($"UDP send failed: {ex.Message}");
            }
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            if (_client == null)
                return;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await _client.ReceiveAsync(token);
                    PacketReceived?.Invoke(result.RemoteEndPoint, result.Buffer);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"UDP receive failed: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
