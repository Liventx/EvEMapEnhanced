namespace EvEMapEnhanced.Core.Models;

/// <summary>
/// Static solar system data as imported from the EVE SDE.
/// Coordinates are in meters, matching the raw SDE scale.
/// </summary>
public sealed record SolarSystem(
    int Id,
    string Name,
    int ConstellationId,
    int RegionId,
    double Security,
    double X,
    double Y,
    double Z)
{
    /// <summary>True when this system is in null security space (security &lt;= 0.0, rounded).</summary>
    public bool IsNullSec => Math.Round(Security, 1) <= 0.0;

    /// <summary>True when this system is low security (0.0 &lt; sec &lt; 0.5).</summary>
    public bool IsLowSec => Security is > 0.0 and < 0.45;

    /// <summary>True when this system is high security (sec &gt;= 0.45, EVE rounds 0.45+ up to 0.5 display).</summary>
    public bool IsHighSec => Security >= 0.45;

    public double DistanceLyTo(SolarSystem other) => SpaceMath.MetersToLightYears(SpaceMath.Distance(X, Y, Z, other.X, other.Y, other.Z));
}
