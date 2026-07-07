using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Ships;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Jump;

public class FuelCalculatorTests
{
    private static readonly ShipHull TestCarrier = new("TestCarrier", Faction.None, CapitalShipClass.Carrier, MassKg: 1, BaseFuelPerLyIsotopes: 1000);
    private static readonly ShipHull TestJumpFreighter = new("TestJF", Faction.None, CapitalShipClass.JumpFreighter, MassKg: 1, BaseFuelPerLyIsotopes: 1000);

    [Fact]
    public void NoSkills_UsesFullBaseFuel()
    {
        var skills = new PilotSkills();
        double fuel = FuelCalculator.IsotopesForJump(TestCarrier, skills, distanceLy: 5);
        Assert.Equal(5000.0, fuel, precision: 6);
    }

    [Fact]
    public void JumpFuelConservationV_HalvesFuel()
    {
        var skills = new PilotSkills { JumpFuelConservation = 5 };
        double fuel = FuelCalculator.IsotopesForJump(TestCarrier, skills, distanceLy: 5);
        Assert.Equal(2500.0, fuel, precision: 6);
    }

    [Fact]
    public void JumpFreightersSkill_OnlyAppliesToJumpFreighterHulls()
    {
        var skills = new PilotSkills { JumpFreighters = 5 };

        double carrierFuel = FuelCalculator.IsotopesForJump(TestCarrier, skills, distanceLy: 5);
        double jfFuel = FuelCalculator.IsotopesForJump(TestJumpFreighter, skills, distanceLy: 5);

        Assert.Equal(5000.0, carrierFuel, precision: 6); // unaffected: not a Jump Freighter
        Assert.Equal(2500.0, jfFuel, precision: 6);       // -50% from JF skill alone
    }

    [Fact]
    public void EconomizerAndSkillsStackMultiplicatively()
    {
        var skills = new PilotSkills { JumpFuelConservation = 5, Economizer = JumpDriveEconomizerTier.T3 };
        // 1000 * 5 LY * (1 - 0.5) * (1 - 0.10) = 2250
        double fuel = FuelCalculator.IsotopesForJump(TestCarrier, skills, distanceLy: 5);
        Assert.Equal(2250.0, fuel, precision: 6);
    }
}
