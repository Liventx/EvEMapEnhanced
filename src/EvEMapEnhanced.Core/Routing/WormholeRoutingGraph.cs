using EvEMapEnhanced.Core.Stats;

namespace EvEMapEnhanced.Core.Routing;

/// <summary>
/// Builds temporary wormhole adjacency for gate routing from EvE-Scout Thera/Turnur connections
/// and user manual markers with a resolved exit system.
/// </summary>
public static class WormholeRoutingGraph
{
    public static IReadOnlyDictionary<int, IReadOnlyList<int>> BuildAdjacency(
        UniverseMap map,
        IReadOnlyList<WormholeConnection> eveScoutConnections,
        IEnumerable<ManualWormholeMarker> manualMarkers)
    {
        var adj = new Dictionary<int, HashSet<int>>();

        void AddEdge(int a, int b)
        {
            if (a == b) return;
            if (map.Get(a) is null || map.Get(b) is null) return;

            if (!adj.TryGetValue(a, out var fromSet))
            {
                fromSet = new HashSet<int>();
                adj[a] = fromSet;
            }

            if (!adj.TryGetValue(b, out var toSet))
            {
                toSet = new HashSet<int>();
                adj[b] = toSet;
            }

            fromSet.Add(b);
            toSet.Add(a);
        }

        var theraRemotes = new List<int>();
        foreach (var connection in eveScoutConnections)
        {
            if (map.Get(connection.RemoteSystemId) is null) continue;

            if (connection.Hub == WormholeHubKind.Turnur && map.Get(connection.HubSystemId) is not null)
            {
                AddEdge(connection.HubSystemId, connection.RemoteSystemId);
            }
            else if (connection.Hub == WormholeHubKind.Thera)
            {
                theraRemotes.Add(connection.RemoteSystemId);
            }
        }

        foreach (int a in theraRemotes)
        {
            foreach (int b in theraRemotes)
            {
                if (a != b) AddEdge(a, b);
            }
        }

        foreach (var marker in manualMarkers)
        {
            if (marker.ExitSystemId is int exitId)
                AddEdge(marker.SolarSystemId, exitId);
        }

        return adj.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<int>)pair.Value.ToList());
    }

    public static bool IsWormholeEdge(
        IReadOnlyDictionary<int, IReadOnlyList<int>>? adjacency,
        int fromSystemId,
        int toSystemId)
    {
        if (adjacency is null) return false;
        return adjacency.TryGetValue(fromSystemId, out var neighbors) && neighbors.Contains(toSystemId);
    }
}
