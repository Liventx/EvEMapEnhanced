using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EvEMapEnhanced.Core.Jump;

namespace EvEMapEnhanced.Data.Auth;

/// <summary>Fetches a character's trained skill sheet from ESI and maps the jump-relevant subset into <see cref="PilotSkills"/>.</summary>
public sealed class EsiCharacterSkillsClient
{
    // Static EVE type IDs for the jump-relevant skills - stable identifiers from CCP's SDE.
    private const int JumpDriveCalibrationTypeId = 21611;
    private const int JumpFuelConservationTypeId = 21610;
    private const int JumpFreightersTypeId = 29029;
    private const int CapitalShipsTypeId = 20533;
    private const int BlackOpsTypeId = 28656;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public EsiCharacterSkillsClient(HttpClient? httpClient = null) => _httpClient = httpClient ?? new HttpClient();

    public async Task<PilotSkills> GetSkillsAsync(long characterId, string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://esi.evetech.net/latest/characters/{characterId}/skills/?datasource=tranquility");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<EsiSkillsDto>(JsonOptions, ct);
        var levels = (dto?.Skills ?? new List<EsiSkillEntryDto>()).ToDictionary(s => s.SkillId, s => s.ActiveSkillLevel);
        return MapToPilotSkills(levels);
    }

    /// <summary>
    /// Pure mapping from (skill type id -&gt; active level) to the jump-relevant
    /// <see cref="PilotSkills"/> fields, kept separate from the HTTP call so it's directly
    /// unit-testable without mocking a network response.
    /// </summary>
    public static PilotSkills MapToPilotSkills(IReadOnlyDictionary<int, int> skillLevelsByTypeId) => new()
    {
        JumpDriveCalibration = skillLevelsByTypeId.GetValueOrDefault(JumpDriveCalibrationTypeId),
        JumpFuelConservation = skillLevelsByTypeId.GetValueOrDefault(JumpFuelConservationTypeId),
        JumpFreighters = skillLevelsByTypeId.GetValueOrDefault(JumpFreightersTypeId),
        CapitalShips = skillLevelsByTypeId.GetValueOrDefault(CapitalShipsTypeId),
        BlackOps = skillLevelsByTypeId.GetValueOrDefault(BlackOpsTypeId),
    };

    private sealed class EsiSkillsDto
    {
        [JsonPropertyName("skills")]
        public List<EsiSkillEntryDto>? Skills { get; set; }
    }

    private sealed class EsiSkillEntryDto
    {
        [JsonPropertyName("skill_id")]
        public int SkillId { get; set; }

        [JsonPropertyName("active_skill_level")]
        public int ActiveSkillLevel { get; set; }
    }
}
