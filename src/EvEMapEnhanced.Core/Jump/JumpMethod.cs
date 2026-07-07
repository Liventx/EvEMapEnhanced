namespace EvEMapEnhanced.Core.Jump;

/// <summary>The mechanism used to cover a jump-drive leg of a route.</summary>
public enum JumpMethod
{
    /// <summary>Jumping to a cynosural field or standup cyno beacon.</summary>
    Cyno,

    /// <summary>Jumping to a covert cynosural field (Black Ops jump portal).</summary>
    CovertCyno,

    /// <summary>Jumping through a player-owned jump bridge (e.g. Ansiblex).</summary>
    JumpBridge,
}

public static class JumpMethodExtensions
{
    /// <summary>Whether this method uses the "covert" fatigue multiplier for the ship's class.</summary>
    public static bool IsCovert(this JumpMethod method) => method == JumpMethod.CovertCyno;
}
