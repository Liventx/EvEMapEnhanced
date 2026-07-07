using EvEMapEnhanced.Core.Models;

namespace EvEMapEnhanced.Core.Routing;

/// <summary>
/// Filters the raw SDE universe down to space that is actually reachable by a normal
/// capsuleer: real k-space regions only. Two kinds of "space" are excluded:
///
///  1. Non-k-space by construction: wormhole regions (regionId >= 11,000,000),
///     Abyssal Deadspace (>= 12,000,000), and CCP's internal VR/GM test universes
///     (>= 14,000,000) -- these are never reachable via stargate or jump drive.
///  2. Disconnected placeholder/test regions that happen to carry a "normal" region ID
///     (e.g. internal dev catalogs) but have little to no stargate connectivity to the
///     rest of New Eden. Rather than hardcoding region names/IDs -- which drifts as CCP
///     changes the SDE -- this is detected structurally: any stargate-connected group of
///     systems smaller than <see cref="MinAccessibleClusterSize"/> is dropped, so only
///     the main New Eden cluster and legitimately-sized pockets (e.g. Pochven) remain.
/// </summary>
public static class PilotSpaceFilter
{
    private const int WormholeRegionIdStart = 11_000_000;

    /// <summary>Default: stargate-connected clusters smaller than this are treated as unreachable test/dev artifacts.</summary>
    public const int DefaultMinAccessibleClusterSize = 10;

    public static IReadOnlyList<SolarSystem> FilterToAccessibleSystems(
        IReadOnlyList<SolarSystem> systems, IReadOnlyList<Stargate> stargates, int minClusterSize = DefaultMinAccessibleClusterSize)
    {
        var candidates = systems
            .Where(s => s.RegionId < WormholeRegionIdStart)
            .ToDictionary(s => s.Id);

        var adjacency = new Dictionary<int, List<int>>();
        void AddEdge(int a, int b)
        {
            if (!candidates.ContainsKey(a) || !candidates.ContainsKey(b)) return;
            if (!adjacency.TryGetValue(a, out var list))
            {
                list = new List<int>();
                adjacency[a] = list;
            }
            list.Add(b);
        }

        foreach (var gate in stargates)
        {
            AddEdge(gate.FromSystemId, gate.ToSystemId);
            AddEdge(gate.ToSystemId, gate.FromSystemId);
        }

        var visited = new HashSet<int>();
        var accessible = new List<SolarSystem>();

        foreach (int id in candidates.Keys)
        {
            if (!visited.Add(id)) continue;

            var component = new List<int> { id };
            var queue = new Queue<int>();
            queue.Enqueue(id);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!adjacency.TryGetValue(current, out var neighbors)) continue;
                foreach (int neighbor in neighbors)
                {
                    if (visited.Add(neighbor))
                    {
                        component.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            if (component.Count >= minClusterSize)
            {
                accessible.AddRange(component.Select(cid => candidates[cid]));
            }
        }

        return accessible;
    }
}
