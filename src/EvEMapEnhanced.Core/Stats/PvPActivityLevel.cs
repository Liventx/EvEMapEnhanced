namespace EvEMapEnhanced.Core.Stats;

/// <summary>Recent kill activity in a solar system (zKillboard, jump-range overlay).</summary>
public enum PvPActivityLevel
{
    None = 0,
    /// <summary>One to four valid player deaths in the last hour.</summary>
    Recent = 1,
    /// <summary>Five or more valid player deaths in the last hour.</summary>
    Hot = 2,
    /// <summary>NPC dreadnought or titan kill activity in the last 30 minutes.</summary>
    NpcCapital = 3,
}
