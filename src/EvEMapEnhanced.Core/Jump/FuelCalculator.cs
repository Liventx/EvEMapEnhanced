using EvEMapEnhanced.Core.Ships;

namespace EvEMapEnhanced.Core.Jump;

/// <summary>
/// Implements the community-verified jump drive isotope consumption formula:
///
///   isotopes = D * F * (1 - 0.1*JFC) * (1 - 0.1*JF_if_applicable) * (1 - economizerBonus)
///
/// where D is distance in light years, F is the hull's base fuel-per-LY, JFC is the
/// Jump Fuel Conservation skill level (0-5), and JF is the Jump Freighters skill level
/// (0-5, only applies to Jump Freighter hulls).
/// </summary>
public static class FuelCalculator
{
    public static double IsotopesForJump(ShipHull hull, PilotSkills skills, double distanceLy)
    {
        double fuel = distanceLy * hull.BaseFuelPerLyIsotopes;
        fuel *= 1.0 - 0.1 * skills.JumpFuelConservation;

        if (hull.ShipClass == CapitalShipClass.JumpFreighter)
        {
            fuel *= 1.0 - 0.1 * skills.JumpFreighters;
        }

        fuel *= 1.0 - skills.Economizer.FuelReductionFraction();

        return Math.Max(0, fuel);
    }
}
