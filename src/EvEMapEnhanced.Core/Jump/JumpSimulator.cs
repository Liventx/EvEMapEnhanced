using EvEMapEnhanced.Core.Ships;

namespace EvEMapEnhanced.Core.Jump;

/// <summary>Outcome of a single simulated jump.</summary>
public sealed record JumpResult(
    double DistanceLy,
    double IsotopesUsed,
    double CooldownMinutes,
    double FatigueBeforeMinutes,
    double FatigueAfterMinutes,
    bool WithinRange);

/// <summary>Mutable jump state carried across a chain of jumps (fatigue accumulates).</summary>
public sealed class JumpState
{
    public double CurrentFatigueMinutes { get; set; }

    public static JumpState Fresh() => new() { CurrentFatigueMinutes = 0 };
}

/// <summary>
/// Combines range, fuel, and fatigue calculations into a single per-jump and
/// per-route simulation API.
/// </summary>
public static class JumpSimulator
{
    /// <summary>Maximum jump range in LY for this hull at the pilot's current JDC skill level.</summary>
    public static double MaxRangeLy(ShipHull hull, PilotSkills skills)
        => hull.Mechanics.RangeAtSkillLevel(skills.JumpDriveCalibration);

    /// <summary>
    /// Maximum jump range in LY for a whole capital class (Dotlan-style "Jump Range" tool):
    /// range depends only on class + JDC skill, not on the specific hull within that class.
    /// </summary>
    public static double MaxRangeLy(CapitalShipClass shipClass, PilotSkills skills)
        => JumpMechanics.Get(shipClass).RangeAtSkillLevel(skills.JumpDriveCalibration);

    public static JumpResult SimulateJump(ShipHull hull, PilotSkills skills, JumpMethod method, double distanceLy, JumpState state)
    {
        double maxRange = MaxRangeLy(hull, skills);
        bool withinRange = distanceLy <= maxRange + 1e-9;

        double effectiveLy = FatigueCalculator.EffectiveLightYears(hull.ShipClass, method, distanceLy);
        double fatigueBefore = state.CurrentFatigueMinutes;
        double cooldown = FatigueCalculator.CooldownMinutes(fatigueBefore, effectiveLy);
        double fatigueAfter = FatigueCalculator.NextFatigueMinutes(fatigueBefore, effectiveLy);
        double fuel = FuelCalculator.IsotopesForJump(hull, skills, distanceLy);

        state.CurrentFatigueMinutes = fatigueAfter;

        return new JumpResult(distanceLy, fuel, cooldown, fatigueBefore, fatigueAfter, withinRange);
    }
}
