using EvEMapEnhanced.Core.Jump;

namespace EvEMapEnhanced.Core.Routing;

public enum RouteStepKind { Gate, Jump, Wormhole }

/// <summary>A single unified step of any composite route (gate hop or capital jump leg).</summary>
public sealed record RouteStep(int FromSystemId, int ToSystemId, RouteStepKind Kind, double? DistanceLy = null, JumpMethod? Method = null);

public static class RouteStepConversions
{
    public static IEnumerable<RouteStep> ToSteps(this GateRoute route)
    {
        for (int i = 0; i < route.SystemIds.Count - 1; i++)
        {
            int from = route.SystemIds[i];
            int to = route.SystemIds[i + 1];
            var kind = route.IsWormholeHop(from, to) ? RouteStepKind.Wormhole : RouteStepKind.Gate;
            yield return new RouteStep(from, to, kind);
        }
    }

    public static IEnumerable<RouteStep> ToSteps(this JumpRoute route) =>
        route.Legs.Select(l => new RouteStep(l.FromSystemId, l.ToSystemId, RouteStepKind.Jump, l.DistanceLy, l.Method));
}
