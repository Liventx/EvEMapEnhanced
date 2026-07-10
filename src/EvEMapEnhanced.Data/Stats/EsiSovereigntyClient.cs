using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvEMapEnhanced.Data.Stats;

internal sealed class EsiSovereigntyMapEntryDto
{
    [JsonPropertyName("system_id")]
    public int SystemId { get; set; }

    [JsonPropertyName("alliance_id")]
    public int? AllianceId { get; set; }
}

internal sealed class EsiUniverseNameDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

/// <summary>
/// Fetches ESI sovereignty occupancy and resolves alliance names for nullsec systems with an
/// IHUB / Sovereignty Hub (player alliance on the sovereignty map).
/// </summary>
public sealed class EsiSovereigntyClient
{
    private const string MapUrl = "https://esi.evetech.net/latest/sovereignty/map/?datasource=tranquility";
    private const string NamesUrl = "https://esi.evetech.net/latest/universe/names/?datasource=tranquility";
    private const int NamesBatchSize = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;

    public EsiSovereigntyClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>Solar system id -> alliance name holding the system's sovereignty hub.</summary>
    public async Task<IReadOnlyDictionary<int, string>> GetIhubAllianceNamesBySystemAsync(CancellationToken ct = default)
    {
        using var mapResponse = await _httpClient.GetAsync(MapUrl, ct);
        mapResponse.EnsureSuccessStatusCode();

        await using var mapStream = await mapResponse.Content.ReadAsStreamAsync(ct);
        var entries = await JsonSerializer.DeserializeAsync<List<EsiSovereigntyMapEntryDto>>(mapStream, JsonOptions, ct)
            ?? [];

        var systemAlliance = new Dictionary<int, int>();
        foreach (var entry in entries)
        {
            if (entry.AllianceId is int allianceId)
                systemAlliance[entry.SystemId] = allianceId;
        }

        if (systemAlliance.Count == 0)
            return new Dictionary<int, string>();

        var allianceNames = await ResolveAllianceNamesAsync(systemAlliance.Values.Distinct(), ct);

        var result = new Dictionary<int, string>(systemAlliance.Count);
        foreach (var (systemId, allianceId) in systemAlliance)
        {
            if (allianceNames.TryGetValue(allianceId, out string? name))
                result[systemId] = name;
        }

        return result;
    }

    private async Task<Dictionary<int, string>> ResolveAllianceNamesAsync(IEnumerable<int> allianceIds, CancellationToken ct)
    {
        var result = new Dictionary<int, string>();
        var pending = allianceIds.ToList();
        for (int offset = 0; offset < pending.Count; offset += NamesBatchSize)
        {
            int count = Math.Min(NamesBatchSize, pending.Count - offset);
            var batch = pending.GetRange(offset, count);

            using var response = await _httpClient.PostAsJsonAsync(NamesUrl, batch, JsonOptions, ct);
            response.EnsureSuccessStatusCode();

            var names = await response.Content.ReadFromJsonAsync<List<EsiUniverseNameDto>>(JsonOptions, ct);
            if (names is null) continue;

            foreach (var entry in names)
                result[entry.Id] = entry.Name;
        }

        return result;
    }
}
