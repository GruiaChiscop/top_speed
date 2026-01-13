using System;

namespace TopSpeed.Tracks
{
    internal readonly struct TrackPosition
    {
        public int EdgeIndex { get; }
        public float EdgeMeters { get; }
        public float DistanceMeters { get; }

        public TrackPosition(int edgeIndex, float edgeMeters, float distanceMeters)
        {
            EdgeIndex = edgeIndex;
            EdgeMeters = edgeMeters;
            DistanceMeters = distanceMeters;
        }

        public bool IsGraphPosition => EdgeIndex >= 0;

        public TrackPosition WithEdge(int edgeIndex, float edgeMeters)
        {
            return new TrackPosition(edgeIndex, edgeMeters, DistanceMeters);
        }

        public TrackPosition WithDistance(float distanceMeters)
        {
            return new TrackPosition(EdgeIndex, EdgeMeters, distanceMeters);
        }

        public override string ToString()
        {
            return $"Edge={EdgeIndex} s={EdgeMeters:F2} dist={DistanceMeters:F2}";
        }
    }
}
