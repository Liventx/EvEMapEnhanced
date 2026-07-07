using EvEMapEnhanced.Core.Jump;

namespace EvEMapEnhanced.Core.Auth;

/// <summary>An EVE character signed in via ESI SSO, with jump-relevant skills fetched from their skill sheet.</summary>
public sealed class AuthenticatedCharacter
{
    public long CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public PilotSkills Skills { get; set; } = new();
    public DateTime? SkillsUpdatedUtc { get; set; }

    /// <summary>Last solar system fetched from ESI's location endpoint (null before the first successful poll).</summary>
    public int? LastKnownSystemId { get; set; }
}
