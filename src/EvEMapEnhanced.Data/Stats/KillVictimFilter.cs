namespace EvEMapEnhanced.Data.Stats;

/// <summary>
/// Excludes capsule, shuttle, corvette and other non-relevant victim hulls when counting
/// player deaths from zKillboard. Type IDs are resolved from the SDE at import time.
/// </summary>
public sealed class KillVictimFilter
{
    public IReadOnlySet<int> ExcludedVictimTypeIds { get; }

    public KillVictimFilter(IReadOnlySet<int> excludedVictimTypeIds)
    {
        ExcludedVictimTypeIds = excludedVictimTypeIds;
    }

    public bool IsExcludedVictim(int shipTypeId) => ExcludedVictimTypeIds.Contains(shipTypeId);
}
