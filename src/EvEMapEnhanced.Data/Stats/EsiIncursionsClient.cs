using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvEMapEnhanced.Data.Stats;

internal sealed class EsiIncursionDto
{
    [JsonPropertyName("faction_id")]
    public int FactionId { get; set; }

    [JsonPropertyName("infested_solar_systems")]
    public List<int> InfestedSolarSystems { get; set; } = [];
}

/// <summary>
/// Fetches ESI's public incursions feed (Sansha Nation invasions) and returns every infested
/// solar system id across all active incursions.
/// </summary>
public sealed class EsiIncursionsClient
{
    /// <summary>Sansha Nation faction id in the SDE/ESI.</summary>
    public const int SanshaFactionId = 500019;

    private const string Url = "https://esi.evetech.net/latest/incursions/?datasource=tranquility";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;

    public EsiIncursionsClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>Solar system ids currently infested by Sansha Nation incursions.</summary>
    public async Task<IReadOnlySet<int>> GetSanshaInfestedSystemIdsAsync(CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync(Url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var entries = await JsonSerializer.DeserializeAsync<List<EsiIncursionDto>>(stream, JsonOptions, ct);

        var result = new HashSet<int>();
        if (entries is null) return result;

        foreach (var entry in entries)
        {
            if (entry.FactionId != SanshaFactionId) continue;
            foreach (int systemId in entry.InfestedSolarSystems)
                result.Add(systemId);
        }

        return result;
    }
}
