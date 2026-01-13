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

    public enum TrackBoundarySide
    {
        Left = 0,
        Right = 1,
        Both = 2
    }

    public enum TrackBoundaryType
    {
        Unknown = 0,
        Wall = 1,
        Guardrail = 2,
        Curb = 3,
        Grass = 4,
        Gravel = 5,
        Barrier = 6,
        Fence = 7,
        Cliff = 8,
        Water = 9,
        TreeLine = 10
    }

    public sealed class TrackBoundaryZone
    {
        public float StartMeters { get; }
        public float EndMeters { get; }
        public TrackBoundarySide Side { get; }
        public TrackBoundaryType BoundaryType { get; }
        public float OffsetMeters { get; }
        public float WidthMeters { get; }
        public float HeightMeters { get; }
        public float Severity { get; }
        public bool IsSolid { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }

        public TrackBoundaryZone(
            float startMeters,
            float endMeters,
            TrackBoundarySide side,
            TrackBoundaryType boundaryType,
            float offsetMeters,
            float widthMeters,
            float heightMeters,
            bool isSolid = true,
            float severity = 1f,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (!TrackGraphValidation.IsFinite(startMeters))
                throw new ArgumentOutOfRangeException(nameof(startMeters));
            if (!TrackGraphValidation.IsFinite(endMeters))
                throw new ArgumentOutOfRangeException(nameof(endMeters));
            if (endMeters < startMeters)
                throw new ArgumentException("Boundary end must be >= start.", nameof(endMeters));
            if (!TrackGraphValidation.IsFinite(offsetMeters))
                throw new ArgumentOutOfRangeException(nameof(offsetMeters));
            if (!TrackGraphValidation.IsFinite(widthMeters) || widthMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(widthMeters));
            if (!TrackGraphValidation.IsFinite(heightMeters) || heightMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(heightMeters));
            if (!TrackGraphValidation.IsFinite(severity) || severity < 0f)
                throw new ArgumentOutOfRangeException(nameof(severity));

            StartMeters = startMeters;
            EndMeters = endMeters;
            Side = side;
            BoundaryType = boundaryType;
            OffsetMeters = offsetMeters;
            WidthMeters = widthMeters;
            HeightMeters = heightMeters;
            IsSolid = isSolid;
            Severity = severity;
            Metadata = metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public bool Contains(float sMeters)
        {
            return sMeters >= StartMeters && sMeters < EndMeters;
        }
    }

    public enum TrackIntersectionShape
    {
        Unspecified = 0,
        Circle = 1,
        Box = 2,
        Cross = 3,
        Roundabout = 4,
        Custom = 5
    }

    public enum TrackIntersectionControl
    {
        None = 0,
        Stop = 1,
        Yield = 2,
        Signal = 3
    }

    public enum TrackIntersectionLegType
    {
        Entry = 0,
        Exit = 1,
        Both = 2
    }

    public sealed class TrackIntersectionLeg
    {
        public string Id { get; }
        public string EdgeId { get; }
        public TrackIntersectionLegType LegType { get; }
        public int LaneCount { get; }
        public float HeadingDegrees { get; }
        public float SpeedLimitKph { get; }
        public int Priority { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }

        public TrackIntersectionLeg(
            string id,
            string edgeId,
            TrackIntersectionLegType legType,
            int laneCount = 0,
            float headingDegrees = 0f,
            float speedLimitKph = 0f,
            int priority = 0,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Intersection leg id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(edgeId))
                throw new ArgumentException("Intersection leg edge id is required.", nameof(edgeId));
            if (laneCount < 0)
                throw new ArgumentOutOfRangeException(nameof(laneCount));
            if (!TrackGraphValidation.IsFinite(headingDegrees))
                throw new ArgumentOutOfRangeException(nameof(headingDegrees));
            if (!TrackGraphValidation.IsFinite(speedLimitKph) || speedLimitKph < 0f)
                throw new ArgumentOutOfRangeException(nameof(speedLimitKph));

            Id = id.Trim();
            EdgeId = edgeId.Trim();
            LegType = legType;
            LaneCount = laneCount;
            HeadingDegrees = headingDegrees;
            SpeedLimitKph = speedLimitKph;
            Priority = priority;
            Metadata = metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class TrackIntersectionConnector
    {
        public string Id { get; }
        public string FromLegId { get; }
        public string ToLegId { get; }
        public TrackTurnDirection TurnDirection { get; }
        public float RadiusMeters { get; }
        public float LengthMeters { get; }
        public float SpeedLimitKph { get; }
        public int LaneCount { get; }
        public int Priority { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }

        public TrackIntersectionConnector(
            string id,
            string fromLegId,
            string toLegId,
            TrackTurnDirection turnDirection,
            float radiusMeters = 0f,
            float lengthMeters = 0f,
            float speedLimitKph = 0f,
            int laneCount = 0,
            int priority = 0,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Intersection connector id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(fromLegId))
                throw new ArgumentException("Intersection connector from-leg id is required.", nameof(fromLegId));
            if (string.IsNullOrWhiteSpace(toLegId))
                throw new ArgumentException("Intersection connector to-leg id is required.", nameof(toLegId));
            if (!TrackGraphValidation.IsFinite(radiusMeters) || radiusMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(radiusMeters));
            if (!TrackGraphValidation.IsFinite(lengthMeters) || lengthMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(lengthMeters));
            if (!TrackGraphValidation.IsFinite(speedLimitKph) || speedLimitKph < 0f)
                throw new ArgumentOutOfRangeException(nameof(speedLimitKph));
            if (laneCount < 0)
                throw new ArgumentOutOfRangeException(nameof(laneCount));

            Id = id.Trim();
            FromLegId = fromLegId.Trim();
            ToLegId = toLegId.Trim();
            TurnDirection = turnDirection;
            RadiusMeters = radiusMeters;
            LengthMeters = lengthMeters;
            SpeedLimitKph = speedLimitKph;
            LaneCount = laneCount;
            Priority = priority;
            Metadata = metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class TrackIntersectionProfile
    {
        public TrackIntersectionShape Shape { get; }
        public float RadiusMeters { get; }
        public float InnerRadiusMeters { get; }
        public float OuterRadiusMeters { get; }
        public int EntryLanes { get; }
        public int ExitLanes { get; }
        public int TurnLanes { get; }
        public float SpeedLimitKph { get; }
        public TrackIntersectionControl Control { get; }
        public int Priority { get; }
        public IReadOnlyList<TrackIntersectionLeg> Legs { get; }
        public IReadOnlyList<TrackIntersectionConnector> Connectors { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }

        public TrackIntersectionProfile(
            TrackIntersectionShape shape,
            float radiusMeters = 0f,
            float innerRadiusMeters = 0f,
            float outerRadiusMeters = 0f,
            int entryLanes = 0,
            int exitLanes = 0,
            int turnLanes = 0,
            float speedLimitKph = 0f,
            TrackIntersectionControl control = TrackIntersectionControl.None,
            int priority = 0,
            IReadOnlyList<TrackIntersectionLeg>? legs = null,
            IReadOnlyList<TrackIntersectionConnector>? connectors = null,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (!TrackGraphValidation.IsFinite(radiusMeters) || radiusMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(radiusMeters));
            if (!TrackGraphValidation.IsFinite(innerRadiusMeters) || innerRadiusMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(innerRadiusMeters));
            if (!TrackGraphValidation.IsFinite(outerRadiusMeters) || outerRadiusMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(outerRadiusMeters));
            if (entryLanes < 0)
                throw new ArgumentOutOfRangeException(nameof(entryLanes));
            if (exitLanes < 0)
                throw new ArgumentOutOfRangeException(nameof(exitLanes));
            if (turnLanes < 0)
                throw new ArgumentOutOfRangeException(nameof(turnLanes));
            if (!TrackGraphValidation.IsFinite(speedLimitKph) || speedLimitKph < 0f)
                throw new ArgumentOutOfRangeException(nameof(speedLimitKph));

            Shape = shape;
            RadiusMeters = radiusMeters;
            InnerRadiusMeters = innerRadiusMeters;
            OuterRadiusMeters = outerRadiusMeters;
            EntryLanes = entryLanes;
            ExitLanes = exitLanes;
            TurnLanes = turnLanes;
            SpeedLimitKph = speedLimitKph;
            Control = control;
            Priority = priority;
            Legs = legs ?? Array.Empty<TrackIntersectionLeg>();
            Connectors = connectors ?? Array.Empty<TrackIntersectionConnector>();
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
