using EvEMapEnhanced.Data.Sde;

namespace EvEMapEnhanced.Data.Tests.Sde;

public class SdeImporterTests : IDisposable
{
    private readonly string _zipPath = Path.Combine(Path.GetTempPath(), $"mini-sde-{Guid.NewGuid():N}.zip");
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"mini-sde-{Guid.NewGuid():N}.sqlite");

    public SdeImporterTests()
    {
        MiniSdeFixture.CreateZip(_zipPath);
    }

    [Fact]
    public void ImportFromZip_ImportsMapDataAndCounts()
    {
        var importer = new SdeImporter();
        var summary = importer.ImportFromZip(_zipPath, _sqlitePath);

        Assert.Equal(1, summary.Regions);
        Assert.Equal(1, summary.Constellations);
        Assert.Equal(3, summary.SolarSystems);
        Assert.Equal(2, summary.Stargates); // 4 directed entries dedupe into 2 undirected pairs
    }

    [Fact]
    public void ImportFromZip_ResolvesRequestedShipTypes()
    {
        var importer = new SdeImporter();
        var names = new HashSet<string> { "Archon", "Capsule", "NonExistentShip" };
        var summary = importer.ImportFromZip(_zipPath, _sqlitePath, names);

        Assert.Equal(2, summary.ShipTypesResolved); // NonExistentShip never matches
    }

    [Fact]
    public void ImportFromZip_IsIdempotent_WhenRunTwice()
    {
        var importer = new SdeImporter();
        importer.ImportFromZip(_zipPath, _sqlitePath);
        var second = importer.ImportFromZip(_zipPath, _sqlitePath);

        Assert.Equal(3, second.SolarSystems);
    }

    public void Dispose()
    {
        TryDelete(_zipPath);
        TryDelete(_sqlitePath);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort cleanup */ }
    }
}
