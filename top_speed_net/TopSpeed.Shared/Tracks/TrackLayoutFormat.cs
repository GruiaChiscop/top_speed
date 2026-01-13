using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TopSpeed.Data;

namespace TopSpeed.Tracks.Geometry
{
    public sealed class TrackLayoutError
    {
        public int LineNumber { get; }
        public string Message { get; }
        public string? LineText { get; }

        public TrackLayoutError(int lineNumber, string message, string? lineText = null)
        {
            LineNumber = lineNumber;
            Message = message;
            LineText = lineText;
        }

        public override string ToString()
        {
            return LineText == null ? $"{LineNumber}: {Message}" : $"{LineNumber}: {Message} -> {LineText}";
        }
    }

    public sealed class TrackLayoutParseResult
    {
        public TrackLayout? Layout { get; }
        public IReadOnlyList<TrackLayoutError> Errors { get; }
        public bool IsSuccess => Errors.Count == 0 && Layout != null;

        public TrackLayoutParseResult(TrackLayout? layout, IReadOnlyList<TrackLayoutError> errors)
        {
            Layout = layout;
            Errors = errors;
        }
    }

    public static class TrackLayoutFormat
    {
        public const string FileExtension = ".ttl";
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public static TrackLayoutParseResult ParseFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is required.", nameof(path));

            var lines = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
            return ParseLines(lines, path);
        }

                public static TrackLayoutParseResult ParseLines(IEnumerable<string> lines, string? sourceName = null)
        {
            var errors = new List<TrackLayoutError>();
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var nodes = new Dictionary<string, NodeBuilder>(StringComparer.OrdinalIgnoreCase);
            var edges = new Dictionary<string, EdgeBuilder>(StringComparer.OrdinalIgnoreCase);
            var routes = new List<RouteBuilder>();
            var edgeOrder = new List<string>();

            var section = SectionInfo.Empty;
            var lineNumber = 0;

            foreach (var rawLine in lines)
            {
                lineNumber++;
                var line = StripComment(rawLine);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (TryParseSection(line, out var nextSection))
                {
                    section = nextSection;
                    continue;
                }

                switch (section.Kind)
                {
                    case "meta":
                        ParseKeyValue(line, lineNumber, meta, errors);
                        break;
                    case "environment":
                        ParseKeyValue(line, lineNumber, environment, errors);
                        break;
                    case "nodes":
                        TryParseNodeLine(line, lineNumber, nodes, errors);
                        break;
                    case "edges":
                        TryParseEdgeLine(line, lineNumber, edges, edgeOrder, errors);
                        break;
                    case "routes":
                        TryParseRouteLine(line, lineNumber, routes, errors);
                        break;
                    case "edge":
                        if (string.IsNullOrWhiteSpace(section.EdgeId))
                        {
                            errors.Add(new TrackLayoutError(lineNumber, "Edge section missing id.", rawLine));
                            break;
                        }
                        var edge = GetOrCreateEdge(section.EdgeId!, edges, edgeOrder);
                        if (string.IsNullOrWhiteSpace(section.Subsection))
                        {
                            ParseEdgeProperties(line, lineNumber, edge, errors);
                        }
                        else
                        {
                            switch (section.Subsection)
                            {
                                case "geometry":
                                    TryParseGeometryLine(line, lineNumber, edge.GeometrySpans, errors);
                                    break;
                                case "width":
                                    TryParseWidthZone(line, lineNumber, edge.WidthZones, errors);
                                    break;
                                case "surface":
                                    TryParseSurfaceZone(line, lineNumber, edge.SurfaceZones, errors);
                                    break;
                                case "noise":
                                    TryParseNoiseZone(line, lineNumber, edge.NoiseZones, errors);
                                    break;
                                case "speed_limits":
                                    TryParseSpeedLimit(line, lineNumber, edge.SpeedZones, errors);
                                    break;
                                case "markers":
                                    TryParseMarker(line, lineNumber, edge.Markers, errors);
                                    break;
                                case "weather":
                                    TryParseWeatherZone(line, lineNumber, edge.WeatherZones, errors);
                                    break;
                                case "ambience":
                                    TryParseAmbienceZone(line, lineNumber, edge.AmbienceZones, errors);
                                    break;
                                case "hazards":
                                    TryParseHazard(line, lineNumber, edge.Hazards, errors);
                                    break;
                                case "checkpoints":
                                    TryParseCheckpoint(line, lineNumber, edge.Checkpoints, errors);
                                    break;
                                case "hit_lanes":
                                    TryParseHitLanes(line, lineNumber, edge.HitLanes, errors);
                                    break;
                                case "allowed_vehicles":
                                    TryParseAllowedVehicles(line, lineNumber, edge.AllowedVehicles, errors);
                                    break;
                                case "emitters":
                                    TryParseEmitter(line, lineNumber, edge.Emitters, errors);
                                    break;
                                case "triggers":
                                    TryParseTrigger(line, lineNumber, edge.Triggers, errors);
                                    break;
                                default:
                                    errors.Add(new TrackLayoutError(lineNumber, $"Unknown edge section '{section.Subsection}'.", rawLine));
                                    break;
                            }
                        }
                        break;
                    default:
                        errors.Add(new TrackLayoutError(lineNumber, $"Unknown or missing section '{section.Kind}'.", rawLine));
                        break;
                }
            }

            if (edges.Count == 0)
                errors.Add(new TrackLayoutError(0, "No edges defined."));

            if (errors.Count > 0)
                return new TrackLayoutParseResult(null, errors);

            var metadata = BuildMetadata(meta);
            var weather = ParseEnum(environment, "weather", TrackWeather.Sunny, errors);
            var ambience = ParseEnum(environment, "ambience", TrackAmbience.NoAmbience, errors);
            var defaultSurface = ParseEnum(environment, "default_surface", TrackSurface.Asphalt, errors);
            var defaultNoise = ParseEnum(environment, "default_noise", TrackNoise.NoNoise, errors);
            var defaultWidth = ParseFloat(environment, "default_width", 12f, errors);
            var sampleSpacing = ParseFloat(environment, "sample_spacing", 1f, errors);
            var enforceClosure = ParseBool(environment, "enforce_closure", true, errors);
            var primaryRouteId = ParseString(environment, "primary_route");

            if (errors.Count > 0)
                return new TrackLayoutParseResult(null, errors);

            foreach (var edge in edges.Values)
            {
                if (!string.IsNullOrWhiteSpace(edge.FromNodeId))
                    EnsureNode(edge.FromNodeId!, nodes);
                if (!string.IsNullOrWhiteSpace(edge.ToNodeId))
                    EnsureNode(edge.ToNodeId!, nodes);
            }

            if (nodes.Count == 0)
                errors.Add(new TrackLayoutError(0, "No nodes defined."));

            if (routes.Count == 0)
            {
                var fallbackId = string.IsNullOrWhiteSpace(primaryRouteId) ? "primary" : primaryRouteId!;
                routes.Add(new RouteBuilder(fallbackId, new List<string>(edgeOrder), isLoop: null));
            }

            if (errors.Count > 0)
                return new TrackLayoutParseResult(null, errors);

            var nodeList = new List<TrackGraphNode>(nodes.Count);
            foreach (var node in nodes.Values)
            {
                nodeList.Add(new TrackGraphNode(node.Id, node.Name, node.ShortName, node.Metadata));
            }

            var edgeList = new List<TrackGraphEdge>(edgeOrder.Count);
            foreach (var edgeId in edgeOrder)
            {
                if (!edges.TryGetValue(edgeId, out var builder))
                    continue;
                var edge = builder.Build(defaultSurface, defaultNoise, defaultWidth, weather, ambience, sampleSpacing, enforceClosure, errors);
                if (edge != null)
                    edgeList.Add(edge);
            }

            if (errors.Count > 0)
                return new TrackLayoutParseResult(null, errors);

            var routeList = new List<TrackGraphRoute>(routes.Count);
            foreach (var route in routes)
            {
                var missing = route.EdgeIds.Find(id => !edges.ContainsKey(id));
                if (missing != null)
                {
                    errors.Add(new TrackLayoutError(0, $"Route '{route.Id}' references missing edge '{missing}'."));
                    continue;
                }
                var isLoop = route.IsLoop ?? InferRouteLoop(route.EdgeIds, edges);
                routeList.Add(new TrackGraphRoute(route.Id, route.EdgeIds, isLoop));
            }

            if (errors.Count > 0)
                return new TrackLayoutParseResult(null, errors);

            if (!string.IsNullOrWhiteSpace(primaryRouteId) &&
                !routeList.Exists(route => route.Id.Equals(primaryRouteId, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(new TrackLayoutError(0, $"Primary route '{primaryRouteId}' not found."));
                return new TrackLayoutParseResult(null, errors);
            }

            var graph = new TrackGraph(nodeList, edgeList, routeList, primaryRouteId);
            var layout = new TrackLayout(
                graph,
                weather,
                ambience,
                defaultSurface,
                defaultNoise,
                defaultWidth,
                metadata);

            return new TrackLayoutParseResult(layout, errors);
        }
        public static string Write(TrackLayout layout)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));

