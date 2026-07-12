using EvEMapEnhanced.Core.Models;

namespace EvEMapEnhanced.Core.Routing;

/// <summary>A resolved stargate-only route: an ordered list of system IDs, origin to destination inclusive.</summary>
public sealed record GateRoute(IReadOnlyList<int> SystemIds)
{
    private static readonly HashSet<(int From, int To)> EmptyWormholeEdges = new();

    /// <summary>Undirected wormhole hops used along this route (for display and step typing).</summary>
    public IReadOnlySet<(int From, int To)> WormholeEdges { get; init; } = EmptyWormholeEdges;

    public int JumpCount => Math.Max(0, SystemIds.Count - 1);

    public bool IsWormholeHop(int fromSystemId, int toSystemId) =>
        WormholeEdges.Contains((fromSystemId, toSystemId))
        || WormholeEdges.Contains((toSystemId, fromSystemId));
}

/// <summary>
/// Dijkstra-based shortest-path router over the stargate graph, with ESI-compatible
/// security preferences (Shorter / Safer / LessSecure) and optional hard avoidance.
/// </summary>
public static class GatePathfinder
{
    public static GateRoute? FindRoute(UniverseMap map, int fromSystemId, int toSystemId, RouteFilterOptions? options = null)
    {
        options ??= new RouteFilterOptions();

        var route = FindRouteInternal(map, fromSystemId, toSystemId, options, applyHardFilters: true);
        if (route is not null) return route;

        if (options.AllowFallbackIfBlocked)
        {
            return FindRouteInternal(map, fromSystemId, toSystemId, options, applyHardFilters: false);
        }

        return null;
    }

    private static GateRoute? FindRouteInternal(UniverseMap map, int fromSystemId, int toSystemId, RouteFilterOptions options, bool applyHardFilters)
    {
        if (map.Get(fromSystemId) is null || map.Get(toSystemId) is null) return null;
        if (fromSystemId == toSystemId) return new GateRoute(new[] { fromSystemId });

        var distances = new Dictionary<int, double> { [fromSystemId] = 0 };
        var previous = new Dictionary<int, (int Prev, bool ViaWormhole)>();
        var visited = new HashSet<int>();
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(fromSystemId, 0);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            if (current == toSystemId) break;

            double currentDist = distances[current];

            foreach (var (neighborId, viaWormhole) in EnumerateNeighbors(map, current, options))
            {
                if (applyHardFilters && IsHardBlocked(map, neighborId, options) && neighborId != toSystemId) continue;

                var neighbor = map.Get(neighborId);
                if (neighbor is null) continue;

                double edgeCost = GateEdgeCost(neighbor, options);
                edgeCost += options.SystemPenalty?.Invoke(neighborId) ?? 0.0;
                double candidate = currentDist + edgeCost;

                if (!distances.TryGetValue(neighborId, out double known) || candidate < known - 1e-12)
                {
                    distances[neighborId] = candidate;
                    previous[neighborId] = (current, viaWormhole);
                    queue.Enqueue(neighborId, candidate);
                }
            }
        }

        if (!distances.ContainsKey(toSystemId)) return null;

        var path = new List<int> { toSystemId };
        var usedWormholeEdges = new HashSet<(int From, int To)>();
        int node = toSystemId;
        while (node != fromSystemId)
        {
            if (!previous.TryGetValue(node, out var step)) return null;
            if (step.ViaWormhole)
                usedWormholeEdges.Add((step.Prev, node));
            path.Add(step.Prev);
            node = step.Prev;
        }
        path.Reverse();

        return new GateRoute(path) { WormholeEdges = usedWormholeEdges };
    }

    private static IEnumerable<(int NeighborId, bool ViaWormhole)> EnumerateNeighbors(
        UniverseMap map, int systemId, RouteFilterOptions options)
    {
        foreach (int neighborId in map.GateNeighbors(systemId))
            yield return (neighborId, false);

        if (options.WormholeAdjacency?.TryGetValue(systemId, out var wormholeNeighbors) != true
            || wormholeNeighbors is null)
            yield break;

        foreach (int neighborId in wormholeNeighbors)
            yield return (neighborId, true);
    }

    /// <summary>
    /// Per-hop cost for entering <paramref name="destination"/>.
    /// Shorter = pure hop count (ESI Shorter). Safer/LessSecure use soft security
    /// biases that prefer alternate paths at equal hop count (DOTLAN-style), without
    /// forcing huge detours like the raw ESI Safer exponential penalty does.
    /// </summary>
    public static double GateEdgeCost(SolarSystem destination, RouteFilterOptions options)
    {
        double security = destination.Security;

        return options.Preference switch
        {
            GateRoutePreference.Shorter => 1.0,
            GateRoutePreference.Safer => security < 0.45
                ? 1.0 + (0.45 - security) * 2.0
                : 1.0 - (security - 0.45) * 0.05,
            GateRoutePreference.LessSecure => security >= 0.45
                ? 1.0 + (security - 0.45) * 2.0
                : 1.0 - (0.45 - security) * 0.05,
            _ => 1.0,
        };
    }

    private static bool IsHardBlocked(UniverseMap map, int systemId, RouteFilterOptions options)
    {
        if (options.AvoidSystemIds.Contains(systemId)) return true;

        var system = map.Get(systemId);
        if (system is null) return false;

        if (options.AvoidLowSec && system.IsLowSec) return true;
        if (options.AvoidNullSec && system.IsNullSec) return true;

        return false;
    }
}
