using System.Text.Json;
using System.Text.Json.Serialization;
using EvEMapEnhanced.Core.Stats;

namespace EvEMapEnhanced.Data.Stats;

internal sealed class EveScoutSignatureDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("wh_type")]
    public string WhType { get; set; } = "";

    [JsonPropertyName("max_ship_size")]
    public string MaxShipSize { get; set; } = "";

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("remaining_hours")]
    public int? RemainingHours { get; set; }

    [JsonPropertyName("wh_exits_outward")]
    public bool WhExitsOutward { get; set; }

    [JsonPropertyName("out_system_id")]
    public int OutSystemId { get; set; }

    [JsonPropertyName("out_system_name")]
    public string OutSystemName { get; set; } = "";

    [JsonPropertyName("out_signature")]
    public string OutSignature { get; set; } = "";

    [JsonPropertyName("in_system_id")]
    public int InSystemId { get; set; }

    [JsonPropertyName("in_system_name")]
    public string InSystemName { get; set; } = "";

    [JsonPropertyName("in_signature")]
    public string InSignature { get; set; } = "";
}

/// <summary>
/// Fetches active Thera/Turnur wormhole signatures from the public EvE-Scout API.
/// </summary>
public sealed class EveScoutWormholesClient
{
    public const string DefaultUrl = "https://api.eve-scout.com/v2/public/signatures";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly string _url;

    public EveScoutWormholesClient(HttpClient? httpClient = null, string? url = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _url = url ?? DefaultUrl;
    }

    public async Task<IReadOnlyList<WormholeConnection>> GetActiveConnectionsAsync(CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync(_url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var entries = await JsonSerializer.DeserializeAsync<List<EveScoutSignatureDto>>(stream, JsonOptions, ct)
            ?? [];

        var result = new List<WormholeConnection>();
        foreach (var entry in entries)
        {
            if (!entry.Completed) continue;
            if (TryParseConnection(entry, out var connection))
                result.Add(connection);
        }

        return result;
    }

    internal static bool TryParseConnection(EveScoutSignatureDto entry, out WormholeConnection connection)
    {
        connection = null!;
        bool outIsHub = WormholeHubCatalog.IsHubSystem(entry.OutSystemId);
        bool inIsHub = WormholeHubCatalog.IsHubSystem(entry.InSystemId);
        if (outIsHub == inIsHub) return false;

        int hubId = outIsHub ? entry.OutSystemId : entry.InSystemId;
        var hubKind = WormholeHubCatalog.TryGetHubKind(hubId)!.Value;
        int remoteId = outIsHub ? entry.InSystemId : entry.OutSystemId;
        string hubName = outIsHub ? entry.OutSystemName : entry.InSystemName;
        string remoteName = outIsHub ? entry.InSystemName : entry.OutSystemName;
        string hubSig = outIsHub ? entry.OutSignature : entry.InSignature;
        string remoteSig = outIsHub ? entry.InSignature : entry.OutSignature;

        connection = new WormholeConnection(
            entry.Id,
            hubKind,
            hubId,
            hubName,
            remoteId,
            remoteName,
            hubSig,
            remoteSig,
            entry.WhType,
            entry.MaxShipSize,
            entry.RemainingHours,
            entry.ExpiresAt,
            entry.WhExitsOutward);

        return true;
    }
}
