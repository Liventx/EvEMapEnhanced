using EvEMapEnhanced.Core.Models;

namespace EvEMapEnhanced.Core.Jump;

/// <summary>EVE jump-drive rules that constrain where cyno-based jumps may end.</summary>
public static class JumpRules
{
    /// <summary>EVE region ID for Pochven — cynos cannot be lit anywhere in this region.</summary>
    public const int PochvenRegionId = 10000070;

    /// <summary>
    /// Standard and covert cynos may only be lit in systems with true security &lt;= 0.4
    /// (EVE UI: 0.4 and below on the map), except Pochven where cyno fields are forbidden.
    /// Jump bridges (Ansiblex) are exempt.
    /// </summary>
    public static bool AllowsCynoField(SolarSystem system) =>
        system.Security <= 0.4 && system.RegionId != PochvenRegionId;

    public static bool IsValidJumpLanding(SolarSystem system, JumpMethod method) =>
        method == JumpMethod.JumpBridge || AllowsCynoField(system);
}
