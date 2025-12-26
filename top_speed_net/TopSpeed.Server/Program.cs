using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using TopSpeed.Server.Logging;
using TopSpeed.Server.Network;

namespace TopSpeed.Server
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var levels = ParseLogLevels(args);
            var logFile = BuildLogFilePath();
            using var logger = new Logger(levels, logFile);
            logger.Info("TopSpeed.Server starting.");

            var config = new RaceServerConfig
            {
                Port = 28630,
                MaxPlayers = 8,
                ServerNumber = 1000,
                Name = "TopSpeed Server"
            };

            using var server = new RaceServer(config, logger);
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            server.Start();
            RunLoop(server, cts.Token);
            server.Stop();

            logger.Info("TopSpeed.Server stopped.");
            return 0;
        }

        private static void RunLoop(RaceServer server, CancellationToken token)
        {
            var stopwatch = Stopwatch.StartNew();
            var last = stopwatch.Elapsed;
            while (!token.IsCancellationRequested)
            {
                var now = stopwatch.Elapsed;
                var deltaSeconds = (float)(now - last).TotalSeconds;
                last = now;
                server.Update(deltaSeconds);
                Thread.Sleep(1);
            }
        }

        private static LogLevel ParseLogLevels(string[] args)
        {
            var value = GetArgumentValue(args, "--log");
            if (string.IsNullOrWhiteSpace(value))
                return LogLevel.Error | LogLevel.Warning | LogLevel.Info;

            var levels = LogLevel.None;
            var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var token = part.Trim().ToLowerInvariant();
                switch (token)
                {
                    case "error":
                        levels |= LogLevel.Error;
                        break;
                    case "warning":
                        levels |= LogLevel.Warning;
                        break;
                    case "info":
                        levels |= LogLevel.Info;
                        break;
                    case "debug":
                        levels |= LogLevel.Debug;
                        break;
                    case "all":
                        levels = LogLevel.All;
                        break;
                }
            }

            return levels == LogLevel.None
                ? LogLevel.Error | LogLevel.Warning | LogLevel.Info
                : levels;
        }

        private static string? GetArgumentValue(string[] args, string key)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.Equals(arg, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                        return args[i + 1];
                    return null;
                }

                if (arg.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    return arg.Substring(key.Length + 1);
            }

            return null;
        }

        private static string BuildLogFilePath()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var logsRoot = Path.Combine(AppContext.BaseDirectory, "Logs");
            return Path.Combine(logsRoot, $"server_{timestamp}.log");
        }
    }
}
