using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Structures;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Routing;

public class JumpBridgePathfinderTests
{
    [Fact]
    public void FindsRoute_AcrossChainedAnsiblexLinks()
    {
        var map = TestFixtures.BuildLinearGateMap();
        map.LoadStructures(new[]
        {
            new UserStructure { SolarSystemId = 1, Kind = StructureKind.Ansiblex, Name = "A-B", LinkedSystemId = 2 },
            new UserStructure { SolarSystemId = 2, Kind = StructureKind.Ansiblex, Name = "B-E", LinkedSystemId = 5 },
        });

        var route = JumpBridgePathfinder.FindRoute(map, 1, 5);

        Assert.NotNull(route);
        Assert.Equal(2, route!.JumpCount);
        Assert.All(route.Legs, leg => Assert.Equal(JumpMethod.JumpBridge, leg.Method));
    }

    [Fact]
    public void ReturnsNull_WhenNoStructuresLoaded()
    {
        var map = TestFixtures.BuildLinearGateMap();
        var route = JumpBridgePathfinder.FindRoute(map, 1, 5);
        Assert.Null(route);
    }
}
