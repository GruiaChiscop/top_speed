using System;
using System.Collections.Generic;
using System.IO;
using TopSpeed.Server.Protocol;

namespace TopSpeed.Server.Tracks
{
    internal static class TrackLoader
    {
        private const int Types = 9;
        private const int Surfaces = 5;
        private const int Noises = 12;
        private const int MinPartLength = 5000;

        public static TrackData LoadTrack(string nameOrPath, byte defaultLaps)
        {
            if (TrackCatalog.BuiltIn.TryGetValue(nameOrPath, out var builtIn))
            {
                return CloneWithLaps(builtIn, ResolveLaps(nameOrPath, defaultLaps));
            }

            var data = ReadCustomTrackData(nameOrPath);
            data.Laps = ResolveLaps(nameOrPath, defaultLaps);
            return data;
        }

        private static TrackData CloneWithLaps(TrackData source, byte laps)
        {
            return new TrackData
            {
                UserDefined = source.UserDefined,
                Weather = source.Weather,
                Ambience = source.Ambience,
                Definitions = source.Definitions,
                Length = (ushort)Math.Min(source.Definitions.Length, ProtocolConstants.MaxMultiTrackLength),
                Laps = laps
            };
        }

        private static byte ResolveLaps(string trackName, byte defaultLaps)
        {
            return trackName.IndexOf("adv", StringComparison.OrdinalIgnoreCase) < 0
                ? defaultLaps
                : (byte)1;
        }

        private static TrackData ReadCustomTrackData(string filename)
        {
            var resolvedPath = ResolveTrackPath(filename);
            if (resolvedPath == null)
            {
                return CreateFallbackTrack();
            }

            var values = new List<int>();
            foreach (var line in File.ReadLines(resolvedPath))
            {
                if (int.TryParse(line.Trim(), out var value))
                    values.Add(value);
            }

            var length = 0;
            var index = 0;
            while (index < values.Count)
            {
                var first = values[index++];
                if (first < 0)
                    break;
                if (index < values.Count) index++;
                if (index >= values.Count) break;
                var third = values[index++];
                if (third < MinPartLength && index < values.Count)
                    index++;
                length++;
            }

            if (length == 0)
            {
                return CreateFallbackTrack();
            }

            var definitions = new MultiplayerDefinition[length];
            index = 0;
            for (var i = 0; i < length; i++)
            {
                var typeValue = index < values.Count ? values[index++] : 0;
                var surfaceValue = index < values.Count ? values[index++] : 0;
                var temp = index < values.Count ? values[index++] : 0;

                var noiseValue = 0;
                var lengthValue = 0;
                if (temp < Noises)
                {
                    noiseValue = temp;
                    lengthValue = index < values.Count ? values[index++] : MinPartLength;
                }
                else
                {
                    if (typeValue >= Types)
                    {
                        noiseValue = (typeValue - Types) + 1;
                        typeValue = 0;
                    }
                    else
                    {
                        noiseValue = 0;
                    }
                    lengthValue = temp;
                }

                if (typeValue >= Types)
                    typeValue = 0;
                if (surfaceValue >= Surfaces)
                    surfaceValue = 0;
                if (noiseValue >= Noises)
                    noiseValue = 0;
                if (lengthValue < MinPartLength)
                    lengthValue = MinPartLength;

                definitions[i] = new MultiplayerDefinition
                {
                    Type = (byte)typeValue,
                    Surface = (byte)surfaceValue,
                    Noise = (byte)noiseValue,
                    Length = (uint)lengthValue
                };
            }

            if (index < values.Count)
                index++; // skip -1

            var weatherValue = index < values.Count ? values[index++] : 0;
            if (weatherValue < 0)
                weatherValue = 0;
            var ambienceValue = index < values.Count ? values[index++] : 0;
            if (ambienceValue < 0)
                ambienceValue = 0;

            return new TrackData
            {
                UserDefined = true,
                Weather = (byte)weatherValue,
                Ambience = (byte)ambienceValue,
                Definitions = definitions,
                Length = (ushort)Math.Min(definitions.Length, ProtocolConstants.MaxMultiTrackLength)
            };
        }

        private static string? ResolveTrackPath(string nameOrPath)
        {
            if (File.Exists(nameOrPath))
                return nameOrPath;

            var baseDir = AppContext.BaseDirectory;
            var candidate = Path.Combine(baseDir, "Tracks", nameOrPath);
            if (File.Exists(candidate))
                return candidate;

            return null;
        }

        private static TrackData CreateFallbackTrack()
        {
            var definitions = new[]
            {
                new MultiplayerDefinition
                {
                    Type = (byte)TrackType.Straight,
                    Surface = (byte)TrackSurface.Asphalt,
                    Noise = (byte)TrackNoise.NoNoise,
                    Length = MinPartLength
                }
            };

            return new TrackData
            {
                UserDefined = true,
                Weather = (byte)TrackWeather.Sunny,
                Ambience = (byte)TrackAmbience.NoAmbience,
                Definitions = definitions,
                Length = 1
            };
        }
    }
}
