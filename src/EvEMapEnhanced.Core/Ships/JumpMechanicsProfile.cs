namespace EvEMapEnhanced.Core.Ships;

/// <summary>
/// Per-class jump drive mechanics: range and fatigue behaviour.
/// Range figures come from documented CCP game mechanics (base range at
/// Jump Drive Calibration 0, +20%/level up to JDC V = double range).
/// Fuel base consumption is hull-specific seed data (see <see cref="ShipHull"/>)
/// and should be recalibrated against a current SDE/game client dump before being
/// relied on for real fleet logistics -- values here are reasonable approximations
/// for the routing engine to be functionally complete and testable.
/// </summary>
public sealed record JumpMechanicsProfile(
    CapitalShipClass ShipClass,
    double BaseRangeLy,
    /// <summary>Fraction of light-years travelled that counts toward fatigue accrual for a
    /// standard cyno/beacon/jump-bridge jump (1.0 = full distance counts).</summary>
    double FatigueMultiplierStandard,
    /// <summary>Fatigue multiplier when travelling via covert cyno / black-ops jump portal.</summary>
    double FatigueMultiplierCovert)
{
    /// <summary>Maximum jump range with Jump Drive Calibration V (base * (1 + 0.2*5) = base * 2).</summary>
    public double MaxRangeLy => BaseRangeLy * 2.0;

    public double RangeAtSkillLevel(int jumpDriveCalibrationLevel)
    {
        int level = Math.Clamp(jumpDriveCalibrationLevel, 0, 5);
        return BaseRangeLy * (1.0 + 0.2 * level);
    }
}

public static class JumpMechanics
{
    /// <summary>
    /// Registry of jump mechanics per capital class, seeded from documented CCP mechanics:
    /// - Jump Freighters / Rorqual: 5.0 -> 10.0 LY, 90% fatigue reduction (0.10 effective multiplier).
    /// - Black Ops: 4.0 -> 8.0 LY, 50% fatigue reduction on both standard and covert jumps.
    /// - Carrier / FAX / Command Carrier / Dreadnought: 3.5 -> 7.0 LY, no fatigue reduction.
    /// - Lancer Dreadnought: 4.0 -> 8.0 LY, no fatigue reduction.
    /// - Supercarrier / Titan: 3.0 -> 6.0 LY, no fatigue reduction.
    /// </summary>
    public static readonly IReadOnlyDictionary<CapitalShipClass, JumpMechanicsProfile> Profiles =
        new Dictionary<CapitalShipClass, JumpMechanicsProfile>
        {
            [CapitalShipClass.Carrier] = new(CapitalShipClass.Carrier, 3.5, 1.0, 1.0),
            [CapitalShipClass.ForceAuxiliary] = new(CapitalShipClass.ForceAuxiliary, 3.5, 1.0, 1.0),
            [CapitalShipClass.CommandCarrier] = new(CapitalShipClass.CommandCarrier, 3.5, 1.0, 1.0),
            [CapitalShipClass.Dreadnought] = new(CapitalShipClass.Dreadnought, 3.5, 1.0, 1.0),
            [CapitalShipClass.LancerDreadnought] = new(CapitalShipClass.LancerDreadnought, 4.0, 1.0, 1.0),
            [CapitalShipClass.BlackOps] = new(CapitalShipClass.BlackOps, 4.0, 0.5, 0.5),
            [CapitalShipClass.Supercarrier] = new(CapitalShipClass.Supercarrier, 3.0, 1.0, 1.0),
            [CapitalShipClass.Titan] = new(CapitalShipClass.Titan, 3.0, 1.0, 1.0),
            [CapitalShipClass.JumpFreighter] = new(CapitalShipClass.JumpFreighter, 5.0, 0.10, 1.0),
            [CapitalShipClass.Rorqual] = new(CapitalShipClass.Rorqual, 5.0, 0.10, 1.0),
        };

    public static JumpMechanicsProfile Get(CapitalShipClass shipClass) => Profiles[shipClass];
}
