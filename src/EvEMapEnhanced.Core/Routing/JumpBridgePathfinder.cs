using EvEMapEnhanced.Core.Jump;

namespace EvEMapEnhanced.Core.Routing;

/// <summary>
/// Finds the shortest chain across user-defined jump bridges (Ansiblex / legacy custom
/// jump bridges). Unlike <see cref="JumpPathfinder"/>, edges here are fixed links from
/// the structure registry, not range-based cyno jumps -- any ship class may traverse
/// them (subject to the same fatigue accrual as any other jump-drive-mediated travel).
/// </summary>
public static class JumpBridgePathfinder
{
    public static JumpRoute? FindRoute(UniverseMap map, int fromSystemId, int toSystemId, RouteFilterOptions? options = null, int maxHops = 25)
    {
        options ??= new RouteFilterOptions();

        if (map.Get(fromSystemId) is null || map.Get(toSystemId) is null) return null;
        if (fromSystemId == toSystemId) return new JumpRoute(Array.Empty<JumpRouteLeg>());

        var previous = new Dictionary<int, (int From, double DistanceLy)>();
        var visited = new HashSet<int> { fromSystemId };
        var frontier = new Queue<(int Id, int Hops)>();
        frontier.Enqueue((fromSystemId, 0));

        while (frontier.Count > 0)
        {
            var (current, hops) = frontier.Dequeue();
            if (hops >= maxHops) continue;

            foreach (var (neighborId, distanceLy, _) in map.JumpBridgeNeighbors(current))
            {
                if (options.AvoidSystemIds.Contains(neighborId) && neighborId != toSystemId) continue;
                if (!visited.Add(neighborId)) continue;

                previous[neighborId] = (current, distanceLy);
                if (neighborId == toSystemId)
                {
                    return BuildRoute(previous, fromSystemId, toSystemId);
                }
                frontier.Enqueue((neighborId, hops + 1));
            }
        }

        return null;
    }

    private static JumpRoute BuildRoute(Dictionary<int, (int From, double DistanceLy)> previous, int fromId, int toId)
    {
        var legs = new List<JumpRouteLeg>();
        int node = toId;
        while (node != fromId)
        {
            var (prev, distLy) = previous[node];
            legs.Add(new JumpRouteLeg(prev, node, distLy, JumpMethod.JumpBridge));
            node = prev;
        }
        legs.Reverse();
        return new JumpRoute(legs);
    }
}
