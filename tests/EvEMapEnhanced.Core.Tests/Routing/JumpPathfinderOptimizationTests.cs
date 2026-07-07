using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Ships;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Routing;

public class JumpPathfinderOptimizationTests
{
    [Fact]
    public void PrefersShorterTotalLy_WhenHopCountIsEqual()
    {
        // Direct A->B is 10 LY (out of 7 LY range). Two 2-hop paths:
        // via C (5+5 LY) or via D (6.4+6.4 LY). Optimizer must pick C.
        double ly = SpaceMath.LightYearsToMeters(1);
        var systems = new[]
        {
            new SolarSystem(1, "A", 1, 1, 0.0, 0, 0, 0),
            new SolarSystem(2, "B", 1, 1, 0.0, ly * 10, 0, 0),
            new SolarSystem(3, "C", 1, 1, 0.0, ly * 5, 0, 0),
            new SolarSystem(4, "D", 1, 1, 0.0, ly * 5, ly * 4, 0),
        };
        var map = new UniverseMap(systems, Array.Empty<Stargate>());
        var carrier = ShipHulls.ByClass(CapitalShipClass.Carrier).First();
        var skills = new PilotSkills { JumpDriveCalibration = 5 };

        var route = JumpPathfinder.FindRoute(map, carrier, skills, 1, 2, JumpMethod.Cyno);

        Assert.NotNull(route);
        Assert.Equal(2, route!.JumpCount);
        Assert.Equal(3, route.Legs[0].ToSystemId);
        Assert.InRange(route.TotalDistanceLy, 9.9, 10.1);
    }
}
