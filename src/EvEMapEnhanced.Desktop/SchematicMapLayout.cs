using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// Dotlan-style hybrid layout: each region is anchored the same place Dotlan puts it on its
/// own universe overview map (falling back to the region's real in-game centroid for the rare
/// region Dotlan's universe map doesn't know about), while systems inside a region are laid
/// out exactly as they are on dotlan.evemaps.com's region maps (using coordinates extracted
/// from Dotlan's own SVGs). Regions Dotlan doesn't cover (or covers too sparsely -- e.g. a
/// handful of very new systems) fall back to a gate-driven force-directed graph so every
/// system still gets a sane, non-overlapping position.
/// </summary>
public sealed class SchematicMapLayout
{
    /// <summary>
    /// Minimum fraction of a region's systems that must have a known Dotlan position before
    /// we trust Dotlan's layout for that region; below this, the whole region falls back to
    /// the force-directed layout instead of mixing in too many guessed positions.
    /// </summary>
    private const double MinDotlanCoverage = 0.6;

    /// <summary>
    /// Dotlan's universe overview map packs 70-odd regions into a roughly 1024x768 canvas, far
    /// smaller than the footprint of a single region laid out at Dotlan's own per-system scale
    /// (hundreds of units wide). Scaling the region anchor grid up preserves Dotlan's relative
    /// region placement/angles while giving each region's internal layout room to breathe
    /// before the overlap-separation pass has to fight for space.
    /// </summary>
    private const double RegionAnchorScale = 22.0;

    private readonly Dictionary<int, Point> _positions = new();
    private readonly Dictionary<int, Point> _regionCentroids = new();
    private readonly Dictionary<int, string> _regionNames = new();
    private readonly HashSet<(int A, int B)> _regionConnections = new();

    public IReadOnlyDictionary<int, Point> RegionCentroids => _regionCentroids;
    public IReadOnlyDictionary<int, string> RegionNames => _regionNames;

    /// <summary>Pairs of region ids (A &lt; B) that have at least one stargate crossing between them.</summary>
    public IReadOnlySet<(int A, int B)> RegionConnections => _regionConnections;

    public Point GetPosition(SolarSystem system) =>
        _positions.TryGetValue(system.Id, out var point) ? point : WorldProjection.RealPosition(system);

    public static SchematicMapLayout Build(UniverseMap map, IReadOnlyDictionary<int, string>? regionNames)
    {
        const double edgeLength = 44.0;
        var dotlanPositions = DotlanLayoutData.Positions;
        var dotlanRegionPositions = DotlanLayoutData.RegionPositions;

        var layout = new SchematicMapLayout();
        var byRegion = map.Systems.Values
            .GroupBy(s => s.RegionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (regionId, systems) in byRegion)
        {
            layout._regionNames[regionId] = regionNames?.GetValueOrDefault(regionId) ?? $"Region {regionId}";

            Point anchor;
            if (dotlanRegionPositions.TryGetValue(regionId, out var dotlanAnchor))
            {
                anchor = new Point(dotlanAnchor.X * RegionAnchorScale, dotlanAnchor.Y * RegionAnchorScale);
            }
            else
            {
                var realPositions = systems.Select(WorldProjection.RealPosition).ToList();
                anchor = new Point(realPositions.Average(p => p.X), realPositions.Average(p => p.Y));
            }
            layout._regionCentroids[regionId] = anchor;

            var localPositions = BuildDotlanRegionLayout(systems, map, dotlanPositions)
                ?? BuildRegionLayout(systems, map, edgeLength);
            var localCentroid = new Point(
                localPositions.Values.Average(p => p.X),
                localPositions.Values.Average(p => p.Y));

            foreach (var (systemId, localPos) in localPositions)
            {
                layout._positions[systemId] = new Point(
                    anchor.X + (localPos.X - localCentroid.X),
                    anchor.Y + (localPos.Y - localCentroid.Y));
            }
        }

        layout.SeparateOverlappingRegions(byRegion);
        layout.ComputeRegionConnections(map);
        return layout;
    }

