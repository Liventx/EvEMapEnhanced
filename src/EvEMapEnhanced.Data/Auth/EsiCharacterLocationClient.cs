using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvEMapEnhanced.Data.Auth;

/// <summary>Fetches a character's current solar system from ESI, used to drive live "follow pilot" jump-range tracking.</summary>
public sealed class EsiCharacterLocationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public EsiCharacterLocationClient(HttpClient? httpClient = null) => _httpClient = httpClient ?? new HttpClient();

    public async Task<int> GetSolarSystemIdAsync(long characterId, string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://esi.evetech.net/latest/characters/{characterId}/location/?datasource=tranquility");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<EsiLocationDto>(JsonOptions, ct);
        return dto?.SolarSystemId ?? throw new InvalidOperationException("ESI location response is missing solar_system_id.");
    }

    private sealed class EsiLocationDto
    {
        [JsonPropertyName("solar_system_id")]
        public int SolarSystemId { get; set; }
    }
}
