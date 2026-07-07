using EvEMapEnhanced.Core.Ships;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Ships;

public class ShipRegistryTests
{
    [Theory]
    [InlineData(CapitalShipClass.Carrier)]
    [InlineData(CapitalShipClass.ForceAuxiliary)]
    [InlineData(CapitalShipClass.CommandCarrier)]
    [InlineData(CapitalShipClass.Dreadnought)]
    [InlineData(CapitalShipClass.LancerDreadnought)]
    [InlineData(CapitalShipClass.BlackOps)]
    [InlineData(CapitalShipClass.Supercarrier)]
    [InlineData(CapitalShipClass.Titan)]
    [InlineData(CapitalShipClass.JumpFreighter)]
    [InlineData(CapitalShipClass.Rorqual)]
    public void EveryShipClass_HasAJumpMechanicsProfile(CapitalShipClass shipClass)
    {
        var profile = JumpMechanics.Get(shipClass);
        Assert.True(profile.BaseRangeLy > 0);
        Assert.Equal(profile.BaseRangeLy * 2, profile.MaxRangeLy);
    }

    [Fact]
    public void AllSeedHulls_BelongToKnownFactionsOrNone()
    {
        Assert.NotEmpty(ShipHulls.All);
        Assert.All(ShipHulls.All, h => Assert.True(h.MassKg > 0 && h.BaseFuelPerLyIsotopes > 0));
    }

    [Fact]
    public void FindByName_IsCaseInsensitive()
    {
        Assert.NotNull(ShipHulls.FindByName("archon"));
        Assert.NotNull(ShipHulls.FindByName("ARCHON"));
        Assert.Null(ShipHulls.FindByName("NotAShip"));
    }

    [Theory]
    [InlineData(0, 3.5)]
    [InlineData(5, 7.0)]
    public void RangeAtSkillLevel_MatchesDocumentedFormula(int jdc, double expected)
    {
        var profile = JumpMechanics.Get(CapitalShipClass.Carrier);
        Assert.Equal(expected, profile.RangeAtSkillLevel(jdc), precision: 6);
    }
}
