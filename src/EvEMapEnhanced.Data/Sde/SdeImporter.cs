using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.Sde;

/// <summary>
/// Imports map-relevant tables (regions, constellations, solar systems, stargates) from
/// a downloaded SDE JSON Lines zip into the local SQLite cache. Only the four small
/// map-* entries are streamed from the archive; large unrelated files (mapMoons,
/// mapPlanets, ...) are never fully read.
/// </summary>
public sealed class SdeImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ImportSummary ImportFromZip(string zipPath, string sqlitePath, IReadOnlySet<string>? shipTypeNamesToResolve = null)
    {
        using var connection = SdeDatabase.OpenConnection(sqlitePath);
        SdeDatabase.ClearMapData(connection);

        using var zip = ZipFile.OpenRead(zipPath);
        using var transaction = connection.BeginTransaction();

        int regions = ImportRegions(zip, connection);
        int constellations = ImportConstellations(zip, connection);
        int systems = ImportSolarSystems(zip, connection);
        int gates = ImportStargates(zip, connection);
        int shipTypes = shipTypeNamesToResolve is { Count: > 0 }
            ? ImportShipTypes(zip, connection, shipTypeNamesToResolve)
            : 0;
        int excludedVictimTypes = ImportExcludedKillVictimTypes(zip, connection);
        int npcCapitalShipTypes = ImportNpcCapitalShipTypes(zip, connection);
        int npcStationSystems = ImportNpcStationSystems(zip, connection);

        transaction.Commit();

        SdeDatabase.SetMeta(connection, "importedAtUtc", DateTime.UtcNow.ToString("O"));
        SdeDatabase.SetMeta(connection, "sourceZip", Path.GetFileName(zipPath));
        SdeDatabase.SetMeta(connection, "npcStationCloneFlags", "1");

        return new ImportSummary(regions, constellations, systems, gates, shipTypes, excludedVictimTypes, npcCapitalShipTypes, npcStationSystems);
    }

    private static int ImportRegions(ZipArchive zip, SqliteConnection connection)
    {
        var entry = zip.GetEntry("mapRegions.jsonl") ?? throw new InvalidOperationException("mapRegions.jsonl not found in SDE archive.");
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO Regions (Id, Name, X, Y, Z) VALUES ($id, $name, $x, $y, $z);";
        var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; cmd.Parameters.Add(pId);
        var pName = cmd.CreateParameter(); pName.ParameterName = "$name"; cmd.Parameters.Add(pName);
        var pX = cmd.CreateParameter(); pX.ParameterName = "$x"; cmd.Parameters.Add(pX);
        var pY = cmd.CreateParameter(); pY.ParameterName = "$y"; cmd.Parameters.Add(pY);
        var pZ = cmd.CreateParameter(); pZ.ParameterName = "$z"; cmd.Parameters.Add(pZ);

        int count = 0;
        foreach (var line in ReadLines(entry))
        {
            var dto = JsonSerializer.Deserialize<SdeRegionDto>(line, JsonOptions);
            if (dto is null) continue;

            pId.Value = dto.Key;
            pName.Value = dto.Name?.GetValueOrDefault("en") ?? $"Region {dto.Key}";
            pX.Value = dto.Position?.X ?? 0;
            pY.Value = dto.Position?.Y ?? 0;
            pZ.Value = dto.Position?.Z ?? 0;
            cmd.ExecuteNonQuery();
            count++;
        }
        return count;
    }

    private static int ImportConstellations(ZipArchive zip, SqliteConnection connection)
    {
        var entry = zip.GetEntry("mapConstellations.jsonl") ?? throw new InvalidOperationException("mapConstellations.jsonl not found in SDE archive.");
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO Constellations (Id, Name, RegionId, X, Y, Z) VALUES ($id, $name, $region, $x, $y, $z);";
        var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; cmd.Parameters.Add(pId);
        var pName = cmd.CreateParameter(); pName.ParameterName = "$name"; cmd.Parameters.Add(pName);
        var pRegion = cmd.CreateParameter(); pRegion.ParameterName = "$region"; cmd.Parameters.Add(pRegion);
        var pX = cmd.CreateParameter(); pX.ParameterName = "$x"; cmd.Parameters.Add(pX);
        var pY = cmd.CreateParameter(); pY.ParameterName = "$y"; cmd.Parameters.Add(pY);
        var pZ = cmd.CreateParameter(); pZ.ParameterName = "$z"; cmd.Parameters.Add(pZ);

        int count = 0;
        foreach (var line in ReadLines(entry))
        {
            var dto = JsonSerializer.Deserialize<SdeConstellationDto>(line, JsonOptions);
            if (dto is null) continue;

            pId.Value = dto.Key;
            pName.Value = dto.Name?.GetValueOrDefault("en") ?? $"Constellation {dto.Key}";
            pRegion.Value = dto.RegionId;
            pX.Value = dto.Position?.X ?? 0;
            pY.Value = dto.Position?.Y ?? 0;
            pZ.Value = dto.Position?.Z ?? 0;
            cmd.ExecuteNonQuery();
            count++;
        }
        return count;
    }

    private static int ImportSolarSystems(ZipArchive zip, SqliteConnection connection)
    {
        var entry = zip.GetEntry("mapSolarSystems.jsonl") ?? throw new InvalidOperationException("mapSolarSystems.jsonl not found in SDE archive.");
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO SolarSystems (Id, Name, ConstellationId, RegionId, Security, X, Y, Z) VALUES ($id, $name, $con, $region, $sec, $x, $y, $z);";
        var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; cmd.Parameters.Add(pId);
        var pName = cmd.CreateParameter(); pName.ParameterName = "$name"; cmd.Parameters.Add(pName);
        var pCon = cmd.CreateParameter(); pCon.ParameterName = "$con"; cmd.Parameters.Add(pCon);
        var pRegion = cmd.CreateParameter(); pRegion.ParameterName = "$region"; cmd.Parameters.Add(pRegion);
        var pSec = cmd.CreateParameter(); pSec.ParameterName = "$sec"; cmd.Parameters.Add(pSec);
        var pX = cmd.CreateParameter(); pX.ParameterName = "$x"; cmd.Parameters.Add(pX);
        var pY = cmd.CreateParameter(); pY.ParameterName = "$y"; cmd.Parameters.Add(pY);
        var pZ = cmd.CreateParameter(); pZ.ParameterName = "$z"; cmd.Parameters.Add(pZ);

        int count = 0;
        foreach (var line in ReadLines(entry))
        {
            var dto = JsonSerializer.Deserialize<SdeSolarSystemDto>(line, JsonOptions);
            if (dto is null) continue;

            pId.Value = dto.Key;
            pName.Value = dto.Name?.GetValueOrDefault("en") ?? $"System {dto.Key}";
            pCon.Value = dto.ConstellationId;
            pRegion.Value = dto.RegionId;
            pSec.Value = dto.SecurityStatus;
            pX.Value = dto.Position?.X ?? 0;
            pY.Value = dto.Position?.Y ?? 0;
            pZ.Value = dto.Position?.Z ?? 0;
            cmd.ExecuteNonQuery();
            count++;
        }
        return count;
    }

    private static int ImportStargates(ZipArchive zip, SqliteConnection connection)
    {
        var entry = zip.GetEntry("mapStargates.jsonl") ?? throw new InvalidOperationException("mapStargates.jsonl not found in SDE archive.");

        var seenPairs = new HashSet<(int, int)>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO Stargates (FromSystemId, ToSystemId) VALUES ($from, $to);";
        var pFrom = cmd.CreateParameter(); pFrom.ParameterName = "$from"; cmd.Parameters.Add(pFrom);
        var pTo = cmd.CreateParameter(); pTo.ParameterName = "$to"; cmd.Parameters.Add(pTo);

        int count = 0;
        foreach (var line in ReadLines(entry))
        {
            var dto = JsonSerializer.Deserialize<SdeStargateDto>(line, JsonOptions);
            if (dto?.Destination is null) continue;

            int a = dto.SolarSystemId, b = dto.Destination.SolarSystemId;
            var key = a < b ? (a, b) : (b, a);
            if (!seenPairs.Add(key)) continue;

            pFrom.Value = key.Item1;
            pTo.Value = key.Item2;
            cmd.ExecuteNonQuery();
            count++;
        }
        return count;
    }

    /// <summary>
    /// Streams the (large, ~150MB uncompressed) types.jsonl entry looking only for the
    /// small set of ship-hull names we care about (capital hulls + "Capsule"), resolving
    /// their real, always-current type ID / group ID / mass from the official SDE rather
    /// than hardcoding numeric IDs anywhere in the codebase. A cheap substring pre-check
    /// avoids full JSON deserialization for the vast majority of unrelated lines.
    /// </summary>
    private static int ImportShipTypes(ZipArchive zip, SqliteConnection connection, IReadOnlySet<string> targetNames)
    {
        var entry = zip.GetEntry("types.jsonl") ?? throw new InvalidOperationException("types.jsonl not found in SDE archive.");
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO ShipTypes (TypeId, Name, GroupId, MassKg) VALUES ($id, $name, $group, $mass);";
        var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; cmd.Parameters.Add(pId);
        var pName = cmd.CreateParameter(); pName.ParameterName = "$name"; cmd.Parameters.Add(pName);
        var pGroup = cmd.CreateParameter(); pGroup.ParameterName = "$group"; cmd.Parameters.Add(pGroup);
        var pMass = cmd.CreateParameter(); pMass.ParameterName = "$mass"; cmd.Parameters.Add(pMass);

        var remaining = new HashSet<string>(targetNames, StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var line in ReadLines(entry))
        {
            if (remaining.Count == 0) break;
            if (!ContainsAnyTargetSubstring(line, remaining)) continue;

            var dto = JsonSerializer.Deserialize<SdeTypeDto>(line, JsonOptions);
            string? name = dto?.Name?.GetValueOrDefault("en");
            if (name is null || !remaining.Contains(name)) continue;

            pId.Value = dto!.Key;
            pName.Value = name;
            pGroup.Value = dto.GroupId;
            pMass.Value = dto.Mass;
            cmd.ExecuteNonQuery();
            remaining.Remove(name);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Capsule, shuttle and corvette hulls are excluded when counting player deaths from
    /// zKillboard (group IDs resolved from the SDE, not hardcoded type IDs).
    /// </summary>
    private static int ImportExcludedKillVictimTypes(ZipArchive zip, SqliteConnection connection)
    {
        var entry = zip.GetEntry("types.jsonl") ?? throw new InvalidOperationException("types.jsonl not found in SDE archive.");
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO ExcludedKillVictimTypes (TypeId) VALUES ($id);";
        var pId = cmd.CreateParameter();
        pId.ParameterName = "$id";
        cmd.Parameters.Add(pId);

        ReadOnlySpan<int> excludedGroups = stackalloc int[] { 29, 31, 237 };
        int count = 0;
        foreach (var line in ReadLines(entry))
        {
            if (!line.Contains("\"groupID\"", StringComparison.Ordinal)) continue;

            var dto = JsonSerializer.Deserialize<SdeTypeDto>(line, JsonOptions);
            if (dto is null || !dto.Published) continue;
            if (!excludedGroups.Contains(dto.GroupId)) continue;

            pId.Value = dto.Key;
            cmd.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    /// <summary>
    /// Dreadnought and titan hulls (player and NPC) used to detect recent NPC capital activity
    /// from zKillboard killmails.
    /// </summary>
    private static int ImportNpcCapitalShipTypes(ZipArchive zip, SqliteConnection connection)
    {
        var entry = zip.GetEntry("types.jsonl") ?? throw new InvalidOperationException("types.jsonl not found in SDE archive.");
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO NpcCapitalShipTypes (TypeId) VALUES ($id);";
        var pId = cmd.CreateParameter();
        pId.ParameterName = "$id";
        cmd.Parameters.Add(pId);

        ReadOnlySpan<int> capitalGroups = stackalloc int[] { 485, 547 };
        int count = 0;
        foreach (var line in ReadLines(entry))
        {
            if (!line.Contains("\"groupID\"", StringComparison.Ordinal)) continue;

            var dto = JsonSerializer.Deserialize<SdeTypeDto>(line, JsonOptions);
            if (dto is null || !dto.Published) continue;
            if (!capitalGroups.Contains(dto.GroupId)) continue;

            pId.Value = dto.Key;
            cmd.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    /// <summary>
    /// Records which solar systems contain at least one NPC station (from npcStations.jsonl),
    /// and which of those have no station offering cloning or jump-clone services.
    /// </summary>
    private static int ImportNpcStationSystems(ZipArchive zip, SqliteConnection connection)
    {
        var entry = zip.GetEntry("npcStations.jsonl");
        if (entry is null) return 0;

        var operationServices = LoadStationOperationServices(zip);

        using var stationCmd = connection.CreateCommand();
        stationCmd.CommandText = "INSERT OR IGNORE INTO NpcStationSystems (SystemId) VALUES ($id);";
        var stationId = stationCmd.CreateParameter();
        stationId.ParameterName = "$id";
        stationCmd.Parameters.Add(stationId);

        using var noCloneCmd = connection.CreateCommand();
        noCloneCmd.CommandText = "INSERT OR IGNORE INTO NpcStationNoCloneSystems (SystemId) VALUES ($id);";
        var noCloneId = noCloneCmd.CreateParameter();
        noCloneId.ParameterName = "$id";
        noCloneCmd.Parameters.Add(noCloneId);

        var systemsWithClone = new HashSet<int>();
        var systemsWithStations = new HashSet<int>();
        foreach (var line in ReadLines(entry))
        {
            var dto = JsonSerializer.Deserialize<SdeNpcStationDto>(line, JsonOptions);
            if (dto is null || dto.SolarSystemId == 0) continue;

            systemsWithStations.Add(dto.SolarSystemId);
            if (StationOffersCloneFacility(dto.OperationId, operationServices))
                systemsWithClone.Add(dto.SolarSystemId);
        }

        int count = 0;
        foreach (var systemId in systemsWithStations)
        {
            stationId.Value = systemId;
            stationCmd.ExecuteNonQuery();
            if (!systemsWithClone.Contains(systemId))
            {
                noCloneId.Value = systemId;
                noCloneCmd.ExecuteNonQuery();
            }
            count++;
        }
        return count;
    }

    /// <summary>SDE station service IDs for medical cloning and jump-clone facilities.</summary>
    private static readonly HashSet<int> CloneServiceIds = [10, 24];

    private static Dictionary<int, HashSet<int>> LoadStationOperationServices(ZipArchive zip)
    {
        var entry = zip.GetEntry("stationOperations.jsonl");
        if (entry is null) return new Dictionary<int, HashSet<int>>();

        var map = new Dictionary<int, HashSet<int>>();
        foreach (var line in ReadLines(entry))
        {
            var dto = JsonSerializer.Deserialize<SdeStationOperationDto>(line, JsonOptions);
            if (dto is null || dto.Key == 0 || dto.Services is not { Length: > 0 }) continue;
            map[dto.Key] = dto.Services.ToHashSet();
        }
        return map;
    }

    private static bool StationOffersCloneFacility(int operationId, IReadOnlyDictionary<int, HashSet<int>> operationServices)
    {
        if (operationId == 0 || !operationServices.TryGetValue(operationId, out var services)) return false;
        return services.Overlaps(CloneServiceIds);
    }

    private static bool ContainsAnyTargetSubstring(string line, HashSet<string> targets)
    {
        foreach (var name in targets)
        {
            if (line.Contains(name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static IEnumerable<string> ReadLines(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line)) yield return line;
        }
    }
}

public sealed record ImportSummary(
    int Regions,
    int Constellations,
    int SolarSystems,
    int Stargates,
    int ShipTypesResolved = 0,
    int ExcludedKillVictimTypes = 0,
    int NpcCapitalShipTypes = 0,
    int NpcStationSystems = 0);
