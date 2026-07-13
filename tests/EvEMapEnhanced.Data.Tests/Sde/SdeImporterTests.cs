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

    [Fact]
    public void ImportFromZip_ImportsExcludedKillVictimTypes()
    {
        var importer = new SdeImporter();
        var summary = importer.ImportFromZip(_zipPath, _sqlitePath, ShipTypeCatalog.NamesToResolve());

        Assert.Equal(3, summary.ExcludedKillVictimTypes);

        var repo = new SdeRepository(_sqlitePath);
        var excluded = repo.LoadExcludedKillVictimTypeIds();
        Assert.Contains(MiniSdeFixture.CapsuleTypeId, excluded);
        Assert.Contains(MiniSdeFixture.ShuttleTypeId, excluded);
        Assert.Contains(MiniSdeFixture.CorvetteTypeId, excluded);
    }

    [Fact]
    public void ImportFromZip_ImportsNpcStationSystems_Deduplicated()
    {
        var importer = new SdeImporter();
        var summary = importer.ImportFromZip(_zipPath, _sqlitePath);

        Assert.Equal(2, summary.NpcStationSystems); // Alpha (two stations) dedupes to one, plus Bravo

        var repo = new SdeRepository(_sqlitePath);
        var stationSystems = repo.LoadNpcStationSystemIds();
        Assert.Contains(MiniSdeFixture.SystemAId, stationSystems);
        Assert.Contains(MiniSdeFixture.SystemBId, stationSystems);
        Assert.DoesNotContain(MiniSdeFixture.SystemCId, stationSystems);
    }

    [Fact]
    public void ImportFromZip_RecordsNpcStationSystemsWithoutCloneFacility()
    {
        var importer = new SdeImporter();
        importer.ImportFromZip(_zipPath, _sqlitePath);

        var repo = new SdeRepository(_sqlitePath);
        var noCloneSystems = repo.LoadNpcStationNoCloneSystemIds();
        Assert.DoesNotContain(MiniSdeFixture.SystemAId, noCloneSystems); // Alpha has a cloning station
        Assert.Contains(MiniSdeFixture.SystemBId, noCloneSystems);
        Assert.DoesNotContain(MiniSdeFixture.SystemCId, noCloneSystems);
        Assert.False(repo.NeedsNpcStationCloneBackfill());
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
