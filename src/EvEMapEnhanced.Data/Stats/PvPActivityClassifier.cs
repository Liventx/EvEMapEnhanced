using EvEMapEnhanced.Core.Stats;

namespace EvEMapEnhanced.Data.Stats;

/// <summary>
/// Classifies recent activity from zKillboard killmails in the last hour: player PvP deaths
/// (red/yellow) and NPC dreadnought/titan events in the last 30 minutes (purple).
/// </summary>
public static class PvPActivityClassifier
{
    public const int HotDeathThreshold = 5;

    private static readonly TimeSpan HourWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan NpcCapitalWindow = TimeSpan.FromMinutes(30);

    public static PvPActivityLevel Classify(
        IEnumerable<ZKillboardKillmail> kills,
        KillVictimFilter filter,
        DateTime utcNow,
        NpcCapitalKillFilter? npcCapitalFilter = null)
    {
        var hourCutoff = utcNow - HourWindow;
        var npcCutoff = utcNow - NpcCapitalWindow;
        int validHourCount = 0;
        bool npcCapital = false;

        foreach (var kill in kills)
        {
            if (!DateTime.TryParse(kill.KillmailTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var time))
                continue;

            if (npcCapitalFilter is not null && time >= npcCutoff && npcCapitalFilter.IsNpcCapitalEvent(kill))
                npcCapital = true;

            if (!IsCountablePlayerDeath(kill, filter) || time < hourCutoff)
                continue;

            validHourCount++;
        }

        if (npcCapital) return PvPActivityLevel.NpcCapital;
        if (validHourCount >= HotDeathThreshold) return PvPActivityLevel.Hot;
        if (validHourCount >= 1) return PvPActivityLevel.Recent;
        return PvPActivityLevel.None;
    }

    private static bool IsCountablePlayerDeath(ZKillboardKillmail kill, KillVictimFilter filter)
    {
        if (kill.Npc) return false;
        return !filter.IsExcludedVictim(kill.VictimShipTypeId);
    }
}
