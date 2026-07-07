namespace EvEMapEnhanced.Core.Structures;

/// <summary>
/// A user-entered structure (citadel, cyno beacon/jammer, or jump bridge link).
/// For <see cref="StructureKind.Ansiblex"/> and <see cref="StructureKind.CustomJumpBridge"/>,
/// <see cref="LinkedSystemId"/> identifies the paired system on the other end of the bridge;
/// the pair is entered as two linked rows (or one row interpreted bidirectionally by the
/// repository -- see <c>UserStructureRepository</c>).
/// </summary>
public sealed class UserStructure
{
    public int Id { get; set; }
    public int SolarSystemId { get; set; }
    public StructureKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? OwnerTag { get; set; }
    public StructureAccessLevel Access { get; set; } = StructureAccessLevel.OwnAlliance;

    /// <summary>For Ansiblex/CustomJumpBridge: the system ID on the other end of the link.</summary>
    public int? LinkedSystemId { get; set; }

    /// <summary>Optional strontium reinforcement timer info for Ansiblex, in hours.</summary>
    public double? StrontHours { get; set; }

    public string? Notes { get; set; }
}
