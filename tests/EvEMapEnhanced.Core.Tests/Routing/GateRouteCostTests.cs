using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Routing;

public class GateRouteCostTests
{
    private static SolarSystem Sys(double sec) => new(1, "Test", 1, 1, sec, 0, 0, 0);

    [Fact]
    public void ShorterMode_UsesUnitCost_RegardlessOfSecurity()
    {
        var options = new RouteFilterOptions { Preference = GateRoutePreference.Shorter };
        Assert.Equal(1.0, GatePathfinder.GateEdgeCost(Sys(0.9), options), 3);
        Assert.Equal(1.0, GatePathfinder.GateEdgeCost(Sys(0.1), options), 3);
    }

    [Fact]
    public void SaferMode_SlightlyPenalizesLowSec_AndRewardsHighSec()
    {
        var options = new RouteFilterOptions { Preference = GateRoutePreference.Safer };
        double hi = GatePathfinder.GateEdgeCost(Sys(0.9), options);
        double low = GatePathfinder.GateEdgeCost(Sys(0.3), options);
        Assert.True(hi < 1.0);
        Assert.True(low > 1.0);
        Assert.True(low - hi < 0.5); // soft bias, not exponential detour
    }

    [Fact]
    public void LessSecureMode_PrefersLowSec()
    {
        var options = new RouteFilterOptions { Preference = GateRoutePreference.LessSecure };
        double hi = GatePathfinder.GateEdgeCost(Sys(0.9), options);
        double low = GatePathfinder.GateEdgeCost(Sys(0.3), options);
        Assert.True(hi > low);
    }
}
