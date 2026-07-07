using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.User;

/// <summary>Creates/opens the local SQLite database holding pilot profiles, structures, saved routes, and notes.</summary>
public static class UserDatabase
{
    public static SqliteConnection OpenConnection(string sqlitePath)
    {
        var connection = new SqliteConnection($"Data Source={sqlitePath}");
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }
        EnsureSchema(connection);
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS PilotProfiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                JumpDriveCalibration INTEGER NOT NULL DEFAULT 0,
                JumpFuelConservation INTEGER NOT NULL DEFAULT 0,
                JumpFreighters INTEGER NOT NULL DEFAULT 0,
                CapitalShips INTEGER NOT NULL DEFAULT 0,
                BlackOps INTEGER NOT NULL DEFAULT 0,
                Economizer TEXT NOT NULL DEFAULT 'None',
                AvoidLowSec INTEGER NOT NULL DEFAULT 0,
                AvoidNullSec INTEGER NOT NULL DEFAULT 0,
                AvoidRecentKillActivity INTEGER NOT NULL DEFAULT 1,
                CurrentSystemId INTEGER NULL
            );

            CREATE TABLE IF NOT EXISTS PilotProfileAvoidSystems (
                ProfileId INTEGER NOT NULL REFERENCES PilotProfiles(Id) ON DELETE CASCADE,
                SystemId INTEGER NOT NULL,
                PRIMARY KEY (ProfileId, SystemId)
            );

            CREATE TABLE IF NOT EXISTS UserStructures (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SolarSystemId INTEGER NOT NULL,
                Kind TEXT NOT NULL,
                Name TEXT NOT NULL,
                OwnerTag TEXT NULL,
                Access TEXT NOT NULL DEFAULT 'OwnAlliance',
                LinkedSystemId INTEGER NULL,
                StrontHours REAL NULL,
                Notes TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_UserStructures_System ON UserStructures(SolarSystemId);

            CREATE TABLE IF NOT EXISTS SavedRoutes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                StepsJson TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SystemNotes (
                SolarSystemId INTEGER PRIMARY KEY,
                Text TEXT NOT NULL,
                TagsJson TEXT NOT NULL DEFAULT '[]'
            );
            """;
        cmd.ExecuteNonQuery();

        MigrateAddColumnIfMissing(connection, "PilotProfiles", "CurrentSystemId", "INTEGER NULL");
    }

    /// <summary>
    /// Lightweight forward-only migration: adds a column to an existing table if it isn't
    /// there yet. SQLite's `CREATE TABLE IF NOT EXISTS` doesn't retroactively add columns to
    /// databases created before a schema change, so new nullable columns need this.
    /// </summary>
    private static void MigrateAddColumnIfMissing(SqliteConnection connection, string table, string column, string columnDefinition)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table});";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDefinition};";
        alter.ExecuteNonQuery();
    }
}
