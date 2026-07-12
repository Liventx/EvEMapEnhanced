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
            SELECT SolarSystemId, ExitSystemId, ExitComment, CreatedAtUtc, ExpiresAtUtc
            FROM ManualWormholes;
            """;
        using var reader = cmd.ExecuteReader();

        var list = new List<ManualWormholeMarker>();
        while (reader.Read())
        {
            list.Add(new ManualWormholeMarker(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                DateTimeOffset.Parse(reader.GetString(4))));
        }

        return list;
    }

    public ManualWormholeMarker Upsert(int solarSystemId, int? exitSystemId, string? exitComment = null)
    {
        var now = DateTimeOffset.UtcNow;
        var marker = new ManualWormholeMarker(
            solarSystemId,
            exitSystemId,
            string.IsNullOrWhiteSpace(exitComment) ? null : exitComment.Trim(),
            now,
            now.AddHours(LifetimeHours));

        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ManualWormholes (SolarSystemId, ExitSystemId, ExitComment, CreatedAtUtc, ExpiresAtUtc)
            VALUES ($sys, $exitId, $comment, $created, $expires)
            ON CONFLICT(SolarSystemId) DO UPDATE SET
                ExitSystemId = excluded.ExitSystemId,
                ExitComment = excluded.ExitComment,
                CreatedAtUtc = excluded.CreatedAtUtc,
                ExpiresAtUtc = excluded.ExpiresAtUtc;
            """;
        cmd.Parameters.AddWithValue("$sys", marker.SolarSystemId);
        cmd.Parameters.AddWithValue("$exitId", (object?)marker.ExitSystemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$comment", (object?)marker.ExitComment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", marker.CreatedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$expires", marker.ExpiresAtUtc.ToString("O"));
        cmd.ExecuteNonQuery();

        return marker;
    }

    /// <summary>
    /// Saves a manual wormhole on <paramref name="solarSystemId"/> and, when an exit system is
    /// set, creates or updates the paired marker on the exit pointing back to the entry.
    /// Replacing or clearing the exit removes the previous pair.
    /// </summary>
    public ManualWormholeMarker UpsertWithPair(int solarSystemId, int? exitSystemId, string? exitComment = null)
    {
        var existing = TryGetActive(solarSystemId);

        if (existing?.ExitSystemId is int oldExit && oldExit != exitSystemId)
            DeletePairMate(oldExit, solarSystemId);

        var marker = Upsert(solarSystemId, exitSystemId, exitComment);

        if (exitSystemId is int exit && exit != solarSystemId)
            Upsert(exit, solarSystemId);
        else if (exitSystemId is null && existing?.ExitSystemId is int previousExit)
            DeletePairMate(previousExit, solarSystemId);

        return marker;
    }

    /// <summary>Removes a manual wormhole and its paired marker on the linked exit system, if any.</summary>
    public void DeleteWithPair(int solarSystemId)
    {
        var existing = TryGetActive(solarSystemId);
        if (existing is null) return;

        if (existing.ExitSystemId is int exitId)
            DeletePairMate(exitId, solarSystemId);
        else
            DeleteReversePairMate(solarSystemId);

        Delete(solarSystemId);
    }

    private ManualWormholeMarker? TryGetActive(int solarSystemId) =>
        LoadActive().FirstOrDefault(marker => marker.SolarSystemId == solarSystemId);

    private void DeletePairMate(int mateSystemId, int entrySystemId)
    {
        var mate = TryGetActive(mateSystemId);
        if (mate?.ExitSystemId == entrySystemId)
            Delete(mateSystemId);
    }

    private void DeleteReversePairMate(int exitSystemId)
    {
        foreach (var marker in LoadActive())
        {
            if (marker.ExitSystemId == exitSystemId)
            {
                Delete(marker.SolarSystemId);
                return;
            }
        }
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
