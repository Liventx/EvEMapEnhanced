namespace EvEMapEnhanced.Core.Stats;

/// <summary>Recent zKillboard activity in a solar system (level plus raw kill count).</summary>
public sealed record PvPActivityStats(PvPActivityLevel Level, int ValidHourKillCount)
{
    public static PvPActivityStats None { get; } = new(PvPActivityLevel.None, 0);
}
