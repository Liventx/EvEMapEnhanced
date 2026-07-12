using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Stats;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Routing;

public class WormholeRoutingGraphTests
{
    [Fact]
    public void BuildAdjacency_AddsTurnurRemoteEdge()
    {
        var map = TestFixtures.BuildLinearGateMap();
        var connections = new[]
        {
            new WormholeConnection(
                "t1", WormholeHubKind.Turnur, 4, "Turnur", 2, "Bravo",
                "SIG-A", "SIG-B", "S641", "Capital", 12, null, true),
        };

        var adj = WormholeRoutingGraph.BuildAdjacency(map, connections, []);

        Assert.Contains(2, adj[4]);
        Assert.Contains(4, adj[2]);
    }

    [Fact]
    public void BuildAdjacency_LinksTheraRemotesAsClique()
    {
        var map = TestFixtures.BuildLinearGateMap();
        var connections = new[]
        {
            new WormholeConnection(
                "a", WormholeHubKind.Thera, WormholeHubCatalog.TheraSystemId, "Thera", 1, "Alpha",
                "SIG-A", "SIG-B", "S641", "Capital", 12, null, true),
            new WormholeConnection(
                "b", WormholeHubKind.Thera, WormholeHubCatalog.TheraSystemId, "Thera", 5, "Echo",
                "SIG-C", "SIG-D", "S641", "Capital", 12, null, true),
        };

        var adj = WormholeRoutingGraph.BuildAdjacency(map, connections, []);

        Assert.Contains(5, adj[1]);
        Assert.Contains(1, adj[5]);
    }

    [Fact]
    public void BuildAdjacency_AddsManualMarkerWithExitSystem()
    {
        var map = TestFixtures.BuildLinearGateMap();
        var markers = new[]
        {
            new ManualWormholeMarker(1, 5, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)),
        };

        var adj = WormholeRoutingGraph.BuildAdjacency(map, [], markers);

        Assert.Contains(5, adj[1]);
        Assert.Contains(1, adj[5]);
    }
}
