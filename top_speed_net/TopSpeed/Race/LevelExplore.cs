using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SharpDX.DirectInput;
using TopSpeed.Audio;
using TopSpeed.Core;
using TopSpeed.Data;
using TopSpeed.Input;
using TopSpeed.Speech;
using TopSpeed.Tracks.Areas;
using TopSpeed.Tracks.Guidance;
using TopSpeed.Tracks.Map;
using TopSpeed.Tracks.Topology;
using TS.Audio;

namespace TopSpeed.Race
{
    internal sealed class LevelExplore : IDisposable
    {
        private static readonly float[] StepSizes = { 1f, 5f, 10f, 20f, 30f, 50f, 100f };
        private const float WidthAnnounceThreshold = 0.5f;
        private const float ApproachBeaconRangeMeters = 50f;
        private const float DefaultApproachToleranceDegrees = 10f;

        private readonly AudioManager _audio;
        private readonly SpeechService _speech;
        private readonly RaceSettings _settings;
        private readonly InputManager _input;
        private readonly TrackMap _map;

        private Vector3 _worldPosition;
        private int _stepIndex;
        private bool _initialized;
        private bool _exitRequested;
        private Vector3 _listenerForward = Vector3.UnitZ;
        private MapDirection _mapHeading = MapDirection.North;
        private MapMovementState _mapState;
        private MapSnapshot _mapSnapshot;
        private TrackAreaManager? _areaManager;
        private TrackApproachBeacon? _approachBeacon;
        private AudioSourceHandle? _soundBeacon;
        private string? _lastApproachPortalId;
        private string? _lastApproachHeading;
        private TrackPathManager? _pathManager;
        private float _beaconCooldown;

        private Vector3 _lastListenerPosition;
        private bool _listenerInitialized;

        public LevelExplore(
            AudioManager audio,
            SpeechService speech,
            RaceSettings settings,
            InputManager input,
            string track)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _speech = speech ?? throw new ArgumentNullException(nameof(speech));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _input = input ?? throw new ArgumentNullException(nameof(input));

            if (!TrackMapLoader.TryResolvePath(track, out var mapPath))
                throw new FileNotFoundException("Track map not found.", track);

            _map = TrackMapLoader.Load(mapPath);
            _stepIndex = 1; // default 5 meters
        }

        public bool WantsExit => _exitRequested;

        public void Initialize()
        {
            _mapState = MapMovement.CreateStart(_map);
            _mapHeading = MapDirection.North;
            _mapState.Heading = _mapHeading;
            _mapState.HeadingDegrees = 0f;
            _worldPosition = _mapState.WorldPosition;
            _listenerForward = Vector3.UnitZ;
            _areaManager = _map.BuildAreaManager();
            _pathManager = _map.BuildPathManager();
            _approachBeacon = new TrackApproachBeacon(_map, ApproachBeaconRangeMeters);
            InitializeBeacon();
            _mapSnapshot = BuildMapSnapshot(_mapState.CellX, _mapState.CellZ, _mapHeading);
            _speech.Speak($"Track {FormatTrackName(_map.Name)}.");
            _speech.Speak($"Step {StepSizes[_stepIndex]:0.#} meters.");
            _initialized = true;
        }

        public void Run(float elapsed)
        {
            if (!_initialized)
                return;

            if (_input.WasPressed(Key.Escape))
                _exitRequested = true;

            HandleStepAdjust();
            HandleCoordinateKeys();
            HandleMovement();
            UpdateApproachGuidance(elapsed);
            UpdateAudioListener(elapsed);
        }

        public void Dispose()
        {
            if (_soundBeacon != null)
            {
                _soundBeacon.Stop();
                _soundBeacon.Dispose();
            }
        }

        private void HandleStepAdjust()
        {
            if (!_input.WasPressed(Key.Back))
                return;

            var shift = _input.IsDown(Key.LeftShift) || _input.IsDown(Key.RightShift);
            if (shift)
            {
                if (_stepIndex > 0)
                    _stepIndex--;
            }
            else
            {
                if (_stepIndex < StepSizes.Length - 1)
                    _stepIndex++;
            }

            _speech.Speak($"Step {StepSizes[_stepIndex]:0.#} meters.");
        }

        private void HandleCoordinateKeys()
        {
            if (_input.WasPressed(Key.K))
                _speech.Speak($"X {Math.Round(_worldPosition.X, 2):0.##} meters.");
            if (_input.WasPressed(Key.L))
                _speech.Speak($"Z {Math.Round(_worldPosition.Z, 2):0.##} meters.");
        }

