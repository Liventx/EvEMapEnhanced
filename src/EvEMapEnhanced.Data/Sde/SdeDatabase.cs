using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.Sde;

/// <summary>Creates/opens the local SQLite cache holding imported SDE map data.</summary>
public static class SdeDatabase
{
    public static SqliteConnection OpenConnection(string sqlitePath)
    {
        var connection = new SqliteConnection($"Data Source={sqlitePath}");
        connection.Open();
        EnsureSchema(connection);
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Regions (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                X REAL NOT NULL,
                Y REAL NOT NULL,
                Z REAL NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Constellations (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                RegionId INTEGER NOT NULL,
                X REAL NOT NULL,
                Y REAL NOT NULL,
                Z REAL NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SolarSystems (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                ConstellationId INTEGER NOT NULL,
                RegionId INTEGER NOT NULL,
                Security REAL NOT NULL,
                X REAL NOT NULL,
                Y REAL NOT NULL,
                Z REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SolarSystems_Name ON SolarSystems(Name);

            CREATE TABLE IF NOT EXISTS Stargates (
                FromSystemId INTEGER NOT NULL,
                ToSystemId INTEGER NOT NULL,
                PRIMARY KEY (FromSystemId, ToSystemId)
            );

            CREATE TABLE IF NOT EXISTS SdeMeta (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ShipTypes (
                TypeId INTEGER PRIMARY KEY,
                Name TEXT NOT NULL UNIQUE,
                GroupId INTEGER NOT NULL,
                MassKg REAL NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ExcludedKillVictimTypes (
                TypeId INTEGER PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS NpcCapitalShipTypes (
                TypeId INTEGER PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS NpcStationSystems (
                SystemId INTEGER PRIMARY KEY
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public static void ClearMapData(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM Regions;
            DELETE FROM Constellations;
            DELETE FROM SolarSystems;
            DELETE FROM Stargates;
            DELETE FROM ShipTypes;
            DELETE FROM ExcludedKillVictimTypes;
            DELETE FROM NpcCapitalShipTypes;
            DELETE FROM NpcStationSystems;
            """;
        cmd.ExecuteNonQuery();
    }

    public static void SetMeta(SqliteConnection connection, string key, string value)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO SdeMeta (Key, Value) VALUES ($k, $v) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public static string? GetMeta(SqliteConnection connection, string key)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM SdeMeta WHERE Key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public static bool HasMapData(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SolarSystems;";
        long count = (long)(cmd.ExecuteScalar() ?? 0L);
        return count > 0;
    }
}
