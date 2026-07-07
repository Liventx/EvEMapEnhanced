namespace EvEMapEnhanced.Core.Routing;

/// <summary>
/// Routing preferences shared by the gate, jump, and hybrid pathfinders.
/// </summary>
public sealed class RouteFilterOptions
{
    /// <summary>Gate routing preference (matches ESI / DOTLAN route modes).</summary>
    public GateRoutePreference Preference { get; set; } = GateRoutePreference.Shorter;

    /// <summary>Security penalty weight (0-100), reserved for future ESI-exact mode.</summary>
    public int SecurityPenalty { get; set; } = 50;

    public bool AvoidLowSec { get; set; }
    public bool AvoidNullSec { get; set; }

    /// <summary>Explicit system IDs to never route through (user avoid list / cyno-jammed systems / etc.).</summary>
    public HashSet<int> AvoidSystemIds { get; set; } = new();

    /// <summary>
    /// Optional per-system routing penalty (e.g. derived from recent kill activity).
    /// Returns an additive cost added on top of the base edge weight of 1 when
    /// entering the given system. Return 0 for "no penalty".
    /// </summary>
    public Func<int, double>? SystemPenalty { get; set; }

    /// <summary>If no route avoiding hard filters exists, fall back to an unrestricted route
    /// instead of returning no route at all.</summary>
    public bool AllowFallbackIfBlocked { get; set; } = true;
}
