namespace EvEMapEnhanced.Core.Routing;

/// <summary>
/// Stargate route preference, matching EVE ESI / in-game autopilot and DOTLAN route options.
/// </summary>
public enum GateRoutePreference
{
    /// <summary>Fewest jumps regardless of security (DOTLAN: "Fastest Route").</summary>
    Shorter,

    /// <summary>Prefer high-security space; may take more jumps (DOTLAN: "Prefer HighSec").</summary>
    Safer,

    /// <summary>Prefer low-security / null-security space (DOTLAN: "Prefer Lowsec/0.0").</summary>
    LessSecure,
}
