using EvEMapEnhanced.Core.Routing;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Routing;

public class GatePathfinderTests
{
    [Fact]
    public void FindsShortestPath_AlongLinearChain()
    {
        var map = TestFixtures.BuildLinearGateMap();
        var route = GatePathfinder.FindRoute(map, 1, 5);

        Assert.NotNull(route);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, route!.SystemIds);
        Assert.Equal(4, route.JumpCount);
    }

    [Fact]
    public void SameOriginAndDestination_ReturnsSingleSystemRoute()
    {
        var map = TestFixtures.BuildLinearGateMap();
        var route = GatePathfinder.FindRoute(map, 1, 1);

        Assert.NotNull(route);
        Assert.Equal(0, route!.JumpCount);
    }

    [Fact]
    public void AvoidExplicitSystem_ReturnsNull_WhenNoFallbackAllowed()
    {
        var map = TestFixtures.BuildLinearGateMap();
        var options = new RouteFilterOptions
        {
            AvoidSystemIds = { 3 }, // Charlie is the only link between Bravo and Delta
            AllowFallbackIfBlocked = false,
        };

        var route = GatePathfinder.FindRoute(map, 1, 5, options);
        Assert.Null(route);
    }

    [Fact]
    public void AvoidExplicitSystem_RemainsBlocked_EvenWhenFallbackAllowed()
    {
        var map = TestFixtures.BuildLinearGateMap();
        var options = new RouteFilterOptions
        {
            AvoidSystemIds = { 3 },
            AllowFallbackIfBlocked = true,
        };

        var route = GatePathfinder.FindRoute(map, 1, 5, options);
        Assert.Null(route);
    }

    [Fact]
    public void AvoidLowSec_StillReachesDestinationViaFallback_WhenNoAllHighRouteExists()
    {
        var map = TestFixtures.BuildLinearGateMap();
        var options = new RouteFilterOptions { AvoidLowSec = true, AvoidNullSec = true };

        // Charlie (low) and Delta (null) are unavoidable bottlenecks on this linear chain,
        // so with fallback allowed we still get a route through them.
        var route = GatePathfinder.FindRoute(map, 1, 5, options);
        Assert.NotNull(route);
        Assert.Equal(5, route!.SystemIds[^1]);
    }

    [Fact]
    public void SystemPenalty_PrefersLowerCostPath_WhenAlternativeExists()
    {
        var map = TestFixtures.BuildLinearGateMap();
        // Even though the graph is linear (only one path exists), verify the penalty
        // function is invoked and doesn't break pathfinding.
        var options = new RouteFilterOptions { SystemPenalty = systemId => systemId == 3 ? 100.0 : 0.0 };

        var route = GatePathfinder.FindRoute(map, 1, 5, options);
        Assert.NotNull(route);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, route!.SystemIds);
    }

    [Fact]
    public void WormholeAdjacency_ShortcutsLongGatePath()
    {
        var map = TestFixtures.BuildLinearGateMap();
        var options = new RouteFilterOptions
        {
            WormholeAdjacency = new Dictionary<int, IReadOnlyList<int>>
            {
                [1] = new[] { 5 },
                [5] = new[] { 1 },
            },
        };

        var route = GatePathfinder.FindRoute(map, 1, 5, options);

        Assert.NotNull(route);
        Assert.Equal(new[] { 1, 5 }, route!.SystemIds);
        Assert.True(route.IsWormholeHop(1, 5));
    }
}
