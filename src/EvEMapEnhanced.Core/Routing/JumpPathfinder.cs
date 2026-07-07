using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Ships;

namespace EvEMapEnhanced.Core.Routing;

/// <summary>One leg of a capital jump-chain route (before fatigue/fuel simulation is applied).</summary>
public sealed record JumpRouteLeg(int FromSystemId, int ToSystemId, double DistanceLy, JumpMethod Method);

public sealed record JumpRoute(IReadOnlyList<JumpRouteLeg> Legs)
{
    public int JumpCount => Legs.Count;
    public double TotalDistanceLy => Legs.Sum(l => l.DistanceLy);
}

/// <summary>
/// Finds a minimum-hop capital jump chain, breaking ties by minimum total light-year
/// distance (matching DOTLAN jump planner behaviour). Uses the hull/skill jump range
/// to build the dynamic graph and respects hard avoidance filters.
/// </summary>
public static class JumpPathfinder
{
    public static JumpRoute? FindRoute(
        UniverseMap map,
        ShipHull hull,
        PilotSkills skills,
        int fromSystemId,
        int toSystemId,
        JumpMethod method,
        RouteFilterOptions? options = null,
        int maxHops = 25)
    {
        options ??= new RouteFilterOptions();

        var origin = map.Get(fromSystemId);
        var destination = map.Get(toSystemId);
        if (origin is null || destination is null) return null;
        if (fromSystemId == toSystemId) return new JumpRoute(Array.Empty<JumpRouteLeg>());

        double rangeLy = JumpSimulator.MaxRangeLy(hull, skills);

        // Lexicographic cost: fewer hops first, then less total LY travelled.
        var best = new Dictionary<int, (int Hops, double TotalLy)> { [fromSystemId] = (0, 0) };
        var previous = new Dictionary<int, (int From, double DistanceLy)>();
        var queue = new PriorityQueue<int, (int Hops, double TotalLy)>();
        queue.Enqueue(fromSystemId, (0, 0));

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            var (currentHops, currentLy) = best[current];
            if (current == toSystemId) break;
            if (currentHops >= maxHops) continue;

            var currentSystem = map.Get(current)!;
            foreach (var (candidate, distLy) in map.SystemsWithinRange(currentSystem, rangeLy))
            {
                bool isDestination = candidate.Id == toSystemId;
                if (IsBlocked(map, candidate, options, method, isDestination)) continue;

                if (method == JumpMethod.Cyno && map.IsCynoJammed(candidate.Id) && !isDestination) continue;

                int nextHops = currentHops + 1;
                double nextLy = currentLy + distLy;
                var nextCost = (nextHops, nextLy);

                if (!best.TryGetValue(candidate.Id, out var known) || IsBetter(nextCost, known))
                {
                    best[candidate.Id] = nextCost;
                    previous[candidate.Id] = (current, distLy);
                    queue.Enqueue(candidate.Id, nextCost);
                }
            }
        }

        return best.ContainsKey(toSystemId)
            ? BuildRoute(previous, fromSystemId, toSystemId, method)
            : null;
    }

    private static bool IsBetter((int Hops, double TotalLy) candidate, (int Hops, double TotalLy) known) =>
        candidate.Hops < known.Hops || (candidate.Hops == known.Hops && candidate.TotalLy < known.TotalLy - 1e-9);

    private static bool IsBlocked(UniverseMap map, SolarSystem system, RouteFilterOptions options, JumpMethod method, bool isDestination)
    {
        if (options.AvoidSystemIds.Contains(system.Id)) return true;
        if (options.AvoidLowSec && system.IsLowSec) return true;
        if (options.AvoidNullSec && system.IsNullSec) return true;
        if (!JumpRules.IsValidJumpLanding(system, method)) return true;
        return false;
    }

    private static JumpRoute BuildRoute(Dictionary<int, (int From, double DistanceLy)> previous, int fromId, int toId, JumpMethod method)
    {
        var legs = new List<JumpRouteLeg>();
        int node = toId;
        while (node != fromId)
        {
            var (prev, distLy) = previous[node];
            legs.Add(new JumpRouteLeg(prev, node, distLy, method));
            node = prev;
        }
        legs.Reverse();
        return new JumpRoute(legs);
    }
}
