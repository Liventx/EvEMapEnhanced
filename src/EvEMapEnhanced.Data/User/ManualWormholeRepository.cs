using EvEMapEnhanced.Core.Stats;
using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.User;

/// <summary>Persistence for user-placed wormhole markers (one active marker per solar system).</summary>
public sealed class ManualWormholeRepository
{
    public const int LifetimeHours = 24;

    private readonly string _sqlitePath;

    public ManualWormholeRepository(string sqlitePath) => _sqlitePath = sqlitePath;

    public IReadOnlyList<ManualWormholeMarker> LoadActive()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        PurgeExpired(connection);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT SolarSystemId, ExitComment, CreatedAtUtc, ExpiresAtUtc
            FROM ManualWormholes;
            """;
        using var reader = cmd.ExecuteReader();

        var list = new List<ManualWormholeMarker>();
        while (reader.Read())
        {
            list.Add(new ManualWormholeMarker(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                DateTimeOffset.Parse(reader.GetString(3))));
        }

        return list;
    }

    public ManualWormholeMarker Upsert(int solarSystemId, string? exitComment)
    {
        var now = DateTimeOffset.UtcNow;
        var marker = new ManualWormholeMarker(
            solarSystemId,
            string.IsNullOrWhiteSpace(exitComment) ? null : exitComment.Trim(),
            now,
            now.AddHours(LifetimeHours));

        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ManualWormholes (SolarSystemId, ExitComment, CreatedAtUtc, ExpiresAtUtc)
            VALUES ($sys, $comment, $created, $expires)
            ON CONFLICT(SolarSystemId) DO UPDATE SET
                ExitComment = excluded.ExitComment,
                CreatedAtUtc = excluded.CreatedAtUtc,
                ExpiresAtUtc = excluded.ExpiresAtUtc;
            """;
        cmd.Parameters.AddWithValue("$sys", marker.SolarSystemId);
        cmd.Parameters.AddWithValue("$comment", (object?)marker.ExitComment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", marker.CreatedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$expires", marker.ExpiresAtUtc.ToString("O"));
        cmd.ExecuteNonQuery();

        return marker;
    }

    public void Delete(int solarSystemId)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM ManualWormholes WHERE SolarSystemId = $sys;";
        cmd.Parameters.AddWithValue("$sys", solarSystemId);
        cmd.ExecuteNonQuery();
    }

    public int PurgeExpired()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        return PurgeExpired(connection);
    }

    private static int PurgeExpired(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM ManualWormholes WHERE ExpiresAtUtc <= $now;";
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        return cmd.ExecuteNonQuery();
    }
}
