using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Ships;

namespace EvEMapEnhanced.Core.Routing;

public sealed record CombinedRoute(IReadOnlyList<RouteStep> Steps)
{
    public int GateJumps => Steps.Count(s => s.Kind == RouteStepKind.Gate);
    public int CapitalJumps => Steps.Count(s => s.Kind == RouteStepKind.Jump);
}

/// <summary>
/// Builds a combined gate + capital-jump route. Evaluates three candidate strategies
/// and returns the one with the fewest total steps:
///
///  A) Pure stargate route (no jump drive used).
///  B) Pure capital jump chain (no gates used), when the destination is reachable
///     within <see cref="JumpPathfinder"/>'s hop limit.
///  C) Gate to the nearest-to-destination system that lies within the ship's current
///     jump range, then a short jump chain to close the remaining distance.
///
/// This is a pragmatic heuristic rather than an exhaustive optimal search over all
/// possible gate/jump interleavings, which is sufficient for real-world route planning
/// where the useful hybrid pattern is "gate most of the way, jump the unreachable tail".
/// </summary>
public static class HybridRouter
{
    public static CombinedRoute? FindRoute(
        UniverseMap map,
        ShipHull hull,
        PilotSkills skills,
        int fromSystemId,
        int toSystemId,
        JumpMethod method,
        RouteFilterOptions? options = null,
        int candidateLandingSystems = 12)
    {
        options ??= new RouteFilterOptions();
        var candidates = new List<CombinedRoute>();

        // Strategy A: pure gate route.
        var gateRoute = GatePathfinder.FindRoute(map, fromSystemId, toSystemId, options);
        if (gateRoute is not null)
        {
            candidates.Add(new CombinedRoute(gateRoute.ToSteps().ToList()));
        }

        // Strategy B: pure jump chain.
        var jumpRoute = JumpPathfinder.FindRoute(map, hull, skills, fromSystemId, toSystemId, method, options);
        if (jumpRoute is not null)
        {
            candidates.Add(new CombinedRoute(jumpRoute.ToSteps().ToList()));
        }

        // Strategy B2: pure jump-bridge chain (Ansiblex network), if the user has one entered.
        var bridgeRoute = JumpBridgePathfinder.FindRoute(map, fromSystemId, toSystemId, options);
        if (bridgeRoute is not null)
        {
            candidates.Add(new CombinedRoute(bridgeRoute.ToSteps().ToList()));
        }

        // Strategy C: gate to a landing system within jump range of destination, then jump the rest.
        var destination = map.Get(toSystemId);
        if (destination is not null)
        {
            double rangeLy = JumpSimulator.MaxRangeLy(hull, skills);
            var landingCandidates = map.SystemsWithinRange(destination, rangeLy)
                .OrderBy(c => c.DistanceLy)
                .Take(candidateLandingSystems)
                .Select(c => c.System.Id)
                .ToList();

            CombinedRoute? best = null;
            foreach (int landingId in landingCandidates)
            {
                if (landingId == fromSystemId) continue;

                var gateLeg = GatePathfinder.FindRoute(map, fromSystemId, landingId, options);
                if (gateLeg is null) continue;

                var jumpLeg = JumpPathfinder.FindRoute(map, hull, skills, landingId, toSystemId, method, options, maxHops: 4);
                if (jumpLeg is null) continue;

                var combined = new CombinedRoute(gateLeg.ToSteps().Concat(jumpLeg.ToSteps()).ToList());
                if (best is null || combined.GateJumps + combined.CapitalJumps < best.GateJumps + best.CapitalJumps)
                {
                    best = combined;
                }
            }

            if (best is not null) candidates.Add(best);
        }

        return candidates.OrderBy(c => c.GateJumps + c.CapitalJumps).FirstOrDefault();
    }
}
