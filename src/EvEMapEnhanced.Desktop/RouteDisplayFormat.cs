using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;

namespace EvEMapEnhanced.Desktop;

internal static class RouteDisplayFormat
{
    public static string SystemName(SolarSystem system) =>
        $"{system.Name} ({system.Security:F1})";

    public static string JumpMethodLabel(JumpMethod? method) => method switch
    {
        JumpMethod.CovertCyno => "covert cyno",
        JumpMethod.JumpBridge => "jump bridge",
        _ => "cyno",
    };
}
