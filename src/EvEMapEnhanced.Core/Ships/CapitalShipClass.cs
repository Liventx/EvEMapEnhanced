namespace EvEMapEnhanced.Core.Ships;

/// <summary>
/// Capital hull classes relevant to jump-drive routing.
/// "CommandCarrier" is kept as a distinct, separately selectable UX mode even though
/// it shares the same range/fatigue mechanics as Carrier/ForceAuxiliary hulls, because
/// operators plan command/logistics capital movement differently from combat carriers.
/// </summary>
public enum CapitalShipClass
{
    Carrier,
    ForceAuxiliary,
    CommandCarrier,
    Dreadnought,
    LancerDreadnought,
    BlackOps,
    Supercarrier,
    Titan,
    JumpFreighter,
    Rorqual,
    /// <summary>Non-capital ships: gate-only routing, no jump-drive planning.</summary>
    Subcapital,
}

public static class CapitalShipClassExtensions
{
    public static bool IsSubcapital(this CapitalShipClass shipClass) =>
        shipClass == CapitalShipClass.Subcapital;

    /// <summary>
    /// Human-readable label for UI display. Kept in English (unlike most other UI-facing strings
    /// in this app) because these are EVE ship-class terms pilots already know by their English
    /// names, not general UI text.
    /// </summary>
    public static string ToDisplayLabel(this CapitalShipClass shipClass) => shipClass switch
    {
        CapitalShipClass.Carrier => "Carrier",
        CapitalShipClass.ForceAuxiliary => "Force Auxiliary (FAX)",
        CapitalShipClass.CommandCarrier => "Command Carrier",
        CapitalShipClass.Dreadnought => "Dreadnought",
        CapitalShipClass.LancerDreadnought => "Lancer Dreadnought",
        CapitalShipClass.BlackOps => "Black Ops",
        CapitalShipClass.Supercarrier => "Supercarrier",
        CapitalShipClass.Titan => "Titan",
        CapitalShipClass.JumpFreighter => "Jump Freighter",
        CapitalShipClass.Rorqual => "Rorqual",
        CapitalShipClass.Subcapital => "Subcapital",
        _ => shipClass.ToString(),
    };
}
