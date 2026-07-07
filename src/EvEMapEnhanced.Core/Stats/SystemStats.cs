namespace EvEMapEnhanced.Core.Stats;

/// <summary>Aggregated recent-activity statistics for a solar system, used for display and routing penalties.</summary>
public sealed record SystemStats(
    int SolarSystemId,
    int KillsLastHour,
    int KillsLast24H,
    int CapitalKillsLast24H,
    int PodKillsLast24H,
    double IskDestroyedLast24H,
    DateTime LastUpdatedUtc)
{
    /// <summary>
    /// A simple weighted activity score used as a soft routing penalty:
    /// capital kills and pod kills (indicators of active hunting/camping) weigh more
    /// than plain kill count.
    /// </summary>
    public double ActivityScore => KillsLast24H * 1.0 + CapitalKillsLast24H * 5.0 + PodKillsLast24H * 2.0;
}
