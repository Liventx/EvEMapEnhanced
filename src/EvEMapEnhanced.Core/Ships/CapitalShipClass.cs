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
}

public static class CapitalShipClassExtensions
{
    /// <summary>Human-readable Russian label for UI display.</summary>
    public static string ToRussianLabel(this CapitalShipClass shipClass) => shipClass switch
    {
        CapitalShipClass.Carrier => "Авианосец (Carrier)",
        CapitalShipClass.ForceAuxiliary => "Носитель обеспечения (FAX)",
        CapitalShipClass.CommandCarrier => "Командный авианосец",
        CapitalShipClass.Dreadnought => "Дредноут",
        CapitalShipClass.LancerDreadnought => "Дредноут-лансер",
        CapitalShipClass.BlackOps => "Black Ops",
        CapitalShipClass.Supercarrier => "Суперавианосец",
        CapitalShipClass.Titan => "Титан",
        CapitalShipClass.JumpFreighter => "Джамп-фрейтер",
        CapitalShipClass.Rorqual => "Роркуаль",
        _ => shipClass.ToString(),
    };
}
