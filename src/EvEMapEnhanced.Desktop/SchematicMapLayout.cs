using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// Dotlan / new EVE map style layout: regions are spaced apart on a schematic grid,
/// systems inside each region keep gate topology but are scaled up for readability.
/// </summary>
public sealed class SchematicMapLayout
{
    private readonly Dictionary<int, Point> _positions = new();
    private readonly Dictionary<int, Rect> _regionBounds = new();
    private readonly Dictionary<int, string> _regionNames = new();

    public IReadOnlyDictionary<int, Rect> RegionBounds => _regionBounds;
    public IReadOnlyDictionary<int, string> RegionNames => _regionNames;

    public Point GetPosition(SolarSystem system) =>
        _positions.TryGetValue(system.Id, out var point) ? point : default;

    public static SchematicMapLayout Build(UniverseMap map, IReadOnlyDictionary<int, string>? regionNames)
    {
        const double regionSpacing = 95.0;
        const double regionInnerSize = 72.0;
        const double regionPadding = 8.0;

        var layout = new SchematicMapLayout();
        var byRegion = map.Systems.Values
            .GroupBy(s => s.RegionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var regionCentroids = new List<(int RegionId, double X, double Z)>();
        foreach (var (regionId, systems) in byRegion)
        {
            double cx = systems.Average(s => SpaceMath.MetersToLightYears(s.X));
            double cz = systems.Average(s => SpaceMath.MetersToLightYears(s.Z));
            regionCentroids.Add((regionId, cx, cz));
        }

        if (regionCentroids.Count == 0) return layout;

        double minRx = regionCentroids.Min(r => r.X);
        double maxRx = regionCentroids.Max(r => r.X);
        double minRz = regionCentroids.Min(r => r.Z);
        double maxRz = regionCentroids.Max(r => r.Z);
        double spanRx = Math.Max(maxRx - minRx, 1.0);
        double spanRz = Math.Max(maxRz - minRz, 1.0);

        foreach (var (regionId, systems) in byRegion)
        {
            string? name = regionNames?.GetValueOrDefault(regionId);
            layout._regionNames[regionId] = name ?? $"Region {regionId}";

            var centroid = regionCentroids.First(r => r.RegionId == regionId);
            double gridX = (centroid.X - minRx) / spanRx;
            double gridZ = (centroid.Z - minRz) / spanRz;

            double regionOriginX = gridX * regionSpacing * 3.2;
            double regionOriginZ = gridZ * regionSpacing * 3.2;

            var localPoints = systems
                .Select(s => new
                {
                    System = s,
                    X = SpaceMath.MetersToLightYears(s.X),
                    Z = SpaceMath.MetersToLightYears(s.Z),
                })
                .ToList();

            double localCx = localPoints.Average(p => p.X);
            double localCz = localPoints.Average(p => p.Z);
            double localMinX = localPoints.Min(p => p.X - localCx);
            double localMaxX = localPoints.Max(p => p.X - localCx);
            double localMinZ = localPoints.Min(p => p.Z - localCz);
            double localMaxZ = localPoints.Max(p => p.Z - localCz);

            double localSpanX = Math.Max(localMaxX - localMinX, 0.5);
            double localSpanZ = Math.Max(localMaxZ - localMinZ, 0.5);
            double scale = Math.Min(regionInnerSize / localSpanX, regionInnerSize / localSpanZ);

            double minPx = double.PositiveInfinity, minPz = double.PositiveInfinity;
            double maxPx = double.NegativeInfinity, maxPz = double.NegativeInfinity;

            foreach (var point in localPoints)
            {
                double x = regionOriginX + (point.X - localCx) * scale;
                double z = regionOriginZ + (point.Z - localCz) * scale;
                layout._positions[point.System.Id] = new Point(x, z);

                minPx = Math.Min(minPx, x);
                maxPx = Math.Max(maxPx, x);
                minPz = Math.Min(minPz, z);
                maxPz = Math.Max(maxPz, z);
            }

            layout._regionBounds[regionId] = new Rect(
                minPx - regionPadding,
                minPz - regionPadding,
                maxPx - minPx + regionPadding * 2,
                maxPz - minPz + regionPadding * 2);
        }

        return layout;
    }
}
