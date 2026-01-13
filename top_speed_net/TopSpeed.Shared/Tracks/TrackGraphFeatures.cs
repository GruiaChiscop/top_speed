using System;
using System.Collections.Generic;
using TopSpeed.Data;

namespace TopSpeed.Tracks.Geometry
{
    public readonly struct TrackWeatherZone
    {
        public float StartMeters { get; }
        public float EndMeters { get; }
        public TrackWeather Weather { get; }
        public float FadeInMeters { get; }
        public float FadeOutMeters { get; }

        public TrackWeatherZone(float startMeters, float endMeters, TrackWeather weather, float fadeInMeters = 0f, float fadeOutMeters = 0f)
        {
            if (!TrackGraphValidation.IsFinite(startMeters))
                throw new ArgumentOutOfRangeException(nameof(startMeters));
            if (!TrackGraphValidation.IsFinite(endMeters))
                throw new ArgumentOutOfRangeException(nameof(endMeters));
            if (endMeters < startMeters)
                throw new ArgumentException("Weather zone end must be >= start.", nameof(endMeters));
            if (!TrackGraphValidation.IsFinite(fadeInMeters) || fadeInMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(fadeInMeters));
            if (!TrackGraphValidation.IsFinite(fadeOutMeters) || fadeOutMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(fadeOutMeters));

            StartMeters = startMeters;
            EndMeters = endMeters;
            Weather = weather;
            FadeInMeters = fadeInMeters;
            FadeOutMeters = fadeOutMeters;
        }
    }

    public readonly struct TrackAmbienceZone
    {
        public float StartMeters { get; }
        public float EndMeters { get; }
        public TrackAmbience Ambience { get; }
        public float FadeInMeters { get; }
        public float FadeOutMeters { get; }

        public TrackAmbienceZone(float startMeters, float endMeters, TrackAmbience ambience, float fadeInMeters = 0f, float fadeOutMeters = 0f)
        {
            if (!TrackGraphValidation.IsFinite(startMeters))
                throw new ArgumentOutOfRangeException(nameof(startMeters));
            if (!TrackGraphValidation.IsFinite(endMeters))
                throw new ArgumentOutOfRangeException(nameof(endMeters));
            if (endMeters < startMeters)
                throw new ArgumentException("Ambience zone end must be >= start.", nameof(endMeters));
            if (!TrackGraphValidation.IsFinite(fadeInMeters) || fadeInMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(fadeInMeters));
            if (!TrackGraphValidation.IsFinite(fadeOutMeters) || fadeOutMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(fadeOutMeters));

            StartMeters = startMeters;
            EndMeters = endMeters;
            Ambience = ambience;
            FadeInMeters = fadeInMeters;
            FadeOutMeters = fadeOutMeters;
        }
    }

    public sealed class TrackHazardZone
    {
        public float StartMeters { get; }
        public float EndMeters { get; }
        public string HazardType { get; }
        public float Severity { get; }
        public string? Name { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }

