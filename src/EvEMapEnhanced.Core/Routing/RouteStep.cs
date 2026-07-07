using EvEMapEnhanced.Core.Jump;

namespace EvEMapEnhanced.Core.Routing;

public enum RouteStepKind { Gate, Jump }

/// <summary>A single unified step of any composite route (gate hop or capital jump leg).</summary>
public sealed record RouteStep(int FromSystemId, int ToSystemId, RouteStepKind Kind, double? DistanceLy = null, JumpMethod? Method = null);

public static class RouteStepConversions
{
    public static IEnumerable<RouteStep> ToSteps(this GateRoute route)
    {
        for (int i = 0; i < route.SystemIds.Count - 1; i++)
        {
            yield return new RouteStep(route.SystemIds[i], route.SystemIds[i + 1], RouteStepKind.Gate);
        }
    }

    public static IEnumerable<RouteStep> ToSteps(this JumpRoute route) =>
        route.Legs.Select(l => new RouteStep(l.FromSystemId, l.ToSystemId, RouteStepKind.Jump, l.DistanceLy, l.Method));
}
