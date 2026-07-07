namespace EvEMapEnhanced.Core.Jump;

/// <summary>
/// Editable per-pilot skill levels affecting jump mechanics. All levels are clamped to 0-5.
/// Mutable by design so the UI can bind numeric editors directly to these properties.
/// </summary>
public sealed class PilotSkills
{
    private int _jumpDriveCalibration;
    private int _jumpFuelConservation;
    private int _jumpFreighters;
    private int _capitalShips;
    private int _blackOps;

    /// <summary>+20% jump range per level (0-5). Level 5 doubles base range.</summary>
    public int JumpDriveCalibration { get => _jumpDriveCalibration; set => _jumpDriveCalibration = Clamp(value); }

    /// <summary>-10% isotope consumption per level (0-5). Level 5 halves fuel use.</summary>
    public int JumpFuelConservation { get => _jumpFuelConservation; set => _jumpFuelConservation = Clamp(value); }

    /// <summary>Additional -10% fuel per level, applies only to Jump Freighter hulls.</summary>
    public int JumpFreighters { get => _jumpFreighters; set => _jumpFreighters = Clamp(value); }

    /// <summary>Required to fly capitals at all; kept for profile completeness / UI display.</summary>
    public int CapitalShips { get => _capitalShips; set => _capitalShips = Clamp(value); }

    /// <summary>Required to fly Black Ops covert jumps; kept for profile completeness / UI display.</summary>
    public int BlackOps { get => _blackOps; set => _blackOps = Clamp(value); }

    /// <summary>Optional Jump Drive Economizer module tier fitted (additional fuel reduction).</summary>
    public JumpDriveEconomizerTier Economizer { get; set; } = JumpDriveEconomizerTier.None;

    private static int Clamp(int value) => Math.Clamp(value, 0, 5);

    public static PilotSkills MaxSkills() => new()
    {
        JumpDriveCalibration = 5,
        JumpFuelConservation = 5,
        JumpFreighters = 5,
        CapitalShips = 5,
        BlackOps = 5,
    };
}

/// <summary>
/// Jump Drive Economizer rig/module tiers. Fuel reduction values are the commonly
/// documented tier bonuses (T1/T2/T3); stack additively with skill-based reductions
/// per the community-verified fuel formula.
/// </summary>
public enum JumpDriveEconomizerTier
{
    None,
    T1,
    T2,
    T3,
}

public static class JumpDriveEconomizerExtensions
{
    public static double FuelReductionFraction(this JumpDriveEconomizerTier tier) => tier switch
    {
        JumpDriveEconomizerTier.T1 => 0.04,
        JumpDriveEconomizerTier.T2 => 0.07,
        JumpDriveEconomizerTier.T3 => 0.10,
        _ => 0.0,
    };
}
