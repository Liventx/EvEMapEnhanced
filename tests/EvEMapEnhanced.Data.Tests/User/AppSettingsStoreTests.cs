using EvEMapEnhanced.Core.Stats;
using EvEMapEnhanced.Data.User;
using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.Tests.User;

public class AppSettingsStoreTests : IDisposable
{
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"evemap-settings-{Guid.NewGuid():N}.sqlite");

    public AppSettingsStoreTests()
    {
        using (UserDatabase.OpenConnection(_sqlitePath)) { }
    }

    [Fact]
    public void ZKillboardRequestMode_RoundTripsThroughSqlite()
    {
        var store = new AppSettingsStore(_sqlitePath);

        Assert.Equal(ZKillboardRequestMode.Polite, store.GetZKillboardRequestMode());

        store.SetZKillboardRequestMode(ZKillboardRequestMode.Faster);
        Assert.Equal(ZKillboardRequestMode.Faster, store.GetZKillboardRequestMode());

        var reloaded = new AppSettingsStore(_sqlitePath);
        Assert.Equal(ZKillboardRequestMode.Faster, reloaded.GetZKillboardRequestMode());
    }

    [Fact]
    public void ZKillboardScope_RoundTripsThroughSqlite()
    {
        var store = new AppSettingsStore(_sqlitePath);

        Assert.Equal(ZKillboardScope.JumpRange, store.GetZKillboardScope());

        store.SetZKillboardScope(ZKillboardScope.GlobalNullsec);
        Assert.Equal(ZKillboardScope.GlobalNullsec, store.GetZKillboardScope());

        var reloaded = new AppSettingsStore(_sqlitePath);
        Assert.Equal(ZKillboardScope.GlobalNullsec, reloaded.GetZKillboardScope());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_sqlitePath))
            File.Delete(_sqlitePath);
    }
}
