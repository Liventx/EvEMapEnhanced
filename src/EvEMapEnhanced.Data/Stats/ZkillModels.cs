using System.Text.Json.Serialization;

namespace EvEMapEnhanced.Data.Stats;

/// <summary>A single killmail stub as returned by the zKillboard list API (not the full killmail).</summary>
internal sealed class ZkillKillmailStubDto
{
    [JsonPropertyName("killmail_id")]
    public long KillmailId { get; set; }
    public ZkillZkbDto? Zkb { get; set; }
}

internal sealed class ZkillZkbDto
{
    public string Hash { get; set; } = string.Empty;
    public double TotalValue { get; set; }
    public bool Npc { get; set; }
}
