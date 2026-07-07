using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvEMapEnhanced.Data.Stats;

internal sealed class EsiSystemKillsDto
{
    public int SystemId { get; set; }
    public int NpcKills { get; set; }
    public int ShipKills { get; set; }
    public int PodKills { get; set; }
}

/// <summary>
/// Fetches ESI's bulk "system kills" feed (public, unauthenticated, one request for every
/// system in the game) -- the same last-hour NPC kill counts Dotlan's "NPC Kills" map filter
/// colors its system plates by. A single cheap call suitable for coloring (and labeling) every
/// visible plate at once.
/// </summary>
public sealed class EsiSystemKillsClient
{
    private const string Url = "https://esi.evetech.net/latest/universe/system_kills/?datasource=tranquility";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;

    public EsiSystemKillsClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>Solar system id -> NPC kills recorded there in the last hour.</summary>
    public async Task<IReadOnlyDictionary<int, int>> GetNpcKillsPerSystemAsync(CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync(Url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var entries = await JsonSerializer.DeserializeAsync<List<EsiSystemKillsDto>>(stream, JsonOptions, ct);

        var result = new Dictionary<int, int>();
        if (entries is null) return result;
        foreach (var entry in entries) result[entry.SystemId] = entry.NpcKills;
        return result;
    }
}
