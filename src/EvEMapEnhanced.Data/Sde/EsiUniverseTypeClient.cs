using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvEMapEnhanced.Data.Sde;

/// <summary>Public ESI lookup for a type's inventory group, used to classify hulls beyond the seeded registry.</summary>
public sealed class EsiUniverseTypeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;
    private readonly Dictionary<int, int> _groupIdByTypeId = new();

    public EsiUniverseTypeClient(HttpClient? httpClient = null) => _httpClient = httpClient ?? new HttpClient();

    public async Task<int> GetGroupIdAsync(int typeId, CancellationToken ct = default)
    {
        if (_groupIdByTypeId.TryGetValue(typeId, out int cachedGroupId))
            return cachedGroupId;

        using var response = await _httpClient.GetAsync(
            $"https://esi.evetech.net/latest/universe/types/{typeId}/?datasource=tranquility", ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<EsiTypeDto>(JsonOptions, ct);
        int groupId = dto?.GroupId
            ?? throw new InvalidOperationException($"ESI type response for {typeId} is missing group_id.");

        _groupIdByTypeId[typeId] = groupId;
        return groupId;
    }

    private sealed class EsiTypeDto
    {
        [JsonPropertyName("group_id")]
        public int GroupId { get; set; }
    }
}
