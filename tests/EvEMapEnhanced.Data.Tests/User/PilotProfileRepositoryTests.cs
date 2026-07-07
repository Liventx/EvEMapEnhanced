using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Data.User;

namespace EvEMapEnhanced.Data.Tests.User;

public class PilotProfileRepositoryTests : IDisposable
{
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"user-{Guid.NewGuid():N}.sqlite");

    [Fact]
    public void SaveAndLoad_RoundTripsSkillsAndAvoidList()
    {
        var repo = new PilotProfileRepository(_sqlitePath);
        var profile = new PilotProfile
        {
            Name = "Основной",
            Skills = new PilotSkills { JumpDriveCalibration = 4, JumpFuelConservation = 3, Economizer = JumpDriveEconomizerTier.T2 },
            AvoidLowSec = true,
            AvoidSystemIds = { 30000142, 30002187 },
        };

        int id = repo.Save(profile);
        Assert.True(id > 0);

        var loaded = repo.LoadAll().Single(p => p.Id == id);
        Assert.Equal("Основной", loaded.Name);
        Assert.Equal(4, loaded.Skills.JumpDriveCalibration);
        Assert.Equal(3, loaded.Skills.JumpFuelConservation);
        Assert.Equal(JumpDriveEconomizerTier.T2, loaded.Skills.Economizer);
        Assert.True(loaded.AvoidLowSec);
        Assert.Contains(30000142, loaded.AvoidSystemIds);
        Assert.Contains(30002187, loaded.AvoidSystemIds);
    }

    [Fact]
    public void Save_UpdatesExistingProfile_WhenIdIsSet()
    {
        var repo = new PilotProfileRepository(_sqlitePath);
        var profile = new PilotProfile { Name = "V1" };
        int id = repo.Save(profile);

        profile.Name = "V2";
        profile.Skills.JumpDriveCalibration = 5;
        repo.Save(profile);

        var all = repo.LoadAll();
        Assert.Single(all);
        Assert.Equal("V2", all[0].Name);
        Assert.Equal(5, all[0].Skills.JumpDriveCalibration);
    }

    [Fact]
    public void Delete_RemovesProfile()
    {
        var repo = new PilotProfileRepository(_sqlitePath);
        int id = repo.Save(new PilotProfile { Name = "ToDelete" });

        repo.Delete(id);

        Assert.Empty(repo.LoadAll());
    }

    public void Dispose()
    {
        try { if (File.Exists(_sqlitePath)) File.Delete(_sqlitePath); } catch { }
    }
}
