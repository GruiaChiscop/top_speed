using System;
using System.Numerics;
using TopSpeed.Tracks.Map;
using TopSpeed.Tracks.Topology;

namespace TopSpeed.Tracks.Guidance
{
    internal readonly struct TrackApproachCue
    {
        public TrackApproachCue(
            string sectorId,
            TrackApproachSide side,
            string portalId,
            Vector2 portalPosition,
            float targetHeadingDegrees,
            float deltaDegrees,
            float distanceMeters,
            float? widthMeters,
            float? lengthMeters,
            float? toleranceDegrees,
            bool passed)
        {
            SectorId = sectorId;
            Side = side;
            PortalId = portalId;
            PortalPosition = portalPosition;
            TargetHeadingDegrees = targetHeadingDegrees;
            DeltaDegrees = deltaDegrees;
            DistanceMeters = distanceMeters;
            WidthMeters = widthMeters;
            LengthMeters = lengthMeters;
            ToleranceDegrees = toleranceDegrees;
            Passed = passed;
        }

        public string SectorId { get; }
        public TrackApproachSide Side { get; }
        public string PortalId { get; }
        public Vector2 PortalPosition { get; }
        public float TargetHeadingDegrees { get; }
        public float DeltaDegrees { get; }
        public float DistanceMeters { get; }
        public float? WidthMeters { get; }
        public float? LengthMeters { get; }
        public float? ToleranceDegrees { get; }
        public bool Passed { get; }
    }

    internal sealed class TrackApproachBeacon
    {
        private readonly TrackPortalManager _portalManager;
        private readonly TrackApproachManager _approachManager;
        private readonly float _rangeMeters;

        public TrackApproachBeacon(TrackMap map, float rangeMeters = 50f)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));

            _portalManager = map.BuildPortalManager();
            _approachManager = new TrackApproachManager(map.Sectors, map.Approaches, _portalManager);
            _rangeMeters = Math.Max(1f, rangeMeters);
        }

        public float RangeMeters => _rangeMeters;

        public bool TryGetCue(Vector3 worldPosition, float headingDegrees, out TrackApproachCue cue)
        {
            cue = default;
            if (_approachManager.Approaches.Count == 0)
                return false;

            var position = new Vector2(worldPosition.X, worldPosition.Z);
            var best = default(Candidate);
            var hasBest = false;

            foreach (var approach in _approachManager.Approaches)
            {
                if (approach == null)
                    continue;

                if (TryBuildCandidate(approach, TrackApproachSide.Entry, position, ref best, ref hasBest))
                    continue;
                TryBuildCandidate(approach, TrackApproachSide.Exit, position, ref best, ref hasBest);
            }

            if (!hasBest)
                return false;

            var delta = DeltaDegrees(headingDegrees, best.TargetHeadingDegrees);
            var forward = HeadingToVector(best.TargetHeadingDegrees);
            var toPlayer = position - best.PortalPosition;
            var passed = Vector2.Dot(forward, toPlayer) > 0f;

            var sectorId = best.SectorId ?? string.Empty;
            var portalId = best.PortalId ?? string.Empty;
            cue = new TrackApproachCue(
                sectorId,
                best.Side,
                portalId,
                best.PortalPosition,
                best.TargetHeadingDegrees,
                delta,
                best.DistanceMeters,
                best.WidthMeters,
                best.LengthMeters,
                best.ToleranceDegrees,
                passed);
            return true;
        }

        private bool TryBuildCandidate(
            TrackApproachDefinition approach,
            TrackApproachSide side,
            Vector2 position,
            ref Candidate best,
            ref bool hasBest)
        {
            var portalId = side == TrackApproachSide.Entry ? approach.EntryPortalId : approach.ExitPortalId;
            var heading = side == TrackApproachSide.Entry ? approach.EntryHeadingDegrees : approach.ExitHeadingDegrees;
            if (!heading.HasValue || string.IsNullOrWhiteSpace(portalId))
                return false;
            if (!_portalManager.TryGetPortal(portalId!, out var portal))
                return false;

            var portalPos = new Vector2(portal.X, portal.Z);
            var distance = Vector2.Distance(position, portalPos);
            if (distance > _rangeMeters)
                return false;

            if (!hasBest || distance < best.DistanceMeters)
            {
                best = new Candidate
                {
                    SectorId = approach.SectorId,
                    Side = side,
                    PortalId = portal.Id,
                    PortalPosition = portalPos,
                    TargetHeadingDegrees = heading.Value,
                    DistanceMeters = distance,
                    WidthMeters = approach.WidthMeters,
                    LengthMeters = approach.LengthMeters,
                    ToleranceDegrees = approach.AlignmentToleranceDegrees
                };
                hasBest = true;
            }

            return true;
        }

        private static float NormalizeDegrees(float degrees)
        {
            var result = degrees % 360f;
            if (result < 0f)
                result += 360f;
            return result;
        }

        private static float DeltaDegrees(float current, float target)
        {
            var diff = Math.Abs(NormalizeDegrees(current - target));
            return diff > 180f ? 360f - diff : diff;
        }

        private static Vector2 HeadingToVector(float headingDegrees)
        {
            var radians = headingDegrees * (float)Math.PI / 180f;
            return new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians));
        }

        private struct Candidate
        {
            public string? SectorId;
            public TrackApproachSide Side;
            public string? PortalId;
            public Vector2 PortalPosition;
            public float TargetHeadingDegrees;
            public float DistanceMeters;
            public float? WidthMeters;
            public float? LengthMeters;
            public float? ToleranceDegrees;
        }
    }
}
