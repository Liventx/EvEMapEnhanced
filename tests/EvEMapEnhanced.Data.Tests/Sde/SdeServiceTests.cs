using EvEMapEnhanced.Data.Sde;

namespace EvEMapEnhanced.Data.Tests.Sde;

public class SdeServiceTests : IDisposable
{
    private readonly string _zipPath = Path.Combine(Path.GetTempPath(), $"mini-sde-{Guid.NewGuid():N}.zip");
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"mini-sde-{Guid.NewGuid():N}.sqlite");

    public void Dispose()
    {
        TryDelete(_zipPath);
        TryDelete(_sqlitePath);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task EnsureCachedAsync_ReturnsImmediately_WhenSqliteCacheExists()
    {
        MiniSdeFixture.CreateZip(_zipPath);
        new SdeImporter().ImportFromZip(_zipPath, _sqlitePath, ShipTypeCatalog.NamesToResolve());

        var service = new SdeService(_zipPath, _sqlitePath);
        var (alreadyCached, summary) = await service.EnsureCachedAsync();

        Assert.True(alreadyCached);
        Assert.Null(summary);
        Assert.True(service.IsCached());
    }

    [Fact]
    public async Task EnsureCachedAsync_ImportsFromExistingZip_WithoutDownload()
    {
        MiniSdeFixture.CreateZip(_zipPath);
        var service = new SdeService(_zipPath, _sqlitePath);

        var (alreadyCached, summary) = await service.EnsureCachedAsync();

        Assert.False(alreadyCached);
        Assert.NotNull(summary);
        Assert.Equal(3, summary!.SolarSystems);
        Assert.True(service.IsCached());
    }
}
