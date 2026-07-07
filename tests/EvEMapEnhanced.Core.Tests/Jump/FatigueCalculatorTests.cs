using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Ships;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Jump;

public class FatigueCalculatorTests
{
    [Fact]
    public void FirstJump_UsesMinimumFatigueFormula()
    {
        // cooldown = max(0/10, 1+5) = 6 ; fatigue = max(0,10)*(1+5) = 60
        double cooldown = FatigueCalculator.CooldownMinutes(currentFatigueMinutes: 0, effectiveLightYears: 5);
        double fatigue = FatigueCalculator.NextFatigueMinutes(currentFatigueMinutes: 0, effectiveLightYears: 5);

        Assert.Equal(6.0, cooldown, precision: 6);
        Assert.Equal(60.0, fatigue, precision: 6);
    }

    [Fact]
    public void SecondJump_CompoundsExistingFatigue()
    {
        // starting fatigue 60, another 5 LY jump:
        // cooldown = max(60/10, 1+5) = max(6,6) = 6
        // fatigue = max(60,10) * (1+5) = 360 -> capped at 300
        double cooldown = FatigueCalculator.CooldownMinutes(60, 5);
        double fatigue = FatigueCalculator.NextFatigueMinutes(60, 5);

        Assert.Equal(6.0, cooldown, precision: 6);
        Assert.Equal(300.0, fatigue, precision: 6);
    }

    [Fact]
    public void Fatigue_IsCappedAtFiveHours()
    {
        double fatigue = FatigueCalculator.NextFatigueMinutes(currentFatigueMinutes: 300, effectiveLightYears: 10);
        Assert.Equal(FatigueCalculator.MaxFatigueMinutes, fatigue);
    }

    [Theory]
    [InlineData(CapitalShipClass.Carrier, JumpMethod.Cyno, 10, 10.0)]
    [InlineData(CapitalShipClass.JumpFreighter, JumpMethod.Cyno, 10, 1.0)]
    [InlineData(CapitalShipClass.BlackOps, JumpMethod.Cyno, 10, 5.0)]
    [InlineData(CapitalShipClass.BlackOps, JumpMethod.CovertCyno, 10, 5.0)]
    [InlineData(CapitalShipClass.Rorqual, JumpMethod.JumpBridge, 10, 1.0)]
    public void EffectiveLightYears_AppliesPerClassFatigueBonus(CapitalShipClass shipClass, JumpMethod method, double distanceLy, double expectedEffective)
    {
        double effective = FatigueCalculator.EffectiveLightYears(shipClass, method, distanceLy);
        Assert.Equal(expectedEffective, effective, precision: 6);
    }
}
