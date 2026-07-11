using EvEMapEnhanced.Core.Models;

namespace EvEMapEnhanced.Core.Stats;

/// <summary>Well-known Thera/Turnur hub ids and static Thera coordinates for Standard-mode placement.</summary>
public static class WormholeHubCatalog
{
    public const int TheraSystemId = 31_000_005;
    public const int TurnurSystemId = 30_002_086;

    /// <summary>
    /// Thera is filtered out of the k-space SDE map but still needs a real position for Standard
    /// mode and tooltips.
    /// </summary>
    public static SolarSystem CreateTheraSystem() => new(
        TheraSystemId,
        "Thera",
        ConstellationId: 21_000_324,
        RegionId: 11_000_002,
        Security: -0.99,
        X: 7_201_177_000_000_000_000.0,
        Y: 1_534_300_000_000_000_000.0,
        Z: -9_501_332_482_538_404_000.0);

    public static WormholeHubKind? TryGetHubKind(int systemId) => systemId switch
    {
        TheraSystemId => WormholeHubKind.Thera,
        TurnurSystemId => WormholeHubKind.Turnur,
        _ => null,
    };

    public static bool IsHubSystem(int systemId) => TryGetHubKind(systemId) is not null;
}
