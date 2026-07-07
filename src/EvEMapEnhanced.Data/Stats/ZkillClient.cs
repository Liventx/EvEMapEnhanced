using System.Text.Json;

namespace EvEMapEnhanced.Data.Stats;

public sealed record KillmailStub(long KillmailId, string Hash, double TotalValue, bool Npc);

/// <summary>
/// Minimal client for the public zKillboard list API
/// (see https://github.com/zKillboard/zKillboard/wiki/API-(Killmails)), used to fetch
/// recent kills for a solar system without requiring any authentication.
/// </summary>
public sealed class ZkillClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ZkillClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EvEMapEnhanced/0.1 (contact: local-dev)");
        }
    }

    public async Task<IReadOnlyList<KillmailStub>> GetRecentKillmailsAsync(int solarSystemId, int pastSeconds, CancellationToken ct = default)
    {
        string url = $"https://zkillboard.com/api/solarSystemID/{solarSystemId}/pastSeconds/{pastSeconds}/";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var dtos = await JsonSerializer.DeserializeAsync<List<ZkillKillmailStubDto>>(stream, JsonOptions, ct) ?? new();

        return dtos
            .Where(d => d.Zkb is not null)
            .Select(d => new KillmailStub(d.KillmailId, d.Zkb!.Hash, d.Zkb.TotalValue, d.Zkb.Npc))
            .ToList();
    }
}
