using EvEMapEnhanced.Core.Models;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Models;

public class SpaceMathTests
{
    [Fact]
    public void LightYearsToMeters_AndBack_RoundTrips()
    {
        double meters = SpaceMath.LightYearsToMeters(5.0);
        double lightYears = SpaceMath.MetersToLightYears(meters);
        Assert.Equal(5.0, lightYears, precision: 9);
    }

    [Fact]
    public void Distance_ComputesEuclideanDistance()
    {
        double dist = SpaceMath.Distance(0, 0, 0, 3, 4, 0);
        Assert.Equal(5.0, dist, precision: 9);
    }

    [Theory]
    [InlineData(0.9, false, false, true)]
    [InlineData(0.3, false, true, false)]
    [InlineData(0.0, true, false, false)]
    [InlineData(-0.5, true, false, false)]
    public void SecurityClassification_MatchesEveConventions(double security, bool expectedNull, bool expectedLow, bool expectedHigh)
    {
        var system = new SolarSystem(1, "Test", 1, 1, security, 0, 0, 0);
        Assert.Equal(expectedNull, system.IsNullSec);
        Assert.Equal(expectedLow, system.IsLowSec);
        Assert.Equal(expectedHigh, system.IsHighSec);
    }

    [Fact]
    public void DistanceLyTo_MatchesManualCalculation()
    {
        var a = new SolarSystem(1, "A", 1, 1, 0.9, 0, 0, 0);
        var b = new SolarSystem(2, "B", 1, 1, 0.9, SpaceMath.LightYearsToMeters(3), 0, 0);
        Assert.Equal(3.0, a.DistanceLyTo(b), precision: 6);
    }
}
