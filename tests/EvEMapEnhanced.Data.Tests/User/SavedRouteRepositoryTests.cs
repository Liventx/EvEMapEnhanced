using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Data.User;

namespace EvEMapEnhanced.Data.Tests.User;

public class SavedRouteRepositoryTests : IDisposable
{
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"user-{Guid.NewGuid():N}.sqlite");

    [Fact]
    public void SaveAndLoad_RoundTripsSteps()
    {
        var repo = new SavedRouteRepository(_sqlitePath);
        var route = new SavedRoute
        {
            Name = "Домой",
            Steps =
            {
                new RouteStep(1, 2, RouteStepKind.Gate),
                new RouteStep(2, 3, RouteStepKind.Jump, DistanceLy: 4.2, Method: EvEMapEnhanced.Core.Jump.JumpMethod.Cyno),
            },
        };

        int id = repo.Save(route);
        var loaded = repo.LoadAll().Single(r => r.Id == id);

        Assert.Equal("Домой", loaded.Name);
        Assert.Equal(2, loaded.Steps.Count);
        Assert.Equal(RouteStepKind.Jump, loaded.Steps[1].Kind);
        Assert.Equal(4.2, loaded.Steps[1].DistanceLy);
    }

    public void Dispose()
    {
        try { if (File.Exists(_sqlitePath)) File.Delete(_sqlitePath); } catch { }
    }
}
