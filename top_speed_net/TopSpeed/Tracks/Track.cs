using System;
using System.Collections.Generic;
using System.Numerics;
using System.IO;
using TopSpeed.Audio;
using TopSpeed.Core;
using TopSpeed.Data;
using TopSpeed.Tracks.Geometry;
using TS.Audio;

namespace TopSpeed.Tracks
{
    internal sealed class Track : IDisposable
    {
        private const float LaneWidthMeters = 5.0f;
        private const float LegacyLaneWidthMeters = 50.0f;
        private const float CallLengthMeters = 30.0f;
        private const int Types = 9;
        private const int Surfaces = 5;
        private const int Noises = 12;
        private const float MinPartLengthMeters = 50.0f;
        private const float DisconnectedNodeSpacingMeters = 2000.0f;

        public struct Road
        {
            public float Left;
            public float Right;
            public TrackSurface Surface;
            public TrackType Type;
            public float Length;
        }

        private sealed class EdgeRuntime
        {
            public TrackGraphEdge Edge { get; }
            public TrackGeometry Geometry { get; }
            public TrackGeometrySpan[] Spans { get; }
            public float[] SpanStartMeters { get; }
            public float LengthMeters { get; }
            public Vector3 Origin;
            public float HeadingRadians;
            public Vector3 EndPosition;
            public float EndHeadingRadians;

            public EdgeRuntime(TrackGraphEdge edge, TrackGeometry geometry, TrackGeometrySpan[] spans, float[] spanStartMeters)
            {
                Edge = edge;
                Geometry = geometry;
                Spans = spans;
                SpanStartMeters = spanStartMeters;
                LengthMeters = edge.LengthMeters;
                Origin = Vector3.Zero;
                HeadingRadians = 0f;
                EndPosition = Vector3.Zero;
                EndHeadingRadians = 0f;
            }
        }

        private readonly struct TrackSpanKey : IEquatable<TrackSpanKey>
        {
            public int EdgeIndex { get; }
            public int SpanIndex { get; }

            public TrackSpanKey(int edgeIndex, int spanIndex)
            {
                EdgeIndex = edgeIndex;
                SpanIndex = spanIndex;
            }

            public bool Equals(TrackSpanKey other)
            {
                return EdgeIndex == other.EdgeIndex && SpanIndex == other.SpanIndex;
            }

            public override bool Equals(object? obj)
            {
                return obj is TrackSpanKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (EdgeIndex * 397) ^ SpanIndex;
                }
            }
        }

        private readonly AudioManager _audio;
        private readonly string _trackName;
        private readonly bool _userDefined;
        private readonly TrackDefinition[] _definition;
        private readonly int _segmentCount;
        private readonly TrackWeather _weather;
        private readonly TrackAmbience _ambience;
        private readonly TrackLayout? _layout;
        private readonly TrackGeometry? _geometry;
        private readonly TrackGeometrySpan[]? _layoutSpans;
        private readonly float[]? _layoutSpanStart;
        private TrackNoise _currentNoise;

        private float _laneWidth;
        private float _curveScale;
        private float _callLength;
        private float _lapDistance;
        private float _lapCenter;
        private int _currentRoad;
        private float _relPos;
        private float _prevRelPos;
        private int _lastCalled;
        private TrackSpanKey _lastCalledSpan;
        private float _factor;
        private float _noiseLength;
        private float _noiseStartPos;
        private float _noiseEndPos;
        private bool _noisePlaying;

        private TrackGraphNode[]? _graphNodes;
        private TrackGraphEdge[]? _graphEdges;
        private EdgeRuntime[]? _edgeRuntime;
        private Dictionary<string, int>? _edgeIndexById;
        private Dictionary<string, int>? _nodeIndexById;
        private List<int>[]? _nodeOutgoing;
        private List<int>[]? _nodeIncoming;
        private int _currentEdgeIndex;
        private int _currentSpanIndex;
        private int _primaryEdgeIndex;

        private AudioSourceHandle? _soundCrowd;
        private AudioSourceHandle? _soundOcean;
        private AudioSourceHandle? _soundRain;
        private AudioSourceHandle? _soundWind;
        private AudioSourceHandle? _soundStorm;
        private AudioSourceHandle? _soundDesert;
        private AudioSourceHandle? _soundAirport;
        private AudioSourceHandle? _soundAirplane;
        private AudioSourceHandle? _soundClock;
        private AudioSourceHandle? _soundJet;
        private AudioSourceHandle? _soundThunder;
        private AudioSourceHandle? _soundPile;
        private AudioSourceHandle? _soundConstruction;
        private AudioSourceHandle? _soundRiver;
        private AudioSourceHandle? _soundHelicopter;
        private AudioSourceHandle? _soundOwl;

        private bool IsLayout => _layout != null && _geometry != null && _layoutSpans != null && _layoutSpanStart != null;

        public bool IsLoop => _layout?.PrimaryRoute.IsLoop ?? true;

        private Track(string trackName, TrackData data, AudioManager audio, bool userDefined)
        {
            _trackName = trackName.Length < 64 ? trackName : string.Empty;      
            _userDefined = userDefined;
            _audio = audio;
            _laneWidth = LaneWidthMeters;
            UpdateCurveScale();
            _callLength = CallLengthMeters;
            _weather = data.Weather;
            _ambience = data.Ambience;
            _definition = data.Definitions;
            _segmentCount = _definition.Length;
            _currentNoise = TrackNoise.NoNoise;

            InitializeSounds();
        }

        private Track(string trackName, TrackLayout layout, TrackGeometry geometry, AudioManager audio, bool userDefined)
        {
            _trackName = trackName.Length < 64 ? trackName : string.Empty;
            _userDefined = userDefined;
            _audio = audio;
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            _laneWidth = Math.Max(1f, _layout.WidthAt(0f) * 0.5f);
            UpdateCurveScale();
            _callLength = CallLengthMeters;
            _weather = _layout.Weather;
            _ambience = _layout.Ambience;
            _definition = Array.Empty<TrackDefinition>();
            _segmentCount = _layout.Geometry.Spans.Count;
            _layoutSpans = new TrackGeometrySpan[_segmentCount];
            _layoutSpanStart = new float[_segmentCount];
            _currentNoise = _layout.NoiseAt(0f);

            var distance = 0f;
            for (var i = 0; i < _segmentCount; i++)
            {
                _layoutSpans[i] = _layout.Geometry.Spans[i];
                _layoutSpanStart[i] = distance;
                distance += _layoutSpans[i].LengthMeters;
            }

            BuildGraphRuntime();
            InitializeSounds();
        }

        public static Track Load(string nameOrPath, AudioManager audio)
        {
            var layoutTrack = TryLoadLayout(nameOrPath, audio);
            if (layoutTrack != null)
                return layoutTrack;

            if (!LooksLikePath(nameOrPath))
                throw new FileNotFoundException("Track layout not found.", nameOrPath);

            var data = ReadCustomTrackData(nameOrPath);
            var displayName = ResolveCustomTrackName(nameOrPath, data.Name);
            return new Track(displayName, data, audio, userDefined: true);
        }

