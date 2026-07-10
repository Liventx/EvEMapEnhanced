using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvEMapEnhanced.Data.Auth;

/// <summary>Fetches a character's current ship type from ESI, used to auto-select jump-range ship class.</summary>
public sealed class EsiCharacterShipClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public EsiCharacterShipClient(HttpClient? httpClient = null) => _httpClient = httpClient ?? new HttpClient();

    public async Task<int> GetShipTypeIdAsync(long characterId, string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://esi.evetech.net/latest/characters/{characterId}/ship/?datasource=tranquility");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<EsiShipDto>(JsonOptions, ct);
        return dto?.ShipTypeId ?? throw new InvalidOperationException("ESI ship response is missing ship_type_id.");
    }

    private sealed class EsiShipDto
    {
        [JsonPropertyName("ship_type_id")]
        public int ShipTypeId { get; set; }
    }
}
