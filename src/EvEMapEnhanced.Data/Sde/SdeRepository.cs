using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;
using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.Sde;

/// <summary>Reads the imported SDE cache into Core domain objects.</summary>
public sealed class SdeRepository
{
    private readonly string _sqlitePath;

    public SdeRepository(string sqlitePath)
    {
        _sqlitePath = sqlitePath;
    }

    public bool HasData()
    {
        using var connection = SdeDatabase.OpenConnection(_sqlitePath);
        return SdeDatabase.HasMapData(connection);
    }

    public IReadOnlyList<Region> LoadRegions()
    {
        using var connection = SdeDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name FROM Regions;";
        using var reader = cmd.ExecuteReader();
        var list = new List<Region>();
        while (reader.Read())
        {
            list.Add(new Region(reader.GetInt32(0), reader.GetString(1)));
        }
        return list;
    }

    public IReadOnlyList<Constellation> LoadConstellations()
    {
        using var connection = SdeDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, RegionId FROM Constellations;";
        using var reader = cmd.ExecuteReader();
        var list = new List<Constellation>();
        while (reader.Read())
        {
            list.Add(new Constellation(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
        }
        return list;
    }

    public IReadOnlyList<SolarSystem> LoadSolarSystems()
    {
        using var connection = SdeDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, ConstellationId, RegionId, Security, X, Y, Z FROM SolarSystems;";
        using var reader = cmd.ExecuteReader();
        var list = new List<SolarSystem>();
        while (reader.Read())
        {
            list.Add(new SolarSystem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetDouble(7)));
        }
        return list;
    }

    public IReadOnlyList<Stargate> LoadStargates()
    {
        using var connection = SdeDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT FromSystemId, ToSystemId FROM Stargates;";
        using var reader = cmd.ExecuteReader();
        var list = new List<Stargate>();
        while (reader.Read())
        {
            list.Add(new Stargate(reader.GetInt32(0), reader.GetInt32(1)));
        }
        return list;
    }

    /// <summary>
    /// Builds a ready-to-use <see cref="UniverseMap"/> from the cached SDE data, restricted
    /// to space actually reachable by a normal pilot (see <see cref="PilotSpaceFilter"/>):
    /// no wormhole space, Abyssal Deadspace, or disconnected CCP test/dev regions.
    /// </summary>
    public UniverseMap BuildUniverseMap(int minAccessibleClusterSize = PilotSpaceFilter.DefaultMinAccessibleClusterSize)
    {
        var allSystems = LoadSolarSystems();
        var allStargates = LoadStargates();

        var accessible = PilotSpaceFilter.FilterToAccessibleSystems(allSystems, allStargates, minAccessibleClusterSize);
        var accessibleIds = accessible.Select(s => s.Id).ToHashSet();
        var accessibleGates = allStargates
            .Where(g => accessibleIds.Contains(g.FromSystemId) && accessibleIds.Contains(g.ToSystemId))
            .ToList();

        return new UniverseMap(accessible, accessibleGates);
    }

    /// <summary>Loads resolved (Name -> TypeId/GroupId/MassKg) entries from the ShipTypes table.</summary>
    public IReadOnlyDictionary<string, (int TypeId, int GroupId, double MassKg)> LoadShipTypes()
    {
        using var connection = SdeDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TypeId, Name, GroupId, MassKg FROM ShipTypes;";
        using var reader = cmd.ExecuteReader();

        var dict = new Dictionary<string, (int, int, double)>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            dict[reader.GetString(1)] = (reader.GetInt32(0), reader.GetInt32(2), reader.GetDouble(3));
        }
        return dict;
    }

    /// <summary>Loads type IDs excluded from zKillboard PvP death counts (capsule/shuttle/corvette).</summary>
    public IReadOnlySet<int> LoadExcludedKillVictimTypeIds()
    {
        using var connection = SdeDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TypeId FROM ExcludedKillVictimTypes;";
        using var reader = cmd.ExecuteReader();

        var ids = new HashSet<int>();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }

    /// <summary>Loads dreadnought and titan type IDs for zKillboard NPC capital detection.</summary>
    public IReadOnlySet<int> LoadNpcCapitalShipTypeIds()
    {
        using var connection = SdeDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TypeId FROM NpcCapitalShipTypes;";
        using var reader = cmd.ExecuteReader();

        var ids = new HashSet<int>();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }
}
