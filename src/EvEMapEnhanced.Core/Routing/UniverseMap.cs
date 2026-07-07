using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Structures;

namespace EvEMapEnhanced.Core.Routing;

/// <summary>
/// In-memory representation of the static universe graph (systems + stargates),
/// with a simple uniform spatial grid index to answer "systems within N light years"
/// queries efficiently for capital jump routing.
/// </summary>
public sealed class UniverseMap
{
    private readonly Dictionary<int, SolarSystem> _systemsById;
    private readonly Dictionary<int, List<int>> _gateAdjacency = new();
    private readonly Dictionary<(long, long, long), List<int>> _spatialGrid = new();
    private readonly double _cellSizeMeters;
    private readonly Dictionary<int, List<(int ToSystemId, double DistanceLy, UserStructure Structure)>> _structureAdjacency = new();
    private readonly HashSet<int> _cynoJammedSystems = new();
    private readonly Dictionary<int, List<UserStructure>> _structuresBySystem = new();

    public IReadOnlyDictionary<int, SolarSystem> Systems => _systemsById;
    public IReadOnlySet<int> CynoJammedSystemIds => _cynoJammedSystems;

    public UniverseMap(IEnumerable<SolarSystem> systems, IEnumerable<Stargate> stargates, double? gridCellSizeLy = null)
    {
        _systemsById = systems.ToDictionary(s => s.Id);
        _cellSizeMeters = SpaceMath.LightYearsToMeters(gridCellSizeLy ?? 5.0);

        foreach (var gate in stargates)
        {
            AddGateEdge(gate.FromSystemId, gate.ToSystemId);
            AddGateEdge(gate.ToSystemId, gate.FromSystemId);
        }

        foreach (var system in _systemsById.Values)
        {
            var cell = CellOf(system);
            if (!_spatialGrid.TryGetValue(cell, out var list))
            {
                list = new List<int>();
                _spatialGrid[cell] = list;
            }
            list.Add(system.Id);
        }
    }

    private void AddGateEdge(int from, int to)
    {
        if (!_gateAdjacency.TryGetValue(from, out var list))
        {
            list = new List<int>();
            _gateAdjacency[from] = list;
        }
        if (!list.Contains(to)) list.Add(to);
    }

    public SolarSystem? Get(int systemId) => _systemsById.GetValueOrDefault(systemId);

    public SolarSystem? FindByName(string name) =>
        _systemsById.Values.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<int> GateNeighbors(int systemId) =>
        _gateAdjacency.TryGetValue(systemId, out var list) ? list : Array.Empty<int>();

    /// <summary>
    /// Loads user structures into the map: builds bidirectional jump-bridge edges for
    /// Ansiblex/CustomJumpBridge pairs, records cyno-jammed systems, and indexes all
    /// structures by system for inspection/UI purposes. Call after construction and
    /// whenever the user's structure list changes.
    /// </summary>
    public void LoadStructures(IEnumerable<UserStructure> structures)
    {
        _structureAdjacency.Clear();
        _cynoJammedSystems.Clear();
        _structuresBySystem.Clear();

        var list = structures.ToList();
        foreach (var structure in list)
        {
            if (!_structuresBySystem.TryGetValue(structure.SolarSystemId, out var bucket))
            {
                bucket = new List<UserStructure>();
                _structuresBySystem[structure.SolarSystemId] = bucket;
            }
            bucket.Add(structure);

            if (structure.Kind == StructureKind.CynoJammer)
            {
                _cynoJammedSystems.Add(structure.SolarSystemId);
            }
        }

        foreach (var structure in list.Where(s => s.Kind.IsJumpEdge() && s.LinkedSystemId is not null))
        {
            var from = Get(structure.SolarSystemId);
            var to = Get(structure.LinkedSystemId!.Value);
            if (from is null || to is null) continue;

            double distanceLy = from.DistanceLyTo(to);
            AddStructureEdge(structure.SolarSystemId, structure.LinkedSystemId.Value, distanceLy, structure);
            AddStructureEdge(structure.LinkedSystemId.Value, structure.SolarSystemId, distanceLy, structure);
        }
    }

    private void AddStructureEdge(int from, int to, double distanceLy, UserStructure structure)
    {
        if (!_structureAdjacency.TryGetValue(from, out var list))
        {
            list = new List<(int, double, UserStructure)>();
            _structureAdjacency[from] = list;
        }
        list.Add((to, distanceLy, structure));
    }

    public IReadOnlyList<(int ToSystemId, double DistanceLy, UserStructure Structure)> JumpBridgeNeighbors(int systemId) =>
        _structureAdjacency.TryGetValue(systemId, out var list) ? list : Array.Empty<(int, double, UserStructure)>();

    public IReadOnlyList<UserStructure> StructuresAt(int systemId) =>
        _structuresBySystem.TryGetValue(systemId, out var list) ? list : Array.Empty<UserStructure>();

    public IEnumerable<UserStructure> AllUserStructures() =>
        _structuresBySystem.Values.SelectMany(s => s);

    public bool IsCynoJammed(int systemId) => _cynoJammedSystems.Contains(systemId);

    private (long, long, long) CellOf(SolarSystem s) =>
        ((long)Math.Floor(s.X / _cellSizeMeters), (long)Math.Floor(s.Y / _cellSizeMeters), (long)Math.Floor(s.Z / _cellSizeMeters));

    /// <summary>
    /// Returns every system within <paramref name="rangeLy"/> light years of
    /// <paramref name="origin"/> (excluding the origin itself), using the spatial grid
    /// to only scan nearby cells rather than the whole universe.
    /// </summary>
    public IEnumerable<(SolarSystem System, double DistanceLy)> SystemsWithinRange(SolarSystem origin, double rangeLy)
    {
        double rangeMeters = SpaceMath.LightYearsToMeters(rangeLy);
        int cellRadius = (int)Math.Ceiling(rangeMeters / _cellSizeMeters) + 1;
        var (cx, cy, cz) = CellOf(origin);

        for (long dx = -cellRadius; dx <= cellRadius; dx++)
        for (long dy = -cellRadius; dy <= cellRadius; dy++)
        for (long dz = -cellRadius; dz <= cellRadius; dz++)
        {
            if (!_spatialGrid.TryGetValue((cx + dx, cy + dy, cz + dz), out var candidates)) continue;

            foreach (var id in candidates)
            {
                if (id == origin.Id) continue;
                var candidate = _systemsById[id];
                double distLy = SpaceMath.MetersToLightYears(SpaceMath.Distance(origin.X, origin.Y, origin.Z, candidate.X, candidate.Y, candidate.Z));
                if (distLy <= rangeLy)
                {
                    yield return (candidate, distLy);
                }
            }
        }
    }
}
