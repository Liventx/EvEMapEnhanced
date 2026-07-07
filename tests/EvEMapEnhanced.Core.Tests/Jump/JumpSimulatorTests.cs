using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Ships;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Jump;

public class JumpSimulatorTests
{
    private static readonly ShipHull TestCarrier = new("TestCarrier", Faction.None, CapitalShipClass.Carrier, MassKg: 1, BaseFuelPerLyIsotopes: 1000);

    [Theory]
    [InlineData(0, 3.5)]
    [InlineData(1, 4.2)]
    [InlineData(5, 7.0)]
    public void MaxRangeLy_ScalesWithJumpDriveCalibration(int jdcLevel, double expectedRangeLy)
    {
        var skills = new PilotSkills { JumpDriveCalibration = jdcLevel };
        double range = JumpSimulator.MaxRangeLy(TestCarrier, skills);
        Assert.Equal(expectedRangeLy, range, precision: 6);
    }

    [Fact]
    public void SimulateJump_WithinRange_IsFlaggedTrue()
    {
        var skills = new PilotSkills { JumpDriveCalibration = 5 }; // 7.0 LY max
        var state = JumpState.Fresh();
        var result = JumpSimulator.SimulateJump(TestCarrier, skills, JumpMethod.Cyno, distanceLy: 6.9, state);
        Assert.True(result.WithinRange);
    }

    [Fact]
    public void SimulateJump_BeyondRange_IsFlaggedFalse_ButStillComputed()
    {
        var skills = new PilotSkills { JumpDriveCalibration = 0 }; // 3.5 LY max
        var state = JumpState.Fresh();
        var result = JumpSimulator.SimulateJump(TestCarrier, skills, JumpMethod.Cyno, distanceLy: 5.0, state);
        Assert.False(result.WithinRange);
        Assert.True(result.IsotopesUsed > 0);
    }

    [Theory]
    [InlineData(0, CapitalShipClass.BlackOps, 4.0)]
    [InlineData(5, CapitalShipClass.BlackOps, 8.0)]
    [InlineData(0, CapitalShipClass.JumpFreighter, 5.0)]
    public void MaxRangeLy_ByShipClass_MatchesClassMechanicsRegardlessOfHull(int jdcLevel, CapitalShipClass shipClass, double expectedRangeLy)
    {
        var skills = new PilotSkills { JumpDriveCalibration = jdcLevel };
        double range = JumpSimulator.MaxRangeLy(shipClass, skills);
        Assert.Equal(expectedRangeLy, range, precision: 6);
    }

    [Fact]
    public void SimulateJump_MutatesSharedState_ForChainedJumps()
    {
        var skills = new PilotSkills { JumpDriveCalibration = 5 };
        var state = JumpState.Fresh();

        var first = JumpSimulator.SimulateJump(TestCarrier, skills, JumpMethod.Cyno, 5.0, state);
        var second = JumpSimulator.SimulateJump(TestCarrier, skills, JumpMethod.Cyno, 5.0, state);

        Assert.Equal(0.0, first.FatigueBeforeMinutes);
        Assert.Equal(first.FatigueAfterMinutes, second.FatigueBeforeMinutes);
        Assert.True(second.FatigueAfterMinutes >= first.FatigueAfterMinutes);
    }
}
