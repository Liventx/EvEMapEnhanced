namespace EvEMapEnhanced.Core.Models;

/// <summary>
/// Geometry helpers for working with SDE coordinates (given in meters).
/// </summary>
public static class SpaceMath
{
    /// <summary>1 light year in meters, as used by the EVE client/SDE.</summary>
    public const double MetersPerLightYear = 9.4607e15;

    public static double Distance(double x1, double y1, double z1, double x2, double y2, double z2)
    {
        double dx = x2 - x1, dy = y2 - y1, dz = z2 - z1;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public static double MetersToLightYears(double meters) => meters / MetersPerLightYear;

    public static double LightYearsToMeters(double lightYears) => lightYears * MetersPerLightYear;
}
