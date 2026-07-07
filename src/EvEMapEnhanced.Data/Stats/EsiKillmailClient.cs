using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvEMapEnhanced.Data.Stats;

internal sealed class EsiKillmailVictimDto
{
    public int ShipTypeId { get; set; }
}

internal sealed class EsiKillmailDto
{
    public EsiKillmailVictimDto? Victim { get; set; }
}

/// <summary>
/// Fetches full killmail detail from ESI (public, unauthenticated endpoint) to read the
/// victim's ship type ID -- needed to classify a kill as "capital" or "pod kill".
/// Only called for a bounded number of recent kills per system to respect rate limits.
/// </summary>
public sealed class EsiKillmailClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public EsiKillmailClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<int?> GetVictimShipTypeIdAsync(long killmailId, string hash, CancellationToken ct = default)
    {
        string url = $"https://esi.evetech.net/latest/killmails/{killmailId}/{hash}/?datasource=tranquility";
        using var response = await _httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var dto = await JsonSerializer.DeserializeAsync<EsiKillmailDto>(stream, JsonOptions, ct);
        return dto?.Victim?.ShipTypeId;
    }
}
