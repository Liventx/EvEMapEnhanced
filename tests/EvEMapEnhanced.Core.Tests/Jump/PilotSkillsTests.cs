using EvEMapEnhanced.Core.Jump;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Jump;

public class PilotSkillsTests
{
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(10, 5)]
    public void SkillLevels_AreClampedTo0Through5(int input, int expected)
    {
        var skills = new PilotSkills { JumpDriveCalibration = input };
        Assert.Equal(expected, skills.JumpDriveCalibration);
    }

    [Fact]
    public void MaxSkills_ReturnsAllFives()
    {
        var skills = PilotSkills.MaxSkills();
        Assert.Equal(5, skills.JumpDriveCalibration);
        Assert.Equal(5, skills.JumpFuelConservation);
        Assert.Equal(5, skills.JumpFreighters);
        Assert.Equal(5, skills.CapitalShips);
        Assert.Equal(5, skills.BlackOps);
    }
}
