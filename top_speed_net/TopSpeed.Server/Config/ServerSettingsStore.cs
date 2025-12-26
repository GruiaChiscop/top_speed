using System;
using System.IO;
using System.Text.Json;
using TopSpeed.Server.Logging;

namespace TopSpeed.Server.Config
{
    internal sealed class ServerSettingsStore
    {
        private readonly string _path;
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public ServerSettingsStore(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public ServerSettings LoadOrCreate(Logger logger)
        {
            if (!File.Exists(_path))
            {
                var settings = new ServerSettings();
                Save(settings, logger);
                return settings;
            }

            try
            {
                var json = File.ReadAllText(_path);
                var settings = JsonSerializer.Deserialize<ServerSettings>(json, _options);
                return settings ?? new ServerSettings();
            }
            catch (Exception ex)
            {
                logger.Warning($"Failed to read server settings, using defaults: {ex.Message}");
                return new ServerSettings();
            }
        }

        public void Save(ServerSettings settings, Logger logger)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                var json = JsonSerializer.Serialize(settings, _options);
                File.WriteAllText(_path, json);
            }
            catch (Exception ex)
            {
                logger.Warning($"Failed to save server settings: {ex.Message}");
            }
        }
    }
}
