using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Ships;

namespace EvEMapEnhanced.Core.Routing;

public sealed record SimulatedLeg(JumpRouteLeg Leg, JumpResult Result);

public sealed record RouteSimulationResult(IReadOnlyList<SimulatedLeg> Legs)
{
    public double TotalFuel => Legs.Sum(l => l.Result.IsotopesUsed);
    public double PeakFatigueMinutes => Legs.Count == 0 ? 0 : Legs.Max(l => l.Result.FatigueAfterMinutes);
    public double TotalCooldownMinutes => Legs.Sum(l => l.Result.CooldownMinutes);
    public bool AnyLegOutOfRange => Legs.Any(l => !l.Result.WithinRange);
}

/// <summary>Applies the jump fatigue/fuel simulator across an entire jump route, in order.</summary>
public static class RouteSimulator
{
    public static RouteSimulationResult SimulateJumpRoute(JumpRoute route, ShipHull hull, PilotSkills skills, JumpState? initialState = null)
    {
        var state = initialState ?? JumpState.Fresh();
        var results = new List<SimulatedLeg>(route.Legs.Count);

        foreach (var leg in route.Legs)
        {
            var result = JumpSimulator.SimulateJump(hull, skills, leg.Method, leg.DistanceLy, state);
            results.Add(new SimulatedLeg(leg, result));
        }

        return new RouteSimulationResult(results);
    }
}
