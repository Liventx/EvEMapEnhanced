namespace EvEMapEnhanced.Core.Routing;

/// <summary>
/// Routing preferences shared by the gate, jump, and hybrid pathfinders.
/// </summary>
public sealed class RouteFilterOptions
{
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