        public TrackHazardZone(
            float startMeters,
            float endMeters,
            string hazardType,
            float severity = 1f,
            string? name = null,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (!TrackGraphValidation.IsFinite(startMeters))
                throw new ArgumentOutOfRangeException(nameof(startMeters));
            if (!TrackGraphValidation.IsFinite(endMeters))
                throw new ArgumentOutOfRangeException(nameof(endMeters));
            if (endMeters < startMeters)
                throw new ArgumentException("Hazard end must be >= start.", nameof(endMeters));
            if (string.IsNullOrWhiteSpace(hazardType))
                throw new ArgumentException("Hazard type is required.", nameof(hazardType));
            if (!TrackGraphValidation.IsFinite(severity) || severity < 0f)
                throw new ArgumentOutOfRangeException(nameof(severity));

            StartMeters = startMeters;
            EndMeters = endMeters;
            HazardType = hazardType.Trim();
            Severity = severity;
            var trimmedName = name?.Trim();
            Name = string.IsNullOrWhiteSpace(trimmedName) ? null : trimmedName;
            Metadata = metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public readonly struct TrackCheckpoint
    {
        public string Id { get; }
        public string? Name { get; }
        public float PositionMeters { get; }

        public TrackCheckpoint(string id, float positionMeters, string? name = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Checkpoint id is required.", nameof(id));
            if (!TrackGraphValidation.IsFinite(positionMeters))
                throw new ArgumentOutOfRangeException(nameof(positionMeters));

            Id = id.Trim();
            var trimmedName = name?.Trim();
            Name = string.IsNullOrWhiteSpace(trimmedName) ? null : trimmedName;
            PositionMeters = positionMeters;
        }
    }

    public sealed class TrackHitLaneZone
    {
        public float StartMeters { get; }
        public float EndMeters { get; }
        public IReadOnlyList<int> LaneIndices { get; }
        public string? Effect { get; }

        public TrackHitLaneZone(float startMeters, float endMeters, IReadOnlyList<int> laneIndices, string? effect = null)
        {
            if (!TrackGraphValidation.IsFinite(startMeters))
                throw new ArgumentOutOfRangeException(nameof(startMeters));
            if (!TrackGraphValidation.IsFinite(endMeters))
                throw new ArgumentOutOfRangeException(nameof(endMeters));
            if (endMeters < startMeters)
                throw new ArgumentException("Hit lane end must be >= start.", nameof(endMeters));
            if (laneIndices == null || laneIndices.Count == 0)
                throw new ArgumentException("Lane indices are required.", nameof(laneIndices));

            StartMeters = startMeters;
            EndMeters = endMeters;
            LaneIndices = laneIndices;
            var trimmedEffect = effect?.Trim();
            Effect = string.IsNullOrWhiteSpace(trimmedEffect) ? null : trimmedEffect;
        }
    }

    public sealed class TrackAudioEmitter
    {
        public string Id { get; }
        public float PositionMeters { get; }
        public float RadiusMeters { get; }
        public string? SoundKey { get; }
        public bool Loop { get; }
        public float Volume { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }

        public TrackAudioEmitter(
            string id,
            float positionMeters,
            float radiusMeters,
            string? soundKey = null,
            bool loop = true,
            float volume = 1f,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Emitter id is required.", nameof(id));
            if (!TrackGraphValidation.IsFinite(positionMeters))
                throw new ArgumentOutOfRangeException(nameof(positionMeters));
            if (!TrackGraphValidation.IsFinite(radiusMeters) || radiusMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(radiusMeters));
            if (!TrackGraphValidation.IsFinite(volume) || volume < 0f)
                throw new ArgumentOutOfRangeException(nameof(volume));

            Id = id.Trim();
            PositionMeters = positionMeters;
            RadiusMeters = radiusMeters;
            var trimmedSoundKey = soundKey?.Trim();
            SoundKey = string.IsNullOrWhiteSpace(trimmedSoundKey) ? null : trimmedSoundKey;
            Loop = loop;
            Volume = volume;
            Metadata = metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class TrackTriggerZone
    {
        public string Id { get; }
        public float StartMeters { get; }
        public float EndMeters { get; }
        public string? Action { get; }
        public string? Payload { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }

        public TrackTriggerZone(
            string id,
            float startMeters,
            float endMeters,
            string? action = null,
            string? payload = null,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Trigger id is required.", nameof(id));
            if (!TrackGraphValidation.IsFinite(startMeters))
                throw new ArgumentOutOfRangeException(nameof(startMeters));
            if (!TrackGraphValidation.IsFinite(endMeters))
                throw new ArgumentOutOfRangeException(nameof(endMeters));
            if (endMeters < startMeters)
                throw new ArgumentException("Trigger end must be >= start.", nameof(endMeters));

            Id = id.Trim();
            StartMeters = startMeters;
            EndMeters = endMeters;
            var trimmedAction = action?.Trim();
            Action = string.IsNullOrWhiteSpace(trimmedAction) ? null : trimmedAction;
            var trimmedPayload = payload?.Trim();
            Payload = string.IsNullOrWhiteSpace(trimmedPayload) ? null : trimmedPayload;
            Metadata = metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal static class TrackGraphValidation
    {
        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
