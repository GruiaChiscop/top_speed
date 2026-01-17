using System;
using System.IO;
using TopSpeed.Core;
using TopSpeed.Data;
using TopSpeed.Tracks.Areas;
using TopSpeed.Tracks.Topology;

namespace TopSpeed.Tracks.Map
{
    internal static class TrackMapLoader
    {
        private const string MapExtension = ".tsm";

        public static bool LooksLikeMap(string nameOrPath)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath))
                return false;
            if (Path.HasExtension(nameOrPath))
                return string.Equals(Path.GetExtension(nameOrPath), MapExtension, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        public static bool TryResolvePath(string nameOrPath, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(nameOrPath))
                return false;

            if (nameOrPath.IndexOfAny(new[] { '\\', '/' }) >= 0)
            {
                path = nameOrPath;
                return File.Exists(path) && LooksLikeMap(path);
            }

            if (!Path.HasExtension(nameOrPath))
            {
                path = Path.Combine(AssetPaths.Root, "Tracks", nameOrPath + MapExtension);
                return File.Exists(path);
            }

            path = Path.Combine(AssetPaths.Root, "Tracks", nameOrPath);
            return File.Exists(path) && LooksLikeMap(path);
        }

        public static TrackMap Load(string nameOrPath)
        {
            var path = ResolvePath(nameOrPath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Track map not found.", path);

            var definition = TrackMapFormat.Parse(path);

            var map = new TrackMap(definition.Metadata.Name, definition.Metadata.CellSizeMeters)
            {
                Weather = definition.Metadata.Weather,
                Ambience = definition.Metadata.Ambience,
                DefaultSurface = definition.Metadata.DefaultSurface,
                DefaultNoise = definition.Metadata.DefaultNoise,
                DefaultWidthMeters = definition.Metadata.DefaultWidthMeters,
                StartX = definition.Metadata.StartX,
                StartZ = definition.Metadata.StartZ,
                StartHeading = definition.Metadata.StartHeading
            };

            foreach (var entry in definition.Cells)
            {
                var cell = entry.Value;
                map.MergeCell(entry.Key.X, entry.Key.Z, cell.Exits, cell.Surface, cell.Noise, cell.WidthMeters, cell.IsSafeZone, cell.Zone);
            }

            foreach (var sector in definition.Sectors)
                map.AddSector(sector);
            foreach (var area in definition.Areas)
                map.AddArea(area);
            foreach (var shape in definition.Shapes)
                map.AddShape(shape);
            foreach (var portal in definition.Portals)
                map.AddPortal(portal);
            foreach (var link in definition.Links)
                map.AddLink(link);
            foreach (var pathDef in definition.Paths)
                map.AddPath(pathDef);
            foreach (var beacon in definition.Beacons)
                map.AddBeacon(beacon);
            foreach (var marker in definition.Markers)
                map.AddMarker(marker);
            foreach (var approach in definition.Approaches)
                map.AddApproach(approach);

            AddSafeZoneRing(map, definition.Metadata);
            AddOuterRing(map, definition.Metadata);
            AddRingCells(map);

            return map;
        }

        private static string ResolvePath(string nameOrPath)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath))
                return nameOrPath;
            if (nameOrPath.IndexOfAny(new[] { '\\', '/' }) >= 0)
                return nameOrPath;
            if (!Path.HasExtension(nameOrPath))
                return Path.Combine(AssetPaths.Root, "Tracks", nameOrPath + MapExtension);
            return Path.Combine(AssetPaths.Root, "Tracks", nameOrPath);
        }

        private static void AddSafeZoneRing(TrackMap map, TrackMapMetadata metadata)
        {
            if (map == null || metadata == null)
                return;

            var ringMeters = metadata.SafeZoneRingMeters;
            if (ringMeters <= 0f)
                return;

            if (!map.TryGetBounds(out var minX, out var minZ, out var maxX, out var maxZ))
                return;

            var cellSize = map.CellSizeMeters;
            var innerMinX = minX * cellSize;
            var innerMaxX = maxX * cellSize;
            var innerMinZ = minZ * cellSize;
            var innerMaxZ = maxZ * cellSize;

            var name = string.IsNullOrWhiteSpace(metadata.SafeZoneName) ? "Safe zone" : metadata.SafeZoneName!;
            var surface = metadata.SafeZoneSurface;
            var noise = metadata.SafeZoneNoise;
            var flags = TrackAreaFlags.SafeZone;

            AddRingShapeArea(map, "__safe_zone", innerMinX, innerMinZ, innerMaxX - innerMinX, innerMaxZ - innerMinZ, ringMeters, name, surface, noise, TrackAreaType.SafeZone, flags);
        }

        private static void AddOuterRing(TrackMap map, TrackMapMetadata metadata)
        {
            if (map == null || metadata == null)
                return;

            var ringMeters = metadata.OuterRingMeters;
            if (ringMeters <= 0f)
                return;

            if (!map.TryGetBounds(out var minX, out var minZ, out var maxX, out var maxZ))
                return;

            var cellSize = map.CellSizeMeters;
            var innerMinX = minX * cellSize;
            var innerMaxX = maxX * cellSize;
            var innerMinZ = minZ * cellSize;
            var innerMaxZ = maxZ * cellSize;

            var name = string.IsNullOrWhiteSpace(metadata.OuterRingName) ? "Outer ring" : metadata.OuterRingName!;
            var surface = metadata.OuterRingSurface;
            var noise = metadata.OuterRingNoise;
            var flags = metadata.OuterRingFlags;
            var areaType = metadata.OuterRingType;

            AddRingShapeArea(map, "__outer_ring", innerMinX, innerMinZ, innerMaxX - innerMinX, innerMaxZ - innerMinZ, ringMeters, name, surface, noise, areaType, flags);
        }

        private static void AddRingShapeArea(
            TrackMap map,
            string idPrefix,
            float innerMinX,
            float innerMinZ,
            float innerWidth,
            float innerHeight,
            float ringWidth,
            string name,
            TrackSurface surface,
            TrackNoise noise,
            TrackAreaType areaType,
            TrackAreaFlags flags)
        {
            if (ringWidth <= 0f || innerWidth <= 0f || innerHeight <= 0f)
                return;

            var shapeId = idPrefix + "_shape";
            var areaId = idPrefix + "_area";
            map.AddShape(new ShapeDefinition(shapeId, ShapeType.Ring, innerMinX, innerMinZ, innerWidth, innerHeight, ringWidth: ringWidth));
            map.AddArea(new TrackAreaDefinition(areaId, areaType, shapeId, name, surface, noise, null, flags));
        }

        private static void AddRingCells(TrackMap map)
        {
            if (map == null || map.Shapes.Count == 0 || map.Areas.Count == 0)
                return;

            var areaManager = map.BuildAreaManager();
            foreach (var area in map.Areas)
            {
                if (!areaManager.TryGetShape(area.ShapeId, out var shape))
                    continue;
                if (shape.Type != ShapeType.Ring)
                    continue;
                if (!TryGetRingBounds(shape, out var minX, out var minZ, out var maxX, out var maxZ))
                    continue;

                AddRingCells(map, areaManager, area, minX, minZ, maxX, maxZ);
            }
        }

        private static void AddRingCells(
            TrackMap map,
            TrackAreaManager areaManager,
            TrackAreaDefinition area,
            float minX,
            float minZ,
            float maxX,
            float maxZ)
        {
            var cellSize = map.CellSizeMeters;
            var minCellX = (int)Math.Floor(minX / cellSize);
            var maxCellX = (int)Math.Ceiling(maxX / cellSize);
            var minCellZ = (int)Math.Floor(minZ / cellSize);
            var maxCellZ = (int)Math.Ceiling(maxZ / cellSize);

            for (var x = minCellX; x <= maxCellX; x++)
            {
                for (var z = minCellZ; z <= maxCellZ; z++)
                {
                    var world = map.CellToWorld(x, z);
                    var position = new System.Numerics.Vector2(world.X, world.Z);
                    if (!areaManager.Contains(area, position))
                        continue;
                    map.GetOrCreateCell(x, z);
                }
            }
        }

        private static bool TryGetRingBounds(
            ShapeDefinition shape,
            out float minX,
            out float minZ,
            out float maxX,
            out float maxZ)
        {
            minX = 0f;
            minZ = 0f;
            maxX = 0f;
            maxZ = 0f;

            var ringWidth = Math.Abs(shape.RingWidth);
            if (ringWidth <= 0f)
                return false;

            if (shape.Radius > 0f)
            {
                var inner = Math.Abs(shape.Radius);
                var outer = inner + ringWidth;
                minX = shape.X - outer;
                maxX = shape.X + outer;
                minZ = shape.Z - outer;
                maxZ = shape.Z + outer;
                return true;
            }

            var innerMinX = Math.Min(shape.X, shape.X + shape.Width);
            var innerMaxX = Math.Max(shape.X, shape.X + shape.Width);
            var innerMinZ = Math.Min(shape.Z, shape.Z + shape.Height);
            var innerMaxZ = Math.Max(shape.Z, shape.Z + shape.Height);
            if (innerMaxX <= innerMinX || innerMaxZ <= innerMinZ)
                return false;

            minX = innerMinX - ringWidth;
            maxX = innerMaxX + ringWidth;
            minZ = innerMinZ - ringWidth;
            maxZ = innerMaxZ + ringWidth;
            return true;
        }
    }
}
