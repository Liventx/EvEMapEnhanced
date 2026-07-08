using EvEMapEnhanced.Core.Stats;

namespace EvEMapEnhanced.Data.Stats;

internal sealed record ZKillboardRequestProfile(TimeSpan MinSpacing, int MaxConcurrency)
{
    public static ZKillboardRequestProfile For(ZKillboardRequestMode mode) => mode switch
    {
        ZKillboardRequestMode.Faster => new(TimeSpan.FromMilliseconds(500), 2),
        _ => new(TimeSpan.FromSeconds(1), 1),
    };
}
