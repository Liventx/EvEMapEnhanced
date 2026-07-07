using EvEMapEnhanced.Core.Ships;

namespace EvEMapEnhanced.Core.Jump;

/// <summary>
/// Implements CCP's jump fatigue / jump activation cooldown mechanic exactly as documented
/// in the official support article and the Phoebe travel change devblog:
///
///   effectiveLy   = distanceLy * fatigueMultiplier(shipClass, jumpMethod)
///   cooldownMin   = max(currentFatigueMin / 10, 1 + effectiveLy)
///   newFatigueMin = min(300, max(currentFatigueMin, 10) * (1 + effectiveLy))
///
/// Fatigue is capped at 5 hours (300 minutes); cooldown at 30 minutes is a client-side
/// display convention only -- the formula itself does not need a separate cap because
/// fatigue is capped and cooldown derives from it, matching community-verified calculators.
/// </summary>
public static class FatigueCalculator
{
    public const double MaxFatigueMinutes = 300.0;

    public static double EffectiveLightYears(CapitalShipClass shipClass, JumpMethod method, double distanceLy)
    {
        var profile = JumpMechanics.Get(shipClass);
        double multiplier = method.IsCovert() ? profile.FatigueMultiplierCovert : profile.FatigueMultiplierStandard;
        return distanceLy * multiplier;
    }

    public static double CooldownMinutes(double currentFatigueMinutes, double effectiveLightYears)
        => Math.Max(currentFatigueMinutes / 10.0, 1.0 + effectiveLightYears);

    public static double NextFatigueMinutes(double currentFatigueMinutes, double effectiveLightYears)
        => Math.Min(MaxFatigueMinutes, Math.Max(currentFatigueMinutes, 10.0) * (1.0 + effectiveLightYears));
}
