using System;

namespace EvEMapEnhanced.Desktop;

/// <summary>Schematic map system-plate detail level, ordered from most to least detailed.</summary>
public enum SchematicPlateDetailTier
{
    Full,
    Compact,
    Dot
}

/// <summary>
/// Zoom-driven schematic plate tier selection and target scale computation. Extracted for unit
/// tests so map-rendering thresholds stay stable across refactors.
/// </summary>
public static class SchematicPlateLayoutPolicy
{
    public const double DefaultSchematicZoom = 3.0;

    /// <summary>Below this zoom every visible system uses the dot tier.</summary>
    public const double DotTierMaxZoom = 17.0;

    /// <summary>From <see cref="DotTierMaxZoom"/> up to (but not including) this zoom, compact plates are used.</summary>
    public const double CompactTierMaxZoom = 23.0;

    public const double PlateMinScale = 0.5;
    public const double PlateOverviewMaxScale = 1.8;
    public const double PlateCompactCloseMaxScale = 3.5;
    public const double PlateFullCloseMaxScale = 5.0;
    public const double ShrinkStep = 0.08;

    public static double ZoomRatio(double zoom) => zoom / DefaultSchematicZoom;

    /// <summary>
    /// Picks one detail tier for every visible system from zoom alone so panning between regions
    /// at the same scale always shows the same tier.
    /// </summary>
    public static SchematicPlateDetailTier ResolveTier(double zoom, bool showNpcKillLabels)
    {
        if (zoom < DotTierMaxZoom)
            return SchematicPlateDetailTier.Dot;
        if (zoom < CompactTierMaxZoom)
            return SchematicPlateDetailTier.Compact;
        return showNpcKillLabels ? SchematicPlateDetailTier.Full : SchematicPlateDetailTier.Compact;
    }

    public static double ComputeTargetPlateScale(
        SchematicPlateDetailTier tier,
        double zoom,
        double wideZoomHighlightScale = 1.0)
    {
        double raw = ZoomRatio(zoom);
        double scale = tier switch
        {
            SchematicPlateDetailTier.Full => zoom <= DefaultSchematicZoom
                ? Math.Clamp(raw, PlateMinScale, PlateOverviewMaxScale)
                : Math.Clamp(Math.Min(PlateFullCloseMaxScale, raw), PlateMinScale, PlateFullCloseMaxScale),
            SchematicPlateDetailTier.Compact => zoom <= DefaultSchematicZoom
                ? Math.Clamp(raw, PlateMinScale, PlateOverviewMaxScale)
                : Math.Clamp(Math.Min(PlateCompactCloseMaxScale, Math.Sqrt(raw)), PlateMinScale, PlateCompactCloseMaxScale),
            _ => Math.Clamp(raw, PlateMinScale, PlateOverviewMaxScale)
        };

        return wideZoomHighlightScale < 1.0 ? scale * wideZoomHighlightScale : scale;
    }

    /// <summary>Reduces scale in fixed steps until <paramref name="fitsAtScale"/> returns true.</summary>
    public static double ShrinkUntilFits(
        double startScale,
        Func<double, bool> fitsAtScale,
        double minScale = PlateMinScale,
        double step = ShrinkStep)
    {
        double scale = startScale;
        while (scale > minScale && !fitsAtScale(scale))
            scale = Math.Max(minScale, scale - step);
        return scale;
    }
}
