namespace EvEMapEnhanced.Core.Stats;

/// <summary>Which solar systems zKillboard activity overlays cover.</summary>
public enum ZKillboardScope
{
    /// <summary>Black Ops jump range from the anchored origin (plus the origin itself).</summary>
    JumpRange = 0,

    /// <summary>Every nullsec system on the map; all nullsec regions are queried.</summary>
    GlobalNullsec = 1,
}
