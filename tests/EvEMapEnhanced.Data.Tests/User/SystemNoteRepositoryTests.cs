using EvEMapEnhanced.Core.Notes;
using EvEMapEnhanced.Data.User;

namespace EvEMapEnhanced.Data.Tests.User;

public class SystemNoteRepositoryTests : IDisposable
{
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"user-{Guid.NewGuid():N}.sqlite");

    [Fact]
    public void SaveAndGet_RoundTripsNoteAndTags()
    {
        var repo = new SystemNoteRepository(_sqlitePath);
        repo.Save(new SystemNote { SolarSystemId = 30000142, Text = "Опасно, часто ганкают", Tags = { "ганк", "трейд-хаб" } });

        var loaded = repo.Get(30000142);
        Assert.NotNull(loaded);
        Assert.Equal("Опасно, часто ганкают", loaded!.Text);
        Assert.Contains("ганк", loaded.Tags);
    }

    [Fact]
    public void Save_UpsertsExistingNote()
    {
        var repo = new SystemNoteRepository(_sqlitePath);
        repo.Save(new SystemNote { SolarSystemId = 1, Text = "V1" });
        repo.Save(new SystemNote { SolarSystemId = 1, Text = "V2" });

        Assert.Single(repo.LoadAll());
        Assert.Equal("V2", repo.Get(1)!.Text);
    }

    [Fact]
    public void Get_ReturnsNull_WhenMissing()
    {
        var repo = new SystemNoteRepository(_sqlitePath);
        Assert.Null(repo.Get(999));
    }

    public void Dispose()
    {
        try { if (File.Exists(_sqlitePath)) File.Delete(_sqlitePath); } catch { }
    }
}
