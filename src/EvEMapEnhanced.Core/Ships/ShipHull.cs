namespace EvEMapEnhanced.Core.Ships;

public enum Faction { Amarr, Caldari, Gallente, Minmatar, None }

/// <summary>
/// A specific capital hull. <see cref="TypeId"/> is resolved by matching <see cref="Name"/>
/// against the imported SDE (name is a stable, unambiguous key) rather than hardcoding
/// EVE type IDs here -- this keeps the seed registry correct even if a numeric ID were
/// misremembered, and lets <c>SdeTypeResolver</c> fill it in at load time.
/// BaseFuelPerLyIsotopes is seed data for the fuel calculator; recalibrate against a
/// current game client / SDE dogma dump before relying on it for real logistics.
/// </summary>
public sealed record ShipHull(
    string Name,
    Faction Faction,
    CapitalShipClass ShipClass,
    double MassKg,
    double BaseFuelPerLyIsotopes)
{
    public int? TypeId { get; set; }

    public JumpMechanicsProfile Mechanics => JumpMechanics.Get(ShipClass);
}

/// <summary>
/// Seed registry of well-known capital hulls, grouped by class.
/// Note: <see cref="CapitalShipClass.CommandCarrier"/> has no dedicated hulls here --
/// in EVE, "command carrier" is an operational role flown on standard Carrier/FAX hulls,
/// so the UI ship picker should offer the Carrier + ForceAuxiliary hulls when that mode
/// is selected. <see cref="CapitalShipClass.LancerDreadnought"/> is intentionally left
/// without seeded hulls (EDENCOM-restricted hulls not reliably enumerable here); its
/// jump mechanics profile is still correct and ready once hulls are added via SDE.
/// </summary>
public static class ShipHulls
{
    public static readonly IReadOnlyList<ShipHull> All = new List<ShipHull>
    {
        // Carriers
        new("Archon", Faction.Amarr, CapitalShipClass.Carrier, 1_297_000_000, 4300),
        new("Chimera", Faction.Caldari, CapitalShipClass.Carrier, 1_297_000_000, 4300),
        new("Thanatos", Faction.Gallente, CapitalShipClass.Carrier, 1_297_000_000, 4300),
        new("Nidhoggur", Faction.Minmatar, CapitalShipClass.Carrier, 1_297_000_000, 4300),

        // Force Auxiliaries
        new("Apostle", Faction.Amarr, CapitalShipClass.ForceAuxiliary, 1_400_000_000, 4600),
        new("Minokawa", Faction.Caldari, CapitalShipClass.ForceAuxiliary, 1_400_000_000, 4600),
        new("Ninazu", Faction.Gallente, CapitalShipClass.ForceAuxiliary, 1_400_000_000, 4600),
        new("Lif", Faction.Minmatar, CapitalShipClass.ForceAuxiliary, 1_400_000_000, 4600),

        // Dreadnoughts
        new("Revelation", Faction.Amarr, CapitalShipClass.Dreadnought, 1_469_000_000, 4600),
        new("Phoenix", Faction.Caldari, CapitalShipClass.Dreadnought, 1_469_000_000, 4600),
        new("Moros", Faction.Gallente, CapitalShipClass.Dreadnought, 1_469_000_000, 4600),
        new("Naglfar", Faction.Minmatar, CapitalShipClass.Dreadnought, 1_469_000_000, 4600),

        // Black Ops (battleship-mass, covert jump portal capable)
        new("Redeemer", Faction.Amarr, CapitalShipClass.BlackOps, 100_500_000, 1000),
        new("Widow", Faction.Caldari, CapitalShipClass.BlackOps, 103_500_000, 1000),
        new("Sin", Faction.Gallente, CapitalShipClass.BlackOps, 99_390_000, 1000),
        new("Panther", Faction.Minmatar, CapitalShipClass.BlackOps, 98_400_000, 1000),

        // Supercarriers
        new("Aeon", Faction.Amarr, CapitalShipClass.Supercarrier, 2_395_000_000, 11500),
        new("Wyvern", Faction.Caldari, CapitalShipClass.Supercarrier, 2_395_000_000, 11500),
        new("Nyx", Faction.Gallente, CapitalShipClass.Supercarrier, 2_395_000_000, 11500),
        new("Hel", Faction.Minmatar, CapitalShipClass.Supercarrier, 2_395_000_000, 11500),

        // Titans
        new("Avatar", Faction.Amarr, CapitalShipClass.Titan, 3_640_000_000, 17800),
        new("Leviathan", Faction.Caldari, CapitalShipClass.Titan, 3_640_000_000, 17800),
        new("Erebus", Faction.Gallente, CapitalShipClass.Titan, 3_640_000_000, 17800),
        new("Ragnarok", Faction.Minmatar, CapitalShipClass.Titan, 3_640_000_000, 17800),

        // Jump Freighters
        new("Ark", Faction.Amarr, CapitalShipClass.JumpFreighter, 1_291_000_000, 1450),
        new("Rhea", Faction.Caldari, CapitalShipClass.JumpFreighter, 1_291_000_000, 1450),
        new("Anshar", Faction.Gallente, CapitalShipClass.JumpFreighter, 1_291_000_000, 1450),
        new("Nomad", Faction.Minmatar, CapitalShipClass.JumpFreighter, 1_291_000_000, 1450),

        // Rorqual
        new("Rorqual", Faction.None, CapitalShipClass.Rorqual, 1_400_000_000, 1450),
    };

    public static IEnumerable<ShipHull> ByClass(CapitalShipClass shipClass) => All.Where(h => h.ShipClass == shipClass);

    public static ShipHull? FindByName(string name) => All.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
}
