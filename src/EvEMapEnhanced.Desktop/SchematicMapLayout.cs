using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// Dotlan / new-Eden-map style schematic layout: each region is drawn as its own
/// non-overlapping panel on a grid, and systems inside a region are laid out with a
/// simple force-directed (spring) algorithm driven by gate connectivity rather than by
/// real-world coordinates. This is what makes Dotlan-style maps readable: dense clusters
/// of systems that sit almost on top of each other in real 3D space get spread out into
/// a legible, evenly-spaced node graph.
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
        const double edgeLength = 32.0;
        const double regionPadding = 26.0;
        const double regionGap = 40.0;

        var layout = new SchematicMapLayout();
        var byRegion = map.Systems.Values
            .GroupBy(s => s.RegionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (byRegion.Count == 0) return layout;

        // Compute a real-space centroid per region purely to decide the reading order
        // (north-to-south, west-to-east) used when packing region panels onto the grid.
        var regionOrder = byRegion.Keys
            .Select(regionId =>
            {
                var systems = byRegion[regionId];
                double cx = systems.Average(s => SpaceMath.MetersToLightYears(s.X));
                double cz = systems.Average(s => SpaceMath.MetersToLightYears(s.Z));
                return (RegionId: regionId, X: cx, Z: cz);
            })
            .ToList();

        int regionCount = regionOrder.Count;
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(regionCount)));
        int rows = (int)Math.Ceiling(regionCount / (double)columns);

        // Bucket into rows by Z (north/south), then sort each row by X (west/east), so the
        // grid roughly preserves New Eden's geography while guaranteeing zero overlap.
        var sortedByZ = regionOrder.OrderBy(r => r.Z).ToList();
        var regionCells = new Dictionary<int, (int Row, int Col)>();
        int perRow = (int)Math.Ceiling(regionCount / (double)rows);
        for (int row = 0; row < rows; row++)
        {
            var rowItems = sortedByZ.Skip(row * perRow).Take(perRow).OrderBy(r => r.X).ToList();
            for (int col = 0; col < rowItems.Count; col++)
            {
                regionCells[rowItems[col].RegionId] = (row, col);
            }
        }

        var localLayouts = new Dictionary<int, (Dictionary<int, Point> Positions, Rect Bounds)>();
        foreach (var (regionId, systems) in byRegion)
        {
            var local = BuildRegionLayout(systems, map, edgeLength);
            localLayouts[regionId] = local;
        }

        double maxWidth = localLayouts.Values.Max(l => l.Bounds.Width) + regionPadding * 2;
        double maxHeight = localLayouts.Values.Max(l => l.Bounds.Height) + regionPadding * 2;
        double cellWidth = maxWidth + regionGap;
        double cellHeight = maxHeight + regionGap;

        foreach (var (regionId, systems) in byRegion)
        {
            string name = regionNames?.GetValueOrDefault(regionId) ?? $"Region {regionId}";
            layout._regionNames[regionId] = name;

            var (row, col) = regionCells[regionId];
            double originX = col * cellWidth;
            double originZ = row * cellHeight;

            var (localPositions, localBounds) = localLayouts[regionId];

            // Center the (possibly smaller) region content within its allotted cell.
            double offsetX = originX + (cellWidth - regionGap - localBounds.Width) / 2 - localBounds.X;
            double offsetZ = originZ + (cellHeight - regionGap - localBounds.Height) / 2 - localBounds.Y;

            foreach (var (systemId, localPos) in localPositions)
            {
                layout._positions[systemId] = new Point(localPos.X + offsetX, localPos.Y + offsetZ);
            }

            layout._regionBounds[regionId] = new Rect(
                originX,
                originZ,
                cellWidth - regionGap,
                cellHeight - regionGap);
        }

        return layout;
    }

    /// <summary>
    /// Fruchterman-Reingold-style force-directed layout for the systems within a single
    /// region, using only intra-region gate edges as attractive forces. Deterministic
    /// (seeded by a golden-angle spiral, no RNG) so re-layout is stable between rebuilds.
    /// </summary>
    private static (Dictionary<int, Point> Positions, Rect Bounds) BuildRegionLayout(
        List<SolarSystem> systems, UniverseMap map, double edgeLength)
    {
        int n = systems.Count;
        var pos = new Dictionary<int, Point>(n);

        if (n == 1)
        {
            pos[systems[0].Id] = new Point(0, 0);
            return (pos, new Rect(-edgeLength / 2, -edgeLength / 2, edgeLength, edgeLength));
        }

        var idSet = systems.Select(s => s.Id).ToHashSet();
        var edges = new List<(int A, int B)>();
        foreach (var system in systems)
        {
            foreach (int neighborId in map.GateNeighbors(system.Id))
            {
                if (neighborId <= system.Id || !idSet.Contains(neighborId)) continue;
                edges.Add((system.Id, neighborId));
            }
        }

        // Deterministic golden-angle spiral seed avoids the "everything piled in the middle"
        // problem plain force-directed layouts have with a fixed/zero starting point.
        const double goldenAngle = 2.399963229728653; // pi * (3 - sqrt(5))
        for (int i = 0; i < n; i++)
        {
            double r = Math.Sqrt(i + 0.5) * edgeLength * 0.9;
            double theta = i * goldenAngle;
            pos[systems[i].Id] = new Point(r * Math.Cos(theta), r * Math.Sin(theta));
        }

        double k = edgeLength;
        double temperature = Math.Max(edgeLength, Math.Sqrt(n) * edgeLength * 0.5);
        int iterations = Math.Clamp(60 + n * 2, 60, 260);
        double cooling = Math.Pow(0.02 / temperature, 1.0 / iterations);

        var disp = new Dictionary<int, Point>(n);
        var ids = systems.Select(s => s.Id).ToList();

        for (int iter = 0; iter < iterations; iter++)
        {
            foreach (int id in ids) disp[id] = default;

            for (int i = 0; i < n; i++)
            {
                var pi = pos[ids[i]];
                for (int j = i + 1; j < n; j++)
                {
                    var pj = pos[ids[j]];
                    double dx = pi.X - pj.X, dy = pi.Y - pj.Y;
                    double distSq = dx * dx + dy * dy;
                    double dist = Math.Sqrt(Math.Max(distSq, 0.0001));
                    double force = (k * k) / dist;
                    double fx = dx / dist * force, fy = dy / dist * force;
                    disp[ids[i]] = new Point(disp[ids[i]].X + fx, disp[ids[i]].Y + fy);
                    disp[ids[j]] = new Point(disp[ids[j]].X - fx, disp[ids[j]].Y - fy);
                }
            }

            foreach (var (a, b) in edges)
            {
                var pa = pos[a];
                var pb = pos[b];
                double dx = pa.X - pb.X, dy = pa.Y - pb.Y;
                double dist = Math.Sqrt(Math.Max(dx * dx + dy * dy, 0.0001));
                double force = (dist * dist) / k;
                double fx = dx / dist * force, fy = dy / dist * force;
                disp[a] = new Point(disp[a].X - fx, disp[a].Y - fy);
                disp[b] = new Point(disp[b].X + fx, disp[b].Y + fy);
            }

            foreach (int id in ids)
            {
                var d = disp[id];
                double len = Math.Sqrt(Math.Max(d.X * d.X + d.Y * d.Y, 0.0001));
                double clamped = Math.Min(len, temperature);
                var p = pos[id];
                pos[id] = new Point(p.X + d.X / len * clamped, p.Y + d.Y / len * clamped);
            }

            temperature *= cooling;
        }

        double minX = pos.Values.Min(p => p.X), maxX = pos.Values.Max(p => p.X);
        double minY = pos.Values.Min(p => p.Y), maxY = pos.Values.Max(p => p.Y);
        var bounds = new Rect(minX, minY, Math.Max(maxX - minX, edgeLength), Math.Max(maxY - minY, edgeLength));

        return (pos, bounds);
    }
}
