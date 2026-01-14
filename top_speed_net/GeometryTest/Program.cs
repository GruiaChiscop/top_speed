using System;
using System.Collections.Generic;
using System.Numerics;
using TopSpeed.Tracks.Geometry;

namespace TopSpeed.GeometryTest
{
    internal static class Program
    {
        private const float Epsilon = 0.001f;

        public static int Main(string[] args)
        {
            try
            {
                // If a path is provided, validate just that file
                ParseArgs(args, out var path, out var verbosity);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    Console.WriteLine($"Validating: {path}");
                    return RunSingleFile(path, verbosity) ? 0 : 1;
                }

                // Otherwise run all standard tests
                var geometry = BuildTestGeometry();
                var ok = RunChecks(geometry, "Generated geometry", expectedLength: 940f, expectBothCurvatures: true);
                var layoutOk = RunLayoutCheck("sample_layout.ttl", expectedLength: 1100f, expectBothCurvatures: true);
                var realisticOk = RunLayoutCheck("realistic_layout.ttl", expectedLength: 3480f, expectBothCurvatures: true);
                var loaderOk = RunLoaderCheck("realistic_layout.ttl", expectedLength: 3480f);
                var allLayoutsOk = RunAllLayouts();
                return ok && layoutOk && realisticOk && loaderOk && allLayoutsOk ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Geometry test failed with exception:");
                Console.WriteLine(ex);
                return 1;
            }
        }

        private static bool RunSingleFile(string path, int verbosity)
        {
            if (!System.IO.File.Exists(path))
            {
                Console.WriteLine($"File not found: {path}");
                return false;
            }

            var result = TrackLayoutFormat.ParseFile(path);
            if (!result.IsSuccess || result.Layout == null)
            {
                Console.WriteLine("[Parse] Failed:");
                foreach (var error in result.Errors)
                    Console.WriteLine($"  {error}");
                return false;
            }
            Console.WriteLine("[Parse] OK");
            var layout = result.Layout;
            if (layout == null)
            {
                Console.WriteLine("[Parse] No layout produced.");
                return false;
            }

            if (verbosity > 0)
                PrintVerboseLayout(layout, verbosity);

            var validation = TrackLayoutValidator.Validate(layout);
            PrintValidation(validation, System.IO.Path.GetFileName(path));
            
            if (!validation.IsValid)
            {
                Console.WriteLine("[Validation] FAILED - has errors");
                return false;
            }

            var geometry = TrackGeometry.Build(layout.Geometry);
            Console.WriteLine($"[Geometry] Built successfully");
            Console.WriteLine($"  Length: {geometry.LengthMeters:0.###}m");
            Console.WriteLine($"  Spans: {result.Layout.Geometry.Spans.Count}");
            Console.WriteLine($"  Sample Spacing: {geometry.SampleSpacingMeters:0.###}m");

            // Check closure
            var startPose = geometry.GetPose(0f);
            var endPose = geometry.GetPose(geometry.LengthMeters);
            var closureDistance = System.Numerics.Vector3.Distance(startPose.Position, endPose.Position);
            var headingDelta = Math.Abs(NormalizeAngle(endPose.HeadingRadians - startPose.HeadingRadians));
            var closureOk = closureDistance < 0.5f && headingDelta < 0.1f;
            Console.WriteLine($"[Closure] Distance: {closureDistance:0.###}m, Heading delta: {headingDelta:0.###} rad -> {(closureOk ? "OK" : "WARN")}");

            // Check for both curve directions
            var hasBothCurvatures = CheckCurvatureSamples(geometry, true, out var curvatureSummary);
            Console.WriteLine($"[Curvature] {curvatureSummary} -> {(hasBothCurvatures ? "OK" : "WARN")}");

            Console.WriteLine();
            Console.WriteLine(closureOk ? "=== VALIDATION PASSED ===" : "=== VALIDATION PASSED (with warnings) ===");
            return true;
        }

