namespace EvEMapEnhanced.Core.Stats;

/// <summary>
/// A user-placed wormhole marker on a k-space solar system. Expires automatically after 24 hours.
/// </summary>
public sealed record ManualWormholeMarker(
    int SolarSystemId,
    string? ExitComment,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);
