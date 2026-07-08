using EvEMapEnhanced.Desktop;

namespace EvEMapEnhanced.Desktop.Tests;

public class SchematicPlateLayoutPolicyTests
{
    [Theory]
    [InlineData(3.0, true, SchematicPlateDetailTier.Dot)]
    [InlineData(10.0, true, SchematicPlateDetailTier.Dot)]
    [InlineData(16.99, true, SchematicPlateDetailTier.Dot)]
    [InlineData(17.0, true, SchematicPlateDetailTier.Compact)]
    [InlineData(20.0, true, SchematicPlateDetailTier.Compact)]
    [InlineData(20.99, true, SchematicPlateDetailTier.Compact)]
    [InlineData(21.0, true, SchematicPlateDetailTier.Full)]
    [InlineData(27.94, true, SchematicPlateDetailTier.Full)]
    public void ResolveTier_UsesZoomThresholds(double zoom, bool showNpcKillLabels, SchematicPlateDetailTier expected)
    {
        Assert.Equal(expected, SchematicPlateLayoutPolicy.ResolveTier(zoom, showNpcKillLabels));
    }

    [Fact]
    public void ResolveTier_AboveCompactBandWithoutNpcLabels_StaysCompact()
    {
        Assert.Equal(
            SchematicPlateDetailTier.Compact,
            SchematicPlateLayoutPolicy.ResolveTier(27.0, showNpcKillLabels: false));
    }

    [Theory]
    [InlineData(16.99, SchematicPlateDetailTier.Dot)]
    [InlineData(17.0, SchematicPlateDetailTier.Compact)]
    [InlineData(20.99, SchematicPlateDetailTier.Compact)]
    [InlineData(21.0, SchematicPlateDetailTier.Full)]
    public void ResolveTier_UsesExactZoomBoundaries(double zoom, SchematicPlateDetailTier expected)
    {
        Assert.Equal(expected, SchematicPlateLayoutPolicy.ResolveTier(zoom, showNpcKillLabels: true));
    }

    [Theory]
    [InlineData(10.0, SchematicPlateDetailTier.Dot)]
    [InlineData(20.0, SchematicPlateDetailTier.Compact)]
    [InlineData(27.0, SchematicPlateDetailTier.Full)]
    public void ComputeTargetPlateScale_UsesTierSpecificGrowth(double zoom, SchematicPlateDetailTier tier)
    {
        double scale = SchematicPlateLayoutPolicy.ComputeTargetPlateScale(tier, zoom);
        Assert.InRange(scale, SchematicPlateLayoutPolicy.PlateMinScale, SchematicPlateLayoutPolicy.PlateCompactCloseMaxScale);
    }

    [Fact]
    public void ComputeTargetPlateScale_FullGrowsLinearlyPastDefaultZoom()
    {
        // Below the (now lower) cap, full-tier scale still grows with zoom; far past it, it saturates.
        double atSeven = SchematicPlateLayoutPolicy.ComputeTargetPlateScale(
            SchematicPlateDetailTier.Full, 7.0);
        double atFive = SchematicPlateLayoutPolicy.ComputeTargetPlateScale(
            SchematicPlateDetailTier.Full, 5.0);

        Assert.True(atSeven > atFive);
        Assert.Equal(
            SchematicPlateLayoutPolicy.PlateFullCloseMaxScale,
            SchematicPlateLayoutPolicy.ComputeTargetPlateScale(SchematicPlateDetailTier.Full, 25.0),
            precision: 5);
    }

    [Fact]
    public void ComputeTargetPlateScale_CompactUsesSqrtGrowthInDetailBand()
    {
        double atTwenty = SchematicPlateLayoutPolicy.ComputeTargetPlateScale(
            SchematicPlateDetailTier.Compact, 20.0);
        double linear = SchematicPlateLayoutPolicy.ZoomRatio(20.0);

        Assert.True(atTwenty < linear);
        Assert.Equal(
            SchematicPlateLayoutPolicy.PlateCompactCloseMaxScale,
            SchematicPlateLayoutPolicy.ComputeTargetPlateScale(SchematicPlateDetailTier.Compact, 50.0),
            precision: 5);
    }

    [Fact]
    public void ComputeTargetPlateScale_AppliesWideOverviewHighlightShrink()
    {
        double normal = SchematicPlateLayoutPolicy.ComputeTargetPlateScale(
            SchematicPlateDetailTier.Compact, 3.0, wideZoomHighlightScale: 1.0);
        double shrunk = SchematicPlateLayoutPolicy.ComputeTargetPlateScale(
            SchematicPlateDetailTier.Compact, 3.0, wideZoomHighlightScale: 0.5);

        Assert.Equal(normal * 0.5, shrunk, precision: 5);
    }

    [Fact]
    public void ShrinkUntilFits_StopsAtFirstScaleThatFits()
    {
        var tried = new List<double>();
        double result = SchematicPlateLayoutPolicy.ShrinkUntilFits(
            startScale: 2.0,
            fitsAtScale: scale =>
            {
                tried.Add(scale);
                return scale <= 1.84;
            });

        Assert.Equal(1.84, result, precision: 2);
        Assert.Equal(3, tried.Count);
        Assert.True(tried[0] >= tried[^1]);
    }

    [Fact]
    public void ShrinkUntilFits_NeverDropsBelowMinimum()
    {
        double result = SchematicPlateLayoutPolicy.ShrinkUntilFits(
            startScale: 1.0,
            fitsAtScale: _ => false);

        Assert.Equal(SchematicPlateLayoutPolicy.PlateMinScale, result);
    }
}
