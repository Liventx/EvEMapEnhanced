using Avalonia;
using EvEMapEnhanced.Core.Models;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// Shared top-down (X/Z) projection of a solar system's real SDE coordinates into light
/// years, used as the common ground truth for both display modes: "Standard" uses it
/// directly, "Schematic" (Dotlan-styled) uses it as the seed for its de-overlap layout.
/// Z is negated because the game's own 2D star map has north "up" on screen, which is the
/// opposite of the raw SDE Z axis.
/// </summary>
internal static class WorldProjection
{
    public static Point RealPosition(SolarSystem system) =>
        new(SpaceMath.MetersToLightYears(system.X), -SpaceMath.MetersToLightYears(system.Z));
}
