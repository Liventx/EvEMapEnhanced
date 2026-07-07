using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Ships;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Routing;

public class HybridRouterTests
{
    [Fact]
    public void PrefersPureJumpChain_WhenItBeatsGateRoute()
    {
        var map = TestFixtures.BuildLinearGateMap(); // 4 gate hops, 1 LY apart
        var jumpFreighter = ShipHulls.ByClass(CapitalShipClass.JumpFreighter).First(); // 5.0 LY base range
        var skills = new PilotSkills();

        var route = HybridRouter.FindRoute(map, jumpFreighter, skills, 1, 5, JumpMethod.Cyno);

        Assert.NotNull(route);
        Assert.Equal(1, route!.CapitalJumps);
        Assert.Equal(0, route.GateJumps);
    }

    [Fact]
    public void CombinesGateAndJump_WhenDestinationOnlyReachableByJumpFromLandingZone()
    {
        var (map, startId, _, destinationId) = TestFixtures.BuildHybridFixture();
        var carrier = ShipHulls.ByClass(CapitalShipClass.Carrier).First(); // 3.5 LY base range
        var skills = new PilotSkills();

        var route = HybridRouter.FindRoute(map, carrier, skills, startId, destinationId, JumpMethod.Cyno);

        Assert.NotNull(route);
        Assert.Equal(3, route!.GateJumps);
        Assert.Equal(1, route.CapitalJumps);

        // The final step must be the capital jump into the isolated destination.
        var lastStep = route.Steps[^1];
        Assert.Equal(RouteStepKind.Jump, lastStep.Kind);
        Assert.Equal(destinationId, lastStep.ToSystemId);
    }

    [Fact]
    public void ReturnsNull_WhenNoStrategyReachesDestination()
    {
        var (map, startId, _, destinationId) = TestFixtures.BuildHybridFixture();
        // Titan has a shorter base range (3.0 LY); LandingZone->Destination is 2 LY so it's
        // still reachable, but let's instead prove "no route" behavior by picking an
        // origin/destination pair that is truly unreachable: disconnect via a system id
        // that doesn't exist.
        var carrier = ShipHulls.ByClass(CapitalShipClass.Carrier).First();
        var skills = new PilotSkills();

        var route = HybridRouter.FindRoute(map, carrier, skills, startId, 999999, JumpMethod.Cyno);
        Assert.Null(route);
    }
}