        private void HandleMovement()
        {
            if (_input.WasPressed(Key.Up))
            {
                AttemptMoveMap(StepSizes[_stepIndex], MapDirection.North);
                return;
            }

            if (_input.WasPressed(Key.Down))
            {
                AttemptMoveMap(StepSizes[_stepIndex], MapDirection.South);
                return;
            }

            if (_input.WasPressed(Key.Left))
            {
                AttemptMoveMap(StepSizes[_stepIndex], MapDirection.West);
                return;
            }

            if (_input.WasPressed(Key.Right))
            {
                AttemptMoveMap(StepSizes[_stepIndex], MapDirection.East);
            }
        }

        private void AttemptMoveMap(float distanceMeters, MapDirection direction)
        {
            var delta = MapMovement.DirectionVector(direction) * distanceMeters;
            var nextWorld = _worldPosition + delta;
            var (nextCellX, nextCellZ) = _map.WorldToCell(nextWorld);
            if (!IsWithinTrack(nextWorld))
            {
                _speech.Speak("Track boundary.");
                return;
            }

            _worldPosition = nextWorld;
            _mapState.CellX = nextCellX;
            _mapState.CellZ = nextCellZ;
            _mapState.WorldPosition = nextWorld;
            _mapHeading = MapDirection.North;
            _mapState.Heading = _mapHeading;
            _mapState.HeadingDegrees = 0f;
            _listenerForward = Vector3.UnitZ;

            var previous = _mapSnapshot;
            var current = BuildMapSnapshot(nextCellX, nextCellZ, _mapHeading);
            AnnounceMapChanges(previous, current);
            _mapSnapshot = current;
        }

        private void UpdateAudioListener(float elapsed)
        {
            var forward = _listenerForward.LengthSquared() > 0.0001f ? Vector3.Normalize(_listenerForward) : Vector3.UnitZ;
            var up = Vector3.UnitY;
            var velocity = Vector3.Zero;
            if (_listenerInitialized && elapsed > 0f)
                velocity = (_worldPosition - _lastListenerPosition) / elapsed;

            _lastListenerPosition = _worldPosition;
            _listenerInitialized = true;

            var position = AudioWorld.ToMeters(_worldPosition);
            var velocityMeters = AudioWorld.ToMeters(velocity);
            _audio.UpdateListener(position, forward, up, velocityMeters);
        }

        private MapSnapshot BuildMapSnapshot(int x, int z, MapDirection heading)
        {
            if (!_map.TryGetCell(x, z, out var cell))
            {
                return new MapSnapshot
                {
                    Surface = TrackSurface.Asphalt,
                    Noise = TrackNoise.NoNoise,
                    WidthMeters = 0f,
                    IsSafeZone = false,
                    Zone = string.Empty,
                    Exits = MapExits.None
                };
            }

            var snapshot = new MapSnapshot
            {
                Surface = cell.Surface,
                Noise = cell.Noise,
                WidthMeters = cell.WidthMeters,
                IsSafeZone = cell.IsSafeZone,
                Zone = cell.Zone ?? string.Empty,
                Exits = cell.Exits
            };

            var worldPosition = _map.CellToWorld(x, z);
            ApplyPathWidthSnapshot(new Vector2(worldPosition.X, worldPosition.Z), ref snapshot.WidthMeters);
            ApplyAreaSnapshotOverrides(new Vector2(worldPosition.X, worldPosition.Z), heading, ref snapshot);
            return snapshot;
        }

        private void ApplyAreaSnapshotOverrides(Vector2 position, MapDirection heading, ref MapSnapshot snapshot)
        {
            if (_areaManager == null)
                return;

            var areas = _areaManager.FindAreasContaining(position);
            if (areas.Count == 0)
                return;

            var area = areas[areas.Count - 1];
            if (area.Surface.HasValue)
                snapshot.Surface = area.Surface.Value;
            if (area.Noise.HasValue)
                snapshot.Noise = area.Noise.Value;
            if (area.WidthMeters.HasValue)
                snapshot.WidthMeters = Math.Max(0.5f, area.WidthMeters.Value);
            if (area.Type == TrackAreaType.SafeZone || (area.Flags & TrackAreaFlags.SafeZone) != 0)
                snapshot.IsSafeZone = true;

            if (!string.IsNullOrWhiteSpace(area.Name))
                snapshot.Zone = area.Name!;
            else if (!string.IsNullOrWhiteSpace(area.Id))
                snapshot.Zone = area.Id;

            if (!TryApplyAreaWidthFromMetadata(area, ref snapshot.WidthMeters))
                TryApplyAreaWidthFromShape(area, heading, ref snapshot.WidthMeters);
        }

        private static bool TryApplyAreaWidthFromMetadata(TrackAreaDefinition area, ref float widthMeters)
        {
            if (area.Metadata == null || area.Metadata.Count == 0)
                return false;

            if (TryGetMetadataFloat(area.Metadata, out var widthValue, "intersection_width", "width", "lane_width"))
            {
                widthMeters = Math.Max(0.5f, widthValue);
                return true;
            }

            return false;
        }