            var sb = new StringBuilder();
            sb.AppendLine("# Top Speed track graph");
            sb.AppendLine("[meta]");
            WriteValue(sb, "name", layout.Metadata.Name);
            WriteValue(sb, "short_name", layout.Metadata.ShortName);
            WriteValue(sb, "description", layout.Metadata.Description);
            WriteValue(sb, "author", layout.Metadata.Author);
            WriteValue(sb, "version", layout.Metadata.Version);
            WriteValue(sb, "source", layout.Metadata.Source);
            if (layout.Metadata.Tags.Count > 0)
                WriteValue(sb, "tags", string.Join(", ", layout.Metadata.Tags));

            sb.AppendLine();
            sb.AppendLine("[environment]");
            sb.AppendLine($"weather={layout.Weather.ToString().ToLowerInvariant()}");
            sb.AppendLine($"ambience={layout.Ambience.ToString().ToLowerInvariant()}");
            sb.AppendLine($"default_surface={layout.DefaultSurface.ToString().ToLowerInvariant()}");
            sb.AppendLine($"default_noise={layout.DefaultNoise.ToString().ToLowerInvariant()}");
            sb.AppendLine($"default_width={FormatFloat(layout.DefaultWidthMeters)}");
            sb.AppendLine($"sample_spacing={FormatFloat(layout.Geometry.SampleSpacingMeters)}");
            sb.AppendLine($"enforce_closure={FormatBool(layout.Geometry.EnforceClosure)}");
            if (!string.IsNullOrWhiteSpace(layout.Graph.PrimaryRouteId))
                sb.AppendLine($"primary_route={layout.Graph.PrimaryRouteId}");

            if (layout.Graph.Nodes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[nodes]");
                foreach (var node in layout.Graph.Nodes)
                {
                    var line = new StringBuilder();
                    line.Append("id=").Append(node.Id);
                    AppendInlineValue(line, "name", node.Name);
                    AppendInlineValue(line, "short_name", node.ShortName);
                    foreach (var kvp in node.Metadata)
                        AppendInlineValue(line, kvp.Key, kvp.Value);
                    sb.AppendLine(line.ToString());
                }
            }

            if (layout.Graph.Edges.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[edges]");
                foreach (var edge in layout.Graph.Edges)
                {
                    sb.AppendLine($"id={edge.Id} from={edge.FromNodeId} to={edge.ToNodeId}");
                }
            }

            if (layout.Graph.Routes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[routes]");
                foreach (var route in layout.Graph.Routes)
                {
                    var edges = string.Join(",", route.EdgeIds);
                    sb.AppendLine($"id={route.Id} edges={edges} is_loop={FormatBool(route.IsLoop)}");
                }
            }

            foreach (var edge in layout.Graph.Edges)
            {
                sb.AppendLine();
                sb.AppendLine($"[edge {edge.Id}]");
                WriteValue(sb, "name", edge.Name);
                WriteValue(sb, "short_name", edge.ShortName);
                if (edge.ConnectorFromEdgeIds.Count > 0)
                    WriteValue(sb, "connector_from", string.Join(",", edge.ConnectorFromEdgeIds));
                if (edge.TurnDirection != TrackTurnDirection.Unknown)
                    WriteValue(sb, "turn", FormatTurnDirection(edge.TurnDirection));
                sb.AppendLine($"from={edge.FromNodeId}");
                sb.AppendLine($"to={edge.ToNodeId}");
                sb.AppendLine($"default_surface={edge.Profile.DefaultSurface.ToString().ToLowerInvariant()}");
                sb.AppendLine($"default_noise={edge.Profile.DefaultNoise.ToString().ToLowerInvariant()}");
                sb.AppendLine($"default_width={FormatFloat(edge.Profile.DefaultWidthMeters)}");
                sb.AppendLine($"default_weather={edge.Profile.DefaultWeather.ToString().ToLowerInvariant()}");
                sb.AppendLine($"default_ambience={edge.Profile.DefaultAmbience.ToString().ToLowerInvariant()}");
                if (edge.Profile.AllowedVehicles.Count > 0)
                    sb.AppendLine($"allowed_vehicles={string.Join(",", edge.Profile.AllowedVehicles)}");
                foreach (var kvp in edge.Metadata)
                    sb.AppendLine($"{kvp.Key}={kvp.Value}");

                sb.AppendLine();
                sb.AppendLine($"[edge {edge.Id}.geometry]");
                foreach (var span in edge.Geometry.Spans)
                {
                    sb.AppendLine(FormatSpan(span));
                }

                if (edge.Profile.WidthZones.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[edge {edge.Id}.width]");
                    foreach (var zone in edge.Profile.WidthZones)
                    {
                        sb.AppendLine($"{FormatFloat(zone.StartMeters)} {FormatFloat(zone.EndMeters)} {FormatFloat(zone.WidthMeters)} {FormatFloat(zone.ShoulderLeftMeters)} {FormatFloat(zone.ShoulderRightMeters)}");
                    }
                }

                if (edge.Profile.SurfaceZones.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[edge {edge.Id}.surface]");
                    foreach (var zone in edge.Profile.SurfaceZones)
                    {
                        sb.AppendLine($"{FormatFloat(zone.StartMeters)} {FormatFloat(zone.EndMeters)} {zone.Value.ToString().ToLowerInvariant()}");
                    }
                }

                if (edge.Profile.NoiseZones.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[edge {edge.Id}.noise]");
                    foreach (var zone in edge.Profile.NoiseZones)
                    {
                        sb.AppendLine($"{FormatFloat(zone.StartMeters)} {FormatFloat(zone.EndMeters)} {zone.Value.ToString().ToLowerInvariant()}");
                    }
                }

                if (edge.Profile.SpeedLimitZones.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[edge {edge.Id}.speed_limits]");
                    foreach (var zone in edge.Profile.SpeedLimitZones)
                    {
                        sb.AppendLine($"{FormatFloat(zone.StartMeters)} {FormatFloat(zone.EndMeters)} {FormatFloat(zone.MaxSpeedKph)}");
                    }
                }

                if (edge.Profile.Markers.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[edge {edge.Id}.markers]");
                    foreach (var marker in edge.Profile.Markers)
                    {
                        sb.AppendLine($"{marker.Name} {FormatFloat(marker.PositionMeters)}");
                    }
                }

                if (edge.Profile.WeatherZones.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[edge {edge.Id}.weather]");
                    foreach (var zone in edge.Profile.WeatherZones)
                    {
                        sb.AppendLine($"{FormatFloat(zone.StartMeters)} {FormatFloat(zone.EndMeters)} {zone.Weather.ToString().ToLowerInvariant()} {FormatFloat(zone.FadeInMeters)} {FormatFloat(zone.FadeOutMeters)}");
                    }
                }

                if (edge.Profile.AmbienceZones.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[edge {edge.Id}.ambience]");
                    foreach (var zone in edge.Profile.AmbienceZones)
                    {
                        sb.AppendLine($"{FormatFloat(zone.StartMeters)} {FormatFloat(zone.EndMeters)} {zone.Ambience.ToString().ToLowerInvariant()} {FormatFloat(zone.FadeInMeters)} {FormatFloat(zone.FadeOutMeters)}");
                    }
                }

                if (edge.Profile.Hazards.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[edge {edge.Id}.hazards]");
                    foreach (var hazard in edge.Profile.Hazards)
                    {
                        var name = string.IsNullOrWhiteSpace(hazard.Name) ? string.Empty : $" name={hazard.Name}";
                        sb.AppendLine($"{FormatFloat(hazard.StartMeters)} {FormatFloat(hazard.EndMeters)} {hazard.HazardType} {FormatFloat(hazard.Severity)}{name}");
                    }
                }

                if (edge.Profile.Checkpoints.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[edge {edge.Id}.checkpoints]");
                    foreach (var checkpoint in edge.Profile.Checkpoints)
                    {
                        var name = string.IsNullOrWhiteSpace(checkpoint.Name) ? string.Empty : $" name={checkpoint.Name}";
                        sb.AppendLine($"{FormatFloat(checkpoint.PositionMeters)} {checkpoint.Id}{name}");
                    }
                }

                if (edge.Profile.HitLanes.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[edge {edge.Id}.hit_lanes]");
                    foreach (var lane in edge.Profile.HitLanes)
                    {
                        var lanes = string.Join(",", lane.LaneIndices);
                        var effect = string.IsNullOrWhiteSpace(lane.Effect) ? string.Empty : $" effect={lane.Effect}";
                        sb.AppendLine($"{FormatFloat(lane.StartMeters)} {FormatFloat(lane.EndMeters)} lanes={lanes}{effect}");
                    }
                }

                if (edge.Profile.Emitters.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[edge {edge.Id}.emitters]");
                    foreach (var emitter in edge.Profile.Emitters)
                    {
                        var sound = string.IsNullOrWhiteSpace(emitter.SoundKey) ? string.Empty : $" sound={emitter.SoundKey}";
                        sb.AppendLine($"id={emitter.Id} pos={FormatFloat(emitter.PositionMeters)} radius={FormatFloat(emitter.RadiusMeters)} loop={FormatBool(emitter.Loop)} volume={FormatFloat(emitter.Volume)}{sound}");
                    }
                }

                if (edge.Profile.Triggers.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[edge {edge.Id}.triggers]");
                    foreach (var trigger in edge.Profile.Triggers)
                    {
                        var action = string.IsNullOrWhiteSpace(trigger.Action) ? string.Empty : $" action={trigger.Action}";
                        var payload = string.IsNullOrWhiteSpace(trigger.Payload) ? string.Empty : $" payload={trigger.Payload}";
                        sb.AppendLine($"id={trigger.Id} {FormatFloat(trigger.StartMeters)} {FormatFloat(trigger.EndMeters)}{action}{payload}");
                    }
                }
            }

