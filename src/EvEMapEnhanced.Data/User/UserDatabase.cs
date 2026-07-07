using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.User;

/// <summary>Creates/opens the local SQLite database holding authenticated ESI characters, structures, saved routes, and notes.</summary>
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
            CREATE TABLE IF NOT EXISTS AuthenticatedCharacters (
                CharacterId INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                RefreshTokenProtected BLOB NOT NULL,
                Scopes TEXT NOT NULL DEFAULT '',
                JumpDriveCalibration INTEGER NOT NULL DEFAULT 0,
                JumpFuelConservation INTEGER NOT NULL DEFAULT 0,
                JumpFreighters INTEGER NOT NULL DEFAULT 0,
                CapitalShips INTEGER NOT NULL DEFAULT 0,
                BlackOps INTEGER NOT NULL DEFAULT 0,
                SkillsUpdatedUtc TEXT NULL,
                LastKnownSystemId INTEGER NULL,
                AddedAtUtc TEXT NOT NULL
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

            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