        private void TryApplyAreaWidthFromShape(TrackAreaDefinition area, MapDirection heading, ref float widthMeters)
        {
            if (_areaManager == null)
                return;
            if (!_areaManager.TryGetShape(area.ShapeId, out var shape))
                return;

            switch (shape.Type)
            {
                case ShapeType.Rectangle:
                    var rectWidth = Math.Abs(shape.Width);
                    var rectHeight = Math.Abs(shape.Height);
                    if (rectWidth <= 0f || rectHeight <= 0f)
                        return;
                    widthMeters = heading == MapDirection.East || heading == MapDirection.West
                        ? Math.Max(widthMeters, rectHeight)
                        : Math.Max(widthMeters, rectWidth);
                    break;
                case ShapeType.Circle:
                    widthMeters = Math.Max(widthMeters, shape.Radius * 2f);
                    break;
            }
        }

        private static bool TryGetMetadataFloat(
            IReadOnlyDictionary<string, string> metadata,
            out float value,
            params string[] keys)
        {
            value = 0f;
            foreach (var key in keys)
            {
                if (!metadata.TryGetValue(key, out var raw))
                    continue;
                if (float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                    return true;
            }
            return false;
        }

        private void AnnounceMapChanges(MapSnapshot previous, MapSnapshot current)
        {
            if (previous.Surface != current.Surface)
                _speech.Speak($"{FormatSurface(current.Surface)} surface.");

            if (previous.Noise != current.Noise)
                _speech.Speak($"{FormatNoise(current.Noise)} zone.");

            if (Math.Abs(previous.WidthMeters - current.WidthMeters) >= WidthAnnounceThreshold)
                _speech.Speak($"Width {Math.Round(current.WidthMeters, 1):0.#} meters.");

            if (previous.IsSafeZone != current.IsSafeZone)
            {
                if (current.IsSafeZone)
                    _speech.Speak("Safe zone.");
                else
                    _speech.Speak("Leaving safe zone.");
            }

            if (!string.Equals(previous.Zone, current.Zone, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(current.Zone))
                    _speech.Speak($"{current.Zone}.");
                else if (!string.IsNullOrWhiteSpace(previous.Zone))
                    _speech.Speak("Leaving zone.");
            }

            var previousCurve = DescribeCurve(previous.Exits, _mapHeading);
            var currentCurve = DescribeCurve(current.Exits, _mapHeading);
            if (!string.Equals(previousCurve, currentCurve, StringComparison.OrdinalIgnoreCase))
                _speech.Speak(currentCurve);

            var wasIntersection = IsIntersection(previous.Exits);
            var isIntersection = IsIntersection(current.Exits);
            if (wasIntersection != isIntersection)
            {
                if (isIntersection)
                    _speech.Speak("Intersection.");
                else
                    _speech.Speak("Leaving intersection.");
            }
        }

        private void ApplyPathWidthSnapshot(Vector2 position, ref float widthMeters)
        {
            if (_pathManager == null || !_pathManager.HasPaths)
                return;

            var paths = _pathManager.FindPathsContaining(position);
            if (paths.Count == 0)
                return;

            var path = paths[paths.Count - 1];
            if (path.WidthMeters > 0f)
                widthMeters = Math.Max(0.5f, path.WidthMeters);
        }

        private void InitializeBeacon()
        {
            var path = Path.Combine(AssetPaths.SoundsRoot, "Legacy", "beacon.wav");
            if (!File.Exists(path))
                return;
            _soundBeacon = _audio.CreateSpatialSource(path, streamFromDisk: true, allowHrtf: true);
        }

        private void UpdateApproachGuidance(float elapsed)
        {
            if (_approachBeacon == null || _soundBeacon == null)
                return;

            var headingDegrees = 0f;
            if (_approachBeacon.TryGetCue(_worldPosition, headingDegrees, out var cue) && !cue.Passed)
            {
                var position = AudioWorld.ToMeters(new Vector3(cue.PortalPosition.X, 0f, cue.PortalPosition.Y));
                _soundBeacon.SetPosition(position);
                _soundBeacon.SetVelocity(Vector3.Zero);
                _beaconCooldown -= elapsed;
                if (_beaconCooldown <= 0f)
                {
                    _soundBeacon.Stop();
                    _soundBeacon.SeekToStart();
                    _soundBeacon.Play(loop: false);
                    _beaconCooldown = 1.5f;
                }

                var tolerance = cue.ToleranceDegrees ?? DefaultApproachToleranceDegrees;
                var headingText = FormatHeadingShort(cue.TargetHeadingDegrees);
                if (cue.DeltaDegrees > tolerance)
                {
                    if (!string.Equals(_lastApproachPortalId, cue.PortalId, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(_lastApproachHeading, headingText, StringComparison.OrdinalIgnoreCase))
                    {
                        _speech.Speak($"Turn {headingText}.");
                        _lastApproachPortalId = cue.PortalId;
                        _lastApproachHeading = headingText;
                    }
                }

                return;
            }

            _beaconCooldown = 0f;
            if (_soundBeacon.IsPlaying)
                _soundBeacon.Stop();
            _lastApproachPortalId = null;
            _lastApproachHeading = null;
        }

        private bool IsWithinTrack(Vector3 worldPosition)
        {
            var position = new Vector2(worldPosition.X, worldPosition.Z);
            var (cellX, cellZ) = _map.WorldToCell(worldPosition);
            var safeZone = IsSafeZone(position);
            if (_map.TryGetCell(cellX, cellZ, out var cell) && cell.IsSafeZone)
                safeZone = true;

            if (_pathManager != null && _pathManager.HasPaths)
            {
                if (_pathManager.ContainsAny(position))
                    return true;
                return safeZone;
            }

            return _map.TryGetCell(cellX, cellZ, out _);
        }

        private bool IsSafeZone(Vector2 position)
        {
            if (_areaManager == null)
                return false;

            var areas = _areaManager.FindAreasContaining(position);
            if (areas.Count == 0)
                return false;

            foreach (var area in areas)
            {
                if (area.Type == TrackAreaType.SafeZone || (area.Flags & TrackAreaFlags.SafeZone) != 0)
                    return true;
            }
            return false;
        }


        private static string FormatHeadingShort(float degrees)
        {
            var normalized = degrees % 360f;
            if (normalized < 0f)
                normalized += 360f;

            if (normalized >= 315f || normalized < 45f)
                return "north";
            if (normalized >= 45f && normalized < 135f)
                return "east";
            if (normalized >= 135f && normalized < 225f)
                return "south";
            return "west";
        }

        private static bool IsIntersection(MapExits exits)
        {
            return CountExits(exits) >= 3;
        }

        private static int CountExits(MapExits exits)
        {
            var count = 0;
            if ((exits & MapExits.North) != 0) count++;
            if ((exits & MapExits.East) != 0) count++;
            if ((exits & MapExits.South) != 0) count++;
            if ((exits & MapExits.West) != 0) count++;
            return count;
        }

        private static string DescribeCurve(MapExits exits, MapDirection heading)
        {
            if (exits == MapExits.None)
                return "Off track.";

            var count = CountExits(exits);
            if (count >= 3)
                return "Straight.";

            if (count == 2)
            {
                var straight = exits == (MapExits.North | MapExits.South) || exits == (MapExits.East | MapExits.West);
                if (straight)
                    return "Straight.";

                var right = IsRightTurn(exits, heading);
                return right ? "Right curve." : "Left curve.";
            }

            return "Dead end.";
        }

        private static bool IsRightTurn(MapExits exits, MapDirection heading)
        {
            return heading switch
            {
                MapDirection.North => (exits & MapExits.East) != 0,
                MapDirection.East => (exits & MapExits.South) != 0,
                MapDirection.South => (exits & MapExits.West) != 0,
                MapDirection.West => (exits & MapExits.North) != 0,
                _ => false
            };
        }

        private static string FormatTrackName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Track";
            return name.Replace('_', ' ').Replace('-', ' ').Trim();
        }

        private static string FormatSurface(TrackSurface surface)
        {
            return surface switch
            {
                TrackSurface.Asphalt => "Asphalt",
                TrackSurface.Gravel => "Gravel",
                TrackSurface.Water => "Water",
                TrackSurface.Sand => "Sand",
                TrackSurface.Snow => "Snow",
                _ => "Surface"
            };
        }

        private static string FormatNoise(TrackNoise noise)
        {
            return noise switch
            {
                TrackNoise.Crowd => "Crowd",
                TrackNoise.Ocean => "Ocean",
                TrackNoise.Trackside => "Trackside",
                TrackNoise.Clock => "Clock",
                TrackNoise.Jet => "Jet",
                TrackNoise.Thunder => "Thunder",
                TrackNoise.Pile => "Construction",
                TrackNoise.Construction => "Construction",
                TrackNoise.River => "River",
                TrackNoise.Helicopter => "Helicopter",
                TrackNoise.Owl => "Owl",
                _ => "Quiet"
            };
        }


        private struct MapSnapshot
        {
            public TrackSurface Surface;
            public TrackNoise Noise;
            public float WidthMeters;
            public bool IsSafeZone;
            public string Zone;
            public MapExits Exits;
        }
    }
}
