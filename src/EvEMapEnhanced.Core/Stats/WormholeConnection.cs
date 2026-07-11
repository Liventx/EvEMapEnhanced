namespace EvEMapEnhanced.Core.Stats;

/// <summary>
/// An active wormhole signature between a Thera/Turnur hub and a remote solar system,
/// sourced from the public EvE-Scout API.
/// </summary>
public sealed record WormholeConnection(
    string Id,
    WormholeHubKind Hub,
    int HubSystemId,
    string HubSystemName,
    int RemoteSystemId,
    string RemoteSystemName,
    string HubSignature,
    string RemoteSignature,
    string WhType,
    string MaxShipSize,
    int? RemainingHours,
    DateTimeOffset? ExpiresAt,
    bool ExitsOutward);
