using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EvEMapEnhanced.Core.Auth;
using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Routing;
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
    public SavedRouteRepository SavedRoutes { get; }
    public SystemNoteRepository SystemNotes { get; }
    private readonly EsiSystemKillsClient _systemKillsClient = new();

    private readonly HttpClient _httpClient = new();
    private readonly EsiCharacterSkillsClient _skillsClient;
    private readonly EsiCharacterLocationClient _locationClient;
    private EsiOAuthClient? _oauthClient;
    private EsiAccessTokenProvider? _tokenProvider;

    public UniverseMap? Map { get; private set; }
    public ShipTypeCatalog? ShipCatalog { get; private set; }
    public IReadOnlyDictionary<int, string>? RegionNames { get; private set; }

    /// <summary>Solar system id -> NPC kills in the last hour (ESI, refreshed via <see cref="RefreshNpcKillsAsync"/>).</summary>
    public IReadOnlyDictionary<int, int>? NpcKills { get; private set; }

    public AppServices()
    {
        SdeService = new SdeService();
        Characters = new AuthenticatedCharacterRepository(AppPaths.UserDbPath);
        UserStructures = new UserStructureRepository(AppPaths.UserDbPath);
        SavedRoutes = new SavedRouteRepository(AppPaths.UserDbPath);
        SystemNotes = new SystemNoteRepository(AppPaths.UserDbPath);
        _skillsClient = new EsiCharacterSkillsClient(_httpClient);
        _locationClient = new EsiCharacterLocationClient(_httpClient);
    }

    public bool IsMapLoaded => Map is not null;

    public IReadOnlyList<AuthenticatedCharacter> LoadCharacters() => Characters.LoadAll();

    /// <summary>
    /// Runs the full "Sign in with EVE Online" flow (opens the user's default browser to
    /// login.eveonline.com, waits for the local redirect, exchanges the code, fetches skills).
    /// </summary>
    public async Task<AuthenticatedCharacter> SignInWithEveOnlineAsync(EsiAuthSettings settings, CancellationToken ct = default)
    {
        EnsureOAuthClient(settings);
        var flow = new EsiSignInFlow(settings, Characters, _httpClient);
        return await flow.SignInAsync(OpenInBrowser, ct);
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

    public void SignOutCharacter(long characterId) => Characters.Delete(characterId);

    private void EnsureOAuthClient(EsiAuthSettings settings)
    {
        _oauthClient ??= new EsiOAuthClient(settings.ClientId, _httpClient);
        _tokenProvider ??= new EsiAccessTokenProvider(_oauthClient, Characters);
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // If the OS can't launch a browser for us, the sign-in call will time out/fail and
            // surface an error to the user; there's no good local fallback for a GUI app.
        }
    }

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
        RegionNames = repo.LoadRegions().ToDictionary(r => r.Id, r => r.Name);
    }

    public void ReloadStructuresOnly()
    {
        Map?.LoadStructures(UserStructures.LoadAll());
    }
}