        private static TrackGeometry BuildTestGeometry()
        {
            var spans = new[]
            {
                TrackGeometrySpan.Straight(100f),
                TrackGeometrySpan.Clothoid(60f, 0f, 80f, TrackCurveDirection.Right, TrackCurveSeverity.Normal),
                TrackGeometrySpan.Arc(200f, 80f, TrackCurveDirection.Right, TrackCurveSeverity.Hard),
                TrackGeometrySpan.Clothoid(60f, 80f, 0f, TrackCurveDirection.Right, TrackCurveSeverity.Normal),
                TrackGeometrySpan.Straight(100f),
                TrackGeometrySpan.Clothoid(60f, 0f, 80f, TrackCurveDirection.Left, TrackCurveSeverity.Normal),
                TrackGeometrySpan.Arc(200f, 80f, TrackCurveDirection.Left, TrackCurveSeverity.Hard),
                TrackGeometrySpan.Clothoid(60f, 80f, 0f, TrackCurveDirection.Left, TrackCurveSeverity.Normal),
                TrackGeometrySpan.Straight(100f)
            };

            var spec = new TrackGeometrySpec(spans, sampleSpacingMeters: 0.5f, enforceClosure: true);
            return TrackGeometry.Build(spec);
        }

        private static bool RunChecks(TrackGeometry geometry, string label, float expectedLength, bool expectBothCurvatures)
        {
            var lengthOk = Math.Abs(geometry.LengthMeters - expectedLength) < 0.01f;
            Console.WriteLine($"[{label}] Length: {geometry.LengthMeters:0.###} (expected {expectedLength:0.###}) -> {(lengthOk ? "OK" : "FAIL")}");

            var startPose = geometry.GetPose(0f);
            var endPose = geometry.GetPose(geometry.LengthMeters);
            var closureDistance = Vector3.Distance(startPose.Position, endPose.Position);
            var headingDelta = Math.Abs(NormalizeAngle(endPose.HeadingRadians - startPose.HeadingRadians));
            var closureOk = closureDistance < 0.01f && headingDelta < 0.01f;
            Console.WriteLine($"[{label}] Closure: dist={closureDistance:0.#####}, headingΔ={headingDelta:0.#####} -> {(closureOk ? "OK" : "FAIL")}");

            var pose = geometry.GetPose(geometry.LengthMeters * 0.25f);
            var tangentOk = IsUnit(pose.Tangent);
            var rightOk = IsUnit(pose.Right);
            var upOk = IsUnit(pose.Up);
            var orthoOk = Math.Abs(Vector3.Dot(pose.Tangent, pose.Right)) < 0.001f &&
                          Math.Abs(Vector3.Dot(pose.Tangent, pose.Up)) < 0.001f &&
                          Math.Abs(Vector3.Dot(pose.Right, pose.Up)) < 0.001f;
            Console.WriteLine($"[{label}] Basis: T={tangentOk}, R={rightOk}, U={upOk}, orthogonal={orthoOk} -> {(tangentOk && rightOk && upOk && orthoOk ? "OK" : "FAIL")}");

            var edges = geometry.GetEdges(geometry.LengthMeters * 0.6f, 12f);
            var width = Vector3.Distance(edges.Left, edges.Right);
            var widthOk = Math.Abs(width - 12f) < 0.05f;
            Console.WriteLine($"[{label}] Edges: width={width:0.###} -> {(widthOk ? "OK" : "FAIL")}");

            var curvatureOk = CheckCurvatureSamples(geometry, expectBothCurvatures, out var curvatureSummary);
            Console.WriteLine($"[{label}] Curvature: {curvatureSummary} -> {(curvatureOk ? "OK" : "FAIL")}");

            var allOk = lengthOk && closureOk && tangentOk && rightOk && upOk && orthoOk && widthOk && curvatureOk;
            Console.WriteLine(allOk ? $"[{label}] Geometry checks passed." : $"[{label}] Geometry checks failed.");
            return allOk;
        }

