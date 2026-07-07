using EvEMapEnhanced.Core.Jump;
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
/// Finds a minimum-hop-count chain of capital jumps between two systems, where an edge
/// exists between any two systems within the hull's current jump range. Uses the
/// hull/skill jump range to build the dynamic graph, and respects hard avoidance filters
/// (e.g. cyno-jammed or blacklisted systems) on intermediate/destination systems.
///
/// This purely geometric shortest-hop search is intentionally decoupled from fatigue/fuel:
/// once a path is chosen, call <see cref="RouteSimulator.SimulateJumpRoute"/> to obtain the
/// full fatigue/fuel/cooldown timeline for it.
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

        var distances = new Dictionary<int, int> { [fromSystemId] = 0 };
        var previous = new Dictionary<int, (int From, double DistanceLy)>();
        var frontier = new Queue<int>();
        frontier.Enqueue(fromSystemId);

        while (frontier.Count > 0)
        {
            int current = frontier.Dequeue();
            int currentHops = distances[current];
            if (currentHops >= maxHops) continue;

            var currentSystem = map.Get(current)!;
            foreach (var (candidate, distLy) in map.SystemsWithinRange(currentSystem, rangeLy))
            {
                bool isDestination = candidate.Id == toSystemId;
                if (IsBlocked(candidate.Id, options) && !isDestination) continue;

                // A standard cyno cannot be lit in a cyno-jammed system; covert cynos are immune to jammers.
                if (method == JumpMethod.Cyno && map.IsCynoJammed(candidate.Id) && !isDestination) continue;

                if (!distances.ContainsKey(candidate.Id))
                {
                    distances[candidate.Id] = currentHops + 1;
                    previous[candidate.Id] = (current, distLy);
                    frontier.Enqueue(candidate.Id);

                    if (candidate.Id == toSystemId)
                    {
                        return BuildRoute(previous, fromSystemId, toSystemId, method);
                    }
                }
            }
        }

        return distances.ContainsKey(toSystemId) ? BuildRoute(previous, fromSystemId, toSystemId, method) : null;
    }

    private static bool IsBlocked(int systemId, RouteFilterOptions options) => options.AvoidSystemIds.Contains(systemId);

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
