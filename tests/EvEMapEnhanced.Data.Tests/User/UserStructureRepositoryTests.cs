using EvEMapEnhanced.Core.Structures;
using EvEMapEnhanced.Data.User;

namespace EvEMapEnhanced.Data.Tests.User;

public class UserStructureRepositoryTests : IDisposable
{
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"user-{Guid.NewGuid():N}.sqlite");

    [Fact]
    public void SaveAndLoad_RoundTripsAnsiblexLink()
    {
        var repo = new UserStructureRepository(_sqlitePath);
        var structure = new UserStructure
        {
            SolarSystemId = 30000142,
            Kind = StructureKind.Ansiblex,
            Name = "Jita - Perimeter Bridge",
            OwnerTag = "[TEST]",
            Access = StructureAccessLevel.OwnAlliance,
            LinkedSystemId = 30000144,
            StrontHours = 48.5,
        };

        int id = repo.Save(structure);
        var loaded = repo.LoadAll().Single(s => s.Id == id);

        Assert.Equal(StructureKind.Ansiblex, loaded.Kind);
        Assert.Equal("Jita - Perimeter Bridge", loaded.Name);
        Assert.Equal(30000144, loaded.LinkedSystemId);
        Assert.Equal(48.5, loaded.StrontHours);
    }

    [Fact]
    public void Delete_RemovesStructure()
    {
        var repo = new UserStructureRepository(_sqlitePath);
        int id = repo.Save(new UserStructure { SolarSystemId = 1, Kind = StructureKind.CynoJammer, Name = "J" });

        repo.Delete(id);

        Assert.Empty(repo.LoadAll());
    }

    public void Dispose()
    {
        try { if (File.Exists(_sqlitePath)) File.Delete(_sqlitePath); } catch { }
    }
}
