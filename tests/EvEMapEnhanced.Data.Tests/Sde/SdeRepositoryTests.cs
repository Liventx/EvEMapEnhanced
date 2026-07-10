using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Data.Sde;

namespace EvEMapEnhanced.Data.Tests.Sde;

public class SdeRepositoryTests : IDisposable
{
    private readonly string _zipPath = Path.Combine(Path.GetTempPath(), $"mini-sde-{Guid.NewGuid():N}.zip");
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"mini-sde-{Guid.NewGuid():N}.sqlite");

    public SdeRepositoryTests()
    {
        MiniSdeFixture.CreateZip(_zipPath);
        new SdeImporter().ImportFromZip(_zipPath, _sqlitePath, ShipTypeCatalog.NamesToResolve());
    }

    [Fact]
    public void HasData_ReturnsTrueAfterImport()
    {
        var repo = new SdeRepository(_sqlitePath);
        Assert.True(repo.HasData());
    }

    [Fact]
    public void BuildUniverseMap_ProducesRoutableGraph()
    {
        var repo = new SdeRepository(_sqlitePath);
        // The mini fixture only has 3 systems, well below the real-world "accessible cluster"
        // size threshold used to drop disconnected test/dev regions -- disable it here.
        var map = repo.BuildUniverseMap(minAccessibleClusterSize: 1);

        var route = GatePathfinder.FindRoute(map, MiniSdeFixture.SystemAId, MiniSdeFixture.SystemCId);

        Assert.NotNull(route);
        Assert.Equal(2, route!.JumpCount);
        Assert.Equal("Charlie", map.Get(MiniSdeFixture.SystemCId)!.Name);
    }

    [Fact]
    public void ShipTypeCatalog_ResolvesArchonAndCapsule()
    {
        var repo = new SdeRepository(_sqlitePath);
        var catalog = ShipTypeCatalog.Build(repo);

        Assert.True(catalog.IsCapitalTypeId(MiniSdeFixture.ArchonTypeId));
        Assert.True(catalog.IsPodTypeId(MiniSdeFixture.CapsuleTypeId));
        Assert.False(catalog.IsCapitalTypeId(587)); // Rifter is not a seeded capital hull
    }

    [Fact]
    public void ShipTypeCatalog_TryGetCapitalShipClass_MapsKnownHullsAndRejectsPods()
    {
        var repo = new SdeRepository(_sqlitePath);
        var catalog = ShipTypeCatalog.Build(repo);

        Assert.True(catalog.TryGetCapitalShipClass(MiniSdeFixture.ArchonTypeId, out var archonClass));
        Assert.Equal(EvEMapEnhanced.Core.Ships.CapitalShipClass.Carrier, archonClass);

        Assert.False(catalog.TryGetCapitalShipClass(MiniSdeFixture.CapsuleTypeId, out _));
        Assert.False(catalog.TryGetCapitalShipClass(587, out _)); // Rifter
    }

    public void Dispose()
    {
        try { if (File.Exists(_zipPath)) File.Delete(_zipPath); } catch { }
        try { if (File.Exists(_sqlitePath)) File.Delete(_sqlitePath); } catch { }
    }
}
