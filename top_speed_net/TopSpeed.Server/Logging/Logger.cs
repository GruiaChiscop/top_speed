using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TopSpeed.Server.Logging
{
    internal sealed class Logger : IDisposable
    {
        private readonly LogLevel _enabledLevels;
        private readonly object _lock = new object();
        private readonly StreamWriter _writer;

        public Logger(LogLevel enabledLevels, string logFilePath)
        {
            _enabledLevels = enabledLevels;
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath) ?? ".");
            _writer = new StreamWriter(logFilePath, append: false, Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        public void Debug(string message) => Log(LogLevel.Debug, message);
        public void Info(string message) => Log(LogLevel.Info, message);
        public void Warning(string message) => Log(LogLevel.Warning, message);
        public void Error(string message) => Log(LogLevel.Error, message);

        public void Log(LogLevel level, string message)
        {
            if ((_enabledLevels & level) == 0)
                return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var line = $"[{timestamp}] [{level.ToString().ToLowerInvariant()}] {message}";
            lock (_lock)
            {
                Console.WriteLine(line);
                _writer.WriteLine(line);
            }
        }

        public void Dispose()
        {
            lock (_lock)
                _writer.Dispose();
        }
    }
}
