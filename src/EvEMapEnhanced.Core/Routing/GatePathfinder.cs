using EvEMapEnhanced.Core.Models;

namespace EvEMapEnhanced.Core.Routing;

/// <summary>A resolved stargate-only route: an ordered list of system IDs, origin to destination inclusive.</summary>
public sealed record GateRoute(IReadOnlyList<int> SystemIds)
{
    public int JumpCount => Math.Max(0, SystemIds.Count - 1);
}

/// <summary>
/// Dijkstra-based shortest-path router over the stargate graph, with optional hard
/// avoidance (lowsec / nullsec / explicit system list) and a soft per-system cost
/// penalty (e.g. from live kill activity statistics).
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
        var previous = new Dictionary<int, int>();
        var visited = new HashSet<int>();
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(fromSystemId, 0);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            if (current == toSystemId) break;

            double currentDist = distances[current];

            foreach (int neighborId in map.GateNeighbors(current))
            {
                if (applyHardFilters && IsHardBlocked(map, neighborId, options) && neighborId != toSystemId) continue;

                double edgeCost = 1.0 + (options.SystemPenalty?.Invoke(neighborId) ?? 0.0);
                double candidate = currentDist + edgeCost;

                if (!distances.TryGetValue(neighborId, out double known) || candidate < known)
                {
                    distances[neighborId] = candidate;
                    previous[neighborId] = current;
                    queue.Enqueue(neighborId, candidate);
                }
            }
        }

        if (!distances.ContainsKey(toSystemId)) return null;

        var path = new List<int> { toSystemId };
        int node = toSystemId;
        while (node != fromSystemId)
        {
            if (!previous.TryGetValue(node, out int prev)) return null;
            path.Add(prev);
            node = prev;
        }
        path.Reverse();
        return new GateRoute(path);
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
