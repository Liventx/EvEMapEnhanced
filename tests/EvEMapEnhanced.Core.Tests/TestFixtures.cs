using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;

namespace EvEMapEnhanced.Core.Tests;

/// <summary>Small, deterministic in-memory universes used across routing tests.</summary>
internal static class TestFixtures
{
    private static double Ly(double lightYears) => SpaceMath.LightYearsToMeters(lightYears);

    /// <summary>
    /// A 5-system linear chain, gate-connected only in sequence:
    /// Alpha(0.9) - Bravo(0.8) - Charlie(0.3, low) - Delta(0.0, null) - Echo(0.3, low).
    /// Also includes a disconnected-by-gate "Zulu" system reachable only if avoidance forces a detour test.
    /// </summary>
    public static UniverseMap BuildLinearGateMap()
    {
        var systems = new[]
        {
            new SolarSystem(1, "Alpha", 1, 1, 0.9, Ly(0), 0, 0),
            new SolarSystem(2, "Bravo", 1, 1, 0.8, Ly(1), 0, 0),
            new SolarSystem(3, "Charlie", 1, 1, 0.3, Ly(2), 0, 0),
            new SolarSystem(4, "Delta", 1, 1, 0.0, Ly(3), 0, 0),
            new SolarSystem(5, "Echo", 1, 1, 0.3, Ly(4), 0, 0),
        };
        var gates = new[]
        {
            new Stargate(1, 2),
            new Stargate(2, 3),
            new Stargate(3, 4),
            new Stargate(4, 5),
        };
        return new UniverseMap(systems, gates);
    }

    /// <summary>
    /// Systems spaced 2 LY apart along a line, with NO stargates at all -- used to exercise
    /// pure jump-drive routing and range queries independent of the gate graph.
    /// System "Jammed" sits at the midpoint and is flagged as cyno-jammed by the caller.
    /// </summary>
    public static UniverseMap BuildJumpOnlyMap()
    {
        var systems = new[]
        {
            new SolarSystem(101, "J0", 1, 1, 0.0, Ly(0), 0, 0),
            new SolarSystem(102, "J1", 1, 1, 0.0, Ly(2), 0, 0),
            new SolarSystem(103, "J2", 1, 1, 0.0, Ly(4), 0, 0),
            new SolarSystem(104, "J3", 1, 1, 0.0, Ly(6), 0, 0),
            new SolarSystem(105, "Jammed", 1, 1, 0.0, Ly(2), Ly(1), 0),
        };
        return new UniverseMap(systems, Array.Empty<Stargate>());
    }

    /// <summary>
    /// Start -- 3 LY -- Relay -- 3 LY -- End, with Start-End direct distance = 6 LY.
    /// A Black Ops hull at base range (4.0 LY, JDC 0) can reach Relay but not jump
    /// directly to End -- used to test that a cyno jammer at Relay blocks standard
    /// cyno chains but not covert cyno chains.
    /// </summary>
    public static (UniverseMap Map, int StartId, int RelayId, int EndId) BuildCynoJammerMap()
    {
        var systems = new[]
        {
            new SolarSystem(201, "Start", 1, 1, 0.0, Ly(0), 0, 0),
            new SolarSystem(202, "Relay", 1, 1, 0.0, Ly(3), 0, 0),
            new SolarSystem(203, "End", 1, 1, 0.0, Ly(6), 0, 0),
        };
        var map = new UniverseMap(systems, Array.Empty<Stargate>());
        return (map, 201, 202, 203);
    }

    /// <summary>
    /// A scenario where only a "gate most of the way, then jump the unreachable tail"
    /// hybrid route works: a gate chain Start-Mid1-Mid2-LandingZone spaced 100 LY apart
    /// (far beyond any capital jump range, so no jump shortcuts exist between them), plus
    /// an isolated Destination system with NO gate connection, reachable only by a 2 LY
    /// jump from LandingZone.
    /// </summary>
    public static (UniverseMap Map, int StartId, int LandingZoneId, int DestinationId) BuildHybridFixture()
    {
        var systems = new[]
        {
            new SolarSystem(301, "Start", 1, 1, 0.9, 0, Ly(0), 0),
            new SolarSystem(302, "Mid1", 1, 1, 0.9, 0, Ly(100), 0),
            new SolarSystem(303, "Mid2", 1, 1, 0.9, 0, Ly(200), 0),
            new SolarSystem(304, "LandingZone", 1, 1, 0.3, 0, Ly(300), 0),
            new SolarSystem(305, "Destination", 1, 1, 0.0, Ly(2), Ly(300), 0),
        };
        var gates = new[]
        {
            new Stargate(301, 302),
            new Stargate(302, 303),
            new Stargate(303, 304),
        };
        var map = new UniverseMap(systems, gates);
        return (map, 301, 304, 305);
    }
}
