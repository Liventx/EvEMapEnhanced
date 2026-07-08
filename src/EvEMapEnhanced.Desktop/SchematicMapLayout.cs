using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// Hybrid layout: whole regions are composed the way EVE's own in-game New Eden star map arranges
/// them, using a curated per-region anchor grid read off that map (bundled as an embedded resource,
/// keyed by region name); any region missing from the grid is placed from its real in-game centroid
/// via a best-fit transform. The anchor field is scaled up uniformly so every region's internal
/// layout has room to render. Systems inside a region are laid out exactly as they are on
/// dotlan.evemaps.com's region maps (coordinates extracted from Dotlan's own SVGs); regions Dotlan
/// doesn't cover (or covers too sparsely) fall back to a gate-driven force-directed graph. Systems
/// are never moved between regions and a region's internal arrangement is only ever
/// translated/scaled as one rigid group, never re-shuffled.
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
    /// The curated in-game anchor grid packs regions onto a compact 0-100 canvas, far smaller than a
    /// single region's internal footprint (hundreds of Dotlan pixel units). Scaling the anchor field
    /// up uniformly by a computed factor preserves the in-game relative arrangement while giving each
    /// region room to render before the overlap-separation pass nudges the tightest few clusters.
    /// </summary>
    private const double RegionSeparationPadding = 40.0;

    private readonly Dictionary<int, Point> _positions = new();
    private readonly Dictionary<int, Point> _regionCentroids = new();
    private readonly Dictionary<int, string> _regionNames = new();
    private readonly HashSet<(int A, int B)> _regionConnections = new();
    private readonly Dictionary<int, Point> _regionRawAnchors = new();
    private readonly Dictionary<int, List<int>> _regionSystemIds = new();

    // Curated-grid -> world affine transform (uniform scale about a shared center), captured so the
    // debug grid overlay can map the JSON's 0-100 coordinate space to/from screen for manual tuning.
    private Point _curatedFieldCenter;
    private double _curatedScale = 1.0;

    public IReadOnlyDictionary<int, Point> RegionCentroids => _regionCentroids;
    public IReadOnlyDictionary<int, string> RegionNames => _regionNames;

    /// <summary>Pairs of region ids (A &lt; B) that have at least one stargate crossing between them.</summary>
    public IReadOnlySet<(int A, int B)> RegionConnections => _regionConnections;

    /// <summary>True when at least one region was anchored from the curated in-game grid (vs. pure fallback).</summary>
    public bool HasCuratedGrid { get; private set; }

    /// <summary>The uniform factor the curated 0-100 grid is scaled by to reach world space.</summary>
    public double CuratedScale => _curatedScale;

    /// <summary>Region id -&gt; its raw anchor in curated-grid (0-100) space, before the uniform scale-up.</summary>
    public IReadOnlyDictionary<int, Point> RegionRawAnchors => _regionRawAnchors;

    /// <summary>Region id -&gt; the ids of the systems it contains (for whole-region hit-testing/dragging).</summary>
    public IReadOnlyDictionary<int, List<int>> RegionSystemIds => _regionSystemIds;

    /// <summary>
    /// Shifts a whole region (all its systems and its label centroid) by a world-space delta, then
    /// re-derives its curated-grid anchor so <see cref="RegionRawAnchors"/> and JSON export reflect
    /// the new position. Used by the interactive region-editing tool.
    /// </summary>
    public void MoveRegionBy(int regionId, Point delta)
    {
        if (!_regionSystemIds.TryGetValue(regionId, out var ids)) return;
        foreach (int id in ids)
        {
            if (_positions.TryGetValue(id, out var p))
                _positions[id] = new Point(p.X + delta.X, p.Y + delta.Y);
        }
        if (_regionCentroids.TryGetValue(regionId, out var c))
        {
            var moved = new Point(c.X + delta.X, c.Y + delta.Y);
            _regionCentroids[regionId] = moved;
            _regionRawAnchors[regionId] = WorldToCurated(moved);
        }
    }

    /// <summary>Maps a curated-grid (0-100) coordinate to world space (the space <see cref="GetPosition"/> returns).</summary>
    public Point CuratedToWorld(Point curated) => new(
        _curatedFieldCenter.X + (curated.X - _curatedFieldCenter.X) * _curatedScale,
        _curatedFieldCenter.Y + (curated.Y - _curatedFieldCenter.Y) * _curatedScale);

    /// <summary>Inverse of <see cref="CuratedToWorld"/>: world space back to curated-grid (0-100) coordinates.</summary>
    public Point WorldToCurated(Point world) => new(
        _curatedFieldCenter.X + (world.X - _curatedFieldCenter.X) / _curatedScale,
        _curatedFieldCenter.Y + (world.Y - _curatedFieldCenter.Y) / _curatedScale);

    public Point GetPosition(SolarSystem system) =>
        _positions.TryGetValue(system.Id, out var point) ? point : WorldProjection.RealPosition(system);

    /// <summary>
    /// Serializes the current per-region curated-grid anchors to the same JSON shape as the bundled
    /// <c>ingame-region-positions.json</c> (normalized region name -&gt; [x, y]), so the interactive
    /// editing tool can hand back a file the user pastes straight back into the project. Regions
    /// without a real name are skipped; entries are sorted by name for a stable diff.
    /// </summary>
    public string BuildRegionPositionsJson()
    {
        var entries = _regionRawAnchors
            .Where(kv => _regionNames.TryGetValue(kv.Key, out var n) && !n.StartsWith("Region ", StringComparison.Ordinal))
            .Select(kv => (Name: DotlanLayoutData.NormalizeRegionName(_regionNames[kv.Key]), Pos: kv.Value))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();

        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"_comment\": \"Curated region anchor grid extracted from EVE's in-game New Eden star map (region labels only). Keyed by lower-cased region name. Origin top-left, x grows east, y grows south, on an arbitrary 0-100 grid; SchematicMapLayout multiplies this field by _scale to reach world space so the arrangement renders exactly as authored.\",");
        sb.AppendLine($"  \"_scale\": {_curatedScale.ToString("0.###", ci)},");
        for (int i = 0; i < entries.Count; i++)
        {
            var (name, pos) = entries[i];
            string comma = i < entries.Count - 1 ? "," : "";
            sb.AppendLine($"  \"{name}\": [{pos.X.ToString("0.##", ci)}, {pos.Y.ToString("0.##", ci)}]{comma}");
        }
        sb.Append('}');
        return sb.ToString();
    }

    public static SchematicMapLayout Build(
        UniverseMap map,
        IReadOnlyDictionary<int, string>? regionNames,
        double? curatedScaleOverride = null)
    {
        const double edgeLength = 44.0;
        var dotlanPositions = DotlanLayoutData.Positions;

        var layout = new SchematicMapLayout();
        var byRegion = map.Systems.Values
            .GroupBy(s => s.RegionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var ingameRegionPositions = DotlanLayoutData.IngameRegionPositions;

        // 1. Build each region's internal layout (never re-shuffled) and record how much on-screen
        //    room it needs, its curated in-game anchor (if the region is in the bundled grid), and
        //    its real in-game centroid (the fallback anchor / affine reference for any region the
        //    grid doesn't cover).
        var localLayouts = new Dictionary<int, Dictionary<int, Point>>();
        var localCentroids = new Dictionary<int, Point>();
        var realCentroids = new Dictionary<int, Point>();
        var footprintRadii = new Dictionary<int, double>();
        var curatedAnchors = new Dictionary<int, Point>();

        foreach (var (regionId, systems) in byRegion)
        {
            string regionName = regionNames?.GetValueOrDefault(regionId) ?? $"Region {regionId}";
            layout._regionNames[regionId] = regionName;
            layout._regionSystemIds[regionId] = systems.Select(s => s.Id).ToList();

            var localPositions = BuildDotlanRegionLayout(systems, map, dotlanPositions)
                ?? BuildRegionLayout(systems, map, edgeLength);

            localLayouts[regionId] = localPositions;
            localCentroids[regionId] = new Point(
                localPositions.Values.Average(p => p.X),
                localPositions.Values.Average(p => p.Y));
            footprintRadii[regionId] = FootprintRadius(localPositions);

            var realPositions = systems.Select(WorldProjection.RealPosition).ToList();
            realCentroids[regionId] = new Point(
                realPositions.Average(p => p.X),
                realPositions.Average(p => p.Y));

            if (regionNames is not null &&
                ingameRegionPositions.TryGetValue(DotlanLayoutData.NormalizeRegionName(regionName), out var curated))
            {
                curatedAnchors[regionId] = curated;
            }
        }

        // 2. Give every region a raw anchor in the curated in-game grid: covered regions use their
        //    curated position directly; any region missing from the grid is mapped from its real
        //    centroid through the best-fit (per-axis) transform between real and curated space so it
        //    still lands in the right neighborhood. With no curated coverage at all (e.g. unit tests)
        //    we fall back to the real-centroid arrangement.
        var anchorRaw = BuildRawAnchors(realCentroids, curatedAnchors);

        // 3. Scale the anchor field up uniformly so each region's internal layout has room, keeping
        //    the in-game relative arrangement (angles/ordering) intact. When the curated grid carries
        //    an explicit scale (hand-tuned via the editor, persisted as "_scale"), honor it so the
        //    arrangement renders exactly as authored; otherwise derive one from the footprints. A
        //    caller-supplied override wins over both (used by tuning experiments).
        double anchorScale = curatedScaleOverride
            ?? (curatedAnchors.Count > 0 ? DotlanLayoutData.IngameRegionScale : null)
            ?? ComputeAnchorScale(anchorRaw, footprintRadii);
        var fieldCenter = anchorRaw.Count == 0
            ? new Point(0, 0)
            : new Point(anchorRaw.Values.Average(p => p.X), anchorRaw.Values.Average(p => p.Y));

        layout.HasCuratedGrid = curatedAnchors.Count > 0;
        layout._curatedFieldCenter = fieldCenter;
        layout._curatedScale = anchorScale;
        foreach (var (regionId, raw) in anchorRaw)
            layout._regionRawAnchors[regionId] = raw;

        foreach (var (regionId, _) in byRegion)
        {
            var raw = anchorRaw[regionId];
            var anchor = new Point(
                fieldCenter.X + (raw.X - fieldCenter.X) * anchorScale,
                fieldCenter.Y + (raw.Y - fieldCenter.Y) * anchorScale);
            layout._regionCentroids[regionId] = anchor;

            var localCentroid = localCentroids[regionId];
            foreach (var (systemId, localPos) in localLayouts[regionId])
            {
                layout._positions[systemId] = new Point(
                    anchor.X + (localPos.X - localCentroid.X),
                    anchor.Y + (localPos.Y - localCentroid.Y));
            }
        }

        // 4. Nudge apart only the handful of regions whose footprints still overlap, each moved
        //    as one rigid cluster so its internal layout is preserved.
        layout.SeparateOverlappingRegions(byRegion);
        layout.ComputeRegionConnections(map);
        return layout;
    }

    /// <summary>
    /// Half the bounding-box diagonal of a region's internal layout -- a stable "how much room
    /// does this cluster need" measure that stays sane for line-shaped regions too.
    /// </summary>
    private static double FootprintRadius(Dictionary<int, Point> positions)
    {
        if (positions.Count == 0) return 0.0;
        double minX = positions.Values.Min(p => p.X), maxX = positions.Values.Max(p => p.X);
        double minY = positions.Values.Min(p => p.Y), maxY = positions.Values.Max(p => p.Y);
        double w = maxX - minX, h = maxY - minY;
        return 0.5 * Math.Sqrt(w * w + h * h);
    }

    /// <summary>
    /// Gives every region a raw anchor in the curated in-game grid. Regions present in the grid use
    /// their curated position; regions missing from it are projected from their real centroid through
    /// the best-fit per-axis transform between real and curated space (so a stray region still lands
    /// near where it belongs). If nothing is curated at all, the real centroids are used unchanged.
    /// </summary>
    private static Dictionary<int, Point> BuildRawAnchors(
        IReadOnlyDictionary<int, Point> realCentroids,
        IReadOnlyDictionary<int, Point> curatedAnchors)
    {
        var anchors = new Dictionary<int, Point>(realCentroids.Count);
        if (curatedAnchors.Count == 0)
        {
            foreach (var (id, real) in realCentroids) anchors[id] = real;
            return anchors;
        }

        // Fit curated = a*real + b independently per axis over the covered regions.
        var coveredIds = curatedAnchors.Keys.ToList();
        var realX = coveredIds.Select(id => realCentroids[id].X).ToList();
        var realY = coveredIds.Select(id => realCentroids[id].Y).ToList();
        var (ax, bx) = LinearFit(realX, coveredIds.Select(id => curatedAnchors[id].X).ToList());
        var (ay, by) = LinearFit(realY, coveredIds.Select(id => curatedAnchors[id].Y).ToList());

        foreach (var (id, real) in realCentroids)
        {
            anchors[id] = curatedAnchors.TryGetValue(id, out var curated)
                ? curated
                : new Point(ax * real.X + bx, ay * real.Y + by);
        }
        return anchors;
    }

    /// <summary>Ordinary least-squares fit of y = a*x + b; a is 0 when x has no spread.</summary>
    private static (double A, double B) LinearFit(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        int n = xs.Count;
        if (n == 0) return (0.0, 0.0);
        double meanX = xs.Average(), meanY = ys.Average();
        double cov = 0.0, varX = 0.0;
        for (int i = 0; i < n; i++)
        {
            double dx = xs[i] - meanX;
            cov += dx * (ys[i] - meanY);
            varX += dx * dx;
        }
        double a = varX > 1e-9 ? cov / varX : 0.0;
        return (a, meanY - a * meanX);
    }

    /// <summary>
    /// Picks the uniform factor to scale the anchor field by. For each region it finds the factor
    /// needed to clear its most-crowded neighbor, then takes a high percentile across all regions so
    /// the great majority of clusters separate purely by uniform scaling (which preserves the in-game
    /// arrangement) and only the tightest few are left for the local separation pass. Absolute size
    /// is irrelevant -- the map view auto-fits -- so only the anchor-spacing/footprint ratio matters,
    /// which this keeps comparable across the universe.
    /// </summary>
    private static double ComputeAnchorScale(
        IReadOnlyDictionary<int, Point> realCentroids,
        IReadOnlyDictionary<int, double> footprintRadii)
    {
        const double percentile = 0.85;
        const double minScale = 1.0;

        var ids = realCentroids.Keys.ToList();
        if (ids.Count < 2) return minScale;

        var perRegionNeed = new List<double>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            var pi = realCentroids[ids[i]];
            double tightest = 0.0;
            for (int j = 0; j < ids.Count; j++)
            {
                if (j == i) continue;
                var pj = realCentroids[ids[j]];
                double dx = pi.X - pj.X, dy = pi.Y - pj.Y;
                double dist = Math.Sqrt(Math.Max(dx * dx + dy * dy, 1e-9));
                double need = (footprintRadii[ids[i]] + footprintRadii[ids[j]] + RegionSeparationPadding) / dist;
                if (need > tightest) tightest = need;
            }
            perRegionNeed.Add(tightest);
        }

        perRegionNeed.Sort();
        int idx = (int)Math.Clamp(Math.Floor(percentile * (perRegionNeed.Count - 1)), 0, perRegionNeed.Count - 1);
        return Math.Max(minScale, perRegionNeed[idx]);
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
        const double padding = RegionSeparationPadding;
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
