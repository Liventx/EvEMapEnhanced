namespace EvEMapEnhanced.Core.Structures;

public enum StructureKind
{
    Ansiblex,
    CynoBeacon,
    CynoJammer,
    Keepstar,
    Fortizar,
    Azbel,
    Athanor,
    Tatara,
    CustomJumpBridge,
}

public enum StructureAccessLevel
{
    OwnAlliance,
    OwnCorporation,
    Public,
    Blacklisted,
}

public static class StructureKindExtensions
{
    public static string ToRussianLabel(this StructureKind kind) => kind switch
    {
        StructureKind.Ansiblex => "Ansiblex (джамп-мост)",
        StructureKind.CynoBeacon => "Cyno-маяк",
        StructureKind.CynoJammer => "Cyno-джаммер",
        StructureKind.Keepstar => "Keepstar",
        StructureKind.Fortizar => "Fortizar",
        StructureKind.Azbel => "Azbel",
        StructureKind.Athanor => "Athanor",
        StructureKind.Tatara => "Tatara",
        StructureKind.CustomJumpBridge => "Джамп-бридж (устар.)",
        _ => kind.ToString(),
    };

    /// <summary>True for structure kinds that form a routable jump edge between two systems.</summary>
    public static bool IsJumpEdge(this StructureKind kind) => kind is StructureKind.Ansiblex or StructureKind.CustomJumpBridge;
}
