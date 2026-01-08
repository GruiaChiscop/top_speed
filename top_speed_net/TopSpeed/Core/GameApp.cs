using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using TopSpeed.Windowing;

namespace TopSpeed.Core
{
    internal sealed class GameApp : IDisposable
    {
        private const int GameLoopIntervalMs = 8;
        private readonly GameWindow _window;
        private Game? _game;
        private readonly Stopwatch _stopwatch;
        private long _lastTicks;
        private Thread? _gameThread;
        private volatile bool _running;

        public GameApp()
        {
            _window = new GameWindow();
            _window.FormClosed += OnFormClosed;
            _window.Load += OnLoad;
            _stopwatch = new Stopwatch();
        }

        public void Run()
        {
            Application.Run(_window);
        }

        private void OnLoad(object? sender, EventArgs e)
        {
            _game = new Game(_window);
            _game.ExitRequested += async () =>
            {
                _game.FadeOutMenuMusic(500);
                await Task.Delay(500).ConfigureAwait(true);
                _window.Close();
            };
            _game.Initialize();
            _stopwatch.Start();
            _lastTicks = _stopwatch.ElapsedTicks;
            StartGameThread();
        }

        private void StartGameThread()
        {
            if (_gameThread != null)
                return;
            _running = true;
            _gameThread = new Thread(GameLoop)
            {
                IsBackground = true,
                Name = "GameLoop"
            };
            _gameThread.Start();
        }

        private void StopGameThread()
        {
            _running = false;
            if (_gameThread == null)
                return;
            if (_gameThread.IsAlive)
                _gameThread.Join(200);
            _gameThread = null;
        }

        private void GameLoop()
        {
            while (_running)
            {
                var game = _game;
                if (game != null && !game.IsModalInputActive)
                {
                    var now = _stopwatch.ElapsedTicks;
                    var deltaSeconds = (float)(now - _lastTicks) / Stopwatch.Frequency;
                    _lastTicks = now;
                    game.Update(deltaSeconds);
                }
                var intervalMs = game != null ? game.LoopIntervalMs : GameLoopIntervalMs;
                if (intervalMs <= 0)
                    intervalMs = GameLoopIntervalMs;
                Thread.Sleep(intervalMs);
            }
        }

        private void OnFormClosed(object? sender, FormClosedEventArgs e)
        {
            StopGameThread();
            _game?.Dispose();
            _game = null;
        }

        public void Dispose()
        {
            _window.Dispose();
            StopGameThread();
            _game?.Dispose();
        }
    }
}
