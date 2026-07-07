namespace EvEMapEnhanced.Core.Jump;

/// <summary>A named, persistable pilot profile: skills plus routing preferences.</summary>
public sealed class PilotProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "Основной пилот";
    public PilotSkills Skills { get; set; } = new();

    /// <summary>Solar system IDs the router should avoid entirely.</summary>
    public HashSet<int> AvoidSystemIds { get; set; } = new();

    public bool AvoidLowSec { get; set; }
    public bool AvoidNullSec { get; set; }
    public bool AvoidRecentKillActivity { get; set; }

    /// <summary>
    /// Last known/manually reported solar system for this pilot, used to drive the map's
    /// "online" jump-range overlay: when this changes, the highlighted jump range
    /// automatically recenters on the new location.
    /// </summary>
    public int? CurrentSystemId { get; set; }
}
