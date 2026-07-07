using EvEMapEnhanced.Core.Stats;
using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.Stats;

/// <summary>
/// SQLite-backed cache for <see cref="SystemStats"/>, so the UI can show the last-known
/// activity for a system immediately (offline-friendly) while a background refresh runs.
/// </summary>
public sealed class SystemStatsCacheRepository
{
    private readonly string _sqlitePath;

    public SystemStatsCacheRepository(string sqlitePath) => _sqlitePath = sqlitePath;

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_sqlitePath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS SystemStatsCache (
                SolarSystemId INTEGER PRIMARY KEY,
                KillsLastHour INTEGER NOT NULL,
                KillsLast24H INTEGER NOT NULL,
                CapitalKillsLast24H INTEGER NOT NULL,
                PodKillsLast24H INTEGER NOT NULL,
                IskDestroyedLast24H REAL NOT NULL,
                LastUpdatedUtc TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
        return connection;
    }

    public SystemStats? Get(int solarSystemId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT KillsLastHour, KillsLast24H, CapitalKillsLast24H, PodKillsLast24H, IskDestroyedLast24H, LastUpdatedUtc FROM SystemStatsCache WHERE SolarSystemId = $id;";
        cmd.Parameters.AddWithValue("$id", solarSystemId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new SystemStats(
            solarSystemId,
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetDouble(4),
            DateTime.Parse(reader.GetString(5)).ToUniversalTime());
    }

    public void Upsert(SystemStats stats)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SystemStatsCache
                (SolarSystemId, KillsLastHour, KillsLast24H, CapitalKillsLast24H, PodKillsLast24H, IskDestroyedLast24H, LastUpdatedUtc)
            VALUES
                ($id, $h1, $h24, $cap, $pod, $isk, $updated)
            ON CONFLICT(SolarSystemId) DO UPDATE SET
                KillsLastHour = excluded.KillsLastHour,
                KillsLast24H = excluded.KillsLast24H,
                CapitalKillsLast24H = excluded.CapitalKillsLast24H,
                PodKillsLast24H = excluded.PodKillsLast24H,
                IskDestroyedLast24H = excluded.IskDestroyedLast24H,
                LastUpdatedUtc = excluded.LastUpdatedUtc;
            """;
        cmd.Parameters.AddWithValue("$id", stats.SolarSystemId);
        cmd.Parameters.AddWithValue("$h1", stats.KillsLastHour);
        cmd.Parameters.AddWithValue("$h24", stats.KillsLast24H);
        cmd.Parameters.AddWithValue("$cap", stats.CapitalKillsLast24H);
        cmd.Parameters.AddWithValue("$pod", stats.PodKillsLast24H);
        cmd.Parameters.AddWithValue("$isk", stats.IskDestroyedLast24H);
        cmd.Parameters.AddWithValue("$updated", stats.LastUpdatedUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }
}
