using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Data.Paths;
using EvEMapEnhanced.Data.Sde;
using EvEMapEnhanced.Data.Stats;
using EvEMapEnhanced.Data.User;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// Simple composition root: wires up the Data-layer services once at startup and exposes
/// them to the views. Deliberately not a DI container -- this is a small desktop app
/// with a handful of long-lived singletons.
/// </summary>
public sealed class AppServices
{
    public SdeService SdeService { get; }
    public PilotProfileRepository PilotProfiles { get; }
    public UserStructureRepository UserStructures { get; }
    public SavedRouteRepository SavedRoutes { get; }
    public SystemNoteRepository SystemNotes { get; }
    public SystemStatsCacheRepository StatsCache { get; }

    public UniverseMap? Map { get; private set; }
    public ShipTypeCatalog? ShipCatalog { get; private set; }

    public AppServices()
    {
        SdeService = new SdeService();
        PilotProfiles = new PilotProfileRepository(AppPaths.UserDbPath);
        UserStructures = new UserStructureRepository(AppPaths.UserDbPath);
        SavedRoutes = new SavedRouteRepository(AppPaths.UserDbPath);
        SystemNotes = new SystemNoteRepository(AppPaths.UserDbPath);
        StatsCache = new SystemStatsCacheRepository(AppPaths.StatsCachePath);
    }

    public bool IsMapLoaded => Map is not null;

    public void ReloadMapFromCache()
    {
        if (!SdeService.IsCached())
        {
            Map = null;
            ShipCatalog = null;
            return;
        }

        var repo = SdeService.GetRepository();
        Map = repo.BuildUniverseMap();
        ShipCatalog = ShipTypeCatalog.Build(repo);
        Map.LoadStructures(UserStructures.LoadAll());
    }

    public void ReloadStructuresOnly()
    {
        Map?.LoadStructures(UserStructures.LoadAll());
    }
}
