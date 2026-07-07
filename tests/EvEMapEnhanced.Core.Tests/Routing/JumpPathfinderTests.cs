using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Ships;
using EvEMapEnhanced.Core.Structures;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Routing;

public class JumpPathfinderTests
{
    private static readonly ShipHull BlackOpsHull = ShipHulls.ByClass(CapitalShipClass.BlackOps).First();

    [Fact]
    public void FindsMinimumHopChain_WithinJumpRange()
    {
        var map = TestFixtures.BuildJumpOnlyMap();
        var skills = new PilotSkills(); // JDC 0

        // Carrier base range 3.5 LY; each hop here is 2 LY, so min chain is 3 hops (J0->J1->J2->J3).
        var carrier = ShipHulls.ByClass(CapitalShipClass.Carrier).First();
        var route = JumpPathfinder.FindRoute(map, carrier, skills, 101, 104, JumpMethod.Cyno);

        Assert.NotNull(route);
        Assert.Equal(3, route!.JumpCount);
    }

    [Fact]
    public void CynoJammer_BlocksStandardCyno_WhenDirectJumpOutOfRange()
    {
        var (map, startId, relayId, endId) = TestFixtures.BuildCynoJammerMap();
        map.LoadStructures(new[]
        {
            new UserStructure { SolarSystemId = relayId, Kind = StructureKind.CynoJammer, Name = "Test Jammer" },
        });

        var skills = new PilotSkills(); // JDC 0 -> Black Ops range 4.0 LY; direct Start-End is 6 LY (out of range).
        var route = JumpPathfinder.FindRoute(map, BlackOpsHull, skills, startId, endId, JumpMethod.Cyno);

        Assert.Null(route); // only path was via the jammed Relay system
    }

    [Fact]
    public void CovertCyno_IgnoresCynoJammer()
    {
        var (map, startId, relayId, endId) = TestFixtures.BuildCynoJammerMap();
        map.LoadStructures(new[]
        {
            new UserStructure { SolarSystemId = relayId, Kind = StructureKind.CynoJammer, Name = "Test Jammer" },
        });

        var skills = new PilotSkills();
        var route = JumpPathfinder.FindRoute(map, BlackOpsHull, skills, startId, endId, JumpMethod.CovertCyno);

        Assert.NotNull(route);
        Assert.Equal(2, route!.JumpCount);
    }

    [Fact]
    public void NoRouteBeyondMaxHops_ReturnsNull()
    {
        var map = TestFixtures.BuildJumpOnlyMap();
        var carrier = ShipHulls.ByClass(CapitalShipClass.Carrier).First();
        var skills = new PilotSkills();

        var route = JumpPathfinder.FindRoute(map, carrier, skills, 101, 104, JumpMethod.Cyno, maxHops: 1);
        Assert.Null(route);
    }
}
