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
    public bool AvoidRecentKillActivity { get; set; } = true;
}
