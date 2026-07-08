namespace EvEMapEnhanced.Data.Stats;

/// <summary>
/// Detects NPC dreadnought or titan involvement in a zKillboard killmail (victim or attacker hull).
/// Type IDs are resolved from the SDE at import time (groups Dreadnought and Titan).
/// </summary>
public sealed class NpcCapitalKillFilter
{
    public IReadOnlySet<int> CapitalShipTypeIds { get; }

    public NpcCapitalKillFilter(IReadOnlySet<int> capitalShipTypeIds)
    {
        CapitalShipTypeIds = capitalShipTypeIds;
    }

    public bool IsNpcCapitalEvent(ZKillboardKillmail kill)
    {
        if (!kill.Npc) return false;

        if (CapitalShipTypeIds.Contains(kill.VictimShipTypeId))
            return true;

        foreach (int shipTypeId in kill.AttackerShipTypeIds)
        {
            if (CapitalShipTypeIds.Contains(shipTypeId))
                return true;
        }

        return false;
    }
}
