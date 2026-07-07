using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Ships;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Routing;

public class RouteSimulatorTests
{
    [Fact]
    public void SimulateJumpRoute_AccumulatesFatigueAndFuelAcrossLegs()
    {
        var hull = new ShipHull("TestCarrier", Faction.None, CapitalShipClass.Carrier, MassKg: 1, BaseFuelPerLyIsotopes: 1000);
        var skills = new PilotSkills { JumpDriveCalibration = 5 }; // 7.0 LY max range, comfortably covers 5 LY legs
        var route = new JumpRoute(new[]
        {
            new JumpRouteLeg(1, 2, 5.0, JumpMethod.Cyno),
            new JumpRouteLeg(2, 3, 5.0, JumpMethod.Cyno),
        });

        var result = RouteSimulator.SimulateJumpRoute(route, hull, skills);

        Assert.Equal(2, result.Legs.Count);
        Assert.Equal(10000.0, result.TotalFuel, precision: 6); // 2 * 5 LY * 1000/LY
        Assert.True(result.PeakFatigueMinutes > 0);
        Assert.False(result.AnyLegOutOfRange);
    }

    [Fact]
    public void SimulateJumpRoute_FlagsOutOfRangeLegs()
    {
        var hull = new ShipHull("TestCarrier", Faction.None, CapitalShipClass.Carrier, MassKg: 1, BaseFuelPerLyIsotopes: 1000);
        var skills = new PilotSkills(); // JDC 0 -> 3.5 LY max range
        var route = new JumpRoute(new[] { new JumpRouteLeg(1, 2, 5.0, JumpMethod.Cyno) });

        var result = RouteSimulator.SimulateJumpRoute(route, hull, skills);

        Assert.True(result.AnyLegOutOfRange);
    }

    [Fact]
    public void EmptyRoute_ProducesZeroedResult()
    {
        var hull = new ShipHull("TestCarrier", Faction.None, CapitalShipClass.Carrier, MassKg: 1, BaseFuelPerLyIsotopes: 1000);
        var result = RouteSimulator.SimulateJumpRoute(new JumpRoute(Array.Empty<JumpRouteLeg>()), hull, new PilotSkills());

        Assert.Equal(0, result.TotalFuel);
        Assert.Equal(0, result.PeakFatigueMinutes);
        Assert.False(result.AnyLegOutOfRange);
    }
}
