namespace EvEMapEnhanced.Core.Stats;

/// <summary>How aggressively the app queries zKillboard's public killmail API.</summary>
public enum ZKillboardRequestMode
{
    /// <summary>About one request per second, no parallelism — safest for zKillboard.</summary>
    Polite = 0,

    /// <summary>Up to two parallel requests, about two per second — faster but may be throttled.</summary>
    Faster = 1,
}
