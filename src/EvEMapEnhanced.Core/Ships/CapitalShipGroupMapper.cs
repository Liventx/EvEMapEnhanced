namespace EvEMapEnhanced.Core.Ships;

/// <summary>
/// Maps EVE inventory group IDs (from SDE / ESI universe types) to jump-capable capital classes.
/// Used when the pilot's hull is not one of the seeded <see cref="ShipHulls"/> entries (e.g.
/// faction supercarriers) but still belongs to a jump-capable capital group.
/// </summary>
public static class CapitalShipGroupMapper
{
    private static readonly IReadOnlyDictionary<int, CapitalShipClass> ByGroupId =
        new Dictionary<int, CapitalShipClass>
        {
            [547] = CapitalShipClass.Carrier,
            [1538] = CapitalShipClass.ForceAuxiliary,
            [883] = CapitalShipClass.Rorqual,
            [902] = CapitalShipClass.JumpFreighter,
            [898] = CapitalShipClass.BlackOps,
            [485] = CapitalShipClass.Dreadnought,
            [2104] = CapitalShipClass.LancerDreadnought,
            [659] = CapitalShipClass.Supercarrier,
            [30] = CapitalShipClass.Titan,
        };

    public static bool TryMapGroupId(int groupId, out CapitalShipClass shipClass) =>
        ByGroupId.TryGetValue(groupId, out shipClass);
}
