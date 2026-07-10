using EvEMapEnhanced.Core.Ships;

namespace EvEMapEnhanced.Data.Sde;

/// <summary>
/// Bridges the Core seed hull registry (<see cref="ShipHulls"/>) with real, always-current
/// type IDs/masses resolved from the imported SDE. Also exposes the resolved "Capsule"
/// type ID and a TypeId -> ShipHull lookup, used by the kill-stats classifier to detect
/// capital and pod kills without any hardcoded EVE type/group IDs.
/// </summary>
public sealed class ShipTypeCatalog
{
    public IReadOnlyDictionary<int, ShipHull> HullsByTypeId { get; }
    public int? CapsuleTypeId { get; }

    public const string CapsuleName = "Capsule";

    private ShipTypeCatalog(IReadOnlyDictionary<int, ShipHull> hullsByTypeId, int? capsuleTypeId)
    {
        HullsByTypeId = hullsByTypeId;
        CapsuleTypeId = capsuleTypeId;
    }

    public static IReadOnlySet<string> NamesToResolve() =>
        new HashSet<string>(ShipHulls.All.Select(h => h.Name), StringComparer.OrdinalIgnoreCase) { CapsuleName };

    public static ShipTypeCatalog Build(SdeRepository repository)
    {
        var resolved = repository.LoadShipTypes();
        var byTypeId = new Dictionary<int, ShipHull>();

        foreach (var hull in ShipHulls.All)
        {
            if (resolved.TryGetValue(hull.Name, out var info))
            {
                hull.TypeId = info.TypeId;
                byTypeId[info.TypeId] = hull;
            }
        }

        int? capsuleId = resolved.TryGetValue(CapsuleName, out var capsule) ? capsule.TypeId : null;
        return new ShipTypeCatalog(byTypeId, capsuleId);
    }

    public bool IsCapitalTypeId(int typeId) => HullsByTypeId.ContainsKey(typeId);
    public bool IsPodTypeId(int typeId) => CapsuleTypeId == typeId;

    /// <summary>
    /// Maps an ESI <c>ship_type_id</c> to a jump-capable capital class when the hull is in the
    /// seeded registry. Pods and non-jump hulls (frigates, etc.) return false.
    /// </summary>
    public bool TryGetCapitalShipClass(int typeId, out CapitalShipClass shipClass)
    {
        if (IsPodTypeId(typeId) || !HullsByTypeId.TryGetValue(typeId, out ShipHull? hull))
        {
            shipClass = default;
            return false;
        }

        shipClass = hull.ShipClass;
        return true;
    }
}