        private static bool RunLayoutCheck(string fileName, float expectedLength, bool expectBothCurvatures)
        {
            var baseDir = AppContext.BaseDirectory;
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "Tracks", fileName));
            if (!System.IO.File.Exists(path))
            {
                var fallback = System.IO.Path.GetFullPath(System.IO.Path.Combine("top_speed_net", "Tracks", fileName));
                if (System.IO.File.Exists(fallback))
                {
                    path = fallback;
                }
                else
                {
                    Console.WriteLine($"[Layout] Layout not found: {path}");
                    return false;
                }
            }

            var result = TrackLayoutFormat.ParseFile(path);
            if (!result.IsSuccess || result.Layout == null)
            {
                Console.WriteLine("[Layout] Parse failed:");
                foreach (var error in result.Errors)
                    Console.WriteLine(error.ToString());
                return false;
            }

            var validation = TrackLayoutValidator.Validate(result.Layout);
            PrintValidation(validation, fileName);
            if (!validation.IsValid)
                return false;

            var geometry = TrackGeometry.Build(result.Layout.Geometry);
            var ok = RunChecks(geometry, $"Parsed layout ({fileName})", expectedLength: expectedLength, expectBothCurvatures: expectBothCurvatures);
            return ok;
        }

        private static bool RunLoaderCheck(string fileName, float expectedLength)
        {
            var baseDir = AppContext.BaseDirectory;
            var tracksPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "Tracks"));
            var source = new FileTrackLayoutSource(new[] { tracksPath });
            var loader = new TrackLayoutLoader(new[] { source });
            var result = loader.Load(new TrackLayoutLoadRequest(fileName, validate: true, buildGeometry: true, allowWarnings: true));

            if (!result.IsSuccess || result.Layout == null || result.Geometry == null)
            {
                Console.WriteLine("[Loader] Failed to load layout.");
                if (result.ParseErrors.Count > 0)
                {
                    Console.WriteLine("[Loader] Parse errors:");
                    foreach (var error in result.ParseErrors)
                        Console.WriteLine(error.ToString());
                }
                if (result.ValidationIssues.Count > 0)
                {
                    Console.WriteLine("[Loader] Validation issues:");
                    foreach (var issue in result.ValidationIssues)
                        Console.WriteLine(issue.ToString());
                }
                return false;
            }

            Console.WriteLine("[Loader] Load success.");
            return RunChecks(result.Geometry, $"Loader layout ({fileName})", expectedLength: expectedLength, expectBothCurvatures: true);
        }

        private static bool RunAllLayouts()
        {
            var baseDir = AppContext.BaseDirectory;
            var tracksPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "Tracks"));
            if (!System.IO.Directory.Exists(tracksPath))
            {
                var fallback = System.IO.Path.GetFullPath(System.IO.Path.Combine("top_speed_net", "Tracks"));
                if (!System.IO.Directory.Exists(fallback))
                {
                    Console.WriteLine("[Layouts] Tracks folder not found.");
                    return false;
                }
                tracksPath = fallback;
            }

            var files = System.IO.Directory.GetFiles(tracksPath, "*.ttl");
            if (files.Length == 0)
            {
                Console.WriteLine("[Layouts] No layouts found.");
                return false;
            }

            var allOk = true;
            foreach (var file in files)
            {
                var name = System.IO.Path.GetFileName(file);
                var result = TrackLayoutFormat.ParseFile(file);
                if (!result.IsSuccess || result.Layout == null)
                {
                    Console.WriteLine($"[Layouts] Parse failed: {name}");
                    foreach (var error in result.Errors)
                        Console.WriteLine(error.ToString());
                    allOk = false;
                    continue;
                }

                var validation = TrackLayoutValidator.Validate(result.Layout);
                PrintValidation(validation, name);
                if (!validation.IsValid)
                    allOk = false;
            }

            return allOk;
        }

        private static bool CheckCurvatureSamples(TrackGeometry geometry, bool expectBoth, out string summary)
        {
            var foundPositive = false;
            var foundNegative = false;
            var epsilon = 0.0005f;
            var length = geometry.LengthMeters;
            if (length <= 0f)
            {
                summary = "no length";
                return false;
            }

            var samples = 200;
            var step = Math.Max(1f, length / samples);
            for (var s = 0f; s <= length; s += step)
            {
                var curvature = geometry.CurvatureAt(s);
                if (curvature > epsilon) foundPositive = true;
                if (curvature < -epsilon) foundNegative = true;
                if (!expectBoth && (foundPositive || foundNegative))
                    break;
                if (expectBoth && foundPositive && foundNegative)
                    break;
            }

            if (expectBoth)
            {
                summary = $"positive={foundPositive}, negative={foundNegative}";
                return foundPositive && foundNegative;
            }

            summary = $"positive={foundPositive}, negative={foundNegative}";
            return foundPositive || foundNegative;
        }

        private static void PrintValidation(TrackLayoutValidationResult validation, string label)
        {
            if (validation.Issues.Count == 0)
            {
                Console.WriteLine($"[Validation {label}] OK");
                return;
            }

            Console.WriteLine($"[Validation {label}] Issues:");
            foreach (var issue in validation.Issues)
                Console.WriteLine(issue.ToString());
        }

        private static bool IsUnit(Vector3 vector)
        {
            var length = vector.Length();
            return Math.Abs(length - 1f) < 0.001f;
        }

        private static float NormalizeAngle(float angle)
        {
            var twoPi = (float)(Math.PI * 2.0);
            while (angle > Math.PI)
                angle -= twoPi;
            while (angle <= -Math.PI)
                angle += twoPi;
            return angle;
        }

        private static void ParseArgs(string[] args, out string? path, out int verbosity)
        {
            path = null;
            verbosity = 0;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.IsNullOrWhiteSpace(arg))
                    continue;

                if (arg.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                {
                    verbosity = Math.Max(verbosity, 1);
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var nextLevel))
                    {
                        verbosity = Math.Max(verbosity, nextLevel);
                        i++;
                    }
                    continue;
                }

                if (arg.StartsWith("--verbose=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = arg.Substring("--verbose=".Length);
                    verbosity = int.TryParse(value, out var level) ? level : Math.Max(verbosity, 1);
                    continue;
                }

                if (arg.StartsWith("-v=", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("-v:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = arg.Substring(3);
                    verbosity = int.TryParse(value, out var level) ? level : Math.Max(verbosity, 1);
                    continue;
                }

                if (path == null)
                {
                    path = arg;
                    continue;
                }
            }
        }

        private static void PrintVerboseLayout(TrackLayout layout, int verbosity)
        {
            Console.WriteLine("[Verbose] Layout summary:");
            Console.WriteLine($"  Name: {layout.Metadata.Name ?? "(none)"}");
            Console.WriteLine($"  Author: {layout.Metadata.Author ?? "(none)"}");
            Console.WriteLine($"  Version: {layout.Metadata.Version ?? "(none)"}");
            Console.WriteLine($"  Description: {layout.Metadata.Description ?? "(none)"}");
            Console.WriteLine($"  Tags: {(layout.Metadata.Tags.Count == 0 ? "(none)" : string.Join(", ", layout.Metadata.Tags))}");
            Console.WriteLine($"  Weather: {layout.Weather}");
            Console.WriteLine($"  Ambience: {layout.Ambience}");
            Console.WriteLine($"  Default Surface: {layout.DefaultSurface}");
            Console.WriteLine($"  Default Noise: {layout.DefaultNoise}");
            Console.WriteLine($"  Default Width: {layout.DefaultWidthMeters:0.###}m");
            Console.WriteLine($"  Geometry Spans: {layout.Geometry.Spans.Count}");
            Console.WriteLine($"  Sample Spacing: {layout.Geometry.SampleSpacingMeters:0.###}m");
            Console.WriteLine($"  Enforce Closure: {layout.Geometry.EnforceClosure}");
            Console.WriteLine($"  Graph: nodes={layout.Graph.Nodes.Count}, edges={layout.Graph.Edges.Count}, routes={layout.Graph.Routes.Count}");
            Console.WriteLine($"  Primary Route: {layout.PrimaryRoute.Id} (loop={layout.PrimaryRoute.IsLoop})");
            Console.WriteLine($"  Primary Route Edges: {string.Join(", ", layout.PrimaryRoute.EdgeIds)}");

            Console.WriteLine("[Verbose] Nodes:");
            foreach (var node in layout.Graph.Nodes)
            {
                Console.WriteLine($"  Node {node.Id} name={node.Name ?? "(none)"} short={node.ShortName ?? "(none)"} metadata={node.Metadata.Count}");
                if (node.Intersection != null)
                {
                    PrintIntersection(node.Id, node.Intersection, verbosity);
                }
            }

            Console.WriteLine("[Verbose] Edges:");
            foreach (var edge in layout.Graph.Edges)
            {
                Console.WriteLine($"  Edge {edge.Id} from={edge.FromNodeId} to={edge.ToNodeId} length={edge.LengthMeters:0.###}m");
                Console.WriteLine($"    Name={edge.Name ?? "(none)"} short={edge.ShortName ?? "(none)"} turn={edge.TurnDirection} connectors_from={edge.ConnectorFromEdgeIds.Count}");
                Console.WriteLine($"    Defaults: surface={edge.Profile.DefaultSurface}, noise={edge.Profile.DefaultNoise}, width={edge.Profile.DefaultWidthMeters:0.###}m, weather={edge.Profile.DefaultWeather}, ambience={edge.Profile.DefaultAmbience}");
                PrintGeometrySpans(edge.Geometry.Spans);
                PrintEdgeProfile(edge.Profile, verbosity);
            }
        }

        private static void PrintIntersection(string nodeId, TrackIntersectionProfile intersection, int verbosity)
        {
            Console.WriteLine($"    Intersection shape={intersection.Shape} control={intersection.Control} priority={intersection.Priority}");
            Console.WriteLine($"      Radii: outer={intersection.OuterRadiusMeters:0.###}m inner={intersection.InnerRadiusMeters:0.###}m radius={intersection.RadiusMeters:0.###}m");
            Console.WriteLine($"      Lanes: entry={intersection.EntryLanes} exit={intersection.ExitLanes} turn={intersection.TurnLanes}");
            Console.WriteLine($"      SpeedLimit: {intersection.SpeedLimitKph:0.###} kph");
            Console.WriteLine($"      Legs={intersection.Legs.Count}, Connectors={intersection.Connectors.Count}, Lanes={intersection.Lanes.Count}, LaneLinks={intersection.LaneLinks.Count}, Areas={intersection.Areas.Count}");

            if (verbosity < 1)
                return;

            for (var i = 0; i < intersection.Legs.Count; i++)
            {
                var leg = intersection.Legs[i];
                Console.WriteLine($"      Leg[{i}] id={leg.Id} edge={leg.EdgeId} type={leg.LegType} lanes={leg.LaneCount} width={leg.WidthMeters:0.###}m approach={leg.ApproachLengthMeters:0.###}m heading={leg.HeadingDegrees:0.###} offset=({leg.OffsetXMeters:0.###},{leg.OffsetZMeters:0.###}) elev={leg.ElevationMeters:0.###}m speed={leg.SpeedLimitKph:0.###} kph priority={leg.Priority}");
            }

            for (var i = 0; i < intersection.Connectors.Count; i++)
            {
                var connector = intersection.Connectors[i];
                Console.WriteLine($"      Connector[{i}] id={connector.Id} from={connector.FromLegId} to={connector.ToLegId} turn={connector.TurnDirection} lanes={connector.LaneCount} radius={connector.RadiusMeters:0.###}m length={connector.LengthMeters:0.###}m speed={connector.SpeedLimitKph:0.###} kph bank={connector.BankDegrees:0.###} cross={connector.CrossSlopeDegrees:0.###} priority={connector.Priority}");
                if (verbosity > 1)
                {
                    PrintPointList("        Path", connector.PathPoints);
                    PrintProfileList("        Profile", connector.Profile);
                }
            }

            for (var i = 0; i < intersection.Lanes.Count; i++)
            {
                var lane = intersection.Lanes[i];
                Console.WriteLine($"      Lane[{i}] id={lane.Id} owner={lane.OwnerKind}:{lane.OwnerId} index={lane.Index} width={lane.WidthMeters:0.###}m offset={lane.CenterOffsetMeters:0.###}m shoulders=({lane.ShoulderLeftMeters:0.###},{lane.ShoulderRightMeters:0.###}) type={lane.LaneType} dir={lane.Direction} markings=({lane.MarkingLeft},{lane.MarkingRight}) entry={lane.EntryHeadingDegrees:0.###} exit={lane.ExitHeadingDegrees:0.###} bank={lane.BankDegrees:0.###} cross={lane.CrossSlopeDegrees:0.###} speed={lane.SpeedLimitKph:0.###} kph surface={lane.Surface} priority={lane.Priority}");
                if (verbosity > 1)
                {
                    PrintPointList("        Centerline", lane.CenterlinePoints);
                    PrintPointList("        LeftEdge", lane.LeftEdgePoints);
                    PrintPointList("        RightEdge", lane.RightEdgePoints);
                    PrintProfileList("        Profile", lane.Profile);
                }
            }

            for (var i = 0; i < intersection.LaneLinks.Count; i++)
            {
                var link = intersection.LaneLinks[i];
                Console.WriteLine($"      LaneLink[{i}] id={link.Id} from={link.FromLaneId} to={link.ToLaneId} turn={link.TurnDirection} lane_change={link.AllowsLaneChange} change_len={link.ChangeLengthMeters:0.###}m priority={link.Priority}");
            }

            for (var i = 0; i < intersection.Areas.Count; i++)
            {
                var area = intersection.Areas[i];
                Console.WriteLine($"      Area[{i}] id={area.Id} shape={area.Shape} kind={area.Kind} radius={area.RadiusMeters:0.###}m size=({area.WidthMeters:0.###}x{area.LengthMeters:0.###}) offset=({area.OffsetXMeters:0.###},{area.OffsetZMeters:0.###}) heading={area.HeadingDegrees:0.###} elev={area.ElevationMeters:0.###}m surface={area.Surface}");
                if (verbosity > 1)
                {
                    PrintPointList("        Points", area.Points);
                }
            }
        }

        private static void PrintGeometrySpans(IReadOnlyList<TrackGeometrySpan> spans)
        {
            Console.WriteLine($"    Geometry spans={spans.Count}");
            for (var i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                Console.WriteLine($"      Span[{i}] kind={span.Kind} len={span.LengthMeters:0.###}m dir={span.Direction} radius={span.RadiusMeters:0.###}m start_radius={span.StartRadiusMeters:0.###}m end_radius={span.EndRadiusMeters:0.###}m elev_delta={span.ElevationDeltaMeters:0.###}m slope=({span.StartSlope:0.###}->{span.EndSlope:0.###}) bank=({span.BankStartDegrees:0.###}->{span.BankEndDegrees:0.###}) severity={(span.CurveSeverity?.ToString() ?? "none")}");
            }
        }

        private static void PrintEdgeProfile(TrackEdgeProfile profile, int verbosity)
        {
            Console.WriteLine($"    Zones: surface={profile.SurfaceZones.Count}, noise={profile.NoiseZones.Count}, width={profile.WidthZones.Count}, speed={profile.SpeedLimitZones.Count}, weather={profile.WeatherZones.Count}, ambience={profile.AmbienceZones.Count}, hazards={profile.Hazards.Count}, checkpoints={profile.Checkpoints.Count}, hit_lanes={profile.HitLanes.Count}, emitters={profile.Emitters.Count}, triggers={profile.Triggers.Count}, boundaries={profile.BoundaryZones.Count}, markers={profile.Markers.Count}");

            if (verbosity < 1)
                return;

            for (var i = 0; i < profile.WidthZones.Count; i++)
            {
                var zone = profile.WidthZones[i];
                Console.WriteLine($"      Width[{i}] {zone.StartMeters:0.###}-{zone.EndMeters:0.###}m width={zone.WidthMeters:0.###}m shoulder=({zone.ShoulderLeftMeters:0.###},{zone.ShoulderRightMeters:0.###})");
            }

            for (var i = 0; i < profile.SurfaceZones.Count; i++)
            {
                var zone = profile.SurfaceZones[i];
                Console.WriteLine($"      Surface[{i}] {zone.StartMeters:0.###}-{zone.EndMeters:0.###}m value={zone.Value}");
            }

            for (var i = 0; i < profile.NoiseZones.Count; i++)
            {
                var zone = profile.NoiseZones[i];
                Console.WriteLine($"      Noise[{i}] {zone.StartMeters:0.###}-{zone.EndMeters:0.###}m value={zone.Value}");
            }

            for (var i = 0; i < profile.SpeedLimitZones.Count; i++)
            {
                var zone = profile.SpeedLimitZones[i];
                Console.WriteLine($"      Speed[{i}] {zone.StartMeters:0.###}-{zone.EndMeters:0.###}m max={zone.MaxSpeedKph:0.###} kph");
            }

            for (var i = 0; i < profile.Markers.Count; i++)
            {
                var marker = profile.Markers[i];
                Console.WriteLine($"      Marker[{i}] {marker.Name} at {marker.PositionMeters:0.###}m");
            }

            if (verbosity > 1)
            {
                PrintWeatherZones(profile.WeatherZones);
                PrintAmbienceZones(profile.AmbienceZones);
                PrintHazardZones(profile.Hazards);
                PrintCheckpoints(profile.Checkpoints);
                PrintHitLanes(profile.HitLanes);
                PrintEmitters(profile.Emitters);
                PrintTriggers(profile.Triggers);
                PrintBoundaryZones(profile.BoundaryZones);
                if (profile.AllowedVehicles.Count > 0)
                    Console.WriteLine($"      AllowedVehicles: {string.Join(", ", profile.AllowedVehicles)}");
            }
        }

        private static void PrintWeatherZones(IReadOnlyList<TrackWeatherZone> zones)
        {
            for (var i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                Console.WriteLine($"      Weather[{i}] {zone.StartMeters:0.###}-{zone.EndMeters:0.###}m {zone.Weather}");
            }
        }

        private static void PrintAmbienceZones(IReadOnlyList<TrackAmbienceZone> zones)
        {
            for (var i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                Console.WriteLine($"      Ambience[{i}] {zone.StartMeters:0.###}-{zone.EndMeters:0.###}m {zone.Ambience}");
            }
        }

        private static void PrintHazardZones(IReadOnlyList<TrackHazardZone> zones)
        {
            for (var i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                Console.WriteLine($"      Hazard[{i}] {zone.StartMeters:0.###}-{zone.EndMeters:0.###}m type={zone.HazardType} severity={zone.Severity:0.###} name={zone.Name ?? "(none)"}");
            }
        }

        private static void PrintCheckpoints(IReadOnlyList<TrackCheckpoint> checkpoints)
        {
            for (var i = 0; i < checkpoints.Count; i++)
            {
                var checkpoint = checkpoints[i];
                Console.WriteLine($"      Checkpoint[{i}] id={checkpoint.Id} at {checkpoint.PositionMeters:0.###}m name={checkpoint.Name ?? "(none)"}");
            }
        }

        private static void PrintHitLanes(IReadOnlyList<TrackHitLaneZone> lanes)
        {
            for (var i = 0; i < lanes.Count; i++)
            {
                var lane = lanes[i];
                Console.WriteLine($"      HitLane[{i}] {lane.StartMeters:0.###}-{lane.EndMeters:0.###}m lanes={string.Join(",", lane.LaneIndices)} effect={lane.Effect ?? "(none)"}");
            }
        }

        private static void PrintEmitters(IReadOnlyList<TrackAudioEmitter> emitters)
        {
            for (var i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                Console.WriteLine($"      Emitter[{i}] id={emitter.Id} at {emitter.PositionMeters:0.###}m radius={emitter.RadiusMeters:0.###}m sound={emitter.SoundKey ?? "(none)"} volume={emitter.Volume:0.###} loop={emitter.Loop}");
            }
        }

        private static void PrintTriggers(IReadOnlyList<TrackTriggerZone> triggers)
        {
            for (var i = 0; i < triggers.Count; i++)
            {
                var trigger = triggers[i];
                Console.WriteLine($"      Trigger[{i}] {trigger.StartMeters:0.###}-{trigger.EndMeters:0.###}m id={trigger.Id} action={trigger.Action ?? "(none)"} payload={trigger.Payload ?? "(none)"}");
            }
        }

        private static void PrintBoundaryZones(IReadOnlyList<TrackBoundaryZone> boundaries)
        {
            for (var i = 0; i < boundaries.Count; i++)
            {
                var boundary = boundaries[i];
                Console.WriteLine($"      Boundary[{i}] {boundary.StartMeters:0.###}-{boundary.EndMeters:0.###}m side={boundary.Side} type={boundary.BoundaryType} offset={boundary.OffsetMeters:0.###}m width={boundary.WidthMeters:0.###}m height={boundary.HeightMeters:0.###}m solid={boundary.IsSolid} severity={boundary.Severity:0.###}");
            }
        }

        private static void PrintPointList(string label, IReadOnlyList<TrackPoint3> points)
        {
            if (points.Count == 0)
                return;

            Console.WriteLine($"{label} points={points.Count}");
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                Console.WriteLine($"{label}[{i}] ({point.X:0.###},{point.Y:0.###},{point.Z:0.###})");
            }
        }

        private static void PrintProfileList(string label, IReadOnlyList<TrackProfilePoint> points)
        {
            if (points.Count == 0)
                return;

            Console.WriteLine($"{label} points={points.Count}");
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                Console.WriteLine($"{label}[{i}] s={point.SMeters:0.###} elev={point.ElevationMeters:0.###} bank={point.BankDegrees:0.###} cross={point.CrossSlopeDegrees:0.###}");
            }
        }
    }
}
