using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using EvEMapEnhanced.Core.Stats;

namespace EvEMapEnhanced.Data.Stats;

internal sealed class ZKillboardKillmailDto
{
    [JsonPropertyName("killmail_id")]
    public long KillmailId { get; set; }

    [JsonPropertyName("killmail_time")]
    public string KillmailTime { get; set; } = "";

    [JsonPropertyName("solar_system_id")]
    public int SolarSystemId { get; set; }

    [JsonPropertyName("victim")]
    public ZKillboardVictimDto Victim { get; set; } = new();

    [JsonPropertyName("attackers")]
    public List<ZKillboardAttackerDto> Attackers { get; set; } = new();

    [JsonPropertyName("zkb")]
    public ZKillboardMetaDto Zkb { get; set; } = new();
}

internal sealed class ZKillboardVictimDto
{
    [JsonPropertyName("ship_type_id")]
    public int ShipTypeId { get; set; }
}

internal sealed class ZKillboardAttackerDto
{
    [JsonPropertyName("ship_type_id")]
    public int ShipTypeId { get; set; }
}

internal sealed class ZKillboardMetaDto
{
    [JsonPropertyName("npc")]
    public bool Npc { get; set; }
}

public sealed class ZKillboardKillmail
{
    public int SolarSystemId { get; init; }
    public string KillmailTime { get; init; } = "";
    public int VictimShipTypeId { get; init; }
    public bool Npc { get; init; }
    public IReadOnlyList<int> AttackerShipTypeIds { get; init; } = Array.Empty<int>();
}

public sealed record ZKillboardFetchProgress(
    int Completed,
    int Total,
    int Failed,
    int Cached,
    int RemainingNetworkRequests,
    IReadOnlyDictionary<int, PvPActivityLevel> Activity);

/// <summary>
/// Fetches recent killmails from zKillboard (last hour, player and NPC) using a per-region feed.
/// Jump-range overlays classify each target system from kills in that regional slice.
/// </summary>
public sealed class ZKillboardSystemKillsClient
{
    private const string UserAgentProduct = "EvEMapEnhanced";
    private const string UserAgentVersion = "1.0.2";
    private const int MaxRegionPages = 25;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(3);

    private readonly HttpClient _httpClient;
    private readonly object _throttleLock = new();
    private DateTime _nextRequestSlotUtc = DateTime.MinValue;
    private readonly ConcurrentDictionary<int, (DateTime FetchedUtc, IReadOnlyList<ZKillboardKillmail> Kills)> _regionCache = new();

    public ZKillboardRequestMode RequestMode { get; set; } = ZKillboardRequestMode.Polite;

    public ZKillboardSystemKillsClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateHttpClient();
    }

    public static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseProxy = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgentProduct, UserAgentVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
        return client;
    }

    public bool IsRegionCacheFresh(int regionId) =>
        _regionCache.TryGetValue(regionId, out var entry) && DateTime.UtcNow - entry.FetchedUtc < CacheTtl;

    public async Task<IReadOnlyDictionary<int, PvPActivityLevel>> GetActivityLevelsAsync(
        IReadOnlyCollection<int> targetSystemIds,
        Func<int, int?> regionLookup,
        KillVictimFilter filter,
        NpcCapitalKillFilter? npcCapitalFilter = null,
        Action<ZKillboardFetchProgress>? onProgress = null,
        CancellationToken ct = default)
    {
        var targets = targetSystemIds.ToHashSet();
        var activity = targets.ToDictionary(id => id, _ => PvPActivityLevel.None);
        if (targets.Count == 0) return activity;

        var profile = ZKillboardRequestProfile.For(RequestMode);
        var utcNow = DateTime.UtcNow;
        int totalSystems = targets.Count;

        var regionGroups = targets
            .Select(id => (SystemId: id, RegionId: regionLookup(id)))
            .Where(x => x.RegionId is int)
            .GroupBy(x => x.RegionId!.Value)
            .OrderBy(g => g.Key)
            .ToList();

        int regionsNeedingNetwork = regionGroups.Count(g => !IsRegionCacheFresh(g.Key));
        int completedSystems = 0;
        int failedRegions = 0;
        int cachedRegions = 0;
        int networkRegionsDone = 0;

        void Report()
        {
            int remainingNetwork = Math.Max(0, regionsNeedingNetwork - networkRegionsDone);
            onProgress?.Invoke(new ZKillboardFetchProgress(
                completedSystems,
                totalSystems,
                failedRegions,
                cachedRegions,
                remainingNetwork,
                activity));
        }

        await Parallel.ForEachAsync(
            regionGroups,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = profile.MaxConcurrency,
                CancellationToken = ct,
            },
            async (regionGroup, token) =>
            {
                int regionId = regionGroup.Key;
                var systemsInRegion = regionGroup.Select(x => x.SystemId).ToHashSet();
                bool usedNetwork = false;
                bool fromCache = false;

                try
                {
                    IReadOnlyList<ZKillboardKillmail> regionKills;
                    if (TryGetCachedRegionKills(regionId, out var cachedKills))
                    {
                        fromCache = true;
                        regionKills = cachedKills;
                    }
                    else
                    {
                        usedNetwork = true;
                        regionKills = await FetchRegionKillsAsync(regionId, profile.MinSpacing, token);
                        _regionCache[regionId] = (DateTime.UtcNow, regionKills);
                    }

                    var killsBySystem = regionKills
                        .Where(k => systemsInRegion.Contains(k.SolarSystemId))
                        .GroupBy(k => k.SolarSystemId)
                        .ToDictionary(g => g.Key, g => (IEnumerable<ZKillboardKillmail>)g);

                    foreach (int systemId in systemsInRegion)
                    {
                        var kills = killsBySystem.GetValueOrDefault(systemId, Array.Empty<ZKillboardKillmail>());
                        activity[systemId] = PvPActivityClassifier.Classify(kills, filter, utcNow, npcCapitalFilter);
                    }
                }
                catch
                {
                    Interlocked.Increment(ref failedRegions);
                    foreach (int systemId in systemsInRegion)
                        activity[systemId] = PvPActivityLevel.None;
                }
                finally
                {
                    if (fromCache) Interlocked.Increment(ref cachedRegions);
                    if (usedNetwork) Interlocked.Increment(ref networkRegionsDone);
                    Interlocked.Add(ref completedSystems, systemsInRegion.Count);
                    Report();
                }
            });

        return activity;
    }

    private bool TryGetCachedRegionKills(int regionId, out IReadOnlyList<ZKillboardKillmail> kills)
    {
        if (IsRegionCacheFresh(regionId))
        {
            kills = _regionCache[regionId].Kills;
            return true;
        }

        kills = Array.Empty<ZKillboardKillmail>();
        return false;
    }

    private async Task<IReadOnlyList<ZKillboardKillmail>> FetchRegionKillsAsync(
        int regionId,
        TimeSpan minSpacing,
        CancellationToken ct)
    {
        var all = new List<ZKillboardKillmail>();
        for (int page = 1; page <= MaxRegionPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            await WaitForRequestSlotAsync(minSpacing, ct);
            var dtos = await GetRecentKillsAsync($"regionID/{regionId}", page, ct);
            if (dtos.Count == 0) break;

            all.AddRange(MapKillmails(dtos));
            if (dtos.Count < 200) break;
        }

        return all;
    }

    private async Task<List<ZKillboardKillmailDto>> GetRecentKillsAsync(string entityPath, int page, CancellationToken ct)
    {
        string url = $"https://zkillboard.com/api/kills/{entityPath}/pastSeconds/3600/page/{page}/";
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var kills = await JsonSerializer.DeserializeAsync<List<ZKillboardKillmailDto>>(stream, JsonOptions, ct);
        return kills ?? new List<ZKillboardKillmailDto>();
    }

    private static IReadOnlyList<ZKillboardKillmail> MapKillmails(IEnumerable<ZKillboardKillmailDto> dtos) =>
        dtos
            .Where(dto => dto.SolarSystemId > 0)
            .Select(dto => new ZKillboardKillmail
            {
                SolarSystemId = dto.SolarSystemId,
                KillmailTime = dto.KillmailTime ?? "",
                VictimShipTypeId = dto.Victim?.ShipTypeId ?? 0,
                Npc = dto.Zkb?.Npc ?? false,
                AttackerShipTypeIds = dto.Attackers?
                    .Where(a => a.ShipTypeId > 0)
                    .Select(a => a.ShipTypeId)
                    .ToArray() ?? Array.Empty<int>(),
            })
            .ToList();

    private async Task WaitForRequestSlotAsync(TimeSpan minSpacing, CancellationToken ct)
    {
        DateTime slot;
        lock (_throttleLock)
        {
            var now = DateTime.UtcNow;
            slot = _nextRequestSlotUtc > now ? _nextRequestSlotUtc : now;
            _nextRequestSlotUtc = slot + minSpacing;
        }

        var delay = slot - DateTime.UtcNow;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, ct);
    }
}