        public static Track LoadFromData(string trackName, TrackData data, AudioManager audio, bool userDefined)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            return new Track(trackName, data, audio, userDefined);
        }

        private static Track? TryLoadLayout(string nameOrPath, AudioManager audio)
        {
            var root = Path.Combine(AssetPaths.Root, "Tracks");
            var sources = new ITrackLayoutSource[]
            {
                new FileTrackLayoutSource(new[] { root })
            };
            var loader = new TrackLayoutLoader(sources);
            var request = new TrackLayoutLoadRequest(nameOrPath, validate: true, buildGeometry: true, allowWarnings: true);
            var result = loader.Load(request);
            if (!result.IsSuccess || result.Layout == null || result.Geometry == null)
                return null;

            var trackName = ResolveLayoutTrackName(nameOrPath, result.Layout);
            var userDefined = LooksLikePath(nameOrPath);
            return new Track(trackName, result.Layout, result.Geometry, audio, userDefined);
        }

        private static string ResolveLayoutTrackName(string identifier, TrackLayout layout)
        {
            var name = layout.Metadata?.Name;
            if (!string.IsNullOrWhiteSpace(name))
                return name!;
            if (identifier.IndexOfAny(new[] { '\\', '/' }) >= 0)
            {
                var fileName = Path.GetFileNameWithoutExtension(identifier);
                if (!string.IsNullOrWhiteSpace(fileName))
                    return fileName;
            }
            return string.IsNullOrWhiteSpace(identifier) ? "Track" : identifier;
        }

        private static bool LooksLikePath(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return false;
            if (identifier.IndexOfAny(new[] { '\\', '/' }) >= 0)
                return true;
            return Path.HasExtension(identifier);
        }

        public string TrackName => _trackName;
        public float Length => _lapDistance;
        public int SegmentCount => _segmentCount;
        public float LapDistance => _lapDistance;
        public TrackWeather Weather => _weather;
        public TrackAmbience Ambience => _ambience;
        public bool UserDefined => _userDefined;
        public float LaneWidth => _laneWidth;
        public bool HasGeometry => _geometry != null;
        public TrackSurface InitialSurface => _layout != null
            ? _layout.SurfaceAt(0f)
            : (_definition.Length > 0 ? _definition[0].Surface : TrackSurface.Asphalt);

        public TrackPose GetPose(TrackPosition position)
        {
            if (IsLayout && _edgeRuntime != null)
            {
                var normalized = NormalizePosition(position);
                if (normalized.EdgeIndex >= 0 && normalized.EdgeIndex < _edgeRuntime.Length)
                {
                    var runtime = _edgeRuntime[normalized.EdgeIndex];
                    var localPose = runtime.Geometry.GetPoseClamped(normalized.EdgeMeters);
                    return TransformPose(localPose, runtime.Origin, runtime.HeadingRadians);
                }
            }

            if (_geometry != null)
                return _geometry.GetPose(position.DistanceMeters);
            var pos = new Vector3(0f, 0f, position.DistanceMeters);
            return new TrackPose(pos, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 0f, 0f);
        }

        public TrackPose GetPose(float positionMeters)
        {
            if (IsLayout)
                return GetPose(CreatePositionFromDistance(positionMeters));
            if (_geometry != null)
                return _geometry.GetPose(positionMeters);
            var pos = new Vector3(0f, 0f, positionMeters);
            return new TrackPose(pos, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 0f, 0f);
        }

        public Vector3 GetWorldPosition(TrackPosition position, float lateralOffset)
        {
            var pose = GetPose(position);
            return pose.Position + pose.Right * lateralOffset;
        }

        public Vector3 GetWorldPosition(float positionMeters, float lateralOffset)
        {
            var pose = GetPose(positionMeters);
            return pose.Position + pose.Right * lateralOffset;
        }

        public void SetLaneWidth(float laneWidth)
        {
            _laneWidth = laneWidth;
            UpdateCurveScale();
        }

        public int Lap(float position)
        {
            if (_lapDistance <= 0)
                return 1;
            return (int)(position / _lapDistance) + 1;
        }

        public int Lap(TrackPosition position)
        {
            return Lap(position.DistanceMeters);
        }

        public TrackPosition CreateStartPosition(float distanceMeters = 0f)
        {
            if (!IsLayout || _edgeRuntime == null || _layout == null || _edgeIndexById == null)
                return new TrackPosition(-1, 0f, distanceMeters);

            return CreatePositionFromDistance(distanceMeters);
        }

        public TrackPosition CreatePositionFromDistance(float distanceMeters)
        {
            if (!IsLayout || _layout == null || _edgeRuntime == null || _edgeIndexById == null)
                return new TrackPosition(-1, 0f, distanceMeters);

            var edge = _layout.ResolvePrimaryEdge(distanceMeters, out var localS);
            if (!_edgeIndexById.TryGetValue(edge.Id, out var edgeIndex))
                edgeIndex = _primaryEdgeIndex;
            localS = Clamp(localS, 0f, _edgeRuntime[edgeIndex].LengthMeters);
            return new TrackPosition(edgeIndex, localS, distanceMeters);
        }

        public TrackPosition Advance(TrackPosition position, float deltaMeters, float branchHint)
        {
            if (!IsLayout || _edgeRuntime == null || _graphEdges == null)
                return new TrackPosition(-1, 0f, position.DistanceMeters + deltaMeters);

            var normalized = NormalizePosition(position);
            var edgeIndex = normalized.EdgeIndex;
            var localS = normalized.EdgeMeters;
            var remaining = deltaMeters;
            var traveled = 0f;
            var safety = 0;

            while (Math.Abs(remaining) > 0.0001f && safety < 1024)
            {
                var edgeLength = _edgeRuntime[edgeIndex].LengthMeters;
                if (remaining > 0f)
                {
                    var toEnd = edgeLength - localS;
                    if (remaining <= toEnd)
                    {
                        localS += remaining;
                        traveled += remaining;
                        remaining = 0f;
                    }
                    else
                    {
                        localS = edgeLength;
                        traveled += toEnd;
                        remaining -= toEnd;
                        var next = SelectNextEdge(edgeIndex, branchHint);
                        if (next < 0)
                        {
                            remaining = 0f;
                            break;
                        }
                        edgeIndex = next;
                        localS = 0f;
                    }
                }
                else
                {
                    var toStart = localS;
                    var step = -remaining;
                    if (step <= toStart)
                    {
                        localS -= step;
                        traveled -= step;
                        remaining = 0f;
                    }
                    else
                    {
                        localS = 0f;
                        traveled -= toStart;
                        remaining += toStart;
                        var prev = SelectPreviousEdge(edgeIndex, branchHint);
                        if (prev < 0)
                        {
                            remaining = 0f;
                            break;
                        }
                        edgeIndex = prev;
                        localS = _edgeRuntime[edgeIndex].LengthMeters;
                    }
                }

                safety++;
            }

            return new TrackPosition(edgeIndex, localS, normalized.DistanceMeters + traveled);
        }


        public void Initialize()
        {
            _lapDistance = 0;
            _lapCenter = 0;
            if (IsLayout)
            {
                _lapDistance = _geometry!.LengthMeters;
                _lapCenter = 0f;
                _currentRoad = 0;
                _currentEdgeIndex = _primaryEdgeIndex;
                _currentSpanIndex = 0;
                _relPos = 0f;
                _prevRelPos = 0f;
                _lastCalled = 0;
                _lastCalledSpan = new TrackSpanKey(-1, -1);
            }
            else
            {
                for (var i = 0; i < _segmentCount; i++)
                {
                    _lapDistance += _definition[i].Length;
                    switch (_definition[i].Type)
                    {
                        case TrackType.EasyLeft:
                            _lapCenter -= (_definition[i].Length * _curveScale) / 2;
                            break;
                        case TrackType.Left:
                            _lapCenter -= (_definition[i].Length * _curveScale) * 2 / 3;
                            break;
                        case TrackType.HardLeft:
                            _lapCenter -= _definition[i].Length * _curveScale;
                            break;
                        case TrackType.HairpinLeft:
                            _lapCenter -= (_definition[i].Length * _curveScale) * 3 / 2;
                            break;
                        case TrackType.EasyRight:
                            _lapCenter += (_definition[i].Length * _curveScale) / 2;
                            break;
                        case TrackType.Right:
                            _lapCenter += (_definition[i].Length * _curveScale) * 2 / 3;
                            break;
                        case TrackType.HardRight:
                            _lapCenter += _definition[i].Length * _curveScale;
                            break;
                        case TrackType.HairpinRight:
                            _lapCenter += (_definition[i].Length * _curveScale) * 3 / 2;
                            break;
                    }
                }
            }

            if (_weather == TrackWeather.Rain)
                _soundRain?.Play(loop: true);
            else if (_weather == TrackWeather.Wind)
                _soundWind?.Play(loop: true);
            else if (_weather == TrackWeather.Storm)
                _soundStorm?.Play(loop: true);

            if (_ambience == TrackAmbience.Desert)
                _soundDesert?.Play(loop: true);
            else if (_ambience == TrackAmbience.Airport)
                _soundAirport?.Play(loop: true);
        }

        public void FinalizeTrack()
        {
            if (_weather == TrackWeather.Rain)
                _soundRain?.Stop();
            else if (_weather == TrackWeather.Wind)
                _soundWind?.Stop();
            else if (_weather == TrackWeather.Storm)
                _soundStorm?.Stop();

            if (_ambience == TrackAmbience.Desert)
                _soundDesert?.Stop();
            else if (_ambience == TrackAmbience.Airport)
                _soundAirport?.Stop();
        }

        public void Run(float position)
        {
            if (IsLayout)
            {
                Run(CreatePositionFromDistance(position));
                return;
            }

            Run(new TrackPosition(-1, 0f, position));
        }

        public void Run(TrackPosition position)
        {
            if (_noisePlaying && position.DistanceMeters > _noiseEndPos)
                _noisePlaying = false;

            if (IsLayout)
            {
                if (_lapDistance == 0)
                    Initialize();
                if (_lapDistance <= 0f || _edgeRuntime == null)
                    return;

                var normalized = NormalizePosition(position);
                var runtime = _edgeRuntime[normalized.EdgeIndex];
                var noise = runtime.Edge.Profile.NoiseAt(normalized.EdgeMeters);
                if (noise != _currentNoise)
                {
                    _noisePlaying = false;
                    _currentNoise = noise;
                }

                switch (noise)
                {
                    case TrackNoise.Crowd:
                        UpdateLoopingNoiseLayout(_soundCrowd, normalized.DistanceMeters, noise, normalized);
                        break;
                    case TrackNoise.Ocean:
                        UpdateLoopingNoiseLayout(_soundOcean, normalized.DistanceMeters, noise, normalized, pan: -10);
                        break;
                    case TrackNoise.Runway:
                        PlayIfNotPlaying(_soundAirplane);
                        break;
                    case TrackNoise.Clock:
                        UpdateLoopingNoiseLayout(_soundClock, normalized.DistanceMeters, noise, normalized, pan: 25);
                        break;
                    case TrackNoise.Jet:
                        PlayIfNotPlaying(_soundJet);
                        break;
                    case TrackNoise.Thunder:
                        PlayIfNotPlaying(_soundThunder);
                        break;
                    case TrackNoise.Pile:
                        UpdateLoopingNoiseLayout(_soundPile, normalized.DistanceMeters, noise, normalized);
                        break;
                    case TrackNoise.Construction:
                        UpdateLoopingNoiseLayout(_soundConstruction, normalized.DistanceMeters, noise, normalized);
                        break;
                    case TrackNoise.River:
                        UpdateLoopingNoiseLayout(_soundRiver, normalized.DistanceMeters, noise, normalized);
                        break;
                    case TrackNoise.Helicopter:
                        PlayIfNotPlaying(_soundHelicopter);
                        break;
                    case TrackNoise.Owl:
                        PlayIfNotPlaying(_soundOwl);
                        break;
                    default:
                        _soundCrowd?.Stop();
                        _soundOcean?.Stop();
                        _soundClock?.Stop();
                        _soundPile?.Stop();
                        _soundConstruction?.Stop();
                        _soundRiver?.Stop();
                        break;
                }
                return;
            }

            if (_segmentCount == 0)
                return;

            var legacyPosition = position.DistanceMeters;
            switch (_definition[_currentRoad].Noise)
            {
                case TrackNoise.Crowd:
                    UpdateLoopingNoise(_soundCrowd, legacyPosition);
                    break;
                case TrackNoise.Ocean:
                    UpdateLoopingNoise(_soundOcean, legacyPosition, pan: -10);
                    break;
                case TrackNoise.Runway:
                    PlayIfNotPlaying(_soundAirplane);
                    break;
                case TrackNoise.Clock:
                    UpdateLoopingNoise(_soundClock, legacyPosition, pan: 25);
                    break;
                case TrackNoise.Jet:
                    PlayIfNotPlaying(_soundJet);
                    break;
                case TrackNoise.Thunder:
                    PlayIfNotPlaying(_soundThunder);
                    break;
                case TrackNoise.Pile:
                    UpdateLoopingNoise(_soundPile, legacyPosition);
                    break;
                case TrackNoise.Construction:
                    UpdateLoopingNoise(_soundConstruction, legacyPosition);
                    break;
                case TrackNoise.River:
                    UpdateLoopingNoise(_soundRiver, legacyPosition);
                    break;
                case TrackNoise.Helicopter:
                    PlayIfNotPlaying(_soundHelicopter);
                    break;
                case TrackNoise.Owl:
                    PlayIfNotPlaying(_soundOwl);
                    break;
                default:
                    _soundCrowd?.Stop();
                    _soundOcean?.Stop();
                    _soundClock?.Stop();
                    _soundPile?.Stop();
                    _soundConstruction?.Stop();
                    _soundRiver?.Stop();
                    break;
            }
        }

        public Road RoadAtPosition(float position)
        {
            if (IsLayout)
                return RoadAtPosition(CreatePositionFromDistance(position));

            return RoadAtPosition(new TrackPosition(-1, 0f, position));
        }

        public Road RoadAtPosition(TrackPosition position)
        {
            if (_lapDistance == 0)
                Initialize();

            if (IsLayout)
            {
                return BuildLayoutRoad(position, updateState: true);
            }

            var lap = (int)(position.DistanceMeters / _lapDistance);
            var pos = WrapDistance(position.DistanceMeters);
            var dist = 0.0f;
            var center = lap * _lapCenter;

            for (var i = 0; i < _segmentCount; i++)
            {
                if (dist <= pos && dist + _definition[i].Length > pos)
                {
                    _prevRelPos = _relPos;
                    _relPos = pos - dist;
                    _currentRoad = i;
                    var road = new Road
                    {
                        Type = _definition[i].Type,
                        Surface = _definition[i].Surface,
                        Length = _definition[i].Length
                    };

                    ApplyRoadOffset(ref road, center, _relPos, _definition[i].Type);
                    return road;
                }

                center = UpdateCenter(center, _definition[i]);
                dist += _definition[i].Length;
            }

            return new Road { Left = 0, Right = 0, Surface = TrackSurface.Asphalt, Type = TrackType.Straight, Length = MinPartLengthMeters };
        }

        public Road RoadComputer(float position)
        {
            if (IsLayout)
                return RoadComputer(CreatePositionFromDistance(position));

            return RoadComputer(new TrackPosition(-1, 0f, position));
        }

        public Road RoadComputer(TrackPosition position)
        {
            if (_lapDistance == 0)
                Initialize();

            if (IsLayout)
            {
                return BuildLayoutRoad(position, updateState: false);
            }

            var lap = (int)(position.DistanceMeters / _lapDistance);
            var pos = WrapDistance(position.DistanceMeters);
            var dist = 0.0f;
            var center = lap * _lapCenter;
            var relPos = 0.0f;

            for (var i = 0; i < _segmentCount; i++)
            {
                if (dist <= pos && dist + _definition[i].Length > pos)
                {
                    relPos = pos - dist;
                    var road = new Road
                    {
                        Type = _definition[i].Type,
                        Surface = _definition[i].Surface,
                        Length = _definition[i].Length
                    };

                    ApplyRoadOffset(ref road, center, relPos, _definition[i].Type);
                    return road;
                }

                center = UpdateCenter(center, _definition[i]);
                dist += _definition[i].Length;
            }

            return new Road { Left = 0, Right = 0, Surface = TrackSurface.Asphalt, Type = TrackType.Straight, Length = MinPartLengthMeters };
        }

        private Road BuildLayoutRoad(TrackPosition position, bool updateState)
        {
            if (!IsLayout || _edgeRuntime == null)
                return new Road { Left = 0, Right = 0, Surface = TrackSurface.Asphalt, Type = TrackType.Straight, Length = MinPartLengthMeters };

            if (_lapDistance <= 0f)
                return new Road { Left = 0, Right = 0, Surface = TrackSurface.Asphalt, Type = TrackType.Straight, Length = MinPartLengthMeters };

            var normalized = NormalizePosition(position);
            var runtime = _edgeRuntime[normalized.EdgeIndex];
            var spanIndex = GetSpanIndex(runtime, normalized.EdgeMeters);
            var span = runtime.Spans[spanIndex];

            if (updateState)
            {
                _prevRelPos = _relPos;
                _relPos = normalized.EdgeMeters - runtime.SpanStartMeters[spanIndex];
                _currentRoad = spanIndex;
                _currentEdgeIndex = normalized.EdgeIndex;
                _currentSpanIndex = spanIndex;
            }

            var width = Math.Max(0.5f, runtime.Edge.Profile.WidthAt(normalized.EdgeMeters));
            var half = width * 0.5f;
            return new Road
            {
                Left = -half,
                Right = half,
                Surface = runtime.Edge.Profile.SurfaceAt(normalized.EdgeMeters),
                Type = ResolveCurveType(span),
                Length = span.LengthMeters
            };
        }

        private Road BuildLayoutRoadForIndex(int edgeIndex, int spanIndex)
        {
            if (!IsLayout || _edgeRuntime == null)
                return new Road { Left = 0, Right = 0, Surface = TrackSurface.Asphalt, Type = TrackType.Straight, Length = MinPartLengthMeters };

            if (edgeIndex < 0 || edgeIndex >= _edgeRuntime.Length)
                return new Road { Left = 0, Right = 0, Surface = TrackSurface.Asphalt, Type = TrackType.Straight, Length = MinPartLengthMeters };

            var runtime = _edgeRuntime[edgeIndex];
            if (spanIndex < 0 || spanIndex >= runtime.Spans.Length)
                return new Road { Left = 0, Right = 0, Surface = TrackSurface.Asphalt, Type = TrackType.Straight, Length = MinPartLengthMeters };

            var span = runtime.Spans[spanIndex];
            var sampleOffset = Math.Min(0.25f, span.LengthMeters * 0.5f);
            var pos = Clamp(runtime.SpanStartMeters[spanIndex] + sampleOffset, 0f, runtime.LengthMeters);
            var width = Math.Max(0.5f, runtime.Edge.Profile.WidthAt(pos));
            var half = width * 0.5f;
            return new Road
            {
                Left = -half,
                Right = half,
                Surface = runtime.Edge.Profile.SurfaceAt(pos),
                Type = ResolveCurveType(span),
                Length = span.LengthMeters
            };
        }

        public bool NextRoad(float position, float speed, int curveAnnouncementMode, out Road road)
        {
            if (IsLayout)
                return NextRoad(CreatePositionFromDistance(position), speed, curveAnnouncementMode, out road);

            return NextRoad(new TrackPosition(-1, 0f, position), speed, curveAnnouncementMode, out road);
        }

        public bool NextRoad(TrackPosition position, float speed, int curveAnnouncementMode, out Road road)
        {
            road = new Road();
            if (_segmentCount == 0)
                return false;

            if (IsLayout)
            {
                return NextLayoutRoad(position, speed, curveAnnouncementMode, out road);
            }

            if (curveAnnouncementMode == 0)
            {
                var currentLength = _definition[_currentRoad].Length;
                if ((_relPos + _callLength > currentLength) && (_prevRelPos + _callLength <= currentLength))
                {
                    var next = _definition[(_currentRoad + 1) % _segmentCount];
                    road.Type = next.Type;
                    road.Surface = next.Surface;
                    road.Length = next.Length;
                    return true;
                }
                return false;
            }

            var lookAhead = _callLength + speed / 2;
            var roadAhead = RoadIndexAt(position.DistanceMeters + lookAhead);
            if (roadAhead < 0)
                return false;

            var delta = (roadAhead - _lastCalled + _segmentCount) % _segmentCount;
            if (delta > 0 && delta <= _segmentCount / 2)
            {
                var next = _definition[roadAhead];
                road.Type = next.Type;
                road.Surface = next.Surface;
                road.Length = next.Length;
                _lastCalled = roadAhead;
                return true;
            }

            return false;
        }

        private bool NextLayoutRoad(TrackPosition position, float speed, int curveAnnouncementMode, out Road road)
        {
            road = new Road();
            if (!IsLayout || _edgeRuntime == null)
                return false;

            if (_lapDistance == 0)
                Initialize();

            if (_segmentCount == 0)
                return false;

            if (curveAnnouncementMode == 0)
            {
                var runtime = _edgeRuntime[_currentEdgeIndex];
                var currentLength = runtime.Spans[_currentSpanIndex].LengthMeters;
                if ((_relPos + _callLength > currentLength) && (_prevRelPos + _callLength <= currentLength))
                {
                    var nextSpan = GetNextSpanKey(_currentEdgeIndex, _currentSpanIndex);
                    if (nextSpan.EdgeIndex >= 0)
                    {
                        road = BuildLayoutRoadForIndex(nextSpan.EdgeIndex, nextSpan.SpanIndex);
                        return road.Length > 0f;
                    }
                }
                return false;
            }

            var lookAhead = _callLength + speed / 2;
            var aheadPos = Advance(position, lookAhead, branchHint: 0f);
            if (!aheadPos.IsGraphPosition)
                return false;
            var runtimeAhead = _edgeRuntime[aheadPos.EdgeIndex];
            var spanIndex = GetSpanIndex(runtimeAhead, aheadPos.EdgeMeters);
            var key = new TrackSpanKey(aheadPos.EdgeIndex, spanIndex);
            if (_lastCalledSpan.EdgeIndex < 0 || !key.Equals(_lastCalledSpan))
            {
                road = BuildLayoutRoadForIndex(key.EdgeIndex, key.SpanIndex);
                _lastCalledSpan = key;
                return road.Length > 0f;
            }

            return false;
        }

        private int RoadIndexAt(float position)
        {
            if (_lapDistance == 0)
                Initialize();

            var pos = WrapDistance(position);
            var dist = 0.0f;
            for (var i = 0; i < _segmentCount; i++)
            {
                if (dist <= pos && dist + _definition[i].Length > pos)
                    return i;
                dist += _definition[i].Length;
            }
            return -1;
        }

        private int LayoutSpanIndexAt(float position)
        {
            if (_layoutSpanStart == null || _layoutSpanStart.Length == 0)
                return -1;

            var pos = WrapDistance(position);
            var index = Array.BinarySearch(_layoutSpanStart, pos);
            if (index >= 0)
                return index;
            index = ~index - 1;
            if (index < 0)
                index = 0;
            if (index >= _layoutSpanStart.Length)
                index = _layoutSpanStart.Length - 1;
            return index;
        }

        private void BuildGraphRuntime()
        {
            if (_layout == null)
                return;

            var graph = _layout.Graph;
            _graphNodes = new TrackGraphNode[graph.Nodes.Count];
            for (var i = 0; i < graph.Nodes.Count; i++)
                _graphNodes[i] = graph.Nodes[i];

            _graphEdges = new TrackGraphEdge[graph.Edges.Count];
            for (var i = 0; i < graph.Edges.Count; i++)
                _graphEdges[i] = graph.Edges[i];

            _nodeIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _graphNodes.Length; i++)
                _nodeIndexById[_graphNodes[i].Id] = i;

            _edgeIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _graphEdges.Length; i++)
                _edgeIndexById[_graphEdges[i].Id] = i;

            _primaryEdgeIndex = 0;
            if (graph.PrimaryRoute.EdgeIds.Count > 0 && _edgeIndexById.TryGetValue(graph.PrimaryRoute.EdgeIds[0], out var primaryIndex))
                _primaryEdgeIndex = primaryIndex;

            _edgeRuntime = new EdgeRuntime[_graphEdges.Length];
            for (var i = 0; i < _graphEdges.Length; i++)
            {
                var edge = _graphEdges[i];
                var spans = new TrackGeometrySpan[edge.Geometry.Spans.Count];
                for (var s = 0; s < spans.Length; s++)
                    spans[s] = edge.Geometry.Spans[s];
                var spanStart = BuildSpanStart(spans);
                var geometry = TrackGeometry.Build(edge.Geometry);
                _edgeRuntime[i] = new EdgeRuntime(edge, geometry, spans, spanStart);
            }

            _nodeOutgoing = new List<int>[_graphNodes.Length];
            _nodeIncoming = new List<int>[_graphNodes.Length];
            for (var i = 0; i < _graphNodes.Length; i++)
            {
                _nodeOutgoing[i] = new List<int>();
                _nodeIncoming[i] = new List<int>();
            }

            for (var i = 0; i < _graphEdges.Length; i++)
            {
                var edge = _graphEdges[i];
                if (!_nodeIndexById.TryGetValue(edge.FromNodeId, out var fromIndex))
                    continue;
                if (!_nodeIndexById.TryGetValue(edge.ToNodeId, out var toIndex))
                    continue;
                _nodeOutgoing[fromIndex].Add(i);
                _nodeIncoming[toIndex].Add(i);
            }

            var nodePositions = new Vector3[_graphNodes.Length];
            var nodeHeadings = new float[_graphNodes.Length];
            var nodeSet = new bool[_graphNodes.Length];
            var edgeBuilt = new bool[_graphEdges.Length];
            var queue = new Queue<int>();
            var component = 0;

            void SeedNode(int nodeIndex)
            {
                nodePositions[nodeIndex] = new Vector3(component * DisconnectedNodeSpacingMeters, 0f, 0f);
                nodeHeadings[nodeIndex] = 0f;
                nodeSet[nodeIndex] = true;
                queue.Enqueue(nodeIndex);
            }

            if (_graphEdges.Length > 0 && _nodeIndexById.TryGetValue(_graphEdges[_primaryEdgeIndex].FromNodeId, out var startNode))
                SeedNode(startNode);

            while (queue.Count > 0)
            {
                var nodeIndex = queue.Dequeue();
                var nodePos = nodePositions[nodeIndex];
                var nodeHeading = nodeHeadings[nodeIndex];
                foreach (var edgeIndex in _nodeOutgoing[nodeIndex])
                {
                    if (!edgeBuilt[edgeIndex])
                    {
                        ApplyEdgeTransform(edgeIndex, nodePos, nodeHeading);
                        edgeBuilt[edgeIndex] = true;
                    }

                    var edge = _graphEdges[edgeIndex];
                    if (_nodeIndexById.TryGetValue(edge.ToNodeId, out var toIndex) && !nodeSet[toIndex])
                    {
                        var runtime = _edgeRuntime[edgeIndex];
                        nodePositions[toIndex] = runtime.EndPosition;
                        nodeHeadings[toIndex] = runtime.EndHeadingRadians;
                        nodeSet[toIndex] = true;
                        queue.Enqueue(toIndex);
                    }
                }
            }

            for (var i = 0; i < _graphNodes.Length; i++)
            {
                if (nodeSet[i])
                    continue;
                component++;
                SeedNode(i);

                while (queue.Count > 0)
                {
                    var nodeIndex = queue.Dequeue();
                    var nodePos = nodePositions[nodeIndex];
                    var nodeHeading = nodeHeadings[nodeIndex];
                    foreach (var edgeIndex in _nodeOutgoing[nodeIndex])
                    {
                        if (!edgeBuilt[edgeIndex])
                        {
                            ApplyEdgeTransform(edgeIndex, nodePos, nodeHeading);
                            edgeBuilt[edgeIndex] = true;
                        }

                        var edge = _graphEdges[edgeIndex];
                        if (_nodeIndexById.TryGetValue(edge.ToNodeId, out var toIndex) && !nodeSet[toIndex])
                        {
                            var runtime = _edgeRuntime[edgeIndex];
                            nodePositions[toIndex] = runtime.EndPosition;
                            nodeHeadings[toIndex] = runtime.EndHeadingRadians;
                            nodeSet[toIndex] = true;
                            queue.Enqueue(toIndex);
                        }
                    }
                }
            }

            for (var i = 0; i < _graphEdges.Length; i++)
            {
                if (edgeBuilt[i])
                    continue;
                var edge = _graphEdges[i];
                if (_nodeIndexById.TryGetValue(edge.FromNodeId, out var fromIndex) && nodeSet[fromIndex])
                {
                    ApplyEdgeTransform(i, nodePositions[fromIndex], nodeHeadings[fromIndex]);
                    edgeBuilt[i] = true;
                }
            }
        }

        private TrackSpanKey GetNextSpanKey(int edgeIndex, int spanIndex)
        {
            if (_edgeRuntime == null)
                return new TrackSpanKey(-1, -1);

            if (edgeIndex < 0 || edgeIndex >= _edgeRuntime.Length)
                return new TrackSpanKey(-1, -1);

            var runtime = _edgeRuntime[edgeIndex];
            if (spanIndex + 1 < runtime.Spans.Length)
                return new TrackSpanKey(edgeIndex, spanIndex + 1);

            var nextEdge = SelectNextEdge(edgeIndex, 0f);
            if (nextEdge < 0)
                return new TrackSpanKey(-1, -1);
            return new TrackSpanKey(nextEdge, 0);
        }

        private int SelectNextEdge(int currentEdgeIndex, float branchHint)
        {
            if (_graphEdges == null || _nodeOutgoing == null || _nodeIndexById == null)
                return -1;

            var edge = _graphEdges[currentEdgeIndex];
            if (!_nodeIndexById.TryGetValue(edge.ToNodeId, out var nodeIndex))
                return -1;
            var candidates = _nodeOutgoing[nodeIndex];
            return ChooseEdgeByHeading(currentEdgeIndex, candidates, branchHint, forward: true);
        }

        private int SelectPreviousEdge(int currentEdgeIndex, float branchHint)
        {
            if (_graphEdges == null || _nodeIncoming == null || _nodeIndexById == null)
                return -1;

            var edge = _graphEdges[currentEdgeIndex];
            if (!_nodeIndexById.TryGetValue(edge.FromNodeId, out var nodeIndex))
                return -1;
            var candidates = _nodeIncoming[nodeIndex];
            return ChooseEdgeByHeading(currentEdgeIndex, candidates, branchHint, forward: false);
        }

        private int ChooseEdgeByHeading(int currentEdgeIndex, List<int> candidates, float branchHint, bool forward)
        {
            if (_edgeRuntime == null || candidates == null || candidates.Count == 0)
                return -1;
            if (candidates.Count == 1)
                return candidates[0];

            var currentHeading = forward
                ? _edgeRuntime[currentEdgeIndex].EndHeadingRadians
                : NormalizeRadians(_edgeRuntime[currentEdgeIndex].HeadingRadians + (float)Math.PI);

            var bestIndex = candidates[0];
            if (branchHint > 0.1f)
            {
                var bestDelta = float.NegativeInfinity;
                foreach (var candidate in candidates)
                {
                    var candidateHeading = forward
                        ? _edgeRuntime[candidate].HeadingRadians
                        : NormalizeRadians(_edgeRuntime[candidate].EndHeadingRadians + (float)Math.PI);
                    var delta = NormalizeRadians(candidateHeading - currentHeading);
                    if (delta > bestDelta)
                    {
                        bestDelta = delta;
                        bestIndex = candidate;
                    }
                }
                return bestIndex;
            }

            if (branchHint < -0.1f)
            {
                var bestDelta = float.PositiveInfinity;
                foreach (var candidate in candidates)
                {
                    var candidateHeading = forward
                        ? _edgeRuntime[candidate].HeadingRadians
                        : NormalizeRadians(_edgeRuntime[candidate].EndHeadingRadians + (float)Math.PI);
                    var delta = NormalizeRadians(candidateHeading - currentHeading);
                    if (delta < bestDelta)
                    {
                        bestDelta = delta;
                        bestIndex = candidate;
                    }
                }
                return bestIndex;
            }

            var bestAbs = float.PositiveInfinity;
            foreach (var candidate in candidates)
            {
                var candidateHeading = forward
                    ? _edgeRuntime[candidate].HeadingRadians
                    : NormalizeRadians(_edgeRuntime[candidate].EndHeadingRadians + (float)Math.PI);
                var delta = NormalizeRadians(candidateHeading - currentHeading);
                var abs = Math.Abs(delta);
                if (abs < bestAbs)
                {
                    bestAbs = abs;
                    bestIndex = candidate;
                }
            }

            return bestIndex;
        }

        private TrackPosition NormalizePosition(TrackPosition position)
        {
            if (!IsLayout || _edgeRuntime == null)
                return position;

            if (!position.IsGraphPosition)
                return CreatePositionFromDistance(position.DistanceMeters);

            var edgeIndex = position.EdgeIndex;
            if (edgeIndex < 0 || edgeIndex >= _edgeRuntime.Length)
                return CreatePositionFromDistance(position.DistanceMeters);

            var clampedS = Clamp(position.EdgeMeters, 0f, _edgeRuntime[edgeIndex].LengthMeters);
            return new TrackPosition(edgeIndex, clampedS, position.DistanceMeters);
        }

        private static float[] BuildSpanStart(TrackGeometrySpan[] spans)
        {
            var start = new float[spans.Length];
            var total = 0f;
            for (var i = 0; i < spans.Length; i++)
            {
                start[i] = total;
                total += spans[i].LengthMeters;
            }
            return start;
        }

        private static int GetSpanIndex(EdgeRuntime runtime, float sMeters)
        {
            var s = Clamp(sMeters, 0f, runtime.LengthMeters);
            var index = Array.BinarySearch(runtime.SpanStartMeters, s);
            if (index >= 0)
                return index;
            index = ~index - 1;
            if (index < 0)
                index = 0;
            if (index >= runtime.SpanStartMeters.Length)
                index = runtime.SpanStartMeters.Length - 1;
            return index;
        }

        private void ApplyEdgeTransform(int edgeIndex, Vector3 origin, float headingRadians)
        {
            if (_edgeRuntime == null)
                return;

            var runtime = _edgeRuntime[edgeIndex];
            runtime.Origin = origin;
            runtime.HeadingRadians = headingRadians;
            var endPose = runtime.Geometry.GetPoseClamped(runtime.LengthMeters);
            runtime.EndPosition = origin + RotateY(endPose.Position, headingRadians);
            runtime.EndHeadingRadians = NormalizeRadians(headingRadians + endPose.HeadingRadians);
        }

        private static TrackPose TransformPose(TrackPose localPose, Vector3 origin, float headingRadians)
        {
            var position = origin + RotateY(localPose.Position, headingRadians);
            var tangent = Vector3.Normalize(RotateY(localPose.Tangent, headingRadians));
            var right = Vector3.Normalize(RotateY(localPose.Right, headingRadians));
            var up = Vector3.Normalize(RotateY(localPose.Up, headingRadians));
            var heading = NormalizeRadians(localPose.HeadingRadians + headingRadians);
            return new TrackPose(position, tangent, right, up, heading, localPose.BankRadians);
        }

        private static Vector3 RotateY(Vector3 value, float radians)
        {
            var sin = (float)Math.Sin(radians);
            var cos = (float)Math.Cos(radians);
            return new Vector3(
                value.X * cos + value.Z * sin,
                value.Y,
                -value.X * sin + value.Z * cos);
        }

        private static float NormalizeRadians(float radians)
        {
            var twoPi = (float)(Math.PI * 2.0);
            radians %= twoPi;
            if (radians > Math.PI)
                radians -= twoPi;
            else if (radians < -Math.PI)
                radians += twoPi;
            return radians;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private TrackType ResolveCurveType(TrackGeometrySpan span)
        {
            if (span.Kind != TrackGeometrySpanKind.Arc || span.Direction == TrackCurveDirection.Straight)
                return TrackType.Straight;
            if (!span.CurveSeverity.HasValue)
                return TrackType.Straight;

            return span.Direction switch
            {
                TrackCurveDirection.Left => span.CurveSeverity.Value switch
                {
                    TrackCurveSeverity.Easy => TrackType.EasyLeft,
                    TrackCurveSeverity.Normal => TrackType.Left,
                    TrackCurveSeverity.Hard => TrackType.HardLeft,
                    TrackCurveSeverity.Hairpin => TrackType.HairpinLeft,
                    _ => TrackType.Left
                },
                TrackCurveDirection.Right => span.CurveSeverity.Value switch
                {
                    TrackCurveSeverity.Easy => TrackType.EasyRight,
                    TrackCurveSeverity.Normal => TrackType.Right,
                    TrackCurveSeverity.Hard => TrackType.HardRight,
                    TrackCurveSeverity.Hairpin => TrackType.HairpinRight,
                    _ => TrackType.Right
                },
                _ => TrackType.Straight
            };
        }

        private float WrapDistance(float position)
        {
            if (_lapDistance <= 0f)
                return 0f;
            var wrapped = position % _lapDistance;
            if (wrapped < 0f)
                wrapped += _lapDistance;
            if (wrapped == _lapDistance)
                return 0f;
            return wrapped;
        }

        private void CalculateNoiseLength()
        {
            _noiseLength = 0;
            var i = _currentRoad;
            while (i < _segmentCount && _definition[i].Noise == _definition[_currentRoad].Noise)
            {
                _noiseLength += _definition[i].Length;
                i++;
            }
            _noisePlaying = true;
        }

        private void CalculateNoiseLengthLayout(TrackPosition position, TrackNoise noise)
        {
            _noiseLength = 0f;
            if (!IsLayout || _edgeRuntime == null)
            {
                _noisePlaying = false;
                return;
            }

            var normalized = NormalizePosition(position);
            var runtime = _edgeRuntime[normalized.EdgeIndex];
            var start = 0f;
            var end = runtime.LengthMeters;
            var found = false;
            for (var i = 0; i < runtime.Edge.Profile.NoiseZones.Count; i++)
            {
                var zone = runtime.Edge.Profile.NoiseZones[i];
                if (zone.Value == noise && zone.Contains(normalized.EdgeMeters))
                {
                    start = zone.StartMeters;
                    end = zone.EndMeters;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                var currentNoise = runtime.Edge.Profile.NoiseAt(normalized.EdgeMeters);
                if (noise == currentNoise)
                {
                    start = 0f;
                    end = runtime.LengthMeters;
                    found = true;
                }
                else
                {
                    _noisePlaying = false;
                    return;
                }
            }

            _noiseLength = Math.Max(0f, end - start);
            if (_noiseLength <= 0f)
            {
                _noisePlaying = false;
                return;
            }

            var offset = normalized.EdgeMeters - start;
            _noiseStartPos = normalized.DistanceMeters - offset;
            _noiseEndPos = _noiseStartPos + _noiseLength;
            _noisePlaying = true;
        }

        private void UpdateLoopingNoise(AudioSourceHandle? sound, float position, int? pan = null)
        {
            if (sound == null)
                return;

            if (!_noisePlaying)
            {
                CalculateNoiseLength();
                _noiseStartPos = position;
                _noiseEndPos = position + _noiseLength;
            }

            _factor = (position - _noiseStartPos) * 1.0f / _noiseLength;
            if (_factor < 0.5f)
                _factor *= 2.0f;
            else
                _factor = 2.0f * (1.0f - _factor);

            SetVolumePercent(sound, (int)(80.0f + _factor * 20.0f));
            if (!sound.IsPlaying)
            {
                if (pan.HasValue)
                    sound.SetPan(pan.Value / 100f);
                sound.Play(loop: true);
            }
        }

        private void UpdateLoopingNoiseLayout(AudioSourceHandle? sound, float position, TrackNoise noise, TrackPosition trackPosition, int? pan = null)
        {
            if (sound == null)
                return;

            if (!_noisePlaying)
            {
                CalculateNoiseLengthLayout(trackPosition, noise);
                if (_noiseLength <= 0f)
                    return;
            }

            _factor = (position - _noiseStartPos) * 1.0f / _noiseLength;
            if (_factor < 0.5f)
                _factor *= 2.0f;
            else
                _factor = 2.0f * (1.0f - _factor);

            SetVolumePercent(sound, (int)(80.0f + _factor * 20.0f));
            if (!sound.IsPlaying)
            {
                if (pan.HasValue)
                    sound.SetPan(pan.Value / 100f);
                sound.Play(loop: true);
            }
        }

        private static void PlayIfNotPlaying(AudioSourceHandle? sound)
        {
            if (sound == null)
                return;
            if (!sound.IsPlaying)
                sound.Play(loop: false);
        }

        private static void SetVolumePercent(AudioSourceHandle sound, int volume)
        {
            var clamped = Math.Max(0, Math.Min(100, volume));
            sound.SetVolume(clamped / 100f);
        }

        private void InitializeSounds()
        {
            var root = Path.Combine(AssetPaths.SoundsRoot, "Legacy");
            _soundCrowd = CreateLegacySound(root, "crowd.wav");
            _soundOcean = CreateLegacySound(root, "ocean.wav");
            _soundRain = CreateLegacySound(root, "rain.wav");
            _soundWind = CreateLegacySound(root, "wind.wav");
            _soundStorm = CreateLegacySound(root, "storm.wav");
            _soundDesert = CreateLegacySound(root, "desert.wav");
            _soundAirport = CreateLegacySound(root, "airport.wav");
            _soundAirplane = CreateLegacySound(root, "airplane.wav");
            _soundClock = CreateLegacySound(root, "clock.wav");
            _soundJet = CreateLegacySound(root, "jet.wav");
            _soundThunder = CreateLegacySound(root, "thunder.wav");
            _soundPile = CreateLegacySound(root, "pile.wav");
            _soundConstruction = CreateLegacySound(root, "const.wav");
            _soundRiver = CreateLegacySound(root, "river.wav");
            _soundHelicopter = CreateLegacySound(root, "helicopter.wav");
            _soundOwl = CreateLegacySound(root, "owl.wav");
        }

        private AudioSourceHandle? CreateLegacySound(string root, string file)
        {
            var path = Path.Combine(root, file);
            if (!File.Exists(path))
                return null;
            return _audio.CreateLoopingSource(path);
        }

        private float UpdateCenter(float center, TrackDefinition definition)    
        {
            switch (definition.Type)
            {
                case TrackType.EasyLeft:
                    return center - (definition.Length * _curveScale) / 2;
                case TrackType.Left:
                    return center - (definition.Length * _curveScale) * 2 / 3;
                case TrackType.HardLeft:
                    return center - definition.Length * _curveScale;
                case TrackType.HairpinLeft:
                    return center - (definition.Length * _curveScale) * 3 / 2;
                case TrackType.EasyRight:
                    return center + (definition.Length * _curveScale) / 2;
                case TrackType.Right:
                    return center + (definition.Length * _curveScale) * 2 / 3;
                case TrackType.HardRight:
                    return center + definition.Length * _curveScale;
                case TrackType.HairpinRight:
                    return center + (definition.Length * _curveScale) * 3 / 2;
                default:
                    return center;
            }
        }

        private void ApplyRoadOffset(ref Road road, float center, float relPos, TrackType type)
        {
            var offset = relPos * _curveScale;
            switch (type)
            {
                case TrackType.Straight:
                    road.Left = center - _laneWidth;
                    road.Right = center + _laneWidth;
                    break;
                case TrackType.EasyLeft:
                    road.Left = center - _laneWidth - offset / 2;
                    road.Right = center + _laneWidth - offset / 2;
                    break;
                case TrackType.Left:
                    road.Left = center - _laneWidth - offset * 2 / 3;
                    road.Right = center + _laneWidth - offset * 2 / 3;
                    break;
                case TrackType.HardLeft:
                    road.Left = center - _laneWidth - offset;
                    road.Right = center + _laneWidth - offset;
                    break;
                case TrackType.HairpinLeft:
                    road.Left = center - _laneWidth - offset * 3 / 2;
                    road.Right = center + _laneWidth - offset * 3 / 2;
                    break;
                case TrackType.EasyRight:
                    road.Left = center - _laneWidth + offset / 2;
                    road.Right = center + _laneWidth + offset / 2;
                    break;
                case TrackType.Right:
                    road.Left = center - _laneWidth + offset * 2 / 3;
                    road.Right = center + _laneWidth + offset * 2 / 3;
                    break;
                case TrackType.HardRight:
                    road.Left = center - _laneWidth + offset;
                    road.Right = center + _laneWidth + offset;
                    break;
                case TrackType.HairpinRight:
                    road.Left = center - _laneWidth + offset * 3 / 2;
                    road.Right = center + _laneWidth + offset * 3 / 2;
                    break;
                default:
                    road.Left = center - _laneWidth;
                    road.Right = center + _laneWidth;
                    break;
            }
        }

        private void UpdateCurveScale()
        {
            _curveScale = LegacyLaneWidthMeters > 0f ? _laneWidth / LegacyLaneWidthMeters : 1.0f;
            if (_curveScale <= 0f)
                _curveScale = 0.01f;
        }

        private static string ResolveCustomTrackName(string path, string? name)
        {
            var trimmedName = name?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedName))
                return trimmedName!;
            var fileName = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        }

        private static bool TryParseCustomTrackName(string line, out string name)
        {
            name = string.Empty;
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var trimmed = line.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) ||
                trimmed.StartsWith(";", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1).TrimStart();
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex < 0)
                separatorIndex = trimmed.IndexOf(':');
            if (separatorIndex <= 0)
                return false;

            var key = trimmed.Substring(0, separatorIndex).Trim();
            if (!key.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("trackname", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var value = trimmed.Substring(separatorIndex + 1).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
                return false;

            name = value;
            return true;
        }

        private static TrackData ReadCustomTrackData(string filename)
        {
            if (!File.Exists(filename))
            {
                return new TrackData(true, TrackWeather.Sunny, TrackAmbience.NoAmbience,
                    new[] { new TrackDefinition(TrackType.Straight, TrackSurface.Asphalt, TrackNoise.NoNoise, MinPartLengthMeters) });
            }

            var ints = new List<int>();
            string? name = null;
            foreach (var line in File.ReadLines(filename))
            {
                var trimmed = line.Trim();
                if (TryParseCustomTrackName(trimmed, out var parsedName))
                {
                    if (string.IsNullOrWhiteSpace(name))
                        name = parsedName;
                    continue;
                }

                AppendIntsFromLine(trimmed, ints);
            }

            var length = 0;
            var index = 0;
            var minPartLengthLegacy = 5000;

            while (index < ints.Count)
            {
                var first = ints[index++];
                if (first < 0)
                    break;
                if (index < ints.Count) index++;
                if (index >= ints.Count) break;
                var third = ints[index++];
                if (third < minPartLengthLegacy && index < ints.Count)
                    index++;
                length++;
            }

            if (length == 0)
            {
                return new TrackData(true, TrackWeather.Sunny, TrackAmbience.NoAmbience,
                    new[] { new TrackDefinition(TrackType.Straight, TrackSurface.Asphalt, TrackNoise.NoNoise, MinPartLengthMeters) },
                    name: name);
            }

            var definitions = new TrackDefinition[length];
            index = 0;
            for (var i = 0; i < length; i++)
            {
                var typeValue = index < ints.Count ? ints[index++] : 0;
                var surfaceValue = index < ints.Count ? ints[index++] : 0;
                var temp = index < ints.Count ? ints[index++] : 0;

                var noiseValue = 0;
                var lengthValueLegacy = 0;
                if (temp < Noises)
                {
                    noiseValue = temp;
                    lengthValueLegacy = index < ints.Count ? ints[index++] : minPartLengthLegacy;
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
                    lengthValueLegacy = temp;
                }

                if (typeValue >= Types)
                    typeValue = 0;
                if (surfaceValue >= Surfaces)
                    surfaceValue = 0;
                if (noiseValue >= Noises)
                    noiseValue = 0;
                if (lengthValueLegacy < minPartLengthLegacy)
                    lengthValueLegacy = minPartLengthLegacy;

                definitions[i] = new TrackDefinition((TrackType)typeValue, (TrackSurface)surfaceValue, (TrackNoise)noiseValue, lengthValueLegacy / 100.0f);
            }

            if (index < ints.Count)
                index++; // skip -1

            var weatherValue = index < ints.Count ? ints[index++] : 0;
            if (weatherValue < 0)
                weatherValue = 0;
            var ambienceValue = index < ints.Count ? ints[index++] : 0;
            if (ambienceValue < 0)
                ambienceValue = 0;

            var weather = (TrackWeather)weatherValue;
            var ambience = (TrackAmbience)ambienceValue;
            return new TrackData(true, weather, ambience, definitions, name: name);
        }

        private static int AppendIntsFromLine(string line, List<int> values)
        {
            if (string.IsNullOrWhiteSpace(line))
                return 0;

            var trimmed = line.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) ||
                trimmed.StartsWith(";", StringComparison.Ordinal))
            {
                return 0;
            }

            var added = 0;
            var parts = trimmed.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (int.TryParse(part, out var value))
                {
                    values.Add(value);
                    added++;
                }
            }

            return added;
        }

        public void Dispose()
        {
            FinalizeTrack();
            DisposeSound(_soundCrowd);
            DisposeSound(_soundOcean);
            DisposeSound(_soundRain);
            DisposeSound(_soundWind);
            DisposeSound(_soundStorm);
            DisposeSound(_soundDesert);
            DisposeSound(_soundAirport);
            DisposeSound(_soundAirplane);
            DisposeSound(_soundClock);
            DisposeSound(_soundJet);
            DisposeSound(_soundThunder);
            DisposeSound(_soundPile);
            DisposeSound(_soundConstruction);
            DisposeSound(_soundRiver);
            DisposeSound(_soundHelicopter);
            DisposeSound(_soundOwl);
        }

        private static void DisposeSound(AudioSourceHandle? sound)
        {
            if (sound == null)
                return;
            sound.Stop();
            sound.Dispose();
        }
    }
}
