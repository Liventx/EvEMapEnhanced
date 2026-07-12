using EvEMapEnhanced.Core.Stats;
using EvEMapEnhanced.Data.User;

namespace EvEMapEnhanced.Data.Tests.User;

public class ManualWormholeRepositoryTests : IDisposable
{
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"manual-wh-{Guid.NewGuid():N}.sqlite");

    [Fact]
    public void UpsertAndLoad_RoundTripsMarker()
    {
        var repo = new ManualWormholeRepository(_sqlitePath);
        var marker = repo.Upsert(30000142, 30002187, "Jita");

        var loaded = repo.LoadActive().Single();
        Assert.Equal(30000142, loaded.SolarSystemId);
        Assert.Equal(30002187, loaded.ExitSystemId);
        Assert.Equal("Jita", loaded.ExitComment);
        Assert.Equal(marker.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(marker.ExpiresAtUtc, loaded.ExpiresAtUtc);
        Assert.Equal(marker.CreatedAtUtc.AddHours(ManualWormholeRepository.LifetimeHours), loaded.ExpiresAtUtc);
    }

    [Fact]
    public void Upsert_ReplacesExistingMarkerForSystem()
    {
        var repo = new ManualWormholeRepository(_sqlitePath);
        repo.Upsert(30000142, 30002187, "Old exit");
        repo.Upsert(30000142, 30002795, "New exit");

        var loaded = repo.LoadActive().Single();
        Assert.Equal(30002795, loaded.ExitSystemId);
        Assert.Equal("New exit", loaded.ExitComment);
    }

    [Fact]
    public void Delete_RemovesMarker()
    {
        var repo = new ManualWormholeRepository(_sqlitePath);
        repo.Upsert(30000142, 30002187, "Jita");

        repo.Delete(30000142);

        Assert.Empty(repo.LoadActive());
    }

    [Fact]
    public void LoadActive_ExcludesExpiredMarkers()
    {
        var repo = new ManualWormholeRepository(_sqlitePath);
        repo.Upsert(30000142, 30002187, "Jita");

        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE ManualWormholes
            SET ExpiresAtUtc = $expires
            WHERE SolarSystemId = $sys;
            """;
        cmd.Parameters.AddWithValue("$sys", 30000142);
        cmd.Parameters.AddWithValue("$expires", DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        cmd.ExecuteNonQuery();

        Assert.Empty(repo.LoadActive());
    }

    [Fact]
    public void UpsertWithPair_CreatesReverseMarkerOnExitSystem()
    {
        var repo = new ManualWormholeRepository(_sqlitePath);
        repo.UpsertWithPair(30000142, 30002187);

        var loaded = repo.LoadActive().ToDictionary(marker => marker.SolarSystemId);
        Assert.Equal(30002187, loaded[30000142].ExitSystemId);
        Assert.Equal(30000142, loaded[30002187].ExitSystemId);
    }

    [Fact]
    public void DeleteWithPair_RemovesBothMarkers()
    {
        var repo = new ManualWormholeRepository(_sqlitePath);
        repo.UpsertWithPair(30000142, 30002187);

        repo.DeleteWithPair(30000142);

        Assert.Empty(repo.LoadActive());
    }

    [Fact]
    public void DeleteWithPair_FromExitSide_RemovesBothMarkers()
    {
        var repo = new ManualWormholeRepository(_sqlitePath);
        repo.UpsertWithPair(30000142, 30002187);

        repo.DeleteWithPair(30002187);

        Assert.Empty(repo.LoadActive());
    }

    [Fact]
    public void UpsertWithPair_ChangingExit_ReplacesPreviousPair()
    {
        var repo = new ManualWormholeRepository(_sqlitePath);
        repo.UpsertWithPair(30000142, 30002187);
        repo.UpsertWithPair(30000142, 30002795);

        var loaded = repo.LoadActive().ToDictionary(marker => marker.SolarSystemId);
        Assert.Equal(2, loaded.Count);
        Assert.Equal(30002795, loaded[30000142].ExitSystemId);
        Assert.Equal(30000142, loaded[30002795].ExitSystemId);
        Assert.False(loaded.ContainsKey(30002187));
    }

    [Fact]
    public void UpsertWithPair_ClearingExit_RemovesPairMarker()
    {
        var repo = new ManualWormholeRepository(_sqlitePath);
        repo.UpsertWithPair(30000142, 30002187);
        repo.UpsertWithPair(30000142, null);

        var loaded = repo.LoadActive().ToDictionary(marker => marker.SolarSystemId);
        Assert.Single(loaded);
        Assert.Equal(30000142, loaded.Keys.Single());
        Assert.Null(loaded[30000142].ExitSystemId);
    }

    public void Dispose()
    {
        try { if (File.Exists(_sqlitePath)) File.Delete(_sqlitePath); } catch { }
    }
}