    /// <summary>
    /// Records every pair of regions joined by at least one real stargate, so the map can draw
    /// a single connector line between the two regions (rather than a chaotic web of individual
    /// system-to-system lines crossing the whole schematic layout).
    /// </summary>
    private void ComputeRegionConnections(UniverseMap map)
    {
        foreach (var system in map.Systems.Values)
        {
            foreach (int neighborId in map.GateNeighbors(system.Id))
            {
                if (map.Get(neighborId) is not { } neighbor) continue;
                if (neighbor.RegionId == system.RegionId) continue;
                int a = Math.Min(system.RegionId, neighbor.RegionId);
                int b = Math.Max(system.RegionId, neighbor.RegionId);
                _regionConnections.Add((a, b));
            }
        }
    }

    /// <summary>
    /// Nudges whole region clusters apart when their schematic footprints overlap, keeping
    /// each region's internal Dotlan layout intact.
    /// </summary>
    private void SeparateOverlappingRegions(Dictionary<int, List<SolarSystem>> byRegion)
    {
        const double padding = 40.0;
        const int passes = 80;

        var regionIds = byRegion.Keys.ToList();
        var offsets = regionIds.ToDictionary(id => id, _ => new Point(0, 0));

        for (int pass = 0; pass < passes; pass++)
        {
            bool moved = false;
            for (int i = 0; i < regionIds.Count; i++)
            {
                for (int j = i + 1; j < regionIds.Count; j++)
                {
                    int a = regionIds[i], b = regionIds[j];
                    var boundsA = RegionBounds(byRegion[a], offsets[a]);
                    var boundsB = RegionBounds(byRegion[b], offsets[b]);
                    if (!BoundsOverlap(boundsA, boundsB, padding)) continue;

                    var centerA = boundsA.Center;
                    var centerB = boundsB.Center;
                    double dx = centerB.X - centerA.X, dy = centerB.Y - centerA.Y;
                    double dist = Math.Sqrt(Math.Max(dx * dx + dy * dy, 0.01));
                    double overlapX = (boundsA.Width / 2 + boundsB.Width / 2 + padding) - Math.Abs(dx);
                    double overlapY = (boundsA.Height / 2 + boundsB.Height / 2 + padding) - Math.Abs(dy);
                    double push = Math.Max(Math.Max(overlapX, overlapY), 8.0) * 0.35;
                    double fx = dx / dist * push, fy = dy / dist * push;

                    var offA = offsets[a];
                    var offB = offsets[b];
                    offsets[a] = new Point(offA.X - fx, offA.Y - fy);
                    offsets[b] = new Point(offB.X + fx, offB.Y + fy);
                    moved = true;
                }
            }
            if (!moved) break;
        }

        foreach (var (regionId, systems) in byRegion)
        {
            var offset = offsets[regionId];
            if (offset.X == 0 && offset.Y == 0) continue;
            foreach (var system in systems)
            {
                var p = _positions[system.Id];
                _positions[system.Id] = new Point(p.X + offset.X, p.Y + offset.Y);
            }
            _regionCentroids[regionId] = new Point(
                _regionCentroids[regionId].X + offset.X,
                _regionCentroids[regionId].Y + offset.Y);
        }
    }

    private static bool BoundsOverlap(Rect a, Rect b, double padding)
    {
        var ap = new Rect(a.X - padding, a.Y - padding, a.Width + padding * 2, a.Height + padding * 2);
        var bp = new Rect(b.X - padding, b.Y - padding, b.Width + padding * 2, b.Height + padding * 2);
        return ap.Intersects(bp);
    }

    private Rect RegionBounds(List<SolarSystem> systems, Point offset)
    {
        var points = systems.Select(s => _positions[s.Id]).Select(p => new Point(p.X + offset.X, p.Y + offset.Y)).ToList();
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        return new Rect(minX, minY, Math.Max(maxX - minX, 1), Math.Max(maxY - minY, 1));
    }

