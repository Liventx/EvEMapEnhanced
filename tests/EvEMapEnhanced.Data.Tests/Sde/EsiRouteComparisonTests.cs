using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Data.Paths;
using EvEMapEnhanced.Data.Sde;
using Xunit;

namespace EvEMapEnhanced.Data.Tests.Sde;

/// <summary>
/// Validates gate routing against ESI reference routes (requires a locally cached production SDE).
/// </summary>
public class EsiRouteComparisonTests
{
    private static readonly int[] EsiJitaToAmarrShorter =
    {
        30000142, 30000138, 30000132, 30000134, 30005196, 30005192,
        30004083, 30004078, 30004079, 30004080, 30002282, 30002187,
    };

    private static readonly int[] EsiJitaToDodixieShorter =
    {
        30000142, 30000138, 30001379, 30001376, 30002813, 30002809,
        30002811, 30002812, 30005334, 30005331, 30005203, 30002661, 30002659,
    };

    [Fact]
    public void GateRoute_MatchesEsiShorter_JitaToAmarr()
    {
        if (!File.Exists(AppPaths.SdeSqlitePath)) return;

        var map = new SdeRepository(AppPaths.SdeSqlitePath).BuildUniverseMap();
        var route = GatePathfinder.FindRoute(map, EsiJitaToAmarrShorter[0], EsiJitaToAmarrShorter[^1],
            new RouteFilterOptions { Preference = GateRoutePreference.Shorter });

        Assert.NotNull(route);
        Assert.Equal(EsiJitaToAmarrShorter.Length - 1, route!.JumpCount);
        Assert.Equal(EsiJitaToAmarrShorter, route.SystemIds);
    }

    [Fact]
    public void GateRoute_MatchesEsiShorter_JitaToDodixie()
    {
        if (!File.Exists(AppPaths.SdeSqlitePath)) return;

        var map = new SdeRepository(AppPaths.SdeSqlitePath).BuildUniverseMap();
        var route = GatePathfinder.FindRoute(map, EsiJitaToDodixieShorter[0], EsiJitaToDodixieShorter[^1],
            new RouteFilterOptions { Preference = GateRoutePreference.Shorter });

        Assert.NotNull(route);
        Assert.Equal(EsiJitaToDodixieShorter.Length - 1, route!.JumpCount);
        Assert.Equal(EsiJitaToDodixieShorter, route.SystemIds);
    }
}
