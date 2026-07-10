using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Ships;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Jump;

public class JumpRulesTests
{
    [Theory]
    [InlineData(0.9, false)]
    [InlineData(0.5, false)]
    [InlineData(0.45, false)]
    [InlineData(0.4, true)]
    [InlineData(0.0, true)]
    public void AllowsCynoField_OnlyInLowAndNullSec(double security, bool expected)
    {
        var system = new SolarSystem(1, "Test", 1, 1, security, 0, 0, 0);
        Assert.Equal(expected, JumpRules.AllowsCynoField(system));
    }

    [Fact]
    public void AllowsCynoField_RejectsPochven_EvenInNullSec()
    {
        var pochven = new SolarSystem(1, "Rairomon", 1, JumpRules.PochvenRegionId, 0.0, 0, 0, 0);
        Assert.False(JumpRules.AllowsCynoField(pochven));
        Assert.False(JumpRules.IsValidJumpLanding(pochven, JumpMethod.Cyno));
        Assert.False(JumpRules.IsValidJumpLanding(pochven, JumpMethod.CovertCyno));
    }

    [Fact]
    public void JumpPathfinder_DoesNotRouteThroughHighSec_WithCyno()
    {
        double ly = SpaceMath.LightYearsToMeters(1);
        var systems = new[]
        {
            new SolarSystem(1, "NullA", 1, 1, 0.0, 0, 0, 0),
            new SolarSystem(2, "HighB", 1, 1, 0.9, ly * 3, 0, 0),
            new SolarSystem(3, "NullC", 1, 1, 0.0, ly * 5, 0, 0),
        };
        var map = new UniverseMap(systems, Array.Empty<Stargate>());
        var carrier = ShipHulls.ByClass(CapitalShipClass.Carrier).First();

        // Only geometric path is A->B->C, but B is high-sec and cyno landing there is illegal.
        var route = JumpPathfinder.FindRoute(map, carrier, new PilotSkills(), 1, 3, JumpMethod.Cyno);

        Assert.Null(route);
    }
}
