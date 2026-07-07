using EvEMapEnhanced.Data.Auth;

namespace EvEMapEnhanced.Data.Tests.Auth;

public class EsiCharacterSkillsClientTests
{
    [Fact]
    public void MapToPilotSkills_MapsKnownTypeIdsToTheirFields()
    {
        var levels = new Dictionary<int, int>
        {
            [21611] = 5, // Jump Drive Calibration
            [21610] = 4, // Jump Fuel Conservation
            [29029] = 3, // Jump Freighters
            [20533] = 5, // Capital Ships
            [28656] = 2, // Black Ops
        };

        var skills = EsiCharacterSkillsClient.MapToPilotSkills(levels);

        Assert.Equal(5, skills.JumpDriveCalibration);
        Assert.Equal(4, skills.JumpFuelConservation);
        Assert.Equal(3, skills.JumpFreighters);
        Assert.Equal(5, skills.CapitalShips);
        Assert.Equal(2, skills.BlackOps);
    }

    [Fact]
    public void MapToPilotSkills_MissingSkillsDefaultToZero()
    {
        var skills = EsiCharacterSkillsClient.MapToPilotSkills(new Dictionary<int, int>());

        Assert.Equal(0, skills.JumpDriveCalibration);
        Assert.Equal(0, skills.JumpFuelConservation);
        Assert.Equal(0, skills.JumpFreighters);
        Assert.Equal(0, skills.CapitalShips);
        Assert.Equal(0, skills.BlackOps);
    }

    [Fact]
    public void MapToPilotSkills_IgnoresUnrelatedSkillTypeIds()
    {
        var levels = new Dictionary<int, int> { [3327] = 5 }; // e.g. Gallente Frigate, irrelevant to jump mechanics

        var skills = EsiCharacterSkillsClient.MapToPilotSkills(levels);

        Assert.Equal(0, skills.JumpDriveCalibration);
        Assert.Equal(0, skills.CapitalShips);
    }
}