            return sb.ToString();
        }

        private static TrackLayoutMetadata BuildMetadata(Dictionary<string, string> meta)
        {
            meta.TryGetValue("name", out var name);
            meta.TryGetValue("short_name", out var shortName);
            meta.TryGetValue("description", out var description);
            meta.TryGetValue("author", out var author);
            meta.TryGetValue("version", out var version);
            meta.TryGetValue("source", out var source);
            var tags = ParseTags(meta.TryGetValue("tags", out var tagValue) ? tagValue : null);

            return new TrackLayoutMetadata(name, shortName, description, author, version, source, tags);
        }

        private static IReadOnlyList<string> ParseTags(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<string>();

            var parts = value!.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(tag => tag.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void WriteValue(StringBuilder sb, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            sb.AppendLine($"{key}={EncodeValue(value!)}");
        }

        private static void AppendInlineValue(StringBuilder sb, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            sb.Append(' ').Append(key).Append('=').Append(EncodeValue(value!));
        }

        private static string EncodeValue(string value)
        {
            var encoded = value;
            if (NeedsQuoting(encoded))
                encoded = $"\"{encoded.Replace("\"", "\\\"")}\"";
            return encoded;
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", Culture);
        }

        private static string FormatBool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string FormatSpan(TrackGeometrySpan span)
        {
            var sb = new StringBuilder();
            sb.Append($"kind={span.Kind.ToString().ToLowerInvariant()}");
            sb.Append($" length={FormatFloat(span.LengthMeters)}");

            if (span.Kind == TrackGeometrySpanKind.Arc)
                sb.Append($" radius={FormatFloat(span.RadiusMeters)}");
            else if (span.Kind == TrackGeometrySpanKind.Clothoid)
                sb.Append($" start={FormatFloat(span.StartRadiusMeters)} end={FormatFloat(span.EndRadiusMeters)}");

            if (span.Direction != TrackCurveDirection.Straight)
                sb.Append($" direction={span.Direction.ToString().ToLowerInvariant()}");
            if (span.CurveSeverity.HasValue)
                sb.Append($" severity={span.CurveSeverity.Value.ToString().ToLowerInvariant()}");

            var slopeDelta = Math.Abs(span.StartSlope - span.EndSlope);
            if (slopeDelta > 0.0001f)
            {
                sb.Append($" slope_start={FormatFloat(span.StartSlope)}");
                sb.Append($" slope_end={FormatFloat(span.EndSlope)}");
            }
            else if (Math.Abs(span.StartSlope) > 0.0001f)
            {
                sb.Append($" slope={FormatFloat(span.StartSlope)}");
            }

            var bankDelta = Math.Abs(span.BankStartDegrees - span.BankEndDegrees);
            if (bankDelta > 0.0001f)
            {
                sb.Append($" bank_start={FormatFloat(span.BankStartDegrees)}");
                sb.Append($" bank_end={FormatFloat(span.BankEndDegrees)}");
            }
            else if (Math.Abs(span.BankStartDegrees) > 0.0001f)
            {
                sb.Append($" bank={FormatFloat(span.BankStartDegrees)}");
            }

            return sb.ToString();
        }

        private static bool NeedsQuoting(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsWhiteSpace(c) || c == '#' || c == ';')
                    return true;
            }
            return value.Contains('"');
        }

        private static bool TryParseGeometryLine(string line, int lineNumber, List<TrackGeometrySpan> spans, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();

            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out var key, out var value))
                    named[key] = value;
                else
                    positional.Add(token);
            }

            string? kind = null;
            var kindFromPositional = false;
            if (named.TryGetValue("kind", out var kindValue) || named.TryGetValue("type", out kindValue))
                kind = kindValue;
            else if (positional.Count > 0 && !LooksNumeric(positional[0]))
            {
                kind = positional[0];
                kindFromPositional = true;
            }

            if (string.IsNullOrWhiteSpace(kind))
            {
                errors.Add(new TrackLayoutError(lineNumber, "Geometry span missing kind.", line));
                return false;
            }

            var spanKind = ParseSpanKind(kind!);
            if (spanKind == null)
            {
                errors.Add(new TrackLayoutError(lineNumber, $"Unknown geometry kind '{kind}'.", line));
                return false;
            }

            if (kindFromPositional)
                positional.RemoveAt(0);

            var length = GetFloat(named, "length", "len", positional, 0, errors, lineNumber, line);
            if (length <= 0f)
                return false;

            var direction = ParseDirection(named, positional);
            var severity = ParseSeverity(named, positional);
            var elevation = GetFloat(named, "elevation", "elev", positional, -1, out var elevFound) ? elevFound : 0f;
            var bankParsed = GetFloat(named, "bank", "bankdeg", positional, -1, out var bankValue);
            var bank = bankParsed ? bankValue : 0f;

            var bankStartParsed = GetFloat(named, "bank_start", "bankstart", positional, -1, out var bankStartValue);
            var bankStart = bankStartParsed ? bankStartValue : (bankParsed ? bank : 0f);
            var bankEndParsed = GetFloat(named, "bank_end", "bankend", positional, -1, out var bankEndValue);
            var bankEnd = bankEndParsed ? bankEndValue : (bankStartParsed ? bankStart : (bankParsed ? bank : 0f));

            var slopeFound = TryParseSlope(named, "slope", "grade", out var slopeCommon);
            var slopeStartFound = TryParseSlope(named, "slope_start", "grade_start", out var slopeStart);
            var slopeEndFound = TryParseSlope(named, "slope_end", "grade_end", out var slopeEnd);

            var useSlopes = slopeFound || slopeStartFound || slopeEndFound;
            float startSlope;
            float endSlope;
            float elevationDelta;
            if (useSlopes)
            {
                startSlope = slopeStartFound ? slopeStart : (slopeFound ? slopeCommon : 0f);
                endSlope = slopeEndFound ? slopeEnd : (slopeFound ? slopeCommon : startSlope);
                elevationDelta = length * (startSlope + endSlope) * 0.5f;
            }
            else
            {
                elevationDelta = elevation;
                startSlope = length > 0f ? elevationDelta / length : 0f;
                endSlope = startSlope;
            }

            try
            {
                switch (spanKind.Value)
                {
                    case TrackGeometrySpanKind.Straight:
                        spans.Add(TrackGeometrySpan.StraightWithProfile(
                            length,
                            elevationDelta,
                            startSlope,
                            endSlope,
                            bankStart,
                            bankEnd));
                        break;
                    case TrackGeometrySpanKind.Arc:
                        var radius = GetFloat(named, "radius", "r", positional, 1, errors, lineNumber, line);
                        spans.Add(TrackGeometrySpan.ArcWithProfile(
                            length,
                            radius,
                            direction,
                            severity,
                            elevationDelta,
                            startSlope,
                            endSlope,
                            bankStart,
                            bankEnd));
                        break;
                    case TrackGeometrySpanKind.Clothoid:
                        var startRadius = GetFloat(named, "start", "startRadius", positional, 1, errors, lineNumber, line);
                        var endRadius = GetFloat(named, "end", "endRadius", positional, 2, errors, lineNumber, line);
                        spans.Add(TrackGeometrySpan.ClothoidWithProfile(
                            length,
                            startRadius,
                            endRadius,
                            direction,
                            severity,
                            elevationDelta,
                            startSlope,
                            endSlope,
                            bankStart,
                            bankEnd));
                        break;
                }
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line));
                return false;
            }

            return true;
        }

        private static bool TryParseWidthZone(string line, int lineNumber, List<TrackWidthZone> zones, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out var key, out var value))
                    named[key] = value;
                else
                    positional.Add(token);
            }

            var start = GetFloat(named, "start", "s", positional, 0, errors, lineNumber, line);
            var end = GetFloat(named, "end", "e", positional, 1, errors, lineNumber, line);
            var width = GetFloat(named, "width", "w", positional, 2, errors, lineNumber, line);
            var shoulderLeft = GetFloat(named, "shoulder_left", "sl", positional, 3, out var leftFound) ? leftFound : 0f;
            var shoulderRight = GetFloat(named, "shoulder_right", "sr", positional, 4, out var rightFound) ? rightFound : 0f;

            try
            {
                zones.Add(new TrackWidthZone(start, end, width, shoulderLeft, shoulderRight));
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line));
                return false;
            }

            return true;
        }

        private static bool TryParseSurfaceZone(string line, int lineNumber, List<TrackZone<TrackSurface>> zones, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count < 3)
            {
                errors.Add(new TrackLayoutError(lineNumber, "Surface zone requires: start end surface.", line));
                return false;
            }

            var start = ParseFloat(tokens[0], lineNumber, errors, line);
            var end = ParseFloat(tokens[1], lineNumber, errors, line);
            var surface = ParseEnum<TrackSurface>(tokens[2], lineNumber, errors, line);
            if (surface == null)
                return false;

            try
            {
                zones.Add(new TrackZone<TrackSurface>(start, end, surface.Value));
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line));
                return false;
            }
            return true;
        }

        private static bool TryParseNoiseZone(string line, int lineNumber, List<TrackZone<TrackNoise>> zones, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count < 3)
            {
                errors.Add(new TrackLayoutError(lineNumber, "Noise zone requires: start end noise.", line));
                return false;
            }

            var start = ParseFloat(tokens[0], lineNumber, errors, line);
            var end = ParseFloat(tokens[1], lineNumber, errors, line);
            var noise = ParseEnum<TrackNoise>(tokens[2], lineNumber, errors, line);
            if (noise == null)
                return false;

            try
            {
                zones.Add(new TrackZone<TrackNoise>(start, end, noise.Value));
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line));
                return false;
            }
            return true;
        }

        private static bool TryParseSpeedLimit(string line, int lineNumber, List<TrackSpeedLimitZone> zones, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count < 3)
            {
                errors.Add(new TrackLayoutError(lineNumber, "Speed limit requires: start end kph.", line));
                return false;
            }

            var start = ParseFloat(tokens[0], lineNumber, errors, line);
            var end = ParseFloat(tokens[1], lineNumber, errors, line);
            var limit = ParseFloat(tokens[2], lineNumber, errors, line);
            if (tokens.Count >= 4 && tokens[3].Equals("mps", StringComparison.OrdinalIgnoreCase))
                limit *= 3.6f;

            try
            {
                zones.Add(new TrackSpeedLimitZone(start, end, limit));
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line));
                return false;
            }
            return true;
        }

        private static bool TryParseMarker(string line, int lineNumber, List<TrackMarker> markers, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count < 2)
            {
                errors.Add(new TrackLayoutError(lineNumber, "Marker requires: name position.", line));
                return false;
            }

            var name = tokens[0];
            var position = ParseFloat(tokens[1], lineNumber, errors, line);     
            try
            {
                markers.Add(new TrackMarker(name, position));
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line)); 
                return false;
            }
            return true;
        }

        private static bool TryParseWeatherZone(string line, int lineNumber, List<TrackWeatherZone> zones, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out var key, out var value))
                    named[key] = value;
                else
                    positional.Add(token);
            }

            var start = GetFloat(named, "start", "s", positional, 0, errors, lineNumber, line);
            var end = GetFloat(named, "end", "e", positional, 1, errors, lineNumber, line);
            var weatherToken = GetString(named, "weather", "w", positional, 2, errors, lineNumber, line);
            var weatherValue = weatherToken?.Trim();
            if (string.IsNullOrWhiteSpace(weatherValue))
                return false;

            var weather = ParseEnum<TrackWeather>(weatherValue!, lineNumber, errors, line);
            if (weather == null)
                return false;

            var fadeIn = GetFloat(named, "fade_in", "fi", positional, 3, out var fadeInValue) ? fadeInValue : 0f;
            var fadeOut = GetFloat(named, "fade_out", "fo", positional, 4, out var fadeOutValue) ? fadeOutValue : 0f;

            try
            {
                zones.Add(new TrackWeatherZone(start, end, weather.Value, fadeIn, fadeOut));
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line));
                return false;
            }

            return true;
        }

        private static bool TryParseAmbienceZone(string line, int lineNumber, List<TrackAmbienceZone> zones, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out var key, out var value))
                    named[key] = value;
                else
                    positional.Add(token);
            }

            var start = GetFloat(named, "start", "s", positional, 0, errors, lineNumber, line);
            var end = GetFloat(named, "end", "e", positional, 1, errors, lineNumber, line);
            var ambienceToken = GetString(named, "ambience", "a", positional, 2, errors, lineNumber, line);
            var ambienceValue = ambienceToken?.Trim();
            if (string.IsNullOrWhiteSpace(ambienceValue))
                return false;

            var ambience = ParseEnum<TrackAmbience>(ambienceValue!, lineNumber, errors, line);
            if (ambience == null)
                return false;

            var fadeIn = GetFloat(named, "fade_in", "fi", positional, 3, out var fadeInValue) ? fadeInValue : 0f;
            var fadeOut = GetFloat(named, "fade_out", "fo", positional, 4, out var fadeOutValue) ? fadeOutValue : 0f;

            try
            {
                zones.Add(new TrackAmbienceZone(start, end, ambience.Value, fadeIn, fadeOut));
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line));
                return false;
            }

            return true;
        }

        private static bool TryParseHazard(string line, int lineNumber, List<TrackHazardZone> hazards, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out var key, out var value))
                    named[key] = value;
                else
                    positional.Add(token);
            }

            var start = GetFloat(named, "start", "s", positional, 0, errors, lineNumber, line);
            var end = GetFloat(named, "end", "e", positional, 1, errors, lineNumber, line);
            var type = GetString(named, "type", "hazard", positional, 2, errors, lineNumber, line);
            var typeValue = type?.Trim();
            if (string.IsNullOrWhiteSpace(typeValue))
                return false;

            var severity = GetFloat(named, "severity", "sev", positional, 3, out var sevValue) ? sevValue : 1f;
            named.TryGetValue("name", out var name);
            var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            var metadata = CollectMetadata(named, "start", "s", "end", "e", "type", "hazard", "severity", "sev", "name");

            try
            {
                hazards.Add(new TrackHazardZone(start, end, typeValue!, severity, trimmedName, metadata));
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line));
                return false;
            }

            return true;
        }

        private static bool TryParseCheckpoint(string line, int lineNumber, List<TrackCheckpoint> checkpoints, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out var key, out var value))
                    named[key] = value;
                else
                    positional.Add(token);
            }

            var position = GetFloat(named, "pos", "position", positional, 0, errors, lineNumber, line);
            var id = GetString(named, "id", "checkpoint", positional, 1, errors, lineNumber, line);
            named.TryGetValue("name", out var name);
            if (string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                id = name;
            if (string.IsNullOrWhiteSpace(id))
                return false;

            try
            {
                var trimmedId = id!.Trim();
                var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
                checkpoints.Add(new TrackCheckpoint(trimmedId, position, trimmedName));
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line));
                return false;
            }

            return true;
        }

        private static bool TryParseHitLanes(string line, int lineNumber, List<TrackHitLaneZone> zones, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out var key, out var value))
                    named[key] = value;
                else
                    positional.Add(token);
            }

            var start = GetFloat(named, "start", "s", positional, 0, errors, lineNumber, line);
            var end = GetFloat(named, "end", "e", positional, 1, errors, lineNumber, line);
            var lanesToken = GetString(named, "lanes", "lane", positional, 2, errors, lineNumber, line);
            if (string.IsNullOrWhiteSpace(lanesToken))
                return false;

            named.TryGetValue("effect", out var effect);
            var lanes = ParseIntList(lanesToken, lineNumber, errors, line);
            if (lanes.Count == 0)
                return false;

            try
            {
                zones.Add(new TrackHitLaneZone(start, end, lanes, effect));
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line));
                return false;
            }

            return true;
        }

        private static bool TryParseAllowedVehicles(string line, int lineNumber, List<string> vehicles, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out _, out var value))
                {
                    foreach (var entry in SplitList(value))
                        AddIfNotEmpty(vehicles, entry);
                }
                else
                {
                    AddIfNotEmpty(vehicles, token);
                }
            }

            return true;
        }

        private static bool TryParseEmitter(string line, int lineNumber, List<TrackAudioEmitter> emitters, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out var key, out var value))
                    named[key] = value;
                else
                    positional.Add(token);
            }

            var id = GetString(named, "id", "name", positional, 0, errors, lineNumber, line);
            if (string.IsNullOrWhiteSpace(id))
                return false;

            var position = GetFloat(named, "pos", "position", positional, 1, errors, lineNumber, line);
            var radius = GetFloat(named, "radius", "r", positional, 2, errors, lineNumber, line);
            named.TryGetValue("sound", out var sound);
            var volume = GetFloat(named, "volume", "vol", positional, 3, out var volumeValue) ? volumeValue : 1f;
            var loop = ParseBoolValue(named.TryGetValue("loop", out var loopValue) ? loopValue : null, true, out var loopParsed);
            if (loopParsed == false && named.ContainsKey("loop"))
            {
                errors.Add(new TrackLayoutError(lineNumber, "Invalid loop value.", line));
                return false;
            }

            var metadata = CollectMetadata(named, "id", "name", "pos", "position", "radius", "r", "sound", "volume", "vol", "loop");
            try
            {
                emitters.Add(new TrackAudioEmitter(id!.Trim(), position, radius, sound, loop, volume, metadata));
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line));
                return false;
            }
            return true;
        }

        private static bool TryParseTrigger(string line, int lineNumber, List<TrackTriggerZone> triggers, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out var key, out var value))
                    named[key] = value;
                else
                    positional.Add(token);
            }

            var id = GetString(named, "id", "name", positional, 0, errors, lineNumber, line);
            if (string.IsNullOrWhiteSpace(id))
                return false;

            var start = GetFloat(named, "start", "s", positional, 1, errors, lineNumber, line);
            var end = GetFloat(named, "end", "e", positional, 2, errors, lineNumber, line);
            named.TryGetValue("action", out var action);
            named.TryGetValue("payload", out var payload);
            var metadata = CollectMetadata(named, "id", "name", "start", "s", "end", "e", "action", "payload");

            try
            {
                triggers.Add(new TrackTriggerZone(id!.Trim(), start, end, action, payload, metadata));
            }
            catch (Exception ex)
            {
                errors.Add(new TrackLayoutError(lineNumber, ex.Message, line));
                return false;
            }
            return true;
        }

        private static EdgeBuilder GetOrCreateEdge(string edgeId, Dictionary<string, EdgeBuilder> edges, List<string> edgeOrder)
        {
            if (!edges.TryGetValue(edgeId, out var edge))
            {
                edge = new EdgeBuilder(edgeId);
                edges.Add(edgeId, edge);
                edgeOrder.Add(edgeId);
            }
            return edge;
        }

        private static void EnsureNode(string nodeId, Dictionary<string, NodeBuilder> nodes)
        {
            if (!nodes.ContainsKey(nodeId))
                nodes.Add(nodeId, new NodeBuilder(nodeId));
        }

        private static bool TryParseNodeLine(string line, int lineNumber, Dictionary<string, NodeBuilder> nodes, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out var key, out var value))
                    named[key] = value;
                else
                    positional.Add(token);
            }

            var id = GetString(named, "id", "node", positional, 0, errors, lineNumber, line);
            if (string.IsNullOrWhiteSpace(id))
                return false;

            var nodeId = id!.Trim();
            named.TryGetValue("name", out var name);
            named.TryGetValue("short_name", out var shortName);
            if (string.IsNullOrWhiteSpace(shortName) && named.TryGetValue("short", out var shortAlt))
                shortName = shortAlt;
            if (string.IsNullOrWhiteSpace(name) && positional.Count > 1)
                name = positional[1];

            if (!nodes.TryGetValue(nodeId, out var node))
            {
                node = new NodeBuilder(nodeId);
                nodes.Add(nodeId, node);
            }

            if (!string.IsNullOrWhiteSpace(name))
                node.Name = name;
            if (!string.IsNullOrWhiteSpace(shortName))
                node.ShortName = shortName;

            foreach (var kvp in named)
            {
                if (kvp.Key.Equals("id", StringComparison.OrdinalIgnoreCase) || kvp.Key.Equals("node", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("name", StringComparison.OrdinalIgnoreCase) || kvp.Key.Equals("short_name", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("short", StringComparison.OrdinalIgnoreCase))
                    continue;
                node.Metadata[kvp.Key] = kvp.Value;
            }

            return true;
        }

        private static bool TryParseEdgeLine(string line, int lineNumber, Dictionary<string, EdgeBuilder> edges, List<string> edgeOrder, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out var key, out var value))
                    named[key] = value;
                else
                    positional.Add(token);
            }

            var id = GetString(named, "id", "edge", positional, 0, errors, lineNumber, line);
            if (string.IsNullOrWhiteSpace(id))
                return false;

            var edge = GetOrCreateEdge(id!, edges, edgeOrder);
            var from = GetString(named, "from", "src", positional, 1, errors, lineNumber, line);
            var to = GetString(named, "to", "dst", positional, 2, errors, lineNumber, line);
            if (!string.IsNullOrWhiteSpace(from))
                edge.FromNodeId = from;
            if (!string.IsNullOrWhiteSpace(to))
                edge.ToNodeId = to;

            if (named.TryGetValue("name", out var edgeName))
                edge.Name = edgeName;
            if (named.TryGetValue("short_name", out var edgeShort))
                edge.ShortName = edgeShort;
            if (string.IsNullOrWhiteSpace(edge.ShortName) && named.TryGetValue("short", out var edgeShortAlt))
                edge.ShortName = edgeShortAlt;

            if (named.TryGetValue("connector_from", out var connectorFrom) ||
                named.TryGetValue("from_edge", out connectorFrom) ||
                named.TryGetValue("from_edges", out connectorFrom))
            {
                foreach (var entry in SplitList(connectorFrom))
                    AddIfNotEmpty(edge.ConnectorFromEdgeIds, entry);
            }

            if (named.TryGetValue("turn", out var turnValue) || named.TryGetValue("turn_direction", out turnValue))
            {
                if (TryParseTurnDirection(turnValue, out var turnDirection))
                    edge.TurnDirection = turnDirection;
                else
                    errors.Add(new TrackLayoutError(lineNumber, $"Invalid turn direction '{turnValue}'.", line));
            }

            if (named.TryGetValue("allowed_vehicles", out var vehicleList) || named.TryGetValue("vehicles", out vehicleList))
            {
                foreach (var entry in SplitList(vehicleList))
                    AddIfNotEmpty(edge.AllowedVehicles, entry);
            }

            foreach (var kvp in named)
            {
                if (kvp.Key.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("edge", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("from", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("src", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("to", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("dst", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("short_name", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("short", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("allowed_vehicles", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("vehicles", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("connector_from", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("from_edge", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("from_edges", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("turn", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("turn_direction", StringComparison.OrdinalIgnoreCase))
                    continue;
                edge.Metadata[kvp.Key] = kvp.Value;
            }

            return true;
        }

        private static bool TryParseRouteLine(string line, int lineNumber, List<RouteBuilder> routes, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return false;

            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            foreach (var token in tokens)
            {
                if (TrySplitKeyValue(token, out var key, out var value))
                    named[key] = value;
                else
                    positional.Add(token);
            }

            var id = GetString(named, "id", "route", positional, 0, errors, lineNumber, line);
            if (string.IsNullOrWhiteSpace(id))
                return false;

            string? edgesToken = null;
            if (named.TryGetValue("edges", out var edgesValue) || named.TryGetValue("path", out edgesValue))
                edgesToken = edgesValue;

            var edgeIds = new List<string>();
            if (!string.IsNullOrWhiteSpace(edgesToken))
            {
                foreach (var entry in SplitList(edgesToken))
                    AddIfNotEmpty(edgeIds, entry);
            }
            else if (positional.Count > 1)
            {
                for (var i = 1; i < positional.Count; i++)
                    AddIfNotEmpty(edgeIds, positional[i]);
            }

            if (edgeIds.Count == 0)
            {
                errors.Add(new TrackLayoutError(lineNumber, "Route requires edge list.", line));
                return false;
            }

            bool? isLoop = null;
            if (named.TryGetValue("is_loop", out var loopValue) || named.TryGetValue("loop", out loopValue))
            {
                isLoop = ParseBoolValue(loopValue, false, out var parsed);
                if (!parsed)
                {
                    errors.Add(new TrackLayoutError(lineNumber, "Invalid loop value.", line));
                    return false;
                }
            }

            routes.Add(new RouteBuilder(id!, edgeIds, isLoop));
            return true;
        }

        private static void ParseEdgeProperties(string line, int lineNumber, EdgeBuilder edge, List<TrackLayoutError> errors)
        {
            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                return;

            foreach (var token in tokens)
            {
                if (!TrySplitKeyValue(token, out var key, out var value))
                {
                    errors.Add(new TrackLayoutError(lineNumber, "Expected key=value in edge section.", line));
                    continue;
                }

                switch (key.ToLowerInvariant())
                {
                    case "name":
                        edge.Name = value;
                        break;
                    case "short_name":
                    case "short":
                        edge.ShortName = value;
                        break;
                    case "from":
                        edge.FromNodeId = value;
                        break;
                    case "to":
                        edge.ToNodeId = value;
                        break;
                    case "connector_from":
                    case "from_edge":
                    case "from_edges":
                        foreach (var entry in SplitList(value))
                            AddIfNotEmpty(edge.ConnectorFromEdgeIds, entry);
                        break;
                    case "turn":
                    case "turn_direction":
                        if (TryParseTurnDirection(value, out var turnDirection))
                            edge.TurnDirection = turnDirection;
                        else
                            errors.Add(new TrackLayoutError(lineNumber, $"Invalid turn direction '{value}'.", line));
                        break;
                    case "default_surface":
                    case "surface":
                        var surface = ParseEnum<TrackSurface>(value, lineNumber, errors, line);
                        if (surface.HasValue)
                        {
                            edge.DefaultSurface = surface.Value;
                            edge.HasDefaultSurface = true;
                        }
                        break;
                    case "default_noise":
                    case "noise":
                        var noise = ParseEnum<TrackNoise>(value, lineNumber, errors, line);
                        if (noise.HasValue)
                        {
                            edge.DefaultNoise = noise.Value;
                            edge.HasDefaultNoise = true;
                        }
                        break;
                    case "default_width":
                    case "width":
                        edge.DefaultWidthMeters = ParseFloat(value, lineNumber, errors, line);
                        edge.HasDefaultWidth = true;
                        break;
                    case "default_weather":
                    case "weather":
                        var weather = ParseEnum<TrackWeather>(value, lineNumber, errors, line);
                        if (weather.HasValue)
                        {
                            edge.DefaultWeather = weather.Value;
                            edge.HasDefaultWeather = true;
                        }
                        break;
                    case "default_ambience":
                    case "ambience":
                        var ambience = ParseEnum<TrackAmbience>(value, lineNumber, errors, line);
                        if (ambience.HasValue)
                        {
                            edge.DefaultAmbience = ambience.Value;
                            edge.HasDefaultAmbience = true;
                        }
                        break;
                    case "sample_spacing":
                        edge.SampleSpacingMeters = ParseFloat(value, lineNumber, errors, line);
                        edge.HasSampleSpacing = true;
                        break;
                    case "enforce_closure":
                        edge.EnforceClosure = ParseBoolValue(value, true, out var parsedClosure);
                        edge.HasEnforceClosure = parsedClosure;
                        if (!parsedClosure)
                            errors.Add(new TrackLayoutError(lineNumber, "Invalid enforce_closure value.", line));
                        break;
                    case "allowed_vehicles":
                    case "vehicles":
                        foreach (var entry in SplitList(value))
                            AddIfNotEmpty(edge.AllowedVehicles, entry);
                        break;
                    default:
                        edge.Metadata[key] = value;
                        break;
                }
            }
        }

        private static bool InferRouteLoop(IReadOnlyList<string> edgeIds, Dictionary<string, EdgeBuilder> edges)
        {
            if (edgeIds == null || edgeIds.Count == 0)
                return false;
            if (!edges.TryGetValue(edgeIds[0], out var firstEdge))
                return false;
            if (!edges.TryGetValue(edgeIds[edgeIds.Count - 1], out var lastEdge))
                return false;
            if (edgeIds.Count == 1)
                return string.Equals(firstEdge.FromNodeId, firstEdge.ToNodeId, StringComparison.OrdinalIgnoreCase);
            return string.Equals(firstEdge.FromNodeId, lastEdge.ToNodeId, StringComparison.OrdinalIgnoreCase);
        }

        private static TrackCurveDirection ParseDirection(Dictionary<string, string> named, List<string> positional)
        {
            if (named.TryGetValue("dir", out var dirValue) || named.TryGetValue("direction", out dirValue))
                return ParseDirection(dirValue);

            foreach (var token in positional)
            {
                var dir = ParseDirection(token);
                if (dir != TrackCurveDirection.Straight)
                    return dir;
            }

            return TrackCurveDirection.Straight;
        }

        private static TrackCurveDirection ParseDirection(string value)
        {
            if (value.Equals("left", StringComparison.OrdinalIgnoreCase))
                return TrackCurveDirection.Left;
            if (value.Equals("right", StringComparison.OrdinalIgnoreCase))
                return TrackCurveDirection.Right;
            return TrackCurveDirection.Straight;
        }

        private static bool TryParseTurnDirection(string value, out TrackTurnDirection direction)
        {
            direction = TrackTurnDirection.Unknown;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim();
            if (trimmed.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                direction = TrackTurnDirection.Unknown;
                return true;
            }
            if (trimmed.Equals("left", StringComparison.OrdinalIgnoreCase))
            {
                direction = TrackTurnDirection.Left;
                return true;
            }
            if (trimmed.Equals("right", StringComparison.OrdinalIgnoreCase))
            {
                direction = TrackTurnDirection.Right;
                return true;
            }
            if (trimmed.Equals("straight", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("forward", StringComparison.OrdinalIgnoreCase))
            {
                direction = TrackTurnDirection.Straight;
                return true;
            }
            if (trimmed.Equals("uturn", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("u_turn", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("u-turn", StringComparison.OrdinalIgnoreCase))
            {
                direction = TrackTurnDirection.UTurn;
                return true;
            }
            return false;
        }

        private static string FormatTurnDirection(TrackTurnDirection direction)
        {
            switch (direction)
            {
                case TrackTurnDirection.Left:
                    return "left";
                case TrackTurnDirection.Right:
                    return "right";
                case TrackTurnDirection.Straight:
                    return "straight";
                case TrackTurnDirection.UTurn:
                    return "uturn";
                default:
                    return "unknown";
            }
        }

        private static TrackCurveSeverity? ParseSeverity(Dictionary<string, string> named, List<string> positional)
        {
            if (named.TryGetValue("severity", out var severityValue) || named.TryGetValue("curve", out severityValue))
                return ParseSeverity(severityValue);

            foreach (var token in positional)
            {
                var severity = ParseSeverity(token);
                if (severity.HasValue)
                    return severity;
            }

            return null;
        }

        private static TrackCurveSeverity? ParseSeverity(string value)
        {
            if (value.Equals("easy", StringComparison.OrdinalIgnoreCase))
                return TrackCurveSeverity.Easy;
            if (value.Equals("normal", StringComparison.OrdinalIgnoreCase))
                return TrackCurveSeverity.Normal;
            if (value.Equals("hard", StringComparison.OrdinalIgnoreCase))
                return TrackCurveSeverity.Hard;
            if (value.Equals("hairpin", StringComparison.OrdinalIgnoreCase))
                return TrackCurveSeverity.Hairpin;
            return null;
        }

        private static TrackGeometrySpanKind? ParseSpanKind(string value)       
        {
            if (value.Equals("straight", StringComparison.OrdinalIgnoreCase))   
                return TrackGeometrySpanKind.Straight;
            if (value.Equals("arc", StringComparison.OrdinalIgnoreCase) ||      
                value.Equals("curve", StringComparison.OrdinalIgnoreCase))      
                return TrackGeometrySpanKind.Arc;
            if (value.Equals("clothoid", StringComparison.OrdinalIgnoreCase))   
                return TrackGeometrySpanKind.Clothoid;
            return null;
        }

        private readonly struct SectionInfo
        {
            public static readonly SectionInfo Empty = new SectionInfo(string.Empty, null, null);
            public string Kind { get; }
            public string? EdgeId { get; }
            public string? Subsection { get; }

            public SectionInfo(string kind, string? edgeId, string? subsection)
            {
                Kind = kind;
                EdgeId = edgeId;
                Subsection = subsection;
            }
        }

        private static void ParseKeyValue(string line, int lineNumber, Dictionary<string, string> target, List<TrackLayoutError> errors)
        {
            if (!TrySplitKeyValue(line, out var key, out var value))
            {
                errors.Add(new TrackLayoutError(lineNumber, "Expected key=value.", line));
                return;
            }
            target[key] = value;
        }

        private static string? ParseString(Dictionary<string, string> named, string key)
        {
            if (named.TryGetValue(key, out var value))
            {
                var trimmed = value?.Trim();
                return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
            }
            return null;
        }

        private static string? GetString(Dictionary<string, string> named, string key, string altKey, List<string> positional, int positionalIndex, List<TrackLayoutError> errors, int lineNumber, string line)
        {
            if (named.TryGetValue(key, out var value) || named.TryGetValue(altKey, out value))
            {
                var trimmed = value?.Trim();
                return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
            }

            if (positionalIndex >= 0 && positionalIndex < positional.Count)
            {
                var trimmed = positional[positionalIndex].Trim();
                return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
            }

            errors.Add(new TrackLayoutError(lineNumber, $"Missing {key}.", line));
            return null;
        }

        private static List<int> ParseIntList(string? value, int lineNumber, List<TrackLayoutError> errors, string line)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(value))
                return result;
            foreach (var part in SplitList(value))
            {
                if (int.TryParse(part, NumberStyles.Integer, Culture, out var parsed))
                    result.Add(parsed);
                else
                    errors.Add(new TrackLayoutError(lineNumber, $"Invalid integer '{part}'.", line));
            }
            return result;
        }

        private static IReadOnlyList<string> SplitList(string? value)
        {
            if (value == null)
                return Array.Empty<string>();
            var trimmedValue = value.Trim();
            if (trimmedValue.Length == 0)
                return Array.Empty<string>();
            var parts = trimmedValue.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    list.Add(trimmed);
            }
            return list;
        }

        private static void AddIfNotEmpty(List<string> list, string? value)
        {
            var trimmed = value?.Trim();
            if (trimmed == null || trimmed.Length == 0)
                return;
            list.Add(trimmed);
        }

        private static Dictionary<string, string> CollectMetadata(Dictionary<string, string> named, params string[] reservedKeys)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var reserved = new HashSet<string>(reservedKeys, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in named)
            {
                if (reserved.Contains(kvp.Key))
                    continue;
                metadata[kvp.Key] = kvp.Value;
            }
            return metadata;
        }

        private static bool ParseBoolValue(string? value, bool defaultValue, out bool parsed)
        {
            parsed = true;
            if (string.IsNullOrWhiteSpace(value))
            {
                parsed = false;
                return defaultValue;
            }

            var normalized = value!.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "true":
                case "1":
                case "yes":
                case "y":
                    return true;
                case "false":
                case "0":
                case "no":
                case "n":
                    return false;
                default:
                    parsed = false;
                    return defaultValue;
            }
        }

        private static bool TrySplitKeyValue(string token, out string key, out string value)
        {
            var idx = token.IndexOf('=');
            if (idx < 0)
                idx = token.IndexOf(':');
            if (idx <= 0)
            {
                key = string.Empty;
                value = string.Empty;
                return false;
            }
            key = token.Substring(0, idx).Trim();
            value = token.Substring(idx + 1).Trim();
            return !string.IsNullOrWhiteSpace(key);
        }

        private static bool TryParseSection(string line, out SectionInfo section)
        {
            section = SectionInfo.Empty;
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("[") || !trimmed.EndsWith("]"))
                return false;

            var content = trimmed.Substring(1, trimmed.Length - 2).Trim();
            if (string.IsNullOrWhiteSpace(content))
                return false;

            if (content.StartsWith("edge", StringComparison.OrdinalIgnoreCase))
            {
                var rest = content.Substring(4).Trim();
                if (rest.StartsWith(":", StringComparison.Ordinal))
                    rest = rest.Substring(1).Trim();
                if (string.IsNullOrWhiteSpace(rest))
                {
                    section = new SectionInfo("edge", null, null);
                    return true;
                }

                string edgeId = rest;
                string? subsection = null;
                var dot = rest.IndexOf('.');
                if (dot >= 0)
                {
                    edgeId = rest.Substring(0, dot);
                    subsection = rest.Substring(dot + 1);
                }
                section = new SectionInfo("edge", edgeId.Trim(), subsection?.Trim().ToLowerInvariant());
                return true;
            }

            section = new SectionInfo(content.ToLowerInvariant(), null, null);
            return true;
        }

        private static string StripComment(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) ||
                trimmed.StartsWith(";", StringComparison.Ordinal))
                return string.Empty;

            return line.Trim();
        }

        private static List<string> SplitTokens(string line)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(line))
                return tokens;

            var current = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '\"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0)
                tokens.Add(current.ToString());

            return tokens;
        }

        private static bool LooksNumeric(string token)
        {
            return float.TryParse(token, NumberStyles.Float, Culture, out _);
        }

        private static float GetFloat(Dictionary<string, string> named, string key, string altKey, List<string> positional, int positionalIndex, List<TrackLayoutError> errors, int lineNumber, string line)
        {
            if (named.TryGetValue(key, out var value) || named.TryGetValue(altKey, out value))
                return ParseFloat(value, lineNumber, errors, line);

            if (positionalIndex >= 0 && positionalIndex < positional.Count)
                return ParseFloat(positional[positionalIndex], lineNumber, errors, line);

            errors.Add(new TrackLayoutError(lineNumber, $"Missing {key}.", line));
            return 0f;
        }

        private static bool GetFloat(Dictionary<string, string> named, string key, string altKey, List<string> positional, int positionalIndex, out float value)
        {
            if (named.TryGetValue(key, out var namedValue) || named.TryGetValue(altKey, out namedValue))
            {
                if (float.TryParse(namedValue, NumberStyles.Float, Culture, out value))
                    return true;
            }

            if (positionalIndex >= 0 && positionalIndex < positional.Count &&
                float.TryParse(positional[positionalIndex], NumberStyles.Float, Culture, out value))
                return true;

            value = 0f;
            return false;
        }

        private static float ParseFloat(string value, int lineNumber, List<TrackLayoutError> errors, string line)
        {
            if (!float.TryParse(value, NumberStyles.Float, Culture, out var result))
            {
                errors.Add(new TrackLayoutError(lineNumber, $"Invalid number '{value}'.", line));
                return 0f;
            }
            return result;
        }

        private static bool TryParseSlope(Dictionary<string, string> named, string key, string altKey, out float slope)
        {
            if (named.TryGetValue(key, out var value) || named.TryGetValue(altKey, out value))
            {
                if (TryParsePercent(value, out var percent))
                {
                    slope = percent / 100f;
                    return true;
                }
            }

            slope = 0f;
            return false;
        }

        private static bool TryParsePercent(string value, out float percent)
        {
            percent = 0f;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim();
            if (trimmed.EndsWith("%", StringComparison.Ordinal))
                trimmed = trimmed.Substring(0, trimmed.Length - 1).Trim();
            return float.TryParse(trimmed, NumberStyles.Float, Culture, out percent);
        }

        private static string FormatPercent(float slope)
        {
            return (slope * 100f).ToString("0.###", Culture);
        }

        private static T? ParseEnum<T>(string value, int lineNumber, List<TrackLayoutError> errors, string line) where T : struct
        {
            if (Enum.TryParse<T>(value, true, out var result))
                return result;
            errors.Add(new TrackLayoutError(lineNumber, $"Unknown value '{value}'.", line));
            return null;
        }

        private static T ParseEnum<T>(Dictionary<string, string> named, string key, T fallback, List<TrackLayoutError> errors) where T : struct
        {
            if (!named.TryGetValue(key, out var value))
                return fallback;
            var parsed = ParseEnum<T>(value, 0, errors, value);
            return parsed ?? fallback;
        }

        private static float ParseFloat(Dictionary<string, string> named, string key, float fallback, List<TrackLayoutError> errors)
        {
            if (!named.TryGetValue(key, out var value))
                return fallback;
            if (!float.TryParse(value, NumberStyles.Float, Culture, out var result))
            {
                errors.Add(new TrackLayoutError(0, $"Invalid number '{value}' for {key}."));
                return fallback;
            }
            return result;
        }

        private static bool ParseBool(Dictionary<string, string> named, string key, bool fallback, List<TrackLayoutError> errors)
        {
            if (!named.TryGetValue(key, out var value))
                return fallback;
            if (bool.TryParse(value, out var result))
                return result;
            if (value.Equals("1", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value.Equals("0", StringComparison.OrdinalIgnoreCase))
                return false;
            errors.Add(new TrackLayoutError(0, $"Invalid bool '{value}' for {key}."));
            return fallback;
        }

        private sealed class NodeBuilder
        {
            public string Id { get; }
            public string? Name { get; set; }
            public string? ShortName { get; set; }
            public Dictionary<string, string> Metadata { get; }

            public NodeBuilder(string id)
            {
                Id = id;
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private sealed class EdgeBuilder
        {
            public string Id { get; }
            public string? FromNodeId { get; set; }
            public string? ToNodeId { get; set; }
            public string? Name { get; set; }
            public string? ShortName { get; set; }
            public readonly List<TrackGeometrySpan> GeometrySpans = new List<TrackGeometrySpan>();
            public readonly List<TrackZone<TrackSurface>> SurfaceZones = new List<TrackZone<TrackSurface>>();
            public readonly List<TrackZone<TrackNoise>> NoiseZones = new List<TrackZone<TrackNoise>>();
            public readonly List<TrackWidthZone> WidthZones = new List<TrackWidthZone>();
            public readonly List<TrackSpeedLimitZone> SpeedZones = new List<TrackSpeedLimitZone>();
            public readonly List<TrackMarker> Markers = new List<TrackMarker>();
            public readonly List<TrackWeatherZone> WeatherZones = new List<TrackWeatherZone>();
            public readonly List<TrackAmbienceZone> AmbienceZones = new List<TrackAmbienceZone>();
            public readonly List<TrackHazardZone> Hazards = new List<TrackHazardZone>();
            public readonly List<TrackCheckpoint> Checkpoints = new List<TrackCheckpoint>();
            public readonly List<TrackHitLaneZone> HitLanes = new List<TrackHitLaneZone>();
            public readonly List<string> AllowedVehicles = new List<string>();
            public readonly List<TrackAudioEmitter> Emitters = new List<TrackAudioEmitter>();
            public readonly List<TrackTriggerZone> Triggers = new List<TrackTriggerZone>();
            public readonly Dictionary<string, string> Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> ConnectorFromEdgeIds = new List<string>();
            public TrackTurnDirection TurnDirection = TrackTurnDirection.Unknown;

            public TrackSurface DefaultSurface;
            public TrackNoise DefaultNoise;
            public float DefaultWidthMeters;
            public TrackWeather DefaultWeather;
            public TrackAmbience DefaultAmbience;
            public float SampleSpacingMeters;
            public bool EnforceClosure;

            public bool HasDefaultSurface;
            public bool HasDefaultNoise;
            public bool HasDefaultWidth;
            public bool HasDefaultWeather;
            public bool HasDefaultAmbience;
            public bool HasSampleSpacing;
            public bool HasEnforceClosure;

            public EdgeBuilder(string id)
            {
                Id = id;
            }

            public TrackGraphEdge? Build(
                TrackSurface defaultSurface,
                TrackNoise defaultNoise,
                float defaultWidth,
                TrackWeather defaultWeather,
                TrackAmbience defaultAmbience,
                float defaultSampleSpacing,
                bool defaultEnforceClosure,
                List<TrackLayoutError> errors)
            {
                if (string.IsNullOrWhiteSpace(FromNodeId) || string.IsNullOrWhiteSpace(ToNodeId))
                {
                    errors.Add(new TrackLayoutError(0, $"Edge '{Id}' is missing from/to node."));
                    return null;
                }

                if (GeometrySpans.Count == 0)
                {
                    errors.Add(new TrackLayoutError(0, $"Edge '{Id}' has no geometry spans."));
                    return null;
                }

                var surface = HasDefaultSurface ? DefaultSurface : defaultSurface;
                var noise = HasDefaultNoise ? DefaultNoise : defaultNoise;
                var width = HasDefaultWidth ? DefaultWidthMeters : defaultWidth;
                var weather = HasDefaultWeather ? DefaultWeather : defaultWeather;
                var ambience = HasDefaultAmbience ? DefaultAmbience : defaultAmbience;
                var spacing = HasSampleSpacing ? SampleSpacingMeters : defaultSampleSpacing;
                var closure = HasEnforceClosure ? EnforceClosure : defaultEnforceClosure;

                if (!TrackGraphValidation.IsFinite(width) || width <= 0f)
                {
                    errors.Add(new TrackLayoutError(0, $"Edge '{Id}' has invalid width."));
                    return null;
                }

                TrackGeometrySpec geometry;
                try
                {
                    geometry = new TrackGeometrySpec(GeometrySpans, spacing, closure);
                }
                catch (Exception ex)
                {
                    errors.Add(new TrackLayoutError(0, $"Edge '{Id}' geometry error: {ex.Message}"));
                    return null;
                }

                var profile = new TrackEdgeProfile(
                    surface,
                    noise,
                    width,
                    weather,
                    ambience,
                    SurfaceZones,
                    NoiseZones,
                    WidthZones,
                    SpeedZones,
                    Markers,
                    WeatherZones,
                    AmbienceZones,
                    Hazards,
                    Checkpoints,
                    HitLanes,
                    AllowedVehicles,
                    Emitters,
                    Triggers);

                return new TrackGraphEdge(
                    Id,
                    FromNodeId!,
                    ToNodeId!,
                    Name,
                    ShortName,
                    geometry,
                    profile,
                    ConnectorFromEdgeIds,
                    TurnDirection,
                    Metadata);
            }
        }

        private sealed class RouteBuilder
        {
            public string Id { get; }
            public List<string> EdgeIds { get; }
            public bool? IsLoop { get; }

            public RouteBuilder(string id, List<string> edgeIds, bool? isLoop)
            {
                Id = id;
                EdgeIds = edgeIds;
                IsLoop = isLoop;
            }
        }
    }
}
