using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EvEMapEnhanced.Core.Auth;
using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Ships;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Stats;
using EvEMapEnhanced.Data.Auth;
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
    public AuthenticatedCharacterRepository Characters { get; }
    public UserStructureRepository UserStructures { get; }
    public ManualWormholeRepository ManualWormholes { get; }
    public SavedRouteRepository SavedRoutes { get; }
    public SystemNoteRepository SystemNotes { get; }
    private readonly EsiSystemKillsClient _systemKillsClient = new();
    private readonly EsiIncursionsClient _incursionsClient = new();
    private readonly EsiSovereigntyClient _sovereigntyClient = new();
    private readonly EveScoutWormholesClient _eveScoutClient = new();

    private readonly HttpClient _httpClient = new();
    private readonly EsiCharacterSkillsClient _skillsClient;
    private readonly EsiCharacterLocationClient _locationClient;
    private readonly EsiCharacterShipClient _shipClient;
    private readonly EsiUniverseTypeClient _universeTypeClient;
    private EsiOAuthClient? _oauthClient;
    private EsiAccessTokenProvider? _tokenProvider;

    public UniverseMap? Map { get; private set; }
    public ShipTypeCatalog? ShipCatalog { get; private set; }
    public KillVictimFilter? KillVictimFilter { get; private set; }
    public NpcCapitalKillFilter? NpcCapitalKillFilter { get; private set; }
    public IReadOnlyDictionary<int, string>? RegionNames { get; private set; }

    /// <summary>Solar system id -> NPC kills in the last hour (ESI, refreshed via <see cref="RefreshNpcKillsAsync"/>).</summary>
    public IReadOnlyDictionary<int, int>? NpcKills { get; private set; }

    /// <summary>Solar system ids that contain at least one NPC station (from the SDE).</summary>
    public IReadOnlySet<int> NpcStationSystems { get; private set; } = new HashSet<int>();

    /// <summary>Solar system id -> recent PvP activity level for jump-range overlay highlighting.</summary>
    public IReadOnlyDictionary<int, PvPActivityLevel> JumpRangePvPActivity { get; private set; } =
        new Dictionary<int, PvPActivityLevel>();

    /// <summary>Solar system ids currently infested by Sansha Nation incursions (ESI).</summary>
    public IReadOnlySet<int> SanshaIncursionSystems { get; private set; } = new HashSet<int>();

    /// <summary>Active Thera/Turnur wormhole signatures from the public EvE-Scout API.</summary>
    public IReadOnlyList<WormholeConnection> EveScoutWormholes { get; private set; } = [];

    /// <summary>EvE-Scout wormholes indexed by every system they touch (hub or remote).</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<WormholeConnection>> EveScoutWormholesBySystem { get; private set; } =
        new Dictionary<int, IReadOnlyList<WormholeConnection>>();

    /// <summary>User-placed wormhole markers indexed by solar system id.</summary>
    public IReadOnlyDictionary<int, ManualWormholeMarker> ManualWormholesBySystem { get; private set; } =
        new Dictionary<int, ManualWormholeMarker>();

    /// <summary>Solar system id -> alliance name holding the system's IHUB (ESI sovereignty map).</summary>
    public IReadOnlyDictionary<int, string> IhubAllianceBySystem { get; private set; } =
        new Dictionary<int, string>();

    /// <summary>Progress of the in-flight zKillboard PvP fetch.</summary>
    public (int Completed, int Total, int Hot, int Recent, int NpcCapital, int Failed, int Cached, int RemainingNetworkRequests) JumpRangePvPProgress { get; private set; }

    private readonly ZKillboardSystemKillsClient _zkillboardClient = new(ZKillboardSystemKillsClient.CreateHttpClient());
    private readonly AppSettingsStore _appSettings;

    public ZKillboardRequestMode ZKillboardRequestMode { get; private set; } = ZKillboardRequestMode.Polite;
    public ZKillboardScope ZKillboardScope { get; private set; } = ZKillboardScope.JumpRange;
    public bool ShowEveScoutWormholes { get; private set; } = true;

    public AppServices()
    {
        SdeService = new SdeService();
        Characters = new AuthenticatedCharacterRepository(AppPaths.UserDbPath);
        UserStructures = new UserStructureRepository(AppPaths.UserDbPath);
        ManualWormholes = new ManualWormholeRepository(AppPaths.UserDbPath);
        SavedRoutes = new SavedRouteRepository(AppPaths.UserDbPath);
        SystemNotes = new SystemNoteRepository(AppPaths.UserDbPath);
        _skillsClient = new EsiCharacterSkillsClient(_httpClient);
        _locationClient = new EsiCharacterLocationClient(_httpClient);
        _shipClient = new EsiCharacterShipClient(_httpClient);
        _universeTypeClient = new EsiUniverseTypeClient(_httpClient);
        _appSettings = new AppSettingsStore(AppPaths.UserDbPath);
        ZKillboardRequestMode = _appSettings.GetZKillboardRequestMode();
        ZKillboardScope = _appSettings.GetZKillboardScope();
        ShowEveScoutWormholes = _appSettings.GetShowEveScoutWormholes();
        _zkillboardClient.RequestMode = ZKillboardRequestMode;
        ReloadManualWormholes();
    }

    public void SetZKillboardRequestMode(ZKillboardRequestMode mode)
    {
        ZKillboardRequestMode = mode;
        _zkillboardClient.RequestMode = mode;
        _appSettings.SetZKillboardRequestMode(mode);
    }

    public void SetZKillboardScope(ZKillboardScope scope)
    {
        ZKillboardScope = scope;
        _appSettings.SetZKillboardScope(scope);
    }

    public void SetShowEveScoutWormholes(bool enabled)
    {
        ShowEveScoutWormholes = enabled;
        _appSettings.SetShowEveScoutWormholes(enabled);
    }

    public void ReloadManualWormholes()
    {
        ManualWormholesBySystem = ManualWormholes.LoadActive()
            .ToDictionary(marker => marker.SolarSystemId);
    }

    public ManualWormholeMarker AddOrUpdateManualWormhole(int solarSystemId, string? exitComment)
    {
        var marker = ManualWormholes.Upsert(solarSystemId, exitComment);
        ReloadManualWormholes();
        return marker;
    }

    public void RemoveManualWormhole(int solarSystemId)
    {
        ManualWormholes.Delete(solarSystemId);
        ReloadManualWormholes();
    }

    public int PurgeExpiredManualWormholes()
    {
        int removed = ManualWormholes.PurgeExpired();
        if (removed > 0)
            ReloadManualWormholes();
        return removed;
    }

    public HashSet<int> GetNullsecSystemIds()
    {
        if (Map is null) return new HashSet<int>();
        return Map.Systems.Values.Where(s => s.IsNullSec).Select(s => s.Id).ToHashSet();
    }

    public int CountNullsecRegionsNeedingFetch()
    {
        if (Map is null) return 0;
        return Map.Systems.Values
            .Where(s => s.IsNullSec)
            .Select(s => s.RegionId)
            .Distinct()
            .Count(regionId => !_zkillboardClient.IsRegionCacheFresh(regionId));
    }

    public bool IsRegionCacheFresh(int regionId) => _zkillboardClient.IsRegionCacheFresh(regionId);

    public bool IsMapLoaded => Map is not null;

    public IReadOnlyList<AuthenticatedCharacter> LoadCharacters() => Characters.LoadAll();

    /// <summary>
    /// Runs the full "Sign in with EVE Online" flow (opens the user's default browser to
    /// login.eveonline.com, waits for the local redirect, exchanges the code, fetches skills).
    /// </summary>
    public async Task<AuthenticatedCharacter> SignInWithEveOnlineAsync(
        EsiAuthSettings settings,
        Func<string, Task>? openBrowser = null,
        CancellationToken ct = default)
    {
        EnsureOAuthClient(settings);
        var flow = new EsiSignInFlow(settings, Characters, _httpClient);
        return await flow.SignInAsync(openBrowser ?? OpenInBrowserAsync, ct);
    }

    public async Task<PilotSkills> RefreshCharacterSkillsAsync(long characterId, EsiAuthSettings settings, CancellationToken ct = default)
    {
        EnsureOAuthClient(settings);
        string accessToken = await _tokenProvider!.GetAccessTokenAsync(characterId, ct);
        var skills = await _skillsClient.GetSkillsAsync(characterId, accessToken, ct);
        Characters.UpdateSkills(characterId, skills);
        return skills;
    }

    public async Task<int> RefreshCharacterLocationAsync(long characterId, EsiAuthSettings settings, CancellationToken ct = default)
    {
        EnsureOAuthClient(settings);
        string accessToken = await _tokenProvider!.GetAccessTokenAsync(characterId, ct);
        int systemId = await _locationClient.GetSolarSystemIdAsync(characterId, accessToken, ct);
        Characters.UpdateLocation(characterId, systemId);
        return systemId;
    }

    public async Task<int> RefreshCharacterShipTypeAsync(long characterId, EsiAuthSettings settings, CancellationToken ct = default)
    {
        EnsureOAuthClient(settings);
        string accessToken = await _tokenProvider!.GetAccessTokenAsync(characterId, ct);
        return await _shipClient.GetShipTypeIdAsync(characterId, accessToken, ct);
    }

    public bool CharacterHasShipTypeScope(long characterId) =>
        Characters.HasScope(characterId, EsiAuthSettings.ShipTypeScope);

    /// <summary>
    /// Resolves a jump-capable capital class from an ESI <c>ship_type_id</c>, using the SDE seed
    /// catalog first and falling back to the hull's EVE inventory group via public ESI.
    /// </summary>
    public async Task<CapitalShipClass?> ResolveJumpShipClassAsync(int shipTypeId, CancellationToken ct = default)
    {
        if (ShipCatalog?.TryGetCapitalShipClass(shipTypeId, out CapitalShipClass shipClass) == true)
            return shipClass;

        try
        {
            int groupId = await _universeTypeClient.GetGroupIdAsync(shipTypeId, ct);
            return CapitalShipGroupMapper.TryMapGroupId(groupId, out shipClass) ? shipClass : null;
        }
        catch
        {
            return null;
        }
    }

    public void SignOutCharacter(long characterId) => Characters.Delete(characterId);

    private void EnsureOAuthClient(EsiAuthSettings settings)
    {
        _oauthClient ??= new EsiOAuthClient(settings.ClientId, _httpClient);
        _tokenProvider ??= new EsiAccessTokenProvider(_oauthClient, Characters);
    }

    public static Task OpenInBrowserAsync(string url)
    {
        BrowserLauncher.OpenOrThrow(url);
        return Task.CompletedTask;
    }

    public static void OpenZKillboardSystemPage(int systemId) =>
        BrowserLauncher.OpenOrThrow($"https://zkillboard.com/system/{systemId}/");

    /// <summary>
    /// Refreshes the NPC-kills-per-system snapshot used to color schematic plates like
    /// Dotlan's "NPC Kills" map filter. Safe to call repeatedly (e.g. on a timer); failures
    /// (offline, ESI outage) are swallowed so the map just keeps showing the last-known data.
    /// </summary>
    public async Task RefreshNpcKillsAsync()
    {
        try
        {
            NpcKills = await _systemKillsClient.GetNpcKillsPerSystemAsync();
        }
        catch
        {
            // Keep whatever we had before; the caller can retry later.
        }
    }

    /// <summary>
    /// Refreshes the Sansha incursion system set from ESI's public incursions feed. Failures keep
    /// the last-known snapshot so the map does not flicker offline.
    /// </summary>
    public async Task RefreshSanshaIncursionsAsync()
    {
        try
        {
            SanshaIncursionSystems = await _incursionsClient.GetSanshaInfestedSystemIdsAsync();
        }
        catch
        {
            // Keep whatever we had before; the caller can retry later.
        }
    }

    /// <summary>
    /// Refreshes active Thera/Turnur wormhole signatures from EvE-Scout. Failures keep the
    /// last-known snapshot.
    /// </summary>
    public async Task RefreshEveScoutWormholesAsync()
    {
        try
        {
            EveScoutWormholes = await _eveScoutClient.GetActiveConnectionsAsync();
            EveScoutWormholesBySystem = IndexWormholeConnections(EveScoutWormholes);
        }
        catch
        {
            // Keep whatever we had before; the caller can retry later.
        }
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<WormholeConnection>> IndexWormholeConnections(
        IReadOnlyList<WormholeConnection> connections)
    {
        var buckets = new Dictionary<int, List<WormholeConnection>>();
        void Add(int systemId, WormholeConnection connection)
        {
            if (!buckets.TryGetValue(systemId, out var list))
            {
                list = new List<WormholeConnection>();
                buckets[systemId] = list;
            }
            list.Add(connection);
        }

        foreach (var connection in connections)
        {
            Add(connection.HubSystemId, connection);
            Add(connection.RemoteSystemId, connection);
        }

        return buckets.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<WormholeConnection>)pair.Value);
    }

    /// <summary>
    /// Refreshes IHUB alliance ownership from ESI's public sovereignty map. Failures keep the
    /// last-known snapshot.
    /// </summary>
    public async Task RefreshIhubAlliancesAsync()
    {
        try
        {
            IhubAllianceBySystem = await _sovereigntyClient.GetIhubAllianceNamesBySystemAsync();
        }
        catch
        {
            // Keep whatever we had before; the caller can retry later.
        }
    }

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
        var excludedVictimTypes = new HashSet<int>(repo.LoadExcludedKillVictimTypeIds());
        if (ShipCatalog.CapsuleTypeId is int capsuleTypeId)
            excludedVictimTypes.Add(capsuleTypeId);
        KillVictimFilter = new KillVictimFilter(excludedVictimTypes);
        NpcCapitalKillFilter = new NpcCapitalKillFilter(repo.LoadNpcCapitalShipTypeIds());
        NpcStationSystems = repo.LoadNpcStationSystemIds();
        Map.LoadStructures(UserStructures.LoadAll());
        RegionNames = repo.LoadRegions().ToDictionary(r => r.Id, r => r.Name);
    }

    /// <summary>
    /// Queries zKillboard for jump-reachable systems (batched by region) and updates
    /// <see cref="JumpRangePvPActivity"/> for red/yellow overlay highlighting.
    /// </summary>
    public async Task RefreshJumpRangePvPAsync(
        IReadOnlyCollection<int> systemIds,
        int? originSystemId = null,
        Action? onProgress = null,
        CancellationToken ct = default)
    {
        if (KillVictimFilter is null)
        {
            JumpRangePvPActivity = new Dictionary<int, PvPActivityLevel>();
            JumpRangePvPProgress = (0, 0, 0, 0, 0, 0, 0, 0);
            onProgress?.Invoke();
            return;
        }

        if (systemIds.Count == 0)
        {
            JumpRangePvPActivity = new Dictionary<int, PvPActivityLevel>();
            JumpRangePvPProgress = (0, 0, 0, 0, 0, 0, 0, 0);
            onProgress?.Invoke();
            return;
        }

        var targets = systemIds.ToHashSet();
        var previousActivity = JumpRangePvPActivity;
        var activity = targets.ToDictionary(
            id => id,
            id => previousActivity.GetValueOrDefault(id, PvPActivityLevel.None));
        JumpRangePvPActivity = activity;
        int total = targets.Count;
        int initialNetwork = CountRegionsNeedingFetch(systemIds);
        int initialHot = activity.Values.Count(v => v == PvPActivityLevel.Hot);
        int initialRecent = activity.Values.Count(v => v == PvPActivityLevel.Recent);
        int initialNpcCapital = activity.Values.Count(v => v == PvPActivityLevel.NpcCapital);
        JumpRangePvPProgress = (0, total, initialHot, initialRecent, initialNpcCapital, 0, 0, initialNetwork);
        onProgress?.Invoke();

        try
        {
            await _zkillboardClient.GetActivityLevelsAsync(
                targets,
                id => Map?.Get(id)?.RegionId,
                KillVictimFilter,
                NpcCapitalKillFilter,
                previousActivity: activity,
                progress =>
                {
                    int hot = progress.Activity.Values.Count(v => v == PvPActivityLevel.Hot);
                    int recent = progress.Activity.Values.Count(v => v == PvPActivityLevel.Recent);
                    int npcCapital = progress.Activity.Values.Count(v => v == PvPActivityLevel.NpcCapital);
                    JumpRangePvPActivity = progress.Activity;
                    JumpRangePvPProgress = (
                        progress.Completed,
                        progress.Total,
                        hot,
                        recent,
                        npcCapital,
                        progress.Failed,
                        progress.Cached,
                        progress.RemainingNetworkRequests);
                    onProgress?.Invoke();
                },
                ct);
        }
        catch
        {
            // Keep partial snapshot on failure.
        }
    }

    private int CountRegionsNeedingFetch(IReadOnlyCollection<int> systemIds)
    {
        if (Map is null) return 0;
        return systemIds
            .Select(id => Map.Get(id)?.RegionId)
            .Where(regionId => regionId is int id && !_zkillboardClient.IsRegionCacheFresh(id))
            .Select(regionId => regionId!.Value)
            .Distinct()
            .Count();
    }

    public void ReloadStructuresOnly()
    {
        Map?.LoadStructures(UserStructures.LoadAll());
    }

    /// <summary>Reloads only the NPC-station system set from the cache (no map rebuild, so the view is preserved).</summary>
    public void ReloadNpcStationData()
    {
        if (!SdeService.IsCached()) return;
        NpcStationSystems = SdeService.GetRepository().LoadNpcStationSystemIds();
    }

    /// <summary>
    /// Backfills NPC-station data into caches that predate the NpcStationSystems table by
    /// re-importing from the already-downloaded SDE archive (no network). Returns true when a
    /// re-import ran and the station set was refreshed.
    /// </summary>
    public async Task<bool> EnsureNpcStationDataAsync(CancellationToken ct = default)
    {
        if (NpcStationSystems.Count > 0) return false;
        bool reimported = await SdeService.TryReimportFromCachedZipAsync(ct);
        if (reimported) ReloadNpcStationData();
        return reimported && NpcStationSystems.Count > 0;
    }
}