    /// <summary>
    /// Builds a region's local layout straight from Dotlan's own published system positions
    /// so the in-region arrangement matches dotlan.evemaps.com exactly (not just "Dotlan
    /// style"). Returns null when Dotlan's coverage of this region's current systems is too
    /// thin (new/renamed regions, systems added after the data was captured, etc.), so the
    /// caller falls back to the force-directed layout for the whole region instead of mixing
    /// in too many guessed positions.
    /// </summary>
    private static Dictionary<int, Point>? BuildDotlanRegionLayout(
        List<SolarSystem> systems, UniverseMap map, IReadOnlyDictionary<int, Point> dotlanPositions)
    {
        int covered = systems.Count(s => dotlanPositions.ContainsKey(s.Id));
        if (covered == 0 || covered < systems.Count * MinDotlanCoverage) return null;

        var pos = new Dictionary<int, Point>(systems.Count);
        foreach (var system in systems)
        {
            if (dotlanPositions.TryGetValue(system.Id, out var p)) pos[system.Id] = p;
        }

        // A handful of systems Dotlan doesn't know about yet (freshly added, not on the
        // captured map) get parked at the average of whichever gate neighbors Dotlan *does*
        // place, or the region's known centroid as a last resort -- never left unpositioned.
        var missing = systems.Where(s => !pos.ContainsKey(s.Id)).ToList();
        if (missing.Count > 0)
        {
            var regionCentroid = new Point(pos.Values.Average(p => p.X), pos.Values.Average(p => p.Y));
            foreach (var system in missing)
            {
                var knownNeighborPositions = map.GateNeighbors(system.Id)
                    .Where(pos.ContainsKey)
                    .Select(id => pos[id])
                    .ToList();
                pos[system.Id] = knownNeighborPositions.Count > 0
                    ? new Point(knownNeighborPositions.Average(p => p.X), knownNeighborPositions.Average(p => p.Y))
                    : regionCentroid;
            }
        }

        return pos;
    }

    /// <summary>
    /// Gate-graph force-directed layout for one region. Seeded with a BFS layering from the
    /// highest-degree hub so the result has Dotlan's typical spine/branch structure rather
    /// than a random blob. Used only as a fallback for regions Dotlan doesn't cover.
    /// </summary>
    private static Dictionary<int, Point> BuildRegionLayout(
        List<SolarSystem> systems, UniverseMap map, double edgeLength)
    {
        int n = systems.Count;
        var pos = new Dictionary<int, Point>(n);
        if (n == 1)
        {
            pos[systems[0].Id] = new Point(0, 0);
            return pos;
        }

        var idSet = systems.Select(s => s.Id).ToHashSet();
        var edges = new List<(int A, int B)>();
        var adjacency = new Dictionary<int, List<int>>();
        foreach (var system in systems)
        {
            adjacency[system.Id] = new List<int>();
            foreach (int neighborId in map.GateNeighbors(system.Id))
            {
                if (!idSet.Contains(neighborId)) continue;
                adjacency[system.Id].Add(neighborId);
                if (neighborId > system.Id) edges.Add((system.Id, neighborId));
            }
        }

        int rootId = systems
            .OrderByDescending(s => adjacency[s.Id].Count)
            .ThenBy(s => s.Id)
            .First().Id;

        var layers = new Dictionary<int, int>();
        var layerNodes = new Dictionary<int, List<int>>();
        var queue = new Queue<int>();
        layers[rootId] = 0;
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int layer = layers[current];
            if (!layerNodes.TryGetValue(layer, out var list))
            {
                list = new List<int>();
                layerNodes[layer] = list;
            }
            list.Add(current);

            foreach (int neighborId in adjacency[current])
            {
                if (layers.ContainsKey(neighborId)) continue;
                layers[neighborId] = layer + 1;
                queue.Enqueue(neighborId);
            }
        }

        foreach (var system in systems)
        {
            if (layers.ContainsKey(system.Id)) continue;
            int maxLayer = layers.Values.DefaultIfEmpty(0).Max() + 1;
            layers[system.Id] = maxLayer;
            if (!layerNodes.TryGetValue(maxLayer, out var list))
            {
                list = new List<int>();
                layerNodes[maxLayer] = list;
            }
            list.Add(system.Id);
        }

        foreach (var (layer, nodes) in layerNodes)
        {
            nodes.Sort();
            double y = layer * edgeLength * 1.15;
            double totalWidth = (nodes.Count - 1) * edgeLength * 1.1;
            for (int i = 0; i < nodes.Count; i++)
            {
                double x = nodes.Count == 1 ? 0 : -totalWidth / 2 + i * edgeLength * 1.1;
                pos[nodes[i]] = new Point(x, y);
            }
        }

        double k = edgeLength;
        double temperature = Math.Max(edgeLength, Math.Sqrt(n) * edgeLength * 0.55);
        int iterations = Math.Clamp(100 + n * 3, 100, 400);
        double cooling = Math.Pow(0.015 / temperature, 1.0 / iterations);

        var ids = systems.Select(s => s.Id).ToList();
        var disp = new Dictionary<int, Point>(n);

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
                    double dist = Math.Sqrt(Math.Max(dx * dx + dy * dy, 0.0001));
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

        return pos;
    }
}
